using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// XPBD constraint relaxation solver (post-ARAP).
    /// Resolves residual penetrations while preserving mesh shape via
    /// distance and bending constraints. No gravity — pure constraint projection.
    /// </summary>
    internal static class XPBDSolver
    {
        public static int Iterations = 50;
        public static float BendCompliance = 0.001f;
        public static float CollisionMargin = 0.002f;
        private const float DistRelaxation = 0.5f; // Under-relaxation to prevent oscillation
        public static float MaxIterDisp = 0.002f; // Max displacement per vertex per iteration (2mm)
        public static float MaxTotalDisp = 0.015f; // Max total displacement from snapshot (1.5cm)

        /// <summary>
        /// Apply constraint relaxation to resolve residual penetrations.
        /// Boundary vertices are pinned (fixed). Closed meshes are skipped.
        /// Returns true if successfully applied.
        /// </summary>
        public static bool TryApply(Vector3[] workVerts, FittingTopology topo, BodySDF bodySDF, FittingLog log = null)
        {
            var rootIndices = topo.RootIndices;
            int vertCount = workVerts.Length;

            // Build constraints from current (post-ARAP) mesh state
            var distConstraints = BuildDistanceConstraints(workVerts, topo);
            var bendConstraints = BuildBendConstraints(workVerts, topo);

            log?.Stat("Relaxation constraints", $"distance={distConstraints.Count}, bend={bendConstraints.Count}");

            // Pin boundary vertices (they stay fixed)
            var pinned = new bool[vertCount];
            int pinnedCount = 0;
            foreach (int vi in rootIndices)
            {
                if (topo.BoundaryMask[vi] > 0.01f)
                {
                    pinned[vi] = true;
                    pinnedCount++;
                }
            }

            // Closed meshes: no boundary anchors → relaxation is meaningless
            if (pinnedCount == 0)
            {
                log?.Info("  Closed mesh (no boundary) - skipping relaxation");
                return false;
            }

            log?.Stat("Pinned vertices", $"{pinnedCount} / {rootIndices.Count}");

            // Check if there are any penetrations to fix
            int initialPenetrations = 0;
            foreach (int vi in rootIndices)
            {
                if (pinned[vi]) continue;
                if (bodySDF.SampleRaw(workVerts[vi]) < CollisionMargin)
                    initialPenetrations++;
            }

            if (initialPenetrations == 0)
            {
                log?.Info("  No penetrations to fix - skipping relaxation");
                return false;
            }

            log?.Stat("Initial penetrations", $"{initialPenetrations}");

            // Inverse mass (0 = pinned/fixed)
            var invMass = new float[vertCount];
            foreach (int vi in rootIndices)
                invMass[vi] = pinned[vi] ? 0f : 1f;

            // Bending stiffness: lower compliance = stiffer
            float bendStiffness = 1f / (1f + BendCompliance * 1000f);

            int logInterval = Mathf.Max(1, Iterations / 5);

            // Snapshot for divergence detection + revert
            var snapshot = new Vector3[vertCount];
            System.Array.Copy(workVerts, snapshot, vertCount);

            // Per-iteration position buffer for displacement clamping
            var iterStart = new Vector3[vertCount];

            // Track which vertices are near/inside the body — distance constraints
            // must not pull these vertices back into the body.
            var nearBody = new bool[vertCount];

            for (int iter = 0; iter < Iterations; iter++)
            {
                if (log != null && log.IsCancelled) break;

                // Save position at start of this iteration for clamping
                System.Array.Copy(workVerts, iterStart, vertCount);

                // 0. Tag vertices inside the body (collision zone)
                //    Only skip distance constraints for actually penetrating vertices,
                //    not "nearby" ones — preserves mesh shape for skin-tight garments.
                foreach (int vi in rootIndices)
                {
                    if (pinned[vi]) continue;
                    nearBody[vi] = bodySDF.SampleRaw(workVerts[vi]) < CollisionMargin;
                }

                // 1. Distance constraints: restore edge lengths (with under-relaxation)
                //    Skip edges where either vertex is in the collision zone — prevents
                //    distance constraints from pulling collision-fixed vertices back in.
                for (int ci = 0; ci < distConstraints.Count; ci++)
                {
                    var c = distConstraints[ci];
                    if (nearBody[c.i0] || nearBody[c.i1]) continue;

                    float w1 = invMass[c.i0], w2 = invMass[c.i1];
                    if (w1 + w2 < 1e-10f) continue;

                    Vector3 diff = workVerts[c.i0] - workVerts[c.i1];
                    float len = diff.magnitude;
                    if (len < 1e-10f) continue;

                    float err = len - c.restLength;
                    if (Mathf.Abs(err) < 1e-8f) continue;

                    Vector3 grad = diff / len;
                    float correction = err / (w1 + w2) * DistRelaxation;

                    workVerts[c.i0] -= grad * (w1 * correction);
                    workVerts[c.i1] += grad * (w2 * correction);
                }

                // 2. Bending constraints: maintain curvature (skip near-body vertices)
                for (int ci = 0; ci < bendConstraints.Count; ci++)
                {
                    var c = bendConstraints[ci];
                    if (nearBody[c.i2] || nearBody[c.i3]) continue;
                    SolveBendConstraint(workVerts, invMass, c, bendStiffness);
                }

                // 3. Collision: push penetrating vertices out of body
                int collisionFixes = 0;
                foreach (int vi in rootIndices)
                {
                    if (pinned[vi]) continue;
                    float sdf = bodySDF.SampleRaw(workVerts[vi]);
                    if (sdf < CollisionMargin)
                    {
                        Vector3 grad = bodySDF.GradientSmooth(workVerts[vi]);
                        workVerts[vi] += grad * (CollisionMargin - sdf);
                        collisionFixes++;
                    }
                }

                // 4. Per-vertex displacement clamping — prevents exponential blowup
                int clampCount = 0;
                foreach (int vi in rootIndices)
                {
                    if (pinned[vi]) continue;
                    // Per-iteration clamp
                    Vector3 delta = workVerts[vi] - iterStart[vi];
                    float mag = delta.magnitude;
                    if (mag > MaxIterDisp)
                    {
                        workVerts[vi] = iterStart[vi] + delta * (MaxIterDisp / mag);
                        clampCount++;
                    }
                    // Total displacement clamp from snapshot — prevents long-term drift
                    Vector3 totalDelta = workVerts[vi] - snapshot[vi];
                    float totalMag = totalDelta.magnitude;
                    if (totalMag > MaxTotalDisp)
                    {
                        workVerts[vi] = snapshot[vi] + totalDelta * (MaxTotalDisp / totalMag);
                    }
                }

                // Logging
                if (iter % logInterval == 0)
                {
                    float maxDisp = 0f;
                    foreach (int vi in rootIndices)
                    {
                        float d = Vector3.Distance(workVerts[vi], snapshot[vi]);
                        if (d > maxDisp) maxDisp = d;
                    }
                    log?.Info($"  Relaxation pass {iter + 1}/{Iterations}, collisions={collisionFixes}, clamped={clampCount}, maxDisp={maxDisp:F5}m");
                }
            }

            // Final penetration count
            int finalPenetrations = 0;
            foreach (int vi in rootIndices)
            {
                if (bodySDF.SampleRaw(workVerts[vi]) < CollisionMargin)
                    finalPenetrations++;
            }
            log?.Stat("Final penetrations", $"{finalPenetrations} (was {initialPenetrations})");

            // Quality gate: revert if XPBD made things worse
            if (finalPenetrations > initialPenetrations)
            {
                log?.Warn($"  XPBD increased penetrations ({initialPenetrations} → {finalPenetrations}) — reverting to pre-XPBD state");
                System.Array.Copy(snapshot, workVerts, vertCount);
                return false;
            }

            // Sync split vertices
            topo.SyncSplitVertices(workVerts);
            return true;
        }

        // ─── Constraint Structures ───

        private struct DistanceConstraint
        {
            public int i0, i1;
            public float restLength;
        }

        private struct BendConstraint
        {
            public int i0, i1, i2, i3; // i0-i1 shared edge, i2-i3 opposite vertices
            public float restAngle;
        }

        // ─── Constraint Building ───

        private static List<DistanceConstraint> BuildDistanceConstraints(Vector3[] verts, FittingTopology topo)
        {
            var constraints = new List<DistanceConstraint>();
            var addedEdges = new HashSet<long>();

            foreach (int vi in topo.RootIndices)
            {
                foreach (int nj in topo.Adjacency[vi])
                {
                    int lo = vi < nj ? vi : nj;
                    int hi = vi < nj ? nj : vi;
                    long key = ((long)lo << 32) | (long)(uint)hi;
                    if (addedEdges.Add(key))
                    {
                        constraints.Add(new DistanceConstraint
                        {
                            i0 = lo,
                            i1 = hi,
                            restLength = Vector3.Distance(verts[lo], verts[hi])
                        });
                    }
                }
            }

            return constraints;
        }

        private static List<BendConstraint> BuildBendConstraints(Vector3[] verts, FittingTopology topo)
        {
            var constraints = new List<BendConstraint>();

            foreach (int vi in topo.RootIndices)
            {
                foreach (int nj in topo.Adjacency[vi])
                {
                    if (nj <= vi) continue; // Process each edge once

                    // Find common neighbors (opposite vertices of triangles sharing this edge)
                    var commonNeighbors = new List<int>();
                    foreach (int ni in topo.Adjacency[vi])
                    {
                        if (ni != nj && topo.Adjacency[nj].Contains(ni))
                            commonNeighbors.Add(ni);
                    }

                    if (commonNeighbors.Count == 2)
                    {
                        int i2 = commonNeighbors[0];
                        int i3 = commonNeighbors[1];

                        float angle = ComputeDihedralAngle(verts[vi], verts[nj], verts[i2], verts[i3]);
                        constraints.Add(new BendConstraint
                        {
                            i0 = vi, i1 = nj,
                            i2 = i2, i3 = i3,
                            restAngle = angle
                        });
                    }
                }
            }

            return constraints;
        }

        private static float ComputeDihedralAngle(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            Vector3 edge = (p1 - p0).normalized;
            Vector3 n1 = Vector3.Cross(edge, p2 - p0).normalized;
            Vector3 n2 = Vector3.Cross(edge, p3 - p0).normalized;
            float dot = Mathf.Clamp(Vector3.Dot(n1, n2), -1f, 1f);
            return Mathf.Acos(dot);
        }

        private static void SolveBendConstraint(Vector3[] verts, float[] invMass,
            BendConstraint c, float stiffness)
        {
            float currentAngle = ComputeDihedralAngle(verts[c.i0], verts[c.i1], verts[c.i2], verts[c.i3]);
            float constraint = currentAngle - c.restAngle;

            if (Mathf.Abs(constraint) < 1e-6f) return;

            Vector3 edge = (verts[c.i1] - verts[c.i0]).normalized;
            Vector3 n1 = Vector3.Cross(edge, verts[c.i2] - verts[c.i0]).normalized;
            Vector3 n2 = Vector3.Cross(edge, verts[c.i3] - verts[c.i0]).normalized;

            float w2 = invMass[c.i2], w3 = invMass[c.i3];
            float wSum = w2 + w3;
            if (wSum < 1e-10f) return;

            float correction = constraint * stiffness / wSum;

            verts[c.i2] += n1 * (w2 * correction * 0.5f);
            verts[c.i3] -= n2 * (w3 * correction * 0.5f);
        }
    }
}
