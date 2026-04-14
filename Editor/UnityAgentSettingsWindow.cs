using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Providers;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;
using AjisaiFlow.UnityAgent.Editor.MCP;
using static AjisaiFlow.UnityAgent.Editor.L10n;


namespace AjisaiFlow.UnityAgent.Editor
{
    public class UnityAgentSettingsWindow : EditorWindow
    {
        // ═══════════════════════════════════════════════════════
        //  Theme
        // ═══════════════════════════════════════════════════════

        private MD3Theme _theme;
        private int _settingsTabIndex;

        // ═══════════════════════════════════════════════════════
        //  Settings — Provider (registry-based)
        // ═══════════════════════════════════════════════════════

        private LLMProviderType _providerType;
        private Dictionary<LLMProviderType, ProviderConfig> _configs;
        private bool _useThinking;
        private int _thinkingBudget = 8192;
        private int _effortLevel = 2;
        private int _imageProviderType; // 0=Gemini, 1=OpenAI
        private string _imageModelName = "gemini-2.5-flash-image";
        private bool _useCustomImageModel;
        private string _imageApiKey = "";
        private int _imageConnectionMode; // 0=Google AI, 1=Vertex AI, 2=Custom
        private string _imageApiVersion = "v1beta";
        private string _imageCustomEndpoint = "";
        private string _imageProjectId = "";
        private string _imageLocation = "us-central1";
        // OpenAI 画像生成設定
        private string _openaiImageApiKey = "";
        private string _openaiImageModelName = "gpt-image-1";
        private string _openaiImageBaseUrl = "https://api.openai.com";
        private bool _useCustomOpenAIImageModel;
        private string _meshyApiKey = "";
        private bool _isFetchingGeminiModels;

        // MCP server config (editing state)
        private List<MCPServerConfig> _mcpServers = new List<MCPServerConfig>();
        private bool _mcpLoaded;

        // Theme customization
        private int _themeMode;
        private MD3Theme _customTheme;
        private bool _customThemeLoaded;
        private Color _seedColor;

        // Chrome Web Store availability check
        private static bool _storeCheckDone;
        private static bool _storeAvailable;
        private static UnityEngine.Networking.UnityWebRequest _storeCheckRequest;

        private const string ChromeStoreUrl =
            "https://chromewebstore.google.com/detail/enifhlicgonefacopanegifncpipiaam";

        // Update check dialog
        private VersionInfo _updateVersionInfo;

        // Snackbar anchor
        private VisualElement _snackbarAnchor;

        // Right panel

        // Content panels (cached for switching)
        private VisualElement _contentArea;
        private VisualElement _rightPanel;

        // ═══════════════════════════════════════════════════════
        //  Open / Lifecycle
        // ═══════════════════════════════════════════════════════

        public static void Open()
        {
            var window = GetWindow<UnityAgentSettingsWindow>();
            window.titleContent = new GUIContent(M("設定"));
            window.minSize = new Vector2(760, 500);
            window.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        // ═══════════════════════════════════════════════════════
        //  CreateGUI
        // ═══════════════════════════════════════════════════════

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

            if (_configs == null) LoadSettings();

            BuildLayout();
        }

        // ═══════════════════════════════════════════════════════
        //  Layout
        // ═══════════════════════════════════════════════════════

        private void BuildLayout()
        {
            var root = rootVisualElement;

            // Main horizontal split
            var hSplit = new VisualElement();
            hSplit.style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
            hSplit.style.flexGrow = 1;
            root.Add(hSplit);

            // ── Left column ──
            var leftCol = new VisualElement();
            leftCol.style.flexGrow = 1;
            leftCol.style.flexBasis = new StyleLength(new Length(50, LengthUnit.Percent));
            hSplit.Add(leftCol);

            // Tab bar
            var tabBar = new MD3Row(8);
            tabBar.style.paddingLeft = 8;
            tabBar.style.paddingRight = 8;
            tabBar.style.paddingTop = 8;
            tabBar.style.paddingBottom = 4;

            var tabLabels = new[] { M("一般"), M("プロバイダ"), M("詳細"), M("テーマ"), "MCP" };
            var segmented = new MD3SegmentedButton(tabLabels, _settingsTabIndex);
            segmented.style.flexGrow = 1;
            segmented.style.maxWidth = 560;
            segmented.changed += idx =>
            {
                _settingsTabIndex = idx;
                RebuildContentArea();
            };
            tabBar.Add(segmented);
            leftCol.Add(tabBar);

            // Scrollable content area
            _contentArea = new VisualElement();
            _contentArea.style.flexGrow = 1;
            leftCol.Add(_contentArea);

            // Vertical divider
            var divider = new VisualElement();
            divider.style.width = 1;
            divider.style.backgroundColor = _theme.OutlineVariant;
            hSplit.Add(divider);

            // ── Right column ──
            _rightPanel = new VisualElement();
            _rightPanel.style.flexBasis = new StyleLength(new Length(50, LengthUnit.Percent));
            _rightPanel.style.backgroundColor = _theme.SurfaceContainerHigh;
            hSplit.Add(_rightPanel);

            BuildRightPanel();
            RebuildContentArea();

            // Snackbar anchor (root itself — MD3Snackbar uses absolute positioning)
            _snackbarAnchor = root;
        }

        private void RebuildContentArea()
        {
            _contentArea.Clear();

            var scroll = new MD3ScrollColumn(8, 12);
            _contentArea.Add(scroll);

            switch (_settingsTabIndex)
            {
                case 0: BuildGeneralTab(scroll); break;
                case 1: BuildProviderTab(scroll); break;
                case 2: BuildAdvancedTab(scroll); break;
                case 3: BuildThemeTab(scroll); break;
                case 4: BuildMCPTab(scroll); break;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  General tab
        // ═══════════════════════════════════════════════════════

        private void BuildGeneralTab(VisualElement parent)
        {
            AddHeading(parent, M("一般設定"));

            // ── Language ──
            AddSectionLabel(parent, M("言語"), M("UIの表示言語を選択します。「自動検出」はシステム言語に従います。"));

            string currentLang = AgentSettings.UILanguageRaw;
            var languages = AgentSettings.SupportedLanguages;

            var displayNames = new string[languages.Length + 1];
            displayNames[0] = M("自動検出") + $" ({AgentSettings.UILanguage})";
            int selectedIdx = 0;
            for (int i = 0; i < languages.Length; i++)
            {
                displayNames[i + 1] = languages[i].label;
                if (currentLang == languages[i].code)
                    selectedIdx = i + 1;
            }

            var langDropdown = new MD3Dropdown(M("言語"), displayNames, selectedIdx);
            langDropdown.style.marginLeft = 12;
            langDropdown.style.marginRight = 12;
            langDropdown.changed += idx =>
            {
                if (idx == 0)
                    AgentSettings.UILanguageRaw = "auto";
                else
                    AgentSettings.UILanguageRaw = languages[idx - 1].code;
                EditorApplication.delayCall += () => { L10n.Reload(); CreateGUI(); };
            };
            parent.Add(langDropdown);

            // Translation manager button
            var transBtn = new MD3Button(M("翻訳マネージャーを開く"), MD3ButtonStyle.Outlined);
            transBtn.style.marginLeft = 12;
            transBtn.style.marginRight = 12;
            transBtn.style.marginTop = 8;
            transBtn.clicked += () => TranslationManagementWindow.Open();
            parent.Add(transBtn);

            AddDivider(parent);

            // ── Context tokens ──
            AddSectionLabel(parent, M("コンテキスト"), M("AIに送信する会話履歴の最大トークン数です。大きいほど長い会話を記憶しますが、コストが増加します。"));

            var cap = GetActiveModelCapability();
            int inputLimit = cap.InputTokenLimit > 0 ? cap.InputTokenLimit : 2000000;
            int maxCtx = _configs[_providerType].MaxContextTokens;
            int effectiveCtx = AgentSettings.ResolveMaxContextTokens(maxCtx, cap.InputTokenLimit);

            string valStr = maxCtx == 0
                ? M("自動") + $" ({FormatTokenCount(effectiveCtx)})"
                : FormatTokenCount(maxCtx);
            string limitStr = inputLimit >= 1000000
                ? (inputLimit / 1000000f).ToString("0.##") + "M"
                : (inputLimit / 1000f).ToString("0") + "K";

            var ctxLabel = new MD3Text($"{valStr} / {limitStr} tokens", MD3TextStyle.LabelLarge,
                color: _theme.Primary);
            ctxLabel.style.marginLeft = 12;
            ctxLabel.style.marginRight = 12;
            parent.Add(ctxLabel);

            var ctxSlider = new MD3Slider(maxCtx == 0 ? effectiveCtx : maxCtx, 0, inputLimit, 10000);
            ctxSlider.style.marginLeft = 12;
            ctxSlider.style.marginRight = 12;
            ctxSlider.changed += v =>
            {
                int newMaxCtx = Mathf.RoundToInt(v / 10000f) * 10000;
                newMaxCtx = Mathf.Clamp(newMaxCtx, 0, inputLimit);
                _configs[_providerType].MaxContextTokens = newMaxCtx;
                SaveSettings();
                string newValStr = newMaxCtx == 0
                    ? M("自動") + $" ({FormatTokenCount(AgentSettings.ResolveMaxContextTokens(newMaxCtx, cap.InputTokenLimit))})"
                    : FormatTokenCount(newMaxCtx);
                ctxLabel.Text = $"{newValStr} / {limitStr} tokens";
            };
            parent.Add(ctxSlider);

            AddDivider(parent);

            // ── Discord logging ──
            AddSectionLabel(parent, M("AIとのチャットを開発者に共有することを許可する"),
                M("有効にすると会話ログが開発者のDiscordに送信されます。ツールの改善に活用されます。"));

            AddSwitchRow(parent, AgentSettings.DiscordLoggingEnabled,
                enabled => enabled ? M("有効 — 会話ログが開発者に送信されます") : M("無効"),
                newVal =>
                {
                    if (newVal)
                    {
                        // Show confirmation dialog
                        var dialog = new MD3Dialog(
                            M("チャット共有の確認"),
                            M("この機能を有効にすると、AIとの会話セッション内容が開発者に匿名で送信されます。\n\n" +
                              "プロンプトの改善に使用され、大幅に精度が高くなることが期待されますが、開発者に会話内容を評価されます。\n\n" +
                              "通常は有効にする必要はなく、開発の支援をしたい方のみ有効にできます。\n\n" +
                              "この機能が有効になっている間は個人情報を含むチャットを行わないようにしてください。"),
                            confirmLabel: M("理解して有効にする"),
                            dismissLabel: M("キャンセル"),
                            onConfirm: () =>
                            {
                                AgentSettings.DiscordLoggingEnabled = true;
                                RebuildContentArea();
                            });
                        dialog.Show(rootVisualElement);
                        // Revert the switch — dialog will set if confirmed
                        return false;
                    }
                    else
                    {
                        AgentSettings.DiscordLoggingEnabled = false;
                        return false;
                    }
                });

            AddDivider(parent);

            // ── Debug mode ──
            AddSectionLabel(parent, M("デバッグモード"),
                M("有効にするとチャットUI上でAPIリクエスト/レスポンスの詳細を折りたたみセクションとして表示します。"));

            AddSwitchRow(parent, AgentSettings.DebugMode,
                enabled => enabled ? M("有効 — デバッグ情報がチャットに表示されます") : M("無効"),
                newVal => { AgentSettings.DebugMode = newVal; return newVal; });

            AddDivider(parent);

            // ── Remote HTTP ──
            AddSectionLabel(parent, M("リモート HTTP 接続"),
                M("localhost・LAN (192.168.*) 以外の HTTP URL への接続を許可します。外部のプロキシサーバー等を使う場合に有効にしてください。"));

            AddSwitchRow(parent, AgentSettings.AllowRemoteInsecureHttp,
                enabled => enabled ? M("有効 — リモート HTTP 接続を許可") : M("無効 — ローカルのみ許可"),
                newVal => { AgentSettings.AllowRemoteInsecureHttp = newVal; return newVal; });

            AddDivider(parent);

            // ── Web server ──
            AddSectionLabel(parent, M("Web サーバー"),
                M("外部ツールからHTTP経由でエージェントを操作するためのサーバー設定です。"));

            AddTextField(parent, M("ポート"), AgentSettings.WebServerPort.ToString(), v =>
            {
                if (int.TryParse(v, out int newPort) && newPort > 0 && newPort <= 65535)
                    AgentSettings.WebServerPort = newPort;
            });

            AddTextField(parent, M("ユーザー名"), AgentSettings.WebServerUsername, v =>
                AgentSettings.WebServerUsername = v);

            AddPasswordField(parent, M("パスワード"), AgentSettings.WebServerPassword, v =>
                AgentSettings.WebServerPassword = v);
        }

        // ═══════════════════════════════════════════════════════
        //  Provider tab
        // ═══════════════════════════════════════════════════════

        private void BuildProviderTab(VisualElement parent)
        {
            AddHeading(parent, M("プロバイダ設定"));

            // "All Models" button
            var allModelsBtn = new MD3Button(M("すべてのモデル性能"), MD3ButtonStyle.Tonal);
            allModelsBtn.style.marginLeft = 12;
            allModelsBtn.style.marginRight = 12;
            allModelsBtn.clicked += () => ModelCapabilityWindow.Open();
            parent.Add(allModelsBtn);

            AddSectionLabel(parent, M("プロバイダ選択"),
                M("使用するAIプロバイダを選択します。各プロバイダにはAPIキーが必要です。"));

            // Provider selector
            var providerDropdown = new MD3Dropdown(M("プロバイダ"),
                ProviderRegistry.ProviderDisplayNames, (int)_providerType);
            providerDropdown.style.marginLeft = 12;
            providerDropdown.style.marginRight = 12;
            providerDropdown.changed += idx =>
            {
                _providerType = (LLMProviderType)idx;
                SaveSettings();
                RebuildContentArea();
            };
            parent.Add(providerDropdown);

            AddDivider(parent);

            // Provider-specific settings
            BuildProviderSettings(parent, _providerType);

            // Thinking mode
            AddDivider(parent);
            BuildThinkingModeSection(parent);
        }

        // ═══════════════════════════════════════════════════════
        //  Advanced tab
        // ═══════════════════════════════════════════════════════

        private enum ThinkingUIMode { Budget, Effort, None }
        private static readonly string[] EffortLabels = { "Low", "Medium", "High" };

        private static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000000)
                return (tokens / 1000000f).ToString("0.##") + "M";
            if (tokens >= 1000)
                return (tokens / 1000f).ToString("0.#") + "K";
            return tokens.ToString();
        }

