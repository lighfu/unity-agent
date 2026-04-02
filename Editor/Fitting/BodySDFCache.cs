using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Session cache for BodySDF instances keyed by avatar InstanceID.
    /// Avoids rebuilding the SDF (~0.5s) when multiple tools query the same avatar.
    /// Automatically invalidated on PlayMode transitions and domain reload.
    /// </summary>
    internal static class BodySDFCache
    {
        private struct Entry
        {
            public BodySDF sdf;
            public Vector3[] bodyVerts;
            public Vector3[] bodyNormals;
            public SpatialGrid bodyGrid;
        }

        private static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>();

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.playModeStateChanged += _ => Clear();
        }

        public static void Clear() => _cache.Clear();

        /// <summary>
        /// Get or build a BodySDF for the given avatar. Caches the result by InstanceID.
        /// Also returns the body vertices, normals, and spatial grid for callers that need them.
        /// </summary>
        public static BodySDF GetOrBuild(GameObject avatarGo,
            out Vector3[] bodyVerts, out Vector3[] bodyNormals, out SpatialGrid bodyGrid,
            GameObject excludeGo = null)
        {
            int id = avatarGo.GetInstanceID();
            if (_cache.TryGetValue(id, out var entry))
            {
                bodyVerts = entry.bodyVerts;
                bodyNormals = entry.bodyNormals;
                bodyGrid = entry.bodyGrid;
                return entry.sdf;
            }

            var bodyMeshes = FittingHelpers.FindBodyMeshes(avatarGo, excludeGo);
            if (bodyMeshes.Count == 0)
            {
                bodyVerts = null;
                bodyNormals = null;
                bodyGrid = null;
                return null;
            }

            FittingHelpers.BakeWorldVerticesAndNormals(bodyMeshes, out bodyVerts, out bodyNormals);
            float cellSize = SpatialGrid.EstimateCellSize(bodyVerts);
            bodyGrid = new SpatialGrid(bodyVerts, cellSize);

            // Adaptive resolution for small avatars:
            // ~1.0m avatar at res=64 → voxelSize=15.6mm, thin limbs get only ~4 voxels
            // blurRadius=3 washes out SDF sign → zero-crossing lost → SnapToBodySurface fails
            // res=96 + blurRadius=2 preserves sign for limbs ≥5.8 voxels wide
            Vector3 bMin = bodyVerts[0], bMax = bodyVerts[0];
            for (int i = 1; i < bodyVerts.Length; i++)
            {
                bMin = Vector3.Min(bMin, bodyVerts[i]);
                bMax = Vector3.Max(bMax, bodyVerts[i]);
            }
            float longestAxis = Mathf.Max(bMax.x - bMin.x,
                Mathf.Max(bMax.y - bMin.y, bMax.z - bMin.z));

            int resolution = longestAxis < 1.2f ? 96 : 64;
            int blurRadius = longestAxis < 1.2f ? 2 : 3;

            var sdf = new BodySDF(bodyVerts, bodyNormals, bodyGrid,
                resolution: resolution, blurRadius: blurRadius);

            _cache[id] = new Entry
            {
                sdf = sdf,
                bodyVerts = bodyVerts,
                bodyNormals = bodyNormals,
                bodyGrid = bodyGrid
            };

            return sdf;
        }
    }
}
