using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers.BrowserBridge;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// Chrome 拡張機能 + WebSocket ブリッジ経由で Web ブラウザ上の AI チャットと連携するプロバイダー。
    /// API キー不要で Gemini / ChatGPT / Copilot を使用できる。
    /// </summary>
    public class BrowserBridgeProvider : ILLMProvider
    {
        public string ProviderName => "Web Browser (Gemini / ChatGPT / Copilot)";

        private readonly int _port;
        private string _currentRequestId;
        private volatile bool _aborted;

        public BrowserBridgeProvider(int port = 6090)
        {
            _port = port;
            BrowserBridgeServerManager.EnsureRunning(port);
        }

        public void Abort()
        {
            _aborted = true;
            if (_currentRequestId != null && BrowserBridgeServerManager.Server != null)
            {
                string msg = BrowserBridgeProtocol.BuildAbortMessage(_currentRequestId);
                BrowserBridgeServerManager.Server.Send(msg);
            }
        }

        public IEnumerator CallLLM(
            IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null,
            Action<ChatStreamEvent> onStreamEvent = null)
        {
            _aborted = false;

            // Ensure server is running
            BrowserBridgeServerManager.EnsureRunning(_port);
            onDebugLog?.Invoke($"[BrowserBridge] Server running on port {_port}");

            // Wait for extension connection (max 60 seconds)
            if (!BrowserBridgeState.IsConnected)
            {
                onStatus?.Invoke("__BROWSER_BRIDGE_WAITING__");
                onDebugLog?.Invoke("[BrowserBridge] Waiting for Chrome extension connection...");

                float timeout = 60f;
                float elapsed = 0f;
                while (!BrowserBridgeState.IsConnected && elapsed < timeout && !_aborted)
                {
                    elapsed += 0.1f;
                    yield return null;
                }

                if (_aborted)
                {
                    onError?.Invoke("中断されました。");
                    yield break;
                }

                if (!BrowserBridgeState.IsConnected)
                {
                    onError?.Invoke("Chrome 拡張機能が接続されていません。gemini.google.com / chatgpt.com / copilot.microsoft.com を開いて拡張機能を有効にしてください。");
                    yield break;
                }
            }

            // Build prompt text — 新規セッション判定は履歴にアシスタント応答があるかで決定
            bool newSession = !HasPriorAssistantResponse(history);
            string promptText = newSession
                ? BuildFullPromptText(history)
                : BuildFollowUpText(history);
            _currentRequestId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string requestId = _currentRequestId;

            onDebugLog?.Invoke($"[BrowserBridge] Sending prompt ({promptText.Length} chars, newSession={newSession}), id={requestId}");

            // Set state and send
            BrowserBridgeState.Request(requestId);
            string msg = BrowserBridgeProtocol.BuildPromptMessage(requestId, promptText, newSession);
            BrowserBridgeServerManager.Server.Send(msg);

            onStatus?.Invoke("Web ブラウザで応答を生成中...");

            // Poll for response
            string lastPartial = "";
            while (!BrowserBridgeState.IsCompleted && !_aborted)
            {
                string partial = BrowserBridgeState.PartialResponse;
                if (partial != lastPartial)
                {
                    lastPartial = partial;
                    onPartialResponse?.Invoke(partial);
                }

                // Check if extension disconnected
                if (!BrowserBridgeState.IsConnected)
                {
                    onError?.Invoke("Chrome 拡張機能との接続が切断されました。");
                    yield break;
                }

                yield return null;
            }

            if (_aborted)
            {
                onError?.Invoke("中断されました。");
                yield break;
            }

            if (BrowserBridgeState.HasError)
            {
                onError?.Invoke(BrowserBridgeState.ErrorMessage ?? "Unknown error from browser bridge");
                yield break;
            }

            string response = BrowserBridgeState.FinalResponse;
            if (string.IsNullOrWhiteSpace(response))
            {
                onError?.Invoke("AI からの応答が空でした。");
                yield break;
            }

            onSuccess?.Invoke(response.Trim());
        }

        // ─── Prompt builders ───

        /// <summary>初回: システム指示 + 全履歴 + 現在のリクエスト</summary>
        private static string BuildFullPromptText(IEnumerable<Message> history)
        {
            string systemPrompt = null;
            var turns = new List<(string role, string text)>();
            CollectTurns(history, out systemPrompt, turns);

            var sb = new StringBuilder();
            sb.AppendLine("# UNITY EDITOR AI AGENT — Browser Bridge モード\n");
            sb.AppendLine("以下のシステム指示と会話履歴を読み、**アシスタントとして1回だけ応答してください**。");
            sb.AppendLine("ツール呼び出しは必ず `[MethodName(arg1, arg2)]` の形式でテキストとして出力してください。\n");
            sb.AppendLine("**重要なルール:**");
            sb.AppendLine("- ユーザーが明示的に依頼していない操作のツールを呼ばないこと。");
            sb.AppendLine("- 挨拶や質問への返答だけで十分な場合は、ツールを一切呼ばずテキストだけで応答すること。");
            sb.AppendLine("- 1回の応答でツール呼び出しは最大1つまで。複数呼ばないこと。\n");

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.AppendLine("---\n## システム指示\n");
                sb.AppendLine(systemPrompt);
                sb.AppendLine();
            }

            if (turns.Count > 1)
            {
                sb.AppendLine("---\n## 会話履歴 (参照用 — 続きを書かないこと)\n");
                for (int i = 0; i < turns.Count - 1; i++)
                {
                    sb.AppendLine($"**{turns[i].role}:** {turns[i].text}\n");
                }
            }

            if (turns.Count > 0)
            {
                sb.AppendLine("---\n## 現在のリクエスト (これだけに応答してください)\n");
                sb.AppendLine(turns[turns.Count - 1].text);
                sb.AppendLine("\n---");
                sb.AppendLine("上記リクエストにアシスタントとして1回だけ応答してください。");
            }

            return sb.ToString();
        }

        /// <summary>2回目以降: 最新メッセージのみ（チャットに既にコンテキストがある）</summary>
        private static string BuildFollowUpText(IEnumerable<Message> history)
        {
            string systemPrompt = null;
            var turns = new List<(string role, string text)>();
            CollectTurns(history, out systemPrompt, turns);

            if (turns.Count == 0)
                return "(応答なし)";

            var sb = new StringBuilder();
            sb.AppendLine(turns[turns.Count - 1].text);
            sb.AppendLine("\n---");
            sb.AppendLine("上記はツール実行結果です。ユーザーの元の依頼が完了していれば、ツールを呼ばずに結果を報告してください。追加のツール呼び出しが必要な場合のみ1つだけ呼んでください。");
            return sb.ToString();
        }

        /// <summary>履歴にユーザーメッセージ後のアシスタント応答があるか判定</summary>
        private static bool HasPriorAssistantResponse(IEnumerable<Message> history)
        {
            bool seenUser = false;
            foreach (var m in history)
            {
                if (m.role == "user" && m.parts?.Length > 0) seenUser = true;
                else if (m.role == "model" && seenUser && m.parts?.Length > 0)
                {
                    foreach (var p in m.parts)
                        if (!string.IsNullOrEmpty(p.text)) return true;
                }
            }
            return false;
        }

        private static void CollectTurns(IEnumerable<Message> history, out string systemPrompt, List<(string role, string text)> turns)
        {
            systemPrompt = null;
            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemPrompt = m.parts[0].text;
                    continue;
                }
                if (m.parts == null || m.parts.Length == 0) continue;
                if (m.role == "model" && turns.Count == 0) continue;

                string role = m.role == "model" ? "Assistant" : "User";
                var textSb = new StringBuilder();
                foreach (var part in m.parts)
                    if (!string.IsNullOrEmpty(part.text))
                        textSb.Append(part.text);

                string text = textSb.ToString();
                if (!string.IsNullOrEmpty(text))
                    turns.Add((role, text));
            }
        }
    }
}
