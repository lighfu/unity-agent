using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Atlas
{
    internal static class AtlasAnalyzer
    {
        /// <summary>
        /// Analyze all material slots on a Renderer, collecting texture info per slot.
        /// </summary>
        internal static List<MaterialSlotInfo> Analyze(Renderer renderer)
        {
            var results = new List<MaterialSlotInfo>();
            var materials = renderer.sharedMaterials;
            var mesh = GetMesh(renderer);

            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat == null) continue;

                var info = new MaterialSlotInfo
                {
                    MaterialIndex = i,
                    Material = mat,
                    MaterialName = mat.name,
                    ShaderName = mat.shader != null ? mat.shader.name : "Unknown",
                    IsLilToon = mat.shader != null && mat.shader.name.StartsWith("lil/"),
                    MainColor = Color.white,
                    SubmeshIndex = i < (mesh != null ? mesh.subMeshCount : 0) ? i : -1,
                };

                // Main texture
                if (mat.HasProperty("_MainTex"))
                    info.MainTex = mat.GetTexture("_MainTex") as Texture2D;

                // Normal map
                if (mat.HasProperty("_BumpMap"))
                    info.NormalMap = mat.GetTexture("_BumpMap") as Texture2D;

                // Emission map
                if (mat.HasProperty("_EmissionMap"))
                    info.EmissionMap = mat.GetTexture("_EmissionMap") as Texture2D;

                // Main color (_Color for lilToon and Standard)
                if (mat.HasProperty("_Color"))
                    info.MainColor = mat.GetColor("_Color");

                // Determine max texture size across all slots
                int maxW = 0, maxH = 0;
                UpdateMaxSize(info.MainTex, ref maxW, ref maxH);
                UpdateMaxSize(info.NormalMap, ref maxW, ref maxH);
                UpdateMaxSize(info.EmissionMap, ref maxW, ref maxH);

                // If no textures at all, default to a small size
                info.TextureWidth = maxW > 0 ? maxW : 64;
                info.TextureHeight = maxH > 0 ? maxH : 64;

                // Triangle count for submesh
                if (mesh != null && info.SubmeshIndex >= 0 && info.SubmeshIndex < mesh.subMeshCount)
                {
                    var subMesh = mesh.GetSubMesh(info.SubmeshIndex);
                    info.TriangleCount = subMesh.indexCount / 3;
                }

                results.Add(info);
            }

            return results;
        }

        /// <summary>
        /// Group materials by shader family. Only same-shader materials can be atlased together.
        /// Returns shader family name → list of material indices.
        /// </summary>
        internal static Dictionary<string, List<int>> GroupByShaderFamily(List<MaterialSlotInfo> materials)
        {
            var groups = new Dictionary<string, List<int>>();
            foreach (var info in materials)
            {
                string family = GetShaderFamily(info.ShaderName);
                if (!groups.ContainsKey(family))
                    groups[family] = new List<int>();
                groups[family].Add(info.MaterialIndex);
            }
            return groups;
        }

        /// <summary>
        /// Check UV ranges for each submesh. Marks HasUVOutOfRange if any vertex UV is outside [0,1].
        /// </summary>
        internal static void CheckUVRanges(Mesh mesh, List<MaterialSlotInfo> materials)
        {
            if (mesh == null) return;

            var uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0) return;

            var triangles = mesh.triangles;

            foreach (var info in materials)
            {
                if (info.SubmeshIndex < 0 || info.SubmeshIndex >= mesh.subMeshCount)
                    continue;

                var subMesh = mesh.GetSubMesh(info.SubmeshIndex);
                int start = subMesh.indexStart;
                int end = start + subMesh.indexCount;

                bool outOfRange = false;
                for (int idx = start; idx < end && !outOfRange; idx++)
                {
                    int vi = triangles[idx];
                    if (vi < uvs.Length)
                    {
                        var uv = uvs[vi];
                        if (uv.x < -0.01f || uv.x > 1.01f || uv.y < -0.01f || uv.y > 1.01f)
                            outOfRange = true;
                    }
                }

                info.HasUVOutOfRange = outOfRange;
            }
        }

        /// <summary>
        /// Check if any material is shared with other Renderers in the scene.
        /// </summary>
        internal static void CheckSharedMaterials(Renderer renderer, List<MaterialSlotInfo> materials)
        {
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var matSet = new HashSet<Material>();
            foreach (var info in materials)
            {
                if (info.Material != null)
                    matSet.Add(info.Material);
            }

            var sharedMats = new HashSet<Material>();
            foreach (var other in allRenderers)
            {
                if (other == renderer) continue;
                foreach (var mat in other.sharedMaterials)
                {
                    if (mat != null && matSet.Contains(mat))
                        sharedMats.Add(mat);
                }
            }

            foreach (var info in materials)
            {
                if (info.Material != null && sharedMats.Contains(info.Material))
                    info.IsSharedAcrossRenderers = true;
            }
        }

        /// <summary>
        /// Estimate total VRAM used by textures on the given materials.
        /// </summary>
        internal static long EstimateTextureMemory(List<MaterialSlotInfo> materials)
        {
            var counted = new HashSet<Texture2D>();
            long total = 0;

            foreach (var info in materials)
            {
                total += AddTexVRAM(info.MainTex, counted);
                total += AddTexVRAM(info.NormalMap, counted);
                total += AddTexVRAM(info.EmissionMap, counted);
            }

            return total;
        }

        // ─── Helpers ───

        private static string GetShaderFamily(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return "Unknown";

            // lilToon family
            if (shaderName.StartsWith("lil/")) return "lilToon";

            // Standard family
            if (shaderName.StartsWith("Standard")) return "Standard";

            // UTS (Unity Toon Shader)
            if (shaderName.Contains("UnityChanToonShader") || shaderName.Contains("UTS"))
                return "UTS";

            // Poiyomi
            if (shaderName.Contains("Poiyomi") || shaderName.Contains(".poyi"))
                return "Poiyomi";

            return shaderName;
        }

        private static void UpdateMaxSize(Texture2D tex, ref int maxW, ref int maxH)
        {
            if (tex == null) return;
            if (tex.width > maxW) maxW = tex.width;
            if (tex.height > maxH) maxH = tex.height;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
                return smr.sharedMesh;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static long AddTexVRAM(Texture2D tex, HashSet<Texture2D> counted)
        {
            if (tex == null || counted.Contains(tex)) return 0;
            counted.Add(tex);
            long size = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
            if (size <= 0)
            {
                // Fallback estimate
                size = (long)tex.width * tex.height * 4; // assume RGBA32
                if (tex.mipmapCount > 1)
                    size = (long)(size * 1.333f);
            }
            return size;
        }
    }
}
