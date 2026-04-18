using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class HierarchyTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("List all root GameObjects in the active scene with name, active state, child count, and components.")]
        public static string ListRootObjects()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            if (roots.Length == 0) return "No root objects in the active scene.";

            var sb = new StringBuilder();
            sb.AppendLine($"Active Scene: {scene.name} ({roots.Length} root objects)");

            foreach (var go in roots)
            {
                var components = go.GetComponents<Component>()
                    .Where(c => c != null && !(c is Transform))
                    .Select(c => c.GetType().Name);
                string compList = string.Join(", ", components);

                sb.AppendLine($"- {go.name} | Active: {go.activeSelf} | Children: {go.transform.childCount} | Components: [{compList}]");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List direct children of a GameObject.")]
        public static string ListChildren(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found. Use FindObjectsByName to search.";

            int count = go.transform.childCount;
            if (count == 0) return $"'{name}' has no children.";

            var sb = new StringBuilder();
            sb.AppendLine($"Children of '{name}' ({count}):");

            for (int i = 0; i < count; i++)
            {
                var child = go.transform.GetChild(i);
                var components = child.GetComponents<Component>()
                    .Where(c => c != null && !(c is Transform))
                    .Select(c => c.GetType().Name);
                string compList = string.Join(", ", components);

                sb.AppendLine($"  {i}: {child.name} | Active: {child.gameObject.activeSelf} | Children: {child.childCount} | Components: [{compList}]");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Get a hierarchy tree view of a GameObject with indentation. maxDepth limits recursion (default 3).")]
        public static string GetHierarchyTree(string name, int maxDepth = 3)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found. Use FindObjectsByName to search.";

            var sb = new StringBuilder();
            sb.AppendLine($"Hierarchy tree for '{name}' (maxDepth={maxDepth}):");
            BuildTree(sb, go.transform, 0, maxDepth);

            return sb.ToString().TrimEnd();
        }

        private static void BuildTree(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            string indent = new string(' ', depth * 2);
            string activeMarker = t.gameObject.activeSelf ? "" : " [Inactive]";
            sb.AppendLine($"{indent}{t.name}{activeMarker}");

            if (depth >= maxDepth)
            {
                if (t.childCount > 0)
                    sb.AppendLine($"{indent}  ... ({t.childCount} children hidden)");
                return;
            }

            for (int i = 0; i < t.childCount; i++)
            {
                BuildTree(sb, t.GetChild(i), depth + 1, maxDepth);
            }
        }

        [AgentTool("Find all GameObjects that have a specific component type.")]
        public static string FindObjectsByComponent(string componentTypeName)
        {
            var type = ComponentTools.FindComponentType(componentTypeName);
            if (type == null) return $"Error: Component type '{componentTypeName}' not found.";

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var results = new System.Collections.Generic.List<string>();

            foreach (var root in roots)
            {
                var components = root.GetComponentsInChildren(type, true);
                foreach (var comp in components)
                {
                    string path = GetFullPath(comp.transform);
                    string active = comp.gameObject.activeInHierarchy ? "Active" : "Inactive";
                    results.Add($"- {path} ({active})");
                }
            }

            if (results.Count == 0) return $"No GameObjects found with component '{componentTypeName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} object(s) with '{componentTypeName}':");
            int limit = System.Math.Min(results.Count, 50);
            for (int i = 0; i < limit; i++)
                sb.AppendLine(results[i]);

            if (results.Count > 50)
                sb.AppendLine($"... and {results.Count - 50} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all Renderers under a GameObject (including children). Shows renderer type, material names, and mesh info. Useful for finding all mesh parts of an avatar. Hint: mesh name alone can be misleading (e.g. an SMR named 'Body' may actually be the face mesh when its material is 'Avatar_face'). For critical operations that depend on identifying a specific body part, call ScanAvatarMeshes afterwards to visually confirm each mesh.")]
        public static string ListRenderers(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return $"No Renderers found under '{gameObjectName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Renderers under '{gameObjectName}': {renderers.Length} found");
            sb.AppendLine("Hint: identify parts by **material name** first (mesh names can be misleading — e.g. 'Body' + 'Avatar_face' = face mesh).");
            sb.AppendLine("      For critical operations (AO bake, deletion, material swap), call ScanAvatarMeshes to get a labeled grid image for visual verification.");
            sb.AppendLine("---");

            foreach (var r in renderers)
            {
                string path = GetFullPath(r.transform);
                string typeName = r.GetType().Name;
                bool active = r.gameObject.activeInHierarchy;

                sb.AppendLine($"[{typeName}] {path} (Active: {active})");

                // Materials
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string matName = mats[i] != null ? mats[i].name : "null";
                    sb.AppendLine($"  Material[{i}]: {matName}");
                }

                // Mesh info
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    var mesh = smr.sharedMesh;
                    sb.AppendLine($"  Mesh: {mesh.name} (Vertices: {mesh.vertexCount}, SubMeshes: {mesh.subMeshCount})");
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mesh = mf.sharedMesh;
                        sb.AppendLine($"  Mesh: {mesh.name} (Vertices: {mesh.vertexCount}, SubMeshes: {mesh.subMeshCount})");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Get the full transform path from root to a GameObject (e.g. Armature/Hips/Spine).")]
        public static string GetTransformPath(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found. Use FindObjectsByName to search.";

            return $"Path: {GetFullPath(go.transform)}";
        }

        [AgentTool("Find GameObjects by partial name match (case-insensitive). Searches entire scene including inactive objects. Limited to 50 results.")]
        public static string FindObjectsByName(string namePattern)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var results = new System.Collections.Generic.List<string>();

            foreach (var root in roots)
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    if (t.name.IndexOf(namePattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string path = GetFullPath(t);
                        string active = t.gameObject.activeInHierarchy ? "Active" : "Inactive";
                        results.Add($"- {path} ({active})");
                    }
                }
            }

            if (results.Count == 0) return $"No GameObjects found matching '{namePattern}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} object(s) matching '{namePattern}':");
            int limit = System.Math.Min(results.Count, 50);
            for (int i = 0; i < limit; i++)
                sb.AppendLine(results[i]);

            if (results.Count > 50)
                sb.AppendLine($"... and {results.Count - 50} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Find all GameObjects with a specific tag (includes inactive objects). Useful for finding EditorOnly objects. Limited to 50 results.")]
        public static string FindObjectsByTag(string tag)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var results = new System.Collections.Generic.List<string>();

            try
            {
                foreach (var root in roots)
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        if (t.gameObject.CompareTag(tag))
                        {
                            string path = GetFullPath(t);
                            string active = t.gameObject.activeInHierarchy ? "Active" : "Inactive";
                            results.Add($"- {path} ({active})");
                        }
                    }
                }
            }
            catch (UnityException)
            {
                return $"Error: Tag '{tag}' is not defined. Use the Tag Manager to add it first.";
            }

            if (results.Count == 0) return $"No GameObjects found with tag '{tag}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} object(s) with tag '{tag}':");
            int limit = System.Math.Min(results.Count, 50);
            for (int i = 0; i < limit; i++)
                sb.AppendLine(results[i]);

            if (results.Count > 50)
                sb.AppendLine($"... and {results.Count - 50} more.");

            return sb.ToString().TrimEnd();
        }

        // ── Selection ─────────────────────────────────────────────

        [AgentTool("Select a GameObject in the Unity Editor. Highlights it in Hierarchy and Inspector.")]
        public static string SelectGameObject(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found. Use FindObjectsByName to search.";

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            return $"Success: Selected '{name}' (path: {GetFullPath(go.transform)}).";
        }

        [AgentTool("Select multiple GameObjects matching a name pattern. Case-insensitive partial match. Searches entire scene including inactive objects. Limited to 50.")]
        public static string SelectByPattern(string namePattern)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var matches = new System.Collections.Generic.List<GameObject>();

            foreach (var root in roots)
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    if (t.name.IndexOf(namePattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(t.gameObject);
                        if (matches.Count >= 50) break;
                    }
                }
                if (matches.Count >= 50) break;
            }

            if (matches.Count == 0) return $"No GameObjects found matching '{namePattern}'.";

            Selection.objects = matches.Select(go => (Object)go).ToArray();

            var sb = new StringBuilder();
            sb.AppendLine($"Selected {matches.Count} object(s) matching '{namePattern}':");
            foreach (var go in matches)
                sb.AppendLine($"  - {GetFullPath(go.transform)}");

            return sb.ToString().TrimEnd();
        }

        // ── Utility ─────────────────────────────────────────────────

        private static string GetFullPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }
    }
}
