#if ANIMATOR_AS_CODE && MODULAR_AVATAR
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Debug window for the AAC Buildup API (AnimatorAsCodeTools).
    /// Reproduces the Begin → ExecuteScript* → Commit/Discard flow without going
    /// through the AI loop, so you can validate the AAC fluent builder snippets
    /// and the destructive overwrite behavior interactively.
    /// </summary>
    internal class AacToolsTestWindow : EditorWindow
    {
        private MD3Theme _theme;

        // Inputs
        private GameObject _avatarRoot;
        private string _systemName = "TestSystem";
        private string _assetDir = "";

        // Script editor
        private TextField _scriptField;
        private string _scriptText = DefaultSampleScript;

        // Output
        private string _lastResult = "";
        private Label _resultLabel;
        private Label _sessionListLabel;

        private const string DefaultSampleScript =
@"// In-scope: AacFlBase aac, AacFlController ctrl, GameObject avatarRoot
// Build a simple ON/OFF FX layer with an MA toggle.
var layer = ctrl.NewLayer(""SampleFx"");
var p = layer.BoolParameter(""SampleOn"");

var off = layer.NewState(""OFF"").WithAnimation(aac.NewClip(""SampleOff"")
    .Toggling(avatarRoot, true));  // dummy: keep avatar visible
var on = layer.NewState(""ON"").WithAnimation(aac.NewClip(""SampleOn"")
    .Toggling(avatarRoot, true));

off.TransitionsTo(on).When(p.IsTrue());
on.TransitionsTo(off).When(p.IsFalse());

// Holder GameObject + MA Merge + Menu
var holder = new GameObject(""AAC_Sample"");
UnityEditor.Undo.RegisterCreatedObjectUndo(holder, ""AAC Sample"");
holder.transform.SetParent(avatarRoot.transform, false);
var maAc = MaAc.Create(holder);
maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);
maAc.NewParameter(p).WithDefaultValue(false);
maAc.EditMenuItemOnSelf().Toggle(p).Name(""Sample Toggle"");

