using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// ツール呼び出し統計の記録基盤。3 経路 (chat / mcp / console) から呼ばれる。
    /// Record はワーカースレッドからも呼ばれ得るため、Unity API・UI Toolkit・ディスク I/O を一切行わない。
    /// 永続化は beforeAssemblyReload / quitting / 周期フラッシュ (<see cref="FlushIntervalSec"/>) で
    /// メインスレッドから行う。1 回のフラッシュで明細全件を書き直すので、周期は長めにとってある。
    /// </summary>
    [InitializeOnLoad]
    internal static class ToolCallStats
    {
        // ─── 制限値 ───

        internal const int CurrentSchemaVersion = 1;
        internal const int DefaultMaxRecords = 5000;
        internal const int MinMaxRecords = 500;
        internal const int MaxMaxRecords = 50000;
        internal const int MaxDailyDays = 730;
        internal const int MaxToolsPerDay = 500;

        /// <summary>
        /// フラッシュ周期 (秒)。1 回のフラッシュで明細と日別集計を丸ごとシリアライズし直すため、
        /// 設定の再読み込み周期よりも長くとる。
        /// </summary>
        private const double FlushIntervalSec = 120.0;

        /// <summary>設定キャッシュの再読み込み周期 (秒)。ディスクを触らないので短くてよい。</summary>
        private const double SettingsRefreshIntervalSec = 30.0;

        /// <summary>件数によるフラッシュしきい値の下限。記録が少ない環境でも取りこぼしを減らす。</summary>
        private const int MinFlushPendingThreshold = 200;

        /// <summary>
        /// 未フラッシュ件数がこれに達したら周期を待たずに書き出す。
        /// 1 回のフラッシュで全件を書き直すので、固定値にすると上限件数を上げたときに
        /// 「毎回 N 件書くために 5 万件シリアライズする」形になる。上限件数の 1/10 に比例させ、
        /// 明細が一巡するまでのフラッシュ回数が上限件数によらず一定になるようにする。
        /// </summary>
        private static int FlushPendingThreshold
        {
            get
            {
                int max = _maxRecordsCache;
                if (max < MinMaxRecords) max = MinMaxRecords;
                int threshold = max / 10;
                return threshold < MinFlushPendingThreshold ? MinFlushPendingThreshold : threshold;
            }
        }

        // ─── 状態 ───

        /// <summary>
        /// _records / _dailyByDate / _dailyToolsByDate / _dirty / _pendingSinceFlush / _droppedRecords を
        /// 守る唯一のロック。書き込みは必ずこのロック内で行う。
        /// </summary>
        private static readonly object _gate = new object();

        /// <summary>明細。追記順 (古い → 新しい)。</summary>
        private static readonly List<ToolStatsRecord> _records = new List<ToolStatsRecord>();

        /// <summary>日別集計。キーはローカル日付 "yyyy-MM-dd"。</summary>
        private static readonly Dictionary<string, ToolStatsDaily> _dailyByDate =
            new Dictionary<string, ToolStatsDaily>(StringComparer.Ordinal);

        /// <summary>
        /// 日別集計のツール別内訳の副インデックス。ToolStatsDaily.tools (List) は
        /// フラッシュ直前にこのインデックスから作り直すので、記録時は List を触らない。
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, ToolStatsDailyTool>> _dailyToolsByDate =
            new Dictionary<string, Dictionary<string, ToolStatsDailyTool>>(StringComparer.Ordinal);

        /// <summary>
        /// 未保存の変更があるか。書き込みは _gate 内だが、OnEditorUpdate / Flush の早期 return は
        /// ロックを取らずに読む。ワーカースレッドの書き込みがメインスレッドから見えないと
        /// beforeAssemblyReload のフラッシュが空振りして記録を丸ごと失うので volatile。
        /// </summary>
        private static volatile bool _dirty;

        /// <summary>
        /// 前回フラッシュ以降の記録件数。_dirty と同様にロック外 (OnEditorUpdate) から読むので volatile。
        /// 加算は _gate 内でしか行わないため ++ でも失われない。
        /// </summary>
        private static volatile int _pendingSinceFlush;

        /// <summary>
        /// 上限超過で捨てた明細の累計件数。保存ファイルにも持ち回るので Editor を再起動しても続く。
        /// 読み書きとも _gate 内で行う (long は 32bit 環境で非アトミックなため)。
        /// </summary>
        private static long _droppedRecords;

        /// <summary>
        /// 上限超過で捨てた明細の累計件数。0 より大きければ、明細は全期間ではなく直近分だけを表す。
        /// </summary>
        internal static long DroppedRecordCount
        {
            get { lock (_gate) { return _droppedRecords; } }
        }

        // volatile フィールドを Interlocked に渡すと CS0420 が出るが、Interlocked 自体が
        // 完全なメモリバリアを張るので volatile 指定と併用しても安全。読み手 (UI) 側の
        // 可視性のために volatile は残す。
#pragma warning disable 420
        private static volatile int _revision;

        /// <summary>記録が変化するたびに増える版数。UI はこれをポーリングして再描画を判断する。</summary>
        internal static int Revision => _revision;

        /// <summary>設定のキャッシュ。ワーカースレッドから読むので volatile。</summary>
        private static volatile bool _enabledCache = true;
        private static volatile int _maxRecordsCache = DefaultMaxRecords;

        /// <summary>統計記録が有効か (AgentSettings のキャッシュ値。ワーカースレッドから読んで安全)。</summary>
        internal static bool IsEnabled => _enabledCache;

        /// <summary>日付キーのキャッシュ。1 日 1 回しか文字列を作らないためのもの (ロック内で触る)。</summary>
        private static string _cachedDateKey;
        private static DateTime _cachedLocalDay;

        private static double _lastFlushTime;
        private static double _lastSettingsRefreshTime;

        /// <summary>
        /// Record 内で例外が出たときのメッセージ。Record は Debug.* を呼べない (ワーカースレッド) ので、
        /// ここに置いてメインスレッドの OnEditorUpdate が初回だけ警告に落とす。
        /// </summary>
        private static volatile string _recordFailure;
        private static bool _recordFailureLogged;

        static ToolCallStats()
        {
            RefreshSettingsCache();
            LoadFromDisk();
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
            EditorApplication.update += OnEditorUpdate;
        }

        // ─── 記録 ───

        /// <summary>
        /// ツール呼び出しを 1 件記録する。スレッドセーフ。1 呼び出しあたり 1ms 未満で返ること。
        /// durationMs は 0 未満なら 0、int.MaxValue 超過は int.MaxValue に丸める。
        /// toolName が null/空なら何もしない。IsEnabled が false なら何もしない。
        /// </summary>
        internal static void Record(string toolName, ToolCallRoute route, bool success,
            double durationMs, int argChars, int resultChars)
        {
            if (!_enabledCache) return;
            if (string.IsNullOrEmpty(toolName)) return;

            int ms;
            if (double.IsNaN(durationMs) || durationMs <= 0.0) ms = 0;
            else if (durationMs >= int.MaxValue) ms = int.MaxValue;
            else ms = (int)durationMs;

            if (argChars < 0) argChars = 0;
            if (resultChars < 0) resultChars = 0;

            DateTime now = DateTime.Now;
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                lock (_gate)
                {
                    var rec = new ToolStatsRecord
                    {
                        toolName = toolName,
                        route = (int)route,
                        success = success,
                        durationMs = ms,
                        argChars = argChars,
                        resultChars = resultChars,
                        tsUnixMs = ts,
                    };
                    _records.Add(rec);

                    int max = _maxRecordsCache;
                    if (max < MinMaxRecords) max = MinMaxRecords;
                    if (_records.Count > max)
                    {
                        // 毎回 1 件ずつ削ると O(n) のシフトが毎回走るので、10% 分をまとめて捨てる。
                        // そのぶん件数は max と max*9/10 の間を往復するが、捨てた累計を
                        // _droppedRecords に積むので「明細は直近 N 件のみ」と UI が断れる。
                        int drop = _records.Count - max * 9 / 10;
                        _records.RemoveRange(0, drop);
                        _droppedRecords += drop;
                    }

                    string key = DateKeyLocked(now);
                    ToolStatsDaily daily;
                    Dictionary<string, ToolStatsDailyTool> toolIndex;
                    if (!_dailyByDate.TryGetValue(key, out daily))
                    {
                        daily = new ToolStatsDaily { date = key };
                        toolIndex = new Dictionary<string, ToolStatsDailyTool>(StringComparer.Ordinal);
                        _dailyByDate[key] = daily;
                        _dailyToolsByDate[key] = toolIndex;
                        TrimDailyLocked();
                    }
                    else if (!_dailyToolsByDate.TryGetValue(key, out toolIndex))
                    {
                        toolIndex = new Dictionary<string, ToolStatsDailyTool>(StringComparer.Ordinal);
                        _dailyToolsByDate[key] = toolIndex;
                    }

                    daily.totalCalls++;
                    if (success) daily.successCalls++;
                    else daily.failureCalls++;
                    switch (route)
                    {
                        case ToolCallRoute.Chat: daily.chatCalls++; break;
                        case ToolCallRoute.Mcp: daily.mcpCalls++; break;
                        default: daily.consoleCalls++; break;
                    }
                    daily.totalDurationMs += ms;
                    daily.totalArgChars += argChars;
                    daily.totalResultChars += resultChars;

                    ToolStatsDailyTool dt;
                    if (!toolIndex.TryGetValue(toolName, out dt))
                    {
                        dt = new ToolStatsDailyTool { toolName = toolName };
                        toolIndex[toolName] = dt;
                    }
                    dt.calls++;
                    if (!success) dt.failures++;
                    dt.totalDurationMs += ms;

                    _dirty = true;
                    _pendingSinceFlush++;
                }
            }
            catch (Exception ex)
            {
                // 統計はあくまで付随機能。記録の失敗でツール実行そのものを壊さないため握りつぶす。
                // ワーカースレッドから Debug.* は呼べないので、メインスレッドで 1 度だけ警告する。
                _recordFailure = ex.Message;
                return;
            }

            Interlocked.Increment(ref _revision);
        }
