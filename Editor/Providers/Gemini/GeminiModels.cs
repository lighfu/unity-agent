using System;
using System.Collections.Generic;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Providers.Gemini
{
    public enum GeminiConnectionMode
    {
        GoogleAI,
        Custom,
        VertexAI_Express
    }

    // --- Request Models ---

    [Serializable]
    public class GeminiRequest
    {
        public List<GeminiContent> contents;
        public List<GeminiTool> tools;
        public GeminiGenerationConfig generationConfig;
    }

    [Serializable]
    public class GeminiContent
    {
        public string role;
        public List<GeminiPart> parts;

        public GeminiContent(string role, string text)
        {
            this.role = role;
            this.parts = new List<GeminiPart> { new GeminiPart { text = text } };
        }
    }

    [Serializable]
    public class GeminiPart
    {
        public string text;
        public bool thought; // For thinking models
        public GeminiExecutableCode executableCode;
        public GeminiCodeExecutionResult codeExecutionResult;
    }

    [Serializable]
    public class GeminiExecutableCode
    {
        public string language; // "PYTHON"
        public string code;
    }

    [Serializable]
    public class GeminiCodeExecutionResult
    {
        public string outcome; // "OUTCOME_OK", "OUTCOME_FAILED"
        public string output;
    }

    [Serializable]
    public class GeminiGroundingMetadata
    {
        public List<GeminiGroundingChunk> groundingChunks;
    }

    [Serializable]
    public class GeminiGroundingChunk
    {
        public GeminiGroundingChunkWeb web;
    }

    [Serializable]
    public class GeminiGroundingChunkWeb
    {
        public string uri;
        public string title;
    }

    [Serializable]
    public class GeminiTool
    {
        // Future: functionDeclarations, googleSearch, etc.
    }

    [Serializable]
    public class GeminiGenerationConfig
    {
        public float temperature = 1.0f;
        public int maxOutputTokens = 8192;
        public GeminiThinkingConfig thinkingConfig;
        
        // Helper to conditionally serialize thinkingConfig logic if needed,
        // but for JsonUtility, we might just set fields.
        // Note: JsonUtility doesn't support null for classes nicely (it just serializes empty objects usually),
        // or we have to be careful.
    }

    [Serializable]
    public class GeminiThinkingConfig
    {
        public bool includeThoughts;
        public int thinkingBudget; // Use -1 for auto
    }

    // --- Response Models ---

    [Serializable]
    public class GeminiResponse
    {
        public List<GeminiCandidate> candidates;
        public GeminiUsageMetadata usageMetadata;
    }

    [Serializable]
    public class GeminiCandidate
    {
        public GeminiContent content;
        public string finishReason;
        public int index;
        public GeminiGroundingMetadata groundingMetadata;
    }

    [Serializable]
    public class GeminiUsageMetadata
    {
        public int promptTokenCount;
        public int candidatesTokenCount;
        public int totalTokenCount;
    }

    /// <summary>Gemini API 組み込み機能の設定。</summary>
    internal struct GeminiFeatures
    {
        public bool GoogleSearch;
        public bool CodeExecution;
        public bool UrlContext;
        public int SafetyLevel;      // 0=Default, 1=BlockNone, 2=BlockOnlyHigh, 3=BlockMedium+, 4=BlockLow+
        public int MediaResolution;  // 0=Default, 1=LOW, 2=MEDIUM, 3=HIGH
    }
}
