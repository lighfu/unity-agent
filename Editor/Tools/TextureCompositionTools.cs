using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class TextureCompositionTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("Textures");

        // ─── Shared Helpers ───

        private static Color ApplyBlendMode(Color baseC, Color overlay, string blendMode, float opacity)
        {
            Color result;
            switch (blendMode)
            {
                case "multiply":
                    result = new Color(baseC.r * overlay.r, baseC.g * overlay.g, baseC.b * overlay.b);
                    break;
                case "screen":
                    result = new Color(
                        1f - (1f - baseC.r) * (1f - overlay.r),
                        1f - (1f - baseC.g) * (1f - overlay.g),
                        1f - (1f - baseC.b) * (1f - overlay.b));
                    break;
                case "overlay":
                    result = new Color(
                        baseC.r < 0.5f ? 2f * baseC.r * overlay.r : 1f - 2f * (1f - baseC.r) * (1f - overlay.r),
                        baseC.g < 0.5f ? 2f * baseC.g * overlay.g : 1f - 2f * (1f - baseC.g) * (1f - overlay.g),
                        baseC.b < 0.5f ? 2f * baseC.b * overlay.b : 1f - 2f * (1f - baseC.b) * (1f - overlay.b));
                    break;
                case "soft_light":
                    result = new Color(
                        SoftLight(baseC.r, overlay.r),
                        SoftLight(baseC.g, overlay.g),
                        SoftLight(baseC.b, overlay.b));
                    break;
                case "hard_light":
                    result = new Color(
                        overlay.r < 0.5f ? 2f * baseC.r * overlay.r : 1f - 2f * (1f - baseC.r) * (1f - overlay.r),
                        overlay.g < 0.5f ? 2f * baseC.g * overlay.g : 1f - 2f * (1f - baseC.g) * (1f - overlay.g),
                        overlay.b < 0.5f ? 2f * baseC.b * overlay.b : 1f - 2f * (1f - baseC.b) * (1f - overlay.b));
                    break;
                case "color_dodge":
                    result = new Color(
                        ColorDodge(baseC.r, overlay.r),
                        ColorDodge(baseC.g, overlay.g),
                        ColorDodge(baseC.b, overlay.b));
                    break;
                case "color_burn":
                    result = new Color(
                        ColorBurn(baseC.r, overlay.r),
                        ColorBurn(baseC.g, overlay.g),
                        ColorBurn(baseC.b, overlay.b));
                    break;
                case "darken":
                    result = new Color(
                        Mathf.Min(baseC.r, overlay.r),
                        Mathf.Min(baseC.g, overlay.g),
                        Mathf.Min(baseC.b, overlay.b));
                    break;
                case "lighten":
                    result = new Color(
                        Mathf.Max(baseC.r, overlay.r),
                        Mathf.Max(baseC.g, overlay.g),
                        Mathf.Max(baseC.b, overlay.b));
                    break;
                case "difference":
                    result = new Color(
                        Mathf.Abs(baseC.r - overlay.r),
                        Mathf.Abs(baseC.g - overlay.g),
                        Mathf.Abs(baseC.b - overlay.b));
                    break;
                case "exclusion":
                    result = new Color(
                        baseC.r + overlay.r - 2f * baseC.r * overlay.r,
                        baseC.g + overlay.g - 2f * baseC.g * overlay.g,
                        baseC.b + overlay.b - 2f * baseC.b * overlay.b);
                    break;
                default: // normal
                    result = overlay;
                    break;
            }

            result = Color.Lerp(baseC, result, opacity);
            result.a = baseC.a;
            return result;
        }

        private static float SoftLight(float b, float s)
        {
            if (s < 0.5f)
                return b - (1f - 2f * s) * b * (1f - b);
            float d = b <= 0.25f ? ((16f * b - 12f) * b + 4f) * b : Mathf.Sqrt(b);
            return b + (2f * s - 1f) * (d - b);
        }

        private static float ColorDodge(float b, float s)
        {
            if (s >= 1f) return 1f;
            return Mathf.Clamp01(b / (1f - s));
        }

        private static float ColorBurn(float b, float s)
        {
            if (s <= 0f) return 0f;
            return 1f - Mathf.Clamp01((1f - b) / s);
        }

        private static Texture2D LoadAndEnsureReadable(string texturePath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) return null;
            return TextureUtility.CreateEditableTexture(tex);
        }

        private static string SaveGeneratedTexture(Texture2D tex, string outputName, bool sRGB)
        {
            byte[] bytes = tex.EncodeToPNG();
            if (bytes == null) return null;

            string folderPath = (GeneratedDir + "/Composed").Replace("\\", "/");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fullPath = Path.Combine(folderPath, outputName + ".png").Replace("\\", "/");
            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(fullPath);

            TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = sRGB;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return fullPath;
        }

        // ─── Tools ───

        [AgentTool("Blend two textures together using a specified blend mode. Creates a new texture asset. baseTexturePath/overlayTexturePath: asset paths. blendMode: normal, multiply, screen, overlay, soft_light, hard_light, color_dodge, color_burn, darken, lighten, difference, exclusion. opacity: 0.0-1.0. outputName: filename without extension.")]
        public static string BlendTextures(
            string baseTexturePath,
            string overlayTexturePath,
            string blendMode = "normal",
            float opacity = 1.0f,
            string outputName = "")
        {
            var baseTex = LoadAndEnsureReadable(baseTexturePath);
            if (baseTex == null) return $"Error: Base texture not found at '{baseTexturePath}'.";

            var overlayTex = LoadAndEnsureReadable(overlayTexturePath);
            if (overlayTex == null) return $"Error: Overlay texture not found at '{overlayTexturePath}'.";

            string[] validModes = { "normal", "multiply", "screen", "overlay", "soft_light", "hard_light", "color_dodge", "color_burn", "darken", "lighten", "difference", "exclusion" };
            if (!Array.Exists(validModes, m => m == blendMode))
                return $"Error: Invalid blendMode '{blendMode}'. Valid: {string.Join(", ", validModes)}.";

            opacity = Mathf.Clamp01(opacity);

            int width = baseTex.width;
            int height = baseTex.height;

            Texture2D resizedOverlay = overlayTex;
            if (overlayTex.width != width || overlayTex.height != height)
                resizedOverlay = ResizeTextureInternal(overlayTex, width, height);

            Color[] basePixels = baseTex.GetPixels();
            Color[] overlayPixels = resizedOverlay.GetPixels();
            Color[] resultPixels = new Color[basePixels.Length];

            for (int i = 0; i < basePixels.Length; i++)
                resultPixels[i] = ApplyBlendMode(basePixels[i], overlayPixels[i], blendMode, opacity);

            var resultTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            resultTex.SetPixels(resultPixels);
            resultTex.Apply();

            if (string.IsNullOrEmpty(outputName))
            {
                string baseName = Path.GetFileNameWithoutExtension(baseTexturePath);
                outputName = $"{baseName}_{blendMode}";
            }

            string savedPath = SaveGeneratedTexture(resultTex, outputName, true);
            UnityEngine.Object.DestroyImmediate(resultTex);
            if (resizedOverlay != overlayTex) UnityEngine.Object.DestroyImmediate(resizedOverlay);
            UnityEngine.Object.DestroyImmediate(baseTex);
            UnityEngine.Object.DestroyImmediate(overlayTex);

            if (string.IsNullOrEmpty(savedPath)) return "Error: Failed to save blended texture.";
            return $"Success: Blended textures with '{blendMode}' (opacity={opacity:F2}). Output: {savedPath} ({width}x{height})";
        }

        [AgentTool("Blend an overlay texture onto a GameObject's main texture with island filtering. Non-destructive (creates copy). overlayTexturePath: asset path to overlay image. blendMode: normal, multiply, screen, overlay, soft_light, hard_light, color_dodge, color_burn, darken, lighten, difference, exclusion. opacity: 0.0-1.0. islandIndices: semicolon-separated like '0;1;3' (empty=all).")]
        public static string BlendTextureOntoGameObject(
            string gameObjectName,
            string overlayTexturePath,
            string blendMode = "normal",
            float opacity = 1.0f,
            string islandIndices = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            Mesh mesh = ToolUtility.GetMesh(go);

            if (!TryParseIslandIndices(islandIndices, out List<int> islandIndexList, out string islandError))
                return islandError;

            int materialIndex = TextureEditTools.DetectMaterialIndex(mesh, islandIndexList);
            if (materialIndex >= renderer.sharedMaterials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{renderer.sharedMaterials.Length - 1}) on '{gameObjectName}'.";
            Material mat = renderer.sharedMaterials[materialIndex];
            if (mat == null) return $"Error: No material at slot {materialIndex} on '{gameObjectName}'.";

            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return $"Error: No main texture found on material '{mat.name}'.";

            var overlayTex = LoadAndEnsureReadable(overlayTexturePath);
            if (overlayTex == null) return $"Error: Overlay texture not found at '{overlayTexturePath}'.";

            string[] validModes = { "normal", "multiply", "screen", "overlay", "soft_light", "hard_light", "color_dodge", "color_burn", "darken", "lighten", "difference", "exclusion" };
            if (!Array.Exists(validModes, m => m == blendMode))
                return $"Error: Invalid blendMode '{blendMode}'. Valid: {string.Join(", ", validModes)}.";

            opacity = Mathf.Clamp01(opacity);

            string avatarName = ToolUtility.FindAvatarRootName(go);
            if (string.IsNullOrEmpty(avatarName))
                return "Error: Could not determine avatar root.";

            try
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("BlendTextureOntoGameObject via Agent");
                Undo.RecordObject(renderer, "BlendTextureOntoGameObject via Agent");

                string originalTexPath = AssetDatabase.GetAssetPath(sourceTex);
                string originalTexGuid = AssetDatabase.AssetPathToGUID(originalTexPath);

                var metadata = MetadataManager.LoadMetadata(avatarName, go.name);
                bool isPartialEdit = islandIndexList.Count > 0;

                if (isPartialEdit)
                {
                    if (metadata == null) metadata = new MeshPaintMetadata();
                    if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                        metadata.originalTextureGuid = originalTexGuid;
                    originalTexGuid = metadata.originalTextureGuid;
                }
                else
                {
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
                    TextureEditTools.SetMaterialAtIndex(renderer, materialIndex, mat);
                }

                if (metadata == null) metadata = new MeshPaintMetadata();

                int width = editableTex.width;
                int height = editableTex.height;

                Texture2D resizedOverlay = overlayTex;
                if (overlayTex.width != width || overlayTex.height != height)
                    resizedOverlay = ResizeTextureInternal(overlayTex, width, height);

                Color[] basePixels = editableTex.GetPixels();
                Color[] overlayPixels = resizedOverlay.GetPixels();

                bool[,] mask = null;
                if (islandIndexList.Count > 0 && mesh != null)
                {
                    var islands = UVIslandDetector.DetectIslands(mesh);
                    foreach (int idx in islandIndexList)
                    {
                        if (idx >= islands.Count)
                            return $"Error: Island index {idx} out of range. Mesh has {islands.Count} islands (0-{islands.Count - 1}).";
                    }
                    mask = BuildPixelMask(editableTex, mesh, islandIndexList);
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (mask != null && !mask[x, y]) continue;
                        int idx = y * width + x;
                        basePixels[idx] = ApplyBlendMode(basePixels[idx], overlayPixels[idx], blendMode, opacity);
                    }
                }

                editableTex.SetPixels(basePixels);
                editableTex.Apply();

                if (resizedOverlay != overlayTex)
                    UnityEngine.Object.DestroyImmediate(resizedOverlay);
                UnityEngine.Object.DestroyImmediate(overlayTex);

                string safeName = go.name.Replace("/", "_").Replace("\\", "_");
                string texPath = TextureUtility.SaveTexture(editableTex, avatarName, sourceTex.name + "_" + safeName);
                UnityEngine.Object.DestroyImmediate(editableTex);
                if (string.IsNullOrEmpty(texPath)) return "Error: Failed to save blended texture.";

                Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                Undo.RecordObject(mat, "BlendTextureOntoGameObject via Agent");
                mat.mainTexture = savedTex;

                if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                    metadata.originalTextureGuid = originalTexGuid;
                MetadataManager.SaveMetadata(metadata, avatarName, go.name);

                EditorUtility.SetDirty(mat);
                Undo.CollapseUndoOperations(undoGroup);

                string islandInfo = islandIndexList.Count > 0 ? $" islands=[{string.Join(";", islandIndexList)}]" : "";
                return $"Success: Blended overlay with '{blendMode}' (opacity={opacity:F2}) onto '{gameObjectName}'.{islandInfo} Texture: {texPath}";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureCompositionTools] BlendTextureOntoGameObject Error: {ex}");
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("Pack up to 4 grayscale textures into RGBA channels of a single texture. Provide asset paths for each channel (empty string to skip/fill with default). defaultValue: 0.0 (black) or 1.0 (white) for missing channels. outputName: filename without extension. resolution: output size (0=use first texture's size).")]
        public static string PackChannelTexture(
            string redTexturePath = "",
            string greenTexturePath = "",
            string blueTexturePath = "",
            string alphaTexturePath = "",
            float defaultValue = 0f,
            string outputName = "packed_channels",
            int resolution = 0)
        {
            Texture2D rTex = !string.IsNullOrEmpty(redTexturePath) ? LoadAndEnsureReadable(redTexturePath) : null;
            Texture2D gTex = !string.IsNullOrEmpty(greenTexturePath) ? LoadAndEnsureReadable(greenTexturePath) : null;
            Texture2D bTex = !string.IsNullOrEmpty(blueTexturePath) ? LoadAndEnsureReadable(blueTexturePath) : null;
            Texture2D aTex = !string.IsNullOrEmpty(alphaTexturePath) ? LoadAndEnsureReadable(alphaTexturePath) : null;

            if (rTex == null && gTex == null && bTex == null && aTex == null)
                return "Error: At least one channel texture must be provided.";

            if (!string.IsNullOrEmpty(redTexturePath) && rTex == null)
                return $"Error: Red channel texture not found at '{redTexturePath}'.";
            if (!string.IsNullOrEmpty(greenTexturePath) && gTex == null)
                return $"Error: Green channel texture not found at '{greenTexturePath}'.";
            if (!string.IsNullOrEmpty(blueTexturePath) && bTex == null)
                return $"Error: Blue channel texture not found at '{blueTexturePath}'.";
            if (!string.IsNullOrEmpty(alphaTexturePath) && aTex == null)
                return $"Error: Alpha channel texture not found at '{alphaTexturePath}'.";

            Texture2D firstTex = rTex ?? gTex ?? bTex ?? aTex;
            int width = resolution > 0 ? resolution : firstTex.width;
            int height = resolution > 0 ? resolution : firstTex.height;
            float defVal = Mathf.Clamp01(defaultValue);

            Color[] rPixels = GetChannelPixels(rTex, width, height);
            Color[] gPixels = GetChannelPixels(gTex, width, height);
            Color[] bPixels = GetChannelPixels(bTex, width, height);
            Color[] aPixels = GetChannelPixels(aTex, width, height);

            int totalPixels = width * height;
            Color[] result = new Color[totalPixels];

            for (int i = 0; i < totalPixels; i++)
            {
                result[i] = new Color(
                    rPixels != null ? rPixels[i].grayscale : defVal,
                    gPixels != null ? gPixels[i].grayscale : defVal,
                    bPixels != null ? bPixels[i].grayscale : defVal,
                    aPixels != null ? aPixels[i].grayscale : defVal);
            }

            var packed = new Texture2D(width, height, TextureFormat.RGBA32, false);
            packed.SetPixels(result);
            packed.Apply();

            string savedPath = SaveGeneratedTexture(packed, outputName, false);
            UnityEngine.Object.DestroyImmediate(packed);
            if (rTex != null) UnityEngine.Object.DestroyImmediate(rTex);
            if (gTex != null) UnityEngine.Object.DestroyImmediate(gTex);
            if (bTex != null) UnityEngine.Object.DestroyImmediate(bTex);
            if (aTex != null) UnityEngine.Object.DestroyImmediate(aTex);

            if (string.IsNullOrEmpty(savedPath)) return "Error: Failed to save packed texture.";

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Packed channel texture ({width}x{height}). Output: {savedPath}");
            sb.AppendLine($"  R: {(rTex != null ? redTexturePath : $"default ({defVal:F1})")}");
            sb.AppendLine($"  G: {(gTex != null ? greenTexturePath : $"default ({defVal:F1})")}");
            sb.AppendLine($"  B: {(bTex != null ? blueTexturePath : $"default ({defVal:F1})")}");
            sb.AppendLine($"  A: {(aTex != null ? alphaTexturePath : $"default ({defVal:F1})")}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Extract a single channel from a texture as a grayscale image. texturePath: asset path. channel: r, g, b, or a. outputName: filename without extension.")]
        public static string UnpackChannelTexture(
            string texturePath,
            string channel = "r",
            string outputName = "")
        {
            var tex = LoadAndEnsureReadable(texturePath);
            if (tex == null) return $"Error: Texture not found at '{texturePath}'.";

            channel = channel.ToLower();
            if (channel != "r" && channel != "g" && channel != "b" && channel != "a")
                return $"Error: Invalid channel '{channel}'. Use: r, g, b, a.";

            Color[] pixels = tex.GetPixels();
            Color[] result = new Color[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                float v;
                switch (channel)
                {
                    case "r": v = pixels[i].r; break;
                    case "g": v = pixels[i].g; break;
                    case "b": v = pixels[i].b; break;
                    default:  v = pixels[i].a; break;
                }
                result[i] = new Color(v, v, v, 1f);
            }

            var output = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            output.SetPixels(result);
            output.Apply();

            if (string.IsNullOrEmpty(outputName))
                outputName = $"{Path.GetFileNameWithoutExtension(texturePath)}_{channel}";

            string savedPath = SaveGeneratedTexture(output, outputName, false);
            UnityEngine.Object.DestroyImmediate(output);

            if (string.IsNullOrEmpty(savedPath)) return "Error: Failed to save unpacked texture.";
            return $"Success: Extracted '{channel}' channel from '{texturePath}'. Output: {savedPath} ({tex.width}x{tex.height})";
        }

        [AgentTool("Resize a texture to new dimensions using high-quality GPU Blit. texturePath: asset path. newWidth/newHeight: target size (1-8192). outputName: filename without extension. Shows VRAM impact.")]
        public static string ResizeTexture(
            string texturePath,
            int newWidth,
            int newHeight,
            string outputName = "")
        {
            var sourceTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (sourceTex == null) return $"Error: Texture not found at '{texturePath}'.";

            if (newWidth <= 0 || newHeight <= 0 || newWidth > 8192 || newHeight > 8192)
                return $"Error: Invalid dimensions {newWidth}x{newHeight}. Range: 1-8192.";

            int oldWidth = sourceTex.width;
            int oldHeight = sourceTex.height;

            Texture2D editable = TextureUtility.CreateEditableTexture(sourceTex);
            Texture2D resized = ResizeTextureInternal(editable, newWidth, newHeight);
            UnityEngine.Object.DestroyImmediate(editable);

            if (string.IsNullOrEmpty(outputName))
                outputName = $"{Path.GetFileNameWithoutExtension(texturePath)}_{newWidth}x{newHeight}";

            string savedPath = SaveGeneratedTexture(resized, outputName, true);
            UnityEngine.Object.DestroyImmediate(resized);

            if (string.IsNullOrEmpty(savedPath)) return "Error: Failed to save resized texture.";

            long oldVRAM = (long)oldWidth * oldHeight * 4;
            long newVRAM = (long)newWidth * newHeight * 4;
            float ratio = newVRAM / (float)oldVRAM;

            return $"Success: Resized '{texturePath}' from {oldWidth}x{oldHeight} to {newWidth}x{newHeight}. Output: {savedPath}\n  VRAM (uncompressed): {FormatBytes(oldVRAM)} -> {FormatBytes(newVRAM)} ({ratio:F2}x)";
        }

        [AgentTool("Create a blank mask texture filled with a single color. Useful for masks, mattes, or starting points. width/height: dimensions (1-8192). color: 'black' (default 0,0,0,1), 'white' (1,1,1,1), or 'R,G,B,A' (0-1 floats). outputName: filename without extension.")]
        public static string CreateMaskTexture(
            int width = 1024,
            int height = 1024,
            string color = "black",
            string outputName = "mask")
        {
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
                return $"Error: Invalid dimensions {width}x{height}. Range: 1-8192.";

            Color fillColor;
            switch (color.ToLower())
            {
                case "black": fillColor = Color.black; break;
                case "white": fillColor = Color.white; break;
                default:
                    if (!TryParseColorValue(color, out fillColor))
                        return $"Error: Invalid color '{color}'. Use 'black', 'white', or 'R,G,B,A'.";
                    break;
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fillColor;
            tex.SetPixels(pixels);
            tex.Apply();

            string savedPath = SaveGeneratedTexture(tex, outputName, false);
            UnityEngine.Object.DestroyImmediate(tex);

            if (string.IsNullOrEmpty(savedPath)) return "Error: Failed to save mask texture.";
            return $"Success: Created {width}x{height} mask texture ({color}). Output: {savedPath}";
        }

        [AgentTool("Configure a decal slot on a Poiyomi or lilToon material. Sets the decal texture, enables required keywords, and adjusts slot properties. materialPath: asset path to material. slotIndex: decal slot number (0-3 for Poiyomi, 0-1 for lilToon). texturePath: asset path to decal texture. position/scale: 'x,y' format. angle: rotation degrees.")]
        public static string ConfigureDecalSlot(
            string materialPath,
            int slotIndex,
            string texturePath,
            string position = "",
            string scale = "",
            float angle = 0f)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (tex == null) return $"Error: Texture not found at '{texturePath}'.";

            string shaderName = mat.shader.name.ToLower();
            Undo.RecordObject(mat, "Configure Decal Slot");

            var sb = new StringBuilder();
            bool success;

            if (shaderName.Contains("poiyomi"))
                success = ConfigurePoiyomiDecal(mat, slotIndex, tex, position, scale, angle, sb);
            else if (shaderName.Contains("liltoon") || shaderName.Contains("lil/"))
                success = ConfigureLilToonDecal(mat, slotIndex, tex, position, scale, angle, sb);
            else
                return $"Error: Shader '{mat.shader.name}' is not Poiyomi or lilToon. Decal configuration is only supported for these shaders.";

            if (success)
            {
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            return sb.ToString().TrimEnd();
        }

        // ─── Internal Helpers ───

        private static Texture2D ResizeTextureInternal(Texture2D source, int newWidth, int newHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var resized = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }

        private static Color[] GetChannelPixels(Texture2D tex, int width, int height)
        {
            if (tex == null) return null;
            if (tex.width == width && tex.height == height) return tex.GetPixels();
            var resized = ResizeTextureInternal(tex, width, height);
            Color[] pixels = resized.GetPixels();
            UnityEngine.Object.DestroyImmediate(resized);
            return pixels;
        }

        private static bool[,] BuildPixelMask(Texture2D tex, Mesh mesh, List<int> islandIndexList)
        {
            var islands = UVIslandDetector.DetectIslands(mesh);
            var triangleFilter = new HashSet<int>();

            foreach (int islandIdx in islandIndexList)
            {
                if (islandIdx < 0 || islandIdx >= islands.Count) continue;
                foreach (int triIdx in islands[islandIdx].triangleIndices)
                    triangleFilter.Add(triIdx);
            }

            if (triangleFilter.Count == 0) return null;

            int width = tex.width;
            int height = tex.height;
            bool[,] mask = new bool[width, height];

            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            for (int tri = 0; tri < triangles.Length / 3; tri++)
            {
                if (!triangleFilter.Contains(tri)) continue;

                int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                Vector2 p0 = new Vector2(uvs[i0].x * width, uvs[i0].y * height);
                Vector2 p1 = new Vector2(uvs[i1].x * width, uvs[i1].y * height);
                Vector2 p2 = new Vector2(uvs[i2].x * width, uvs[i2].y * height);

                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, width - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, width - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, height - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, height - 1);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (mask[x, y]) continue;
                        Vector3 bary = ComputeBarycentric(new Vector2(x + 0.5f, y + 0.5f), p0, p1, p2);
                        if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                            mask[x, y] = true;
                    }
                }
            }

            return mask;
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
            if (Mathf.Abs(denom) < 1e-8f) return new Vector3(-1, -1, -1);
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }

        private static bool TryParseIslandIndices(string islandIndices, out List<int> indices, out string error)
        {
            indices = new List<int>();
            error = null;
            if (string.IsNullOrEmpty(islandIndices) || string.IsNullOrWhiteSpace(islandIndices)) return true;

            foreach (string part in islandIndices.Split(';', ','))
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (int.TryParse(trimmed, out int idx))
                {
                    if (idx < 0) { error = $"Error: Negative island index '{idx}'."; return false; }
                    indices.Add(idx);
                }
                else
                {
                    error = $"Error: Invalid island index '{trimmed}'.";
                    return false;
                }
            }
            return true;
        }

        private static bool TryParseColorValue(string value, out Color color)
        {
            color = Color.black;
            if (string.IsNullOrEmpty(value)) return false;

            if (value.StartsWith("#")) return ColorUtility.TryParseHtmlString(value, out color);

            string[] parts = value.Split(',');
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0].Trim(), out float r) &&
                    float.TryParse(parts[1].Trim(), out float g) &&
                    float.TryParse(parts[2].Trim(), out float b))
                {
                    float a = 1f;
                    if (parts.Length > 3) float.TryParse(parts[3].Trim(), out a);
                    color = new Color(r, g, b, a);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseVector2(string value, out Vector2 vec)
        {
            vec = Vector2.zero;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y))
            {
                vec = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        // ─── Shader-Specific Decal Setup ───

        private static bool ConfigurePoiyomiDecal(Material mat, int slotIndex, Texture tex, string position, string scale, float angle, StringBuilder sb)
        {
            if (slotIndex < 0 || slotIndex > 3)
            {
                sb.AppendLine($"Error: Poiyomi decal slot index must be 0-3, got {slotIndex}.");
                return false;
            }

            string suffix = slotIndex == 0 ? "" : slotIndex.ToString();
            string texProp = $"_DecalTexture{suffix}";
            string enableKw = $"_DECAL_{slotIndex}";

            mat.EnableKeyword(enableKw);
            sb.AppendLine($"Enabled keyword: {enableKw}");

            if (mat.HasTexture(texProp))
            {
                mat.SetTexture(texProp, tex);
                sb.AppendLine($"Set {texProp} = {tex.name}");
            }
            else
            {
                sb.AppendLine($"Warning: Property '{texProp}' not found. Shader variant may differ.");
            }

            if (!string.IsNullOrEmpty(position) && TryParseVector2(position, out Vector2 pos))
            {
                string posProp = $"_DecalPosition{suffix}";
                if (mat.HasProperty(posProp))
                {
                    mat.SetVector(posProp, new Vector4(pos.x, pos.y, 0, 0));
                    sb.AppendLine($"Set {posProp} = ({pos.x:F3}, {pos.y:F3})");
                }
            }

            if (!string.IsNullOrEmpty(scale) && TryParseVector2(scale, out Vector2 scl))
            {
                string sclProp = $"_DecalScale{suffix}";
                if (mat.HasProperty(sclProp))
                {
                    mat.SetVector(sclProp, new Vector4(scl.x, scl.y, 0, 0));
                    sb.AppendLine($"Set {sclProp} = ({scl.x:F3}, {scl.y:F3})");
                }
            }

            if (angle != 0f)
            {
                string angleProp = $"_DecalRotation{suffix}";
                if (mat.HasProperty(angleProp))
                {
                    mat.SetFloat(angleProp, angle);
                    sb.AppendLine($"Set {angleProp} = {angle:F1}");
                }
            }

            sb.Insert(0, $"Success: Configured Poiyomi decal slot {slotIndex} on '{mat.name}'.\n");
            return true;
        }

        private static bool ConfigureLilToonDecal(Material mat, int slotIndex, Texture tex, string position, string scale, float angle, StringBuilder sb)
        {
            if (slotIndex < 0 || slotIndex > 1)
            {
                sb.AppendLine($"Error: lilToon decal slot index must be 0-1, got {slotIndex}.");
                return false;
            }

            string texProp = slotIndex == 0 ? "_Main2ndTex" : "_Main3rdTex";
            string enableProp = slotIndex == 0 ? "_UseMain2ndTex" : "_UseMain3rdTex";

            if (mat.HasFloat(enableProp))
            {
                mat.SetFloat(enableProp, 1f);
                sb.AppendLine($"Set {enableProp} = 1");
            }

            if (mat.HasTexture(texProp))
            {
                mat.SetTexture(texProp, tex);
                sb.AppendLine($"Set {texProp} = {tex.name}");
            }
            else
            {
                sb.AppendLine($"Warning: Property '{texProp}' not found.");
            }

            if (!string.IsNullOrEmpty(position) && TryParseVector2(position, out Vector2 pos))
            {
                mat.SetTextureOffset(texProp, pos);
                sb.AppendLine($"Set {texProp} offset = ({pos.x:F3}, {pos.y:F3})");
            }

            if (!string.IsNullOrEmpty(scale) && TryParseVector2(scale, out Vector2 scl))
            {
                mat.SetTextureScale(texProp, scl);
                sb.AppendLine($"Set {texProp} scale = ({scl.x:F3}, {scl.y:F3})");
            }

            if (angle != 0f)
            {
                string angleProp = slotIndex == 0 ? "_Main2ndTexAngle" : "_Main3rdTexAngle";
                if (mat.HasProperty(angleProp))
                {
                    mat.SetFloat(angleProp, angle);
                    sb.AppendLine($"Set {angleProp} = {angle:F1}");
                }
            }

            sb.Insert(0, $"Success: Configured lilToon decal slot {slotIndex} on '{mat.name}'.\n");
            return true;
        }
    }
}
