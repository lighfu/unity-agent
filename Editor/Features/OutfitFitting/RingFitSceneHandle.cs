using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.Editor.MA;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [InitializeOnLoad]
    public static class RingFitSceneHandle
    {
        private static float _positionT;
        private static float _scale;
        private static float _nudgeStep = 0.001f;
        private static Transform _lastSelected;

        static RingFitSceneHandle()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            var selected = Selection.activeTransform;
            if (selected == null) return;

            // Must have mesh component
            if (selected.GetComponent<MeshFilter>() == null && selected.GetComponent<MeshRenderer>() == null)
                return;

            // Get bone context (supports both direct parent and MA Bone Proxy)
            Transform bone, nextBone;
            Vector3 fingerDirLocal;
            if (!MeshAnalysisTools.TryGetBoneContext(selected, out bone, out nextBone, out fingerDirLocal))
                return;

            float boneLengthLocal = bone.InverseTransformPoint(nextBone.position).magnitude;
            if (boneLengthLocal < 0.0001f) return;

            // Initialize on selection change
            if (_lastSelected != selected)
            {
                SyncFromTransform(selected, bone, fingerDirLocal, boneLengthLocal);
                _lastSelected = selected;
            }

            Vector3 bonePos = bone.position;
            Vector3 nextBonePos = nextBone.position;
            Vector3 boneWorldDir = (nextBonePos - bonePos).normalized;
            float handleSize = HandleUtility.GetHandleSize(selected.position);

            // Perpendicular axes
            Vector3 perpA = Vector3.Cross(boneWorldDir, Vector3.up).normalized;
            if (perpA.sqrMagnitude < 0.001f)
                perpA = Vector3.Cross(boneWorldDir, Vector3.right).normalized;
            Vector3 perpB = Vector3.Cross(boneWorldDir, perpA).normalized;

            // --- Bone reference line (green dotted) ---
            Handles.color = new Color(0f, 1f, 0f, 0.5f);
            Handles.DrawDottedLine(bonePos, nextBonePos, 4f);

            // --- Arrow handle: slide along bone ---
            Handles.color = Color.green;
            EditorGUI.BeginChangeCheck();
            Vector3 newSlidePos = Handles.Slider(
                selected.position, boneWorldDir,
                handleSize * 0.8f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selected, "Slide Ring Along Bone");
                Vector3 delta = newSlidePos - selected.position;
                ApplyWorldDelta(selected, bone, delta);
                SyncFromTransform(selected, bone, fingerDirLocal, boneLengthLocal);
            }

            // --- Disc handle: perpendicular centering ---
            Handles.color = new Color(1f, 1f, 0f, 0.6f);
            EditorGUI.BeginChangeCheck();
            Vector3 discPos = Handles.Slider2D(
                selected.position, boneWorldDir, perpA, perpB,
                handleSize * 0.25f, Handles.CircleHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selected, "Center Ring");
                Vector3 delta = discPos - selected.position;
                ApplyWorldDelta(selected, bone, delta);
            }

            // --- Scale handle (cyan cube) ---
            Vector3 scaleHandlePos = selected.position + perpA * handleSize * 0.6f;
            Handles.color = Color.cyan;
            EditorGUI.BeginChangeCheck();
            float newScaleVal = Handles.ScaleValueHandle(
                _scale, scaleHandlePos,
                Quaternion.LookRotation(perpA),
                handleSize * 0.35f,
                Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                newScaleVal = Mathf.Clamp(newScaleVal, 0.01f, 5f);
                Undo.RecordObject(selected, "Scale Ring");
                selected.localScale = Vector3.one * newScaleVal;
                _scale = newScaleVal;
            }

            // --- Wire disc visualization ---
            Handles.color = new Color(0.3f, 0.7f, 1f, 0.3f);
            Handles.DrawWireDisc(selected.position, boneWorldDir, handleSize * 0.3f * _scale);

            // --- GUI Overlay Panel ---
            DrawOverlay(selected, bone, nextBone, fingerDirLocal, boneLengthLocal);

            sceneView.Repaint();
        }

        private static void ApplyWorldDelta(Transform ring, Transform bone, Vector3 worldDelta)
        {
            if (ring.parent == bone)
                ring.localPosition += bone.InverseTransformVector(worldDelta);
            else
                ring.position += worldDelta;
        }

        private static void SyncFromTransform(Transform ring, Transform bone, Vector3 fingerDirLocal, float boneLengthLocal)
        {
            Vector3 localPos;
            if (ring.parent == bone)
                localPos = ring.localPosition;
            else
                localPos = bone.InverseTransformPoint(ring.position);

            float along = Vector3.Dot(localPos, fingerDirLocal);
            _positionT = Mathf.Clamp01(along / boneLengthLocal);
            _scale = ring.localScale.x;
        }

        private static void DrawOverlay(Transform selected, Transform bone, Transform nextBone, Vector3 fingerDirLocal, float boneLengthLocal)
        {
            Handles.BeginGUI();

            var rect = new Rect(10, 10, 260, 155);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            GUILayout.Label(string.Format(M("リング: {0}"), selected.name), EditorStyles.boldLabel);
            GUILayout.Label(string.Format(M("ボーン: {0} \u2192 {1}"), bone.name, nextBone.name), EditorStyles.miniLabel);

            // MA Bone Proxy indicator
            if (MAComponentFactory.HasBoneProxy(selected.gameObject))
                GUILayout.Label(M("MA Bone Proxy: 有効"), EditorStyles.miniLabel);

            GUILayout.Space(2);

            // Position slider (0-1)
            EditorGUI.BeginChangeCheck();
            float newT = EditorGUILayout.Slider(M("位置"), _positionT, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selected, "Adjust Ring Position");
                ApplyPositionT(selected, bone, fingerDirLocal, boneLengthLocal, newT);
                _positionT = newT;
            }

            // Nudge buttons
            GUILayout.BeginHorizontal();
            GUILayout.Label(M("微調整"), GUILayout.Width(42));
            if (GUILayout.Button("\u25C0", GUILayout.Width(24)))
            {
                Undo.RecordObject(selected, "Nudge Ring");
                ApplyNudge(selected, bone, fingerDirLocal, -_nudgeStep);
                SyncFromTransform(selected, bone, fingerDirLocal, boneLengthLocal);
            }
            _nudgeStep = EditorGUILayout.FloatField(_nudgeStep, GUILayout.Width(60));
            if (GUILayout.Button("\u25B6", GUILayout.Width(24)))
            {
                Undo.RecordObject(selected, "Nudge Ring");
                ApplyNudge(selected, bone, fingerDirLocal, _nudgeStep);
                SyncFromTransform(selected, bone, fingerDirLocal, boneLengthLocal);
            }
            GUILayout.EndHorizontal();

            // Scale slider
            EditorGUI.BeginChangeCheck();
            float newScale = EditorGUILayout.Slider(M("スケール"), _scale, 0.01f, 5.0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selected, "Adjust Ring Scale");
                selected.localScale = Vector3.one * newScale;
                _scale = newScale;
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void ApplyPositionT(Transform ring, Transform bone, Vector3 fingerDirLocal, float boneLengthLocal, float t)
        {
            if (ring.parent == bone)
            {
                Vector3 localPos = ring.localPosition;
                Vector3 perp = localPos - fingerDirLocal * Vector3.Dot(localPos, fingerDirLocal);
                ring.localPosition = fingerDirLocal * (t * boneLengthLocal) + perp;
            }
            else
            {
                Vector3 boneLocalPos = bone.InverseTransformPoint(ring.position);
                Vector3 perp = boneLocalPos - fingerDirLocal * Vector3.Dot(boneLocalPos, fingerDirLocal);
                Vector3 newLocal = fingerDirLocal * (t * boneLengthLocal) + perp;
                ring.position = bone.TransformPoint(newLocal);
            }
        }

        private static void ApplyNudge(Transform ring, Transform bone, Vector3 fingerDirLocal, float amount)
        {
            if (ring.parent == bone)
                ring.localPosition += fingerDirLocal * amount;
            else
                ring.position += bone.TransformVector(fingerDirLocal * amount);
        }
    }
}
