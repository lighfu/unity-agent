using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Mesh topology preprocessing: merged indices, adjacency graph, boundary mask, cotangent weights.
    /// </summary>
    internal class FittingTopology
    {
        public static int BoundaryDiffusionPasses = 4;

        public int[] MergedIndices;
        public List<int> RootIndices;
        public List<int>[] Adjacency;
        public float[] BoundaryMask;
        public Dictionary<long, float> CotWeights;

        /// <summary>
        /// Build topology data from mesh vertices, triangles and original (pre-alignment) vertices.
        /// </summary>
        public static FittingTopology Build(Vector3[] workVerts, Vector3[] origVerts, int[] triangles)
        {
            var topo = new FittingTopology();
            int vertCount = workVerts.Length;

            // Merge colocated vertices (UV seams)
            topo.MergedIndices = new int[vertCount];
            for (int i = 0; i < vertCount; i++) topo.MergedIndices[i] = i;

            topo.RootIndices = new List<int>();
            var processed = new bool[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                if (processed[i]) continue;
                topo.RootIndices.Add(i);
                processed[i] = true;
                for (int j = i + 1; j < vertCount; j++)
                {
                    if (!processed[j] && Vector3.Distance(origVerts[i], origVerts[j]) < 0.0001f)
                    {
                        processed[j] = true;
                        topo.MergedIndices[j] = i;
                    }
                }
            }

            // Sync colocated vertices
            for (int i = 0; i < vertCount; i++)
            {
                if (topo.MergedIndices[i] != i)
                    workVerts[i] = workVerts[topo.MergedIndices[i]];
            }

            // Build adjacency graph from triangles using merged indices
            var adjSet = new HashSet<int>[vertCount];
            for (int i = 0; i < vertCount; i++) adjSet[i] = new HashSet<int>();
            for (int ti = 0; ti < triangles.Length; ti += 3)
            {
                int i0 = topo.MergedIndices[triangles[ti]];
                int i1 = topo.MergedIndices[triangles[ti + 1]];
                int i2 = topo.MergedIndices[triangles[ti + 2]];
                if (i0 != i1) { adjSet[i0].Add(i1); adjSet[i1].Add(i0); }
                if (i1 != i2) { adjSet[i1].Add(i2); adjSet[i2].Add(i1); }
                if (i2 != i0) { adjSet[i2].Add(i0); adjSet[i0].Add(i2); }
            }
            topo.Adjacency = new List<int>[vertCount];
            for (int i = 0; i < vertCount; i++) topo.Adjacency[i] = new List<int>(adjSet[i]);

            // Boundary detection via edge-triangle count
            topo.BoundaryMask = new float[vertCount];
            var edgeTriCount = new Dictionary<long, int>();
            for (int ti = 0; ti < triangles.Length; ti += 3)
            {
                int i0 = topo.MergedIndices[triangles[ti]];
                int i1 = topo.MergedIndices[triangles[ti + 1]];
                int i2 = topo.MergedIndices[triangles[ti + 2]];

                CountMergedEdge(edgeTriCount, i0, i1);
                CountMergedEdge(edgeTriCount, i1, i2);
                CountMergedEdge(edgeTriCount, i2, i0);
            }
            foreach (var kvp in edgeTriCount)
            {
                if (kvp.Value == 1)
                {
                    int v0 = (int)(kvp.Key >> 32);
                    int v1 = (int)(kvp.Key & 0xFFFFFFFF);
                    topo.BoundaryMask[v0] = 1f;
                    topo.BoundaryMask[v1] = 1f;
                }
            }

            // Boundary mask gradient diffusion
            var tempMask = new float[vertCount];
            for (int step = 0; step < BoundaryDiffusionPasses; step++)
            {
                System.Array.Copy(topo.BoundaryMask, tempMask, vertCount);
                foreach (int vi in topo.RootIndices)
                {
                    if (topo.Adjacency[vi].Count > 0)
                    {
                        float maxN = 0f;
                        foreach (int ni in topo.Adjacency[vi])
                        {
                            if (topo.BoundaryMask[ni] > maxN) maxN = topo.BoundaryMask[ni];
                        }
                        tempMask[vi] = Mathf.Max(tempMask[vi], maxN - 0.25f);
                    }
                }
                System.Array.Copy(tempMask, topo.BoundaryMask, vertCount);
            }

            // Build cotangent weights
            topo.CotWeights = new Dictionary<long, float>();
            for (int ti = 0; ti < triangles.Length; ti += 3)
            {
                int a = topo.MergedIndices[triangles[ti]];
                int b = topo.MergedIndices[triangles[ti + 1]];
                int c = topo.MergedIndices[triangles[ti + 2]];
                if (a == b || b == c || a == c) continue;
                AddCotWeight(topo.CotWeights, a, b, workVerts[a], workVerts[b], workVerts[c]);
                AddCotWeight(topo.CotWeights, b, c, workVerts[b], workVerts[c], workVerts[a]);
                AddCotWeight(topo.CotWeights, a, c, workVerts[a], workVerts[c], workVerts[b]);
            }

            return topo;
        }

        /// <summary>
        /// Sync split vertices (UV seams) to their root positions.
        /// </summary>
        public void SyncSplitVertices(Vector3[] verts)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                if (MergedIndices[i] != i)
                    verts[i] = verts[MergedIndices[i]];
            }
        }

        public static float GetCotEdgeWeight(Dictionary<long, float> weights, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = ((long)lo << 32) | (long)(uint)hi;
            return weights.TryGetValue(key, out float w) ? w : 0.01f;
        }

        private static void AddCotWeight(Dictionary<long, float> weights,
            int a, int b, Vector3 pa, Vector3 pb, Vector3 apex)
        {
            if (a == b) return;
            Vector3 e0 = pa - apex, e1 = pb - apex;
            float crossMag = Vector3.Cross(e0, e1).magnitude;
            float dot = Vector3.Dot(e0, e1);
            float cot = crossMag > 1e-8f ? dot / crossMag : 0f;
            cot = Mathf.Clamp(cot, 0.01f, 100f);

            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = ((long)lo << 32) | (long)(uint)hi;
            if (weights.TryGetValue(key, out float existing))
                weights[key] = existing + 0.5f * cot;
            else
                weights[key] = 0.5f * cot;
        }

        private static void CountMergedEdge(Dictionary<long, int> edgeTriCount, int a, int b)
        {
            if (a == b) return;
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (long)(uint)hi;
            edgeTriCount[key] = edgeTriCount.TryGetValue(key, out int c) ? c + 1 : 1;
        }
    }
}
