using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Compressed Sparse Row (CSR) matrix for efficient SpMV and PCG solver.
    /// Each "element" is a 3x3 block, but stored as scalar for simplicity since
    /// ARAP systems are solved per-component (x, y, z independently).
    /// </summary>
    internal class SparseMatrixCSR
    {
        public readonly float[] Values;
        public readonly int[] ColIndices;
        public readonly int[] RowPointers;
        public readonly int N; // matrix dimension

        private float[] _diagonal; // extracted diagonal for Jacobi preconditioner

        public SparseMatrixCSR(int n, float[] values, int[] colIndices, int[] rowPointers)
        {
            N = n;
            Values = values;
            ColIndices = colIndices;
            RowPointers = rowPointers;
        }

        /// <summary>
        /// Build CSR matrix from ARAP Laplacian: L_ii = sum_j w_ij + lambda_i,
        /// L_ij = -w_ij for neighbors j.
        /// </summary>
        public static SparseMatrixCSR BuildARAPSystem(
            List<int> rootIndices, List<int>[] adjacency,
            Dictionary<long, float> cotWeights,
            float[] constraintWeights, int vertCount)
        {
            // Count non-zeros
            int nnz = 0;
            var isRoot = new bool[vertCount];
            foreach (int vi in rootIndices) isRoot[vi] = true;

            foreach (int vi in rootIndices)
            {
                nnz++; // diagonal
                nnz += adjacency[vi].Count; // off-diagonals
            }

            var values = new float[nnz];
            var colIndices = new int[nnz];
            var rowPointers = new int[vertCount + 1];

            int ptr = 0;
            for (int vi = 0; vi < vertCount; vi++)
            {
                rowPointers[vi] = ptr;

                if (!isRoot[vi])
                {
                    // Non-root row: identity (diagonal = 1, no off-diagonals)
                    // Actually, skip — non-root vertices are handled by sync
                    continue;
                }

                // Diagonal: sum of cotangent weights + constraint weight
                float diagVal = constraintWeights[vi];
                var adj = adjacency[vi];
                for (int k = 0; k < adj.Count; k++)
                {
                    float w = FittingTopology.GetCotEdgeWeight(cotWeights, vi, adj[k]);
                    diagVal += w;
                }

                // Store off-diagonals first (sorted by column for CSR)
                var offDiag = new List<(int col, float val)>();
                for (int k = 0; k < adj.Count; k++)
                {
                    float w = FittingTopology.GetCotEdgeWeight(cotWeights, vi, adj[k]);
                    offDiag.Add((adj[k], -w));
                }
                offDiag.Sort((a, b) => a.col.CompareTo(b.col));

                // Insert entries: lower off-diags, diagonal, upper off-diags
                foreach (var (col, val) in offDiag)
                {
                    if (col < vi)
                    {
                        values[ptr] = val;
                        colIndices[ptr] = col;
                        ptr++;
                    }
                }

                // Diagonal
                values[ptr] = diagVal;
                colIndices[ptr] = vi;
                ptr++;

                foreach (var (col, val) in offDiag)
                {
                    if (col > vi)
                    {
                        values[ptr] = val;
                        colIndices[ptr] = col;
                        ptr++;
                    }
                }
            }

            rowPointers[vertCount] = ptr;

            // Trim arrays if needed
            if (ptr < nnz)
            {
                var trimValues = new float[ptr];
                var trimCols = new int[ptr];
                System.Array.Copy(values, trimValues, ptr);
                System.Array.Copy(colIndices, trimCols, ptr);
                return new SparseMatrixCSR(vertCount, trimValues, trimCols, rowPointers);
            }

            return new SparseMatrixCSR(vertCount, values, colIndices, rowPointers);
        }

        /// <summary>
        /// Sparse matrix-vector multiply: y = A * x
        /// </summary>
        public void Multiply(float[] x, float[] y)
        {
            for (int i = 0; i < N; i++)
            {
                float sum = 0f;
                for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
                {
                    sum += Values[j] * x[ColIndices[j]];
                }
                y[i] = sum;
            }
        }

        /// <summary>
        /// Extract diagonal elements for Jacobi preconditioner.
        /// </summary>
        public float[] ExtractDiagonal()
        {
            if (_diagonal != null) return _diagonal;

            _diagonal = new float[N];
            for (int i = 0; i < N; i++)
            {
                for (int j = RowPointers[i]; j < RowPointers[i + 1]; j++)
                {
                    if (ColIndices[j] == i)
                    {
                        _diagonal[i] = Values[j];
                        break;
                    }
                }
                if (_diagonal[i] < 1e-10f)
                    _diagonal[i] = 1f; // Fallback for safety
            }
            return _diagonal;
        }
    }

    /// <summary>
    /// Preconditioned Conjugate Gradient solver for SPD systems.
    /// Uses Jacobi (diagonal) preconditioner.
    /// </summary>
    internal static class PCGSolver
    {
        public const int MaxIterations = 200;
        public const float Tolerance = 1e-5f;

        /// <summary>
        /// Solve A * x = b where A is SPD.
        /// x is used as initial guess and overwritten with the solution.
        /// Returns number of iterations performed.
        /// </summary>
        public static int Solve(SparseMatrixCSR A, float[] b, float[] x)
        {
            int n = A.N;
            float[] diag = A.ExtractDiagonal();

            var r = new float[n]; // residual
            var z = new float[n]; // preconditioned residual
            var p = new float[n]; // search direction
            var Ap = new float[n]; // A * p

            // r = b - A*x
            A.Multiply(x, Ap);
            for (int i = 0; i < n; i++) r[i] = b[i] - Ap[i];

            // z = M^-1 * r (Jacobi: z_i = r_i / diag_i)
            for (int i = 0; i < n; i++) z[i] = r[i] / diag[i];

            // p = z
            System.Array.Copy(z, p, n);

            float rz = Dot(r, z, n);

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                // Check convergence
                float rNorm = Dot(r, r, n);
                if (rNorm < Tolerance * Tolerance)
                    return iter;

                // Ap = A * p
                A.Multiply(p, Ap);

                float pAp = Dot(p, Ap, n);
                if (Mathf.Abs(pAp) < 1e-15f)
                    return iter;

                float alpha = rz / pAp;

                // x += alpha * p
                // r -= alpha * Ap
                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * p[i];
                    r[i] -= alpha * Ap[i];
                }

                // z = M^-1 * r
                for (int i = 0; i < n; i++) z[i] = r[i] / diag[i];

                float rzNew = Dot(r, z, n);
                float beta = rzNew / (rz + 1e-15f);
                rz = rzNew;

                // p = z + beta * p
                for (int i = 0; i < n; i++)
                    p[i] = z[i] + beta * p[i];
            }

            return MaxIterations;
        }

        /// <summary>
        /// Solve 3 independent scalar systems for x, y, z components.
        /// A is the same for all 3 components (Laplacian + constraints).
        /// </summary>
        public static int Solve3(SparseMatrixCSR A, Vector3[] rhs, Vector3[] solution, int vertCount)
        {
            int n = A.N;
            var bx = new float[n];
            var by = new float[n];
            var bz = new float[n];
            var xx = new float[n];
            var xy = new float[n];
            var xz = new float[n];

            for (int i = 0; i < vertCount; i++)
            {
                bx[i] = rhs[i].x; by[i] = rhs[i].y; bz[i] = rhs[i].z;
                xx[i] = solution[i].x; xy[i] = solution[i].y; xz[i] = solution[i].z;
            }

            int iters = 0;
            iters += Solve(A, bx, xx);
            iters += Solve(A, by, xy);
            iters += Solve(A, bz, xz);

            for (int i = 0; i < vertCount; i++)
            {
                solution[i] = new Vector3(xx[i], xy[i], xz[i]);
            }

            return iters / 3;
        }

        private static float Dot(float[] a, float[] b, int n)
        {
            float sum = 0f;
            for (int i = 0; i < n; i++) sum += a[i] * b[i];
            return sum;
        }
    }
}
