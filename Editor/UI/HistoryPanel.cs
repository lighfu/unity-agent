using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// チャット履歴一覧パネル。ListView による仮想化で、件数が多くても
    /// 表示中の行しか生成しないためフリーズしない。ヘッダー（タイトル・件数）は
    /// バックグラウンドで分割解析してキャッシュする。
    /// </summary>
    internal class HistoryPanel : VisualElement
    {
        readonly MD3Theme _theme;
        readonly ListView _listView;
        readonly TextField _searchField;

        public Action<string> OnSessionSelected;
        public Action<string> OnSessionDeleted;

        // 解析済みヘッダーのメモリキャッシュ（filePath -> header）。
        readonly Dictionary<string, ChatSessionHeader> _headerCache =
            new Dictionary<string, ChatSessionHeader>();
        // 全セッションファイル（新しい順）。
        readonly List<string> _allFiles = new List<string>();
        // フィルタ適用後の表示対象。ListView.itemsSource に渡す。
        readonly List<string> _visibleFiles = new List<string>();
        // ヘッダー未解析のファイル。
        readonly Queue<string> _pendingFiles = new Queue<string>();

        IVisualElementScheduledItem _loadPump;

        public HistoryPanel(MD3Theme theme)
        {
            _theme = theme;
            style.flexGrow = 1;
            style.display = DisplayStyle.None;

            _searchField = new TextField();
            _searchField.style.marginLeft = 12;
            _searchField.style.marginRight = 12;
            _searchField.style.marginTop = 8;
            _searchField.style.marginBottom = 8;
            _searchField.RegisterValueChangedCallback(evt => ApplyFilter());
            Add(_searchField);

            _listView = new ListView();
            _listView.style.flexGrow = 1;
            _listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _listView.selectionType = SelectionType.None;
            _listView.makeItem = MakeRow;
            _listView.bindItem = BindRow;
            _listView.itemsSource = _visibleFiles;
            Add(_listView);
        }

        /// <summary>
        /// 履歴一覧の読み込みを開始する。ファイルパス一覧の取得は高速で、
        /// ListView 仮想化により行生成は表示中ぶんだけ。ヘッダーはポンプで分割解析。
        /// </summary>
        public void BeginLoad()
        {
            _loadPump?.Pause();
            _loadPump = null;
            _pendingFiles.Clear();

            _allFiles.Clear();
            _allFiles.AddRange(ChatHistoryManager.ListSessionFiles());

            foreach (var f in _allFiles)
                if (!_headerCache.ContainsKey(f))
                    _pendingFiles.Enqueue(f);

            ApplyFilter();

            if (_pendingFiles.Count > 0)
                _loadPump = schedule.Execute(PumpLoad).Every(1);
        }

        /// <summary>時間予算（約 32ms）内でヘッダーを解析し、表示中の行を更新する。</summary>
        void PumpLoad()
        {
            var sw = Stopwatch.StartNew();
            bool any = false;
            while (_pendingFiles.Count > 0 && sw.ElapsedMilliseconds < 32)
            {
                string file = _pendingFiles.Dequeue();
                var header = ChatHistoryManager.ReadSessionHeader(file);
                if (header == null)
                {
                    header = new ChatSessionHeader
                    {
                        title = M("(読み込み失敗)"),
                        timestamp = ChatHistoryManager.TimestampFromFileName(file),
                        filePath = file,
                        messageCount = 0
                    };
                }
                _headerCache[file] = header;
                any = true;
            }
            if (any) _listView.RefreshItems();
            if (_pendingFiles.Count == 0)
            {
                _loadPump?.Pause();
                _loadPump = null;
            }
        }

        /// <summary>指定セッションを一覧から取り除く（削除後に呼ぶ。再解析しない）。</summary>
        public void RemoveSession(string filePath)
        {
            _headerCache.Remove(filePath);
            _allFiles.Remove(filePath);
            if (_visibleFiles.Remove(filePath))
                _listView.Rebuild();
        }

        /// <summary>検索フィルタを適用して表示リストを再構築する。</summary>
        void ApplyFilter()
        {
            string query = (_searchField.value ?? "").Trim().ToLowerInvariant();
            _visibleFiles.Clear();
            if (string.IsNullOrEmpty(query))
            {
                _visibleFiles.AddRange(_allFiles);
            }
            else
            {
                foreach (var f in _allFiles)
                {
                    string title =
                        _headerCache.TryGetValue(f, out var h) && !string.IsNullOrEmpty(h.title)
                            ? h.title
                            : Path.GetFileNameWithoutExtension(f);
                    if (title.ToLowerInvariant().Contains(query))
                        _visibleFiles.Add(f);
                }
            }
            _listView.Rebuild();
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        void BindRow(VisualElement element, int index)
        {
            element.Clear();
            if (index < 0 || index >= _visibleFiles.Count) return;
            string filePath = _visibleFiles[index];
            _headerCache.TryGetValue(filePath, out var header);

            string title = header != null && !string.IsNullOrEmpty(header.title)
                ? header.title
                : M("(読み込み中…)");
            string timestamp = header != null && !string.IsNullOrEmpty(header.timestamp)
                ? header.timestamp
                : ChatHistoryManager.TimestampFromFileName(filePath);
            string supporting = header != null
                ? string.Format("{0} · {1} {2}", timestamp, header.messageCount, M("メッセージ"))
                : timestamp;

            var item = new MD3ListItem(title, supporting, MD3Icon.Mail);
            item.style.flexGrow = 1;
            item.RegisterCallback<ClickEvent>(evt => OnSessionSelected?.Invoke(filePath));
            element.Add(item);

            var deleteBtn = new MD3IconButton(
                MD3Icon.Delete, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
            deleteBtn.tooltip = M("この履歴を削除");
            deleteBtn.style.flexShrink = 0;
            deleteBtn.style.marginRight = 8;
            deleteBtn.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                string dlgTitle =
                    _headerCache.TryGetValue(filePath, out var ch) && !string.IsNullOrEmpty(ch.title)
                        ? ch.title
                        : ChatHistoryManager.TimestampFromFileName(filePath);
                bool ok = UnityEditor.EditorUtility.DisplayDialog(
                    M("履歴を削除"),
                    string.Format(M("「{0}」を削除しますか？\nこの操作は元に戻せません。"), dlgTitle),
                    M("削除"), M("キャンセル"));
                if (ok) OnSessionDeleted?.Invoke(filePath);
            });
            element.Add(deleteBtn);
        }

        public void Show() => style.display = DisplayStyle.Flex;

        public void Hide()
        {
            _loadPump?.Pause();
            _loadPump = null;
            style.display = DisplayStyle.None;
        }

        public bool IsVisible => resolvedStyle.display == DisplayStyle.Flex;
    }
}
