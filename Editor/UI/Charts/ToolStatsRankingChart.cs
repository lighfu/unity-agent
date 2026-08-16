using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>ツール別呼び出し数を横棒で描く。棒の右にツール名と件数を重ねる。</summary>
    internal sealed class ToolStatsRankingChart : ToolStatsChartBase
    {
        /// <summary>行の高さ (px)。</summary>
        internal const float RowHeight = 26f;

        /// <summary>上下の余白 (px)。高さは 8 + 行数 * RowHeight になる。</summary>
        const float EdgePad = 4f;

        /// <summary>左右の余白 (px)。このチャートだけ PadLeft を使わない。</summary>
        const float SidePad = 8f;

        /// <summary>棒の高さ (px)。</summary>
        const float BarHeight = 16f;

        /// <summary>データが空のときの高さ。M("データなし") が隠れない最低限を確保する。</summary>
        const float EmptyHeight = 48f;

        List<ToolStatsRankItem> _items;
        int _maxCalls;

        internal ToolStatsRankingChart(MD3Theme theme) : base(theme, RowHeight)
        {
        }

        /// <summary>ランキングを差し替える。高さを 8 + items.Count * RowHeight に更新する。</summary>
        internal void SetData(List<ToolStatsRankItem> items)
        {
            _items = items;

            _maxCalls = 0;
            if (_items != null)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].calls > _maxCalls) _maxCalls = _items[i].calls;
                }
            }

            int count = _items != null ? _items.Count : 0;
            style.height = count > 0 ? EdgePad * 2f + count * RowHeight : EmptyHeight;

            RebuildLabels();
            MarkDirtyRepaint();
        }

        protected override void RebuildLabels()
        {
            ClearLabels();

            if (_items == null || _items.Count == 0)
            {
                ShowNoData();
                return;
            }
            if (!TryGetRowLayout(out float x, out float w)) return;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                float top = RowTop(i) + 6f;

                var name = AddLabel(item.toolName ?? "", MD3TextStyle.LabelSmall,
                    _theme.OnSurface, x + 4f, top);
                name.style.width = Mathf.Max(40f, w * 0.5f);
                name.style.whiteSpace = WhiteSpace.NoWrap;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;

                string detail = string.Format(M("{0} 件 / 平均 {1} ms"),
                    item.calls, Mathf.RoundToInt((float)item.avgDurationMs));
                var stat = AddLabel(detail, MD3TextStyle.LabelSmall,
                    _theme.OnSurfaceVariant, 0f, top);
                stat.style.right = x + 4f;
                stat.style.unityTextAlign = TextAnchor.UpperRight;
                stat.style.whiteSpace = WhiteSpace.NoWrap;
                stat.style.overflow = Overflow.Hidden;
            }
        }

        protected override void DrawChart(Painter2D painter, float plotW, float plotH)
        {
            if (_items == null || _items.Count == 0) return;
            if (!TryGetRowLayout(out float x, out float w)) return;
            if (_maxCalls <= 0) return;   // 全件 0 呼び出しは比率を作れないので棒を描かない

            var track = _theme.SurfaceVariant;
            track.a *= 0.5f;

            // 棒は「ラベルの背後に敷く帯」なので不透明では困る。ラベルは行の左端に
            // OnSurface (明色) で重ねて描くため、Primary を不透明で塗ると明色どうしが重なって
            // ツール名が読めなくなる (実機のスクリーンショットで確認済み)。
            // 透かして敷けば、棒の上でも地の上でも同じラベル色で読める。
            var bar = _theme.Primary;
            bar.a *= 0.38f;
            var fail = _theme.Error;
            fail.a *= 0.55f;   // 失敗は目立つべきなので棒よりわずかに濃く

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                float barY = RowTop(i) + (RowHeight - BarHeight) * 0.5f;

                painter.fillColor = track;
                DrawRoundedRect(painter, x, barY, w, BarHeight, 4f);

                float barW = w * Mathf.Clamp01(item.calls / (float)_maxCalls);
                if (barW < 1f) continue;

                painter.fillColor = bar;
                DrawRoundedRect(painter, x, barY, barW, BarHeight, 4f);

                if (item.failures <= 0) continue;

                // 失敗分は棒の右端側に内包する。棒からはみ出さないようクランプする。
                float failW = Mathf.Min(barW, w * Mathf.Clamp01(item.failures / (float)_maxCalls));
                if (failW < 1f) continue;

                painter.fillColor = fail;
                DrawRoundedRect(painter, x + barW - failW, barY, failW, BarHeight, 4f);
            }
        }

        /// <summary>i 行目の上端 Y。描画とラベルで同じ式を使う。</summary>
        static float RowTop(int index) => EdgePad + index * RowHeight;

        /// <summary>行の左端と幅を求める。サイズ未確定なら false。</summary>
        bool TryGetRowLayout(out float x, out float w)
        {
            x = 0f;
            w = 0f;

            float rw = resolvedStyle.width;
            if (float.IsNaN(rw) || rw <= 0f) return false;

            float width = rw - SidePad * 2f;
            if (width <= 0f) return false;

            x = SidePad;
            w = width;
            return true;
        }
    }
}
