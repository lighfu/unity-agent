using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Pure pixel computation helpers extracted from TextureEditTools.
    /// All methods are side-effect free: they take a baseline Color[] and return a new Color[].
    /// Shared by TextureEditTools (disk commit path) and MeshPaintPreviewSession (live preview path)
    /// so the two paths can never diverge in pixel behavior.
    /// </summary>
    public static class TextureEditCore
    {
        // ───── Gradient ─────

        public static Color[] ComputeGradient(
            Color[] baseline, int width, int height,
            Mesh mesh,
            List<int> targetIslandIndices,
            List<UVIsland> allIslands,
            int[] islandGroups,
            Color fromColor, Color toColor,
            int axis, bool invert, string blendMode,
            float startT, float endT)
        {
            var pixels = new Color[baseline.Length];
            System.Array.Copy(baseline, pixels, baseline.Length);

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            // Compute 3D bounds per island group (only for target islands)
            var groupBounds = new Dictionary<int, Vector2>();
            foreach (int islandIdx in targetIslandIndices)
            {
                if (islandIdx < 0 || islandIdx >= allIslands.Count) continue;
                int groupId = islandGroups != null && islandIdx < islandGroups.Length ? islandGroups[islandIdx] : islandIdx;
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

            float invEndMinusStart = (endT - startT) > 0.0001f ? 1f / (endT - startT) : 0f;

            foreach (int islandIdx in targetIslandIndices)
            {
                if (islandIdx < 0 || islandIdx >= allIslands.Count) continue;
                var island = allIslands[islandIdx];
                int groupId = islandGroups != null && islandIdx < islandGroups.Length ? islandGroups[islandIdx] : islandIdx;
                if (!groupBounds.ContainsKey(groupId)) continue;

                float minVal = groupBounds[groupId].x;
                float maxVal = groupBounds[groupId].y;
                float range = maxVal - minVal;
                if (range < 0.0001f) continue;
                float invRange = 1f / range;

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

                            float remappedT = Mathf.Clamp01((t - startT) * invEndMinusStart);

                            Color gradColor = Color.Lerp(fromColor, toColor, remappedT);
                            float strength = gradColor.a;
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
                                default:
                                    blended = gradColor;
                                    break;
                            }

                            Color final = Color.Lerp(original, blended, strength);
                            final.a = original.a;
                            pixels[pixelIdx] = final;
                        }
                    }
                }
            }

            return pixels;
        }

        // ───── HSV ─────

        public static Color[] ComputeHSV(
            Color[] baseline, int width, int height,
            Mesh mesh,
            List<int> targetIslandIndices,
            List<UVIsland> allIslands,
            float hueShift, float satScale, float valScale)
        {
            var pixels = new Color[baseline.Length];
            System.Array.Copy(baseline, pixels, baseline.Length);

            float hueShiftNormalized = hueShift / 360f;
            bool[] mask = null;
            if (targetIslandIndices != null && targetIslandIndices.Count > 0)
            {
                var triFilter = BuildTriangleFilter(targetIslandIndices, allIslands);
                if (triFilter != null)
                    mask = BuildPixelMask(width, height, mesh, triFilter);
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (mask != null && !mask[i]) continue;

                Color c = pixels[i];
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = (h + hueShiftNormalized) % 1f;
                if (h < 0f) h += 1f;
                s = Mathf.Clamp01(s * satScale);
                v = Mathf.Clamp01(v * valScale);
                Color adjusted = Color.HSVToRGB(h, s, v);
                adjusted.a = c.a;
                pixels[i] = adjusted;
            }

            return pixels;
        }

        // ───── Brightness / Contrast ─────

        public static Color[] ComputeBrightnessContrast(
            Color[] baseline, int width, int height,
            Mesh mesh,
            List<int> targetIslandIndices,
            List<UVIsland> allIslands,
            float brightness, float contrast)
        {
            var pixels = new Color[baseline.Length];
            System.Array.Copy(baseline, pixels, baseline.Length);

            // Match TextureEditTools.AdjustBrightnessContrast contrastFactor mapping
            float contrastFactor;
            if (contrast > 0f)
                contrastFactor = 1f + contrast * 2f;
            else
                contrastFactor = Mathf.Max(0f, 1f + contrast);

            bool[] mask = null;
            if (targetIslandIndices != null && targetIslandIndices.Count > 0)
            {
                var triFilter = BuildTriangleFilter(targetIslandIndices, allIslands);
                if (triFilter != null)
                    mask = BuildPixelMask(width, height, mesh, triFilter);
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (mask != null && !mask[i]) continue;
                pixels[i] = ApplyBC(pixels[i], brightness, contrastFactor);
            }

            return pixels;
        }

        // ───── Shared helpers ─────

        public static Color ApplyBC(Color c, float brightness, float contrastFactor)
        {
            float r = (c.r - 0.5f) * contrastFactor + 0.5f + brightness;
            float g = (c.g - 0.5f) * contrastFactor + 0.5f + brightness;
            float b = (c.b - 0.5f) * contrastFactor + 0.5f + brightness;
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), c.a);
        }

        public static float GetAxisValue(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return v.x;
                case 1: return v.y;
                case 2: return v.z;
                default: return v.y;
            }
        }

        public static Vector3 ComputeBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-8f)
                return new Vector3(-1, -1, -1);
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;
            return new Vector3(u, v, w);
        }

        public static HashSet<int> BuildTriangleFilter(List<int> islandIndexList, List<UVIsland> islands)
        {
            if (islandIndexList == null || islandIndexList.Count == 0 || islands == null)
                return null;

            var filter = new HashSet<int>();
            foreach (int islandIdx in islandIndexList)
            {
                if (islandIdx < 0 || islandIdx >= islands.Count) continue;
                foreach (int triIdx in islands[islandIdx].triangleIndices)
                    filter.Add(triIdx);
            }
            return filter.Count > 0 ? filter : null;
        }

        public static bool[] BuildPixelMask(int width, int height, Mesh mesh, HashSet<int> triangleFilter)
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
