using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// LODGroup の追加・設定・検査ツール。
    /// VRChat アバターのパフォーマンス最適化に使用。
    /// </summary>
    public static class LODTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool(@"Add a LODGroup to a GameObject and configure LOD levels.
screenHeights: semicolon-separated screen relative transition heights (0-1), from highest LOD to lowest.
  Example: '0.6;0.3;0.1' means LOD0 at >60%, LOD1 at 30-60%, LOD2 at 10-30%, culled at <10%.
Each LOD level uses the Renderers from child GameObjects named 'LOD0', 'LOD1', etc. if they exist,
otherwise the first Renderer found on the target.")]
        public static string AddLODGroup(string goName, string screenHeights = "0.6;0.3;0.1")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            if (go.GetComponent<LODGroup>() != null)
                return $"Error: '{goName}' already has a LODGroup.";

            var heights = ParseFloatList(screenHeights);
            if (heights == null || heights.Length == 0)
                return "Error: Invalid screenHeights. Use semicolon-separated values like '0.6;0.3;0.1'.";

            var lodGroup = Undo.AddComponent<LODGroup>(go);
            var lods = new LOD[heights.Length];

            for (int i = 0; i < heights.Length; i++)
            {
                // Try to find child named LOD0, LOD1, etc.
                var lodChild = go.transform.Find($"LOD{i}");
                Renderer[] renderers;
                if (lodChild != null)
                    renderers = lodChild.GetComponentsInChildren<Renderer>(true);
                else if (i == 0)
                    renderers = go.GetComponentsInChildren<Renderer>(true);
                else
                    renderers = new Renderer[0];

                lods[i] = new LOD(heights[i], renderers);
            }

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

            EditorUtility.SetDirty(lodGroup);
            return $"Success: Added LODGroup to '{goName}' with {heights.Length} levels ({string.Join(", ", heights.Select(h => $"{h:F2}"))}).";
        }

        [AgentTool(@"Configure LOD levels on an existing LODGroup.
screenHeights: semicolon-separated transition heights.
rendererPaths: semicolon-separated groups of renderer GameObject paths, groups separated by '|'.
  Example: 'MeshHigh|MeshMid;MeshMid2|MeshLow' = LOD0 uses MeshHigh, LOD1 uses MeshMid+MeshMid2, LOD2 uses MeshLow.
fadeMode: 0=None, 1=CrossFade, 2=SpeedTree.")]
        public static string ConfigureLODGroup(string goName, string screenHeights = "", string rendererPaths = "",
            int fadeMode = -1, int animateCrossFading = -1, int lastLODBillboard = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup == null) return $"Error: No LODGroup on '{goName}'.";

            Undo.RecordObject(lodGroup, "Configure LODGroup");

            if (fadeMode >= 0) lodGroup.fadeMode = (LODFadeMode)fadeMode;
            if (animateCrossFading >= 0) lodGroup.animateCrossFading = animateCrossFading != 0;

            if (!string.IsNullOrEmpty(screenHeights))
            {
                var heights = ParseFloatList(screenHeights);
                if (heights == null) return "Error: Invalid screenHeights format.";

                var lods = new LOD[heights.Length];

                if (!string.IsNullOrEmpty(rendererPaths))
                {
                    var groups = rendererPaths.Split('|');
                    for (int i = 0; i < heights.Length; i++)
                    {
                        var renderers = new List<Renderer>();
                        if (i < groups.Length)
                        {
                            var paths = groups[i].Split(';');
                            foreach (var path in paths)
                            {
                                var rGo = FindGO(path.Trim());
                                if (rGo != null)
                                    renderers.AddRange(rGo.GetComponentsInChildren<Renderer>(true));
                            }
                        }
                        lods[i] = new LOD(heights[i], renderers.ToArray());
                    }
                }
                else
                {
                    // Keep existing renderers where possible
                    var existingLods = lodGroup.GetLODs();
                    for (int i = 0; i < heights.Length; i++)
                    {
                        Renderer[] renderers = (i < existingLods.Length) ? existingLods[i].renderers : new Renderer[0];
                        lods[i] = new LOD(heights[i], renderers);
                    }
                }

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }

            EditorUtility.SetDirty(lodGroup);
            return $"Success: Configured LODGroup on '{goName}'.";
        }

        [AgentTool("Inspect a LODGroup. Shows LOD levels, screen heights, renderers, and settings.")]
        public static string InspectLODGroup(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup == null) return $"Error: No LODGroup on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"LODGroup on '{goName}':");
            sb.AppendLine($"  Enabled: {lodGroup.enabled}");
            sb.AppendLine($"  LOD Count: {lodGroup.lodCount}");
            sb.AppendLine($"  FadeMode: {lodGroup.fadeMode}");
            sb.AppendLine($"  AnimateCrossFading: {lodGroup.animateCrossFading}");
            sb.AppendLine($"  Size: {lodGroup.size:F3}");
            sb.AppendLine($"  LocalReferencePoint: {lodGroup.localReferencePoint}");

            var lods = lodGroup.GetLODs();
            sb.AppendLine($"  LOD Levels ({lods.Length}):");
            for (int i = 0; i < lods.Length; i++)
            {
                var lod = lods[i];
                int totalTris = 0;
                foreach (var r in lod.renderers)
                {
                    if (r is MeshRenderer mr)
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) totalTris += mf.sharedMesh.triangles.Length / 3;
                    }
                    else if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        totalTris += smr.sharedMesh.triangles.Length / 3;
                }
                sb.AppendLine($"    LOD{i}: height={lod.screenRelativeTransitionHeight:F3}, renderers={lod.renderers.Length}, tris~{totalTris}, fade={lod.fadeTransitionWidth:F2}");
                foreach (var r in lod.renderers)
                    if (r != null) sb.AppendLine($"      {r.gameObject.name} ({r.GetType().Name})");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Force a specific LOD level on a LODGroup. index=-1 for automatic. Useful for previewing LOD levels.")]
        public static string ForceLOD(string goName, int lodIndex)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup == null) return $"Error: No LODGroup on '{goName}'.";

            lodGroup.ForceLOD(lodIndex);
            return $"Success: Forced LOD{lodIndex} on '{goName}' ({(lodIndex < 0 ? "auto" : "manual")}).";
        }

        [AgentTool("List all LODGroup components in the scene.")]
        public static string ListLODGroups()
        {
            var lodGroups = Object.FindObjectsOfType<LODGroup>(true);
            if (lodGroups.Length == 0) return "No LODGroup components in the scene.";

            var sb = new StringBuilder();
            sb.AppendLine($"LODGroups in scene ({lodGroups.Length}):");
            foreach (var lg in lodGroups.OrderBy(l => l.gameObject.name))
            {
                string path = GetHierarchyPath(lg.transform);
                var lods = lg.GetLODs();
                sb.AppendLine($"  {path}: {lods.Length} levels, fade={lg.fadeMode}");
            }
            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static float[] ParseFloatList(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var parts = input.Split(';');
            var result = new List<float>();
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            foreach (var p in parts)
            {
                if (float.TryParse(p.Trim(), ns, ic, out float v))
                    result.Add(v);
            }
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null) { sb.Insert(0, current.name + "/"); current = current.parent; }
            return sb.ToString();
        }
    }
}
