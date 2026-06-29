<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/unityagent-dark.png">
  <img alt="UnityAgent" src="docs/assets/unityagent-light.png" width="440">
</picture>

### AI-powered Unity Editor agent for VRChat avatar creation

自然言語で Unity Editor を操作する、VRChat アバター制作特化の AI エージェント — **400+ ツール**搭載

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![Release](https://img.shields.io/github/v/release/lighfu/unity-agent?label=release&color=8a2be2)](https://github.com/lighfu/unity-agent/releases)
[![VPM](https://img.shields.io/badge/VPM-ALCOM%20%2F%20VCC-orange)](https://lighfu.github.io/vpm/)
[![Stars](https://img.shields.io/github/stars/lighfu/unity-agent?style=social)](https://github.com/lighfu/unity-agent/stargazers)

[**🌐 Website**](https://lighfu.github.io/unity-agent/) · [**📦 Releases**](https://github.com/lighfu/unity-agent/releases) · [**🧩 VPM Repo**](https://lighfu.github.io/vpm/)

**[日本語](#日本語)** · **[English](#english)** · **[繁體中文](#繁體中文)** · **[简体中文](#简体中文)**

</div>

---

## ✨ Highlights

| | |
|---|---|
| 🗣️ **Natural-language control** | Drive the Unity Editor by chatting — the agent calls 400+ purpose-built tools. |
| 🧍 **Avatar essentials** | Viewpoint / Eye Look / Visemes, PhysBone · Contact · Constraint, Expression Menu & Parameters. |
| 🧩 **Non-destructive workflow** | First-class Modular Avatar / NDMF / VRCFury integration (Merge Animator/Armature, Bone Proxy, nested menus…). |
| 😀 **Faces & animation** | FaceEmo expressions, AnimationClip / Animator / AnimatorAsCode authoring. |
| 🎨 **Mesh · material · texture** | Mesh/UV/BlendShape/weight editing, lilToon, plus **AI texture generation (img2img)**. |
| 📱 **Quest & performance** | Optimization, performance-rank diagnostics, Quest conversion helpers. |
| 🔌 **MCP server** | Exposes its tools over the Model Context Protocol so external clients (e.g. Claude Code) can drive Unity. |
| 🌍 **Multi-language UI** | Japanese / English / 繁體中文 / 简体中文. |

### 🤖 Supported providers

| Type | Providers |
|---|---|
| **LLM** | Anthropic Claude · OpenAI · Google Gemini (incl. Vertex AI) · OpenAI-compatible endpoints · Claude / Codex / Gemini **CLI** |
| **Image (texture gen)** | Google Gemini · OpenAI · **ComfyUI** (local img2img) |

### 🔗 Optional integrations (auto-detected when installed)

Modular Avatar · NDMF · VRCFury · Avatar Optimizer (AAO) · lilToon · FaceEmo · VRCQuestTools · AnimatorAsCode · BlendShape Modifier · NDMF Mesh Simplifier · Gesture Manager · TexTransTool

> Features above are shared across all languages. Per-language overview, install steps and usage follow below.

---

## 日本語

### 概要

**UnityAgent** は、LLM（大規模言語モデル）を活用して Unity Editor を自然言語で操作できる AI エージェントです。VRChat アバター制作に特化した **400 以上のツール**を搭載し、「ボディに服を非破壊で着せて」「表情メニューにトグルを追加して」といった指示を実際の Editor 操作に変換します。

### インストール

#### ALCOM / VCC 経由（推奨）

1. [VPM リポジトリ](https://lighfu.github.io/vpm/) を ALCOM / VCC に追加
2. プロジェクトに **UnityAgent** を追加

#### 手動インストール

[Releases](https://github.com/lighfu/unity-agent/releases) から最新の zip をダウンロードし、`Packages/` フォルダに展開してください。

### 使い方

1. Unity メニュー **`Tools ▸ UnityAgent`** からエージェントウィンドウを開く
2. 設定で LLM プロバイダーと API キーを入力（Claude / OpenAI / Gemini / ローカル CLI 等）
3. チャットで自然言語で指示 → エージェントがツールを呼び出して Editor を操作
4. （任意）AI テクスチャ生成を使うなら画像プロバイダー（Gemini / OpenAI / ComfyUI）も設定

> 破壊的な操作（削除など）は実行前に確認ダイアログが表示されます。

### 依存・必要要件

- Unity **2022.3** 以降
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk)（UI コンポーネント）
- VRChat SDK（`com.vrchat.avatars`）

### リンク

[公式サイト](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM リポジトリ](https://lighfu.github.io/vpm/)

### ライセンス

MIT License — 詳細は [LICENSE](LICENSE) を参照してください。

---

## English

### Overview

**UnityAgent** is an AI agent that lets you control the Unity Editor with natural language. It ships **400+ tools** specialized for VRChat avatar creation, turning instructions like "dress this body non-destructively" or "add a toggle to the expression menu" into real Editor actions.

### Installation

#### Via ALCOM / VCC (recommended)

1. Add the [VPM repository](https://lighfu.github.io/vpm/) to ALCOM / VCC
2. Add **UnityAgent** to your project

#### Manual installation

Download the latest zip from [Releases](https://github.com/lighfu/unity-agent/releases) and extract it into your `Packages/` folder.

### Usage

1. Open the agent window from **`Tools ▸ UnityAgent`**
2. In settings, choose an LLM provider and enter its API key (Claude / OpenAI / Gemini / local CLI, etc.)
3. Chat in natural language — the agent calls tools to operate the Editor
4. (Optional) To use AI texture generation, also configure an image provider (Gemini / OpenAI / ComfyUI)

> Destructive operations (e.g. deletions) prompt for confirmation before running.

### Requirements

- Unity **2022.3** or newer
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk) (UI components)
- VRChat SDK (`com.vrchat.avatars`)

### Links

[Website](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM Repo](https://lighfu.github.io/vpm/)

### License

MIT License — see [LICENSE](LICENSE) for details.

---

## 繁體中文

### 概要

**UnityAgent** 是一款由 LLM（大型語言模型）驅動的 AI 代理，可透過自然語言操作 Unity Editor。內建 **400+ 個**面向 VRChat Avatar 製作的專用工具，將「把衣服非破壞地穿到身體上」「在表情選單新增切換」等指示轉換為實際的 Editor 操作。

### 安裝

#### 透過 ALCOM / VCC 安裝（推薦）

1. 將 [VPM 倉庫](https://lighfu.github.io/vpm/) 新增到 ALCOM / VCC
2. 在專案中新增 **UnityAgent**

#### 手動安裝

從 [Releases](https://github.com/lighfu/unity-agent/releases) 下載最新 zip，並解壓到專案的 `Packages/` 資料夾。

### 使用方式

1. 從 **`Tools ▸ UnityAgent`** 開啟代理視窗
2. 在設定中選擇 LLM 提供者並輸入 API 金鑰（Claude / OpenAI / Gemini / 本機 CLI 等）
3. 以自然語言對話 → 代理會呼叫工具操作 Editor
4. （選用）若要使用 AI 材質生成，請一併設定影像提供者（Gemini / OpenAI / ComfyUI）

> 破壞性操作（如刪除）在執行前會顯示確認對話框。

### 相依與需求

- Unity **2022.3** 以上
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk)（UI 元件）
- VRChat SDK（`com.vrchat.avatars`）

### 連結

[官方網站](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM 倉庫](https://lighfu.github.io/vpm/)

### 授權

MIT License — 詳見 [LICENSE](LICENSE)。

---

## 简体中文

### 概要

**UnityAgent** 是一款由 LLM（大语言模型）驱动的 AI 代理，可通过自然语言操作 Unity Editor。内置 **400+ 个**面向 VRChat 头像制作的专用工具，将“把衣服非破坏地穿到身体上”“在表情菜单添加开关”等指示转换为实际的 Editor 操作。

### 安装

#### 通过 ALCOM / VCC 安装（推荐）

1. 将 [VPM 仓库](https://lighfu.github.io/vpm/) 添加到 ALCOM / VCC
2. 在项目中添加 **UnityAgent**

#### 手动安装

从 [Releases](https://github.com/lighfu/unity-agent/releases) 下载最新 zip，并解压到项目的 `Packages/` 文件夹。

### 使用方法

1. 从 **`Tools ▸ UnityAgent`** 打开代理窗口
2. 在设置中选择 LLM 提供商并输入 API 密钥（Claude / OpenAI / Gemini / 本地 CLI 等）
3. 用自然语言对话 → 代理会调用工具操作 Editor
4. （可选）若要使用 AI 纹理生成，请一并配置图像提供商（Gemini / OpenAI / ComfyUI）

> 破坏性操作（如删除）在执行前会显示确认对话框。

### 依赖与要求

- Unity **2022.3** 及以上
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk)（UI 组件）
- VRChat SDK（`com.vrchat.avatars`）

### 链接

[官方网站](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM 仓库](https://lighfu.github.io/vpm/)

### 许可证

MIT License — 详见 [LICENSE](LICENSE)。

---

<div align="center">
<sub>Built for VRChat creators · 🤝 co-developed with Claude (Anthropic) · © AjisaiFlow — MIT</sub>
</div>
