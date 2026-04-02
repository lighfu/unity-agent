using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorLibrarySection
    {
        private static string _newPoseName = "";
        private static string _newPoseCategory = "";
        private static Vector2 _scrollPos;
        private static bool _gridView = true;
        private static bool _showGroupMask;
        private static HashSet<int> _applyGroupMask; // null = all

        // Search & filter
        private static string _searchFilter = "";
        private static string _categoryFilter = ""; // "" = All

        // Hover preview
        private static bool _previewActive;
        private static Dictionary<HumanBodyBones, Quaternion> _savedPreviewRotations;

        // Inline rename
        private static int _renamingIndex = -1;
        private static string _renamingText = "";

        // Thumbnail cache
        private static readonly Dictionary<PoseEntry, Texture2D> _thumbCache
            = new Dictionary<PoseEntry, Texture2D>();

        public static void Draw()
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(M("ポーズライブラリ"), EditorStyles.boldLabel);

            // Library asset field
            EditorGUILayout.BeginHorizontal();
            BonePoseEditorState.CurrentLibrary = (PoseLibraryAsset)
                EditorGUILayout.ObjectField(BonePoseEditorState.CurrentLibrary,
                    typeof(PoseLibraryAsset), false);

            if (GUILayout.Button(M("新規"), EditorStyles.miniButton, GUILayout.Width(36)))
                CreateNewLibrary();
            EditorGUILayout.EndHorizontal();

            var lib = BonePoseEditorState.CurrentLibrary;
            if (lib == null)
            {
                EditorGUILayout.HelpBox(
                    M("ポーズライブラリアセットを選択または作成してください。"), MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Save pose row (name + category + save button)
            EditorGUILayout.BeginHorizontal();
            _newPoseName = EditorGUILayout.TextField(_newPoseName,
                GUILayout.MinWidth(60));
            GUILayout.Label(M("分類:"), EditorStyles.miniLabel, GUILayout.Width(24));
            _newPoseCategory = EditorGUILayout.TextField(_newPoseCategory,
                GUILayout.Width(60));
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_newPoseName));
            if (GUILayout.Button(M("保存"), EditorStyles.miniButton, GUILayout.Width(40)))
            {
                PoseLibraryOperations.SavePose(lib, _newPoseName,
                    BonePoseEditorState.BoneTransforms,
                    category: _newPoseCategory);
                _newPoseName = "";
                GUI.FocusControl(null);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // Blend weight
            BonePoseEditorState.PoseBlendWeight = EditorGUILayout.Slider(
                M("ブレンドウェイト"), BonePoseEditorState.PoseBlendWeight, 0f, 1f);

            // Group mask foldout
            _showGroupMask = EditorGUILayout.Foldout(_showGroupMask, M("適用グループマスク"));
            if (_showGroupMask)
            {
                EditorGUI.indentLevel++;
                bool allGroups = _applyGroupMask == null;
                EditorGUI.BeginChangeCheck();
                bool newAll = EditorGUILayout.Toggle(M("全グループ"), allGroups);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newAll)
                        _applyGroupMask = null;
                    else
                    {
                        _applyGroupMask = new HashSet<int>();
                        for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
                            _applyGroupMask.Add(g);
                    }
                }
                if (!newAll && _applyGroupMask != null)
                {
                    for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
                    {
                        bool enabled = _applyGroupMask.Contains(g);
                        bool newEnabled = EditorGUILayout.Toggle(
                            BonePoseEditorState.BoneGroups[g].name, enabled);
                        if (newEnabled && !enabled) _applyGroupMask.Add(g);
                        if (!newEnabled && enabled) _applyGroupMask.Remove(g);
                    }
                }
                EditorGUI.indentLevel--;
            }

            // Search bar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("\u2315", GUILayout.Width(14));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18)))
                {
                    _searchFilter = "";
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Category filter buttons
            if (lib.Poses.Count > 0)
            {
                var categories = new HashSet<string>();
                foreach (var p in lib.Poses)
                {
                    if (!string.IsNullOrEmpty(p.Category))
                        categories.Add(p.Category);
                }

                if (categories.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    bool isAll = string.IsNullOrEmpty(_categoryFilter);
                    if (GUILayout.Toggle(isAll, M("全て"), EditorStyles.miniButton,
                            GUILayout.Width(32)) && !isAll)
                        _categoryFilter = "";

                    foreach (var cat in categories.OrderBy(c => c))
                    {
                        bool isSel = _categoryFilter == cat;
                        if (GUILayout.Toggle(isSel, cat, EditorStyles.miniButton) && !isSel)
                            _categoryFilter = cat;
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }

            // View toggle
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            _gridView = GUILayout.Toggle(_gridView, M("グリッド"), EditorStyles.miniButtonLeft,
                GUILayout.Width(36));
            _gridView = !GUILayout.Toggle(!_gridView, M("リスト"), EditorStyles.miniButtonRight,
                GUILayout.Width(36));
            EditorGUILayout.EndHorizontal();

            // Filter poses
            var filteredIndices = GetFilteredIndices(lib);

            // Pose entries
            if (lib.Poses.Count == 0)
            {
                EditorGUILayout.LabelField(M("まだポーズが保存されていません。"),
                    EditorStyles.centeredGreyMiniLabel);
            }
            else if (filteredIndices.Count == 0)
            {
                EditorGUILayout.LabelField(M("一致するポーズがありません。"),
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                _scrollPos = EditorGUILayout.BeginScrollView(
                    _scrollPos, GUILayout.MaxHeight(300));

                int removeIdx = -1;
                if (_gridView)
                    removeIdx = DrawGridView(lib, filteredIndices);
                else
                    removeIdx = DrawListView(lib, filteredIndices);

                if (removeIdx >= 0)
                {
                    Undo.RecordObject(lib, "Delete Pose");
                    if (_thumbCache.ContainsKey(lib.Poses[removeIdx]))
                    {
                        Object.DestroyImmediate(_thumbCache[lib.Poses[removeIdx]]);
                        _thumbCache.Remove(lib.Poses[removeIdx]);
                    }
                    lib.Poses.RemoveAt(removeIdx);
                    EditorUtility.SetDirty(lib);
                    if (_renamingIndex == removeIdx) _renamingIndex = -1;
                }

                EditorGUILayout.EndScrollView();
            }

            // End hover preview if mouse left the scroll area
            EndHoverPreviewIfNeeded();

            EditorGUILayout.EndVertical();
        }

        private static List<int> GetFilteredIndices(PoseLibraryAsset lib)
        {
            var result = new List<int>();
            string searchLower = _searchFilter?.ToLowerInvariant() ?? "";

            for (int i = 0; i < lib.Poses.Count; i++)
            {
                var pose = lib.Poses[i];

                // Category filter
                if (!string.IsNullOrEmpty(_categoryFilter)
                    && pose.Category != _categoryFilter)
                    continue;

                // Search filter
                if (!string.IsNullOrEmpty(searchLower))
                {
                    bool nameMatch = pose.Name != null
                        && pose.Name.ToLowerInvariant().Contains(searchLower);
                    bool catMatch = pose.Category != null
                        && pose.Category.ToLowerInvariant().Contains(searchLower);
                    if (!nameMatch && !catMatch)
                        continue;
                }

                result.Add(i);
            }
            return result;
        }

        private static int DrawGridView(PoseLibraryAsset lib, List<int> indices)
        {
            int removeIdx = -1;
            int cols = Mathf.Max(1,
                (int)((EditorGUIUtility.currentViewWidth - 40) / 155));
            int col = 0;

            EditorGUILayout.BeginHorizontal();
            foreach (int i in indices)
            {
                var pose = lib.Poses[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                    GUILayout.Width(145));

                // Thumbnail (larger: 130x100)
                var thumb = GetThumbnail(pose);
                if (thumb != null)
                {
                    var r = GUILayoutUtility.GetRect(130, 100);
                    GUI.DrawTexture(r, thumb, ScaleMode.ScaleToFit);

                    // Hover preview
                    if (r.Contains(Event.current.mousePosition))
                    {
                        BeginHoverPreview(pose);
                        EditorWindow.focusedWindow?.Repaint();
                    }
                    else if (_previewActive)
                    {
                        // Will be ended by EndHoverPreviewIfNeeded
                    }
                }

                // Name (double-click to rename)
                if (_renamingIndex == i)
                {
                    EditorGUI.BeginChangeCheck();
                    _renamingText = EditorGUILayout.TextField(_renamingText,
                        EditorStyles.miniTextField);
                    if (EditorGUI.EndChangeCheck() || Event.current.isKey
                        && Event.current.keyCode == KeyCode.Return)
                    {
                        if (Event.current.isKey
                            && Event.current.keyCode == KeyCode.Return)
                        {
                            CommitRename(lib, i);
                            Event.current.Use();
                        }
                    }
                    // Click elsewhere to commit
                    if (Event.current.type == EventType.MouseDown
                        && !GUILayoutUtility.GetLastRect().Contains(
                            Event.current.mousePosition))
                    {
                        CommitRename(lib, i);
                    }
                }
                else
                {
                    var nameRect = GUILayoutUtility.GetRect(
                        new GUIContent(pose.Name), EditorStyles.miniLabel);
                    GUI.Label(nameRect, pose.Name, EditorStyles.miniLabel);

                    // Double-click detection
                    if (Event.current.type == EventType.MouseDown
                        && Event.current.clickCount == 2
                        && nameRect.Contains(Event.current.mousePosition))
                    {
                        _renamingIndex = i;
                        _renamingText = pose.Name;
                        Event.current.Use();
                    }
                }

                // Category label
                if (!string.IsNullOrEmpty(pose.Category))
                {
                    var catStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.6f, 0.8f, 1f) },
                        fontSize = 9,
                    };
                    GUILayout.Label(pose.Category, catStyle);
                }

                // Action buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(M("適用"), EditorStyles.miniButton))
                {
                    EndHoverPreview();
                    PoseLibraryOperations.ApplyPose(pose,
                        BonePoseEditorState.BoneTransforms,
                        BonePoseEditorState.PoseBlendWeight,
                        _applyGroupMask);
                    BonePoseEditorState.TryAutoKey();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button(M("反転"), EditorStyles.miniButton))
                {
                    EndHoverPreview();
                    PoseLibraryOperations.ApplyPoseMirrored(pose,
                        BonePoseEditorState.BoneTransforms,
                        BonePoseEditorState.PoseBlendWeight);
                    BonePoseEditorState.TryAutoKey();
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();

                // Retake / Delete row
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(M("再撮影"), EditorStyles.miniButton))
                {
                    Undo.RecordObject(lib, "Retake Thumbnail");
                    pose.ThumbnailPNG = PoseLibraryOperations.GenerateThumbnail(
                        SceneView.lastActiveSceneView);
                    if (_thumbCache.ContainsKey(pose))
                    {
                        Object.DestroyImmediate(_thumbCache[pose]);
                        _thumbCache.Remove(pose);
                    }
                    EditorUtility.SetDirty(lib);
                }
                if (GUILayout.Button(M("削除"), EditorStyles.miniButton))
                    removeIdx = i;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();

                col++;
                if (col >= cols)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();

            return removeIdx;
        }

        private static int DrawListView(PoseLibraryAsset lib, List<int> indices)
        {
            int removeIdx = -1;

            foreach (int i in indices)
            {
                var pose = lib.Poses[i];

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Small thumbnail
                var thumb = GetThumbnail(pose);
                if (thumb != null)
                {
                    var r = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    GUI.DrawTexture(r, thumb, ScaleMode.ScaleToFit);

                    // Hover preview
                    if (r.Contains(Event.current.mousePosition))
                    {
                        BeginHoverPreview(pose);
                        EditorWindow.focusedWindow?.Repaint();
                    }
                }

                // Name and date
                EditorGUILayout.BeginVertical();
                if (_renamingIndex == i)
                {
                    _renamingText = EditorGUILayout.TextField(_renamingText,
                        EditorStyles.miniTextField);
                    if (Event.current.isKey
                        && Event.current.keyCode == KeyCode.Return)
                    {
                        CommitRename(lib, i);
                        Event.current.Use();
                    }
                }
                else
                {
                    var nameRect = GUILayoutUtility.GetRect(
                        new GUIContent(pose.Name), EditorStyles.miniLabel);
                    GUI.Label(nameRect, pose.Name, EditorStyles.miniLabel);

                    if (Event.current.type == EventType.MouseDown
                        && Event.current.clickCount == 2
                        && nameRect.Contains(Event.current.mousePosition))
                    {
                        _renamingIndex = i;
                        _renamingText = pose.Name;
                        Event.current.Use();
                    }
                }

                // Date + Category
                string info = pose.CreatedDate;
                if (!string.IsNullOrEmpty(pose.Category))
                    info += $" [{pose.Category}]";
                GUILayout.Label(info, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(M("再撮影"), EditorStyles.miniButton,
                        GUILayout.Width(44)))
                {
                    Undo.RecordObject(lib, "Retake Thumbnail");
                    pose.ThumbnailPNG = PoseLibraryOperations.GenerateThumbnail(
                        SceneView.lastActiveSceneView);
                    if (_thumbCache.ContainsKey(pose))
                    {
                        Object.DestroyImmediate(_thumbCache[pose]);
                        _thumbCache.Remove(pose);
                    }
                    EditorUtility.SetDirty(lib);
                }
                if (GUILayout.Button(M("適用"), EditorStyles.miniButton,
                        GUILayout.Width(40)))
                {
                    EndHoverPreview();
                    PoseLibraryOperations.ApplyPose(pose,
                        BonePoseEditorState.BoneTransforms,
                        BonePoseEditorState.PoseBlendWeight,
                        _applyGroupMask);
                    BonePoseEditorState.TryAutoKey();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button(M("反転"), EditorStyles.miniButton,
                        GUILayout.Width(30)))
                {
                    EndHoverPreview();
                    PoseLibraryOperations.ApplyPoseMirrored(pose,
                        BonePoseEditorState.BoneTransforms,
                        BonePoseEditorState.PoseBlendWeight);
                    BonePoseEditorState.TryAutoKey();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("X", EditorStyles.miniButton,
                        GUILayout.Width(20)))
                    removeIdx = i;

                EditorGUILayout.EndHorizontal();
            }

            return removeIdx;
        }

        // ─── Hover Preview ───

        private static void BeginHoverPreview(PoseEntry pose)
        {
            if (_previewActive) return;

            // Save current rotations
            _savedPreviewRotations = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var kvp in BonePoseEditorState.BoneTransforms)
                _savedPreviewRotations[kvp.Key] = kvp.Value.localRotation;

            // Apply pose temporarily
            PoseLibraryOperations.ApplyPose(pose,
                BonePoseEditorState.BoneTransforms, 1f);
            SceneView.RepaintAll();

            _previewActive = true;
        }

        private static void EndHoverPreview()
        {
            if (!_previewActive || _savedPreviewRotations == null) return;

            // Restore original rotations
            foreach (var kvp in _savedPreviewRotations)
            {
                if (BonePoseEditorState.BoneTransforms.TryGetValue(kvp.Key, out var t))
                    t.localRotation = kvp.Value;
            }
            SceneView.RepaintAll();

            _savedPreviewRotations = null;
            _previewActive = false;
        }

        private static void EndHoverPreviewIfNeeded()
        {
            if (!_previewActive) return;

            // Check if mouse is still inside the scroll area
            // If a repaint occurs without hover being re-triggered, end preview
            if (Event.current.type == EventType.Repaint)
            {
                // Schedule end on next non-hover frame
                EditorApplication.delayCall += () =>
                {
                    if (_previewActive)
                        EndHoverPreview();
                };
            }
        }

        // ─── Rename ───

        private static void CommitRename(PoseLibraryAsset lib, int index)
        {
            if (index < 0 || index >= lib.Poses.Count) return;
            if (!string.IsNullOrEmpty(_renamingText))
            {
                Undo.RecordObject(lib, "Rename Pose");
                lib.Poses[index].Name = _renamingText;
                EditorUtility.SetDirty(lib);
            }
            _renamingIndex = -1;
            _renamingText = "";
            GUI.FocusControl(null);
        }

        // ─── Helpers ───

        private static Texture2D GetThumbnail(PoseEntry entry)
        {
            if (entry.ThumbnailPNG == null || entry.ThumbnailPNG.Length == 0)
                return null;

            if (_thumbCache.TryGetValue(entry, out var cached) && cached != null)
                return cached;

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(entry.ThumbnailPNG))
            {
                _thumbCache[entry] = tex;
                return tex;
            }

            Object.DestroyImmediate(tex);
            return null;
        }

        private static void CreateNewLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                M("ポーズライブラリ作成"), "PoseLibrary", "asset",
                M("保存先を選択"));
            if (string.IsNullOrEmpty(path)) return;

            var lib = ScriptableObject.CreateInstance<PoseLibraryAsset>();
            AssetDatabase.CreateAsset(lib, path);
            AssetDatabase.SaveAssets();
            BonePoseEditorState.CurrentLibrary = lib;
            EditorGUIUtility.PingObject(lib);
        }
    }
}
