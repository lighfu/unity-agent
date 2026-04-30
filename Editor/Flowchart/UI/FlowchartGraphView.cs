using System;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Hosts the flowchart canvas. Owns one FlowchartNodeBase per FlowchartNode and translates
    /// GraphView's edge-set back into FlowchartGraph.edges + AIDecide branch destinations on save.
    /// </summary>
    internal sealed class FlowchartGraphView : GraphView
    {
        readonly MD3Theme _theme;
        FlowchartGraph _graph;

        public event Action<FlowchartNodeBase> NodeSelected;
        public event Action GraphChanged;
        public event Action<FlowchartNodeKind, Vector2> AddNodeRequested;

        public FlowchartGraph Graph => _graph;

        public FlowchartGraphView(MD3Theme theme)
        {
            _theme = theme;

            // Standard GraphView ergonomics — pan/zoom, marquee select, and drag-drop edges.
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Background grid.
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // USS class hooks for theming. The companion stylesheet is loaded by the host window.
            AddToClassList("ua-flow-graphview");

            graphViewChanged = OnGraphViewChanged;
            style.flexGrow = 1;

            // Tool palette drops land here as ToolCall nodes.
            RegisterCallback<DragEnterEvent>(_ => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            RegisterCallback<DragUpdatedEvent>(_ => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        void OnDragPerform(DragPerformEvent evt)
        {
            if (_graph == null) return;
            var entry = DragAndDrop.GetGenericData(FlowchartToolPalette.DragGenericKey) as ToolPaletteEntry;
            if (entry == null) return;

            DragAndDrop.AcceptDrag();
            Vector2 canvasPos = contentViewContainer.WorldToLocal(evt.mousePosition);

            var model = AddNode(FlowchartNodeKind.ToolCall, canvasPos);
            if (model == null) return;
            model.tool = entry.Name;
            model.args = BuildDefaultArgs(entry);

            // Refresh the visual node so the new title/summary lights up.
            var view = nodes.ToList().OfType<ToolCallNodeView>().FirstOrDefault(n => n.Model == model);
            view?.RefreshTitleAndSummary();

            GraphChanged?.Invoke();
        }

        static System.Collections.Generic.List<FlowchartArg> BuildDefaultArgs(ToolPaletteEntry entry)
        {
            var args = new System.Collections.Generic.List<FlowchartArg>();
            if (entry.Method == null) return args;
            foreach (var p in entry.Method.GetParameters())
            {
                args.Add(new FlowchartArg
                {
                    name = p.Name,
                    value = p.HasDefaultValue && p.DefaultValue != null ? p.DefaultValue.ToString() : "",
                });
            }
            return args;
        }

        public void Bind(FlowchartGraph graph)
        {
            _graph = graph ?? new FlowchartGraph();
            RebuildAll();
        }

        // ─── Population from model ───────────────────────────────────────────

        public void RebuildAll()
        {
            DeleteElements(graphElements.ToList());
            if (_graph == null) return;

            var byId = new Dictionary<string, FlowchartNodeBase>();
            foreach (var n in _graph.nodes)
            {
                if (n == null) continue;
                var view = CreateNodeView(n);
                if (view == null) continue;
                view.SetCanvasPosition(n.pos);
                AddElement(view);
                byId[n.id] = view;
            }

            // Restore explicit edges first so plain ToolCall→ToolCall connections show up.
            if (_graph.edges != null)
            {
                foreach (var e in _graph.edges)
                {
                    if (e == null) continue;
                    if (!byId.TryGetValue(e.from, out var fromView)) continue;
                    if (!byId.TryGetValue(e.to, out var toView)) continue;

                    var outPort = GetPrimaryOutputPort(fromView);
                    var inPort = GetPrimaryInputPort(toView);
                    if (outPort == null || inPort == null) continue;

                    ConnectPorts(outPort, inPort);
                }
            }

            // AIDecide branches — visualize the per-branch ports as edges so they round-trip.
            foreach (var n in _graph.nodes)
            {
                if (n == null || n.kind != FlowchartNodeKind.AIDecide) continue;
                if (n.branches == null) continue;
                if (!byId.TryGetValue(n.id, out var fromView)) continue;
                if (!(fromView is AIDecideNodeView decide)) continue;

                for (int i = 0; i < decide.BranchPorts.Count && i < n.branches.Count; i++)
                {
                    var branch = n.branches[i];
                    if (branch == null || string.IsNullOrEmpty(branch.next)) continue;
                    if (!byId.TryGetValue(branch.next, out var toView)) continue;
                    var inPort = GetPrimaryInputPort(toView);
                    if (inPort == null) continue;
                    ConnectPorts(decide.BranchPorts[i], inPort);
                }
            }
        }

        FlowchartNodeBase CreateNodeView(FlowchartNode model)
        {
            FlowchartNodeBase view = model.kind switch
            {
                FlowchartNodeKind.Start => new StartNodeView(model, _theme),
                FlowchartNodeKind.End => new EndNodeView(model, _theme),
                FlowchartNodeKind.ToolCall => new ToolCallNodeView(model, _theme),
                FlowchartNodeKind.AIDecide => new AIDecideNodeView(model, _theme),
                FlowchartNodeKind.SubFlowchart => new SubFlowchartNodeView(model, _theme),
                _ => null,
            };
            if (view != null)
            {
                view.Selected += v => NodeSelected?.Invoke(v);
                view.Changed += _ => GraphChanged?.Invoke();
            }
            return view;
        }

        void ConnectPorts(Port output, Port input)
        {
            var edge = output.ConnectTo(input);
            AddElement(edge);
        }

        // ─── Sync back to model ──────────────────────────────────────────────

        public void SyncModel()
        {
            if (_graph == null) return;

            // Positions
            foreach (var element in nodes.ToList())
            {
                if (element is FlowchartNodeBase fnv)
                    fnv.Model.pos = fnv.GetCanvasPosition();
            }

            // Plain edges (excluding AIDecide branch ports — those are persisted via Model.branches).
            _graph.edges = new List<FlowchartEdge>();
            foreach (var element in edges.ToList())
            {
                var fromNode = element.output?.node as FlowchartNodeBase;
                var toNode = element.input?.node as FlowchartNodeBase;
                if (fromNode == null || toNode == null) continue;

                if (fromNode is AIDecideNodeView)
                {
                    // Branch destinations are already on the model.branches[i].next — skip
                    // here so the edge isn't duplicated as both branch and FlowchartEdge.
                    continue;
                }
                _graph.edges.Add(new FlowchartEdge { from = fromNode.Model.id, to = toNode.Model.id });
            }

            // AIDecide branches — read from the live graph, then write back to model.
            foreach (var element in nodes.ToList())
            {
                if (!(element is AIDecideNodeView decide)) continue;
                var model = decide.Model;
                if (model.branches == null) model.branches = new List<FlowchartBranch>();

                for (int i = 0; i < decide.BranchPorts.Count; i++)
                {
                    var port = decide.BranchPorts[i];
                    string nextId = null;
                    foreach (var ed in port.connections)
                    {
                        if (ed.input?.node is FlowchartNodeBase target)
                        {
                            nextId = target.Model.id;
                            break;
                        }
                    }
                    while (model.branches.Count <= i)
                        model.branches.Add(new FlowchartBranch());
                    model.branches[i].next = nextId;
                }
            }
        }

        // ─── Compatibility / change handling ─────────────────────────────────

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            // Translate the screen-space mouse position into canvas coordinates so the
            // new node lands where the user right-clicked, not at (0,0).
            Vector2 mouseScreen = evt.mousePosition;
            Vector2 canvasPos = contentViewContainer.WorldToLocal(mouseScreen);

            evt.menu.AppendSeparator();
            evt.menu.AppendAction(M("ノード追加/Start"), _ => AddNodeRequested?.Invoke(FlowchartNodeKind.Start, canvasPos));
            evt.menu.AppendAction(M("ノード追加/End"), _ => AddNodeRequested?.Invoke(FlowchartNodeKind.End, canvasPos));
            evt.menu.AppendAction(M("ノード追加/AI 判断"), _ => AddNodeRequested?.Invoke(FlowchartNodeKind.AIDecide, canvasPos));
            evt.menu.AppendAction(M("ノード追加/サブフロー"), _ => AddNodeRequested?.Invoke(FlowchartNodeKind.SubFlowchart, canvasPos));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction(M("ノード追加/ToolCall (空)"), _ => AddNodeRequested?.Invoke(FlowchartNodeKind.ToolCall, canvasPos));
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            ports.ForEach(port =>
            {
                if (port == startPort) return;
                if (port.node == startPort.node) return;       // no self-loops via own ports
                if (port.direction == startPort.direction) return; // out↔in only
                compatible.Add(port);
            });
            return compatible;
        }

        GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            // Removal of a node should also drop FlowchartEdge entries pointing at it. The
            // simpler invariant is "rebuild edges from the current view on every change",
            // which SyncModel handles, so we just bubble the event upward.
            if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
                GraphChanged?.Invoke();
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
                GraphChanged?.Invoke();
            if (change.movedElements != null && change.movedElements.Count > 0)
                GraphChanged?.Invoke();
            return change;
        }

        // ─── Node creation API for the host window ───────────────────────────

        public FlowchartNode AddNode(FlowchartNodeKind kind, Vector2 canvasPos)
        {
            if (_graph == null) return null;

            // Enforce a single Start node per graph.
            if (kind == FlowchartNodeKind.Start && _graph.nodes.Any(n => n.kind == FlowchartNodeKind.Start))
                return null;

            var model = new FlowchartNode
            {
                id = NewId(kind),
                kind = kind,
                pos = canvasPos,
            };
            if (kind == FlowchartNodeKind.AIDecide)
            {
                model.branches = new List<FlowchartBranch>
                {
                    new FlowchartBranch { label = "条件A" },
                    new FlowchartBranch { label = "条件B" },
                };
            }
            _graph.nodes.Add(model);

            var view = CreateNodeView(model);
            if (view != null)
            {
                view.SetCanvasPosition(canvasPos);
                AddElement(view);
            }
            GraphChanged?.Invoke();
            return model;
        }

        public void RemoveNode(FlowchartNodeBase view)
        {
            if (view == null || _graph == null) return;
            DeleteElements(new[] { view });
            _graph.nodes.RemoveAll(n => n != null && n.id == view.Model.id);
            GraphChanged?.Invoke();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        static Port GetPrimaryOutputPort(FlowchartNodeBase view) => view switch
        {
            StartNodeView s => s.OutputPort,
            ToolCallNodeView t => t.OutputPort,
            SubFlowchartNodeView s => s.OutputPort,
            // AIDecide is handled per-branch, not via a single primary output.
            _ => null,
        };

        static Port GetPrimaryInputPort(FlowchartNodeBase view) => view switch
        {
            EndNodeView e => e.InputPort,
            ToolCallNodeView t => t.InputPort,
            SubFlowchartNodeView s => s.InputPort,
            AIDecideNodeView a => a.InputPort,
            _ => null,
        };

        string NewId(FlowchartNodeKind kind)
        {
            string prefix = kind.ToString().ToLowerInvariant();
            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"n_{prefix}_{suffix}";
                suffix++;
            } while (_graph.nodes.Any(n => n != null && n.id == candidate));
            return candidate;
        }
    }
}
