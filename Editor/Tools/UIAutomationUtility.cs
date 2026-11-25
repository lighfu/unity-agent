using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Dispatch after the tool response, so callbacks opening a modal do not trap that response.</summary>
    [InitializeOnLoad]
    internal static class UIAutomationUtility
    {
        [Serializable]
        private sealed class ActionResult
        {
            public string Id;
            public string State = "queued";
            public string Verification = "not_performed";
            public string Error;
        }

        private const int MaxResults = 256;
        private const string SessionIndexKey = "UnityAgent.UIAction.Ids";
        private const string SessionKeyPrefix = "UnityAgent.UIAction.Result.";
        private static readonly object ResultLock = new object();
        private static readonly Dictionary<string, ActionResult> Results = new Dictionary<string, ActionResult>();
        private static readonly List<string> ResultOrder = new List<string>();

        internal static int PlaySession { get; private set; }

        static UIAutomationUtility()
        {
            EditorApplication.playModeStateChanged += _ => PlaySession++;
        }

        [InitializeOnLoadMethod]
        private static void RestoreResults()
        {
            lock (ResultLock)
            {
                string[] ids = SessionState.GetString(SessionIndexKey, "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string id in ids)
                {
                    string json = SessionState.GetString(SessionKeyPrefix + id, "");
                    if (string.IsNullOrEmpty(json)) continue;
                    var result = JsonUtility.FromJson<ActionResult>(json);
                    if (result?.Id != id) continue;
                    // The old delegates belonged to the unloaded domain. Only their receipts survive.
                    if (result.State == "queued") result.State = "not_dispatched";
                    else if (result.State == "running") result.State = "outcome_unknown";
                    Results.Add(id, result);
                    ResultOrder.Add(id);
                }
            }
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // The tool protocol uses integer instance IDs on supported editors. Unity 6.5 retains
        // the legacy bridge at runtime but marks direct source calls obsolete-as-error.
#if UNITY_6000_0_OR_NEWER
        private static readonly MethodInfo GetLegacyId = typeof(UnityEngine.Object).GetMethod("GetInstanceID", Type.EmptyTypes);
        private static readonly MethodInfo FindLegacyObject = typeof(EditorUtility).GetMethod("InstanceIDToObject", new[] { typeof(int) });
#endif
        internal static int InstanceId(UnityEngine.Object target)
        {
            if (target == null) return 0;
#if UNITY_6000_0_OR_NEWER
            if (GetLegacyId == null) throw new NotSupportedException("This editor no longer supports integer object IDs.");
            return (int)GetLegacyId.Invoke(target, null);
#else
            return target.GetInstanceID();
#endif
        }

        internal static UnityEngine.Object FindObject(int instanceId)
        {
#if UNITY_6000_0_OR_NEWER
            if (FindLegacyObject == null) throw new NotSupportedException("This editor no longer supports integer object lookup.");
            return FindLegacyObject.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        internal static EditorWindow ResolveWindow(string title, int windowInstanceId, int matchIndex, out string error)
        {
            error = null;
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null).OrderBy(w => UIAutomationUtility.InstanceId(w)).ToList();
            if (windowInstanceId != 0)
            {
                var byId = windows.FirstOrDefault(w => UIAutomationUtility.InstanceId(w) == windowInstanceId);
                if (byId == null) error = "Error: windowInstanceId is no longer loaded. Call ListUIAutomationTargets again.";
                else if (!string.IsNullOrEmpty(title) && Title(byId).IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error = "Error: windowInstanceId does not match windowTitleContains.";
                    return null;
                }
                return byId;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                error = "Error: specify windowInstanceId or windowTitleContains. Call ListUIAutomationTargets first.";
                return null;
            }
            var matches = windows.Where(w => Title(w).IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (matches.Count == 0) error = "Error: no loaded EditorWindow matches '" + title + "'.";
            else if (matchIndex < -1 || matchIndex >= matches.Count) error = "Error: matchIndex is out of range.";
            else if (matches.Count > 1 && matchIndex == -1)
                error = "Error: ambiguous window title. Use windowInstanceId or matchIndex: " +
                    string.Join(", ", matches.Select((w, i) => $"[{i}] {Title(w)} (windowInstanceId={UIAutomationUtility.InstanceId(w)})"));
            else return matches[matchIndex < 0 ? 0 : matchIndex];
            return null;
        }

        internal static string Title(EditorWindow window) => window.titleContent != null ? window.titleContent.text ?? "" : "";

        internal static string Queue(UnityEngine.Object target, Action action, Func<bool?> verify = null)
        {
            if (target == null) return "Error: the UI target was destroyed.";
            ActionResult result;
            lock (ResultLock)
            {
                if (Results.Count >= MaxResults)
                {
                    // Never lose the status of an action that has yet to finish.
                    string completed = ResultOrder.FirstOrDefault(id => Results[id].State != "queued" && Results[id].State != "running");
                    if (completed == null) return "Error: too many pending UI actions. Wait for existing actions to finish.";
                    Results.Remove(completed);
                    ResultOrder.Remove(completed);
                    SessionState.EraseString(SessionKeyPrefix + completed);
                }
                result = new ActionResult { Id = Guid.NewGuid().ToString("N") };
                SaveResult(result);
                Results.Add(result.Id, result);
                ResultOrder.Add(result.Id);
                SessionState.SetString(SessionIndexKey, string.Join(",", ResultOrder));
            }
            // update is also pumped by batch mode and the Unity test runner. Registering during
            // an update does not enter its current delegate snapshot, so the tool response goes first.
            EditorApplication.CallbackFunction dispatch = null;
            dispatch = () =>
            {
                EditorApplication.update -= dispatch;
                SetResult(result, "running");
                try
                {
                    if (target == null) throw new InvalidOperationException("The UI target was destroyed before dispatch.");
                    action();
                }
                catch (ExitGUIException)
                {
                    // Native menus and some IMGUI commands abort the current GUI pass deliberately.
                    SetResult(result, "completed", verify == null ? "not_performed" : "unavailable");
                    return;
                }
                catch (Exception ex)
                {
                    SetResult(result, "failed", error: ex.GetBaseException().Message);
                    Debug.LogWarning("[UnityAgent] UI action " + result.Id + " failed: " + result.Error);
                    return;
                }
                SetResult(result, "completed", CheckEffect(verify));
            };
            EditorApplication.update += dispatch;
            return "Queued: actionId=" + result.Id + ". Call GetUIActionResult after the next editor update. " +
                "Queued is not confirmation that the UI changed. If a modal blocks Unity, use GetEditorState and AnswerModalDialog.";
        }

        private static string CheckEffect(Func<bool?> verify)
        {
            if (verify == null) return "not_performed";
            try
            {
                bool? matches = verify();
                return matches == true ? "matched" : matches == false ? "mismatch" : "unavailable";
            }
            catch (Exception)
            {
                // A failing getter cannot change the outcome of dispatch.
                return "unavailable";
            }
        }

        private static void SaveResult(ActionResult result)
            => SessionState.SetString(SessionKeyPrefix + result.Id, JsonUtility.ToJson(result));

        private static void SetResult(ActionResult result, string state, string verification = "not_performed", string error = null)
        {
            lock (ResultLock)
            {
                result.State = state;
                result.Verification = verification;
                result.Error = error;
                SaveResult(result);
            }
        }

        internal static string ReadResult(string actionId)
        {
            lock (ResultLock)
            {
                if (string.IsNullOrWhiteSpace(actionId) || !Results.TryGetValue(actionId, out var result))
                    return "Error: unknown or expired actionId (up to 256 results are retained in this Unity editor session).";
                string detail = null;
                switch (result.State)
                {
                    case "completed":
                        detail = result.Verification == "matched"
                            ? "Dispatch completed and the control reported the requested value immediately afterward."
                            : "Dispatch completed. Inspect the UI to confirm the resulting application state.";
                        break;
                    case "failed":
                        detail = "Dispatch failed. Inspect the UI for partial effects before deciding whether to try again.";
                        break;
                    case "not_dispatched":
                        detail = "A domain reload discarded the queued action before dispatch.";
                        break;
                    case "outcome_unknown":
                        detail = "A domain reload interrupted result recording after dispatch began. Inspect the UI for effects before deciding whether to try again.";
                        break;
                }
                return "actionId=" + result.Id + " state=" + result.State + " verification=" + result.Verification +
                    (result.Error == null ? "" : " error=" + result.Error) + (detail == null ? "" : ". " + detail);
            }
        }
    }

    public static class UIActionTools
    {
        [AgentTool("Read a queued UI automation action's dispatch status: queued, running, completed, failed, " +
                   "not_dispatched (discarded while queued), or outcome_unknown (reload during dispatch). " +
                   "For direct value writes, verification reports the control's immediate read-back separately: matched, mismatch or unavailable. " +
                   "For other actions it is not_performed; inspect or capture the UI to confirm the effect. " +
                   "Up to 256 results survive a domain reload within this Unity editor session. Interrupted actions are never replayed. " +
                   "MCP serves this cache off the main thread, " +
                   "so running can be observed while a modal blocks Unity; GetEditorState/AnswerModalDialog handles the dialog.", Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string GetUIActionResult(string actionId) => UIAutomationUtility.ReadResult(actionId);
    }
}
