using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Debug window for the AvatarMask tools in <see cref="AnimatorAdvancedTools"/>.
    /// Each card maps to a single tool, exposes the same parameters, and writes the
    /// raw return string to the Result panel — for verifying ParseBool/bonesOnly/
    /// additive/scene-root behavior without going through the AI loop.
    /// </summary>
    internal class AvatarMaskTestWindow : EditorWindow
    {
        // ─── State ───
        private MD3Theme _theme;

        // Target mask + avatar
        private AvatarMask _targetMask;
        private GameObject _avatarRoot;

        // Create
        private string _createMaskName = "TestMask";
        private string _createSavePath = "Assets/UnityAgent_TestArtifacts";

        // Configure
        private string _bodyPartsInput = "Head=1;LeftArm=0;RightArm=on;LeftLeg=yes;Body=false";

        // Transforms-from-avatar
        private bool _setAllActive = true;
        private bool _additive;
        private bool _bonesOnly = true;

        // Toggle
        private string _transformPathsInput = "Armature/Hips=1;Armature/Hips/Spine=0";

        // Result
        private string _lastResult = "";
        private Label _resultLabel;
        private VisualElement _resultBody;

        // Detected masks
        private ObjectField _maskField;
        private VisualElement _detectedMasksList;
        private Label _detectedMasksHint;

        [MenuItem(UnityAgentMenu.AvatarMaskTest, false, UnityAgentMenu.AvatarMaskTestOrder)]
        public static void Open()
        {
            var w = GetWindow<AvatarMaskTestWindow>();
            w.titleContent = new GUIContent("AvatarMask (Test)");
            w.minSize = new Vector2(520, 760);
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
            BuildCreateCard(scroll);
            BuildConfigureCard(scroll);
            BuildTransformsFromAvatarCard(scroll);
            BuildSetTransformCard(scroll);
            BuildInspectCard(scroll);
            BuildResultCard(scroll);
        }

        // ───────────────────── Target Card ─────────────────────

        private void BuildTargetCard(VisualElement parent)
        {
            var card = MakeCard("Target Mask & Avatar", MD3CardStyle.Filled);

            var maskLabel = MakeFieldLabel("AvatarMask asset");
            card.Add(maskLabel);
            _maskField = new ObjectField
            {
                objectType = typeof(AvatarMask),
                allowSceneObjects = false,
                value = _targetMask,
            };
            _maskField.RegisterValueChangedCallback(evt => _targetMask = evt.newValue as AvatarMask);
            card.Add(_maskField);

            var avatarLabel = MakeFieldLabel("Avatar root GameObject (Animator+Avatar required)");
            avatarLabel.style.marginTop = 8;
            card.Add(avatarLabel);
            var avatarField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = _avatarRoot,
            };
            avatarField.RegisterValueChangedCallback(evt =>
            {
                _avatarRoot = evt.newValue as GameObject;
                RefreshDetectedMasks();
            });
            card.Add(avatarField);

            // ── Detected masks subsection ──
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 10;
            divider.style.marginBottom = 8;
            divider.style.backgroundColor = _theme.OutlineVariant;
            card.Add(divider);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            var headerLbl = MakeFieldLabel("Detected AvatarMasks (referenced by this avatar)");
            header.Add(headerLbl);

            var refreshBtn = new MD3Button("Refresh", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            refreshBtn.clicked += RefreshDetectedMasks;
            header.Add(refreshBtn);
            card.Add(header);

            _detectedMasksHint = new Label("(set an avatar to detect masks)");
            _detectedMasksHint.style.color = _theme.OnSurfaceVariant;
            _detectedMasksHint.style.fontSize = 11;
            _detectedMasksHint.style.unityFontStyleAndWeight = FontStyle.Italic;
            _detectedMasksHint.style.marginTop = 4;
            card.Add(_detectedMasksHint);

            _detectedMasksList = new VisualElement();
            _detectedMasksList.style.marginTop = 4;
            card.Add(_detectedMasksList);

            // Initial population if avatar pre-bound
            RefreshDetectedMasks();

            parent.Add(card);
        }

        // ─── Detected masks logic ───

        private void RefreshDetectedMasks()
        {
            if (_detectedMasksList == null || _detectedMasksHint == null) return;

            _detectedMasksList.Clear();

            if (_avatarRoot == null)
            {
                _detectedMasksHint.text = "(set an avatar to detect masks)";
                _detectedMasksHint.style.display = DisplayStyle.Flex;
                return;
            }

            var masks = DetectMasksFromAvatar(_avatarRoot);

            if (masks.Count == 0)
            {
                _detectedMasksHint.text = "(no AvatarMasks referenced by this avatar)";
                _detectedMasksHint.style.display = DisplayStyle.Flex;
                return;
            }

            _detectedMasksHint.style.display = DisplayStyle.None;

            // Auto-bind if exactly one
            if (masks.Count == 1 && _targetMask == null)
            {
                _targetMask = masks[0];
                if (_maskField != null) _maskField.value = masks[0];
            }

            foreach (var m in masks)
            {
                _detectedMasksList.Add(BuildDetectedMaskRow(m));
            }
        }

        private VisualElement BuildDetectedMaskRow(AvatarMask mask)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;
            row.style.marginBottom = 2;

            string path = AssetDatabase.GetAssetPath(mask);
            string display = string.IsNullOrEmpty(path) ? mask.name : path;
            bool isSelected = _targetMask == mask;

            var lbl = new Label((isSelected ? "● " : "○ ") + display);
            lbl.style.flexGrow = 1;
            lbl.style.flexShrink = 1;
            lbl.style.minWidth = 0;
            lbl.style.fontSize = 11;
            lbl.style.color = isSelected ? _theme.Primary : _theme.OnSurface;
            lbl.style.unityFontStyleAndWeight = isSelected ? FontStyle.Bold : FontStyle.Normal;
            row.Add(lbl);

            var useBtn = new MD3Button(isSelected ? "Selected" : "Use", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            useBtn.style.minWidth = 64;
            useBtn.style.flexShrink = 0;
            useBtn.SetEnabled(!isSelected);
            useBtn.clicked += () =>
            {
                _targetMask = mask;
                if (_maskField != null) _maskField.value = mask;
                RefreshDetectedMasks();
            };
            row.Add(useBtn);

            var pingBtn = new MD3Button("Ping", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            pingBtn.style.minWidth = 48;
            pingBtn.style.flexShrink = 0;
            pingBtn.clicked += () => EditorGUIUtility.PingObject(mask);
            row.Add(pingBtn);

            return row;
        }

        private static List<AvatarMask> DetectMasksFromAvatar(GameObject root)
        {
            var sink = new HashSet<AvatarMask>();
            if (root == null) return new List<AvatarMask>();

            // 1. Animator's direct controller
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController is AnimatorController ac)
                CollectMasksFromController(ac, sink);

            // 2. VRC Avatar Descriptor (SerializedObject reflection — avoids VRC.SDK3 hard reference)
            foreach (var comp in root.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (typeName != "VRCAvatarDescriptor") continue;

                var so = new SerializedObject(comp);
                CollectMasksFromDescriptorLayers(so.FindProperty("baseAnimationLayers"), sink);
                CollectMasksFromDescriptorLayers(so.FindProperty("specialAnimationLayers"), sink);
                break;
            }

            return sink.OrderBy(m => AssetDatabase.GetAssetPath(m) ?? m.name).ToList();
        }

        private static void CollectMasksFromController(AnimatorController ac, HashSet<AvatarMask> sink)
        {
            if (ac == null || ac.layers == null) return;
            foreach (var layer in ac.layers)
                if (layer.avatarMask != null) sink.Add(layer.avatarMask);
        }

        private static void CollectMasksFromDescriptorLayers(SerializedProperty layers, HashSet<AvatarMask> sink)
        {
            if (layers == null || !layers.isArray) return;
            for (int i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);

                // Descriptor-level layer mask
                var maskProp = layer.FindPropertyRelative("mask");
                if (maskProp != null && maskProp.objectReferenceValue is AvatarMask layerMask)
                    sink.Add(layerMask);

                // Nested AnimatorController's per-layer masks
                var isDefault = layer.FindPropertyRelative("isDefault");
                if (isDefault != null && isDefault.boolValue) continue;

                var controllerProp = layer.FindPropertyRelative("animatorController");
                if (controllerProp != null && controllerProp.objectReferenceValue is AnimatorController nested)
                    CollectMasksFromController(nested, sink);
            }
        }

        // ───────────────────── Create Card ─────────────────────

        private void BuildCreateCard(VisualElement parent)
        {
            var card = MakeCard("CreateAvatarMask", MD3CardStyle.Outlined);

            var nameTf = new MD3TextField("Mask name", MD3TextFieldStyle.Outlined,
                placeholder: "Filename will be '<name>.mask'");
            nameTf.Value = _createMaskName;
            nameTf.changed += v => _createMaskName = v ?? "";
            card.Add(nameTf);

            var pathTf = new MD3TextField("Save folder (under Assets/)", MD3TextFieldStyle.Outlined,
                placeholder: "Folder is created if missing");
            pathTf.Value = _createSavePath;
            pathTf.changed += v => _createSavePath = v ?? "";
            pathTf.style.marginTop = 6;
            card.Add(pathTf);

            var btn = new MD3Button("Create mask", MD3ButtonStyle.Filled);
            btn.style.marginTop = 8;
            btn.clicked += () =>
            {
                var r = AnimatorAdvancedTools.CreateAvatarMask(_createMaskName, _createSavePath);
                ShowResult(r);
                // Auto-bind created asset for subsequent operations
                var path = $"{_createSavePath}/{_createMaskName}.mask";
                var created = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
                if (created != null)
                {
                    _targetMask = created;
                    RebuildWindow();
                }
            };
            card.Add(btn);

            parent.Add(card);
        }

        // ───────────────────── Configure Card ─────────────────────

        private void BuildConfigureCard(VisualElement parent)
        {
            var card = MakeCard("ConfigureAvatarMask (humanoid body parts)", MD3CardStyle.Outlined);

            var hint = new Label("Format: 'partName=value;partName=value'\n" +
                                 "Truthy: true, 1, on, yes, enabled. Falsy: anything else.\n" +
                                 "Parts: Root, Body, Head, LeftLeg, RightLeg, LeftArm, RightArm, LeftFingers, RightFingers, LeftFootIK, RightFootIK, LeftHandIK, RightHandIK.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.fontSize = 11;
            card.Add(hint);

            var tf = new MD3TextField("bodyParts string", MD3TextFieldStyle.Outlined);
            tf.Value = _bodyPartsInput;
            tf.changed += v => _bodyPartsInput = v ?? "";
            tf.style.marginTop = 6;
            card.Add(tf);

            var btn = new MD3Button("Configure", MD3ButtonStyle.Tonal);
            btn.style.marginTop = 8;
            btn.clicked += () =>
            {
                if (!RequireMask()) return;
                var r = AnimatorAdvancedTools.ConfigureAvatarMask(
                    AssetDatabase.GetAssetPath(_targetMask), _bodyPartsInput);
                ShowResult(r);
            };
            card.Add(btn);

            parent.Add(card);
        }

        // ─────────────── SetAvatarMaskTransformsFromAvatar ───────────────

        private void BuildTransformsFromAvatarCard(VisualElement parent)
        {
            var card = MakeCard("SetAvatarMaskTransformsFromAvatar", MD3CardStyle.Outlined);

            var hint = new Label(
                "Mirrors Unity's 'Import Skeleton'.\n" +
                "bonesOnly=true (default): SMR.bones ∪ Hips descendants.\n" +
                "additive=false (default): REPLACE existing entries.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.fontSize = 11;
            card.Add(hint);

            var switchesContainer = new VisualElement();
            switchesContainer.style.flexDirection = FlexDirection.Column;
            switchesContainer.style.marginTop = 6;
            card.Add(switchesContainer);

            switchesContainer.Add(MakeLabeledSwitch("setAllActive", _setAllActive, v => _setAllActive = v));
            switchesContainer.Add(MakeLabeledSwitch("additive (append + dedupe)", _additive, v => _additive = v));
            switchesContainer.Add(MakeLabeledSwitch("bonesOnly (skeleton-only collection)", _bonesOnly, v => _bonesOnly = v));

            var btn = new MD3Button("Populate transforms", MD3ButtonStyle.Tonal);
            btn.style.marginTop = 8;
            btn.clicked += () =>
            {
                if (!RequireMask() || !RequireAvatar()) return;
                var r = AnimatorAdvancedTools.SetAvatarMaskTransformsFromAvatar(
                    AssetDatabase.GetAssetPath(_targetMask),
                    _avatarRoot.name,
                    _setAllActive,
                    _additive,
                    _bonesOnly);
                ShowResult(r);
            };
            card.Add(btn);

            parent.Add(card);
        }

        // ───────────────────── SetAvatarMaskTransform ─────────────────────

        private void BuildSetTransformCard(VisualElement parent)
        {
            var card = MakeCard("SetAvatarMaskTransform (toggle individual paths)", MD3CardStyle.Outlined);

            var hint = new Label("Format: 'path=value;path=value' — same bool tokens as Configure.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.fontSize = 11;
            card.Add(hint);

            var tf = new MD3TextField("paths string", MD3TextFieldStyle.Outlined);
            tf.Value = _transformPathsInput;
            tf.changed += v => _transformPathsInput = v ?? "";
            tf.style.marginTop = 6;
            card.Add(tf);

            var btn = new MD3Button("Apply", MD3ButtonStyle.Tonal);
            btn.style.marginTop = 8;
            btn.clicked += () =>
            {
                if (!RequireMask()) return;
                var r = AnimatorAdvancedTools.SetAvatarMaskTransform(
                    AssetDatabase.GetAssetPath(_targetMask), _transformPathsInput);
                ShowResult(r);
            };
            card.Add(btn);

            parent.Add(card);
        }

        // ───────────────────── Inspect Card ─────────────────────

        private void BuildInspectCard(VisualElement parent)
        {
            var card = MakeCard("InspectAvatarMask", MD3CardStyle.Outlined);

            var btn = new MD3Button("Inspect", MD3ButtonStyle.Outlined);
            btn.clicked += () =>
            {
                if (!RequireMask()) return;
                var r = AnimatorAdvancedTools.InspectAvatarMask(AssetDatabase.GetAssetPath(_targetMask));
                ShowResult(r);
            };
            card.Add(btn);

            var pingBtn = new MD3Button("Ping mask in Project", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            pingBtn.style.marginTop = 4;
            pingBtn.clicked += () =>
            {
                if (_targetMask != null) EditorGUIUtility.PingObject(_targetMask);
            };
            card.Add(pingBtn);

            parent.Add(card);
        }

        // ───────────────────── Result Card ─────────────────────

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

            _resultBody = new ScrollView(ScrollViewMode.Vertical);
            _resultBody.style.maxHeight = 320;
            _resultBody.style.marginTop = 4;
            _resultBody.style.backgroundColor = _theme.SurfaceContainerLow;
            _resultBody.style.borderTopLeftRadius = 6;
            _resultBody.style.borderTopRightRadius = 6;
            _resultBody.style.borderBottomLeftRadius = 6;
            _resultBody.style.borderBottomRightRadius = 6;
            _resultBody.style.paddingLeft = 8;
            _resultBody.style.paddingRight = 8;
            _resultBody.style.paddingTop = 6;
            _resultBody.style.paddingBottom = 6;
            card.Add(_resultBody);

            _resultLabel = new Label(_lastResult);
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.fontSize = 11;
            _resultLabel.style.unityFont = (Font)EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf");
            _resultBody.Add(_resultLabel);

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

        private VisualElement MakeLabeledSwitch(string label, bool initial, Action<bool> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;

            var sw = new MD3Switch(initial);
            sw.changed += v => onChange(v);
            row.Add(sw);

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.fontSize = 12;
            lbl.style.marginLeft = 8;
            row.Add(lbl);

            return row;
        }

        private Label MakeFieldLabel(string text)
        {
            var l = new Label(text);
            l.style.color = _theme.OnSurfaceVariant;
            l.style.fontSize = 11;
            l.style.marginTop = 2;
            l.style.marginBottom = 2;
            return l;
        }

        private bool RequireMask()
        {
            if (_targetMask == null)
            {
                ShowResult("Error: Target AvatarMask is not set. Drag a .mask asset into the Target Mask field, or use Create first.");
                return false;
            }
            return true;
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

        private void RebuildWindow()
        {
            CreateGUI();
        }
    }
}
