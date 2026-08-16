using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Bridge モードの Unity 側クライアント。
    ///
    /// 動作:
    /// 1. localhost の bridge プロセスへ TCP 接続
    /// 2. <c>{"type":"hello","token":...}</c> を送信し <c>hello_ack</c> を待つ
    /// 3. ループ: bridge から <c>{"type":"call","id":...,"tool":...,"args":{...}}</c> を受信
    /// 4. main thread キューに積み <see cref="Invoker"/> 経由で実行
    /// 5. 結果を <c>{"type":"result"|"error","id":...,...}</c> として bridge に書き戻す
    ///
    /// スレッドモデル:
    /// - 受信ループはバックグラウンドスレッド
    /// - ツール実行は <see cref="EditorApplication.update"/> 経由で main thread
    /// - 結果書き込みも main thread から (送信は短時間なので blocking でOK)
    ///
    /// Domain reload 対応:
    /// - <see cref="AssemblyReloadEvents.beforeAssemblyReload"/> で <c>{"type":"shutdown"}</c>
    ///   を送ってからソケットを閉じる。bridge 側はこれを見て受信した未完了 call を queue に保持する。
    /// - reload 後、AgentMCPServerBootstrap が <see cref="Connect"/> を再呼出しする。
    ///   bridge は再 hello を受け取って queue を flush する。
    ///
    /// P1 スコープ: 1 connection、再接続なし、最低限の call ルーティングのみ。
    /// </summary>
    internal sealed class AgentMCPBridgeClient
    {
        static AgentMCPBridgeClient _instance;

        public static AgentMCPBridgeClient Shared => _instance ??= new AgentMCPBridgeClient();

        TcpClient _tcp;
        NetworkStream _stream;
        StreamWriter _writer;

        const int MainThreadStallThresholdMs = MainThreadWatchdog.DefaultStallThresholdMs;
        StreamReader _reader;
        Thread _readerThread;
        volatile bool _running;
        volatile bool _connected;
        volatile bool _starting;

        readonly object _queueLock = new object();

        /// <summary>
        /// Serializes writes to <see cref="_writer"/>. Results normally go out from the main-thread
        /// pump, but the reader thread also replies directly when it rejects a call (main thread
        /// stalled), so the two paths must not interleave a line.
        /// </summary>
        readonly object _writeLock = new object();
        readonly Queue<PendingBridgeCall> _pending = new Queue<PendingBridgeCall>();
        bool _pumpRegistered;

        int _port;
        string _token;
        const string ProtocolVersion = "1";

        public bool IsConnected => _connected;
        public bool IsStarting => _starting && !_connected;
        public int Port => _port;

        public void MarkStarting() { _starting = true; }
        public void ClearStarting() { _starting = false; }

        // ─── Lifecycle ───

        public void Connect(int port, string token)
        {
            if (_running) return;
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Token must not be empty.", nameof(token));

            _port = port;
            _token = token;
            _running = true;

            try
            {
                _tcp = new TcpClient();
                _tcp.NoDelay = true;
                _tcp.Connect("127.0.0.1", port);
                _stream = _tcp.GetStream();
                _reader = new StreamReader(_stream, new UTF8Encoding(false));
                _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

                // Send hello (synchronous)
                SendHello();

                // Wait for hello_ack on this thread (short timeout)
                _stream.ReadTimeout = 5000;
                string ackLine = _reader.ReadLine();
                _stream.ReadTimeout = System.Threading.Timeout.Infinite;
                if (string.IsNullOrEmpty(ackLine))
                    throw new InvalidOperationException("Bridge did not respond to hello.");
                var ack = JNode.Parse(ackLine);
                if (ack["type"].AsString != "hello_ack" || !ack["ok"].AsBool)
                {
                    string err = ack["error"].AsString ?? "unknown";
                    throw new InvalidOperationException($"Bridge rejected hello: {err}");
                }

                _connected = true;
                _starting = false;
                AgentLogger.Info(LogTag.MCP, $"[BridgeClient] connected to bridge at 127.0.0.1:{port}");

                _readerThread = new Thread(ReaderLoop)
                {
                    IsBackground = true,
                    Name = "UnityAgent-BridgeReader",
                };
                _readerThread.Start();

                RegisterPump();
            }
            catch (Exception ex)
            {
                _running = false;
                _connected = false;
                CleanupSocket();
                // Warning 止まりにする。接続は監視 tick から無限に再試行されるので、Error にすると
                // bridge バイナリ未ビルドのようなケースで 30 秒ごとに赤エラーが永久に積もる。
                // 例外はそのまま投げるので、手動トグルなど「1 回だけ試す」呼び出し元は従来どおり扱える。
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] connect failed: {ex.Message}");
                throw;
            }
        }

        public void Disconnect(string reason)
        {
            if (!_running) return;
            _running = false;

            // Same lock as SendResultBack / SendErrorFrame so this frame is not spliced into a
            // half-written one. But NEVER block indefinitely: this runs on the main thread during
            // assembly reload, and the lock holder is a background thread inside a socket write
            // that may not return. Skipping the courtesy shutdown frame is far cheaper than
            // hanging the editor through a domain reload.
            bool writeLockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(_writeLock, 500, ref writeLockTaken);
                if (!writeLockTaken)
                    AgentLogger.Warning(LogTag.MCP, "[BridgeClient] shutdown frame skipped — writer busy");
                else if (_writer != null && _connected)
                {
                    var msg = JNode.Obj(
                        ("type", JNode.Str("shutdown")),
                        ("reason", JNode.Str(reason ?? "unknown"))
                    );
                    _writer.WriteLine(msg.ToJson());
                }
            }
            catch { }
            finally
            {
                if (writeLockTaken) System.Threading.Monitor.Exit(_writeLock);
            }

            CleanupSocket();
            UnregisterPump();

            // Cancel any in-flight pending calls — bridge will time out the corresponding HTTP requests.
            int dropped = 0;
            lock (_queueLock)
            {
                while (_pending.Count > 0)
                {
                    var c = _pending.Dequeue();
                    AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] dropping pending call id={c.ID} tool={c.Tool} argsBytes={c.Args?.ToJson().Length ?? 0} on disconnect");
                    dropped++;
                }
            }
            if (dropped > 0)
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] disconnect dropped {dropped} pending call(s) (reason={reason})");

            _connected = false;
            _starting = false;
            AgentLogger.Info(LogTag.MCP, $"[BridgeClient] disconnected (reason={reason})");
        }

        void CleanupSocket()
        {
            // Prefer to dispose the writer under the lock so a concurrent send is not holding it.
            // Do not wait forever, for the same reason as Disconnect: a blocked socket write would
            // otherwise take the main thread down with it. If the lock cannot be had, closing the
            // underlying socket below unblocks that write anyway (it throws, and SendResultBack
            // already treats a failed send as a dropped result).
            bool writeLockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(_writeLock, 500, ref writeLockTaken);
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }
            finally
            {
                if (writeLockTaken) System.Threading.Monitor.Exit(_writeLock);
            }

            try { _reader?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            _reader = null;
            _stream = null;
            _tcp = null;
        }

        void SendHello()
        {
            var msg = JNode.Obj(
                ("type", JNode.Str("hello")),
                ("version", JNode.Str(ProtocolVersion)),
                ("token", JNode.Str(_token))
            );
            _writer.WriteLine(msg.ToJson());
            AgentLogger.Debug(LogTag.MCP, $"[BridgeClient] sent hello (version={ProtocolVersion}, token.len={_token?.Length ?? 0})");
        }

        // ─── Reader loop (background thread) ───

        void ReaderLoop()
        {
            try
            {
                while (_running)
                {
                    string line = _reader.ReadLine();
                    if (line == null)
                    {
                        AgentLogger.Info(LogTag.MCP, "[BridgeClient] reader EOF; bridge closed connection");
                        break;
                    }
                    if (line.Length == 0) continue;

                    AgentLogger.Debug(LogTag.MCP, $"[BridgeClient] recv bytes={line.Length}");

                    JNode msg;
                    try { msg = JNode.Parse(line); }
                    catch (Exception ex)
                    {
                        AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] parse error: {ex.Message} (line.len={line.Length})");
                        continue;
                    }

                    string type = msg["type"].AsString ?? "";
                    if (type != "call")
                    {
                        AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] ignoring unknown message type: {type}");
                        continue;
                    }

                    var call = new PendingBridgeCall
                    {
                        ID = msg["id"].AsString ?? "",
                        Tool = msg["tool"].AsString ?? "",
                        Args = msg["args"] ?? JNode.Obj(),
                    };
                    // ログ・RaiseCallStart・統計の 3 箇所で使うので、ここで 1 度だけ作る。
                    call.ArgsJson = call.Args.ToJson() ?? "{}";

                    // メインスレッドが詰まっているなら即座に原因を返す。キューに積むと
                    // タイムアウトまで待たされたうえ "Timeout" としか分からない。
                    if (MainThreadWatchdog.TryDescribeStall(MainThreadStallThresholdMs, out string stallMsg))
                    {
                        AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] REJECTED (main thread stalled) id={call.ID} tool={call.Tool}");
                        // 拒否も失敗 1 件として統計に残す。ここを落とすと、モーダルで拒否され
                        // 続けている間だけ統計が空になり、成功率 100% に見えてしまう。
                        // 記録は UI イベントを発火しないので reader スレッドから呼んで安全。
                        AgentMCPServer.RecordStallRejection(call.Tool, call.ArgsJson.Length, stallMsg);
                        // Write the error frame directly. PendingCall.SetError fires
                        // AgentMCPServer.RaiseCallFinish synchronously, and the in-editor chat window
                        // subscribes to that — touching UI Toolkit and mutating its history list.
                        // Both are main-thread-only, and this is the reader thread.
                        SendErrorFrame(call.ID, call.Tool, "Unity main thread blocked", stallMsg, -32002);
                        continue;
                    }

                    int qdepth;
                    lock (_queueLock)
                    {
                        _pending.Enqueue(call);
                        qdepth = _pending.Count;
                    }
                    AgentLogger.Debug(LogTag.MCP, $"[BridgeClient] recv call id={call.ID} tool={call.Tool} argsBytes={call.ArgsJson.Length} qdepth={qdepth}");
                }
            }
            catch (IOException ex)
            {
                if (_running)
                    AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] reader IO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                AgentLogger.Error(LogTag.MCP, $"[BridgeClient] reader unhandled error: {ex}");
            }
            finally
            {
                _connected = false;
            }
        }

        // ─── Main-thread pump ───

        void RegisterPump()
        {
            if (_pumpRegistered) return;
            EditorApplication.update += PumpMainThread;
            _pumpRegistered = true;
        }

        void UnregisterPump()
        {
            if (!_pumpRegistered) return;
            EditorApplication.update -= PumpMainThread;
            _pumpRegistered = false;
        }

        void PumpMainThread()
        {
            // NotePump だけでは compiling / importing / autoRefresh が初期値のまま固定され、
            // GetEditorState が「snapshotAge 0.00s / compiling False」と新鮮な計測値のように
            // 嘘をつく。InProc 側と同じ集約入口を通す。
            MainThreadWatchdog.NoteMainThreadTick();

            const int MaxPerFrame = 4;
            for (int i = 0; i < MaxPerFrame; i++)
            {
                PendingBridgeCall call;
                int remaining;
                lock (_queueLock)
                {
                    if (_pending.Count == 0) return;
                    call = _pending.Dequeue();
                    remaining = _pending.Count;
                }

                AgentLogger.Debug(LogTag.MCP, $"[BridgeClient] pump dispatch id={call.ID} tool={call.Tool} remaining={remaining}");

                // Forward to existing Invoker via a shim PendingCall.
                // The bridge call's id is bridge-internal; we keep it in BridgePendingId so we
                // can tag the response message back to the bridge.
                // argsJson は reader スレッドで組み立て済み。長さを渡して再シリアライズを省く。
                var inner = new PendingCall(call.Tool, call.Args, call.ArgsJson?.Length ?? -1);
                inner.BridgePendingId = call.ID;
                inner.OnComplete = (resultCall) => SendResultBack(resultCall);

                // RaiseCallStart so UnityAgentWindow can show it in chat (same as InProc path)
                AgentMCPServer.RaiseCallStart(call.Tool, call.ArgsJson ?? "{}");

                MainThreadWatchdog.NoteToolStart(call.Tool);
                // 統計の所要時間は「実行に要した時間」に統一する。キュー待ちはここまで
                // (InProc 側の pump も同じ位置で印を付けている)。
                inner.MarkExecutionStart();
                try
                {
                    Invoker.Invoke(inner);
                }
                catch (Exception ex)
                {
                    AgentLogger.Error(LogTag.MCP, $"[BridgeClient] Invoker unhandled exception id={call.ID} tool={call.Tool}: {ex.Message}");
                    inner.SetError($"Invoker exception: {ex.Message}", ex.ToString(), -32603);
                }
                finally
                {
                    MainThreadWatchdog.NoteToolFinish();
                }
            }
        }

        /// <summary>
        /// Writes an error frame without going through <see cref="PendingCall"/>. Safe to call from
        /// the reader thread: it raises no events and touches no Unity API.
        /// </summary>
        void SendErrorFrame(string id, string tool, string message, string data, int code)
        {
            if (!_connected || _writer == null)
            {
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] error frame dropped (disconnected) id={id} tool={tool}");
                return;
            }
            try
            {
                var msg = JNode.Obj(
                    ("type", JNode.Str("error")),
                    ("id", JNode.Str(id ?? "")),
                    ("code", JNode.Num(code)),
                    ("message", JNode.Str(message ?? "")),
                    ("data", JNode.Str(data ?? ""))
                );
                string wire = msg.ToJson();
                lock (_writeLock)
                {
                    if (_writer == null) return;
                    _writer.WriteLine(wire);
                }
                AgentLogger.Debug(LogTag.MCP, $"[BridgeClient] send error id={id} tool={tool} bytes={wire.Length} code={code}");
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] failed to send error frame id={id} tool={tool}: {ex.Message}");
            }
        }

        void SendResultBack(PendingCall call)
        {
            // Note: PendingCall.SetResult/SetError already raises OnCallFinish; do not duplicate it here.
            if (!_connected || _writer == null)
            {
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] send dropped (disconnected) id={call.BridgePendingId} tool={call.ToolName}");
                return;
            }
            try
            {
                JNode msg;
                string kind;
                if (call.Error != null)
                {
                    kind = "error";
                    msg = JNode.Obj(
                        ("type", JNode.Str("error")),
                        ("id", JNode.Str(call.BridgePendingId ?? "")),
                        ("code", JNode.Num(call.ErrorCode)),
                        ("message", JNode.Str(call.Error)),
                        ("data", JNode.Str(call.ErrorData ?? ""))
                    );
                }
                else
                {
                    kind = "result";
                    var fields = new List<(string, JNode)>
                    {
                        ("type", JNode.Str("result")),
                        ("id", JNode.Str(call.BridgePendingId ?? "")),
                        ("ok", JNode.Bool(true)),
                        ("text", JNode.Str(call.ResultText ?? "")),
                    };
                    if (call.ImageBytes != null && call.ImageBytes.Length > 0)
                    {
                        // Bridge relays an optional base64 image alongside the text summary
                        // so the remote MCP-facing process can emit an MCP image content
                        // block instead of just the summary string.
                        fields.Add(("imageBase64", JNode.Str(Convert.ToBase64String(call.ImageBytes))));
                        fields.Add(("imageMimeType", JNode.Str(call.ImageMimeType ?? "image/png")));
                    }
                    msg = JNode.Obj(fields.ToArray());
                }
                string wire = msg.ToJson();
                lock (_writeLock)
                {
                    // Re-check inside the lock: CleanupSocket may have disposed and nulled the
                    // writer between the guard at the top of this method and here.
                    if (_writer == null)
                    {
                        AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] send dropped (socket closed) id={call.BridgePendingId} tool={call.ToolName}");
                        return;
                    }
                    _writer.WriteLine(wire);
                }
                AgentLogger.Debug(LogTag.MCP,
                    $"[BridgeClient] send {kind} id={call.BridgePendingId} tool={call.ToolName} bytes={wire.Length} textBytes={(call.ResultText?.Length ?? 0)} imgBytes={(call.ImageBytes?.Length ?? 0)}");
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.MCP, $"[BridgeClient] failed to send result back id={call.BridgePendingId} tool={call.ToolName}: {ex.Message}");
            }
        }
    }

    internal sealed class PendingBridgeCall
    {
        public string ID;
        public string Tool;
        public JNode Args;

        /// <summary>
        /// 受信フレームの args をシリアライズし直した文字列。ログ・RaiseCallStart・統計の
        /// 文字数で 3 回使うので、reader スレッドで 1 度だけ作って持ち回る。
        /// </summary>
        public string ArgsJson;
    }
}
