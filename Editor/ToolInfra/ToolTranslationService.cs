using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;

namespace AjisaiFlow.UnityAgent.Editor
{
    internal static class ToolTranslationService
    {
        private const int BatchSize = 30;
        private const string CacheDir = "Library/UnityAgent/ToolTranslations";
        private static string AssetDir => PackagePaths.LocalizationDir("tools");

        // langCode → { toolName → translatedDescription }
        private static readonly Dictionary<string, Dictionary<string, string>> _memoryCache
            = new Dictionary<string, Dictionary<string, string>>();

        // ─── 公開API ───

        /// <summary>
        /// 翻訳済み説明を取得。キャッシュにあれば返却、なければ英語フォールバック。
        /// en の場合はそのまま英語を返す。それ以外は全てAI翻訳対象。
        /// </summary>
        public static string Get(string toolName, string englishDesc, string langCode)
        {
            if (langCode == "en")
                return englishDesc;

            var dict = GetOrLoadCache(langCode);
            if (dict.TryGetValue(toolName, out string cached))
                return cached;

            return englishDesc;
        }

        /// <summary>
        /// 翻訳済み件数 / 全件数。
        /// </summary>
        public static (int translated, int total) GetProgress(string langCode, int totalTools)
        {
            if (langCode == "en")
                return (totalTools, totalTools);

            var dict = GetOrLoadCache(langCode);
            return (dict.Count, totalTools);
        }

        /// <summary>
        /// 翻訳にかかる推定トークン数とコストを計算。
        /// </summary>
        public static TranslationEstimate EstimateCost(List<(string name, string description)> tools, string langCode)
        {
            var dict = GetOrLoadCache(langCode);
            var untranslated = tools.Where(t => !dict.ContainsKey(t.name)).ToList();

            long totalDescChars = 0;
            foreach (var t in untranslated)
                totalDescChars += t.description?.Length ?? 0;

            int batches = (untranslated.Count + BatchSize - 1) / BatchSize;

            // プロンプトのオーバーヘッド（指示文）≈ 350文字/バッチ
            long totalPromptChars = 350L * batches + totalDescChars;

            // 英語テキスト: ~4文字/トークン、JSON構造オーバーヘッド: ~20%
            int inputTokens = (int)(totalPromptChars / 3.5);
            // 出力: 翻訳結果 ≈ 入力の説明部分と同程度 + JSONオーバーヘッド
            int outputTokens = (int)(totalDescChars / 3.0);

            string modelName = SettingsStore.GetString("UnityAgent_ModelName", "gemini-2.0-flash");
            var (inPrice, outPrice) = GetModelPricing(modelName);

            double costUsd = -1;
            if (inPrice >= 0)
                costUsd = (inputTokens / 1_000_000.0) * inPrice + (outputTokens / 1_000_000.0) * outPrice;

            return new TranslationEstimate
            {
                untranslatedCount = untranslated.Count,
                totalDescChars = totalDescChars,
                batches = batches,
                inputTokens = inputTokens,
                outputTokens = outputTokens,
                modelName = modelName,
                costUsd = costUsd,
            };
        }

        public struct TranslationEstimate
        {
            public int untranslatedCount;
            public long totalDescChars;
            public int batches;
            public int inputTokens;
            public int outputTokens;
            public string modelName;
            public double costUsd; // < 0 なら不明
        }

        private static (double inputPerMToken, double outputPerMToken) GetModelPricing(string modelName)
        {
            string m = modelName.ToLower();

            // Gemini
            if (m.Contains("gemini-2.0-flash")) return (0.10, 0.40);
            if (m.Contains("gemini-2.5-flash")) return (0.15, 0.60);
            if (m.Contains("gemini-2.5-pro")) return (1.25, 10.00);
            if (m.Contains("gemini-1.5-flash")) return (0.075, 0.30);
            if (m.Contains("gemini-1.5-pro")) return (1.25, 5.00);

            // OpenAI
            if (m.Contains("gpt-4o-mini")) return (0.15, 0.60);
            if (m.Contains("gpt-4.1-mini")) return (0.40, 1.60);
            if (m.Contains("gpt-4.1-nano")) return (0.10, 0.40);
            if (m.Contains("gpt-4o")) return (2.50, 10.00);
            if (m.Contains("gpt-4.1")) return (2.00, 8.00);

            // Claude
            if (m.Contains("claude-3-haiku") || m.Contains("claude-3.5-haiku")) return (0.80, 4.00);
            if (m.Contains("claude-3.5-sonnet") || m.Contains("claude-sonnet-4")) return (3.00, 15.00);
            if (m.Contains("claude-3-opus") || m.Contains("claude-opus")) return (15.00, 75.00);

            return (-1, -1); // 不明
        }

