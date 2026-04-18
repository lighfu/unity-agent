using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Test window for <see cref="AmbientOcclusionBakeTools"/>. Exposes the tool's
    /// parameters through MD3SDK controls and drives its IEnumerator bake loop.
    /// </summary>
    internal class AmbientOcclusionBakeTestWindow : EditorWindow
    {
        // ─── State ───
        private MD3Theme _theme;
        private Renderer _targetRenderer;
        private readonly List<Renderer> _occluderRenderers = new List<Renderer>();
        private int _modeIndex;        // 0 = texel, 1 = vertex
        private int _qualityIndex = 1; // 0 low / 1 medium / 2 high
        private int _samplesOverride;  // 0 = preset
        private float _maxDistance = 0.5f;
        private float _bias = 0.001f;
        private float _intensity = 1.0f;
        private string _outputPath = "";

        private IEnumerator _running;
        private bool _executing;
        private string _resultMessage = "";

        // ─── UI refs for enabling/disabling ───
        private MD3Button _runButton;
        private Label _progressStatusLbl;
        private Label _progressDetailLbl;
        private VisualElement _progressBarFill;
        private VisualElement _progressBarContainer;
        private Label _resultLabel;
        private VisualElement _occluderListContainer;

        // ─── Constants ───
        private static readonly string[] ModeLabels = { "Texel (PNG)", "Vertex (mesh.colors)" };
        private static readonly string[] QualityLabels = { "Low (32)", "Medium (64)", "High (128)" };
        private static readonly string[] QualityValues = { "low", "medium", "high" };

        [MenuItem("Window/紫陽花広場/AO Bake (Test)")]
        public static void Open()
        {
            var window = GetWindow<AmbientOcclusionBakeTestWindow>();
            window.titleContent = new GUIContent("AO Bake (Test)");
            window.minSize = new Vector2(460, 560);
            window.Show();
        }

        private void OnDisable()
        {
            if (_executing)
            {
                // Dispose so the IEnumerator's finally block runs (destroys temp colliders).
                (_running as IDisposable)?.Dispose();
                _running = null;
                _executing = false;
                ToolProgress.Clear();
            }
        }

        private void Update()
        {
            if (_running == null) return;

            try
            {
                if (!_running.MoveNext())
                {
                    _running = null;
                    _executing = false;
                    UpdateProgressDisplay();
                    UpdateRunButtonEnabled();
                    Repaint();
                    return;
                }
                if (_running.Current is string s)
                {
                    _resultMessage = s;
                    if (_resultLabel != null) _resultLabel.text = s;
                }
                UpdateProgressDisplay();
                Repaint();
            }
            catch (Exception e)
            {
                _resultMessage = $"Error: {e.Message}\n{e.StackTrace}";
                if (_resultLabel != null) _resultLabel.text = _resultMessage;
                _running = null;
                _executing = false;
                ToolProgress.Clear();
                UpdateProgressDisplay();
                UpdateRunButtonEnabled();
                Repaint();
            }
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
            BuildModeCard(scroll);
            BuildQualityCard(scroll);
            BuildAdvancedCard(scroll);
            BuildActionCard(scroll);
            BuildProgressCard(scroll);
            BuildResultCard(scroll);
        }

        // ────────────────────── Target Card ──────────────────────

        private void BuildTargetCard(VisualElement parent)
        {
            var card = new MD3Card("Target & Occluders", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            // ── Target ──
            var targetLabel = new Label("Target (MeshRenderer / SkinnedMeshRenderer)");
            targetLabel.style.color = _theme.OnSurfaceVariant;
            targetLabel.style.fontSize = 11;
            targetLabel.style.marginTop = 2;
            card.Add(targetLabel);

            var targetField = new ObjectField
            {
                objectType = typeof(Renderer),
                allowSceneObjects = true,
                value = _targetRenderer,
            };
            targetField.style.marginTop = 2;
            targetField.RegisterValueChangedCallback(evt =>
            {
                _targetRenderer = evt.newValue as Renderer;
            });
            card.Add(targetField);

            // ── Divider ──
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.marginTop = 10;
            divider.style.marginBottom = 8;
            divider.style.backgroundColor = _theme.OutlineVariant;
            card.Add(divider);

            // ── Occluders header ──
            var occHeader = new VisualElement();
            occHeader.style.flexDirection = FlexDirection.Row;
            occHeader.style.justifyContent = Justify.SpaceBetween;
            occHeader.style.alignItems = Align.Center;

            var occLabel = new Label("Occluders (empty = self-only)");
            occLabel.style.color = _theme.OnSurfaceVariant;
            occLabel.style.fontSize = 11;
            occHeader.Add(occLabel);

            var addBtn = new MD3Button("+ Add", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            addBtn.clicked += () =>
            {
                _occluderRenderers.Add(null);
                RebuildOccluderList();
            };
            occHeader.Add(addBtn);
            card.Add(occHeader);

            _occluderListContainer = new VisualElement();
            _occluderListContainer.style.marginTop = 4;
            card.Add(_occluderListContainer);
            RebuildOccluderList();

            parent.Add(card);
        }

        private void RebuildOccluderList()
        {
            if (_occluderListContainer == null) return;
            _occluderListContainer.Clear();

            if (_occluderRenderers.Count == 0)
            {
                var hint = new Label("(none — target is the only occluder)");
                hint.style.color = _theme.OnSurfaceVariant;
                hint.style.fontSize = 11;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                _occluderListContainer.Add(hint);
                return;
            }

            for (int i = 0; i < _occluderRenderers.Count; i++)
            {
                int idx = i; // capture

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                row.style.marginBottom = 2;

                var field = new ObjectField
                {
                    objectType = typeof(Renderer),
                    allowSceneObjects = true,
                    value = _occluderRenderers[idx],
                };
                field.style.flexGrow = 1;
                field.style.flexShrink = 1;
                field.style.minWidth = 0;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (idx < _occluderRenderers.Count)
                        _occluderRenderers[idx] = evt.newValue as Renderer;
                });
                row.Add(field);

                // Explicit fixed-size remove button. MD3Button Text style can render
                // invisibly when its row competes with a flex-grow ObjectField — force
                // a min/max width and a distinct outlined style so it stays tappable.
                var removeBtn = new MD3Button("Remove", MD3ButtonStyle.Outlined, size: MD3ButtonSize.Small);
                removeBtn.style.marginLeft = 6;
                removeBtn.style.minWidth = 72;
                removeBtn.style.flexShrink = 0;
                removeBtn.clicked += () =>
                {
                    if (idx < _occluderRenderers.Count)
                    {
                        _occluderRenderers.RemoveAt(idx);
                        RebuildOccluderList();
                    }
                };
                row.Add(removeBtn);

                _occluderListContainer.Add(row);
            }
        }

        // ────────────────────── Mode Card ──────────────────────

        private void BuildModeCard(VisualElement parent)
        {
            var card = new MD3Card("Output Mode", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            var seg = new MD3SegmentedButton(ModeLabels, _modeIndex);
            seg.changed += idx => _modeIndex = idx;
            card.Add(seg);

            var hint = new Label(
                "Texel: UV-based PNG written to Assets/UnityAgent_Generated/AmbientOcclusion/\n" +
                "Vertex: mesh.colors.rgb written, saved as new .asset, Renderer.sharedMesh swapped.");
            hint.style.fontSize = 11;
            hint.style.color = _theme.OnSurfaceVariant;
            hint.style.marginTop = 6;
            hint.style.whiteSpace = WhiteSpace.Normal;
            card.Add(hint);

            parent.Add(card);
        }

        // ────────────────────── Quality Card ──────────────────────

        private void BuildQualityCard(VisualElement parent)
        {
            var card = new MD3Card("Quality", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            var qSeg = new MD3SegmentedButton(QualityLabels, _qualityIndex);
            qSeg.changed += idx => _qualityIndex = idx;
            card.Add(qSeg);

            var samplesRow = BuildSliderRow("Samples Override (0 = use preset)",
                _samplesOverride, 0, 512,
                v =>
                {
                    _samplesOverride = Mathf.RoundToInt(v);
                },
                formatter: v => Mathf.RoundToInt(v).ToString());
            samplesRow.style.marginTop = 6;
            card.Add(samplesRow);

            parent.Add(card);
        }

        // ────────────────────── Advanced Card ──────────────────────

        private void BuildAdvancedCard(VisualElement parent)
        {
            var card = new MD3Card("Advanced", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            card.Add(BuildSliderRow("Max Distance (m)",
                _maxDistance, 0.05f, 3.0f,
                v => _maxDistance = v));

            card.Add(BuildSliderRow("Bias",
                _bias, 0.0001f, 0.02f,
                v => _bias = v,
                formatter: v => v.ToString("F4")));

            card.Add(BuildSliderRow("Intensity",
                _intensity, 0.1f, 3.0f,
                v => _intensity = v));

            var outputTf = new MD3TextField("Output path (blank = auto)",
                MD3TextFieldStyle.Outlined,
                placeholder: "e.g. Assets/Generated/custom_ao.png (.asset for vertex)");
            outputTf.Value = _outputPath;
            outputTf.changed += v => _outputPath = v ?? "";
            outputTf.style.marginTop = 6;
            card.Add(outputTf);

            parent.Add(card);
        }

        // ────────────────────── Action Card ──────────────────────

        private void BuildActionCard(VisualElement parent)
        {
            var card = new MD3Card("Run", null, MD3CardStyle.Filled);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            _runButton = new MD3Button("Bake AO", MD3ButtonStyle.Filled);
            _runButton.clicked += StartBake;
            card.Add(_runButton);

            parent.Add(card);
        }

        // ────────────────────── Progress Card ──────────────────────

        private void BuildProgressCard(VisualElement parent)
        {
            var card = new MD3Card("Progress", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;

            _progressStatusLbl = new Label("Idle");
            _progressStatusLbl.style.color = _theme.OnSurface;
            _progressStatusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(_progressStatusLbl);

            _progressBarContainer = new VisualElement();
            _progressBarContainer.style.height = 10;
            _progressBarContainer.style.marginTop = 4;
            _progressBarContainer.style.marginBottom = 4;
            _progressBarContainer.style.backgroundColor = _theme.SurfaceVariant;
            _progressBarContainer.style.borderTopLeftRadius = 5;
            _progressBarContainer.style.borderTopRightRadius = 5;
            _progressBarContainer.style.borderBottomLeftRadius = 5;
            _progressBarContainer.style.borderBottomRightRadius = 5;

            _progressBarFill = new VisualElement();
            _progressBarFill.style.height = 10;
            _progressBarFill.style.width = new Length(0, LengthUnit.Percent);
            _progressBarFill.style.backgroundColor = _theme.Primary;
            _progressBarFill.style.borderTopLeftRadius = 5;
            _progressBarFill.style.borderBottomLeftRadius = 5;
            _progressBarContainer.Add(_progressBarFill);
            card.Add(_progressBarContainer);

            _progressDetailLbl = new Label("");
            _progressDetailLbl.style.fontSize = 11;
            _progressDetailLbl.style.color = _theme.OnSurfaceVariant;
            _progressDetailLbl.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_progressDetailLbl);

            parent.Add(card);
        }

        // ────────────────────── Result Card ──────────────────────

        private void BuildResultCard(VisualElement parent)
        {
            var card = new MD3Card("Result", null, MD3CardStyle.Outlined);
            card.style.marginLeft = 8;
            card.style.marginRight = 8;
            card.style.marginTop = 8;
            card.style.marginBottom = 8;

            _resultLabel = new Label(string.IsNullOrEmpty(_resultMessage) ? "(not run)" : _resultMessage);
            _resultLabel.style.color = _theme.OnSurface;
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.selection.isSelectable = true;
            card.Add(_resultLabel);

            var pingBtn = new MD3Button("Ping last output in Project", MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            pingBtn.clicked += () =>
            {
                if (string.IsNullOrEmpty(_resultMessage)) return;
                // Extract quoted path: Success: ... 'Assets/.../foo.png' ...
                int q1 = _resultMessage.IndexOf('\'');
                int q2 = q1 >= 0 ? _resultMessage.IndexOf('\'', q1 + 1) : -1;
                if (q1 < 0 || q2 < 0) return;
                var path = _resultMessage.Substring(q1 + 1, q2 - q1 - 1);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            };
            card.Add(pingBtn);

            parent.Add(card);
        }

        // ────────────────────── Actions ──────────────────────

        private void StartBake()
        {
            if (_executing) return;
            if (_targetRenderer == null)
            {
                _resultMessage = "Error: target renderer is not set.";
                if (_resultLabel != null) _resultLabel.text = _resultMessage;
                return;
            }

            string targetPath = GetHierarchyPath(_targetRenderer.transform);

            var occluderPaths = new System.Text.StringBuilder();
            for (int i = 0; i < _occluderRenderers.Count; i++)
            {
                var r = _occluderRenderers[i];
                if (r == null) continue;
                if (occluderPaths.Length > 0) occluderPaths.Append(';');
                occluderPaths.Append(GetHierarchyPath(r.transform));
            }

            _resultMessage = "";
            if (_resultLabel != null) _resultLabel.text = "Running…";

            string mode = _modeIndex == 0 ? "texel" : "vertex";
            string quality = QualityValues[Mathf.Clamp(_qualityIndex, 0, QualityValues.Length - 1)];

            _executing = true;
            UpdateRunButtonEnabled();

            _running = AmbientOcclusionBakeTools.BakeAmbientOcclusion(
                targetPath,
                occluderPaths.ToString(),
                mode,
                quality,
                _samplesOverride,
                _maxDistance,
                _bias,
                _intensity,
                _outputPath ?? "");
        }

        private void UpdateProgressDisplay()
        {
            if (_progressStatusLbl == null) return;

            if (ToolProgress.IsActive)
            {
                _progressStatusLbl.text = ToolProgress.Status ?? "Working…";
                _progressBarFill.style.width = new Length(Mathf.Clamp01(ToolProgress.Progress) * 100f, LengthUnit.Percent);
                _progressDetailLbl.text = ToolProgress.Detail ?? "";
            }
            else
            {
                _progressStatusLbl.text = _executing ? "Starting…" : "Idle";
                _progressBarFill.style.width = new Length(_executing ? 0 : 0, LengthUnit.Percent);
                if (!_executing) _progressDetailLbl.text = "";
            }
        }

        private void UpdateRunButtonEnabled()
        {
            if (_runButton == null) return;
            _runButton.SetEnabled(!_executing);
        }

        // ────────────────────── Helpers ──────────────────────

        private VisualElement BuildSliderRow(string label, float initial, float min, float max,
            Action<float> onChange,
            Func<float, string> formatter = null)
        {
            var row = new VisualElement();
            row.style.marginTop = 4;
            row.style.marginBottom = 4;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;

            var lbl = new Label(label);
            lbl.style.color = _theme.OnSurface;
            lbl.style.fontSize = 12;
            header.Add(lbl);

            string Fmt(float v) => formatter != null ? formatter(v) : v.ToString("F3");

            var valueLbl = new Label(Fmt(initial));
            valueLbl.style.color = _theme.OnSurfaceVariant;
            valueLbl.style.fontSize = 11;
            header.Add(valueLbl);

            row.Add(header);

            var slider = new MD3Slider(initial, min, max);
            slider.changed += v => { valueLbl.text = Fmt(v); onChange(v); };
            row.Add(slider);

            return row;
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "";
            string path = t.name;
            var cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }
    }
}
