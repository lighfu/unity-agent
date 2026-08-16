using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// 1 回の描画で使う統計スナップショット。KPI と 4 種のグラフが同じ瞬間のデータを見るように、
    /// 日別集計と明細をまとめて 1 度だけ切り出す。
    /// </summary>
    internal sealed class ToolStatsSnapshot
    {
        /// <summary>このスナップショットを取った期間。</summary>
        internal ToolStatsPeriod period;

        /// <summary>
        /// 期間内の日別集計 (date 昇順)。件数の上限で切り詰められないので、
        /// 呼び出し数・成功数・ツール別集計はすべてこちらを正とする。
        /// </summary>
        internal List<ToolStatsDaily> daily = new List<ToolStatsDaily>();

        /// <summary>
        /// 期間内の明細 (古い → 新しい)。明細は全期間通算で上限件数まで保持されるため、
        /// 古い呼び出しは既に捨てられていることがある。母集団が欠けても困らない用途
        /// (散布図・時間別の分布) にだけ使う。
        /// </summary>
        internal List<ToolStatsRecord> records = new List<ToolStatsRecord>();

        /// <summary>切り出す直前に読んだ <see cref="ToolCallStats.Revision"/>。</summary>
        internal int revision;
    }

    /// <summary>
    /// ToolCallStats のスナップショットから 4 種のグラフ用データを作る。メインスレッド専用
    /// (ToolRegistry を引くため)。すべて呼び出しごとに新しいリストを返す純関数。
    /// </summary>
    internal static class ToolStatsQuery
    {
        /// <summary>All の時系列で表示する最大日数。</summary>
        private const int AllPeriodMaxDays = 90;

        /// <summary>
        /// ツール名 → カテゴリ (ToolRegistry が持つ生の文字列)。ToolRegistry を 1 度だけ走査して作る。
        /// ローカライズ済みの文字列は入れない (言語を切り替えても内訳が割れないように)。
        /// </summary>
        private static Dictionary<string, string> _categoryByTool;

        // ─── スナップショット ───

        /// <summary>
        /// 指定期間の日別集計と明細をまとめて 1 度だけ切り出す。以降の Get* はこの結果だけを見るので、
        /// 1 回の再描画中に統計が増えても KPI とグラフの数字がずれない。
        /// </summary>
        internal static ToolStatsSnapshot Capture(ToolStatsPeriod period)
        {
            var snapshot = new ToolStatsSnapshot { period = period };

            // 版数はデータより先に読む。逆順だと、切り出しの直後に増えた分を
            // 「取り込み済み」と誤認して次のポーリングで拾えなくなる。
            snapshot.revision = ToolCallStats.Revision;

            DateTime start = PeriodStartDate(period);
            snapshot.daily = ToolCallStats.SnapshotDaily(start);
            snapshot.records = ToolCallStats.SnapshotRecords(start);
            return snapshot;
        }

        // ─── 集計 ───

        /// <summary>
        /// 期間の概況を返す。すべて日別集計から作るので、明細の上限件数に影響されない。
        /// ただし distinctTools だけは日別のツール別内訳 (1 日あたり上位
        /// <see cref="ToolCallStats.MaxToolsPerDay"/> 件) を数えるため、
        /// 1 日にそこを超える種類のツールを呼んだ日があると実際より少なくなる。
        /// </summary>
        internal static ToolStatsOverview GetOverview(ToolStatsSnapshot snapshot)
        {
            var result = new ToolStatsOverview();
            if (snapshot == null) return result;

            long totalDuration = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);

            var daily = snapshot.daily;
            for (int i = 0; i < daily.Count; i++)
            {
                var d = daily[i];
                if (d == null) continue;
                result.totalCalls += d.totalCalls;
                result.successCalls += d.successCalls;
                result.failureCalls += d.failureCalls;
                result.chatCalls += d.chatCalls;
                result.mcpCalls += d.mcpCalls;
                result.consoleCalls += d.consoleCalls;
                totalDuration += d.totalDurationMs;
                if (d.tools == null) continue;
                for (int j = 0; j < d.tools.Count; j++)
                {
                    var t = d.tools[j];
                    if (t != null && !string.IsNullOrEmpty(t.toolName)) distinct.Add(t.toolName);
                }
            }

            result.distinctTools = distinct.Count;
            result.avgDurationMs = result.totalCalls > 0
                ? (double)totalDuration / result.totalCalls
                : 0.0;
            return result;
        }

        /// <summary>
        /// (1) 時系列の呼び出し数。Today は 24 時間分、それ以外は日別。
        /// Today だけは時間別の内訳が日別集計に無いため明細から作る
        /// (= 上限件数を超えた古い呼び出しは含まれない)。<see cref="IsTimeSeriesFromRecords"/> 参照。
        /// </summary>
        internal static List<ToolStatsTimePoint> GetTimeSeries(ToolStatsSnapshot snapshot)
        {
            var points = new List<ToolStatsTimePoint>();
            if (snapshot == null) return points;

            if (snapshot.period == ToolStatsPeriod.Today)
            {
                // ローカル時 0〜23 の 24 バケット固定。記録が無い時間も 0 の点を作る。
                var totals = new int[24];
                var failures = new int[24];
                var records = snapshot.records;
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    if (r == null) continue;
                    int hour = DateTimeOffset.FromUnixTimeMilliseconds(r.tsUnixMs).LocalDateTime.Hour;
                    if (hour < 0 || hour > 23) continue;
                    totals[hour]++;
                    if (!r.success) failures[hour]++;
                }
                for (int h = 0; h < 24; h++)
                {
                    points.Add(new ToolStatsTimePoint
                    {
                        label = h.ToString(CultureInfo.InvariantCulture),
                        total = totals[h],
                        failures = failures[h],
                    });
                }
                return points;
            }

            var daily = snapshot.daily;

            if (snapshot.period == ToolStatsPeriod.All)
            {
                // All は「記録のある日」だけを末尾 90 日ぶん出す (穴埋めしない)。
                int start = Math.Max(0, daily.Count - AllPeriodMaxDays);
                for (int i = start; i < daily.Count; i++)
                {
                    points.Add(new ToolStatsTimePoint
                    {
                        label = FormatDayLabel(daily[i].date),
                        total = daily[i].totalCalls,
                        failures = daily[i].failureCalls,
                    });
                }
                return points;
            }

            // Last7Days / Last30Days は記録が無い日も 0 の点を作る。
            var byDate = new Dictionary<string, ToolStatsDaily>(StringComparer.Ordinal);
            for (int i = 0; i < daily.Count; i++)
            {
                if (daily[i] != null && !string.IsNullOrEmpty(daily[i].date))
                    byDate[daily[i].date] = daily[i];
            }

            int days = snapshot.period == ToolStatsPeriod.Last7Days ? 7 : 30;
            DateTime first = DateTime.Now.Date.AddDays(-(days - 1));
            for (int i = 0; i < days; i++)
            {
                DateTime day = first.AddDays(i);
                string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                ToolStatsDaily d;
                byDate.TryGetValue(key, out d);
                points.Add(new ToolStatsTimePoint
                {
                    label = day.ToString("MM/dd", CultureInfo.InvariantCulture),
                    total = d != null ? d.totalCalls : 0,
                    failures = d != null ? d.failureCalls : 0,
                });
            }
            return points;
        }

        /// <summary>
        /// 時系列が明細ベース (= 明細の上限件数で頭打ちになり得る) かどうか。
        /// UI がその旨を注記に出すために使う。
        /// </summary>
        internal static bool IsTimeSeriesFromRecords(ToolStatsPeriod period)
        {
            return period == ToolStatsPeriod.Today;
        }

        /// <summary>
        /// (2) ツール別ランキング (全件)。calls 降順、同数は toolName 昇順。
        /// 日別集計から作るので明細の上限件数に影響されないが、
        /// 日別のツール別内訳は 1 日あたり上位
        /// <see cref="ToolCallStats.MaxToolsPerDay"/> 件までなので、
        /// それを超える種類を 1 日に呼んだ場合は下位のツールが落ちる。
        /// 上位 N 件だけが要るときは <see cref="TakeTop"/> を通す。
        /// </summary>
        internal static List<ToolStatsRankItem> GetRanking(ToolStatsSnapshot snapshot)
        {
            var calls = new Dictionary<string, int>(StringComparer.Ordinal);
            var failures = new Dictionary<string, int>(StringComparer.Ordinal);
            var durations = new Dictionary<string, long>(StringComparer.Ordinal);

            if (snapshot != null)
            {
                var daily = snapshot.daily;
                for (int i = 0; i < daily.Count; i++)
                {
                    var tools = daily[i] != null ? daily[i].tools : null;
                    if (tools == null) continue;
                    for (int j = 0; j < tools.Count; j++)
                    {
                        var t = tools[j];
                        if (t == null) continue;
                        Accumulate(calls, failures, durations, t.toolName, t.calls, t.failures, t.totalDurationMs);
                    }
                }
            }

            var items = new List<ToolStatsRankItem>(calls.Count);
            foreach (var pair in calls)
            {
                int c = pair.Value;
                items.Add(new ToolStatsRankItem
                {
                    toolName = pair.Key,
                    calls = c,
                    failures = failures[pair.Key],
                    avgDurationMs = c > 0 ? (double)durations[pair.Key] / c : 0.0,
                });
            }

            items.Sort((a, b) =>
            {
                if (a.calls != b.calls) return b.calls.CompareTo(a.calls);
                return string.CompareOrdinal(a.toolName, b.toolName);
            });

            return items;
        }

        /// <summary>ランキングの上位 topN 件を新しいリストで返す。topN が 0 以下なら全件を複製する。</summary>
        internal static List<ToolStatsRankItem> TakeTop(List<ToolStatsRankItem> ranking, int topN)
        {
            if (ranking == null) return new List<ToolStatsRankItem>();
            if (topN <= 0 || ranking.Count <= topN) return new List<ToolStatsRankItem>(ranking);

            var result = new List<ToolStatsRankItem>(topN);
            for (int i = 0; i < topN; i++) result.Add(ranking[i]);
            return result;
        }

        /// <summary>
        /// (3) カテゴリ別内訳。<see cref="GetRanking"/> の結果 (全件) をそのまま渡す
        /// (集計を 2 度やらないため)。calls 降順、topN を超えた分は「その他」にまとめる。
        /// カテゴリ不明のツールも「その他」に入るので、両者は同じスライスに合流する。
        /// </summary>
        internal static List<ToolStatsCategorySlice> GetCategoryBreakdown(
            List<ToolStatsRankItem> ranking, int topN)
        {
            var slices = new List<ToolStatsCategorySlice>();
            if (ranking == null) return slices;

            // 表示ラベルはここで初めて付ける。キャッシュには生のカテゴリ名しか入れない。
            string otherLabel = M("その他");

            var byCategory = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ranking.Count; i++)
            {
                string cat = ResolveCategory(ranking[i].toolName);
                if (string.IsNullOrEmpty(cat)) cat = otherLabel;
                int cur;
                byCategory.TryGetValue(cat, out cur);
                byCategory[cat] = cur + ranking[i].calls;
            }

            slices.Capacity = byCategory.Count;
            foreach (var pair in byCategory)
                slices.Add(new ToolStatsCategorySlice { category = pair.Key, calls = pair.Value });

            slices.Sort((a, b) =>
            {
                if (a.calls != b.calls) return b.calls.CompareTo(a.calls);
                return string.CompareOrdinal(a.category, b.category);
            });

            if (topN <= 0 || slices.Count <= topN) return slices;

            int otherCalls = 0;
            for (int i = topN; i < slices.Count; i++) otherCalls += slices[i].calls;
            slices.RemoveRange(topN, slices.Count - topN);

            int existing = slices.FindIndex(s => string.Equals(s.category, otherLabel, StringComparison.Ordinal));
            if (existing >= 0)
            {
                // 上位に既に「その他」がある場合は同じスライスに足す (凡例が重複しないように)。
                var merged = slices[existing];
                merged.calls += otherCalls;
                slices[existing] = merged;
            }
            else
            {
                slices.Add(new ToolStatsCategorySlice { category = otherLabel, calls = otherCalls });
            }
            return slices;
        }

        /// <summary>
        /// (4) 文字数と所要時間の散布図。明細だけが元データなので、
        /// 上限件数を超えた古い呼び出しは含まれない。最大 maxPoints 件に間引く。
        /// </summary>
        internal static List<ToolStatsScatterPoint> GetScatter(ToolStatsSnapshot snapshot, int maxPoints)
        {
            var result = new List<ToolStatsScatterPoint>();
            if (snapshot == null || maxPoints <= 0) return result;

            var records = snapshot.records;
            int n = records.Count;
            if (n == 0) return result;

            // 新しい方から stride 刻みで拾い、最後に古い → 新しい順へ戻す。
            int stride = (n + maxPoints - 1) / maxPoints;
            if (stride < 1) stride = 1;
            for (int i = n - 1; i >= 0; i -= stride)
            {
                var r = records[i];
                if (r == null) continue;
                result.Add(new ToolStatsScatterPoint
                {
                    toolName = r.toolName,
                    chars = r.argChars + r.resultChars,
                    durationMs = r.durationMs,
                    success = r.success,
                });
            }
            result.Reverse();
            return result;
        }

        // ─── カテゴリ解決 ───

        /// <summary>
        /// ツール名から生のカテゴリ名を解決する。ToolRegistry に無い / カテゴリを持たない場合は null。
        /// 表示用のラベルが要るときは呼び出し側で <c>M("その他")</c> に読み替える
        /// (ローカライズ済み文字列をキャッシュに焼き込まないため)。
        /// </summary>
        internal static string ResolveCategory(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;

            var map = GetCategoryMap();
            string cat;
            if (map.TryGetValue(toolName, out cat) && !string.IsNullOrEmpty(cat)) return cat;
            return null;
        }

        private static Dictionary<string, string> GetCategoryMap()
        {
            if (_categoryByTool != null) return _categoryByTool;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool complete = true;
            try
            {
                foreach (var info in ToolRegistry.GetAllTools())
                {
                    if (info.method == null) continue;
                    string name = info.method.Name;
                    if (string.IsNullOrEmpty(name) || map.ContainsKey(name)) continue;

                    // 既存のカテゴリ解決 (ToolConsoleWindow) と同じフォールバック順に揃える。
                    string cat = info.attribute != null ? info.attribute.Category : null;
                    if (string.IsNullOrEmpty(cat) && info.method.DeclaringType != null)
                        cat = info.method.DeclaringType.Name.Replace("Tools", "");
                    // カテゴリ不明のときは入れない。表示時に「その他」へ寄せる。
                    if (string.IsNullOrEmpty(cat)) continue;
                    map[name] = cat;
                }
            }
            catch (Exception ex)
            {
                // 外部アセンブリの読み込み中などで走査が途中で落ちることがある。
                // その場合はキャッシュを確定させず (次回リトライ)、今回は拾えた分だけ使う。
                complete = false;
                Debug.LogWarning($"[UnityAgent] ToolStatsQuery: failed to build category cache: {ex.Message}");
            }

            if (complete) _categoryByTool = map;
            return map;
        }

        // ─── 内部ヘルパー ───

        private static void Accumulate(Dictionary<string, int> calls, Dictionary<string, int> failures,
            Dictionary<string, long> durations, string toolName, int addCalls, int addFailures, long addDuration)
        {
            if (string.IsNullOrEmpty(toolName)) return;

            int c;
            calls.TryGetValue(toolName, out c);
            calls[toolName] = c + addCalls;

            int f;
            failures.TryGetValue(toolName, out f);
            failures[toolName] = f + addFailures;

            long d;
            durations.TryGetValue(toolName, out d);
            durations[toolName] = d + addDuration;
        }

        /// <summary>期間の開始日 (ローカル日付の 0 時)。All は DateTime.MinValue。</summary>
        private static DateTime PeriodStartDate(ToolStatsPeriod period)
        {
            DateTime today = DateTime.Now.Date;
            switch (period)
            {
                case ToolStatsPeriod.Today: return today;
                case ToolStatsPeriod.Last7Days: return today.AddDays(-6);
                case ToolStatsPeriod.Last30Days: return today.AddDays(-29);
                default: return DateTime.MinValue;
            }
        }

        /// <summary>"yyyy-MM-dd" を軸ラベル "MM/dd" に変換する。解釈できなければ元の文字列を返す。</summary>
        private static string FormatDayLabel(string date)
        {
            DateTime day;
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day))
                return day.ToString("MM/dd", CultureInfo.InvariantCulture);
            return date ?? "";
        }
    }
}
