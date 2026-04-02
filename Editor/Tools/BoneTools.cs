using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class BoneTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool("List bones (Transform hierarchy) under an avatar root. Use filter for case-insensitive name search. Shows tree with depth indentation. Limited to 100 results.")]
        public static string ListBones(string avatarRootName, string filter = "")
        {
            var root = FindGO(avatarRootName);
            if (root == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            int count = 0;
            bool hasFilter = !string.IsNullOrEmpty(filter);

            if (hasFilter)
            {
                sb.AppendLine($"Bones under '{avatarRootName}' matching '{filter}':");
                CollectFilteredBones(root.transform, filter, sb, ref count);
            }
            else
            {
                sb.AppendLine($"Bone hierarchy of '{avatarRootName}':");
                CollectAllBones(root.transform, 0, sb, ref count);
            }

            if (count == 0)
                return hasFilter
                    ? $"No bones matching '{filter}' found under '{avatarRootName}'."
                    : $"No child transforms found under '{avatarRootName}'.";

            if (count >= 100)
                sb.AppendLine("... (limit of 100 reached, use filter to narrow down)");

            return sb.ToString().TrimEnd();
        }

        private static void CollectAllBones(Transform t, int depth, StringBuilder sb, ref int count)
        {
            if (count >= 100) return;

            string indent = new string(' ', depth * 2);
            string pos = $"({t.localPosition.x:F3}, {t.localPosition.y:F3}, {t.localPosition.z:F3})";
            sb.AppendLine($"{indent}{t.name} pos={pos} children={t.childCount}");
            count++;

            for (int i = 0; i < t.childCount; i++)
            {
                if (count >= 100) return;
                CollectAllBones(t.GetChild(i), depth + 1, sb, ref count);
            }
        }

        private static void CollectFilteredBones(Transform root, string filter, StringBuilder sb, ref int count)
        {
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (count >= 100) return;
                if (t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string path = GetBonePath(t, root);
                    string pos = $"({t.localPosition.x:F3}, {t.localPosition.y:F3}, {t.localPosition.z:F3})";
                    sb.AppendLine($"  {path} pos={pos} children={t.childCount}");
                    count++;
                }
            }
        }

        [AgentTool("Inspect a specific bone in detail. Shows local/world position, rotation, scale, children, and attached components.")]
        public static string InspectBone(string avatarRootName, string boneName)
        {
            var root = FindGO(avatarRootName);
            if (root == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var bone = FindBoneRecursive(root.transform, boneName);
            if (bone == null) return $"Error: Bone '{boneName}' not found under '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Bone: {GetBonePath(bone, root.transform)}");
            sb.AppendLine($"  Local Position: ({bone.localPosition.x:F4}, {bone.localPosition.y:F4}, {bone.localPosition.z:F4})");
            sb.AppendLine($"  Local Rotation: ({bone.localEulerAngles.x:F2}, {bone.localEulerAngles.y:F2}, {bone.localEulerAngles.z:F2})");
            sb.AppendLine($"  Local Scale: ({bone.localScale.x:F4}, {bone.localScale.y:F4}, {bone.localScale.z:F4})");
            sb.AppendLine($"  World Position: ({bone.position.x:F4}, {bone.position.y:F4}, {bone.position.z:F4})");
            sb.AppendLine($"  World Rotation: ({bone.eulerAngles.x:F2}, {bone.eulerAngles.y:F2}, {bone.eulerAngles.z:F2})");

            // Children
            if (bone.childCount > 0)
            {
                sb.AppendLine($"  Children ({bone.childCount}):");
                for (int i = 0; i < bone.childCount; i++)
                    sb.AppendLine($"    - {bone.GetChild(i).name}");
            }

            // Components (excluding Transform)
            var components = bone.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name)
                .ToArray();
            if (components.Length > 0)
                sb.AppendLine($"  Components: [{string.Join(", ", components)}]");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List HumanBodyBones to Transform mapping for a humanoid avatar. Shows which bones are mapped.")]
        public static string ListHumanoidMapping(string avatarRootName)
        {
            var root = FindGO(avatarRootName);
            if (root == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var animator = root.GetComponent<Animator>();
            if (animator == null) return $"Error: No Animator component on '{avatarRootName}'.";
            if (!animator.isHuman) return $"Error: Animator on '{avatarRootName}' is not humanoid.";

            var sb = new StringBuilder();
            sb.AppendLine($"Humanoid bone mapping for '{avatarRootName}':");

            var boneValues = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));
            foreach (var bone in boneValues)
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                    sb.AppendLine($"  {bone} -> {t.name}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Add a constraint to a GameObject. Types: Parent, Aim, Position, Rotation, Scale. sourceName is the constraint source object.")]
        public static string AddConstraint(string goName, string type, string sourceName, float weight = 1f)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var sourceGo = FindGO(sourceName);
            if (sourceGo == null) return $"Error: Source GameObject '{sourceName}' not found.";

            Type constraintType = GetConstraintType(type);
            if (constraintType == null)
                return $"Error: Unknown constraint type '{type}'. Valid types: Parent, Aim, Position, Rotation, Scale.";

            var component = Undo.AddComponent(go, constraintType) as IConstraint;
            if (component == null) return $"Error: Failed to add {type}Constraint to '{goName}'.";

            var source = new ConstraintSource { sourceTransform = sourceGo.transform, weight = weight };
            component.AddSource(source);
            component.constraintActive = true;

            return $"Success: Added {type}Constraint to '{goName}' with source '{sourceName}' (weight={weight}).";
        }

        [AgentTool("Configure an existing constraint source. Use sourceIndex to select which source. Set weight to -999 to keep unchanged. offsetPosition/offsetRotation format: 'x,y,z'.")]
        public static string ConfigureConstraint(string goName, string type, int sourceIndex = 0, float weight = -999f, string offsetPosition = "", string offsetRotation = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            Type constraintType = GetConstraintType(type);
            if (constraintType == null)
                return $"Error: Unknown constraint type '{type}'. Valid types: Parent, Aim, Position, Rotation, Scale.";

            var constraint = go.GetComponent(constraintType) as IConstraint;
            if (constraint == null) return $"Error: No {type}Constraint found on '{goName}'.";

            if (sourceIndex < 0 || sourceIndex >= constraint.sourceCount)
                return $"Error: Source index {sourceIndex} out of range (0-{constraint.sourceCount - 1}).";

            Undo.RecordObject(constraint as Component, $"Configure {type}Constraint");

            var source = constraint.GetSource(sourceIndex);
            if (weight > -999f)
                source.weight = weight;
            constraint.SetSource(sourceIndex, source);

            // Handle offset for position/rotation constraints
            if (!string.IsNullOrEmpty(offsetPosition))
            {
                var pos = ParseVector3(offsetPosition);
                if (pos.HasValue)
                {
                    if (constraint is PositionConstraint pc)
                        pc.translationOffset = pos.Value;
                    else if (constraint is ParentConstraint parc)
                        parc.SetTranslationOffset(sourceIndex, pos.Value);
                }
            }

            if (!string.IsNullOrEmpty(offsetRotation))
            {
                var rot = ParseVector3(offsetRotation);
                if (rot.HasValue)
                {
                    if (constraint is RotationConstraint rc)
                        rc.rotationOffset = rot.Value;
                    else if (constraint is ParentConstraint parc)
                        parc.SetRotationOffset(sourceIndex, rot.Value);
                }
            }

            return $"Success: Configured {type}Constraint on '{goName}' source[{sourceIndex}].";
        }

        [AgentTool("Remove a constraint from a GameObject. Requires user confirmation.")]
        public static string RemoveConstraint(string goName, string type)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            Type constraintType = GetConstraintType(type);
            if (constraintType == null)
                return $"Error: Unknown constraint type '{type}'. Valid types: Parent, Aim, Position, Rotation, Scale.";

            var component = go.GetComponent(constraintType);
            if (component == null) return $"Error: No {type}Constraint found on '{goName}'.";

            if (!AgentSettings.RequestConfirmation(
                "Constraintを削除",
                $"'{goName}' から {type}Constraint を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(component);
            return $"Success: Removed {type}Constraint from '{goName}'.";
        }

        [AgentTool("List all constraints on a GameObject with their sources, weights, and active state.")]
        public static string ListConstraints(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var constraints = go.GetComponents<Component>()
                .Where(c => c is IConstraint)
                .Cast<IConstraint>()
                .ToArray();

            if (constraints.Length == 0) return $"No constraints found on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Constraints on '{goName}': {constraints.Length} found");

            foreach (var c in constraints)
            {
                var comp = c as Component;
                string typeName = comp.GetType().Name;
                sb.AppendLine($"  [{typeName}] Active={c.constraintActive}, Weight={c.weight:F2}");
                for (int i = 0; i < c.sourceCount; i++)
                {
                    var src = c.GetSource(i);
                    string srcName = src.sourceTransform != null ? src.sourceTransform.name : "null";
                    sb.AppendLine($"    Source[{i}]: {srcName} (weight={src.weight:F2})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Activate or deactivate a constraint on a GameObject.")]
        public static string ActivateConstraint(string goName, string type, bool active)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            Type constraintType = GetConstraintType(type);
            if (constraintType == null)
                return $"Error: Unknown constraint type '{type}'. Valid types: Parent, Aim, Position, Rotation, Scale.";

            var constraint = go.GetComponent(constraintType) as IConstraint;
            if (constraint == null) return $"Error: No {type}Constraint found on '{goName}'.";

            Undo.RecordObject(constraint as Component, $"Toggle {type}Constraint");
            constraint.constraintActive = active;

            return $"Success: {type}Constraint on '{goName}' is now {(active ? "active" : "inactive")}.";
        }

        // --- Helpers ---

        private static Type GetConstraintType(string type)
        {
            switch (type.ToLower())
            {
                case "parent": return typeof(ParentConstraint);
                case "aim": return typeof(AimConstraint);
                case "position": return typeof(PositionConstraint);
                case "rotation": return typeof(RotationConstraint);
                case "scale": return typeof(ScaleConstraint);
                default: return null;
            }
        }

        private static Transform FindBoneRecursive(Transform root, string name)
        {
            if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindBoneRecursive(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetBonePath(Transform bone, Transform root)
        {
            var sb = new StringBuilder(bone.name);
            var current = bone.parent;
            while (current != null && current != root)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }

        private static Vector3? ParseVector3(string s)
        {
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }
    }
}
