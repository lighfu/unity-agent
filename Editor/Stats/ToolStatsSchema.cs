using System;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>ツール呼び出しの実行経路。</summary>
    internal enum ToolCallRoute
    {
        /// <summary>チャット / LLM 経由 (UnityAgentCore.ExecuteToolsAsync)。</summary>
        Chat = 0,
        /// <summary>MCP 経由 (HTTP / Bridge の両方を含む)。</summary>
        Mcp = 1,
        /// <summary>ツールコンソールからの手動実行。</summary>
        Console = 2,
    }

    /// <summary>ツール呼び出し 1 件の明細。</summary>
    [Serializable]
    internal class ToolStatsRecord
    {
        /// <summary>ツール名 (メソッド名)。</summary>
        public string toolName = "";
        /// <summary>実行経路。<see cref="ToolCallRoute"/> の int 値。</summary>
        public int route;
        /// <summary>例外・キャンセル・引数エラーなく完走したか。</summary>
        public bool success;
        /// <summary>所要時間 (ミリ秒、0 以上に丸め済み)。</summary>
        public int durationMs;
        /// <summary>引数の文字数。</summary>
        public int argChars;
        /// <summary>結果文字列の文字数。</summary>
        public int resultChars;
        /// <summary>呼び出し完了時刻 (UTC, Unix ミリ秒)。</summary>
        public long tsUnixMs;
    }

    /// <summary>日別集計の 1 ツール分。</summary>
    [Serializable]
    internal class ToolStatsDailyTool
    {
        public string toolName = "";
        public int calls;
        public int failures;
        public long totalDurationMs;
    }

    /// <summary>1 日分の集計。明細を捨てても長期の推移が残るように別途保持する。</summary>
    [Serializable]
    internal class ToolStatsDaily
    {
        /// <summary>ローカル日付 "yyyy-MM-dd"。</summary>
        public string date = "";
        public int totalCalls;
        public int successCalls;
        public int failureCalls;
        public int chatCalls;
        public int mcpCalls;
        public int consoleCalls;
        public long totalDurationMs;
        public long totalArgChars;
        public long totalResultChars;
        /// <summary>ツール別内訳。calls 降順で保持する (フラッシュ時に整列)。</summary>
        public List<ToolStatsDailyTool> tools = new List<ToolStatsDailyTool>();
    }

    /// <summary>ToolStats.json のルート。</summary>
    [Serializable]
    internal class ToolStatsRoot
    {
        /// <summary>スキーマバージョン。</summary>
        public int version = 1;
        /// <summary>明細。追記順 (古い → 新しい)。上限を超えたら先頭から捨てる。</summary>
        public List<ToolStatsRecord> records = new List<ToolStatsRecord>();
        /// <summary>日別集計。date 昇順。</summary>
        public List<ToolStatsDaily> daily = new List<ToolStatsDaily>();
        /// <summary>
        /// 上限超過で捨てた明細の累計件数。日別集計には残っているが明細からは消えた分。
        /// UI が「明細は直近 N 件のみ」と断るために使う。
        /// v1 の途中で足したフィールドなので、古いファイルには存在せず 0 として読まれる。
        /// </summary>
        public long droppedRecords;
    }
}
