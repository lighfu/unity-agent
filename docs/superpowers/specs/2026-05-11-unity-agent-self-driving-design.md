# UnityAgent Self-Driving — Test & Automation Tools

## Context

UnityAgent には現在「AI 自身が自分の chat 画面に入力して送信する」手段がない。ユーザーは UI テスト（チャット送受信、ボタン状態、セッション一覧）と AI モデル応答の自動化（同じ prompt を異なるモデルに投げて比較、リグレッションテスト、デモスクショ生成など）を CI からスクリプトで実行したい。

このドキュメントは、外部スクリプト/CI が MCP 経由で UnityAgent を駆動できる**低レベルプリミティブ Tool 群**の設計を定める。

要件:
- 外部スクリプト駆動が主ユースケース（AI 自己呼び出しは原則拒否）
- 検証範囲: チャット動作 / UI 状態 / Console エラー / ビジュアル（既存 `Capture*` と組合せ）
- 同期ブロック方式（timeout 付き）— 外部スクリプトが完了を「待って受け取る」モデル
- テスト間の状態分離: 「テスト専用セッション」を都度作成・破棄
- 応答に含める情報: AI テキスト / 呼ばれた Tool 一覧 (name + args + result) / token 使用量 / Console ログ

3 案 (オールインワン / 低レベルプリミティブ / ハイブリッド) を比較した結果、**プリミティブ案** を採用。理由は他レベル（UI チェック・ビジュアル）が既存 Tool との合成で表現できるため、巨大 1 Tool を作らず YAGNI を守る。

## Approach Summary

**新規追加 6 つの `[AgentTool]`** を 2 ファイル並列フラット配置で実装する:

```
Editor/Tools/TestRunnerTools.cs    -- 6 つの [AgentTool] エントリポイント
Editor/Tools/TestRunnerCore.cs     -- internal: TestSession 管理 / 完了待機 / Console フック
```

既存 `Editor/Core/UnityAgentCore.cs` に **internal API 追加**（既存ユーザー入力パスは触らない並行実装）:

```csharp
internal string CreateProgrammaticSession(string label, string providerId, string modelId);
internal Task<TurnResult> SubmitProgrammaticTurn(string sessionId, string prompt, CancellationToken ct);
internal void DiscardSession(string sessionId, bool deleteFile);
internal event Action<string, TurnResult> OnTurnComplete;
```

`TurnResult` は `text` / `toolCalls (List<{name, args, result}>)` / `tokens` / `durationMs` を持つ DTO。

## Public Tools

### 1. `StartTestSession(sessionLabel = "", providerId = "", modelId = "")`

**Risk: Caution** — セッション新規作成、history に記録される。

```
StartTestSession(sessionLabel="MonitorTest", providerId="Anthropic", modelId="claude-opus-4-7")
→ "TestSession sess_3a7f8b9c created (label=[TEST] MonitorTest, provider=Anthropic, model=claude-opus-4-7).
   Use SendTestPrompt('sess_3a7f8b9c', ...) to send messages."
```

- `sessionLabel` 空 → `[TEST] <ISO timestamp>` を自動生成
- `providerId` / `modelId` 空 → 現在のグローバル設定を継承
- セッション ID は `sess_<8 hex>` 形式
- UI History panel に表示される（プレフィックス `[TEST]` でユーザー作成と区別）
- `MAX_CONCURRENT_TEST_SESSIONS = 4` 超過時はエラー

### 2. `SendTestPrompt(sessionId, prompt, timeoutSec = 120, captureConsoleLogs = true)`

**Risk: Caution** — AI 呼び出しでコスト発生。

同期で AI 応答完了まで待機し、結果を JSON で返す。

```json
{
  "completed": true,
  "text": "AI's final assistant message",
  "toolCalls": [
    {"name": "ListMonitors", "args": {}, "result": "Monitors: 2 found...", "durationMs": 12},
    {"name": "CaptureMonitor", "args": {"monitorId": "primary"}, "result": "Success: ...", "durationMs": 340}
  ],
  "tokens": {"input": 1234, "output": 567, "cached": 200, "estCostUsd": 0.0123},
  "consoleLogs": [
    {"level": "Warning", "message": "...", "stackTrace": "...", "timestamp": "10:45:32"}
  ],
  "durationMs": 3450
}
```

