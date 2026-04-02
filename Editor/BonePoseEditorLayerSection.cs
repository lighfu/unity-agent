using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorLayerSection
    {
        private static Vector2 _scrollPos;
        private static bool _showGroupMask;

        public static void Draw()
        {
            var layers = BonePoseEditorState.Layers;
            if (layers == null) return;

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(M("レイヤー"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(M(".animをインポート"), EditorStyles.miniButton, GUILayout.Width(80)))
            {
                var imported = BonePoseEditorClipImporter.ImportClipFromDialog(
                    BonePoseEditorState.TargetAnimator);
                if (imported != null)
                {
                    layers.Add(imported);
                    BonePoseEditorState.ActiveLayerIndex = layers.Count - 1;
                    BonePoseEditorState.ApplyPoseAtTime(BonePoseEditorState.CurrentTime);
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                layers.Add(new PoseLayer("Layer " + layers.Count));
                BonePoseEditorState.ActiveLayerIndex = layers.Count - 1;
            }
            EditorGUILayout.EndHorizontal();

            // Layer list
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos, GUILayout.MaxHeight(150));

            int removeIndex = -1;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                bool isActive = i == BonePoseEditorState.ActiveLayerIndex;

                var bg = GUI.backgroundColor;
                if (isActive)
                    GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Select button (layer name)
                if (GUILayout.Button(layer.Name, isActive
                        ? EditorStyles.boldLabel : EditorStyles.label,
                    GUILayout.MinWidth(60)))
                {
                    BonePoseEditorState.ActiveLayerIndex = i;
                }

                // V(isible) toggle
                var vColor = layer.IsVisible
                    ? new Color(0.3f, 1f, 0.3f) : Color.gray;
                GUI.contentColor = vColor;
                if (GUILayout.Button("V", EditorStyles.miniButton, GUILayout.Width(20)))
                    layer.IsVisible = !layer.IsVisible;

                // S(olo) toggle
                var sColor = layer.IsSolo
                    ? new Color(1f, 0.9f, 0.2f) : Color.gray;
                GUI.contentColor = sColor;
                if (GUILayout.Button("S", EditorStyles.miniButton, GUILayout.Width(20)))
                    layer.IsSolo = !layer.IsSolo;

                // M(ute) toggle
                var mColor = layer.IsMute
                    ? new Color(1f, 0.3f, 0.3f) : Color.gray;
                GUI.contentColor = mColor;
                if (GUILayout.Button("M", EditorStyles.miniButton, GUILayout.Width(20)))
                    layer.IsMute = !layer.IsMute;

                GUI.contentColor = Color.white;

                // Delete (only if more than 1 layer)
                EditorGUI.BeginDisabledGroup(layers.Count <= 1);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                    removeIndex = i;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = bg;
            }

            EditorGUILayout.EndScrollView();

            // Remove layer if requested
            if (removeIndex >= 0)
            {
                layers.RemoveAt(removeIndex);
                if (BonePoseEditorState.ActiveLayerIndex >= layers.Count)
                    BonePoseEditorState.ActiveLayerIndex = layers.Count - 1;
            }

            // Active layer details
            var active = BonePoseEditorState.ActiveLayer;
            if (active != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(M("アクティブレイヤー"), EditorStyles.miniBoldLabel);

                active.Name = EditorGUILayout.TextField(M("名前"), active.Name);
                active.BlendMode = (LayerBlendMode)EditorGUILayout.EnumPopup(
                    M("ブレンドモード"), active.BlendMode);
                active.Weight = EditorGUILayout.Slider(M("ウェイト"), active.Weight, 0f, 1f);

                // Bone group mask
                _showGroupMask = EditorGUILayout.Foldout(_showGroupMask, M("ボーングループマスク"));
                if (_showGroupMask)
                {
                    EditorGUI.indentLevel++;
                    bool allGroups = active.BoneGroupMask == null;

                    EditorGUI.BeginChangeCheck();
                    bool newAllGroups = EditorGUILayout.Toggle(M("全グループ"), allGroups);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newAllGroups)
                            active.BoneGroupMask = null;
                        else
                        {
                            active.BoneGroupMask = new HashSet<int>();
                            for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
                                active.BoneGroupMask.Add(g);
                        }
                    }

                    if (!newAllGroups && active.BoneGroupMask != null)
                    {
                        for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
                        {
                            bool enabled = active.BoneGroupMask.Contains(g);
                            bool newEnabled = EditorGUILayout.Toggle(
                                BonePoseEditorState.BoneGroups[g].name, enabled);
                            if (newEnabled && !enabled) active.BoneGroupMask.Add(g);
                            if (!newEnabled && enabled) active.BoneGroupMask.Remove(g);
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                // Layer keyframe info
                EditorGUILayout.LabelField(
                    string.Format(M("{0} キーフレーム"), active.Keyframes.Count),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
