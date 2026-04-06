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
    internal class OpenAICompatibleProvider : ILLMProvider
    {
        public string ProviderName => "OpenAI Compatible";

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

        private string _apiKey;
        private string _baseUrl;
        private string _modelName;
        private int _effortLevel; // -1=off, 0=low, 1=medium, 2=high
        private ModelCapability _capability;

        private static readonly string[] EffortNames = { "low", "medium", "high" };
        private const int MaxRetries = 5;

        public OpenAICompatibleProvider(string apiKey, string baseUrl, string modelName, int effortLevel = -1, LLMProviderType providerType = LLMProviderType.OpenAI_Compatible)
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
            _modelName = modelName;
            _effortLevel = effortLevel;
            _capability = ModelCapabilityRegistry.GetCapability(modelName, providerType);
        }

        /// <summary>推論モデルかどうか。reasoning_effort を送信する。</summary>
        private bool IsReasoningModel => _capability.SupportsThinking;

        public IEnumerator CallLLM(IEnumerable<Message> history, Action<string> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null, Action<string> onPartialResponse = null)
        {
            _aborted = false;
            _activeRequest = null;

            string url = _baseUrl.Replace("localhost", "127.0.0.1") + "/chat/completions";
            onStatus?.Invoke($"Requesting to: {url}");

            // Build OpenAI messages array
            var sb = new StringBuilder();
            sb.Append($"{{\"model\": \"{_modelName}\", \"messages\": [");
            bool first = true;
            foreach (var m in history)
            {
                if (!first) sb.Append(",");
                string role = m.role;
                if (role == "model") role = "assistant";

                bool hasImage = m.parts != null && m.parts.Length > 1 &&
                    System.Linq.Enumerable.Any(m.parts, p => p.imageBytes != null);

                if (hasImage)
                {
                    sb.Append($"{{\"role\": \"{role}\", \"content\": [");
                    bool firstPart = true;
                    foreach (var part in m.parts)
                    {
                        if (!firstPart) sb.Append(",");
                        firstPart = false;

                        if (part.imageBytes != null)
                        {
                            string base64 = Convert.ToBase64String(part.imageBytes);
                            sb.Append($"{{\"type\": \"image_url\", \"image_url\": {{\"url\": \"data:{part.imageMimeType};base64,{base64}\"}}}}");
                        }
                        else
                        {
                            sb.Append($"{{\"type\": \"text\", \"text\": \"{EscapeJson(part.text)}\"}}");
                        }
                    }
                    sb.Append("]}");
                }
                else
                {
                    sb.Append($"{{\"role\": \"{role}\", \"content\": \"{EscapeJson(m.parts[0].text)}\"}}");
                }
                first = false;
            }
            sb.Append("]");

            // Add reasoning_effort for o-series / deepseek-reasoner models
            if (IsReasoningModel && _effortLevel >= 0 && _effortLevel < EffortNames.Length)
            {
                sb.Append($", \"reasoning_effort\": \"{EffortNames[_effortLevel]}\"");
            }

            // max_tokens
            if (_capability.OutputTokenLimit > 0)
            {
                sb.Append($", \"max_tokens\": {_capability.OutputTokenLimit}");
            }

            // Enable streaming
            sb.Append(", \"stream\": true");

            sb.Append("}");
            var requestData = sb.ToString();

            AgentLogger.Debug(LogTag.Provider, $"Request to: {url}\nPayload: {requestData}");

            // ── HTTP request with SSE streaming and retry ──
            float retryDelay = 1.0f;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    string waitMsg = $"Rate limit (429). Retrying in {retryDelay}s... ({attempt}/{MaxRetries})";
                    AgentLogger.Warning(LogTag.Provider, waitMsg);
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
                var handler = new SSEDownloadHandler();

                using (HttpHelper.AllowInsecureIfNeeded(url))
                using (var req = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestData);
                    req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    req.downloadHandler = handler;
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Accept", "text/event-stream");
                    if (!string.IsNullOrEmpty(_apiKey))
                        req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

                    onStatus?.Invoke("Connecting...");
                    _activeRequest = req;
                    var op = req.SendWebRequest();

                    while (!op.isDone)
                    {
                        if (_aborted) { _activeRequest = null; yield break; }
                        while (handler.TryDequeue(out string ev))
                            ProcessEvent(ev, acc, onPartialResponse);
                        yield return null;
                    }
                    _activeRequest = null;

                    // Drain remaining events
                    while (handler.TryDequeue(out string ev))
                        ProcessEvent(ev, acc, onPartialResponse);

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        string result = acc.ToString();

                        // Fallback: if streaming produced no content, try parsing as non-streaming response
                        if (string.IsNullOrEmpty(result))
                        {
                            string rawResponse = handler.GetBufferedText();
                            if (!string.IsNullOrEmpty(rawResponse))
                            {
                                result = ExtractJsonValue(rawResponse, "content");
                            }
                        }

                        if (string.IsNullOrEmpty(result))
                        {
                            onError?.Invoke("OpenAI Compatible: 空の応答を受け取りました。");
                            yield break;
                        }

                        AgentLogger.Info(LogTag.Provider, $"Success ({result.Length} chars)");
                        onSuccess?.Invoke(result);
                        yield break;
                    }
                    else if (req.responseCode == 429 && attempt < MaxRetries)
                    {
                        continue;
                    }
                    else
                    {
                        string body = handler.GetBufferedText();
                        if (string.IsNullOrEmpty(body)) body = req.downloadHandler?.text ?? "";
                        string errorDetail = $"Status Code: {req.responseCode}\nError: {req.error}\nContext: {body}";
                        AgentLogger.Error(LogTag.Provider, $"OpenAI Compatible Error:\n{errorDetail}");
                        onError?.Invoke($"OpenAI Error: {req.error}\n{body}");
                        yield break;
                    }
                }
            }
        }

        // ─── SSE event processing ───

        private static void ProcessEvent(string data, StringBuilder acc, Action<string> onPartialResponse)
        {
            if (string.IsNullOrEmpty(data) || data == "[DONE]") return;

            // Extract delta content from: {"choices":[{"delta":{"content":"..."}}]}
            string content = ExtractDeltaContent(data);
            if (!string.IsNullOrEmpty(content))
            {
                acc.Append(content);
                onPartialResponse?.Invoke(acc.ToString());
            }
        }

        /// <summary>
        /// SSE delta から content を抽出する。
        /// OpenAI SSE format: {"choices":[{"delta":{"content":"text"}}]}
        /// </summary>
        private static string ExtractDeltaContent(string json)
        {
            // Find "delta" object
            int deltaIdx = json.IndexOf("\"delta\"", StringComparison.Ordinal);
            if (deltaIdx < 0) return null;

            // Find "content" within delta
            int contentIdx = json.IndexOf("\"content\"", deltaIdx, StringComparison.Ordinal);
            if (contentIdx < 0) return null;

            // Extract string value after "content":
            int i = contentIdx + 9; // length of "content"
            while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;
            if (i >= json.Length) return null;

            // Could be null
            if (json[i] == 'n') return null; // null

            if (json[i] != '"') return null;
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

        // ─── Helpers ───

        private string ExtractJsonValue(string json, string key)
        {
            // Matches "key": "value" or "key": 123
            var match = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\"\\s*:\\s*(\"[^\"]*\"|\\d+)");
            if (match.Success)
            {
                string val = match.Groups[1].Value;
                return val.Trim('\"');
            }
            return "";
        }

        private string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
