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
        [AgentTool("Add a component by type name (e.g. 'Rigidbody', 'BoxCollider'). Returns info if component already exists.")]
        public static string AddComponent(string gameObjectName, string componentName)
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;
            if (!TryResolveComponentType(componentName, out Type type, out error)) return error;

            if (go.GetComponent(type) != null) return $"Info: '{componentName}' already exists on {SceneTools.DescribeTarget(go)}.";

            Undo.AddComponent(go, type);
            return $"Success: Added '{componentName}' to {SceneTools.DescribeTarget(go)}.";
        }

        [AgentTool("Remove a component from a GameObject. Requires user confirmation when enabled.")]
        public static string RemoveComponent(string gameObjectName, string componentName)
        {
            if (!SceneTools.TryResolveTarget(gameObjectName, out var go, out string error)) return error;
            if (!TryResolveComponent(go, componentName, out var component, out error)) return error;
            string target = SceneTools.DescribeTarget(go);

            if (!AgentSettings.RequestConfirmation(
                "コンポーネントを削除",
                $"{target} から '{componentName}' を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(component);
            return $"Success: Removed '{componentName}' from {target}.";
        }

        internal static bool TryResolveComponentType(string name, out Type type, out string error)
        {
            type = Type.GetType(name ?? "");
            error = null;
            if (type != null && type.IsSubclassOf(typeof(Component)) && name.Contains(",")) return true;

            var candidates = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => t.FullName == name || t.Name == name).ToArray();
            if (candidates.Length == 1)
            {
                type = candidates[0];
                return true;
            }
            type = null;
            error = candidates.Length == 0
                ? $"Error: Component type '{name}' was not found."
                : $"Error: Component type '{name}' is ambiguous. Use one of: " +
                  string.Join(", ", candidates.Select(t => t.AssemblyQualifiedName));
            return false;
        }

        internal static bool TryResolveComponent(GameObject go, string name, out Component component, out string error)
        {
            component = null;
            error = null;
            if (name.StartsWith("instanceId:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name.Substring("instanceId:".Length), out int id))
            {
                component = EditorUtility.InstanceIDToObject(id) as Component;
                if (component != null && component.gameObject == go) return true;
                error = $"Error: '{name}' does not identify a component on {SceneTools.DescribeTarget(go)}.";
                return false;
            }
            if (!TryResolveComponentType(name, out var type, out error)) return false;
            var matches = go.GetComponents(type);
            if (matches.Length == 1)
            {
                component = matches[0];
                return true;
            }
            error = matches.Length == 0
                ? $"Error: Component '{name}' was not found on {SceneTools.DescribeTarget(go)}."
                : $"Error: Component '{name}' is ambiguous on {SceneTools.DescribeTarget(go)}. Use one of: " +
                  string.Join(", ", matches.Select(c => $"instanceId:{c.GetInstanceID()}"));
            return false;
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
