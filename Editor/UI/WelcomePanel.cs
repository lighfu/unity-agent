using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>ウェルカム画面: タイトル + ドキュメントカード + ヒントカード。</summary>
    internal class WelcomePanel : VisualElement
    {
        readonly MD3Theme _theme;
        VisualElement _updateBanner;

        static readonly (string icon, string title, string body, int accent)[] Hints =
        {
            ("\u2B07", "Drag & Drop", "ヒエラルキーやプロジェクトウィンドウからアセットやGameObjectをチャット欄にドロップすると、AIがそれを参照できます。", 0),
            ("\u26A0", "バックアップ", "AIは間違えることがあります。重要な作業の前にはシーンやプロジェクトのバックアップを取ることをお勧めします。", 2),
            ("\u2665", "サポート", "寄付することで開発を支援できます。ツールバーの\u2665ボタンからKo-fiページにアクセスできます。", 1),
            ("\u2709", "コミュニティ", "Discordサーバーで他のユーザーと交流したり、最新情報を入手できます。", 0),
            ("\u270E", "フィードバック", "正確な指示をしても期待通りの結果が得られない場合は、具体的な修正内容を伝えてください。", 2),
        };

        const string DocUrl = "https://www.notion.so/UnityAgent-316b2648e05780afb7d5e027cffc1ac1";

        public WelcomePanel(MD3Theme theme)
        {
            _theme = theme;

            style.flexGrow = 1;
            style.justifyContent = Justify.Center;
            style.alignItems = Align.Center;
            style.paddingLeft = 16;
            style.paddingRight = 16;

            BuildUI();
            PlayEntranceAnimation();
        }

        void BuildUI()
        {
            // Title
            var title = new MD3Text(M("UnityAI Agent へようこそ"), MD3TextStyle.DisplaySmall);
            title.style.color = _theme.OnSurface;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 16;
            title.name = "welcome-title";
            Add(title);

            // Update banner placeholder
            _updateBanner = new VisualElement();
            _updateBanner.style.display = DisplayStyle.None;
            _updateBanner.style.marginBottom = 8;
            _updateBanner.style.maxWidth = 500;
            _updateBanner.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            Add(_updateBanner);

            // Documentation card
            var docCard = CreateDocCard();
            docCard.name = "doc-card";
            Add(docCard);

            // Hint cards
            for (int i = 0; i < Hints.Length; i++)
            {
                var card = CreateHintCard(i);
                card.style.marginTop = 8;
                Add(card);
            }
        }

        MD3Card CreateDocCard()
        {
            var card = new MD3Card(null, null, MD3CardStyle.Filled);
            card.style.maxWidth = 500;
            card.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.flexDirection = FlexDirection.Row;

            // Icon
            var iconCircle = CreateIconCircle("\uD83D\uDCC4", _theme.PrimaryContainer, _theme.OnPrimaryContainer);
            card.Add(iconCircle);

            // Text
            var textCol = new MD3Column(gap: 2f);
            textCol.style.marginLeft = 12;
            textCol.style.flexShrink = 1;

            var title = new MD3Text(M("ドキュメント"), MD3TextStyle.TitleMedium);
            title.style.color = _theme.OnSurface;
            textCol.Add(title);

            var body = new Label(M("使い方や設定方法、トラブルシューティングなど、UnityAgent の詳しいドキュメントはこちら。"));
            body.style.fontSize = 12;
            body.style.color = _theme.OnSurfaceVariant;
            body.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(body);

            card.Add(textCol);

            card.RegisterCallback<ClickEvent>(evt => Application.OpenURL(DocUrl));
            card.style.cursor = StyleKeyword.Null; // ClickEvent will handle it

            return card;
        }

        MD3Card CreateHintCard(int index)
        {
            var (icon, titleText, bodyText, accent) = Hints[index];
            bool filled = (index % 2 == 0);

            Color accentBg, accentFg;
            switch (accent)
            {
                case 1: accentBg = _theme.SecondaryContainer; accentFg = _theme.OnSecondaryContainer; break;
                case 2: accentBg = _theme.TertiaryContainer; accentFg = _theme.OnTertiaryContainer; break;
                default: accentBg = _theme.PrimaryContainer; accentFg = _theme.OnPrimaryContainer; break;
            }

            var card = new MD3Card(null, null, filled ? MD3CardStyle.Filled : MD3CardStyle.Outlined);
            card.style.maxWidth = 500;
            card.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.flexDirection = FlexDirection.Row;

            // Icon circle
            var iconCircle = CreateIconCircle(icon, accentBg, accentFg);
            card.Add(iconCircle);

            // Text column
            var textCol = new MD3Column(gap: 2f);
            textCol.style.marginLeft = 12;
            textCol.style.flexShrink = 1;

            var title = new MD3Text(M(titleText), MD3TextStyle.TitleMedium);
            title.style.color = _theme.OnSurface;
            textCol.Add(title);

            var body = new Label(M(bodyText));
            body.style.fontSize = 12;
            body.style.color = _theme.OnSurfaceVariant;
            body.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(body);

            card.Add(textCol);

            return card;
        }

        VisualElement CreateIconCircle(string icon, Color bg, Color fg)
        {
            var circle = new VisualElement();
            circle.style.width = 40;
            circle.style.height = 40;
            circle.style.borderTopLeftRadius = 20;
            circle.style.borderTopRightRadius = 20;
            circle.style.borderBottomLeftRadius = 20;
            circle.style.borderBottomRightRadius = 20;
            circle.style.backgroundColor = bg;
            circle.style.alignItems = Align.Center;
            circle.style.justifyContent = Justify.Center;
            circle.style.flexShrink = 0;

            var iconLabel = new Label(icon);
            iconLabel.style.fontSize = 18;
            iconLabel.style.color = fg;
            iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            circle.Add(iconLabel);

            return circle;
        }

        void PlayEntranceAnimation()
        {
            // Fade-in + slide-up for title
            var title = this.Q<VisualElement>("welcome-title");
            if (title != null)
            {
                title.style.opacity = 0f;
                title.style.translate = new Translate(0, -30);
                MD3Animate.Float(title, 0f, 1f, 600f, MD3Easing.EaseOut, v =>
                {
                    title.style.opacity = v;
                    title.style.translate = new Translate(0, (1f - v) * -30f);
                });
            }

            // Stagger cards
            int cardIdx = 0;
            foreach (var child in Children())
            {
                if (child.name == "welcome-title" || child == _updateBanner) continue;

                child.style.opacity = 0f;
                child.style.translate = new Translate(0, 40);

                float delay = 300f + cardIdx * 100f;
                int ci = cardIdx;
                var captured = child;
                MD3Animate.Delayed(child, delay, () =>
                {
                    MD3Animate.Float(captured, 0f, 1f, 500f, MD3Easing.EaseOut, v =>
                    {
                        captured.style.opacity = v;
                        captured.style.translate = new Translate(0, (1f - v) * 40f);
                    });
                });

                cardIdx++;
            }
        }

        /// <summary>アップデートバナーを表示する。</summary>
        public void ShowUpdateBanner(string version, string changelog, Action onDownload)
        {
            _updateBanner.Clear();
            string msg = string.IsNullOrEmpty(changelog)
                ? string.Format(M("v{0} が利用可能です！"), version)
                : string.Format(M("v{0} が利用可能です！\n{1}"), version, changelog);

            var banner = new MD3Banner(msg, MD3Icon.Info);
            _updateBanner.Add(banner);
            _updateBanner.style.display = DisplayStyle.Flex;

            // TODO: action button for download
        }
    }
}
