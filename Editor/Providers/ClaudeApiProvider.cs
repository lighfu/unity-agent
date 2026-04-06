using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Interfaces;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// Anthropic Claude Messages API への直接接続プロバイダー。
    /// OpenAI 互換ではなく Claude 独自フォーマットを使用するため専用実装が必要。
    /// SSE ストリーミング対応、画像入力対応。
    /// </summary>
    internal class ClaudeApiProvider : ILLMProvider
    {
        public string ProviderName => "Claude API";

        private volatile bool _aborted;
        private UnityWebRequest _activeRequest;

        public void Abort()
        {
            _aborted = true;
            var req = _activeRequest;
            if (req != null)
            {
                try { req.Abort(); } catch { /* ignore */ }
                _activeRequest = null;
            }
        }

        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly int _thinkingBudget;
        private readonly ModelCapability _capability;

        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        private const int MaxRetries = 5;

        public ClaudeApiProvider(string apiKey, string modelName, int thinkingBudget = 0)
        {
            _apiKey = apiKey;
            _modelName = string.IsNullOrEmpty(modelName) ? "claude-sonnet-4-6" : modelName;
            _capability = ModelCapabilityRegistry.GetCapability(_modelName, LLMProviderType.Claude_API);
            _thinkingBudget = thinkingBudget > 0 && _capability.SupportsThinking
                ? Mathf.Clamp(thinkingBudget,
                    _capability.ThinkingBudgetMin,
                    _capability.ThinkingBudgetMax > 0 ? _capability.ThinkingBudgetMax : thinkingBudget)
                : 0;
        }

        // ─── ILLMProvider ───

        public IEnumerator CallLLM(
            IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null)
        {
            _aborted = false;
            _activeRequest = null;

            // ── Build Claude message list ──
            string systemPrompt = null;
            var rawMsgs = new List<ClaudeMsg>();

            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemPrompt = m.parts[0].text;
                    continue;
                }
                if (m.parts == null || m.parts.Length == 0) continue;

                // Skip the initial "System initialized." (role=model before any user message)
                if (m.role == "model" && rawMsgs.Count == 0)
                    continue;

                string role = m.role == "model" ? "assistant" : "user";

                var textSb = new StringBuilder();
                byte[] imageBytes = null;
                string mimeType = null;
                foreach (var part in m.parts)
                {
                    if (!string.IsNullOrEmpty(part.text))
                        textSb.Append(part.text);
                    if (part.imageBytes != null)
                    {
                        imageBytes = part.imageBytes;
                        mimeType = string.IsNullOrEmpty(part.imageMimeType) ? "image/png" : part.imageMimeType;
                    }
                }

                string text = textSb.ToString();
                if (!string.IsNullOrEmpty(text) || imageBytes != null)
                    rawMsgs.Add(new ClaudeMsg(role, text, imageBytes, mimeType));
            }

            // Claude API requires strictly alternating user/assistant — merge same-role runs
            var msgs = MergeConsecutiveSameRole(rawMsgs);

            if (msgs.Count == 0)
            {
                onError?.Invoke("Claude API: 送信可能なメッセージがありません。");
                yield break;
            }

            string requestJson = BuildRequestJson(systemPrompt, msgs);
            onDebugLog?.Invoke($"[ClaudeApiProvider] POST {Endpoint} model={_modelName}");

            // ── HTTP request with SSE streaming and retry ──
            float retryDelay = 1.0f;
            string lastRateLimitBody = null;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    string waitMsg = $"Claude API rate limit (429). Retrying in {retryDelay}s... ({attempt}/{MaxRetries})";
                    AgentLogger.Warning(LogTag.Provider,
                        $"{waitMsg}\n  Model: {_modelName}\n  Response: {lastRateLimitBody ?? "(empty)"}");
                    onStatus?.Invoke(waitMsg);
                    double t0 = EditorApplication.timeSinceStartup;
                    while (EditorApplication.timeSinceStartup - t0 < retryDelay)
                    {
                        if (_aborted) yield break;
                        yield return null;
                    }
                    retryDelay *= 2f;
                }

                var acc = new StringBuilder();
                var thinkingAcc = new StringBuilder();
                var handler = new SSEDownloadHandler();

                using (HttpHelper.AllowInsecureIfNeeded(Endpoint))
                using (var req = new UnityWebRequest(Endpoint, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                    req.downloadHandler = handler;
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("x-api-key", _apiKey);
                    req.SetRequestHeader("anthropic-version", AnthropicVersion);

                    onStatus?.Invoke("Connecting to Claude API...");
                    _activeRequest = req;
                    var op = req.SendWebRequest();

                    while (!op.isDone)
                    {
                        if (_aborted) { _activeRequest = null; yield break; }
                        while (handler.TryDequeue(out string ev))
                            ProcessEvent(ev, acc, thinkingAcc, onPartialResponse, onDebugLog);
                        yield return null;
                    }
                    _activeRequest = null;

                    while (handler.TryDequeue(out string ev))
                        ProcessEvent(ev, acc, thinkingAcc, onPartialResponse, onDebugLog);

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        string result = acc.ToString();
                        if (string.IsNullOrEmpty(result))
                        {
                            onError?.Invoke("Claude API から空の応答を受け取りました。");
                            yield break;
                        }
                        // Wrap thinking content in <Thinking> tags for UI extraction
                        if (thinkingAcc.Length > 0)
                            result = $"<Thinking>\n{thinkingAcc}\n</Thinking>\n{result}";
                        onDebugLog?.Invoke($"[ClaudeApiProvider] Response received ({result.Length} chars)");
                        onSuccess?.Invoke(result);
                        yield break;
                    }
                    else if (req.responseCode == 429 && attempt < MaxRetries)
                    {
                        lastRateLimitBody = handler.GetBufferedText();
                        if (string.IsNullOrEmpty(lastRateLimitBody))
                            lastRateLimitBody = req.downloadHandler?.text;
                        string retryAfter = req.GetResponseHeader("Retry-After");
                        if (!string.IsNullOrEmpty(retryAfter))
                            AgentLogger.Info(LogTag.Provider, $"Retry-After header: {retryAfter}");
                        continue;
                    }
                    else
                    {
                        string body = handler.GetBufferedText();
                        if (string.IsNullOrEmpty(body)) body = req.downloadHandler?.text ?? "";
                        string errorDetail = $"Claude API エラー (HTTP {req.responseCode}): {req.error}\nModel: {_modelName}\nResponse: {body}";
                        AgentLogger.Error(LogTag.Provider, errorDetail);
                        onError?.Invoke($"Claude API エラー (HTTP {req.responseCode}): {req.error}\n{body}");
                        yield break;
                    }
                }
            }

            // 全リトライ失敗
            string exhaustedMsg = $"Rate limit (429) - Max retries ({MaxRetries}) exceeded.\nModel: {_modelName}\nLast response: {lastRateLimitBody ?? "(empty)"}";
            AgentLogger.Error(LogTag.Provider, exhaustedMsg);
            onError?.Invoke($"Error: {exhaustedMsg}");
        }

        // ─── SSE event processing ───

        private static void ProcessEvent(string data, StringBuilder acc, StringBuilder thinkingAcc,
            Action<string> onPartialResponse, Action<string> onDebugLog)
        {
            if (string.IsNullOrEmpty(data) || data == "[DONE]") return;

            if (data.Contains("\"content_block_delta\""))
            {
                int deltaStart = data.IndexOf("\"delta\"", StringComparison.Ordinal);
                if (deltaStart < 0) return;
                string deltaType = ExtractStringAfter(data, "type", deltaStart);

                if (deltaType == "thinking_delta")
                {
                    string thinking = ExtractStringAfter(data, "thinking", deltaStart);
                    if (!string.IsNullOrEmpty(thinking))
                        thinkingAcc.Append(thinking);
                }
                else if (deltaType == "text_delta")
                {
                    string text = ExtractStringAfter(data, "text", deltaStart);
                    if (!string.IsNullOrEmpty(text))
                    {
                        acc.Append(text);
                        onPartialResponse?.Invoke(acc.ToString());
                    }
                }
            }
        }

        // ─── JSON builder ───

        private string BuildRequestJson(string system, List<ClaudeMsg> messages)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"model\":{JS(_modelName)},");

            bool useThinking = _thinkingBudget > 0;
            int outputLimit = _capability.OutputTokenLimit;
            int maxTokens = useThinking ? Mathf.Max(outputLimit, _thinkingBudget + 4096) : outputLimit;
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append("\"stream\":true");

            if (useThinking)
                sb.Append($",\"thinking\":{{\"type\":\"enabled\",\"budget_tokens\":{_thinkingBudget}}}");

            if (!string.IsNullOrEmpty(system))
                sb.Append($",\"system\":{JS(system)}");

            sb.Append(",\"messages\":[");
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var msg = messages[i];
                sb.Append('{');
                sb.Append($"\"role\":{JS(msg.Role)},");

                bool hasImage = msg.ImageBytes != null && msg.ImageBytes.Length > 0;
                if (hasImage)
                {
                    // Use content array for image+text
                    sb.Append("\"content\":[");
                    sb.Append("{\"type\":\"image\",\"source\":{\"type\":\"base64\",");
                    sb.Append($"\"media_type\":{JS(msg.MimeType ?? "image/png")},");
                    sb.Append($"\"data\":{JS(Convert.ToBase64String(msg.ImageBytes))}}}}}");
                    if (!string.IsNullOrEmpty(msg.Text))
                        sb.Append($",{{\"type\":\"text\",\"text\":{JS(msg.Text)}}}");
                    sb.Append(']');
                }
                else
                {
                    // Simple string content
                    sb.Append($"\"content\":{JS(msg.Text ?? "")}");
                }

                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        // ─── Helpers ───

        private static List<ClaudeMsg> MergeConsecutiveSameRole(List<ClaudeMsg> src)
        {
            var result = new List<ClaudeMsg>();
            foreach (var msg in src)
            {
                if (result.Count > 0 && result[result.Count - 1].Role == msg.Role)
                {
                    var last = result[result.Count - 1];
                    string merged = string.IsNullOrEmpty(last.Text)
                        ? msg.Text
                        : string.IsNullOrEmpty(msg.Text)
                            ? last.Text
                            : last.Text + "\n\n" + msg.Text;
                    result[result.Count - 1] = new ClaudeMsg(
                        last.Role, merged,
                        last.ImageBytes ?? msg.ImageBytes,
                        last.MimeType ?? msg.MimeType);
                }
                else
                {
                    result.Add(msg);
                }
            }
            return result;
        }

        /// <summary>JSON 文字列エスケープ付き引用符生成。</summary>
        private static string JS(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 4);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>JSON の指定位置以降から "key": "value" パターンを探して文字列値を返す。</summary>
        private static string ExtractStringAfter(string json, string key, int startIndex)
        {
            if (string.IsNullOrEmpty(json) || startIndex < 0) return null;
            string needle = $"\"{key}\"";
            int from = startIndex;

            while (true)
            {
                int ki = json.IndexOf(needle, from, StringComparison.Ordinal);
                if (ki < 0) return null;

                int i = ki + needle.Length;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i] != ':') { from = ki + needle.Length; continue; }
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
                        switch (json[i + 1])
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case 'n':  sb.Append('\n'); break;
                            case 'r':  sb.Append('\r'); break;
                            case 't':  sb.Append('\t'); break;
                            default:   sb.Append(json[i + 1]); break;
                        }
                        i += 2;
                    }
                    else if (c == '"') return sb.ToString();
                    else { sb.Append(c); i++; }
                }
                return null;
            }
        }

        // ─── SSE download handler ───

        private sealed class SSEDownloadHandler : DownloadHandlerScript
        {
            private readonly object _lock = new object();
            private readonly StringBuilder _buffer = new StringBuilder();
            private readonly Queue<string> _queue = new Queue<string>();

            public SSEDownloadHandler() : base(new byte[4096]) { }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                string text = Encoding.UTF8.GetString(data, 0, dataLength).Replace("\r", "");
                lock (_lock) { _buffer.Append(text); Flush(); }
                return true;
            }

            private void Flush()
            {
                string buf = _buffer.ToString();
                int idx;
                while ((idx = buf.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
                {
                    string block = buf.Substring(0, idx);
                    buf = buf.Substring(idx + 2);
                    foreach (string line in block.Split('\n'))
                    {
                        string t = line.Trim();
                        if (t.StartsWith("data: "))
                            _queue.Enqueue(t.Substring(6));
                    }
                }
                _buffer.Clear();
                _buffer.Append(buf);
            }

            public bool TryDequeue(out string data)
            {
                lock (_lock)
                {
                    if (_queue.Count > 0) { data = _queue.Dequeue(); return true; }
                }
                data = null;
                return false;
            }

            public string GetBufferedText()
            {
                lock (_lock) { return _buffer.ToString(); }
            }
        }

        // ─── Message value type ───

        private struct ClaudeMsg
        {
            public readonly string Role;
            public readonly string Text;
            public readonly byte[] ImageBytes;
            public readonly string MimeType;

            public ClaudeMsg(string role, string text, byte[] img, string mime)
            {
                Role = role; Text = text; ImageBytes = img; MimeType = mime;
            }
        }
    }
}
