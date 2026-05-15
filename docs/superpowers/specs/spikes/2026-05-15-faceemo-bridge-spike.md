<!-- docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md -->
# FaceEmo Bridge Spike Results

Date: 2026-05-15
Spec: ../2026-05-15-faceemo-realtime-bridge-design.md §11

## 0.2 IExpressionEditor DI resolution
Status: **PASS**
Notes:
- Resolved type: `Suzuryg.FaceEmo.Detail.ExpressionEditor.Presenters.ExpressionEditorPresenter`
  - FaceEmo の `IExpressionEditor` impl は Presenter パターンで、`ExpressionEditorPresenter` が DI 解決される
- `FaceEmoInstaller(launcher.gameObject).Container.Resolve<T>()` の generic Resolve は問題なく動作（既存 `FaceEmoAPI.ApplyToAvatar` と同じパターン）
- assembly 名: `jp.suzuryg.face-emo.appmain.Editor` / `jp.suzuryg.face-emo.detail.Editor`

## 0.3 ExpressionEditorModelFacade access
Status: **PASS**
Notes:
- Field name on `ExpressionEditorPresenter`: **`_modelFacade`**（Plan の placeholder `_model` ではない）
- Access path: NonPublic|Instance field scan
- Bridge は `FirstOrDefault(f => f.FieldType.Name == "ExpressionEditorModelFacade")` で取得しているので **コード変更不要**（field 名に依存しない実装になっている）
- Facade の API は仕様書通り:
  - 読取系: `FaceBlendShapes` / `AnimatedBlendShapes` / `BlinkBlendShapes` / `LipSyncBlendShapes` / `Toggles` / `Transforms` / `AnimatedToggles` / `AnimatedTransforms` (すべて IReadOnlyDictionary プロパティ)
  - 書込系: `SetBlendShapeValue(BlendShape, float)` / `RemoveBlendShapeValue` / `AddAllBlendShapes` / `SetToggleValue` / `SetTransformValue`
  - lifecycle: `OpenTargetClip` / `FetchPreviewAvatar` / `StartSampling` / `StopSampling` / `Dispose`
  - event: `OnThumbnailUpdateRequested`（Plan B のサムネ統合で利用可能）

## 0.4 SetBlendShapeValue live reflection
Status: **PASS (full)**
Notes:
- ExpressionEditor was opened with probe clip via `IExpressionEditor.Open(clip)` (実体は Presenter.Open)
- Facade acquired correctly via `_modelFacade` field
- First face BlendShape: `Body.vrc.v_sil` (Avatar SDK の viseme "silent" position — face SMR が `Body` という命名のアバター)
- `SetBlendShapeValue(blendShape, 100f)` 例外なく成功
- **ExpressionEditor preview の視覚反映を実機で確認済み** (`Body.vrc.v_sil` が値 100 で正しく表示される)。Live モードの双方向同期が end-to-end で動作することが確認された。追加の Repaint や `Sampler.StartSampling` は不要だった

## Decision

| Spike | Result |
|---|---|
| 0.2 IExpressionEditor DI | **PASS** |
| 0.3 Facade access | **PASS** |
| 0.4 SetBlendShapeValue live | **PASS** (preview の視覚反映も実機確認済み) |

### Implementation path
- [x] **Full Live + Degraded** (0.2-0.4 all PASS): Proceed with Phase 2-5 as written.
- [ ] **Degraded only** (any of 0.2-0.4 FAIL): Skip Phase 2 Tasks 2.4/2.5. Bridge.TryOpen still possible (for PreviewWindow display) but SetBlendShape always returns false → Session uses AssetPathFallback only.
- [ ] **Abort** (0.2 itself fails): FaceEmo パッケージ構造が想定と完全に異なる。仕様を見直す。

Selected path: **Full Live + Degraded** (Plan A 全フェーズを書かれた通りに実装。実装完了。`babc233` で master にマージ済み)

---

## B.0 ThumbnailDrawer instantiation + render
Status: TBD (user runs Spike B.0 button)
Notes:
- Drawer types: MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer
- Ctor args: (AV3Setting, ThumbnailSetting) — both public properties on FaceEmoLauncherComponent
- Render driver: GetThumbnail + RequestUpdate + Update() loop until GetCachedThumbnailOrNull returns non-null
- Drives synchronously, no main-thread blocking event loop required
- Expected: Cached after a few Update() iterations (1-10 typical)

## Integration Test Checklist (Plan A 完了後の手動検証)

シーン: FaceEmo + ターゲットアバター（既知の BlendShape を持つ "Body" mesh）

- [ ] AI に「笑顔の表情を作って」と依頼 → ExpressionEditor が開き、Live プレビューが更新される
- [ ] AI が表情編集中、ユーザーが ExpressionEditor のスライダーを動かす → 次の AI ターンで `ReadExpressionFromWindow` がその値を含む
- [ ] FaceEmo をアンインストールした状態で同じ依頼 → "FaceEmo is not installed" エラーが返る
- [ ] launcher を削除した状態で同じ依頼 → "No FaceEmo launcher" エラーが返る
- [ ] TargetAvatar が未設定の状態 → "no TargetAvatar" エラーが返る
- [ ] Bridge.IsHealthy=false を強制（例: FaceEmoInstaller の型名を一時的に書き換え）→ Degraded モードでも `.anim` が更新され、FaceEmo ウィンドウが再読込される
- [ ] `CommitExpressionSession` 後、FaceEmo Menu に新しい Mode が追加されている
- [ ] `ApplyFaceEmoToAvatar` を呼ぶと FX レイヤーに反映される

---

## Plan B Integration Test Checklist (after merge)

シーン: FaceEmo + ターゲットアバター + 表情 1 つ以上が登録された状態

- [ ] `CaptureFaceEmoModeThumbnail('Neutral')` → `Library/UnityAgent/face-thumbnails/Neutral.png` が生成され、顔が正しく描画されている
- [ ] `CaptureFaceEmoGestureTable('Neutral')` → 4×2 のグリッド合成 PNG が生成され、8 セルが暗背景の上に並んでいる
- [ ] `CaptureFaceEmoExMenuThumbnail('Neutral')` → ExMenu サイズのサムネ PNG が生成されている
- [ ] 既存表情を編集 → `CommitExpressionSession` → `RefreshFaceEmoMainView()` → FaceEmo MainView のサムネが新しい表情を反映している
- [ ] FaceEmo をアンインストール → 各 Capture ツールが "Error: FaceEmo is not installed." を返す
- [ ] `FaceEmoThumbnailRenderer.IsHealthy=false` を強制 → Capture ツールが `Error: Thumbnail renderer init failed — ...` を返す（表情変更ツールは健在）
- [ ] サムネ PNG が `Library/UnityAgent/face-thumbnails/` に蓄積する。ファイル名衝突時は上書き
- [ ] 名前に invalid file char を含む Mode（例: `'Test/Slash'`）でも path が正しく sanitize される
