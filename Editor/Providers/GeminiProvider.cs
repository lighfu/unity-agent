using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    internal class GeminiProvider : ILLMProvider
    {
        public string ProviderName => "Gemini";

        private GeminiClient _client;
        private string _modelName;
        private int _thinkingBudget;
        private int _effortLevel; // -1=off, 0=low, 1=medium, 2=high
        private LLMProviderType _providerType;
        private ModelCapability _capability;
        private GeminiFeatures _features;

        private static readonly string[] EffortNames = { "low", "medium", "high" };

        public GeminiProvider(string apiKey, GeminiConnectionMode mode, string modelName = "gemini-2.5-flash", string apiVersion = "v1beta", int thinkingBudget = 0, string customEndpoint = "", string projectId = "", string location = "", LLMProviderType providerType = LLMProviderType.Gemini, int effortLevel = -1, GeminiFeatures features = default)
        {
            _modelName = modelName ?? "";
            _providerType = providerType;
            _capability = ModelCapabilityRegistry.GetCapability(_modelName, _providerType);
            _effortLevel = effortLevel;
            _features = features;
            _thinkingBudget = thinkingBudget > 0 && _capability.SupportsThinking
                ? Mathf.Clamp(thinkingBudget, _capability.ThinkingBudgetMin, _capability.ThinkingBudgetMax > 0 ? _capability.ThinkingBudgetMax : thinkingBudget)
                : 0;

            // v1 は system_instruction / thinkingConfig 未サポート → v1beta に自動昇格
            if (apiVersion == "v1")
            {
                if (mode == GeminiConnectionMode.VertexAI_Express)
                {
                    apiVersion = "v1beta1";
                    Debug.Log("[GeminiProvider] system_instruction サポートのため API Version を v1beta1 に自動変更");
                }
                else if (mode == GeminiConnectionMode.GoogleAI)
                {
                    apiVersion = "v1beta";
                    Debug.Log("[GeminiProvider] system_instruction サポートのため API Version を v1beta に自動変更");
                }
            }

            _client = new GeminiClient(apiKey, mode, modelName, apiVersion, customEndpoint, projectId, location);
        }

        private bool SupportsThinking => _capability.SupportsThinking;

        public void Abort() => _client.Abort();

        public IEnumerator CallLLM(IEnumerable<Message> history, Action<string> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null, Action<string> onPartialResponse = null)
        {
            Debug.Log("[GeminiProvider] CallLLM Started");

            string json = BuildRequestJson(history);

            if (_client.SupportsStreaming)
            {
                yield return CallLLMStreaming(json, onSuccess, onError, onStatus, onDebugLog, onPartialResponse);
            }
            else
            {
                yield return CallLLMNonStreaming(json, onSuccess, onError, onStatus, onDebugLog);
            }
        }

        private IEnumerator CallLLMStreaming(string json, Action<string> onSuccess, Action<string> onError, Action<string> onStatus, Action<string> onDebugLog, Action<string> onPartialResponse)
        {
            var textAccumulator = new StringBuilder();
            var thoughtAccumulator = new StringBuilder();
            GeminiUsageMetadata lastUsageMetadata = null;
            GeminiCandidate lastCandidate = null;
            bool hasError = false;

            Action<GeminiResponse> onChunk = chunk =>
            {
                if (chunk.candidates != null && chunk.candidates.Count > 0)
                {
                    var candidate = chunk.candidates[0];
                    if (candidate.content != null && candidate.content.parts != null)
                    {
                        foreach (var part in candidate.content.parts)
                        {
                            if (part.thought)
                                thoughtAccumulator.Append(part.text);
                            else
                            {
                                string special = FormatSpecialPart(part);
                                if (special != null) textAccumulator.Append(special);
                                else if (part.text != null) textAccumulator.Append(part.text);
                            }
                        }
                    }
                    lastCandidate = candidate;
                }

                if (chunk.usageMetadata != null)
                    lastUsageMetadata = chunk.usageMetadata;

                onPartialResponse?.Invoke(textAccumulator.ToString());
            };

            Action onComplete = () => { };

            Action<string> onStreamError = error =>
            {
                hasError = true;
                Debug.LogError($"[GeminiProvider] Stream Error: {error}");
                onError?.Invoke(error);
            };

            yield return _client.StreamGenerateContentFromJson(json, onChunk, onComplete, onStreamError, onStatus, onDebugLog);

            if (hasError) yield break;

            string fullText = textAccumulator.ToString();
            string thoughtText = thoughtAccumulator.ToString();

            if (lastCandidate?.groundingMetadata != null)
                fullText += FormatGroundingMetadata(lastCandidate.groundingMetadata);

            if (lastUsageMetadata != null)
            {
                fullText += $"\n\n[Tokens: {lastUsageMetadata.totalTokenCount} (In: {lastUsageMetadata.promptTokenCount}, Out: {lastUsageMetadata.candidatesTokenCount})]";
            }

            if (!string.IsNullOrEmpty(thoughtText))
            {
                fullText = $"<Thinking>\n{thoughtText}\n</Thinking>\n\n" + fullText;
            }

            onSuccess?.Invoke(fullText);
        }

        private IEnumerator CallLLMNonStreaming(string json, Action<string> onSuccess, Action<string> onError, Action<string> onStatus, Action<string> onDebugLog)
        {
            Action<GeminiResponse> handleResponse = response =>
            {
                if (response.candidates != null && response.candidates.Count > 0)
                {
                    var candidate = response.candidates[0];
                    string fullText = "";
                    string thoughtText = "";

                    if (candidate.content != null && candidate.content.parts != null)
                    {
                        foreach (var part in candidate.content.parts)
                        {
                            if (part.thought)
                                thoughtText += part.text;
                            else
                            {
                                string special = FormatSpecialPart(part);
                                if (special != null) fullText += special;
                                else if (part.text != null) fullText += part.text;
                            }
                        }
                    }

                    if (candidate.groundingMetadata != null)
                        fullText += FormatGroundingMetadata(candidate.groundingMetadata);

                    if (response.usageMetadata != null)
                    {
                         fullText += $"\n\n[Tokens: {response.usageMetadata.totalTokenCount} (In: {response.usageMetadata.promptTokenCount}, Out: {response.usageMetadata.candidatesTokenCount})]";
                    }

                    if (!string.IsNullOrEmpty(thoughtText))
                    {
                        fullText = $"<Thinking>\n{thoughtText}\n</Thinking>\n\n" + fullText;
                    }

                    onSuccess?.Invoke(fullText);
                }
                else
                {
                    onError?.Invoke("No candidates returned.");
                }
            };

            Action<string> handleError = error =>
            {
                Debug.LogError($"[GeminiProvider] Error: {error}");
                onError?.Invoke(error);
            };

            yield return _client.GenerateContentFromJson(json, handleResponse, handleError, onStatus, onDebugLog);
        }

        private string BuildRequestJson(IEnumerable<Message> history)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // Extract system message into system_instruction field
            string systemText = null;
            foreach (var m in history)
            {
                if (m.role == "system" && m.parts?.Length > 0)
                {
                    systemText = m.parts[0].text;
                    break;
                }
            }
            if (systemText != null)
            {
                sb.Append("\"system_instruction\":{\"parts\":[{\"text\":\"");
                sb.Append(EscapeJson(systemText));
                sb.Append("\"}]},");
            }

            // Gemini 組み込みツール
            bool hasTools = _features.GoogleSearch || _features.CodeExecution || _features.UrlContext;
            if (hasTools)
            {
                sb.Append("\"tools\": [");
                bool first = true;
                if (_features.GoogleSearch)  { sb.Append("{\"google_search\": {}}"); first = false; }
                if (_features.CodeExecution) { if (!first) sb.Append(","); sb.Append("{\"code_execution\": {}}"); first = false; }
                if (_features.UrlContext)    { if (!first) sb.Append(","); sb.Append("{\"url_context\": {}}"); }
                sb.Append("],");
            }

            // 安全性設定
            if (_features.SafetyLevel > 0)
            {
                string[] thresholds = { "", "BLOCK_NONE", "BLOCK_ONLY_HIGH",
                                        "BLOCK_MEDIUM_AND_ABOVE", "BLOCK_LOW_AND_ABOVE" };
                string[] cats = { "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH",
                                  "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT",
                                  "HARM_CATEGORY_CIVIC_INTEGRITY" };
                sb.Append("\"safetySettings\": [");
                for (int i = 0; i < cats.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"category\": \"{cats[i]}\", \"threshold\": \"{thresholds[_features.SafetyLevel]}\"}}");
                }
                sb.Append("],");
            }

            sb.Append("\"contents\": [");

            bool firstContent = true;
            foreach (var m in history)
            {
                if (m.role == "system") continue;
                if (m.parts == null || m.parts.Length == 0) continue;

                if (!firstContent) sb.Append(",");
                firstContent = false;

                string role = m.role == "model" ? "model" : "user";
                sb.Append($"{{\"role\": \"{role}\", \"parts\": [");

                bool firstPart = true;
                foreach (var part in m.parts)
                {
                    if (!firstPart) sb.Append(",");
                    firstPart = false;

                    if (part.imageBytes != null && part.imageBytes.Length > 0)
                    {
                        string base64 = Convert.ToBase64String(part.imageBytes);
                        string mime = string.IsNullOrEmpty(part.imageMimeType) ? "image/png" : part.imageMimeType;
                        sb.Append($"{{\"inlineData\": {{\"mimeType\": \"{mime}\", \"data\": \"{base64}\"}}}}");
                    }
                    else
                    {
                        sb.Append($"{{\"text\": \"{EscapeJson(part.text)}\"}}");
                    }
                }

                sb.Append("]}");
            }

            sb.Append($"], \"generationConfig\": {{\"temperature\": 1.0, \"maxOutputTokens\": {_capability.OutputTokenLimit}");

            if (_features.MediaResolution > 0)
            {
                string[] res = { "", "MEDIA_RESOLUTION_LOW", "MEDIA_RESOLUTION_MEDIUM", "MEDIA_RESOLUTION_HIGH" };
                sb.Append($", \"mediaResolution\": \"{res[_features.MediaResolution]}\"");
            }

            if (SupportsThinking)
            {
                // Effort 型 (Gemini 3 系): thinkingLevel で指定
                if (_capability.ThinkingBudgetMax == 0 && _effortLevel >= 0 && _effortLevel < EffortNames.Length)
                {
                    sb.Append($", \"thinkingConfig\": {{\"includeThoughts\": true, \"thinkingLevel\": \"{EffortNames[_effortLevel]}\"}}");
                }
                // Budget 型 (Gemini 2.5 系): thinkingBudget で指定
                else if (_thinkingBudget > 0)
                {
                    sb.Append($", \"thinkingConfig\": {{\"includeThoughts\": true, \"thinkingBudget\": {_thinkingBudget}}}");
                }
            }

            sb.Append("}}");

            return sb.ToString();
        }


        private static string FormatSpecialPart(GeminiPart part)
        {
            if (part.executableCode != null && !string.IsNullOrEmpty(part.executableCode.code))
            {
                string lang = (part.executableCode.language ?? "python").ToLower();
                return $"\n[Code Execution]\n```{lang}\n{part.executableCode.code}\n```\n";
            }
            if (part.codeExecutionResult != null && part.codeExecutionResult.outcome != null)
                return $"[Result: {part.codeExecutionResult.outcome}]\n{part.codeExecutionResult.output ?? ""}\n";
            return null;
        }

        private static string FormatGroundingMetadata(GeminiGroundingMetadata meta)
        {
            if (meta?.groundingChunks == null || meta.groundingChunks.Count == 0) return "";
            var sb = new StringBuilder("\n\n[Sources]\n");
            int idx = 1;
            foreach (var chunk in meta.groundingChunks)
                if (chunk.web != null && !string.IsNullOrEmpty(chunk.web.uri))
                    sb.Append($"{idx++}. {(string.IsNullOrEmpty(chunk.web.title) ? chunk.web.uri : chunk.web.title)}: {chunk.web.uri}\n");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
