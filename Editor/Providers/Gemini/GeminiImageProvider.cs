using System;
using System.Collections;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace AjisaiFlow.UnityAgent.Editor.Providers.Gemini
{
    /// <summary>
    /// Gemini API を使った画像生成専用プロバイダー。
    /// GeminiClient の HTTP パターン（429 リトライ、Abort 等）を踏襲し、
    /// 画像生成固有のリクエスト構築・レスポンスパースを集約する。
    /// </summary>
    internal sealed class GeminiImageProvider : IImageProvider
    {
        public string ProviderName => "Gemini";

        readonly string _apiKey;
        readonly GeminiConnectionMode _mode;
        readonly string _imageModelName;
        readonly string _customEndpoint;
        readonly string _projectId;
        readonly string _location;

        UnityWebRequest _activeRequest;
        bool _aborted;

        public GeminiImageProvider(string apiKey, GeminiConnectionMode mode,
            string imageModelName, string customEndpoint = "",
            string projectId = "", string location = "us-central1")
        {
            _apiKey = apiKey;
            _mode = mode;
            _imageModelName = string.IsNullOrEmpty(imageModelName) ? "gemini-2.5-flash-image" : imageModelName;
            _customEndpoint = customEndpoint;
            _projectId = projectId;
            _location = string.IsNullOrEmpty(location) ? "us-central1" : location;
        }

        public void Abort()
        {
            _aborted = true;
            if (_activeRequest != null && !_activeRequest.isDone)
                _activeRequest.Abort();
            _activeRequest = null;
        }

        /// <summary>
        /// 画像生成リクエストを送信する。
        /// </summary>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="userPrompt">ユーザープロンプト</param>
        /// <param name="inputImagePng">入力画像の PNG バイト列</param>
        /// <param name="onSuccess">(生成画像 PNG bytes, usage 情報文字列)</param>
        /// <param name="onError">エラーメッセージ</param>
        /// <param name="onStatus">進捗ステータス通知</param>
        public IEnumerator GenerateImage(
            string systemPrompt, string userPrompt, byte[] inputImagePng,
            Action<byte[], string> onSuccess, Action<string> onError,
            Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            _aborted = false;

            string url = BuildUrl();
            string base64Image = Convert.ToBase64String(inputImagePng);
            string requestJson = BuildRequestJson(systemPrompt, userPrompt, base64Image);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);

            onDebugLog?.Invoke($"[IMAGE REQUEST] Provider: Gemini, Model: {_imageModelName}, URL: {url}, InputSize: {inputImagePng.Length}bytes, Timeout: 120s");

            int maxRetries = 5;
            int currentRetry = 0;
            float delay = 1.0f;

            while (currentRetry <= maxRetries)
            {
                if (_aborted) yield break;

                using (HttpHelper.AllowInsecureIfNeeded(url))
                using (var request = new UnityWebRequest(url, "POST"))
                {
                    _activeRequest = request;
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = 120;

                    if (!string.IsNullOrEmpty(_apiKey))
                        request.SetRequestHeader("x-goog-api-key", _apiKey);

                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (_aborted) { request.Abort(); _activeRequest = null; yield break; }
                        yield return null;
                    }
                    _activeRequest = null;
                    if (_aborted) yield break;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        string responseJson = request.downloadHandler.text;
                        onDebugLog?.Invoke($"[IMAGE RESPONSE] Code: {request.responseCode}, ResponseSize: {responseJson.Length}chars");

                        byte[] imageBytes = ParseImageBytes(responseJson);
                        if (imageBytes == null)
                        {
                            onError?.Invoke($"No image found in AI response. Response: {Truncate(responseJson, 500)}");
                            yield break;
                        }

                        string usageInfo = ParseUsageInfo(responseJson);
                        onSuccess?.Invoke(imageBytes, usageInfo);
                        yield break;
                    }
                    else if (request.responseCode == 429)
                    {
                        currentRetry++;
                        if (currentRetry > maxRetries)
                        {
                            onError?.Invoke($"Too Many Requests (429) - Max retries exceeded.\nResponse: {request.downloadHandler.text}");
                            yield break;
                        }

                        string waitMsg = $"Rate limit exceeded (429). Retrying in {delay}s... ({currentRetry}/{maxRetries})";
                        onDebugLog?.Invoke($"[IMAGE RETRY] Attempt {currentRetry}/{maxRetries}, delay: {delay}s");
                        Debug.LogWarning($"[GeminiImageProvider] {waitMsg}");
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
                        onDebugLog?.Invoke($"[IMAGE ERROR] {request.error} (Code: {request.responseCode})");
                        onError?.Invoke($"{request.error} (Code: {request.responseCode})\n{request.downloadHandler.text}");
                        yield break;
                    }
                }
            }
        }

        // ─── URL 構築 ───

        string BuildUrl()
        {
            if (_mode == GeminiConnectionMode.Custom)
                return _customEndpoint;

            // 画像生成は v1beta (GoogleAI) / v1beta1 (VertexAI) が必要
            if (_mode == GeminiConnectionMode.VertexAI_Express)
            {
                const string vertexApiVersion = "v1beta1";
                string host = _location.ToLower() == "global"
                    ? "aiplatform.googleapis.com"
                    : $"{_location}-aiplatform.googleapis.com";
                return $"https://{host}/{vertexApiVersion}/projects/{_projectId}/locations/{_location}/publishers/google/models/{_imageModelName}:generateContent";
            }

            const string googleAIApiVersion = "v1beta";
            return $"https://generativelanguage.googleapis.com/{googleAIApiVersion}/models/{_imageModelName}:generateContent";
        }

        // ─── リクエスト JSON 構築 ───

        static string BuildRequestJson(string systemPrompt, string userPrompt, string base64Image)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // System instruction
            sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":");
            sb.Append(EscapeJsonString(systemPrompt));
            sb.Append("}]},");

            // Contents
            sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[");
            sb.Append("{\"text\":");
            sb.Append(EscapeJsonString(userPrompt));
            sb.Append("},");
            sb.Append("{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"");
            sb.Append(base64Image);
            sb.Append("\"}}");
            sb.Append("]}],");

            // Generation config with image output
            sb.Append("\"generationConfig\":{");
            sb.Append("\"responseModalities\":[\"TEXT\",\"IMAGE\"],");
            sb.Append("\"temperature\":0.7");
            sb.Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        // ─── レスポンスパース ───

        static byte[] ParseImageBytes(string json)
        {
            int mimeIdx = FindJsonStringValue(json, "mimeType", 0);
            while (mimeIdx >= 0)
            {
                int valEnd = json.IndexOf('"', mimeIdx);
                if (valEnd < 0) break;
                string mimeVal = json.Substring(mimeIdx, valEnd - mimeIdx);
                if (mimeVal.StartsWith("image/"))
                {
                    int dataStart = FindJsonStringValue(json, "data", valEnd);
                    if (dataStart >= 0)
                    {
                        int dataEnd = json.IndexOf('"', dataStart);
                        if (dataEnd > dataStart)
                        {
                            string base64 = json.Substring(dataStart, dataEnd - dataStart);
                            try { return Convert.FromBase64String(base64); }
                            catch { /* invalid base64, continue searching */ }
                        }
                    }
                }
                mimeIdx = FindJsonStringValue(json, "mimeType", valEnd);
            }
            return null;
        }

        static string ParseUsageInfo(string json)
        {
            int prompt = FindJsonIntValue(json, "promptTokenCount", 0);
            int candidates = FindJsonIntValue(json, "candidatesTokenCount", 0);
            int total = FindJsonIntValue(json, "totalTokenCount", 0);
            if (total <= 0 && prompt <= 0 && candidates <= 0)
                return "Token usage: N/A";
            return $"Token usage: prompt={prompt}, output={candidates}, total={total}";
        }

        // ─── JSON ユーティリティ ───

        static int FindJsonStringValue(string json, string key, int startIndex)
        {
            string keyPattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(keyPattern, startIndex, StringComparison.Ordinal);
            if (keyIdx < 0) return -1;

            int afterKey = keyIdx + keyPattern.Length;
            int colonIdx = -1;
            for (int i = afterKey; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ':') { colonIdx = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return -1;
            }
            if (colonIdx < 0) return -1;

            for (int i = colonIdx + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') return i + 1;
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return -1;
            }
            return -1;
        }

        static int FindJsonIntValue(string json, string key, int startIndex)
        {
            string keyPattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(keyPattern, startIndex, StringComparison.Ordinal);
            if (keyIdx < 0) return 0;

            int afterKey = keyIdx + keyPattern.Length;
            int colonIdx = -1;
            for (int i = afterKey; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ':') { colonIdx = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return 0;
            }
            if (colonIdx < 0) return 0;

            int numStart = -1;
            for (int i = colonIdx + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c >= '0' && c <= '9') { numStart = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return 0;
            }
            if (numStart < 0) return 0;

            int numEnd = numStart;
            while (numEnd < json.Length && json[numEnd] >= '0' && json[numEnd] <= '9') numEnd++;
            if (int.TryParse(json.Substring(numStart, numEnd - numStart), out int val))
                return val;
            return 0;
        }

        static string EscapeJsonString(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        static string Truncate(string s, int maxLen)
        {
            if (s == null) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
    }
}
