using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly AutoResetEvent TickRequested = new AutoResetEvent(false);
        static CallbackFunction _update, _delayCall, _quitting;
        static int _signalCount, _workerSignalCount;
        public static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        public static event CallbackFunction update
        {
            add { AssertMainThread(); _update += value; }
            remove { AssertMainThread(); _update -= value; }
        }
        public static event CallbackFunction delayCall
        {
            add { AssertMainThread(); _delayCall += value; }
            remove { AssertMainThread(); _delayCall -= value; }
        }
        public static event CallbackFunction quitting
        {
            add { AssertMainThread(); _quitting += value; }
            remove { AssertMainThread(); _quitting -= value; }
        }
        public static double timeSinceStartup { get { AssertMainThread(); return Clock.Elapsed.TotalSeconds; } }
        public static int SignalCount => Volatile.Read(ref _signalCount);
        public static int WorkerSignalCount => Volatile.Read(ref _workerSignalCount);
        public static int UpdateCount { get; private set; }
        public static bool isCompiling { get { AssertMainThread(); return false; } }
        public static bool isUpdating { get { AssertMainThread(); return false; } }

        // Mirrors Unity's internal, thread-safe wakeup: workers request a tick;
        // only the editor main thread executes update callbacks.
        internal static void SignalTick()
        {
            Interlocked.Increment(ref _signalCount);
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
                Interlocked.Increment(ref _workerSignalCount);
            TickRequested.Set();
        }

        public static bool RunRequestedUpdate(int timeoutMs)
        {
            AssertMainThread();
            if (!TickRequested.WaitOne(timeoutMs)) return false;
            UpdateCount++;
            _update?.Invoke();
            return true;
        }

        public static void Quit() { AssertMainThread(); _quitting?.Invoke(); }

        public static void AssertMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
                throw new InvalidOperationException("Editor API accessed from a worker thread");
        }
    }

    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload;
        public static void Reload() { EditorApplication.AssertMainThread(); beforeAssemblyReload?.Invoke(); }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public string resolvedPath;
        public static PackageInfo FindForAssembly(Assembly assembly) => null;
    }
}

namespace UnityEngine
{
    public enum RuntimePlatform { WindowsEditor, OSXEditor, LinuxEditor }
    public static class Application
    {
        public static string dataPath;
        public static RuntimePlatform platform => RuntimePlatform.WindowsEditor;
    }
}

namespace AjisaiFlow.UnityAgent.Editor
{
    internal static class DeveloperMode { public static bool IsDevBuild => false; }
    public enum MCPServerMode { InProc, Bridge }
    internal static class AgentSettings
    {
        static bool _enabled;
        static MCPServerMode _mode;
        static int _internalPort;
        public static bool MCPServerEnabled
        {
            get { UnityEditor.EditorApplication.AssertMainThread(); return _enabled; }
            set { UnityEditor.EditorApplication.AssertMainThread(); _enabled = value; }
        }
        public static MCPServerMode MCPServerMode
        {
            get { UnityEditor.EditorApplication.AssertMainThread(); return _mode; }
            set { UnityEditor.EditorApplication.AssertMainThread(); _mode = value; }
        }
        public static int MCPBridgeInternalPort
        {
            get { UnityEditor.EditorApplication.AssertMainThread(); return _internalPort; }
            set { UnityEditor.EditorApplication.AssertMainThread(); _internalPort = value; }
        }
        public static int MCPBridgePublicPort => MCPBridgeInternalPort + 1;
        public static string EnsureMCPServerToken() { UnityEditor.EditorApplication.AssertMainThread(); return "test-token"; }
    }

    internal enum LogTag { MCP }
    internal static class AgentLogger
    {
        public static readonly List<string> Errors = new List<string>();
        public static readonly ManualResetEventSlim EofObserved = new ManualResetEventSlim(false);
        public static readonly ManualResetEventSlim ReleaseEof = new ManualResetEventSlim(false);
        static int _holdEof;
        public static void HoldNextEof()
        {
            EofObserved.Reset(); ReleaseEof.Reset(); Interlocked.Exchange(ref _holdEof, 1);
        }
        public static void Debug(LogTag tag, string message) { }
        public static void Info(LogTag tag, string message)
        {
            // Hold a retired reader just before finally so replacement races
            // are deterministic instead of depending on thread timing.
            if (message.Contains("reader EOF") && Interlocked.Exchange(ref _holdEof, 0) == 1)
            {
                EofObserved.Set();
                if (!ReleaseEof.Wait(5000)) throw new TimeoutException("test reader EOF barrier");
            }
        }
        public static void Warning(LogTag tag, string message) { }
        public static void Error(LogTag tag, string message) { lock (Errors) Errors.Add(message); }
    }
    internal static class PackagePaths { public static string PackageRoot => null; }
}

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    internal static class ModalAutoAnswer
    {
        public static int InitializeCount;
        public static void Initialize() { InitializeCount++; }
        public static void Shutdown() { }
    }
    internal static class MainThreadWatchdog
    {
        public const int DefaultStallThresholdMs = 5000;
        public static bool TryDescribeStall(int threshold, out string message) { message = null; return false; }
        public static void NoteMainThreadTick() { UnityEditor.EditorApplication.AssertMainThread(); }
        public static void NoteToolStart(string tool) { UnityEditor.EditorApplication.AssertMainThread(); }
        public static void NoteToolFinish() { UnityEditor.EditorApplication.AssertMainThread(); }
    }
    internal static class ListenerThreadTools
    {
        public static void RefreshReaderGateOnMainThread() { UnityEditor.EditorApplication.AssertMainThread(); }
        public static Func<string> Match(string tool, JNode args) => null;
        public static Func<string> Match(string tool, JNode args, out string error) { error = null; return null; }
        public static Func<string> Match(string tool, JNode args, out string error, out int errorCode, out ToolResultRequest.Options options)
        { error = null; errorCode = -32602; options = ToolResultRequest.Options.Default; return null; }
    }
    internal static class ToolResultRequest
    {
        internal readonly struct Options { internal static Options Default => new Options(); }
        internal static string FormatError(string tool, string text, Options options) => text;
    }
    internal static class AgentMCPServer
    {
        public static void StartShared() { UnityEditor.EditorApplication.AssertMainThread(); }
        public static void StopShared() { UnityEditor.EditorApplication.AssertMainThread(); }
        public static void RaiseCallStart(string tool, string args) { UnityEditor.EditorApplication.AssertMainThread(); }
        public static void RecordStallRejection(string tool, int bytes, string message) { }
    }
    internal sealed class PendingCall
    {
        public readonly string ToolName;
        public readonly JNode Args;
        public string BridgePendingId, Error, ErrorData, ResultText;
        public string ImageMimeType = null;
        public int ErrorCode;
        public byte[] ImageBytes = null;
        public Action<PendingCall> OnComplete;
        public PendingCall(string tool, JNode args, int bytes) { ToolName = tool; Args = args; }
        public void MarkExecutionStart() { UnityEditor.EditorApplication.AssertMainThread(); }
        public void SetError(string error, string data, int code)
        {
            Error = error; ErrorData = data; ErrorCode = code; OnComplete?.Invoke(this);
        }
    }
    internal static class Invoker
    {
        public static readonly List<string> Executed = new List<string>();
        public static void Invoke(PendingCall call)
        {
            UnityEditor.EditorApplication.AssertMainThread();
            Executed.Add(call.BridgePendingId);
            call.ResultText = call.Args["value"].AsString;
            call.OnComplete?.Invoke(call);
        }
    }
}
