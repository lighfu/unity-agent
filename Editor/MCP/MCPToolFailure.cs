using System;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    internal static class MCPToolFailure
    {
        static readonly char[] LineBreaks = { '\r', '\n' };

        internal static string Target(string tool, JNode arguments)
        {
            string target = tool == "ExecuteUnityTool" ? arguments?["name"].AsString : null;
            return string.IsNullOrEmpty(target) ? tool : target;
        }

        // Built-in tools commonly return their runtime failures as strings. Inspect the
        // original first line before argument warnings or paging can obscure the marker.
        internal static JNode FromText(string tool, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string line = FirstLine(text);
            return line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Runtime Error:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Cancelled:", StringComparison.OrdinalIgnoreCase)
                ? Execution(tool, line) : null;
        }

        internal static JNode Execution(string tool, string reason, string detail = null)
            => Create("tool", tool, reason, "Check the target and arguments with DescribeUnityTool before retrying.", detail);

        internal static JNode Protocol(string tool, int code, string reason, string detail)
        {
            string suggestion = code switch
            {
                -32601 => "Find the correct tool with SearchUnityTool.",
                -32602 => "Check the required arguments and their types with DescribeUnityTool.",
                -32002 => "Use GetEditorState and AnswerModalDialog with dryRun=true to inspect a blocking dialog.",
                -32001 => "Check the Editor state and verify whether the original operation completed before retrying.",
                _ => "Check the tool settings, Editor state and Console before retrying.",
            };
            return Create("json_rpc", tool, reason, suggestion, detail);
        }

        internal static JNode Result(JNode content, JNode failure)
        {
            var result = JNode.Obj(("content", content), ("isError", JNode.Bool(failure != null)));
            if (failure != null) result.AsObject.Add("structuredContent", failure);
            return result;
        }

        static JNode Create(string kind, string tool, string reason, string suggestion, string detail)
        {
            var suggestions = JNode.Arr(JNode.Str(suggestion));
            const string candidates = "Did you mean: ";
            if (detail != null && detail.StartsWith(candidates, StringComparison.Ordinal))
                foreach (string name in detail.Substring(candidates.Length).Split(',')) suggestions.AsArray.Add(JNode.Str(name.Trim()));
            var result = JNode.Obj(("kind", JNode.Str(kind)), ("tool", JNode.Str(tool)),
                ("reason", JNode.Str(FirstLine(reason))), ("suggestions", suggestions));
            if (!string.IsNullOrEmpty(detail)) result.AsObject.Add("detail", JNode.Str(detail));
            return result;
        }

        internal static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Tool execution failed.";
            string line = text.TrimStart();
            int newline = line.IndexOfAny(LineBreaks);
            int length = newline < 0 ? line.Length : newline;
            return length <= 512 ? line.Substring(0, length).TrimEnd() : line.Substring(0, 512) + "…";
        }
    }
}
