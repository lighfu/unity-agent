using System;
using System.IO;
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
    /// Top-level flowchart authoring window. Three-pane layout:
    ///   left = tool palette (Phase 3 will populate)
    ///   center = GraphView canvas
    ///   right = inspector (Phase 4 will populate)
    /// </summary>
    internal sealed class FlowchartEditorWindow : EditorWindow
    {
        static string Title => M("フローチャート");

        FlowchartGraph _graph;
        bool _dirty;

        FlowchartGraphView _graphView;
        FlowchartInspector _inspector;
        VisualElement _palettePane;
        VisualElement _inspectorPane;
        Label _statusLabel;
        TextField _idField;
        TextField _titleField;
        MD3Theme _theme;

        [MenuItem("Window/紫陽花広場/フローチャート")]
        public static void Open()
        {
            var w = GetWindow<FlowchartEditorWindow>();
            w.titleContent = new GUIContent(Title);
            w.minSize = new Vector2(900, 540);
            w.Show();
            if (w._graph == null)
                w.LoadOrCreateGraph(NewBlankGraph());
        }

        public static FlowchartEditorWindow OpenWithGraph(FlowchartGraph graph)
        {
            var w = GetWindow<FlowchartEditorWindow>();
            w.titleContent = new GUIContent(Title);
            w.minSize = new Vector2(900, 540);
            w.Show();
            w.LoadOrCreateGraph(graph ?? NewBlankGraph());
            return w;
        }

        public static FlowchartEditorWindow OpenById(string id)
        {
            var graph = FlowchartIO.LoadById(id) ?? NewBlankGraph(id);
            return OpenWithGraph(graph);
        }

        public static FlowchartEditorWindow OpenNew(string suggestedId = null)
        {
            return OpenWithGraph(NewBlankGraph(suggestedId));
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────

        void CreateGUI()
        {
            _theme = FlowchartTheme.Resolve();
            BuildLayout();
            // If a graph was set before CreateGUI fired, hydrate the canvas now.
            if (_graph != null) _graphView.Bind(_graph);
        }

        void OnDestroy()
        {
            if (_dirty)
            {
                bool save = EditorUtility.DisplayDialog(
                    M("保存しますか?"),
                    M("未保存の変更があります。閉じる前に保存しますか?"),
                    M("保存"), M("破棄"));
                if (save) SaveGraph();
            }
        }

        // ─── Layout ──────────────────────────────────────────────────────────

        void BuildLayout()
        {
            rootVisualElement.Clear();

            var sheet = MD3Theme.LoadThemeStyleSheet();
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (compSheet != null) rootVisualElement.styleSheets.Add(compSheet);

            var ownSheet = LoadOwnStyleSheet();
            if (ownSheet != null) rootVisualElement.styleSheets.Add(ownSheet);

            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1;
            if (_theme != null)
                rootVisualElement.style.backgroundColor = _theme.Surface;

            rootVisualElement.Add(BuildToolbar());

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            rootVisualElement.Add(body);

            body.Add(BuildPalettePane());
            body.Add(BuildCanvasPane());
            body.Add(BuildInspectorPane());

            _statusLabel = new Label("");
            _statusLabel.style.paddingLeft = 8;
            _statusLabel.style.paddingRight = 8;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            _statusLabel.style.fontSize = 11;
            if (_theme != null) _statusLabel.style.color = _theme.OnSurfaceVariant;
            rootVisualElement.Add(_statusLabel);
        }

        VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 8;
            bar.style.paddingRight = 8;
            bar.style.paddingTop = 4;
            bar.style.paddingBottom = 4;
            if (_theme != null) bar.style.backgroundColor = _theme.SurfaceContainerLow;
            bar.style.borderBottomWidth = 1;
            if (_theme != null) bar.style.borderBottomColor = _theme.OutlineVariant;

            void AddButton(string label, Action handler)
            {
                var btn = new MD3Button(label, MD3ButtonStyle.Text);
                btn.style.marginRight = 6;
                btn.clicked += handler;
                bar.Add(btn);
            }

            AddButton(M("新規"), () => LoadOrCreateGraph(NewBlankGraph()));
            AddButton(M("開く"), PromptOpen);
            AddButton(M("保存"), SaveGraph);
            AddButton(M("コンパイル"), CompileGraph);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            _idField = new TextField(M("ID"));
            _idField.style.minWidth = 200;
            _idField.style.marginRight = 8;
            _idField.RegisterValueChangedCallback(evt =>
            {
                if (_graph != null)
                {
                    _graph.id = evt.newValue;
                    MarkDirty();
                }
            });
            bar.Add(_idField);

            _titleField = new TextField(M("タイトル"));
            _titleField.style.minWidth = 240;
            _titleField.RegisterValueChangedCallback(evt =>
            {
                if (_graph != null)
                {
                    _graph.title = evt.newValue;
                    MarkDirty();
                }
            });
            bar.Add(_titleField);

            return bar;
        }

        VisualElement BuildPalettePane()
        {
            _palettePane = new VisualElement();
            _palettePane.style.width = 240;
            _palettePane.style.flexShrink = 0;
            _palettePane.style.flexDirection = FlexDirection.Column;
            if (_theme != null) _palettePane.style.backgroundColor = _theme.SurfaceContainerLowest;
            _palettePane.style.borderRightWidth = 1;
            if (_theme != null) _palettePane.style.borderRightColor = _theme.OutlineVariant;

            _palettePane.Add(new FlowchartToolPalette(_theme));
            return _palettePane;
        }

        VisualElement BuildCanvasPane()
        {
            _graphView = new FlowchartGraphView(_theme);
            _graphView.style.flexGrow = 1;
            _graphView.GraphChanged += MarkDirty;
            _graphView.NodeSelected += OnNodeSelected;
            _graphView.AddNodeRequested += AddNodeAt;
            return _graphView;
        }

        VisualElement BuildInspectorPane()
        {
            _inspectorPane = new VisualElement();
            _inspectorPane.style.width = 300;
            _inspectorPane.style.flexShrink = 0;
            _inspectorPane.style.flexDirection = FlexDirection.Column;
            if (_theme != null) _inspectorPane.style.backgroundColor = _theme.SurfaceContainerLowest;
            _inspectorPane.style.borderLeftWidth = 1;
            if (_theme != null) _inspectorPane.style.borderLeftColor = _theme.OutlineVariant;

            _inspector = new FlowchartInspector(_theme);
            _inspector.ModelChanged += MarkDirty;
            _inspector.NodeRefreshRequested += () => { /* node visuals refresh themselves */ };
            _inspectorPane.Add(_inspector);
            return _inspectorPane;
        }

        // ─── Graph operations ────────────────────────────────────────────────

        void LoadOrCreateGraph(FlowchartGraph graph)
        {
            _graph = graph;
            _dirty = false;
            if (_graphView != null) _graphView.Bind(_graph);
            if (_idField != null) _idField.SetValueWithoutNotify(_graph.id ?? "");
            if (_titleField != null) _titleField.SetValueWithoutNotify(_graph.title ?? "");
            UpdateStatus();
            CheckDriftAndWarn();
        }

        /// <summary>
        /// If the compiled .md was edited externally since the last compile, prompt the user.
        /// Hand-edits are allowed but they'll be overwritten on next Compile, so we make
        /// that explicit rather than silently clobber the file.
        /// </summary>
        void CheckDriftAndWarn()
        {
            if (_graph == null || string.IsNullOrEmpty(_graph.id)) return;
            string mdPath = FlowchartPaths.SkillFile(_graph.id);
            if (!File.Exists(mdPath)) return;

            // Skip the check on first compile — compiledAt empty means we never compiled.
            if (string.IsNullOrEmpty(_graph.compiledAt)) return;

            DateTime compiledAt;
            if (!DateTime.TryParse(_graph.compiledAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out compiledAt))
                return;
            DateTime mdWriteTime = File.GetLastWriteTimeUtc(mdPath);

            // 2 second slop: file timestamps on Windows can drift by a small amount around save.
            if (mdWriteTime > compiledAt.AddSeconds(2))
            {
                EditorUtility.DisplayDialog(
                    M("外部編集を検出"),
                    string.Format(M("スキル `{0}.md` が最後のコンパイル以降に外部で編集されています。\n\n次回コンパイル時に上書きされます。.md の変更を保持したい場合はフローチャート側に反映してから続行してください。"), _graph.id),
                    "OK");
            }
        }

        void SaveGraph()
        {
            if (_graph == null) return;
            if (string.IsNullOrEmpty(_graph.id))
            {
                EditorUtility.DisplayDialog(M("ID 未入力"), M("保存する前にフロー ID を設定してください (kebab-case)。"), "OK");
                return;
            }

            _graphView?.SyncModel();
            FlowchartIO.Save(_graph);
            _dirty = false;
            UpdateStatus(M("保存しました"));
            AssetDatabase.Refresh();
        }

        void CompileGraph()
        {
            if (_graph == null) return;
            if (string.IsNullOrEmpty(_graph.id))
            {
                EditorUtility.DisplayDialog(M("ID 未入力"), M("コンパイルする前にフロー ID を設定してください。"), "OK");
                return;
            }

            _graphView?.SyncModel();
            var result = FlowchartCompiler.Compile(_graph);
            _graph.compiledFromHash = result.hash;
            _graph.compiledAt = DateTime.UtcNow.ToString("o");

            FlowchartPaths.EnsureDirs();
            File.WriteAllText(FlowchartPaths.SkillFile(_graph.id), result.markdown);
            FlowchartIO.Save(_graph);
            _dirty = false;
            AssetDatabase.Refresh();

            string msg = result.warnings.Count > 0
                ? string.Format(M("コンパイル完了 (警告 {0} 件)"), result.warnings.Count)
                : M("コンパイル完了");
            UpdateStatus(msg);
            if (result.warnings.Count > 0)
                Debug.LogWarning("Flowchart compile warnings:\n - " + string.Join("\n - ", result.warnings));
        }

        void PromptOpen()
        {
            string startDir = FlowchartPaths.FlowchartsDir;
            if (!Directory.Exists(startDir))
                Directory.CreateDirectory(startDir);
            string path = EditorUtility.OpenFilePanel(M("フローチャートを開く"), startDir, "json");
            if (string.IsNullOrEmpty(path)) return;

            var graph = FlowchartIO.Load(path);
            if (graph == null)
            {
                EditorUtility.DisplayDialog(M("読み込みエラー"), M("ファイルを開けませんでした。"), "OK");
                return;
            }
            LoadOrCreateGraph(graph);
        }

        // ─── Context menu / selection ────────────────────────────────────────

        void AddNodeAt(FlowchartNodeKind kind, Vector2 canvasPos)
        {
            var model = _graphView.AddNode(kind, canvasPos);
            if (model == null && kind == FlowchartNodeKind.Start)
            {
                EditorUtility.DisplayDialog(M("追加できません"), M("Start ノードはフローに 1 つだけ配置できます。"), "OK");
                return;
            }
            MarkDirty();
        }

        void OnNodeSelected(FlowchartNodeBase view)
        {
            _inspector?.Show(view);
            UpdateStatus($"選択: {view.Model.id} ({view.Model.kind})");
        }

        // ─── Misc ────────────────────────────────────────────────────────────

        void MarkDirty()
        {
            _dirty = true;
            UpdateStatus();
        }

        void UpdateStatus(string message = null)
        {
            if (_statusLabel == null) return;
            string id = _graph?.id ?? M("(no graph)");
            string dirty = _dirty ? " *" : "";
            string suffix = string.IsNullOrEmpty(message) ? "" : " — " + message;
            _statusLabel.text = $"{id}{dirty}{suffix}";
        }

        StyleSheet LoadOwnStyleSheet()
        {
            // The companion stylesheet lives next to this file. Loading it via AssetDatabase
            // keeps the editor working when the package is installed via VPM (the file is
            // relative to the package root in either case).
            const string path = "Packages/com.ajisaiflow.unityagent/Editor/Flowchart/UI/FlowchartGraphView.uss";
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null) return sheet;
            // Fallback when the package is consumed as Assets/ during local dev.
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/紫陽花広場/UnityAgent/Editor/Flowchart/UI/FlowchartGraphView.uss");
        }

        static FlowchartGraph NewBlankGraph(string id = null)
        {
            var graph = new FlowchartGraph
            {
                id = id ?? "new-flowchart",
                title = M("新規フロー"),
                description = "",
                tags = "",
            };
            graph.nodes.Add(new FlowchartNode
            {
                id = "n_start",
                kind = FlowchartNodeKind.Start,
                pos = new Vector2(40, 200),
            });
            graph.nodes.Add(new FlowchartNode
            {
                id = "n_end",
                kind = FlowchartNodeKind.End,
                pos = new Vector2(560, 200),
            });
            return graph;
        }
    }
}
