using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    public class GeminiCliProvider : ILLMProvider
    {
        public string ProviderName => "Gemini CLI";

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
        private readonly int _thinkingBudget; // -1=off, >0=token budget
        private const int TimeoutSeconds = 300;

        public GeminiCliProvider(string cliPath, string modelName, int thinkingBudget = -1)
        {
            _cliPath = string.IsNullOrEmpty(cliPath) ? "gemini" : cliPath;
            _modelName = modelName ?? "";
            _thinkingBudget = thinkingBudget;
        }

        public IEnumerator CallLLM(
            IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null)
        {
            _aborted = false;
            _activeProcess = null;

            Debug.Log("[GeminiCliProvider] CallLLM Started");

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
            // IMPORTANT: Do NOT format this as a chat continuation — Gemini CLI runs an internal
            // agentic loop and would treat chat-style history as an ongoing session, intercepting
            // [MethodName()] patterns with its own native tool system before returning output.
            // Instead, embed history as a read-only reference block and present only the final
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

            // Write system prompt to temp file (GEMINI_SYSTEM_MD env var)
            // Prepend integration override to prevent Gemini CLI's native tool system from
            // intercepting [MethodName()] patterns. Gemini CLI runs in agentic mode and will
            // try to execute tool calls through its own system (bash, file ops, etc.) rather
            // than outputting them as plain text for Unity to parse.
            string tempFilePath = null;
            {
                string fileContent = GetIntegrationOverride() + "\n\n" + (systemPrompt ?? "");
                try
                {
                    tempFilePath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "gemini_system_" + System.IO.Path.GetRandomFileName() + ".md");
                    System.IO.File.WriteAllText(tempFilePath, fileContent, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"システムプロンプトの一時ファイル作成に失敗: {ex.Message}");
                    yield break;
                }
            }

            // Build CLI arguments
            // --prompt は文字列引数が必須。空文字を渡してヘッドレスモードを有効化し、
            // 実際の会話内容は stdin 経由で渡す（stdin の内容に --prompt 値が追記される）
            var args = new StringBuilder();
            args.Append("--prompt \"\" --output-format stream-json");

            if (!string.IsNullOrEmpty(_modelName))
            {
                args.Append(" -m ");
                args.Append(EscapeShellArg(_modelName));
            }

            // Thinking budget — create a temp workspace with .gemini/settings.json
            string tempWorkDir = null;
            if (_thinkingBudget > 0)
            {
                try
                {
                    tempWorkDir = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "gemini_ws_" + System.IO.Path.GetRandomFileName());
                    string geminiDir = System.IO.Path.Combine(tempWorkDir, ".gemini");
                    System.IO.Directory.CreateDirectory(geminiDir);
                    string settingsJson = "{\n  \"modelConfigs\": {\n    \"overrides\": [\n      {\n        \"modelConfig\": {\n          \"generateContentConfig\": {\n            \"thinkingConfig\": {\n              \"thinkingBudget\": " + _thinkingBudget + "\n            }\n          }\n        }\n      }\n    ]\n  }\n}";
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(geminiDir, "settings.json"),
                        settingsJson, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    onDebugLog?.Invoke($"[THINKING] settings.json 作成失敗 (無視して続行): {ex.Message}");
                    tempWorkDir = null;
                }
            }

            // Start process
            System.Diagnostics.Process process;
            try
            {
                var startInfo = BuildProcessStartInfo(_cliPath, args.ToString());

                // Use temp workspace with thinkingConfig if available
                if (!string.IsNullOrEmpty(tempWorkDir))
                    startInfo.WorkingDirectory = tempWorkDir;

                if (!string.IsNullOrEmpty(tempFilePath))
                    startInfo.EnvironmentVariables["GEMINI_SYSTEM_MD"] = tempFilePath;

                onStatus?.Invoke("Starting Gemini CLI...");
                onDebugLog?.Invoke($"[CLI LAUNCH] Provider: Gemini CLI, CLI: {_cliPath}, Model: {(string.IsNullOrEmpty(_modelName) ? "(default)" : _modelName)}, ThinkingBudget: {(_thinkingBudget > 0 ? _thinkingBudget.ToString() : "off")}, Timeout: {TimeoutSeconds}s" +
                    $"\nCommand: {startInfo.FileName} {startInfo.Arguments}");

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    onError?.Invoke("Gemini CLI プロセスの起動に失敗しました。");
                    DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir);
                    yield break;
                }
                _activeProcess = process;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Gemini CLI 起動エラー: {ex.Message}\nパス: {_cliPath}");
                DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir);
                yield break;
            }

            // Set up async readers — queue lines for incremental streaming parse
            var lineQueue = new Queue<string>();
            var stderrBuilder = new StringBuilder();
            var syncLock = new object();
            bool stdoutDone = false;

            process.OutputDataReceived += (s, e) =>
            {
                lock (syncLock)
                {
                    if (e.Data != null)
                        lineQueue.Enqueue(e.Data);
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

            // Write stdin on background thread to avoid blocking
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
                if (_aborted) { _activeProcess = null; DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir); yield break; }
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

                string errorMsg = "Gemini CLI が入力を受け付ける前に終了しました。";
                if (!string.IsNullOrEmpty(earlyStderr))
                    errorMsg += $"\n{earlyStderr}";
                else
                    errorMsg += $"\n(終了コード: {(process.HasExited ? process.ExitCode.ToString() : "不明")}, 詳細: {stdinWriteError.Message})";

                onError?.Invoke(errorMsg);
                SafeKill(process);
                DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir);
                yield break;
            }

            // Polling loop — parse stream-json lines incrementally
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var textAccumulator = new StringBuilder();
            var errorAccumulator = new StringBuilder();
            onStatus?.Invoke("Gemini CLI streaming...");

            while (true)
            {
                // Abort check
                if (_aborted) { _activeProcess = null; SafeKill(process); DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir); yield break; }

                if (stopwatch.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    _activeProcess = null;
                    SafeKill(process);
                    DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir);
                    onDebugLog?.Invoke($"[CLI TIMEOUT] Provider: Gemini CLI, Elapsed: {TimeoutSeconds}s");
                    onError?.Invoke($"Gemini CLI がタイムアウトしました ({TimeoutSeconds}秒)。");
                    yield break;
                }

                // Dequeue and process pending lines
                string[] pending;
                bool done;
                lock (syncLock)
                {
                    pending = lineQueue.Count > 0 ? lineQueue.ToArray() : null;
                    if (pending != null) lineQueue.Clear();
                    done = stdoutDone;
                }
                if (pending != null)
                {
                    foreach (var line in pending)
                        ProcessStreamLine(line, textAccumulator, errorAccumulator,
                            onPartialResponse, onDebugLog);
                }

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

            DeleteTempFile(tempFilePath); DeleteTempDir(tempWorkDir);

            string stderr;
            lock (syncLock)
                stderr = stderrBuilder.ToString().Trim();

            int exitCode = process.HasExited ? process.ExitCode : -1;
            string resultText = textAccumulator.ToString();
            string streamErrors = errorAccumulator.ToString().Trim();

            onDebugLog?.Invoke($"[CLI RESULT] Provider: Gemini CLI, ResponseSize: {resultText.Length}chars, ExitCode: {exitCode}");

            if (exitCode != 0 && string.IsNullOrEmpty(resultText))
            {
                string errorMsg = $"Gemini CLI エラー (終了コード {exitCode})";
                if (!string.IsNullOrEmpty(streamErrors))
                    errorMsg += $"\n{streamErrors}";
                else if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\n{stderr}";
                onDebugLog?.Invoke($"[CLI ERROR] Provider: Gemini CLI, ExitCode: {exitCode}");
                onError?.Invoke(errorMsg);
                yield break;
            }

            if (string.IsNullOrEmpty(resultText))
            {
                string errorMsg = "Gemini CLI から応答がありませんでした。";
                if (!string.IsNullOrEmpty(streamErrors))
                    errorMsg += $"\n{streamErrors}";
                else if (!string.IsNullOrEmpty(stderr))
                    errorMsg += $"\nstderr: {stderr}";
                onError?.Invoke(errorMsg);
                yield break;
            }

            _activeProcess = null;
            onSuccess?.Invoke(resultText);
        }

        // ─── Stream JSON parsing ───

        /// <summary>
        /// stream-json の1行をパースし、assistant のテキストデルタを蓄積する。
        /// 形式: {"type":"message","role":"assistant","content":"...","delta":true}
        ///        {"type":"result","status":"success|error","stats":{...}}
        /// </summary>
        private static void ProcessStreamLine(string line, StringBuilder textAccumulator,
            StringBuilder errorAccumulator,
            Action<string> onPartialResponse, Action<string> onDebugLog)
        {
            if (string.IsNullOrEmpty(line)) return;

            string type = ExtractJsonStringValue(line, "type");
            if (type == null) return;

            if (type == "message")
            {
                string role = ExtractJsonStringValue(line, "role");
                if (role != "assistant") return;

                string content = ExtractJsonStringValue(line, "content");
                if (string.IsNullOrEmpty(content)) return;

                textAccumulator.Append(content);
                onPartialResponse?.Invoke(textAccumulator.ToString());
            }
            else if (type == "result")
            {
                string status = ExtractJsonStringValue(line, "status");
                if (status == "error")
                {
                    string errMsg = ExtractJsonStringValue(line, "error");
                    errorAccumulator.AppendLine(errMsg ?? "Unknown stream error");
                    onDebugLog?.Invoke($"[CLI STREAM] Provider: Gemini CLI, result error: {errMsg}");
                }
                else
                {
                    onDebugLog?.Invoke($"[CLI STREAM] Provider: Gemini CLI, result: {status}");
                }
            }
        }

        // ─── JSON parsing ───

        private static string ExtractJsonStringValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string needle = $"\"{key}\"";
            int searchFrom = 0;

            while (true)
            {
                int keyIdx = json.IndexOf(needle, searchFrom, StringComparison.Ordinal);
                if (keyIdx < 0) return null;

                int i = keyIdx + needle.Length;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

                if (i >= json.Length || json[i] != ':')
                {
                    searchFrom = keyIdx + needle.Length;
                    continue;
                }
                i++;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;

                if (i >= json.Length || json[i] != '"') return null;
                i++;

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
                return null;
            }
        }

        // ─── Helpers ───

        /// <summary>
        /// Gemini CLI のネイティブツール系との干渉を防ぐためのオーバーライド前文。
        /// Gemini CLI はアジェンティックモードで動作し、モデルが [MethodName()] を出力すると
        /// 自身のネイティブツール(bash/ファイル操作等)として解釈して実行しようとする。
        /// このオーバーライドでネイティブツールを使わず [MethodName()] テキストのみ出力させる。
        /// </summary>
        private static string GetIntegrationOverride() =>
