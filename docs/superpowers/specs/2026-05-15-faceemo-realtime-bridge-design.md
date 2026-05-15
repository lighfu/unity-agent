# FaceEmo 必須化 + ExpressionEditor リアルタイム双方向ブリッジ — 設計仕様

- **日付**: 2026-05-15
- **アプローチ**: C（ハイブリッド：facade 直結 + 安全フォールバック）
- **視覚版**: `2026-05-15-faceemo-realtime-bridge-design.html`（同ディレクトリ）

---

## 1. ゴール

### 満たすべき要件

- **FaceEmo 必須化** — FaceEmo が無い／launcher が無い／TargetAvatar 未設定の状態では「表情<u>変更</u>」系ツールを動かせない
- **ウィンドウ中心ワークフロー** — 表情リクエスト時に FaceEmo MainWindow と ExpressionEditor を開き、それを舞台に会話で詰める
- **リアルタイム双方向同期** — AI の BlendShape 操作が ExpressionEditor のライブプレビューに即反映され、ユーザーのスライダー操作も AI が読み取れる
- **壊れにくさ** — FaceEmo バージョン差で reflection が壊れても「表情変更そのもの」は生き残る（degraded mode）
- **プレビュー連携** — FaceEmo の表情プレビュー機能（MainView サムネ、GestureTable、ExMenu サムネ、ExpressionEditor PreviewWindow）と統合する

### 非ゴール（YAGNI）

- FaceEmo パッケージをハード依存化する（reflection／`#if FACE_EMO` 撤廃はしない）
- FaceEmo の MainWindow/ExpressionEditor の UI そのものを置き換える
- BlendShape 解析・カテゴリ分類ロジックの再設計（`FaceProfileTools` 内部はほぼ温存）
- FaceEmo のジェスチャー分岐／メニュー構造編集ツール（`FaceEmoAdvancedTools` の Menu/Branch/Group 系）の再設計
- FaceEmo の Inspector サイズ調整サムネ（D）、サムネ mouseover 拡大（E）の AI 連携（AI 側から触る意味が無い）

---

## 2. アーキテクチャ

4 層構造。「**動かして良いか（ゲート）**」と「**リアルタイムで動かせるか（健全性）**」を別の関心として分離する。

```
┌─ ① AgentTool 表面 ────────────────────────────────────────┐
│  SuggestExpressionShapes / SetExpressionPreviewMulti /    │
│  SearchExpressionShapesV2 / CreateAndRegisterExpression / │
│  OpenExpressionSession (新) / ReadExpressionFromWindow (新)│
│  CaptureFaceEmoModeThumbnail (新) / ...                    │
└──────────────┬────────────────────────────────────────────┘
               │ 表情「変更」系ツールは必ず通す
               ▼
┌─ ② FaceEmoGate (新規・必須通過) ─────────────────────────┐
│  FaceEmo インストール？ / launcher 在る？ / Avatar 設定？  │
│  NG なら統一フォーマットで "Error: ..." + 復旧手順を返す  │
└──────────────┬────────────────────────────────────────────┘
               ▼
┌─ ③ FaceEmoExpressionSession (新規) ──────────────────────┐
│  「いま編集中の表情」を表す高レベル抽象                    │
│  モード判定: Live (Bridge.IsHealthy=true) / Degraded       │
└──────────────┬────────────────────────────────────────────┘
               ▼
┌─ ④ ExpressionEditorBridge (新規) ─────────────────────────┐
│  FaceEmo 内部 reflection を局所化                          │
│  IExpressionEditor.Open(clip) +                            │
│  ExpressionEditorModelFacade.SetBlendShapeValue /          │
│  FaceBlendShapes / AnimatedBlendShapes                     │
│  失敗時 IsHealthy=false → ④' へ                            │
└──────────────┬────────────────────────────────────────────┘
               ▼ Live                ▼ Degraded
   ┌────────────────────────┐   ┌────────────────────────┐
   │ ExpressionEditorModel  │   │ AssetPathFallback      │
   │  Facade (internal)     │   │  .anim 直書き +        │
   │  - SetBlendShapeValue  │   │  RefreshWindowIfOpen   │
   │  - FaceBlendShapes 等  │   │                        │
   └────────────────────────┘   └────────────────────────┘
```

