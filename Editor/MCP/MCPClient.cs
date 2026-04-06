using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// MCP (Model Context Protocol) stdio client.
    /// Spawns an MCP server process and communicates via JSON-RPC 2.0 over stdin/stdout.
    /// </summary>
    internal sealed class MCPClient : IDisposable
    {
        public string ServerName { get; }
        public bool IsConnected { get; private set; }
        public List<MCPToolDef> Tools { get; private set; } = new List<MCPToolDef>();
        public List<MCPResource> Resources { get; private set; } = new List<MCPResource>();
        public List<MCPPrompt> Prompts { get; private set; } = new List<MCPPrompt>();
        public string LastError { get; private set; }

        /// <summary>プロセスが生存しているか確認する。</summary>
        public bool IsAlive => _process != null && !_process.HasExited;

        /// <summary>蓄積された STDERR ログを取得する（スレッドセーフ）。</summary>
        public string GetStderrLog()
        {
            lock (_lock) return _stderrBuilder.ToString();
        }

        /// <summary>蓄積された STDERR ログをクリアする。</summary>
        public void ClearStderrLog()
        {
            lock (_lock) _stderrBuilder.Clear();
        }

        /// <summary>サーバーから受信した通知キュー（lock で保護）。</summary>
        readonly Queue<MCPNotification> _pendingNotifications = new Queue<MCPNotification>();

        /// <summary>保留中の通知をスレッドセーフに取り出す。</summary>
        public List<MCPNotification> DrainNotifications()
        {
            lock (_lock)
            {
                if (_pendingNotifications.Count == 0) return null;
                var result = new List<MCPNotification>(_pendingNotifications);
                _pendingNotifications.Clear();
                return result;
            }
        }

        /// <summary>サーバーの capabilities (initialize で取得)。</summary>
        public JNode ServerCapabilities { get; private set; }

        readonly string _command;
        readonly string[] _args;
        readonly Dictionary<string, string> _env;

        System.Diagnostics.Process _process;
        readonly Queue<string> _lineQueue = new Queue<string>();
        readonly StringBuilder _stderrBuilder = new StringBuilder();
        readonly object _lock = new object();
        int _nextId = 1;
        bool _stdoutDone;

        const int TimeoutSeconds = 30;
        const string ProtocolVersion = "2024-11-05";

        public MCPClient(string name, string command, string[] args, Dictionary<string, string> env)
        {
            ServerName = name;
            _command = command;
            _args = args ?? Array.Empty<string>();
            _env = env ?? new Dictionary<string, string>();
        }

        // ─── Lifecycle ───

        /// <summary>Spawn the MCP server, perform initialize handshake, and discover tools.</summary>
        public IEnumerator Connect()
        {
            // Spawn process
            string argsStr = string.Join(" ", _args);
            AgentLogger.Info(LogTag.MCP, $"[{ServerName}] Connecting: {_command} {argsStr}");

            System.Diagnostics.Process process;
            try
            {
                var startInfo = BuildStartInfo(_command, argsStr);
                foreach (var kv in _env)
                {
                    startInfo.EnvironmentVariables[kv.Key] = kv.Value;
                    AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] ENV: {kv.Key}={kv.Value}");
                }

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    AgentLogger.Error(LogTag.MCP, $"[{ServerName}] Failed to start process.");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                LastError = $"Launch error: {ex.Message}";
                AgentLogger.Error(LogTag.MCP, $"[{ServerName}] {LastError}\n{ex.StackTrace}");
                yield break;
            }

            _process = process;
            AgentLogger.Info(LogTag.MCP, $"[{ServerName}] Process started. PID={process.Id}");

            // Async readers
            process.OutputDataReceived += (s, e) =>
            {
                lock (_lock)
                {
                    if (e.Data != null)
                    {
                        _lineQueue.Enqueue(e.Data);
                        // 接続完了前のみログ出力（接続後の通常通信はキューに入れるだけ）
                        if (!IsConnected)
                            AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] STDOUT: {e.Data.Substring(0, Math.Min(200, e.Data.Length))}...");
                    }
                    else
                    {
                        _stdoutDone = true;
                        AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] STDOUT: EOF");
                    }
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (_lock)
                        _stderrBuilder.AppendLine(e.Data);
                    // WARNING/ERROR は常に LogWarning
                    if (e.Data.Contains("ERROR") || e.Data.Contains("WARN"))
                        AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] STDERR: {e.Data}");
                    else if (!IsConnected)
                        AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] STDERR: {e.Data}");
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Brief delay to let the process start up
            yield return null;
            yield return null;

            // Check if process died immediately
            if (process.HasExited)
            {
                string stderr;
                lock (_lock) stderr = _stderrBuilder.ToString();
                LastError = $"Process exited immediately (ExitCode={process.ExitCode})";
                AgentLogger.Error(LogTag.MCP, $"[{ServerName}] {LastError}\nSTDERR:\n{stderr}");
                Dispose();
                yield break;
            }

            // --- Initialize handshake ---
            int initId = NextId();
            var initParams = JNode.Obj(
                ("protocolVersion", JNode.Str(ProtocolVersion)),
                ("capabilities", JNode.Obj()),
                ("clientInfo", JNode.Obj(
                    ("name", JNode.Str("UnityAgent")),
                    ("version", JNode.Str("1.0"))
                ))
            );
            AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Sending initialize request (id={initId})...");
            SendRequest("initialize", initParams, initId);

            JNode initResponse = null;
            yield return WaitForResponse(initId, r => initResponse = r);

            if (initResponse == null || initResponse["error"].Type != JNode.JType.Null)
            {
                string errMsg = initResponse?["error"]["message"].AsString ?? "No response";
                string stderr;
                lock (_lock) stderr = _stderrBuilder.ToString();
                LastError = $"Initialize failed: {errMsg}";
                AgentLogger.Error(LogTag.MCP, $"[{ServerName}] {LastError}");
                if (!string.IsNullOrEmpty(stderr))
                    AgentLogger.Error(LogTag.MCP, $"[{ServerName}] STDERR accumulated:\n{stderr}");
                Dispose();
                yield break;
            }

            // Send initialized notification (no id — it's a notification)
            SendNotification("notifications/initialized");

            // Save server capabilities
            ServerCapabilities = initResponse["result"]["capabilities"];
            AgentLogger.Info(LogTag.MCP, $"[{ServerName}] Initialized (protocol: {initResponse["result"]["protocolVersion"].AsString})");

            // --- Discover tools ---
            int listId = NextId();
            AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Sending tools/list request (id={listId})...");
            SendRequest("tools/list", JNode.Obj(), listId);

            JNode listResponse = null;
            yield return WaitForResponse(listId, r => listResponse = r);

            if (listResponse != null && listResponse["result"].Has("tools"))
            {
                var toolsArr = listResponse["result"]["tools"].AsArray;
                if (toolsArr != null)
                {
                    foreach (var t in toolsArr)
                    {
                        var def = new MCPToolDef
                        {
                            Name = t["name"].AsString ?? "",
                            Description = t["description"].AsString ?? "",
                            Params = ParseInputSchema(t["inputSchema"])
                        };
                        Tools.Add(def);
                    }
                }
            }
            else
            {
                AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] tools/list returned no tools. Response: {listResponse?.ToJson() ?? "null"}");
            }

            // --- Discover resources (if supported) ---
            if (ServerCapabilities != null && !ServerCapabilities["resources"].IsNull)
            {
                int resId = NextId();
                SendRequest("resources/list", JNode.Obj(), resId);
                JNode resResponse = null;
                yield return WaitForResponse(resId, r => resResponse = r, timeoutSeconds: 10);

                if (resResponse != null && resResponse["result"].Has("resources"))
                {
                    var resArr = resResponse["result"]["resources"].AsArray;
                    if (resArr != null)
                    {
                        foreach (var r in resArr)
                        {
                            Resources.Add(new MCPResource
                            {
                                Uri = r["uri"].AsString ?? "",
                                Name = r["name"].AsString ?? "",
                                Description = r["description"].AsString ?? "",
                                MimeType = r["mimeType"].AsString ?? "",
                            });
                        }
                    }
                }
                AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Resources: {Resources.Count}");
            }

            // --- Discover prompts (if supported) ---
            if (ServerCapabilities != null && !ServerCapabilities["prompts"].IsNull)
            {
                int promptId = NextId();
                SendRequest("prompts/list", JNode.Obj(), promptId);
                JNode promptResponse = null;
                yield return WaitForResponse(promptId, r => promptResponse = r, timeoutSeconds: 10);

                if (promptResponse != null && promptResponse["result"].Has("prompts"))
                {
                    var promptArr = promptResponse["result"]["prompts"].AsArray;
                    if (promptArr != null)
                    {
                        foreach (var p in promptArr)
                        {
                            var promptDef = new MCPPrompt
                            {
                                Name = p["name"].AsString ?? "",
                                Description = p["description"].AsString ?? "",
                                Arguments = new List<MCPPromptArg>(),
                            };
                            var promptArgs = p["arguments"].AsArray;
                            if (promptArgs != null)
                            {
                                foreach (var pa in promptArgs)
                                {
                                    var reqNode = pa["required"];
                                    promptDef.Arguments.Add(new MCPPromptArg
                                    {
                                        Name = pa["name"].AsString ?? "",
                                        Description = pa["description"].AsString ?? "",
                                        Required = reqNode.Type == JNode.JType.Bool && reqNode.AsBool,
                                    });
                                }
                            }
                            Prompts.Add(promptDef);
                        }
                    }
                }
                AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Prompts: {Prompts.Count}");
            }

            IsConnected = true;
            LastError = null;
            AgentLogger.Info(LogTag.MCP, $"[{ServerName}] Connected. {Tools.Count} tools, {Resources.Count} resources, {Prompts.Count} prompts.");
        }

        /// <summary>Call an MCP tool and return the result text.</summary>
        public IEnumerator CallTool(string toolName, JNode arguments,
            Action<string> onResult, Action<string> onError)
        {
            if (!IsConnected || _process == null || _process.HasExited)
            {
                IsConnected = false;
                LastError = "Server process is not running";
                onError?.Invoke($"MCP server '{ServerName}' is not connected.");
                yield break;
            }

            int id = NextId();
            var callParams = JNode.Obj(
                ("name", JNode.Str(toolName)),
                ("arguments", arguments ?? JNode.Obj())
            );
            SendRequest("tools/call", callParams, id);

            JNode response = null;
            yield return WaitForResponse(id, r => response = r, timeoutSeconds: 120);

            if (response == null)
            {
                onError?.Invoke($"MCP tool '{toolName}' timed out.");
                yield break;
            }

            if (response["error"].Type != JNode.JType.Null)
            {
                onError?.Invoke($"MCP tool '{toolName}' error: {response["error"]["message"].AsString}");
                yield break;
            }

            // Extract text content from result
            var content = response["result"]["content"].AsArray;
            if (content != null && content.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var item in content)
                {
                    if (item["type"].AsString == "text")
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(item["text"].AsString);
                    }
                }
                onResult?.Invoke(sb.ToString());
            }
            else
            {
                onResult?.Invoke("(empty result)");
            }
        }

        /// <summary>Read a resource by URI via resources/read.</summary>
        public IEnumerator ReadResource(string uri,
            Action<string> onResult, Action<string> onError)
        {
            if (!IsConnected || _process == null || _process.HasExited)
            {
                onError?.Invoke($"MCP server '{ServerName}' is not connected.");
                yield break;
            }

            int id = NextId();
            var reqParams = JNode.Obj(("uri", JNode.Str(uri)));
            SendRequest("resources/read", reqParams, id);

            JNode response = null;
            yield return WaitForResponse(id, r => response = r, timeoutSeconds: 30);

            if (response == null)
            {
                onError?.Invoke($"resources/read timed out for '{uri}'.");
                yield break;
            }
            if (response["error"].Type != JNode.JType.Null)
            {
                onError?.Invoke($"resources/read error: {response["error"]["message"].AsString}");
                yield break;
            }

            var contents = response["result"]["contents"].AsArray;
            if (contents == null || contents.Count == 0)
            {
                onResult?.Invoke("(empty resource)");
                yield break;
            }

            var sb = new StringBuilder();
            foreach (var item in contents)
            {
                string text = item["text"].AsString;
                if (text != null)
                {
                    if (sb.Length > 0) sb.AppendLine("---");
                    sb.Append(text);
                }
                else if (item["blob"].AsString != null)
                {
                    string mime = item["mimeType"].AsString ?? "unknown";
                    sb.AppendLine($"[Binary content: {mime}, {item["blob"].AsString.Length} chars base64]");
                }
            }

            // 100KB 制限
            const int MaxLen = 100 * 1024;
            string result = sb.ToString();
            if (result.Length > MaxLen)
                result = result.Substring(0, MaxLen) + "\n[... truncated at 100KB]";

            onResult?.Invoke(result);
        }

        /// <summary>Get a prompt by name via prompts/get.</summary>
        public IEnumerator GetPrompt(string name, JNode arguments,
            Action<string> onResult, Action<string> onError)
        {
            if (!IsConnected || _process == null || _process.HasExited)
            {
                onError?.Invoke($"MCP server '{ServerName}' is not connected.");
                yield break;
            }

            int id = NextId();
            var reqParams = JNode.Obj(
                ("name", JNode.Str(name)),
                ("arguments", arguments ?? JNode.Obj())
            );
            SendRequest("prompts/get", reqParams, id);

            JNode response = null;
            yield return WaitForResponse(id, r => response = r, timeoutSeconds: 30);

            if (response == null)
            {
                onError?.Invoke($"prompts/get timed out for '{name}'.");
                yield break;
            }
            if (response["error"].Type != JNode.JType.Null)
            {
                onError?.Invoke($"prompts/get error: {response["error"]["message"].AsString}");
                yield break;
            }

            var sb = new StringBuilder();
            string desc = response["result"]["description"].AsString;
            if (!string.IsNullOrEmpty(desc))
                sb.AppendLine($"Description: {desc}");

            var messages = response["result"]["messages"].AsArray;
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    string role = msg["role"].AsString ?? "unknown";
                    string text = msg["content"]["text"].AsString ?? "";
                    sb.AppendLine($"[{role}]: {text}");
                }
            }

            onResult?.Invoke(sb.Length > 0 ? sb.ToString() : "(empty prompt)");
        }

        public void Dispose()
        {
            IsConnected = false;
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.StandardInput.Close();
                            if (!_process.WaitForExit(3000))
                                _process.Kill();
                        }
                        catch { /* shutdown error — ignore */ }
                    }
                }
                finally
                {
                    try { _process.Dispose(); }
                    catch { /* ignore */ }
                    _process = null;
                }
            }
        }

        // ─── JSON-RPC ───

        int NextId() => _nextId++;

        void SendRequest(string method, JNode parameters, int id)
        {
            var msg = JNode.Obj(
                ("jsonrpc", JNode.Str("2.0")),
                ("method", JNode.Str(method)),
                ("params", parameters),
                ("id", JNode.Num(id))
            );
            SendLine(msg.ToJson());
        }

        void SendNotification(string method)
        {
            var msg = JNode.Obj(
                ("jsonrpc", JNode.Str("2.0")),
                ("method", JNode.Str(method))
            );
            SendLine(msg.ToJson());
        }

        void SendLine(string json)
        {
            if (_process == null || _process.HasExited)
            {
                AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] SendLine: process is null or exited, cannot send.");
                return;
            }
            try
            {
                // 接続完了前のみログ出力
                if (!IsConnected)
                {
                    var preview = json.Length > 200 ? json.Substring(0, 200) + "..." : json;
                    AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] STDIN> {preview}");
                }
                // Write on background thread to avoid blocking
                var proc = _process;
                var serverName = ServerName;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        proc.StandardInput.WriteLine(json);
                        proc.StandardInput.Flush();
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Error(LogTag.MCP, $"[{serverName}] SendLine write error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                AgentLogger.Error(LogTag.MCP, $"[{ServerName}] SendLine outer error: {ex.Message}");
            }
        }

        /// <summary>Poll for a JSON-RPC response with matching id.</summary>
        IEnumerator WaitForResponse(int id, Action<JNode> callback, int timeoutSeconds = TimeoutSeconds)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                // Drain queue and look for matching response
                List<string> lines = null;
                lock (_lock)
                {
                    if (_lineQueue.Count > 0)
                    {
                        lines = new List<string>(_lineQueue);
                        _lineQueue.Clear();
                    }
                }

                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var node = JNode.Parse(line);
                        if (node == null || node.Type == JNode.JType.Null)
                        {
                            AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] Failed to parse line: {line.Substring(0, Math.Min(200, line.Length))}");
                            continue;
                        }
                        if (node["id"].AsInt == id)
                        {
                            callback?.Invoke(node);
                            yield break;
                        }
                        // Non-matching message: server notification or other response
                        string method = node["method"].AsString;
                        if (!string.IsNullOrEmpty(method))
                        {
                            // Server-initiated notification — queue for later processing (thread-safe)
                            lock (_lock)
                            {
                                _pendingNotifications.Enqueue(new MCPNotification
                                {
                                    Method = method,
                                    Params = node["params"],
                                });
                            }
                            AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Notification queued: {method}");
                        }
                        else
                        {
                            AgentLogger.Debug(LogTag.MCP, $"[{ServerName}] Non-matching response (waiting for id={id}): id={node["id"].AsInt}");
                        }
                    }
                }

                // Check if process died
                bool done;
                lock (_lock) done = _stdoutDone;
                if (done || (_process != null && _process.HasExited))
                {
                    string stderr;
                    lock (_lock) stderr = _stderrBuilder.ToString();
                    int exitCode = -1;
                    try { if (_process != null && _process.HasExited) exitCode = _process.ExitCode; } catch { }
                    AgentLogger.Error(LogTag.MCP, $"[{ServerName}] Process died while waiting for id={id}. stdoutDone={done}, exitCode={exitCode}");
                    if (!string.IsNullOrEmpty(stderr))
                        AgentLogger.Error(LogTag.MCP, $"[{ServerName}] STDERR:\n{stderr}");
                    callback?.Invoke(null);
                    yield break;
                }

                yield return null;
            }

            string stderrTimeout;
            lock (_lock) stderrTimeout = _stderrBuilder.ToString();
            AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] Response timeout for id={id} after {timeoutSeconds}s");
            if (!string.IsNullOrEmpty(stderrTimeout))
                AgentLogger.Warning(LogTag.MCP, $"[{ServerName}] STDERR at timeout:\n{stderrTimeout}");
            callback?.Invoke(null);
        }

        // ─── Schema parsing ───

        static List<MCPToolParam> ParseInputSchema(JNode schema)
        {
            var result = new List<MCPToolParam>();
            if (schema.IsNull || !schema.Has("properties")) return result;

            var requiredSet = new HashSet<string>();
            var reqArr = schema["required"].AsArray;
            if (reqArr != null)
                foreach (var r in reqArr)
                    if (!string.IsNullOrEmpty(r.AsString))
                        requiredSet.Add(r.AsString);

            var props = schema["properties"].AsObject;
            if (props == null) return result;

            foreach (var kv in props)
            {
                result.Add(new MCPToolParam
                {
                    Name = kv.Key,
                    Type = kv.Value["type"].AsString ?? "string",
                    Description = kv.Value["description"].AsString ?? "",
                    Required = requiredSet.Contains(kv.Key)
                });
            }
            return result;
        }

        // ─── Process helpers ───

        static System.Diagnostics.ProcessStartInfo BuildStartInfo(string command, string arguments)
        {
            string fileName, cmdArgs;
            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

            if (isWindows)
            {
                fileName = "cmd.exe";
                // chcp 65001 で UTF-8 コードページに切り替え（日本語環境での文字化け防止）
                string innerCmd = command + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);
                cmdArgs = "/c chcp 65001 >nul && " + innerCmd;
            }
            else
            {
                fileName = command;
                cmdArgs = arguments;
            }

            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = cmdArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (isWindows)
            {
                // Add npm global bin, Python/pipx, and uv/uvx to PATH
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var extraPaths = new[]
                {
                    System.IO.Path.Combine(appData, "npm"),
                    System.IO.Path.Combine(localAppData, "Programs", "Python", "Python311", "Scripts"),
                    System.IO.Path.Combine(localAppData, "Programs", "Python", "Python312", "Scripts"),
                    System.IO.Path.Combine(localAppData, "Programs", "Python", "Python313", "Scripts"),
                    System.IO.Path.Combine(appData, "Python", "Scripts"),
                    // uv/uvx common install locations
                    System.IO.Path.Combine(userProfile, ".local", "bin"),
                    System.IO.Path.Combine(localAppData, "uv", "bin"),
                    System.IO.Path.Combine(userProfile, ".cargo", "bin"),
                    // Git for Windows
                    System.IO.Path.Combine(programFiles, "Git", "cmd"),
                };
                string currentPath = info.EnvironmentVariables.ContainsKey("PATH")
                    ? info.EnvironmentVariables["PATH"] : "";
                foreach (var p in extraPaths)
                {
                    if (currentPath.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0)
                        currentPath = p + ";" + currentPath;
                }
                info.EnvironmentVariables["PATH"] = currentPath;
            }

            return info;
        }
    }

    // ─── Data structures ───

    internal struct MCPToolDef
    {
        public string Name;
        public string Description;
        public List<MCPToolParam> Params;
    }

    internal struct MCPToolParam
    {
        public string Name;
        public string Type;
        public string Description;
        public bool Required;
    }

    internal struct MCPResource
    {
        public string Uri;
        public string Name;
        public string Description;
        public string MimeType;
    }

    internal struct MCPPrompt
    {
        public string Name;
        public string Description;
        public List<MCPPromptArg> Arguments;
    }

    internal struct MCPPromptArg
    {
        public string Name;
        public string Description;
        public bool Required;
    }

    internal struct MCPNotification
    {
        public string Method;
        public JNode Params;
    }
}
