#if AVATAR_OPTIMIZER
using Anatawa12.AvatarOptimizer;
#endif
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// アバター最適化を 1 画面で行う統合ウィンドウ (MD3SDK / UI Toolkit ベース)。
    /// 解析・AAO TraceAndOptimize 設定・NDMF MeshSimplifier・テクスチャ最適化推奨をまとめて操作する。
    /// </summary>
    public class AvatarOptimizerWindow : EditorWindow
    {
        private const string MenuPath = "UnityAgent/Avatar Optimizer";

        // ─── Theme ───
        private MD3Theme _theme;

        // ─── Domain state ───
        private GameObject _avatarRoot;
        private SkinnedMeshRenderer _simplifierTarget;
        private string _resultLog = string.Empty;

        // AAO toggles (mirrors TraceAndOptimize SerializedProperties)
        private bool _aaoOptimizeBlendShape = true;
        private bool _aaoRemoveUnusedObjects = true;
        private bool _aaoPreserveEndBone;
        private bool _aaoOptimizePhysBone = true;
        private bool _aaoOptimizeAnimator = true;
        private bool _aaoMergeSkinnedMesh;
        private bool _aaoOptimizeTexture;
        private bool _aaoMmdWorldCompatibility = true;

        // Mesh simplifier
        private float _simplifierQuality = 0.5f;
        // Texture optimization
        private float _vramTargetMB = 100f;

        // ─── UI element refs that need to update ───
        private ObjectField _avatarRootField;
        private VisualElement _aaoBody;          // dynamic content under AAO card
        private VisualElement _simplifierBody;
        private Label _resultLabel;

        [MenuItem(MenuPath)]
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
            var window = GetWindow<AvatarOptimizerWindow>();
            window.titleContent = new GUIContent(M("アバター最適化"));
            window.minSize = new Vector2(440, 600);
        }

        private void OnSelectionChange()
        {
            if (TryAutoDetectFromSelection())
                SyncAvatarRootField();
        }

        private bool TryAutoDetectFromSelection()
        {
            var sel = Selection.activeGameObject;
            if (sel == null) return false;

            var root = AutoDetectAvatarRoot(sel);
            if (root == null || root == _avatarRoot) return false;

            ApplyAvatarRoot(root);
            return true;
        }

        private void ApplyAvatarRoot(GameObject root)
        {
            _avatarRoot = root;
            LoadAAOSettingsFromComponent();
            RebuildAAOBody();
            RebuildSimplifierBody();
        }

        private void SyncAvatarRootField()
        {
            if (_avatarRootField != null)
                _avatarRootField.SetValueWithoutNotify(_avatarRoot);
        }

        // ─── UI build ───

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            _theme = MD3Theme.Auto();
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !root.styleSheets.Contains(themeSheet))
                root.styleSheets.Add(themeSheet);
            if (compSheet != null && !root.styleSheets.Contains(compSheet))
                root.styleSheets.Add(compSheet);
            _theme.ApplyTo(root);

            root.style.flexGrow = 1;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            scroll.Add(BuildHeaderCard());
            scroll.Add(BuildAnalysisCard());
            scroll.Add(BuildAAOCard());
            scroll.Add(BuildSimplifierCard());
            scroll.Add(BuildTextureCard());
            scroll.Add(BuildResultCard());

            // Initial sync from current Selection
            TryAutoDetectFromSelection();
            SyncAvatarRootField();
        }

        // ─── Header (avatar selector) ───

        private VisualElement BuildHeaderCard()
        {
            var card = NewCard(M("対象アバター"), null, MD3CardStyle.Filled);

            card.Add(new MD3Text(
                M("選択中のアバターに対して、解析・AAO 設定・メッシュ簡易化・テクスチャ最適化をまとめて操作できます。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            _avatarRootField = new ObjectField(M("アバター ルート"))
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = _avatarRoot,
            };
            _avatarRootField.style.marginTop = 6;
            _avatarRootField.RegisterValueChangedCallback(evt =>
            {
                var go = evt.newValue as GameObject;
                if (go == _avatarRoot) return;
                _avatarRoot = go;
                LoadAAOSettingsFromComponent();
                RebuildAAOBody();
                RebuildSimplifierBody();
            });
            card.Add(_avatarRootField);

            var detectBtn = new MD3Button(
                M("選択中の GameObject から自動検出"),
                MD3ButtonStyle.Outlined,
                size: MD3ButtonSize.Small);
            detectBtn.style.marginTop = 6;
            detectBtn.style.alignSelf = Align.FlexStart;
            detectBtn.clicked += () =>
            {
                var sel = Selection.activeGameObject;
                if (sel == null)
                {
                    ShowNotification(new GUIContent(M("Hierarchy で GameObject を選択してください")));
                    return;
                }
                var root = AutoDetectAvatarRoot(sel);
                if (root == null)
                {
                    ShowNotification(new GUIContent(M("Animator / VRCAvatarDescriptor が見つかりません")));
                    return;
                }
                ApplyAvatarRoot(root);
                SyncAvatarRootField();
            };
            card.Add(detectBtn);

            return card;
        }

        // ─── 1. Analysis ───

        private VisualElement BuildAnalysisCard()
        {
            var card = NewCard(M("1. パフォーマンス解析"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("Performance"), MD3ButtonStyle.Filled,
                () => InvokeWithAvatar(name =>
                    SetLog(AvatarPerformanceAnalyzer.AnalyzeAvatarPerformance(name)))));
            btnRow.Add(NewActionButton(M("Validate"), MD3ButtonStyle.Tonal,
                () => InvokeWithAvatar(name =>
                    SetLog(AvatarValidationTools.ValidateAvatar(name)))));
            btnRow.Add(NewActionButton(M("Texture VRAM"), MD3ButtonStyle.Tonal,
                () => InvokeWithAvatar(name =>
                    SetLog(TextureMemoryAnalysisTools.AnalyzeTextureMemory(name)))));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("VRC SDK 公式 AvatarPerformance + NDMF ParameterInfo によるビルド不要の解析 (シーン現在状態 + post-build パラメータ予測)。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 2. AAO TraceAndOptimize ───

        private VisualElement BuildAAOCard()
        {
            var card = NewCard(M("2. AAO Trace and Optimize"), null, MD3CardStyle.Outlined);

            _aaoBody = new VisualElement();
            card.Add(_aaoBody);

            RebuildAAOBody();
            return card;
        }

        private void RebuildAAOBody()
        {
            if (_aaoBody == null) return;
            _aaoBody.Clear();

#if !AVATAR_OPTIMIZER
            _aaoBody.Add(new MD3Banner(
                M("AAO (com.anatawa12.avatar-optimizer) がインストールされていません。VPM 経由でインストールしてください。"),
                MD3Icon.Info));
#else
            if (_avatarRoot == null)
            {
                _aaoBody.Add(NewHintText(M("アバター ルートを指定してください。")));
                return;
            }

            bool hasComponent = _avatarRoot.GetComponent<TraceAndOptimize>() != null;

            var statusRow = new MD3Row(MD3Spacing.S);
            statusRow.Add(new MD3Text(M("状態"), MD3TextStyle.LabelMedium,
                color: _theme.OnSurfaceVariant));
            statusRow.Add(new MD3Text(
                hasComponent ? M("追加済み") : M("未追加"),
                MD3TextStyle.LabelMedium,
                color: hasComponent ? _theme.Primary : _theme.OnSurfaceVariant));
            _aaoBody.Add(statusRow);

            if (!hasComponent)
            {
                var addBtn = new MD3Button(M("TraceAndOptimize を追加"), MD3ButtonStyle.Filled);
                addBtn.style.marginTop = 6;
                addBtn.style.alignSelf = Align.FlexStart;
                addBtn.clicked += () =>
                {
                    SetLog(AvatarOptimizerTools.AddTraceAndOptimize(_avatarRoot.name));
                    LoadAAOSettingsFromComponent();
                    RebuildAAOBody();
                };
                _aaoBody.Add(addBtn);
                return;
            }

            // 8 toggle switches
            _aaoBody.Add(BuildSwitchRow(M("BlendShape 最適化"), _aaoOptimizeBlendShape,
                v => _aaoOptimizeBlendShape = v));
            _aaoBody.Add(BuildSwitchRow(M("未使用オブジェクト削除"), _aaoRemoveUnusedObjects,
                v => _aaoRemoveUnusedObjects = v));
            _aaoBody.Add(BuildSwitchRow(M("End Bone を残す"), _aaoPreserveEndBone,
                v => _aaoPreserveEndBone = v));
            _aaoBody.Add(BuildSwitchRow(M("PhysBone 最適化"), _aaoOptimizePhysBone,
                v => _aaoOptimizePhysBone = v));
            _aaoBody.Add(BuildSwitchRow(M("Animator 最適化"), _aaoOptimizeAnimator,
                v => _aaoOptimizeAnimator = v));
            _aaoBody.Add(BuildSwitchRow(M("Skinned Mesh の自動統合"), _aaoMergeSkinnedMesh,
                v => _aaoMergeSkinnedMesh = v));
            _aaoBody.Add(BuildSwitchRow(M("Texture 最適化"), _aaoOptimizeTexture,
                v => _aaoOptimizeTexture = v));
            _aaoBody.Add(BuildSwitchRow(M("MMD ワールド互換"), _aaoMmdWorldCompatibility,
                v => _aaoMmdWorldCompatibility = v));

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.marginTop = 8;
            btnRow.style.flexWrap = Wrap.Wrap;
            btnRow.Add(new MD3Button(M("設定を保存"), MD3ButtonStyle.Filled).WithClick(() =>
            {
                SetLog(AvatarOptimizerTools.ConfigureTraceAndOptimize(_avatarRoot.name,
                    Bs(_aaoOptimizeBlendShape), Bs(_aaoRemoveUnusedObjects),
                    Bs(_aaoPreserveEndBone), Bs(_aaoOptimizePhysBone),
                    Bs(_aaoOptimizeAnimator), Bs(_aaoMergeSkinnedMesh),
                    Bs(_aaoOptimizeTexture), Bs(_aaoMmdWorldCompatibility)));
            }));
            btnRow.Add(new MD3Button(M("コンポーネントから読込"), MD3ButtonStyle.Outlined).WithClick(() =>
            {
                LoadAAOSettingsFromComponent();
                RebuildAAOBody();
                SetLog(AvatarOptimizerTools.ConfigureTraceAndOptimize(_avatarRoot.name));
            }));
            btnRow.Add(new MD3Button(M("AAO 構成を一覧"), MD3ButtonStyle.Text).WithClick(() =>
            {
                SetLog(AvatarOptimizerTools.ListAAOComponents(_avatarRoot.name));
            }));
            _aaoBody.Add(btnRow);
#endif
        }

        // ─── 3. NDMF Mesh Simplifier ───

        private VisualElement BuildSimplifierCard()
        {
            var card = NewCard(M("3. NDMF Mesh Simplifier"), null, MD3CardStyle.Outlined);

            _simplifierBody = new VisualElement();
            card.Add(_simplifierBody);

            RebuildSimplifierBody();
            return card;
        }

        private void RebuildSimplifierBody()
        {
            if (_simplifierBody == null) return;
            _simplifierBody.Clear();

#if !NDMF_MESH_SIMPLIFIER
            _simplifierBody.Add(new MD3Banner(
                M("NDMF Mesh Simplifier (jp.lilxyzw.ndmfmeshsimplifier) がインストールされていません。"),
                MD3Icon.Info));
#else
            if (_avatarRoot == null)
            {
                _simplifierBody.Add(NewHintText(M("アバター ルートを指定してください。")));
                return;
            }

            var smrField = new ObjectField(M("対象 SkinnedMeshRenderer"))
            {
                objectType = typeof(SkinnedMeshRenderer),
                allowSceneObjects = true,
                value = _simplifierTarget,
            };
            smrField.RegisterValueChangedCallback(evt =>
            {
                _simplifierTarget = evt.newValue as SkinnedMeshRenderer;
            });
            _simplifierBody.Add(smrField);

            _simplifierBody.Add(BuildSliderRow(
                M("品質 (残す三角形比率)"),
                _simplifierQuality, 0.05f, 1.0f,
                v => _simplifierQuality = v,
                v => v.ToString("F2")));

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.marginTop = 6;
            btnRow.Add(new MD3Button(M("追加 / 上書き"), MD3ButtonStyle.Filled).WithClick(() =>
            {
                if (_simplifierTarget == null)
                {
                    ShowNotification(new GUIContent(M("対象 SkinnedMeshRenderer を指定してください")));
                    return;
                }
                SetLog(MeshSimplifierTools.AddMeshSimplifier(_simplifierTarget.name, _simplifierQuality));
            }));
            btnRow.Add(new MD3Button(M("除去"), MD3ButtonStyle.Outlined).WithClick(() =>
            {
                if (_simplifierTarget == null)
                {
                    ShowNotification(new GUIContent(M("対象 SkinnedMeshRenderer を指定してください")));
                    return;
                }
                SetLog(MeshSimplifierTools.RemoveMeshSimplifier(_simplifierTarget.name));
            }));
            btnRow.Add(new MD3Button(M("一覧"), MD3ButtonStyle.Text).WithClick(() =>
            {
                SetLog(MeshSimplifierTools.ListMeshSimplifiers(_avatarRoot.name));
            }));
            _simplifierBody.Add(btnRow);
#endif
        }

        // ─── 4. Texture optimization ───

        private VisualElement BuildTextureCard()
        {
            var card = NewCard(M("4. テクスチャ最適化"), null, MD3CardStyle.Outlined);

            card.Add(BuildSliderRow(
                M("目標 VRAM (MB, 0 = 目標なし)"),
                _vramTargetMB, 0f, 500f,
                v => _vramTargetMB = v,
                v => Mathf.RoundToInt(v).ToString()));

            var runBtn = new MD3Button(M("最適化推奨を取得"), MD3ButtonStyle.Filled);
            runBtn.style.marginTop = 6;
            runBtn.style.alignSelf = Align.FlexStart;
            runBtn.clicked += () => InvokeWithAvatar(name =>
                SetLog(TextureMemoryAnalysisTools.GetTextureOptimizationRecommendations(
                    name, _vramTargetMB)));
            card.Add(runBtn);

            return card;
        }

        // ─── Result log ───

        private VisualElement BuildResultCard()
        {
            var card = NewCard(M("結果ログ"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.Add(new MD3Button(M("クリア"), MD3ButtonStyle.Outlined, size: MD3ButtonSize.Small)
                .WithClick(() => SetLog(string.Empty)));
            btnRow.Add(new MD3Button(M("クリップボードにコピー"), MD3ButtonStyle.Outlined, size: MD3ButtonSize.Small)
                .WithClick(() => EditorGUIUtility.systemCopyBuffer = _resultLog ?? string.Empty));
            card.Add(btnRow);

            var logScroll = new ScrollView(ScrollViewMode.Vertical);
            logScroll.style.minHeight = 140;
            logScroll.style.maxHeight = 280;
            logScroll.style.marginTop = 6;
            logScroll.style.backgroundColor = _theme.SurfaceContainerLowest;
            logScroll.style.borderTopLeftRadius = 6;
            logScroll.style.borderTopRightRadius = 6;
            logScroll.style.borderBottomLeftRadius = 6;
            logScroll.style.borderBottomRightRadius = 6;
            logScroll.style.paddingLeft = 8;
            logScroll.style.paddingRight = 8;
            logScroll.style.paddingTop = 4;
            logScroll.style.paddingBottom = 4;

            _resultLabel = new Label(string.IsNullOrEmpty(_resultLog) ? M("(まだ実行結果はありません)") : _resultLog);
            _resultLabel.style.color = _theme.OnSurface;
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.selection.isSelectable = true;
            logScroll.Add(_resultLabel);
            card.Add(logScroll);

            return card;
        }

        // ─── Generic row helpers ───

        private VisualElement BuildSwitchRow(string label, bool initial, Action<bool> onChange)
        {
            var row = new MD3Row(MD3Spacing.S);
            row.style.marginTop = 4;
            row.style.alignItems = Align.Center;

            var sw = new MD3Switch(initial);
            sw.changed += v => onChange(v);
            row.Add(sw);

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.fontSize = 12;
            lbl.style.marginLeft = 4;
            row.Add(lbl);

            return row;
        }

        private VisualElement BuildSliderRow(string label, float initial, float min, float max,
            Action<float> onChange, Func<float, string> formatter = null)
        {
            var col = new VisualElement();
            col.style.marginTop = 4;
            col.style.marginBottom = 4;

            var header = new MD3Row(MD3Spacing.S);
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.fontSize = 12;
            header.Add(lbl);

            string Fmt(float v) => formatter != null ? formatter(v) : v.ToString("F3");
            var valueLbl = new Label(Fmt(initial));
            valueLbl.style.color = _theme.OnSurfaceVariant;
            valueLbl.style.fontSize = 11;
            header.Add(valueLbl);
            col.Add(header);

            var slider = new MD3Slider(initial, min, max);
            slider.changed += v => { valueLbl.text = Fmt(v); onChange(v); };
            col.Add(slider);

            return col;
        }

        // ─── Helpers ───

        private MD3Button NewActionButton(string label, MD3ButtonStyle style, Action onClick)
        {
            var btn = new MD3Button(label, style);
            btn.clicked += onClick;
            return btn;
        }

        private VisualElement NewHintText(string text)
        {
            return new MD3Text(text, MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
        }

        private MD3Card NewCard(string title, string subtitle, MD3CardStyle style)
        {
            var card = new MD3Card(title, subtitle, style);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;
            return card;
        }

        private void InvokeWithAvatar(Action<string> action)
        {
            if (_avatarRoot == null)
            {
                ShowNotification(new GUIContent(M("アバター ルートを指定してください")));
                return;
            }
            action(_avatarRoot.name);
        }

        private void SetLog(string message)
        {
            _resultLog = message ?? string.Empty;
            if (_resultLabel != null)
                _resultLabel.text = string.IsNullOrEmpty(_resultLog) ? M("(まだ実行結果はありません)") : _resultLog;
        }

        private static string Bs(bool b) => b ? "true" : "false";

        private void LoadAAOSettingsFromComponent()
        {
#if AVATAR_OPTIMIZER
            if (_avatarRoot == null) return;
            var comp = _avatarRoot.GetComponent<TraceAndOptimize>();
            if (comp == null) return;

            var so = new SerializedObject(comp);
            _aaoOptimizeBlendShape = so.FindProperty("optimizeBlendShape")?.boolValue ?? _aaoOptimizeBlendShape;
            _aaoRemoveUnusedObjects = so.FindProperty("removeUnusedObjects")?.boolValue ?? _aaoRemoveUnusedObjects;
            _aaoPreserveEndBone = so.FindProperty("preserveEndBone")?.boolValue ?? _aaoPreserveEndBone;
            _aaoOptimizePhysBone = so.FindProperty("optimizePhysBone")?.boolValue ?? _aaoOptimizePhysBone;
            _aaoOptimizeAnimator = so.FindProperty("optimizeAnimator")?.boolValue ?? _aaoOptimizeAnimator;
            _aaoMergeSkinnedMesh = so.FindProperty("mergeSkinnedMesh")?.boolValue ?? _aaoMergeSkinnedMesh;
            _aaoOptimizeTexture = so.FindProperty("optimizeTexture")?.boolValue ?? _aaoOptimizeTexture;
            _aaoMmdWorldCompatibility = so.FindProperty("mmdWorldCompatibility")?.boolValue ?? _aaoMmdWorldCompatibility;
#endif
        }

        private static GameObject AutoDetectAvatarRoot(GameObject obj)
        {
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
    }

    internal static class AvatarOptimizerWindowExtensions
    {
        public static MD3Button WithClick(this MD3Button btn, Action onClick)
        {
            btn.clicked += onClick;
            return btn;
        }
    }
}
