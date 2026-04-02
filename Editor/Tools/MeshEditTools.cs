using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.Fitting;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Mesh vertex editing tools for AI agent and programmatic use.
    /// Supports sphere/bone-based vertex selection, move, scale, smooth, and direct set.
    /// </summary>
    public static class MeshEditTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("MeshEdit");

        // ========== Internal Context ==========

        internal struct MeshContext
        {
            public SkinnedMeshRenderer smr;
            public MeshFilter mf;
            public Mesh mesh;
            public Vector3[] vertices;       // mesh-space
            public Vector3[] worldVertices;   // world-space (baked for SMR)
            public BoneWeight[] boneWeights;
            public Transform[] bones;
            public Matrix4x4[] bindPoses;
            public bool isSkinned;
        }

        internal struct VertexSelection
        {
            public int index;
            public float weight; // 0..1 influence (falloff)
        }

        // ========== Tool: GetVertexPositions ==========

        [AgentTool("Read vertex positions from a mesh. Select by sphere (center+radius) or by bone influence (boneName+minWeight). Returns index, world position, and bone weights. Use maxResults to limit output.")]
        public static string GetVertexPositions(
            string gameObjectName,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            string boneName = "",
            float minWeight = 0.1f,
            int maxResults = 100)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshContext ctx;
            if (!TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";

            List<VertexSelection> selection;
            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            bool hasBone = !string.IsNullOrEmpty(boneName);

            if (hasSphere)
            {
                var center = new Vector3(centerX, centerY, centerZ);
                selection = SelectBySphere(ctx, center, radius, 0f);
            }
            else if (hasBone && ctx.isSkinned)
            {
                selection = SelectByBone(ctx, boneName, minWeight);
            }
            else
            {
                // Return all vertices (limited)
                selection = new List<VertexSelection>();
                for (int i = 0; i < ctx.worldVertices.Length; i++)
                    selection.Add(new VertexSelection { index = i, weight = 1f });
            }

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            // Sort by weight descending
            selection.Sort((a, b) => b.weight.CompareTo(a.weight));

            int total = selection.Count;
            if (maxResults > 0 && selection.Count > maxResults)
                selection = selection.GetRange(0, maxResults);

            var sb = new StringBuilder();
            sb.AppendLine($"Vertices on '{gameObjectName}' ({total} matched, showing {selection.Count}):");
            sb.AppendLine($"{"Idx",6} {"WorldX",9} {"WorldY",9} {"WorldZ",9} {"Weight",7}");

            foreach (var sel in selection)
            {
                var wp = ctx.worldVertices[sel.index];
                sb.AppendLine($"{sel.index,6} {wp.x,9:F4} {wp.y,9:F4} {wp.z,9:F4} {sel.weight,7:F3}");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: MoveVertices ==========

        [AgentTool("Move vertices within a sphere or bone region by a world-space delta. falloff: 0=hard edge, 1=smooth linear falloff. Uses skinning-aware transform for SkinnedMeshRenderer.")]
        public static string MoveVertices(
            string gameObjectName,
            float deltaX, float deltaY, float deltaZ,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            string boneName = "",
            float minWeight = 0.1f,
            float falloff = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshContext ctx;
            if (!TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";

            List<VertexSelection> selection;
            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            bool hasBone = !string.IsNullOrEmpty(boneName);

            if (hasSphere)
                selection = SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, falloff);
            else if (hasBone && ctx.isSkinned)
                selection = SelectByBone(ctx, boneName, minWeight);
            else
                return "Error: Specify either sphere center (centerX/Y/Z + radius) or boneName for vertex selection.";

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            var deltaWorld = new Vector3(deltaX, deltaY, deltaZ);
            var newVerts = (Vector3[])ctx.vertices.Clone();

            if (ctx.isSkinned)
            {
                var skinMats = FittingHelpers.PrecomputeSkinMatrices(ctx.bones, ctx.bindPoses);
                foreach (var sel in selection)
                {
                    var meshDelta = FittingHelpers.WorldToMeshDelta(skinMats, ctx.boneWeights[sel.index], deltaWorld * sel.weight);
                    newVerts[sel.index] += meshDelta;
                }
            }
            else
            {
                var invTRS = go.transform.worldToLocalMatrix;
                foreach (var sel in selection)
                {
                    var localDelta = invTRS.MultiplyVector(deltaWorld * sel.weight);
                    newVerts[sel.index] += localDelta;
                }
            }

            string result = ApplyMeshEdit(ctx, newVerts, "MoveVertices");
            return $"Moved {selection.Count} vertices by ({deltaX:F4}, {deltaY:F4}, {deltaZ:F4}). {result}";
        }

        // ========== Tool: ScaleVertices ==========

        [AgentTool("Scale vertices relative to the sphere center. scaleX/Y/Z are multipliers (1.0=no change). falloff: 0=hard, 1=smooth.")]
        public static string ScaleVertices(
            string gameObjectName,
            float scaleX = 1f, float scaleY = 1f, float scaleZ = 1f,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            float falloff = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshContext ctx;
            if (!TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";

            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            if (!hasSphere)
                return "Error: Specify sphere center (centerX/Y/Z + radius) for scaling.";

            var center = new Vector3(centerX, centerY, centerZ);
            var selection = SelectBySphere(ctx, center, radius, falloff);
            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            var scale = new Vector3(scaleX, scaleY, scaleZ);
            var newVerts = (Vector3[])ctx.vertices.Clone();

            if (ctx.isSkinned)
            {
                var skinMats = FittingHelpers.PrecomputeSkinMatrices(ctx.bones, ctx.bindPoses);
                foreach (var sel in selection)
                {
                    var wp = ctx.worldVertices[sel.index];
                    var offset = wp - center;
                    var scaled = Vector3.Scale(offset, scale);
                    var deltaWorld = (scaled - offset) * sel.weight;
                    newVerts[sel.index] += FittingHelpers.WorldToMeshDelta(skinMats, ctx.boneWeights[sel.index], deltaWorld);
                }
            }
            else
            {
                var invTRS = go.transform.worldToLocalMatrix;
                foreach (var sel in selection)
                {
                    var wp = ctx.worldVertices[sel.index];
                    var offset = wp - center;
                    var scaled = Vector3.Scale(offset, scale);
                    var deltaWorld = (scaled - offset) * sel.weight;
                    newVerts[sel.index] += invTRS.MultiplyVector(deltaWorld);
                }
            }

            string result = ApplyMeshEdit(ctx, newVerts, "ScaleVertices");
            return $"Scaled {selection.Count} vertices by ({scaleX:F3}, {scaleY:F3}, {scaleZ:F3}). {result}";
        }

        // ========== Tool: SmoothVertices ==========

        [AgentTool("Smooth vertices using volume-preserving Taubin smoothing. Select by sphere or bone. iterations controls smoothing strength.")]
        public static string SmoothVertices(
            string gameObjectName,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            string boneName = "",
            float minWeight = 0.1f,
            int iterations = 3,
            float strength = 0.5f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshContext ctx;
            if (!TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";

            List<VertexSelection> selection;
            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            bool hasBone = !string.IsNullOrEmpty(boneName);

            if (hasSphere)
                selection = SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, 0f);
            else if (hasBone && ctx.isSkinned)
                selection = SelectByBone(ctx, boneName, minWeight);
            else
                return "Error: Specify either sphere center or boneName for smoothing selection.";

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            // Build adjacency from triangles
            var tris = ctx.mesh.triangles;
            var adjacency = BuildAdjacency(ctx.vertices.Length, tris);

            // Collect root indices for smoothing
            var rootIndices = new List<int>(selection.Count);
            foreach (var sel in selection)
                rootIndices.Add(sel.index);

            // Run Taubin smoothing on world-space vertices (then convert delta back)
            var workVerts = (Vector3[])ctx.worldVertices.Clone();
            var buffer = new Vector3[workVerts.Length];
            System.Array.Copy(workVerts, buffer, workVerts.Length);

            float lambda = strength;
            float mu = -(strength + 0.02f); // slightly stronger inflation for volume preservation

            for (int i = 0; i < iterations; i++)
            {
                ToolProgress.Report((float)i / iterations, $"Smoothing pass {i + 1}/{iterations}");
                FittingHelpers.TaubinSmoothRoots(workVerts, buffer, adjacency, rootIndices, lambda, mu);
            }

            // Convert world delta to mesh space
            var newVerts = (Vector3[])ctx.vertices.Clone();
            if (ctx.isSkinned)
            {
                var skinMats = FittingHelpers.PrecomputeSkinMatrices(ctx.bones, ctx.bindPoses);
                foreach (var sel in selection)
                {
                    var delta = workVerts[sel.index] - ctx.worldVertices[sel.index];
                    newVerts[sel.index] += FittingHelpers.WorldToMeshDelta(skinMats, ctx.boneWeights[sel.index], delta * sel.weight);
                }
            }
            else
            {
                var invTRS = go.transform.worldToLocalMatrix;
                foreach (var sel in selection)
                {
                    var delta = workVerts[sel.index] - ctx.worldVertices[sel.index];
                    newVerts[sel.index] += invTRS.MultiplyVector(delta * sel.weight);
                }
            }

            ToolProgress.Clear();
            string result = ApplyMeshEdit(ctx, newVerts, "SmoothVertices");
            return $"Smoothed {selection.Count} vertices ({iterations} Taubin iterations, strength={strength:F2}). {result}";
        }

        // ========== Tool: SetVertexPositions ==========

        [AgentTool("Set specific vertex positions by index. vertexData format: 'index:x,y,z;index:x,y,z'. Coordinates are in world space. Use GetVertexPositions first to find indices.")]
        public static string SetVertexPositions(string gameObjectName, string vertexData)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshContext ctx;
            if (!TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";

            if (string.IsNullOrEmpty(vertexData))
                return "Error: vertexData is required. Format: 'index:x,y,z;index:x,y,z'";

            var entries = ParseVertexData(vertexData);
            if (entries.Count == 0)
                return "Error: No valid entries in vertexData.";

            var newVerts = (Vector3[])ctx.vertices.Clone();
            int applied = 0;
            var errors = new List<string>();

            Matrix4x4[] skinMats = null;
            if (ctx.isSkinned)
                skinMats = FittingHelpers.PrecomputeSkinMatrices(ctx.bones, ctx.bindPoses);

            foreach (var (idx, worldPos) in entries)
            {
                if (idx < 0 || idx >= ctx.vertices.Length)
                {
                    errors.Add($"Index {idx} out of range (0..{ctx.vertices.Length - 1})");
                    continue;
                }

                var deltaWorld = worldPos - ctx.worldVertices[idx];
                if (ctx.isSkinned)
                    newVerts[idx] += FittingHelpers.WorldToMeshDelta(skinMats, ctx.boneWeights[idx], deltaWorld);
                else
                    newVerts[idx] += go.transform.worldToLocalMatrix.MultiplyVector(deltaWorld);

                applied++;
            }

            string result = ApplyMeshEdit(ctx, newVerts, "SetVertexPositions");

            var sb = new StringBuilder();
            sb.Append($"Set {applied} vertex positions. {result}");
            if (errors.Count > 0)
                sb.Append($" Errors: {string.Join("; ", errors)}");
            return sb.ToString();
        }

        // ========== Internal: TryGetMeshContext ==========

        internal static bool TryGetMeshContext(GameObject go, out MeshContext ctx)
        {
            ctx = new MeshContext();

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                ctx.smr = smr;
                ctx.mesh = smr.sharedMesh;
                ctx.vertices = ctx.mesh.vertices;
                ctx.boneWeights = ctx.mesh.boneWeights;
                ctx.bones = smr.bones;
                ctx.bindPoses = ctx.mesh.bindposes;
                ctx.isSkinned = true;

                // Bake to world space
                var baked = new Mesh();
                smr.BakeMesh(baked);
                ctx.worldVertices = baked.vertices;
                for (int i = 0; i < ctx.worldVertices.Length; i++)
                    ctx.worldVertices[i] = smr.transform.TransformPoint(ctx.worldVertices[i]);
                Object.DestroyImmediate(baked);
                return true;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                ctx.mf = mf;
                ctx.mesh = mf.sharedMesh;
                ctx.vertices = ctx.mesh.vertices;
                ctx.isSkinned = false;

                ctx.worldVertices = new Vector3[ctx.vertices.Length];
                var ltw = go.transform.localToWorldMatrix;
                for (int i = 0; i < ctx.vertices.Length; i++)
                    ctx.worldVertices[i] = ltw.MultiplyPoint3x4(ctx.vertices[i]);
                return true;
            }

            return false;
        }

        // ========== Internal: SelectBySphere ==========

        internal static List<VertexSelection> SelectBySphere(MeshContext ctx, Vector3 center, float radius, float falloff)
        {
            var result = new List<VertexSelection>();
            float r2 = radius * radius;

            for (int i = 0; i < ctx.worldVertices.Length; i++)
            {
                float dist2 = (ctx.worldVertices[i] - center).sqrMagnitude;
                if (dist2 > r2) continue;

                float dist = Mathf.Sqrt(dist2);
                float w = 1f;
                if (falloff > 0f && radius > 0f)
                {
                    float t = dist / radius;
                    w = 1f - Mathf.Pow(t, 1f / Mathf.Max(falloff, 0.01f));
                    w = Mathf.Clamp01(w);
                }

                result.Add(new VertexSelection { index = i, weight = w });
            }

            return result;
        }

        // ========== Internal: SelectByBone ==========

        internal static List<VertexSelection> SelectByBone(MeshContext ctx, string boneName, float minWeight)
        {
            var result = new List<VertexSelection>();
            if (!ctx.isSkinned || ctx.boneWeights == null) return result;

            int boneIdx = FindBoneIndex(ctx.bones, boneName);
            if (boneIdx < 0) return result;

            for (int i = 0; i < ctx.boneWeights.Length; i++)
            {
                float w = GetBoneInfluence(ctx.boneWeights[i], boneIdx);
                if (w >= minWeight)
                    result.Add(new VertexSelection { index = i, weight = w });
            }

            return result;
        }

        // ========== Internal: ApplyMeshEdit ==========

        internal static string ApplyMeshEdit(MeshContext ctx, Vector3[] newVertices, string undoName)
        {
            ToolUtility.EnsureAssetDirectory(GeneratedDir);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"MeshEdit: {undoName}");

            var newMesh = Object.Instantiate(ctx.mesh);
            newMesh.name = $"{ctx.mesh.name}_edited";
            newMesh.vertices = newVertices;
            newMesh.RecalculateNormals();
            newMesh.RecalculateBounds();

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);

            if (ctx.smr != null)
            {
                Undo.RecordObject(ctx.smr, $"MeshEdit: {undoName}");
                ctx.smr.sharedMesh = newMesh;
            }
            else if (ctx.mf != null)
            {
                Undo.RecordObject(ctx.mf, $"MeshEdit: {undoName}");
                ctx.mf.sharedMesh = newMesh;
            }

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            return $"Saved: {assetPath}";
        }

        // ========== Internal Helpers ==========

        public static int FindBoneIndex(Transform[] bones, string boneName)
        {
            if (bones == null) return -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i].name == boneName)
                    return i;
            }
            // Case-insensitive fallback
            string lower = boneName.ToLowerInvariant();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && bones[i].name.ToLowerInvariant() == lower)
                    return i;
            }
            return -1;
        }

        public static float GetBoneInfluence(BoneWeight bw, int boneIndex)
        {
            float w = 0f;
            if (bw.boneIndex0 == boneIndex) w += bw.weight0;
            if (bw.boneIndex1 == boneIndex) w += bw.weight1;
            if (bw.boneIndex2 == boneIndex) w += bw.weight2;
            if (bw.boneIndex3 == boneIndex) w += bw.weight3;
            return w;
        }

        public static List<int>[] BuildAdjacency(int vertexCount, int[] triangles)
        {
            var adj = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                adj[i] = new List<int>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (!adj[a].Contains(b)) adj[a].Add(b);
                if (!adj[a].Contains(c)) adj[a].Add(c);
                if (!adj[b].Contains(a)) adj[b].Add(a);
                if (!adj[b].Contains(c)) adj[b].Add(c);
                if (!adj[c].Contains(a)) adj[c].Add(a);
                if (!adj[c].Contains(b)) adj[c].Add(b);
            }

            return adj;
        }

        private static List<(int index, Vector3 pos)> ParseVertexData(string data)
        {
            var result = new List<(int, Vector3)>();
            foreach (string entry in data.Split(';'))
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;

                string idxStr = trimmed.Substring(0, colonIdx).Trim();
                string posStr = trimmed.Substring(colonIdx + 1).Trim();

                if (!int.TryParse(idxStr, out int idx)) continue;

                string[] parts = posStr.Split(',');
                if (parts.Length != 3) continue;

                if (!float.TryParse(parts[0].Trim(), out float x)) continue;
                if (!float.TryParse(parts[1].Trim(), out float y)) continue;
                if (!float.TryParse(parts[2].Trim(), out float z)) continue;

                result.Add((idx, new Vector3(x, y, z)));
            }
            return result;
        }
    }
}
