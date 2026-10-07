using System;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Source windows stay separate from paging a captured tool result.</summary>
    internal static class ToolSourcePaging
    {
        internal readonly struct Window
        {
            internal readonly int Total, Offset, Limit, Start, Count, Remaining;
            internal readonly bool FromEnd;
            internal int End => Start + Count;
            internal bool HasMore => Remaining > 0;
            internal int? NextOffset => HasMore ? Offset + Count : (int?)null;

            internal Window(int total, int offset, int limit, bool fromEnd)
            {
                if (total < 0 || offset < 0 || limit < 1) throw new ArgumentOutOfRangeException();
                Total = total;
                Offset = offset;
                Limit = limit;
                FromEnd = fromEnd;
                int available = offset < total ? total - offset : 0;
                Count = Math.Min(limit, available);
                Start = fromEnd ? available - Count : Math.Min(offset, total);
                Remaining = available - Count;
            }

            internal JNode Metadata() => JNode.Obj(
                ("total", JNode.Num(Total)), ("offset", JNode.Num(Offset)),
                ("limit", JNode.Num(Limit)), ("returned", JNode.Num(Count)),
                ("remaining", JNode.Num(Remaining)), ("hasMore", JNode.Bool(HasMore)),
                ("nextOffset", NextOffset.HasValue ? JNode.Num(NextOffset.Value) : JNode.NullNode),
                ("direction", JNode.Str(FromEnd ? "older" : "forward")),
                ("sourceTruncated", JNode.Bool(Offset > 0 || Count < Total)));

            internal string Describe() => "sourcePage=" + Metadata().ToJson();
        }
    }
}
