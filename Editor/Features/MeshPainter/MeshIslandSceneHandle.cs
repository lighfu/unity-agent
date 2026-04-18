using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [InitializeOnLoad]
    public static class MeshIslandSceneHandle
    {
        static MeshIslandSceneHandle()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            // アイランド選択モード中はこちらの描画をスキップ（IslandSelectionSceneUIが担当）
            if (IslandSelectionState.IsActive) return;

            var renderer = MeshIslandTools._highlightedRenderer;
            if (renderer == null) return;

            var islands = MeshIslandTools._highlightedIslands;
            int highlightIdx = MeshIslandTools._highlightedIslandIndex;
            if (islands == null || islands.Count == 0) return;

            Mesh mesh = GetMesh(renderer);
            if (mesh == null) return;

            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;

            // SkinnedMeshRenderer 対応
            if (renderer is SkinnedMeshRenderer smr)
            {
                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                verts = bakedMesh.vertices;
            }

            int[] tris = mesh.triangles;

            // 色予約済みアイランドを薄く色付け表示
            var metadata = MeshIslandTools._highlightedMetadata;
            if (metadata != null)
            {
                foreach (var setting in metadata.islandColors)
                {
                    if (setting.islandIndex < 0 || setting.islandIndex >= islands.Count) continue;
                    if (setting.islandIndex == highlightIdx) continue; // 選択中は別描画

                    Handles.color = new Color(setting.color.r, setting.color.g, setting.color.b, 0.3f);
                    DrawIslandFaces(islands[setting.islandIndex], verts, tris, localToWorld);
                }
            }

            // 選択中のアイランドをハイライト
            if (highlightIdx >= 0 && highlightIdx < islands.Count)
            {
                var island = islands[highlightIdx];

                // 半透明面で塗りつぶし
                Color fillColor = Color.yellow;
                if (metadata != null)
                {
                    var setting = metadata.islandColors.Find(s => s.islandIndex == highlightIdx);
                    if (setting != null)
                        fillColor = setting.color;
                }
                Handles.color = new Color(fillColor.r, fillColor.g, fillColor.b, 0.25f);
                DrawIslandFaces(island, verts, tris, localToWorld);

                // 黄色ワイヤーフレームで輪郭
                Handles.color = Color.yellow;
                foreach (int triIdx in island.triangleIndices)
                {
                    Vector3 v0 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3]]);
                    Vector3 v1 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 1]]);
                    Vector3 v2 = localToWorld.MultiplyPoint3x4(verts[tris[triIdx * 3 + 2]]);
                    Handles.DrawPolyLine(v0, v1, v2, v0);
                }

                // GUI オーバーレイ
                DrawOverlay(island, highlightIdx, metadata);
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

        private static void DrawOverlay(UVIsland island, int islandIndex, MeshPaintMetadata metadata)
        {
            Handles.BeginGUI();

            var rect = new Rect(10, 10, 220, 70);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            GUILayout.Label(string.Format(M("アイランド #{0}"), islandIndex), EditorStyles.boldLabel);
            GUILayout.Label(string.Format(M("三角形: {0}"), island.triangleIndices.Count), EditorStyles.miniLabel);

            if (metadata != null)
            {
                var setting = metadata.islandColors.Find(s => s.islandIndex == islandIndex);
                if (setting != null)
                {
                    GUILayout.Label(string.Format(M("予約色: RGB({0:F2}, {1:F2}, {2:F2})"), setting.color.r, setting.color.g, setting.color.b), EditorStyles.miniLabel);
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (renderer is MeshRenderer) return renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }
    }
}