        /// <summary>
        /// バッチ翻訳を実行。省略なしのフル翻訳。
        /// </summary>
        public static IEnumerator TranslateAll(
            List<(string name, string description)> tools,
            string langCode,
            Action<float> onProgress,
            Action<string> onStatus,
            Action<string> onLog,
            Action onComplete,
            Action<string> onError)
        {
            if (langCode == "en")
            {
                onComplete?.Invoke();
                yield break;
            }

            string languageName = GetLanguageName(langCode);
            var dict = GetOrLoadCache(langCode);

            var untranslated = tools.Where(t => !dict.ContainsKey(t.name)).ToList();

            if (untranslated.Count == 0)
            {
                onStatus?.Invoke("全ツール翻訳済み");
                onLog?.Invoke("全ツール翻訳済み");
                onComplete?.Invoke();
                yield break;
            }

            int totalBatches = (untranslated.Count + BatchSize - 1) / BatchSize;
            onLog?.Invoke($"翻訳開始: {untranslated.Count}件 ({totalBatches}バッチ)");

            int completed = 0;
            double startTime = EditorApplication.timeSinceStartup;

            for (int b = 0; b < totalBatches; b++)
            {
                var batch = untranslated.Skip(b * BatchSize).Take(BatchSize).ToList();
                string statusMsg = $"バッチ {b + 1}/{totalBatches} ({batch.Count}件)";
                onStatus?.Invoke(statusMsg);
                onLog?.Invoke($"  バッチ {b + 1}/{totalBatches} 開始: {batch[0].name} ... {batch[batch.Count - 1].name}");

                string prompt = BuildTranslationPrompt(batch, languageName);
                onLog?.Invoke($"  プロンプト生成完了 ({prompt.Length}文字)");

                string response = null;
                string error = null;

                onLog?.Invoke($"  LLM呼び出し開始...");
                yield return RunLLMCall(prompt, r => response = r, e => error = e);
                onLog?.Invoke($"  LLM呼び出し完了 (response={response?.Length ?? 0}文字, error={error ?? "null"})");

                if (!string.IsNullOrEmpty(error))
                {
                    string errMsg = $"バッチ {b + 1} エラー: {error}";
                    onLog?.Invoke($"  ERROR: {error}");
                    onError?.Invoke(errMsg);
                    SaveToDisk(langCode, dict);
                    yield break;
                }

                if (string.IsNullOrEmpty(response))
                {
                    onLog?.Invoke($"  WARNING: レスポンスが空です");
                }
                else
                {
                    // レスポンスの先頭を表示（デバッグ用）
                    string preview = response.Length > 150
                        ? response.Substring(0, 150) + "..."
                        : response;
                    onLog?.Invoke($"  レスポンス先頭: {preview.Replace("\n", " ")}");

                    var parsed = ParseTranslationResponse(response);
                    onLog?.Invoke($"  パース結果: {parsed.Count}件");
                    foreach (var kv in parsed)
                        dict[kv.Key] = kv.Value;

                    if (parsed.Count == 0)
                        onLog?.Invoke($"  WARNING: パース結果が0件 — レスポンス形式を確認してください");
                }

                completed += batch.Count;
                onProgress?.Invoke((float)completed / untranslated.Count);
                SaveToDisk(langCode, dict);
                yield return null;
            }

            double elapsed = EditorApplication.timeSinceStartup - startTime;
            string doneMsg = $"翻訳完了 ({elapsed:F1}秒)";
            onStatus?.Invoke(doneMsg);
            onLog?.Invoke(doneMsg);
            onComplete?.Invoke();
        }

