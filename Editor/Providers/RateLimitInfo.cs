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
        /// <summary>自動リトライで待ってよい上限秒数。これを超える指示が来たら待たずに打ち切る。</summary>
        public const float MaxAutoWaitSeconds = 60f;

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
        /// <summary>ログ専用。ユーザー向けメッセージには絶対に入れない。</summary>
        public string RawBody;

        /// <summary>この 429 は待てば解消する見込みがあるか。</summary>
        public bool ShouldRetry =>
            !IsDailyQuota &&
            !(RetryAfterSeconds.HasValue && RetryAfterSeconds.Value > MaxAutoWaitSeconds);

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
            var info = new RateLimitInfo { RawBody = body ?? "" };

            try { ParseBody(info, body); }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Provider,
                    $"[RateLimit] 429 の本文をパースできませんでした ({ex.Message})。本文なしとして扱います。");
            }

            // RetryInfo が無い場合のみ Retry-After ヘッダーを見る（本文の指定のほうが具体的なため）。
            if (!info.RetryAfterSeconds.HasValue)
            {
                float? fromHeader = ParseRetryAfterHeader(retryAfterHeader);
                if (fromHeader.HasValue) info.RetryAfterSeconds = fromHeader;
            }

            return info;
        }

        static void ParseBody(RateLimitInfo info, string body)
        {
            if (string.IsNullOrEmpty(body)) return;

            string trimmed = body.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return;

            var root = JNode.Parse(trimmed);
            // Google は配列で返すことがある: [{"error":{...}}]
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

        /// <summary>
        /// 1 日あたりの枠かどうか。quotaId が最も信頼できる（例: ...PerDayPerProjectPerModel-FreeTier）。
        /// 分あたりの枠（PerMinute）と明示的に取り違えないよう、そちらは先に除外する。
        /// </summary>
        static bool LooksDaily(RateLimitInfo info)
        {
            if (Contains(info.QuotaId, "PerMinute") || Contains(info.QuotaMetric, "per_minute"))
                return false;
            if (Contains(info.QuotaId, "PerDay") || Contains(info.QuotaMetric, "per_day"))
                return true;
            // 構造化情報が無いプロバイダー向けの最後の手掛かり。
            return Contains(info.ServerMessage, "per day");
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
            header = header.Trim();

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
        public string ToUserMessage(string providerLabel, string modelName, int attempts)
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
                sb.Append("を使い切っています。この枠は太平洋時間の 0 時（日本時間で 16〜17 時ごろ）にリセットされるため、");
                sb.Append("待っても当日は回復しません。リトライは行いませんでした。");
                sb.Append("\n上限はモデルごとに異なります。別のモデルに切り替えるか、");
                sb.Append(IsFreeTier
                    ? "Google Cloud プロジェクトで課金を有効にしてください。"
                    : "しばらく時間を置いてから再送してください。");
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

            string modelNote = ModelCapabilityRegistry.DescribeUnknownGeminiModel(modelName);
            if (modelNote != null)
                sb.Append($"\n\n{modelNote}");

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
