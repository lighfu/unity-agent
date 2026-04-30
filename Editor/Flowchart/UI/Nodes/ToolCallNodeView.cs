using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    internal sealed class ToolCallNodeView : FlowchartNodeBase
    {
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        Label _summaryLabel;

        public ToolCallNodeView(FlowchartNode model, MD3Theme theme) : base(model, theme)
        {
            title = string.IsNullOrEmpty(model.tool) ? "(ツール未選択)" : model.tool;
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
            title = string.IsNullOrEmpty(Model.tool) ? "(ツール未選択)" : Model.tool;
            if (_summaryLabel != null) _summaryLabel.text = BuildSummary();
        }

        string BuildSummary()
        {
            int argCount = Model.args?.Count ?? 0;
            string aliasLine = string.IsNullOrEmpty(Model.alias) ? "" : $"\nalias: {Model.alias}";
            return $"{argCount} 引数{aliasLine}";
        }
    }
}