        /// <summary>
        /// 未翻訳ツールをエクスポート用テキストとして生成。
        /// 他のAIにコピペして翻訳してもらうためのプロンプト付きJSON。
        /// </summary>
        public static string ExportForTranslation(List<(string name, string description)> tools, string langCode)
        {
            string languageName = GetLanguageName(langCode);
            var dict = GetOrLoadCache(langCode);
            var untranslated = tools.Where(t => !dict.ContainsKey(t.name)).ToList();

            if (untranslated.Count == 0)
                untranslated = tools; // 全て翻訳済みの場合は全件エクスポート（再翻訳用）

            var sb = new StringBuilder();
            sb.AppendLine($"以下のUnity Editorツール説明を{languageName}に翻訳してください。");
            sb.AppendLine("同じJSON形式で翻訳済みの値を返してください。");
            sb.AppendLine("ツール名（キー）は翻訳せず、説明文（値）のみ翻訳してください。");
            sb.AppendLine("技術用語（GameObject, BlendShape, PhysBone, Material等）はそのまま残してください。");
            sb.AppendLine("省略や要約はせず、完全に翻訳してください。");
            sb.AppendLine();
            sb.AppendLine("{");

            for (int i = 0; i < untranslated.Count; i++)
            {
                string comma = i < untranslated.Count - 1 ? "," : "";
                sb.AppendLine($"  \"{EscapeJson(untranslated[i].name)}\": \"{EscapeJson(untranslated[i].description)}\"{comma}");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 翻訳済みテキスト（JSON）をインポートしてキャッシュに保存。
        /// </summary>
        public static int ImportTranslations(string text, string langCode)
        {
            var parsed = ParseTranslationResponse(text);
            if (parsed.Count == 0)
                return 0;

            var dict = GetOrLoadCache(langCode);
            foreach (var kv in parsed)
                dict[kv.Key] = kv.Value;

            SaveToDisk(langCode, dict);
            return parsed.Count;
        }

        /// <summary>
        /// キャッシュをクリア。
        /// </summary>
        public static void ClearCache(string langCode)
        {
            _memoryCache.Remove(langCode);

            string path = GetCachePath(langCode);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// 現在のキャッシュ内容をアセット言語ファイルに書き出す。
        /// </summary>
        /// <returns>保存件数。</returns>
        public static int SaveToAsset(string langCode)
        {
            var dict = GetOrLoadCache(langCode);
            if (dict.Count == 0) return 0;

            try
            {
                if (!Directory.Exists(AssetDir))
                    Directory.CreateDirectory(AssetDir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"version\": 3,");
                sb.AppendLine($"  \"language\": \"{EscapeJson(langCode)}\",");
                sb.AppendLine("  \"translations\": {");

                var entries = dict.ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    string comma = i < entries.Count - 1 ? "," : "";
                    sb.AppendLine($"    \"{EscapeJson(entries[i].Key)}\": \"{EscapeJson(entries[i].Value)}\"{comma}");
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                File.WriteAllText(GetAssetPath(langCode), sb.ToString(), Encoding.UTF8);
                return dict.Count;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ToolTranslationService] アセット保存エラー ({langCode}): {ex.Message}");
                return 0;
            }
        }

        // ─── LLM呼び出し（スタック駆動コルーチン） ───

        private static IEnumerator RunLLMCall(string prompt, Action<string> onResponse, Action<string> onError)
        {
            ILLMProvider provider;
            try
            {
                provider = CreateProvider();
                Debug.Log($"[ToolTranslation] プロバイダー生成成功: {provider.GetType().Name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolTranslation] プロバイダー生成エラー: {ex.Message}\n{ex.StackTrace}");
                onError?.Invoke($"プロバイダー生成エラー: {ex.Message}");
                yield break;
            }

            var messages = new List<Message>
            {
                new Message
                {
                    role = "user",
                    parts = new[] { new Part { text = prompt } }
                }
            };

            string response = null;
            string error = null;
            bool done = false;

            Debug.Log("[ToolTranslation] CallLLM 開始...");
            var coroutine = provider.CallLLM(
                messages,
                onSuccess: r =>
                {
                    response = r;
                    done = true;
                    Debug.Log($"[ToolTranslation] CallLLM 成功 ({r?.Length ?? 0}文字)");
                },
                onError: e =>
                {
                    error = e;
                    done = true;
                    Debug.LogError($"[ToolTranslation] CallLLM エラー: {e}");
                });

            // ネストされたコルーチンに対応するスタック駆動
            var stack = new Stack<IEnumerator>();
            stack.Push(coroutine);

            while (!done && stack.Count > 0)
            {
                var current = stack.Peek();
                bool moved;
                try
                {
                    moved = current.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ToolTranslation] コルーチン駆動中エラー: {ex.Message}\n{ex.StackTrace}");
                    error = ex.Message;
                    break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }

                var yielded = current.Current;
                if (yielded is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else if (yielded is AsyncOperation asyncOp)
                {
                    while (!asyncOp.isDone)
                        yield return null;
                }
                else
                {
                    yield return null;
                }
            }

            if (!string.IsNullOrEmpty(error))
                onError?.Invoke(error);
            else
                onResponse?.Invoke(response);
        }

        // ─── 内部 ───

        private static Dictionary<string, string> GetOrLoadCache(string langCode)
        {
            if (_memoryCache.TryGetValue(langCode, out var dict))
                return dict;

            dict = LoadFromDisk(langCode);
            _memoryCache[langCode] = dict;
            return dict;
        }

        private static string GetCachePath(string langCode)
        {
            return Path.Combine(CacheDir, $"{langCode}.json");
        }

        private static string GetAssetPath(string langCode)
        {
            return Path.Combine(AssetDir, $"{langCode}.json");
        }

        private static Dictionary<string, string> LoadFromDisk(string langCode)
        {
            var dict = new Dictionary<string, string>();

            // 1. アセットベース翻訳を読み込み
            string assetPath = GetAssetPath(langCode);
            if (File.Exists(assetPath))
            {
                try
                {
                    string json = File.ReadAllText(assetPath, Encoding.UTF8);
                    foreach (var kv in ParseCacheFile(json))
                        dict[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ToolTranslationService] アセット読み込みエラー ({langCode}): {ex.Message}");
                }
            }

            // 2. Library/ キャッシュがあればマージ（上書き）
            string cachePath = GetCachePath(langCode);
            if (File.Exists(cachePath))
            {
                try
                {
                    string json = File.ReadAllText(cachePath, Encoding.UTF8);
                    foreach (var kv in ParseCacheFile(json))
                        dict[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ToolTranslationService] キャッシュ読み込みエラー ({langCode}): {ex.Message}");
                }
            }

            return dict;
        }

        private static void SaveToDisk(string langCode, Dictionary<string, string> dict)
        {
            try
            {
                string dirPath = CacheDir;
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"version\": 3,");
                sb.AppendLine($"  \"language\": \"{EscapeJson(langCode)}\",");
                sb.AppendLine("  \"translations\": {");

                var entries = dict.ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    string comma = i < entries.Count - 1 ? "," : "";
                    sb.AppendLine($"    \"{EscapeJson(entries[i].Key)}\": \"{EscapeJson(entries[i].Value)}\"{comma}");
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                File.WriteAllText(GetCachePath(langCode), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ToolTranslationService] キャッシュ保存エラー ({langCode}): {ex.Message}");
            }
        }

        private static Dictionary<string, string> ParseCacheFile(string json)
        {
            var result = new Dictionary<string, string>();

            int transIdx = json.IndexOf("\"translations\"");
            if (transIdx < 0) return result;

            int braceStart = json.IndexOf('{', transIdx);
            if (braceStart < 0) return result;

            int braceEnd = FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) return result;

            string block = json.Substring(braceStart, braceEnd - braceStart + 1);
            return ParseJsonKeyValues(block);
        }

