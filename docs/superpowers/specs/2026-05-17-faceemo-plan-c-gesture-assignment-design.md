# FaceEmo Plan C — Gesture-Aware Expression Workflow Design

**Date:** 2026-05-17
**Status:** Draft (brainstorming session output, awaiting implementation plan)
**Depends on:** Plan A (`2026-05-15-faceemo-realtime-bridge-design.md`), Plan B (thumbnail integration)
**Successor of:** Plan A/B 13 ホットフィックス群 (`tool-audit-3rd-pass-and-runeditorscript.md`, `aac-buildup-api.md` 系の流れ)

---

## Goal

User が「Milfy_Another に笑顔つけて」と発話したときに、AI が **適切な avatar / Mode / gesture / hand qualifier** を決定し、FaceEmo Expression Editor 上で **関連 BlendShape を自動絞り込み + 3 案 starter** を流し込み、user が最終 slider 調整して **Branch に割当** するまでを通す。**user に最終決定権を委ねる propose-then-act** 路線。

---

## Architecture

5 レイヤー (上から下に流れる):

```
① 対話 Orchestrator  ─── 発話パース / top-level mode 決定 / AskUser 制御
② Discovery Layer    ─── avatar 解決 / FaceEmo 状態判定
③ Convention Layer   ─── intent → 推奨 gesture / hand pose i18n / キーワード辞書
④ Curation Layer     ─── intent → 関連 shape 10-15 個 + variation 3 案生成
⑤ Execution Layer    ─── Plan A の Session API 拡張 (CommitAsBranchOf / CommitInPlace)
```

各レイヤーは **独立 namespace** (`Editor/Tools/FaceEmoPlanC/{Conventions,Discovery,Curation,Orchestration,AgentTools}`)。Plan A の `Editor/Tools/FaceEmoExpressionEditor/` はそのまま残し、`FaceEmoExpressionSession.cs` と `ExpressionSessionTools.cs` のみ拡張する。

---

## Workflow (8 step + manual skip)

```
Step 1  ● Avatar 決定 (auto if 1, AskUser if ≥2)
Step 2  ● FaceEmo セットアップ確認 (auto: New Menu + ConfigureTargetAvatar)
Step 3  ○ Mode 選択 — AskUser (Mode 1 個ならスキップ可)
Step 4  ○ Gesture 選択 — AskUser (8-grid + ★ 推奨)
Step 4a-bis ○ Hand qualifier — AskUser (デフォルト Either、95% スキップ)
Step 4b ○ 既存 binding 検出時 AskUser (上書き / 編集 / Cancel)
Step 4c ○ Branch slot 選択 (advanced のみ surface、通常 BaseAnimation)
Step 5  ● Editor 起動 + BlendShape 絞り込み + starter (やさしい/満面/はにかみ) 流し込み
Step 6  ▲ User が Editor で slider 調整
Step 7  ● 回収 + 保存 + Branch 割当 (atomic 6 step)
Step 8  ● Capture + 報告 + Undo group
```

凡例: ● AI 自動 / ○ AskUser 確認 / ▲ User 操作

### Top-level mode 切替 (毎回 AskUser)

最初に: `[AI 任せ / 編集する]` を AskUser。発話に `任せて/おまかせ/適当/quick` or `編集/調整/詳しく/ちゃんと` が含まれれば自動採択 + 宣言。

### Manual skip (自然言語キーワード検出のみ)

| 要素 | スキップ条件 |
|---|---|
| Avatar (Step 1) | 「`<avatar名>`の」含む、OR 該当 launcher 1 個のみ |
| Mode (Step 3) | 「`<Mode名>`の」含む、OR Mode 1 個のみ、OR 「新規」明示 |
| Gesture (Step 4) | 「`<gesture名>`で」「`<hand pose名>`で」含む |
| Top mode | 上記の `任せ/編集` キーワード |

---

## Layer 1 — 対話 Orchestrator

実装: `Editor/Tools/FaceEmoPlanC/Orchestration/ExpressionWorkflow.cs`

責務:
- 発話を `IntentVocabulary` で抽出: intent / avatar / mode / gesture / hand / mode_preference
- 各 Step で「発話から判明 → skip」「曖昧 → AskUser」を判定
- スキップした Step も **「何を採用したか」明示宣言**

