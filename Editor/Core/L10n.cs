using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// UI多言語化フレームワーク。
    /// 日本語文字列をキーとして使い、M("日本語テキスト") で現在の言語の翻訳を返す。
    /// 日本語選択時はゼロオーバーヘッド（そのまま返す）。
    /// M() で収集されたキーはディスクに永続化され、エディタ再起動後も翻訳対象として残る。
    /// </summary>
    internal static class L10n
    {
        private const string CacheDir = "Library/UnityAgent/UITranslations";
        private const string KeysFilePath = "Library/UnityAgent/UITranslations/_keys.json";
        private static string AssetDir => PackagePaths.LocalizationDir("ui");

        // ─── 翻訳辞書 ───
        private static Dictionary<string, string> _dict;
        private static string _loadedLang;

        // ─── キー収集（永続化） ───
        private static readonly HashSet<string> _allKeys = new HashSet<string>();
        private static bool _keysLoaded;
        private static bool _keysDirty;

        // ─── 公開API ───

        /// <summary>
        /// M("日本語テキスト") → 現在の言語の翻訳を返す。
        /// 日本語の場合はそのまま返す。他言語はJSON辞書から引く、なければ日本語フォールバック。
        /// 初回呼び出し時にディスクから既存キーを復元し、新規キーは自動的に永続化される。
        /// </summary>
        public static string M(string ja)
        {
            EnsureKeysLoaded();
            if (_allKeys.Add(ja))
                _keysDirty = true;

            string lang = AgentSettings.UILanguage;
            if (lang == "ja") return ja;

            EnsureLoaded(lang);
            if (_dict != null && _dict.TryGetValue(ja, out string t))
                return t;
            return ja;
        }

        /// <summary>
        /// 収集した全キーを取得（エクスポート用）。
        /// ディスクに永続化済みのキーと、今回のセッションで M() が呼ばれたキーの両方を含む。
        /// </summary>
        public static IReadOnlyCollection<string> GetAllKeys()
        {
            EnsureKeysLoaded();
            return _allKeys;
        }

        /// <summary>
        /// 翻訳進捗を返す。
        /// </summary>
        public static (int translated, int total) GetProgress(string langCode)
        {
            EnsureKeysLoaded();

            if (langCode == "ja")
                return (_allKeys.Count, _allKeys.Count);

            if (_allKeys.Count == 0)
                return (0, 0);

            var dict = GetOrLoadDict(langCode);
            int translated = 0;
            foreach (var key in _allKeys)
            {
                if (dict.ContainsKey(key))
                    translated++;
            }
            return (translated, _allKeys.Count);
        }

        /// <summary>
        /// キャッシュをリロード。インポート後や言語切替時に呼ぶ。
        /// </summary>
        public static void Reload()
        {
            _dict = null;
            _loadedLang = null;
        }

        /// <summary>
        /// 指定言語のキャッシュをクリア（ファイルも削除）。
        /// </summary>
        public static void ClearCache(string langCode)
        {
            if (_loadedLang == langCode)
            {
                _dict = null;
                _loadedLang = null;
            }

            string path = GetCachePath(langCode);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// 収集済みキーをプロンプト付きJSONとしてエクスポート。
        /// クリップボードにコピーして他のAIチャットに貼り付ける用途。
        /// </summary>
        public static string ExportForTranslation(string langCode)
        {
            EnsureKeysLoaded();
            string languageName = GetLanguageName(langCode);
            var keys = _allKeys.OrderBy(k => k).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"以下のUI文字列を{languageName}に翻訳してください。");
            sb.AppendLine("同じJSON形式で翻訳済みの値を返してください。");
            sb.AppendLine("省略や要約はせず、{0} 等のプレースホルダはそのまま残してください。");
            sb.AppendLine();
            sb.AppendLine("{");

            for (int i = 0; i < keys.Count; i++)
            {
                string comma = i < keys.Count - 1 ? "," : "";
                sb.AppendLine($"  \"{EscapeJson(keys[i])}\": \"\"{comma}");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// JSONテキストから翻訳をインポートしてキャッシュに保存。
        /// </summary>
        /// <returns>インポート件数。パース失敗時は0。</returns>
        public static int ImportTranslations(string text, string langCode)
        {
            var parsed = ParseJsonResponse(text);
            if (parsed.Count == 0) return 0;

            var dict = GetOrLoadDict(langCode);
            foreach (var kv in parsed)
                dict[kv.Key] = kv.Value;

            SaveToDisk(langCode, dict);

            // 現在の言語ならメモリキャッシュも更新
            if (_loadedLang == langCode)
                _dict = dict;

            return parsed.Count;
        }

        /// <summary>
        /// 全言語の未翻訳UI文字列を一括エクスポート。
        /// JSON形式: { "en": { "日本語キー": "", ... }, "ko": { ... } }
        /// </summary>
        public static string ExportAllUntranslated()
        {
            EnsureKeysLoaded();
            var keys = _allKeys.OrderBy(k => k).ToList();
            var langs = AgentSettings.SupportedLanguages;

            var sb = new StringBuilder();
            sb.AppendLine("以下のUI文字列をそれぞれの言語に翻訳してください。");
            sb.AppendLine("同じJSON形式で翻訳済みの値を返してください。");
            sb.AppendLine("省略や要約はせず、{0} 等のプレースホルダはそのまま残してください。");
            sb.AppendLine();
            sb.AppendLine("{");

            bool firstLang = true;
            foreach (var (code, label) in langs)
            {
                if (code == "ja") continue;

                var dict = GetOrLoadDict(code);
                var untranslated = keys.Where(k => !dict.ContainsKey(k)).ToList();
                if (untranslated.Count == 0) continue;

                if (!firstLang) sb.AppendLine(",");
                firstLang = false;

                sb.AppendLine($"  \"{EscapeJson(code)}\": {{");
                for (int i = 0; i < untranslated.Count; i++)
                {
                    string comma = i < untranslated.Count - 1 ? "," : "";
                    sb.AppendLine($"    \"{EscapeJson(untranslated[i])}\": \"\"{comma}");
                }
                sb.Append("  }");
            }

            sb.AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 全言語の翻訳を一括インポート。
        /// JSON形式: { "en": { "日本語キー": "Translation", ... }, "ko": { ... } }
        /// </summary>
        /// <returns>インポートした総件数。</returns>
        public static int ImportAllTranslations(string text)
        {
            var parsed = ParseMultiLangJson(text);
            int totalCount = 0;

            foreach (var (langCode, translations) in parsed)
            {
                var dict = GetOrLoadDict(langCode);
                int count = 0;
                foreach (var kv in translations)
                {
                    if (!string.IsNullOrEmpty(kv.Value))
                    {
                        dict[kv.Key] = kv.Value;
                        count++;
                    }
                }

                if (count > 0)
                {
                    SaveToDisk(langCode, dict);
                    if (_loadedLang == langCode)
                        _dict = dict;
                    totalCount += count;
                }
            }

            return totalCount;
        }

        /// <summary>
        /// 全言語一括エクスポートの未翻訳件数サマリを返す。
        /// </summary>
        public static List<(string code, string label, int untranslated, int total)> GetAllLanguageProgress()
        {
            EnsureKeysLoaded();
            var keys = _allKeys;
            var result = new List<(string, string, int, int)>();

            foreach (var (code, label) in AgentSettings.SupportedLanguages)
            {
                if (code == "ja") continue;

                var dict = GetOrLoadDict(code);
                int untranslated = 0;
                foreach (var k in keys)
                {
                    if (!dict.ContainsKey(k))
                        untranslated++;
                }
                result.Add((code, label, untranslated, keys.Count));
            }

            return result;
        }

        private static List<(string langCode, Dictionary<string, string> translations)> ParseMultiLangJson(string text)
        {
            var results = new List<(string, Dictionary<string, string>)>();

            // Extract code blocks if present
            var codeBlockMatch = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]*?)```");
            if (codeBlockMatch.Success)
                text = codeBlockMatch.Groups[1].Value.Trim();

            // Find outermost { }
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return results;
            text = text.Substring(start, end - start + 1);

            // Match "langCode": { ... } patterns
            var langPattern = new Regex(@"""([^""]+)""\s*:\s*\{");
            var matches = langPattern.Matches(text);

            foreach (Match m in matches)
            {
                string langCode = UnescapeJson(m.Groups[1].Value);
                if (langCode.StartsWith("_")) continue;

                // Find the matching closing brace
                int braceStart = m.Index + m.Length - 1;
                int depth = 1;
                int pos = braceStart + 1;
                while (pos < text.Length && depth > 0)
                {
                    if (text[pos] == '{') depth++;
                    else if (text[pos] == '}') depth--;
                    else if (text[pos] == '"')
                    {
                        // Skip string contents (handle escaped quotes)
                        pos++;
                        while (pos < text.Length && text[pos] != '"')
                        {
                            if (text[pos] == '\\') pos++;
                            pos++;
                        }
                    }
                    pos++;
                }

                if (depth == 0)
                {
                    string block = text.Substring(braceStart, pos - braceStart);
                    var translations = ParseJsonKeyValues(block);
                    if (translations.Count > 0)
                        results.Add((langCode, translations));
                }
            }

            return results;
        }

        /// <summary>
        /// 現在のキャッシュ内容をアセット言語ファイルに書き出す。
        /// </summary>
        /// <returns>保存件数。</returns>
        public static int SaveToAsset(string langCode)
        {
            var dict = GetOrLoadDict(langCode);
            if (dict.Count == 0) return 0;

            try
            {
                string dirPath = AssetDir;
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                var entries = dict.OrderBy(kv => kv.Key).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    string comma = i < entries.Count - 1 ? "," : "";
                    sb.AppendLine($"  \"{EscapeJson(entries[i].Key)}\": \"{EscapeJson(entries[i].Value)}\"{comma}");
                }
                sb.AppendLine("}");

                File.WriteAllText(GetAssetPath(langCode), sb.ToString(), Encoding.UTF8);
                return dict.Count;
            }
            catch (System.Exception ex)
            {
                AgentLogger.Warning(LogTag.L10n, $"Asset save error ({langCode}): {ex.Message}");
                return 0;
            }
        }

        // ─── キー永続化 ───

        private static void EnsureKeysLoaded()
        {
            if (_keysLoaded) return;
            _keysLoaded = true;

            LoadKeysFromDisk();

            // ドメインリロード・エディタ終了時にキーを保存
            AssemblyReloadEvents.beforeAssemblyReload += FlushKeys;
            EditorApplication.quitting += FlushKeys;
        }

        /// <summary>
        /// 未保存のキーがあればディスクに書き出す。
        /// </summary>
        public static void FlushKeys()
        {
            if (!_keysDirty) return;
            _keysDirty = false;
            SaveKeysToDisk();
        }

        private static void LoadKeysFromDisk()
        {
            if (!File.Exists(KeysFilePath)) return;

            try
            {
                string json = File.ReadAllText(KeysFilePath, Encoding.UTF8);
                // JSON配列: ["key1", "key2", ...]
                var matches = Regex.Matches(json, @"""([^""\\]*(?:\\.[^""\\]*)*)""");
                foreach (Match m in matches)
                {
                    string key = UnescapeJson(m.Groups[1].Value);
                    if (!string.IsNullOrEmpty(key))
                        _allKeys.Add(key);
                }
            }
            catch (System.Exception ex)
            {
                AgentLogger.Warning(LogTag.L10n, $"Keys load error: {ex.Message}");
            }
        }

        private static void SaveKeysToDisk()
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                var sorted = _allKeys.OrderBy(k => k).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < sorted.Count; i++)
                {
                    string comma = i < sorted.Count - 1 ? "," : "";
                    sb.AppendLine($"  \"{EscapeJson(sorted[i])}\"{comma}");
                }
                sb.AppendLine("]");

                File.WriteAllText(KeysFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                AgentLogger.Warning(LogTag.L10n, $"Keys save error: {ex.Message}");
            }
        }

        // ─── 翻訳辞書 内部 ───

        private static void EnsureLoaded(string lang)
        {
            if (_loadedLang == lang && _dict != null) return;
            _dict = LoadFromDisk(lang);
            _loadedLang = lang;
        }

        private static Dictionary<string, string> GetOrLoadDict(string langCode)
        {
            if (_loadedLang == langCode && _dict != null)
                return _dict;
            return LoadFromDisk(langCode);
        }

        private static string GetCachePath(string langCode)
            => Path.Combine(CacheDir, $"{langCode}.json");

        private static string GetAssetPath(string langCode)
            => Path.Combine(AssetDir, $"{langCode}.json");

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
                    foreach (var kv in ParseJsonKeyValues(json))
                        dict[kv.Key] = kv.Value;
                }
                catch (System.Exception ex)
                {
                    AgentLogger.Warning(LogTag.L10n, $"Asset load error ({langCode}): {ex.Message}");
                }
            }

            // 2. Library/ キャッシュがあればマージ（上書き）
            string cachePath = GetCachePath(langCode);
            if (File.Exists(cachePath))
            {
                try
                {
                    string json = File.ReadAllText(cachePath, Encoding.UTF8);
                    foreach (var kv in ParseJsonKeyValues(json))
                        dict[kv.Key] = kv.Value;
                }
                catch (System.Exception ex)
                {
                    AgentLogger.Warning(LogTag.L10n, $"Cache load error ({langCode}): {ex.Message}");
                }
            }

            return dict;
        }

        private static void SaveToDisk(string langCode, Dictionary<string, string> dict)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                var entries = dict.OrderBy(kv => kv.Key).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    string comma = i < entries.Count - 1 ? "," : "";
                    sb.AppendLine($"  \"{EscapeJson(entries[i].Key)}\": \"{EscapeJson(entries[i].Value)}\"{comma}");
                }
                sb.AppendLine("}");

                File.WriteAllText(GetCachePath(langCode), sb.ToString(), Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                AgentLogger.Warning(LogTag.L10n, $"Cache save error ({langCode}): {ex.Message}");
            }
        }

        private static Dictionary<string, string> ParseJsonResponse(string text)
        {
            // ```json ... ``` ブロックを抽出
            var match = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]*?)```");
            if (match.Success)
            {
                var result = ParseJsonKeyValues(match.Groups[1].Value.Trim());
                if (result.Count > 0) return result;
            }

            // 全体をパース
            var direct = ParseJsonKeyValues(text);
            if (direct.Count > 0) return direct;

            // { から最後の } までを抽出
            int first = text.IndexOf('{');
            int last = text.LastIndexOf('}');
            if (first >= 0 && last > first)
                return ParseJsonKeyValues(text.Substring(first, last - first + 1));

            return new Dictionary<string, string>();
        }

        private static Dictionary<string, string> ParseJsonKeyValues(string json)
        {
            var result = new Dictionary<string, string>();
            var matches = Regex.Matches(json,
                @"""([^""\\]*(?:\\.[^""\\]*)*)""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""");
            foreach (Match m in matches)
            {
                string key = UnescapeJson(m.Groups[1].Value);
                string value = UnescapeJson(m.Groups[2].Value);
                if (!string.IsNullOrEmpty(value))
                    result[key] = value;
            }
            return result;
        }

        private static string GetLanguageName(string langCode)
        {
            foreach (var lang in AgentSettings.SupportedLanguages)
                if (lang.code == langCode) return lang.label;
            return langCode;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
                    .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
