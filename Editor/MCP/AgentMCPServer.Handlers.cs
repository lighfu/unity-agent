using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// AgentMCPServer 用の JSON-RPC ハンドラ群。スレッドセーフな読み取り系のみ
    /// (`initialize`, `tools/list`, `ping`) 。ツール実行は <see cref="Invoker"/>。
    /// </summary>
    internal static class Handlers
    {
        // hindsight 等の動作実績ある MCP サーバーが使っている安定 version に固定する。
        const string ProtocolVersion = "2024-11-05";
        const string ServerName = "UnityAgent";

        public static JNode HandleInitialize(JNode paramsNode)
        {
            string version = GetPackageVersion();
            return JNode.Obj(
                ("protocolVersion", JNode.Str(ProtocolVersion)),
                ("capabilities", JNode.Obj(
                    ("experimental", JNode.Obj()),
                    ("prompts", JNode.Obj(("listChanged", JNode.Bool(false)))),
                    ("resources", JNode.Obj(
                        ("subscribe", JNode.Bool(false)),
                        ("listChanged", JNode.Bool(false)))),
                    ("tools", JNode.Obj(("listChanged", JNode.Bool(true))))
                )),
                ("serverInfo", JNode.Obj(
                    ("name", JNode.Str(ServerName)),
                    ("version", JNode.Str(version))
                ))
            );
        }

        /// <summary>
        /// MCP 経由で公開するのは 3 つのメタツールだけ。
        /// 実際の ~456 Unity ツールは <see cref="Invoker"/> 内で名前ディスパッチする。
        /// これにより Claude Code の Zod validator の 60 秒タイムアウト問題を回避する。
        /// </summary>
        public static JNode HandleToolsList(JNode _)
        {
            var tools = new List<JNode>
            {
                BuildSearchToolSchema(),
                BuildDescribeToolSchema(),
                BuildExecuteToolSchema(),
            };
            return JNode.Obj(("tools", JNode.Arr(tools.ToArray())));
        }

        static JNode BuildSearchToolSchema() => JNode.Obj(
            ("name", JNode.Str("SearchUnityTool")),
            ("description", JNode.Str(
                "Search for Unity Editor tools by keyword. Returns matching tool names with short descriptions. " +
                "Use this first to discover which tool to call, then use DescribeUnityTool for parameter details, " +
                "then ExecuteUnityTool to run it.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("query", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("description", JNode.Str("Keyword to search in tool names, descriptions, and categories. Example: 'gameobject create', 'animator', 'blendshape'.")))),
                    ("limit", JNode.Obj(
                        ("type", JNode.Str("integer")),
                        ("description", JNode.Str("Maximum number of results to return. Default 20.")),
                        ("default", JNode.Num(20))))
                )),
                ("required", JNode.Arr(JNode.Str("query")))
            ))
        );

        static JNode BuildDescribeToolSchema() => JNode.Obj(
            ("name", JNode.Str("DescribeUnityTool")),
            ("description", JNode.Str(
                "Get full parameter schema and documentation for a specific Unity tool. " +
                "Call this before ExecuteUnityTool to learn what arguments to pass.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("name", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("description", JNode.Str("Exact tool name (case-sensitive). Obtain via SearchUnityTool.")))
                    ))),
                ("required", JNode.Arr(JNode.Str("name")))
            ))
        );

        static JNode BuildExecuteToolSchema() => JNode.Obj(
            ("name", JNode.Str("ExecuteUnityTool")),
            ("description", JNode.Str(
                "Execute a Unity Editor tool by name. Arguments are passed as a JSON object keyed by parameter name. " +
                "Use DescribeUnityTool to find the exact argument names and types.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("name", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("description", JNode.Str("Exact tool name. Obtain via SearchUnityTool.")))),
                    ("arguments", JNode.Obj(
                        ("type", JNode.Str("object")),
                        ("description", JNode.Str("Parameters as a JSON object. Keys must match the tool's parameter names. Example: {\"gameObjectName\":\"MyObject\"}.")),
                        ("additionalProperties", JNode.Bool(true))))
                )),
                ("required", JNode.Arr(JNode.Str("name"), JNode.Str("arguments")))
            ))
        );

        // ─── Meta-tool implementations (called from Invoker) ───

        /// <summary>SearchUnityTool の実装。マッチしたツール名と概要を改行区切りで返す。</summary>
        public static string ImplSearchTool(string query, int limit)
        {
            if (string.IsNullOrEmpty(query))
                return "Error: 'query' is required.";

            var exposeRisk = AgentSettings.MCPServerExposeRisk;
            var q = query.Trim();
            var matches = new List<(int score, string name, string desc, string category, ToolRisk risk)>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var info in ToolRegistry.GetAllTools())
            {
                var method = info.method;
                if (method == null) continue;
                if (!AgentSettings.IsToolEnabled(method.Name, info.isExternal)) continue;
                if ((int)info.resolvedRisk > (int)exposeRisk) continue;
                if (!seen.Add(method.Name)) continue;

                string name = method.Name;
                string desc = info.attribute?.Description ?? "";
                string category = info.attribute?.Category ?? "";

                int score = MatchScore(name, desc, category, q);
                if (score > 0)
                    matches.Add((score, name, desc, category, info.resolvedRisk));
            }

            if (matches.Count == 0)
                return $"No tools found matching '{query}'.";

            matches.Sort((a, b) => b.score.CompareTo(a.score));
            if (limit <= 0) limit = 20;
            int take = System.Math.Min(limit, matches.Count);

            var sb = new System.Text.StringBuilder();
            sb.Append($"Found {matches.Count} tools (showing {take}):\n\n");
            for (int i = 0; i < take; i++)
            {
                var m = matches[i];
                sb.Append($"• {m.name}");
                if (!string.IsNullOrEmpty(m.category)) sb.Append($" [{m.category}]");
                sb.Append($" (Risk: {m.risk})\n");
                if (!string.IsNullOrEmpty(m.desc))
                {
                    string shortDesc = m.desc.Length > 180 ? m.desc.Substring(0, 180) + "..." : m.desc;
                    sb.Append("    ").Append(shortDesc.Replace("\r", " ").Replace("\n", " ")).Append('\n');
                }
            }
            if (matches.Count > take)
                sb.Append($"\n... and {matches.Count - take} more. Use a more specific query or increase limit.");
            return sb.ToString();
        }

        static int MatchScore(string name, string desc, string category, string query)
        {
            var tokens = query.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return 0;
            int score = 0;
            foreach (var tok in tokens)
            {
                if (string.IsNullOrWhiteSpace(tok)) continue;
                if (name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                if (desc.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
                if (category.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
            }
            return score;
        }

        /// <summary>DescribeUnityTool の実装。特定ツールの詳細スキーマを人間可読形式で返す。</summary>
        public static string ImplDescribeTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Error: 'name' is required.";

            var exposeRisk = AgentSettings.MCPServerExposeRisk;
            foreach (var info in ToolRegistry.GetAllTools())
            {
                var method = info.method;
                if (method == null) continue;
                if (!string.Equals(method.Name, name, System.StringComparison.Ordinal)) continue;
                if (!AgentSettings.IsToolEnabled(method.Name, info.isExternal))
                    return $"Error: Tool '{name}' is disabled.";
                if ((int)info.resolvedRisk > (int)exposeRisk)
                    return $"Error: Tool '{name}' exceeds current risk limit ({exposeRisk}).";

                var sb = new System.Text.StringBuilder();
                sb.Append($"# {method.Name}\n");
                if (!string.IsNullOrEmpty(info.attribute?.Category))
                    sb.Append($"**Category:** {info.attribute.Category}\n");
                sb.Append($"**Risk:** {info.resolvedRisk}\n");
                if (info.isExternal && !string.IsNullOrEmpty(info.assemblyName))
                    sb.Append($"**Assembly:** {info.assemblyName}\n");
                sb.Append('\n');
                if (!string.IsNullOrEmpty(info.attribute?.Description))
                {
                    sb.Append(info.attribute.Description.Replace("\r\n", "\n").Replace("\r", "\n"));
                    sb.Append("\n\n");
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    sb.Append("**Parameters:** (none)\n");
                }
                else
                {
                    sb.Append("**Parameters:**\n");
                    foreach (var p in parameters)
                    {
                        sb.Append($"- `{p.Name}` ({p.ParameterType.Name})");
                        if (p.HasDefaultValue)
                            sb.Append($" = {p.DefaultValue ?? "null"}");
                        else
                            sb.Append(" *(required)*");
                        sb.Append('\n');
                    }
                }
                sb.Append("\n**Usage:** ExecuteUnityTool(name=\"").Append(method.Name).Append("\", arguments={...})");
                return sb.ToString();
            }
            return $"Error: Tool '{name}' not found.";
        }

        static string GetPackageVersion()
        {
            try
            {
                // package.json は UnityEditor.PackageManager から取得する手もあるが、
                // 同期で軽量に参照するため Assembly.Version を返す。
                var asm = typeof(Handlers).Assembly;
                var v = asm.GetName().Version;
                return v != null ? v.ToString() : "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }
    }

    /// <summary>
    /// MethodInfo → MCP/JSON Schema 変換。
    /// </summary>
    internal static class Schema
    {
        public static JNode BuildToolDescriptor(MethodInfo method, ToolRegistry.ToolInfo info)
        {
            var attr = info.attribute;

            // description: 基本説明のみ (メタデータは省略して schema サイズを削減)
            string desc = (attr?.Description ?? method.Name)
                .Replace("\r\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");

            var parameters = method.GetParameters();
            var propertyPairs = new List<(string, JNode)>();
            var required = new List<JNode>();

            foreach (var p in parameters)
            {
                propertyPairs.Add((p.Name, BuildParameterSchema(p)));
                if (!p.HasDefaultValue)
                    required.Add(JNode.Str(p.Name));
            }

            var inputSchema = JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(propertyPairs.ToArray())),
                ("required", JNode.Arr(required.ToArray()))
            );

            return JNode.Obj(
                ("name", JNode.Str(method.Name)),
                ("description", JNode.Str(desc)),
                ("inputSchema", inputSchema)
            );
        }

        static JNode BuildParameterSchema(ParameterInfo p)
        {
            string jsonType;
            var t = p.ParameterType;

            if (t == typeof(string))
                jsonType = "string";
            else if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte))
                jsonType = "integer";
            else if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
                jsonType = "number";
            else if (t == typeof(bool))
                jsonType = "boolean";
            else
                jsonType = "string";

            var pairs = new List<(string, JNode)>
            {
                ("type", JNode.Str(jsonType)),
            };

            if (p.HasDefaultValue && p.DefaultValue != null)
            {
                switch (jsonType)
                {
                    case "integer":
                    case "number":
                        if (double.TryParse(
                                p.DefaultValue.ToString(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double dv))
                            pairs.Add(("default", JNode.Num(dv)));
                        break;
                    case "boolean":
                        if (p.DefaultValue is bool bv)
                            pairs.Add(("default", JNode.Bool(bv)));
                        break;
                    default:
                        pairs.Add(("default", JNode.Str(p.DefaultValue.ToString())));
                        break;
                }
            }

            return JNode.Obj(pairs.ToArray());
        }
    }
}