        private ModelCapability GetActiveModelCapability()
        {
            var cfg = _configs[_providerType];
            string modelName = cfg.ModelName;
            if (string.IsNullOrEmpty(modelName))
                modelName = ResolveEffectiveModelName(_providerType);
            return ModelCapabilityRegistry.GetCapability(modelName, _providerType);
        }

        private static string ResolveEffectiveModelName(LLMProviderType type)
        {
            switch (type)
            {
                case LLMProviderType.Claude_API:  return "claude-sonnet-4-6";
                case LLMProviderType.Claude_CLI:  return "claude-sonnet-4-6";
                case LLMProviderType.Gemini:      return "gemini-2.0-flash";
                case LLMProviderType.Vertex_AI:   return "gemini-2.0-flash";
                case LLMProviderType.Gemini_CLI:  return "gemini-2.5-flash";
                case LLMProviderType.Codex_CLI:   return "gpt-4.1";
                default:
                    var desc = ProviderRegistry.Get(type);
                    return desc.DefaultModel ?? "";
            }
        }

        private ThinkingUIMode GetThinkingUIMode(out string hint)
        {
            var desc = ProviderRegistry.Get(_providerType);
            var cap = GetActiveModelCapability();

            if (cap.SupportsThinking)
            {
                if (cap.ThinkingBudgetMax > 0)
                {
                    hint = $"{cap.DisplayName}: {M("思考モード対応")} ({FormatTokenCount(cap.ThinkingBudgetMin)}–{FormatTokenCount(cap.ThinkingBudgetMax)})";
                    return ThinkingUIMode.Budget;
                }
                else
                {
                    hint = $"{cap.DisplayName}: {M("思考モード対応")} (effort)";
                    return ThinkingUIMode.Effort;
                }
            }

            if (desc.ThinkingMode != ThinkingMode.None)
            {
                hint = $"{cap.DisplayName}: {M("このモデルでは思考モード非対応")}";
                return ThinkingUIMode.None;
            }

            hint = M("現在のプロバイダーでは利用できません");
            return ThinkingUIMode.None;
        }

        private void BuildAdvancedTab(VisualElement parent)
        {
            AddHeading(parent, M("詳細設定"));
            BuildImageModelSection(parent);
            AddDivider(parent);
            BuildMeshySettings(parent);
            AddDivider(parent);

            AddSectionLabel(parent, M("ログ"), M("UnityAgentの内部ログを確認できます。"));
            var logBtn = new MD3Button(M("ログウィンドウを開く"), MD3ButtonStyle.Outlined);
            logBtn.style.marginLeft = 12;
            logBtn.style.marginRight = 12;
            logBtn.style.marginTop = 8;
            logBtn.clicked += () => AgentLogWindow.Open();
            parent.Add(logBtn);
        }

        private void BuildThinkingModeSection(VisualElement parent)
        {
            var uiMode = GetThinkingUIMode(out string thinkingHint);
            bool supported = uiMode != ThinkingUIMode.None;

            AddSectionLabel(parent, M("思考モード"),
                M("AIが回答前に内部で推論ステップを実行します。精度が向上しますが、応答が遅くなりコストも増えます。"));

            // Hint
            var hintText = new MD3Text(
                (supported ? "\u2713 " : "\u2717 ") + thinkingHint,
                MD3TextStyle.BodySmall,
                color: supported ? _theme.Primary : _theme.Error);
            hintText.style.marginLeft = 16;
            hintText.style.marginRight = 12;
            parent.Add(hintText);

            // Thinking toggle
            if (supported)
            {
                AddSwitchRow(parent, _useThinking,
                    enabled => enabled ? M("有効") : M("無効"),
                    newVal => { _useThinking = newVal; SaveSettings(); RebuildContentArea(); return newVal; });

                if (_useThinking)
                {
                    if (uiMode == ThinkingUIMode.Effort)
                    {
                        // Effort selector
                        var effortRow = new MD3Row(8);
                        effortRow.style.marginLeft = 12;
                        effortRow.style.marginRight = 12;
                        effortRow.style.marginTop = 8;

                        var effortLabel = new MD3Text(M("Effort レベル"), MD3TextStyle.Body);
                        effortRow.Add(effortLabel);

                        var effortSeg = new MD3SegmentedButton(EffortLabels, _effortLevel);
                        effortSeg.style.maxWidth = 240;
                        effortSeg.changed += idx =>
                        {
                            _effortLevel = idx;
                            SaveSettings();
                        };
                        effortRow.Add(effortSeg);
                        parent.Add(effortRow);
                    }
                    else // Budget
                    {
                        var budgetCap = GetActiveModelCapability();
                        int budgetMin = Mathf.Max(1, budgetCap.ThinkingBudgetMin);
                        int budgetMax = budgetCap.ThinkingBudgetMax > 0 ? budgetCap.ThinkingBudgetMax : 128000;
                        _thinkingBudget = Mathf.Clamp(_thinkingBudget, budgetMin, budgetMax);

                        var budgetLabel = new MD3Text(
                            $"{M("思考バジェット")}: {FormatTokenCount(_thinkingBudget)} / {FormatTokenCount(budgetMax)} tokens",
                            MD3TextStyle.LabelLarge, color: _theme.Primary);
                        budgetLabel.style.marginLeft = 12;
                        budgetLabel.style.marginRight = 12;
                        parent.Add(budgetLabel);

                        var budgetSlider = new MD3Slider(_thinkingBudget, budgetMin, budgetMax, 1024);
                        budgetSlider.style.marginLeft = 12;
                        budgetSlider.style.marginRight = 12;
                        budgetSlider.changed += v =>
                        {
                            int newBudget = Mathf.RoundToInt(v / 1024f) * 1024;
                            newBudget = Mathf.Clamp(newBudget, budgetMin, budgetMax);
                            if (newBudget != _thinkingBudget)
                            {
                                _thinkingBudget = newBudget;
                                SaveSettings();
                                budgetLabel.Text = $"{M("思考バジェット")}: {FormatTokenCount(_thinkingBudget)} / {FormatTokenCount(budgetMax)} tokens";
                            }
                        };
                        parent.Add(budgetSlider);
                    }
                }
            }
            else
            {
                // Disabled switch
                var sw = new MD3Switch(_useThinking);
                sw.SetEnabled(false);
                sw.style.marginLeft = 12;
                parent.Add(sw);
            }
        }

        private void BuildImageModelSection(VisualElement parent)
        {
            AddSectionLabel(parent, M("画像生成プロバイダ"),
                M("テクスチャ生成に使用する画像生成 API の設定です。"));

            // Image provider type
            var imgProvDropdown = new MD3Dropdown(M("プロバイダー"),
                ProviderRegistry.ImageProviderDisplayNames, _imageProviderType);
            imgProvDropdown.style.marginLeft = 12;
            imgProvDropdown.style.marginRight = 12;
            imgProvDropdown.changed += idx =>
            {
                _imageProviderType = idx;
                SaveSettings();
                RebuildContentArea();
            };
            parent.Add(imgProvDropdown);

            if (_imageProviderType == 0) // Gemini
                BuildGeminiImageSubsection(parent);
            else if (_imageProviderType == 1) // OpenAI
                BuildOpenAIImageSubsection(parent);
        }

        private void BuildGeminiImageSubsection(VisualElement parent)
        {
            // Connection mode
            var modes = new[] { "Google AI", "Vertex AI", M("カスタム") };
            var connDropdown = new MD3Dropdown(M("接続モード"), modes, _imageConnectionMode);
            connDropdown.style.marginLeft = 12;
            connDropdown.style.marginRight = 12;
            connDropdown.style.marginTop = 8;
            connDropdown.changed += idx =>
            {
                _imageConnectionMode = idx;
                SaveSettings();
                RebuildContentArea();
            };
            parent.Add(connDropdown);

            // API Key
            AddPasswordField(parent, M("API Key"), _imageApiKey, v =>
            {
                _imageApiKey = v;
                SaveSettings();
            });

            if (_imageConnectionMode == 2) // Custom
            {
                AddTextField(parent, M("エンドポイント URL"), _imageCustomEndpoint, v =>
                {
                    _imageCustomEndpoint = v;
                    SaveSettings();
                });
            }
            else if (_imageConnectionMode == 1) // Vertex AI
            {
                AddTextField(parent, M("プロジェクト ID"), _imageProjectId, v =>
                {
                    _imageProjectId = v;
                    SaveSettings();
                });

                int locIdx = Array.IndexOf(ProviderRegistry.VertexAILocationOptions, _imageLocation);
                if (locIdx < 0) locIdx = 0;
                var locDropdown = new MD3Dropdown(M("リージョン"),
                    ProviderRegistry.VertexAILocationOptions, locIdx);
                locDropdown.style.marginLeft = 12;
                locDropdown.style.marginRight = 12;
                locDropdown.style.marginTop = 8;
                locDropdown.changed += idx =>
                {
                    _imageLocation = ProviderRegistry.VertexAILocationOptions[idx];
                    SaveSettings();
                };
                parent.Add(locDropdown);
            }

            // Image model — custom toggle + dropdown/textfield
            BuildImageModelSelector(parent, M("画像モデル"), ref _imageModelName, ref _useCustomImageModel,
                ProviderRegistry.GeminiImageModelPresets);
        }

        private void BuildOpenAIImageSubsection(VisualElement parent)
        {
            AddPasswordField(parent, M("API Key"), _openaiImageApiKey, v =>
            {
                _openaiImageApiKey = v;
                SaveSettings();
            });

            AddTextField(parent, "Base URL", _openaiImageBaseUrl, v =>
            {
                _openaiImageBaseUrl = v;
                SaveSettings();
            });

            BuildImageModelSelector(parent, M("画像モデル"), ref _openaiImageModelName, ref _useCustomOpenAIImageModel,
                ProviderRegistry.OpenAIImageModelPresets);
        }