return ""Built SampleFx layer (OFF/ON) + MA toggle holder"";
";

        [MenuItem("UnityAgent/_Debug/Animator-as-Code")]
        public static void Open()
        {
            var w = GetWindow<AacToolsTestWindow>();
            w.titleContent = new GUIContent("AAC Builder (Test)");
            w.minSize = new Vector2(640, 820);
            w.Show();
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            _theme = MD3Theme.Auto();
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !rootVisualElement.styleSheets.Contains(themeSheet))
                rootVisualElement.styleSheets.Add(themeSheet);
            if (compSheet != null && !rootVisualElement.styleSheets.Contains(compSheet))
                rootVisualElement.styleSheets.Add(compSheet);
            _theme.ApplyTo(rootVisualElement);
            rootVisualElement.style.flexGrow = 1;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            rootVisualElement.Add(scroll);

            BuildTargetCard(scroll);
            BuildSessionCard(scroll);
            BuildScriptCard(scroll);
            BuildSessionListCard(scroll);
            BuildResultCard(scroll);
        }

        // ───────────────────── Target ─────────────────────

        private void BuildTargetCard(VisualElement parent)
        {
            var card = MakeCard("Target Avatar & System", MD3CardStyle.Filled);

            card.Add(MakeFieldLabel("Avatar root GameObject"));
            var avatarField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = _avatarRoot,
            };
            avatarField.RegisterValueChangedCallback(evt => _avatarRoot = evt.newValue as GameObject);
            card.Add(avatarField);

            card.Add(MakeTextRow("systemName (session key)", _systemName, v => _systemName = v));

            var hint = new Label("assetDir 空欄時のデフォルト: Assets/UnityAgent_Generated/AnimatorAsCode/{avatarName}");
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.fontSize = 11;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 6;
            card.Add(hint);
            card.Add(MakeTextRow("assetDir (任意)", _assetDir, v => _assetDir = v));

            parent.Add(card);
        }

        // ───────────────────── Session Lifecycle ─────────────────────

        private void BuildSessionCard(VisualElement parent)
        {
            var card = MakeCard("Session Lifecycle", MD3CardStyle.Outlined);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 4;

            var beginBtn = new MD3Button("Begin", MD3ButtonStyle.Filled, size: MD3ButtonSize.Small);
            beginBtn.style.marginRight = 6;
            beginBtn.clicked += () =>
            {
                if (!RequireAvatar()) return;
                var r = AnimatorAsCodeTools.AacBeginSystem(_systemName, _avatarRoot.name, _assetDir);
                ShowResult(r);
                RefreshSessionList();
            };
            row.Add(beginBtn);

            var commitBtn = new MD3Button("Commit", MD3ButtonStyle.Tonal, size: MD3ButtonSize.Small);
            commitBtn.style.marginRight = 6;
            commitBtn.clicked += () =>
            {
                var r = AnimatorAsCodeTools.AacCommitSystem(_systemName);
                ShowResult(r);
                RefreshSessionList();
            };
            row.Add(commitBtn);

            var discardBtn = new MD3Button("Discard", MD3ButtonStyle.Outlined, size: MD3ButtonSize.Small);
            discardBtn.style.marginRight = 6;
            discardBtn.clicked += () =>
            {
                var r = AnimatorAsCodeTools.AacDiscardSession(_systemName);
                ShowResult(r);
                RefreshSessionList();
            };
            row.Add(discardBtn);

            var inspectBtn = new MD3Button("Inspect", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            inspectBtn.style.marginRight = 6;
            inspectBtn.clicked += () =>
            {
                var r = AnimatorAsCodeTools.AacInspectSession(_systemName);
                ShowResult(r);
            };
            row.Add(inspectBtn);

            var listBtn = new MD3Button("List", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            listBtn.clicked += () =>
            {
                var r = AnimatorAsCodeTools.AacListSessions();
                ShowResult(r);
                RefreshSessionList();
            };
            row.Add(listBtn);

            card.Add(row);
            parent.Add(card);
        }

        // ───────────────────── Script Editor ─────────────────────

        private void BuildScriptCard(VisualElement parent)
        {
            var card = MakeCard("AacExecuteScript", MD3CardStyle.Outlined);

            var hint = new Label(
                "In-scope: AacFlBase aac, AacFlController ctrl, GameObject avatarRoot\n" +
                "Auto-imported: System(.Linq, .Collections.Generic), UnityEngine, UnityEditor(.Animations),\n" +
                "  AnimatorAsCode.V1(.VRC, .ModularAvatar), VRC.SDK3.Avatars.Components.\n" +
                "Use `return \"summary\";` to log what you built.");
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.fontSize = 11;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 6;
            card.Add(hint);

            _scriptField = new TextField { multiline = true, value = _scriptText };
            _scriptField.style.minHeight = 260;
            _scriptField.style.whiteSpace = WhiteSpace.Normal;
            var input = _scriptField.Q<VisualElement>("unity-text-input");
            if (input != null)
            {
                input.style.unityFont = (Font)EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf");
                input.style.fontSize = 12;
                input.style.whiteSpace = WhiteSpace.Normal;
            }
            _scriptField.RegisterValueChangedCallback(evt => _scriptText = evt.newValue);
            card.Add(_scriptField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 6;

            var execBtn = new MD3Button("Execute Script", MD3ButtonStyle.Filled);
            execBtn.style.marginRight = 6;
            execBtn.clicked += () =>
            {
                var r = AnimatorAsCodeTools.AacExecuteScript(_systemName, _scriptText);
                ShowResult(r);
                RefreshSessionList();
            };
            btnRow.Add(execBtn);

            var resetBtn = new MD3Button("Reset to sample", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            resetBtn.clicked += () =>
            {
                _scriptText = DefaultSampleScript;
                if (_scriptField != null) _scriptField.value = _scriptText;
            };
            btnRow.Add(resetBtn);

            card.Add(btnRow);
            parent.Add(card);
        }

        // ───────────────────── Session List ─────────────────────

        private void BuildSessionListCard(VisualElement parent)
        {
            var card = MakeCard("Active Sessions", MD3CardStyle.Filled);
            _sessionListLabel = new Label("(use List or Begin to populate)");
            _sessionListLabel.style.whiteSpace = WhiteSpace.Normal;
            _sessionListLabel.style.fontSize = 11;
            _sessionListLabel.style.color = _theme.OnSurfaceVariant;
            _sessionListLabel.style.unityFont = (Font)EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf");
            card.Add(_sessionListLabel);
            parent.Add(card);
        }

        private void RefreshSessionList()
        {
            if (_sessionListLabel == null) return;
            _sessionListLabel.text = AnimatorAsCodeTools.AacListSessions();
        }

        // ───────────────────── Result ─────────────────────

        private void BuildResultCard(VisualElement parent)
        {
            var card = MakeCard("Result", MD3CardStyle.Filled);

            var copyBtn = new MD3Button("Copy to clipboard", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            copyBtn.clicked += () =>
            {
                if (!string.IsNullOrEmpty(_lastResult))
                    EditorGUIUtility.systemCopyBuffer = _lastResult;
            };
            card.Add(copyBtn);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.maxHeight = 320;
            body.style.marginTop = 4;
            body.style.backgroundColor = _theme.SurfaceContainerLow;
            body.style.borderTopLeftRadius = 6;
            body.style.borderTopRightRadius = 6;
            body.style.borderBottomLeftRadius = 6;
            body.style.borderBottomRightRadius = 6;
            body.style.paddingLeft = 8;
            body.style.paddingRight = 8;
            body.style.paddingTop = 6;
            body.style.paddingBottom = 6;
            card.Add(body);

            _resultLabel = new Label(_lastResult);
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.fontSize = 11;
            _resultLabel.style.unityFont = (Font)EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf");
            body.Add(_resultLabel);

            parent.Add(card);
        }

        // ───────────────────── Helpers ─────────────────────

        private MD3Card MakeCard(string title, MD3CardStyle style)
        {
            var card = new MD3Card(title, null, style);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;
            return card;
        }

        private VisualElement MakeTextRow(string label, string initial, Action<string> onChange)
        {
            var wrap = new VisualElement();
            wrap.Add(MakeFieldLabel(label));
            var tf = new MD3TextField("", MD3TextFieldStyle.Outlined);
            tf.Value = initial;
            tf.changed += v => onChange(v ?? "");
            wrap.Add(tf);
            return wrap;
        }

        private Label MakeFieldLabel(string text)
        {
            var l = new Label(text);
            l.style.color = _theme.OnSurfaceVariant;
            l.style.fontSize = 11;
            l.style.marginTop = 6;
            l.style.marginBottom = 2;
            return l;
        }

        private bool RequireAvatar()
        {
            if (_avatarRoot == null)
            {
                ShowResult("Error: Avatar root GameObject is not set. Drag the avatar root from the Hierarchy.");
                return false;
            }
            return true;
        }

        private void ShowResult(string text)
        {
            _lastResult = text ?? "";
            if (_resultLabel != null) _resultLabel.text = _lastResult;
        }
    }
}
#endif
