using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.Editor.UI;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public partial class UnityAgentWindow : EditorWindow
    {
        // ═══════════════════════════════════════════════════════
        //  UI Toolkit panels
        // ═══════════════════════════════════════════════════════

        MD3Theme _theme;
        ToolbarPanel _toolbarPanel;
        ChatPanel _chatPanel;
        WelcomePanel _welcomePanel;
        InputBar _inputBar;
        TokenBar _tokenBar;
        SuggestionChips _suggestionChipsView;
        HistoryPanel _historyPanel;
        TransformPanelView _transformPanelView;
        QuickMenuOverlay _quickMenuOverlay;


        // ═══════════════════════════════════════════════════════
        //  Core state
        // ═══════════════════════════════════════════════════════

        private string _userQuery = "";
        private List<ChatEntry> _chatHistory = new List<ChatEntry>();
        private System.Text.StringBuilder _fullLog = new System.Text.StringBuilder();
        private bool _shouldScrollToBottom;
        private UnityAgentCore _agent;
        private bool _showHistory;
        private List<ChatSessionHeader> _historyList;
        private string _currentToolStatus = "";
        private ChatEntry _streamingEntry = null;
        // In-flight tool call tracking for rich ToolCall cards (Phase 1)
        private int _toolCallSeq;
        private string _activeToolCallId;
        private int _lastAgentEntryIndex = -1;
        private AgentWebServer _webServer;
        private double _lastWebCacheTime;
        private ChatEntry _currentDebugTarget;
        private System.Diagnostics.Stopwatch _requestStopwatch;
        private List<string> _earlyDebugLogs;

        // Attachment
        private byte[] _pendingAttachmentBytes;
        private string _pendingAttachmentMimeType;
        private Texture2D _pendingAttachmentPreview;
        private string _pendingAttachmentFilename;

        // Suggestions & autocomplete
        private List<SuggestionItem> _suggestionChips;
        private List<SuggestionItem> _autocompleteResults = new List<SuggestionItem>();
        private int _autocompleteIndex = -1;
        private bool _autocompleteVisible;
        private string _pendingAutocomplete;
        private List<string> _recentQueries = new List<string>();
        private double _lastChipRefresh;
        private string _prevUserQuery = "";

        private struct SuggestionItem
        {
            public string displayText;
            public string insertText;
            public string category; // "skill", "recent", "general"
        }

        // ═══════════════════════════════════════════════════════
        //  Settings — Provider (registry-based)
        // ═══════════════════════════════════════════════════════

        private LLMProviderType _providerType;
        private Dictionary<LLMProviderType, ProviderConfig> _configs;
        private bool _useThinking;
        private int _thinkingBudget = 8192;
        private int _effortLevel = 2;
        private string _imageModelName = "gemini-2.5-flash-image";
        private bool _useCustomImageModel;
        private string _meshyApiKey = "";

        // ═══════════════════════════════════════════════════════
        //  Thinking / Markdown
        // ═══════════════════════════════════════════════════════

        private static readonly Regex ThinkingTagRegex = new Regex(
            @"<Thinking>\s*([\s\S]*?)\s*</Thinking>\s*", RegexOptions.Compiled);

        private static void ExtractThinking(ChatEntry entry, string response)
        {
            var match = ThinkingTagRegex.Match(response);
            if (match.Success)
            {
                entry.thinkingText = match.Groups[1].Value.Trim();
                entry.text = response.Substring(0, match.Index) + response.Substring(match.Index + match.Length);
                entry.text = entry.text.TrimStart('\n', '\r');
            }
            else
            {
                entry.text = response;
            }
        }

        private static readonly Regex CodeBlockRegex = new Regex(
            @"```[\w]*\r?\n([\s\S]*?)```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(
            @"`([^`\n]+)`", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new Regex(
            @"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(
            @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);

        private static string MarkdownToRichText(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";

            bool dark = EditorGUIUtility.isProSkin;
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

        private static string EscapeRichText(string text)
        {
            return text.Replace("<", "\u2039").Replace(">", "\u203A");
        }

        // ═══════════════════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════════════════

        [MenuItem("Window/紫陽花広場/Unity AI Agent")]
        public static void ShowWindow()
        {
            GetWindow<UnityAgentWindow>(M("Unity AIエージェント"));
        }

        private void OnEnable()
        {
            LoadSettings();
            InitializeAgent();
            CollectRecentQueries();

            // Domain reload を跨ぐセッション復元 (UnityAgentWindow.Persistence.cs)。
            // 順序: InitializeAgent で _agent を作ってから TryLoadSnapshot で _agent._history に書き戻す。
            // CollectRecentQueries の後に呼ぶことで snapshot 側の recentQueries が優先される。
            TryLoadSnapshot();
            RegisterPersistenceHooks();

            // MCP Server からのツール呼び出しを chat に表示する
            MCP.AgentMCPServer.OnCallStart += HandleMCPCallStart;
            MCP.AgentMCPServer.OnCallFinish += HandleMCPCallFinish;
            MCP.AgentMCPServer.OnUserChoiceRequested += HandleMCPUserChoiceRequested;
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
            WireCallbacks();

            // Rebuild chat from history
            if (_chatHistory.Count > 0)
                _chatPanel.RebuildFromHistory(_chatHistory);

            // Update UI state
            UpdateProviderModelChips();
            UpdateToolbarBadges();

            // Update notification banner + periodic re-check (1 hour)
            CheckAndShowUpdateBanner(forceCheck: false);
            rootVisualElement.schedule.Execute(() => CheckAndShowUpdateBanner(forceCheck: true))
                .Every(3600000);

            // Post-update changelog dialog (shown once per version)
            ShowChangelogDialogIfNeeded();

            // Reload 由来で復元された場合は、ここでパネル再構築完了後に自動リトライを発火する。
            // (UnityAgentWindow.Persistence.cs)
            TryAutoResumeAfterReload();
        }

        private void BuildLayout()
        {
            // Toolbar
            _toolbarPanel = new ToolbarPanel(_theme);
            rootVisualElement.Add(_toolbarPanel);

            // Content container
            var contentContainer = new VisualElement();
            contentContainer.style.flexGrow = 1;
            contentContainer.style.overflow = Overflow.Hidden;

            // History panel (hidden by default)
            _historyPanel = new HistoryPanel(_theme);
            contentContainer.Add(_historyPanel);

            // Welcome panel
            _welcomePanel = new WelcomePanel(_theme);
            _welcomePanel.style.display = _chatHistory.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            contentContainer.Add(_welcomePanel);

            // Chat panel
            _chatPanel = new ChatPanel(_theme);
            _chatPanel.style.display = _chatHistory.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            contentContainer.Add(_chatPanel);

            // Transform panel
            _transformPanelView = new TransformPanelView(_theme);
            contentContainer.Add(_transformPanelView);

            rootVisualElement.Add(contentContainer);

            // Token bar
            _tokenBar = new TokenBar(_theme);
            rootVisualElement.Add(_tokenBar);

            // Suggestion chips
            _suggestionChipsView = new SuggestionChips(_theme);
            rootVisualElement.Add(_suggestionChipsView);

            // Input bar
            _inputBar = new InputBar(_theme);
            _inputBar.OnSaveLogClicked = () => SaveFullLog();
            rootVisualElement.Add(_inputBar);

            // Quick menu overlay (absolute positioned)
            _quickMenuOverlay = new QuickMenuOverlay(_theme);
            rootVisualElement.Add(_quickMenuOverlay);
        }

        private void WireCallbacks()
        {
            // Toolbar
            _toolbarPanel.OnSettingsClicked = () => UnityAgentSettingsWindow.Open();
            _toolbarPanel.OnUndoAllClicked = () =>
            {
                int undoCount = _agent?.SessionUndoCount ?? 0;
                if (undoCount == 0 || IsProcessing) return;
                if (EditorUtility.DisplayDialog(
                    M("全操作を元に戻す"),
                    string.Format(M("このチャットセッション中の {0} 件の変更をすべて元に戻しますか？"), undoCount),
                    M("すべて元に戻す"), M("キャンセル")))
                {
                    EditorApplication.delayCall += () =>
                    {
                        int undone = _agent.UndoAll();
                        var infoEntry = ChatEntry.CreateInfo(string.Format(M("[Undo All] {0} 件の操作を元に戻しました。"), undone));
                        _chatHistory.Add(infoEntry);
                        _chatPanel.AppendEntry(infoEntry);
                        _chatPanel.ScrollToBottom();
                    };
                }
            };
            _toolbarPanel.OnNewChatClicked = () =>
            {
                if (_chatHistory.Count > 0)
                    ChatHistoryManager.Save(_chatHistory);
                if (_agent != null) _agent.Cancel();
                ToolConfirmState.Clear();
                ToolConfirmState.SessionSkipAll = false;
                BatchToolConfirmState.Clear();
                UserChoiceState.Clear();
                ClipboardProviderState.Clear();
                _chatHistory.Clear();
                _lastAgentEntryIndex = -1;
                _fullLog.Clear();
                _currentToolStatus = "";
                _streamingEntry = null;
                _activeToolCallId = null;
                _toolCallSeq = 0;
                if (_agent != null) _agent.ClearHistory();
                DiscordWebhookLogger.ResetSession();

                _chatPanel.Clear();
                _welcomePanel.style.display = DisplayStyle.Flex;
                _chatPanel.style.display = DisplayStyle.None;
            };
            _toolbarPanel.OnHistoryToggled = () =>
            {
                _showHistory = !_showHistory;
                if (_showHistory)
                {
                    _historyList = ChatHistoryManager.ListSessions();
                    _historyPanel.LoadSessions(_historyList);
                    _historyPanel.Show();
                }
                else
                {
                    _historyPanel.Hide();
                }
                _toolbarPanel.SetHistoryActive(_showHistory);
            };
            _toolbarPanel.OnSupportClicked = () => Application.OpenURL("https://ko-fi.com/ajisaiflow");
            _toolbarPanel.OnWebToggled = () =>
            {
                bool webRunning = _webServer != null && _webServer.IsRunning;
                if (webRunning)
                {
                    _webServer.Stop();
                }
                else
                {
                    int port = AgentSettings.WebServerPort;
                    _webServer = new AgentWebServer();
                    _webServer.Start(port, AgentSettings.WebServerUsername, AgentSettings.WebServerPassword);
                }
                _toolbarPanel.SetWebActive(_webServer != null && _webServer.IsRunning);
            };
            _toolbarPanel.OnSafetyClicked = () => ToolConfirmSettingsWindow.Open();
            _toolbarPanel.OnSkillsClicked = () => SkillManagementWindow.Open();

            // Input bar
            _inputBar.OnSendClicked = () => SendMessage();
            _inputBar.OnStopClicked = () =>
            {
                _agent?.Cancel();
                _streamingEntry = null;
                if (!string.IsNullOrEmpty(_activeToolCallId))
                {
                    _chatPanel?.CompleteToolCall(_activeToolCallId, "Cancelled", true);
                    _activeToolCallId = null;
                }
                _chatPanel.FinalizeStreaming(null);
                _inputBar.SetProcessing(false);
            };
            _inputBar.OnAttachClicked = () => OpenAttachmentDialog();
            _inputBar.SetUserQuery = text => _userQuery = text;
            _inputBar.GetUserQuery = () => _userQuery;
            _inputBar.OnProviderChipClicked = () => ShowProviderQuickMenu();
            _inputBar.OnModelChipClicked = () => ShowModelQuickMenu();

            // Quick menu overlay
            _quickMenuOverlay.OnDismiss = () => { };
            _quickMenuOverlay.OnProviderSelected = idx =>
            {
                _providerType = (LLMProviderType)idx;
                SaveSettings();
                UpdateProviderModelChips();
            };
            _quickMenuOverlay.OnModelSelected = modelId =>
            {
                _configs[_providerType].ModelName = modelId;
                SaveSettings();
                UpdateProviderModelChips();
            };

            // Suggestion chips
            _suggestionChipsView.OnChipClicked = text =>
            {
                _userQuery = text;
                _inputBar.SetText(text);
                SendMessage();
            };

            // History panel
            _historyPanel.OnSessionSelected = filePath =>
            {
                var entries = ChatHistoryManager.Load(filePath);
                if (entries != null)
                {
                    if (_chatHistory.Count > 0)
                        ChatHistoryManager.Save(_chatHistory);
                    _chatHistory = entries;
                    _chatPanel.RebuildFromHistory(_chatHistory);
                    _chatPanel.ScrollToBottom();
                    _showHistory = false;
                    _historyPanel.Hide();
                    _toolbarPanel.SetHistoryActive(false);
                    _welcomePanel.style.display = DisplayStyle.None;
                    _chatPanel.style.display = DisplayStyle.Flex;
                    if (_agent != null) _agent.ClearHistory();
                    InitializeAgent();
                }
            };
        }

        private void OnDisable()
        {
            _webServer?.Stop();
            _webServer = null;
            if (_chatHistory.Count > 0)
                ChatHistoryManager.Save(_chatHistory);

            // Domain reload で OnDisable が呼ばれた場合は snapshot を残す (次回 OnEnable で復元)。
            // ユーザーが Window を閉じた場合は snapshot を削除して新規セッションから始める。
            // 判別フラグは UnityAgentWindow.Persistence.cs の HandleBeforeAssemblyReload で立てる。
            if (!_reloadInProgress)
                Persistence.ChatSessionPersistence.Clear();
            UnregisterPersistenceHooks();

            MCP.AgentMCPServer.OnCallStart -= HandleMCPCallStart;
            MCP.AgentMCPServer.OnCallFinish -= HandleMCPCallFinish;
            MCP.AgentMCPServer.OnUserChoiceRequested -= HandleMCPUserChoiceRequested;
        }

        /// <summary>外部エージェントがツールを呼び始めたとき、chat に Info エントリを追加する。</summary>
        private void HandleMCPCallStart(string toolName, string argsJson)
        {
            // コンパクトに: 引数は 120 文字まで
            string args = argsJson ?? "";
            if (args.Length > 120) args = args.Substring(0, 120) + "...";
            string text = $"[MCP] {toolName}({args})";
            var entry = ChatEntry.CreateInfo(text);
            _chatHistory.Add(entry);
            _chatPanel?.AppendEntry(entry);
            _shouldScrollToBottom = true;
        }

        /// <summary>外部エージェントへ応答が返ったとき、chat に結果/エラーを表示する。</summary>
        private void HandleMCPCallFinish(string toolName, string result, bool isError)
        {
            string snippet = result ?? "";
            if (snippet.Length > 400) snippet = snippet.Substring(0, 400) + "...";
            string prefix = isError ? "[MCP Error]" : "[MCP Result]";
            var entry = isError
                ? ChatEntry.CreateError($"{prefix} {toolName}: {snippet}")
                : ChatEntry.CreateInfo($"{prefix} {toolName}: {snippet}");
            _chatHistory.Add(entry);
            _chatPanel?.AppendEntry(entry);
            _shouldScrollToBottom = true;
        }

        /// <summary>MCP 経由のツールが AskUser を呼んだとき、Choice エントリを chat に追加する。</summary>
        private void HandleMCPUserChoiceRequested()
        {
            MCP.AgentMCPServer.TraceLog($"  Window.HandleMCPUserChoiceRequested called: pending={UserChoiceState.IsPending}, q={UserChoiceState.Question}, opts={(UserChoiceState.Options!=null?UserChoiceState.Options.Length:-1)}, panel={(_chatPanel!=null)}, historyCount={_chatHistory.Count}");
            if (!UserChoiceState.IsPending) return;

            // Welcome パネルが表示されていれば隠して chat パネルを前面に
            if (_welcomePanel != null) _welcomePanel.style.display = DisplayStyle.None;
            if (_chatPanel != null) _chatPanel.style.display = DisplayStyle.Flex;

            var choiceEntry = ChatEntry.CreateChoice(
                UserChoiceState.Question,
                UserChoiceState.Options,
                UserChoiceState.Importance);
            _chatHistory.Add(choiceEntry);

            // 次フレームに遅延させて UIElements のレイアウト更新を確実にする
            rootVisualElement.schedule.Execute(() =>
            {
                _chatPanel?.AppendEntry(choiceEntry);
                _shouldScrollToBottom = true;
                MCP.AgentMCPServer.TraceLog($"  Choice entry appended via schedule: historyCount={_chatHistory.Count}");
                Repaint();
            }).StartingIn(1);

            _fullLog.AppendLine($"[MCP CHOICE] {UserChoiceState.Question}");
        }

        private void Update()
        {
            // Process messages from web dashboard
            if (_webServer != null && _webServer.IsRunning && (!IsProcessing || UserChoiceState.IsPending))
            {
                var webMessage = _webServer.DequeueMessage();
                if (webMessage != null)
                {
                    if (webMessage.imageBytes != null && webMessage.imageBytes.Length > 0)
                        Tools.SceneViewTools.SetPendingImage(webMessage.imageBytes, webMessage.imageMimeType);
                    _userQuery = webMessage.text;
                    if (string.IsNullOrEmpty(_userQuery) && webMessage.imageBytes != null)
                        _userQuery = M("(画像を添付しました)");
                    if (UserChoiceState.IsPending && !string.IsNullOrEmpty(_userQuery))
                    {
                        var userEntry = ChatEntry.CreateUser(_userQuery);
                        _chatHistory.Add(userEntry);
                        _chatPanel?.AppendEntry(userEntry);
                        _fullLog.AppendLine($"[USER] {_userQuery}");
                        for (int ci = _chatHistory.Count - 2; ci >= 0; ci--)
                        {
                            if (_chatHistory[ci].type == ChatEntry.EntryType.Choice
                                && _chatHistory[ci].choiceSelectedIndex < 0
                                && !_chatHistory[ci].isToolConfirm)
                            {
                                _chatHistory[ci].choiceSelectedIndex = 0;
                                break;
                            }
                        }
                        UserChoiceState.SelectCustom(_userQuery);
                        _userQuery = "";
                        _shouldScrollToBottom = true;
                    }
                    else
                    {
                        SendMessage();
                    }
                }
            }

            if (_webServer != null && _webServer.IsRunning)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastWebCacheTime >= 0.5)
                {
                    _lastWebCacheTime = now;

                    string costStr = "";
                    if (_agent != null)
                    {
                        float inputPricePerM, outputPricePerM;
                        if (GetModelPricing(_configs[_providerType].ModelName, out inputPricePerM, out outputPricePerM))
                        {
                            float cost = (_agent.SessionInputTokens * inputPricePerM + _agent.SessionOutputTokens * outputPricePerM) / 1000000f;
                            costStr = FormatCost(cost);
                        }
                    }

                    _webServer.UpdateCache(new AgentWebStatus
                    {
                        isProcessing = IsProcessing,
                        modelName = _configs[_providerType].ModelName,
                        sessionTotalTokens = _agent?.SessionTotalTokens ?? 0,
                        sessionInputTokens = _agent?.SessionInputTokens ?? 0,
                        sessionOutputTokens = _agent?.SessionOutputTokens ?? 0,
                        lastPromptTokens = _agent?.LastPromptTokens ?? 0,
                        maxContextTokens = _agent?.MaxContextTokens ?? AgentSettings.ResolveMaxContextTokens(
                            _configs[_providerType].MaxContextTokens, 0),
                        estimatedCost = costStr,
                        currentTool = _currentToolStatus,
                    }, _chatHistory);
                }
            }

            // Update token bar
            if (_tokenBar != null && _agent != null)
            {
                string costStr = "";
                float inputPricePerM2, outputPricePerM2;
                if (GetModelPricing(_configs[_providerType].ModelName, out inputPricePerM2, out outputPricePerM2))
                {
                    float cost = (_agent.SessionInputTokens * inputPricePerM2 + _agent.SessionOutputTokens * outputPricePerM2) / 1000000f;
                    costStr = FormatCost(cost);
                }
                _tokenBar.UpdateTokens(
                    _agent.LastPromptTokens,
                    _agent.MaxContextTokens,
                    _agent.SessionTotalTokens,
                    _agent.SessionInputTokens,
                    _agent.SessionOutputTokens,
                    _configs[_providerType].ModelName,
                    costStr);
            }

            // Scroll to bottom if needed
            if (_shouldScrollToBottom)
            {
                _shouldScrollToBottom = false;
                _chatPanel?.ScrollToBottom();
            }

            // Update processing state (AskUser 中はユーザー入力を許可)
            bool userCanInput = UserChoiceState.IsPending;
            bool mcpProviderMode = _providerType == LLMProviderType.MCPServer;
            // MCP Server モードでは LLM への送信は無効化するが、AskUser 応答用の入力は許可する。
            bool disableInput = mcpProviderMode
                ? !userCanInput
                : (IsProcessing && !userCanInput);
            _inputBar?.SetProcessing(disableInput);

            // Update thinking indicator
            bool waitingForUser = UserChoiceState.IsPending
                || ToolConfirmState.IsPending
                || BatchToolConfirmState.IsPending
                || ClipboardProviderState.IsPending;
            _chatPanel?.ShowThinkingIndicator(IsProcessing && _streamingEntry == null && !waitingForUser);

            // Tool progress
            _chatPanel?.UpdateToolProgress(
                ToolProgress.IsActive,
                _currentToolStatus,
                ToolProgress.IsActive ? ToolProgress.Progress : 0f);

            UpdateToolbarBadges();
        }

        private bool IsProcessing => _agent != null && _agent.IsProcessing;

        // ═══════════════════════════════════════════════════════
        //  UI Helpers
        // ═══════════════════════════════════════════════════════

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

        private void UpdateProviderModelChips()
        {
            _inputBar?.UpdateProviderName(GetProviderShortName());
            _inputBar?.UpdateModelName(GetActiveModelDisplayName());
        }

        private void UpdateToolbarBadges()
        {
            if (_toolbarPanel == null) return;
            _toolbarPanel.UpdateUndoCount(_agent?.SessionUndoCount ?? 0);
            _toolbarPanel.UpdateConfirmCount(AgentSettings.GetConfirmTools().Count);

            var allSkills = SkillTools.GetAllSkills();
            var disabledSkills = AgentSettings.GetDisabledSkills();
            int skillTotal = allSkills.Count;
            int skillEnabled = skillTotal - Enumerable.Count(allSkills.Keys, k => disabledSkills.Contains(k));
            _toolbarPanel.UpdateSkillCount(skillEnabled, skillTotal);
        }

        private void ShowProviderQuickMenu()
        {
            var providers = (LLMProviderType[])Enum.GetValues(typeof(LLMProviderType));
            var names = new string[providers.Length];
            var shortNames = new string[providers.Length];
            for (int i = 0; i < providers.Length; i++)
            {
                var desc = ProviderRegistry.Get(providers[i]);
                names[i] = desc.DisplayName;
                shortNames[i] = desc.ShortName;
            }
            _quickMenuOverlay.ShowProviderMenu(names, shortNames, (int)_providerType,
                _inputBar?.worldBound ?? Rect.zero);
        }

        private void ShowModelQuickMenu()
        {
            string[] presets, displayNames;
            string currentModel;
            Action<string> setModel;
            GetModelPresetsForProvider(out presets, out displayNames, out currentModel, out setModel);
            _quickMenuOverlay.ShowModelMenu(presets, displayNames, currentModel,
                _inputBar?.worldBound ?? Rect.zero);
        }

        // ═══════════════════════════════════════════════════════
        //  Settings Load/Save + InitializeAgent
        // ═══════════════════════════════════════════════════════

        private void LoadSettings()
        {
            _providerType = (LLMProviderType)SettingsStore.GetInt("UnityAgent_ProviderType", 0);
            _configs = ProviderRegistry.LoadAllConfigs();
            _useThinking = SettingsStore.GetBool("UnityAgent_UseThinking", false);
            _thinkingBudget = Mathf.Clamp(SettingsStore.GetInt("UnityAgent_ThinkingBudget", 8192), 0, 128000);
            _effortLevel = Mathf.Clamp(SettingsStore.GetInt("UnityAgent_EffortLevel", 2), 0, 2);
            _imageModelName = SettingsStore.GetString("UnityAgent_ImageModelName", "gemini-2.5-flash-image");
            _useCustomImageModel = _providerType == LLMProviderType.Gemini
                                   && Array.IndexOf(ProviderRegistry.GeminiImageModelPresets, _imageModelName) < 0;
            _meshyApiKey = SettingsStore.GetString("UnityAgent_MeshyApiKey", "");
        }

        private void SaveSettings()
        {
            SettingsStore.SetInt("UnityAgent_ProviderType", (int)_providerType);
            ProviderRegistry.SaveAllConfigs(_configs, _providerType);
            SettingsStore.SetBool("UnityAgent_UseThinking", _useThinking);
            SettingsStore.SetInt("UnityAgent_ThinkingBudget", _thinkingBudget);
            SettingsStore.SetInt("UnityAgent_EffortLevel", _effortLevel);
            SettingsStore.SetString("UnityAgent_ImageModelName", _imageModelName);
            SettingsStore.SetString("UnityAgent_MeshyApiKey", _meshyApiKey);
            InitializeAgent();
        }

        /// <summary>
        /// Called by UnityAgentSettingsWindow after settings are saved.
        /// Lightweight reload: settings + agent only, no UI rebuild.
        /// </summary>
        internal void ReloadSettingsFromPrefs()
        {
            LoadSettings();
            InitializeAgent();
            UpdateProviderModelChips();
            UpdateToolbarBadges();
        }

        /// <summary>
        /// Called by UnityAgentSettingsWindow after theme changes.
        /// Full UI rebuild required because inline colors need updating.
        /// </summary>
        internal void ReloadSettingsAndTheme()
        {
            LoadSettings();
            InitializeAgent();
            CreateGUI();
        }

        private void InitializeAgent()
        {
            var cfg = _configs[_providerType];
            var provider = ProviderRegistry.CreateProvider(_providerType, cfg, _useThinking, _thinkingBudget, _effortLevel);
            _agent = new UnityAgentCore(provider);
            _agent.MaxContextTokens = ResolveCurrentMaxContextTokens();
            AgentLogger.Info(LogTag.UI, $"Agent initialized: provider={_providerType}, model={cfg.ModelName}, maxContext={_agent.MaxContextTokens}");
        }

        /// <summary>現在のプロバイダー/モデルに基づく実効コンテキスト上限を返す。</summary>
        private int ResolveCurrentMaxContextTokens()
        {
            var cfg = _configs[_providerType];
            string modelName = cfg.ModelName;
            if (string.IsNullOrEmpty(modelName))
            {
                var desc = ProviderRegistry.Get(_providerType);
                modelName = desc.DefaultModel ?? "";
            }
            var cap = ModelCapabilityRegistry.GetCapability(modelName, _providerType);
            return AgentSettings.ResolveMaxContextTokens(cfg.MaxContextTokens, cap.InputTokenLimit);
        }

        // ═══════════════════════════════════════════════════════
        //  Provider / Model quick selector
        // ═══════════════════════════════════════════════════════

        private string GetProviderShortName()
        {
            return ProviderRegistry.Get(_providerType).ShortName;
        }

        /// <summary>Returns current model display name, or null if the provider has no model selection.</summary>
        private string GetActiveModelDisplayName()
        {
            var cfg = _configs[_providerType];
            var displayName = ProviderRegistry.GetActiveModelDisplayName(_providerType, cfg);
            if (displayName == null)
            {
                var desc = ProviderRegistry.Get(_providerType);
                if (desc.SupportsModelSelection)
                    return M("デフォルト");
            }
            return displayName;
        }

        /// <summary>Returns connected site display name for BrowserBridge, or null if not connected.</summary>
        private static string GetBrowserBridgeSiteName()
        {
            if (!BrowserBridgeState.IsConnected) return null;
            var url = BrowserBridgeState.ConnectedSiteUrl;
            if (string.IsNullOrEmpty(url)) return null;
            if (url.Contains("gemini.google.com")) return "Gemini";
            if (url.Contains("chatgpt.com")) return "ChatGPT";
            if (url.Contains("copilot.microsoft.com")) return "Copilot";
            return null;
        }

        // ShowProviderMenu/ShowModelMenu replaced by ShowProviderQuickMenu/ShowModelQuickMenu

        private void GetModelPresetsForProvider(out string[] presets, out string[] displayNames,
            out string currentModel, out System.Action<string> setModel)
        {
            var desc = ProviderRegistry.Get(_providerType);
            presets = desc.ModelPresets;
            displayNames = desc.ModelDisplayNames;
            if (presets != null)
            {
                var cfg = _configs[_providerType];
                currentModel = cfg.ModelName;
                setModel = v => cfg.ModelName = v;
            }
            else
            {
                currentModel = null;
                setModel = null;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Utility (pricing, formatting)
        // ═══════════════════════════════════════════════════════

        private static string StripPrefix(string text, string prefix)
        {
            if (text != null && text.StartsWith(prefix))
                return text.Substring(prefix.Length);
            return text ?? "";
        }

        private static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000000) return $"{tokens / 1000000f:0.#}M";
            if (tokens >= 1000) return $"{tokens / 1000f:0.#}k";
            return tokens.ToString();
        }

        private static bool GetModelPricing(string modelName, out float inputPricePerM, out float outputPricePerM)
        {
            inputPricePerM = 0;
            outputPricePerM = 0;
            if (string.IsNullOrEmpty(modelName)) return false;

            if (modelName.Contains("2.5-flash-lite"))
            { inputPricePerM = 0.10f; outputPricePerM = 0.40f; return true; }
            if (modelName.Contains("2.0-flash-lite"))
            { inputPricePerM = 0.075f; outputPricePerM = 0.30f; return true; }
            if (modelName.Contains("2.5-pro"))
            { inputPricePerM = 1.25f; outputPricePerM = 10.00f; return true; }
            if (modelName.Contains("2.5-flash"))
            { inputPricePerM = 0.30f; outputPricePerM = 2.50f; return true; }
            if (modelName.Contains("2.0-flash"))
            { inputPricePerM = 0.15f; outputPricePerM = 0.60f; return true; }
            if (modelName.Contains("1.5-pro"))
            { inputPricePerM = 1.25f; outputPricePerM = 5.00f; return true; }
            if (modelName.Contains("1.5-flash"))
            { inputPricePerM = 0.075f; outputPricePerM = 0.30f; return true; }
            if (modelName.Contains("3-pro") || modelName.Contains("3.0-pro"))
            { inputPricePerM = 2.00f; outputPricePerM = 12.00f; return true; }
            if (modelName.Contains("3-flash") || modelName.Contains("3.0-flash"))
            { inputPricePerM = 0.50f; outputPricePerM = 3.00f; return true; }

            return false;
        }

        private static string FormatCost(float cost)
        {
            if (cost < 0.001f) return "< $0.001";
            if (cost < 0.01f) return $"${cost:F3}";
            return $"${cost:F2}";
        }

        // ═══════════════════════════════════════════════════════
        //  Business logic (edit/resend, regenerate, send, etc.)
        // ═══════════════════════════════════════════════════════

        private void UpdateLastAgentEntryIndex()
        {
            _lastAgentEntryIndex = -1;
            for (int i = _chatHistory.Count - 1; i >= 0; i--)
            {
                if (_chatHistory[i].type == ChatEntry.EntryType.Agent)
                {
                    _lastAgentEntryIndex = i;
                    break;
                }
            }
        }

        private void EditAndResend(int chatIndex)
        {
            if (chatIndex < 0 || chatIndex >= _chatHistory.Count) return;

            var entry = _chatHistory[chatIndex];
            string originalText = StripPrefix(entry.text, "You: ");

            int userMessageCount = 0;
            for (int i = 0; i < chatIndex; i++)
            {
                if (_chatHistory[i].type == ChatEntry.EntryType.User)
                    userMessageCount++;
            }

            _chatHistory.RemoveRange(chatIndex, _chatHistory.Count - chatIndex);
            UpdateLastAgentEntryIndex();
            _agent?.TruncateHistory(userMessageCount);
            _userQuery = originalText;
            _inputBar?.SetText(originalText);
            _chatPanel?.RebuildFromHistory(_chatHistory);
            _shouldScrollToBottom = true;
        }

        private void RegenerateLastResponse()
        {
            int lastUserIdx = -1;
            for (int i = _chatHistory.Count - 1; i >= 0; i--)
            {
                if (_chatHistory[i].type == ChatEntry.EntryType.User)
                {
                    lastUserIdx = i;
                    break;
                }
            }
            if (lastUserIdx < 0) return;

            string userText = StripPrefix(_chatHistory[lastUserIdx].text, "You: ");
            EditAndResend(lastUserIdx);
            _userQuery = userText;
            SendMessage();
        }

        private void OpenAttachmentDialog()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                M("画像を選択"), "",
                new[] { "Image files", "png,jpg,jpeg,gif,bmp,webp" });
            if (string.IsNullOrEmpty(path)) return;

            _pendingAttachmentBytes = File.ReadAllBytes(path);
            _pendingAttachmentFilename = Path.GetFileName(path);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".png": _pendingAttachmentMimeType = "image/png"; break;
                case ".jpg": case ".jpeg": _pendingAttachmentMimeType = "image/jpeg"; break;
                case ".gif": _pendingAttachmentMimeType = "image/gif"; break;
                case ".bmp": _pendingAttachmentMimeType = "image/bmp"; break;
                case ".webp": _pendingAttachmentMimeType = "image/webp"; break;
                default: _pendingAttachmentMimeType = "image/png"; break;
            }

            if (_pendingAttachmentPreview != null)
                DestroyImmediate(_pendingAttachmentPreview);
            _pendingAttachmentPreview = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _pendingAttachmentPreview.hideFlags = HideFlags.HideAndDontSave;
            _pendingAttachmentPreview.LoadImage(_pendingAttachmentBytes);
            _inputBar?.ShowAttachmentPreview(_pendingAttachmentPreview);
        }

        private void ClearAttachment()
        {
            _pendingAttachmentBytes = null;
            _pendingAttachmentMimeType = null;
            _pendingAttachmentFilename = null;
            if (_pendingAttachmentPreview != null)
            {
                DestroyImmediate(_pendingAttachmentPreview);
                _pendingAttachmentPreview = null;
            }
        }

        private void RefreshSuggestionChipsUI()
        {
            RefreshSuggestionChips();
            if (_suggestionChips != null && _suggestionChips.Count > 0 && _suggestionChipsView != null)
            {
                var items = new List<(string, string)>();
                foreach (var chip in _suggestionChips)
                    items.Add((chip.displayText, chip.insertText));
                _suggestionChipsView.UpdateChips(items);
            }
            else
            {
                _suggestionChipsView?.UpdateChips(null);
            }
        }

        private static string FormatGameObjectDrop(GameObject go)
        {
            var path = new System.Text.StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                path.Insert(0, "/");
                path.Insert(0, t.name);
                t = t.parent;
            }

            var comps = go.GetComponents<Component>();
            var compNames = new List<string>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var typeName = c.GetType().Name;
                if (typeName == "Transform") continue;
                compNames.Add(typeName);
            }

            int childCount = go.transform.childCount;
            var childNames = new List<string>();
            int limit = Math.Min(childCount, 5);
            for (int i = 0; i < limit; i++)
                childNames.Add(go.transform.GetChild(i).name);

            var sb = new System.Text.StringBuilder();
            sb.Append($" (GameObject: \"{go.name}\"");
            sb.Append($" | Path: {path}");
            if (compNames.Count > 0)
                sb.Append($" | Components: {string.Join(", ", compNames)}");
            if (childCount > 0)
            {
                sb.Append($" | Children({childCount}): {string.Join(", ", childNames)}");
                if (childCount > 5) sb.Append(", ...");
            }
            if (!go.activeSelf)
                sb.Append(" | Inactive");
            sb.Append(")");
            return sb.ToString();
        }

        // ShowCopyContextMenu removed — handled by ChatEntryView contextual menu

        // ═══════════════════════════════════════════════════════
        //  Autocomplete logic
        // ═══════════════════════════════════════════════════════

        private void CollectRecentQueries()
        {
            try
            {
                var sessions = ChatHistoryManager.ListSessions();
                int limit = Mathf.Min(sessions.Count, 10);
                for (int i = 0; i < limit; i++)
                {
                    var entries = ChatHistoryManager.Load(sessions[i].filePath);
                    if (entries == null) continue;
                    foreach (var entry in entries)
                    {
                        if (entry.type == ChatEntry.EntryType.User)
                        {
                            string text = entry.text;
                            if (text != null && text.StartsWith("You: "))
                                text = text.Substring(5);
                            if (!string.IsNullOrEmpty(text) && !_recentQueries.Contains(text))
                            {
                                _recentQueries.Add(text);
                                if (_recentQueries.Count >= 50) return;
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private void RefreshSuggestionChips()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_suggestionChips != null && now - _lastChipRefresh < 5.0) return;
            _lastChipRefresh = now;

            _suggestionChips = new List<SuggestionItem>();
            var disabled = AgentSettings.GetDisabledSkills();

            try
            {
                var skills = SkillTools.GetAllSkills();
                foreach (var kv in skills.OrderBy(k => k.Key))
                {
                    if (disabled.Contains(kv.Key)) continue;
                    var meta = SkillTools.ParseFrontMatter(kv.Value);
                    string title = meta.ContainsKey("title") ? meta["title"] : kv.Key;
                    _suggestionChips.Add(new SuggestionItem
                    {
                        displayText = "\u2728 " + title,
                        insertText = title,
                        category = "skill"
                    });
                }
            }
            catch { }

            int recentCount = Mathf.Min(_recentQueries.Count, 3);
            for (int i = 0; i < recentCount; i++)
            {
                string text = _recentQueries[i];
                string display = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
                _suggestionChips.Add(new SuggestionItem
                {
                    displayText = "\u23F2 " + display,
                    insertText = text,
                    category = "recent"
                });
            }

            if (_suggestionChips.Count > 8)
                _suggestionChips.RemoveRange(8, _suggestionChips.Count - 8);
        }

        private void UpdateAutocomplete()
        {
            _autocompleteResults.Clear();
            _autocompleteIndex = -1;

            if (string.IsNullOrEmpty(_userQuery) || _userQuery.Length < 2)
            {
                _autocompleteVisible = false;
                return;
            }

            string query = _userQuery.ToLower();
            var scored = new List<(SuggestionItem item, int score)>();

            var disabled = AgentSettings.GetDisabledSkills();
            try
            {
                var skills = SkillTools.GetAllSkills();
                foreach (var kv in skills)
                {
                    if (disabled.Contains(kv.Key)) continue;
                    var meta = SkillTools.ParseFrontMatter(kv.Value);
                    string title = meta.ContainsKey("title") ? meta["title"] : kv.Key;
                    string desc = meta.ContainsKey("description") ? meta["description"] : "";
                    string tags = meta.ContainsKey("tags") ? meta["tags"] : "";

                    int score = 0;
                    if (title.ToLower().StartsWith(query)) score += 10;
                    else if (title.ToLower().Contains(query)) score += 5;
                    if (desc.ToLower().Contains(query)) score += 5;
                    if (tags.ToLower().Contains(query)) score += 2;

                    if (score > 0)
                    {
                        string displayDesc = string.IsNullOrEmpty(desc) ? "" : $"  <color=#888888>— {desc}</color>";
                        scored.Add((new SuggestionItem
                        {
                            displayText = "\u2728 " + title + displayDesc,
                            insertText = title,
                            category = "skill"
                        }, score));
                    }
                }
            }
            catch { }

            foreach (var recent in _recentQueries)
            {
                if (recent.ToLower().Contains(query))
                {
                    string display = recent.Length > 50 ? recent.Substring(0, 50) + "..." : recent;
                    int score = recent.ToLower().StartsWith(query) ? 8 : 4;
                    scored.Add((new SuggestionItem
                    {
                        displayText = "\u23F2 " + display,
                        insertText = recent,
                        category = "recent"
                    }, score));
                }
            }

            _autocompleteResults = scored
                .OrderByDescending(s => s.score)
                .Take(6)
                .Select(s => s.item)
                .ToList();

            _autocompleteVisible = _autocompleteResults.Count > 0;
        }

        // ═══════════════════════════════════════════════════════
        //  SendMessage
        // ═══════════════════════════════════════════════════════

        private void SendMessage()
        {
            bool hasAttachment = _pendingAttachmentBytes != null;
            if (string.IsNullOrEmpty(_userQuery) && !hasAttachment) return;

            // MCP Server モードでは LLM を呼ばない。AskUser 回答のみ許可する。
            if (_providerType == LLMProviderType.MCPServer && !UserChoiceState.IsPending)
            {
                var info = ChatEntry.CreateInfo(M("MCP Server モードでは外部エージェントが会話を主導します。入力は無効です。"));
                _chatHistory.Add(info);
                _chatPanel?.AppendEntry(info);
                _userQuery = "";
                _inputBar?.ClearText();
                _shouldScrollToBottom = true;
                return;
            }

            // AskUser 中はカスタム回答として処理
            if (UserChoiceState.IsPending && !string.IsNullOrEmpty(_userQuery))
            {
                string customAnswer = _userQuery;
                var choiceUserEntry = ChatEntry.CreateUser(customAnswer);
                _chatHistory.Add(choiceUserEntry);
                _chatPanel?.AppendEntry(choiceUserEntry);
                _fullLog.AppendLine($"[USER] {customAnswer}");

                // 未回答の Choice エントリを解決済みにする
                for (int ci = _chatHistory.Count - 2; ci >= 0; ci--)
                {
                    if (_chatHistory[ci].type == ChatEntry.EntryType.Choice
                        && _chatHistory[ci].choiceSelectedIndex < 0
                        && !_chatHistory[ci].isToolConfirm)
                    {
                        _chatHistory[ci].choiceSelectedIndex = 0;
                        break;
                    }
                }

                UserChoiceState.SelectCustom(customAnswer);
                _userQuery = "";
                _inputBar?.ClearText();
                _shouldScrollToBottom = true;
                return;
            }

            if (_agent == null) InitializeAgent();

            if (hasAttachment)
                Tools.SceneViewTools.SetPendingImage(_pendingAttachmentBytes, _pendingAttachmentMimeType);

            string query = _userQuery;
            if (string.IsNullOrEmpty(query) && hasAttachment)
                query = M("(画像を添付しました)");

            var userEntry = ChatEntry.CreateUser(query);

            if (hasAttachment && _pendingAttachmentPreview != null)
            {
                var copy = new Texture2D(_pendingAttachmentPreview.width, _pendingAttachmentPreview.height);
                copy.SetPixels(_pendingAttachmentPreview.GetPixels());
                copy.Apply();
                copy.hideFlags = HideFlags.HideAndDontSave;
                userEntry.imagePreview = copy;
            }

            _chatHistory.Add(userEntry);
            _chatPanel?.AppendEntry(userEntry);
            _welcomePanel.style.display = DisplayStyle.None;
            _chatPanel.style.display = DisplayStyle.Flex;
            _fullLog.AppendLine($"[USER] {query}");
            _userQuery = "";
            _inputBar?.ClearText();
            _shouldScrollToBottom = true;
            _currentToolStatus = "";
            _activeToolCallId = null;
            _autocompleteVisible = false;
            _autocompleteIndex = -1;

            ClearAttachment();
            _inputBar?.ClearAttachmentPreview();

            _recentQueries.Remove(query);
            _recentQueries.Insert(0, query);
            if (_recentQueries.Count > 50) _recentQueries.RemoveRange(50, _recentQueries.Count - 50);
            _suggestionChips = null;

            _currentDebugTarget = null;
            _earlyDebugLogs = AgentSettings.DebugMode ? new List<string>() : null;
            _requestStopwatch = System.Diagnostics.Stopwatch.StartNew();

            var callbacks = BuildAgentCallbacks(query);
            var rootHandle = EditorCoroutineUtility.StartCoroutineOwnerless(
                _agent.ProcessUserQuery(query, callbacks.OnReply, callbacks.OnStatus, callbacks.OnDebugLog, callbacks.OnPartial, callbacks.OnStreamEvent));
            _agent.SetRootCoroutine(rootHandle);

            _inputBar?.SetProcessing(true);
        }

        // ═══════════════════════════════════════════════════════
        //  Agent callback set — used by both SendMessage and the
        //  domain-reload auto-resume path so they share identical
        //  UI plumbing (loading indicator, streaming, tool dialogs).
        // ═══════════════════════════════════════════════════════

        internal struct AgentCallbacks
        {
            public Action<string, bool> OnReply;
            public Action<string> OnStatus;
            public Action<string> OnDebugLog;
            public Action<string> OnPartial;
            public Action<Interfaces.ChatStreamEvent> OnStreamEvent;
        }

        internal AgentCallbacks BuildAgentCallbacks(string query)
        {
            return new AgentCallbacks
            {
                OnReply = (response, isFinal) =>
                {
                    if (isFinal)
                    {
                        _requestStopwatch?.Stop();
                        if (_streamingEntry != null)
                        {
                            ExtractThinking(_streamingEntry, response);
                            _fullLog.AppendLine($"[AGENT] {response}");
                            if (_currentDebugTarget != null)
                                _currentDebugTarget.requestDuration = _requestStopwatch?.Elapsed;
                            _chatPanel?.FinalizeStreaming(_streamingEntry);
                            _streamingEntry = null;
                        }
                        else
                        {
                            var entry = ChatEntry.CreateAgent(response);
                            ExtractThinking(entry, response);
                            _chatHistory.Add(entry);
                            _chatPanel?.AppendEntry(entry);
                            _lastAgentEntryIndex = _chatHistory.Count - 1;
                            _fullLog.AppendLine($"[AGENT] {response}");
                            _currentDebugTarget = entry;
                            if (_earlyDebugLogs != null)
                            {
                                foreach (var log in _earlyDebugLogs)
                                    _currentDebugTarget.AppendDebugLog(log);
                                _earlyDebugLogs = null;
                            }
                            if (_currentDebugTarget != null)
                                _currentDebugTarget.requestDuration = _requestStopwatch?.Elapsed;
                        }
                        _currentToolStatus = "";
                        _chatPanel?.ClearActivity();
                        _inputBar?.SetProcessing(false);

                        // Refresh suggestions
                        RefreshSuggestionChipsUI();

                        if (AgentSettings.DiscordLoggingEnabled)
                            DiscordWebhookLogger.Send(query, response);
                    }
                    else
                    {
                        if (response.StartsWith("Error:"))
                        {
                            var errorEntry = ChatEntry.CreateError(response);
                            _chatHistory.Add(errorEntry);
                            _chatPanel?.AppendEntry(errorEntry);
                            _fullLog.AppendLine($"[ERROR] {response}");
                            _chatPanel?.ClearActivity();
                            _inputBar?.SetProcessing(false);
                        }
                    }
                    _shouldScrollToBottom = true;
                },

                OnStatus = status =>
                {
                    if (status == "__BATCH_TOOL_CONFIRM__")
                    {
                        if (BatchToolConfirmState.IsPending && BatchToolConfirmState.Items != null)
                        {
                            var batchEntry = ChatEntry.CreateBatchToolConfirm(BatchToolConfirmState.Items);
                            _chatHistory.Add(batchEntry);
                            _chatPanel?.AppendEntry(batchEntry);
                            _fullLog.AppendLine($"[BATCH TOOL CONFIRM] {BatchToolConfirmState.Items.Count} tools");
                        }
                        _shouldScrollToBottom = true;
                        return;
                    }

                    if (status == "__BROWSER_BRIDGE_WAITING__")
                    {
                        var bbEntry = new ChatEntry
                        {
                            type = ChatEntry.EntryType.Info,
                            text = "Chrome 拡張機能の接続を待機中... (対応サイトを開いてください)",
                            timestamp = DateTime.Now
                        };
                        _chatHistory.Add(bbEntry);
                        _chatPanel?.AppendEntry(bbEntry);
                        _fullLog.AppendLine("[BROWSER_BRIDGE] Waiting for Chrome extension connection");
                        _shouldScrollToBottom = true;
                        return;
                    }

                    if (status == "__CLIPBOARD_WAITING__")
                    {
                        if (ClipboardProviderState.IsPending)
                        {
                            var clipEntry = ChatEntry.CreateClipboard();
                            _chatHistory.Add(clipEntry);
                            _chatPanel?.AppendEntry(clipEntry);
                            _fullLog.AppendLine("[CLIPBOARD] Waiting for user to paste response");
                        }
                        _shouldScrollToBottom = true;
                        return;
                    }

                    if (status == "__TOOL_CONFIRM__")
                    {
                        if (ToolConfirmState.IsPending)
                        {
                            string question = string.Format(M("ツール: {0}({1})\n\n{2}\n\nこのツールを実行しますか？"), ToolConfirmState.ToolName, ToolConfirmState.Parameters, ToolConfirmState.Description);
                            var confirmEntry = ChatEntry.CreateToolConfirm(
                                question,
                                new[] { M("実行"), M("キャンセル"), M("今後確認しない"), M("今回すべて許可") },
                                "warning");
                            _chatHistory.Add(confirmEntry);
                            _chatPanel?.AppendEntry(confirmEntry);
                            _fullLog.AppendLine($"[TOOL CONFIRM] {ToolConfirmState.ToolName}");
                        }
                        _shouldScrollToBottom = true;
                        return;
                    }

                    if (status == "__CHOICE__")
                    {
                        if (UserChoiceState.IsPending)
                        {
                            var choiceEntry = ChatEntry.CreateChoice(
                                UserChoiceState.Question,
                                UserChoiceState.Options,
                                UserChoiceState.Importance);
                            _chatHistory.Add(choiceEntry);
                            _chatPanel?.AppendEntry(choiceEntry);
                            _fullLog.AppendLine($"[CHOICE] {UserChoiceState.Question}");
                        }
                        _shouldScrollToBottom = true;
                        return;
                    }

                    // CLI activity events → live activity panel
                    if (status.StartsWith("\U0001f9e0 Thinking: "))
                    {
                        string preview = status.Substring("\U0001f9e0 Thinking: ".Length);
                        _chatPanel?.UpdateActivity(preview, "\U0001f9e0 Thinking...", null);
                        _fullLog.AppendLine($"[THINKING] {preview}");
                        _shouldScrollToBottom = true;
                        return;
                    }
                    if (status.StartsWith("\U0001f527 Tool: "))
                    {
                        string toolName = status.Substring("\U0001f527 Tool: ".Length);
                        _chatPanel?.UpdateActivity(null, $"\U0001f527 Executing: {toolName}", toolName);
                        _fullLog.AppendLine($"[CLI TOOL] {toolName}");
                        _shouldScrollToBottom = true;
                        return;
                    }
                    if (status.StartsWith("\U0001f310 Server tool: "))
                    {
                        string toolName = status.Substring("\U0001f310 Server tool: ".Length);
                        _chatPanel?.UpdateActivity(null, $"\U0001f310 Server: {toolName}", toolName);
                        _fullLog.AppendLine($"[CLI SERVER_TOOL] {toolName}");
                        _shouldScrollToBottom = true;
                        return;
                    }
                    if (status.StartsWith("\U0001f4cb "))
                    {
                        _chatPanel?.UpdateActivity(null, status, null);
                        _fullLog.AppendLine($"[CLI RESULT] {status}");
                        _shouldScrollToBottom = true;
                        return;
                    }

                    // ── Rich ToolCall cards (Phase 1) ──
                    if (AgentSettings.UseRichChatUI && status.StartsWith("Executing Tool:"))
                    {
                        _currentToolStatus = status;
                        // Format: "Executing Tool: ToolName(arg1, arg2, ...)"
                        string rest = status.Substring("Executing Tool:".Length).Trim();
                        string toolName = rest;
                        string argsRaw = "";
                        int parenIdx = rest.IndexOf('(');
                        if (parenIdx >= 0)
                        {
                            toolName = rest.Substring(0, parenIdx);
                            argsRaw = rest.Substring(parenIdx);
                        }

                        _toolCallSeq++;
                        _activeToolCallId = $"tc-{_toolCallSeq}";
                        var tcEntry = _chatPanel?.BeginToolCall(_activeToolCallId, toolName, argsRaw, _chatHistory);
                        _fullLog.AppendLine($"[TOOL CALL START] {toolName}");
                        _shouldScrollToBottom = true;
                        return;
                    }

                    if (AgentSettings.UseRichChatUI &&
                        (status.StartsWith("[Tool Result]") || status.StartsWith("[Tool Error]")))
                    {
                        bool isError = status.StartsWith("[Tool Error]");
                        string prefix = isError ? "[Tool Error] " : "[Tool Result] ";
                        string resultText = status.Length > prefix.Length
                            ? status.Substring(prefix.Length)
                            : "";

                        if (!string.IsNullOrEmpty(_activeToolCallId))
                        {
                            _chatPanel?.CompleteToolCall(_activeToolCallId, resultText, isError);
                            _activeToolCallId = null;
                            _fullLog.AppendLine($"[TOOL CALL {(isError ? "ERROR" : "RESULT")}] {resultText}");

                            // Scene view captures still emit an image preview as a standalone Info entry
                            // so the user can see the thumbnail inline with the conversation.
                            if (!isError && Tools.SceneViewTools.PendingImageBytes != null &&
                                (resultText.Contains("Captured") || resultText.Contains("Generated")))
                            {
                                var imgEntry = new ChatEntry
                                {
                                    type = ChatEntry.EntryType.Info,
                                    text = "",
                                    timestamp = DateTime.Now,
                                };
                                var tex = new Texture2D(2, 2);
                                if (tex.LoadImage(Tools.SceneViewTools.PendingImageBytes))
                                {
                                    tex.hideFlags = HideFlags.HideAndDontSave;
                                    imgEntry.imagePreview = tex;
                                    _chatHistory.Add(imgEntry);
                                    _chatPanel?.AppendEntry(imgEntry);
                                }
                                else
                                {
                                    UnityEngine.Object.DestroyImmediate(tex);
                                }
                            }

                            _shouldScrollToBottom = true;
                            return;
                        }
                        // Fall through to Info if no active call (shouldn't normally happen)
                    }

                    var infoEntry = ChatEntry.CreateInfo(status);

                    if (status.StartsWith("[Tool Result]") && Tools.SceneViewTools.PendingImageBytes != null)
                    {
                        string resultText = status.Substring("[Tool Result] ".Length);
                        if (resultText.Contains("Captured scene view") ||
                            resultText.Contains("Captured SceneView") ||
                            resultText.Contains("Captured multi-angle") ||
                            resultText.Contains("Captured") ||
                            resultText.Contains("Generated"))
                        {
                            var tex = new Texture2D(2, 2);
                            if (tex.LoadImage(Tools.SceneViewTools.PendingImageBytes))
                            {
                                tex.hideFlags = HideFlags.HideAndDontSave;
                                infoEntry.imagePreview = tex;
                            }
                            else
                            {
                                UnityEngine.Object.DestroyImmediate(tex);
                            }
                        }
                    }

                    _chatHistory.Add(infoEntry);
                    _chatPanel?.AppendEntry(infoEntry);
                    _fullLog.AppendLine($"[INFO] {status}");
                    if (status.StartsWith("Executing Tool:"))
                        _currentToolStatus = status;
                    _shouldScrollToBottom = true;
                },

                OnDebugLog = debugLog =>
                {
                    _fullLog.AppendLine(debugLog);
                    if (AgentSettings.DebugMode)
                    {
                        if (_currentDebugTarget != null)
                        {
                            _currentDebugTarget.AppendDebugLog(debugLog);
                        }
                        else if (_earlyDebugLogs != null)
                        {
                            _earlyDebugLogs.Add(debugLog);
                            _shouldScrollToBottom = true;
                        }
                    }
                },

                OnPartial = partialText =>
                {
                    if (partialText == null)
                    {
                        _streamingEntry = null;
                        return;
                    }
                    if (_streamingEntry == null)
                    {
                        _chatPanel?.ClearActivity();
                        _streamingEntry = ChatEntry.CreateAgent(partialText);
                        _chatHistory.Add(_streamingEntry);
                        _lastAgentEntryIndex = _chatHistory.Count - 1;
                        _currentDebugTarget = _streamingEntry;
                        if (_earlyDebugLogs != null)
                        {
                            foreach (var log in _earlyDebugLogs)
                                _currentDebugTarget.AppendDebugLog(log);
                            _earlyDebugLogs = null;
                        }
                        _chatPanel?.SetStreamingEntry(_streamingEntry);
                    }
                    else
                    {
                        _streamingEntry.text = partialText;
                    }
                    _shouldScrollToBottom = true;
                },

                OnStreamEvent = ev =>
                {
                    // Structured streaming from providers (Claude API SSE etc).
                    // Text events are already handled via OnPartial (provider fires both);
                    // here we only need to pipe Thinking chunks into the streaming entry.
                    if (ev.Kind != Interfaces.StreamEventKind.Thinking) return;
                    if (string.IsNullOrEmpty(ev.Chunk)) return;

                    if (_streamingEntry == null)
                    {
                        _chatPanel?.ClearActivity();
                        _streamingEntry = ChatEntry.CreateAgent("");
                        _chatHistory.Add(_streamingEntry);
                        _lastAgentEntryIndex = _chatHistory.Count - 1;
                        _currentDebugTarget = _streamingEntry;
                        _chatPanel?.SetStreamingEntry(_streamingEntry);
                    }

                    _streamingEntry.thinkingText =
                        (_streamingEntry.thinkingText ?? "") + ev.Chunk;
                    _streamingEntry.cachedThinkingRich = null;
                    _chatPanel?.NotifyStreamingThinkingUpdated();
                    _shouldScrollToBottom = true;
                }
            };
        }

        // ═══════════════════════════════════════════════════════
        //  Log Export
        // ═══════════════════════════════════════════════════════

        private void SaveFullLog()
        {
            if (_chatHistory.Count == 0)
            {
                ShowNotification(new GUIContent(M("保存するログがありません")));
                return;
            }

            string defaultName = $"UnityAgent_Log_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = EditorUtility.SaveFilePanel(M("チャットログを保存"), "", defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var logData = BuildFullLogData();
                string json = JsonUtility.ToJson(logData, true);
                System.IO.File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                ShowNotification(new GUIContent(M("ログを保存しました")));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityAgent] Failed to save log: {ex}");
                ShowNotification(new GUIContent(M("ログの保存に失敗しました")));
            }
        }

        [Serializable]
        private class FullLogData
        {
            public string exportedAt;
            public SessionInfo session;
            public List<LogEntry> entries;
            public string rawLog;
        }

        [Serializable]
        private class SessionInfo
        {
            public string provider;
            public string model;
            public int sessionTotalTokens;
            public int sessionInputTokens;
            public int sessionOutputTokens;
            public int lastPromptTokens;
            public int maxContextTokens;
            public string estimatedCost;
            public string systemPromptSummary;
            public int toolCount;
            public int chatEntryCount;
        }

        [Serializable]
        private class LogEntry
        {
            public string type;
            public string timestamp;
            public string text;
            public string thinkingText;
            public List<string> debugLogs;
            public float requestDurationMs;
            public string[] choiceOptions;
            public string choiceImportance;
            public int choiceSelectedIndex;
            public bool isToolConfirm;
        }

        private FullLogData BuildFullLogData()
        {
            var data = new FullLogData();
            data.exportedAt = DateTime.Now.ToString("O");

            var session = new SessionInfo();
            session.provider = _providerType.ToString();
            session.model = _configs != null && _configs.ContainsKey(_providerType)
                ? _configs[_providerType].ModelName : "unknown";
            if (_agent != null)
            {
                session.sessionTotalTokens = _agent.SessionTotalTokens;
                session.sessionInputTokens = _agent.SessionInputTokens;
                session.sessionOutputTokens = _agent.SessionOutputTokens;
                session.lastPromptTokens = _agent.LastPromptTokens;
                session.maxContextTokens = _agent.MaxContextTokens;
            }
            float inP, outP;
            if (GetModelPricing(session.model, out inP, out outP))
            {
                float cost = (session.sessionInputTokens * inP + session.sessionOutputTokens * outP) / 1000000f;
                session.estimatedCost = FormatCost(cost);
            }
            session.systemPromptSummary = "";
            session.toolCount = 0;
            session.chatEntryCount = _chatHistory.Count;
            data.session = session;

            data.entries = new List<LogEntry>();
            foreach (var entry in _chatHistory)
            {
                var le = new LogEntry();
                le.type = entry.type.ToString();
                le.timestamp = entry.timestamp.ToString("O");
                le.text = entry.text;
                le.thinkingText = entry.thinkingText;
                le.debugLogs = entry.debugLogs;
                le.requestDurationMs = entry.requestDuration.HasValue
                    ? (float)entry.requestDuration.Value.TotalMilliseconds : -1;
                le.choiceOptions = entry.choiceOptions;
                le.choiceImportance = entry.choiceImportance;
                le.choiceSelectedIndex = entry.choiceSelectedIndex;
                le.isToolConfirm = entry.isToolConfirm;
                data.entries.Add(le);
            }

            data.rawLog = _fullLog.ToString();
            return data;
        }
    }

    internal class ContextInfoPopup : PopupWindowContent
    {
        private readonly int _promptTokens;
        private readonly int _maxTokens;
        private readonly int _totalTokens;
        private readonly int _inputTokens;
        private readonly int _outputTokens;
        private readonly string _modelName;

        public ContextInfoPopup(int promptTokens, int maxTokens, int totalTokens,
            int inputTokens, int outputTokens, string modelName)
        {
            _promptTokens = promptTokens;
            _maxTokens = maxTokens;
            _totalTokens = totalTokens;
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
            _modelName = modelName;
        }

        public override Vector2 GetWindowSize() => new Vector2(360, 340);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(M("コンテキスト情報"), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                M("「コンテキスト」はAIが一度に参照できる会話の量です。上限に近づくと古いメッセージが要約・削除され、精度が低下する場合があります。"),
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawRow(M("コンテキスト使用量"), FormatTokens(_promptTokens) + " / " + FormatTokens(_maxTokens));
            DrawRow(M("使用率"), (_maxTokens > 0 ? ((float)_promptTokens / _maxTokens * 100f).ToString("F1") : "0") + "%");

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(M("セッション累計"), EditorStyles.boldLabel);
            DrawRow(M("入力トークン"), FormatTokens(_inputTokens));
            DrawRow(M("出力トークン"), FormatTokens(_outputTokens));
            DrawRow(M("合計トークン"), FormatTokens(_totalTokens));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(M("新規チャットの目安"), EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(
                M("以下のような場合は「新規チャット」で会話をリセットすると効果的です:\n\n• 使用率が 70% を超えたとき — 古い情報が失われ始めます\n• 話題を大きく変えるとき — 不要な文脈が応答の質を下げます\n• AIの応答が不正確になったとき — コンテキスト圧縮による劣化の可能性があります"),
                MessageType.None);

            EditorGUILayout.Space(6);
        }

        private static void DrawRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(140));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatTokens(int tokens)
        {
            if (tokens >= 1000000) return $"{tokens / 1000000f:0.#}M";
            if (tokens >= 1000) return $"{tokens / 1000f:0.#}k";
            return tokens.ToString();
        }
    }
}