並列に **`FaceEmoThumbnailRenderer`（新規）** が存在し、`MainThumbnailDrawer` / `GestureTableThumbnailDrawer` / `ExMenuThumbnailDrawer` を reflection 経由で扱う。

### 核心の分離

- **ゲート失敗** → ツールが即拒否（FaceEmo が無いから何もできない）
- **Live** → ExpressionEditor の facade に直結（リアルタイム）
- **Degraded** → `.anim` 経由でも編集自体は続行（reflection 壊れても表情変更は死なない）
- **Thumbnail Renderer 失敗** → サムネが出ないだけ、表情変更ツールはブロックしない

---

## 3. コンポーネント詳細

### 3.1 `FaceEmoGate` (新規)

事前条件を一元化する static クラス。表情「変更」系ツールの先頭でこれを呼ぶだけで gating が完成する。

```csharp
// 配置: Editor/Tools/FaceEmoGate.cs
public static class FaceEmoGate
{
    public struct Result
    {
        public bool Ok;
        public string ErrorMessage;                       // Ok=false の時、ツールがそのまま return できる文字列
        public FaceEmoLauncherComponent Launcher;         // Ok=true の時のみ非 null
    }

    // 表情を「変更」するツールが必ず先頭で呼ぶ
    public static Result RequireExpressionEditingReady(string gameObjectName = "");

    // 軽量: 解析系（read-only）はこちらだけ呼ぶ — FaceEmo 必須までは課さない
    public static bool IsFaceEmoInstalled();
}
```

エラーメッセージは **原因 × 復旧手順**を一行に。例:

```
Error: FaceEmo (jp.suzuryg.face-emo) is not installed. Expression editing is only available with FaceEmo. Install FaceEmo via VCC, then retry.
Error: No FaceEmo launcher in scene. Run ExecuteMenu('FaceEmo/New Menu') to create one.
Error: FaceEmo launcher 'FaceEmo_1' has no TargetAvatar. Run ConfigureTargetAvatar('<avatarName>') first.
```

### 3.2 `ExpressionEditorBridge` (新規)

FaceEmo 内部への reflection を 1 ファイルに集中させる。破損面積の最小化。

```csharp
// 配置: Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs
internal sealed class ExpressionEditorBridge : IDisposable
{
    public bool IsHealthy { get; private set; }
    public string LastReflectionError { get; private set; }

    // FaceEmoInstaller(launcher.gameObject).Container.Resolve<IExpressionEditor>()
    // → IExpressionEditor.Open(clip)
    // → ExpressionEditorModelFacade を presenter の private field から取得
    public bool TryOpen(FaceEmoLauncherComponent launcher, AnimationClip clip);

    // PreviewWindow を明示的に起動（F の連携）
    public bool TryOpenPreviewWindow();

    public bool TrySetBlendShape(string smrRelativePath, string shapeName, float value);
    public bool TryGetAnimatedBlendShapes(out IReadOnlyDictionary<(string path, string name), float> values);

    public void Close();
}
```

> 「presenter の private field 経由で facade を掴む」のは **spike 必須項目（§11）**。最悪 `ExpressionEditorPresenter` から取り出せなければ Bridge は `IsHealthy=false` を返し続け、自動的に Degraded モードになる。

### 3.3 `FaceEmoExpressionSession` (新規)

「いま編集中の表情」を表すオブジェクト。Bridge の生死を意識せずツール側から使える高レベル抽象。

```csharp
// 配置: Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
public sealed class FaceEmoExpressionSession : IDisposable
{
    public enum SyncMode { Live, Degraded }
    public SyncMode Mode { get; }
    public string ModeId { get; }
    public AnimationClip Clip { get; }

    public static FaceEmoExpressionSession OpenForMode(string modeName, string gameObjectName = "");
    public static FaceEmoExpressionSession OpenForNewExpression(string displayName, string animSavePath, string gameObjectName = "");

    public void SetBlendShape(string smrRelativePath, string shapeName, float value);
    public IReadOnlyDictionary<string, float> GetCurrentValues();
    public void Commit();
    public void Dispose();
}
```

