using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class ToolResultTools
    {
        [AgentTool(@"Read another page of a saved Unity tool result without executing the original tool again.
Use resultId and page.nextOffset from a paged result. offset counts textSegments, not source objects.
limit defaults to 50 segments (1..200); maxChars defaults to 8192 (1024..32768).
Pages contain exact text fragments. Concatenate text from offset 0 onward to reconstruct the full result;
parse an original JSON result only after joining all fragments. Newlines and Unicode boundaries are preserved.
A snapshot contains only data collected under the original tool's filters, depth and source-row limits;
those native controls are independent of this output page budget.
Snapshots expire 15 minutes after creation, can be evicted for capacity, and are cleared by domain reload.
Unknown/expired IDs return an error; do not repeat a modifying tool to retrieve its output.
Images remain on the original call's response; this tool retrieves saved text only.", Risk = ToolRisk.Safe)]
        public static string ReadUnityToolResultPage(string resultId, int offset = 0,
            int limit = ToolResultPager.DefaultLimit, int maxChars = ToolResultPager.DefaultMaxChars)
        {
            return ToolResultPager.ReadPage(resultId, offset, limit, maxChars);
        }
    }
}
