using System;
using System.Threading;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// HTTP スレッドとメインスレッド間で受け渡されるツール呼び出しのコンテキスト。
    /// InProc モード (HTTP listener) と Bridge モード (TCP client) の両方から共用する。
    /// </summary>
    internal sealed class PendingCall
    {
        public string ToolName { get; private set; }
        public JNode Arguments { get; private set; }

        /// <summary>
        /// ExecuteUnityTool が target ツールに再ディスパッチするために name/args を差し替える。
        ///
        /// 統計の argChars は意図的に更新しない。ここで数え直すと、RunEditorScript のように
        /// 本文を引数で渡すツールで数百 KB の JSON を 1 呼び出しにつきもう一度シリアライズする
        /// ことになる。argChars は受信した引数 JSON の文字数 (= 実際に運んだ量) という定義なので、
        /// ExecuteUnityTool 経由では外側のラッパ ({"name":...,"arguments":{...}}) を含んだ値になる。
        /// </summary>
        public void Rewrite(string newName, JNode newArgs)
        {
            ToolName = newName ?? "";
            Arguments = newArgs ?? JNode.Obj();
        }
        public string ResultText { get; private set; }
        ToolResultRequest.Options _resultOptions = ToolResultRequest.Options.Default;

        internal string ConfigureResultPaging()
        {
            return ToolResultRequest.TryGetOptions(ToolName, Arguments, out _resultOptions, out var error) ? null : error;
        }

        /// <summary>
        /// Optional image payload attached to the tool result. Populated by
        /// <see cref="Invoker"/> when a tool sets <see cref="Tools.SceneViewTools.PendingImageBytes"/>
        /// (scene / expression / multi-angle captures). When non-null, the HTTP / bridge
        /// transports wrap this in an MCP <c>image</c> content block so the calling LLM
        /// actually sees the picture instead of just the tool's summary string.
        /// </summary>
        public byte[] ImageBytes { get; private set; }
        public string ImageMimeType { get; private set; }

        public string Error { get; private set; }
        public string ErrorData { get; private set; }
        public int ErrorCode { get; private set; } = -32000;
        public bool Cancelled { get; private set; }

        /// <summary>
        /// Bridge モードで使う識別子。bridge 内部の pending id を持ち回り、結果送信時に
        /// レスポンスメッセージへタグ付けするために <see cref="AgentMCPBridgeClient"/> が参照する。
        /// InProc モードでは null。
        /// </summary>
        public string BridgePendingId;

        /// <summary>
        /// 完了通知コールバック (Bridge モード専用)。InProc モードは <see cref="Wait"/> でブロックするが、
        /// Bridge モードは push 方式で結果を bridge に書き戻す。
        /// </summary>
        public Action<PendingCall> OnComplete;

        readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

        // ── ツール呼び出し統計 ──
        // SetResult / SetError は HTTP リスナースレッドからも呼ばれ得るため、記録側では
        // Unity API / UI Toolkit に触れない。タイムアウト Cancel() と遅延完了 SetResult() が
        // 競合して 2 回発火し得るので Interlocked で単発を保証する。

        /// <summary>
        /// 実行時間の計測。生成時点では止まっており、メインスレッドの pump が
        /// <see cref="MarkExecutionStart"/> を呼んだ時点から動き出す。
        /// </summary>
        readonly System.Diagnostics.Stopwatch _statsSw = new System.Diagnostics.Stopwatch();
        int _statsRecorded;

        /// <summary>
        /// 受信した引数 JSON の文字数。呼び出し元が既に持っている文字列の長さを渡す想定で、
        /// 負値なら「未知」を意味し、記録時に 1 度だけ数える。
        /// </summary>
        int _statsArgChars;

        /// <param name="argChars">
        /// 呼び出し元が既にシリアライズ済みの引数 JSON の文字数。省略すると記録時に
        /// もう一度シリアライズして数えることになるので、分かるなら必ず渡すこと。
        /// </param>
        public PendingCall(string toolName, JNode arguments, int argChars = -1)
        {
            ToolName = toolName ?? "";
            Arguments = arguments ?? JNode.Obj();
            _statsArgChars = argChars;
        }

        /// <summary>
        /// 実行時間の計測を開始する。メインスレッドの pump が <c>Invoker.Invoke</c> に渡す
        /// 直前に呼ぶ。統計の durationMs は「実行に要した時間」であり、キュー待ちは含めない。
        ///
        /// 以前は InProc がリスナースレッドの enqueue 時点、Bridge がメインスレッドの dispatch
        /// 時点から計っており、同じ <see cref="ToolCallRoute.Mcp"/> なのに意味が違っていた。
        /// キュー待ちを含めた値が要るなら、この計測に混ぜずに別フィールドを足すこと。
        ///
        /// 一度も dispatch されないまま終わった呼び出し (キューで待っている間にタイムアウト、
        /// キャンセル済みで pump にスキップされた等) は実行時間 0 として記録される。
        /// </summary>
        public void MarkExecutionStart()
        {
            _statsSw.Start();
        }

        /// <summary>
        /// スキーマにない引数が渡されていた場合の警告文 (<see cref="Invoker"/> が設定)。
        /// <see cref="SetResult"/> / <see cref="SetError"/> が本文の先頭に差し込む。
        ///
        /// 結果を確定させる経路が同期・非同期コルーチン・ユーザー選択待ちの 3 つあり、
        /// どこを通っても必ず付くようにここで一元化している。エラー側にも付けるのは、
        /// 無視された引数が原因でエラーになるケース (別ランチャーを見に行って
        /// 「Mode not found」) こそ、この情報が要るため。
        /// </summary>
        public string ArgumentWarning { get; set; }

        /// <summary>警告があれば本文の先頭に差し込む。</summary>
        string WithArgumentWarning(string body)
        {
            if (string.IsNullOrEmpty(ArgumentWarning)) return body ?? "";
            return ArgumentWarning + "\n\n" + (body ?? "");
        }

        public void SetResult(string text)
        {
            string body = WithArgumentWarning(text);
            RecordStats(body.Length, false);
            ResultText = ToolResultRequest.Format(ToolName, body, _resultOptions);
            AgentMCPServer.RaiseCallFinish(ToolName, body, false);
            _done.Set();
            try { OnComplete?.Invoke(this); } catch { }
        }

        /// <summary>
        /// Attach an image payload to the result. Must be called from the main thread
        /// *before* <see cref="SetResult"/> for the transport layer to pick it up.
        /// </summary>
        public void SetImage(byte[] bytes, string mimeType)
        {
            if (bytes == null || bytes.Length == 0) return;
            ImageBytes = bytes;
            ImageMimeType = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;
        }

        public void SetError(string message, string data = null, int code = -32000)
        {
            RecordStats(0, true);
            string fullError = WithArgumentWarning(message ?? "Unknown error");
            Error = ToolResultRequest.FormatError(ToolName, fullError, _resultOptions);
            ErrorData = ToolResultRequest.FormatError(ToolName + ".errorData", data ?? "", _resultOptions);
            ErrorCode = code;
            AgentMCPServer.RaiseCallFinish(ToolName, fullError, true);
            _done.Set();
            try { OnComplete?.Invoke(this); } catch { }
        }

        public bool Wait(int timeoutMs) => _done.Wait(timeoutMs);

        public void Cancel()
        {
            Cancelled = true;
            if (!_done.IsSet)
                SetError("Cancelled");
        }

        /// <summary>
        /// ツール呼び出し統計へ 1 回だけ記録する。SetResult / SetError の双方から呼ばれ、
        /// タイムアウト Cancel() 後の遅延完了と競合しても二重記録しない。
        /// ワーカースレッドから呼ばれ得るので Unity API には触れない。
        /// 所要時間の定義は <see cref="MarkExecutionStart"/> を参照。
        /// </summary>
        void RecordStats(int resultChars, bool isError)
        {
            if (System.Threading.Interlocked.Exchange(ref _statsRecorded, 1) != 0) return;
            _statsSw.Stop();

            int argChars = _statsArgChars;
            // 呼び出し元が長さを渡さなかった場合の保険。通常は通らない経路。
            if (argChars < 0) argChars = Arguments?.ToJson()?.Length ?? 0;

            ToolCallStats.Record(ToolName, ToolCallRoute.Mcp, !isError,
                _statsSw.Elapsed.TotalMilliseconds, argChars, resultChars);
        }
    }

    /// <summary>Transport options belong to ExecuteUnityTool's envelope, never to its target arguments.</summary>
    internal static class ToolResultRequest
    {
        internal const string ReaderName = "ReadUnityToolResultPage";

        internal readonly struct Options
        {
            internal readonly int Offset, Limit, MaxChars;
            internal Options(int offset, int limit, int maxChars) { Offset = offset; Limit = limit; MaxChars = maxChars; }
            internal static Options Default => new Options(0, ToolResultPager.DefaultLimit, ToolResultPager.DefaultMaxChars);
        }

        internal static bool IsReader(string name) => string.Equals(name, ReaderName, StringComparison.OrdinalIgnoreCase);

        internal static bool TryGetOptions(string toolName, JNode args, out Options options, out string error)
        {
            options = Options.Default;
            error = null;
            if (toolName != "ExecuteUnityTool") return ValidateReader(toolName, args, out error);
            if (!ReadInteger(args, "resultOffset", 0, out int offset, out error)
                || !ReadInteger(args, "resultLimit", ToolResultPager.DefaultLimit, out int limit, out error)
                || !ReadInteger(args, "resultMaxChars", ToolResultPager.DefaultMaxChars, out int maxChars, out error)) return false;
            error = ToolResultPager.Validate(offset, limit, maxChars);
            if (error != null)
            {
                error = error.Replace("offset", "resultOffset").Replace("limit", "resultLimit").Replace("maxChars", "resultMaxChars");
                return false;
            }
            options = new Options(offset, limit, maxChars);
            return ValidateReader(args?["name"].AsString, args?["arguments"], out error);
        }

        internal static bool ValidateReader(string name, JNode args, out string error)
        {
            error = null;
            if (!IsReader(name)) return true;
            return TryReadReaderArguments(args, out _, out _, out _, out _, out error);
        }

        internal static bool TryReadReaderArguments(JNode args, out string resultId, out int offset, out int limit, out int maxChars, out string error)
        {
            resultId = null;
            offset = 0; limit = ToolResultPager.DefaultLimit; maxChars = ToolResultPager.DefaultMaxChars;
            // Unlike ordinary tools, this reader returns an already bounded canonical JSON page.
            // Reject unknown keys before binding so an unbounded warning cannot be prepended to it.
            if (args != null)
            {
                foreach (string key in args.Keys)
                {
                    if (string.Equals(key, "resultId", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "offset", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "limit", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "maxChars", StringComparison.OrdinalIgnoreCase)) continue;
                    string preview = key.Length > 96 ? key.Substring(0, 96) + "…" : key;
                    error = $"Error: Unknown argument '{preview}' for ReadUnityToolResultPage. Valid keys: resultId, offset, limit, maxChars.";
                    return false;
                }
            }
            string idKey = ResolveKey(args, "resultId", true, out error);
            var id = idKey == null ? JNode.NullNode : args[idKey];
            if (error != null) return false;
            if (id.Type != JNode.JType.String || string.IsNullOrWhiteSpace(id.AsString))
            {
                error = "Error: resultId must be a non-empty string.";
                return false;
            }
            resultId = id.AsString;
            if (!ReadInteger(args, "offset", 0, out offset, out error, true)
                || !ReadInteger(args, "limit", ToolResultPager.DefaultLimit, out limit, out error, true)
                || !ReadInteger(args, "maxChars", ToolResultPager.DefaultMaxChars, out maxChars, out error, true)) return false;
            error = ToolResultPager.Validate(offset, limit, maxChars);
            return error == null;
        }

        internal static bool ReadInteger(JNode args, string key, int defaultValue, out int value, out string error, bool ignoreCase = false)
        {
            value = defaultValue;
            string resolved = ResolveKey(args, key, ignoreCase, out error);
            if (error != null) return false;
            if (resolved == null) return true;
            var raw = args[resolved];
            double number = raw.Type == JNode.JType.Number ? raw.AsNumber : double.NaN;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number))
            {
                error = $"Error: {key} must be an integer.";
                return false;
            }
            value = (int)number;
            return true;
        }

        static string ResolveKey(JNode args, string key, bool ignoreCase, out string error)
        {
            error = null;
            if (args == null) return null;
            if (!ignoreCase) return args.Has(key) ? key : null;
            string found = null;
            foreach (string candidate in args.Keys)
            {
                if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) { error = $"Error: argument '{key}' has ambiguous case variants."; return null; }
                found = candidate;
            }
            return found;
        }

        internal static string Format(string toolName, string text, Options options)
            => IsReader(toolName) ? text ?? "" : ToolResultPager.CreateFirstPage(toolName, text, options.Offset, options.Limit, options.MaxChars);

        internal static string FormatError(string toolName, string text, Options options)
        {
            // A failure is always shown from its start, even when a caller requested a later success page.
            // Only an exact page generated from a live immutable snapshot can skip the fallback bound.
            // Tool error text can contain arbitrary JSON, including forged page fields or an altered payload.
            if (ToolResultPager.IsSavedPage(text))
                return text;
            return ToolResultPager.CreateFirstPage(toolName, text, 0, options.Limit, options.MaxChars);
        }
    }
}
