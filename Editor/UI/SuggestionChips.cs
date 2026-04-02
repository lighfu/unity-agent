using System;
using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using UIE = UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>サジェスションチップの Flow レイアウト行。</summary>
    internal class SuggestionChips : VisualElement
    {
        readonly MD3Theme _theme;

        public Action<string> OnChipClicked;

        public SuggestionChips(MD3Theme theme)
        {
            _theme = theme;

            style.flexDirection = UIE.FlexDirection.Row;
            style.flexShrink = 0;
            style.flexWrap = Wrap.Wrap;
            style.paddingLeft = 16;
            style.paddingRight = 16;
            style.paddingTop = 4;
            style.paddingBottom = 4;
        }

        /// <summary>チップリストを更新する。</summary>
        public void UpdateChips(List<(string displayText, string insertText)> items)
        {
            Clear();

            if (items == null || items.Count == 0)
            {
                style.display = DisplayStyle.None;
                return;
            }

            style.display = DisplayStyle.Flex;

            foreach (var (display, insert) in items)
            {
                var chip = new MD3Chip(display, false);
                chip.style.marginRight = 6;
                chip.style.marginBottom = 4;
                chip.toggled += _ => OnChipClicked?.Invoke(insert);
                Add(chip);
            }
        }
    }
}
