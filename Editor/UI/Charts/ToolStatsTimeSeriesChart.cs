using System;
using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>時系列の呼び出し数を折れ線 + 面で描く。失敗数は赤の折れ線で重ねる。</summary>
    internal sealed class ToolStatsTimeSeriesChart : ToolStatsChartBase
    {
        /// <summary>X 軸ラベルの最大表示数。これを超えたら等間隔に間引く。</summary>
        const int MaxAxisLabels = 8;

        /// <summary>プロット座標の計算結果。描画とラベル配置で同じ式を使うために 1 箇所へ集約する。</summary>
        struct Layout
        {
            public float x0;
            public float y0;
            public float w;
            public float h;
            public int count;
            public int max;

            /// <summary>i 番目の点の X 座標。点が 1 つだけなら中央に置く。</summary>
            public float XAt(int i)
            {
                if (count <= 1) return x0 + w * 0.5f;
                return x0 + w * i / (count - 1);
            }

            /// <summary>値 v の Y 座標。max は 1 以上が保証されている。</summary>
            public float YAt(float v)
            {
                float t = Mathf.Clamp01(v / max);
                return y0 + h * (1f - t);
            }
        }

        List<ToolStatsTimePoint> _points;

        /// <param name="theme">ウィンドウのテーマ。</param>
        internal ToolStatsTimeSeriesChart(MD3Theme theme) : base(theme, 180f)
        {
        }

        /// <summary>点列を差し替える。null / 空なら M("データなし") を表示する。</summary>
        internal void SetData(List<ToolStatsTimePoint> points)
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

            // Y 軸は 0 と最大値の 2 個だけ。左余白 (PadLeft) の中に右寄せで置く。
            var yMax = AddLabel(lay.max.ToString(), MD3TextStyle.LabelSmall,
                _theme.OnSurfaceVariant, 0f, lay.YAt(lay.max) - 6f);
            yMax.style.width = PadLeft - 6f;
            yMax.style.unityTextAlign = TextAnchor.UpperRight;

            var yZero = AddLabel("0", MD3TextStyle.LabelSmall,
                _theme.OnSurfaceVariant, 0f, lay.YAt(0f) - 6f);
            yZero.style.width = PadLeft - 6f;
            yZero.style.unityTextAlign = TextAnchor.UpperRight;

            // X 軸ラベルは点数が多いと潰れるので最大 8 個へ間引く。
            float labelTop = lay.y0 + lay.h + 3f;
            int shown = Mathf.Min(lay.count, MaxAxisLabels);
            int lastIndex = -1;
            for (int k = 0; k < shown; k++)
            {
                int i = shown <= 1
                    ? 0
                    : Mathf.RoundToInt(k * (lay.count - 1) / (float)(shown - 1));
                if (i == lastIndex) continue;
                lastIndex = i;

                string text = _points[i].label ?? "";
                var lbl = AddLabel(text, MD3TextStyle.LabelSmall,
                    _theme.OnSurfaceVariant, lay.XAt(i) - 16f, labelTop);
                lbl.style.width = 32f;
                lbl.style.unityTextAlign = TextAnchor.UpperCenter;
                lbl.style.whiteSpace = WhiteSpace.NoWrap;
                lbl.style.overflow = Overflow.Hidden;
            }
        }

        protected override void DrawChart(Painter2D painter, float plotW, float plotH)
        {
            if (_points == null || _points.Count == 0) return;
            if (!TryGetLayout(out Layout lay)) return;

            DrawGrid(painter, lay);
            DrawArea(painter, lay);
            DrawSeries(painter, lay);
        }

        /// <summary>0 / 1/4 / 1/2 / 3/4 / max の水平グリッドを引く。</summary>
        void DrawGrid(Painter2D painter, Layout lay)
        {
            painter.strokeColor = _theme.OutlineVariant;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;

            for (int i = 0; i <= 4; i++)
            {
                float y = lay.y0 + lay.h * i / 4f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(lay.x0, y));
                painter.LineTo(new Vector2(lay.x0 + lay.w, y));
                painter.Stroke();
            }
        }

        /// <summary>折れ線の下を薄く塗る。点が 1 つのときは面にならないので描かない。</summary>
        void DrawArea(Painter2D painter, Layout lay)
        {
            if (lay.count < 2) return;

            var fill = _theme.Primary;
            fill.a *= 0.18f;
            painter.fillColor = fill;

            float bottom = lay.y0 + lay.h;
            painter.BeginPath();
            painter.MoveTo(new Vector2(lay.XAt(0), bottom));
            for (int i = 0; i < lay.count; i++)
                painter.LineTo(new Vector2(lay.XAt(i), lay.YAt(_points[i].total)));
            painter.LineTo(new Vector2(lay.XAt(lay.count - 1), bottom));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>呼び出し数の線と、失敗が 1 件でもあれば失敗数の線を重ねる。</summary>
        void DrawSeries(Painter2D painter, Layout lay)
        {
            bool hasFailure = false;
            for (int i = 0; i < lay.count; i++)
            {
                if (_points[i].failures > 0) { hasFailure = true; break; }
            }

            if (lay.count == 1)
            {
                // 点が 1 つだけのときは線を引けないので点を打つ。
                painter.fillColor = _theme.Primary;
                painter.BeginPath();
                painter.Arc(new Vector2(lay.XAt(0), lay.YAt(_points[0].total)), 3f, 0f, 360f);
                painter.Fill();

                if (hasFailure)
                {
                    painter.fillColor = _theme.Error;
                    painter.BeginPath();
                    painter.Arc(new Vector2(lay.XAt(0), lay.YAt(_points[0].failures)), 3f, 0f, 360f);
                    painter.Fill();
                }
                return;
            }

            painter.strokeColor = _theme.Primary;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            for (int i = 0; i < lay.count; i++)
            {
                var p = new Vector2(lay.XAt(i), lay.YAt(_points[i].total));
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }
            painter.Stroke();

            if (!hasFailure) return;

            painter.strokeColor = _theme.Error;
            painter.lineWidth = 1.5f;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            for (int i = 0; i < lay.count; i++)
            {
                var p = new Vector2(lay.XAt(i), lay.YAt(_points[i].failures));
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }
            painter.Stroke();
        }

        /// <summary>描画とラベルで共有するプロット座標系を作る。サイズ未確定なら false。</summary>
        bool TryGetLayout(out Layout lay)
        {
            lay = default;
            if (_points == null || _points.Count == 0) return false;
            if (!TryGetPlotSize(out float w, out float h)) return false;

            int max = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].total > max) max = _points[i].total;
                if (_points[i].failures > max) max = _points[i].failures;
            }

            lay = new Layout
            {
                x0 = PadLeft,
                y0 = PadTop,
                w = w,
                h = h,
                count = _points.Count,
                max = NiceCeil(max),
            };
            return true;
        }

        /// <summary>1・2・5 × 10^n の刻みで切り上げる。0 以下でも必ず 1 以上を返す (ゼロ除算避け)。</summary>
        static int NiceCeil(int value)
        {
            if (value <= 1) return 1;

            double exp = Math.Floor(Math.Log10(value));
            double pow = Math.Pow(10.0, exp);
            double f = value / pow;
            double m = f <= 1.0 ? 1.0 : f <= 2.0 ? 2.0 : f <= 5.0 ? 5.0 : 10.0;
            double nice = m * pow;
            if (nice >= int.MaxValue) return int.MaxValue;
            return Mathf.Max(1, (int)Math.Round(nice));
        }
    }
}
