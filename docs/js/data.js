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
    },
    {
      key: "face",
      count: 55,
      ja: { name: "BlendShape & Face", desc: "FaceEmo、表情パターン、カメラキャプチャまでを統合。" },
      en: { name: "BlendShape & Face", desc: "FaceEmo, expression sets, face camera capture in one place." },
    },
    {
      key: "material-texture",
      count: 74,
      ja: { name: "Material & Texture", desc: "lilToon / Poiyomi、TextureAtlas、AO ベイク、TexTransTool 統合。" },
      en: { name: "Material & Texture", desc: "lilToon / Poiyomi, atlasing, AO bake, TexTransTool integration." },
    },
    {
      key: "mesh",
      count: 28,
      ja: { name: "Mesh", desc: "メッシュ解析、編集、生成、UV アイランド単位の選択など。" },
      en: { name: "Mesh", desc: "Analyze, edit, generate meshes; per-UV-island selection and more." },
    },
    {
      key: "bone-physics",
      count: 47,
      ja: { name: "Bone & Physics", desc: "Bone、PhysBone、ウェイト編集、物理コンストレイント。" },
      en: { name: "Bone & Physics", desc: "Bone setups, PhysBones, weight editing, physics constraints." },
    },
    {
      key: "vrchat",
      count: 35,
      ja: { name: "VRChat SDK", desc: "Avatar3、Constraint、Contact、Performance、Expression Parameters。" },
      en: { name: "VRChat SDK", desc: "Avatar3, Constraints, Contacts, Performance, Expression Parameters." },
    },
    {
      key: "ndmf",
      count: 24,
      ja: { name: "Modular Avatar / NDMF", desc: "MA メニュー・パラメータ、NDMF パイプライン、VRCFury。" },
      en: { name: "Modular Avatar / NDMF", desc: "MA menus & params, NDMF pipelines, VRCFury." },
    },
    {
      key: "outfit",
      count: 23,
      ja: { name: "Outfit & Accessory", desc: "衣装フィッティング、アクセサリ配置、Mochi Fitter 連携。" },
      en: { name: "Outfit & Accessory", desc: "Outfit fitting, accessory placement, Mochi Fitter catalog." },
    },
    {
      key: "scene",
      count: 49,
      ja: { name: "Scene & Hierarchy", desc: "Scene、Hierarchy、Inspector、SceneView の操作。" },
      en: { name: "Scene & Hierarchy", desc: "Scene, Hierarchy, Inspector and SceneView operations." },
    },
    {
      key: "asset",
      count: 20,
      ja: { name: "Asset & Importer", desc: "アセット検索、インポート設定、Prefab。" },
      en: { name: "Asset & Importer", desc: "Asset search, importer settings, prefabs." },
    },
    {
      key: "quest",
      count: 8,
      ja: { name: "Quest 変換", desc: "Quest 互換シェーダー、Quest 用最適化ワークフロー。" },
      en: { name: "Quest conversion", desc: "Quest-compatible shaders and optimization workflows." },
    },
    {
      key: "osc",
      count: 16,
      ja: { name: "OSC", desc: "OSC 入出力の自動化と高度な制御。" },
      en: { name: "OSC", desc: "OSC I/O automation and advanced control." },
    },
    {
      key: "particle",
      count: 16,
      ja: { name: "Particle", desc: "Particle System の各モジュール設定を一括操作。" },
      en: { name: "Particle", desc: "Bulk configuration across Particle System modules." },
    },
    {
      key: "build",
      count: 16,
      ja: { name: "Build & Prefab", desc: "BuildPipeline、Prefab、Meta ファイル管理。" },
      en: { name: "Build & Prefab", desc: "BuildPipeline, prefabs, .meta management." },
    },
    {
      key: "gesture",
      count: 15,
      ja: { name: "Gesture & Menu", desc: "GestureManager、Interaction、メニュー編集。" },
      en: { name: "Gesture & Menu", desc: "GestureManager, interactions, menu editing." },
    },
    {
      key: "renderer",
      count: 11,
      ja: { name: "Renderer", desc: "Renderer Settings、SkinnedMesh の各種設定。" },
      en: { name: "Renderer", desc: "Renderer settings and SkinnedMesh configuration." },
    },
  ];

  // -------- Providers --------
  // Curated from Editor/Providers/*.cs.
  const PROVIDERS = [
    { name: "Anthropic Claude API", kind: "cloud", auth: "API key", ja: "Claude Sonnet / Opus / Haiku 各モデル対応。", en: "Supports Claude Sonnet / Opus / Haiku families." },
    { name: "OpenAI", kind: "cloud", auth: "API key", ja: "GPT-4 / GPT-5 系。OpenAI 互換 API もここから。", en: "GPT-4 / GPT-5 families. OpenAI-compatible APIs share this lane." },
    { name: "Google Gemini", kind: "cloud", auth: "API key / OAuth", ja: "Gemini 系。Vertex AI Express にも対応。", en: "Gemini family. Vertex AI Express supported." },
    { name: "DeepSeek", kind: "cloud", auth: "API key", ja: "OpenAI 互換 API 経由。", en: "Through OpenAI-compatible API." },
    { name: "Groq / xAI / Mistral / Perplexity", kind: "cloud", auth: "API key", ja: "OpenAI 互換 API 経由で利用可能。", en: "Available through OpenAI-compatible API." },
    { name: "Ollama / LM Studio (Local)", kind: "local", auth: "なし / None", ja: "OpenAI 互換 URL を指定するだけでローカル LLM が動作。", en: "Plug an OpenAI-compatible URL to run local LLMs." },
    { name: "Claude CLI", kind: "cli", auth: "CLI ログイン", ja: "Claude Code を Unity 内から呼び出し、ライブで thinking と tool 実行を表示。", en: "Drives Claude Code from Unity with live thinking & tool stream." },
    { name: "Gemini CLI", kind: "cli", auth: "CLI ログイン", ja: "Gemini CLI を介した会話実行。", en: "Conversation through Gemini CLI." },
    { name: "Codex CLI", kind: "cli", auth: "CLI ログイン", ja: "OpenAI Codex CLI 連携。", en: "OpenAI Codex CLI integration." },
    { name: "Browser Bridge", kind: "bridge", auth: "拡張機能", ja: "ブラウザのチャット UI を Unity に橋渡しする実験的プロバイダー。", en: "Experimental bridge that wires browser chat UIs into Unity." },
    { name: "Clipboard", kind: "bridge", auth: "なし / None", ja: "プロンプトをクリップボード経由で外部 AI に手渡し。", en: "Hand off prompts to any external AI via the clipboard." },
    { name: "MCP Server", kind: "bridge", auth: "ローカル", ja: "外部 MCP クライアントから UnityAgent ツール群を直接呼び出し。", en: "Lets external MCP clients invoke UnityAgent tools directly." },
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
          },
        },
      ],
    },
  ];

  window.UA_DATA = { TOOL_CATEGORIES, PROVIDERS, CHANGELOG };
})();
