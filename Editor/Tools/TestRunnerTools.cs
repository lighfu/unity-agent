using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Public AgentTools that allow external CI/scripts (or AI itself, with caution)
    /// to drive UnityAgent programmatically: create test sessions, send prompts,
    /// inspect state, switch models, capture console logs, discard.
    /// All tools return user-facing strings (or JSON for SendTestPrompt).
    /// </summary>
    public static class TestRunnerTools
    {
        // ─── 1. StartTestSession ───
        [AgentTool("Create an isolated test chat session for programmatic AI control. " +
            "providerId/modelId fall back to global UnityAgent settings if empty. " +
            "Returns 'sess_xxxxxxxx' id used by SendTestPrompt / GetSessionState / SwitchModel / DiscardTestSession. " +
            "Max 4 concurrent test sessions. Session label is prefixed with [TEST] in history.",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Caution)]
        public static string StartTestSession(string sessionLabel = "", string providerId = "", string modelId = "")
        {
            try
            {
                string id = TestRunnerCore.CreateSession(sessionLabel, providerId, modelId);
                var ctx = TestRunnerCore.GetSession(id);
                return $"TestSession {id} created (label={ctx.Label}, provider={ctx.ProviderId}, model={ctx.ModelId}). Use SendTestPrompt('{id}', ...) to send messages.";
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        // ─── 2. SendTestPrompt — IEnumerator yield pattern ───
        [AgentTool("Send a prompt to a test session and synchronously wait (via editor coroutine yield) for AI completion. " +
            "Returns JSON: {completed,text,toolCalls[],tokens:{input,output,cached,estCostUsd},consoleLogs[],durationMs,error?}. " +
            "timeoutSec (default 120) bounds the wait — on timeout returns {completed:false,error:'Timeout...'}. " +
            "captureConsoleLogs=true (default) collects Unity Console entries during the turn. " +
            "Recursion-protected: only sessions created via StartTestSession can receive prompts.",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Caution)]
        public static IEnumerator SendTestPrompt(string sessionId, string prompt, int timeoutSec = 120, bool captureConsoleLogs = true)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                yield return "Error: prompt is empty.";
                yield break;
            }

            // TestRunnerCore.SendPromptBlocking is itself IEnumerator. Forward all yields.
            // Pre-checks (session existence, IsProcessing) throw — capture error message and yield outside try/catch (CS1631).
            IEnumerator inner = null;
            string startError = null;
            try
            {
                inner = TestRunnerCore.SendPromptBlocking(sessionId, prompt, timeoutSec, captureConsoleLogs);
            }
            catch (Exception ex) { startError = ex.Message; }
            if (startError != null)
            {
                yield return "Error: " + startError;
                yield break;
            }

            // Drive inner enumerator and forward its yielded values.
            // CS1631: cannot yield in catch — capture error to local and yield outside.
            while (true)
            {
                bool hasMore = false;
                object current = null;
                string moveError = null;
                try
                {
                    hasMore = inner.MoveNext();
                    if (hasMore) current = inner.Current;
                }
                catch (Exception ex) { moveError = ex.Message; }
                if (moveError != null)
                {
                    yield return "Error during SendTestPrompt: " + moveError;
                    yield break;
                }
                if (!hasMore) yield break;
                yield return current;
            }
        }

        // ─── 3. GetSessionState ───
        [AgentTool("Read-only inspection of a test session: message count, processing flag, last error, provider/model, label, age. ",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Safe)]
        public static string GetSessionState(string sessionId)
        {
            try
            {
                var ctx = TestRunnerCore.GetSession(sessionId);
                int total = ctx.Core.GetHistory().Count;
                var sb = new StringBuilder();
                sb.Append($"{ctx.SessionId}: messages={total}, processing={ctx.IsProcessing}, ");
                sb.Append($"lastError={(ctx.LastError ?? "null")}, ");
                sb.Append($"provider={ctx.ProviderId}, model={ctx.ModelId}, label='{ctx.Label}', ");
                sb.Append($"age={(DateTime.UtcNow - ctx.CreatedAt).ToString(@"hh\:mm\:ss")}");
                return sb.ToString();
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        // ─── 4. GetConsoleLogs ───
        [AgentTool("Get recent Unity Console entries (rolling buffer max 1000). " +
            "sinceLastSeconds: lookback window (default 60). minLevel: 'log' | 'warning' | 'error' (default 'warning'). " +
            "Independent of SendTestPrompt's per-turn capture — works any time.",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Safe)]
        public static string GetConsoleLogs(int sinceLastSeconds = 60, string minLevel = "warning")
        {
            try
            {
                var entries = TestRunnerCore.GetRecentConsoleLogs(sinceLastSeconds, minLevel);
                var sb = new StringBuilder();
                sb.AppendLine($"Console logs (last {sinceLastSeconds}s, level >= {minLevel}): {entries.Count} entries");
                if (entries.Count > 0) sb.AppendLine("---");
                foreach (var e in entries)
                {
                    sb.AppendLine($"[{e.Level}][{e.Timestamp}] {e.Message}");
                }
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        // ─── 5. SwitchModel ───
        [AgentTool("Switch the AI provider/model on an existing test session. Conversation history is preserved (RestoreHistory on a fresh UnityAgentCore). Errors if API key missing.",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Caution)]
        public static string SwitchModel(string sessionId, string providerId, string modelId)
        {
            try
            {
                var ctx = TestRunnerCore.GetSession(sessionId);
                string oldP = ctx.ProviderId, oldM = ctx.ModelId;

                // Snapshot history from old core
                var historySnapshot = new List<Message>(ctx.Core.GetHistory());

                // Build new core with new provider/model
                var newCore = UnityAgentCore.CreateProgrammaticInstance(providerId, modelId);
                newCore.RestoreHistory(historySnapshot);

                // Swap (no Cancel on old — we just drop the reference; history lives on newCore now)
                ctx.Core = newCore;
                ctx.ProviderId = providerId;
                ctx.ModelId = modelId;
                return $"{ctx.SessionId} model changed: {oldP}/{oldM} → {providerId}/{modelId}";
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        // ─── 6. DiscardTestSession ───
        [AgentTool("Discard a test session and free its slot in MAX_CONCURRENT counter. " +
            "deleteHistoryFile=true: also delete the persisted JSON if any (currently not yet wired — kept for forward compatibility).",
            Author = "ajisaiflow", Category = "TestRunner", Risk = ToolRisk.Caution)]
        public static string DiscardTestSession(string sessionId, bool deleteHistoryFile = false)
        {
            try
            {
                var ctx = TestRunnerCore.GetSession(sessionId);
                string path = ctx.HistoryFilePath;
                TestRunnerCore.DiscardSession(sessionId, deleteHistoryFile);
                string fileMsg = deleteHistoryFile && !string.IsNullOrEmpty(path)
                    ? $"history file deleted ({path})"
                    : (string.IsNullOrEmpty(path) ? "no persistent history file" : $"history file kept at {path}");
                return $"{sessionId} discarded ({fileMsg})";
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }
    }
}
