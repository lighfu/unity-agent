using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class TextureEditTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // [Deprecated] Use ApplyGradientEx instead — it accepts string color params and supports island/range filtering.
        [AgentTool("(Deprecated: use ApplyGradientEx instead) Apply a gradient to a mesh's texture in 3D space. direction: top_to_bottom, bottom_to_top, left_to_right, right_to_left. blendMode: replace (overwrite), multiply (multiply with existing), tint (preserve lightness detail, recommended). Color values 0.0-1.0.")]
        public static string ApplyGradient(string gameObjectName,
            float fromR, float fromG, float fromB,
            float toR, float toG, float toB,
            string direction = "top_to_bottom",
            string blendMode = "tint")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            Material mat = renderer.sharedMaterial;
            if (mat == null) return $"Error: No material on '{gameObjectName}'.";

            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return $"Error: No main texture found on material '{mat.name}'.";

            // 方向軸の決定
            int axis; // 0=x, 1=y, 2=z
            bool invert;
            switch (direction.ToLower())
            {
                case "top_to_bottom": axis = 1; invert = true; break;
                case "bottom_to_top": axis = 1; invert = false; break;
                case "left_to_right": axis = 0; invert = false; break;
                case "right_to_left": axis = 0; invert = true; break;
                default: return $"Error: Invalid direction '{direction}'. Use: top_to_bottom, bottom_to_top, left_to_right, right_to_left.";
            }

            // blendMode の検証
            if (blendMode != "replace" && blendMode != "multiply" && blendMode != "tint")
                return $"Error: Invalid blendMode '{blendMode}'. Use: replace, multiply, tint.";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                // Undo グループ開始
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Apply Gradient via Agent");

                Undo.RecordObject(renderer, "Apply Gradient via Agent");

                // 元テクスチャの GUID を記録（非破壊のため）
                string originalTexPath = AssetDatabase.GetAssetPath(sourceTex);
                string originalTexGuid = AssetDatabase.AssetPathToGUID(originalTexPath);

                // 既に Customized マテリアルの場合、元テクスチャを辿る
                var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
                if (metadata != null && !string.IsNullOrEmpty(metadata.originalTextureGuid))
                {
                    string origPath = MetadataManager.GetOriginalTexturePath(metadata.originalTextureGuid);
                    var origTex = AssetDatabase.LoadAssetAtPath<Texture2D>(origPath);
                    if (origTex != null)
                    {
                        sourceTex = origTex;
                        originalTexGuid = metadata.originalTextureGuid;
                    }
                }

                // editable コピー作成
                Texture2D editableTex = TextureUtility.CreateEditableTexture(sourceTex);
                if (editableTex == null)
                    return "Error: Failed to create editable texture copy.";

                // Create material copy FIRST (before baking to avoid modifying original shared material)
                if (!mat.name.EndsWith("_Customized"))
                {
                    Material newMat = new Material(mat);
                    newMat.name = mat.name + "_Customized";
                    string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                    mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    renderer.sharedMaterial = mat;
                }

                // Bake _Color into texture if needed (lilToon: finalColor = texture × _Color)
                if (metadata == null) metadata = new MeshPaintMetadata();
                BakeMainColorIfNeeded(editableTex, mat, metadata);

                // メッシュデータ取得
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                int[] triangles = mesh.triangles;

                if (uvs.Length == 0)
                    return "Error: Mesh has no UV coordinates.";

                // 3D バウンド計算（方向軸の min/max）
                float minVal = float.MaxValue, maxVal = float.MinValue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float v = GetAxisValue(vertices[i], axis);
                    minVal = Mathf.Min(minVal, v);
                    maxVal = Mathf.Max(maxVal, v);
                }

                float range = maxVal - minVal;
                if (range < 0.0001f)
                    return "Error: Mesh has zero extent along the gradient direction.";

                Color fromColor = new Color(fromR, fromG, fromB, 1f);
                Color toColor = new Color(toR, toG, toB, 1f);

                int width = editableTex.width;
                int height = editableTex.height;

                // Bulk read all pixels once
                Color[] pixels = editableTex.GetPixels();
                float invRange = 1f / range;

                // 全三角形を走査してグラデーション適用
                for (int tri = 0; tri < triangles.Length / 3; tri++)
                {
                    int i0 = triangles[tri * 3];
                    int i1 = triangles[tri * 3 + 1];
                    int i2 = triangles[tri * 3 + 2];

                    Vector3 v0 = vertices[i0], v1 = vertices[i1], v2 = vertices[i2];
                    Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                    // UV ピクセル座標
                    Vector2 p0 = new Vector2(uv0.x * width, uv0.y * height);
                    Vector2 p1 = new Vector2(uv1.x * width, uv1.y * height);
                    Vector2 p2 = new Vector2(uv2.x * width, uv2.y * height);

                    // バウンディングボックス
                    int minX = Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)));
                    int maxX = Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)));
                    int minY = Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)));
                    int maxY = Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)));

                    minX = Mathf.Clamp(minX, 0, width - 1);
                    maxX = Mathf.Clamp(maxX, 0, width - 1);
                    minY = Mathf.Clamp(minY, 0, height - 1);
                    maxY = Mathf.Clamp(maxY, 0, height - 1);

                    for (int y = minY; y <= maxY; y++)
                    {
                        int rowOffset = y * width;
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector2 pt = new Vector2(x + 0.5f, y + 0.5f);
                            Vector3 bary = ComputeBarycentric(pt, p0, p1, p2);

                            if (bary.x < 0 || bary.y < 0 || bary.z < 0) continue;

                            // ベイリセントリック座標で3D位置を補間
                            Vector3 worldPos = v0 * bary.x + v1 * bary.y + v2 * bary.z;
                            float axisVal = GetAxisValue(worldPos, axis);
                            float t = Mathf.Clamp01((axisVal - minVal) * invRange);
                            if (invert) t = 1f - t;

                            Color gradColor = Color.Lerp(fromColor, toColor, t);
                            int pixelIdx = rowOffset + x;
                            Color original = pixels[pixelIdx];
                            Color final;

                            switch (blendMode)
                            {
                                case "multiply":
                                    final = original * gradColor;
                                    final.a = original.a;
                                    break;
                                case "tint":
                                    float lum = original.grayscale;
                                    final = gradColor * (lum * 0.7f + 0.3f);
                                    final.a = original.a;
                                    break;
                                default: // replace
                                    final = gradColor;
                                    final.a = original.a;
                                    break;
                            }

                            pixels[pixelIdx] = final;
                        }
                    }
                }

                // Bulk write all pixels and apply
                editableTex.SetPixels(pixels);
                editableTex.Apply();

                // テクスチャ保存 (per-object filename to avoid collision)
                string safeName = go.name.Replace("/", "_").Replace("\\", "_");
                string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name + "_" + safeName);
                if (string.IsNullOrEmpty(texPath))
                    return "Error: Failed to save gradient texture.";

                Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                UnityEngine.Object.DestroyImmediate(editableTex);

                // マテリアルの mainTexture 変更を Undo 記録
                Undo.RecordObject(mat, "Apply Gradient via Agent");
                mat.mainTexture = savedTex;
                NeutralizeLilToonShadowColors(mat);

                // メタデータ更新（元テクスチャ参照を保持）
                if (metadata == null) metadata = new MeshPaintMetadata();
                if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    metadata.originalTextureGuid = originalTexGuid;
                MetadataManager.SaveMetadata(metadata, avatarName, go.name);

                EditorUtility.SetDirty(mat);

                // Undo グループ終了 — 1回の Ctrl+Z でまとめて戻る
                // NOTE: SaveAssets() を呼ばない。呼ぶとディスクに変更後状態が書き込まれ、
                // Undo 後のドメインリロードでディスク状態が優先されて Undo が無効になる。
                Undo.CollapseUndoOperations(undoGroup);

                return $"Success: Applied {direction} gradient ({fromR:F1},{fromG:F1},{fromB:F1}) -> ({toR:F1},{toG:F1},{toB:F1}) with {blendMode} blend to '{gameObjectName}'. Texture: {texPath}";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureEditTools] ApplyGradient Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("Apply gradient to mesh texture. Example: [ApplyGradientEx(\"go\", \"#FF0000\", \"#0000FF\")]\ngameObjectName: hierarchy path (e.g. 'Chiffon/Hair'), NOT asset path.\nfromColor/toColor: '#RRGGBB' | '#RRGGBBAA' | 'R,G,B' (0-1) | 'transparent'\nblendMode: screen (lighten/white) | overlay (natural mix) | tint (recolor) | multiply (darken) | replace\n  IMPORTANT: For white/brightening, use 'screen'. 'tint' with white → gray.\ndirection: top_to_bottom | bottom_to_top | left_to_right | right_to_left\nislandIndices: '0;1;3' (empty=all). startT/endT: 0.0-1.0 gradient range.")]
        public static string ApplyGradientEx(
            string gameObjectName,
            string fromColor,
            string toColor,
            string direction = "top_to_bottom",
            string blendMode = "tint",
            string islandIndices = "",
            float startT = 0.0f,
            float endT = 1.0f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Try ScanAvatarMeshes(avatarRoot) to discover correct mesh paths.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            // Parse island indices first to determine correct material slot
            if (!TryParseIslandIndices(islandIndices, out List<int> islandIndexList, out string islandError))
                return islandError;

            // Detect islands once and reuse for material detection + gradient computation
            List<UVIsland> allIslands = UVIslandDetector.DetectIslands(mesh);

            int materialIndex = DetectMaterialIndex(mesh, islandIndexList, allIslands);
            if (materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}) on '{gameObjectName}'.";
            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: No material at slot {materialIndex} on '{gameObjectName}'.";

            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return $"Error: No main texture found on material '{mat.name}' (slot {materialIndex}).";

            // Parse colors
            if (!TryParseColorString(fromColor, out Color from))
                return $"Error: Could not parse fromColor '{fromColor}'. Use '#FF0000' or '1.0,0.0,0.0'.";
            if (!TryParseColorString(toColor, out Color to))
                return $"Error: Could not parse toColor '{toColor}'. Use '#FF0000' or '1.0,0.0,0.0'.";

            // When one end is fully transparent, inherit RGB from the opaque end.
            // This ensures "transparent → blue" means "original → blue" (not "black → blue").
            if (from.a < 0.01f && to.a >= 0.01f)
                from = new Color(to.r, to.g, to.b, 0f);
            else if (to.a < 0.01f && from.a >= 0.01f)
                to = new Color(from.r, from.g, from.b, 0f);

            // Parse direction
            int axis;
            bool invert;
            switch (direction.ToLower())
            {
                case "top_to_bottom": axis = 1; invert = true; break;
                case "bottom_to_top": axis = 1; invert = false; break;
                case "left_to_right": axis = 0; invert = false; break;
                case "right_to_left": axis = 0; invert = true; break;
                default: return $"Error: Invalid direction '{direction}'. Use: top_to_bottom, bottom_to_top, left_to_right, right_to_left.";
            }

            // Validate blendMode
            if (blendMode != "replace" && blendMode != "multiply" && blendMode != "tint" && blendMode != "overlay" && blendMode != "screen")
                return $"Error: Invalid blendMode '{blendMode}'. Use: screen, overlay, tint, multiply, replace.";

            // Validate startT/endT
            if (startT < 0f || startT > 1f) return $"Error: startT must be 0.0-1.0, got {startT}.";
            if (endT < 0f || endT > 1f) return $"Error: endT must be 0.0-1.0, got {endT}.";
            if (startT >= endT) return $"Error: startT ({startT}) must be less than endT ({endT}).";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("ApplyGradientEx via Agent");

                Undo.RecordObject(renderer, "ApplyGradientEx via Agent");

                // Track original texture for non-destructive editing
                string originalTexPath = AssetDatabase.GetAssetPath(sourceTex);
                string originalTexGuid = AssetDatabase.AssetPathToGUID(originalTexPath);

                var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
                // Treat as partial edit when targeting specific islands OR using restricted range
                // (range-restricted gradients should preserve current texture outside the range)
                bool isPartialEdit = islandIndexList.Count > 0 || startT > 0.001f || endT < 0.999f;

                if (isPartialEdit)
                {
                    // Partial edit: use CURRENT texture as base to preserve other areas
                    // Store original GUID in metadata if not yet saved
                    if (metadata == null) metadata = new MeshPaintMetadata();
                    if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    {
                        metadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(originalTexPath);
                    }
                    originalTexGuid = metadata.originalTextureGuid;
                    // sourceTex stays as the current material texture (not reverted to original)
                }
                else
                {
                    // Full edit: start from original texture (non-destructive)
                    if (metadata != null && !string.IsNullOrEmpty(metadata.originalTextureGuid))
                    {
                        string origPath = MetadataManager.GetOriginalTexturePath(metadata.originalTextureGuid);
                        var origTex = AssetDatabase.LoadAssetAtPath<Texture2D>(origPath);
                        if (origTex != null)
                        {
                            sourceTex = origTex;
                            originalTexGuid = metadata.originalTextureGuid;
                        }
                    }
                }

                Texture2D editableTex = TextureUtility.CreateEditableTexture(sourceTex);
                if (editableTex == null)
                    return "Error: Failed to create editable texture copy.";

                // Create material copy FIRST (before baking to avoid modifying original shared material)
                if (!mat.name.EndsWith("_Customized"))
                {
                    Material newMat = new Material(mat);
                    newMat.name = mat.name + "_Customized";
                    string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                    mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    SetMaterialAtIndex(renderer, materialIndex, mat);
                }

                // Bake _Color into texture if needed (lilToon: finalColor = texture × _Color)
                // Skip baking for partial edits — the current texture already has it baked
                if (metadata == null) metadata = new MeshPaintMetadata();
                if (!isPartialEdit)
                    BakeMainColorIfNeeded(editableTex, mat, metadata);

                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                int[] triangles = mesh.triangles;

                if (uvs.Length == 0)
                    return "Error: Mesh has no UV coordinates.";

                int width = editableTex.width;
                int height = editableTex.height;

                // Determine target islands (allIslands already computed above)
                List<int> targetIslandIndices;
                if (islandIndexList.Count > 0)
                {
                    targetIslandIndices = new List<int>();
                    foreach (int idx in islandIndexList)
                    {
                        if (idx < 0 || idx >= allIslands.Count)
                            return $"Error: Island index {idx} out of range. Mesh has {allIslands.Count} islands (0-{allIslands.Count - 1}).";
                        targetIslandIndices.Add(idx);
                    }
                }
                else
                {
                    targetIslandIndices = Enumerable.Range(0, allIslands.Count).ToList();
                }

                // Group 3D-connected islands so they share gradient bounds
                int[] islandGroups = UVIslandDetector.BuildIslandGroups(mesh, allIslands);

                // Compute 3D bounds per group (only for target islands)
                var groupBounds = new Dictionary<int, Vector2>(); // groupId → (min, max)
                foreach (int islandIdx in targetIslandIndices)
                {
                    int groupId = islandGroups[islandIdx];
                    var island = allIslands[islandIdx];

                    float gMin = groupBounds.ContainsKey(groupId) ? groupBounds[groupId].x : float.MaxValue;
                    float gMax = groupBounds.ContainsKey(groupId) ? groupBounds[groupId].y : float.MinValue;

                    foreach (int triIdx in island.triangleIndices)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            int vIdx = triangles[triIdx * 3 + j];
                            float v = GetAxisValue(vertices[vIdx], axis);
                            gMin = Mathf.Min(gMin, v);
                            gMax = Mathf.Max(gMax, v);
                        }
                    }
                    groupBounds[groupId] = new Vector2(gMin, gMax);
                }

                // Bulk read all pixels once (avoids per-pixel GetPixel calls)
                Color[] pixels = editableTex.GetPixels();

                // Apply gradient per-island using shared group bounds
                foreach (int islandIdx in targetIslandIndices)
                {
                    var island = allIslands[islandIdx];
                    int groupId = islandGroups[islandIdx];
                    float minVal = groupBounds[groupId].x;
                    float maxVal = groupBounds[groupId].y;

                    float range = maxVal - minVal;
                    if (range < 0.0001f) continue; // skip flat islands

                    float invRange = 1f / range;
                    float invEndMinusStart = (endT - startT) > 0.0001f ? 1f / (endT - startT) : 0f;

                    foreach (int triIdx in island.triangleIndices)
                    {
                        int i0 = triangles[triIdx * 3];
                        int i1 = triangles[triIdx * 3 + 1];
                        int i2 = triangles[triIdx * 3 + 2];

                        Vector3 v0 = vertices[i0], v1 = vertices[i1], v2 = vertices[i2];
                        Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                        Vector2 p0 = new Vector2(uv0.x * width, uv0.y * height);
                        Vector2 p1 = new Vector2(uv1.x * width, uv1.y * height);
                        Vector2 p2 = new Vector2(uv2.x * width, uv2.y * height);

                        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, width - 1);
                        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, width - 1);
                        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, height - 1);
                        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, height - 1);

                        for (int y = minY; y <= maxY; y++)
                        {
                            int rowOffset = y * width;
                            for (int x = minX; x <= maxX; x++)
                            {
                                Vector2 pt = new Vector2(x + 0.5f, y + 0.5f);
                                Vector3 bary = ComputeBarycentric(pt, p0, p1, p2);

                                if (bary.x < 0 || bary.y < 0 || bary.z < 0) continue;

                                Vector3 worldPos = v0 * bary.x + v1 * bary.y + v2 * bary.z;
                                float axisVal = GetAxisValue(worldPos, axis);
                                float t = Mathf.Clamp01((axisVal - minVal) * invRange);
                                if (invert) t = 1f - t;

                                // Range clipping
                                if (t < startT || t > endT) continue;
                                float remappedT = (t - startT) * invEndMinusStart;

                                Color gradColor = Color.Lerp(from, to, remappedT);
                                float strength = gradColor.a; // alpha = blend strength
                                int pixelIdx = rowOffset + x;
                                Color original = pixels[pixelIdx];

                                Color blended;
                                switch (blendMode)
                                {
                                    case "multiply":
                                        blended = original * gradColor;
                                        break;
                                    case "tint":
                                        float lum = original.grayscale;
                                        blended = new Color(gradColor.r, gradColor.g, gradColor.b) * (lum * 0.7f + 0.3f);
                                        break;
                                    case "overlay":
                                        blended = new Color(
                                            original.r < 0.5f ? 2f * original.r * gradColor.r : 1f - 2f * (1f - original.r) * (1f - gradColor.r),
                                            original.g < 0.5f ? 2f * original.g * gradColor.g : 1f - 2f * (1f - original.g) * (1f - gradColor.g),
                                            original.b < 0.5f ? 2f * original.b * gradColor.b : 1f - 2f * (1f - original.b) * (1f - gradColor.b));
                                        break;
                                    case "screen":
                                        blended = new Color(
                                            1f - (1f - original.r) * (1f - gradColor.r),
                                            1f - (1f - original.g) * (1f - gradColor.g),
                                            1f - (1f - original.b) * (1f - gradColor.b));
                                        break;
                                    default: // replace
                                        blended = gradColor;
                                        break;
                                }

                                // Alpha-based blend: alpha=0 → original (no change), alpha=1 → full blend
                                Color final = Color.Lerp(original, blended, strength);
                                final.a = original.a;

                                pixels[pixelIdx] = final;
                            }
                        }
                    }
                }

                // Bulk write all pixels and apply
                editableTex.SetPixels(pixels);
                editableTex.Apply();

                // Save texture (per-object filename to avoid collision)
                string safeName = go.name.Replace("/", "_").Replace("\\", "_");
                string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name + "_" + safeName);
                if (string.IsNullOrEmpty(texPath))
                    return "Error: Failed to save gradient texture.";

                Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                UnityEngine.Object.DestroyImmediate(editableTex);

                Undo.RecordObject(mat, "ApplyGradientEx via Agent");
                mat.mainTexture = savedTex;
                NeutralizeLilToonShadowColors(mat);

                if (metadata == null) metadata = new MeshPaintMetadata();
                if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    metadata.originalTextureGuid = originalTexGuid;
                MetadataManager.SaveMetadata(metadata, avatarName, go.name);

                EditorUtility.SetDirty(mat);
                Undo.CollapseUndoOperations(undoGroup);

                string islandInfo = islandIndexList.Count > 0 ? $" islands=[{string.Join(";", islandIndexList)}]" : "";
                string rangeInfo = (startT > 0f || endT < 1f) ? $" range=[{startT:F1}-{endT:F1}]" : "";
                return $"Success: Applied {direction} gradient ({fromColor}) -> ({toColor}) with {blendMode} blend to '{gameObjectName}'.{islandInfo}{rangeInfo} Texture: {texPath}\n[NEXT] Call CaptureSceneView() to visually verify the result, then ask the user for confirmation.";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureEditTools] ApplyGradientEx Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("Adjust HSV of mesh texture. For brightness/saturation only. To SET a specific color, use ApplyGradientEx.\ngameObjectName: hierarchy path, NOT asset path.\nhueShift: RELATIVE degrees -180..+180 (rotates existing hue, NOT absolute)\nsaturationScale: 0=grayscale, 1=unchanged, 2=vivid\nvalueScale: 0=black, 1=unchanged, 1.5=brighter\nislandIndices: '0;1;3' (empty=all)\nRecipes: Brighten=[AdjustHSV(\"go\",0,1,1.5)] Darken=[AdjustHSV(\"go\",0,1,0.5)] Desaturate=[AdjustHSV(\"go\",0,0,1)]\nWARNING: Do NOT use hueShift to 'set' a color. Use ApplyGradientEx with tint/overlay.")]
        public static string AdjustHSV(
            string gameObjectName,
            float hueShift = 0f,
            float saturationScale = 1f,
            float valueScale = 1f,
            string islandIndices = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Try ScanAvatarMeshes(avatarRoot) to discover correct mesh paths.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            // Parse island indices first to determine correct material slot
            if (!TryParseIslandIndices(islandIndices, out List<int> islandIndexList, out string islandError))
                return islandError;

            // Detect islands once and reuse for material detection, filtering, and validation
            List<UVIsland> islands = islandIndexList.Count > 0 ? UVIslandDetector.DetectIslands(mesh) : null;

            // Validate island indices early
            if (islands != null)
            {
                foreach (int idx in islandIndexList)
                {
                    if (idx >= islands.Count)
                        return $"Error: Island index {idx} out of range. Mesh has {islands.Count} islands (0-{islands.Count - 1}).";
                }
            }

            int materialIndex = DetectMaterialIndex(mesh, islandIndexList, islands);
            if (materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}) on '{gameObjectName}'.";
            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: No material at slot {materialIndex} on '{gameObjectName}'.";

            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return $"Error: No main texture found on material '{mat.name}' (slot {materialIndex}).";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("AdjustHSV via Agent");

                Undo.RecordObject(renderer, "AdjustHSV via Agent");

                // Track original texture
                string originalTexPath = AssetDatabase.GetAssetPath(sourceTex);
                string originalTexGuid = AssetDatabase.AssetPathToGUID(originalTexPath);

                var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
                bool isPartialEdit = islandIndexList.Count > 0;

                if (isPartialEdit)
                {
                    // Per-island edit: use CURRENT texture as base to preserve other areas
                    if (metadata == null) metadata = new MeshPaintMetadata();
                    if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                        metadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(originalTexPath);
                    originalTexGuid = metadata.originalTextureGuid;
                    // sourceTex stays as the current material texture (not reverted to original)
                }
                else
                {
                    // Full edit: start from original texture (non-destructive)
                    if (metadata != null && !string.IsNullOrEmpty(metadata.originalTextureGuid))
                    {
                        string origPath = MetadataManager.GetOriginalTexturePath(metadata.originalTextureGuid);
                        var origTex = AssetDatabase.LoadAssetAtPath<Texture2D>(origPath);
                        if (origTex != null)
                        {
                            sourceTex = origTex;
                            originalTexGuid = metadata.originalTextureGuid;
                        }
                    }
                }

                Texture2D editableTex = TextureUtility.CreateEditableTexture(sourceTex);
                if (editableTex == null)
                    return "Error: Failed to create editable texture copy.";

                // Create material copy FIRST (before baking to avoid modifying original shared material)
                if (!mat.name.EndsWith("_Customized"))
                {
                    Material newMat = new Material(mat);
                    newMat.name = mat.name + "_Customized";
                    string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                    mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    SetMaterialAtIndex(renderer, materialIndex, mat);
                }

                // Bake _Color into texture if needed (lilToon: finalColor = texture × _Color)
                // Skip baking for partial edits — the current texture already has it baked
                if (metadata == null) metadata = new MeshPaintMetadata();
                if (!isPartialEdit)
                    BakeMainColorIfNeeded(editableTex, mat, metadata);

                int width = editableTex.width;
                int height = editableTex.height;
                float hueShiftNormalized = hueShift / 360f;

                // Build triangle filter (reuse cached islands)
                HashSet<int> triangleFilter = BuildTriangleFilter(islandIndexList, islands);

                // Bulk pixel processing for both paths
                Color[] pixels = editableTex.GetPixels();

                if (triangleFilter == null)
                {
                    // Fast path: no island filter, process all pixels
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color c = pixels[i];
                        Color.RGBToHSV(c, out float h, out float s, out float v);

                        h = (h + hueShiftNormalized) % 1f;
                        if (h < 0f) h += 1f;
                        s = Mathf.Clamp01(s * saturationScale);
                        v = Mathf.Clamp01(v * valueScale);

                        Color adjusted = Color.HSVToRGB(h, s, v);
                        adjusted.a = c.a;
                        pixels[i] = adjusted;
                    }
                }
                else
                {
                    // Island-filtered path: build flat pixel mask then adjust only masked pixels
                    bool[] mask = BuildPixelMask(width, height, mesh, triangleFilter);

                    for (int i = 0; i < pixels.Length; i++)
                    {
                        if (!mask[i]) continue;

                        Color c = pixels[i];
                        Color.RGBToHSV(c, out float h, out float s, out float v);

                        h = (h + hueShiftNormalized) % 1f;
                        if (h < 0f) h += 1f;
                        s = Mathf.Clamp01(s * saturationScale);
                        v = Mathf.Clamp01(v * valueScale);

                        Color adjusted = Color.HSVToRGB(h, s, v);
                        adjusted.a = c.a;
                        pixels[i] = adjusted;
                    }
                }

                editableTex.SetPixels(pixels);

                editableTex.Apply();

                // Save texture (per-object filename to avoid collision)
                string safeName = go.name.Replace("/", "_").Replace("\\", "_");
                string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name + "_" + safeName);
                if (string.IsNullOrEmpty(texPath))
                    return "Error: Failed to save adjusted texture.";

                Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                UnityEngine.Object.DestroyImmediate(editableTex);

                Undo.RecordObject(mat, "AdjustHSV via Agent");
                mat.mainTexture = savedTex;
                NeutralizeLilToonShadowColors(mat);

                if (metadata == null) metadata = new MeshPaintMetadata();
                if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    metadata.originalTextureGuid = originalTexGuid;
                MetadataManager.SaveMetadata(metadata, avatarName, go.name);

                EditorUtility.SetDirty(mat);
                Undo.CollapseUndoOperations(undoGroup);

                string islandInfo = islandIndexList.Count > 0 ? $" islands=[{string.Join(";", islandIndexList)}]" : "";
                return $"Success: Adjusted HSV (hue={hueShift:+0;-0;0}°, sat×{saturationScale:F1}, val×{valueScale:F1}) on '{gameObjectName}'.{islandInfo} Texture: {texPath}\n[NEXT] Call CaptureSceneView() to visually verify the result, then ask the user for confirmation.";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureEditTools] AdjustHSV Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("Adjust brightness/contrast. Example: [AdjustBrightnessContrast(\"go\", 0.3, 0.1)]\ngameObjectName: hierarchy path, NOT asset path.\nbrightness: -1.0 (black) to +1.0 (white), 0=no change.\ncontrast: -1.0 (flat gray) to +1.0 (high contrast), 0=no change.\nislandIndices: '0;1;3' (empty=all).")]
        public static string AdjustBrightnessContrast(
            string gameObjectName,
            float brightness = 0f,
            float contrast = 0f,
            string islandIndices = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Try ScanAvatarMeshes(avatarRoot) to discover correct mesh paths.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            if (!TryParseIslandIndices(islandIndices, out List<int> islandIndexList, out string islandError))
                return islandError;

            // Detect islands once and reuse
            List<UVIsland> islands = islandIndexList.Count > 0 ? UVIslandDetector.DetectIslands(mesh) : null;

            if (islands != null)
            {
                foreach (int idx in islandIndexList)
                {
                    if (idx >= islands.Count)
                        return $"Error: Island index {idx} out of range. Mesh has {islands.Count} islands.";
                }
            }

            int materialIndex = DetectMaterialIndex(mesh, islandIndexList, islands);
            if (materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}) on '{gameObjectName}'.";
            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: No material at slot {materialIndex} on '{gameObjectName}'.";

            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return $"Error: No main texture found on material '{mat.name}'.";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("AdjustBrightnessContrast via Agent");
                Undo.RecordObject(renderer, "AdjustBrightnessContrast via Agent");

                string originalTexPath = AssetDatabase.GetAssetPath(sourceTex);
                string originalTexGuid = AssetDatabase.AssetPathToGUID(originalTexPath);

                var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
                bool isPartialEdit = islandIndexList.Count > 0;

                if (isPartialEdit)
                {
                    // Per-island edit: use CURRENT texture as base to preserve other areas
                    if (metadata == null) metadata = new MeshPaintMetadata();
                    if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                        metadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(originalTexPath);
                    originalTexGuid = metadata.originalTextureGuid;
                    // sourceTex stays as the current material texture (not reverted to original)
                }
                else
                {
                    // Full edit: start from original texture (non-destructive)
                    if (metadata != null && !string.IsNullOrEmpty(metadata.originalTextureGuid))
                    {
                        string origPath = MetadataManager.GetOriginalTexturePath(metadata.originalTextureGuid);
                        var origTex = AssetDatabase.LoadAssetAtPath<Texture2D>(origPath);
                        if (origTex != null)
                        {
                            sourceTex = origTex;
                            originalTexGuid = metadata.originalTextureGuid;
                        }
                    }
                }

                Texture2D editableTex = TextureUtility.CreateEditableTexture(sourceTex);
                if (editableTex == null) return "Error: Failed to create editable texture copy.";

                if (!mat.name.EndsWith("_Customized"))
                {
                    Material newMat = new Material(mat);
                    newMat.name = mat.name + "_Customized";
                    string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                    mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    SetMaterialAtIndex(renderer, materialIndex, mat);
                }

                // Bake _Color into texture if needed (lilToon: finalColor = texture × _Color)
                // Skip baking for partial edits — the current texture already has it baked
                if (metadata == null) metadata = new MeshPaintMetadata();
                if (!isPartialEdit)
                    BakeMainColorIfNeeded(editableTex, mat, metadata);

                int width = editableTex.width;
                int height = editableTex.height;

                // Contrast formula: output = (input - 0.5) * contrastFactor + 0.5 + brightness
                // contrastFactor = (1 + contrast) for contrast in [-1, 1] mapped to reasonable range
                float contrastFactor = 1f + contrast; // 0..2
                if (contrast > 0f)
                    contrastFactor = 1f + contrast * 2f; // boost: up to 3x
                else
                    contrastFactor = Mathf.Max(0f, 1f + contrast); // reduce: down to 0

                HashSet<int> triangleFilter = BuildTriangleFilter(islandIndexList, islands);

                // Bulk pixel processing for both paths
                Color[] pixels = editableTex.GetPixels();

                if (triangleFilter == null)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = ApplyBC(pixels[i], brightness, contrastFactor);
                    }
                }
                else
                {
                    bool[] mask = BuildPixelMask(width, height, mesh, triangleFilter);

                    for (int i = 0; i < pixels.Length; i++)
                    {
                        if (!mask[i]) continue;
                        pixels[i] = ApplyBC(pixels[i], brightness, contrastFactor);
                    }
                }

                editableTex.SetPixels(pixels);

                editableTex.Apply();

                string safeName = go.name.Replace("/", "_").Replace("\\", "_");
                string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name + "_" + safeName);
                if (string.IsNullOrEmpty(texPath)) return "Error: Failed to save adjusted texture.";

                Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                UnityEngine.Object.DestroyImmediate(editableTex);
                Undo.RecordObject(mat, "AdjustBrightnessContrast via Agent");
                mat.mainTexture = savedTex;
                NeutralizeLilToonShadowColors(mat);

                if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    metadata.originalTextureGuid = originalTexGuid;
                MetadataManager.SaveMetadata(metadata, avatarName, go.name);

                EditorUtility.SetDirty(mat);
                Undo.CollapseUndoOperations(undoGroup);

                string islandInfo = islandIndexList.Count > 0 ? $" islands=[{string.Join(";", islandIndexList)}]" : "";
                return $"Success: Adjusted brightness={brightness:+0.0;-0.0;0}, contrast={contrast:+0.0;-0.0;0} on '{gameObjectName}'.{islandInfo} Texture: {texPath}\n[NEXT] Call CaptureSceneView() to visually verify the result, then ask the user for confirmation.";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureEditTools] AdjustBrightnessContrast Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        private static Color ApplyBC(Color c, float brightness, float contrastFactor)
        {
            float r = (c.r - 0.5f) * contrastFactor + 0.5f + brightness;
            float g = (c.g - 0.5f) * contrastFactor + 0.5f + brightness;
            float b = (c.b - 0.5f) * contrastFactor + 0.5f + brightness;
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), c.a);
        }

        [AgentTool("Set a material property on a GameObject's material. Creates a non-destructive copy. Supported value types: float (e.g. '0.5'), color (e.g. '1,0,0,1' for RGBA), int (e.g. '1'). materialIndex selects which material slot to modify.")]
        public static string SetMaterialProperty(string gameObjectName, string propertyName, string value, int materialIndex = 0)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            if (materialIndex < 0 || materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}).";

            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: Material at index {materialIndex} is null.";

            if (!mat.HasProperty(propertyName))
                return $"Error: Material '{mat.name}' does not have property '{propertyName}'.";

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                // Undo グループ開始
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Set Material Property via Agent");

                Undo.RecordObject(renderer, "Set Material Property via Agent");

                // 非破壊: コピーを作成
                if (!mat.name.EndsWith("_Customized"))
                {
                    Material newMat = new Material(mat);
                    newMat.name = mat.name + "_Customized";
                    string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                    newMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                    var mats = renderer.sharedMaterials;
                    mats[materialIndex] = newMat;
                    renderer.sharedMaterials = mats;
                    mat = newMat;
                }

                // マテリアルのプロパティ変更を Undo 記録
                Undo.RecordObject(mat, "Set Material Property via Agent");

                // 値のパース＆設定
                var propType = ShaderUtil.GetPropertyType(mat.shader, FindPropertyIndex(mat.shader, propertyName));

                switch (propType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (TryParseColor(value, out Color color))
                        {
                            mat.SetColor(propertyName, color);
                        }
                        else return $"Error: Could not parse '{value}' as color. Use 'R,G,B' or 'R,G,B,A' (0-1 range).";
                        break;

                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (float.TryParse(value, out float floatVal))
                        {
                            mat.SetFloat(propertyName, floatVal);
                        }
                        else return $"Error: Could not parse '{value}' as float.";
                        break;

                    case ShaderUtil.ShaderPropertyType.Int:
                        if (int.TryParse(value, out int intVal))
                        {
                            mat.SetInt(propertyName, intVal);
                        }
                        else return $"Error: Could not parse '{value}' as int.";
                        break;

                    default:
                        return $"Error: Property type '{propType}' is not supported for direct value setting.";
                }

                EditorUtility.SetDirty(mat);
                Undo.CollapseUndoOperations(undoGroup);

                return $"Success: Set '{propertyName}' = '{value}' on material '{mat.name}' (slot {materialIndex}) of '{gameObjectName}'.";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureEditTools] SetMaterialProperty Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("List all properties of a material on a GameObject. Shows property name, type, and current value. Useful for discovering which properties to modify.")]
        public static string ListMaterialProperties(string gameObjectName, int materialIndex = 0)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            if (materialIndex < 0 || materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}).";

            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: Material at index {materialIndex} is null.";

            Shader shader = mat.shader;
            int propCount = ShaderUtil.GetPropertyCount(shader);

            var sb = new StringBuilder();
            sb.AppendLine($"Material: {mat.name} (Shader: {shader.name})");
            sb.AppendLine($"Properties ({propCount}):");
            sb.AppendLine("---");

            for (int i = 0; i < propCount; i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                string desc = ShaderUtil.GetPropertyDescription(shader, i);
                var type = ShaderUtil.GetPropertyType(shader, i);

                string valueStr;
                switch (type)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        Color c = mat.GetColor(name);
                        valueStr = $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                        valueStr = mat.GetFloat(name).ToString("F3");
                        break;
                    case ShaderUtil.ShaderPropertyType.Range:
                        float rangeMin = ShaderUtil.GetRangeLimits(shader, i, 1);
                        float rangeMax = ShaderUtil.GetRangeLimits(shader, i, 2);
                        valueStr = $"{mat.GetFloat(name):F3} (range: {rangeMin}-{rangeMax})";
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        var tex = mat.GetTexture(name);
                        valueStr = tex != null ? tex.name : "None";
                        break;
                    case ShaderUtil.ShaderPropertyType.Int:
                        valueStr = mat.GetInt(name).ToString();
                        break;
                    default:
                        valueStr = "?";
                        break;
                }

                string descStr = string.IsNullOrEmpty(desc) ? "" : $" \"{desc}\"";
                sb.AppendLine($"  [{type}] {name}{descStr} = {valueStr}");
            }

            return sb.ToString();
        }

        // --- Helper Methods ---

        /// <summary>
        /// Neutralize lilToon shadow colors after gradient/color changes to prevent color bleeding.
        /// Removes saturation from shadow colors, converting them to grayscale while preserving brightness.
        /// </summary>
        private static void NeutralizeLilToonShadowColors(Material mat)
        {
            if (!mat.HasProperty("_ShadowColor")) return;

            string[] shadowProps = { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor" };
            foreach (var prop in shadowProps)
            {
                if (!mat.HasProperty(prop)) continue;
                Color c = mat.GetColor(prop);
                float gray = c.grayscale;
                mat.SetColor(prop, new Color(gray, gray, gray, c.a));
            }
        }

        /// <summary>
        /// Bake material _Color into texture pixels and reset _Color to white.
        /// lilToon renders finalColor = texture × _Color, so _Color is pre-multiplied
        /// into the texture to ensure texture edits reflect the actual displayed color.
        /// </summary>
        private static void BakeMainColorIfNeeded(Texture2D editableTex, Material mat, MeshPaintMetadata metadata)
        {
            if (!mat.HasProperty("_Color")) return;

            // Determine the original _Color to bake
            Color mainColor;
            if (metadata.originalMainColor != null && metadata.originalMainColor.Length == 4)
            {
                // Already stored from a previous edit — use the saved original
                mainColor = new Color(
                    metadata.originalMainColor[0],
                    metadata.originalMainColor[1],
                    metadata.originalMainColor[2],
                    metadata.originalMainColor[3]);
            }
            else
            {
                // First time: read current _Color from the material
                mainColor = mat.GetColor("_Color");
            }

            // If close to white, no baking needed
            if (mainColor.r > 0.95f && mainColor.g > 0.95f && mainColor.b > 0.95f)
                return;

            // Store original _Color in metadata for future edits
            if (metadata.originalMainColor == null || metadata.originalMainColor.Length != 4)
            {
                metadata.originalMainColor = new float[] { mainColor.r, mainColor.g, mainColor.b, mainColor.a };
            }

            Color bakeColor = mainColor;

            // Pre-multiply _Color into every pixel
            Color[] pixels = editableTex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    pixels[i].r * bakeColor.r,
                    pixels[i].g * bakeColor.g,
                    pixels[i].b * bakeColor.b,
                    pixels[i].a);
            }
            editableTex.SetPixels(pixels);
            editableTex.Apply();

            // Reset _Color to white so the texture colors are displayed as-is
            Undo.RecordObject(mat, "Bake _Color into texture");
            mat.SetColor("_Color", Color.white);
        }

        private static float GetAxisValue(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return v.x;
                case 1: return v.y;
                case 2: return v.z;
                default: return v.y;
            }
        }

        private static Vector3 ComputeBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;

            if (Mathf.Abs(denom) < 1e-8f)
                return new Vector3(-1, -1, -1); // degenerate

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;

            return new Vector3(u, v, w);
        }

        private static int FindPropertyIndex(Shader shader, string propertyName)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyName(shader, i) == propertyName)
                    return i;
            }
            return -1;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = Color.white;
            string[] parts = value.Split(',');

            if (parts.Length == 3)
            {
                if (float.TryParse(parts[0].Trim(), out float r) &&
                    float.TryParse(parts[1].Trim(), out float g) &&
                    float.TryParse(parts[2].Trim(), out float b))
                {
                    color = new Color(r, g, b, 1f);
                    return true;
                }
            }
            else if (parts.Length == 4)
            {
                if (float.TryParse(parts[0].Trim(), out float r) &&
                    float.TryParse(parts[1].Trim(), out float g) &&
                    float.TryParse(parts[2].Trim(), out float b) &&
                    float.TryParse(parts[3].Trim(), out float a))
                {
                    color = new Color(r, g, b, a);
                    return true;
                }
            }

            // Try hex format
            if (ColorUtility.TryParseHtmlString(value.Trim(), out color))
                return true;

            return false;
        }

        private static bool TryParseColorString(string colorStr, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(colorStr)) return false;

            string trimmed = colorStr.Trim();

            // "transparent" keyword → fully transparent (alpha=0, used as "no effect" end of gradient)
            if (trimmed.ToLower() == "transparent")
            {
                color = new Color(0f, 0f, 0f, 0f);
                return true;
            }

            // #RRGGBB / #RGB hex format
            if (trimmed.StartsWith("#"))
            {
                return ColorUtility.TryParseHtmlString(trimmed, out color);
            }

            // RRGGBB / RRGGBBAA without # prefix (common AI omission)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[0-9a-fA-F]{3,8}$"))
            {
                return ColorUtility.TryParseHtmlString("#" + trimmed, out color);
            }

            // R,G,B or R,G,B,A float format — delegate to existing TryParseColor
            return TryParseColor(trimmed, out color);
        }

        private static bool TryParseIslandIndices(string islandIndices, out List<int> indices, out string error)
        {
            indices = new List<int>();
            error = null;

            if (string.IsNullOrEmpty(islandIndices) || string.IsNullOrWhiteSpace(islandIndices))
                return true; // empty = all triangles

            string[] parts = islandIndices.Split(';', ',');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (int.TryParse(trimmed, out int idx))
                {
                    if (idx < 0)
                    {
                        error = $"Error: Negative island index '{idx}'. Indices must be >= 0.";
                        return false;
                    }
                    indices.Add(idx);
                }
                else
                {
                    error = $"Error: Invalid island index '{trimmed}'. Use semicolon-separated integers like '0;1;3'.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Detect which material slot (submesh) the target islands belong to.
        /// Returns 0 for single-material meshes or when no islands are specified.
        /// </summary>
        internal static int DetectMaterialIndex(Mesh mesh, List<int> islandIndexList)
        {
            return DetectMaterialIndex(mesh, islandIndexList, null);
        }

        internal static int DetectMaterialIndex(Mesh mesh, List<int> islandIndexList, List<UVIsland> islands)
        {
            if (islandIndexList == null || islandIndexList.Count == 0 || mesh.subMeshCount <= 1)
                return 0;

            if (islands == null) islands = UVIslandDetector.DetectIslands(mesh);
            var targetTris = new HashSet<int>();
            foreach (int idx in islandIndexList)
            {
                if (idx >= 0 && idx < islands.Count)
                    foreach (int tri in islands[idx].triangleIndices)
                        targetTris.Add(tri);
            }

            int bestSubmesh = 0;
            int bestCount = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var desc = mesh.GetSubMesh(sm);
                int triStart = desc.indexStart / 3;
                int triEnd = triStart + desc.indexCount / 3;
                int count = 0;
                foreach (int tri in targetTris)
                    if (tri >= triStart && tri < triEnd) count++;
                if (count > bestCount) { bestCount = count; bestSubmesh = sm; }
            }
            return bestSubmesh;
        }

        /// <summary>
        /// Assign material to specific slot on renderer.
        /// </summary>
        internal static void SetMaterialAtIndex(Renderer renderer, int index, Material mat)
        {
            var mats = renderer.sharedMaterials;
            mats[index] = mat;
            renderer.sharedMaterials = mats;
        }

        private static HashSet<int> BuildTriangleFilter(Mesh mesh, List<int> islandIndexList)
        {
            return BuildTriangleFilter(islandIndexList, null, mesh);
        }

        private static HashSet<int> BuildTriangleFilter(List<int> islandIndexList, List<UVIsland> islands, Mesh meshFallback = null)
        {
            if (islandIndexList == null || islandIndexList.Count == 0)
                return null; // no filter

            if (islands == null) islands = UVIslandDetector.DetectIslands(meshFallback);
            var filter = new HashSet<int>();

            foreach (int islandIdx in islandIndexList)
            {
                if (islandIdx < 0 || islandIdx >= islands.Count)
                    continue; // skip invalid — caller can validate separately
                foreach (int triIdx in islands[islandIdx].triangleIndices)
                {
                    filter.Add(triIdx);
                }
            }

            return filter.Count > 0 ? filter : null;
        }

        /// <summary>Build flat pixel mask (row-major: index = y * width + x) for bulk GetPixels/SetPixels.</summary>
        private static bool[] BuildPixelMask(int width, int height, Mesh mesh, HashSet<int> triangleFilter)
        {
            bool[] mask = new bool[width * height];

            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            for (int tri = 0; tri < triangles.Length / 3; tri++)
            {
                if (triangleFilter != null && !triangleFilter.Contains(tri)) continue;

                int i0 = triangles[tri * 3];
                int i1 = triangles[tri * 3 + 1];
                int i2 = triangles[tri * 3 + 2];

                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
                Vector2 p0 = new Vector2(uv0.x * width, uv0.y * height);
                Vector2 p1 = new Vector2(uv1.x * width, uv1.y * height);
                Vector2 p2 = new Vector2(uv2.x * width, uv2.y * height);

                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, width - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, width - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, height - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, height - 1);

                for (int y = minY; y <= maxY; y++)
                {
                    int rowOffset = y * width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (mask[rowOffset + x]) continue;
                        Vector2 pt = new Vector2(x + 0.5f, y + 0.5f);
                        Vector3 bary = ComputeBarycentric(pt, p0, p1, p2);
                        if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                            mask[rowOffset + x] = true;
                    }
                }
            }

            return mask;
        }

    }
}
