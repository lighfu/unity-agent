using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    internal class AgentLogWindow : EditorWindow
    {
        private MD3Theme _theme;
        private int _filterLevel; // 0=All, 1=Debug, 2=Info, 3=Warning, 4=Error
        private int _filterTagIndex; // 0=All, 1..N=LogTag enum values
        private ScrollView _scrollView;
        private bool _autoScroll = true;

        private static readonly string[] TagFilterLabels;

        static AgentLogWindow()
        {
            var names = Enum.GetNames(typeof(LogTag));
            TagFilterLabels = new string[names.Length + 1];
            TagFilterLabels[0] = "All";
            for (int i = 0; i < names.Length; i++)
                TagFilterLabels[i + 1] = names[i];
        }

        [MenuItem("Window/紫陽花広場/Agent Log")]
        public static void Open()
        {
            var window = GetWindow<AgentLogWindow>();
            window.titleContent = new GUIContent(M("Agent ログ"));
            window.minSize = new Vector2(500, 300);
            window.Show();
        }

        private void OnEnable()
        {
            AgentLogger.OnLogAdded += OnLogAdded;
        }

        private void OnDisable()
        {
            AgentLogger.OnLogAdded -= OnLogAdded;
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            _theme = ResolveTheme();
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !rootVisualElement.styleSheets.Contains(themeSheet))
                rootVisualElement.styleSheets.Add(themeSheet);
            if (compSheet != null && !rootVisualElement.styleSheets.Contains(compSheet))
                rootVisualElement.styleSheets.Add(compSheet);
            _theme.ApplyTo(rootVisualElement);

            rootVisualElement.style.flexGrow = 1;

            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = rootVisualElement;

            // Level filter toolbar
            var toolbar = new MD3Row(8);
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.paddingTop = 8;
            toolbar.style.paddingBottom = 4;
            toolbar.style.flexShrink = 0;

            var levelLabels = new[] { M("全て"), "Debug", "Info", "Warning", "Error" };
            var segmented = new MD3SegmentedButton(levelLabels, _filterLevel);
            segmented.style.flexGrow = 1;
            segmented.style.maxWidth = 400;
            segmented.changed += idx =>
            {
                _filterLevel = idx;
                RebuildLogEntries();
            };
            toolbar.Add(segmented);
            root.Add(toolbar);

            // Tag filter + clear row
            var filterRow = new MD3Row(8);
            filterRow.style.paddingLeft = 8;
            filterRow.style.paddingRight = 8;
            filterRow.style.paddingBottom = 4;
            filterRow.style.flexShrink = 0;

            var tagDropdown = new MD3Dropdown(M("タグ"), TagFilterLabels, _filterTagIndex);
            tagDropdown.style.flexGrow = 1;
            tagDropdown.style.maxWidth = 200;
            tagDropdown.changed += idx =>
            {
                _filterTagIndex = idx;
                RebuildLogEntries();
            };
            filterRow.Add(tagDropdown);

            var clearBtn = new MD3Button(M("クリア"), MD3ButtonStyle.Outlined);
            clearBtn.clicked += () =>
            {
                AgentLogger.Clear();
                RebuildLogEntries();
            };
            filterRow.Add(clearBtn);
            root.Add(filterRow);

            // Divider
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = _theme.OutlineVariant;
            divider.style.flexShrink = 0;
            root.Add(divider);

            // Log scroll area
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.backgroundColor = _theme.Surface;
            root.Add(_scrollView);

            RebuildLogEntries();
        }

        private void RebuildLogEntries()
        {
            if (_scrollView == null) return;
            _scrollView.Clear();

            var entries = AgentLogger.GetEntries();
            foreach (var entry in entries)
            {
                if (!PassesFilter(entry)) continue;
                _scrollView.Add(CreateEntryElement(entry));
            }

            if (_autoScroll)
            {
                _scrollView.schedule.Execute(() =>
                    _scrollView.scrollOffset = new Vector2(0, _scrollView.contentContainer.layout.height));
            }
        }

        private bool PassesFilter(LogEntry entry)
        {
            if (_filterLevel > 0)
            {
                var targetLevel = (LogLevel)(_filterLevel - 1);
                if (entry.Level != targetLevel) return false;
            }

            if (_filterTagIndex > 0)
            {
                var targetTag = (LogTag)(_filterTagIndex - 1);
                if (entry.Tag != targetTag) return false;
            }

            return true;
        }

        private VisualElement CreateEntryElement(LogEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = _theme.OutlineVariant;

            var color = GetLevelColor(entry.Level);

            var timeLabel = new Label(entry.Timestamp.ToString("HH:mm:ss.fff"));
            timeLabel.style.color = _theme.OnSurfaceVariant;
            timeLabel.style.fontSize = 11;
            timeLabel.style.minWidth = 85;
            timeLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
            row.Add(timeLabel);

            var levelLabel = new Label(entry.Level.ToString().ToUpper());
            levelLabel.style.color = color;
            levelLabel.style.fontSize = 11;
            levelLabel.style.minWidth = 55;
            levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(levelLabel);

            var tagLabel = new Label(entry.Tag.ToString());
            tagLabel.style.color = _theme.Primary;
            tagLabel.style.fontSize = 11;
            tagLabel.style.minWidth = 80;
            tagLabel.style.maxWidth = 120;
            tagLabel.style.overflow = Overflow.Hidden;
            row.Add(tagLabel);

            var msgLabel = new Label(entry.Message ?? "");
            msgLabel.style.color = color;
            msgLabel.style.fontSize = 11;
            msgLabel.style.flexGrow = 1;
            msgLabel.style.flexShrink = 1;
            msgLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(msgLabel);

            return row;
        }

        private Color GetLevelColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug:   return _theme.OnSurfaceVariant;
                case LogLevel.Info:    return _theme.OnSurface;
                case LogLevel.Warning: return _theme.Tertiary;
                case LogLevel.Error:   return _theme.Error;
                default:               return _theme.OnSurface;
            }
        }

        private void OnLogAdded(LogEntry entry)
        {
            EditorApplication.delayCall += () =>
            {
                if (_scrollView == null) return;
                if (!PassesFilter(entry)) return;

                _scrollView.Add(CreateEntryElement(entry));

                if (_autoScroll)
                {
                    _scrollView.schedule.Execute(() =>
                        _scrollView.scrollOffset = new Vector2(0, _scrollView.contentContainer.layout.height));
                }
            };
        }

        private static MD3Theme ResolveTheme()
        {
            switch (AgentSettings.ThemeMode)
            {
                case 1: return MD3Theme.Dark();
                case 2: return MD3Theme.Light();
                case 3: return AgentSettings.BuildCustomTheme();
                default: return MD3Theme.Auto();
            }
        }
    }
}
