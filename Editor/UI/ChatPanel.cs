using System;
using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// チャットメッセージのスクロールエリア。
    /// ChatEntry を ChatEntryView に変換して表示する。
    /// </summary>
    internal class ChatPanel : VisualElement
    {
        readonly ScrollView _scrollView;
        readonly VisualElement _content;
        readonly MD3Theme _theme;
        IVisualElementScheduledItem _streamingPoll;
        ChatEntryView _streamingView;
        ChatEntry _streamingEntry;
        int _streamingTextLen;
        int _streamingThinkingLen;

        // Info グループ折りたたみ状態
        readonly Dictionary<int, bool> _toolGroupFoldouts = new Dictionary<int, bool>();

        // ToolCall view index (callId → view)
        readonly Dictionary<string, ChatEntryView> _toolCallViews = new Dictionary<string, ChatEntryView>();
        readonly Dictionary<string, ChatEntry> _toolCallEntries = new Dictionary<string, ChatEntry>();

        // Running ToolCall card duration ticker
        IVisualElementScheduledItem _toolCallTicker;

        // Thinking indicator rotating label
        IVisualElementScheduledItem _thinkingLabelRotator;
        int _thinkingLabelIndex;

        // CLI Activity Panel (live thinking / tool display)
        VisualElement _activityPanel;
        Label _activityThinkingLabel;
        Label _activityStatusLabel;
        VisualElement _activityToolList;

        // Callbacks
        public Action<int> OnEditAndResend;
        public Action<string, int> OnChoiceSelected;
        public Action<int, bool> OnBatchConfirmItem;
        public Action OnBatchConfirmAll;
        public Action OnBatchConfirmDeny;
        public Action<string> OnClipboardSubmit;
        public Action OnCopyText;

        public ChatPanel(MD3Theme theme)
        {
            _theme = theme;
            style.flexGrow = 1;

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Add(_scrollView);

            _content = _scrollView.contentContainer;
            _content.style.paddingLeft = 16;
            _content.style.paddingRight = 16;
            _content.style.paddingBottom = 4;
        }

        /// <summary>チャット履歴から全エントリを再構築する。</summary>
        public void RebuildFromHistory(List<ChatEntry> history)
        {
            StopStreaming();
            _content.Clear();
            _toolGroupFoldouts.Clear();

            if (history == null || history.Count == 0) return;

            // Info グループの最後を特定
            int lastGroupStart = FindLastInfoGroupStart(history);

            for (int i = 0; i < history.Count;)
            {
                if (history[i].type == ChatEntry.EntryType.Info)
                {
                    int groupStart = i;
                    while (i < history.Count && history[i].type == ChatEntry.EntryType.Info)
                        i++;
                    int groupCount = i - groupStart;

                    if (groupCount >= 2)
                    {
                        bool expanded = (groupStart == lastGroupStart);
                        _toolGroupFoldouts[groupStart] = expanded;
                        AddInfoGroup(history, groupStart, groupCount, expanded);
                    }
                    else
                    {
                        AddEntryView(history[groupStart]);
                    }
                }
                else
                {
                    AddEntryView(history[i]);
                    i++;
                }
            }
        }

        /// <summary>新しいエントリを末尾に追加する。</summary>
        public void AppendEntry(ChatEntry entry)
        {
            AddEntryView(entry);
        }

        /// <summary>
        /// 新しい ToolCall entry を履歴に追加してカード表示する。Phase 1 では Window が生成した
        /// callId を使って後続の CompleteToolCall と紐付ける。
        /// </summary>
        public ChatEntry BeginToolCall(string callId, string toolName, string argsRaw, List<ChatEntry> history)
        {
            var entry = ChatEntry.CreateToolCall(callId, toolName, argsRaw);
            history?.Add(entry);

            var view = ChatEntryView.Create(entry, _theme);
            _content.Add(view);

            if (!string.IsNullOrEmpty(callId))
            {
                _toolCallViews[callId] = view;
                _toolCallEntries[callId] = entry;
            }

            EnsureToolCallTicker();
            return entry;
        }

        /// <summary>実行完了した ToolCall を in-place で Success/Error に更新する。</summary>
        public void CompleteToolCall(string callId, string result, bool isError)
        {
            if (string.IsNullOrEmpty(callId)) return;

            if (!_toolCallEntries.TryGetValue(callId, out var entry)) return;
            entry.CompleteToolCall(result, isError);

            if (_toolCallViews.TryGetValue(callId, out var view) && view != null)
                view.RefreshToolCall();

            _toolCallEntries.Remove(callId);
            _toolCallViews.Remove(callId);

            if (_toolCallEntries.Count == 0)
                StopToolCallTicker();
        }

        void EnsureToolCallTicker()
        {
            if (_toolCallTicker != null) return;
            _toolCallTicker = schedule.Execute(() =>
            {
                if (_toolCallEntries.Count == 0)
                {
                    StopToolCallTicker();
                    return;
                }
                // Refresh duration label on running cards
                foreach (var kv in _toolCallEntries)
                {
                    if (_toolCallViews.TryGetValue(kv.Key, out var view) && view != null)
                        view.RefreshToolCall();
                }
            }).Every(200);
        }

        void StopToolCallTicker()
        {
            _toolCallTicker?.Pause();
            _toolCallTicker = null;
        }

        /// <summary>ストリーミング中のエントリを設定し、ポーリングを開始する。</summary>
        public void SetStreamingEntry(ChatEntry entry)
        {
            StopStreaming();
            _streamingEntry = entry;
            _streamingTextLen = entry.text?.Length ?? 0;
            _streamingThinkingLen = entry.thinkingText?.Length ?? 0;

            var view = ChatEntryView.Create(entry, _theme);
            _content.Add(view);
            _streamingView = view;

            _streamingPoll = schedule.Execute(() =>
            {
                if (_streamingEntry == null)
                {
                    StopStreaming();
                    return;
                }
                int newLen = _streamingEntry.text?.Length ?? 0;
                int newThinkingLen = _streamingEntry.thinkingText?.Length ?? 0;
                if (newLen != _streamingTextLen || newThinkingLen != _streamingThinkingLen)
                {
                    _streamingTextLen = newLen;
                    _streamingThinkingLen = newThinkingLen;
                    _streamingView?.UpdateContent(_streamingEntry);
                }
            }).Every(50);
        }

        /// <summary>
        /// OnStreamEvent(Thinking) から即時更新を促すシグナル。
        /// ポーリングも並行で動いているが、ユーザー応答性のため即時更新したいときに呼ぶ。
        /// </summary>
        public void NotifyStreamingThinkingUpdated()
        {
            if (_streamingEntry == null || _streamingView == null) return;
            _streamingView.UpdateContent(_streamingEntry);
        }

        /// <summary>ストリーミング完了。ポーリングを停止し、最終テキストで更新。</summary>
        public void FinalizeStreaming(ChatEntry entry)
        {
            if (_streamingView != null && entry != null)
                _streamingView.UpdateContent(entry);
            StopStreaming();
        }

        /// <summary>Thinking インジケータを表示/非表示する。</summary>
        public void ShowThinkingIndicator(bool show)
        {
            if (show)
            {
                var indicator = _content.Q<VisualElement>("thinking-indicator");
                if (indicator == null)
                {
                    indicator = CreateThinkingIndicator();
                    indicator.name = "thinking-indicator";
                    _content.Add(indicator);
                }
                StartThinkingLabelRotation(indicator);
            }
            else
            {
                StopThinkingLabelRotation();
                var indicator = _content.Q<VisualElement>("thinking-indicator");
                indicator?.RemoveFromHierarchy();
            }
        }

        static readonly string[] ThinkingLabels =
        {
            "考え中...",
            "接続中...",
            "応答を待っています...",
            "処理しています...",
        };

        void StartThinkingLabelRotation(VisualElement indicator)
        {
            StopThinkingLabelRotation();
            _thinkingLabelIndex = 0;
            var label = indicator?.Q<Label>("thinking-label");
            if (label == null) return;

            _thinkingLabelRotator = schedule.Execute(() =>
            {
                _thinkingLabelIndex = (_thinkingLabelIndex + 1) % ThinkingLabels.Length;
                label.text = M(ThinkingLabels[_thinkingLabelIndex]);
            }).Every(1500);
        }

        void StopThinkingLabelRotation()
        {
            _thinkingLabelRotator?.Pause();
            _thinkingLabelRotator = null;
        }

        /// <summary>ツール実行プログレスバーを更新。</summary>
        public void UpdateToolProgress(bool active, string toolName, float progress)
        {
            var bar = _content.Q<VisualElement>("tool-progress");
            if (active)
            {
                if (bar == null)
                {
                    bar = CreateToolProgressBar();
                    bar.name = "tool-progress";
                    _content.Add(bar);
                }
                var label = bar.Q<Label>("tool-progress-label");
                if (label != null) label.text = toolName ?? "";
                var progressBar = bar.Q<MD3LinearProgress>("tool-progress-bar");
                if (progressBar != null) progressBar.Value = progress;
            }
            else
            {
                bar?.RemoveFromHierarchy();
            }
        }

        /// <summary>スクロールを最下部へ。</summary>
        public void ScrollToBottom()
        {
            schedule.Execute(() =>
            {
                _scrollView.scrollOffset = new Vector2(0, _scrollView.contentContainer.layout.height);
            });
        }

        /// <summary>全エントリをクリアする。</summary>
        public new void Clear()
        {
            StopStreaming();
            StopToolCallTicker();
            StopThinkingLabelRotation();
            _content.Clear();
            _toolGroupFoldouts.Clear();
            _toolCallViews.Clear();
            _toolCallEntries.Clear();
        }

        // ── Private ──

        void StopStreaming()
        {
            _streamingPoll?.Pause();
            _streamingPoll = null;
            _streamingView = null;
            _streamingEntry = null;
        }

        void AddEntryView(ChatEntry entry)
        {
            var view = ChatEntryView.Create(entry, _theme);
            WireCallbacks(view, entry);
            _content.Add(view);
        }

        void WireCallbacks(ChatEntryView view, ChatEntry entry)
        {
            view.OnEdit = () => OnEditAndResend?.Invoke(view.EntryIndex);
            view.OnCopy = text => EditorGUIUtility.systemCopyBuffer = text;
        }

        void AddInfoGroup(List<ChatEntry> history, int startIdx, int count, bool expanded)
        {
            var foldout = new MD3Foldout(string.Format(M("\u25B6 ツール実行 ({0}件)"), count), expanded);
            foldout.style.marginLeft = 20;

            for (int j = startIdx; j < startIdx + count; j++)
            {
                var view = ChatEntryView.Create(history[j], _theme);
                foldout.Content.Add(view);
            }

            foldout.changed += (bool exp) =>
            {
                _toolGroupFoldouts[startIdx] = exp;
            };

            _content.Add(foldout);
        }

        static int FindLastInfoGroupStart(List<ChatEntry> history)
        {
            int lastGroupStart = -1;
            for (int i = 0; i < history.Count;)
            {
                if (history[i].type == ChatEntry.EntryType.Info)
                {
                    int gs = i;
                    while (i < history.Count && history[i].type == ChatEntry.EntryType.Info) i++;
                    if (i - gs >= 2) lastGroupStart = gs;
                }
                else i++;
            }
            return lastGroupStart;
        }

        // ── CLI Activity Panel ──

        /// <summary>CLI プロバイダーの内部状態 (thinking/tool) をライブ表示する。</summary>
        public void UpdateActivity(string thinkingPreview, string status, string toolName)
        {
            EnsureActivityPanel();

            if (!string.IsNullOrEmpty(thinkingPreview))
            {
                _activityThinkingLabel.text = thinkingPreview;
                _activityThinkingLabel.style.display = DisplayStyle.Flex;
            }

            if (!string.IsNullOrEmpty(status))
                _activityStatusLabel.text = status;

            if (!string.IsNullOrEmpty(toolName))
            {
                // Append tool entry to tool list (avoid duplicates)
                string tagName = "tool-" + toolName.GetHashCode();
                if (_activityToolList.Q(tagName) == null)
                {
                    var row = new MD3Row(gap: 4f);
                    row.name = tagName;
                    row.style.alignItems = Align.Center;

                    var dot = new VisualElement();
                    dot.style.width = 6;
                    dot.style.height = 6;
                    dot.style.borderTopLeftRadius = 3;
                    dot.style.borderTopRightRadius = 3;
                    dot.style.borderBottomLeftRadius = 3;
                    dot.style.borderBottomRightRadius = 3;
                    dot.style.backgroundColor = _theme.Primary;
                    dot.style.flexShrink = 0;
                    row.Add(dot);

                    var label = new Label(toolName);
                    label.style.fontSize = 11;
                    label.style.color = _theme.OnSurfaceVariant;
                    row.Add(label);

                    _activityToolList.Add(row);
                    _activityToolList.style.display = DisplayStyle.Flex;
                }
            }

            _activityPanel.style.display = DisplayStyle.Flex;
        }

        /// <summary>アクティビティパネルを削除する。</summary>
        public void ClearActivity()
        {
            _activityPanel?.RemoveFromHierarchy();
            _activityPanel = null;
            _activityThinkingLabel = null;
            _activityStatusLabel = null;
            _activityToolList = null;
        }

        void EnsureActivityPanel()
        {
            if (_activityPanel != null) return;

            _activityPanel = new VisualElement();
            _activityPanel.style.marginTop = 8;
            _activityPanel.style.marginBottom = 8;
            _activityPanel.style.paddingLeft = 12;
            _activityPanel.style.paddingRight = 12;
            _activityPanel.style.paddingTop = 10;
            _activityPanel.style.paddingBottom = 10;
            _activityPanel.style.borderTopLeftRadius = 12;
            _activityPanel.style.borderTopRightRadius = 12;
            _activityPanel.style.borderBottomLeftRadius = 12;
            _activityPanel.style.borderBottomRightRadius = 12;
            _activityPanel.style.backgroundColor =
                new Color(_theme.SurfaceVariant.r, _theme.SurfaceVariant.g, _theme.SurfaceVariant.b, 0.5f);
            _activityPanel.style.borderLeftWidth = 3;
            _activityPanel.style.borderLeftColor = _theme.Primary;

            // Expressive loading + status row
            var headerRow = new MD3Row(gap: 8f);
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;

            var loading = new MD3Loading(MD3LoadingStyle.Expressive, 32f);
            headerRow.Add(loading);

            _activityStatusLabel = new Label("...");
            _activityStatusLabel.style.fontSize = 12;
            _activityStatusLabel.style.color = _theme.OnSurfaceVariant;
            headerRow.Add(_activityStatusLabel);

            _activityPanel.Add(headerRow);

            // Thinking label (preview)
            _activityThinkingLabel = new Label();
            _activityThinkingLabel.style.fontSize = 11;
            _activityThinkingLabel.style.color = _theme.OnSurfaceVariant;
            _activityThinkingLabel.style.opacity = 0.7f;
            _activityThinkingLabel.style.whiteSpace = WhiteSpace.Normal;
            _activityThinkingLabel.style.marginBottom = 4;
            _activityThinkingLabel.style.display = DisplayStyle.None;
            _activityPanel.Add(_activityThinkingLabel);

            // Tool list
            _activityToolList = new VisualElement();
            _activityToolList.style.display = DisplayStyle.None;
            _activityToolList.style.marginTop = 4;
            _activityPanel.Add(_activityToolList);

            _content.Add(_activityPanel);
        }

        VisualElement CreateThinkingIndicator()
        {
            var container = new MD3Column(gap: 8f);
            container.style.alignItems = Align.Center;
            container.style.paddingTop = 24;
            container.style.paddingBottom = 24;

            var loading = new MD3Loading(MD3LoadingStyle.Expressive, 128f);
            container.Add(loading);

            var label = new Label(M("考え中..."));
            label.name = "thinking-label";
            label.style.fontSize = 14;
            label.style.color = _theme.OnSurfaceVariant;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            container.Add(label);

            return container;
        }

        VisualElement CreateToolProgressBar()
        {
            var container = new MD3Column(gap: 4f);
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;

            var label = new Label { name = "tool-progress-label" };
            label.style.fontSize = 12;
            label.style.color = _theme.OnSurfaceVariant;
            container.Add(label);

            var bar = new MD3LinearProgress(0f);
            bar.name = "tool-progress-bar";
            container.Add(bar);

            return container;
        }
    }
}
