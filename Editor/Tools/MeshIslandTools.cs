using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MeshIslandTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // SceneView ハイライト用の状態（MeshIslandSceneHandle から参照）
        internal static Renderer _highlightedRenderer;
        internal static int _highlightedIslandIndex = -1;
        internal static List<UVIsland> _highlightedIslands;
        internal static MeshPaintMetadata _highlightedMetadata;

        [AgentTool("List all mesh islands (UV-connected triangle groups) on a GameObject's mesh. Returns island index, triangle count, UV bounds, and 3D bounds. use3DConnection=true groups by 3D vertex proximity instead of UV connectivity.")]
        public static string ListMeshIslands(string gameObjectName, bool use3DConnection = false)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var islands = UVIslandDetector.DetectIslands(mesh, use3DConnection);
            if (islands.Count == 0) return $"Result: No UV islands found on '{gameObjectName}'.";

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // 三角形数の大きい順にソート（インデックスを保持）
            var indexed = islands.Select((isl, idx) => new { island = isl, originalIndex = idx })
                .OrderByDescending(x => x.island.triangleIndices.Count)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Mesh Islands on '{gameObjectName}': {islands.Count} islands found (use3DConnection={use3DConnection})");
            sb.AppendLine("---");

            foreach (var item in indexed)
            {
                var isl = item.island;
                int idx = item.originalIndex;

                // 3D バウンド計算
                Vector3 min3D = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max3D = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                HashSet<int> vertexIndices = new HashSet<int>();

                foreach (int triIdx in isl.triangleIndices)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int vIdx = triangles[triIdx * 3 + j];
                        vertexIndices.Add(vIdx);
                        Vector3 v = vertices[vIdx];
                        min3D = Vector3.Min(min3D, v);
                        max3D = Vector3.Max(max3D, v);
                    }
                }

                Vector3 center3D = (min3D + max3D) * 0.5f;
                Vector3 size3D = max3D - min3D;

                sb.AppendLine($"[{idx}] Triangles: {isl.triangleIndices.Count}, Vertices: {vertexIndices.Count}");
                sb.AppendLine($"    UV Bounds: ({isl.uvBounds.xMin:F3}, {isl.uvBounds.yMin:F3}) - ({isl.uvBounds.xMax:F3}, {isl.uvBounds.yMax:F3})");
                sb.AppendLine($"    3D Center: ({center3D.x:F4}, {center3D.y:F4}, {center3D.z:F4}), Size: ({size3D.x:F4}, {size3D.y:F4}, {size3D.z:F4})");
            }

            return sb.ToString();
        }

        [AgentTool("Get detailed information about a specific mesh island by index. Includes triangle/vertex counts, UV bounds, 3D center, average color from texture, and submesh/material slot.")]
        public static string InspectMeshIsland(string gameObjectName, int islandIndex)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var islands = UVIslandDetector.DetectIslands(mesh, false);
            if (islandIndex < 0 || islandIndex >= islands.Count)
                return $"Error: Island index {islandIndex} out of range (0-{islands.Count - 1}).";

            var island = islands[islandIndex];
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            // 頂点・バウンド計算
            HashSet<int> vertexIndices = new HashSet<int>();
            Vector3 min3D = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max3D = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (int triIdx in island.triangleIndices)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vIdx = triangles[triIdx * 3 + j];
                    vertexIndices.Add(vIdx);
                    Vector3 v = vertices[vIdx];
                    min3D = Vector3.Min(min3D, v);
                    max3D = Vector3.Max(max3D, v);
                }
            }

            Vector3 center3D = (min3D + max3D) * 0.5f;

            // サブメッシュ特定
            int submeshIndex = GetSubmeshIndex(mesh, island);

            // テクスチャから平均色をサンプリング
            string avgColorStr = "N/A";
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = null;
                if (submeshIndex >= 0 && submeshIndex < renderer.sharedMaterials.Length)
                    mat = renderer.sharedMaterials[submeshIndex];
                else if (renderer.sharedMaterial != null)
                    mat = renderer.sharedMaterial;

                if (mat != null && mat.mainTexture is Texture2D tex)
                {
                    avgColorStr = SampleAverageColor(tex, uvs, triangles, island);
                }
            }

            // マテリアル情報
            string matName = "N/A";
            if (renderer != null && submeshIndex >= 0 && submeshIndex < renderer.sharedMaterials.Length)
            {
                var m = renderer.sharedMaterials[submeshIndex];
                if (m != null) matName = m.name;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Island #{islandIndex} on '{gameObjectName}':");
            sb.AppendLine($"  Triangles: {island.triangleIndices.Count}");
            sb.AppendLine($"  Vertices: {vertexIndices.Count}");
            sb.AppendLine($"  UV Bounds: ({island.uvBounds.xMin:F3}, {island.uvBounds.yMin:F3}) - ({island.uvBounds.xMax:F3}, {island.uvBounds.yMax:F3})");
            sb.AppendLine($"  3D Center: ({center3D.x:F4}, {center3D.y:F4}, {center3D.z:F4})");
            sb.AppendLine($"  3D Size: ({(max3D - min3D).x:F4}, {(max3D - min3D).y:F4}, {(max3D - min3D).z:F4})");
            sb.AppendLine($"  SubMesh/Material Slot: {submeshIndex} ({matName})");
            sb.AppendLine($"  Average Texture Color: {avgColorStr}");

            return sb.ToString();
        }

        [AgentTool("Highlight a specific mesh island in the SceneView. Sets selection to the GameObject and draws the island wireframe.")]
        public static string SelectMeshIsland(string gameObjectName, int islandIndex)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var islands = UVIslandDetector.DetectIslands(mesh, false);
            if (islandIndex < 0 || islandIndex >= islands.Count)
                return $"Error: Island index {islandIndex} out of range (0-{islands.Count - 1}).";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            // ハイライト状態を設定
            _highlightedRenderer = renderer;
            _highlightedIslandIndex = islandIndex;
            _highlightedIslands = islands;

            // メタデータのロード
            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (!string.IsNullOrEmpty(avatarName))
                _highlightedMetadata = MetadataManager.LoadMetadata(avatarName, go.name);

            Selection.activeGameObject = go;
            SceneView.RepaintAll();

            return $"Success: Highlighting island #{islandIndex} on '{gameObjectName}' ({islands[islandIndex].triangleIndices.Count} triangles).";
        }

        [AgentTool("Reserve a paint color for a mesh island. Does not modify the texture immediately - use ApplyMeshPaint to apply all reservations. Color values are 0.0-1.0 (RGB).")]
        public static string PaintMeshIsland(string gameObjectName, int islandIndex, float r, float g, float b)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var islands = UVIslandDetector.DetectIslands(mesh, false);
            if (islandIndex < 0 || islandIndex >= islands.Count)
                return $"Error: Island index {islandIndex} out of range (0-{islands.Count - 1}).";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root. Ensure the GameObject is under an avatar hierarchy.";

            // メタデータのロード or 作成
            var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
            if (metadata == null)
                metadata = new MeshPaintMetadata();

            // Store per-material original texture GUID
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                int submeshIdx = GetSubmeshIndex(mesh, islands[islandIndex]);
                Material mat = (submeshIdx >= 0 && submeshIdx < renderer.sharedMaterials.Length)
                    ? renderer.sharedMaterials[submeshIdx] : null;

                if (mat != null && mat.mainTexture != null
                    && string.IsNullOrEmpty(metadata.GetMaterialTextureGuid(submeshIdx)))
                {
                    string guid = AssetDatabase.AssetPathToGUID(
                        AssetDatabase.GetAssetPath(mat.mainTexture));
                    metadata.SetMaterialTextureGuid(submeshIdx, guid);
                }
            }

            Color color = new Color(r, g, b, 1f);
            metadata.SetColor(islandIndex, color);
            MetadataManager.SaveMetadata(metadata, avatarName, go.name);

            // ハイライト更新
            if (_highlightedRenderer != null && _highlightedRenderer.gameObject == go)
                _highlightedMetadata = metadata;

            return $"Success: Reserved color ({r:F2}, {g:F2}, {b:F2}) for island #{islandIndex} on '{gameObjectName}'. Use ApplyMeshPaint to apply.";
        }

        [AgentTool("Clear a paint color reservation for a mesh island.")]
        public static string ClearMeshIslandPaint(string gameObjectName, int islandIndex)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
            if (metadata == null)
                return $"Info: No paint reservations found for '{gameObjectName}'.";

            metadata.RemoveSetting(islandIndex);
            MetadataManager.SaveMetadata(metadata, avatarName, go.name);

            if (_highlightedRenderer != null && _highlightedRenderer.gameObject == go)
                _highlightedMetadata = metadata;

            return $"Success: Cleared paint reservation for island #{islandIndex} on '{gameObjectName}'.";
        }

        [AgentTool("Apply all reserved paint colors to the mesh texture. Creates a non-destructive copy of the original texture and material. Original assets are never modified.")]
        public static string ApplyMeshPaint(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
            if (metadata == null || metadata.islandColors.Count == 0)
                return $"Info: No paint reservations found for '{gameObjectName}'. Use PaintMeshIsland first.";

            try
            {
                // Undo グループ開始
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Apply Mesh Paint via Agent");

                Undo.RecordObject(renderer, "Apply Mesh Paint via Agent");

                // アイランド検出
                var islands = UVIslandDetector.DetectIslands(mesh, false);

                // アイランドをサブメッシュ（マテリアルスロット）別にグループ化
                var perMaterial = new Dictionary<int, List<IslandColorSetting>>();
                foreach (var setting in metadata.islandColors)
                {
                    if (setting.islandIndex < 0 || setting.islandIndex >= islands.Count) continue;
                    int submeshIdx = GetSubmeshIndex(mesh, islands[setting.islandIndex]);
                    if (!perMaterial.ContainsKey(submeshIdx))
                        perMaterial[submeshIdx] = new List<IslandColorSetting>();
                    perMaterial[submeshIdx].Add(setting);
                }

                var mats = renderer.sharedMaterials;
                int processedCount = 0;
                var texPaths = new List<string>();

                foreach (var kvp in perMaterial)
                {
                    int submeshIdx = kvp.Key;
                    var settings = kvp.Value;

                    if (submeshIdx < 0 || submeshIdx >= mats.Length || mats[submeshIdx] == null)
                        continue;

                    // Get original texture for this material slot
                    Texture2D sourceTex = null;
                    string matOrigGuid = metadata.GetMaterialTextureGuid(submeshIdx);
                    if (!string.IsNullOrEmpty(matOrigGuid))
                    {
                        string sourcePath = MetadataManager.GetOriginalTexturePath(matOrigGuid);
                        sourceTex = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                    }
                    if (sourceTex == null)
                    {
                        Debug.LogWarning($"[MeshIslandTools] Original texture not found for material slot {submeshIdx}. Skipping.");
                        continue;
                    }

                    // Create material copy if needed
                    Material mat = mats[submeshIdx];
                    if (!mat.name.EndsWith("_Customized"))
                    {
                        Material newMat = new Material(mat);
                        newMat.name = mat.name + "_Customized";
                        string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                        mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        mats[submeshIdx] = mat;
                    }

                    // Paint islands for this material
                    Texture2D editableTex = TextureUtility.CreateEditableTexture(sourceTex);
                    if (editableTex == null) continue;

                    TextureUtility.PaintIslands(editableTex, mesh, islands, settings);

                    string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name);
                    if (string.IsNullOrEmpty(texPath)) continue;

                    Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    Undo.RecordObject(mat, "Apply Mesh Paint via Agent");
                    mat.mainTexture = savedTex;
                    EditorUtility.SetDirty(mat);

                    texPaths.Add(texPath);
                    processedCount += settings.Count;
                }

                // Update renderer with potentially modified materials array
                renderer.sharedMaterials = mats;

                AssetDatabase.SaveAssets();

                // Undo グループ終了 — 1回の Ctrl+Z でまとめて戻る
                Undo.CollapseUndoOperations(undoGroup);

                return $"Success: Applied {processedCount} paint reservation(s) to '{gameObjectName}'. Textures: {string.Join(", ", texPaths)}";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MeshIslandTools] ApplyMeshPaint Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("Enable interactive island selection mode in Scene view for direct color operations. Opens a visual tool where you can click to select islands and apply gradient/HSV adjustments.")]
        public static string EnableIslandSelectionMode(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            IslandSelectionState.Activate(renderer);
            Selection.activeGameObject = go;
            SceneView.RepaintAll();

            int islandCount = IslandSelectionState.Islands != null ? IslandSelectionState.Islands.Count : 0;
            return $"Success: Island selection mode enabled on '{gameObjectName}' ({islandCount} islands). Click islands in Scene view to select, then use the color panel to apply operations.";
        }

        [AgentTool("Get the currently selected island indices. Takes NO arguments — the target GameObject is already known from EnableIslandSelectionMode. " +
                    "Returns indices ready for use with AdjustHSV/ApplyGradientEx/GenerateTextureWithAI islandIndices parameter. " +
                    "Usage: [GetSelectedIslands()]")]
        public static string GetSelectedIslands()
        {
            if (!IslandSelectionState.IsActive)
                return "Error: Island selection mode is not active. Call EnableIslandSelectionMode first.";

            if (IslandSelectionState.SelectedIndices.Count == 0)
                return "Info: No islands selected yet. Ask the user to click on islands in the Scene view.";

            string indices = IslandSelectionState.GetIslandIndicesString();
            string goPath = IslandSelectionState.GetGameObjectPath();
            return $"Selected islands: {indices} (on '{goPath}', {IslandSelectionState.SelectedIndices.Count} island(s) selected)";
        }

        // --- Helper Methods ---

        private static int GetSubmeshIndex(Mesh mesh, UVIsland island)
        {
            if (mesh.subMeshCount <= 1) return 0;
            if (island.triangleIndices.Count == 0) return 0;

            int sampleTri = island.triangleIndices[0];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var subDesc = mesh.GetSubMesh(s);
                int subTriStart = subDesc.indexStart / 3;
                int subTriCount = subDesc.indexCount / 3;
                if (sampleTri >= subTriStart && sampleTri < subTriStart + subTriCount)
                    return s;
            }
            return 0;
        }

        private static string SampleAverageColor(Texture2D tex, Vector2[] uvs, int[] triangles, UVIsland island)
        {
            try
            {
                // テクスチャの読み取り可能チェック
                string path = AssetDatabase.GetAssetPath(tex);
                if (!string.IsNullOrEmpty(path))
                {
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                }

                // アイランドのUV中心付近からサンプリング
                int sampleCount = Mathf.Min(island.triangleIndices.Count, 10);
                Color total = Color.black;
                int validSamples = 0;

                for (int i = 0; i < sampleCount; i++)
                {
                    int triIdx = island.triangleIndices[i * island.triangleIndices.Count / sampleCount];
                    Vector2 uv0 = uvs[triangles[triIdx * 3]];
                    Vector2 uv1 = uvs[triangles[triIdx * 3 + 1]];
                    Vector2 uv2 = uvs[triangles[triIdx * 3 + 2]];
                    Vector2 center = (uv0 + uv1 + uv2) / 3f;

                    int px = Mathf.Clamp(Mathf.FloorToInt(center.x * tex.width), 0, tex.width - 1);
                    int py = Mathf.Clamp(Mathf.FloorToInt(center.y * tex.height), 0, tex.height - 1);

                    total += tex.GetPixel(px, py);
                    validSamples++;
                }

                if (validSamples > 0)
                {
                    Color avg = total / validSamples;
                    return $"RGB({avg.r:F2}, {avg.g:F2}, {avg.b:F2}) #{ColorUtility.ToHtmlStringRGB(avg)}";
                }
            }
            catch (System.Exception)
            {
                // テクスチャが読み取れない場合はN/A
            }

            return "N/A";
        }

    }
}
