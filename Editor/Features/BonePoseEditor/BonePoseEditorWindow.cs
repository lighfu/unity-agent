using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class BonePoseEditorWindow : EditorWindow
    {
        // ── Constants ──
        private const float LabelW = 80f;
        private const float SummaryH = 24f;
        private const float GroupH = 18f;
        private const float BoneRowH = 15f;

        private static readonly Color[] GroupColors =
        {
            new Color(0.8f, 0.8f, 0.85f),  // Body
            new Color(0.3f, 0.55f, 1f),     // L.Arm
            new Color(0.55f, 0.75f, 1f),    // R.Arm
            new Color(0.25f, 0.8f, 0.35f),  // L.Leg
            new Color(0.5f, 0.9f, 0.55f),   // R.Leg
            new Color(0.7f, 0.4f, 0.9f),    // L.Fing
            new Color(0.85f, 0.6f, 1f),     // R.Fing
        };

        // ── Fields ──
        private Animator _animator;
        private Vector2 _dopeScrollPos;
        private bool[] _dopeGroupFoldouts;

        // Playback
        private double _playStartRealTime;
        private float _playStartAnimTime;

        // Dope sheet interaction
        private bool _isScrubbing;
        private PoseKeyframe _draggingMarker;
        private int _hoveredMarkerIndex = -1;

        // Styles
        private GUIStyle _autoKeyOnStyle;

        [MenuItem("Window/紫陽花広場/Bone Pose Editor")]
        private static void Open()
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
            GetWindow<BonePoseEditorWindow>(M("ボーンポーズエディタ"));
        }

        private void OnEnable()
        {
            _dopeGroupFoldouts = new bool[BonePoseEditorState.BoneGroups.Length];
            Selection.selectionChanged += OnSelectionChanged;
            TryAutoDetectAnimator();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            StopPlayback();
        }

        private void OnDestroy() => StopPlayback();

        private void OnSelectionChanged()
        {
            if (!BonePoseEditorState.IsActive)
            {
                var go = Selection.activeGameObject;
                if (go != null)
                {
                    var anim = go.GetComponent<Animator>()
                               ?? go.GetComponentInParent<Animator>();
                    if (anim != null && anim.isHuman)
                        _animator = anim;
                }
            }
            Repaint();
        }

        private void TryAutoDetectAnimator()
        {
            if (_animator != null && _animator.isHuman) return;

            // 1. Try current selection
            if (Selection.activeGameObject != null)
            {
                var anim = Selection.activeGameObject.GetComponent<Animator>()
                           ?? Selection.activeGameObject.GetComponentInParent<Animator>();
                if (anim != null && anim.isHuman)
                {
                    _animator = anim;
                    return;
                }
            }

            // 2. Find all Humanoid animators in scene, pick the first active one
            var animators = Object.FindObjectsOfType<Animator>();
            foreach (var a in animators)
            {
                if (a.isHuman && a.gameObject.activeInHierarchy)
                {
                    _animator = a;
                    return;
                }
            }
        }

        // ══════════════════════════════════════════
        //  OnGUI — clean sectioned layout
        // ══════════════════════════════════════════

        private void OnGUI()
        {
            DrawHeader();
            if (!BonePoseEditorState.IsActive) return;

            DrawTransportBar();
            BonePoseEditorLayerSection.Draw();
            DrawDopeSheetSection();
            DrawKeyframeActions();
            DrawPoseInspector();
            BonePoseEditorGraphSection.Draw();
            DrawIKPanel();
            BonePoseEditorLibrarySection.Draw();
            DrawSaveSection();
        }

        // ── Section 1: Header ──

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsActive);
            _animator = (Animator)EditorGUILayout.ObjectField(
                _animator, typeof(Animator), true, GUILayout.Height(20));
            if (!BonePoseEditorState.IsActive
                && GUILayout.Button("\u2727", GUILayout.Width(22), GUILayout.Height(20)))
            {
                _animator = null;
                TryAutoDetectAnimator();
            }
            EditorGUI.EndDisabledGroup();

            if (!BonePoseEditorState.IsActive)
            {
                EditorGUI.BeginDisabledGroup(
                    _animator == null || (_animator != null && !_animator.isHuman));
                if (GUILayout.Button(M("編集"), GUILayout.Width(50), GUILayout.Height(20)))
                {
                    BonePoseEditorState.Activate(_animator);
                    _dopeGroupFoldouts = new bool[BonePoseEditorState.BoneGroups.Length];
                    SceneView.RepaintAll();
                }
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button(M("停止"), GUILayout.Width(50), GUILayout.Height(20)))
                {
                    StopPlayback();
                    BonePoseEditorState.Deactivate();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = bg;
            }

            EditorGUILayout.EndHorizontal();

            if (_animator != null && !_animator.isHuman)
                EditorGUILayout.HelpBox(M("Humanoidではありません。"), MessageType.Warning);

            if (!BonePoseEditorState.IsActive) return;

            // Settings row
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            BonePoseEditorState.SymmetryEnabled = GUILayout.Toggle(
                BonePoseEditorState.SymmetryEnabled, M("対称"),
                EditorStyles.toolbarButton, GUILayout.Width(64));
            BonePoseEditorState.UseWorldRotation = GUILayout.Toggle(
                BonePoseEditorState.UseWorldRotation, M("ワールド"),
                EditorStyles.toolbarButton, GUILayout.Width(44));
            BonePoseEditorState.OnionSkinEnabled = GUILayout.Toggle(
                BonePoseEditorState.OnionSkinEnabled, M("オニオン"),
                EditorStyles.toolbarButton, GUILayout.Width(44));
            BonePoseEditorState.ShowGraphEditor = GUILayout.Toggle(
                BonePoseEditorState.ShowGraphEditor, M("グラフ"),
                EditorStyles.toolbarButton, GUILayout.Width(44));
            BonePoseEditorState.IKEnabled = GUILayout.Toggle(
                BonePoseEditorState.IKEnabled, M("IK"),
                EditorStyles.toolbarButton, GUILayout.Width(28));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ── Section 2: Transport Bar ──

        private void DrawTransportBar()
        {
            // Row 1: Auto-Key | Nav | Play | Curve | Len | FPS
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawAutoKeyButton();
            GUILayout.Space(2);

            // Navigation
            EditorGUI.BeginDisabledGroup(
                BonePoseEditorState.IsPlaying
                || BonePoseEditorState.Keyframes.Count == 0);
            if (GUILayout.Button("|\u25C0", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                if (BonePoseEditorState.Keyframes.Count > 0)
                    NavigateAndApply(BonePoseEditorState.Keyframes[0].Time);
            }
            if (GUILayout.Button("\u25C0", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                var t = BonePoseEditorState.GetPrevKeyframeTime();
                if (t.HasValue) NavigateAndApply(t.Value);
            }
            if (GUILayout.Button("\u25B6", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                var t = BonePoseEditorState.GetNextKeyframeTime();
                if (t.HasValue) NavigateAndApply(t.Value);
            }
            if (GUILayout.Button("\u25B6|", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                var kfs = BonePoseEditorState.Keyframes;
                if (kfs.Count > 0) NavigateAndApply(kfs[kfs.Count - 1].Time);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(2);

            // Play/Stop
            if (BonePoseEditorState.IsPlaying)
            {
                if (GUILayout.Button("\u25A0", EditorStyles.toolbarButton, GUILayout.Width(24)))
                    StopPlayback();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(BonePoseEditorState.Keyframes.Count < 2);
                if (GUILayout.Button("\u25B6", EditorStyles.toolbarButton, GUILayout.Width(24)))
                    StartPlayback();
                EditorGUI.EndDisabledGroup();
            }

            BonePoseEditorState.LoopPlayback = GUILayout.Toggle(
                BonePoseEditorState.LoopPlayback, M("ループ"),
                EditorStyles.toolbarButton, GUILayout.Width(36));

            GUILayout.FlexibleSpace();

            // Default Curve
            GUILayout.Label(M("カーブ:"), EditorStyles.miniLabel, GUILayout.Width(34));
            var presets = (CurvePreset[])System.Enum.GetValues(typeof(CurvePreset));
            foreach (var p in presets)
            {
                if (GUILayout.Button(p.ToString().Substring(0, 1),
                        EditorStyles.toolbarButton, GUILayout.Width(18)))
                {
                    BonePoseEditorState.DefaultCurve =
                        BonePoseEditorState.CreatePreset(p);
                    GUI.changed = true;
                }
            }
            BonePoseEditorState.DefaultCurve = EditorGUILayout.CurveField(
                BonePoseEditorState.DefaultCurve, Color.green,
                new Rect(0, 0, 1, 1), GUILayout.Width(48), GUILayout.Height(16));

            GUILayout.Space(4);
            GUILayout.Label(M("長さ:"), EditorStyles.miniLabel, GUILayout.Width(24));
            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsPlaying);
            BonePoseEditorState.AnimationLength = Mathf.Max(0.1f,
                EditorGUILayout.FloatField(
                    BonePoseEditorState.AnimationLength, GUILayout.Width(36)));
            EditorGUI.EndDisabledGroup();
            GUILayout.Label(M("FPS:"), EditorStyles.miniLabel, GUILayout.Width(24));
            BonePoseEditorState.FrameRate = Mathf.Max(1,
                EditorGUILayout.IntField(
                    BonePoseEditorState.FrameRate, GUILayout.Width(30)));

            EditorGUILayout.EndHorizontal();

            // Row 2: Time scrubber
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsPlaying);
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.Slider(
                BonePoseEditorState.CurrentTime,
                0f, BonePoseEditorState.AnimationLength);
            if (EditorGUI.EndChangeCheck())
            {
                BonePoseEditorState.CurrentTime = newTime;
                if (BonePoseEditorState.Keyframes.Count > 0)
                {
                    BonePoseEditorState.ApplyPoseAtTime(newTime);
                    SceneView.RepaintAll();
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.Label($"{BonePoseEditorState.CurrentTime:F2}s",
                EditorStyles.miniLabel, GUILayout.Width(38));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAutoKeyButton()
        {
            if (_autoKeyOnStyle == null)
                _autoKeyOnStyle = new GUIStyle(EditorStyles.toolbarButton)
                    { fontStyle = FontStyle.Bold };

            var bg = GUI.backgroundColor;
            if (BonePoseEditorState.AutoKeyEnabled)
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);

            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsPlaying);
            if (GUILayout.Button(
                    BonePoseEditorState.AutoKeyEnabled ? "AUTO" : "Auto",
                    _autoKeyOnStyle, GUILayout.Width(42)))
                BonePoseEditorState.AutoKeyEnabled = !BonePoseEditorState.AutoKeyEnabled;
            EditorGUI.EndDisabledGroup();

            GUI.backgroundColor = bg;
        }

        // ── Section 3: Dope Sheet ──

        private void DrawDopeSheetSection()
        {
            int groupCount = BonePoseEditorState.BoneGroups.Length;

            // Dynamic height
            float dopeH = SummaryH;
            for (int g = 0; g < groupCount; g++)
            {
                dopeH += GroupH;
                if (_dopeGroupFoldouts != null
                    && g < _dopeGroupFoldouts.Length
                    && _dopeGroupFoldouts[g])
                {
                    foreach (var bone in BonePoseEditorState.BoneGroups[g].bones)
                        if (BonePoseEditorState.BoneTransforms.ContainsKey(bone))
                            dopeH += BoneRowH;
                }
            }

            float maxH = 320f;
            bool needsScroll = dopeH > maxH;
            if (needsScroll)
                _dopeScrollPos = EditorGUILayout.BeginScrollView(
                    _dopeScrollPos, GUILayout.Height(maxH));

            Rect dopeRect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.Height(dopeH), GUILayout.ExpandWidth(true));
            DrawDopeSheet(dopeRect);

            if (needsScroll)
                EditorGUILayout.EndScrollView();
        }

        private void DrawDopeSheet(Rect totalRect)
        {
            if (totalRect.width <= 0 || totalRect.height <= 0) return;

            float length = BonePoseEditorState.AnimationLength;
            var keyframes = BonePoseEditorState.Keyframes;
            int groupCount = BonePoseEditorState.BoneGroups.Length;

            Rect trackArea = new Rect(
                totalRect.x + LabelW, totalRect.y,
                totalRect.width - LabelW, totalRect.height);

            // ── Background ──
            Color bgMain = BonePoseEditorState.AutoKeyEnabled
                ? new Color(0.25f, 0.14f, 0.14f)
                : new Color(0.18f, 0.18f, 0.18f);
            EditorGUI.DrawRect(totalRect, bgMain);
            EditorGUI.DrawRect(
                new Rect(totalRect.x, totalRect.y, LabelW, totalRect.height),
                new Color(0.22f, 0.22f, 0.22f));

            // ── Summary row ──
            Rect summaryTrackR = new Rect(
                trackArea.x, totalRect.y, trackArea.width, SummaryH);
            GUI.Label(new Rect(totalRect.x + 4, totalRect.y, LabelW - 4, SummaryH),
                M("全体"), EditorStyles.boldLabel);
            EditorGUI.DrawRect(new Rect(
                totalRect.x, totalRect.y + SummaryH - 1, totalRect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));

            // ── Group & Bone rows ──
            float currentY = totalRect.y + SummaryH;
            var shortNames = BonePoseEditorState.BoneGroupShortNames;
            var arrowStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 8,
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
            };

            for (int g = 0; g < groupCount; g++)
            {
                Rect gTrackR = new Rect(trackArea.x, currentY, trackArea.width, GroupH);

                // Alternating tint
                if (g % 2 == 0)
                    EditorGUI.DrawRect(gTrackR, new Color(0f, 0f, 0f, 0.12f));
                // Separator
                EditorGUI.DrawRect(new Rect(
                    totalRect.x, currentY + GroupH - 1, totalRect.width, 1),
                    new Color(0.25f, 0.25f, 0.25f));

                // Foldout arrow
                bool expanded = _dopeGroupFoldouts != null && _dopeGroupFoldouts[g];
                GUI.Label(new Rect(totalRect.x + 1, currentY, 12, GroupH),
                    expanded ? "\u25BC" : "\u25B6", arrowStyle);

                // Color dot
                Color dotC = g < GroupColors.Length ? GroupColors[g] : Color.gray;
                EditorGUI.DrawRect(new Rect(
                    totalRect.x + 12, currentY + (GroupH - 8) / 2f, 8, 8), dotC);

                // Label
                string label = g < shortNames.Length
                    ? shortNames[g]
                    : BonePoseEditorState.BoneGroups[g].name;
                GUI.Label(new Rect(totalRect.x + 22, currentY, LabelW - 22, GroupH),
                    label, EditorStyles.miniLabel);

                // Group diamonds
                DrawGroupDiamonds(gTrackR, g, length, keyframes);
                currentY += GroupH;

                // Bone sub-rows
                if (expanded)
                {
                    var bones = BonePoseEditorState.BoneGroups[g].bones;
                    foreach (var bone in bones)
                    {
                        if (!BonePoseEditorState.BoneTransforms.ContainsKey(bone))
                            continue;

                        Rect bTrackR = new Rect(
                            trackArea.x, currentY, trackArea.width, BoneRowH);

                        // Tint
                        EditorGUI.DrawRect(bTrackR, new Color(0f, 0f, 0f, 0.06f));
                        EditorGUI.DrawRect(new Rect(
                            totalRect.x, currentY + BoneRowH - 1, totalRect.width, 1),
                            new Color(0.2f, 0.2f, 0.2f));

                        // Highlight selected bone
                        if (bone == BonePoseEditorState.SelectedBone)
                            EditorGUI.DrawRect(new Rect(
                                totalRect.x, currentY, totalRect.width, BoneRowH),
                                new Color(1f, 0.92f, 0f, 0.08f));

                        // Label (indented) + curve indicator
                        string boneName = GetBoneShortName(bone, g);
                        bool hasCurveOverride =
                            BonePoseEditorState.BoneCurves.ContainsKey(bone);
                        float labelEnd = LabelW - 24;
                        if (hasCurveOverride)
                        {
                            var ciStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = new Color(1f, 0.7f, 0.3f) },
                                fontSize = 8,
                            };
                            string curveChar = GetCurveTypeChar(
                                BonePoseEditorState.BoneCurves[bone]);
                            GUI.Label(new Rect(
                                totalRect.x + LabelW - 14, currentY, 12, BoneRowH),
                                curveChar, ciStyle);
                            labelEnd -= 14;
                        }
                        GUI.Label(new Rect(
                            totalRect.x + 24, currentY, labelEnd, BoneRowH),
                            boneName, EditorStyles.miniLabel);

                        // Bone diamonds
                        DrawBoneDiamonds(bTrackR, bone, g, length, keyframes);
                        currentY += BoneRowH;
                    }
                }
            }

            // ── Time ticks ──
            DrawTimeTicks(summaryTrackR, length);

            // ── Hovered marker (summary row) ──
            _hoveredMarkerIndex = -1;
            Event evt = Event.current;
            if (summaryTrackR.Contains(evt.mousePosition))
            {
                for (int i = 0; i < keyframes.Count; i++)
                {
                    float mx = summaryTrackR.x
                        + (keyframes[i].Time / length) * summaryTrackR.width;
                    if (Mathf.Abs(evt.mousePosition.x - mx) < 8)
                    {
                        _hoveredMarkerIndex = i;
                        break;
                    }
                }
            }

            // ── Summary stacked bars ──
            float barW = 10f;
            float cellH = 3f;
            float stackH = groupCount * cellH;
            float stackY = summaryTrackR.y + (SummaryH - stackH) / 2f;

            for (int i = 0; i < keyframes.Count; i++)
            {
                float x = summaryTrackR.x
                    + (keyframes[i].Time / length) * summaryTrackR.width;
                bool isCurrent = Mathf.Abs(
                    keyframes[i].Time - BonePoseEditorState.CurrentTime) < 0.001f;
                bool isHovered = i == _hoveredMarkerIndex;
                bool isDragged = _draggingMarker == keyframes[i];

                if (isCurrent || isHovered || isDragged)
                {
                    Color outC;
                    if (isDragged) outC = new Color(1f, 0.6f, 0.2f);
                    else if (isCurrent) outC = new Color(1f, 0.92f, 0.016f);
                    else outC = new Color(0.5f, 1f, 0.5f);
                    EditorGUI.DrawRect(new Rect(
                        x - barW / 2 - 1, stackY - 1,
                        barW + 2, stackH + 2), outC);
                }

                EditorGUI.DrawRect(new Rect(
                    x - barW / 2, stackY, barW, stackH),
                    new Color(0.12f, 0.12f, 0.12f));

                PoseKeyframe prev = i > 0 ? keyframes[i - 1] : null;
                for (int g = 0; g < groupCount; g++)
                {
                    if (!BonePoseEditorState.IsGroupAnimatedInKeyframe(g, keyframes[i]))
                        continue;
                    bool changed = BonePoseEditorState.IsGroupChangedFromPrev(
                        g, keyframes[i], prev);
                    Color cc = g < GroupColors.Length ? GroupColors[g] : Color.gray;
                    if (!changed)
                        cc = new Color(cc.r, cc.g, cc.b, 0.35f);
                    EditorGUI.DrawRect(new Rect(
                        x - barW / 2, stackY + g * cellH,
                        barW, cellH - 0.5f), cc);
                }
            }

            // ── Playhead ──
            float phX = trackArea.x
                + (BonePoseEditorState.CurrentTime / length) * trackArea.width;
            EditorGUI.DrawRect(new Rect(
                phX - 1, trackArea.y, 2, trackArea.height),
                new Color(1f, 0.25f, 0.25f, 0.7f));
            EditorGUI.DrawRect(new Rect(
                phX - 5, trackArea.y, 10, 3),
                new Color(1f, 0.25f, 0.25f));

            // ── Borders ──
            EditorGUI.DrawRect(new Rect(
                totalRect.x, totalRect.y, totalRect.width, 1), Color.gray);
            EditorGUI.DrawRect(new Rect(
                totalRect.x, totalRect.yMax - 1, totalRect.width, 1), Color.gray);
            EditorGUI.DrawRect(new Rect(
                trackArea.x, totalRect.y, 1, totalRect.height),
                new Color(0.35f, 0.35f, 0.35f));

            // ── Mouse ──
            if (!BonePoseEditorState.IsPlaying)
                HandleDopeSheetMouse(totalRect, trackArea, summaryTrackR,
                    length, keyframes);
        }

        // ── Dope Sheet Helpers ──

        private void DrawGroupDiamonds(
            Rect rowRect, int groupIndex, float length,
            List<PoseKeyframe> keyframes)
        {
            Color baseC = groupIndex < GroupColors.Length
                ? GroupColors[groupIndex] : Color.gray;

            for (int i = 0; i < keyframes.Count; i++)
            {
                if (!BonePoseEditorState.IsGroupAnimatedInKeyframe(
                        groupIndex, keyframes[i]))
                    continue;

                float x = rowRect.x + (keyframes[i].Time / length) * rowRect.width;
                float cy = rowRect.y + rowRect.height / 2f;
                bool isCurrent = Mathf.Abs(
                    keyframes[i].Time - BonePoseEditorState.CurrentTime) < 0.001f;
                PoseKeyframe prev = i > 0 ? keyframes[i - 1] : null;
                bool changed = BonePoseEditorState.IsGroupChangedFromPrev(
                    groupIndex, keyframes[i], prev);

                Color c = isCurrent
                    ? new Color(1f, 0.92f, 0.016f)
                    : changed ? baseC
                    : new Color(baseC.r, baseC.g, baseC.b, 0.35f);
                float sz = isCurrent ? 8f : 6f;
                EditorGUI.DrawRect(
                    new Rect(x - sz / 2, cy - sz / 2, sz, sz), c);
            }
        }

        private void DrawBoneDiamonds(
            Rect rowRect, HumanBodyBones bone, int groupIndex,
            float length, List<PoseKeyframe> keyframes)
        {
            Color baseC = groupIndex < GroupColors.Length
                ? GroupColors[groupIndex] : Color.gray;

            for (int i = 0; i < keyframes.Count; i++)
            {
                if (!BonePoseEditorState.IsBoneAnimatedInKeyframe(
                        bone, keyframes[i]))
                    continue;

                float x = rowRect.x + (keyframes[i].Time / length) * rowRect.width;
                float cy = rowRect.y + rowRect.height / 2f;
                bool isCurrent = Mathf.Abs(
                    keyframes[i].Time - BonePoseEditorState.CurrentTime) < 0.001f;
                PoseKeyframe prev = i > 0 ? keyframes[i - 1] : null;
                bool changed = BonePoseEditorState.IsBoneChangedFromPrev(
                    bone, keyframes[i], prev);

                Color c = isCurrent
                    ? new Color(1f, 0.92f, 0.016f)
                    : changed ? baseC
                    : new Color(baseC.r, baseC.g, baseC.b, 0.35f);
                float sz = 4f;
                EditorGUI.DrawRect(
                    new Rect(x - sz / 2, cy - sz / 2, sz, sz), c);
            }
        }

        private void DrawTimeTicks(Rect summaryTrackR, float length)
        {
            float tickInterval = CalculateTickInterval(length, summaryTrackR.width);
            var tickStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                fontSize = 9,
            };
            for (float t = 0; t <= length + 0.001f; t += tickInterval)
            {
                float x = summaryTrackR.x + (t / length) * summaryTrackR.width;
                EditorGUI.DrawRect(new Rect(x, summaryTrackR.y, 1, 4),
                    new Color(0.45f, 0.45f, 0.45f));
                GUI.Label(new Rect(x - 16, summaryTrackR.yMax - 12, 32, 12),
                    t.ToString("F1"), tickStyle);
            }
        }

        private static string GetBoneShortName(HumanBodyBones bone, int groupIndex)
        {
            string name = bone.ToString();
            if (groupIndex >= 1) // strip Left/Right for limb groups
                name = name.Replace("Left", "").Replace("Right", "");
            name = name.Replace("Proximal", ".P")
                       .Replace("Intermediate", ".I")
                       .Replace("Distal", ".D")
                       .Replace("Upper", "Up")
                       .Replace("Lower", "Lo");
            return name;
        }

        // ── Dope Sheet Mouse ──

        private (int group, int bone) ResolveDopeRow(float mouseY, float startY)
        {
            float y = mouseY - startY - SummaryH;
            if (y < 0) return (-1, -1);

            for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
            {
                if (y < GroupH) return (g, -1);
                y -= GroupH;

                if (_dopeGroupFoldouts != null
                    && g < _dopeGroupFoldouts.Length
                    && _dopeGroupFoldouts[g])
                {
                    int bIdx = 0;
                    foreach (var bone in BonePoseEditorState.BoneGroups[g].bones)
                    {
                        if (!BonePoseEditorState.BoneTransforms.ContainsKey(bone))
                            continue;
                        if (y < BoneRowH) return (g, bIdx);
                        y -= BoneRowH;
                        bIdx++;
                    }
                }
            }
            return (-2, -2);
        }

        private void HandleDopeSheetMouse(
            Rect totalRect, Rect trackArea, Rect summaryTrackR,
            float length, List<PoseKeyframe> keyframes)
        {
            Event e = Event.current;
            if (e == null) return;

            // Right-click on summary marker
            if (e.type == EventType.MouseDown && e.button == 1
                && _hoveredMarkerIndex >= 0)
            {
                ShowMarkerContextMenu(keyframes[_hoveredMarkerIndex]);
                e.Use();
                return;
            }

            // Left-click
            if (e.type == EventType.MouseDown && e.button == 0
                && totalRect.Contains(e.mousePosition))
            {
                // Label column click → foldout toggle or bone select
                if (e.mousePosition.x < totalRect.x + LabelW
                    && e.mousePosition.y > totalRect.y + SummaryH)
                {
                    var (g, b) = ResolveDopeRow(
                        e.mousePosition.y, totalRect.y);
                    if (g >= 0 && b == -1 && g < _dopeGroupFoldouts.Length)
                    {
                        _dopeGroupFoldouts[g] = !_dopeGroupFoldouts[g];
                        e.Use();
                        Repaint();
                        return;
                    }
                    if (g >= 0 && b >= 0)
                    {
                        // Select bone
                        var bones = BonePoseEditorState.BoneGroups[g].bones;
                        int idx = 0;
                        foreach (var bone in bones)
                        {
                            if (!BonePoseEditorState.BoneTransforms.ContainsKey(bone))
                                continue;
                            if (idx == b)
                            {
                                BonePoseEditorState.SelectedBone = bone;
                                SceneView.RepaintAll();
                                break;
                            }
                            idx++;
                        }
                        e.Use();
                        Repaint();
                        return;
                    }
                }

                // Track area
                if (trackArea.Contains(e.mousePosition))
                {
                    if (e.mousePosition.y < totalRect.y + SummaryH)
                    {
                        // Summary row
                        if (_hoveredMarkerIndex >= 0)
                        {
                            _draggingMarker = keyframes[_hoveredMarkerIndex];
                            NavigateAndApply(_draggingMarker.Time);
                        }
                        else
                        {
                            _isScrubbing = true;
                            ApplyScrub(e.mousePosition.x, trackArea, length);
                        }
                    }
                    else
                    {
                        // Group/bone row — click diamond or scrub
                        int nearKf = FindNearestKeyframe(
                            e.mousePosition.x, trackArea, length, keyframes);
                        if (nearKf >= 0)
                            NavigateAndApply(keyframes[nearKf].Time);
                        else
                        {
                            _isScrubbing = true;
                            ApplyScrub(e.mousePosition.x, trackArea, length);
                        }
                    }
                    e.Use();
                }
            }

            // Drag
            if (e.type == EventType.MouseDrag)
            {
                if (_draggingMarker != null)
                {
                    float t = Mathf.Clamp01(
                        (e.mousePosition.x - trackArea.x) / trackArea.width);
                    float newTime = t * length;
                    _draggingMarker.Time = newTime;
                    foreach (var kvp in BonePoseEditorState.BoneTransforms)
                        _draggingMarker.BoneWorldPositions[kvp.Key] =
                            kvp.Value.position;
                    BonePoseEditorState.CurrentTime = newTime;
                    keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
                    e.Use();
                    Repaint();
                }
                else if (_isScrubbing)
                {
                    ApplyScrub(e.mousePosition.x, trackArea, length);
                    e.Use();
                }
            }

            // MouseUp
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                _draggingMarker = null;
                _isScrubbing = false;
                e.Use();
            }
        }

        private int FindNearestKeyframe(
            float mouseX, Rect trackArea, float length,
            List<PoseKeyframe> keyframes)
        {
            for (int i = 0; i < keyframes.Count; i++)
            {
                float x = trackArea.x
                    + (keyframes[i].Time / length) * trackArea.width;
                if (Mathf.Abs(mouseX - x) < 8)
                    return i;
            }
            return -1;
        }

        private void ShowMarkerContextMenu(PoseKeyframe kf)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(M("ジャンプ")), false, () =>
            {
                NavigateAndApply(kf.Time);
                Repaint();
            });
            menu.AddSeparator("");
            menu.AddItem(
                new GUIContent(M("現在の時間に複製")), false, () =>
            {
                var copy = new PoseKeyframe
                    { Time = BonePoseEditorState.CurrentTime };
                foreach (var kvp in kf.BoneRotations)
                    copy.BoneRotations[kvp.Key] = kvp.Value;
                foreach (var kvp in kf.BoneWorldPositions)
                    copy.BoneWorldPositions[kvp.Key] = kvp.Value;
                foreach (var kvp in kf.BoneSpaces)
                    copy.BoneSpaces[kvp.Key] = new SpaceOverride
                    {
                        Space = kvp.Value.Space,
                        ReferenceBone = kvp.Value.ReferenceBone,
                    };
                var ks = BonePoseEditorState.Keyframes;
                ks.RemoveAll(k => Mathf.Abs(k.Time - copy.Time) < 0.001f);
                ks.Add(copy);
                ks.Sort((a, b) => a.Time.CompareTo(b.Time));
                BonePoseEditorState.ApplyPoseAtTime(copy.Time);
                SceneView.RepaintAll();
                Repaint();
            });
            menu.AddItem(new GUIContent(M("削除")), false, () =>
            {
                BonePoseEditorState.Keyframes.Remove(kf);
                Repaint();
            });
            menu.ShowAsContext();
        }

        private void NavigateAndApply(float time)
        {
            BonePoseEditorState.NavigateToTime(time);
            SceneView.RepaintAll();
            Repaint();
        }

        private void ApplyScrub(float mouseX, Rect trackArea, float length)
        {
            float t = Mathf.Clamp01(
                (mouseX - trackArea.x) / trackArea.width);
            BonePoseEditorState.CurrentTime = t * length;
            if (BonePoseEditorState.Keyframes.Count > 0)
            {
                BonePoseEditorState.ApplyPoseAtTime(
                    BonePoseEditorState.CurrentTime);
                SceneView.RepaintAll();
            }
            Repaint();
        }

        private static string GetCurveTypeChar(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length < 2) return "~";

            var k0 = curve.keys[0];
            var k1 = curve.keys[curve.keys.Length - 1];

            // Constant: end value near 0
            if (Mathf.Abs(k1.value) < 0.01f && Mathf.Abs(k0.value) < 0.01f)
                return "C";

            // Linear: tangents near 1
            if (Mathf.Abs(k0.outTangent - 1f) < 0.1f && Mathf.Abs(k1.inTangent - 1f) < 0.1f)
                return "L";

            // EaseIn: start tangent near 0, end tangent > 1
            if (Mathf.Abs(k0.outTangent) < 0.1f && k1.inTangent > 1.5f)
                return "I";

            // EaseOut: start tangent > 1, end tangent near 0
            if (k0.outTangent > 1.5f && Mathf.Abs(k1.inTangent) < 0.1f)
                return "O";

            // Smooth: both tangents moderate
            if (Mathf.Abs(k0.outTangent) < 0.1f && Mathf.Abs(k1.inTangent) < 0.1f)
                return "S";

            return "~";
        }

        private static float CalculateTickInterval(float length, float width)
        {
            float minPx = 40f;
            float raw = length * minPx / width;
            float[] snaps = { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f };
            foreach (float s in snaps)
                if (s >= raw) return s;
            return snaps[snaps.Length - 1];
        }

        // ── Section 4: Keyframe Actions ──

        private void DrawKeyframeActions()
        {
            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsPlaying);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            string addLabel = BonePoseEditorState.HasKeyframeAtCurrentTime()
                ? M("キー更新") : M("+ キー");
            if (GUILayout.Button(addLabel, EditorStyles.toolbarButton))
            {
                BonePoseEditorState.CaptureKeyframe();
                Repaint();
            }

            EditorGUI.BeginDisabledGroup(
                !BonePoseEditorState.HasKeyframeAtCurrentTime());
            if (GUILayout.Button(M("- キー"), EditorStyles.toolbarButton))
            {
                BonePoseEditorState.RemoveKeyframeAtCurrentTime();
                Repaint();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);

            if (GUILayout.Button(M("コピー"), EditorStyles.toolbarButton,
                    GUILayout.Width(40)))
                BonePoseEditorState.CopyPoseToClipboard();

            EditorGUI.BeginDisabledGroup(
                BonePoseEditorState.ClipboardKeyframe == null);
            if (GUILayout.Button(M("ペースト"), EditorStyles.toolbarButton,
                    GUILayout.Width(40)))
            {
                BonePoseEditorState.PastePoseFromClipboard();
                SceneView.RepaintAll();
                Repaint();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            GUILayout.Label(string.Format(M("{0} キー"), BonePoseEditorState.Keyframes.Count),
                EditorStyles.miniLabel);

            if (BonePoseEditorState.Keyframes.Count > 2)
            {
                if (GUILayout.Button(M("削減"), EditorStyles.toolbarButton,
                        GUILayout.Width(48)))
                {
                    int removed = BonePoseEditorKeyReduction.ReduceKeyframes(
                        BonePoseEditorState.Keyframes,
                        BonePoseEditorState.OriginalRotations,
                        BonePoseEditorState.KeyReductionTolerance);
                    if (removed > 0)
                        Debug.Log($"[BonePoseEditor] Reduced {removed} keyframes");
                    Repaint();
                }
            }

            if (BonePoseEditorState.Keyframes.Count > 0)
            {
                if (GUILayout.Button(M("全削除"), EditorStyles.toolbarButton,
                        GUILayout.Width(56)))
                {
                    if (EditorUtility.DisplayDialog(M("キーフレーム削除"),
                        M("全てのキーフレームを削除しますか？"), M("OK"), M("キャンセル")))
                    {
                        BonePoseEditorState.Keyframes.Clear();
                        Repaint();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        // ── Section 5: Pose Inspector ──

        private void DrawPoseInspector()
        {
            var selected = BonePoseEditorState.SelectedBone;
            if ((int)selected < 0) return;
            if (!BonePoseEditorState.BoneTransforms.TryGetValue(
                    selected, out var selTransform)) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(string.Format(M("ボーン: {0}"), selected), EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(BonePoseEditorState.IsPlaying);
            EditorGUI.BeginChangeCheck();
            var euler = selTransform.localRotation.eulerAngles;
            var newEuler = EditorGUILayout.Vector3Field(M("回転"), euler);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selTransform, "Edit Bone Rotation");
                selTransform.localRotation = Quaternion.Euler(newEuler);

                if (BonePoseEditorState.SymmetryEnabled)
                {
                    var mirror = BonePoseEditorState.GetMirrorBone(selected);
                    if ((int)mirror >= 0
                        && BonePoseEditorState.BoneTransforms.TryGetValue(
                            mirror, out var mt))
                    {
                        Undo.RecordObject(mt, "Mirror Rotation");
                        mt.localRotation =
                            BonePoseEditorState.ComputeMirrorRotation(
                                selTransform.localRotation);
                    }
                }
                BonePoseEditorState.TryAutoKey();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();

            // Space switching — always visible when bone is selected
            {
                var kf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                SpaceOverride currentSpace = null;
                kf?.BoneSpaces.TryGetValue(selected, out currentSpace);
                var space = currentSpace?.Space ?? BoneSpace.Local;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(M("空間:"), GUILayout.Width(42));

                EditorGUI.BeginChangeCheck();
                var newSpace = (BoneSpace)EditorGUILayout.EnumPopup(space);
                if (EditorGUI.EndChangeCheck())
                {
                    // Auto-generate keyframe if none exists
                    if (kf == null)
                    {
                        BonePoseEditorState.CaptureKeyframe();
                        kf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                        kf.BoneSpaces.TryGetValue(selected, out currentSpace);
                    }
                    var newOverride = new SpaceOverride { Space = newSpace };
                    if (newSpace == BoneSpace.ParentBone && currentSpace != null)
                        newOverride.ReferenceBone = currentSpace.ReferenceBone;
                    BonePoseEditorSpaceSwitching.SwitchSpace(
                        selected, newOverride, kf, BonePoseEditorState.BoneTransforms);
                    BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                    SceneView.RepaintAll();
                }

                if (space == BoneSpace.ParentBone)
                {
                    EditorGUI.BeginChangeCheck();
                    var refBone = (HumanBodyBones)EditorGUILayout.EnumPopup(
                        currentSpace?.ReferenceBone ?? HumanBodyBones.Hips);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (kf == null)
                        {
                            BonePoseEditorState.CaptureKeyframe();
                            kf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                        }
                        var newOverride = new SpaceOverride
                        {
                            Space = BoneSpace.ParentBone,
                            ReferenceBone = refBone,
                        };
                        BonePoseEditorSpaceSwitching.SwitchSpace(
                            selected, newOverride, kf, BonePoseEditorState.BoneTransforms);
                        BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            // Per-bone curve — preset buttons
            bool hasOverride = BonePoseEditorState.BoneCurves.ContainsKey(selected);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("カーブ:"), GUILayout.Width(42));
            string[] presetLabels = { M("リニア"), M("スムーズ"), M("イーズイン"), M("イーズアウト"), M("定数") };
            var presetValues = (CurvePreset[])System.Enum.GetValues(typeof(CurvePreset));
            for (int i = 0; i < presetValues.Length; i++)
            {
                if (GUILayout.Button(presetLabels[i], EditorStyles.miniButton))
                {
                    BonePoseEditorState.SetBoneCurve(selected,
                        BonePoseEditorState.CreatePreset(presetValues[i]));
                    GUI.changed = true;
                }
            }
            if (hasOverride)
            {
                if (GUILayout.Button(M("リセット"), EditorStyles.miniButton,
                        GUILayout.Width(40)))
                {
                    BonePoseEditorState.ClearBoneCurve(selected);
                    GUI.changed = true;
                }
            }
            else
            {
                GUILayout.Label(M("(デフォルト)"), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            // Per-bone curve — graph editor
            var currentCurve = BonePoseEditorState.GetBoneCurve(selected);
            EditorGUI.BeginChangeCheck();
            var editedCurve = EditorGUILayout.CurveField(
                M("グラフ"), currentCurve, Color.cyan, new Rect(0, 0, 1, 1),
                GUILayout.Height(40));
            if (EditorGUI.EndChangeCheck())
                BonePoseEditorState.SetBoneCurve(selected, editedCurve);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(M("ボーンリセット"), EditorStyles.miniButton))
            {
                if (BonePoseEditorState.OriginalRotations.TryGetValue(
                        selected, out var orig))
                {
                    Undo.RecordObject(selTransform, "Reset Bone");
                    selTransform.localRotation = orig;
                    BonePoseEditorState.TryAutoKey();
                    SceneView.RepaintAll();
                }
            }
            if (GUILayout.Button(M("全てTポーズにリセット"), EditorStyles.miniButton))
            {
                ResetToTPose();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── Section: IK Panel ──

        private void DrawIKPanel()
        {
            if (!BonePoseEditorState.IKEnabled) return;
            if (BonePoseEditorState.IKTargets == null) return;

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(M("IKターゲット"), EditorStyles.boldLabel);

            foreach (var target in BonePoseEditorState.IKTargets)
            {
                EditorGUILayout.BeginHorizontal();
                target.Enabled = EditorGUILayout.Toggle(
                    target.Enabled, GUILayout.Width(16));
                EditorGUILayout.LabelField(target.Limb.ToString(),
                    GUILayout.Width(80));

                EditorGUI.BeginDisabledGroup(!target.Enabled);
                target.Pinned = GUILayout.Toggle(target.Pinned, M("固定"),
                    EditorStyles.miniButton, GUILayout.Width(32));

                // FK/IK Blend slider
                EditorGUI.BeginChangeCheck();
                target.FKIKBlend = EditorGUILayout.Slider(
                    target.FKIKBlend, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                    SceneView.RepaintAll();
                }

                // Quick FK/IK buttons
                if (GUILayout.Button("FK", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    target.FKIKBlend = 0f;
                    BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("IK", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    target.FKIKBlend = 1f;
                    BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                    SceneView.RepaintAll();
                }

                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();

                // Space switching for IK target EndBone
                if (target.Enabled)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.Label(M("空間:"), EditorStyles.miniLabel, GUILayout.Width(38));

                    var ikKf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                    SpaceOverride ikCurrentSpace = null;
                    ikKf?.BoneSpaces.TryGetValue(target.EndBone, out ikCurrentSpace);
                    var ikSpace = ikCurrentSpace?.Space ?? BoneSpace.Local;

                    EditorGUI.BeginChangeCheck();
                    var ikNewSpace = (BoneSpace)EditorGUILayout.EnumPopup(
                        ikSpace, GUILayout.Width(80));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (ikKf == null)
                        {
                            BonePoseEditorState.CaptureKeyframe();
                            ikKf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                            ikKf.BoneSpaces.TryGetValue(target.EndBone, out ikCurrentSpace);
                        }
                        var newOverride = new SpaceOverride { Space = ikNewSpace };
                        if (ikNewSpace == BoneSpace.ParentBone && ikCurrentSpace != null)
                            newOverride.ReferenceBone = ikCurrentSpace.ReferenceBone;
                        BonePoseEditorSpaceSwitching.SwitchSpace(
                            target.EndBone, newOverride, ikKf,
                            BonePoseEditorState.BoneTransforms);
                        BonePoseEditorState.ApplyPoseAtTime(
                            BonePoseEditorState.CurrentTime);
                        SceneView.RepaintAll();
                    }

                    if (ikSpace == BoneSpace.ParentBone)
                    {
                        EditorGUI.BeginChangeCheck();
                        var ikRefBone = (HumanBodyBones)EditorGUILayout.EnumPopup(
                            ikCurrentSpace?.ReferenceBone ?? HumanBodyBones.Hips,
                            GUILayout.Width(100));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (ikKf == null)
                            {
                                BonePoseEditorState.CaptureKeyframe();
                                ikKf = BonePoseEditorState.GetKeyframeAtCurrentTime();
                            }
                            var newOverride = new SpaceOverride
                            {
                                Space = BoneSpace.ParentBone,
                                ReferenceBone = ikRefBone,
                            };
                            BonePoseEditorSpaceSwitching.SwitchSpace(
                                target.EndBone, newOverride, ikKf,
                                BonePoseEditorState.BoneTransforms);
                            BonePoseEditorState.ApplyPoseAtTime(
                                BonePoseEditorState.CurrentTime);
                            SceneView.RepaintAll();
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ── Section 6: Save ──

        private void DrawSaveSection()
        {
            EditorGUILayout.Space(4);

            // Root Motion settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(M("ルートモーション"), EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            BonePoseEditorState.BakeRootPosition = EditorGUILayout.ToggleLeft(
                M("ルート位置をベイク"), BonePoseEditorState.BakeRootPosition,
                GUILayout.Width(140));
            BonePoseEditorState.BakeRootRotation = EditorGUILayout.ToggleLeft(
                M("ルート回転をベイク"), BonePoseEditorState.BakeRootRotation,
                GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();
            BonePoseEditorState.RootHeightOffset = EditorGUILayout.FloatField(
                M("高さオフセット"), BonePoseEditorState.RootHeightOffset);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button(M("AnimationClipとして保存"), GUILayout.Height(24)))
                SaveAsAnimationClip();
            EditorGUILayout.Space(4);
        }

        // ══════════════════════════════════════════
        //  Playback
        // ══════════════════════════════════════════

        private void StartPlayback()
        {
            if (BonePoseEditorState.Keyframes.Count < 2) return;
            BonePoseEditorState.IsPlaying = true;
            _playStartRealTime = EditorApplication.timeSinceStartup;
            _playStartAnimTime = BonePoseEditorState.CurrentTime;
            EditorApplication.update += UpdatePlayback;
        }

        private void StopPlayback()
        {
            if (!BonePoseEditorState.IsPlaying) return;
            BonePoseEditorState.IsPlaying = false;
            EditorApplication.update -= UpdatePlayback;
        }

        private void UpdatePlayback()
        {
            if (!BonePoseEditorState.IsActive) { StopPlayback(); return; }

            double elapsed = EditorApplication.timeSinceStartup - _playStartRealTime;
            float newTime = _playStartAnimTime + (float)elapsed;
            float length = BonePoseEditorState.AnimationLength;

            if (newTime > length)
            {
                if (BonePoseEditorState.LoopPlayback)
                {
                    _playStartRealTime = EditorApplication.timeSinceStartup;
                    _playStartAnimTime = 0f;
                    newTime = 0f;
                }
                else
                {
                    newTime = length;
                    StopPlayback();
                }
            }

            BonePoseEditorState.CurrentTime = newTime;
            BonePoseEditorState.ApplyPoseAtTime(newTime);
            SceneView.RepaintAll();
            Repaint();
        }

        // ══════════════════════════════════════════
        //  Save (AnimationClip)
        // ══════════════════════════════════════════

        private void ResetToTPose()
        {
            foreach (var kvp in BonePoseEditorState.OriginalRotations)
            {
                if (BonePoseEditorState.BoneTransforms.TryGetValue(kvp.Key, out var t))
                {
                    Undo.RecordObject(t, "Reset to T-Pose");
                    t.localRotation = kvp.Value;
                }
            }
            SceneView.RepaintAll();
        }

        private void SaveAsAnimationClip()
        {
            var animator = BonePoseEditorState.TargetAnimator;
            if (animator == null) return;

            // Check if any layer has keyframes
            bool hasKf = false;
            foreach (var layer in BonePoseEditorState.Layers)
            {
                if (layer.Keyframes.Count > 0) { hasKf = true; break; }
            }

            string defaultName = animator.gameObject.name
                + (hasKf ? "_Motion" : "_Pose");
            string path = EditorUtility.SaveFilePanelInProject(
                M("AnimationClipとして保存"), defaultName, "anim",
                M("保存先を選択"));
            if (string.IsNullOrEmpty(path)) return;

            string dir = Path.GetDirectoryName(path).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(dir))
                EnsureAssetFolder(dir);

            var clip = hasKf
                ? CreateMultiKeyframeClip(animator)
                : CreateSinglePoseClip(animator);
            clip.name = Path.GetFileNameWithoutExtension(path);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = BonePoseEditorState.LoopPlayback;
            if (hasKf) settings.stopTime = BonePoseEditorState.AnimationLength;

            // Root motion clip settings (Bake Into Pose)
            if (BonePoseEditorState.BakeRootPosition)
            {
                settings.loopBlendPositionXZ = true;
                settings.loopBlendPositionY = true;
            }
            if (BonePoseEditorState.BakeRootRotation)
            {
                settings.loopBlendOrientation = true;
            }
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(clip);

            int kfCount = hasKf ? BonePoseEditorState.Keyframes.Count : 1;
            Debug.Log($"[BonePoseEditor] Saved: {path} ({kfCount} keyframes)");
        }

        private AnimationClip CreateSinglePoseClip(Animator animator)
        {
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var hp = new HumanPose();
            handler.GetHumanPose(ref hp);
            handler.Dispose();

            ApplyRootMotionBake(ref hp);

            var clip = new AnimationClip { frameRate = BonePoseEditorState.FrameRate };
            SetCurve(clip, "RootT.x", new Keyframe(0f, hp.bodyPosition.x));
            SetCurve(clip, "RootT.y", new Keyframe(0f, hp.bodyPosition.y));
            SetCurve(clip, "RootT.z", new Keyframe(0f, hp.bodyPosition.z));
            SetCurve(clip, "RootQ.x", new Keyframe(0f, hp.bodyRotation.x));
            SetCurve(clip, "RootQ.y", new Keyframe(0f, hp.bodyRotation.y));
            SetCurve(clip, "RootQ.z", new Keyframe(0f, hp.bodyRotation.z));
            SetCurve(clip, "RootQ.w", new Keyframe(0f, hp.bodyRotation.w));

            int mc = HumanTrait.MuscleCount;
            for (int i = 0; i < mc && i < hp.muscles.Length; i++)
                SetCurve(clip, HumanTrait.MuscleName[i],
                    new Keyframe(0f, hp.muscles[i]));
            return clip;
        }

        private AnimationClip CreateMultiKeyframeClip(Animator animator)
        {
            // Collect all unique keyframe times across all layers
            var timeSet = new SortedSet<float>();
            foreach (var layer in BonePoseEditorState.Layers)
            {
                foreach (var kf in layer.Keyframes)
                    timeSet.Add(kf.Time);
            }
            if (timeSet.Count == 0) return new AnimationClip();

            int mc = HumanTrait.MuscleCount;

            var rp = new[] {
                new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
            var rr = new[] {
                new List<Keyframe>(), new List<Keyframe>(),
                new List<Keyframe>(), new List<Keyframe>() };
            var mk = new List<Keyframe>[mc];
            for (int i = 0; i < mc; i++) mk[i] = new List<Keyframe>();

            var saved = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var kvp in BonePoseEditorState.BoneTransforms)
                saved[kvp.Key] = kvp.Value.localRotation;

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var hp = new HumanPose();

            try
            {
                foreach (float time in timeSet)
                {
                    // Flatten all layers at this time
                    var finalRots = LayerEvaluator.EvaluateAllLayers(
                        BonePoseEditorState.Layers, time,
                        BonePoseEditorState.OriginalRotations,
                        BonePoseEditorState.DefaultCurve);

                    foreach (var kvp in finalRots)
                        if (BonePoseEditorState.BoneTransforms.TryGetValue(
                                kvp.Key, out var t))
                            t.localRotation = kvp.Value;

                    // Apply IK if enabled
                    if (BonePoseEditorState.IKEnabled && BonePoseEditorState.IKTargets != null)
                    {
                        foreach (var target in BonePoseEditorState.IKTargets)
                        {
                            if (target.Enabled)
                                TwoBoneIKSolver.SolveTargetBlended(target,
                                    BonePoseEditorState.BoneTransforms);
                        }
                    }

                    handler.GetHumanPose(ref hp);
                    ApplyRootMotionBake(ref hp);

                    rp[0].Add(new Keyframe(time, hp.bodyPosition.x));
                    rp[1].Add(new Keyframe(time, hp.bodyPosition.y));
                    rp[2].Add(new Keyframe(time, hp.bodyPosition.z));
                    rr[0].Add(new Keyframe(time, hp.bodyRotation.x));
                    rr[1].Add(new Keyframe(time, hp.bodyRotation.y));
                    rr[2].Add(new Keyframe(time, hp.bodyRotation.z));
                    rr[3].Add(new Keyframe(time, hp.bodyRotation.w));

                    for (int i = 0; i < mc && i < hp.muscles.Length; i++)
                        mk[i].Add(new Keyframe(time, hp.muscles[i]));
                }
            }
            finally
            {
                foreach (var kvp in saved)
                    if (BonePoseEditorState.BoneTransforms.TryGetValue(
                            kvp.Key, out var t))
                        t.localRotation = kvp.Value;
                handler.Dispose();
            }

            var clip = new AnimationClip { frameRate = BonePoseEditorState.FrameRate };
            string[] rpA = { "RootT.x", "RootT.y", "RootT.z" };
            string[] rrA = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 3; i++)
                SetCurve(clip, rpA[i], rp[i].ToArray());
            for (int i = 0; i < 4; i++)
                SetCurve(clip, rrA[i], rr[i].ToArray());
            for (int i = 0; i < mc; i++)
                if (mk[i].Count > 0)
                    SetCurve(clip, HumanTrait.MuscleName[i], mk[i].ToArray());
            return clip;
        }

        private static void ApplyRootMotionBake(ref HumanPose hp)
        {
            if (BonePoseEditorState.BakeRootPosition)
            {
                hp.bodyPosition = new Vector3(
                    0f,
                    BonePoseEditorState.RootHeightOffset,
                    0f);
            }
            else if (Mathf.Abs(BonePoseEditorState.RootHeightOffset) > 0.0001f)
            {
                hp.bodyPosition = new Vector3(
                    hp.bodyPosition.x,
                    hp.bodyPosition.y + BonePoseEditorState.RootHeightOffset,
                    hp.bodyPosition.z);
            }

            if (BonePoseEditorState.BakeRootRotation)
            {
                hp.bodyRotation = Quaternion.identity;
            }
        }

        private static void SetCurve(AnimationClip clip,
            string attr, params Keyframe[] keys)
        {
            var binding = new EditorCurveBinding
            {
                path = "", type = typeof(Animator), propertyName = attr,
            };
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        }

        private static void EnsureAssetFolder(string folder)
        {
            folder = folder.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
