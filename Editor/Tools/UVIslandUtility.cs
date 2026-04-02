using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public class UVIsland
    {
        public List<int> triangleIndices = new List<int>();
        public Rect uvBounds;
    }

    public static class UVIslandDetector
    {
        public static List<UVIsland> DetectIslands(Mesh mesh, bool use3DConnection = false)
        {
            if (mesh == null) return new List<UVIsland>();

            int[] triangles = mesh.triangles;
            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            if (uvs.Length == 0) return new List<UVIsland>();

            int triangleCount = triangles.Length / 3;
            bool[] visited = new bool[triangleCount];
            List<UVIsland> islands = new List<UVIsland>();

            // Build adjacency map (vertex index or 3D position -> triangle indices)
            Dictionary<object, List<int>> connectionMap = new Dictionary<object, List<int>>();
            for (int i = 0; i < triangleCount; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int vIdx = triangles[i * 3 + j];
                    object key = use3DConnection ? (object)vertices[vIdx] : (object)vIdx;

                    if (!connectionMap.ContainsKey(key))
                        connectionMap[key] = new List<int>();
                    connectionMap[key].Add(i);
                }
            }

            // BFS to find connected components
            for (int i = 0; i < triangleCount; i++)
            {
                if (visited[i]) continue;

                UVIsland island = new UVIsland();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                while (queue.Count > 0)
                {
                    int triIdx = queue.Dequeue();
                    island.triangleIndices.Add(triIdx);

                    for (int j = 0; j < 3; j++)
                    {
                        int vIdx = triangles[triIdx * 3 + j];
                        Vector2 uv = uvs[vIdx];

                        minX = Mathf.Min(minX, uv.x);
                        minY = Mathf.Min(minY, uv.y);
                        maxX = Mathf.Max(maxX, uv.x);
                        maxY = Mathf.Max(maxY, uv.y);

                        object key = use3DConnection ? (object)vertices[vIdx] : (object)vIdx;
                        foreach (int neighborTriIdx in connectionMap[key])
                        {
                            if (!visited[neighborTriIdx])
                            {
                                visited[neighborTriIdx] = true;
                                queue.Enqueue(neighborTriIdx);
                            }
                        }
                    }
                }

                island.uvBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                islands.Add(island);
            }

            return islands;
        }

        /// <summary>
        /// Group UV islands that share vertex indices (3D-connected).
        /// Returns a mapping: islandIndex → groupIndex.
        /// Islands in the same group share at least one vertex index in the mesh,
        /// meaning they are geometrically connected in 3D even if separate in UV space.
        /// </summary>
        public static int[] BuildIslandGroups(Mesh mesh, List<UVIsland> islands)
        {
            if (mesh == null || islands == null || islands.Count == 0)
                return new int[0];

            int[] triangles = mesh.triangles;
            int islandCount = islands.Count;

            // Union-Find
            int[] parent = new int[islandCount];
            int[] rank = new int[islandCount];
            for (int i = 0; i < islandCount; i++) parent[i] = i;

            // Build: vertex index → which island(s) use it
            var vertexToIslands = new Dictionary<int, List<int>>();
            for (int isl = 0; isl < islandCount; isl++)
            {
                foreach (int triIdx in islands[isl].triangleIndices)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int vIdx = triangles[triIdx * 3 + j];
                        if (!vertexToIslands.ContainsKey(vIdx))
                            vertexToIslands[vIdx] = new List<int>();

                        var list = vertexToIslands[vIdx];
                        if (list.Count == 0 || list[list.Count - 1] != isl)
                            list.Add(isl);
                    }
                }
            }

            // Union islands that share vertex indices
            foreach (var kvp in vertexToIslands)
            {
                var isls = kvp.Value;
                for (int i = 1; i < isls.Count; i++)
                {
                    Union(parent, rank, isls[0], isls[i]);
                }
            }

            // Flatten: return group index for each island
            int[] groups = new int[islandCount];
            for (int i = 0; i < islandCount; i++)
                groups[i] = Find(parent, i);

            return groups;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // path compression
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int[] rank, int a, int b)
        {
            a = Find(parent, a);
            b = Find(parent, b);
            if (a == b) return;
            if (rank[a] < rank[b]) { int tmp = a; a = b; b = tmp; }
            parent[b] = a;
            if (rank[a] == rank[b]) rank[a]++;
        }
    }
}
