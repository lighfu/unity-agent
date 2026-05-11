using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Test session manager for programmatic UnityAgent control via TestRunnerTools.
    /// Owns Dictionary<sessionId, TestSessionContext>. Designed to be called from
    /// the editor main thread (MCP Invoker dispatches there).
    /// SendPromptBlocking returns IEnumerator and uses Unity's editor coroutine yield
    /// pattern — no worker threads or ManualResetEvent (matches AgentMCPServer.Invoker.RunAsyncTool).
    /// </summary>
    internal static class TestRunnerCore
    {
        public const int MAX_CONCURRENT = 4;
        public const string SESSION_PREFIX = "sess_";
        private const int GLOBAL_LOG_BUFFER_MAX = 1000;

        private static readonly Dictionary<string, TestSessionContext> _sessions = new Dictionary<string, TestSessionContext>();
        private static readonly object _sessionsLock = new object();

        // ─── Global rolling console log buffer (independent of per-turn capture) ───
        private static readonly List<ConsoleEntry> _globalLogs = new List<ConsoleEntry>();
        private static readonly object _globalLogsLock = new object();
        private static bool _globalHookInstalled = false;

        // ════════════════════════════════════════════════════════════
        //  Session lifecycle
        // ════════════════════════════════════════════════════════════
        public static string CreateSession(string label, string providerId, string modelId)
        {
            lock (_sessionsLock)
            {
                if (_sessions.Count >= MAX_CONCURRENT)
                    throw new InvalidOperationException($"Too many concurrent test sessions ({MAX_CONCURRENT} max). Discard some first.");

                string id = SESSION_PREFIX + Guid.NewGuid().ToString("N").Substring(0, 8);
                var ctx = new TestSessionContext
                {
                    SessionId = id,
                    Label = "[TEST] " + (string.IsNullOrEmpty(label) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : label),
                    ProviderId = providerId,
                    ModelId = modelId,
                    CreatedAt = DateTime.UtcNow,
                };
                ctx.Core = UnityAgentCore.CreateProgrammaticInstance(providerId, modelId);
                _sessions[id] = ctx;
                return id;
            }
        }

        public static TestSessionContext GetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !sessionId.StartsWith(SESSION_PREFIX))
                throw new ArgumentException($"Invalid session id format: '{sessionId}' (must start with '{SESSION_PREFIX}')");
            lock (_sessionsLock)
            {
                if (!_sessions.TryGetValue(sessionId, out var ctx))
                    throw new InvalidOperationException($"Test session '{sessionId}' not found or already discarded.");
                return ctx;
            }
        }

        public static void DiscardSession(string sessionId, bool deleteHistoryFile)
        {
            lock (_sessionsLock)
            {
                if (_sessions.TryGetValue(sessionId, out var ctx))
                {
                    try { ctx.Core?.Cancel(); } catch { /* ignore */ }
                    _sessions.Remove(sessionId);
                    if (deleteHistoryFile && !string.IsNullOrEmpty(ctx.HistoryFilePath) && System.IO.File.Exists(ctx.HistoryFilePath))
                    {
                        try { System.IO.File.Delete(ctx.HistoryFilePath); } catch { /* best-effort */ }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Sync prompt — IEnumerator yield pattern
        //  Caller (TestRunnerTools.SendTestPrompt) yield-returns this enumerator;
        //  Invoker.RunAsyncTool drives MoveNext per editor frame and captures the
        //  final yielded string as the MCP result.
        // ════════════════════════════════════════════════════════════
        public static IEnumerator SendPromptBlocking(string sessionId, string prompt, int timeoutSec, bool captureConsoleLogs)
        {
            // ── Pre-checks (these throw, but Invoker catches and returns Error) ──
            var ctx = GetSession(sessionId);
            if (ctx.IsProcessing)
                throw new InvalidOperationException($"Session '{sessionId}' is already processing a prompt.");

            // ── Per-turn console log capture ──
            List<ConsoleEntry> turnLogs = captureConsoleLogs ? new List<ConsoleEntry>() : null;
            Application.LogCallback turnLogHandler = null;
            if (captureConsoleLogs)
            {
                turnLogHandler = (logString, stackTrace, type) =>
                {
                    lock (turnLogs)
                    {
                        turnLogs.Add(new ConsoleEntry
                        {
                            Level = type.ToString(),
                            Message = logString,
                            StackTrace = stackTrace,
                            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                            At = DateTime.Now,
                        });
                    }
                };
                Application.logMessageReceivedThreaded += turnLogHandler;
            }

            // ── Subscribe to OnTurnComplete ──
            TurnResult capturedResult = null;
            Action<TurnResult> onComplete = (r) => { capturedResult = r; };
            ctx.Core.OnTurnComplete += onComplete;
            ctx.IsProcessing = true;

            var turnSw = Stopwatch.StartNew();
            long timeoutMs = timeoutSec * 1000L;

            // ── Start AI processing as a separate editor coroutine ──
            bool startFailed = false;
            string startError = null;
            try
            {
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    ctx.Core.ProcessUserQuery(prompt, null, null, null, null, null));
            }
            catch (Exception ex)
            {
                startFailed = true;
                startError = ex.Message;
            }

            if (startFailed)
            {
                ctx.Core.OnTurnComplete -= onComplete;
                if (turnLogHandler != null) Application.logMessageReceivedThreaded -= turnLogHandler;
                ctx.IsProcessing = false;
                yield return $"{{\"completed\":false,\"error\":\"Failed to start ProcessUserQuery: {EscapeJson(startError)}\"}}";
                yield break;
            }

            // ── Yield-poll until completion or timeout ──
            while (capturedResult == null && turnSw.ElapsedMilliseconds < timeoutMs)
            {
                yield return null;
            }

            // ── Cleanup ──
            ctx.Core.OnTurnComplete -= onComplete;
            if (turnLogHandler != null) Application.logMessageReceivedThreaded -= turnLogHandler;
            ctx.IsProcessing = false;

            // ── Build final result ──
            TurnResult finalResult;
            if (capturedResult == null)
            {
                finalResult = new TurnResult
                {
                    Completed = false,
                    Error = $"Timeout after {timeoutSec}s",
                    DurationMs = turnSw.ElapsedMilliseconds,
                };
            }
            else
            {
                finalResult = capturedResult;
                if (turnLogs != null) finalResult.ConsoleLogs = turnLogs;
            }

            yield return FormatTurnResultJson(finalResult);
        }

        // ════════════════════════════════════════════════════════════
        //  Console log helpers
        // ════════════════════════════════════════════════════════════
        public static List<ConsoleEntry> GetRecentConsoleLogs(int sinceLastSeconds, string minLevel)
        {
            EnsureGlobalConsoleHookActive();
            int minLvl = LevelStringToInt(minLevel);
            DateTime cutoff = DateTime.Now.AddSeconds(-sinceLastSeconds);
            lock (_globalLogsLock)
            {
                var result = new List<ConsoleEntry>();
                foreach (var e in _globalLogs)
                {
                    if (e.At < cutoff) continue;
                    if (LevelToInt(e.Level) >= minLvl) result.Add(e);
                }
                return result;
            }
        }

        private static int LevelStringToInt(string s)
        {
            switch (s?.ToLowerInvariant())
            {
                case "log":
                case "info":
                    return 0;
                case "warning":
                    return 1;
                case "error":
                    return 2;
                default:
                    return 1;
            }
        }

        private static int LevelToInt(string level)
        {
            if (level == "Error" || level == "Exception" || level == "Assert") return 2;
            if (level == "Warning") return 1;
            return 0;
        }

        private static void EnsureGlobalConsoleHookActive()
        {
            if (_globalHookInstalled) return;
            _globalHookInstalled = true;
            Application.logMessageReceivedThreaded += (logString, stackTrace, type) =>
            {
                lock (_globalLogsLock)
                {
                    _globalLogs.Add(new ConsoleEntry
                    {
                        Level = type.ToString(),
                        Message = logString,
                        StackTrace = stackTrace,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        At = DateTime.Now,
                    });
                    if (_globalLogs.Count > GLOBAL_LOG_BUFFER_MAX) _globalLogs.RemoveAt(0);
                }
            };
        }

        // ════════════════════════════════════════════════════════════
        //  JSON formatting
        // ════════════════════════════════════════════════════════════
        public static string FormatTurnResultJson(TurnResult r)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"completed\":").Append(r.Completed ? "true" : "false");
            sb.Append(",\"text\":").Append(EscapeJsonQuoted(r.Text ?? ""));
            sb.Append(",\"toolCalls\":[");
            for (int i = 0; i < r.ToolCalls.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var tc = r.ToolCalls[i];
                sb.Append("{\"name\":").Append(EscapeJsonQuoted(tc.Name ?? ""));
                sb.Append(",\"args\":").Append(string.IsNullOrEmpty(tc.ArgsJson) ? "{}" : tc.ArgsJson);
                sb.Append(",\"result\":").Append(EscapeJsonQuoted(tc.Result ?? ""));
                sb.Append(",\"durationMs\":").Append(tc.DurationMs);
                sb.Append("}");
            }
            sb.Append("],\"tokens\":{");
            sb.Append("\"input\":").Append(r.InputTokens);
            sb.Append(",\"output\":").Append(r.OutputTokens);
            sb.Append(",\"cached\":").Append(r.CachedTokens);
            sb.Append(",\"estCostUsd\":").Append(r.EstimatedCostUsd.ToString("F4"));
            sb.Append("}");
            if (r.ConsoleLogs != null && r.ConsoleLogs.Count > 0)
            {
                sb.Append(",\"consoleLogs\":[");
                for (int i = 0; i < r.ConsoleLogs.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var l = r.ConsoleLogs[i];
                    sb.Append("{\"level\":").Append(EscapeJsonQuoted(l.Level));
                    sb.Append(",\"message\":").Append(EscapeJsonQuoted(l.Message));
                    sb.Append(",\"timestamp\":").Append(EscapeJsonQuoted(l.Timestamp));
                    sb.Append("}");
                }
                sb.Append("]");
            }
            sb.Append(",\"durationMs\":").Append(r.DurationMs);
            if (!string.IsNullOrEmpty(r.Error))
                sb.Append(",\"error\":").Append(EscapeJsonQuoted(r.Error));
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJsonQuoted(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string EscapeJson(string s) =>
            EscapeJsonQuoted(s).Trim('"');  // remove outer quotes for inline use

        public static int ActiveSessionCount
        {
            get { lock (_sessionsLock) return _sessions.Count; }
        }
    }

    internal class TestSessionContext
    {
        public string SessionId;
        public string Label;
        public string ProviderId;
        public string ModelId;
        public DateTime CreatedAt;
        public UnityAgentCore Core;
        public bool IsProcessing;
        public string LastError;
        public string HistoryFilePath;
    }
}
