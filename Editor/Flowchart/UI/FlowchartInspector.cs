using System;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Flowchart.UI.Nodes;
using AjisaiFlow.UnityAgent.Editor.Tools;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Right-side inspector. Rebuilds completely when the selected node changes
    /// (cheap because each node has at most a handful of fields). Calls back to the
    /// host window when the user edits any value so the dirty flag flips.
    /// </summary>
    internal sealed class FlowchartInspector : VisualElement
    {
        readonly MD3Theme _theme;
        readonly ScrollView _body;
        FlowchartNodeBase _current;

        public event Action ModelChanged;
        public event Action NodeRefreshRequested;  // ask the GraphView node to re-render its title/labels

        public FlowchartInspector(MD3Theme theme)
        {
            _theme = theme;
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1;

            var heading = new Label(M("インスペクター"));
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.paddingLeft = 8;
            heading.style.paddingRight = 8;
            heading.style.paddingTop = 8;
            heading.style.paddingBottom = 4;
            if (_theme != null) heading.style.color = _theme.OnSurface;
            Add(heading);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.style.flexGrow = 1;
            Add(_body);

            ShowEmpty();
        }

        public void Show(FlowchartNodeBase node)
        {
            _current = node;
            _body.Clear();
            if (node == null)
            {
                ShowEmpty();
                return;
            }

            AddCommonHeader(node);

            switch (node.Model.kind)
            {
                case FlowchartNodeKind.Start: BuildStart(node.Model); break;
                case FlowchartNodeKind.End: BuildEnd(); break;
                case FlowchartNodeKind.ToolCall: BuildToolCall(node); break;
                case FlowchartNodeKind.AIDecide: BuildAIDecide(node); break;
                case FlowchartNodeKind.SubFlowchart: BuildSubFlowchart(node); break;
            }
        }

        void ShowEmpty()
        {
            _body.Clear();
            var empty = new Label(M("ノードを選択してください。"));
            empty.style.paddingLeft = 8;
            empty.style.paddingRight = 8;
            empty.style.paddingTop = 8;
            empty.style.fontSize = 11;
            if (_theme != null) empty.style.color = _theme.OnSurfaceVariant;
            _body.Add(empty);
        }

        // ─── Common header ───────────────────────────────────────────────────

        void AddCommonHeader(FlowchartNodeBase node)
        {
            var idLabel = new Label(string.Format(M("ID: {0}"), node.Model.id));
            idLabel.style.paddingLeft = 8;
            idLabel.style.paddingRight = 8;
            idLabel.style.fontSize = 10;
            if (_theme != null) idLabel.style.color = _theme.OnSurfaceVariant;
            _body.Add(idLabel);

            var kindLabel = new Label(string.Format(M("種別: {0}"), node.Model.kind));
            kindLabel.style.paddingLeft = 8;
            kindLabel.style.paddingRight = 8;
            kindLabel.style.fontSize = 10;
            kindLabel.style.marginBottom = 8;
            if (_theme != null) kindLabel.style.color = _theme.OnSurfaceVariant;
            _body.Add(kindLabel);
        }

        // ─── Start ───────────────────────────────────────────────────────────

        void BuildStart(FlowchartNode model)
        {
            _body.Add(SectionHeader(M("入力 (フローパラメータ)")));

            var listContainer = new VisualElement();
            listContainer.style.marginLeft = 8;
            listContainer.style.marginRight = 8;
            _body.Add(listContainer);

            void RebuildInputs()
            {
                listContainer.Clear();
                if (model.inputs == null) model.inputs = new List<FlowchartInput>();
                for (int i = 0; i < model.inputs.Count; i++)
                {
                    int captured = i;
                    var input = model.inputs[captured];
                    if (input == null) { model.inputs[captured] = input = new FlowchartInput(); }

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 4;

                    var nameField = new TextField();
                    nameField.value = input.name ?? "";
                    nameField.style.flexGrow = 1;
                    nameField.style.marginRight = 4;
                    nameField.RegisterValueChangedCallback(evt =>
                    {
                        input.name = evt.newValue;
                        ModelChanged?.Invoke();
                    });

                    var typePopup = new PopupField<string>(
                        choices: new List<string> { "string", "int", "float", "bool" },
                        defaultValue: string.IsNullOrEmpty(input.type) ? "string" : input.type);
                    typePopup.style.minWidth = 70;
                    typePopup.style.marginRight = 4;
                    typePopup.RegisterValueChangedCallback(evt =>
                    {
                        input.type = evt.newValue;
                        ModelChanged?.Invoke();
                    });

                    var removeBtn = new Button(() =>
                    {
                        model.inputs.RemoveAt(captured);
                        ModelChanged?.Invoke();
                        RebuildInputs();
                    }) { text = "×" };
                    removeBtn.style.width = 24;

                    row.Add(nameField);
                    row.Add(typePopup);
                    row.Add(removeBtn);
                    listContainer.Add(row);
                }
            }

            RebuildInputs();

            var addBtn = new Button(() =>
            {
                if (model.inputs == null) model.inputs = new List<FlowchartInput>();
                model.inputs.Add(new FlowchartInput { name = $"input{model.inputs.Count + 1}", type = "string" });
                ModelChanged?.Invoke();
                RebuildInputs();
            }) { text = M("+ 入力を追加") };
            addBtn.style.marginLeft = 8;
            addBtn.style.marginRight = 8;
            addBtn.style.marginTop = 4;
            _body.Add(addBtn);
        }

        // ─── End ─────────────────────────────────────────────────────────────

        void BuildEnd()
        {
            var note = new Label(M("End ノードに編集項目はありません。"));
            note.style.paddingLeft = 8;
            note.style.paddingRight = 8;
            note.style.fontSize = 11;
            if (_theme != null) note.style.color = _theme.OnSurfaceVariant;
            _body.Add(note);
        }

        // ─── ToolCall ────────────────────────────────────────────────────────

        void BuildToolCall(FlowchartNodeBase nodeView)
        {
            var model = nodeView.Model;
            _body.Add(SectionHeader(M("ツール")));

            var toolField = TextRow(M("Tool"), model.tool ?? "", v =>
            {
                model.tool = v;
                ModelChanged?.Invoke();
                if (nodeView is ToolCallNodeView tv) tv.RefreshTitleAndSummary();
                NodeRefreshRequested?.Invoke();
                Show(nodeView); // rebuild inspector to refresh args from MethodInfo
            });
            _body.Add(toolField);

            // Parameter form built from MethodInfo when the tool resolves.
            var entry = ToolPaletteSource.FindByName(model.tool);
            _body.Add(SectionHeader(M("引数")));

            if (entry == null || entry.Method == null)
            {
                var hint = new Label(string.IsNullOrEmpty(model.tool)
                    ? M("ツール名を入力するかパレットからドラッグしてください。")
                    : string.Format(M("ツール `{0}` がレジストリに見つかりません。"), model.tool));
                hint.style.paddingLeft = 8;
                hint.style.paddingRight = 8;
                hint.style.fontSize = 11;
                if (_theme != null) hint.style.color = _theme.OnSurfaceVariant;
                _body.Add(hint);
            }
            else
            {
                if (model.args == null) model.args = new List<FlowchartArg>();
                foreach (var p in entry.Method.GetParameters())
                {
                    var arg = model.args.FirstOrDefault(a => a != null && a.name == p.Name);
                    if (arg == null)
                    {
                        arg = new FlowchartArg { name = p.Name, value = "" };
                        model.args.Add(arg);
                    }
                    string label = $"{p.Name} ({p.ParameterType.Name})";
                    _body.Add(TextRow(label, arg.value ?? "", v =>
                    {
                        arg.value = v;
                        ModelChanged?.Invoke();
                    }));
                }

                if (entry.Method.GetParameters().Length == 0)
                {
                    var none = new Label(M("(引数なし)"));
                    none.style.paddingLeft = 8;
                    none.style.fontSize = 11;
                    if (_theme != null) none.style.color = _theme.OnSurfaceVariant;
                    _body.Add(none);
                }
            }

            _body.Add(SectionHeader(M("エイリアス")));
            _body.Add(TextRow(M("alias"), model.alias ?? "", v =>
            {
                model.alias = v;
                ModelChanged?.Invoke();
                if (nodeView is ToolCallNodeView tv) tv.RefreshTitleAndSummary();
                NodeRefreshRequested?.Invoke();
            }));
        }

        // ─── AIDecide ────────────────────────────────────────────────────────

        void BuildAIDecide(FlowchartNodeBase nodeView)
        {
            var model = nodeView.Model;
            _body.Add(SectionHeader(M("判断条件")));

            var promptField = new TextField();
            promptField.multiline = true;
            promptField.value = model.prompt ?? "";
            promptField.style.marginLeft = 8;
            promptField.style.marginRight = 8;
            promptField.style.minHeight = 80;
            promptField.style.whiteSpace = WhiteSpace.Normal;
            promptField.RegisterValueChangedCallback(evt =>
            {
                model.prompt = evt.newValue;
                ModelChanged?.Invoke();
                if (nodeView is AIDecideNodeView dv) dv.RefreshPrompt();
                NodeRefreshRequested?.Invoke();
            });
            _body.Add(promptField);

            _body.Add(SectionHeader(M("分岐")));

            var listContainer = new VisualElement();
            listContainer.style.marginLeft = 8;
            listContainer.style.marginRight = 8;
            _body.Add(listContainer);

            void RebuildBranches()
            {
                listContainer.Clear();
                if (model.branches == null) model.branches = new List<FlowchartBranch>();
                for (int i = 0; i < model.branches.Count; i++)
                {
                    int captured = i;
                    var branch = model.branches[captured];
                    if (branch == null) { model.branches[captured] = branch = new FlowchartBranch(); }

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 4;

                    var labelField = new TextField();
                    labelField.value = branch.label ?? "";
                    labelField.style.flexGrow = 1;
                    labelField.style.marginRight = 4;
                    labelField.RegisterValueChangedCallback(evt =>
                    {
                        branch.label = evt.newValue;
                        ModelChanged?.Invoke();
                    });

                    var nextLabel = new Label(string.IsNullOrEmpty(branch.next) ? M("(未接続)") : "→ " + branch.next);
                    nextLabel.style.fontSize = 10;
                    nextLabel.style.marginRight = 4;
                    if (_theme != null) nextLabel.style.color = _theme.OnSurfaceVariant;

                    var removeBtn = new Button(() =>
                    {
                        model.branches.RemoveAt(captured);
                        ModelChanged?.Invoke();
                        Show(nodeView); // rebuild because branch ports change
                    }) { text = "×" };
                    removeBtn.style.width = 24;

                    row.Add(labelField);
                    row.Add(nextLabel);
                    row.Add(removeBtn);
                    listContainer.Add(row);
                }
            }

            RebuildBranches();

            var addBtn = new Button(() =>
            {
                if (model.branches == null) model.branches = new List<FlowchartBranch>();
                model.branches.Add(new FlowchartBranch { label = $"条件{model.branches.Count + 1}" });
                ModelChanged?.Invoke();
                Show(nodeView); // rebuild ports + UI
            }) { text = M("+ 分岐を追加") };
            addBtn.style.marginLeft = 8;
            addBtn.style.marginRight = 8;
            addBtn.style.marginTop = 4;
            _body.Add(addBtn);

            var hint = new Label(M("分岐ノードを増やしたら、保存後にキャンバスを再描画して接続してください。"));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.paddingLeft = 8;
            hint.style.paddingRight = 8;
            hint.style.fontSize = 10;
            hint.style.marginTop = 4;
            if (_theme != null) hint.style.color = _theme.OnSurfaceVariant;
            _body.Add(hint);
        }

        // ─── SubFlowchart ────────────────────────────────────────────────────

        void BuildSubFlowchart(FlowchartNodeBase nodeView)
        {
            var model = nodeView.Model;
            _body.Add(SectionHeader(M("呼び出すスキル")));

            var skills = SkillTools.GetAllSkills().Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            string current = model.skillName ?? "";
            if (!skills.Contains(current) && !string.IsNullOrEmpty(current))
                skills.Insert(0, current);

            if (skills.Count == 0)
            {
                _body.Add(TextRow(M("skillName"), model.skillName ?? "", v =>
                {
                    model.skillName = v;
                    ModelChanged?.Invoke();
                    if (nodeView is SubFlowchartNodeView sv) sv.RefreshTitleAndSummary();
                    NodeRefreshRequested?.Invoke();
                }));
            }
            else
            {
                var dropdown = new PopupField<string>(skills,
                    skills.Contains(current) ? current : skills[0]);
                dropdown.style.marginLeft = 8;
                dropdown.style.marginRight = 8;
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    model.skillName = evt.newValue;
                    ModelChanged?.Invoke();
                    if (nodeView is SubFlowchartNodeView sv) sv.RefreshTitleAndSummary();
                    NodeRefreshRequested?.Invoke();
                });
                _body.Add(dropdown);
                if (string.IsNullOrEmpty(model.skillName))
                {
                    model.skillName = dropdown.value;
                    ModelChanged?.Invoke();
                }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        VisualElement SectionHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.paddingTop = 6;
            label.style.paddingBottom = 4;
            label.style.fontSize = 11;
            if (_theme != null)
            {
                label.style.color = _theme.OnSurfaceVariant;
                label.style.backgroundColor = _theme.SurfaceContainerLow;
            }
            return label;
        }

        VisualElement TextRow(string label, string initialValue, Action<string> onChange)
        {
            var field = new TextField(label);
            field.value = initialValue ?? "";
            field.style.marginLeft = 8;
            field.style.marginRight = 8;
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return field;
        }
    }
}
