using System;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.Editor.Providers.Gemini;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    // ═══════════════════════════════════════════════════════
    //  Shared enums
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// LLM プロバイダー種別。int 値順序は既存永続化と完全一致。
    /// </summary>
    internal enum LLMProviderType
    {
        Gemini, OpenAI_Compatible, Claude_CLI, Gemini_CLI, Clipboard,
        Claude_API, OpenAI, DeepSeek, Groq, Ollama, xAI_Grok, Mistral, Perplexity,
        Gemini_Web, Vertex_AI, Codex_CLI,
    }

    /// <summary>
    /// プロバイダーを UI / 生成パターンでグループ化。
    /// </summary>
    internal enum ProviderSettingsKind
    {
        OpenAICompatibleApiKey,  // OpenAI, DeepSeek, Groq, xAI, Mistral, Perplexity
        OpenAICompatibleUrl,     // Ollama, OpenAI_Compatible
        ClaudeApi,               // Claude API
        Gemini,                  // Gemini (Google AI)
        VertexAI,                // Vertex AI
        CliProvider,             // Claude CLI, Gemini CLI, Codex CLI
        BrowserBridge,           // Gemini_Web
        Clipboard,               // Clipboard
    }

    /// <summary>
    /// 画像生成プロバイダー種別。
    /// </summary>
    internal enum ImageProviderType
    {
        Gemini = 0,
        OpenAI = 1,
    }

    /// <summary>
    /// 思考モード UI 種別。
    /// </summary>
    internal enum ThinkingMode
    {
        None,
        Budget,
        Effort,
    }

    // ═══════════════════════════════════════════════════════
    //  ProviderDescriptor — 各プロバイダーの宣言的定義
    // ═══════════════════════════════════════════════════════

    internal sealed class ProviderDescriptor
    {
        public string DisplayName;
        public string ShortName;
        public ProviderSettingsKind SettingsKind;
        public string[] ModelPresets;
        public string[] ModelDisplayNames;
        public string DefaultModel;
        public ThinkingMode ThinkingMode;
        public string ThinkingHintKey; // L10n key for thinking UI hint
        public string DefaultBaseUrl;
        public bool SupportsModelSelection;
        public string DescriptionKey;  // L10n key for settings section description
        public string SectionTitle;    // settings section title prefix (e.g. "OpenAI")

        // SettingsStore keys — 既存キーと完全一致
        public string SettingsKeyApiKey;
        public string SettingsKeyModelName;
        public string SettingsKeyBaseUrl;
        public string SettingsKeyCliPath;
    }

    // ═══════════════════════════════════════════════════════
    //  ProviderConfig — プロバイダーごとの実行時設定バッグ
    // ═══════════════════════════════════════════════════════

    internal sealed class ProviderConfig
    {
        public string ApiKey = "";
        public string ModelName = "";
        public string BaseUrl = "";
        public string CliPath = "";
        public int Port;
        public bool UseCustomModel;

        // Gemini/VertexAI 固有
        public GeminiConnectionMode GeminiMode = GeminiConnectionMode.GoogleAI;
        public string ApiVersion = "v1";
        public string CustomEndpoint = "";
        public string ProjectId = "";
        public string Location = "us-central1";

        // Gemini 組み込み機能 (Gemini / Vertex AI 共通)
        public bool GeminiGoogleSearch;
        public bool GeminiCodeExecution;
        public bool GeminiUrlContext;
        public int GeminiSafetyLevel;
        public int GeminiMediaResolution;

        /// <summary>0 = 自動 (ModelCapability.InputTokenLimit を使用)</summary>
        public int MaxContextTokens;
    }

    // ═══════════════════════════════════════════════════════
    //  ProviderRegistry — 静的レジストリ
    // ═══════════════════════════════════════════════════════

    internal static class ProviderRegistry
    {
        // ─── Static data shared across windows ───

        public static readonly string[] ApiVersionOptions = { "v1", "v1beta", "v1beta1" };

        /// <summary>Google AI 用 API バージョン。v1beta が推奨 (system_instruction, thinkingConfig 対応)。</summary>
        public static readonly string[] GoogleAIApiVersions = { "v1beta", "v1" };
        public static readonly string[] GoogleAIApiVersionLabels = { "v1beta (推奨: system_instruction, 思考モード対応)", "v1 (安定版: 基本生成のみ)" };

        /// <summary>Vertex AI 用 API バージョン。v1beta1 が推奨。</summary>
        public static readonly string[] VertexAIApiVersions = { "v1beta1", "v1" };
        public static readonly string[] VertexAIApiVersionLabels = { "v1beta1 (推奨: system_instruction, 思考モード対応)", "v1 (安定版: 基本生成のみ)" };

        public static readonly string[] GeminiImageModelPresets =
        {
            "gemini-2.5-flash-image",
            "gemini-3.1-flash-image-preview",
        };

        public static readonly string[] ImageProviderDisplayNames = { "Gemini", "OpenAI" };

        public static readonly string[] OpenAIImageModelPresets = { "gpt-image-1.5", "gpt-image-1", "gpt-image-1-mini" };

        public static readonly string[] VertexAILocationOptions =
        {
            "global", "us-central1", "us-east1", "us-east4", "us-east5",
            "us-south1", "us-west1", "us-west4",
            "northamerica-northeast1", "southamerica-east1",
            "europe-central2", "europe-north1", "europe-southwest1",
            "europe-west1", "europe-west2", "europe-west3", "europe-west4",
            "europe-west6", "europe-west8", "europe-west9",
            "asia-east1", "asia-east2", "asia-northeast1", "asia-northeast3",
            "asia-south1", "asia-southeast1", "australia-southeast1",
            "me-central1", "me-central2", "me-west1",
        };

        public static readonly string[] ProviderDisplayNames =
        {
            "Gemini (Google AI)",
            "OpenAI Compatible (LM Studio etc.)",
            "Claude CLI",
            "Gemini CLI",
            "Clipboard (Manual)",
            "Claude API",
            "OpenAI",
            "DeepSeek",
            "Groq",
            "Ollama",
            "xAI (Grok)",
            "Mistral",
            "Perplexity",
            "Web Browser (Gemini / ChatGPT / Copilot)",
            "Vertex AI",
            "Codex CLI",
        };

        // ─── Model preset arrays ───

        static readonly string[] GeminiModelPresets =
        {
            "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro",
            "gemini-3-flash-preview", "gemini-3.1-pro-preview",
        };

        static readonly string[] ClaudeCliModelPresets = { "", "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5-20251001" };
        static readonly string[] ClaudeCliModelDisplayNames =
        {
            "(CLIデフォルト)",
            "Opus 4.6  (claude-opus-4-6)",
            "Sonnet 4.6  (claude-sonnet-4-6)",
            "Haiku 4.5  (claude-haiku-4-5-20251001)",
        };

        static readonly string[] GeminiCliModelPresets = { "", "gemini-2.5-flash", "gemini-2.5-pro", "gemini-3-flash-preview" };
        static readonly string[] GeminiCliModelDisplayNames =
        {
            "(CLIデフォルト)",
            "Gemini 2.5 Flash  (gemini-2.5-flash)",
            "Gemini 2.5 Pro  (gemini-2.5-pro)",
            "Gemini 3 Flash  (gemini-3-flash-preview)",
        };

        static readonly string[] ClaudeApiModelPresets = { "", "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5-20251001" };
        static readonly string[] ClaudeApiModelDisplayNames =
        {
            "(デフォルト: claude-sonnet-4-6)",
            "Opus 4.6  (claude-opus-4-6)",
            "Sonnet 4.6  (claude-sonnet-4-6)",
            "Haiku 4.5  (claude-haiku-4-5-20251001)",
        };

        static readonly string[] OpenAIModelPresets = { "gpt-5.4", "gpt-4.1", "gpt-4.1-mini", "gpt-4o", "o4-mini", "o3" };
        static readonly string[] OpenAIModelDisplayNames =
        {
            "GPT-5.4  (gpt-5.4)", "GPT-4.1  (gpt-4.1)", "GPT-4.1 mini  (gpt-4.1-mini)",
            "GPT-4o  (gpt-4o)", "o4-mini  (o4-mini)", "o3  (o3)",
        };

        static readonly string[] DeepSeekModelPresets = { "deepseek-chat", "deepseek-reasoner" };
        static readonly string[] DeepSeekModelDisplayNames = { "DeepSeek V3  (deepseek-chat)", "DeepSeek R1  (deepseek-reasoner)" };

        static readonly string[] GroqModelPresets =
        {
            "llama-3.3-70b-versatile", "deepseek-r1-distill-llama-70b",
            "qwen-qwq-32b", "llama-3.1-8b-instant", "mixtral-8x7b-32768",
        };
        static readonly string[] GroqModelDisplayNames =
        {
            "Llama 3.3 70B  (llama-3.3-70b-versatile)",
            "DeepSeek R1 Distill 70B  (deepseek-r1-distill-llama-70b)",
            "QwQ 32B  (qwen-qwq-32b)",
            "Llama 3.1 8B Instant  (llama-3.1-8b-instant)",
            "Mixtral 8x7B  (mixtral-8x7b-32768)",
        };

        static readonly string[] OllamaModelPresets =
        {
            "llama3.3", "llama3.2", "llama3.1", "gemma3:9b",
            "qwen2.5:14b", "phi4", "mistral", "deepseek-r1:14b",
        };
        static readonly string[] OllamaModelDisplayNames =
        {
            "Llama 3.3  (llama3.3)", "Llama 3.2  (llama3.2)", "Llama 3.1  (llama3.1)",
            "Gemma 3 9B  (gemma3:9b)", "Qwen 2.5 14B  (qwen2.5:14b)",
            "Phi-4  (phi4)", "Mistral  (mistral)", "DeepSeek R1 14B  (deepseek-r1:14b)",
        };

        static readonly string[] XaiModelPresets = { "grok-3", "grok-3-fast", "grok-3-mini", "grok-3-mini-fast", "grok-2-1212" };
        static readonly string[] XaiModelDisplayNames =
        {
            "Grok 3  (grok-3)", "Grok 3 Fast  (grok-3-fast)",
            "Grok 3 Mini  (grok-3-mini)", "Grok 3 Mini Fast  (grok-3-mini-fast)",
            "Grok 2  (grok-2-1212)",
        };

        static readonly string[] MistralModelPresets =
        {
            "mistral-large-latest", "mistral-medium-latest", "mistral-small-latest",
            "codestral-latest", "pixtral-large-latest",
        };
        static readonly string[] MistralModelDisplayNames =
        {
            "Mistral Large  (mistral-large-latest)", "Mistral Medium  (mistral-medium-latest)",
            "Mistral Small  (mistral-small-latest)", "Codestral  (codestral-latest)",
            "Pixtral Large  (pixtral-large-latest)",
        };

        static readonly string[] PerplexityModelPresets =
        {
            "sonar-pro", "sonar", "sonar-reasoning-pro", "sonar-reasoning", "sonar-deep-research",
        };
        static readonly string[] PerplexityModelDisplayNames =
        {
            "Sonar Pro  (sonar-pro)", "Sonar  (sonar)",
            "Sonar Reasoning Pro  (sonar-reasoning-pro)",
            "Sonar Reasoning  (sonar-reasoning)",
            "Sonar Deep Research  (sonar-deep-research)",
        };

        static readonly string[] CodexCliModelPresets = { "", "gpt-5.3-codex", "gpt-5.2-codex", "gpt-5.1-codex-max", "gpt-5.2", "gpt-5.1-codex-mini", "codex-mini", "o4-mini", "o3", "gpt-4.1", "gpt-4.1-mini" };
        static readonly string[] CodexCliModelDisplayNames =
        {
            "(CLIデフォルト)",
            "GPT-5.3 Codex  (gpt-5.3-codex)",
            "GPT-5.2 Codex  (gpt-5.2-codex)",
            "GPT-5.1 Codex Max  (gpt-5.1-codex-max)",
            "GPT-5.2  (gpt-5.2)",
            "GPT-5.1 Codex Mini  (gpt-5.1-codex-mini)",
            "Codex Mini  (codex-mini)",
            "o4-mini  (o4-mini)",
            "o3  (o3)",
            "GPT-4.1  (gpt-4.1)",
            "GPT-4.1 mini  (gpt-4.1-mini)",
        };

        // ─── Descriptor table ───

        private static Dictionary<LLMProviderType, ProviderDescriptor> _descriptors;

        public static IReadOnlyDictionary<LLMProviderType, ProviderDescriptor> Descriptors
        {
            get
            {
                if (_descriptors == null) BuildDescriptors();
                return _descriptors;
            }
        }

        public static ProviderDescriptor Get(LLMProviderType type) => Descriptors[type];

        private static void BuildDescriptors()
        {
            _descriptors = new Dictionary<LLMProviderType, ProviderDescriptor>
            {
                [LLMProviderType.Gemini] = new ProviderDescriptor
                {
                    DisplayName = "Gemini (Google AI)", ShortName = "Gemini",
                    SettingsKind = ProviderSettingsKind.Gemini,
                    ModelPresets = GeminiModelPresets, ModelDisplayNames = GeminiModelPresets,
                    DefaultModel = "gemini-2.5-flash",
                    ThinkingMode = ThinkingMode.Budget,
                    ThinkingHintKey = "Gemini 2.5 系モデルで対応",
                    SupportsModelSelection = true,
                    SectionTitle = "Gemini",
                    DescriptionKey = "Google AI Studio 経由で Gemini モデルに接続します。",
                    SettingsKeyApiKey = "UnityAgent_ApiKey",
                    SettingsKeyModelName = "UnityAgent_ModelName",
                },
                [LLMProviderType.OpenAI_Compatible] = new ProviderDescriptor
                {
                    DisplayName = "OpenAI Compatible (LM Studio etc.)", ShortName = "OpenAI互換",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleUrl,
                    ModelPresets = null, ModelDisplayNames = null,
                    DefaultModel = "local-model",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "モデルが対応していれば適用",
                    DefaultBaseUrl = "http://localhost:1234/v1",
                    SupportsModelSelection = false,
                    SectionTitle = "OpenAI互換",
                    DescriptionKey = "OpenAI互換APIを提供するサービスに接続します。LM Studio、LocalAI 等に対応。",
                    SettingsKeyApiKey = "UnityAgent_CompatibleApiKey",
                    SettingsKeyModelName = "UnityAgent_CompatibleModelName",
                    SettingsKeyBaseUrl = "UnityAgent_BaseUrl",
                },
                [LLMProviderType.Claude_CLI] = new ProviderDescriptor
                {
                    DisplayName = "Claude CLI", ShortName = "Claude CLI",
                    SettingsKind = ProviderSettingsKind.CliProvider,
                    ModelPresets = ClaudeCliModelPresets, ModelDisplayNames = ClaudeCliModelDisplayNames,
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "Effort レベルとして適用",
                    SupportsModelSelection = true,
                    SectionTitle = "Claude CLI",
                    DescriptionKey = "ローカルにインストールされた Claude CLI を使用します。",
                    SettingsKeyCliPath = "UnityAgent_ClaudeCliPath",
                    SettingsKeyModelName = "UnityAgent_ClaudeModelName",
                },
                [LLMProviderType.Gemini_CLI] = new ProviderDescriptor
                {
                    DisplayName = "Gemini CLI", ShortName = "Gemini CLI",
                    SettingsKind = ProviderSettingsKind.CliProvider,
                    ModelPresets = GeminiCliModelPresets, ModelDisplayNames = GeminiCliModelDisplayNames,
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Budget,
                    ThinkingHintKey = "Gemini 2.5+ で適用 (settings.json 経由)",
                    SupportsModelSelection = true,
                    SectionTitle = "Gemini CLI",
                    DescriptionKey = "ローカルにインストールされた Gemini CLI を使用します。",
                    SettingsKeyCliPath = "UnityAgent_GeminiCliPath",
                    SettingsKeyModelName = "UnityAgent_GeminiCliModelName",
                },
                [LLMProviderType.Clipboard] = new ProviderDescriptor
                {
                    DisplayName = "Clipboard (Manual)", ShortName = "Clipboard",
                    SettingsKind = ProviderSettingsKind.Clipboard,
                    ModelPresets = null, ModelDisplayNames = null,
                    ThinkingMode = ThinkingMode.None,
                    SupportsModelSelection = false,
                    SectionTitle = "クリップボード",
                    DescriptionKey = "APIを使わずに手動でAIとやり取りするモードです。",
                },
                [LLMProviderType.Claude_API] = new ProviderDescriptor
                {
                    DisplayName = "Claude API", ShortName = "Claude API",
                    SettingsKind = ProviderSettingsKind.ClaudeApi,
                    ModelPresets = ClaudeApiModelPresets, ModelDisplayNames = ClaudeApiModelDisplayNames,
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Budget,
                    ThinkingHintKey = "Claude 3.5 Sonnet 以降で対応",
                    SupportsModelSelection = true,
                    SectionTitle = "Claude API",
                    DescriptionKey = "Anthropic API に直接接続します。API キーは console.anthropic.com から取得できます。",
                    SettingsKeyApiKey = "UnityAgent_ClaudeApiKey",
                    SettingsKeyModelName = "UnityAgent_ClaudeApiModelName",
                },
                [LLMProviderType.OpenAI] = new ProviderDescriptor
                {
                    DisplayName = "OpenAI", ShortName = "OpenAI",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = OpenAIModelPresets, ModelDisplayNames = OpenAIModelDisplayNames,
                    DefaultModel = "gpt-4.1",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "o 系モデル (o3, o4-mini 等) で適用",
                    DefaultBaseUrl = "https://api.openai.com/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "OpenAI",
                    DescriptionKey = "OpenAI API に接続します。GPT-4o 等のモデルが利用可能です。",
                    SettingsKeyApiKey = "UnityAgent_OpenAIApiKey",
                    SettingsKeyModelName = "UnityAgent_OpenAIModelName",
                },
                [LLMProviderType.DeepSeek] = new ProviderDescriptor
                {
                    DisplayName = "DeepSeek", ShortName = "DeepSeek",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = DeepSeekModelPresets, ModelDisplayNames = DeepSeekModelDisplayNames,
                    DefaultModel = "deepseek-chat",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "deepseek-reasoner で適用",
                    DefaultBaseUrl = "https://api.deepseek.com/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "DeepSeek",
                    DescriptionKey = "DeepSeek API に接続します。DeepSeek V3 / R1 が利用可能です。",
                    SettingsKeyApiKey = "UnityAgent_DeepSeekApiKey",
                    SettingsKeyModelName = "UnityAgent_DeepSeekModelName",
                },
                [LLMProviderType.Groq] = new ProviderDescriptor
                {
                    DisplayName = "Groq", ShortName = "Groq",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = GroqModelPresets, ModelDisplayNames = GroqModelDisplayNames,
                    DefaultModel = "llama-3.3-70b-versatile",
                    ThinkingMode = ThinkingMode.None,
                    DefaultBaseUrl = "https://api.groq.com/openai/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "Groq",
                    DescriptionKey = "Groq API に接続します。高速推論が特徴です。",
                    SettingsKeyApiKey = "UnityAgent_GroqApiKey",
                    SettingsKeyModelName = "UnityAgent_GroqModelName",
                },
                [LLMProviderType.Ollama] = new ProviderDescriptor
                {
                    DisplayName = "Ollama", ShortName = "Ollama",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleUrl,
                    ModelPresets = OllamaModelPresets, ModelDisplayNames = OllamaModelDisplayNames,
                    DefaultModel = "llama3.3",
                    ThinkingMode = ThinkingMode.None,
                    DefaultBaseUrl = "http://localhost:11434/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "Ollama",
                    DescriptionKey = "ローカルで動作する Ollama サーバーに接続します。API キー不要です。",
                    SettingsKeyApiKey = null,
                    SettingsKeyModelName = "UnityAgent_OllamaModelName",
                    SettingsKeyBaseUrl = "UnityAgent_OllamaBaseUrl",
                },
                [LLMProviderType.xAI_Grok] = new ProviderDescriptor
                {
                    DisplayName = "xAI (Grok)", ShortName = "Grok",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = XaiModelPresets, ModelDisplayNames = XaiModelDisplayNames,
                    DefaultModel = "grok-3",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "Grok 3 Mini / Grok 4 で適用",
                    DefaultBaseUrl = "https://api.x.ai/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "xAI (Grok)",
                    DescriptionKey = "xAI API に接続します。Grok モデルが利用可能です。",
                    SettingsKeyApiKey = "UnityAgent_XaiApiKey",
                    SettingsKeyModelName = "UnityAgent_XaiModelName",
                },
                [LLMProviderType.Mistral] = new ProviderDescriptor
                {
                    DisplayName = "Mistral", ShortName = "Mistral",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = MistralModelPresets, ModelDisplayNames = MistralModelDisplayNames,
                    DefaultModel = "mistral-large-latest",
                    ThinkingMode = ThinkingMode.None,
                    DefaultBaseUrl = "https://api.mistral.ai/v1",
                    SupportsModelSelection = true,
                    SectionTitle = "Mistral",
                    DescriptionKey = "Mistral API に接続します。",
                    SettingsKeyApiKey = "UnityAgent_MistralApiKey",
                    SettingsKeyModelName = "UnityAgent_MistralModelName",
                },
                [LLMProviderType.Perplexity] = new ProviderDescriptor
                {
                    DisplayName = "Perplexity", ShortName = "Perplexity",
                    SettingsKind = ProviderSettingsKind.OpenAICompatibleApiKey,
                    ModelPresets = PerplexityModelPresets, ModelDisplayNames = PerplexityModelDisplayNames,
                    DefaultModel = "sonar-pro",
                    ThinkingMode = ThinkingMode.None,
                    DefaultBaseUrl = "https://api.perplexity.ai",
                    SupportsModelSelection = true,
                    SectionTitle = "Perplexity",
                    DescriptionKey = "Perplexity API に接続します。検索拡張生成が特徴です。",
                    SettingsKeyApiKey = "UnityAgent_PerplexityApiKey",
                    SettingsKeyModelName = "UnityAgent_PerplexityModelName",
                },
                [LLMProviderType.Gemini_Web] = new ProviderDescriptor
                {
                    DisplayName = "Web Browser (Gemini / ChatGPT / Copilot)", ShortName = "Web Browser",
                    SettingsKind = ProviderSettingsKind.BrowserBridge,
                    ModelPresets = null, ModelDisplayNames = null,
                    ThinkingMode = ThinkingMode.None,
                    SupportsModelSelection = false,
                    SectionTitle = "Web Browser",
                    DescriptionKey = "Chrome 拡張機能経由で gemini.google.com / chatgpt.com / copilot.microsoft.com と連携します。API キー不要です。",
                },
                [LLMProviderType.Vertex_AI] = new ProviderDescriptor
                {
                    DisplayName = "Vertex AI", ShortName = "Vertex AI",
                    SettingsKind = ProviderSettingsKind.VertexAI,
                    ModelPresets = GeminiModelPresets, ModelDisplayNames = GeminiModelPresets,
                    DefaultModel = "gemini-2.5-flash",
                    ThinkingMode = ThinkingMode.Budget,
                    ThinkingHintKey = "Gemini 2.5 系モデルで対応",
                    SupportsModelSelection = true,
                    SectionTitle = "Vertex AI",
                    DescriptionKey = "Google Cloud Vertex AI 経由で Gemini モデルに接続します。",
                    SettingsKeyApiKey = "UnityAgent_VertexAIApiKey",
                    SettingsKeyModelName = "UnityAgent_VertexAIModelName",
                },
                [LLMProviderType.Codex_CLI] = new ProviderDescriptor
                {
                    DisplayName = "Codex CLI", ShortName = "Codex CLI",
                    SettingsKind = ProviderSettingsKind.CliProvider,
                    ModelPresets = CodexCliModelPresets, ModelDisplayNames = CodexCliModelDisplayNames,
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "Responses API 対応モデルで適用",
                    SupportsModelSelection = true,
                    SectionTitle = "Codex CLI",
                    DescriptionKey = "ローカルにインストールされた Codex CLI を使用します。",
                    SettingsKeyCliPath = "UnityAgent_CodexCliPath",
                    SettingsKeyModelName = "UnityAgent_CodexCliModelName",
                },
            };
        }

        // ─── Config load/save ───

        public static Dictionary<LLMProviderType, ProviderConfig> LoadAllConfigs()
        {
            var configs = new Dictionary<LLMProviderType, ProviderConfig>();
            foreach (LLMProviderType type in Enum.GetValues(typeof(LLMProviderType)))
            {
                var desc = Get(type);
                var cfg = new ProviderConfig();

                if (desc.SettingsKeyApiKey != null)
                {
                    cfg.ApiKey = SettingsStore.GetString(desc.SettingsKeyApiKey, "");
                    // Migration: these providers used to share "UnityAgent_ApiKey"
                    if (string.IsNullOrEmpty(cfg.ApiKey) &&
                        (type == LLMProviderType.OpenAI_Compatible || type == LLMProviderType.Vertex_AI))
                        cfg.ApiKey = SettingsStore.GetString("UnityAgent_ApiKey", "");
                }
                if (desc.SettingsKeyModelName != null)
                {
                    cfg.ModelName = SettingsStore.GetString(desc.SettingsKeyModelName, desc.DefaultModel ?? "");
                    // Migration: these providers used to share "UnityAgent_ModelName"
                    if (cfg.ModelName == (desc.DefaultModel ?? "") &&
                        (type == LLMProviderType.OpenAI_Compatible || type == LLMProviderType.Vertex_AI))
                    {
                        string old = SettingsStore.GetString("UnityAgent_ModelName", "");
                        if (!string.IsNullOrEmpty(old)) cfg.ModelName = old;
                    }
                }
                if (desc.SettingsKeyBaseUrl != null)
                    cfg.BaseUrl = SettingsStore.GetString(desc.SettingsKeyBaseUrl, desc.DefaultBaseUrl ?? "");
                if (desc.SettingsKeyCliPath != null)
                    cfg.CliPath = SettingsStore.GetString(desc.SettingsKeyCliPath, GetDefaultCliPath(type));

                // Gemini (Google AI) specific fields
                if (type == LLMProviderType.Gemini)
                {
                    cfg.GeminiMode = (GeminiConnectionMode)SettingsStore.GetInt("UnityAgent_GeminiMode", 0);
                    cfg.ApiVersion = SettingsStore.GetString("UnityAgent_ApiVersion", "v1beta");
                    cfg.CustomEndpoint = SettingsStore.GetString("UnityAgent_CustomEndpoint", "");
                    cfg.GeminiGoogleSearch = SettingsStore.GetBool("UnityAgent_GeminiGoogleSearch", false);
                    cfg.GeminiCodeExecution = SettingsStore.GetBool("UnityAgent_GeminiCodeExecution", false);
                    cfg.GeminiUrlContext = SettingsStore.GetBool("UnityAgent_GeminiUrlContext", false);
                    cfg.GeminiSafetyLevel = SettingsStore.GetInt("UnityAgent_GeminiSafetyLevel", 0);
                    cfg.GeminiMediaResolution = SettingsStore.GetInt("UnityAgent_GeminiMediaResolution", 0);
                }

                // Vertex AI specific fields
                if (type == LLMProviderType.Vertex_AI)
                {
                    cfg.ApiVersion = SettingsStore.GetString("UnityAgent_VertexAI_ApiVersion", "v1beta1");
                    cfg.ProjectId = SettingsStore.GetString("UnityAgent_ProjectId", "");
                    cfg.Location = SettingsStore.GetString("UnityAgent_Location", "us-central1");
                    cfg.GeminiGoogleSearch = SettingsStore.GetBool("UnityAgent_VertexAI_GoogleSearch", false);
                    cfg.GeminiCodeExecution = SettingsStore.GetBool("UnityAgent_VertexAI_CodeExecution", false);
                    cfg.GeminiUrlContext = SettingsStore.GetBool("UnityAgent_VertexAI_UrlContext", false);
                    cfg.GeminiSafetyLevel = SettingsStore.GetInt("UnityAgent_VertexAI_SafetyLevel", 0);
                    cfg.GeminiMediaResolution = SettingsStore.GetInt("UnityAgent_VertexAI_MediaResolution", 0);
                }

                // BrowserBridge port
                if (type == LLMProviderType.Gemini_Web)
                    cfg.Port = SettingsStore.GetInt("UnityAgent_BrowserBridgePort", 6090);

                // Per-provider max context tokens (0 = auto)
                cfg.MaxContextTokens = SettingsStore.GetInt($"UnityAgent_{type}_MaxContextTokens", 0);

                // UseCustomModel derived flag
                if (desc.ModelPresets != null)
                    cfg.UseCustomModel = Array.IndexOf(desc.ModelPresets, cfg.ModelName) < 0;

                configs[type] = cfg;
            }
            return configs;
        }

        public static void SaveAllConfigs(Dictionary<LLMProviderType, ProviderConfig> configs,
            LLMProviderType activeType = LLMProviderType.Gemini)
        {
            // Save active provider first, then others.
            // Track written keys to prevent shared-key overwrite.
            var written = new HashSet<string>();
            SaveSingleConfig(activeType, configs[activeType], written);
            foreach (var kv in configs)
            {
                if (kv.Key != activeType)
                    SaveSingleConfig(kv.Key, kv.Value, written);
            }
        }

        private static void SaveSingleConfig(LLMProviderType type, ProviderConfig cfg, HashSet<string> written)
        {
            var desc = Get(type);

            if (desc.SettingsKeyApiKey != null && written.Add(desc.SettingsKeyApiKey))
                SettingsStore.SetString(desc.SettingsKeyApiKey, cfg.ApiKey);
            if (desc.SettingsKeyModelName != null && written.Add(desc.SettingsKeyModelName))
                SettingsStore.SetString(desc.SettingsKeyModelName, cfg.ModelName);
            if (desc.SettingsKeyBaseUrl != null && written.Add(desc.SettingsKeyBaseUrl))
                SettingsStore.SetString(desc.SettingsKeyBaseUrl, cfg.BaseUrl);
            if (desc.SettingsKeyCliPath != null && written.Add(desc.SettingsKeyCliPath))
                SettingsStore.SetString(desc.SettingsKeyCliPath, cfg.CliPath);

            // Gemini (Google AI) specific
            if (type == LLMProviderType.Gemini)
            {
                SettingsStore.SetInt("UnityAgent_GeminiMode", (int)cfg.GeminiMode);
                SettingsStore.SetString("UnityAgent_ApiVersion", cfg.ApiVersion);
                SettingsStore.SetString("UnityAgent_CustomEndpoint", cfg.CustomEndpoint);
                SettingsStore.SetBool("UnityAgent_GeminiGoogleSearch", cfg.GeminiGoogleSearch);
                SettingsStore.SetBool("UnityAgent_GeminiCodeExecution", cfg.GeminiCodeExecution);
                SettingsStore.SetBool("UnityAgent_GeminiUrlContext", cfg.GeminiUrlContext);
                SettingsStore.SetInt("UnityAgent_GeminiSafetyLevel", cfg.GeminiSafetyLevel);
                SettingsStore.SetInt("UnityAgent_GeminiMediaResolution", cfg.GeminiMediaResolution);
            }

            // Vertex AI specific
            if (type == LLMProviderType.Vertex_AI)
            {
                SettingsStore.SetString("UnityAgent_VertexAI_ApiVersion", cfg.ApiVersion);
                SettingsStore.SetString("UnityAgent_ProjectId", cfg.ProjectId);
                SettingsStore.SetString("UnityAgent_Location", cfg.Location);
                SettingsStore.SetBool("UnityAgent_VertexAI_GoogleSearch", cfg.GeminiGoogleSearch);
                SettingsStore.SetBool("UnityAgent_VertexAI_CodeExecution", cfg.GeminiCodeExecution);
                SettingsStore.SetBool("UnityAgent_VertexAI_UrlContext", cfg.GeminiUrlContext);
                SettingsStore.SetInt("UnityAgent_VertexAI_SafetyLevel", cfg.GeminiSafetyLevel);
                SettingsStore.SetInt("UnityAgent_VertexAI_MediaResolution", cfg.GeminiMediaResolution);
            }

            if (type == LLMProviderType.Gemini_Web)
                SettingsStore.SetInt("UnityAgent_BrowserBridgePort", cfg.Port);

            SettingsStore.SetInt($"UnityAgent_{type}_MaxContextTokens", cfg.MaxContextTokens);
        }

        // ─── Provider factory ───

        public static ILLMProvider CreateProvider(LLMProviderType type, ProviderConfig cfg,
            bool useThinking, int thinkingBudget, int effortLevel)
        {
            AgentLogger.Info(LogTag.Provider, $"CreateProvider: type={type}, model={cfg.ModelName}, thinking={useThinking}, budget={thinkingBudget}, effort={effortLevel}");
            var features = new GeminiFeatures
            {
                GoogleSearch = cfg.GeminiGoogleSearch,
                CodeExecution = cfg.GeminiCodeExecution,
                UrlContext = cfg.GeminiUrlContext,
                SafetyLevel = cfg.GeminiSafetyLevel,
                MediaResolution = cfg.GeminiMediaResolution,
            };

            switch (type)
            {
                case LLMProviderType.Gemini:
                    return new GeminiProvider(cfg.ApiKey, cfg.GeminiMode, cfg.ModelName,
                        cfg.ApiVersion, useThinking ? thinkingBudget : 0,
                        cfg.CustomEndpoint, cfg.ProjectId, cfg.Location,
                        LLMProviderType.Gemini, useThinking ? effortLevel : -1, features);

                case LLMProviderType.Vertex_AI:
                    return new GeminiProvider(cfg.ApiKey, GeminiConnectionMode.VertexAI_Express, cfg.ModelName,
                        cfg.ApiVersion, useThinking ? thinkingBudget : 0,
                        cfg.CustomEndpoint, cfg.ProjectId, cfg.Location,
                        LLMProviderType.Vertex_AI, useThinking ? effortLevel : -1, features);

                case LLMProviderType.Claude_CLI:
                    return new ClaudeCliProvider(cfg.CliPath, cfg.ModelName,
                        useThinking ? effortLevel : -1, useThinking ? thinkingBudget : 0);

                case LLMProviderType.Gemini_CLI:
                    return new GeminiCliProvider(cfg.CliPath, cfg.ModelName,
                        useThinking ? thinkingBudget : -1);

                case LLMProviderType.Codex_CLI:
                    return new CodexCliProvider(cfg.CliPath, cfg.ModelName,
                        useThinking ? effortLevel : -1);

                case LLMProviderType.Clipboard:
                    return new ClipboardProvider();

                case LLMProviderType.Claude_API:
                    return new ClaudeApiProvider(cfg.ApiKey, cfg.ModelName,
                        useThinking ? thinkingBudget : 0);

                case LLMProviderType.Gemini_Web:
                    return new BrowserBridgeProvider(cfg.Port);

                // OpenAI-compatible providers
                case LLMProviderType.OpenAI:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.openai.com/v1",
                        cfg.ModelName, useThinking ? effortLevel : -1, LLMProviderType.OpenAI);

                case LLMProviderType.DeepSeek:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.deepseek.com/v1",
                        cfg.ModelName, useThinking ? effortLevel : -1, LLMProviderType.DeepSeek);

                case LLMProviderType.Groq:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.groq.com/openai/v1",
                        cfg.ModelName, -1, LLMProviderType.Groq);

                case LLMProviderType.Ollama:
                    return new OpenAICompatibleProvider("ollama", cfg.BaseUrl, cfg.ModelName,
                        -1, LLMProviderType.Ollama);

                case LLMProviderType.xAI_Grok:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.x.ai/v1", cfg.ModelName,
                        useThinking ? effortLevel : -1, LLMProviderType.xAI_Grok);

                case LLMProviderType.Mistral:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.mistral.ai/v1", cfg.ModelName,
                        -1, LLMProviderType.Mistral);

                case LLMProviderType.Perplexity:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.perplexity.ai", cfg.ModelName,
                        -1, LLMProviderType.Perplexity);

                default: // OpenAI_Compatible
                    return new OpenAICompatibleProvider(cfg.ApiKey, cfg.BaseUrl, cfg.ModelName,
                        useThinking ? effortLevel : -1, LLMProviderType.OpenAI_Compatible);
            }
        }

        /// <summary>
        /// 画像生成プロバイダーを作成する。
        /// プロバイダー種別に応じて Gemini / OpenAI プロバイダーを返す。
        /// </summary>
        internal static IImageProvider CreateImageProvider()
        {
            var providerType = (ImageProviderType)SettingsStore.GetInt("UnityAgent_ImageProviderType", 0);

            switch (providerType)
            {
                case ImageProviderType.OpenAI:
                    string oaiKey = SettingsStore.GetString("UnityAgent_OpenAI_ImageApiKey", "");
                    string oaiModel = SettingsStore.GetString("UnityAgent_OpenAI_ImageModelName", "gpt-image-1");
                    string oaiBase = SettingsStore.GetString("UnityAgent_OpenAI_ImageBaseUrl", "https://api.openai.com");
                    return new OpenAIImageProvider(oaiKey, oaiModel, oaiBase);

                default: // Gemini
                    string apiKey = SettingsStore.GetString("UnityAgent_ImageApiKey", "");
                    int connMode = SettingsStore.GetInt("UnityAgent_ImageConnectionMode", 0);
                    string imageModel = SettingsStore.GetString("UnityAgent_ImageModelName", "gemini-2.5-flash-image");
                    string customEndpoint = SettingsStore.GetString("UnityAgent_ImageCustomEndpoint", "");
                    string projectId = SettingsStore.GetString("UnityAgent_ImageProjectId", "");
                    string location = SettingsStore.GetString("UnityAgent_ImageLocation", "us-central1");

                    // Migration: 旧設定 (Gemini LLM の API キー共有) からの移行
                    if (string.IsNullOrEmpty(apiKey))
                        apiKey = SettingsStore.GetString("UnityAgent_ApiKey", "");

                    GeminiConnectionMode mode;
                    switch (connMode)
                    {
                        case 1: mode = GeminiConnectionMode.VertexAI_Express; break;
                        case 2: mode = GeminiConnectionMode.Custom; break;
                        default: mode = GeminiConnectionMode.GoogleAI; break;
                    }

                    return new GeminiImageProvider(apiKey, mode, imageModel, customEndpoint, projectId, location);
            }
        }

        // ─── Helpers ───

        private static string GetDefaultCliPath(LLMProviderType type)
        {
            switch (type)
            {
                case LLMProviderType.Claude_CLI: return "claude";
                case LLMProviderType.Gemini_CLI: return "gemini";
                case LLMProviderType.Codex_CLI: return "codex";
                default: return "";
            }
        }

        /// <summary>
        /// アクティブなモデル表示名を取得。モデル選択不可のプロバイダーは null を返す。
        /// </summary>
        public static string GetActiveModelDisplayName(LLMProviderType type, ProviderConfig cfg)
        {
            var desc = Get(type);
            if (!desc.SupportsModelSelection) return null;

            // CLI providers: empty means default
            if (desc.SettingsKind == ProviderSettingsKind.CliProvider ||
                desc.SettingsKind == ProviderSettingsKind.ClaudeApi)
            {
                return string.IsNullOrEmpty(cfg.ModelName) ? null : cfg.ModelName;
            }

            return cfg.ModelName;
        }
    }
}
