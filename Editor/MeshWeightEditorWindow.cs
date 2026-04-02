using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.Fitting;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Interactive editor window for mesh vertex and bone weight editing.
    /// Supports multi-renderer editing, BlendShape export, and vertex display settings.
    /// </summary>
    public class MeshWeightEditorWindow : EditorWindow
    {
        // ─── SmrEntry ───

        private class SmrEntry
        {
            public SkinnedMeshRenderer smr;
            public MeshFilter mf;
            public bool enabled = true;
            public int vertexCount;
            public Mesh originalMesh;
            public Mesh editMesh;
            public Vector3[] originalVertices;
            // BakeMesh cache
            public Vector3[] cachedWorldVerts;
            public int[] cachedTriangles;
            public Vector3[] cachedNormals;
            public int cacheFrame;
            public int[] sampledIndices;

            public Mesh SharedMesh
            {
                get => smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                set { if (smr != null) smr.sharedMesh = value; else if (mf != null) mf.sharedMesh = value; }
            }

            public bool IsSkinned => smr != null;
            public GameObject Go => smr != null ? smr.gameObject : mf != null ? mf.gameObject : null;
            public Transform Transform => smr != null ? smr.transform : mf != null ? mf.transform : null;
            public string Name => smr != null ? smr.name : mf != null ? mf.name : "?";
        }

        private class DragEntryState
        {
            public int entryIdx;
            public List<MeshEditTools.VertexSelection> selection;
            public Vector3[] baseVerts;
            public Matrix4x4[] skinMatrices;
            public BoneWeight[] boneWeights;
            public Matrix4x4 invTRS;
        }

        // ─── Multi-renderer ───
        private GameObject _rootObject;
        private List<SmrEntry> _smrEntries = new List<SmrEntry>();
        private int _activeSmrIndex = -1;
        private Vector2 _smrListScroll;

        // ─── Mode ───
        private int _mainTab;
        private static string[] MainTabLabels => new[] { M("メッシュ編集"), M("ウエイト編集") };

        // ─── Brush ───
        private bool _brushActive;
        private Vector3 _brushCenter;
        private float _brushRadius = 0.02f;
        private float _brushFalloff = 1f;
        private float _brushStrength = 0.5f;
        private bool _brushValid;

        // ─── Mesh edit params ───
        private int _meshOp;
        private static string[] MeshOpLabels => new[] { M("ドラッグ移動"), M("選択移動"), M("スケール"), M("スムーズ") };
        private Vector3 _moveDelta;
        private Vector3 _scaleAmount = Vector3.one;
        private int _smoothIterations = 3;
        private float _smoothStrength = 0.5f;

        // ─── Selection-Move state ───
        private List<MeshEditTools.VertexSelection> _persistentSelection = new List<MeshEditTools.VertexSelection>();
        private HashSet<int> _persistentSelectionIndices = new HashSet<int>();
        private Vector3 _selectionCenter;
        private Vector3 _handlePosition;
        private bool _hasSelection;
        private bool _selectionHandleMoved;
        private Mesh _selMesh;
        private Mesh _selOriginalMesh;
        private Vector3[] _selBaseVertices;
        private Matrix4x4[] _selSkinMatrices;
        private BoneWeight[] _selBoneWeights;
        private Matrix4x4 _selInvTRS;
        private bool _selMeshCloned;

        // ─── Interactive drag state (multi-SMR) ───
        private bool _isDragging;
        private Vector2 _dragStartScreen;
        private Vector3 _dragStartWorld;
        private float _dragDepth;
        private Vector3 _dragHitNormal;
        private int _dragMoveAxis;
        private List<DragEntryState> _multiDragSelections;

        // ─── Vertex Display ───
        private bool _showOccluded;
        private Color _vertexColor = new Color(0f, 0.8f, 1f, 0.8f);
        private int _samplingTarget = 5000;
        private float _vertexDrawDistance = 5f;
        private float _vertexDrawSize = 0.0015f;

        // ─── BlendShape save ───
        private string _blendShapeName = "New_BlendShape";

        // ─── Preview throttle ───
        private int _previewEveryNFrames = 1;
        private int _previewFrameCounter;

        // ─── Weight edit params ───
        private int _weightOp;
        private static string[] WeightOpLabels => new[] { M("ウエイト設定"), M("ウエイト転送"), M("ウエイトスムーズ"), M("正規化") };
        private int _selectedBoneIdx;
        private float _targetWeight = 1f;
        private int _fromBoneIdx;
        private int _toBoneIdx;
        private float _transferAmount = 1f;
        private int _weightSmoothIterations = 3;

        // ─── Visualization ───
        private bool _showWeightColors = true;
        private int _visualBoneIdx;
        private float[] _cachedInfluence;
        private Vector3[] _cachedWorldVerts;

        // ─── Bone names ───
        private string[] _boneNames = new string[0];

        // ─── UI ───
        private Vector2 _scrollPos;

        private static string GeneratedDir => PackagePaths.GetGeneratedDir("MeshEdit");

        // ─── Properties ───

        private SmrEntry ActiveEntry => _activeSmrIndex >= 0 && _activeSmrIndex < _smrEntries.Count
            ? _smrEntries[_activeSmrIndex] : null;

        private SkinnedMeshRenderer ActiveSmr => ActiveEntry?.smr;

        private GameObject ActiveGameObject => ActiveEntry?.Go;

        private bool HasAnyEditMesh => _smrEntries.Any(e => e.editMesh != null);

        // ════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════

        [MenuItem("Window/紫陽花広場/Mesh & Weight Editor")]
        public static void ShowWindow()
        {
            if (UpdateChecker.IsBlocked)
            {
                EditorUtility.DisplayDialog(M("バージョン期限切れ"),
                    UpdateChecker.IsExpired
                        ? M("このバージョンは期限切れです。最新バージョンを BOOTH からダウンロードしてください。")
                        : M("ライセンス認証に失敗しました。インターネット接続を確認し、Unity を再起動してください。"),
                    "OK");
                return;
            }
            GetWindow<MeshWeightEditorWindow>(M("メッシュ＆ウェイトエディタ"));
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            RestoreAll();
            _brushActive = false;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;
            var go = Selection.activeGameObject;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            var mf = go.GetComponent<MeshFilter>();
            if (smr != null || mf != null)
            {
                var root = FindAvatarRoot(go);
                SetRootObject(root);
                // Auto-select the clicked SMR
                for (int i = 0; i < _smrEntries.Count; i++)
                {
                    if (_smrEntries[i].Go == go)
                    {
                        SetActiveSmrIndex(i);
                        break;
                    }
                }
                Repaint();
            }
        }

        // ════════════════════════════════════════
        // Root / SMR Management
        // ════════════════════════════════════════

        private static GameObject FindAvatarRoot(GameObject go)
        {
            // Search upward for VRCAvatarDescriptor
            Transform t = go.transform;
            while (t != null)
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c != null && c.GetType().Name == "VRCAvatarDescriptor")
                        return t.gameObject;
                }
                t = t.parent;
            }
            // Fallback: first Animator going up
            t = go.transform;
            while (t != null)
            {
                if (t.GetComponent<Animator>() != null)
                    return t.gameObject;
                t = t.parent;
            }
            // Last resort: hierarchy root
            t = go.transform;
            while (t.parent != null) t = t.parent;
            return t.gameObject;
        }

        private void SetRootObject(GameObject go)
        {
            if (_isDragging) CancelDrag();
            ClearPersistentSelection();
            _rootObject = go;
            RefreshSmrEntries();
        }

        private void SetActiveSmrIndex(int idx)
        {
            if (idx == _activeSmrIndex) return;
            if (_isDragging) CancelDrag();
            ClearPersistentSelection();
            _activeSmrIndex = idx;
            UpdateBoneNames();
            _cachedInfluence = null;
            _cachedWorldVerts = null;
        }

        private void RefreshSmrEntries()
        {
            if (_isDragging) CancelDrag();
            ClearPersistentSelection();
            _smrEntries.Clear();
            _activeSmrIndex = -1;
            if (_rootObject == null) return;

            var smrs = _rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                _smrEntries.Add(new SmrEntry
                {
                    smr = smr,
                    enabled = true,
                    vertexCount = smr.sharedMesh.vertexCount,
                    originalMesh = smr.sharedMesh,
                });
            }

            // MeshFilter fallback if no SMRs
            if (_smrEntries.Count == 0)
            {
                var mfs = _rootObject.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in mfs)
                {
                    if (mf.sharedMesh == null) continue;
                    _smrEntries.Add(new SmrEntry
                    {
                        mf = mf,
                        enabled = true,
                        vertexCount = mf.sharedMesh.vertexCount,
                        originalMesh = mf.sharedMesh,
                    });
                }
            }

            if (_smrEntries.Count > 0)
                _activeSmrIndex = 0;

            UpdateBoneNames();
            RebuildSampling();
            _cachedInfluence = null;
            _cachedWorldVerts = null;
        }

        private void UpdateBoneNames()
        {
            var smr = ActiveSmr;
            if (smr != null && smr.bones != null)
            {
                _boneNames = smr.bones
                    .Select(b => b != null ? b.name : "(null)")
                    .ToArray();
            }
            else
            {
                _boneNames = new string[0];
            }
        }

        private void RestoreAll()
        {
            CancelDrag();
            ClearPersistentSelection();

            foreach (var entry in _smrEntries)
            {
                if (entry.editMesh != null)
                {
                    entry.SharedMesh = entry.originalMesh;
                    Object.DestroyImmediate(entry.editMesh);
                    entry.editMesh = null;
                    entry.originalVertices = null;
                }
            }
        }

        // ════════════════════════════════════════
        // Sampling & BakeMesh Cache
        // ════════════════════════════════════════

        private void RebuildSampling()
        {
            int enabledCount = _smrEntries.Count(e => e.enabled);
            if (enabledCount == 0) return;
            int perEntry = _samplingTarget / enabledCount;

            foreach (var entry in _smrEntries)
            {
                if (!entry.enabled)
                {
                    entry.sampledIndices = null;
                    continue;
                }
                int count = entry.vertexCount;
                if (count <= perEntry)
                {
                    entry.sampledIndices = Enumerable.Range(0, count).ToArray();
                }
                else
                {
                    int stride = Mathf.Max(1, count / perEntry);
                    var list = new List<int>();
                    for (int i = 0; i < count; i += stride)
                        list.Add(i);
                    entry.sampledIndices = list.ToArray();
                }
            }
        }

        private void EnsureBakeCache(SmrEntry entry)
        {
            int frame = Time.frameCount;
            if (entry.cacheFrame == frame && entry.cachedWorldVerts != null) return;

            if (entry.smr != null)
            {
                var baked = new Mesh();
                entry.smr.BakeMesh(baked);
                entry.cachedWorldVerts = baked.vertices;
                entry.cachedTriangles = baked.triangles;
                entry.cachedNormals = baked.normals;
                var xform = entry.smr.transform;
                for (int i = 0; i < entry.cachedWorldVerts.Length; i++)
                    entry.cachedWorldVerts[i] = xform.TransformPoint(entry.cachedWorldVerts[i]);
                Object.DestroyImmediate(baked);
            }
            else if (entry.mf != null)
            {
                var mesh = entry.mf.sharedMesh;
                if (mesh == null) return;
                entry.cachedWorldVerts = mesh.vertices;
                entry.cachedTriangles = mesh.triangles;
                entry.cachedNormals = mesh.normals;
                var ltw = entry.mf.transform.localToWorldMatrix;
                for (int i = 0; i < entry.cachedWorldVerts.Length; i++)
                    entry.cachedWorldVerts[i] = ltw.MultiplyPoint3x4(entry.cachedWorldVerts[i]);
            }

            entry.cacheFrame = frame;
        }

        // ════════════════════════════════════════
        // GUI
        // ════════════════════════════════════════

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 8, 8) });
            GUILayout.Label(M("メッシュ＆ウェイトエディタ"), EditorStyles.boldLabel);

            // 1. Root Object + SMR list
            DrawRootAndSmrList();

            if (_rootObject == null || _smrEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(M("メッシュを持つオブジェクトを選択してください。\nヒエラルキーで選択すると自動検出されます。"), MessageType.Warning);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(4);

            // 2. Vertex Display settings
            DrawVertexDisplaySettings();

            EditorGUILayout.Space(4);

            // 3. Brush settings
            DrawBrushSettings();

            EditorGUILayout.Space(4);

            // 4. Tab
            _mainTab = GUILayout.Toolbar(_mainTab, MainTabLabels);
            EditorGUILayout.Space(4);

            switch (_mainTab)
            {
                case 0: DrawMeshEditTab(); break;
                case 1: DrawWeightEditTab(); break;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        // ─── Root & SMR List ───

        private void DrawRootAndSmrList()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            _rootObject = (GameObject)EditorGUILayout.ObjectField(M("ルートオブジェクト"), _rootObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && _rootObject != null)
                SetRootObject(_rootObject);

            if (GUILayout.Button(M("更新"), GUILayout.Width(50)))
                RefreshSmrEntries();

            if (HasAnyEditMesh)
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.6f, 0.3f);
                if (GUILayout.Button(M("復元"), GUILayout.Width(50)))
                    RestoreAll();
                GUI.backgroundColor = prevBg;
            }

            EditorGUILayout.EndHorizontal();

            // Show SMR list only when multiple entries exist
            if (_smrEntries.Count > 1)
            {
                float listHeight = Mathf.Min(_smrEntries.Count * 22f, 120f);
                _smrListScroll = EditorGUILayout.BeginScrollView(_smrListScroll, GUILayout.MaxHeight(listHeight));

                for (int i = 0; i < _smrEntries.Count; i++)
                {
                    var entry = _smrEntries[i];
                    bool isActive = i == _activeSmrIndex;

                    EditorGUILayout.BeginHorizontal();

                    EditorGUI.BeginChangeCheck();
                    entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck())
                        RebuildSampling();

                    Color prevBg2 = GUI.backgroundColor;
                    if (isActive) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
                    string label = entry.Name;
                    if (entry.editMesh != null) label += " *";
                    if (GUILayout.Button(label, EditorStyles.miniButton))
                        SetActiveSmrIndex(i);
                    GUI.backgroundColor = prevBg2;

                    EditorGUILayout.LabelField(string.Format("v:{0}", entry.vertexCount),
                        EditorStyles.miniLabel, GUILayout.Width(70));

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // ─── Vertex Display Settings ───

        private void DrawVertexDisplaySettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("頂点表示設定"), EditorStyles.boldLabel);

            _showOccluded = EditorGUILayout.Toggle(M("X-Ray (裏面も表示)"), _showOccluded);
            _vertexColor = EditorGUILayout.ColorField(M("頂点カラー"), _vertexColor);

            EditorGUI.BeginChangeCheck();
            _samplingTarget = EditorGUILayout.IntSlider(M("表示上限"), _samplingTarget, 500, 50000);
            if (EditorGUI.EndChangeCheck())
                RebuildSampling();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(M("サンプリング再構築"), GUILayout.Width(140)))
                RebuildSampling();
            EditorGUILayout.EndHorizontal();

            _vertexDrawDistance = EditorGUILayout.Slider(M("描画距離"), _vertexDrawDistance, 0.5f, 20f);
            _vertexDrawSize = EditorGUILayout.Slider(M("頂点サイズ"), _vertexDrawSize, 0.0005f, 0.01f);

            EditorGUILayout.EndVertical();
        }

        // ─── Brush Settings ───

        private void DrawBrushSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("ブラシ設定"), EditorStyles.boldLabel);

            _brushRadius = EditorGUILayout.Slider(M("半径 (m)"), _brushRadius, 0.001f, 0.5f);
            _brushFalloff = EditorGUILayout.Slider(M("フォールオフ"), _brushFalloff, 0f, 1f);
            _brushStrength = EditorGUILayout.Slider(M("強度"), _brushStrength, 0.01f, 1f);

            EditorGUILayout.Space(2);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _brushActive ? new Color(1f, 0.5f, 0.3f) : new Color(0.3f, 0.8f, 0.3f);
            string buttonLabel = _brushActive ? M("ブラシ解除 (Esc)") : M("ブラシ有効化");
            if (GUILayout.Button(buttonLabel, GUILayout.Height(26)))
            {
                _brushActive = !_brushActive;
                if (!_brushActive)
                {
                    _brushValid = false;
                    CancelDrag();
                }
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = prevBg;

            if (_brushValid)
            {
                EditorGUILayout.LabelField(
                    string.Format(M("ブラシ位置: ({0:F4}, {1:F4}, {2:F4})"), _brushCenter.x, _brushCenter.y, _brushCenter.z),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        // ─── Mesh Edit Tab ───

        private void DrawMeshEditTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _meshOp = GUILayout.SelectionGrid(_meshOp, MeshOpLabels, 4);
            EditorGUILayout.Space(4);

            switch (_meshOp)
            {
                case 0: // Drag Move
                    EditorGUILayout.HelpBox(
                        M("ブラシを有効にしてScene Viewでドラッグすると頂点を移動できます。\n" +
                          "Shift+ドラッグ: 法線方向に移動\n" +
                          "Alt+ドラッグ: カメラ操作（通常通り）\n" +
                          "スクロール: ブラシサイズ変更"),
                        MessageType.Info);

                    _moveDelta = EditorGUILayout.Vector3Field(M("手動移動量 (ワールド空間)"), _moveDelta);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("手動移動実行")))
                        ExecuteMeshMove();
                    break;

                case 1: // Select & Move
                    EditorGUILayout.HelpBox(
                        M("ブラシを有効にしてクリックで頂点を選択します。\n" +
                          "Shift+クリック: 選択から除外\n" +
                          "選択後、Scene View上のハンドルをドラッグして移動できます。"),
                        MessageType.Info);

                    if (_hasSelection)
                    {
                        EditorGUILayout.LabelField(
                            string.Format(M("選択頂点数: {0}"), _persistentSelection.Count),
                            EditorStyles.boldLabel);

                        EditorGUILayout.Space(2);
                        Color prevConfirm = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f);
                        if (GUILayout.Button(M("移動を確定"), GUILayout.Height(26)))
                            ConfirmSelectionMove();
                        GUI.backgroundColor = prevConfirm;

                        EditorGUILayout.Space(2);
                        if (GUILayout.Button(M("選択クリア")))
                            ClearPersistentSelection();
                    }
                    else
                    {
                        EditorGUILayout.LabelField(M("頂点が選択されていません。"), EditorStyles.miniLabel);
                    }
                    break;

                case 2: // Scale
                    _scaleAmount = EditorGUILayout.Vector3Field(M("スケール倍率"), _scaleAmount);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("スケール実行")))
                        ExecuteMeshScale();
                    break;

                case 3: // Smooth
                    _smoothIterations = EditorGUILayout.IntSlider(M("反復回数"), _smoothIterations, 1, 20);
                    _smoothStrength = EditorGUILayout.Slider(M("強度"), _smoothStrength, 0.1f, 1f);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("スムーズ実行")))
                        ExecuteMeshSmooth();
                    break;
            }

            EditorGUILayout.EndVertical();

            // ─── Preview throttle ───
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("プレビュー"), EditorStyles.boldLabel);
            _previewEveryNFrames = EditorGUILayout.IntSlider(M("更新間隔 (フレーム)"), _previewEveryNFrames, 1, 30);
            EditorGUILayout.EndVertical();

            // ─── Save / Bake ───
            EditorGUILayout.Space(4);
            DrawSaveBakeSection();
        }

        // ─── Save / Bake Section ───

        private void DrawSaveBakeSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("保存"), EditorStyles.boldLabel);

            _blendShapeName = EditorGUILayout.TextField(M("BlendShape名"), _blendShapeName);

            EditorGUILayout.Space(4);

            bool hasEdits = HasAnyEditMesh;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(!hasEdits);
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.6f);
            if (GUILayout.Button(M("BlendShapeとして保存"), GUILayout.Height(28)))
                SaveAsBlendShape();
            GUI.backgroundColor = prevBg;

            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button(M("メッシュに適用"), GUILayout.Height(28)))
                ApplyToMeshAsset();
            GUI.backgroundColor = prevBg;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (!hasEdits)
                EditorGUILayout.HelpBox(M("ドラッグ移動や選択移動で頂点を編集すると保存できます。"), MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        // ─── Weight Edit Tab ───

        private void DrawWeightEditTab()
        {
            var smr = ActiveSmr;
            if (smr == null)
            {
                EditorGUILayout.HelpBox(M("SkinnedMeshRendererが必要です。"), MessageType.Warning);
                return;
            }

            // Visualization
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("ウエイト表示"), EditorStyles.boldLabel);
            _showWeightColors = EditorGUILayout.Toggle(M("ウエイトカラー表示"), _showWeightColors);
            if (_showWeightColors && _boneNames.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                _visualBoneIdx = EditorGUILayout.Popup(M("表示ボーン"), _visualBoneIdx, _boneNames);
                if (EditorGUI.EndChangeCheck())
                    UpdateWeightVisualization();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _weightOp = GUILayout.SelectionGrid(_weightOp, WeightOpLabels, 2);
            EditorGUILayout.Space(4);

            switch (_weightOp)
            {
                case 0: // Set weight
                    if (_boneNames.Length > 0)
                        _selectedBoneIdx = EditorGUILayout.Popup(M("ボーン"), _selectedBoneIdx, _boneNames);
                    _targetWeight = EditorGUILayout.Slider(M("ウエイト値"), _targetWeight, 0f, 1f);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("ウエイト設定実行")))
                        ExecuteSetWeight();
                    break;

                case 1: // Transfer
                    if (_boneNames.Length > 0)
                    {
                        _fromBoneIdx = EditorGUILayout.Popup(M("移動元ボーン"), _fromBoneIdx, _boneNames);
                        _toBoneIdx = EditorGUILayout.Popup(M("移動先ボーン"), _toBoneIdx, _boneNames);
                    }
                    _transferAmount = EditorGUILayout.Slider(M("移動量"), _transferAmount, 0f, 1f);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("ウエイト転送実行")))
                        ExecuteTransferWeight();
                    break;

                case 2: // Smooth
                    _weightSmoothIterations = EditorGUILayout.IntSlider(M("反復回数"), _weightSmoothIterations, 1, 20);
                    if (_boneNames.Length > 0)
                        _selectedBoneIdx = EditorGUILayout.Popup(M("対象ボーン (任意)"), _selectedBoneIdx, _boneNames);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("ウエイトスムーズ実行")))
                        ExecuteSmoothWeights();
                    break;

                case 3: // Normalize
                    EditorGUILayout.HelpBox(M("全頂点のウエイト合計を1.0に正規化します。"), MessageType.Info);
                    EditorGUILayout.Space(4);
                    if (DrawExecuteButton(M("正規化実行")))
                        ExecuteNormalize();
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private bool DrawExecuteButton(string label)
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            bool result = GUILayout.Button(label, GUILayout.Height(28));
            GUI.backgroundColor = prevBg;
            return result;
        }

        // ════════════════════════════════════════
        // Interactive Drag (multi-SMR)
        // ════════════════════════════════════════

        private void BeginDrag(SceneView sceneView, Vector3 hitPoint, Vector3 hitNormal, Vector2 mousePos)
        {
            if (_rootObject == null || _smrEntries.Count == 0) return;

            _multiDragSelections = new List<DragEntryState>();

            for (int ei = 0; ei < _smrEntries.Count; ei++)
            {
                var entry = _smrEntries[ei];
                if (!entry.enabled) continue;

                EnsureBakeCache(entry);
                if (entry.cachedWorldVerts == null) continue;

                // Select vertices within brush radius
                var selection = SelectVerticesFromCache(entry.cachedWorldVerts, hitPoint, _brushRadius, _brushFalloff);
                if (selection.Count == 0) continue;

                // Clone editMesh if first time editing this entry
                if (entry.editMesh == null)
                {
                    entry.originalVertices = entry.originalMesh.vertices;
                    entry.editMesh = Object.Instantiate(entry.originalMesh);
                    entry.editMesh.name = entry.originalMesh.name + "_editing";
                    Undo.RecordObject(entry.smr != null ? (Object)entry.smr : entry.mf, "MeshEdit: DragMove");
                    entry.SharedMesh = entry.editMesh;
                }

                // Build drag state for this entry
                var dragState = new DragEntryState
                {
                    entryIdx = ei,
                    selection = selection,
                    baseVerts = entry.editMesh.vertices,
                };

                if (entry.IsSkinned && entry.smr.bones != null && entry.editMesh.bindposes != null)
                {
                    dragState.skinMatrices = FittingHelpers.PrecomputeSkinMatrices(entry.smr.bones, entry.editMesh.bindposes);
                    dragState.boneWeights = entry.editMesh.boneWeights;
                }
                else
                {
                    dragState.invTRS = entry.Transform.worldToLocalMatrix;
                }

                _multiDragSelections.Add(dragState);
            }

            if (_multiDragSelections.Count == 0)
            {
                _multiDragSelections = null;
                return;
            }

            _dragStartScreen = mousePos;
            _dragStartWorld = hitPoint;
            _dragHitNormal = hitNormal;
            _dragDepth = sceneView.camera.WorldToScreenPoint(hitPoint).z;
            _isDragging = true;
            _previewFrameCounter = 0;
        }

        private void UpdateDrag(SceneView sceneView, Vector2 mousePos, bool shiftHeld)
        {
            if (!_isDragging || _multiDragSelections == null) return;

            // Throttle preview
            _previewFrameCounter++;
            if (_previewEveryNFrames > 1 && _previewFrameCounter % _previewEveryNFrames != 0) return;

            // Compute world-space delta
            Vector3 deltaWorld;
            if (shiftHeld)
            {
                float screenDelta = (mousePos.y - _dragStartScreen.y) * -0.002f;
                float handleSize = HandleUtility.GetHandleSize(_dragStartWorld);
                deltaWorld = _dragHitNormal * screenDelta * handleSize;
            }
            else
            {
                Camera cam = sceneView.camera;
                Vector3 startScreen = cam.WorldToScreenPoint(_dragStartWorld);
                Vector3 curScreen = new Vector3(mousePos.x, cam.pixelHeight - mousePos.y, startScreen.z);
                Vector3 startScreenFlipped = new Vector3(_dragStartScreen.x, cam.pixelHeight - _dragStartScreen.y, startScreen.z);
                Vector3 curWorld = cam.ScreenToWorldPoint(curScreen);
                Vector3 startWorld = cam.ScreenToWorldPoint(startScreenFlipped);
                deltaWorld = curWorld - startWorld;
            }

            // Apply strength
            deltaWorld *= _brushStrength;

            // Apply to each drag entry
            foreach (var ds in _multiDragSelections)
            {
                var entry = _smrEntries[ds.entryIdx];
                if (entry.editMesh == null) continue;

                var newVerts = (Vector3[])ds.baseVerts.Clone();

                if (ds.skinMatrices != null && ds.boneWeights != null)
                {
                    foreach (var sel in ds.selection)
                    {
                        var meshDelta = FittingHelpers.WorldToMeshDelta(
                            ds.skinMatrices, ds.boneWeights[sel.index], deltaWorld * sel.weight);
                        newVerts[sel.index] += meshDelta;
                    }
                }
                else
                {
                    foreach (var sel in ds.selection)
                    {
                        newVerts[sel.index] += ds.invTRS.MultiplyVector(deltaWorld * sel.weight);
                    }
                }

                entry.editMesh.vertices = newVerts;
                entry.editMesh.RecalculateNormals();
                entry.editMesh.RecalculateBounds();
            }
        }

        private void EndDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;

            int totalVerts = 0;
            if (_multiDragSelections != null)
            {
                foreach (var ds in _multiDragSelections)
                    totalVerts += ds.selection.Count;
            }
            Debug.Log(string.Format("[MeshWeightEditor] DragMove: {0} vertices moved across {1} meshes.",
                totalVerts, _multiDragSelections != null ? _multiDragSelections.Count : 0));

            _multiDragSelections = null;
        }

        private void CancelDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;

            // Restore base vertices for each affected entry
            if (_multiDragSelections != null)
            {
                foreach (var ds in _multiDragSelections)
                {
                    var entry = _smrEntries[ds.entryIdx];
                    if (entry.editMesh != null)
                    {
                        entry.editMesh.vertices = ds.baseVerts;
                        entry.editMesh.RecalculateNormals();
                        entry.editMesh.RecalculateBounds();
                    }
                }
            }

            _multiDragSelections = null;
        }

        // ════════════════════════════════════════
        // Selection-Move (single active entry)
        // ════════════════════════════════════════

        private void AddToSelection(Vector3 hitPoint)
        {
            var go = ActiveGameObject;
            if (go == null) return;

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx)) return;

            var hits = MeshEditTools.SelectBySphere(ctx, hitPoint, _brushRadius, _brushFalloff);
            if (hits.Count == 0) return;

            foreach (var h in hits)
            {
                if (_persistentSelectionIndices.Contains(h.index))
                {
                    for (int i = 0; i < _persistentSelection.Count; i++)
                    {
                        if (_persistentSelection[i].index == h.index && _persistentSelection[i].weight < h.weight)
                        {
                            _persistentSelection[i] = h;
                            break;
                        }
                    }
                }
                else
                {
                    _persistentSelection.Add(h);
                    _persistentSelectionIndices.Add(h.index);
                }
            }

            UpdateSelectionCenter(ctx);
        }

        private void RemoveFromSelection(Vector3 hitPoint)
        {
            var go = ActiveGameObject;
            if (go == null) return;

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx)) return;

            var hits = MeshEditTools.SelectBySphere(ctx, hitPoint, _brushRadius, 0f);
            var removeSet = new HashSet<int>();
            foreach (var h in hits)
                removeSet.Add(h.index);

            _persistentSelection.RemoveAll(s => removeSet.Contains(s.index));
            _persistentSelectionIndices.ExceptWith(removeSet);

            if (_persistentSelection.Count > 0)
                UpdateSelectionCenter(ctx);
            else
                _hasSelection = false;
        }

        private void UpdateSelectionCenter(MeshEditTools.MeshContext ctx)
        {
            if (_persistentSelection.Count == 0)
            {
                _hasSelection = false;
                return;
            }

            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;
            foreach (var sel in _persistentSelection)
            {
                sum += ctx.worldVertices[sel.index] * sel.weight;
                totalWeight += sel.weight;
            }

            _selectionCenter = totalWeight > 0f ? sum / totalWeight : sum / _persistentSelection.Count;
            _handlePosition = _selectionCenter;
            _hasSelection = true;
            _selectionHandleMoved = false;

            DiscardSelectionMeshClone();
        }

        private void EnsureSelectionMeshCloned()
        {
            if (_selMeshCloned) return;

            var entry = ActiveEntry;
            if (entry == null) return;

            Mesh currentMesh = entry.editMesh ?? entry.originalMesh;
            if (currentMesh == null) return;

            // Cache skinning data BEFORE swapping the mesh
            if (entry.IsSkinned && entry.smr.bones != null && currentMesh.bindposes != null)
            {
                _selSkinMatrices = FittingHelpers.PrecomputeSkinMatrices(entry.smr.bones, currentMesh.bindposes);
                _selBoneWeights = currentMesh.boneWeights;
            }
            else
            {
                _selSkinMatrices = null;
                _selBoneWeights = null;
                if (entry.Transform != null)
                    _selInvTRS = entry.Transform.worldToLocalMatrix;
            }

            _selOriginalMesh = currentMesh;
            _selMesh = Object.Instantiate(currentMesh);
            _selMesh.name = currentMesh.name + "_selMove";
            _selBaseVertices = _selMesh.vertices;

            Undo.RecordObject(entry.smr != null ? (Object)entry.smr : entry.mf, "MeshEdit: SelectMove");
            entry.SharedMesh = _selMesh;

            _selMeshCloned = true;
        }

        private void ApplyHandleMovement(Vector3 newHandlePos)
        {
            if (!_hasSelection || _persistentSelection.Count == 0) return;

            Vector3 deltaWorld = newHandlePos - _selectionCenter;
            if (deltaWorld.sqrMagnitude < 1e-10f) return;

            EnsureSelectionMeshCloned();
            if (_selMesh == null) return;

            var newVerts = (Vector3[])_selBaseVertices.Clone();

            if (_selSkinMatrices != null && _selBoneWeights != null)
            {
                foreach (var sel in _persistentSelection)
                {
                    var meshDelta = FittingHelpers.WorldToMeshDelta(
                        _selSkinMatrices, _selBoneWeights[sel.index], deltaWorld * sel.weight);
                    newVerts[sel.index] += meshDelta;
                }
            }
            else
            {
                foreach (var sel in _persistentSelection)
                {
                    newVerts[sel.index] += _selInvTRS.MultiplyVector(deltaWorld * sel.weight);
                }
            }

            _selMesh.vertices = newVerts;
            _selMesh.RecalculateNormals();
            _selMesh.RecalculateBounds();
            _selectionHandleMoved = true;
        }

        private void ConfirmSelectionMove()
        {
            if (!_selMeshCloned || _selMesh == null)
            {
                ClearPersistentSelection();
                return;
            }

            var entry = ActiveEntry;
            if (entry != null)
            {
                // _selMesh becomes the new editMesh
                if (entry.editMesh != null && entry.editMesh != _selOriginalMesh)
                    Object.DestroyImmediate(entry.editMesh);
                entry.editMesh = _selMesh;
                if (entry.originalVertices == null)
                    entry.originalVertices = entry.originalMesh.vertices;

                Debug.Log(string.Format("[MeshWeightEditor] SelectMove: {0} vertices moved.", _persistentSelection.Count));
            }

            // Clear without restoring (keep _selMesh as current mesh)
            _selMesh = null;
            _selOriginalMesh = null;
            _selBaseVertices = null;
            _selSkinMatrices = null;
            _selBoneWeights = null;
            _selMeshCloned = false;

            ClearPersistentSelection();
        }

        private void ClearPersistentSelection()
        {
            DiscardSelectionMeshClone();
            _persistentSelection.Clear();
            _persistentSelectionIndices.Clear();
            _hasSelection = false;
            _selectionHandleMoved = false;
            SceneView.RepaintAll();
            Repaint();
        }

        private void DiscardSelectionMeshClone()
        {
            if (!_selMeshCloned) return;

            // Restore the previous mesh
            if (_selOriginalMesh != null)
            {
                var entry = ActiveEntry;
                if (entry != null)
                    entry.SharedMesh = _selOriginalMesh;
            }

            if (_selMesh != null)
                Object.DestroyImmediate(_selMesh);

            _selMesh = null;
            _selOriginalMesh = null;
            _selBaseVertices = null;
            _selSkinMatrices = null;
            _selBoneWeights = null;
            _selMeshCloned = false;
        }

        private void DrawSelectionDots(SceneView sceneView)
        {
            if (!_hasSelection || _persistentSelection.Count == 0) return;
            var go = ActiveGameObject;
            if (go == null) return;

            MeshEditTools.MeshContext ctx;
            if (!MeshEditTools.TryGetMeshContext(go, out ctx)) return;

            float dotSize = HandleUtility.GetHandleSize(_selectionCenter) * 0.015f;

            foreach (var sel in _persistentSelection)
            {
                if (sel.index >= ctx.worldVertices.Length) continue;

                Color c = Color.Lerp(new Color(0f, 1f, 1f, 0.3f), new Color(0f, 1f, 1f, 0.9f), sel.weight);
                Handles.color = c;
                Handles.DotHandleCap(0, ctx.worldVertices[sel.index], Quaternion.identity, dotSize, EventType.Repaint);
            }
        }

        // ════════════════════════════════════════
        // Execute operations (button-based, active entry)
        // ════════════════════════════════════════

        private void ExecuteMeshMove()
        {
            var go = ActiveGameObject;
            if (go == null) return;
            string name = GetScenePath(go);
            string result;

            if (_brushValid)
            {
                result = MeshEditTools.MoveVertices(name,
                    _moveDelta.x, _moveDelta.y, _moveDelta.z,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z,
                    _brushRadius, "", 0.1f, _brushFalloff);
            }
            else
            {
                result = "Error: " + M("ブラシをScene Viewで配置してください。");
            }

            LogResult("MoveVertices", result);
        }

        private void ExecuteMeshScale()
        {
            var go = ActiveGameObject;
            if (go == null) return;
            string name = GetScenePath(go);
            string result;

            if (_brushValid)
            {
                result = MeshEditTools.ScaleVertices(name,
                    _scaleAmount.x, _scaleAmount.y, _scaleAmount.z,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z,
                    _brushRadius, _brushFalloff);
            }
            else
            {
                result = "Error: " + M("ブラシをScene Viewで配置してください。");
            }

            LogResult("ScaleVertices", result);
        }

        private void ExecuteMeshSmooth()
        {
            var go = ActiveGameObject;
            if (go == null) return;
            string name = GetScenePath(go);
            string result;

            if (_brushValid)
            {
                result = MeshEditTools.SmoothVertices(name,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z,
                    _brushRadius, "", 0.1f, _smoothIterations, _smoothStrength);
            }
            else
            {
                result = "Error: " + M("ブラシをScene Viewで配置してください。");
            }

            LogResult("SmoothVertices", result);
        }

        private void ExecuteSetWeight()
        {
            var go = ActiveGameObject;
            if (go == null || _boneNames.Length == 0) return;
            string name = GetScenePath(go);
            string bone = _boneNames[_selectedBoneIdx];
            string result;

            if (_brushValid)
            {
                result = WeightEditTools.SetBoneWeight(name, bone, _targetWeight,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z,
                    _brushRadius, _brushFalloff);
            }
            else
            {
                result = WeightEditTools.SetBoneWeight(name, bone, _targetWeight);
            }

            LogResult("SetBoneWeight", result);
            UpdateWeightVisualization();
        }

        private void ExecuteTransferWeight()
        {
            var go = ActiveGameObject;
            if (go == null || _boneNames.Length == 0) return;
            string name = GetScenePath(go);
            string from = _boneNames[_fromBoneIdx];
            string to = _boneNames[_toBoneIdx];

            string result;
            if (_brushValid)
            {
                result = WeightEditTools.TransferWeightBetweenBones(name, from, to, _transferAmount,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z, _brushRadius);
            }
            else
            {
                result = WeightEditTools.TransferWeightBetweenBones(name, from, to, _transferAmount);
            }

            LogResult("TransferWeight", result);
            UpdateWeightVisualization();
        }

        private void ExecuteSmoothWeights()
        {
            var go = ActiveGameObject;
            if (go == null) return;
            string name = GetScenePath(go);
            string result;

            if (_brushValid)
            {
                result = WeightEditTools.SmoothBoneWeights(name,
                    _brushCenter.x, _brushCenter.y, _brushCenter.z,
                    _brushRadius, "", _weightSmoothIterations);
            }
            else if (_boneNames.Length > 0)
            {
                result = WeightEditTools.SmoothBoneWeights(name,
                    float.NaN, float.NaN, float.NaN, 0f,
                    _boneNames[_selectedBoneIdx], _weightSmoothIterations);
            }
            else
            {
                result = "Error: " + M("ブラシを配置するか、ボーンを選択してください。");
            }

            LogResult("SmoothBoneWeights", result);
            UpdateWeightVisualization();
        }

        private void ExecuteNormalize()
        {
            var go = ActiveGameObject;
            if (go == null) return;
            string name = GetScenePath(go);
            string result = WeightEditTools.NormalizeBoneWeights(name);
            LogResult("NormalizeBoneWeights", result);
        }

        // ════════════════════════════════════════
        // Save / Bake
        // ════════════════════════════════════════

        private void SaveAsBlendShape()
        {
            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            int saved = 0;

            foreach (var entry in _smrEntries)
            {
                if (!entry.enabled || entry.editMesh == null || entry.originalVertices == null) continue;

                var editVerts = entry.editMesh.vertices;
                var deltaVerts = new Vector3[editVerts.Length];
                bool hasDeltas = false;
                for (int i = 0; i < editVerts.Length; i++)
                {
                    deltaVerts[i] = editVerts[i] - entry.originalVertices[i];
                    if (deltaVerts[i].sqrMagnitude > 1e-10f)
                        hasDeltas = true;
                }
                if (!hasDeltas) continue;

                // Clone original mesh to preserve existing BlendShapes
                var newMesh = Object.Instantiate(entry.originalMesh);
                newMesh.name = entry.originalMesh.name + "_bs_" + _blendShapeName;

                var zeroDelta = new Vector3[deltaVerts.Length];
                newMesh.AddBlendShapeFrame(_blendShapeName, 100f, deltaVerts, zeroDelta, zeroDelta);

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    string.Format("{0}/{1}.asset", GeneratedDir, newMesh.name));
                AssetDatabase.CreateAsset(newMesh, assetPath);

                // Assign new mesh and set BlendShape weight
                entry.SharedMesh = newMesh;
                if (entry.smr != null)
                {
                    int bsIndex = newMesh.GetBlendShapeIndex(_blendShapeName);
                    if (bsIndex >= 0)
                        entry.smr.SetBlendShapeWeight(bsIndex, 100f);
                }

                // Reset entry
                Object.DestroyImmediate(entry.editMesh);
                entry.editMesh = null;
                entry.originalMesh = newMesh;
                entry.originalVertices = null;
                saved++;

                Debug.Log(string.Format("[MeshWeightEditor] BlendShape '{0}' saved: {1}", _blendShapeName, assetPath));
            }

            AssetDatabase.SaveAssets();
            if (saved > 0)
                Debug.Log(string.Format("[MeshWeightEditor] {0} BlendShape(s) saved.", saved));
            else
                Debug.LogWarning("[MeshWeightEditor] No edits to save as BlendShape.");
        }

        private void ApplyToMeshAsset()
        {
            ToolUtility.EnsureAssetDirectory(GeneratedDir);
            int saved = 0;

            foreach (var entry in _smrEntries)
            {
                if (!entry.enabled || entry.editMesh == null) continue;

                entry.editMesh.name = entry.originalMesh.name + "_edited";
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    string.Format("{0}/{1}.asset", GeneratedDir, entry.editMesh.name));
                AssetDatabase.CreateAsset(entry.editMesh, assetPath);

                // editMesh is now an asset — keep it assigned
                entry.originalMesh = entry.editMesh;
                entry.editMesh = null;
                entry.originalVertices = null;
                saved++;

                Debug.Log(string.Format("[MeshWeightEditor] Mesh saved: {0}", assetPath));
            }

            AssetDatabase.SaveAssets();
            if (saved > 0)
                Debug.Log(string.Format("[MeshWeightEditor] {0} mesh(es) applied.", saved));
            else
                Debug.LogWarning("[MeshWeightEditor] No edits to apply.");
        }

        // ════════════════════════════════════════
        // Scene GUI
        // ════════════════════════════════════════

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_rootObject == null || _smrEntries.Count == 0) return;

            // Vertex visualization (both tabs)
            DrawVertexVisualization(sceneView);

            // Weight visualization (weight tab only)
            if (_showWeightColors && _mainTab == 1 && _cachedInfluence != null && _cachedWorldVerts != null)
                DrawWeightVisualization();

            // Draw persistent selection dots (always visible when in select-move mode)
            bool isSelectMoveMode = _mainTab == 0 && _meshOp == 1;
            if (isSelectMoveMode)
                DrawSelectionDots(sceneView);

            // Draw position handle for selection-move
            if (isSelectMoveMode && _hasSelection && _persistentSelection.Count > 0)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(_handlePosition, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    _handlePosition = newPos;
                    ApplyHandleMovement(newPos);
                    SceneView.RepaintAll();
                }
            }

            if (!_brushActive) return;

            // Consume default control to prevent scene selection
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);
            Event e = Event.current;

            // Escape to deactivate
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (_isDragging)
                    CancelDrag();
                else if (isSelectMoveMode && _hasSelection)
                    ClearPersistentSelection();
                else
                {
                    _brushActive = false;
                    _brushValid = false;
                }
                e.Use();
                Repaint();
                SceneView.RepaintAll();
                return;
            }

            // Brush radius with scroll wheel
            if (e.type == EventType.ScrollWheel && !e.alt && !_isDragging)
            {
                _brushRadius = Mathf.Clamp(_brushRadius - e.delta.y * 0.002f, 0.001f, 0.5f);
                e.Use();
                Repaint();
                SceneView.RepaintAll();
                return;
            }

            // Alt held = let Unity handle camera orbit/pan normally
            if (e.alt) return;

            // ─── Drag handling ───

            bool canDrag = _mainTab == 0 && _meshOp == 0;

            if (_isDragging)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    UpdateDrag(sceneView, e.mousePosition, e.shift);
                    e.Use();
                    SceneView.RepaintAll();
                    return;
                }

                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    EndDrag();
                    e.Use();
                    SceneView.RepaintAll();
                    return;
                }

                // Draw drag indicator
                Handles.color = new Color(0f, 1f, 0.5f, 0.9f);
                Handles.DrawWireDisc(_dragStartWorld,
                    (sceneView.camera.transform.position - _dragStartWorld).normalized, _brushRadius);

                // HUD while dragging
                Handles.BeginGUI();
                Rect dragArea = new Rect(10, sceneView.position.height - 70, 300, 50);
                GUILayout.BeginArea(dragArea, EditorStyles.helpBox);
                GUILayout.Label(M("ドラッグ中... Shift: 法線方向  Esc: キャンセル"), EditorStyles.boldLabel);
                int dragVertCount = 0;
                if (_multiDragSelections != null)
                    foreach (var ds in _multiDragSelections)
                        dragVertCount += ds.selection.Count;
                GUILayout.Label(string.Format(M("選択頂点数: {0}"), dragVertCount));
                GUILayout.EndArea();
                Handles.EndGUI();
                return;
            }

            // ─── Not dragging: show brush cursor ───

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector3 hitPoint;
            Vector3 hitNormal;
            bool hit = RaycastToAllTargets(ray, out hitPoint, out hitNormal);

            if (hit)
            {
                _brushCenter = hitPoint;
                _brushValid = true;

                Vector3 discNormal = (sceneView.camera.transform.position - hitPoint).normalized;

                // Outer brush circle
                Handles.color = new Color(1f, 0.8f, 0f, 0.8f);
                Handles.DrawWireDisc(hitPoint, discNormal, _brushRadius);

                // Inner falloff circle
                if (_brushFalloff > 0.01f)
                {
                    float innerRadius = _brushRadius * (1f - _brushFalloff);
                    Handles.color = new Color(1f, 0.8f, 0f, 0.3f);
                    Handles.DrawWireDisc(hitPoint, discNormal, innerRadius);
                }

                // Normal indicator
                Handles.color = new Color(0.3f, 0.8f, 1f, 0.6f);
                Handles.DrawLine(hitPoint, hitPoint + hitNormal * _brushRadius * 0.5f);

                SceneView.RepaintAll();
            }

            // Mouse down
            if (e.type == EventType.MouseDown && e.button == 0 && hit)
            {
                if (canDrag)
                {
                    BeginDrag(sceneView, hitPoint, hitNormal, e.mousePosition);
                    e.Use();
                    return;
                }
                else if (isSelectMoveMode)
                {
                    if (e.shift)
                        RemoveFromSelection(hitPoint);
                    else
                        AddToSelection(hitPoint);
                    e.Use();
                    Repaint();
                    SceneView.RepaintAll();
                    return;
                }
                else
                {
                    _brushCenter = hitPoint;
                    _brushValid = true;
                    e.Use();
                    Repaint();
                }
            }

            // Select-move: drag-painting selection
            if (isSelectMoveMode && e.type == EventType.MouseDrag && e.button == 0 && hit)
            {
                if (e.shift)
                    RemoveFromSelection(hitPoint);
                else
                    AddToSelection(hitPoint);
                e.Use();
                Repaint();
                SceneView.RepaintAll();
                return;
            }

            // HUD overlay
            Handles.BeginGUI();
            float hudHeight = (canDrag || isSelectMoveMode) ? 85 : 70;
            Rect area = new Rect(10, sceneView.position.height - hudHeight - 20, 320, hudHeight);
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            GUILayout.Label(M("Mesh & Weight Editor"), EditorStyles.boldLabel);
            GUILayout.Label(string.Format(M("半径: {0:F3}m  スクロール: サイズ変更  Esc: 解除"), _brushRadius));
            if (canDrag)
                GUILayout.Label(M("クリック+ドラッグ: 頂点移動  Shift+ドラッグ: 法線方向"));
            if (isSelectMoveMode)
                GUILayout.Label(string.Format(M("クリック: 選択追加  Shift+クリック: 選択除外  選択数: {0}"),
                    _persistentSelection.Count));
            if (_brushValid)
                GUILayout.Label(string.Format("({0:F3}, {1:F3}, {2:F3})", _brushCenter.x, _brushCenter.y, _brushCenter.z));
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ════════════════════════════════════════
        // Vertex Visualization
        // ════════════════════════════════════════

        private void DrawVertexVisualization(SceneView sceneView)
        {
            if (_smrEntries.Count == 0) return;

            var prevZTest = Handles.zTest;
            Handles.zTest = _showOccluded ? CompareFunction.Always : CompareFunction.LessEqual;

            Camera cam = sceneView.camera;
            float maxDist2 = _vertexDrawDistance * _vertexDrawDistance;
            Vector3 camPos = cam.transform.position;

            Handles.color = _vertexColor;

            for (int ei = 0; ei < _smrEntries.Count; ei++)
            {
                var entry = _smrEntries[ei];
                if (!entry.enabled || entry.sampledIndices == null) continue;

                EnsureBakeCache(entry);
                if (entry.cachedWorldVerts == null) continue;

                foreach (int vi in entry.sampledIndices)
                {
                    if (vi >= entry.cachedWorldVerts.Length) continue;
                    Vector3 wpos = entry.cachedWorldVerts[vi];
                    if ((camPos - wpos).sqrMagnitude > maxDist2) continue;
                    Handles.DotHandleCap(0, wpos, Quaternion.identity, _vertexDrawSize, EventType.Repaint);
                }
            }

            Handles.zTest = prevZTest;
        }

        // ════════════════════════════════════════
        // Weight Visualization
        // ════════════════════════════════════════

        private void UpdateWeightVisualization()
        {
            var smr = ActiveSmr;
            if (smr == null || smr.sharedMesh == null) return;

            var mesh = smr.sharedMesh;
            var boneWeights = mesh.boneWeights;

            _cachedInfluence = new float[boneWeights.Length];
            for (int i = 0; i < boneWeights.Length; i++)
                _cachedInfluence[i] = MeshEditTools.GetBoneInfluence(boneWeights[i], _visualBoneIdx);

            // Bake world verts
            var baked = new Mesh();
            smr.BakeMesh(baked);
            _cachedWorldVerts = baked.vertices;
            for (int i = 0; i < _cachedWorldVerts.Length; i++)
                _cachedWorldVerts[i] = smr.transform.TransformPoint(_cachedWorldVerts[i]);
            Object.DestroyImmediate(baked);

            SceneView.RepaintAll();
        }

        private void DrawWeightVisualization()
        {
            if (_cachedWorldVerts == null || _cachedInfluence == null) return;

            int count = Mathf.Min(_cachedWorldVerts.Length, _cachedInfluence.Length);
            float size = HandleUtility.GetHandleSize(_cachedWorldVerts.Length > 0 ? _cachedWorldVerts[0] : Vector3.zero) * 0.02f;

            for (int i = 0; i < count; i++)
            {
                float w = _cachedInfluence[i];
                if (w < 0.001f) continue;

                Color c;
                if (w < 0.5f)
                    c = Color.Lerp(Color.blue, Color.green, w * 2f);
                else
                    c = Color.Lerp(Color.green, Color.red, (w - 0.5f) * 2f);
                c.a = 0.8f;

                Handles.color = c;
                Handles.DotHandleCap(0, _cachedWorldVerts[i], Quaternion.identity, size, EventType.Repaint);
            }
        }

        // ════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════

        private bool RaycastToAllTargets(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            float globalMinDist = float.MaxValue;
            int bestEntryIdx = -1;

            for (int ei = 0; ei < _smrEntries.Count; ei++)
            {
                var entry = _smrEntries[ei];
                if (!entry.enabled) continue;

                EnsureBakeCache(entry);
                if (entry.cachedWorldVerts == null || entry.cachedTriangles == null) continue;

                var wv = entry.cachedWorldVerts;
                var tris = entry.cachedTriangles;
                var normals = entry.cachedNormals;
                var xform = entry.Transform;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    if (tris[i] >= wv.Length || tris[i + 1] >= wv.Length || tris[i + 2] >= wv.Length) continue;

                    var v0 = wv[tris[i]];
                    var v1 = wv[tris[i + 1]];
                    var v2 = wv[tris[i + 2]];

                    if (RayTriangle(ray, v0, v1, v2, out float dist) && dist < globalMinDist)
                    {
                        globalMinDist = dist;
                        hitPoint = ray.GetPoint(dist);
                        bestEntryIdx = ei;

                        // Compute face normal from world-space vertices
                        if (normals != null && tris[i] < normals.Length && tris[i + 1] < normals.Length && tris[i + 2] < normals.Length)
                        {
                            // For SMR baked mesh normals are in local space, for MF also local space
                            var n0 = xform.TransformDirection(normals[tris[i]]);
                            var n1 = xform.TransformDirection(normals[tris[i + 1]]);
                            var n2 = xform.TransformDirection(normals[tris[i + 2]]);
                            hitNormal = ((n0 + n1 + n2) / 3f).normalized;
                        }
                        else
                        {
                            hitNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                        }
                    }
                }
            }

            if (bestEntryIdx >= 0)
            {
                _activeSmrIndex = bestEntryIdx;
                return true;
            }
            return false;
        }

        private static List<MeshEditTools.VertexSelection> SelectVerticesFromCache(
            Vector3[] worldVerts, Vector3 center, float radius, float falloff)
        {
            var result = new List<MeshEditTools.VertexSelection>();
            float r2 = radius * radius;
            float innerRadius = radius * (1f - falloff);

            for (int i = 0; i < worldVerts.Length; i++)
            {
                float dist2 = (worldVerts[i] - center).sqrMagnitude;
                if (dist2 > r2) continue;

                float w = 1f;
                if (falloff > 0.001f)
                {
                    float dist = Mathf.Sqrt(dist2);
                    if (dist > innerRadius)
                        w = 1f - (dist - innerRadius) / (radius - innerRadius);
                }

                result.Add(new MeshEditTools.VertexSelection { index = i, weight = w });
            }

            return result;
        }

        private static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            distance = 0;
            Vector3 e1 = v1 - v0, e2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, e2);
            float a = Vector3.Dot(e1, h);
            if (a > -1e-5f && a < 1e-5f) return false;
            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0f || u + v > 1f) return false;
            distance = f * Vector3.Dot(e2, q);
            return distance > 1e-5f;
        }

        private static string GetScenePath(GameObject go)
        {
            var parts = new List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return "/" + string.Join("/", parts);
        }

        private static void LogResult(string operation, string result)
        {
            if (result.StartsWith("Error"))
                Debug.LogWarning($"[MeshWeightEditor] {operation}: {result}");
            else
                Debug.Log($"[MeshWeightEditor] {operation}: {result}");
        }
    }
}
