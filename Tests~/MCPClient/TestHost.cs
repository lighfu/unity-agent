// The protocol client only needs logging and secret masking from the Editor host.
// Link the production client directly; no transport or parsing behavior is mocked.
namespace AjisaiFlow.UnityAgent.Editor
{
    internal enum LogTag { MCP }
    internal static class AgentLogger
    {
        internal static void Info(LogTag tag, string message) { }
        internal static void Debug(LogTag tag, string message) { }
        internal static void Warning(LogTag tag, string message) { }
        internal static void Error(LogTag tag, string message) { }
    }
}

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    internal static class AgentMCPServer
    {
        internal static string MaskSecretValue(string key, string value) => "[test]";
    }
}