決定原則:
- 推測でやらない (誤動作回避)
- **Propose-then-act** (AI が提案→user が決める→AI が実行): AskUser を挟むが、user が先回りした step は再確認しない
- 透明性最優先

---

## Layer 2 — Discovery

実装: `Editor/Tools/FaceEmoPlanC/Discovery/{AvatarResolver, FaceEmoStateInspector}.cs`

### `ResolveTargetAvatar(promptHint?)` 優先順位

| 優先 | 条件 | 動作 |
|---|---|---|
| 1 | promptHint に名前あり | 厳密一致採用、部分一致は AskUser disambiguate |
| 2 | Active Session の avatarRootName | 継続編集 |
| 3a | activeInHierarchy VRC avatar が 1 体 | 採用 + 宣言 |
| 3b | activeInHierarchy VRC avatar が複数 | AskUser + サムネ |
| 4 | 該当無し | AskUser「Hierarchy から選んでください」 |

「表示されている」= `gameObject.activeInHierarchy && VRCAvatarDescriptor 持ち`。frustum 判定は採用しない。

### `InspectFaceEmoState(avatarRootName)` 状態判定

| 状態 | 条件 | 次のアクション |
|---|---|---|
| NotInstalled | パッケージ無し | `EnsureFaceEmoInstalled` (Plan A 既存) を促す |
| NoLauncher | launcher 無し | `AutoSetupFaceEmoForAvatar` 自動実行 |
| LauncherUnconfigured | TargetAvatar 未設定 | `ConfigureTargetAvatar` 自動実行 |
| Configured | Mode 0 個 | Phase 3 (Mode 選択 = 新規のみ) |
| HasModes | Mode 1+ 個 | Phase 3 (既存 Mode 提示) |

**注**: FaceEmo に「avatar BlendShape を scan して初期 Mode を populate する programmatic API」は存在しない (調査済)。Step 2 の「自動検出」は **launcher 作成 + TargetAvatar 設定** までで止まり、初期 Mode は空。将来 FaceEmo が API 提供した場合の hook ポイントを `AutoSetupFaceEmoForAvatar` 内に残す。

---

## Layer 3 — Convention

実装: `Editor/Tools/FaceEmoPlanC/Conventions/{IntentGestureMap, HandPoseDisplay, IntentVocabulary}.cs`

### intent → 推奨 gesture map (★ 表示用)

| Intent | 推奨 | Intent | 推奨 |
|---|---|---|---|
| smile/happy/joy | HandOpen ✋ | confident/smug | ThumbsUp 👍 |
| angry/mad/pout | Fist ✊ | love/heart | HandGun 🤙 |
| sad/cry/sob | Neutral 😐 | cool/rock | RockNRoll 🤘 |
| surprise/shock | HandOpen ✋ | concentrate | Fingerpoint ☝️ |
| wink/playful | Victory ✌️ | sleepy/tired | Neutral 😐 |

preset 不在 intent は「推奨無し」表示 + 8 grid フラット。

### Hand pose i18n (絵文字 + 英名 + 日本語名併記)

```
Neutral  😐 / ニュートラル
Fist     ✊ / 握り
HandOpen ✋ / パー
Fingerpoint ☝️ / 指差し
Victory  ✌️ / ピース
RockNRoll 🤘 / ロック
HandGun  🤙 / ハンドガン
ThumbsUp 👍 / グッド
```

### Hand qualifier i18n

```
Either   どちらの手でも (Either)
Left     左手のみ (Left)
Right    右手のみ (Right)
Both     両手 (Both)
OneSide  片手だけ (OneSide)
```

### IntentVocabulary キーワード辞書

```
Top mode = auto:          任せて / おまかせ / 適当 / quick / 一発 / ぱっと
Top mode = interactive:   編集 / 調整 / 詳しく / ちゃんと / カスタム / 手で
Hand qualifier:           「左手で」→Left, 「右手で」→Right, 「両手で」→Both
Hand pose 日本語:         「パー」「ピース」「ぐっど」「指差し」「ロック」等 (双方向)
```

拡張可能な辞書として `IntentVocabulary.cs` に集約。ハードコードしない。

---

## Layer 4 — Curation

