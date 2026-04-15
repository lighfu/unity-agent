using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class MeshPainterWindow : EditorWindow
    {
        private GameObject _avatarRoot;
        private List<RendererData> _rendererList = new List<RendererData>();
        private Vector2 _listScrollPos;
        private Vector2 _mainScrollPos;
        private string _searchKeyword = "";

        private List<UVIsland> _currentIslands = new List<UVIsland>();
        // Multi-island selection. Empty = "no island selection" (treated as whole-mesh scope).
        // Ctrl/Shift+click in UV editor or Scene view toggles membership; plain click replaces.
        private readonly HashSet<int> _selectedIslandIndices = new HashSet<int>();
        private Renderer _activeRenderer;
        private MeshPaintMetadata _currentMetadata;

        // Live preview session for 4 adjustment tabs (Paint / Gradient / HSV / Brightness-Contrast).
        // Scene Paint (tab 4) runs its own session via ScenePaintState; tab-switch auto-commits.
        private readonly MeshPaintPreviewSession _session = new MeshPaintPreviewSession();

        private bool _isSceneSelectionEnabled = true;
        private bool _use3DConnection = false;

        // ─── UV Zoom/Pan ───
        private float _uvZoom = 1f;
        private Vector2 _uvPan = new Vector2(0.5f, 0.5f); // center of view in view-space

        // ─── Paint tab ───
        private Color _targetColor = Color.white;

        // ─── Gradient tab ───
        private Color _gradFrom = Color.white;
        private Color _gradTo = Color.black;
        private int _gradDirectionIdx = 0;
        private int _gradBlendModeIdx = 0;
        private float _gradStartT = 0f;
        private float _gradEndT = 1f;
        private int _gradHistoryTarget = 0; // 0=From, 1=To

        // ─── HSV tab ───
        private float _hueShift = 0f;
        private float _satScale = 1f;
        private float _valScale = 1f;

        // ─── Contrast tab ───
        private float _contrast = 0f;
        private float _brightness = 0f;

        // ─── Scene Paint tab ───
        private float _sceneBrushSize = 0.02f;
        private float _sceneBrushOpacity = 1.0f;
        private float _sceneBrushHardness = 0.8f;
        private Color _sceneBrushColor = Color.white;
        private int _sceneBlendModeIdx = 0;
        private int _sceneToolIdx = 0;
        private bool _sceneSymmetry = false;
        private bool _sceneIslandMask = false;

        // ─── Tab selection ───
        private int _editTab = 0;
        private int _prevEditTab = 0;
        private static string[] TabLabels => new[] { M("ペイント"), M("グラデーション"), M("HSV"), M("明るさ/コントラスト"), M("Sceneペイント") };
        private static string[] DirectionLabels => new[] { M("上→下"), M("下→上"), M("左→右"), M("右→左") };
        private static readonly string[] DirectionValues = { "top_to_bottom", "bottom_to_top", "left_to_right", "right_to_left" };
        private static string[] BlendModeLabels => new[] { M("スクリーン"), M("オーバーレイ"), M("ティント"), M("乗算"), M("置換") };
        private static readonly string[] BlendModeValues = { "screen", "overlay", "tint", "multiply", "replace" };

        // ─── Color History ───
        private const int MaxColorHistory = 16;
        private List<Color> _colorHistory = new List<Color>();
        private const string ColorHistoryPrefKey = "MeshPainter_ColorHistory";
        private bool _colorHistoryLoaded = false;

        private class RendererData
        {
            public Renderer renderer;
            public bool isChecked; // multi-select for batch operations
            public string displayName;
            public string fullPath;
        }

        [MenuItem("Window/紫陽花広場/Mesh Painter")]
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
            GetWindow<MeshPainterWindow>(M("メッシュペインター"));
        }

        public static void OpenForRenderer(Renderer renderer)
        {
            var window = GetWindow<MeshPainterWindow>(M("メッシュペインター"));
            var root = AutoDetectAvatarRoot(renderer.gameObject);
            if (root != null)
            {
                window._avatarRoot = root;
                window.RefreshRendererList();
            }
            window.SelectRenderer(renderer);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            ScenePaintState.OnColorPicked += HandleColorPicked;
            EnsureColorHistoryLoaded();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            ScenePaintState.OnColorPicked -= HandleColorPicked;
            if (ScenePaintState.IsActive)
                ScenePaintState.Deactivate();

            // End preview session. On window close, we can't show a blocking dialog safely
            // from OnDisable (Unity may be in the middle of domain reload), so auto-commit
            // any pending preview to avoid data loss.
            if (_session.IsActive)
                _session.End(autoCommit: _session.HasUncommittedChanges);
        }

        private void HandleColorPicked(Color color)
        {
            _sceneBrushColor = color;
            AddColorToHistory(color);
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;

            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null)
            {
                if (_avatarRoot != root)
                {
                    _avatarRoot = root;
                    RefreshRendererList();
                }

                // Auto-select renderer if the selected object has one
                var renderer = Selection.activeGameObject.GetComponent<Renderer>();
                if (renderer != null)
                    SelectRenderer(renderer);
            }

            Repaint();
        }

        private void OnGUI()
        {
            try { DrawMainGUI(); }
            catch (System.ArgumentException) { }
        }

        private void DrawMainGUI()
        {
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 8, 8) });

            GUILayout.Label(M("メッシュペインター"), EditorStyles.boldLabel);

            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = _isSceneSelectionEnabled ? Color.green : new Color(1, 0.5f, 0.5f);
            _isSceneSelectionEnabled = GUILayout.Toggle(_isSceneSelectionEnabled,
                M("Scene選択") + " " + (_isSceneSelectionEnabled ? "(ON)" : "(OFF)"), EditorStyles.toolbarButton);
            GUI.backgroundColor = oldBg;

            EditorGUI.BeginChangeCheck();
            _use3DConnection = GUILayout.Toggle(_use3DConnection, M("3D接続でグループ化"), EditorStyles.toolbarButton);
            if (EditorGUI.EndChangeCheck() && _activeRenderer != null)
            {
                Renderer r = _activeRenderer;
                _activeRenderer = null;
                PrepareEditor(r);
                _selectedIslandIndices.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _avatarRoot = (GameObject)EditorGUILayout.ObjectField(M("アバタールート"), _avatarRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) RefreshRendererList();

            if (_avatarRoot == null)
            {
                EditorGUILayout.HelpBox(M("アバターのルートオブジェクトを選択してください。\nヒエラルキーでアバターを選択すると自動検出されます。"), MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawRendererList();
            DrawUVEditor();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Edit tabs (scrollable)
            _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos, GUILayout.Height(320));
            EditorGUI.BeginChangeCheck();
            _editTab = GUILayout.Toolbar(_editTab, TabLabels);
            if (EditorGUI.EndChangeCheck())
            {
                // Leaving Scene Paint: deactivate stroke session
                if (_prevEditTab == 4 && _editTab != 4 && ScenePaintState.IsActive)
                    ScenePaintState.Deactivate();

                // Entering Scene Paint: auto-commit any preview session from 4 adjustment tabs
                if (_prevEditTab != 4 && _editTab == 4 && _session.IsActive)
                {
                    if (_session.HasUncommittedChanges)
                    {
                        int choice = EditorUtility.DisplayDialogComplex(
                            M("未コミットの変更があります"),
                            M("Scene ペイントに切替える前に、未適用の変更をどうしますか？"),
                            M("適用"), M("キャンセル"), M("破棄"));
                        if (choice == 1)
                        {
                            // Cancel: revert tab switch, stay on previous tab
                            _editTab = _prevEditTab;
                        }
                        else
                        {
                            _session.End(autoCommit: choice == 0);
                            _prevEditTab = _editTab;
                        }
                    }
                    else
                    {
                        _session.End(autoCommit: false);
                        _prevEditTab = _editTab;
                    }
                }
                else
                {
                    // Leaving Scene Paint and re-entering adjustment tabs: start fresh session
                    if (_prevEditTab == 4 && _editTab != 4 && !_session.IsActive && _activeRenderer != null)
                    {
                        _session.Begin(_activeRenderer, _avatarRoot);
                        ResetAdjustmentParameters();
                    }
                    _prevEditTab = _editTab;
                }
            }
            EditorGUILayout.Space(4);

            switch (_editTab)
            {
                case 0: DrawPaintTab(); break;
                case 1: DrawGradientTab(); break;
                case 2: DrawHSVTab(); break;
                case 3: DrawBrightnessContrastTab(); break;
                case 4: DrawScenePaintTab(); break;
            }

            // Color History (shared across tabs)
            DrawColorHistory();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // Renderer List (with multi-select)
        // ══════════════════════════════════════

        private void DrawRendererList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240));
            GUILayout.Label(M("メッシュ一覧"), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _searchKeyword = EditorGUILayout.TextField(_searchKeyword);
            if (GUILayout.Button("↻", GUILayout.Width(25))) RefreshRendererList();
            EditorGUILayout.EndHorizontal();

            // Select All / Deselect All
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(M("全選択"), EditorStyles.miniButtonLeft))
                foreach (var d in _rendererList) d.isChecked = true;
            if (GUILayout.Button(M("全解除"), EditorStyles.miniButtonRight))
                foreach (var d in _rendererList) d.isChecked = false;
            EditorGUILayout.EndHorizontal();

            int checkedCount = _rendererList.Count(d => d.isChecked);
            if (checkedCount > 0)
                EditorGUILayout.LabelField(string.Format(M("{0} メッシュ選択中"), checkedCount), EditorStyles.miniLabel);

            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos, GUILayout.Height(360));
            foreach (var data in _rendererList)
            {
                if (!string.IsNullOrEmpty(_searchKeyword))
                {
                    string kw = _searchKeyword.ToLower();
                    if (!data.displayName.ToLower().Contains(kw) && !data.fullPath.ToLower().Contains(kw))
                        continue;
                }

                bool isPrimary = data.renderer == _activeRenderer;
                EditorGUILayout.BeginHorizontal(isPrimary ? EditorStyles.selectionRect : EditorStyles.label);

                // Checkbox for multi-select
                data.isChecked = EditorGUILayout.Toggle(data.isChecked, GUILayout.Width(18));

                // Name button for primary selection (UV display)
                if (GUILayout.Button(new GUIContent(data.displayName, data.fullPath), EditorStyles.label))
                {
                    Selection.activeGameObject = data.renderer.gameObject;
                    PrepareEditor(data.renderer);
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // UV Editor (with zoom/pan)
        // ══════════════════════════════════════

        private void DrawUVEditor()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("UV展開図"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(M("リセット表示"), EditorStyles.miniButton, GUILayout.Width(80)))
            {
                _uvZoom = 1f;
                _uvPan = new Vector2(0.5f, 0.5f);
            }
            GUILayout.Label($"x{_uvZoom:F1}", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();

            if (_activeRenderer == null)
            {
                GUILayout.Label(M("メッシュをリストまたはSceneから選択してください。"));
                EditorGUILayout.EndVertical();
                return;
            }

            Mesh mesh = GetMesh(_activeRenderer);
            if (mesh == null)
            {
                GUILayout.Label(M("メッシュが見つかりません。"));
                EditorGUILayout.EndVertical();
                return;
            }

            Rect containerRect = GUILayoutUtility.GetRect(400, 400);
            float viewSize = Mathf.Min(containerRect.width, containerRect.height);
            Rect uvRect = new Rect(
                containerRect.x + (containerRect.width - viewSize) * 0.5f,
                containerRect.y + (containerRect.height - viewSize) * 0.5f,
                viewSize, viewSize);

            // Background
            GUI.Box(uvRect, "");

            // Clipped drawing area
            GUI.BeginClip(uvRect);
            Rect localRect = new Rect(0, 0, uvRect.width, uvRect.height);

            // Texture
            Texture mainTex = _activeRenderer.sharedMaterial?.mainTexture;
            if (mainTex != null)
            {
                Vector2 tl = UVToLocal(localRect, new Vector2(0, 1));
                Vector2 br = UVToLocal(localRect, new Vector2(1, 0));
                Rect texRect = Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
                GUI.DrawTexture(texRect, mainTex, ScaleMode.StretchToFill);
            }

            // UV wireframe
            DrawUVWireframe(localRect, mesh);

            GUI.EndClip();

            // Handle input (using absolute coordinates)
            HandleUVZoomPan(uvRect);
            HandleUVClick(uvRect, mesh);

            if (_selectedIslandIndices.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(string.Format(M("選択中: {0} アイランド ({1})"),
                    _selectedIslandIndices.Count, FormatIslandIndices(_selectedIslandIndices)),
                    EditorStyles.miniBoldLabel);
                if (GUILayout.Button(M("選択解除"), EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    _selectedIslandIndices.Clear();
                    RecomputePreviewForCurrentTab();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(M("Ctrl/Shift+クリックで追加選択"), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private static string FormatIslandIndices(HashSet<int> set)
        {
            if (set == null || set.Count == 0) return "";
            var sorted = new List<int>(set); sorted.Sort();
            const int maxShow = 8;
            if (sorted.Count <= maxShow)
                return "#" + string.Join(", #", sorted);
            var head = sorted.GetRange(0, maxShow);
            return "#" + string.Join(", #", head) + string.Format(" +{0}", sorted.Count - maxShow);
        }

        /// <summary>
        /// Apply a click-to-select action with modifier-key semantics.
        /// <paramref name="hitIsland"/> = -1 means "click missed any island".
        /// <paramref name="additive"/> = true (Ctrl/Shift) toggles membership; false replaces.
        /// </summary>
        private void ApplyIslandSelection(int hitIsland, bool additive)
        {
            if (additive)
            {
                if (hitIsland < 0) return; // missed: do nothing in additive mode
                if (!_selectedIslandIndices.Add(hitIsland))
                    _selectedIslandIndices.Remove(hitIsland); // already present → toggle off
            }
            else
            {
                _selectedIslandIndices.Clear();
                if (hitIsland >= 0)
                    _selectedIslandIndices.Add(hitIsland);
            }
            RecomputePreviewForCurrentTab();
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
                // Zoom toward mouse cursor
                Vector2 mouseUV = ScreenToUV(rect, e.mousePosition);
                float mvx = mouseUV.x;
                float mvy = 1f - mouseUV.y;

                float oldZoom = _uvZoom;
                float zoomDelta = -e.delta.y * 0.1f;
                _uvZoom = Mathf.Clamp(_uvZoom * (1f + zoomDelta), 0.5f, 20f);

                // Adjust pan so point under cursor stays fixed
                _uvPan.x = mvx - (mvx - _uvPan.x) * oldZoom / _uvZoom;
                _uvPan.y = mvy - (mvy - _uvPan.y) * oldZoom / _uvZoom;

                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseDrag && e.button == 2) // middle mouse pan
            {
                _uvPan.x -= e.delta.x / (rect.width * _uvZoom);
                _uvPan.y -= e.delta.y / (rect.height * _uvZoom);
                e.Use();
                Repaint();
            }
        }

        // GL material for fast line drawing (shared, lazy-init)
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

            // Viewport bounds for culling
            float viewMin = -0.1f;
            float viewMaxX = rect.width + 0.1f;
            float viewMaxY = rect.height + 0.1f;

            // Batch all wireframe lines in a single GL.Begin/End
            GL.Begin(GL.LINES);
            GL.Color(new Color(1, 1, 1, 0.3f));
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 p0 = UVToLocal(rect, uvs[tris[i]]);
                Vector2 p1 = UVToLocal(rect, uvs[tris[i + 1]]);
                Vector2 p2 = UVToLocal(rect, uvs[tris[i + 2]]);

                // Cull triangles fully outside viewport
                float triMinX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x));
                float triMaxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
                float triMinY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y));
                float triMaxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                if (triMaxX < viewMin || triMinX > viewMaxX || triMaxY < viewMin || triMinY > viewMaxY)
                    continue;

                GL.Vertex3(p0.x, p0.y, 0); GL.Vertex3(p1.x, p1.y, 0);
                GL.Vertex3(p1.x, p1.y, 0); GL.Vertex3(p2.x, p2.y, 0);
                GL.Vertex3(p2.x, p2.y, 0); GL.Vertex3(p0.x, p0.y, 0);
            }
            GL.End();

            // Selected island highlight (multi-select)
            if (_selectedIslandIndices.Count > 0)
            {
                GL.Begin(GL.LINES);
                GL.Color(Color.yellow);
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

        private void HandleUVClick(Rect rect, Mesh mesh)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;

            Vector2 clickUV = ScreenToUV(rect, e.mousePosition);

            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;

            int hitIsland = -1;
            for (int i = 0; i < _currentIslands.Count; i++)
            {
                foreach (int triIdx in _currentIslands[i].triangleIndices)
                {
                    if (IsPointInTriangle(clickUV, uvs[tris[triIdx * 3]], uvs[tris[triIdx * 3 + 1]], uvs[tris[triIdx * 3 + 2]]))
                    {
                        hitIsland = i;
                        break;
                    }
                }
                if (hitIsland != -1) break;
            }

            bool additive = e.control || e.shift || e.command;
            ApplyIslandSelection(hitIsland, additive);

            Repaint();
            e.Use();
        }

        // ══════════════════════════════════════
        // Tab 0: Paint (immediate apply)
        // ══════════════════════════════════════

        private void DrawPaintTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("単色ペイント"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _targetColor = EditorGUILayout.ColorField(M("ペイント色"), _targetColor);
            if (EditorGUI.EndChangeCheck())
            {
                AddColorToHistory(_targetColor);
                RecomputePreviewForCurrentTab();
            }

            EditorGUILayout.Space(4);
            DrawLivePreviewButtons();
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // Tab 1: Gradient
        // ══════════════════════════════════════

        private void DrawGradientTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("グラデーション"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            EditorGUI.BeginChangeCheck();
            _gradFrom = EditorGUILayout.ColorField(M("開始色 (From)"), _gradFrom);
            if (EditorGUI.EndChangeCheck()) { AddColorToHistory(_gradFrom); _gradHistoryTarget = 0; }

            EditorGUI.BeginChangeCheck();
            _gradTo = EditorGUILayout.ColorField(M("終了色 (To)"), _gradTo);
            if (EditorGUI.EndChangeCheck()) { AddColorToHistory(_gradTo); _gradHistoryTarget = 1; }

            _gradDirectionIdx = EditorGUILayout.Popup(M("方向"), _gradDirectionIdx, DirectionLabels);
            _gradBlendModeIdx = EditorGUILayout.Popup(M("ブレンドモード"), _gradBlendModeIdx, BlendModeLabels);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(M("範囲"), GUILayout.Width(40));
            _gradStartT = EditorGUILayout.FloatField(_gradStartT, GUILayout.Width(50));
            EditorGUILayout.MinMaxSlider(ref _gradStartT, ref _gradEndT, 0f, 1f);
            _gradEndT = EditorGUILayout.FloatField(_gradEndT, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
                RecomputePreviewForCurrentTab();

            EditorGUILayout.Space(4);
            DrawLivePreviewButtons();
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // Tab 2: HSV
        // ══════════════════════════════════════

        private void DrawHSVTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("HSV 調整"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _hueShift = EditorGUILayout.Slider(M("色相シフト (Hue)"), _hueShift, -180f, 180f);
            _satScale = EditorGUILayout.Slider(M("彩度 (Saturation)"), _satScale, 0f, 3f);
            _valScale = EditorGUILayout.Slider(M("明度 (Value)"), _valScale, 0f, 3f);
            bool hsvChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(M("リセット"), GUILayout.Width(60)))
            {
                _hueShift = 0f; _satScale = 1f; _valScale = 1f;
                hsvChanged = true;
            }
            EditorGUILayout.EndHorizontal();

            if (hsvChanged)
                RecomputePreviewForCurrentTab();

            DrawLivePreviewButtons();
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // Tab 3: Brightness / Contrast
        // ══════════════════════════════════════

        private void DrawBrightnessContrastTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("明るさ / コントラスト"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _brightness = EditorGUILayout.Slider(M("明るさ (Brightness)"), _brightness, -1f, 1f);
            _contrast = EditorGUILayout.Slider(M("コントラスト (Contrast)"), _contrast, -1f, 1f);
            bool bcChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(M("リセット"), GUILayout.Width(60)))
            {
                _brightness = 0f; _contrast = 0f;
                bcChanged = true;
            }
            EditorGUILayout.EndHorizontal();

            if (bcChanged)
                RecomputePreviewForCurrentTab();

            DrawLivePreviewButtons();
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════
        // Tab 4: Scene Paint
        // ══════════════════════════════════════

        private void DrawScenePaintTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(M("Scene View ペイント"), EditorStyles.boldLabel);

            // Sync from ScenePaintState (shortcuts in scene view may change these)
            if (ScenePaintState.IsActive)
            {
                _sceneToolIdx = (int)ScenePaintState.ActiveTool;
                _sceneBrushSize = ScenePaintState.BrushSize;
                _sceneBrushOpacity = ScenePaintState.BrushOpacity;
                _sceneBrushHardness = ScenePaintState.BrushHardness;
                _sceneBrushColor = ScenePaintState.BrushColor;
                _sceneSymmetry = ScenePaintState.SymmetryEnabled;
            }

            if (_activeRenderer == null && !ScenePaintState.IsActive)
            {
                EditorGUILayout.HelpBox(M("メッシュをリストまたはScene Viewで選択してください。\nScene Viewのメッシュをクリックするとそのまま開始します。"), MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Tool selection
            EditorGUI.BeginChangeCheck();
            _sceneToolIdx = GUILayout.SelectionGrid(_sceneToolIdx, ScenePaintState.ToolLabels, 4);

            // Brush settings
            _sceneBrushSize = EditorGUILayout.Slider(M("ブラシサイズ"), _sceneBrushSize, 0.001f, 0.2f);
            _sceneBrushOpacity = EditorGUILayout.Slider(M("不透明度"), _sceneBrushOpacity, 0f, 1f);
            _sceneBrushHardness = EditorGUILayout.Slider(M("硬さ"), _sceneBrushHardness, 0f, 1f);

            // Color + Blend mode (tools that use brush color)
            bool needsColor = _sceneToolIdx == (int)BrushTool.Paint || _sceneToolIdx == (int)BrushTool.Tint
                || _sceneToolIdx == (int)BrushTool.Dodge || _sceneToolIdx == (int)BrushTool.Burn;
            if (needsColor)
            {
                _sceneBrushColor = EditorGUILayout.ColorField(M("ブラシ色"), _sceneBrushColor);
                if (_sceneToolIdx == (int)BrushTool.Paint)
                    _sceneBlendModeIdx = EditorGUILayout.Popup(M("ブレンドモード"), _sceneBlendModeIdx, ScenePaintState.BlendModeLabels);
            }

            // Symmetry toggle
            _sceneSymmetry = EditorGUILayout.Toggle(M("対称ペイント (M)"), _sceneSymmetry);

            // Island mask: always available. When ON + island selected → restrict to islands. When ON + no island → paint all.
            _sceneIslandMask = EditorGUILayout.Toggle(M("アイランドマスク"), _sceneIslandMask);
            if (_sceneIslandMask && _selectedIslandIndices.Count == 0)
                EditorGUILayout.HelpBox(M("アイランドを選択するとその範囲のみにペイントを制限します。"), MessageType.None);

            if (EditorGUI.EndChangeCheck())
            {
                SyncBrushSettings();
                if (_sceneBrushColor != _targetColor)
                    AddColorToHistory(_sceneBrushColor);
            }

            EditorGUILayout.Space(4);

            // Activate / Deactivate button
            if (ScenePaintState.IsActive)
            {
                EditorGUILayout.HelpBox(M("Scene View でペイント中です。\nAlt+ドラッグ: 3D操作  Ctrl+Click: スポイト  Ctrl+Z: 元に戻す"), MessageType.Info);
                if (GUILayout.Button(M("ペイント終了"), GUILayout.Height(28)))
                    ScenePaintState.Deactivate();
            }
            else
            {
                if (GUILayout.Button(M("ペイント開始"), GUILayout.Height(28)))
                    ActivateScenePaint();
            }

            EditorGUILayout.EndVertical();
        }

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

            // Build island mask if enabled
            if (_sceneIslandMask && _selectedIslandIndices.Count > 0 && _currentIslands != null)
            {
                ScenePaintEngine.BuildIslandMask(_currentIslands, _selectedIslandIndices);
            }
            else
            {
                ScenePaintState.MaskedTriangles = null;
            }
        }

        private void ActivateScenePaint()
        {
            if (_activeRenderer == null || _avatarRoot == null) return;

            SyncBrushSettings();
            ScenePaintState.Activate(_activeRenderer, _avatarRoot);

            // Build island mask if enabled
            if (_sceneIslandMask && _selectedIslandIndices.Count > 0 && _currentIslands != null)
            {
                ScenePaintEngine.BuildIslandMask(_currentIslands, _selectedIslandIndices);
            }

            SceneView.RepaintAll();
        }

        // ══════════════════════════════════════
        // Shared Apply Buttons
        // ══════════════════════════════════════

        private void DrawApplyButtons(System.Action<string> applyAction)
        {
            // Legacy signature kept for Scene Paint tab / any future callers. New live-preview
            // tabs use DrawLivePreviewButtons below.
            int checkedCount = _rendererList.Count(d => d.isChecked);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _activeRenderer != null;

            string wholeLabel = checkedCount > 1
                ? string.Format(M("適用 (全体 x{0}メッシュ)"), checkedCount)
                : M("適用 (メッシュ全体)");

            if (GUILayout.Button(wholeLabel, GUILayout.Height(28)))
                applyAction("");

            if (GUILayout.Button(M("適用 (選択アイランドのみ)"), GUILayout.Height(28)))
                applyAction(GetSelectedIslandIndicesString());

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Unified live-preview Apply / Revert button row used by the four adjustment tabs.
        /// The preview has already been staged in _session by slider-change handlers; this
        /// method only flushes it to disk (Apply) or throws it away (Revert).
        /// </summary>
        private void DrawLivePreviewButtons()
        {
            int checkedCount = _rendererList.Count(d => d.isChecked);

            if (_session.IsActive && _session.HasUncommittedChanges)
            {
                EditorGUILayout.HelpBox(M("未コミットのプレビューがあります。「適用」でディスクに保存、「元に戻す」で破棄します。"), MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _activeRenderer != null && _session.IsActive;

            string applyLabel = checkedCount > 1
                ? string.Format(M("適用 (確定 x{0}メッシュ)"), checkedCount)
                : M("適用 (確定)");
            if (GUILayout.Button(applyLabel, GUILayout.Height(28)))
            {
                CommitPreviewAndBatch();
                ResetAdjustmentParameters();
            }

            GUI.enabled = _activeRenderer != null && _session.IsActive && _session.HasUncommittedChanges;
            if (GUILayout.Button(M("元に戻す"), GUILayout.Height(28), GUILayout.Width(100)))
            {
                _session.Revert();
                ResetAdjustmentParameters();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════
        // Color History
        // ══════════════════════════════════════

        private void EnsureColorHistoryLoaded()
        {
            if (_colorHistoryLoaded) return;
            _colorHistoryLoaded = true;
            _colorHistory.Clear();
            string saved = SettingsStore.GetString(ColorHistoryPrefKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            foreach (string hex in saved.Split(';'))
            {
                if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c))
                    _colorHistory.Add(c);
            }
        }

        private void SaveColorHistory()
        {
            var parts = new List<string>();
            foreach (var c in _colorHistory)
                parts.Add("#" + ColorUtility.ToHtmlStringRGBA(c));
            SettingsStore.SetString(ColorHistoryPrefKey, string.Join(";", parts));
        }

        private void AddColorToHistory(Color c)
        {
            EnsureColorHistoryLoaded();
            // Remove near-duplicate
            _colorHistory.RemoveAll(h =>
                Mathf.Abs(h.r - c.r) < 0.01f && Mathf.Abs(h.g - c.g) < 0.01f &&
                Mathf.Abs(h.b - c.b) < 0.01f && Mathf.Abs(h.a - c.a) < 0.01f);
            _colorHistory.Insert(0, c);
            if (_colorHistory.Count > MaxColorHistory)
                _colorHistory.RemoveRange(MaxColorHistory, _colorHistory.Count - MaxColorHistory);
            SaveColorHistory();
        }

        private void DrawColorHistory()
        {
            EnsureColorHistoryLoaded();
            if (_colorHistory.Count == 0) return;

            EditorGUILayout.Space(6);

            // For gradient tab, show target selector
            if (_editTab == 1)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(M("色の履歴"), EditorStyles.miniLabel, GUILayout.Width(52));
                GUILayout.Label(M("適用先:"), EditorStyles.miniLabel, GUILayout.Width(40));
                _gradHistoryTarget = GUILayout.Toolbar(_gradHistoryTarget, new[] { M("開始色"), M("終了色") },
                    EditorStyles.miniButton, GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField(M("色の履歴"), EditorStyles.miniLabel);
            }

            const int swatchSize = 22;
            const int swatchesPerRow = 8;

            for (int row = 0; row * swatchesPerRow < _colorHistory.Count; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < swatchesPerRow; col++)
                {
                    int idx = row * swatchesPerRow + col;
                    if (idx >= _colorHistory.Count) break;

                    Color c = _colorHistory[idx];
                    Rect rect = GUILayoutUtility.GetRect(swatchSize, swatchSize,
                        GUILayout.Width(swatchSize), GUILayout.Height(swatchSize));

                    // Background for alpha colors
                    EditorGUI.DrawRect(rect, new Color(0.8f, 0.8f, 0.8f));
                    // Color swatch
                    EditorGUI.DrawRect(rect, c);
                    // Border
                    Color borderColor = new Color(0.2f, 0.2f, 0.2f);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), borderColor);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), borderColor);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), borderColor);
                    EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), borderColor);

                    // Tooltip
                    EditorGUI.LabelField(rect, new GUIContent("", $"#{ColorUtility.ToHtmlStringRGBA(c)}"));

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        ApplyHistoryColor(c);
                        Event.current.Use();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ApplyHistoryColor(Color c)
        {
            switch (_editTab)
            {
                case 0: // Paint
                    _targetColor = c;
                    break;
                case 1: // Gradient
                    if (_gradHistoryTarget == 0) _gradFrom = c;
                    else _gradTo = c;
                    break;
                case 4: // Scene Paint
                    _sceneBrushColor = c;
                    ScenePaintState.BrushColor = c;
                    break;
            }
            Repaint();
        }

        // ══════════════════════════════════════
        // Scene GUI
        // ══════════════════════════════════════

        private void OnSceneGUI(SceneView sceneView)
        {
            // Skip island selection when scene paint is active
            if (ScenePaintState.IsActive) return;
            if (!_isSceneSelectionEnabled || _avatarRoot == null) return;

            Event e = Event.current;

            Handles.BeginGUI();
            Rect area = new Rect(10, sceneView.position.height - 90, 280, 70);
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            GUILayout.Label(M("Mesh Painter - Scene選択"), EditorStyles.boldLabel);
            if (_activeRenderer != null)
                GUILayout.Label(string.Format(M("メッシュ: {0}"), _activeRenderer.gameObject.name));
            if (_selectedIslandIndices.Count > 0)
                GUILayout.Label(string.Format(M("選択中: {0} アイランド"), _selectedIslandIndices.Count));
            GUILayout.Label(M("Ctrl/Shift+クリックで追加選択"), EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                GameObject picked = HandleUtility.PickGameObject(e.mousePosition, true);

                if (picked != null && picked.transform.IsChildOf(_avatarRoot.transform))
                {
                    Renderer r = picked.GetComponent<Renderer>();
                    if (r != null)
                    {
                        bool additive = e.control || e.shift || e.command;
                        SelectBySceneClick(r, ray, additive);

                        // Scene Paint tab: auto-start painting on click
                        if (_editTab == 4 && !ScenePaintState.IsActive)
                        {
                            ActivateScenePaint();
                        }

                        e.Use();
                    }
                }
            }

            DrawSceneHighlight();
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
                    minDist = dist;
                    hitTriIdx = i;
                }
            }

            int hitIsland = -1;
            if (hitTriIdx != -1 && _currentIslands != null)
                hitIsland = _currentIslands.FindIndex(isl => isl.triangleIndices.Contains(hitTriIdx));
            ApplyIslandSelection(hitIsland, additive);
            Repaint();
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
                Handles.color = Color.yellow;
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

        // ══════════════════════════════════════
        // Core Logic
        // ══════════════════════════════════════

        private string GetGameObjectPath(Renderer renderer = null)
        {
            var r = renderer != null ? renderer : _activeRenderer;
            if (r == null) return "";
            Transform t = r.transform;
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private string GetSelectedIslandIndicesString()
        {
            if (_selectedIslandIndices.Count == 0) return "";
            var sorted = new List<int>(_selectedIslandIndices);
            sorted.Sort();
            return string.Join(";", sorted);
        }

        private List<Renderer> GetTargetRenderers()
        {
            var targets = new List<Renderer>();
            foreach (var data in _rendererList)
            {
                if (data.isChecked && data.renderer != null)
                    targets.Add(data.renderer);
            }
            if (targets.Count == 0 && _activeRenderer != null)
                targets.Add(_activeRenderer);
            return targets;
        }

        private void SelectRenderer(Renderer r)
        {
            PrepareEditor(r);
        }

        private void PrepareEditor(Renderer r)
        {
            if (r == _activeRenderer) return;

            // Handle dirty session from previous renderer: prompt Apply / Discard / Cancel.
            if (_session.IsActive && _session.HasUncommittedChanges)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    M("未コミットの変更があります"),
                    M("現在編集中のメッシュに未適用の変更があります。どうしますか？"),
                    M("適用"), M("キャンセル"), M("破棄"));
                if (choice == 1) return;          // Cancel: stay on current renderer
                if (choice == 0) _session.End(autoCommit: true);
                else _session.End(autoCommit: false); // Discard
            }
            else if (_session.IsActive)
            {
                _session.End(autoCommit: false);
            }

            _activeRenderer = r;
            _selectedIslandIndices.Clear();
            _uvZoom = 1f;
            _uvPan = new Vector2(0.5f, 0.5f);

            Mesh mesh = GetMesh(r);
            if (mesh != null)
                _currentIslands = UVIslandDetector.DetectIslands(mesh, _use3DConnection);

            _currentMetadata = MetadataManager.LoadMetadata(_avatarRoot.name, r.gameObject.name);
            if (_currentMetadata == null)
            {
                _currentMetadata = new MeshPaintMetadata();
                Texture mainTex = r.sharedMaterial?.mainTexture;
                if (mainTex != null)
                    _currentMetadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mainTex));
            }

            // Start new preview session for the 4 adjustment tabs. Scene Paint tab has its
            // own session and is started on-demand via ScenePaintState.Activate.
            if (_editTab != 4)
                _session.Begin(r, _avatarRoot);

            // Reset transient slider state so preview starts at "no change"
            ResetAdjustmentParameters();
        }

        private void ResetAdjustmentParameters()
        {
            _hueShift = 0f;
            _satScale = 1f;
            _valScale = 1f;
            _brightness = 0f;
            _contrast = 0f;
            _gradStartT = 0f;
            _gradEndT = 1f;
        }

        // ─── Live preview helpers ───

        /// <summary>
        /// Return the island indices list that should be used as the scope of the current
        /// preview/commit. If the user has selected one or more islands in the UV/Scene editor,
        /// scope = those islands; otherwise null (meaning "whole texture").
        /// </summary>
        private List<int> GetPreviewIslandScope()
        {
            if (_selectedIslandIndices.Count == 0) return null;
            var list = new List<int>(_selectedIslandIndices);
            list.Sort();
            return list;
        }

        private void RecomputePreviewForCurrentTab()
        {
            if (!_session.IsActive || _session.BaselinePixels == null) return;
            if (_editTab == 4) return; // Scene Paint has its own system

            var scopeRaw = GetPreviewIslandScope();
            // For HSV/BC/paint/gradient, an empty scope list means "apply everywhere".
            // TextureEditCore treats null/empty lists as "no island filter".
            List<int> scopeForHSV = scopeRaw; // HSV/BC read targetIslandIndices as optional filter

            Color[] newPixels = null;
            switch (_editTab)
            {
                case 0: // Paint — single color fill via Gradient with from==to, replace
                    newPixels = ComputePaintPreview(scopeRaw);
                    break;
                case 1: // Gradient
                    newPixels = ComputeGradientPreview(scopeRaw);
                    break;
                case 2: // HSV
                    newPixels = TextureEditCore.ComputeHSV(
                        _session.BaselinePixels, _session.Width, _session.Height,
                        _session.CachedMesh, scopeForHSV, _session.CachedIslands,
                        _hueShift, _satScale, _valScale);
                    break;
                case 3: // Brightness / Contrast
                    newPixels = TextureEditCore.ComputeBrightnessContrast(
                        _session.BaselinePixels, _session.Width, _session.Height,
                        _session.CachedMesh, scopeForHSV, _session.CachedIslands,
                        _brightness, _contrast);
                    break;
            }

            if (newPixels != null)
                _session.ApplyPreview(newPixels);
        }

        private Color[] ComputePaintPreview(List<int> scopeRaw)
        {
            // Single-color fill = Gradient with from==to, blend=replace
            List<int> targets = scopeRaw;
            if (targets == null || targets.Count == 0)
            {
                targets = new List<int>();
                if (_session.CachedIslands != null)
                    for (int i = 0; i < _session.CachedIslands.Count; i++) targets.Add(i);
            }
            return TextureEditCore.ComputeGradient(
                _session.BaselinePixels, _session.Width, _session.Height,
                _session.CachedMesh, targets, _session.CachedIslands, _session.CachedIslandGroups,
                _targetColor, _targetColor, 1, false, "replace", 0f, 1f);
        }

        private Color[] ComputeGradientPreview(List<int> scopeRaw)
        {
            List<int> targets = scopeRaw;
            if (targets == null || targets.Count == 0)
            {
                targets = new List<int>();
                if (_session.CachedIslands != null)
                    for (int i = 0; i < _session.CachedIslands.Count; i++) targets.Add(i);
            }
            int axis; bool invert;
            switch (DirectionValues[_gradDirectionIdx])
            {
                case "top_to_bottom": axis = 1; invert = true; break;
                case "bottom_to_top": axis = 1; invert = false; break;
                case "left_to_right": axis = 0; invert = false; break;
                case "right_to_left": axis = 0; invert = true; break;
                default: axis = 1; invert = true; break;
            }
            return TextureEditCore.ComputeGradient(
                _session.BaselinePixels, _session.Width, _session.Height,
                _session.CachedMesh, targets, _session.CachedIslands, _session.CachedIslandGroups,
                _gradFrom, _gradTo, axis, invert, BlendModeValues[_gradBlendModeIdx],
                _gradStartT, _gradEndT);
        }

        /// <summary>
        /// Commit the current preview session: save to disk, point material at saved tex,
        /// then propagate the same pixel transformation to any other checked renderers via
        /// the existing TextureEditTools path (so batch edits still work).
        /// </summary>
        private void CommitPreviewAndBatch()
        {
            if (!_session.IsActive) { Debug.LogWarning("[MeshPainter] No active session."); return; }
            if (!_session.HasUncommittedChanges)
            {
                Debug.Log(M("[MeshPainter] 変更がありません (既にコミット済み)"));
                return;
            }

            // 1. Commit active renderer via session
            bool ok = _session.Commit();
            if (!ok)
            {
                EditorUtility.DisplayDialog(M("エラー"), M("コミットに失敗しました。Console を確認してください。"), "OK");
                return;
            }

            // 2. Propagate to other checked renderers via existing disk-write path.
            //    Uses the same parameters the live preview used, so behavior stays consistent.
            foreach (var r in GetTargetRenderers())
            {
                if (r == _activeRenderer) continue;
                string path = GetGameObjectPath(r);
                string scopeStr = GetSelectedIslandIndicesString();
                switch (_editTab)
                {
                    case 0:
                        {
                            string hex = ColorToHex(_targetColor);
                            TextureEditTools.ApplyGradientEx(path, hex, hex, "top_to_bottom", "replace", scopeStr, 0f, 1f);
                            break;
                        }
                    case 1:
                        {
                            string from = ColorToHex(_gradFrom);
                            string to = ColorToHex(_gradTo);
                            TextureEditTools.ApplyGradientEx(path, from, to,
                                DirectionValues[_gradDirectionIdx],
                                BlendModeValues[_gradBlendModeIdx],
                                scopeStr, _gradStartT, _gradEndT);
                            break;
                        }
                    case 2:
                        TextureEditTools.AdjustHSV(path, _hueShift, _satScale, _valScale, scopeStr);
                        break;
                    case 3:
                        TextureEditTools.AdjustBrightnessContrast(path, _brightness, _contrast, scopeStr);
                        break;
                }
            }

            SceneView.RepaintAll();
            Repaint();
        }

        // ══════════════════════════════════════
        // Utilities
        // ══════════════════════════════════════

        private static string ColorToHex(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255);
            int g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255);
            int b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255);
            int a = Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255);
            return a < 255 ? $"#{r:X2}{g:X2}{b:X2}{a:X2}" : $"#{r:X2}{g:X2}{b:X2}";
        }

        private void RefreshRendererList()
        {
            _rendererList.Clear();
            if (_avatarRoot == null) return;
            foreach (var r in _avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                _rendererList.Add(new RendererData
                {
                    renderer = r,
                    displayName = r.gameObject.name,
                    fullPath = GetRelativePath(r.transform, _avatarRoot.transform),
                    isChecked = false
                });
            }
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root) { path = current.name + "/" + path; current = current.parent; }
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
            Matrix4x4 ltw = r.transform.localToWorldMatrix;
            Vector3[] verts = (r is SkinnedMeshRenderer smr) ? BakeSMR(smr) : mesh.vertices;
            Vector3[] world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) world[i] = ltw.MultiplyPoint3x4(verts[i]);
            return world;
        }

        private static Vector3[] BakeSMR(SkinnedMeshRenderer smr)
        {
            Mesh baked = new Mesh();
            smr.BakeMesh(baked);
            return baked.vertices;
        }

        private static GameObject AutoDetectAvatarRoot(GameObject obj)
        {
            Transform current = obj.transform;
            GameObject bestRoot = null;
            while (current != null)
            {
                if (current.GetComponent("VRCAvatarDescriptor") != null || current.GetComponent("VRC_AvatarDescriptor") != null)
                    return current.gameObject;
                if (current.GetComponent<Animator>() != null) bestRoot = current.gameObject;
                current = current.parent;
            }
            return bestRoot;
        }

        private static bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
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
