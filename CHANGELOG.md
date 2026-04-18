# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- TexTransTool (TTT) AI integration tools behind `NET_RS64_TTT` version define:
  - Tier 1 (read-only, Risk=Safe): `TttDescribePhases`, `TttListStableComponents`, `TttListComponents`
  - Tier 2 (authoring, Risk=Caution): `TttAddSimpleDecal`, `TttAddTextureBlender`, `TttAddAtlasTexture`
  - Tier 3 (pipeline, Risk=Caution/Safe): `TttManualBake`, `TttExitPreviews`
- New sub-assembly `AjisaiFlow.UnityAgent.TexTransTool.Editor` (`Editor/Tools/TexTransTool/`) gated on `net.rs64.tex-trans-tool [1.0.0,2.0.0)` presence

### Changed
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
