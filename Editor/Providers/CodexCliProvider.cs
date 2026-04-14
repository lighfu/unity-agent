using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    public class CodexCliProvider : ILLMProvider
    {
        public string ProviderName => "Codex CLI";

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
        private const int TimeoutSeconds = 300;

        private static readonly string[] EffortNames = { "low", "medium", "high" };

        public CodexCliProvider(string cliPath, string modelName, int effortLevel = -1)
        {
            _cliPath = string.IsNullOrEmpty(cliPath) ? "codex" : cliPath;
            _modelName = modelName ?? "";
            _effortLevel = effortLevel;
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

            Debug.Log("[CodexCliProvider] CallLLM Started");

            // Extract system prompt and build conversation turns
            string systemPrompt = null;
            var turns = new List<(string role, string text)>();

            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemPrompt = m.parts[0].text;
                    continue;
                }
                if (m.parts == null || m.parts.Length == 0) continue;

                string role = m.role == "model" ? "Assistant" : "User";
                var sb = new StringBuilder();
                foreach (var part in m.parts)
                    if (!string.IsNullOrEmpty(part.text))
                        sb.Append(part.text);
                turns.Add((role, sb.ToString()));
            }

            // Build stdin as a single-shot document.
            // Codex CLI runs an internal agentic loop — formatting as a chat continuation
            // would cause it to intercept [MethodName()] patterns with its own native tool system.
            // Instead, embed history as a read-only reference and present only the final
            // user message as "the one thing to respond to right now."
            var stdinBuilder = new StringBuilder();
            if (turns.Count > 1)
            {
                stdinBuilder.AppendLine("## Conversation History (READ-ONLY reference — do NOT continue this history)");
                stdinBuilder.AppendLine();
                for (int i = 0; i < turns.Count - 1; i++)
                    stdinBuilder.AppendLine($"<<{turns[i].role}>>: {turns[i].text}");
                stdinBuilder.AppendLine();
                stdinBuilder.AppendLine("---");
                stdinBuilder.AppendLine();
                stdinBuilder.AppendLine("## Your Task (respond to THIS and ONLY THIS — output exactly ONE response then stop)");
                stdinBuilder.AppendLine();
            }
            if (turns.Count > 0)
                stdinBuilder.Append(turns[turns.Count - 1].text);

            string stdinContent = stdinBuilder.ToString();

            // Write system prompt + integration override to temp file
            string instructionsFile = null;
            string outputFile = null;
            {
                string fileContent = GetIntegrationOverride() + "\n\n" + (systemPrompt ?? "");
                try
                {
                    instructionsFile = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "codex_inst_" + System.IO.Path.GetRandomFileName() + ".md");
                    System.IO.File.WriteAllText(instructionsFile, fileContent, Encoding.UTF8);

                    outputFile = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "codex_out_" + System.IO.Path.GetRandomFileName() + ".txt");
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"一時ファイル作成に失敗: {ex.Message}");
                    yield break;
                }
            }

            // Build CLI arguments
            // codex exec --json --ephemeral -s read-only -m MODEL -c model_instructions_file=FILE -o OUTPUT -
            var args = new StringBuilder();
            args.Append("exec --json --ephemeral --skip-git-repo-check -s read-only");

            if (!string.IsNullOrEmpty(_modelName))
            {
                args.Append(" -m ");
                args.Append(EscapeShellArg(_modelName));
            }

            args.Append(" -c ");
            args.Append(EscapeShellArg($"model_instructions_file={instructionsFile}"));

            if (_effortLevel >= 0 && _effortLevel < EffortNames.Length)
            {
                args.Append(" -c ");
                args.Append(EscapeShellArg($"model_reasoning_effort={EffortNames[_effortLevel]}"));
            }

            args.Append(" -o ");
            args.Append(EscapeShellArg(outputFile));

            args.Append(" -"); // Read prompt from stdin

            // Start process
            System.Diagnostics.Process process;
            try
            {
                var startInfo = BuildProcessStartInfo(_cliPath, args.ToString());

                onStatus?.Invoke("Starting Codex CLI...");
                onDebugLog?.Invoke($"[CLI LAUNCH] Provider: Codex CLI, CLI: {_cliPath}, Model: {(string.IsNullOrEmpty(_modelName) ? "(default)" : _modelName)}, Timeout: {TimeoutSeconds}s" +
                    (_effortLevel >= 0 ? $", Effort: {EffortNames[_effortLevel]}" : "") +
                    $"\nCommand: {startInfo.FileName} {startInfo.Arguments}");

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    onError?.Invoke("Codex CLI プロセスの起動に失敗しました。");
                    DeleteTempFiles(instructionsFile, outputFile);
                    yield break;
                }
                _activeProcess = process;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Codex CLI 起動エラー: {ex.Message}\nパス: {_cliPath}");
                DeleteTempFiles(instructionsFile, outputFile);
                yield break;
            }

            // Set up async readers
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
                    lock (syncLock)
                        stderrBuilder.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Write stdin on background thread to avoid blocking the main thread
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
                if (_aborted) { _activeProcess = null; DeleteTempFiles(instructionsFile, outputFile); yield break; }
                yield return null;
            }

            if (stdinWriteError != null)
            {
                var earlyWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && earlyWait.Elapsed.TotalSeconds < 3)
                    yield return null;

                string earlyStderr;
                lock (syncLock)
                    earlyStderr = stderrBuilder.ToString().Trim();

                string errorMsg = "Codex CLI が入力を受け付ける前に終了しました。";
                if (!string.IsNullOrEmpty(earlyStderr))
                    errorMsg += $"\n{earlyStderr}";
                else
                    errorMsg += $"\n(終了コード: {(process.HasExited ? process.ExitCode.ToString() : "不明")}, 詳細: {stdinWriteError.Message})";

                onError?.Invoke(errorMsg);
                SafeKill(process);
                DeleteTempFiles(instructionsFile, outputFile);
                yield break;
            }

            // Polling loop — parse JSONL for streaming
            var textAccumulator = new StringBuilder();
            var errorAccumulator = new StringBuilder();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            onStatus?.Invoke("Codex CLI processing...");

            while (true)
            {
                // Abort check
                if (_aborted) { _activeProcess = null; SafeKill(process); DeleteTempFiles(instructionsFile, outputFile); yield break; }

                if (stopwatch.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    _activeProcess = null;
                    SafeKill(process);
                    DeleteTempFiles(instructionsFile, outputFile);
                    onDebugLog?.Invoke($"[CLI TIMEOUT] Provider: Codex CLI, Elapsed: {TimeoutSeconds}s");
                    onError?.Invoke($"Codex CLI がタイムアウトしました ({TimeoutSeconds}秒)。");
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
                    foreach (var line in lines)
                        ProcessJsonlLine(line, textAccumulator, errorAccumulator, onPartialResponse, onDebugLog);
                }

                bool done;
                lock (syncLock)
                    done = stdoutDone && outputQueue.Count == 0;

                if (done) break;
                yield return null;
            }

            // Wait for process exit
            if (!process.HasExited)
            {
                var exitWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && exitWait.Elapsed.TotalSeconds < 5)
                    yield return null;
            }

            // Try to read final message from -o output file (most reliable)
            string resultText = null;
            try
            {
                if (!string.IsNullOrEmpty(outputFile) && System.IO.File.Exists(outputFile))
                    resultText = System.IO.File.ReadAllText(outputFile, Encoding.UTF8).Trim();
            }
            catch { /* ignore */ }

            DeleteTempFiles(instructionsFile, outputFile);

            // Fall back to accumulated JSONL text
            if (string.IsNullOrEmpty(resultText))
                resultText = textAccumulator.ToString();

            string stderr;
            lock (syncLock)
                stderr = stderrBuilder.ToString().Trim();

            int exitCode = process.HasExited ? process.ExitCode : -1;

            string jsonlErrors = errorAccumulator.ToString().Trim();

            if (exitCode != 0 && string.IsNullOrEmpty(resultText))
            {
                string errorMsg = $"Codex CLI エラー (終了コード {exitCode})";
                // JSONL から抽出したエラー詳細を優先表示
                if (!string.IsNullOrEmpty(jsonlErrors))
                    errorMsg += $"\n{jsonlErrors}";
                else if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\n{stderr}";
                onDebugLog?.Invoke($"[CLI ERROR] Provider: Codex CLI, ExitCode: {exitCode}");
                onError?.Invoke(errorMsg);
                yield break;
            }

            if (string.IsNullOrEmpty(resultText))
            {
                string errorMsg = "Codex CLI から応答がありませんでした。";
                if (!string.IsNullOrEmpty(jsonlErrors))
                    errorMsg += $"\n{jsonlErrors}";
                else if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\nstderr: {stderr}";
                onError?.Invoke(errorMsg);
                yield break;
            }

            _activeProcess = null;
            onDebugLog?.Invoke($"[CLI RESULT] Provider: Codex CLI, ResponseSize: {resultText.Length}chars, ExitCode: {exitCode}");
            onSuccess?.Invoke(resultText);
        }

        // ─── JSONL parsing ───

        /// <summary>
        /// Codex exec --json の JSONL イベントを解析。
        /// agent_message アイテムから text を抽出する。
        /// 形式: {"type":"item.completed","item":{"type":"agent_message","text":"..."}}
        /// </summary>
        private static void ProcessJsonlLine(string line, StringBuilder textAccumulator,
            StringBuilder errorAccumulator,
            Action<string> onPartialResponse, Action<string> onDebugLog)
        {
            if (string.IsNullOrEmpty(line)) return;

            // Look for agent_message items
            int agentMsgIdx = line.IndexOf("\"agent_message\"", StringComparison.Ordinal);
            if (agentMsgIdx >= 0)
            {
                string text = ExtractJsonStringValueAfter(line, "text", agentMsgIdx + 15);
                if (!string.IsNullOrEmpty(text))
                {
                    // item.completed contains the full message — replace accumulator
                    textAccumulator.Clear();
                    textAccumulator.Append(text);
                    onPartialResponse?.Invoke(text);
                    onDebugLog?.Invoke($"[CLI STREAM] Provider: Codex CLI, agent_message: {text.Length}chars");
                }
                return;
            }

            // Capture error and turn.failed events for better diagnostics
            // e.g. {"type":"error","message":"{\"detail\":\"The 'codex-mini' model is not supported...\"}"}
            // e.g. {"type":"turn.failed","error":{"message":"..."}}
            bool isError = line.IndexOf("\"error\"", StringComparison.Ordinal) >= 0
                        || line.IndexOf("\"turn.failed\"", StringComparison.Ordinal) >= 0;
            if (!isError) return;

            // Skip transient reconnect messages
            if (line.IndexOf("Reconnecting...", StringComparison.Ordinal) >= 0) return;
            if (line.IndexOf("Falling back from WebSockets", StringComparison.Ordinal) >= 0) return;

            // Try to extract the error detail
            string errorMsg = ExtractErrorDetail(line);
            if (!string.IsNullOrEmpty(errorMsg))
            {
                errorAccumulator.AppendLine(errorMsg);
                onDebugLog?.Invoke($"[CLI STREAM] Provider: Codex CLI, error: {errorMsg}");
            }
        }

        /// <summary>JSON 文字列の指定位置以降から指定キーの文字列値を抽出。</summary>
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

        /// <summary>
        /// JSONL のエラーイベントから人間可読なエラーメッセージを抽出する。
        /// "message" フィールドが JSON 文字列を含む場合 (e.g. "{\"detail\":\"...\"}") は
        /// "detail" の値を取り出す。
        /// </summary>
        private static string ExtractErrorDetail(string line)
        {
            // Extract "message" value from the line
            string msg = ExtractJsonStringValueAfter(line, "message", 0);
            if (string.IsNullOrEmpty(msg)) return null;

            // The message may itself be a JSON string like {"detail":"..."}
            int detailIdx = msg.IndexOf("\"detail\"", StringComparison.Ordinal);
            if (detailIdx >= 0)
            {
                string detail = ExtractJsonStringValueAfter(msg, "detail", 0);
                if (!string.IsNullOrEmpty(detail)) return detail;
            }

            return msg;
        }

        // ─── Helpers ───

        /// <summary>
        /// Codex CLI のネイティブツール系との干渉を防ぐためのオーバーライド前文。
        /// Codex CLI はアジェンティックモードで動作し、シェル実行やファイル操作等のネイティブツールを
        /// 自律的に呼び出そうとする。このオーバーライドでネイティブツールを使わず
        /// [MethodName()] テキストのみ出力させる。
        /// </summary>
        private static string GetIntegrationOverride() =>
