using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>インライン Transform エディタパネル (UI Toolkit 版)。</summary>
    internal class TransformPanelView : VisualElement
    {
        readonly MD3Theme _theme;

        VisualElement _headerRow;
        Label _nameLabel;
        Label _chevronLabel;
        VisualElement _contentContainer;

        // SegmentedButton for tool mode
        MD3SegmentedButton _toolButtons;

        // Axis fields
        FloatField[] _posFields = new FloatField[3];
        FloatField[] _rotFields = new FloatField[3];
        FloatField[] _sclFields = new FloatField[3];

        bool _isOpen = true;
        GameObject _target;
        int _targetId;
        int _nudgeStepIndex = 1;

        static readonly float[] NudgeSteps = { 0.001f, 0.01f, 0.1f, 1f };
        static readonly string[] NudgeLabels = { "\u00B10.001", "\u00B10.01", "\u00B10.1", "\u00B11" };
        static readonly string[] ToolLabels = { "W", "E", "R", "T" };
        static readonly Tool[] ToolModes = { Tool.Move, Tool.Rotate, Tool.Scale, Tool.Rect };

        static readonly Color AxisColorX = new Color(0.9f, 0.35f, 0.35f, 1f);
        static readonly Color AxisColorY = new Color(0.35f, 0.78f, 0.35f, 1f);
        static readonly Color AxisColorZ = new Color(0.35f, 0.55f, 0.9f, 1f);

        const string PanelOpenPref = "UnityAgent_TransformPanelOpen";

        public TransformPanelView(MD3Theme theme)
        {
            _theme = theme;

            style.marginLeft = 8;
            style.marginRight = 8;
            style.marginBottom = 4;
            style.backgroundColor = theme.SurfaceContainerHigh;
            style.borderTopLeftRadius = 12;
            style.borderTopRightRadius = 12;
            style.borderBottomLeftRadius = 12;
            style.borderBottomRightRadius = 12;
            style.display = DisplayStyle.None;

            _isOpen = EditorPrefs.GetBool(PanelOpenPref, true);

            BuildHeader();
            BuildContent();

            UpdateContentVisibility();

            // Poll for selection changes
            schedule.Execute(UpdateSelection).Every(200);
        }

        void BuildHeader()
        {
            _headerRow = new MD3Row();
            _headerRow.style.alignItems = Align.Center;
            _headerRow.style.height = 28;
            _headerRow.style.paddingLeft = 10;
            _headerRow.style.paddingRight = 10;

            _chevronLabel = new Label(_isOpen ? "\u25BC" : "\u25B6");
            _chevronLabel.style.fontSize = 10;
            _chevronLabel.style.color = _theme.OnSurfaceVariant;
            _chevronLabel.style.width = 16;
            _headerRow.Add(_chevronLabel);

            _nameLabel = new Label("");
            _nameLabel.style.fontSize = 12;
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.color = _theme.OnSurface;
            _headerRow.Add(_nameLabel);

            _headerRow.RegisterCallback<ClickEvent>(evt =>
            {
                _isOpen = !_isOpen;
                EditorPrefs.SetBool(PanelOpenPref, _isOpen);
                _chevronLabel.text = _isOpen ? "\u25BC" : "\u25B6";
                UpdateContentVisibility();
            });

            Add(_headerRow);
        }

        void BuildContent()
        {
            _contentContainer = new MD3Column(gap: 4f);
            _contentContainer.style.paddingLeft = 10;
            _contentContainer.style.paddingRight = 10;
            _contentContainer.style.paddingBottom = 6;

            // Toolbar row: tool mode + nudge step
            var toolbar = new MD3Row(gap: 8f);
            toolbar.style.alignItems = Align.Center;

            // Tool mode buttons (W/E/R/T) — using chips as substitute
            for (int i = 0; i < ToolLabels.Length; i++)
            {
                int idx = i;
                var btn = new MD3Button(ToolLabels[i], MD3ButtonStyle.Outlined, null, MD3ButtonSize.Small);
                btn.style.minWidth = 32;
                btn.clicked += () =>
                {
                    UnityEditor.Tools.current = ToolModes[idx];
                };
                toolbar.Add(btn);
            }

            toolbar.Add(new MD3Spacer());

            // Nudge step chip
            var nudgeChip = new MD3Chip(NudgeLabels[_nudgeStepIndex], false);
            nudgeChip.toggled += _ =>
            {
                _nudgeStepIndex = (_nudgeStepIndex + 1) % NudgeSteps.Length;
                nudgeChip.Text = NudgeLabels[_nudgeStepIndex];
            };
            toolbar.Add(nudgeChip);

            // Frame button
            var frameBtn = new MD3IconButton("\u2316", MD3IconButtonStyle.Tonal, MD3IconButtonSize.Small);
            frameBtn.clicked += () => SceneView.FrameLastActiveSceneView();
            toolbar.Add(frameBtn);

            _contentContainer.Add(toolbar);

            // Transform rows
            _contentContainer.Add(CreateTransformRow("P", new Color(0.95f, 0.45f, 0.45f, 1f), _posFields));
            _contentContainer.Add(CreateTransformRow("R", new Color(0.45f, 0.82f, 0.45f, 1f), _rotFields));
            _contentContainer.Add(CreateTransformRow("S", new Color(0.45f, 0.6f, 0.95f, 1f), _sclFields));

            Add(_contentContainer);
        }

        VisualElement CreateTransformRow(string label, Color labelColor, FloatField[] fields)
        {
            var row = new MD3Row(gap: 2f);
            row.style.alignItems = Align.Center;
            row.style.height = 22;

            var rowLabel = new Label(label);
            rowLabel.style.fontSize = 12;
            rowLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            rowLabel.style.color = labelColor;
            rowLabel.style.width = 16;
            rowLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(rowLabel);

            Color[] axisColors = { AxisColorX, AxisColorY, AxisColorZ };

            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                var field = new FloatField();
                field.style.flexGrow = 1;
                field.style.minWidth = 40;
                field.style.borderBottomWidth = 2;
                field.style.borderBottomColor = new Color(axisColors[i].r, axisColors[i].g, axisColors[i].b, 0.6f);

                field.RegisterValueChangedCallback(evt => ApplyTransformValue());
                fields[i] = field;
                row.Add(field);
            }

            // Reset button
            var resetBtn = new MD3IconButton("\u21BA", MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
            resetBtn.clicked += () =>
            {
                Vector3 resetVal = label == "S" ? Vector3.one : Vector3.zero;
                fields[0].value = resetVal.x;
                fields[1].value = resetVal.y;
                fields[2].value = resetVal.z;
                ApplyTransformValue();
            };
            row.Add(resetBtn);

            return row;
        }

        void ApplyTransformValue()
        {
            if (_target == null) return;
            var t = _target.transform;
            Undo.RecordObject(t, "Transform Panel");

            t.localPosition = new Vector3(_posFields[0].value, _posFields[1].value, _posFields[2].value);
            t.localEulerAngles = new Vector3(_rotFields[0].value, _rotFields[1].value, _rotFields[2].value);
            t.localScale = new Vector3(_sclFields[0].value, _sclFields[1].value, _sclFields[2].value);
        }

        void UpdateSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                _target = null;
                _targetId = 0;
                style.display = DisplayStyle.None;
                return;
            }

            int selId = go.GetInstanceID();
            if (selId != _targetId)
            {
                _target = go;
                _targetId = selId;
                string name = go.name;
                if (name.Length > 30) name = name.Substring(0, 27) + "...";
                _nameLabel.text = name;
            }

            style.display = DisplayStyle.Flex;

            if (_target != null && _isOpen)
                ReadTransformValues();
        }

        void ReadTransformValues()
        {
            if (_target == null) return;
            var t = _target.transform;

            SetFieldWithoutNotify(_posFields[0], t.localPosition.x);
            SetFieldWithoutNotify(_posFields[1], t.localPosition.y);
            SetFieldWithoutNotify(_posFields[2], t.localPosition.z);

            SetFieldWithoutNotify(_rotFields[0], t.localEulerAngles.x);
            SetFieldWithoutNotify(_rotFields[1], t.localEulerAngles.y);
            SetFieldWithoutNotify(_rotFields[2], t.localEulerAngles.z);

            SetFieldWithoutNotify(_sclFields[0], t.localScale.x);
            SetFieldWithoutNotify(_sclFields[1], t.localScale.y);
            SetFieldWithoutNotify(_sclFields[2], t.localScale.z);
        }

        static void SetFieldWithoutNotify(FloatField field, float value)
        {
            if (Mathf.Abs(field.value - value) > 0.0001f)
                field.SetValueWithoutNotify(value);
        }

        void UpdateContentVisibility()
        {
            _contentContainer.style.display = _isOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
