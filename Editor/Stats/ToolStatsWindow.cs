using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.UI;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// ツール呼び出し統計を表示する独立ウィンドウ。上部ナビゲーターの統計ボタンから開く。
    /// タブは持たず、期間フィルタ・KPI 行・4 種のグラフを 1 画面にスクロールで並べる。
    /// </summary>
    internal class ToolStatsWindow : EditorWindow
    {
        // ── グラフの取得件数 (契約で確定) ──
        private const int RankingTopN = 15;
        private const int CategoryTopN = 7;
        private const int ScatterMaxPoints = 400;

        /// <summary>自動更新のポーリング間隔 (ミリ秒)。</summary>
        private const long PollIntervalMs = 1000;

        private MD3Theme _theme;

        /// <summary>表示中の集計期間。既定は直近 7 日。</summary>
        private ToolStatsPeriod _period = ToolStatsPeriod.Last7Days;

        /// <summary>最後に描画したときの <see cref="ToolCallStats.Revision"/>。差分が出たら再描画する。</summary>
        private int _lastRevision;

        /// <summary>
        /// 自動更新のスケジュール。CreateGUI はドッキングし直すたびに走るので、
        /// ハンドルを持っておいて前回分を止めないとポーリングが多重登録される。
        /// </summary>
        private IVisualElementScheduledItem _pollItem;

        // ── KPI タイルの値ラベル (再描画で差し替える) ──
        private MD3Text _kpiTotalCalls;
        private MD3Text _kpiSuccessRate;
        private MD3Text _kpiAvgDuration;
        private MD3Text _kpiDistinctTools;

        // ── 本体 ──
        private MD3EmptyState _emptyState;
        private VisualElement _chartsRoot;
        private ToolStatsTimeSeriesChart _timeSeriesChart;
        private ToolStatsRankingChart _rankingChart;
        private ToolStatsCategoryChart _categoryChart;
        private ToolStatsScatterChart _scatterChart;

        // ── 母集団が明細 (保持上限あり) のグラフに出す注記 ──
        private MD3Text _timeSeriesNote;
        private MD3Text _scatterNote;

        // ── Open ──

        /// <summary>ウィンドウを開く (既に開いていれば前面に出す)。</summary>
        public static void Open()
        {
            var window = GetWindow<ToolStatsWindow>();
            window.titleContent = new GUIContent(M("ツール呼び出し統計"));
            window.minSize = new Vector2(720, 520);
            window.Show();
        }

        // ── Lifecycle ──

        internal void CreateGUI()
        {
            // rootVisualElement は CreateGUI をまたいで生き残るので、前回のポーリングを先に止める。
            if (_pollItem != null)
            {
                _pollItem.Pause();
                _pollItem = null;
            }

            rootVisualElement.Clear();

            _theme = ResolveTheme();
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !rootVisualElement.styleSheets.Contains(themeSheet))
                rootVisualElement.styleSheets.Add(themeSheet);
            if (compSheet != null && !rootVisualElement.styleSheets.Contains(compSheet))
                rootVisualElement.styleSheets.Add(compSheet);
            _theme.ApplyTo(rootVisualElement);
            rootVisualElement.style.flexGrow = 1;

            BuildUI();
            RefreshData();

            // 開いたまま統計が増えても追随できるようにポーリングする。
            // ToolCallStats 側からイベントを飛ばさない (Record はワーカースレッドからも呼ばれるため)。
            _pollItem = rootVisualElement.schedule.Execute(() =>
            {
                if (ToolCallStats.Revision != _lastRevision) RefreshData();
            }).Every(PollIntervalMs);
        }

        private void OnDisable()
        {
            // ウィンドウを閉じた・タブを切り替えた後もポーリングが回り続けないように止める。
            if (_pollItem != null)
            {
                _pollItem.Pause();
                _pollItem = null;
            }
        }

        // ── Build ──

        private void BuildUI()
        {
            rootVisualElement.Add(BuildHeader());
            rootVisualElement.Add(BuildKpiRow());
            rootVisualElement.Add(BuildBody());
            rootVisualElement.Add(BuildFooter());
        }

        /// <summary>タイトル・期間フィルタ・更新/リセットボタンを並べたヘッダー行。</summary>
        private VisualElement BuildHeader()
        {
            var row = new MD3Row(MD3Spacing.S);
            row.style.flexShrink = 0;
            row.style.paddingLeft = MD3Spacing.M;
            row.style.paddingRight = MD3Spacing.M;
            row.style.paddingTop = MD3Spacing.M;
            row.style.paddingBottom = MD3Spacing.M;

            row.Add(new MD3SectionLabel(M("ツール呼び出し統計")));
            row.Add(new MD3Spacer());

            var periods = new[] { M("今日"), M("7 日"), M("30 日"), M("全期間") };
            row.Add(new MD3SegmentedButton(periods, (int)_period, OnPeriodChanged));

            var refreshBtn = new MD3IconButton(MD3Icon.Refresh, MD3IconButtonStyle.Standard,
                MD3IconButtonSize.Small);
            refreshBtn.tooltip = M("更新");
            refreshBtn.clicked += RefreshData;
            row.Add(refreshBtn);

            var resetBtn = new MD3IconButton(MD3Icon.Delete, MD3IconButtonStyle.Standard,
                MD3IconButtonSize.Small);
            resetBtn.tooltip = M("統計をリセット");
            resetBtn.clicked += OnResetClicked;
            row.Add(resetBtn);

            return row;
        }

        /// <summary>概況 4 件を並べた KPI 行。値ラベルはフィールドに控えて再描画で書き換える。</summary>
        private VisualElement BuildKpiRow()
        {
            var row = new MD3Row(MD3Spacing.S);
            row.style.flexShrink = 0;
            row.style.paddingLeft = MD3Spacing.M;
            row.style.paddingRight = MD3Spacing.M;
            row.style.paddingBottom = MD3Spacing.M;

            row.Add(BuildKpiTile(M("総呼び出し数"), out _kpiTotalCalls));
            row.Add(BuildKpiTile(M("成功率"), out _kpiSuccessRate));
            row.Add(BuildKpiTile(M("平均所要時間"), out _kpiAvgDuration));
            row.Add(BuildKpiTile(M("対象ツール数"), out _kpiDistinctTools));

            return row;
        }

        private VisualElement BuildKpiTile(string label, out MD3Text valueText)
        {
            var tile = new MD3Column(MD3Spacing.XXS);
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.paddingLeft = MD3Spacing.M;
            tile.style.paddingRight = MD3Spacing.M;
            tile.style.paddingTop = MD3Spacing.M;
            tile.style.paddingBottom = MD3Spacing.M;
            tile.style.backgroundColor = _theme.SurfaceContainerHigh;
            tile.style.borderTopLeftRadius = MD3Radius.M;
            tile.style.borderTopRightRadius = MD3Radius.M;
            tile.style.borderBottomLeftRadius = MD3Radius.M;
            tile.style.borderBottomRightRadius = MD3Radius.M;

            valueText = new MD3Text("0", MD3TextStyle.HeadlineSmall, _theme.OnSurface);
            tile.Add(valueText);
            tile.Add(new MD3Text(label, MD3TextStyle.LabelSmall, _theme.OnSurfaceVariant));

            return tile;
        }

        /// <summary>空表示と 4 グラフを載せるスクロール領域。</summary>
        private VisualElement BuildBody()
        {
            var scroll = new MD3ScrollColumn(MD3Spacing.M, MD3Spacing.M);
            scroll.style.flexGrow = 1;

            _emptyState = new MD3EmptyState(
                M("まだ記録がありません"),
                M("ツールを実行すると、ここに呼び出しの統計が表示されます。"),
                MD3Icon.BarChart);
            _emptyState.style.display = DisplayStyle.None;
            scroll.Add(_emptyState);

            _chartsRoot = new MD3Column(MD3Spacing.M);
            scroll.Add(_chartsRoot);

            _timeSeriesChart = new ToolStatsTimeSeriesChart(_theme);
            _rankingChart = new ToolStatsRankingChart(_theme);
            _categoryChart = new ToolStatsCategoryChart(_theme);
            _scatterChart = new ToolStatsScatterChart(_theme);

            _timeSeriesNote = CreateChartNote();
            _scatterNote = CreateChartNote();

            AddChartSection(M("時系列の呼び出し数"), _timeSeriesNote, _timeSeriesChart, true);
            AddChartSection(M("ツール別ランキング"), null, _rankingChart, true);
            AddChartSection(M("カテゴリ別内訳"), null, _categoryChart, true);
            AddChartSection(M("文字数と所要時間"), _scatterNote, _scatterChart, false);

            return scroll;
        }

        private void AddChartSection(string title, MD3Text note, VisualElement chart, bool divider)
        {
            _chartsRoot.Add(new MD3SectionLabel(title));
            if (note != null) _chartsRoot.Add(note);
            _chartsRoot.Add(chart);
            if (divider) _chartsRoot.Add(new MD3Divider(0f));
        }

        /// <summary>
        /// グラフ見出しの下に出す注記ラベル。母集団が明細 (保持上限あり) のグラフに、
        /// 何件を元にしているかを表示するために使う。初期状態は非表示。
        /// </summary>
        private MD3Text CreateChartNote()
        {
            var note = new MD3Text("", MD3TextStyle.LabelSmall, _theme.OnSurfaceVariant);
            note.style.display = DisplayStyle.None;
            return note;
        }

        /// <summary>記録の有効/無効を切り替えるフッター行。</summary>
        private VisualElement BuildFooter()
        {
            var row = new MD3Row(MD3Spacing.S);
            row.style.flexShrink = 0;
            row.style.paddingLeft = MD3Spacing.M;
            row.style.paddingRight = MD3Spacing.M;
            row.style.paddingTop = MD3Spacing.S;
            row.style.paddingBottom = MD3Spacing.S;

            var chip = new MD3Chip(M("統計の記録を有効にする"), AgentSettings.ToolStatsEnabled);
            chip.toggled += v =>
            {
                AgentSettings.ToolStatsEnabled = v;
                ToolCallStats.RefreshSettingsCache();
            };
            row.Add(chip);

            return row;
        }

        // ── Data ──

        /// <summary>
        /// 統計を読み直して KPI とグラフを描き直す。スナップショットは 1 回だけ取り、
        /// KPI と 4 グラフで使い回す (途中で記録が増えても数字がずれないように)。
        /// </summary>
        private void RefreshData()
        {
            if (_theme == null) return;

            var snapshot = ToolStatsQuery.Capture(_period);
            _lastRevision = snapshot.revision;

            var ov = ToolStatsQuery.GetOverview(snapshot);
            _kpiTotalCalls.Text = FormatCount(ov.totalCalls);
            // totalCalls が int.MaxValue に近いと successCalls * 100 が桁溢れするので long で計算する。
            int successRate = (int)((long)ov.successCalls * 100 / Mathf.Max(1, ov.totalCalls));
            _kpiSuccessRate.Text = FormatCount(successRate) + "%";
            _kpiAvgDuration.Text = FormatDuration(ov.avgDurationMs);
            _kpiDistinctTools.Text = FormatCount(ov.distinctTools);

            bool hasData = ov.totalCalls > 0;
            _emptyState.style.display = hasData ? DisplayStyle.None : DisplayStyle.Flex;
            _chartsRoot.style.display = hasData ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasData) return;

            var ranking = ToolStatsQuery.GetRanking(snapshot);

            _timeSeriesChart.SetData(ToolStatsQuery.GetTimeSeries(snapshot));
            _rankingChart.SetData(ToolStatsQuery.TakeTop(ranking, RankingTopN));
            _categoryChart.SetData(ToolStatsQuery.GetCategoryBreakdown(ranking, CategoryTopN));
            _scatterChart.SetData(ToolStatsQuery.GetScatter(snapshot, ScatterMaxPoints));

            // 明細ベースのグラフは保持上限で古い分が落ちるので、母集団の件数を明示する。
            int recordCount = snapshot.records.Count;
            SetChartNote(_timeSeriesNote, ToolStatsQuery.IsTimeSeriesFromRecords(_period), recordCount);
            SetChartNote(_scatterNote, true, recordCount);
        }

        /// <summary>明細ベースのグラフに「直近 N 件の明細に基づく」注記を出す (不要なら隠す)。</summary>
        private static void SetChartNote(MD3Text note, bool visible, int recordCount)
        {
            if (note == null) return;
            if (!visible)
            {
                note.style.display = DisplayStyle.None;
                return;
            }

            note.Text = string.Format(
                M("この期間の直近 {0} 件の明細に基づく (保持上限を超えた古い明細は含まれません)"),
                FormatCount(recordCount));
            note.style.display = DisplayStyle.Flex;
        }

        private void OnPeriodChanged(int index)
        {
            _period = (ToolStatsPeriod)index;
            RefreshData();
        }

        private void OnResetClicked()
        {
            bool ok = EditorUtility.DisplayDialog(
                M("統計をリセット"),
                M("統計データをすべて削除しますか？この操作は元に戻せません。"),
                M("削除する"),
                M("キャンセル"));
            if (!ok) return;

            ToolCallStats.ResetAll();
            RefreshData();
        }

        // ── Utility ──

        /// <summary>件数を 3 桁区切りで整形する (1234 → "1,234")。</summary>
        private static string FormatCount(int value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 所要時間を読みやすい単位で整形する。1 秒未満は ms、それ以上は s に切り替える。
        /// </summary>
        private static string FormatDuration(double ms)
        {
            if (ms >= 1000.0)
                return (ms / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + M("s");
            return FormatCount(Mathf.RoundToInt((float)ms)) + M("ms");
        }

        private static MD3Theme ResolveTheme()
        {
            switch (AgentSettings.ThemeMode)
            {
                case 1: return MD3Theme.Dark();
                case 2: return MD3Theme.Light();
                case 3: return AgentSettings.BuildCustomTheme();
                default: return MD3Theme.Auto();
            }
        }
    }
}
