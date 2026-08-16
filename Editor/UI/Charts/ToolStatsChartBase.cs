using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// ツール統計グラフの共通基底。Painter2D で描画し、テーマ色はコンストラクタで受け取る。
    /// resolvedStyle が未確定 (NaN / 0) の間は描画しないので、描画自体は安全に空振りする。
    /// ただし絶対配置のラベルは描画パスとは別に座標を決める必要があるため、
    /// GeometryChangedEvent でラベルを組み直して MarkDirtyRepaint する。
    /// </summary>
    internal abstract class ToolStatsChartBase : VisualElement, IMD3Themeable
    {
        /// <summary>プロット領域の余白。軸ラベルの場所。</summary>
        protected const float PadLeft = 44f;
        protected const float PadRight = 8f;
        protected const float PadTop = 8f;
        protected const float PadBottom = 20f;

        protected readonly MD3Theme _theme;

        /// <summary>直近に処理したジオメトリ。同じサイズでのラベル再構築を省く。</summary>
        Rect _lastGeometry;

        /// <summary>ラベル再構築中フラグ。子要素からのイベントで再入するのを防ぐ。</summary>
        bool _rebuilding;

        /// <summary>ラベルを置く絶対配置レイヤー。pickingMode は Ignore 済み。</summary>
        protected VisualElement LabelLayer { get; }

        /// <param name="theme">ウィンドウが ResolveTheme() で解決したテーマ。</param>
        /// <param name="height">要素の固定高さ (px)。</param>
        protected ToolStatsChartBase(MD3Theme theme, float height)
        {
            _theme = theme;

            style.height = height;
            style.flexGrow = 0;
            style.flexShrink = 0;
            style.marginTop = MD3Spacing.S;
            style.marginBottom = MD3Spacing.S;

            LabelLayer = new VisualElement();
            LabelLayer.pickingMode = PickingMode.Ignore;
            LabelLayer.style.position = Position.Absolute;
            LabelLayer.style.left = 0f;
            LabelLayer.style.top = 0f;
            LabelLayer.style.right = 0f;
            LabelLayer.style.bottom = 0f;
            Add(LabelLayer);

            // ハンドラの付け外しはしない (契約: 1 回だけ登録)。
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>テーマ再適用時に再描画する (色は _theme を持ち回るので取り直さない)。</summary>
        public void RefreshTheme()
        {
            MarkDirtyRepaint();
        }

        /// <summary>プロット領域のサイズを取得する。未確定なら false (呼び出し側は描画を諦める)。</summary>
        protected bool TryGetPlotSize(out float w, out float h)
        {
            w = 0f;
            h = 0f;

            float rw = resolvedStyle.width;
            float rh = resolvedStyle.height;
            if (float.IsNaN(rw) || float.IsNaN(rh) || rw <= 0f || rh <= 0f) return false;

            float pw = rw - PadLeft - PadRight;
            float ph = rh - PadTop - PadBottom;
            if (pw <= 0f || ph <= 0f) return false;

            w = pw;
            h = ph;
            return true;
        }

        /// <summary>角丸矩形を塗る。棒グラフの棒に使う。</summary>
        protected static void DrawRoundedRect(Painter2D painter, float x, float y, float w, float h, float r)
        {
            if (painter == null || w <= 0f || h <= 0f) return;

            // 半径が辺より大きいと自己交差した形になるので必ずクランプする。
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            if (r < 0f) r = 0f;

            painter.BeginPath();
            if (r <= 0.5f)
            {
                // 角丸不要なら単純な矩形にする (半径 0 の Arc は描けないため)。
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + w, y));
                painter.LineTo(new Vector2(x + w, y + h));
                painter.LineTo(new Vector2(x, y + h));
                painter.ClosePath();
                painter.Fill();
                return;
            }

            // UI Toolkit は Y 下向き。角度 0 度 = +X、90 度 = +Y (下)。
            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(x + w - r, y));
            painter.Arc(new Vector2(x + w - r, y + r), r, -90f, 0f);
            painter.LineTo(new Vector2(x + w, y + h - r));
            painter.Arc(new Vector2(x + w - r, y + h - r), r, 0f, 90f);
            painter.LineTo(new Vector2(x + r, y + h));
            painter.Arc(new Vector2(x + r, y + h - r), r, 90f, 180f);
            painter.LineTo(new Vector2(x, y + r));
            painter.Arc(new Vector2(x + r, y + r), r, 180f, 270f);
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>LabelLayer に絶対配置の Label を足す。left/top はプロット座標系ではなく要素座標系。</summary>
        protected Label AddLabel(string text, MD3TextStyle style, Color color, float left, float top)
        {
            var label = new Label(text ?? "");
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.left = left;
            label.style.top = top;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.fontSize = FontSizeOf(style);
            if (IsBold(style)) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = color;
            LabelLayer.Add(label);
            return label;
        }

        /// <summary>LabelLayer をクリアする。SetData の先頭で呼ぶ。</summary>
        protected void ClearLabels()
        {
            LabelLayer.Clear();
        }

        /// <summary>データが空のとき中央に M("データなし") を出す。</summary>
        protected void ShowNoData()
        {
            // _theme はウィンドウから必ず渡されるが、null でも文言だけは出せるようにしておく。
            Color c = _theme != null ? _theme.OnSurfaceVariant : Color.gray;
            var label = AddLabel(M("データなし"), MD3TextStyle.LabelSmall, c, 0f, 0f);
            label.style.right = 0f;
            label.style.bottom = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        /// <summary>
        /// ラベルを配置し直す。SetData と GeometryChangedEvent の双方から呼ばれる。
        /// 実装側は必ず ClearLabels() から始め、描画と同じ座標計算を使うこと。
        /// </summary>
        protected virtual void RebuildLabels()
        {
        }

        /// <summary>実データを描く。サイズとテーマの検証は基底で済ませてある。</summary>
        protected abstract void DrawChart(Painter2D painter, float plotW, float plotH);

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_theme == null) return;
            if (!TryGetPlotSize(out float w, out float h)) return;
            DrawChart(mgc.painter2D, w, h);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // GeometryChangedEvent はバブリングするので、LabelLayer に足した Label からも上がってくる。
            // 素通しすると「ラベル追加 → イベント → ラベル再構築」の無限ループになるため自分宛だけ処理する。
            if (evt.target != (object)this) return;
            if (evt.newRect == _lastGeometry) return;
            _lastGeometry = evt.newRect;
            if (_rebuilding) return;

            // サイズが確定して初めてラベル座標が決まる。描画は generateVisualContent 側で再実行される。
            _rebuilding = true;
            try
            {
                RebuildLabels();
            }
            finally
            {
                _rebuilding = false;
            }
            MarkDirtyRepaint();
        }

        /// <summary>MD3Text と同じ字送りを生の Label に与える。</summary>
        static int FontSizeOf(MD3TextStyle s)
        {
            switch (s)
            {
                case MD3TextStyle.DisplayLarge: return 40;
                case MD3TextStyle.DisplayMedium: return 32;
                case MD3TextStyle.DisplaySmall: return 28;
                case MD3TextStyle.HeadlineLarge: return 24;
                case MD3TextStyle.HeadlineMedium: return 20;
                case MD3TextStyle.HeadlineSmall: return 16;
                case MD3TextStyle.TitleLarge: return 18;
                case MD3TextStyle.TitleMedium: return 14;
                case MD3TextStyle.TitleSmall: return 12;
                case MD3TextStyle.Body: return 14;
                case MD3TextStyle.BodySmall: return 12;
                case MD3TextStyle.LabelLarge: return 13;
                case MD3TextStyle.LabelMedium: return 12;
                case MD3TextStyle.LabelSmall: return 11;
                case MD3TextStyle.LabelAnnotation: return 10;
                default: return 11;
            }
        }

        static bool IsBold(MD3TextStyle s)
        {
            switch (s)
            {
                case MD3TextStyle.HeadlineLarge:
                case MD3TextStyle.HeadlineMedium:
                case MD3TextStyle.HeadlineSmall:
                case MD3TextStyle.TitleLarge:
                case MD3TextStyle.TitleMedium:
                case MD3TextStyle.TitleSmall:
                    return true;
                default:
                    return false;
            }
        }
    }
}
