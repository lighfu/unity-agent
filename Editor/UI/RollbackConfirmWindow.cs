using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>編集・再生成で巻き戻し対象の Unity 変更があるときの 3 択確認結果。</summary>
    internal enum RollbackChoice { Cancel, KeepChanges, Rollback }

    /// <summary>
    /// 変更リスト付き・スクロール可能なモーダル確認ダイアログ。
    /// DisplayDialogComplex はリスト表示できないため UI Toolkit のモーダルウィンドウで実装する。
    /// </summary>
    internal class RollbackConfirmWindow : EditorWindow
    {
        MD3Theme _theme;
        IReadOnlyList<ChangeRecord> _changes;
        RollbackChoice _result = RollbackChoice.Cancel;

        /// <summary>
        /// モーダルで開き、ユーザーの選択を返す。ShowModal が閉じるまでブロックする。
        /// </summary>
        public static RollbackChoice Show(MD3Theme theme, IReadOnlyList<ChangeRecord> changes)
        {
            var w = CreateInstance<RollbackConfirmWindow>();
            w.titleContent = new GUIContent(M("変更の巻き戻し確認"));
            w._theme = theme;
            w._changes = changes ?? new List<ChangeRecord>();
            w.minSize = new Vector2(460, 380);
            w.maxSize = new Vector2(460, 380);
            w.ShowModal();
            return w._result;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 14;
            root.style.paddingBottom = 14;
            if (_theme != null) root.style.backgroundColor = _theme.Surface;

            var title = new Label(M("この時点より後の変更を巻き戻しますか？"));
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            if (_theme != null) title.style.color = _theme.OnSurface;
            root.Add(title);

            int count = _changes?.Count ?? 0;
            var msg = new Label(string.Format(
                M("この時点以降にエージェントが行った Unity の変更が {0} 件あります。編集を続けると会話履歴は切り詰められます。"),
                count));
            msg.style.fontSize = 12;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.marginTop = 6;
            msg.style.marginBottom = 8;
            if (_theme != null) msg.style.color = _theme.OnSurfaceVariant;
            root.Add(msg);

            // スクロール可能な変更リスト
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.marginBottom = 10;
            scroll.style.borderTopLeftRadius = 8;
            scroll.style.borderTopRightRadius = 8;
            scroll.style.borderBottomLeftRadius = 8;
            scroll.style.borderBottomRightRadius = 8;
            if (_theme != null)
                scroll.style.backgroundColor = _theme.SurfaceContainerHigh;
            scroll.style.paddingLeft = 6;
            scroll.style.paddingRight = 6;
            scroll.style.paddingTop = 4;
            scroll.style.paddingBottom = 4;

            if (_changes != null)
            {
                foreach (var c in _changes)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.FlexStart;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;

                    var tool = new Label(c.toolName ?? "");
                    tool.style.fontSize = 11;
                    tool.style.unityFontStyleAndWeight = FontStyle.Bold;
                    tool.style.flexShrink = 0;
                    tool.style.marginRight = 8;
                    if (_theme != null) tool.style.color = _theme.Primary;
                    row.Add(tool);

                    var sum = new Label(c.summary ?? "");
                    sum.style.fontSize = 11;
                    sum.style.whiteSpace = WhiteSpace.Normal;
                    sum.style.flexGrow = 1;
                    if (_theme != null) sum.style.color = _theme.OnSurface;
                    row.Add(sum);

                    scroll.Add(row);
                }
            }
            root.Add(scroll);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.FlexEnd;

            var cancel = new Button(() => { _result = RollbackChoice.Cancel; Close(); })
            { text = M("キャンセル") };
            var keep = new Button(() => { _result = RollbackChoice.KeepChanges; Close(); })
            { text = M("巻き戻さず続行") };
            var rollback = new Button(() => { _result = RollbackChoice.Rollback; Close(); })
            { text = M("巻き戻して続行") };
            keep.style.marginLeft = 6;
            rollback.style.marginLeft = 6;

            btnRow.Add(cancel);
            btnRow.Add(keep);
            btnRow.Add(rollback);
            root.Add(btnRow);
        }
    }
}
