using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Persistence;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Domain reload を跨いだチャットセッション復元のパーシャル。
    /// 主役は <see cref="ChatSessionPersistence"/> で、こちらは UnityAgentWindow 内部状態との橋渡し:
    /// - reload 直前: <see cref="SaveSnapshot"/> で <see cref="SessionSnapshot"/> を組み立てて保存
    /// - reload 後の OnEnable: <see cref="TryLoadSnapshot"/> で復元
    /// - ユーザーが Window を閉じた時: 残っているスナップショットを <see cref="ChatSessionPersistence.Clear"/> で削除
    ///
    /// reload 由来 OnDisable とユーザー閉じ OnDisable の判別は、static フラグ
    /// <see cref="_reloadInProgress"/> を <c>AssemblyReloadEvents.beforeAssemblyReload</c> で立てて行う。
    /// </summary>
    public partial class UnityAgentWindow
    {
        // beforeAssemblyReload で true、afterAssemblyReload で false。OnDisable はこの値を見て
        // 「reload 由来か」「ユーザーが閉じたか」を判別する。static は同 AppDomain 内で共有される。
        private static bool _reloadInProgress;

        /// <summary>自動リトライ無限ループ防止カウンタ。1 リクエスト 1 リトライまで。</summary>
        private int _autoRetryCount;
        private const int MaxAutoRetryPerSession = 1;

        // 復元直後だけ立つフラグ。CreateGUI 完了後に ResumeAfterReload を発火するために使う。
        private bool _pendingAutoResume;

        // ───────────────────────────────────────────────
        //  Wire-in (called from OnEnable / OnDisable)
        // ───────────────────────────────────────────────

        private void RegisterPersistenceHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += HandleAfterAssemblyReload;
        }

        private void UnregisterPersistenceHooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= HandleAfterAssemblyReload;
        }

        private void HandleBeforeAssemblyReload()
        {
            _reloadInProgress = true;
            try
            {
                SaveSnapshot();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] SaveSnapshot in beforeAssemblyReload failed: {ex.Message}");
            }
        }

        private void HandleAfterAssemblyReload()
        {
            _reloadInProgress = false;
        }

        // ───────────────────────────────────────────────
        //  Save
        // ───────────────────────────────────────────────

        private void SaveSnapshot()
        {
            var snap = new SessionSnapshot
            {
                version = 1,
                wasProcessing = _agent != null && _agent.IsProcessing,
                providerType = (int)_providerType,
                modelName = _configs != null && _configs.ContainsKey(_providerType)
                    ? _configs[_providerType].ModelName ?? ""
                    : "",
                userQuery = _userQuery ?? "",
                currentToolStatus = _currentToolStatus ?? "",
                showHistory = _showHistory,
                recentQueries = _recentQueries != null ? _recentQueries.ToArray() : Array.Empty<string>(),
            };

            // チャット履歴 (UI 表示)
            snap.chatHistory = SerializeChatHistory(_chatHistory);

            // LLM 履歴 (会話コンテキスト)
            if (_agent != null)
            {
                snap.llmHistory = SerializeLlmHistory(_agent.GetHistory());
                snap.sessionTotalTokens = _agent.SessionTotalTokens;
                snap.sessionInputTokens = _agent.SessionInputTokens;
                snap.sessionOutputTokens = _agent.SessionOutputTokens;
                snap.lastPromptTokens = _agent.LastPromptTokens;
                snap.sessionUndoCount = _agent.SessionUndoCount;
            }

            // 添付ファイル
            snap.pendingAttachment = SerializePendingAttachment();

            ChatSessionPersistence.Save(snap);
        }

        private static ChatEntrySnapshot[] SerializeChatHistory(List<ChatEntry> history)
        {
            if (history == null || history.Count == 0) return Array.Empty<ChatEntrySnapshot>();

            var arr = new ChatEntrySnapshot[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                var e = history[i];
                arr[i] = new ChatEntrySnapshot
                {
                    type = (int)e.type,
                    text = e.text ?? "",
                    thinkingText = e.thinkingText ?? "",
                    thinkingFoldout = e.thinkingFoldout,
                    timestamp = e.timestamp.ToUniversalTime().ToString("o"),
                    choiceOptions = e.choiceOptions ?? Array.Empty<string>(),
                    choiceImportance = e.choiceImportance ?? "info",
                    choiceSelectedIndex = e.choiceSelectedIndex,
                    isToolConfirm = e.isToolConfirm,
                    isBatchToolConfirm = e.isBatchToolConfirm,
                    batchItems = SerializeBatchItems(e.batchItems),
                    batchResolved = e.batchResolved,
                    isClipboard = e.isClipboard,
                    debugLogs = e.debugLogs != null ? e.debugLogs.ToArray() : Array.Empty<string>(),
                    debugFoldout = e.debugFoldout,
                    requestDurationTicks = e.requestDuration.HasValue ? e.requestDuration.Value.Ticks : -1,
                    imagePreview = ImageDownscaler.Encode(e.imagePreview),
                    results = SerializeResults(e.results),
                };
            }
            return arr;
        }

        private static BatchToolItemSnapshot[] SerializeBatchItems(List<BatchToolItem> items)
        {
            if (items == null || items.Count == 0) return Array.Empty<BatchToolItemSnapshot>();
            var arr = new BatchToolItemSnapshot[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                arr[i] = new BatchToolItemSnapshot
                {
                    toolName = items[i].toolName ?? "",
                    description = items[i].description ?? "",
                    parameters = items[i].parameters ?? "",
                    approved = items[i].approved,
                };
            }
            return arr;
        }

        private static ResultItemSnapshot[] SerializeResults(List<ResultItem> results)
        {
            if (results == null || results.Count == 0) return Array.Empty<ResultItemSnapshot>();
            var arr = new ResultItemSnapshot[results.Count];
            for (int i = 0; i < results.Count; i++)
            {
                arr[i] = new ResultItemSnapshot
                {
                    displayName = results[i].displayName ?? "",
                    typeName = results[i].typeName ?? "",
                    reference = results[i].reference ?? "",
                    isAsset = results[i].isAsset,
                };
            }
            return arr;
        }

        private static LlmMessageSnapshot[] SerializeLlmHistory(IReadOnlyList<Message> history)
        {
            if (history == null || history.Count == 0) return Array.Empty<LlmMessageSnapshot>();
            var arr = new LlmMessageSnapshot[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                var m = history[i];
                int partCount = m.parts != null ? m.parts.Length : 0;
                var parts = new LlmPartSnapshot[partCount];
                for (int p = 0; p < partCount; p++)
                {
                    var src = m.parts[p];
                    parts[p] = new LlmPartSnapshot
                    {
                        text = src.text ?? "",
                        imageBase64 = src.imageBytes != null && src.imageBytes.Length > 0
                            ? Convert.ToBase64String(src.imageBytes)
                            : "",
                        imageMimeType = src.imageMimeType ?? "",
                    };
                }
                arr[i] = new LlmMessageSnapshot
                {
                    role = m.role ?? "",
                    parts = parts,
                };
            }
            return arr;
        }

        private PendingAttachmentSnapshot SerializePendingAttachment()
        {
            var snap = new PendingAttachmentSnapshot();
            if (_pendingAttachmentBytes == null || _pendingAttachmentBytes.Length == 0) return snap;

            if (_pendingAttachmentBytes.Length > ChatSessionPersistence.MaxAttachmentBytes)
            {
                Debug.LogWarning(
                    $"[UnityAgent] Pending attachment ({_pendingAttachmentBytes.Length} bytes) exceeds " +
                    $"persistence limit ({ChatSessionPersistence.MaxAttachmentBytes}). Skipping attachment persistence.");
                return snap;
            }

            snap.filename = _pendingAttachmentFilename ?? "";
            snap.mimeType = _pendingAttachmentMimeType ?? "";
            snap.bytesBase64 = Convert.ToBase64String(_pendingAttachmentBytes);
            snap.preview = ImageDownscaler.Encode(_pendingAttachmentPreview);
            return snap;
        }

        // ───────────────────────────────────────────────
        //  Load
        // ───────────────────────────────────────────────

        /// <summary>
        /// snapshot が存在すれば復元する。OnEnable から呼ばれる。CreateGUI より前に
        /// _chatHistory を埋めておけば、CreateGUI の RebuildFromHistory が拾う。
        /// </summary>
        /// <returns>復元したか</returns>
        private bool TryLoadSnapshot()
        {
            var snap = ChatSessionPersistence.Load();
            if (snap == null) return false;

            try
            {
                _chatHistory = DeserializeChatHistory(snap.chatHistory);
                _userQuery = snap.userQuery ?? "";
                _currentToolStatus = snap.currentToolStatus ?? "";
                _showHistory = snap.showHistory;
                _recentQueries = snap.recentQueries != null
                    ? new List<string>(snap.recentQueries)
                    : new List<string>();

                // LLM 履歴を agent に戻す
                if (_agent != null && snap.llmHistory != null && snap.llmHistory.Length > 0)
                {
                    var msgs = DeserializeLlmHistory(snap.llmHistory);
                    _agent.RestoreHistory(msgs);
                    _agent.RestoreSessionStats(
                        snap.sessionTotalTokens,
                        snap.sessionInputTokens,
                        snap.sessionOutputTokens,
                        snap.lastPromptTokens,
                        snap.sessionUndoCount);
                }

                // 添付ファイル
                if (snap.pendingAttachment != null && snap.pendingAttachment.HasAttachment)
                {
                    try
                    {
                        _pendingAttachmentBytes = Convert.FromBase64String(snap.pendingAttachment.bytesBase64);
                        _pendingAttachmentMimeType = snap.pendingAttachment.mimeType;
                        _pendingAttachmentFilename = snap.pendingAttachment.filename;
                        if (snap.pendingAttachment.preview != null && snap.pendingAttachment.preview.HasImage)
                            _pendingAttachmentPreview = ImageDownscaler.Decode(snap.pendingAttachment.preview);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[UnityAgent] Failed to restore pending attachment: {ex.Message}");
                        _pendingAttachmentBytes = null;
                        _pendingAttachmentMimeType = null;
                        _pendingAttachmentFilename = null;
                        _pendingAttachmentPreview = null;
                    }
                }

                // 自動リトライ予約 (CreateGUI 完了後に発火)
                if (snap.wasProcessing)
                    _pendingAutoResume = true;

                AgentLogger.Info(LogTag.UI,
                    $"Session restored from snapshot: {_chatHistory.Count} chat entries, " +
                    $"{(_agent?.GetHistory()?.Count ?? 0)} llm messages, wasProcessing={snap.wasProcessing}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] TryLoadSnapshot failed: {ex.Message}");
                return false;
            }
        }

        private static List<ChatEntry> DeserializeChatHistory(ChatEntrySnapshot[] arr)
        {
            var list = new List<ChatEntry>();
            if (arr == null) return list;

            foreach (var s in arr)
            {
                if (s == null) continue;
                var entry = new ChatEntry
                {
                    type = (ChatEntry.EntryType)s.type,
                    text = s.text ?? "",
                    thinkingText = string.IsNullOrEmpty(s.thinkingText) ? null : s.thinkingText,
                    thinkingFoldout = s.thinkingFoldout,
                    choiceOptions = (s.choiceOptions != null && s.choiceOptions.Length > 0) ? s.choiceOptions : null,
                    choiceImportance = s.choiceImportance,
                    choiceSelectedIndex = s.choiceSelectedIndex,
                    isToolConfirm = s.isToolConfirm,
                    isBatchToolConfirm = s.isBatchToolConfirm,
                    batchResolved = s.batchResolved,
                    isClipboard = s.isClipboard,
                    debugFoldout = s.debugFoldout,
                };

                if (DateTime.TryParse(s.timestamp, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                    entry.timestamp = ts.ToLocalTime();
                else
                    entry.timestamp = DateTime.Now;

                if (s.requestDurationTicks >= 0)
                    entry.requestDuration = TimeSpan.FromTicks(s.requestDurationTicks);

                if (s.batchItems != null && s.batchItems.Length > 0)
                {
                    entry.batchItems = new List<BatchToolItem>();
                    foreach (var b in s.batchItems)
                    {
                        if (b == null) continue;
                        entry.batchItems.Add(new BatchToolItem
                        {
                            toolName = b.toolName,
                            description = b.description,
                            parameters = b.parameters,
                            approved = b.approved,
                        });
                    }
                }

                if (s.debugLogs != null && s.debugLogs.Length > 0)
                    entry.debugLogs = new List<string>(s.debugLogs);

                if (s.results != null && s.results.Length > 0)
                {
                    entry.results = new List<ResultItem>();
                    foreach (var r in s.results)
                    {
                        if (r == null) continue;
                        entry.results.Add(new ResultItem
                        {
                            displayName = r.displayName,
                            typeName = r.typeName,
                            reference = r.reference,
                            isAsset = r.isAsset,
                        });
                    }
                }

                if (s.imagePreview != null && s.imagePreview.HasImage)
                {
                    try
                    {
                        entry.imagePreviewBytes = Convert.FromBase64String(s.imagePreview.pngBase64);
                        entry.imagePreviewWidth = s.imagePreview.width;
                        entry.imagePreviewHeight = s.imagePreview.height;
                    }
                    catch
                    {
                        entry.imagePreviewBytes = null;
                    }
                }

                list.Add(entry);
            }
            return list;
        }

        private static List<Message> DeserializeLlmHistory(LlmMessageSnapshot[] arr)
        {
            var list = new List<Message>();
            if (arr == null) return list;

            foreach (var m in arr)
            {
                if (m == null) continue;
                int partCount = m.parts != null ? m.parts.Length : 0;
                var parts = new Part[partCount];
                for (int p = 0; p < partCount; p++)
                {
                    var src = m.parts[p];
                    var dst = new Part
                    {
                        text = src.text ?? "",
                        imageMimeType = string.IsNullOrEmpty(src.imageMimeType) ? null : src.imageMimeType,
                    };
                    if (!string.IsNullOrEmpty(src.imageBase64))
                    {
                        try { dst.imageBytes = Convert.FromBase64String(src.imageBase64); }
                        catch { dst.imageBytes = null; }
                    }
                    parts[p] = dst;
                }
                list.Add(new Message { role = m.role ?? "", parts = parts });
            }
            return list;
        }

        // ───────────────────────────────────────────────
        //  Auto-resume (called from CreateGUI after rebuild)
        // ───────────────────────────────────────────────

        /// <summary>
        /// CreateGUI の最後で呼ばれ、reload 復元時に進行中だった LLM 呼び出しを自動再発行する。
        /// 同セッション内では <see cref="MaxAutoRetryPerSession"/> 回までに制限。
        /// </summary>
        internal void TryAutoResumeAfterReload()
        {
            if (!_pendingAutoResume) return;
            _pendingAutoResume = false;

            if (_agent == null || !_agent.CanResume()) return;
            if (_autoRetryCount >= MaxAutoRetryPerSession)
            {
                AgentLogger.Warning(LogTag.UI, "Auto-retry limit reached; not resuming.");
                AppendInfoEntry(M("自動リトライ上限に達しました。手動で再送信してください。"));
                return;
            }
            _autoRetryCount++;

            AppendInfoEntry(M("(Domain reload を検出 — 自動的に処理を再開しています…)"));

            EditorApplication.delayCall += () =>
            {
                if (_agent == null) return;
                var routine = _agent.ResumeAfterReload(
                    onReplyReceived: (response, success) =>
                    {
                        OnAutoResumeReply(response, success);
                    },
                    onStatus: status => _currentToolStatus = status ?? "",
                    onDebugLog: log => AgentLogger.Debug(LogTag.Core, log),
                    onPartialResponse: partial => HandlePartialResponseFromResume(partial)
                );
                var handle = EditorCoroutineUtility.StartCoroutineOwnerless(routine);
                _agent.SetRootCoroutine(handle);
            };
        }

        private void OnAutoResumeReply(string response, bool success)
        {
            if (!success)
            {
                AppendErrorEntry(M("自動リトライに失敗しました。再送信してください。") + "\n" + (response ?? ""));
                return;
            }
            // 成功時は通常の応答ハンドラに任せる (HandleResponse 内で _streamingEntry が更新される)。
            // 本メソッドは特に追加処理をしない。
        }

        private void HandlePartialResponseFromResume(string partial)
        {
            // 通常経路と同じく SetStreamingEntry でポーリング更新する。
            // partial == null は新規ストリーミングセッション開始の合図。
            if (partial == null)
            {
                _streamingEntry = ChatEntry.CreateAgent("");
                _chatHistory.Add(_streamingEntry);
                _chatPanel?.SetStreamingEntry(_streamingEntry);
                _shouldScrollToBottom = true;
                return;
            }

            if (_streamingEntry == null)
            {
                _streamingEntry = ChatEntry.CreateAgent(partial);
                _chatHistory.Add(_streamingEntry);
                _chatPanel?.SetStreamingEntry(_streamingEntry);
            }
            else
            {
                _streamingEntry.text = partial;
                _streamingEntry.cachedRichText = null;
            }
            _shouldScrollToBottom = true;
        }

        private void AppendInfoEntry(string text)
        {
            var entry = ChatEntry.CreateInfo(text);
            _chatHistory.Add(entry);
            _chatPanel?.AppendEntry(entry);
            _shouldScrollToBottom = true;
        }

        private void AppendErrorEntry(string text)
        {
            var entry = ChatEntry.CreateError(text);
            _chatHistory.Add(entry);
            _chatPanel?.AppendEntry(entry);
            _shouldScrollToBottom = true;
        }
    }
}
