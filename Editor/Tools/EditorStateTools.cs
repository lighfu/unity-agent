using UnityEngine;
using UnityEditor;
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
    }
}
