using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// HTTP 429 レスポンスの解釈結果。
    ///
    /// 429 を「responseCode == 429」だけで判定すると、待てば直る分あたりのレート超過と、
    /// 待っても当日は直らない 1 日あたりの枠切れを区別できず、後者でも無駄にリトライして
    /// ユーザーを待たせたうえで必ず失敗する。本文をパースしてその区別を付けるのが役目。
    ///
    /// 対応フォーマット:
    ///   - Google (Gemini / Vertex AI): error.details[] の QuotaFailure / RetryInfo
    ///   - OpenAI 互換 / Anthropic:     error.message（構造化された枠情報は返らない）
    ///   - Retry-After ヘッダー:        秒数と HTTP-date の両方
    /// </summary>
    internal sealed class RateLimitInfo
    {
        /// <summary>1 回の自動リトライで待ってよい上限秒数。これを超える指示が来たら待たずに打ち切る。</summary>
        public const float MaxAutoWaitSeconds = 60f;

        /// <summary>
        /// 1 リクエストの間に自動リトライで待ってよい合計秒数。
        /// サーバー指定を優先するようにした結果、`Retry-After: 60` を 5 回言われると
        /// 5 分固まる、という事故を防ぐための天井。
        /// </summary>
        public const float MaxTotalWaitSeconds = 90f;

        /// <summary>1 日あたりの枠を使い切っている。待っても当日は回復しない。</summary>
        public bool IsDailyQuota;
        /// <summary>無料枠（課金無効）の上限に当たっている。</summary>
        public bool IsFreeTier;
        /// <summary>サーバーが指定した待ち時間（RetryInfo.retryDelay または Retry-After）。</summary>
        public float? RetryAfterSeconds;
        /// <summary>例: "GenerateRequestsPerDayPerProjectPerModel-FreeTier"</summary>
        public string QuotaId;
        /// <summary>例: "generativelanguage.googleapis.com/generate_content_free_tier_requests"</summary>
        public string QuotaMetric;
        /// <summary>枠の値。例: "20"</summary>
        public string QuotaValue;
        /// <summary>error.message。人間向けの説明として使える唯一の部分。</summary>
        public string ServerMessage;
        /// <summary>error.status。例: "RESOURCE_EXHAUSTED"</summary>
        public string Status;
        /// <summary>Retry-After ヘッダーの生の値。解釈できなかった場合もログに残せるよう保持する。</summary>
        public string RetryAfterHeader;
        /// <summary>ログ専用。ユーザー向けメッセージには絶対に入れない。</summary>
        public string RawBody;

        /// <summary>
        /// Google 形式の構造化された枠情報 (QuotaFailure) が取れている。
        /// リセット時刻や課金の案内は Google 固有なので、これが false のときは言わない。
        /// </summary>
        public bool HasGoogleQuotaDetail => QuotaId != null || QuotaMetric != null;

        /// <summary>この 429 は原理的に待てば解消するか（合計待ち時間の予算は見ない）。</summary>
        public bool ShouldRetry =>
            !IsDailyQuota &&
            !(RetryAfterSeconds.HasValue && RetryAfterSeconds.Value > MaxAutoWaitSeconds);

        /// <summary>
        /// 次に待つ秒数を決める。打ち切るべきなら null。
        /// リトライ可否の判断はここ 1 箇所に集約する（プロバイダー側で条件が分岐すると必ずズレる）。
        /// </summary>
        /// <param name="fallbackDelay">サーバー指定が無い場合に使う、呼び出し側の指数バックオフ値。</param>
        /// <param name="alreadyWaited">このリクエストでこれまでに待った合計秒数。</param>
        public float? PlanWait(float fallbackDelay, float alreadyWaited)
        {
            if (!ShouldRetry) return null;
            float wait = NextDelaySeconds(fallbackDelay);
            if (alreadyWaited + wait > MaxTotalWaitSeconds) return null;
            return wait;
        }

        /// <summary>次の待ち時間。サーバー指定があればそれを優先し、無ければ呼び出し側の指数バックオフ値を使う。</summary>
        public float NextDelaySeconds(float fallback)
        {
            if (RetryAfterSeconds.HasValue)
                return Mathf.Clamp(RetryAfterSeconds.Value, 0.5f, MaxAutoWaitSeconds);
            return Mathf.Clamp(fallback, 0.5f, MaxAutoWaitSeconds);
        }

        // ─── Parse ───

        /// <summary>
        /// 429 のレスポンス本文とヘッダーを解釈する。パースに失敗しても null は返さず、
        /// 判別できなかったことを表すインスタンスを返す（呼び出し側で分岐を増やさないため）。
        /// </summary>
        public static RateLimitInfo Parse(string body, string retryAfterHeader)
        {
            var info = new RateLimitInfo
            {
                RawBody = body ?? "",
                RetryAfterHeader = string.IsNullOrEmpty(retryAfterHeader) ? null : retryAfterHeader.Trim(),
            };

            try { ParseBody(info, body); }
            catch (Exception ex)
            {
                // Debug だと DebugMode オフのユーザーには何も残らず、
                // 「なぜ無駄にリトライしたのか」が誰にも分からなくなる。Warning で出す。
                AgentLogger.Warning(LogTag.Provider,
                    $"[RateLimit] 429 の本文をパースできませんでした ({ex.Message})。" +
                    "枠の種別が判定できないため、通常のレート超過として扱います。");
            }

            // RetryInfo が無い場合のみ Retry-After ヘッダーを見る（本文の指定のほうが具体的なため）。
            if (!info.RetryAfterSeconds.HasValue)
            {
                float? fromHeader = ParseRetryAfterHeader(info.RetryAfterHeader);
                if (fromHeader.HasValue) info.RetryAfterSeconds = fromHeader;
            }

            return info;
        }

        static void ParseBody(RateLimitInfo info, string body)
        {
            if (string.IsNullOrEmpty(body)) return;

            string trimmed = body.TrimStart();
            // Google は単一オブジェクトのことも配列 ([{"error":{...}}]) のこともある。両方受ける。
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) &&
                !trimmed.StartsWith("[", StringComparison.Ordinal)) return;

            var root = JNode.Parse(trimmed);
            if (root.Type == JNode.JType.Array && root.Count > 0) root = root[0];

            var error = root["error"];
            if (error.IsNull) error = root; // 一部のプロバイダーは error でくるまない

            string msg = error["message"].AsString;
            if (!string.IsNullOrEmpty(msg)) info.ServerMessage = msg;

            string status = error["status"].AsString;
            if (!string.IsNullOrEmpty(status)) info.Status = status;

            var details = error["details"];
            if (details.Type == JNode.JType.Array && details.AsArray != null)
            {
                foreach (var detail in details.AsArray)
                {
                    string type = detail["@type"].AsString ?? "";

                    if (type.EndsWith("QuotaFailure", StringComparison.Ordinal))
                    {
                        var violations = detail["violations"];
                        if (violations.Type == JNode.JType.Array && violations.Count > 0)
                        {
                            var v = violations[0];
                            info.QuotaId = NullIfEmpty(v["quotaId"].AsString);
                            info.QuotaMetric = NullIfEmpty(v["quotaMetric"].AsString);
                            info.QuotaValue = NullIfEmpty(v["quotaValue"].AsString);
                        }
                    }
                    else if (type.EndsWith("RetryInfo", StringComparison.Ordinal))
                    {
                        float? d = ParseProtobufDuration(detail["retryDelay"].AsString);
                        if (d.HasValue) info.RetryAfterSeconds = d;
                    }
                }
            }

            info.IsDailyQuota = LooksDaily(info);
            info.IsFreeTier = Contains(info.QuotaId, "FreeTier") || Contains(info.QuotaMetric, "free_tier");
        }

        /// <summary>分あたりの枠を表す綴り。日次判定より先に除外する。</summary>
        static readonly string[] MinuteMarkers = { "PerMinute", "per_minute", "per minute", "per-minute" };

        /// <summary>1 日あたりの枠を表す綴り。プロバイダーごとに表記が違うので候補を並べる。</summary>
        static readonly string[] DayMarkers = { "PerDay", "per_day", "per day", "per-day" };

        /// <summary>
        /// 1 日あたりの枠かどうか。quotaId が最も信頼できる（例: ...PerDayPerProjectPerModel-FreeTier）。
        /// 構造化情報を返さないプロバイダー向けに error.message も見るが、そちらは綴りの揺れが大きい。
        /// </summary>
        static bool LooksDaily(RateLimitInfo info)
        {
            if (ContainsAny(info.QuotaId, MinuteMarkers) || ContainsAny(info.QuotaMetric, MinuteMarkers))
                return false;
            if (ContainsAny(info.QuotaId, DayMarkers) || ContainsAny(info.QuotaMetric, DayMarkers))
                return true;
            if (info.HasGoogleQuotaDetail)
                return false; // 構造化情報があるのに日次でないなら、message の推測に頼る必要はない
            if (ContainsAny(info.ServerMessage, MinuteMarkers))
                return false;
            return ContainsAny(info.ServerMessage, DayMarkers);
        }

        static bool ContainsAny(string haystack, string[] needles)
        {
            foreach (var n in needles)
                if (Contains(haystack, n)) return true;
            return false;
        }

        static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        /// <summary>protobuf Duration ("2s" / "2.06258963s") を秒に変換する。</summary>
        static float? ParseProtobufDuration(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string num = s.EndsWith("s", StringComparison.Ordinal) ? s.Substring(0, s.Length - 1) : s;
            if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f)
                return v;
            return null;
        }

        /// <summary>Retry-After ヘッダー。秒数と HTTP-date の両方の形式がある。</summary>
        static float? ParseRetryAfterHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return null;

            if (float.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out float secs) && secs >= 0f)
                return secs;

            if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
            {
                double diff = (when - DateTimeOffset.UtcNow).TotalSeconds;
                if (diff > 0) return (float)diff;
                return 0f;
            }

            return null;
        }

        // ─── Message building ───

        /// <summary>
        /// チャット欄に出す説明文を組み立てる。生の JSON は絶対に含めない
        /// （本文は呼び出し側が AgentLogger でログに出すこと）。
        /// </summary>
        /// <param name="extraNote">
        /// モデル名についての注意など、呼び出し側が足したい 1 行。
        /// モデル一覧を持っているのはチャット系プロバイダーだけなので、ここでは自動生成しない。
        /// </param>
        public string ToUserMessage(string providerLabel, string modelName, int attempts, string extraNote = null)
        {
            var sb = new StringBuilder();
            sb.Append($"{providerLabel} がレート制限 (429) を返しました。");
            if (!string.IsNullOrEmpty(modelName))
                sb.Append($"\nモデル: {modelName}");
            sb.Append('\n');

            if (IsDailyQuota)
            {
                sb.Append('\n');
                sb.Append(IsFreeTier ? "1 日あたりの無料枠" : "1 日あたりの上限");
                if (!string.IsNullOrEmpty(QuotaValue))
                    sb.Append($"（{QuotaValue} リクエスト）");
                sb.Append("を使い切っています。待っても当日は回復しないため、リトライは行いませんでした。");

                if (HasGoogleQuotaDetail)
                {
                    sb.Append("\nこの枠は太平洋時間の 0 時（日本時間で 16〜17 時ごろ）にリセットされます。");
                    sb.Append("\n上限はモデルごとに異なります。別のモデルに切り替えるか、");
                    sb.Append(IsFreeTier
                        ? "Google Cloud プロジェクトで課金を有効にしてください。"
                        : "枠のリセットを待ってください。");
                }
                else
                {
                    // リセットの時刻も課金の導線もプロバイダーによって違うので、断定しない。
                    sb.Append("\nリセットの時刻と上限の引き上げ方法は、利用しているサービスのプランを確認してください。");
                }
            }
            else if (RetryAfterSeconds.HasValue && RetryAfterSeconds.Value > MaxAutoWaitSeconds)
            {
                sb.Append($"\nサーバーから {RetryAfterSeconds.Value:0.#} 秒後に再試行するよう指示されました。");
                sb.Append($"自動リトライの上限 ({MaxAutoWaitSeconds:0} 秒) を超えるため打ち切りました。時間を置いてから再送してください。");
            }
            else
            {
                sb.Append($"\n{attempts} 回試行しましたが解消しませんでした。しばらく待ってから再送してください。");
            }

            if (!string.IsNullOrEmpty(ServerMessage))
                sb.Append($"\n\nサーバーからの説明: {Summarize(ServerMessage, 300)}");

            if (!string.IsNullOrEmpty(extraNote))
                sb.Append($"\n\n{extraNote}");

            sb.Append("\n\n応答の全文は Unity Console のログに出力しています。");
            return sb.ToString();
        }

        /// <summary>ログ用の詳細。こちらには生の本文を入れてよい。</summary>
        public string ToLogDetail(string providerLabel, string modelName, int attempts)
        {
            var sb = new StringBuilder();
            sb.Append($"[{providerLabel}] HTTP 429: model={modelName}, attempts={attempts}");
            sb.Append($", daily={IsDailyQuota}, freeTier={IsFreeTier}");
            if (!string.IsNullOrEmpty(QuotaId)) sb.Append($", quotaId={QuotaId}");
            if (!string.IsNullOrEmpty(QuotaValue)) sb.Append($", quotaValue={QuotaValue}");
            if (RetryAfterSeconds.HasValue) sb.Append($", retryAfter={RetryAfterSeconds.Value:0.###}s");
            if (!string.IsNullOrEmpty(RetryAfterHeader))
                sb.Append($", retryAfterHeader=\"{RetryAfterHeader}\"");
            if (!string.IsNullOrEmpty(Status)) sb.Append($", status={Status}");
            sb.Append($"\nResponse: {(string.IsNullOrEmpty(RawBody) ? "(empty)" : RawBody)}");
            return sb.ToString();
        }

        /// <summary>改行を潰して長さを切り詰める。チャット欄が本文で埋まるのを防ぐため。</summary>
        static string Summarize(string s, int maxChars)
        {
            string flat = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= maxChars ? flat : flat.Substring(0, maxChars) + "…";
        }
    }
}
