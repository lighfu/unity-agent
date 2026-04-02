#if AVATAR_OPTIMIZER
using Anatawa12.AvatarOptimizer;
#endif
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.Atlas;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class TextureAtlasWindow : EditorWindow
    {
        // ─── State ───
        private GameObject _avatarRoot;
        private Renderer _selectedRenderer;
        private int _rendererPopupIndex;
        private string[] _rendererNames = new string[0];
        private Renderer[] _renderers = new Renderer[0];

        private List<MaterialSlotInfo> _materials = new List<MaterialSlotInfo>();
        private bool[] _materialSelected = new bool[0];
        private AtlasLayout _previewLayout;

        // ─── Settings ───
        private bool _settingsFoldout = true;
        private int _maxAtlasSizeIndex = 2; // 0=1024, 1=2048, 2=4096
        private static readonly int[] AtlasSizeOptions = { 1024, 2048, 4096 };
        private static readonly string[] AtlasSizeLabels = { "1024", "2048", "4096" };
        private int _padding = 4;
        private bool _bakeLilToonColor = true;
        private int _uniformTileSize;

        // ─── Scroll ───
        private Vector2 _mainScrollPos;
        private Vector2 _materialListScrollPos;

        // ─── Result ───
        private string _resultMessage;

        // ─── Preview colors ───
        private static readonly Color[] RectColors =
        {
            new Color(0.35f, 0.65f, 0.95f, 0.6f),
            new Color(0.95f, 0.55f, 0.35f, 0.6f),
            new Color(0.45f, 0.85f, 0.45f, 0.6f),
            new Color(0.90f, 0.45f, 0.85f, 0.6f),
            new Color(0.95f, 0.85f, 0.35f, 0.6f),
            new Color(0.55f, 0.85f, 0.85f, 0.6f),
            new Color(0.75f, 0.55f, 0.35f, 0.6f),
            new Color(0.65f, 0.65f, 0.95f, 0.6f),
        };

        [MenuItem("Window/紫陽花広場/Texture Atlas")]
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
            GetWindow<TextureAtlasWindow>(M("テクスチャアトラス"));
        }

#if AVATAR_OPTIMIZER
        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;

            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null && root != _avatarRoot)
            {
                _avatarRoot = root;
                RefreshRendererList();
            }

            // Auto-select renderer if has one
            var renderer = Selection.activeGameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == renderer)
                    {
                        _rendererPopupIndex = i;
                        SelectRenderer(renderer);
                        break;
                    }
                }
            }

            Repaint();
        }
