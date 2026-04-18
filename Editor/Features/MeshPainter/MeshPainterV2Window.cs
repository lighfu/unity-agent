using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.Editor.MeshPaint;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Mesh Painter v2 — MD3SDK (UI Toolkit) ベースで書き直した Mesh Painter。
    /// バックエンドは v1 と共通の <see cref="MeshPaintPreviewSession"/> /
    /// <see cref="TextureEditCore"/> / <see cref="ScenePaintState"/> を使う。
    /// 見た目だけが MD3 化されており、機能的には v1 と同等 (live preview + multi-island
    /// selection + 5 tabs)。
    /// </summary>
    public class MeshPainterV2Window : EditorWindow
    {
        // ─── Session / runtime state ───
        private readonly MeshPaintSessionManager _sessionManager = new MeshPaintSessionManager();
        private MeshPaintSessionEntry ActiveEntry => _sessionManager.Active;
        private MeshPaintPreviewSession Session => _sessionManager.Active?.Session;
        private VisualElement _editListContainer;
        private GameObject _avatarRoot;
        private Renderer _activeRenderer;
        private readonly List<RendererData> _rendererList = new List<RendererData>();
        private List<UVIsland> _currentIslands = new List<UVIsland>();
        private readonly HashSet<int> _selectedIslandIndices = new HashSet<int>();
        private MeshPaintMetadata _currentMetadata;
        private bool _use3DConnection;
        private bool _useMANonDestructive;
        private bool _isSceneSelectionEnabled = true;
        private int _editTab;
        private bool _suppressTabChanged;
        private int _prevEditTab;

        // Tab parameters (same field set as v1)
        private Color _targetColor = Color.white;
        private Color _gradFrom = Color.white;
        private Color _gradTo = Color.black;
        private int _gradDirectionIdx;
        private int _gradBlendModeIdx;
        private float _gradStartT;
        private float _gradEndT = 1f;
        private float _hueShift;
        private float _satScale = 1f;
        private float _valScale = 1f;
        private float _brightness;
        private float _contrast;
        private float _sceneBrushSize = 0.02f;
        private float _sceneBrushOpacity = 1f;
        private float _sceneBrushHardness = 0.8f;
        private Color _sceneBrushColor = Color.white;
        private int _sceneBlendModeIdx;
        private int _sceneToolIdx;
        private bool _sceneSymmetry;
        private bool _sceneIslandMask;

        // UV editor pan/zoom state (shared with the IMGUI sub-container)
        private float _uvZoom = 1f;
        private Vector2 _uvPan = new Vector2(0.5f, 0.5f);

        // MD3 UI refs (built in CreateGUI / BuildLayout)
        private MD3Theme _theme;
        private VisualElement _rendererListContainer;
        private Label _selectionSummaryLabel;
        private VisualElement _tabContentHost;
        private readonly List<MD3Tab> _tabButtons = new List<MD3Tab>();
        private Label _previewStatusLabel;
        private IMGUIContainer _uvImguiContainer;

        private static readonly string[] TabLabelsRaw = { "ペイント", "グラデーション", "HSV", "明るさ/コントラスト", "Sceneペイント" };
        private static readonly string[] DirectionValues = { "top_to_bottom", "bottom_to_top", "left_to_right", "right_to_left" };
        private static readonly string[] BlendModeValues = { "screen", "overlay", "tint", "multiply", "replace" };

        private class RendererData
        {
            public Renderer renderer;
            public bool isChecked;
            public string displayName;
            public string fullPath;
        }

        [MenuItem("Window/紫陽花広場/Mesh Painter v2")]
        public static void ShowWindow()
        {
            var w = GetWindow<MeshPainterV2Window>();
            w.titleContent = new GUIContent(M("メッシュペインター v2"));
            w.minSize = new Vector2(780, 520);
        }

        public static void OpenForRenderer(Renderer renderer)
        {
            var w = GetWindow<MeshPainterV2Window>();
            w.titleContent = new GUIContent(M("メッシュペインター v2"));
            var root = AutoDetectAvatarRoot(renderer.gameObject);
            if (root != null) w._avatarRoot = root;
            w.RefreshRendererList();
            w.SelectRenderer(renderer);
            w.Show();
            w.Focus();
        }

        // ══════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            ScenePaintState.OnColorPicked += HandleColorPicked;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            ScenePaintState.OnColorPicked -= HandleColorPicked;
            if (ScenePaintState.IsActive) ScenePaintState.Deactivate();
            // Window close discards staged ops without writing PNGs — the explicit
            // "Apply All" button is the only path that commits.
            _sessionManager.DisposeAll();
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            _theme = MD3Theme.Resolve(rootVisualElement);
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !rootVisualElement.styleSheets.Contains(themeSheet))
                rootVisualElement.styleSheets.Add(themeSheet);
            if (compSheet != null && !rootVisualElement.styleSheets.Contains(compSheet))
                rootVisualElement.styleSheets.Add(compSheet);
            _theme.ApplyTo(rootVisualElement);

            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.backgroundColor = _theme.Surface;

            BuildLayout();
            RefreshRendererList();
        }

        private void OnSelectionChange()
        {
            // Only update avatar root / renderer list from scene selection.
            // Do NOT auto-start a paint session on a renderer — that swaps
            // mat.mainTexture and feels like "mesh paint unintentionally activated".
            // The user must click a mesh in the list (or move a slider) to start one.
            if (Selection.activeGameObject == null) return;
            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null && _avatarRoot != root)
            {
                _avatarRoot = root;
                RefreshRendererList();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Layout
        // ══════════════════════════════════════════════════════════════

        private void BuildLayout()
        {
            // Top app bar
            var topBar = new VisualElement();
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.alignItems = Align.Center;
            topBar.style.paddingLeft = 12;
            topBar.style.paddingRight = 12;
            topBar.style.paddingTop = 8;
            topBar.style.paddingBottom = 8;
            topBar.style.backgroundColor = _theme.SurfaceContainer;
            topBar.style.borderBottomWidth = 1;
            topBar.style.borderBottomColor = _theme.OutlineVariant;

            var title = new Label(M("メッシュペインター v2"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            title.style.color = _theme.OnSurface;
            topBar.Add(title);

            var spacer = new MD3Spacer();
            topBar.Add(spacer);

            _previewStatusLabel = new Label("");
            _previewStatusLabel.style.color = _theme.OnSurfaceVariant;
            _previewStatusLabel.style.fontSize = 11;
            topBar.Add(_previewStatusLabel);

            rootVisualElement.Add(topBar);

            // Avatar root field row
            var rootRow = new VisualElement();
            rootRow.style.flexDirection = FlexDirection.Row;
            rootRow.style.alignItems = Align.Center;
            rootRow.style.paddingLeft = 12;
            rootRow.style.paddingRight = 12;
            rootRow.style.paddingTop = 6;
            rootRow.style.paddingBottom = 6;

            var rootLabel = new Label(M("アバタールート"));
            rootLabel.style.color = _theme.OnSurface;
            rootLabel.style.width = 100;
            rootRow.Add(rootLabel);

            var rootField = new UnityEditor.UIElements.ObjectField { objectType = typeof(GameObject), allowSceneObjects = true };
            rootField.value = _avatarRoot;
            rootField.style.width = 260;
            rootField.style.flexShrink = 0;
            rootField.RegisterValueChangedCallback(evt =>
            {
                _avatarRoot = evt.newValue as GameObject;
                RefreshRendererList();
            });
            rootRow.Add(rootField);

            var use3d = new MD3Switch(_use3DConnection);
            use3d.style.marginLeft = 8;
            use3d.changed += v =>
            {
                _use3DConnection = v;
                if (_activeRenderer != null)
                {
                    var r = _activeRenderer; _activeRenderer = null; PrepareEditor(r);
                }
            };
            var use3dLbl = new Label(M("3D接続"));
            use3dLbl.style.color = _theme.OnSurfaceVariant;
            use3dLbl.style.marginLeft = 4;
            use3dLbl.style.marginRight = 12;
            rootRow.Add(use3dLbl);
            rootRow.Add(use3d);

            var useMA = new MD3Switch(_useMANonDestructive);
            useMA.style.marginLeft = 8;
            useMA.changed += v => { _useMANonDestructive = v; };
            var useMALbl = new Label(M("MA非破壊"));
            useMALbl.style.color = _theme.OnSurfaceVariant;
            useMALbl.style.marginLeft = 4;
            useMALbl.style.marginRight = 12;
            useMALbl.tooltip = M("Modular Avatar を使用して非破壊的にテクスチャ変更を適用します");
            rootRow.Add(useMALbl);
            rootRow.Add(useMA);

            rootVisualElement.Add(rootRow);

            // Main content: a vertical TwoPaneSplitView so the user can drag the
            // divider between the top row (mesh list + tabs) and the UV preview.
            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.paddingLeft = 8;
            body.style.paddingRight = 8;
            body.style.paddingBottom = 8;
            rootVisualElement.Add(body);

            var split = new TwoPaneSplitView(
                fixedPaneIndex: 0,
                fixedPaneStartDimension: 300,
                orientation: TwoPaneSplitViewOrientation.Vertical);
            split.style.flexGrow = 1;
            body.Add(split);

            // Set the initial split ratio to 5:5 once the body has a real height.
            // TwoPaneSplitView stores its dimension in a private field; we reach it
            // via reflection + Init() (which is internal in some Unity versions).
            EventCallback<GeometryChangedEvent> onceSplit = null;
            onceSplit = evt =>
            {
                float h = split.resolvedStyle.height;
                if (h > 0)
                {
                    var initMethod = typeof(TwoPaneSplitView).GetMethod(
                        "Init",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (initMethod != null)
                    {
                        initMethod.Invoke(split, new object[] { 0, h * 0.5f, TwoPaneSplitViewOrientation.Vertical });
                    }
                    split.UnregisterCallback(onceSplit);
                }
            };
            split.RegisterCallback(onceSplit);

            // --- Top pane (A + C) ---
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.flexGrow = 1;
            split.Add(topRow);

            // [A] mesh list + edit list (stacked vertically in the left column)
            var leftCol = new VisualElement();
            leftCol.style.width = 260;
            leftCol.style.marginRight = 8;
            leftCol.style.flexDirection = FlexDirection.Column;

            var rendererCard = new MD3Card(M("メッシュ一覧"), null, MD3CardStyle.Outlined);
            rendererCard.style.flexGrow = 1;
            rendererCard.style.minHeight = 120;
            _rendererListContainer = new ScrollView(ScrollViewMode.Vertical);
            _rendererListContainer.style.flexGrow = 1;
            rendererCard.Add(_rendererListContainer);
            leftCol.Add(rendererCard);

            var editListCard = new MD3Card(M("編集リスト"), null, MD3CardStyle.Outlined);
            editListCard.style.marginTop = 6;
            editListCard.style.flexGrow = 1;
            editListCard.style.minHeight = 140;
            _editListContainer = new VisualElement();
            _editListContainer.style.flexDirection = FlexDirection.Column;
            editListCard.Add(_editListContainer);
            leftCol.Add(editListCard);

            topRow.Add(leftCol);

            // [C] right column (tabs + content)
            var rightCol = new VisualElement();
            rightCol.style.flexGrow = 1;
            rightCol.style.flexDirection = FlexDirection.Column;

            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.borderBottomWidth = 1;
            tabRow.style.borderBottomColor = _theme.OutlineVariant;
            tabRow.style.marginBottom = 6;
            for (int i = 0; i < TabLabelsRaw.Length; i++)
            {
                int idx = i;
                var tab = new MD3Tab(M(TabLabelsRaw[i]), selected: i == 0);
                tab.style.flexGrow = 1;
                tab.changed += _ => OnTabSelected(idx);
                _tabButtons.Add(tab);
                tabRow.Add(tab);
            }
            rightCol.Add(tabRow);

            _tabContentHost = new ScrollView(ScrollViewMode.Vertical);
            _tabContentHost.style.flexGrow = 1;
            rightCol.Add(_tabContentHost);

            topRow.Add(rightCol);

            // --- Bottom pane: [B] UV preview ---
            var uvCard = new MD3Card(M("UV展開図"), null, MD3CardStyle.Outlined);
            uvCard.style.flexGrow = 1;
            uvCard.style.marginTop = 4;
            _uvImguiContainer = new IMGUIContainer(DrawUVEditorIMGUI);
            _uvImguiContainer.style.flexGrow = 1;
            _uvImguiContainer.style.minHeight = 120;
            uvCard.Add(_uvImguiContainer);

            _selectionSummaryLabel = new Label("");
            _selectionSummaryLabel.style.color = _theme.OnSurfaceVariant;
            _selectionSummaryLabel.style.fontSize = 11;
            _selectionSummaryLabel.style.marginTop = 4;
            uvCard.Add(_selectionSummaryLabel);

            split.Add(uvCard);

            RebuildTabContent();
            RebuildEditListUI();
            UpdatePreviewStatusLabel();
            UpdateSelectionSummary();
        }

        // ══════════════════════════════════════════════════════════════
        // Renderer list (left column)
        // ══════════════════════════════════════════════════════════════

        private void RefreshRendererList()
        {
            _rendererList.Clear();
            if (_avatarRoot != null)
            {
                foreach (var r in _avatarRoot.GetComponentsInChildren<Renderer>(true))
                {
                    _rendererList.Add(new RendererData
                    {
                        renderer = r,
                        displayName = r.gameObject.name,
                        fullPath = GetRelativePath(r.transform, _avatarRoot.transform),
                        isChecked = false,
                    });
                }
            }
            RebuildRendererListUI();
        }

        // ══════════════════════════════════════════════════════════════
        // Edit history list (left column, below mesh list)
        // ══════════════════════════════════════════════════════════════

        private void RebuildEditListUI()
        {
            if (_editListContainer == null) return;
            _editListContainer.Clear();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.maxHeight = 260;
            _editListContainer.Add(scroll);

            bool any = false;
            foreach (var entry in _sessionManager.AllEntries)
            {
                if (entry.Ops.Count == 0) continue;
                any = true;

                var header = new Label(string.Format("▼ {0} ({1})",
                    entry.Renderer != null ? entry.Renderer.gameObject.name : "?", entry.Ops.Count));
                header.style.color = _theme.OnSurfaceVariant;
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.marginTop = 4;
                header.style.paddingLeft = 4;
                scroll.Add(header);

                for (int i = 0; i < entry.Ops.Count; i++)
                {
                    int opIndex = i;
                    var op = entry.Ops[i];
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.paddingLeft = 12;
                    row.style.paddingRight = 4;
                    row.style.paddingTop = 1;
                    row.style.paddingBottom = 1;

                    var label = new Label((opIndex + 1) + ". " + op.ShortLabel() + " " + op.ScopeLabel());
                    label.style.color = _theme.OnSurface;
                    label.style.flexGrow = 1;
                    label.style.fontSize = 11;
                    label.tooltip = op.ShortLabel() + " " + op.ScopeLabel();
                    row.Add(label);

                    var delBtn = new MD3Button("✕", MD3ButtonStyle.Text);
                    delBtn.style.width = 22;
                    delBtn.tooltip = M("この編集を取り消す");
                    var entryCapture = entry;
                    delBtn.clicked += () =>
                    {
                        entryCapture.RemoveOpAt(opIndex);
                        RebuildEditListUI();
                        UpdatePreviewStatusLabel();
                        SceneView.RepaintAll();
                    };
                    row.Add(delBtn);

                    scroll.Add(row);
                }
            }

            if (!any)
            {
                var empty = new Label(M("編集履歴はありません。"));
                empty.style.color = _theme.OnSurfaceVariant;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.paddingTop = 12;
                empty.style.paddingBottom = 12;
                scroll.Add(empty);
            }

            // Apply-all footer
            var applyAllBtn = new MD3Button(M("すべて適用"), MD3ButtonStyle.Filled);
            applyAllBtn.style.marginTop = 6;
            applyAllBtn.tooltip = M("編集リストとアクティブなタブの編集内容をディスク/MA にコミットします");
            applyAllBtn.SetEnabled(any);
            applyAllBtn.clicked += () =>
            {
                ApplyAllStaged();
                ResetAdjustmentParameters();
                RebuildTabContent();
            };
            _editListContainer.Add(applyAllBtn);
        }

        private void RebuildRendererListUI()
        {
            if (_rendererListContainer == null) return;
            _rendererListContainer.Clear();

            if (_rendererList.Count == 0)
            {
                var empty = new Label(M("アバターを選択してください。"));
                empty.style.color = _theme.OnSurfaceVariant;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.paddingTop = 16;
                empty.style.paddingBottom = 16;
                _rendererListContainer.Add(empty);
                return;
            }

            foreach (var data in _rendererList)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                if (data.renderer == _activeRenderer)
                    row.style.backgroundColor = _theme.SecondaryContainer;

                var check = new Toggle { value = data.isChecked };
                check.style.marginRight = 4;
                check.RegisterValueChangedCallback(evt => data.isChecked = evt.newValue);
                row.Add(check);

                var name = new Label(data.displayName);
                name.style.color = _theme.OnSurface;
                name.style.flexGrow = 1;
                name.tooltip = data.fullPath;
                row.Add(name);

                row.RegisterCallback<ClickEvent>(evt =>
                {
                    Selection.activeGameObject = data.renderer.gameObject;
                    SelectRenderer(data.renderer);
                });

                _rendererListContainer.Add(row);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Tab content
        // ══════════════════════════════════════════════════════════════

        private void OnTabSelected(int idx)
        {
            if (_suppressTabChanged) return;
            if (_editTab == idx) return;

            // Leaving Scene Paint → deactivate stroke session
            if (_prevEditTab == 4 && idx != 4 && ScenePaintState.IsActive)
                ScenePaintState.Deactivate();

            // Coming back to an adjustment tab → start with zeroed sliders so the
            // next slider move is a fresh op (not additive to whatever was on tab 2).
            if (idx != 4)
                ResetAdjustmentParameters();

            _editTab = idx;
            _prevEditTab = idx;
            _suppressTabChanged = true;
            try { for (int i = 0; i < _tabButtons.Count; i++) _tabButtons[i].Selected = (i == idx); }
            finally { _suppressTabChanged = false; }
            RebuildTabContent();
            RebuildEditListUI();
        }

        private void RebuildTabContent()
        {
            if (_tabContentHost == null) return;
            _tabContentHost.Clear();
            switch (_editTab)
            {
                case 0: BuildPaintTab(); break;
                case 1: BuildGradientTab(); break;
                case 2: BuildHSVTab(); break;
                case 3: BuildBrightnessContrastTab(); break;
                case 4: BuildScenePaintTab(); break;
            }
        }

        // ─── Tab 0: Paint ───
        private void BuildPaintTab()
        {
            var card = new MD3Card(M("単色ペイント"), null, MD3CardStyle.Filled);
            card.Add(BuildColorFieldRow(M("ペイント色"), _targetColor, c => { _targetColor = c; }));
            card.Add(BuildLivePreviewButtons());
            _tabContentHost.Add(card);
        }

        // ─── Tab 1: Gradient ───
        private void BuildGradientTab()
        {
            var card = new MD3Card(M("グラデーション"), null, MD3CardStyle.Filled);

            card.Add(BuildColorFieldRow(M("開始色 (From)"), _gradFrom, c => { _gradFrom = c; }));
            card.Add(BuildColorFieldRow(M("終了色 (To)"), _gradTo, c => { _gradTo = c; }));

            var dirDropdown = new MD3Dropdown(M("方向"),
                new[] { M("上→下"), M("下→上"), M("左→右"), M("右→左") }, _gradDirectionIdx,
                i => { _gradDirectionIdx = i; });
            card.Add(dirDropdown);

            var blendDropdown = new MD3Dropdown(M("ブレンドモード"),
                new[] { M("スクリーン"), M("オーバーレイ"), M("ティント"), M("乗算"), M("置換") }, _gradBlendModeIdx,
                i => { _gradBlendModeIdx = i; });
            card.Add(blendDropdown);

            card.Add(BuildSliderRow(M("範囲 開始"), _gradStartT, 0f, 1f, v =>
            {
                _gradStartT = Mathf.Min(v, _gradEndT - 0.01f);
            }));
            card.Add(BuildSliderRow(M("範囲 終了"), _gradEndT, 0f, 1f, v =>
            {
                _gradEndT = Mathf.Max(v, _gradStartT + 0.01f);
            }));

            card.Add(BuildLivePreviewButtons());
            _tabContentHost.Add(card);
        }

        // ─── Tab 2: HSV ───
        private void BuildHSVTab()
        {
            var card = new MD3Card(M("HSV 調整"), null, MD3CardStyle.Filled);
            card.Add(BuildSliderRow(M("色相シフト (Hue)"), _hueShift, -180f, 180f, v => { _hueShift = v; }));
            card.Add(BuildSliderRow(M("彩度 (Saturation)"), _satScale, 0f, 3f, v => { _satScale = v; }));
            card.Add(BuildSliderRow(M("明度 (Value)"), _valScale, 0f, 3f, v => { _valScale = v; }));

            var resetBtn = new MD3Button(M("リセット"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            resetBtn.clicked += () => { _hueShift = 0f; _satScale = 1f; _valScale = 1f; RebuildTabContent(); };
            card.Add(resetBtn);

            card.Add(BuildLivePreviewButtons());
            _tabContentHost.Add(card);
        }

        // ─── Tab 3: Brightness / Contrast ───
        private void BuildBrightnessContrastTab()
        {
            var card = new MD3Card(M("明るさ / コントラスト"), null, MD3CardStyle.Filled);
            card.Add(BuildSliderRow(M("明るさ"), _brightness, -1f, 1f, v => { _brightness = v; }));
            card.Add(BuildSliderRow(M("コントラスト"), _contrast, -1f, 1f, v => { _contrast = v; }));

            var resetBtn = new MD3Button(M("リセット"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            resetBtn.clicked += () => { _brightness = 0f; _contrast = 0f; RebuildTabContent(); };
            card.Add(resetBtn);

            card.Add(BuildLivePreviewButtons());
            _tabContentHost.Add(card);
        }

        // ─── Tab 4: Scene Paint ───
        private void BuildScenePaintTab()
        {
            var card = new MD3Card(M("Scene View ペイント"), null, MD3CardStyle.Filled);

            var toolLabels = new[] {
                M("ペイント"), M("消しゴム"), M("ぼかし"), M("指先"), M("クローン"),
                M("覆い焼き"), M("焼き込み"), M("色合い"), M("シャープン"), M("ノイズ"),
                M("彩度+"), M("彩度-") };
            // Segmented button supports up to ~5 labels comfortably; fall back to dropdown here.
            var toolDd = new MD3Dropdown(M("ツール"), toolLabels, _sceneToolIdx,
                i => { _sceneToolIdx = i; SyncBrushSettings(); });
            card.Add(toolDd);

            card.Add(BuildColorFieldRow(M("ブラシ色"), _sceneBrushColor, c => { _sceneBrushColor = c; SyncBrushSettings(); }));

            card.Add(BuildSliderRow(M("サイズ"), _sceneBrushSize, 0.001f, 0.2f, v => { _sceneBrushSize = v; SyncBrushSettings(); }));
            card.Add(BuildSliderRow(M("不透明度"), _sceneBrushOpacity, 0f, 1f, v => { _sceneBrushOpacity = v; SyncBrushSettings(); }));
            card.Add(BuildSliderRow(M("ハードネス"), _sceneBrushHardness, 0f, 1f, v => { _sceneBrushHardness = v; SyncBrushSettings(); }));

            // Blend mode / symmetry / island mask
            var blendDd = new MD3Dropdown(M("ブレンドモード"),
                new[] { M("通常"), M("乗算"), M("スクリーン"), M("オーバーレイ") }, _sceneBlendModeIdx,
                i => { _sceneBlendModeIdx = i; SyncBrushSettings(); });
            card.Add(blendDd);

            var symRow = new MD3Row(gap: 6f);
            symRow.Add(new Label(M("対称ペイント")) { style = { color = _theme.OnSurface, unityTextAlign = TextAnchor.MiddleLeft } });
            symRow.Add(new MD3Spacer());
            var symSw = new MD3Switch(_sceneSymmetry);
            symSw.changed += v => { _sceneSymmetry = v; SyncBrushSettings(); };
            symRow.Add(symSw);
            card.Add(symRow);

            var maskRow = new MD3Row(gap: 6f);
            maskRow.Add(new Label(M("アイランドマスク")) { style = { color = _theme.OnSurface, unityTextAlign = TextAnchor.MiddleLeft } });
            maskRow.Add(new MD3Spacer());
            var maskSw = new MD3Switch(_sceneIslandMask);
            maskSw.changed += v => { _sceneIslandMask = v; SyncBrushSettings(); };
            maskRow.Add(maskSw);
            card.Add(maskRow);

            // Start / Stop
            if (ScenePaintState.IsActive)
            {
                var stopBtn = new MD3Button(M("ペイント終了"), MD3ButtonStyle.Tonal);
                stopBtn.clicked += () => { ScenePaintState.Deactivate(); RebuildTabContent(); };
                card.Add(stopBtn);
            }
            else
            {
                var startBtn = new MD3Button(M("ペイント開始"), MD3ButtonStyle.Filled);
                startBtn.clicked += () => { ActivateScenePaint(); RebuildTabContent(); };
                card.Add(startBtn);
            }

            _tabContentHost.Add(card);
        }

        // ══════════════════════════════════════════════════════════════
        // Shared UI helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>Slider row with label + value badge. MD3Slider currently lacks a built-in label.</summary>
        private VisualElement BuildSliderRow(string label, float initial, float min, float max, System.Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.marginTop = 6;
            row.style.marginBottom = 6;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.flexGrow = 1;
            header.Add(lbl);

            var valueLbl = new Label(FormatFloat(initial));
            valueLbl.style.color = _theme.OnSurfaceVariant;
            valueLbl.style.fontSize = 11;
            header.Add(valueLbl);

            row.Add(header);

            var slider = new MD3Slider(initial, min, max);
            slider.changed += v => { valueLbl.text = FormatFloat(v); onChange(v); };
            row.Add(slider);

            return row;
        }

        private static string FormatFloat(float v) => v.ToString("F2");

        /// <summary>
        /// Color picker row. MD3SDK does not provide a native color picker so we wrap
        /// Unity's <c>EditorGUILayout.ColorField</c> in an IMGUIContainer.
        /// </summary>
        private VisualElement BuildColorFieldRow(string label, Color initial, System.Action<Color> onChange)
        {
            var row = new VisualElement();
            row.style.marginTop = 4;
            row.style.marginBottom = 4;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.width = 120;
            row.Add(lbl);

            Color current = initial;
            var imgui = new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                current = EditorGUILayout.ColorField(GUIContent.none, current, showEyedropper: true, showAlpha: true, hdr: false, GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck()) onChange(current);
            });
            imgui.style.flexGrow = 1;
            row.Add(imgui);

            return row;
        }

        private VisualElement BuildLivePreviewButtons()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 10;
            row.style.marginBottom = 4;

            var applyBtn = new MD3Button(M("適用"), MD3ButtonStyle.Filled);
            applyBtn.tooltip = M("現在のタブ設定を編集リストに追加し、プレビューに反映します。テクスチャはまだディスクに書き込まれません。");
            applyBtn.style.flexGrow = 1;
            applyBtn.clicked += () =>
            {
                FreezeDraftForCurrentTab();
                ResetAdjustmentParameters();
                RebuildTabContent();
                RebuildEditListUI();
                UpdatePreviewStatusLabel();
            };
            row.Add(applyBtn);

            var revertBtn = new MD3Button(M("スライダー初期化"), MD3ButtonStyle.Outlined);
            revertBtn.style.marginLeft = 6;
            revertBtn.style.width = 140;
            revertBtn.tooltip = M("現在のタブのスライダーを初期値に戻します。編集リストの内容には影響しません。");
            revertBtn.clicked += () =>
            {
                ResetAdjustmentParameters();
                RebuildTabContent();
                UpdatePreviewStatusLabel();
            };
            row.Add(revertBtn);

            return row;
        }

        private void UpdatePreviewStatusLabel()
        {
            if (_previewStatusLabel == null) return;
            var s = Session;
            int pendingOps = 0;
            foreach (var e in _sessionManager.AllEntries)
                pendingOps += e.Ops.Count;

            if (s != null && s.IsActive && s.HasUncommittedChanges)
                _previewStatusLabel.text = "● " + M("編集中") + (pendingOps > 0 ? $" (+{pendingOps})" : "");
            else if (pendingOps > 0)
                _previewStatusLabel.text = string.Format(M("未適用 {0} 件"), pendingOps);
            else if (s != null && s.IsActive)
                _previewStatusLabel.text = M("セッション中");
            else
                _previewStatusLabel.text = "";
        }

        private void UpdateSelectionSummary()
        {
            if (_selectionSummaryLabel == null) return;
            if (_selectedIslandIndices.Count == 0)
            {
                _selectionSummaryLabel.text = M("アイランド未選択 (全体対象)");
                return;
            }
            var sorted = new List<int>(_selectedIslandIndices); sorted.Sort();
            int show = Mathf.Min(8, sorted.Count);
            var head = sorted.GetRange(0, show);
            string indices = "#" + string.Join(", #", head);
            if (sorted.Count > show) indices += string.Format(" +{0}", sorted.Count - show);
            _selectionSummaryLabel.text = string.Format(M("選択中: {0} アイランド ({1})  Ctrl/Shift+Click で追加"), sorted.Count, indices);
        }

        // ══════════════════════════════════════════════════════════════
        // Renderer selection & session plumbing
        // ══════════════════════════════════════════════════════════════

        private void SelectRenderer(Renderer r) => PrepareEditor(r);

        private void PrepareEditor(Renderer r)
        {
            if (r == _activeRenderer) return;

            // Suspend the old entry's preview (if any) so the new renderer can
            // show its own. Unapplied slider values stay local to the UI — they
            // only become an op when the user presses "適用".
            var existingEntry = _sessionManager.FindEntry(r, 0);
            if (existingEntry != null)
                _sessionManager.SetActive(existingEntry);
            else if (ActiveEntry != null)
                _sessionManager.SetActive(null);

            _activeRenderer = r;
            _selectedIslandIndices.Clear();
            _uvZoom = 1f;
            _uvPan = new Vector2(0.5f, 0.5f);

            Mesh mesh = GetMesh(r);
            if (mesh != null)
                _currentIslands = UVIslandDetector.DetectIslands(mesh, _use3DConnection);

            if (_avatarRoot != null)
            {
                _currentMetadata = MetadataManager.LoadMetadata(_avatarRoot.name, r.gameObject.name);
                if (_currentMetadata == null)
                {
                    _currentMetadata = new MeshPaintMetadata();
                    Texture mainTex = r.sharedMaterial?.mainTexture;
                    if (mainTex != null)
                        _currentMetadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mainTex));
                }
            }

            // Session start is still lazy — entry is created only when the user
            // actually moves a slider (EnsureSessionStarted). This preserves the
            // "don't swap mat.mainTexture on mere selection" behavior.

            ResetAdjustmentParameters();
            RebuildRendererListUI();
            RebuildTabContent();
            RebuildEditListUI();
            UpdatePreviewStatusLabel();
            UpdateSelectionSummary();
            _uvImguiContainer?.MarkDirtyRepaint();
        }

        private void ResetAdjustmentParameters()
        {
            _hueShift = 0f; _satScale = 1f; _valScale = 1f;
            _brightness = 0f; _contrast = 0f;
            _gradStartT = 0f; _gradEndT = 1f;
        }

        // ══════════════════════════════════════════════════════════════
        // Live preview dispatch — shared with v1 via MeshPaintPreviewSession
        // ══════════════════════════════════════════════════════════════

        private List<int> GetPreviewIslandScope()
        {
            if (_selectedIslandIndices.Count == 0) return null;
            var list = new List<int>(_selectedIslandIndices); list.Sort();
            return list;
        }

        /// <summary>
        /// Lazily start the preview session on the first real edit. Returns false
        /// if there is no valid active renderer / avatar root to start against.
        /// </summary>
        private bool EnsureSessionStarted()
        {
            if (Session != null && Session.IsActive) return true;
            if (_editTab == 4) return false;
            if (_activeRenderer == null || _avatarRoot == null) return false;

            var entry = _sessionManager.GetOrCreate(_activeRenderer, _avatarRoot, 0);
            if (entry == null) return false;
            _sessionManager.SetActive(entry);

            // If the entry already has ops (user came back to this renderer),
            // rebuild its preview from BakedOrigin + ops before any live editing.
            if (entry.Ops.Count > 0)
                entry.ReplayAll();

            UpdatePreviewStatusLabel();
            return true;
        }

        /// <summary>
        /// Build an operation representing the current tab's slider state.
        /// Returns null when the tab is not an op-producing tab (e.g. Scene Paint).
        /// </summary>
        private MeshPaintOperation BuildDraftOpFromCurrentTab()
        {
            var scope = GetPreviewIslandScope();
            switch (_editTab)
            {
                case 0:
                    return new MeshPaintOperation
                    {
                        Type = MeshPaintOpType.Paint,
                        IslandScope = scope,
                        Paint = new PaintOpParams { color = _targetColor },
                    };
                case 1:
                    return new MeshPaintOperation
                    {
                        Type = MeshPaintOpType.Gradient,
                        IslandScope = scope,
                        Gradient = new GradientOpParams
                        {
                            fromColor = _gradFrom,
                            toColor = _gradTo,
                            directionIndex = _gradDirectionIdx,
                            blendModeIndex = _gradBlendModeIdx,
                            startT = _gradStartT,
                            endT = _gradEndT,
                        },
                    };
                case 2:
                    return new MeshPaintOperation
                    {
                        Type = MeshPaintOpType.HSV,
                        IslandScope = scope,
                        HSV = new HSVOpParams
                        {
                            hueShift = _hueShift,
                            satScale = _satScale,
                            valScale = _valScale,
                        },
                    };
                case 3:
                    return new MeshPaintOperation
                    {
                        Type = MeshPaintOpType.BrightnessContrast,
                        IslandScope = scope,
                        BC = new BCOpParams
                        {
                            brightness = _brightness,
                            contrast = _contrast,
                        },
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// Build an op from the current tab's sliders and push it onto the active
        /// entry's history. Because live preview is disabled, this is the only path
        /// that turns tab adjustments into actual texture changes. The op is also
        /// cloned into every checked renderer's entry so batch edits stay in sync.
        /// </summary>
        private void FreezeDraftForCurrentTab()
        {
            if (_editTab == 4) return;

            var draft = BuildDraftOpFromCurrentTab();
            if (draft == null || draft.IsNoop()) return;

            // Lazy-start the session on first apply so nothing touches the
            // material until the user actually commits an edit.
            if (!EnsureSessionStarted()) return;
            var activeEntry = ActiveEntry;
            if (activeEntry == null) return;

            activeEntry.AddOp(draft);

            // Propagate to every checked renderer except the active one.
            foreach (var other in GetTargetRenderers())
            {
                if (other == null || other == _activeRenderer) continue;
                var otherEntry = _sessionManager.GetOrCreate(other, _avatarRoot, 0);
                if (otherEntry == null) continue;
                if (otherEntry == activeEntry) continue;
                otherEntry.AddOp(draft.Clone());
            }

            RebuildEditListUI();
            UpdatePreviewStatusLabel();
        }

        /// <summary>
        /// Commit every entry that has staged ops. Destructive path writes PNGs
        /// and repoints <c>mat.mainTexture</c>; MA path creates MaterialSetter
        /// components instead (see <see cref="MeshPaintMACommitter"/>).
        /// </summary>
        private void ApplyAllStaged()
        {
            var entriesWithOps = new List<MeshPaintSessionEntry>();
            foreach (var e in _sessionManager.AllEntries)
                if (e.Ops.Count > 0) entriesWithOps.Add(e);

            if (entriesWithOps.Count == 0) return;

            bool anyFailed = false;
            int committed = 0;
            foreach (var entry in entriesWithOps)
            {
                bool ok;
                if (_useMANonDestructive)
                    ok = MeshPaintMACommitter.Apply(entry, _avatarRoot);
                else
                    ok = CommitEntryDestructive(entry);

                if (!ok) { anyFailed = true; continue; }
                entry.Ops.Clear();
                committed++;
            }

            if (anyFailed)
                EditorUtility.DisplayDialog(M("エラー"),
                    M("一部のメッシュでコミットに失敗しました。Console を確認してください。"), "OK");

            RebuildEditListUI();
            UpdatePreviewStatusLabel();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Destructive commit of a single entry: the preview texture (already
        /// reflecting BakedOrigin + all ops) is written to disk and assigned
        /// back to the material. Reuses <see cref="MeshPaintPreviewSession.Commit"/>
        /// which also moves BakedOrigin forward.
        /// </summary>
        private bool CommitEntryDestructive(MeshPaintSessionEntry entry)
        {
            if (entry == null) return false;
            var s = entry.Session;
            if (s == null || !s.IsActive) return false;

            // Session.Commit() only flushes when HasUncommittedChanges is true.
            // After ReplayAll the flag may be clear even though Ops > 0, so mark
            // the preview as dirty by forcing a tiny re-apply.
            if (!s.HasUncommittedChanges && entry.Ops.Count > 0)
                s.ApplyPreview(s.BaselinePixels);

            return s.Commit();
        }

        private List<Renderer> GetTargetRenderers()
        {
            var targets = new List<Renderer>();
            foreach (var d in _rendererList)
                if (d.isChecked && d.renderer != null) targets.Add(d.renderer);
            if (targets.Count == 0 && _activeRenderer != null) targets.Add(_activeRenderer);
            return targets;
        }

        // ══════════════════════════════════════════════════════════════
        // Scene Paint helpers (same as v1)
        // ══════════════════════════════════════════════════════════════

        private void SyncBrushSettings()
        {
            ScenePaintState.ActiveTool = (BrushTool)_sceneToolIdx;
            ScenePaintState.BrushSize = _sceneBrushSize;
            ScenePaintState.BrushOpacity = _sceneBrushOpacity;
            ScenePaintState.BrushHardness = _sceneBrushHardness;
            ScenePaintState.BrushColor = _sceneBrushColor;
            ScenePaintState.BlendModeIndex = _sceneBlendModeIdx;
            ScenePaintState.SymmetryEnabled = _sceneSymmetry;
            ScenePaintState.IslandMaskEnabled = _sceneIslandMask;

            if (_sceneIslandMask && _selectedIslandIndices.Count > 0 && _currentIslands != null)
                ScenePaintEngine.BuildIslandMask(_currentIslands, _selectedIslandIndices);
            else
                ScenePaintState.MaskedTriangles = null;
        }

        private void ActivateScenePaint()
        {
            if (_activeRenderer == null || _avatarRoot == null) return;
            SyncBrushSettings();
            ScenePaintState.Activate(_activeRenderer, _avatarRoot);
            if (_sceneIslandMask && _selectedIslandIndices.Count > 0 && _currentIslands != null)
                ScenePaintEngine.BuildIslandMask(_currentIslands, _selectedIslandIndices);
            SceneView.RepaintAll();
        }

        private void HandleColorPicked(Color c)
        {
            _sceneBrushColor = c;
            RebuildTabContent();
        }

        // ══════════════════════════════════════════════════════════════
        // UV editor (IMGUI)
        // ══════════════════════════════════════════════════════════════

        private void DrawUVEditorIMGUI()
        {
            if (_activeRenderer == null)
            {
                GUILayout.Label(M("メッシュをリストから選択してください。"));
                return;
            }
            Mesh mesh = GetMesh(_activeRenderer);
            if (mesh == null) return;

            Rect containerRect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float viewSize = Mathf.Min(containerRect.width, containerRect.height);
            Rect uvRect = new Rect(
                containerRect.x + (containerRect.width - viewSize) * 0.5f,
                containerRect.y + (containerRect.height - viewSize) * 0.5f,
                viewSize, viewSize);
            GUI.Box(uvRect, GUIContent.none);

            GUI.BeginClip(uvRect);
            Rect local = new Rect(0, 0, uvRect.width, uvRect.height);

            Texture mainTex = _activeRenderer.sharedMaterial?.mainTexture;
            if (mainTex != null)
            {
                Vector2 tl = UVToLocal(local, new Vector2(0, 1));
                Vector2 br = UVToLocal(local, new Vector2(1, 0));
                Rect texRect = Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
                GUI.DrawTexture(texRect, mainTex, ScaleMode.StretchToFill);
            }

            DrawUVWireframe(local, mesh);
            GUI.EndClip();

            HandleUVZoomPan(uvRect);
            HandleUVClick(uvRect, mesh);
        }

        private Vector2 UVToLocal(Rect rect, Vector2 uv)
        {
            float vx = uv.x;
            float vy = 1f - uv.y;
            float cx = (vx - _uvPan.x) * _uvZoom + 0.5f;
            float cy = (vy - _uvPan.y) * _uvZoom + 0.5f;
            return new Vector2(rect.x + cx * rect.width, rect.y + cy * rect.height);
        }

        private Vector2 ScreenToUV(Rect rect, Vector2 screenPos)
        {
            float cx = (screenPos.x - rect.x) / rect.width;
            float cy = (screenPos.y - rect.y) / rect.height;
            float vx = (cx - 0.5f) / _uvZoom + _uvPan.x;
            float vy = (cy - 0.5f) / _uvZoom + _uvPan.y;
            return new Vector2(vx, 1f - vy);
        }

        private void HandleUVZoomPan(Rect rect)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.ScrollWheel)
            {
                Vector2 mouseUV = ScreenToUV(rect, e.mousePosition);
                float mvx = mouseUV.x; float mvy = 1f - mouseUV.y;
                float oldZoom = _uvZoom;
                _uvZoom = Mathf.Clamp(_uvZoom * (1f + (-e.delta.y * 0.1f)), 0.5f, 20f);
                _uvPan.x = mvx - (mvx - _uvPan.x) * oldZoom / _uvZoom;
                _uvPan.y = mvy - (mvy - _uvPan.y) * oldZoom / _uvZoom;
                e.Use();
                _uvImguiContainer?.MarkDirtyRepaint();
            }
            if (e.type == EventType.MouseDrag && e.button == 2)
            {
                _uvPan.x -= e.delta.x / (rect.width * _uvZoom);
                _uvPan.y -= e.delta.y / (rect.height * _uvZoom);
                e.Use();
                _uvImguiContainer?.MarkDirtyRepaint();
            }
        }

        private void HandleUVClick(Rect rect, Mesh mesh)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;

            Vector2 clickUV = ScreenToUV(rect, e.mousePosition);
            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;

            int hit = -1;
            for (int i = 0; i < _currentIslands.Count; i++)
            {
                foreach (int triIdx in _currentIslands[i].triangleIndices)
                {
                    if (IsPointInTriangle(clickUV, uvs[tris[triIdx * 3]], uvs[tris[triIdx * 3 + 1]], uvs[tris[triIdx * 3 + 2]]))
                    {
                        hit = i; break;
                    }
                }
                if (hit != -1) break;
            }

            bool additive = e.control || e.shift || e.command;
            ApplyIslandSelection(hit, additive);
            e.Use();
            _uvImguiContainer?.MarkDirtyRepaint();
        }

        private void ApplyIslandSelection(int hitIsland, bool additive)
        {
            if (additive)
            {
                if (hitIsland < 0) return;
                if (!_selectedIslandIndices.Add(hitIsland))
                    _selectedIslandIndices.Remove(hitIsland);
            }
            else
            {
                _selectedIslandIndices.Clear();
                if (hitIsland >= 0) _selectedIslandIndices.Add(hitIsland);
            }
            UpdateSelectionSummary();
        }

        private static Material _glLineMaterial;
        private static Material GLLineMaterial
        {
            get
            {
                if (_glLineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    _glLineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _glLineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _glLineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _glLineMaterial.SetInt("_Cull", 0);
                    _glLineMaterial.SetInt("_ZWrite", 0);
                }
                return _glLineMaterial;
            }
        }

        private void DrawUVWireframe(Rect rect, Mesh mesh)
        {
            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;
            if (uvs.Length == 0) return;

            GLLineMaterial.SetPass(0);
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.6f, 0.6f, 0.6f, 0.6f));
            for (int tri = 0; tri < tris.Length / 3; tri++)
            {
                Vector2 p0 = UVToLocal(rect, uvs[tris[tri * 3]]);
                Vector2 p1 = UVToLocal(rect, uvs[tris[tri * 3 + 1]]);
                Vector2 p2 = UVToLocal(rect, uvs[tris[tri * 3 + 2]]);
                GL.Vertex3(p0.x, p0.y, 0); GL.Vertex3(p1.x, p1.y, 0);
                GL.Vertex3(p1.x, p1.y, 0); GL.Vertex3(p2.x, p2.y, 0);
                GL.Vertex3(p2.x, p2.y, 0); GL.Vertex3(p0.x, p0.y, 0);
            }
            GL.End();

            if (_selectedIslandIndices.Count > 0)
            {
                GL.Begin(GL.LINES);
                GL.Color(new Color(1f, 1f, 0f, 0.4f));
                foreach (int islandIdx in _selectedIslandIndices)
                {
                    if (islandIdx < 0 || islandIdx >= _currentIslands.Count) continue;
                    foreach (int triIdx in _currentIslands[islandIdx].triangleIndices)
                    {
                        Vector2 p0 = UVToLocal(rect, uvs[tris[triIdx * 3]]);
                        Vector2 p1 = UVToLocal(rect, uvs[tris[triIdx * 3 + 1]]);
                        Vector2 p2 = UVToLocal(rect, uvs[tris[triIdx * 3 + 2]]);
                        GL.Vertex3(p0.x, p0.y, 0); GL.Vertex3(p1.x, p1.y, 0);
                        GL.Vertex3(p1.x, p1.y, 0); GL.Vertex3(p2.x, p2.y, 0);
                        GL.Vertex3(p2.x, p2.y, 0); GL.Vertex3(p0.x, p0.y, 0);
                    }
                }
                GL.End();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Scene GUI (island highlight + click)
        // ══════════════════════════════════════════════════════════════

        private void OnSceneGUI(SceneView sceneView)
        {
            if (ScenePaintState.IsActive) return;
            if (!_isSceneSelectionEnabled || _avatarRoot == null) return;

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                // HandleUtility.PickGameObject is unreliable for avatars: it ignores
                // meshes with transparent materials (hair, clothing) and sometimes
                // returns the opaque mesh behind. Do our own ray-vs-triangle pass
                // across every Renderer under the avatar root and pick the nearest
                // real hit instead.
                Renderer bestRenderer = null;
                int bestTriIdx = -1;
                float bestDist = float.MaxValue;

                foreach (var candidate in _avatarRoot.GetComponentsInChildren<Renderer>(false))
                {
                    if (!candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
                    Mesh m = GetMesh(candidate);
                    if (m == null) continue;

                    Vector3[] verts = GetWorldVertices(candidate, m);
                    if (verts == null || verts.Length == 0) continue;

                    // Do NOT use candidate.bounds as an early-reject: for SkinnedMeshRenderers
                    // with updateWhenOffscreen=false (the VRChat default) the bounds reflect
                    // the T-pose AABB stored at import time and may not contain the live
                    // skinned vertices. Rejecting on stale bounds silently hid foreground
                    // meshes (e.g. front hair) and let the click fall through to a
                    // background mesh behind. Compute a quick AABB from the actual baked
                    // vertices and test against that instead.
                    Vector3 bbMin = verts[0], bbMax = verts[0];
                    for (int i = 1; i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];
                        if (v.x < bbMin.x) bbMin.x = v.x; else if (v.x > bbMax.x) bbMax.x = v.x;
                        if (v.y < bbMin.y) bbMin.y = v.y; else if (v.y > bbMax.y) bbMax.y = v.y;
                        if (v.z < bbMin.z) bbMin.z = v.z; else if (v.z > bbMax.z) bbMax.z = v.z;
                    }
                    var liveBounds = new Bounds((bbMin + bbMax) * 0.5f, bbMax - bbMin);
                    if (!liveBounds.IntersectRay(ray)) continue;

                    int[] tris = m.triangles;
                    for (int i = 0; i < tris.Length / 3; i++)
                    {
                        if (RayTriangleIntersection(ray,
                                verts[tris[i * 3]], verts[tris[i * 3 + 1]], verts[tris[i * 3 + 2]],
                                out float dist)
                            && dist < bestDist)
                        {
                            bestDist = dist;
                            bestTriIdx = i;
                            bestRenderer = candidate;
                        }
                    }
                }

                if (bestRenderer != null)
                {
                    bool additive = e.control || e.shift || e.command;
                    SelectBySceneClickPrecomputed(bestRenderer, bestTriIdx, additive);
                    if (_editTab == 4 && !ScenePaintState.IsActive) ActivateScenePaint();
                    e.Use();
                }
                else if (_selectedIslandIndices.Count > 0)
                {
                    // Click on empty space → deselect all
                    _selectedIslandIndices.Clear();
                    UpdateSelectionSummary();
                    _uvImguiContainer?.MarkDirtyRepaint();
                    e.Use();
                }
            }
            DrawSceneHighlight();
        }

        private void SelectBySceneClickPrecomputed(Renderer r, int hitTriIdx, bool additive)
        {
            PrepareEditor(r);
            int hitIsland = -1;
            if (hitTriIdx != -1 && _currentIslands != null)
                hitIsland = _currentIslands.FindIndex(isl => isl.triangleIndices.Contains(hitTriIdx));
            ApplyIslandSelection(hitIsland, additive);
            _uvImguiContainer?.MarkDirtyRepaint();
        }

        private void SelectBySceneClick(Renderer r, Ray ray, bool additive)
        {
            PrepareEditor(r);
            Mesh mesh = GetMesh(r);
            if (mesh == null) return;

            Vector3[] verts = GetWorldVertices(r, mesh);
            int[] tris = mesh.triangles;

            float minDist = float.MaxValue;
            int hitTriIdx = -1;
            for (int i = 0; i < tris.Length / 3; i++)
            {
                if (RayTriangleIntersection(ray, verts[tris[i * 3]], verts[tris[i * 3 + 1]], verts[tris[i * 3 + 2]], out float dist) && dist < minDist)
                {
                    minDist = dist; hitTriIdx = i;
                }
            }

            int hitIsland = -1;
            if (hitTriIdx != -1 && _currentIslands != null)
                hitIsland = _currentIslands.FindIndex(isl => isl.triangleIndices.Contains(hitTriIdx));
            ApplyIslandSelection(hitIsland, additive);
            _uvImguiContainer?.MarkDirtyRepaint();
        }

        private void DrawSceneHighlight()
        {
            if (_activeRenderer == null || _currentIslands == null || _currentIslands.Count == 0) return;
            Mesh mesh = GetMesh(_activeRenderer);
            if (mesh == null) return;

            Vector3[] verts = GetWorldVertices(_activeRenderer, mesh);
            int[] tris = mesh.triangles;

            if (_selectedIslandIndices.Count > 0)
            {
                Handles.color = new Color(1f, 1f, 0f, 0.35f);
                foreach (int islandIdx in _selectedIslandIndices)
                {
                    if (islandIdx < 0 || islandIdx >= _currentIslands.Count) continue;
                    foreach (int triIdx in _currentIslands[islandIdx].triangleIndices)
                    {
                        Vector3 v0 = verts[tris[triIdx * 3]], v1 = verts[tris[triIdx * 3 + 1]], v2 = verts[tris[triIdx * 3 + 2]];
                        Handles.DrawPolyLine(v0, v1, v2, v0);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Small utilities (duplicated from v1 — intentional to keep v2 standalone)
        // ══════════════════════════════════════════════════════════════

        private static GameObject AutoDetectAvatarRoot(GameObject go)
        {
            if (go == null) return null;
            Transform t = go.transform;
            while (t != null)
            {
                if (t.GetComponent<Animator>() != null) return t.gameObject;
                if (t.parent == null) return t.gameObject;
                t = t.parent;
            }
            return null;
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            string path = target.name;
            Transform cur = target.parent;
            while (cur != null && cur != root) { path = cur.name + "/" + path; cur = cur.parent; }
            return path;
        }

        private static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (r is MeshRenderer) return r.GetComponent<MeshFilter>()?.sharedMesh;
            return null;
        }

        private static Vector3[] GetWorldVertices(Renderer r, Mesh mesh)
        {
            if (r is SkinnedMeshRenderer smr)
            {
                // IMPORTANT: SkinnedMeshRenderer.BakeMesh(mesh) returns vertices in a
                // space that *already includes* the SMR transform's scale — despite
                // the Unity docs claiming "local scale excluded". Applying the full
                // localToWorldMatrix (which also multiplies by scale) would scale
                // twice, shoving the mesh away from where it actually renders. For
                // avatars with non-identity SMR scale (e.g. raw Blender FBX exports
                // with 270° X rotation + ~1.37 scale), this made scene-click rays
                // miss foreground meshes and hit background ones instead.
                // Apply only position + rotation to match the rendered geometry.
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var locals = baked.vertices;
                var world = new Vector3[locals.Length];
                var pos = smr.transform.position;
                var rot = smr.transform.rotation;
                for (int i = 0; i < locals.Length; i++) world[i] = pos + rot * locals[i];
                Object.DestroyImmediate(baked);
                return world;
            }
            else
            {
                var locals = mesh.vertices;
                var world = new Vector3[locals.Length];
                var ltw = r.transform.localToWorldMatrix;
                for (int i = 0; i < locals.Length; i++) world[i] = ltw.MultiplyPoint3x4(locals[i]);
                return world;
            }
        }

        private static bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            distance = 0;
            Vector3 e1 = v1 - v0, e2 = v2 - v0;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-6f) return false;
            float invDet = 1f / det;
            Vector3 tvec = ray.origin - v0;
            float u = Vector3.Dot(tvec, p) * invDet;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(ray.direction, q) * invDet;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * invDet;
            return distance > 1e-5f;
        }

        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
            if ((s < 0) != (t < 0)) return false;
            float area = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
            if (area < 0) { s = -s; t = -t; area = -area; }
            return s > 0 && t > 0 && (s + t) <= area;
        }

    }
}
