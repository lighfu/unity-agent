<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/unityagent-dark.png">
  <img alt="UnityAgent" src="docs/assets/unityagent-light.png" width="440">
</picture>

### 面向 VRChat Avatar 製作的 AI Unity Editor 代理

以自然語言操作 Unity Editor — 內建 **400+ 個**面向 VRChat Avatar 的專用工具。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![Release](https://img.shields.io/github/v/release/lighfu/unity-agent?label=release&color=8a2be2)](https://github.com/lighfu/unity-agent/releases)
[![VPM](https://img.shields.io/badge/VPM-ALCOM%20%2F%20VCC-orange)](https://lighfu.github.io/vpm/)
[![Stars](https://img.shields.io/github/stars/lighfu/unity-agent?style=social)](https://github.com/lighfu/unity-agent/stargazers)

[**🌐 官方網站**](https://lighfu.github.io/unity-agent/) · [**📦 Releases**](https://github.com/lighfu/unity-agent/releases) · [**🧩 VPM 倉庫**](https://lighfu.github.io/vpm/)

[English](README.md) · [日本語](README.ja.md) · **繁體中文** · [简体中文](README.zh-CN.md)

</div>

---

## ✨ 功能亮點

| | |
|---|---|
| 🗣️ **自然語言操作** | 透過對話操作 Unity Editor — 代理會呼叫 400+ 個專用工具。 |
| 🧍 **Avatar 必備設定** | 視點(Viewpoint) / Eye Look / Viseme、PhysBone・Contact・Constraint、表情選單與參數。 |
| 🧩 **非破壞工作流程** | 完整整合 Modular Avatar / NDMF / VRCFury(Merge Animator/Armature、Bone Proxy、巢狀選單…)。 |
| 😀 **表情與動畫** | FaceEmo 表情、AnimationClip / Animator / AnimatorAsCode 製作。 |
| 🎨 **網格・材質・貼圖** | 網格/UV/BlendShape/權重編輯、lilToon，以及 **AI 貼圖生成(img2img)**。 |
| 📱 **Quest 與效能** | 最佳化、效能等級診斷、Quest 轉換輔助。 |
| 🔌 **MCP 伺服器** | 透過 Model Context Protocol 公開工具，外部用戶端(如 Claude Code)可操作 Unity。 |
| 🌍 **多語言 UI** | 日本語 / English / 繁體中文 / 简体中文。 |

### 🤖 支援的提供者

| 類型 | 提供者 |
|---|---|
| **LLM** | Anthropic Claude · OpenAI · Google Gemini(含 Vertex AI) · OpenAI 相容端點 · Claude / Codex / Gemini **CLI** |
| **影像(貼圖生成)** | Google Gemini · OpenAI · **ComfyUI**(本機 img2img) |

### 🔗 整合(安裝後自動偵測)

Modular Avatar · NDMF · VRCFury · Avatar Optimizer (AAO) · lilToon · FaceEmo · VRCQuestTools · AnimatorAsCode · BlendShape Modifier · NDMF Mesh Simplifier · Gesture Manager · TexTransTool

---

## 概要

**UnityAgent** 是一款由 LLM(大型語言模型)驅動的 AI 代理，可透過自然語言操作 Unity Editor。內建 **400+ 個**面向 VRChat Avatar 製作的專用工具，將「把衣服非破壞地穿到身體上」「在表情選單新增切換」等指示轉換為實際的 Editor 操作。

## 安裝

### 透過 ALCOM / VCC 安裝(推薦)

1. 將 [VPM 倉庫](https://lighfu.github.io/vpm/) 新增到 ALCOM / VCC
2. 在專案中新增 **UnityAgent**

### 手動安裝

從 [Releases](https://github.com/lighfu/unity-agent/releases) 下載最新 zip，並解壓到專案的 `Packages/` 資料夾。

## 使用方式

1. 從 **`Tools ▸ UnityAgent`** 開啟代理視窗
2. 在設定中選擇 LLM 提供者並輸入 API 金鑰(Claude / OpenAI / Gemini / 本機 CLI 等)
3. 以自然語言對話 → 代理會呼叫工具操作 Editor
4. *(選用)* 若要使用 AI 貼圖生成，請一併設定影像提供者(Gemini / OpenAI / ComfyUI)

> 破壞性操作(如刪除)在執行前會顯示確認對話框。

## 相依與需求

- Unity **2022.3** 以上
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk)(UI 元件)
- VRChat SDK(`com.vrchat.avatars`)

## 連結

[官方網站](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM 倉庫](https://lighfu.github.io/vpm/)

## 授權

MIT License — 詳見 [LICENSE](LICENSE)。

---

<div align="center">
<sub>為 VRChat 創作者打造 · 🤝 與 Claude (Anthropic) 共同開發 · © AjisaiFlow — MIT</sub>
</div>
