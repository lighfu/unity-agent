/* UnityAgent — Site data
 * Tool categories, providers and changelog excerpts.
 * Pure data; rendering happens in main.js.
 */
(function () {
  "use strict";

  // -------- Tool categories (curated highlights) --------
  // Counts reflect the number of [AgentTool] entries in each Editor/Tools file (or grouped files).
  const TOOL_CATEGORIES = [
    {
      key: "animation",
      count: 51,
      ja: { name: "Animation & Animator", desc: "Animator State Machine、AnimationClip、AAC を編集・生成。" },
      en: { name: "Animation & Animator", desc: "Edit and generate Animator state machines, AnimationClips, and AAC." },
      "zh-TW": { name: "Animation & Animator", desc: "編輯和產生 Animator 狀態機、AnimationClip 與 AAC。" },
      zh: { name: "Animation & Animator", desc: "编辑和生成 Animator 状态机、AnimationClip 与 AAC。" },
    },
    {
      key: "face",
      count: 55,
      ja: { name: "BlendShape & Face", desc: "FaceEmo、表情パターン、カメラキャプチャまでを統合。" },
      en: { name: "BlendShape & Face", desc: "FaceEmo, expression sets, face camera capture in one place." },
      "zh-TW": { name: "BlendShape & Face", desc: "整合 FaceEmo、表情組合與臉部攝影機擷取。" },
      zh: { name: "BlendShape & Face", desc: "集成 FaceEmo、表情组合和面部摄像机捕获。" },
    },
    {
      key: "material-texture",
      count: 74,
      ja: { name: "Material & Texture", desc: "lilToon / Poiyomi、TextureAtlas、AO ベイク、TexTransTool 統合。" },
      en: { name: "Material & Texture", desc: "lilToon / Poiyomi, atlasing, AO bake, TexTransTool integration." },
      "zh-TW": { name: "Material & Texture", desc: "lilToon / Poiyomi、圖集化、AO 烘焙與 TexTransTool 整合。" },
      zh: { name: "Material & Texture", desc: "lilToon / Poiyomi、图集化、AO 烘焙和 TexTransTool 集成。" },
    },
    {
      key: "mesh",
      count: 28,
      ja: { name: "Mesh", desc: "メッシュ解析、編集、生成、UV アイランド単位の選択など。" },
      en: { name: "Mesh", desc: "Analyze, edit, generate meshes; per-UV-island selection and more." },
      "zh-TW": { name: "Mesh", desc: "分析、編輯和產生網格，支援按 UV 島選擇等操作。" },
      zh: { name: "Mesh", desc: "分析、编辑和生成网格，支持按 UV 岛选择等操作。" },
    },
    {
      key: "bone-physics",
      count: 47,
      ja: { name: "Bone & Physics", desc: "Bone、PhysBone、ウェイト編集、物理コンストレイント。" },
      en: { name: "Bone & Physics", desc: "Bone setups, PhysBones, weight editing, physics constraints." },
      "zh-TW": { name: "Bone & Physics", desc: "骨骼設定、PhysBone、權重編輯與物理約束。" },
      zh: { name: "Bone & Physics", desc: "骨骼设置、PhysBone、权重编辑和物理约束。" },
    },
    {
      key: "vrchat",
      count: 35,
      ja: { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance、Expression Parameters。" },
      en: { name: "VRChat SDK", desc: "Avatar3, Constraints, Contacts, Performance, Expression Parameters." },
      "zh-TW": { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance 與 Expression Parameters。" },
      zh: { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance 与 Expression Parameters。" },
    },
    {
      key: "ndmf",
      count: 24,
      ja: { name: "Modular Avatar / NDMF", desc: "MA メニュー・パラメータ、NDMF パイプライン、VRCFury。" },
      en: { name: "Modular Avatar / NDMF", desc: "MA menus & params, NDMF pipelines, VRCFury." },
      "zh-TW": { name: "Modular Avatar / NDMF", desc: "MA 選單與參數、NDMF 流程、VRCFury。" },
      zh: { name: "Modular Avatar / NDMF", desc: "MA 菜单与参数、NDMF 流程、VRCFury。" },
    },
    {
      key: "outfit",
      count: 23,
      ja: { name: "Outfit & Accessory", desc: "衣装フィッティング、アクセサリ配置、Mochi Fitter 連携。" },
      en: { name: "Outfit & Accessory", desc: "Outfit fitting, accessory placement, Mochi Fitter catalog." },
      "zh-TW": { name: "Outfit & Accessory", desc: "服裝適配、配件放置與 Mochi Fitter 目錄。" },
      zh: { name: "Outfit & Accessory", desc: "服装适配、配饰放置和 Mochi Fitter 目录。" },
    },
    {
      key: "scene",
      count: 49,
      ja: { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector、SceneView の操作。" },
      en: { name: "Scene & Hierarchy", desc: "Scene, Hierarchy, Inspector and SceneView operations." },
      "zh-TW": { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector 與 SceneView 操作。" },
      zh: { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector 和 SceneView 操作。" },
    },
    {
      key: "asset",
      count: 20,
      ja: { name: "Asset & Importer", desc: "アセット検索、インポート設定、Prefab。" },
      en: { name: "Asset & Importer", desc: "Asset search, importer settings, prefabs." },
      "zh-TW": { name: "Asset & Importer", desc: "資源搜尋、匯入設定與 Prefab。" },
      zh: { name: "Asset & Importer", desc: "资源搜索、导入设置和 Prefab。" },
    },
    {
      key: "quest",
      count: 8,
      ja: { name: "Quest 変換", desc: "Quest 互換シェーダー、Quest 用最適化ワークフロー。" },
      en: { name: "Quest conversion", desc: "Quest-compatible shaders and optimization workflows." },
      "zh-TW": { name: "Quest 轉換", desc: "Quest 相容 Shader 與 Quest 最佳化工作流程。" },
      zh: { name: "Quest 转换", desc: "Quest 兼容 Shader 与 Quest 优化工作流。" },
    },
    {
      key: "osc",
      count: 16,
      ja: { name: "OSC", desc: "OSC 入出力の自動化と高度な制御。" },
      en: { name: "OSC", desc: "OSC I/O automation and advanced control." },
      "zh-TW": { name: "OSC", desc: "OSC 輸入輸出自動化與進階控制。" },
      zh: { name: "OSC", desc: "OSC 输入输出自动化和高级控制。" },
    },
    {
      key: "particle",
      count: 16,
      ja: { name: "Particle", desc: "Particle System の各モジュール設定を一括操作。" },
      en: { name: "Particle", desc: "Bulk configuration across Particle System modules." },
      "zh-TW": { name: "Particle", desc: "批次設定 Particle System 的各個模組。" },
      zh: { name: "Particle", desc: "批量配置 Particle System 的各个模块。" },
    },
    {
      key: "build",
      count: 16,
      ja: { name: "Build & Prefab", desc: "BuildPipeline、Prefab、Meta ファイル管理。" },
      en: { name: "Build & Prefab", desc: "BuildPipeline, prefabs, .meta management." },
      "zh-TW": { name: "Build & Prefab", desc: "BuildPipeline、Prefab 與 .meta 檔案管理。" },
      zh: { name: "Build & Prefab", desc: "BuildPipeline、Prefab 和 .meta 文件管理。" },
    },
    {
      key: "gesture",
      count: 15,
      ja: { name: "Gesture & Menu", desc: "GestureManager、Interaction、メニュー編集。" },
      en: { name: "Gesture & Menu", desc: "GestureManager, interactions, menu editing." },
      "zh-TW": { name: "Gesture & Menu", desc: "GestureManager、互動與選單編輯。" },
      zh: { name: "Gesture & Menu", desc: "GestureManager、交互和菜单编辑。" },
    },
    {
      key: "renderer",
      count: 11,
      ja: { name: "Renderer", desc: "Renderer Settings、SkinnedMesh の各種設定。" },
      en: { name: "Renderer", desc: "Renderer settings and SkinnedMesh configuration." },
      "zh-TW": { name: "Renderer", desc: "Renderer 設定與 SkinnedMesh 各類配置。" },
      zh: { name: "Renderer", desc: "Renderer 设置和 SkinnedMesh 各类配置。" },
    },
  ];

  // -------- Providers --------
  // Curated from Editor/Providers/*.cs.
  const PROVIDERS = [
    { name: "Anthropic Claude API", kind: "cloud", auth: "API key", ja: "Claude Sonnet / Opus / Haiku 各モデル対応。", en: "Supports Claude Sonnet / Opus / Haiku families.", "zh-TW": "支援 Claude Sonnet / Opus / Haiku 系列模型。", zh: "支持 Claude Sonnet / Opus / Haiku 系列模型。" },
    { name: "OpenAI", kind: "cloud", auth: "API key", ja: "GPT-4 / GPT-5 系。OpenAI 互換 API もここから。", en: "GPT-4 / GPT-5 families. OpenAI-compatible APIs share this lane.", "zh-TW": "支援 GPT-4 / GPT-5 系列，OpenAI 相容 API 也走這裡。", zh: "支持 GPT-4 / GPT-5 系列，OpenAI 兼容 API 也走这里。" },
    { name: "Google Gemini", kind: "cloud", auth: "API key / OAuth", ja: "Gemini 系。Vertex AI Express にも対応。", en: "Gemini family. Vertex AI Express supported.", "zh-TW": "支援 Gemini 系列，也支援 Vertex AI Express。", zh: "支持 Gemini 系列，也支持 Vertex AI Express。" },
    { name: "DeepSeek", kind: "cloud", auth: "API key", ja: "OpenAI 互換 API 経由。", en: "Through OpenAI-compatible API.", "zh-TW": "透過 OpenAI 相容 API 使用。", zh: "通过 OpenAI 兼容 API 使用。" },
    { name: "Groq / xAI / Mistral / Perplexity", kind: "cloud", auth: "API key", ja: "OpenAI 互換 API 経由で利用可能。", en: "Available through OpenAI-compatible API.", "zh-TW": "可透過 OpenAI 相容 API 使用。", zh: "可通过 OpenAI 兼容 API 使用。" },
    { name: "Ollama / LM Studio (Local)", kind: "local", auth: "なし / None", ja: "OpenAI 互換 URL を指定するだけでローカル LLM が動作。", en: "Plug an OpenAI-compatible URL to run local LLMs.", "zh-TW": "指定 OpenAI 相容 URL 即可執行本機 LLM。", zh: "指定 OpenAI 兼容 URL 即可运行本地 LLM。" },
    { name: "Claude CLI", kind: "cli", auth: "CLI ログイン", ja: "Claude Code を Unity 内から呼び出し、ライブで thinking と tool 実行を表示。", en: "Drives Claude Code from Unity with live thinking & tool stream.", "zh-TW": "從 Unity 內呼叫 Claude Code，並即時顯示 thinking 與工具流。", zh: "从 Unity 内调用 Claude Code，并实时显示 thinking 与工具流。" },
    { name: "Gemini CLI", kind: "cli", auth: "CLI ログイン", ja: "Gemini CLI を介した会話実行。", en: "Conversation through Gemini CLI.", "zh-TW": "透過 Gemini CLI 執行對話。", zh: "通过 Gemini CLI 执行对话。" },
    { name: "Codex CLI", kind: "cli", auth: "CLI ログイン", ja: "OpenAI Codex CLI 連携。", en: "OpenAI Codex CLI integration.", "zh-TW": "整合 OpenAI Codex CLI。", zh: "集成 OpenAI Codex CLI。" },
    { name: "Browser Bridge", kind: "bridge", auth: "拡張機能", ja: "ブラウザのチャット UI を Unity に橋渡しする実験的プロバイダー。", en: "Experimental bridge that wires browser chat UIs into Unity.", "zh-TW": "實驗性橋接提供者，將瀏覽器聊天 UI 接入 Unity。", zh: "实验性桥接提供商，将浏览器聊天 UI 接入 Unity。" },
    { name: "Clipboard", kind: "bridge", auth: "なし / None", ja: "プロンプトをクリップボード経由で外部 AI に手渡し。", en: "Hand off prompts to any external AI via the clipboard.", "zh-TW": "透過剪貼簿把提示詞交給任意外部 AI。", zh: "通过剪贴板把提示词交给任意外部 AI。" },
    { name: "MCP Server", kind: "bridge", auth: "ローカル", ja: "外部 MCP クライアントから UnityAgent ツール群を直接呼び出し。", en: "Lets external MCP clients invoke UnityAgent tools directly.", "zh-TW": "允許外部 MCP 用戶端直接呼叫 UnityAgent 工具。", zh: "允许外部 MCP 客户端直接调用 UnityAgent 工具。" },
  ];

  // -------- Changelog excerpts --------
  // Lifted from CHANGELOG.md; keep latest 3 entries.
  const CHANGELOG = [
    {
      version: "Unreleased",
      isUnreleased: true,
      groups: [
        {
          label: "added",
          items: {
            ja: [
              "BakeAmbientOcclusion — Raycast ベースの AO ベイク (texel / vertex 2 モード)。",
              "IdentifyBodySmr / IdentifyFaceSmr — 多段ヒューリスティクスで Body / Face SMR を誤差ゼロで特定。",
              "TexTransTool 連携 — read-only / authoring / pipeline 各 Tier のツール群を NET_RS64_TTT 越しに搭載。",
            ],
            en: [
              "BakeAmbientOcclusion — raycast AO bake with texel / vertex modes.",
              "IdentifyBodySmr / IdentifyFaceSmr — multi-stage heuristics to identify Body / Face SMRs reliably.",
              "TexTransTool integration — read-only, authoring and pipeline tiers behind NET_RS64_TTT.",
            ],
            "zh-TW": [
              "BakeAmbientOcclusion — 基於 Raycast 的 AO 烘焙，支援 texel / vertex 兩種模式。",
              "IdentifyBodySmr / IdentifyFaceSmr — 透過多階段啟發式可靠識別 Body / Face SMR。",
              "TexTransTool 整合 — 透過 NET_RS64_TTT 提供 read-only、authoring 與 pipeline 各層工具。",
            ],
            zh: [
              "BakeAmbientOcclusion — 基于 Raycast 的 AO 烘焙，支持 texel / vertex 两种模式。",
              "IdentifyBodySmr / IdentifyFaceSmr — 通过多阶段启发式可靠识别 Body / Face SMR。",
              "TexTransTool 集成 — 通过 NET_RS64_TTT 提供 read-only、authoring 和 pipeline 各层工具。",
            ],
          },
        },
        {
          label: "changed",
          items: {
            ja: [
              "ToolRegistry が AjisaiFlow.UnityAgent.* を内部ツール扱い。Optional パッケージ依存ツールも同梱可能に。",
              "[AgentTool(Risk=...)] を尊重し、内部ツールのリスク判定を改善。",
            ],
            en: [
              "ToolRegistry now treats AjisaiFlow.UnityAgent.* as internal. Optional-package-gated modules ship built-in.",
              "Honors [AgentTool(Risk=...)] for internal tools, improving risk classification.",
            ],
            "zh-TW": [
              "ToolRegistry 現在將 AjisaiFlow.UnityAgent.* 視為內部工具，可內建依賴可選套件的模組。",
              "內部工具會尊重 [AgentTool(Risk=...)]，改進風險分類。",
            ],
            zh: [
              "ToolRegistry 现在将 AjisaiFlow.UnityAgent.* 视为内部工具，可内置依赖可选包的模块。",
              "内部工具会尊重 [AgentTool(Risk=...)]，改进风险分类。",
            ],
          },
        },
      ],
    },
    {
      version: "0.5.0",
      date: "2026-04-02",
      groups: [
        {
          label: "changed",
          items: {
            ja: [
              "VPM 配布物がコンパイル済み DLL から ソースコード に切替。",
              "Obfuscar 難読化を撤去。完全ソース透明化。",
              "リポジトリを MIT ライセンスで OSS 化。",
            ],
            en: [
              "VPM distribution switched from compiled DLL to source.",
              "Removed Obfuscar obfuscation — full source transparency.",
              "Repository open-sourced under MIT license.",
            ],
            "zh-TW": [
              "VPM 發布形式從已編譯 DLL 切換為原始碼。",
              "移除 Obfuscar 混淆，實現完整原始碼透明。",
              "倉庫以 MIT 授權開源。",
            ],
            zh: [
              "VPM 分发形式从已编译 DLL 切换为源码。",
              "移除 Obfuscar 混淆，实现完整源码透明。",
              "仓库以 MIT 许可开源。",
            ],
          },
        },
        {
          label: "added",
          items: {
            ja: [
              "メインウィンドウに更新通知バナー。",
              "更新後の CHANGELOG ダイアログ (バージョン毎に 1 度)。",
              "Claude CLI のライブ thinking / tool 表示パネル。",
              "AI 応答中の表現的ローディングアニメーション。",
            ],
            en: [
              "Update notification banner on the main window.",
              "Post-update changelog dialog (shown once per version).",
              "Claude CLI activity panel with live thinking / tool stream.",
              "Expressive loading animation during AI processing.",
            ],
            "zh-TW": [
              "主視窗新增更新通知橫幅。",
              "更新後顯示 CHANGELOG 對話框（每個版本顯示一次）。",
              "新增 Claude CLI 活動面板，即時顯示 thinking / 工具流。",
              "AI 回應期間加入更直觀的載入動畫。",
            ],
            zh: [
              "主窗口新增更新通知横幅。",
              "更新后显示 CHANGELOG 对话框（每个版本显示一次）。",
              "新增 Claude CLI 活动面板，实时显示 thinking / 工具流。",
              "AI 响应期间加入更直观的加载动画。",
            ],
          },
        },
        {
          label: "fixed",
          items: {
            ja: [
              "Claude CLI プロバイダーが正しくリアルタイム出力するように修正。",
              "応答中の誤タイムアウトを防ぐ非アクティブ判定タイムアウトに置換。",
            ],
            en: [
              "Claude CLI provider now correctly streams real-time output.",
              "Replaced fixed timeout with inactivity-based timeout to avoid false timeouts.",
            ],
            "zh-TW": [
              "Claude CLI 提供者現在可以正確串流輸出即時內容。",
              "將固定逾時替換為基於閒置時間的逾時，避免誤判逾時。",
            ],
            zh: [
              "Claude CLI 提供商现在可以正确流式输出实时内容。",
              "将固定超时替换为基于空闲时间的超时，避免误判超时。",
            ],
          },
        },
      ],
    },
  ];

  window.UA_DATA = { TOOL_CATEGORIES, PROVIDERS, CHANGELOG };
})();
