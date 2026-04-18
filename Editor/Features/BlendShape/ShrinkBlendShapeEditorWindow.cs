using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class ShrinkBlendShapeEditorWindow : EditorWindow
    {
        private GameObject _avatarRoot;
        private SkinnedMeshRenderer _bodySmr;
        private List<ShrinkEntry> _shrinkEntries = new List<ShrinkEntry>();
        private Vector2 _scrollPos;
        private Dictionary<string, bool> _groupFoldouts = new Dictionary<string, bool>();

        private static readonly string[] GroupOrder = { "胴体", "脚", "腕（両側）", "腕（左）", "腕（右）", "頭/首", "その他" };

        private static readonly Color ActiveColor = new Color(0.2f, 0.8f, 0.2f);

        private class ShrinkEntry
        {
            public string name;
            public string displayName;
            public int blendShapeIndex;
            public string group;
        }

        [MenuItem("Window/紫陽花広場/Shrink Editor")]
        public static void ShowWindow()
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
            GetWindow<ShrinkBlendShapeEditorWindow>(M("シュリンクエディタ"));
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null) return;

            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null && root != _avatarRoot)
            {
                _avatarRoot = root;
                FindBodyMesh();
                RefreshShrinkList();
            }

            Repaint();
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();

            if (_bodySmr == null)
            {
                EditorGUILayout.HelpBox(M("Hierarchyでアバターを選択してください"), MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (_shrinkEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(M("このアバターにはShrink BlendShapeがありません"), MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawGlobalButtons();
            EditorGUILayout.Space(4);

            foreach (var groupName in GroupOrder)
            {
                var entries = _shrinkEntries.Where(e => e.group == groupName).ToList();
                if (entries.Count > 0)
                    DrawShrinkGroup(groupName, entries);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _avatarRoot = (GameObject)EditorGUILayout.ObjectField(M("アバター"), _avatarRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && _avatarRoot != null)
            {
                FindBodyMesh();
                RefreshShrinkList();
            }

            if (GUILayout.Button(M("自動検出"), GUILayout.Width(70)))
                AutoDetectFromSelection();

            EditorGUILayout.EndHorizontal();

            if (_bodySmr != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(M("ボディ"), GetRelativePath(_bodySmr.gameObject), EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            DrawSeparator();
        }

        private void DrawGlobalButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(M("全てリセット (0)")))
                SetAll(0f);

            if (GUILayout.Button(M("全てON (100)")))
                SetAll(100f);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawShrinkGroup(string groupName, List<ShrinkEntry> entries)
        {
            if (!_groupFoldouts.ContainsKey(groupName))
                _groupFoldouts[groupName] = true;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            _groupFoldouts[groupName] = EditorGUILayout.Foldout(_groupFoldouts[groupName], M(groupName), true, EditorStyles.foldoutHeader);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("ON", GUILayout.Width(36)))
                SetAllInGroup(entries, 100f);
            if (GUILayout.Button("OFF", GUILayout.Width(36)))
                SetAllInGroup(entries, 0f);

            EditorGUILayout.EndHorizontal();

            if (_groupFoldouts[groupName])
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);

                foreach (var entry in entries)
                    DrawShrinkToggle(entry);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawShrinkToggle(ShrinkEntry entry)
        {
            float weight = _bodySmr.GetBlendShapeWeight(entry.blendShapeIndex);
            bool isActive = weight > 50f;

            var prevBg = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = ActiveColor;

            if (GUILayout.Button(entry.displayName, GUILayout.MinWidth(60)))
                ToggleShrink(entry);

            GUI.backgroundColor = prevBg;
        }

        private void ToggleShrink(ShrinkEntry entry)
        {
            Undo.RecordObject(_bodySmr, "Toggle Shrink");

            float current = _bodySmr.GetBlendShapeWeight(entry.blendShapeIndex);
            float newValue = current > 50f ? 0f : 100f;
            _bodySmr.SetBlendShapeWeight(entry.blendShapeIndex, newValue);

            SceneView.RepaintAll();
        }

        private void SetAllInGroup(List<ShrinkEntry> entries, float value)
        {
            Undo.RecordObject(_bodySmr, "Set Shrink Group");

            foreach (var entry in entries)
                _bodySmr.SetBlendShapeWeight(entry.blendShapeIndex, value);

            SceneView.RepaintAll();
        }

        private void SetAll(float value)
        {
            Undo.RecordObject(_bodySmr, "Set All Shrink");

            foreach (var entry in _shrinkEntries)
                _bodySmr.SetBlendShapeWeight(entry.blendShapeIndex, value);

            SceneView.RepaintAll();
        }

        // ─── Refresh / Detection ───

        private void RefreshShrinkList()
        {
            _shrinkEntries.Clear();
            _groupFoldouts.Clear();

            if (_bodySmr == null || _bodySmr.sharedMesh == null) return;

            var mesh = _bodySmr.sharedMesh;

            // Detect which pattern this mesh uses
            bool hasShrinkPrefix = false;
            bool hasShrinkSeparator = false;

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (name.StartsWith("Shrink_")) hasShrinkPrefix = true;
                if (IsSectionSeparator(name) && ExtractSectionName(name) == "Shrink") hasShrinkSeparator = true;
            }

            if (hasShrinkSeparator)
                RefreshFromSeparatorPattern(mesh);
            else if (hasShrinkPrefix)
                RefreshFromPrefixPattern(mesh);
        }

        /// <summary>
        /// Pattern 1: Shrink_ prefix (e.g. Shrink_Hips, Shrink_UpperLeg_L)
        /// </summary>
        private void RefreshFromPrefixPattern(Mesh mesh)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!name.StartsWith("Shrink_")) continue;

                string stripped = name.Substring("Shrink_".Length);
                _shrinkEntries.Add(new ShrinkEntry
                {
                    name = name,
                    displayName = stripped,
                    blendShapeIndex = i,
                    group = ClassifyGroup(stripped)
                });
            }
        }

        /// <summary>
        /// Pattern 2: Section separator (e.g. ---------Shrink---------, ---------Separate---------)
        /// Shapes after "Shrink" and "Separate" sections are treated as shrink blend shapes.
        /// </summary>
        private void RefreshFromSeparatorPattern(Mesh mesh)
        {
            bool inShrinkSection = false;

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);

                if (IsSectionSeparator(name))
                {
                    string section = ExtractSectionName(name);
                    inShrinkSection = section == "Shrink" || section == "Separate";
                    continue;
                }

                if (!inShrinkSection) continue;

                string displayName = DeriveShrinkDisplayName(name);
                _shrinkEntries.Add(new ShrinkEntry
                {
                    name = name,
                    displayName = displayName,
                    blendShapeIndex = i,
                    group = ClassifyGroup(name)
                });
            }
        }

        private void AutoDetectFromSelection()
        {
            if (Selection.activeGameObject == null) return;

            var root = AutoDetectAvatarRoot(Selection.activeGameObject);
            if (root != null)
            {
                _avatarRoot = root;
                FindBodyMesh();
                RefreshShrinkList();
                Repaint();
            }
        }

        private void FindBodyMesh()
        {
            _bodySmr = null;
            if (_avatarRoot == null) return;

            var smrs = _avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                if (HasShrinkBlendShapes(smr.sharedMesh))
                {
                    _bodySmr = smr;
                    return;
                }
            }
        }

        private static bool HasShrinkBlendShapes(Mesh mesh)
        {
            bool foundShrinkSeparator = false;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (name.StartsWith("Shrink_")) return true;
                if (IsSectionSeparator(name) && ExtractSectionName(name) == "Shrink")
                    foundShrinkSeparator = true;
            }
            return foundShrinkSeparator;
        }

        // ─── Section Separator Parsing ───

        private static bool IsSectionSeparator(string name)
        {
            return name.Length >= 5 && name.StartsWith("-") && name.EndsWith("-");
        }

        private static string ExtractSectionName(string separatorName)
        {
            return separatorName.Trim('-');
        }

        // ─── Display Name ───

        /// <summary>
        /// Derives a display name from separator-pattern shapes.
        /// e.g. "Shoulder_OFF" → "Shoulder", "Shoulder_OFF_L" → "Shoulder L",
        ///      "Chest_1_OFF" → "Chest 1", "UpperLeg_2_OFF" → "UpperLeg 2"
        /// </summary>
        private static string DeriveShrinkDisplayName(string name)
        {
            if (name.EndsWith("_OFF_L"))
                return name.Substring(0, name.Length - "_OFF_L".Length).Replace("_", " ") + " L";
            if (name.EndsWith("_OFF_R"))
                return name.Substring(0, name.Length - "_OFF_R".Length).Replace("_", " ") + " R";
            if (name.EndsWith("_OFF"))
                return name.Substring(0, name.Length - "_OFF".Length).Replace("_", " ");
            return name;
        }

        // ─── Group Classification ───

        private static string ClassifyGroup(string name)
        {
            string lower = name.ToLower();

            // Determine left/right from suffix or keyword
            bool hasLeft = lower.EndsWith("_l") || lower.EndsWith("_off_l") || lower.Contains("left");
            bool hasRight = lower.EndsWith("_r") || lower.EndsWith("_off_r") || lower.Contains("right");

            // Extract base part name for classification
            // Remove known suffixes and number segments
            string baseName = lower
                .Replace("shrink_", "")
                .Replace("_off_l", "").Replace("_off_r", "").Replace("_off", "")
                .Replace("_l", "").Replace("_r", "");
            // Remove trailing number segments like "_1", "_2"
            while (baseName.Length >= 2 && baseName[baseName.Length - 2] == '_' && char.IsDigit(baseName[baseName.Length - 1]))
                baseName = baseName.Substring(0, baseName.Length - 2);

            if (IsArmPart(baseName))
            {
                if (hasLeft) return "腕（左）";
                if (hasRight) return "腕（右）";
                return "腕（両側）";
            }

            if (IsLegPart(baseName)) return "脚";
            if (IsTorsoPart(baseName)) return "胴体";
            if (IsHeadPart(baseName)) return "頭/首";

            return "その他";
        }

        private static bool IsArmPart(string lower)
        {
            return lower.Contains("arm") || lower.Contains("hand") ||
                   lower.Contains("shoulder") || lower.Contains("finger") ||
                   lower.Contains("elbow") || lower.Contains("wrist");
        }

        private static bool IsLegPart(string lower)
        {
            return lower.Contains("leg") || lower.Contains("foot") ||
                   lower.Contains("toe") || lower.Contains("knee") ||
                   lower.Contains("ankle");
        }

        private static bool IsTorsoPart(string lower)
        {
            return lower.Contains("hip") || lower.Contains("chest") ||
                   lower.Contains("spine") || lower.Contains("breast") ||
                   lower.Contains("waist") || lower.Contains("belly");
        }

        private static bool IsHeadPart(string lower)
        {
            return lower.Contains("head") || lower.Contains("neck") || lower.Contains("face");
        }

        // ─── Utility ───

        private static GameObject AutoDetectAvatarRoot(GameObject obj)
        {
            Transform current = obj.transform;
            GameObject bestRoot = null;
            while (current != null)
            {
                if (current.GetComponent("VRCAvatarDescriptor") != null ||
                    current.GetComponent("VRC_AvatarDescriptor") != null)
                    return current.gameObject;
                if (current.GetComponent<Animator>() != null)
                    bestRoot = current.gameObject;
                current = current.parent;
            }
            return bestRoot;
        }

        private static string GetRelativePath(GameObject go)
        {
            var parts = new List<string>();
            Transform current = go.transform;
            while (current.parent != null)
            {
                if (current.parent.GetComponent("VRCAvatarDescriptor") != null ||
                    current.parent.GetComponent<Animator>() != null)
                {
                    parts.Insert(0, current.name);
                    return string.Join("/", parts);
                }
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return go.name;
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}
