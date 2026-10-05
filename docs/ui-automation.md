# Unity UI の検査と操作

UnityAgent のチャットと MCP から、Editor と実行中のシーンの UI を検査・操作できます。専用ツールは `SearchTools("UIAutomation")` で検索できます。ツールの登録は既存の `AgentTool` 検出経路を使用します。

| 対象 | 検査 | 操作 |
|---|---|---|
| EditorWindow の UI Toolkit | `ListUIAutomationTargets` → `InspectUIToolkit` | `ClickUIToolkitElement` / `SetUIToolkitValue` / `SendUIToolkitEvent` / `ScrollUIToolkitElement` |
| EditorWindow の IMGUI | `InspectIMGUI` → `GetIMGUIInspectionResult`、`CaptureEditorWindow` | `ClickEditorUIAt` / `SendEditorUIEvent` / `TypeEditorUIText` |
| シーンの UI Toolkit / UIDocument | `ListUIAutomationTargets` → `InspectUIToolkit(documentInstanceId: …)` | UI Toolkit と同じツール。Play Mode が必要 |
| Canvas / uGUI / TMP | `ListRuntimeUI` | `ClickRuntimeUI` / `SetRuntimeUIValue` / `SendRuntimeUIEvent` / `InvokeRuntimeUIUnityEvent`。Play Mode が必要 |
| メニュー | `SearchMenu` / `ListMenuCategory` | `ExecuteMenu` |
| Unity を止めているモーダルダイアログ | `GetEditorState` / `AnswerModalDialog(dryRun: true)` | `AnswerModalDialog`。Windows のみ |

## UI Toolkit

まず `ListUIAutomationTargets()` で `windowInstanceId` または `documentInstanceId` を取得します。検査は読み取り専用です。同じタイトルのウィンドウが複数ある場合は、ID か明示的な `matchIndex` が必要です。`matchIndex` は ID 順に並べたタイトル一致の中での位置です。従来の `ListEditorWindows` の番号とは別です。

```csharp
InspectUIToolkit(windowInstanceId: 12345, filter: "Apply");
ClickUIToolkitElement(elementId: "検査結果の elementId");
GetUIActionResult(actionId: "操作結果の actionId");
```

検査結果には要素の型、名前、ラベル、ツールチップ、bindingPath、現在値、USS クラス、enabled / visible / attached / focusable、階層パスと矩形が含まれます。`filter` は型・名前・ラベル・ツールチップ・bindingPath の部分一致です。`includeHidden` の既定値は `true` で、非表示・無効の要素も調べられます。

`elementId` は実際の要素参照に結びついており、兄弟要素の挿入で別のコントロールへずれません。要素がツリーから外れた場合や、名前・ラベル・bindingPath が変わった場合は再検査が必要です。仮想化リストは同じ要素を別の項目に再利用するため、スクロール後には再検査してください。ID は 15 分、16 回の検査、またはドメインリロードで失効します。従来の `ListUIElements` の行番号は操作用 ID ではありません。

`SetUIToolkitValue` は文字列・bool・整数・浮動小数点・enum・Vector2/3/4・Vector2Int/3Int・Color・ObjectField に対応します。ベクトルは `"1,2,3"`、Color は `"#ff8800"` または `"1,0.5,0,1"`、ObjectField は対象オブジェクトのインスタンス ID または `"null"` を指定します。`notify=true` は通常の value setter を通して値変更通知を発生させ、`notify=false` は `SetValueWithoutNotify` を使います。独自型のフィールドには、そのコンポーネント向けの専用ツールが必要です。

クリックは PointerDown / PointerUp の組で送り、Button の通常のクリック処理を通します。無効・非表示・切り離された要素を拒否し、中心がクリップ・遮蔽されている場合は失敗します。スクロール領域外なら `ScrollUIToolkitElement` を使い、再検査してください。独自イベントは `SendUIToolkitEvent` で pointerDown/up/move、keyDown/up、submit、cancel、scroll、focus、blur を送れます。

## IMGUI と座標入力

IMGUI は OnGUI の中で描画されるため、UI Toolkit のような永続的なコントロールツリーがありません。`InspectIMGUI` は Unity の内部 GUI デバッガで一度の再描画を記録し、文字列・描画矩形・スタイル等を取得します。完了後に `GetIMGUIInspectionResult` で結果を読みます。既に別の GUI デバッガが動いている場合は開始しません。

描画命令はコントロールの完全な意味情報ではありません。1 個のコントロールが複数行に現れることも、独自描画が記録に出ないこともあります。ボタンや入力欄の判断が難しい場合は、`CaptureEditorWindow` の画像と照合します。Unity の内部 API が利用できないバージョンでは、その理由を返して画像と座標入力へ案内します。

```csharp
ClickEditorUIAt(x: 80, y: 35, windowInstanceId: 12345);
SendEditorUIEvent("key", windowInstanceId: 12345, key: "Return");
SendEditorUIEvent("scroll", windowInstanceId: 12345, x: 100, y: 100, deltaY: 3);
SendEditorUIEvent("drag", windowInstanceId: 12345, x: 30, y: 40, deltaX: 100, deltaY: 0);
TypeEditorUIText("日本語の入力", windowInstanceId: 12345);
```

