# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- 任意の位置・向きにカメラを置いて 1 枚撮る `CaptureFromPose`。被写体の内側から外を見る画が撮れる (#10)
  - `near` を明示できる。VRChat 相当の 0.01 が既定で、Unity の 0.3 では発火しないニアクリップ由来の不具合を再現できる
  - `positionFromBone` でボーン位置から、`lookAt` で注視点から指定可能
  - `stereoSeparation` で左右 2 枚を 1 枚に並べて出力。片目だけ壊れる不具合の突き合わせ用
  - 使い捨てカメラは `HideAndDontSave` で作って必ず破棄する。シーンは汚れず dirty にもならない
- `RunEditorScript` / `RunEditorScriptAsync` に `members` 引数。クラススコープにヘルパーメソッドやイテレータを宣言できる。動的コンパイラはローカル関数を受け付けないため、これが無いと `RunEditorScriptAsync` が推奨するコルーチン形状をそもそも書けなかった
- 型のメンバを一覧する `DescribeType`。`InvokeMember` の「呼ぶ」に対する「見る」側で、難読化 DLL でも使える (#14)
  - 同名オーバーロードを全部並べ、複数あるものは引数型を完全修飾名で出す。`Ambiguous match found` の原因が一目で分かる
  - 宣言元の型ごとに区切って出力。`memberFilter` と `nameContains` で絞り込める
  - enum は `name = value` と `enum:Full.Type.Value` 形式を併記し、そのまま `InvokeMember` に貼れる
- シェーダーのパスを一覧する `ListShaderPasses`。索引 / パス名 / LightMode タグ / 持っているシェーダーステージを返す (#16)
  - `materialPath` を渡すと、そのマテリアルでパスが有効かどうかも並べる。マテリアル側の切り替えは LightMode 値で引くので、タグのないパスは `n/a` と出す
- Gemini のモデル一覧に `gemini-3.5-flash-lite` を追加 (#19)。Flash-Lite は無料枠の 1 日あたり上限が最も大きく、無料枠で使うなら第一候補になるモデルだった
- EditorPrefs を読み書き・列挙する `GetEditorPref` / `SetEditorPref` / `DeleteEditorPref` / `ListEditorPrefKeys` (#24)
  - 「未設定」と「false / 0 / 空文字」を区別して返す。既定値を getter 側に持たせて「キー削除 = 既定値に戻す」と設計している拡張で、リセットが効いたかどうかを判定できるようにするため
  - EditorPrefs はプロジェクトではなくマシン共通の設定で、API キーやトークンを置いているパッケージが実在する。キー名が資格情報に見える場合 (`apikey` / `token` / `password` / `secret` など) は値を伏せ、書き込みと削除は拒否する
  - `ListEditorPrefKeys` は Windows 専用。Unity にキーを列挙する API が無いため、レジストリの `HKCU\Software\Unity Technologies\Unity Editor 5.x` を直接読み、Unity が付ける `_h<数字>` の接尾辞を剥がす。値は返さない
- メインスレッドを止めているモーダルダイアログのボタンを押す `AnswerModalDialog` (#27)。Windows 専用
  - `GetEditorState` と同じくメインスレッドを待たずにリスナースレッド (Bridge モードでは reader スレッド) で答える。モーダルで全ツールが止まっている最中に呼ぶためのもの
  - `dryRun=true` でダイアログの title / message / ボタン一覧 / 子ウィンドウ一覧を返す。何が出ているか分からないときはまずこれ
  - `button` は表示文字 (`OK` / `Cancel` / `はい`) か左から 0 始まりの index。`EditorUtility.DisplayDialog` は OS ネイティブの `#32770` で `Button` の子ウィンドウを持つことを実測で確認し、`BM_CLICK` で押す (123 ms で消えた)。Win32 のボタンを持たない自前描画のダイアログ向けに `enter` / `escape` / `close` の特別値も用意した
  - `titleContains` で対象を絞る。一致しなければ何も押さずにダイアログの説明だけ返す (別のダイアログを誤って押さないため)
  - 押した内容は Console にも残す。押した後ダイアログが消えたかどうかを返すが、メインスレッドの復帰は `GetEditorState` で確認すること
  - Risk は `Caution` を明示 (`RiskExplicit`)。`Dangerous` にすると既定の `MCPServerExposeRisk` では MCP から見えず、無人セッションから呼べない

### Changed
- `RunEditorScript` / `RunEditorScriptAsync` が、既存ツールで足りる処理を手書きしていた場合に、そのツール名を結果の末尾に添えるようになった。最大 2 件、実在するツールだけを名指しする (#11)
- `RunEditorScript` の既定 usings に `using Object = UnityEngine.Object;` を追加。`System` と `UnityEngine` が同居していて `Object.FindObjectsOfType` が曖昧参照になっていたのを解消 (#13)
- `CompileShaderVariants` / `PreprocessShaderVariant` が、パスを LightMode タグ値でも指定できるようになった (#16)
  - パス名はあてにならない。無名のパスは名前で選べず、名前があっても体系がシェーダーごとに違い (`ForwardAdd` / `FORWARD_DELTA` / `Add`)、同じ名前のパスが 2 つあるシェーダーすら実在する
  - エラーの `Available:` 一覧と、コンパイル結果の各行にも LightMode を併記する。全パスをコンパイルしてバイトコードの大きさから目的のパスを探す必要がなくなる
- 429 (レート制限) のレスポンス本文を解釈するようになった。全プロバイダー共通 (#18)
  - サーバーが指定した待ち時間 (`RetryInfo.retryDelay` / `Retry-After`) を優先して待つ。従来は 1 秒からの倍々を機械的に繰り返すだけだった
  - 60 秒を超える待ち時間を指示されたら待たずに打ち切り、その旨を伝える
  - 1 リクエストあたりの自動リトライは合計 90 秒までとする。サーバー指定を優先した結果、従来 (1+2+4+8+16=31 秒) より長く待たされることがないようにするため
- 設定画面の「モデル一覧を更新」で取得したモデルが、モデル選択のドロップダウンに反映されるようになった (#19)。従来は取得件数のラベルが増えるだけで選択肢は 1 つも増えなかった
  - TTS / 画像生成 / 埋め込みなどチャットに使えないモデルは一覧から除く。カスタムモデル欄に直接書けば従来どおり使える
  - 取得に失敗したときに「更新しました」と表示していたのをやめ、失敗として伝えるようにした
- 無料枠では使えない `gemini-3.1-pro-preview` を、ドロップダウン上で `[課金必須]` と分かるようにした (#19)
- `GetConsoleLogs` / `CountConsoleLogs` が `regex` 引数を受け取るようになった (#21)。`keyword` との併用は AND
  - 正規表現は各エントリの 1 行目 (Console のその行に表示されている文字列) に大文字小文字を無視して当てる。スタックトレースには当てない
  - パターンが不正なときは 0 件ではなく `Error:` を返す。件数でゲートを組んでいる側が、壊れたフィルターを「問題なし」と誤認しないようにするため
  - `CountConsoleLogs` にも `keyword` / `regex` を追加。「自分のプラグインが出した警告だけ数える」が payload なしでできる
- `InspectNDMFErrorReport` が NDMF 自身の 4 段階 severity で集計するようになった (#22)。`severity` と `maxEntries` で絞り込める
  - 先頭に `internalError=N, error=N, nonFatal=N, information=N, total=N, uploadBlocking=true/false` を出す。`uploadBlocking` は `Error` 以上が 1 件でもあるかどうかで、NDMF がアップロードを止める条件そのもの
  - エントリごとにプラグイン名・pass 名・アバター名・メッセージに加えて、エラーが指しているシーン上のオブジェクトの階層パスを並べる
- `TriggerNDMFManualBake` が結果の末尾に実測値を添えるようになった (#23)。既存の文言は変えていない
  - `elapsedMs` / `bakedRootPath` / NDMF の severity 別件数 / bake 中に増えた Console の error・exception・warning 件数
  - Console は開始前後の差分で数えるので、事前に `ClearConsole` を呼ぶ必要がない。bake 中にクリアされた場合はその旨を明示して絶対値で返す
  - NDMF は処理に失敗しても bake 済みオブジェクトを返すため、`Success` の字面は「例外なく終わった」以上の意味を持たない。件数で判定するよう結果にも明記した
- `[AgentTool]` に `RiskExplicit` を追加 (#24)。`Risk = ToolRisk.Caution` を「明示した」と扱わせ、メソッド名による分類を飛ばす
  - `Caution` は属性の既定値でもあるため、値だけでは「指定した」と「未指定」を区別できず、`Delete` で始まるツールが一律 `Dangerous` に落ちて既定の `MCPServerExposeRisk` では MCP から呼べなかった
  - 既定値そのものを変える案は採らなかった。`Risk = ToolRisk.Caution` と書きつつ実際は名前判定に頼っている既存ツールが 20 件以上あり、そちらのリスクが黙って下がるため
- `ExecuteUnityTool` にメタツール名 (`SearchUnityTool` / `DescribeUnityTool`) を渡すと、失敗させずにそのメタツールとして実行するようになった (#25)
  - `DescribeUnityTool` の出力が毎回 `Usage: ExecuteUnityTool(name=...)` で締まるため、呼び出し側は「Unity 側の道具は全部 `ExecuteUnityTool` 経由」と学習して検索までその経路に載せてくる。従来はそこで `not found` になり、似た名前の Unity ツールも無いので `Did you mean` すら出なかった
  - `ExecuteUnityTool` → `ExecuteUnityTool` の入れ子だけは深さ 1 で拒否する。`GetUnityAgentInfo` は通常ツールとして登録してあるので従来から通る
- MCP ブリッジが、ドメインリロードで途切れた実行中の呼び出しを 120 秒のタイムアウトを待たずに即座にエラーで返すようになった (#26)
  - Unity に送信済みで未応答の呼び出しは、接続が閉じた時点で JSON-RPC エラー `-32003` を返す (`-32002` は Unity 側の「main thread blocked」拒否が既に使っている)。`shutdown` 通知 (reason=domain_reload) を受けていれば `Unity reloaded the app domain while this call was running`、通知なしに切れた場合は `Unity connection lost while this call was running` として区別する。後者はクラッシュの疑いとして扱える
  - `error.data` に、中断されたツール名・経過時間・次に呼ぶべきもの (`CompareAssemblyBaseline` / `GetConsoleLogs`) を書く。呼び出し側は自前の復帰ポーリングを組まなくてよい
  - 中断された呼び出しは再送しない。典型例が `RefreshAssetDatabase` のように「リロードを起こした呼び出しそのもの」で、再送すると二重に走る。Unity 切断中に届いた未送信の呼び出しは従来どおりキューに残して再接続時に流す
  - Unity 側の `error` メッセージの `data` もブリッジが捨てずに MCP クライアントへ通すようになった
- リスナースレッドで答えるツールの一覧を `ListenerThreadTools` に集約し、InProc と Bridge の両経路が同じ一覧を見るようにした (#27)。従来 Bridge モードには `GetEditorState` の fast-path 自体が無かった。Bridge モードでも `GetEditorState` が reader スレッドで答える

### Fixed
- `RunEditorScript` / `RunEditorScriptAsync` が `Debug.Log` の出力を捨てたうえで「成功」とだけ返していた問題。戻り値が無いことを明示し、実行中に出たコンソール行をそのまま返すようにした (#12)
- `RunEditorScriptAsync` の説明が、書けないコルーチン形状を「Prefer this」として勧めていた問題。`members` で宣言できるようにしたうえで、実際にコンパイルできる例に差し替えた
- 1 日あたりの無料枠を使い切った 429 でも 5 回リトライし、31 秒待たせた末に必ず失敗していた問題 (#18)。本文の `quotaId` / `quotaMetric` を見て日次枠の枯渇と判定した場合はリトライせず、リセットの時刻まで含めて日本語で説明するようにした
- 429 のレスポンス JSON をそのままチャット欄に流していた問題 (#18)。要約した説明だけを出し、全文は Unity Console のログに回すようにした
- エラーが `Error: Error: ...` と二重に前置されていた問題 (#18)
- 廃止済みや未登録のモデル名が何の警告もなく通っていた問題 (#19)。設定画面のカスタムモデル欄と、429 のエラー文で注意を出すようにした。対象は Google AI のチャットのみで、バージョン付き ID を受け付ける Vertex AI や、未登録が正常な OpenAI 互換 / Ollama には出さない
- Claude / OpenAI 互換プロバイダーの「Max retries exceeded」が到達不能なデッドコードだった問題 (#20)。最終試行の 429 が汎用エラー分岐に落ちて、生の本文がそのまま表示されていた
- ツールバーの履歴アイコンが □ で表示されていた問題。MD3Icon に存在しないコードポイント `\ue889` を直書きしていた (正しくは `MD3Icon.History` = `\ue8b3`)
  - 欠落グリフは Static アトラスでは実行時に補えず、レイアウトのたびにフォント解決へ失敗する。エディターが応答しなくなる事象と相関していたため、MD3SDK 側にも起票した (lighfu/unity-md3sdk#3)
  - あわせて残り 2 箇所の生のコードポイント直書きも `MD3Icon.AttachFile` / `MD3Icon.Stop` に置き換えた。同種の打ち間違いを構造的に防ぐため
- UnityAgent ウィンドウを開くと Unity ごと応答しなくなる問題。`UnityAgentWindow` が `minSize` を設定していなかったため、UI Toolkit が `EditorWindow` の既定サイズ 100x100 で最初のレイアウトを走らせ、その幅にチャット UI を押し込んだレイアウト計算からメインスレッドが戻ってこなくなっていた。`minSize` を 360x300 に設定して、実用上ありえない幅でレイアウトさせないようにした
  - `OnEnable` / `CreateGUI` 自体は 0.3 秒で完了しており、停止していたのはその後のレイアウト計算。描画 (`generateVisualContent`) には 1 要素も到達していなかった
  - 100px 幅で無限に止まる理由自体は未特定。`minSize` は引き金を踏ませない対策で、狭い幅で壊れる脆さは残っている。MD3SDK 側にも起票した (lighfu/unity-md3sdk#4)
- `InspectNDMFErrorReport` が `InternalError` を `Error` に丸めていた問題 (#22)。severity を「`Error` を含む文字列か」で判定していたため、アップロードを止める内部エラーを機械判定できなかった。NDMF の enum 名との完全一致で分類するようにした
- `InspectNDMFErrorReport` のプラグイン名が常に `?` になっていた問題 (#22)。プラグインは `ErrorReport` ではなく各エントリの `ErrorContext` 側にぶら下がっているため、`report.Plugin` は常に取れなかった
- `GetEditorState` の「メインスレッドを待たない」fast-path が、4 メタツール経由の MCP クライアントでは一度も効いていなかった問題 (#27)。クライアントは `ExecuteUnityTool(name="GetEditorState")` の形で呼ぶので届くツール名は `ExecuteUnityTool` になり、名前の一致判定を素通りして stall 判定に落ち、モーダル中は `Unity main thread blocked` で拒否されていた (拒否メッセージにモーダルの説明が含まれていたため、結果的に状態は読めていた)。`ListenerThreadTools` が `ExecuteUnityTool` を 1 段だけ剥がして判定するようにした

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
