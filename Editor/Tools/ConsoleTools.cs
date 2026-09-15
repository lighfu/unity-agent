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
                   "sinceIndex: return only entries NEWER than this console index (default -1 = all). " +
                   "regex: .NET regular expression, matched case-insensitively against the entry's FIRST line " +
                   "(the text the Console window shows on the row) — not the stack trace. Combined with keyword as AND. " +
                   "An invalid pattern returns 'Error:', never an empty result, so a broken filter can never be " +
                   "mistaken for a clean console. " +
                   "Every response ends with 'nextSinceIndex=N' — pass that back on the next call to see " +
                   "only what appeared since, instead of trying to tell old errors from new ones by eye. " +
                   "Indices reset when the console is cleared, so treat a smaller total than expected as a reset. " +
                   "Returns newest entries last, matching the Console window order.")]
        public static string GetConsoleLogs(
            string severity = "all",
            int maxEntries = 50,
            string keyword = "",
            bool includeStackTrace = false,
            int sinceIndex = -1,
            string regex = "")
        {
            if (maxEntries <= 0) maxEntries = 50;
            if (maxEntries > 500) maxEntries = 500;

            int severityMask = ParseSeverityMask(severity);
            if (severityMask == 0)
                return $"Error: unknown severity '{severity}'. Use all | error | warning | info.";

            if (!TryCompileFilter(regex, out Regex rx, out string rxError))
                return rxError;

            if (!TryGetLogEntriesReflection(out var refl, out string err))
                return $"Error: {err}";

            string kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.ToLowerInvariant();

            refl.StartGettingEntries.Invoke(null, null);
            try
            {
                int total = (int)refl.GetCount.Invoke(null, null);
                if (total == 0) return "Console is empty. nextSinceIndex=-1";

                // Indices restart whenever the console is cleared — which "Clear on Recompile"
                // does on every script compile, exactly when a caller is polling with sinceIndex
                // to see new compile errors. Without this check every entry looks old and three
                // fresh errors get reported as a clean build.
                bool indexReset = sinceIndex >= 0 && total <= sinceIndex;
                if (indexReset) sinceIndex = -1;

                var rows = new List<ConsoleRow>(Math.Min(maxEntries, total));
                int errorCount = 0, warnCount = 0, infoCount = 0;

                for (int i = 0; i < total; i++)
                {
                    // Severity counts stay whole-console so the header still answers
                    // "is the project broken right now", independent of the since window.
                    if (sinceIndex >= 0 && i <= sinceIndex)
                    {
                        var skipped = Activator.CreateInstance(refl.LogEntryType);
                        refl.GetEntryInternal.Invoke(null, new object[] { i, skipped });
                        string skippedSev = ClassifySeverity((int)refl.ModeField.GetValue(skipped));
                        if (skippedSev == "error") errorCount++;
                        else if (skippedSev == "warning") warnCount++;
                        else infoCount++;
                        continue;
                    }

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
                    // keyword scans the whole entry (stack trace included, unchanged behaviour);
                    // regex deliberately sees only the row text, so '$' and '^' anchor to the
                    // message the way the caller reads it in the Console window.
                    if (rx != null && !rx.IsMatch(FirstLine(message ?? "")))
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
                if (indexReset)
                    sb.AppendLine("NOTE: the console was cleared since your last call (entry count went backwards). " +
                                  "sinceIndex was ignored and ALL current entries are shown.");
                sb.Append($"=== Console ({total} total | errors={errorCount}, warnings={warnCount}, info={infoCount}");
                if (sinceIndex >= 0) sb.Append($" | since #{sinceIndex}");
                if (rows.Count != total) sb.Append($" | filtered {rows.Count}");
                if (kept != rows.Count) sb.Append($" | showing last {kept}");
                sb.AppendLine(") ===");

                if (sinceIndex >= 0 && rows.Count == 0)
                    sb.AppendLine($"(no new entries since #{sinceIndex})");

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

                sb.Append($"nextSinceIndex={total - 1}");
                return sb.ToString();
            }
            finally
            {
                refl.EndGettingEntries.Invoke(null, null);
            }
        }

        [AgentTool("Count Unity Console entries by severity. Returns 'errors=N, warnings=N, info=N, total=N'. " +
                   "Quick no-payload check for build / compile status before running a more expensive query. " +
                   "keyword: case-insensitive substring filter (optional). " +
                   "regex: .NET regular expression matched case-insensitively against each entry's first line (optional); " +
                   "combined with keyword as AND. An invalid pattern returns 'Error:' rather than a count of 0, " +
                   "so a broken filter cannot be read as 'nothing wrong'. " +
                   "With a filter active the matched counts are returned plus the unfiltered console total, " +
                   "which is what you want for 'how many warnings did MY plugin emit'.")]
        public static string CountConsoleLogs(string keyword = "", string regex = "")
        {
            if (!TryCompileFilter(regex, out Regex rx, out string rxError))
                return rxError;

            string kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.ToLowerInvariant();
            if (!TryCountBySeverity(kw, rx, out var counts, out string err))
                return $"Error: {err}";

            string line = $"errors={counts.errors}, warnings={counts.warnings}, info={counts.infos}, total={counts.matched}";
            if (kw == null && rx == null) return line;
            return line + $" (filtered; console total={counts.total}, exceptions among matches={counts.exceptions})";
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

        /// <summary>
        /// Current console entry count, or -1 if the internal API is unavailable.
        /// Lets other tools take a "before" marker to pass back as GetConsoleLogs(sinceIndex:).
        /// </summary>
        internal static int GetEntryCount()
        {
            if (!TryGetLogEntriesReflection(out var refl, out _)) return -1;
            refl.StartGettingEntries.Invoke(null, null);
            try { return (int)refl.GetCount.Invoke(null, null); }
            finally { refl.EndGettingEntries.Invoke(null, null); }
        }

        /// <summary>
        /// Severity tallies over the console, optionally restricted to entries matching a filter.
        /// <c>total</c> is always the whole console; <c>matched</c> is what passed the filter
        /// (equal to <c>total</c> when no filter is given).
        /// </summary>
        internal struct ConsoleCounts
        {
            public int total;
            public int matched;
            public int errors;
            public int warnings;
            public int infos;
            public int exceptions;
        }

        /// <summary>Unfiltered severity tallies. Used by tools that need a before/after snapshot.</summary>
        internal static bool TryCountBySeverity(out ConsoleCounts counts, out string error)
            => TryCountBySeverity(null, null, out counts, out error);

        internal static bool TryCountBySeverity(string lowerKeyword, Regex rx, out ConsoleCounts counts, out string error)
            => TryCountBySeverity(lowerKeyword, rx, -1, out counts, out error);

        /// <summary>
        /// Same, restricted to entries NEWER than <paramref name="sinceIndex"/> — the meaning
        /// GetConsoleLogs gives it; -1 counts everything. <c>total</c> stays the whole console.
        /// </summary>
        internal static bool TryCountBySeverity(string lowerKeyword, Regex rx, int sinceIndex, out ConsoleCounts counts, out string error)
        {
            counts = default;
            if (!TryGetLogEntriesReflection(out var refl, out error)) return false;

            refl.StartGettingEntries.Invoke(null, null);
            try
            {
                int total = (int)refl.GetCount.Invoke(null, null);
                counts.total = total;

                for (int i = Math.Max(0, sinceIndex + 1); i < total; i++)
                {
                    var entry = Activator.CreateInstance(refl.LogEntryType);
                    refl.GetEntryInternal.Invoke(null, new object[] { i, entry });
                    int mode = (int)refl.ModeField.GetValue(entry);

                    if (lowerKeyword != null || rx != null)
                    {
                        string message = (string)refl.MessageField.GetValue(entry) ?? "";
                        if (lowerKeyword != null
                            && message.ToLowerInvariant().IndexOf(lowerKeyword, StringComparison.Ordinal) < 0)
                            continue;
                        if (rx != null && !rx.IsMatch(FirstLine(message)))
                            continue;
                    }

                    counts.matched++;
                    string sev = ClassifySeverity(mode);
                    if (sev == "error") counts.errors++;
                    else if (sev == "warning") counts.warnings++;
                    else counts.infos++;
                    if ((mode & ModeScriptingException) != 0) counts.exceptions++;
                }

                error = null;
                return true;
            }
            finally
            {
                refl.EndGettingEntries.Invoke(null, null);
            }
        }

        /// <summary>
        /// Index of the newest entry whose message contains <paramref name="text"/> (ordinal), or
        /// -1 when none does or the console cannot be read. A tool that logs a unique marker line
        /// can find where its own entries start later on. A before/after count cannot do that: a
        /// clear followed by enough new lines leaves the total higher than before, and the clear
        /// goes unnoticed. A cleared marker is simply gone, which is the signal.
        /// </summary>
        internal static int FindLastEntryContaining(string text)
        {
            if (string.IsNullOrEmpty(text) || !TryGetLogEntriesReflection(out var refl, out _)) return -1;

            refl.StartGettingEntries.Invoke(null, null);
            try
            {
                int total = (int)refl.GetCount.Invoke(null, null);
                var entry = Activator.CreateInstance(refl.LogEntryType);
                for (int i = total - 1; i >= 0; i--)
                {
                    refl.GetEntryInternal.Invoke(null, new object[] { i, entry });
                    string message = (string)refl.MessageField.GetValue(entry);
                    if (message != null && message.IndexOf(text, StringComparison.Ordinal) >= 0) return i;
                }
                return -1;
            }
            finally
            {
                refl.EndGettingEntries.Invoke(null, null);
            }
        }

        // ── internals ────────────────────────────────────────────────────────

        /// <summary>
        /// Compiles the caller-supplied pattern. A malformed pattern must surface as an error:
        /// silently treating it as "no matches" would let a caller gating on the count read a
        /// broken filter as a clean console.
        /// </summary>
        private static bool TryCompileFilter(string pattern, out Regex rx, out string error)
        {
            rx = null;
            error = null;
            if (string.IsNullOrWhiteSpace(pattern)) return true;

            try
            {
                rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = $"Error: invalid regex '{pattern}': {ex.Message}";
                return false;
            }
        }

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