- **Live モード**: `SetBlendShape` → `Bridge.TrySetBlendShape`。失敗したら 1 回だけ Degraded に降格して再試行（その session 内ではモード固定）
- **Degraded モード**: `SetBlendShape` → `.anim` に直接 `EditorCurveBinding` を書く → `RefreshWindowIfOpen`。FaceEmo ウィンドウが再読込し、ユーザーは結果を見られる

### 3.4 リファクタ対象ツール

`FaceProfileTools.SetExpressionPreviewMulti` と `FaceEmoAdvancedTools.SetExpressionPreview` の中で `smr.SetBlendShapeWeight()` を直接叩いている部分を、**Session 経由**に差し替える。Session 取得には `FaceEmoGate.RequireExpressionEditingReady()` を先頭で必ず呼ぶ。

#### Shape → SMR ルーティング

新 Session API (`SetBlendShape(string smrRelativePath, string shapeName, float value)`) は SMR パスを引数に取る低レベル API。`SetExpressionPreviewMulti` 等の高レベルツールは引き続き `'eye_joy=80;mouth_smile=100'` 形式を受け取り、**FaceProfile の shape index** (`FaceProfileTools.BuildShapeIndex`) で shape 名 → SMR パスを解決してから `Session.SetBlendShape` をペアごとに呼ぶ。つまりルーティングロジックは FaceProfile 側に残り、Session 自体は「どの SMR の何を何に」を指定された通りに書き込むだけ。

#### auto-session のライフサイクル

`OpenExpressionSession` を AI が先に呼ばずに `SetExpressionPreviewMulti` 等を呼んだ場合、Session は**自動的に「新規表情用の暫定セッション」を作る**（auto-session）。挙動:

- 名前は `Tmp_<8桁hex>` で生成、Mode としてはまだ Menu に**登録しない**（メモリ上のみ）
- `Commit` が呼ばれて初めて Menu に登録される。**呼ばれなければ何も残らない**（FaceEmo に Tmp_ Mode が溢れることはない）
- 別の `OpenExpressionSession` / `CloseExpressionSession` が呼ばれた瞬間、auto-session は破棄される
- `.anim` ファイルも `Commit` 時に確定パスに保存される。それまでは一時パス
- AI への応答に必ず `(auto-session: "Tmp_XXXX". Call CommitExpressionSession to persist.)` を含める

### 3.5 新規 AgentTool (セッション系)

| ツール | 役割 |
|---|---|
| `OpenExpressionSession(modeName?, newName?)` | MainWindow ＋ ExpressionEditor を開き、対象 Mode（または新規）の編集セッションを開始 |
| `ReadExpressionFromWindow()` | 現在開いている ExpressionEditor の AnimatedBlendShapes を **`'shape1=80;shape2=100'` 形式の文字列**で返す（`SetExpressionPreviewMulti` の入力にそのまま渡せる対称形式） |
| `CommitExpressionSession(animPath?)` | 編集中セッションを保存し、新規なら FaceEmo Menu に Mode として登録 |
| `CloseExpressionSession()` | セッション終了・破棄。ExpressionEditor は閉じない |

### 3.6 `FaceEmoThumbnailRenderer` (新規)

FaceEmo の 3 種類の `ThumbnailDrawerBase` 派生を reflection でインスタンス化し、`Texture2D` を生成する薄い層。生成した PNG を `Library/UnityAgent/face-thumbnails/` に保存して path を返す。

```csharp
// 配置: Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
internal sealed class FaceEmoThumbnailRenderer : IDisposable
{
    public bool IsHealthy { get; private set; }

    public string RenderModeThumbnail(string modeName);     // A — 単一 Mode サムネ
    public string RenderGestureTable(string modeName);      // B — 8 セル合成
    public string RenderExMenuThumbnail(string modeName);   // C — ExMenu 焼込画像
    public void   RefreshMainView(string modeName = null);  // A — MainView 強制再生成
}
```

Renderer 自身も `IsHealthy` を持ち、reflection 失敗時はツール側で「サムネ無しで応答」にフォールバック。表情変更そのものは止めない。

