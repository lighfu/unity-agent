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
    /// チャット履歴一覧パネル。ListView 仮想化＋再利用可能な行で、件数が多くても
    /// 開く操作もスクロールも軽い。ヘッダー（タイトル・件数）はバックグラウンドで分割解析。
    /// </summary>
    internal class HistoryPanel : VisualElement
    {
        const float RowHeight = 56f;

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
            _listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _listView.fixedItemHeight = RowHeight;
            _listView.selectionType = SelectionType.None;
            _listView.makeItem = MakeRow;
            _listView.bindItem = BindRow;
            _listView.itemsSource = _visibleFiles;
            Add(_listView);
        }

        /// <summary>
        /// 履歴一覧の読み込みを開始する。ファイルパス一覧の取得は高速、ListView 仮想化＋
        /// 行再利用により行生成は表示中ぶんだけ。ヘッダーはポンプで分割解析。
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

        VisualElement MakeRow() => new HistoryRow(_theme, this);

        void BindRow(VisualElement element, int index)
        {
            if (!(element is HistoryRow row)) return;
            if (index < 0 || index >= _visibleFiles.Count)
            {
                row.BindEmpty();
                return;
            }
            string filePath = _visibleFiles[index];
            _headerCache.TryGetValue(filePath, out var header);
            row.Bind(filePath, header);
        }

        // 行から呼ばれる内部ハンドラ。
        internal void HandleRowSelected(string filePath)
        {
            OnSessionSelected?.Invoke(filePath);
        }

        internal void HandleRowDeleteClicked(string filePath)
        {
            string dlgTitle =
                _headerCache.TryGetValue(filePath, out var ch) && !string.IsNullOrEmpty(ch.title)
                    ? ch.title
                    : ChatHistoryManager.TimestampFromFileName(filePath);
            bool ok = UnityEditor.EditorUtility.DisplayDialog(
                M("履歴を削除"),
                string.Format(M("「{0}」を削除しますか？\nこの操作は元に戻せません。"), dlgTitle),
                M("削除"), M("キャンセル"));
            if (ok) OnSessionDeleted?.Invoke(filePath);
        }

        public void Show() => style.display = DisplayStyle.Flex;

        public void Hide()
        {
            _loadPump?.Pause();
            _loadPump = null;
            style.display = DisplayStyle.None;
        }

        public bool IsVisible => resolvedStyle.display == DisplayStyle.Flex;

        // ───────────────────────────────────────────────
        //  再利用される 1 行。構造はコンストラクタで一度だけ作り、
        //  Bind で文字列と対象パスだけ更新する（スクロールを軽く保つ）。
        // ───────────────────────────────────────────────
        sealed class HistoryRow : VisualElement
        {
            readonly HistoryPanel _panel;
            readonly Label _title;
            readonly Label _supporting;
            string _filePath;

            public HistoryRow(MD3Theme theme, HistoryPanel panel)
            {
                _panel = panel;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.height = RowHeight;
                style.paddingLeft = 10;
                style.paddingRight = 4;

                var icon = new Label(MD3Icon.Mail);
                MD3Icon.Apply(icon, 20);
                icon.style.color = theme.OnSurfaceVariant;
                icon.style.marginRight = 10;
                icon.style.flexShrink = 0;
                Add(icon);

                var col = new VisualElement();
                col.style.flexGrow = 1;
                col.style.flexDirection = FlexDirection.Column;
                col.style.justifyContent = Justify.Center;
                col.style.overflow = Overflow.Hidden;

                _title = new Label();
                _title.style.fontSize = 13;
                _title.style.color = theme.OnSurface;
                _title.style.whiteSpace = WhiteSpace.NoWrap;
                _title.style.overflow = Overflow.Hidden;
                _title.style.textOverflow = TextOverflow.Ellipsis;
                col.Add(_title);

                _supporting = new Label();
                _supporting.style.fontSize = 11;
                _supporting.style.color = theme.OnSurfaceVariant;
                _supporting.style.marginTop = 1;
                _supporting.style.whiteSpace = WhiteSpace.NoWrap;
                _supporting.style.overflow = Overflow.Hidden;
                _supporting.style.textOverflow = TextOverflow.Ellipsis;
                col.Add(_supporting);

                Add(col);

                var deleteBtn = new MD3IconButton(
                    MD3Icon.Delete, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
                deleteBtn.tooltip = M("この履歴を削除");
                deleteBtn.style.flexShrink = 0;
                deleteBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    if (_filePath != null) _panel.HandleRowDeleteClicked(_filePath);
                });
                Add(deleteBtn);

                RegisterCallback<ClickEvent>(evt =>
                {
                    if (_filePath != null) _panel.HandleRowSelected(_filePath);
                });
                RegisterCallback<MouseEnterEvent>(_ =>
                    style.backgroundColor = theme.SurfaceContainerHigh);
                RegisterCallback<MouseLeaveEvent>(_ =>
                    style.backgroundColor = Color.clear);
            }

            /// <summary>行に 1 セッションのデータを割り当てる（文字列更新のみ、軽量）。</summary>
            public void Bind(string filePath, ChatSessionHeader header)
            {
                _filePath = filePath;
                string title = header != null && !string.IsNullOrEmpty(header.title)
                    ? header.title
                    : M("(読み込み中…)");
                string timestamp = header != null && !string.IsNullOrEmpty(header.timestamp)
                    ? header.timestamp
                    : ChatHistoryManager.TimestampFromFileName(filePath);
                _title.text = title;
                _supporting.text = header != null
                    ? string.Format("{0} · {1} {2}", timestamp, header.messageCount, M("メッセージ"))
                    : timestamp;
            }

            /// <summary>範囲外バインド時の空表示。</summary>
            public void BindEmpty()
            {
                _filePath = null;
                _title.text = "";
                _supporting.text = "";
            }
        }
    }
}