座標はウィンドウの描画領域左上を原点とする **Unity 論理ポイント**で、y は下向きです。ドックのタブ帯は含みません。画像の物理ピクセルとは表示倍率の分だけ異なります。たとえば 150% 表示の原寸画像では、画像座標を 1.5 で割って論理ポイントへ変換します。縮小・クロップ画像ではその変換も考慮してください。UIDocument の検査矩形はパネル座標、Canvas の矩形は左下原点の画面ピクセルで、互換ではありません。

座標入力は対象ウィンドウを実行時にアクティブにし、Unity の `SendEvent` を使います。OS のマウスカーソルは動かしません。クリック・ダブルクリック・右クリック・mouseDown/up/move/drag・スクロール・キー・Copy/Paste 等のコマンドを送れます。Unity 自体やパッケージがイベントを無視した場合、画面に変化は起こりません。

文字列を入力するときは、先に入力欄をクリックしてから `TypeEditorUIText` を呼びます。1 回に最大 4096 UTF-16 文字をキーイベントで送り、改行とタブも処理します。Unicode のサロゲートペアは検証します。クリップボードを変更せず、既存テキストを自動的に全選択しません。Tab / Return は対象コントロールによってフォーカス移動や確定として扱われるため、複数行の入力には対象の仕様を確認してください。

## Canvas / uGUI / TMP

`ListRuntimeUI` は読み取り専用で、停止中や非アクティブのシーンオブジェクトも検査できます。インスタンス ID、階層パス、ラベル、コントロール値、操作可否、EventSystem ハンドラ、公開 UnityEvent を返します。操作対象は ID、完全な階層パス、または一意な名前で指定します。同名や同一パスの対象が複数あれば ID を指定してください。

操作は Play Mode が必要です。`ClickRuntimeUI` と `SendRuntimeUIEvent` は既存のアクティブな EventSystem を使います。EventSystem を勝手に作成せず、存在しなければエラーを返します。クリックは指定したオブジェクトのハンドラへ直接送る方式で、別の UI に覆われているかの物理的なヒットテストは行いません。

`SetRuntimeUIValue` は Toggle・InputField・TMP_InputField・Slider・Scrollbar・Dropdown・TMP_Dropdown・ScrollRect の値を通常の setter で変更します。`InvokeRuntimeUIUnityEvent` は検査結果に現れる公開 UnityEvent のみを呼び出し、任意のメソッド名は実行しません。引数は `argumentsJson: "[42]"` のような JSON 配列です。コンポーネントが複数あれば `componentType` を完全な型名で指定します。uGUI / TMP は実行時に検出するため、これらのパッケージを必須依存として追加していません。

## 実行結果と対応範囲

操作ツールは次の Editor 更新へ実行をキューし、`actionId` をすぐ返します。`GetUIActionResult` の状態は queued / running / completed / failed です。**completed はイベント送信の完了**を表し、目的の画面状態になった保証ではありません。再検査やキャプチャで確認し、結果待ちの操作を重ねて再実行しないでください。結果は直近 256 件を保持し、ドメインリロードで失効します。

クリック先がモーダルダイアログを開くと Unity のメインスレッドが停止する場合があります。MCP の `GetUIActionResult` / `GetIMGUIInspectionResult` はメインスレッドを使わず結果キャッシュを読めます。Windows では既存の `GetEditorState` / `AnswerModalDialog` が別スレッドからダイアログの状態取得・応答もできます。UI 操作とフォーカス・再描画を伴う `InspectIMGUI` の分類は Caution、構造検査と結果取得は Safe です。UI 操作を元に戻せるかは、対象 UI のコールバックが Undo を実装しているかによります。

対象は Unity の読み取り可能な UI Toolkit ツリー、IMGUI の取得可能な描画命令、Canvas UI です。閉じたウィンドウ、未生成の UI、描画しない仮想化項目、OS のネイティブファイルダイアログ、独自 GPU 描画の内部状態は自動で完全認識できません。画像からの視覚的な判断は利用中のモデルの画像入力対応にも依存します。

API の仕様確認には [Unity UI Toolkit の Clickable](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/UIElements.Clickable.html) と [Unity 2022.3 GUI デバッガの公開参照ソース](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Editor/Mono/GUIDebugger/GUIViewDebuggerHelper.bindings.cs) を使用しています。

## 検証

追加した UI モジュールを分離した Unity 2022.3.22f1 プロジェクトで検証し、Edit Mode テスト 42 件がすべて成功しました。実際の UI Toolkit / IMGUI ボタン、IMGUI の日本語入力、描画矩形、Play Mode の uGUI Button / Toggle / UnityEvent、古い対象の拒否、別スレッドからの結果取得を確認しています。テストは `Editor/Tests` にあります。

Unity 6000.5.2f1 では追加コードのコンパイルと GameObject / EditorWindow の ID 変換を確認しました。Unity 6 の旧 ID API は共通アダプタ経由で使用します。これらの検証は、既存パッケージ全体とすべての外部依存を含めたビルドの検証ではありません。
