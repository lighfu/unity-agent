/* UnityAgent — Site data
 * Tool categories, providers and changelog excerpts.
 * Pure data; rendering happens in main.js.
 */
(function () {
  "use strict";

  // -------- Tool categories --------
  // Counts are the number of [AgentTool] entries in the Editor/Tools files of each
  // category. Every tool in the source belongs to exactly one category here, so the
  // counts add up to the total quoted in tools.footnote.
  const TOOL_CATEGORIES = [
    {
      key: "animation",
      count: 57,
      ja: { name: "Animation & Animator", desc: "Animator State Machine、AnimationClip、AAC を編集・生成。binding path の一括付け替えも。" },
      en: { name: "Animation & Animator", desc: "Edit and generate Animator state machines, AnimationClips and AAC; rebind clip paths in bulk." },
      "zh-TW": { name: "Animation & Animator", desc: "編輯和產生 Animator 狀態機、AnimationClip 與 AAC，也可批次改寫 binding path。" },
      zh: { name: "Animation & Animator", desc: "编辑和生成 Animator 状态机、AnimationClip 与 AAC，也可批量改写 binding path。" },
    },
    {
      key: "face",
      count: 78,
      ja: { name: "BlendShape & Face", desc: "FaceEmo、表情パターン、BlendShape の解析と編集。" },
      en: { name: "BlendShape & Face", desc: "FaceEmo, expression sets, BlendShape analysis and editing." },
      "zh-TW": { name: "BlendShape & Face", desc: "FaceEmo、表情組合，以及 BlendShape 的分析與編輯。" },
      zh: { name: "BlendShape & Face", desc: "FaceEmo、表情组合，以及 BlendShape 的分析与编辑。" },
    },
    {
      key: "material-texture",
      count: 82,
      ja: { name: "Material & Texture", desc: "lilToon / Poiyomi、TextureAtlas、AO ベイク、TexTransTool 統合、シェーダー変種の実コンパイル検証。" },
      en: { name: "Material & Texture", desc: "lilToon / Poiyomi, atlasing, AO bake, TexTransTool integration, real shader-variant compilation checks." },
      "zh-TW": { name: "Material & Texture", desc: "lilToon / Poiyomi、圖集化、AO 烘焙、TexTransTool 整合，以及 Shader 變體的實際編譯驗證。" },
      zh: { name: "Material & Texture", desc: "lilToon / Poiyomi、图集化、AO 烘焙、TexTransTool 集成，以及 Shader 变体的实际编译验证。" },
    },
    {
      key: "mesh",
      count: 30,
      ja: { name: "Mesh", desc: "メッシュ解析、編集、生成、UV アイランド単位の選択など。" },
      en: { name: "Mesh", desc: "Analyze, edit, generate meshes; per-UV-island selection and more." },
      "zh-TW": { name: "Mesh", desc: "分析、編輯和產生網格，支援按 UV 島選擇等操作。" },
      zh: { name: "Mesh", desc: "分析、编辑和生成网格，支持按 UV 岛选择等操作。" },
    },
    {
      key: "bone-physics",
      count: 54,
      ja: { name: "Bone & Physics", desc: "Bone、PhysBone、ウェイト編集、Cloth。" },
      en: { name: "Bone & Physics", desc: "Bone setups, PhysBones, weight editing, cloth." },
      "zh-TW": { name: "Bone & Physics", desc: "骨骼設定、PhysBone、權重編輯與 Cloth。" },
      zh: { name: "Bone & Physics", desc: "骨骼设置、PhysBone、权重编辑与 Cloth。" },
    },
    {
      key: "vrchat",
      count: 51,
      ja: { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance、Expression Parameters、SDK からのアップロード。" },
      en: { name: "VRChat SDK", desc: "Avatar3, Constraints, Contacts, Performance, Expression Parameters, and SDK uploads." },
      "zh-TW": { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance、Expression Parameters 與 SDK 上傳。" },
      zh: { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance、Expression Parameters 与 SDK 上传。" },
    },
    {
      key: "ndmf",
      count: 39,
      ja: { name: "Modular Avatar / NDMF", desc: "MA メニュー・パラメータ、NDMF パイプライン、VRCFury、AAO。" },
      en: { name: "Modular Avatar / NDMF", desc: "MA menus & params, NDMF pipelines, VRCFury, AAO." },
      "zh-TW": { name: "Modular Avatar / NDMF", desc: "MA 選單與參數、NDMF 流程、VRCFury 與 AAO。" },
      zh: { name: "Modular Avatar / NDMF", desc: "MA 菜单与参数、NDMF 流程、VRCFury 与 AAO。" },
    },
    {
      key: "outfit",
      count: 18,
      ja: { name: "Outfit & Accessory", desc: "衣装フィッティング、アクセサリ配置、Mochi Fitter 連携。" },
      en: { name: "Outfit & Accessory", desc: "Outfit fitting, accessory placement, Mochi Fitter catalog." },
      "zh-TW": { name: "Outfit & Accessory", desc: "服裝適配、配件放置與 Mochi Fitter 目錄。" },
      zh: { name: "Outfit & Accessory", desc: "服装适配、配饰放置和 Mochi Fitter 目录。" },
    },
    {
      key: "scene",
      count: 61,
      ja: { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector、コンポーネントとプロパティの操作。" },
      en: { name: "Scene & Hierarchy", desc: "Scene, Hierarchy, Inspector, component and property operations." },
      "zh-TW": { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector 與元件、屬性操作。" },
      zh: { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector 与组件、属性操作。" },
    },
    {
      key: "asset",
      count: 26,
      ja: { name: "Asset & Importer", desc: "アセット検索、インポート設定、GUID による逆引き参照検索。" },
      en: { name: "Asset & Importer", desc: "Asset search, importer settings, reverse reference lookup by GUID." },
      "zh-TW": { name: "Asset & Importer", desc: "資源搜尋、匯入設定，以及以 GUID 反查參照。" },
      zh: { name: "Asset & Importer", desc: "资源搜索、导入设置，以及以 GUID 反查引用。" },
    },
    {
      key: "capture",
      count: 20,
      ja: { name: "Capture & Diff", desc: "GameView / SceneView / エディタウィンドウの撮影と、画像同士の差分。AI に見せる目。" },
      en: { name: "Capture & Diff", desc: "Capture GameView, SceneView and editor windows, then diff the images — the AI's eyes." },
      "zh-TW": { name: "Capture & Diff", desc: "擷取 GameView / SceneView / 編輯器視窗並比對影像差異，等於 AI 的眼睛。" },
      zh: { name: "Capture & Diff", desc: "捕获 GameView / SceneView / 编辑器窗口并比对图像差异，相当于 AI 的眼睛。" },
    },
    {
      key: "script",
      count: 31,
      ja: { name: "Script & Diagnostics", desc: "C# の動的実行、コンパイル待ち、コンソール、テスト実行、スキル管理。" },
      en: { name: "Script & Diagnostics", desc: "Run C# dynamically, await compilation, read the console, run tests, manage skills." },
      "zh-TW": { name: "Script & Diagnostics", desc: "動態執行 C#、等待編譯、讀取 Console、執行測試與管理 Skill。" },
      zh: { name: "Script & Diagnostics", desc: "动态执行 C#、等待编译、读取 Console、运行测试和管理 Skill。" },
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
      key: "particle-audio",
      count: 21,
      ja: { name: "Particle & Audio", desc: "Particle System の各モジュール設定と、AudioSource / AudioClip の操作。" },
      en: { name: "Particle & Audio", desc: "Particle System modules plus AudioSource / AudioClip handling." },
      "zh-TW": { name: "Particle & Audio", desc: "Particle System 各模組設定，以及 AudioSource / AudioClip 操作。" },
      zh: { name: "Particle & Audio", desc: "Particle System 各模块设置，以及 AudioSource / AudioClip 操作。" },
    },
    {
      key: "build",
      count: 14,
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
              "CaptureFromPose — 任意の位置・向きにカメラを置いて 1 枚撮る。アバターの目の位置から見た画も撮れる。",
              "DescribeType — 型のメンバを一覧。難読化された DLL でも中身を確認できる。",
              "RunEditorScript / RunEditorScriptAsync に members 引数。ヘルパーメソッドやイテレータを宣言できる。",
            ],
            en: [
              "CaptureFromPose — place a throwaway camera anywhere and take one shot, including the view from the avatar's eyes.",
              "DescribeType — list a type's members, even inside obfuscated DLLs.",
              "A members argument for RunEditorScript / RunEditorScriptAsync, so scripts can declare helper methods and iterators.",
            ],
            "zh-TW": [
              "CaptureFromPose — 可在任意位置與角度放置攝影機拍攝一張畫面，也能拍出從 Avatar 眼睛看出去的視角。",
              "DescribeType — 列出型別的成員，即使是混淆過的 DLL 也能查看。",
              "RunEditorScript / RunEditorScriptAsync 新增 members 參數，可宣告輔助方法與迭代器。",
            ],
            zh: [
              "CaptureFromPose — 可在任意位置和角度放置摄像机拍摄一张画面，也能拍出从 Avatar 眼睛看出去的视角。",
              "DescribeType — 列出类型的成员，即使是混淆过的 DLL 也能查看。",
              "RunEditorScript / RunEditorScriptAsync 新增 members 参数，可声明辅助方法与迭代器。",
            ],
          },
        },
        {
          label: "fixed",
          items: {
            ja: [
              "RunEditorScript が Debug.Log の出力を捨てたうえで、成功とだけ返していた問題。",
            ],
            en: [
              "RunEditorScript discarded Debug.Log output and reported nothing but success.",
            ],
            "zh-TW": [
              "RunEditorScript 會丟棄 Debug.Log 的輸出，只回報成功。",
            ],
            zh: [
              "RunEditorScript 会丢弃 Debug.Log 的输出，只回报成功。",
            ],
          },
        },
      ],
    },
    {
      version: "0.15.0",
      date: "2026-08-19",
      groups: [
        {
          label: "added",
          items: {
            ja: [
              "ツール呼び出しの統計ウィンドウ。時系列・ランキング・カテゴリ別・所要時間の 4 グラフ。",
              "キャプチャ群 — GameView、カメラ、エディタウィンドウ、アニメーション連番を撮影して差分を取る。",
              "シェーダー変種の実コンパイル検証。差し込んだコードが本当に通るかを確かめられる。",
              "MCP の 120 秒制限を超える処理向けの非同期ジョブ (RunEditorScriptAsync / GetJobResult)。",
            ],
            en: [
              "A tool-call statistics window with four charts: timeline, ranking, category breakdown and duration.",
              "Capture tools — GameView, cameras, editor windows and animation frames, plus image diffing.",
              "Real shader-variant compilation checks, so you can confirm injected code actually compiles.",
              "Async jobs for work that exceeds the 120-second MCP limit (RunEditorScriptAsync / GetJobResult).",
            ],
            "zh-TW": [
              "工具呼叫統計視窗，提供時間軸、排行、類別分佈與耗時共 4 種圖表。",
              "擷取工具群 — 可擷取 GameView、攝影機、編輯器視窗與動畫連續影格，並比對差異。",
              "Shader 變體的實際編譯驗證，可確認插入的程式碼是否真的能通過編譯。",
              "針對超過 MCP 120 秒限制的作業提供非同步工作 (RunEditorScriptAsync / GetJobResult)。",
            ],
            zh: [
              "工具调用统计窗口，提供时间轴、排行、类别分布与耗时共 4 种图表。",
              "捕获工具群 — 可捕获 GameView、摄像机、编辑器窗口与动画连续帧，并比对差异。",
              "Shader 变体的实际编译验证，可确认插入的代码是否真的能通过编译。",
              "针对超过 MCP 120 秒限制的作业提供异步任务 (RunEditorScriptAsync / GetJobResult)。",
            ],
          },
        },
        {
          label: "fixed",
          items: {
            ja: [
              "Ollama など OpenAI 互換プロバイダで Unicode エスケープがデコードされず、ツールが 1 つも実行できなかった (#5)。",
              "スキーマにない引数が黙って捨てられ、意図しないオブジェクトを操作していた (#7)。",
            ],
            en: [
              "Unicode escapes were not decoded on OpenAI-compatible providers such as Ollama, so no tool could run (#5).",
              "Arguments missing from the schema were dropped silently, letting tools act on the wrong object (#7).",
            ],
            "zh-TW": [
              "在 Ollama 等 OpenAI 相容供應商上 Unicode 跳脫字元未被解碼，導致完全無法執行工具 (#5)。",
              "不在 schema 中的參數會被靜默丟棄，導致工具操作到非預期的物件 (#7)。",
            ],
            zh: [
              "在 Ollama 等 OpenAI 兼容供应商上 Unicode 转义未被解码，导致完全无法执行工具 (#5)。",
              "不在 schema 中的参数会被静默丢弃，导致工具操作到非预期的对象 (#7)。",
            ],
          },
        },
      ],
    },
    {
      version: "0.14.0",
      date: "2026-08-09",
      groups: [
        {
          label: "added",
          items: {
            ja: [
              "MCP サーバーが Streamable HTTP トランスポートに対応 (#4)。",
            ],
            en: [
              "The MCP server now supports the Streamable HTTP transport (#4).",
            ],
            "zh-TW": [
              "MCP 伺服器支援 Streamable HTTP 傳輸 (#4)。",
            ],
            zh: [
              "MCP 服务器支持 Streamable HTTP 传输 (#4)。",
            ],
          },
        },
        {
          label: "fixed",
          items: {
            ja: [
              "Streamable HTTP の Origin 検証を追加。IPv6 と IPv4-mapped のループバックを受け付ける。",
            ],
            en: [
              "Added Origin validation for Streamable HTTP, accepting IPv6 and IPv4-mapped loopback.",
            ],
            "zh-TW": [
              "新增 Streamable HTTP 的 Origin 驗證，並接受 IPv6 與 IPv4-mapped 的 loopback。",
            ],
            zh: [
              "新增 Streamable HTTP 的 Origin 验证，并接受 IPv6 与 IPv4-mapped 的 loopback。",
            ],
          },
        },
      ],
    },
  ];

  window.UA_DATA = { TOOL_CATEGORIES, PROVIDERS, CHANGELOG };
})();
