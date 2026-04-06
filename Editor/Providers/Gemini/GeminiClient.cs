using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;

namespace AjisaiFlow.UnityAgent.Editor.Providers.Gemini
{
    public class GeminiClient
    {
        private string _apiKey;
        private GeminiConnectionMode _mode;
        private string _apiVersion;
        private string _modelName;
        private string _customEndpoint;
        private string _projectId;
        private string _location;

        /// <summary>現在実行中のリクエスト。Abort() で中断可能。</summary>
        private UnityWebRequest _activeRequest;
        private bool _aborted;

        public GeminiClient(string apiKey, GeminiConnectionMode mode, string modelName, string apiVersion, string customEndpoint, string projectId, string location)
        {
            _apiKey = apiKey;
            _mode = mode;
            _apiVersion = string.IsNullOrEmpty(apiVersion) ? "v1" : apiVersion; // Default v1
            _modelName = string.IsNullOrEmpty(modelName) ? "gemini-2.5-flash" : modelName;
            _customEndpoint = customEndpoint;
            _projectId = projectId;
            _location = string.IsNullOrEmpty(location) ? "us-central1" : location;
        }

        public bool SupportsStreaming => _mode != GeminiConnectionMode.Custom;

        /// <summary>進行中のHTTPリクエストを中断する。</summary>
        public void Abort()
        {
            _aborted = true;
            if (_activeRequest != null && !_activeRequest.isDone)
            {
                _activeRequest.Abort();
            }
            _activeRequest = null;
        }

        public IEnumerator GenerateContent(GeminiRequest requestBody, Action<GeminiResponse> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            string json = EditorJsonUtility.ToJson(requestBody);
            yield return SendRequest(json, onSuccess, onError, onStatus, onDebugLog);
        }

        public IEnumerator GenerateContentFromJson(string json, Action<GeminiResponse> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            yield return SendRequest(json, onSuccess, onError, onStatus, onDebugLog);
        }

        public IEnumerator StreamGenerateContent(GeminiRequest requestBody, Action<GeminiResponse> onChunk, Action onComplete, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            string json = EditorJsonUtility.ToJson(requestBody);
            yield return StreamSendRequest(json, onChunk, onComplete, onError, onStatus, onDebugLog);
        }

        public IEnumerator StreamGenerateContentFromJson(string json, Action<GeminiResponse> onChunk, Action onComplete, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            yield return StreamSendRequest(json, onChunk, onComplete, onError, onStatus, onDebugLog);
        }

        private IEnumerator SendRequest(string json, Action<GeminiResponse> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            _aborted = false;
            string url = BuildUrl();

            onDebugLog?.Invoke($"[REQUEST] URL: {url}, Timeout: 300s\nBody:\n{json}");
            onStatus?.Invoke($"Requesting to: {url}");

            int maxRetries = 5;
            int currentRetry = 0;
            float delay = 1.0f;

            while (currentRetry <= maxRetries)
            {
                if (_aborted) yield break;

                using (HttpHelper.AllowInsecureIfNeeded(url))
                using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
                {
                    _activeRequest = request;
                    request.timeout = 300;
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    if (_mode == GeminiConnectionMode.GoogleAI || _mode == GeminiConnectionMode.VertexAI_Express)
                    {
                        request.SetRequestHeader("x-goog-api-key", _apiKey);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_apiKey))
                        {
                            request.SetRequestHeader("x-goog-api-key", _apiKey);
                        }
                    }

                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (_aborted) { request.Abort(); _activeRequest = null; yield break; }
                        yield return null;
                    }
                    _activeRequest = null;
                    if (_aborted) yield break;

