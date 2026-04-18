using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// UnityAgent が他のエージェントに向けて公開する MCP サーバー (HTTP 上の JSON-RPC 2.0)。
    /// 外部クライアント (Claude Code, Cursor 等) は HTTP POST /mcp でツール呼び出しを行う。
    ///
    /// スレッドモデル:
    ///   - HttpListener 受信ループはバックグラウンドスレッド。
    ///   - JSON-RPC のパースもバックグラウンドで行い、ツール実行が必要な場合は
    ///     メインスレッドキューに積んで <see cref="EditorApplication.update"/> で drain する。
    ///   - バックグラウンドスレッドは ManualResetEventSlim で結果を待機し、HTTP レスポンスを返却する。
    /// </summary>
    internal sealed class AgentMCPServer
    {
        const string EndpointPath = "/mcp";
        const int MaxRequestBodyBytes = 2 * 1024 * 1024; // 2MB
        const int DefaultCallTimeoutMs = 120_000;

        static AgentMCPServer _shared;

        /// <summary>プロセス内で 1 インスタンスを共有する。</summary>
        public static AgentMCPServer Shared => _shared ??= new AgentMCPServer();

        HttpListener _listener;
        Thread _listenerThread;
        volatile bool _running;
        int _port;
        string _token;

        readonly object _queueLock = new object();
        readonly Queue<PendingCall> _pendingCalls = new Queue<PendingCall>();
        bool _pumpRegistered;

        public bool IsRunning => _running;
        public int Port => _port;
        public string Endpoint => _running ? $"http://localhost:{_port}{EndpointPath}" : "";

        /// <summary>起動回数の粗いカウンタ (UI 表示用)。</summary>
        public int TotalCallsServed { get; private set; }

        /// <summary>
        /// 外部エージェントからツール呼び出しが到着したときに発火するイベント。
        /// UnityAgentWindow 等の UI 層がチャット表示するために購読する。
        /// <see cref="PumpMainThread"/> から main thread で呼ばれる。
        /// </summary>
        public static event Action<string /*toolName*/, string /*argsJson*/> OnCallStart;

        /// <summary>ツール呼び出しが完了したときに発火するイベント。</summary>
        public static event Action<string /*toolName*/, string /*resultOrError*/, bool /*isError*/> OnCallFinish;

        /// <summary>
        /// AskUser 等でユーザー選択待ちになったときに発火するイベント。
        /// UnityAgentWindow が Choice エントリを chat に追加するために購読する。
        /// 既に <see cref="UserChoiceState.IsPending"/> は true になっている前提。
        /// </summary>
        public static event Action OnUserChoiceRequested;

        internal static void RaiseCallStart(string toolName, string argsJson)
        {
            try { OnCallStart?.Invoke(toolName, argsJson); } catch { }
        }

        internal static void RaiseCallFinish(string toolName, string resultOrError, bool isError)
        {
            try { OnCallFinish?.Invoke(toolName, resultOrError, isError); } catch { }
        }

        internal static void RaiseUserChoiceRequested()
        {
            try { OnUserChoiceRequested?.Invoke(); } catch { }
        }

        // ─── Lifecycle ───

        public void Start(int port, string token)
        {
            if (_running) return;
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Token must not be empty.", nameof(token));

            _port = port;
            _token = token;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                _listener?.Close();
                _listener = null;
                AgentLogger.Error(LogTag.MCP, $"MCP Server failed to bind port {port}: {ex.Message}");
                throw;
            }

            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "UnityAgent-MCPServer"
            };
            _listenerThread.Start();

            RegisterPump();
            AgentLogger.Info(LogTag.MCP, $"MCP Server started at http://localhost:{port}{EndpointPath} (token.len={token?.Length ?? 0}, timeoutMs={DefaultCallTimeoutMs}, maxBody={MaxRequestBodyBytes})");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _listenerThread = null;

            // 保留中の呼び出しをすべてキャンセル応答にする
            lock (_queueLock)
            {
                while (_pendingCalls.Count > 0)
                {
                    var call = _pendingCalls.Dequeue();
                    call.SetError("Server stopped before call could be dispatched.");
                }
            }

            UnregisterPump();
            AgentLogger.Info(LogTag.MCP, $"MCP Server stopped. (served={TotalCallsServed})");
        }

        /// <summary>静的ヘルパ。Window / Menu 等からの呼び出し用。</summary>
        public static void StartShared()
        {
            var s = Shared;
            if (s.IsRunning) return;
            int port = AgentSettings.MCPServerPort;
            string token = AgentSettings.EnsureMCPServerToken();
            try
            {
                s.Start(port, token);
            }
            catch (Exception ex)
            {
                AgentLogger.Error(LogTag.MCP, $"MCP Server start failed: {ex.Message}");
            }
        }

        public static void StopShared()
        {
            _shared?.Stop();
        }

        // ─── HTTP listener loop (background thread) ───

        void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (Exception ex)
                {
                    // 正常 Stop: Stop() が _running=false を立てて listener.Stop() するため
                    // GetContext が例外化する。その場合は無言 break が正しい。
                    // _running が true のまま例外が飛んできた場合は、listener が予期せず死んだ可能性が高い。
                    if (_running)
                    {
                        AgentLogger.Error(LogTag.MCP,
                            $"MCP Server ListenLoop died unexpectedly: {ex.GetType().Name}: {ex.Message}");
                        _running = false;
                    }
                    break;
                }

                // 並行処理: SSE stream (GET /mcp) が長期保持されるため、
                // ListenLoop をブロックしないよう ThreadPool に流す。
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        HandleRequest(ctx);
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Warning(LogTag.MCP, $"MCP Server request error: {ex.Message}");
                        TraceLog($"EXCEPTION {ex}");
                        TryWriteError(ctx, 500, "Internal error");
                    }
                });
            }
        }

        void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // デバッグログ: 認証系の問題調査用
            string remote = req.RemoteEndPoint?.ToString() ?? "?";
            string ua = req.UserAgent ?? "-";
            TraceLog($"REQ {req.HttpMethod} {req.Url.AbsolutePath} remote={remote} len={req.ContentLength64} ct={req.ContentType ?? "-"} auth={(req.Headers["Authorization"] != null ? "present" : "MISSING")} accept={req.Headers["Accept"]} ua={ua}");

            // CORS (ローカルツール用)
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 204;
                resp.ContentLength64 = 0;
                resp.OutputStream.Close();
                return;
            }

            string path = req.Url.AbsolutePath;

            // GET / → 簡易ヘルスエンドポイント (Authorization 不要)
            if (req.HttpMethod == "GET" && (path == "/" || path == "/health"))
            {
                AgentLogger.Debug(LogTag.MCP, $"oauth health probe remote={remote} path={path}");
                WriteJson(resp, 200, $"{{\"server\":\"UnityAgent\",\"endpoint\":\"{EndpointPath}\",\"status\":\"ok\"}}");
                return;
            }

            // ── OAuth 2.1 discovery (minimal fake flow) ──
            // Claude Code の MCP SDK は OAuth discovery が完全に通らないと
            // 「authenticated」扱いにならない。UnityAgent はローカル専用なので
            // OAuth フローを偽装して静的トークンをそのまま access_token として返す。
            string origin = $"http://localhost:{_port}";

            // RFC 9728: Protected Resource Metadata
            if (req.HttpMethod == "GET" && (
                path == "/.well-known/oauth-protected-resource" ||
                path == "/.well-known/oauth-protected-resource/mcp"))
            {
                AgentLogger.Debug(LogTag.MCP, $"oauth discovery protected-resource path={path} remote={remote}");
                string md = "{" +
                    "\"resource\":\"" + origin + EndpointPath + "\"," +
                    "\"authorization_servers\":[\"" + origin + "\"]," +
                    "\"bearer_methods_supported\":[\"header\"]," +
                    "\"resource_name\":\"UnityAgent\"" +
                    "}";
                WriteJson(resp, 200, md);
                return;
            }

            // RFC 8414: Authorization Server Metadata
            if (req.HttpMethod == "GET" && (
                path == "/.well-known/oauth-authorization-server" ||
                path == "/.well-known/oauth-authorization-server/mcp" ||
                path == "/.well-known/openid-configuration" ||
                path == "/.well-known/openid-configuration/mcp" ||
                path == "/mcp/.well-known/openid-configuration"))
            {
                AgentLogger.Debug(LogTag.MCP, $"oauth discovery authz-server path={path} remote={remote}");
                string md = "{" +
                    "\"issuer\":\"" + origin + "\"," +
                    "\"authorization_endpoint\":\"" + origin + "/authorize\"," +
                    "\"token_endpoint\":\"" + origin + "/token\"," +
                    "\"registration_endpoint\":\"" + origin + "/register\"," +
                    "\"response_types_supported\":[\"code\"]," +
                    "\"response_modes_supported\":[\"query\"]," +
                    "\"grant_types_supported\":[\"authorization_code\",\"refresh_token\"]," +
                    "\"token_endpoint_auth_methods_supported\":[\"none\"]," +
                    "\"code_challenge_methods_supported\":[\"S256\",\"plain\"]," +
                    "\"scopes_supported\":[\"mcp\"]" +
                    "}";
                WriteJson(resp, 200, md);
                return;
            }

            // RFC 7591: Dynamic Client Registration
            if (req.HttpMethod == "POST" && path == "/register")
            {
                AgentLogger.Debug(LogTag.MCP, $"oauth register → client=unity-agent-local remote={remote}");
                long nowSec = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                string clientMd = "{" +
                    "\"client_id\":\"unity-agent-local\"," +
                    "\"client_id_issued_at\":" + nowSec + "," +
                    "\"grant_types\":[\"authorization_code\",\"refresh_token\"]," +
                    "\"response_types\":[\"code\"]," +
                    "\"redirect_uris\":[]," +
                    "\"token_endpoint_auth_method\":\"none\"" +
                    "}";
                WriteJson(resp, 201, clientMd);
                return;
            }

            // RFC 6749: Authorization endpoint — 即時リダイレクトで code を発行する。
            // UnityAgent はローカル専用のため本物の user consent 画面は不要。
            if (req.HttpMethod == "GET" && path == "/authorize")
            {
                string query = req.Url.Query ?? "";
                var qp = ParseQuery(query);
                qp.TryGetValue("redirect_uri", out string redirectUri);
                qp.TryGetValue("state", out string state);
                if (string.IsNullOrEmpty(redirectUri))
                {
                    AgentLogger.Warning(LogTag.MCP, $"oauth authorize rejected: redirect_uri missing (remote={remote})");
                    WriteJson(resp, 400, "{\"error\":\"invalid_request\",\"error_description\":\"redirect_uri required\"}");
                    return;
                }
                string code = Guid.NewGuid().ToString("N");
                string sep = redirectUri.Contains("?") ? "&" : "?";
                string target = redirectUri + sep + "code=" + Uri.EscapeDataString(code);
                if (!string.IsNullOrEmpty(state))
                    target += "&state=" + Uri.EscapeDataString(state);
                string redirectHost = "?";
                try { redirectHost = new Uri(redirectUri).Host; } catch { }
                AgentLogger.Debug(LogTag.MCP, $"oauth authorize → 302 redirect_host={redirectHost} state_present={!string.IsNullOrEmpty(state)} remote={remote}");
                resp.StatusCode = 302;
                resp.Headers["Location"] = target;
                resp.ContentLength64 = 0;
                try { resp.OutputStream.Close(); } catch { }
                return;
            }

            // RFC 6749: Token endpoint — 常に現在の静的トークンを access_token として返す。
            // localhost 限定なので client_id/secret 検証や grant_type 分岐は行わない。
            if (req.HttpMethod == "POST" && path == "/token")
            {
                string tok = AgentSettings.MCPServerToken ?? "";
                if (string.IsNullOrEmpty(tok)) tok = AgentSettings.EnsureMCPServerToken();
                // セキュリティ: access_token の値自体はログに出さない (長さのみ)。
                AgentLogger.Debug(LogTag.MCP, $"oauth token issued (len={tok.Length}, expires=31536000) remote={remote}");
                string tokenMd = "{" +
                    "\"access_token\":\"" + tok + "\"," +
                    "\"token_type\":\"Bearer\"," +
                    "\"expires_in\":31536000," +
                    "\"scope\":\"mcp\"" +
                    "}";
                WriteJson(resp, 200, tokenMd);
                return;
            }

            if (path != EndpointPath)
            {
                WriteJson(resp, 404, "{\"error\":\"not_found\"}");
                return;
            }

            // GET /mcp (Accept: text/event-stream) → MCP Streamable HTTP の
            // サーバー送信イベントチャネル。Claude Code はこれが貼れないと
            // 「capabilities: none」扱いにするため、認証が通っていれば空ストリームを維持する。
            if (req.HttpMethod == "GET")
            {
                string accept = req.Headers["Accept"] ?? "";
                if (accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!CheckAuth(req))
                    {
                        WriteJson(resp, 200, "{\"error\":\"unauthorized\"}");
                        return;
                    }
                    HandleSseStream(ctx);
                    return;
                }

                WriteJson(resp, 405, "{\"error\":\"method_not_allowed\"}");
                return;
            }

            if (req.HttpMethod != "POST")
            {
                WriteJson(resp, 405, "{\"error\":\"method_not_allowed\"}");
                return;
            }

            // Body read (size capped)
            string body;
            try
            {
                body = ReadBody(req);
            }
            catch (Exception ex)
            {
                WriteJson(resp, 413, "{\"error\":\"" + JsonEscapeInline(ex.Message) + "\"}");
                return;
            }

            JNode root;
            try
            {
                root = JNode.Parse(body);
            }
            catch (Exception ex)
            {
                WriteJsonRpcError(resp, JNode.NullNode, -32700, "Parse error", ex.Message);
                return;
            }

            TraceLog($"  body: {(body.Length > 400 ? body.Substring(0, 400) + "..." : body)}");

            if (root == null || root.Type != JNode.JType.Object)
            {
                WriteJsonRpcError(resp, JNode.NullNode, -32600, "Invalid Request", "Root must be a JSON object.");
                return;
            }

            // Bearer token 認証。
            // 注: HTTP 401 を返すと Claude Code の MCP SDK は OAuth 2.1 discovery flow を
            // 発火させ、static Bearer header の設定を無視してしまう。よって HTTP 層では
            // 常に 200 を返し、認証失敗は JSON-RPC error として body で通知する。
            if (!CheckAuth(req))
            {
                JNode idNode = root["id"];
                AgentLogger.Warning(LogTag.MCP, $"Unauthorized JSON-RPC request from {remote} (method={root["method"].AsString ?? "?"}, ua={ua})");
                WriteJsonRpcError(resp, idNode, -32001, "Unauthorized",
                    "Missing or invalid Authorization header. Provide 'Authorization: Bearer <token>'.");
                return;
            }

            DispatchJsonRpc(resp, root);
        }

        void DispatchJsonRpc(HttpListenerResponse resp, JNode root)
        {
            JNode idNode = root["id"];
            string method = root["method"].AsString ?? "";
            JNode paramsNode = root["params"];
            TraceLog($"  dispatch method={method} id={(idNode?.ToJson() ?? "null")}");

            if (string.IsNullOrEmpty(method))
            {
                WriteJsonRpcError(resp, idNode, -32600, "Invalid Request", "Missing method.");
                return;
            }

            // Notifications (no response expected)
            if (method.StartsWith("notifications/"))
            {
                resp.StatusCode = 202;
                resp.ContentLength64 = 0;
                resp.OutputStream.Close();
                return;
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        WriteJsonRpcResult(resp, idNode, Handlers.HandleInitialize(paramsNode));
                        return;
                    case "ping":
                        WriteJsonRpcResult(resp, idNode, JNode.Obj());
                        return;
                    case "tools/list":
                        WriteJsonRpcResult(resp, idNode, Handlers.HandleToolsList(paramsNode));
                        return;
                    case "prompts/list":
                        WriteJsonRpcResult(resp, idNode, JNode.Obj(("prompts", JNode.Arr())));
                        return;
                    case "resources/list":
                        WriteJsonRpcResult(resp, idNode, JNode.Obj(("resources", JNode.Arr())));
                        return;
                    case "resources/templates/list":
                        WriteJsonRpcResult(resp, idNode, JNode.Obj(("resourceTemplates", JNode.Arr())));
                        return;
                    case "tools/call":
                    {
                        string toolName = paramsNode["name"].AsString ?? "";
                        JNode args = paramsNode["arguments"];
                        string argsJson = (args ?? JNode.Obj()).ToJson();
                        var pending = new PendingCall(toolName, args ?? JNode.Obj());
                        int qdepth;
                        EnqueueCall(pending);
                        lock (_queueLock) { qdepth = _pendingCalls.Count; }
                        var sw = Stopwatch.StartNew();
                        AgentLogger.Debug(LogTag.MCP, $"tools/call ENQUEUE tool={toolName} argsBytes={argsJson.Length} qdepth={qdepth}");
                        TraceLog($"  tools/call enqueue tool={toolName} argsBytes={argsJson.Length} qdepth={qdepth}");

                        bool ok = pending.Wait(DefaultCallTimeoutMs);
                        sw.Stop();
                        if (!ok)
                        {
                            AgentLogger.Warning(LogTag.MCP, $"tools/call TIMEOUT tool={toolName} after {sw.ElapsedMilliseconds}ms (limit={DefaultCallTimeoutMs}ms)");
                            WriteJsonRpcError(resp, idNode, -32001, "Timeout",
                                $"Tool '{toolName}' did not complete within {DefaultCallTimeoutMs}ms.");
                            pending.Cancel();
                            return;
                        }

                        if (pending.Error != null)
                        {
                            AgentLogger.Warning(LogTag.MCP, $"tools/call ERROR tool={toolName} code={pending.ErrorCode} elapsed={sw.ElapsedMilliseconds}ms msg={pending.Error}");
                            WriteJsonRpcError(resp, idNode, pending.ErrorCode, pending.Error, pending.ErrorData);
                            return;
                        }

                        TotalCallsServed++;
                        AgentLogger.Debug(LogTag.MCP,
                            $"tools/call DONE tool={toolName} elapsed={sw.ElapsedMilliseconds}ms textBytes={(pending.ResultText?.Length ?? 0)} imgBytes={(pending.ImageBytes?.Length ?? 0)} served={TotalCallsServed}");

                        var contentNodes = new List<JNode>
                        {
                            JNode.Obj(
                                ("type", JNode.Str("text")),
                                ("text", JNode.Str(pending.ResultText ?? "")))
                        };
                        if (pending.ImageBytes != null && pending.ImageBytes.Length > 0)
                        {
                            contentNodes.Add(JNode.Obj(
                                ("type", JNode.Str("image")),
                                ("data", JNode.Str(Convert.ToBase64String(pending.ImageBytes))),
                                ("mimeType", JNode.Str(pending.ImageMimeType ?? "image/png"))));
                        }

                        var result = JNode.Obj(
                            ("content", JNode.Arr(contentNodes.ToArray())),
                            ("isError", JNode.Bool(false))
                        );
                        WriteJsonRpcResult(resp, idNode, result);
                        return;
                    }
                    default:
                        WriteJsonRpcError(resp, idNode, -32601, "Method not found", method);
                        return;
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Error(LogTag.MCP, $"MCP dispatch error: {ex}");
                string detail = DeveloperMode.IsDevBuild ? ex.ToString() : ex.Message;
                WriteJsonRpcError(resp, idNode, -32603, "Internal error", detail);
            }
        }

        // ─── SSE stream (GET /mcp) ───

        void HandleSseStream(HttpListenerContext ctx)
        {
            var resp = ctx.Response;
            TraceLog("  SSE stream opened");
            try
            {
                resp.StatusCode = 200;
                resp.ContentType = "text/event-stream";
                resp.Headers["Cache-Control"] = "no-cache";
                resp.Headers["Connection"] = "keep-alive";
                resp.SendChunked = true;

                var output = resp.OutputStream;
                var keepalive = Encoding.UTF8.GetBytes(": keepalive\n\n");

                while (_running)
                {
                    try
                    {
                        output.Write(keepalive, 0, keepalive.Length);
                        output.Flush();
                    }
                    catch (Exception)
                    {
                        // Client disconnected
                        break;
                    }
                    Thread.Sleep(15000);
                }
            }
            catch (Exception ex)
            {
                TraceLog($"  SSE error: {ex.Message}");
            }
            finally
            {
                TraceLog("  SSE stream closed");
                try { resp.OutputStream.Close(); } catch { }
                try { resp.Close(); } catch { }
            }
        }

        // ─── Main thread pump ───

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
            // 1 フレームで最大 N 件処理 (Editor UI を塞がないため)
            const int MaxPerFrame = 4;
            int processed = 0;
            while (processed < MaxPerFrame)
            {
                PendingCall call;
                lock (_queueLock)
                {
                    if (_pendingCalls.Count == 0) return;
                    call = _pendingCalls.Dequeue();
                }

                if (call.Cancelled) { processed++; continue; }

                int remaining;
                lock (_queueLock) { remaining = _pendingCalls.Count; }
                TraceLog($"  pump dispatch tool={call.ToolName} remaining={remaining}");

                RaiseCallStart(call.ToolName, call.Arguments?.ToJson() ?? "{}");

                try
                {
                    Invoker.Invoke(call);
                }
                catch (Exception ex)
                {
                    AgentLogger.Error(LogTag.MCP, $"Invoker unhandled exception tool={call.ToolName}: {ex.Message}");
                    call.SetError($"Invoker exception: {ex.Message}", ex.ToString(), -32603);
                }
                processed++;
            }
        }

        void EnqueueCall(PendingCall call)
        {
            lock (_queueLock)
            {
                _pendingCalls.Enqueue(call);
            }
        }

        // ─── Helpers ───

        bool CheckAuth(HttpListenerRequest req)
        {
            var header = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(header)) return false;
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            string supplied = header.Substring(7).Trim();
            return ConstantTimeEquals(supplied, _token);
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static string ReadBody(HttpListenerRequest req)
        {
            using (var ms = new MemoryStream())
            {
                var buf = new byte[4096];
                int total = 0;
                int read;
                while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    total += read;
                    if (total > MaxRequestBodyBytes)
                        throw new InvalidOperationException($"Request body exceeds {MaxRequestBodyBytes} bytes.");
                    ms.Write(buf, 0, read);
                }
                var enc = req.ContentEncoding ?? Encoding.UTF8;
                return enc.GetString(ms.ToArray());
            }
        }

        static void WriteJson(HttpListenerResponse resp, int status, string json)
        {
            try
            {
                resp.StatusCode = status;
                resp.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(json);
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.OutputStream.Close();
            }
            catch (Exception) { }
        }

        static void WriteJsonRpcResult(HttpListenerResponse resp, JNode id, JNode result)
        {
            var root = JNode.Obj(
                ("jsonrpc", JNode.Str("2.0")),
                ("id", id ?? JNode.NullNode),
                ("result", result ?? JNode.Obj())
            );
            string json = root.ToJson();
            TraceLog($"  response size={json.Length}");
            WriteJson(resp, 200, json);
        }

        static void WriteJsonRpcError(HttpListenerResponse resp, JNode id, int code, string message, string data)
        {
            var errObj = JNode.Obj(
                ("code", JNode.Num(code)),
                ("message", JNode.Str(message ?? "Error")),
                ("data", JNode.Str(data ?? ""))
            );
            var root = JNode.Obj(
                ("jsonrpc", JNode.Str("2.0")),
                ("id", id ?? JNode.NullNode),
                ("error", errObj)
            );
            // JSON-RPC: transport は 200 でも良いが、クライアントの可読性のためステータスは常に 200。
            WriteJson(resp, 200, root.ToJson());
        }

        static void TryWriteError(HttpListenerContext ctx, int status, string message)
        {
            try
            {
                WriteJson(ctx.Response, status, "{\"error\":\"" + JsonEscapeInline(message) + "\"}");
            }
            catch (Exception ex)
            {
                // レスポンス書き込みに失敗するケース: client disconnect / already closed stream。
                // これ自体は致命ではないが、無言で捨てると上流の 500 応答が届かない原因を追跡できないため残す。
                TraceLog($"  TryWriteError failed (status={status}): {ex.Message}");
            }
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;
            if (query[0] == '?') query = query.Substring(1);
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    result[Uri.UnescapeDataString(pair)] = "";
                }
                else
                {
                    string k = Uri.UnescapeDataString(pair.Substring(0, eq));
                    string v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    result[k] = v;
                }
            }
            return result;
        }

        // ─── Debug trace (temporary) ───
        static readonly object _traceLock = new object();
        static string _traceFilePath;

        static string TracePath
        {
            get
            {
                if (_traceFilePath == null)
                {
                    try
                    {
                        string projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                        _traceFilePath = System.IO.Path.Combine(projectRoot, "Temp", "UnityAgent-MCPServer.log");
                    }
                    catch
                    {
                        _traceFilePath = "";
                    }
                }
                return _traceFilePath;
            }
        }

        internal static void TraceLog(string line)
        {
            // AgentLogWindow 用のインメモリバッファにも流す (DebugMode で可視化)。
            // 開発者モード (DLL 非配置) の場合のみ Temp/UnityAgent-MCPServer.log にも追記する。
            try { AgentLogger.Debug(LogTag.MCP, line); } catch { }

            if (!DeveloperMode.IsDevBuild) return;
            try
            {
                string path = TracePath;
                if (string.IsNullOrEmpty(path)) return;
                lock (_traceLock)
                {
                    System.IO.File.AppendAllText(path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
                }
            }
            catch { }
        }

        static string JsonEscapeInline(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// HTTP スレッドとメインスレッド間で受け渡されるツール呼び出しのコンテキスト。
    /// InProc モード (HTTP listener) と Bridge モード (TCP client) の両方から共用する。
    /// </summary>
    internal sealed class PendingCall
    {
        public string ToolName { get; private set; }
        public JNode Arguments { get; private set; }

        /// <summary>ExecuteUnityTool が target ツールに再ディスパッチするために name/args を差し替える。</summary>
        public void Rewrite(string newName, JNode newArgs)
        {
            ToolName = newName ?? "";
            Arguments = newArgs ?? JNode.Obj();
        }
        public string ResultText { get; private set; }

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

        public PendingCall(string toolName, JNode arguments)
        {
            ToolName = toolName ?? "";
            Arguments = arguments ?? JNode.Obj();
        }

        public void SetResult(string text)
        {
            ResultText = text ?? "";
            AgentMCPServer.RaiseCallFinish(ToolName, ResultText, false);
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
            Error = message ?? "Unknown error";
            ErrorData = data ?? "";
            ErrorCode = code;
            AgentMCPServer.RaiseCallFinish(ToolName, Error, true);
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
    }
}