### 3.7 `ExpressionEditorBridge` への追加 (F)

`TryOpenPreviewWindow()` を Bridge に追加。`IExpressionEditor.Open(clip)` の直後に呼び、FaceEmo の `PreviewWindow` も明示起動する。編集中の大きいプレビューが常に開く。

---

## 4. データフロー

### 4.1 Live モード（標準）

```
User      → AI:        "笑顔の表情を作って"
AI        → Gate:      OpenExpressionSession()
Gate      → AI:        Ok (launcher='FaceEmo_1', avatar='Capra')
AI        → Session:   OpenForNewExpression('Smile', '.../smile.anim')
Session   → Bridge:    TryOpen(launcher, clip)
Bridge    → FaceEmo:   IExpressionEditor.Open(clip) + PreviewWindow
AI        → Session:   SetBlendShape('Body','eye_joy',80) × N
Session   → Bridge:    TrySetBlendShape × N
Bridge    → FaceEmo:   facade.SetBlendShapeValue × N
FaceEmo   → User:      ExpressionEditor が即 live preview を更新
```

### 4.2 Degraded モード（reflection 失敗時）

```
AI        → Session:   SetBlendShape(...)
Session   → Bridge:    TrySetBlendShape(...)
Bridge                  → false (IsHealthy=false)
Session   → AssetPath: AnimationUtility.SetEditorCurve → .anim 書込
Session   → FaceEmo:   RefreshWindowIfOpen
FaceEmo   → User:      ウィンドウ再読込で値が反映（リアルタイムではない）
```

---

## 4-bis. プレビュー統合

FaceEmo には 6 種類のプレビュー関連機能があるが、AI 連携で意味があるのは **A・B・C・F の 4 つ**に絞る。D・E は AI 側からの操作対象にならない。

| # | プレビュー機能 | FaceEmo クラス | AI 連携 | 対応 |
|---|---|---|---|---|
| A | MainView の Mode サムネ | `MainThumbnailDrawer` | AI 応答に表情画像を添付 / 編集後の強制再生成 | 採用 (Renderer) |
| B | GestureTable のセルサムネ | `GestureTableThumbnailDrawer` | ジェスチャー 8 セル一括キャプチャ | 採用 (Renderer) |
| C | ExMenu サムネ | `ExMenuThumbnailDrawer` | VRChat 側メニュー画像の事前確認 | 採用 (Renderer) |
| D | Inspector サイズ調整サムネ | `InspectorThumbnailDrawer` | FaceEmo の UI 用、AI 操作対象外 | 対象外 |
| E | サムネ mouseover 拡大 | MainView 内 UI | マウス操作で自動、AI 制御 API 無し | 対象外 |
| F | ExpressionEditor PreviewWindow | `PreviewWindow` | §4 で既にカバー、明示起動を追加 | 採用 (Bridge 拡張) |

### サムネ生成の流れ

```
AI → Renderer:  CaptureFaceEmoModeThumbnail('Smile')
Renderer → FaceEmo: MainThumbnailDrawer.GetThumbnail(clip)
FaceEmo → Renderer: Texture2D
Renderer: EncodeToPNG → Library/UnityAgent/face-thumbnails/Smile.png
AI: ツール戻り値で path を返却 → 応答に画像として添付
```

### 新規 AgentTool (プレビュー系)

| ツール | 用途 | 対応 |
|---|---|---|
| `CaptureFaceEmoModeThumbnail(modeName)` | 単一 Mode のサムネ PNG を生成、path を返す | A |
| `RefreshFaceEmoMainView(modeName?)` | MainView のサムネを強制再生成 | A |
| `CaptureFaceEmoGestureTable(modeName)` | 8 セル合成 PNG を返す | B |
| `CaptureFaceEmoExMenuThumbnails()` | ExMenu 焼込画像の一覧確認 | C |

**ゲートとの関係**: サムネ生成系ツールも「FaceEmo＋launcher＋avatar」を要求する（§5 のゲート対象）。ただし `FaceEmoThumbnailRenderer.IsHealthy=false` でも**表情変更ツールはブロックしない**。サムネだけ無しで応答する（致命傷にしない）。

