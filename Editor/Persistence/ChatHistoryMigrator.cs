using System;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Persistence
{
    /// <summary>
    /// ChatEntrySnapshot スキーマのマイグレーション。
    ///
    /// v1 → v2: Phase 1 で追加された EntryType.ToolCall を既存 v1 ファイルに後方適用する。
    /// v1 は「Executing Tool: ...」Info entry と「[Tool Result] ...」Info entry のペアで
    /// ツール実行を記録していたため、このペアを検出して ToolCall entry にまとめる。
    /// マッチしなかった Info はそのまま残す (データ損失なし)。
    /// </summary>
    internal static class ChatHistoryMigrator
    {
        const string ExecPrefix = "Executing Tool:";
        const string ResultPrefix = "[Tool Result] ";
        const string ErrorPrefix = "[Tool Error] ";

        public static void UpgradeV1ToV2(SessionSnapshot snap)
        {
            if (snap?.chatHistory == null || snap.chatHistory.Length == 0) return;

            var src = snap.chatHistory;
            var result = new List<ChatEntrySnapshot>(src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                var e = src[i];
                if (e == null) continue;

                // v1 uses EntryType.Info (2) for both Executing/Result pairs.
                bool isInfo = e.type == (int)ChatEntry.EntryType.Info;
                if (isInfo && !string.IsNullOrEmpty(e.text) && e.text.StartsWith(ExecPrefix))
                {
                    // Look ahead for the matching [Tool Result] / [Tool Error] Info entry.
                    ChatEntrySnapshot resultEntry = null;
                    bool isError = false;
                    int consumedUpTo = i;
                    for (int j = i + 1; j < src.Length; j++)
                    {
                        var next = src[j];
                        if (next == null) continue;
                        if (next.type != (int)ChatEntry.EntryType.Info) break;
                        if (string.IsNullOrEmpty(next.text)) continue;
                        if (next.text.StartsWith(ResultPrefix))
                        {
                            resultEntry = next;
                            consumedUpTo = j;
                            break;
                        }
                        if (next.text.StartsWith(ErrorPrefix))
                        {
                            resultEntry = next;
                            isError = true;
                            consumedUpTo = j;
                            break;
                        }
                    }

                    if (resultEntry != null)
                    {
                        string rest = e.text.Substring(ExecPrefix.Length).Trim();
                        string toolName = rest;
                        string argsRaw = "";
                        int paren = rest.IndexOf('(');
                        if (paren >= 0)
                        {
                            toolName = rest.Substring(0, paren);
                            argsRaw = rest.Substring(paren);
                        }

                        string resultText = resultEntry.text ?? "";
                        if (isError && resultText.StartsWith(ErrorPrefix))
                            resultText = resultText.Substring(ErrorPrefix.Length);
                        else if (resultText.StartsWith(ResultPrefix))
                            resultText = resultText.Substring(ResultPrefix.Length);

                        var merged = new ChatEntrySnapshot
                        {
                            type = (int)ChatEntry.EntryType.ToolCall,
                            timestamp = e.timestamp ?? "",
                            toolCallId = "migrated-" + i,
                            toolName = toolName,
                            toolArgsRaw = argsRaw,
                            toolResult = resultText,
                            toolStatus = isError
                                ? (int)ToolCallStatus.Error
                                : (int)ToolCallStatus.Success,
                            toolDurationMs = 0,
                            toolCategory = "",
                        };

                        // Preserve image preview if present on either side
                        if (e.imagePreview != null && e.imagePreview.HasImage)
                            merged.imagePreview = e.imagePreview;
                        else if (resultEntry.imagePreview != null && resultEntry.imagePreview.HasImage)
                            merged.imagePreview = resultEntry.imagePreview;

                        // Preserve result items from the v1 Info entry
                        if (resultEntry.results != null && resultEntry.results.Length > 0)
                            merged.results = resultEntry.results;

                        result.Add(merged);
                        i = consumedUpTo; // skip the consumed pair
                        continue;
                    }
                }

                // Default: pass-through
                result.Add(e);
            }

            snap.chatHistory = result.ToArray();
        }
    }
}
