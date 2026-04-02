using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.SDK;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class ToolConsoleWindow : EditorWindow
    {
        // ─── Tool metadata ───

        private struct ToolEntry
        {
            public string name;
            public string description;          // 英語 (AgentToolAttribute)
            public string descriptionLocalized;  // 翻訳済み（フル翻訳、省略なし）
            public string category;
            public MethodInfo method;
            public ParamEntry[] parameters;
            public bool isAsync;
        }

        private struct ParamEntry
        {
            public string name;
            public string typeName;
            public Type type;
            public bool hasDefault;
            public object defaultValue;
        }

        // ─── Cache ───
        private List<ToolEntry> _allTools;
        private List<string> _categories;
        private List<ToolEntry> _filteredTools;

        // ─── UI state ───
        private Vector2 _listScrollPos;
        private Vector2 _resultScrollPos;
        private string _searchText = "";
        private string _prevSearchText;
        private int _selectedIndex = -1;
        private string _selectedCategory = "";
        private string[] _paramValues;
        private int _selectedLangIndex;

        // ─── Execution ───
        private string _lastResult = "";
        private bool _isExecuting;
        private IEnumerator _runningCoroutine;

        // ─── Styles ───
        private GUIStyle _descStyle;
        private GUIStyle _descBoldStyle;
        private GUIStyle _resultStyle;
        private GUIStyle _sigStyle;
        private GUIStyle _paramLabelStyle;

        [MenuItem("Window/紫陽花広場/Tool Console")]
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
            var window = GetWindow<ToolConsoleWindow>();
            window.titleContent = new GUIContent(M("ツールコンソール"));
            window.minSize = new Vector2(700, 450);
            window.Show();
        }

        private void OnEnable()
        {
            string currentLang = AgentSettings.UILanguage;
            _selectedLangIndex = 0;
            for (int i = 0; i < AgentSettings.SupportedLanguages.Length; i++)
            {
                if (AgentSettings.SupportedLanguages[i].code == currentLang)
                {
                    _selectedLangIndex = i;
                    break;
                }
            }

            TranslationManagementWindow.OnTranslationsUpdated += RebuildLocalizedDescriptions;
            BuildToolCache();
        }

        private void OnDisable()
        {
            TranslationManagementWindow.OnTranslationsUpdated -= RebuildLocalizedDescriptions;
        }

        private void Update()
        {
            if (_runningCoroutine != null)
            {
                try
                {
                    if (!_runningCoroutine.MoveNext())
                    {
                        _runningCoroutine = null;
                        _isExecuting = false;
                        Repaint();
                    }
                    else if (_runningCoroutine.Current is string str)
                    {
                        _lastResult = str;
                        Repaint();
                    }
                }
                catch (Exception ex)
                {
                    _lastResult = string.Format(M("エラー: {0}\n{1}"), ex.Message, ex.StackTrace);
                    _runningCoroutine = null;
                    _isExecuting = false;
                    Repaint();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Tool discovery
        // ═══════════════════════════════════════════════════════════════

        private void BuildToolCache()
        {
            var registryTools = ToolRegistry.GetAllTools();

            _allTools = new List<ToolEntry>(registryTools.Count);
            string lang = AgentSettings.UILanguage;

            foreach (var t in registryTools)
            {
                var m = t.method;
                var attr = t.attribute;
                var ps = m.GetParameters();
                var paramEntries = new ParamEntry[ps.Length];

                for (int i = 0; i < ps.Length; i++)
                {
                    paramEntries[i] = new ParamEntry
                    {
                        name = ps[i].Name,
                        typeName = FriendlyTypeName(ps[i].ParameterType),
                        type = ps[i].ParameterType,
                        hasDefault = ps[i].HasDefaultValue,
                        defaultValue = ps[i].HasDefaultValue ? ps[i].DefaultValue : null,
                    };
                }

                string cat = attr?.Category;
                if (string.IsNullOrEmpty(cat))
                    cat = m.DeclaringType.Name.Replace("Tools", "");
                string eng = attr?.Description ?? "";
                _allTools.Add(new ToolEntry
                {
                    name = m.Name,
                    description = eng,
                    descriptionLocalized = ToolTranslationService.Get(m.Name, eng, lang),
                    category = cat,
                    method = m,
                    parameters = paramEntries,
                    isAsync = m.ReturnType == typeof(IEnumerator),
                });
            }

            _allTools.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            _categories = _allTools.Select(t => t.category).Distinct().OrderBy(c => c).ToList();

            _prevSearchText = null;
            ApplyFilter();
        }

        private static string FriendlyTypeName(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(long)) return "long";
            return t.Name;
        }

        private void ApplyFilter()
        {
            if (_searchText == _prevSearchText && _filteredTools != null) return;
            _prevSearchText = _searchText;

            IEnumerable<ToolEntry> source = _allTools;

            if (!string.IsNullOrEmpty(_selectedCategory))
                source = source.Where(t => t.category == _selectedCategory);

            if (!string.IsNullOrEmpty(_searchText))
            {
                string kw = _searchText.ToLower();
                source = source.Where(t =>
                    t.name.ToLower().Contains(kw) ||
                    t.description.ToLower().Contains(kw) ||
                    t.descriptionLocalized.ToLower().Contains(kw) ||
                    t.category.ToLower().Contains(kw));
            }

            _filteredTools = source.ToList();

            if (_selectedIndex >= _filteredTools.Count)
                _selectedIndex = _filteredTools.Count > 0 ? 0 : -1;
        }

        // ═══════════════════════════════════════════════════════════════
        // Styles
        // ═══════════════════════════════════════════════════════════════

        private void InitStyles()
        {
            if (_descStyle != null) return;

            _descStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = true
            };
            _descStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.3f, 0.3f);

            _descBoldStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            _resultStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false,
                fontSize = 11
            };

            _sigStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                wordWrap = true,
                richText = true
            };

            _paramLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // OnGUI
        // ═══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();
            if (_allTools == null) BuildToolCache();

            EditorGUILayout.BeginHorizontal();
            {
                // Left: tool list
                EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.35f), GUILayout.MinWidth(200));
                DrawToolList();
                EditorGUILayout.EndVertical();

                // Right: detail + console
                EditorGUILayout.BeginVertical();
                DrawDetailPanel();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Tool List ───

        private void DrawToolList()
        {
            DrawLanguageHeader();

            // Search bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            string newSearch = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                _prevSearchText = null;
                ApplyFilter();
            }
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.miniButton, GUILayout.Width(18)))
            {
                _searchText = "";
                _prevSearchText = null;
                ApplyFilter();
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // Category filter
            EditorGUILayout.BeginHorizontal();
            {
                bool allSelected = string.IsNullOrEmpty(_selectedCategory);
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = allSelected ? new Color(0.4f, 0.7f, 1f) : Color.gray;
                if (GUILayout.Toggle(allSelected, M("全て"), EditorStyles.miniButtonLeft) && !allSelected)
                {
                    _selectedCategory = "";
                    _prevSearchText = null;
                    ApplyFilter();
                }
                GUI.backgroundColor = prevBg;
            }
            EditorGUILayout.EndHorizontal();

            // Category buttons
            EditorGUILayout.BeginHorizontal();
            float catRowWidth = 0;
            float maxCatWidth = position.width * 0.35f - 10;
            foreach (var cat in _categories)
            {
                bool isSel = cat == _selectedCategory;
                float btnW = GUI.skin.button.CalcSize(new GUIContent(cat)).x + 4;
                if (catRowWidth + btnW > maxCatWidth && catRowWidth > 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    catRowWidth = 0;
                }
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isSel ? new Color(0.4f, 0.7f, 1f) : Color.gray;
                if (GUILayout.Toggle(isSel, cat, EditorStyles.miniButton, GUILayout.Height(16)))
                {
                    if (!isSel)
                    {
                        _selectedCategory = cat;
                        _prevSearchText = null;
                        ApplyFilter();
                    }
                }
                else if (isSel)
                {
                    _selectedCategory = "";
                    _prevSearchText = null;
                    ApplyFilter();
                }
                GUI.backgroundColor = prevBg;
                catRowWidth += btnW;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField(string.Format(M("{0} ツール"), _filteredTools.Count), EditorStyles.centeredGreyMiniLabel);

            // Scrollable list
            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos);
            for (int i = 0; i < _filteredTools.Count; i++)
            {
                var tool = _filteredTools[i];
                bool isSelected = (i == _selectedIndex);

                var rect = EditorGUILayout.BeginHorizontal(
                    isSelected ? "SelectionRect" : GUIStyle.none,
                    GUILayout.Height(24));

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    SelectTool(i);
                    Event.current.Use();
                }

                if (tool.isAsync)
                    GUILayout.Label("~", GUILayout.Width(12));
                else
                    GUILayout.Space(12);

                GUILayout.Label(tool.name, isSelected ? EditorStyles.whiteLabel : EditorStyles.label);
                GUILayout.Label(TruncateForList(tool.descriptionLocalized), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                int requiredCount = tool.parameters.Count(p => !p.hasDefault);
                string paramHint = requiredCount == tool.parameters.Length
                    ? $"({tool.parameters.Length})"
                    : $"({requiredCount}..{tool.parameters.Length})";
                GUILayout.Label(paramHint, EditorStyles.miniLabel, GUILayout.Width(40));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void SelectTool(int index)
        {
            _selectedIndex = index;
            _lastResult = "";

            if (index >= 0 && index < _filteredTools.Count)
            {
                var tool = _filteredTools[index];
                _paramValues = new string[tool.parameters.Length];
                for (int i = 0; i < tool.parameters.Length; i++)
                {
                    var p = tool.parameters[i];
                    _paramValues[i] = p.hasDefault && p.defaultValue != null
                        ? p.defaultValue.ToString()
                        : "";
                }
            }
        }

        // ─── Detail Panel ───

        private void DrawDetailPanel()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredTools.Count)
            {
                EditorGUILayout.Space(40);
                GUILayout.Label(M("左のリストからツールを選択してください"), EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var tool = _filteredTools[_selectedIndex];

            // ── Signature (auto-height) ──
            EditorGUILayout.Space(4);
            string sig = BuildSignatureDisplay(tool);
            float panelWidth = position.width * 0.65f - 20;
            DrawAutoHeightLabel(sig, _sigStyle, panelWidth);

            // Category + async badge
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.Format(M("カテゴリ: {0}"), tool.category), _paramLabelStyle, GUILayout.Width(200));
            if (tool.isAsync)
                EditorGUILayout.LabelField(M("非同期 (IEnumerator)"), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // ── Description (auto-height) ──
            EditorGUILayout.Space(2);
            string lang = AgentSettings.UILanguage;
            if (lang == "en")
            {
                if (!string.IsNullOrEmpty(tool.description))
                    DrawAutoHeightLabel(tool.description, _descBoldStyle, panelWidth);
            }
            else
            {
                // 翻訳済み説明（フル翻訳）
                DrawAutoHeightLabel(tool.descriptionLocalized, _descBoldStyle, panelWidth);

                // 翻訳がフォールバック（英語のまま）でなければ、英語原文も参照用に表示
                if (tool.descriptionLocalized != tool.description)
                    DrawAutoHeightLabel(tool.description, _descStyle, panelWidth);
            }

            // Separator
            EditorGUILayout.Space(4);
            var sepRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(sepRect, EditorGUIUtility.isProSkin
                ? new Color(1, 1, 1, 0.1f) : new Color(0, 0, 0, 0.1f));
            EditorGUILayout.Space(4);

            // ── Parameters ──
            if (tool.parameters.Length > 0)
            {
                EditorGUILayout.LabelField(M("パラメータ"), EditorStyles.boldLabel);

                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 160;

                for (int i = 0; i < tool.parameters.Length; i++)
                {
                    var p = tool.parameters[i];
                    string label = p.hasDefault
                        ? $"{p.name} ({p.typeName}, ={FormatDefault(p.defaultValue)})"
                        : $"{p.name} ({p.typeName}) *";

                    if (p.type == typeof(bool))
                    {
                        bool boolVal = _paramValues[i] == "True" || _paramValues[i] == "true";
                        bool newVal = EditorGUILayout.Toggle(label, boolVal);
                        _paramValues[i] = newVal.ToString();
                    }
                    else
                    {
                        _paramValues[i] = EditorGUILayout.TextField(label, _paramValues[i]);
                    }
                }

                EditorGUIUtility.labelWidth = prevLabelWidth;
                EditorGUILayout.Space(4);
            }

            // ── Action buttons ──
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = !_isExecuting;
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
                if (GUILayout.Button(M("実行"), GUILayout.Height(28), GUILayout.Width(80)))
                {
                    ExecuteTool(tool);
                }
                GUI.backgroundColor = prevBg;
                GUI.enabled = true;

                if (GUILayout.Button(M("スキル記法をコピー"), EditorStyles.miniButton, GUILayout.Height(28)))
                {
                    string callSyntax = BuildCallSyntax(tool);
                    EditorGUIUtility.systemCopyBuffer = callSyntax;
                    ShowNotification(new GUIContent(M("コピーしました")));
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_isExecuting)
            {
                EditorGUILayout.HelpBox(M("実行中..."), MessageType.Info);
            }

            // ── Result ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(M("結果"), EditorStyles.boldLabel);
            _resultScrollPos = EditorGUILayout.BeginScrollView(_resultScrollPos, GUILayout.MinHeight(100));
            EditorGUILayout.TextArea(_lastResult, _resultStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        // Execution
        // ═══════════════════════════════════════════════════════════════

        private void ExecuteTool(ToolEntry tool)
        {
            var ps = tool.parameters;
            object[] args = new object[ps.Length];

            for (int i = 0; i < ps.Length; i++)
            {
                string val = _paramValues[i];

                if (string.IsNullOrEmpty(val) && ps[i].hasDefault)
                {
                    args[i] = ps[i].defaultValue;
                    continue;
                }

                try
                {
                    args[i] = ConvertParam(val, ps[i].type);
                }
                catch (Exception ex)
                {
                    _lastResult = string.Format(M("パラメータ '{0}' の変換エラー: {1}"), ps[i].name, ex.Message);
                    return;
                }
            }

            try
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"ToolConsole: {tool.name}");

                object rawResult = tool.method.Invoke(null, args);

                if (rawResult is IEnumerator enumerator)
                {
                    _isExecuting = true;
                    _lastResult = M("非同期実行中...");
                    _runningCoroutine = enumerator;
                }
                else
                {
                    _lastResult = rawResult?.ToString() ?? M("(返り値なし)");
                }
            }
            catch (TargetInvocationException ex)
            {
                _lastResult = string.Format(M("エラー: {0}\n{1}"), ex.InnerException?.Message ?? ex.Message, ex.InnerException?.StackTrace ?? ex.StackTrace);
            }
            catch (Exception ex)
            {
                _lastResult = string.Format(M("エラー: {0}\n{1}"), ex.Message, ex.StackTrace);
            }

            Repaint();
        }

        private static object ConvertParam(string value, Type targetType)
        {
            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
                return value == "True" || value == "true" || value == "1";
            if (targetType == typeof(int))
                return int.Parse(value);
            if (targetType == typeof(float))
                return float.Parse(value);
            if (targetType == typeof(double))
                return double.Parse(value);
            if (targetType == typeof(long))
                return long.Parse(value);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, true);

            return Convert.ChangeType(value, targetType);
        }

        // ═══════════════════════════════════════════════════════════════
        // Formatting
        // ═══════════════════════════════════════════════════════════════

        private string BuildSignatureDisplay(ToolEntry tool)
        {
            var parts = new List<string>();
            foreach (var p in tool.parameters)
            {
                string part = p.hasDefault
                    ? $"<color=#888>{p.typeName} {p.name}={FormatDefault(p.defaultValue)}</color>"
                    : $"<color=#9cf>{p.typeName}</color> {p.name}";
                parts.Add(part);
            }
            string ret = tool.isAsync ? "IEnumerator" : "string";
            return $"<color=#6d6>{ret}</color> <b>{tool.name}</b>({string.Join(", ", parts)})";
        }

        private string BuildCallSyntax(ToolEntry tool)
        {
            var args = new List<string>();
            for (int i = 0; i < tool.parameters.Length; i++)
            {
                string val = _paramValues[i];
                var p = tool.parameters[i];

                if (p.hasDefault && val == FormatDefault(p.defaultValue))
                    continue;
                if (p.hasDefault && string.IsNullOrEmpty(val))
                    continue;

                if (p.type == typeof(string))
                    args.Add($"'{val}'");
                else
                    args.Add(val);
            }
            return $"[{tool.name}({string.Join(", ", args)})]";
        }

        private static string FormatDefault(object val)
        {
            if (val == null) return "null";
            if (val is string s) return $"'{s}'";
            if (val is bool b) return b ? "true" : "false";
            if (val is float f) return f.ToString("G");
            return val.ToString();
        }

        private static string TruncateForList(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";

            int nl = desc.IndexOf('\n');
            if (nl >= 0) desc = desc.Substring(0, nl);

            int dot = desc.IndexOf('.');
            if (dot >= 0 && dot < 40)
                return desc.Substring(0, dot + 1);

            if (desc.Length <= 40)
                return desc;

            return desc.Substring(0, 37) + "...";
        }

        private void DrawAutoHeightLabel(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text)) return;
            var content = new GUIContent(text);
            float h = style.CalcHeight(content, width);
            EditorGUILayout.LabelField(content, style, GUILayout.Height(h));
        }

        // ═══════════════════════════════════════════════════════════════
        // Language header
        // ═══════════════════════════════════════════════════════════════

        private void DrawLanguageHeader()
        {
            var langs = AgentSettings.SupportedLanguages;
            string[] labels = new string[langs.Length];
            for (int i = 0; i < langs.Length; i++)
                labels[i] = langs[i].label;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                EditorGUILayout.LabelField(M("言語"), GUILayout.Width(30));
                int newIndex = EditorGUILayout.Popup(_selectedLangIndex, labels, EditorStyles.toolbarPopup, GUILayout.Width(80));
                if (newIndex != _selectedLangIndex)
                {
                    _selectedLangIndex = newIndex;
                    AgentSettings.UILanguage = langs[newIndex].code;
                    titleContent = new GUIContent(M("ツールコンソール"));
                    L10n.Reload();
                    RebuildLocalizedDescriptions();
                }

                string langCode = langs[_selectedLangIndex].code;
                bool needsToolTranslation = langCode != "en";
                bool needsUITranslation = langCode != "ja";

                // ─── 進捗表示 ───
                if (needsToolTranslation)
                {
                    var (toolTranslated, toolTotal) = ToolTranslationService.GetProgress(langCode, _allTools?.Count ?? 0);
                    GUILayout.Label($"({M("ツール")}: {toolTranslated}/{toolTotal})",
                        EditorStyles.miniLabel, GUILayout.Width(90));
                }

                if (needsUITranslation)
                {
                    var (uiTranslated, uiTotal) = L10n.GetProgress(langCode);
                    GUILayout.Label($"({M("UI")}: {uiTranslated}/{uiTotal})",
                        EditorStyles.miniLabel, GUILayout.Width(70));
                }

                GUILayout.FlexibleSpace();

                // ─── 翻訳管理ボタン ───
                if (GUILayout.Button(M("翻訳管理"), EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    TranslationManagementWindow.Open();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void RebuildLocalizedDescriptions()
        {
            if (_allTools == null) return;

            string lang = AgentSettings.UILanguage;
            for (int i = 0; i < _allTools.Count; i++)
            {
                var t = _allTools[i];
                t.descriptionLocalized = ToolTranslationService.Get(t.name, t.description, lang);
                _allTools[i] = t;
            }

            _prevSearchText = null;
            ApplyFilter();
            Repaint();
        }
    }
}
