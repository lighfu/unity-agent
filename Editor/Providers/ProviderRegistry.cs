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
    /// 新しいプロバイダーは必ず末尾に追加すること (既存保存値の破壊を防ぐため)。
    /// </summary>
    internal enum LLMProviderType
    {
        Gemini, OpenAI_Compatible, Claude_CLI, Gemini_CLI, Clipboard,
        Claude_API, OpenAI, DeepSeek, Groq, Ollama, xAI_Grok, Mistral, Perplexity,
        Gemini_Web, Vertex_AI, Codex_CLI,
        /// <summary>
        /// 外部 MCP クライアント (Claude Code, Cursor 等) が UnityAgent の会話/ツール実行を
        /// 主導するモード。UnityAgent は LLM を呼ばず、UI 側で対話を受け取るだけ。
        /// </summary>
        MCPServer,
        /// <summary>Antigravity CLI (agy)。Gemini CLI の後継。末尾に追加 (上の注意のとおり)。</summary>
        Antigravity_CLI,
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
        CliProvider,             // Claude CLI, Gemini CLI, Codex CLI, Antigravity CLI
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
        ComfyUI = 2,
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
        // ModelPresets / ModelDisplayNames は BuildDescriptors() の末尾で
        // ModelCapabilityRegistry から算出して埋める（単一の真実源）。
        public string[] ModelPresets;
        public string[] ModelDisplayNames;
        /// <summary>非 null の場合、モデルドロップダウン先頭に空文字 ("") 選択肢をこのラベルで追加する。</summary>
        public string EmptyModelOptionLabel;
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

        /// <summary>
        /// Gemini の画像生成モデル。先頭が既定値。
        /// gemini-2.5-flash-image は 2026-10-02 に提供が終わるので一覧から外した。
        /// 設定に残っている値はカスタムモデル扱いでそのまま使える (各ウィンドウの LoadSettings)。
        /// </summary>
        public static readonly string[] GeminiImageModelPresets =
        {
            "gemini-3.1-flash-image",
            "gemini-3.1-flash-lite-image",
            "gemini-3-pro-image",
        };

        /// <summary>
        /// Google が提供を終えた画像生成モデル。設定に残っていても API は受け付けない。
        /// チャットモデル側の RetiredGeminiPrefixes は画像モデルに当ててはいけないので別に持つ。
        /// </summary>
        public static readonly string[] RetiredGeminiImageModels =
        {
            "gemini-2.5-flash-image",
            "gemini-2.5-flash-image-preview",
            "gemini-3.1-flash-image-preview",
        };

        /// <summary>
        /// generationConfig.imageConfig.aspectRatio に渡せる値。先頭の空文字は「指定しない」。
        /// 公式が受け付ける 14 通りを全部並べ、縦長 → 正方形 → 横長の順にした
        /// (ai.google.dev/gemini-api/docs/generate-content/image-generation)。
        /// </summary>
        public static readonly string[] GeminiImageAspectRatios =
        {
            "",
            "1:8", "1:4", "9:16", "2:3", "3:4", "4:5",
            "1:1",
            "5:4", "4:3", "3:2", "16:9", "21:9", "4:1", "8:1",
        };

        /// <summary>generationConfig.imageConfig.imageSize に渡せる値。先頭の空文字は「指定しない」。</summary>
        public static readonly string[] GeminiImageSizes = { "", "512", "1K", "2K", "4K" };

        /// <summary>
        /// そのモデルが受け付ける imageSize。知らないモデル名なら null を返し、呼び出し側は検証しない。
        /// 知らない名前を弾く作りにすると、Google が新しいモデルを出すたびに UnityAgent の更新を
        /// 待たないとカスタムモデル欄から使えなくなるため。
        /// </summary>
        public static string[] GeminiImageSizesFor(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return null;
            switch (modelId)
            {
                case "gemini-3.1-flash-image": return new[] { "512", "1K", "2K", "4K" };
                case "gemini-3.1-flash-lite-image": return new[] { "1K" };
                case "gemini-3-pro-image": return new[] { "1K", "2K", "4K" };
                default: return null;
            }
        }

        public static readonly string[] ImageProviderDisplayNames = { "Gemini", "OpenAI", "ComfyUI" };

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
            "Gemini CLI (legacy)",
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
            "MCP Server (External Agent)",
            "Antigravity CLI (agy)",
        };

        // ─── 推論の強さ (effort) ───
        //
        // 強さの設定 (UnityAgent_EffortLevel) は全プロバイダー共通の 1 つで、値はこの配列の添字。
        // 渡し方だけがプロバイダーごとに違う (Codex CLI は -c model_reasoning_effort、
        // Claude CLI / agy は --effort、OpenAI 互換は reasoning_effort、Gemini は thinkingLevel)。

        /// <summary>設定 UI に並べる推論の強さ。添字が UnityAgent_EffortLevel の値。</summary>
        public static readonly string[] EffortLevelLabels = { "Low", "Medium", "High", "xHigh" };

        /// <summary>
        /// 未設定のときの推論の強さ。medium は Codex CLI と OpenAI の reasoning_effort の既定でもある。
        /// </summary>
        public const int DefaultEffortLevel = 1;

        /// <summary>
        /// そのプロバイダーが受け付ける最大の添字。xhigh を持つのは今のところ Codex CLI だけで、
        /// 他は low / medium / high の 3 段階しかない。
        /// </summary>
        public static int MaxEffortLevel(LLMProviderType type)
            => type == LLMProviderType.Codex_CLI ? 3 : 2;

        /// <summary>
        /// モデルを選んでいない (CLI 側が選ぶ) ときに、能力の判定に使うモデル名。
        ///
        /// 空文字のまま ModelCapabilityRegistry.GetCapability に渡すと「不明なモデル」の
        /// 既定値 (128K / 思考モード非対応) が返る。その結果、コンテキストの上限が実態より
        /// 大幅に小さく表示され、Effort の UI も出ないまま使うことになる。
        /// CLI が実際に何を選ぶかは分からないので、その CLI の現行の代表的なモデルを当てる。
        /// </summary>
        public static string RepresentativeModel(LLMProviderType type)
        {
            switch (type)
            {
                case LLMProviderType.Claude_API:
                case LLMProviderType.Claude_CLI:      return "claude-sonnet-4-6";
                case LLMProviderType.Gemini_CLI:      return "gemini-2.5-flash";
                case LLMProviderType.Codex_CLI:       return "gpt-5.3-codex";
                // agy は系列名だけ渡して --effort で強さを選ぶ使い方もできるので、
                // 強さが中間の行を代表にする (どの行も実モデルは同じで上限も同じ)。
                case LLMProviderType.Antigravity_CLI: return "gemini-3.8-flash-medium";
                // 残りは descriptor の既定モデルが正しい (Gemini / Vertex AI など)。
                default:                              return Get(type).DefaultModel ?? "";
            }
        }

        // ─── Model preset arrays ───
        //
        // モデルのプリセット / 表示名は ModelCapabilityRegistry を単一の真実源とし、
        // BuildDescriptors() の末尾で各 descriptor.ModelPresets / .ModelDisplayNames に
        // 算出して埋める。ここに静的配列を持たせると二重管理になりズレるため置かない。
        // モデルの追加・変更は ModelCapabilities.cs の BuildStaticModels() で行うこと。

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
                    DefaultModel = "gemini-3.5-flash",
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
                    EmptyModelOptionLabel = "(CLIデフォルト)",
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
                    DisplayName = "Gemini CLI (legacy)", ShortName = "Gemini CLI",
                    SettingsKind = ProviderSettingsKind.CliProvider,
                    EmptyModelOptionLabel = "(CLIデフォルト)",
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Budget,
                    ThinkingHintKey = "Gemini 2.5+ で適用 (settings.json 経由)",
                    SupportsModelSelection = true,
                    SectionTitle = "Gemini CLI",
                    DescriptionKey = "ローカルにインストールされた Gemini CLI を使用します。Google は 2026-06-18 に個人アカウント (無料枠・Google AI Pro / Ultra) での Gemini CLI の提供を終了しました。個人アカウントなら Antigravity CLI (agy) を選んでください。",
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
                    EmptyModelOptionLabel = "(デフォルト: claude-sonnet-4-6)",
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
                    DefaultModel = "gemini-3.5-flash",
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
                    EmptyModelOptionLabel = "(CLIデフォルト)",
                    // 既定モデルは CLI に委ねる。Codex CLI はサインインしているアカウントで使える
                    // モデルを自分で選ぶので、UnityAgent 側で固定すると新しいモデルが出るたびに
                    // 設定を直すことになる。
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "-c model_reasoning_effort (low / medium / high / xhigh) として適用",
                    SupportsModelSelection = true,
                    SectionTitle = "Codex CLI",
                    DescriptionKey = "ローカルにインストールされた Codex CLI を使用します。",
                    SettingsKeyCliPath = "UnityAgent_CodexCliPath",
                    SettingsKeyModelName = "UnityAgent_CodexCliModelName",
                },
                [LLMProviderType.MCPServer] = new ProviderDescriptor
                {
                    DisplayName = "MCP Server (External Agent)", ShortName = "MCP Server",
                    SettingsKind = ProviderSettingsKind.Clipboard, // 設定 UI は不要だが互換のため Clipboard kind を流用
                    ModelPresets = null, ModelDisplayNames = null,
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.None,
                    SupportsModelSelection = false,
                    SectionTitle = "MCP Server",
                    DescriptionKey = "外部エージェント (Claude Code, Cursor 等) が MCP 経由で UnityAgent を操作します。UnityAgent 側のチャット入力は無効化されますが、メッシュ選択などの UI インタラクションは外部エージェントからの要求に応じて引き続き動作します。",
                },
                [LLMProviderType.Antigravity_CLI] = new ProviderDescriptor
                {
                    DisplayName = "Antigravity CLI (agy)", ShortName = "Antigravity CLI",
                    SettingsKind = ProviderSettingsKind.CliProvider,
                    EmptyModelOptionLabel = "(CLIデフォルト)",
                    DefaultModel = "",
                    ThinkingMode = ThinkingMode.Effort,
                    ThinkingHintKey = "--effort (low / medium / high) として適用。-high などの付いたモデルでは使わない",
                    SupportsModelSelection = true,
                    SectionTitle = "Antigravity CLI",
                    DescriptionKey = "ローカルにインストールされた Antigravity CLI (agy) を使用します。Gemini CLI の後継で、Google アカウントでログインして使います。",
                    SettingsKeyCliPath = "UnityAgent_AntigravityCliPath",
                    SettingsKeyModelName = "UnityAgent_AntigravityCliModelName",
                },
            };

            ApplyModelPresets();

            // 既定モデルの健全性チェック（単一真実源化後に残る唯一のドリフト面）。
            foreach (var kv in _descriptors)
            {
                var desc = kv.Value;
                if (string.IsNullOrEmpty(desc.DefaultModel)) continue;
                var cap = ModelCapabilityRegistry.GetRegistered(desc.DefaultModel);
                if (cap == null)
                    Debug.LogWarning($"[UnityAgent] プロバイダー '{desc.DisplayName}' の既定モデル '{desc.DefaultModel}' が ModelCapabilityRegistry に未登録です。");
                else if (cap.IsDeprecated)
                    Debug.LogWarning($"[UnityAgent] プロバイダー '{desc.DisplayName}' の既定モデル '{desc.DefaultModel}' は deprecated です。");
            }
        }

        /// <summary>
        /// モデルプリセット / 表示名は ModelCapabilityRegistry を単一の真実源として算出する。
        /// *ModelPresets[] / *ModelDisplayNames[] の二重管理を排除するための要。
        /// </summary>
        static void ApplyModelPresets()
        {
            foreach (var kv in _descriptors)
            {
                var desc = kv.Value;
                var (ids, labels) = ModelCapabilityRegistry.GetDropdownModels(kv.Key);
                if (ids.Length == 0) continue; // モデル選択を持たないプロバイダーは null のまま

                if (desc.EmptyModelOptionLabel != null)
                {
                    ids = PrependItem("", ids);
                    labels = PrependItem(desc.EmptyModelOptionLabel, labels);
                }
                desc.ModelPresets = ids;
                desc.ModelDisplayNames = labels;
            }
        }

        /// <summary>
        /// ドロップダウンの選択肢を作り直す。descriptor は初回に一度だけ構築されてキャッシュされるため、
        /// models.list で動的モデルを取得した後はこれを呼ばないと選択肢が増えない。
        /// </summary>
        public static void RefreshModelPresets()
        {
            if (_descriptors == null) { BuildDescriptors(); return; }
            ApplyModelPresets();
        }

        static string[] PrependItem(string item, string[] arr)
        {
            var result = new string[arr.Length + 1];
            result[0] = item;
            Array.Copy(arr, 0, result, 1, arr.Length);
            return result;
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

        /// <summary>
        /// そのプロバイダーに渡す推論の強さ。思考モードが無効なら -1 (送らない)。
        ///
        /// 強さの設定は全プロバイダー共通なので、Codex CLI で xhigh を選んだまま別のプロバイダーに
        /// 切り替えると、そのプロバイダーにとっては範囲外の値が渡る。各プロバイダーは範囲外を
        /// 「送らない」と解釈するため、丸めずに渡すと強さを上げたつもりが思考オフ相当になる。
        /// </summary>
        private static int EffortFor(LLMProviderType type, bool useThinking, int effortLevel)
        {
            if (!useThinking || effortLevel < 0) return -1;
            int max = MaxEffortLevel(type);
            return effortLevel > max ? max : effortLevel;
        }

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
                        LLMProviderType.Gemini, EffortFor(type, useThinking, effortLevel), features);

                case LLMProviderType.Vertex_AI:
                    return new GeminiProvider(cfg.ApiKey, GeminiConnectionMode.VertexAI_Express, cfg.ModelName,
                        cfg.ApiVersion, useThinking ? thinkingBudget : 0,
                        cfg.CustomEndpoint, cfg.ProjectId, cfg.Location,
                        LLMProviderType.Vertex_AI, EffortFor(type, useThinking, effortLevel), features);

                case LLMProviderType.Claude_CLI:
                    return new ClaudeCliProvider(cfg.CliPath, cfg.ModelName,
                        EffortFor(type, useThinking, effortLevel), useThinking ? thinkingBudget : 0);

                case LLMProviderType.Gemini_CLI:
                    return new GeminiCliProvider(cfg.CliPath, cfg.ModelName,
                        useThinking ? thinkingBudget : -1);

                case LLMProviderType.Codex_CLI:
                    return new CodexCliProvider(cfg.CliPath, cfg.ModelName,
                        EffortFor(type, useThinking, effortLevel));

                case LLMProviderType.Antigravity_CLI:
                    return new AntigravityCliProvider(cfg.CliPath, cfg.ModelName,
                        EffortFor(type, useThinking, effortLevel));

                case LLMProviderType.Clipboard:
                    return new ClipboardProvider();

                case LLMProviderType.MCPServer:
                    return new MCPServerProvider();

                case LLMProviderType.Claude_API:
                    return new ClaudeApiProvider(cfg.ApiKey, cfg.ModelName,
                        useThinking ? thinkingBudget : 0);

                case LLMProviderType.Gemini_Web:
                    return new BrowserBridgeProvider(cfg.Port);

                // OpenAI-compatible providers
                case LLMProviderType.OpenAI:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.openai.com/v1",
                        cfg.ModelName, EffortFor(type, useThinking, effortLevel), LLMProviderType.OpenAI);

                case LLMProviderType.DeepSeek:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.deepseek.com/v1",
                        cfg.ModelName, EffortFor(type, useThinking, effortLevel), LLMProviderType.DeepSeek);

                case LLMProviderType.Groq:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.groq.com/openai/v1",
                        cfg.ModelName, -1, LLMProviderType.Groq);

                case LLMProviderType.Ollama:
                    return new OpenAICompatibleProvider("ollama", cfg.BaseUrl, cfg.ModelName,
                        -1, LLMProviderType.Ollama);

                case LLMProviderType.xAI_Grok:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.x.ai/v1", cfg.ModelName,
                        EffortFor(type, useThinking, effortLevel), LLMProviderType.xAI_Grok);

                case LLMProviderType.Mistral:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.mistral.ai/v1", cfg.ModelName,
                        -1, LLMProviderType.Mistral);

                case LLMProviderType.Perplexity:
                    return new OpenAICompatibleProvider(cfg.ApiKey, "https://api.perplexity.ai", cfg.ModelName,
                        -1, LLMProviderType.Perplexity);

                default: // OpenAI_Compatible
                    return new OpenAICompatibleProvider(cfg.ApiKey, cfg.BaseUrl, cfg.ModelName,
                        EffortFor(type, useThinking, effortLevel), LLMProviderType.OpenAI_Compatible);
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

                case ImageProviderType.ComfyUI:
                    string cfBase = SettingsStore.GetString("UnityAgent_ComfyUI_BaseUrl", "http://127.0.0.1:8188");
                    string cfWorkflow = SettingsStore.GetString("UnityAgent_ComfyUI_WorkflowJson", "");
                    string cfCkpt = SettingsStore.GetString("UnityAgent_ComfyUI_Ckpt", "v1-5-pruned-emaonly.safetensors");
                    string cfNeg = SettingsStore.GetString("UnityAgent_ComfyUI_Negative", "text, watermark, lowres, bad anatomy, blurry");
                    float cfDenoise = ParseFloat(SettingsStore.GetString("UnityAgent_ComfyUI_Denoise", "0.75"), 0.75f);
                    int cfSteps = ParseInt(SettingsStore.GetString("UnityAgent_ComfyUI_Steps", "20"), 20);
                    float cfCfg = ParseFloat(SettingsStore.GetString("UnityAgent_ComfyUI_Cfg", "8"), 8f);
                    string cfSampler = SettingsStore.GetString("UnityAgent_ComfyUI_Sampler", "euler");
                    string cfScheduler = SettingsStore.GetString("UnityAgent_ComfyUI_Scheduler", "normal");
                    // ComfyUI seed は 0..2^64-1。long に収まらない再現シードを毎回ランダムに化けさせないため ulong でパース。
                    string cfSeedStr = (SettingsStore.GetString("UnityAgent_ComfyUI_Seed", "-1") ?? "").Trim();
                    ulong cfSeedFixed = 0;
                    bool cfSeedRandom = cfSeedStr.Length == 0 || cfSeedStr == "-1"
                        || !ulong.TryParse(cfSeedStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out cfSeedFixed);
                    return new ComfyUIImageProvider(cfBase, cfWorkflow, cfCkpt, cfNeg, cfDenoise, cfSteps, cfCfg, cfSampler, cfScheduler, cfSeedRandom, cfSeedFixed);

                default: // Gemini
                    string apiKey = SettingsStore.GetString("UnityAgent_ImageApiKey", "");
                    int connMode = SettingsStore.GetInt("UnityAgent_ImageConnectionMode", 0);
                    string imageModel = SettingsStore.GetString("UnityAgent_ImageModelName", "gemini-3.1-flash-image");
                    string customEndpoint = SettingsStore.GetString("UnityAgent_ImageCustomEndpoint", "");
                    string projectId = SettingsStore.GetString("UnityAgent_ImageProjectId", "");
                    string location = SettingsStore.GetString("UnityAgent_ImageLocation", "us-central1");
                    // 未指定 (空文字) なら imageConfig 自体を送らない
                    string aspectRatio = SettingsStore.GetString("UnityAgent_ImageAspectRatio", "");
                    string imageSize = SettingsStore.GetString("UnityAgent_ImageSize", "");

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

                    return new GeminiImageProvider(apiKey, mode, imageModel, customEndpoint, projectId, location,
                        aspectRatio, imageSize);
            }
        }

        // ─── Helpers ───

        private static float ParseFloat(string s, float def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            // ロケール由来のカンマ小数 (例 "7,5") を救済
            if (float.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v2)) return v2;
            return def;
        }

        private static int ParseInt(string s, int def)
            => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

        private static string GetDefaultCliPath(LLMProviderType type)
        {
            switch (type)
            {
                case LLMProviderType.Claude_CLI: return "claude";
                case LLMProviderType.Gemini_CLI: return "gemini";
                case LLMProviderType.Codex_CLI: return "codex";
                case LLMProviderType.Antigravity_CLI: return "agy";
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
