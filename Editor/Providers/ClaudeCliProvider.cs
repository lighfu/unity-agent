using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    public class ClaudeCliProvider : ILLMProvider
    {
        public string ProviderName => "Claude CLI";

        private volatile bool _aborted;
        private System.Diagnostics.Process _activeProcess;

        public void Abort()
        {
            _aborted = true;
            var p = _activeProcess;
            if (p != null)
            {
                SafeKill(p);
                _activeProcess = null;
            }
        }

        private readonly string _cliPath;
        private readonly string _modelName;
        private readonly int _effortLevel; // -1=off, 0=low, 1=medium, 2=high
        private readonly int _thinkingBudget;
        private const int TimeoutSeconds = 300;

        private static readonly string[] EffortNames = { "low", "medium", "high" };

        public ClaudeCliProvider(string cliPath, string modelName, int effortLevel = -1, int thinkingBudget = 0)
        {
            _cliPath = string.IsNullOrEmpty(cliPath) ? "claude" : cliPath;
            _modelName = modelName ?? "";
            _effortLevel = effortLevel;
            _thinkingBudget = thinkingBudget;
        }

        public IEnumerator CallLLM(
            IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null,
            Action<ChatStreamEvent> onStreamEvent = null)
        {
            _aborted = false;
            _activeProcess = null;

            Debug.Log("[ClaudeCliProvider] CallLLM Started");

            // Extract system prompt and build conversation text
            string systemPrompt = null;
            var conversation = new StringBuilder();

            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemPrompt = m.parts[0].text;
                    continue;
                }
                if (m.parts == null || m.parts.Length == 0) continue;

                string role = m.role == "model" ? "assistant" : m.role;
                conversation.AppendLine($"[{role}]");
                foreach (var part in m.parts)
                {
                    if (!string.IsNullOrEmpty(part.text))
                        conversation.AppendLine(part.text);
                }
                conversation.AppendLine();
            }

            // Build stdin content — embed system prompt if too long for flag
            string stdinContent;
            bool systemInFlag = !string.IsNullOrEmpty(systemPrompt) && systemPrompt.Length <= 6000;

            if (!string.IsNullOrEmpty(systemPrompt) && !systemInFlag)
                stdinContent = $"[system]\n{systemPrompt}\n\n{conversation}";
            else
                stdinContent = conversation.ToString();

            // Build CLI arguments
            var args = new StringBuilder();
            args.Append("-p --verbose --output-format stream-json");

            if (!string.IsNullOrEmpty(_modelName))
            {
                args.Append(" --model ");
                args.Append(EscapeShellArg(_modelName));
            }

            if (systemInFlag)
            {
                args.Append(" --system-prompt ");
                args.Append(EscapeShellArg(systemPrompt));
            }

            if (_effortLevel >= 0 && _effortLevel < EffortNames.Length)
            {
                args.Append(" --effort ");
                args.Append(EffortNames[_effortLevel]);
            }

            // Start process
            System.Diagnostics.Process process;
            try
            {
                var startInfo = BuildProcessStartInfo(_cliPath, args.ToString());

                if (_thinkingBudget > 0)
                    startInfo.EnvironmentVariables["MAX_THINKING_TOKENS"] = _thinkingBudget.ToString();

                onStatus?.Invoke("Starting Claude CLI...");
                onDebugLog?.Invoke($"[CLI LAUNCH] Provider: Claude CLI, CLI: {_cliPath}, Model: {(string.IsNullOrEmpty(_modelName) ? "(default)" : _modelName)}, Timeout: {TimeoutSeconds}s" +
                    (_effortLevel >= 0 ? $", Effort: {EffortNames[_effortLevel]}" : "") +
                    (_thinkingBudget > 0 ? $", ThinkingBudget: {_thinkingBudget}" : "") +
                    $"\nCommand: {startInfo.FileName} {startInfo.Arguments}");

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    onError?.Invoke("Claude CLI プロセスの起動に失敗しました。");
                    yield break;
                }
                _activeProcess = process;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Claude CLI 起動エラー: {ex.Message}\nパス: {_cliPath}");
                yield break;
            }

            // Set up async readers BEFORE writing stdin so we capture errors
            // even if the process exits immediately
            var outputQueue = new Queue<string>();
            var stderrBuilder = new StringBuilder();
            var syncLock = new object();
            bool stdoutDone = false;

            process.OutputDataReceived += (s, e) =>
            {
                lock (syncLock)
                {
                    if (e.Data != null)
                        outputQueue.Enqueue(e.Data);
                    else
                        stdoutDone = true;
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (syncLock)
                        stderrBuilder.AppendLine(e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Write stdin on background thread to avoid blocking the main thread.
            // StandardInput.Write is synchronous and blocks when the pipe buffer is full,
            // which freezes the UI until the child process reads its stdin.
            bool stdinWriteDone = false;
            Exception stdinWriteError = null;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    process.StandardInput.Write(stdinContent);
                    process.StandardInput.Close();
                }
                catch (Exception ex)
                {
                    stdinWriteError = ex;
                }
                finally
                {
                    stdinWriteDone = true;
                }
            });

            while (!stdinWriteDone)
            {
                if (_aborted) { _activeProcess = null; yield break; }
                yield return null;
            }

            if (stdinWriteError != null)
            {
                // Process likely exited immediately — wait briefly and collect stderr
                var earlyExitWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && earlyExitWait.Elapsed.TotalSeconds < 3)
                    yield return null;

                string earlyStderr;
                lock (syncLock)
                    earlyStderr = stderrBuilder.ToString().Trim();

                string errorMsg = $"Claude CLI が入力を受け付ける前に終了しました。";
                if (!string.IsNullOrEmpty(earlyStderr))
                    errorMsg += $"\n{earlyStderr}";
                else
                    errorMsg += $"\n(終了コード: {(process.HasExited ? process.ExitCode.ToString() : "不明")}, 詳細: {stdinWriteError.Message})";

                onError?.Invoke(errorMsg);
                SafeKill(process);
                yield break;
            }

            // Polling loop
            var textAccumulator = new StringBuilder();
            var thinkingAccumulator = new StringBuilder();
            string resultText = null;
            var inactivityWatch = System.Diagnostics.Stopwatch.StartNew();

            onStatus?.Invoke("Claude CLI processing...");

            while (true)
            {
                // Abort check
                if (_aborted) { _activeProcess = null; SafeKill(process); yield break; }

                // Inactivity timeout (resets on every received line)
                if (inactivityWatch.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    _activeProcess = null;
                    SafeKill(process);
                    onDebugLog?.Invoke($"[CLI TIMEOUT] Provider: Claude CLI, Inactivity: {TimeoutSeconds}s");
                    onError?.Invoke($"Claude CLI がタイムアウトしました（{TimeoutSeconds}秒間応答なし）。");
                    yield break;
                }

                // Drain queue
                List<string> lines = null;
                lock (syncLock)
                {
                    if (outputQueue.Count > 0)
                    {
                        lines = new List<string>(outputQueue);
                        outputQueue.Clear();
                    }
                }

                if (lines != null)
                {
                    inactivityWatch.Restart();
                    foreach (var line in lines)
                        ProcessStreamLine(line, textAccumulator, thinkingAccumulator,
                            ref resultText, onPartialResponse, onStatus, onDebugLog);
                }

                // Check done
                bool done;
                lock (syncLock)
                    done = stdoutDone && outputQueue.Count == 0;

                if (done) break;

                yield return null;
            }

            // Wait for process exit (non-blocking poll to avoid freezing the UI)
            if (!process.HasExited)
            {
                var exitWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && exitWait.Elapsed.TotalSeconds < 5)
                    yield return null;
            }

            // Check errors
            string stderr;
            lock (syncLock)
                stderr = stderrBuilder.ToString().Trim();

            int exitCode = process.HasExited ? process.ExitCode : -1;

            if (exitCode != 0 && string.IsNullOrEmpty(resultText) && textAccumulator.Length == 0)
            {
                string errorMsg = $"Claude CLI エラー (終了コード {exitCode})";
                if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\n{stderr}";
                onDebugLog?.Invoke($"[CLI ERROR] Provider: Claude CLI, ExitCode: {exitCode}");
                onError?.Invoke(errorMsg);
                yield break;
            }

            string finalText = resultText ?? textAccumulator.ToString();
            if (string.IsNullOrEmpty(finalText))
            {
                string errorMsg = "Claude CLI から応答がありませんでした。";
                if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\nstderr: {stderr}";
                onError?.Invoke(errorMsg);
                yield break;
            }

            // Wrap thinking content in <Thinking> tags for UI extraction (matches ClaudeApiProvider)
            if (thinkingAccumulator.Length > 0)
                finalText = $"<Thinking>\n{thinkingAccumulator}\n</Thinking>\n{finalText}";

            _activeProcess = null;
            onSuccess?.Invoke(finalText);
        }

        // ─── Stream JSON parsing ───
        //
        // Claude Code CLI (--output-format stream-json) outputs turn-level events:
        //   {"type":"system","subtype":"init",...}
        //   {"type":"assistant","message":{"content":[{"type":"thinking","thinking":"..."}]}}
        //   {"type":"assistant","message":{"content":[{"type":"text","text":"..."}]}}
        //   {"type":"assistant","message":{"content":[{"type":"tool_use","name":"...","input":{...}}]}}
        //   {"type":"user","message":{"content":[{"type":"tool_result",...}]}}
        //   {"type":"result","result":"...","cost_usd":...}
        // NOT token-level deltas like the API (no text_delta events).

        private static void ProcessStreamLine(string line, StringBuilder textAccumulator,
            StringBuilder thinkingAccumulator,
            ref string resultText, Action<string> onPartialResponse,
            Action<string> onStatus, Action<string> onDebugLog)
        {
            if (string.IsNullOrEmpty(line)) return;

            string type = ExtractJsonStringValue(line, "type");
            if (type == null) return;

            if (type == "assistant")
            {
                // Thinking: {"type":"thinking","thinking":"..."}
                if (line.Contains("\"type\":\"thinking\""))
                {
                    string thinking = ExtractJsonStringValueAfter(line, "thinking",
                        line.IndexOf("\"type\":\"thinking\""));
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        thinkingAccumulator.Append(thinking);
                        string preview = thinking.Length > 80
                            ? thinking.Substring(0, 80) + "..."
                            : thinking;
                        onStatus?.Invoke($"🧠 Thinking: {preview}");
                        onDebugLog?.Invoke($"[CLI THINKING] {(thinking.Length > 200 ? thinking.Substring(0, 200) + "..." : thinking)}");
                    }
                }

                // Text: {"type":"text","text":"..."}
                if (line.Contains("\"type\":\"text\""))
                {
                    string text = ExtractTextContentBlock(line);
                    if (!string.IsNullOrEmpty(text))
                    {
                        textAccumulator.Clear();
                        textAccumulator.Append(text);
                        onPartialResponse?.Invoke(text);
                    }
                }

                // Tool use: {"type":"tool_use","name":"...","input":{...}}
                if (line.Contains("\"type\":\"tool_use\""))
                {
                    string toolName = ExtractJsonStringValueAfter(line, "name",
                        line.IndexOf("\"type\":\"tool_use\""));
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        onStatus?.Invoke($"🔧 Tool: {toolName}");
                        onDebugLog?.Invoke($"[CLI TOOL_USE] {toolName}");
                    }
                }

                // Server tool use: {"type":"server_tool_use","name":"..."}
                if (line.Contains("\"type\":\"server_tool_use\""))
                {
                    string toolName = ExtractJsonStringValueAfter(line, "name",
                        line.IndexOf("\"type\":\"server_tool_use\""));
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        onStatus?.Invoke($"🌐 Server tool: {toolName}");
                        onDebugLog?.Invoke($"[CLI SERVER_TOOL] {toolName}");
                    }
                }
            }
            else if (type == "user")
            {
                // Tool result: shows that a tool finished executing
                if (line.Contains("\"type\":\"tool_result\""))
                {
                    onStatus?.Invoke("📋 Tool result received");
                    onDebugLog?.Invoke("[CLI TOOL_RESULT] Tool execution completed");
                }
            }
            else if (type == "system")
            {
                string subtype = ExtractJsonStringValue(line, "subtype");
                if (subtype == "init")
                    onStatus?.Invoke("Claude CLI connected");
                else if (subtype != null)
                    onDebugLog?.Invoke($"[CLI SYSTEM] subtype={subtype}");
            }
            else if (type == "result")
            {
                resultText = ExtractJsonStringValue(line, "result");
                string costStr = ExtractJsonStringValue(line, "cost_usd");
                string durationStr = ExtractJsonStringValue(line, "duration_ms");
                string info = $"ResponseSize: {resultText?.Length ?? 0}chars";
                if (!string.IsNullOrEmpty(costStr)) info += $", Cost: ${costStr}";
                if (!string.IsNullOrEmpty(durationStr)) info += $", Duration: {durationStr}ms";
                onDebugLog?.Invoke($"[CLI RESULT] Provider: Claude CLI, {info}");
            }
        }

        /// <summary>
        /// assistant イベントの content 配列から "type":"text" ブロックの text 値を抽出。
        /// </summary>
        private static string ExtractTextContentBlock(string line)
        {
            // Find "type":"text" then extract the "text" value after it
            int typeTextIdx = line.IndexOf("\"type\":\"text\"");
            if (typeTextIdx < 0) return null;
            return ExtractJsonStringValueAfter(line, "text", typeTextIdx + 13);
        }

        /// <summary>JSON 文字列から指定キーの文字列値を抽出（最初のマッチ）。</summary>
        private static string ExtractJsonStringValue(string json, string key)
        {
            return ExtractJsonStringValueAfter(json, key, 0);
        }

        /// <summary>JSON 文字列の指定位置以降から指定キーの文字列値を抽出。
        /// "key": "value" パターンを探す。値側に同名文字列がある場合はスキップして次を探す。</summary>
        private static string ExtractJsonStringValueAfter(string json, string key, int startIndex)
        {
            string needle = $"\"{key}\"";
            int searchFrom = startIndex;

            while (true)
            {
                int keyIdx = json.IndexOf(needle, searchFrom, StringComparison.Ordinal);
                if (keyIdx < 0) return null;

                int i = keyIdx + needle.Length;

                // skip whitespace + colon
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i] != ':')
                {
                    // Not a key (matched a value like "type":"result") — skip and continue
                    searchFrom = keyIdx + needle.Length;
                    continue;
                }
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

                if (i >= json.Length || json[i] != '"') return null;
                i++; // skip opening quote

                var sb = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i];
                    if (c == '\\' && i + 1 < json.Length)
                    {
                        char next = json[i + 1];
                        switch (next)
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case '/':  sb.Append('/');  break;
                            case 'n':  sb.Append('\n'); break;
                            case 'r':  sb.Append('\r'); break;
                            case 't':  sb.Append('\t'); break;
                            case 'b':  sb.Append('\b'); break;
                            case 'f':  sb.Append('\f'); break;
                            default:   sb.Append('\\'); sb.Append(next); break;
                        }
                        i += 2;
                    }
                    else if (c == '"')
                    {
                        return sb.ToString();
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }
                return null; // unterminated string
            }
        }

        // ─── Helpers ───

        /// <summary>
        /// Windows では cmd.exe /c 経由で起動し .cmd ファイルを解決する。
        /// %APPDATA%\npm を PATH に補完して npm グローバルパッケージを検索可能にする。
        /// </summary>
        private static System.Diagnostics.ProcessStartInfo BuildProcessStartInfo(string cliPath, string arguments)
        {
            string fileName, cmdArgs;

            bool isWindows = System.Environment.OSVersion.Platform == PlatformID.Win32NT;
            if (isWindows)
            {
                fileName = "cmd.exe";
                // chcp 65001 forces cmd.exe to output in UTF-8, matching StandardErrorEncoding.
                // Without this, error messages on Japanese Windows (CP932) appear as mojibake.
                cmdArgs = "/c chcp 65001 >nul & " + cliPath + " " + arguments;
            }
            else
            {
                fileName = cliPath;
                cmdArgs = arguments;
            }

            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = cmdArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (isWindows)
            {
                // Unity Editor may not inherit the full user PATH when launched from
                // Start Menu, ALCOM, or file association. Reconstruct from registry
                // so CLI tools (claude, gemini, codex) and Node.js can be found.
                string machinePath = Environment.GetEnvironmentVariable("PATH",
                    EnvironmentVariableTarget.Machine) ?? "";
                string userPath = Environment.GetEnvironmentVariable("PATH",
                    EnvironmentVariableTarget.User) ?? "";
                info.EnvironmentVariables["PATH"] = machinePath + ";" + userPath;
            }

            return info;
        }

        /// <summary>Windows コマンドライン引数エスケープ。</summary>
        private static string EscapeShellArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '"', '\\', '\t', '\n', '\r' }) < 0)
                return arg;

            var sb = new StringBuilder(arg.Length + 4);
            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int bs = 0;
                while (i < arg.Length && arg[i] == '\\') { bs++; i++; }

                if (i == arg.Length)
                {
                    sb.Append('\\', bs * 2);
                    break;
                }

                if (arg[i] == '"')
                {
                    sb.Append('\\', bs * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', bs);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static void SafeKill(System.Diagnostics.Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { /* ignore */ }
        }
    }
}
