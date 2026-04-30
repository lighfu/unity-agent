using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    internal sealed class SubFlowchartNodeView : FlowchartNodeBase
    {
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        Label _summaryLabel;

        public SubFlowchartNodeView(FlowchartNode model, MD3Theme theme) : base(model, theme)
        {
            title = string.IsNullOrEmpty(model.skillName) ? "(スキル未選択)" : "→ " + model.skillName;
            Rebuild();
        }

        public override void Rebuild()
        {
            var (input, output) = AddSinglePortPair(hasInput: true, hasOutput: true);
            InputPort = input;
            OutputPort = output;

            _summaryLabel = new Label(BuildSummary());
            _summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            _summaryLabel.style.marginLeft = 8;
            _summaryLabel.style.marginRight = 8;
            _summaryLabel.style.marginTop = 4;
            _summaryLabel.style.marginBottom = 4;
            _summaryLabel.style.fontSize = 11;
            if (Theme != null) _summaryLabel.style.color = Theme.OnSurfaceVariant;
            extensionContainer.Add(_summaryLabel);

            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshTitleAndSummary()
        {
            title = string.IsNullOrEmpty(Model.skillName) ? "(スキル未選択)" : "→ " + Model.skillName;
            if (_summaryLabel != null) _summaryLabel.text = BuildSummary();
        }

        string BuildSummary()
        {
            return string.IsNullOrEmpty(Model.skillName)
                ? "ReadSkill 呼び出し先を選択"
                : $"[ReadSkill('{Model.skillName}')]";
        }
    }
}
