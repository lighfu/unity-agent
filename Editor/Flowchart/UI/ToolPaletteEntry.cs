using System.Reflection;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Lightweight projection of a tool registry entry for the palette UI.
    /// Keeps the MethodInfo so the inspector (Phase 4) can introspect parameters
    /// without hitting the registry again.
    /// </summary>
    internal sealed class ToolPaletteEntry
    {
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }
        public ToolRisk Risk { get; }
        public MethodInfo Method { get; }
        public bool IsExternal { get; }

        public ToolPaletteEntry(string name, string description, string category, ToolRisk risk, MethodInfo method, bool isExternal)
        {
            Name = name;
            Description = description;
            Category = string.IsNullOrEmpty(category) ? "Other" : category;
            Risk = risk;
            Method = method;
            IsExternal = isExternal;
        }
    }
}
