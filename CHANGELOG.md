# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- **Avatar Optimizer Window** (`UnityAgent > Avatar Optimizer`) — MD3SDK / UI Toolkit ベースの統合最適化 UI。1 画面で Performance 解析 / AAO TraceAndOptimize 設定 / NDMF Mesh Simplifier / テクスチャ最適化を操作。アバター ルートは Selection から自動検出 (VRCAvatarDescriptor → Animator フォールバック)
- **NDMF Tester Window** (`UnityAgent > NDMF Tester`) — NDMFTools / BuildPipelineTools / AvatarPerformanceAnalyzer の各 API をボタンから直接呼び出してデバッグするウィンドウ
- `AnalyzeAvatarPerformance` — bake 不要のパフォーマンス解析ツール (`Editor/Tools/AvatarPerformanceAnalyzer.cs`)。VRC SDK 公式の `AvatarPerformance.CalculatePerformanceStats` (AAO もこれを利用) と NDMF `ParameterInfo.ForUI` を組み合わせ、シーン現在状態と post-build パラメータ予測を 1 レポートに統合
- `BakeAmbientOcclusion` — Raycast ベースの AO ベイクツール。`mode="texel"` (UV 展開 → PNG 出力) / `mode="vertex"` (mesh.colors → 新規 .asset + Renderer 差替) の 2 モード対応。SkinnedMeshRenderer の scale double-apply 回避済み
- `IdentifyBodySmr` / `IdentifyFaceSmr` — 誤差ゼロで Body / Face SkinnedMeshRenderer を特定 (多段ヒューリスティクス: 名前マッチ → 骨領域多様性 → viseme BlendShape → fallback)。BoundBonePro のアルゴリズムを独立移植。Risk=Safe
- TexTransTool (TTT) AI integration tools behind `NET_RS64_TTT` version define:
  - Tier 1 (read-only, Risk=Safe): `TttDescribePhases`, `TttListStableComponents`, `TttListComponents`
  - Tier 2 (authoring, Risk=Caution): `TttAddSimpleDecal`, `TttAddTextureBlender`, `TttAddAtlasTexture`
  - Tier 3 (pipeline, Risk=Caution/Safe): `TttManualBake`, `TttExitPreviews`
- New sub-assembly `AjisaiFlow.UnityAgent.TexTransTool.Editor` (`Editor/Tools/TexTransTool/`) gated on `net.rs64.tex-trans-tool [1.0.0,2.0.0)` presence
- `nadena.dev.ndmf` / `nadena.dev.ndmf.runtime` / `nadena.dev.ndmf.vrchat` を `AjisaiFlow.UnityAgent.Editor.asmdef` の必須参照に追加 (NDMF を hard dependency 化)
- `VRChatPerformanceTools.GetAvatarPerformanceStatsForGameObject` / `AvatarValidationTools.ValidateAvatarForGameObject` / `TextureMemoryAnalysisTools.AnalyzeTextureMemoryForGameObject` — それぞれ GameObject を直接受け取る internal overload (外部から clone 等を解析するための再利用パス)

### Changed
- メニューを `Window > 紫陽花広場 > *` から最上位 `UnityAgent > *` に集約 (例: `UnityAgent > AO Bake (Test)`)
- `ToolRegistry` now treats first-party sub-assemblies (`AjisaiFlow.UnityAgent.*`) as internal tools. Optional-package-gated modules like TexTransTool ship built-in and no longer require external-tool opt-in.
- `ToolRegistry.ResolveRisk` honors `[AgentTool(Risk=Safe|Dangerous)]` for internal tools when explicitly set; falls back to method-name-prefix heuristic only when attribute risk is the default `Caution`.
- Side effect: `AjisaiFlow.UnityAgent.World.Editor` tools (World/Template 系 21 件) previously required external-tool opt-in; they are now internal by default. Users who intentionally disabled them must re-disable via settings UI.

## [0.5.0] - 2026-04-02

### Changed
- VPM distribution switched from compiled DLL to **source code**
- Removed Obfuscar obfuscation — full source transparency
- Repository open-sourced under MIT license

### Added
- Update notification banner in main window
- Post-update changelog dialog (shown once per version)
- Claude CLI activity panel with live thinking/tool display
- Expressive loading animation during AI processing

### Fixed
- Claude CLI provider now correctly streams real-time output
- Inactivity-based timeout replaces fixed timeout (prevents false timeouts during active responses)