エラー条件:
- sessionId 不正/破棄済み → `Error: Test session 'sess_xxx' not found or already discarded.`
- sessionId が非 test session（recursion guard） → `Error: Cannot send to a non-test session (recursion prevention).`
- timeout 経過 → `{completed: false, partial: <accumulated>, reason: "Timeout after 120s"}`（処理は継続。キャンセルしたければ別途 `DiscardTestSession` で殺す）
- API key 未設定 → `Error: Provider 'OpenAI' has no API key configured.`

### 3. `GetSessionState(sessionId)`

**Risk: Safe** — 読み取り専用。

```
GetSessionState("sess_3a7f8b9c")
→ "sess_3a7f8b9c: messages=4 (user=2, assistant=2), processing=false, lastError=null,
    provider=Anthropic, model=claude-opus-4-7, label='[TEST] MonitorTest', age=00:02:34"
```

タイムアウト後の状態確認や、複数 prompt の合間の中間チェックに使用。

### 4. `GetConsoleLogs(sinceLastSeconds = 60, minLevel = "warning")`

**Risk: Safe** — 読み取り専用。

直近 N 秒間に Unity Console に出たログを返す。`SendTestPrompt` の `captureConsoleLogs=true` と独立に、任意タイミングで取得可能。

```
GetConsoleLogs(sinceLastSeconds=30, minLevel="warning")
→
Console logs (last 30s, level >= warning): 3 entries
---
[Error][10:45:32] NullReferenceException: ... at File.cs:123
[Warning][10:45:30] Some warning ...
[Warning][10:45:15] Another warning ...
```

`minLevel`: `"log"` | `"warning"` | `"error"`。

### 5. `SwitchModel(sessionId, providerId, modelId)`

**Risk: Caution** — セッション設定変更。

```
SwitchModel("sess_3a7f8b9c", "OpenAI", "gpt-4o")
→ "sess_3a7f8b9c model changed: Anthropic/claude-opus-4-7 → OpenAI/gpt-4o"
```

会話履歴は保持される。次回の `SendTestPrompt` から新モデルが使われる。プロバイダー API key が無いとエラー。

### 6. `DiscardTestSession(sessionId, deleteHistoryFile = false)`

**Risk: Caution** — `deleteHistoryFile=true` ならディスクからも削除。

```
DiscardTestSession("sess_3a7f8b9c", deleteHistoryFile=false)
→ "sess_3a7f8b9c discarded (history file kept at .../session-3a7f8b9c.json)"
```

UI からも消え、`MAX_CONCURRENT` カウンタも減る。`deleteHistoryFile=true` なら永続化ファイルも削除。

## Threading Model

MCP tool エントリは MCP Bridge から `EditorApplication.delayCall` 経由でメインスレッドに dispatch される既存パターン (`AgentMCPServer.Invoker.cs`) を踏襲。

ただし `SendTestPrompt` だけは特例: メインスレッドで `MRE.Wait` するとエディタ更新が止まり AI ループが進まないため**デッドロック**する。対策:

1. `SubmitProgrammaticTurn` は内部で `EditorApplication.delayCall` 経由でメインスレッドに enqueue（AI ループはメインスレッドで進む）
2. `SendTestPrompt` 自体は **MCP worker thread に残す**（メインへ dispatch しない）
3. worker thread で `ManualResetEventSlim.Wait(timeoutMs)` し、メインスレッドの AI ループ完了 event で Set される

`AgentMCPServer.Invoker.cs` を実装時に確認:
- 既存に「特定 Tool は worker thread で実行」のオプションがあれば流用
- なければ `[AgentTool(RunOnWorkerThread = true)]` のような属性 or per-tool routing を追加 (`SendTestPrompt` のみ対象)
- Unity の `async Task` AI ループは `EditorApplication.delayCall` でメインスレッドに乗っているという前提も実装時に検証する。前提が崩れた場合（AI ループが worker でも進める設計だった場合）はメインスレッド dispatch でも OK

## Recursion Guards

