using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.Editor;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class ComponentTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Add a component by type name (e.g. 'Rigidbody', 'BoxCollider'). Returns info if component already exists.")]
        public static string AddComponent(string gameObjectName, string componentName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            Type type = FindComponentType(componentName);
            if (type == null) return $"Error: Component type '{componentName}' not found.";

            if (go.GetComponent(type) != null) return $"Info: '{componentName}' already exists on '{gameObjectName}'.";

            Undo.AddComponent(go, type);
            return $"Success: Added '{componentName}' to '{gameObjectName}'.";
        }

        [AgentTool("Remove a component from a GameObject. Requires user confirmation when enabled.")]
        public static string RemoveComponent(string gameObjectName, string componentName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found. Use FindObjectsByName to search.";

            Type type = FindComponentType(componentName);
            if (type == null) return $"Error: Component type '{componentName}' not found.";

            var component = go.GetComponent(type);
            if (component == null) return $"Error: '{componentName}' not found on '{gameObjectName}'.";

            if (!AgentSettings.RequestConfirmation(
                "コンポーネントを削除",
                $"'{gameObjectName}' から '{componentName}' を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(component);
            return $"Success: Removed '{componentName}' from '{gameObjectName}'.";
        }

        public static Type FindComponentType(string name)
        {
            // 1. Try exact match
            var type = Type.GetType(name);
            if (type != null) return type;

            // 2. Try UnityEngine namespace
            type = Type.GetType($"UnityEngine.{name}, UnityEngine");
            if (type != null) return type;

            // 3. Search all assemblies (match on Name or FullName)
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetTypes().FirstOrDefault(t =>
                        (t.Name == name || t.FullName == name) &&
                        typeof(Component).IsAssignableFrom(t));
                    if (type != null) return type;
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies with unresolvable types
                }
            }

            return null;
        }
    }
}