        private void BuildImageModelSelector(VisualElement parent, string label,
            ref string modelName, ref bool useCustom, string[] presets)
        {
            // Capture by value for closures
            string currentModel = modelName;
            bool currentCustom = useCustom;

            var customRow = new MD3Row(8);
            customRow.style.marginLeft = 12;
            customRow.style.marginRight = 12;
            customRow.style.marginTop = 12;

            var customSwitch = new MD3Switch(currentCustom);
            customRow.Add(customSwitch);
            var customLabel = new MD3Text(M("カスタムモデル"), MD3TextStyle.Body);
            customRow.Add(customLabel);
            parent.Add(customRow);

            if (currentCustom)
            {
                var tf = new MD3TextField(label);
                tf.Value = currentModel;
                tf.style.marginLeft = 12;
                tf.style.marginRight = 12;
                tf.style.marginTop = 4;
                tf.changed += v =>
                {
                    // We need to use a field reference since ref params can't be captured
                    // This is handled by SaveSettings reading from fields
                    if (presets == ProviderRegistry.GeminiImageModelPresets)
                        _imageModelName = v;
                    else
                        _openaiImageModelName = v;
                    SaveSettings();
                };
                parent.Add(tf);
            }
            else
            {
                int idx = Array.IndexOf(presets, currentModel);
                if (idx < 0) idx = 0;
                var dd = new MD3Dropdown(label, presets, idx);
                dd.style.marginLeft = 12;
                dd.style.marginRight = 12;
                dd.style.marginTop = 4;
                dd.changed += i =>
                {
                    if (presets == ProviderRegistry.GeminiImageModelPresets)
                        _imageModelName = presets[i];
                    else
                        _openaiImageModelName = presets[i];
                    SaveSettings();
                };
                parent.Add(dd);
            }

            customSwitch.changed += newVal =>
            {
                if (presets == ProviderRegistry.GeminiImageModelPresets)
                {
                    _useCustomImageModel = newVal;
                    if (!newVal && Array.IndexOf(presets, _imageModelName) < 0)
                        _imageModelName = presets[0];
                }
                else
                {
                    _useCustomOpenAIImageModel = newVal;
                    if (!newVal && Array.IndexOf(presets, _openaiImageModelName) < 0)
                        _openaiImageModelName = presets[0];
                }
                SaveSettings();
                RebuildContentArea();
            };
        }

        private void BuildMeshySettings(VisualElement parent)
        {
            AddSectionLabel(parent,
                "Meshy API (3D " + M("生成") + ")",
                M("テキストや画像から3Dメッシュを生成します。APIキーは meshy.ai/settings/api から取得できます。"));

            AddPasswordField(parent, "Meshy API Key", _meshyApiKey, v =>
            {
                _meshyApiKey = v;
                SaveSettings();
            });
        }

        // ═══════════════════════════════════════════════════════
        //  Provider settings — unified dispatch
        // ═══════════════════════════════════════════════════════

        private void BuildProviderSettings(VisualElement parent, LLMProviderType type)
        {
            var desc = ProviderRegistry.Get(type);
            var cfg = _configs[type];

            // MCP Server プロバイダーは LLM 呼び出しを行わないので、情報表示のみ
            if (type == LLMProviderType.MCPServer)
            {
                BuildMCPServerProviderInfo(parent, desc);
                return;
            }

            switch (desc.SettingsKind)
            {
                case ProviderSettingsKind.Gemini:
                    BuildGeminiSettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.VertexAI:
                    BuildVertexAISettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.OpenAICompatibleApiKey:
                    BuildOpenAICompatibleApiKeySettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.OpenAICompatibleUrl:
                    BuildOpenAICompatibleUrlSettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.ClaudeApi:
                    BuildClaudeApiSettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.CliProvider:
                    BuildCliProviderSettings(parent, cfg, desc);
                    break;
                case ProviderSettingsKind.BrowserBridge:
                    BuildGeminiWebSettings(parent, cfg);
                    break;
                case ProviderSettingsKind.Clipboard:
                    BuildClipboardSettings(parent, desc);
                    break;
            }
        }

        // ─── MCP Server provider info ───

        private void BuildMCPServerProviderInfo(VisualElement parent, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle, M(desc.DescriptionKey));

            bool serverRunning = MCP.AgentMCPServer.Shared != null && MCP.AgentMCPServer.Shared.IsRunning;
            string endpoint = serverRunning
                ? MCP.AgentMCPServer.Shared.Endpoint
                : $"http://localhost:{AgentSettings.MCPServerPort}/mcp";

            var statusText = serverRunning
                ? $"● Running — {endpoint}"
                : M("○ 停止中 — MCP タブで有効化してください");
            var statusLabel = new MD3Text(statusText,
                MD3TextStyle.LabelLarge,
                color: serverRunning ? _theme.Primary : _theme.OnSurfaceVariant);
            statusLabel.style.marginLeft = 12;
            statusLabel.style.marginRight = 12;
            statusLabel.style.marginTop = 8;
            parent.Add(statusLabel);

            var hint = new MD3Text(
                M("このモードを選択すると UnityAgent はチャット入力を受け付けません。"
                  + " 外部エージェントからの MCP 経由のツール呼び出しのみを処理します。"
                  + " AskUser 等のユーザー対話 (メッシュ選択、確認ダイアログ) は引き続き動作します。"),
                MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant);
            hint.style.marginLeft = 12;
            hint.style.marginRight = 12;
            hint.style.marginTop = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(hint);
        }

        // ─── Gemini ───

        private void BuildGeminiSettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            // Connection mode
            var modes = new[] { "Google AI", M("カスタム") };
            int modeIdx = cfg.GeminiMode == GeminiConnectionMode.Custom ? 1 : 0;
            var connDropdown = new MD3Dropdown(M("接続モード"), modes, modeIdx);
            connDropdown.style.marginLeft = 12;
            connDropdown.style.marginRight = 12;
            connDropdown.changed += idx =>
            {
                cfg.GeminiMode = idx == 1 ? GeminiConnectionMode.Custom : GeminiConnectionMode.GoogleAI;
                SaveSettings();
                RebuildContentArea();
            };
            parent.Add(connDropdown);

            AddPasswordField(parent, M("API Key"), cfg.ApiKey, v =>
            {
                cfg.ApiKey = v;
                SaveSettings();
            });

            if (cfg.GeminiMode == GeminiConnectionMode.Custom)
            {
                AddTextField(parent, M("エンドポイント URL"), cfg.CustomEndpoint, v =>
                {
                    cfg.CustomEndpoint = v;
                    SaveSettings();
                });
            }
            else
            {
                // API version dropdown
                int verIdx = Array.IndexOf(ProviderRegistry.GoogleAIApiVersions, cfg.ApiVersion);
                if (verIdx < 0) verIdx = 0;
                var verDropdown = new MD3Dropdown(M("API バージョン"),
                    ProviderRegistry.GoogleAIApiVersionLabels, verIdx);
                verDropdown.style.marginLeft = 12;
                verDropdown.style.marginRight = 12;
                verDropdown.style.marginTop = 8;
                verDropdown.changed += idx =>
                {
                    cfg.ApiVersion = ProviderRegistry.GoogleAIApiVersions[idx];
                    SaveSettings();
                };
                parent.Add(verDropdown);

                // Model selector
                BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);

                // Refresh models button
                var refreshRow = new MD3Row(8);
                refreshRow.style.marginLeft = 12;
                refreshRow.style.marginRight = 12;
                refreshRow.style.marginTop = 8;

                var refreshBtn = new MD3Button(
                    _isFetchingGeminiModels ? M("取得中...") : M("モデル一覧を更新"),
                    MD3ButtonStyle.Tonal);
                refreshBtn.IsDisabled = string.IsNullOrEmpty(cfg.ApiKey) || _isFetchingGeminiModels;
                refreshBtn.clicked += () =>
                {
                    _isFetchingGeminiModels = true;
                    RebuildContentArea();
                    EditorCoroutineUtility.StartCoroutineOwnerless(
                        ModelCapabilityRegistry.FetchGeminiModels(cfg.ApiKey, cfg.ApiVersion, () =>
                        {
                            _isFetchingGeminiModels = false;
                            if (ModelCapabilityRegistry.HasDynamicGeminiModels)
                                ShowSnackbar(M("モデル一覧を更新しました"));
                            RebuildContentArea();
                        }));
                };
                refreshRow.Add(refreshBtn);

                if (ModelCapabilityRegistry.HasDynamicGeminiModels)
                {
                    var countLabel = new MD3Text(
                        $"{ModelCapabilityRegistry.GetDynamicGeminiModelIds().Length} {M("モデル取得済み")}",
                        MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
                    refreshRow.Add(countLabel);
                }
                parent.Add(refreshRow);
            }

