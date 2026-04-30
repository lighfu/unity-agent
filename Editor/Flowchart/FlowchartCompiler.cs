using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart
{
    /// <summary>
    /// Compiles a FlowchartGraph into Skill markdown that the existing AI execution path can consume.
    ///
    /// Output structure (mirrors Editor/Skills/_template.md so the AI sees a familiar shape):
    ///   1. Auto-generated banner (HTML comment) — warns humans not to hand-edit
    ///   2. YAML front-matter (title, description, tags, source: flowchart)
    ///   3. # Title
    ///   4. ## 概要         — from graph.description
    ///   5. ## 前提         — generated only if the Start node declares inputs
    ///   6. ## ツール一覧   — distinct ToolCall.tool list
    ///   7. ## 手順         — one ### Step N block per non-Start/End node in topological order
    ///   8. ## Notes        — auto-generated footer
    /// </summary>
    public static class FlowchartCompiler
    {
        public sealed class CompileResult
        {
            public string markdown;
            public string hash;
            public List<string> warnings = new List<string>();
        }

        // ─── Public API ──────────────────────────────────────────────────────

        public static CompileResult Compile(FlowchartGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var warnings = new List<string>();
            var ordered = TopoSort(graph, warnings);

            var sb = new StringBuilder();
            EmitBanner(sb, graph);
            EmitFrontMatter(sb, graph);
            EmitTitleAndOverview(sb, graph);
            EmitPrerequisites(sb, graph);
            EmitToolList(sb, graph);
            EmitProcedure(sb, graph, ordered, warnings);
            EmitNotes(sb);

            string md = sb.ToString();
            string hash = ComputeHash(graph);

            return new CompileResult { markdown = md, hash = hash, warnings = warnings };
        }

        public static string ComputeHash(FlowchartGraph graph)
        {
            // Hash a canonical form: zero out compiledFromHash / compiledAt so they
            // don't feed into their own recomputation.
            var clone = graph.Clone();
            clone.compiledFromHash = null;
            clone.compiledAt = null;
            string json = JsonUtility.ToJson(clone);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) hex.Append(b.ToString("x2"));
                return "sha256-" + hex.ToString();
            }
        }

        // ─── Topological sort ────────────────────────────────────────────────

        /// <summary>
        /// Pre-order DFS from Start, visiting successors in declaration order so AIDecide branches
        /// emit in the order the user listed them (branch[0] before branch[1]). This produces
        /// markdown the AI reads top-to-bottom as a narrative procedure. Cycles raise a warning
        /// (the second visit is suppressed by the visited-set guard).
        /// </summary>
        public static List<FlowchartNode> TopoSort(FlowchartGraph graph, List<string> warnings)
        {
            var byId = graph.nodes.ToDictionary(n => n.id, n => n);
            var successors = BuildSuccessors(graph);

            var start = graph.nodes.FirstOrDefault(n => n.kind == FlowchartNodeKind.Start);
            if (start == null)
            {
                warnings.Add("Start ノードが見つかりません。フローには 1 つの Start ノードが必要です。");
                return new List<FlowchartNode>(graph.nodes);
            }

            var visited = new HashSet<string>();
            var onStack = new HashSet<string>();
            var ordered = new List<FlowchartNode>();

            void Visit(FlowchartNode node)
            {
                if (!visited.Add(node.id)) return;
                ordered.Add(node);
                onStack.Add(node.id);

                if (successors.TryGetValue(node.id, out var succIds))
                {
                    foreach (var sid in succIds)
                    {
                        if (onStack.Contains(sid))
                        {
                            warnings.Add($"循環参照を検出: {node.id} → {sid}");
                            continue;
                        }
                        if (byId.TryGetValue(sid, out var succ))
                            Visit(succ);
                    }
                }

                onStack.Remove(node.id);
            }

            Visit(start);

            // Append unreachable nodes at the end so they aren't silently dropped from the .md.
            foreach (var n in graph.nodes)
            {
                if (!visited.Contains(n.id))
                {
                    warnings.Add($"到達不能なノード: {n.id} ({n.kind})");
                    ordered.Add(n);
                }
            }

            return ordered;
        }

        private static Dictionary<string, List<string>> BuildSuccessors(FlowchartGraph graph)
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var n in graph.nodes) map[n.id] = new List<string>();

            foreach (var e in graph.edges)
            {
                if (e == null || string.IsNullOrEmpty(e.from) || string.IsNullOrEmpty(e.to)) continue;
                if (!map.ContainsKey(e.from)) map[e.from] = new List<string>();
                map[e.from].Add(e.to);
            }

            // AIDecide carries its branch targets inline; merge them so a graph stays valid
            // even if the editor only stores branch destinations on the node (no FlowchartEdge).
            foreach (var n in graph.nodes)
            {
                if (n.kind != FlowchartNodeKind.AIDecide || n.branches == null) continue;
                if (!map.ContainsKey(n.id)) map[n.id] = new List<string>();
                foreach (var b in n.branches)
                {
                    if (b == null || string.IsNullOrEmpty(b.next)) continue;
                    if (!map[n.id].Contains(b.next)) map[n.id].Add(b.next);
                }
            }

            return map;
        }

        // ─── Markdown emission ───────────────────────────────────────────────

        private static void EmitBanner(StringBuilder sb, FlowchartGraph g)
        {
            sb.AppendLine("<!--");
            sb.AppendLine("  このファイルは Flowchart Editor によって自動生成されました。");
            sb.AppendLine("  直接編集しないでください。再生成すると上書きされます。");
            sb.AppendLine($"  Source: Assets/紫陽花広場/UnityAgent/Editor/Flowcharts/{g.id}.flow.json");
            sb.AppendLine("-->");
        }

        private static void EmitFrontMatter(StringBuilder sb, FlowchartGraph g)
        {
            sb.AppendLine("---");
            sb.AppendLine($"title: {SafeYaml(g.title ?? g.id ?? "")}");
            sb.AppendLine($"description: {SafeYaml(g.description ?? "")}");
            if (!string.IsNullOrEmpty(g.tags))
                sb.AppendLine($"tags: {SafeYaml(g.tags)}");
            sb.AppendLine("source: flowchart");
            sb.AppendLine("---");
            sb.AppendLine();
        }

        private static void EmitTitleAndOverview(StringBuilder sb, FlowchartGraph g)
        {
            sb.AppendLine($"# {g.title ?? g.id}");
            sb.AppendLine();
            sb.AppendLine("## 概要");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrEmpty(g.description)
                ? "(説明なし)"
                : g.description);
            sb.AppendLine();
        }

        private static void EmitPrerequisites(StringBuilder sb, FlowchartGraph g)
        {
            var start = g.nodes.FirstOrDefault(n => n.kind == FlowchartNodeKind.Start);
            if (start == null || start.inputs == null || start.inputs.Count == 0) return;

            sb.AppendLine("## 前提");
            sb.AppendLine();
            sb.AppendLine("このスキルを実行するときに AI が把握しておくべき入力:");
            sb.AppendLine();
            foreach (var input in start.inputs)
            {
                if (input == null || string.IsNullOrEmpty(input.name)) continue;
                string label = string.IsNullOrEmpty(input.label) ? input.name : input.label;
                sb.AppendLine($"- `{{{{input.{input.name}}}}}` ({input.type ?? "string"}) — {label}");
            }
            sb.AppendLine();
        }

        private static void EmitToolList(StringBuilder sb, FlowchartGraph g)
        {
            var toolNames = g.nodes
                .Where(n => n.kind == FlowchartNodeKind.ToolCall && !string.IsNullOrEmpty(n.tool))
                .Select(n => n.tool)
                .Distinct()
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            if (toolNames.Count == 0) return;

            sb.AppendLine("## ツール一覧");
            sb.AppendLine();
            foreach (var t in toolNames) sb.AppendLine($"- `{t}`");
            sb.AppendLine();

            // Sub-flowchart cross-references — emitted alongside tools so AI sees them as dependencies.
            var subSkills = g.nodes
                .Where(n => n.kind == FlowchartNodeKind.SubFlowchart && !string.IsNullOrEmpty(n.skillName))
                .Select(n => n.skillName)
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            if (subSkills.Count > 0)
            {
                sb.AppendLine("**呼び出すスキル:**");
                sb.AppendLine();
                foreach (var s in subSkills) sb.AppendLine($"- `[ReadSkill('{s}')]`");
                sb.AppendLine();
            }
        }

        private static void EmitProcedure(StringBuilder sb, FlowchartGraph g, List<FlowchartNode> ordered, List<string> warnings)
        {
            sb.AppendLine("## 手順");
            sb.AppendLine();

            // Build alias map: explicit alias wins; otherwise auto "stepN" for ToolCall in topo order.
            var aliasByNode = new Dictionary<string, string>();
            int stepCounter = 0;
            foreach (var node in ordered)
            {
                if (node.kind == FlowchartNodeKind.ToolCall)
                {
                    stepCounter++;
                    string a = string.IsNullOrEmpty(node.alias) ? $"step{stepCounter}" : node.alias;
                    aliasByNode[node.id] = a;
                }
            }

            int displayStep = 0;
            foreach (var node in ordered)
            {
                switch (node.kind)
                {
                    case FlowchartNodeKind.Start:
                        // The Start node is structural; it doesn't generate a numbered step.
                        break;
                    case FlowchartNodeKind.End:
                        // Same — End is structural.
                        break;
                    case FlowchartNodeKind.ToolCall:
                        displayStep++;
                        EmitToolCallStep(sb, node, displayStep, aliasByNode);
                        break;
                    case FlowchartNodeKind.AIDecide:
                        displayStep++;
                        EmitAIDecideStep(sb, node, displayStep, g);
                        break;
                    case FlowchartNodeKind.SubFlowchart:
                        displayStep++;
                        EmitSubFlowchartStep(sb, node, displayStep);
                        break;
                }
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine("<!-- compiler warnings:");
                foreach (var w in warnings) sb.AppendLine($"  - {w}");
                sb.AppendLine("-->");
                sb.AppendLine();
            }
        }

        private static void EmitToolCallStep(StringBuilder sb, FlowchartNode n, int step, Dictionary<string, string> aliasByNode)
        {
            string headline = string.IsNullOrEmpty(n.tool) ? "(ツール未選択)" : n.tool;
            sb.AppendLine($"### Step {step}: {headline}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine($"[{n.tool}({FormatArgs(n.args)})]");
            sb.AppendLine("```");
            if (aliasByNode.TryGetValue(n.id, out var alias))
                sb.AppendLine($"← 結果は `{{{{{alias}}}}}` として後続ステップから参照可能。");
            sb.AppendLine();
        }

        private static void EmitAIDecideStep(StringBuilder sb, FlowchartNode n, int step, FlowchartGraph g)
        {
            sb.AppendLine($"### Step {step}: AI 判断");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrEmpty(n.prompt)
                ? "前ステップの結果を見て分岐する。"
                : n.prompt);
            sb.AppendLine();

            if (n.branches != null && n.branches.Count > 0)
            {
                sb.AppendLine("**分岐:**");
                sb.AppendLine();
                foreach (var b in n.branches)
                {
                    if (b == null) continue;
                    string label = string.IsNullOrEmpty(b.label) ? "(条件未記入)" : b.label;
                    string nextDesc = ResolveBranchTarget(b.next, g);
                    sb.AppendLine($"- 「{label}」の場合 → {nextDesc}");
                }
                sb.AppendLine();
            }
        }

        private static void EmitSubFlowchartStep(StringBuilder sb, FlowchartNode n, int step)
        {
            string skill = string.IsNullOrEmpty(n.skillName) ? "(スキル未選択)" : n.skillName;
            sb.AppendLine($"### Step {step}: サブスキル呼び出し ({skill})");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine($"[ReadSkill('{skill}')]");
            sb.AppendLine("```");
            sb.AppendLine("上記スキルの手順に従って実行し、完了後に元のフローへ戻る。");
            sb.AppendLine();
        }

        private static void EmitNotes(StringBuilder sb)
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine("- このスキルは Flowchart Editor から生成されたため、編集はフローチャート側で行うこと。");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static string FormatArgs(List<FlowchartArg> args)
        {
            if (args == null || args.Count == 0) return "";
            var parts = new List<string>(args.Count);
            foreach (var a in args)
            {
                if (a == null) continue;
                string val = a.value ?? "";
                // Always render as `name='value'` with single-quote escaping — matches the
                // hand-written skill convention (see Editor/Skills/_template.md).
                string escaped = val.Replace("\\", "\\\\").Replace("'", "\\'");
                if (string.IsNullOrEmpty(a.name))
                    parts.Add($"'{escaped}'");
                else
                    parts.Add($"{a.name}='{escaped}'");
            }
            return string.Join(", ", parts);
        }

        private static string ResolveBranchTarget(string targetId, FlowchartGraph g)
        {
            if (string.IsNullOrEmpty(targetId)) return "(分岐先未設定)";
            var target = g.nodes.FirstOrDefault(x => x.id == targetId);
            if (target == null) return $"未知のノード `{targetId}`";

            switch (target.kind)
            {
                case FlowchartNodeKind.SubFlowchart:
                    return $"`[ReadSkill('{target.skillName}')]` を実行";
                case FlowchartNodeKind.ToolCall:
                    return $"`{target.tool}` の手順へ";
                case FlowchartNodeKind.End:
                    return "終了";
                case FlowchartNodeKind.AIDecide:
                    return "次の AI 判断ステップへ";
                default:
                    return $"`{target.id}` へ";
            }
        }

        private static string SafeYaml(string value)
        {
            if (value == null) return "";
            // Quote when the value contains any character that would confuse YAML parsers.
            bool needsQuote = value.IndexOfAny(new[] { ':', '#', '\n', '"' }) >= 0
                              || value.StartsWith("-")
                              || value.StartsWith("[")
                              || value.StartsWith("{");
            if (!needsQuote) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