実装: `Editor/Tools/FaceEmoPlanC/Curation/{CandidateShapeBuilder, ExpressionVariations}.cs`

### `SuggestCandidateShapes(avatarRootName, intent, breadth=wide)` アルゴリズム

```
step 1. seed = PresetMap[intent]                                  // 3 shape (Plan A 流用)
step 2. relatedShapes = [
          FaceProfile の intent カテゴリ (smile→Mouth+Cheek+Eye_positive),
          intent シノニム名前検索 (smile/happy/laugh/joy/grin),
          seed shape の prefix 一致 (mouth_smile_1 → _2, _3, ...)
        ]
step 3. candidates = unique(seed + relatedShapes)[:15]
step 4. variations = GenerateVariations(intent, candidates)        // 4.3 参照
step 5. return { candidates, variations }
```

### Variation 3 案 (intent 別)

| Intent | 案1 (low) | 案2 (mid) | 案3 (high) |
|---|---|---|---|
| smile | やさしい | 満面 | はにかみ |
| angry | 不満 | 激怒 | むすっと |
| sad | しょんぼり | 大泣き | 我慢 |
| surprise | びっくり | 驚愕 | ぽかん |
| wink | 軽い | しっかり | キュート |
| sleepy | うとうと | 熟睡 | 寝起き |
| (汎用) | 弱 | 中 | 強 |

各 variation は **同じ candidate set を共有** (UI 安定)、活性化する shape の subset と値が違う。candidate set は **全 variation の union**。

### Browse UX

```
[AI] ApplyExpressionVariation("やさしい")
     「やさしい笑顔をセットしました」
     [編集する / 次:満面 / 次:はにかみ / Cancel]

[User] 「次:満面」
[AI]   ApplyExpressionVariation("満面") (同 candidate set、値差替)
       「満面に切替えました」
       [編集する / 次:はにかみ / 戻る:やさしい / Cancel]

[User] 「編集する」 → starter として現在値で確定、user に slider 委譲
```

### Preset 不在 intent への対応

優先順位:
1. **シノニム fallback** ("ニコニコ" → smile に正規化、内部マップ)
2. **キーワードのみ検索** (shape name 直接マッチ)
3. (将来) LLM 補強 — **実装は 1 と 2 まで**、3 はコスト判断で別途

### 巻き込み回避保証

- **追加のみ、削除は user 許可**: EditExisting モードでは AI は新 shape を animated 追加するだけ、既存 animated は touch しない
- **explicit override 警告**: candidate と既存 animated が被ったら AskUser
- **variation 切替は session 内のみ**: session 跨ぎで residue 残さない (Bridge.Dispose で clean)

---

## Layer 5 — Execution (Plan A Session 拡張)

修正: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

### 新規メソッド

```csharp
public static Result OpenForBranch(launcherName, modeName, gesture, hand, slot, avatarRootName);
public Result CommitAsBranchOf(modeName, gesture, hand, slot, overwriteMode);
public Result CommitInPlace();
```

### SessionEditMode enum (内部)

```csharp
internal enum SessionEditMode {
    NewMode,           // OpenForNewExpression 経由 (Plan A 既存)
    EditExistingClip,  // OpenForBranch 経由 (上書き編集)
    CreateBranchClip,  // OpenForNewExpression + 後で CommitAsBranchOf
}
```

### OverwriteMode enum

```csharp
public enum OverwriteMode { Ask, Overwrite, EditExisting, Cancel }
```

### AssignClipToGesture の atomic 6 step

```
① Session.GetCurrentValues() → blendshape dict
② new clip 作成 + 値書込 + Save                ← 失敗時 abort
③ FindBranchByCondition → 既存 OR 新規
   新規時: Menu.AddBranch(modeId, conditions)   ← 失敗時 ② の clip 削除
④ Branch.SetBranchAnimation(slot, clip)        ← 失敗時 ③ の Branch 削除
⑤ SerializableMenu.Save + AssetDatabase.SaveAssets ← 失敗時 ④ を旧 clip 参照に戻す
⑥ FaceEmoThumbnailRenderer.RefreshMainView()   ← 失敗時 warn のみ
```

Undo group: `Undo.SetCurrentGroupName("Plan C: smile to HandOpen on Milfy_Another")` で **Ctrl+Z 一回で commit 全体ロールバック**。

