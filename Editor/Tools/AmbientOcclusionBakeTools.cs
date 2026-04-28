using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Raycast-based ambient occlusion (AO) bake tool.
    /// - mode="texel": per-texel AO → PNG under Assets/UnityAgent_Generated/AmbientOcclusion/
    /// - mode="vertex": per-vertex AO → mesh.colors → new .asset + swap Renderer.sharedMesh
    ///
    /// Occluders are realized as temporary MeshColliders placed on a dedicated layer
    /// so Physics.Raycast only hits our geometry (not PhysBone/VRC colliders in the
    /// scene). SkinnedMeshRenderer vertex/normal bake follows the
    /// MeshPainterV2Window.GetWorldVertices pattern (position + rotation only) to
    /// sidestep the Unity BakeMesh scale-double-apply gotcha.
    /// </summary>
    internal static class AmbientOcclusionBakeTools
    {
        // Builtin layer 31 is unused in a stock Unity project. If a user happens to
        // have their own colliders on this layer they'll show up as occluders, which
        // is a narrow risk accepted in v1 in exchange for much simpler ray filtering.
        private const int TempLayer = 31;
        private const int TempLayerMask = 1 << TempLayer;

        private const float DefaultMaxDistance = 0.5f;
        private const float DefaultBias = 0.001f;
        private const int FallbackResolution = 1024;
        private const int ProgressYieldInterval = 256; // yield every N texels/verts
        // Padding grown outward from baked texels so bilinear/mip sampling does not
        // hit the unwritten background across UV-island seams. 4px handles up to
        // trilinear+mip1 on typical atlases; larger at a small cost.
        private const int SeamPadding = 4;

        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool(
            "メッシュに対して Raycast ベースの AO (ambient occlusion) を焼く。" +
            "mode=\"texel\": UV 展開に従い各テクセルに AO を焼き PNG を生成 (Assets/UnityAgent_Generated/AmbientOcclusion/...)。" +
            "マテリアル (submesh) ごとに個別の PNG を出力し、解像度は各 material の _MainTex から決定 (無ければ 1024)。" +
            "UV シーム対策として 4px の dilation (seam padding) を自動適用。" +
            "mode=\"vertex\": 各頂点に AO を焼き mesh.colors に書き込んだ新規 .asset を作成し Renderer に差し替え。" +
            "occluderHierarchyPaths はセミコロン区切りで遮蔽物 (例 'Avatar/Body') を指定、空なら self のみ。" +
            "quality=low(32)/medium(64)/high(128)。samplesOverride>0 なら上書き。" +
            "maxDistance はメートル (0=0.5m)、biasAmount (0=0.001)、intensity (<=0=1.0)。" +
            "outputPath が空なら自動命名 (決定的パス—再焼きで上書き)。multi-submesh + 明示指定時は '{stem}_mat{i}_{matname}{ext}' に展開。" +
            "Note: SkinnedMeshRenderer を vertex モードで焼くとその時のポーズが焼き込まれるので、T-pose (Animator disabled 等) 状態で呼ぶこと。",
            Author = "ajisaiflow",
            Version = "0.2.0",
            Category = "Baking",
            Risk = ToolRisk.Caution)]
        public static IEnumerator BakeAmbientOcclusion(
            string targetHierarchyPath,
            string occluderHierarchyPaths,
            string mode,
            string quality,
            int samplesOverride,
            float maxDistance,
            float biasAmount,
            float intensity,
            string outputPath)
        {
            // ─── Validate args ───
            mode = string.IsNullOrWhiteSpace(mode) ? "texel" : mode.Trim().ToLowerInvariant();
            if (mode != "texel" && mode != "vertex")
            {
                yield return $"Error: mode must be 'texel' or 'vertex', got '{mode}'.";
                yield break;
            }

            int samples = ResolveSamples(quality, samplesOverride);
            if (samples <= 0)
            {
                yield return "Error: samples must be > 0.";
                yield break;
            }

            if (maxDistance <= 0f) maxDistance = DefaultMaxDistance;
            if (biasAmount <= 0f) biasAmount = DefaultBias;
            if (intensity <= 0f) intensity = 1f;

            // ─── Resolve target renderer ───
            if (string.IsNullOrWhiteSpace(targetHierarchyPath))
            {
                yield return "Error: targetHierarchyPath is empty.";
                yield break;
            }
            var targetGo = FindGO(targetHierarchyPath);
            if (targetGo == null)
            {
                yield return $"Error: target '{targetHierarchyPath}' not found in any open scene.";
                yield break;
            }
            var targetRenderer = targetGo.GetComponent<Renderer>();
            if (!(targetRenderer is MeshRenderer) && !(targetRenderer is SkinnedMeshRenderer))
            {
                yield return $"Error: target '{targetHierarchyPath}' must have a MeshRenderer or SkinnedMeshRenderer.";
                yield break;
            }
            var targetMesh = GetSharedMesh(targetRenderer);
            if (targetMesh == null)
            {
                yield return $"Error: target '{targetHierarchyPath}' has no mesh.";
                yield break;
            }
            if (mode == "texel" && (targetMesh.uv == null || targetMesh.uv.Length == 0))
            {
                yield return $"Error: target mesh has no UVs — texel mode requires UVs. Use mode='vertex' instead.";
                yield break;
            }

            // ─── Resolve occluder renderers ───
            var occluderList = new List<Renderer> { targetRenderer };
            if (!string.IsNullOrWhiteSpace(occluderHierarchyPaths))
            {
                foreach (var raw in occluderHierarchyPaths.Split(';'))
                {
                    var path = raw.Trim();
                    if (string.IsNullOrEmpty(path)) continue;
                    var go = FindGO(path);
                    if (go == null)
                    {
                        yield return $"Error: occluder '{path}' not found.";
                        yield break;
                    }
                    var r = go.GetComponent<Renderer>();
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                    {
                        yield return $"Error: occluder '{path}' has no renderer.";
                        yield break;
                    }
                    if (!occluderList.Contains(r)) occluderList.Add(r);
                }
            }

            // ─── Build temp colliders (finally cleans up) ───
            var tempGOs = new List<GameObject>();
            var tempMeshes = new List<Mesh>();
            bool originalAutoSync = Physics.autoSyncTransforms;
            string finalResult = null;

            try
            {
                Physics.autoSyncTransforms = true;

                foreach (var r in occluderList)
                {
                    var built = BuildTempCollider(r);
                    if (built.go != null)
                    {
                        tempGOs.Add(built.go);
                        if (built.ownedMesh != null) tempMeshes.Add(built.ownedMesh);
                    }
                }
                if (tempGOs.Count == 0)
                {
                    yield return "Error: failed to build any temp colliders for occluders.";
                    yield break;
                }
                Physics.SyncTransforms();

                // ─── Bake ───
                if (mode == "texel")
                {
                    var texelBake = BakeTexel(
                        targetRenderer, targetMesh,
                        samples, maxDistance, biasAmount, intensity,
                        outputPath);
                    while (texelBake.MoveNext())
                    {
                        if (texelBake.Current is string s && (s.StartsWith("Success") || s.StartsWith("Error") || s.StartsWith("Warning")))
                        {
                            finalResult = s;
                            break;
                        }
                        yield return texelBake.Current;
                    }
                }
                else // vertex
                {
                    var vertexBake = BakeVertex(
                        targetRenderer, targetMesh,
                        samples, maxDistance, biasAmount, intensity,
                        outputPath);
                    while (vertexBake.MoveNext())
                    {
                        if (vertexBake.Current is string s && (s.StartsWith("Success") || s.StartsWith("Error") || s.StartsWith("Warning")))
                        {
                            finalResult = s;
                            break;
                        }
                        yield return vertexBake.Current;
                    }
                }
            }
            finally
            {
                foreach (var go in tempGOs) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                foreach (var m in tempMeshes) if (m != null) UnityEngine.Object.DestroyImmediate(m);
                Physics.autoSyncTransforms = originalAutoSync;
                ToolProgress.Clear();
            }

            yield return finalResult ?? "Error: bake produced no result.";
        }

        // ───────────────────────── Texel-mode bake ─────────────────────────

        private static IEnumerator BakeTexel(
            Renderer targetRenderer, Mesh targetMesh,
            int samples, float maxDistance, float biasAmount, float intensity,
            string outputPath)
        {
            // Baked world-space vertices / normals on the target — one BakeMesh per call.
            if (!GetWorldBakedGeometry(targetRenderer, out var wv, out var wn) || wv.Length == 0)
            {
                yield return "Error: failed to bake target vertices/normals.";
                yield break;
            }
            if (wn == null || wn.Length != wv.Length)
            {
                yield return "Error: target mesh is missing per-vertex normals.";
                yield break;
            }

            var uvs = targetMesh.uv;
            if (uvs == null || uvs.Length == 0)
            {
                yield return "Error: target mesh has no UVs.";
                yield break;
            }

            int submeshCount = Mathf.Max(1, targetMesh.subMeshCount);
            var mats = targetRenderer.sharedMaterials;

            // Total triangles across submeshes for stable progress reporting
            int totalTris = 0;
            for (int sm = 0; sm < submeshCount; sm++)
                totalTris += targetMesh.GetTriangles(sm).Length / 3;
            if (totalTris == 0)
            {
                yield return "Error: no triangles found in target mesh.";
                yield break;
            }

            string meshName = SanitizeName(targetMesh.name);
            var savedEntries = new List<string>();
            string firstSavedPath = null;
            int cumulativeTris = 0;
            int yieldCounter = 0;
            float lastReportedProgress = -1f;

            for (int sm = 0; sm < submeshCount; sm++)
            {
                var smTris = targetMesh.GetTriangles(sm);
                if (smTris.Length == 0) continue;
                int smTriCount = smTris.Length / 3;

                Material mat = (mats != null && sm < mats.Length) ? mats[sm] : null;
                ResolveResolution(mat, out int resW, out int resH);

                var pixels = new Color[resW * resH];
                var written = new bool[resW * resH];

                for (int t = 0; t < smTris.Length; t += 3)
                {
                    int i0 = smTris[t];
                    int i1 = smTris[t + 1];
                    int i2 = smTris[t + 2];

                    // Guard both uv and world-baked buffer lengths — BakeMesh can
                    // return fewer verts than sharedMesh.uv on some SMR configurations.
                    if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length) continue;
                    if (i0 >= wv.Length || i1 >= wv.Length || i2 >= wv.Length) continue;

                    Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
                    Vector3 p0 = wv[i0], p1 = wv[i1], p2 = wv[i2];
                    Vector3 n0 = wn[i0], n1 = wn[i1], n2 = wn[i2];

                    Vector2 px0 = new Vector2(uv0.x * resW, uv0.y * resH);
                    Vector2 px1 = new Vector2(uv1.x * resW, uv1.y * resH);
                    Vector2 px2 = new Vector2(uv2.x * resW, uv2.y * resH);

                    int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(px0.x, Mathf.Min(px1.x, px2.x))), 0, resW - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(px0.x, Mathf.Max(px1.x, px2.x))), 0, resW - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(px0.y, Mathf.Min(px1.y, px2.y))), 0, resH - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(px0.y, Mathf.Max(px1.y, px2.y))), 0, resH - 1);

                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector2 pCenter = new Vector2(x + 0.5f, y + 0.5f);
                            Vector3 bc = TextureEditCore.ComputeBarycentric(pCenter, px0, px1, px2);
                            if (bc.x < 0f || bc.y < 0f || bc.z < 0f) continue;

                            Vector3 wp = p0 * bc.x + p1 * bc.y + p2 * bc.z;
                            Vector3 wnrm = (n0 * bc.x + n1 * bc.y + n2 * bc.z).normalized;
                            if (wnrm.sqrMagnitude < 1e-6f) continue;

                            int idx = y * resW + x;
                            float ao = ComputeAOAtPoint(wp, wnrm, samples, maxDistance, biasAmount, intensity,
                                Hash2(x, y));
                            pixels[idx] = new Color(ao, ao, ao, 1f);
                            written[idx] = true;
                        }
                    }

                    cumulativeTris++;
                    yieldCounter++;

                    float progress = (float)cumulativeTris / totalTris;
                    if (progress - lastReportedProgress >= 0.005f)
                    {
                        ToolProgress.Report(progress,
                            $"Baking AO (texel, submesh {sm + 1}/{submeshCount})",
                            $"{cumulativeTris}/{totalTris} triangles, {samples} samples, {resW}×{resH}");
                        lastReportedProgress = progress;
                    }
                    if (yieldCounter >= ProgressYieldInterval)
                    {
                        yieldCounter = 0;
                        yield return null;
                    }
                }

                // Grow baked texels outward across UV-island seams (bilinear/mip safety)
                DilateSeams(pixels, written, resW, resH, SeamPadding);

                // Any still-unwritten pixel (beyond dilation reach) → white (no AO)
                for (int i = 0; i < pixels.Length; i++)
                    if (!written[i]) pixels[i] = Color.white;

                // Save
                var tex = new Texture2D(resW, resH, TextureFormat.RGBA32, false);
                tex.SetPixels(pixels);
                tex.Apply();

                string savedPath = null;
                string saveError = null;
                try
                {
                    string materialName = mat != null ? mat.name : null;
                    savedPath = SaveAOTexture(
                        tex, targetRenderer, meshName, outputPath,
                        sm, submeshCount, materialName);
                }
                catch (Exception e) { saveError = e.Message; }
                UnityEngine.Object.DestroyImmediate(tex);

                if (saveError != null)
                {
                    yield return $"Error: failed to save AO texture (submesh {sm}): {saveError}";
                    yield break;
                }

                if (firstSavedPath == null) firstSavedPath = savedPath;
                savedEntries.Add($"'{savedPath}' ({resW}×{resH}, {smTriCount} tris)");
            }

            if (savedEntries.Count == 0)
            {
                yield return "Error: no submeshes produced output.";
                yield break;
            }

            int skippedSubmeshes = submeshCount - savedEntries.Count;
            string skipNote = skippedSubmeshes > 0
                ? $" [skipped {skippedSubmeshes} empty submesh{(skippedSubmeshes == 1 ? "" : "es")}]"
                : "";

            if (savedEntries.Count == 1)
            {
                yield return $"Success: AO baked to {savedEntries[0]}, {samples} samples{skipNote}.";
            }
            else
            {
                // Keep the first saved path quoted early so downstream parsers (e.g.
                // the test window's "Ping last output" button) can still extract it.
                yield return $"Success: AO baked to '{firstSavedPath}' and {savedEntries.Count - 1} more ({samples} samples){skipNote}:\n  "
                    + string.Join("\n  ", savedEntries);
            }
        }

        // ───────────────────────── Seam dilation ─────────────────────────

        // Iteratively grow the written mask by one pixel per pass, filling each new
        // pixel with the average of its already-written 8-neighbors. O(w·h·passes).
        // NOTE: operates on the red channel only, then broadcasts to RGB. AO bakes
        // are monochrome by construction (new Color(ao, ao, ao, 1)); extending this
        // helper to colored inputs would require averaging all three channels.
        private static void DilateSeams(Color[] pixels, bool[] written, int width, int height, int passes)
        {
            if (passes <= 0) return;
            var nextWritten = new bool[written.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                Array.Copy(written, nextWritten, written.Length);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        if (written[idx]) continue;

                        float sum = 0f;
                        int count = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = y + dy;
                            if ((uint)ny >= (uint)height) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx;
                                if ((uint)nx >= (uint)width) continue;
                                int nIdx = ny * width + nx;
                                if (!written[nIdx]) continue;
                                sum += pixels[nIdx].r;
                                count++;
                            }
                        }
                        if (count > 0)
                        {
                            float v = sum / count;
                            pixels[idx] = new Color(v, v, v, 1f);
                            nextWritten[idx] = true;
                        }
                    }
                }
                Array.Copy(nextWritten, written, written.Length);
            }
        }

        // ───────────────────────── Vertex-mode bake ─────────────────────────

        private static IEnumerator BakeVertex(
            Renderer targetRenderer, Mesh targetMesh,
            int samples, float maxDistance, float biasAmount, float intensity,
            string outputPath)
        {
            if (!GetWorldBakedGeometry(targetRenderer, out var wv, out var wn) || wv.Length == 0)
            {
                yield return "Error: failed to bake target vertices/normals.";
                yield break;
            }
            if (wn == null || wn.Length != wv.Length)
            {
                yield return "Error: target mesh is missing per-vertex normals.";
                yield break;
            }

            int vcount = wv.Length;
            var aoColors = new Color[vcount];
            float lastReportedProgress = -1f;
            int yieldCounter = 0;

            for (int i = 0; i < vcount; i++)
            {
                Vector3 n = wn[i];
                if (n.sqrMagnitude < 1e-6f)
                {
                    aoColors[i] = Color.white;
                }
                else
                {
                    float ao = ComputeAOAtPoint(wv[i], n.normalized, samples, maxDistance, biasAmount, intensity,
                        Hash2(i, 0));
                    aoColors[i] = new Color(ao, ao, ao, 1f);
                }

                yieldCounter++;
                float progress = (float)(i + 1) / vcount;
                if (progress - lastReportedProgress >= 0.005f)
                {
                    ToolProgress.Report(progress, "Baking AO (vertex)",
                        $"{i + 1}/{vcount} verts, {samples} samples");
                    lastReportedProgress = progress;
                }
                if (yieldCounter >= ProgressYieldInterval)
                {
                    yieldCounter = 0;
                    yield return null;
                }
            }

            // Duplicate mesh with vertex colors
            var newMesh = UnityEngine.Object.Instantiate(targetMesh);
            newMesh.name = targetMesh.name + "_AOVC";
            newMesh.colors = aoColors;

            string dir = PackagePaths.GetGeneratedDir("AmbientOcclusion");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string assetPath = !string.IsNullOrWhiteSpace(outputPath)
                ? outputPath.Replace("\\", "/")
                : $"{dir}/{SanitizeName(targetMesh.name)}_AOVC.asset";

            string assetError = null;
            try
            {
                // Stable path: overwrite by deleting the prior asset if present.
                // CreateAsset otherwise fails ("already exists") and the caller
                // would end up accumulating _1/_2 variants via GenerateUniqueAssetPath.
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                AssetDatabase.CreateAsset(newMesh, assetPath);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                assetError = e.Message;
            }
            if (assetError != null)
            {
                yield return $"Error: failed to save mesh asset to '{assetPath}': {assetError}";
                yield break;
            }

            Undo.RecordObject(targetRenderer, "Bake AO (vertex)");
            if (targetRenderer is SkinnedMeshRenderer smr)
                smr.sharedMesh = newMesh;
            else if (targetRenderer is MeshRenderer)
            {
                var mf = targetRenderer.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    Undo.RecordObject(mf, "Bake AO (vertex)");
                    mf.sharedMesh = newMesh;
                }
            }
            EditorUtility.SetDirty(targetRenderer);

            yield return $"Success: AO baked to vertex colors at '{assetPath}' ({vcount} verts, {samples} samples). Renderer.sharedMesh swapped.";
        }

        // ───────────────────────── Ray & sampling ─────────────────────────

        private static float ComputeAOAtPoint(
            Vector3 worldPos, Vector3 worldNormal,
            int samples, float maxDistance, float biasAmount, float intensity,
            uint seed)
        {
            // Orthonormal basis around normal
            Vector3 up = Mathf.Abs(worldNormal.y) > 0.999f ? Vector3.forward : Vector3.up;
            Vector3 t = Vector3.Cross(up, worldNormal).normalized;
            Vector3 b = Vector3.Cross(worldNormal, t);

            Vector3 origin = worldPos + worldNormal * biasAmount;
            uint state = seed == 0 ? 0x12345678u : seed;

            int hits = 0;
            for (int i = 0; i < samples; i++)
            {
                // Cosine-weighted hemisphere sample in local frame (z = normal)
                float u1 = NextFloat01(ref state);
                float u2 = NextFloat01(ref state);
                float r = Mathf.Sqrt(u1);
                float theta = 2f * Mathf.PI * u2;
                float lx = r * Mathf.Cos(theta);
                float ly = r * Mathf.Sin(theta);
                float lz = Mathf.Sqrt(Mathf.Max(0f, 1f - u1));

                Vector3 dir = t * lx + b * ly + worldNormal * lz;
                if (Physics.Raycast(origin, dir, maxDistance, TempLayerMask, QueryTriggerInteraction.Ignore))
                    hits++;
            }

            float occlusion = (float)hits / samples;
            float ao = 1f - occlusion * intensity;
            return Mathf.Clamp01(ao);
        }

        // Simple xorshift32 — deterministic per-texel RNG
        private static float NextFloat01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFFu) / 16777216f; // 24-bit mantissa
        }

        private static uint Hash2(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u;
                h ^= (uint)b + 0x85EBCA77u + (h << 6) + (h >> 2);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h == 0 ? 0x12345678u : h;
            }
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static int ResolveSamples(string quality, int samplesOverride)
        {
            if (samplesOverride > 0) return samplesOverride;
            var q = string.IsNullOrWhiteSpace(quality) ? "medium" : quality.Trim().ToLowerInvariant();
            switch (q)
            {
                case "low": return 32;
                case "medium": return 64;
                case "high": return 128;
                default: return 64;
            }
        }

        // Non-square-aware: returns the _MainTex's native dimensions so UV mapping
        // stays isotropic on wide atlases (e.g. 2048×1024). Falls back to a square
        // FallbackResolution when the material has no usable main texture.
        private static void ResolveResolution(Material mat, out int width, out int height)
        {
            if (mat != null && mat.HasProperty("_MainTex"))
            {
                var main = mat.GetTexture("_MainTex");
                if (main != null && main.width >= 16 && main.height >= 16)
                {
                    width = main.width;
                    height = main.height;
                    return;
                }
            }
            width = FallbackResolution;
            height = FallbackResolution;
        }

        private static Mesh GetSharedMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (r is MeshRenderer) return r.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }

        // Single-pass world-space vertex+normal bake. Previously split into two methods
        // that each called SMR.BakeMesh — doubling skinning cost and leaving a
        // non-atomic window where a pose tick between calls could desync verts/normals.
        private static bool GetWorldBakedGeometry(Renderer r, out Vector3[] worldVerts, out Vector3[] worldNormals)
        {
            worldVerts = null;
            worldNormals = null;

            if (r is SkinnedMeshRenderer smr)
            {
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked);
                    var localVerts = baked.vertices;
                    var localNorms = baked.normals;
                    if (localVerts == null || localVerts.Length == 0) return false;

                    var pos = smr.transform.position;
                    var rot = smr.transform.rotation;

                    worldVerts = new Vector3[localVerts.Length];
                    for (int i = 0; i < localVerts.Length; i++)
                        worldVerts[i] = pos + rot * localVerts[i];

                    if (localNorms != null && localNorms.Length == localVerts.Length)
                    {
                        worldNormals = new Vector3[localNorms.Length];
                        for (int i = 0; i < localNorms.Length; i++)
                            worldNormals[i] = (rot * localNorms[i]).normalized;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                return worldVerts != null;
            }

            if (r is MeshRenderer)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return false;
                var localVerts = mesh.vertices;
                var localNorms = mesh.normals;
                if (localVerts == null || localVerts.Length == 0) return false;

                var ltw = r.transform.localToWorldMatrix;
                worldVerts = new Vector3[localVerts.Length];
                for (int i = 0; i < localVerts.Length; i++)
                    worldVerts[i] = ltw.MultiplyPoint3x4(localVerts[i]);

                if (localNorms != null && localNorms.Length == localVerts.Length)
                {
                    // Inverse-transpose keeps normals correct under non-uniform scale.
                    var m = r.transform.localToWorldMatrix.inverse.transpose;
                    worldNormals = new Vector3[localNorms.Length];
                    for (int i = 0; i < localNorms.Length; i++)
                        worldNormals[i] = m.MultiplyVector(localNorms[i]).normalized;
                }
                return true;
            }
            return false;
        }

        private struct TempCollider
        {
            public GameObject go;
            public Mesh ownedMesh; // when created via BakeMesh; null if we reused sharedMesh
        }

        private static TempCollider BuildTempCollider(Renderer r)
        {
            var result = new TempCollider();

            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) return result;

                var baked = new Mesh();
                baked.name = "__AOBake_BakedMesh";
                smr.BakeMesh(baked);

                var go = new GameObject("__AOBakeCollider_" + r.name);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.layer = TempLayer;
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.transform.localScale = Vector3.one; // BakeMesh already includes scale (gotcha)

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = baked;

                result.go = go;
                result.ownedMesh = baked;
                return result;
            }

            if (r is MeshRenderer)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return result;

                var go = new GameObject("__AOBakeCollider_" + r.name);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.layer = TempLayer;
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;

                result.go = go;
                return result;
            }

            return result;
        }

        private static string SaveAOTexture(
            Texture2D tex, Renderer targetRenderer,
            string meshName, string outputPath,
            int subMeshIndex, int subMeshCount, string materialName)
        {
            byte[] bytes = tex.EncodeToPNG();
            if (bytes == null) throw new Exception("EncodeToPNG returned null.");

            string fullPath = BuildAOTexturePath(outputPath, targetRenderer, meshName,
                subMeshIndex, subMeshCount, materialName);

            string parent = Path.GetDirectoryName(fullPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(fullPath);

            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                // AO is a luminance mask — mark as linear (non-sRGB) so shaders read
                // it without gamma correction. Keep it uncompressed for fidelity.
                importer.sRGBTexture = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return fullPath;
        }

        // Deterministic path — re-bake overwrites rather than piling _1/_2/_3 variants.
        // For multi-submesh bakes, injects "_mat{i}_{matname}" before the extension
        // so each submesh gets its own distinct file even when outputPath is explicit.
        private static string BuildAOTexturePath(
            string outputPath, Renderer r, string meshName,
            int subMeshIndex, int subMeshCount, string materialName)
        {
            string suffix = subMeshCount > 1
                ? $"_mat{subMeshIndex}_{SanitizeName(materialName ?? "unnamed")}"
                : "";

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                string normalized = outputPath.Replace("\\", "/");
                if (subMeshCount <= 1) return normalized;

                string dir = Path.GetDirectoryName(normalized)?.Replace("\\", "/") ?? "";
                string stem = Path.GetFileNameWithoutExtension(normalized);
                string ext = Path.GetExtension(normalized);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                return string.IsNullOrEmpty(dir)
                    ? $"{stem}{suffix}{ext}"
                    : $"{dir}/{stem}{suffix}{ext}";
            }

            string avatarName = ResolveAvatarName(r);
            string folder = Path.Combine(PackagePaths.GetGeneratedDir("AmbientOcclusion"), avatarName)
                .Replace("\\", "/");
            return $"{folder}/{meshName}_AO{suffix}.png";
        }

        private static string ResolveAvatarName(Renderer r)
        {
            // Walk up until parent is null; use the top-most ancestor's name as avatar id.
            var t = r.transform;
            while (t.parent != null) t = t.parent;
            return SanitizeName(t.name);
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Unnamed";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
                else sb.Append('_');
            }
            return sb.Length == 0 ? "Unnamed" : sb.ToString();
        }
    }
}
