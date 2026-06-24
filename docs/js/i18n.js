/* UnityAgent — i18n
 * Lightweight key-based translator. data-i18n="key" on any element.
 * Switches between ja, en, zh-TW and zh. Persists choice in localStorage.
 */
(function () {
  "use strict";

  const STRINGS = {
    ja: {
      "meta.title": "UnityAgent — VRChat アバターのための AI Unity Editor",
      "meta.description": "LLM で Unity Editor を自然言語操作。VRChat アバター制作に特化した 500+ ツールを搭載した AI エージェント。",
      "a11y.skip": "本文へスキップ",

      "nav.features": "特徴",
      "nav.standalone": "スタンドアロン",
      "nav.tools": "ツール",
      "nav.providers": "プロバイダー",
      "nav.install": "インストール",
      "nav.changelog": "更新履歴",
      "nav.community": "コミュニティ",

      "hero.badge": "v0.10 リリース · MIT OSS",
      "hero.title.line1": "Unity Editor を、",
      "hero.title.line2": "話しかけて動かす。",
      "hero.lede": "UnityAgent は、VRChat アバター制作のための AI エージェント兼 Editor ユーティリティ集。500+ 専用ツールを自然言語から呼び出すことも、AI なしで GUI ツール単体として使うこともできます。",
      "hero.cta.install": "インストール",
      "hero.cta.github": "GitHub で見る",
      "hero.stat.tools": "専用ツール",
      "hero.stat.providers": "LLM プロバイダー",
      "hero.stat.license": "オープンソース",

      "features.eyebrow": "FEATURES",
      "features.title": "アバター制作のあらゆる場面に。",
      "features.lede": "一貫した自然言語インタフェースの裏で、UnityAgent は 500+ の専用ツールが連携して動きます。",
      "features.f1.title": "アバター解析と最適化",
      "features.f1.body": "PhysBone・BlendShape・骨構造を一括検査し、Quest 変換、メッシュ簡略化、テクスチャアトラスまで自動化。",
      "features.f2.title": "Modular Avatar / NDMF 連携",
      "features.f2.body": "MA メニュー・パラメータ・MergeArmature・FaceEmo をネイティブにサポート。NDMF パイプラインも自然言語で。",
      "features.f3.title": "テクスチャ・マテリアル編集",
      "features.f3.body": "lilToon / Poiyomi の一括設定、AO ベイク、UV 検証、TexTransTool 連携で繊細な仕上げまで。",
      "features.f4.title": "アニメーション・表情",
      "features.f4.body": "Animator State Machine の編集、FaceEmo 表情パターン、ジェスチャー連動を会話で組み立て。",
      "features.f5.title": "マルチプロバイダー対応",
      "features.f5.body": "Claude / GPT / Gemini / DeepSeek / ローカル LLM (Ollama / LM Studio) まで、好みに合わせて選択可能。",
      "features.f6.title": "完全オープンソース",
      "features.f6.body": "MIT ライセンス。難読化なしの完全ソース配布で、改造も商用利用も自由。",

      "standalone.eyebrow": "STANDALONE",
      "standalone.title": "AI を使わなくても、これだけ揃う。",
      "standalone.lede": "UnityAgent は GUI ユーティリティの集合体としても完成しています。プロバイダー設定なしで使える、独立した Editor ツールを多数同梱。",
      "standalone.t1.title": "Bone Pose Editor",
      "standalone.t1.badge": "実験的",
      "standalone.t1.body": "ボーンポーズ編集・IK・レイヤーシステム・キーリダクションを一画面に統合した独立ツール。実験段階のため、一部の操作で挙動が変わる場合があります。",
      "standalone.t2.title": "Mesh Painter",
      "standalone.t2.body": "UV アイランド単位の選択ペイント。テクスチャ編集・マスク作成・部分修正が GUI で完結。",
      "standalone.t3.title": "Outfit Fitting Wizard",
      "standalone.t3.badge": "実験的",
      "standalone.t3.body": "ARAP / XPBD / Body SDF を駆使した衣装の自動フィッティング。手動調整不要のプリセットも完備。現在は実験段階で、複雑な衣装では仕上げ調整が必要になる場合があります。",
      "standalone.t4.title": "BlendShape Shrinker",
      "standalone.t4.body": "不要な BlendShape の検出と一括除去。容量とビルド時間を圧縮。",
      "standalone.t5.title": "Texture Atlas",
      "standalone.t5.badge": "実験的",
      "standalone.t5.body": "MaxRects パッカーによる高密度アトラス化。マテリアル統合とドローコール削減を GUI で。実験段階のため、複雑なシェーダー設定では再調整が必要になる場合があります。",
      "standalone.t6.title": "Pose Estimation",
      "standalone.t6.badge": "実験的",
      "standalone.t6.body": "MediaPipe / ROMP / WHAM 連携で動画からモーション抽出。撮影素材から AnimationClip を生成。Python 環境のセットアップが必要な実験機能です。",
      "standalone.note": "※ いずれのツールも UnityAgent ウィンドウから起動できます。LLM プロバイダーの設定は不要です。",

      "tools.eyebrow": "TOOLS",
      "tools.title": "500+ の専用ツール。",
      "tools.lede": "すべて自然言語から呼び出せます。下表は主要カテゴリの抜粋です。",
      "tools.footnote": "※ 各カテゴリ配下に複数の細分化ツールがあり、合計 549 件以上を提供しています。",

      "providers.eyebrow": "PROVIDERS",
      "providers.title": "使い慣れた LLM をそのまま。",
      "providers.lede": "主要なクラウド LLM、CLI 統合、ローカル推論まで、フラットに切り替えできます。",
      "providers.col.name": "プロバイダー",
      "providers.col.kind": "種別",
      "providers.col.auth": "認証",
      "providers.col.notes": "メモ",
      "providers.kind.cloud": "クラウド",
      "providers.kind.local": "ローカル",
      "providers.kind.cli": "CLI",
      "providers.kind.bridge": "ブリッジ",

      "install.eyebrow": "INSTALL",
      "install.title": "数クリックで導入。",
      "install.tab.alcom": "ALCOM / VCC",
      "install.tab.manual": "手動インストール",
      "install.alcom.s1.title": "VPM リポジトリを追加",
      "install.alcom.s1.body": "下のボタンから VPM リポジトリを ALCOM / VCC に追加します。",
      "install.alcom.s1.cta": "VPM リポジトリを開く",
      "install.alcom.s2.title": "プロジェクトに追加",
      "install.alcom.s2.body": "Unity プロジェクトを開き、Packages 一覧から「UnityAgent」を追加します。",
      "install.alcom.s3.title": "起動",
      "install.alcom.s3.body": "Unity メニューから <code>AjisaiFlow → UnityAgent</code> を開いて、好きなプロバイダーを設定するだけ。",
      "install.manual.s1.title": "最新リリースをダウンロード",
      "install.manual.s1.body": "GitHub Releases から最新 zip を取得します。",
      "install.manual.s1.cta": "Releases を開く",
      "install.manual.s2.title": "Packages に展開",
      "install.manual.s2.body": "プロジェクトの <code>Packages/</code> フォルダに展開します。",
      "install.manual.s3.title": "依存パッケージ",
      "install.manual.s3.body": '<a href="https://github.com/lighfu/unity-md3sdk" target="_blank" rel="noopener">MD3 SDK</a> と VRChat SDK (com.vrchat.avatars) を併せて導入してください。',

      "changelog.eyebrow": "CHANGELOG",
      "changelog.title": "更新履歴。",
      "changelog.lede": "最新の変更点を抜粋しています。すべての履歴は GitHub をご覧ください。",
      "changelog.cta": "完全な CHANGELOG を見る",
      "changelog.unreleased": "Unreleased",
      "changelog.added": "Added",
      "changelog.changed": "Changed",
      "changelog.fixed": "Fixed",

      "community.eyebrow": "COMMUNITY",
      "community.title": "仲間と一緒に。",
      "community.lede": "質問・要望・バグ報告はコミュニティへ。",
      "community.discord": "招待リンクは GitHub README に掲載予定です。",
      "community.gh.title": "GitHub Issues",
      "community.gh.body": "バグ報告・機能要望はこちらから。",
      "community.x": "最新リリース情報をお届けします。",

      "footer.tag": "AI-powered Unity Editor for VRChat avatars",
      "footer.product": "プロダクト",
      "footer.resources": "リソース",
      "footer.credits": "クレジット",
      "footer.credits.body": "UnityAgent は AjisaiFlow によって開発・公開されています。<br />ロゴデザインおよびブランドアセットは MIT ライセンスのもとリポジトリに同梱されています。",
      "footer.copyright": "© 2026 AjisaiFlow. MIT License.",
    },

    en: {
      "meta.title": "UnityAgent — AI-powered Unity Editor for VRChat avatars",
      "meta.description": "Control Unity Editor in natural language. An AI agent with 500+ tools specialized for VRChat avatar creation.",
      "a11y.skip": "Skip to content",

      "nav.features": "Features",
      "nav.standalone": "Standalone",
      "nav.tools": "Tools",
      "nav.providers": "Providers",
      "nav.install": "Install",
      "nav.changelog": "Changelog",
      "nav.community": "Community",

      "hero.badge": "v0.10 released · MIT OSS",
      "hero.title.line1": "Talk to Unity Editor.",
      "hero.title.line2": "Watch it build.",
      "hero.lede": "UnityAgent is both an AI agent and a suite of standalone Editor utilities for VRChat avatar creation. Call 500+ tools in natural language — or use the GUI utilities directly, no AI required.",
      "hero.cta.install": "Install",
      "hero.cta.github": "View on GitHub",
      "hero.stat.tools": "Specialized tools",
      "hero.stat.providers": "LLM providers",
      "hero.stat.license": "Open source",

      "features.eyebrow": "FEATURES",
      "features.title": "Built for avatar creators.",
      "features.lede": "Behind a single natural-language interface, 500+ tools cooperate to get the job done.",
      "features.f1.title": "Avatar analysis & optimization",
      "features.f1.body": "Inspect PhysBones, BlendShapes and rigs at once. Automate Quest conversion, mesh simplification and texture atlasing.",
      "features.f2.title": "Modular Avatar / NDMF integration",
      "features.f2.body": "First-class support for MA menus, parameters, MergeArmature and FaceEmo. Drive NDMF pipelines in plain language.",
      "features.f3.title": "Texture & material editing",
      "features.f3.body": "Bulk lilToon / Poiyomi configuration, AO bake, UV validation, and TexTransTool integration for fine-tuning.",
      "features.f4.title": "Animation & expressions",
      "features.f4.body": "Edit Animator state machines, build FaceEmo expression sets and gesture wiring through conversation.",
      "features.f5.title": "Multi-provider",
      "features.f5.body": "Claude / GPT / Gemini / DeepSeek and local LLMs (Ollama / LM Studio) — pick what fits your workflow.",
      "features.f6.title": "Fully open source",
      "features.f6.body": "MIT licensed. Distributed as un-obfuscated source — fork it, ship it, customize it.",

      "standalone.eyebrow": "STANDALONE",
      "standalone.title": "Use it without AI, too.",
      "standalone.lede": "UnityAgent ships as a complete suite of GUI utilities. Multiple Editor tools are bundled and usable without configuring any LLM provider.",
      "standalone.t1.title": "Bone Pose Editor",
      "standalone.t1.badge": "Experimental",
      "standalone.t1.body": "Pose editing, IK, layer system and key reduction unified in a single window. Works on any rig. Experimental — some operations may behave inconsistently.",
      "standalone.t2.title": "Mesh Painter",
      "standalone.t2.body": "Per-UV-island selection painting — texture editing, mask creation and partial fixes, all in the GUI.",
      "standalone.t3.title": "Outfit Fitting Wizard",
      "standalone.t3.badge": "Experimental",
      "standalone.t3.body": "Automated outfit fitting backed by ARAP / XPBD / Body SDF. Comes with no-tweak presets. Currently experimental — complex outfits may need manual touch-up.",
      "standalone.t4.title": "BlendShape Shrinker",
      "standalone.t4.body": "Detect and remove unused BlendShapes in bulk to shrink size and build time.",
      "standalone.t5.title": "Texture Atlas",
      "standalone.t5.badge": "Experimental",
      "standalone.t5.body": "Dense atlasing via the MaxRects packer. Material consolidation and draw call reduction from the GUI. Experimental — complex shader setups may need re-tuning.",
      "standalone.t6.title": "Pose Estimation",
      "standalone.t6.badge": "Experimental",
      "standalone.t6.body": "Extract motion from video via MediaPipe / ROMP / WHAM. Turn footage into AnimationClips. Experimental — requires Python environment setup.",
      "standalone.note": "* Each utility opens directly from the UnityAgent window. No LLM provider setup needed.",

      "tools.eyebrow": "TOOLS",
      "tools.title": "500+ specialized tools.",
      "tools.lede": "All callable from natural language. Below is a curated set of major categories.",
      "tools.footnote": "* Each category contains multiple finer-grained tools, totaling 549+ in this release.",

      "providers.eyebrow": "PROVIDERS",
      "providers.title": "Bring your own LLM.",
      "providers.lede": "Cloud APIs, CLI integrations and local inference — switch flatly across them.",
      "providers.col.name": "Provider",
      "providers.col.kind": "Kind",
      "providers.col.auth": "Auth",
      "providers.col.notes": "Notes",
      "providers.kind.cloud": "Cloud",
      "providers.kind.local": "Local",
      "providers.kind.cli": "CLI",
      "providers.kind.bridge": "Bridge",

      "install.eyebrow": "INSTALL",
      "install.title": "A few clicks to start.",
      "install.tab.alcom": "ALCOM / VCC",
      "install.tab.manual": "Manual install",
      "install.alcom.s1.title": "Add the VPM repository",
      "install.alcom.s1.body": "Add the VPM repository to ALCOM / VCC using the button below.",
      "install.alcom.s1.cta": "Open VPM repository",
      "install.alcom.s2.title": "Add to your project",
      "install.alcom.s2.body": "Open your Unity project and add \"UnityAgent\" from the package list.",
      "install.alcom.s3.title": "Launch",
      "install.alcom.s3.body": "Open <code>AjisaiFlow → UnityAgent</code> from the Unity menu, configure your provider, and you are set.",
      "install.manual.s1.title": "Download the latest release",
      "install.manual.s1.body": "Grab the latest zip from GitHub Releases.",
      "install.manual.s1.cta": "Open Releases",
      "install.manual.s2.title": "Extract into Packages",
      "install.manual.s2.body": "Extract the archive into your project's <code>Packages/</code> folder.",
      "install.manual.s3.title": "Dependencies",
      "install.manual.s3.body": 'Install <a href="https://github.com/lighfu/unity-md3sdk" target="_blank" rel="noopener">MD3 SDK</a> and VRChat SDK (com.vrchat.avatars) alongside.',

      "changelog.eyebrow": "CHANGELOG",
      "changelog.title": "Release notes.",
      "changelog.lede": "Selected highlights. The full history lives on GitHub.",
      "changelog.cta": "View full CHANGELOG",
      "changelog.unreleased": "Unreleased",
      "changelog.added": "Added",
      "changelog.changed": "Changed",
      "changelog.fixed": "Fixed",

      "community.eyebrow": "COMMUNITY",
      "community.title": "Build with us.",
      "community.lede": "Questions, feature requests and bug reports — come say hi.",
      "community.discord": "Invite link will be posted in the GitHub README.",
      "community.gh.title": "GitHub Issues",
      "community.gh.body": "File bugs and feature requests here.",
      "community.x": "Follow for release announcements.",

      "footer.tag": "AI-powered Unity Editor for VRChat avatars",
      "footer.product": "Product",
      "footer.resources": "Resources",
      "footer.credits": "Credits",
      "footer.credits.body": "UnityAgent is built and maintained by AjisaiFlow.<br />Logo and brand assets are bundled with the repository under MIT.",
      "footer.copyright": "© 2026 AjisaiFlow. MIT License.",
    },

    "zh-TW": {
      "meta.title": "UnityAgent — 面向 VRChat Avatar 的 AI Unity Editor",
      "meta.description": "用自然語言控制 Unity Editor。UnityAgent 是專為 VRChat Avatar 製作準備的 AI Agent，內建 500+ 專用工具。",
      "a11y.skip": "跳到正文",

      "nav.features": "特色",
      "nav.standalone": "獨立工具",
      "nav.tools": "工具",
      "nav.providers": "提供者",
      "nav.install": "安裝",
      "nav.changelog": "更新紀錄",
      "nav.community": "社群",

      "hero.badge": "v0.10 已發布 · MIT 開源",
      "hero.title.line1": "和 Unity Editor 對話，",
      "hero.title.line2": "讓它替你製作。",
      "hero.lede": "UnityAgent 既是面向 VRChat Avatar 製作的 AI Agent，也是一套獨立的 Editor 工具集。你可以用自然語言呼叫 500+ 專用工具，也可以不設定 AI，直接使用 GUI 工具。",
      "hero.cta.install": "安裝",
      "hero.cta.github": "在 GitHub 查看",
      "hero.stat.tools": "專用工具",
      "hero.stat.providers": "LLM 提供者",
      "hero.stat.license": "開源",

      "features.eyebrow": "FEATURES",
      "features.title": "涵蓋 Avatar 製作的各個環節。",
      "features.lede": "在統一的自然語言介面背後，UnityAgent 讓 500+ 專用工具協同工作。",
      "features.f1.title": "Avatar 分析與最佳化",
      "features.f1.body": "一次檢查 PhysBone、BlendShape 和骨骼結構，自動化 Quest 轉換、網格簡化與貼圖圖集處理。",
      "features.f2.title": "Modular Avatar / NDMF 整合",
      "features.f2.body": "原生支援 MA 選單、參數、MergeArmature 與 FaceEmo，也可以用自然語言驅動 NDMF 流程。",
      "features.f3.title": "貼圖與材質編輯",
      "features.f3.body": "批次設定 lilToon / Poiyomi，支援 AO 烘焙、UV 檢查與 TexTransTool 整合，用於細緻收尾。",
      "features.f4.title": "動畫與表情",
      "features.f4.body": "透過對話編輯 Animator 狀態機、建立 FaceEmo 表情組合與手勢連動。",
      "features.f5.title": "多提供者支援",
      "features.f5.body": "Claude / GPT / Gemini / DeepSeek 以及本機 LLM（Ollama / LM Studio）都可依工作流程選擇。",
      "features.f6.title": "完全開源",
      "features.f6.body": "MIT 授權。以未混淆原始碼發布，方便 fork、發布與客製化。",

      "standalone.eyebrow": "STANDALONE",
      "standalone.title": "不使用 AI，也能直接使用。",
      "standalone.lede": "UnityAgent 同時是一套完整的 GUI 工具集合。不需設定任何 LLM 提供者，也能使用多個內建 Editor 工具。",
      "standalone.t1.title": "Bone Pose Editor",
      "standalone.t1.badge": "實驗性",
      "standalone.t1.body": "將姿勢編輯、IK、圖層系統與關鍵影格簡化整合在同一個視窗中。適用於任意骨架。此功能仍為實驗性，部分操作可能出現不一致行為。",
      "standalone.t2.title": "Mesh Painter",
      "standalone.t2.body": "按 UV 島選擇並繪製，貼圖編輯、遮罩建立與局部修正都可以在 GUI 中完成。",
      "standalone.t3.title": "Outfit Fitting Wizard",
      "standalone.t3.badge": "實驗性",
      "standalone.t3.body": "基於 ARAP / XPBD / Body SDF 的自動服裝適配，並提供無需調參的預設。此功能仍為實驗性，複雜服裝可能需要手動收尾。",
      "standalone.t4.title": "BlendShape Shrinker",
      "standalone.t4.body": "批次偵測並移除未使用的 BlendShape，減少體積與建置時間。",
      "standalone.t5.title": "Texture Atlas",
      "standalone.t5.badge": "實驗性",
      "standalone.t5.body": "使用 MaxRects 打包器進行高密度圖集化，在 GUI 中完成材質合併與 Draw Call 最佳化。複雜 Shader 設定可能需要重新微調。",
      "standalone.t6.title": "Pose Estimation",
      "standalone.t6.badge": "實驗性",
      "standalone.t6.body": "透過 MediaPipe / ROMP / WHAM 從影片中擷取動作，並產生 AnimationClip。此實驗功能需要設定 Python 環境。",
      "standalone.note": "* 每個工具都可以直接從 UnityAgent 視窗啟動，無需設定 LLM 提供者。",

      "tools.eyebrow": "TOOLS",
      "tools.title": "500+ 專用工具。",
      "tools.lede": "全部都可以透過自然語言呼叫。下面列出的是主要類別摘錄。",
      "tools.footnote": "* 每個類別下都有多個細分工具，本版本合計提供 549+ 個工具。",

      "providers.eyebrow": "PROVIDERS",
      "providers.title": "使用你熟悉的 LLM。",
      "providers.lede": "雲端 API、CLI 整合與本機推論都可以統一切換。",
      "providers.col.name": "提供者",
      "providers.col.kind": "類型",
      "providers.col.auth": "驗證",
      "providers.col.notes": "說明",
      "providers.kind.cloud": "雲端",
      "providers.kind.local": "本機",
      "providers.kind.cli": "CLI",
      "providers.kind.bridge": "橋接",

      "install.eyebrow": "INSTALL",
      "install.title": "幾次點擊即可開始。",
      "install.tab.alcom": "ALCOM / VCC",
      "install.tab.manual": "手動安裝",
      "install.alcom.s1.title": "新增 VPM 倉庫",
      "install.alcom.s1.body": "使用下方按鈕，將 VPM 倉庫新增到 ALCOM / VCC。",
      "install.alcom.s1.cta": "開啟 VPM 倉庫",
      "install.alcom.s2.title": "新增到專案",
      "install.alcom.s2.body": "開啟你的 Unity 專案，在 Packages 清單中新增「UnityAgent」。",
      "install.alcom.s3.title": "啟動",
      "install.alcom.s3.body": "從 Unity 選單開啟 <code>AjisaiFlow → UnityAgent</code>，設定你想使用的提供者即可。",
      "install.manual.s1.title": "下載最新版本",
      "install.manual.s1.body": "從 GitHub Releases 取得最新 zip。",
      "install.manual.s1.cta": "開啟 Releases",
      "install.manual.s2.title": "解壓到 Packages",
      "install.manual.s2.body": "將壓縮檔解壓到專案的 <code>Packages/</code> 資料夾。",
      "install.manual.s3.title": "依賴套件",
      "install.manual.s3.body": '請同時安裝 <a href="https://github.com/lighfu/unity-md3sdk" target="_blank" rel="noopener">MD3 SDK</a> 與 VRChat SDK (com.vrchat.avatars)。',

      "changelog.eyebrow": "CHANGELOG",
      "changelog.title": "發布紀錄。",
      "changelog.lede": "這裡摘錄最新重點變更。完整歷史請查看 GitHub。",
      "changelog.cta": "查看完整 CHANGELOG",
      "changelog.unreleased": "未發布",
      "changelog.added": "新增",
      "changelog.changed": "變更",
      "changelog.fixed": "修復",

      "community.eyebrow": "COMMUNITY",
      "community.title": "一起建構。",
      "community.lede": "問題、功能建議與錯誤回報都可以在社群提出。",
      "community.discord": "邀請連結將發布在 GitHub README 中。",
      "community.gh.title": "GitHub Issues",
      "community.gh.body": "在這裡提交錯誤回報與功能建議。",
      "community.x": "關注發布公告。",

      "footer.tag": "面向 VRChat Avatar 的 AI Unity Editor",
      "footer.product": "產品",
      "footer.resources": "資源",
      "footer.credits": "致謝",
      "footer.credits.body": "UnityAgent 由 AjisaiFlow 開發與維護。<br />Logo 與品牌素材隨倉庫一起按 MIT 授權發布。",
      "footer.copyright": "© 2026 AjisaiFlow. MIT License.",
    },

    zh: {
      "meta.title": "UnityAgent — 面向 VRChat 头像的 AI Unity Editor",
      "meta.description": "用自然语言控制 Unity Editor。UnityAgent 是专为 VRChat 头像制作准备的 AI Agent，内置 500+ 专用工具。",
      "a11y.skip": "跳到正文",

      "nav.features": "特性",
      "nav.standalone": "独立工具",
      "nav.tools": "工具",
      "nav.providers": "提供商",
      "nav.install": "安装",
      "nav.changelog": "更新记录",
      "nav.community": "社区",

      "hero.badge": "v0.10 已发布 · MIT 开源",
      "hero.title.line1": "和 Unity Editor 对话，",
      "hero.title.line2": "让它替你搭建。",
      "hero.lede": "UnityAgent 既是面向 VRChat 头像制作的 AI Agent，也是一套独立的 Editor 工具集。你可以用自然语言调用 500+ 专用工具，也可以不配置 AI，直接使用 GUI 工具。",
      "hero.cta.install": "安装",
      "hero.cta.github": "在 GitHub 查看",
      "hero.stat.tools": "专用工具",
      "hero.stat.providers": "LLM 提供商",
      "hero.stat.license": "开源",

      "features.eyebrow": "FEATURES",
      "features.title": "覆盖头像制作的各个环节。",
      "features.lede": "在统一的自然语言界面背后，UnityAgent 让 500+ 专用工具协同工作。",
      "features.f1.title": "头像分析与优化",
      "features.f1.body": "一次性检查 PhysBone、BlendShape 和骨骼结构，自动化 Quest 转换、网格简化和纹理图集处理。",
      "features.f2.title": "Modular Avatar / NDMF 集成",
      "features.f2.body": "原生支持 MA 菜单、参数、MergeArmature 和 FaceEmo，也可以用自然语言驱动 NDMF 流程。",
      "features.f3.title": "纹理与材质编辑",
      "features.f3.body": "批量配置 lilToon / Poiyomi，支持 AO 烘焙、UV 校验和 TexTransTool 集成，用于细致收尾。",
      "features.f4.title": "动画与表情",
      "features.f4.body": "通过对话编辑 Animator 状态机、构建 FaceEmo 表情组合和手势联动。",
      "features.f5.title": "多提供商支持",
      "features.f5.body": "Claude / GPT / Gemini / DeepSeek 以及本地 LLM（Ollama / LM Studio）均可按工作流选择。",
      "features.f6.title": "完全开源",
      "features.f6.body": "MIT 许可。以未混淆源码发布，便于 fork、发布和定制。",

      "standalone.eyebrow": "STANDALONE",
      "standalone.title": "不使用 AI，也能直接使用。",
      "standalone.lede": "UnityAgent 同时是一套完整的 GUI 工具集合。无需配置任何 LLM 提供商，也能使用多个内置 Editor 工具。",
      "standalone.t1.title": "Bone Pose Editor",
      "standalone.t1.badge": "实验性",
      "standalone.t1.body": "将姿势编辑、IK、图层系统和关键帧简化整合在同一个窗口中。适用于任意骨架。当前仍为实验功能，部分操作可能存在不一致行为。",
      "standalone.t2.title": "Mesh Painter",
      "standalone.t2.body": "按 UV 岛选择并绘制，纹理编辑、遮罩创建和局部修正都可以在 GUI 中完成。",
      "standalone.t3.title": "Outfit Fitting Wizard",
      "standalone.t3.badge": "实验性",
      "standalone.t3.body": "基于 ARAP / XPBD / Body SDF 的自动服装适配，并提供无需调参的预设。当前仍为实验功能，复杂服装可能需要手动收尾。",
      "standalone.t4.title": "BlendShape Shrinker",
      "standalone.t4.body": "批量检测并移除未使用的 BlendShape，减少体积和构建时间。",
      "standalone.t5.title": "Texture Atlas",
      "standalone.t5.badge": "实验性",
      "standalone.t5.body": "使用 MaxRects 打包器进行高密度图集化，在 GUI 中完成材质合并和 Draw Call 优化。复杂 Shader 设置可能需要重新微调。",
      "standalone.t6.title": "Pose Estimation",
      "standalone.t6.badge": "实验性",
      "standalone.t6.body": "通过 MediaPipe / ROMP / WHAM 从视频中提取动作，并生成 AnimationClip。该实验功能需要配置 Python 环境。",
      "standalone.note": "* 每个工具都可以直接从 UnityAgent 窗口启动，无需配置 LLM 提供商。",

      "tools.eyebrow": "TOOLS",
      "tools.title": "500+ 专用工具。",
      "tools.lede": "全部都可以通过自然语言调用。下面列出的是主要类别摘录。",
      "tools.footnote": "* 每个类别下都有多个细分工具，本版本合计提供 549+ 个工具。",

      "providers.eyebrow": "PROVIDERS",
      "providers.title": "使用你熟悉的 LLM。",
      "providers.lede": "云端 API、CLI 集成和本地推理都可以统一切换。",
      "providers.col.name": "提供商",
      "providers.col.kind": "类型",
      "providers.col.auth": "认证",
      "providers.col.notes": "说明",
      "providers.kind.cloud": "云端",
      "providers.kind.local": "本地",
      "providers.kind.cli": "CLI",
      "providers.kind.bridge": "桥接",

      "install.eyebrow": "INSTALL",
      "install.title": "几次点击即可开始。",
      "install.tab.alcom": "ALCOM / VCC",
      "install.tab.manual": "手动安装",
      "install.alcom.s1.title": "添加 VPM 仓库",
      "install.alcom.s1.body": "使用下面的按钮，将 VPM 仓库添加到 ALCOM / VCC。",
      "install.alcom.s1.cta": "打开 VPM 仓库",
      "install.alcom.s2.title": "添加到项目",
      "install.alcom.s2.body": "打开你的 Unity 项目，在 Packages 列表中添加“UnityAgent”。",
      "install.alcom.s3.title": "启动",
      "install.alcom.s3.body": "从 Unity 菜单打开 <code>AjisaiFlow → UnityAgent</code>，配置你想用的提供商即可。",
      "install.manual.s1.title": "下载最新版本",
      "install.manual.s1.body": "从 GitHub Releases 获取最新 zip。",
      "install.manual.s1.cta": "打开 Releases",
      "install.manual.s2.title": "解压到 Packages",
      "install.manual.s2.body": "将压缩包解压到项目的 <code>Packages/</code> 文件夹。",
      "install.manual.s3.title": "依赖包",
      "install.manual.s3.body": '请同时安装 <a href="https://github.com/lighfu/unity-md3sdk" target="_blank" rel="noopener">MD3 SDK</a> 和 VRChat SDK (com.vrchat.avatars)。',

      "changelog.eyebrow": "CHANGELOG",
      "changelog.title": "发布记录。",
      "changelog.lede": "这里摘录最新重点变更。完整历史请查看 GitHub。",
      "changelog.cta": "查看完整 CHANGELOG",
      "changelog.unreleased": "未发布",
      "changelog.added": "新增",
      "changelog.changed": "变更",
      "changelog.fixed": "修复",

      "community.eyebrow": "COMMUNITY",
      "community.title": "一起构建。",
      "community.lede": "问题、功能建议和缺陷报告都可以在社区提出。",
      "community.discord": "邀请链接将发布在 GitHub README 中。",
      "community.gh.title": "GitHub Issues",
      "community.gh.body": "在这里提交缺陷报告和功能建议。",
      "community.x": "关注发布公告。",

      "footer.tag": "面向 VRChat 头像的 AI Unity Editor",
      "footer.product": "产品",
      "footer.resources": "资源",
      "footer.credits": "致谢",
      "footer.credits.body": "UnityAgent 由 AjisaiFlow 开发和维护。<br />Logo 与品牌素材随仓库一起按 MIT 许可发布。",
      "footer.copyright": "© 2026 AjisaiFlow. MIT License.",
    },
  };

  const STORAGE_KEY = "ua-lang";
  const DEFAULT_LANG = "ja";

  function detectLang() {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && STRINGS[saved]) return saved;
    const browser = (navigator.language || "").toLowerCase();
    if (browser.startsWith("zh-tw") || browser.startsWith("zh-hant") || browser.startsWith("zh-hk") || browser.startsWith("zh-mo")) return "zh-TW";
    if (browser.startsWith("zh")) return "zh";
    if (browser.startsWith("ja")) return "ja";
    if (browser.startsWith("en")) return "en";
    return DEFAULT_LANG;
  }

  // ----- Left-to-right character morph -----
  // For each [data-i18n] element, swap glyphs left→right so the layout never
  // jitters in the middle of an animation. The element's box is locked to the
  // wider of (from, to) up-front so reflow doesn't cascade through siblings.

  const HTML_RE = /<[a-z\/!][\s\S]*?>|&[a-z#0-9]+;/i;

  function lockBox(el, fromText, toText) {
    const cs = getComputedStyle(el);
    const prevDisplay = el.style.display;
    let inlinePromoted = false;
    if (cs.display === "inline") {
      el.style.display = "inline-block";
      inlinePromoted = true;
    }
    const original = el.textContent;
    el.textContent = fromText;
    const fromW = el.getBoundingClientRect().width;
    el.textContent = toText;
    const toW = el.getBoundingClientRect().width;
    el.textContent = original;
    const lock = Math.max(fromW, toW);
    if (lock > 0) el.style.minWidth = lock + "px";
    return () => {
      el.style.minWidth = "";
      if (inlinePromoted) el.style.display = prevDisplay;
    };
  }

  function morphChars(el, fromText, toText, dur) {
    const fromArr = Array.from(fromText);
    const toArr = Array.from(toText);
    const maxLen = Math.max(fromArr.length, toArr.length);
    if (maxLen === 0) {
      el.textContent = toText;
      return;
    }
    const release = lockBox(el, fromText, toText);
    el.classList.add("is-morphing");
    const start = performance.now();
    function frame(now) {
      const t = Math.min((now - start) / dur, 1);
      // Ease the cursor so the start/end feel anchored.
      const eased = 1 - Math.pow(1 - t, 2);
      const i = Math.floor(eased * maxLen);
      let out = "";
      for (let j = 0; j < maxLen; j++) {
        out += j < i ? (toArr[j] || "") : (fromArr[j] || "");
      }
      el.textContent = out;
      if (t < 1) {
        requestAnimationFrame(frame);
      } else {
        el.textContent = toText;
        el.classList.remove("is-morphing");
        release();
      }
    }
    requestAnimationFrame(frame);
  }

  function fadeSwap(el, finalHtml, totalMs) {
    el.classList.add("is-morphing");
    el.style.transition = `opacity ${(totalMs / 2) | 0}ms cubic-bezier(.2,.7,.3,1)`;
    el.style.opacity = "0";
    setTimeout(() => {
      el.innerHTML = finalHtml;
      el.style.opacity = "1";
      setTimeout(() => {
        el.style.transition = "";
        el.classList.remove("is-morphing");
      }, (totalMs / 2) | 0);
    }, (totalMs / 2) | 0);
  }

  function morphElement(el, finalVal, baseMs) {
    // HTML payloads (with <code>, <a>, <br>) cannot be character-swapped.
    if (HTML_RE.test(finalVal)) {
      fadeSwap(el, finalVal, baseMs);
      return;
    }
    const fromText = el.textContent || "";
    morphChars(el, fromText, finalVal, baseMs);
  }

  function applyLang(lang, opts) {
    opts = opts || {};
    const animate = opts.animate !== false;
    if (!STRINGS[lang]) lang = DEFAULT_LANG;
    const dict = STRINGS[lang];
    const reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const useAnim = animate && !reduced;

    document.documentElement.lang = lang;
    document.documentElement.classList.toggle("is-langswitching", useAnim);

    const els = document.querySelectorAll("[data-i18n]");
    const items = [];
    els.forEach(el => {
      const key = el.getAttribute("data-i18n");
      const val = dict[key];
      if (typeof val !== "string") return;
      items.push({ el, val });
    });

    if (!useAnim) {
      items.forEach(({ el, val }) => { el.innerHTML = val; });
    } else {
      // Per-element duration scales with text length so long paragraphs don't
      // race ahead of short labels. Tight cap keeps the page coherent.
      items.forEach(({ el, val }) => {
        const len = Math.max(val.length, (el.textContent || "").length);
        const dur = Math.min(820, Math.max(360, 220 + len * 14));
        morphElement(el, val, dur);
      });
      setTimeout(() => {
        document.documentElement.classList.remove("is-langswitching");
      }, 1100);
    }

    // Meta description
    const metaDesc = document.querySelector('meta[name="description"]');
    if (metaDesc && dict["meta.description"]) metaDesc.setAttribute("content", dict["meta.description"]);

    // Title
    if (dict["meta.title"]) document.title = dict["meta.title"];

    // Toggle label (show the next language in the cycle)
    const label = document.querySelector("#lang-toggle .lang-label");
    if (label) {
      if (lang === "ja") label.textContent = "EN";
      else if (lang === "en") label.textContent = "繁中";
      else if (lang === "zh-TW") label.textContent = "简中";
      else label.textContent = "日本語";
    }

    // Notify listeners (data tables rebuild headers etc.)
    document.dispatchEvent(new CustomEvent("ua:langchange", { detail: { lang } }));
  }

  function setLang(lang) {
    localStorage.setItem(STORAGE_KEY, lang);
    applyLang(lang);
  }

  function toggle() {
    const order = ["ja", "en", "zh-TW", "zh"];
    const current = document.documentElement.lang || DEFAULT_LANG;
    const idx = order.indexOf(current);
    const next = order[(idx + 1) % order.length] || DEFAULT_LANG;
    setLang(next);
  }

  // Public API
  window.UA_I18N = {
    get: (key) => {
      const lang = document.documentElement.lang || DEFAULT_LANG;
      return (STRINGS[lang] && STRINGS[lang][key]) || key;
    },
    lang: () => document.documentElement.lang || DEFAULT_LANG,
    set: setLang,
    toggle,
  };

  document.addEventListener("DOMContentLoaded", () => {
    applyLang(detectLang(), { animate: false });
    const btn = document.getElementById("lang-toggle");
    if (btn) btn.addEventListener("click", toggle);
  });
})();
