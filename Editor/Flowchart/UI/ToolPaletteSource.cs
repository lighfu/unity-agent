using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Adapts ToolRegistry to the palette's flat entry list. Cached the first time it's
    /// asked because the registry itself caches once per AppDomain — re-querying every
    /// keystroke during search would cost reflection on hundreds of tools.
    /// </summary>
    internal static class ToolPaletteSource
    {
        static List<ToolPaletteEntry> _cache;

        public static IReadOnlyList<ToolPaletteEntry> GetAll()
        {
            if (_cache != null) return _cache;

            var entries = new List<ToolPaletteEntry>();
            foreach (var info in ToolRegistry.GetAllTools())
            {
                if (info.method == null || info.attribute == null) continue;
                entries.Add(new ToolPaletteEntry(
                    name: info.method.Name,
                    description: info.attribute.Description ?? "",
                    category: info.attribute.Category,
                    risk: info.resolvedRisk,
                    method: info.method,
                    isExternal: info.isExternal));
            }

            _cache = entries
                .OrderBy(e => e.Category)
                .ThenBy(e => e.Name)
                .ToList();
            return _cache;
        }

        public static ToolPaletteEntry FindByName(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;
            foreach (var e in GetAll())
                if (string.Equals(e.Name, toolName, System.StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        public static IEnumerable<IGrouping<string, ToolPaletteEntry>> GroupedByCategory(string searchText = null)
        {
            var query = (IEnumerable<ToolPaletteEntry>)GetAll();
            if (!string.IsNullOrEmpty(searchText))
            {
                string kw = searchText.ToLowerInvariant();
                query = query.Where(e =>
                    e.Name.ToLowerInvariant().Contains(kw) ||
                    (e.Description != null && e.Description.ToLowerInvariant().Contains(kw)) ||
                    e.Category.ToLowerInvariant().Contains(kw));
            }
            return query.GroupBy(e => e.Category).OrderBy(g => g.Key);
        }
    }
}
