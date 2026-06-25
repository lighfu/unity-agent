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
        /// <summary>このモデルを設定 UI のドロップダウンに表示するプロバイダー一覧。null = 表示しない（性能照会のみ）。</summary>
        public LLMProviderType[] Dropdowns;

        public ModelCapability() { }

        public ModelCapability(string modelId, string displayName,
            int inputTokenLimit, int outputTokenLimit,
            bool supportsThinking, int thinkingBudgetMin, int thinkingBudgetMax,
            bool supportsImageInput, bool supportsSearch = false,
            bool supportsStreaming = true, bool isDeprecated = false,
            LLMProviderType[] dropdowns = null)
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
            Dropdowns = dropdowns;
        }
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
                labels.Add($"{m.DisplayName}  ({m.ModelId})");
            }
            return (ids.ToArray(), labels.ToArray());
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
            if (id.Contains("opus-4-7") || id.Contains("opus-4-6")) output = 128000;
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

        static List<ModelCapability> BuildStaticModels()
        {
            var d = new List<ModelCapability>();

            // プロバイダー → ドロップダウン所属の略記（同一インスタンスを複数行で共有してよい）
            LLMProviderType[] gem    = { LLMProviderType.Gemini, LLMProviderType.Vertex_AI };
            LLMProviderType[] gemCli = { LLMProviderType.Gemini, LLMProviderType.Vertex_AI, LLMProviderType.Gemini_CLI };
            LLMProviderType[] claude = { LLMProviderType.Claude_API, LLMProviderType.Claude_CLI };
            LLMProviderType[] oa     = { LLMProviderType.OpenAI };
            LLMProviderType[] oaCdx  = { LLMProviderType.OpenAI, LLMProviderType.Codex_CLI };
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
            Reg(d, "gemini-3-flash-preview", "Gemini 3 Flash Preview",
                1048576, 65536, true, 0, 0, true, search: true);
            Reg(d, "gemini-3.1-pro-preview", "Gemini 3.1 Pro Preview",
                1048576, 65536, true, 0, 0, true, search: true, dropdowns: gem);

            // ── Claude ── (ドロップダウンは最新3モデルのみ。旧モデルは性能照会用に登録)
            Reg(d, "claude-opus-4-8", "Claude Opus 4.8",
                200000, 128000, true, 1024, 128000, true, dropdowns: claude);
            Reg(d, "claude-opus-4-7", "Claude Opus 4.7",
                200000, 128000, true, 1024, 128000, true);
            Reg(d, "claude-sonnet-4-6", "Claude Sonnet 4.6",
                200000, 64000, true, 1024, 128000, true, dropdowns: claude);
            Reg(d, "claude-haiku-4-5-20251001", "Claude Haiku 4.5",
                200000, 64000, true, 1024, 128000, true, dropdowns: claude);
            Reg(d, "claude-opus-4-6", "Claude Opus 4.6",
                200000, 128000, true, 1024, 128000, true);
            Reg(d, "claude-sonnet-4-5-20250929", "Claude Sonnet 4.5",
                200000, 64000, true, 1024, 128000, true);
            Reg(d, "claude-opus-4-5-20251101", "Claude Opus 4.5",
                200000, 64000, true, 1024, 128000, true);
            // claude-opus-4-1 は 2026-08-05 廃止予定 (Anthropic 公式 Deprecated → 移行先: claude-opus-4-8)
            Reg(d, "claude-opus-4-1-20250805", "Claude Opus 4.1",
                200000, 32000, true, 1024, 128000, true, deprecated: true);
            Reg(d, "claude-sonnet-4-20250514", "Claude Sonnet 4",
                200000, 64000, true, 1024, 128000, true, deprecated: true);
            Reg(d, "claude-opus-4-20250514", "Claude Opus 4",
                200000, 32000, true, 1024, 128000, true, deprecated: true);

            // ── Codex CLI 専用モデル ── (Codex CLI ドロップダウンの先頭グループ)
            Reg(d, "gpt-5.3-codex", "GPT-5.3 Codex",
                200000, 16384, true, 0, 0, false, dropdowns: cdx);
            // gpt-5.2-codex は API で廃止予定 (2026-10-23)。Codex CLI 経由のため当面現役。
            Reg(d, "gpt-5.2-codex", "GPT-5.2 Codex",
                200000, 16384, true, 0, 0, false, dropdowns: cdx);
            Reg(d, "gpt-5.1-codex-max", "GPT-5.1 Codex Max",
                200000, 32768, true, 0, 0, false, dropdowns: cdx);
            Reg(d, "gpt-5.1-codex-mini", "GPT-5.1 Codex Mini",
                200000, 16384, true, 0, 0, false, dropdowns: cdx);
            // codex-mini: 旧 ID codex-mini-latest は 2026-02-12 廃止済み。現行 codex-mini の有効性は Codex CLI 側に依存。
            Reg(d, "codex-mini", "Codex Mini",
                200000, 16384, true, 0, 0, false, dropdowns: cdx);

            // ── OpenAI ──
            Reg(d, "gpt-5.5", "GPT-5.5",
                1000000, 128000, true, 0, 0, true, dropdowns: oa);
            Reg(d, "gpt-5.4", "GPT-5.4",
                400000, 128000, true, 0, 0, true);
            Reg(d, "gpt-4.1", "GPT-4.1",
                1048576, 32768, false, 0, 0, true, dropdowns: oaCdx);
            Reg(d, "gpt-4.1-mini", "GPT-4.1 Mini",
                1048576, 32768, false, 0, 0, true, dropdowns: oaCdx);
            Reg(d, "gpt-4o", "GPT-4o",
                128000, 16384, false, 0, 0, true, dropdowns: oa);
            Reg(d, "o4-mini", "o4-mini",
                200000, 100000, true, 0, 0, true, dropdowns: oaCdx);
            Reg(d, "o3", "o3",
                200000, 100000, true, 0, 0, true, dropdowns: oaCdx);
            Reg(d, "gpt-5.2", "GPT-5.2",
                400000, 128000, true, 0, 0, true, dropdowns: cdx);
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
            LLMProviderType[] dropdowns = null)
        {
            list.Add(new ModelCapability(modelId, displayName,
                input, output, thinking, budgetMin, budgetMax, imageInput, search, stream, deprecated, dropdowns));
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
