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
        const string ServerName = "UnityAgent";

        public static JNode HandleInitialize(JNode paramsNode)
        {
            string version = GetPackageVersion();
            string protocolVersion = MCPHttpProtocol.NegotiateProtocolVersion(paramsNode);
            return JNode.Obj(
                ("protocolVersion", JNode.Str(protocolVersion)),
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
        /// MCP 経由で公開するのは 3 つのメタツールと GetUnityAgentInfo だけ。
        /// 実際の ~456 Unity ツールは <see cref="Invoker"/> 内で名前ディスパッチする。
        /// これにより Claude Code の Zod validator の 60 秒タイムアウト問題を回避する。
        ///
        /// GetUnityAgentInfo は他の 3 つと違い専用の実装を持たない。名前が [AgentTool] と一致して
        /// いるので <see cref="Invoker"/> の通常の名前ディスパッチがそのまま拾い、引数バインドと
        /// メインスレッド投入は既存経路が処理する。ここに要るのはスキーマだけ。
        /// </summary>
        public static JNode HandleToolsList(JNode _)
        {
            var tools = new List<JNode>
            {
                BuildSearchToolSchema(),
                BuildDescribeToolSchema(),
                BuildExecuteToolSchema(),
                BuildAgentInfoToolSchema(),
            };
            return JNode.Obj(("tools", JNode.Arr(tools.ToArray())));
        }

        static JNode BuildSearchToolSchema() => JNode.Obj(
            ("name", JNode.Str("SearchUnityTool")),
            ("description", JNode.Str(
                "Search for Unity Editor tools by keyword. Returns matching tool names with short descriptions. " +
                "Use this first to discover which tool to call, then use DescribeUnityTool for parameter details, " +
                "then ExecuteUnityTool to run it. Use offset to continue the matching tool list.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("query", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("description", JNode.Str("Keyword to search in tool names, descriptions, and categories. Example: 'gameobject create', 'animator', 'blendshape'.")))),
                    ("limit", JNode.Obj(
                        ("type", JNode.Str("integer")),
                        ("description", JNode.Str("Maximum number of results to return. Default 20.")),
                        ("minimum", JNode.Num(1)),
                        ("maximum", JNode.Num(200)),
                        ("default", JNode.Num(20)))),
                    ("offset", JNode.Obj(("type", JNode.Str("integer")), ("minimum", JNode.Num(0)), ("default", JNode.Num(0))))
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
                "Use DescribeUnityTool to find the exact argument names and types. Long results return a " +
                "text snapshot page; continue with ReadUnityToolResultPage(resultId, offset=page.nextOffset). " +
                "resultOffset/resultLimit/resultMaxChars apply to completed result text, outside arguments.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("name", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("description", JNode.Str("Exact tool name. Obtain via SearchUnityTool.")))),
                    ("arguments", JNode.Obj(
                        ("type", JNode.Str("object")),
                        ("description", JNode.Str("Parameters as a JSON object. Keys must match the tool's parameter names. Example: {\"gameObjectName\":\"MyObject\"}.")),
                        ("additionalProperties", JNode.Bool(true)))),
                    ("resultOffset", JNode.Obj(("type", JNode.Str("integer")), ("minimum", JNode.Num(0)), ("default", JNode.Num(0)))),
                    ("resultLimit", JNode.Obj(("type", JNode.Str("integer")), ("minimum", JNode.Num(1)), ("maximum", JNode.Num(200)), ("default", JNode.Num(50)))),
                    ("resultMaxChars", JNode.Obj(("type", JNode.Str("integer")), ("minimum", JNode.Num(1024)), ("maximum", JNode.Num(32768)), ("default", JNode.Num(8192))))
                )),
                ("required", JNode.Arr(JNode.Str("name"), JNode.Str("arguments")))
            ))
        );

        static JNode BuildAgentInfoToolSchema() => JNode.Obj(
            ("name", JNode.Str("GetUnityAgentInfo")),
            ("description", JNode.Str(
                "Report what this UnityAgent install is: UnityAgent version, Unity version and project, " +
                "how many tools are registered and how many are reachable over MCP, which optional VRChat / " +
                "avatar packages are installed (with versions), and how the MCP server is wired up. " +
                "Call this once at the start of a session — the tool surface is not constant between " +
                "installs because optional packages compile whole tool modules in or out, so 'that tool " +
                "does not exist' usually means the package is absent rather than the build being broken.")),
            ("inputSchema", JNode.Obj(
                ("type", JNode.Str("object")),
                ("properties", JNode.Obj(
                    ("detail", JNode.Obj(
                        ("type", JNode.Str("string")),
                        ("enum", JNode.Arr(JNode.Str("brief"), JNode.Str("full"))),
                        ("description", JNode.Str("'brief' (default) returns a few lines suitable for every session start. 'full' adds per-category and per-risk tool counts, every detected package, MCP endpoint / bridge state and project render settings — use it for bug reports.")),
                        ("default", JNode.Str("brief"))))
                )),
                ("required", JNode.Arr())
            ))
        );

        // ─── Meta-tool implementations (called from Invoker) ───

        /// <summary>SearchUnityTool の実装。マッチしたツール名と概要を改行区切りで返す。</summary>
        public static string ImplSearchTool(string query, int limit, int offset = 0)
        {
            if (offset < 0 || limit < 1 || limit > ToolResultPager.MaximumLimit)
                return "Error: SearchUnityTool offset must be non-negative and limit must be between 1 and 200.";
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

            matches.Sort((a, b) =>
            {
                int score = b.score.CompareTo(a.score);
                return score != 0 ? score : System.StringComparer.Ordinal.Compare(a.name, b.name);
            });
            int take = offset < matches.Count ? System.Math.Min(limit, matches.Count - offset) : 0;

            var sb = new System.Text.StringBuilder();
            sb.Append($"Found {matches.Count} tools (showing {take}):\n\n");
            for (int i = 0; i < take; i++)
            {
                var m = matches[offset + i];
                sb.Append($"• {m.name}");
                if (!string.IsNullOrEmpty(m.category)) sb.Append($" [{m.category}]");
                sb.Append($" (Risk: {m.risk})\n");
                if (!string.IsNullOrEmpty(m.desc))
                {
                    string shortDesc = m.desc.Length > 180 ? m.desc.Substring(0, 180) + "..." : m.desc;
                    sb.Append("    ").Append(shortDesc.Replace("\r", " ").Replace("\n", " ")).Append('\n');
                }
            }
            int remaining = offset < matches.Count ? matches.Count - offset - take : 0;
            sb.Append("\nPage: ").Append(JNode.Obj(("total", JNode.Num(matches.Count)), ("offset", JNode.Num(offset)),
                ("limit", JNode.Num(limit)), ("returned", JNode.Num(take)), ("remaining", JNode.Num(remaining)),
                ("hasMore", JNode.Bool(remaining > 0)), ("nextOffset", remaining > 0 ? JNode.Num(offset + take) : JNode.NullNode)).ToJson());
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
                sb.Append("\n**Result paging:** ExecuteUnityTool envelope resultOffset=0, resultLimit=50 (1..200), resultMaxChars=8192 (1024..32768). " +
                    "These do not replace the tool's arguments.offset/limit. Long results include resultId, text and page.nextOffset; " +
                    "read further pages with ExecuteUnityTool(name=\"ReadUnityToolResultPage\", arguments={\"resultId\":\"...\",\"offset\":...}). " +
                    "Continuation reads the completed snapshot and never executes the original action again.");
                return sb.ToString();
            }
            return $"Error: Tool '{name}' not found.";
        }

        /// <summary>
        /// initialize が返す serverInfo.version。
        /// 以前は Assembly.Version を返していたが、このアセンブリに [assembly: AssemblyVersion] が
        /// 無いため実際には常に "0.0.0.0" になっていた。接続してきたクライアントが見る唯一の
        /// バージョンなので、package.json 由来の実バージョン (UpdateChecker.CurrentVersion) を返す。
        /// </summary>
        static string GetPackageVersion()
        {
            try
            {
                var version = UpdateChecker.CurrentVersion;
                return string.IsNullOrEmpty(version) ? "0.0.0" : version;
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
