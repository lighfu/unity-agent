using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Multi-stage fitting pipeline orchestrator.
    /// Stage 1: Green Coordinates (cage-based deformation, Air Gap protection)
    /// Stage 2: ARAP (as-rigid-as-possible with SVD + GS, detail restoration)
    /// Stage 3: XPBD (constraint relaxation for residual collision, optional)
    /// Stage 4: Air Gap (surface offset for comfortable fit)
    /// </summary>
    internal class FittingPipeline
    {
        // ARAP settings
        public int ArapOuterIter = 8;
        public int ArapGSIterations = 10;
        public float ArapPenetrationWeight = 10f;
        public float ArapBoundaryWeight = 50f;
        public float CollisionMargin = 0.0015f;
        public int ArapFinalCollisionPasses = 3;
        public int ArapKNearest = 8;

        // Skin-tight boost
        public float SkinTightThreshold = 0.5f;
        public float SkinTightBoostMax = 5f;

        // Smoothing
        public float TaubinLambda = 0.4f;
        public float TaubinMu = -0.42f;

        // Air Gap: post-fitting surface offset
        public float AirGap = 0f;
        public float AirGapMaxDepth = 0.02f;

        /// <summary>
        /// Execute the fitting pipeline on a single mesh.
        /// Returns a log string describing what was done.
        /// </summary>
        public string Execute(Vector3[] workVerts, FittingTopology topo,
            BodySDF bodySDF, SpatialGrid bodyGrid, Vector3[] bodyVerts, Vector3[] bodyNormals,
            string meshName, int meshIndex, int totalMeshes,
            bool useGreenCoordinates, bool useXPBD, FittingLog log = null)
        {
            int totalCollisionFixes = 0;
            var logParts = new List<string>();

            // Stage 1: Green Coordinates (if enabled)
            if (useGreenCoordinates)
            {
                log?.Info("Stage 1: Green Coordinates...");
                bool gcApplied = GreenCoordinates.TryApply(workVerts, topo, bodyVerts, bodyNormals, bodyGrid);
                if (gcApplied)
                {
                    topo.SyncSplitVertices(workVerts);
                    logParts.Add("GC");
                    log?.Info("  GC applied successfully");
                }
                else
                {
                    log?.Warn("  GC failed, skipping");
                }
            }

            // Stage 2: ARAP (SVD + Gauss-Seidel)
            log?.Info($"Stage 2: ARAP-SVD (outer={ArapOuterIter}, GS={ArapGSIterations}, margin={CollisionMargin:F4}m)");
            var arapSolver = new ARAPSolver
            {
                OuterIterations = ArapOuterIter,
                InnerGSIterations = ArapGSIterations,
                PenetrationWeight = ArapPenetrationWeight,
                BoundaryWeight = ArapBoundaryWeight,
                CollisionMargin = CollisionMargin,
                FinalCollisionPasses = ArapFinalCollisionPasses,
                SkinTightThreshold = SkinTightThreshold,
                SkinTightBoostMax = SkinTightBoostMax,
                TaubinLambda = TaubinLambda,
                TaubinMu = TaubinMu,
                KNearest = ArapKNearest,
            };
            totalCollisionFixes = arapSolver.Solve(workVerts, topo, bodySDF, bodyGrid, bodyVerts, bodyNormals,
                meshName, meshIndex, totalMeshes, log);
            logParts.Add($"ARAP-SVD({ArapOuterIter}x GS{ArapGSIterations})");

            // Stage 3: XPBD (if enabled)
            if (useXPBD)
            {
                log?.Info("Stage 3: XPBD Relaxation...");
                bool xpbdApplied = XPBDSolver.TryApply(workVerts, topo, bodySDF, log);
                if (xpbdApplied)
                {
                    topo.SyncSplitVertices(workVerts);
                    logParts.Add("XPBD-relax");
                    log?.Info("  XPBD relaxation applied");
                }
                else
                {
                    log?.Info("  XPBD skipped (not applicable)");
                }
            }

            // Stage 4: Air Gap (post-fitting surface offset)
            if (AirGap > 0.0001f)
            {
                int airGapPushed = ApplyAirGap(workVerts, topo, bodySDF, log);
                if (airGapPushed > 0)
                    logParts.Add($"AirGap({AirGap * 1000f:F1}mm)");
            }

            string stages = string.Join("+", logParts);
            return $"{totalCollisionFixes} collision fixes, {stages}";
        }

        /// <summary>
        /// Push non-boundary vertices outward so they sit at least AirGap distance
        /// from the body surface. Uses SDF gradient for direction. Only affects
        /// vertices within AirGapMaxDepth of the surface (deep interior left alone).
        /// Applies Taubin smooth afterward to prevent spikes.
        /// </summary>
        private int ApplyAirGap(Vector3[] workVerts, FittingTopology topo, BodySDF bodySDF, FittingLog log)
        {
            var rootIndices = topo.RootIndices;
            int pushed = 0;
            float targetDist = CollisionMargin + AirGap;

            foreach (int vi in rootIndices)
            {
                // Skip boundary vertices — they are anchored
                if (topo.BoundaryMask[vi] > 0.01f) continue;

                float sdf = bodySDF.SampleRaw(workVerts[vi]);

                // Only push vertices that are closer than targetDist
                // and not deep inside (sdf > -AirGapMaxDepth)
                if (sdf < targetDist && sdf > -AirGapMaxDepth)
                {
                    Vector3 grad = bodySDF.GradientSmooth(workVerts[vi]);
                    workVerts[vi] += grad * (targetDist - sdf);
                    pushed++;
                }
            }

            // Smooth to blend pushed vertices with neighbors
            if (pushed > 0)
            {
                var smoothBuffer = new Vector3[workVerts.Length];
                FittingHelpers.TaubinSmoothRoots(workVerts, smoothBuffer, topo.Adjacency, rootIndices,
                    TaubinLambda * 0.5f, TaubinMu * 0.5f);
                topo.SyncSplitVertices(workVerts);
            }

            log?.Stat("  Air Gap", $"{pushed} vertices pushed, target={targetDist * 1000f:F1}mm from body");
            return pushed;
        }
    }
}
