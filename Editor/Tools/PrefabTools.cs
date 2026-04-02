using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class PrefabTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Instantiate a prefab or model asset (.prefab, .fbx, .blend, etc.) into the scene from its asset path. Optionally set a parent by name.")]
        public static string InstantiatePrefab(string assetPath, string parentName = "")
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return $"Error: Prefab not found at '{assetPath}'. Check the path (e.g., 'Assets/Prefabs/MyPrefab.prefab').";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // 名前の一意性を保証
            string baseName = instance.name;
            var found = GameObject.Find(instance.name);
            if (found != null && found != instance)
            {
                int suffix = 1;
                string uniqueName;
                do
                {
                    uniqueName = $"{baseName} ({suffix})";
                    suffix++;
                } while (GameObject.Find(uniqueName) != null);
                instance.name = uniqueName;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab via Agent");

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = FindGO(parentName);
                if (parent != null)
                {
                    instance.transform.SetParent(parent.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                }
                else
                {
                    // Parent not found — destroy the instance to avoid orphaned objects at scene root
                    Undo.DestroyObjectImmediate(instance);
                    return $"Error: Parent '{parentName}' was not found. Prefab was not instantiated. Use GetHierarchyTree or ListChildren to find the correct parent path.";
                }
            }

            return $"Success: Instantiated prefab '{instance.name}'.";
        }

        [AgentTool("Save a GameObject as a prefab.")]
        public static string SaveAsPrefab(string gameObjectName, string savePath)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

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

            PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.UserAction);
            return $"Success: Saved '{gameObjectName}' as prefab at '{savePath}'.";
        }

        [AgentTool("Unpack a prefab instance in the scene. Mode: 'completely' (flatten all nested) or 'root' (one level only).")]
        public static string UnpackPrefab(string gameObjectName, string mode = "completely")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            Undo.RegisterFullObjectHierarchyUndo(outermost, "Unpack Prefab via Agent");

            if (mode.ToLower() == "completely")
            {
                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                return $"Success: Completely unpacked prefab '{outermost.name}'.";
            }
            else
            {
                PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
                return $"Success: Unpacked one level of prefab '{outermost.name}'.";
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
                int shown = 0;
                foreach (var ov in overrides)
                {
                    if (shown >= 20) { sb.AppendLine("    ... (truncated)"); break; }
                    sb.AppendLine($"    - {ov.instanceObject.GetType().Name} on '{(ov.instanceObject as Component)?.gameObject.name ?? ov.instanceObject.name}'");
                    shown++;
                }
            }

            return sb.ToString();
        }

        [AgentTool("Apply all prefab overrides on a GameObject back to the source prefab asset.")]
        public static string ApplyPrefabOverrides(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outermost);

            PrefabUtility.ApplyPrefabInstance(outermost, InteractionMode.UserAction);
            return $"Success: Applied all overrides from '{outermost.name}' to prefab asset '{assetPath}'.";
        }

        [AgentTool("Revert all prefab overrides on a GameObject, restoring it to match the source prefab asset.")]
        public static string RevertPrefabOverrides(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: '{gameObjectName}' is not a prefab instance.";

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            Undo.RegisterFullObjectHierarchyUndo(outermost, "Revert Prefab via Agent");

            PrefabUtility.RevertPrefabInstance(outermost, InteractionMode.UserAction);
            return $"Success: Reverted all overrides on '{outermost.name}' to match the source prefab.";
        }

        [AgentTool("Create a Prefab Variant from an existing prefab asset. The variant inherits from the base and can have overrides. basePrefabPath: e.g. 'Assets/Prefabs/Base.prefab'. variantPath: output path.")]
        public static string CreatePrefabVariant(string basePrefabPath, string variantPath)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
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

            // Instantiate, save as variant, then destroy
            var instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            var variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            Object.DestroyImmediate(instance);

            return variant != null
                ? $"Success: Created prefab variant at '{variantPath}' based on '{basePrefabPath}'."
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
