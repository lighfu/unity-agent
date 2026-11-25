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
        // Fail closed until a main-thread startup/pump or settings setter publishes a gate.
        // Listener threads must never touch AgentSettings' mutable sets or lazy SettingsStore.
        static volatile string _readerGateError = "Tool 'ReadUnityToolResultPage' access settings have not been initialized.";
        static volatile string _scriptJobGateError = "Tool 'GetJobResult' access settings have not been initialized.";

        internal static void RefreshReaderGateOnMainThread()
        {
            string readerError = "Tool 'ReadUnityToolResultPage' is not available.";
            string scriptJobError = "Tool 'GetJobResult' is not available.";
            try
            {
                foreach (var info in ToolRegistry.GetAllTools())
                {
                    string name = info.method?.Name;
                    bool isReader = ToolResultRequest.IsReader(name);
                    if (!isReader && name != "GetJobResult") continue;
                    string error;
                    if (!AgentSettings.IsToolEnabled(name, info.isExternal))
                        error = $"Tool '{name}' is disabled in UnityAgent settings.";
                    else if ((int)info.resolvedRisk > (int)AgentSettings.MCPServerExposeRisk)
                        error = $"Tool '{name}' risk level ({info.resolvedRisk}) exceeds MCP expose limit ({AgentSettings.MCPServerExposeRisk}).";
                    else error = null;
                    if (isReader) readerError = error;
                    else scriptJobError = error;
                }
            }
            catch
            {
                readerError = "Tool 'ReadUnityToolResultPage' access settings are unavailable.";
                scriptJobError = "Tool 'GetJobResult' access settings are unavailable.";
            }
            _readerGateError = readerError;
            _scriptJobGateError = scriptJobError;
        }

        /// <summary>
        /// The work that answers this call off the main thread, or null when the call belongs on
        /// the main thread. The work may block — GetVRChatBuildTestResult waits up to its
        /// waitSeconds — so run it on a thread that is allowed to wait, never on the bridge's
        /// single reader thread.
        /// </summary>
        public static Func<string> Match(string toolName, JNode args)
        {
            var work = Match(toolName, args, out string error);
            return error == null ? work : () => error;
        }

        public static Func<string> Match(string toolName, JNode args, out string error)
            => Match(toolName, args, out error, out _, out _);

        public static Func<string> Match(string toolName, JNode args, out string error, out int errorCode, out ToolResultRequest.Options options)
        {
            var work = MatchResult(toolName, args, out error, out errorCode, out options);
            return work == null ? null : () => work().Text;
        }

        internal static Func<(string Text, JNode Failure)> MatchResult(string toolName, JNode args,
            out string error, out int errorCode, out ToolResultRequest.Options options)
        {
            errorCode = -32602;
            if (!ToolResultRequest.TryGetOptions(toolName, args, out options, out error)) return null;
            string targetName = MCPToolFailure.Target(toolName, args);
            if (targetName == "AnswerModalDialog")
            {
                error = ValidateModalArguments(toolName, args);
                if (error != null) return null;
            }
            if (ToolResultRequest.IsReader(targetName) || targetName == "GetJobResult")
            {
                error = targetName == "GetJobResult" ? _scriptJobGateError : _readerGateError;
                if (error != null) { errorCode = -32000; return null; }
            }
            var work = MatchCore(toolName, args, depth: 0);
            var resultOptions = options;
            return work == null ? null : () =>
            {
                string text = work();
                JNode failure = MCPToolFailure.FromText(targetName, text);
                return (failure == null ? ToolResultRequest.Format(targetName, text, resultOptions)
                    : ToolResultRequest.FormatError(targetName, text, resultOptions), failure);
            };
        }

        static string ValidateModalArguments(string toolName, JNode args)
        {
            if (toolName == "ExecuteUnityTool")
            {
                string invalidEnvelope = ToolResultRequest.ValidateExecuteEnvelope(args);
                if (invalidEnvelope != null) return invalidEnvelope;
                args = args["arguments"];
            }

            if (args == null || args.IsNull) return null;
            if (args.Type != JNode.JType.Object) return "AnswerModalDialog arguments must be an object.";
            foreach (string key in args.Keys)
            {
                JNode value = args[key];
                switch (key)
                {
                    case "button":
                    case "titleContains":
                        if (value.Type != JNode.JType.String && !value.IsNull) return $"AnswerModalDialog '{key}' must be a string.";
                        break;
                    case "dryRun":
                        if (value.Type != JNode.JType.Bool && !(value.Type == JNode.JType.String && Tools.ToolUtility.TryParseBool(value.AsString, out _)))
                            return "AnswerModalDialog 'dryRun' must be a boolean.";
                        break;
                    default:
                        return $"Unknown AnswerModalDialog argument '{key}'. Valid keys: button, titleContains, dryRun.";
                }
            }
            return null;
        }

        static Func<string> MatchCore(string toolName, JNode args, int depth)
        {
            if (ToolResultRequest.IsReader(toolName))
            {
                ToolResultRequest.TryReadReaderArguments(args, out string resultId, out int offset, out int limit, out int maxChars, out _);
                return () => ToolResultPager.ReadPage(resultId, offset, limit, maxChars);
            }
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
                    return MatchCore(args["name"].AsString ?? "", args["arguments"], depth + 1);

                case "GetEditorState":
                    return Tools.EditorStateTools.GetEditorState;

                // These read locked caches only. An action's callback can hold the main thread
                // in a modal, so its status must not wait behind the callback it describes.
                case "GetUIActionResult":
                    return () => Tools.UIActionTools.GetUIActionResult(Str(args, "actionId"));

                case "GetIMGUIInspectionResult":
                    return () => Tools.IMGUIInspectionTools.GetIMGUIInspectionResult(Str(args, "inspectionId"));

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

                // Polled while the SDK build holds the main thread (#29). Never returns null: an id
                // that is not in memory (domain reload, typo) is answered from the in-memory mirror
                // of the session records, so it cannot end up queued behind the build.
                case "GetVRChatBuildTestResult":
                    return Tools.VRChatBuildTestTools.MatchOffMainThread(Str(args, "jobId"), Int(args, "waitSeconds"));

                case "GetJobResult":
                    return Tools.ScriptJobTools.MatchOffMainThread(Str(args, "jobId"), Int(args, "waitSeconds"));

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
