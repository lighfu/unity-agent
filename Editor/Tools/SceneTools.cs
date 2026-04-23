using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
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
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Create a new empty GameObject. Optionally set a parent.")]
        public static string CreateGameObject(string name, string parentName = "")
        {
            GameObject go = new GameObject(name);

            // 名前の一意性を保証
            var found = GameObject.Find(go.name);
            if (found != null && found != go)
            {
                int suffix = 1;
                string uniqueName;
                do
                {
                    uniqueName = $"{name} ({suffix})";
                    suffix++;
                } while (GameObject.Find(uniqueName) != null);
                go.name = uniqueName;
            }

            Undo.RegisterCreatedObjectUndo(go, "Create GameObject via Agent");

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = FindGO(parentName);
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform, false);
                }
                else
                {
                    return $"Success: Created '{go.name}', but parent '{parentName}' was not found.";
                }
            }
            return $"Success: Created GameObject '{go.name}'.";
        }

        [AgentTool("Delete a GameObject by name. Requires user confirmation when enabled.")]
        public static string DeleteGameObject(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            if (!AgentSettings.RequestConfirmation(
                "GameObject を削除",
                $"'{name}' を削除します。子オブジェクトも全て削除されます。"))
                return "Cancelled: User denied the deletion.";

            Undo.DestroyObjectImmediate(go);
            return $"Success: Deleted GameObject '{name}'.";
        }

        [AgentTool("Find if a GameObject exists and return basic info.")]
        public static string FindGameObject(string name)
        {
            var go = FindGO(name);
            if (go == null) return "Result: Not found.";

            var t = go.transform;
            string info = $"Found '{go.name}'. Local Position: {t.localPosition}, Local Rotation: {t.localEulerAngles}, Scale: {t.localScale}";
            if (t.parent != null) info += $", Parent: {t.parent.name}";
            return info;
        }

        [AgentTool("Set the parent of a child GameObject. worldPositionStays=false (default) places the child at the parent's local origin, true keeps its world position.")]
        public static string SetParent(string childName, string parentName, bool worldPositionStays = false)
        {
            var child = FindGO(childName);
            if (child == null) return $"Error: Child '{childName}' not found.";

            if (string.IsNullOrEmpty(parentName))
            {
                Undo.SetTransformParent(child.transform, null, "Set Parent via Agent");
                return $"Success: Unparented '{childName}'.";
            }

            var parent = FindGO(parentName);
            if (parent == null) return $"Error: Parent '{parentName}' not found.";

            Undo.SetTransformParent(child.transform, parent.transform, worldPositionStays, "Set Parent via Agent");
            if (!worldPositionStays)
            {
                child.transform.localPosition = UnityEngine.Vector3.zero;
                child.transform.localRotation = UnityEngine.Quaternion.identity;
            }
            return $"Success: Set parent of '{childName}' to '{parentName}'.";
        }

        [AgentTool("Set LOCAL position/rotation/scale of a GameObject. Values are in local space (relative to parent). Only pass values to change — omit or use -999 to leave unchanged. Supports named args: SetTransform('Obj', posY=1.5) to change only Y.")]
        public static string SetTransform(string name, float posX = -999, float posY = -999, float posZ = -999, float rotX = -999, float rotY = -999, float rotZ = -999, float scaleX = -999, float scaleY = -999, float scaleZ = -999)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

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

            return $"Success: Updated local Transform for '{name}'.";
        }

        [AgentTool(@"Set WORLD-space position/rotation of a GameObject (avoids manual local-space math when the parent has non-identity transform).
Only pass values to change — omit or use -999 to leave unchanged.
Scale remains local (world scale can't be set directly when parents have non-uniform scale).
Supports named args: SetTransformWorld('Cube', posX=1.5, posZ=-0.2).")]
        public static string SetTransformWorld(string name, float posX = -999, float posY = -999, float posZ = -999, float rotX = -999, float rotY = -999, float rotZ = -999)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

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
            return $"Success: Updated world Transform for '{name}' (world pos=({wp.x:F3}, {wp.y:F3}, {wp.z:F3})).";
        }

        [AgentTool("Duplicate a GameObject.")]
        public static string DuplicateGameObject(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            var newGo = Object.Instantiate(go, go.transform.parent);

            // 名前の一意性を保証
            string baseName = go.name + "_Clone";
            newGo.name = baseName;
            var found = GameObject.Find(newGo.name);
            if (found != null && found != newGo)
            {
                int suffix = 1;
                string uniqueName;
                do
                {
                    uniqueName = $"{baseName} ({suffix})";
                    suffix++;
                } while (GameObject.Find(uniqueName) != null);
                newGo.name = uniqueName;
            }

            Undo.RegisterCreatedObjectUndo(newGo, "Duplicate GameObject via Agent");

            return $"Success: Duplicated '{name}' to '{newGo.name}'.";
        }

        [AgentTool("Make a GameObject look at another target GameObject.")]
        public static string LookAt(string sourceName, string targetName)
        {
            var source = FindGO(sourceName);
            if (source == null) return $"Error: Source GameObject '{sourceName}' not found.";

            var target = FindGO(targetName);
            if (target == null) return $"Error: Target GameObject '{targetName}' not found.";

            Undo.RecordObject(source.transform, "LookAt via Agent");
            source.transform.LookAt(target.transform);
            return $"Success: Made '{sourceName}' look at '{targetName}'.";
        }

        [AgentTool("Get the local and world Transform values (position, rotation, scale) of a GameObject.")]
        public static string GetTransform(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            var t = go.transform;
            var sb = new StringBuilder();
            sb.AppendLine($"Transform: {go.name}");
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
            var go = FindGO(currentName);
            if (go == null) return $"Error: GameObject '{currentName}' not found.";

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
                    return $"Success: Renamed '{currentName}' to '{newName}'. Warning: {sameNameCount} sibling(s) with the same name exist.";
            }

            return $"Success: Renamed '{currentName}' to '{newName}'.";
        }

        [AgentTool("Copy transform (position/rotation/scale) from one GameObject to another. 'what' = position, rotation, scale, or all.")]
        public static string CopyTransform(string sourceName, string targetName, string what = "all", bool local = false)
        {
            var source = FindGO(sourceName);
            if (source == null) return $"Error: Source GameObject '{sourceName}' not found.";

            var target = FindGO(targetName);
            if (target == null) return $"Error: Target GameObject '{targetName}' not found.";

            Undo.RecordObject(target.transform, "Copy Transform via Agent");

            string w = what.ToLowerInvariant();

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
            return $"Success: Copied {w} from '{sourceName}' to '{targetName}' ({space} space).";
        }

        [AgentTool("Set the sibling order of a GameObject in hierarchy. Index 0 = first child, -1 = last.")]
        public static string SetSiblingIndex(string name, int index)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            Undo.RecordObject(go.transform, "Set Sibling Index via Agent");

            if (index == -1)
                go.transform.SetAsLastSibling();
            else if (index == 0)
                go.transform.SetAsFirstSibling();
            else
                go.transform.SetSiblingIndex(index);

            int actual = go.transform.GetSiblingIndex();
            return $"Success: Set sibling index of '{name}' to {actual}.";
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
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            for (int i = 0; i < matches.Count; i++)
            {
                ToolProgress.Report((float)(i + 1) / matches.Count, $"BatchSetActive: {i + 1}/{matches.Count}");
                Undo.RecordObject(matches[i], "BatchSetActive via Agent");
                matches[i].SetActive(isActive);
            }
            ToolProgress.Clear();

            return $"Success: Set active={isActive} on {matches.Count} object(s) matching '{namePattern}' under '{parentName}'.";
        }

        [AgentTool("Rename child GameObjects by replacing a search string with a replacement. Case-insensitive match. Recursive by default.")]
        public static string BatchRename(string parentName, string search, string replace, bool recursive = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

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
                sb.AppendLine($"  '{oldName}' -> '{matches[i].name}'");
            }
            ToolProgress.Clear();

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Delete child GameObjects matching a name pattern. Requires user confirmation. Recursive by default.")]
        public static string BatchDelete(string parentName, string namePattern, bool recursive = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

            var matches = GetMatchingChildren(parent, namePattern, recursive);
            if (matches.Count == 0) return $"No children matching '{namePattern}' found under '{parentName}'.";

            if (!AgentSettings.RequestConfirmation(
                "一括削除",
                $"{matches.Count}個のGameObjectを削除します。\nパターン: '{namePattern}' (親: '{parentName}')"))
                return "Cancelled: User denied the batch deletion.";

            // 逆順で削除（インデックスずれ防止）
            int total = matches.Count;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                ToolProgress.Report((float)(total - i) / total, $"BatchDelete: {total - i}/{total}");
                Undo.DestroyObjectImmediate(matches[i]);
            }
            ToolProgress.Clear();

            return $"Success: Deleted {total} object(s) matching '{namePattern}' under '{parentName}'.";
        }

        [AgentTool("Set tag on child GameObjects matching a name pattern. Recursive by default.")]
        public static string BatchSetTag(string parentName, string namePattern, string tag, bool recursive = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

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

            return $"Success: Set tag='{tag}' on {matches.Count} object(s) matching '{namePattern}' under '{parentName}'.";
        }

        [AgentTool("Set layer on child GameObjects matching a name pattern. Layer can be a name or number. Recursive by default.")]
        public static string BatchSetLayer(string parentName, string namePattern, string layerName, bool recursive = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

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
            return $"Success: Set layer='{resolvedName}' ({layer}) on {matches.Count} object(s) matching '{namePattern}' under '{parentName}'.";
        }

        // ── Hierarchy Structure ─────────────────────────────────────

        [AgentTool("Detach all direct children of a GameObject to its parent level. Preserves world position by default.")]
        public static string UnparentChildren(string parentName, bool worldPositionStays = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

            int childCount = parent.transform.childCount;
            if (childCount == 0) return $"'{parentName}' has no children to unparent.";

            Transform grandparent = parent.transform.parent;

            // 逆順イテレーション（インデックスずれ防止）
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = parent.transform.GetChild(i);
                Undo.SetTransformParent(child, grandparent, worldPositionStays, "UnparentChildren via Agent");
            }

            return $"Success: Unparented {childCount} children from '{parentName}'.";
        }

        [AgentTool("Sort direct children of a GameObject alphabetically. ascending=true for A-Z.")]
        public static string SortChildren(string parentName, bool ascending = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

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
            return $"Success: Sorted {childCount} children of '{parentName}' ({order}).";
        }

        [AgentTool("Move all nested descendants to be direct children of a GameObject. Preserves world position by default.")]
        public static string FlattenHierarchy(string parentName, bool worldPositionStays = true)
        {
            var parent = FindGO(parentName);
            if (parent == null) return $"Error: GameObject '{parentName}' not found.";

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

            return $"Success: Flattened {toMove.Count} descendant(s) to direct children of '{parentName}'.";
        }

        [AgentTool("Move a GameObject one position up in sibling order.")]
        public static string MoveUp(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            int current = go.transform.GetSiblingIndex();
            if (current == 0) return $"'{name}' is already the first sibling.";

            Undo.RecordObject(go.transform, "MoveUp via Agent");
            go.transform.SetSiblingIndex(current - 1);
            return $"Success: Moved '{name}' up from index {current} to {current - 1}.";
        }

        [AgentTool("Move a GameObject one position down in sibling order.")]
        public static string MoveDown(string name)
        {
            var go = FindGO(name);
            if (go == null) return $"Error: GameObject '{name}' not found.";

            int current = go.transform.GetSiblingIndex();
            int lastIndex = go.transform.parent != null ? go.transform.parent.childCount - 1 : SceneManager.GetActiveScene().GetRootGameObjects().Length - 1;
            if (current >= lastIndex) return $"'{name}' is already the last sibling.";

            Undo.RecordObject(go.transform, "MoveDown via Agent");
            go.transform.SetSiblingIndex(current + 1);
            return $"Success: Moved '{name}' down from index {current} to {current + 1}.";
        }
    }
}
