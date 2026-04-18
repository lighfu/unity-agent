using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Access to Unity's Console window (Debug.Log / warnings / errors, including compile
    /// errors and asset-import diagnostics). Uses reflection against
    /// <c>UnityEditor.LogEntries</c> / <c>UnityEditor.LogEntry</c>, which are internal
    /// Unity APIs — the shape has been stable since 2019 but is not an official contract.
    /// </summary>
    public static class ConsoleTools
    {
        // Unity LogEntry.mode bit flags (from editor source).
        private const int ModeError = 1 << 0;
        private const int ModeAssert = 1 << 1;
        private const int ModeLog = 1 << 2;
        private const int ModeFatal = 1 << 4;
        private const int ModeAssetImportError = 1 << 6;
        private const int ModeAssetImportWarning = 1 << 7;
        private const int ModeScriptingError = 1 << 8;
        private const int ModeScriptingWarning = 1 << 9;
        private const int ModeScriptingLog = 1 << 10;
        private const int ModeScriptCompileError = 1 << 11;
        private const int ModeScriptCompileWarning = 1 << 12;
        private const int ModeScriptingException = 1 << 20;

        private const int ErrorMask = ModeError | ModeFatal | ModeAssetImportError
            | ModeScriptingError | ModeScriptCompileError | ModeScriptingException | ModeAssert;
        private const int WarningMask = ModeAssetImportWarning | ModeScriptingWarning
            | ModeScriptCompileWarning;
        private const int InfoMask = ModeLog | ModeScriptingLog;

        [AgentTool("Read recent Unity Console entries (Debug.Log, warnings, errors, compile errors, asset-import diagnostics). " +
                   "severity: 'all' (default) | 'error' | 'warning' | 'info'. " +
                   "maxEntries: cap on returned rows (default 50, max 500). " +
                   "keyword: case-insensitive substring filter on the message (optional). " +
                   "includeStackTrace: include the callstack attached to each entry (default false). " +
                   "Returns newest entries last, matching the Console window order.")]
        public static string GetConsoleLogs(
            string severity = "all",
            int maxEntries = 50,
            string keyword = "",
            bool includeStackTrace = false)
        {
            if (maxEntries <= 0) maxEntries = 50;
            if (maxEntries > 500) maxEntries = 500;

            int severityMask = ParseSeverityMask(severity);
            if (severityMask == 0)
                return $"Error: unknown severity '{severity}'. Use all | error | warning | info.";

            if (!TryGetLogEntriesReflection(out var refl, out string err))
                return $"Error: {err}";

            string kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.ToLowerInvariant();

            refl.StartGettingEntries.Invoke(null, null);
            try
            {
                int total = (int)refl.GetCount.Invoke(null, null);
                if (total == 0) return "Console is empty.";

                var rows = new List<ConsoleRow>(Math.Min(maxEntries, total));
                int errorCount = 0, warnCount = 0, infoCount = 0;

                for (int i = 0; i < total; i++)
                {
                    var entry = Activator.CreateInstance(refl.LogEntryType);
                    refl.GetEntryInternal.Invoke(null, new object[] { i, entry });

                    int mode = (int)refl.ModeField.GetValue(entry);
                    string message = (string)refl.MessageField.GetValue(entry);
                    string file = refl.FileField != null ? (string)refl.FileField.GetValue(entry) : "";
                    int line = refl.LineField != null ? (int)refl.LineField.GetValue(entry) : 0;

                    string sev = ClassifySeverity(mode);
                    if (sev == "error") errorCount++;
                    else if (sev == "warning") warnCount++;
                    else infoCount++;

                    if ((mode & severityMask) == 0) continue;
                    if (kw != null && (message ?? "").ToLowerInvariant().IndexOf(kw, StringComparison.Ordinal) < 0)
                        continue;

                    rows.Add(new ConsoleRow
                    {
                        index = i,
                        severity = sev,
                        mode = mode,
                        message = message ?? "",
                        file = file ?? "",
                        line = line,
                    });
                }

                // Newest entries last → slice the tail up to maxEntries.
                int start = Math.Max(0, rows.Count - maxEntries);
                int kept = rows.Count - start;

                var sb = new StringBuilder();
                sb.Append($"=== Console ({total} total | errors={errorCount}, warnings={warnCount}, info={infoCount}");
                if (rows.Count != total) sb.Append($" | filtered {rows.Count}");
                if (kept != rows.Count) sb.Append($" | showing last {kept}");
                sb.AppendLine(") ===");

                for (int i = start; i < rows.Count; i++)
                {
                    var r = rows[i];
                    string prefix = r.severity == "error" ? "E"
                                  : r.severity == "warning" ? "W"
                                  : "I";
                    string firstLine = FirstLine(r.message);
                    sb.Append($"[{prefix}] #{r.index} ");
                    if (!string.IsNullOrEmpty(r.file) && r.line > 0)
                        sb.Append($"{ShortenPath(r.file)}:{r.line}  ");
                    sb.AppendLine(firstLine);

                    if (includeStackTrace)
                    {
                        // LogEntry.message is "<first line>\n<stack trace>". Split and indent.
                        int nl = r.message.IndexOf('\n');
                        if (nl >= 0 && nl < r.message.Length - 1)
                        {
                            string rest = r.message.Substring(nl + 1).TrimEnd();
                            foreach (var stackLine in rest.Split('\n'))
                                sb.AppendLine("    " + stackLine.TrimEnd());
                        }
                    }
                }

                return sb.ToString();
            }
            finally
            {
                refl.EndGettingEntries.Invoke(null, null);
            }
        }

        [AgentTool("Count Unity Console entries by severity. Returns 'errors=N, warnings=N, info=N, total=N'. " +
                   "Quick no-payload check for build / compile status before running a more expensive query.")]
        public static string CountConsoleLogs()
        {
            if (!TryGetLogEntriesReflection(out var refl, out string err))
                return $"Error: {err}";

            refl.StartGettingEntries.Invoke(null, null);
            try
            {
                int total = (int)refl.GetCount.Invoke(null, null);
                int errorCount = 0, warnCount = 0, infoCount = 0;

                for (int i = 0; i < total; i++)
                {
                    var entry = Activator.CreateInstance(refl.LogEntryType);
                    refl.GetEntryInternal.Invoke(null, new object[] { i, entry });
                    int mode = (int)refl.ModeField.GetValue(entry);
                    string sev = ClassifySeverity(mode);
                    if (sev == "error") errorCount++;
                    else if (sev == "warning") warnCount++;
                    else infoCount++;
                }

                return $"errors={errorCount}, warnings={warnCount}, info={infoCount}, total={total}";
            }
            finally
            {
                refl.EndGettingEntries.Invoke(null, null);
            }
        }

        [AgentTool("Clear the Unity Console. Matches the Clear button in the Console window. " +
                   "Use before running a build / import step so the next GetConsoleLogs call shows only new diagnostics.")]
        public static string ClearConsole()
        {
            if (!TryGetLogEntriesReflection(out var refl, out string err))
                return $"Error: {err}";
            refl.Clear.Invoke(null, null);
            return "Console cleared.";
        }

        // ── internals ────────────────────────────────────────────────────────

        private class ConsoleRow
        {
            public int index;
            public string severity;
            public int mode;
            public string message;
            public string file;
            public int line;
        }

        private class LogEntriesReflection
        {
            public Type LogEntriesType;
            public Type LogEntryType;
            public MethodInfo StartGettingEntries;
            public MethodInfo EndGettingEntries;
            public MethodInfo GetCount;
            public MethodInfo GetEntryInternal;
            public MethodInfo Clear;
            public FieldInfo ModeField;
            public FieldInfo MessageField;
            public FieldInfo FileField;
            public FieldInfo LineField;
        }

        private static LogEntriesReflection _cached;

        private static bool TryGetLogEntriesReflection(out LogEntriesReflection refl, out string error)
        {
            if (_cached != null)
            {
                refl = _cached;
                error = null;
                return true;
            }

            refl = new LogEntriesReflection();
            var asm = typeof(EditorWindow).Assembly;
            refl.LogEntriesType = asm.GetType("UnityEditor.LogEntries");
            refl.LogEntryType = asm.GetType("UnityEditor.LogEntry");
            if (refl.LogEntriesType == null || refl.LogEntryType == null)
            {
                error = "UnityEditor.LogEntries / LogEntry not found in this Unity version.";
                return false;
            }

            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            refl.StartGettingEntries = refl.LogEntriesType.GetMethod("StartGettingEntries", BF);
            refl.EndGettingEntries = refl.LogEntriesType.GetMethod("EndGettingEntries", BF);
            refl.GetCount = refl.LogEntriesType.GetMethod("GetCount", BF);
            refl.GetEntryInternal = refl.LogEntriesType.GetMethod("GetEntryInternal", BF);
            refl.Clear = refl.LogEntriesType.GetMethod("Clear", BF);

            if (refl.StartGettingEntries == null || refl.EndGettingEntries == null
                || refl.GetCount == null || refl.GetEntryInternal == null || refl.Clear == null)
            {
                error = "Expected static methods missing on UnityEditor.LogEntries (Unity API changed?).";
                return false;
            }

            const BindingFlags IF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            refl.ModeField = refl.LogEntryType.GetField("mode", IF);
            refl.MessageField = refl.LogEntryType.GetField("message", IF);
            refl.FileField = refl.LogEntryType.GetField("file", IF);
            refl.LineField = refl.LogEntryType.GetField("line", IF);

            if (refl.ModeField == null || refl.MessageField == null)
            {
                error = "Expected 'mode' / 'message' fields missing on UnityEditor.LogEntry.";
                return false;
            }

            _cached = refl;
            error = null;
            return true;
        }

        private static int ParseSeverityMask(string severity)
        {
            switch ((severity ?? "all").Trim().ToLowerInvariant())
            {
                case "all": return ErrorMask | WarningMask | InfoMask;
                case "error":
                case "errors":
                    return ErrorMask;
                case "warning":
                case "warnings":
                case "warn":
                    return WarningMask;
                case "info":
                case "log":
                case "logs":
                    return InfoMask;
                default: return 0;
            }
        }

        private static string ClassifySeverity(int mode)
        {
            if ((mode & ErrorMask) != 0) return "error";
            if ((mode & WarningMask) != 0) return "warning";
            return "info";
        }

        private static string FirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            int nl = message.IndexOf('\n');
            return nl >= 0 ? message.Substring(0, nl) : message;
        }

        private static string ShortenPath(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            int idx = file.LastIndexOfAny(new[] { '/', '\\' });
            return idx >= 0 && idx < file.Length - 1 ? file.Substring(idx + 1) : file;
        }
    }
}
