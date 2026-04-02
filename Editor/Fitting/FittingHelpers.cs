using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Common helper methods shared across the fitting pipeline.
    /// </summary>
    internal static class FittingHelpers
    {
        /// <summary>
        /// Auto-detect body meshes on avatar, excluding a given GameObject subtree.
        /// </summary>
        public static List<SkinnedMeshRenderer> FindBodyMeshes(GameObject avatarGo, GameObject excludeGo = null)
        {
            var result = new List<SkinnedMeshRenderer>();
            var allSmrs = avatarGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null) continue;
                if (excludeGo != null && smr.transform.IsChildOf(excludeGo.transform)) continue;
                string lower = smr.name.ToLowerInvariant();
                if (lower.Contains("body") || lower.Contains("素体") || lower.Contains("mesh_body"))
                    result.Add(smr);
            }
            if (result.Count == 0)
            {
                var candidates = allSmrs
                    .Where(s => s.sharedMesh != null)
                    .Where(s => excludeGo == null || !s.transform.IsChildOf(excludeGo.transform));
                var largest = candidates
                    .OrderByDescending(s => s.sharedMesh.vertexCount)
                    .FirstOrDefault();
                if (largest != null)
                    result.Add(largest);
            }
            return result;
        }

        /// <summary>
        /// Bake all SMRs to world-space vertices and normals.
        /// </summary>
        public static void BakeWorldVerticesAndNormals(List<SkinnedMeshRenderer> smrs,
            out Vector3[] allVerts, out Vector3[] allNormals)
        {
            var vertList = new List<Vector3>();
            var normList = new List<Vector3>();
            foreach (var smr in smrs)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var verts = baked.vertices;
                var normals = baked.normals;
                for (int i = 0; i < verts.Length; i++)
                {
                    vertList.Add(smr.transform.TransformPoint(verts[i]));
                    if (i < normals.Length)
                        normList.Add(smr.transform.TransformDirection(normals[i]).normalized);
                    else
                        normList.Add(Vector3.up);
                }
                Object.DestroyImmediate(baked);
            }
            allVerts = vertList.ToArray();
            allNormals = normList.ToArray();
        }

        /// <summary>
        /// Inverse-distance weighted surface point and normal from KNN results.
        /// </summary>
        public static void ComputeWeightedSurface(Vector3[] verts, Vector3[] normals,
            int[] indices, float[] distances, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = Vector3.zero;
            surfaceNormal = Vector3.zero;
            float totalWeight = 0f;

            for (int i = 0; i < indices.Length; i++)
            {
                float w = distances[i] > 0.00001f ? 1f / distances[i] : 100000f;
                int idx = indices[i];
                surfacePoint += verts[idx] * w;
                if (idx < normals.Length)
                    surfaceNormal += normals[idx] * w;
                totalWeight += w;
            }

            if (totalWeight > 0f)
            {
                surfacePoint /= totalWeight;
                surfaceNormal = surfaceNormal.normalized;
            }
            else
            {
                surfaceNormal = Vector3.up;
            }
        }

        /// <summary>
        /// Precompute per-bone skin matrices (bone.L2W * bindPose).
        /// </summary>
        public static Matrix4x4[] PrecomputeSkinMatrices(Transform[] bones, Matrix4x4[] bindPoses)
        {
            int count = Mathf.Min(bones.Length, bindPoses.Length);
            var result = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                if (bones[i] != null)
                    result[i] = bones[i].localToWorldMatrix * bindPoses[i];
                else
                    result[i] = Matrix4x4.identity;
            }
            return result;
        }

        /// <summary>
        /// Transform a world-space delta into mesh-space using blended skinning inverse.
        /// </summary>
        public static Vector3 WorldToMeshDelta(Matrix4x4[] skinMatrices,
            BoneWeight bw, Vector3 deltaWorld)
        {
            Matrix4x4 blended = Matrix4x4.zero;

            if (bw.weight0 > 0.001f && bw.boneIndex0 >= 0 && bw.boneIndex0 < skinMatrices.Length)
                AccumSkinMatrix(ref blended, skinMatrices[bw.boneIndex0], bw.weight0);
            if (bw.weight1 > 0.001f && bw.boneIndex1 >= 0 && bw.boneIndex1 < skinMatrices.Length)
                AccumSkinMatrix(ref blended, skinMatrices[bw.boneIndex1], bw.weight1);
            if (bw.weight2 > 0.001f && bw.boneIndex2 >= 0 && bw.boneIndex2 < skinMatrices.Length)
                AccumSkinMatrix(ref blended, skinMatrices[bw.boneIndex2], bw.weight2);
            if (bw.weight3 > 0.001f && bw.boneIndex3 >= 0 && bw.boneIndex3 < skinMatrices.Length)
                AccumSkinMatrix(ref blended, skinMatrices[bw.boneIndex3], bw.weight3);

            blended[3, 3] = 1f;

            Vector3 result = blended.inverse.MultiplyVector(deltaWorld);

            if (float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsNaN(result.z))
                return deltaWorld;

            return result;
        }

        private static void AccumSkinMatrix(ref Matrix4x4 target, Matrix4x4 source, float weight)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    target[r, c] += weight * source[r, c];
        }

        /// <summary>
        /// Accumulate bone weight into a dictionary, remapping from avatar bones to outfit bones by name.
        /// If no exact name match is found, walks up the bone hierarchy to find the nearest ancestor
        /// that exists in the outfit. This prevents weight loss when the outfit has fewer bones than the avatar.
        /// </summary>
        public static void AccumulateWeight(Dictionary<int, float> accum,
            Transform[] avatarBones, Dictionary<string, int> outfitBoneByName,
            int avatarBoneIdx, float weight)
        {
            if (weight < 0.0001f) return;
            if (avatarBoneIdx < 0 || avatarBoneIdx >= avatarBones.Length) return;
            if (avatarBones[avatarBoneIdx] == null) return;

            // Walk up the hierarchy: try exact match first, then ancestors
            Transform bone = avatarBones[avatarBoneIdx];
            while (bone != null)
            {
                if (outfitBoneByName.TryGetValue(bone.name, out int outfitIdx))
                {
                    if (!accum.ContainsKey(outfitIdx)) accum[outfitIdx] = 0f;
                    accum[outfitIdx] += weight;
                    return;
                }
                bone = bone.parent;
            }
        }

        /// <summary>
        /// Classify body region by local-space position (Y-based).
        /// </summary>
        public static string ClassifyRegion(Vector3 localPos)
        {
            float y = localPos.y;
            float absX = Mathf.Abs(localPos.x);
            if (y > 1.2f) return "Head";
            if (y > 0.9f) return absX > 0.15f ? "Shoulder/Arm" : "Neck/Chest";
            if (y > 0.5f) return absX > 0.15f ? "Arm" : "Torso";
            if (y > 0.1f) return "Hip/Thigh";
            return "Leg/Foot";
        }

        /// <summary>
        /// Project point onto triangle and return distance + barycentric coordinates.
        /// </summary>
        public static float ProjectOntoTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
            out float u, out float v, out float w)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                u = 1f; v = 0f; w = 0f;
                return Vector3.Distance(p, a);
            }

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                u = 0f; v = 1f; w = 0f;
                return Vector3.Distance(p, b);
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float t = d1 / (d1 - d3);
                u = 1f - t; v = t; w = 0f;
                return Vector3.Distance(p, a + t * ab);
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                u = 0f; v = 0f; w = 1f;
                return Vector3.Distance(p, c);
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float t = d2 / (d2 - d6);
                u = 1f - t; v = 0f; w = t;
                return Vector3.Distance(p, a + t * ac);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                u = 0f; v = 1f - t; w = t;
                return Vector3.Distance(p, b + t * (c - b));
            }

            float denom = 1f / (va + vb + vc);
            v = vb * denom;
            w = vc * denom;
            u = 1f - v - w;
            Vector3 closest = u * a + v * b + w * c;
            return Vector3.Distance(p, closest);
        }

        /// <summary>
        /// Interpolate BoneWeight from triangle vertices using barycentric coordinates.
        /// </summary>
        public static BoneWeight InterpolateBoneWeights(
            BoneWeight bw0, BoneWeight bw1, BoneWeight bw2,
            float u, float v, float w,
            Transform[] avatarBones, Dictionary<string, int> outfitBoneByName)
        {
            var accum = new Dictionary<int, float>();

            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw0.boneIndex0, bw0.weight0 * u);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw0.boneIndex1, bw0.weight1 * u);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw0.boneIndex2, bw0.weight2 * u);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw0.boneIndex3, bw0.weight3 * u);

            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw1.boneIndex0, bw1.weight0 * v);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw1.boneIndex1, bw1.weight1 * v);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw1.boneIndex2, bw1.weight2 * v);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw1.boneIndex3, bw1.weight3 * v);

            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw2.boneIndex0, bw2.weight0 * w);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw2.boneIndex1, bw2.weight1 * w);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw2.boneIndex2, bw2.weight2 * w);
            AccumulateWeight(accum, avatarBones, outfitBoneByName, bw2.boneIndex3, bw2.weight3 * w);

            var sorted = accum.OrderByDescending(kv => kv.Value).Take(4).ToArray();
            float sum = sorted.Sum(kv => kv.Value);
            if (sum < 0.0001f) sum = 1f;

            var bwResult = new BoneWeight();
            if (sorted.Length > 0) { bwResult.boneIndex0 = sorted[0].Key; bwResult.weight0 = sorted[0].Value / sum; }
            if (sorted.Length > 1) { bwResult.boneIndex1 = sorted[1].Key; bwResult.weight1 = sorted[1].Value / sum; }
            if (sorted.Length > 2) { bwResult.boneIndex2 = sorted[2].Key; bwResult.weight2 = sorted[2].Value / sum; }
            if (sorted.Length > 3) { bwResult.boneIndex3 = sorted[3].Key; bwResult.weight3 = sorted[3].Value / sum; }
            return bwResult;
        }

        /// <summary>
        /// Laplacian smoothing pass on root vertices only.
        /// </summary>
        public static void LaplacianPassRoots(Vector3[] verts, Vector3[] buffer,
            List<int>[] adjacency, List<int> rootIndices, float factor)
        {
            foreach (int vi in rootIndices)
            {
                var neighbors = adjacency[vi];
                if (neighbors.Count == 0)
                {
                    buffer[vi] = verts[vi];
                    continue;
                }
                Vector3 avg = Vector3.zero;
                foreach (int ni in neighbors)
                    avg += verts[ni];
                avg /= neighbors.Count;
                buffer[vi] = Vector3.LerpUnclamped(verts[vi], avg, factor);
            }
            foreach (int vi in rootIndices)
                verts[vi] = buffer[vi];
        }

        /// <summary>
        /// Taubin smoothing (shrink+inflate) on root vertices.
        /// </summary>
        public static void TaubinSmoothRoots(Vector3[] verts, Vector3[] buffer,
            List<int>[] adjacency, List<int> rootIndices, float lambda, float mu)
        {
            LaplacianPassRoots(verts, buffer, adjacency, rootIndices, lambda);
            LaplacianPassRoots(verts, buffer, adjacency, rootIndices, mu);
        }

        /// <summary>
        /// KNN collision pass: push vertices out of body.
        /// </summary>
        public static int CollisionPassRoots(Vector3[] workVerts, SpatialGrid bodyGrid,
            Vector3[] bodyVerts, Vector3[] bodyNormals, List<int> rootIndices, int kNearest, float margin)
        {
            int fixes = 0;
            foreach (int vi in rootIndices)
            {
                bodyGrid.FindKNearest(workVerts[vi], kNearest, out var idx, out var dist);
                if (idx.Length == 0) continue;

                ComputeWeightedSurface(bodyVerts, bodyNormals, idx, dist,
                    out var surfPt, out var surfNrm);
                float signedDist = Vector3.Dot(workVerts[vi] - surfPt, surfNrm);

                if (signedDist < margin)
                {
                    workVerts[vi] += surfNrm * (margin - signedDist);
                    fixes++;
                }
            }
            return fixes;
        }
    }
}
