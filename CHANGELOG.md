# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

## [0.15.0] - 2026-08-19

### Added
- ツール呼び出しの統計ウィンドウ。時系列・ツール別ランキング・カテゴリ別内訳・文字数と所要時間の 4 グラフ。ツールバーのアイコンから開く
- `GetUnityAgentInfo` — バージョン / ツール内訳 / 導入パッケージ / MCP 状態を 1 コールで返す。`detail='full'` で詳細版
- 背景の Unity にスクリプト変更をコンパイルさせる手段。`RefreshAssetDatabase` / `RecordAssemblyBaseline` / `CompareAssemblyBaseline` / `BringUnityToForeground`
- コンパイル・インポートの完了待ち `WaitForCompilation`
- キャプチャを拡張。`CaptureGameView` / `CaptureFromCamera` / `ListCameras` / `CaptureAnimationFrames` / `ListWindows` / `CaptureWindow` / `ListUIElements`
- シェーダー変種の実コンパイル検証 `CompileShaderVariants` / `PreprocessShaderVariant` / `GetShaderVariantCount`
- 画像差分 `DiffImages` と、マテリアルを A/B で振って描き比べる `RenderMaterialAB`
- マテリアル関連 `DumpMaterial`（宣言型つき）/ `DiffMaterials` / `FindMaterials` / `RenderMaterialMask`
- マテリアル割り当てのスナップショット比較 `SnapshotSceneMaterials` / `CompareSnapshots`
- GUID による逆引き参照検索 `FindReferencesTo`
- リフレクション呼び出しの入口 `InvokeMember`（Risk=Dangerous）
- MCP の 120 秒制限を超える処理向けに `RunEditorScriptAsync` / `GetJobResult`
- エディタの状態を軽量に返す `GetEditorState` と、モーダル中の呼び出しを即座に弾くメインスレッド・ウォッチドッグ
- FaceEmo — 条件なしブランチ、条件の削除・変更（`RemoveGestureCondition` / `ModifyGestureCondition`）、アニメーションの一括設定（`SetExpressionAnimations`）、ランチャー間の Mode 複製（`CopyFaceEmoMode`）
- AnimationClip の binding path を一括で付け替える `RebindAnimationClipPaths`。移植先メッシュの BlendShape 実在チェックつき

### Changed
- `CaptureSceneView` に `pivot` / `rotation` / `orthoSize` / `source` / `drawMode` / `lighting` を追加
- `CaptureEditorWindow` に `focusless`（新既定 true）と `bringToFront` を追加。フォーカスを奪わずに撮れる
- `ListEditorWindows` に `activeTab` を追加。背面タブは撮影を拒否する
- `DiffImages` に `maskRegion` と `magentaPixels`、`GetConsoleLogs` に `sinceIndex` を追加
- `RunEditorScript` で `HashSet<T>` / `Dictionary<K,V>` が使えるようになり、`usings` / `additionalReferences` を追加
- `WaitForCompilation` に `assemblyName` と `settleSeconds`、`TriggerDomainReload` の `mode='recompile'` を実際に動くようにした
- ブリッジに `--idle-quit` フラグを追加（既定 5 分、`0` 以下で無効）
- `ModifyBranchProperties` に `branchIndices` を追加。`all` / `0-13` / `0,2,4` でまとめて設定できる
- FaceEmo の一覧に GUID 先頭 8 桁、詳細にアセットパスを併記。同名クリップを識別できるようにした

### Fixed
- Ollama など OpenAI 互換プロバイダで `\uXXXX` がデコードされず、ツールが 1 つも実行できなかった（#5）。Claude API / Claude CLI / Codex CLI にも同じ欠陥があり併せて修正
- スキーマにない引数が黙って捨てられ、意図しないオブジェクトを操作していた（#7）。未知の引数は結果の先頭に警告を出す。引数バインドの大文字小文字も無視するようにした
- FaceEmo のサムネイル系 4 ツールが `gameObjectName` を受け付けず、`GetHierarchyTree` だけ対象引数が `name` だった（#7）
- FaceEmo の一覧で条件の意味が誤って表示され、左右・両手のクリップが出ていなかった（#8）
- Bridge モードで `GetEditorState` が `compiling` / `importing` / `playMode` / `autoRefresh` を一度も更新していなかった
- ブリッジが利用中に自死する、落ちると恒久的に無応答になる、取り残された呼び出しが後から実行される、の 3 点
- MCP の `initialize` が返す `serverInfo.version` が常に `0.0.0.0` だった
- 多角度キャプチャの角度名が反対側を指し、真上・真下でセルの向きが不定になり、セルのラベルが描かれず、グリッド行列がツール間で食い違い、フレーミングが FOV を無視していた
- `CaptureMeshIsolated` がシーン全 Renderer と対象の祖先の `SetActive` を無条件に書き換えていた
- デバッグダンプが固定名で上書きされ、キャプチャでない画像が保持窓を押し出していた

