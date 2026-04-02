using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Green Coordinates cage-based deformation.
    /// Provides Air Gap protection through geometrically-aware cage deformation.
    /// </summary>
    internal static class GreenCoordinates
    {
        public static int CageVertexCount = 200;
        public static float CageOffset = 0.02f;

        /// <summary>
        /// Try to apply Green Coordinates deformation to the mesh.
        /// Returns true if successfully applied.
        /// </summary>
        public static bool TryApply(Vector3[] workVerts, FittingTopology topo,
            Vector3[] bodyVerts, Vector3[] bodyNormals, SpatialGrid bodyGrid)
        {
            // Generate cage
            var cage = GenerateCage(workVerts, topo.RootIndices, CageVertexCount, CageOffset);
            if (cage == null) return false;

            // Compute GC coordinates (phi for vertices, psi for faces)
            ComputeCoordinates(workVerts, topo.RootIndices, cage,
                out float[][] phi, out float[][] psi);

            // Deform cage to target body
            DeformCage(cage, bodyVerts, bodyNormals, bodyGrid);

            // Apply deformation
            ApplyDeformation(workVerts, topo.RootIndices, cage, phi, psi);

            topo.SyncSplitVertices(workVerts);
            return true;
        }

        // ─── Cage Generation ───

        private class Cage
        {
            public Vector3[] Vertices;
            public int[][] Faces; // Each face is 3-4 vertex indices
            public Vector3[] FaceNormals;
            public float[] FaceAreas;
        }

        private static Cage GenerateCage(Vector3[] meshVerts, List<int> rootIndices,
            int targetVertCount, float offset)
        {
            // Compute AABB
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (int vi in rootIndices)
            {
                min = Vector3.Min(min, meshVerts[vi]);
                max = Vector3.Max(max, meshVerts[vi]);
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 extent = (max - min) * 0.5f + Vector3.one * offset;

            // Determine subdivision level per face to approach targetVertCount
            // A subdivided box: 6 faces, each subdivided into NxN quads = 6*N*N quads
            // Vertices ~= 6*N*N*4 - shared = roughly 6*(N+1)^2
            int subdivPerAxis = Mathf.Max(2, Mathf.RoundToInt(Mathf.Sqrt(targetVertCount / 6f)));

            // Generate subdivided box faces
            var vertexList = new List<Vector3>();
            var faceList = new List<int[]>();
            var vertexMap = new Dictionary<long, int>(); // (quantized position -> vertex index)

            // 6 faces of the box: +X, -X, +Y, -Y, +Z, -Z
            Vector3[] faceNormals = {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
            };
            Vector3[] faceU = {
                Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.right, Vector3.left
            };
            Vector3[] faceV = {
                Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up
            };

            for (int face = 0; face < 6; face++)
            {
                Vector3 normal = faceNormals[face];
                Vector3 u = faceU[face];
                Vector3 v = faceV[face];
                Vector3 faceCenter = center + Vector3.Scale(normal, extent);

                float uExtent = Mathf.Abs(Vector3.Dot(u, extent));
                float vExtent = Mathf.Abs(Vector3.Dot(v, extent));

                for (int iu = 0; iu < subdivPerAxis; iu++)
                for (int iv = 0; iv < subdivPerAxis; iv++)
                {
                    float u0 = -1f + 2f * iu / subdivPerAxis;
                    float u1 = -1f + 2f * (iu + 1) / subdivPerAxis;
                    float v0 = -1f + 2f * iv / subdivPerAxis;
                    float v1 = -1f + 2f * (iv + 1) / subdivPerAxis;

                    Vector3 p00 = faceCenter + u * uExtent * u0 + v * vExtent * v0;
                    Vector3 p10 = faceCenter + u * uExtent * u1 + v * vExtent * v0;
                    Vector3 p11 = faceCenter + u * uExtent * u1 + v * vExtent * v1;
                    Vector3 p01 = faceCenter + u * uExtent * u0 + v * vExtent * v1;

                    int i00 = GetOrAddVertex(vertexList, vertexMap, p00);
                    int i10 = GetOrAddVertex(vertexList, vertexMap, p10);
                    int i11 = GetOrAddVertex(vertexList, vertexMap, p11);
                    int i01 = GetOrAddVertex(vertexList, vertexMap, p01);

                    // Two triangles per quad
                    faceList.Add(new[] { i00, i10, i11 });
                    faceList.Add(new[] { i00, i11, i01 });
                }
            }

            // Shrinkwrap: project cage vertices toward mesh surface
            var meshGrid = new SpatialGrid(meshVerts, SpatialGrid.EstimateCellSize(meshVerts));
            var cageVerts = vertexList.ToArray();

            for (int i = 0; i < cageVerts.Length; i++)
            {
                Vector3 dirToCenter = (center - cageVerts[i]).normalized;
                meshGrid.FindKNearest(cageVerts[i], 4, out var idx, out var dist);
                if (idx.Length > 0)
                {
                    // Find closest mesh point along shrinkwrap direction
                    Vector3 closestMeshPt = Vector3.zero;
                    float minDist = float.MaxValue;
                    for (int k = 0; k < idx.Length; k++)
                    {
                        float d = Vector3.Distance(cageVerts[i], meshVerts[idx[k]]);
                        if (d < minDist)
                        {
                            minDist = d;
                            closestMeshPt = meshVerts[idx[k]];
                        }
                    }

                    // Move cage vertex toward mesh surface but keep offset
                    Vector3 dirFromCenter = (cageVerts[i] - center).normalized;
                    float meshDist = Vector3.Dot(closestMeshPt - center, dirFromCenter);
                    float cageDist = Vector3.Dot(cageVerts[i] - center, dirFromCenter);

                    // Only shrink inward, never expand beyond original AABB
                    float targetDist = meshDist + offset;
                    if (targetDist < cageDist)
                    {
                        cageVerts[i] = center + dirFromCenter * targetDist;
                    }
                }
            }

            // Compute face normals and areas
            var cage = new Cage
            {
                Vertices = cageVerts,
                Faces = faceList.ToArray(),
                FaceNormals = new Vector3[faceList.Count],
                FaceAreas = new float[faceList.Count]
            };

            for (int fi = 0; fi < cage.Faces.Length; fi++)
            {
                var f = cage.Faces[fi];
                Vector3 a = cage.Vertices[f[0]];
                Vector3 b = cage.Vertices[f[1]];
                Vector3 c = cage.Vertices[f[2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                cage.FaceAreas[fi] = area;
                cage.FaceNormals[fi] = area > 1e-10f ? cross.normalized : Vector3.up;
            }

            return cage;
        }

        private static int GetOrAddVertex(List<Vector3> vertexList, Dictionary<long, int> map, Vector3 pos)
        {
            // Quantize position to merge close vertices
            long key = QuantizePosition(pos);
            if (map.TryGetValue(key, out int existing))
                return existing;
            int idx = vertexList.Count;
            vertexList.Add(pos);
            map[key] = idx;
            return idx;
        }

        private static long QuantizePosition(Vector3 p)
        {
            const float scale = 10000f; // 0.1mm precision
            int x = Mathf.RoundToInt(p.x * scale);
            int y = Mathf.RoundToInt(p.y * scale);
            int z = Mathf.RoundToInt(p.z * scale);
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        // ─── GC Coordinate Computation ───

        private static void ComputeCoordinates(Vector3[] meshVerts, List<int> rootIndices,
            Cage cage, out float[][] phi, out float[][] psi)
        {
            int nVerts = meshVerts.Length;
            int nCageVerts = cage.Vertices.Length;
            int nCageFaces = cage.Faces.Length;

            phi = new float[nVerts][];
            psi = new float[nVerts][];

            foreach (int vi in rootIndices)
            {
                phi[vi] = new float[nCageVerts];
                psi[vi] = new float[nCageFaces];

                Vector3 eta = meshVerts[vi];

                // Compute phi (vertex coordinates) using signed solid angle
                for (int fi = 0; fi < nCageFaces; fi++)
                {
                    var f = cage.Faces[fi];
                    Vector3 a = cage.Vertices[f[0]] - eta;
                    Vector3 b = cage.Vertices[f[1]] - eta;
                    Vector3 c = cage.Vertices[f[2]] - eta;

                    float solidAngle = SignedSolidAngle(a, b, c);

                    // Distribute solid angle to vertices
                    phi[vi][f[0]] += solidAngle / 3f;
                    phi[vi][f[1]] += solidAngle / 3f;
                    phi[vi][f[2]] += solidAngle / 3f;

                    // Compute psi (face coordinates) using Green's function integral
                    psi[vi][fi] = ComputePsiFace(eta, cage.Vertices[f[0]], cage.Vertices[f[1]], cage.Vertices[f[2]],
                        cage.FaceNormals[fi]);
                }

                // Normalize phi so they sum to 1
                float phiSum = 0f;
                for (int i = 0; i < nCageVerts; i++) phiSum += phi[vi][i];
                if (Mathf.Abs(phiSum) > 1e-8f)
                {
                    float invSum = 1f / phiSum;
                    for (int i = 0; i < nCageVerts; i++) phi[vi][i] *= invSum;
                }
            }
        }

        private static float SignedSolidAngle(Vector3 a, Vector3 b, Vector3 c)
        {
            float la = a.magnitude, lb = b.magnitude, lc = c.magnitude;
            if (la < 1e-10f || lb < 1e-10f || lc < 1e-10f) return 0f;

            // Van Oosterom-Strackee formula
            float num = Vector3.Dot(a, Vector3.Cross(b, c));
            float den = la * lb * lc
                      + Vector3.Dot(a, b) * lc
                      + Vector3.Dot(b, c) * la
                      + Vector3.Dot(a, c) * lb;

            return 2f * Mathf.Atan2(num, den);
        }

        private static float ComputePsiFace(Vector3 eta, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 faceNormal)
        {
            // Simplified Green's function integral for a triangle face
            // Based on the GBC (Generalized Barycentric Coordinates) formulation
            Vector3 centroid = (v0 + v1 + v2) / 3f;
            Vector3 diff = eta - centroid;
            float dist = diff.magnitude;
            if (dist < 1e-8f) return 0f;

            float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;

            // Green's function contribution: integral of (1/|eta-x|) * n_j over face j
            // Approximate using centroid for now (will be refined for accuracy)
            float normalDist = Vector3.Dot(diff, faceNormal);
            float psi = -normalDist * area / (4f * Mathf.PI * dist * dist * dist);

            return psi;
        }

        // ─── Cage Deformation ───

        private static void DeformCage(Cage cage, Vector3[] bodyVerts, Vector3[] bodyNormals, SpatialGrid bodyGrid)
        {
            for (int i = 0; i < cage.Vertices.Length; i++)
            {
                bodyGrid.FindKNearest(cage.Vertices[i], 4, out var idx, out var dist);
                if (idx.Length == 0) continue;

                FittingHelpers.ComputeWeightedSurface(bodyVerts, bodyNormals, idx, dist,
                    out var surfPt, out var surfNrm);

                // Move cage vertex to follow body surface with offset
                float signedDist = Vector3.Dot(cage.Vertices[i] - surfPt, surfNrm);
                if (signedDist < CageOffset)
                {
                    cage.Vertices[i] += surfNrm * (CageOffset - signedDist);
                }
            }

            // Recompute face normals and areas after deformation
            for (int fi = 0; fi < cage.Faces.Length; fi++)
            {
                var f = cage.Faces[fi];
                Vector3 a = cage.Vertices[f[0]];
                Vector3 b = cage.Vertices[f[1]];
                Vector3 c = cage.Vertices[f[2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                cage.FaceAreas[fi] = area;
                cage.FaceNormals[fi] = area > 1e-10f ? cross.normalized : Vector3.up;
            }
        }

        // ─── Apply Deformation ───

        private static void ApplyDeformation(Vector3[] meshVerts, List<int> rootIndices,
            Cage cage, float[][] phi, float[][] psi)
        {
            foreach (int vi in rootIndices)
            {
                if (phi[vi] == null) continue;

                Vector3 newPos = Vector3.zero;

                // Vertex contribution: sum phi_i * v'_i
                for (int ci = 0; ci < cage.Vertices.Length; ci++)
                {
                    newPos += phi[vi][ci] * cage.Vertices[ci];
                }

                // Face contribution: sum psi_j * s_j * n'_j
                for (int fi = 0; fi < cage.Faces.Length; fi++)
                {
                    float scaleFactor = Mathf.Sqrt(cage.FaceAreas[fi]);
                    newPos += psi[vi][fi] * scaleFactor * cage.FaceNormals[fi];
                }

                meshVerts[vi] = newPos;
            }
        }
    }
}