        private static ILLMProvider CreateProvider()
        {
            int providerType = SettingsStore.GetInt("UnityAgent_ProviderType", 0);
            string apiKey = SettingsStore.GetString("UnityAgent_ApiKey", "");

            if (providerType == 0) // Gemini
            {
                var mode = (GeminiConnectionMode)SettingsStore.GetInt("UnityAgent_GeminiMode", 0);
                string model = SettingsStore.GetString("UnityAgent_ModelName", "gemini-2.0-flash");
                string apiVersion = SettingsStore.GetString("UnityAgent_ApiVersion", "v1");
                string customEndpoint = SettingsStore.GetString("UnityAgent_CustomEndpoint", "");
                string projectId = SettingsStore.GetString("UnityAgent_ProjectId", "");
                string location = SettingsStore.GetString("UnityAgent_Location", "us-central1");
                return new GeminiProvider(apiKey, mode, model, apiVersion, 0, customEndpoint, projectId, location);
            }
            else if (providerType == 2) // Claude CLI
            {
                string cliPath = SettingsStore.GetString("UnityAgent_ClaudeCliPath", "claude");
                string model = SettingsStore.GetString("UnityAgent_ClaudeModelName", "");
                return new ClaudeCliProvider(cliPath, model);
            }
            else if (providerType == 3) // Gemini CLI
            {
                string cliPath = SettingsStore.GetString("UnityAgent_GeminiCliPath", "gemini");
                string model = SettingsStore.GetString("UnityAgent_GeminiCliModelName", "");
                return new GeminiCliProvider(cliPath, model);
            }
            else // OpenAI Compatible
            {
                string baseUrl = SettingsStore.GetString("UnityAgent_BaseUrl", "http://localhost:1234/v1");
                string model = SettingsStore.GetString("UnityAgent_ModelName", "local-model");
                return new OpenAICompatibleProvider(apiKey, baseUrl, model);
            }
        }