### Notes
- キャプチャ関連の変更は Unity Editor 上での実機未検証（コンパイル検証のみ）。とくにリフレクション経路と `PrintWindow` の実挙動はビルドでは確かめられない
- ブリッジのバイナリは `build.ps1 -All` で 4 RID を再ビルドしたものを同梱している。`main.go` を触ったら再ビルドすること

## [0.14.0] - 2026-08-09

> このバージョンはリリース時に CHANGELOG へ記載されなかったため、git 履歴から後追いで再構成した。

### Added
- MCP サーバーが Streamable HTTP トランスポートに対応（#4）。

### Fixed
- Streamable HTTP の Origin 検証を追加し、IPv6 ループバックと IPv4-mapped ループバックを受け付けるようにした。ブリッジ側の Origin の扱いも本体と揃えた。
- 応答をボディ未読のまま閉じて接続が RST になる問題を修正。
- レガシーな asset package 配置でのブリッジのルート解決に対応。
- GestureManager 連携で `GetModuleFor` に GameObject を渡していた型不一致を修正。

## [0.13.0] - 2026-07-19

### Added
- VRChat SDK 連携ツール群 `VRChatUploadTools`（7 ツール）。認証確認、Control Panel 起動、アップロード済みコンテンツの一覧・詳細、アバター/ワールドの Build & Publish、再アップロードなしのメタ情報更新。SDK の型はリフレクションで解決するので SDK 未導入でもコンパイルできる
- Visibility は既定 private。public 化は `confirmPublic=true` とネイティブ確認ダイアログの二重ゲートで、LLM 単独では公開できない
- `-batchmode` ではアップロードを拒否する。確認ダイアログが自動承認され、人間の同意なしに通ってしまうため
- Skill Management に「URLから取込」を追加。GitHub の raw `.md` URL からスキルを取り込める（blob URL は自動変換、取込前にプレビュー）

## [0.12.1] - 2026-07-09

> このバージョンはリリース時に CHANGELOG へ記載されなかったため、git 履歴から後追いで再構成した。

### Fixed
- NDMF の手動 bake ツールで起きていた `Ambiguous match found` と、prefab を誤って拒否していた問題を解消。

## [0.12.0] - 2026-07-06

> このバージョンはリリース時に CHANGELOG へ記載されなかったため、git 履歴から後追いで再構成した。

### Added
- XML 形式 `<tool>` / `<arg>` のツール呼び出し構文を追加。内蔵スキル群の呼び出し例も XML に統一した。
- ComfyUI 画像生成プロバイダを追加。
- Modular Avatar の Merge Animator / Merge Armature / BoneProxy を AgentTool として公開。メニューの入れ子と icon、Merge Animator の layerType も指定できる。
- VRChat アバター向けに Viseme / リップシンクの自動マッピングと、ViewPosition の自動算出・Eye Look セットアップを追加。
- ローカライズリソースを整備。

### Changed
- システムプロンプト本文を外部 `.md` リソース化し、冗長な静的テキストを圧縮した。
- README を英語主言語にして言語別ファイル（ja / zh-TW / zh-CN）へ分割。

### Removed
- 死蔵していた `SupporterData` / `SupporterShowcaseWindow` を削除。

### Fixed
- 破壊系ツール 6 件が確認ダイアログを迂回していた問題を解消（`DefaultConfirmTools` を追加）。
- 長文が丸ごと非表示になる VisualElement の 65535 頂点上限を回避。
- Write Defaults の案内と、VRChat 公式仕様と食い違っていたスキルの記述を訂正。
- 廃止された Gemini モデルを除去し、Opus 4.8 を追加、価格テーブルを更新。

## [0.11.1] - 2026-06-20

> このバージョンはリリース時に CHANGELOG へ記載されなかったため、git 履歴から後追いで再構成した。

### Added
- ターンごとの変更ログを持ち、部分的な undo（ロールバック）ができるようにした。ドメインリロードをまたいで保持され、編集・再生成の前にはロールバック確認ダイアログを挟む。
- メッセージ操作行を統一し、最後の応答に対する再生成ボタンを追加。処理中でも割り込んで編集できる。
- チャット履歴パネルに削除ボタンとメッセージ数表示を追加。
- 下端追従スクロールと「最新へジャンプ」ボタン。
- Mesh Painter v2: ドラッグ分割 UI、共有テクスチャへのコミット、操作の永続化。
- プロバイダに Claude Opus 4.7 / Gemini 3.5 Flash / GPT-5.5 を登録。

