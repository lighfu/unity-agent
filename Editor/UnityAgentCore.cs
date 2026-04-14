using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class UnityAgentCore
    {
        private List<Message> _history = new List<Message>();
        private ILLMProvider _provider;
        
        private bool _isProcessing;
        public bool IsProcessing => _isProcessing;

        /// <summary>HandleResponse から StartCoroutineOwnerless で起動されたコルーチンハンドル。</summary>
        private readonly List<EditorCoroutineHandle> _activeCoroutines = new List<EditorCoroutineHandle>();
        /// <summary>ProcessUserQuery のルートコルーチンハンドル。</summary>
        private EditorCoroutineHandle _rootCoroutine;

        private int _sessionTotalTokens;
        private int _sessionInputTokens;
        private int _sessionOutputTokens;
        private int _lastPromptTokens;
        public int SessionTotalTokens => _sessionTotalTokens;
        public int SessionInputTokens => _sessionInputTokens;
        public int SessionOutputTokens => _sessionOutputTokens;
        public int LastPromptTokens => _lastPromptTokens;

        public int MaxContextTokens { get; set; } = 900000;

        private static readonly System.Text.RegularExpressions.Regex TokenParseRegex =
            new System.Text.RegularExpressions.Regex(@"\[Tokens: (\d+) \(In: (\d+), Out: (\d+)\)\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>コアツール名のセット — LLM が常に署名付きで認識すべき基本ツール。</summary>
        private static readonly HashSet<string> BuiltinMCPToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ReadMCPResource", "GetMCPPrompt", "ListMCPResources", "ListMCPPrompts"
        };

        private static readonly HashSet<string> CoreToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Discovery / Meta
            "SearchTools", "ListTools", "AskUser", "SearchSkills", "ReadSkill",
            // Inspection
            "InspectGameObject", "DeepInspectComponent", "ListRenderers",
            "ListChildren", "GetHierarchyTree", "ListRootObjects", "FindGameObject",
            // Basic Operations
            "SetActive", "SetProperty", "CreateGameObject", "SetParent",
            // SceneView
            "CaptureSceneView", "ScanAvatarMeshes", "CaptureMultiAngle", "FocusSceneView",
            // Assets
            "SearchAssets",
        };

        private int _sessionUndoCount = 0;
        public int SessionUndoCount => _sessionUndoCount;

        /// <summary>1回のユーザーリクエストに対するツール→LLMループ回数。</summary>
        private int _toolLoopCount;
        private const int MaxToolLoops = 30;

        /// <summary>ExecuteToolsAsync が検出した最初のツール呼び出しの終端位置 (text 内)。
        /// HandleResponse で履歴をここまで切り詰め、ハルシネーション部分を除去するために使用。</summary>
        private int _firstToolEndIndex = -1;

        public void Cancel()
        {
            if (_isProcessing)
            {
                AgentLogger.Info(LogTag.Core, $"Cancel requested. Coroutines={_activeCoroutines.Count}");
                _isProcessing = false;
                // Abort in-flight HTTP request
                _provider.Abort();
                // Stop all spawned coroutines (HandleResponse chains)
                foreach (var h in _activeCoroutines)
                    h.Stop();
                _activeCoroutines.Clear();
                // Stop root coroutine
                _rootCoroutine?.Stop();
                _rootCoroutine = null;
                ToolConfirmState.Clear();
                ToolConfirmState.SessionSkipAll = false;
                BatchToolConfirmState.Clear();
                UserChoiceState.Clear();
                ClipboardProviderState.Clear();
                ToolProgress.Clear();
                // Add system message indicating cancellation
                _history.Add(new Message { role = "user", parts = new[] { new Part { text = "System: Operation cancelled by user." } } });
            }
        }

        public void ClearHistory()
        {
            _history.Clear();
            _sessionUndoCount = 0;
            _sessionTotalTokens = 0;
            _sessionInputTokens = 0;
            _sessionOutputTokens = 0;
            _lastPromptTokens = 0;
        }

        /// <summary>
        /// Truncate history to keep only the first keepCount user messages (plus system/model init).
        /// Used for message edit & resend.
        /// </summary>
        public void TruncateHistory(int keepUserMessageCount)
        {
            if (_history.Count == 0) return;

            int userCount = 0;
            int cutIndex = _history.Count;
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].role == "user" && !_history[i].parts[0].text.StartsWith("Tool Outputs:"))
                {
                    userCount++;
                    if (userCount > keepUserMessageCount)
                    {
                        cutIndex = i;
                        break;
                    }
                }
            }

            if (cutIndex < _history.Count)
                _history.RemoveRange(cutIndex, _history.Count - cutIndex);
        }

        public int UndoAll()
        {
            int count = _sessionUndoCount;
            for (int i = 0; i < count; i++)
                Undo.PerformUndo();
            _sessionUndoCount = 0;
            return count;
        }
        
        public UnityAgentCore(ILLMProvider provider)
        {
            _provider = provider;
        }

        // ═══════════════════════════════════════════════════════
        //  Snapshot API — persists LLM conversation across domain reload.
        //  Pairs with Editor/Persistence/ChatSessionPersistence.
        // ═══════════════════════════════════════════════════════

        /// <summary>LLM 履歴の参照を返す (snapshot 取得用、変更しないこと)。</summary>
        public IReadOnlyList<Message> GetHistory() => _history;

        /// <summary>LLM 履歴を直接置き換える。snapshot 復元時のみ使う。</summary>
        public void RestoreHistory(List<Message> history)
        {
            _history = history ?? new List<Message>();
        }

        /// <summary>セッショントークン累積を直接設定する。snapshot 復元時のみ使う。</summary>
        public void RestoreSessionStats(int total, int input, int output, int lastPrompt, int undoCount)
        {
            _sessionTotalTokens = total;
            _sessionInputTokens = input;
            _sessionOutputTokens = output;
            _lastPromptTokens = lastPrompt;
            _sessionUndoCount = undoCount;
        }

        /// <summary>
        /// 自動リトライ可能な状態かを判定する。<see cref="_history"/> の末尾を見て:
        /// - 末尾が user → そのまま再発行可能
        /// - 末尾が model/tool → 不完全なツールループの可能性。末尾の不完全 model ターンを
        ///   pop してから再発行する必要がある
        /// </summary>
        public bool CanResume()
        {
            return _history.Count > 0;
        }

        /// <summary>
        /// 末尾の不完全な assistant/tool ターンを巻き戻して直近の user ターンまで戻す。
        /// 自動リトライ前に呼ぶ。
        /// </summary>
        public void RewindToLastUserTurn()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].role == "user")
                {
                    int removeFrom = i + 1;
                    if (removeFrom < _history.Count)
                        _history.RemoveRange(removeFrom, _history.Count - removeFrom);
                    return;
                }
            }
        }

        /// <summary>
        /// reload 後に進行中だった LLM 呼び出しを再開する。<see cref="ProcessQueryInternal"/> と同じ
        /// パスで _provider.CallLLM を再発行するが、user message は履歴に追加しない (既に履歴にあるため)。
        /// </summary>
        public IEnumerator ResumeAfterReload(
            Action<string, bool> onReplyReceived,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null)
        {
            if (_isProcessing)
            {
                onDebugLog?.Invoke("[UnityAgentCore] ResumeAfterReload: already processing, skipping.");
                yield break;
            }
            if (_history.Count == 0)
            {
                onDebugLog?.Invoke("[UnityAgentCore] ResumeAfterReload: empty history, nothing to resume.");
                yield break;
            }

            RewindToLastUserTurn();

            _isProcessing = true;
            _toolLoopCount = 0;
            ToolConfirmState.SessionSkipAll = false;

            onPartialResponse?.Invoke(null);

            yield return _provider.CallLLM(
                _history,
                response =>
                {
                    if (!_isProcessing) return;
                    var h = EditorCoroutineUtility.StartCoroutineOwnerless(
                        HandleResponse(response, onReplyReceived, onStatus, onDebugLog, onPartialResponse));
                    _activeCoroutines.RemoveAll(x => x.Stopped);
                    _activeCoroutines.Add(h);
                },
                error =>
                {
                    _isProcessing = false;
                    onReplyReceived?.Invoke($"Error: {error}", false);
                },
                onStatus,
                onDebugLog,
                onPartialResponse
            );
        }

        /// <summary>ルートコルーチンハンドルを設定する。Cancel() で停止するため。</summary>
        public void SetRootCoroutine(EditorCoroutineHandle handle) => _rootCoroutine = handle;

        public IEnumerator ProcessUserQuery(string userMessage, Action<string, bool> onReplyReceived, Action<string> onStatus = null, Action<string> onDebugLog = null, Action<string> onPartialResponse = null)
        {
            if (_isProcessing)
            {
                onDebugLog?.Invoke("[UnityAgentCore] Already processing a request. Ignoring new query.");
                yield break;
            }

            _isProcessing = true;
            _toolLoopCount = 0;
            ToolConfirmState.SessionSkipAll = false;
            yield return ProcessQueryInternal(userMessage, onReplyReceived, onStatus, onDebugLog, onPartialResponse);
        }

        private IEnumerator ProcessQueryInternal(string userMessage, Action<string, bool> onReplyReceived, Action<string> onStatus, Action<string> onDebugLog, Action<string> onPartialResponse)
        {
            onDebugLog?.Invoke($"[UnityAgentCore] ProcessQueryInternal: {userMessage}");

            // Initialize MCP servers if needed (before building system prompt)
            onDebugLog?.Invoke($"[MCP] IsInitialized={MCPManager.IsInitialized}, HasEnabledServers={MCPManager.HasEnabledServers}");
            if (!MCPManager.IsInitialized && MCPManager.HasEnabledServers)
            {
                onDebugLog?.Invoke("[MCP] Starting MCP initialization...");
                yield return MCPManager.Initialize();
                onDebugLog?.Invoke($"[MCP] Initialization complete. Tools: {MCPManager.GetAllTools().Count}");
            }
            else if (MCPManager.IsInitialized)
            {
                // ヘルスチェック: プロセス死亡を検知し自動再接続
                yield return MCPManager.EnsureConnected();
            }

            // Initialize history if empty
            if (_history.Count == 0)
            {
                _history.Add(new Message { role = "system", parts = new[] { new Part { text = GetSystemPrompt() } } });
                _history.Add(new Message { role = "model", parts = new[] { new Part { text = "System initialized." } } });
            }

            // Add selection context to user messages (not tool result feedback)
            string messageText = userMessage;
            if (!userMessage.StartsWith("Tool Outputs:"))
            {
                string selectionContext = GetSelectionContext();
                if (!string.IsNullOrEmpty(selectionContext))
                    messageText = $"{userMessage}\n\n{selectionContext}";
            }

            // Add user message to history (with pending image if available)
            var messageParts = new List<Part> { new Part { text = messageText } };
            if (Tools.SceneViewTools.PendingImageBytes != null)
            {
                messageParts.Add(new Part
                {
                    imageBytes = Tools.SceneViewTools.PendingImageBytes,
                    imageMimeType = Tools.SceneViewTools.PendingImageMimeType
                });
                Tools.SceneViewTools.ClearPendingImage();
            }
            _history.Add(new Message { role = "user", parts = messageParts.ToArray() });
            
            // Signal new streaming session start
            onPartialResponse?.Invoke(null);

            yield return _provider.CallLLM(
                _history,
                response =>
                {
                    if (!_isProcessing) return; // Cancelled

                    // Handle response (check for tool calls) — track handle for cancellation
                    var h = EditorCoroutineUtility.StartCoroutineOwnerless(HandleResponse(response, onReplyReceived, onStatus, onDebugLog, onPartialResponse));
                    _activeCoroutines.RemoveAll(x => x.Stopped);
                    _activeCoroutines.Add(h);
                },
                error =>
                {
                    _isProcessing = false;
                    onReplyReceived?.Invoke($"Error: {error}", false);
                },
                onStatus,
                onDebugLog,
                onPartialResponse
            );
        }

        private IEnumerator HandleResponse(string responseText, Action<string, bool> onReplyReceived, Action<string> onStatus, Action<string> onDebugLog, Action<string> onPartialResponse)
        {
            if (!_isProcessing) yield break;

            onDebugLog?.Invoke($"[UnityAgentCore] HandleResponse: {responseText}");

            // Parse token usage from response before stripping
            var tokenMatch = TokenParseRegex.Match(responseText);
            if (tokenMatch.Success)
            {
                int total = int.Parse(tokenMatch.Groups[1].Value);
                int prompt = int.Parse(tokenMatch.Groups[2].Value);
                int output = int.Parse(tokenMatch.Groups[3].Value);
                _sessionTotalTokens += total;
                _sessionInputTokens += prompt;
                _sessionOutputTokens += output;
                _lastPromptTokens = prompt;
            }

            // Strip display-only annotations before adding to history
            // so the model doesn't see [Tokens: ...] or <Thinking> wrappers
            string historyText = StripDisplayAnnotations(responseText);
            _history.Add(new Message { role = "model", parts = new[] { new Part { text = historyText } } });

            var toolResults = new List<string>();
            yield return ExecuteToolsAsync(historyText, onStatus, toolResults);
            if (!_isProcessing) yield break;

            // Truncate history entry to remove hallucinated content after the first tool call.
            // When the LLM outputs multiple tool calls, everything after the first is based on
            // fabricated results and must not persist in the conversation history.
            if (toolResults.Count > 0 && _firstToolEndIndex > 0 && _firstToolEndIndex < historyText.Length)
            {
                string truncated = historyText.Substring(0, _firstToolEndIndex);
                if (_history.Count > 0 && _history[_history.Count - 1].role == "model")
                {
                    _history[_history.Count - 1] = new Message
                    {
                        role = "model",
                        parts = new[] { new Part { text = truncated } }
                    };
                    onDebugLog?.Invoke($"[UnityAgentCore] Truncated model history: removed {historyText.Length - _firstToolEndIndex} chars of hallucinated content after first tool call.");
                }
                _firstToolEndIndex = -1;
            }

            if (toolResults.Count > 0)
            {
                // Check for user choice marker and handle waiting
                for (int i = 0; i < toolResults.Count; i++)
                {
                    if (toolResults[i] == "__WAITING_USER_CHOICE__")
                    {
                        onStatus?.Invoke("__CHOICE__");
                        onDebugLog?.Invoke("[UnityAgentCore] Waiting for user choice...");

                        // Wait for user selection
                        while (UserChoiceState.SelectedIndex < 0)
                        {
                            if (!_isProcessing) yield break;
                            yield return null;
                        }

                        // Replace marker with user's selection
                        string selected = UserChoiceState.CustomText
                            ?? UserChoiceState.Options[UserChoiceState.SelectedIndex];
                        toolResults[i] = UserChoiceState.CustomText != null
                            ? $"User responded: \"{selected}\""
                            : $"User selected: \"{selected}\"";
                        UserChoiceState.Clear();
                        onDebugLog?.Invoke($"[UnityAgentCore] User selected: {selected}");
                    }
                }

                // Feed tool results back to LLM
                var sb = new StringBuilder();
                foreach (var result in toolResults)
                {
                    sb.AppendLine(result);
                }
                string combinedResult = sb.ToString();

                onDebugLog?.Invoke($"[UnityAgentCore] Tool results: {combinedResult}. Feeding back to LLM.");
                _toolLoopCount++;
                int maxTokens = MaxContextTokens;
                if (_isProcessing && _toolLoopCount > MaxToolLoops)
                {
                    onDebugLog?.Invoke($"[UnityAgentCore] Tool loop limit ({MaxToolLoops}) reached. Stopping.");
                    onReplyReceived?.Invoke($"ツールループの上限 ({MaxToolLoops}回) に達しました。処理を中断します。", true);
                    _isProcessing = false;
                }
                else if (_isProcessing && (_lastPromptTokens == 0 || _lastPromptTokens < maxTokens))
                {
                    yield return ProcessQueryInternal($"Tool Outputs:\n{combinedResult}", onReplyReceived, onStatus, onDebugLog, onPartialResponse);
                }
                else if (_isProcessing)
                {
                    onDebugLog?.Invoke($"[UnityAgentCore] Context token limit ({maxTokens}) reached (current: {_lastPromptTokens}). Stopping.");
                    onReplyReceived?.Invoke($"コンテキストのトークン上限 ({FormatTokenCount(maxTokens)}) に達しました。処理を中断します。", true);
                    _isProcessing = false;
                }
            }
            else
            {
                onDebugLog?.Invoke($"[UnityAgentCore] No tool call detected. Invoking onReplyReceived.");
                string finalText = string.IsNullOrWhiteSpace(historyText) && string.IsNullOrWhiteSpace(responseText)
                    ? "（LLMから空の応答を受け取りました。再度お試しください。）"
                    : responseText;
                onReplyReceived?.Invoke(finalText, true);
                _isProcessing = false;
            }
        }

        private IEnumerator ExecuteToolsAsync(string text, Action<string> onStatus, List<string> results)
        {
            // Try bracketed format first: [MethodName(arg1, arg2)]
            // The argument-list grammar (inside the inner non-capturing group) accepts in priority order:
            //   1. """...""" — triple-quoted raw multi-line string (no escape processing)
            //      Pattern: """(?:[^"]|"(?!""))*""" — matches anything not containing 3 consecutive quotes
            //   2. [^'"()]* — bare characters
            //   3. '...' — single-quoted string
            //   4. "..." — double-quoted string
            // Triple-quoted MUST come first so the matcher does not consume it as a sequence of empty "" tokens.
            const string ArgsPattern = @"(?:""""""(?:[^""]|""(?!""""))*""""""|[^'""()]*|'[^']*'|""[^""]*"")*";

            var matches = System.Text.RegularExpressions.Regex.Matches(
                text, $@"\[(\w+)\(({ArgsPattern})\)\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (matches.Count == 0)
            {
                // Fallback: [MCP/server.MethodName(args)] or [prefix.MethodName(args)] format
                // Captures just the final method name after the last dot or slash
                matches = System.Text.RegularExpressions.Regex.Matches(
                    text, $@"\[[\w/]+[./](\w+)\(({ArgsPattern})\)\]",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            }

            if (matches.Count == 0)
            {
                // Fallback: [Category: MethodName(args)] format (some models add a label prefix)
                matches = System.Text.RegularExpressions.Regex.Matches(
                    text, $@"\[\w+:\s*(\w+)\(({ArgsPattern})\)\]",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            }

            if (matches.Count == 0)
            {
                // Fallback: match line-only MethodName(args) without brackets
                // (some models omit the square brackets)
                matches = System.Text.RegularExpressions.Regex.Matches(text, @"^(\w+)\((.*)\)\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
            }

            if (matches.Count == 0)
            {
                // Fallback: backtick-wrapped tool calls — `MethodName(args)` or ```MethodName(args)```
                // (some models wrap tool calls in markdown code formatting)
                matches = System.Text.RegularExpressions.Regex.Matches(text, @"^`+(\w+)\(((?:[^'""()]*|'[^']*'|""[^""]*"")*)\)`+\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
            }

            if (matches.Count == 0)
            {
                // Fallback: MethodName(args) at start of line followed by non-tool text
                // Handles cases like: FindFaceEmo()を呼び出して... or ListBlendShapesEx("Body")を確認
                // Uses balanced parentheses matching for simple args
                var lenientMatches = System.Text.RegularExpressions.Regex.Matches(text,
                    @"^(\w+)\(([^)]*)\)",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                // Only accept if at least one match is a known tool name
                var toolNames = new HashSet<string>(
                    GetToolMethods().Select(m => m.Name),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var mn in MCPManager.GetToolNames()) toolNames.Add(mn);
                foreach (var mn in BuiltinMCPToolNames) toolNames.Add(mn);
                var validMatches = new List<System.Text.RegularExpressions.Match>();
                foreach (System.Text.RegularExpressions.Match lm in lenientMatches)
                {
                    if (lm.Success && toolNames.Contains(lm.Groups[1].Value))
                        validMatches.Add(lm);
                }
                if (validMatches.Count > 0)
                {
                    // Re-run with the same regex — matches already validated above
                    matches = lenientMatches;
                }
            }

            // --- Supplemental: detect tool calls embedded in running text ---
            // Earlier stages may miss unbracketed calls mid-sentence
            // (e.g. "次に AnalyzeGimmickStructure("TK") で確認")
            // This pass always runs and adds non-overlapping, tool-name-validated matches.
            var matchList = new List<System.Text.RegularExpressions.Match>();
            foreach (System.Text.RegularExpressions.Match m in matches)
                if (m.Success) matchList.Add(m);

            {
                var inlineToolNames = new HashSet<string>(
                    GetToolMethods().Select(m => m.Name),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var mn in MCPManager.GetToolNames()) inlineToolNames.Add(mn);
                foreach (var mn in BuiltinMCPToolNames) inlineToolNames.Add(mn);
                var inlineMatches = System.Text.RegularExpressions.Regex.Matches(text,
                    @"(?<!\.)(\w+)\(((?:[^'""()]*|'[^']*'|""[^""]*"")*)\)");
                foreach (System.Text.RegularExpressions.Match im in inlineMatches)
                {
                    if (!im.Success || !inlineToolNames.Contains(im.Groups[1].Value))
                        continue;
                    // Skip if overlapping with an existing match
                    bool overlaps = false;
                    foreach (var existing in matchList)
                    {
                        if (im.Index < existing.Index + existing.Length
                            && im.Index + im.Length > existing.Index)
                        { overlaps = true; break; }
                    }
                    if (!overlaps)
                        matchList.Add(im);
                }
            }

            // --- Enforce single-tool-per-turn ---
            // Only execute the first tool call found in the response.
            // This prevents hallucination cascades where the LLM fabricates tool results
            // and chains additional calls based on imagined data.
            _firstToolEndIndex = -1;
            if (matchList.Count > 0)
            {
                // Sort by position to ensure we pick the first occurrence in text
                matchList.Sort((a, b) => a.Index.CompareTo(b.Index));
                var first = matchList[0];
                _firstToolEndIndex = first.Index + first.Length;
                if (matchList.Count > 1)
                {
                    int ignored = matchList.Count - 1;
                    matchList.RemoveRange(1, ignored);
                    onStatus?.Invoke($"[INFO] {ignored} additional tool call(s) ignored (1-tool-per-turn policy).");
                }
            }

            // --- Batch confirmation pre-scan ---
            HashSet<string> batchApproved = null;
            bool batchUsed = false;
            if (!ToolConfirmState.SessionSkipAll && matchList.Count >= 2)
            {
                var confirmNeeded = new List<BatchToolItem>();
                foreach (var m in matchList)
                {
                    if (!m.Success) continue;
                    string mName = m.Groups[1].Value;
                    var mMethod = GetToolMethods().FirstOrDefault(mt =>
                        string.Equals(mt.Name, mName, StringComparison.OrdinalIgnoreCase));
                    if (mMethod != null && AgentSettings.IsToolConfirmRequired(mMethod.Name))
                    {
                        var mAttr = ToolRegistry.GetAgentToolAttribute(mMethod);
                        confirmNeeded.Add(new BatchToolItem
                        {
                            toolName = mMethod.Name,
                            description = mAttr?.Description ?? mMethod.Name,
                            parameters = m.Groups[2].Value.Trim(),
                            approved = true
                        });
                    }
                }

                if (confirmNeeded.Count >= 2)
                {
                    BatchToolConfirmState.Request(confirmNeeded);
                    onStatus?.Invoke("__BATCH_TOOL_CONFIRM__");

                    while (!BatchToolConfirmState.IsResolved)
                    {
                        if (!_isProcessing) yield break;
                        yield return null;
                    }

                    batchApproved = BatchToolConfirmState.ApprovedTools ?? new HashSet<string>();
                    batchUsed = true;

                    // Check if session skip was set via batch UI
                    if (ToolConfirmState.SessionSkipAll)
                        batchUsed = false; // Skip individual confirmations too

                    BatchToolConfirmState.Clear();
                }
            }

            foreach (var match in matchList)
            {
                if (!_isProcessing) yield break;

                if (match.Success)
                {
                    var methodName = match.Groups[1].Value;
                    var argsString = match.Groups[2].Value;
                    var argsRaw = SplitArguments(argsString);

                    var method = GetToolMethods().FirstOrDefault(m =>
                        string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));
                    if (method != null)
                    {
                        // Invoke tool — split into arg parsing, confirmation, and invocation.
                        // Confirmation and async tools use yield, which C# forbids inside
                        // try-catch (CS1626), so they run outside try-catch blocks.
                        object rawResult = null;
                        int groupBefore = 0;
                        bool invokeOk = false;
                        bool argsParsed = false;

                        var parameterInfos = method.GetParameters();
                        object[] typedArgs = new object[parameterInfos.Length];

                        // --- Phase 1: Arg parsing (in try-catch) ---
                        try
                        {
                            // Log execution attempt
                            string paramsLog = string.Join(", ", argsRaw.Select(a => a.Trim()));
                            onStatus?.Invoke($"Executing Tool: {methodName}({paramsLog})");

                            // Pre-validate argument count
                            int requiredParamCount = parameterInfos.Count(p => !p.HasDefaultValue);
                            if (argsRaw.Length > parameterInfos.Length)
                            {
                                results.Add(GenerateUsageError(method,
                                    $"Error: Too many arguments for {methodName}. Got {argsRaw.Length}, max {parameterInfos.Length} ({requiredParamCount} required, {parameterInfos.Length - requiredParamCount} optional)."));
                                goto NextMatch;
                            }

                            // Initialize all with defaults
                            for (int i = 0; i < parameterInfos.Length; i++)
                            {
                                if (parameterInfos[i].HasDefaultValue)
                                    typedArgs[i] = parameterInfos[i].DefaultValue;
                            }

                            // Unified parser: support mixed positional and named args
                            int positionalIdx = 0;
                            for (int rawIdx = 0; rawIdx < argsRaw.Length; rawIdx++)
                            {
                                string rawArg = argsRaw[rawIdx].Trim();

                                // Check if this is a named argument (name=value)
                                // Skip named-arg detection for quoted literals (e.g. 'Shrink_A=100;Shrink_B=50')
                                bool isQuotedLiteral = (rawArg.Length >= 2)
                                    && ((rawArg[0] == '\'' && rawArg[rawArg.Length - 1] == '\'')
                                     || (rawArg[0] == '"'  && rawArg[rawArg.Length - 1] == '"'));
                                int eqIdx = isQuotedLiteral ? -1 : rawArg.IndexOf('=');
                                if (eqIdx > 0)
                                {
                                    string possibleName = rawArg.Substring(0, eqIdx).Trim().Trim('\'', '"');
                                    // Only treat as named arg if key is a valid identifier (no spaces/special chars)
                                    if (System.Text.RegularExpressions.Regex.IsMatch(possibleName, @"^\w+$"))
                                    {
                                        string valueAfterEq = UnquoteAndUnescape(rawArg.Substring(eqIdx + 1));

                                        int namedIdx = -1;
                                        for (int i = 0; i < parameterInfos.Length; i++)
                                        {
                                            if (string.Equals(parameterInfos[i].Name, possibleName, StringComparison.OrdinalIgnoreCase))
                                            {
                                                namedIdx = i;
                                                break;
                                            }
                                        }

                                        if (namedIdx >= 0)
                                        {
                                            try
                                            {
                                                typedArgs[namedIdx] = Convert.ChangeType(valueAfterEq, parameterInfos[namedIdx].ParameterType);
                                            }
                                            catch (Exception)
                                            {
                                                results.Add(GenerateUsageError(method,
                                                    $"Error: Cannot convert '{valueAfterEq}' to {parameterInfos[namedIdx].ParameterType.Name} for parameter '{parameterInfos[namedIdx].Name}'."));
                                                goto NextMatch;
                                            }
                                            continue;
                                        }

                                        // Named arg key doesn't match any parameter name — return error with valid names
                                        results.Add(GenerateUsageError(method,
                                            $"Error: Unknown parameter '{possibleName}' for {methodName}. Valid parameter names: {string.Join(", ", parameterInfos.Select(p => p.Name))}"));
                                        goto NextMatch;
                                    }
                                }

                                // Positional arg: assign to next open slot
                                while (positionalIdx < parameterInfos.Length && typedArgs[positionalIdx] != null
                                       && !(parameterInfos[positionalIdx].HasDefaultValue && typedArgs[positionalIdx].Equals(parameterInfos[positionalIdx].DefaultValue)))
                                {
                                    positionalIdx++;
                                }

                                if (positionalIdx < parameterInfos.Length)
                                {
                                    string arg = UnquoteAndUnescape(rawArg);
                                    try
                                    {
                                        typedArgs[positionalIdx] = Convert.ChangeType(arg, parameterInfos[positionalIdx].ParameterType);
                                    }
                                    catch (Exception)
                                    {
                                        results.Add(GenerateUsageError(method,
                                            $"Error: Cannot convert '{arg}' to {parameterInfos[positionalIdx].ParameterType.Name} for parameter '{parameterInfos[positionalIdx].Name}'."));
                                        goto NextMatch;
                                    }
                                    positionalIdx++;
                                }
                            }

                            // Check required params (null check + empty string check for required string params)
                            for (int i = 0; i < parameterInfos.Length; i++)
                            {
                                if (typedArgs[i] == null && !parameterInfos[i].HasDefaultValue)
                                {
                                    results.Add(GenerateUsageError(method, $"Error: Missing REQUIRED argument '{parameterInfos[i].Name}'. This parameter must be provided."));
                                    goto NextMatch;
                                }
                                // Reject empty/whitespace-only strings for required string parameters
                                if (!parameterInfos[i].HasDefaultValue
                                    && parameterInfos[i].ParameterType == typeof(string)
                                    && typedArgs[i] is string strVal
                                    && string.IsNullOrWhiteSpace(strVal))
                                {
                                    results.Add(GenerateUsageError(method, $"Error: REQUIRED parameter '{parameterInfos[i].Name}' cannot be empty. Provide a valid value."));
                                    goto NextMatch;
                                }
                            }

                            argsParsed = true;
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = GenerateUsageError(method, $"Error parsing args for {methodName}: {ex.Message}");
                            results.Add(errorMsg);
                            onStatus?.Invoke($"[Tool Error] {ex.Message}");
                        }

                        if (!argsParsed) goto NextMatch;

                        // --- Phase 1.5: Enabled check (block disabled tools) ---
                        {
                            var toolInfo = ToolRegistry.GetAllTools()
                                .FirstOrDefault(t => t.method == method);
                            if (!AgentSettings.IsToolEnabled(method.Name, toolInfo.isExternal))
                            {
                                results.Add($"Error: Tool '{method.Name}' is disabled. Enable it in tool settings.");
                                onStatus?.Invoke($"[Tool Blocked] {method.Name} is disabled");
                                goto NextMatch;
                            }
                        }

                        // --- Phase 2: Per-tool confirmation (outside try-catch for yield) ---
                        if (batchUsed && AgentSettings.IsToolConfirmRequired(method.Name))
                        {
                            // Batch confirmation was used — check pre-approved set
                            if (!batchApproved.Contains(method.Name))
                            {
                                results.Add($"Cancelled by user: {methodName}");
                                onStatus?.Invoke($"[Tool Result] Cancelled by user: {methodName}");
                                goto NextMatch;
                            }
                        }
                        else if (!ToolConfirmState.SessionSkipAll && AgentSettings.IsToolConfirmRequired(method.Name))
                        {
                            var attr = ToolRegistry.GetAgentToolAttribute(method);
                            string desc = attr?.Description ?? method.Name;
                            string paramsStr = string.Join(", ", argsRaw.Select(a => a.Trim()));

                            ToolConfirmState.Request(method.Name, desc, paramsStr);
                            onStatus?.Invoke("__TOOL_CONFIRM__");

                            // Wait for user selection via in-chat buttons
                            while (ToolConfirmState.SelectedIndex < 0)
                            {
                                if (!_isProcessing) yield break;
                                yield return null;
                            }

                            int selection = ToolConfirmState.SelectedIndex;
                            ToolConfirmState.Clear();

                            if (selection == ToolConfirmState.CANCEL)
                            {
                                results.Add($"Cancelled by user: {method.Name}");
                                onStatus?.Invoke($"[Tool Result] Cancelled by user: {method.Name}");
                                goto NextMatch;
                            }
                            if (selection == ToolConfirmState.APPROVE_AND_DISABLE)
                            {
                                AgentSettings.SetToolConfirmRequired(method.Name, false);
                            }
                            else if (selection == ToolConfirmState.APPROVE_ALL_SESSION)
                            {
                                ToolConfirmState.SessionSkipAll = true;
                            }
                        }

                        // --- Phase 3: Invocation (in try-catch) ---
                        try
                        {
                            groupBefore = Undo.GetCurrentGroup();
                            rawResult = method.Invoke(null, typedArgs);
                            invokeOk = true;
                        }
                        catch (System.Reflection.TargetInvocationException tex)
                        {
                            string innerMsg = tex.InnerException?.Message ?? tex.Message;
                            string errorMsg = GenerateUsageError(method, $"Error executing tool {methodName}: {innerMsg}");
                            results.Add(errorMsg);
                            onStatus?.Invoke($"[Tool Error] {innerMsg}");
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = GenerateUsageError(method, $"Error executing tool {methodName}: {ex.Message}");
                            results.Add(errorMsg);
                            onStatus?.Invoke($"[Tool Error] {ex.Message}");
                        }

                        // Process result outside try-catch (yield is not allowed in try-catch)
                        if (invokeOk)
                        {
                            if (rawResult is IEnumerator enumerator)
                            {
                                // Async tool: run as coroutine, collect last string yield as result
                                string asyncResult = null;
                                while (enumerator.MoveNext())
                                {
                                    if (!_isProcessing)
                                    {
                                        (enumerator as IDisposable)?.Dispose();
                                        ToolProgress.Clear();
                                        yield break;
                                    }
                                    if (enumerator.Current is string str)
                                        asyncResult = str;
                                    else
                                        yield return enumerator.Current;
                                }
                                ToolProgress.Clear();
                                int groupAfter = Undo.GetCurrentGroup();
                                _sessionUndoCount += Mathf.Max(0, groupAfter - groupBefore);
                                string resStr = asyncResult ?? "Error: Async tool completed without result.";
                                results.Add(resStr);
                                onStatus?.Invoke($"[Tool Result] {resStr}");
                            }
                            else
                            {
                                // Sync tool: use result directly
                                int groupAfter = Undo.GetCurrentGroup();
                                _sessionUndoCount += Mathf.Max(0, groupAfter - groupBefore);
                                string resStr = rawResult?.ToString() ?? "Success (No return value)";
                                results.Add(resStr);
                                onStatus?.Invoke($"[Tool Result] {resStr}");
                            }
                        }
                    }
                    else if (BuiltinMCPToolNames.Contains(methodName))
                    {
                        // Built-in MCP resource/prompt tools
                        onStatus?.Invoke($"Executing: {methodName}");
                        string mcpBuiltinResult = null;
                        string mcpBuiltinError = null;

                        if (methodName == "ListMCPResources")
                        {
                            var allRes = MCPManager.GetAllResources();
                            if (allRes.Count == 0)
                                mcpBuiltinResult = "No MCP resources available.";
                            else
                            {
                                var sb = new StringBuilder();
                                foreach (var (srvName, res) in allRes)
                                {
                                    string d = !string.IsNullOrEmpty(res.Description) ? $" — {res.Description}" : "";
                                    sb.AppendLine($"[{srvName}] {res.Name} ({res.Uri}){d}");
                                }
                                mcpBuiltinResult = sb.ToString();
                            }
                        }
                        else if (methodName == "ListMCPPrompts")
                        {
                            var allP = MCPManager.GetAllPrompts();
                            if (allP.Count == 0)
                                mcpBuiltinResult = "No MCP prompts available.";
                            else
                            {
                                var sb = new StringBuilder();
                                foreach (var (srvName, p) in allP)
                                {
                                    string d = !string.IsNullOrEmpty(p.Description) ? $" — {p.Description}" : "";
                                    string argList = "";
                                    if (p.Arguments != null && p.Arguments.Count > 0)
                                        argList = $" args: [{string.Join(", ", p.Arguments.Select(a => a.Required ? a.Name + " [REQUIRED]" : a.Name))}]";
                                    sb.AppendLine($"[{srvName}] {p.Name}{d}{argList}");
                                }
                                mcpBuiltinResult = sb.ToString();
                            }
                        }
                        else if (methodName == "ReadMCPResource")
                        {
                            string uri = argsRaw.Length > 0 ? argsRaw[0].Trim().Trim('\'', '"') : "";
                            if (string.IsNullOrEmpty(uri))
                                mcpBuiltinError = "ReadMCPResource requires a URI argument.";
                            else
                            {
                                var found = MCPManager.FindResource(uri);
                                if (!found.HasValue)
                                    mcpBuiltinError = $"Resource not found: {uri}. Use ListMCPResources() to see available resources.";
                                else
                                {
                                    yield return found.Value.client.ReadResource(uri,
                                        r => mcpBuiltinResult = r,
                                        e => mcpBuiltinError = e);
                                }
                            }
                        }
                        else if (methodName == "GetMCPPrompt")
                        {
                            string promptName = argsRaw.Length > 0 ? argsRaw[0].Trim().Trim('\'', '"') : "";
                            if (string.IsNullOrEmpty(promptName))
                                mcpBuiltinError = "GetMCPPrompt requires a prompt name argument.";
                            else
                            {
                                var found = MCPManager.FindPrompt(promptName);
                                if (!found.HasValue)
                                    mcpBuiltinError = $"Prompt not found: {promptName}";
                                else
                                {
                                    JNode promptArgs = JNode.Obj();
                                    if (argsRaw.Length > 1)
                                    {
                                        string argsJson = argsRaw[1].Trim().Trim('\'', '"');
                                        var parsed = JNode.Parse(argsJson);
                                        if (parsed != null && parsed.Type == JNode.JType.Object)
                                            promptArgs = parsed;
                                        else if (!string.IsNullOrWhiteSpace(argsJson))
                                            mcpBuiltinError = $"Invalid JSON for prompt arguments: {argsJson}";
                                    }
                                    if (mcpBuiltinError == null)
                                    {
                                        yield return found.Value.client.GetPrompt(promptName, promptArgs,
                                            r => mcpBuiltinResult = r,
                                            e => mcpBuiltinError = e);
                                    }
                                }
                            }
                        }

                        if (!_isProcessing) yield break;

                        if (mcpBuiltinError != null)
                            results.Add($"Error: {mcpBuiltinError}");
                        else
                            results.Add(mcpBuiltinResult ?? "(empty)");
                    }
                    else if (MCPManager.HasTool(methodName))
                    {
                        // MCP tool — execute via MCP server
                        var mcpResult = MCPManager.GetTool(methodName);
                        if (mcpResult.HasValue)
                        {
                            var (mcpClient, mcpTool) = mcpResult.Value;
                            onStatus?.Invoke($"Executing MCP Tool: {mcpClient.ServerName}/{methodName}");

                            // Build JSON arguments from positional/named args
                            var jsonArgs = MCPManager.BuildArguments(mcpTool, argsRaw);

                            string mcpResultText = null;
                            string mcpError = null;

                            yield return mcpClient.CallTool(methodName, jsonArgs,
                                r => mcpResultText = r,
                                e => mcpError = e);

                            if (!_isProcessing) yield break;

                            if (mcpError != null)
                            {
                                results.Add($"MCP Error ({mcpClient.ServerName}/{methodName}): {mcpError}");
                                onStatus?.Invoke($"[MCP Error] {mcpError}");
                            }
                            else
                            {
                                string resStr = mcpResultText ?? "(empty)";
                                results.Add(resStr);
                                onStatus?.Invoke($"[MCP Result] {mcpClient.ServerName}/{methodName}");
                            }
                        }
                    }
                    else
                    {
                        var allTools = GetToolMethods();
                        var suggestions = allTools
                            .Where(m => m.Name.IndexOf(methodName, StringComparison.OrdinalIgnoreCase) >= 0
                                     || methodName.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(m => m.Name)
                            .Take(5)
                            .ToList();
                        if (suggestions.Count > 0)
                            results.Add($"Error: Tool '{methodName}' not found. Did you mean: {string.Join(", ", suggestions)}? Use SearchTools(\"{methodName}\") to find tools.");
                        else
                            results.Add($"Error: Tool '{methodName}' not found. Use SearchTools(\"{methodName}\") to find available tools.");
                    }
                }
                
                // Yield between tool executions to prevent editor from freezing
                yield return null;
                NextMatch:;
            }
        }

        /// <summary>
        /// Decode a raw argument token produced by SplitArguments into a plain string.
        ///
        /// Supported forms:
        /// - <c>"""...content..."""</c> — raw multi-line string; content is returned verbatim with NO
        ///   escape processing. Use this for file contents that contain quotes, backslashes, or newlines.
        /// - <c>"..."</c> or <c>'...'</c> — conventional quoted string; standard escape sequences
        ///   (\", \', \\, \n, \r, \t, \0, \b, \f, \/) are processed.
        /// - Unquoted tokens are returned as-is (trimmed).
        ///
        /// This is the inverse of what the model emits when it passes string literals inside [Tool(args)] calls.
        /// </summary>
        private static string UnquoteAndUnescape(string rawArg)
        {
            if (rawArg == null) return null;
            string s = rawArg.Trim();
            if (s.Length < 2) return s;

            // Raw triple-quoted string: strip """ from both ends, return inner content verbatim.
            if (s.Length >= 6 && s.StartsWith("\"\"\"", StringComparison.Ordinal)
                && s.EndsWith("\"\"\"", StringComparison.Ordinal))
            {
                return s.Substring(3, s.Length - 6);
            }

            char first = s[0];
            char last = s[s.Length - 1];
            bool isQuoted = (first == '"' && last == '"') || (first == '\'' && last == '\'');
            if (!isQuoted) return s;

            string inner = s.Substring(1, s.Length - 2);
            var sb = new StringBuilder(inner.Length);
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '\\' && i + 1 < inner.Length)
                {
                    char next = inner[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  i++; continue;
                        case '\'': sb.Append('\''); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case '/':  sb.Append('/');  i++; continue;
                        case 'n':  sb.Append('\n'); i++; continue;
                        case 'r':  sb.Append('\r'); i++; continue;
                        case 't':  sb.Append('\t'); i++; continue;
                        case '0':  sb.Append('\0'); i++; continue;
                        case 'b':  sb.Append('\b'); i++; continue;
                        case 'f':  sb.Append('\f'); i++; continue;
                        default:   sb.Append(c); continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Split tool arguments respecting quoted strings.
        /// e.g. "'path', '0.1,0.2,0.3', 'tint'" → ["'path'", "'0.1,0.2,0.3'", "'tint'"]
        ///
        /// Supported quoting styles:
        /// - 'single' / "double" — conventional quoted strings (honors \-escapes for embedded quotes)
        /// - """triple-quoted""" — raw multi-line string (NO escape processing, any content including
        ///   newlines and embedded quotes is taken verbatim until the next """)
        /// </summary>
        private static string[] SplitArguments(string argsString)
        {
            if (string.IsNullOrWhiteSpace(argsString))
                return new string[0];

            var args = new List<string>();
            var current = new StringBuilder();
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool inTripleQuote = false;
            int bracketDepth = 0;
            int braceDepth = 0;

            for (int i = 0; i < argsString.Length; i++)
            {
                char c = argsString[i];

                // Inside a triple-quoted raw string: copy everything verbatim until we meet `"""`.
                // No escape processing, no comma splitting, no bracket tracking.
                if (inTripleQuote)
                {
                    if (c == '"' && i + 2 < argsString.Length && argsString[i + 1] == '"' && argsString[i + 2] == '"')
                    {
                        current.Append("\"\"\"");
                        inTripleQuote = false;
                        i += 2;
                        continue;
                    }
                    current.Append(c);
                    continue;
                }

                // Detect opening triple-quote before any other quote handling.
                if (!inSingleQuote && !inDoubleQuote
                    && c == '"' && i + 2 < argsString.Length
                    && argsString[i + 1] == '"' && argsString[i + 2] == '"')
                {
                    current.Append("\"\"\"");
                    inTripleQuote = true;
                    i += 2;
                    continue;
                }

                // Inside a quoted string, honor backslash escapes so that \" and \' do not
                // prematurely terminate the string and so that \\ does not confuse the
                // quote-state tracker. We copy both the backslash and the escaped char
                // verbatim here — the actual escape processing happens later in
                // UnquoteAndUnescape when the individual argument is decoded.
                if ((inSingleQuote || inDoubleQuote) && c == '\\' && i + 1 < argsString.Length)
                {
                    current.Append(c);
                    current.Append(argsString[i + 1]);
                    i++;
                    continue;
                }
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    current.Append(c);
                }
                else if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    current.Append(c);
                }
                else if (c == '[' && !inSingleQuote && !inDoubleQuote)
                {
                    bracketDepth++;
                    current.Append(c);
                }
                else if (c == ']' && !inSingleQuote && !inDoubleQuote)
                {
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    current.Append(c);
                }
                else if (c == '{' && !inSingleQuote && !inDoubleQuote)
                {
                    braceDepth++;
                    current.Append(c);
                }
                else if (c == '}' && !inSingleQuote && !inDoubleQuote)
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                    current.Append(c);
                }
                else if (c == '#' && !inSingleQuote && !inDoubleQuote && bracketDepth == 0 && braceDepth == 0)
                {
                    // Skip inline comment until end of line
                    while (i < argsString.Length && argsString[i] != '\n')
                        i++;
                }
                else if (c == ',' && !inSingleQuote && !inDoubleQuote && bracketDepth == 0 && braceDepth == 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                args.Add(current.ToString());

            return args.ToArray();
        }

        private static readonly Dictionary<string, string> ParamHints = new Dictionary<string, string>
        {
            { "ApplyGradientEx.fromColor", "'#RRGGBB'|'R,G,B'|'transparent'" },
            { "ApplyGradientEx.toColor", "'#RRGGBB'|'R,G,B'|'transparent'" },
            { "ApplyGradientEx.blendMode", "screen|overlay|tint|multiply|replace" },
            { "ApplyGradientEx.direction", "top_to_bottom|bottom_to_top|left_to_right|right_to_left" },
            { "AdjustHSV.hueShift", "-180..+180 RELATIVE" },
            { "AdjustHSV.saturationScale", "0=grayscale,1=unchanged,2=vivid" },
            { "AdjustHSV.valueScale", "0=black,1=unchanged,1.5=brighter" },
            { "GenerateTextureWithAI.textureProperty", "'_MainTex'|'_EmissionMap'|'_BumpMap'" },
        };

        private string GenerateUsageError(MethodInfo method, string errorMessage)
        {
            var sb = new StringBuilder();
            sb.AppendLine(errorMessage);

            var parameters = method.GetParameters();
            int requiredCount = parameters.Count(p => !p.HasDefaultValue);
            int optionalCount = parameters.Length - requiredCount;

            sb.Append("Expected usage: [");
            sb.Append(method.Name);
            sb.Append("(");

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.HasDefaultValue)
                {
                    sb.Append($"{p.ParameterType.Name} {p.Name}");
                    if (p.DefaultValue == null) sb.Append(" = null");
                    else if (p.DefaultValue is string) sb.Append($" = \"{p.DefaultValue}\"");
                    else if (p.DefaultValue is bool) sb.Append($" = {p.DefaultValue.ToString().ToLower()}");
                    else sb.Append($" = {p.DefaultValue}");
                }
                else
                {
                    sb.Append($"{p.ParameterType.Name} {p.Name} [REQUIRED]");
                }
                if (ParamHints.TryGetValue($"{method.Name}.{p.Name}", out var hint))
                    sb.Append($" ({hint})");

                if (i < parameters.Length - 1) sb.Append(", ");
            }

            sb.Append(")]");
            sb.AppendLine();
            sb.Append($"Parameters: {requiredCount} REQUIRED, {optionalCount} optional. You MUST provide all REQUIRED parameters.");
            return sb.ToString();
        }

        private string GetSystemPrompt()
        {
            var sb = new StringBuilder();

            // Section 1: Identity & Syntax
            sb.AppendLine("You are an AI Agent for Unity Editor. You can manipulate the project using tools.");
            sb.AppendLine("Use [MethodName(arg1, arg2)] to call tools. Use SearchTools(\"keyword\") to find tools and see parameters.");
            sb.AppendLine("Use AskUser(question, option1, option2, ...) to present choices. option1 and option2 are REQUIRED (minimum 2 options). The user can also ignore the options and type a free-text response in the input field. Use importance='warning' for side effects, 'critical' for destructive operations.");
            sb.AppendLine("\n<string_literal_syntax>");
            sb.AppendLine("STRING LITERALS IN TOOL ARGUMENTS — two forms are available:");
            sb.AppendLine("");
            sb.AppendLine("1. Regular quoted strings: \"text\" or 'text'");
            sb.AppendLine("   - Use standard escapes for special chars: \\\" (double quote), \\' (single quote), \\\\ (backslash), \\n (newline), \\r, \\t");
            sb.AppendLine("   - Good for short, single-line values.");
            sb.AppendLine("   - Example: [WriteFile(\"Assets/foo.txt\", \"hello\\nworld\")]");
            sb.AppendLine("");
            sb.AppendLine("2. **Triple-quoted raw strings: \"\"\"text\"\"\"** (RECOMMENDED for file content / multi-line text)");
            sb.AppendLine("   - NO escape processing. Content is taken verbatim, including real newlines, quotes, and backslashes.");
            sb.AppendLine("   - Ends at the next \"\"\" sequence — so you CANNOT put \"\"\" inside a triple-quoted string.");
            sb.AppendLine("   - Use this for writing C# scripts, shaders, JSON, etc. so you do not have to escape every \" or \\.");
            sb.AppendLine("   - Example:");
            sb.AppendLine("     [WriteFile(\"Assets/HelloWorld.cs\", \"\"\"using UnityEngine;");
            sb.AppendLine("     public class HelloWorld : MonoBehaviour");
            sb.AppendLine("     {");
            sb.AppendLine("         void Start() { Debug.Log(\"こんにちは世界\"); }");
            sb.AppendLine("     }\"\"\")]");
            sb.AppendLine("");
            sb.AppendLine("Prefer triple-quoted strings whenever a value contains quotes, backslashes, or newlines. This avoids the common pitfall of double-escaping that produces literal \\\" or \\n in output files.");
            sb.AppendLine("</string_literal_syntax>");
            sb.AppendLine("\n<argument_rules>");
            sb.AppendLine("STRICT ARGUMENT REQUIREMENTS:");
            sb.AppendLine("- Parameters WITHOUT a default value are REQUIRED. You MUST provide them. Omitting required arguments causes an error.");
            sb.AppendLine("- Parameters WITH a default value (shown as '= value') are optional.");
            sb.AppendLine("- NEVER pass empty strings ('') for required string parameters. An empty string is NOT a valid value.");
            sb.AppendLine("- ALWAYS call SearchTools(\"keyword\") first for specialized tools to see exact parameter signatures before calling them.");
            sb.AppendLine("- When SearchTools shows 'REQUIRED' next to a parameter, you MUST provide it.");
            sb.AppendLine("- If you are unsure what value to use for a required parameter, use inspection tools or AskUser — do NOT guess or omit.");
            sb.AppendLine("</argument_rules>");

            // Section 2: Available Tools — Core (signatures) + Specialized (category summary)
            var toolMethods = ToolRegistry.GetEnabledMethods();

            // Core tools: full signatures grouped by category
            var coreTools = toolMethods.Where(m => CoreToolNames.Contains(m.Name)).ToList();
            var coreCategoryOrder = new[] { "Discovery", "Inspect", "Edit", "SceneView", "Assets" };
            var coreByCategory = new Dictionary<string, List<MethodInfo>>();
            foreach (var m in coreTools)
            {
                string cat;
                if (m.Name == "SearchTools" || m.Name == "ListTools" || m.Name == "AskUser"
                    || m.Name == "SearchSkills" || m.Name == "ReadSkill")
                    cat = "Discovery";
                else if (m.Name == "InspectGameObject" || m.Name == "DeepInspectComponent"
                    || m.Name == "ListRenderers" || m.Name == "ListChildren"
                    || m.Name == "GetHierarchyTree" || m.Name == "ListRootObjects"
                    || m.Name == "FindGameObject")
                    cat = "Inspect";
                else if (m.Name == "SetActive" || m.Name == "SetProperty"
                    || m.Name == "CreateGameObject" || m.Name == "SetParent")
                    cat = "Edit";
                else if (m.Name == "CaptureSceneView" || m.Name == "ScanAvatarMeshes"
                    || m.Name == "CaptureMultiAngle" || m.Name == "FocusSceneView")
                    cat = "SceneView";
                else if (m.Name == "SearchAssets")
                    cat = "Assets";
                else
                    cat = "Other";

                if (!coreByCategory.ContainsKey(cat))
                    coreByCategory[cat] = new List<MethodInfo>();
                coreByCategory[cat].Add(m);
            }

            sb.AppendLine("\nCore Tools (always available — use directly):");
            foreach (var cat in coreCategoryOrder)
            {
                if (!coreByCategory.TryGetValue(cat, out var methods)) continue;
                var signatures = methods.Select(m =>
                {
                    var pars = m.GetParameters();
                    var parNames = string.Join(", ", pars.Select(p =>
                    {
                        string typeName = p.ParameterType == typeof(string) ? "string"
                            : p.ParameterType == typeof(int) ? "int"
                            : p.ParameterType == typeof(float) ? "float"
                            : p.ParameterType == typeof(bool) ? "bool"
                            : p.ParameterType.Name;
                        if (p.HasDefaultValue)
                        {
                            string defVal;
                            if (p.DefaultValue == null) defVal = "null";
                            else if (p.DefaultValue is string s) defVal = $"\"{s}\"";
                            else if (p.DefaultValue is bool b) defVal = b ? "true" : "false";
                            else defVal = p.DefaultValue.ToString();
                            return $"{typeName} {p.Name}={defVal}";
                        }
                        return $"{typeName} {p.Name} [REQUIRED]";
                    }));
                    return $"{m.Name}({parNames})";
                });
                sb.AppendLine($"  {cat}: {string.Join(", ", signatures)}");
            }

            // Specialized tools: category name + count only
            var specializedTools = toolMethods.Where(m => !CoreToolNames.Contains(m.Name)).ToList();
            var specializedGrouped = specializedTools
                .GroupBy(m => m.DeclaringType.Name.Replace("Tools", ""))
                .OrderBy(g => g.Key);
            var specializedSummaries = specializedGrouped
                .Select(g => $"{g.Key}({g.Count()})");
            sb.AppendLine($"\nSpecialized Tools ({specializedTools.Count} total — MUST SearchTools(\"keyword\") before use):");
            sb.AppendLine($"  {string.Join(", ", specializedSummaries)}");

            // Section 2b: MCP Tools (from external MCP servers)
            var mcpTools = MCPManager.GetAllTools();
            if (mcpTools.Count > 0)
            {
                sb.AppendLine("\n  --- MCP Tools (external servers) ---");
                foreach (var (serverName, tool) in mcpTools)
                {
                    string paramList = "";
                    if (tool.Params != null && tool.Params.Count > 0)
                    {
                        var paramParts = tool.Params.Select(p =>
                        {
                            string typeStr = !string.IsNullOrEmpty(p.Type) ? p.Type + " " : "";
                            string reqStr = p.Required ? " [REQUIRED]" : "";
                            return $"{typeStr}{p.Name}{reqStr}";
                        });
                        paramList = string.Join(", ", paramParts);
                    }
                    string desc = !string.IsNullOrEmpty(tool.Description)
                        ? $" — {tool.Description}" : "";
                    sb.AppendLine($"  {tool.Name}({paramList}){desc}");

                    // パラメータの説明を出力（required のみ）
                    if (tool.Params != null)
                    {
                        foreach (var p in tool.Params)
                        {
                            if (p.Required && !string.IsNullOrEmpty(p.Description))
                                sb.AppendLine($"    {p.Name}: {p.Description}");
                        }
                    }
                }
                sb.AppendLine("  Call MCP tools using just the tool name, e.g., [get_current_config()] not [MCP/serena.get_current_config()].");

                // MCP Resources (if any)
                var mcpResources = MCPManager.GetAllResources();
                if (mcpResources.Count > 0)
                {
                    sb.AppendLine("\n  --- MCP Resources (available context) ---");
                    foreach (var (srvName, res) in mcpResources)
                    {
                        string resDesc = !string.IsNullOrEmpty(res.Description) ? $" — {res.Description}" : "";
                        sb.AppendLine($"  [{srvName}] {res.Name} ({res.Uri}){resDesc}");
                    }
                    sb.AppendLine("  To read a resource: [ReadMCPResource(\"uri\")]");
                    sb.AppendLine("  To list all resources: [ListMCPResources()]");
                }

                // MCP Prompts (if any)
                var mcpPrompts = MCPManager.GetAllPrompts();
                if (mcpPrompts.Count > 0)
                {
                    sb.AppendLine("\n  --- MCP Prompts (templates) ---");
                    foreach (var (srvName, p) in mcpPrompts)
                    {
                        string pDesc = !string.IsNullOrEmpty(p.Description) ? $" — {p.Description}" : "";
                        string argInfo = "";
                        if (p.Arguments != null && p.Arguments.Count > 0)
                            argInfo = $" args: [{string.Join(", ", p.Arguments.Select(a => a.Required ? a.Name + " [REQUIRED]" : a.Name))}]";
                        sb.AppendLine($"  [{srvName}] {p.Name}{pDesc}{argInfo}");
                    }
                    sb.AppendLine("  To get a prompt: [GetMCPPrompt(\"name\", '{\"arg\": \"value\"}')]");
                    sb.AppendLine("  To list all prompts: [ListMCPPrompts()]");
                }
            }

            // Section 3: Core Rules
            // Dynamic language from UILanguage setting
            string lang = AgentSettings.UILanguage;
            var langEntry = AgentSettings.SupportedLanguages
                .FirstOrDefault(l => l.code == lang);
            string langLabel = langEntry.label ?? lang;

            sb.AppendLine("\n<rules>");
            sb.AppendLine($"- Answer in {langLabel} ({lang}).");
            sb.AppendLine("- EXACTLY ONE TOOL PER TURN: You may call EXACTLY ONE tool per response. After writing [MethodName(args)], you MUST STOP generating immediately. Do NOT write any text after the tool call. Do NOT call a second tool. The system will execute the tool and return the real result in the next message.");
            sb.AppendLine("- NEVER HALLUCINATE RESULTS: You do NOT know what a tool will return. NEVER predict, assume, or fabricate tool output. NEVER invent GameObject paths, material paths, or property values. Wait for the actual system response before planning your next action.");
            sb.AppendLine("- RESPONSE STRUCTURE: Write your reasoning FIRST, then end with ONE tool call as the LAST line. Example format:\n  思考: [your reasoning here]\n  [ToolName(args)]");
            sb.AppendLine("- Complete ONLY what the user explicitly requested. When done, summarize and STOP. Do NOT chain into unrequested steps (e.g., placing avatar ≠ setting up outfits; outfit setup ≠ creating toggles/menus/PhysBones). One task at a time.");
            sb.AppendLine("- Before each tool call, write a 1-line reason on the preceding line explaining WHAT you will do and WHY. This prevents wrong arguments.");
            sb.AppendLine("- Tool calls MUST be on their own line. Do NOT put comments (# ...) inside tool call arguments.");
            sb.AppendLine("- Do NOT output \"Tool Output:\" yourself. The system provides it.");
            sb.AppendLine("- Do NOT repeat the same tool call if it already succeeded.");
            sb.AppendLine("- If a tool call FAILS, read the error carefully — it shows expected parameters with REQUIRED/optional markers. Fix ALL required arguments before retrying. After 2 consecutive failures, try a different approach or [AskUser(\"question\", \"option1\", \"option2\")].");
            sb.AppendLine("- Use SearchTools/ListTools to discover tool parameters. Use SearchSkills/ReadSkill for multi-step procedures.");
            sb.AppendLine("- When [Hierarchy Selection] or [Project Selection] appears, use the full path as gameObjectName parameter.");
            sb.AppendLine("- After visible changes: FIRST call CaptureSceneView() to verify, THEN [AskUser(\"結果はいかがですか？\", \"OK\", \"やり直し\", \"微調整したい\")] to confirm with user.");
            sb.AppendLine("- Some tools require user confirmation before execution (the system handles this automatically).");
            sb.AppendLine("- Vague or subjective requests (\"かわいくして\", \"かっこよくして\", \"improve it\"): [AskUser(\"具体的にどうしますか？\", \"option1\", \"option2\", \"option3\")] で2-4個の具体的選択肢を提示。美的判断を推測しない。");
            sb.AppendLine("- Read-only intent (\"確認して\", \"見せて\", \"教えて\", \"調べて\", \"チェックして\"): Inspect and report ONLY. NEVER modify anything unless the user explicitly asks for changes afterward.");
            sb.AppendLine("- No target specified (\"色を変えて\" without object name): [AskUser(\"どのオブジェクトですか？\", ...)] で対象を確認。自動選択しない。複数アバター時は必ず確認。");
            sb.AppendLine("- Undo requests (\"元に戻して\", \"やり直し\", \"取り消して\"): You cannot undo previous operations. Tell the user to use Unity's Edit > Undo (Ctrl+Z). Do NOT attempt to reverse changes manually.");
            sb.AppendLine("- MANDATORY VISUAL DISCOVERY: Before modifying ANY mesh (color, texture, material), call ScanAvatarMeshes(avatarRoot) to visually identify all meshes. Object names do NOT reliably indicate what the mesh is (e.g., 'Body' may be a head mesh). You MUST see each mesh to know what it is. NEVER guess based on names alone.");
            sb.AppendLine("- MANDATORY TOOL SEARCH: For ANY operation beyond Core Tools (color change, toggle, outfit, PhysBone, expression, animation, material modification), you MUST call SearchTools(\"keyword\") first to find the correct tool and learn its parameters. Core Tools above can be used directly. Do NOT guess tool names or parameters for specialized tools.");
            sb.AppendLine("- MANDATORY SKILL LOOKUP: Before complex operations (color change, outfit setup, PhysBone, expression, toggle), call ReadSkill('relevant-skill') to learn the correct procedure and avoid known mistakes. Skip only if you already read the skill in this conversation.");
            sb.AppendLine("- MANDATORY VISUAL VERIFICATION: After ANY visual change (color, texture, material), call CaptureSceneView() to see the actual result. Then show the screenshot and ask the user for confirmation. Do NOT skip this step.");
            sb.AppendLine("</rules>");

            // Section 4: Inspect-First Principle
            sb.AppendLine("\n<inspect_first>");
            sb.AppendLine("- NEVER ask the user about component types, property names, object structure, GameObject names, or error details. Inspect it yourself using tools.");
            sb.AppendLine("- No selection context → call ListRootObjects() or GetHierarchyTree() first.");
            sb.AppendLine("- User mentions a GameObject → InspectGameObject / DeepInspectComponent before asking questions.");
            sb.AppendLine("- User reports an error → inspect the scene yourself (ListRootObjects, InspectGameObject, etc.) instead of asking the user to paste error messages or provide object names.");
            sb.AppendLine("- Only ask when info cannot be obtained from inspection (user intent, aesthetic preference).");
            sb.AppendLine("</inspect_first>");

            // Section 5: Critical Anti-Patterns
            sb.AppendLine("\n<anti_patterns>");
            sb.AppendLine("- Color/texture changes: ReadSkill('texture-editing') FIRST. Use ApplyGradientEx (set color) or AdjustHSV (brightness/saturation). NEVER SetMaterialProperty on lilToon.");
            sb.AppendLine("- Visibility (\"非表示にして\", \"hide\", \"消して\"): SetActive(gameObjectName, false). This is editor-only visibility. VRChat in-game toggles (\"トグルを作って\", \"切り替えられるようにして\"): SearchTools(\"toggle\") first. NEVER use SetupObjectToggle unless user explicitly requests VRChat gimmick. FaceEmo is ONLY for facial expressions.");
            sb.AppendLine("- Outfit setup: ReadSkill('outfit-setup'). Accessories: ReadSkill('accessory-setup'). NEVER guess coordinates with SetTransform.");
            sb.AppendLine("- FaceEmo / expressions / gestures (\"FaceEmoを適用して\", \"表情を作って\", \"表情メニュー\", \"ジェスチャーで…\", \"表情を見せて\", \"表情を消して\", \"デフォルト表情\", \"AFK表情\", \"ウインク\", \"笑顔\"): ReadSkill('face-emo'). Use FaceEmo tools for ALL expression work. NEVER guess BlendShape names — always use SearchExpressionShapes with filter.");
            sb.AppendLine("- Animation clips (non-expression): use CreateExpressionClipFromData. For expression clips, use FaceEmo tools (CreateAndRegisterExpression, CreateExpressionFromData). NEVER use CreateAnimationClip + SetAnimationCurve.");
            sb.AppendLine("- Custom patterns/designs: use GenerateTextureWithAI. Do NOT give up when no asset is found.");
            sb.AppendLine("- Partial coloring: EnableIslandSelectionMode → user clicks → GetSelectedIslands → apply with islandIndices. Do NOT use for full-object changes.");
            sb.AppendLine("- lilToon effects: ReadSkill('liltoon-effects'). ScrollRotate ONLY works when a TEXTURE is assigned.");
            sb.AppendLine("- Shader creation: WriteFile + ChangeMaterialShader, or CreateShaderFile.");
            sb.AppendLine("- PhysBone setup: ReadSkill('physbone-setup'). ApplyPhysBoneTemplate → ConfigurePhysBone.");
            sb.AppendLine("- Troubleshooting: ReadSkill('troubleshooting'). ALWAYS ValidateAvatar + GetAvatarPerformanceStats first.");
            sb.AppendLine("- Batch changes: ReadSkill('batch-operations'). ListRenderers/ListPhysBones to enumerate first.");
            sb.AppendLine("- Vague references (\"○○みたいにして\"): [AskUser(\"具体的に教えてください\", ...)] で確認。想像で適用しない。");
            sb.AppendLine("- PhysBone adjustments: InspectPhysBone first → AskUser with current values → apply after approval.");
            sb.AppendLine("</anti_patterns>");

            // Section 6: Skill References
            sb.AppendLine("\n<skills>");
            sb.AppendLine("Skills are step-by-step guides for complex operations. Use SearchSkills(keyword) to find or ReadSkill(name) to read full instructions.");

            // Section 7: Dynamic Skill Summaries
            string skillSummaries = Tools.SkillTools.GetSkillSummariesForPrompt();
            if (!string.IsNullOrEmpty(skillSummaries))
                sb.Append(skillSummaries);
            sb.AppendLine("</skills>");

            return sb.ToString();
        }

        private static string FormatTokenCount(int tokens)
        {
            if (tokens >= 1000000) return $"{tokens / 1000000f:0.#}M";
            if (tokens >= 1000) return $"{tokens / 1000f:0.#}k";
            return tokens.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex TokenInfoRegex =
            new System.Text.RegularExpressions.Regex(@"\n*\[Tokens: \d+.*?\]$", System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex ThinkingWrapperRegex =
            new System.Text.RegularExpressions.Regex(@"^<Thinking>\n[\s\S]*?\n</Thinking>\n*", System.Text.RegularExpressions.RegexOptions.None);

        /// <summary>
        /// Strip [Tokens: ...] suffix and Thinking wrapper from response text
        /// so the model history only contains the raw model output.
        /// </summary>
        private static string StripDisplayAnnotations(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = TokenInfoRegex.Replace(text, "");
            text = ThinkingWrapperRegex.Replace(text, "");
            return text.Trim();
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }

        private string GetSelectionContext()
        {
            var sb = new StringBuilder();

            // ヒエラルキー選択
            var gameObjects = Selection.gameObjects;
            if (gameObjects.Length > 0)
            {
                sb.AppendLine("[Hierarchy Selection]");
                foreach (var go in gameObjects)
                {
                    string path = GetHierarchyPath(go.transform);
                    sb.AppendLine($"- gameObjectName: {path}");
                }
            }

            // プロジェクトアセット選択（ヒエラルキーと重複しないもの）
            var guids = Selection.assetGUIDs;
            if (guids.Length > 0)
            {
                var hierarchyPaths = new HashSet<string>(
                    gameObjects.Select(go => AssetDatabase.GetAssetPath(go))
                               .Where(p => !string.IsNullOrEmpty(p)));

                var assetLines = new List<string>();
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath) || hierarchyPaths.Contains(assetPath))
                        continue;
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    string typeName = obj != null ? obj.GetType().Name : "Unknown";
                    assetLines.Add($"- {assetPath} ({typeName})");
                }
                if (assetLines.Count > 0)
                {
                    sb.AppendLine("[Project Selection]");
                    foreach (var line in assetLines)
                        sb.AppendLine(line);
                }
            }

            // アイランド選択状態
            if (IslandSelectionState.IsActive && IslandSelectionState.SelectedIndices.Count > 0)
            {
                string indices = IslandSelectionState.GetIslandIndicesString();
                string goPath = IslandSelectionState.GetGameObjectPath();
                sb.AppendLine("[Island Selection]");
                sb.AppendLine($"- gameObjectName: {goPath}");
                sb.AppendLine($"- selectedIslands: {indices}");
                sb.AppendLine($"- count: {IslandSelectionState.SelectedIndices.Count}");
            }

            return sb.ToString().TrimEnd();
        }

        private List<MethodInfo> GetToolMethods()
        {
            return ToolRegistry.GetAllMethods();
        }
    }

    // Since we are in Editor, we need a way to run Coroutines
    /// <summary>停止可能なエディタコルーチンハンドル。</summary>
    public class EditorCoroutineHandle
    {
        internal EditorApplication.CallbackFunction Callback;
        internal bool Stopped;

        /// <summary>コルーチンを停止する。</summary>
        public void Stop()
        {
            if (Stopped) return;
            Stopped = true;
            if (Callback != null)
                EditorApplication.update -= Callback;
        }
    }

    public static class EditorCoroutineUtility
    {
        public static EditorCoroutineHandle StartCoroutineOwnerless(IEnumerator routine) => StartCoroutine(routine, null);

        public static EditorCoroutineHandle StartCoroutine(IEnumerator routine, object owner)
        {
            var handle = new EditorCoroutineHandle();
            EditorApplication.CallbackFunction callback = null;
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(routine);
            IEnumerator lastCompleted = null;

            callback = () =>
            {
                // Stop check
                if (handle.Stopped)
                {
                    EditorApplication.update -= callback;
                    return;
                }

                // Safety check
                if (stack == null || stack.Count == 0)
                {
                    EditorApplication.update -= callback;
                    return;
                }

                IEnumerator currentRoutine = stack.Peek();

                // 1. Check if current instruction is waitable
                if (currentRoutine.Current != null)
                {
                    if (currentRoutine.Current is AsyncOperation asyncOp && !asyncOp.isDone)
                    {
                        return; // Wait for async op
                    }
                    else if (currentRoutine.Current is IEnumerator inner && inner != lastCompleted)
                    {
                        // New child IEnumerator — push it to stack
                        stack.Push(inner);
                        lastCompleted = null;
                        return; // Next loop will execute inner.MoveNext()
                    }
                    // If inner == lastCompleted, the child already completed.
                    // Fall through to advance the parent past this yield.
                }

                // 2. Step forward
                lastCompleted = null;
                bool hasMore = currentRoutine.MoveNext();

                if (!hasMore)
                {
                    lastCompleted = currentRoutine;
                    stack.Pop();
                    if (stack.Count == 0)
                    {
                        EditorApplication.update -= callback;
                    }
                }
            };

            handle.Callback = callback;
            EditorApplication.update += callback;
            // Run first step immediately to catch synchronous errors or starts
            callback();
            return handle;
        }
    }

    [Serializable]
    public class Message
    {
        public string role;
        public Part[] parts;
    }

    [Serializable]
    public class Part
    {
        public string text;
        [NonSerialized] public byte[] imageBytes;
        [NonSerialized] public string imageMimeType;
    }
}