        private static string BuildTranslationPrompt(
            List<(string name, string description)> batch,
            string languageName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are a translator. Translate the following Unity Editor tool descriptions to {languageName}.");
            sb.AppendLine("Translate the FULL content accurately without any abbreviation or summarization.");
            sb.AppendLine("Keep technical terms (e.g. GameObject, BlendShape, PhysBone, Material) as-is.");
            sb.AppendLine("Return ONLY a JSON object mapping tool names to translated descriptions.");
            sb.AppendLine("Do not translate tool names, only descriptions.");
            sb.AppendLine();
            sb.AppendLine("Input:");
            sb.AppendLine("{");

            for (int i = 0; i < batch.Count; i++)
            {
                string comma = i < batch.Count - 1 ? "," : "";
                sb.AppendLine($"  \"{EscapeJson(batch[i].name)}\": \"{EscapeJson(batch[i].description)}\"{comma}");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Output (JSON only, no markdown):");

            return sb.ToString();
        }

        /// <summary>
        /// AIレスポンスからJSON部分を抽出してパース。
        /// </summary>
        private static Dictionary<string, string> ParseTranslationResponse(string response)
        {
            // プロバイダーが付加する Tokens 情報や Thinking ブロックを除去
            response = Regex.Replace(response, @"\n\n\[Tokens:.*?\]$", "", RegexOptions.Singleline);
            response = Regex.Replace(response, @"^<Thinking>[\s\S]*?</Thinking>\s*", "");

            // 1. レスポンス全体が有効なJSONか試行
            var result = ParseJsonKeyValues(response);
            if (result.Count > 0) return result;

            // 2. ```json ... ``` ブロックを抽出
            var match = Regex.Match(response, @"```(?:json)?\s*\n?([\s\S]*?)```", RegexOptions.Multiline);
            if (match.Success)
            {
                result = ParseJsonKeyValues(match.Groups[1].Value.Trim());
                if (result.Count > 0) return result;
            }

            // 3. { から最後の } までを抽出
            int first = response.IndexOf('{');
            int last = response.LastIndexOf('}');
            if (first >= 0 && last > first)
            {
                result = ParseJsonKeyValues(response.Substring(first, last - first + 1));
                if (result.Count > 0) return result;
            }

            Debug.LogWarning($"[ToolTranslationService] レスポンスのパースに失敗:\n{response.Substring(0, Math.Min(200, response.Length))}");
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// シンプルな正規表現パーサーで "key": "value" ペアを抽出。
        /// </summary>
        private static Dictionary<string, string> ParseJsonKeyValues(string json)
        {
            var result = new Dictionary<string, string>();
            var matches = Regex.Matches(json, @"""([^""\\]*(?:\\.[^""\\]*)*)""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""");
            foreach (Match m in matches)
            {
                string key = UnescapeJson(m.Groups[1].Value);
                string value = UnescapeJson(m.Groups[2].Value);
                if (key == "version" || key == "language" || key == "translations")
                    continue;
                result[key] = value;
            }
            return result;
        }

        private static int FindMatchingBrace(string json, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                }
                else
                {
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}') { depth--; if (depth == 0) return i; }
                }
            }
            return -1;
        }

        private static string GetLanguageName(string langCode)
        {
            foreach (var lang in AgentSettings.SupportedLanguages)
            {
                if (lang.code == langCode) return lang.label;
            }
            return langCode;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