---

## AgentTools Inventory

### 新規 10 個 (`[AgentTool(Group = "FaceEmoPlanC")]`)

**Discovery** (`AgentTools/DiscoveryTools.cs`):
- `ResolveTargetAvatar(promptHint?)` → `{avatarRootName, confidence, alternatives[]}`
- `InspectFaceEmoState(avatarRootName)` → state enum + next-action hint
- `AutoSetupFaceEmoForAvatar(avatarRootName)` → atomic New Menu + ConfigureTargetAvatar

**Gesture** (`AgentTools/GestureTools.cs`):
- `ListGestureBindings(launcherName, modeName)` → `[(gesture, hand, slot, clipName), ...]`
- `FindBranchByCondition(launcherName, modeName, gesture, hand)` → branchIndex or -1
- `DetectGestureConflicts(launcherName, modeName, gesture, hand)` → 死ぬ binding リスト
- `AssignClipToGesture(launcherName, modeName, gesture, hand, slot, clipPath, overwriteMode)`

**Curation** (`AgentTools/CurationTools.cs`):
- `SuggestCandidateShapes(avatarRootName, intent, breadth)` → `{candidates[], variations[]}`
- `ApplyExpressionVariation(variationName)` → Active session の Editor 値差替
- `ListExpressionVariations(intent)` → variation 名リスト

### 修正 2 個 (既存 Plan A)

- `OpenExpressionSession(avatarRootName, ...)` — **`editMode` param 追加** (`"new-mode"` / `"edit-existing-clip"` / `"create-branch-clip"`)。省略時 `"new-mode"` で Plan A 互換
- `CommitExpressionSession(...)` — session.EditMode で内部分岐、`CommitAs*` に route

### Guided workflow ラッパーは作らない (YAGNI)

将来必要になったら `StartGuidedExpressionWorkflow(...)` 追加。今は granular tool で AI が組合せ。

---

## Risks & Mitigation

