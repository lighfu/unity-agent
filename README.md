# UnityAgent

AI-powered Unity Editor agent for VRChat avatar creation with 400+ tools.

[日本語](#日本語) | [English](#english)

---

## 日本語

### 概要

UnityAgent は、LLM (大規模言語モデル) を活用して Unity Editor を自然言語で操作できるツールです。VRChat アバター制作に特化した 400 以上のツールを搭載しています。

### 機能

- 自然言語による Unity Editor 操作
- VRChat アバター制作に特化したツール群
- 複数の LLM プロバイダー対応 (OpenAI, Anthropic, Google, ローカル等)
- Modular Avatar / FaceEmo / Avatar Optimizer 連携
- MCP (Model Context Protocol) サーバー対応
- 多言語 UI ローカライズ

### インストール

#### ALCOM / VCC 経由 (推奨)

1. [VPM リポジトリ](https://lighfu.github.io/vpm/) を ALCOM / VCC に追加
2. プロジェクトに「UnityAgent」を追加

#### 手動インストール

[Releases](https://github.com/lighfu/unity-agent/releases) から最新の zip をダウンロードし、`Packages/` フォルダに展開してください。

### 依存パッケージ

- [MD3 SDK](https://github.com/lighfu/unity-md3sdk) (UI コンポーネント)
- VRChat SDK (com.vrchat.avatars)

### ライセンス

MIT License - 詳細は [LICENSE](LICENSE) を参照してください。

---

## English

### Overview

UnityAgent is an AI-powered tool that lets you control the Unity Editor using natural language. It includes 400+ tools specialized for VRChat avatar creation.

### Features

- Natural language Unity Editor control
- 400+ tools specialized for VRChat avatars
- Multiple LLM provider support (OpenAI, Anthropic, Google, local, etc.)
- Modular Avatar / FaceEmo / Avatar Optimizer integration
- MCP (Model Context Protocol) server support
- Multi-language UI localization

### Installation

#### Via ALCOM / VCC (Recommended)

1. Add the [VPM repository](https://lighfu.github.io/vpm/) to ALCOM / VCC
2. Add "UnityAgent" to your project

#### Manual Installation

Download the latest zip from [Releases](https://github.com/lighfu/unity-agent/releases) and extract it into your `Packages/` folder.

### Dependencies

- [MD3 SDK](https://github.com/lighfu/unity-md3sdk) (UI components)
- VRChat SDK (com.vrchat.avatars)

### License

MIT License - See [LICENSE](LICENSE) for details.
