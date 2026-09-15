using System;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// The short list of tools that are answered on the MCP listener side (InProc: the request's
    /// thread-pool thread; Bridge: a thread-pool thread handed the call by the reader) instead of
    /// being queued for the main thread.
    ///
    /// These exist to be useful precisely when the main thread is not available — a modal
    /// dialog is up, a domain reload is pending, the VRChat SDK is building — so queuing them would
    /// give them the same fate as the calls they are meant to diagnose or unblock. Both transports
    /// consult this before their stall check, so the set stays identical between InProc and Bridge.
    ///
    /// Every tool here must touch no Unity API and raise no UI event. Anything that needs the
    /// main thread does not belong in this list.
    /// </summary>
    internal static class ListenerThreadTools
    {
        /// <summary>
        /// The work that answers this call off the main thread, or null when the call belongs on
        /// the main thread. The work may block — GetVRChatBuildTestResult waits up to its
        /// waitSeconds — so run it on a thread that is allowed to wait, never on the bridge's
        /// single reader thread.
        /// </summary>
        public static Func<string> Match(string toolName, JNode args) => Match(toolName, args, depth: 0);

        static Func<string> Match(string toolName, JNode args, int depth)
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
                    if (depth > 0 || args == null) return null;
                    return Match(args["name"].AsString ?? "", args["arguments"], depth + 1);

                case "GetEditorState":
                    return Tools.EditorStateTools.GetEditorState;

                case "AnswerModalDialog":
                {
                    string button = Str(args, "button");
                    string titleContains = Str(args, "titleContains");
                    bool dryRun = Flag(args, "dryRun");
                    return () => Tools.ModalDialogTools.AnswerModalDialog(button, titleContains, dryRun);
                }

                // Read-only view of the auto-answer rules; useful while a dialog is up to see
                // why (or why not) it was answered. Set/Clear stay on the main thread — they are
                // for arming before the run, and they write to the store.
                case "ListModalAutoAnswers":
                    return Tools.ModalDialogTools.ListModalAutoAnswers;

                // Polled while the SDK build holds the main thread (#29). A job that is not in
                // memory any more (domain reload) returns null here and goes to the main thread,
                // where the editor-session record can say what became of it.
                case "GetVRChatBuildTestResult":
                    return Tools.VRChatBuildTestTools.MatchOffMainThread(Str(args, "jobId"), Int(args, "waitSeconds"));

                default:
                    return null;
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

        /// <summary>A JSON number or a numeric string; anything else is 0.</summary>
        static int Int(JNode args, string key)
        {
            if (args == null) return 0;
            var node = args[key];
            if (node.Type == JNode.JType.Number) return node.AsInt;
            if (node.Type == JNode.JType.String && int.TryParse(node.AsString, out int n)) return n;
            return 0;
        }
    }
}
