using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// OpenAI Images API (gpt-image-1) を使った画像生成プロバイダー。
    /// POST /v1/images/edits — multipart/form-data 形式。
    /// GeminiImageProvider と同じ 429 リトライパターンを踏襲。
    /// </summary>
    internal sealed class OpenAIImageProvider : IImageProvider
    {
        public string ProviderName => "OpenAI";

        readonly string _apiKey;
        readonly string _modelName;
        readonly string _baseUrl;

        UnityWebRequest _activeRequest;
        bool _aborted;

        public OpenAIImageProvider(string apiKey, string modelName, string baseUrl = "https://api.openai.com")
        {
            _apiKey = apiKey;
            _modelName = string.IsNullOrEmpty(modelName) ? "gpt-image-1" : modelName;
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://api.openai.com" : baseUrl.TrimEnd('/');
        }

        public void Abort()
        {
            _aborted = true;
            if (_activeRequest != null && !_activeRequest.isDone)
                _activeRequest.Abort();
            _activeRequest = null;
        }

        public IEnumerator GenerateImage(
            string systemPrompt, string userPrompt, byte[] inputImagePng,
            Action<byte[], string> onSuccess, Action<string> onError,
            Action<string> onStatus = null, Action<string> onDebugLog = null)
        {
            _aborted = false;

            string url = $"{_baseUrl}/v1/images/edits";
            onDebugLog?.Invoke($"[IMAGE REQUEST] Provider: OpenAI, Model: {_modelName}, URL: {url}, InputSize: {inputImagePng.Length}bytes, Timeout: 120s");
            string combinedPrompt = string.IsNullOrEmpty(systemPrompt)
                ? userPrompt
                : systemPrompt + "\n\n" + userPrompt;

            int maxRetries = 5;
            int currentRetry = 0;
            float delay = 1.0f;

            while (currentRetry <= maxRetries)
            {
                if (_aborted) yield break;

                // Build multipart form data
                var formData = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("model", _modelName),
                    new MultipartFormDataSection("prompt", combinedPrompt),
                    new MultipartFormFileSection("image[]", inputImagePng, "input.png", "image/png"),
                    new MultipartFormDataSection("size", "1024x1024"),
                };

                using (HttpHelper.AllowInsecureIfNeeded(url))
                using (var request = UnityWebRequest.Post(url, formData))
                {
                    _activeRequest = request;
                    request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    request.timeout = 120;

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

                        byte[] imageBytes = ParseB64ImageBytes(responseJson);
                        if (imageBytes == null)
                        {
                            onError?.Invoke($"No image found in OpenAI response. Response: {Truncate(responseJson, 500)}");
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
                        Debug.LogWarning($"[OpenAIImageProvider] {waitMsg}");
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

        // ─── レスポンスパース ───

        /// <summary>
        /// OpenAI Images API レスポンスから b64_json 画像を抽出。
        /// レスポンス形式: { "data": [{ "b64_json": "..." }] }
        /// </summary>
        static byte[] ParseB64ImageBytes(string json)
        {
            int dataStart = FindJsonStringValue(json, "b64_json", 0);
            if (dataStart < 0) return null;

            int dataEnd = json.IndexOf('"', dataStart);
            if (dataEnd <= dataStart) return null;

            string base64 = json.Substring(dataStart, dataEnd - dataStart);
            try { return Convert.FromBase64String(base64); }
            catch { return null; }
        }

        static string ParseUsageInfo(string json)
        {
            int input = FindJsonIntValue(json, "input_tokens", 0);
            int output = FindJsonIntValue(json, "output_tokens", 0);
            int total = FindJsonIntValue(json, "total_tokens", 0);
            if (total <= 0 && input <= 0 && output <= 0)
                return "Token usage: N/A";
            return $"Token usage: input={input}, output={output}, total={total}";
        }

        // ─── JSON ユーティリティ (GeminiImageProvider と同パターン) ───

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

        static string Truncate(string s, int maxLen)
        {
            if (s == null) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
    }
}
