using System;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Providers;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>プロバイダー/モデル/思考の深さの選択オーバーレイ。</summary>
    internal class QuickMenuOverlay : VisualElement
    {
        readonly MD3Theme _theme;
        readonly VisualElement _scrim;
        readonly VisualElement _menuCard;
        readonly ScrollView _scrollView;

        public Action OnDismiss;
        public Action<int> OnProviderSelected;
        public Action<string> OnModelSelected;
        public Action<int> OnThinkingSelected;

        public QuickMenuOverlay(MD3Theme theme)
        {
            _theme = theme;

            // Full-screen overlay
            style.position = Position.Absolute;
            style.top = 0;
            style.left = 0;
            style.right = 0;
            style.bottom = 0;
            style.display = DisplayStyle.None;

            // Scrim (semi-transparent background)
            _scrim = new VisualElement();
            _scrim.style.position = Position.Absolute;
            _scrim.style.top = 0;
            _scrim.style.left = 0;
            _scrim.style.right = 0;
            _scrim.style.bottom = 0;
            _scrim.style.backgroundColor = new Color(0, 0, 0, 0.32f);
            _scrim.RegisterCallback<ClickEvent>(evt =>
            {
                Hide();
                OnDismiss?.Invoke();
            });
            Add(_scrim);

            // Menu card
            _menuCard = new MD3Card(null, null, MD3CardStyle.Elevated);
            _menuCard.style.position = Position.Absolute;
            _menuCard.style.minWidth = 200;
            // 360px はプロバイダー名で最も長い「Web Browser (Gemini / ChatGPT / Copilot)」が
            // アイコンを足しても 1 行に収まる幅。.md3-menu-item は height 48px / overflow: hidden
            // なので、折り返すと 2 行目が切れて読めなくなる。
            // 想定している最も狭いウィンドウ (632px) でも、左端 8px + 360px で収まる。
            _menuCard.style.maxWidth = 360;
            _menuCard.style.maxHeight = 400;
            MD3Elevation.AddInnerShadow(_menuCard, 16f, 2);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _menuCard.Add(_scrollView);

            Add(_menuCard);
        }

        /// <summary>
        /// プロバイダー選択メニューを表示する。
        /// types を渡すと、項目の先頭にそのプロバイダーのアイコンが付く
        /// (names の添字と対応させること)。
        /// </summary>
        public void ShowProviderMenu(string[] names, string[] shortNames, int currentIndex, Rect anchorRect,
                                     LLMProviderType[] types = null)
        {
            _scrollView.Clear();

            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool selected = i == currentIndex;
                string label = names[i];

                var item = new MD3MenuItem(label);
                if (selected)
                    item.style.backgroundColor = _theme.SecondaryContainer;

                if (types != null && i < types.Length)
                {
                    // MD3MenuItem も中身は Label だけだが、.md3-menu-item は
                    // flex-direction: row / align-items: center なので先頭に差し込める。
                    var icon = ProviderIconStyle.CreateIcon(types[i], _theme, 16f);
                    icon.style.marginRight = 10;
                    item.Insert(0, icon);
                    // 増える幅を抑えるため、左の余白を 16px から 12px に詰める。
                    item.style.paddingLeft = 12;
                    item.tooltip = ProviderIconStyle.RouteHint(types[i]);
                }

                item.clicked += () =>
                {
                    OnProviderSelected?.Invoke(idx);
                    Hide();
                };

                _scrollView.Add(item);
            }

            PositionNear(anchorRect);
            Show();
        }

        /// <summary>モデル選択メニューを表示する。</summary>
        public void ShowModelMenu(string[] presets, string[] displayNames, string currentModel, Rect anchorRect)
        {
            _scrollView.Clear();

            if (presets == null || presets.Length == 0)
            {
                var emptyLabel = new Label(M("モデルプリセットなし"));
                emptyLabel.style.color = _theme.OnSurfaceVariant;
                emptyLabel.style.paddingLeft = 16;
                emptyLabel.style.paddingTop = 8;
                emptyLabel.style.paddingBottom = 8;
                _scrollView.Add(emptyLabel);
            }
            else
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    string modelId = presets[i];
                    string display = (displayNames != null && i < displayNames.Length)
                        ? displayNames[i] : modelId;
                    bool selected = modelId == currentModel;

                    var item = new MD3MenuItem(display);
                    if (selected)
                        item.style.backgroundColor = _theme.SecondaryContainer;

                    item.clicked += () =>
                    {
                        OnModelSelected?.Invoke(modelId);
                        Hide();
                    };

                    _scrollView.Add(item);
                }
            }

            PositionNear(anchorRect);
            Show();
        }

        /// <summary>
        /// 思考の深さの選択メニューを表示する。項目の中身 (強さの段階か、トークン数か) は
        /// モデルの指定方法で変わるので、ラベルは呼び出し側が組み立てて渡し、選ばれた添字を返す。
        /// </summary>
        public void ShowThinkingMenu(string[] labels, int currentIndex, Rect anchorRect)
        {
            _scrollView.Clear();

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var item = new MD3MenuItem(labels[i]);
                if (i == currentIndex)
                    item.style.backgroundColor = _theme.SecondaryContainer;

                item.clicked += () =>
                {
                    OnThinkingSelected?.Invoke(idx);
                    Hide();
                };

                _scrollView.Add(item);
            }

            PositionNear(anchorRect);
            Show();
        }

        void PositionNear(Rect anchor)
        {
            // Position menu card above or below anchor
            _menuCard.style.left = Mathf.Max(8, anchor.x);
            _menuCard.style.bottom = 60; // Above input area
        }

        public void Show()
        {
            style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
        }

        public bool IsVisible => resolvedStyle.display == DisplayStyle.Flex;
    }
}
