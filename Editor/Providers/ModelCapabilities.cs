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

    internal sealed class ModelCapability
    {
        public string ModelId;
        public string DisplayName;
        public int InputTokenLimit;
        public int OutputTokenLimit;
        public bool SupportsThinking;
        public int ThinkingBudgetMin;
        public int ThinkingBudgetMax;
        public bool SupportsImageInput;
        public bool SupportsSearch;
        public bool SupportsStreaming;
        public bool IsDeprecated;

        public ModelCapability() { }

        public ModelCapability(string modelId, string displayName,
            int inputTokenLimit, int outputTokenLimit,
            bool supportsThinking, int thinkingBudgetMin, int thinkingBudgetMax,
            bool supportsImageInput, bool supportsSearch = false,
            bool supportsStreaming = true, bool isDeprecated = false)
        {
            ModelId = modelId;
            DisplayName = displayName;
            InputTokenLimit = inputTokenLimit;
            OutputTokenLimit = outputTokenLimit;
            SupportsThinking = supportsThinking;
            ThinkingBudgetMin = thinkingBudgetMin;
            ThinkingBudgetMax = thinkingBudgetMax;
            SupportsImageInput = supportsImageInput;
            SupportsSearch = supportsSearch;
            SupportsStreaming = supportsStreaming;
            IsDeprecated = isDeprecated;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  ModelCapabilityRegistry — 一元管理レジストリ
    // ═══════════════════════════════════════════════════════

    internal static class ModelCapabilityRegistry
    {
        // ─── Static + Dynamic data ───

        static readonly Dictionary<string, ModelCapability> StaticModels = BuildStaticModels();
        static Dictionary<string, ModelCapability> DynamicModels;

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
            foreach (var kv in StaticModels)
                yield return kv.Value;
            if (DynamicModels != null)
            {
                foreach (var kv in DynamicModels)
                {
                    if (!StaticModels.ContainsKey(kv.Key))
                        yield return kv.Value;
                }
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

        static ModelCapability InferClaude(string id)
        {
            bool thinking = id.Contains("opus-4") || id.Contains("sonnet-4") || id.Contains("haiku-4")
                || id.Contains("3-5-sonnet") || id.Contains("3.5-sonnet")
                || id.Contains("3-7") || id.Contains("3.7");
            int output = 64000;
            if (id.Contains("opus-4-6")) output = 128000;
            return new ModelCapability(id, id, 200000, output,
                thinking, thinking ? 1024 : 0, thinking ? 128000 : 0, true);
        }

        static ModelCapability InferOpenAI(string id)
        {
            // o-series or gpt-5 series → reasoning models
            bool thinking = id.StartsWith("o") && id.Length >= 2 && char.IsDigit(id[1])
                || id.Contains("gpt-5");
            int output = thinking ? 100000 : 32768;
            int input = id.Contains("gpt-4.1") ? 1048576
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
        public static IEnumerator FetchGeminiModels(string apiKey, string apiVersion, Action onComplete)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[ModelCapabilityRegistry] API キーが設定されていません。");
                onComplete?.Invoke();
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
                    onComplete?.Invoke();
                    yield break;
                }

                var models = ParseModelsListResponse(req.downloadHandler.text);
                if (models.Count > 0)
                {
                    DynamicModels = models;
                    Debug.Log($"[ModelCapabilityRegistry] {models.Count} 個の Gemini モデルを取得しました。");
                }
            }

            onComplete?.Invoke();
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

        static Dictionary<string, ModelCapability> BuildStaticModels()
        {
            var d = new Dictionary<string, ModelCapability>();

            // ── Gemini ──
            // 思考バジェット範囲は公式ドキュメント準拠: ai.google.dev/gemini-api/docs/thinking
            // search=true: Google Search Grounding 対応
            Reg(d, "gemini-2.5-flash", "Gemini 2.5 Flash",
                1048576, 65536, true, 0, 24576, true, search: true);
            Reg(d, "gemini-2.5-flash-lite", "Gemini 2.5 Flash Lite",
                1048576, 65536, true, 512, 24576, true, search: true);
            Reg(d, "gemini-2.5-pro", "Gemini 2.5 Pro",
                1048576, 65536, true, 128, 32768, true, search: true);
            Reg(d, "gemini-2.0-flash", "Gemini 2.0 Flash",
                1048576, 8192, false, 0, 0, true, search: true, deprecated: true);
            Reg(d, "gemini-2.0-flash-lite", "Gemini 2.0 Flash Lite",
                1048576, 8192, false, 0, 0, true, deprecated: true);
            Reg(d, "gemini-1.5-flash", "Gemini 1.5 Flash",
                1048576, 8192, false, 0, 0, true, search: true);
            Reg(d, "gemini-1.5-pro", "Gemini 1.5 Pro",
                2097152, 8192, false, 0, 0, true, search: true);
            // Gemini 3 系は thinkingLevel (effort) 推奨 → ThinkingBudgetMax=0 で Effort UI を表示
            Reg(d, "gemini-3-flash-preview", "Gemini 3 Flash Preview",
                1048576, 65536, true, 0, 0, true, search: true);
            Reg(d, "gemini-3-pro-preview", "Gemini 3 Pro Preview",
                1048576, 65536, true, 0, 0, true, search: true, deprecated: true);
            Reg(d, "gemini-3.1-pro-preview", "Gemini 3.1 Pro Preview",
                1048576, 65536, true, 0, 0, true, search: true);

            // ── Claude ──
            Reg(d, "claude-opus-4-6", "Claude Opus 4.6",
                200000, 128000, true, 1024, 128000, true);
            Reg(d, "claude-sonnet-4-6", "Claude Sonnet 4.6",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-haiku-4-5-20251001", "Claude Haiku 4.5",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-sonnet-4-5-20250929", "Claude Sonnet 4.5",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-opus-4-5-20251101", "Claude Opus 4.5",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-opus-4-1-20250805", "Claude Opus 4.1",
                200000, 32000, true, 1024, 128000, true);
            Reg(d, "claude-sonnet-4-20250514", "Claude Sonnet 4",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-opus-4-20250514", "Claude Opus 4",
                200000, 32000, true, 1024, 128000, true);

            // ── OpenAI ──
            Reg(d, "gpt-4.1", "GPT-4.1",
                1048576, 32768, false, 0, 0, true);
            Reg(d, "gpt-4.1-mini", "GPT-4.1 Mini",
                1048576, 32768, false, 0, 0, true);
            Reg(d, "gpt-4o", "GPT-4o",
                128000, 16384, false, 0, 0, true);
            Reg(d, "o3", "o3",
                200000, 100000, true, 0, 0, true);
            Reg(d, "o4-mini", "o4-mini",
                200000, 100000, true, 0, 0, true);
            Reg(d, "gpt-5", "GPT-5",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5-mini", "GPT-5 Mini",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5-nano", "GPT-5 Nano",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5.2", "GPT-5.2",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5.2-pro", "GPT-5.2 Pro",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-5.4", "GPT-5.4",
                400000, 128000, true, 0, 0, true);

            // ── Codex CLI ──
            Reg(d, "gpt-5.3-codex", "GPT-5.3 Codex",
                200000, 16384, true, 0, 0, false);
            Reg(d, "gpt-5.2-codex", "GPT-5.2 Codex",
                200000, 16384, true, 0, 0, false);
            Reg(d, "gpt-5.1-codex-max", "GPT-5.1 Codex Max",
                200000, 32768, true, 0, 0, false);
            Reg(d, "gpt-5.1-codex-mini", "GPT-5.1 Codex Mini",
                200000, 16384, true, 0, 0, false);
            Reg(d, "codex-mini", "Codex Mini",
                200000, 16384, true, 0, 0, false);

            // ── DeepSeek ──
            Reg(d, "deepseek-chat", "DeepSeek V3",
                128000, 8192, false, 0, 0, false);
            Reg(d, "deepseek-reasoner", "DeepSeek R1",
                128000, 64000, true, 0, 0, false);

            // ── xAI (Grok) ──
            Reg(d, "grok-4", "Grok 4",
                256000, 16384, true, 0, 0, true);
            Reg(d, "grok-3", "Grok 3",
                131072, 16384, false, 0, 0, false);
            Reg(d, "grok-3-mini", "Grok 3 Mini",
                131072, 16384, true, 0, 0, false);
            Reg(d, "grok-3-fast", "Grok 3 Fast",
                131072, 16384, false, 0, 0, false);
            Reg(d, "grok-3-mini-fast", "Grok 3 Mini Fast",
                131072, 16384, true, 0, 0, false);
            Reg(d, "grok-2", "Grok 2",
                32768, 8192, false, 0, 0, false);
            Reg(d, "grok-2-vision", "Grok 2 Vision",
                32768, 8192, false, 0, 0, true);
            Reg(d, "grok-code-fast-1", "Grok Code Fast 1",
                256000, 16384, true, 0, 0, false);

            // ── Groq ──
            Reg(d, "llama-3.3-70b-versatile", "Llama 3.3 70B Versatile",
                131072, 32768, false, 0, 0, false);
            Reg(d, "llama-3.1-8b-instant", "Llama 3.1 8B Instant",
                131072, 131072, false, 0, 0, false);
            Reg(d, "gpt-oss-120b", "GPT-OSS 120B",
                131072, 65536, true, 0, 0, false);
            Reg(d, "gpt-oss-20b", "GPT-OSS 20B",
                131072, 65536, true, 0, 0, false);

            // ── Ollama ──
            Reg(d, "llama3.3", "Llama 3.3",
                131072, 32768, false, 0, 0, false);
            Reg(d, "qwen2.5:32b", "Qwen 2.5 32B",
                128000, 8192, false, 0, 0, false);
            Reg(d, "deepseek-r1:32b", "DeepSeek R1 32B",
                128000, 8192, true, 0, 0, false);
            Reg(d, "gemma2:27b", "Gemma 2 27B",
                8192, 8192, false, 0, 0, false);
            Reg(d, "phi4", "Phi-4",
                16384, 8192, false, 0, 0, false);

            // ── Mistral ──
            Reg(d, "mistral-large-latest", "Mistral Large",
                256000, 32768, false, 0, 0, true);
            Reg(d, "mistral-large-2512", "Mistral Large",
                256000, 32768, false, 0, 0, true);
            Reg(d, "mistral-medium-latest", "Mistral Medium",
                128000, 16384, false, 0, 0, true);
            Reg(d, "mistral-medium-2508", "Mistral Medium",
                128000, 16384, false, 0, 0, true);
            Reg(d, "mistral-small-latest", "Mistral Small",
                128000, 16384, false, 0, 0, false);
            Reg(d, "mistral-small-2506", "Mistral Small",
                128000, 16384, false, 0, 0, false);
            Reg(d, "codestral-latest", "Codestral",
                256000, 32768, false, 0, 0, false);
            Reg(d, "devstral-2512", "Devstral",
                256000, 32768, false, 0, 0, false);
            Reg(d, "pixtral-large-latest", "Pixtral Large",
                128000, 4096, false, 0, 0, true);
            Reg(d, "open-mistral-nemo", "Mistral Nemo",
                128000, 8192, false, 0, 0, false);

            // ── Perplexity ── (全モデル検索内蔵)
            Reg(d, "sonar-pro", "Sonar Pro",
                200000, 8192, false, 0, 0, false, search: true);
            Reg(d, "sonar", "Sonar",
                128000, 8192, false, 0, 0, false, search: true);
            Reg(d, "sonar-reasoning", "Sonar Reasoning",
                128000, 8192, true, 0, 0, false, search: true);

            return d;
        }

        static void Reg(Dictionary<string, ModelCapability> d,
            string modelId, string displayName,
            int input, int output,
            bool thinking, int budgetMin, int budgetMax,
            bool imageInput, bool search = false, bool stream = true, bool deprecated = false)
        {
            d[modelId] = new ModelCapability(modelId, displayName,
                input, output, thinking, budgetMin, budgetMax, imageInput, search, stream, deprecated);
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
