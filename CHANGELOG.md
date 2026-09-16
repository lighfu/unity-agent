# Changelog

このプロジェクトの主な変更点をこのファイルに記録する。

書式は [Keep a Changelog 1.1.0](https://keepachangelog.com/ja/1.1.0/) に従い、バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/spec/v2.0.0.html) に従う。

## [Unreleased]

### Added
- 画像生成の出力の比率と解像度を指定できるようにした。設定画面の「画像生成プロバイダ」に「出力の比率」「出力の解像度」を追加
  - どちらも既定は「指定しない」。そのときは従来どおり `imageConfig` 自体を送らない。空の `imageConfig` を付けると、対応していないモデルや Vertex の旧エンドポイントが 400 を返すため
  - モデルごとに受け付ける解像度が違う (Flash は 512 / 1K / 2K / 4K、Flash Lite は 1K のみ、Pro は 1K / 2K / 4K)。対応していない組み合わせは送る前にエラーで止め、設定画面でもその場で赤字にする。投げても返ってくるのは 400 だけで、本文からは何が悪かったのか読み取りにくい
  - 比率は公式が受け付ける 14 通り (`1:8` / `1:4` / `9:16` / `2:3` / `3:4` / `4:5` / `1:1` / `5:4` / `4:3` / `3:2` / `16:9` / `21:9` / `4:1` / `8:1`) を並べ、縦長から横長の順にした
  - カスタムモデル欄に一覧に無い名前を書いた場合は検証しない。知らない名前を弾く作りにすると、Google が新しいモデルを出すたびに UnityAgent の更新を待たないと使えなくなるため
  - 比率を指定すると、生成結果が入力したテクスチャと違う形で返る。UV の島に貼り戻す用途では「指定しない」のままが安全
- 推論の強さに `xhigh` を追加した。Codex CLI と、Claude の 4.7 以降・5 系で選べる
  - `ultra` は入れていない。Codex 側では Ultra を選んだときだけマルチエージェント用の別の設定と組で扱われるので、`model_reasoning_effort=ultra` だけ渡しても意図どおりには動かない
  - `max` は Codex CLI には入れていない。一部のモデルの説明にしか出てこず、Responses API 限定という記述もあって裏が取れなかった
- 設定画面に増えた文言の各言語の訳 (25 言語)。「出力の比率」「出力の解像度」「指定しない」と、解像度・コンテキスト上限・提供終了モデルの 3 つの注意文
  - モデル一覧ウィンドウの「可変」も、以前から使っていながら訳が無かったので、あわせて入れた
  - 訳の無い文言は日本語のまま表示される仕組みなので表示が壊れることは無かったが、日本語以外の利用者には読めないままだった
- Claude の現行モデル `claude-opus-5` / `claude-sonnet-5` / `claude-fable-5-1` を追加した。Claude API / Claude CLI の選択肢は、この 3 つと `claude-haiku-4-5-20251001` の 4 本になる
  - `claude-fable-5-1` は一覧の先頭には置いていない。カスタムモデルのスイッチを切ると一覧の先頭に戻る作りなので、先頭に置くと入出力とも最も高価なモデルが黙って既定になる
  - `claude-haiku-4-5` (日付なしの別名) も登録した。ドロップダウンには出さないが、カスタムモデル欄に書かれたときに名前からの推定ではなく実データで思考の指定方法を判断できる
- 推論の強さに `Max` を追加した。Claude の 4.6 以降で選べる
  - Claude が受け付けるのは low / medium / high / xhigh / max の 5 つで、既定は high。Codex CLI は従来どおり 4 段階、他は 3 段階のまま
- チャット画面の入力欄の上に、思考の深さを切り替えるチップを足した。「モデル」の右隣にあり、押すといまのモデルで選べるものが並ぶ。深さを変えるたびに設定画面を開かなくて済む
  - 名前も選択肢もモデルの指定方法に合わせる。強さで指定するモデル (Claude 5 系など) は「推論の強さ」と段階を、トークン数で指定するモデル (Haiku 4.5 / Gemini 2.5 系) は「思考バジェット」とトークン数を並べる。どちらも先頭は「思考しない」で、思考モードのオン/オフもこのチップから切り替わる
  - バジェットのトークン数は、設定画面のスライダーと同じ範囲を 1 回で選べる程度に間引いて並べる。範囲はモデルごとに違うので、範囲外は落として上端だけは必ず出す
  - 思考の指定を受け付けないモデルではチップを出さない。押しても選ぶものが無いため
  - 選ぶとその場で保存してプロバイダーを作り直す。プロバイダーは生成時に思考の設定を受け取る作りなので、作り直さないと次の送信に反映されない

### Changed
- 画像生成の既定モデルを `gemini-3.1-flash-image` にした。従来の既定だった `gemini-2.5-flash-image` は 2026-10-02 に提供が終わる
- 画像モデルの選択肢を現行の 3 つ (`gemini-3.1-flash-image` / `gemini-3.1-flash-lite-image` / `gemini-3-pro-image`) に入れ替え、`gemini-2.5-flash-image` を一覧から外した
  - 設定に保存済みのモデル名は消さない。一覧に無い名前は従来どおりカスタムモデル扱いになり、そのまま API に送られる。ただし提供の終わったモデルを選んだままだと 404 になるまで気付けないので、設定画面に注意を出すようにした
  - モデル一覧ウィンドウの画像モデルの「出力サイズ」に、対応解像度を並べるようにした。従来は一律で「可変」と出していた
- Codex CLI のモデルの選択肢を現行の一覧に入れ替え、`gpt-6-astra` / `gpt-5.6-sol` / `gpt-5.6-terra` / `gpt-5.6-luna` を追加した。`gpt-5.3-codex` はコンテキスト長を 400K、最大出力を 128K に直した (従来は 200K / 16K)
  - モデルごとに使える強さは Codex CLI がサーバーのモデルカタログから受け取るもので、CLI に固定で入っていない。UnityAgent 側は上記の一覧を静的に持つしかない
  - `gpt-5.3-codex-spark` は一覧に出していない。推論フェーズを持たない設計で、コンテキスト長も最大出力も非公開のため一覧の列を埋められない。カスタムモデル欄に書けば従来どおり使える
- 推論の強さの既定を high から medium にした。Codex CLI も OpenAI の `reasoning_effort` も既定は medium で、UnityAgent だけが黙って強い側に倒していた。設定済みの値はそのまま
- `gpt-5.3-codex-spark` を選んだときは `-c model_reasoning_effort` を送らないようにした。推論フェーズを持たない設計で、渡すと弾かれる
- 「(CLIデフォルト)」を選んだ Codex CLI で、思考モードの判定に使うモデルを `gpt-4.1` から `gpt-5.3-codex` に変えた。`gpt-4.1` は思考モード非対応なので、モデルを明示しないかぎり「このモデルでは思考モード非対応」と出て Effort を選べなかった
- 思考の指定方法をモデルごとの値として持つようにした (`ModelCapability.ThinkingApi`)。バジェットで渡すのか強さで渡すのかを、モデル名の綴りではなくこの値で判断する。Claude は同じプロバイダーの中で指定方法が分かれるので、プロバイダー単位の設定では表せなかった
- モデルを指定しない (「(デフォルト)」のままの) Claude API / Claude CLI が仮定するモデルを `claude-sonnet-4-6` から `claude-sonnet-5` にした。コンテキスト長の表示と思考 UI の判定に使う値で、実際にどのモデルを呼ぶかは変わらない
- Claude のレガシーモデルのコンテキスト長と最大出力を現行のドキュメントに合わせた。`claude-opus-4-8` / `claude-opus-4-7` / `claude-opus-4-6` / `claude-sonnet-4-6` は 1M / 128K で、従来は 200K / 64K〜128K と実態より小さかった
  - Antigravity CLI の `claude-opus-4-6-thinking` は 200K のままにした。agy 経由で使えるコンテキストの上限は公開されていないので、第一者 API の値をそのまま当てられない
- Claude API / Claude CLI のドロップダウンから `claude-opus-4-8` / `claude-sonnet-4-6` を外した。現行の 4 モデルだけを並べる方針に合わせたもので、どちらも性能照会用の登録としては残る (`claude-sonnet-4-6` は Antigravity CLI の一覧には引き続き出る)
  - 設定に保存済みのモデル名は消さない。一覧に無い名前はカスタムモデル扱いで、そのまま API に送られる
- 思考モードの 3 つの設定 (オン/オフ・思考バジェット・推論の強さ) をプロバイダーごとに保存するようにした。従来は全プロバイダーで 1 つずつを共有していた
  - 指定方法も受け付ける段階もプロバイダー・モデルで違うので、1 つの値を使い回すと、切り替えるたびに別のプロバイダー向けに決めた値が渡ることになる。コンテキスト上限 (`UnityAgent_{プロバイダー}_MaxContextTokens`) と同じ持ち方に揃えた
  - 保存済みの設定は失わない。プロバイダー単位のキーがまだ無い間は従来の共通キーの値を初期値として読むので、移行した時点では全プロバイダーが従来と同じ値になる。旧キーは消さない
  - チャット画面と設定画面はどちらも同じ経路 (`ProviderRegistry.LoadAllConfigs` / `SaveAllConfigs`) を通す。片方だけ直すと画面ごとに値が食い違う
- 推論の強さの上限をモデル単位にした。従来はプロバイダー単位で、Claude API ならどのモデルでも xHigh / Max が選べた
  - Claude 4.6 世代 (`claude-opus-4-6` / `claude-sonnet-4-6`) は `xhigh` だけを受け付けず `max` は受け付ける。上限を 1 つの数で持つとこの飛びを表せないので、モデルごとに「受け付ける強さの集合」を持たせた。カスタムモデル欄に 4.6 世代を書いて xHigh を選ぶと 400 になっていた
  - 同じモデルでも経路が違えば受け付ける段階が違う (`claude-sonnet-4-6` は Claude API では `max` まで通るが、Antigravity CLI の `--effort` は low / medium / high しか受け取らない)。実際に出す強さは、モデル側の集合とプロバイダー側の集合の積で決める
  - 設定に残っている強さがいまのモデルでは選べないときは、超えない範囲で最も強いものに丸めて送る。丸めずに渡すと範囲外の値として「強さを送らない」扱いになり、強さを上げたつもりが思考モードを切ったのと同じ結果になる
  - モデルが分からないときは従来どおりプロバイダー単位の上限に退避する
- 設定画面の「Effort レベル」を「推論の強さ」に改めた。思考モードの説明も、そのモデルの指定方法に合わせて「推論の強さで指定」「思考バジェットで指定」と出し分ける。どちらの方式で指定するのかが画面からは読み取れなかった
  - 強さで指定するモデルでは、選べる段階をそのまま並べて出す。上限だけを出すと、途中が抜けるモデルで実態と食い違う
- 設定画面でモデルを選び直したときに、思考モードの節も作り直すようにした。指定方法も選べる強さもモデルで変わるのに、モデルを変えても前のモデル向けの UI が残っていた

### Removed
- Codex CLI のモデル一覧から `gpt-5.2-codex` / `gpt-5.1-codex-max` / `gpt-5.1-codex-mini` / `codex-mini` を削除した。いずれも現行の一覧に無い
- Codex CLI のドロップダウンから `gpt-4.1` / `gpt-4.1-mini` / `o4-mini` / `o3` / `gpt-5.2` を外した。どれも OpenAI API 側のモデルで、Codex CLI が配るモデルの一覧には入っていない (`gpt-5.2` は ChatGPT サインイン経由では選べない)。`gpt-4.1` などは OpenAI プロバイダーのドロップダウンには残る
- Claude のモデル一覧から `claude-opus-4-1-20250805` / `claude-sonnet-4-20250514` / `claude-opus-4-20250514` を登録ごと削除した。3 つとも提供が終了 (それぞれ 2026-08-05 / 2026-06-15 / 2026-06-15) していて、選ぶとリクエストが必ず失敗する
  - 従来は `deprecated` として残していたが、実態は「非推奨」ではなく「使えない」。性能照会用に残しても、使えないモデルの数字を見せるだけになる

### Fixed
- 更新内容のダイアログが、文面が長いとウィンドウの外まで伸びて「閉じる」を押せなくなっていた問題。`MD3Dialog` のカードは幅の上限しか持たず中央寄せで絶対配置されるため、本文が伸びるとカードごと上下にはみ出していた。更新内容は、ウィンドウの高さから決めた上限つきのスクロール領域に入れるようにした
  - 設定画面の「更新確認」で出るダイアログと、新しいバージョンを知らせるバナーにも同じ対処をした。どちらも文面をそのまま積んでいて、同じようにボタンが隠れうる作りだった
- コンテキスト上限の設定値が、いま使っているモデルの上限を超えていてもそのまま使われていた問題。`AgentSettings.ResolveMaxContextTokens` が保存値をそのまま返していた
  - 上限の設定はプロバイダー単位 (`UnityAgent_{type}_MaxContextTokens`) でモデル単位ではない。コンテキストの大きいモデルで上限いっぱいに設定したあと、同じプロバイダー内で小さいモデルに切り替えると過大な値が残る。設定画面のスライダーは操作した瞬間にしかクランプせず、しかもパネルを組み立てた時点の上限で丸めるので、保存値は当てにならなかった
  - この値はツールループを止める条件に使う。大きすぎると自前で止まらず API 側のエラーで初めて失敗し、小さすぎると早期に打ち切られる
  - モデルの上限が分かっているときは必ずその範囲に収めるようにした。上限が分からない (0 を渡す) 呼び出し元の扱いは変えていない
  - 設定画面は、保存値がモデルの上限を超えているときに実際に使う値を赤字で出すようにした
- Antigravity CLI (agy) のモデルのコンテキスト長と最大出力が、13 モデルとも仮の値 (128K / 8K) のままだった問題。設定画面のコンテキストが 128K で頭打ちになっていた
  - Gemini 3.8 / 3.7 / 3.6 Flash と Gemini 3.1 Pro は 1,048,576 / 65,536、`claude-opus-4-6-thinking` は Claude 節の `claude-opus-4-6` と同じ 200,000 / 128,000、`gpt-oss-120b-medium` は公開されている 131,072 / 131,072 にした
  - 強さ違い (`-high` / `-medium` / `-low`) は実モデルが同じなので同じ値になる。思考バジェットの列は 0 のまま。agy は `--effort` で強さを渡すので、バジェットのスライダーではなく Effort の UI を出す必要がある
- モデルを選ばずに CLI のプロバイダーを使うと、コンテキストの上限が 128K に固定され、推論の強さも選べなくなっていた問題
  - モデル欄が「(CLIデフォルト)」のとき、能力の判定に空のモデル名を渡していた。受け取った側はそれを「不明なモデル」として 128K・思考モード非対応の既定値で返すので、Antigravity CLI では設定画面に「このモデルでは思考モード非対応」と出て Effort の選択肢自体が消えていた
  - モデル未指定のときに何を仮定するかを `ProviderRegistry.RepresentativeModel` に集約し、設定画面とチャットの両方が同じモデルを見るようにした。片方だけ直すと、画面に出る上限と実際にツールループを止める上限が食い違う
- Claude API で思考モードを有効にすると、現行のモデルではリクエストが必ず失敗していた問題。モデルによらず `thinking:{"type":"enabled","budget_tokens":N}` を送っていた
  - Claude 4.6 以降は `thinking:{"type":"adaptive"}` と `output_config.effort` で深さを指定する方式に変わっていて、Opus 4.7 以降に `budget_tokens` を送ると 400 で拒否される。逆に Haiku 4.5 以前に `adaptive` を送っても 400 になる
  - 従来のドロップダウンの先頭は `claude-opus-4-8` だったので、既定のまま思考モードを入れると必ず失敗する状態だった
  - 思考の中身は `display:"summarized"` を明示して受け取るようにした。現行モデルの既定は `omitted` で、そのままだと thinking ブロックが空文字で流れてきて、画面の思考欄に何も出ない
- Claude API の `max_tokens` が、思考バジェットによってはモデルの最大出力を超えていた問題。バジェット + 4096 まで膨らませていたので、最大出力 64K の Haiku 4.5 にバジェット 128K を渡すと 132K を送っていた
  - `max_tokens` はモデルの最大出力そのものを送り、バジェットは 1024 以上・`max_tokens` 未満に収めるようにした。どちらも API 側の要件
- 設定画面の思考モードの UI が、Claude API ではモデルによらずバジェットのスライダーだった問題。選んでいるモデルの指定方法に合わせて、強さで指定するモデルでは Effort、バジェットで指定するモデルではスライダーを出す

## [0.16.0] - 2026-09-16

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
- 後で出るモーダルへの応答を事前登録する `SetModalAutoAnswer` / `ListModalAutoAnswers` / `ClearModalAutoAnswers` (#27)。Play 遷移や NDMF / VRCFury のビルドのように「いつ出るか分からない」場面向け
  - 専用スレッドが 250 ms ごとにモーダルを見て、`titleContains` / `messageContains` に一致したら `AnswerModalDialog` と同じ経路で押す。押した内容は Console と `GetEditorState` の `lastAutoAnswer` に残る
  - `titleContains` か `messageContains` のどちらかを必須にし、全ダイアログに一致するルールは受け付けない。`ttlSeconds` (既定 600、最大 3600) で必ず期限切れになる
  - ルールは `Library/UnityAgent/ModalAutoAnswers.json` に持つ。想定場面の Play 遷移がドメインリロードそのもので、静的フィールドでは登録が消えるため。リロードのたびに読み直してポーラーを張り直す
  - `GetEditorState` に `autoAnswerRules` / `lastAutoAnswer` の 2 行を追加。モーダルの verdict に `AnswerModalDialog` の案内も添えた
- VRChat SDK の Build & Test を開始して受付番号を返す `StartVRChatBuildTest` と、進捗・結果を取る `GetVRChatBuildTestResult` (#29)。実アバターのビルドは数分かかり、MCP の 1 呼び出し (120 秒) では完了まで待てないため、開始と結果取得を分けた
  - SDK の公開 API `IVRCSdkAvatarBuilderApi.BuildAndTest` でビルドする。NDMF などのビルド処理は Control Panel のボタンを押したときと同じく走り、SDK のローカルテスト用アバター一覧に追加される。アップロードはしない
  - `StartVRChatBuildTest` は返事を先に返し、SDK の呼び出しは数 tick 後に行う。SDK は `Task.Delay(100)` の直後にメインスレッドを数分占有するエクスポートを始めるので、同じ tick で呼ぶと、エディタの tick が遅いとき (背面にあるときなど) に受付番号の返事がビルド完了まで届かなくなる
  - 結果は `starting` / `running` / `finishing` / `succeeded` / `failed` / `lost`。実行中は経過時間・SDK のビルド状態・直近の進捗メッセージ、完了後はエラー内容・バンドルのパス・NDMF の severity 別件数・ビルドが出した Console の error / exception / warning 件数と、その行を読むための `sinceIndex` を返す
  - Console の件数は、開始時に書いた目印の行より後ろを数える。前後の件数の差では、ビルド中にクリアされたあと行が増えると合計が前より多くなってクリアを見逃し、負の値や失敗したビルドのエラーを隠す値になるため。目印が消えていればクリアされたと明示する
  - SDK はビルドの大半でメインスレッドを占有するため、`GetVRChatBuildTestResult` はリスナースレッドで答える。`waitSeconds` (最大 110 秒) で完了を待てる。案内する値は 50 秒にした。MCP クライアント側が 120 秒より手前で呼び出しを打ち切ることがあり、実測では 100 秒の待ちがクライアント側でタイムアウトし、55 秒は通った。モーダルが出ていれば結果にその名前を出す (質問のダイアログなら、ビルドはそこで止まっている)
  - 受付番号はドメインリロードで消えるが、記録を SessionState に残す。リロード後に問い合わせると、完了済みならその結果を、実行中に中断されたなら `lost` を返す。記録はメモリにも写しておき、知らない受付番号への返事もメインスレッドを待たずに返す (ビルドの後ろに並んで 120 秒待たされないように)
  - batch mode では拒否する (SDK やビルド処理のダイアログが自動で承認されるため)。ビルドターゲットが Windows / Android / iOS 以外なら開始前に拒否する。SDK はこの確認をビルド状態を Building にした後で行い、状態を戻さずに例外を投げるため
  - 非アクティブなアバターも開始前に拒否する。SDK はこれをエクスポートを最後まで終えた後の検証で初めて弾くので、実アバターでは数分のビルドが結果の分かりきった失敗に使われる
  - Risk は `Caution` を明示。ローカルでビルドするだけでアップロードはしないので、既定の `MCPServerExposeRisk` で呼べるようにした
  - `GetEditorState` に、ビルド実行中だけ `vrchatBuildTest` 行を出す。ビルド中は実行中のツールが無いままメインスレッドが止まるので、原因の分からない停止に見えないようにするため
- チャットのプロバイダーに Antigravity CLI (`agy`) を追加。Google が 2026-06-18 に個人アカウントでの Gemini CLI の提供を終え、その後継として出した CLI
  - 応答 1 回ごとに agy をヘッドレスモード (`--input-format stream-json --output-format stream-json`) で起動する。会話を 1 行の JSON にして stdin に渡して閉じ、`step_update` の `text_delta` を逐次表示し、`result` の `response` を最終結果にする
  - agy にはシステムプロンプトを渡す口が無い。Gemini CLI で `GEMINI_SYSTEM_MD` に置いていた指示は、本文の先頭に入れる
  - agy の出力は Go の JSON で、`<` `>` `&` が `\u003c` などにエスケープされて届く。行ごとに JSON として読み直さないと、ツール呼び出しの `<tool>` を取りこぼす
  - 推論の強さは `--effort` (low / medium / high) で渡す。`gemini-3.8-flash-high` のように強さを含むモデル名のときは渡さない
  - モデルの選択肢は agy 1.1.20 の `agy models` の一覧。agy 自身のツールの許可をまとめて通す `--dangerously-skip-permissions` は付けない
  - MCP 設定画面に、agy 向けの登録コマンド (`agy mcp add --header ...`) と `~/.gemini/config/mcp_config.json` の書き方を追加。agy は `url` ではなく `serverUrl` を読むので、Claude 向けの例はそのままでは使えない
- 統計ウィンドウとモデル案内の UI 文字列 39 件 (英語・簡体字・繁体字) と、v0.13.0 以降に増えたツール 37 件の説明 (日本語・簡体字・繁体字) の翻訳 (#28, @CQMHV)。取り込み時に、37 件へ訳元の英語のハッシュを付けた

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
- Bridge モードで、リスナースレッドで答えるツールを reader スレッドではなく ThreadPool で実行するようにした (#29)。`GetVRChatBuildTestResult` の `waitSeconds` で reader が止まると、後続の呼び出しが全部その間待たされるため。あわせて、そこで出た例外が reader まで上がって接続ごと切れることもなくなった
- Gemini CLI の表示名を「Gemini CLI (legacy)」にし、設定画面の説明に、個人アカウントでは使えなくなったことと移行先 (Antigravity CLI) を書いた。API キーや法人向けの利用者は引き続き使えるので、プロバイダーとしては残している

### Removed
- 使われていなかった `ToolDescriptionsJP.cs`。日本語のツール説明は `localization/tools/ja.json` から表示されていて、このクラスはどこからも参照されていなかった

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
- `TriggerVRChatBuildTest` が必ず失敗していた問題 (#29)。存在しないメニュー `VRChat SDK/Build & Test New Build` を実行しようとしていた (VRChat SDK 3.10.4 には無い)。名前はそのままに `StartVRChatBuildTest` と同じ処理へ付け替え、受付番号を返すようにした
- ツール説明の翻訳が、英語の説明を書き換えたあとも古いまま表示されていた問題。訳はツール名で引くため、説明が変わっても古い訳がそのまま使われていた。訳ごとに「どの英語から訳したか」を表すハッシュを `localization/tools/source-hashes/{lang}.json` に持たせ、英語が変わっていれば英語の説明を表示するようにした
  - 既存の訳には、最後に一括翻訳した 2026-06-22 時点の英語のハッシュを付けた。それ以降に説明が変わった 21 ツールの訳は、22 言語すべてで英語表示に戻る
  - 日本語・簡体字中国語・繁体字中国語は、英語が変わっていた 10 件と、訳が無かった #24 / #27 / #29 のツール 11 件の計 21 件を、今の英語から訳し直した
  - AI 翻訳と翻訳のインポートは、訳した時点の英語のハッシュを記録する。英語が変わった訳は未翻訳として再翻訳の対象になり、翻訳管理画面の進捗にも数えない
  - 翻訳ファイル本体の形式は変えていない。ハッシュの記録が無い訳はこれまでどおり信用する
  - UI 文字列は日本語の原文そのものがキーなので、この問題は起きない

## [0.15.0] - 2026-08-19

> キャプチャ関連の変更は、リリースの時点ではコンパイルの確認だけで、Unity エディタ上では動かしていない。とくにリフレクション経由の処理と `PrintWindow` の実際の挙動は、ビルドでは確かめられない。

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
- ブリッジのバイナリを `build.ps1 -All` で 4 環境向けに作り直して同梱した

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

### Added
- ジェスチャーに連動する表情ワークフロー。FaceEmoPlanC 名前空間に 10 ツール
  - 探索: `ResolveTargetAvatar` / `InspectFaceEmoState` / `AutoSetupFaceEmoForAvatar`
  - ジェスチャー: `ListGestureBindings` / `FindBranchByCondition` / `DetectGestureConflicts` / `AssignClipToGesture`
  - 仕上げ: `SuggestCandidateShapes` / `ApplyExpressionVariation` / `ListExpressionVariations`
- 表情セッションとサムネイル連携
  - `OpenExpressionSession` / `ReadExpressionFromWindow` / `CommitExpressionSession` / `CloseExpressionSession`
  - サムネイル 3 種と MainView の更新 `CaptureFaceEmoModeThumbnail` / `CaptureFaceEmoGestureTable` / `CaptureFaceEmoExMenuThumbnail` / `RefreshFaceEmoMainView`。出力は `Library/UnityAgent/face-thumbnails/`
  - 取り残された FaceEmo プレビューアバターを掃除する `CleanupFaceEmoPreviewAvatars`
- Session API を拡張。`OpenForBranch` / `CommitAsBranchOf`（6 段階のアトミックなコミットとロールバック）/ `CommitInPlace` / `GetCurrentValuesWithPaths`
- `OpenExpressionSession` に `editMode`（`new-mode` / `create-branch-clip` / `edit-existing-clip`）を追加。CreateBranchClip 用に `CommitExpressionSessionToBranch` を新設
- Ctrl+Z でターン全体をロールバックできるようにした

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

## [0.10.6] - 2026-05-15

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Fixed
- Editor のツール群を 4 回に分けて監査し、挙動の不具合を多数直した
  - 失敗したのに「成功」と返していたツール
  - テクスチャの編集・合成・生成ツールのメモリリーク
  - 検査系ツールがテクスチャの Read/Write 設定を書き換えたまま戻していなかった
  - メッシュ・ウェイトの編集ツールが、編集のたびに不要なアセットを作り続けていた
  - lilToon マテリアルの変更が保存されないことがあった
- `RunEditorScript` が長いコマンドラインで失敗していた
- 多くのツールの説明文を、実際の挙動に合わせて訂正した

## [0.10.5] - 2026-05-12

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- エージェントからアセンブリを再読み込みさせる `TriggerDomainReload`

### Changed
- AnimatorAsCode 連携を Buildup API に作り替えた。AI が AAC の C# を直接書く形になる
- `ConfigureCollider` の `isTrigger` で -1 を「変更しない」として扱うようにした（ほかの引数と揃えた）
- `SetParent` が localRotation をリセットする仕様を説明文に明記した

### Fixed
- AvatarMask 系のツールが、綴りの違う部位名や未登録のパスを黙って読み飛ばしていた。エラーを返すようにした
- `SetAvatarMaskTransformsFromAvatar` を、Unity 公式の Import Skeleton と同じ結果になるようにした
- `CreateBlendTree` が古い BlendTree のサブアセットを残していた
- `AacExecuteScript` が、Windows のコマンドライン長の上限（32K）に引っかかって失敗していた。渡す参照アセンブリを絞った
- 真偽値の引数の解釈がツールごとにばらばらだったのを、1 か所に揃えた
- 空の catch で握りつぶしていたエラーを、ログに出すようにした

## [0.10.4] - 2026-05-11

### Added
- TestRunner ツール群 `StartTestSession` / `SendTestPrompt` / `GetSessionState` / `SwitchModel` / `DiscardTestSession`。外部の CI やスクリプトから、MCP 経由で UnityAgent を動かせる。テストセッションは開いている UnityAgent ウィンドウにそのまま表示され、プロンプトと AI の応答を通常のチャット画面で確認できる
- 特定のメッシュや GameObject だけを残して、前・左・右・後ろの多角度から撮る `CaptureMeshIsolated`。非アクティブな衣装メッシュも、一時的にアクティブにして撮れる
- キャプチャ系ツール（`CaptureSceneView` / `CaptureMultiAngle` / `CaptureFacePreview` / `CaptureExpressionPreview` / `ScanAvatarMeshes`）に、画質の引数 `maxWidth` / `format='png'|'jpg'` / `jpgQuality` / `saveToPath` を揃えて追加した。既定の解像度は 512 から 1024 に上げた
- すべてのキャプチャを `%TEMP%\unity-agent-last-capture.{png,jpg}` にも書き出す。MCP の画像添付を表示できないクライアントでも、このファイルを読めば確認できる
- `ScanAvatarMeshes` の各セルに `[N] メッシュ名` のラベルを描き込む

### Changed
- `CaptureExpressionPreview` を `CaptureFacePreview` に統合した。SceneView を動かす副作用がなくなり、両ツールはバイト単位で同じ画像を返す

### Fixed
- `CaptureMultiAngle` の範囲計算に非アクティブな衣装メッシュまで入り、カメラが遠ざかっていた。アクティブな Renderer と、メッシュ自身の bounds だけを使うようにした
- `CaptureFacePreview` のフレーミングが胸元にずれていた。頭のボーンを基準にした
- `ScanAvatarMeshes` で、同じシーンにアクティブなアバターが複数あると対象以外も写り込み、全セルが似た見た目になっていた

## [0.10.3] - 2026-05-11

### Added
- Windows エディタ向けのウィンドウキャプチャ `ListEditorWindows` / `ListMonitors` / `CaptureEditorWindow` / `CaptureMonitor`。Unity 内の任意の EditorWindow（設定パネル・Inspector・Console・自作のウィンドウ）や、物理モニター全体を撮れる
  - モニターごとの DPI を検出して補正する（`Shcore.dll!GetDpiForMonitor`）。4K@150% と 1080p@100% が混在していても、それぞれのモニターの実ピクセルで撮れる
  - `maxWidth` で長辺を縮小し、`format='jpg'` と `jpgQuality` で容量を抑えられる。`saveToPath` で任意の場所にも保存する
  - `waitForRepaint=true` で、ドッキングされたタブの切り替えを 1 回の呼び出しで反映してから撮る

## [0.10.2] - 2026-05-03

> 0.10.1 の内容を、アップデート通知の文面を付けて配布し直した版。コードの変更はない。

## [0.10.1] - 2026-05-03

> リリース時に CHANGELOG へ記載されなかったため、git 履歴と 0.10.2 のアップデート通知の文面から後追いで再構成した。

### Added
- 最適化の統合ウィンドウ `UnityAgent > Avatar Optimizer`。パフォーマンス・検証・テクスチャ VRAM の解析、AAO TraceAndOptimize の設定、NDMF Mesh Simplifier の操作、テクスチャ最適化の提案を 1 画面で扱える。対象のアバターは選択中のオブジェクトから自動で決める
- NDMF の API をボタンから試せるデバッグ用ウィンドウ `UnityAgent > NDMF Tester`
- bake せずに解析する `AnalyzeAvatarPerformance`。VRChat SDK 公式の `AvatarPerformance.CalculatePerformanceStats` と NDMF の `ParameterInfo` を組み合わせ、今のシーンの状態とビルド後のパラメータの予測を 1 つのレポートにまとめる

### Changed
- メニューを `Window > 紫陽花広場 > *` から、最上位の `UnityAgent > *` に集めた
- NDMF（`nadena.dev.ndmf` / `nadena.dev.ndmf.runtime` / `nadena.dev.ndmf.vrchat`）を必須の参照にした

## [0.10.0] - 2026-04-30

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- 表情の作り方を刷新した。まつげ・舌・歯など複数の SkinnedMeshRenderer に対応したプロファイルと、9 種のプリセット（smile / angry / surprised / sad / cry / wink / sleep / kiss / shy）を用意し、日本語のキーワードから BlendShape の組み合わせを作れる。専用カメラ `FaceCameraCapture` で、SceneView に左右されないプレビュー画像を撮る
- AnimatorController の編集ツール 8 種。Layer / State / Parameter の名前変更・削除・移動・複製と、Parameter の既定値の設定。Parameter の名前を変えると、遷移の条件と BlendTree の参照も追従する
- GraphView でスキルを組み立てるノーコードのフローチャート
- 必須の API キーが未設定のとき、チャットに案内のバナーを出す
- 開発者向けのチャット共有が有効なとき、チャットに警告を出す
- マテリアルの値を MaterialPropertyBlock も含めて読むようにし、複数サブメッシュの AO ベイクに対応した

### Changed
- 設定画面のタイトルを、テーマに合わせたロゴバナーにした
- 製品名の表記を UnityAgent に統一した
- VRChat の PhysBone 関連ツールを `PhysBoneTools` にまとめ、VRChat 関連のツール名に `VRC` を付けた。旧名で呼んでいたスキルやスクリプトは新しい名前に直す必要がある

### Fixed
- VRChat の Permission の True と False が逆に設定されていた

## [0.9.7] - 2026-04-23

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- Play モードや Gesture Manager のプレビュー中の状態を観測するツール群。Animator のパラメータと現在のステート、BlendTree のブレンド値、マテリアルの Vector4 の一括取得、Contact Receiver の現在値、`Physics.Raycast` のシミュレーション、エディタの状態など
- AI から Gesture Manager の「Enter Play-Mode」「Exit Play-Mode」を実行できるようにした。非アクティブなアバターは自動でアクティブにし、Undo に記録する
- Gesture Manager の PlayableGraph 内の FX / Gesture / Action レイヤーを調べる `GetGmAnimatorCurrentStateInfo`

### Fixed
- `ListMaterialProperties` で Vector4 の値が `?` と表示されていた

## [0.9.6] - 2026-04-23

> 0.9.5 の内容を、アップデート通知の文面を直して配布し直した版。コードの変更はない。

## [0.9.5] - 2026-04-23

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- MCP Bridge モードを Linux / macOS でも使えるようにした。ブリッジのバイナリを CI で 4 環境（win-x64 / linux-x64 / osx-x64 / osx-arm64）向けにビルドし、配布 zip に同梱する

### Changed
- MCP の各層（SSE / Bridge / Bootstrap / Manager / Client）の診断ログを大幅に増やした。外向きの呼び出し、OAuth discovery、リスナーの停止、状態の遷移を追えるようにし、それまで何も出さずに失敗していた経路もログに出す
- 呼び出しごとの開始と完了のログを、Info から Debug に下げた

### Fixed
- Linux / macOS で MCP Bridge モードが起動しなかった

## [0.9.4] - 2026-04-18

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Changed
- MCP サーバー・Bridge・Invoker のログを AgentLogger に集めた。DebugMode を有効にすると、詳細なトレースを AgentLogWindow で確認できる
- `ListRenderers` の出力に、`ScanAvatarMeshes` を使うよう案内を足した
- `Editor/` 直下のファイルを役割ごとのフォルダに整理した（内部の整理で、動作は変わらない）

## [0.9.3] - 2026-04-18

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- レイキャストで AO をベイクする `BakeAmbientOcclusion`。`mode="texel"` は UV を展開して PNG に、`mode="vertex"` は頂点カラーに書き込み、新しいメッシュアセットと差し替える。SkinnedMeshRenderer のスケールが二重にかからないようにしてある
- Body と Face の SkinnedMeshRenderer を特定する `IdentifyBodySmr` / `IdentifyFaceSmr`。名前、骨の領域の広がり、viseme 用の BlendShape の順に段階的に判定し、紛らわしい命名でも外さない

## [0.9.2] - 2026-04-18

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- TexTransTool 連携。`net.rs64.tex-trans-tool` が入っていると、サブアセンブリ `AjisaiFlow.UnityAgent.TexTransTool.Editor`（`NET_RS64_TTT`）がコンパイルされ、8 ツールが使えるようになる
  - 読み取り（Risk=Safe）: `TttDescribePhases` / `TttListStableComponents` / `TttListComponents`
  - 作成（Risk=Caution）: `TttAddSimpleDecal` / `TttAddTextureBlender` / `TttAddAtlasTexture`
  - パイプライン: `TttManualBake` / `TttExitPreviews`
- Unity の Console を読む・数える・消す `GetConsoleLogs` / `CountConsoleLogs` / `ClearConsole`。エージェントが自分でエラーや警告に気づけるようにした

### Changed
- 同梱のサブアセンブリ（`AjisaiFlow.UnityAgent.*`）のツールを、外部ツールではなく内蔵ツールとして扱うようにした。オプションのパッケージがあるときだけ有効になるモジュールも、外部ツールの許可なしで使える
- 内蔵ツールでも `[AgentTool(Risk=Safe|Dangerous)]` の明示指定を優先するようにした。名前からの推定は、指定が既定の `Caution` のときだけ使う
- その影響で、World / Template 系の 21 ツールが外部ツールから内蔵ツールに変わった。意図して無効にしていた場合は、設定画面でもう一度無効にする必要がある

### Fixed
- `CaptureSceneView` などが撮った画像が、外部の MCP クライアント（Claude Code など）に画像として届いていなかった。MCP の image content block で返すようにした

## [0.9.1] - 2026-04-17

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- Mesh Painter v2 に、編集リスト、部分的な Undo、「すべて適用」ボタンを追加
- Mesh Painter v2 に、Modular Avatar で非破壊に適用するトグルを追加（ModularAvatarMaterialSetter 経由）
- もちふぃった～で、BOOTH のアバター商品ページが対応を明記している「本体」のエントリに対応

### Changed
- Mesh Painter v2 のスライダーは即時に反映せず、「適用」ボタンで履歴に加える方式にした
- Mesh Painter v2 で、SMR やタブを切り替えるときの未コミットの確認ダイアログをなくした
- Mesh Painter v2 の選択ハイライトを半透明にし、何もないところをクリックすると選択を解除するようにした
- もちふぃった～のグリッド表示を仮想化し、大量のカタログでも軽く動くようにした。サムネイルのキャッシュには上限を設け、メモリを使いすぎないようにした

### Fixed
- Mesh Painter v2 で、スケールの付いた SMR だとクリックした位置がずれていた

## [0.9.0] - 2026-04-16

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- もちふぃった～のプロファイルカタログ。BOOTH の商品 305 件を収録し、グリッドとリストの切り替え、変換の種類と価格での絞り込み、サムネイルの遅延読み込みに対応
- AI 向けのもちふぃった～連携ツール。アバターの対応確認、シーンのスキャン、プロファイルの推薦
- チャットで BOOTH の URL をリンクプレビューのカードで表示する
- チャットのリンクを開く前の確認ダイアログ。信頼済みのドメインかどうかを判定して表示する

## [0.8.3] - 2026-04-16

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Fixed
- Mesh Painter のグラデーションで、開始と終了の範囲の外にあるピクセルに色が付かなかった

## [0.8.2] - 2026-04-15

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- Mesh Painter v2 ウィンドウ（MD3 UI、サイズを変えられる分割レイアウト）
- Mesh Painter のライブプレビュー。スライダーの操作をすぐメッシュに反映し、ディスクへの書き出しは適用したときだけにした

### Changed
- MCP Bridge モードを有効にした直後や切り替えた直後に、状態の表示が「Bridge starting…」から「connected」へ自動で変わるようにした

### Fixed
- Scene ビューでメッシュをクリックしたとき、半透明マテリアルのメッシュを正しく最前面として選べていなかった
- Scene ビューでのペイントで、メッシュの境界をまたぐとストロークが途切れていた
- 1 つのメッシュを編集したあとの Undo で、関係のない別のメッシュの色まで戻っていた
- タブを切り替えると StackOverflow で落ちていた
- ドメインリロードの直後にフォントが抜けていた（UIRStylePainter の NullReferenceException）

## [0.8.1] - 2026-04-14

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Fixed
- ドメインリロードの後に、中身の空のツールカードが残り続けていた。中断されたツール実行は、灰色の「Cancelled」カードで表示する
- プロバイダーの進捗メッセージ（Streaming from: / Requesting to: / Rate limit / Connecting など）がチャット履歴に残っていた。以後はアクティビティパネルに一時的に表示するだけにした
- リロードの前に出ていた AskUser の選択肢が、リロードの後に押しても反応しなかった。答えないまま残っていた選択は、自動でキャンセル扱いにする
- `CurrentSession.json` が毎回旧形式（v1）で保存され、変換が毎回やり直されていた。v2 で保存するようにした
- リロード後の新しいツール実行の ID が、古いツールカードと衝突することがあった

## [0.8.0] - 2026-04-14

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- エージェントの応答に含まれるコードブロックのシンタックスハイライト（C# / JSON / Python / Shader / JS）
- エージェントの吹き出しに、ホバーで出るコピーボタンと相対時刻（「5秒前」など）
- 思考過程のライブ表示（Claude / Gemini / OpenAI 互換 API の構造化 SSE イベント）
- ツールカードの履歴をドメインリロードをまたいで保存し、1 枚のカードとして復元する

### Changed
- 思考過程の折りたたみ表示を作り直した（脳のアイコン、行数のバッジ、専用の背景）
- 旧形式（v1）のセッションを、新しい形式に自動で変換する。変換前のファイルは `v1.bak` として残す

## [0.7.2] - 2026-04-14

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Changed
- チャット画面を刷新した。ツールの実行を 1 枚のカードにまとめ、実行中から成功・エラーへの状態の変化、実行時間、引数と結果の折りたたみ、アセットへのリンクを表示する
- 読み込み中の表示の文言を 1.5 秒ごとに切り替える（考え中… / 接続中… / 応答を待っています… / 処理しています…）
- 実行中のツールカードに、左端の強調線とスピナーを付けた。カスタムテーマでも見える色で表示する

## [0.7.1] - 2026-04-14

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Fixed
- カスタムテーマを使うと、アップデート通知のバナー（「新しいバージョンが利用可能です」）の背景と文字が透明になって読めなかった。テーマの ErrorContainer / OnErrorContainer が未定義でも表示されるようにした

## [0.7.0] - 2026-04-14

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- MCP Bridge（別プロセス）。スクリプトの編集で Unity のドメインリロードが起きても、Claude Code や codex などの外部 MCP クライアントとの接続が切れない
- チャット履歴、LLM との会話の文脈、選択待ちのダイアログを、ドメインリロードをまたいで保存する。スクリプトを編集するたびにチャットが消えていた問題を解消した
- ドメインリロードで中断された LLM の応答を、自動で再開する（1 セッションにつき 1 回まで）
- ツールの引数に、Python のような三重引用符の文字列 `"""..."""` を使えるようにした。C# スクリプトなどのファイルの中身を、エスケープせずに渡せる
- MCP 設定タブに「サーバーモード」（InProc / Bridge）の選択を追加した。Bridge モードのポートは、プロジェクトのパスから自動で決まる
- Windows 用のビルド済みブリッジ（`UnityAgentBridge.exe`）を同梱

### Changed
- ユーザーの入力待ちでリロードが起きたとき、AI が勝手に応答を再開しないよう判定を厳しくした
- 自動再開のときも、読み込み中の表示・ストリーミング表示・ツールの確認ダイアログが通常どおり動くようにした

### Fixed
- 時間のかかるリロードのあとに、アイドル監視がブリッジを止めてしまっていた

## [0.6.0] - 2026-04-12

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- MCP サーバーを内蔵した。Claude Code や Cursor などの外部 AI エージェントから、HTTP で UnityAgent の 400 以上のツールを呼べる
- 「MCP Server (External Agent)」プロバイダー。外部エージェントが会話を進め、AskUser やメッシュ選択のやり取りは UnityAgent の画面で行う
- MCP サーバーの OAuth 2.1 discovery。Claude Code などが自動で認証できる
- 設定画面に MCP サーバーのタブ（ポート / Bearer トークン / 公開するリスクの上限）

## [0.5.1] - 2026-04-07

> リリース時に CHANGELOG へ記載されなかったため、アップデート通知に載せた文面と git 履歴から後追いで再構成した。

### Added
- ロギングの仕組み `AgentLogger`。全プロバイダーのログを詳しくし、429 のリトライ時の記録も増やした

### Changed
- 設定の再読み込みを減らした

### Fixed
- OpenAI の新しいモデルが要求する `max_completion_tokens` に対応した
- 画像の MIME タイプが分からないときの既定値を用意した

## [0.5.0] - 2026-04-02

### Added
- メインウィンドウのアップデート通知バナー
- アップデート後に 1 度だけ出る変更点のダイアログ
- Claude CLI の考え中の内容とツールの実行をライブで表示するアクティビティパネル
- AI の処理中に表示するアニメーション

### Changed
- VPM での配布を、コンパイル済みの DLL からソースコードに切り替えた
- リポジトリを MIT ライセンスで公開した

### Removed
- Obfuscar による難読化をやめた。ソースはすべて公開している

### Fixed
- Claude CLI プロバイダーが出力をリアルタイムに流していなかった
- 固定のタイムアウトを、無応答の時間で判定するタイムアウトに変えた。応答中なのにタイムアウトする誤判定を防ぐ

[Unreleased]: https://github.com/lighfu/unity-agent/compare/editor-v0.16.0...HEAD
[0.16.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.15.0...editor-v0.16.0
[0.15.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.14.0...editor-v0.15.0
[0.14.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.13.0...editor-v0.14.0
[0.13.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.12.1...editor-v0.13.0
[0.12.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.12.0...editor-v0.12.1
[0.12.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.11.1...editor-v0.12.0
[0.11.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.11.0...editor-v0.11.1
[0.11.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.6...editor-v0.11.0
[0.10.6]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.5...editor-v0.10.6
[0.10.5]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.4...editor-v0.10.5
[0.10.4]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.3...editor-v0.10.4
[0.10.3]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.2...editor-v0.10.3
[0.10.2]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.1...editor-v0.10.2
[0.10.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.10.0...editor-v0.10.1
[0.10.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.7...editor-v0.10.0
[0.9.7]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.6...editor-v0.9.7
[0.9.6]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.5...editor-v0.9.6
[0.9.5]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.4...editor-v0.9.5
[0.9.4]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.3...editor-v0.9.4
[0.9.3]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.2...editor-v0.9.3
[0.9.2]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.1...editor-v0.9.2
[0.9.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.9.0...editor-v0.9.1
[0.9.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.8.3...editor-v0.9.0
[0.8.3]: https://github.com/lighfu/unity-agent/compare/editor-v0.8.2...editor-v0.8.3
[0.8.2]: https://github.com/lighfu/unity-agent/compare/editor-v0.8.1...editor-v0.8.2
[0.8.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.8.0...editor-v0.8.1
[0.8.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.7.2...editor-v0.8.0
[0.7.2]: https://github.com/lighfu/unity-agent/compare/editor-v0.7.1...editor-v0.7.2
[0.7.1]: https://github.com/lighfu/unity-agent/compare/editor-v0.7.0...editor-v0.7.1
[0.7.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.6.0...editor-v0.7.0
[0.6.0]: https://github.com/lighfu/unity-agent/compare/editor-v0.5.1...editor-v0.6.0
[0.5.1]: https://github.com/lighfu/unity-agent/releases/tag/editor-v0.5.1
[0.5.0]: https://github.com/lighfu/vpm/releases/tag/editor-v0.5.0
