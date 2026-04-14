using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// プロンプトをクリップボードにコピーし、ユーザーが外部チャットサービスで
    /// 得た回答を手動で貼り付けることで動作する「手動入力」プロバイダー。
    /// API キー不要で、ChatGPT / Claude.ai / Gemini 等あらゆるサービスと連携できる。
    /// </summary>
    public class ClipboardProvider : ILLMProvider
    {
        public string ProviderName => "Clipboard (手動)";

        private volatile bool _aborted;

        public void Abort()
        {
            _aborted = true;
            ClipboardProviderState.Cancel();
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

            // Build the full prompt text formatted for any external chat service
            string promptText = BuildPromptText(history);

            // Copy to clipboard (must be called on main thread — coroutine runs on main thread)
            GUIUtility.systemCopyBuffer = promptText;
            onDebugLog?.Invoke($"[ClipboardProvider] Prompt copied to clipboard ({promptText.Length} chars)");

            // Set state and signal UI to show the paste panel
            ClipboardProviderState.Request(promptText);
            onStatus?.Invoke("__CLIPBOARD_WAITING__");

            // Yield until user submits (or cancels) in the UI
            while (!ClipboardProviderState.IsSubmitted && !_aborted)
                yield return null;

            if (_aborted)
            {
                ClipboardProviderState.Clear();
                yield break;
            }

            string response = ClipboardProviderState.Response;
            ClipboardProviderState.Clear();

            if (string.IsNullOrWhiteSpace(response))
            {
                onError?.Invoke("応答が入力されませんでした。");
                yield break;
            }

            onSuccess?.Invoke(response.Trim());
        }

        // ─── Prompt builder ───

        private static string BuildPromptText(IEnumerable<Message> history)
        {
            string systemPrompt = null;
            var turns = new List<(string role, string text)>();

            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemPrompt = m.parts[0].text;
                    continue;
                }
                if (m.parts == null || m.parts.Length == 0) continue;

                // Skip the initial "System initialized." model message
                if (m.role == "model" && turns.Count == 0)
                    continue;

                string role = m.role == "model" ? "Assistant" : "User";
                var textSb = new StringBuilder();
                foreach (var part in m.parts)
                    if (!string.IsNullOrEmpty(part.text))
                        textSb.Append(part.text);

                string text = textSb.ToString();
                if (!string.IsNullOrEmpty(text))
                    turns.Add((role, text));
            }

            var sb = new StringBuilder();
            sb.AppendLine("# UNITY EDITOR AI AGENT — 手動入力モード");
            sb.AppendLine();
            sb.AppendLine("以下のシステム指示と会話履歴を読み、**アシスタントとして1回だけ応答してください**。");
            sb.AppendLine("ツール呼び出しは必ず `[MethodName(arg1, arg2)]` の形式でテキストとして出力してください。");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.AppendLine("---");
                sb.AppendLine("## システム指示");
                sb.AppendLine();
                sb.AppendLine(systemPrompt);
                sb.AppendLine();
            }

            if (turns.Count > 1)
            {
                sb.AppendLine("---");
                sb.AppendLine("## 会話履歴 (参照用 — 続きを書かないこと)");
                sb.AppendLine();
                for (int i = 0; i < turns.Count - 1; i++)
                {
                    sb.AppendLine($"**{turns[i].role}:** {turns[i].text}");
                    sb.AppendLine();
                }
            }

            if (turns.Count > 0)
            {
                sb.AppendLine("---");
                sb.AppendLine("## 現在のリクエスト (これだけに応答してください)");
                sb.AppendLine();
                sb.AppendLine(turns[turns.Count - 1].text);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine("上記リクエストにアシスタントとして1回だけ応答してください。");
            }

            return sb.ToString();
        }
    }
}
