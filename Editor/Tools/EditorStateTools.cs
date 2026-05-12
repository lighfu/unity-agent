using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Editor / Play mode の状態を観測するツール群。
    /// 「今 Edit か Play か」「compile 中か」などを判別するためのエージェント向け入口。
    /// </summary>
    public static class EditorStateTools
    {
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