            BuildGeminiFeatures(parent, cfg);
        }

        // ─── Vertex AI ───

        private void BuildVertexAISettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            AddPasswordField(parent, M("API Key"), cfg.ApiKey, v =>
            {
                cfg.ApiKey = v;
                SaveSettings();
            });

            AddTextField(parent, M("プロジェクト ID"), cfg.ProjectId, v =>
            {
                cfg.ProjectId = v;
                SaveSettings();
            });

            // Region
            int locIdx = Array.IndexOf(ProviderRegistry.VertexAILocationOptions, cfg.Location);
            if (locIdx < 0) locIdx = 0;
            var locDropdown = new MD3Dropdown(M("リージョン"),
                ProviderRegistry.VertexAILocationOptions, locIdx);
            locDropdown.style.marginLeft = 12;
            locDropdown.style.marginRight = 12;
            locDropdown.style.marginTop = 8;
            locDropdown.changed += idx =>
            {
                cfg.Location = ProviderRegistry.VertexAILocationOptions[idx];
                SaveSettings();
            };
            parent.Add(locDropdown);

            // API version
            int verIdx = Array.IndexOf(ProviderRegistry.VertexAIApiVersions, cfg.ApiVersion);
            if (verIdx < 0) verIdx = 0;
            var verDropdown = new MD3Dropdown(M("API バージョン"),
                ProviderRegistry.VertexAIApiVersionLabels, verIdx);
            verDropdown.style.marginLeft = 12;
            verDropdown.style.marginRight = 12;
            verDropdown.style.marginTop = 8;
            verDropdown.changed += idx =>
            {
                cfg.ApiVersion = ProviderRegistry.VertexAIApiVersions[idx];
                SaveSettings();
            };
            parent.Add(verDropdown);

            BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);
            BuildGeminiFeatures(parent, cfg);
        }

        // ─── Gemini 組み込み機能 ───

        private void BuildGeminiFeatures(VisualElement parent, ProviderConfig cfg)
        {
            AddDivider(parent);
            AddSectionLabel(parent, M("組み込み機能"),
                M("Gemini API のサーバーサイド機能を有効にします。コストが増加する場合があります。"));

            // Google Search
            AddSwitchRow(parent, cfg.GeminiGoogleSearch,
                enabled => M("Google 検索グラウンディング"),
                newVal => { cfg.GeminiGoogleSearch = newVal; SaveSettings(); return newVal; });

            // Code Execution
            AddSwitchRow(parent, cfg.GeminiCodeExecution,
                enabled => M("コード実行 (Python)"),
                newVal => { cfg.GeminiCodeExecution = newVal; SaveSettings(); return newVal; });

            // URL Context
            AddSwitchRow(parent, cfg.GeminiUrlContext,
                enabled => M("URL コンテキスト"),
                newVal => { cfg.GeminiUrlContext = newVal; SaveSettings(); return newVal; });

            // Safety level
            AddDivider(parent);
            AddSectionLabel(parent, M("安全性設定"), M("コンテンツフィルタリングのしきい値を設定します。"));

            var safetyLabels = new[] { M("デフォルト"), "Block None", "Block Only High", "Block Medium+", "Block Low+" };
            var safetyDropdown = new MD3Dropdown(M("しきい値"), safetyLabels, cfg.GeminiSafetyLevel);
            safetyDropdown.style.marginLeft = 12;
            safetyDropdown.style.marginRight = 12;
            safetyDropdown.changed += idx =>
            {
                cfg.GeminiSafetyLevel = idx;
                SaveSettings();
            };
            parent.Add(safetyDropdown);

            // Media resolution
            AddDivider(parent);
            AddSectionLabel(parent, M("画像解像度"), M("入力画像のトークン消費量を制御します。"));

            var resLabels = new[] { M("デフォルト") + " (HIGH)", "LOW (280 tokens)", "MEDIUM (560 tokens)", "HIGH (1120 tokens)" };
            var resDropdown = new MD3Dropdown(M("解像度"), resLabels, cfg.GeminiMediaResolution);
            resDropdown.style.marginLeft = 12;
            resDropdown.style.marginRight = 12;
            resDropdown.changed += idx =>
            {
                cfg.GeminiMediaResolution = idx;
                SaveSettings();
            };
            parent.Add(resDropdown);
        }

        // ─── OpenAI-compatible (API key + model selector) ───

        private void BuildOpenAICompatibleApiKeySettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            AddPasswordField(parent, M("API Key"), cfg.ApiKey, v =>
            {
                cfg.ApiKey = v;
                SaveSettings();
            });

            BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);
        }

        // ─── OpenAI-compatible (URL-based) ───

        private void BuildOpenAICompatibleUrlSettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            if (desc.SettingsKeyApiKey != null)
            {
                AddPasswordField(parent, M("API Key"), cfg.ApiKey, v =>
                {
                    cfg.ApiKey = v;
                    SaveSettings();
                });
            }

            AddTextField(parent, M("ベースURL"), cfg.BaseUrl, v =>
            {
                cfg.BaseUrl = v;
                SaveSettings();
            });

            if (desc.ModelPresets != null)
            {
                BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);
            }
            else
            {
                AddTextField(parent, M("モデル名"), cfg.ModelName, v =>
                {
                    cfg.ModelName = v;
                    SaveSettings();
                });
            }
        }

        // ─── Claude API ───

        private void BuildClaudeApiSettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            AddPasswordField(parent, M("API Key"), cfg.ApiKey, v =>
            {
                cfg.ApiKey = v;
                SaveSettings();
            });

            BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);
        }

        // ─── CLI providers ───

        private void BuildCliProviderSettings(VisualElement parent, ProviderConfig cfg, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            AddTextField(parent, M("CLIパス"), cfg.CliPath, v =>
            {
                cfg.CliPath = v;
                SaveSettings();
            });

            BuildModelSelector(parent, M("モデル"), cfg, desc.ModelPresets, desc.ModelDisplayNames);
        }

        // ─── Clipboard ───

        private void BuildClipboardSettings(VisualElement parent, ProviderDescriptor desc)
        {
            AddSectionLabel(parent, M(desc.SectionTitle) + " " + M("設定"), M(desc.DescriptionKey));

            var banner = new MD3Banner(
                M("プロンプトがクリップボードにコピーされます。外部のチャットサービスで回答を取得し、テキストエリアに貼り付けてください。"),
                MD3Icon.Info);
            banner.style.marginLeft = 12;
            banner.style.marginRight = 12;
            parent.Add(banner);
        }

        // ─── Web Browser (Gemini / ChatGPT / Copilot) ───

        private void BuildGeminiWebSettings(VisualElement parent, ProviderConfig cfg)
        {
            var desc = ProviderRegistry.Get(LLMProviderType.Gemini_Web);
            AddSectionLabel(parent, desc.SectionTitle + " " + M("設定"), M(desc.DescriptionKey));

            // Warning banner
            var banner = new MD3Banner(
                M("シークレットモード（プライベートウィンドウ）の使用を強く推奨します。" +
                  "通常モードでは会話履歴がブラウザとサービス側の両方に保存され、学習データとして利用される可能性があります。\n" +
                  "\n" +
                  "個人アカウント経由でプロジェクト情報（コード・階層構造など）がサービスに送信されます。" +
                  "機密情報を含むプロジェクトでの使用には十分注意してください。\n" +
                  "\n" +
                  "API 接続と比べて不安定です。ブラウザやサイトの更新により動作しなくなる可能性があります。" +
                  "また、サービスの利用規約により自動化アクセスが制限される場合があります。"),
                MD3Icon.Warning);
            banner.style.marginLeft = 12;
            banner.style.marginRight = 12;
            parent.Add(banner);

            // Port
            AddTextField(parent, M("WebSocket ポート"), cfg.Port.ToString(), v =>
            {
                if (int.TryParse(v, out int newPort) && newPort > 0 && newPort <= 65535)
                {
                    cfg.Port = newPort;
                    SaveSettings();
                }
            });

            // Connection status
            bool isConnected = BrowserBridgeState.IsConnected;
            bool isRunning = Providers.BrowserBridge.BrowserBridgeServerManager.IsRunning;
            string statusText = isConnected
                ? M("Chrome 拡張機能: 接続中")
                : isRunning
                    ? M("Chrome 拡張機能: 待機中 (未接続)")
                    : M("サーバー: 停止中");

            var statusLabel = new MD3Text(statusText, MD3TextStyle.LabelLarge,
                color: isConnected ? _theme.Primary : _theme.OnSurfaceVariant);
            statusLabel.style.marginLeft = 12;
            statusLabel.style.marginTop = 8;
            parent.Add(statusLabel);

            // Chrome Web Store check (once)
            if (!_storeCheckDone && _storeCheckRequest == null)
            {
                _storeCheckRequest = UnityEngine.Networking.UnityWebRequest.Get(ChromeStoreUrl);
                _storeCheckRequest.timeout = 10;
                _storeCheckRequest.SendWebRequest();
                EditorApplication.update += PollStoreCheck;
            }

            if (_storeAvailable)
            {
                var storeBtn = new MD3Button(M("Chrome Web Store から拡張機能をインストール"),
                    MD3ButtonStyle.Filled);
                storeBtn.style.marginLeft = 12;
                storeBtn.style.marginRight = 12;
                storeBtn.style.marginTop = 8;
                storeBtn.clicked += () => Application.OpenURL(ChromeStoreUrl);
                parent.Add(storeBtn);

                var instrBanner = new MD3Banner(
                    M("セットアップ手順:\n" +
                      "1. 上のボタンから Chrome 拡張機能をインストール\n" +
                      "2. gemini.google.com / chatgpt.com / copilot.microsoft.com を開く\n" +
                      "3. 拡張機能のポップアップで接続状態を確認"),
                    MD3Icon.Info);
                instrBanner.style.marginLeft = 12;
                instrBanner.style.marginRight = 12;
                instrBanner.style.marginTop = 8;
                parent.Add(instrBanner);
            }
            else
            {
                string extPath = GetBrowserExtensionPath();

                var openBtn = new MD3Button(M("BrowserExtension~ をエクスプローラーで開く"),
                    MD3ButtonStyle.Filled);
                openBtn.style.marginLeft = 12;
                openBtn.style.marginRight = 12;
                openBtn.style.marginTop = 8;
                openBtn.clicked += () => EditorUtility.RevealInFinder(extPath);
                parent.Add(openBtn);

                var pathLabel = new MD3Text(extPath, MD3TextStyle.BodySmall,
                    color: _theme.OnSurfaceVariant);
                pathLabel.style.marginLeft = 12;
                pathLabel.style.marginRight = 12;
                parent.Add(pathLabel);

                var instrBanner = new MD3Banner(
                    M("セットアップ手順:\n" +
                      "1. chrome://extensions を開き「デベロッパーモード」を有効にする\n" +
                      "2. 「パッケージ化されていない拡張機能を読み込む」→ 上記フォルダを選択\n" +
                      "3. gemini.google.com / chatgpt.com / copilot.microsoft.com を開く\n" +
                      "4. 拡張機能のポップアップで接続状態を確認"),
                    MD3Icon.Info);
                instrBanner.style.marginLeft = 12;
                instrBanner.style.marginRight = 12;
                instrBanner.style.marginTop = 8;
                parent.Add(instrBanner);
            }
        }

        private void PollStoreCheck()
        {
            if (_storeCheckRequest == null || !_storeCheckRequest.isDone) return;
            _storeAvailable = _storeCheckRequest.responseCode == 200;
            _storeCheckDone = true;
            _storeCheckRequest.Dispose();
            _storeCheckRequest = null;
            EditorApplication.update -= PollStoreCheck;
            if (_settingsTabIndex == 1) RebuildContentArea();
        }

        private static string GetBrowserExtensionPath()
        {
            string packageRoot = System.IO.Path.GetFullPath("Assets/紫陽花広場/UnityAgent");
            return System.IO.Path.Combine(packageRoot, "BrowserExtension~");
        }

        // ═══════════════════════════════════════════════════════
        //  Model selector (shared)
        // ═══════════════════════════════════════════════════════

        private void BuildModelSelector(VisualElement parent, string label,
            ProviderConfig cfg, string[] presets, string[] displayNames)
        {
            // Model dropdown (when not custom)
            if (!cfg.UseCustomModel)
            {
                int idx = presets != null ? Array.IndexOf(presets, cfg.ModelName) : -1;
                if (idx < 0) idx = 0;
                var modelDropdown = new MD3Dropdown(label, displayNames ?? presets, idx);
                modelDropdown.style.marginLeft = 12;
                modelDropdown.style.marginRight = 12;
                modelDropdown.style.marginTop = 12;
                modelDropdown.changed += i =>
                {
                    if (presets != null)
                    {
                        cfg.ModelName = presets[i];
                        SaveSettings();
                    }
                };
                parent.Add(modelDropdown);
            }

            // Custom model toggle
            var customRow = new MD3Row(8);
            customRow.style.marginLeft = 12;
            customRow.style.marginRight = 12;
            customRow.style.marginTop = 8;

            var customSwitch = new MD3Switch(cfg.UseCustomModel);
            customRow.Add(customSwitch);
            customRow.Add(new MD3Text(M("カスタムモデル"), MD3TextStyle.Body));
            parent.Add(customRow);

            customSwitch.changed += newVal =>
            {
                cfg.UseCustomModel = newVal;
                if (!newVal && presets != null && presets.Length > 0 && Array.IndexOf(presets, cfg.ModelName) < 0)
                    cfg.ModelName = presets[0];
                SaveSettings();
                RebuildContentArea();
            };

            // Custom model text field
            if (cfg.UseCustomModel)
            {
                var tf = new MD3TextField(M("モデル名"));
                tf.Value = cfg.ModelName;
                tf.style.marginLeft = 12;
                tf.style.marginRight = 12;
                tf.style.marginTop = 4;
                tf.changed += v =>
                {
                    cfg.ModelName = v;
                    SaveSettings();
                };
                parent.Add(tf);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Theme tab
        // ═══════════════════════════════════════════════════════

        private void BuildThemeTab(VisualElement parent)
        {
            AddHeading(parent, M("テーマ設定"));
            AddSectionLabel(parent, M("カラーモード"),
                M("UIのカラーテーマを選択します。「カスタム」で自由に配色を調整できます。"));

            // Theme mode
            var themeLabels = new[] { M("自動"), M("ダーク"), M("ライト"), M("カスタム") };
            var themeSeg = new MD3SegmentedButton(themeLabels, _themeMode);
            themeSeg.style.marginLeft = 12;
            themeSeg.style.marginRight = 12;
            themeSeg.style.maxWidth = 360;
            themeSeg.changed += idx =>
            {
                _themeMode = idx;
                AgentSettings.ThemeMode = idx;
                if (idx == 3 && !_customThemeLoaded)
                    LoadCustomTheme();
                NotifyMainWindowTheme();
                // テーマ変更を自身にも反映
                _theme = ResolveTheme();
                _theme.ApplyTo(rootVisualElement);
                _rightPanel.style.backgroundColor = _theme.SurfaceContainerHigh;
                BuildRightPanel();
                RebuildContentArea();
            };
            parent.Add(themeSeg);

            // Custom palette editor
            if (_themeMode == 3)
            {
                if (!_customThemeLoaded || _customTheme == null)
                    LoadCustomTheme();

                AddDivider(parent);
                AddSectionLabel(parent, M("色から生成"),
                    M("1色を選ぶだけでMaterial Design 3のカラーパレットを自動生成します。"));

                // Seed color (using Unity's built-in ColorField via IMGUIContainer)
                var seedRow = new MD3Row(8);
                seedRow.style.marginLeft = 12;
                seedRow.style.marginRight = 12;

                var seedLabel = new MD3Text(M("シードカラー"), MD3TextStyle.Body);
                seedRow.Add(seedLabel);

                var seedImgui = new IMGUIContainer(() =>
                {
                    var newSeed = EditorGUILayout.ColorField(GUIContent.none, _seedColor, true, false, false,
                        GUILayout.Width(60), GUILayout.Height(24));
                    if (newSeed != _seedColor)
                    {
                        _seedColor = newSeed;
                        AgentSettings.SeedColor = newSeed;
                    }
                });
                seedImgui.style.width = 64;
                seedImgui.style.height = 28;
                seedRow.Add(seedImgui);
                parent.Add(seedRow);

                // Generate buttons
                var genRow = new MD3Row(8);
                genRow.style.marginLeft = 12;
                genRow.style.marginRight = 12;
                genRow.style.marginTop = 8;

                var genDarkBtn = new MD3Button(M("ダークで生成"), MD3ButtonStyle.Filled);
                genDarkBtn.clicked += () => ApplyBaseTheme(AgentSettings.GenerateThemeFromSeed(_seedColor, true));
                genRow.Add(genDarkBtn);

                var genLightBtn = new MD3Button(M("ライトで生成"), MD3ButtonStyle.Filled);
                genLightBtn.clicked += () => ApplyBaseTheme(AgentSettings.GenerateThemeFromSeed(_seedColor, false));
                genRow.Add(genLightBtn);
                parent.Add(genRow);

                // Manual edit
                AddDivider(parent);
                AddSectionLabel(parent, M("カスタムカラー"),
                    M("パレットの各色を個別に編集します。既存テーマをベースに調整することもできます。"));

                // Copy/reset buttons
                var copyRow = new MD3Row(8, wrap: true);
                copyRow.style.marginLeft = 12;
                copyRow.style.marginRight = 12;

                var darkCopyBtn = new MD3Button(M("ダークから複製"), MD3ButtonStyle.Outlined);
                darkCopyBtn.clicked += () => ApplyBaseTheme(MD3Theme.Dark());
                copyRow.Add(darkCopyBtn);

                var lightCopyBtn = new MD3Button(M("ライトから複製"), MD3ButtonStyle.Outlined);
                lightCopyBtn.clicked += () => ApplyBaseTheme(MD3Theme.Light());
                copyRow.Add(lightCopyBtn);

                var resetBtn = new MD3Button(M("デフォルトに戻す"), MD3ButtonStyle.Outlined);
                resetBtn.clicked += () =>
                {
                    AgentSettings.ClearAllThemeColors();
                    _customTheme = MD3Theme.Auto();
                    NotifyMainWindowTheme();
                    RebuildContentArea();
                };
                copyRow.Add(resetBtn);
                parent.Add(copyRow);

                // Color groups as foldouts
                var groups = AgentSettings.PaletteColorGroups;
                var names = AgentSettings.PaletteColorNames;

                for (int g = 0; g < groups.Length; g++)
                {
                    var group = groups[g];
                    var foldout = new MD3Foldout(group.label, false);
                    foldout.style.marginLeft = 12;
                    foldout.style.marginRight = 12;
                    foldout.style.marginTop = 4;

                    for (int i = group.start; i < group.start + group.count; i++)
                    {
                        var colorName = names[i];
                        var color = AgentSettings.GetThemeColorField(_customTheme, colorName);

                        var colorRow = new MD3Row(8);
                        colorRow.style.paddingTop = 4;
                        colorRow.style.paddingBottom = 4;

                        var colorLabel = new MD3Text(colorName, MD3TextStyle.Body);
                        colorLabel.style.minWidth = 160;
                        colorRow.Add(colorLabel);

                        string cn = colorName; // capture for closure
                        var colorImgui = new IMGUIContainer(() =>
                        {
                            var currentColor = AgentSettings.GetThemeColorField(_customTheme, cn);
                            var newColor = EditorGUILayout.ColorField(GUIContent.none, currentColor, true, false, false,
                                GUILayout.Width(60), GUILayout.Height(20));
                            if (newColor != currentColor)
                            {
                                AgentSettings.SetThemeColorField(_customTheme, cn, newColor);
                                AgentSettings.SetThemeColor(cn, newColor);
                                NotifyMainWindowTheme();
                            }
                        });
                        colorImgui.style.width = 64;
                        colorImgui.style.height = 24;
                        colorRow.Add(colorImgui);

                        foldout.Content.Add(colorRow);
                    }

                    parent.Add(foldout);
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        //  MCP tab
        // ═══════════════════════════════════════════════════════

        private void BuildMCPTab(VisualElement parent)
        {
            if (!_mcpLoaded)
            {
                var configs = MCPManager.GetServerConfigs();
                _mcpServers = configs != null ? new List<MCPServerConfig>(configs) : new List<MCPServerConfig>();
                _mcpLoaded = true;
            }

            // ── MCP Server (UnityAgent 自身を MCP サーバーとして公開) ──
            BuildMCPServerSection(parent);
            AddDivider(parent);

            AddSectionLabel(parent, "MCP " + M("サーバー設定"),
                M("MCP (Model Context Protocol) サーバーを設定して、AIエージェントに外部ツールを追加します。"));

            // Preset buttons (dynamic from MCPManager.GetPresets)
            var existingNames = new HashSet<string>();
            foreach (var s in _mcpServers)
                if (!string.IsNullOrEmpty(s.name)) existingNames.Add(s.name);

            var presets = MCPManager.GetPresets();
            var presetRow = new MD3Row(6);
            presetRow.style.marginLeft = 12;
            presetRow.style.marginRight = 12;
            presetRow.style.flexWrap = Wrap.Wrap;

            foreach (var preset in presets)
            {
                bool alreadyAdded = existingNames.Contains(preset.Id);
                var btn = new MD3Button($"+ {preset.DisplayName}", MD3ButtonStyle.Tonal);
                btn.tooltip = preset.Description;
                if (alreadyAdded) btn.SetEnabled(false);
                var p = preset; // capture
                btn.clicked += () =>
                {
                    _mcpServers.Add(p.Create());
                    SaveMCPServers();
                    RebuildContentArea();
                };
                presetRow.Add(btn);
            }

            // カスタム追加ボタン
            var customBtn = new MD3Button(M("+ カスタム"), MD3ButtonStyle.Outlined);
            customBtn.tooltip = M("カスタム MCP サーバーを手動設定");
            customBtn.clicked += () =>
            {
                _mcpServers.Add(new MCPServerConfig
                {
                    name = "custom",
                    command = "",
                    args = Array.Empty<string>(),
                    envKeys = Array.Empty<string>(),
                    envValues = Array.Empty<string>(),
                    enabled = true,
                });
                SaveMCPServers();
                RebuildContentArea();
            };
            presetRow.Add(customBtn);
            parent.Add(presetRow);

            if (_mcpServers.Count == 0)
            {
                var infoBanner = new MD3Banner(
                    M("MCP サーバーが設定されていません。上のボタンからプリセットを追加するか、手動で設定してください。"),
                    MD3Icon.Info);
                infoBanner.style.marginLeft = 12;
                infoBanner.style.marginRight = 12;
                infoBanner.style.marginTop = 8;
                parent.Add(infoBanner);
            }

            // Server list
            for (int i = 0; i < _mcpServers.Count; i++)
            {
                BuildMCPServerEntry(parent, i);
                if (i < _mcpServers.Count - 1)
                    AddDivider(parent);
            }

            // 接続ボタン / 合計ステータス
            if (_mcpServers.Count > 0)
            {
                AddDivider(parent);
                var actionRow = new MD3Row(8);
                actionRow.style.marginLeft = 16;
                actionRow.style.marginRight = 12;
                actionRow.style.marginTop = 4;
                actionRow.style.alignItems = Align.Center;

                if (!MCPManager.IsInitialized)
                {
                    actionRow.Add(new MD3Text(M("未接続"),
                        MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant));
                    actionRow.Add(new MD3Spacer());

                    var connectBtn = new MD3Button(M("接続"), MD3ButtonStyle.Filled);
                    connectBtn.clicked += () =>
                    {
                        connectBtn.SetEnabled(false);
                        connectBtn.Text = M("接続中...");
                        EditorCoroutineUtility.StartCoroutineOwnerless(ConnectMCPAndRefresh());
                    };
                    actionRow.Add(connectBtn);
                }
                else
                {
                    var allTools = MCPManager.GetAllTools();
                    actionRow.Add(new MD3Text($"{allTools.Count} MCP tools active",
                        MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant));
                    actionRow.Add(new MD3Spacer());

                    var reconnectBtn = new MD3Button(M("再接続"), MD3ButtonStyle.Text);
                    reconnectBtn.clicked += () =>
                    {
                        MCPManager.Shutdown();
                        _mcpLoaded = false;
                        NotifyMainWindow();
                        reconnectBtn.SetEnabled(false);
                        reconnectBtn.Text = M("接続中...");
                        EditorCoroutineUtility.StartCoroutineOwnerless(ConnectMCPAndRefresh());
                    };
                    actionRow.Add(reconnectBtn);
                }

                parent.Add(actionRow);
            }

            // ── Logging section ──
            if (MCPManager.IsInitialized && _mcpServers.Count > 0)
            {
                AddDivider(parent);
                var logFoldout = new MD3Foldout(M("ログ"), false);
                logFoldout.style.marginLeft = 4;
                logFoldout.style.marginRight = 4;

                var logContent = logFoldout.Content;

                // ログ表示エリア
                var logScroll = new ScrollView(ScrollViewMode.Vertical);
                logScroll.style.maxHeight = 300;
                logScroll.style.minHeight = 100;
                logScroll.style.backgroundColor = _theme.SurfaceContainerLowest;
                logScroll.style.borderTopLeftRadius = 8;
                logScroll.style.borderTopRightRadius = 8;
                logScroll.style.borderBottomLeftRadius = 8;
                logScroll.style.borderBottomRightRadius = 8;
                logScroll.style.marginTop = 4;
                logScroll.style.paddingLeft = 8;
                logScroll.style.paddingRight = 8;
                logScroll.style.paddingTop = 4;
                logScroll.style.paddingBottom = 4;

                var logLabel = new Label();
                logLabel.style.fontSize = 11;
                logLabel.style.color = _theme.OnSurfaceVariant;
                logLabel.style.whiteSpace = WhiteSpace.Normal;
                logLabel.enableRichText = false;
                logScroll.Add(logLabel);
                logContent.Add(logScroll);

                // ログテキストを設定
                string allLogs = MCPManager.GetAllLogs();
                logLabel.text = string.IsNullOrEmpty(allLogs) ? M("ログはありません") : allLogs;

                // ボタン行
                var logBtnRow = new MD3Row(8);
                logBtnRow.style.marginTop = 6;

                var refreshLogBtn = new MD3Button(M("更新"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
                refreshLogBtn.clicked += () =>
                {
                    string logs = MCPManager.GetAllLogs();
                    logLabel.text = string.IsNullOrEmpty(logs) ? M("ログはありません") : logs;
                };
                logBtnRow.Add(refreshLogBtn);

                var clearLogBtn = new MD3Button(M("クリア"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
                clearLogBtn.clicked += () =>
                {
                    MCPManager.ClearAllLogs();
                    logLabel.text = M("ログはありません");
                };
                logBtnRow.Add(clearLogBtn);

                logBtnRow.Add(new MD3Spacer());

                var saveLogBtn = new MD3Button(M("ファイルに保存"), MD3ButtonStyle.Tonal, size: MD3ButtonSize.Small);
                saveLogBtn.clicked += () =>
                {
                    string logs = MCPManager.GetAllLogs();
                    if (string.IsNullOrEmpty(logs))
                    {
                        ShowSnackbar(M("保存するログがありません"));
                        return;
                    }
                    string defaultName = $"MCP_Log_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                    string path = EditorUtility.SaveFilePanel(M("MCP ログを保存"), "", defaultName, "txt");
                    if (!string.IsNullOrEmpty(path))
                    {
                        System.IO.File.WriteAllText(path, logs, System.Text.Encoding.UTF8);
                        ShowSnackbar(M("ログを保存しました"));
                    }
                };
                logBtnRow.Add(saveLogBtn);

                logContent.Add(logBtnRow);

                // サーバー統計
                var statuses = MCPManager.GetServerStatuses();
                if (statuses.Count > 0)
                {
                    var statsCol = new MD3Column(2);
                    statsCol.style.marginTop = 8;

                    foreach (var st in statuses)
                    {
                        string uptime = st.IsConnected && st.IsAlive ? M("稼働中") : M("停止");
                        var statLine = new MD3Text(
                            $"{st.Name}: {uptime} | {st.ToolCount} tools" +
                            (!string.IsNullOrEmpty(st.LastError) ? $" | Error: {st.LastError}" : ""),
                            MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
                        statLine.style.marginLeft = 4;
                        statsCol.Add(statLine);
                    }

                    logContent.Add(statsCol);
                }

                parent.Add(logFoldout);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  MCP Server (UnityAgent を MCP サーバーとして他エージェントに公開)
        // ═══════════════════════════════════════════════════════

        private void BuildMCPServerSection(VisualElement parent)
        {
            AddSectionLabel(parent, "UnityAgent " + M("を MCP サーバーとして公開"),
                M("他のエージェント (Claude Code, Cursor 等) から HTTP 経由で UnityAgent のツールを呼び出せるようにします。"));

            bool running = AgentMCPServer.Shared.IsRunning;
            var exposeRisk = AgentSettings.MCPServerExposeRisk;

            // Enabled switch
            AddSwitchRow(parent, AgentSettings.MCPServerEnabled,
                enabled => enabled
                    ? M("有効 — 他のエージェントからアクセスできます")
                    : M("無効"),
                newVal =>
                {
                    AgentSettings.MCPServerEnabled = newVal;
                    if (newVal)
                    {
                        AgentSettings.EnsureMCPServerToken();
                        if (AgentSettings.MCPServerMode == MCPServerMode.InProc)
                            AgentMCPServer.StartShared();
                        // Bridge モードはここでは spawn しない (Bootstrap が delayCall で行う)
                    }
                    else
                    {
                        AgentMCPServer.StopShared();
                        AgentMCPBridgeClient.Shared.Disconnect("user_disabled");
                    }
                    EditorApplication.delayCall += RebuildContentArea;
                    return AgentSettings.MCPServerEnabled;
                });

            // Server Mode (InProc / Bridge)
            var modeOptions = new[]
            {
                "InProc — Editor 内で HTTP listener (legacy)",
                "Bridge — 別プロセス (推奨、reload を生き残る)",
            };
            int modeIdx = (int)AgentSettings.MCPServerMode;
            var modeDropdown = new MD3Dropdown(M("サーバーモード"), modeOptions, modeIdx);
            modeDropdown.style.marginLeft = 12;
            modeDropdown.style.marginRight = 12;
            modeDropdown.style.marginTop = 8;
            modeDropdown.changed += idx =>
            {
                var newMode = (MCPServerMode)idx;
                if (newMode == AgentSettings.MCPServerMode) return;

                // Stop both before switching
                if (AgentMCPServer.Shared.IsRunning)
                    AgentMCPServer.StopShared();
                AgentMCPBridgeClient.Shared.Disconnect("mode_change");

                AgentSettings.MCPServerMode = newMode;

                // Restart in new mode if enabled
                if (AgentSettings.MCPServerEnabled)
                {
                    if (newMode == MCPServerMode.InProc)
                    {
                        AgentMCPServer.StartShared();
                    }
                    else
                    {
                        ShowSnackbar(M("Bridge モードに切替えました。Unity を再起動するか reload するとブリッジが起動します。"));
                    }
                }
                EditorApplication.delayCall += RebuildContentArea;
            };
            parent.Add(modeDropdown);

            // Port (InProc only — Bridge derives ports from project hash)
            if (AgentSettings.MCPServerMode == MCPServerMode.InProc)
            {
                var portField = new MD3TextField(M("ポート"));
                portField.Value = AgentSettings.MCPServerPort.ToString();
                portField.style.marginLeft = 12;
                portField.style.marginRight = 12;
                portField.style.marginTop = 8;
                portField.changed += v =>
                {
                    if (int.TryParse(v, out int port) && port > 0 && port < 65536)
                        AgentSettings.MCPServerPort = port;
                };
                parent.Add(portField);
            }
            else
            {
                // Bridge mode: show derived ports as read-only info
                var bridgeInfo = new MD3Text(
                    $"{M("Bridge ポート")}: public={AgentSettings.MCPBridgePublicPort}, internal={AgentSettings.MCPBridgeInternalPort}",
                    MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
                bridgeInfo.style.marginLeft = 12;
                bridgeInfo.style.marginRight = 12;
                bridgeInfo.style.marginTop = 8;
                parent.Add(bridgeInfo);

                var bridgeNote = new MD3Text(
                    M("ポートはプロジェクトパスから自動派生します。Unity 起動時に Library/UnityAgent/Bridge.lock に pid が記録されます。"),
                    MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
                bridgeNote.style.marginLeft = 12;
                bridgeNote.style.marginRight = 12;
                bridgeNote.style.marginTop = 4;
                parent.Add(bridgeNote);
            }

            // Risk selector
            var riskOptions = new[] { "Safe", "Caution (Recommended)", "Dangerous" };
            int riskIdx = Mathf.Clamp((int)exposeRisk, 0, 2);
            var riskDropdown = new MD3Dropdown(M("公開するツールのリスク上限"), riskOptions, riskIdx);
            riskDropdown.style.marginLeft = 12;
            riskDropdown.style.marginRight = 12;
            riskDropdown.style.marginTop = 8;
            riskDropdown.changed += idx =>
            {
                AgentSettings.MCPServerExposeRisk = (AjisaiFlow.UnityAgent.SDK.ToolRisk)idx;
            };
            parent.Add(riskDropdown);

            // Token row
            string token = AgentSettings.MCPServerToken;
            if (string.IsNullOrEmpty(token)) token = "(not generated yet)";
            var tokenLabel = new MD3Text(M("トークン") + ": " + MaskToken(token),
                MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant);
            tokenLabel.style.marginLeft = 12;
            tokenLabel.style.marginRight = 12;
            tokenLabel.style.marginTop = 12;
            parent.Add(tokenLabel);

            var tokenRow = new MD3Row(8);
            tokenRow.style.marginLeft = 12;
            tokenRow.style.marginRight = 12;
            tokenRow.style.marginTop = 4;

            var copyBtn = new MD3Button(M("トークンをコピー"), MD3ButtonStyle.Tonal, size: MD3ButtonSize.Small);
            copyBtn.clicked += () =>
            {
                string t = AgentSettings.EnsureMCPServerToken();
                EditorGUIUtility.systemCopyBuffer = t;
                ShowSnackbar(M("トークンをクリップボードにコピーしました"));
            };
            tokenRow.Add(copyBtn);

            var regenBtn = new MD3Button(M("再生成"), MD3ButtonStyle.Outlined, size: MD3ButtonSize.Small);
            regenBtn.clicked += () =>
            {
                if (EditorUtility.DisplayDialog(
                    M("トークンを再生成"),
                    M("トークンを再生成すると既存の外部エージェントは再接続が必要になります。続行しますか?"),
                    M("OK"), M("キャンセル")))
                {
                    AgentSettings.RegenerateMCPServerToken();
                    if (AgentMCPServer.Shared.IsRunning)
                    {
                        AgentMCPServer.StopShared();
                        AgentMCPServer.StartShared();
                    }
                    RebuildContentArea();
                }
            };
            tokenRow.Add(regenBtn);
            parent.Add(tokenRow);

            // Status (mode-aware)
            string statusText;
            string endpointForSnippet = null;
            bool isLive;
            if (AgentSettings.MCPServerMode == MCPServerMode.Bridge)
            {
                bool bridgeConnected = AgentMCPBridgeClient.Shared.IsConnected;
                isLive = bridgeConnected;
                if (bridgeConnected)
                {
                    endpointForSnippet = $"http://127.0.0.1:{AgentSettings.MCPBridgePublicPort}/mcp";
                    statusText = $"● Bridge connected — {endpointForSnippet}  (internal={AgentSettings.MCPBridgeInternalPort})";
                }
                else
                {
                    statusText = "○ Bridge not connected (binary may be missing or starting up)";
                }
            }
            else
            {
                isLive = running;
                if (running)
                {
                    endpointForSnippet = AgentMCPServer.Shared.Endpoint;
                    statusText = $"● Running — {endpointForSnippet}  (calls: {AgentMCPServer.Shared.TotalCallsServed})";
                }
                else
                {
                    statusText = "○ Stopped";
                }
            }
            var statusLabel = new MD3Text(statusText, MD3TextStyle.LabelLarge,
                color: isLive ? _theme.Primary : _theme.OnSurfaceVariant);
            statusLabel.style.marginLeft = 12;
            statusLabel.style.marginRight = 12;
            statusLabel.style.marginTop = 12;
            parent.Add(statusLabel);

            // Client config snippet
            if (isLive && !string.IsNullOrEmpty(endpointForSnippet))
            {
                string snippet =
                    "{\n" +
                    "  \"mcpServers\": {\n" +
                    "    \"unity-agent\": {\n" +
                    "      \"type\": \"http\",\n" +
                    "      \"url\": \"" + endpointForSnippet + "\",\n" +
                    "      \"headers\": { \"Authorization\": \"Bearer " + AgentSettings.MCPServerToken + "\" }\n" +
                    "    }\n" +
                    "  }\n" +
                    "}";

                var snippetFoldout = new MD3Foldout(M("クライアント設定例 (claude_desktop_config.json)"), false);
                snippetFoldout.style.marginLeft = 4;
                snippetFoldout.style.marginRight = 4;
                snippetFoldout.style.marginTop = 8;

                var snippetLabel = new Label(snippet);
                snippetLabel.style.fontSize = 11;
                snippetLabel.style.color = _theme.OnSurfaceVariant;
                snippetLabel.style.backgroundColor = _theme.SurfaceContainerLowest;
                snippetLabel.style.paddingLeft = 8;
                snippetLabel.style.paddingRight = 8;
                snippetLabel.style.paddingTop = 6;
                snippetLabel.style.paddingBottom = 6;
                snippetLabel.style.borderTopLeftRadius = 6;
                snippetLabel.style.borderTopRightRadius = 6;
                snippetLabel.style.borderBottomLeftRadius = 6;
                snippetLabel.style.borderBottomRightRadius = 6;
                snippetLabel.style.whiteSpace = WhiteSpace.Normal;
                snippetLabel.enableRichText = false;
                snippetFoldout.Content.Add(snippetLabel);

                var copySnipBtn = new MD3Button(M("設定をコピー"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
                copySnipBtn.clicked += () =>
                {
                    EditorGUIUtility.systemCopyBuffer = snippet;
                    ShowSnackbar(M("設定をコピーしました"));
                };
                snippetFoldout.Content.Add(copySnipBtn);

                parent.Add(snippetFoldout);
            }
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "(empty)";
            if (token.Length <= 12) return new string('•', token.Length);
            return token.Substring(0, 4) + new string('•', 8) + token.Substring(token.Length - 4);
        }

        private void BuildMCPServerEntry(VisualElement parent, int index)
        {
            var cfg = _mcpServers[index];
            int idx = index;

            // ── ステータス情報を取得 ──
            var statuses = MCPManager.IsInitialized ? MCPManager.GetServerStatuses() : null;
            MCPServerStatus? status = null;
            if (statuses != null)
                foreach (var s in statuses)
                    if (s.Name == cfg.name) { status = s; break; }

            // ステータスラベルを構築
            string statusText;
            Color statusColor;
            if (status.HasValue && status.Value.IsConnected && status.Value.IsAlive)
            {
                statusText = $"\u25CF {M("接続済み")} | {status.Value.ToolCount} tools";
                statusColor = new Color(0.3f, 0.85f, 0.4f);
            }
            else if (status.HasValue)
            {
                statusText = $"\u25CF {M("切断")}";
                statusColor = _theme.Error;
            }
            else
            {
                statusText = $"\u25CB {M("未接続")}";
                statusColor = _theme.OnSurfaceVariant;
            }

            string displayName = string.IsNullOrEmpty(cfg.name) ? "(unnamed)" : cfg.name;
            string foldLabel = $"{displayName}  {statusText}";
            var foldout = new MD3Foldout(foldLabel, false);
            foldout.style.marginLeft = 4;
            foldout.style.marginRight = 4;
            foldout.style.marginTop = 4;

            // Foldout のヘッダーにステータス + スイッチ + 削除ボタンを挿入
            var header = foldout.Q(className: "md3-foldout__header");
            if (header != null)
            {
                header.Add(new MD3Spacer());

                var actions = new MD3Row(4);
                actions.style.flexShrink = 0;
                actions.style.alignItems = Align.Center;

                var enableSwitch = new MD3Switch(cfg.enabled);
                enableSwitch.style.scale = new Scale(new Vector3(0.8f, 0.8f, 1f));
                enableSwitch.changed += newVal =>
                {
                    var c = _mcpServers[idx];
                    c.enabled = newVal;
                    _mcpServers[idx] = c;
                    SaveMCPServers();
                };
                enableSwitch.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                actions.Add(enableSwitch);

                var removeBtn = new MD3IconButton(MD3Icon.Close, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
                removeBtn.clicked += () =>
                {
                    _mcpServers.RemoveAt(idx);
                    SaveMCPServers();
                    RebuildContentArea();
                };
                removeBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                actions.Add(removeBtn);

                header.Add(actions);
            }

            // ── 折りたたみコンテンツ: 設定フィールド ──
            var content = foldout.Content;

            AddTextField(content, M("名前"), cfg.name ?? "", v =>
            {
                var c = _mcpServers[idx];
                c.name = v;
                _mcpServers[idx] = c;
                SaveMCPServers();
            }, 8);

            // コマンド形式ドロップダウン + 引数
            var cmdPresets = new[] { "npx", "uvx", "node", "python", "docker", M("カスタム") };
            int cmdIdx = Array.IndexOf(cmdPresets, cfg.command ?? "");
            if (cmdIdx < 0) cmdIdx = cmdPresets.Length - 1; // カスタム

            var cmdDropdown = new MD3Dropdown(M("コマンド形式"), cmdPresets, cmdIdx);
            cmdDropdown.style.marginLeft = 8;
            cmdDropdown.style.marginRight = 8;
            cmdDropdown.style.marginTop = 4;
            content.Add(cmdDropdown);

            // コマンドプレビュー
            var cmdPreview = new Label();
            cmdPreview.style.fontSize = 11;
            cmdPreview.style.color = _theme.OnSurfaceVariant;
            cmdPreview.style.marginLeft = 12;
            cmdPreview.style.marginTop = 2;
            cmdPreview.style.opacity = 0.7f;
            cmdPreview.enableRichText = false;
            content.Add(cmdPreview);

            // プレビュー更新関数
            Action updatePreview = () =>
            {
                var c = _mcpServers[idx];
                string cmd = c.command ?? "";
                string args = c.args != null ? string.Join(" ", c.args) : "";
                cmdPreview.text = $"$ {cmd} {args}".Trim();
            };
            updatePreview();

            // カスタムコマンド入力（カスタム選択時のみ表示）
            var customCmdContainer = new VisualElement();
            customCmdContainer.style.display = cmdIdx == cmdPresets.Length - 1 ? DisplayStyle.Flex : DisplayStyle.None;
            AddTextField(customCmdContainer, M("コマンドパス"), cfg.command ?? "", v =>
            {
                var c = _mcpServers[idx];
                c.command = v;
                _mcpServers[idx] = c;
                SaveMCPServers();
                updatePreview();
            }, 8);
            content.Add(customCmdContainer);

            cmdDropdown.changed += newIdx =>
            {
                var c = _mcpServers[idx];
                if (newIdx < cmdPresets.Length - 1)
                {
                    c.command = cmdPresets[newIdx];
                    customCmdContainer.style.display = DisplayStyle.None;
                }
                else
                {
                    customCmdContainer.style.display = DisplayStyle.Flex;
                }
                _mcpServers[idx] = c;
                SaveMCPServers();
                updatePreview();
            };

            string argsText = cfg.args != null ? string.Join(" ", cfg.args) : "";
            AddTextField(content, M("引数"), argsText, v =>
            {
                var c = _mcpServers[idx];
                c.args = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                _mcpServers[idx] = c;
                SaveMCPServers();
                updatePreview();
            }, 8);

            // 環境変数
            var envContainer = new MD3Column(4);
            content.Add(envContainer);

            BuildEnvVarRows(envContainer, idx);

            // 環境変数 追加ボタン
            var addEnvBtn = new MD3Button(M("+ 環境変数"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            addEnvBtn.style.marginLeft = 8;
            addEnvBtn.style.marginTop = 4;
            addEnvBtn.clicked += () =>
            {
                var c = _mcpServers[idx];
                var keys = new List<string>(c.envKeys ?? Array.Empty<string>()) { "" };
                var vals = new List<string>(c.envValues ?? Array.Empty<string>()) { "" };
                c.envKeys = keys.ToArray();
                c.envValues = vals.ToArray();
                _mcpServers[idx] = c;
                SaveMCPServers();
                // Foldout を閉じずに環境変数行だけ再構築
                envContainer.Clear();
                BuildEnvVarRows(envContainer, idx);
            };
            content.Add(addEnvBtn);

            // エラー表示
            if (status.HasValue && !string.IsNullOrEmpty(status.Value.LastError))
            {
                var errLabel = new MD3Text(status.Value.LastError,
                    MD3TextStyle.BodySmall, color: _theme.Error);
                errLabel.style.marginLeft = 8;
                errLabel.style.marginTop = 4;
                content.Add(errLabel);
            }

            parent.Add(foldout);
        }

        private void SaveMCPServers()
        {
            MCPManager.SetServerConfigs(_mcpServers.ToArray());
            MCPManager.Shutdown();
            NotifyMainWindow();
        }

        private IEnumerator ConnectMCPAndRefresh()
        {
            yield return MCPManager.Reinitialize();
            NotifyMainWindow();
            RebuildContentArea();
        }

        private void BuildEnvVarRows(VisualElement container, int serverIdx)
        {
            var cfg = _mcpServers[serverIdx];
            int envCount = cfg.envKeys?.Length ?? 0;
            for (int e = 0; e < envCount; e++)
            {
                int ei = e;
                var envRow = new MD3Row(4);
                envRow.style.marginLeft = 8;
                envRow.style.marginRight = 8;
                envRow.style.alignItems = Align.Center;

                var keyTf = new MD3TextField(ei == 0 ? M("環境変数キー") : M("キー"));
                keyTf.Value = cfg.envKeys[ei] ?? "";
                keyTf.style.flexGrow = 1;
                keyTf.style.flexShrink = 1;
                keyTf.style.flexBasis = 0;
                keyTf.changed += v =>
                {
                    var c = _mcpServers[serverIdx];
                    c.envKeys[ei] = v;
                    _mcpServers[serverIdx] = c;
                    SaveMCPServers();
                };
                envRow.Add(keyTf);

                var valTf = new MD3TextField(M("値"));
                valTf.Value = cfg.envValues[ei] ?? "";
                valTf.style.flexGrow = 1;
                valTf.style.flexShrink = 1;
                valTf.style.flexBasis = 0;
                valTf.changed += v =>
                {
                    var c = _mcpServers[serverIdx];
                    c.envValues[ei] = v;
                    _mcpServers[serverIdx] = c;
                    SaveMCPServers();
                };
                envRow.Add(valTf);

                // 削除ボタン
                var delBtn = new MD3IconButton(MD3Icon.Close, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
                delBtn.clicked += () =>
                {
                    var c = _mcpServers[serverIdx];
                    var keys = new List<string>(c.envKeys);
                    var vals = new List<string>(c.envValues);
                    if (ei < keys.Count) keys.RemoveAt(ei);
                    if (ei < vals.Count) vals.RemoveAt(ei);
                    c.envKeys = keys.ToArray();
                    c.envValues = vals.ToArray();
                    _mcpServers[serverIdx] = c;
                    SaveMCPServers();
                    container.Clear();
                    BuildEnvVarRows(container, serverIdx);
                };
                envRow.Add(delBtn);

                container.Add(envRow);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Right panel (About / Links / Supporters)
        // ═══════════════════════════════════════════════════════

        private const string DiscordUrl = "https://discord.gg/4NDgAY9HPE";

        private void BuildRightPanel()
        {
            _rightPanel.Clear();

            var scroll = new MD3ScrollColumn(8, 16);
            _rightPanel.Add(scroll);

            // ── Tool info ──
            scroll.Add(new MD3Text("Unity AI Agent", MD3TextStyle.DisplayLarge,
                color: _theme.OnSurface));

            var verRow = new MD3Row(8);
            verRow.Add(new MD3Text($"v{UpdateChecker.CurrentVersion}", MD3TextStyle.HeadlineSmall,
                color: _theme.OnSurfaceVariant));

            var checkBtn = new MD3Button(M("更新確認"), MD3ButtonStyle.Tonal);
            checkBtn.clicked += () =>
                UpdateChecker.CheckNow(
                    onResult: msg => ShowSnackbar(msg),
                    onUpdateAvailable: info =>
                    {
                        _updateVersionInfo = info;
                        var dialog = new MD3Dialog(
                            M("アップデート"),
                            $"v{info.version}\n\n{info.changelog ?? ""}",
                            confirmLabel: M("商品ページを開く"),
                            dismissLabel: M("閉じる"),
                            onConfirm: () => Application.OpenURL(UpdateChecker.ProductPageUrl));

                        // Add "skip this version" button to content
                        var skipBtn = new MD3Button(M("このバージョンを無視"), MD3ButtonStyle.Outlined);
                        skipBtn.clicked += () =>
                        {
                            if (_updateVersionInfo != null)
                                SettingsStore.SetString("UnityAgent_SkipVersion", _updateVersionInfo.version);
                            dialog.Dismiss();
                        };
                        dialog.Content.Add(skipBtn);

                        dialog.Show(rootVisualElement);
                    });
            verRow.Add(checkBtn);
            scroll.Add(verRow);

            scroll.Add(new MD3Text(M("LLMを活用したUnity Editor操作ツール"),
                MD3TextStyle.BodySmall, color: _theme.OnSurfaceVariant));
            scroll.Add(new MD3Text("紫陽花広場", MD3TextStyle.LabelLarge,
                color: _theme.OnSurface));
            scroll.Add(new MD3Text("朔さくパンダ（さくばん）", MD3TextStyle.BodySmall,
                color: _theme.OnSurfaceVariant));

            // ── Links ──
            scroll.Add(new MD3Divider());

            var boothBtn = new MD3Button("BOOTH", MD3ButtonStyle.Outlined, icon: MD3Icon.Link);
            boothBtn.clicked += () => Application.OpenURL(UpdateChecker.ProductPageUrl);
            scroll.Add(boothBtn);

            var kofiBtn = new MD3Button(M("サポート"), MD3ButtonStyle.Outlined, icon: MD3Icon.Favorite);
            kofiBtn.clicked += () => Application.OpenURL("https://ko-fi.com/ajisaiflow");
            scroll.Add(kofiBtn);

            var discordBtn = new MD3Button("Discord", MD3ButtonStyle.Outlined, icon: MD3Icon.Share);
            discordBtn.clicked += () => Application.OpenURL(DiscordUrl);
            scroll.Add(discordBtn);

            // ── Supporters ──
            scroll.Add(new MD3Divider());
            scroll.Add(new MD3Text(M("広場に咲く花々（支援者）"), MD3TextStyle.TitleMedium,
                color: _theme.Primary));

            var supporterContainer = new MD3Row(12);
            supporterContainer.style.flexWrap = Wrap.Wrap;
            supporterContainer.style.justifyContent = Justify.Center;
            supporterContainer.style.marginTop = 12;
            scroll.Add(supporterContainer);

            LoadSupportersFromJson(supporterContainer);
        }

        private const string SupporterJsonUrl = "https://raw.githubusercontent.com/lighfu/ajisaiflow-assets/main/docs/supporters.json";
        private const string SupporterAvatarBaseUrl = "https://raw.githubusercontent.com/lighfu/ajisaiflow-assets/main/docs/";

        [System.Serializable]
        private struct SupporterEntry
        {
            public string name;
            public string avatar;
        }

        private void LoadSupportersFromJson(VisualElement container)
        {
            string jsonUrl = SupporterJsonUrl + "?v=" + UnityEngine.Random.Range(0, 999999);
            var request = UnityEngine.Networking.UnityWebRequest.Get(jsonUrl);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    request.Dispose();
                    return;
                }

                string json = request.downloadHandler.text;
                request.Dispose();

                var entries = SupporterJsonHelper.FromJsonArray<SupporterEntry>(json);
                if (entries == null) return;

                for (int i = 0; i < entries.Length; i++)
                    container.Add(CreateSupporterCard(entries[i], i));
            };
        }

        private VisualElement CreateSupporterCard(SupporterEntry entry, int index)
        {
            var card = new MD3Column(6);
            card.style.alignItems = Align.Center;
            card.style.width = 140;

            // 桜型アバター + ゆっくり回転 (MD3ShapedAvatar)
            const int sz = 110;
            float speed = 360f / 15f;
            float offset = index * 72f;

            var shapedAvatar = new MD3ShapedAvatar(MD3Shape.Sakura, sz, speed, offset);
            card.Add(shapedAvatar);

            // Async load → テクスチャ設定
            string avatarUrl = SupporterAvatarBaseUrl + entry.avatar + "?v=" + UnityEngine.Random.Range(0, 999999);
            var imgReq = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(avatarUrl);
            var imgOp = imgReq.SendWebRequest();
            imgOp.completed += _ =>
            {
                if (imgReq.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var srcTex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(imgReq);
                    // 元画像の解像度をそのまま使う（HiDPI対応）
                    int texSz = Mathf.Max(srcTex.width, srcTex.height);
                    var rt = RenderTexture.GetTemporary(texSz, texSz, 0, RenderTextureFormat.ARGB32);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    Graphics.Blit(srcTex, rt);
                    var readable = new Texture2D(texSz, texSz, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, texSz, texSz), 0, 0);
                    readable.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.DestroyImmediate(srcTex); // ダウンロード元テクスチャを破棄
                    readable.hideFlags = HideFlags.HideAndDontSave;
                    shapedAvatar.SetTexture(readable);
                }
                imgReq.Dispose();
            };

            // Name
            var nameLabel = new MD3Text(entry.name, MD3TextStyle.TitleSmall, color: _theme.OnSurface);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.marginTop = 8;
            card.Add(nameLabel);

            return card;
        }


        private static class SupporterJsonHelper
        {
            public static T[] FromJsonArray<T>(string json)
            {
                // BOM 除去
                if (json.Length > 0 && json[0] == '\uFEFF')
                    json = json.Substring(1);
                json = json.Trim();
                string wrapped = "{\"items\":" + json + "}";
                var wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
                return wrapper?.items;
            }

            [System.Serializable]
            private class Wrapper<T> { public T[] items; }
        }

        // ═══════════════════════════════════════════════════════
        //  Reusable form controls
        // ═══════════════════════════════════════════════════════

        private void AddHeading(VisualElement parent, string text)
        {
            var heading = new MD3Text(text, MD3TextStyle.HeadlineLarge,
                color: _theme.OnSurface);
            heading.style.marginLeft = 12;
            heading.style.marginRight = 12;
            heading.style.marginTop = 12;
            heading.style.marginBottom = 16;
            parent.Add(heading);
        }

        private void AddSectionLabel(VisualElement parent, string title, string description = null)
        {
            var titleText = new MD3Text(title, MD3TextStyle.TitleMedium,
                color: _theme.Primary);
            titleText.style.marginLeft = 12;
            titleText.style.marginRight = 12;
            titleText.style.marginTop = 8;
            parent.Add(titleText);

            if (description != null)
            {
                var descText = new MD3Text(description, MD3TextStyle.BodySmall,
                    color: _theme.OnSurfaceVariant);
                descText.style.marginLeft = 12;
                descText.style.marginRight = 12;
                descText.style.marginBottom = 8;
                parent.Add(descText);
            }
        }

        private void AddDivider(VisualElement parent)
        {
            var d = new MD3Divider(12);
            d.style.marginTop = 16;
            d.style.marginBottom = 16;
            parent.Add(d);
        }

        private void AddTextField(VisualElement parent, string label, string value, Action<string> onChange, float leftMargin = 12)
        {
            var tf = new MD3TextField(label);
            tf.Value = value ?? "";
            tf.style.marginLeft = leftMargin;
            tf.style.marginRight = 12;
            tf.style.marginTop = 8;
            tf.changed += onChange;
            parent.Add(tf);
        }

        private void AddPasswordField(VisualElement parent, string label, string value, Action<string> onChange)
        {
            var tf = new MD3TextField(label);
            tf.Value = value ?? "";
            tf.style.marginLeft = 12;
            tf.style.marginRight = 12;
            tf.style.marginTop = 8;

            // Make it a password field by querying the inner TextField
            tf.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var inner = tf.Q<TextField>();
                if (inner != null)
                    inner.isPasswordField = true;
            });

            tf.changed += onChange;
            parent.Add(tf);
        }

        /// <summary>
        /// Adds a switch row with label. The onChanged callback receives the new value
        /// and returns the value to actually set (allows cancellation).
        /// </summary>
        private void AddSwitchRow(VisualElement parent, bool currentValue,
            Func<bool, string> labelFunc, Func<bool, bool> onChanged)
        {
            var row = new MD3Row(8);
            row.style.marginLeft = 12;
            row.style.marginRight = 12;

            var sw = new MD3Switch(currentValue);

            var label = new MD3Text(labelFunc(currentValue), MD3TextStyle.Body);
            label.style.flexGrow = 1;

            sw.changed += newVal =>
            {
                bool result = onChanged(newVal);
                // If the callback returns a different value, update the switch
                if (result != newVal)
                {
                    // Schedule to avoid re-entrant changed event
                    sw.schedule.Execute(() => sw.Value = result);
                }
                label.Text = labelFunc(sw.Value);
            };

            row.Add(sw);
            row.Add(label);
            parent.Add(row);
        }

        private void ShowSnackbar(string message)
        {
            if (_snackbarAnchor == null) return;
            var snackbar = new MD3Snackbar(message);
            snackbar.Show(_snackbarAnchor);
        }

        // ═══════════════════════════════════════════════════════
        //  Settings Load/Save
        // ═══════════════════════════════════════════════════════

        private void LoadSettings()
        {
            _providerType = (LLMProviderType)SettingsStore.GetInt("UnityAgent_ProviderType", 0);
            _configs = ProviderRegistry.LoadAllConfigs();
            _useThinking = SettingsStore.GetBool("UnityAgent_UseThinking", false);
            _thinkingBudget = SettingsStore.GetInt("UnityAgent_ThinkingBudget", 8192);
            _effortLevel = Mathf.Clamp(SettingsStore.GetInt("UnityAgent_EffortLevel", 2), 0, 2);
            _imageProviderType = SettingsStore.GetInt("UnityAgent_ImageProviderType", 0);
            _imageModelName = SettingsStore.GetString("UnityAgent_ImageModelName", "gemini-2.5-flash-image");
            _useCustomImageModel = Array.IndexOf(ProviderRegistry.GeminiImageModelPresets, _imageModelName) < 0;
            _imageApiKey = SettingsStore.GetString("UnityAgent_ImageApiKey", "");
            _imageConnectionMode = SettingsStore.GetInt("UnityAgent_ImageConnectionMode", 0);
            _imageApiVersion = SettingsStore.GetString("UnityAgent_ImageApiVersion", "v1beta");
            _imageCustomEndpoint = SettingsStore.GetString("UnityAgent_ImageCustomEndpoint", "");
            _imageProjectId = SettingsStore.GetString("UnityAgent_ImageProjectId", "");
            _imageLocation = SettingsStore.GetString("UnityAgent_ImageLocation", "us-central1");
            // Migration: 旧設定 (Gemini LLM の API キー共有) からの移行
            if (string.IsNullOrEmpty(_imageApiKey))
                _imageApiKey = SettingsStore.GetString("UnityAgent_ApiKey", "");
            // OpenAI 画像設定
            _openaiImageApiKey = SettingsStore.GetString("UnityAgent_OpenAI_ImageApiKey", "");
            _openaiImageModelName = SettingsStore.GetString("UnityAgent_OpenAI_ImageModelName", "gpt-image-1");
            _openaiImageBaseUrl = SettingsStore.GetString("UnityAgent_OpenAI_ImageBaseUrl", "https://api.openai.com");
            _useCustomOpenAIImageModel = Array.IndexOf(ProviderRegistry.OpenAIImageModelPresets, _openaiImageModelName) < 0;
            _meshyApiKey = SettingsStore.GetString("UnityAgent_MeshyApiKey", "");

            _themeMode = AgentSettings.ThemeMode;
            _seedColor = AgentSettings.SeedColor;
            if (_themeMode == 3)
                LoadCustomTheme();
        }

        private void LoadCustomTheme()
        {
            _customTheme = AgentSettings.BuildCustomTheme();
            _customThemeLoaded = true;
        }

        private void ApplyBaseTheme(MD3Theme baseTheme)
        {
            _customTheme = baseTheme;
            var names = AgentSettings.PaletteColorNames;
            for (int i = 0; i < names.Length; i++)
                AgentSettings.SetThemeColor(names[i], AgentSettings.GetThemeColorField(baseTheme, names[i]));
            AgentSettings.InvalidateThemeCache();
            NotifyMainWindowTheme();
            _theme = ResolveTheme();
            _theme.ApplyTo(rootVisualElement);
            _rightPanel.style.backgroundColor = _theme.SurfaceContainerHigh;
            BuildRightPanel();
            RebuildContentArea();
        }

        private void SaveSettings()
        {
            SettingsStore.SetInt("UnityAgent_ProviderType", (int)_providerType);
            ProviderRegistry.SaveAllConfigs(_configs, _providerType);
            SettingsStore.SetBool("UnityAgent_UseThinking", _useThinking);
            SettingsStore.SetInt("UnityAgent_ThinkingBudget", _thinkingBudget);
            SettingsStore.SetInt("UnityAgent_EffortLevel", _effortLevel);
            SettingsStore.SetInt("UnityAgent_ImageProviderType", _imageProviderType);
            SettingsStore.SetString("UnityAgent_ImageModelName", _imageModelName);
            SettingsStore.SetString("UnityAgent_ImageApiKey", _imageApiKey);
            SettingsStore.SetInt("UnityAgent_ImageConnectionMode", _imageConnectionMode);
            SettingsStore.SetString("UnityAgent_ImageApiVersion", _imageApiVersion);
            SettingsStore.SetString("UnityAgent_ImageCustomEndpoint", _imageCustomEndpoint);
            SettingsStore.SetString("UnityAgent_ImageProjectId", _imageProjectId);
            SettingsStore.SetString("UnityAgent_ImageLocation", _imageLocation);
            // OpenAI 画像設定
            SettingsStore.SetString("UnityAgent_OpenAI_ImageApiKey", _openaiImageApiKey);
            SettingsStore.SetString("UnityAgent_OpenAI_ImageModelName", _openaiImageModelName);
            SettingsStore.SetString("UnityAgent_OpenAI_ImageBaseUrl", _openaiImageBaseUrl);
            SettingsStore.SetString("UnityAgent_MeshyApiKey", _meshyApiKey);

            // Notify the main window to reload settings and reinitialize agent
            NotifyMainWindow();
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

        /// <summary>Lightweight: reload settings + reinitialize agent. No UI rebuild.</summary>
        private static void NotifyMainWindow()
        {
            var mainWindows = Resources.FindObjectsOfTypeAll<UnityAgentWindow>();
            foreach (var w in mainWindows)
                w.ReloadSettingsFromPrefs();
        }

        /// <summary>Full rebuild: reload settings + recreate all UI (theme change).</summary>
        private static void NotifyMainWindowTheme()
        {
            var mainWindows = Resources.FindObjectsOfTypeAll<UnityAgentWindow>();
            foreach (var w in mainWindows)
                w.ReloadSettingsAndTheme();

            var capWindows = Resources.FindObjectsOfTypeAll<ModelCapabilityWindow>();
            foreach (var w in capWindows)
                w.CreateGUI();
        }
    }
}
