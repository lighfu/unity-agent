using System;
using System.Collections.Generic;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart
{
    /// <summary>
    /// Flowchart graph root — serializable POCO that round-trips through JsonUtility.
    /// Compiles to a Skill .md file via FlowchartCompiler.
    /// </summary>
    [Serializable]
    public class FlowchartGraph
    {
        public int schemaVersion = 1;
        public string id;            // file-name stem, must be kebab-case (matches skill id rules)
        public string title;
        public string description;
        public string tags;          // comma-separated, mirrors Skill front-matter convention

        public List<FlowchartNode> nodes = new List<FlowchartNode>();
        public List<FlowchartEdge> edges = new List<FlowchartEdge>();

        public string compiledFromHash;
        public string compiledAt;    // ISO-8601 UTC

        public FlowchartGraph Clone()
        {
            string json = JsonUtility.ToJson(this);
            return JsonUtility.FromJson<FlowchartGraph>(json);
        }
    }

    public enum FlowchartNodeKind
    {
        Start = 0,
        End = 1,
        ToolCall = 2,
        AIDecide = 3,
        SubFlowchart = 4,
    }

    /// <summary>
    /// Single node container — JsonUtility doesn't support polymorphism,
    /// so all node-kind specific fields live here and are interpreted via <see cref="kind"/>.
    /// Unused fields stay at default and are tolerated by the serializer.
    /// </summary>
    [Serializable]
    public class FlowchartNode
    {
        public string id;             // stable graph-local id, e.g. "n_renderers"
        public FlowchartNodeKind kind;
        public Vector2 pos;

        // ── ToolCall ───────────────────────────────────────────────
        public string tool;                                // method name (matches AgentTool registry)
        public List<FlowchartArg> args = new List<FlowchartArg>();
        public string alias;                               // optional friendly variable name

        // ── AIDecide ───────────────────────────────────────────────
        public string prompt;                              // human-readable decision instruction
        public List<FlowchartBranch> branches = new List<FlowchartBranch>();

        // ── SubFlowchart ───────────────────────────────────────────
        public string skillName;                           // target skill id

        // ── Start ──────────────────────────────────────────────────
        public List<FlowchartInput> inputs = new List<FlowchartInput>();
    }

    [Serializable]
    public class FlowchartArg
    {
        public string name;     // parameter name
        public string value;    // raw text; may contain {{alias}} / {{stepN}} / {{input.foo}}
    }

    [Serializable]
    public class FlowchartBranch
    {
        public string label;    // human-readable condition, e.g. "見つかった"
        public string next;     // node id to jump to when this branch matches
    }

    [Serializable]
    public class FlowchartInput
    {
        public string name;
        public string label;
        public string type = "string";   // "string" | "int" | "float" | "bool"
    }

    [Serializable]
    public class FlowchartEdge
    {
        public string from;     // node id
        public string to;       // node id
    }
}
