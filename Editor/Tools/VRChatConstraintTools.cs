using UnityEngine;
using UnityEditor;
using System;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class VRChatConstraintTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // VRC Constraint types (VRChat SDK 3.7+)
        private const string VrcPositionConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCPositionConstraint";
        private const string VrcRotationConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint";
        private const string VrcScaleConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCScaleConstraint";
        private const string VrcParentConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint";
        private const string VrcAimConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCAimConstraint";
        private const string VrcLookAtConstraintType = "VRC.SDK3.Dynamics.Constraint.Components.VRCLookAtConstraint";
        private const string VrcHeadChopType = "VRC.SDK3.Avatars.Components.VRCHeadChop";

        private static Type ResolveConstraintType(string type)
        {
            switch (type.ToLower())
            {
                case "position": return VRChatTools.FindVrcType(VrcPositionConstraintType);
                case "rotation": return VRChatTools.FindVrcType(VrcRotationConstraintType);
                case "scale": return VRChatTools.FindVrcType(VrcScaleConstraintType);
                case "parent": return VRChatTools.FindVrcType(VrcParentConstraintType);
                case "aim": return VRChatTools.FindVrcType(VrcAimConstraintType);
                case "lookat": return VRChatTools.FindVrcType(VrcLookAtConstraintType);
                default: return null;
            }
        }

        [AgentTool("Add a VRC Constraint to a GameObject. type: Position, Rotation, Scale, Parent, Aim, LookAt. Optionally set a source transform. VRC Constraints are preferred over Unity constraints for VRChat.")]
        public static string AddVRCConstraint(string gameObjectName, string type, string sourceName = "", float weight = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var constraintType = ResolveConstraintType(type);
            if (constraintType == null)
                return $"Error: VRC {type}Constraint type not found. Ensure VRChat SDK 3.7+ is installed. Valid types: Position, Rotation, Scale, Parent, Aim, LookAt.";

            var comp = Undo.AddComponent(go, constraintType);
            var so = new SerializedObject(comp);

            // Set active and weight
            var isActive = so.FindProperty("IsActive") ?? so.FindProperty("m_Active") ?? so.FindProperty("isActive");
            if (isActive != null) isActive.boolValue = true;

            var weightProp = so.FindProperty("Weight") ?? so.FindProperty("m_Weight") ?? so.FindProperty("weight");
            if (weightProp != null) weightProp.floatValue = weight;

            // Add source if specified
            if (!string.IsNullOrEmpty(sourceName))
            {
                var sourceGo = FindGO(sourceName);
                if (sourceGo == null)
                {
                    so.ApplyModifiedProperties();
                    return $"Warning: Added VRC{type}Constraint but source '{sourceName}' not found. Add source manually.";
                }

                var sources = so.FindProperty("Sources") ?? so.FindProperty("m_Sources") ?? so.FindProperty("sources");
                if (sources != null && sources.isArray)
                {
                    sources.arraySize = 1;
                    var source = sources.GetArrayElementAtIndex(0);

                    var srcTransform = source.FindPropertyRelative("SourceTransform")
                        ?? source.FindPropertyRelative("sourceTransform");
                    if (srcTransform != null) srcTransform.objectReferenceValue = sourceGo.transform;

                    var srcWeight = source.FindPropertyRelative("Weight")
                        ?? source.FindPropertyRelative("weight");
                    if (srcWeight != null) srcWeight.floatValue = weight;
                }
            }

            so.ApplyModifiedProperties();
            return $"Success: Added VRC{type}Constraint to '{gameObjectName}'" +
                   (!string.IsNullOrEmpty(sourceName) ? $" with source '{sourceName}'." : ".");
        }

        [AgentTool("Add a source to an existing VRC Constraint. Returns the source index.")]
        public static string AddVRCConstraintSource(string gameObjectName, string type, string sourceName, float weight = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var constraintType = ResolveConstraintType(type);
            if (constraintType == null) return $"Error: VRC {type}Constraint type not found.";

            var comp = go.GetComponent(constraintType);
            if (comp == null) return $"Error: No VRC{type}Constraint on '{gameObjectName}'.";

            var sourceGo = FindGO(sourceName);
            if (sourceGo == null) return $"Error: Source GameObject '{sourceName}' not found.";

            var so = new SerializedObject(comp);
            var sources = so.FindProperty("Sources") ?? so.FindProperty("m_Sources") ?? so.FindProperty("sources");
            if (sources == null || !sources.isArray) return "Error: Cannot access Sources array on this constraint.";

            Undo.RecordObject(comp, "Add VRC Constraint Source");

            int newIndex = sources.arraySize;
            sources.arraySize = newIndex + 1;
            var source = sources.GetArrayElementAtIndex(newIndex);

            var srcTransform = source.FindPropertyRelative("SourceTransform")
                ?? source.FindPropertyRelative("sourceTransform");
            if (srcTransform != null) srcTransform.objectReferenceValue = sourceGo.transform;

            var srcWeight = source.FindPropertyRelative("Weight")
                ?? source.FindPropertyRelative("weight");
            if (srcWeight != null) srcWeight.floatValue = weight;

            so.ApplyModifiedProperties();
            return $"Success: Added source '{sourceName}' at index [{newIndex}] to VRC{type}Constraint on '{gameObjectName}'.";
        }

        [AgentTool("Configure a VRC Constraint. Set weight, active state, freeze axes. freezeAxes: comma-separated 'X,Y,Z' to affect. Use -999 for unchanged floats.")]
        public static string ConfigureVRCConstraint(string gameObjectName, string type,
            float weight = -999f, int active = -1, string freezeAxes = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var constraintType = ResolveConstraintType(type);
            if (constraintType == null) return $"Error: VRC {type}Constraint type not found.";

            var comp = go.GetComponent(constraintType);
            if (comp == null) return $"Error: No VRC{type}Constraint on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            Undo.RecordObject(comp, "Configure VRC Constraint");

            if (weight > -999f)
            {
                var weightProp = so.FindProperty("Weight") ?? so.FindProperty("m_Weight") ?? so.FindProperty("weight");
                if (weightProp != null) weightProp.floatValue = weight;
            }

            if (active >= 0)
            {
                var isActive = so.FindProperty("IsActive") ?? so.FindProperty("m_Active") ?? so.FindProperty("isActive");
                if (isActive != null) isActive.boolValue = active != 0;

                var locked = so.FindProperty("Locked") ?? so.FindProperty("m_Locked") ?? so.FindProperty("locked");
                if (locked != null) locked.boolValue = active != 0;
            }

            if (!string.IsNullOrEmpty(freezeAxes))
            {
                string upper = freezeAxes.ToUpper();
                bool x = upper.Contains("X");
                bool y = upper.Contains("Y");
                bool z = upper.Contains("Z");

                // Position constraint axes
                SetBoolProp(so, "AffectsPositionX", x);
                SetBoolProp(so, "AffectsPositionY", y);
                SetBoolProp(so, "AffectsPositionZ", z);
                // Rotation constraint axes
                SetBoolProp(so, "AffectsRotationX", x);
                SetBoolProp(so, "AffectsRotationY", y);
                SetBoolProp(so, "AffectsRotationZ", z);
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured VRC{type}Constraint on '{gameObjectName}'.";
        }

        [AgentTool("Inspect a VRC Constraint in detail. Shows all properties, sources, axes, and weights.")]
        public static string InspectVRCConstraint(string gameObjectName, string type)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var constraintType = ResolveConstraintType(type);
            if (constraintType == null) return $"Error: VRC {type}Constraint type not found.";

            var comp = go.GetComponent(constraintType);
            if (comp == null) return $"Error: No VRC{type}Constraint on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var sb = new StringBuilder();
            sb.AppendLine($"VRC{type}Constraint on '{gameObjectName}':");

            // Active & Weight
            AppendBool(sb, so, "IsActive", "m_Active", "isActive");
            AppendFloat(sb, so, "Weight", "m_Weight", "weight");
            AppendBool(sb, so, "Locked", "m_Locked", "locked");

            // Freeze axes
            AppendBool(sb, so, "AffectsPositionX");
            AppendBool(sb, so, "AffectsPositionY");
            AppendBool(sb, so, "AffectsPositionZ");
            AppendBool(sb, so, "AffectsRotationX");
            AppendBool(sb, so, "AffectsRotationY");
            AppendBool(sb, so, "AffectsRotationZ");
            AppendBool(sb, so, "FreezeToWorld");

            // Aim specific
            AppendVector3(sb, so, "AimVector");
            AppendVector3(sb, so, "UpVector");
            AppendVector3(sb, so, "WorldUpVector");

            // Sources
            var sources = so.FindProperty("Sources") ?? so.FindProperty("m_Sources") ?? so.FindProperty("sources");
            if (sources != null && sources.isArray)
            {
                sb.AppendLine($"  Sources ({sources.arraySize}):");
                for (int i = 0; i < sources.arraySize; i++)
                {
                    var source = sources.GetArrayElementAtIndex(i);
                    var srcTransform = source.FindPropertyRelative("SourceTransform")
                        ?? source.FindPropertyRelative("sourceTransform");
                    var srcWeight = source.FindPropertyRelative("Weight")
                        ?? source.FindPropertyRelative("weight");

                    string srcName = srcTransform?.objectReferenceValue != null
                        ? (srcTransform.objectReferenceValue as Transform)?.name ?? srcTransform.objectReferenceValue.name
                        : "null";
                    float w = srcWeight?.floatValue ?? 0f;
                    sb.AppendLine($"    [{i}] {srcName} (weight={w:F2})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all VRC Constraints under an avatar root. Shows type, source count, and active state.")]
        public static string ListVRCConstraints(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"VRC Constraints under '{avatarRootName}':");
            int total = 0;

            string[] typeNames = { "Position", "Rotation", "Scale", "Parent", "Aim", "LookAt" };
            string[] fullTypeNames = {
                VrcPositionConstraintType, VrcRotationConstraintType, VrcScaleConstraintType,
                VrcParentConstraintType, VrcAimConstraintType, VrcLookAtConstraintType
            };

            for (int t = 0; t < fullTypeNames.Length; t++)
            {
                var type = VRChatTools.FindVrcType(fullTypeNames[t]);
                if (type == null) continue;

                var comps = go.GetComponentsInChildren(type, true);
                foreach (var comp in comps)
                {
                    total++;
                    var so = new SerializedObject(comp);
                    string path = VRChatTools.GetRelativePath(go.transform, (comp as Component).transform);

                    var isActive = so.FindProperty("IsActive") ?? so.FindProperty("m_Active") ?? so.FindProperty("isActive");
                    bool active = isActive?.boolValue ?? false;

                    var sources = so.FindProperty("Sources") ?? so.FindProperty("m_Sources") ?? so.FindProperty("sources");
                    int srcCount = sources != null && sources.isArray ? sources.arraySize : 0;

                    sb.AppendLine($"  [{total - 1}] VRC{typeNames[t]}Constraint on '{path}' Active={active} Sources={srcCount}");
                }
            }

            if (total == 0)
            {
                // Also check for VRCHeadChop
                var headChopType = VRChatTools.FindVrcType(VrcHeadChopType);
                if (headChopType != null)
                {
                    var headChops = go.GetComponentsInChildren(headChopType, true);
                    if (headChops.Length > 0)
                    {
                        foreach (var hc in headChops)
                        {
                            string path = VRChatTools.GetRelativePath(go.transform, (hc as Component).transform);
                            sb.AppendLine($"  VRCHeadChop on '{path}'");
                            total++;
                        }
                    }
                }
            }

            if (total == 0)
                return $"No VRC Constraints found under '{avatarRootName}'. (SDK 3.7+ required for VRC Constraints)";

            sb.AppendLine($"\nTotal: {total} VRC Constraint(s).");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Remove a VRC Constraint from a GameObject. type: Position, Rotation, Scale, Parent, Aim, LookAt. Requires confirmation.")]
        public static string RemoveVRCConstraint(string gameObjectName, string type)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var constraintType = ResolveConstraintType(type);
            if (constraintType == null) return $"Error: VRC {type}Constraint type not found.";

            var comp = go.GetComponent(constraintType);
            if (comp == null) return $"Error: No VRC{type}Constraint on '{gameObjectName}'.";

            if (!AgentSettings.RequestConfirmation(
                "VRC Constraint削除",
                $"'{gameObjectName}' から VRC{type}Constraint を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(comp);
            return $"Success: Removed VRC{type}Constraint from '{gameObjectName}'.";
        }

        [AgentTool("Add a VRCHeadChop component to the avatar root. Used to hide head mesh in first-person view.")]
        public static string AddHeadChop(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var type = VRChatTools.FindVrcType(VrcHeadChopType);
            if (type == null) return "Error: VRCHeadChop type not found. Ensure VRChat SDK is installed.";

            var existing = go.GetComponent(type);
            if (existing != null) return $"Error: VRCHeadChop already exists on '{avatarRootName}'.";

            Undo.AddComponent(go, type);
            return $"Success: Added VRCHeadChop to '{avatarRootName}'.";
        }

        [AgentTool("Inspect VRCHeadChop component. Shows target bones and settings.")]
        public static string InspectHeadChop(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var type = VRChatTools.FindVrcType(VrcHeadChopType);
            if (type == null) return "Error: VRCHeadChop type not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: No VRCHeadChop on '{avatarRootName}'.";

            var so = new SerializedObject(comp);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCHeadChop on '{avatarRootName}':");

            // Iterate all visible properties
            var iter = so.GetIterator();
            bool entered = iter.NextVisible(true);
            while (entered)
            {
                if (iter.name != "m_Script")
                {
                    switch (iter.propertyType)
                    {
                        case SerializedPropertyType.Boolean:
                            sb.AppendLine($"  {iter.name}: {iter.boolValue}");
                            break;
                        case SerializedPropertyType.Float:
                            sb.AppendLine($"  {iter.name}: {iter.floatValue:F3}");
                            break;
                        case SerializedPropertyType.Integer:
                            sb.AppendLine($"  {iter.name}: {iter.intValue}");
                            break;
                        case SerializedPropertyType.ObjectReference:
                            sb.AppendLine($"  {iter.name}: {(iter.objectReferenceValue != null ? iter.objectReferenceValue.name : "null")}");
                            break;
                        case SerializedPropertyType.ArraySize:
                            sb.AppendLine($"  {iter.name}: {iter.intValue} elements");
                            break;
                    }
                }
                entered = iter.NextVisible(false);
            }

            return sb.ToString().TrimEnd();
        }

        // ===== Helpers =====

        private static void SetBoolProp(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        private static void AppendBool(StringBuilder sb, SerializedObject so, params string[] names)
        {
            foreach (var name in names)
            {
                var p = so.FindProperty(name);
                if (p != null)
                {
                    sb.AppendLine($"  {name}: {p.boolValue}");
                    return;
                }
            }
        }

        private static void AppendFloat(StringBuilder sb, SerializedObject so, params string[] names)
        {
            foreach (var name in names)
            {
                var p = so.FindProperty(name);
                if (p != null)
                {
                    sb.AppendLine($"  {name}: {p.floatValue:F3}");
                    return;
                }
            }
        }

        private static void AppendVector3(StringBuilder sb, SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Vector3)
                sb.AppendLine($"  {name}: ({p.vector3Value.x:F3}, {p.vector3Value.y:F3}, {p.vector3Value.z:F3})");
        }
    }
}
