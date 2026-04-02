using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class UVValidationTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ─── Shared Helpers ───

        private static int[,] RasterizeUVTriangles(Mesh mesh, int resolution, out int[,] hitCount)
        {
            int[,] grid = new int[resolution, resolution];
            hitCount = new int[resolution, resolution];

            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    grid[x, y] = -1;

            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            var islands = UVIslandDetector.DetectIslands(mesh);

            int[] triToIsland = new int[triangles.Length / 3];
            for (int isl = 0; isl < islands.Count; isl++)
                foreach (int triIdx in islands[isl].triangleIndices)
                    triToIsland[triIdx] = isl;

            for (int tri = 0; tri < triangles.Length / 3; tri++)
            {
                int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                Vector2 p0 = new Vector2(uv0.x * resolution, uv0.y * resolution);
                Vector2 p1 = new Vector2(uv1.x * resolution, uv1.y * resolution);
                Vector2 p2 = new Vector2(uv2.x * resolution, uv2.y * resolution);

                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, resolution - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, resolution - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, resolution - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, resolution - 1);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector3 bary = ComputeBarycentric(new Vector2(x + 0.5f, y + 0.5f), p0, p1, p2);
                        if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                        {
                            hitCount[x, y]++;
                            grid[x, y] = triToIsland[tri];
                        }
                    }
                }
            }

            return grid;
        }

        private static float ComputeUVTriangleArea(Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Vector2 e1 = uv1 - uv0;
            Vector2 e2 = uv2 - uv0;
            return Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f;
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

        // ─── Tools ───

        [AgentTool("Check for UV overlaps on a mesh. UV overlaps cause paint operations to affect unintended areas, making them the primary cause of paint corruption. gameObjectName: hierarchy path. resolution: rasterization grid size (higher=more accurate but slower, default 512).")]
        public static string CheckUVOverlaps(string gameObjectName, int resolution = 512)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0) return $"Error: Mesh on '{gameObjectName}' has no UV coordinates.";

            resolution = Mathf.Clamp(resolution, 64, 2048);

            int[,] hitCount;
            RasterizeUVTriangles(mesh, resolution, out hitCount);

            int totalPixels = 0;
            int overlapPixels = 0;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    if (hitCount[x, y] > 0) totalPixels++;
                    if (hitCount[x, y] > 1) overlapPixels++;
                }
            }

            // Detect inter-island overlap pairs
            var islands = UVIslandDetector.DetectIslands(mesh);
            int[] triangles = mesh.triangles;

            int[] triToIsland = new int[triangles.Length / 3];
            for (int isl = 0; isl < islands.Count; isl++)
                foreach (int triIdx in islands[isl].triangleIndices)
                    triToIsland[triIdx] = isl;

            var islandHitMap = new Dictionary<int, HashSet<int>>();

            for (int tri = 0; tri < triangles.Length / 3; tri++)
            {
                int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                Vector2 p0 = new Vector2(uvs[i0].x * resolution, uvs[i0].y * resolution);
                Vector2 p1 = new Vector2(uvs[i1].x * resolution, uvs[i1].y * resolution);
                Vector2 p2 = new Vector2(uvs[i2].x * resolution, uvs[i2].y * resolution);

                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, resolution - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, resolution - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, resolution - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, resolution - 1);

                int islId = triToIsland[tri];
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (hitCount[x, y] <= 1) continue;
                        Vector3 bary = ComputeBarycentric(new Vector2(x + 0.5f, y + 0.5f), p0, p1, p2);
                        if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                        {
                            int key = y * resolution + x;
                            if (!islandHitMap.ContainsKey(key))
                                islandHitMap[key] = new HashSet<int>();
                            islandHitMap[key].Add(islId);
                        }
                    }
                }
            }

            var overlapIslandPairs = new HashSet<string>();
            foreach (var kvp in islandHitMap)
            {
                var islIds = kvp.Value.ToList();
                for (int i = 0; i < islIds.Count; i++)
                    for (int j = i + 1; j < islIds.Count; j++)
                    {
                        int a = Mathf.Min(islIds[i], islIds[j]);
                        int b = Mathf.Max(islIds[i], islIds[j]);
                        overlapIslandPairs.Add($"{a}-{b}");
                    }
            }

            float overlapPercent = totalPixels > 0 ? overlapPixels * 100f / totalPixels : 0f;

            var sb = new StringBuilder();
            sb.AppendLine($"UV Overlap Check for '{gameObjectName}' (resolution={resolution}):");
            sb.AppendLine($"  Total UV-covered pixels: {totalPixels}");
            sb.AppendLine($"  Overlap pixels: {overlapPixels} ({overlapPercent:F2}%)");
            sb.AppendLine($"  Islands: {islands.Count}");

            if (overlapIslandPairs.Count > 0)
            {
                sb.AppendLine($"  Inter-island overlaps: {overlapIslandPairs.Count} pairs");
                foreach (var pair in overlapIslandPairs.Take(10))
                    sb.AppendLine($"    Islands {pair}");
                if (overlapIslandPairs.Count > 10)
                    sb.AppendLine($"    ... and {overlapIslandPairs.Count - 10} more pairs");
            }

            if (overlapPixels == 0)
                sb.AppendLine("  Result: [PASS] No UV overlaps detected.");
            else if (overlapPercent < 1f)
                sb.AppendLine("  Result: [WARN] Minor UV overlaps. Paint operations may have minor artifacts at overlap boundaries.");
            else
                sb.AppendLine("  Result: [FAIL] Significant UV overlaps. Paint operations will affect overlapping areas simultaneously.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Analyze UV space utilization and texel density. Shows how efficiently the UV space is used and identifies areas with high/low texel density. gameObjectName: hierarchy path. textureResolution: the texture resolution to compute texel density against (default 1024).")]
        public static string AnalyzeUVUtilization(string gameObjectName, int textureResolution = 1024)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (uvs == null || uvs.Length == 0) return $"Error: Mesh on '{gameObjectName}' has no UV coordinates.";

            int triCount = triangles.Length / 3;
            float totalUVArea = 0f;
            float totalWorldArea = 0f;
            var texelDensities = new List<float>();

            for (int tri = 0; tri < triCount; tri++)
            {
                int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                float uvArea = ComputeUVTriangleArea(uvs[i0], uvs[i1], uvs[i2]);
                totalUVArea += uvArea;

                Vector3 e1 = vertices[i1] - vertices[i0];
                Vector3 e2 = vertices[i2] - vertices[i0];
                float worldArea = Vector3.Cross(e1, e2).magnitude * 0.5f;
                totalWorldArea += worldArea;

                if (worldArea > 1e-8f && uvArea > 1e-10f)
                {
                    float texelArea = uvArea * textureResolution * textureResolution;
                    float density = Mathf.Sqrt(texelArea / worldArea);
                    texelDensities.Add(density);
                }
            }

            int rasterRes = Mathf.Min(textureResolution, 1024);
            int[,] hitCount;
            RasterizeUVTriangles(mesh, rasterRes, out hitCount);

            int coveredPixels = 0;
            int totalRasterPixels = rasterRes * rasterRes;
            for (int y = 0; y < rasterRes; y++)
                for (int x = 0; x < rasterRes; x++)
                    if (hitCount[x, y] > 0) coveredPixels++;

            float coveragePercent = coveredPixels * 100f / totalRasterPixels;
            float uvUtilization = Mathf.Clamp01(totalUVArea) * 100f;

            texelDensities.Sort();
            float minDensity = texelDensities.Count > 0 ? texelDensities[0] : 0f;
            float maxDensity = texelDensities.Count > 0 ? texelDensities[texelDensities.Count - 1] : 0f;
            float medianDensity = texelDensities.Count > 0 ? texelDensities[texelDensities.Count / 2] : 0f;
            float avgDensity = texelDensities.Count > 0 ? texelDensities.Average() : 0f;
            float densityRatio = minDensity > 0 ? maxDensity / minDensity : float.PositiveInfinity;

            var sb = new StringBuilder();
            sb.AppendLine($"UV Utilization Analysis for '{gameObjectName}':");
            sb.AppendLine($"  Texture resolution: {textureResolution}x{textureResolution}");
            sb.AppendLine($"  Triangles: {triCount}");
            sb.AppendLine();
            sb.AppendLine("  UV Coverage:");
            sb.AppendLine($"    UV area utilization: {uvUtilization:F1}%");
            sb.AppendLine($"    Pixel coverage: {coveragePercent:F1}% (rasterized at {rasterRes}x{rasterRes})");
            sb.AppendLine($"    Wasted space: {100f - coveragePercent:F1}%");
            sb.AppendLine();
            sb.AppendLine($"  Texel Density (pixels/world unit at {textureResolution}px):");
            sb.AppendLine($"    Min:    {minDensity:F1}");
            sb.AppendLine($"    Max:    {maxDensity:F1}");
            sb.AppendLine($"    Median: {medianDensity:F1}");
            sb.AppendLine($"    Avg:    {avgDensity:F1}");
            sb.AppendLine($"    Ratio (max/min): {densityRatio:F1}x");

            if (densityRatio > 10f)
                sb.AppendLine("    [WARN] High texel density variation. Some areas may appear blurry while others are sharp.");
            else if (densityRatio > 4f)
                sb.AppendLine("    [INFO] Moderate texel density variation.");
            else
                sb.AppendLine("    [GOOD] Texel density is fairly uniform.");

            if (coveragePercent < 50f)
                sb.AppendLine($"\n  [WARN] Low UV coverage ({coveragePercent:F0}%). Consider repacking UVs for better texture space usage.");
            else if (coveragePercent > 80f)
                sb.AppendLine($"\n  [GOOD] UV coverage is efficient ({coveragePercent:F0}%).");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Comprehensive UV paint readiness check. Validates overlaps, out-of-range UVs, padding, degenerate triangles, and coverage. Returns PASS/WARN/FAIL for each check. gameObjectName: hierarchy path.")]
        public static string CheckUVPaintReadiness(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            Mesh mesh = ToolUtility.GetMesh(go);
            if (mesh == null) return $"Error: No mesh found on '{gameObjectName}'.";

            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (uvs == null || uvs.Length == 0) return $"Error: Mesh on '{gameObjectName}' has no UV coordinates.";

            int triCount = triangles.Length / 3;
            var sb = new StringBuilder();
            sb.AppendLine($"UV Paint Readiness Check for '{gameObjectName}':");
            sb.AppendLine($"  Mesh: {triCount} triangles, {uvs.Length} UV vertices");
            sb.AppendLine();

            int passCount = 0, warnCount = 0, failCount = 0;

            // Check 1: UV Overlaps
            int resolution = 512;
            int[,] hitCount;
            RasterizeUVTriangles(mesh, resolution, out hitCount);
            int overlapPixels = 0, coveredPixels = 0;
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    if (hitCount[x, y] > 0) coveredPixels++;
                    if (hitCount[x, y] > 1) overlapPixels++;
                }
            float overlapPercent = coveredPixels > 0 ? overlapPixels * 100f / coveredPixels : 0;

            if (overlapPercent < 0.1f) { sb.AppendLine($"  [PASS] Overlaps: None detected ({overlapPercent:F2}%)"); passCount++; }
            else if (overlapPercent < 2f) { sb.AppendLine($"  [WARN] Overlaps: Minor ({overlapPercent:F2}%)"); warnCount++; }
            else { sb.AppendLine($"  [FAIL] Overlaps: Significant ({overlapPercent:F2}%) — paint will bleed across overlapping areas"); failCount++; }

            // Check 2: Out-of-range UVs
            int outOfRange = 0;
            for (int i = 0; i < uvs.Length; i++)
            {
                if (uvs[i].x < 0f || uvs[i].x > 1f || uvs[i].y < 0f || uvs[i].y > 1f)
                    outOfRange++;
            }
            float oorPercent = outOfRange * 100f / uvs.Length;

            if (outOfRange == 0) { sb.AppendLine("  [PASS] Out-of-range UVs: None"); passCount++; }
            else if (oorPercent < 5f) { sb.AppendLine($"  [WARN] Out-of-range UVs: {outOfRange} vertices ({oorPercent:F1}%) — paint may tile or be clipped"); warnCount++; }
            else { sb.AppendLine($"  [FAIL] Out-of-range UVs: {outOfRange} vertices ({oorPercent:F1}%) — significant tiling/clipping issues"); failCount++; }

            // Check 3: Island padding
            var islands = UVIslandDetector.DetectIslands(mesh);
            float minGap = float.MaxValue;
            int islandCap = Mathf.Min(islands.Count, 50);
            for (int i = 0; i < islandCap; i++)
            {
                for (int j = i + 1; j < islandCap; j++)
                {
                    float gap = EstimateIslandGap(islands[i], islands[j], uvs, triangles);
                    if (gap < minGap) minGap = gap;
                }
            }
            float paddingPixels = minGap * 1024;

            if (minGap == float.MaxValue || paddingPixels >= 4f) { sb.AppendLine($"  [PASS] Padding: {paddingPixels:F1}px at 1024px — adequate"); passCount++; }
            else if (paddingPixels >= 2f) { sb.AppendLine($"  [WARN] Padding: {paddingPixels:F1}px at 1024px — tight, may cause bleed at lower resolutions"); warnCount++; }
            else { sb.AppendLine($"  [FAIL] Padding: {paddingPixels:F1}px at 1024px — too small, paint will bleed between islands"); failCount++; }

            // Check 4: Degenerate triangles
            int degenerateCount = 0;
            for (int tri = 0; tri < triCount; tri++)
            {
                int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                float area = ComputeUVTriangleArea(uvs[i0], uvs[i1], uvs[i2]);
                if (area < 1e-8f) degenerateCount++;
            }
            float degPercent = degenerateCount * 100f / triCount;

            if (degenerateCount == 0) { sb.AppendLine("  [PASS] Degenerate triangles: None"); passCount++; }
            else if (degPercent < 2f) { sb.AppendLine($"  [WARN] Degenerate triangles: {degenerateCount} ({degPercent:F1}%)"); warnCount++; }
            else { sb.AppendLine($"  [FAIL] Degenerate triangles: {degenerateCount} ({degPercent:F1}%) — these areas cannot be painted"); failCount++; }

            // Check 5: UV Coverage
            float coveragePercent = coveredPixels * 100f / (resolution * resolution);

            if (coveragePercent > 60f) { sb.AppendLine($"  [PASS] UV coverage: {coveragePercent:F1}%"); passCount++; }
            else if (coveragePercent > 30f) { sb.AppendLine($"  [WARN] UV coverage: {coveragePercent:F1}% — low coverage wastes texture space"); warnCount++; }
            else { sb.AppendLine($"  [FAIL] UV coverage: {coveragePercent:F1}% — very low, much texture space is wasted"); failCount++; }

            // Summary
            sb.AppendLine();
            sb.AppendLine($"  Summary: {passCount} PASS, {warnCount} WARN, {failCount} FAIL");
            if (failCount == 0 && warnCount == 0)
                sb.AppendLine("  Overall: READY for texture painting.");
            else if (failCount == 0)
                sb.AppendLine("  Overall: Paintable with minor caveats.");
            else
                sb.AppendLine("  Overall: UV issues should be addressed before painting for best results.");

            return sb.ToString().TrimEnd();
        }

        // ─── Helpers ───

        private static float EstimateIslandGap(UVIsland a, UVIsland b, Vector2[] uvs, int[] triangles)
        {
            Rect boundsA = a.uvBounds;
            Rect boundsB = b.uvBounds;

            float dx = Mathf.Max(0, Mathf.Max(boundsA.xMin - boundsB.xMax, boundsB.xMin - boundsA.xMax));
            float dy = Mathf.Max(0, Mathf.Max(boundsA.yMin - boundsB.yMax, boundsB.yMin - boundsA.yMax));

            if (dx > 0 || dy > 0)
                return Mathf.Sqrt(dx * dx + dy * dy);

            var aEdgeUVs = GetIslandEdgeUVs(a, uvs, triangles, 20);
            var bEdgeUVs = GetIslandEdgeUVs(b, uvs, triangles, 20);

            float minDist = float.MaxValue;
            foreach (var ua in aEdgeUVs)
            {
                foreach (var ub in bEdgeUVs)
                {
                    float dist = Vector2.Distance(ua, ub);
                    if (dist < minDist) minDist = dist;
                }
            }

            return minDist;
        }

        private static List<Vector2> GetIslandEdgeUVs(UVIsland island, Vector2[] uvs, int[] triangles, int maxSamples)
        {
            var result = new HashSet<Vector2>();
            int step = Mathf.Max(1, island.triangleIndices.Count / maxSamples);
            for (int i = 0; i < island.triangleIndices.Count; i += step)
            {
                int triIdx = island.triangleIndices[i];
                for (int j = 0; j < 3; j++)
                {
                    result.Add(uvs[triangles[triIdx * 3 + j]]);
                    if (result.Count >= maxSamples) break;
                }
                if (result.Count >= maxSamples) break;
            }
            return result.ToList();
        }
    }
}
