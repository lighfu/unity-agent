using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class InspectorTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("List ALL components on a GameObject with their enabled state and type info. Shows full Inspector overview.")]
        public static string InspectGameObject(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var sb = new StringBuilder();
            sb.AppendLine($"Inspector: '{gameObjectName}'");
            sb.AppendLine($"  Active: {go.activeSelf}");
            sb.AppendLine($"  Tag: {go.tag}");
            sb.AppendLine($"  Layer: {LayerMask.LayerToName(go.layer)} ({go.layer})");
            sb.AppendLine($"  Static: {go.isStatic}");

            // Transform
            var t = go.transform;
            sb.AppendLine($"  Transform:");
            sb.AppendLine($"    Position: {t.localPosition}");
            sb.AppendLine($"    Rotation: {t.localEulerAngles}");
            sb.AppendLine($"    Scale: {t.localScale}");

            // All components
            var components = go.GetComponents<Component>();
            sb.AppendLine($"\n  Components ({components.Length}):");
            foreach (var comp in components)
            {
                if (comp == null)
                {
                    sb.AppendLine("    [Missing Script]");
                    continue;
                }

                string typeName = comp.GetType().Name;
                string fullTypeName = comp.GetType().FullName;

                // Check enabled state for Behaviour types
                string enabledStr = "";
                if (comp is Behaviour behaviour)
                    enabledStr = behaviour.enabled ? " [ON]" : " [OFF]";
                else if (comp is Renderer renderer)
                    enabledStr = renderer.enabled ? " [ON]" : " [OFF]";
                else if (comp is Collider collider)
                    enabledStr = collider.enabled ? " [ON]" : " [OFF]";

                sb.AppendLine($"    {typeName}{enabledStr} ({fullTypeName})");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Deep inspect a component with ALL properties including nested objects, arrays, and object references. maxDepth controls recursion (default 2). Useful for understanding complex components.")]
        public static string DeepInspectComponent(string gameObjectName, string componentType, int maxDepth = 2)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var sb = new StringBuilder();
            sb.AppendLine($"Deep Inspect: {componentType} on '{gameObjectName}'");

            var prop = so.GetIterator();
            bool enterChildren = true;
            int count = 0;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                int depth = prop.depth;
                if (depth > maxDepth)
                {
                    enterChildren = false;
                    continue;
                }

                string indent = new string(' ', depth * 2 + 2);
                string valueStr = GetDetailedPropertyValue(prop);
                sb.AppendLine($"{indent}{prop.propertyPath} [{prop.propertyType}] = {valueStr}");

                count++;
                if (count >= 200)
                {
                    sb.AppendLine("  ... (truncated at 200 properties)");
                    break;
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set a Vector3 property on a component. Use for positions, rotations, scales, etc. Example: SetVector3Property('MyObject', 'Transform', 'm_LocalPosition', 1.0, 2.0, 3.0)")]
        public static string SetVector3Property(string gameObjectName, string componentType, string propertyPath, float x, float y, float z)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";

            if (prop.propertyType != SerializedPropertyType.Vector3)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not Vector3.";

            Undo.RecordObject(comp, $"Set Vector3 {propertyPath}");
            prop.vector3Value = new Vector3(x, y, z);
            so.ApplyModifiedProperties();

            return $"Success: Set '{propertyPath}' to ({x}, {y}, {z}) on {componentType}.";
        }

        [AgentTool("Set a Color property on a component. RGBA values 0-1. Example: SetColorProperty('MyObject', 'Renderer', 'm_Color', 1.0, 0.5, 0.0, 1.0)")]
        public static string SetColorProperty(string gameObjectName, string componentType, string propertyPath, float r, float g, float b, float a = 1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";

            if (prop.propertyType != SerializedPropertyType.Color)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not Color.";

            Undo.RecordObject(comp, $"Set Color {propertyPath}");
            prop.colorValue = new Color(r, g, b, a);
            so.ApplyModifiedProperties();

            return $"Success: Set '{propertyPath}' to ({r}, {g}, {b}, {a}) on {componentType}.";
        }

        [AgentTool("Set an object reference property on a component. Finds the asset by path or name. Example: SetObjectReference('MyObject', 'Animator', 'm_Controller', 'Assets/Animations/MyController.controller')")]
        public static string SetObjectReference(string gameObjectName, string componentType, string propertyPath, string assetPathOrName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";

            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not ObjectReference.";

            // Find the asset
            UnityEngine.Object asset = null;

            // Try direct path first
            if (assetPathOrName.StartsWith("Assets/") || assetPathOrName.StartsWith("Packages/"))
            {
                asset = AssetDatabase.LoadMainAssetAtPath(assetPathOrName);
            }

            // Try search by name
            if (asset == null)
            {
                string[] guids = AssetDatabase.FindAssets(assetPathOrName);
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    asset = AssetDatabase.LoadMainAssetAtPath(path);
                }
            }

            // Try finding as scene object
            if (asset == null)
            {
                var sceneGo = FindGO(assetPathOrName);
                if (sceneGo != null) asset = sceneGo;
            }

            if (asset == null)
                return $"Error: Could not find asset or object '{assetPathOrName}'.";

            Undo.RecordObject(comp, $"Set ObjectRef {propertyPath}");
            prop.objectReferenceValue = asset;
            so.ApplyModifiedProperties();

            return $"Success: Set '{propertyPath}' to '{asset.name}' ({asset.GetType().Name}) on {componentType}.";
        }

        [AgentTool("Toggle a component's enabled state. Works with any Behaviour, Renderer, or Collider.")]
        public static string ToggleComponent(string gameObjectName, string componentType, bool enabled)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            Undo.RecordObject(comp, $"Toggle {componentType}");

            if (comp is Behaviour behaviour)
            {
                behaviour.enabled = enabled;
                return $"Success: {componentType} on '{gameObjectName}' is now {(enabled ? "enabled" : "disabled")}.";
            }
            if (comp is Renderer renderer)
            {
                renderer.enabled = enabled;
                return $"Success: {componentType} on '{gameObjectName}' is now {(enabled ? "enabled" : "disabled")}.";
            }
            if (comp is Collider collider)
            {
                collider.enabled = enabled;
                return $"Success: {componentType} on '{gameObjectName}' is now {(enabled ? "enabled" : "disabled")}.";
            }

            return $"Error: {componentType} does not support enable/disable toggling.";
        }

        [AgentTool("Set a GameObject's active state (show/hide). Example: SetActive('MyObject', false) to hide.")]
        public static string SetActive(string gameObjectName, bool active)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            Undo.RecordObject(go, $"SetActive via Agent");
            go.SetActive(active);
            return $"Success: '{gameObjectName}' is now {(active ? "active" : "inactive")}.";
        }

        [AgentTool("Set a GameObject's tag. Example: SetTag('MyObject', 'Player').")]
        public static string SetTag(string gameObjectName, string tag)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            try
            {
                Undo.RecordObject(go, "Set Tag via Agent");
                go.tag = tag;
                return $"Success: Set tag of '{gameObjectName}' to '{tag}'.";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to set tag: {ex.Message}. Available tags might not include '{tag}'.";
            }
        }

        [AgentTool("Set a GameObject's layer by name. Example: SetLayer('MyObject', 'UI').")]
        public static string SetLayer(string gameObjectName, string layerName, bool includeChildren = false)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1) return $"Error: Layer '{layerName}' not found.";

            Undo.RecordObject(go, "Set Layer via Agent");
            go.layer = layer;

            if (includeChildren)
            {
                foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                {
                    Undo.RecordObject(child.gameObject, "Set Layer via Agent");
                    child.gameObject.layer = layer;
                }
            }

            return $"Success: Set layer of '{gameObjectName}' to '{layerName}' ({layer}).{(includeChildren ? " (including children)" : "")}";
        }

        [AgentTool("Copy a component from one GameObject to another. Copies all serialized property values.")]
        public static string CopyComponent(string sourceGoName, string targetGoName, string componentType)
        {
            var sourceGo = FindGO(sourceGoName);
            if (sourceGo == null) return $"Error: Source GameObject '{sourceGoName}' not found. Use FindObjectsByName to search.";

            var targetGo = FindGO(targetGoName);
            if (targetGo == null) return $"Error: Target GameObject '{targetGoName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var sourceComp = sourceGo.GetComponent(type);
            if (sourceComp == null) return $"Error: Component '{componentType}' not found on '{sourceGoName}'.";

            // Use UnityEditorInternal for component copying
            UnityEditorInternal.ComponentUtility.CopyComponent(sourceComp);

            var existing = targetGo.GetComponent(type);
            if (existing != null)
            {
                Undo.RecordObject(existing, "Paste Component Values via Agent");
                UnityEditorInternal.ComponentUtility.PasteComponentValues(existing);
                return $"Success: Pasted '{componentType}' values from '{sourceGoName}' to '{targetGoName}' (existing component updated).";
            }
            else
            {
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(targetGo);
                return $"Success: Copied '{componentType}' from '{sourceGoName}' to '{targetGoName}' (new component added).";
            }
        }

        [AgentTool("Set an array element on a component. Useful for setting array-type properties. arrayIndex: index in array. value: string representation.")]
        public static string SetArrayElement(string gameObjectName, string componentType, string propertyPath, int arrayIndex, string value)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            var type = ComponentTools.FindComponentType(componentType);
            if (type == null) return $"Error: Component type '{componentType}' not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";

            if (!prop.isArray)
                return $"Error: Property '{propertyPath}' is not an array.";

            if (arrayIndex < 0 || arrayIndex >= prop.arraySize)
                return $"Error: Index {arrayIndex} out of range (0-{prop.arraySize - 1}).";

            var element = prop.GetArrayElementAtIndex(arrayIndex);
            Undo.RecordObject(comp, "Set Array Element via Agent");

            try
            {
                SetPropertyValue(element, value);
                so.ApplyModifiedProperties();
                return $"Success: Set {propertyPath}[{arrayIndex}] to '{value}'.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        // ─── Helpers ───

        private static string GetDetailedPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F4");
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.String: return $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? $"{prop.enumDisplayNames[prop.enumValueIndex]} ({prop.enumValueIndex})"
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                        return string.IsNullOrEmpty(assetPath)
                            ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                            : $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name}) @ {assetPath}";
                    }
                    return "None";
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4: return prop.vector4Value.ToString();
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.Rect: return prop.rectValue.ToString();
                case SerializedPropertyType.Bounds: return prop.boundsValue.ToString();
                case SerializedPropertyType.Quaternion: return prop.quaternionValue.eulerAngles.ToString();
                case SerializedPropertyType.ArraySize: return $"[{prop.intValue} elements]";
                case SerializedPropertyType.AnimationCurve:
                    return prop.animationCurveValue != null ? $"AnimCurve ({prop.animationCurveValue.length} keys)" : "null";
                default: return $"({prop.propertyType})";
            }
        }

        private static void SetPropertyValue(SerializedProperty prop, string value)
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
                    throw new Exception($"Setting {prop.propertyType} is not supported for array elements.");
            }
        }
    }
}
