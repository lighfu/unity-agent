using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// Antigravity CLI (<c>agy</c>) — the successor to Gemini CLI, which Google stopped serving to
    /// personal accounts on 2026-06-18.
    ///
    /// Launched once per response in headless (print) mode:
    ///   agy --input-format stream-json --output-format stream-json ...
    /// One line <c>{"event":"user","message":{"content":...}}</c> goes to stdin, then stdin is closed;
    /// agy finishes that turn and exits. stdout is one event per line: <c>init</c>, <c>step_update</c>
    /// (the reply arrives as <c>text_delta</c> on <c>agent_response</c> steps) and <c>result</c>
    /// (<c>status</c>, the full <c>response</c>).
    ///
    /// How it differs from <see cref="GeminiCliProvider"/>:
    /// - agy has no channel for a system prompt (nothing like GEMINI_SYSTEM_MD), so the host's
    ///   instructions go at the top of the message itself.
    /// - agy writes JSON with Go's encoder, which escapes &lt; &gt; &amp; as < and friends. Every line
    ///   is parsed as real JSON; a substring scan would keep "<tool" literally and never see a
    ///   tool call.
    /// - Reasoning effort is the --effort flag, not a settings.json in the working directory.
    ///
    /// Checked against agy 1.1.20 and https://antigravity.google/docs/cli/headless/ (2026-09-16).
    /// </summary>
    public class AntigravityCliProvider : ILLMProvider
    {
        public string ProviderName => "Antigravity CLI";

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
        private static readonly string[] EffortNames = { "low", "medium", "high" };
        private const int TimeoutSeconds = 300;

        public AntigravityCliProvider(string cliPath, string modelName, int effortLevel = -1)
        {
            _cliPath = string.IsNullOrEmpty(cliPath) ? "agy" : cliPath;
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

            // One NDJSON line. It is written to stdin as raw UTF-8 bytes (see below), so the console
            // code page never gets a chance to mangle non-ASCII text on its way in.
            string inputLine = JNode.Obj(
                ("event", JNode.Str("user")),
                ("message", JNode.Obj(("content", JNode.Str(BuildContent(systemPrompt, turns)))))).ToJson() + "\n";

            var args = new StringBuilder("--input-format stream-json --output-format stream-json");
            // The content always starts with the override below, never with '/', but a user message
            // quoted inside it must not be taken for a slash command or skill either.
            args.Append(" --disable-slash-commands");
            // Our own timeout further down is the hard stop. agy's print timeout returns whatever it
            // has so far and exits cleanly, so let it fire first.
            args.Append(" --print-timeout ").Append(TimeoutSeconds - 10).Append('s');
            if (!string.IsNullOrEmpty(_modelName))
                args.Append(" --model ").Append(EscapeShellArg(_modelName));
            string effort = ResolveEffort();
            if (effort != null)
                args.Append(" --effort ").Append(effort);

            // agy treats its working directory as the workspace. An empty directory gives it
            // nothing to index; the Unity project would flood its context.
            string workDir;
            try
            {
                workDir = Path.Combine(Path.GetTempPath(), "agy_ws_" + Path.GetRandomFileName());
                Directory.CreateDirectory(workDir);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Antigravity CLI 用の一時フォルダを作れませんでした: {ex.Message}");
                yield break;
            }

            System.Diagnostics.Process process;
            try
            {
                var startInfo = BuildProcessStartInfo(_cliPath, args.ToString(), workDir);
                onStatus?.Invoke("Starting Antigravity CLI...");
                onDebugLog?.Invoke($"[CLI LAUNCH] Provider: Antigravity CLI, CLI: {_cliPath}, " +
                    $"Model: {(string.IsNullOrEmpty(_modelName) ? "(default)" : _modelName)}, Effort: {effort ?? "(default)"}, Timeout: {TimeoutSeconds}s" +
                    $"\nCommand: {startInfo.FileName} {startInfo.Arguments}");

                process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    onError?.Invoke("Antigravity CLI プロセスの起動に失敗しました。");
                    DeleteTempDir(workDir);
                    yield break;
                }
                _activeProcess = process;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Antigravity CLI 起動エラー: {ex.Message}\nパス: {_cliPath}");
                DeleteTempDir(workDir);
                yield break;
            }

            var lineQueue = new Queue<string>();
            var stderrBuilder = new StringBuilder();
            var syncLock = new object();
            bool stdoutDone = false;

            process.OutputDataReceived += (s, e) =>
            {
                lock (syncLock)
                {
                    if (e.Data != null) lineQueue.Enqueue(e.Data);
                    else stdoutDone = true;
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    lock (syncLock) stderrBuilder.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Closing stdin is what ends the session: agy completes the turn, then exits.
            bool stdinWriteDone = false;
            Exception stdinWriteError = null;
            byte[] inputBytes = new UTF8Encoding(false).GetBytes(inputLine);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var stdin = process.StandardInput.BaseStream;
                    stdin.Write(inputBytes, 0, inputBytes.Length);
                    stdin.Flush();
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
                if (_aborted) { _activeProcess = null; SafeKill(process); DeleteTempDir(workDir); yield break; }
                yield return null;
            }

            if (stdinWriteError != null)
            {
                var earlyWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && earlyWait.Elapsed.TotalSeconds < 3)
                    yield return null;

                string earlyStderr;
                lock (syncLock) earlyStderr = stderrBuilder.ToString().Trim();
                string errorMsg = "Antigravity CLI が入力を受け付ける前に終了しました。";
                errorMsg += !string.IsNullOrEmpty(earlyStderr)
                    ? $"\n{earlyStderr}"
                    : $"\n(終了コード: {(process.HasExited ? process.ExitCode.ToString() : "不明")}, 詳細: {stdinWriteError.Message})";
                onError?.Invoke(errorMsg);
                SafeKill(process);
                DeleteTempDir(workDir);
                yield break;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var state = new StreamState();
            System.Diagnostics.Stopwatch sinceResult = null;
            onStatus?.Invoke("Antigravity CLI streaming...");

            while (true)
            {
                if (_aborted) { _activeProcess = null; SafeKill(process); DeleteTempDir(workDir); yield break; }

                if (stopwatch.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    _activeProcess = null;
                    SafeKill(process);
                    DeleteTempDir(workDir);
                    onDebugLog?.Invoke($"[CLI TIMEOUT] Provider: Antigravity CLI, Elapsed: {TimeoutSeconds}s");
                    onError?.Invoke($"Antigravity CLI がタイムアウトしました ({TimeoutSeconds}秒)。");
                    yield break;
                }

                string[] pending;
                bool done;
                lock (syncLock)
                {
                    pending = lineQueue.Count > 0 ? lineQueue.ToArray() : null;
                    if (pending != null) lineQueue.Clear();
                    done = stdoutDone;
                }
                if (pending != null)
                    foreach (var line in pending)
                        ProcessStreamLine(line, state, onPartialResponse, onDebugLog);

                if (done) break;

                // The result event is the end of the turn. agy should exit right after it; if it
                // lingers, don't hold the chat for it.
                if (state.ResultSeen)
                {
                    if (sinceResult == null) sinceResult = System.Diagnostics.Stopwatch.StartNew();
                    else if (sinceResult.Elapsed.TotalSeconds > 5) break;
                }
                yield return null;
            }

            if (!process.HasExited)
            {
                var exitWait = System.Diagnostics.Stopwatch.StartNew();
                while (!process.HasExited && exitWait.Elapsed.TotalSeconds < 5)
                    yield return null;
                SafeKill(process);
            }
            DeleteTempDir(workDir);

            string stderr;
            lock (syncLock) stderr = stderrBuilder.ToString().Trim();
            int exitCode = process.HasExited ? process.ExitCode : -1;
            // result.response is the whole reply; the deltas are only the fallback if it is missing.
            string resultText = !string.IsNullOrEmpty(state.FinalResponse) ? state.FinalResponse : state.Text.ToString();

            onDebugLog?.Invoke($"[CLI RESULT] Provider: Antigravity CLI, ResponseSize: {resultText.Length}chars, Status: {state.Status ?? "(no result event)"}, ExitCode: {exitCode}");

            if (string.IsNullOrEmpty(resultText))
            {
                string errorMsg = exitCode != 0 && exitCode != -1
                    ? $"Antigravity CLI エラー (終了コード {exitCode})"
                    : "Antigravity CLI から応答がありませんでした。";
                if (!string.IsNullOrEmpty(state.Error)) errorMsg += $"\n{state.Error}";
                else if (!string.IsNullOrEmpty(stderr)) errorMsg += $"\n{stderr}";
                onError?.Invoke(errorMsg);
                _activeProcess = null;
                yield break;
            }

            _activeProcess = null;
            onSuccess?.Invoke(resultText);
        }

        /// <summary>A model slug such as gemini-3.8-flash-high already names its effort, so --effort is left off.</summary>
        private string ResolveEffort()
        {
            if (_effortLevel < 0 || _effortLevel >= EffortNames.Length) return null;
            string m = _modelName.ToLowerInvariant();
            if (m.EndsWith("-high") || m.EndsWith("-medium") || m.EndsWith("-low")) return null;
            return EffortNames[_effortLevel];
        }

        // ─── Stream JSON parsing ───

        private sealed class StreamState
        {
            public readonly StringBuilder Text = new StringBuilder();
            public string FinalResponse;
            public string Status;
            public string Error;
            public bool ResultSeen;
        }

        /// <summary>
        /// One stdout line:
        ///   {"event":"step_update","step_update":{"step_type":"agent_response","text_delta":"..."}}
        ///   {"event":"result","result":{"status":"SUCCESS","response":"..."}}
        /// Everything else (init, user_input and tool steps) carries no reply text.
        /// </summary>
        private static void ProcessStreamLine(string line, StreamState state,
            Action<string> onPartialResponse, Action<string> onDebugLog)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            JNode node;
            try { node = JNode.Parse(line); }
            catch
            {
                onDebugLog?.Invoke($"[CLI STREAM] Provider: Antigravity CLI, non-JSON line: {Shorten(line)}");
                return;
            }
            if (node == null || node.Type != JNode.JType.Object) return;

            string ev = node["event"].AsString;
            if (ev == "step_update")
            {
                var step = node["step_update"];
                if (step.Type != JNode.JType.Object) return;
                if (step["step_type"].AsString != "agent_response") return;

                string delta = step["text_delta"].AsString;
                if (string.IsNullOrEmpty(delta)) return;
                state.Text.Append(delta);
                onPartialResponse?.Invoke(state.Text.ToString());
            }
            else if (ev == "result")
            {
                state.ResultSeen = true;
                var result = node["result"];
                if (result.Type != JNode.JType.Object) return;

                state.Status = result["status"].AsString;
                string response = result["response"].AsString;
                if (!string.IsNullOrEmpty(response)) state.FinalResponse = response;

                if (!string.Equals(state.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                {
                    var err = result["error"];
                    state.Error = err.Type == JNode.JType.String ? err.AsString
                                : err.Type == JNode.JType.Null ? null
                                : err.ToJson();
                    if (string.IsNullOrEmpty(state.Error)) state.Error = $"status={state.Status ?? "(none)"}";
                }
                onDebugLog?.Invoke($"[CLI STREAM] Provider: Antigravity CLI, result: {state.Status}" +
                    (state.Error != null ? $", error: {state.Error}" : ""));
            }
        }

        private static string Shorten(string s) => s.Length <= 200 ? s : s.Substring(0, 197) + "...";

        // ─── Message ───

        /// <summary>
        /// The whole request as one message: integration override, the host's instructions (agy has no
        /// system-prompt channel), then the conversation as a read-only record with only the last user
        /// message to answer — agy runs an agent loop and would otherwise continue the history itself.
        /// </summary>
        private static string BuildContent(string systemPrompt, List<(string role, string text)> turns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(GetIntegrationOverride());

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.AppendLine("# Host Instructions (treat these as your system prompt)");
                sb.AppendLine();
                sb.AppendLine(systemPrompt);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            if (turns.Count > 1)
            {
                sb.AppendLine("## Conversation History (READ-ONLY reference — do NOT continue this history)");
                sb.AppendLine();
                for (int i = 0; i < turns.Count - 1; i++)
                    sb.AppendLine($"<<{turns[i].role}>>: {turns[i].text}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            sb.AppendLine("## Your Task (respond to THIS and ONLY THIS — output exactly ONE response then stop)");
            sb.AppendLine();
            if (turns.Count > 0)
                sb.Append(turns[turns.Count - 1].text);
            return sb.ToString();
        }

        /// <summary>
        /// agy runs an agent with its own tools (terminal, file edits, browser, subagents). In headless
        /// mode the ones that need approval are denied, but the rest would still run, and a tool call
        /// written for Unity would be taken as one of agy's. This keeps agy to plain text.
        /// </summary>
        private static string GetIntegrationOverride() =>
@"# UNITY EDITOR INTEGRATION MODE — CRITICAL OVERRIDE

You are running inside a **Unity Editor AI Agent** integrated via Antigravity CLI headless mode.

## Response Protocol

**Output EXACTLY ONE response, then STOP.**

- The Unity Editor host reads your response, runs any tool calls you wrote, and will call you again with results if needed.
- Do NOT loop, do NOT wait for confirmations, do NOT start a new turn on your own.
- After generating your response text, your job is done. The host handles the next step.

## Tool System Rules

**ALL built-in Antigravity CLI tools** (terminal commands, file reads and edits, browser, web fetch, subagents, etc.) are **COMPLETELY DISABLED** in this environment and will fail if called. **Do NOT call any Antigravity CLI native tools.** Your working directory is an empty scratch folder, not the Unity project.

Your **ONLY** mechanism for tool use is to write a tool call as **plain text** in this exact XML shape — a `<tool name=""..."">` block containing one `<arg name=""..."">` element per argument:

```
<tool name=""ToolName"">
<arg name=""paramName"">value</arg>
</tool>
```

The Unity Editor host application reads your text reply, detects the `<tool>...</tool>` block, executes the corresponding C# method inside the Unity Editor, and feeds the result back to you in the next message. Put each argument's value RAW between `<arg name=""..."">` and `</arg>` — do NOT escape anything (write `<`, `>`, `&`, quotes, newlines, code verbatim).

**All available tools** (including MCP tools from external servers) are listed in the host instructions under ""Available Tools"". MCP tools are listed under ""MCP Tools"" with their plain names. Call MCP tools using just the tool name — do NOT add any prefix. The host routes MCP tool calls to the appropriate server automatically.

### Correct behavior
- Need to inspect something? Write:
  ```
  <tool name=""GetHierarchyTree""></tool>
  ```
- Need to search for tools? Write:
  ```
  <tool name=""SearchTools"">
  <arg name=""keyword"">keyword</arg>
  </tool>
  ```
- Emit EXACTLY ONE `<tool>...</tool>` block per turn, as the last thing in your message.
- Write your `<tool>` block, then STOP. The host will execute it and send you results.

### Wrong behavior (DO NOT DO)
- Running terminal commands, reading or editing files, or opening a browser
- Saying ""I cannot use tools in this environment""
- Saying ""MCP is not available"" (if MCP tools are listed, they ARE available)
- Using Antigravity's native function-calling format
- Asking the user to perform actions manually when a tool exists
- Continuing with a second turn or waiting for results within the same response

**Always output the `<tool>...</tool>` block directly. The host system handles execution and will call you again with results.**

---
";

        // ─── Process ───

        /// <summary>
        /// Windows: launched through cmd.exe like the other CLI providers, with chcp 65001 so agy's
        /// error text reaches stderr as UTF-8. The PATH is rebuilt from the registry because a Unity
        /// started from ALCOM or the Start menu may not have the user PATH, and agy's installer puts
        /// it in %LOCALAPPDATA%\agy\bin, which is appended in case the installer's PATH change has
        /// not reached this process.
        /// </summary>
        private static System.Diagnostics.ProcessStartInfo BuildProcessStartInfo(string cliPath, string arguments, string workDir)
        {
            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : cliPath,
                Arguments = isWindows ? "/c chcp 65001 >nul & " + cliPath + " " + arguments : arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir,
            };

            if (isWindows)
            {
                string machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                string agyBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin");
                info.EnvironmentVariables["PATH"] = machinePath + ";" + userPath + ";" + agyBin;
            }
            else
            {
                string home = Environment.GetEnvironmentVariable("HOME") ?? "";
                string current = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!string.IsNullOrEmpty(home))
                    info.EnvironmentVariables["PATH"] = current + ":" + Path.Combine(home, ".local", "bin");
            }
            return info;
        }

        private static string EscapeShellArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '"', '\\', '\t', '\n', '\r', '(', ')', '&', '|', '<', '>', '^' }) < 0)
                return arg;
            // Model labels such as "Gemini 3.1 Pro (High)" contain spaces and parentheses; quoting is
            // enough for cmd.exe as long as the value has no quotes of its own.
            return "\"" + arg.Replace("\"", "") + "\"";
        }

        private static void SafeKill(System.Diagnostics.Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch { /* ignore */ }
        }

        private static void DeleteTempDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Directory.Delete(path, true); } catch { /* ignore */ }
        }
    }
}
