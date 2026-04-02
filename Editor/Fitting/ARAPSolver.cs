using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// ARAP (As-Rigid-As-Possible) solver using SVD rotation extraction
    /// and Gauss-Seidel global step for reliable convergence.
    /// </summary>
    internal class ARAPSolver
    {
        public int OuterIterations = 8;
        public int InnerGSIterations = 10;
        public float PenetrationWeight = 10f;
        public float BoundaryWeight = 50f;
        public float RegularizationWeight = 0.01f;
        public float CollisionMargin = 0.0015f;
        public int FinalCollisionPasses = 3;
        public float SkinTightThreshold = 0.5f;
        public float SkinTightBoostMax = 5f;
        public float TaubinLambda = 0.4f;
        public float TaubinMu = -0.42f;
        public int KNearest = 8;

        /// <summary>
        /// Run ARAP fitting with SVD rotation extraction and Gauss-Seidel global solve.
        /// Returns total collision fixes.
        /// </summary>
        public int Solve(Vector3[] workVerts, FittingTopology topo,
            BodySDF bodySDF, SpatialGrid bodyGrid, Vector3[] bodyVerts, Vector3[] bodyNormals,
            string meshName, int meshIndex, int totalMeshes, FittingLog log = null)
        {
            var rootIndices = topo.RootIndices;
            var adjacency = topo.Adjacency;
            int vertCount = workVerts.Length;

            // Build per-vertex cotangent weight arrays
            var adjW = new float[vertCount][];
            var diagW = new float[vertCount];
            float minCotW = float.MaxValue, maxCotW = 0f;
            double sumCotW = 0;
            int edgeCount = 0;

            foreach (int vi in rootIndices)
            {
                int count = adjacency[vi].Count;
                adjW[vi] = new float[count];
                float dw = 0f;
                for (int k = 0; k < count; k++)
                {
                    float w = FittingTopology.GetCotEdgeWeight(topo.CotWeights, vi, adjacency[vi][k]);
                    adjW[vi][k] = w;
                    dw += w;
                    if (w < minCotW) minCotW = w;
                    if (w > maxCotW) maxCotW = w;
                    sumCotW += w;
                    edgeCount++;
                }
                diagW[vi] = dw;
            }

            float avgCotW = edgeCount > 0 ? (float)(sumCotW / edgeCount) : 0f;
            log?.Stat("CotWeights", $"min={minCotW:F3}, max={maxCotW:F3}, avg={avgCotW:F3}, edges={edgeCount}");

            // Compute constraint targets and weights
            var constraintW = new float[vertCount];
            var constraintT = new Vector3[vertCount];
            // Track which vertices are boundary (immutable constraint type)
            var isBoundary = new bool[vertCount];
            int penCount = 0, bndCount = 0, freeCount = 0;

            foreach (int vi in rootIndices)
            {
                constraintT[vi] = workVerts[vi];
                constraintW[vi] = RegularizationWeight;

                float rawDist = bodySDF.SampleRaw(workVerts[vi]);
                if (rawDist < CollisionMargin)
                {
                    Vector3 grad = bodySDF.GradientSmooth(workVerts[vi]);
                    constraintT[vi] = workVerts[vi] + grad * (CollisionMargin - rawDist);
                    constraintW[vi] = PenetrationWeight;
                    penCount++;
                }

                if (topo.BoundaryMask[vi] > 0.01f)
                {
                    constraintT[vi] = workVerts[vi];
                    constraintW[vi] = Mathf.Max(constraintW[vi], topo.BoundaryMask[vi] * BoundaryWeight);
                    isBoundary[vi] = true;
                    bndCount++;
                }

                if (constraintW[vi] <= RegularizationWeight + 0.001f)
                    freeCount++;
            }

            log?.Stat("Constraints", $"penetrating={penCount}, boundary={bndCount}, free={freeCount}");

            // Adaptive collision weight for skin-tight garments
            // When most vertices are penetrating, collision must dominate shape preservation
            float penRatio = rootIndices.Count > 0 ? (float)penCount / rootIndices.Count : 0f;
            float effectivePenWeight = PenetrationWeight;
            if (penRatio > SkinTightThreshold)
            {
                float range = Mathf.Max(1f - SkinTightThreshold, 0.01f);
                float boost = Mathf.Lerp(1f, SkinTightBoostMax, (penRatio - SkinTightThreshold) / range);
                effectivePenWeight *= boost;
                foreach (int vi in rootIndices)
                {
                    if (!isBoundary[vi] && constraintW[vi] >= PenetrationWeight - 0.001f)
                        constraintW[vi] = effectivePenWeight;
                }
                log?.Info($"  Skin-tight boost: pen ratio={penRatio:P0}, weight {PenetrationWeight:F0} → {effectivePenWeight:F0}");
            }

            // Store rest positions
            var restVerts = new Vector3[vertCount];
            System.Array.Copy(workVerts, restVerts, vertCount);

            // Per-vertex rotations
            var rotations = new Mat3[vertCount];
            for (int i = 0; i < rotations.Length; i++) rotations[i] = Mat3.Identity;

            var prevVerts = new Vector3[vertCount];

            for (int outer = 0; outer < OuterIterations; outer++)
            {
                if (log != null && log.IsCancelled) break;

                log?.Info($"  ARAP iter {outer + 1}/{OuterIterations}");

                // Save for displacement measurement
                System.Array.Copy(workVerts, prevVerts, vertCount);

                // ── Dynamic collision constraint refresh ──
                if (outer > 0)
                {
                    int refreshPen = 0, resolved = 0;
                    foreach (int vi in rootIndices)
                    {
                        // Never override boundary constraints
                        if (isBoundary[vi]) continue;

                        float rawDist = bodySDF.SampleRaw(workVerts[vi]);
                        if (rawDist < CollisionMargin)
                        {
                            Vector3 grad = bodySDF.GradientSmooth(workVerts[vi]);
                            constraintT[vi] = workVerts[vi] + grad * (CollisionMargin - rawDist);
                            constraintW[vi] = effectivePenWeight;
                            refreshPen++;
                        }
                        else if (constraintW[vi] >= PenetrationWeight - 0.001f)
                        {
                            // Was penetrating, now resolved → relax constraint
                            constraintW[vi] = RegularizationWeight;
                            constraintT[vi] = workVerts[vi];
                            resolved++;
                        }
                    }
                    log?.Stat("  Collision refresh", $"pen={refreshPen}, resolved={resolved}");
                }

                // ── Local Step: SVD rotation extraction ──
                float minDet = float.MaxValue, maxDet = float.MinValue;
                double sumFrob = 0;
                int degenerateCount = 0;

                foreach (int vi in rootIndices)
                {
                    Mat3 S = new Mat3();
                    var adj = adjacency[vi];
                    var aw = adjW[vi];
                    for (int k = 0; k < adj.Count; k++)
                    {
                        int nj = adj[k];
                        Vector3 restEdge = restVerts[vi] - restVerts[nj];
                        Vector3 defEdge = workVerts[vi] - workVerts[nj];
                        S = Mat3.Add(S, Mat3.Scale(Mat3.OuterProduct(defEdge, restEdge), aw[k]));
                    }
                    rotations[vi] = SVD3x3.ExtractRotation(S);

                    // Guard: replace degenerate rotations with identity
                    float det = rotations[vi].Determinant;
                    if (det < 0.5f || det > 1.5f || float.IsNaN(det))
                    {
                        rotations[vi] = Mat3.Identity;
                        degenerateCount++;
                    }

                    if (det < minDet) minDet = det;
                    if (det > maxDet) maxDet = det;
                    sumFrob += S.FrobeniusNormSq;
                }

                if (outer == 0 || outer == OuterIterations - 1)
                {
                    log?.Stat("  Rotation", $"det=[{minDet:F4}, {maxDet:F4}], avgFrob={sumFrob / rootIndices.Count:E2}");
                    if (degenerateCount > 0)
                        log?.Warn($"  {degenerateCount} degenerate rotations replaced with Identity");
                }

                // ── Global Step: Gauss-Seidel iteration ──
                for (int gs = 0; gs < InnerGSIterations; gs++)
                {
                    foreach (int vi in rootIndices)
                    {
                        Mat3 Ri = rotations[vi];
                        var adj = adjacency[vi];
                        var aw = adjW[vi];

                        Vector3 sumNeighbor = Vector3.zero;
                        for (int k = 0; k < adj.Count; k++)
                        {
                            int nj = adj[k];
                            float w = aw[k];
                            Vector3 restEdge = restVerts[vi] - restVerts[nj];
                            Vector3 rotatedEdge = Mat3.Mul(Mat3.Add(Ri, rotations[nj]), restEdge) * 0.5f;
                            sumNeighbor += w * (rotatedEdge + workVerts[nj]);
                        }

                        sumNeighbor += constraintW[vi] * constraintT[vi];

                        float denom = diagW[vi] + constraintW[vi];
                        if (denom > 1e-10f)
                            workVerts[vi] = sumNeighbor / denom;
                    }
                }

                // Sync split vertices after GS
                topo.SyncSplitVertices(workVerts);

                // Measure displacement from previous iteration
                float maxDisp = 0f;
                double sumDisp = 0;
                foreach (int vi in rootIndices)
                {
                    float d = Vector3.Distance(workVerts[vi], prevVerts[vi]);
                    if (d > maxDisp) maxDisp = d;
                    sumDisp += d;
                }
                float avgDisp = rootIndices.Count > 0 ? (float)(sumDisp / rootIndices.Count) : 0f;
                log?.Stat("  Displacement", $"max={maxDisp:F5}m, avg={avgDisp:F5}m");

                // Update progress
                if (log != null)
                    log.Progress = (float)(meshIndex * OuterIterations + outer + 1) / (totalMeshes * OuterIterations);
            }

            // ── Final collision cleanup ──
            int totalCollisionFixes = 0;
            var smoothBuffer = new Vector3[vertCount];

            // Pass 1: KNN-based collision (good for shallow penetrations near surface)
            for (int ps = 0; ps < FinalCollisionPasses; ps++)
            {
                FittingHelpers.TaubinSmoothRoots(workVerts, smoothBuffer, adjacency, rootIndices, TaubinLambda, TaubinMu);
                int fixes = FittingHelpers.CollisionPassRoots(workVerts, bodyGrid, bodyVerts, bodyNormals, rootIndices, KNearest, CollisionMargin);
                totalCollisionFixes += fixes;
                topo.SyncSplitVertices(workVerts);
            }

            // Pass 2: SDF-based collision (consistent gradient direction, better for remaining deep penetrations)
            for (int ps = 0; ps < 2; ps++)
            {
                FittingHelpers.TaubinSmoothRoots(workVerts, smoothBuffer, adjacency, rootIndices, TaubinLambda, TaubinMu);
                int sdfFixes = 0;
                foreach (int vi in rootIndices)
                {
                    float sdf = bodySDF.SampleRaw(workVerts[vi]);
                    if (sdf < CollisionMargin)
                    {
                        Vector3 grad = bodySDF.GradientSmooth(workVerts[vi]);
                        workVerts[vi] += grad * (CollisionMargin - sdf);
                        sdfFixes++;
                    }
                }
                totalCollisionFixes += sdfFixes;
                topo.SyncSplitVertices(workVerts);
            }

            // Final Taubin smooth to remove spike artifacts from collision
            FittingHelpers.TaubinSmoothRoots(workVerts, smoothBuffer, adjacency, rootIndices, TaubinLambda, TaubinMu);
            topo.SyncSplitVertices(workVerts);

            log?.Stat("  Collision cleanup", $"{totalCollisionFixes} fixes in {FinalCollisionPasses + 2} passes");

            // Final displacement from rest
            float maxFinalDisp = 0f;
            double sumFinalDisp = 0;
            foreach (int vi in rootIndices)
            {
                float d = Vector3.Distance(workVerts[vi], restVerts[vi]);
                if (d > maxFinalDisp) maxFinalDisp = d;
                sumFinalDisp += d;
            }
            float avgFinalDisp = rootIndices.Count > 0 ? (float)(sumFinalDisp / rootIndices.Count) : 0f;
            log?.Stat("  Total deformation", $"max={maxFinalDisp:F5}m, avg={avgFinalDisp:F5}m");

            return totalCollisionFixes;
        }
    }
}
