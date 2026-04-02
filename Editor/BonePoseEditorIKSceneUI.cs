using UnityEngine;
using UnityEditor;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorIKSceneUI
    {
        private static readonly Color ColorIKTarget = new Color(0.2f, 0.8f, 1f, 0.8f);
        private static readonly Color ColorIKPole = new Color(0.8f, 0.8f, 0.2f, 0.7f);
        private static readonly Color ColorIKLine = new Color(0.2f, 0.8f, 1f, 0.4f);
        private static readonly Color ColorIKPinned = new Color(1f, 0.2f, 0.2f, 0.3f);

        public static void DrawIKHandles()
        {
            if (!BonePoseEditorState.IKEnabled) return;
            if (BonePoseEditorState.IKTargets == null) return;
            if (BonePoseEditorState.IsPlaying) return;

            foreach (var target in BonePoseEditorState.IKTargets)
            {
                if (!target.Enabled) continue;
                DrawSingleIKTarget(target);
            }
        }

        private static void DrawSingleIKTarget(IKTarget target)
        {
            var bones = BonePoseEditorState.BoneTransforms;
            if (!bones.TryGetValue(target.EndBone, out var endT)) return;

            float handleSize = HandleUtility.GetHandleSize(target.TargetPosition);

            // Target position handle
            Handles.color = ColorIKTarget;
            EditorGUI.BeginChangeCheck();
            var newTargetPos = Handles.PositionHandle(
                target.TargetPosition, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                target.TargetPosition = newTargetPos;
                ApplyIKAndAutoKey(target);
            }

            // Target sphere — size and alpha scale with blend value
            float blendAlpha = Mathf.Lerp(0.2f, 1f, target.FKIKBlend);
            float blendSize = Mathf.Lerp(0.5f, 1f, target.FKIKBlend);
            Color sphereColor = target.Pinned ? ColorIKPinned : ColorIKTarget;
            sphereColor.a *= blendAlpha;
            Handles.color = sphereColor;
            Handles.SphereHandleCap(0, target.TargetPosition,
                Quaternion.identity, handleSize * 0.08f * blendSize, EventType.Repaint);

            // Pinned indicator: larger transparent red sphere
            if (target.Pinned)
            {
                Handles.color = ColorIKPinned;
                Handles.SphereHandleCap(0, target.TargetPosition,
                    Quaternion.identity, handleSize * 0.15f, EventType.Repaint);
            }

            // Pole position handle (smaller)
            Handles.color = ColorIKPole;
            EditorGUI.BeginChangeCheck();
            float poleSize = handleSize * 0.5f;
            var newPolePos = Handles.FreeMoveHandle(
                target.PolePosition, poleSize * 0.08f,
                Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                target.PolePosition = newPolePos;
                ApplyIKAndAutoKey(target);
            }

            // Pole sphere
            Handles.color = ColorIKPole;
            Handles.SphereHandleCap(0, target.PolePosition,
                Quaternion.identity, handleSize * 0.04f, EventType.Repaint);

            // Feedback line: end bone to target
            Handles.color = ColorIKLine;
            Handles.DrawDottedLine(endT.position, target.TargetPosition, 3f);

            // Line from upper bone through middle to pole direction
            if (bones.TryGetValue(target.UpperBone, out var upperT)
                && bones.TryGetValue(target.MiddleBone, out var midT))
            {
                Handles.color = new Color(ColorIKPole.r, ColorIKPole.g,
                    ColorIKPole.b, 0.3f);
                Handles.DrawDottedLine(midT.position, target.PolePosition, 2f);
            }

            // Label
            Handles.BeginGUI();
            Vector2 screen = HandleUtility.WorldToGUIPoint(target.TargetPosition);
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = ColorIKTarget },
                alignment = TextAnchor.MiddleCenter,
            };
            string label = target.Limb.ToString();
            if (target.Pinned) label += " " + M("(固定)");
            GUI.Label(new Rect(screen.x - 40, screen.y + 10, 80, 16),
                label, labelStyle);
            Handles.EndGUI();
        }

        private static void ApplyIKAndAutoKey(IKTarget target)
        {
            TwoBoneIKSolver.SolveTarget(target, BonePoseEditorState.BoneTransforms);
            BonePoseEditorState.TryAutoKey();
            SceneView.RepaintAll();
        }

        public static bool IsBoneControlledByIK(HumanBodyBones bone)
        {
            if (!BonePoseEditorState.IKEnabled) return false;
            if (BonePoseEditorState.IKTargets == null) return false;

            foreach (var target in BonePoseEditorState.IKTargets)
            {
                if (!target.Enabled) continue;
                if (target.FKIKBlend < 0.999f) continue; // Allow FK handle when not full IK
                if (target.UpperBone == bone || target.MiddleBone == bone
                    || target.EndBone == bone)
                    return true;
            }
            return false;
        }
    }
}
