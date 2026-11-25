using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class SceneTools
    {
        internal static GameObject FindReadableTarget(string name)
        {
            if (name.StartsWith("/", System.StringComparison.Ordinal) || name.Contains("::/") ||
                name.StartsWith("instanceId:", System.StringComparison.OrdinalIgnoreCase))
                return TryResolveTarget(name, out var target, out _) ? target : null;
            return MeshAnalysisTools.FindGameObject(name);
        }

        // Mutation targets never fall back from a path to a matching leaf elsewhere. A unique
        // bare name or partial path remains supported, including inactive objects.
        internal static bool TryResolveTarget(string target, out GameObject found, out string error) =>
            TryResolveTarget(target, out found, out error, out _);

        internal static bool TryResolveTarget(string target, out GameObject found, out string error, out bool ambiguous)
        {
            found = null;
            error = null;
            ambiguous = false;
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "Error: A GameObject name or hierarchy path is required.";
                return false;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            if (target.StartsWith("instanceId:", System.StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.Substring("instanceId:".Length), out int id))
            {
                found = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (found != null && found.scene.IsValid() && found.scene.isLoaded && !EditorUtility.IsPersistent(found) &&
                    (stage == null || found.scene == stage.scene)) return true;
                found = null;
                error = $"Error: '{target}' does not identify a loaded scene GameObject.";
                return false;
            }

            int qualifier = target.IndexOf("::/", System.StringComparison.Ordinal);
            string scene = qualifier >= 0 ? target.Substring(0, qualifier) : null;
            string path = qualifier >= 0 ? target.Substring(qualifier + 2) : target;
            int separator = path.LastIndexOf('/');
            string leaf = path.Substring(separator + 1);
            var exact = new List<GameObject>();
            var folded = new List<GameObject>();
            var nearby = new List<GameObject>();
            int sceneCount = stage == null ? SceneManager.sceneCount : 1;
            for (int i = 0; i < sceneCount; i++)
            {
                var loaded = stage == null ? SceneManager.GetSceneAt(i) : stage.scene;
                if (!loaded.isLoaded) continue;
                foreach (var root in loaded.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(transform.name, leaf, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var candidate = transform.gameObject;
                    nearby.Add(candidate);
                    if (scene != null && !string.Equals(scene, stage?.assetPath ?? SceneLabel(loaded), System.StringComparison.OrdinalIgnoreCase)) continue;
                    string candidatePath = separator < 0 ? transform.name : "/" + HierarchyTools.GetFullPath(transform);
                    if (MatchesPath(candidatePath, path, System.StringComparison.Ordinal)) exact.Add(candidate);
                    else if (MatchesPath(candidatePath, path, System.StringComparison.OrdinalIgnoreCase)) folded.Add(candidate);
                }
            }

            var matches = exact.Count > 0 ? exact : folded;
            if (matches.Count == 1)
            {
                found = matches[0];
                return true;
            }

            ambiguous = matches.Count > 1;
            error = ambiguous
                ? $"Error: GameObject target '{target}' is ambiguous ({matches.Count} matches)."
                : $"Error: GameObject target '{target}' was not found.";
            var choices = matches.Count > 0 ? matches : nearby;
            if (choices.Count > 0)
            {
                error += " Candidate hierarchy paths: " + string.Join(", ", choices.Take(12).Select(DescribeTarget)) + ".";
                if (choices.Count > 12) error += $" Showing 12 of {choices.Count}.";
                error += " Use the full path or the instanceId when paths are identical.";
            }
            else error += " Use FindObjectsByName or ListRootObjects to locate the target.";
            return false;
        }

        static bool MatchesPath(string candidate, string target, System.StringComparison comparison) =>
            target.IndexOf('/') < 0 || target.StartsWith("/", System.StringComparison.Ordinal)
                ? string.Equals(candidate, target, comparison)
                : candidate.EndsWith("/" + target, comparison);

        static string SceneLabel(Scene scene) => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;

        internal static string DescribeTarget(GameObject go)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            string scene = stage != null && stage.scene == go.scene ? stage.assetPath : SceneLabel(go.scene);
            return $"'{scene}::/{HierarchyTools.GetFullPath(go.transform)}' (instanceId:{go.GetInstanceID()})";
        }

        internal static bool TryResolveCreationParent(string name, out GameObject parent, out string error)
        {
            if (!string.IsNullOrEmpty(name)) return TryResolveTarget(name, out parent, out error);
            parent = PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot;
            error = null;
            return true;
        }

        internal static string UniqueSiblingName(GameObject go, string baseName)
        {
            var used = new HashSet<string>();
            var parent = go.transform.parent;
            if (parent == null)
            {
                foreach (var root in go.scene.GetRootGameObjects())
                    if (root != go) used.Add(root.name);
            }
            else
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i).gameObject;
                    if (sibling != go) used.Add(sibling.name);
                }
            }

            string name = baseName;
            for (int suffix = 1; used.Contains(name); suffix++) name = $"{baseName} ({suffix})";
            return name;
        }

        [AgentTool("Create a new empty GameObject. Optionally set a parent.")]
        public static string CreateGameObject(string name, string parentName = "")
        {
            if (string.IsNullOrWhiteSpace(name)) return "Error: GameObject name is required.";
            if (!TryResolveCreationParent(parentName, out var parent, out string error)) return error;

            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.name = UniqueSiblingName(go, name);
            Undo.RegisterCreatedObjectUndo(go, "Create GameObject via Agent");
            return $"Success: Created GameObject {DescribeTarget(go)}.";
        }

        [AgentTool("Delete a GameObject by name. Requires user confirmation when enabled.")]
        public static string DeleteGameObject(string name)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;
            string target = DescribeTarget(go);

            if (!AgentSettings.RequestConfirmation(
                "GameObject を削除",
                $"{target} を削除します。子オブジェクトも全て削除されます。"))
                return "Cancelled: User denied the deletion.";

            Undo.DestroyObjectImmediate(go);
            return $"Success: Deleted GameObject {target}.";
        }

        [AgentTool("Find if a GameObject exists and return basic info.")]
        public static string FindGameObject(string name)
        {
            var go = FindReadableTarget(name);
            if (go == null) return "Result: Not found.";

            var t = go.transform;
            string info = $"Found '{go.name}'. Local Position: {t.localPosition}, Local Rotation: {t.localEulerAngles}, Scale: {t.localScale}";
            if (t.parent != null) info += $", Parent: {t.parent.name}";
            return info;
        }

        [AgentTool("Set the parent of a child GameObject. worldPositionStays=false (default) SNAPS the child to the parent's local origin AND resets localRotation to identity — useful for fresh attachment. worldPositionStays=true preserves the child's current world position AND rotation (Unity standard behavior) — use this when re-parenting a posed bone or accessory that already sits in the right place.")]
        public static string SetParent(string childName, string parentName, bool worldPositionStays = false)
        {
            if (!TryResolveTarget(childName, out var child, out string error)) return error;

            if (string.IsNullOrEmpty(parentName))
            {
                Undo.SetTransformParent(child.transform, null, "Set Parent via Agent");
                return $"Success: Unparented {DescribeTarget(child)}.";
            }

            if (!TryResolveTarget(parentName, out var parent, out error)) return error;
            if (parent.transform.IsChildOf(child.transform))
                return $"Error: {DescribeTarget(parent)} cannot be the parent of {DescribeTarget(child)}.";

            Undo.SetTransformParent(child.transform, parent.transform, worldPositionStays, "Set Parent via Agent");
            if (!worldPositionStays)
            {
                child.transform.localPosition = UnityEngine.Vector3.zero;
                child.transform.localRotation = UnityEngine.Quaternion.identity;
            }
            return $"Success: Set parent of {DescribeTarget(child)} to {DescribeTarget(parent)}.";
        }

        [AgentTool("Set LOCAL position/rotation/scale of a GameObject. Values are in local space (relative to parent). Only pass values to change — omit or use -999 to leave unchanged. Supports named args: SetTransform('Obj', posY=1.5) to change only Y.")]
        public static string SetTransform(string name, float posX = -999, float posY = -999, float posZ = -999, float rotX = -999, float rotY = -999, float rotZ = -999, float scaleX = -999, float scaleY = -999, float scaleZ = -999)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            Undo.RecordObject(go.transform, "Set Transform via Agent");

            if (posX != -999 || posY != -999 || posZ != -999)
            {
                var pos = go.transform.localPosition;
                if (posX != -999) pos.x = posX;
                if (posY != -999) pos.y = posY;
                if (posZ != -999) pos.z = posZ;
                go.transform.localPosition = pos;
            }

            if (rotX != -999 || rotY != -999 || rotZ != -999)
            {
                var rot = go.transform.localEulerAngles;
                if (rotX != -999) rot.x = rotX;
                if (rotY != -999) rot.y = rotY;
                if (rotZ != -999) rot.z = rotZ;
                go.transform.localEulerAngles = rot;
            }

            if (scaleX != -999 || scaleY != -999 || scaleZ != -999)
            {
                var scale = go.transform.localScale;
                if (scaleX != -999) scale.x = scaleX;
                if (scaleY != -999) scale.y = scaleY;
                if (scaleZ != -999) scale.z = scaleZ;
                go.transform.localScale = scale;
            }

            return $"Success: Updated local Transform for {DescribeTarget(go)}.";
        }

        [AgentTool(@"Set WORLD-space position/rotation of a GameObject (avoids manual local-space math when the parent has non-identity transform).
Only pass values to change — omit or use -999 to leave unchanged.
Scale remains local (world scale can't be set directly when parents have non-uniform scale).
Supports named args: SetTransformWorld('Cube', posX=1.5, posZ=-0.2).")]
        public static string SetTransformWorld(string name, float posX = -999, float posY = -999, float posZ = -999, float rotX = -999, float rotY = -999, float rotZ = -999)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            Undo.RecordObject(go.transform, "Set Transform World via Agent");

            if (posX != -999 || posY != -999 || posZ != -999)
            {
                var pos = go.transform.position;
                if (posX != -999) pos.x = posX;
                if (posY != -999) pos.y = posY;
                if (posZ != -999) pos.z = posZ;
                go.transform.position = pos;
            }

            if (rotX != -999 || rotY != -999 || rotZ != -999)
            {
                var rot = go.transform.eulerAngles;
                if (rotX != -999) rot.x = rotX;
                if (rotY != -999) rot.y = rotY;
                if (rotZ != -999) rot.z = rotZ;
                go.transform.eulerAngles = rot;
            }

            var wp = go.transform.position;
            return $"Success: Updated world Transform for {DescribeTarget(go)} (world pos=({wp.x:F3}, {wp.y:F3}, {wp.z:F3})).";
        }

        [AgentTool("Duplicate a GameObject.")]
        public static string DuplicateGameObject(string name)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            var newGo = Object.Instantiate(go, go.transform.parent);
            newGo.name = UniqueSiblingName(newGo, go.name + "_Clone");

            Undo.RegisterCreatedObjectUndo(newGo, "Duplicate GameObject via Agent");

            return $"Success: Duplicated {DescribeTarget(go)} to {DescribeTarget(newGo)}.";
        }

        [AgentTool("Make a GameObject look at another target GameObject.")]
        public static string LookAt(string sourceName, string targetName)
        {
            if (!TryResolveTarget(sourceName, out var source, out string error)) return error;
            if (!TryResolveTarget(targetName, out var target, out error)) return error;

            Undo.RecordObject(source.transform, "LookAt via Agent");
            source.transform.LookAt(target.transform);
            return $"Success: Made {DescribeTarget(source)} look at {DescribeTarget(target)}.";
        }

        [AgentTool("Get the local and world Transform values (position, rotation, scale) of a GameObject.")]
        public static string GetTransform(string name)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            var t = go.transform;
            var sb = new StringBuilder();
            sb.AppendLine($"Transform: {go.name}");
            sb.AppendLine($"  Target: {DescribeTarget(go)}");
            sb.AppendLine($"  Local Position: ({t.localPosition.x:F4}, {t.localPosition.y:F4}, {t.localPosition.z:F4})");
            sb.AppendLine($"  Local Rotation: ({t.localEulerAngles.x:F2}, {t.localEulerAngles.y:F2}, {t.localEulerAngles.z:F2})");
            sb.AppendLine($"  Local Scale: ({t.localScale.x:F4}, {t.localScale.y:F4}, {t.localScale.z:F4})");
            sb.AppendLine($"  World Position: ({t.position.x:F4}, {t.position.y:F4}, {t.position.z:F4})");
            sb.AppendLine($"  World Rotation: ({t.eulerAngles.x:F2}, {t.eulerAngles.y:F2}, {t.eulerAngles.z:F2})");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Rename a GameObject.")]
        public static string RenameGameObject(string currentName, string newName)
        {
            if (!TryResolveTarget(currentName, out var go, out string error)) return error;
            if (string.IsNullOrWhiteSpace(newName)) return "Error: The new GameObject name is required.";
            string oldTarget = DescribeTarget(go);

            Undo.RecordObject(go, "Rename GameObject via Agent");
            go.name = newName;

            // 兄弟に同名がある場合は Warning 付き
            if (go.transform.parent != null)
            {
                int sameNameCount = 0;
                var parent = go.transform.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (parent.GetChild(i) != go.transform && parent.GetChild(i).name == newName)
                        sameNameCount++;
                }
                if (sameNameCount > 0)
                    return $"Success: Renamed {oldTarget} to {DescribeTarget(go)}. Warning: {sameNameCount} sibling(s) with the same name exist.";
            }

            return $"Success: Renamed {oldTarget} to {DescribeTarget(go)}.";
        }

        [AgentTool("Copy transform (position/rotation/scale) from one GameObject to another. 'what' = position, rotation, scale, or all.")]
        public static string CopyTransform(string sourceName, string targetName, string what = "all", bool local = false)
        {
            if (!TryResolveTarget(sourceName, out var source, out string error)) return error;
            if (!TryResolveTarget(targetName, out var target, out error)) return error;
            string w = what.ToLowerInvariant();
            if (w != "position" && w != "rotation" && w != "scale" && w != "all")
                return "Error: 'what' must be position, rotation, scale, or all.";

            Undo.RecordObject(target.transform, "Copy Transform via Agent");

            if (w == "position" || w == "all")
            {
                if (local)
                    target.transform.localPosition = source.transform.localPosition;
                else
                    target.transform.position = source.transform.position;
            }

            if (w == "rotation" || w == "all")
            {
                if (local)
                    target.transform.localRotation = source.transform.localRotation;
                else
                    target.transform.rotation = source.transform.rotation;
            }

            if (w == "scale" || w == "all")
            {
                target.transform.localScale = source.transform.localScale;
            }

            string space = local ? "local" : "world";
            return $"Success: Copied {w} from {DescribeTarget(source)} to {DescribeTarget(target)} ({space} space).";
        }

        [AgentTool("Set the sibling order of a GameObject in hierarchy. Index 0 = first child, -1 = last.")]
        public static string SetSiblingIndex(string name, int index)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            Undo.RecordObject(go.transform, "Set Sibling Index via Agent");

            if (index == -1)
                go.transform.SetAsLastSibling();
            else if (index == 0)
                go.transform.SetAsFirstSibling();
            else
                go.transform.SetSiblingIndex(index);

            int actual = go.transform.GetSiblingIndex();
            return $"Success: Set sibling index of {DescribeTarget(go)} to {actual}.";
        }

        // ── Helper ──────────────────────────────────────────────────

        private static List<GameObject> GetMatchingChildren(GameObject parent, string namePattern, bool recursive)
        {
            var results = new List<GameObject>();
            if (recursive)
            {
                var transforms = parent.GetComponentsInChildren<Transform>(true);
                foreach (var t in transforms)
                {
                    if (t.gameObject == parent) continue;
                    if (t.name.IndexOf(namePattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        results.Add(t.gameObject);
                    if (results.Count >= 50) break;
                }
            }
            else
            {
                for (int i = 0; i < parent.transform.childCount; i++)
                {
                    var child = parent.transform.GetChild(i).gameObject;
                    if (child.name.IndexOf(namePattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        results.Add(child);
                    if (results.Count >= 50) break;
                }
            }
            return results;
        }

        // ── Batch Operations ────────────────────────────────────────

        [AgentTool("Set active state for child GameObjects matching a name pattern. Case-insensitive partial match. Recursive by default.")]
        public static string BatchSetActive(string parentName, string namePattern, bool isActive, bool recursive = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            for (int i = 0; i < matches.Count; i++)
            {
                ToolProgress.Report((float)(i + 1) / matches.Count, $"BatchSetActive: {i + 1}/{matches.Count}");
                Undo.RecordObject(matches[i], "BatchSetActive via Agent");
                matches[i].SetActive(isActive);
            }
            ToolProgress.Clear();

            return $"Success: Set active={isActive} on {matches.Count} object(s) matching '{namePattern}' under {DescribeTarget(parent)}.";
        }

        [AgentTool("Rename child GameObjects by replacing a search string with a replacement. Case-insensitive match. Recursive by default.")]
        public static string BatchRename(string parentName, string search, string replace, bool recursive = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            var matches = GetMatchingChildren(parent, search, recursive);
            if (matches.Count == 0) return $"No children matching '{search}' found under '{parentName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Renamed {matches.Count} object(s):");
            string pattern = Regex.Escape(search);
            for (int i = 0; i < matches.Count; i++)
            {
                ToolProgress.Report((float)(i + 1) / matches.Count, $"BatchRename: {i + 1}/{matches.Count}");
                Undo.RecordObject(matches[i], "BatchRename via Agent");
                string oldName = matches[i].name;
                matches[i].name = Regex.Replace(matches[i].name, pattern, replace, RegexOptions.IgnoreCase);
                sb.AppendLine($"  '{oldName}' -> {DescribeTarget(matches[i])}");
            }
            ToolProgress.Clear();

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Delete child GameObjects matching a name pattern. Requires user confirmation. Recursive by default.")]
        public static string BatchDelete(string parentName, string namePattern, bool recursive = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;
            string target = DescribeTarget(parent);

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            if (!AgentSettings.RequestConfirmation(
                "一括削除",
                $"{matches.Count}個のGameObjectを削除します。\nパターン: '{namePattern}' (親: {target})"))
                return "Cancelled: User denied the batch deletion.";

            // 逆順で削除（インデックスずれ防止）
            int total = matches.Count;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                ToolProgress.Report((float)(total - i) / total, $"BatchDelete: {total - i}/{total}");
                Undo.DestroyObjectImmediate(matches[i]);
            }
            ToolProgress.Clear();

            return $"Success: Deleted {total} object(s) matching '{namePattern}' under {target}.";
        }

        [AgentTool("Set tag on child GameObjects matching a name pattern. Recursive by default.")]
        public static string BatchSetTag(string parentName, string namePattern, string tag, bool recursive = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            // タグの有効性を事前検証
            try { parent.CompareTag(tag); }
            catch (UnityException) { return $"Error: Tag '{tag}' is not defined. Use the Tag Manager to add it first."; }

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            foreach (var go in matches)
            {
                Undo.RecordObject(go, "BatchSetTag via Agent");
                go.tag = tag;
            }

            return $"Success: Set tag='{tag}' on {matches.Count} object(s) matching '{namePattern}' under {DescribeTarget(parent)}.";
        }

        [AgentTool("Set layer on child GameObjects matching a name pattern. Layer can be a name or number. Recursive by default.")]
        public static string BatchSetLayer(string parentName, string namePattern, string layerName, bool recursive = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            int layer;
            if (int.TryParse(layerName, out layer))
            {
                if (layer < 0 || layer > 31) return $"Error: Layer number must be 0-31, got {layer}.";
            }
            else
            {
                layer = LayerMask.NameToLayer(layerName);
                if (layer == -1) return $"Error: Layer '{layerName}' not found.";
            }

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            foreach (var go in matches)
            {
                Undo.RecordObject(go, "BatchSetLayer via Agent");
                go.layer = layer;
            }

            string resolvedName = LayerMask.LayerToName(layer);
            return $"Success: Set layer='{resolvedName}' ({layer}) on {matches.Count} object(s) matching '{namePattern}' under {DescribeTarget(parent)}.";
        }

        // ── Hierarchy Structure ─────────────────────────────────────

        [AgentTool("Detach all direct children of a GameObject to its parent level. Preserves world position by default.")]
        public static string UnparentChildren(string parentName, bool worldPositionStays = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            int childCount = parent.transform.childCount;
            if (childCount == 0) return $"'{parentName}' has no children to unparent.";

            Transform grandparent = parent.transform.parent;

            // 逆順イテレーション（インデックスずれ防止）
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = parent.transform.GetChild(i);
                Undo.SetTransformParent(child, grandparent, worldPositionStays, "UnparentChildren via Agent");
            }

            return $"Success: Unparented {childCount} children from {DescribeTarget(parent)}.";
        }

        [AgentTool("Sort direct children of a GameObject alphabetically. ascending=true for A-Z.")]
        public static string SortChildren(string parentName, bool ascending = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            int childCount = parent.transform.childCount;
            if (childCount <= 1) return $"'{parentName}' has {childCount} child(ren), no sorting needed.";

            Undo.RegisterFullObjectHierarchyUndo(parent, "SortChildren via Agent");

            var children = new List<Transform>();
            for (int i = 0; i < childCount; i++)
                children.Add(parent.transform.GetChild(i));

            if (ascending)
                children.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            else
                children.Sort((a, b) => string.Compare(b.name, a.name, System.StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < children.Count; i++)
                children[i].SetSiblingIndex(i);

            string order = ascending ? "A-Z" : "Z-A";
            return $"Success: Sorted {childCount} children of {DescribeTarget(parent)} ({order}).";
        }

        [AgentTool("Move grandchildren and deeper descendants to be direct children of a GameObject. Direct children are left untouched. Preserves world position by default.")]
        public static string FlattenHierarchy(string parentName, bool worldPositionStays = true)
        {
            if (!TryResolveTarget(parentName, out var parent, out string error)) return error;

            var allDescendants = parent.GetComponentsInChildren<Transform>(true);
            // 自身と直接の子は除外 — 孫以下のみ対象
            var toMove = new List<Transform>();
            foreach (var t in allDescendants)
            {
                if (t == parent.transform) continue;
                if (t.parent == parent.transform) continue;
                toMove.Add(t);
            }

            if (toMove.Count == 0) return $"'{parentName}' has no nested descendants to flatten.";

            if (!AgentSettings.RequestConfirmation(
                "階層フラット化",
                $"{toMove.Count}個の孫以下のオブジェクトを'{parentName}'の直接の子に移動します。"))
                return "Cancelled: User denied the flatten operation.";

            foreach (var t in toMove)
            {
                Undo.SetTransformParent(t, parent.transform, worldPositionStays, "FlattenHierarchy via Agent");
            }

            return $"Success: Flattened {toMove.Count} descendant(s) to direct children of {DescribeTarget(parent)}.";
        }

        [AgentTool("Move a GameObject one position up in sibling order.")]
        public static string MoveUp(string name)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            int current = go.transform.GetSiblingIndex();
            if (current == 0) return $"'{name}' is already the first sibling.";

            Undo.RecordObject(go.transform, "MoveUp via Agent");
            go.transform.SetSiblingIndex(current - 1);
            return $"Success: Moved {DescribeTarget(go)} up from index {current} to {current - 1}.";
        }

        [AgentTool("Move a GameObject one position down in sibling order.")]
        public static string MoveDown(string name)
        {
            if (!TryResolveTarget(name, out var go, out string error)) return error;

            int current = go.transform.GetSiblingIndex();
            int lastIndex = go.transform.parent != null ? go.transform.parent.childCount - 1 : go.scene.rootCount - 1;
            if (current >= lastIndex) return $"'{name}' is already the last sibling.";

            Undo.RecordObject(go.transform, "MoveDown via Agent");
            go.transform.SetSiblingIndex(current + 1);
            return $"Success: Moved {DescribeTarget(go)} down from index {current} to {current + 1}.";
        }
    }
}