#endif

        private void OnGUI()
        {
            try { DrawMainGUI(); }
            catch (System.ArgumentException) { }
        }

        private void DrawMainGUI()
        {
#if !AVATAR_OPTIMIZER
            EditorGUILayout.HelpBox(
                M("Avatar Optimizer がインストールされていません。\nこの機能には AAO (com.anatawa12.avatar-optimizer) が必要です。"),
                MessageType.Error);
            return;
#else
            _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 8, 8) });

            GUILayout.Label(M("テクスチャアトラス"), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Avatar root
            _avatarRoot = (GameObject)EditorGUILayout.ObjectField(
                M("アバタールート"), _avatarRoot, typeof(GameObject), true);

            if (_avatarRoot == null)
            {
                EditorGUILayout.HelpBox(
                    M("アバターのルートオブジェクトを選択してください。\nヒエラルキーでアバターを選択すると自動検出されます。"),
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }

            // Renderer selector
            DrawRendererSelector();

            if (_selectedRenderer == null)
            {
                EditorGUILayout.HelpBox(M("対象レンダラーを選択してください。"), MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }

            // Summary
            EditorGUILayout.Space(4);
            long totalVRAM = AtlasAnalyzer.EstimateTextureMemory(_materials);
            EditorGUILayout.LabelField(
                $"{M("マテリアル数")}: {_materials.Count}    {M("テクスチャメモリ")}: {FormatBytes(totalVRAM)}",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Selection buttons
            DrawSelectionButtons();

            // Material list
            DrawMaterialList();

            // Settings
            EditorGUILayout.Space(8);
            DrawSettings();

            // Preview
            EditorGUILayout.Space(8);
            DrawPreview();

            // Actions
            EditorGUILayout.Space(8);
            DrawActions();

            // Result
            if (!string.IsNullOrEmpty(_resultMessage))
            {
                EditorGUILayout.Space(8);
                DrawResult();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
#endif
        }

#if AVATAR_OPTIMIZER

        // ─── Renderer Selector ───

        private void DrawRendererSelector()
        {
            if (_rendererNames.Length == 0)
                RefreshRendererList();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _rendererPopupIndex = EditorGUILayout.Popup(
                M("対象レンダラー"), _rendererPopupIndex, _rendererNames);
            if (EditorGUI.EndChangeCheck() && _rendererPopupIndex < _renderers.Length)
                SelectRenderer(_renderers[_rendererPopupIndex]);

            if (GUILayout.Button("↻", GUILayout.Width(25)))
                RefreshRendererList();
            EditorGUILayout.EndHorizontal();
        }

        // ─── Selection Buttons ───

        private void DrawSelectionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(M("全選択"), EditorStyles.miniButtonLeft))
            {
                for (int i = 0; i < _materialSelected.Length; i++)
                {
                    if (!_materials[i].HasUVOutOfRange)
                        _materialSelected[i] = true;
                }
                UpdatePreviewLayout();
            }

            if (GUILayout.Button(M("全解除"), EditorStyles.miniButtonMid))
            {
                for (int i = 0; i < _materialSelected.Length; i++)
                    _materialSelected[i] = false;
                UpdatePreviewLayout();
            }

            if (GUILayout.Button(M("同シェーダー選択"), EditorStyles.miniButtonRight))
            {
                SelectSameShaderFamily();
                UpdatePreviewLayout();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Material List ───

        private void DrawMaterialList()
        {
            int selectedCount = 0;
            for (int i = 0; i < _materialSelected.Length; i++)
                if (_materialSelected[i]) selectedCount++;
            EditorGUILayout.LabelField(
                string.Format(M("{0}/{1} マテリアル選択中"), selectedCount, _materials.Count),
                EditorStyles.miniLabel);

            float listHeight = Mathf.Min(_materials.Count * 60 + 10, 240);
            _materialListScrollPos = EditorGUILayout.BeginScrollView(
                _materialListScrollPos, GUILayout.Height(listHeight));

            for (int i = 0; i < _materials.Count; i++)
            {
                var info = _materials[i];
                bool hasWarning = info.HasUVOutOfRange;

                EditorGUILayout.BeginHorizontal(
                    _materialSelected[i] ? EditorStyles.selectionRect : EditorStyles.label);

                // Checkbox
                EditorGUI.BeginDisabledGroup(hasWarning);
                EditorGUI.BeginChangeCheck();
                _materialSelected[i] = EditorGUILayout.Toggle(_materialSelected[i], GUILayout.Width(18));
                if (EditorGUI.EndChangeCheck())
                    UpdatePreviewLayout();
                EditorGUI.EndDisabledGroup();

                // Thumbnail
                if (info.Material != null)
                {
                    var preview = AssetPreview.GetAssetPreview(info.Material);
                    if (preview != null)
                        GUILayout.Label(preview, GUILayout.Width(40), GUILayout.Height(40));
                    else
                        GUILayout.Label("", GUILayout.Width(40), GUILayout.Height(40));
                }

                // Info
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(
                    $"#{info.MaterialIndex} {info.MaterialName}    {info.ShaderName}    " +
                    $"{info.TextureWidth}x{info.TextureHeight}    SM:{info.SubmeshIndex}",
                    EditorStyles.miniLabel);

                var details = new StringBuilder();
                if (info.MainTex != null)
                    details.Append($"MainTex: {info.MainTex.name}  ");
                if (info.NormalMap != null)
                    details.Append($"Normal: {info.NormalMap.name}  ");
                if (info.EmissionMap != null)
                    details.Append($"Emission: {info.EmissionMap.name}");
                if (details.Length > 0)
                    EditorGUILayout.LabelField(details.ToString(), EditorStyles.miniLabel);

                // Warnings
                if (info.HasUVOutOfRange)
                    EditorGUILayout.LabelField(M("⚠ UV範囲外の頂点あり（アトラス不可）"),
                        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.yellow } });
                if (info.IsSharedAcrossRenderers)
                    EditorGUILayout.LabelField(M("⚠ 他レンダラーと共有（前処理時に複製）"),
                        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.7f, 0.3f) } });
                if (info.MainTex == null)
                    EditorGUILayout.LabelField(M("⚠ テクスチャなし（前処理で生成）"),
                        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.7f, 0.7f, 1f) } });

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Settings ───

        private void DrawSettings()
        {
            _settingsFoldout = EditorGUILayout.Foldout(_settingsFoldout, M("設定"), true);
            if (!_settingsFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            _maxAtlasSizeIndex = EditorGUILayout.Popup(
                M("最大アトラスサイズ"), _maxAtlasSizeIndex, AtlasSizeLabels);
            if (EditorGUI.EndChangeCheck())
                UpdatePreviewLayout();

            _padding = EditorGUILayout.IntField(M("パディング (px)"), _padding);
            _padding = Mathf.Clamp(_padding, 0, 32);

            _bakeLilToonColor = EditorGUILayout.Toggle(M("lilToon _Colorベイク"), _bakeLilToonColor);

            EditorGUI.BeginChangeCheck();
            _uniformTileSize = EditorGUILayout.IntField(M("統一サイズ (0=自動)"), _uniformTileSize);
            _uniformTileSize = Mathf.Max(0, _uniformTileSize);
            if (EditorGUI.EndChangeCheck())
                UpdatePreviewLayout();

            EditorGUI.indentLevel--;
        }

        // ─── Preview ───

        private void DrawPreview()
        {
            GUILayout.Label(M("プレビュー"), EditorStyles.boldLabel);

            if (_previewLayout == null || _previewLayout.Rects.Count == 0)
            {
                EditorGUILayout.HelpBox(M("マテリアルを選択するとプレビューが表示されます。"), MessageType.Info);
                return;
            }

            // Info
            int selectedCount = _materialSelected.Count(s => s);
            int totalCount = _materials.Count;
            EditorGUILayout.LabelField(
                $"{M("アトラス")}: {_previewLayout.AtlasWidth}x{_previewLayout.AtlasHeight}    " +
                $"{M("効率")}: {_previewLayout.Efficiency * 100:F1}%    " +
                $"{M("選択")}: {selectedCount} {M("マテリアル")}    " +
                $"{M("結果")}: {totalCount}→{totalCount - selectedCount + 1} {M("マテリアル")}",
                EditorStyles.miniLabel);

            // GL preview rect
            float previewSize = Mathf.Min(position.width - 32, 300);
            Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);

            if (Event.current.type == EventType.Repaint)
                DrawPreviewGL(previewRect);
        }

        // ─── GL Drawing ───

        private static Material _glMaterial;
        private static Material GLMaterial
        {
            get
            {
                if (_glMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    _glMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _glMaterial.SetInt("_Cull", 0);
                    _glMaterial.SetInt("_ZWrite", 0);
                }
                return _glMaterial;
            }
        }

        private void DrawPreviewGL(Rect rect)
        {
            if (_previewLayout == null || _previewLayout.Rects.Count == 0) return;

            float aw = _previewLayout.AtlasWidth;
            float ah = _previewLayout.AtlasHeight;
            if (aw <= 0 || ah <= 0) return;

            // Background
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));

            GUI.BeginClip(rect);
            GL.PushMatrix();
            GLMaterial.SetPass(0);

            float scaleX = rect.width / aw;
            float scaleY = rect.height / ah;
            float scale = Mathf.Min(scaleX, scaleY);
            float offsetX = (rect.width - aw * scale) * 0.5f;
            float offsetY = (rect.height - ah * scale) * 0.5f;

            // Draw padding background
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0.25f, 0.25f, 0.25f, 1f));
            GL.Vertex3(offsetX, offsetY, 0);
            GL.Vertex3(offsetX + aw * scale, offsetY, 0);
            GL.Vertex3(offsetX + aw * scale, offsetY + ah * scale, 0);
            GL.Vertex3(offsetX, offsetY + ah * scale, 0);
            GL.End();

            // Draw rects
            for (int i = 0; i < _previewLayout.Rects.Count; i++)
            {
                var r = _previewLayout.Rects[i];
                Color color = RectColors[i % RectColors.Length];

                float x = offsetX + r.PackedX * scale;
                float y = offsetY + r.PackedY * scale;
                float w = r.PackedWidth * scale;
                float h = r.PackedHeight * scale;

                // Filled rect
                GL.Begin(GL.QUADS);
                GL.Color(color);
                GL.Vertex3(x, y, 0);
                GL.Vertex3(x + w, y, 0);
                GL.Vertex3(x + w, y + h, 0);
                GL.Vertex3(x, y + h, 0);
                GL.End();

                // Border
                GL.Begin(GL.LINES);
                GL.Color(new Color(color.r, color.g, color.b, 1f));
                GL.Vertex3(x, y, 0); GL.Vertex3(x + w, y, 0);
                GL.Vertex3(x + w, y, 0); GL.Vertex3(x + w, y + h, 0);
                GL.Vertex3(x + w, y + h, 0); GL.Vertex3(x, y + h, 0);
                GL.Vertex3(x, y + h, 0); GL.Vertex3(x, y, 0);
                GL.End();
            }

            GL.PopMatrix();
            GUI.EndClip();

            // Draw material name labels (after GL, using GUI)
            for (int i = 0; i < _previewLayout.Rects.Count; i++)
            {
                var r = _previewLayout.Rects[i];
                var info = _materials.FirstOrDefault(m => m.MaterialIndex == r.MaterialIndex);
                if (info == null) continue;

                float scale2 = Mathf.Min(rect.width / aw, rect.height / ah);
                float ox = rect.x + (rect.width - aw * scale2) * 0.5f;
                float oy = rect.y + (rect.height - ah * scale2) * 0.5f;

                float lx = ox + r.PackedX * scale2 + 2;
                float ly = oy + r.PackedY * scale2 + 2;

                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = Color.white },
                    fontSize = 9
                };
                GUI.Label(new Rect(lx, ly, r.PackedWidth * scale2, 14), info.MaterialName, labelStyle);
            }
        }

        // ─── Actions ───

        private void DrawActions()
        {
            GUILayout.Label(M("アクション"), EditorStyles.boldLabel);

            var selectedIndices = GetSelectedIndices();
            EditorGUI.BeginDisabledGroup(selectedIndices.Count == 0);

            // Step 1: Preprocess
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("ステップ 1:"), GUILayout.Width(70));
            if (GUILayout.Button(M("テクスチャ前処理")))
                ExecutePreprocess();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(M("  (_Color ベイク、リサイズ)"), EditorStyles.miniLabel);

            EditorGUILayout.Space(2);

            // Step 2: AAO setup
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("ステップ 2:"), GUILayout.Width(70));
            if (GUILayout.Button(M("AAO コンポーネント設定")))
                ExecuteAAOSetup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(M("  (MergeSkinnedMesh + TraceAndOptimize)"), EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Separator
            var separatorRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(separatorRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space(4);

            // Combined
            if (GUILayout.Button(M("一括実行（前処理 + AAO設定）"), GUILayout.Height(30)))
            {
                ExecutePreprocess();
                ExecuteAAOSetup();
            }

            EditorGUI.EndDisabledGroup();
        }

        // ─── Result ───

        private void DrawResult()
        {
            EditorGUILayout.LabelField(M("結果"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_resultMessage, MessageType.Info);
        }

        // ─── Execution ───

        private void ExecutePreprocess()
        {
            if (_selectedRenderer == null) return;
            var indices = GetSelectedIndices();
            if (indices.Count == 0) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Atlas Preprocess");

            var sb = new StringBuilder();

            // Ensure textures
            foreach (int idx in indices)
            {
                string result = AtlasPreprocessor.EnsureTextures(_selectedRenderer, idx);
                if (result.StartsWith("OK"))
                    sb.AppendLine(result);
            }

            // Bake _Color
            if (_bakeLilToonColor)
            {
                foreach (int idx in indices)
                {
                    string result = AtlasPreprocessor.BakeLilToonColor(_selectedRenderer, idx);
                    if (!result.StartsWith("Skip"))
                        sb.AppendLine(result);
                }
            }

            // Resize
            if (_uniformTileSize > 0)
            {
                string result = AtlasPreprocessor.ResizeTextures(_selectedRenderer, indices, _uniformTileSize);
                sb.AppendLine(result);
            }

            Undo.CollapseUndoOperations(undoGroup);

            sb.AppendLine(M("テクスチャ前処理完了。"));
            _resultMessage = sb.ToString().TrimEnd();

            // Refresh material analysis
            AnalyzeMaterials();
            Repaint();
        }

        private void ExecuteAAOSetup()
        {
            if (_avatarRoot == null || _selectedRenderer == null) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Atlas AAO Setup");

            var sb = new StringBuilder();

            // TraceAndOptimize on avatar root
            var existingTAO = _avatarRoot.GetComponent<TraceAndOptimize>();
            if (existingTAO == null)
            {
                var tao = Undo.AddComponent<TraceAndOptimize>(_avatarRoot);
                var so = new SerializedObject(tao);

                var mergeSkinnedMeshProp = so.FindProperty("mergeSkinnedMesh");
                if (mergeSkinnedMeshProp != null) mergeSkinnedMeshProp.boolValue = true;

                var optimizeTexProp = so.FindProperty("optimizeTexture");
                if (optimizeTexProp != null) optimizeTexProp.boolValue = true;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(tao);

                sb.AppendLine(M("✓ TraceAndOptimize 追加済み"));
            }
            else
            {
                var so = new SerializedObject(existingTAO);
                bool changed = false;

                var mergeSkinnedMeshProp = so.FindProperty("mergeSkinnedMesh");
                if (mergeSkinnedMeshProp != null && !mergeSkinnedMeshProp.boolValue)
                {
                    Undo.RecordObject(existingTAO, "Enable mergeSkinnedMesh");
                    mergeSkinnedMeshProp.boolValue = true;
                    changed = true;
                }

                var optimizeTexProp = so.FindProperty("optimizeTexture");
                if (optimizeTexProp != null && !optimizeTexProp.boolValue)
                {
                    Undo.RecordObject(existingTAO, "Enable optimizeTexture");
                    optimizeTexProp.boolValue = true;
                    changed = true;
                }

                if (changed)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(existingTAO);
                    sb.AppendLine(M("✓ TraceAndOptimize 設定更新済み"));
                }
                else
                {
                    sb.AppendLine(M("✓ TraceAndOptimize 既に設定済み"));
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            sb.AppendLine(M("✓ ビルド時にアトラス化されます"));
            sb.AppendLine(M("→ VRChat SDK でビルドして結果を確認してください"));

            _resultMessage = (_resultMessage ?? "") + "\n" + sb.ToString().TrimEnd();
            Repaint();
        }

        // ─── Helpers ───

        private void RefreshRendererList()
        {
            if (_avatarRoot == null)
            {
                _rendererNames = new string[0];
                _renderers = new Renderer[0];
                return;
            }

            var allRenderers = _avatarRoot.GetComponentsInChildren<Renderer>(true);
            _renderers = allRenderers;
            _rendererNames = allRenderers.Select(r => r.gameObject.name).ToArray();

            if (_rendererNames.Length > 0 && _selectedRenderer == null)
            {
                _rendererPopupIndex = 0;
                SelectRenderer(_renderers[0]);
            }
        }

        private void SelectRenderer(Renderer renderer)
        {
            _selectedRenderer = renderer;
            AnalyzeMaterials();
        }

        private void AnalyzeMaterials()
        {
            if (_selectedRenderer == null)
            {
                _materials.Clear();
                _materialSelected = new bool[0];
                _previewLayout = null;
                return;
            }

            _materials = AtlasAnalyzer.Analyze(_selectedRenderer);

            var mesh = GetMesh(_selectedRenderer);
            if (mesh != null)
                AtlasAnalyzer.CheckUVRanges(mesh, _materials);
            AtlasAnalyzer.CheckSharedMaterials(_selectedRenderer, _materials);

            _materialSelected = new bool[_materials.Count];
            _previewLayout = null;
            _resultMessage = null;
        }

        private void SelectSameShaderFamily()
        {
            // Find the most common shader family among selected, or the first if none selected
            var groups = AtlasAnalyzer.GroupByShaderFamily(_materials);
            string targetFamily = null;

            // Find first selected material's family
            for (int i = 0; i < _materialSelected.Length; i++)
            {
                if (_materialSelected[i])
                {
                    targetFamily = GetShaderFamily(_materials[i].ShaderName);
                    break;
                }
            }

            // If nothing selected, use the largest group
            if (targetFamily == null && groups.Count > 0)
                targetFamily = groups.OrderByDescending(g => g.Value.Count).First().Key;

            if (targetFamily == null) return;

            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i].HasUVOutOfRange) continue;
                _materialSelected[i] = GetShaderFamily(_materials[i].ShaderName) == targetFamily;
            }
        }

        private void UpdatePreviewLayout()
        {
            var indices = GetSelectedIndices();
            if (indices.Count == 0)
            {
                _previewLayout = null;
                return;
            }

            int maxSize = AtlasSizeOptions[_maxAtlasSizeIndex];
            var items = new List<(int materialIndex, int width, int height)>();

            foreach (int idx in indices)
            {
                var info = _materials.FirstOrDefault(m => m.MaterialIndex == idx);
                if (info == null) continue;

                int w = _uniformTileSize > 0 ? _uniformTileSize : info.TextureWidth;
                int h = _uniformTileSize > 0 ? _uniformTileSize : info.TextureHeight;
                items.Add((idx, w, h));
            }

            _previewLayout = MaxRectsPacker.Pack(items, _padding, maxSize);
        }

        private List<int> GetSelectedIndices()
        {
            var result = new List<int>();
            for (int i = 0; i < _materialSelected.Length; i++)
            {
                if (_materialSelected[i])
                    result.Add(_materials[i].MaterialIndex);
            }
            return result;
        }

        private static string GetShaderFamily(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return "Unknown";
            if (shaderName.StartsWith("lil/")) return "lilToon";
            if (shaderName.StartsWith("Standard")) return "Standard";
            if (shaderName.Contains("UnityChanToonShader") || shaderName.Contains("UTS")) return "UTS";
            if (shaderName.Contains("Poiyomi") || shaderName.Contains(".poyi")) return "Poiyomi";
            return shaderName;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
                return smr.sharedMesh;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static GameObject AutoDetectAvatarRoot(GameObject obj)
        {
            if (obj == null) return null;
            Transform current = obj.transform;
            GameObject bestRoot = null;
            while (current != null)
            {
                if (current.GetComponent("VRCAvatarDescriptor") != null ||
                    current.GetComponent("VRC_AvatarDescriptor") != null)
                    return current.gameObject;
                if (current.GetComponent<Animator>() != null)
                    bestRoot = current.gameObject;
                current = current.parent;
            }
            return bestRoot;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

#endif
    }
}
