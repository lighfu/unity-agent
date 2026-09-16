using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// 更新内容 (changelog) を、高さの上限を持つスクロール領域として組み立てる。
    ///
    /// MD3Dialog のカード (.md3-dialog__card) は max-width しか持たず、絶対配置で
    /// 中央に寄せられる。長い文面を本文やカードに直接積むとカードごと上下に伸び、
    /// 一番下のボタン行がウィンドウの外へ出て押せなくなる。更新内容のように
    /// 長さが青天井の文面は必ずここを通して高さを抑える。
    /// </summary>
    internal static class ChangelogView
    {
        /// <summary>
        /// ウィンドウの高さから、更新内容に割ける高さを決める。
        /// reserved はタイトル・ボタン行・余白など、更新内容以外が使う高さの見積もり。
        /// 狭いウィンドウでも最低限は見えるように下限を、
        /// 広いウィンドウで間延びしないように上限を設ける。
        /// </summary>
        internal static float MaxHeightFor(float windowHeight, float reserved)
        {
            return Mathf.Clamp(windowHeight - reserved, 96f, 360f);
        }

        /// <summary>
        /// changelog を maxHeight で頭打ちにしたスクロール領域に入れて返す。
        /// 本文は選択してコピーできる。
        /// </summary>
        internal static VisualElement Build(string changelog, float maxHeight, Color textColor,
            int fontSize = 12)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            // flexGrow を 0 にしないと、親の余白に合わせて伸びて上限が効かない。
            scroll.style.flexGrow = 0;
            scroll.style.maxHeight = maxHeight;

            scroll.Add(LongText.Build(changelog ?? "", label =>
            {
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.fontSize = fontSize;
                label.style.color = textColor;
                label.selection.isSelectable = true;
            }));

            return scroll;
        }
    }
}
