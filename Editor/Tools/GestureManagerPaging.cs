using System;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Shared bounded pagination for Gesture Manager read results.</summary>
    internal static class GestureManagerPaging
    {
        internal const int DefaultLimit = 50;
        internal const int MaximumLimit = 200;

        internal static string Validate(int offset, int limit)
        {
            if (offset < 0) return "Error: offset must be a non-negative integer.";
            if (limit < 1 || limit > MaximumLimit)
                return $"Error: limit must be between 1 and {MaximumLimit}.";
            return null;
        }

        internal static JNode Metadata(int total, int offset, int limit, int returned)
        {
            string error = Validate(offset, limit);
            if (error != null) throw new ArgumentException(error);
            int available = offset < total ? total - offset : 0;
            if (total < 0 || returned < 0 || returned > Math.Min(limit, available))
                throw new ArgumentOutOfRangeException(nameof(returned));
            int remaining = available - returned;
            bool hasMore = remaining > 0;
            return JNode.Obj(
                ("total", JNode.Num(total)), ("offset", JNode.Num(offset)),
                ("limit", JNode.Num(limit)), ("returned", JNode.Num(returned)),
                ("remaining", JNode.Num(remaining)), ("hasMore", JNode.Bool(hasMore)),
                ("nextOffset", hasMore ? JNode.Num(offset + returned) : JNode.NullNode));
        }

        internal static JNode Page(IReadOnlyList<JNode> items, int offset, int limit)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            string error = Validate(offset, limit);
            if (error != null) throw new ArgumentException(error);
            int count = offset < items.Count ? Math.Min(limit, items.Count - offset) : 0;
            var page = new JNode[count];
            for (int i = 0; i < count; i++) page[i] = items[offset + i];
            return JNode.Obj(("items", JNode.Arr(page)),
                ("page", Metadata(items.Count, offset, limit, count)));
        }
    }
}