---

## 5. ゲートと健全性の判定マトリクス

| FaceEmo インストール | Launcher | TargetAvatar | Bridge.IsHealthy | 表情変更 | 表情解析 | 動作モード |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| ✓ | ✓ | ✓ | ✓ | 許可 | 許可 | **Live** |
| ✓ | ✓ | ✓ | ✗ | 許可（degraded） | 許可 | **Degraded** |
| ✓ | ✓ | ✗ | — | 拒否 + ConfigureTargetAvatar 誘導 | 許可 | — |
| ✓ | ✗ | — | — | 拒否 + New Menu 誘導 | 許可 | — |
| ✗ | — | — | — | 拒否 + FaceEmo インストール誘導 | 許可 | — |

解析系（`AnalyzeFaceBlendShapes`, `SearchExpressionShapesV2`, `IdentifyFaceSmr` 等）は read-only なのでゲートしない。「表情変更」とは Mesh または `.anim` の BlendShape 値を書き換える操作のみを指す。

---

## 6. ツール影響表

| ツール | カテゴリ | 変更 | 備考 |
|---|---|---|---|
| `OpenExpressionSession` | 新 | 新規 | セッションを開く起点 |
| `ReadExpressionFromWindow` | 新 | 新規 | ユーザー手動編集の読み取り |
| `CommitExpressionSession` | 新 | 新規 | 保存＋FaceEmo 登録の薄いラッパ |
| `CloseExpressionSession` | 新 | 新規 | セッション破棄 |
| `CaptureFaceEmoModeThumbnail` | 新 | 新規 | プレビュー A — Mode サムネ PNG 生成 |
| `RefreshFaceEmoMainView` | 新 | 新規 | プレビュー A — MainView サムネ強制再生成 |
| `CaptureFaceEmoGestureTable` | 新 | 新規 | プレビュー B — 8 セル合成 PNG |
| `CaptureFaceEmoExMenuThumbnails` | 新 | 新規 | プレビュー C — ExMenu 焼込画像確認 |
| `SetExpressionPreviewMulti` | FaceProfile | 改修 | Gate + Session 経由。auto-session 対応 |
| `SuggestExpressionShapes` | FaceProfile | 改修 | Gate を先頭に追加 |
| `SetExpressionPreview` (旧) | FaceEmoAdvanced | 改修 | Gate + Session 経由。直接 SMR 操作を廃止 |
| `SearchExpressionShapes` (旧) | FaceEmoAdvanced | 温存 | read-only なので変更なし |
| `CreateAndRegisterExpression` | FaceEmoAdvanced | 改修 | 内部で `Session.Commit` を呼ぶ |
| `CreateExpressionFromData` | FaceEmoAdvanced | 改修 | 同上 |
| `UpdateExpressionAnimation` | FaceEmoAdvanced | 改修 | 同上 |
| `ResetExpressionPreview` | FaceEmoAdvanced | 改修 | Gate 追加 |
| `AnalyzeFaceBlendShapes` | FaceProfile | 温存 | 解析のみ。ゲートしない |
| `SearchExpressionShapesV2` | FaceProfile | 温存 | 同上 |
| `LaunchFaceEmoWindow` | FaceEmoTools | 温存 | MainWindow のみ開く低レベル。`OpenExpressionSession` の前段としても利用可 |
| `ImportExpressions` | FaceEmoAdvanced | 温存 | FX レイヤーからの取り込み。Gate は不要（FaceEmo セットアップの一部） |
| `CaptureFacePreview` | FaceEmoAdvanced | 温存 | シーンメッシュ＋専用カメラの画像取得。FaceEmo 非依存なのでゲートしない |
| `CaptureExpressionPreview` | FaceEmoAdvanced | 温存 | SceneView 依存の旧式。新規利用は非推奨だが互換のため温存 |
| `FaceEmoAPI` Menu/Branch/Group | Core | 温存 | ExpressionEditor とは別系統 |
| `BlendShapeTools` | 汎用 | 温存 | FaceEmo 文脈外。ゲートしない |

---

## 7. ワークフロー Before / After

### Before（現状 — Workflow B）

