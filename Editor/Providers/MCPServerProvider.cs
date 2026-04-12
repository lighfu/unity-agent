using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// MCP Server モード用の no-op プロバイダー。
    ///
    /// 外部エージェント (Claude Code, Cursor 等) が <see cref="AgentMCPServer"/> 経由で
    /// UnityAgent のツールを直接呼び出す運用を想定した場合、UnityAgent 内部のチャット LLM は
    /// 動作する必要が無い。本プロバイダーは <see cref="CallLLM"/> で即座に終了し、
    /// UnityAgent の会話ループを実質無効化する。
    ///
    /// ただし、外部ツール呼び出しの最中に UnityAgent 側で発生するユーザー対話
    /// (メッシュ選択, 確認ダイアログ, AskUser 等) は <see cref="AgentMCPServer"/> 経由の
    /// ツール実行コルーチンが直接駆動するため、引き続き正常に動作する。
    /// </summary>
    public class MCPServerProvider : ILLMProvider
    {
        public string ProviderName => "MCP Server (External Agent)";

        public void Abort()
        {
            // 外部エージェント駆動なので UnityAgent 側にアボート対象は無い。
        }

        public IEnumerator CallLLM(
            IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null)
        {
            // MCP Server プロバイダー選択中は UnityAgent 側のチャット入力が禁止されるため、
            // 本来ここに到達してはいけない。安全のためユーザーへ案内を返して終了する。
            bool running = AgentMCPServer.Shared != null && AgentMCPServer.Shared.IsRunning;
            string port = running
                ? AgentMCPServer.Shared.Port.ToString()
                : AgentSettings.MCPServerPort.ToString();

            string message = running
                ? $"このモードでは UnityAgent 側の LLM は動作しません。外部エージェントから http://localhost:{port}/mcp に接続してツールを呼び出してください。"
                : $"MCP Server が起動していません。設定の MCP タブから UnityAgent MCP Server を有効化してください (ポート: {port})。";

            onDebugLog?.Invoke("[MCPServerProvider] no-op CallLLM");
            onError?.Invoke(message);
            yield break;
        }
    }
}
