using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AudioSource / AudioClip の追加・設定・検査ツール。
    /// VRChat アバターサウンドの設定に使用。
    /// </summary>
    public static class AudioTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Add an AudioSource to a GameObject. clipPath is optional asset path to an AudioClip. spatialBlend: 0=2D, 1=3D.")]
        public static string AddAudioSource(string goName, string clipPath = "", bool playOnAwake = false, bool loop = false,
            float volume = 1f, float spatialBlend = 1f)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var source = Undo.AddComponent<AudioSource>(go);
            source.playOnAwake = playOnAwake;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = spatialBlend;

            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null)
                    source.clip = clip;
                else
                    return $"Warning: Added AudioSource but clip not found at '{clipPath}'.";
            }

            return $"Success: Added AudioSource to '{goName}' (volume={volume}, spatial={spatialBlend}, loop={loop}).";
        }

        [AgentTool("Configure an existing AudioSource. Use -1 for unchanged float values. sourceIndex selects which AudioSource if multiple exist.")]
        public static string ConfigureAudioSource(string goName, int sourceIndex = 0, float volume = -1f, float pitch = -1f,
            float spatialBlend = -1f, float minDistance = -1f, float maxDistance = -1f,
            int loop = -1, int playOnAwake = -1, string clipPath = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var sources = go.GetComponents<AudioSource>();
            if (sources.Length == 0)
                return $"Error: No AudioSource on '{goName}'.";
            if (sourceIndex < 0 || sourceIndex >= sources.Length)
                return $"Error: AudioSource index {sourceIndex} out of range (0-{sources.Length - 1}).";

            var source = sources[sourceIndex];
            Undo.RecordObject(source, "Configure AudioSource via Agent");

            if (volume >= 0) source.volume = volume;
            if (pitch >= 0) source.pitch = pitch;
            if (spatialBlend >= 0) source.spatialBlend = spatialBlend;
            if (minDistance >= 0) source.minDistance = minDistance;
            if (maxDistance >= 0) source.maxDistance = maxDistance;
            if (loop >= 0) source.loop = loop != 0;
            if (playOnAwake >= 0) source.playOnAwake = playOnAwake != 0;

            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null) source.clip = clip;
                else return $"Warning: Configured AudioSource but clip not found at '{clipPath}'.";
            }

            EditorUtility.SetDirty(source);
            return $"Success: Configured AudioSource[{sourceIndex}] on '{goName}'.";
        }

        [AgentTool("Inspect all AudioSource components on a GameObject. Shows clip, volume, spatial settings, and 3D sound properties.")]
        public static string InspectAudioSources(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var sources = go.GetComponents<AudioSource>();
            if (sources.Length == 0)
                return $"No AudioSource components on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"AudioSources on '{goName}' ({sources.Length}):");

            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                sb.AppendLine($"  [{i}] clip={s.clip?.name ?? "none"}, volume={s.volume:F2}, pitch={s.pitch:F2}");
                sb.AppendLine($"       playOnAwake={s.playOnAwake}, loop={s.loop}, mute={s.mute}");
                sb.AppendLine($"       spatialBlend={s.spatialBlend:F2} ({(s.spatialBlend < 0.5f ? "2D" : "3D")})");
                if (s.spatialBlend > 0)
                    sb.AppendLine($"       minDist={s.minDistance}, maxDist={s.maxDistance}, rolloff={s.rolloffMode}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Search for AudioClip assets in the project. Returns paths to matching audio files.")]
        public static string SearchAudioClips(string nameFilter = "", string searchFolder = "Assets")
        {
            string filter = "t:AudioClip";
            string[] guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });

            if (guids.Length == 0)
                return $"No AudioClip assets found in '{searchFolder}'.";

            var sb = new StringBuilder();
            int count = 0;
            bool hasFilter = !string.IsNullOrEmpty(nameFilter);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (hasFilter && !path.ToLower().Contains(nameFilter.ToLower()))
                    continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    sb.AppendLine($"  {path} ({clip.length:F1}s, {clip.channels}ch, {clip.frequency}Hz)");
                    count++;
                    if (count >= 50) { sb.AppendLine("  ... (limit 50 reached)"); break; }
                }
            }

            if (count == 0)
                return hasFilter ? $"No AudioClip matching '{nameFilter}' in '{searchFolder}'." : $"No AudioClip found in '{searchFolder}'.";

            sb.Insert(0, $"AudioClips found ({count}):\n");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all AudioSource components in the scene (useful for auditing avatar sounds).")]
        public static string ListAllAudioSources()
        {
            var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>(true);
            if (sources.Length == 0)
                return "No AudioSource components in the scene.";

            var sb = new StringBuilder();
            sb.AppendLine($"AudioSources in scene ({sources.Length}):");

            foreach (var s in sources.OrderBy(s => s.gameObject.name))
            {
                string path = GetHierarchyPath(s.transform);
                sb.AppendLine($"  {path}: clip={s.clip?.name ?? "none"}, vol={s.volume:F2}, spatial={s.spatialBlend:F1}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }
    }
}
