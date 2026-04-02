using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Cell-based 3D spatial index for fast K-nearest-neighbor queries.
    /// </summary>
    internal class SpatialGrid
    {
        private readonly Vector3[] _positions;
        private readonly float _cellSize;
        private readonly float _invCellSize;
        private readonly Dictionary<long, List<int>> _cells;

        public SpatialGrid(Vector3[] positions, float cellSize)
        {
            _positions = positions;
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
            _cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < positions.Length; i++)
            {
                long key = CellKey(positions[i]);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _cells[key] = list;
                }
                list.Add(i);
            }
        }

        public void FindKNearest(Vector3 point, int k, out int[] indices, out float[] distances)
        {
            var candidates = new List<(int idx, float dist)>();
            int cx = Mathf.FloorToInt(point.x * _invCellSize);
            int cy = Mathf.FloorToInt(point.y * _invCellSize);
            int cz = Mathf.FloorToInt(point.z * _invCellSize);

            GatherCandidates(candidates, point, cx, cy, cz, 1);

            if (candidates.Count < k)
                GatherCandidates(candidates, point, cx, cy, cz, 2);
            if (candidates.Count < k)
                GatherCandidates(candidates, point, cx, cy, cz, 3);

            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
            int count = Mathf.Min(k, candidates.Count);
            indices = new int[count];
            distances = new float[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = candidates[i].idx;
                distances[i] = candidates[i].dist;
            }
        }

        private void GatherCandidates(List<(int, float)> candidates, Vector3 point,
            int cx, int cy, int cz, int radius)
        {
            candidates.Clear();
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                long key = PackKey(cx + dx, cy + dy, cz + dz);
                if (!_cells.TryGetValue(key, out var list)) continue;
                foreach (int idx in list)
                {
                    float dist = Vector3.Distance(point, _positions[idx]);
                    candidates.Add((idx, dist));
                }
            }
        }

        private long CellKey(Vector3 p)
        {
            return PackKey(
                Mathf.FloorToInt(p.x * _invCellSize),
                Mathf.FloorToInt(p.y * _invCellSize),
                Mathf.FloorToInt(p.z * _invCellSize));
        }

        private static long PackKey(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        /// <summary>
        /// Adaptively estimate cell size from a position array.
        /// </summary>
        public static float EstimateCellSize(Vector3[] positions)
        {
            if (positions.Length < 2) return 0.01f;
            Vector3 min = positions[0], max = positions[0];
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 extent = max - min;
            float avgExtent = (extent.x + extent.y + extent.z) / 3f;
            return Mathf.Max(avgExtent / 50f, 0.005f);
        }
    }
}
