using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    internal sealed class EndNodeView : FlowchartNodeBase
    {
        public Port InputPort { get; private set; }

        public EndNodeView(FlowchartNode model, MD3Theme theme) : base(model, theme)
        {
            title = "■ End";
            // End nodes are deletable so users can repurpose graphs that ended differently.
            Rebuild();
        }

        public override void Rebuild()
        {
            var (input, _) = AddSinglePortPair(hasInput: true, hasOutput: false);
            InputPort = input;

            var hint = new Label("フロー終端");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginLeft = 8;
            hint.style.marginRight = 8;
            hint.style.marginTop = 4;
            hint.style.marginBottom = 4;
            hint.style.fontSize = 11;
            if (Theme != null) hint.style.color = Theme.OnSurfaceVariant;
            extensionContainer.Add(hint);

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
