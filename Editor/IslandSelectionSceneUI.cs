using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [InitializeOnLoad]
    public static class IslandSelectionSceneUI
    {
        private static bool _gradientFoldout = true;
        private static bool _hsvFoldout = true;

        static IslandSelectionSceneUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!IslandSelectionState.IsActive) return;
            if (IslandSelectionState.ActiveRenderer == null || IslandSelectionState.Islands == null)
            {
                IslandSelectionState.Deactivate();
                return;
            }

            HandleMouseInput(sceneView);
            DrawIslandHighlights();

            Handles.BeginGUI();
            DrawToolbar();
            if (IslandSelectionState.ShowColorPanel)
                DrawColorPanel(sceneView);
            Handles.EndGUI();
        }

        // ─── Mouse Input & Raycast ───

        private static void HandleMouseInput(SceneView sceneView)
        {
            Event e = Event.current;
            if (e == null) return;

            // Suppress default scene click while hovering an island (but keep Alt+drag orbit)
            if (!e.alt && IslandSelectionState.HoveredIslandIndex >= 0)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                int hit = RaycastToIsland(e.mousePosition);
                if (hit != IslandSelectionState.HoveredIslandIndex)
                {
                    IslandSelectionState.HoveredIslandIndex = hit;
                    sceneView.Repaint();
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                int hit = RaycastToIsland(e.mousePosition);
                if (hit >= 0)
                {
                    IslandSelectionState.ToggleIsland(hit);
                    e.Use();
                    sceneView.Repaint();
                }
            }
        }

        private static int RaycastToIsland(Vector2 mousePosition)
        {
            var renderer = IslandSelectionState.ActiveRenderer;
            var bakedMesh = IslandSelectionState.GetBakedMesh();
            var triToIsland = IslandSelectionState.GetTriangleToIsland();
            if (renderer == null || bakedMesh == null || triToIsland == null) return -1;

            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            Matrix4x4 worldToLocal = renderer.transform.worldToLocalMatrix;

            Vector3 localOrigin = worldToLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 localDir = worldToLocal.MultiplyVector(worldRay.direction).normalized;

            Vector3[] verts = bakedMesh.vertices;
            int[] tris = bakedMesh.triangles;

            float closestDist = float.MaxValue;
            int closestTriIndex = -1;

            for (int i = 0; i < tris.Length; i += 3)
            {
                int triIndex = i / 3;
                Vector3 v0 = verts[tris[i]];
                Vector3 v1 = verts[tris[i + 1]];
                Vector3 v2 = verts[tris[i + 2]];

                if (RayTriangleIntersect(localOrigin, localDir, v0, v1, v2, out float dist))
                {
                    if (dist > 0 && dist < closestDist)
                    {
                        closestDist = dist;
                        closestTriIndex = triIndex;
                    }
                }
            }

            if (closestTriIndex >= 0 && triToIsland.TryGetValue(closestTriIndex, out int islandIdx))
                return islandIdx;

            return -1;
        }

        /// <summary>
        /// Möller–Trumbore ray-triangle intersection.
        /// </summary>
        private static bool RayTriangleIntersect(Vector3 origin, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            const float EPSILON = 1e-8f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 h = Vector3.Cross(dir, e2);
            float a = Vector3.Dot(e1, h);

            if (a < EPSILON) return false; // backface cull + parallel

            float f = 1f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(dir, q);
            if (v < 0f || u + v > 1f) return false;

            t = f * Vector3.Dot(e2, q);
            return t > EPSILON;
        }

        // ─── Highlight Drawing ───

        private static void DrawIslandHighlights()
        {
            var renderer = IslandSelectionState.ActiveRenderer;
            var islands = IslandSelectionState.Islands;
            var bakedMesh = IslandSelectionState.GetBakedMesh();
            if (renderer == null || islands == null || bakedMesh == null) return;

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
            Vector3[] verts = bakedMesh.vertices;

            // Use shared mesh triangles (indices match islands)
            Mesh sharedMesh = GetSharedMesh(renderer);
            if (sharedMesh == null) return;
            int[] tris = sharedMesh.triangles;

            // Draw selected islands
            foreach (int idx in IslandSelectionState.SelectedIndices)
            {
                if (idx < 0 || idx >= islands.Count) continue;
                var island = islands[idx];

                // Cyan fill
                Handles.color = new Color(0f, 1f, 1f, 0.25f);
                DrawIslandFaces(island, verts, tris, localToWorld);

                // Cyan wireframe
                Handles.color = new Color(0f, 1f, 1f, 1f);
                DrawIslandWireframe(island, verts, tris, localToWorld);
            }

            // Draw hovered island (if not selected)
            int hovered = IslandSelectionState.HoveredIslandIndex;
            if (hovered >= 0 && hovered < islands.Count && !IslandSelectionState.SelectedIndices.Contains(hovered))
            {
                var island = islands[hovered];

                // Yellow fill
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawIslandFaces(island, verts, tris, localToWorld);

                // Yellow wireframe
                Handles.color = new Color(1f, 1f, 0f, 1f);
                DrawIslandWireframe(island, verts, tris, localToWorld);
            }
        }

        private static void DrawIslandFaces(UVIsland island, Vector3[] verts, int[] tris, Matrix4x4 localToWorld)
        {
            foreach (int triIdx in island.triangleIndices)
            {
                Vector3 v0 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3]]);
                Vector3 v1 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 1]]);
                Vector3 v2 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 2]]);
                Handles.DrawAAConvexPolygon(v0, v1, v2);
            }
        }

        private static void DrawIslandWireframe(UVIsland island, Vector3[] verts, int[] tris, Matrix4x4 localToWorld)
        {
            foreach (int triIdx in island.triangleIndices)
            {
                Vector3 v0 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3]]);
                Vector3 v1 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 1]]);
                Vector3 v2 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 2]]);
                Handles.DrawPolyLine(v0, v1, v2, v0);
            }
        }

        // ─── Toolbar (top-left) ───

        private static void DrawToolbar()
        {
            var renderer = IslandSelectionState.ActiveRenderer;
            var islands = IslandSelectionState.Islands;
            string rendererName = renderer != null ? renderer.name : "?";
            int total = islands != null ? islands.Count : 0;
            int selected = IslandSelectionState.SelectedIndices.Count;

            var rect = new Rect(10, 10, 280, 95);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            GUILayout.Label(string.Format(M("アイランド選択: {0}"), rendererName), EditorStyles.boldLabel);
            GUILayout.Label(string.Format(M("選択中: {0} / {1} アイランド"), selected, total), EditorStyles.miniLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(M("全選択"), EditorStyles.miniButton))
                IslandSelectionState.SelectAll();
            if (GUILayout.Button(M("選択解除"), EditorStyles.miniButton))
                IslandSelectionState.DeselectAll();
            if (GUILayout.Button(M("反転"), EditorStyles.miniButton))
                IslandSelectionState.InvertSelection();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string panelLabel = IslandSelectionState.ShowColorPanel ? M("カラーパネル") + " \u25B2" : M("カラーパネル") + " \u25BC";
            if (GUILayout.Button(panelLabel, EditorStyles.miniButton))
                IslandSelectionState.ShowColorPanel = !IslandSelectionState.ShowColorPanel;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("\u00D7 " + M("終了"), EditorStyles.miniButton))
                IslandSelectionState.Deactivate();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // ─── Color Panel (top-right) ───

        private static void DrawColorPanel(SceneView sceneView)
        {
            float panelWidth = 280f;
            float panelHeight = 370f;
            float panelX = sceneView.position.width - panelWidth - 10;
            float panelY = 10f;

            var rect = new Rect(panelX, panelY, panelWidth, panelHeight);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            float innerX = rect.x + 8;
            float innerWidth = rect.width - 16;
            float y = rect.y + 8;
            float lineHeight = 18f;
            float spacing = 2f;

            // Title
            GUI.Label(new Rect(innerX, y, innerWidth, lineHeight), M("カラー操作"), EditorStyles.boldLabel);
            y += lineHeight + spacing + 4;

            // ─ Gradient section ─
            _gradientFoldout = EditorGUI.Foldout(new Rect(innerX, y, innerWidth, lineHeight), _gradientFoldout, M("グラデーション"), true);
            y += lineHeight + spacing;

            if (_gradientFoldout)
            {
                // From
                GUI.Label(new Rect(innerX, y, 40, lineHeight), M("開始:"));
                IslandSelectionState.GradientFromColor = GUI.TextField(new Rect(innerX + 42, y, innerWidth - 42, lineHeight), IslandSelectionState.GradientFromColor);
                y += lineHeight + spacing;

                // To
                GUI.Label(new Rect(innerX, y, 40, lineHeight), M("終了:"));
                IslandSelectionState.GradientToColor = GUI.TextField(new Rect(innerX + 42, y, innerWidth - 42, lineHeight), IslandSelectionState.GradientToColor);
                y += lineHeight + spacing;

                // Direction
                GUI.Label(new Rect(innerX, y, 40, lineHeight), M("方向:"));
                IslandSelectionState.GradientDirectionIndex = EditorGUI.Popup(
                    new Rect(innerX + 42, y, innerWidth - 42, lineHeight),
                    IslandSelectionState.GradientDirectionIndex,
                    IslandSelectionState.DirectionLabels);
                y += lineHeight + spacing;

                // Blend
                GUI.Label(new Rect(innerX, y, 40, lineHeight), M("合成:"));
                IslandSelectionState.GradientBlendModeIndex = EditorGUI.Popup(
                    new Rect(innerX + 42, y, innerWidth - 42, lineHeight),
                    IslandSelectionState.GradientBlendModeIndex,
                    IslandSelectionState.BlendModeLabels);
                y += lineHeight + spacing;

                // Range
                GUI.Label(new Rect(innerX, y, 40, lineHeight), M("範囲:"));
                float rangeFieldW = (innerWidth - 42 - 10) / 2f;
                IslandSelectionState.GradientStartT = EditorGUI.FloatField(new Rect(innerX + 42, y, rangeFieldW, lineHeight), IslandSelectionState.GradientStartT);
                IslandSelectionState.GradientEndT = EditorGUI.FloatField(new Rect(innerX + 42 + rangeFieldW + 10, y, rangeFieldW, lineHeight), IslandSelectionState.GradientEndT);
                y += lineHeight + spacing + 2;

                // Apply button
                bool noSelection = IslandSelectionState.SelectedIndices.Count == 0;
                EditorGUI.BeginDisabledGroup(noSelection);
                if (GUI.Button(new Rect(innerX, y, innerWidth, 22), M("グラデーション適用")))
                    ApplyGradientToSelection();
                EditorGUI.EndDisabledGroup();
                y += 26;
            }

            y += 4;

            // ─ HSV section ─
            _hsvFoldout = EditorGUI.Foldout(new Rect(innerX, y, innerWidth, lineHeight), _hsvFoldout, M("HSV調整"), true);
            y += lineHeight + spacing;

            if (_hsvFoldout)
            {
                // Hue
                GUI.Label(new Rect(innerX, y, 36, lineHeight), M("色相:"));
                IslandSelectionState.HueShift = EditorGUI.Slider(new Rect(innerX + 36, y, innerWidth - 36, lineHeight), IslandSelectionState.HueShift, -180f, 180f);
                y += lineHeight + spacing;

                // Saturation
                GUI.Label(new Rect(innerX, y, 36, lineHeight), M("彩度:"));
                IslandSelectionState.SaturationScale = EditorGUI.Slider(new Rect(innerX + 36, y, innerWidth - 36, lineHeight), IslandSelectionState.SaturationScale, 0f, 2f);
                y += lineHeight + spacing;

                // Value
                GUI.Label(new Rect(innerX, y, 36, lineHeight), M("明度:"));
                IslandSelectionState.ValueScale = EditorGUI.Slider(new Rect(innerX + 36, y, innerWidth - 36, lineHeight), IslandSelectionState.ValueScale, 0f, 2f);
                y += lineHeight + spacing + 2;

                // Apply button
                bool noSelection = IslandSelectionState.SelectedIndices.Count == 0;
                EditorGUI.BeginDisabledGroup(noSelection);
                if (GUI.Button(new Rect(innerX, y, innerWidth, 22), M("HSV適用")))
                    ApplyHSVToSelection();
                EditorGUI.EndDisabledGroup();
                y += 26;
            }

            y += 6;

            // Info message
            if (IslandSelectionState.SelectedIndices.Count == 0)
            {
                EditorGUI.HelpBox(new Rect(innerX, y, innerWidth, 36), M("Sceneでアイランドをクリックして選択してください。"), MessageType.Info);
            }
        }

        // ─── Apply Operations ───

        private static void ApplyGradientToSelection()
        {
            string path = IslandSelectionState.GetGameObjectPath();
            string indices = IslandSelectionState.GetIslandIndicesString();
            string fromColor = IslandSelectionState.GradientFromColor;
            string toColor = IslandSelectionState.GradientToColor;
            string direction = IslandSelectionState.DirectionValues[IslandSelectionState.GradientDirectionIndex];
            string blendMode = IslandSelectionState.BlendModeValues[IslandSelectionState.GradientBlendModeIndex];
            float startT = IslandSelectionState.GradientStartT;
            float endT = IslandSelectionState.GradientEndT;

            string result = TextureEditTools.ApplyGradientEx(path, fromColor, toColor, direction, blendMode, indices, startT, endT);
            Debug.Log($"[IslandSelection] Gradient: {result}");
            SceneView.RepaintAll();
        }

        private static void ApplyHSVToSelection()
        {
            string path = IslandSelectionState.GetGameObjectPath();
            string indices = IslandSelectionState.GetIslandIndicesString();
            float hue = IslandSelectionState.HueShift;
            float sat = IslandSelectionState.SaturationScale;
            float val = IslandSelectionState.ValueScale;

            string result = TextureEditTools.AdjustHSV(path, hue, sat, val, indices);
            Debug.Log($"[IslandSelection] HSV: {result}");
            SceneView.RepaintAll();
        }

        // ─── Helpers ───

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer is MeshRenderer) return renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }
    }
}
