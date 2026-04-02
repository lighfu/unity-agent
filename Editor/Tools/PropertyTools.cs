using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class PropertyTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("List all serialized properties of a component on a GameObject (name, type, value).")]
        public static string GetComponentProperties(string goName, string componentType)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found. Use FindObjectsByName(\"{goName.Split('/').Last()}\") to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.GetIterator();
            var sb = new StringBuilder();
            sb.AppendLine($"Properties of {componentType} on '{goName}':");

            int count = 0;
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                sb.AppendLine($"  {prop.propertyPath} ({prop.propertyType}): {GetPropertyValueString(prop)}");
                count++;
                if (count >= 50)
                {
                    sb.AppendLine("  ... (truncated at 50 properties)");
                    break;
                }
            }

            if (count == 0) sb.AppendLine("  (no visible properties)");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Get a specific property value from a component.")]
        public static string GetProperty(string goName, string componentType, string propertyPath)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found. Use FindObjectsByName(\"{goName.Split('/').Last()}\") to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found on '{componentType}'.";

            return $"{propertyPath} ({prop.propertyType}): {GetPropertyValueString(prop)}";
        }

        [AgentTool("Set a property value on a component. Supports: Integer, Float (e.g. '0.5'), Boolean ('true'/'false'), String, Enum (by index number). Use GetComponentProperties to see available properties and current values.")]
        public static string SetProperty(string goName, string componentType, string propertyPath, string value)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found. Use FindObjectsByName(\"{goName.Split('/').Last()}\") to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found on '{componentType}'.";

            Undo.RecordObject(comp, $"Set Property {propertyPath} via Agent");

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = Convert.ToInt32(value);
                        break;
                    case SerializedPropertyType.Float:
                        prop.floatValue = Convert.ToSingle(value);
                        break;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = Convert.ToBoolean(value);
                        break;
                    case SerializedPropertyType.String:
                        prop.stringValue = value;
                        break;
                    case SerializedPropertyType.Enum:
                        prop.enumValueIndex = Convert.ToInt32(value);
                        break;
                    default:
                        return $"Error: Property type '{prop.propertyType}' is not supported for setting. Supported: Integer, Float, Boolean, String, Enum.";
                }

                so.ApplyModifiedProperties();
                return $"Success: Set '{propertyPath}' on '{componentType}' to '{value}'.";
            }
            catch (FormatException)
            {
                return $"Error: '{value}' is not valid for {prop.propertyType} property '{propertyPath}'. " +
                       $"Expected: {GetFormatHint(prop.propertyType)}. Use GetComponentProperties('{goName}', '{componentType}') to check.";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to set property: {ex.Message}";
            }
        }

        [AgentTool("List all blend shapes and their weights on a SkinnedMeshRenderer.")]
        public static string ListBlendShapes(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found. Use FindObjectsByName(\"{goName.Split('/').Last()}\") to search.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer found on '{goName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return $"Error: SkinnedMeshRenderer on '{goName}' has no mesh.";

            int count = mesh.blendShapeCount;
            if (count == 0) return $"'{goName}' has no blend shapes.";

            var sb = new StringBuilder();
            sb.AppendLine($"Blend shapes on '{goName}' ({count}):");

            int limit = Math.Min(count, 50);
            for (int i = 0; i < limit; i++)
            {
                string shapeName = mesh.GetBlendShapeName(i);
                float weight = smr.GetBlendShapeWeight(i);
                sb.AppendLine($"  {i}: {shapeName} = {weight:F1}");
            }

            if (count > 50)
                sb.AppendLine($"  ... and {count - 50} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set a blend shape weight by name on a SkinnedMeshRenderer (weight: 0-100).")]
        public static string SetBlendShape(string goName, string blendShapeName, float weight)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found. Use FindObjectsByName(\"{goName.Split('/').Last()}\") to search.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer found on '{goName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return $"Error: SkinnedMeshRenderer on '{goName}' has no mesh.";

            int index = mesh.GetBlendShapeIndex(blendShapeName);
            if (index < 0) return $"Error: Blend shape '{blendShapeName}' not found on '{goName}'.";

            Undo.RecordObject(smr, "Set BlendShape via Agent");
            smr.SetBlendShapeWeight(index, weight);

            return $"Success: Set blend shape '{blendShapeName}' to {weight} on '{goName}'.";
        }

        private static string GetFormatHint(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer: return "integer number (e.g. '42')";
                case SerializedPropertyType.Float: return "decimal number (e.g. '0.5')";
                case SerializedPropertyType.Boolean: return "'true' or 'false'";
                case SerializedPropertyType.Enum: return "enum index number (e.g. '0', '1', '2')";
                default: return type.ToString();
            }
        }

        private static string GetPropertyValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F4");
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.String: return $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum: return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? $"{prop.enumDisplayNames[prop.enumValueIndex]} ({prop.enumValueIndex})"
                    : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                        : "None";
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return prop.vector4Value.ToString();
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.Rect: return prop.rectValue.ToString();
                case SerializedPropertyType.Bounds: return prop.boundsValue.ToString();
                case SerializedPropertyType.ArraySize: return prop.intValue.ToString();
                default: return $"({prop.propertyType})";
            }
        }
    }
}
