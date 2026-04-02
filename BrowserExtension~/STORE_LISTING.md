# Chrome Web Store Listing — Unity Agent - Web Browser Bridge

## Title

Unity Agent - Web Browser Bridge

## Short Description (132 characters max)

Bridges Unity Editor AI Agent with web AI chats (Gemini, ChatGPT, Copilot) via local WebSocket. No data leaves your machine.

## Detailed Description (English)

Unity Agent - Web Browser Bridge connects your locally running Unity Editor AI agent to web-based AI chat services through a secure localhost WebSocket connection.

**Supported AI Services:**
- Google Gemini (gemini.google.com)
- ChatGPT (chatgpt.com)
- Microsoft Copilot (copilot.microsoft.com)

**How It Works:**
1. The Unity Editor runs an AI agent that opens a local WebSocket server
2. This extension connects to the local server and acts as a bridge
3. The agent can send prompts to AI chat services and receive responses
4. All communication stays on your local machine — nothing is sent to external servers

**Features:**
- Automatic WebSocket reconnection with exponential backoff
- Real-time connection status overlay on supported pages
- Popup UI showing connection state and port configuration
- Zero data collection — no analytics, no tracking, no external requests

**Setup:**
1. Install this extension
2. Open Unity Editor with the Unity Agent package installed
3. Start the AI agent — it will open a WebSocket server on localhost
4. Navigate to a supported AI chat site — the extension connects automatically
5. The status overlay in the bottom-right corner shows the connection state

**Privacy:**
This extension communicates only with localhost (127.0.0.1). It does not collect, store, or transmit any personal information. See the full privacy policy for details.

**Requirements:**
- Unity Editor with the Unity Agent package (com.ajisaiflow.vrchat.avater)
- Chrome or Chromium-based browser

---

## 詳細説明（日本語）

Unity Agent - Web Browser Bridge は、ローカルで動作する Unity Editor の AI エージェントと、Web ブラウザ上の AI チャットサービスを WebSocket で接続するブリッジ拡張機能です。

**対応 AI サービス:**
- Google Gemini (gemini.google.com)
- ChatGPT (chatgpt.com)
- Microsoft Copilot (copilot.microsoft.com)

**仕組み:**
1. Unity Editor で AI エージェントがローカル WebSocket サーバーを起動
2. この拡張機能がローカルサーバーに接続しブリッジとして動作
3. エージェントが AI チャットにプロンプトを送信し、応答を受信
4. すべての通信はローカルマシン内で完結 — 外部サーバーへのデータ送信なし

**機能:**
- 指数バックオフ付き自動再接続
- 対応サイト上のリアルタイム接続ステータスオーバーレイ
- 接続状態とポート設定を表示するポップアップ UI
- データ収集ゼロ — アナリティクス・トラッキング・外部リクエストなし

**セットアップ:**
1. この拡張機能をインストール
2. Unity Agent パッケージがインストールされた Unity Editor を開く
3. AI エージェントを起動 — localhost に WebSocket サーバーが開始
4. 対応する AI チャットサイトにアクセス — 自動で接続
5. 右下のステータスオーバーレイで接続状態を確認

**プライバシー:**
この拡張機能は localhost (127.0.0.1) とのみ通信します。個人情報の収集・保存・送信は一切行いません。

---

## Category

Developer Tools

## Language

English (primary), Japanese
