using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Profiling;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class TextureMemoryAnalysisTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ─── Shared Helpers ───

        private static int GetBitsPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.Alpha8: return 8;
                case TextureFormat.R8: return 8;
                case TextureFormat.R16: return 16;
                case TextureFormat.RFloat: return 32;
                case TextureFormat.RHalf: return 16;
                case TextureFormat.RG16: return 16;
                case TextureFormat.RG32: return 32;
                case TextureFormat.RGFloat: return 64;
                case TextureFormat.RGHalf: return 32;
                case TextureFormat.RGB24: return 24;
                case TextureFormat.RGB48: return 48;
                case TextureFormat.RGB565: return 16;
                case TextureFormat.RGBA32: return 32;
                case TextureFormat.RGBA64: return 64;
                case TextureFormat.RGBAFloat: return 128;
                case TextureFormat.RGBAHalf: return 64;
                case TextureFormat.RGBA4444: return 16;
                case TextureFormat.ARGB32: return 32;
                case TextureFormat.ARGB4444: return 16;
                case TextureFormat.BGRA32: return 32;
                case TextureFormat.DXT1: return 4;
                case TextureFormat.DXT1Crunched: return 4;
                case TextureFormat.DXT5: return 8;
                case TextureFormat.DXT5Crunched: return 8;
                case TextureFormat.BC4: return 4;
                case TextureFormat.BC5: return 8;
                case TextureFormat.BC6H: return 8;
                case TextureFormat.BC7: return 8;
                case TextureFormat.ETC_RGB4: return 4;
                case TextureFormat.ETC2_RGB: return 4;
                case TextureFormat.ETC2_RGBA8: return 8;
                case TextureFormat.ASTC_4x4: return 8;
                case TextureFormat.ASTC_5x5: return 5;
                case TextureFormat.ASTC_6x6: return 4;
                case TextureFormat.ASTC_8x8: return 2;
                case TextureFormat.ASTC_10x10: return 1;
                case TextureFormat.ASTC_12x12: return 1;
                default: return 32;
            }
        }

        private static long EstimateVRAM(int width, int height, TextureFormat format, bool hasMipmaps)
        {
            int bpp = GetBitsPerPixel(format);
            long baseSize = (long)width * height * bpp / 8;
            if (hasMipmaps)
                baseSize = (long)(baseSize * 1.333f);
            return baseSize;
        }

        private static long EstimateVRAMForBpp(int width, int height, int bpp, bool hasMipmaps)
        {
            long baseSize = (long)width * height * bpp / 8;
            if (hasMipmaps)
                baseSize = (long)(baseSize * 1.333f);
            return baseSize;
        }

        private struct TextureInfo
        {
            public Texture2D texture;
            public string assetPath;
            public long vramBytes;
            public List<string> referencedBy;
        }

        private static List<TextureInfo> CollectAllTextures(GameObject avatarRoot)
        {
            var textureMap = new Dictionary<Texture2D, TextureInfo>();
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < propCount; i++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;

                        string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                        var tex = mat.GetTexture(propName) as Texture2D;
                        if (tex == null) continue;

                        if (!textureMap.ContainsKey(tex))
                        {
                            string path = AssetDatabase.GetAssetPath(tex);
                            textureMap[tex] = new TextureInfo
                            {
                                texture = tex,
                                assetPath = path,
                                vramBytes = Profiler.GetRuntimeMemorySizeLong(tex),
                                referencedBy = new List<string>()
                            };
                        }

                        var info = textureMap[tex];
                        string refName = $"{mat.name} ({propName})";
                        if (!info.referencedBy.Contains(refName))
                            info.referencedBy.Add(refName);
                        textureMap[tex] = info;
                    }
                }
            }

            return textureMap.Values.OrderByDescending(t => t.vramBytes).ToList();
        }

        // ─── Tools ───

        [AgentTool("Analyze VRAM usage of all textures referenced by an avatar. Shows per-texture breakdown sorted by VRAM, format, resolution, and which materials reference each texture. avatarRootName: name of the avatar root GameObject.")]
        public static string AnalyzeTextureMemory(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var textures = CollectAllTextures(go);
            if (textures.Count == 0) return $"No textures found on avatar '{avatarRootName}'.";

            long totalVRAM = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Texture Memory Analysis for '{avatarRootName}':");
            sb.AppendLine($"{"#",-3} {"Texture",-30} {"Size",-12} {"Format",-16} {"VRAM",-10} {"Mip",-4} References");
            sb.AppendLine(new string('-', 110));

            int idx = 1;
            foreach (var info in textures)
            {
                var tex = info.texture;
                totalVRAM += info.vramBytes;

                string size = $"{tex.width}x{tex.height}";
                string format = tex.format.ToString();
                string vram = FormatBytes(info.vramBytes);
                string mip = tex.mipmapCount > 1 ? "Yes" : "No";
                string refs = string.Join("; ", info.referencedBy.Take(3));
                if (info.referencedBy.Count > 3) refs += $" (+{info.referencedBy.Count - 3} more)";

                string texName = tex.name;
                if (texName.Length > 28) texName = texName.Substring(0, 25) + "...";

                sb.AppendLine($"{idx,-3} {texName,-30} {size,-12} {format,-16} {vram,-10} {mip,-4} {refs}");
                idx++;
            }

            sb.AppendLine(new string('-', 110));
            sb.AppendLine($"Total: {textures.Count} textures, {FormatBytes(totalVRAM)} VRAM");

            var formatGroups = textures.GroupBy(t => t.texture.format).OrderByDescending(g => g.Sum(t => t.vramBytes));
            sb.AppendLine("\nVRAM by Format:");
            foreach (var group in formatGroups)
            {
                long groupVram = group.Sum(t => t.vramBytes);
                sb.AppendLine($"  {group.Key,-20} {group.Count(),3} textures  {FormatBytes(groupVram),10}  ({groupVram * 100f / totalVRAM:F1}%)");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Compare VRAM usage of a specific texture across different compression formats. Shows what-if analysis for format changes. texturePath: asset path to the texture.")]
        public static string CompareTextureFormats(string texturePath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) return $"Error: Texture not found at '{texturePath}'.";

            int width = tex.width;
            int height = tex.height;
            bool hasMipmaps = tex.mipmapCount > 1;
            long currentVRAM = Profiler.GetRuntimeMemorySizeLong(tex);

            var sb = new StringBuilder();
            sb.AppendLine($"Format Comparison for '{tex.name}' ({width}x{height}, mipmaps={hasMipmaps}):");
            sb.AppendLine($"Current: {tex.format} ({FormatBytes(currentVRAM)})");
            sb.AppendLine();
            sb.AppendLine($"{"Format",-22} {"BPP",-5} {"VRAM",-12} {"vs Current",-12} Notes");
            sb.AppendLine(new string('-', 80));

            var formats = new (string name, int bpp, TextureFormat format, string notes)[]
            {
                ("RGBA32", 32, TextureFormat.RGBA32, "Uncompressed, highest quality"),
                ("RGB24", 24, TextureFormat.RGB24, "Uncompressed, no alpha"),
                ("DXT1 (BC1)", 4, TextureFormat.DXT1, "Good for opaque, lossy"),
                ("DXT5 (BC3)", 8, TextureFormat.DXT5, "Good with alpha, lossy"),
                ("BC7", 8, TextureFormat.BC7, "High quality, alpha support"),
                ("ASTC 4x4", 8, TextureFormat.ASTC_4x4, "Mobile, high quality"),
                ("ASTC 6x6", 4, TextureFormat.ASTC_6x6, "Mobile, balanced"),
                ("ASTC 8x8", 2, TextureFormat.ASTC_8x8, "Mobile, smaller"),
                ("ETC2 RGB", 4, TextureFormat.ETC2_RGB, "Mobile, no alpha"),
                ("ETC2 RGBA", 8, TextureFormat.ETC2_RGBA8, "Mobile, with alpha"),
            };

            foreach (var (name, bpp, format, notes) in formats)
            {
                long vram = EstimateVRAMForBpp(width, height, bpp, hasMipmaps);
                float ratio = currentVRAM > 0 ? vram / (float)currentVRAM : 0;
                string vs = currentVRAM > 0 ? $"{ratio:F2}x" : "N/A";
                string marker = format == tex.format ? " <-- current" : "";
                sb.AppendLine($"{name,-22} {bpp,-5} {FormatBytes(vram),-12} {vs,-12} {notes}{marker}");
            }

            sb.AppendLine();
            bool hasAlpha = tex.format == TextureFormat.RGBA32 || tex.format == TextureFormat.ARGB32 ||
                           tex.format == TextureFormat.DXT5 || tex.format == TextureFormat.BC7 ||
                           tex.format == TextureFormat.RGBA64 || tex.format == TextureFormat.RGBAHalf ||
                           tex.format == TextureFormat.RGBAFloat || tex.format == TextureFormat.ETC2_RGBA8 ||
                           tex.format == TextureFormat.ASTC_4x4;

            if (hasAlpha)
                sb.AppendLine("Recommendation: BC7 offers best quality with alpha. DXT5 for lower VRAM.");
            else
                sb.AppendLine("Recommendation: DXT1 (BC1) for best compression. BC7 for higher quality.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Get texture optimization recommendations for an avatar. Identifies large textures, uncompressed formats, unnecessary alpha, and atlas candidates. Shows potential VRAM savings. avatarRootName: name of the avatar root. targetVRAMMB: target VRAM budget in MB (0=no target, just list recommendations).")]
        public static string GetTextureOptimizationRecommendations(string avatarRootName, float targetVRAMMB = 0f)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var textures = CollectAllTextures(go);
            if (textures.Count == 0) return $"No textures found on avatar '{avatarRootName}'.";

            long totalVRAM = textures.Sum(t => t.vramBytes);
            var recommendations = new List<(string category, string texture, string detail, long savings)>();

            foreach (var info in textures)
            {
                var tex = info.texture;
                long currentVRAM = info.vramBytes;

                // 1. Large uncompressed textures
                if (IsUncompressed(tex.format) && tex.width >= 512)
                {
                    long bc7VRAM = EstimateVRAM(tex.width, tex.height, TextureFormat.BC7, tex.mipmapCount > 1);
                    long savings = currentVRAM - bc7VRAM;
                    if (savings > 0)
                    {
                        recommendations.Add(("Uncompressed", tex.name,
                            $"{tex.format} -> BC7: {FormatBytes(currentVRAM)} -> {FormatBytes(bc7VRAM)}",
                            savings));
                    }
                }

                // 2. Oversized textures (>2048)
                if (tex.width > 2048 || tex.height > 2048)
                {
                    int newSize = 2048;
                    long resizedVRAM = EstimateVRAM(newSize, newSize, tex.format, tex.mipmapCount > 1);
                    long savings = currentVRAM - resizedVRAM;
                    if (savings > 0)
                    {
                        recommendations.Add(("Oversized", tex.name,
                            $"{tex.width}x{tex.height} -> {newSize}x{newSize}: save {FormatBytes(savings)}",
                            savings));
                    }
                }

                // 3. Textures without mipmaps
                if (tex.mipmapCount <= 1 && tex.width >= 256)
                {
                    recommendations.Add(("No Mipmaps", tex.name,
                        $"{tex.width}x{tex.height}: consider enabling mipmaps for rendering quality",
                        0));
                }
            }

            // Find atlas candidates
            var materialTextures = new Dictionary<string, List<TextureInfo>>();
            foreach (var info in textures)
            {
                foreach (var refName in info.referencedBy)
                {
                    string matName = refName.Split('(')[0].Trim();
                    if (!materialTextures.ContainsKey(matName))
                        materialTextures[matName] = new List<TextureInfo>();
                    materialTextures[matName].Add(info);
                }
            }

            foreach (var kvp in materialTextures)
            {
                var smallTextures = kvp.Value.Where(t => t.texture.width <= 512 && t.texture.height <= 512).ToList();
                if (smallTextures.Count >= 3)
                {
                    recommendations.Add(("Atlas Candidate", kvp.Key,
                        $"{smallTextures.Count} small textures on material — consider texture atlas",
                        0));
                }
            }

            recommendations.Sort((a, b) => b.savings.CompareTo(a.savings));

            var sb = new StringBuilder();
            sb.AppendLine($"Texture Optimization for '{avatarRootName}':");
            sb.AppendLine($"Current: {textures.Count} textures, {FormatBytes(totalVRAM)} total VRAM");

            if (targetVRAMMB > 0)
            {
                long targetBytes = (long)(targetVRAMMB * 1024 * 1024);
                long excess = totalVRAM - targetBytes;
                if (excess > 0)
                    sb.AppendLine($"Target: {targetVRAMMB:F0} MB — need to save {FormatBytes(excess)}");
                else
                    sb.AppendLine($"Target: {targetVRAMMB:F0} MB — ALREADY WITHIN TARGET");
            }

            sb.AppendLine();

            if (recommendations.Count == 0)
            {
                sb.AppendLine("No optimization opportunities found. Textures appear well-optimized.");
                return sb.ToString().TrimEnd();
            }

            long totalSavings = recommendations.Sum(r => r.savings);
            sb.AppendLine($"Recommendations ({recommendations.Count}), potential savings: {FormatBytes(totalSavings)}");
            sb.AppendLine();

            string currentCategory = "";
            foreach (var (category, texName, detail, savings) in recommendations)
            {
                if (category != currentCategory)
                {
                    currentCategory = category;
                    sb.AppendLine($"[{category}]");
                }
                string savingsStr = savings > 0 ? $" (save {FormatBytes(savings)})" : "";
                sb.AppendLine($"  {texName}: {detail}{savingsStr}");
            }

            if (targetVRAMMB > 0)
            {
                long targetBytes = (long)(targetVRAMMB * 1024 * 1024);
                long afterOpt = totalVRAM - totalSavings;
                sb.AppendLine();
                sb.AppendLine($"After all optimizations: ~{FormatBytes(afterOpt)} ({(afterOpt <= targetBytes ? "WITHIN" : "EXCEEDS")} target)");
            }

            return sb.ToString().TrimEnd();
        }

        // ─── Helpers ───

        private static bool IsUncompressed(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGB24:
                case TextureFormat.RGBA64:
                case TextureFormat.RGBAFloat:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGB48:
                case TextureFormat.R8:
                case TextureFormat.R16:
                case TextureFormat.RFloat:
                case TextureFormat.RHalf:
                case TextureFormat.RG16:
                case TextureFormat.RG32:
                case TextureFormat.RGFloat:
                case TextureFormat.RGHalf:
                    return true;
                default:
                    return false;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F2} MB";
        }
    }
}
