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
        private sealed class ActionResult
        {
            public string Id;
            public string State = "queued";
            public string Error;
        }

        private const int MaxResults = 256;
        private static readonly object ResultLock = new object();
        private static readonly Dictionary<string, ActionResult> Results = new Dictionary<string, ActionResult>();
        private static readonly Queue<string> ResultOrder = new Queue<string>();

        internal static int PlaySession { get; private set; }

        static UIAutomationUtility()
        {
            EditorApplication.playModeStateChanged += _ => PlaySession++;
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

        internal static string Queue(UnityEngine.Object target, Action action)
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
                    var kept = ResultOrder.Where(id => id != completed).ToArray();
                    ResultOrder.Clear();
                    foreach (string id in kept) ResultOrder.Enqueue(id);
                }
                result = new ActionResult { Id = Guid.NewGuid().ToString("N") };
                Results.Add(result.Id, result);
                ResultOrder.Enqueue(result.Id);
            }
            // update is also pumped by batch mode and the Unity test runner. Registering during
            // an update does not enter its current delegate snapshot, so the tool response goes first.
            EditorApplication.CallbackFunction dispatch = null;
            dispatch = () =>
            {
                EditorApplication.update -= dispatch;
                lock (ResultLock) result.State = "running";
                try
                {
                    if (target == null) throw new InvalidOperationException("The UI target was destroyed before dispatch.");
                    action();
                    lock (ResultLock) result.State = "completed";
                }
                catch (ExitGUIException)
                {
                    // Native menus and some IMGUI commands abort the current GUI pass deliberately.
                    lock (ResultLock) result.State = "completed";
                }
                catch (Exception ex)
                {
                    lock (ResultLock)
                    {
                        result.State = "failed";
                        result.Error = ex.GetBaseException().Message;
                    }
                    Debug.LogWarning("[UnityAgent] UI action " + result.Id + " failed: " + result.Error);
                }
            };
            EditorApplication.update += dispatch;
            return "Queued: actionId=" + result.Id + ". Call GetUIActionResult after the next editor update. " +
                "Queued is not confirmation that the UI changed. If a modal blocks Unity, use GetEditorState and AnswerModalDialog.";
        }

        internal static string ReadResult(string actionId)
        {
            lock (ResultLock)
            {
                if (string.IsNullOrWhiteSpace(actionId) || !Results.TryGetValue(actionId, out var result))
                    return "Error: unknown or expired actionId (results keep the last 256 actions and reset on domain reload).";
                return "actionId=" + result.Id + " state=" + result.State +
                    (result.Error == null ? "" : " error=" + result.Error) +
                    (result.State == "completed" ? ". Dispatch completed; inspect the UI to verify the resulting application state." : "");
            }
        }
    }

    public static class UIActionTools
    {
        [AgentTool("Read a queued UI automation action's status: queued, running, completed or failed. " +
                   "Dispatch completed does not prove the application's desired state; inspect or capture the UI afterward. " +
                   "Results expire after 256 actions or a domain reload. MCP serves this cache off the main thread, " +
                   "so running can be observed while a modal blocks Unity; GetEditorState/AnswerModalDialog handles the dialog.", Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string GetUIActionResult(string actionId) => UIAutomationUtility.ReadResult(actionId);
    }
}
