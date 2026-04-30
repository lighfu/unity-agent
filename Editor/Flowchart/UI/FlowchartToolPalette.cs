using System;
using System.Linq;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Left-side tool palette. Shows every registered AgentTool grouped by Category with
    /// a search field at the top and a per-row drag handle. Drag uses Unity's editor
    /// DragAndDrop so the canvas can read the payload via DragPerformEvent.
    /// </summary>
    internal sealed class FlowchartToolPalette : VisualElement
    {
        public const string DragGenericKey = "ua-flowchart-tool";

        readonly MD3Theme _theme;
        readonly TextField _search;
        readonly ScrollView _list;
        string _currentSearch = "";

        public FlowchartToolPalette(MD3Theme theme)
        {
            _theme = theme;
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1;

            var heading = new Label(M("ツール"));
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.paddingLeft = 8;
            heading.style.paddingRight = 8;
            heading.style.paddingTop = 8;
            heading.style.paddingBottom = 4;
            if (_theme != null) heading.style.color = _theme.OnSurface;
            Add(heading);

            _search = new TextField();
            _search.style.marginLeft = 8;
            _search.style.marginRight = 8;
            _search.style.marginBottom = 4;
            var searchInput = _search.Q<TextElement>();
            if (searchInput != null) searchInput.text = "";
            _search.RegisterValueChangedCallback(evt =>
            {
                _currentSearch = evt.newValue ?? "";
                Refresh();
            });
            Add(_search);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            Add(_list);

            Refresh();
        }

        public void Refresh()
        {
            _list.Clear();
            int rendered = 0;
            foreach (var group in ToolPaletteSource.GroupedByCategory(_currentSearch))
            {
                var section = BuildSection(group.Key, group.OrderBy(e => e.Name));
                _list.Add(section);
                rendered += group.Count();
            }

            if (rendered == 0)
            {
                var empty = new Label(M("該当するツールがありません"));
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.marginTop = 16;
                if (_theme != null) empty.style.color = _theme.OnSurfaceVariant;
                _list.Add(empty);
            }
        }

        VisualElement BuildSection(string category, System.Collections.Generic.IEnumerable<ToolPaletteEntry> entries)
        {
            var section = new VisualElement();
            section.style.marginBottom = 8;

            var header = new Label(category);
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 4;
            header.style.fontSize = 11;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (_theme != null)
            {
                header.style.color = _theme.OnSurfaceVariant;
                header.style.backgroundColor = _theme.SurfaceContainerLow;
            }
            section.Add(header);

            foreach (var entry in entries)
                section.Add(BuildRow(entry));

            return section;
        }

        VisualElement BuildRow(ToolPaletteEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.tooltip = string.IsNullOrEmpty(entry.Description) ? entry.Name : entry.Description;

            var name = new Label(entry.Name);
            name.style.flexGrow = 1;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (_theme != null) name.style.color = _theme.OnSurface;
            row.Add(name);

            row.Add(BuildRiskBadge(entry.Risk));

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                StartDrag(entry);
            });

            // Highlight on hover so the row feels affordant for drag.
            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (_theme != null) row.style.backgroundColor = _theme.SurfaceContainer;
            });
            row.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                row.style.backgroundColor = StyleKeyword.Null;
            });

            return row;
        }

        VisualElement BuildRiskBadge(ToolRisk risk)
        {
            var badge = new Label(RiskShortLabel(risk));
            badge.style.fontSize = 9;
            badge.style.paddingLeft = 4;
            badge.style.paddingRight = 4;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            badge.style.borderTopLeftRadius = 4;
            badge.style.borderTopRightRadius = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;
            badge.style.color = Color.white;
            badge.style.backgroundColor = RiskColor(risk);
            badge.style.marginLeft = 6;
            return badge;
        }

        static string RiskShortLabel(ToolRisk risk) => risk switch
        {
            ToolRisk.Safe => "S",
            ToolRisk.Caution => "C",
            ToolRisk.Dangerous => "!",
            _ => "?",
        };

        static Color RiskColor(ToolRisk risk) => risk switch
        {
            ToolRisk.Safe => new Color(0.30f, 0.65f, 0.40f),
            ToolRisk.Caution => new Color(0.85f, 0.65f, 0.20f),
            ToolRisk.Dangerous => new Color(0.85f, 0.30f, 0.30f),
            _ => Color.gray,
        };

        void StartDrag(ToolPaletteEntry entry)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragGenericKey, entry);
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(entry.Name);
        }
    }
}
