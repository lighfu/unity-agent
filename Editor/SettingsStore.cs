using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// JSON ファイルベースの設定ストア。
    /// EditorPrefs (Windows レジストリ) の代わりに UserSettings/UnityAgentSettings.json に永続化する。
    /// ALCOM 等の環境で PC 再起動時にレジストリが消失しても設定を保持できる。
    /// </summary>
    internal static class SettingsStore
    {
        private const string FileName = "UnityAgentSettings.json";

        private static string _filePath;
        private static Dictionary<string, object> _data;
        private static bool _dirty;

        private static string FilePath
        {
            get
            {
                if (_filePath == null)
                {
                    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                    _filePath = Path.Combine(projectRoot, "UserSettings", FileName);
                }
                return _filePath;
            }
        }

        // ─── Public API ───

        internal static string GetString(string key, string defaultValue = "")
        {
            EnsureLoaded();
            if (_data.TryGetValue(key, out var val))
            {
                if (val is string s) return s;
                if (val != null) return val.ToString();
            }
            return defaultValue;
        }

        internal static void SetString(string key, string value)
        {
            EnsureLoaded();
            _data[key] = value;
            _dirty = true;
            DeferredSave();
        }

        internal static int GetInt(string key, int defaultValue = 0)
        {
            EnsureLoaded();
            if (_data.TryGetValue(key, out var val))
            {
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
                if (val is int i) return i;
                if (val is string s && int.TryParse(s, out int parsed)) return parsed;
            }
            return defaultValue;
        }

        internal static void SetInt(string key, int value)
        {
            EnsureLoaded();
            _data[key] = (long)value;
            _dirty = true;
            DeferredSave();
        }

        internal static bool GetBool(string key, bool defaultValue = false)
        {
            EnsureLoaded();
            if (_data.TryGetValue(key, out var val))
            {
                if (val is bool b) return b;
                if (val is long l) return l != 0;
                if (val is double d) return d != 0;
                if (val is string s) return s == "True" || s == "true" || s == "1";
            }
            return defaultValue;
        }

        internal static void SetBool(string key, bool value)
        {
            EnsureLoaded();
            _data[key] = value;
            _dirty = true;
            DeferredSave();
        }

        internal static bool HasKey(string key)
        {
            EnsureLoaded();
            return _data.ContainsKey(key);
        }

        internal static void DeleteKey(string key)
        {
            EnsureLoaded();
            if (_data.Remove(key))
            {
                _dirty = true;
                DeferredSave();
            }
        }

        // ─── Load / Save ───

        private static void EnsureLoaded()
        {
            if (_data != null) return;

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    _data = MiniJson.Deserialize(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UnityAgent] Failed to load settings: {e.Message}");
                    _data = new Dictionary<string, object>();
                }
            }
            else
            {
                _data = new Dictionary<string, object>();
                MigrateFromEditorPrefs();
            }
        }

        private static bool _saveScheduled;

        private static void DeferredSave()
        {
            if (_saveScheduled) return;
            _saveScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _saveScheduled = false;
                Flush();
            };
        }

        private static void Flush()
        {
            if (!_dirty || _data == null) return;
            _dirty = false;

            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = MiniJson.Serialize(_data);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityAgent] Failed to save settings: {e.Message}");
            }
        }

        // ─── Migration ───

        private static readonly string[] MigrationStringKeys =
        {
            "UnityAgent_ApiKey", "UnityAgent_BaseUrl", "UnityAgent_ModelName",
            "UnityAgent_ApiVersion", "UnityAgent_CustomEndpoint", "UnityAgent_ProjectId",
            "UnityAgent_Location", "UnityAgent_ImageModelName",
            "UnityAgent_ClaudeCliPath", "UnityAgent_ClaudeModelName",
            "UnityAgent_GeminiCliPath", "UnityAgent_GeminiCliModelName",
            "UnityAgent_ClaudeApiKey", "UnityAgent_ClaudeApiModelName",
            "UnityAgent_OpenAIApiKey", "UnityAgent_OpenAIModelName",
            "UnityAgent_DeepSeekApiKey", "UnityAgent_DeepSeekModelName",
            "UnityAgent_GroqApiKey", "UnityAgent_GroqModelName",
            "UnityAgent_OllamaBaseUrl", "UnityAgent_OllamaModelName",
            "UnityAgent_XaiApiKey", "UnityAgent_XaiModelName",
            "UnityAgent_MistralApiKey", "UnityAgent_MistralModelName",
            "UnityAgent_PerplexityApiKey", "UnityAgent_PerplexityModelName",
            "UnityAgent_UILanguage",
            "UnityAgent_WebServerUsername", "UnityAgent_WebServerPassword",
            "UnityAgent_ConfirmTools", "UnityAgent_DisabledSkills",
            "UnityAgent_DisabledTools", "UnityAgent_EnabledExternalTools",
            "UnityAgent_SeedColor",
            "UnityAgent_LastUpdateCheck", "UnityAgent_SkipVersion",
            "UnityAgent_ExpirationDate", "UnityAgent_ExpirationReason",
            "UnityAgent_ExpirationVersion",
            "UnityAgent_MeshyApiKey",
            "MeshPainter_ColorHistory",
        };

        private static readonly string[] MigrationIntKeys =
        {
            "UnityAgent_ProviderType", "UnityAgent_GeminiMode",
            "UnityAgent_ThinkingBudget", "UnityAgent_BrowserBridgePort",
            "UnityAgent_MaxContextTokens", "UnityAgent_WebServerPort",
            "UnityAgent_ThemeMode",
        };

        private static readonly string[] MigrationBoolKeys =
        {
            "UnityAgent_UseThinking",
            "UnityAgent_ConfirmDestructive",
            "UnityAgent_DiscordLoggingEnabled",
        };

        // Theme color keys: UnityAgent_ThemeColor_{name}
        private static readonly string[] ThemeColorNames =
        {
            "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer",
            "Secondary", "OnSecondary", "SecondaryContainer", "OnSecondaryContainer",
            "Tertiary", "OnTertiary", "TertiaryContainer", "OnTertiaryContainer",
            "Surface", "OnSurface", "SurfaceVariant", "OnSurfaceVariant", "SurfaceContainerHigh",
            "Outline", "OutlineVariant",
            "Error", "OnError",
            "InverseSurface", "InverseOnSurface", "InversePrimary",
        };

        private static void MigrateFromEditorPrefs()
        {
            bool migrated = false;

            foreach (string key in MigrationStringKeys)
            {
                if (EditorPrefs.HasKey(key))
                {
                    _data[key] = EditorPrefs.GetString(key, "");
                    migrated = true;
                }
            }

            foreach (string key in MigrationIntKeys)
            {
                if (EditorPrefs.HasKey(key))
                {
                    _data[key] = (long)EditorPrefs.GetInt(key, 0);
                    migrated = true;
                }
            }

            foreach (string key in MigrationBoolKeys)
            {
                if (EditorPrefs.HasKey(key))
                {
                    _data[key] = EditorPrefs.GetBool(key, false);
                    migrated = true;
                }
            }

            foreach (string name in ThemeColorNames)
            {
                string key = "UnityAgent_ThemeColor_" + name;
                if (EditorPrefs.HasKey(key))
                {
                    _data[key] = EditorPrefs.GetString(key, "");
                    migrated = true;
                }
            }

            if (migrated)
            {
                _dirty = true;
                Flush();
                Debug.Log("[UnityAgent] Settings migrated from EditorPrefs to JSON file.");
            }
        }

        // ─── Minimal JSON serializer/deserializer ───

        private static class MiniJson
        {
            internal static Dictionary<string, object> Deserialize(string json)
            {
                var result = new Dictionary<string, object>();
                if (string.IsNullOrEmpty(json)) return result;

                json = json.Trim();
                if (json.Length < 2 || json[0] != '{') return result;

                int i = 1;
                while (i < json.Length)
                {
                    SkipWhitespace(json, ref i);
                    if (i >= json.Length || json[i] == '}') break;

                    // key
                    string key = ReadString(json, ref i);
                    if (key == null) break;

                    SkipWhitespace(json, ref i);
                    if (i >= json.Length || json[i] != ':') break;
                    i++; // skip ':'

                    SkipWhitespace(json, ref i);
                    object val = ReadValue(json, ref i);
                    result[key] = val;

                    SkipWhitespace(json, ref i);
                    if (i < json.Length && json[i] == ',') i++;
                }
                return result;
            }

            internal static string Serialize(Dictionary<string, object> data)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("{\n");
                bool first = true;
                // Sort keys for stable output
                var keys = new List<string>(data.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  ");
                    WriteString(sb, key);
                    sb.Append(": ");
                    WriteValue(sb, data[key]);
                }
                sb.Append("\n}");
                return sb.ToString();
            }

            private static void SkipWhitespace(string s, ref int i)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                    i++;
            }

            private static string ReadString(string s, ref int i)
            {
                if (i >= s.Length || s[i] != '"') return null;
                i++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        switch (s[i])
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 < s.Length)
                                {
                                    string hex = s.Substring(i + 1, 4);
                                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                        sb.Append((char)code);
                                    i += 4;
                                }
                                break;
                            default: sb.Append(s[i]); break;
                        }
                    }
                    else
                    {
                        sb.Append(s[i]);
                    }
                    i++;
                }
                if (i < s.Length) i++; // skip closing quote
                return sb.ToString();
            }

            private static object ReadValue(string s, ref int i)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length) return null;

                char c = s[i];
                if (c == '"') return ReadString(s, ref i);
                if (c == 't' || c == 'f') return ReadBool(s, ref i);
                if (c == 'n') { i += 4; return null; } // null
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i);
                // skip unknown
                i++;
                return null;
            }

            private static bool ReadBool(string s, ref int i)
            {
                if (s.Substring(i, 4) == "true") { i += 4; return true; }
                if (s.Substring(i, 5) == "false") { i += 5; return false; }
                i++;
                return false;
            }

            private static object ReadNumber(string s, ref int i)
            {
                int start = i;
                bool isFloat = false;
                if (s[i] == '-') i++;
                while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                {
                    if (s[i] == '.' || s[i] == 'e' || s[i] == 'E') isFloat = true;
                    i++;
                }
                string num = s.Substring(start, i - start);
                if (isFloat)
                {
                    if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return d;
                }
                else
                {
                    if (long.TryParse(num, out long l)) return l;
                }
                return 0L;
            }

            private static void WriteString(System.Text.StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                sb.AppendFormat("\\u{0:x4}", (int)c);
                            else
                                sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }

            private static void WriteValue(System.Text.StringBuilder sb, object val)
            {
                if (val == null) { sb.Append("null"); return; }
                if (val is string s) { WriteString(sb, s); return; }
                if (val is bool b) { sb.Append(b ? "true" : "false"); return; }
                if (val is long l) { sb.Append(l.ToString()); return; }
                if (val is int i) { sb.Append(i.ToString()); return; }
                if (val is double d)
                {
                    sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }
                // fallback
                WriteString(sb, val.ToString());
            }
        }
    }
}
