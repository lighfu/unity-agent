using System;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>ヘッダーツールバー: 設定、Undo、新規チャット、履歴、Web、セーフ、スキル。</summary>
    internal class ToolbarPanel : VisualElement
    {
        readonly MD3Theme _theme;

        VisualElement _undoBadgeContainer;
        VisualElement _confirmBadgeContainer;
        VisualElement _skillBadgeContainer;
        VisualElement _webBadgeContainer;
        VisualElement _historyBadgeContainer;
        MD3IconButton _webBtn;
        MD3IconButton _historyBtn;

        public Action OnSettingsClicked;
        public Action OnUndoAllClicked;
        public Action OnNewChatClicked;
        public Action OnHistoryToggled;
        public Action OnSupportClicked;
        public Action OnWebToggled;
        public Action OnSafetyClicked;
        public Action OnSkillsClicked;

        public ToolbarPanel(MD3Theme theme)
        {
            _theme = theme;

            style.flexDirection = FlexDirection.Row;
            style.flexShrink = 0;
            style.alignItems = Align.Center;
            style.height = 56;
            style.paddingLeft = 8;
            style.paddingRight = 8;
            style.backgroundColor = theme.SurfaceContainerHigh;
            style.borderBottomWidth = 1;
            style.borderBottomColor = theme.OutlineVariant;

            BuildButtons();
        }

        void BuildButtons()
        {
            // Settings
            var settingsBtn = CreateToolbarButton(MD3Icon.Settings, M("設定"));
            settingsBtn.clicked += () => OnSettingsClicked?.Invoke();
            Add(settingsBtn);

            // Spacer pushes remaining buttons to the right
            Add(new MD3Spacer());

            // Skills
            var skillsBtn = CreateToolbarButton(MD3Icon.Star, M("スキル"));
            skillsBtn.clicked += () => OnSkillsClicked?.Invoke();
            var skillBadgeContainer = WrapWithBadge(skillsBtn, out _skillBadgeContainer);
            Add(skillBadgeContainer);

            // Safety
            var safeBtn = CreateToolbarButton(MD3Icon.Lock, M("安全"));
            safeBtn.clicked += () => OnSafetyClicked?.Invoke();
            var safeBadgeContainer = WrapWithBadge(safeBtn, out _confirmBadgeContainer);
            Add(safeBadgeContainer);

            // Web server
            _webBtn = CreateToolbarButton(MD3Icon.Link, M("Web サーバー"));
            _webBtn.clicked += () => OnWebToggled?.Invoke();
            var webBadgeContainer = WrapWithBadge(_webBtn, out _webBadgeContainer);
            Add(webBadgeContainer);

            // Support
            var supportBtn = CreateToolbarButton(MD3Icon.Favorite, "Ko-fi");
            supportBtn.clicked += () => OnSupportClicked?.Invoke();
            Add(supportBtn);

            // History
            _historyBtn = CreateToolbarButton("\ue889", M("履歴"));
            _historyBtn.clicked += () => OnHistoryToggled?.Invoke();
            var historyBadgeContainer = WrapWithBadge(_historyBtn, out _historyBadgeContainer);
            Add(historyBadgeContainer);

            // New Chat
            var newChatBtn = CreateToolbarButton(MD3Icon.Add, M("新規チャット"));
            newChatBtn.clicked += () => OnNewChatClicked?.Invoke();
            Add(newChatBtn);

            // Undo All
            var undoBtn = CreateToolbarButton(MD3Icon.Undo, M("元に戻す"));
            undoBtn.clicked += () => OnUndoAllClicked?.Invoke();
            var undoBadgeContainer = WrapWithBadge(undoBtn, out _undoBadgeContainer);
            Add(undoBadgeContainer);
        }

        MD3IconButton CreateToolbarButton(string icon, string tooltip)
        {
            var btn = new MD3IconButton(icon, MD3IconButtonStyle.Standard, MD3IconButtonSize.Medium);
            btn.tooltip = tooltip;
            btn.style.marginLeft = 2;
            btn.style.marginRight = 2;
            return btn;
        }

        VisualElement WrapWithBadge(MD3IconButton button, out VisualElement badgeSlot)
        {
            var container = new VisualElement();
            container.style.position = Position.Relative;
            container.Add(button);

            badgeSlot = new VisualElement();
            badgeSlot.style.position = Position.Absolute;
            badgeSlot.style.top = 2;
            badgeSlot.style.right = 2;
            container.Add(badgeSlot);

            return container;
        }

        void SetBadge(VisualElement slot, int count, bool visible)
        {
            slot.Clear();
            if (visible && count > 0)
            {
                var badge = new MD3Badge(count);
                slot.Add(badge);
            }
        }

        /// <summary>Undo バッジ数を更新する。</summary>
        public void UpdateUndoCount(int count)
        {
            SetBadge(_undoBadgeContainer, count, count > 0);
        }

        /// <summary>安全確認ツール数を更新する。</summary>
        public void UpdateConfirmCount(int count)
        {
            SetBadge(_confirmBadgeContainer, count, count > 0);
        }

        /// <summary>スキル有効数を更新する。</summary>
        public void UpdateSkillCount(int enabled, int total)
        {
            SetBadge(_skillBadgeContainer, enabled, enabled < total);
        }

        /// <summary>Web サーバーのアクティブ状態を反映する。</summary>
        public void SetWebActive(bool active)
        {
            SetActiveState(_webBtn, _webBadgeContainer, active);
        }

        /// <summary>履歴パネルの表示状態を反映する。</summary>
        public void SetHistoryActive(bool active)
        {
            SetActiveState(_historyBtn, _historyBadgeContainer, active);
        }

        void SetActiveState(MD3IconButton btn, VisualElement badgeSlot, bool active)
        {
            // ドットバッジ
            badgeSlot.Clear();
            if (active)
                badgeSlot.Add(new MD3Badge());

            // アイコン色: アクティブ時は Primary、非アクティブ時はデフォルト
            btn.IconColorOverride = active ? _theme.Primary : (Color?)null;
        }
    }
}