@"# UNITY EDITOR INTEGRATION MODE — CRITICAL OVERRIDE

You are running inside a **Unity Editor AI Agent** integrated via Codex CLI pipe mode.

## Response Protocol

**Output EXACTLY ONE response, then STOP.**

- The Unity Editor host reads your response, runs any tool calls you wrote, and will call you again with results if needed.
- Do NOT loop, do NOT wait for confirmations, do NOT start a new turn on your own.
- After generating your response text, your job is done. The host handles the next step.

## Tool System Rules

**ALL built-in Codex CLI tools** (shell execution, file operations, code execution, web browsing, etc.) are **COMPLETELY DISABLED** in this environment and will fail with errors if called. **Do NOT call any Codex CLI native tools.**

Your **ONLY** mechanism for tool use is to write tool call patterns as **plain text** on their own line:

```
[MethodName(arg1, arg2)]
```

The Unity Editor host application reads your text output, detects `[MethodName()]` patterns using regex, executes the corresponding C# methods inside the Unity Editor, and feeds the result back to you in the next message.

**All available tools** (including MCP tools from external servers) are listed in the system prompt under ""Available Tools"". MCP tools are listed under ""MCP Tools"" with their plain names. Call MCP tools using just the tool name — do NOT add any prefix. For example, if a tool named `get_current_config` is listed, call it as `[get_current_config()]`, not `[MCP/serena.get_current_config()]`. The host routes MCP tool calls to the appropriate server automatically.

