using System;
using System.Collections;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.SDK;

namespace UnityEngine
{
    internal static class Application { internal static string dataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UnityAgent-Paging-Assets"); }
}
namespace UnityEditor
{
    internal static class EditorApplication
    {
        internal static event Action update;
        internal static void Tick() { update?.Invoke(); AjisaiFlow.UnityAgent.Editor.EditorCoroutineUtility.Tick(); }
    }
    internal static class Undo { internal static int GetCurrentGroup() => 0; }
}
namespace AjisaiFlow.UnityAgent.Editor
{
    internal enum LogTag { MCP, Tool }
    internal static class AgentLogger
    {
        internal static void Debug(LogTag tag, string message) { }
        internal static void Info(LogTag tag, string message) { }
        internal static void Warning(LogTag tag, string message) { }
        internal static void Error(LogTag tag, string message) { }
    }
    internal static class DeveloperMode { internal static bool IsDevBuild = false; }
    internal static class UpdateChecker { internal static string CurrentVersion => "test"; }
    internal static class AgentSettings
    {
        static readonly int MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        static string _disabledTool;
        static ToolRisk _risk = ToolRisk.Dangerous;
        internal static bool MCPServerEnabled = true;
        internal static int MCPServerPort = 0;
        internal static string MCPServerToken = "test-token";
        internal static ToolRisk MCPServerExposeRisk
        {
            get { AssertMainThread(); return _risk; }
            set { AssertMainThread(); _risk = value; MCP.ListenerThreadTools.RefreshReaderGateOnMainThread(); }
        }
        internal static string DisabledTool
        {
            get { AssertMainThread(); return _disabledTool; }
            set { AssertMainThread(); _disabledTool = value; MCP.ListenerThreadTools.RefreshReaderGateOnMainThread(); }
        }
        internal static void AssertMainThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != MainThread)
                throw new InvalidOperationException("mutable settings read off the main thread");
        }
        internal static bool IsToolEnabled(string name, bool external) { AssertMainThread(); return name != _disabledTool; }
        internal static string EnsureMCPServerToken() => MCPServerToken;
    }
    internal enum ToolCallRoute { Mcp }
    internal static class ToolCallStats
    {
        internal static int Count, LastResultChars;
        internal static void Record(string tool, ToolCallRoute route, bool success, double duration, int args, int result)
        { Count++; LastResultChars = result; }
    }
    internal static class UserChoiceState
    {
        internal static int SelectedIndex = -1;
        internal static bool IsPending;
        internal static string Question, CustomText;
        internal static string[] Options;
        internal static void Clear() { SelectedIndex = -1; IsPending = false; Question = CustomText = null; Options = null; }
    }
    internal static class EditorCoroutineUtility
    {
        static readonly List<IEnumerator> Active = new List<IEnumerator>();
        internal static void StartCoroutineOwnerless(IEnumerator routine) { Active.Add(routine); }
        internal static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; i--) if (!Active[i].MoveNext()) Active.RemoveAt(i);
        }
    }
}
namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    internal static class MCPMainThreadWakeup { internal static void RequestTick() { } }
    internal static class MainThreadWatchdog
    {
        internal const int DefaultStallThresholdMs = 5000;
        internal static bool Stalled;
        internal static void Reset() { Stalled = false; }
        internal static bool TryDescribeStall(int threshold, out string text) { text = Stalled ? "test stall" : null; return Stalled; }
        internal static void NoteMainThreadTick() { }
        internal static void NoteToolStart(string tool) { }
        internal static void NoteToolFinish() { }
    }
}
namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    internal static class SceneViewTools
    {
        internal static byte[] PendingImageBytes;
        internal static string PendingImageMimeType;
        internal static void ClearPendingImage() { PendingImageBytes = null; PendingImageMimeType = null; }
    }
    internal static class EditorStateTools
    {
        internal static volatile bool Throw = false;
        internal static string GetEditorState() => Throw ? throw new InvalidOperationException(new string('t', 30000)) : FixtureTools.LongText;
    }
    internal static class UIActionTools { internal static string GetUIActionResult(string id) => FixtureTools.LongText; }
    internal static class IMGUIInspectionTools { internal static string GetIMGUIInspectionResult(string id) => FixtureTools.LongText; }
    internal static class ModalDialogTools
    {
        internal static int Calls;
        internal static string AnswerModalDialog(string button, string title, bool dryRun) { Calls++; return FixtureTools.LongText; }
        internal static string ListModalAutoAnswers() => FixtureTools.LongText;
    }
    internal static class VRChatBuildTestTools { internal static Func<string> MatchOffMainThread(string job, int wait) => () => FixtureTools.LongText; }
    internal static class ToolUtility
    {
        internal static bool TryParseBool(string value, out bool result) => bool.TryParse(value, out result);
    }
    public static class FixtureTools
    {
        public static int Calls, NativeOffset, NativeLimit;
        public static readonly string LongText = string.Join("\n", System.Linq.Enumerable.Range(0, 205)) + "\n";
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static string GetPagingFixture(int offset = 17, int limit = 23) { Calls++; NativeOffset = offset; NativeLimit = limit; return LongText; }
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static IEnumerator GetPagingAsyncFixture() { Calls++; yield return "progress"; yield return null; yield return LongText; }
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static string GetPagingImageFixture() { Calls++; SceneViewTools.PendingImageBytes = new byte[] { 1, 2, 3 }; SceneViewTools.PendingImageMimeType = "image/test"; return LongText; }
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static string GetPagingFailureFixture() { Calls++; throw new InvalidOperationException(new string('x', 20000)); }
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static string GetPagingChoiceFixture() { Calls++; UserChoiceState.IsPending = true; UserChoiceState.Question = "fixture"; return "__WAITING_USER_CHOICE__"; }
        [AgentTool("paging fixture", Risk = ToolRisk.Safe)]
        public static string GetPagingShortFixture() => "short";
    }
}
