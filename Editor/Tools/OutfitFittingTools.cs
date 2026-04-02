using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.Fitting;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class OutfitFittingTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("OutfitFitting");

        // Weight transfer settings (set from UI before calling)
        public static int WeightKNearest = 8;
        public static float WeightBlend = 1f;
        public static float WeightMaxDistance = 0f;

        // ========== Tool: AnalyzeOutfitCompatibility ==========

        [AgentTool("Analyze outfit-avatar compatibility. Compares bone structures, proportions, and reports compatibility issues for non-matching outfits.")]
        public static string AnalyzeOutfitCompatibility(string outfitName, string avatarName)
        {
            var outfitGo = FindGO(outfitName);
            if (outfitGo == null) return $"Error: Outfit '{outfitName}' not found.";
            var avatarGo = FindGO(avatarName);
            if (avatarGo == null) return $"Error: Avatar '{avatarName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Outfit Compatibility Analysis ===");
            sb.AppendLine($"Outfit: {outfitGo.name}");
            sb.AppendLine($"Avatar: {avatarGo.name}");

            var outfitBones = FittingBoneMap.CollectBones(outfitGo.transform);
            var avatarBones = FittingBoneMap.CollectAvatarBones(avatarGo.transform);
            sb.AppendLine($"\nBone Count: Outfit={outfitBones.Count}, Avatar={avatarBones.Count}");

            var outfitAnimator = outfitGo.GetComponent<Animator>();
            var avatarAnimator = avatarGo.GetComponent<Animator>();
            bool outfitHumanoid = outfitAnimator != null && outfitAnimator.isHuman;
            bool avatarHumanoid = avatarAnimator != null && avatarAnimator.isHuman;
            sb.AppendLine($"Humanoid: Outfit={outfitHumanoid}, Avatar={avatarHumanoid}");

            var avatarBoneNames = new HashSet<string>(avatarBones.Select(b => b.name));
            int exactMatch = 0, caseMatch = 0, strippedMatch = 0, noMatch = 0;
            var unmatchedBones = new List<string>();
            foreach (var bone in outfitBones)
            {
                if (avatarBoneNames.Contains(bone.name))
                    exactMatch++;
                else if (avatarBones.Any(a => string.Equals(a.name, bone.name, System.StringComparison.OrdinalIgnoreCase)))
                    caseMatch++;
                else if (avatarBones.Any(a => FittingBoneMap.StripBoneName(a.name) == FittingBoneMap.StripBoneName(bone.name) && FittingBoneMap.StripBoneName(bone.name).Length > 0))
                    strippedMatch++;
                else
                {
                    noMatch++;
                    unmatchedBones.Add(bone.name);
                }
            }

            sb.AppendLine($"\nBone Matching Summary:");
            sb.AppendLine($"  Exact match: {exactMatch}");
            sb.AppendLine($"  Case-insensitive match: {caseMatch}");
            sb.AppendLine($"  Stripped name match: {strippedMatch}");
            sb.AppendLine($"  No match: {noMatch}");

            if (unmatchedBones.Count > 0)
            {
                sb.AppendLine($"\nUnmatched Outfit Bones ({Mathf.Min(unmatchedBones.Count, 20)} shown):");
                foreach (var name in unmatchedBones.Take(20))
                    sb.AppendLine($"  - {name}");
            }

            var outfitArmature = FittingBoneMap.FindArmature(outfitGo.transform);
            var avatarArmature = FittingBoneMap.FindArmature(avatarGo.transform);
            if (outfitArmature != null && avatarArmature != null)
            {
                float outfitHeight = FittingBoneMap.MeasureArmatureHeight(outfitArmature);
                float avatarHeight = FittingBoneMap.MeasureArmatureHeight(avatarArmature);
                float ratio = avatarHeight > 0.001f ? outfitHeight / avatarHeight : 0f;
                sb.AppendLine($"\nProportion:");
                sb.AppendLine($"  Outfit Armature Height: {outfitHeight:F4}");
                sb.AppendLine($"  Avatar Armature Height: {avatarHeight:F4}");
                sb.AppendLine($"  Scale Ratio: {ratio:F3}");
                if (ratio < 0.8f || ratio > 1.2f)
                    sb.AppendLine($"  WARNING: Significant size difference detected.");
            }

            var outfitSMRs = outfitGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            sb.AppendLine($"\nOutfit SkinnedMeshRenderers: {outfitSMRs.Length}");
            foreach (var smr in outfitSMRs)
                sb.AppendLine($"  - {smr.name} (vertices: {(smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)})");

            int totalOutfit = outfitBones.Count;
            int matched = exactMatch + caseMatch + strippedMatch;
            float compatibility = totalOutfit > 0 ? (float)matched / totalOutfit * 100f : 0f;
            sb.AppendLine($"\nOverall Compatibility: {compatibility:F1}%");
            if (compatibility >= 80f)
                sb.AppendLine("Recommendation: Good compatibility. RetargetOutfit should work well.");
            else if (compatibility >= 50f)
                sb.AppendLine("Recommendation: Moderate compatibility. RetargetOutfit with adaptStrength=1.0 recommended.");
            else
                sb.AppendLine("Recommendation: Low compatibility. Manual bone mapping review recommended after MapOutfitBones.");

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: MapOutfitBones ==========

        [AgentTool("Generate automatic bone mapping between outfit and avatar. Shows mapping table with confidence scores.")]
        public static string MapOutfitBones(string outfitName, string avatarName)
        {
            var outfitGo = FindGO(outfitName);
            if (outfitGo == null) return $"Error: Outfit '{outfitName}' not found.";
            var avatarGo = FindGO(avatarName);
            if (avatarGo == null) return $"Error: Avatar '{avatarName}' not found.";

            var mapping = FittingBoneMap.BuildBoneMapping(outfitGo.transform, avatarGo.transform);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Bone Mapping: {outfitGo.name} → {avatarGo.name} ===");
            sb.AppendLine($"Total mappings: {mapping.Count}");
            sb.AppendLine();
            sb.AppendLine($"{"Outfit Bone",-35} {"Avatar Bone",-35} {"Conf",5} {"Method",-20}");
            sb.AppendLine(new string('-', 100));

            foreach (var entry in mapping.OrderByDescending(e => e.confidence))
            {
                string outfitPath = FittingBoneMap.GetShortPath(entry.outfitBone, outfitGo.transform);
                string avatarPath = entry.avatarBone != null ? FittingBoneMap.GetShortPath(entry.avatarBone, avatarGo.transform) : "(unmapped)";
                sb.AppendLine($"{outfitPath,-35} {avatarPath,-35} {entry.confidence,5:F2} {entry.method,-20}");
            }

            int unmapped = mapping.Count(e => e.avatarBone == null);
            if (unmapped > 0)
                sb.AppendLine($"\nWARNING: {unmapped} bones could not be mapped.");

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: RetargetOutfit ==========

        [AgentTool("Retarget an outfit to a different avatar. Uses bone alignment + ARAP fitting (optionally with Green Coordinates pre-pass and XPBD draping). adaptStrength 0-1 controls bone alignment strength.")]
        public static string RetargetOutfit(string outfitName, string avatarName,
            float adaptStrength = 1.0f,
            bool useGreenCoordinates = false,
            bool useXPBD = false)
        {
            var outfitGo = FindGO(outfitName);
            if (outfitGo == null) return $"Error: Outfit '{outfitName}' not found.";
            var avatarGo = FindGO(avatarName);
            if (avatarGo == null) return $"Error: Avatar '{avatarName}' not found.";

            var smrs = outfitGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0) return $"Error: No SkinnedMeshRenderer found on '{outfitName}'.";

            var mapping = FittingBoneMap.BuildBoneMapping(outfitGo.transform, avatarGo.transform);

            var bodyMeshes = FittingHelpers.FindBodyMeshes(avatarGo, outfitGo);
            if (bodyMeshes.Count == 0) return "Error: Body mesh not found on avatar.";

            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            int undoGroup = Undo.GetCurrentGroup();
            var sb = new StringBuilder();
            sb.AppendLine($"=== RetargetOutfit: {outfitGo.name} → {avatarGo.name} ===");

            // Phase 1: Preparation
            FittingHelpers.BakeWorldVerticesAndNormals(bodyMeshes, out var bodyVerts, out var bodyNormals);
            float cellSize = SpatialGrid.EstimateCellSize(bodyVerts);
            var bodyGrid = new SpatialGrid(bodyVerts, cellSize);
            var bodySDF = new BodySDF(bodyVerts, bodyNormals, bodyGrid, blurRadius: 3);

            var prepSmrs = new List<SkinnedMeshRenderer>();
            var prepOrigVerts = new List<Vector3[]>();

            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var origVerts = baked.vertices;
                for (int i = 0; i < origVerts.Length; i++)
                    origVerts[i] = smr.transform.TransformPoint(origVerts[i]);
                Object.DestroyImmediate(baked);
                prepSmrs.Add(smr);
                prepOrigVerts.Add(origVerts);
            }

            sb.AppendLine($"  Phase 1 - Preparation: {prepSmrs.Count} meshes, {bodyVerts.Length} body verts");

            // Phase 2: Bone Alignment
            var alignTargets = mapping
                .Where(e => e.outfitBone != null && e.avatarBone != null && e.confidence >= 0.5f)
                .OrderBy(e => FittingBoneMap.GetHierarchyDepth(e.outfitBone))
                .ToList();

            int alignedCount = 0;
            float maxPosDelta = 0f;
            float maxRotDelta = 0f;

            foreach (var entry in alignTargets)
            {
                Undo.RecordObject(entry.outfitBone, "RetargetOutfit");

                Vector3 oldPos = entry.outfitBone.position;
                Quaternion oldRot = entry.outfitBone.rotation;

                entry.outfitBone.position = Vector3.Lerp(oldPos, entry.avatarBone.position, adaptStrength);
                entry.outfitBone.rotation = Quaternion.Slerp(oldRot, entry.avatarBone.rotation, adaptStrength);

                float posDelta = Vector3.Distance(oldPos, entry.outfitBone.position);
                float rotDelta = Quaternion.Angle(oldRot, entry.outfitBone.rotation);
                if (posDelta > maxPosDelta) maxPosDelta = posDelta;
                if (rotDelta > maxRotDelta) maxRotDelta = rotDelta;
                alignedCount++;
            }

            sb.AppendLine($"  Phase 2 - Bone Alignment: {alignedCount} bones (maxPosDelta={maxPosDelta:F4}m, maxRotDelta={maxRotDelta:F1}°)");

            // Phase 3: Pipeline fitting (GC + ARAP + optional XPBD)
            var pipeline = new FittingPipeline();
            int meshCount = 0;
            for (int si = 0; si < prepSmrs.Count; si++)
            {
                var smr = prepSmrs[si];
                var origVerts = prepOrigVerts[si];

                var baked = new Mesh();
                smr.BakeMesh(baked);
                var workVerts = baked.vertices;
                for (int i = 0; i < workVerts.Length; i++)
                    workVerts[i] = smr.transform.TransformPoint(workVerts[i]);
                Object.DestroyImmediate(baked);

                var topo = FittingTopology.Build(workVerts, origVerts, smr.sharedMesh.triangles);

                string stagesLog = pipeline.Execute(workVerts, topo, bodySDF, bodyGrid, bodyVerts, bodyNormals,
                    smr.name, meshCount, prepSmrs.Count, useGreenCoordinates, useXPBD);

                // Phase 4: Bake Results
                for (int i = 0; i < workVerts.Length; i++)
                    workVerts[i] = smr.transform.InverseTransformPoint(workVerts[i]);

                var newMesh = Object.Instantiate(smr.sharedMesh);
                newMesh.name = $"{smr.sharedMesh.name}_retargeted";
                newMesh.vertices = workVerts;

                var bones = smr.bones;
                var oldBindPoses = smr.sharedMesh.bindposes;
                var newBindPoses = new Matrix4x4[oldBindPoses.Length];
                for (int i = 0; i < newBindPoses.Length; i++)
                {
                    if (i < bones.Length && bones[i] != null)
                        newBindPoses[i] = bones[i].worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    else
                        newBindPoses[i] = oldBindPoses[i];
                }
                newMesh.bindposes = newBindPoses;

                newMesh.RecalculateNormals();
                newMesh.RecalculateBounds();

                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedDir}/{newMesh.name}.asset");
                AssetDatabase.CreateAsset(newMesh, assetPath);

                Undo.RecordObject(smr, "RetargetOutfit");
                smr.sharedMesh = newMesh;

                sb.AppendLine($"  {smr.name}: {workVerts.Length} verts, {stagesLog} → {assetPath}");
                meshCount++;
            }

            sb.AppendLine($"  Phase 3+4 - Pipeline Fitting + Bake: {meshCount} meshes");

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            ToolProgress.Clear();

            sb.AppendLine($"\nadaptStrength={adaptStrength:F1}, GC={useGreenCoordinates}, XPBD={useXPBD}");
            sb.AppendLine("Undo available via Edit > Undo.");
            return sb.ToString().TrimEnd();
        }

        // ========== Tool: TransferOutfitWeights ==========

        [AgentTool("Transfer bone weights from avatar body mesh to outfit mesh. Improves joint deformation for retargeted outfits. Uses barycentric coordinate interpolation on nearest triangles.")]
        public static string TransferOutfitWeights(string outfitMeshName, string avatarBodyMeshName)
        {
            var outfitGo = FindGO(outfitMeshName);
            if (outfitGo == null) return $"Error: Outfit mesh '{outfitMeshName}' not found.";
            var avatarGo = FindGO(avatarBodyMeshName);
            if (avatarGo == null) return $"Error: Avatar body mesh '{avatarBodyMeshName}' not found.";

            var outfitSmr = outfitGo.GetComponent<SkinnedMeshRenderer>();
            if (outfitSmr == null) return $"Error: No SkinnedMeshRenderer on '{outfitMeshName}'.";
            var avatarSmr = avatarGo.GetComponent<SkinnedMeshRenderer>();
            if (avatarSmr == null) return $"Error: No SkinnedMeshRenderer on '{avatarBodyMeshName}'.";

            if (outfitSmr.sharedMesh == null) return "Error: Outfit has no mesh.";
            if (avatarSmr.sharedMesh == null) return "Error: Avatar body has no mesh.";

            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            int undoGroup = Undo.GetCurrentGroup();

            var avatarBaked = new Mesh();
            avatarSmr.BakeMesh(avatarBaked);
            var avatarVerts = avatarBaked.vertices;
            for (int i = 0; i < avatarVerts.Length; i++)
                avatarVerts[i] = avatarSmr.transform.TransformPoint(avatarVerts[i]);
            Object.DestroyImmediate(avatarBaked);

            var outfitBaked = new Mesh();
            outfitSmr.BakeMesh(outfitBaked);
            var outfitVerts = outfitBaked.vertices;
            for (int i = 0; i < outfitVerts.Length; i++)
                outfitVerts[i] = outfitSmr.transform.TransformPoint(outfitVerts[i]);
            Object.DestroyImmediate(outfitBaked);

            float cellSize = SpatialGrid.EstimateCellSize(avatarVerts);
            var grid = new SpatialGrid(avatarVerts, cellSize);
            var avatarWeights = avatarSmr.sharedMesh.boneWeights;
            var avatarBones = avatarSmr.bones;
            var outfitBones = outfitSmr.bones;

            var outfitBoneByName = new Dictionary<string, int>();
            for (int i = 0; i < outfitBones.Length; i++)
            {
                if (outfitBones[i] != null)
                    outfitBoneByName[outfitBones[i].name] = i;
            }

            var avatarTris = avatarSmr.sharedMesh.triangles;
            var vertToTris = new List<int>[avatarVerts.Length];
            for (int i = 0; i < avatarVerts.Length; i++)
                vertToTris[i] = new List<int>();
            for (int ti = 0; ti < avatarTris.Length; ti += 3)
            {
                int triIdx = ti / 3;
                if (avatarTris[ti] < vertToTris.Length)
                    vertToTris[avatarTris[ti]].Add(triIdx);
                if (avatarTris[ti + 1] < vertToTris.Length)
                    vertToTris[avatarTris[ti + 1]].Add(triIdx);
                if (avatarTris[ti + 2] < vertToTris.Length)
                    vertToTris[avatarTris[ti + 2]].Add(triIdx);
            }

            var newMesh = Object.Instantiate(outfitSmr.sharedMesh);
            newMesh.name = $"{outfitSmr.sharedMesh.name}_weightTransfer";
            var newWeights = new BoneWeight[outfitVerts.Length];

            int k = WeightKNearest;
            float maxDist = WeightMaxDistance;
            float blend = Mathf.Clamp01(WeightBlend);
            var originalWeightsForBlend = blend < 0.999f ? outfitSmr.sharedMesh.boneWeights : null;

            for (int vi = 0; vi < outfitVerts.Length; vi++)
            {
                if (vi % 1000 == 0)
                    ToolProgress.Report((float)vi / outfitVerts.Length, $"Transferring weights... {vi}/{outfitVerts.Length}");

                grid.FindKNearest(outfitVerts[vi], k, out var indices, out var distances);

                var candidateTris = new HashSet<int>();
                for (int ni = 0; ni < indices.Length; ni++)
                {
                    int avatarVertIdx = indices[ni];
                    if (avatarVertIdx < vertToTris.Length)
                    {
                        foreach (int triIdx in vertToTris[avatarVertIdx])
                            candidateTris.Add(triIdx);
                    }
                }

                float bestDist = float.MaxValue;
                float bestU = 1f, bestV = 0f, bestW = 0f;
                int bestTri = -1;

                foreach (int triIdx in candidateTris)
                {
                    int i0 = avatarTris[triIdx * 3];
                    int i1 = avatarTris[triIdx * 3 + 1];
                    int i2 = avatarTris[triIdx * 3 + 2];
                    float dist = FittingHelpers.ProjectOntoTriangle(outfitVerts[vi],
                        avatarVerts[i0], avatarVerts[i1], avatarVerts[i2],
                        out float pu, out float pv, out float pw);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestU = pu; bestV = pv; bestW = pw;
                        bestTri = triIdx;
                    }
                }

                if (bestTri >= 0)
                {
                    int i0 = avatarTris[bestTri * 3];
                    int i1 = avatarTris[bestTri * 3 + 1];
                    int i2 = avatarTris[bestTri * 3 + 2];
                    newWeights[vi] = FittingHelpers.InterpolateBoneWeights(
                        avatarWeights[i0], avatarWeights[i1], avatarWeights[i2],
                        bestU, bestV, bestW,
                        avatarBones, outfitBoneByName);
                }
                else if (indices.Length > 0)
                {
                    int nearestIdx = indices[0];
                    if (nearestIdx < avatarWeights.Length)
                    {
                        var accum = new Dictionary<int, float>();
                        var aw = avatarWeights[nearestIdx];
                        FittingHelpers.AccumulateWeight(accum, avatarBones, outfitBoneByName, aw.boneIndex0, aw.weight0);
                        FittingHelpers.AccumulateWeight(accum, avatarBones, outfitBoneByName, aw.boneIndex1, aw.weight1);
                        FittingHelpers.AccumulateWeight(accum, avatarBones, outfitBoneByName, aw.boneIndex2, aw.weight2);
                        FittingHelpers.AccumulateWeight(accum, avatarBones, outfitBoneByName, aw.boneIndex3, aw.weight3);

                        var sorted = accum.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                        float sum = sorted.Sum(kv => kv.Value);
                        if (sum < 0.0001f) sum = 1f;

                        var bw = new BoneWeight();
                        if (sorted.Length > 0) { bw.boneIndex0 = sorted[0].Key; bw.weight0 = sorted[0].Value / sum; }
                        if (sorted.Length > 1) { bw.boneIndex1 = sorted[1].Key; bw.weight1 = sorted[1].Value / sum; }
                        if (sorted.Length > 2) { bw.boneIndex2 = sorted[2].Key; bw.weight2 = sorted[2].Value / sum; }
                        if (sorted.Length > 3) { bw.boneIndex3 = sorted[3].Key; bw.weight3 = sorted[3].Value / sum; }
                        newWeights[vi] = bw;
                    }
                }
            }

            // Distance threshold: vertices farther than maxDist keep original weights
            var originalWeights = outfitSmr.sharedMesh.boneWeights;
            int distSkipCount = 0;
            if (maxDist > 0.0001f)
            {
                var bodyGrid2 = new SpatialGrid(avatarVerts, cellSize);
                for (int vi = 0; vi < outfitVerts.Length; vi++)
                {
                    bodyGrid2.FindKNearest(outfitVerts[vi], 1, out _, out var dists);
                    if (dists.Length > 0 && dists[0] > maxDist && vi < originalWeights.Length)
                    {
                        newWeights[vi] = originalWeights[vi];
                        distSkipCount++;
                    }
                }
            }

            // Blend: mix transferred weights with original weights
            int blendCount = 0;
            if (originalWeightsForBlend != null)
            {
                for (int vi = 0; vi < newWeights.Length; vi++)
                {
                    if (vi >= originalWeightsForBlend.Length) break;
                    var orig = originalWeightsForBlend[vi];
                    var xfer = newWeights[vi];

                    // Blend by accumulating both into a single dictionary
                    var accum = new Dictionary<int, float>();
                    void Add(int idx, float w) { if (w < 0.0001f) return; accum[idx] = accum.TryGetValue(idx, out float e) ? e + w : w; }
                    Add(xfer.boneIndex0, xfer.weight0 * blend);
                    Add(xfer.boneIndex1, xfer.weight1 * blend);
                    Add(xfer.boneIndex2, xfer.weight2 * blend);
                    Add(xfer.boneIndex3, xfer.weight3 * blend);
                    float origBlend = 1f - blend;
                    Add(orig.boneIndex0, orig.weight0 * origBlend);
                    Add(orig.boneIndex1, orig.weight1 * origBlend);
                    Add(orig.boneIndex2, orig.weight2 * origBlend);
                    Add(orig.boneIndex3, orig.weight3 * origBlend);

                    var sorted = accum.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                    float sum = sorted.Sum(kv => kv.Value);
                    if (sum < 0.0001f) sum = 1f;

                    var bw = new BoneWeight();
                    if (sorted.Length > 0) { bw.boneIndex0 = sorted[0].Key; bw.weight0 = sorted[0].Value / sum; }
                    if (sorted.Length > 1) { bw.boneIndex1 = sorted[1].Key; bw.weight1 = sorted[1].Value / sum; }
                    if (sorted.Length > 2) { bw.boneIndex2 = sorted[2].Key; bw.weight2 = sorted[2].Value / sum; }
                    if (sorted.Length > 3) { bw.boneIndex3 = sorted[3].Key; bw.weight3 = sorted[3].Value / sum; }
                    newWeights[vi] = bw;
                    blendCount++;
                }
            }

            // Fallback: vertices with zero total weight keep original weights
            int fallbackCount = 0;
            for (int vi = 0; vi < newWeights.Length; vi++)
            {
                float totalW = newWeights[vi].weight0 + newWeights[vi].weight1 +
                               newWeights[vi].weight2 + newWeights[vi].weight3;
                if (totalW < 0.0001f && vi < originalWeights.Length)
                {
                    newWeights[vi] = originalWeights[vi];
                    fallbackCount++;
                }
            }

            newMesh.boneWeights = newWeights;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);

            Undo.RecordObject(outfitSmr, "TransferOutfitWeights");
            outfitSmr.sharedMesh = newMesh;

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            ToolProgress.Clear();

            var info = new List<string>();
            if (blend < 0.999f) info.Add($"blend={blend:P0}");
            if (distSkipCount > 0) info.Add($"{distSkipCount} dist-skipped");
            if (fallbackCount > 0) info.Add($"{fallbackCount} kept original");
            string extra = info.Count > 0 ? $" ({string.Join(", ", info)})" : "";
            return $"Weight transfer complete: {outfitVerts.Length} vertices processed{extra}.\nSaved to: {assetPath}";
        }

        // ========== Tool: DetectMeshPenetration ==========

        [AgentTool("Detect mesh penetration between outfit and avatar body. Reports penetrating vertices with depth information.")]
        public static string DetectMeshPenetration(string outfitMeshName, string bodyMeshName, float threshold = 0f)
        {
            var outfitGo = FindGO(outfitMeshName);
            if (outfitGo == null) return $"Error: Outfit mesh '{outfitMeshName}' not found.";
            var bodyGo = FindGO(bodyMeshName);
            if (bodyGo == null) return $"Error: Body mesh '{bodyMeshName}' not found.";

            var outfitSmr = outfitGo.GetComponent<SkinnedMeshRenderer>();
            var bodySmr = bodyGo.GetComponent<SkinnedMeshRenderer>();
            if (outfitSmr == null) return $"Error: No SkinnedMeshRenderer on '{outfitMeshName}'.";
            if (bodySmr == null) return $"Error: No SkinnedMeshRenderer on '{bodyMeshName}'.";
            if (outfitSmr.sharedMesh == null || bodySmr.sharedMesh == null)
                return "Error: One of the meshes is null.";

            var bodyBaked = new Mesh();
            bodySmr.BakeMesh(bodyBaked);
            var bodyVerts = bodyBaked.vertices;
            var bodyNormals = bodyBaked.normals;
            for (int i = 0; i < bodyVerts.Length; i++)
            {
                bodyVerts[i] = bodySmr.transform.TransformPoint(bodyVerts[i]);
                bodyNormals[i] = bodySmr.transform.TransformDirection(bodyNormals[i]).normalized;
            }

            var outfitBaked = new Mesh();
            outfitSmr.BakeMesh(outfitBaked);
            var outfitVerts = outfitBaked.vertices;
            for (int i = 0; i < outfitVerts.Length; i++)
                outfitVerts[i] = outfitSmr.transform.TransformPoint(outfitVerts[i]);

            float cellSize = SpatialGrid.EstimateCellSize(bodyVerts);
            var grid = new SpatialGrid(bodyVerts, cellSize);

            int penetratingCount = 0;
            float maxDepth = 0f;
            float totalDepth = 0f;
            var regionCounts = new Dictionary<string, int>();

            for (int vi = 0; vi < outfitVerts.Length; vi++)
            {
                if (vi % 2000 == 0)
                    ToolProgress.Report((float)vi / outfitVerts.Length, $"Detecting penetration... {vi}/{outfitVerts.Length}");

                grid.FindKNearest(outfitVerts[vi], 4, out var indices, out var distances);
                if (indices.Length == 0) continue;

                FittingHelpers.ComputeWeightedSurface(bodyVerts, bodyNormals, indices, distances,
                    out var surfacePoint, out var surfaceNormal);

                float signedDist = Vector3.Dot(outfitVerts[vi] - surfacePoint, surfaceNormal);

                if (signedDist < -threshold)
                {
                    float depth = -signedDist;
                    penetratingCount++;
                    totalDepth += depth;
                    if (depth > maxDepth) maxDepth = depth;

                    string region = FittingHelpers.ClassifyRegion(bodySmr.transform.InverseTransformPoint(surfacePoint));
                    if (!regionCounts.ContainsKey(region)) regionCounts[region] = 0;
                    regionCounts[region]++;
                }
            }

            ToolProgress.Clear();

            var sb = new StringBuilder();
            sb.AppendLine($"=== Mesh Penetration Report ===");
            sb.AppendLine($"Outfit: {outfitSmr.name} ({outfitVerts.Length} vertices)");
            sb.AppendLine($"Body: {bodySmr.name} ({bodyVerts.Length} vertices)");
            sb.AppendLine($"\nPenetrating vertices: {penetratingCount} / {outfitVerts.Length} ({(float)penetratingCount / outfitVerts.Length * 100f:F1}%)");

            if (penetratingCount > 0)
            {
                sb.AppendLine($"Max penetration depth: {maxDepth:F4}");
                sb.AppendLine($"Average penetration depth: {totalDepth / penetratingCount:F4}");
                sb.AppendLine($"\nPenetration by region:");
                foreach (var kv in regionCounts.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value} vertices");
            }
            else
            {
                sb.AppendLine("No penetration detected.");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: FixMeshPenetration ==========

        [AgentTool("Fix mesh penetration by pushing outfit vertices out along body normals. offset is extra padding in meters (default 0.001).")]
        public static string FixMeshPenetration(string outfitMeshName, string bodyMeshName, float offset = 0.001f)
        {
            var outfitGo = FindGO(outfitMeshName);
            if (outfitGo == null) return $"Error: Outfit mesh '{outfitMeshName}' not found.";
            var bodyGo = FindGO(bodyMeshName);
            if (bodyGo == null) return $"Error: Body mesh '{bodyMeshName}' not found.";

            var outfitSmr = outfitGo.GetComponent<SkinnedMeshRenderer>();
            var bodySmr = bodyGo.GetComponent<SkinnedMeshRenderer>();
            if (outfitSmr == null) return $"Error: No SkinnedMeshRenderer on '{outfitMeshName}'.";
            if (bodySmr == null) return $"Error: No SkinnedMeshRenderer on '{bodyMeshName}'.";
            if (outfitSmr.sharedMesh == null || bodySmr.sharedMesh == null)
                return "Error: One of the meshes is null.";

            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            int undoGroup = Undo.GetCurrentGroup();

            var bodyBaked = new Mesh();
            bodySmr.BakeMesh(bodyBaked);
            var bodyVertsWorld = bodyBaked.vertices;
            var bodyNormalsWorld = bodyBaked.normals;
            for (int i = 0; i < bodyVertsWorld.Length; i++)
            {
                bodyVertsWorld[i] = bodySmr.transform.TransformPoint(bodyVertsWorld[i]);
                bodyNormalsWorld[i] = bodySmr.transform.TransformDirection(bodyNormalsWorld[i]).normalized;
            }

            var outfitBaked = new Mesh();
            outfitSmr.BakeMesh(outfitBaked);
            var outfitVertsWorld = outfitBaked.vertices;
            for (int i = 0; i < outfitVertsWorld.Length; i++)
                outfitVertsWorld[i] = outfitSmr.transform.TransformPoint(outfitVertsWorld[i]);

            float cellSize = SpatialGrid.EstimateCellSize(bodyVertsWorld);
            var grid = new SpatialGrid(bodyVertsWorld, cellSize);

            var originalMesh = outfitSmr.sharedMesh;
            var newMesh = Object.Instantiate(originalMesh);
            newMesh.name = $"{originalMesh.name}_penetrationFixed";
            var vertices = newMesh.vertices;
            Object.DestroyImmediate(outfitBaked);

            var bones = outfitSmr.bones;
            var bindPoses = originalMesh.bindposes;
            var boneWeights = originalMesh.boneWeights;
            var skinMats = FittingHelpers.PrecomputeSkinMatrices(bones, bindPoses);

            int fixedCount = 0;
            for (int vi = 0; vi < outfitVertsWorld.Length; vi++)
            {
                if (vi % 2000 == 0)
                    ToolProgress.Report((float)vi / outfitVertsWorld.Length, $"Fixing penetration... {vi}/{outfitVertsWorld.Length}");

                grid.FindKNearest(outfitVertsWorld[vi], 4, out var indices, out var distances);
                if (indices.Length == 0) continue;

                FittingHelpers.ComputeWeightedSurface(bodyVertsWorld, bodyNormalsWorld, indices, distances,
                    out var surfacePoint, out var surfaceNormal);

                float signedDist = Vector3.Dot(outfitVertsWorld[vi] - surfacePoint, surfaceNormal);

                if (signedDist < offset)
                {
                    float pushDist = offset - signedDist;
                    Vector3 deltaWorld = surfaceNormal * pushDist;

                    if (vi < vertices.Length && vi < boneWeights.Length)
                    {
                        vertices[vi] += FittingHelpers.WorldToMeshDelta(skinMats, boneWeights[vi], deltaWorld);
                        fixedCount++;
                    }
                }
            }

            newMesh.vertices = vertices;
            newMesh.RecalculateBounds();
            newMesh.RecalculateNormals();

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedDir}/{newMesh.name}.asset");
            AssetDatabase.CreateAsset(newMesh, assetPath);

            Undo.RecordObject(outfitSmr, "FixMeshPenetration");
            outfitSmr.sharedMesh = newMesh;

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            ToolProgress.Clear();

            return $"Penetration fix complete: {fixedCount} vertices pushed out (offset={offset:F4}).\nSaved to: {assetPath}";
        }
    }
}
