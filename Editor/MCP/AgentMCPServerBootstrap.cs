using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// MCP サーバーのオートスタート。
    /// Editor ドメインがロードされた時点で <see cref="AgentSettings.MCPServerEnabled"/> を確認し、
    /// 有効なら <see cref="AgentMCPServer.StartShared"/> を呼ぶ。UnityAgentWindow が開いていなくても動作する。
    /// また、Editor が終了/リロードされる直前に確実に Stop を呼ぶ。
    /// </summary>
    [InitializeOnLoad]
    internal static class AgentMCPServerBootstrap
    {
        static AgentMCPServerBootstrap()
        {
            // DelayCall で domain reload 後に安定してから起動
            EditorApplication.delayCall += StartIfEnabled;

            AssemblyReloadEvents.beforeAssemblyReload += StopBeforeReload;
            EditorApplication.quitting += StopBeforeReload;
        }

        static void StartIfEnabled()
        {
            if (!AgentSettings.MCPServerEnabled) return;
            AgentMCPServer.StartShared();
        }

        static void StopBeforeReload()
        {
            AgentMCPServer.StopShared();
        }
    }
}
