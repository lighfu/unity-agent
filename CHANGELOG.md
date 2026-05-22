# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [0.11.0] - 2026-05-17

### Added — Plan C: Gesture-Aware Expression Workflow

- 5 layers: Orchestrator / Discovery / Convention / Curation / Execution (`Editor/Tools/FaceEmoPlanC/`)
- 10 new AgentTools under FaceEmoPlanC namespace:
  - Discovery: `ResolveTargetAvatar`, `InspectFaceEmoState`, `AutoSetupFaceEmoForAvatar`
  - Gesture: `ListGestureBindings`, `FindBranchByCondition`, `DetectGestureConflicts`, `AssignClipToGesture`
  - Curation: `SuggestCandidateShapes`, `ApplyExpressionVariation`, `ListExpressionVariations`
- Session API extensions (`FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`):
  - `OpenForBranch` — load existing Branch clip into Expression Editor
  - `CommitAsBranchOf` — atomic 6-step commit + rollback (clip create → branch find/add → slot assign → menu save → mainview refresh)
  - `CommitInPlace` — overwrite existing clip in place (EditExistingClip mode)
  - `GetCurrentValuesWithPaths` — path-preserving blendshape read for commit
  - Enums: `SessionEditMode` (NewMode/EditExistingClip/CreateBranchClip), `OverwriteMode` (Ask/Overwrite/EditExisting/Cancel)
- `OpenExpressionSession` accepts `editMode='new-mode'|'create-branch-clip'|'edit-existing-clip'` (default 'new-mode')
- New AgentTool `CommitExpressionSessionToBranch` for CreateBranchClip path
- `CommitExpressionSession` routes to `CommitInPlace` when EditMode=EditExistingClip
- `BuiltInSkills.cs` Workflow C guide (10-step gesture-aware flow)
- Ctrl+Z atomic rollback (`Undo.SetCurrentGroupName` + `CollapseUndoOperations`)
- `FaceProfileTools.LoadOrBuild` visibility changed from private to internal (CurationTools access)
- Spec: `docs/superpowers/specs/2026-05-17-faceemo-plan-c-gesture-assignment-design.md`
- Plan: `docs/superpowers/plans/2026-05-17-faceemo-plan-c-gesture-assignment.md`

### Fixed

- Quest conversion tools no longer break compilation on VRCQuestTools versions older than 2.7.0. The `MaterialSwap` component was added in VRCQuestTools 2.7.0, but `QuestConversionTools.cs` referenced it whenever *any* VRCQuestTools version was installed, causing `CS0246: MaterialSwap could not be found`. The `AddMaterialSwap` tool and the Material Swap section of `InspectQuestSettings` are now gated behind a new `VRC_QUEST_TOOLS_MATERIAL_SWAP` define (`[2.7.0,)`). All other Quest tools stay available on older VRCQuestTools.

