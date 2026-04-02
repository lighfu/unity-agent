using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor.Atlas
{
    /// <summary>
    /// Texture preprocessing to improve AAO atlas build results.
    /// </summary>
    internal static class AtlasPreprocessor
    {
        private static string GeneratedDir => PackagePaths.GetGeneratedDir("Textures");

        /// <summary>
        /// Bake lilToon _Color into the main texture for a material slot.
        /// Resets _Color to white so AAO's atlas merging produces correct colors.
        /// Returns status message.
        /// </summary>
        internal static string BakeLilToonColor(Renderer renderer, int materialIndex)
        {
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return $"Error: Material index {materialIndex} out of range.";

            var mat = materials[materialIndex];
            if (mat == null)
                return $"Error: Material at index {materialIndex} is null.";

            if (!mat.HasProperty("_Color"))
                return $"Skip: Material '{mat.name}' has no _Color property.";

            Color color = mat.GetColor("_Color");
            if (color.r > 0.95f && color.g > 0.95f && color.b > 0.95f)
                return $"Skip: Material '{mat.name}' _Color is already white.";

            Texture2D mainTex = null;
            if (mat.HasProperty("_MainTex"))
                mainTex = mat.GetTexture("_MainTex") as Texture2D;

            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName)) avatarName = "Unknown";

            // If shared with other renderers, duplicate the material first
            mat = EnsureUniqueMaterial(renderer, materialIndex, avatarName);

            Undo.RecordObject(mat, "Bake _Color for Atlas");

            if (mainTex == null)
            {
                // No texture — create 1x1 solid color texture
                var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTex.SetPixel(0, 0, color);
                solidTex.Apply();

                string path = SaveAtlasTexture(solidTex, avatarName, mat.name + "_solid");
                Object.DestroyImmediate(solidTex);

                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                mat.SetTexture("_MainTex", saved);
            }
            else
            {
                // Bake color into existing texture
                var editable = TextureUtility.CreateEditableTexture(mainTex);
                if (editable == null)
                    return $"Error: Failed to create editable copy of '{mainTex.name}'.";

                Color[] pixels = editable.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color(
                        pixels[i].r * color.r,
                        pixels[i].g * color.g,
                        pixels[i].b * color.b,
                        pixels[i].a);
                }
                editable.SetPixels(pixels);
                editable.Apply();

                string path = SaveAtlasTexture(editable, avatarName, mainTex.name + "_baked");
                Object.DestroyImmediate(editable);

                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                mat.SetTexture("_MainTex", saved);
            }

            mat.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(mat);

            return $"OK: Baked _Color ({color.r:F2},{color.g:F2},{color.b:F2}) into '{mat.name}'.";
        }

        /// <summary>
        /// Resize textures of selected material slots to a uniform size.
        /// Returns status message.
        /// </summary>
        internal static string ResizeTextures(Renderer renderer, List<int> materialIndices, int targetSize)
        {
            if (targetSize <= 0)
                return "Error: targetSize must be > 0.";

            var materials = renderer.sharedMaterials;
            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName)) avatarName = "Unknown";

            var sb = new StringBuilder();
            int count = 0;

            foreach (int idx in materialIndices)
            {
                if (idx < 0 || idx >= materials.Length) continue;
                var mat = materials[idx];
                if (mat == null) continue;

                mat = EnsureUniqueMaterial(renderer, idx, avatarName);
                Undo.RecordObject(mat, "Resize Textures for Atlas");

                string[] texProps = { "_MainTex", "_BumpMap", "_EmissionMap" };
                foreach (var prop in texProps)
                {
                    if (!mat.HasProperty(prop)) continue;
                    var tex = mat.GetTexture(prop) as Texture2D;
                    if (tex == null) continue;
                    if (tex.width == targetSize && tex.height == targetSize) continue;

                    var editable = TextureUtility.CreateEditableTexture(tex);
                    if (editable == null) continue;

                    var resized = ResizeTextureInternal(editable, targetSize, targetSize);
                    Object.DestroyImmediate(editable);

                    string suffix = prop == "_MainTex" ? "" : prop.TrimStart('_');
                    string path = SaveAtlasTexture(resized, avatarName,
                        tex.name + $"_{targetSize}" + (suffix.Length > 0 ? "_" + suffix : ""));
                    Object.DestroyImmediate(resized);

                    var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                    // Preserve normal map import settings
                    if (prop == "_BumpMap")
                    {
                        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (importer != null)
                        {
                            importer.textureType = TextureImporterType.NormalMap;
                            importer.SaveAndReimport();
                            saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        }
                    }

                    mat.SetTexture(prop, saved);
                    count++;
                }

                EditorUtility.SetDirty(mat);
            }

            sb.AppendLine($"OK: Resized {count} texture(s) to {targetSize}x{targetSize}.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Ensure a material slot has a main texture. Creates a 1x1 solid color texture if missing.
        /// Returns status message.
        /// </summary>
        internal static string EnsureTextures(Renderer renderer, int materialIndex)
        {
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return $"Error: Material index {materialIndex} out of range.";

            var mat = materials[materialIndex];
            if (mat == null)
                return $"Error: Material at index {materialIndex} is null.";

            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                return $"Skip: Material '{mat.name}' already has a main texture.";

            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName)) avatarName = "Unknown";

            mat = EnsureUniqueMaterial(renderer, materialIndex, avatarName);
            Undo.RecordObject(mat, "Ensure Texture for Atlas");

            Color fillColor = Color.white;
            if (mat.HasProperty("_Color"))
                fillColor = mat.GetColor("_Color");

            var solidTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fillColor;
            solidTex.SetPixels(pixels);
            solidTex.Apply();

            string path = SaveAtlasTexture(solidTex, avatarName, mat.name + "_placeholder");
            Object.DestroyImmediate(solidTex);

            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", saved);

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);

            EditorUtility.SetDirty(mat);

            return $"OK: Created placeholder texture for '{mat.name}'.";
        }

        // ─── Internal Helpers ───

        /// <summary>
        /// If the material is shared with other renderers, duplicate it.
        /// Returns the (possibly new) material.
        /// </summary>
        private static Material EnsureUniqueMaterial(Renderer renderer, int materialIndex, string avatarName)
        {
            var mat = renderer.sharedMaterials[materialIndex];

            // Check if shared
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            bool isShared = false;
            foreach (var other in allRenderers)
            {
                if (other == renderer) continue;
                foreach (var m in other.sharedMaterials)
                {
                    if (m == mat) { isShared = true; break; }
                }
                if (isShared) break;
            }

            if (!isShared) return mat;

            // Duplicate
            var newMat = new Material(mat);
            newMat.name = mat.name + "_AtlasCopy";
            string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
            newMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            var mats = renderer.sharedMaterials;
            mats[materialIndex] = newMat;
            renderer.sharedMaterials = mats;

            return newMat;
        }

        private static string SaveAtlasTexture(Texture2D tex, string avatarName, string baseName)
        {
            byte[] bytes = tex.EncodeToPNG();
            string folderPath = Path.Combine(GeneratedDir, avatarName).Replace("\\", "/");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string fileName = baseName + ".png";
            string fullPath = Path.Combine(folderPath, fileName).Replace("\\", "/");
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(fullPath);

            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return fullPath;
        }

        private static Texture2D ResizeTextureInternal(Texture2D source, int newWidth, int newHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
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
    }
}
