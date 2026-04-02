using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.SDK;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class ToolConfirmSettingsWindow : EditorWindow
    {
        // Cached tool info (built once, never changes during session)
        private struct ToolInfo
        {
            public string name;
            public string description;
            public string briefDescription;
            public string category;
            public ToolRisk risk;
            public bool isExternal;
            public string author;
            public string version;
        }

        private struct CategoryInfo
        {
            public string key;
            public List<ToolInfo> tools;
        }

        private List<ToolInfo> _allTools;
        private List<CategoryInfo> _categories;
        private List<ToolInfo> _dangerousTools;
        private List<ToolInfo> _cautionTools;
        private List<ToolInfo> _safeTools;

        // UI state
        private Vector2 _scrollPos;
        private string _searchText = "";
        private string _prevSearchText;
        private List<CategoryInfo> _filteredCategories;
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();
        private bool _showHelp = true;
        private GUIStyle _helpStyle;
        private GUIStyle _barLabelStyle;
        private GUIStyle _barLabelStyleRight;
        private GUIStyle _overviewLabelStyle;
        private GUIStyle _disabledLabelStyle;

        // Risk level colors
        private static readonly Color DangerousColor = new Color(1f, 0.35f, 0.35f);
        private static readonly Color CautionColor = new Color(1f, 0.75f, 0.25f);
        private static readonly Color SafeColor = new Color(0.4f, 0.85f, 0.4f);

        // Bar background (empty portion)
        private static readonly Color BarBgDark = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color BarBgLight = new Color(0.78f, 0.78f, 0.78f);

        // Danger background tints (subtle)
        private static readonly Color DangerousBg = new Color(1f, 0.2f, 0.2f, 0.08f);
        private static readonly Color CautionBg = new Color(1f, 0.7f, 0.1f, 0.06f);

        // Disabled tool background tint
        private static readonly Color DisabledBg = new Color(0.5f, 0.5f, 0.5f, 0.1f);

        // Separator color (cached, used for column header line)
        private static readonly Color SepColorDark = new Color(1, 1, 1, 0.05f);
        private static readonly Color SepColorLight = new Color(0, 0, 0, 0.08f);

        // Zebra stripe for alternating tool rows
        private static readonly Color AltRowDark = new Color(1, 1, 1, 0.03f);
        private static readonly Color AltRowLight = new Color(0, 0, 0, 0.04f);

        // Category header background
        private static readonly Color CategoryBgDark = new Color(1, 1, 1, 0.04f);
        private static readonly Color CategoryBgLight = new Color(0, 0, 0, 0.05f);

        public static void Open()
        {
            var window = GetWindow<ToolConfirmSettingsWindow>();
            window.titleContent = new GUIContent(M("安全確認設定"));
            window.minSize = new Vector2(420, 400);
            window.Show();
        }

        private void OnEnable()
        {
            BuildToolCache();
        }

        private void BuildToolCache()
        {
            var registryTools = ToolRegistry.GetAllTools();

            _allTools = new List<ToolInfo>(registryTools.Count);
            foreach (var t in registryTools)
            {
                string desc = t.attribute?.Description ?? "";
                string category = t.attribute?.Category;
                if (string.IsNullOrEmpty(category))
                    category = t.method.DeclaringType.Name.Replace("Tools", "");
                if (t.isExternal)
                    category = M("[外部] ") + category;

                _allTools.Add(new ToolInfo
                {
                    name = t.method.Name,
                    description = desc,
                    briefDescription = GetBriefDescription(desc),
                    category = category,
                    risk = t.resolvedRisk,
                    isExternal = t.isExternal,
                    author = t.attribute?.Author,
                    version = t.attribute?.Version
                });
            }

            _allTools.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

            // Group by category
            _categories = _allTools
                .GroupBy(t => t.category)
                .OrderBy(g => g.Key)
                .Select(g => new CategoryInfo { key = g.Key, tools = g.ToList() })
                .ToList();

            // Group by risk level
            _dangerousTools = _allTools.Where(t => t.risk == ToolRisk.Dangerous).ToList();
            _cautionTools = _allTools.Where(t => t.risk == ToolRisk.Caution).ToList();
            _safeTools = _allTools.Where(t => t.risk == ToolRisk.Safe).ToList();

            // Reset search filter
            _prevSearchText = null;
            _filteredCategories = null;
        }

        private void InitStyles()
        {
            if (_helpStyle != null) return;

            _barLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fontSize = 10
            };
            _barLabelStyle.normal.textColor = Color.white;

            _barLabelStyleRight = new GUIStyle(_barLabelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };

            _overviewLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };

            _helpStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = true
            };
            _helpStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.75f, 0.75f, 0.75f)
                : new Color(0.25f, 0.25f, 0.25f);

            _disabledLabelStyle = new GUIStyle(EditorStyles.label);
            _disabledLabelStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.45f, 0.45f, 0.45f)
                : new Color(0.55f, 0.55f, 0.55f);
        }

        private void OnGUI()
        {
            InitStyles();
            if (_allTools == null) BuildToolCache();

            var confirmTools = AgentSettings.GetConfirmTools();

            // Header
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            int externalCount = _allTools.Count(t => t.isExternal);
            int internalCount = _allTools.Count - externalCount;
            int enabledExternalCount = _allTools.Count(t => t.isExternal && AgentSettings.IsToolEnabled(t.name, true));
            string headerLabel;
            if (externalCount > 0)
                headerLabel = string.Format(M("ツール別設定 (内蔵: {0}, 外部: {1} うち有効: {2})"), internalCount, externalCount, enabledExternalCount);
            else
                headerLabel = M("ツール別設定");
            EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);
            if (GUILayout.Button("?", EditorStyles.miniButton, GUILayout.Width(22)))
                _showHelp = !_showHelp;
            EditorGUILayout.EndHorizontal();

            // Help section
            if (_showHelp)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    M("AIエージェントがツールを実行する際の<b>有効/無効</b>と<b>確認動作</b>を設定します。\n\n" +
                    "<b>有効</b> のツール → AIが使用可能。確認ONなら実行前に確認ボタンが表示されます。\n" +
                    "<b>無効</b> のツール → AIには見えず、実行もブロックされます。\n\n" +
                    "外部ツールはセキュリティのため<b>デフォルト無効</b>です。\n" +
                    "使用するには手動で有効化してください。\n\n" +
                    "ツールは操作内容に応じて3段階に色分けされています:"),
                    _helpStyle);
                EditorGUILayout.Space(2);
                DrawHelpRiskRow(ToolRisk.Dangerous, M("<b>破壊的</b> - 削除・リセットなど元に戻しにくい操作"));
                DrawHelpRiskRow(ToolRisk.Caution, M("<b>変更</b> - 作成・設定変更など状態を変える操作"));
                DrawHelpRiskRow(ToolRisk.Safe, M("<b>読み取り</b> - 一覧取得・検索など変更を伴わない操作"));
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    M("迷ったら<b>「デフォルトに戻す」</b>がおすすめです。\n" +
                    "破壊的ツールを中心にONに設定されます。"),
                    _helpStyle);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            int totalAll = _allTools.Count;
            int onAll = CountOn(_allTools, confirmTools);

            // === Overview stacked bar ===
            DrawOverviewStackedBar(confirmTools, totalAll, onAll);

            EditorGUILayout.Space(6);

            // === Per-risk-level bars with ON/OFF buttons ===
            DrawRiskLevelBar(_dangerousTools, confirmTools, ToolRisk.Dangerous, M("破壊的"), DangerousColor);
            EditorGUILayout.Space(2);
            DrawRiskLevelBar(_cautionTools, confirmTools, ToolRisk.Caution, M("変更"), CautionColor);
            EditorGUILayout.Space(2);
            DrawRiskLevelBar(_safeTools, confirmTools, ToolRisk.Safe, M("読み取り"), SafeColor);

            EditorGUILayout.Space(6);

            // Preset buttons row (confirmation presets only — does not affect enabled/disabled)
            EditorGUILayout.BeginHorizontal();
            {
                var currentPreset = DetectCurrentPreset(confirmTools);

                // 慎重: all tools ON
                DrawPresetButton(M("慎重"), currentPreset == Preset.Careful, EditorStyles.miniButtonLeft, () =>
                {
                    var tools = new HashSet<string>(_allTools.Select(t => t.name));
                    AgentSettings.SetConfirmTools(tools);
                });
                // バランス: Dangerous + Caution ON, Safe OFF
                DrawPresetButton(M("バランス"), currentPreset == Preset.Balanced, EditorStyles.miniButtonMid, () =>
                {
                    var tools = new HashSet<string>(
                        _dangerousTools.Select(t => t.name).Concat(_cautionTools.Select(t => t.name)));
                    AgentSettings.SetConfirmTools(tools);
                });
                // 高速: Dangerous only ON
                DrawPresetButton(M("高速"), currentPreset == Preset.Fast, EditorStyles.miniButtonMid, () =>
                {
                    var tools = new HashSet<string>(_dangerousTools.Select(t => t.name));
                    AgentSettings.SetConfirmTools(tools);
                });
                // デフォルトに戻す
                DrawPresetButton(M("デフォルトに戻す"), currentPreset == Preset.Default, EditorStyles.miniButtonRight, () =>
                {
                    AgentSettings.SetConfirmTools(new HashSet<string>(AgentSettings.DefaultConfirmTools));
                });
            }
            EditorGUILayout.EndHorizontal();

            // External tool bulk enable/disable buttons
            if (externalCount > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(M("外部ツール:"), EditorStyles.miniLabel);
                if (GUILayout.Button(M("全て有効"), EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                {
                    foreach (var t in _allTools.Where(t => t.isExternal))
                        AgentSettings.SetToolEnabled(t.name, true, true);
                }
                if (GUILayout.Button(M("全て無効"), EditorStyles.miniButtonRight, GUILayout.Width(60)))
                {
                    foreach (var t in _allTools.Where(t => t.isExternal))
                        AgentSettings.SetToolEnabled(t.name, true, false);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);

            // Search field
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("\u2315", GUILayout.Width(20));
            _searchText = EditorGUILayout.TextField(_searchText);
            if (!string.IsNullOrEmpty(_searchText) && GUILayout.Button("\u2716", GUILayout.Width(20)))
                _searchText = "";
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Rebuild filtered list only when search text changes
            var displayCategories = GetFilteredCategories();

            // === Column headers ===
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16); // indent matching tool rows (slightly less for label alignment)
            GUILayout.Label(new GUIContent(M("有効"), M("AIがこのツールを使用できるかどうか")),
                EditorStyles.miniLabel, GUILayout.Width(24));
            GUILayout.Label(new GUIContent(M("確認"), M("AIが実行前にユーザーの確認を必要とするかどうか")),
                EditorStyles.miniLabel, GUILayout.Width(24));
            GUILayout.Space(16); // risk badge width + gap
            GUILayout.Label(M("ツール名"), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            var headerSepRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(headerSepRect, EditorGUIUtility.isProSkin ? SepColorDark : SepColorLight);

            EditorGUILayout.Space(2);

            // === Tool list ===
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var cat in displayCategories)
            {
                int catOnCount = CountOn(cat.tools, confirmTools);
                int catEnabledCount = CountEnabled(cat.tools);
                int catTotal = cat.tools.Count;

                // Category header with background
                var catRect = EditorGUILayout.BeginHorizontal();
                if (Event.current.type == EventType.Repaint)
                {
                    Color catBg = EditorGUIUtility.isProSkin ? CategoryBgDark : CategoryBgLight;
                    EditorGUI.DrawRect(catRect, catBg);
                }

                if (!_categoryFoldouts.ContainsKey(cat.key))
                    _categoryFoldouts[cat.key] = false;

                _categoryFoldouts[cat.key] = EditorGUILayout.Foldout(
                    _categoryFoldouts[cat.key],
                    cat.key,
                    true);

                GUILayout.FlexibleSpace();

                // Enabled count + bulk toggle
                GUILayout.Label($"{M("有効")} {catEnabledCount}/{catTotal}",
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                bool allEnabled = catEnabledCount == catTotal;
                bool newAllEnabled = GUILayout.Toggle(allEnabled,
                    new GUIContent("", M("カテゴリ内の全ツールの有効/無効を一括切替")),
                    GUILayout.Width(16));
                if (newAllEnabled != allEnabled)
                {
                    foreach (var t in cat.tools)
                        AgentSettings.SetToolEnabled(t.name, t.isExternal, newAllEnabled);
                }

                GUILayout.Space(8);

                // Confirm count + bulk toggle
                GUILayout.Label($"{M("確認")} {catOnCount}/{catTotal}",
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                bool allOn = catOnCount == catTotal;
                bool newAllOn = GUILayout.Toggle(allOn,
                    new GUIContent("", M("カテゴリ内の全ツールの確認を一括ON/OFF")),
                    GUILayout.Width(16));
                if (newAllOn != allOn)
                {
                    foreach (var t in cat.tools)
                        AgentSettings.SetToolConfirmRequired(t.name, newAllOn);
                }

                EditorGUILayout.EndHorizontal();

                // Individual tools (when expanded)
                if (!_categoryFoldouts[cat.key]) continue;

                Color altRowColor = EditorGUIUtility.isProSkin ? AltRowDark : AltRowLight;
                int rowIndex = 0;

                foreach (var tool in cat.tools)
                {
                    bool isEnabled = AgentSettings.IsToolEnabled(tool.name, tool.isExternal);

                    // Background tint + zebra stripe
                    var bgRect = EditorGUILayout.BeginHorizontal();
                    if (Event.current.type == EventType.Repaint)
                    {
                        if (!isEnabled)
                            EditorGUI.DrawRect(bgRect, DisabledBg);
                        else if (tool.risk == ToolRisk.Dangerous)
                            EditorGUI.DrawRect(bgRect, DangerousBg);
                        else if (tool.risk == ToolRisk.Caution)
                            EditorGUI.DrawRect(bgRect, CautionBg);

                        if (rowIndex % 2 == 1)
                            EditorGUI.DrawRect(bgRect, altRowColor);
                    }

                    GUILayout.Space(20);

                    // Enabled/disabled toggle
                    bool newEnabled = GUILayout.Toggle(isEnabled,
                        new GUIContent("", M("有効: AIがこのツールを使用可能")),
                        GUILayout.Width(16));
                    if (newEnabled != isEnabled)
                        AgentSettings.SetToolEnabled(tool.name, tool.isExternal, newEnabled);

                    // Confirmation toggle (greyed out when tool is disabled)
                    if (isEnabled)
                    {
                        bool isOn = confirmTools.Contains(tool.name);
                        bool newIsOn = GUILayout.Toggle(isOn,
                            new GUIContent("", M("確認: AIが実行前にユーザーの確認を要求")),
                            GUILayout.Width(16));
                        if (newIsOn != isOn)
                            AgentSettings.SetToolConfirmRequired(tool.name, newIsOn);
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            GUILayout.Toggle(false,
                                new GUIContent("", M("ツールが無効のため確認設定は変更できません")),
                                GUILayout.Width(16));
                        }
                    }

                    DrawRiskBadge(tool.risk);

                    // Tool name with description as tooltip
                    var nameContent = tool.briefDescription.Length > 0
                        ? new GUIContent(tool.name, tool.briefDescription)
                        : new GUIContent(tool.name);
                    GUILayout.Label(nameContent, isEnabled ? EditorStyles.label : _disabledLabelStyle);

                    if (tool.isExternal)
                    {
                        var prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
                        string badge = string.IsNullOrEmpty(tool.author) ? M("外部") : tool.author;
                        if (!string.IsNullOrEmpty(tool.version))
                            badge += $" v{tool.version}";
                        GUILayout.Label(badge, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                        GUI.backgroundColor = prevBg;
                    }

                    GUILayout.FlexibleSpace();

                    EditorGUILayout.EndHorizontal();
                    rowIndex++;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // =====================================================================
        // Filtered categories (cached, rebuilt only on search text change)
        // =====================================================================

        private List<CategoryInfo> GetFilteredCategories()
        {
            if (_searchText == _prevSearchText && _filteredCategories != null)
                return _filteredCategories;

            _prevSearchText = _searchText;

            if (string.IsNullOrEmpty(_searchText))
            {
                _filteredCategories = _categories;
                return _filteredCategories;
            }

            string searchLower = _searchText.ToLower();
            _filteredCategories = new List<CategoryInfo>();
            foreach (var cat in _categories)
            {
                var filtered = cat.tools.Where(t =>
                    t.name.ToLower().Contains(searchLower) ||
                    t.description.ToLower().Contains(searchLower)
                ).ToList();

                if (filtered.Count > 0)
                    _filteredCategories.Add(new CategoryInfo { key = cat.key, tools = filtered });
            }

            return _filteredCategories;
        }

        // =====================================================================
        // Overview stacked bar
        // =====================================================================

        private void DrawOverviewStackedBar(HashSet<string> confirmTools, int totalAll, int onAll)
        {
            int dangerousOn = CountOn(_dangerousTools, confirmTools);
            int cautionOn = CountOn(_cautionTools, confirmTools);
            int safeOn = CountOn(_safeTools, confirmTools);

            int pct = totalAll > 0 ? Mathf.RoundToInt(100f * onAll / totalAll) : 0;
            EditorGUILayout.LabelField(string.Format(M("確認ON: {0} / {1} ({2}%)"), onAll, totalAll, pct), _overviewLabelStyle);
            EditorGUILayout.Space(2);

            var barRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            barRect.x += 4;
            barRect.width -= 8;

            if (Event.current.type == EventType.Repaint)
            {
                Color barBg = EditorGUIUtility.isProSkin ? BarBgDark : BarBgLight;
                EditorGUI.DrawRect(barRect, barBg);

                if (totalAll > 0)
                {
                    float unitW = barRect.width / totalAll;
                    float x = barRect.x;

                    if (dangerousOn > 0)
                    {
                        EditorGUI.DrawRect(new Rect(x, barRect.y, unitW * dangerousOn, barRect.height), DangerousColor * 0.85f);
                        x += unitW * dangerousOn;
                    }
                    if (cautionOn > 0)
                    {
                        EditorGUI.DrawRect(new Rect(x, barRect.y, unitW * cautionOn, barRect.height), CautionColor * 0.85f);
                        x += unitW * cautionOn;
                    }
                    if (safeOn > 0)
                    {
                        EditorGUI.DrawRect(new Rect(x, barRect.y, unitW * safeOn, barRect.height), SafeColor * 0.85f);
                    }
                }

                // Overlay text with drop shadow
                var shadowRect = new Rect(barRect.x + 1, barRect.y + 1, barRect.width, barRect.height);
                var prevColor = _barLabelStyle.normal.textColor;
                _barLabelStyle.normal.textColor = new Color(0, 0, 0, 0.6f);
                GUI.Label(shadowRect, $"  {onAll} ON", _barLabelStyle);
                _barLabelStyle.normal.textColor = Color.white;
                GUI.Label(barRect, $"  {onAll} ON", _barLabelStyle);
                _barLabelStyle.normal.textColor = prevColor;
            }
        }

        // =====================================================================
        // Per-risk-level bar with ON/OFF buttons
        // =====================================================================

        private void DrawRiskLevelBar(List<ToolInfo> toolsInLevel, HashSet<string> confirmTools,
            ToolRisk risk, string label, Color color)
        {
            int total = toolsInLevel.Count;
            int onCount = CountOn(toolsInLevel, confirmTools);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);

            DrawRiskBadge(risk);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(46));

            // Progress bar
            var barRect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                Color barBg = EditorGUIUtility.isProSkin ? BarBgDark : BarBgLight;
                EditorGUI.DrawRect(barRect, barBg);

                if (total > 0 && onCount > 0)
                {
                    float fillW = barRect.width * onCount / total;
                    EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, fillW, barRect.height), color * 0.85f);
                }

                // Text on bar with drop shadow
                var textRect = new Rect(barRect.x + 4, barRect.y, barRect.width - 8, barRect.height);
                var shadowRect = new Rect(textRect.x + 1, textRect.y + 1, textRect.width, textRect.height);

                var prevL = _barLabelStyle.normal.textColor;
                var prevR = _barLabelStyleRight.normal.textColor;

                _barLabelStyle.normal.textColor = new Color(0, 0, 0, 0.6f);
                _barLabelStyleRight.normal.textColor = new Color(0, 0, 0, 0.6f);
                GUI.Label(shadowRect, $"{onCount}", _barLabelStyle);
                GUI.Label(shadowRect, $"/ {total}", _barLabelStyleRight);

                _barLabelStyle.normal.textColor = Color.white;
                _barLabelStyleRight.normal.textColor = new Color(1, 1, 1, 0.7f);
                GUI.Label(textRect, $"{onCount}", _barLabelStyle);
                GUI.Label(textRect, $"/ {total}", _barLabelStyleRight);

                _barLabelStyle.normal.textColor = prevL;
                _barLabelStyleRight.normal.textColor = prevR;
            }

            if (GUILayout.Button("ON", EditorStyles.miniButtonLeft, GUILayout.Width(32)))
            {
                foreach (var t in toolsInLevel)
                    AgentSettings.SetToolConfirmRequired(t.name, true);
            }
            if (GUILayout.Button("OFF", EditorStyles.miniButtonRight, GUILayout.Width(32)))
            {
                foreach (var t in toolsInLevel)
                    AgentSettings.SetToolConfirmRequired(t.name, false);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private void DrawHelpRiskRow(ToolRisk risk, string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            DrawRiskBadge(risk);
            GUILayout.Label(text, _helpStyle);
            EditorGUILayout.EndHorizontal();
        }

        private static int CountOn(List<ToolInfo> tools, HashSet<string> confirmTools)
        {
            int count = 0;
            for (int i = 0; i < tools.Count; i++)
            {
                if (confirmTools.Contains(tools[i].name))
                    count++;
            }
            return count;
        }

        private static int CountEnabled(List<ToolInfo> tools)
        {
            int count = 0;
            for (int i = 0; i < tools.Count; i++)
            {
                if (AgentSettings.IsToolEnabled(tools[i].name, tools[i].isExternal))
                    count++;
            }
            return count;
        }

        private static void DrawRiskBadge(ToolRisk risk)
        {
            Color color;
            switch (risk)
            {
                case ToolRisk.Dangerous: color = DangerousColor; break;
                case ToolRisk.Caution:   color = CautionColor; break;
                default:                 color = SafeColor; break;
            }

            var rect = GUILayoutUtility.GetRect(12, 14, GUILayout.Width(12));
            if (Event.current.type == EventType.Repaint)
            {
                var center = rect.center;
                float r = 4f;
                EditorGUI.DrawRect(new Rect(center.x - r, center.y - r, r * 2, r * 2), color);
            }
        }

        private static string GetBriefDescription(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            int periodIdx = desc.IndexOf('.');
            if (periodIdx > 0 && periodIdx < 120)
                return desc.Substring(0, periodIdx + 1);
            if (desc.Length <= 120)
                return desc;
            return desc.Substring(0, 117) + "...";
        }

        // =====================================================================
        // Presets (confirmation only — does not affect enabled/disabled)
        // =====================================================================

        private enum Preset { None, Careful, Balanced, Fast, Default }

        private Preset DetectCurrentPreset(HashSet<string> confirmTools)
        {
            int dangerousOn = CountOn(_dangerousTools, confirmTools);
            int cautionOn = CountOn(_cautionTools, confirmTools);
            int safeOn = CountOn(_safeTools, confirmTools);

            // Careful: all ON
            if (dangerousOn == _dangerousTools.Count && cautionOn == _cautionTools.Count && safeOn == _safeTools.Count)
                return Preset.Careful;
            // Fast: only Dangerous ON
            if (dangerousOn == _dangerousTools.Count && cautionOn == 0 && safeOn == 0)
                return Preset.Fast;
            // Balanced: Dangerous + Caution ON, Safe OFF
            if (dangerousOn == _dangerousTools.Count && cautionOn == _cautionTools.Count && safeOn == 0)
                return Preset.Balanced;
            // Default: matches DefaultConfirmTools exactly
            if (confirmTools.Count == AgentSettings.DefaultConfirmTools.Count && confirmTools.SetEquals(AgentSettings.DefaultConfirmTools))
                return Preset.Default;

            return Preset.None;
        }

        private static void DrawPresetButton(string label, bool isActive, GUIStyle style, System.Action onClick)
        {
            var prevBg = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = new Color(0.4f, 0.7f, 0.4f);
            string displayLabel = isActive ? $"\u2713 {label}" : label;
            if (GUILayout.Button(displayLabel, style))
                onClick();
            GUI.backgroundColor = prevBg;
        }
    }
}
