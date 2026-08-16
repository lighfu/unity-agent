using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Library/UnityAgent/ToolStats.json の読み書き。
    /// Library 配下なので git に乗らず、プロジェクトクリーンで消えてよい実行時データ。
    /// メインスレッド専用 (Application.dataPath を使うため)。
    /// </summary>
    internal static class ToolStatsStore
    {
        private static string _filePath;

        /// <summary>ToolStats.json の絶対パス。メインスレッドで 1 度だけ解決してキャッシュする。</summary>
        internal static string FilePath
        {
            get
            {
                if (_filePath == null)
                {
                    // Application.dataPath はメインスレッドでしか読めないので、
                    // 初回アクセス (static ctor 経由の Load) で解決してキャッシュする。
                    string dir = Path.GetFullPath(
                        Path.Combine(Application.dataPath, "..", "Library", "UnityAgent"));
                    _filePath = Path.Combine(dir, "ToolStats.json");
                }
                return _filePath;
            }
        }

        /// <summary>JSON を読み込む。存在しない/壊れている場合は null を返す (呼び出し側は新規ルートを作る)。</summary>
        internal static ToolStatsRoot Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return null;

                var root = JsonUtility.FromJson<ToolStatsRoot>(json);
                if (root == null) return null;

                if (root.version < ToolCallStats.CurrentSchemaVersion)
                {
                    // v1 が初版なのでここに到達する経路はまだ無い。将来の移行はこの枠に足す。
                    root.version = ToolCallStats.CurrentSchemaVersion;
                }
                if (root.version > ToolCallStats.CurrentSchemaVersion)
                {
                    Debug.LogWarning(
                        $"[UnityAgent] ToolStats.json version {root.version} is newer than supported ({ToolCallStats.CurrentSchemaVersion}). " +
                        "Loading best-effort.");
                }

                Normalize(root);
                return root;
            }
            catch (Exception ex)
            {
                // 統計は付随機能なので、読み込み失敗で UnityAgent 本体を止めない。
                // null を返すと呼び出し側が空のルートから始める。
                Debug.LogWarning($"[UnityAgent] ToolStatsStore.Load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 一時ファイルに書いてから本体と差し替える。差し替えは <see cref="File.Replace(string,string,string)"/> を
        /// 使うので、既存ファイルがある通常経路では「本体が存在しない瞬間」が生じない。
        /// 本体がまだ無い初回だけは Replace が使えないので File.Move で置く
        /// (この場合は失っても困る中身が無い)。Replace が使えない環境では削除 → 移動に落ちるため、
        /// そのフォールバック経路にだけ非アトミックな窓が残る。失敗は警告ログのみで握らない。
        /// </summary>
        internal static void Save(ToolStatsRoot root)
        {
            if (root == null) return;
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Library 配下の内部ファイルで人が読むものではないので prettyPrint しない。
                // 整形すると同じ内容でファイルサイズが約 2 倍になる。
                string json = JsonUtility.ToJson(root, false);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);

                if (File.Exists(path))
                {
                    try
                    {
                        // 第 3 引数 null = バックアップファイルを作らない。
                        File.Replace(tmp, path, null);
                    }
                    catch (Exception ex) when (
                        ex is PlatformNotSupportedException ||
                        ex is NotSupportedException ||
                        ex is IOException ||
                        ex is UnauthorizedAccessException)
                    {
                        // Replace は同一ボリューム前提で、ファイルシステムによっては未対応。
                        // 判定中に本体が消えた場合 (FileNotFoundException も IOException) もここに来る。
                        // その場合だけ従来の削除 → 移動に落とす。
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch (Exception ex)
            {
                // 書き込み失敗はディスク満杯・権限・ウイルス対策のロック等。
                // 次のフラッシュ契機で再試行されるので、警告だけ出して続行する。
                Debug.LogWarning($"[UnityAgent] ToolStatsStore.Save failed: {ex.Message}");
            }
        }

        /// <summary>保存ファイルを削除する。存在しなくてもエラーにしない。</summary>
        internal static void DeleteFile()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Delete(path);
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception ex)
            {
                // 削除できなくてもメモリ上の統計はリセット済みなので、警告だけで続行する。
                Debug.LogWarning($"[UnityAgent] ToolStatsStore.DeleteFile failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 読み込み直後の正規化。null リストの補完、daily の date 昇順ソート、
        /// スキーマ上のハード上限までの切り詰めを行う。
        /// 明細の件数は設定値 (ToolStatsMaxRecords) にも依存するので、
        /// ここではハード上限 (MaxMaxRecords) までしか削らない。設定値での切り詰めは
        /// <see cref="ToolCallStats"/> 側が読み込み後に行う。
        /// </summary>
        private static void Normalize(ToolStatsRoot root)
        {
            if (root.records == null) root.records = new List<ToolStatsRecord>();
            if (root.daily == null) root.daily = new List<ToolStatsDaily>();
            if (root.droppedRecords < 0) root.droppedRecords = 0;

            for (int i = root.records.Count - 1; i >= 0; i--)
            {
                var r = root.records[i];
                if (r == null || string.IsNullOrEmpty(r.toolName))
                {
                    root.records.RemoveAt(i);
                    continue;
                }
                if (r.durationMs < 0) r.durationMs = 0;
                if (r.argChars < 0) r.argChars = 0;
                if (r.resultChars < 0) r.resultChars = 0;
            }
            if (root.records.Count > ToolCallStats.MaxMaxRecords)
            {
                int drop = root.records.Count - ToolCallStats.MaxMaxRecords;
                root.records.RemoveRange(0, drop);
                root.droppedRecords += drop;
            }

            for (int i = root.daily.Count - 1; i >= 0; i--)
            {
                var d = root.daily[i];
                if (d == null || string.IsNullOrEmpty(d.date))
                {
                    root.daily.RemoveAt(i);
                    continue;
                }
                if (d.tools == null) d.tools = new List<ToolStatsDailyTool>();
                for (int j = d.tools.Count - 1; j >= 0; j--)
                {
                    var t = d.tools[j];
                    if (t == null || string.IsNullOrEmpty(t.toolName)) d.tools.RemoveAt(j);
                }
                if (d.tools.Count > ToolCallStats.MaxToolsPerDay)
                    d.tools.RemoveRange(ToolCallStats.MaxToolsPerDay,
                        d.tools.Count - ToolCallStats.MaxToolsPerDay);
            }

            root.daily.Sort((a, b) => string.CompareOrdinal(a.date, b.date));
            if (root.daily.Count > ToolCallStats.MaxDailyDays)
                root.daily.RemoveRange(0, root.daily.Count - ToolCallStats.MaxDailyDays);
        }
    }
}
