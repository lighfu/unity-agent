using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using System;
using System.Collections;
using System.IO;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Editor / Play mode の状態を観測するツール群。
    /// 「今 Edit か Play か」「compile 中か」などを判別するためのエージェント向け入口。
    /// </summary>
    public static class EditorStateTools
    {
        /// <summary>
        /// Upper bound for any tool that blocks waiting. The MCP transport gives up on a call at
        /// 120 s (AgentMCPServer.DefaultCallTimeoutMs), so a longer wait can never deliver a
        /// result — it just leaves work running after the caller has stopped listening.
        /// </summary>
        internal const int MaxToolSeconds = 110;

        [AgentTool(@"Report the current Unity Editor runtime state.
Use to distinguish Edit mode vs Play mode before running Play-mode-only tools
(e.g., GetAnimatorRuntimeParameterValue, GetContactRuntimeProximity).
Also reports compile / domain-reload / pause state so agents can avoid racing Unity.")]
        public static string GetPlayModeState()
        {
            bool isPlaying = EditorApplication.isPlaying;
            bool isPaused = EditorApplication.isPaused;
            bool isCompiling = EditorApplication.isCompiling;
            bool isUpdating = EditorApplication.isUpdating;
            bool willChange = EditorApplication.isPlayingOrWillChangePlaymode;

            string mode;
            if (isPlaying && isPaused) mode = "Play (paused)";
            else if (isPlaying) mode = "Play";
            else if (willChange) mode = "Entering Play";
            else mode = "Edit";

            var scene = EditorSceneManager.GetActiveScene();

            var sb = new StringBuilder();
            sb.AppendLine($"PlayModeState: {mode}");
            sb.AppendLine($"  isPlaying: {isPlaying}");
            sb.AppendLine($"  isPaused: {isPaused}");
            sb.AppendLine($"  isCompiling: {isCompiling}");
            sb.AppendLine($"  isUpdating: {isUpdating}");
            sb.AppendLine($"  isPlayingOrWillChangePlaymode: {willChange}");
            sb.AppendLine($"  Time.timeScale: {Time.timeScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            if (isPlaying)
            {
                sb.AppendLine($"  Time.frameCount: {Time.frameCount}");
                sb.AppendLine($"  Time.realtimeSinceStartup: {Time.realtimeSinceStartup.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}s");
            }
            sb.AppendLine($"  ActiveScene: {scene.name} ({scene.path})");
            sb.AppendLine($"  Scene.isDirty: {scene.isDirty}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Cheap, always-answerable probe of what the editor is doing right now.
Answers the question a timeout cannot: is Unity waiting on a modal dialog, busy compiling,
grinding through a slow tool, or genuinely idle?

Unlike every other tool, this one is served WITHOUT waiting for the Unity main thread — the MCP
server answers it directly on the listener thread. That is deliberate: a modal dialog stops the
main thread entirely, so a tool that needed the main thread could never report a modal dialog.

The consequence is that compiling / importing / playMode come from a snapshot taken on the last
main-thread tick, not from this instant. 'snapshotAge' says how stale that is, and is itself the
most useful number here: an age of seconds means the main thread is blocked, whatever the
snapshot claims. An age near zero means the editor is healthy and the values are current.

modalDialog is read live from the OS, so it is accurate even while everything else is frozen.")]
        public static string GetEditorState()
        {
            MainThreadWatchdog.ReadSnapshot(
                out bool compiling, out bool importing, out bool playing, out bool paused,
                out string inFlightTool, out bool everPumped, out string autoRefresh);

            int stalledMs = MainThreadWatchdog.StalledMilliseconds();
            bool hasModal = MainThreadWatchdog.TryGetModalWindow(out string modalDesc, out string modalTitle);

            // A tick is scheduled ~100 Hz, so anything past a couple of hundred ms already means the
            // main thread is inside something. One second is a generous line between "a normal frame"
            // and "not answering".
            bool responding = everPumped && stalledMs < 1000 && !hasModal;

            string playMode;
            if (!everPumped) playMode = "unknown (no main-thread tick observed yet)";
            else if (playing && paused) playMode = "Play (paused)";
            else if (playing) playMode = "Play";
            else playMode = "Edit";

            var sb = new StringBuilder();
            sb.AppendLine($"responding: {responding}");
            sb.AppendLine($"snapshotAge: {(everPumped ? (stalledMs / 1000.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s" : "n/a")}");
            sb.AppendLine($"compiling: {(everPumped ? compiling.ToString() : "unknown")}");
            sb.AppendLine($"importing: {(everPumped ? importing.ToString() : "unknown")}");
            sb.AppendLine($"playMode: {playMode}");
            sb.AppendLine($"modalDialog: {(hasModal ? modalDesc : "(none)")}");
            sb.AppendLine($"inFlightTool: {inFlightTool ?? "(none)"}");
            sb.AppendLine($"autoRefresh: {autoRefresh ?? "unknown (not sampled yet)"}");

            sb.Append("verdict: ");
            if (hasModal)
            {
                sb.Append(LooksLikeProgress(modalTitle)
                    ? "a PROGRESS window is up — Unity is busy on its own, retry shortly, no human needed."
                    : "a DIALOG is waiting for an answer — a human must dismiss it in the Unity window. Every queued tool call is blocked until then.");
            }
            else if (!everPumped)
            {
                sb.Append("no main-thread tick observed yet — the editor may still be starting up or reloading the domain.");
            }
            else if (stalledMs >= 1000 && inFlightTool != null)
            {
                sb.Append($"the main thread is inside '{inFlightTool}'. This is a slow tool, not a wedge — wait for it.");
            }
            else if (stalledMs >= 1000)
            {
                sb.Append("the main thread has not ticked recently and no tool is running — likely a domain reload, an import, or an editor operation started from the GUI.");
            }
            else if (compiling || importing)
            {
                sb.Append("busy compiling / importing. Use WaitForCompilation before acting on new types.");
            }
            else
            {
                sb.Append("idle and responsive.");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Mirrors MainThreadWatchdog's progress-window heuristic. Duplicated rather than shared
        /// because the two callers want different wording, and the list is a guess either way.
        /// </summary>
        static bool LooksLikeProgress(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            return t.Contains("hold on") || t.Contains("importing") || t.Contains("compiling")
                || t.Contains("loading") || t.Contains("applying") || t.Contains("building")
                || t.Contains("baking") || t.Contains("progress");
        }

        /// <summary>
        /// Auto Refresh off is the reason a freshly written .cs file leaves isCompiling false
        /// forever: Unity never imports the file, so there is nothing to compile and nothing to
        /// wait for. Reported by both GetEditorState and WaitForCompilation because the symptom
        /// ("already idle") is indistinguishable from success unless you know this setting.
        ///
        /// 2021.2+ stores a tri-state mode; older editors a plain bool. Read whichever exists
        /// rather than assuming, and say "unknown" instead of guessing when neither is set.
        /// </summary>
        internal static string SampleAutoRefresh() => DescribeAutoRefresh();

        static string DescribeAutoRefresh()
        {
            try
            {
                if (EditorPrefs.HasKey("kAutoRefreshMode"))
                {
                    switch (EditorPrefs.GetInt("kAutoRefreshMode", 1))
                    {
                        case 0: return "DISABLED (Unity will not notice edited files on its own)";
                        case 2: return "enabled outside play mode";
                        default: return "enabled";
                    }
                }
                if (EditorPrefs.HasKey("kAutoRefresh"))
                    return EditorPrefs.GetBool("kAutoRefresh")
                        ? "enabled"
                        : "DISABLED (Unity will not notice edited files on its own)";
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
            return "unknown";
        }

        [AgentTool(@"Block until Unity finishes compiling / importing, then report any errors that
appeared while waiting. Replaces polling GetPlayModeState and guessing from isCompiling.

timeoutSeconds: give up after this long (default 60) and report what state Unity is in.
  Clamped to 110 s because the MCP transport abandons a call at 120 s — waiting longer than that
  cannot produce a result, it only leaves a coroutine polling after the caller has given up.

IMPORTANT — domain reload: if scripts actually recompile, Unity reloads the app domain and the MCP
bridge disconnects for several seconds. This call cannot survive that: it dies mid-wait and the
caller sees a dropped connection, not a result. That is a property of the editor, not a bug here.
The reliable pattern after editing scripts is:
  1. note the console index (GetConsoleLogs returns nextSinceIndex)
  2. trigger the compile
  3. reconnect, then call GetConsoleLogs(severity:'error', sinceIndex: <that index>)
This tool is for the common case: confirming the editor is idle and clean before doing more work,
and waiting out an asset import that does not reload the domain.

assemblyName: also report where that assembly was loaded from and when it was built, so you can
  tell 'nothing needed compiling' apart from 'my new code is actually loaded'. Accepts a simple
  name with or without .dll (e.g. 'Assembly-CSharp-Editor'). Leave empty to report the two default
  script assemblies when they are loaded.

BEWARE 'Unity was already idle': that is also what you get when Auto Refresh is off and Unity
never noticed your edit — nothing is compiling because nothing was imported. This tool reports the
Auto Refresh setting for exactly that reason. If it says DISABLED, trigger the work yourself with
TriggerDomainReload(confirm:true, mode:'recompile').")]
        public static IEnumerator WaitForCompilation(int timeoutSeconds = 60, string assemblyName = "")
        {
            if (timeoutSeconds <= 0) timeoutSeconds = 60;
            if (timeoutSeconds > MaxToolSeconds) timeoutSeconds = MaxToolSeconds;

            int baseline = ConsoleTools.GetEntryCount();
            int sinceIndex = baseline > 0 ? baseline - 1 : -1;

            double start = EditorApplication.timeSinceStartup;
            bool everBusy = false;
            bool timedOut = false;

            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                everBusy = true;
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    timedOut = true;
                    break;
                }
                yield return null;
            }

            double waited = EditorApplication.timeSinceStartup - start;
            var sb = new StringBuilder();

            if (timedOut)
            {
                sb.AppendLine($"TIMEOUT after {waited:F1}s (limit {timeoutSeconds}s). Unity is still busy:");
                sb.AppendLine($"  isCompiling: {EditorApplication.isCompiling}");
                sb.AppendLine($"  isUpdating: {EditorApplication.isUpdating}");
                sb.Append("Raise timeoutSeconds, or check the editor for a modal dialog blocking progress.");
                yield return sb.ToString();
                yield break;
            }

            sb.AppendLine(everBusy
                ? $"Compilation / import finished after {waited:F1}s ({(int)(waited * 1000)}ms)."
                : $"Unity was already idle (nothing to wait for) after {(int)(waited * 1000)}ms.");

            string autoRefresh = DescribeAutoRefresh();
            sb.AppendLine($"autoRefresh: {autoRefresh}");
            if (!everBusy && autoRefresh.StartsWith("DISABLED", StringComparison.Ordinal))
            {
                sb.AppendLine("  WARNING: nothing was compiling AND Auto Refresh is off. If you just edited a script,");
                sb.AppendLine("  Unity has not imported it yet — 'idle' here does NOT mean your code is loaded.");
                sb.AppendLine("  Call TriggerDomainReload(confirm:true, mode:'recompile') to force it.");
            }
            AppendAssemblyInfo(sb, assemblyName);

            if (sinceIndex < 0)
            {
                sb.Append("Console entry index unavailable — call GetConsoleLogs(severity:'error') to check for errors.");
                yield return sb.ToString();
                yield break;
            }

            string newErrors = ConsoleTools.GetConsoleLogs(
                severity: "error", maxEntries: 50, keyword: "", includeStackTrace: false, sinceIndex: sinceIndex);

            sb.AppendLine("--- new console errors since the wait began ---");
            sb.Append(newErrors);
            yield return sb.ToString();
        }

        /// <summary>
        /// Reports where an assembly was loaded from and when that file was written. This is the
        /// only reliable answer to "did my new code actually load" — isCompiling going false says
        /// a compile ended, not that the domain reloaded with the result, and after a reload the
        /// timestamp is the one thing that visibly changes.
        /// </summary>
        static void AppendAssemblyInfo(StringBuilder sb, string assemblyName)
        {
            string[] wanted = string.IsNullOrWhiteSpace(assemblyName)
                ? new[] { "Assembly-CSharp", "Assembly-CSharp-Editor" }
                : new[] { assemblyName.Trim() };
            bool explicitRequest = !string.IsNullOrWhiteSpace(assemblyName);

            foreach (string raw in wanted)
            {
                string name = raw.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? raw.Substring(0, raw.Length - 4)
                    : raw;

                System.Reflection.Assembly found = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = asm;
                        break;
                    }
                }

                if (found == null)
                {
                    // Silent for the defaults: a project with no loose scripts in Assets/ simply has
                    // no Assembly-CSharp, and saying so every call would be noise.
                    if (explicitRequest)
                        sb.AppendLine($"assembly '{name}': NOT LOADED (no such assembly in the current domain)");
                    continue;
                }

                string location;
                try { location = found.Location; }
                catch (NotSupportedException) { location = null; }   // dynamic assemblies throw

                if (string.IsNullOrEmpty(location))
                {
                    sb.AppendLine($"assembly '{name}': loaded, but has no file on disk (dynamic assembly)");
                    continue;
                }

                try
                {
                    var info = new FileInfo(location);
                    sb.AppendLine(info.Exists
                        ? $"assembly '{name}': {location}\n  built: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss} ({(DateTime.Now - info.LastWriteTime).TotalSeconds:F0}s ago)"
                        : $"assembly '{name}': {location} (file no longer on disk — the domain is holding a deleted build)");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"assembly '{name}': {location} (timestamp unreadable: {ex.GetType().Name})");
                }
            }
        }

        [AgentTool(@"Force a Unity Editor domain reload (assembly reload).
Useful for testing InitializeOnLoad / [InitializeOnLoadMethod] behavior, clearing static field state,
verifying serialization survives a reload, or recovering from stale references.

mode:
  'reload' (default) — EditorUtility.RequestScriptReload(). Managed-only reload, no recompile.
  'recompile'        — CompilationPipeline.RequestScriptCompilation(). Recompile then reload
                       (no-op if no .cs file is dirty; touch a script first if you need a guaranteed compile).

Pass confirm=true to proceed. The MCP bridge will briefly disconnect during the reload, and
unsaved EditorWindow state that isn't [SerializeField] will be lost. Cannot run while in Play mode
or while Unity is already compiling/updating.")]
        public static string TriggerDomainReload(bool confirm = false, string mode = "reload")
        {
            if (!confirm)
                return "Error: Dangerous operation - pass confirm=true to proceed. This will trigger a Unity domain reload and briefly disconnect the MCP bridge.";

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return "Error: Cannot trigger domain reload while in Play mode. Exit Play mode first.";
            if (EditorApplication.isCompiling)
                return "Error: Unity is already compiling. Wait for compilation to finish.";
            if (EditorApplication.isUpdating)
                return "Error: Unity is already updating (asset import / domain reload in progress).";

            string normalized = (mode ?? "reload").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "reload":
                case "":
                    EditorUtility.RequestScriptReload();
                    return "Success: RequestScriptReload queued. Domain reload will start on the next editor tick. MCP bridge will briefly disconnect.";
                case "recompile":
                case "compile":
                    CompilationPipeline.RequestScriptCompilation();
                    return "Success: RequestScriptCompilation queued. Unity will recompile dirty scripts (no-op if none dirty) and then reload. MCP bridge will briefly disconnect.";
                default:
                    return $"Error: Unknown mode '{mode}'. Expected 'reload' or 'recompile'.";
            }
        }
    }
}
