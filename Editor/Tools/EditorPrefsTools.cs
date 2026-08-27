using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Read / write / enumerate EditorPrefs.
    ///
    /// Two things make this more than a thin wrapper:
    ///
    /// 1. "not set" and "false / 0 / empty" are reported separately. Editor extensions commonly
    ///    keep a flag's default in the getter's defaultValue and treat "reset to default" as
    ///    deleting the key; <c>GetBool(key, false)</c> cannot tell a deliberate false from an
    ///    absent key, so a reset looks like it did nothing.
    /// 2. EditorPrefs is machine-global and plenty of packages park API keys and tokens in it —
    ///    this very machine has a <c>UnityAgent_ApiKey</c> left over from before the settings
    ///    moved to JSON. Values are therefore withheld for keys whose NAME looks like a
    ///    credential, and writes/deletes to those keys are refused outright.
    /// </summary>
    public static class EditorPrefsTools
    {
        // Unity has stored EditorPrefs here for every 5.x+ release. Value names carry a
        // "_h<hash>" suffix that is not part of the key.
        private const string RegistryPath = @"Software\Unity Technologies\Unity Editor 5.x";
        private static readonly Regex HashSuffix = new Regex(@"_h\d+$", RegexOptions.CultureInvariant);

        // Matched against WORDS split out of the key name, not against the raw string. A plain
        // substring test flags UnityAgent_MaxContextTokens (it contains "token") and would then
        // refuse to let anyone set an ordinary numeric setting.
        private static readonly string[] SecretWords =
        {
            "token", "password", "passwd", "passphrase", "secret",
            "credential", "credentials", "apikey", "privatekey",
        };

        // Adjacent word pairs, so "Api" + "Key" is a credential but "Max" + "Key" is not.
        private static readonly string[] SecretWordPairs =
        {
            "apikey", "apisecret", "apitoken", "authkey", "authtoken", "accesskey",
            "accesstoken", "refreshtoken", "servertoken", "secretkey", "privatekey",
            "clientsecret", "clientkey", "sessiontoken",
        };

        // ── Read ─────────────────────────────────────────────────────────────

        [AgentTool("Read one EditorPrefs value. " +
                   "type: 'string' (default) | 'bool' | 'int' | 'float' — must match how the key was written. " +
                   "Reports state=missing when the key does not exist, which is what distinguishes 'never set' " +
                   "from 'explicitly false / 0 / empty'; use that to verify a reset-to-default actually took. " +
                   "EditorPrefs is per-machine and shared by every Unity project and package on it. " +
                   "The value is withheld (value=<redacted>) when the key name looks like a credential " +
                   "(apikey / token / password / secret / …), because packages routinely store API keys here.")]
        public static string GetEditorPref(string key, string type = "string")
        {
            if (string.IsNullOrEmpty(key)) return "Error: key is required.";
            if (!TryParseType(type, out string t)) return TypeError(type);

            if (!EditorPrefs.HasKey(key))
                return $"key={key} state=missing (EditorPrefs has no such key; the caller's defaultValue applies)";

            if (LooksLikeSecret(key))
                return $"key={key} type={t} state=set value=<redacted> " +
                       "(the key name looks like a credential — read it in Unity's own UI if you need the value)";

            try
            {
                switch (t)
                {
                    case "bool": return $"key={key} type=bool state=set value={(EditorPrefs.GetBool(key) ? "true" : "false")}";
                    case "int": return $"key={key} type=int state=set value={EditorPrefs.GetInt(key)}";
                    case "float": return $"key={key} type=float state=set value={EditorPrefs.GetFloat(key)}";
                    default: return $"key={key} type=string state=set value={EditorPrefs.GetString(key)}";
                }
            }
            catch (Exception e)
            {
                return $"Error: key '{key}' exists but could not be read as {t}: {e.Message}. " +
                       "The key was probably written with a different type.";
            }
        }

        [AgentTool("List EditorPrefs key names. " +
                   "prefix: return only keys starting with this (case-insensitive, optional). " +
                   "maxKeys: cap on returned names (default 200, max 2000); the total found is always reported, " +
                   "so a truncated listing is never mistaken for the whole set. " +
                   "Returns NAMES ONLY — no values — so this is safe to run over a machine that stores API keys " +
                   "in EditorPrefs. Windows only: Unity exposes no API to enumerate EditorPrefs, so this reads " +
                   "HKCU\\Software\\Unity Technologies\\Unity Editor 5.x directly and strips the _h<hash> suffix " +
                   "Unity appends to each value name.")]
        public static string ListEditorPrefKeys(string prefix = "", int maxKeys = 200)
        {
            if (maxKeys <= 0) maxKeys = 200;
            if (maxKeys > 2000) maxKeys = 2000;

            if (!TryEnumerateValueNames(out var rawNames, out string err))
                return $"Error: {err}";

            string pre = prefix ?? "";
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in rawNames)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string name = HashSuffix.Replace(raw, "");
                if (pre.Length > 0 && !name.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name)) continue;
                // Two different keys can collapse onto the same name once the hash is stripped
                // (and stale entries survive Unity upgrades). HasKey settles which are real.
                if (!EditorPrefs.HasKey(name)) continue;
                keys.Add(name);
            }

            keys.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.Append($"=== EditorPrefs keys ({keys.Count} found");
            if (pre.Length > 0) sb.Append($" with prefix '{pre}'");
            sb.Append($", {rawNames.Count} registry values scanned");
            if (keys.Count > maxKeys) sb.Append($", showing first {maxKeys}");
            sb.AppendLine(") ===");

            for (int i = 0; i < keys.Count && i < maxKeys; i++)
                sb.AppendLine("  " + keys[i]);

            if (keys.Count > maxKeys)
                sb.AppendLine($"  … +{keys.Count - maxKeys} more not listed (raise maxKeys or narrow prefix)");
            if (keys.Count == 0)
                sb.AppendLine("  (no keys matched)");

            return sb.ToString().TrimEnd();
        }

        // ── Write ────────────────────────────────────────────────────────────

        [AgentTool("Write one EditorPrefs value. " +
                   "type: 'string' | 'bool' | 'int' | 'float'. value is parsed according to type; a value that " +
                   "does not parse returns an error instead of silently writing 0. " +
                   "Reports whether the key already existed, so an overwrite is visible. " +
                   "EditorPrefs is per-machine and shared by every Unity project on it — this is not project state. " +
                   "Refuses keys whose name looks like a credential (apikey / token / password / secret / …); " +
                   "set those through the owning package's own UI.")]
        public static string SetEditorPref(string key, string type, string value)
        {
            if (string.IsNullOrEmpty(key)) return "Error: key is required.";
            if (!TryParseType(type, out string t)) return TypeError(type);
            if (LooksLikeSecret(key))
                return $"Error: refusing to write '{key}' — the key name looks like a credential. " +
                       "Set it through the owning package's own settings UI instead.";

            bool existed = EditorPrefs.HasKey(key);
            string state = existed ? "overwrote" : "created";

            try
            {
                switch (t)
                {
                    case "bool":
                        if (!ToolUtility.TryParseBool(value, out bool b))
                            return $"Error: '{value}' is not a bool. Use true/false, 1/0, on/off, yes/no.";
                        EditorPrefs.SetBool(key, b);
                        return $"{state}: key={key} type=bool value={(b ? "true" : "false")}";

                    case "int":
                        if (!int.TryParse(value, out int i))
                            return $"Error: '{value}' is not an int.";
                        EditorPrefs.SetInt(key, i);
                        return $"{state}: key={key} type=int value={i}";

                    case "float":
                        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out float f))
                            return $"Error: '{value}' is not a float.";
                        EditorPrefs.SetFloat(key, f);
                        return $"{state}: key={key} type=float value={f}";

                    default:
                        EditorPrefs.SetString(key, value ?? "");
                        return $"{state}: key={key} type=string value={value ?? ""}";
                }
            }
            catch (Exception e)
            {
                return $"Error: failed to write '{key}': {e.Message}";
            }
        }

        // Deleting one EditorPrefs key is not in the same class as deleting a GameObject or an
        // asset — it resets a preference to its code-side default and nothing in the project
        // changes. The Delete* prefix would otherwise classify it Dangerous and hide it from MCP
        // clients at the default expose level, which is exactly the workflow this tool exists for
        // ("clear the key, re-run, confirm the default took effect").
        [AgentTool("Delete one EditorPrefs key, which resets it to whatever default the reading code passes. " +
                   "Reports state=missing when there was nothing to delete, so 'already default' and 'just reset' " +
                   "stay distinguishable. Affects this machine, not the project. " +
                   "Refuses keys whose name looks like a credential (apikey / token / password / secret / …), " +
                   "since the value cannot be read back to restore it.",
            Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string DeleteEditorPref(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Error: key is required.";
            if (LooksLikeSecret(key))
                return $"Error: refusing to delete '{key}' — the key name looks like a credential and the value " +
                       "cannot be recovered afterwards. Clear it through the owning package's own settings UI.";

            if (!EditorPrefs.HasKey(key))
                return $"key={key} state=missing (nothing to delete; it was already at its default)";

            try
            {
                EditorPrefs.DeleteKey(key);
                return $"deleted: key={key} (now reads as the caller's defaultValue)";
            }
            catch (Exception e)
            {
                return $"Error: failed to delete '{key}': {e.Message}";
            }
        }

        // ── internals ────────────────────────────────────────────────────────

        private static bool TryParseType(string type, out string normalized)
        {
            switch ((type ?? "string").Trim().ToLowerInvariant())
            {
                case "":
                case "string": normalized = "string"; return true;
                case "bool":
                case "boolean": normalized = "bool"; return true;
                case "int":
                case "integer": normalized = "int"; return true;
                case "float":
                case "single": normalized = "float"; return true;
                default: normalized = null; return false;
            }
        }

        private static string TypeError(string type)
            => $"Error: unknown type '{type}'. Use string | bool | int | float.";

        private static bool LooksLikeSecret(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            var words = SplitWords(key);
            foreach (var w in words)
                if (Array.IndexOf(SecretWords, w) >= 0) return true;

            for (int i = 0; i + 1 < words.Count; i++)
                if (Array.IndexOf(SecretWordPairs, words[i] + words[i + 1]) >= 0) return true;

            return false;
        }

        /// <summary>
        /// Splits an EditorPrefs key into lowercase words on separators and camelCase boundaries,
        /// so "UnityAgent_ClaudeApiKey" becomes [unity, agent, claude, api, key] and "APIKey"
        /// becomes [api, key].
        /// </summary>
        private static List<string> SplitWords(string key)
        {
            var words = new List<string>();
            var cur = new StringBuilder();

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!char.IsLetterOrDigit(c))
                {
                    FlushWord(words, cur);
                    continue;
                }

                if (cur.Length > 0 && char.IsUpper(c))
                {
                    // cur is non-empty, so key[i - 1] is the alphanumeric char that fed it.
                    char prev = key[i - 1];
                    bool afterLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                    bool lastOfAcronym = char.IsUpper(prev)
                                         && i + 1 < key.Length && char.IsLower(key[i + 1]);
                    if (afterLowerOrDigit || lastOfAcronym) FlushWord(words, cur);
                }

                cur.Append(char.ToLowerInvariant(c));
            }

            FlushWord(words, cur);
            return words;
        }

        private static void FlushWord(List<string> words, StringBuilder cur)
        {
            if (cur.Length == 0) return;
            words.Add(cur.ToString());
            cur.Length = 0;
        }

        /// <summary>
        /// Reads the raw registry value names backing EditorPrefs.
        /// Microsoft.Win32.Registry is not in the netstandard surface this assembly compiles
        /// against, so the type is resolved at runtime out of the Mono BCL instead of referenced.
        /// </summary>
        private static bool TryEnumerateValueNames(out List<string> names, out string error)
        {
            names = null;

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                error = $"listing EditorPrefs keys is Windows-only (current platform: {Application.platform}). " +
                        "Unity provides no API to enumerate EditorPrefs, and only the Windows backing store " +
                        "(the registry) can be read directly. GetEditorPref / SetEditorPref / DeleteEditorPref " +
                        "work on every platform.";
                return false;
            }

            var registryType = ResolveType("Microsoft.Win32.Registry");
            if (registryType == null)
            {
                error = "Microsoft.Win32.Registry is not available in this runtime, so the EditorPrefs " +
                        "backing store cannot be enumerated.";
                return false;
            }

            object hkcu = registryType.GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Static)
                                      ?.GetValue(null);
            if (hkcu == null)
            {
                error = "Microsoft.Win32.Registry.CurrentUser could not be read.";
                return false;
            }

            var keyType = hkcu.GetType();
            var openSubKey = keyType.GetMethod("OpenSubKey", new[] { typeof(string) });
            if (openSubKey == null)
            {
                error = "RegistryKey.OpenSubKey(string) not found (unexpected BCL shape).";
                return false;
            }

            object subKey;
            try
            {
                subKey = openSubKey.Invoke(hkcu, new object[] { RegistryPath });
            }
            catch (Exception e)
            {
                error = $"opening HKCU\\{RegistryPath} failed: {e.InnerException?.Message ?? e.Message}";
                return false;
            }

            if (subKey == null)
            {
                error = $"registry key HKCU\\{RegistryPath} does not exist. " +
                        "Unity may not have written any EditorPrefs on this machine yet.";
                return false;
            }

            try
            {
                var getValueNames = keyType.GetMethod("GetValueNames", Type.EmptyTypes);
                if (!(getValueNames?.Invoke(subKey, null) is string[] raw))
                {
                    error = "RegistryKey.GetValueNames() returned nothing usable.";
                    return false;
                }
                names = new List<string>(raw);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"reading value names failed: {e.InnerException?.Message ?? e.Message}";
                return false;
            }
            finally
            {
                (subKey as IDisposable)?.Dispose();
            }
        }

        private static Type ResolveType(string fullName)
        {
            // Mono's .NET Framework profile keeps this in mscorlib; the split BCL puts it in an
            // assembly of the same name, which may not be loaded yet — hence the explicit Load.
            var t = Type.GetType(fullName + ", mscorlib") ?? Type.GetType(fullName);
            if (t != null) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }

            try { t = Assembly.Load(fullName)?.GetType(fullName); }
            catch { t = null; }
            return t;
        }
    }
}