                    if (request.downloadHandler != null)
                    {
                         onDebugLog?.Invoke($"[RESPONSE] Code: {request.responseCode}\nBody:\n{request.downloadHandler.text}");
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        string responseJson = request.downloadHandler.text;
                        GeminiResponse parsed = null;
                        string parseError = null;
                        try
                        {
                            // SSE 形式のレスポンス ("data: {...}") を検出してフォールバック
                            if (responseJson.TrimStart().StartsWith("data:"))
                                parsed = ParseSSEResponse(responseJson);
                            else
                                parsed = JsonUtility.FromJson<GeminiResponse>(responseJson);
                        }
                        catch (Exception ex)
                        {
                            parseError = $"Failed to parse response: {ex.Message}\nRaw: {responseJson}";
                        }
                        if (parseError != null) { onError?.Invoke(parseError); yield break; }
                        // Yield after heavy parsing to prevent editor freeze
                        yield return null;
                        onSuccess?.Invoke(parsed);
                        yield break;
                    }
                    else if (request.responseCode == 429)
                    {
                        currentRetry++;
                        if (currentRetry > maxRetries)
                        {
                            onError?.Invoke($"Error: Too Many Requests (429) - Max retries exceeded.\nResponse: {request.downloadHandler.text}");
                            yield break;
                        }

                        string waitMsg = $"Rate limit exceeded (429). Retrying in {delay}s... ({currentRetry}/{maxRetries})";
                        AgentLogger.Warning(LogTag.Provider, waitMsg);
                        onStatus?.Invoke(waitMsg);

                        double startTime = UnityEditor.EditorApplication.timeSinceStartup;
                        while (UnityEditor.EditorApplication.timeSinceStartup - startTime < delay)
                        {
                            if (_aborted) yield break;
                            yield return null;
                        }

                        delay *= 2.0f;
                        continue;
                    }
                    else
                    {
                        onError?.Invoke($"Error: {request.error} (Code: {request.responseCode})\nResponse: {request.downloadHandler.text}");
                        yield break;
                    }
                }
            }
        }

        private IEnumerator StreamSendRequest(string json, Action<GeminiResponse> onChunk, Action onComplete, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            _aborted = false;
            string url = BuildStreamUrl();

            onDebugLog?.Invoke($"[STREAM REQUEST] URL: {url}, Timeout: 300s\nBody:\n{json}");
            onStatus?.Invoke($"Streaming from: {url}");

            int maxRetries = 5;
            int currentRetry = 0;
            float delay = 1.0f;

            while (currentRetry <= maxRetries)
            {
                if (_aborted) yield break;

                var handler = new SSEDownloadHandler();
                using (HttpHelper.AllowInsecureIfNeeded(url))
                using (var request = new UnityWebRequest(url, "POST"))
                {
                    _activeRequest = request;
                    request.timeout = 300;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.downloadHandler = handler;
                    request.SetRequestHeader("Content-Type", "application/json");

                    if (_mode == GeminiConnectionMode.GoogleAI || _mode == GeminiConnectionMode.VertexAI_Express)
                    {
                        request.SetRequestHeader("x-goog-api-key", _apiKey);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_apiKey))
                        {
                            request.SetRequestHeader("x-goog-api-key", _apiKey);
                        }
                    }

                    var op = request.SendWebRequest();

                    while (!op.isDone)
                    {
                        if (_aborted) { request.Abort(); _activeRequest = null; yield break; }

                        while (handler.TryDequeue(out string chunkJson))
                        {
                            try
                            {
                                var chunk = JsonUtility.FromJson<GeminiResponse>(chunkJson);
                                onChunk?.Invoke(chunk);
                            }
                            catch (Exception ex)
                            {
                                onDebugLog?.Invoke($"[STREAM] Failed to parse chunk: {ex.Message}\nRaw: {chunkJson}");
                            }
                        }
                        yield return null;
                    }

                    _activeRequest = null;
                    if (_aborted) yield break;

                    // Process remaining chunks after request completes
                    while (handler.TryDequeue(out string remaining))
                    {
                        try
                        {
                            var chunk = JsonUtility.FromJson<GeminiResponse>(remaining);
                            onChunk?.Invoke(chunk);
                        }
                        catch (Exception ex)
                        {
                            onDebugLog?.Invoke($"[STREAM] Failed to parse final chunk: {ex.Message}");
                        }
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        onComplete?.Invoke();
                        yield break;
                    }
                    else if (request.responseCode == 429)
                    {
                        currentRetry++;
                        if (currentRetry > maxRetries)
                        {
                            onError?.Invoke($"Error: Too Many Requests (429) - Max retries exceeded.");
                            yield break;
                        }

                        string waitMsg = $"Rate limit exceeded (429). Retrying in {delay}s... ({currentRetry}/{maxRetries})";
                        AgentLogger.Warning(LogTag.Provider, waitMsg);
                        onStatus?.Invoke(waitMsg);

                        double startTime = EditorApplication.timeSinceStartup;
                        while (EditorApplication.timeSinceStartup - startTime < delay)
                        {
                            if (_aborted) yield break;
                            yield return null;
                        }

                        delay *= 2.0f;
                        continue;
                    }
                    else if (_aborted)
                    {
                        yield break;
                    }
                    else
                    {
                        string errorBody = handler.GetBufferedText();
                        if (string.IsNullOrEmpty(errorBody))
                            errorBody = request.downloadHandler?.text ?? "";
                        onError?.Invoke($"Error: {request.error} (Code: {request.responseCode})\n{errorBody}");
                        yield break;
                    }
                }
            }
        }

        private string BuildUrl()
        {
            if (_mode == GeminiConnectionMode.Custom)
            {
                return _customEndpoint;
            }
            if (_mode == GeminiConnectionMode.VertexAI_Express)
            {
                // If location is global, use generic aiplatform endpoint
                string host = _location.ToLower() == "global"
                    ? "aiplatform.googleapis.com"
                    : $"{_location}-aiplatform.googleapis.com";

                return $"https://{host}/{_apiVersion}/projects/{_projectId}/locations/{_location}/publishers/google/models/{_modelName}:generateContent";
            }
            return $"https://generativelanguage.googleapis.com/{_apiVersion}/models/{_modelName}:generateContent";
        }

        private string BuildStreamUrl()
        {
            if (_mode == GeminiConnectionMode.VertexAI_Express)
            {
                string host = _location.ToLower() == "global"
                    ? "aiplatform.googleapis.com"
                    : $"{_location}-aiplatform.googleapis.com";

                return $"https://{host}/{_apiVersion}/projects/{_projectId}/locations/{_location}/publishers/google/models/{_modelName}:streamGenerateContent?alt=sse";
            }
            return $"https://generativelanguage.googleapis.com/{_apiVersion}/models/{_modelName}:streamGenerateContent?alt=sse";
        }

        /// <summary>SSE 形式のレスポンス本文を GeminiResponse に変換する。</summary>
        private static GeminiResponse ParseSSEResponse(string sseBody)
        {
            var textSb = new StringBuilder();
            var thoughtSb = new StringBuilder();
            GeminiResponse last = null;
            GeminiGroundingMetadata lastGrounding = null;
            var specialParts = new List<GeminiPart>();

            foreach (var line in sseBody.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("data: ")) continue;
                var json = trimmed.Substring(6);
                if (string.IsNullOrEmpty(json)) continue;

                GeminiResponse chunk;
                try { chunk = JsonUtility.FromJson<GeminiResponse>(json); }
                catch { continue; }

                if (chunk.candidates != null && chunk.candidates.Count > 0)
                {
                    var c = chunk.candidates[0];
                    if (c.content?.parts != null)
                    {
                        foreach (var p in c.content.parts)
                        {
                            if (p.thought) thoughtSb.Append(p.text);
                            else if (p.executableCode != null || p.codeExecutionResult != null)
                                specialParts.Add(p);
                            else textSb.Append(p.text);
                        }
                    }
                    if (c.groundingMetadata != null)
                        lastGrounding = c.groundingMetadata;
                }
                last = chunk;
            }

            // 結合テキストで単一レスポンスを構築
            var resultCandidate = new GeminiCandidate
            {
                content = new GeminiContent("model", textSb.ToString()),
                finishReason = "STOP",
                groundingMetadata = lastGrounding
            };

            // Code Execution パーツを追加
            foreach (var sp in specialParts)
                resultCandidate.content.parts.Add(sp);

            var result = new GeminiResponse
            {
                candidates = new List<GeminiCandidate> { resultCandidate },
                usageMetadata = last?.usageMetadata
            };

            if (thoughtSb.Length > 0)
            {
                result.candidates[0].content.parts.Insert(0,
                    new GeminiPart { text = thoughtSb.ToString(), thought = true });
            }

            return result;
        }

        private class SSEDownloadHandler : DownloadHandlerScript
        {
            private readonly object _lock = new object();
            private readonly StringBuilder _buffer = new StringBuilder();
            private readonly Queue<string> _pendingChunks = new Queue<string>();

            public SSEDownloadHandler() : base(new byte[4096]) { }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                // Strip \r so that \r\n\r\n (CRLF) becomes \n\n (LF) for SSE parsing
                string text = Encoding.UTF8.GetString(data, 0, dataLength).Replace("\r", "");
                lock (_lock)
                {
                    _buffer.Append(text);
                    ProcessBuffer();
                }
                return true;
            }

            private void ProcessBuffer()
            {
                string buf = _buffer.ToString();
                int idx;
                while ((idx = buf.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
                {
                    string eventBlock = buf.Substring(0, idx);
                    buf = buf.Substring(idx + 2);
                    foreach (var line in eventBlock.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("data: "))
                            _pendingChunks.Enqueue(trimmed.Substring(6));
                    }
                }
                _buffer.Clear();
                _buffer.Append(buf);
            }

            public bool TryDequeue(out string json)
            {
                lock (_lock)
                {
                    if (_pendingChunks.Count > 0)
                    {
                        json = _pendingChunks.Dequeue();
                        return true;
                    }
                }
                json = null;
                return false;
            }

            /// <summary>Get remaining buffered text (useful for non-SSE error responses).</summary>
            public string GetBufferedText()
            {
                lock (_lock) { return _buffer.ToString(); }
            }
        }
    }
}