```
1. AnalyzeFaceBlendShapes('Avatar')
2. SuggestExpressionShapes('Avatar','smile')
3. SetExpressionPreviewMulti(...)       ← SMR を直接操作
4. CaptureFacePreview('Avatar')
5. CreateAndRegisterExpression(...)
6. ApplyFaceEmoToAvatar()
```

FaceEmo ウィンドウは登録後に `RefreshWindowIfOpen` で更新される程度。ユーザーは編集中の様子を見られない。

### After（新フロー）

```
1. [Gate] FaceEmo / launcher / avatar チェック
2. AnalyzeFaceBlendShapes('Avatar')
3. OpenExpressionSession(newName='Smile')        ← MainWindow + ExpressionEditor を開く
4. SuggestExpressionShapes('Avatar','smile')
5. SetExpressionPreviewMulti(...)                ← facade 経由でライブ反映 (F)
6. CaptureFaceEmoModeThumbnail('Smile')          ← AI 応答に画像を貼る (A)
7. (任意) ReadExpressionFromWindow()             ← ユーザー手調整を取り込む
8. (任意) CaptureFaceEmoGestureTable('Smile')    ← 全 8 ジェスチャー一覧 (B)
9. CommitExpressionSession()                     ← 保存＋FaceEmo 登録
10. RefreshFaceEmoMainView()                     ← MainView サムネを最新化 (A)
11. ApplyFaceEmoToAvatar()
```

`BuiltInSkills.cs` の FaceEmo Skill ドキュメントもこの After フローに合わせて全面書き換え。

---

## 8. エラー設計

エラーメッセージは必ず「**原因**」「**次に取るべき具体行動**」の 2 要素を含める。AI が次のツール選択に迷わない設計。

| 状況 | 返すメッセージ（抜粋） |
|---|---|
| FaceEmo 未インストール | `Error: FaceEmo is not installed. Install jp.suzuryg.face-emo via VCC, then retry.` |
| Launcher 不在 | `Error: No FaceEmo launcher in scene. Run ExecuteMenu('FaceEmo/New Menu') first.` |
| TargetAvatar 未設定 | `Error: FaceEmo launcher 'FaceEmo_1' has no TargetAvatar. Run ConfigureTargetAvatar('Avatar') first.` |
| Bridge 失敗（初回） | ツールは成功扱い。応答に `(degraded: live preview unavailable — using .anim path)` 注記 |
| Bridge 失敗（連続） | テレメトリログ。Editor.log にスタックトレース。ユーザー応答は最初の 1 回だけ注記 |
| セッション未開かれているのに編集ツール呼出 | auto-session 作成して続行。応答に `(auto-session created: "Tmp_XXXX")` 注記 |
| Renderer 失敗 | サムネ無しで応答（`(thumbnail unavailable)` 注記）。表情変更はブロックしない |

---

## 9. テスト戦略

### 自動テスト（Unity Test Runner）

- **Gate のユニットテスト**: 5 パターン（インストール無 / launcher 無 / avatar 無 / 全揃 / Healthy=false）をモックで網羅
- **Session の状態遷移**: Live/Degraded 切替、Open→Set→Commit→Close、再 Open での状態クリア
- **AssetPathFallback の `.anim` 出力**: 期待した `EditorCurveBinding` が書き込まれているか
- **Renderer の PNG 出力パス**: 期待されたディレクトリにファイルが生成されているか（モック ThumbnailDrawer で）

### 手動・統合テスト（FaceEmo 実機）

- FaceEmo を入れた空アバターで Live 動作確認
- `ExpressionEditorBridge` をわざと壊し、Degraded への自動降格を確認
- FaceEmo をアンインストールした状態でゲートエラーが正しく返るか
- ExpressionEditor 上でユーザーがスライダーを触った後 `ReadExpressionFromWindow` が正しい値を返すか
- 各種 Capture ツールが PNG を出力し、AI 応答に画像として添付されるか

---

## 10. リスクと緩和

