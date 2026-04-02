using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Providers;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// All-models capability reference window.
    /// Shows every registered model's capabilities regardless of provider setting.
    /// </summary>
    internal class ModelCapabilityWindow : EditorWindow
    {
        private MD3Theme _theme;
        private List<Section> _sections;
        private List<ImageSection> _imageSections;

        // ── Data ──

        private struct Section
        {
            public string Label;
            public List<Row> Rows;
        }

        private struct Row
        {
            public string DisplayName;
            public string ModelId;
            public bool Thinking;
            public bool Image;
            public bool Search;
            public bool Streaming;
            public int InputTokens;
            public int OutputTokens;
            public int ThinkingBudgetMin;
            public int ThinkingBudgetMax;
            public bool Deprecated;
        }

        private struct ImageSection
        {
            public string Label;
            public List<ImageRow> Rows;
        }

        private struct ImageRow
        {
            public string DisplayName;
            public string ModelId;
            public string Provider;
            public string OutputSize;
            public bool EditSupport;
            public int TimeoutSec;
        }

        // Model family order for grouping
        private static readonly (string prefix, string label)[] FamilyRules =
        {
            ("gemini-", "Gemini"),
            ("claude-", "Claude"),
            ("codex-",  "OpenAI / Codex"),
            ("gpt-",    "OpenAI / Codex"),
            ("o3",      "OpenAI / Codex"),
            ("o4-",     "OpenAI / Codex"),
            ("deepseek-","DeepSeek"),
            ("grok-",   "xAI (Grok)"),
            ("llama",   "Groq / Ollama"),
            ("gpt-oss", "Groq / Ollama"),
            ("qwen",    "Groq / Ollama"),
            ("gemma",   "Groq / Ollama"),
            ("phi",     "Groq / Ollama"),
            ("mistral-","Mistral"),
            ("codestral","Mistral"),
            ("devstral","Mistral"),
            ("pixtral-","Mistral"),
            ("open-mistral","Mistral"),
            ("sonar",   "Perplexity"),
        };

        // ── Open ──

        public static void Open()
        {
            var window = GetWindow<ModelCapabilityWindow>();
            window.titleContent = new GUIContent(M("すべてのモデル性能"));
            window.minSize = new Vector2(780, 420);
            window.Show();
        }

        // ── Lifecycle ──

        internal void CreateGUI()
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

            Build();
            BuildUI();
        }

        // ── Build ──

        private void Build()
        {
            // Collect all models and bucket them by family label
            var buckets = new Dictionary<string, List<Row>>();
            var order = new List<string>(); // preserve display order

            foreach (var cap in ModelCapabilityRegistry.GetAllModels())
            {
                string family = ClassifyFamily(cap.ModelId);
                if (!buckets.TryGetValue(family, out var list))
                {
                    list = new List<Row>();
                    buckets[family] = list;
                    order.Add(family);
                }
                list.Add(new Row
                {
                    DisplayName = cap.DisplayName,
                    ModelId = cap.ModelId,
                    Thinking = cap.SupportsThinking,
                    Image = cap.SupportsImageInput,
                    Search = cap.SupportsSearch,
                    Streaming = cap.SupportsStreaming,
                    InputTokens = cap.InputTokenLimit,
                    OutputTokens = cap.OutputTokenLimit,
                    ThinkingBudgetMin = cap.ThinkingBudgetMin,
                    ThinkingBudgetMax = cap.ThinkingBudgetMax,
                    Deprecated = cap.IsDeprecated,
                });
            }

            _sections = new List<Section>();
            foreach (var label in order)
                _sections.Add(new Section { Label = label, Rows = buckets[label] });

            // ── Append CLI provider sections ──
            var cliProviders = new[]
            {
                (type: LLMProviderType.Claude_CLI,  label: "Claude CLI",  stream: true),
                (type: LLMProviderType.Gemini_CLI,  label: "Gemini CLI",  stream: false),
                (type: LLMProviderType.Codex_CLI,   label: "Codex CLI",   stream: true),
            };
            var descriptors = ProviderRegistry.Descriptors;

            foreach (var cli in cliProviders)
            {
                if (!descriptors.TryGetValue(cli.type, out var desc)) continue;
                if (desc.ModelPresets == null || desc.ModelPresets.Length == 0) continue;

                var rows = new List<Row>();
                for (int i = 0; i < desc.ModelPresets.Length; i++)
                {
                    string modelId = desc.ModelPresets[i];
                    if (string.IsNullOrEmpty(modelId)) continue;

                    var cap = ModelCapabilityRegistry.GetCapability(modelId, cli.type);
                    rows.Add(new Row
                    {
                        DisplayName = cap.DisplayName,
                        ModelId = modelId,
                        Thinking = cap.SupportsThinking,
                        Image = cap.SupportsImageInput,
                        Search = cap.SupportsSearch,
                        Streaming = cli.stream,
                        InputTokens = cap.InputTokenLimit,
                        OutputTokens = cap.OutputTokenLimit,
                        ThinkingBudgetMin = cap.ThinkingBudgetMin,
                        ThinkingBudgetMax = cap.ThinkingBudgetMax,
                        Deprecated = cap.IsDeprecated,
                    });
                }

                if (rows.Count > 0)
                    _sections.Add(new Section { Label = cli.label, Rows = rows });
            }

            // ── Image generation models ──
            _imageSections = new List<ImageSection>();

            // Gemini image models
            var geminiImageRows = new List<ImageRow>();
            foreach (var modelId in ProviderRegistry.GeminiImageModelPresets)
            {
                if (string.IsNullOrEmpty(modelId)) continue;
                geminiImageRows.Add(new ImageRow
                {
                    DisplayName = FormatImageModelName(modelId),
                    ModelId = modelId,
                    Provider = "Gemini",
                    OutputSize = M("可変"),
                    EditSupport = true,
                    TimeoutSec = 120,
                });
            }
            if (geminiImageRows.Count > 0)
                _imageSections.Add(new ImageSection { Label = "Gemini", Rows = geminiImageRows });

            // OpenAI image models
            var openaiImageRows = new List<ImageRow>();
            foreach (var modelId in ProviderRegistry.OpenAIImageModelPresets)
            {
                if (string.IsNullOrEmpty(modelId)) continue;
                openaiImageRows.Add(new ImageRow
                {
                    DisplayName = FormatImageModelName(modelId),
                    ModelId = modelId,
                    Provider = "OpenAI",
                    OutputSize = "1024x1024",
                    EditSupport = true,
                    TimeoutSec = 120,
                });
            }
            if (openaiImageRows.Count > 0)
                _imageSections.Add(new ImageSection { Label = "OpenAI", Rows = openaiImageRows });
        }

        private static string FormatImageModelName(string modelId)
        {
            // Simple capitalization for display
            return modelId.Replace("-", " ")
                .Replace("gemini ", "Gemini ")
                .Replace("gpt ", "GPT ")
                .Replace("image ", "Image ")
                .Replace("flash ", "Flash ")
                .Replace("preview", "Preview")
                .Replace("mini", "Mini");
        }

        private static string ClassifyFamily(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Other";

            // Specific prefixes that must match before generic "gpt-"
            if (modelId.StartsWith("gpt-oss")) return "Groq / Ollama";

            foreach (var (prefix, label) in FamilyRules)
            {
                if (modelId.StartsWith(prefix))
                    return label;
            }
            return "Other";
        }

        // ── Build UI ──

        private void BuildUI()
        {
            var scroll = new MD3ScrollColumn(0f, 16f);

            // ── Heading ──
            scroll.Add(new MD3Text(M("すべてのモデル性能"), MD3TextStyle.HeadlineSmall));

            // ── Text generation table ──
            scroll.Add(BuildTextGenHeaders());
            scroll.Add(new MD3Divider());

            for (int s = 0; s < _sections.Count; s++)
            {
                var sec = _sections[s];
                scroll.Add(BuildSectionHeader(sec.Label, s == 0));

                for (int i = 0; i < sec.Rows.Count; i++)
                    scroll.Add(BuildRow(sec.Rows[i], i));
            }

            // ── Image generation table ──
            if (_imageSections != null && _imageSections.Count > 0)
            {
                var spacer = new VisualElement();
                spacer.style.height = 16;
                scroll.Add(spacer);

                scroll.Add(new MD3Text(M("画像生成モデル"), MD3TextStyle.TitleMedium,
                    color: _theme.Tertiary));

                var imgSpacer = new VisualElement();
                imgSpacer.style.height = 4;
                scroll.Add(imgSpacer);

                scroll.Add(BuildImageHeaders());
                scroll.Add(new MD3Divider());

                for (int s = 0; s < _imageSections.Count; s++)
                {
                    var sec = _imageSections[s];
                    scroll.Add(BuildSectionHeader(sec.Label, s == 0));

                    for (int i = 0; i < sec.Rows.Count; i++)
                        scroll.Add(BuildImageRow(sec.Rows[i], i));
                }
            }

            // ── Legend ──
            var legendSpacer = new VisualElement();
            legendSpacer.style.height = 8;
            scroll.Add(legendSpacer);

            scroll.Add(new MD3Text(
                M("\u2713 = 対応  \u2014 = 非対応  \u2020 = 非推奨  思考予算 = min\u2013max tokens"),
                MD3TextStyle.LabelAnnotation,
                color: _theme.OnSurfaceVariant));

            rootVisualElement.Add(scroll);
        }

        // ── Header row for text gen table ──

        private VisualElement BuildTextGenHeaders()
        {
            var row = new MD3Row(0f, Align.Center);
            row.style.paddingLeft = 0;
            row.style.paddingRight = 0;
            row.style.marginTop = 4;
            row.style.marginBottom = 2;

            row.Add(MakeHeaderCell(M("モデル"), 22f));
            row.Add(MakeHeaderCell(M("思考"), 5.5f));
            row.Add(MakeHeaderCell(M("画像"), 5.5f));
            row.Add(MakeHeaderCell(M("検索"), 5.5f));
            row.Add(MakeHeaderCell(M("ストリーム"), 5.5f));
            row.Add(MakeHeaderCell(M("入力"), 9f));
            row.Add(MakeHeaderCell(M("出力"), 9f));
            row.Add(MakeHeaderCell(M("思考予算"), 14f));

            return row;
        }

        // ── Header row for image gen table ──

        private VisualElement BuildImageHeaders()
        {
            var row = new MD3Row(0f, Align.Center);
            row.style.paddingLeft = 0;
            row.style.paddingRight = 0;
            row.style.marginTop = 4;
            row.style.marginBottom = 2;

            row.Add(MakeHeaderCell(M("モデル"), 28f));
            row.Add(MakeHeaderCell(M("プロバイダ"), 12f));
            row.Add(MakeHeaderCell(M("出力サイズ"), 16f));
            row.Add(MakeHeaderCell(M("編集入力"), 10f));
            row.Add(MakeHeaderCell(M("タイムアウト"), 12f));

            return row;
        }

        private VisualElement MakeHeaderCell(string text, float widthPercent)
        {
            var label = new MD3Text(text, MD3TextStyle.LabelCaption, color: _theme.OnSurfaceVariant);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.width = Length.Percent(widthPercent);
            label.style.flexShrink = 0;
            return label;
        }

        // ── Section header ──

        private VisualElement BuildSectionHeader(string label, bool first)
        {
            var container = new VisualElement();
            container.style.marginTop = first ? 4 : 10;
            container.style.marginBottom = 2;
            container.style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
            container.style.alignItems = Align.Center;

            // Left accent bar
            var accent = new VisualElement();
            accent.style.width = 3;
            accent.style.height = 16;
            accent.style.backgroundColor = _theme.Primary;
            accent.style.marginRight = 6;
            accent.style.borderTopLeftRadius = 2;
            accent.style.borderBottomLeftRadius = 2;
            container.Add(accent);

            // Background + label
            var bgRow = new VisualElement();
            bgRow.style.flexGrow = 1;
            bgRow.style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
            bgRow.style.alignItems = Align.Center;
            bgRow.style.height = 20;
            bgRow.style.paddingLeft = 6;
            var bgColor = _theme.PrimaryContainer;
            bgRow.style.backgroundColor = new Color(bgColor.r, bgColor.g, bgColor.b, 0.15f);
            bgRow.style.borderTopRightRadius = 4;
            bgRow.style.borderBottomRightRadius = 4;

            var sectionLabel = new MD3Text(label, MD3TextStyle.TitleSmall, color: _theme.Primary);
            bgRow.Add(sectionLabel);

            container.Add(bgRow);
            return container;
        }

        // ── Data row ──

        private VisualElement BuildRow(Row row, int rowIdx)
        {
            var container = new MD3Row(0f, Align.Center);
            container.style.paddingLeft = 0;
            container.style.paddingRight = 0;
            container.style.height = 20;

            // Zebra stripe
            if (rowIdx % 2 == 1)
            {
                var sc = _theme.SurfaceContainerHigh;
                container.style.backgroundColor = new Color(sc.r, sc.g, sc.b, 0.5f);
            }

            // Deprecated row tint
            if (row.Deprecated)
            {
                var dc = _theme.Error;
                container.style.backgroundColor = new Color(dc.r, dc.g, dc.b, 0.06f);
            }

            // ── Name ──
            string name = row.DisplayName;
            if (row.Deprecated) name += " \u2020";
            var nameLabel = new MD3Text(name, MD3TextStyle.LabelCaption,
                color: row.Deprecated ? _theme.Error : _theme.OnSurface);
            nameLabel.style.width = Length.Percent(22f);
            nameLabel.style.flexShrink = 0;
            nameLabel.style.overflow = Overflow.Hidden;
            container.Add(nameLabel);

            // ── Thinking badge ──
            container.Add(MakeBadgeCell(row.Thinking, _theme.PrimaryContainer,
                _theme.OnPrimaryContainer, 5.5f));

            // ── Image badge ──
            container.Add(MakeBadgeCell(row.Image, _theme.TertiaryContainer,
                _theme.OnTertiaryContainer, 5.5f));

            // ── Search badge ──
            container.Add(MakeBadgeCell(row.Search, _theme.SecondaryContainer,
                _theme.OnSecondaryContainer, 5.5f));

            // ── Streaming badge ──
            container.Add(MakeBadgeCell(row.Streaming, _theme.SurfaceContainerHigh,
                _theme.OnSurface, 5.5f));

            // ── Input tokens ──
            container.Add(MakeTokenCell(row.InputTokens, 9f));

            // ── Output tokens ──
            container.Add(MakeTokenCell(row.OutputTokens, 9f));

            // ── Thinking budget ──
            container.Add(MakeBudgetCell(row, 14f));

            return container;
        }

        // ── Image data row ──

        private VisualElement BuildImageRow(ImageRow row, int rowIdx)
        {
            var container = new MD3Row(0f, Align.Center);
            container.style.paddingLeft = 0;
            container.style.paddingRight = 0;
            container.style.height = 20;

            // Zebra stripe
            if (rowIdx % 2 == 1)
            {
                var sc = _theme.SurfaceContainerHigh;
                container.style.backgroundColor = new Color(sc.r, sc.g, sc.b, 0.5f);
            }

            // ── Name ──
            var nameLabel = new MD3Text(row.DisplayName, MD3TextStyle.LabelCaption,
                color: _theme.OnSurface);
            nameLabel.style.width = Length.Percent(28f);
            nameLabel.style.flexShrink = 0;
            nameLabel.style.overflow = Overflow.Hidden;
            container.Add(nameLabel);

            // ── Provider ──
            Color provColor = row.Provider == "Gemini" ? _theme.Primary : _theme.Tertiary;
            var provLabel = new MD3Text(row.Provider, MD3TextStyle.LabelAnnotation, color: provColor);
            provLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            provLabel.style.width = Length.Percent(12f);
            provLabel.style.flexShrink = 0;
            container.Add(provLabel);

            // ── Output size ──
            var sizeLabel = new MD3Text(row.OutputSize, MD3TextStyle.LabelAnnotation,
                color: _theme.OnSurface);
            sizeLabel.style.width = Length.Percent(16f);
            sizeLabel.style.flexShrink = 0;
            container.Add(sizeLabel);

            // ── Edit support ──
            container.Add(MakeBadgeCell(row.EditSupport, _theme.TertiaryContainer,
                _theme.OnTertiaryContainer, 10f));

            // ── Timeout ──
            var toutLabel = new MD3Text(row.TimeoutSec + "s", MD3TextStyle.LabelAnnotation,
                color: _theme.OnSurface);
            toutLabel.style.width = Length.Percent(12f);
            toutLabel.style.flexShrink = 0;
            container.Add(toutLabel);

            return container;
        }

        // ── Cell helpers ──

        private VisualElement MakeBadgeCell(bool supported, Color bgOn, Color fgOn, float widthPercent)
        {
            var cell = new VisualElement();
            cell.style.width = Length.Percent(widthPercent);
            cell.style.flexShrink = 0;
            cell.style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;

            var badge = new VisualElement();
            badge.style.width = 28;
            badge.style.height = 16;
            badge.style.borderTopLeftRadius = 4;
            badge.style.borderTopRightRadius = 4;
            badge.style.borderBottomLeftRadius = 4;
            badge.style.borderBottomRightRadius = 4;
            badge.style.justifyContent = Justify.Center;
            badge.style.alignItems = Align.Center;

            if (supported)
                badge.style.backgroundColor = bgOn;

            var label = new Label(supported ? "\u2713" : "\u2014");
            label.style.fontSize = 10;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = supported ? fgOn : _theme.OnSurfaceVariant;
            if (supported)
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            badge.Add(label);

            cell.Add(badge);
            return cell;
        }

        private VisualElement MakeTokenCell(int tokens, float widthPercent)
        {
            var color = TokenColor(tokens);
            var label = new MD3Text(Tok(tokens), MD3TextStyle.LabelAnnotation, color: color);
            label.style.width = Length.Percent(widthPercent);
            label.style.flexShrink = 0;
            return label;
        }

        private VisualElement MakeBudgetCell(Row row, float widthPercent)
        {
            string budgetText;
            Color budgetColor;
            if (!row.Thinking)
            {
                budgetText = "\u2014";
                budgetColor = _theme.OnSurfaceVariant;
            }
            else if (row.ThinkingBudgetMax <= 0)
            {
                budgetText = "Effort";
                budgetColor = _theme.Tertiary;
            }
            else
            {
                budgetText = Tok(row.ThinkingBudgetMin) + "\u2013" + Tok(row.ThinkingBudgetMax);
                budgetColor = _theme.Secondary;
            }

            var label = new MD3Text(budgetText, MD3TextStyle.LabelAnnotation, color: budgetColor);
            label.style.width = Length.Percent(widthPercent);
            label.style.flexShrink = 0;
            return label;
        }

        // ── Utility ──

        private static string Tok(int t)
        {
            if (t >= 1000000) return (t / 1000000f).ToString("0.##") + "M";
            if (t >= 1000)    return (t / 1000f).ToString("0.#") + "K";
            return t.ToString();
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

        private Color TokenColor(int tokens)
        {
            if (tokens >= 200000) return _theme.Primary;
            if (tokens >= 100000) return _theme.Secondary;
            if (tokens >= 32000)  return _theme.OnSurface;
            return _theme.OnSurfaceVariant;
        }
    }
}
