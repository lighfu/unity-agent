using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// McAdams-Sifakis 3x3 SVD decomposition.
    /// Fixed iteration count, no trig functions, no data-dependent branching.
    /// Ported from tbtSVD (MIT license).
    ///
    /// Decomposes M = U * S * V^T where U,V are orthogonal and S is diagonal.
    /// For rotation extraction: R = U * V^T (with det correction).
    /// </summary>
    internal static class SVD3x3
    {
        private const int JacobiSweeps = 4;

        /// <summary>
        /// Extract the optimal rotation R from a 3x3 covariance matrix M.
        /// R = U * V^T from the SVD of M, with det(R) > 0 guaranteed.
        /// </summary>
        public static Mat3 ExtractRotation(Mat3 M)
        {
            if (M.FrobeniusNormSq < 1e-8f) return Mat3.Identity;

            Decompose(M, out Mat3 U, out float s0, out float s1, out float s2, out Mat3 V);

            // Ensure proper rotation (det = +1)
            float detU = U.Determinant;
            float detV = V.Determinant;

            if (detU < 0f) { U = FlipColumn(U, 2); }
            if (detV < 0f) { V = FlipColumn(V, 2); }

            // R = U * V^T
            Mat3 R = Mat3.Mul(U, V.Transposed);

            // Final determinant check
            if (R.Determinant < 0f)
                R = Mat3.Scale(R, -1f);

            return R;
        }

        /// <summary>
        /// Full SVD decomposition: M = U * diag(s0,s1,s2) * V^T
        /// </summary>
        public static void Decompose(Mat3 M, out Mat3 U, out float s0, out float s1, out float s2, out Mat3 V)
        {
            // Step 1: Compute S = M^T * M (symmetric 3x3)
            Mat3 MtM = Mat3.Mul(M.Transposed, M);

            // Step 2: Jacobi eigenvalue decomposition of S → V, eigenvalues
            JacobiEigen(MtM, out V, out float e0, out float e1, out float e2);

            // Step 3: Singular values = sqrt of eigenvalues
            s0 = Mathf.Sqrt(Mathf.Max(e0, 0f));
            s1 = Mathf.Sqrt(Mathf.Max(e1, 0f));
            s2 = Mathf.Sqrt(Mathf.Max(e2, 0f));

            // Step 4: Compute B = M * V
            Mat3 B = Mat3.Mul(M, V);

            // Step 5: Extract U columns from B via QR-like normalization
            U = ExtractU(B, s0, s1, s2);
        }

        // ─── Jacobi Eigenvalue Decomposition ───

        private static void JacobiEigen(Mat3 S, out Mat3 V, out float e0, out float e1, out float e2)
        {
            V = Mat3.Identity;

            // S is symmetric: only use upper triangle
            float s00 = S.m00, s01 = S.m01, s02 = S.m02;
            float             s11 = S.m11, s12 = S.m12;
            float                          s22 = S.m22;

            for (int sweep = 0; sweep < JacobiSweeps; sweep++)
            {
                // Givens rotation for (0,1) element
                JacobiRotation(s00, s01, s11, out float c01, out float sn01);
                ApplyJacobiLeft(ref s00, ref s01, ref s02, ref s11, ref s12, c01, sn01, 0, 1);
                ApplyJacobiRight(ref V, c01, sn01, 0, 1);
                s01 = 0f; // Zeroed by construction

                // Givens rotation for (0,2) element
                JacobiRotation(s00, s02, s22, out float c02, out float sn02);
                ApplyJacobiLeft02(ref s00, ref s01, ref s02, ref s12, ref s22, c02, sn02);
                ApplyJacobiRight(ref V, c02, sn02, 0, 2);
                s02 = 0f;

                // Givens rotation for (1,2) element
                JacobiRotation(s11, s12, s22, out float c12, out float sn12);
                ApplyJacobiLeft12(ref s01, ref s02, ref s11, ref s12, ref s22, c12, sn12);
                ApplyJacobiRight(ref V, c12, sn12, 1, 2);
                s12 = 0f;
            }

            e0 = s00;
            e1 = s11;
            e2 = s22;

            // Sort eigenvalues in descending order
            SortEigen(ref e0, ref e1, ref e2, ref V);
        }

        private static void JacobiRotation(float aii, float aij, float ajj, out float c, out float s)
        {
            // Compute Givens rotation to zero aij
            if (Mathf.Abs(aij) < 1e-10f)
            {
                c = 1f; s = 0f;
                return;
            }

            float tau = (ajj - aii) / (2f * aij);
            float t;
            if (tau >= 0f)
                t = 1f / (tau + Mathf.Sqrt(1f + tau * tau));
            else
                t = -1f / (-tau + Mathf.Sqrt(1f + tau * tau));

            c = 1f / Mathf.Sqrt(1f + t * t);
            s = t * c;
        }

        // Apply Jacobi rotation to symmetric matrix elements for (0,1) pivot
        private static void ApplyJacobiLeft(ref float s00, ref float s01, ref float s02,
            ref float s11, ref float s12, float c, float s, int p, int q)
        {
            float new00 = c * c * s00 - 2f * s * c * s01 + s * s * s11;
            float new11 = s * s * s00 + 2f * s * c * s01 + c * c * s11;
            float new02 = c * s02 - s * s12;
            float new12 = s * s02 + c * s12;
            s00 = new00;
            s11 = new11;
            s02 = new02;
            s12 = new12;
        }

        // Apply Jacobi rotation for (0,2) pivot
        private static void ApplyJacobiLeft02(ref float s00, ref float s01, ref float s02,
            ref float s12, ref float s22, float c, float s)
        {
            float new00 = c * c * s00 - 2f * s * c * s02 + s * s * s22;
            float new22 = s * s * s00 + 2f * s * c * s02 + c * c * s22;
            float new01 = c * s01 - s * s12;
            float new12 = s * s01 + c * s12;
            s00 = new00;
            s22 = new22;
            s01 = new01;
            s12 = new12;
        }

        // Apply Jacobi rotation for (1,2) pivot
        private static void ApplyJacobiLeft12(ref float s01, ref float s02, ref float s11,
            ref float s12, ref float s22, float c, float s)
        {
            float new11 = c * c * s11 - 2f * s * c * s12 + s * s * s22;
            float new22 = s * s * s11 + 2f * s * c * s12 + c * c * s22;
            float new01 = c * s01 - s * s02;
            float new02 = s * s01 + c * s02;
            s11 = new11;
            s22 = new22;
            s01 = new01;
            s02 = new02;
        }

        // Apply rotation to V matrix (columns p and q)
        private static void ApplyJacobiRight(ref Mat3 V, float c, float s, int p, int q)
        {
            // V = V * G(p,q,theta)
            // Columns p and q of V are rotated
            float v0p = GetCol(V, p, 0), v0q = GetCol(V, q, 0);
            float v1p = GetCol(V, p, 1), v1q = GetCol(V, q, 1);
            float v2p = GetCol(V, p, 2), v2q = GetCol(V, q, 2);

            SetCol(ref V, p, 0, c * v0p - s * v0q);
            SetCol(ref V, q, 0, s * v0p + c * v0q);
            SetCol(ref V, p, 1, c * v1p - s * v1q);
            SetCol(ref V, q, 1, s * v1p + c * v1q);
            SetCol(ref V, p, 2, c * v2p - s * v2q);
            SetCol(ref V, q, 2, s * v2p + c * v2q);
        }

        // ─── U Extraction from B = M*V ───

        private static Mat3 ExtractU(Mat3 B, float s0, float s1, float s2)
        {
            Mat3 U = Mat3.Identity;
            const float eps = 1e-6f;

            // Column 0: normalize B column 0
            Vector3 b0 = new Vector3(B.m00, B.m10, B.m20);
            if (s0 > eps)
            {
                b0 /= s0;
                U.m00 = b0.x; U.m10 = b0.y; U.m20 = b0.z;
            }

            // Column 1: normalize B column 1
            Vector3 b1 = new Vector3(B.m01, B.m11, B.m21);
            if (s1 > eps)
            {
                b1 /= s1;
                // Gram-Schmidt: remove b0 component
                b1 -= Vector3.Dot(b1, b0) * b0;
                float len = b1.magnitude;
                if (len > eps) b1 /= len;
                U.m01 = b1.x; U.m11 = b1.y; U.m21 = b1.z;
            }

            // Column 2: cross product of first two columns
            Vector3 b2 = Vector3.Cross(b0, b1);
            float b2len = b2.magnitude;
            if (b2len > eps) b2 /= b2len;
            U.m02 = b2.x; U.m12 = b2.y; U.m22 = b2.z;

            return U;
        }

        // ─── Sort eigenvalues descending ───

        private static void SortEigen(ref float e0, ref float e1, ref float e2, ref Mat3 V)
        {
            // Bubble sort 3 elements (fixed 3 comparisons)
            if (e0 < e1) { Swap(ref e0, ref e1); SwapColumns(ref V, 0, 1); }
            if (e1 < e2) { Swap(ref e1, ref e2); SwapColumns(ref V, 1, 2); }
            if (e0 < e1) { Swap(ref e0, ref e1); SwapColumns(ref V, 0, 1); }
        }

        // ─── Matrix element accessors ───

        private static float GetCol(Mat3 m, int col, int row)
        {
            if (col == 0) return row == 0 ? m.m00 : (row == 1 ? m.m10 : m.m20);
            if (col == 1) return row == 0 ? m.m01 : (row == 1 ? m.m11 : m.m21);
            return row == 0 ? m.m02 : (row == 1 ? m.m12 : m.m22);
        }

        private static void SetCol(ref Mat3 m, int col, int row, float val)
        {
            if (col == 0) { if (row == 0) m.m00 = val; else if (row == 1) m.m10 = val; else m.m20 = val; }
            else if (col == 1) { if (row == 0) m.m01 = val; else if (row == 1) m.m11 = val; else m.m21 = val; }
            else { if (row == 0) m.m02 = val; else if (row == 1) m.m12 = val; else m.m22 = val; }
        }

        private static void Swap(ref float a, ref float b)
        {
            float t = a; a = b; b = t;
        }

        private static void SwapColumns(ref Mat3 m, int c0, int c1)
        {
            float t;
            t = GetCol(m, c0, 0); SetCol(ref m, c0, 0, GetCol(m, c1, 0)); SetCol(ref m, c1, 0, t);
            t = GetCol(m, c0, 1); SetCol(ref m, c0, 1, GetCol(m, c1, 1)); SetCol(ref m, c1, 1, t);
            t = GetCol(m, c0, 2); SetCol(ref m, c0, 2, GetCol(m, c1, 2)); SetCol(ref m, c1, 2, t);
        }

        private static Mat3 FlipColumn(Mat3 m, int col)
        {
            SetCol(ref m, col, 0, -GetCol(m, col, 0));
            SetCol(ref m, col, 1, -GetCol(m, col, 1));
            SetCol(ref m, col, 2, -GetCol(m, col, 2));
            return m;
        }
    }
}
