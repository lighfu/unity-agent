using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// 3x3 matrix for ARAP and SVD computations.
    /// </summary>
    internal struct Mat3
    {
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;

        public static readonly Mat3 Identity = new Mat3
        {
            m00 = 1f, m11 = 1f, m22 = 1f
        };

        public float Determinant =>
            m00 * (m11 * m22 - m12 * m21) -
            m01 * (m10 * m22 - m12 * m20) +
            m02 * (m10 * m21 - m11 * m20);

        public Mat3 Transposed => new Mat3
        {
            m00 = m00, m01 = m10, m02 = m20,
            m10 = m01, m11 = m11, m12 = m21,
            m20 = m02, m21 = m12, m22 = m22
        };

        public Mat3 Inverse
        {
            get
            {
                float det = Determinant;
                if (Mathf.Abs(det) < 1e-10f) return Identity;
                float inv = 1f / det;
                return new Mat3
                {
                    m00 = (m11 * m22 - m12 * m21) * inv,
                    m01 = (m02 * m21 - m01 * m22) * inv,
                    m02 = (m01 * m12 - m02 * m11) * inv,
                    m10 = (m12 * m20 - m10 * m22) * inv,
                    m11 = (m00 * m22 - m02 * m20) * inv,
                    m12 = (m02 * m10 - m00 * m12) * inv,
                    m20 = (m10 * m21 - m11 * m20) * inv,
                    m21 = (m01 * m20 - m00 * m21) * inv,
                    m22 = (m00 * m11 - m01 * m10) * inv
                };
            }
        }

        public float FrobeniusNormSq =>
            m00 * m00 + m01 * m01 + m02 * m02 +
            m10 * m10 + m11 * m11 + m12 * m12 +
            m20 * m20 + m21 * m21 + m22 * m22;

        public static Mat3 OuterProduct(Vector3 a, Vector3 b)
        {
            return new Mat3
            {
                m00 = a.x * b.x, m01 = a.x * b.y, m02 = a.x * b.z,
                m10 = a.y * b.x, m11 = a.y * b.y, m12 = a.y * b.z,
                m20 = a.z * b.x, m21 = a.z * b.y, m22 = a.z * b.z
            };
        }

        public static Mat3 Add(Mat3 a, Mat3 b)
        {
            return new Mat3
            {
                m00 = a.m00 + b.m00, m01 = a.m01 + b.m01, m02 = a.m02 + b.m02,
                m10 = a.m10 + b.m10, m11 = a.m11 + b.m11, m12 = a.m12 + b.m12,
                m20 = a.m20 + b.m20, m21 = a.m21 + b.m21, m22 = a.m22 + b.m22
            };
        }

        public static Mat3 Scale(Mat3 m, float s)
        {
            return new Mat3
            {
                m00 = m.m00 * s, m01 = m.m01 * s, m02 = m.m02 * s,
                m10 = m.m10 * s, m11 = m.m11 * s, m12 = m.m12 * s,
                m20 = m.m20 * s, m21 = m.m21 * s, m22 = m.m22 * s
            };
        }

        public static Vector3 Mul(Mat3 m, Vector3 v)
        {
            return new Vector3(
                m.m00 * v.x + m.m01 * v.y + m.m02 * v.z,
                m.m10 * v.x + m.m11 * v.y + m.m12 * v.z,
                m.m20 * v.x + m.m21 * v.y + m.m22 * v.z);
        }

        public static Mat3 Mul(Mat3 a, Mat3 b)
        {
            return new Mat3
            {
                m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20,
                m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21,
                m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
                m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20,
                m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21,
                m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
                m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20,
                m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21,
                m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22
            };
        }

        /// <summary>
        /// Extract rotation via polar decomposition (Newton iteration, 8 steps).
        /// Legacy method preserved for backward compatibility.
        /// </summary>
        public static Mat3 ExtractRotationPolar(Mat3 M)
        {
            if (M.FrobeniusNormSq < 1e-10f) return Identity;

            Mat3 R = M;
            for (int i = 0; i < 8; i++)
            {
                float det = R.Determinant;
                if (Mathf.Abs(det) < 1e-8f) return Identity;
                Mat3 RInvT = R.Inverse.Transposed;
                R = Scale(Add(R, RInvT), 0.5f);
            }

            if (R.Determinant < 0f)
                R = Scale(R, -1f);
            return R;
        }
    }
}
