using System;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// The short list of tools that are answered on the MCP listener thread (InProc) or the
    /// bridge reader thread (Bridge) instead of being queued for the main thread.
    ///
    /// These exist to be useful precisely when the main thread is not available — a modal
    /// dialog is up, a domain reload is pending — so queuing them would give them the same fate
    /// as the calls they are meant to diagnose or unblock. Both transports consult this before
    /// their stall check, so the set stays identical between InProc and Bridge modes.
    ///
    /// Every tool here must touch no Unity API and raise no UI event. Anything that needs the
    /// main thread does not belong in this list.
    /// </summary>
    internal static class ListenerThreadTools
    {
        public static bool TryInvoke(string toolName, JNode args, out string text)
            => TryInvoke(toolName, args, out text, depth: 0);

        static bool TryInvoke(string toolName, JNode args, out string text, int depth)
        {
            switch (toolName)
            {
                // MCP clients on the 4-tool surface never call a Unity tool by name — they call
                // ExecuteUnityTool(name=..., arguments=...). Seen from here that is tool
                // "ExecuteUnityTool", so without this unwrap the fast-path only ever matched a
                // client that addressed GetEditorState directly, and a wrapped call fell through
                // to the stall check and was rejected as "main thread blocked" — exactly when
                // these tools are needed. One level only: ExecuteUnityTool nested in itself is
                // refused by the Invoker anyway.
                case "ExecuteUnityTool":
                    if (depth > 0 || args == null) { text = null; return false; }
                    return TryInvoke(args["name"].AsString ?? "", args["arguments"], out text, depth + 1);

                case "GetEditorState":
                    text = Tools.EditorStateTools.GetEditorState();
                    return true;

                case "AnswerModalDialog":
                    text = Tools.ModalDialogTools.AnswerModalDialog(
                        Str(args, "button"),
                        Str(args, "titleContains"),
                        Flag(args, "dryRun"));
                    return true;

                // Read-only view of the auto-answer rules; useful while a dialog is up to see
                // why (or why not) it was answered. Set/Clear stay on the main thread — they are
                // for arming before the run, and they write to the store.
                case "ListModalAutoAnswers":
                    text = Tools.ModalDialogTools.ListModalAutoAnswers();
                    return true;

                default:
                    text = null;
                    return false;
            }
        }

        static string Str(JNode args, string key)
            => args == null ? "" : (args[key].AsString ?? "");

        /// <summary>
        /// Accepts a JSON bool or the strings ToolUtility.TryParseBool understands, since not
        /// every MCP client types its arguments.
        /// </summary>
        static bool Flag(JNode args, string key)
        {
            if (args == null) return false;
            var node = args[key];
            if (node.Type == JNode.JType.Bool) return node.AsBool;
            if (node.Type == JNode.JType.String) return Tools.ToolUtility.TryParseBool(node.AsString, out bool b) && b;
            return false;
        }
    }
}
