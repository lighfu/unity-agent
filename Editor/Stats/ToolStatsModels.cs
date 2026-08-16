namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>統計の集計期間。</summary>
    internal enum ToolStatsPeriod
    {
        /// <summary>今日 (ローカル日付)。明細から時間別に集計する。</summary>
        Today = 0,
        /// <summary>直近 7 日 (今日を含む)。日別集計から。</summary>
        Last7Days = 1,
        /// <summary>直近 30 日 (今日を含む)。日別集計から。</summary>
        Last30Days = 2,
        /// <summary>全期間。日別集計から (時系列は末尾 90 日)。</summary>
        All = 3,
    }

    /// <summary>KPI 行に出す概況。</summary>
    internal struct ToolStatsOverview
    {
        public int totalCalls;
        public int successCalls;
        public int failureCalls;
        public double avgDurationMs;
        public int chatCalls;
        public int mcpCalls;
        public int consoleCalls;
        public int distinctTools;
    }

    /// <summary>時系列グラフの 1 点。</summary>
    internal struct ToolStatsTimePoint
    {
        /// <summary>軸ラベル。Today は "0"〜"23"、それ以外は "MM/dd"。</summary>
        public string label;
        public int total;
        public int failures;
    }

    /// <summary>ランキンググラフの 1 行。</summary>
    internal struct ToolStatsRankItem
    {
        public string toolName;
        public int calls;
        public int failures;
        public double avgDurationMs;
    }

    /// <summary>カテゴリ内訳の 1 スライス。</summary>
    internal struct ToolStatsCategorySlice
    {
        public string category;
        public int calls;
    }

    /// <summary>散布図の 1 点。</summary>
    internal struct ToolStatsScatterPoint
    {
        public string toolName;
        /// <summary>引数の文字数 + 結果の文字数。</summary>
        public int chars;
        public double durationMs;
        public bool success;
    }
}
