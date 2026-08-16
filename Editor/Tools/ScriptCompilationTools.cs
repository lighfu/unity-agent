using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// 「エディタの外で編集した .cs を Unity に気づかせ、実際にコンパイルさせ、本当に走ったか確かめる」
    /// ための入口。
    ///
    /// 背景: Unity 公式マニュアル "Refreshing the Asset Database" が挙げる自動リフレッシュ契機は
    /// (1) エディタがフォーカスを取り戻したとき (Auto Refresh 有効時) (2) Assets &gt; Refresh
    /// (3) AssetDatabase.Refresh() の呼び出し の 3 つだけで、「ファイル変更を検出した瞬間に走る」
    /// という記述は存在しない。バックグラウンドのエディタは書き込まれた .cs を import しない。
    /// CompilationPipeline.RequestScriptCompilation() はフラグを立てる予約でしかなく、
    /// Unity から見て dirty なファイルが無ければ何も起こさない。
    /// つまりエージェントが .cs を書いただけでは何も始まらない。
    ///
    /// ここでの役割分担:
    ///   RefreshAssetDatabase    — 気づかせる (唯一の正攻法)
    ///   RecordAssemblyBaseline / CompareAssemblyBaseline — DLL の mtime で「本当に建った」を証明する
    ///   BringUnityToForeground  — 実績のある最後の手段。ユーザーの作業を奪う
    /// </summary>
    public static class ScriptCompilationTools
    {
        /// <summary>
        /// ベースラインの保存先。SessionState はドメインリロードを跨いで残り、
        /// エディタを閉じると消える。リロードで死ぬ static フィールドでは用を成さないので必須。
        /// </summary>
        const string BaselineKey = "AjisaiFlow.UnityAgent.AssemblyBaseline";

        /// <summary>ベースライン一覧の表示上限。全 DLL を並べると数十行になるので頭だけ出す。</summary>
        const int MaxListedAssemblies = 12;

        /// <summary>コンパイル済みスクリプトアセンブリの出力先 (&lt;Project&gt;/Library/ScriptAssemblies)。</summary>
        internal static string ScriptAssembliesDir
        {
            get
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
                return Path.Combine(projectRoot, "Library", "ScriptAssemblies");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 1. 気づかせる
        // ─────────────────────────────────────────────────────────────

        [AgentTool(@"Make Unity notice files that were edited outside the editor, and report whether that started a compile.

WHY THIS EXISTS: a backgrounded Unity does not import your edits. Unity's manual lists exactly three
things that refresh the asset database — the editor regaining focus (with Auto Refresh on),
Assets > Refresh, and this API call. Writing a .cs file triggers none of them, and
TriggerDomainReload(mode:'recompile') only raises a flag: with nothing imported there is nothing
dirty, so it compiles nothing while still reporting success. Call this after writing script files.

settleSeconds (default 3, max 30): how long to keep watching after the refresh call returns before
  concluding that nothing started. Compilation is kicked off by a later editor tick, so deciding on
  the first frame can miss a compile that was about to begin.

The 'result:' line is one of:
  COMPILING  — compilation has started; your code is being built.
  IMPORTING  — assets are still importing; a compile may follow.
  COMPILED   — script assemblies were rewritten during this call (a fast compile that already ended).
  NO CHANGE  — the refresh finished and nothing started. Unity found nothing new to import.

WHAT THIS TOOL CANNOT DO: it does not wait for the build to finish — a real project takes on the
order of a minute, the MCP transport abandons any call at 120 s, and the domain reload that follows a
successful compile drops the connection outright. Prove completion afterwards instead:
RecordAssemblyBaseline before this call, CompareAssemblyBaseline after reconnecting.

Refuses to run in Play mode (importing a changed script there recompiles and ends the play session)
and skips if Unity is already compiling or importing.")]
        public static IEnumerator RefreshAssetDatabase(int settleSeconds = 3)
        {
            if (settleSeconds < 0) settleSeconds = 0;
            if (settleSeconds > 30) settleSeconds = 30;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                yield return "Error: Refusing to refresh while in Play mode. Importing a changed script during Play recompiles and ends the play session. Exit Play mode first (SetPlayMode), then refresh.";
                yield break;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                yield return $"Skipped: Unity is already busy (isCompiling: {EditorApplication.isCompiling}, isUpdating: {EditorApplication.isUpdating}) and no refresh was requested. Wait it out with WaitForCompilation, then call this again if your edits still are not imported.";
                yield break;
            }

            var before = SnapshotScriptAssemblies();
            bool compilationStarted = false;
            Action<object> onCompilationStarted = _ => compilationStarted = true;

            double start = EditorApplication.timeSinceStartup;
            double refreshSeconds = 0;
            string refreshError = null;

            CompilationPipeline.compilationStarted += onCompilationStarted;
            try
            {
                refreshError = TryRefreshNow();
                refreshSeconds = EditorApplication.timeSinceStartup - start;

                // Refresh が戻った時点ではまだ何も始まっていないことがある。実際のコンパイル開始は
                // native の tick が拾ってから。猶予を置かずに判定すると「始まる直前の idle」を
                // 「何も起きなかった」と読み違える。
                while (refreshError == null
                       && !compilationStarted
                       && !EditorApplication.isCompiling
                       && !EditorApplication.isUpdating
                       && EditorApplication.timeSinceStartup - start < refreshSeconds + settleSeconds)
                {
                    yield return null;
                }
            }
            finally
            {
                CompilationPipeline.compilationStarted -= onCompilationStarted;
            }

            double watched = EditorApplication.timeSinceStartup - start - refreshSeconds;
            var sb = new StringBuilder();

            if (refreshError != null)
            {
                sb.AppendLine($"Error: AssetDatabase.Refresh() threw — {refreshError}");
                sb.Append("Nothing was imported. This usually means the editor is in a state that forbids importing (an import already running, or a modal dialog); check GetEditorState.");
                yield return sb.ToString();
                yield break;
            }

            var rebuilt = DiffSnapshots(before, SnapshotScriptAssemblies());

            bool compiling = EditorApplication.isCompiling;
            bool updating = EditorApplication.isUpdating;
            string verdict;
            if (compilationStarted || compiling) verdict = "COMPILING";
            else if (updating) verdict = "IMPORTING";
            else if (rebuilt.Count > 0) verdict = "COMPILED";
            else verdict = "NO CHANGE";

            sb.AppendLine($"AssetDatabase.Refresh() returned after {Secs(refreshSeconds)}s; watched for {Secs(watched)}s more (settleSeconds: {settleSeconds}).");
            sb.AppendLine($"result: {verdict}");
            sb.AppendLine($"  isCompiling: {compiling}");
            sb.AppendLine($"  isUpdating: {updating}");
            sb.AppendLine($"  compilationStarted event: {(compilationStarted ? "fired" : "not fired")}");
            sb.AppendLine($"  autoRefresh: {EditorStateTools.SampleAutoRefresh()}");
            AppendRebuiltList(sb, rebuilt);

            switch (verdict)
            {
                case "COMPILING":
                    sb.Append("Unity is building now. Do NOT wait for it here: the build ends in a domain reload that drops this connection. Reconnect, then call CompareAssemblyBaseline (and GetConsoleLogs with the index you noted) to learn the outcome.");
                    break;
                case "IMPORTING":
                    sb.Append("Assets are still importing. If any of them were scripts, a compile follows the import — poll with GetEditorState or WaitForCompilation.");
                    break;
                case "COMPILED":
                    sb.Append("Script assemblies were rewritten while this call ran, so a compile happened and already finished. The new DLLs are on disk but the running domain still holds the old code until the reload completes.");
                    break;
                default:
                    sb.AppendLine("Unity imported nothing and started nothing. Either your edits were already imported (Auto Refresh may have got there first), or nothing actually changed under Assets/ or Packages/ — Unity watches only those two roots, so edits anywhere else are invisible to it no matter how many times you refresh.");
                    sb.Append("This result alone does not prove your edit reached the editor. If you need proof, compare DLL timestamps with RecordAssemblyBaseline / CompareAssemblyBaseline.");
                    break;
            }
            yield return sb.ToString();
        }

        /// <summary>
        /// AssetDatabase.Refresh() 本体。イテレータ内には try/catch を書けないので分離している。
        /// 失敗理由を返し、成功時は null。TriggerDomainReload からも呼ばれる。
        /// </summary>
        internal static string TryRefreshNow()
        {
            try
            {
                // ImportAssetOptions.ForceUpdate は使わない。プロジェクト全体の再インポートになり、
                // 既存 FBX などの警告を大量に掘り起こすだけで、変更検出には何の利点も無い。
                AssetDatabase.Refresh();
                return null;
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 2. 本当に建ったかを DLL の mtime で証明する
        // ─────────────────────────────────────────────────────────────

        // Risk = Safe: プロジェクトには何も書かない。保存先は SessionState だけで、
        // アセットにもシーンにも一切触らないので実質読み取り。
        [AgentTool(@"Record the current build timestamp of every compiled script assembly, so that
CompareAssemblyBaseline can later prove whether your code was actually rebuilt.

This pair is the only trustworthy completion signal after editing scripts. isCompiling going false
says a compile ended, not that it produced anything; 'Unity was already idle' says nothing ever
started. A DLL whose modification time moved forward is the one fact a no-op cannot fake.

Order of operations:
  1. RecordAssemblyBaseline
  2. RefreshAssetDatabase (or TriggerDomainReload(confirm:true, mode:'recompile'))
  3. the domain reload drops the MCP connection — reconnect
  4. CompareAssemblyBaseline

assemblyNames: comma-separated simple names ('Assembly-CSharp-Editor, AjisaiFlow.UnityAgent.Editor',
  with or without .dll). Leave empty to record every DLL in Library/ScriptAssemblies, which is the
  useful default when you do not know which assembly owns the file you edited.

The baseline lives in SessionState: it survives domain reloads (that is the entire point) but is
lost when the editor is closed. There is exactly one slot — recording again overwrites the previous
baseline.", Risk = ToolRisk.Safe)]
        public static string RecordAssemblyBaseline(string assemblyNames = "")
        {
            var names = ResolveNames(assemblyNames);
            if (names.Count == 0)
            {
                return string.IsNullOrWhiteSpace(assemblyNames)
                    ? $"Error: nothing to record — no DLL found in {ScriptAssembliesDir}. The project may never have compiled, or the path is unreadable."
                    : $"Error: nothing to record — '{assemblyNames}' resolved to no assembly name.";
            }

            var lines = new List<string>();
            lines.Add(string.Join("\t", "#recorded", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)));

            int missing = 0;
            var display = new List<string>();
            foreach (string name in names)
            {
                string path = ResolveAssemblyPath(name);
                long ticks = FileTicks(path);
                if (ticks == 0) missing++;
                lines.Add(string.Join("\t", name, ticks.ToString(CultureInfo.InvariantCulture), path));
                if (display.Count < MaxListedAssemblies)
                    display.Add($"  {name}: {Stamp(ticks)}");
            }

            SessionState.SetString(BaselineKey, string.Join("\n", lines));

            var sb = new StringBuilder();
            sb.AppendLine($"Baseline recorded for {names.Count} assembl{(names.Count == 1 ? "y" : "ies")}"
                + (missing > 0 ? $" ({missing} not built yet — no file on disk)." : "."));
            foreach (string line in display) sb.AppendLine(line);
            if (names.Count > display.Count) sb.AppendLine($"  ... and {names.Count - display.Count} more");
            sb.Append("Now trigger the compile, and call CompareAssemblyBaseline after reconnecting.");
            return sb.ToString();
        }

        [AgentTool(@"Compare script assembly timestamps against the last RecordAssemblyBaseline and report
which assemblies were rebuilt. Answers 'did my edit actually compile?' with evidence instead of a
status flag.

assemblyNames: comma-separated simple names to restrict the report to. Leave empty to compare
  everything that was recorded.

Reads only file modification times, so it is safe to call at any moment, including while a compile
is still running (it will simply report that nothing has been rewritten yet — DLLs are written at
the end of a compile, not during it).

A rebuilt DLL means the compile produced output. It does NOT mean the new code is running: the
domain still holds the old assemblies until the reload finishes. Check isCompiling in the output,
and confirm the build was clean with GetConsoleLogs(severity:'error').

Requires a baseline from this editor session — SessionState is cleared when Unity is closed.", Risk = ToolRisk.Safe)]
        public static string CompareAssemblyBaseline(string assemblyNames = "")
        {
            string raw = SessionState.GetString(BaselineKey, "");
            if (string.IsNullOrEmpty(raw))
                return "Error: no baseline recorded in this editor session. Call RecordAssemblyBaseline before triggering the compile. (The baseline survives domain reloads but not an editor restart.)";

            var recorded = new List<KeyValuePair<string, long>>();
            var recordedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long recordedAtTicks = 0;

            foreach (string line in raw.Split('\n'))
            {
                if (line.Length == 0) continue;
                string[] parts = line.Split('\t');
                if (parts[0] == "#recorded")
                {
                    if (parts.Length > 1) long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordedAtTicks);
                    continue;
                }
                if (parts.Length < 3) continue;
                long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks);
                recorded.Add(new KeyValuePair<string, long>(parts[0], ticks));
                recordedPaths[parts[0]] = parts[2];
            }

            if (recorded.Count == 0)
                return "Error: the stored baseline is empty or unreadable. Call RecordAssemblyBaseline again.";

            var filter = ParseNameList(assemblyNames);
            var changed = new List<string>();
            var unchanged = new List<string>();
            var vanished = new List<string>();
            var appeared = new List<string>();

            foreach (var entry in recorded)
            {
                if (filter.Count > 0 && !filter.Contains(entry.Key)) continue;

                string path = recordedPaths.TryGetValue(entry.Key, out string p) ? p : ResolveAssemblyPath(entry.Key);
                long now = FileTicks(path);

                if (now == 0 && entry.Value != 0)
                {
                    vanished.Add($"  {entry.Key}: GONE (was {Stamp(entry.Value)}, no file now)");
                }
                else if (entry.Value == 0 && now != 0)
                {
                    appeared.Add($"  {entry.Key}: BUILT for the first time at {Stamp(now)}");
                }
                else if (now > entry.Value)
                {
                    double afterBaseline = recordedAtTicks > 0 ? (now - recordedAtTicks) / (double)TimeSpan.TicksPerSecond : 0;
                    changed.Add($"  {entry.Key}: REBUILT at {Stamp(now)}"
                        + (recordedAtTicks > 0 ? $" ({Secs(afterBaseline)}s after the baseline)" : ""));
                }
                else if (now < entry.Value)
                {
                    changed.Add($"  {entry.Key}: REPLACED WITH AN OLDER FILE ({Stamp(now)}, baseline was {Stamp(entry.Value)})");
                }
                else
                {
                    unchanged.Add(entry.Key);
                }
            }

            var sb = new StringBuilder();
            double age = recordedAtTicks > 0
                ? (DateTime.UtcNow.Ticks - recordedAtTicks) / (double)TimeSpan.TicksPerSecond
                : -1;
            sb.AppendLine($"Baseline taken {(age >= 0 ? Secs(age) + "s ago" : "at an unknown time")}, covering {recorded.Count} assembl{(recorded.Count == 1 ? "y" : "ies")}"
                + (filter.Count > 0 ? $" (comparing {changed.Count + unchanged.Count + vanished.Count + appeared.Count} matching the filter)." : "."));

            int moved = changed.Count + appeared.Count;
            sb.AppendLine(moved > 0
                ? $"verdict: REBUILT — {moved} assembl{(moved == 1 ? "y" : "ies")} written since the baseline. The compile produced output."
                : "verdict: UNCHANGED — no assembly has been rewritten since the baseline. Nothing was compiled (yet).");

            foreach (string line in changed) sb.AppendLine(line);
            foreach (string line in appeared) sb.AppendLine(line);
            foreach (string line in vanished) sb.AppendLine(line);
            if (unchanged.Count > 0) sb.AppendLine($"  unchanged: {unchanged.Count}");

            sb.AppendLine($"isCompiling: {EditorApplication.isCompiling}  isUpdating: {EditorApplication.isUpdating}");
            if (moved > 0)
                sb.Append("Reminder: a rewritten DLL is loaded only after the domain reload. Check GetConsoleLogs(severity:'error') for compile errors — a failed compile leaves the old DLL in place, which is exactly the UNCHANGED case above.");
            else if (EditorApplication.isCompiling)
                sb.Append("A compile is running right now — DLLs are written when it ends, so call this again in a while.");
            else
                sb.Append("Nothing is compiling either. If you expected a build, Unity never imported your edit: call RefreshAssetDatabase.");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────
        // 3. 最後の手段
        // ─────────────────────────────────────────────────────────────

        [AgentTool(@"Raise the Unity editor window above every other application, exactly as clicking its
taskbar button would.

STEALS THE USER'S FOCUS. Whatever they are typing loses the keyboard, and the window they were
looking at is covered. Never call this as a routine step, and never without a reason the user would
accept. It exists for one documented failure: a backgrounded editor that has not acted on queued
work, where regaining focus is itself the thing that makes Unity import and compile (that is the
first of the three refresh triggers in Unity's manual, active when Auto Refresh is on).
Prefer RefreshAssetDatabase, which imports without touching the user's screen.

Windows only — foreground activation is a Win32 call. On macOS / Linux this returns an error instead
of pretending to have worked.

Reports whether the window actually ended up in the foreground, not merely that the request was
sent: Windows is free to refuse an activation coming from a background process (foreground lock
timeout), and that refusal is reported as a failure.", Risk = ToolRisk.Dangerous)]
        public static string BringUnityToForeground()
        {
#if UNITY_EDITOR_WIN
            if (WindowCaptureNative.TryBringUnityToForeground(out string error))
                return "Success: the Unity editor window is now in the foreground. If Auto Refresh is enabled, regaining focus makes Unity import files that were edited while it was in the background, which can start a compile and then a domain reload — expect the MCP bridge to disconnect.";
            return $"Failed: {error}. The editor window is still behind other applications. Ask the user to click the Unity window, or call RefreshAssetDatabase, which imports without needing focus.";
#else
            return "Error: BringUnityToForeground is Windows-only (foreground activation is a Win32 API). On macOS / Linux, ask the user to click the Unity window. RefreshAssetDatabase imports without focus on every platform.";
#endif
        }

        // ─────────────────────────────────────────────────────────────
        // helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Library/ScriptAssemblies の *.dll について「名前 → 最終更新 ticks」を取る。
        /// 走査中に Unity が書き換えていれば取り逃すが、そこで例外を投げて判定ごと落とすより
        /// 取れた分で続けるほうが呼び出し側にとって有用なので握る。
        /// </summary>
        static Dictionary<string, long> SnapshotScriptAssemblies()
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string dir = ScriptAssembliesDir;
                if (!Directory.Exists(dir)) return map;
                foreach (string path in Directory.GetFiles(dir, "*.dll"))
                    map[Path.GetFileNameWithoutExtension(path)] = File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (IOException) { /* 取れた分だけ返す (上のコメント参照) */ }
            catch (UnauthorizedAccessException) { /* 同上 */ }
            return map;
        }

        /// <summary>2 つのスナップショットを比べ、新しくなった / 増えた DLL 名を返す。</summary>
        static List<string> DiffSnapshots(Dictionary<string, long> before, Dictionary<string, long> after)
        {
            var result = new List<string>();
            foreach (var kv in after)
            {
                if (!before.TryGetValue(kv.Key, out long old)) result.Add($"{kv.Key} (new)");
                else if (kv.Value > old) result.Add(kv.Key);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        static void AppendRebuiltList(StringBuilder sb, List<string> rebuilt)
        {
            if (rebuilt.Count == 0)
            {
                sb.AppendLine("  assembliesRewrittenDuringThisCall: 0 (expected while a compile is still running — DLLs are written at the end)");
                return;
            }
            sb.AppendLine($"  assembliesRewrittenDuringThisCall: {rebuilt.Count}");
            for (int i = 0; i < rebuilt.Count && i < MaxListedAssemblies; i++)
                sb.AppendLine($"    {rebuilt[i]}");
            if (rebuilt.Count > MaxListedAssemblies)
                sb.AppendLine($"    ... and {rebuilt.Count - MaxListedAssemblies} more");
        }

        /// <summary>
        /// 記録対象の名前一覧。空指定なら Library/ScriptAssemblies の全 DLL。
        /// </summary>
        static List<string> ResolveNames(string assemblyNames)
        {
            var explicitNames = ParseNameList(assemblyNames);
            if (explicitNames.Count > 0) return new List<string>(explicitNames);

            var all = new List<string>(SnapshotScriptAssemblies().Keys);
            all.Sort(StringComparer.OrdinalIgnoreCase);
            return all;
        }

        /// <summary>カンマ / セミコロン / 改行区切りの名前を正規化する (.dll は落とす)。</summary>
        static HashSet<string> ParseNameList(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (string piece in csv.Split(',', ';', '\n', '\r'))
            {
                string name = piece.Trim();
                if (name.Length == 0) continue;
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                set.Add(name);
            }
            return set;
        }

        /// <summary>
        /// アセンブリ名 → DLL のパス。まず Library/ScriptAssemblies を見て、無ければ
        /// ロード済みアセンブリの実ファイル位置に落とす (パッケージ同梱のプリコンパイル DLL は
        /// ScriptAssemblies に置かれないため)。どちらも無ければ「まだ建っていない」想定のパスを返す。
        /// </summary>
        static string ResolveAssemblyPath(string name)
        {
            string candidate = Path.Combine(ScriptAssembliesDir, name + ".dll");
            if (File.Exists(candidate)) return candidate;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (!string.IsNullOrEmpty(asm.Location)) return asm.Location;
                }
                catch (NotSupportedException)
                {
                    // 動的アセンブリは Location を持たない。ファイルが無い以上比較もできないので
                    // 想定パスのまま返し、mtime 0 = "no file" として扱わせる。
                }
                break;
            }
            return candidate;
        }

        /// <summary>ファイルの最終更新 ticks。存在しない / 読めない場合は 0。</summary>
        static long FileTicks(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                return File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        static string Secs(double seconds) => seconds.ToString("F1", CultureInfo.InvariantCulture);

        static string Stamp(long utcTicks) =>
            utcTicks == 0
                ? "(no file)"
                : new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
