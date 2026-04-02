using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;
using UIE = UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>トークン使用量バー: LinearProgress + クリックでコンテキスト詳細ポップアップ。</summary>
    internal class TokenBar : VisualElement
    {
        readonly MD3Theme _theme;
        readonly MD3LinearProgress _progress;
        readonly Label _label;
        int _promptTokens, _maxTokens, _totalTokens, _inputTokens, _outputTokens;
        string _modelName;

        public TokenBar(MD3Theme theme)
        {
            _theme = theme;

            style.flexDirection = UIE.FlexDirection.Row;
            style.flexShrink = 0;
            style.alignItems = Align.Center;
            style.height = 20;
            style.paddingLeft = 16;
            style.paddingRight = 16;

            _progress = new MD3LinearProgress(0f);
            _progress.style.flexGrow = 1;
            _progress.style.height = 4;
            Add(_progress);

            _label = new Label("");
            _label.style.fontSize = 10;
            _label.style.color = theme.OnSurfaceVariant;
            _label.style.marginLeft = 8;
            _label.style.minWidth = 80;
            _label.style.unityTextAlign = TextAnchor.MiddleRight;
            Add(_label);

            // クリックでポップアップ (IMGUI PopupWindow を使用)
            RegisterCallback<ClickEvent>(evt =>
            {
                var popup = new ContextInfoPopup(
                    _promptTokens, _maxTokens, _totalTokens,
                    _inputTokens, _outputTokens, _modelName);
                UnityEditor.PopupWindow.Show(worldBound, popup);
            });
        }

        /// <summary>トークン情報を更新する。</summary>
        public void UpdateTokens(int promptTokens, int maxTokens, int totalTokens,
            int inputTokens, int outputTokens, string modelName, string costStr = null)
        {
            _promptTokens = promptTokens;
            _maxTokens = maxTokens;
            _totalTokens = totalTokens;
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
            _modelName = modelName;

            float ratio = maxTokens > 0 ? (float)promptTokens / maxTokens : 0f;
            _progress.Value = ratio;

            string labelText = $"{FormatTokenCount(promptTokens)} / {FormatTokenCount(maxTokens)}";
            if (!string.IsNullOrEmpty(costStr))
                labelText += $"  {costStr}";
            _label.text = labelText;

            // 色を使用率で変更
            if (ratio > 0.85f)
                _label.style.color = _theme.Error;
            else if (ratio > 0.7f)
                _label.style.color = _theme.Tertiary;
            else
                _label.style.color = _theme.OnSurfaceVariant;
        }

        static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000000) return $"{tokens / 1000000f:0.#}M";
            if (tokens >= 1000) return $"{tokens / 1000f:0.#}k";
            return tokens.ToString();
        }
    }
}
