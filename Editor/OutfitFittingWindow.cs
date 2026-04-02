using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AjisaiFlow.UnityAgent.Editor.Fitting;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class OutfitFittingWindow : EditorWindow
    {
        private GameObject _avatarRoot;
        private GameObject _outfitRoot;
        private bool _advancedFoldout;
        private float _adaptStrength = 1.0f;
        private float _offset = 0.001f;
        private bool _transferWeights;
        private Vector2 _scrollPos;
        private Vector2 _logScrollPos;

        // Pipeline stage toggles
        private bool _useGreenCoordinates;
        private bool _useARAP = true;
        private bool _useXPBD;

        // GC settings
        private int _cageVertexCount = 200;
        private float _cageOffset = 0.02f;

        // SDF settings
        private int _sdfResolution = 64;
        private int _sdfBlurRadius = 3;
        private int _sdfKNearest = 6;

        // ARAP settings
        private int _arapIterations = 8;
        private int _arapGSIterations = 10;
        private float _collisionMargin = 0.0015f;
        private float _arapPenetrationWeight = 10f;
        private float _arapBoundaryWeight = 50f;
        private int _arapFinalCollisionPasses = 3;
        private int _arapKNearest = 8;

        // Skin-tight boost
        private float _skinTightThreshold = 0.5f;
        private float _skinTightBoostMax = 5f;

        // Smoothing
        private float _taubinLambda = 0.4f;
        private float _taubinMu = -0.42f;

        // Topology
        private int _boundaryDiffusionPasses = 4;

        // Air Gap (post-fitting offset)
        private float _airGap = 0.002f;
        private float _airGapMaxDepth = 0.02f;

        // Weight transfer settings
        private int _weightKNearest = 8;
        private float _weightBlend = 1f;
        private float _weightMaxDistance = 0f;

        // XPBD relaxation settings
        private int _xpbdIterations = 50;
        private float _xpbdBendCompliance = 0.001f;
        private float _xpbdCollisionMargin = 0.002f;
        private float _xpbdMaxIterDisp = 0.002f;
        private float _xpbdMaxTotalDisp = 0.015f;

        // Impact color coding
        private static readonly Color HighImpact = new Color(1f, 0.5f, 0.35f);
        private static readonly Color MedImpact = new Color(1f, 0.85f, 0.3f);
        private static readonly Color LowImpact = new Color(0.65f, 0.65f, 0.65f);

        // Quality preset
        private int _qualityPreset = 1;
        private static string[] QualityLabels => new[] { M("高速"), M("標準"), M("高品質") };

        // ─── Async execution state ───
        private bool _isRunning;
        private Thread _fittingThread;
        private FittingLog _fittingLog;
        private int _lastLogLength;
        private bool _autoScroll = true;

        // Data for main-thread finalization
        private MeshJobData[] _pendingJobs;
        private SkinnedMeshRenderer[] _pendingSmrs;
        private int _pendingUndoGroup;

        private class MeshJobData
        {
            public Vector3[] origVerts;
            public Vector3[] workVerts;
            public int[] triangles;
            public string name;
            public string stagesLog;
        }

        [MenuItem("Window/紫陽花広場/Outfit Fitting")]
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
            GetWindow<OutfitFittingWindow>(M("衣装フィッティング"));
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;
            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null && root != _avatarRoot)
            {
                _avatarRoot = root;
                Repaint();
            }
        }

        private void OnDisable()
        {
            // Clean up on window close
            if (_fittingLog != null) _fittingLog.IsCancelled = true;
            EditorApplication.update -= OnFittingUpdate;
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.HelpBox(
                M("Beta: 最適なパラメータをまだ模索中です。うまくいった設定があれば「設定コピー」で共有していただけると嬉しいです！"),
                MessageType.Info);

            EditorGUILayout.Space(4);

            // ─── Input fields (disabled while running) ───
            using (new EditorGUI.DisabledScope(_isRunning))
            {
                DrawInputFields();
                DrawAdvancedSettings();

                EditorGUILayout.Space(4);
                DrawSeparator();
                EditorGUILayout.Space(4);

                DrawExecuteButton();
            }

            // ─── Running state: progress + cancel ───
            if (_isRunning)
            {
                EditorGUILayout.Space(4);
                DrawRunningUI();
            }

            EditorGUILayout.Space(4);
            DrawSeparator();

            // ─── Log ───
            DrawLog();

            EditorGUILayout.EndScrollView();
        }

        // ─── Input UI ───

        private void DrawInputFields()
        {
            EditorGUILayout.BeginHorizontal();
            _avatarRoot = (GameObject)EditorGUILayout.ObjectField(M("アバター"), _avatarRoot, typeof(GameObject), true);
            if (GUILayout.Button(M("自動検出"), GUILayout.Width(70)))
                AutoDetectFromSelection();
            EditorGUILayout.EndHorizontal();

            _outfitRoot = (GameObject)EditorGUILayout.ObjectField(M("衣装"), _outfitRoot, typeof(GameObject), true);

            EditorGUILayout.Space(4);
        }

        private void DrawAdvancedSettings()
        {
            _advancedFoldout = EditorGUILayout.Foldout(_advancedFoldout, M("詳細設定"), true);
            if (!_advancedFoldout) return;

            EditorGUI.indentLevel++;

            _adaptStrength = SliderC(M("体型適応強度"), M("ボーンアライメントの適応強度。1.0=完全一致"), _adaptStrength, 0f, 1f, HighImpact);
            _offset = FloatC(M("貫通マージン (m)"), M("最終パスで素体表面からのオフセット距離"), _offset, MedImpact);
            _transferWeights = EditorGUILayout.Toggle(Tip(M("ウェイト転送"), M("アバターのボーンウェイトを衣装に転送。関節変形を改善")), _transferWeights);
            if (_transferWeights)
            {
                EditorGUI.indentLevel++;
                _weightKNearest = IntSliderC(M("KNN近傍数"), M("三角形マッチング用の近傍頂点数。多い=精度向上、遠い頂点もマッチ"), _weightKNearest, 4, 24, MedImpact);
                _weightBlend = SliderC(M("ブレンド率"), M("転送ウェイトと元ウェイトの混合比率。1.0=完全転送、0.5=半々ブレンド"), _weightBlend, 0f, 1f, HighImpact);
                _weightMaxDistance = SliderC(M("最大距離 (m)"), M("素体からこの距離以上離れた頂点は元のウェイトを保持。0=無制限"), _weightMaxDistance, 0f, 0.1f, MedImpact);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _qualityPreset = EditorGUILayout.Popup(M("品質プリセット"), _qualityPreset, QualityLabels);
            if (EditorGUI.EndChangeCheck())
                ApplyQualityPreset(_qualityPreset);

            // Copy/Paste buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(M("設定コピー"), EditorStyles.miniButton, GUILayout.Width(80)))
                CopySettings();
            if (GUILayout.Button(M("設定ペースト"), EditorStyles.miniButton, GUILayout.Width(80)))
                PasteSettings();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ─── Impact legend ───
            EditorGUILayout.BeginHorizontal();
            DrawColorDot(HighImpact); GUILayout.Label(M("高影響"), GUILayout.Width(40));
            GUILayout.Space(4);
            DrawColorDot(MedImpact); GUILayout.Label(M("中影響"), GUILayout.Width(40));
            GUILayout.Space(4);
            DrawColorDot(LowImpact); GUILayout.Label(M("低影響"), GUILayout.Width(40));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // ─── SDF settings ───
            EditorGUILayout.LabelField(M("SDF設定"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _sdfResolution = IntSliderC(M("SDF解像度"), M("ボクセルグリッドの解像度。高い=精度向上、メモリ増"), _sdfResolution, 32, 128, MedImpact);
            _sdfBlurRadius = IntSliderC(M("ブラー半径"), M("ガウシアンブラーの半径。SDF勾配の滑らかさに影響"), _sdfBlurRadius, 1, 5, LowImpact);
            _sdfKNearest = IntSliderC(M("KNN近傍数"), M("SDF構築時の最近傍数。精度と処理速度のトレードオフ"), _sdfKNearest, 3, 12, LowImpact);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4);

            // ─── Topology ───
            _boundaryDiffusionPasses = IntSliderC(M("境界拡散パス"), M("境界マスクの拡散回数。境界固定の影響範囲を制御"), _boundaryDiffusionPasses, 1, 8, MedImpact);

            EditorGUILayout.Space(4);

            // ─── Green Coordinates ───
            _useGreenCoordinates = EditorGUILayout.Toggle(Tip(M("Green Coordinates (Air Gap保護)"), M("ケージ変形で大域的形状を転写。Air Gapを幾何学的に保護")), _useGreenCoordinates);
            if (_useGreenCoordinates)
            {
                EditorGUI.indentLevel++;
                _cageVertexCount = IntSliderC(M("ケージ頂点数"), M("ケージの解像度。高い=精密だが処理時間増"), _cageVertexCount, 50, 500, MedImpact);
                _cageOffset = FloatC(M("ケージオフセット (m)"), M("メッシュ表面からのケージオフセット距離"), _cageOffset, MedImpact);
                EditorGUI.indentLevel--;
            }

            // ─── ARAP ───
            _useARAP = EditorGUILayout.Toggle(Tip(M("ARAP (ディテール復元)"), M("SVD回転抽出+Gauss-Seidelで局所剛性を保持しながら変形")), _useARAP);
            if (_useARAP)
            {
                EditorGUI.indentLevel++;
                _arapIterations = IntSliderC(M("外部反復回数"), M("ARAP外部ループ回数。多い=収束精度向上だが過剰反復は品質低下の可能性"), _arapIterations, 1, 20, HighImpact);
                _arapGSIterations = IntSliderC(M("GS反復回数"), M("Gauss-Seidel内部反復数。大域的位置解決の精度"), _arapGSIterations, 1, 30, MedImpact);
                _collisionMargin = FloatC(M("衝突マージン (m)"), M("素体表面からの最小距離。小さい=タイト、大きい=余裕"), _collisionMargin, HighImpact);
                _arapPenetrationWeight = SliderC(M("貫通重み"), M("貫通頂点を押し出す力の強さ。高い=貫通除去優先、形状歪みリスク増"), _arapPenetrationWeight, 1f, 50f, HighImpact);
                _arapBoundaryWeight = SliderC(M("境界重み"), M("境界（端/縫い目）頂点の固定強度。高い=端が動かない、低い=自然な変形"), _arapBoundaryWeight, 5f, 200f, HighImpact);
                _arapFinalCollisionPasses = IntSliderC(M("最終衝突パス"), M("ARAP後の衝突クリーンアップ回数"), _arapFinalCollisionPasses, 1, 10, MedImpact);
                _arapKNearest = IntSliderC(M("衝突KNN近傍数"), M("KNN衝突検出の近傍数。多い=検出精度向上"), _arapKNearest, 4, 16, MedImpact);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(M("スキンタイトブースト"), EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                _skinTightThreshold = SliderC(M("閾値 (貫通率)"), M("この貫通率を超えるとブーストが有効化。低い=敏感"), _skinTightThreshold, 0.2f, 0.9f, MedImpact);
                _skinTightBoostMax = SliderC(M("最大係数"), M("貫通重みの最大倍率。高い=スキンタイト衣装で強力な押し出し"), _skinTightBoostMax, 1f, 10f, MedImpact);
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(M("スムージング (Taubin)"), EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                _taubinLambda = SliderC(M("Lambda (収縮)"), M("Laplacianスムージングの収縮係数。高い=強い平滑化"), _taubinLambda, 0.1f, 0.8f, HighImpact);
                _taubinMu = SliderC(M("Mu (膨張)"), M("収縮を補正する膨張係数（負値）。|Mu|>Lambda=体積保持"), _taubinMu, -0.8f, -0.1f, HighImpact);
                EditorGUI.indentLevel--;

                EditorGUI.indentLevel--;
            }

            // ─── XPBD ───
            _useXPBD = EditorGUILayout.Toggle(Tip(M("XPBD リラクゼーション (衝突解消)"), M("制約ベースの衝突解消。ARAP後の残留貫通を修正")), _useXPBD);
            if (_useXPBD)
            {
                EditorGUI.indentLevel++;
                _xpbdIterations = IntSliderC(M("反復回数"), M("制約投影の反復数。多い=収束精度向上"), _xpbdIterations, 10, 200, MedImpact);
                _xpbdBendCompliance = FloatC(M("曲げコンプライアンス"), M("曲げ制約の柔らかさ。0=硬い、高い=柔らかい"), _xpbdBendCompliance, LowImpact);
                _xpbdCollisionMargin = FloatC(M("衝突マージン (m)"), M("XPBD衝突検出の距離閾値"), _xpbdCollisionMargin, MedImpact);
                _xpbdMaxIterDisp = FloatC(M("最大変位/反復 (m)"), M("1反復あたりの最大頂点移動量。発散防止"), _xpbdMaxIterDisp, LowImpact);
                _xpbdMaxTotalDisp = FloatC(M("最大総変位 (m)"), M("全反復での合計最大変位。メッシュ崩壊防止"), _xpbdMaxTotalDisp, LowImpact);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ─── Air Gap ───
            EditorGUILayout.LabelField(Tip(M("エアギャップ (ゆとり調整)"), M("フィッティング後に衣装を素体から離す距離。ぴちぴち感を解消")), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _airGap = SliderC(M("エアギャップ (mm)"), M("素体表面からの追加オフセット。0=ぴちぴち、2-4mm=自然なゆとり"), _airGap * 1000f, 0f, 10f, HighImpact) / 1000f;
            _airGapMaxDepth = SliderC(M("最大適用深度 (mm)"), M("この深さ以上に素体内部にある頂点はスキップ。深い貫通は無視"), _airGapMaxDepth * 1000f, 5f, 50f, LowImpact) / 1000f;
            EditorGUI.indentLevel--;

            EditorGUI.indentLevel--;
        }

        // ─── Colored parameter helpers ───

        private static GUIContent Tip(string label, string tooltip)
        {
            return new GUIContent(label, tooltip);
        }

        private static int IntSliderC(string label, string tooltip, int value, int min, int max, Color c)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = c;
            value = EditorGUILayout.IntSlider(new GUIContent(label, tooltip), value, min, max);
            GUI.contentColor = prev;
            return value;
        }

        private static float SliderC(string label, string tooltip, float value, float min, float max, Color c)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = c;
            value = EditorGUILayout.Slider(new GUIContent(label, tooltip), value, min, max);
            GUI.contentColor = prev;
            return value;
        }

        private static float FloatC(string label, string tooltip, float value, Color c)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = c;
            value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), value);
            GUI.contentColor = prev;
            return value;
        }

        private static void DrawColorDot(Color c)
        {
            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            rect.y += 2;
            EditorGUI.DrawRect(rect, c);
        }

        private void DrawExecuteButton()
        {
            using (new EditorGUI.DisabledScope(_avatarRoot == null || _outfitRoot == null))
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
                if (GUILayout.Button(M("フィッティング実行"), GUILayout.Height(32)))
                    ExecuteFitting();
                GUI.backgroundColor = prevBg;
            }

            if (_avatarRoot == null || _outfitRoot == null)
                EditorGUILayout.HelpBox(M("AvatarとOutfitを設定してください。"), MessageType.Info);
        }

        private void DrawRunningUI()
        {
            float progress = _fittingLog != null ? _fittingLog.Progress : 0f;
            EditorGUI.ProgressBar(
                EditorGUILayout.GetControlRect(false, 20),
                progress,
                string.Format(M("処理中... {0}%"), (progress * 100f).ToString("F0")));

            EditorGUILayout.Space(2);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
            if (GUILayout.Button(M("キャンセル"), GUILayout.Height(24)))
            {
                if (_fittingLog != null) _fittingLog.IsCancelled = true;
            }
            GUI.backgroundColor = prevBg;
        }

        // ─── Log display ───

        private void DrawLog()
        {
            string logText = _fittingLog != null ? _fittingLog.GetText() : null;
            if (string.IsNullOrEmpty(logText)) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(M("実行ログ"), EditorStyles.boldLabel);
            if (GUILayout.Button(M("コピー"), EditorStyles.miniButton, GUILayout.Width(50)))
                EditorGUIUtility.systemCopyBuffer = logText;
            _autoScroll = GUILayout.Toggle(_autoScroll, M("自動スクロール"), EditorStyles.miniButton, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            // Auto-scroll when new content appears
            int currentLen = logText.Length;
            if (_autoScroll && currentLen != _lastLogLength)
            {
                _logScrollPos.y = float.MaxValue;
                _lastLogLength = currentLen;
            }

            _logScrollPos = EditorGUILayout.BeginScrollView(
                _logScrollPos, EditorStyles.helpBox, GUILayout.Height(350));
            EditorGUILayout.TextArea(logText, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        // ─── Quality Presets ───

        private void ApplyQualityPreset(int preset)
        {
            switch (preset)
            {
                case 0: // Fast
                    _sdfResolution = 48; _sdfBlurRadius = 2; _sdfKNearest = 6;
                    _boundaryDiffusionPasses = 4;
                    _useGreenCoordinates = false; _cageVertexCount = 100; _cageOffset = 0.02f;
                    _useARAP = true;
                    _arapIterations = 4; _arapGSIterations = 8;
                    _collisionMargin = 0.0015f; _arapPenetrationWeight = 10f;
                    _arapBoundaryWeight = 50f; _arapFinalCollisionPasses = 3; _arapKNearest = 8;
                    _skinTightThreshold = 0.5f; _skinTightBoostMax = 3f;
                    _taubinLambda = 0.4f; _taubinMu = -0.42f;
                    _useXPBD = false;
                    _xpbdIterations = 50; _xpbdBendCompliance = 0.001f;
                    _xpbdCollisionMargin = 0.002f; _xpbdMaxIterDisp = 0.002f; _xpbdMaxTotalDisp = 0.015f;
                    _airGap = 0.002f; _airGapMaxDepth = 0.02f;
                    _weightKNearest = 8; _weightBlend = 1f; _weightMaxDistance = 0f;
                    break;
                case 1: // Standard
                    _sdfResolution = 64; _sdfBlurRadius = 3; _sdfKNearest = 6;
                    _boundaryDiffusionPasses = 4;
                    _useGreenCoordinates = false; _cageVertexCount = 200; _cageOffset = 0.02f;
                    _useARAP = true;
                    _arapIterations = 8; _arapGSIterations = 10;
                    _collisionMargin = 0.0015f; _arapPenetrationWeight = 10f;
                    _arapBoundaryWeight = 50f; _arapFinalCollisionPasses = 3; _arapKNearest = 8;
                    _skinTightThreshold = 0.5f; _skinTightBoostMax = 5f;
                    _taubinLambda = 0.4f; _taubinMu = -0.42f;
                    _useXPBD = false;
                    _xpbdIterations = 50; _xpbdBendCompliance = 0.001f;
                    _xpbdCollisionMargin = 0.002f; _xpbdMaxIterDisp = 0.002f; _xpbdMaxTotalDisp = 0.015f;
                    _airGap = 0.002f; _airGapMaxDepth = 0.02f;
                    _weightKNearest = 8; _weightBlend = 1f; _weightMaxDistance = 0f;
                    break;
                case 2: // High Quality
                    _sdfResolution = 96; _sdfBlurRadius = 3; _sdfKNearest = 8;
                    _boundaryDiffusionPasses = 5;
                    _useGreenCoordinates = false; _cageVertexCount = 400; _cageOffset = 0.015f;
                    _useARAP = true;
                    _arapIterations = 15; _arapGSIterations = 15;
                    _collisionMargin = 0.001f; _arapPenetrationWeight = 10f;
                    _arapBoundaryWeight = 50f; _arapFinalCollisionPasses = 5; _arapKNearest = 10;
                    _skinTightThreshold = 0.4f; _skinTightBoostMax = 5f;
                    _taubinLambda = 0.4f; _taubinMu = -0.42f;
                    _useXPBD = true;
                    _xpbdIterations = 50; _xpbdBendCompliance = 0.001f;
                    _xpbdCollisionMargin = 0.002f; _xpbdMaxIterDisp = 0.002f; _xpbdMaxTotalDisp = 0.015f;
                    _airGap = 0.003f; _airGapMaxDepth = 0.02f;
                    _weightKNearest = 12; _weightBlend = 1f; _weightMaxDistance = 0f;
                    break;
            }
        }

        // ─── Async Fitting Execution ───

        private void ExecuteFitting()
        {
            if (_isRunning) return;

            _fittingLog = new FittingLog();
            _fittingLog.Start();
            _lastLogLength = 0;

            try
            {
                // ═══ Phase A: Main-thread preparation ═══
                _fittingLog.Section(M("準備"));

                // Body mesh
                var bodyMeshes = FittingHelpers.FindBodyMeshes(_avatarRoot, _outfitRoot);
                if (bodyMeshes.Count == 0)
                {
                    _fittingLog.Error("Avatar上にBodyメッシュが見つかりません。");
                    return;
                }

                FittingHelpers.BakeWorldVerticesAndNormals(bodyMeshes, out var bodyVerts, out var bodyNormals);
                _fittingLog.Info($"Body: {bodyVerts.Length} vertices ({bodyMeshes.Count} mesh)");

                // Outfit meshes
                var smrs = _outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.sharedMesh != null).ToArray();
                if (smrs.Length == 0)
                {
                    _fittingLog.Error("Outfit上にSkinnedMeshRendererがありません。");
                    return;
                }

                // Bake pre-alignment (origVerts)
                var meshJobs = new MeshJobData[smrs.Length];
                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    var baked = new Mesh();
                    smr.BakeMesh(baked);
                    var origVerts = baked.vertices;
                    for (int v = 0; v < origVerts.Length; v++)
                        origVerts[v] = smr.transform.TransformPoint(origVerts[v]);
                    Object.DestroyImmediate(baked);
                    meshJobs[i] = new MeshJobData
                    {
                        origVerts = origVerts,
                        name = smr.name
                    };
                }

                // Bone alignment
                _fittingLog.Section(M("ボーン整列"));
                var mapping = FittingBoneMap.BuildBoneMapping(_outfitRoot.transform, _avatarRoot.transform);
                var alignTargets = mapping
                    .Where(e => e.outfitBone != null && e.avatarBone != null && e.confidence >= 0.5f)
                    .OrderBy(e => FittingBoneMap.GetHierarchyDepth(e.outfitBone))
                    .ToList();

                int undoGroup = Undo.GetCurrentGroup();
                int alignedCount = 0, skippedCount = 0;
                float maxPosDelta = 0f, maxRotDelta = 0f;
                string maxPosBone = "", maxRotBone = "";

                // Safety thresholds: skip bones with extreme deltas (likely mismatches)
                const float maxPosThreshold = 0.5f;  // Skip if > 50cm
                const float maxRotThreshold = 120f;   // Skip if > 120°

                foreach (var entry in alignTargets)
                {
                    Vector3 oldPos = entry.outfitBone.position;
                    Quaternion oldRot = entry.outfitBone.rotation;

                    // Pre-check: compute delta BEFORE applying
                    float prePosDelta = Vector3.Distance(oldPos, entry.avatarBone.position) * _adaptStrength;
                    float preRotDelta = Quaternion.Angle(oldRot, entry.avatarBone.rotation) * _adaptStrength;

                    if (prePosDelta > maxPosThreshold || preRotDelta > maxRotThreshold)
                    {
                        _fittingLog.Warn($"  SKIPPED: '{entry.outfitBone.name}' → '{entry.avatarBone.name}' ({entry.method}, conf={entry.confidence:F2}): pos={prePosDelta:F4}m, rot={preRotDelta:F1}° exceeds threshold");
                        skippedCount++;
                        continue;
                    }

                    Undo.RecordObject(entry.outfitBone, "OutfitFitting");
                    entry.outfitBone.position = Vector3.Lerp(oldPos, entry.avatarBone.position, _adaptStrength);
                    entry.outfitBone.rotation = Quaternion.Slerp(oldRot, entry.avatarBone.rotation, _adaptStrength);
                    float pd = Vector3.Distance(oldPos, entry.outfitBone.position);
                    float rd = Quaternion.Angle(oldRot, entry.outfitBone.rotation);
                    if (pd > maxPosDelta) { maxPosDelta = pd; maxPosBone = entry.outfitBone.name; }
                    if (rd > maxRotDelta) { maxRotDelta = rd; maxRotBone = entry.outfitBone.name; }

                    // Log large deltas (potential mismatches, but below threshold)
                    if (pd > 0.1f || rd > 45f)
                        _fittingLog.Warn($"  Large delta: '{entry.outfitBone.name}' → '{entry.avatarBone.name}' ({entry.method}, conf={entry.confidence:F2}): pos={pd:F4}m, rot={rd:F1}°");

                    alignedCount++;
                }
                _fittingLog.Info($"Aligned {alignedCount}/{mapping.Count} bones" + (skippedCount > 0 ? $" ({skippedCount} skipped)" : ""));
                _fittingLog.Stat(M("最大差分"), $"pos={maxPosDelta:F4}m ({maxPosBone}), rot={maxRotDelta:F1}° ({maxRotBone})");

                // Bake post-alignment (workVerts)
                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    var baked = new Mesh();
                    smr.BakeMesh(baked);
                    var workVerts = baked.vertices;
                    for (int v = 0; v < workVerts.Length; v++)
                        workVerts[v] = smr.transform.TransformPoint(workVerts[v]);
                    meshJobs[i].workVerts = workVerts;
                    meshJobs[i].triangles = smr.sharedMesh.triangles;
                    Object.DestroyImmediate(baked);
                    _fittingLog.Info($"Mesh '{smr.name}': {workVerts.Length} verts, {meshJobs[i].triangles.Length / 3} tris");
                }

                // Capture settings for background thread
                bool useGC = _useGreenCoordinates;
                bool useXPBD = _useXPBD;
                int arapIter = _arapIterations;
                int arapGSIter = _arapGSIterations;
                float margin = _collisionMargin;
                float arapPenWeight = _arapPenetrationWeight;
                float arapBndWeight = _arapBoundaryWeight;
                int arapFinalPasses = _arapFinalCollisionPasses;
                int arapKnn = _arapKNearest;
                float skinTightThresh = _skinTightThreshold;
                float skinTightBoost = _skinTightBoostMax;
                float tLambda = _taubinLambda;
                float tMu = _taubinMu;
                int sdfRes = _sdfResolution;
                int sdfBlur = _sdfBlurRadius;
                int sdfKnn = _sdfKNearest;
                int bndDiffPasses = _boundaryDiffusionPasses;
                float airGap = _airGap;
                float airGapMaxDepth = _airGapMaxDepth;
                var log = _fittingLog;

                // Set static configs before thread start
                GreenCoordinates.CageVertexCount = _cageVertexCount;
                GreenCoordinates.CageOffset = _cageOffset;
                FittingTopology.BoundaryDiffusionPasses = bndDiffPasses;
                XPBDSolver.Iterations = _xpbdIterations;
                XPBDSolver.BendCompliance = _xpbdBendCompliance;
                XPBDSolver.CollisionMargin = _xpbdCollisionMargin;
                XPBDSolver.MaxIterDisp = _xpbdMaxIterDisp;
                XPBDSolver.MaxTotalDisp = _xpbdMaxTotalDisp;
                OutfitFittingTools.WeightKNearest = _weightKNearest;
                OutfitFittingTools.WeightBlend = _weightBlend;
                OutfitFittingTools.WeightMaxDistance = _weightMaxDistance;

                // Store for finalization
                _pendingJobs = meshJobs;
                _pendingSmrs = smrs;
                _pendingUndoGroup = undoGroup;

                // ═══ Phase B: Background thread computation ═══
                _isRunning = true;

                _fittingThread = new Thread(() =>
                {
                    try
                    {
                        log.Section("Building Spatial Structures");
                        float cellSize = SpatialGrid.EstimateCellSize(bodyVerts);
                        var bodyGrid = new SpatialGrid(bodyVerts, cellSize);
                        log.Info($"SpatialGrid: cellSize={cellSize:F4}m");

                        var bodySDF = new BodySDF(bodyVerts, bodyNormals, bodyGrid, resolution: sdfRes, blurRadius: sdfBlur, kNearest: sdfKnn);
                        log.Info($"BodySDF: resolution={sdfRes}, blur={sdfBlur}, knn={sdfKnn}");

                        var pipeline = new FittingPipeline
                        {
                            ArapOuterIter = arapIter,
                            ArapGSIterations = arapGSIter,
                            ArapPenetrationWeight = arapPenWeight,
                            ArapBoundaryWeight = arapBndWeight,
                            CollisionMargin = margin,
                            ArapFinalCollisionPasses = arapFinalPasses,
                            ArapKNearest = arapKnn,
                            SkinTightThreshold = skinTightThresh,
                            SkinTightBoostMax = skinTightBoost,
                            TaubinLambda = tLambda,
                            TaubinMu = tMu,
                            AirGap = airGap,
                            AirGapMaxDepth = airGapMaxDepth,
                        };

                        for (int si = 0; si < meshJobs.Length; si++)
                        {
                            if (log.IsCancelled) { log.Warn("キャンセルされました"); break; }

                            var job = meshJobs[si];
                            log.Section(string.Format(M("フィッティング: {0} ({1}/{2})"), job.name, si + 1, meshJobs.Length));

                            var topo = FittingTopology.Build(job.workVerts, job.origVerts, job.triangles);
                            log.Info($"Topology: {job.workVerts.Length} verts → {topo.RootIndices.Count} roots");

                            int bndCount = 0;
                            foreach (float m in topo.BoundaryMask) if (m > 0.01f) bndCount++;
                            log.Stat(M("境界"), string.Format(M("{0} 頂点"), bndCount));

                            job.stagesLog = pipeline.Execute(
                                job.workVerts, topo, bodySDF, bodyGrid,
                                bodyVerts, bodyNormals,
                                job.name, si, meshJobs.Length,
                                useGC, useXPBD, log);

                            log.Info($"Result: {job.stagesLog}");
                        }

                        log.Section(string.Format(M("計算完了 ({0:F1}s)"), log.ElapsedSeconds));
                    }
                    catch (System.Exception ex)
                    {
                        log.Error(ex.ToString());
                    }
                    finally
                    {
                        _isRunning = false;
                    }
                });
                _fittingThread.IsBackground = true;
                _fittingThread.Start();

                EditorApplication.update += OnFittingUpdate;
            }
            catch (System.Exception ex)
            {
                _fittingLog.Error(ex.Message);
                _isRunning = false;
            }
        }

        private void OnFittingUpdate()
        {
            Repaint();

            if (!_isRunning)
            {
                EditorApplication.update -= OnFittingUpdate;
                ApplyPendingResults();
            }
        }

        // ─── Phase C: Main-thread finalization ───

        private void ApplyPendingResults()
        {
            if (_pendingJobs == null || _pendingSmrs == null) return;
            if (_fittingLog.IsCancelled)
            {
                _fittingLog.Warn("キャンセルのため結果は適用されません。");
                _pendingJobs = null;
                _pendingSmrs = null;
                Repaint();
                return;
            }

            try
            {
                _fittingLog.Section(M("結果の適用"));

                string genDir = PackagePaths.GetGeneratedDir("OutfitFitting");
                ToolUtility.EnsureAssetDirectory(genDir);

                for (int i = 0; i < _pendingJobs.Length; i++)
                {
                    var job = _pendingJobs[i];
                    var smr = _pendingSmrs[i];
                    if (job.workVerts == null) continue;

                    // World → Local
                    for (int v = 0; v < job.workVerts.Length; v++)
                        job.workVerts[v] = smr.transform.InverseTransformPoint(job.workVerts[v]);

                    var newMesh = Object.Instantiate(smr.sharedMesh);
                    newMesh.name = $"{smr.sharedMesh.name}_retargeted";
                    newMesh.vertices = job.workVerts;

                    // Update bind poses
                    var bones = smr.bones;
                    var oldBindPoses = smr.sharedMesh.bindposes;
                    var newBindPoses = new Matrix4x4[oldBindPoses.Length];
                    for (int bi = 0; bi < newBindPoses.Length; bi++)
                    {
                        if (bi < bones.Length && bones[bi] != null)
                            newBindPoses[bi] = bones[bi].worldToLocalMatrix * smr.transform.localToWorldMatrix;
                        else
                            newBindPoses[bi] = oldBindPoses[bi];
                    }
                    newMesh.bindposes = newBindPoses;
                    newMesh.RecalculateNormals();
                    newMesh.RecalculateBounds();

                    string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{genDir}/{newMesh.name}.asset");
                    AssetDatabase.CreateAsset(newMesh, assetPath);

                    Undo.RecordObject(smr, "OutfitFitting");
                    smr.sharedMesh = newMesh;

                    _fittingLog.Info($"Saved: {smr.name} → {assetPath}");
                }

                // Optional: fix penetration (sync, fast)
                if (_offset > 0f)
                {
                    string bodyMeshName = DetectBodyMeshName();
                    if (bodyMeshName != null)
                    {
                        _fittingLog.Section(M("貫通修正"));
                        foreach (var smr in _pendingSmrs)
                        {
                            string meshPath = GetScenePath(smr.gameObject);
                            string fixResult = RunSafe(() =>
                                OutfitFittingTools.FixMeshPenetration(meshPath, bodyMeshName, _offset));
                            _fittingLog.Info($"  {smr.name}: {fixResult}");
                        }
                    }
                }

                // Optional: weight transfer
                if (_transferWeights)
                {
                    string bodyMeshName = DetectBodyMeshName();
                    if (bodyMeshName != null)
                    {
                        _fittingLog.Section(M("ウェイト転送"));
                        foreach (var smr in _pendingSmrs)
                        {
                            string meshPath = GetScenePath(smr.gameObject);
                            string weightResult = RunSafe(() =>
                                OutfitFittingTools.TransferOutfitWeights(meshPath, bodyMeshName));
                            _fittingLog.Info($"  {smr.name}: {weightResult}");
                        }
                    }
                }

                Undo.CollapseUndoOperations(_pendingUndoGroup);
                AssetDatabase.SaveAssets();
                ToolProgress.Clear();

                _fittingLog.Section(string.Format(M("完了 ({0:F1}s)"), _fittingLog.ElapsedSeconds));
                _fittingLog.Info(M("Undo (Ctrl+Z) で元に戻せます。"));
            }
            catch (System.Exception ex)
            {
                _fittingLog.Error(ex.Message);
            }
            finally
            {
                _pendingJobs = null;
                _pendingSmrs = null;
                Repaint();
            }
        }

        // ─── Helpers ───

        private string RunSafe(System.Func<string> action)
        {
            try { return action(); }
            catch (System.Exception ex) { return "Error: " + ex.Message; }
        }

        private string DetectBodyMeshName()
        {
            if (_avatarRoot == null) return null;
            var smrs = _avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer best = null;
            int bestVerts = 0;
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                if (_outfitRoot != null && smr.transform.IsChildOf(_outfitRoot.transform)) continue;
                string lower = smr.name.ToLowerInvariant();
                if (lower.Contains("body") || lower.Contains("素体") || lower.Contains("mesh_body"))
                    return GetScenePath(smr.gameObject);
                if (smr.sharedMesh.vertexCount > bestVerts)
                {
                    bestVerts = smr.sharedMesh.vertexCount;
                    best = smr;
                }
            }
            return best != null ? GetScenePath(best.gameObject) : null;
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

        private void AutoDetectFromSelection()
        {
            if (Selection.activeGameObject == null) return;
            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null)
            {
                _avatarRoot = root;
                Repaint();
            }
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

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        // ─── Settings Copy/Paste ───

        [System.Serializable]
        private class FittingSettings
        {
            // General
            public float adaptStrength = 1f;
            public float offset = 0.001f;
            public bool transferWeights;

            // SDF
            public int sdfResolution = 64;
            public int sdfBlurRadius = 3;
            public int sdfKNearest = 6;

            // Topology
            public int boundaryDiffusionPasses = 4;

            // Green Coordinates
            public bool useGreenCoordinates;
            public int cageVertexCount = 200;
            public float cageOffset = 0.02f;

            // ARAP
            public bool useARAP = true;
            public int arapIterations = 8;
            public int arapGSIterations = 10;
            public float collisionMargin = 0.0015f;
            public float arapPenetrationWeight = 10f;
            public float arapBoundaryWeight = 50f;
            public int arapFinalCollisionPasses = 3;
            public int arapKNearest = 8;

            // Skin-tight
            public float skinTightThreshold = 0.5f;
            public float skinTightBoostMax = 5f;

            // Taubin
            public float taubinLambda = 0.4f;
            public float taubinMu = -0.42f;

            // XPBD
            public bool useXPBD;
            public int xpbdIterations = 50;
            public float xpbdBendCompliance = 0.001f;
            public float xpbdCollisionMargin = 0.002f;
            public float xpbdMaxIterDisp = 0.002f;
            public float xpbdMaxTotalDisp = 0.015f;

            // Air Gap
            public float airGap = 0.002f;
            public float airGapMaxDepth = 0.02f;

            // Weight transfer
            public int weightKNearest = 8;
            public float weightBlend = 1f;
            public float weightMaxDistance = 0f;
        }

        private FittingSettings GatherSettings()
        {
            return new FittingSettings
            {
                adaptStrength = _adaptStrength,
                offset = _offset,
                transferWeights = _transferWeights,
                sdfResolution = _sdfResolution,
                sdfBlurRadius = _sdfBlurRadius,
                sdfKNearest = _sdfKNearest,
                boundaryDiffusionPasses = _boundaryDiffusionPasses,
                useGreenCoordinates = _useGreenCoordinates,
                cageVertexCount = _cageVertexCount,
                cageOffset = _cageOffset,
                useARAP = _useARAP,
                arapIterations = _arapIterations,
                arapGSIterations = _arapGSIterations,
                collisionMargin = _collisionMargin,
                arapPenetrationWeight = _arapPenetrationWeight,
                arapBoundaryWeight = _arapBoundaryWeight,
                arapFinalCollisionPasses = _arapFinalCollisionPasses,
                arapKNearest = _arapKNearest,
                skinTightThreshold = _skinTightThreshold,
                skinTightBoostMax = _skinTightBoostMax,
                taubinLambda = _taubinLambda,
                taubinMu = _taubinMu,
                useXPBD = _useXPBD,
                xpbdIterations = _xpbdIterations,
                xpbdBendCompliance = _xpbdBendCompliance,
                xpbdCollisionMargin = _xpbdCollisionMargin,
                xpbdMaxIterDisp = _xpbdMaxIterDisp,
                xpbdMaxTotalDisp = _xpbdMaxTotalDisp,
                airGap = _airGap,
                airGapMaxDepth = _airGapMaxDepth,
                weightKNearest = _weightKNearest,
                weightBlend = _weightBlend,
                weightMaxDistance = _weightMaxDistance,
            };
        }

        private void ApplySettings(FittingSettings s)
        {
            _adaptStrength = s.adaptStrength;
            _offset = s.offset;
            _transferWeights = s.transferWeights;
            _sdfResolution = s.sdfResolution;
            _sdfBlurRadius = s.sdfBlurRadius;
            _sdfKNearest = s.sdfKNearest;
            _boundaryDiffusionPasses = s.boundaryDiffusionPasses;
            _useGreenCoordinates = s.useGreenCoordinates;
            _cageVertexCount = s.cageVertexCount;
            _cageOffset = s.cageOffset;
            _useARAP = s.useARAP;
            _arapIterations = s.arapIterations;
            _arapGSIterations = s.arapGSIterations;
            _collisionMargin = s.collisionMargin;
            _arapPenetrationWeight = s.arapPenetrationWeight;
            _arapBoundaryWeight = s.arapBoundaryWeight;
            _arapFinalCollisionPasses = s.arapFinalCollisionPasses;
            _arapKNearest = s.arapKNearest;
            _skinTightThreshold = s.skinTightThreshold;
            _skinTightBoostMax = s.skinTightBoostMax;
            _taubinLambda = s.taubinLambda;
            _taubinMu = s.taubinMu;
            _useXPBD = s.useXPBD;
            _xpbdIterations = s.xpbdIterations;
            _xpbdBendCompliance = s.xpbdBendCompliance;
            _xpbdCollisionMargin = s.xpbdCollisionMargin;
            _xpbdMaxIterDisp = s.xpbdMaxIterDisp;
            _xpbdMaxTotalDisp = s.xpbdMaxTotalDisp;
            _airGap = s.airGap;
            _airGapMaxDepth = s.airGapMaxDepth;
            _weightKNearest = s.weightKNearest;
            _weightBlend = s.weightBlend;
            _weightMaxDistance = s.weightMaxDistance;
        }

        private void CopySettings()
        {
            var settings = GatherSettings();
            string json = JsonUtility.ToJson(settings, true);
            EditorGUIUtility.systemCopyBuffer = json;
        }

        private void PasteSettings()
        {
            string json = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var settings = JsonUtility.FromJson<FittingSettings>(json);
                if (settings != null)
                {
                    ApplySettings(settings);
                    Repaint();
                }
            }
            catch (System.Exception)
            {
                Debug.LogWarning("OutfitFitting: クリップボードの内容が設定JSONとして解析できませんでした。");
            }
        }
    }
}
