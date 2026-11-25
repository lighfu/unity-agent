using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class PrefabTools
    {
        private static GameObject FindGO(string name) => SceneTools.FindReadableTarget(name);
        [AgentTool("Instantiate a prefab or model asset (.prefab, .fbx, .blend, etc.) into the scene from its asset path. Optionally set a parent by name.")]
        public static string InstantiatePrefab(string assetPath, string parentName = "")
        {
            if (!AssetTools.TryResolveAssetPath(assetPath, out string path, out string error, allowPackages: true)) return error;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return $"Error: '{path}' does not contain a Prefab or model GameObject.";
            if (!SceneTools.TryResolveCreationParent(parentName, out var parent, out error)) return error;

            var instance = (parent == null ? PrefabUtility.InstantiatePrefab(prefab) : PrefabUtility.InstantiatePrefab(prefab, parent.transform)) as GameObject;
            if (instance == null) return $"Error: Failed to instantiate prefab '{path}'.";
            if (parent != null)
            {
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
            instance.name = SceneTools.UniqueSiblingName(instance, instance.name);
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab via Agent");
            return $"Success: Instantiated prefab '{path}' at {SceneTools.DescribeTarget(instance)}.";
        }

        [AgentTool("Save a GameObject as a prefab.")]
        public static string SaveAsPrefab(string gameObjectName, string savePath)
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;
            if (!AssetTools.IsProjectPath(savePath)) return $"Error: Invalid prefab destination '{savePath}'. Pass a full path under Assets/.";

            // Ensure directory exists
            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                // Create folder recursively
                string[] folders = dir.Split('/', '\\');
                string currentPath = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string nextFolder = folders[i];
                    string fullPath = currentPath + "/" + nextFolder;
                    if (!AssetDatabase.IsValidFolder(fullPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, nextFolder);
                    }
                    currentPath = fullPath;
                }
            }

            // Make sure path ends with .prefab
            if (!savePath.EndsWith(".prefab")) savePath += ".prefab";

            PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.UserAction, out bool success);
            if (!success)
                return $"Error: Failed to save '{gameObjectName}' as prefab at '{savePath}'. Check the path is under Assets/ and writable.";
            return $"Success: Saved {SceneTools.DescribeTarget(go)} as prefab at '{savePath}'.";
        }

        [AgentTool("Unpack a prefab instance in the scene. Mode: 'completely' (flatten all nested) or 'root' (one level only).")]
        public static string UnpackPrefab(string gameObjectName, string mode = "completely")
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            Undo.RegisterFullObjectHierarchyUndo(outermost, "Unpack Prefab via Agent");

            if (mode.ToLower() == "completely")
            {
                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                return $"Success: Completely unpacked prefab {SceneTools.DescribeTarget(outermost)}.";
            }
            else
            {
                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
                return $"Success: Unpacked one level of prefab {SceneTools.DescribeTarget(outermost)}.";
            }
        }

        [AgentTool("Get prefab information for a GameObject: whether it's a prefab instance, its source asset path, and any property overrides.")]
        public static string GetPrefabInfo(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {gameObjectName}");

            bool isPartOfPrefab = PrefabUtility.IsPartOfPrefabInstance(go);
            sb.AppendLine($"  Is Prefab Instance: {isPartOfPrefab}");

            if (!isPartOfPrefab)
            {
                bool isAsset = PrefabUtility.IsPartOfPrefabAsset(go);
                sb.AppendLine($"  Is Prefab Asset: {isAsset}");
                return sb.ToString();
            }

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            sb.AppendLine($"  Outermost Root: {outermost.name}");

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            sb.AppendLine($"  Source Asset: {assetPath}");

            var status = PrefabUtility.GetPrefabInstanceStatus(go);
            sb.AppendLine($"  Instance Status: {status}");

            var overrides = PrefabUtility.GetObjectOverrides(outermost, false);
            var addedComponents = PrefabUtility.GetAddedComponents(outermost);
            var removedComponents = PrefabUtility.GetRemovedComponents(outermost);
            var addedObjects = PrefabUtility.GetAddedGameObjects(outermost);

            sb.AppendLine($"  Property Overrides: {overrides.Count}");
            sb.AppendLine($"  Added Components: {addedComponents.Count}");
            sb.AppendLine($"  Removed Components: {removedComponents.Count}");
            sb.AppendLine($"  Added GameObjects: {addedObjects.Count}");

            if (overrides.Count > 0)
            {
                sb.AppendLine("  Override Details:");
                foreach (var ov in overrides)
                {
                    sb.AppendLine($"    - {ov.instanceObject.GetType().Name} on '{(ov.instanceObject as Component)?.gameObject.name ?? ov.instanceObject.name}'");
                }
            }

            return sb.ToString();
        }

        [AgentTool("Apply all prefab overrides on a GameObject back to the source prefab asset.")]
        public static string ApplyPrefabOverrides(string gameObjectName)
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outermost);

            PrefabUtility.ApplyPrefabInstance(outermost, InteractionMode.UserAction);
            return $"Success: Applied all overrides from {SceneTools.DescribeTarget(outermost)} to prefab asset '{assetPath}'.";
        }

        [AgentTool("Revert all prefab overrides on a GameObject, restoring it to match the source prefab asset.")]
        public static string RevertPrefabOverrides(string gameObjectName)
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            Undo.RegisterFullObjectHierarchyUndo(outermost, "Revert Prefab via Agent");

            PrefabUtility.RevertPrefabInstance(outermost, InteractionMode.UserAction);
            return $"Success: Reverted all overrides on {SceneTools.DescribeTarget(outermost)} to match the source prefab.";
        }

        [AgentTool("Create a Prefab Variant from an existing prefab asset. The variant inherits from the base and can have overrides. basePrefabPath: e.g. 'Assets/Prefabs/Base.prefab'. variantPath: output path.")]
        public static string CreatePrefabVariant(string basePrefabPath, string variantPath)
        {
            if (!AssetTools.TryResolveAssetPath(basePrefabPath, out string source, out string error, allowPackages: true)) return error;
            if (!AssetTools.IsProjectPath(variantPath)) return $"Error: Invalid variant destination '{variantPath}'. Pass a full path under Assets/.";
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (basePrefab == null) return $"Error: Base prefab not found at '{basePrefabPath}'.";

            if (!variantPath.EndsWith(".prefab")) variantPath += ".prefab";

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(variantPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                string[] folders = dir.Replace('\\', '/').Split('/');
                string current = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string next = current + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, folders[i]);
                    current = next;
                }
            }

            // Instantiate, save as variant, then destroy. The temp instance must be
            // destroyed even if SaveAsPrefabAsset throws, or it leaks into the open scene.
            var instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            GameObject variant;
            try
            {
                variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out bool success);
                if (!success) variant = null;
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }

            return variant != null
                ? $"Success: Created prefab variant at '{variantPath}' based on '{source}'."
                : $"Error: Failed to create prefab variant at '{variantPath}'.";
        }

        [AgentTool("Get the base prefab of a variant. Returns the inheritance chain. assetPath: path to the prefab asset.")]
        public static string GetPrefabVariantBase(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return $"Error: Prefab not found at '{assetPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Prefab: {assetPath}");

            var assetType = PrefabUtility.GetPrefabAssetType(prefab);
            sb.AppendLine($"  Asset Type: {assetType}");

            bool isVariant = assetType == PrefabAssetType.Variant;
            sb.AppendLine($"  Is Variant: {isVariant}");

            if (isVariant)
            {
                // Walk up the inheritance chain
                sb.AppendLine("  Inheritance Chain:");
                var current = prefab;
                int depth = 0;
                while (current != null && depth < 10)
                {
                    string path = AssetDatabase.GetAssetPath(current);
                    string indent = new string(' ', depth * 2);
                    sb.AppendLine($"    {indent}{(depth == 0 ? "-> " : "   ")}{path}");

                    var corr = PrefabUtility.GetCorrespondingObjectFromSource(current);
                    if (corr == current) break;
                    current = corr;
                    depth++;
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all prefab variants of a base prefab in the project. basePrefabPath: the source prefab to find variants for.")]
        public static string ListPrefabVariants(string basePrefabPath)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (basePrefab == null) return $"Error: Base prefab not found at '{basePrefabPath}'.";

            var allPrefabs = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p != basePrefabPath)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine($"Variants of '{basePrefabPath}':");
            int found = 0;

            foreach (var path in allPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant) continue;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                if (source == null) continue;

                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (sourcePath == basePrefabPath)
                {
                    found++;
                    sb.AppendLine($"  [{found}] {path}");
                    if (found >= 50)
                    {
                        sb.AppendLine("  ... (limit reached)");
                        break;
                    }
                }
            }

            if (found == 0)
                return $"No variants found for '{basePrefabPath}'.";

            sb.AppendLine($"\nTotal: {found} variant(s).");
            return sb.ToString().TrimEnd();
        }
    }
}
