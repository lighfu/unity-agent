using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [InitializeOnLoad]
    public static class BonePoseEditorSceneUI
    {
        private static readonly Color ColorSkeleton = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        private static readonly Color ColorSelected = new Color(1f, 0.92f, 0.016f, 1f);
        private static readonly Color ColorHovered = new Color(0f, 1f, 1f, 1f);
        private static readonly Color ColorMirror = new Color(0.4f, 0.6f, 1f, 1f);
        private static readonly Color ColorNormal = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        private static readonly Color ColorDragGuide = new Color(1f, 0.5f, 0f, 0.6f);
        private static readonly Color ColorOnionPrev = new Color(0.2f, 0.6f, 1f, 0.25f);
        private static readonly Color ColorOnionNext = new Color(1f, 0.3f, 0.2f, 0.25f);
        private static readonly Color ColorAutoKey = new Color(1f, 0.2f, 0.2f, 1f);

        // Control Rig group colors
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

        private enum ControlShape { Cross, WireDisc, Diamond }

        private static Dictionary<HumanBodyBones, int> _boneGroupLookup;

        // Drag state
        private static HumanBodyBones _draggingBone = (HumanBodyBones)(-1);
        private static int _hotControlId;
        private static Plane _dragPlane;
        private static int _undoGroup;
        private static Vector3 _dragTargetPos;

        // Label style cache
        private static GUIStyle _boneLabelStyle;
        private static GUIStyle _autoKeyStyle;

        static BonePoseEditorSceneUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!BonePoseEditorState.IsActive) return;
            if (BonePoseEditorState.TargetAnimator == null)
            {
                BonePoseEditorState.Deactivate();
                return;
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // Draw onion skin ghosts behind the live skeleton
            if (BonePoseEditorState.OnionSkinEnabled && !BonePoseEditorState.IsPlaying)
                DrawOnionSkin();

            HandleBoneInteraction(sceneView);
            DrawSkeletonLines();
            DrawControlRig();

            // IK handles (drawn before FK rotation handle)
            BonePoseEditorIKSceneUI.DrawIKHandles();

            if ((int)_draggingBone < 0)
            {
                // Suppress rotation handle for IK-controlled bones
                if (!BonePoseEditorIKSceneUI.IsBoneControlledByIK(
                        BonePoseEditorState.SelectedBone))
                    DrawRotationHandle();
            }
            else
                DrawDragFeedback();

            Handles.BeginGUI();
            DrawBoneLabels();
            DrawOverlayPanel();
            Handles.EndGUI();
        }

        // ─── Bone Picking ───

        private static HumanBodyBones PickBone(Vector2 mousePos)
        {
            float bestDist = 18f;
            HumanBodyBones best = (HumanBodyBones)(-1);

            foreach (var kvp in BonePoseEditorState.BoneTransforms)
            {
                Vector2 screen = HandleUtility.WorldToGUIPoint(kvp.Value.position);
                float dist = Vector2.Distance(screen, mousePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // ─── Interaction: Hover + Click-to-Select + Drag-to-Rotate ───

        private static void HandleBoneInteraction(SceneView sceneView)
        {
            Event e = Event.current;
            if (e == null) return;

            // Hover (always, even during playback)
            if (e.type == EventType.MouseMove)
            {
                var newHover = PickBone(e.mousePosition);
                if (newHover != BonePoseEditorState.HoveredBone)
                {
                    BonePoseEditorState.HoveredBone = newHover;
                    sceneView.Repaint();
                }
            }

            // No editing during playback
            if (BonePoseEditorState.IsPlaying) return;

            // Escape → deselect
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                BonePoseEditorState.SelectedBone = (HumanBodyBones)(-1);
                e.Use();
                sceneView.Repaint();
                return;
            }

            // MouseDown → select + start drag
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                var picked = PickBone(e.mousePosition);
                if ((int)picked >= 0)
                {
                    BonePoseEditorState.SelectedBone = picked;
                    StartDrag(picked, sceneView);
                    e.Use();
                    sceneView.Repaint();
                }
                else
                {
                    BonePoseEditorState.SelectedBone = (HumanBodyBones)(-1);
                    sceneView.Repaint();
                }
            }

            // MouseDrag → rotate parent bone so dragged bone follows cursor
            if (e.type == EventType.MouseDrag && (int)_draggingBone >= 0
                && GUIUtility.hotControl == _hotControlId)
            {
                ApplyDragRotation(e.mousePosition);
                e.Use();
                sceneView.Repaint();
            }

            // MouseUp → end drag + auto-key
            if (e.type == EventType.MouseUp && e.button == 0 && (int)_draggingBone >= 0)
            {
                Undo.CollapseUndoOperations(_undoGroup);
                _draggingBone = (HumanBodyBones)(-1);
                GUIUtility.hotControl = 0;
                BonePoseEditorState.TryAutoKey();
                e.Use();
            }
        }

        private static void StartDrag(HumanBodyBones bone, SceneView sceneView)
        {
            _draggingBone = bone;
            _hotControlId = GUIUtility.GetControlID(FocusType.Passive);
            GUIUtility.hotControl = _hotControlId;

            var boneT = BonePoseEditorState.BoneTransforms[bone];
            _dragPlane = new Plane(sceneView.camera.transform.forward, boneT.position);
            _dragTargetPos = boneT.position;

            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
        }

        private static void ApplyDragRotation(Vector2 mousePosition)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (!_dragPlane.Raycast(ray, out float enter)) return;

            _dragTargetPos = ray.GetPoint(enter);
            var boneT = BonePoseEditorState.BoneTransforms[_draggingBone];

            Transform rotatedTransform;
            HumanBodyBones rotatedBone;

            if (BonePoseEditorState.TryGetParentBone(_draggingBone, out var parentBone)
                && BonePoseEditorState.BoneTransforms.TryGetValue(parentBone, out var parentT))
            {
                // Has parent → rotate parent so this bone follows cursor
                rotatedBone = parentBone;
                rotatedTransform = parentT;

                Vector3 oldDir = (boneT.position - parentT.position).normalized;
                Vector3 newDir = (_dragTargetPos - parentT.position).normalized;
                if (oldDir.sqrMagnitude < 0.0001f || newDir.sqrMagnitude < 0.0001f) return;

                Undo.RecordObject(parentT, "Drag Bone");
                parentT.rotation = Quaternion.FromToRotation(oldDir, newDir) * parentT.rotation;
            }
            else
            {
                // Root bone (Hips) → use Animator root as pivot
                rotatedBone = _draggingBone;
                rotatedTransform = boneT;

                Vector3 pivot = BonePoseEditorState.TargetAnimator.transform.position;
                Vector3 oldDir = (boneT.position - pivot).normalized;
                Vector3 newDir = (_dragTargetPos - pivot).normalized;
                if (oldDir.sqrMagnitude < 0.0001f || newDir.sqrMagnitude < 0.0001f) return;

                Undo.RecordObject(boneT, "Drag Bone");
                boneT.rotation = Quaternion.FromToRotation(oldDir, newDir) * boneT.rotation;
            }

            // Mirror
            if (BonePoseEditorState.SymmetryEnabled)
            {
                var mirror = BonePoseEditorState.GetMirrorBone(rotatedBone);
                if ((int)mirror >= 0
                    && BonePoseEditorState.BoneTransforms.TryGetValue(mirror, out var mirrorT))
                {
                    Undo.RecordObject(mirrorT, "Drag Bone (Mirror)");
                    mirrorT.localRotation =
                        BonePoseEditorState.ComputeMirrorRotation(rotatedTransform.localRotation);
                }
            }
        }

        // ─── Drawing ───

        private static void DrawSkeletonLines()
        {
            var bones = BonePoseEditorState.BoneTransforms;
            var selected = BonePoseEditorState.SelectedBone;
            var mirrorOfSelected = (int)selected >= 0
                ? BonePoseEditorState.GetMirrorBone(selected)
                : (HumanBodyBones)(-1);

            foreach (var kvp in bones)
            {
                if (!BonePoseEditorState.TryGetParentBone(kvp.Key, out var parentBone)) continue;
                if (!bones.TryGetValue(parentBone, out var parentT)) continue;

                if (kvp.Key == selected || parentBone == selected)
                    Handles.color = ColorSelected;
                else if (BonePoseEditorState.SymmetryEnabled
                         && (kvp.Key == mirrorOfSelected || parentBone == mirrorOfSelected))
                    Handles.color = ColorMirror;
                else
                    Handles.color = ColorSkeleton;

                Handles.DrawLine(parentT.position, kvp.Value.position);
            }
        }

        // ─── Control Rig ───

        private static void BuildBoneGroupLookup()
        {
            _boneGroupLookup = new Dictionary<HumanBodyBones, int>();
            for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
            {
                foreach (var bone in BonePoseEditorState.BoneGroups[g].bones)
                    _boneGroupLookup[bone] = g;
            }
        }

        private static ControlShape GetControlShape(int groupIndex)
        {
            switch (groupIndex)
            {
                case 0: return ControlShape.Cross;      // Body
                case 1: case 2: return ControlShape.WireDisc; // Arms
                case 3: case 4: return ControlShape.WireDisc; // Legs
                case 5: case 6: return ControlShape.Diamond;  // Fingers
                default: return ControlShape.Cross;
            }
        }

        private static float GetShapeSizeFactor(int groupIndex)
        {
            switch (groupIndex)
            {
                case 0: return 0.35f;  // Body — cross
                case 1: case 2: return 0.24f;  // Arms — disc
                case 3: case 4: return 0.24f;  // Legs — disc
                case 5: case 6: return 0.14f;  // Fingers — diamond
                default: return 0.18f;
            }
        }

        private static float GetLineWidth(int groupIndex)
        {
            switch (groupIndex)
            {
                case 0: return 6f;   // Body
                case 1: case 2: return 5f; // Arms
                case 3: case 4: return 5f; // Legs
                case 5: case 6: return 4f; // Fingers
                default: return 4f;
            }
        }

        private static Vector3 GetBoneDirection(HumanBodyBones bone, Transform boneT)
        {
            // Try to find a child bone to determine direction
            if (BonePoseEditorState.TryGetParentBone(bone, out _))
            {
                // Look for children of this bone
                foreach (var kvp in BonePoseEditorState.BoneTransforms)
                {
                    if (BonePoseEditorState.TryGetParentBone(kvp.Key, out var parent)
                        && parent == bone)
                    {
                        return (kvp.Value.position - boneT.position).normalized;
                    }
                }
            }

            // Leaf bone — use parent→self direction
            if (BonePoseEditorState.TryGetParentBone(bone, out var parentBone)
                && BonePoseEditorState.BoneTransforms.TryGetValue(parentBone, out var parentT))
            {
                return (boneT.position - parentT.position).normalized;
            }

            return boneT.up;
        }

        private static void DrawControlRig()
        {
            if (_boneGroupLookup == null) BuildBoneGroupLookup();

            var selected = BonePoseEditorState.SelectedBone;
            var hovered = BonePoseEditorState.HoveredBone;
            var mirror = (int)selected >= 0
                ? BonePoseEditorState.GetMirrorBone(selected)
                : (HumanBodyBones)(-1);

            foreach (var kvp in BonePoseEditorState.BoneTransforms)
            {
                var bone = kvp.Key;
                var boneT = kvp.Value;
                var pos = boneT.position;

                if (!_boneGroupLookup.TryGetValue(bone, out int groupIndex))
                    groupIndex = 0;

                float handleSize = HandleUtility.GetHandleSize(pos);
                float sizeFactor = GetShapeSizeFactor(groupIndex);
                float size = handleSize * sizeFactor;

                // Determine color
                Color color;
                if (bone == selected)
                {
                    color = ColorSelected;
                    size *= 1.5f;
                }
                else if (bone == hovered)
                {
                    color = ColorHovered;
                    size *= 1.3f;
                }
                else if (bone == mirror && BonePoseEditorState.SymmetryEnabled)
                {
                    color = ColorMirror;
                }
                else
                {
                    color = groupIndex < GroupColors.Length
                        ? GroupColors[groupIndex] : ColorNormal;
                }

                // Draw shape based on group
                float lineWidth = GetLineWidth(groupIndex);
                var shape = GetControlShape(groupIndex);
                switch (shape)
                {
                    case ControlShape.Cross:
                        DrawCrossShape(pos, boneT.rotation, size, color, lineWidth);
                        break;
                    case ControlShape.WireDisc:
                        var boneDir = GetBoneDirection(bone, boneT);
                        DrawWireDiscShape(pos, boneDir, size, color, lineWidth);
                        break;
                    case ControlShape.Diamond:
                        DrawDiamondShape(pos, size, color, lineWidth);
                        break;
                }

                // Filled center dot for visibility
                float dotSize = HandleUtility.GetHandleSize(pos) * 0.06f;
                Handles.color = color;
                Handles.SphereHandleCap(0, pos, Quaternion.identity,
                    dotSize, EventType.Repaint);
            }
        }

        private static void DrawCrossShape(Vector3 pos, Quaternion rot,
            float size, Color color, float lineWidth)
        {
            Handles.color = color;
            var right = rot * Vector3.right * size;
            var up = rot * Vector3.up * size;
            Handles.DrawAAPolyLine(lineWidth, pos - right, pos + right);
            Handles.DrawAAPolyLine(lineWidth, pos - up, pos + up);
        }

        private static void DrawWireDiscShape(Vector3 pos, Vector3 boneDir,
            float size, Color color, float lineWidth)
        {
            if (boneDir.sqrMagnitude < 0.001f) boneDir = Vector3.up;

            // Filled disc (semi-transparent)
            Handles.color = new Color(color.r, color.g, color.b, 0.25f);
            Handles.DrawSolidDisc(pos, boneDir, size);

            // Outline as thick polyline
            Handles.color = color;
            const int segments = 24;
            var points = new Vector3[segments + 1];
            var perp = Vector3.Cross(boneDir, Vector3.up).normalized;
            if (perp.sqrMagnitude < 0.001f)
                perp = Vector3.Cross(boneDir, Vector3.right).normalized;
            var perp2 = Vector3.Cross(boneDir, perp).normalized;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                points[i] = pos + (perp * Mathf.Cos(angle) + perp2 * Mathf.Sin(angle)) * size;
            }
            Handles.DrawAAPolyLine(lineWidth, points);
        }

        private static void DrawDiamondShape(Vector3 pos, float size, Color color,
            float lineWidth)
        {
            Handles.color = color;
            var cam = SceneView.currentDrawingSceneView?.camera;
            if (cam == null) return;

            var camRight = cam.transform.right * size;
            var camUp = cam.transform.up * size;

            var top = pos + camUp;
            var right = pos + camRight;
            var bottom = pos - camUp;
            var left = pos - camRight;

            Handles.DrawAAPolyLine(lineWidth, top, right, bottom, left, top);
        }

        private static void DrawBoneLabels()
        {
            if (_boneLabelStyle == null)
            {
                _boneLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
            }

            // Show label for hovered or selected bone
            var hovered = BonePoseEditorState.HoveredBone;
            var selected = BonePoseEditorState.SelectedBone;

            if ((int)hovered >= 0 && hovered != selected
                && BonePoseEditorState.BoneTransforms.TryGetValue(hovered, out var ht))
            {
                DrawWorldLabel(ht.position, hovered.ToString(), _boneLabelStyle);
            }
            if ((int)selected >= 0
                && BonePoseEditorState.BoneTransforms.TryGetValue(selected, out var st))
            {
                string selLabel = selected.ToString() + " " + GetSpaceIndicator(selected);
                DrawWorldLabel(st.position, selLabel, _boneLabelStyle);
            }
        }

        private static void DrawWorldLabel(Vector3 worldPos, string text, GUIStyle style)
        {
            Vector2 screen = HandleUtility.WorldToGUIPoint(worldPos);
            var rect = new Rect(screen.x - 80, screen.y - 28, 160, 18);
            GUI.Label(rect, text, style);
        }

        private static void DrawRotationHandle()
        {
            var selected = BonePoseEditorState.SelectedBone;
            if ((int)selected < 0) return;
            if (!BonePoseEditorState.BoneTransforms.TryGetValue(selected, out var transform))
                return;

            // Determine handle rotation based on space setting
            Quaternion handleRot;
            BoneSpace activeSpace = BoneSpace.Local;
            SpaceOverride spaceOverride = null;

            // Check keyframe space override
            var kf = BonePoseEditorState.GetKeyframeAtCurrentTime();
            if (kf != null && kf.BoneSpaces.TryGetValue(selected, out spaceOverride))
                activeSpace = spaceOverride.Space;

            // UseWorldRotation toggle overrides to World
            if (BonePoseEditorState.UseWorldRotation)
                activeSpace = BoneSpace.World;

            switch (activeSpace)
            {
                case BoneSpace.World:
                    handleRot = Quaternion.identity;
                    break;
                case BoneSpace.ParentBone:
                    if (spaceOverride != null
                        && BonePoseEditorState.BoneTransforms.TryGetValue(
                            spaceOverride.ReferenceBone, out var refT))
                        handleRot = refT.rotation;
                    else
                        handleRot = transform.rotation;
                    break;
                default: // Local
                    handleRot = transform.rotation;
                    break;
            }

            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(handleRot, transform.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(transform, "Rotate Bone");

                if (activeSpace != BoneSpace.Local)
                {
                    Quaternion delta = newRot * Quaternion.Inverse(handleRot);
                    transform.rotation = delta * transform.rotation;
                }
                else
                {
                    transform.rotation = newRot;
                }

                if (BonePoseEditorState.SymmetryEnabled)
                {
                    var mirrorBone = BonePoseEditorState.GetMirrorBone(selected);
                    if ((int)mirrorBone >= 0
                        && BonePoseEditorState.BoneTransforms.TryGetValue(mirrorBone, out var mirrorT))
                    {
                        Undo.RecordObject(mirrorT, "Rotate Bone (Mirror)");
                        mirrorT.localRotation =
                            BonePoseEditorState.ComputeMirrorRotation(transform.localRotation);
                    }
                }

                BonePoseEditorState.TryAutoKey();
            }
        }

        private static void DrawDragFeedback()
        {
            if ((int)_draggingBone < 0) return;

            // Guide line from pivot to target
            Vector3 pivot;
            if (BonePoseEditorState.TryGetParentBone(_draggingBone, out var parentBone)
                && BonePoseEditorState.BoneTransforms.TryGetValue(parentBone, out var parentT))
                pivot = parentT.position;
            else
                pivot = BonePoseEditorState.TargetAnimator.transform.position;

            Handles.color = ColorDragGuide;
            Handles.DrawDottedLine(pivot, _dragTargetPos, 4f);

            // Ghost sphere at target
            float size = HandleUtility.GetHandleSize(_dragTargetPos) * 0.05f;
            Handles.color = new Color(1f, 0.5f, 0f, 0.3f);
            Handles.SphereHandleCap(
                0, _dragTargetPos, Quaternion.identity, size * 2f, EventType.Repaint);
        }

        // ─── Onion Skin ───

        private static void DrawOnionSkin()
        {
            var prev = BonePoseEditorState.GetPrevKeyframe();
            var next = BonePoseEditorState.GetNextKeyframe();

            if (prev != null && prev.BoneWorldPositions.Count > 0)
                DrawGhostSkeleton(prev, ColorOnionPrev);

            if (next != null && next.BoneWorldPositions.Count > 0)
                DrawGhostSkeleton(next, ColorOnionNext);
        }

        private static void DrawGhostSkeleton(PoseKeyframe keyframe, Color color)
        {
            var positions = keyframe.BoneWorldPositions;

            // Draw ghost skeleton lines
            Handles.color = color;
            foreach (var kvp in positions)
            {
                if (!BonePoseEditorState.TryGetParentBone(kvp.Key, out var parentBone)) continue;
                if (!positions.TryGetValue(parentBone, out var parentPos)) continue;

                Handles.DrawDottedLine(parentPos, kvp.Value, 3f);
            }

            // Draw ghost joint spheres
            Color sphereColor = new Color(color.r, color.g, color.b, color.a * 0.6f);
            Handles.color = sphereColor;
            foreach (var kvp in positions)
            {
                float size = HandleUtility.GetHandleSize(kvp.Value) * 0.025f;
                Handles.SphereHandleCap(
                    0, kvp.Value, Quaternion.identity, size * 2f, EventType.Repaint);
            }
        }

        // ─── Overlay Panel (top-left) ───

        private static void DrawOverlayPanel()
        {
            var selected = BonePoseEditorState.SelectedBone;
            bool hasBone = (int)selected >= 0
                           && BonePoseEditorState.BoneTransforms.ContainsKey(selected);

            float panelHeight = hasBone ? 165f : 50f;
            if (BonePoseEditorState.AutoKeyEnabled) panelHeight += 18f;

            var rect = new Rect(10, 10, 280, panelHeight);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(
                new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            // Auto-key indicator
            if (BonePoseEditorState.AutoKeyEnabled)
            {
                if (_autoKeyStyle == null)
                {
                    _autoKeyStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 11,
                        normal = { textColor = ColorAutoKey },
                    };
                }
                GUILayout.Label(M("● オートキー記録中"), _autoKeyStyle);
            }

            if (!hasBone)
            {
                GUILayout.Label(M("ボーンポーズエディタ"), EditorStyles.boldLabel);
                string hint;
                if (BonePoseEditorState.IsPlaying)
                    hint = M("再生中... 停止して編集。");
                else if (BonePoseEditorState.AutoKeyEnabled)
                    hint = M("オートキーON。ボーンをドラッグしてキーフレームを記録。");
                else
                    hint = M("ボーンをドラッグしてポーズ。キーフレーム追加で記録。");
                GUILayout.Label(hint, EditorStyles.miniLabel);
            }
            else
            {
                var transform = BonePoseEditorState.BoneTransforms[selected];
                var euler = transform.localRotation.eulerAngles;

                // Space indicator
                string spaceLabel = GetSpaceIndicator(selected);
                GUILayout.Label(string.Format(M("ボーン: {0}  {1}"), selected, spaceLabel), EditorStyles.boldLabel);
                GUILayout.Label(
                    string.Format(M("回転: ({0:F1}, {1:F1}, {2:F1})"), euler.x, euler.y, euler.z),
                    EditorStyles.miniLabel);

                GUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                var newEuler = EditorGUILayout.Vector3Field(M("オイラー"), euler);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(transform, "Edit Bone Euler");
                    transform.localRotation = Quaternion.Euler(newEuler);
                    ApplyMirror(selected, transform);
                    BonePoseEditorState.TryAutoKey();
                }

                GUILayout.Space(2);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(M("リセット"), EditorStyles.miniButton))
                {
                    if (BonePoseEditorState.OriginalRotations.TryGetValue(selected, out var orig))
                    {
                        Undo.RecordObject(transform, "Reset Bone");
                        transform.localRotation = orig;
                        BonePoseEditorState.TryAutoKey();
                    }
                }
                if (GUILayout.Button(M("コピー"), EditorStyles.miniButton))
                    BonePoseEditorState.CopiedRotation = transform.localRotation;
                EditorGUI.BeginDisabledGroup(!BonePoseEditorState.CopiedRotation.HasValue);
                if (GUILayout.Button(M("ペースト"), EditorStyles.miniButton))
                {
                    Undo.RecordObject(transform, "Paste Bone Rotation");
                    transform.localRotation = BonePoseEditorState.CopiedRotation.Value;
                    ApplyMirror(selected, transform);
                    BonePoseEditorState.TryAutoKey();
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private static string GetSpaceIndicator(HumanBodyBones bone)
        {
            var kf = BonePoseEditorState.GetKeyframeAtCurrentTime();
            if (kf == null || !kf.BoneSpaces.TryGetValue(bone, out var spaceOverride))
                return "[L]";

            switch (spaceOverride.Space)
            {
                case BoneSpace.World:
                    return "[W]";
                case BoneSpace.ParentBone:
                    return $"[P:{spaceOverride.ReferenceBone}]";
                default:
                    return "[L]";
            }
        }

        private static void ApplyMirror(HumanBodyBones bone, Transform transform)
        {
            if (!BonePoseEditorState.SymmetryEnabled) return;
            var mirrorBone = BonePoseEditorState.GetMirrorBone(bone);
            if ((int)mirrorBone >= 0
                && BonePoseEditorState.BoneTransforms.TryGetValue(mirrorBone, out var mt))
            {
                Undo.RecordObject(mt, "Mirror Rotation");
                mt.localRotation =
                    BonePoseEditorState.ComputeMirrorRotation(transform.localRotation);
            }
        }
    }
}
