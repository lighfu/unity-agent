<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/unityagent-dark.png">
  <img alt="UnityAgent" src="docs/assets/unityagent-light.png" width="440">
</picture>

### AI-powered Unity Editor agent for VRChat avatar creation

Control the Unity Editor in natural language — **400+ tools** specialized for VRChat avatars.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![Release](https://img.shields.io/github/v/release/lighfu/unity-agent?label=release&color=8a2be2)](https://github.com/lighfu/unity-agent/releases)
[![VPM](https://img.shields.io/badge/VPM-ALCOM%20%2F%20VCC-orange)](https://lighfu.github.io/vpm/)
[![Stars](https://img.shields.io/github/stars/lighfu/unity-agent?style=social)](https://github.com/lighfu/unity-agent/stargazers)

[**🌐 Website**](https://lighfu.github.io/unity-agent/) · [**📦 Releases**](https://github.com/lighfu/unity-agent/releases) · [**🧩 VPM Repo**](https://lighfu.github.io/vpm/)

**English** · [日本語](README.ja.md) · [繁體中文](README.zh-TW.md) · [简体中文](README.zh-CN.md)

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

---

## Overview

**UnityAgent** is an AI agent that lets you control the Unity Editor with natural language. It ships **400+ tools** specialized for VRChat avatar creation, turning instructions like *"dress this body non-destructively"* or *"add a toggle to the expression menu"* into real Editor actions.

## Installation

### Via ALCOM / VCC (recommended)

1. Add the [VPM repository](https://lighfu.github.io/vpm/) to ALCOM / VCC
2. Add **UnityAgent** to your project

### Manual installation

Download the latest zip from [Releases](https://github.com/lighfu/unity-agent/releases) and extract it into your `Packages/` folder.

## Usage

1. Open the agent window from **`Tools ▸ UnityAgent`**
2. In settings, choose an LLM provider and enter its API key (Claude / OpenAI / Gemini / local CLI, etc.)
3. Chat in natural language — the agent calls tools to operate the Editor
4. *(Optional)* To use AI texture generation, also configure an image provider (Gemini / OpenAI / ComfyUI)

> Destructive operations (e.g. deletions) prompt for confirmation before running.

## Requirements

- Unity **2022.3** or newer
- [MD3 SDK](https://github.com/lighfu/unity-md3sdk) (UI components)
- VRChat SDK (`com.vrchat.avatars`)

## Links

[Website](https://lighfu.github.io/unity-agent/) · [Releases](https://github.com/lighfu/unity-agent/releases) · [VPM Repo](https://lighfu.github.io/vpm/)

## License

MIT License — see [LICENSE](LICENSE) for details.

---

<div align="center">
<sub>Built for VRChat creators · 🤝 co-developed with Claude (Anthropic) · © AjisaiFlow — MIT</sub>
</div>