## [Unreleased]
### Changed
- FaceEmo is now REQUIRED for expression editing. Expression tools refuse to run without FaceEmo installed + a configured launcher + TargetAvatar.
- Expression building now drives FaceEmo's ExpressionEditor live preview (when reflection access is healthy) or falls back to `.anim` write + window refresh (Degraded mode).
- `FaceEmoAPI.SaveMenu` no longer auto-calls `RefreshWindowIfOpen`. After a domain reload, FaceEmo's stale `MainView` can NRE in `HierarchyView.Dispose` during re-Launch, and that exception fires on the next editor tick — out of any reachable try/catch. AI workflows already call `RefreshFaceEmoMainView` explicitly (BuiltInSkills.cs Workflow B step 7). `RefreshWindowIfOpen` now also force-closes any stale `MainWindow` before re-Launch as a defensive reset; failures are demoted from Warning to Info log.
- `FaceEmoTools.ListFaceEmoExpressions` / `InspectFaceEmo` now read from the launcher's own `MenuRepositoryComponent` (live scene data) as their highest-priority source. Previously they hit `AssetDatabase.FindAssets` for a backup `FaceEmoProject` first, which often returned an empty backup and misled AI into retrying registrations.
- `FaceEmoAPI.FindLauncher("")` (auto-find) now prefers `FaceEmo*` roots with a configured `TargetAvatar` over the first arbitrary launcher. Scenes with many launchers no longer surface misleading "no TargetAvatar" gate errors on the wrong one.
- `SetExpressionPreviewMulti` Live-mode success message now includes a `Note:` explaining that the scene mesh is NOT updated (only FaceEmo's ExpressionEditor preview) and that visual verification needs `CaptureFaceEmoModeThumbnail` AFTER `CommitExpressionSession`.
- `ListFaceEmoExpressions` / `InspectFaceEmo` now also dump the **Unregistered** menu list. When `Registered` is at its FaceEmo cap (7 items), Plan A's `Session.Commit` falls back to `Unregistered` — without showing it, those Modes appeared "missing" and the AI/user retried registration.
- `FaceProfileTools.SetExpressionPreviewMulti` / `SuggestExpressionShapes` now use a new **avatar-aware** gate (`FaceEmoGate.RequireExpressionEditingReadyForAvatar`) that prefers the launcher whose `TargetAvatar` matches `avatarRootName`. Previously these tools picked the first arbitrary configured launcher in scene root order, so a Commit for "Milfy_Another" could end up registered in a Chiffon-targeted launcher's menu. New helper: `FaceEmoAPI.FindLauncherForAvatar(string)`.
- `ExpressionEditorBridge.Dispose` now calls `IExpressionEditor.Dispose()` on FaceEmo's presenter so the chain disposes `ExpressionEditorModelFacade` → `PreviewClipSampler` → `DestroyImmediate(_previewAvatar)`. Without this, every `TryOpen` left a `HideAndDontSave` cloned avatar at world position (100,100,100); stacking N invocations made N avatars render on top of each other in FaceEmo's `PreviewWindow`. `OpenForMode` / `OpenForNewExpression` also bracket-sweep (before _active.Dispose and after Bridge.TryOpen) using `ExpressionEditorBridge.CleanupOrphanPreviewAvatars` to handle FaceEmo edge cases where the standard dispose chain leaves orphans.
- `CommitExpressionSession` / `CreateAndRegisterExpression` / `CreateExpressionFromData` success messages now include `destination=Registered|Unregistered|Existing`, and append an actionable **Note** when the Mode landed in Unregistered because FaceEmo's Registered list is at its 7-item cap. The Note explains the consequence (gesture-assignable but NOT in the VRChat radial menu) and how to recover via `RemoveExpression` + `MoveExpressionItem` or `CreateExpressionGroup` + `MoveExpressionItem`. New helper: `FaceEmoExpressionSession.GetUnregisteredFallbackNote()`.
- `OpenExpressionSession` gains a new optional `avatarRootName` parameter that picks the FaceEmo launcher whose `TargetAvatar` matches that name. Without this, generic auto-find returned the first configured launcher in scene root order — which could be a different avatar's launcher — and the subsequent `SetExpressionPreviewMulti` would silently commit the new Mode to the wrong avatar's menu. Workflow B now passes `avatarRootName` and `SetExpressionPreviewMulti` errors with a clear recovery hint when the active session's launcher targets a different avatar than the requested one. `Session.OpenForNewExpression` and `Session.OpenForMode` gain the same parameter at the API level.
- Plan B thumbnail tools (`CaptureFaceEmoModeThumbnail`, `CaptureFaceEmoGestureTable`, `CaptureFaceEmoExMenuThumbnail`, `RefreshFaceEmoMainView`) gain the same optional `avatarRootName` parameter and a unified launcher-resolution priority: explicit `avatarRootName` → active session's launcher → generic auto-find. The error message when a Mode is not found now reports which launcher was searched so the AI knows to retry with the right avatar.
### Added
- `OpenExpressionSession`, `ReadExpressionFromWindow`, `CommitExpressionSession`, `CloseExpressionSession` AgentTools.
- `CleanupFaceEmoPreviewAvatars` AgentTool — sweeps the scene for orphan FaceEmo preview avatars (HideAndDontSave, located at (100,100,100), avatar-like). Use this when FaceEmo's PreviewWindow shows multiple stacked avatars from prior abandoned sessions.
- `FaceEmoGate`, `FaceEmoExpressionSession`, `ExpressionEditorBridge`, `AssetPathFallback`.
- Plan B (Thumbnail integration): `CaptureFaceEmoModeThumbnail`, `CaptureFaceEmoGestureTable`, `CaptureFaceEmoExMenuThumbnail`, `RefreshFaceEmoMainView` AgentTools.
- `FaceEmoThumbnailRenderer` (internal, reflection layer for FaceEmo's MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer).
- PNG output under `Library/UnityAgent/face-thumbnails/`.
### Notes
- Plan B (Thumbnail integration) is included in this release.

## [0.10.4] — 2026-05-11

### Added
- **TestRunner** ツール群 — 外部 CI/スクリプトから MCP 経由で UnityAgent を駆動可能: `StartTestSession` / `SendTestPrompt` / `GetSessionState` / `SwitchModel` / `DiscardTestSession`。テストセッションはアクティブな UnityAgentWindow に live 表示 (UI hijack) され、user prompt と AI 応答が通常のチャット UI でリアルタイム確認可能。
- **`CaptureMeshIsolated`** — 特定 mesh/GameObject を**シーン全体 isolation** で多角度 (front/left/right/back) からキャプチャ。inactive な outfit メッシュも一時 activate して撮影可能。
- Group A capture ツール群 (CaptureSceneView / CaptureMultiAngle / CaptureFacePreview / CaptureExpressionPreview / ScanAvatarMeshes) に画質オプションを統一追加: `maxWidth` (downscale), `format='png'|'jpg'`, `jpgQuality`, `saveToPath`。デフォルト解像度を 512→1024 に引き上げ。
- 全 capture ツールが `%TEMP%\unity-agent-last-capture.{png,jpg}` にデバッグダンプ。AI クライアントが MCP image attachment を表示できない環境でも Read ツールで画像確認可能。
- ScanAvatarMeshes の各 cell に **`[N] mesh-name` の TextMesh ラベル**を埋め込み。

### Fixed
- `CaptureMultiAngle` の bounds 計算 — 非アクティブ衣装メッシュの runtime SMR bounds 合算で camera が遠ざかる問題を修正 (アクティブ renderer のみ + tight mesh.bounds 使用)。
- `CaptureFacePreview` のフレーミング — SMR runtime bounds の平均値で center が胸部にずれる問題を修正 (headBone 基準 + sharedMesh.bounds size)。
- `ScanAvatarMeshes` のシーン全体 isolation — 同じシーンに複数アバターが Active な場合、target 以外が裏で描画されて全 cell が似た見た目になっていた問題を修正。

### Changed
- `CaptureExpressionPreview` を `CaptureFacePreview` に統合 — SceneView を動かす副作用がなくなり、再現性のある安定キャプチャに統一。両ツールはバイト単位で同じ出力を返す。

## [0.10.3] — 2026-05-11

### Added
- **Window Capture** ツール群 (Windows Editor のみ): `ListEditorWindows` / `ListMonitors` / `CaptureEditorWindow` / `CaptureMonitor`。AI が Unity 内部の任意 EditorWindow（設定パネル / Inspector / Console / カスタムウィンドウ）や物理モニター全体をスクリーンショット可能。
- Per-monitor DPI 自動検出・補正 (`Shcore.dll!GetDpiForMonitor`)。4K@150% + 1080p@100% のような混在環境でも各モニターのスケールに合わせて正しい物理 px でキャプチャ。
- `maxWidth` パラメータ — 長辺の上限を指定して bilinear ダウンスケール（4K → 1280px で 5 倍以上の容量削減）
- `format='jpg'` + `jpgQuality` — JPG 出力で UI スクショの容量を大幅圧縮
- `saveToPath` — 任意のパスへの追加保存
- `waitForRepaint=true` — リフレクションで `HostView.RepaintImmediately()` を呼び出し、docked タブ切替を 1 回呼び出しで反映

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
