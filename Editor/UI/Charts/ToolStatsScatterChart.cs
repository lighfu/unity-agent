using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>X = 引数 + 結果の文字数、Y = 所要時間 (ms) の散布図。失敗した呼び出しは Error 色。</summary>
    internal sealed class ToolStatsScatterChart : ToolStatsChartBase
    {
        /// <summary>点の半径 (px)。</summary>
        const float DotRadius = 3f;

        /// <summary>プロット座標。描画とラベル配置で同じ式を使うために 1 箇所へ集約する。</summary>
        struct Layout
        {
            public float x0;
            public float y0;
            public float w;
            public float h;
            public float maxX;
            public float maxY;

            /// <summary>文字数 chars の X 座標。maxX は 1 以上が保証されている。</summary>
            public float XAt(float chars)
            {
                return x0 + w * Mathf.Clamp01(chars / maxX);
            }

            /// <summary>所要時間 ms の Y 座標。maxY は 1 以上が保証されている。</summary>
            public float YAt(float ms)
            {
                return y0 + h * (1f - Mathf.Clamp01(ms / maxY));
            }
        }

        List<ToolStatsScatterPoint> _points;

        internal ToolStatsScatterChart(MD3Theme theme) : base(theme, 220f)
        {
        }

        /// <summary>点列を差し替える。null / 空なら M("データなし")。</summary>
        internal void SetData(List<ToolStatsScatterPoint> points)
        {
            _points = points;
            RebuildLabels();
            MarkDirtyRepaint();
        }

        protected override void RebuildLabels()
        {
            ClearLabels();

            if (_points == null || _points.Count == 0)
            {
                ShowNoData();
                return;
            }
            if (!TryGetLayout(out Layout lay)) return;

            // 縦軸の軸名は左上に置く。目盛りラベルと重ならないよう目盛りは 1 行下げる。
            AddLabel(M("所要時間 (ms)"), MD3TextStyle.LabelSmall, _theme.OnSurfaceVariant, 2f, 0f);

            var yMax = AddLabel(
                Mathf.RoundToInt(lay.maxY).ToString() + M("ms"),
                MD3TextStyle.LabelSmall, _theme.OnSurfaceVariant, 0f, lay.y0 + 14f);
            yMax.style.width = PadLeft - 6f;
            yMax.style.unityTextAlign = TextAnchor.UpperRight;

            float bottom = lay.y0 + lay.h + 3f;

            var xName = AddLabel(M("文字数"), MD3TextStyle.LabelSmall,
                _theme.OnSurfaceVariant, 0f, bottom);
            xName.style.right = 0f;
            xName.style.unityTextAlign = TextAnchor.UpperCenter;

            var xMax = AddLabel(Mathf.RoundToInt(lay.maxX).ToString(), MD3TextStyle.LabelSmall,
                _theme.OnSurfaceVariant, 0f, bottom);
            xMax.style.right = PadRight;
            xMax.style.unityTextAlign = TextAnchor.UpperRight;
        }

        protected override void DrawChart(Painter2D painter, float plotW, float plotH)
        {
            if (_points == null || _points.Count == 0) return;
            if (!TryGetLayout(out Layout lay)) return;

            DrawGrid(painter, lay);

            // 色ごとに 2 パスに分ける。Painter2D は連続した Arc を線で繋いでしまうため、
            // 点は 1 つずつ BeginPath / Fill する。
            var okColor = _theme.Primary;
            okColor.a *= 0.7f;
            painter.fillColor = okColor;
            for (int i = 0; i < _points.Count; i++)
            {
                if (!_points[i].success) continue;
                DrawDot(painter, lay, _points[i]);
            }

            var ngColor = _theme.Error;
            ngColor.a *= 0.85f;
            painter.fillColor = ngColor;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].success) continue;
                DrawDot(painter, lay, _points[i]);
            }
        }

        void DrawDot(Painter2D painter, Layout lay, ToolStatsScatterPoint p)
        {
            float x = lay.XAt(p.chars);
            float y = lay.YAt((float)p.durationMs);
            painter.BeginPath();
            painter.Arc(new Vector2(x, y), DotRadius, 0f, 360f);
            painter.Fill();
        }

        /// <summary>X / Y それぞれ 0・中央・最大の 3 本を引く。</summary>
        void DrawGrid(Painter2D painter, Layout lay)
        {
            painter.strokeColor = _theme.OutlineVariant;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;

            for (int i = 0; i <= 2; i++)
            {
                float y = lay.y0 + lay.h * i * 0.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(lay.x0, y));
                painter.LineTo(new Vector2(lay.x0 + lay.w, y));
                painter.Stroke();

                float x = lay.x0 + lay.w * i * 0.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, lay.y0));
                painter.LineTo(new Vector2(x, lay.y0 + lay.h));
                painter.Stroke();
            }
        }

        /// <summary>描画とラベルで共有するプロット座標系を作る。サイズ未確定なら false。</summary>
        bool TryGetLayout(out Layout lay)
        {
            lay = default;
            if (_points == null || _points.Count == 0) return false;
            if (!TryGetPlotSize(out float w, out float h)) return false;

            float maxX = 0f;
            float maxY = 0f;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].chars > maxX) maxX = _points[i].chars;
                if (_points[i].durationMs > maxY) maxY = (float)_points[i].durationMs;
            }

            // 全点が同値 (0 を含む) でもゼロ除算しないよう 1 を下限にする。
            lay = new Layout
            {
                x0 = PadLeft,
                y0 = PadTop,
                w = w,
                h = h,
                maxX = maxX > 0f ? maxX : 1f,
                maxY = maxY > 0f ? maxY : 1f,
            };
            return true;
        }
    }
}