| # | リスク | 緩和策 | 残存 |
|---|---|---|---|
| R1 | アバター取り違え | 全 tool が `avatarRootName` 必須、`FindLauncherForAvatar` (Plan A Hotfix #5) 使用 | 低 |
| R2 | Mode 同時編集 (user が MainView で切替) | Session.Open 時 snapshot、Commit 時再検証 → 違ってたら abort + AskUser | 中 |
| R3 | Branch 割当中の部分失敗 | atomic 6 step + 各 step rollback handler (詳細上記) | 低 |
| R4 | Preview avatar 蓄積 | Plan A Hotfix #6-#10 のブラケット sweep を Plan C session でも適用 | 低 |
| R5 | Registered 7 枠 cap | Commit 前に Registered 数取得、≥6 で AskUser 「Branch 経路に切替?」推奨 | 中 |
| R6 | AI の intent / gesture 誤推定 | Propose-then-act で AskUser、明示的発話のみ skip | 低 |
| R7 | User の編集放置 | timeout 30 min で auto Dispose、明示 `CloseExpressionSession` 用意 | 低 |
| R8 | Asset 孤児 (上書き orphan) | 既定: 残す。`deleteOldClipOnOverwrite` flag を将来追加 | 低 |
| R9 | Domain reload で session 喪失 | Plan A Chat Persistence で会話保持、launcher state から session 再 acquire | 中 |
| R10 | 同 avatar 複数 launcher | Plan A Hotfix #1 の 2-pass scan (configured 優先) | 低 |
| R11 | Variation browse の loop | max 3 cycle で「Editor で直接調整しますか?」誘導 | 低 |
| R12 | 巻き込み禁止違反 (curation が既存 touch) | EditExisting は追加のみ、被ったら AskUser | 低 |
| R13 | AssignClipToGesture の overlap branch (first-match 死) | `DetectGestureConflicts` を先行実行、検出時 AskUser | 低 |
| R14 | FaceEmo MainView 未開 | InspectFaceEmoState で判定、必要なら Launch (Plan A `RefreshWindowIfOpen` 改良) | 低 |

### Logging

`LogTag.FaceEmoPlanC` 追加 (Plan A の `LogTag.MCP` 統一に倣う):
- `INFO`: Phase 遷移、user 選択、ファイル保存 path
- `WARN`: AskUser fallback、conflict 警告、cap 接近、rollback 実行
- `ERROR`: 例外、cancel された atomic 操作
- `TRACE` (DevBuild + DebugMode): blendshape 値 diff、reflection access、preview avatar lifecycle

### 透明性

各 atomic 操作の前後で **明示**:
- 「Milfy_Another の表情パターン1 の HandOpen に smile_20260517_153021.anim を割当ました」
- 「(Either, HandOpen) は既存 F_smile_1 がありましたが、編集モードでそのまま開きました (新 clip は作っていません)」
- 「旧 Branch (Left=HandOpen) は無効化されますがそのまま残ります」

---

## File Structure

```
Editor/Tools/FaceEmoPlanC/                     ← Plan C 新規
├─ AgentTools/
│   ├─ DiscoveryTools.cs
│   ├─ GestureTools.cs
│   └─ CurationTools.cs
├─ Conventions/
│   ├─ IntentGestureMap.cs
│   ├─ HandPoseDisplay.cs
│   └─ IntentVocabulary.cs
├─ Discovery/
│   ├─ AvatarResolver.cs
│   └─ FaceEmoStateInspector.cs
├─ Curation/
│   ├─ CandidateShapeBuilder.cs
│   └─ ExpressionVariations.cs
└─ Orchestration/
    └─ ExpressionWorkflow.cs

Editor/Tools/FaceEmoExpressionEditor/          ← Plan A 既存、拡張
├─ FaceEmoExpressionSession.cs                 ← OpenForBranch + CommitAs* 追加
└─ ExpressionSessionTools.cs                   ← editMode param 追加
```

---

## Testing Strategy

### Unit tests (Curation / Convention)

- `IntentGestureMap`: 全 preset intent で推奨 gesture が決定的に返ること
- `IntentVocabulary`: キーワード抽出が双方向 (`パー` ↔ `HandOpen`)
- `CandidateShapeBuilder`: smile / angry / preset 不在 intent で 10-15 候補返ること、shape カテゴリ正しいこと
- `ExpressionVariations`: 3 案で同 candidate set 共有、値プロファイルが正しいこと

### Integration tests (Plan A Bridge 経由)

- `OpenForBranch` → 既存 clip が Editor に読み込まれ animated dict に表れること
- `CommitAsBranchOf` atomic 6 step の各 step 失敗時に rollback されること (故意 throw)
- `CommitInPlace` で既存 clip が上書き保存され、Branch 参照が変わらないこと

### Manual E2E (Gemini ハイジャック)

- Plan A の Gemini 実テスト方式を踏襲 (memory: `tool-audit-3rd-pass-and-runeditorscript.md`)
- 「Milfy_Another に笑顔つけて」を full flow で実行、各 AskUser 段階で proper 応答
- 異常系: avatar 同名複数、Mode 同時切替、Registered cap 直前、preset 不在 intent
- 後述 Appendix A の E2E trace と完全一致すること

---

## Appendix A — E2E Walkthrough (sanity check)

「Milfy_Another に対して笑顔つけて」発話 → 完成 までの全 trace。

### 前提

Scene に `Milfy_Another` + `Chiffon` の 2 体。Milfy_Another は FaceEmoLauncher 設定済、Mode 1 個 (`表情パターン1`)、Branch 5 個 (HandOpen 空)。FaceEmo MainView は閉。

### Trace

```
[turn 1] User: 「Milfy_Another に笑顔つけて」
AI: ResolveTargetAvatar("Milfy_Another") → HIGH, "Milfy_Another"
AI: InspectFaceEmoState → HasModes, ["表情パターン1"]
AI: 発話に top-mode キーワード無し → AskUser「[AI 任せ / 編集する]」
User: 「編集する」

[turn 2]
AI: Mode 1 個のみ → 自動採択 + 宣言「表情パターン1 を使います」
AI: ListGestureBindings → [(Either,HandOpen,Base,空), (Either,Fist,Base,F_doya_1), ...]
AI: intent=smile → 推奨 HandOpen ★、AskUser [8-grid]
User: クリック HandOpen

[turn 3]
AI: hand 発話無し → デフォルト Either、宣言「(Either, HandOpen) に割当ます」
AI: FindBranchByCondition → -1 (新規)
AI: DetectGestureConflicts → [] (無し)
AI: SuggestCandidateShapes → {15 candidates, 3 variations}
AI: Session.OpenForNewExpression(..., editMode=CreateBranchClip) → Live
AI: ApplyExpressionVariation("やさしい")
AI: 「やさしい笑顔をセットしました」AskUser [編集する/次:満面/次:はにかみ/Cancel]
User: 「次:満面」

[turn 4]
AI: ApplyExpressionVariation("満面")、AskUser [編集する/次/戻る/Cancel]
User: 「編集する」

[turn 5]
AI: AskUser「Editor で slider 調整、完了したら OK」
[User Editor 操作中、AI 待機]
User: 「OK」

[turn 6]
AI: Session.GetCurrentValues() → {(Face,mouth_smile_1):95, ...}
AI: Session.CommitAsBranchOf("表情パターン1", HandOpen, Either, BaseAnimation, Ask)
     atomic 6 step:
       ① Clip 作成 + Save → smile_20260517_154500.anim
       ② FindBranchByCondition → -1
       ③ Menu.AddBranch(modeId, [(Either,HandOpen,Equals)])
       ④ Branch.SetBranchAnimation(BaseAnimation, clip)
       ⑤ SerializableMenu.Save + AssetDatabase.SaveAssets
       ⑥ RefreshMainView
     Undo group: "Plan C: smile to HandOpen on Milfy_Another"
AI: Session.Dispose() + preview avatar 破棄 + bracket sweep
AI: CaptureFaceEmoGestureTable → R:/UnityAgent_Capture_Milfy_Another_gestures_xxx.png
AI: 応答「Milfy_Another の表情パターン1 の (Either, HandOpen) に笑顔を割当ました。
         新規 clip: smile_20260517_154500.anim
         [gesture table 画像]  Ctrl+Z で取消可」
```

### 通過した layer

```
Orchestrator: ✓ top-mode AskUser、キーワードスキップ未発火
Discovery:    ✓ Resolve + Inspect (priority 1 hit、HasModes 状態)
Convention:   ✓ intent=smile → HandOpen ★、Either デフォルト、絵文字 i18n
Gesture:      ✓ List + Find + Detect + Assign (新規 Branch path)
Curation:     ✓ Suggest + ApplyVariation × 2 (browse)
Execution:    ✓ OpenForNewExpression + CommitAsBranchOf + Dispose
Risks:        ✓ R1 avatar 名固定、R3 atomic 6 step、Undo group
```

全 layer を通過、矛盾なし。

### 異常系トレース (R3 リハーサル)

`AssignClipToGesture` の ④ で `SetBranchAnimation` throw した場合:
```
③ で AddBranch 済 → ④ throw
catch: Menu.RemoveBranchAt(branchIndex)   ← ③ rollback
catch: File.Delete(clip path)             ← ② rollback
report: "Branch 更新に失敗、変更は全てロールバックしました"
session state: 残存 (再 Commit 可能)
```

---

## Acceptance Criteria

1. ✅ user が「笑顔つけて」と言うだけで avatar 解決 → Mode 選択 → gesture 選択 → starter → 編集 → 割当 → 報告 が通る
2. ✅ Plan A/B 既存ツールは互換 (旧 workflow も動く)
3. ✅ 全 atomic 操作が Ctrl+Z で取り消せる (Undo group 1 個)
4. ✅ AskUser 時に「現在の bindings」が常に可視化される
5. ✅ FaceEmo Registered 7 枠を Plan C 単体で消費しない (Branch 経路がデフォルト)
6. ✅ 別の関係ない表情を巻き込まない (EditExisting で追加のみ、被ったら AskUser)
7. ✅ R2 / R3 / R9 のリカバリパスを E2E テストで実証

---

## Out of Scope

- FaceEmo の avatar BlendShape 自動 scan (上流に API 無し)
- LLM 動的 shape 推測 (Curation 4.6 の 3 番目、コスト判断で別途)
- Guided workflow ラッパー (YAGNI、将来必要時)
- Branch 並べ替え / priority 操作 (本 Plan は新規 Branch 追加と既存 slot 更新まで)
- Mode 削除 / リネーム (Plan A の既存ツール領域)
