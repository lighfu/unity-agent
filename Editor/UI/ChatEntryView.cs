using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// 個別のチャットエントリを表示する VisualElement。
    /// ChatEntry.EntryType に基づいてファクトリで生成する。
    /// </summary>
    internal class ChatEntryView : VisualElement
    {
        public int EntryIndex { get; set; }
        public Action OnEdit;
        public Action<string> OnCopy;

        Label _textLabel;
        string _rawText;

        ChatEntryView() { }

        /// <summary>ChatEntry からビューを生成するファクトリ。</summary>
        public static ChatEntryView Create(ChatEntry entry, MD3Theme theme)
        {
            switch (entry.type)
            {
                case ChatEntry.EntryType.User: return CreateUserView(entry, theme);
                case ChatEntry.EntryType.Agent: return CreateAgentView(entry, theme);
                case ChatEntry.EntryType.Info: return CreateInfoView(entry, theme);
                case ChatEntry.EntryType.Error: return CreateErrorView(entry, theme);
                case ChatEntry.EntryType.ToolCall: return CreateToolCallView(entry, theme);
                case ChatEntry.EntryType.Choice:
                    if (entry.isBatchToolConfirm) return CreateBatchConfirmView(entry, theme);
                    if (entry.isClipboard) return CreateClipboardView(entry, theme);
                    return CreateChoiceView(entry, theme);
                default: return CreateInfoView(entry, theme);
            }
        }

        /// <summary>
        /// EntryType.ToolCall 用のラッパー。ToolCallView.Create が返す VisualElement を
        /// ChatEntryView にホストさせ、後から UpdateState で再描画できるようにする。
        /// </summary>
        static ChatEntryView CreateToolCallView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 4;
            view.style.marginBottom = 4;

            var card = ToolCallView.Create(entry, theme);
            view._toolCallRoot = card;
            view._toolCallEntry = entry;
            view._toolCallTheme = theme;
            view.Add(card);
            return view;
        }

        /// <summary>
        /// ToolCall 状態遷移を反映する (Running → Success/Error)。
        /// ChatPanel.CompleteToolCall から呼ばれる。
        /// </summary>
        public void RefreshToolCall()
        {
            if (_toolCallRoot != null && _toolCallEntry != null && _toolCallTheme != null)
                ToolCallView.UpdateState(_toolCallRoot, _toolCallEntry, _toolCallTheme);
        }

        VisualElement _toolCallRoot;
        ChatEntry _toolCallEntry;
        MD3Theme _toolCallTheme;

        /// <summary>ストリーミング中のコンテンツ更新。</summary>
        public void UpdateContent(ChatEntry entry)
        {
            if (_textLabel == null) return;
            string newText = entry.text ?? "";
            if (newText != _rawText)
            {
                _rawText = newText;
                _textLabel.text = MarkdownToRichText(newText);
            }
        }

        // ══════════════════════════════════════════════
        //  User Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateUserView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 8;
            view.style.marginBottom = 4;
            view.style.flexDirection = FlexDirection.Row;
            view.style.justifyContent = Justify.FlexEnd;

            var bubble = new MD3Card(null, null, MD3CardStyle.Filled);
            bubble.style.backgroundColor = theme.PrimaryContainer;
            bubble.style.borderTopLeftRadius = 16;
            bubble.style.borderTopRightRadius = 16;
            bubble.style.borderBottomLeftRadius = 16;
            bubble.style.borderBottomRightRadius = 4;
            bubble.style.maxWidth = new StyleLength(new Length(80, LengthUnit.Percent));

            string displayText = entry.text ?? "";
            if (displayText.StartsWith("You: "))
                displayText = displayText.Substring(5);

            // 画像プレビュー
            var userImg = entry.EnsureImagePreview();
            if (userImg != null)
            {
                var img = new Image { image = userImg };
                img.style.width = 120;
                img.style.height = 90;
                img.style.borderTopLeftRadius = 8;
                img.style.borderTopRightRadius = 8;
                img.style.borderBottomLeftRadius = 8;
                img.style.borderBottomRightRadius = 8;
                img.style.marginBottom = 4;
                img.scaleMode = ScaleMode.ScaleToFit;
                bubble.Add(img);
            }

            var label = new Label(displayText);
            label.style.color = theme.OnPrimaryContainer;
            label.style.fontSize = 14;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.enableRichText = false;
            bubble.Add(label);

            view._textLabel = label;
            view._rawText = displayText;

            // 右クリックでコピー
            view.RegisterCallback<ContextClickEvent>(evt =>
            {
                view.OnCopy?.Invoke(entry.text);
            });

            view.Add(bubble);

            // 編集ボタン
            var editBtn = new MD3IconButton(MD3Icon.Edit, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
            editBtn.style.alignSelf = Align.FlexEnd;
            editBtn.clicked += () => view.OnEdit?.Invoke();
            view.Add(editBtn);

            return view;
        }

        // ══════════════════════════════════════════════
        //  Agent Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateAgentView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 4;
            view.style.marginBottom = 8;

            var bubble = new MD3Card(null, null, MD3CardStyle.Filled);
            bubble.style.backgroundColor = theme.SurfaceContainerHigh;
            bubble.style.borderTopLeftRadius = 4;
            bubble.style.borderTopRightRadius = 16;
            bubble.style.borderBottomLeftRadius = 16;
            bubble.style.borderBottomRightRadius = 16;
            bubble.style.maxWidth = new StyleLength(new Length(90, LengthUnit.Percent));

            // Thinking 折りたたみ
            if (!string.IsNullOrEmpty(entry.thinkingText))
            {
                var thinkFold = new MD3Foldout(M("思考プロセス"), false);
                var thinkLabel = new Label(entry.thinkingText);
                thinkLabel.style.color = theme.OnSurfaceVariant;
                thinkLabel.style.fontSize = 12;
                thinkLabel.style.whiteSpace = WhiteSpace.Normal;
                thinkLabel.style.opacity = 0.7f;
                thinkFold.Content.Add(thinkLabel);
                bubble.Add(thinkFold);
            }

            // メイン テキスト
            string displayText = entry.text ?? "";
            var label = new Label(MarkdownToRichText(displayText));
            label.enableRichText = true;
            label.style.color = theme.OnSurface;
            label.style.fontSize = 14;
            label.style.whiteSpace = WhiteSpace.Normal;
            bubble.Add(label);

            view._textLabel = label;
            view._rawText = displayText;

            // ツール結果
            if (entry.results != null && entry.results.Count > 0)
            {
                var resultContainer = new MD3Column(gap: 4f);
                resultContainer.style.marginTop = 8;
                foreach (var item in entry.results)
                {
                    var resultLabel = new Label(item.displayName ?? item.reference);
                    resultLabel.style.fontSize = 12;
                    resultLabel.style.color = theme.OnSurfaceVariant;
                    resultLabel.style.whiteSpace = WhiteSpace.Normal;
                    resultLabel.RegisterCallback<ClickEvent>(evt => item.SelectAndPing());
                    resultContainer.Add(resultLabel);
                }
                bubble.Add(resultContainer);
            }

            // デバッグログ
            if (entry.debugLogs != null && entry.debugLogs.Count > 0)
            {
                var debugFold = new MD3Foldout(M("デバッグログ"), false);
                foreach (var log in entry.debugLogs)
                {
                    var logLabel = new Label(log);
                    logLabel.style.fontSize = 11;
                    logLabel.style.color = theme.OnSurfaceVariant;
                    logLabel.style.whiteSpace = WhiteSpace.Normal;
                    logLabel.style.opacity = 0.6f;
                    debugFold.Content.Add(logLabel);
                }
                bubble.Add(debugFold);
            }

            // リクエスト時間
            if (entry.requestDuration.HasValue)
            {
                var durationLabel = new Label($"{entry.requestDuration.Value.TotalSeconds:F1}s");
                durationLabel.style.fontSize = 10;
                durationLabel.style.color = theme.OnSurfaceVariant;
                durationLabel.style.opacity = 0.5f;
                durationLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                durationLabel.style.marginTop = 4;
                bubble.Add(durationLabel);
            }

            // 右クリックでコピー
            view.RegisterCallback<ContextClickEvent>(evt =>
            {
                view.OnCopy?.Invoke(entry.text);
            });

            view.Add(bubble);
            return view;
        }

        // ══════════════════════════════════════════════
        //  Info Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateInfoView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 2;
            view.style.marginBottom = 2;

            var row = new MD3Row(gap: 8f);
            row.style.alignItems = Align.Center;

            string text = entry.text ?? "";
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = theme.OnSurfaceVariant;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 1;
            row.Add(label);

            view._textLabel = label;
            view._rawText = text;

            // 画像プレビュー (SceneView キャプチャ等)
            var infoImg = entry.EnsureImagePreview();
            if (infoImg != null)
            {
                var img = new Image { image = infoImg };
                img.style.width = 160;
                img.style.height = 120;
                img.style.borderTopLeftRadius = 8;
                img.style.borderTopRightRadius = 8;
                img.style.borderBottomLeftRadius = 8;
                img.style.borderBottomRightRadius = 8;
                img.scaleMode = ScaleMode.ScaleToFit;
                view.Add(img);
            }

            view.Add(row);
            return view;
        }

        // ══════════════════════════════════════════════
        //  Error Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateErrorView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 4;
            view.style.marginBottom = 4;

            var bubble = new MD3Card(null, null, MD3CardStyle.Filled);
            bubble.style.backgroundColor = new Color(theme.Error.r, theme.Error.g, theme.Error.b, 0.15f);

            var label = new Label(entry.text ?? "");
            label.style.color = theme.Error;
            label.style.fontSize = 13;
            label.style.whiteSpace = WhiteSpace.Normal;
            bubble.Add(label);

            view._textLabel = label;
            view._rawText = entry.text;

            view.Add(bubble);
            return view;
        }

        // ══════════════════════════════════════════════
        //  Choice Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateChoiceView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 8;
            view.style.marginBottom = 8;

            // Question text
            if (!string.IsNullOrEmpty(entry.text))
            {
                var qLabel = new Label(MarkdownToRichText(entry.text));
                qLabel.enableRichText = true;
                qLabel.style.color = theme.OnSurface;
                qLabel.style.fontSize = 14;
                qLabel.style.whiteSpace = WhiteSpace.Normal;
                qLabel.style.marginBottom = 8;
                view.Add(qLabel);

                view._textLabel = qLabel;
                view._rawText = entry.text;
            }

            // 選択肢ボタン
            if (entry.choiceOptions != null)
            {
                bool isResolved = entry.choiceSelectedIndex >= 0;
                var btnRow = new MD3Row();
                btnRow.style.flexWrap = Wrap.Wrap;

                for (int i = 0; i < entry.choiceOptions.Length; i++)
                {
                    int idx = i;
                    bool selected = isResolved && entry.choiceSelectedIndex == i;

                    var style = entry.isToolConfirm && i == 0
                        ? MD3ButtonStyle.Filled
                        : MD3ButtonStyle.Outlined;

                    var btn = new MD3Button(entry.choiceOptions[i], style);
                    btn.style.marginRight = 6;
                    btn.style.marginBottom = 4;

                    if (isResolved)
                    {
                        btn.SetEnabled(false);
                        if (selected)
                            btn.style.opacity = 1f;
                        else
                            btn.style.opacity = 0.4f;
                    }
                    else
                    {
                        btn.clicked += () =>
                        {
                            entry.choiceSelectedIndex = idx;
                            // Disable all buttons after selection
                            foreach (var child in btnRow.Children().OfType<MD3Button>())
                                child.SetEnabled(false);

                            if (entry.isToolConfirm)
                                HandleToolConfirm(entry, idx);
                            else
                                HandleUserChoice(entry, idx);
                        };
                    }

                    btnRow.Add(btn);
                }

                view.Add(btnRow);
            }

            return view;
        }

        static void HandleToolConfirm(ChatEntry entry, int idx)
        {
            switch (idx)
            {
                case 0: ToolConfirmState.Select(ToolConfirmState.APPROVE); break;
                case 1: ToolConfirmState.Select(ToolConfirmState.CANCEL); break;
                case 2: ToolConfirmState.Select(ToolConfirmState.APPROVE_AND_DISABLE); break;
                case 3: ToolConfirmState.SessionSkipAll = true; ToolConfirmState.Select(ToolConfirmState.APPROVE_ALL_SESSION); break;
            }
        }

        static void HandleUserChoice(ChatEntry entry, int idx)
        {
            if (entry.choiceOptions != null && idx >= 0 && idx < entry.choiceOptions.Length)
                UserChoiceState.Select(idx);
        }

        // ══════════════════════════════════════════════
        //  Batch Tool Confirm
        // ══════════════════════════════════════════════

        static ChatEntryView CreateBatchConfirmView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 8;
            view.style.marginBottom = 8;

            var card = new MD3Card(null, null, MD3CardStyle.Outlined);
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;

            var title = new MD3Text(M("ツール実行確認"), MD3TextStyle.TitleMedium);
            title.style.color = theme.OnSurface;
            card.Add(title);

            if (entry.batchItems != null)
            {
                foreach (var item in entry.batchItems)
                {
                    var itemRow = new MD3Row(gap: 8f);
                    itemRow.style.alignItems = Align.Center;
                    itemRow.style.marginTop = 4;

                    var checkIcon = new Label(item.approved ? MD3Icon.Check : MD3Icon.Close);
                    MD3Icon.Apply(checkIcon, 16);
                    checkIcon.style.color = item.approved ? theme.Primary : theme.OnSurfaceVariant;
                    itemRow.Add(checkIcon);

                    var nameLabel = new Label(item.toolName ?? "");
                    nameLabel.style.fontSize = 13;
                    nameLabel.style.color = theme.OnSurface;
                    itemRow.Add(nameLabel);

                    card.Add(itemRow);
                }
            }

            // ボタン行
            if (!entry.batchResolved)
            {
                var btnRow = new MD3Row(gap: 8f);
                btnRow.style.marginTop = 12;
                btnRow.style.justifyContent = Justify.FlexEnd;

                var denyBtn = new MD3Button(M("キャンセル"), MD3ButtonStyle.Outlined);
                denyBtn.clicked += () =>
                {
                    entry.batchResolved = true;
                    BatchToolConfirmState.Resolve(new System.Collections.Generic.HashSet<string>());
                };
                btnRow.Add(denyBtn);

                var allowBtn = new MD3Button(M("すべて実行"), MD3ButtonStyle.Filled);
                allowBtn.clicked += () =>
                {
                    entry.batchResolved = true;
                    var approved = new System.Collections.Generic.HashSet<string>();
                    if (entry.batchItems != null)
                        foreach (var item in entry.batchItems)
                            if (item.toolName != null) approved.Add(item.toolName);
                    BatchToolConfirmState.Resolve(approved);
                };
                btnRow.Add(allowBtn);

                card.Add(btnRow);
            }

            view.Add(card);
            return view;
        }

        // ══════════════════════════════════════════════
        //  Clipboard Message
        // ══════════════════════════════════════════════

        static ChatEntryView CreateClipboardView(ChatEntry entry, MD3Theme theme)
        {
            var view = new ChatEntryView();
            view.style.marginTop = 8;
            view.style.marginBottom = 8;

            var card = new MD3Card(null, null, MD3CardStyle.Outlined);
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;

            var title = new MD3Text(M("クリップボードから応答を貼り付けてください"), MD3TextStyle.TitleSmall);
            title.style.color = theme.OnSurface;
            card.Add(title);

            if (entry.choiceSelectedIndex < 0)
            {
                var textField = new TextField();
                textField.multiline = true;
                textField.style.minHeight = 80;
                textField.style.marginTop = 8;
                card.Add(textField);

                var submitBtn = new MD3Button(M("送信"), MD3ButtonStyle.Filled);
                submitBtn.style.marginTop = 8;
                submitBtn.clicked += () =>
                {
                    string text = textField.value;
                    if (!string.IsNullOrEmpty(text))
                    {
                        entry.choiceSelectedIndex = 0;
                        ClipboardProviderState.PendingResponse = text;
                        ClipboardProviderState.Submit();
                    }
                };
                card.Add(submitBtn);
            }
            else
            {
                var doneLabel = new Label(M("送信済み"));
                doneLabel.style.color = theme.OnSurfaceVariant;
                doneLabel.style.marginTop = 8;
                card.Add(doneLabel);
            }

            view.Add(card);
            return view;
        }

        // ══════════════════════════════════════════════
        //  Markdown → RichText
        // ══════════════════════════════════════════════

        static readonly Regex CodeBlockRegex = new Regex(
            @"```[\w]*\r?\n([\s\S]*?)```", RegexOptions.Compiled);
        static readonly Regex InlineCodeRegex = new Regex(
            @"`([^`\n]+)`", RegexOptions.Compiled);
        static readonly Regex BoldRegex = new Regex(
            @"\*\*(.+?)\*\*", RegexOptions.Compiled);
        static readonly Regex ItalicRegex = new Regex(
            @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);

        internal static string MarkdownToRichText(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";

            bool dark = UnityEditor.EditorGUIUtility.isProSkin;
            string codeColor = dark ? "#9CDCFE" : "#0451A5";
            string headingColor = dark ? "#DCDCAA" : "#795E26";

            var codeBlocks = new List<string>();
            md = CodeBlockRegex.Replace(md, m =>
            {
                int idx = codeBlocks.Count;
                codeBlocks.Add($"<color={codeColor}>{EscapeRichText(m.Groups[1].Value.TrimEnd())}</color>");
                return $"\x00CB{idx}\x00";
            });

            var inlineCodes = new List<string>();
            md = InlineCodeRegex.Replace(md, m =>
            {
                int idx = inlineCodes.Count;
                inlineCodes.Add($"<color={codeColor}>{EscapeRichText(m.Groups[1].Value)}</color>");
                return $"\x00IC{idx}\x00";
            });

            md = BoldRegex.Replace(md, "<b>$1</b>");
            md = ItalicRegex.Replace(md, "<i>$1</i>");

            var lines = md.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("### "))
                    lines[i] = $"<b><color={headingColor}>{trimmed.Substring(4)}</color></b>";
                else if (trimmed.StartsWith("## "))
                    lines[i] = $"<b><color={headingColor}>{trimmed.Substring(3)}</color></b>";
                else if (trimmed.StartsWith("# "))
                    lines[i] = $"<b><color={headingColor}>{trimmed.Substring(2)}</color></b>";
                else if (trimmed.StartsWith("* ") || trimmed.StartsWith("- "))
                    lines[i] = "  \u2022 " + trimmed.Substring(2);
            }

            md = string.Join("\n", lines);

            for (int i = 0; i < inlineCodes.Count; i++)
                md = md.Replace($"\x00IC{i}\x00", inlineCodes[i]);
            for (int i = 0; i < codeBlocks.Count; i++)
                md = md.Replace($"\x00CB{i}\x00", codeBlocks[i]);

            return md;
        }

        static string EscapeRichText(string text)
        {
            return text.Replace("<", "\u2039").Replace(">", "\u203A");
        }
    }
}
