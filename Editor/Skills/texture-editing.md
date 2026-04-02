---
title: Texture Editing & AI Generation
description: Mesh island-based texture color changes and AI image generation for partial texture replacement
tags: texture, color change, gradient, AI generation, island, HSV, paint
---

# Texture Editing & AI Generation

## Overview

Edit avatar textures (main color, emission, normal map, etc.)
on a per-mesh-island basis. Supports color adjustments through AI image generation.

## Quick Reference

### Common Operations
- Recolor: [ApplyGradientEx("go", "#FF0000", "#FF0000", blendMode="tint")]
- Lighten: [ApplyGradientEx("go", "#FFFFFF", "#FFFFFF", blendMode="screen")]
- Gradient: [ApplyGradientEx("go", "#FF0000", "#0000FF")]
- Brighten: [AdjustHSV("go", 0, 1, 1.5)]
- Darken: [AdjustHSV("go", 0, 1, 0.5)]
- Desaturate: [AdjustHSV("go", 0, 0, 1)]

### Color Formats
- Hex: '#FF0000', '#FF000080' (with alpha)
- Float: '1.0,0.0,0.0' or '1.0,0.0,0.0,1.0'
- Keyword: 'transparent'

### blendMode Guide
| Mode | Effect | Use When |
|------|--------|----------|
| tint | Recolor preserving lightness | "赤/青/ピンクにして" |
| screen | Lighten/brighten | "白/明るくして" |
| overlay | Natural color mixing | "色を足して" |
| multiply | Darken only | "暗くして" |
| replace | Overwrite entirely | 完全上書き |

NEVER use tint/overlay with white → gray. Use screen for white/brightening.

## Workflow

### Pattern A: Color Change (Gradient, HSV, Brightness/Contrast)

1. `ListRenderers(avatarName)` to check renderer list and materials
2. `ListMeshIslands(gameObjectName)` to get island list
3. `EnableIslandSelectionMode(gameObjectName)` to launch Scene view island selection
4. Have the user click on islands
5. `GetSelectedIslands()` to get selected island indices
6. Call editing tool:
   - `ApplyGradientEx(gameObjectName, fromColor, toColor, ...)` — Set specific color, gradient, recolor
   - `AdjustHSV(gameObjectName, hueShift, satScale, valScale, islandIndices)` — Brightness/saturation adjustment only
   - `AdjustBrightnessContrast(gameObjectName, brightness, contrast, islandIndices)` — Brightness/Contrast

**Tool selection guide:**
- "Make it red/blue/pink" → `ApplyGradientEx` with tint or overlay blend
- "Make it brighter/darker" → `AdjustHSV` (valueScale) or `AdjustBrightnessContrast`
- "Make it more vivid/grayscale" → `AdjustHSV` (saturationScale)
- "Add gradient from X to Y" → `ApplyGradientEx`
- **NEVER** use `AdjustHSV` hueShift to set a specific color — it only rotates the existing hue

### Pattern B: AI Image Generation for Texture Replacement

1. `ListRenderers(avatarName)` to check renderer list and materials
2. `ListMeshIslands(gameObjectName)` to get island list
3. Identify target islands (`EnableIslandSelectionMode` → user selection → `GetSelectedIslands()`, or infer from island list)
4. Call `GenerateTextureWithAI`:

```
GenerateTextureWithAI(
  gameObjectName,      // Hierarchy path (e.g., "avatarName/Body")
  prompt,              // Generation prompt (e.g., "make the eyes look like a galaxy nebula")
  islandIndices,       // Island indices (e.g., "5;6")
  materialIndex,       // Material slot number (e.g., 0)
  textureProperty,     // Texture property name (e.g., "_MainTex")
  imageModelName       // AI model name (optional)
)
```

## Parameter Guide

### materialIndex
- Specifies which material slot to use on multi-material renderers
- Shown as `Material[0]`, `Material[1]` ... in `ListRenderers` output

### textureProperty
- Shader texture property name to edit
- Key properties:
  - `_MainTex` — Main color texture (default)
  - `_EmissionMap` — Emission (glow) map
  - `_BumpMap` — Normal map
  - `_ShadowColorTex` — lilToon shadow color texture
- Can be checked in the Texture section of `InspectMaterial(materialPath)`

### islandIndices
- Semicolon-separated: `"0;1;3"`
- Empty string targets the entire texture

## Tool Call Examples

### Example 1: Make Eyes Look Like Space
```
User: "Make the avatar's eyes look like space"

AI:
1. ListRenderers("avatarName") → Confirm Body Material[0]: body contains eyes
2. ListMeshIslands("avatarName/Body") → Island list
3. EnableIslandSelectionMode("avatarName/Body")
4. "Please click on the eye area in the Scene view"
5. GetSelectedIslands() → "5;6"
6. GenerateTextureWithAI("avatarName/Body", "Transform the eye iris area into a cosmic galaxy nebula with deep blue, purple, and sparkles", "5;6", 0, "_MainTex")
```

### Example 2: Make Eyes Glow with Emission
```
AI:
1. (Same island identification as above)
2. GenerateTextureWithAI("avatarName/Body", "Create a glowing nebula emission pattern", "5;6", 0, "_EmissionMap")
```

### Example 3: Hair Gradient
```
AI:
1. ListMeshIslands("avatarName/hair")
2. EnableIslandSelectionMode("avatarName/hair")
3. User selects hair tip islands
4. GetSelectedIslands() → "0;1;2"
5. ApplyGradientEx("avatarName/hair", "1.0,0.8,0.9", "0.5,0.2,0.8", "top_to_bottom", "screen", "0;1;2")
```

## Notes

- **Island selection is done by clicking in Scene view**. Let the user select rather than guessing
- `GenerateTextureWithAI` `textureProperty` defaults to `_MainTex`. Always explicitly specify `_EmissionMap` when editing emission
- AI generation preserves UV structure, so it won't draw in transparent areas. Specifying islands improves accuracy
- For multi-material objects, specify the correct slot with `materialIndex`
- For setting a specific color, use `ApplyGradientEx` (tint/overlay). `AdjustHSV` is for brightness/saturation adjustments only — its hueShift is RELATIVE (rotates existing hue), not absolute

## Troubleshooting

- **Stack Overflow**: `FindGO` bug. Fixed in latest version
- **Texture property not found**: Check property name with `InspectMaterial`
- **AI-generated image is different size**: AI model constraint. System prompt strongly requests same size
- **Emission not glowing**: Material's `_UseEmission` may be 0. Set to 1 with `SetMaterialFloat`
