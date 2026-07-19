using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class SkillManagementWindow : EditorWindow
    {
        private enum SkillSource { BuiltIn, Custom, Override }

        private struct SkillEntry
        {
            public string name;
            public string title;
            public string description;
            public string tags;
            public string content;
            public SkillSource source;
        }

        // Cache
        private List<SkillEntry> _allSkills;
        private List<SkillEntry> _filteredSkills;

        // UI state
        private Vector2 _listScrollPos;
        private Vector2 _previewScrollPos;
        private string _searchText = "";
        private string _prevSearchText;
        private int _selectedIndex = -1;

        // Create skill form
        private bool _showCreateForm;
        private string _newSkillName = "";
        private string _newSkillTitle = "";
        private string _newSkillDescription = "";
        private string _newSkillTags = "";
        private HashSet<string> _selectedTags = new HashSet<string>();
        private string _customTagInput = "";

        // Import-from-URL form
        private bool _showImportForm;
        private string _importUrl = "";
        private string _importName = "";
        private string _importedContent;
        private bool _importFetched;
        private bool _importing;
        private string _importStatus = "";
        private UnityWebRequest _importRequest;

        // Tag palette (collected from all skills)
        private List<string> _knownTags;

        // Styles (lazy init)
        private GUIStyle _descStyle;
        private GUIStyle _previewStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _sourceBuiltInStyle;
        private GUIStyle _sourceCustomStyle;
        private GUIStyle _sourceOverrideStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _btnLeftStyle;
        private GUIStyle _btnRightStyle;

        // Colors
        private static readonly Color EnabledColor = new Color(0.4f, 0.85f, 0.4f);
        private static readonly Color DisabledColor = new Color(0.85f, 0.4f, 0.4f);
        private static readonly Color BuiltInColor = new Color(0.5f, 0.7f, 1f);
        private static readonly Color CustomColor = new Color(0.5f, 1f, 0.6f);
        private static readonly Color OverrideColor = new Color(1f, 0.8f, 0.4f);

        [MenuItem("UnityAgent/Skill Management")]
        public static void Open()
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
            var window = GetWindow<SkillManagementWindow>();
            window.titleContent = new GUIContent(M("スキル管理"));
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnEnable()
        {
            RebuildCache();
        }

        private void OnFocus()
        {
            // Refresh when the window regains focus (user may have edited files externally)
            RebuildCache();
        }

        private void OnDisable()
        {
            CancelImport();
        }

        /// <summary>UserSkillsPath → SkillsPath の順でカスタムファイルのパスを返す。</summary>
        private static string ResolveCustomSkillPath(string skillName)
        {
            string userFile = Path.Combine(SkillTools.UserSkillsPath, skillName + ".md");
            if (File.Exists(userFile)) return userFile;
            string pkgFile = Path.Combine(SkillTools.SkillsPath, skillName + ".md");
            if (File.Exists(pkgFile)) return pkgFile;
            return null;
        }

        private void RebuildCache()
        {
            var allSkills = SkillTools.GetAllSkills();
            _allSkills = new List<SkillEntry>(allSkills.Count);

            foreach (var kv in allSkills.OrderBy(k => k.Key))
            {
                var meta = SkillTools.ParseFrontMatter(kv.Value);
                bool isBuiltIn = SkillTools.IsBuiltIn(kv.Key);
                bool hasFile = SkillTools.HasCustomFile(kv.Key);

                SkillSource source;
                if (isBuiltIn && hasFile)
                    source = SkillSource.Override;
                else if (isBuiltIn)
                    source = SkillSource.BuiltIn;
                else
                    source = SkillSource.Custom;

                _allSkills.Add(new SkillEntry
                {
                    name = kv.Key,
                    title = meta.ContainsKey("title") ? meta["title"] : kv.Key,
                    description = meta.ContainsKey("description") ? meta["description"] : "",
                    tags = meta.ContainsKey("tags") ? meta["tags"] : "",
                    content = kv.Value,
                    source = source,
                });
            }

            // Collect all known tags from existing skills
            var tagSet = new HashSet<string>();
            foreach (var s in _allSkills)
            {
                if (string.IsNullOrEmpty(s.tags)) continue;
                foreach (var t in s.tags.Split(','))
                {
                    string trimmed = t.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        tagSet.Add(trimmed);
                }
            }
            _knownTags = tagSet.OrderBy(t => t).ToList();

            _prevSearchText = null; // force filter rebuild
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_searchText == _prevSearchText && _filteredSkills != null) return;
            _prevSearchText = _searchText;

            if (string.IsNullOrEmpty(_searchText))
            {
                _filteredSkills = new List<SkillEntry>(_allSkills);
            }
            else
            {
                string kw = _searchText.ToLower();
                _filteredSkills = _allSkills
                    .Where(s => s.name.ToLower().Contains(kw)
                             || s.title.ToLower().Contains(kw)
                             || s.description.ToLower().Contains(kw)
                             || s.tags.ToLower().Contains(kw))
                    .ToList();
            }

            // Keep selection valid
            if (_selectedIndex >= _filteredSkills.Count)
                _selectedIndex = _filteredSkills.Count - 1;
        }

        private void InitStyles()
        {
            if (_descStyle != null) return;

            _descStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = true
            };
            _descStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.65f, 0.65f, 0.65f)
                : new Color(0.35f, 0.35f, 0.35f);

            _previewStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = false,
                fontSize = 11
            };
            _previewStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.78f, 0.78f, 0.78f)
                : new Color(0.2f, 0.2f, 0.2f);

            _tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Italic
            };
            _tagStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.55f, 0.55f, 0.55f)
                : new Color(0.45f, 0.45f, 0.45f);

            _sourceBuiltInStyle = CreateSourceStyle(BuiltInColor);
            _sourceCustomStyle = CreateSourceStyle(CustomColor);
            _sourceOverrideStyle = CreateSourceStyle(OverrideColor);

            _btnStyle = new GUIStyle(EditorStyles.miniButton) { fixedHeight = 0 };
            _btnLeftStyle = new GUIStyle(EditorStyles.miniButtonLeft) { fixedHeight = 0 };
            _btnRightStyle = new GUIStyle(EditorStyles.miniButtonRight) { fixedHeight = 0 };
        }

        private GUIStyle CreateSourceStyle(Color color)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9
            };
            style.normal.textColor = color;
            return style;
        }

        // ═══════════════════════════════════════════════════════════════
        // OnGUI
        // ═══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();
            if (_allSkills == null) RebuildCache();

            DrawHeader();
            EditorGUILayout.Space(4);

            // Main content: split list (left) + preview (right)
            EditorGUILayout.BeginHorizontal();
            {
                // Left: skill list
                EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.45f), GUILayout.MinWidth(220));
                DrawSkillList();
                EditorGUILayout.EndVertical();

                // Right: preview / create form / import form
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (_showImportForm)
                    DrawImportForm();
                else if (_showCreateForm)
                    DrawCreateForm();
                else
                    DrawPreviewPanel();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Header ───

        private void DrawHeader()
        {
            var disabled = AgentSettings.GetDisabledSkills();
            int total = _allSkills.Count;
            int enabledCount = _allSkills.Count(s => !disabled.Contains(s.name));

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(M("スキル管理"), EditorStyles.boldLabel, GUILayout.Width(80));

            // Summary
            GUILayout.Label(string.Format(M("{0}/{1} 有効"), enabledCount, total), _descStyle, GUILayout.Width(70));

            GUILayout.FlexibleSpace();

            // Buttons
            if (GUILayout.Button(M("全て有効"), _btnLeftStyle, GUILayout.Width(80), GUILayout.Height(24)))
            {
                AgentSettings.SetDisabledSkills(new HashSet<string>());
                Repaint();
            }
            if (GUILayout.Button(M("全て無効"), _btnRightStyle, GUILayout.Width(80), GUILayout.Height(24)))
            {
                AgentSettings.SetDisabledSkills(new HashSet<string>(_allSkills.Select(s => s.name)));
                Repaint();
            }

            GUILayout.Space(8);

            if (GUILayout.Button(M("ツール一覧"), _btnStyle, GUILayout.Width(90), GUILayout.Height(24)))
            {
                ToolConsoleWindow.Open();
            }

            if (GUILayout.Button(M("フォルダを開く"), _btnStyle, GUILayout.Width(110), GUILayout.Height(24)))
            {
                if (!Directory.Exists(SkillTools.UserSkillsPath))
                    Directory.CreateDirectory(SkillTools.UserSkillsPath);
                EditorUtility.RevealInFinder(SkillTools.UserSkillsPath);
            }

            if (GUILayout.Button(M("URLから取込"), _btnStyle, GUILayout.Width(100), GUILayout.Height(24)))
            {
                OpenImportForm();
            }

            if (GUILayout.Button(M("更新"), _btnStyle, GUILayout.Width(60), GUILayout.Height(24)))
            {
                RebuildCache();
            }

            EditorGUILayout.EndHorizontal();

            // Search bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(M("検索:"), GUILayout.Width(36));
            string newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                ApplyFilter();
            }
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
            {
                _searchText = "";
                ApplyFilter();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Skill List ───

        private void DrawSkillList()
        {
            var disabled = AgentSettings.GetDisabledSkills();

            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos);

            for (int i = 0; i < _filteredSkills.Count; i++)
            {
                var skill = _filteredSkills[i];
                bool isDisabled = disabled.Contains(skill.name);
                bool isSelected = (i == _selectedIndex);

                // Row background
                var rowRect = EditorGUILayout.BeginHorizontal(
                    isSelected ? "SelectionRect" : EditorStyles.helpBox,
                    GUILayout.Height(40));

                // Click to select
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    _showCreateForm = false;
                    _showImportForm = false;
                    Event.current.Use();
                    Repaint();
                }

                // Enable/Disable toggle
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isDisabled ? DisabledColor : EnabledColor;
                bool newEnabled = GUILayout.Toggle(!isDisabled, "", GUILayout.Width(18), GUILayout.Height(18));
                GUI.backgroundColor = prevBg;
                if (newEnabled == isDisabled) // toggled
                {
                    AgentSettings.SetSkillDisabled(skill.name, !isDisabled);
                    Repaint();
                }

                // Skill info
                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.BeginHorizontal();

                    // Title
                    var titleStyle = isDisabled
                        ? new GUIStyle(EditorStyles.label) { normal = { textColor = Color.gray } }
                        : EditorStyles.label;
                    GUILayout.Label(skill.title, titleStyle);

                    GUILayout.FlexibleSpace();

                    // Source badge
                    string sourceName;
                    GUIStyle sourceStyle;
                    switch (skill.source)
                    {
                        case SkillSource.BuiltIn:
                            sourceName = M("組込");
                            sourceStyle = _sourceBuiltInStyle;
                            break;
                        case SkillSource.Custom:
                            sourceName = M("カスタム");
                            sourceStyle = _sourceCustomStyle;
                            break;
                        default:
                            sourceName = M("上書き");
                            sourceStyle = _sourceOverrideStyle;
                            break;
                    }
                    GUILayout.Label(sourceName, sourceStyle, GUILayout.Width(50));

                    EditorGUILayout.EndHorizontal();

                    // Description (one line)
                    if (!string.IsNullOrEmpty(skill.description))
                    {
                        string desc = skill.description;
                        if (desc.Length > 60) desc = desc.Substring(0, 57) + "...";
                        GUILayout.Label(desc, _descStyle);
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);

            // Create new skill button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ " + M("新規スキル作成"), GUILayout.Width(140), GUILayout.Height(28)))
            {
                _showCreateForm = true;
                _showImportForm = false;
                _selectedIndex = -1;
                _newSkillName = "";
                _newSkillTitle = "";
                _newSkillDescription = "";
                _newSkillTags = "";
                _selectedTags.Clear();
                _customTagInput = "";
            }
            if (GUILayout.Button("+ " + M("フローチャートで新規"), GUILayout.Width(180), GUILayout.Height(28)))
            {
                Flowchart.UI.FlowchartEditorWindow.OpenNew();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // ─── Preview Panel ───

        private void DrawPreviewPanel()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredSkills.Count)
            {
                EditorGUILayout.Space(20);
                GUILayout.Label(M("左のリストからスキルを選択してください"), EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var skill = _filteredSkills[_selectedIndex];
            bool isDisabled = AgentSettings.IsSkillDisabled(skill.name);

            // Title bar
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(skill.title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // Status badge
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isDisabled ? DisabledColor : EnabledColor;
            string statusLabel = isDisabled ? M("無効") : M("有効");
            if (GUILayout.Button(statusLabel, EditorStyles.miniButton, GUILayout.Width(40)))
            {
                AgentSettings.SetSkillDisabled(skill.name, !isDisabled);
                Repaint();
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            // Metadata
            EditorGUILayout.LabelField(string.Format(M("ID: {0}"), skill.name), _descStyle);
            if (!string.IsNullOrEmpty(skill.tags))
                EditorGUILayout.LabelField(string.Format(M("タグ: {0}"), skill.tags), _tagStyle);

            // Source info
            EditorGUILayout.BeginHorizontal();
            switch (skill.source)
            {
                case SkillSource.BuiltIn:
                    EditorGUILayout.LabelField(M("ソース: 組込 (DLL内蔵)"), _descStyle);
                    break;
                case SkillSource.Custom:
                    EditorGUILayout.LabelField(M("ソース: カスタム (ファイル)"), _descStyle);
                    break;
                case SkillSource.Override:
                    EditorGUILayout.LabelField(M("ソース: カスタム上書き (組込を上書き中)"), _descStyle);
                    break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Action buttons
            EditorGUILayout.BeginHorizontal();
            {
                bool hasFile = SkillTools.HasCustomFile(skill.name);

                if (hasFile)
                {
                    if (GUILayout.Button(M("ファイルを編集"), _btnLeftStyle, GUILayout.Height(24)))
                    {
                        string path = ResolveCustomSkillPath(skill.name);
                        UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
                    }
                }

                if (skill.source == SkillSource.Custom)
                {
                    if (GUILayout.Button(M("削除"), _btnRightStyle, GUILayout.Height(24)))
                    {
                        if (EditorUtility.DisplayDialog(M("スキル削除"),
                            string.Format(M("カスタムスキル '{0}' を削除しますか？"), skill.name), M("削除"), M("キャンセル")))
                        {
                            string path = ResolveCustomSkillPath(skill.name);
                            if (path != null && File.Exists(path)) File.Delete(path);
                            RebuildCache();
                            _selectedIndex = -1;
                            Repaint();
                            return;
                        }
                    }
                }
                else if (skill.source == SkillSource.Override)
                {
                    if (GUILayout.Button(M("上書き解除"), _btnRightStyle, GUILayout.Height(24)))
                    {
                        if (EditorUtility.DisplayDialog(M("上書き解除"),
                            M("カスタムファイルを削除し、組込版に戻しますか？"), M("戻す"), M("キャンセル")))
                        {
                            string path = ResolveCustomSkillPath(skill.name);
                            if (path != null && File.Exists(path)) File.Delete(path);
                            RebuildCache();
                            Repaint();
                            return;
                        }
                    }
                }

                if (skill.source == SkillSource.BuiltIn)
                {
                    if (GUILayout.Button(M("カスタム版を作成"), _btnStyle, GUILayout.Height(24)))
                    {
                        if (!Directory.Exists(SkillTools.UserSkillsPath))
                            Directory.CreateDirectory(SkillTools.UserSkillsPath);
                        string path = Path.Combine(SkillTools.UserSkillsPath, skill.name + ".md");
                        File.WriteAllText(path, skill.content);
                        RebuildCache();
                        Repaint();
                        EditorUtility.DisplayDialog(M("エクスポート完了"),
                            string.Format(M("組込スキルをファイルに書き出しました:\n{0}\n\nファイルを編集すると組込版を上書きします。"), path), "OK");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Separator
            EditorGUILayout.Space(4);
            var sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, EditorGUIUtility.isProSkin
                ? new Color(1, 1, 1, 0.1f) : new Color(0, 0, 0, 0.1f));
            EditorGUILayout.Space(4);

            // Preview content
            string body = SkillTools.StripFrontMatter(skill.content);
            _previewScrollPos = EditorGUILayout.BeginScrollView(_previewScrollPos);
            EditorGUILayout.LabelField(body, _previewStyle);
            EditorGUILayout.EndScrollView();
        }

        // ─── Create Form ───

        private void DrawCreateForm()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(M("新規スキル作成"), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUIUtility.labelWidth = 80;

            _newSkillName = EditorGUILayout.TextField(M("ID (kebab)"), _newSkillName);
            EditorGUILayout.HelpBox(M("半角英数字とハイフンのみ (例: my-new-skill)"), MessageType.None);

            _newSkillTitle = EditorGUILayout.TextField(M("タイトル"), _newSkillTitle);
            _newSkillDescription = EditorGUILayout.TextField(M("説明"), _newSkillDescription);

            // ─── Tag picker ───
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(M("タグ"), EditorStyles.boldLabel);

            // Selected tags as removable chips
            if (_selectedTags.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                float rowWidth = 0f;
                float maxWidth = position.width * 0.5f - 30f;
                foreach (var tag in _selectedTags.OrderBy(t => t).ToList())
                {
                    float chipWidth = GUI.skin.button.CalcSize(new GUIContent($" {tag} x ")).x;
                    if (rowWidth + chipWidth > maxWidth && rowWidth > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        rowWidth = 0f;
                    }
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                    if (GUILayout.Button($" {tag} \u00d7 ", EditorStyles.miniButton, GUILayout.Height(20)))
                    {
                        _selectedTags.Remove(tag);
                        SyncTagsToString();
                    }
                    GUI.backgroundColor = prevBg;
                    rowWidth += chipWidth;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            // Known tags as toggle buttons
            if (_knownTags != null && _knownTags.Count > 0)
            {
                EditorGUILayout.LabelField(M("既存タグから選択:"), _descStyle);
                EditorGUILayout.BeginHorizontal();
                float rowWidth = 0f;
                float maxWidth = position.width * 0.5f - 30f;
                foreach (var tag in _knownTags)
                {
                    bool isOn = _selectedTags.Contains(tag);
                    float btnWidth = GUI.skin.button.CalcSize(new GUIContent(tag)).x + 8;
                    if (rowWidth + btnWidth > maxWidth && rowWidth > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        rowWidth = 0f;
                    }
                    var prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = isOn ? new Color(0.3f, 0.8f, 0.5f) : Color.gray;
                    bool newOn = GUILayout.Toggle(isOn, tag, EditorStyles.miniButton, GUILayout.Height(18));
                    GUI.backgroundColor = prevBg;
                    if (newOn != isOn)
                    {
                        if (newOn) _selectedTags.Add(tag);
                        else _selectedTags.Remove(tag);
                        SyncTagsToString();
                    }
                    rowWidth += btnWidth;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            // Custom tag input
            EditorGUILayout.BeginHorizontal();
            _customTagInput = EditorGUILayout.TextField(_customTagInput, GUILayout.Height(20));
            bool canAdd = !string.IsNullOrWhiteSpace(_customTagInput)
                       && !_selectedTags.Contains(_customTagInput.Trim());
            GUI.enabled = canAdd;
            if (GUILayout.Button(M("追加"), EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(20))
                || (canAdd && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
            {
                _selectedTags.Add(_customTagInput.Trim());
                SyncTagsToString();
                _customTagInput = "";
                GUI.FocusControl(null);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(M("キャンセル"), GUILayout.Height(28)))
                {
                    _showCreateForm = false;
                }

                GUI.enabled = !string.IsNullOrEmpty(_newSkillName) && !string.IsNullOrEmpty(_newSkillTitle);
                if (GUILayout.Button(M("テンプレートから作成"), GUILayout.Height(28)))
                {
                    CreateSkillFromTemplate();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            // Validation hints
            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(_newSkillName))
            {
                if (_newSkillName.Contains(' '))
                    EditorGUILayout.HelpBox(M("スペースは使用できません。ハイフン(-) を使ってください。"), MessageType.Warning);
                else if (_newSkillName.StartsWith("_"))
                    EditorGUILayout.HelpBox(M("アンダースコアで始まる名前は予約されています。"), MessageType.Warning);
                else if (_allSkills.Any(s => s.name == _newSkillName))
                    EditorGUILayout.HelpBox(string.Format(M("'{0}' は既に存在します。"), _newSkillName), MessageType.Warning);
            }
        }

        private void SyncTagsToString()
        {
            _newSkillTags = string.Join(", ", _selectedTags.OrderBy(t => t));
        }

        private void CreateSkillFromTemplate()
        {
            string name = _newSkillName.Trim();

            // Validate
            if (name.Contains(' ') || name.StartsWith("_"))
            {
                EditorUtility.DisplayDialog(M("エラー"), M("無効なスキル名です。"), "OK");
                return;
            }

            if (!Directory.Exists(SkillTools.UserSkillsPath))
                Directory.CreateDirectory(SkillTools.UserSkillsPath);

            string filePath = Path.Combine(SkillTools.UserSkillsPath, name + ".md");
            if (File.Exists(filePath))
            {
                EditorUtility.DisplayDialog(M("エラー"), string.Format(M("'{0}' は既に存在します。"), name), "OK");
                return;
            }

            // Build content from shared template
            string content = SkillTools.BuildNewSkillFromTemplate(
                _newSkillTitle, _newSkillDescription, _newSkillTags);
            File.WriteAllText(filePath, content);

            // Open in editor
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(filePath, 1);

            _showCreateForm = false;
            RebuildCache();

            // Select the new skill
            for (int i = 0; i < _filteredSkills.Count; i++)
            {
                if (_filteredSkills[i].name == name)
                {
                    _selectedIndex = i;
                    break;
                }
            }

            Repaint();
        }

        // ─── Import from URL ───

        private void OpenImportForm()
        {
            _showImportForm = true;
            _showCreateForm = false;
            _selectedIndex = -1;
            _importUrl = "";
            _importName = "";
            _importedContent = null;
            _importFetched = false;
            _importStatus = "";
            CancelImport();
        }

        private void DrawImportForm()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(M("URL からスキルを取り込み"), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                M("GitHub の raw .md URL などを指定してください。blob URL は自動で raw に変換します。\nスキルは AI への指示文です。信頼できる提供元のみ取り込んでください。"),
                MessageType.Info);

            EditorGUIUtility.labelWidth = 40;
            EditorGUI.BeginDisabledGroup(_importing);
            _importUrl = EditorGUILayout.TextField("URL", _importUrl);
            EditorGUI.EndDisabledGroup();
            EditorGUIUtility.labelWidth = 0;

            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = !_importing && !string.IsNullOrWhiteSpace(_importUrl);
                if (GUILayout.Button(_importing ? M("取得中...") : M("取得"), GUILayout.Height(24)))
                {
                    StartImport();
                }
                GUI.enabled = true;

                if (GUILayout.Button(M("キャンセル"), GUILayout.Height(24)))
                {
                    CancelImport();
                    _showImportForm = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_importStatus))
                EditorGUILayout.HelpBox(_importStatus, _importFetched ? MessageType.Info : MessageType.Warning);

            if (!_importFetched || _importedContent == null)
                return;

            // ─── Preview + save ───
            EditorGUILayout.Space(4);
            var sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, EditorGUIUtility.isProSkin
                ? new Color(1, 1, 1, 0.1f) : new Color(0, 0, 0, 0.1f));
            EditorGUILayout.Space(4);

            var meta = SkillTools.ParseFrontMatter(_importedContent);
            string title = meta.ContainsKey("title") ? meta["title"] : "";
            string desc = meta.ContainsKey("description") ? meta["description"] : "";
            string tags = meta.ContainsKey("tags") ? meta["tags"] : "";

            EditorGUIUtility.labelWidth = 80;
            _importName = EditorGUILayout.TextField(M("ID (kebab)"), _importName);
            EditorGUIUtility.labelWidth = 0;

            EditorGUILayout.LabelField(
                string.Format(M("タイトル: {0}"), string.IsNullOrEmpty(title) ? M("(なし)") : title), _descStyle);
            if (!string.IsNullOrEmpty(desc))
                EditorGUILayout.LabelField(string.Format(M("説明: {0}"), desc), _descStyle);
            if (!string.IsNullOrEmpty(tags))
                EditorGUILayout.LabelField(string.Format(M("タグ: {0}"), tags), _tagStyle);

            // Validation
            string nameError = ValidateImportName(_importName);
            if (nameError != null)
                EditorGUILayout.HelpBox(nameError, MessageType.Warning);
            else if (SkillExists(_importName.Trim()))
                EditorGUILayout.HelpBox(
                    string.Format(M("'{0}' は既に存在します。保存すると上書きします。"), _importName.Trim()),
                    MessageType.Warning);
            if (string.IsNullOrEmpty(title))
                EditorGUILayout.HelpBox(M("frontmatter に title がありません。取り込みは可能ですが推奨されません。"), MessageType.Warning);

            // Body preview
            EditorGUILayout.Space(2);
            string body = SkillTools.StripFrontMatter(_importedContent);
            _previewScrollPos = EditorGUILayout.BeginScrollView(_previewScrollPos, GUILayout.MinHeight(100));
            EditorGUILayout.LabelField(body, _previewStyle);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            GUI.enabled = nameError == null;
            if (GUILayout.Button(M("保存"), GUILayout.Height(28)))
            {
                SaveImportedSkill();
            }
            GUI.enabled = true;
        }

        private void StartImport()
        {
            CancelImport();
            _importStatus = "";
            _importFetched = false;
            _importedContent = null;

            string url = NormalizeToRawUrl(_importUrl.Trim());
            if (!(url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                  || url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)))
            {
                _importStatus = M("http/https の URL を指定してください。");
                return;
            }

            _importUrl = url; // reflect normalization back into the field
            _importRequest = UnityWebRequest.Get(url);
            _importRequest.SendWebRequest();
            _importing = true;
            _importStatus = M("取得中...");
            EditorApplication.update += PollImport;
        }

        private void PollImport()
        {
            if (_importRequest == null)
            {
                EditorApplication.update -= PollImport;
                return;
            }
            if (!_importRequest.isDone) return;

            EditorApplication.update -= PollImport;
            _importing = false;

            if (_importRequest.result != UnityWebRequest.Result.Success)
            {
                _importStatus = string.Format(M("取得に失敗しました: {0}"), _importRequest.error);
                _importRequest.Dispose();
                _importRequest = null;
                Repaint();
                return;
            }

            string content = _importRequest.downloadHandler.text;
            string finalUrl = _importRequest.url;
            _importRequest.Dispose();
            _importRequest = null;

            if (string.IsNullOrWhiteSpace(content))
            {
                _importStatus = M("取得したファイルが空です。");
                Repaint();
                return;
            }

            string head = content.TrimStart();
            if (head.StartsWith("<!DOCTYPE", System.StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<html", System.StringComparison.OrdinalIgnoreCase))
            {
                _importStatus = M("HTML が返されました。raw の .md URL を指定してください。");
                Repaint();
                return;
            }

            _importedContent = content;
            _importFetched = true;
            if (string.IsNullOrEmpty(_importName))
                _importName = DeriveSkillNameFromUrl(finalUrl);
            _importStatus = M("取得しました。ID と内容を確認して保存してください。");
            Repaint();
        }

        private void CancelImport()
        {
            if (_importRequest != null)
            {
                EditorApplication.update -= PollImport;
                try { _importRequest.Dispose(); } catch { /* already disposed */ }
                _importRequest = null;
            }
            _importing = false;
        }

        private void SaveImportedSkill()
        {
            string name = _importName.Trim();
            if (ValidateImportName(name) != null) return;

            if (SkillExists(name))
            {
                if (!EditorUtility.DisplayDialog(M("上書き確認"),
                    string.Format(M("スキル '{0}' は既に存在します。上書きしますか？"), name),
                    M("上書き"), M("キャンセル")))
                    return;
            }

            if (!Directory.Exists(SkillTools.UserSkillsPath))
                Directory.CreateDirectory(SkillTools.UserSkillsPath);

            string filePath = Path.Combine(SkillTools.UserSkillsPath, name + ".md");
            File.WriteAllText(filePath, _importedContent);
            AssetDatabase.Refresh();

            _showImportForm = false;
            CancelImport();
            RebuildCache();

            // Select the imported skill
            for (int i = 0; i < _filteredSkills.Count; i++)
            {
                if (_filteredSkills[i].name == name)
                {
                    _selectedIndex = i;
                    break;
                }
            }

            Repaint();
            EditorUtility.DisplayDialog(M("取り込み完了"),
                string.Format(M("スキル '{0}' を取り込みました。"), name), "OK");
        }

        private static bool SkillExists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return File.Exists(Path.Combine(SkillTools.UserSkillsPath, name + ".md"))
                || File.Exists(Path.Combine(SkillTools.SkillsPath, name + ".md"))
                || SkillTools.IsBuiltIn(name);
        }

        private string ValidateImportName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return M("ID を入力してください。");
            name = name.Trim();
            if (name.Contains(' '))
                return M("スペースは使用できません。ハイフン(-) を使ってください。");
            if (name.StartsWith("_"))
                return M("アンダースコアで始まる名前は予約されています。");
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (name.IndexOf(c) >= 0)
                    return string.Format(M("無効な文字 '{0}' が含まれています。"), c);
            }
            return null;
        }

        /// <summary>GitHub の blob URL を raw URL に変換する（それ以外はそのまま返す）。</summary>
        private static string NormalizeToRawUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            const string ghHost = "://github.com/";
            int hostIdx = url.IndexOf(ghHost, System.StringComparison.OrdinalIgnoreCase);
            if (hostIdx >= 0)
            {
                string rest = url.Substring(hostIdx + ghHost.Length); // {owner}/{repo}/blob/{branch}/{path}
                int blobIdx = rest.IndexOf("/blob/", System.StringComparison.OrdinalIgnoreCase);
                if (blobIdx >= 0)
                {
                    string repoPart = rest.Substring(0, blobIdx);
                    string pathPart = rest.Substring(blobIdx + "/blob/".Length);
                    return "https://raw.githubusercontent.com/" + repoPart + "/" + pathPart;
                }
            }
            return url;
        }

        private static string DeriveSkillNameFromUrl(string url)
        {
            try
            {
                var uri = new System.Uri(url);
                string file = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                return SanitizeSkillName(file);
            }
            catch
            {
                return "";
            }
        }

        private static string SanitizeSkillName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ')
                    sb.Append('-');
            }
            return sb.ToString().Trim('-', '_');
        }
    }
}
