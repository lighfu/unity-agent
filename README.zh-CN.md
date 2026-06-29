<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/unityagent-dark.png">
  <img alt="UnityAgent" src="docs/assets/unityagent-light.png" width="440">
</picture>

### 面向 VRChat 头像制作的 AI Unity Editor 代理

用自然语言操作 Unity Editor — 内置 **400+ 个**面向 VRChat 头像的专用工具。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![Release](https://img.shields.io/github/v/release/lighfu/unity-agent?label=release&color=8a2be2)](https://github.com/lighfu/unity-agent/releases)
[![VPM](https://img.shields.io/badge/VPM-ALCOM%20%2F%20VCC-orange)](https://lighfu.github.io/vpm/)
[![Stars](https://img.shields.io/github/stars/lighfu/unity-agent?style=social)](https://github.com/lighfu/unity-agent/stargazers)

[**🌐 官方网站**](https://lighfu.github.io/unity-agent/) · [**📦 Releases**](https://github.com/lighfu/unity-agent/releases) · [**🧩 VPM 仓库**](https://lighfu.github.io/vpm/)

[English](README.md) · [日本語](README.ja.md) · [繁體中文](README.zh-TW.md) · **简体中文**

</div>

---

## ✨ 功能亮点

| | |
|---|---|
| 🗣️ **自然语言操作** | 通过对话操作 Unity Editor — 代理会调用 400+ 个专用工具。 |
| 🧍 **头像必备设置** | 视点(Viewpoint) / Eye Look / Viseme、PhysBone・Contact・Constraint、表情菜单与参数。 |
| 🧩 **非破坏工作流** | 深度集成 Modular Avatar / NDMF / VRCFury(Merge Animator/Armature、Bone Proxy、嵌套菜单…)。 |
| 😀 **表情与动画** | FaceEmo 表情、AnimationClip / Animator / AnimatorAsCode 制作。 |
| 🎨 **网格・材质・贴图** | 网格/UV/BlendShape/权重编辑、lilToon，以及 **AI 纹理生成(img2img)**。 |
| 📱 **Quest 与性能** | 优化、性能等级诊断、Quest 转换辅助。 |
| 🔌 **MCP 服务器** | 通过 Model Context Protocol 公开工具，外部客户端(如 Claude Code)可操作 Unity。 |
| 🌍 **多语言 UI** | 日本語 / English / 繁體中文 / 简体中文。 |

### 🤖 支持的提供商

| 类型 | 提供商 |
|---|---|
| **LLM** | Anthropic Claude · OpenAI · Google Gemini(含 Vertex AI) · OpenAI 兼容端点 · Claude / Codex / Gemini **CLI** |
| **图像(纹理生成)** | Google Gemini · OpenAI · **ComfyUI**(本地 img2img) |

### 🔗 集成(安装后自动检测)

Modular Avatar · NDMF · VRCFury · Avatar Optimizer (AAO) · lilToon · FaceEmo · VRCQuestTools · AnimatorAsCode · BlendShape Modifier · NDMF Mesh Simplifier · Gesture Manager · TexTransTool

---

## 概要

**UnityAgent** 是一款由 LLM(大语言模型)驱动的 AI 代理，可通过自然语言操作 Unity Editor。内置 **400+ 个**面向 VRChat 头像制作的专用工具，将“把衣服非破坏地穿到身体上”“在表情菜单添加开关”等指示转换为实际的 Editor 操作。

## 安装

### 通过 ALCOM / VCC 安装(推荐)

1. 将 [VPM 仓库](https://lighfu.github.io/vpm/) 添加到 ALCOM / VCC
2. 在项目中添加 **UnityAgent**

### 手动安装

从 [Releases](https://github.com/lighfu/unity-agent/releases) 下载最新 zip，并解压到项目的 `Packages/` 文件夹。

## 使用方法

1. 从 **`Tools ▸ UnityAgent`** 打开代理窗口
2. 在设置中选择 LLM 提供商并输入 API 密钥(Claude / OpenAI / Gemini / 本地 CLI 等)
3. 用自然语言对话 → 代理会调用工具操作 Editor
4. *(可选)* 若要使用 AI 纹理生成，请一并配置图像提供商(Gemini / OpenAI / ComfyUI)

> 破坏性操作(如删除)在执行前会显示确认对话框。

## 依赖与要求

- Unity **2022.3** 及以上
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk)(UI 组件)
- VRChat SDK(`com.vrchat.avatars`)

## 链接

[官方网站](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM 仓库](https://lighfu.github.io/vpm/)

## 许可证

MIT License — 详见 [LICENSE](LICENSE)。

---

<div align="center">
<sub>为 VRChat 创作者打造 · 🤝 与 Claude (Anthropic) 共同开发 · © AjisaiFlow — MIT</sub>
</div>
