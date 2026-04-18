using UnityEngine;
using UnityEditor;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorGraphSection
    {
        public static void Draw()
        {
            if (!BonePoseEditorState.ShowGraphEditor) return;

            var selected = BonePoseEditorState.SelectedBone;
            if ((int)selected < 0)
            {
                EditorGUILayout.HelpBox(M("ボーンを選択してカーブを編集してください。"), MessageType.Info);
                return;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(string.Format(M("グラフ: {0}"), selected), EditorStyles.boldLabel);

            // Custom bezier graph editor
            var currentCurve = BonePoseEditorState.GetBoneCurve(selected);
            BonePoseEditorCustomGraph.Draw(currentCurve, selected);

            // Tangent mode buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("タンジェント:"), EditorStyles.miniLabel, GUILayout.Width(48));
            var tangentModes = new[] { TangentMode.Auto, TangentMode.Unified, TangentMode.Broken };
            foreach (var mode in tangentModes)
            {
                bool isActive = BonePoseEditorState.CurrentTangentMode == mode;
                var bg = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUILayout.Button(mode.ToString(), EditorStyles.miniButton))
                    BonePoseEditorState.CurrentTangentMode = mode;
                GUI.backgroundColor = bg;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Preset buttons row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(M("プリセット:"), EditorStyles.miniLabel, GUILayout.Width(40));
            if (GUILayout.Button(M("リニア"), EditorStyles.miniButton))
                BonePoseEditorState.SetBoneCurve(selected,
                    BonePoseEditorState.CreatePreset(CurvePreset.Linear));
            if (GUILayout.Button(M("スムーズ"), EditorStyles.miniButton))
                BonePoseEditorState.SetBoneCurve(selected,
                    BonePoseEditorState.CreatePreset(CurvePreset.Smooth));
            if (GUILayout.Button(M("イーズイン"), EditorStyles.miniButton))
                BonePoseEditorState.SetBoneCurve(selected,
                    BonePoseEditorState.CreatePreset(CurvePreset.EaseIn));
            if (GUILayout.Button(M("イーズアウト"), EditorStyles.miniButton))
                BonePoseEditorState.SetBoneCurve(selected,
                    BonePoseEditorState.CreatePreset(CurvePreset.EaseOut));
            if (GUILayout.Button(M("定数"), EditorStyles.miniButton))
                BonePoseEditorState.SetBoneCurve(selected,
                    BonePoseEditorState.CreatePreset(CurvePreset.Constant));
            EditorGUILayout.EndHorizontal();

            // Utility buttons row
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(M("グループにコピー"), EditorStyles.miniButton))
                CopyCurveToGroup(selected);

            if (GUILayout.Button(M("ミラーにコピー"), EditorStyles.miniButton))
                CopyCurveToMirror(selected);

            bool hasOverride = BonePoseEditorState.BoneCurves.ContainsKey(selected);
            EditorGUI.BeginDisabledGroup(!hasOverride);
            if (GUILayout.Button(M("リセット"), EditorStyles.miniButton))
            {
                BonePoseEditorState.ClearBoneCurve(selected);
                GUI.changed = true;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private static void CopyCurveToGroup(HumanBodyBones bone)
        {
            var curve = BonePoseEditorState.GetBoneCurve(bone);
            foreach (var group in BonePoseEditorState.BoneGroups)
            {
                bool found = false;
                foreach (var b in group.bones)
                {
                    if (b == bone) { found = true; break; }
                }
                if (!found) continue;

                foreach (var b in group.bones)
                {
                    if (b != bone && BonePoseEditorState.BoneTransforms.ContainsKey(b))
                        BonePoseEditorState.SetBoneCurve(b, curve);
                }
                break;
            }
        }

        private static void CopyCurveToMirror(HumanBodyBones bone)
        {
            var mirror = BonePoseEditorState.GetMirrorBone(bone);
            if ((int)mirror < 0) return;
            if (!BonePoseEditorState.BoneTransforms.ContainsKey(mirror)) return;

            var curve = BonePoseEditorState.GetBoneCurve(bone);
            BonePoseEditorState.SetBoneCurve(mirror, curve);
        }
    }
}
