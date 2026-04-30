using System.IO;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart
{
    /// <summary>
    /// Manual smoke-test entry points for the flowchart compiler.
    /// These menu items are intentionally not user-facing — they exist so the developer can
    /// regenerate a canonical sample and inspect the produced markdown without booting the
    /// full editor UI. Safe to delete once the GraphView UI lands.
    /// </summary>
    internal static class FlowchartDebugMenu
    {
        [MenuItem("Window/紫陽花広場/_Debug/Flowchart: Compile Sample to Console")]
        public static void CompileSampleToConsole()
        {
            var graph = BuildSampleGraph();
            var result = FlowchartCompiler.Compile(graph);
            Debug.Log("=== Flowchart compiled markdown ===\n" + result.markdown);
            if (result.warnings.Count > 0)
                Debug.LogWarning("Warnings:\n - " + string.Join("\n - ", result.warnings));
            Debug.Log("Hash: " + result.hash);
        }

        [MenuItem("Window/紫陽花広場/_Debug/Flowchart: Save Sample to Disk")]
        public static void SaveSampleToDisk()
        {
            var graph = BuildSampleGraph();
            var result = FlowchartCompiler.Compile(graph);
            graph.compiledFromHash = result.hash;
            graph.compiledAt = System.DateTime.UtcNow.ToString("o");

            FlowchartIO.Save(graph);
            File.WriteAllText(FlowchartPaths.SkillFile(graph.id), result.markdown);
            AssetDatabase.Refresh();

            EditorUtility.RevealInFinder(FlowchartPaths.FlowFile(graph.id));
            Debug.Log($"Saved sample flowchart: {FlowchartPaths.FlowFile(graph.id)}");
            Debug.Log($"Compiled skill: {FlowchartPaths.SkillFile(graph.id)}");
        }

        private static FlowchartGraph BuildSampleGraph()
        {
            var g = new FlowchartGraph
            {
                id = "check-avatar-info-sample",
                title = "アバター情報を調べる (サンプル)",
                description = "アバターの構造とコンポーネント状態を一通り確認するサンプルフロー",
                tags = "avatar, inspection, sample",
            };

            g.nodes.Add(new FlowchartNode
            {
                id = "n_start",
                kind = FlowchartNodeKind.Start,
                pos = new Vector2(0, 0),
                inputs = new System.Collections.Generic.List<FlowchartInput>
                {
                    new FlowchartInput { name = "avatar", label = "アバター名", type = "string" },
                },
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_renderers",
                kind = FlowchartNodeKind.ToolCall,
                pos = new Vector2(240, 0),
                tool = "ListRenderers",
                alias = "renderers",
                args = new System.Collections.Generic.List<FlowchartArg>
                {
                    new FlowchartArg { name = "avatarName", value = "{{input.avatar}}" },
                },
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_body",
                kind = FlowchartNodeKind.ToolCall,
                pos = new Vector2(480, 0),
                tool = "IdentifyBodySmr",
                alias = "bodySmr",
                args = new System.Collections.Generic.List<FlowchartArg>
                {
                    new FlowchartArg { name = "avatarName", value = "{{input.avatar}}" },
                },
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_decide",
                kind = FlowchartNodeKind.AIDecide,
                pos = new Vector2(720, 0),
                prompt = "Step 'bodySmr' の結果を見て、Body SMR が見つかったかを判定し分岐する",
                branches = new System.Collections.Generic.List<FlowchartBranch>
                {
                    new FlowchartBranch { label = "見つかった", next = "n_inspect" },
                    new FlowchartBranch { label = "見つからなかった", next = "n_fallback" },
                },
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_inspect",
                kind = FlowchartNodeKind.SubFlowchart,
                pos = new Vector2(960, -120),
                skillName = "inspect-body-smr",
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_fallback",
                kind = FlowchartNodeKind.SubFlowchart,
                pos = new Vector2(960, 120),
                skillName = "scan-avatar-meshes",
            });
            g.nodes.Add(new FlowchartNode
            {
                id = "n_end",
                kind = FlowchartNodeKind.End,
                pos = new Vector2(1200, 0),
            });

            g.edges.Add(new FlowchartEdge { from = "n_start", to = "n_renderers" });
            g.edges.Add(new FlowchartEdge { from = "n_renderers", to = "n_body" });
            g.edges.Add(new FlowchartEdge { from = "n_body", to = "n_decide" });
            g.edges.Add(new FlowchartEdge { from = "n_inspect", to = "n_end" });
            g.edges.Add(new FlowchartEdge { from = "n_fallback", to = "n_end" });
            return g;
        }
    }
}
