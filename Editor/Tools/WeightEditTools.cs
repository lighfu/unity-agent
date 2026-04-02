using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Bone weight editing tools for AI agent and programmatic use.
    /// Supports reading, setting, transferring, smoothing, and normalizing bone weights.
    /// </summary>
    public static class WeightEditTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("WeightEdit");

        // ========== Tool: GetBoneWeights ==========

        [AgentTool("Read bone weight info for vertices. Select by boneName, sphere (center+radius), or both. Shows vertex index, bone names, and weights.")]
        public static string GetBoneWeights(
            string gameObjectName,
            string boneName = "",
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            int maxResults = 50)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";
            if (!ctx.isSkinned)
                return $"Error: '{gameObjectName}' is not a SkinnedMeshRenderer.";

            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            bool hasBone = !string.IsNullOrEmpty(boneName);

            List<MeshEditTools.VertexSelection> selection;
            if (hasSphere)
                selection = MeshEditTools.SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, 0f);
            else if (hasBone)
                selection = MeshEditTools.SelectByBone(ctx, boneName, 0.01f);
            else
            {
                selection = new List<MeshEditTools.VertexSelection>();
                for (int i = 0; i < ctx.worldVertices.Length; i++)
                    selection.Add(new MeshEditTools.VertexSelection { index = i, weight = 1f });
            }

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            selection.Sort((a, b) => b.weight.CompareTo(a.weight));

            int total = selection.Count;
            if (maxResults > 0 && selection.Count > maxResults)
                selection = selection.GetRange(0, maxResults);

            var sb = new StringBuilder();
            sb.AppendLine($"Bone weights on '{gameObjectName}' ({total} matched, showing {selection.Count}):");

            foreach (var sel in selection)
            {
                var bw = ctx.boneWeights[sel.index];
                var wp = ctx.worldVertices[sel.index];
                sb.Append($"  [{sel.index}] pos=({wp.x:F4},{wp.y:F4},{wp.z:F4}): ");

                var influences = new List<string>();
                if (bw.weight0 > 0.001f && bw.boneIndex0 < ctx.bones.Length && ctx.bones[bw.boneIndex0] != null)
                    influences.Add($"{ctx.bones[bw.boneIndex0].name}={bw.weight0:F3}");
                if (bw.weight1 > 0.001f && bw.boneIndex1 < ctx.bones.Length && ctx.bones[bw.boneIndex1] != null)
                    influences.Add($"{ctx.bones[bw.boneIndex1].name}={bw.weight1:F3}");
                if (bw.weight2 > 0.001f && bw.boneIndex2 < ctx.bones.Length && ctx.bones[bw.boneIndex2] != null)
                    influences.Add($"{ctx.bones[bw.boneIndex2].name}={bw.weight2:F3}");
                if (bw.weight3 > 0.001f && bw.boneIndex3 < ctx.bones.Length && ctx.bones[bw.boneIndex3] != null)
                    influences.Add($"{ctx.bones[bw.boneIndex3].name}={bw.weight3:F3}");

                sb.AppendLine(influences.Count > 0 ? string.Join(", ", influences) : "(no weights)");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: ListBoneInfluence ==========

        [AgentTool("List all bones and their influence summary on a SkinnedMeshRenderer. Shows vertex count, average and max weight per bone.")]
        public static string ListBoneInfluence(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";
            if (smr.sharedMesh == null) return "Error: No mesh assigned.";

            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            var boneWeights = mesh.boneWeights;

            // Accumulate stats per bone
            var stats = new Dictionary<int, BoneStats>();

            for (int vi = 0; vi < boneWeights.Length; vi++)
            {
                var bw = boneWeights[vi];
                AccumulateBoneStat(stats, bw.boneIndex0, bw.weight0);
                AccumulateBoneStat(stats, bw.boneIndex1, bw.weight1);
                AccumulateBoneStat(stats, bw.boneIndex2, bw.weight2);
                AccumulateBoneStat(stats, bw.boneIndex3, bw.weight3);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Bone influence on '{gameObjectName}' ({mesh.vertexCount} vertices, {bones.Length} bones):");
            sb.AppendLine($"{"Bone",-35} {"Verts",6} {"AvgW",6} {"MaxW",6}");
            sb.AppendLine(new string('-', 56));

            var sorted = stats
                .Where(kv => kv.Key >= 0 && kv.Key < bones.Length && bones[kv.Key] != null)
                .OrderByDescending(kv => kv.Value.vertexCount);

            foreach (var kv in sorted)
            {
                var s = kv.Value;
                float avg = s.vertexCount > 0 ? s.totalWeight / s.vertexCount : 0f;
                sb.AppendLine($"{bones[kv.Key].name,-35} {s.vertexCount,6} {avg,6:F3} {s.maxWeight,6:F3}");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: SetBoneWeight ==========

        [AgentTool("Set weight for a specific bone within a sphere region. Automatically normalizes weights. falloff: 0=hard, 1=smooth.")]
        public static string SetBoneWeight(
            string gameObjectName,
            string boneName,
            float weight,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            float falloff = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";
            if (!ctx.isSkinned)
                return $"Error: '{gameObjectName}' is not a SkinnedMeshRenderer.";

            int boneIdx = MeshEditTools.FindBoneIndex(ctx.bones, boneName);
            if (boneIdx < 0)
                return $"Error: Bone '{boneName}' not found on '{gameObjectName}'.";

            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            List<MeshEditTools.VertexSelection> selection;

            if (hasSphere)
                selection = MeshEditTools.SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, falloff);
            else
            {
                // Apply to all vertices
                selection = new List<MeshEditTools.VertexSelection>();
                for (int i = 0; i < ctx.worldVertices.Length; i++)
                    selection.Add(new MeshEditTools.VertexSelection { index = i, weight = 1f });
            }

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            var newWeights = (BoneWeight[])ctx.boneWeights.Clone();

            foreach (var sel in selection)
            {
                float targetW = Mathf.Lerp(MeshEditTools.GetBoneInfluence(newWeights[sel.index], boneIdx), weight, sel.weight);
                var dict = BoneWeightToDict(newWeights[sel.index]);
                dict[boneIdx] = targetW;
                newWeights[sel.index] = PackTopFourWeights(dict);
            }

            string result = ApplyWeightEdit(ctx, newWeights, "SetBoneWeight");
            return $"Set bone '{boneName}' weight to {weight:F3} for {selection.Count} vertices. {result}";
        }

        // ========== Tool: TransferWeightBetweenBones ==========

        [AgentTool("Transfer weight from one bone to another within a sphere region. amount: fraction of fromBone's weight to move (0-1).")]
        public static string TransferWeightBetweenBones(
            string gameObjectName,
            string fromBoneName,
            string toBoneName,
            float amount = 1f,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";
            if (!ctx.isSkinned)
                return $"Error: '{gameObjectName}' is not a SkinnedMeshRenderer.";

            int fromIdx = MeshEditTools.FindBoneIndex(ctx.bones, fromBoneName);
            if (fromIdx < 0) return $"Error: Bone '{fromBoneName}' not found.";
            int toIdx = MeshEditTools.FindBoneIndex(ctx.bones, toBoneName);
            if (toIdx < 0) return $"Error: Bone '{toBoneName}' not found.";

            amount = Mathf.Clamp01(amount);

            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            List<MeshEditTools.VertexSelection> selection;

            if (hasSphere)
                selection = MeshEditTools.SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, 0f);
            else
                selection = MeshEditTools.SelectByBone(ctx, fromBoneName, 0.01f);

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            var newWeights = (BoneWeight[])ctx.boneWeights.Clone();
            int transferred = 0;

            foreach (var sel in selection)
            {
                var dict = BoneWeightToDict(newWeights[sel.index]);
                float fromW = 0f;
                if (dict.TryGetValue(fromIdx, out fromW) && fromW > 0.001f)
                {
                    float move = fromW * amount;
                    dict[fromIdx] = fromW - move;
                    float toW = 0f;
                    dict.TryGetValue(toIdx, out toW);
                    dict[toIdx] = toW + move;
                    newWeights[sel.index] = PackTopFourWeights(dict);
                    transferred++;
                }
            }

            string result = ApplyWeightEdit(ctx, newWeights, "TransferWeight");
            return $"Transferred {amount:P0} weight from '{fromBoneName}' to '{toBoneName}' for {transferred} vertices. {result}";
        }

        // ========== Tool: SmoothBoneWeights ==========

        [AgentTool("Smooth bone weights using Laplacian averaging within a sphere or bone region. Reduces sharp weight transitions.")]
        public static string SmoothBoneWeights(
            string gameObjectName,
            float centerX = float.NaN, float centerY = float.NaN, float centerZ = float.NaN,
            float radius = 0.05f,
            string boneName = "",
            int iterations = 3)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";
            if (!ctx.isSkinned)
                return $"Error: '{gameObjectName}' is not a SkinnedMeshRenderer.";

            bool hasSphere = !float.IsNaN(centerX) && !float.IsNaN(centerY) && !float.IsNaN(centerZ);
            bool hasBone = !string.IsNullOrEmpty(boneName);

            List<MeshEditTools.VertexSelection> selection;
            if (hasSphere)
                selection = MeshEditTools.SelectBySphere(ctx, new Vector3(centerX, centerY, centerZ), radius, 0f);
            else if (hasBone)
                selection = MeshEditTools.SelectByBone(ctx, boneName, 0.01f);
            else
                return "Error: Specify either sphere center or boneName for weight smoothing.";

            if (selection.Count == 0)
                return "No vertices matched the selection criteria.";

            var selectedSet = new HashSet<int>(selection.Select(s => s.index));
            var adjacency = BuildSimpleAdjacency(ctx.mesh.vertices.Length, ctx.mesh.triangles);
            var newWeights = (BoneWeight[])ctx.boneWeights.Clone();

            for (int iter = 0; iter < iterations; iter++)
            {
                ToolProgress.Report((float)iter / iterations, $"Smoothing weights pass {iter + 1}/{iterations}");
                var smoothed = (BoneWeight[])newWeights.Clone();

                foreach (var sel in selection)
                {
                    int vi = sel.index;
                    var neighbors = adjacency[vi];
                    if (neighbors.Count == 0) continue;

                    // Average bone weights from neighbors
                    var avgDict = new Dictionary<int, float>();
                    int count = 0;
                    foreach (int ni in neighbors)
                    {
                        var nbw = newWeights[ni];
                        AccumWeightDict(avgDict, nbw.boneIndex0, nbw.weight0);
                        AccumWeightDict(avgDict, nbw.boneIndex1, nbw.weight1);
                        AccumWeightDict(avgDict, nbw.boneIndex2, nbw.weight2);
                        AccumWeightDict(avgDict, nbw.boneIndex3, nbw.weight3);
                        count++;
                    }

                    if (count > 0)
                    {
                        foreach (var key in avgDict.Keys.ToList())
                            avgDict[key] /= count;

                        // Blend: 50% original + 50% neighbor average
                        var origDict = BoneWeightToDict(newWeights[vi]);
                        var blendDict = new Dictionary<int, float>();
                        var allKeys = new HashSet<int>(origDict.Keys);
                        foreach (var k in avgDict.Keys) allKeys.Add(k);

                        foreach (int k in allKeys)
                        {
                            float origW = 0f, avgW = 0f;
                            origDict.TryGetValue(k, out origW);
                            avgDict.TryGetValue(k, out avgW);
                            blendDict[k] = origW * 0.5f + avgW * 0.5f;
                        }

                        smoothed[vi] = PackTopFourWeights(blendDict);
                    }
                }

                newWeights = smoothed;
            }

            ToolProgress.Clear();
            string result = ApplyWeightEdit(ctx, newWeights, "SmoothBoneWeights");
            return $"Smoothed weights for {selection.Count} vertices ({iterations} iterations). {result}";
        }

        // ========== Tool: NormalizeBoneWeights ==========

        [AgentTool("Normalize all bone weights so each vertex sums to 1.0. Fixes broken weights after manual editing.")]
        public static string NormalizeBoneWeights(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx))
                return $"Error: No mesh found on '{gameObjectName}'.";
            if (!ctx.isSkinned)
                return $"Error: '{gameObjectName}' is not a SkinnedMeshRenderer.";

            var newWeights = (BoneWeight[])ctx.boneWeights.Clone();
            int fixed_count = 0;

            for (int i = 0; i < newWeights.Length; i++)
            {
                var bw = newWeights[i];
                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                if (sum < 0.999f || sum > 1.001f)
                {
                    if (sum > 0.0001f)
                    {
                        float inv = 1f / sum;
                        bw.weight0 *= inv;
                        bw.weight1 *= inv;
                        bw.weight2 *= inv;
                        bw.weight3 *= inv;
                        newWeights[i] = bw;
                    }
                    fixed_count++;
                }
            }

            if (fixed_count == 0)
                return $"All {newWeights.Length} vertices already normalized.";

            string result = ApplyWeightEdit(ctx, newWeights, "NormalizeBoneWeights");
            return $"Normalized {fixed_count} / {newWeights.Length} vertices. {result}";
        }

        // ========== Internal Helpers ==========

        private struct BoneStats
        {
            public int vertexCount;
            public float totalWeight;
            public float maxWeight;
        }

        private static void AccumulateBoneStat(Dictionary<int, BoneStats> stats, int boneIdx, float weight)
        {
            if (weight < 0.001f) return;
            BoneStats s;
            if (!stats.TryGetValue(boneIdx, out s))
                s = new BoneStats();
            s.vertexCount++;
            s.totalWeight += weight;
            if (weight > s.maxWeight) s.maxWeight = weight;
            stats[boneIdx] = s;
        }

        public static Dictionary<int, float> BoneWeightToDict(BoneWeight bw)
        {
            var dict = new Dictionary<int, float>();
            if (bw.weight0 > 0.0001f) dict[bw.boneIndex0] = bw.weight0;
            if (bw.weight1 > 0.0001f)
            {
                if (dict.ContainsKey(bw.boneIndex1)) dict[bw.boneIndex1] += bw.weight1;
                else dict[bw.boneIndex1] = bw.weight1;
            }
            if (bw.weight2 > 0.0001f)
            {
                if (dict.ContainsKey(bw.boneIndex2)) dict[bw.boneIndex2] += bw.weight2;
                else dict[bw.boneIndex2] = bw.weight2;
            }
            if (bw.weight3 > 0.0001f)
            {
                if (dict.ContainsKey(bw.boneIndex3)) dict[bw.boneIndex3] += bw.weight3;
                else dict[bw.boneIndex3] = bw.weight3;
            }
            return dict;
        }

        public static BoneWeight PackTopFourWeights(Dictionary<int, float> dict)
        {
            var sorted = dict
                .Where(kv => kv.Value > 0.0001f)
                .OrderByDescending(kv => kv.Value)
                .Take(4)
                .ToArray();

            float sum = 0f;
            foreach (var kv in sorted) sum += kv.Value;
            if (sum < 0.0001f) sum = 1f;
            float inv = 1f / sum;

            var bw = new BoneWeight();
            if (sorted.Length > 0) { bw.boneIndex0 = sorted[0].Key; bw.weight0 = sorted[0].Value * inv; }
            if (sorted.Length > 1) { bw.boneIndex1 = sorted[1].Key; bw.weight1 = sorted[1].Value * inv; }
            if (sorted.Length > 2) { bw.boneIndex2 = sorted[2].Key; bw.weight2 = sorted[2].Value * inv; }
            if (sorted.Length > 3) { bw.boneIndex3 = sorted[3].Key; bw.weight3 = sorted[3].Value * inv; }
            return bw;
        }

        internal static List<int>[] BuildSimpleAdjacency(int vertexCount, int[] triangles)
        {
            return MeshEditTools.BuildAdjacency(vertexCount, triangles);
        }

        private static void AccumWeightDict(Dictionary<int, float> dict, int boneIdx, float weight)
        {
            if (weight < 0.001f) return;
            if (dict.ContainsKey(boneIdx)) dict[boneIdx] += weight;
            else dict[boneIdx] = weight;
        }

        private static string ApplyWeightEdit(MeshEditTools.MeshContext ctx, BoneWeight[] newWeights, string undoName)
        {
            ToolUtility.EnsureAssetDirectory(GeneratedDir);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"WeightEdit: {undoName}");

            var newMesh = Object.Instantiate(ctx.mesh);
            newMesh.name = $"{ctx.mesh.name}_weightEdited";
            newMesh.boneWeights = newWeights;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);

            Undo.RecordObject(ctx.smr, $"WeightEdit: {undoName}");
            ctx.smr.sharedMesh = newMesh;

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            return $"Saved: {assetPath}";
        }
    }
}
