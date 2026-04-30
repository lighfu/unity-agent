using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes
{
    /// <summary>
    /// Common scaffolding for every flowchart node view.
    /// Each subclass owns a back-reference to the FlowchartNode model so the editor can
    /// sync positions/branches back to the graph on save without a separate lookup table.
    /// </summary>
    internal abstract class FlowchartNodeBase : Node
    {
        public FlowchartNode Model { get; }
        protected MD3Theme Theme { get; }

        public event Action<FlowchartNodeBase> Selected;
        public event Action<FlowchartNodeBase> Changed;

        protected FlowchartNodeBase(FlowchartNode model, MD3Theme theme)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Theme = theme;

            // Store the node id on the visual element so GraphView lookups (selection,
            // edge endpoints) can map back to the model without a managed dictionary.
            viewDataKey = model.id;

            // Stable id used when marshalling between view and model.
            userData = model.id;

            // MD3 surface — a thin wrapper around GraphView's default node chrome that
            // recolors the title bar and main background to match the agent window.
            AddToClassList("ua-flow-node");
            if (theme != null)
            {
                style.borderTopLeftRadius = 8;
                style.borderTopRightRadius = 8;
                style.borderBottomLeftRadius = 8;
                style.borderBottomRightRadius = 8;
            }

            ApplyKindClass();
        }

        public Vector2 GetCanvasPosition() => GetPosition().position;

        public void SetCanvasPosition(Vector2 pos)
        {
            var rect = GetPosition();
            rect.position = pos;
            SetPosition(rect);
            Model.pos = pos;
        }

        public override void OnSelected()
        {
            base.OnSelected();
            Selected?.Invoke(this);
        }

        protected void NotifyChanged() => Changed?.Invoke(this);

        /// <summary>Subclasses build their kind-specific body here; called once after construction.</summary>
        public abstract void Rebuild();

        private void ApplyKindClass()
        {
            // USS hooks per node kind — see FlowchartGraphView.uss.
            AddToClassList("ua-flow-node--" + Model.kind.ToString().ToLowerInvariant());
        }

        /// <summary>Helper for subclasses to add a single in/out control-flow port pair.</summary>
        protected (Port input, Port output) AddSinglePortPair(bool hasInput, bool hasOutput)
        {
            Port input = null, output = null;
            if (hasInput)
            {
                input = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                input.portName = "in";
                inputContainer.Add(input);
            }
            if (hasOutput)
            {
                output = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                output.portName = "out";
                outputContainer.Add(output);
            }
            return (input, output);
        }

        protected Port AddBranchOutputPort(string label, string branchKey)
        {
            var port = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            port.portName = string.IsNullOrEmpty(label) ? "branch" : label;
            // Stash branch identity so the graph view can read it back when the user reconnects.
            port.userData = branchKey;
            outputContainer.Add(port);
            return port;
        }
    }
}
