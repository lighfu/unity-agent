using System;
using System.Collections.Generic;
using System.Reflection;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// ツール実行を 1 枚のカードで表示する View。
    /// Running → Success/Error/Cancelled の状態遷移を in-place で反映する。
    /// ChatEntryView.Create() が EntryType.ToolCall を検出したときに呼ばれる。
    /// </summary>
    internal static class ToolCallView
    {
        // VisualElement name constants (Q によるルックアップ用)
        const string NameRoot = "toolcall-root";
        const string NameHeaderIcon = "toolcall-header-icon";
        const string NameStatusPill = "toolcall-status-pill";
        const string NameDurationLabel = "toolcall-duration";
        const string NameRunningSpinner = "toolcall-spinner";
        const string NameResultContainer = "toolcall-result";
        const string NameArgsFoldout = "toolcall-args";

        public static VisualElement Create(ChatEntry entry, MD3Theme theme)
        {
            var card = new MD3Card(null, null, MD3CardStyle.Outlined);
            card.name = NameRoot;
            card.style.marginTop = 4;
            card.style.marginBottom = 4;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.borderLeftWidth = 4;

            // Background: SurfaceContainerHigh with safe fallback (テーマ未定義対策)
            var bgColor = theme.SurfaceContainerHigh.a > 0.1f
                ? theme.SurfaceContainerHigh
                : new Color(0.16f, 0.15f, 0.18f, 1f);
            card.style.backgroundColor = bgColor;

            // ── Header row ──
            var header = new MD3Row(gap: 8f);
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;

            var headerIcon = new Label(IconForCategory(entry.toolCategory, entry.toolName));
            headerIcon.name = NameHeaderIcon;
            MD3Icon.Apply(headerIcon, 18f);
            header.Add(headerIcon);

            var title = new MD3Text(entry.toolName ?? "(unknown tool)", MD3TextStyle.TitleSmall);
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            header.Add(title);

            // Running spinner (only visible while running)
            var spinner = new MD3Loading(MD3LoadingStyle.Expressive, 16f);
            spinner.name = NameRunningSpinner;
            header.Add(spinner);

            // Status pill
            var pill = BuildStatusPill(entry.toolStatus, theme);
            pill.name = NameStatusPill;
            header.Add(pill);

            // Duration badge
            var duration = new Label();
            duration.name = NameDurationLabel;
            duration.style.fontSize = 11;
            duration.style.color = theme.OnSurfaceVariant;
            duration.style.marginLeft = 4;
            header.Add(duration);

            card.Add(header);

            // ── Arguments foldout ──
            if (!string.IsNullOrEmpty(entry.toolArgsRaw))
            {
                var argsFold = new MD3Foldout(M("引数"), false);
                argsFold.name = NameArgsFoldout;
                argsFold.style.marginTop = 4;

                var pairs = ToolArgsFormatter.Parse(entry.toolName, entry.toolArgsRaw);
                if (pairs.Count == 0)
                {
                    var raw = new Label(entry.toolArgsRaw);
                    raw.style.fontSize = 11;
                    raw.style.color = theme.OnSurfaceVariant;
                    raw.style.whiteSpace = WhiteSpace.Normal;
                    raw.selection.isSelectable = true;
                    argsFold.Content.Add(raw);
                }
                else
                {
                    foreach (var kv in pairs)
                    {
                        var row = new MD3Row(gap: 8f);
                        row.style.alignItems = Align.FlexStart;
                        row.style.marginBottom = 2;

                        var key = new Label(kv.Key + ":");
                        key.style.fontSize = 11;
                        key.style.color = theme.OnSurfaceVariant;
                        key.style.minWidth = 80;
                        key.style.unityFontStyleAndWeight = FontStyle.Bold;
                        row.Add(key);

                        var val = new Label(TruncateForPreview(kv.Value));
                        val.style.fontSize = 11;
                        val.style.color = theme.OnSurface;
                        val.style.whiteSpace = WhiteSpace.Normal;
                        val.style.flexGrow = 1;
                        val.style.flexShrink = 1;
                        val.selection.isSelectable = true;
                        row.Add(val);

                        argsFold.Content.Add(row);
                    }
                }
                card.Add(argsFold);
            }

            // ── Result container (filled after CompleteToolCall) ──
            var resultContainer = new VisualElement();
            resultContainer.name = NameResultContainer;
            resultContainer.style.marginTop = 6;
            card.Add(resultContainer);

            ApplyState(card, entry, theme);
            return card;
        }

        public static void UpdateState(VisualElement card, ChatEntry entry, MD3Theme theme)
        {
            if (card == null) return;
            ApplyState(card, entry, theme);
        }

        static void ApplyState(VisualElement card, ChatEntry entry, MD3Theme theme)
        {
            // Border accent color
            var accent = AccentFor(entry.toolStatus, theme);
            card.style.borderLeftColor = accent;

            // Spinner
            var spinner = card.Q<VisualElement>(NameRunningSpinner);
            if (spinner != null)
                spinner.style.display = entry.toolStatus == ToolCallStatus.Running
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            // Status pill refresh
            var oldPill = card.Q<VisualElement>(NameStatusPill);
            if (oldPill != null)
            {
                var parent = oldPill.parent;
                int idx = parent.IndexOf(oldPill);
                oldPill.RemoveFromHierarchy();
                var newPill = BuildStatusPill(entry.toolStatus, theme);
                newPill.name = NameStatusPill;
                parent.Insert(idx, newPill);
            }

            // Duration
            var duration = card.Q<Label>(NameDurationLabel);
            if (duration != null)
            {
                duration.text = FormatDuration(entry);
                duration.style.display = string.IsNullOrEmpty(duration.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            // Result container: only fill once a result/error arrives
            var resultContainer = card.Q<VisualElement>(NameResultContainer);
            if (resultContainer != null)
            {
                resultContainer.Clear();
                if (!string.IsNullOrEmpty(entry.toolResult))
                {
                    if (entry.toolStatus == ToolCallStatus.Error)
                    {
                        var errLabel = new Label(entry.toolResult);
                        errLabel.style.fontSize = 12;
                        errLabel.style.color = theme.Error.a > 0.1f ? theme.Error : new Color(0.95f, 0.5f, 0.5f, 1f);
                        errLabel.style.whiteSpace = WhiteSpace.Normal;
                        errLabel.selection.isSelectable = true;
                        resultContainer.Add(errLabel);
                    }
                    else
                    {
                        resultContainer.Add(ToolResultLinkifier.Build(entry.toolResult, theme));
                    }
                }
            }
        }

        // ── Status pill ──

        static VisualElement BuildStatusPill(ToolCallStatus status, MD3Theme theme)
        {
            var pill = new VisualElement();
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.paddingLeft = 8;
            pill.style.paddingRight = 8;
            pill.style.paddingTop = 2;
            pill.style.paddingBottom = 2;
            pill.style.borderTopLeftRadius = 10;
            pill.style.borderTopRightRadius = 10;
            pill.style.borderBottomLeftRadius = 10;
            pill.style.borderBottomRightRadius = 10;

            var accent = AccentFor(status, theme);
            pill.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.18f);

            var icon = new Label(IconFor(status));
            MD3Icon.Apply(icon, 12f);
            icon.style.color = accent;
            icon.style.marginRight = 4;
            pill.Add(icon);

            var label = new Label(StatusLabel(status));
            label.style.fontSize = 10;
            label.style.color = accent;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.Add(label);

            return pill;
        }

        static Color AccentFor(ToolCallStatus status, MD3Theme theme)
        {
            switch (status)
            {
                case ToolCallStatus.Running:
                    return theme.Primary.a > 0.1f
                        ? theme.Primary
                        : new Color(0.82f, 0.74f, 1f, 1f);
                case ToolCallStatus.Success:
                    return theme.Tertiary.a > 0.1f
                        ? theme.Tertiary
                        : new Color(0.55f, 0.85f, 0.55f, 1f);
                case ToolCallStatus.Error:
                    return theme.Error.a > 0.1f
                        ? theme.Error
                        : new Color(0.95f, 0.5f, 0.5f, 1f);
                case ToolCallStatus.Cancelled:
                default:
                    return theme.OnSurfaceVariant.a > 0.1f
                        ? theme.OnSurfaceVariant
                        : new Color(0.7f, 0.7f, 0.72f, 1f);
            }
        }

        static string IconFor(ToolCallStatus status)
        {
            switch (status)
            {
                case ToolCallStatus.Running: return MD3Icon.Schedule;
                case ToolCallStatus.Success: return MD3Icon.CheckCircle;
                case ToolCallStatus.Error: return MD3Icon.Error;
                case ToolCallStatus.Cancelled: return MD3Icon.Cancel;
                default: return MD3Icon.Schedule;
            }
        }

        static string StatusLabel(ToolCallStatus status)
        {
            switch (status)
            {
                case ToolCallStatus.Running: return M("実行中");
                case ToolCallStatus.Success: return M("成功");
                case ToolCallStatus.Error: return M("エラー");
                case ToolCallStatus.Cancelled: return M("キャンセル");
                default: return "?";
            }
        }

        static string IconForCategory(string category, string toolName)
        {
            if (!string.IsNullOrEmpty(toolName))
            {
                if (toolName.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Create", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MD3Icon.InsertDriveFile;
                if (toolName.IndexOf("Read", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Search", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MD3Icon.FolderOpen;
                if (toolName.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MD3Icon.ViewInAr;
                if (toolName.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    toolName.IndexOf("Compile", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MD3Icon.Code;
            }
            return MD3Icon.Build;
        }

        static string FormatDuration(ChatEntry entry)
        {
            if (entry.toolStatus == ToolCallStatus.Running)
            {
                if (entry.toolStartedUtc == default) return "";
                var elapsed = (DateTime.UtcNow - entry.toolStartedUtc).TotalMilliseconds;
                return $"{elapsed:0}ms";
            }

            if (entry.toolDurationMs <= 0) return "";
            if (entry.toolDurationMs < 1000) return $"{entry.toolDurationMs}ms";
            return $"{entry.toolDurationMs / 1000.0:0.0}s";
        }

        static string TruncateForPreview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            const int Max = 400;
            if (s.Length <= Max) return s;
            return s.Substring(0, Max) + "…";
        }
    }

    /// <summary>
    /// "foo, bar=1, \"str\"" 形式の引数文字列を key/value ペアに分解する。
    /// ツール名が ToolRegistry に登録されていれば ParameterInfo から正しい key 名を取る。
    /// </summary>
    internal static class ToolArgsFormatter
    {
        public static List<KeyValuePair<string, string>> Parse(string toolName, string argsRaw)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(argsRaw)) return result;

            // Strip surrounding parens if present
            var trimmed = argsRaw.Trim();
            if (trimmed.StartsWith("(") && trimmed.EndsWith(")"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);

            var parts = SplitTopLevelCommas(trimmed);
            var paramNames = TryGetParamNames(toolName);

            for (int i = 0; i < parts.Count; i++)
            {
                string p = parts[i].Trim();
                string key, value;

                int eq = FindNamedAssignOp(p);
                if (eq > 0)
                {
                    key = p.Substring(0, eq).Trim();
                    value = p.Substring(eq + 1).Trim();
                }
                else
                {
                    key = (paramNames != null && i < paramNames.Count)
                        ? paramNames[i]
                        : $"arg{i}";
                    value = p;
                }

                result.Add(new KeyValuePair<string, string>(key, TrimQuotes(value)));
            }

            return result;
        }

        static List<string> TryGetParamNames(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;
            try
            {
                foreach (var info in ToolRegistry.GetAllTools())
                {
                    if (info.method == null) continue;
                    if (info.method.Name == toolName)
                    {
                        var names = new List<string>();
                        foreach (var p in info.method.GetParameters())
                            names.Add(p.Name);
                        return names;
                    }
                }
            }
            catch
            {
                // ignore — fall through to arg0/arg1 naming
            }
            return null;
        }

        static int FindNamedAssignOp(string s)
        {
            int depth = 0;
            bool inStr = false;
            char strCh = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                    if (c == strCh) inStr = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inStr = true; strCh = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (depth == 0 && c == ':' && i + 1 < s.Length && s[i + 1] == ' ')
                    return i;
                else if (depth == 0 && c == '=' && (i == 0 || s[i - 1] != '='))
                    return i;
            }
            return -1;
        }

        static List<string> SplitTopLevelCommas(string s)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inStr = false;
            char strCh = '\0';
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                    if (c == strCh) inStr = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inStr = true; strCh = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (depth == 0 && c == ',')
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start <= s.Length)
                parts.Add(s.Substring(start));
            return parts;
        }

        static string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0])
                return s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