### Correct behavior
- Need to inspect something? Write: `[GetHierarchyTree()]`
- Need to search for tools? Write: `[SearchTools(""keyword"")]`
- Need to change a material? Write: `[AdjustHSV(""ObjectName"", 0, 0, 0)]`
- Each `[MethodName()]` call goes on its own line with no other text on that line.
- Write your `[MethodName()]` calls, then STOP. The host will execute them and send you results.

### Wrong behavior (DO NOT DO)
- Attempting to call shell / bash commands
- Saying ""I cannot use tools in this environment""
- Saying ""MCP is not available"" (if MCP tools are listed, they ARE available)
- Using native Codex CLI function-calling format
- Asking the user to perform actions manually when a tool exists
- Continuing with a second turn or waiting for results within the same response

**Always output `[MethodName()]` patterns directly. The host system handles execution and will call you again with results.**

---
";

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
                WorkingDirectory = System.IO.Path.GetTempPath(),
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

        private static void DeleteTempFiles(string path1, string path2)
        {
            if (!string.IsNullOrEmpty(path1))
                try { System.IO.File.Delete(path1); } catch { /* ignore */ }
            if (!string.IsNullOrEmpty(path2))
                try { System.IO.File.Delete(path2); } catch { /* ignore */ }
        }
    }
}