#pragma warning restore 420

        // ─── フラッシュ ───

        private static void OnEditorUpdate()
        {
            string failure = _recordFailure;
            if (failure != null && !_recordFailureLogged)
            {
                _recordFailureLogged = true;
                Debug.LogWarning(
                    $"[UnityAgent] ToolCallStats.Record failed: {failure} (further failures are not logged)");
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastSettingsRefreshTime >= SettingsRefreshIntervalSec)
            {
                _lastSettingsRefreshTime = now;
                RefreshSettingsCache();
            }

            if (!_dirty)
            {
                // 変更が無い間は最終フラッシュ時刻を進めておく。こうしないと、しばらく
                // 記録が無かった後の 1 件目で毎回ディスク書き込みが走ってしまう。
                _lastFlushTime = now;
                return;
            }

            if (_pendingSinceFlush >= FlushPendingThreshold || now - _lastFlushTime >= FlushIntervalSec)
                Flush();
        }

        /// <summary>
        /// 未保存の変更があればディスクに書き出す。メインスレッド専用。
        /// ロック内で行うのは値のコピーだけで、整列・切り詰め・シリアライズ・I/O はロック外で行う。
        /// </summary>
        internal static void Flush()
        {
            if (!_dirty) return;

            List<ToolStatsRecord> records;
            List<ToolStatsDaily> daily;
            long dropped;
            lock (_gate)
            {
                if (!_dirty) return;
                // ToolStatsRecord は生成後に書き換えないので、参照のコピーで足りる。
                records = new List<ToolStatsRecord>(_records);
                daily = CollectDailyCopiesLocked(null, null);
                dropped = _droppedRecords;
                _dirty = false;
                _pendingSinceFlush = 0;
            }

            FinishDailyList(daily);
            var root = new ToolStatsRoot
            {
                version = CurrentSchemaVersion,
                records = records,
                daily = daily,
                droppedRecords = dropped,
            };

            _lastFlushTime = EditorApplication.timeSinceStartup;
            // I/O はロックの外で行う (ワーカースレッドの Record を止めないため)。
            ToolStatsStore.Save(root);
        }

        /// <summary>明細・日別集計・保存ファイルをすべて消す。メインスレッド専用。</summary>
        internal static void ResetAll()
        {
            lock (_gate)
            {
                _records.Clear();
                _dailyByDate.Clear();
                _dailyToolsByDate.Clear();
                _cachedDateKey = null;
                _cachedLocalDay = default(DateTime);
                _droppedRecords = 0;
                _dirty = false;
                _pendingSinceFlush = 0;
            }

            ToolStatsStore.DeleteFile();
#pragma warning disable 420
            Interlocked.Increment(ref _revision);
#pragma warning restore 420
        }

        // ─── スナップショット ───

        /// <summary>保持している明細すべてのスナップショット (古い → 新しい)。ロック内でコピーした新しいリストを返す。</summary>
        internal static List<ToolStatsRecord> SnapshotRecords()
        {
            lock (_gate)
            {
                // ToolStatsRecord は生成後に書き換えないので、参照のコピーで足りる。
                return new List<ToolStatsRecord>(_records);
            }
        }

        /// <summary>
        /// fromLocalTime 以降の明細のスナップショット (古い → 新しい)。
        /// <see cref="DateTime.MinValue"/> を渡すと絞り込まず全件を返す。
        /// 明細は追記順なので、先頭から境界を 1 度探して以降をまとめて複製する。
        /// </summary>
        internal static List<ToolStatsRecord> SnapshotRecords(DateTime fromLocalTime)
        {
            if (fromLocalTime == DateTime.MinValue) return SnapshotRecords();

            DateTime utc = fromLocalTime.Kind == DateTimeKind.Utc
                ? fromLocalTime
                : fromLocalTime.ToUniversalTime();
            long fromUnixMs = new DateTimeOffset(utc).ToUnixTimeMilliseconds();

            lock (_gate)
            {
                int start = 0;
                while (start < _records.Count && _records[start].tsUnixMs < fromUnixMs) start++;
                if (start == 0) return new List<ToolStatsRecord>(_records);
                return _records.GetRange(start, _records.Count - start);
            }
        }

        /// <summary>
        /// 全期間の日別集計のスナップショット (date 昇順)。互換のために残している薄いラッパで、
        /// 最大 <see cref="MaxDailyDays"/> 日 × <see cref="MaxToolsPerDay"/> ツール分を複製する。
        /// 表示に必要な期間が決まっているなら <see cref="SnapshotDaily(string,string)"/> を使うこと。
        /// </summary>
        internal static List<ToolStatsDaily> SnapshotDaily()
        {
            return SnapshotDaily(null, null);
        }

        /// <summary>
        /// 指定期間の日別集計のスナップショット (date 昇順)。
        /// fromDateKey / toDateKey は <see cref="DateKey"/> が返す "yyyy-MM-dd" 形式で、両端を含む。
        /// null または空文字はその側を無制限として扱う。
        /// ロック内で行うのは値のコピーだけで、ツール別内訳の整列と切り詰めはロック外で行う。
        /// 返す要素はすべて複製なので、呼び出し側が書き換えても内部状態には影響しない。
        /// </summary>
        internal static List<ToolStatsDaily> SnapshotDaily(string fromDateKey, string toDateKey)
        {
            List<ToolStatsDaily> list;
            lock (_gate)
            {
                list = CollectDailyCopiesLocked(fromDateKey, toDateKey);
            }
            FinishDailyList(list);
            return list;
        }

        /// <summary>
        /// fromLocalDate の日以降の日別集計のスナップショット (date 昇順)。
        /// <see cref="DateTime.MinValue"/> を渡すと絞り込まず全期間を返す。
        /// 時刻部分は無視し、その日を含めて切り出す。
        /// </summary>
        internal static List<ToolStatsDaily> SnapshotDaily(DateTime fromLocalDate)
        {
            if (fromLocalDate == DateTime.MinValue) return SnapshotDaily(null, null);
            return SnapshotDaily(DateKey(fromLocalDate), null);
        }

        /// <summary>
        /// ローカル日時を日別集計のキー "yyyy-MM-dd" に変換する。
        /// <see cref="SnapshotDaily(string,string)"/> に渡す範囲を組み立てるためのもの。
        /// </summary>
        internal static string DateKey(DateTime localTime)
        {
            // 和暦などの非グレゴリオ暦カレンダーで "yyyy" がずれないよう InvariantCulture を指定する。
            return localTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // ─── 設定 ───

        /// <summary>AgentSettings から有効フラグ・上限件数を読み直してキャッシュする。メインスレッド専用。</summary>
        internal static void RefreshSettingsCache()
        {
            try
            {
                _enabledCache = AgentSettings.ToolStatsEnabled;

                int max = AgentSettings.ToolStatsMaxRecords;
                if (max < MinMaxRecords) max = MinMaxRecords;
                if (max > MaxMaxRecords) max = MaxMaxRecords;
                _maxRecordsCache = max;
            }
            catch (Exception ex)
            {
                // 設定ストアが未初期化 (起動直後の InitializeOnLoad 順序) の場合に備える。
                // 既定値のまま動かせば統計が取れないだけなので、警告に留める。
                Debug.LogWarning($"[UnityAgent] ToolCallStats.RefreshSettingsCache failed: {ex.Message}");
            }
        }

        // ─── 内部ヘルパー ───

        /// <summary>起動時に保存済み統計を読み戻す。メインスレッド (static ctor) 専用。</summary>
        private static void LoadFromDisk()
        {
            var root = ToolStatsStore.Load();
            if (root == null) return;

            lock (_gate)
            {
                _records.Clear();
                _dailyByDate.Clear();
                _dailyToolsByDate.Clear();

                _droppedRecords = root.droppedRecords;

                _records.AddRange(root.records);
                int max = _maxRecordsCache;
                if (_records.Count > max)
                {
                    int drop = _records.Count - max;
                    _records.RemoveRange(0, drop);
                    _droppedRecords += drop;
                }

                foreach (var d in root.daily)
                {
                    if (d == null || string.IsNullOrEmpty(d.date)) continue;
                    _dailyByDate[d.date] = d;

                    var index = new Dictionary<string, ToolStatsDailyTool>(StringComparer.Ordinal);
                    foreach (var t in d.tools)
                    {
                        if (t == null || string.IsNullOrEmpty(t.toolName)) continue;
                        index[t.toolName] = t;
                    }
                    _dailyToolsByDate[d.date] = index;
                }
                TrimDailyLocked();

                _dirty = false;
                _pendingSinceFlush = 0;
            }
        }

        /// <summary>ローカル日付キーを返す。日付が変わったときだけ文字列を作り直す。ロック内専用。</summary>
        private static string DateKeyLocked(DateTime now)
        {
            DateTime day = now.Date;
            if (_cachedDateKey == null || day != _cachedLocalDay)
            {
                _cachedLocalDay = day;
                _cachedDateKey = DateKey(day);
            }
            return _cachedDateKey;
        }

        /// <summary>日別集計が MaxDailyDays を超えていたら古い方から削る。ロック内専用。</summary>
        private static void TrimDailyLocked()
        {
            while (_dailyByDate.Count > MaxDailyDays)
            {
                string oldest = null;
                foreach (var key in _dailyByDate.Keys)
                {
                    if (oldest == null || string.CompareOrdinal(key, oldest) < 0) oldest = key;
                }
                if (oldest == null) break;
                _dailyByDate.Remove(oldest);
                _dailyToolsByDate.Remove(oldest);
            }
        }

        /// <summary>
        /// 指定期間 (両端を含む。null/空はその側を無制限) の日別集計を複製して集める。ロック内専用。
        /// ワーカースレッドが元の集計オブジェクトと副インデックスを随時書き換えるので、
        /// 参照を持ち出すだけでは列挙中の変更で壊れる。そのため値のコピーまでをロック内で行い、
        /// コストの支配項である整列・切り詰めは <see cref="FinishDailyList"/> がロック外で行う。
        /// この段階では tools は未整列・未切り詰め、リスト自体も date 順ではない。
        /// </summary>
        private static List<ToolStatsDaily> CollectDailyCopiesLocked(string fromDateKey, string toDateKey)
        {
            bool hasFrom = !string.IsNullOrEmpty(fromDateKey);
            bool hasTo = !string.IsNullOrEmpty(toDateKey);

            var list = new List<ToolStatsDaily>(_dailyByDate.Count);
            foreach (var pair in _dailyByDate)
            {
                // キーは "yyyy-MM-dd" 固定長なので、序数比較がそのまま日付の前後になる。
                if (hasFrom && string.CompareOrdinal(pair.Key, fromDateKey) < 0) continue;
                if (hasTo && string.CompareOrdinal(pair.Key, toDateKey) > 0) continue;

                var src = pair.Value;
                var copy = new ToolStatsDaily
                {
                    date = src.date,
                    totalCalls = src.totalCalls,
                    successCalls = src.successCalls,
                    failureCalls = src.failureCalls,
                    chatCalls = src.chatCalls,
                    mcpCalls = src.mcpCalls,
                    consoleCalls = src.consoleCalls,
                    totalDurationMs = src.totalDurationMs,
                    totalArgChars = src.totalArgChars,
                    totalResultChars = src.totalResultChars,
                };

                Dictionary<string, ToolStatsDailyTool> index;
                if (_dailyToolsByDate.TryGetValue(pair.Key, out index))
                {
                    var tools = new List<ToolStatsDailyTool>(index.Count);
                    foreach (var t in index.Values)
                    {
                        tools.Add(new ToolStatsDailyTool
                        {
                            toolName = t.toolName,
                            calls = t.calls,
                            failures = t.failures,
                            totalDurationMs = t.totalDurationMs,
                        });
                    }
                    copy.tools = tools;
                }

                list.Add(copy);
            }

            return list;
        }

        /// <summary>
        /// <see cref="CollectDailyCopiesLocked"/> が集めた複製を仕上げる。ロック外で呼ぶこと。
        /// 各日の tools を calls 降順 (同数は toolName 昇順) に整列して MaxToolsPerDay 件に切り詰め、
        /// リスト自体を date 昇順に並べる。触るのは複製だけなので _gate は要らない。
        /// </summary>
        private static void FinishDailyList(List<ToolStatsDaily> list)
        {
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                var tools = list[i].tools;
                if (tools == null || tools.Count <= 1) continue;

                tools.Sort(CompareDailyToolDesc);
                if (tools.Count > MaxToolsPerDay)
                    tools.RemoveRange(MaxToolsPerDay, tools.Count - MaxToolsPerDay);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.date, b.date));
        }

        /// <summary>calls 降順 → toolName 昇順。下位を落とす順序 (calls 昇順 → toolName 降順) の逆。</summary>
        private static int CompareDailyToolDesc(ToolStatsDailyTool a, ToolStatsDailyTool b)
        {
            if (a.calls != b.calls) return b.calls.CompareTo(a.calls);
            return string.CompareOrdinal(a.toolName, b.toolName);
        }
    }
}
