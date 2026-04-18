using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Manages MCP server instances, tool discovery, and tool execution routing.
    /// </summary>
    internal static class MCPManager
    {
        static readonly List<MCPClient> _clients = new List<MCPClient>();
        static bool _initialized;

        public static bool IsInitialized => _initialized;

        public static bool HasEnabledServers
        {
            get
            {
                var configs = GetServerConfigs();
                return configs != null && configs.Any(c => c.enabled);
            }
        }

        /// <summary>Start all enabled MCP servers and discover their tools.</summary>
        public static IEnumerator Initialize()
        {
            if (_initialized) yield break;

            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            Shutdown();

            var configs = GetServerConfigs();
            AgentLogger.Info(LogTag.MCP, $"Initialize: {configs?.Length ?? 0} configs loaded");
            if (configs == null || configs.Length == 0)
            {
                AgentLogger.Debug(LogTag.MCP, "No configs found, skipping initialization.");
                _initialized = true;
                yield break;
            }

            foreach (var cfg in configs)
            {
                AgentLogger.Debug(LogTag.MCP, $"Config: name={cfg.name}, cmd={cfg.command}, enabled={cfg.enabled}, args=[{string.Join(",", cfg.args ?? Array.Empty<string>())}]");
                if (!cfg.enabled) continue;
                if (string.IsNullOrEmpty(cfg.command)) continue;

                var env = new Dictionary<string, string>();
                if (cfg.envKeys != null)
                {
                    for (int i = 0; i < cfg.envKeys.Length && i < cfg.envValues.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(cfg.envKeys[i]))
                            env[cfg.envKeys[i]] = cfg.envValues[i] ?? "";
                    }
                }

                var client = new MCPClient(cfg.name, cfg.command, cfg.args, env);
                _clients.Add(client);

                var perServerSw = System.Diagnostics.Stopwatch.StartNew();
                yield return client.Connect();
                perServerSw.Stop();

                if (!client.IsConnected)
                    AgentLogger.Warning(LogTag.MCP, $"Failed to connect to MCP server: {cfg.name} (elapsed={perServerSw.ElapsedMilliseconds}ms)");
                else
                    AgentLogger.Debug(LogTag.MCP, $"Connected to MCP server: {cfg.name} (elapsed={perServerSw.ElapsedMilliseconds}ms)");
            }

            _initialized = true;
            totalSw.Stop();
            int totalTools = _clients.Sum(c => c.Tools.Count);
            AgentLogger.Info(LogTag.MCP, $"Initialized. {_clients.Count(c => c.IsConnected)} servers, {totalTools} MCP tools. (total elapsed={totalSw.ElapsedMilliseconds}ms)");
        }

        /// <summary>Reinitialize MCP (e.g., after settings change).</summary>
        public static IEnumerator Reinitialize()
        {
            _initialized = false;
            Shutdown();
            yield return Initialize();
        }

        /// <summary>Shutdown all MCP servers.</summary>
        public static void Shutdown()
        {
            if (_clients.Count > 0)
                AgentLogger.Info(LogTag.MCP, $"Shutdown: disposing {_clients.Count} MCP client(s)");
            foreach (var c in _clients)
                c.Dispose("MCPManager.Shutdown");
            _clients.Clear();
            _initialized = false;
        }

        // ─── Health check & auto-reconnect ───

        /// <summary>プロセス死亡を検知し、自動再接続を試みる。通知も処理する。</summary>
        public static IEnumerator EnsureConnected()
        {
            if (!_initialized) yield break;

            // サーバー通知を処理 (tools/list_changed 等)
            ProcessNotifications();

            var deadClients = new List<int>();
            for (int i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                if (client.IsConnected && !client.IsAlive)
                {
                    AgentLogger.Warning(LogTag.MCP, $"Server '{client.ServerName}' process died. Attempting reconnect...");
                    client.Dispose("process-died-auto-reconnect");
                    deadClients.Add(i);
                }
            }

            if (deadClients.Count == 0) yield break;

            // 再接続
            var configs = GetServerConfigs();
            foreach (int idx in deadClients)
            {
                if (idx >= _clients.Count) continue;
                var oldClient = _clients[idx];
                var cfg = configs.FirstOrDefault(c => c.name == oldClient.ServerName);
                if (string.IsNullOrEmpty(cfg.command) || !cfg.enabled) continue;

                var env = new Dictionary<string, string>();
                if (cfg.envKeys != null)
                    for (int i = 0; i < cfg.envKeys.Length && i < cfg.envValues.Length; i++)
                        if (!string.IsNullOrEmpty(cfg.envKeys[i]))
                            env[cfg.envKeys[i]] = cfg.envValues[i] ?? "";

                var newClient = new MCPClient(cfg.name, cfg.command, cfg.args, env);
                var reconnectSw = System.Diagnostics.Stopwatch.StartNew();
                yield return newClient.Connect();
                reconnectSw.Stop();

                _clients[idx] = newClient;
                if (newClient.IsConnected)
                    AgentLogger.Info(LogTag.MCP, $"Reconnected '{cfg.name}' ({newClient.Tools.Count} tools, elapsed={reconnectSw.ElapsedMilliseconds}ms)");
                else
                    AgentLogger.Warning(LogTag.MCP, $"Reconnect failed for '{cfg.name}' (elapsed={reconnectSw.ElapsedMilliseconds}ms): {newClient.LastError}");
            }
        }

        /// <summary>全サーバーの接続状態を取得する。</summary>
        public static List<MCPServerStatus> GetServerStatuses()
        {
            var result = new List<MCPServerStatus>();
            foreach (var client in _clients)
            {
                result.Add(new MCPServerStatus
                {
                    Name = client.ServerName,
                    IsConnected = client.IsConnected,
                    IsAlive = client.IsAlive,
                    ToolCount = client.Tools.Count,
                    LastError = client.LastError,
                });
            }
            return result;
        }

        // ─── Tool / Resource / Prompt queries ───

        /// <summary>Get all tools from all connected MCP servers (for system prompt).</summary>
        public static List<(string serverName, MCPToolDef tool)> GetAllTools()
        {
            var result = new List<(string, MCPToolDef)>();
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var tool in client.Tools)
                    result.Add((client.ServerName, tool));
            }
            return result;
        }

        /// <summary>Get all resources from all connected MCP servers.</summary>
        public static List<(string serverName, MCPResource resource)> GetAllResources()
        {
            var result = new List<(string, MCPResource)>();
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var res in client.Resources)
                    result.Add((client.ServerName, res));
            }
            return result;
        }

        /// <summary>Get all prompts from all connected MCP servers.</summary>
        public static List<(string serverName, MCPPrompt prompt)> GetAllPrompts()
        {
            var result = new List<(string, MCPPrompt)>();
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var p in client.Prompts)
                    result.Add((client.ServerName, p));
            }
            return result;
        }

        /// <summary>Find a resource by URI across all connected servers.</summary>
        public static (MCPClient client, MCPResource resource)? FindResource(string uri)
        {
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var res in client.Resources)
                {
                    if (string.Equals(res.Uri, uri, StringComparison.Ordinal))
                        return (client, res);
                }
            }
            return null;
        }

        /// <summary>Find a prompt by name across all connected servers.</summary>
        public static (MCPClient client, MCPPrompt prompt)? FindPrompt(string name)
        {
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var p in client.Prompts)
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        return (client, p);
                }
            }
            return null;
        }

        /// <summary>全サーバーの STDERR ログを結合して返す。</summary>
        public static string GetAllLogs()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var client in _clients)
            {
                string log = client.GetStderrLog();
                if (!string.IsNullOrEmpty(log))
                {
                    sb.AppendLine($"═══ {client.ServerName} ═══");
                    sb.Append(log);
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>全サーバーの STDERR ログをクリアする。</summary>
        public static void ClearAllLogs()
        {
            foreach (var client in _clients)
                client.ClearStderrLog();
        }

        /// <summary>Process pending notifications from all servers (tools/list changed etc.).</summary>
        public static void ProcessNotifications()
        {
            foreach (var client in _clients)
            {
                var notifications = client.DrainNotifications();
                if (notifications == null) continue;

                foreach (var notif in notifications)
                {
                    AgentLogger.Info(LogTag.MCP, $"Notification from {client.ServerName}: {notif.Method}");

                    if (notif.Method == "notifications/tools/list_changed")
                    {
                        AgentLogger.Info(LogTag.MCP, $"Tools changed on {client.ServerName}, will re-discover on next query.");
                        client.Tools.Clear();
                        _initialized = false;
                    }
                }
            }
        }

        /// <summary>Check if a tool name matches any MCP tool.</summary>
        public static bool HasTool(string name)
        {
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var tool in client.Tools)
                {
                    if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Get the MCPClient and tool definition for a given tool name.</summary>
        public static (MCPClient client, MCPToolDef tool)? GetTool(string name)
        {
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var tool in client.Tools)
                {
                    if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
                        return (client, tool);
                }
            }
            return null;
        }

        /// <summary>Get all MCP tool names (for validation sets).</summary>
        public static HashSet<string> GetToolNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var client in _clients)
            {
                if (!client.IsConnected) continue;
                foreach (var tool in client.Tools)
                    names.Add(tool.Name);
            }
            return names;
        }

        /// <summary>Build JSON arguments from positional args and MCP tool schema.</summary>
        public static JNode BuildArguments(MCPToolDef tool, string[] rawArgs)
        {
            var pairs = new List<(string, JNode)>();

            for (int i = 0; i < rawArgs.Length && i < tool.Params.Count; i++)
            {
                string raw = rawArgs[i].Trim().Trim('\'', '"');

                // Check for named arg (key=value)
                int eqIdx = raw.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = raw.Substring(0, eqIdx).Trim();
                    string val = raw.Substring(eqIdx + 1).Trim().Trim('\'', '"');
                    // Verify it matches a param name
                    var matchParam = tool.Params.FirstOrDefault(p =>
                        string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(matchParam.Name))
                    {
                        pairs.Add((matchParam.Name, ConvertArg(val, matchParam.Type)));
                        continue;
                    }
                }

                // Positional arg — map to parameter by index
                var param = tool.Params[i];
                pairs.Add((param.Name, ConvertArg(raw, param.Type)));
            }

            return JNode.Obj(pairs.ToArray());
        }

        static JNode ConvertArg(string value, string type)
        {
            switch (type)
            {
                case "number":
                case "integer":
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return JNode.Num(d);
                    return JNode.Str(value);
                case "boolean":
                    if (bool.TryParse(value, out bool b))
                        return JNode.Bool(b);
                    return JNode.Str(value);
                case "array":
                case "object":
                {
                    string trimmed = value.Trim();
                    var parsed = JNode.Parse(trimmed);
                    if (parsed != null && parsed.Type != JNode.JType.Null)
                        return parsed;
                    return JNode.Str(value);
                }
                case "string":
                    return JNode.Str(value);
                default:
                {
                    // 型が未指定の場合のみ自動検出: JSON array/object 形式の値をパース
                    string trimmed = value.Trim();
                    if ((trimmed.StartsWith("[") && trimmed.EndsWith("]")) ||
                        (trimmed.StartsWith("{") && trimmed.EndsWith("}")))
                    {
                        var parsed = JNode.Parse(trimmed);
                        if (parsed != null && (parsed.Type == JNode.JType.Array || parsed.Type == JNode.JType.Object))
                            return parsed;
                    }
                    return JNode.Str(value);
                }
            }
        }

        // ─── Configuration ───

        const string ConfigKey = "UnityAgent_MCPServers";

        public static MCPServerConfig[] GetServerConfigs()
        {
            string json = SettingsStore.GetString(ConfigKey, "");
            if (string.IsNullOrEmpty(json)) return Array.Empty<MCPServerConfig>();

            try
            {
                // Parse JSON array manually
                var root = JNode.Parse(json);
                if (root.Type != JNode.JType.Array) return Array.Empty<MCPServerConfig>();

                var configs = new List<MCPServerConfig>();
                foreach (var item in root.AsArray)
                {
                    var cfg = new MCPServerConfig
                    {
                        name = item["name"].AsString ?? "",
                        command = item["command"].AsString ?? "",
                        enabled = item["enabled"].Type == JNode.JType.Bool ? item["enabled"].AsBool : true,
                    };

                    // Parse args array
                    var argsArr = item["args"].AsArray;
                    if (argsArr != null)
                        cfg.args = argsArr.Select(a => a.AsString ?? "").ToArray();
                    else
                        cfg.args = Array.Empty<string>();

                    // Parse env as parallel key/value arrays
                    var envObj = item["env"];
                    if (envObj.Type == JNode.JType.Object && envObj.AsObject != null)
                    {
                        var keys = new List<string>();
                        var vals = new List<string>();
                        foreach (var kv in envObj.AsObject)
                        {
                            keys.Add(kv.Key);
                            vals.Add(kv.Value.AsString ?? "");
                        }
                        cfg.envKeys = keys.ToArray();
                        cfg.envValues = vals.ToArray();
                    }
                    else
                    {
                        cfg.envKeys = Array.Empty<string>();
                        cfg.envValues = Array.Empty<string>();
                    }

                    configs.Add(cfg);
                }
                return configs.ToArray();
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.MCP, $"Failed to parse MCP config: {ex.Message}");
                return Array.Empty<MCPServerConfig>();
            }
        }

        public static void SetServerConfigs(MCPServerConfig[] configs)
        {
            // Build JSON array
            var items = new List<JNode>();
            foreach (var cfg in configs)
            {
                var envPairs = new List<(string, JNode)>();
                if (cfg.envKeys != null)
                {
                    for (int i = 0; i < cfg.envKeys.Length && i < cfg.envValues.Length; i++)
                        envPairs.Add((cfg.envKeys[i], JNode.Str(cfg.envValues[i] ?? "")));
                }

                items.Add(JNode.Obj(
                    ("name", JNode.Str(cfg.name ?? "")),
                    ("command", JNode.Str(cfg.command ?? "")),
                    ("args", JNode.Arr(
                        (cfg.args ?? Array.Empty<string>()).Select(a => JNode.Str(a)).ToArray())),
                    ("env", JNode.Obj(envPairs.ToArray())),
                    ("enabled", JNode.Bool(cfg.enabled))
                ));
            }

            string json = JNode.Arr(items.ToArray()).ToJson();
            SettingsStore.SetString(ConfigKey, json);
        }

        // ─── Presets ───

        public struct MCPPreset
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public Func<MCPServerConfig> Create;
        }

        public static MCPPreset[] GetPresets() => s_presets;

        static readonly MCPPreset[] s_presets = new[]
        {
            new MCPPreset
            {
                Id = "serena", DisplayName = "Serena",
                Description = "Semantic code analysis & editing (LSP-based)",
                Create = CreateSerenaPreset,
            },
            new MCPPreset
            {
                Id = "filesystem", DisplayName = "Filesystem",
                Description = "Read/write/search files on local disk",
                Create = CreateFilesystemPreset,
            },
            new MCPPreset
            {
                Id = "fetch", DisplayName = "Fetch",
                Description = "HTTP requests (GET/POST) and web content retrieval",
                Create = CreateFetchPreset,
            },
            new MCPPreset
            {
                Id = "memory", DisplayName = "Memory",
                Description = "Knowledge graph-based persistent memory",
                Create = CreateMemoryPreset,
            },
            new MCPPreset
            {
                Id = "github", DisplayName = "GitHub",
                Description = "GitHub API (repos, issues, PRs, search)",
                Create = CreateGitHubPreset,
            },
            new MCPPreset
            {
                Id = "brave-search", DisplayName = "Brave Search",
                Description = "Web search via Brave Search API",
                Create = CreateBraveSearchPreset,
            },
            new MCPPreset
            {
                Id = "sqlite", DisplayName = "SQLite",
                Description = "Read/write SQLite databases",
                Create = CreateSQLitePreset,
            },
            new MCPPreset
            {
                Id = "puppeteer", DisplayName = "Puppeteer",
                Description = "Browser automation (screenshot, navigate, click)",
                Create = CreatePuppeteerPreset,
            },
            new MCPPreset
            {
                Id = "everything", DisplayName = "Everything",
                Description = "Fast file search via Everything (Windows)",
                Create = CreateEverythingPreset,
            },
        };

        public static MCPServerConfig CreateSerenaPreset()
        {
            string projectPath = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath)
                ?.Replace('\\', '/') ?? "";
            return new MCPServerConfig
            {
                name = "serena",
                command = "uvx",
                args = new[] { "--from", "git+https://github.com/oraios/serena", "serena", "start-mcp-server" },
                envKeys = new[] { "SERENA_WORKSPACE" },
                envValues = new[] { projectPath },
                enabled = true,
            };
        }

        public static MCPServerConfig CreateFilesystemPreset()
        {
            string projectPath = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath)
                ?.Replace('\\', '/') ?? "";
            return new MCPServerConfig
            {
                name = "filesystem",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-filesystem", projectPath },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }

        public static MCPServerConfig CreateFetchPreset()
        {
            return new MCPServerConfig
            {
                name = "fetch",
                command = "uvx",
                args = new[] { "mcp-server-fetch" },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }

        public static MCPServerConfig CreateMemoryPreset()
        {
            return new MCPServerConfig
            {
                name = "memory",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-memory" },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }

        public static MCPServerConfig CreateGitHubPreset()
        {
            return new MCPServerConfig
            {
                name = "github",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-github" },
                envKeys = new[] { "GITHUB_PERSONAL_ACCESS_TOKEN" },
                envValues = new[] { "" },
                enabled = true,
            };
        }

        public static MCPServerConfig CreateBraveSearchPreset()
        {
            return new MCPServerConfig
            {
                name = "brave-search",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-brave-search" },
                envKeys = new[] { "BRAVE_API_KEY" },
                envValues = new[] { "" },
                enabled = true,
            };
        }

        public static MCPServerConfig CreateSQLitePreset()
        {
            return new MCPServerConfig
            {
                name = "sqlite",
                command = "uvx",
                args = new[] { "mcp-server-sqlite", "--db-path", "database.db" },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }

        public static MCPServerConfig CreatePuppeteerPreset()
        {
            return new MCPServerConfig
            {
                name = "puppeteer",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-puppeteer" },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }

        public static MCPServerConfig CreateEverythingPreset()
        {
            return new MCPServerConfig
            {
                name = "everything",
                command = "npx",
                args = new[] { "-y", "@modelcontextprotocol/server-everything" },
                envKeys = Array.Empty<string>(),
                envValues = Array.Empty<string>(),
                enabled = true,
            };
        }
    }

    internal struct MCPServerStatus
    {
        public string Name;
        public bool IsConnected;
        public bool IsAlive;
        public int ToolCount;
        public string LastError;
    }

    [Serializable]
    internal struct MCPServerConfig
    {
        public string name;
        public string command;
        public string[] args;
        public string[] envKeys;
        public string[] envValues;
        public bool enabled;
    }
}
