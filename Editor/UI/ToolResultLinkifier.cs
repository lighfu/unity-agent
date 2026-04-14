using System.Collections.Generic;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// ツール実行結果テキストをクリック可能なリンク付きの VisualElement に変換するヘルパ。
    /// ChatEntry.ParseResultsPublic 経由で既存の正規表現群を再利用する。
    /// </summary>
    internal static class ToolResultLinkifier
    {
        /// <summary>
        /// 結果テキストを Label に変換し、認識した asset/object 参照をクリック可能な行として下に追加する。
        /// </summary>
        public static VisualElement Build(string text, MD3Theme theme, int maxPreviewLines = 0)
        {
            var container = new MD3Column(gap: 2f);
            container.style.flexGrow = 1;

            if (string.IsNullOrEmpty(text))
                return container;

            string displayText = maxPreviewLines > 0
                ? TruncateToLines(text, maxPreviewLines)
                : text;

            var textLabel = new Label(displayText);
            textLabel.style.fontSize = 12;
            textLabel.style.color = theme.OnSurface;
            textLabel.style.whiteSpace = WhiteSpace.Normal;
            textLabel.selection.isSelectable = true;
            container.Add(textLabel);

            var results = ChatEntry.ParseResultsPublic(text);
            if (results != null && results.Count > 0)
            {
                foreach (var item in results)
                {
                    var row = new MD3Row(gap: 6f);
                    row.style.alignItems = Align.Center;
                    row.style.marginLeft = 8;
                    row.style.marginTop = 2;

                    var icon = new Label(item.isAsset ? MD3Icon.InsertDriveFile : MD3Icon.ViewInAr);
                    MD3Icon.Apply(icon, 14f);
                    icon.style.color = theme.Primary;
                    row.Add(icon);

                    var link = new Label(item.displayName ?? item.reference ?? "");
                    link.style.fontSize = 12;
                    link.style.color = theme.Primary;
                    link.style.unityFontStyleAndWeight = FontStyle.Bold;
                    link.RegisterCallback<ClickEvent>(_ => item.SelectAndPing());
                    link.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        link.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
                    });
                    link.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        link.style.unityFontStyleAndWeight = FontStyle.Bold;
                    });
                    row.Add(link);

                    if (!string.IsNullOrEmpty(item.typeName))
                    {
                        var type = new Label($"({item.typeName})");
                        type.style.fontSize = 11;
                        type.style.color = theme.OnSurfaceVariant;
                        type.style.opacity = 0.7f;
                        row.Add(type);
                    }

                    container.Add(row);
                }
            }

            return container;
        }

        static string TruncateToLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text) || maxLines <= 0) return text;
            var lines = text.Split('\n');
            if (lines.Length <= maxLines) return text;
            return string.Join("\n", lines, 0, maxLines) + "\n…";
        }
    }
}
