using System;
using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>チャット履歴一覧パネル。</summary>
    internal class HistoryPanel : VisualElement
    {
        readonly MD3Theme _theme;
        readonly ScrollView _scrollView;
        readonly TextField _searchField;

        public Action<string> OnSessionSelected;
        public Action<string> OnSessionDeleted;

        public HistoryPanel(MD3Theme theme)
        {
            _theme = theme;

            style.flexGrow = 1;
            style.display = DisplayStyle.None;

            // 検索バー
            _searchField = new TextField();
            _searchField.style.marginLeft = 12;
            _searchField.style.marginRight = 12;
            _searchField.style.marginTop = 8;
            _searchField.style.marginBottom = 8;
            var placeholder = M("履歴を検索...");
            _searchField.RegisterValueChangedCallback(evt => FilterItems(evt.newValue));
            Add(_searchField);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Add(_scrollView);
        }

        /// <summary>セッション一覧を表示する。</summary>
        public void LoadSessions(List<ChatSessionHeader> sessions)
        {
            _scrollView.Clear();
            if (sessions == null) return;

            foreach (var session in sessions)
            {
                string filePath = session.filePath;

                // 行コンテナ（リストアイテム + 削除ボタン）
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.name = "session-item";

                string supporting = string.Format(
                    "{0} · {1} {2}",
                    session.timestamp ?? "",
                    session.messageCount,
                    M("メッセージ"));

                var item = new MD3ListItem(
                    session.title ?? M("(無題)"),
                    supporting,
                    MD3Icon.Mail);
                item.style.flexGrow = 1;
                item.RegisterCallback<ClickEvent>(evt =>
                {
                    OnSessionSelected?.Invoke(filePath);
                });
                row.Add(item);

                // 削除ボタン（普段は薄く、行ホバーで明るく）
                var deleteBtn = new MD3IconButton(
                    MD3Icon.Delete, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
                deleteBtn.tooltip = M("この履歴を削除");
                deleteBtn.style.opacity = 0.35f;
                deleteBtn.style.flexShrink = 0;
                deleteBtn.style.marginRight = 8;
                deleteBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    string title = session.title ?? M("(無題)");
                    bool ok = UnityEditor.EditorUtility.DisplayDialog(
                        M("履歴を削除"),
                        string.Format(M("「{0}」を削除しますか？\nこの操作は元に戻せません。"), title),
                        M("削除"), M("キャンセル"));
                    if (ok) OnSessionDeleted?.Invoke(filePath);
                });
                row.Add(deleteBtn);

                row.RegisterCallback<MouseEnterEvent>(_ => deleteBtn.style.opacity = 0.95f);
                row.RegisterCallback<MouseLeaveEvent>(_ => deleteBtn.style.opacity = 0.35f);

                _scrollView.Add(row);
            }
        }

        void FilterItems(string query)
        {
            foreach (var child in _scrollView.Children())
            {
                if (string.IsNullOrEmpty(query))
                {
                    child.style.display = DisplayStyle.Flex;
                }
                else
                {
                    // Search for the query in the headline label text
                    var headlineLabel = child.Q<Label>(className: "md3-list-item__headline");
                    string text = headlineLabel?.text ?? "";
                    bool match = text.ToLower().Contains(query.ToLower());
                    child.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        public void Show() => style.display = DisplayStyle.Flex;
        public void Hide() => style.display = DisplayStyle.None;
        public bool IsVisible => resolvedStyle.display == DisplayStyle.Flex;
    }
}
