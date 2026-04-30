using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    internal sealed class AIDecideNodeView : FlowchartNodeBase
    {
        public Port InputPort { get; private set; }

        // One output port per branch. Keyed by branch index so the editor can rebuild
        // FlowchartBranch.next from the current edge state without ambiguity.
        public List<Port> BranchPorts { get; } = new List<Port>();

        Label _promptLabel;

        public AIDecideNodeView(FlowchartNode model, MD3Theme theme) : base(model, theme)
        {
            title = "🤖 AI 判断";
            Rebuild();
        }

        public override void Rebuild()
        {
            var input = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = "in";
            inputContainer.Add(input);
            InputPort = input;

            BranchPorts.Clear();
            if (Model.branches != null)
            {
                for (int i = 0; i < Model.branches.Count; i++)
                {
                    var b = Model.branches[i];
                    string label = (b != null && !string.IsNullOrEmpty(b.label)) ? b.label : $"branch{i + 1}";
                    var port = AddBranchOutputPort(label, branchKey: i.ToString());
                    BranchPorts.Add(port);
                }
            }

            _promptLabel = new Label(string.IsNullOrEmpty(Model.prompt) ? "(判断条件未記入)" : Model.prompt);
            _promptLabel.style.whiteSpace = WhiteSpace.Normal;
            _promptLabel.style.marginLeft = 8;
            _promptLabel.style.marginRight = 8;
            _promptLabel.style.marginTop = 4;
            _promptLabel.style.marginBottom = 4;
            _promptLabel.style.fontSize = 11;
            if (Theme != null) _promptLabel.style.color = Theme.OnSurfaceVariant;
            extensionContainer.Add(_promptLabel);

            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshPrompt()
        {
            if (_promptLabel != null)
                _promptLabel.text = string.IsNullOrEmpty(Model.prompt) ? "(判断条件未記入)" : Model.prompt;
        }
    }
}
