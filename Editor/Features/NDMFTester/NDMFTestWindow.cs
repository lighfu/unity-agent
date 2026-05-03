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
    /// NDMF (Non-Destructive Modular Framework) のすべての機能を 1 画面で
    /// テストできるウィンドウ。NDMFTools / BuildPipelineTools / AvatarPerformanceAnalyzer
    /// の各 API をボタンから直接呼び出し、結果ログに表示する。
    /// </summary>
    public class NDMFTestWindow : EditorWindow
    {
        private const string MenuPath = "UnityAgent/NDMF Tester";

        private MD3Theme _theme;

        private GameObject _avatarRoot;
        private bool _previewEnabled = true;
        private string _resultLog = string.Empty;

        private ObjectField _avatarRootField;
        private Label _resultLabel;
        private Label _ndmfStatusValue;

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
            var window = GetWindow<NDMFTestWindow>();
            window.titleContent = new GUIContent(M("NDMF テスター"));
            window.minSize = new Vector2(460, 640);
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

            _avatarRoot = root;
            return true;
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
            scroll.Add(BuildRuntimeCard());
            scroll.Add(BuildPluginRegistryCard());
            scroll.Add(BuildPerformanceCard());
            scroll.Add(BuildBakeCard());
            scroll.Add(BuildErrorReportCard());
            scroll.Add(BuildPreviewCard());
            scroll.Add(BuildConsoleCard());
            scroll.Add(BuildResultCard());

            TryAutoDetectFromSelection();
            SyncAvatarRootField();
        }

        // ─── Header (avatar selector) ───

        private VisualElement BuildHeaderCard()
        {
            var card = NewCard(M("対象アバター"), null, MD3CardStyle.Filled);

            card.Add(new MD3Text(
                M("NDMF (Non-Destructive Modular Framework) の機能を一通り検証するためのウィンドウです。各カードのボタンから API を呼び出し、結果ログを確認できます。"),
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
                _avatarRoot = evt.newValue as GameObject;
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
                _avatarRoot = root;
                SyncAvatarRootField();
            };
            card.Add(detectBtn);

            return card;
        }

        // ─── 1. NDMF Runtime info ───

        private VisualElement BuildRuntimeCard()
        {
            var card = NewCard(M("1. NDMF ランタイム情報"), null, MD3CardStyle.Outlined);

            var statusRow = new MD3Row(MD3Spacing.S);
            statusRow.Add(new MD3Text(M("状態"), MD3TextStyle.LabelMedium,
                color: _theme.OnSurfaceVariant));
            _ndmfStatusValue = new Label(M("(未確認)"));
            _ndmfStatusValue.style.color = _theme.OnSurface;
            _ndmfStatusValue.style.fontSize = 12;
            _ndmfStatusValue.style.marginLeft = 4;
            statusRow.Add(_ndmfStatusValue);
            card.Add(statusRow);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.marginTop = 6;
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("GetNDMFInfo"), MD3ButtonStyle.Filled, () =>
            {
                var info = NDMFTools.GetNDMFInfo();
                SetLog(info);
                RefreshNdmfStatus(info);
            }));

            btnRow.Add(NewActionButton(M("ListNDMFPlugins (簡易)"), MD3ButtonStyle.Tonal,
                () => SetLog(BuildPipelineTools.ListNDMFPlugins())));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("インストールされている NDMF のバージョン・場所と、検出されたプラグイン数を表示します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 2. Plugin Registry ───

        private VisualElement BuildPluginRegistryCard()
        {
            var card = NewCard(M("2. プラグインレジストリ"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("ListNDMFPluginRegistry"), MD3ButtonStyle.Filled,
                () => SetLog(NDMFTools.ListNDMFPluginRegistry())));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("PluginBase / Plugin<T> を継承したプラグインを公式 Plugin Registry から走査して列挙します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 3. Performance & Parameters (NDMF ParameterInfo) ───

        private VisualElement BuildPerformanceCard()
        {
            var card = NewCard(M("3. ParameterInfo / パフォーマンス"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("ListNDMFParameters"), MD3ButtonStyle.Filled,
                () => InvokeWithAvatar(name =>
                    SetLog(NDMFTools.ListNDMFParameters(name)))));

            btnRow.Add(NewActionButton(M("AnalyzeAvatarPerformance"), MD3ButtonStyle.Tonal,
                () => InvokeWithAvatar(name =>
                    SetLog(AvatarPerformanceAnalyzer.AnalyzeAvatarPerformance(name)))));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("NDMF ParameterInfo.ForUI を経由して post-build パラメータと bit cost を取得します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 4. Manual Bake ───

        private VisualElement BuildBakeCard()
        {
            var card = NewCard(M("4. Manual Bake (ProcessAvatar)"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("TriggerNDMFManualBake"), MD3ButtonStyle.Filled,
                () => InvokeWithAvatar(name =>
                {
                    if (!EditorUtility.DisplayDialog(
                            M("Manual Bake を実行"),
                            M("対象アバターの NDMF Manual Bake を実行します。シーンに新しい焼き込み済みコピーが生成されます。\n続行しますか？"),
                            M("実行"),
                            M("キャンセル")))
                    {
                        return;
                    }
                    SetLog(BuildPipelineTools.TriggerNDMFManualBake(name));
                })));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("nadena.dev.ndmf.AvatarProcessor.ProcessAvatar を呼び出し、すべての NDMF プラグインの実行結果をシーン上に焼き込みます。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 5. Error Report ───

        private VisualElement BuildErrorReportCard()
        {
            var card = NewCard(M("5. Error Report"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("InspectNDMFErrorReport"), MD3ButtonStyle.Filled,
                () => SetLog(NDMFTools.InspectNDMFErrorReport())));

            btnRow.Add(NewActionButton(M("ClearNDMFErrorReport"), MD3ButtonStyle.Outlined,
                () => SetLog(NDMFTools.ClearNDMFErrorReport())));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("Manual Bake の前後で実行すると、各プラグインから報告された Error / Warning / Info を一覧表示します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 6. Preview System ───

        private VisualElement BuildPreviewCard()
        {
            var card = NewCard(M("6. Preview System"), null, MD3CardStyle.Outlined);

            var switchRow = new MD3Row(MD3Spacing.S);
            switchRow.style.alignItems = Align.Center;
            switchRow.style.marginTop = 4;

            var sw = new MD3Switch(_previewEnabled);
            sw.changed += v => _previewEnabled = v;
            switchRow.Add(sw);

            var lbl = new Label(M("Preview を有効にする"));
            lbl.style.color = _theme.OnSurface;
            lbl.style.fontSize = 12;
            lbl.style.marginLeft = 4;
            switchRow.Add(lbl);
            card.Add(switchRow);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.marginTop = 6;
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("SetNDMFPreviewEnabled"), MD3ButtonStyle.Filled,
                () => SetLog(NDMFTools.SetNDMFPreviewEnabled(_previewEnabled))));

            btnRow.Add(NewActionButton(M("ListNDMFPreviewFilters"), MD3ButtonStyle.Tonal,
                () => SetLog(NDMFTools.ListNDMFPreviewFilters())));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("NDMF Preview System の有効/無効を切り替え、登録されている IRenderFilter を一覧表示します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            return card;
        }

        // ─── 7. NDMF Console ───

        private VisualElement BuildConsoleCard()
        {
            var card = NewCard(M("7. NDMF Console"), null, MD3CardStyle.Outlined);

            var btnRow = new MD3Row(MD3Spacing.S);
            btnRow.style.flexWrap = Wrap.Wrap;

            btnRow.Add(NewActionButton(M("OpenNDMFConsole"), MD3ButtonStyle.Filled,
                () => SetLog(NDMFTools.OpenNDMFConsole())));

            card.Add(btnRow);

            card.Add(new MD3Text(
                M("Tools / NDM Framework / Show NDMF Console をプログラム的に開きます。最新ビルドの実行履歴とタイミングを確認できます。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

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
            logScroll.style.minHeight = 160;
            logScroll.style.maxHeight = 320;
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

        // ─── Helpers ───

        private MD3Button NewActionButton(string label, MD3ButtonStyle style, Action onClick)
        {
            var btn = new MD3Button(label, style);
            btn.clicked += onClick;
            return btn;
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

        private void RefreshNdmfStatus(string info)
        {
            if (_ndmfStatusValue == null) return;
            bool installed = info != null && info.StartsWith("NDMF Runtime Info");
            _ndmfStatusValue.text = installed ? M("インストール済み") : M("未検出");
            _ndmfStatusValue.style.color = installed ? _theme.Primary : _theme.OnSurfaceVariant;
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

}
