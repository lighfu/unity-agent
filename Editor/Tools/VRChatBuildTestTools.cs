using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MCP;
using Debug = UnityEngine.Debug;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// VRChat SDK "Build &amp; Test" as a start-then-poll pair (#29).
    ///
    /// A real avatar build takes minutes and the MCP transport abandons a call at 120 s, so a
    /// single call can never both start the build and report how it ended. StartVRChatBuildTest
    /// returns a job id as soon as the SDK has accepted the build; GetVRChatBuildTestResult reads
    /// the outcome later.
    ///
    /// The SDK runs the export synchronously inside one continuation, so the build holds the main
    /// thread for most of its duration. The result therefore has to be readable without it: the
    /// SDK's build events write into the job under a lock, and ListenerThreadTools answers
    /// GetVRChatBuildTestResult from the MCP listener thread. Only the final tally (console and
    /// NDMF counts) needs the main thread, and it is taken once, when the build task ends.
    ///
    /// SDK types are resolved by reflection, as in VRChatUploadTools, so the package still
    /// compiles in projects without the VRChat SDK.
    /// </summary>
    public static class VRChatBuildTestTools
    {
        const int MaxRetainedJobs = 16;

        // Statics die with the domain; SessionState does not. The job table itself cannot survive a
        // reload (the SDK's task lives in the old domain), but a record of it can, which is what
        // lets GetVRChatBuildTestResult say "lost to a reload" instead of "never heard of it".
        const string SessionIndexKey = "UnityAgent.VRChatBuildTest.Ids";
        const string SessionKeyPrefix = "UnityAgent.VRChatBuildTest.Job.";
        const string RunningRecordTag = "running";
        const string FinalRecordTag = "final";

        sealed class BuildTestJob
        {
            public string Id;
            public string AvatarPath;
            public DateTime StartedUtc;
            public readonly Stopwatch Clock = new Stopwatch();
            public Task Task;
            public string Note;

            // Main thread only.
            public object Builder;
            public readonly List<KeyValuePair<EventInfo, Delegate>> Handlers = new List<KeyValuePair<EventInfo, Delegate>>();
            public bool HaveConsoleBefore;
            public ConsoleTools.ConsoleCounts ConsoleBefore;
            public int ConsoleIndexBefore = -1;

            // Everything below is guarded by _lock: written on the main thread (SDK events, Finish),
            // read from the MCP listener thread.
            public string LastProgress;
            public string SdkBuildState;
            public string SdkError;
            public string BundlePath;
            public bool Finished;
            public bool Succeeded;
            public string Failure;
            public long ElapsedMs;
            public string NdmfLine;
            public string ConsoleLine;
        }

        static readonly object _lock = new object();
        static readonly List<BuildTestJob> _jobs = new List<BuildTestJob>();   // oldest first
        static bool _driverRegistered;                                          // main thread only

        [AgentTool(@"Start the VRChat SDK 'Build & Test' for an avatar and return a jobId at once; read the
outcome with GetVRChatBuildTestResult.

A real avatar build takes minutes, far past the 120 s limit of one MCP call, so this never waits
for the build to finish.

It builds through the SDK's public builder API (IVRCSdkAvatarBuilderApi.BuildAndTest): NDMF and
every other build hook run exactly as when the user clicks Build & Test, and the result is added
to the SDK's local test avatars. Nothing is uploaded.

While the SDK builds, it holds the editor's main thread for most of the build, so other tools
queue behind it. GetVRChatBuildTestResult and GetEditorState still answer during that time.

Requires the VRChat Avatars SDK, an interactive editor (refused in batch mode, where SDK dialogs
would be auto-confirmed), a Windows / Android / iOS build target, and the avatar builder of the
SDK Control Panel. The panel is opened automatically, but the SDK creates the builder only while
logged in (see CheckVRChatAuthentication).
If the avatar has no blueprint ID, the SDK assigns a local test ID, which marks the scene dirty.
One build at a time. The jobId does not survive a domain reload; GetVRChatBuildTestResult then
reports the job as lost.",
            Category = "VRChat Publishing", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static IEnumerator StartVRChatBuildTest(string avatarRootName)
        {
            if (Application.isBatchMode)
            {
                yield return "Error: Build & Test is disabled in batch mode. The SDK and its build hooks may show dialogs, " +
                             "and in batch mode EditorUtility.DisplayDialog answers them on its own. Run this from an interactive Unity Editor.";
                yield break;
            }

            var builderInterface = VRChatTools.FindVrcType(VRChatUploadTools.AvatarBuilderInterfaceName);
            if (builderInterface == null)
            {
                yield return "Error: VRChat Avatars SDK not found. Ensure com.vrchat.avatars is installed.";
                yield break;
            }

            var buildAndTest = builderInterface.GetMethod("BuildAndTest", new[] { typeof(GameObject) });
            if (buildAndTest == null || !typeof(Task).IsAssignableFrom(buildAndTest.ReturnType))
            {
                yield return "Error: IVRCSdkAvatarBuilderApi.BuildAndTest(GameObject) not found (VRChat SDK version mismatch?).";
                yield break;
            }

            string busy = DescribeRunningJob();
            if (busy != null) { yield return "Error: " + busy; yield break; }

            // The SDK checks this only after it has switched its state to Building, and throws
            // without resetting that state, which leaves the panel stuck. Refuse up front instead.
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64
                && target != BuildTarget.Android && target != BuildTarget.iOS)
            {
                yield return $"Error: the VRChat SDK supports Build & Test only on Windows, Android and iOS build targets; the active target is {target}.";
                yield break;
            }

            var go = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (go == null) { yield return $"Error: GameObject '{avatarRootName}' not found."; yield break; }
            if (VRChatTools.FindAvatarDescriptor(avatarRootName) == null)
            {
                yield return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";
                yield break;
            }

            string note = null;
            var pmType = VRChatTools.FindVrcType(VRChatUploadTools.PipelineManagerTypeName);
            if (pmType != null && go.GetComponent(pmType) == null)
            {
                Undo.AddComponent(go, pmType);
                note = "a PipelineManager was added to the avatar root (the SDK refuses to build without one).";
            }

            string openError = VRChatUploadTools.EnsureControlPanelOpen();
            if (openError != null) { yield return openError; yield break; }

            // The panel registers its builders while building its UI, one tick after GetWindow at
            // the earliest and a few more on a cold open.
            object builder = null;
            string builderError = null;
            var waitForBuilder = Stopwatch.StartNew();
            while (true)
            {
                yield return null;
                try { builder = VRChatUploadTools.GetSdkBuilder(builderInterface, out builderError); }
                catch (Exception e) { builder = null; builderError = $"Error: TryGetBuilder failed: {VRChatUploadTools.Describe(e)}"; }
                if (builder != null || waitForBuilder.Elapsed.TotalSeconds > 5) break;
            }
            if (builder == null)
            {
                yield return (builderError ?? "Error: the SDK Control Panel did not provide an avatar builder.") +
                             "\nThe SDK creates its avatar builder on the panel's Builder tab, and only while logged in. " +
                             "Run CheckVRChatAuthentication (it can restore a saved session), then retry.";
                yield break;
            }

            string sdkState = null;
            try { sdkState = builder.GetType().GetProperty("BuildState")?.GetValue(builder)?.ToString(); }
            catch { }
            if (sdkState == "Building")
            {
                yield return "Error: the VRChat SDK is already building (started from the Control Panel or another tool). " +
                             "Wait for it to finish, then retry.";
                yield break;
            }

            var job = new BuildTestJob
            {
                Id = "buildtest-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                AvatarPath = AvatarAnatomyTools.GetHierarchyPathInternal(go),
                StartedUtc = DateTime.UtcNow,
                Builder = builder,
                Note = note,
            };
            // A delta, not a ClearConsole: the caller may need what was logged before the build.
            job.HaveConsoleBefore = ConsoleTools.TryCountBySeverity(out job.ConsoleBefore, out _);
            job.ConsoleIndexBefore = ConsoleTools.GetEntryCount();
            AttachHandlers(job, builder);

            Task task = null;
            string startError = null;
            job.Clock.Start();
            try
            {
                // Returns at the SDK's first await; the export itself starts on a later tick.
                task = (Task)buildAndTest.Invoke(builder, new object[] { go });
            }
            catch (Exception e)
            {
                startError = $"Error: BuildAndTest failed to start: {VRChatUploadTools.Describe(e)}";
            }
            if (startError == null && task == null)
                startError = "Error: BuildAndTest did not return a task.";
            if (startError != null)
            {
                DetachHandlers(job);
                yield return startError;
                yield break;
            }

            job.Task = task;
            Register(job);
            RememberInSession(job.Id, RunningRecord(job));
            EnsureDriver();
            Debug.Log($"[UnityAgent] StartVRChatBuildTest {job.Id}: Build & Test started for '{job.AvatarPath}'.");

            var sb = new StringBuilder();
            sb.AppendLine($"jobId: {job.Id}");
            sb.AppendLine($"avatar: {job.AvatarPath}");
            sb.AppendLine("state: running (the VRChat SDK accepted the build)");
            if (note != null) sb.AppendLine($"note: {note}");
            sb.AppendLine($"Poll with GetVRChatBuildTestResult(jobId:'{job.Id}', waitSeconds:100) until state is succeeded or failed.");
            sb.Append("The SDK holds the editor's main thread for most of the build, so other tools will queue until it ends. " +
                      "GetVRChatBuildTestResult and GetEditorState keep answering. The jobId does not survive a domain reload.");
            yield return sb.ToString();
        }

        [AgentTool(@"Read the outcome of a StartVRChatBuildTest job.

state: running / finishing / succeeded / failed / lost.
  running    elapsed time, the SDK's build state and its latest progress message. If a modal window
             is up it is named here, because a dialog waiting for an answer stops the build.
  succeeded  the bundle path; the avatar is in the SDK's local test avatars.
  failed     the SDK's error.
Both finished states add NDMF's error-report counts and how many console errors / exceptions /
warnings appeared during the build. The console count is a delta, so no ClearConsole call is needed;
the sinceIndex to read those entries with GetConsoleLogs is included.

This is answered from the MCP listener thread, so it works while the SDK build holds the main thread.

waitSeconds: wait up to this long for the build to finish before answering (default 0 = answer now,
  capped at 110 s so the reply beats the transport's 120 s limit). A build usually takes minutes:
  call again with waitSeconds until the state is succeeded or failed.
jobId: omit for the most recent build.

After a domain reload the in-memory job is gone. The answer then comes from the editor-session
record: the recorded outcome if the build had finished, or state 'lost' if the reload interrupted it.",
            Category = "VRChat Publishing")]
        public static IEnumerator GetVRChatBuildTestResult(string jobId = "", int waitSeconds = 0)
        {
            var job = FindJob(jobId);
            if (job == null)
            {
                yield return DescribeFromSession(jobId);
                yield break;
            }

            if (waitSeconds > EditorStateTools.MaxToolSeconds) waitSeconds = EditorStateTools.MaxToolSeconds;
            double deadline = EditorApplication.timeSinceStartup + Math.Max(0, waitSeconds);
            while (true)
            {
                // Don't leave a finished task for the driver's next tick when the answer is wanted now.
                if (!IsFinished(job) && job.Task.IsCompleted) Finish(job);
                if (IsFinished(job) || EditorApplication.timeSinceStartup >= deadline) break;
                yield return null;
            }
            yield return Describe(job);
        }

        // ─── listener-thread entry points ───

        /// <summary>
        /// The GetVRChatBuildTestResult answer for the MCP listener thread, or null when the call
        /// must go to the main thread: a job that is not in memory can only be looked up in
        /// SessionState, which is main-thread only. The returned work may wait up to waitSeconds,
        /// so it has to run on a thread that is allowed to block (see ListenerThreadTools).
        /// </summary>
        internal static Func<string> MatchOffMainThread(string jobId, int waitSeconds)
        {
            var job = FindJob(jobId);
            if (job == null) return null;
            int limitMs = Math.Min(Math.Max(0, waitSeconds), EditorStateTools.MaxToolSeconds) * 1000;
            return () =>
            {
                var waited = Stopwatch.StartNew();
                while (!IsFinished(job) && waited.ElapsedMilliseconds < limitMs)
                    Thread.Sleep(250);
                return Describe(job);
            };
        }

        /// <summary>One line for GetEditorState while a build runs, else null. Safe off the main thread.</summary>
        internal static string DescribeRunningForEditorState()
        {
            lock (_lock)
            {
                for (int i = _jobs.Count - 1; i >= 0; i--)
                {
                    var job = _jobs[i];
                    if (job.Finished) continue;
                    string progress = job.LastProgress != null ? $", last progress \"{OneLine(job.LastProgress)}\"" : "";
                    return $"{job.Id} running for {job.Clock.ElapsedMilliseconds / 1000}s ({job.SdkBuildState ?? "starting"}{progress})";
                }
            }
            return null;
        }

        // ─── job table ───

        static BuildTestJob FindJob(string jobId)
        {
            string id = (jobId ?? "").Trim();
            lock (_lock)
            {
                if (id.Length == 0) return _jobs.Count > 0 ? _jobs[_jobs.Count - 1] : null;
                foreach (var job in _jobs)
                    if (job.Id == id) return job;
            }
            return null;
        }

        static bool IsFinished(BuildTestJob job)
        {
            lock (_lock) return job.Finished;
        }

        static string DescribeRunningJob()
        {
            lock (_lock)
            {
                foreach (var job in _jobs)
                    if (!job.Finished)
                        return $"a Build & Test is already running (jobId {job.Id}, {job.Clock.ElapsedMilliseconds / 1000}s so far). " +
                               $"Poll GetVRChatBuildTestResult(jobId:'{job.Id}') and start the next build after it finishes.";
            }
            return null;
        }

        static void Register(BuildTestJob job)
        {
            lock (_lock)
            {
                // Finished jobs only: dropping a running one would strand a build nobody can observe.
                while (_jobs.Count >= MaxRetainedJobs)
                {
                    int oldest = _jobs.FindIndex(j => j.Finished);
                    if (oldest < 0) break;
                    _jobs.RemoveAt(oldest);
                }
                _jobs.Add(job);
            }
        }

        // ─── SDK events ───

        static void AttachHandlers(BuildTestJob job, object builder)
        {
            var type = builder.GetType();
            Attach(job, builder, type.GetEvent("OnSdkBuildProgress"),
                new EventHandler<string>((_, status) => { lock (_lock) job.LastProgress = status; }));
            Attach(job, builder, type.GetEvent("OnSdkBuildError"),
                new EventHandler<string>((_, message) => { lock (_lock) job.SdkError = message; }));
            Attach(job, builder, type.GetEvent("OnSdkBuildSuccess"),
                new EventHandler<string>((_, path) => { lock (_lock) job.BundlePath = path; }));

            // EventHandler<SdkBuildState>: the enum is an SDK type, so the handler is built by reflection.
            var stateEvent = type.GetEvent("OnSdkBuildStateChange");
            if (stateEvent != null)
                Attach(job, builder, stateEvent, MakeStateHandler(stateEvent.EventHandlerType, job));
        }

        static void Attach(BuildTestJob job, object builder, EventInfo ev, Delegate handler)
        {
            // A signature change in a future SDK should cost the progress detail, never the build.
            // (The upload side's OnSdkUploadProgress is EventHandler<(string, float)>, not <string>.)
            if (ev == null || handler == null || ev.EventHandlerType != handler.GetType()) return;
            try
            {
                ev.AddEventHandler(builder, handler);
                job.Handlers.Add(new KeyValuePair<EventInfo, Delegate>(ev, handler));
            }
            catch { }
        }

        static Delegate MakeStateHandler(Type handlerType, BuildTestJob job)
        {
            if (handlerType == null || !handlerType.IsGenericType || handlerType.GetGenericTypeDefinition() != typeof(EventHandler<>))
                return null;
            try
            {
                var make = typeof(VRChatBuildTestTools)
                    .GetMethod(nameof(StateHandler), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(handlerType.GetGenericArguments()[0]);
                return (Delegate)make.Invoke(null, new object[] { job });
            }
            catch
            {
                return null;
            }
        }

        static EventHandler<T> StateHandler<T>(BuildTestJob job)
        {
            return (_, value) =>
            {
                object boxed = value;
                lock (_lock) job.SdkBuildState = boxed?.ToString();
            };
        }

        static void DetachHandlers(BuildTestJob job)
        {
            foreach (var h in job.Handlers)
            {
                try { h.Key.RemoveEventHandler(job.Builder, h.Value); }
                catch { }
            }
            job.Handlers.Clear();
            job.Builder = null;
        }

        // ─── completion (main thread) ───

        static void EnsureDriver()
        {
            if (_driverRegistered) return;
            EditorApplication.update += Drive;
            _driverRegistered = true;
        }

        static void Drive()
        {
            List<BuildTestJob> completed = null;
            bool anyRunning = false;
            lock (_lock)
            {
                foreach (var job in _jobs)
                {
                    if (job.Finished) continue;
                    if (job.Task.IsCompleted)
                    {
                        if (completed == null) completed = new List<BuildTestJob>();
                        completed.Add(job);
                    }
                    else
                    {
                        anyRunning = true;
                    }
                }
            }

            if (completed != null)
                foreach (var job in completed) Finish(job);

            if (!anyRunning)
            {
                EditorApplication.update -= Drive;
                _driverRegistered = false;
            }
        }

        static void Finish(BuildTestJob job)
        {
            if (IsFinished(job)) return;

            DetachHandlers(job);
            job.Clock.Stop();

            string failure = null;
            if (job.Task.IsFaulted) failure = VRChatUploadTools.Describe(job.Task.Exception);
            else if (job.Task.IsCanceled) failure = "the SDK cancelled the build.";

            string ndmfLine = NDMFTools.TryGetErrorReportCounts(out var ndmf, out string ndmfDiag)
                ? ndmf.Format()
                : "unavailable — " + OneLine(ndmfDiag);
            string consoleLine = DescribeConsoleDelta(job);

            lock (_lock)
            {
                job.Succeeded = failure == null;
                job.Failure = failure;
                job.ElapsedMs = job.Clock.ElapsedMilliseconds;
                job.NdmfLine = ndmfLine;
                job.ConsoleLine = consoleLine;
                job.Finished = true;
            }

            string text = Describe(job);
            RememberInSession(job.Id, FinalRecordTag + "\n" + text);
            Debug.Log($"[UnityAgent] VRChat Build & Test {job.Id} {(job.Succeeded ? "succeeded" : "FAILED")} " +
                      $"after {job.ElapsedMs / 1000.0:F1}s ({job.AvatarPath}).");
        }

        static string DescribeConsoleDelta(BuildTestJob job)
        {
            if (!job.HaveConsoleBefore || !ConsoleTools.TryCountBySeverity(out var after, out _))
                return "unavailable (the console could not be read)";
            // Fewer entries than before means the console was cleared mid-build ("Clear on Recompile"
            // after the player-script compile, or the user). A delta would then go negative.
            if (after.total < job.ConsoleBefore.total)
                return $"errors={after.errors}, exceptions={after.exceptions}, warnings={after.warnings} " +
                       "(the console was cleared during the build, so these are totals since the clear)";
            return $"errors={after.errors - job.ConsoleBefore.errors}, " +
                   $"exceptions={after.exceptions - job.ConsoleBefore.exceptions}, " +
                   $"warnings={after.warnings - job.ConsoleBefore.warnings} (new during this build)";
        }

        // ─── reporting ───

        /// <summary>Touches no Unity API, so it serves the listener thread as well as the main thread.</summary>
        static string Describe(BuildTestJob job)
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                sb.AppendLine($"jobId: {job.Id}");
                sb.AppendLine($"avatar: {job.AvatarPath}");
                if (!job.Finished)
                {
                    sb.AppendLine(job.Task != null && job.Task.IsCompleted
                        ? "state: finishing (the SDK has returned; the outcome is being collected on the main thread, poll again)"
                        : "state: running");
                    sb.AppendLine($"elapsedMs: {job.Clock.ElapsedMilliseconds}");
                    sb.AppendLine($"sdkBuildState: {job.SdkBuildState ?? "(no state change reported yet)"}");
                    sb.AppendLine($"lastProgress: {Quoted(job.LastProgress)}");
                }
                else
                {
                    sb.AppendLine(job.Succeeded ? "state: succeeded" : "state: failed");
                    sb.AppendLine($"elapsedMs: {job.ElapsedMs}");
                    if (job.Succeeded)
                    {
                        sb.AppendLine("outcome: built and added to the VRChat SDK's local test avatars. Nothing was uploaded.");
                        sb.AppendLine($"bundlePath: {job.BundlePath ?? "(not reported by the SDK)"}");
                    }
                    else
                    {
                        sb.AppendLine($"error: {job.Failure}");
                        if (!string.IsNullOrEmpty(job.SdkError) && (job.Failure == null || !job.Failure.Contains(job.SdkError)))
                            sb.AppendLine($"sdkError: {job.SdkError}");
                        sb.AppendLine($"lastProgress: {Quoted(job.LastProgress)}");
                    }
                    sb.AppendLine($"ndmf: {job.NdmfLine}");
                    sb.AppendLine($"console: {job.ConsoleLine}");
                    if (job.ConsoleIndexBefore >= 0)
                        sb.AppendLine($"The build's console entries: GetConsoleLogs(sinceIndex: {job.ConsoleIndexBefore - 1}).");
                }
                if (job.Note != null) sb.AppendLine($"note: {job.Note}");
            }

            if (!IsFinished(job) && MainThreadWatchdog.TryGetModalWindow(out string modal, out _))
                sb.AppendLine($"modalWindow: {modal}. A progress bar is normal during a build; a question dialog stops it until answered " +
                              "(AnswerModalDialog(dryRun=true) shows what it says).");
            return sb.ToString().TrimEnd();
        }

        static string DescribeFromSession(string jobId)
        {
            string id = (jobId ?? "").Trim();
            var ids = SessionIds();
            if (id.Length == 0 && ids.Count > 0) id = ids[ids.Count - 1];

            string record = id.Length > 0 ? SessionState.GetString(SessionKeyPrefix + id, "") : "";
            if (record.StartsWith(FinalRecordTag + "\n", StringComparison.Ordinal))
                return "(recovered after a domain reload: this is the outcome recorded when the build finished)\n" +
                       record.Substring(FinalRecordTag.Length + 1);

            if (record.StartsWith(RunningRecordTag + "\n", StringComparison.Ordinal))
            {
                string[] parts = record.Split('\n');
                string avatar = parts.Length > 1 ? parts[1] : "(unknown)";
                string started = parts.Length > 2 ? parts[2] : "(unknown)";
                return $"jobId: {id}\n" +
                       $"avatar: {avatar}\n" +
                       $"state: lost (a domain reload happened while this Build & Test was running; it started at {started} UTC)\n" +
                       "The SDK's build task lived in the unloaded domain, so its outcome was never recorded and the build most likely did not complete. " +
                       "GetConsoleLogs shows how far the SDK got; start a new build with StartVRChatBuildTest.";
            }

            var sb = new StringBuilder(id.Length == 0
                ? "Error: no Build & Test has been started in this editor session."
                : $"Error: unknown jobId '{id}'. It was not started in this editor session (job ids do not carry over to a new session).");
            lock (_lock)
            {
                if (_jobs.Count > 0)
                {
                    sb.Append("\nKnown jobs:");
                    foreach (var job in _jobs)
                        sb.Append($"\n  {job.Id}  {(job.Finished ? (job.Succeeded ? "succeeded" : "failed") : "running")}  {job.AvatarPath}");
                }
            }
            return sb.ToString();
        }

        static string RunningRecord(BuildTestJob job)
            => $"{RunningRecordTag}\n{job.AvatarPath}\n{job.StartedUtc:yyyy-MM-dd HH:mm:ss}";

        static void RememberInSession(string id, string value)
        {
            SessionState.SetString(SessionKeyPrefix + id, value);
            var ids = SessionIds();
            if (!ids.Contains(id)) ids.Add(id);
            while (ids.Count > MaxRetainedJobs)
            {
                SessionState.EraseString(SessionKeyPrefix + ids[0]);
                ids.RemoveAt(0);
            }
            SessionState.SetString(SessionIndexKey, string.Join(",", ids));
        }

        static List<string> SessionIds()
            => new List<string>(SessionState.GetString(SessionIndexKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        static string Quoted(string s) => s == null ? "(none yet)" : $"\"{OneLine(s)}\"";

        static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string flat = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= 200 ? flat : flat.Substring(0, 197) + "...";
        }
    }
}
