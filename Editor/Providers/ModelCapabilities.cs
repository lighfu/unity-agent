using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    // ═══════════════════════════════════════════════════════
    //  ModelCapability — モデルごとの性能定義
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 思考（推論）をリクエストに載せる方法。モデルごとに受け口が違い、取り違えると API が 400 を返す。
    ///
    /// Claude は 4.5 以前が ExtendedBudget (thinking.type=enabled + budget_tokens)、
    /// 4.6 以降が Adaptive (thinking.type=adaptive + output_config.effort) で、Opus 4.7 以降に
    /// budget_tokens を送ると拒否され、逆に 4.5 以前に adaptive を送っても拒否される
    /// (platform.claude.com/docs/en/build-with-claude/extended-thinking)。
    /// 他のプロバイダーも同じ二分で、Gemini 2.5 系の thinkingBudget が ExtendedBudget、
    /// Gemini 3 系の thinkingLevel と OpenAI 互換の reasoning_effort が Adaptive にあたる。
    /// </summary>
    internal enum ThinkingApi
    {
        /// <summary>思考の指定を受け付けない。</summary>
        None,
        /// <summary>トークン数のバジェットで指定する。</summary>
        ExtendedBudget,
        /// <summary>強さ (effort) で指定する。バジェットは渡せない。</summary>
        Adaptive,
    }

    internal sealed class ModelCapability
    {
        public string ModelId;
        public string DisplayName;
        public int InputTokenLimit;
        public int OutputTokenLimit;
        /// <summary>思考の指定方法。設定 UI と各プロバイダーの送信内容はこれを見て決める。</summary>
        public ThinkingApi ThinkingApi;
        /// <summary>思考モードに対応するか。ThinkingApi から導出する（別々に持つと必ずズレる）。</summary>
        public bool SupportsThinking => ThinkingApi != ThinkingApi.None;
        public int ThinkingBudgetMin;
        public int ThinkingBudgetMax;
        /// <summary>
        /// そのモデルが受け付ける推論の強さ。ProviderRegistry.EffortLevelLabels の添字をビットにした集合で、
        /// 0 = 分からない（プロバイダー単位の上限に退避する）。
        ///
        /// 上限ではなく集合として持つのは、Claude 4.6 世代が xhigh を飛ばして max を受け付けるため。
        /// 「どこまで上げられるか」の 1 つの数では、この飛びを表せない。
        /// </summary>
        public int EffortLevelMask;
        public bool SupportsImageInput;
        public bool SupportsSearch;
        public bool SupportsStreaming;
        public bool IsDeprecated;
        /// <summary>無料枠では使えない（公式 pricing の Free Tier が Not available）。ドロップダウンのラベルで注記する。</summary>
        public bool FreeTierUnavailable;
        /// <summary>このモデルを設定 UI のドロップダウンに表示するプロバイダー一覧。null = 表示しない（性能照会のみ）。</summary>
        public LLMProviderType[] Dropdowns;

        public ModelCapability() { }

        /// <param name="thinkingApi">
        /// 思考の指定方法。省略するとバジェットの上限の有無から導出する。
        /// Claude のように同じ世代でも受け口が分かれるモデルは必ず明示すること。
        /// </param>
        /// <param name="effortLevelMask">
        /// そのモデルが受け付ける推論の強さの集合 (ProviderRegistry.EffortLow などの OR)。
        /// 省略すると「分からない」扱いになり、プロバイダー単位の上限が使われる。
        /// </param>
        public ModelCapability(string modelId, string displayName,
            int inputTokenLimit, int outputTokenLimit,
            bool supportsThinking, int thinkingBudgetMin, int thinkingBudgetMax,
            bool supportsImageInput, bool supportsSearch = false,
            bool supportsStreaming = true, bool isDeprecated = false,
            LLMProviderType[] dropdowns = null, bool freeTierUnavailable = false,
            ThinkingApi? thinkingApi = null, int effortLevelMask = 0)
        {
            ModelId = modelId;
            DisplayName = displayName;
            InputTokenLimit = inputTokenLimit;
            OutputTokenLimit = outputTokenLimit;
            ThinkingApi = thinkingApi ?? DeriveThinkingApi(supportsThinking, thinkingBudgetMax);
            ThinkingBudgetMin = thinkingBudgetMin;
            ThinkingBudgetMax = thinkingBudgetMax;
            EffortLevelMask = effortLevelMask;
            SupportsImageInput = supportsImageInput;
            SupportsSearch = supportsSearch;
            SupportsStreaming = supportsStreaming;
            IsDeprecated = isDeprecated;
            FreeTierUnavailable = freeTierUnavailable;
            Dropdowns = dropdowns;
        }

        /// <summary>
        /// 指定が無いときの思考の指定方法。「バジェットの上限を持つならバジェット方式、持たないなら
        /// 強さ方式」という、従来 ThinkingBudgetMax の 0 / 非 0 で暗黙に表していた区別をそのまま型にした。
        /// </summary>
        static ThinkingApi DeriveThinkingApi(bool supportsThinking, int thinkingBudgetMax)
            => !supportsThinking ? ThinkingApi.None
             : thinkingBudgetMax > 0 ? ThinkingApi.ExtendedBudget
             : ThinkingApi.Adaptive;
    }

    // ═══════════════════════════════════════════════════════
    //  ModelCapabilityRegistry — 一元管理レジストリ
    // ═══════════════════════════════════════════════════════

    internal static class ModelCapabilityRegistry
    {
        // ─── Static + Dynamic data ───

        /// <summary>登録順を保持するモデル一覧。ドロップダウンの並び順 = この順序。</summary>
        static readonly List<ModelCapability> StaticModelList = BuildStaticModels();
        /// <summary>ModelId → ModelCapability の索引（StaticModelList から導出）。</summary>
        static readonly Dictionary<string, ModelCapability> StaticModels = BuildIndex(StaticModelList);
        static Dictionary<string, ModelCapability> DynamicModels;

        static Dictionary<string, ModelCapability> BuildIndex(List<ModelCapability> list)
        {
            var d = new Dictionary<string, ModelCapability>();
            foreach (var m in list) d[m.ModelId] = m;
            return d;
        }

        public static bool HasDynamicGeminiModels => DynamicModels != null && DynamicModels.Count > 0;

        // ─── Lookup ───

        /// <summary>
        /// モデル性能を取得する。優先順位: 動的 → 静的 → パターン推定 → プロバイダーデフォルト
        /// </summary>
        public static ModelCapability GetCapability(string modelId, LLMProviderType provider)
        {
            if (string.IsNullOrEmpty(modelId))
                return ProviderDefault(provider);

            // 1. Dynamic (Gemini models.list API)
            if (DynamicModels != null && DynamicModels.TryGetValue(modelId, out var dyn))
                return dyn;

            // 2. Static (built-in data)
            if (StaticModels.TryGetValue(modelId, out var stat))
                return stat;

            // 3. Pattern inference
            var inferred = InferCapability(modelId, provider);
            if (inferred != null)
                return inferred;

            // 4. Provider default
            return ProviderDefault(provider);
        }

        /// <summary>
        /// 動的データに含まれる全モデルIDを返す (設定UIのドロップダウン用)。
        /// </summary>
        public static string[] GetDynamicGeminiModelIds()
        {
            if (DynamicModels == null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var kv in DynamicModels)
                ids.Add(kv.Key);
            ids.Sort();
            return ids.ToArray();
        }

        /// <summary>
        /// 静的 + 動的に登録されている全モデルを返す（モデル機能一覧ウインドウ用）。
        /// </summary>
        public static IEnumerable<ModelCapability> GetAllModels()
        {
            foreach (var m in StaticModelList)
                yield return m;
            if (DynamicModels != null)
            {
                foreach (var kv in DynamicModels)
                {
                    if (!StaticModels.ContainsKey(kv.Key))
                        yield return kv.Value;
                }
            }
        }

        /// <summary>
        /// 指定プロバイダーの設定 UI ドロップダウンに表示するモデルを登録順で返す。
        /// ids[i] と labels[i] は添字対応。ラベルは "DisplayName  (modelId)" 形式で生成する。
        ///
        /// Gemini (Google AI) に限り、models.list で取得済みの動的モデルを静的分の後ろに足す。
        /// これがないと「モデル一覧を更新」を押しても選択肢が 1 つも増えない。
        /// Vertex AI / Gemini CLI は利用可能なモデルの集合が異なるので足さない。
        /// </summary>
        public static (string[] ids, string[] labels) GetDropdownModels(LLMProviderType provider)
        {
            var ids = new List<string>();
            var labels = new List<string>();
            foreach (var m in StaticModelList)
            {
                if (m.Dropdowns == null) continue;
                bool match = false;
                foreach (var p in m.Dropdowns)
                    if (p == provider) { match = true; break; }
                if (!match) continue;
                ids.Add(m.ModelId);
                labels.Add(DropdownLabel(m));
            }

            if (provider == LLMProviderType.Gemini && DynamicModels != null)
            {
                var extra = new List<string>();
                foreach (var kv in DynamicModels)
                {
                    // 静的登録済みは飛ばす。ids ではなく StaticModels で判定するのが要点で、
                    // ids で判定すると「あえてドロップダウンに出していない」モデルが復活する。
                    if (StaticModels.ContainsKey(kv.Key)) continue;
                    if (!LooksLikeChatModel(kv.Key)) continue;
                    extra.Add(kv.Key);
                }
                extra.Sort(StringComparer.Ordinal);
                foreach (var id in extra)
                {
                    ids.Add(id);
                    labels.Add(DropdownLabel(DynamicModels[id]));
                }
            }

            return (ids.ToArray(), labels.ToArray());
        }

        static string DropdownLabel(ModelCapability m)
        {
            string mark = m.FreeTierUnavailable ? " [課金必須]" : "";
            return $"{m.DisplayName}{mark}  ({m.ModelId})";
        }

        /// <summary>
        /// models.list は generateContent 対応というだけで TTS / 画像生成 / 埋め込み系まで返す。
        /// チャットのドロップダウンにそれらを混ぜると 50 件超の使えない選択肢で埋まるので落とす。
        /// 落としたモデルもカスタムモデル欄に直接書けば使える。
        /// </summary>
        static readonly string[] NonChatModelMarkers =
        {
            "-tts", "-image", "imagen", "veo", "embedding", "aqa", "-audio",
        };

        static bool LooksLikeChatModel(string modelId)
        {
            foreach (var marker in NonChatModelMarkers)
                if (modelId.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        /// <summary>
        /// 静的 / 動的データに登録されているモデルを返す。未登録なら null（推定フォールバックなし）。
        /// </summary>
        public static ModelCapability GetRegistered(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return null;
            if (DynamicModels != null && DynamicModels.TryGetValue(modelId, out var dyn)) return dyn;
            return StaticModels.TryGetValue(modelId, out var stat) ? stat : null;
        }

        /// <summary>
        /// Google が提供を終了したモデル ID のプレフィクス。設定に残っていても API は 404 か
        /// 最小の枠しか返さないため、名前を見た時点で警告できるようにここに持つ。
        /// </summary>
        static readonly string[] RetiredGeminiPrefixes =
        {
            "gemini-1.0", "gemini-1.5", "gemini-2.0", "gemini-pro", "gemini-3-pro-preview",
        };

        /// <summary>Gemini のチャットモデル ID がレジストリから見てどういう状態か。</summary>
        internal enum GeminiModelStatus
        {
            /// <summary>登録済み、または判定対象外（Gemini 以外のモデル名）。</summary>
            Ok,
            /// <summary>Google が提供を終了したと分かっている。</summary>
            Retired,
            /// <summary>一覧に無い。綴り違いか、レジストリより新しいモデル。</summary>
            Unlisted,
        }

        /// <summary>
        /// Gemini のチャットモデル ID を分類する。
        ///
        /// 対象を "gemini" で始まる ID に限るのは、OpenAI 互換 / Ollama / カスタムエンドポイントでは
        /// 未登録のモデル名が正常だから。Gemini だけは Google の現行モデルを列挙できるので、
        /// 一覧に無い = 廃止済みか新モデルのどちらか、と言い切れる。
        ///
        /// なお呼び出す側は「Google AI のチャット」に限ること。Vertex AI はバージョン付き ID
        /// (gemini-3.5-flash-002 など) を受け付け、画像生成は別系統のモデル ID を使うため、
        /// そちらに当てると正常な設定を誤って警告する。
        /// </summary>
        internal static GeminiModelStatus ClassifyGeminiModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return GeminiModelStatus.Ok;
            if (!modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) return GeminiModelStatus.Ok;
            if (GetRegistered(modelId) != null) return GeminiModelStatus.Ok;

            foreach (var prefix in RetiredGeminiPrefixes)
                if (modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return GeminiModelStatus.Retired;

            return GeminiModelStatus.Unlisted;
        }

        /// <summary>
        /// チャット欄のエラーに添える注意文。問題なければ null。
        /// 設定 UI 側は L10n を通す必要があるので、こちらは使わず ClassifyGeminiModel を直接見ること。
        /// </summary>
        public static string DescribeUnknownGeminiModel(string modelId)
        {
            switch (ClassifyGeminiModel(modelId))
            {
                case GeminiModelStatus.Retired:
                    return $"注意: モデル '{modelId}' は Google が提供を終了しています。" +
                           "設定画面で現行のモデル（Gemini 3.5 Flash / Gemini 3.5 Flash Lite など）に変更してください。";
                case GeminiModelStatus.Unlisted:
                    return $"注意: モデル '{modelId}' は UnityAgent のモデル一覧にありません。" +
                           "綴り違いか、提供が終了した可能性があります。設定画面の「モデル一覧を更新」で現行のモデルを取得できます。";
                default:
                    return null;
            }
        }

        // ─── Pattern inference for custom/unknown models ───

        static ModelCapability InferCapability(string modelId, LLMProviderType provider)
        {
            switch (provider)
            {
                case LLMProviderType.Gemini:
                case LLMProviderType.Vertex_AI:
                    return InferGemini(modelId);

                case LLMProviderType.Claude_API:
                    return InferClaude(modelId);

                case LLMProviderType.OpenAI:
                    return InferOpenAI(modelId);

                case LLMProviderType.DeepSeek:
                    return InferDeepSeek(modelId);

                case LLMProviderType.xAI_Grok:
                    return InferGrok(modelId);

                case LLMProviderType.Groq:
                    return InferGroq(modelId);

                case LLMProviderType.Mistral:
                    return InferMistral(modelId);

                case LLMProviderType.Perplexity:
                    return InferPerplexity(modelId);

                case LLMProviderType.Ollama:
                    return InferOllama(modelId);

                default:
                    return null;
            }
        }

        static ModelCapability InferGemini(string id)
        {
            bool thinking = id.Contains("2.5-") || id.Contains("3-") || id.Contains("3.");
            int output = thinking ? 65536 : 8192;
            int input = id.Contains("1.5-pro") ? 2097152 : 1048576;
            // Gemini 3 系 → thinkingLevel (effort) 推奨 → budgetMax=0
            bool isGemini3 = id.Contains("3-") || id.Contains("3.");
            int budgetMin = 0;
            int budgetMax = 0;
            if (!isGemini3 && thinking)
            {
                budgetMax = 24576;
                if (id.Contains("2.5-pro")) { budgetMin = 128; budgetMax = 32768; }
            }
            return new ModelCapability(id, id, input, output,
                thinking, budgetMin, budgetMax, true, supportsSearch: true);
        }

        /// <summary>
        /// budget_tokens 方式 (extended thinking) しか受け付けない Claude の綴り。
        /// 4.6 以降は adaptive のみなので、取り違えるとどちらの向きでも 400 になる。
        /// </summary>
        static readonly string[] ClaudeExtendedThinkingMarkers =
        {
            "-4-5", "-4-1", "-4-20250514", "-3-7", "-3.7", "3-5-sonnet", "3.5-sonnet",
        };

        /// <summary>思考モードを持たない Claude の綴り (Claude 3 世代の一部と Claude 2)。</summary>
        static readonly string[] ClaudeNoThinkingMarkers =
        {
            "claude-2", "claude-3-opus", "claude-3-haiku", "3-5-haiku", "3.5-haiku",
        };

        static ModelCapability InferClaude(string id)
        {
            // 未登録のモデル名。思考の指定方法を外すとリクエストごと失敗するので、4.5 以前と分かる綴りだけ
            // budget_tokens に倒し、残りは adaptive と仮定する (一覧より新しい名前は adaptive 側のため)。
            ThinkingApi api =
                ContainsAny(id, ClaudeNoThinkingMarkers) ? ThinkingApi.None :
                ContainsAny(id, ClaudeExtendedThinkingMarkers) ? ThinkingApi.ExtendedBudget :
                ThinkingApi.Adaptive;

            bool extended = api == ThinkingApi.ExtendedBudget;
            int output = api == ThinkingApi.Adaptive ? 128000 : 64000;
            // コンテキスト長は名前からは分からない。現行世代は 1M だが、大きすぎるとツールループを
            // 自前で止められずに API のエラーで初めて失敗するので、確実に通る 200K を仮に置く。
            return new ModelCapability(id, id, 200000, output,
                api != ThinkingApi.None, extended ? 1024 : 0, extended ? output - 1000 : 0, true,
                thinkingApi: api);
        }

        static bool ContainsAny(string id, string[] markers)
        {
            foreach (var marker in markers)
                if (id.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static ModelCapability InferOpenAI(string id)
        {
            // o-series or gpt-5 series → reasoning models
            bool thinking = id.StartsWith("o") && id.Length >= 2 && char.IsDigit(id[1])
                || id.Contains("gpt-5");
            int output = thinking ? 100000 : 32768;
            int input = id.Contains("gpt-4.1") ? 1048576
                : id.Contains("gpt-5.5") ? 1000000
                : id.Contains("gpt-5") ? 400000
                : 200000;
            return new ModelCapability(id, id, input, output,
                thinking, 0, 0, true);
        }

        static ModelCapability InferDeepSeek(string id)
        {
            bool thinking = id.Contains("reasoner");
            return new ModelCapability(id, id, 128000,
                thinking ? 64000 : 8192,
                thinking, 0, 0, false);
        }

        static ModelCapability InferGrok(string id)
        {
            bool thinking = id.Contains("grok-3-mini") || id.Contains("grok-4") || id.Contains("grok-code");
            bool image = id.Contains("vision") || id.Contains("grok-4");
            int input = id.Contains("grok-2") ? 32768
                : id.Contains("grok-4") || id.Contains("grok-code") ? 256000
                : 131072;
            return new ModelCapability(id, id, input, 16384,
                thinking, 0, 0, image);
        }

        static ModelCapability InferGroq(string id)
        {
            bool thinking = id.Contains("gpt-oss");
            return new ModelCapability(id, id, 131072,
                thinking ? 65536 : 32768,
                thinking, 0, 0, false);
        }

        static ModelCapability InferMistral(string id)
        {
            bool image = id.Contains("large") || id.Contains("medium") || id.Contains("pixtral");
            int input = id.Contains("large") || id.Contains("codestral") || id.Contains("devstral") ? 256000 : 128000;
            int output = id.Contains("large") || id.Contains("codestral") || id.Contains("devstral") ? 32768 : 16384;
            if (id.Contains("nemo")) output = 8192;
            return new ModelCapability(id, id, input, output,
                false, 0, 0, image);
        }

        static ModelCapability InferPerplexity(string id)
        {
            bool thinking = id.Contains("reasoning");
            int input = id.Contains("pro") && !id.Contains("reasoning") ? 200000 : 128000;
            return new ModelCapability(id, id, input, 8192,
                thinking, 0, 0, false);
        }

        static ModelCapability InferOllama(string id)
        {
            bool thinking = id.Contains("deepseek-r1");
            return new ModelCapability(id, id, 128000, 8192,
                thinking, 0, 0, false);
        }

        // ─── Provider defaults (conservative) ───

        static ModelCapability ProviderDefault(LLMProviderType provider)
        {
            switch (provider)
            {
                case LLMProviderType.Gemini:
                case LLMProviderType.Vertex_AI:
                    return new ModelCapability("", "Unknown Gemini", 1048576, 8192,
                        false, 0, 0, true);
                case LLMProviderType.Claude_API:
                    return new ModelCapability("", "Unknown Claude", 200000, 8192,
                        false, 0, 0, true);
                case LLMProviderType.OpenAI:
                    return new ModelCapability("", "Unknown OpenAI", 128000, 16384,
                        false, 0, 0, true);
                default:
                    return new ModelCapability("", "Unknown", 128000, 8192,
                        false, 0, 0, false);
            }
        }

        // ─── Gemini models.list API 動的取得 ───

        /// <summary>
        /// Gemini models.list API からモデル情報を取得し DynamicModels を更新する。
        /// EditorCoroutineUtility.StartCoroutineOwnerless() で実行する。
        /// </summary>
        /// <param name="onComplete">
        /// この呼び出しが成功したかを渡す。呼び出し側が HasDynamicGeminiModels で成否を判断すると、
        /// 前回の取得結果が残っているせいで失敗を成功と誤認する。
        /// </param>
        public static IEnumerator FetchGeminiModels(string apiKey, string apiVersion, Action<bool> onComplete)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[ModelCapabilityRegistry] API キーが設定されていません。");
                onComplete?.Invoke(false);
                yield break;
            }

            string url = $"https://generativelanguage.googleapis.com/{apiVersion}/models?key={apiKey}&pageSize=1000";
            using (HttpHelper.AllowInsecureIfNeeded(url))
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[ModelCapabilityRegistry] models.list 取得失敗: {req.error}");
                    onComplete?.Invoke(false);
                    yield break;
                }

                var models = ParseModelsListResponse(req.downloadHandler.text);
                if (models.Count == 0)
                {
                    Debug.LogWarning("[ModelCapabilityRegistry] models.list の応答からモデルを 1 件も読み取れませんでした。");
                    onComplete?.Invoke(false);
                    yield break;
                }

                DynamicModels = models;
                Debug.Log($"[ModelCapabilityRegistry] {models.Count} 個の Gemini モデルを取得しました。");
            }

            onComplete?.Invoke(true);
        }

        /// <summary>
        /// models.list API のレスポンス JSON をパースする。
        /// </summary>
        static Dictionary<string, ModelCapability> ParseModelsListResponse(string json)
        {
            var result = new Dictionary<string, ModelCapability>();

            // "models" 配列の各オブジェクトを処理
            int idx = 0;
            while (true)
            {
                // 次の model オブジェクト開始を探す
                int nameIdx = json.IndexOf("\"name\"", idx, StringComparison.Ordinal);
                if (nameIdx < 0) break;

                // オブジェクト範囲を推定 (次の "name" or 配列終端まで)
                int nextNameIdx = json.IndexOf("\"name\"", nameIdx + 6, StringComparison.Ordinal);
                string objSlice = nextNameIdx > 0
                    ? json.Substring(nameIdx, nextNameIdx - nameIdx)
                    : json.Substring(nameIdx);

                // supportedGenerationMethods に "generateContent" を含むかチェック
                if (!objSlice.Contains("generateContent"))
                {
                    idx = nameIdx + 6;
                    continue;
                }

                string name = ExtractJsonString(objSlice, "name");
                string displayName = ExtractJsonString(objSlice, "displayName");
                int inputLimit = ExtractJsonInt(objSlice, "inputTokenLimit");
                int outputLimit = ExtractJsonInt(objSlice, "outputTokenLimit");

                if (string.IsNullOrEmpty(name))
                {
                    idx = nameIdx + 6;
                    continue;
                }

                // "models/" プレフィクス除去
                string modelId = name.StartsWith("models/") ? name.Substring(7) : name;

                // thinking サポートは API には明示フィールドがないため、
                // 静的データがあればそれを優先、なければパターン推定
                bool thinking = false;
                int budgetMin = 0, budgetMax = 0;
                if (StaticModels.TryGetValue(modelId, out var existing))
                {
                    thinking = existing.SupportsThinking;
                    budgetMin = existing.ThinkingBudgetMin;
                    budgetMax = existing.ThinkingBudgetMax;
                }
                else
                {
                    var inferred = InferGemini(modelId);
                    thinking = inferred.SupportsThinking;
                    budgetMin = inferred.ThinkingBudgetMin;
                    budgetMax = inferred.ThinkingBudgetMax;
                }

                // 画像入力はモデル名パターンで推定 (Gemini は基本的に画像対応)
                bool imageInput = !modelId.Contains("text-only");

                // 検索対応は静的データがあればそれを優先、なければ Gemini は基本対応
                bool search = existing?.SupportsSearch ?? true;

                // ストリーミングは Gemini API で常に対応
                result[modelId] = new ModelCapability(modelId, displayName ?? modelId,
                    inputLimit > 0 ? inputLimit : 1048576,
                    outputLimit > 0 ? outputLimit : 8192,
                    thinking, budgetMin, budgetMax, imageInput, search, supportsStreaming: true);

                idx = nameIdx + 6;
            }

            return result;
        }

        // ─── Static model data ───

        static List<ModelCapability> BuildStaticModels()
        {
            var d = new List<ModelCapability>();

            // プロバイダー → ドロップダウン所属の略記（同一インスタンスを複数行で共有してよい）
            LLMProviderType[] gem    = { LLMProviderType.Gemini, LLMProviderType.Vertex_AI };
            LLMProviderType[] gemCli = { LLMProviderType.Gemini, LLMProviderType.Vertex_AI, LLMProviderType.Gemini_CLI };
            LLMProviderType[] claude = { LLMProviderType.Claude_API, LLMProviderType.Claude_CLI };
            LLMProviderType[] agy    = { LLMProviderType.Antigravity_CLI };
            LLMProviderType[] oa     = { LLMProviderType.OpenAI };
            LLMProviderType[] cdx    = { LLMProviderType.Codex_CLI };
            LLMProviderType[] ds     = { LLMProviderType.DeepSeek };
            LLMProviderType[] grok   = { LLMProviderType.xAI_Grok };
            LLMProviderType[] groq   = { LLMProviderType.Groq };
            LLMProviderType[] olla   = { LLMProviderType.Ollama };
            LLMProviderType[] mist   = { LLMProviderType.Mistral };
            LLMProviderType[] pplx   = { LLMProviderType.Perplexity };

            // ── Gemini ──
            // 思考バジェット範囲は公式ドキュメント準拠: ai.google.dev/gemini-api/docs/thinking
            // search=true: Google Search Grounding 対応 / dropdowns: 設定 UI のどのドロップダウンに出すか
            // gemini-2.5 系は 2026-10-16 シャットダウン予定 (移行先: gemini-3.5-flash / gemini-3.1-pro-preview / gemini-3.1-flash-lite)
            Reg(d, "gemini-2.5-flash", "Gemini 2.5 Flash",
                1048576, 65536, true, 0, 24576, true, search: true, dropdowns: gemCli);
            Reg(d, "gemini-2.5-flash-lite", "Gemini 2.5 Flash Lite",
                1048576, 65536, true, 512, 24576, true, search: true, dropdowns: gem);
            Reg(d, "gemini-2.5-pro", "Gemini 2.5 Pro",
                1048576, 65536, true, 128, 32768, true, search: true, dropdowns: gemCli);
            // gemini-1.5 系 / gemini-2.0 系 / gemini-3-pro-preview は公式に全廃止 (404) のため登録から除去済み
            // Gemini 3 系は thinkingLevel (effort) 推奨 → ThinkingBudgetMax=0 で Effort UI を表示
            Reg(d, "gemini-3.5-flash", "Gemini 3.5 Flash",
                1048576, 65536, true, 0, 0, true, search: true, dropdowns: gemCli);
            // Flash-Lite は無料枠の 1 日あたり上限が最も大きい。無料枠で使うユーザーの既定候補。
            Reg(d, "gemini-3.5-flash-lite", "Gemini 3.5 Flash Lite",
                1048576, 65536, true, 0, 0, true, search: true, dropdowns: gem);
            Reg(d, "gemini-3-flash-preview", "Gemini 3 Flash Preview",
                1048576, 65536, true, 0, 0, true, search: true);
            // 無料枠では一切使えない (公式 pricing の Free Tier が Not available)。
            // 選ぶと必ず失敗するので、ドロップダウンのラベルで分かるようにしておく。
            Reg(d, "gemini-3.1-pro-preview", "Gemini 3.1 Pro Preview",
                1048576, 65536, true, 0, 0, true, search: true, dropdowns: gem, paidOnly: true);

            // ── Antigravity CLI (agy) ──
            // ID は agy 1.1.20 の `agy models` が返す slug そのまま (2026-09-16 時点。Claude Sonnet 4.6 は上の Claude 節と共有)。
            // Gemini 系の -high / -medium / -low は推論の強さの違い。系列名だけ (gemini-3.8-flash) を渡して --effort で
            // 強さを選ぶこともでき、実機で確認した。その場合はカスタムモデルに書く。
            //
            // コンテキスト長 / 最大出力は agy 固有の値が公開されていないので、同じ実モデルの公称値を置く。
            // -high / -medium / -low は強さ違いで実モデルは同じなので、3 行とも同じ値になる。
            // ここは UnityAgent が履歴を詰める / ツールループを止める目安に使う列で、以前は 13 モデル
            // 一律 128K / 8K の仮値だった。そのせいで設定画面のコンテキストが 128K で頭打ちになっていた。
            // 思考バジェット列を 0 / 0 のままにしているのは意図的で、agy は --effort で強さを渡すため
            // バジェットのスライダーではなく Effort の UI を出す必要がある。
            //
            // Gemini 3.8 Flash は 1M コンテキスト / 64K 出力 (ai.google.dev/gemini-api/docs/latest-model)。
            // 3.7 / 3.6 Flash は個別の公称値が出ていないが、同じ Flash 系列で 3.5 Flash (上の Gemini 節) も
            // 3.8 Flash も 1,048,576 / 65,536 なので揃える。
            // Gemini 3.1 Pro は上の gemini-3.1-pro-preview と同値。claude-opus-4-6-thinking は agy 経由の
            // Claude Opus 4.6 で、第一者 API の claude-opus-4-6 は 1M / 128K だが agy 側の上限は公開されて
            // いないため、以前 agy 向けに置いた 200,000 / 128,000 のままにしている。
            // gpt-oss-120b は 131,072 / 131,072
            // (developers.openai.com/api/docs/models/gpt-oss-120b)。
            Reg(d, "gemini-3.8-flash-high",   "Gemini 3.8 Flash (High)",   1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.8-flash-medium", "Gemini 3.8 Flash (Medium)", 1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.8-flash-low",    "Gemini 3.8 Flash (Low)",    1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.7-flash-high",   "Gemini 3.7 Flash (High)",   1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.7-flash-medium", "Gemini 3.7 Flash (Medium)", 1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.7-flash-low",    "Gemini 3.7 Flash (Low)",    1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.6-flash-high",   "Gemini 3.6 Flash (High)",   1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.6-flash-medium", "Gemini 3.6 Flash (Medium)", 1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.6-flash-low",    "Gemini 3.6 Flash (Low)",    1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.1-pro-high",     "Gemini 3.1 Pro (High)",     1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gemini-3.1-pro-low",      "Gemini 3.1 Pro (Low)",      1048576, 65536, true, 0, 0, false, dropdowns: agy);
            Reg(d, "claude-opus-4-6-thinking", "Claude Opus 4.6 (Thinking)", 200000, 128000, true, 0, 0, false, dropdowns: agy);
            Reg(d, "gpt-oss-120b-medium",     "GPT-OSS 120B (Medium)",      131072, 131072, true, 0, 0, false, dropdowns: agy);

            // ── Claude ── (ドロップダウンは現行の 4 モデルのみ。旧モデルは性能照会用に登録)
            // 値は platform.claude.com/docs/en/models/overview 準拠 (2026-09-17 時点)。
            //
            // api: が要。思考の指定方法は世代で分かれていて、Adaptive のモデルに budget_tokens を送ると
            // 400 で拒否され、ExtendedBudget のモデルに adaptive を送っても 400 になる。モデル名の綴りで
            // 判定すると必ずどこかで踏むので、モデルごとに明示する
            // (platform.claude.com/docs/en/build-with-claude/extended-thinking)。
            //
            // ExtendedBudget のモデルのバジェット上限を最大出力より小さくしているのは、
            // budget_tokens < max_tokens が API の要件で、max_tokens には最大出力を送るため。
            //
            // effort: は、そのモデルが受け付ける強さ。Claude の effort は low / medium / high /
            // xhigh / max の 5 段階 (既定 high) だが、全部を受け付けるのは 4.7 以降と 5 系だけで、
            // 4.6 世代は xhigh だけを飛ばす。ExtendedBudget のモデル (Haiku 4.5 / 4.5 世代) には
            // そもそも強さを送らないので指定しない。
            Reg(d, "claude-opus-5", "Claude Opus 5",
                1000000, 128000, true, 0, 0, true, dropdowns: claude, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            Reg(d, "claude-sonnet-5", "Claude Sonnet 5",
                1000000, 128000, true, 0, 0, true, dropdowns: claude, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            // Fable 5.1 は思考が常時オンで、強さ (effort) だけで深さが変わる。入出力とも現行で最も高価な
            // ので一覧の先頭には置かない。カスタムモデルのスイッチを切ると一覧の先頭に戻る作りなので、
            // 先頭に置くと最上位のモデルが黙って既定になってしまう。
            Reg(d, "claude-fable-5-1", "Claude Fable 5.1",
                1000000, 128000, true, 0, 0, true, dropdowns: claude, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            // Haiku 4.5 だけは現行で唯一の ExtendedBudget。強さ (effort) は受け付けない。
            Reg(d, "claude-haiku-4-5-20251001", "Claude Haiku 4.5",
                200000, 64000, true, 1024, 63000, true, dropdowns: claude, api: ThinkingApi.ExtendedBudget);
            // 日付なしの別名。ドロップダウンには出さないが、カスタムモデル欄に書かれたときに
            // 名前からの推定ではなく実データで判定できるように登録しておく。
            Reg(d, "claude-haiku-4-5", "Claude Haiku 4.5",
                200000, 64000, true, 1024, 63000, true, api: ThinkingApi.ExtendedBudget);

            // ── Claude (レガシー) ── 現行の一覧からは外れたが API はまだ受け付ける
            Reg(d, "claude-fable-5", "Claude Fable 5",
                1000000, 128000, true, 0, 0, true, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            Reg(d, "claude-opus-4-8", "Claude Opus 4.8",
                1000000, 128000, true, 0, 0, true, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            Reg(d, "claude-opus-4-7", "Claude Opus 4.7",
                1000000, 128000, true, 0, 0, true, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAll);
            // 4.6 の 2 つは budget_tokens もまだ通るが公式に非推奨なので adaptive に寄せる。
            // 強さは xhigh だけを受け付けず、max は受け付ける。飛びがあるので上限の数では表せない。
            Reg(d, "claude-opus-4-6", "Claude Opus 4.6",
                1000000, 128000, true, 0, 0, true, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAllButXHigh);
            // agy も同じ ID で Claude Sonnet 4.6 を出すので、Antigravity CLI のドロップダウンには載せる。
            // agy の --effort は low / medium / high しか受け取らないが、強さの集合はプロバイダー側の
            // 集合との積を取るので (ProviderRegistry.EffortMaskFor)、ここは第一者 API の値でよい。
            Reg(d, "claude-sonnet-4-6", "Claude Sonnet 4.6",
                1000000, 128000, true, 0, 0, true, dropdowns: agy, api: ThinkingApi.Adaptive,
                effort: ProviderRegistry.EffortAllButXHigh);
            Reg(d, "claude-opus-4-5-20251101", "Claude Opus 4.5",
                200000, 64000, true, 1024, 63000, true, api: ThinkingApi.ExtendedBudget);
            Reg(d, "claude-sonnet-4-5-20250929", "Claude Sonnet 4.5",
                200000, 64000, true, 1024, 63000, true, api: ThinkingApi.ExtendedBudget);
            // claude-opus-4-1-20250805 (2026-08-05) / claude-sonnet-4-20250514 (2026-06-15) /
            // claude-opus-4-20250514 (2026-06-15) は提供が終了し、送ってもリクエストが失敗するので
            // 登録ごと削除した。deprecated として残すと「使えないのに選べる」状態になる。

            // ── Codex CLI 専用モデル ── (Codex CLI ドロップダウンの先頭グループ)
            // 一覧・コンテキスト長・最大出力は learn.chatgpt.com/docs/models と
            // developers.openai.com/api/docs/models/* に準拠 (2026-09-16 時点)。
            // モデルごとに使える推論の強さは Codex CLI 側がサーバーのモデルカタログから受け取るもので、
            // CLI に固定で入っていない。UnityAgent 側は静的な写しを持つしかない。
            //
            // gpt-5.3-codex-spark は一覧に入れていない。推論フェーズを持たない設計 (強さ非対応) で
            // コンテキスト長・最大出力も非公開のため、この表の列を埋められないため。カスタムモデル欄に
            // 書けば使えて、その場合は強さを送らない (CodexCliProvider.NoEffortModels)。
            Reg(d, "gpt-6-astra", "GPT-6 Astra",
                1050000, 128000, true, 0, 0, false, dropdowns: cdx);
            Reg(d, "gpt-5.6-sol", "GPT-5.6 Sol",
                1050000, 128000, true, 0, 0, false, dropdowns: cdx);
            Reg(d, "gpt-5.6-terra", "GPT-5.6 Terra",
                1050000, 128000, true, 0, 0, false, dropdowns: cdx);
            Reg(d, "gpt-5.6-luna", "GPT-5.6 Luna",
                1050000, 128000, true, 0, 0, false, dropdowns: cdx);
            // gpt-5.3-codex は ChatGPT サインイン経由では選べない (API キーでの利用のみ)。
            // 現行のドキュメントには残っているので一覧にも残す。強さは max を受け付けない。
            Reg(d, "gpt-5.3-codex", "GPT-5.3 Codex",
                400000, 128000, true, 0, 0, false, dropdowns: cdx);
            // gpt-5.2-codex / gpt-5.1-codex-max / gpt-5.1-codex-mini / codex-mini は
            // 現行の一覧から消えたため登録ごと削除した。

            // ── OpenAI ──
            Reg(d, "gpt-5.5", "GPT-5.5",
                1000000, 128000, true, 0, 0, true, dropdowns: oa);
            Reg(d, "gpt-5.4", "GPT-5.4",
                400000, 128000, true, 0, 0, true);
            // gpt-4.1 / gpt-4.1-mini / o4-mini / o3 は OpenAI API のモデルで、Codex CLI が配る
            // モデルの一覧には入っていない。Codex CLI のドロップダウンから外した。
            Reg(d, "gpt-4.1", "GPT-4.1",
                1048576, 32768, false, 0, 0, true, dropdowns: oa);
            Reg(d, "gpt-4.1-mini", "GPT-4.1 Mini",
                1048576, 32768, false, 0, 0, true, dropdowns: oa);
            Reg(d, "gpt-4o", "GPT-4o",
                128000, 16384, false, 0, 0, true, dropdowns: oa);
            Reg(d, "o4-mini", "o4-mini",
                200000, 100000, true, 0, 0, true, dropdowns: oa);
            Reg(d, "o3", "o3",
                200000, 100000, true, 0, 0, true, dropdowns: oa);
            // gpt-5.2 も Codex の一覧に無く、ChatGPT サインイン経由では選べない。
            // Codex CLI のドロップダウンから外し、性能照会用の登録だけ残す。
            Reg(d, "gpt-5.2", "GPT-5.2",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5", "GPT-5",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5-mini", "GPT-5 Mini",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5-nano", "GPT-5 Nano",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5.2-pro", "GPT-5.2 Pro",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5.5-pro", "GPT-5.5 Pro",
                1050000, 128000, true, 0, 0, true);

            // ── DeepSeek ──
            Reg(d, "deepseek-chat", "DeepSeek V3",
                128000, 8192, false, 0, 0, false, dropdowns: ds);
            Reg(d, "deepseek-reasoner", "DeepSeek R1",
                128000, 64000, true, 0, 0, false, dropdowns: ds);

            // ── xAI (Grok) ──
            Reg(d, "grok-4", "Grok 4",
                256000, 16384, true, 0, 0, true, dropdowns: grok);
            Reg(d, "grok-3", "Grok 3",
                131072, 16384, false, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-3-fast", "Grok 3 Fast",
                131072, 16384, false, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-3-mini", "Grok 3 Mini",
                131072, 16384, true, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-3-mini-fast", "Grok 3 Mini Fast",
                131072, 16384, true, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-code-fast-1", "Grok Code Fast 1",
                256000, 16384, true, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-2-1212", "Grok 2",
                32768, 8192, false, 0, 0, false, dropdowns: grok);
            Reg(d, "grok-2-vision", "Grok 2 Vision",
                32768, 8192, false, 0, 0, true);

            // ── Groq ──
            Reg(d, "llama-3.3-70b-versatile", "Llama 3.3 70B Versatile",
                131072, 32768, false, 0, 0, false, dropdowns: groq);
            Reg(d, "llama-3.1-8b-instant", "Llama 3.1 8B Instant",
                131072, 131072, false, 0, 0, false, dropdowns: groq);
            Reg(d, "gpt-oss-120b", "GPT-OSS 120B",
                131072, 65536, true, 0, 0, false, dropdowns: groq);
            Reg(d, "gpt-oss-20b", "GPT-OSS 20B",
                131072, 65536, true, 0, 0, false, dropdowns: groq);

            // ── Ollama ── (ローカル: ドロップダウンは目安。任意のモデル名を入力可)
            Reg(d, "llama3.3", "Llama 3.3",
                131072, 32768, false, 0, 0, false, dropdowns: olla);
            Reg(d, "llama3.2", "Llama 3.2",
                131072, 8192, false, 0, 0, false, dropdowns: olla);
            Reg(d, "llama3.1", "Llama 3.1",
                131072, 8192, false, 0, 0, false, dropdowns: olla);
            Reg(d, "gemma3:9b", "Gemma 3 9B",
                128000, 8192, false, 0, 0, true, dropdowns: olla);
            Reg(d, "qwen2.5:14b", "Qwen 2.5 14B",
                128000, 8192, false, 0, 0, false, dropdowns: olla);
            Reg(d, "phi4", "Phi-4",
                16384, 8192, false, 0, 0, false, dropdowns: olla);
            Reg(d, "mistral", "Mistral (Ollama)",
                32768, 8192, false, 0, 0, false, dropdowns: olla);
            Reg(d, "deepseek-r1:14b", "DeepSeek R1 14B",
                128000, 8192, true, 0, 0, false, dropdowns: olla);

            // ── Mistral ── (ドロップダウンは -latest 系のみ。日付固定版は性能照会用)
            Reg(d, "mistral-large-latest", "Mistral Large",
                256000, 32768, false, 0, 0, true, dropdowns: mist);
            Reg(d, "mistral-large-2512", "Mistral Large",
                256000, 32768, false, 0, 0, true);
            Reg(d, "mistral-medium-latest", "Mistral Medium",
                128000, 16384, false, 0, 0, true, dropdowns: mist);
            Reg(d, "mistral-medium-2508", "Mistral Medium",
                128000, 16384, false, 0, 0, true);
            Reg(d, "mistral-small-latest", "Mistral Small",
                128000, 16384, false, 0, 0, false, dropdowns: mist);
            Reg(d, "mistral-small-2506", "Mistral Small",
                128000, 16384, false, 0, 0, false);
            Reg(d, "codestral-latest", "Codestral",
                256000, 32768, false, 0, 0, false, dropdowns: mist);
            Reg(d, "devstral-2512", "Devstral",
                256000, 32768, false, 0, 0, false);
            Reg(d, "pixtral-large-latest", "Pixtral Large",
                128000, 4096, false, 0, 0, true, dropdowns: mist);
            Reg(d, "open-mistral-nemo", "Mistral Nemo",
                128000, 8192, false, 0, 0, false);

            // ── Perplexity ── (全モデル検索内蔵)
            Reg(d, "sonar-pro", "Sonar Pro",
                200000, 8192, false, 0, 0, false, search: true, dropdowns: pplx);
            Reg(d, "sonar", "Sonar",
                128000, 8192, false, 0, 0, false, search: true, dropdowns: pplx);
            Reg(d, "sonar-reasoning-pro", "Sonar Reasoning Pro",
                128000, 8192, true, 0, 0, false, search: true, dropdowns: pplx);
            Reg(d, "sonar-reasoning", "Sonar Reasoning",
                128000, 8192, true, 0, 0, false, search: true, dropdowns: pplx);
            Reg(d, "sonar-deep-research", "Sonar Deep Research",
                128000, 8192, true, 0, 0, false, search: true, dropdowns: pplx);

            return d;
        }

        static void Reg(List<ModelCapability> list,
            string modelId, string displayName,
            int input, int output,
            bool thinking, int budgetMin, int budgetMax,
            bool imageInput, bool search = false, bool stream = true, bool deprecated = false,
            LLMProviderType[] dropdowns = null, bool paidOnly = false, ThinkingApi? api = null,
            int effort = 0)
        {
            list.Add(new ModelCapability(modelId, displayName,
                input, output, thinking, budgetMin, budgetMax, imageInput, search, stream, deprecated, dropdowns, paidOnly, api, effort));
        }

        // ─── Simple JSON helpers (no external dependency) ───

        static string ExtractJsonString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int ki = json.IndexOf(needle, StringComparison.Ordinal);
            if (ki < 0) return null;

            int i = ki + needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;

            int start = i;
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\') i++; // skip escaped char
                i++;
            }
            return json.Substring(start, i - start);
        }

        static int ExtractJsonInt(string json, string key)
        {
            string needle = $"\"{key}\"";
            int ki = json.IndexOf(needle, StringComparison.Ordinal);
            if (ki < 0) return 0;

            int i = ki + needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;

            int start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            if (i == start) return 0;

            if (int.TryParse(json.Substring(start, i - start), out int val))
                return val;
            return 0;
        }
    }
}
