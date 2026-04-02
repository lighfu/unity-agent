using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Signed Distance Field for a body mesh. Uses voxelization with Gaussian blur
    /// and Laplacian-based concavity detection.
    /// </summary>
    internal class BodySDF
    {
        private readonly float[] _rawGrid;
        private readonly float[] _smoothGrid;
        private readonly float[] _concavityGrid;
        private readonly Vector3 _origin;
        private readonly float _voxelSize;
        private readonly float _invVoxelSize;
        private readonly int _resX, _resY, _resZ;

        public BodySDF(Vector3[] bodyVerts, Vector3[] bodyNormals,
                       SpatialGrid bodyGrid, int resolution = 64, int blurRadius = 2, int kNearest = 6)
        {
            Vector3 min = bodyVerts[0], max = bodyVerts[0];
            for (int i = 1; i < bodyVerts.Length; i++)
            {
                min = Vector3.Min(min, bodyVerts[i]);
                max = Vector3.Max(max, bodyVerts[i]);
            }

            Vector3 extent = max - min;
            float longestAxis = Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
            _voxelSize = longestAxis / resolution;
            _invVoxelSize = 1f / _voxelSize;

            float padding = 4f * _voxelSize;
            min -= Vector3.one * padding;
            max += Vector3.one * padding;
            _origin = min;

            Vector3 size = max - min;
            _resX = Mathf.Max(1, Mathf.CeilToInt(size.x * _invVoxelSize));
            _resY = Mathf.Max(1, Mathf.CeilToInt(size.y * _invVoxelSize));
            _resZ = Mathf.Max(1, Mathf.CeilToInt(size.z * _invVoxelSize));

            int totalVoxels = _resX * _resY * _resZ;
            _rawGrid = new float[totalVoxels];
            _smoothGrid = new float[totalVoxels];

            for (int z = 0; z < _resZ; z++)
            for (int y = 0; y < _resY; y++)
            for (int x = 0; x < _resX; x++)
            {
                Vector3 center = _origin + new Vector3(
                    (x + 0.5f) * _voxelSize,
                    (y + 0.5f) * _voxelSize,
                    (z + 0.5f) * _voxelSize);

                bodyGrid.FindKNearest(center, kNearest, out var idx, out var dist);
                if (idx.Length == 0)
                {
                    _rawGrid[x + y * _resX + z * _resX * _resY] = _voxelSize * resolution;
                    continue;
                }

                FittingHelpers.ComputeWeightedSurface(bodyVerts, bodyNormals, idx, dist,
                    out var surfPt, out var surfNrm);

                float signedDist = Vector3.Dot(center - surfPt, surfNrm);
                _rawGrid[x + y * _resX + z * _resX * _resY] = signedDist;
            }

            GaussianBlur3D(_rawGrid, _smoothGrid, blurRadius);

            _concavityGrid = new float[totalVoxels];
            for (int z = 1; z < _resZ - 1; z++)
            for (int y = 1; y < _resY - 1; y++)
            for (int x = 1; x < _resX - 1; x++)
            {
                int idx = x + y * _resX + z * _resX * _resY;
                float center = _smoothGrid[idx];
                float laplacian =
                    _smoothGrid[idx - 1] + _smoothGrid[idx + 1]
                  + _smoothGrid[idx - _resX] + _smoothGrid[idx + _resX]
                  + _smoothGrid[idx - _resX * _resY] + _smoothGrid[idx + _resX * _resY]
                  - 6f * center;
                _concavityGrid[idx] = laplacian;
            }
        }

        public float SampleRaw(Vector3 worldPos) => SampleGrid(_rawGrid, worldPos);
        public float SampleSmooth(Vector3 worldPos) => SampleGrid(_smoothGrid, worldPos);

        public Vector3 GradientSmooth(Vector3 worldPos)
        {
            float h = _voxelSize * 0.5f;
            float dx = SampleGrid(_smoothGrid, worldPos + new Vector3(h, 0, 0))
                      - SampleGrid(_smoothGrid, worldPos - new Vector3(h, 0, 0));
            float dy = SampleGrid(_smoothGrid, worldPos + new Vector3(0, h, 0))
                      - SampleGrid(_smoothGrid, worldPos - new Vector3(0, h, 0));
            float dz = SampleGrid(_smoothGrid, worldPos + new Vector3(0, 0, h))
                      - SampleGrid(_smoothGrid, worldPos - new Vector3(0, 0, h));
            Vector3 grad = new Vector3(dx, dy, dz);
            float mag = grad.magnitude;
            return mag > 0.00001f ? grad / mag : Vector3.up;
        }

        public float SampleConcavity(Vector3 worldPos) => SampleGrid(_concavityGrid, worldPos);

        private float SampleGrid(float[] grid, Vector3 worldPos)
        {
            float fx = (worldPos.x - _origin.x) * _invVoxelSize - 0.5f;
            float fy = (worldPos.y - _origin.y) * _invVoxelSize - 0.5f;
            float fz = (worldPos.z - _origin.z) * _invVoxelSize - 0.5f;

            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;

            x0 = Mathf.Clamp(x0, 0, _resX - 2);
            y0 = Mathf.Clamp(y0, 0, _resY - 2);
            z0 = Mathf.Clamp(z0, 0, _resZ - 2);

            int sliceXY = _resX * _resY;
            float c000 = grid[x0     + y0       * _resX + z0       * sliceXY];
            float c100 = grid[(x0+1) + y0       * _resX + z0       * sliceXY];
            float c010 = grid[x0     + (y0+1)   * _resX + z0       * sliceXY];
            float c110 = grid[(x0+1) + (y0+1)   * _resX + z0       * sliceXY];
            float c001 = grid[x0     + y0       * _resX + (z0+1)   * sliceXY];
            float c101 = grid[(x0+1) + y0       * _resX + (z0+1)   * sliceXY];
            float c011 = grid[x0     + (y0+1)   * _resX + (z0+1)   * sliceXY];
            float c111 = grid[(x0+1) + (y0+1)   * _resX + (z0+1)   * sliceXY];

            float c00 = Mathf.Lerp(c000, c100, tx);
            float c10 = Mathf.Lerp(c010, c110, tx);
            float c01 = Mathf.Lerp(c001, c101, tx);
            float c11 = Mathf.Lerp(c011, c111, tx);

            float c0 = Mathf.Lerp(c00, c10, ty);
            float c1 = Mathf.Lerp(c01, c11, ty);

            return Mathf.Lerp(c0, c1, tz);
        }

        private void GaussianBlur3D(float[] src, float[] dst, int blurRadius)
        {
            int kernelSize = blurRadius * 2 + 1;
            float[] kernel = new float[kernelSize];
            float sigma = blurRadius * 0.5f;
            float sum = 0f;
            for (int i = 0; i < kernelSize; i++)
            {
                float d = i - blurRadius;
                kernel[i] = Mathf.Exp(-d * d / (2f * sigma * sigma));
                sum += kernel[i];
            }
            for (int i = 0; i < kernelSize; i++) kernel[i] /= sum;

            int total = _resX * _resY * _resZ;
            float[] temp1 = new float[total];
            float[] temp2 = new float[total];
            int sliceXY = _resX * _resY;

            for (int z = 0; z < _resZ; z++)
            for (int y = 0; y < _resY; y++)
            for (int x = 0; x < _resX; x++)
            {
                float val = 0f;
                for (int k = -blurRadius; k <= blurRadius; k++)
                {
                    int sx = Mathf.Clamp(x + k, 0, _resX - 1);
                    val += src[sx + y * _resX + z * sliceXY] * kernel[k + blurRadius];
                }
                temp1[x + y * _resX + z * sliceXY] = val;
            }

            for (int z = 0; z < _resZ; z++)
            for (int y = 0; y < _resY; y++)
            for (int x = 0; x < _resX; x++)
            {
                float val = 0f;
                for (int k = -blurRadius; k <= blurRadius; k++)
                {
                    int sy = Mathf.Clamp(y + k, 0, _resY - 1);
                    val += temp1[x + sy * _resX + z * sliceXY] * kernel[k + blurRadius];
                }
                temp2[x + y * _resX + z * sliceXY] = val;
            }

            for (int z = 0; z < _resZ; z++)
            for (int y = 0; y < _resY; y++)
            for (int x = 0; x < _resX; x++)
            {
                float val = 0f;
                for (int k = -blurRadius; k <= blurRadius; k++)
                {
                    int sz = Mathf.Clamp(z + k, 0, _resZ - 1);
                    val += temp2[x + y * _resX + sz * sliceXY] * kernel[k + blurRadius];
                }
                dst[x + y * _resX + z * sliceXY] = val;
            }
        }
    }
}
