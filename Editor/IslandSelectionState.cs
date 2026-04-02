using UnityEngine;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class IslandSelectionState
    {
        // Mode
        public static bool IsActive;
        public static Renderer ActiveRenderer;
        public static List<UVIsland> Islands;
        public static HashSet<int> SelectedIndices = new HashSet<int>();
        public static int HoveredIslandIndex = -1;
        public static bool ShowColorPanel;

        // Raycast cache
        private static Mesh _bakedMesh;
        private static Dictionary<int, int> _triangleToIsland; // tri index → island index

        // Color panel settings — Gradient
        public static string GradientFromColor = "#FFFFFF";
        public static string GradientToColor = "#000000";
        public static int GradientDirectionIndex = 0;  // 0=top_to_bottom, 1=bottom_to_top, 2=left_to_right, 3=right_to_left
        public static int GradientBlendModeIndex = 0;   // 0=tint, 1=multiply, 2=replace
        public static float GradientStartT = 0f;
        public static float GradientEndT = 1f;

        // Color panel settings — HSV
        public static float HueShift = 0f;
        public static float SaturationScale = 1f;
        public static float ValueScale = 1f;

        public static string[] DirectionLabels => new[] { M("上→下"), M("下→上"), M("左→右"), M("右→左") };
        public static readonly string[] DirectionValues = { "top_to_bottom", "bottom_to_top", "left_to_right", "right_to_left" };
        public static string[] BlendModeLabels => new[] { M("スクリーン"), M("オーバーレイ"), M("ティント"), M("乗算"), M("置換") };
        public static readonly string[] BlendModeValues = { "screen", "overlay", "tint", "multiply", "replace" };

        public static void Activate(Renderer renderer)
        {
            ActiveRenderer = renderer;

            Mesh mesh = GetMesh(renderer);
            if (mesh == null) return;

            Islands = UVIslandDetector.DetectIslands(mesh);

            // BakeMesh for SkinnedMeshRenderer
            if (renderer is SkinnedMeshRenderer smr)
            {
                _bakedMesh = new Mesh();
                smr.BakeMesh(_bakedMesh);
            }
            else
            {
                _bakedMesh = mesh;
            }

            // Build triangle → island lookup
            _triangleToIsland = new Dictionary<int, int>();
            for (int i = 0; i < Islands.Count; i++)
            {
                foreach (int triIdx in Islands[i].triangleIndices)
                {
                    _triangleToIsland[triIdx] = i;
                }
            }

            SelectedIndices.Clear();
            HoveredIslandIndex = -1;
            ShowColorPanel = true;
            IsActive = true;
        }

        public static void Deactivate()
        {
            // Destroy baked mesh before nulling renderer (only if we created it for SkinnedMeshRenderer)
            if (_bakedMesh != null && ActiveRenderer is SkinnedMeshRenderer)
                UnityEngine.Object.DestroyImmediate(_bakedMesh);
            _bakedMesh = null;
            _triangleToIsland = null;

            IsActive = false;
            ActiveRenderer = null;
            Islands = null;
            SelectedIndices.Clear();
            HoveredIslandIndex = -1;
            ShowColorPanel = false;

            // Reset color panel defaults
            GradientFromColor = "#FFFFFF";
            GradientToColor = "#000000";
            GradientDirectionIndex = 0;
            GradientBlendModeIndex = 0;
            GradientStartT = 0f;
            GradientEndT = 1f;
            HueShift = 0f;
            SaturationScale = 1f;
            ValueScale = 1f;
        }

        public static void ToggleIsland(int index)
        {
            if (index < 0 || Islands == null || index >= Islands.Count) return;
            if (!SelectedIndices.Remove(index))
                SelectedIndices.Add(index);
        }

        public static void SelectAll()
        {
            if (Islands == null) return;
            for (int i = 0; i < Islands.Count; i++)
                SelectedIndices.Add(i);
        }

        public static void DeselectAll()
        {
            SelectedIndices.Clear();
        }

        public static void InvertSelection()
        {
            if (Islands == null) return;
            var newSet = new HashSet<int>();
            for (int i = 0; i < Islands.Count; i++)
            {
                if (!SelectedIndices.Contains(i))
                    newSet.Add(i);
            }
            SelectedIndices = newSet;
        }

        /// <summary>
        /// Returns selected indices as "0;1;3" format for TextureEditTools.
        /// </summary>
        public static string GetIslandIndicesString()
        {
            if (SelectedIndices.Count == 0) return "";
            var sorted = new List<int>(SelectedIndices);
            sorted.Sort();
            return string.Join(";", sorted);
        }

        public static Mesh GetBakedMesh() => _bakedMesh;
        public static Dictionary<int, int> GetTriangleToIsland() => _triangleToIsland;

        /// <summary>
        /// Get hierarchy path for GameObject.Find (e.g. "AvatarRoot/Body").
        /// </summary>
        public static string GetGameObjectPath()
        {
            if (ActiveRenderer == null) return "";
            return GetHierarchyPath(ActiveRenderer.transform);
        }

        private static string GetHierarchyPath(Transform t)
        {
            // GameObject.Find uses "/" path from root
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer is MeshRenderer) return renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }
    }
}
