using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>カテゴリ別の内訳をドーナツで描き、右側に凡例を並べる。</summary>
    internal sealed class ToolStatsCategoryChart : ToolStatsChartBase
    {
        /// <summary>凡例の最大行数。</summary>
        const int MaxLegendRows = 8;

        /// <summary>凡例 1 行の高さ (px)。</summary>
        const float LegendRowHeight = 18f;

        /// <summary>凡例の色見本のサイズ (px)。</summary>
        const float SwatchSize = 10f;

        /// <summary>ドーナツの輪の太さ (px)。</summary>
        const float RingWidth = 22f;

        /// <summary>ドーナツの中心と凡例の間隔 (px)。</summary>
        const float LegendGap = 24f;

        /// <summary>ドーナツの座標。描画とラベル配置で同じ式を使うために 1 箇所へ集約する。</summary>
        struct Layout
        {
            public float cx;
            public float cy;
            public float radius;
            public float legendX;
            public float legendTop;
            public int legendRows;
            public int total;
        }

        List<ToolStatsCategorySlice> _slices;
        int _total;

        internal ToolStatsCategoryChart(MD3Theme theme) : base(theme, 220f)
        {
        }

        /// <summary>スライスを差し替える。合計 0 なら M("データなし")。</summary>
        internal void SetData(List<ToolStatsCategorySlice> slices)
        {
            _slices = slices;

            _total = 0;
            if (_slices != null)
            {
                for (int i = 0; i < _slices.Count; i++)
                {
                    if (_slices[i].calls > 0) _total += _slices[i].calls;
                }
            }

            RebuildLabels();
            MarkDirtyRepaint();
        }

        protected override void RebuildLabels()
        {
            ClearLabels();

            if (_total <= 0)
            {
                ShowNoData();
                return;
            }
            if (!TryGetLayout(out Layout lay)) return;

            // 中心の総数。
            var center = AddLabel(lay.total.ToString(), MD3TextStyle.TitleMedium,
                _theme.OnSurface, lay.cx - 40f, lay.cy - 10f);
            center.style.width = 80f;
            center.style.unityTextAlign = TextAnchor.MiddleCenter;

            // 凡例。色見本は DrawChart 側で描くので、ここは文字だけ。
            for (int i = 0; i < lay.legendRows; i++)
            {
                var slice = _slices[i];
                string text = string.Format("{0}  {1}{2}", slice.category ?? "", slice.calls, M("回"));
                var lbl = AddLabel(text, MD3TextStyle.LabelSmall, _theme.OnSurfaceVariant,
                    lay.legendX + SwatchSize + 6f, lay.legendTop + i * LegendRowHeight + 2f);
                lbl.style.right = PadRight;
                lbl.style.whiteSpace = WhiteSpace.NoWrap;
                lbl.style.overflow = Overflow.Hidden;
                lbl.style.textOverflow = TextOverflow.Ellipsis;
            }
        }

        protected override void DrawChart(Painter2D painter, float plotW, float plotH)
        {
            if (_total <= 0) return;
            if (!TryGetLayout(out Layout lay)) return;

            // 真上 (-90 度) から時計回り。UI Toolkit は Y 下向きなので角度も時計回りになる。
            painter.lineWidth = RingWidth;
            painter.lineCap = LineCap.Butt;

            float angle = -90f;
            const float sweepEnd = 270f;
            for (int i = 0; i < _slices.Count; i++)
            {
                int calls = _slices[i].calls;
                if (calls <= 0) continue;

                // 1 度未満のスライスも見えるように最低 1 度を確保する。
                float sweep = Mathf.Max(1f, 360f * calls / _total);
                if (angle + sweep > sweepEnd) sweep = sweepEnd - angle;
                if (sweep <= 0f) break;

                painter.strokeColor = SliceColor(i);
                painter.BeginPath();
                painter.Arc(new Vector2(lay.cx, lay.cy), lay.radius, angle, angle + sweep);
                painter.Stroke();

                angle += sweep;
            }

            // 凡例の色見本。
            for (int i = 0; i < lay.legendRows; i++)
            {
                painter.fillColor = SliceColor(i);
                DrawRoundedRect(painter, lay.legendX,
                    lay.legendTop + i * LegendRowHeight + 3f, SwatchSize, SwatchSize, 2f);
            }
        }

        /// <summary>スライス i の色。先頭は Primary、以降は 5 色を循環する。</summary>
        Color SliceColor(int index)
        {
            if (index <= 0) return _theme.Primary;
            switch ((index - 1) % 5)
            {
                case 0: return _theme.Tertiary;
                case 1: return _theme.Secondary;
                case 2: return _theme.PrimaryContainer;
                case 3: return _theme.SecondaryContainer;
                default: return _theme.TertiaryContainer;
            }
        }

        /// <summary>描画とラベルで共有する座標系を作る。サイズ未確定なら false。</summary>
        bool TryGetLayout(out Layout lay)
        {
            lay = default;
            if (_slices == null || _slices.Count == 0 || _total <= 0) return false;
            if (!TryGetPlotSize(out float w, out float h)) return false;

            float radius = Mathf.Min(h, 140f) * 0.5f - 12f;
            if (radius <= RingWidth * 0.5f) return false;   // 輪が潰れる高さでは描かない

            float cx = PadLeft + radius + 12f;
            float cy = PadTop + h * 0.5f;

            int rows = Mathf.Min(_slices.Count, MaxLegendRows);
            lay = new Layout
            {
                cx = cx,
                cy = cy,
                radius = radius,
                legendX = cx + radius + LegendGap,
                legendTop = cy - rows * LegendRowHeight * 0.5f,
                legendRows = rows,
                total = _total,
            };

            // プロット右端を越える凡例は描いても読めないので行数を 0 に落とす。
            if (lay.legendX + SwatchSize + 6f >= PadLeft + w) lay.legendRows = 0;
            return true;
        }
    }
}
