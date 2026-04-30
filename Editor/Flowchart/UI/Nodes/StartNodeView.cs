using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    internal sealed class StartNodeView : FlowchartNodeBase
    {
        public Port OutputPort { get; private set; }

        public StartNodeView(FlowchartNode model, MD3Theme theme) : base(model, theme)
        {
            title = "▶ Start";
            capabilities &= ~Capabilities.Deletable; // Exactly one Start per graph; cannot remove.
            Rebuild();
        }

        public override void Rebuild()
        {
            var (_, output) = AddSinglePortPair(hasInput: false, hasOutput: true);
            OutputPort = output;

            var summary = new Label(BuildInputSummary());
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginLeft = 8;
            summary.style.marginRight = 8;
            summary.style.marginTop = 4;
            summary.style.marginBottom = 4;
            summary.style.fontSize = 11;
            if (Theme != null) summary.style.color = Theme.OnSurfaceVariant;
            extensionContainer.Add(summary);

            RefreshExpandedState();
            RefreshPorts();
        }

        private string BuildInputSummary()
        {
            if (Model.inputs == null || Model.inputs.Count == 0)
                return "(入力なし)";
            var parts = new System.Collections.Generic.List<string>(Model.inputs.Count);
            foreach (var input in Model.inputs)
            {
                if (input == null || string.IsNullOrEmpty(input.name)) continue;
                parts.Add($"{input.name}: {input.type ?? "string"}");
            }
            return parts.Count == 0 ? "(入力なし)" : string.Join("\n", parts);
        }
    }
}
