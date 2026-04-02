using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AI-powered texture generation tools. Combines mesh-aware extraction/application
    /// with external AI image generation (Gemini) for intelligent texture editing.
    /// </summary>
    public static class TextureGenerationTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("TextureGen");
        private const int DilationPadding = 8;
        private const int ExtractionPadding = 16;

        // ─── Material / Texture resolution helper ───

        /// <summary>
        /// Resolve Material and Texture2D from renderer, materialIndex, and textureProperty.
        /// </summary>
        private static string ResolveMaterialAndTexture(
            Renderer renderer, int materialIndex, string textureProperty,
            out Material mat, out Texture2D tex, bool allowEmpty = false)
        {
            mat = null;
            tex = null;

            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
                return "Error: Renderer has no materials.";
            if (materialIndex < 0 || materialIndex >= mats.Length)
                return $"Error: materialIndex {materialIndex} out of range (0-{mats.Length - 1}).";

            mat = mats[materialIndex];
            if (mat == null)
                return $"Error: Material at index {materialIndex} is null.";

            // Resolve texture property
            string prop = string.IsNullOrEmpty(textureProperty) ? "_MainTex" : textureProperty;
            if (!mat.HasProperty(prop))
                return $"Error: Material '{mat.name}' has no property '{prop}'.";

            Texture rawTex = mat.GetTexture(prop);
            if (rawTex == null)
            {
                if (allowEmpty) { tex = null; return null; }
                return $"Error: Texture property '{prop}' on material '{mat.name}' is null.";
            }

            tex = rawTex as Texture2D;
            if (tex == null)
                return $"Error: Texture '{rawTex.name}' is not a Texture2D (type: {rawTex.GetType().Name}).";

            return null; // success
        }

        /// <summary>
        /// Create a transparent canvas with dimensions matching _MainTex (fallback for empty texture slots).
        /// </summary>
        private static Texture2D CreateFallbackCanvas(Material mat)
        {
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            int w = mainTex != null ? mainTex.width : 1024;
            int h = mainTex != null ? mainTex.height : 1024;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h]; // default Color(0,0,0,0)
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        [AgentTool("Extract texture region for specified mesh islands as a PNG file. Returns the file path for further processing or AI generation. " +
                    "islandIndices: semicolon-separated (e.g. '0;1;3'), empty for entire texture. " +
                    "materialIndex: which material slot to use (default 0, for multi-material renderers). " +
                    "textureProperty: shader texture property name (default '_MainTex', use '_EmissionMap', '_BumpMap', etc. for other textures).")]
        public static string ExtractIslandTexture(string gameObjectName, string islandIndices = "",
            int materialIndex = 0, string textureProperty = "_MainTex")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return "Error: No Renderer found.";

            string resolveErr = ResolveMaterialAndTexture(renderer, materialIndex, textureProperty,
                out Material mat, out Texture2D srcTex, allowEmpty: true);
            if (resolveErr != null) return resolveErr;

            bool createdFallback = false;
            if (srcTex == null)
            {
                srcTex = CreateFallbackCanvas(mat);
                createdFallback = true;
            }
            else
            {
                srcTex = EnsureReadable(srcTex);
            }

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null)
            {
                if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
                return "Error: No mesh found.";
            }

            var islands = UVIslandDetector.DetectIslands(mesh);
            var islandList = ParseIslandIndices(islandIndices, islands.Count);

            // Determine UV bounds
            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;
            Rect uvBounds;

            if (islandList.Count == 0)
            {
                // Entire texture
                uvBounds = new Rect(0, 0, 1, 1);
            }
            else
            {
                uvBounds = ComputeIslandUVBounds(islands, islandList, uvs, tris);
            }

            // Map UV bounds to pixel bounds
            int texW = srcTex.width;
            int texH = srcTex.height;
            int px0 = Mathf.Clamp(Mathf.FloorToInt(uvBounds.xMin * texW), 0, texW);
            int py0 = Mathf.Clamp(Mathf.FloorToInt(uvBounds.yMin * texH), 0, texH);
            int px1 = Mathf.Clamp(Mathf.CeilToInt(uvBounds.xMax * texW), 0, texW);
            int py1 = Mathf.Clamp(Mathf.CeilToInt(uvBounds.yMax * texH), 0, texH);
            int outW = px1 - px0;
            int outH = py1 - py0;

            if (outW <= 0 || outH <= 0) return "Error: Invalid UV bounds (zero-area).";

            // Create UV mask if specific islands selected
            bool[,] mask = null;
            if (islandList.Count > 0)
            {
                mask = CreateUVMask(islands, islandList, uvs, tris, texW, texH, px0, py0, outW, outH);
            }

            // Extract pixels
            var output = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            Color[] srcPixels = srcTex.GetPixels(px0, py0, outW, outH);

            if (mask != null)
            {
                for (int y = 0; y < outH; y++)
                    for (int x = 0; x < outW; x++)
                        if (!mask[x, y])
                            srcPixels[y * outW + x] = Color.clear;
            }

            output.SetPixels(srcPixels);
            output.Apply();

            // Save
            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            string fileName = $"{go.name}_islands_{(islandList.Count > 0 ? string.Join("-", islandList) : "all")}_{DateTime.Now:HHmmss}.png";
            string filePath = $"{GeneratedDir}/{fileName}";
            File.WriteAllBytes(filePath, output.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(output);
            if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
            AssetDatabase.Refresh();

            return $"Success: Extracted to {filePath} ({outW}x{outH}px, UV bounds: [{uvBounds.xMin:F3},{uvBounds.yMin:F3}]-[{uvBounds.xMax:F3},{uvBounds.yMax:F3}])";
        }

        [AgentTool("Generate a normal/position guide map for mesh islands in UV space. The guide map shows 3D surface orientation as colors, helping AI understand how the texture maps to 3D space. Red=X, Green=Y, Blue=Z world direction.")]
        public static string GenerateGuideMap(string gameObjectName, int resolution = 512)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return "Error: No mesh found.";

            Vector2[] uvs = mesh.uv;
            Vector3[] normals = mesh.normals;
            int[] tris = mesh.triangles;

            if (uvs == null || uvs.Length == 0) return "Error: No UVs on mesh.";
            if (normals == null || normals.Length == 0) return "Error: No normals on mesh.";

            // Create guide map: normals in UV space
            var guideTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[resolution * resolution];
            // Initialize transparent
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // Rasterize each triangle
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector2 uv0 = uvs[tris[t]], uv1 = uvs[tris[t + 1]], uv2 = uvs[tris[t + 2]];
                Vector3 n0 = normals[tris[t]], n1 = normals[tris[t + 1]], n2 = normals[tris[t + 2]];

                // Pixel-space UV
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) * resolution));
                int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) * resolution));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) * resolution));
                int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) * resolution));

                for (int py = minY; py <= maxY; py++)
                {
                    for (int px = minX; px <= maxX; px++)
                    {
                        Vector2 p = new Vector2((px + 0.5f) / resolution, (py + 0.5f) / resolution);
                        Vector3 bary = Barycentric(p, uv0, uv1, uv2);
                        if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                        {
                            // Interpolate normal
                            Vector3 n = (n0 * bary.x + n1 * bary.y + n2 * bary.z).normalized;
                            // Map normal [-1,1] to color [0,1]
                            pixels[py * resolution + px] = new Color(
                                n.x * 0.5f + 0.5f,
                                n.y * 0.5f + 0.5f,
                                n.z * 0.5f + 0.5f,
                                1f);
                        }
                    }
                }
            }

            guideTex.SetPixels(pixels);
            guideTex.Apply();

            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            string fileName = $"{go.name}_guide_{resolution}.png";
            string filePath = $"{GeneratedDir}/{fileName}";
            File.WriteAllBytes(filePath, guideTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(guideTex);
            AssetDatabase.Refresh();

            return $"Success: Guide map saved to {filePath} ({resolution}x{resolution}px)";
        }

        [AgentTool("Apply an external PNG image onto specified mesh islands' texture region. The image is mapped to the islands' UV bounding box. " +
                    "imagePath: path to the PNG file. islandIndices: semicolon-separated island indices. " +
                    "materialIndex: which material slot (default 0). " +
                    "textureProperty: shader texture property (default '_MainTex').")]
        public static string ApplyExternalTexture(string gameObjectName, string imagePath, string islandIndices = "",
            int materialIndex = 0, string textureProperty = "_MainTex")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            if (!File.Exists(imagePath))
            {
                // Try as asset path
                string fullPath = Path.Combine(Application.dataPath, "..", imagePath);
                if (!File.Exists(fullPath))
                    return $"Error: Image file '{imagePath}' not found.";
                imagePath = fullPath;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return "Error: No Renderer found.";

            string resolveErr = ResolveMaterialAndTexture(renderer, materialIndex, textureProperty,
                out Material mat, out Texture2D srcTex, allowEmpty: true);
            if (resolveErr != null) return resolveErr;

            bool createdFallback = false;
            if (srcTex == null)
            {
                srcTex = CreateFallbackCanvas(mat);
                createdFallback = true;
            }
            else
            {
                srcTex = EnsureReadable(srcTex);
            }

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null)
            {
                if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
                return "Error: No mesh found.";
            }

            var islands = UVIslandDetector.DetectIslands(mesh);
            var islandList = ParseIslandIndices(islandIndices, islands.Count);

            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;

            // Load external image
            byte[] imgBytes = File.ReadAllBytes(imagePath);
            var inputTex = new Texture2D(2, 2);
            if (!inputTex.LoadImage(imgBytes))
            {
                UnityEngine.Object.DestroyImmediate(inputTex);
                return "Error: Failed to load image file.";
            }

            // Determine UV bounds
            Rect uvBounds;
            if (islandList.Count == 0)
                uvBounds = new Rect(0, 0, 1, 1);
            else
                uvBounds = ComputeIslandUVBounds(islands, islandList, uvs, tris);

            int texW = srcTex.width;
            int texH = srcTex.height;

            // Create editable copy
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply External Texture");

            string avatarName = ToolUtility.FindAvatarRootName(go);
            Texture2D editTex = TextureUtility.CreateEditableTexture(srcTex);
            Color[] editPixels = editTex.GetPixels();

            // Create UV mask
            bool[,] mask = null;
            if (islandList.Count > 0)
            {
                int regionW = Mathf.CeilToInt(uvBounds.width * texW);
                int regionH = Mathf.CeilToInt(uvBounds.height * texH);
                int regionX = Mathf.FloorToInt(uvBounds.xMin * texW);
                int regionY = Mathf.FloorToInt(uvBounds.yMin * texH);
                mask = CreateUVMask(islands, islandList, uvs, tris, texW, texH, regionX, regionY, regionW, regionH);
            }

            // Map input image to UV bounds and paste
            for (int py = Mathf.FloorToInt(uvBounds.yMin * texH); py < Mathf.CeilToInt(uvBounds.yMax * texH) && py < texH; py++)
            {
                for (int px = Mathf.FloorToInt(uvBounds.xMin * texW); px < Mathf.CeilToInt(uvBounds.xMax * texW) && px < texW; px++)
                {
                    if (px < 0 || py < 0) continue;

                    int localX = px - Mathf.FloorToInt(uvBounds.xMin * texW);
                    int localY = py - Mathf.FloorToInt(uvBounds.yMin * texH);

                    if (mask != null)
                    {
                        int mw = mask.GetLength(0), mh = mask.GetLength(1);
                        if (localX < 0 || localX >= mw || localY < 0 || localY >= mh || !mask[localX, localY])
                            continue;
                    }

                    // Sample input texture
                    float u = (px / (float)texW - uvBounds.xMin) / uvBounds.width;
                    float v = (py / (float)texH - uvBounds.yMin) / uvBounds.height;
                    int srcX = Mathf.Clamp(Mathf.FloorToInt(u * inputTex.width), 0, inputTex.width - 1);
                    int srcY = Mathf.Clamp(Mathf.FloorToInt(v * inputTex.height), 0, inputTex.height - 1);
                    Color inputColor = inputTex.GetPixel(srcX, srcY);

                    // Alpha blend
                    int idx = py * texW + px;
                    if (inputColor.a > 0.01f)
                    {
                        Color existing = editPixels[idx];
                        editPixels[idx] = Color.Lerp(existing, inputColor, inputColor.a);
                    }
                }
            }

            // Dilate colors outward from mask edges to prevent bilinear filtering seam artifacts
            if (mask != null)
            {
                int regionX = Mathf.FloorToInt(uvBounds.xMin * texW);
                int regionY = Mathf.FloorToInt(uvBounds.yMin * texH);
                DilateColors(editPixels, texW, texH, mask, regionX, regionY, DilationPadding);
            }

            editTex.SetPixels(editPixels);
            editTex.Apply();

            // Save
            string texPath = TextureUtility.SaveTexture(editTex, avatarName ?? go.name, srcTex.name);
            UnityEngine.Object.DestroyImmediate(editTex);
            UnityEngine.Object.DestroyImmediate(inputTex);
            if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);

            if (string.IsNullOrEmpty(texPath)) return "Error: Failed to save texture.";

            // Assign saved texture to the correct property
            string prop = string.IsNullOrEmpty(textureProperty) ? "_MainTex" : textureProperty;
            Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Undo.RecordObject(mat, "Apply External Texture");
            mat.SetTexture(prop, savedTex);
            EditorUtility.SetDirty(mat);
            Undo.CollapseUndoOperations(undoGroup);

            return $"Success: Applied image to material[{materialIndex}].{prop} on {(islandList.Count > 0 ? $"islands [{string.Join(",", islandList)}]" : "entire texture")}. Saved: {texPath}";
        }

        [AgentTool("Generate a texture variation using AI image generation. Extracts the current texture from the specified material slot and texture property, " +
                    "sends it to the configured image provider with the prompt, and applies the AI-generated result back. " +
                    "gameObjectName: hierarchy path. prompt: what to generate/modify. " +
                    "islandIndices: semicolon-separated island indices (e.g. '0;1;3'), empty for entire texture. " +
                    "materialIndex: which material slot (default 0). " +
                    "textureProperty: shader texture property name (default '_MainTex', use '_EmissionMap' for emission, '_BumpMap' for normal map, etc.). " +
                    "imageModelName: (deprecated, model is configured in settings).")]
        public static IEnumerator GenerateTextureWithAI(string gameObjectName, string prompt, string islandIndices = "",
            int materialIndex = 0, string textureProperty = "_MainTex", string imageModelName = "gemini-2.0-flash-exp")
        {
            // 1. Extract island texture (strict, validates mesh/material/UV)
            string extractResult = ExtractIslandTexture(gameObjectName, islandIndices, materialIndex, textureProperty);
            if (extractResult.StartsWith("Error")) { yield return extractResult; yield break; }

            // Parse file path from result
            int pathStart = extractResult.IndexOf("Extracted to ") + "Extracted to ".Length;
            int pathEnd = extractResult.IndexOf(" (", pathStart);
            if (pathStart < "Extracted to ".Length || pathEnd < 0) { yield return "Error: Could not parse extracted file path."; yield break; }
            string extractedPath = extractResult.Substring(pathStart, pathEnd - pathStart);

            // 2. Create image provider (reads settings from SettingsStore)
            var provider = ProviderRegistry.CreateImageProvider();

            // 3. Build padded extraction for AI (wider opaque context to avoid edge darkening)
            int cropLeft = 0, cropBottom = 0, cropRight = 0, cropTop = 0;
            byte[] aiInputBytes = CreatePaddedExtraction(
                gameObjectName, islandIndices, materialIndex, textureProperty, ExtractionPadding,
                out cropLeft, out cropBottom, out cropRight, out cropTop);
            if (aiInputBytes == null)
            {
                // No padding applicable: use strict extraction
                string fullPath = Path.Combine(Application.dataPath, "..", extractedPath);
                if (!File.Exists(fullPath)) { yield return $"Error: Extracted file not found: {fullPath}"; yield break; }
                aiInputBytes = File.ReadAllBytes(fullPath);
            }

            string prop = string.IsNullOrEmpty(textureProperty) ? "_MainTex" : textureProperty;
            string systemPrompt = BuildSystemPrompt(prop);

            // 4. Send image generation request via image provider
            string truncatedPrompt = prompt.Length > 100 ? prompt.Substring(0, 100) + "..." : prompt;
            string detail = $"プロバイダー: {provider.ProviderName}\nプロンプト: {truncatedPrompt}";
            ToolProgress.Report(0.1f, "AI テクスチャ生成中...", detail);

            byte[] generatedImage = null;
            string usageInfo = null;
            string errorMsg = null;

            yield return provider.GenerateImage(
                systemPrompt, prompt, aiInputBytes,
                onSuccess: (img, usage) => { generatedImage = img; usageInfo = usage; },
                onError: err => errorMsg = err,
                onStatus: msg => ToolProgress.Report(
                    Mathf.PingPong(Time.realtimeSinceStartup * 0.2f, 1f), msg),
                onDebugLog: log => Debug.Log($"[TextureGen] {log}")
            );
            ToolProgress.Clear();

            if (errorMsg != null)
            {
                yield return $"Error: AI generation failed: {errorMsg}";
                yield break;
            }
            if (generatedImage == null)
            {
                yield return "Error: No image returned from AI generation.";
                yield break;
            }

            // 5. Crop padded AI result back to strict island bounds
            if (cropLeft + cropBottom + cropRight + cropTop > 0)
                generatedImage = CropImage(generatedImage, cropLeft, cropBottom, cropRight, cropTop);

            // 6. Save generated image
            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            string safeName = gameObjectName.Replace("/", "_").Replace("\\", "_");
            string genFileName = $"{safeName}_ai_gen_{DateTime.Now:HHmmss}.png";
            string genFilePath = $"{GeneratedDir}/{genFileName}";
            File.WriteAllBytes(genFilePath, generatedImage);
            SceneViewTools.SetPendingImage(generatedImage, "image/png");
            AssetDatabase.Refresh();

            // 7. Apply to mesh (same material/property)
            string applyResult = ApplyExternalTexture(gameObjectName, genFilePath, islandIndices, materialIndex, textureProperty);

            yield return $"AI generation complete. Generated: {genFilePath}\nProvider: {provider.ProviderName}\n{usageInfo}\n{applyResult}";
        }

        // ─── Padded Extraction / Crop Helpers ───

        /// <summary>
        /// Create a padded extraction for AI input. The UV mask is dilated so the AI sees
        /// more opaque context around each island, preventing edge-darkening artifacts.
        /// Returns null if no padding is needed (empty islandList or padding=0).
        /// </summary>
        private static byte[] CreatePaddedExtraction(
            string gameObjectName, string islandIndices, int materialIndex, string textureProperty, int padding,
            out int cropLeft, out int cropBottom, out int cropRight, out int cropTop)
        {
            cropLeft = cropBottom = cropRight = cropTop = 0;

            var go = FindGO(gameObjectName);
            if (go == null) return null;
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return null;
            string err = ResolveMaterialAndTexture(renderer, materialIndex, textureProperty,
                out Material mat, out Texture2D srcTex, allowEmpty: true);
            if (err != null) return null;

            bool createdFallback = false;
            if (srcTex == null)
            {
                srcTex = CreateFallbackCanvas(mat);
                createdFallback = true;
            }
            else
            {
                srcTex = EnsureReadable(srcTex);
            }

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null)
            {
                if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
                return null;
            }
            var islands = UVIslandDetector.DetectIslands(mesh);
            var islandList = ParseIslandIndices(islandIndices, islands.Count);
            if (islandList.Count == 0 || padding <= 0)
            {
                if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
                return null;
            }

            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;
            int texW = srcTex.width, texH = srcTex.height;

            // Strict pixel bounds
            Rect strictUV = ComputeIslandUVBounds(islands, islandList, uvs, tris);
            int sx0 = Mathf.Clamp(Mathf.FloorToInt(strictUV.xMin * texW), 0, texW);
            int sy0 = Mathf.Clamp(Mathf.FloorToInt(strictUV.yMin * texH), 0, texH);
            int sx1 = Mathf.Clamp(Mathf.CeilToInt(strictUV.xMax * texW), 0, texW);
            int sy1 = Mathf.Clamp(Mathf.CeilToInt(strictUV.yMax * texH), 0, texH);

            // Padded pixel bounds
            int px0 = Mathf.Clamp(sx0 - padding, 0, texW);
            int py0 = Mathf.Clamp(sy0 - padding, 0, texH);
            int px1 = Mathf.Clamp(sx1 + padding, 0, texW);
            int py1 = Mathf.Clamp(sy1 + padding, 0, texH);
            int pW = px1 - px0, pH = py1 - py0;
            if (pW <= 0 || pH <= 0)
            {
                if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
                return null;
            }

            // Crop offsets for trimming the AI output back to strict bounds
            cropLeft = sx0 - px0;
            cropBottom = sy0 - py0;
            cropRight = px1 - sx1;
            cropTop = py1 - sy1;

            // Create strict mask within padded region, then dilate
            var strictMask = CreateUVMask(islands, islandList, uvs, tris, texW, texH, px0, py0, pW, pH);
            var expandedMask = DilateMask(strictMask, padding);

            // Extract padded region with expanded mask
            Color[] pixels = srcTex.GetPixels(px0, py0, pW, pH);
            for (int y = 0; y < pH; y++)
                for (int x = 0; x < pW; x++)
                    if (!expandedMask[x, y])
                        pixels[y * pW + x] = Color.clear;

            var tex = new Texture2D(pW, pH, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] result = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            if (createdFallback) UnityEngine.Object.DestroyImmediate(srcTex);
            return result;
        }

        /// <summary>
        /// Crop padding pixels from a PNG image. Used to trim padded AI output to strict island bounds.
        /// </summary>
        private static byte[] CropImage(byte[] pngBytes, int cropLeft, int cropBottom, int cropRight, int cropTop)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(pngBytes);
            int newW = tex.width - cropLeft - cropRight;
            int newH = tex.height - cropBottom - cropTop;
            if (newW <= 0 || newH <= 0)
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return pngBytes;
            }
            Color[] pixels = tex.GetPixels(cropLeft, cropBottom, newW, newH);
            UnityEngine.Object.DestroyImmediate(tex);

            var cropped = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
            cropped.SetPixels(pixels);
            cropped.Apply();
            byte[] result = cropped.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(cropped);
            return result;
        }

        /// <summary>
        /// Dilate a boolean mask by radius pixels (4-connected flood fill).
        /// </summary>
        private static bool[,] DilateMask(bool[,] mask, int radius)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            bool[,] result = (bool[,])mask.Clone();

            for (int iter = 0; iter < radius; iter++)
            {
                bool[,] prev = (bool[,])result.Clone();
                bool changed = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (prev[x, y]) continue;
                        if ((x > 0 && prev[x - 1, y]) || (x < w - 1 && prev[x + 1, y]) ||
                            (y > 0 && prev[x, y - 1]) || (y < h - 1 && prev[x, y + 1]))
                        {
                            result[x, y] = true;
                            changed = true;
                        }
                    }
                }
                if (!changed) break;
            }
            return result;
        }

        // ─── UV Helpers ───

        /// <summary>
        /// Dilate (bleed) colors outward from UV mask boundary to fill gutter pixels.
        /// Prevents bilinear filtering from sampling black/stale colors at UV island seams.
        /// Uses iterative flood-fill: each iteration extends by 1 pixel, averaging valid neighbors.
        /// </summary>
        private static void DilateColors(Color[] pixels, int texW, int texH,
            bool[,] mask, int offsetX, int offsetY, int radius)
        {
            int mW = mask.GetLength(0), mH = mask.GetLength(1);

            // Work area: mask region expanded by radius (clamped to texture bounds)
            int aMinX = Mathf.Max(0, offsetX - radius);
            int aMinY = Mathf.Max(0, offsetY - radius);
            int aMaxX = Mathf.Min(texW, offsetX + mW + radius);
            int aMaxY = Mathf.Min(texH, offsetY + mH + radius);
            int aW = aMaxX - aMinX, aH = aMaxY - aMinY;

            // Validity map for the work area
            bool[] valid = new bool[aW * aH];
            for (int my = 0; my < mH; my++)
                for (int mx = 0; mx < mW; mx++)
                    if (mask[mx, my])
                        valid[(offsetY + my - aMinY) * aW + (offsetX + mx - aMinX)] = true;

            for (int iter = 0; iter < radius; iter++)
            {
                bool[] prev = (bool[])valid.Clone();
                bool changed = false;

                for (int ay = 0; ay < aH; ay++)
                {
                    for (int ax = 0; ax < aW; ax++)
                    {
                        if (prev[ay * aW + ax]) continue;

                        float r = 0, g = 0, b = 0;
                        int n = 0;

                        if (ax > 0 && prev[ay * aW + ax - 1])
                        { var c = pixels[(aMinY + ay) * texW + aMinX + ax - 1]; r += c.r; g += c.g; b += c.b; n++; }
                        if (ax < aW - 1 && prev[ay * aW + ax + 1])
                        { var c = pixels[(aMinY + ay) * texW + aMinX + ax + 1]; r += c.r; g += c.g; b += c.b; n++; }
                        if (ay > 0 && prev[(ay - 1) * aW + ax])
                        { var c = pixels[(aMinY + ay - 1) * texW + aMinX + ax]; r += c.r; g += c.g; b += c.b; n++; }
                        if (ay < aH - 1 && prev[(ay + 1) * aW + ax])
                        { var c = pixels[(aMinY + ay + 1) * texW + aMinX + ax]; r += c.r; g += c.g; b += c.b; n++; }

                        if (n > 0)
                        {
                            pixels[(aMinY + ay) * texW + aMinX + ax] = new Color(r / n, g / n, b / n, 1f);
                            valid[ay * aW + ax] = true;
                            changed = true;
                        }
                    }
                }

                if (!changed) break;
            }
        }

        private static Rect ComputeIslandUVBounds(List<UVIsland> islands, List<int> indices, Vector2[] uvs, int[] tris)
        {
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= islands.Count) continue;
                var island = islands[idx];
                foreach (int triIdx in island.triangleIndices)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        Vector2 uv = uvs[tris[triIdx * 3 + j]];
                        minU = Mathf.Min(minU, uv.x);
                        minV = Mathf.Min(minV, uv.y);
                        maxU = Mathf.Max(maxU, uv.x);
                        maxV = Mathf.Max(maxV, uv.y);
                    }
                }
            }

            // Add 1px margin
            return new Rect(minU, minV, maxU - minU, maxV - minV);
        }

        private static bool[,] CreateUVMask(List<UVIsland> islands, List<int> indices,
            Vector2[] uvs, int[] tris, int texW, int texH, int offsetX, int offsetY, int maskW, int maskH)
        {
            bool[,] mask = new bool[maskW, maskH];

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= islands.Count) continue;
                foreach (int triIdx in islands[idx].triangleIndices)
                {
                    Vector2 uv0 = uvs[tris[triIdx * 3]];
                    Vector2 uv1 = uvs[tris[triIdx * 3 + 1]];
                    Vector2 uv2 = uvs[tris[triIdx * 3 + 2]];

                    // Convert to pixel space
                    Vector2 p0 = new Vector2(uv0.x * texW, uv0.y * texH);
                    Vector2 p1 = new Vector2(uv1.x * texW, uv1.y * texH);
                    Vector2 p2 = new Vector2(uv2.x * texW, uv2.y * texH);

                    // Bounding box of triangle in pixel space
                    int triMinX = Mathf.Max(offsetX, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
                    int triMaxX = Mathf.Min(offsetX + maskW - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
                    int triMinY = Mathf.Max(offsetY, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
                    int triMaxY = Mathf.Min(offsetY + maskH - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));

                    for (int py = triMinY; py <= triMaxY; py++)
                    {
                        for (int px = triMinX; px <= triMaxX; px++)
                        {
                            Vector2 pt = new Vector2(px + 0.5f, py + 0.5f);
                            Vector3 bary = Barycentric(pt, p0, p1, p2);
                            if (bary.x >= -0.001f && bary.y >= -0.001f && bary.z >= -0.001f)
                            {
                                int mx = px - offsetX;
                                int my = py - offsetY;
                                if (mx >= 0 && mx < maskW && my >= 0 && my < maskH)
                                    mask[mx, my] = true;
                            }
                        }
                    }
                }
            }

            return mask;
        }

        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float inv = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(inv) < 1e-10f) return new Vector3(-1, -1, -1);
            inv = 1f / inv;

            float u = (dot11 * dot02 - dot01 * dot12) * inv;
            float v = (dot00 * dot12 - dot01 * dot02) * inv;
            return new Vector3(1f - u - v, v, u);
        }

        // ─── System Prompt ───

        private static string BuildSystemPrompt(string textureProperty)
        {
            var sb = new StringBuilder();
            sb.Append("You are a texture editing AI for 3D avatar models. ");
            sb.Append("The input image is a UV-mapped texture extracted from a 3D mesh. ");
            sb.AppendLine();
            sb.AppendLine("CRITICAL RULES:");
            sb.Append("1. Output image MUST have the EXACT same dimensions (width x height) as the input. ");
            sb.AppendLine("Do NOT resize, crop, or change aspect ratio.");
            sb.Append("2. Transparent/clear areas (alpha=0) in the input represent regions outside the target mesh islands. ");
            sb.AppendLine("Keep these areas transparent — do NOT paint into them.");
            sb.Append("3. Only modify the opaque (visible) pixel regions. ");
            sb.AppendLine("The spatial layout of painted vs transparent areas defines the UV mapping and must be preserved exactly.");
            sb.Append("4. The texture will be applied back onto a 3D model. ");
            sb.AppendLine("Ensure smooth color transitions and avoid hard rectangular edges that would look unnatural in 3D.");
            sb.Append("5. Colors must extend fully and evenly to the very edges of each opaque region. ");
            sb.AppendLine("Do NOT darken, fade, or anti-alias pixels near the transparent boundary — maintain full color intensity right up to the edge.");

            if (textureProperty == "_EmissionMap")
            {
                sb.AppendLine("6. This is an EMISSION MAP — it controls which parts of the model glow/emit light. "
                    + "Use bright, vivid colors for areas that should glow. Black areas will not emit light.");
            }
            else if (textureProperty == "_BumpMap")
            {
                sb.AppendLine("6. This is a NORMAL MAP — use the standard tangent-space normal map color convention "
                    + "(neutral = RGB 128,128,255 / purple-blue). Do NOT use arbitrary colors.");
            }

            return sb.ToString();
        }

        // ─── Utility ───

        private static List<int> ParseIslandIndices(string indices, int maxCount)
        {
            if (string.IsNullOrEmpty(indices)) return new List<int>();
            var list = new List<int>();
            foreach (string s in indices.Split(';', ','))
            {
                if (int.TryParse(s.Trim(), out int idx) && idx >= 0 && idx < maxCount)
                    list.Add(idx);
            }
            return list;
        }

        private static Texture2D EnsureReadable(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return tex;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return tex;
        }

    }
}