| リスク | 影響 | 緩和 |
|---|---|---|
| FaceEmo の `ExpressionEditorModelFacade` が今後リネーム／非公開化される | Live モードが死ぬ | Bridge を 1 ファイルに局所化 / `IsHealthy` で自動 Degraded / 起動時 reflection 検証 + 警告 |
| presenter から facade を取り出す経路が見つからない | そもそも Live モードに入れない（spike 失敗） | spike 段階で発覚させる（§11）。Bridge を実装せず Degraded のみで進める道へリプラン可能 |
| auto-session の暗黙的な生成で AI が「保存されたつもり」になる | 意図しない Mode 量産 | レスポンスに必ず `(auto-session: "Tmp_XXXX")` 注記 / `BuiltInSkills.cs` に「明示的に `OpenExpressionSession` を先に呼べ」と書く |
| ExpressionEditor が開いていない状態での `ReadExpressionFromWindow` | 空辞書 or エラー | Bridge が "no session" を区別して返す。ツール側は「セッション無し」と「値が全部 0」を区別して応答 |
| 解析ツールがゲートされないことで「FaceEmo 無しでも表情変更できる」と AI が誤解 | 無駄なやり取り | `BuiltInSkills.cs` の Skill ドキュメントを書き換え、解析と変更の境界を明示 |
| `FaceEmoThumbnailRenderer` の reflection が壊れる | サムネが出ない（表情変更は健在） | Renderer の `IsHealthy=false` で各 Capture ツールが「サムネ無し」応答。表情変更ツールはサムネ依存を持たない |
| サムネ PNG が `Library/UnityAgent/face-thumbnails/` に蓄積し続ける | ディスク肥大 | `{modeName}.png` 上書き＋古いファイルの世代管理（最大 N 枚） |

---

## 11. 先行スパイク項目

実装計画の前に Editor で実証しておく:

1. **IExpressionEditor の DI 解決経路** — `FaceEmoInstaller(launcher.gameObject).Container.Resolve<IExpressionEditor>()` が既存の `FaceEmoAPI.ApplyToAvatar` と同じパターンで取れるか
2. **`ExpressionEditorModelFacade` の取得** — `IExpressionEditor` 実装の private field か、presenter 経由で facade インスタンスにアクセスできるか
3. **`SetBlendShapeValue` のライブ反映** — `facade.SetBlendShapeValue` 呼出だけで ExpressionEditor のプレビューが即更新されるか、追加で Repaint や `Sampler.StartSampling` が必要か
4. **`ThumbnailDrawer` の単独インスタンス化** — `MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer` を ctor 直呼びでインスタンス化し、`GetThumbnail(clip)` で Texture2D を取れるか（DI 経由でなくても動くか）

すべて green なら Live + 全プレビュー実装、いずれか red ならその経路は諦め、Degraded only または該当プレビュー無しで先に出す道もアリ。スパイク結果次第で実装計画を分岐させる。

---

## 12. 関連ファイル

### 新規ファイル

- `Editor/Tools/FaceEmoGate.cs`
- `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`
- `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`
- `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`
- `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs` (AgentTool 4 個)
- `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs` (AgentTool 4 個)

### 改修ファイル

- `Editor/Tools/FaceProfileTools.cs` — `SetExpressionPreviewMulti`, `SuggestExpressionShapes` に Gate + Session 経路を追加
- `Editor/Tools/FaceEmoAdvancedTools.cs` — `SetExpressionPreview` / `CreateAndRegisterExpression` 系を Session 経由へ
- `Editor/Tools/BuiltInSkills.cs` — FaceEmo skill ドキュメントを新ワークフロー（Before/After）に書き換え

### 温存（変更しない）

- `Editor/Tools/FaceEmoAPI.cs` の Menu/Branch/Group 系
- `Editor/Tools/FaceEmoTools.cs`（基本 Find/Inspect 系）
- `Editor/Tools/FaceProfileTools.cs` の解析メソッド本体

---

## 13. 進め方

1. スパイク §11.1〜§11.4 を Editor で順に検証
2. 結果に応じて実装計画を作成（`writing-plans` スキル）
3. 実装フェーズ — Gate → Bridge → Session → Renderer → ツール改修 → Skill ドキュメント更新 の順
4. Unity Test Runner + 実機統合テスト
5. 既存 user に告知（破壊的変更: FaceEmo 必須化）