@"# UNITY EDITOR INTEGRATION MODE — CRITICAL OVERRIDE

You are running inside a **Unity Editor AI Agent** integrated via Gemini CLI pipe mode.

## Response Protocol

**Output EXACTLY ONE response, then STOP.**

- The Unity Editor host reads your response, runs any tool calls you wrote, and will call you again with results if needed.
- Do NOT loop, do NOT wait for confirmations, do NOT start a new turn on your own.
- After generating your response text, your job is done. The host handles the next step.

## Tool System Rules

**ALL built-in Gemini CLI tools** (shell execution, file operations, code execution, web browsing, etc.) are **COMPLETELY DISABLED** in this environment and will fail with errors if called. **Do NOT call any Gemini CLI native tools.**

Your **ONLY** mechanism for tool use is to write tool call patterns as **plain text** on their own line:

```
[MethodName(arg1, arg2)]
```

The Unity Editor host application reads your text output from the `response` field, detects `[MethodName()]` patterns using regex, executes the corresponding C# methods inside the Unity Editor, and feeds the result back to you in the next message.

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
- Using native Gemini CLI function-calling format
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
                // .cmd files cannot be launched directly with UseShellExecute=false, so use cmd.exe.
                // chcp 65001 forces cmd.exe to output in UTF-8, matching StandardErrorEncoding.
                // Without this, error messages on Japanese Windows (CP932) appear as mojibake.
                fileName = "cmd.exe";
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
                // Gemini CLI scans the current directory as a project workspace and saves
                // session checkpoints per project. Launching from the Unity project folder
                // would pollute the context with Unity files, so use a temp directory.
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

        private static void DeleteTempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { System.IO.File.Delete(path); } catch { /* ignore */ }
        }

        private static void DeleteTempDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { System.IO.Directory.Delete(path, true); } catch { /* ignore */ }
        }
    }
}