- `SendTestPrompt(sessionId)` は **`StartTestSession` で発行された sessionId** に限定
- 非テストセッション (UI の通常セッション含む) には送信不可 → 明示エラー
- `MAX_CONCURRENT_TEST_SESSIONS = 4` で fork bomb 防止
- セッション ID は `sess_` プレフィックス必須でバリデート

## Critical Files

### 新規作成
- `Editor/Tools/TestRunnerTools.cs` (~250 行想定) — 6 つの `[AgentTool]`
- `Editor/Tools/TestRunnerCore.cs` (~300 行想定) — `Dictionary<string, TestSessionContext>` でセッション管理 / Console フック (`Application.logMessageReceivedThreaded`) / 完了待機 MRE / JSON 整形

### 修正
- `Editor/Core/UnityAgentCore.cs` — internal API 追加: `CreateProgrammaticSession` / `SubmitProgrammaticTurn` / `DiscardSession` / `OnTurnComplete` event / `TurnResult` DTO
- 必要に応じ `Editor/MCP/AgentMCPServer.Invoker.cs` — worker thread 実行オプション追加（既存にない場合）

### ローカライズ
- `localization/tools/*.json` × 22 言語 — 6 エントリ追加（既存パターンに従う）
- `Editor/ToolInfra/ToolDescriptionsJP.cs` — `// ── Test Runner ──` セクション新設し 6 説明追加

### 参照のみ（修正不要）
- `Editor/UI/InputBar.cs` — 既存ユーザー入力パスの参考
- `Editor/UI/ChatPanel.cs` — UI レンダリング参考
- `Editor/Tools/SceneViewTools.cs:23` — `SetPendingImage` パイプライン参考

## Reusable Components

- **`UnityAgentCore`** — チャット処理本体。新 internal API はこの中に追加し、既存 user-input パスとロジックを共通化する余地を残す
- **`Application.logMessageReceivedThreaded`** — Unity 公式の Console フック API。`captureConsoleLogs` の実装に使用
- **`ManualResetEventSlim`** — .NET 標準の同期プリミティブ
- **`System.Text.Json`** または既存の JSON ヘルパ (リポ内で使われているもの) — JSON 整形

## Verification

ビルド & 動作確認手順:

1. **ビルド検証** — `dotnet build` で 0 エラー確認。Unity 2022.3.22f1 で asmdef が解決されること
2. **MVP 動作テスト (手動)**:
   - **Case A**: `StartTestSession()` → sessionId 取得 → `GetSessionState(sessionId)` で 0 メッセージ確認
   - **Case B**: `SendTestPrompt(sessionId, "List monitors")` → `toolCalls[0].name == "ListMonitors"` を確認
   - **Case C**: `SendTestPrompt(sessionId, "Now capture monitor 0")` → 同セッションで継続会話、`toolCalls[0].name == "CaptureMonitor"`
   - **Case D**: `SwitchModel(sessionId, "OpenAI", "gpt-4o")` → 新モデルで再度 prompt → 別 provider の応答確認
   - **Case E**: `GetConsoleLogs(sinceLastSeconds=300, minLevel="error")` → 直近のエラーが返る
   - **Case F**: 短い `timeoutSec=2` で重い prompt → `{completed:false, reason:"Timeout..."}` 確認
   - **Case G**: 不正 sessionId / 非テストセッション ID → recursion guard エラー
   - **Case H**: 4 並列 + 5 つ目 → `MAX_CONCURRENT` エラー
   - **Case I**: `DiscardTestSession()` → UI History から消えること
3. **Recursion test**: AI セッション内で `SendTestPrompt(自分のセッション ID)` → 拒否される
4. **デッドロック確認**: 30 秒応答待機中もエディタが固まらないこと（メインスレッドが進む）

## Out of Scope

- 並列セッション内の同時 prompt（1 セッション内シリアルのみ）
- AI 自己呼び出し（recursion guard で拒否）
- ストリーミングコールバック (Webhook / SSE) — 同期 timeout で十分
- 高度な assertion DSL（外部スクリプト側）
- AgentWebServer (web UI) との統合 — エディタ内 chat のみ
- Visual diff 機能 — 既存 `Capture*` を呼んで外部スクリプト側で比較
- セッション永続化フォーマットの変更
- Tool 結果の構造化 schema 検証（生 string で返す）