### Fixed
- 履歴が大きいとパネルを開いた瞬間に固まる問題。遅延読み込みと ListView による仮想化で解消し、行要素も再利用するようにした。
- 破損した履歴ファイルの扱い、非表示中のポンプ停止、削除ダイアログのタイトルを修正。
- ドメインリロード後にツールカードが消える問題と、リクエスト処理の耐障害性。
- lilToon の decal `IsDecal` フラグ、Shadow3rd のテクスチャマッピング、docstring のずれを修正。
- アバターパフォーマンス解析が NDMF 未導入環境で壊れないよう versionDefine で保護。

## [0.11.0] - 2026-05-22

### Added — Plan C: Gesture-Aware Expression Workflow
- FaceEmoPlanC 名前空間に 10 ツール。Discovery（`ResolveTargetAvatar` / `InspectFaceEmoState` / `AutoSetupFaceEmoForAvatar`）、Gesture（`ListGestureBindings` / `FindBranchByCondition` / `DetectGestureConflicts` / `AssignClipToGesture`）、Curation（`SuggestCandidateShapes` / `ApplyExpressionVariation` / `ListExpressionVariations`）
- Session API を拡張。`OpenForBranch` / `CommitAsBranchOf`（6 段階のアトミックなコミットとロールバック）/ `CommitInPlace` / `GetCurrentValuesWithPaths`
- `OpenExpressionSession` に `editMode`（`new-mode` / `create-branch-clip` / `edit-existing-clip`）を追加。CreateBranchClip 用に `CommitExpressionSessionToBranch` を新設
- Ctrl+Z でターン全体をロールバックできるようにした
- 設計と計画は `docs/superpowers/` 配下

### Added — Plan B: Thumbnail Integration / Expression Session
- `OpenExpressionSession` / `ReadExpressionFromWindow` / `CommitExpressionSession` / `CloseExpressionSession`
- サムネイル 3 種と MainView 更新 `CaptureFaceEmoModeThumbnail` / `CaptureFaceEmoGestureTable` / `CaptureFaceEmoExMenuThumbnail` / `RefreshFaceEmoMainView`。出力は `Library/UnityAgent/face-thumbnails/`
- 取り残された FaceEmo プレビューアバターを掃除する `CleanupFaceEmoPreviewAvatars`

### Changed
- 表情編集に FaceEmo を必須にした。未導入・ランチャー未設定・TargetAvatar 未設定では実行を拒否する
- 表情の組み立ては FaceEmo の ExpressionEditor ライブプレビューを駆動し、リフレクションが通らない場合は `.anim` 書き込みへ退避する
- `ListFaceEmoExpressions` / `InspectFaceEmo` はシーン上の `MenuRepositoryComponent` を最優先で読むようにした。空のバックアップアセットを返して登録失敗と誤認させることがなくなる
- 同 2 ツールが Unregistered 一覧も出すようにした。Registered が上限 7 件のときの退避先が見えず「消えた」と誤認していた
- ランチャーの自動探索が `TargetAvatar` 設定済みのものを優先するようにした。あわせて `avatarRootName` で対象アバターを指定できる引数を各ツールに追加し、別アバターのメニューへ登録される事故を防ぐ
- コミット系の成功メッセージに `destination` を含め、Unregistered へ退避した場合は回復手順を添えるようにした
- `FaceEmoAPI.SaveMenu` が `RefreshWindowIfOpen` を自動で呼ばないようにした。ドメインリロード後の古い MainView が到達不能な例外を投げるため
- `ExpressionEditorBridge.Dispose` が FaceEmo 側の Dispose 連鎖を呼ぶようにした。呼び出すたびにプレビューアバターが積み上がっていた

### Fixed
- VRCQuestTools 2.7.0 より前のバージョンでコンパイルが壊れる問題。`MaterialSwap` は 2.7.0 で追加された型なので `VRC_QUEST_TOOLS_MATERIAL_SWAP` で切り分けた
- モデル定義を `ModelCapabilityRegistry` に一本化。ドロップダウンの手書きリストと二重管理になっていて食い違っていた（xAI から選べないモデル、容量誤り、未登録の Perplexity モデル、廃止済みの Vertex AI 既定）

### Notes
- Plan B（サムネイル統合）もこのリリースに含む

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
