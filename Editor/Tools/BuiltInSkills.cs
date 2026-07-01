using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Built-in skill definitions embedded in code for DLL distribution.
    /// To update: edit the .md files in Skills/ folder, then regenerate this file.
    /// </summary>
    internal static class BuiltInSkills
    {
        internal static readonly Dictionary<string, string> All = new Dictionary<string, string>
        {
            { "avatar-build", @"---
title: VRChat Avatar Build
description: Build and upload avatars using the VRChat SDK
tags: VRChat, build, upload, SDK
---

# VRChat Avatar Build Procedure

## Overview
Build and upload avatars using the VRChat SDK Control Panel.
Perform performance validation before building and prompt fixes if issues are found.

## Prerequisites
- VRChat SDK (com.vrchat.avatars) is installed
- Avatar has a VRCAvatarDescriptor configured
- Logged in to VRChat SDK

## Procedure

### 1. Performance Validation
First, check the avatar's performance:
```
<tool name=""GetAvatarPerformanceStats"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```

### 2. AvatarDescriptor Check
Verify the configuration is correct:
```
<tool name=""InspectVRCAvatarDescriptor"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```

### 3. Common Issues to Check
- ViewPosition is between the eyes
- LipSync is correctly configured
- ExpressionParameters cost is within 256 bits

### 4. Execute Build
Open the SDK Control Panel:
```
<tool name=""ExecuteMenu"">
<arg name=""menuPath"">VRChat SDK/Show Control Panel</arg>
</tool>
```

**Note**: The actual build and upload must be done manually by the user in the SDK Control Panel.
The AI supports up to opening the Control Panel and guides the user through the process.

### 5. Post-Build Guidance
Tell the user:
- Select the ""Build & Publish"" tab in the Control Panel
- Select the avatar
- Use ""Build & Test"" for local testing, or ""Build & Publish"" to upload

## Performance Rank Thresholds (PC)
| Category | Excellent | Good | Medium | Poor |
|----------|-----------|------|--------|------|
| Triangles | ≤32,000 | ≤70,000 | ≤70,000 | ≤70,000 |
| Materials | ≤4 | ≤8 | ≤16 | ≤32 |
| PhysBone | ≤4 | ≤8 | ≤16 | ≤32 |
| Bones | ≤75 | ≤150 | ≤256 | ≤400 |

## Troubleshooting
- ""SDK not found"" → VRChat SDK package is not installed
- Build errors → Check the Console window for errors
- Not logged in → Login required in SDK Control Panel" },

            { "modular-avatar", @"---
title: Modular Avatar Setup
description: Non-destructive outfit and gimmick integration using Modular Avatar
tags: Modular Avatar, MA, outfit, non-destructive, setup
---

# Modular Avatar Setup

## Overview
Use Modular Avatar (MA) to non-destructively integrate outfits and gimmicks into an avatar.
Simply place the Prefab as a child of the avatar, and it will be automatically merged at build time.

## Installation Check
Package: `nadena.dev.modular-avatar`

## Key Components

### MA Merge Armature
Non-destructively merges the outfit's Armature (bone structure) into the avatar's Armature.
```
<tool name=""AddMAMergeArmature"">
<arg name=""goName"">outfitName/Armature</arg>
<arg name=""mergeTargetName"">avatarRootName/Armature</arg>
<arg name=""prefix""></arg>
<arg name=""suffix""></arg>
</tool>
```
- goName = the outfit's Armature object; mergeTargetName = the avatar's Armature (root bone).
- If bone names don't match, pass prefix/suffix (e.g. suffix '.1' for bones named 'Hips.1').
- Bones merge automatically at build time.

### MA Merge Animator
Integrates an Animator Controller into a playable layer (non-destructive).
```
<tool name=""AddMAMergeAnimator"">
<arg name=""goName"">gimmickHolder</arg>
<arg name=""controllerPath"">Assets/Anim/gimmick.controller</arg>
<arg name=""layerType"">FX</arg>
<arg name=""pathMode"">0</arg>
<arg name=""matchWriteDefaults"">true</arg>
</tool>
```
- layerType: FX (default), Gesture, Action, Base, Additive, Sitting, TPose, IKPose.
- pathMode: 0=Relative (MA default) / 1=Absolute. matchWriteDefaults=true keeps WD consistent (see Notes).

### MA Menu Item / MA Parameters
Non-destructively adds Expression Menu and Parameters.
- Single entry on an existing object (iconPath optional):
  ```
  <tool name=""AddMenuItem"">
  <arg name=""goName"">Toggle_Hat</arg>
  <arg name=""type"">Toggle</arg>
  <arg name=""paramName"">Hat</arg>
  <arg name=""value"">1</arg>
  <arg name=""synced"">true</arg>
  <arg name=""saved"">true</arg>
  <arg name=""isDefault"">false</arg>
  <arg name=""iconPath"">Assets/Icons/hat.png</arg>
  </tool>
  ```
- Nested submenu (container + children; nest deeper via a SubMenu entry):
  ```
  <tool name=""CreateMAMenu"">
  <arg name=""avatarRootName"">avatarRootName</arg>
  <arg name=""menuName"">Outfits</arg>
  </tool>
  <tool name=""AddMAMenuItemUnder"">
  <arg name=""parentMenuName"">Outfits</arg>
  <arg name=""displayName"">Dress</arg>
  <arg name=""type"">Toggle</arg>
  <arg name=""paramName"">Dress</arg>
  </tool>
  <tool name=""AddMAMenuItemUnder"">
  <arg name=""parentMenuName"">Outfits</arg>
  <arg name=""displayName"">Colors</arg>
  <arg name=""type"">SubMenu</arg>
  </tool>
  <tool name=""AddMAMenuItemUnder"">
  <arg name=""parentMenuName"">Colors</arg>
  <arg name=""displayName"">Red</arg>
  <arg name=""type"">Toggle</arg>
  <arg name=""paramName"">ColorRed</arg>
  </tool>
  ```

### MA Bone Proxy
Non-destructively places objects as children of specific bones.
Used for making weapons or accessories follow the hand or Head.
```
<tool name=""AddMABoneProxy"">
<arg name=""goName"">weaponName</arg>
<arg name=""targetBoneName"">RightHand</arg>
<arg name=""mode"">1</arg>
</tool>
```
- mode 1=AsChildAtRoot (snaps to the bone). To preserve the object's current world placement, use mode 2 (or run AlignAccessoryToBone first).
- For ring/finger accessories, AttachRingWithBoneProxy is a convenience wrapper.

## General Outfit Setup Procedure

1. Place the outfit Prefab as a child of the avatar:
   ```
   <tool name=""SetParent"">
   <arg name=""childName"">outfitName</arg>
   <arg name=""parentName"">avatarRootName</arg>
   </tool>
   ```

2. Verify MA Merge Armature is configured on the outfit's Armature:
   ```
   <tool name=""InspectGameObject"">
   <arg name=""gameObjectName"">avatarRootName/outfitName/Armature</arg>
   </tool>
   ```
   If it's missing, add it:
   ```
   <tool name=""AddMAMergeArmature"">
   <arg name=""goName"">avatarRootName/outfitName/Armature</arg>
   <arg name=""mergeTargetName"">avatarRootName/Armature</arg>
   <arg name=""prefix""></arg>
   <arg name=""suffix""></arg>
   </tool>
   ```

3. Prevent body mesh clipping:
   - Use AAO's Remove Mesh By Box to remove body mesh under the clothing
   - Or use BlendShapes to shrink the body

4. Material adjustments:
   - Check that outfit materials match the avatar's skin color
   - Adjust texture colors as needed

## Notes
- Write Defaults: keep consistent across the entire avatar — all states ON or all OFF (VRChat treats all Playable Layer controllers as one controller, so don't mix across FX/Gesture/Action; mixed WD behaves like all-OFF and makes properties stick / expressions fail to reset; the SDK only warns).
- Exception (non-negotiable): Direct Blend Tree single-state layers and additive-blending layers must ALWAYS be WD ON, even on an all-OFF avatar (WD OFF makes their values blow up toward infinity); the SDK excludes these from mixed-WD warnings.
- MA note: Merge Animator's ""Match Avatar Write Defaults"" (default ON since 1.16.1) only matches the avatar's existing WD — it will NOT fix an already-mixed avatar. Only VRCFury enforces a single WD value avatar-wide.
- If you choose all-OFF: every state needs a clip/blend tree, and any layer animating Transforms needs an Avatar Mask.
- Bones won't merge if names don't match → Use MA Merge Armature settings to resolve
- For Quest builds, watch parameter count from MA-generated animator layers" },

            { "face-emo", @"---
title: FaceEmo Expression Menu Setup
description: Build, edit, and configure gesture-based expression menus using FaceEmo
tags: FaceEmo, expression, Expression Menu, gesture, non-destructive
---

# FaceEmo Expression Menu Setup

## Overview
FaceEmo (`jp.suzuryg.face-emo`) is a non-destructive expression menu tool for VRChat Avatars 3.0.
It manages gesture-to-AnimationClip switching and generates FX layers via NDMF/Modular Avatar.
- **Registered** expressions: max **6** (shown in Expression Menu; a folder counts as 1 slot and holds up to 8)
- **Unregistered** expressions: unlimited (not in menu, gesture-only)

## ⚠️ CRITICAL RULES (NEVER VIOLATE)

1. **NEVER guess mesh names.** Always call `IdentifyFaceSmr` or `AnalyzeFaceBlendShapes` first.
   Aliases like `'Body'` or `'Face'` are unreliable — Chiffon-style avatars name the face mesh `Body`.
2. **BlendShape weights use 0-100, NEVER 0-1.** Do not confuse with VRChat Expression Parameter floats.
   Passing `eye_joy=0.8` instead of `eye_joy=80` results in invisible expression changes.
3. **Run AnalyzeFaceBlendShapes once per avatar before any preview/build work.**
   It caches face SMR + extra SMRs (eyelash/tongue) + categorized shapes + preset candidates,
   so subsequent calls are instant and consistent.
4. **Use SetExpressionPreviewMulti, not SetExpressionPreview, for cross-SMR expressions.**
   Some avatars split eyes/lashes/tongue across multiple SMRs. SetExpressionPreviewMulti
   auto-routes shapes to the correct SMR via the cached profile.

## Tool Reference

### Detection & Inspection
```
<tool name=""FindFaceEmo""></tool> — Discover FaceEmo objects in scene
<tool name=""InspectFaceEmo"">
<arg name=""gameObjectName"">FaceEmo</arg>
</tool> — Show AV3 settings, expression modes, gestures
<tool name=""ListFaceEmoExpressions"">
<arg name=""gameObjectName"">FaceEmo</arg>
</tool> — List all expressions and branches
<tool name=""InspectExpressionDetail"">
<arg name=""expressionName"">Angry</arg>
</tool> — Detailed info (branches, conditions, animations)
<tool name=""LaunchFaceEmoWindow"">
<arg name=""gameObjectName"">FaceEmo</arg>
</tool> — Open the FaceEmo editor window
```

### ★ Face Profile (always call first)
```
<tool name=""IdentifyFaceSmr"">
<arg name=""avatarRootName"">Avatar</arg>
</tool> — Pick face SMR by viseme count + bone heuristic
<tool name=""AnalyzeFaceBlendShapes"">
<arg name=""avatarRootName"">Avatar</arg>
</tool> — Build profile: face SMR + extras + categorized + presets
<tool name=""SuggestExpressionShapes"">
<arg name=""avatarRootName"">Avatar</arg>
<arg name=""intent"">smile</arg>
</tool> — Get preset shape mix (returns 'shape=value;...')
<tool name=""SearchExpressionShapesV2"">
<arg name=""avatarRootName"">Avatar</arg>
<arg name=""categories"">eye,mouth,brow</arg>
</tool> — Multi-category search across SMRs
```

### Expression Building (preferred — multi-SMR safe)
```
<tool name=""SetExpressionPreviewMulti"">
<arg name=""avatarRootName"">Avatar</arg>
<arg name=""blendShapeData"">eye_joy=80;mouth_smile=100</arg>
</tool> — Apply across SMRs (values 0-100)
<tool name=""CaptureFacePreview"">
<arg name=""avatarRootName"">Avatar</arg>
</tool> — Stable face capture (dedicated camera, fixed FOV/distance)
<tool name=""GetCurrentExpressionValues"">
<arg name=""meshObjectName""><faceSmrPath></arg>
</tool> — Inspect current non-zero values per SMR
```

### Expression Building (legacy — single SMR fallback)
```
<tool name=""SearchExpressionShapes"">
<arg name=""meshObjectName""><faceSmrPath></arg>
<arg name=""filter"">eye</arg>
</tool> — Single-mesh search (synonym expansion only)
<tool name=""SetExpressionPreview"">
<arg name=""meshObjectName""><faceSmrPath></arg>
<arg name=""blendShapeData"">eye_joy=100;mouth_smile=80</arg>
</tool> — Single-mesh preview
<tool name=""CaptureExpressionPreview"">
<arg name=""avatarRootName"">Avatar</arg>
</tool> — SceneView-dependent capture (less stable)
<tool name=""ResetExpressionPreview"">
<arg name=""meshObjectName""><faceSmrPath></arg>
</tool> — Reset blend shapes on a single SMR
```

### Thumbnail Capture (Plan B)
```
<tool name=""CaptureFaceEmoModeThumbnail"">
<arg name=""modeName"">Smile</arg>
</tool> — Save Mode face thumbnail as PNG (for AI response embedding)
<tool name=""CaptureFaceEmoGestureTable"">
<arg name=""modeName"">Smile</arg>
</tool> — Save 4×2 grid of all 8 gesture variants
<tool name=""CaptureFaceEmoExMenuThumbnail"">
<arg name=""modeName"">Smile</arg>
</tool> — Save the VRChat menu image
<tool name=""RefreshFaceEmoMainView""></tool> — Force-refresh FaceEmo MainView thumbnails after edits
```

### Expression Registration & Management
```
<tool name=""AddExpression"">
<arg name=""displayName"">Angry</arg>
<arg name=""destination"">Registered</arg>
<arg name=""animationClipPath"">Assets/.../angry.anim</arg>
</tool> — Add new expression
<tool name=""RemoveExpression"">
<arg name=""displayName"">Angry</arg>
</tool> — Remove expression (with confirmation)
<tool name=""CopyExpression"">
<arg name=""sourceExpressionName"">Smile</arg>
<arg name=""newDisplayName"">Smile2</arg>
<arg name=""destination"">Registered</arg>
</tool> — Duplicate expression
<tool name=""SetExpressionAnimation"">
<arg name=""expressionName"">Angry</arg>
<arg name=""animationClipPath"">Assets/.../angry.anim</arg>
</tool> — Set/change animation clip
<tool name=""ModifyExpressionProperties"">
<arg name=""expressionName"">Angry</arg>
<arg name=""newDisplayName"">Angry_v2</arg>
</tool> — Modify properties
<tool name=""SetDefaultExpression"">
<arg name=""expressionName"">Smile</arg>
</tool> — Set default expression
<tool name=""CreateAndRegisterExpression"">
<arg name=""meshObjectName"">Body</arg>
<arg name=""expressionName"">Smile</arg>
<arg name=""animPath"">Assets/.../smile.anim</arg>
</tool> — Save current mesh weights as clip + register in one step
<tool name=""CreateExpressionFromData"">
<arg name=""displayName"">Smile</arg>
<arg name=""animPath"">Assets/.../smile.anim</arg>
<arg name=""meshPath"">Body</arg>
<arg name=""blendShapeData"">eye_joy=100;mouth_smile=80</arg>
</tool> — Create clip from explicit data + register
<tool name=""UpdateExpressionAnimation"">
<arg name=""expressionName"">Smile</arg>
<arg name=""meshObjectName"">Body</arg>
<arg name=""animPath"">Assets/.../smile_v2.anim</arg>
</tool> — Re-create clip from current mesh weights + update existing expression
<tool name=""PreviewFaceEmoExpression"">
<arg name=""expressionName"">Smile</arg>
<arg name=""meshObjectName"">Body</arg>
</tool> — Preview existing expression on mesh
```

### Gesture Branch Management
```
<tool name=""AddGestureBranch"">
<arg name=""expressionName"">Angry</arg>
<arg name=""conditions"">Left=Fist</arg>
<arg name=""baseAnimationPath"">Assets/.../angry.anim</arg>
</tool> — Add gesture branch
<tool name=""RemoveGestureBranch"">
<arg name=""expressionName"">Angry</arg>
<arg name=""branchIndex"">0</arg>
</tool> — Remove branch by index
<tool name=""AddGestureCondition"">
<arg name=""expressionName"">Angry</arg>
<arg name=""branchIndex"">0</arg>
<arg name=""hand"">Right</arg>
<arg name=""gesture"">Fist</arg>
</tool> — Add condition to branch
<tool name=""ModifyBranchProperties"">
<arg name=""expressionName"">Angry</arg>
<arg name=""branchIndex"">0</arg>
<arg name=""eyeTracking"">Animation</arg>
</tool> — Modify branch properties
```
Condition format: `'Left=Fist;Right=Victory'` or `'Either!=Neutral'`
Hand: Left / Right / Either / Both / OneSide
| ID | Gesture | Japanese aliases | Operation |
|----|---------|-----------------|-----------|
| 0 | Neutral | ニュートラル, 何もしない | No input |
| 1 | Fist | グー, 握り拳, フィスト | Full trigger press |
| 2 | HandOpen | パー, 手を開く, ハンドオープン | All fingers open |
| 3 | FingerPoint | 人差し指, 指差し, ポインティング | Index finger only |
| 4 | Victory | ピース, Vサイン, チョキ | Index + middle finger |
| 5 | RockNRoll | ロック, メロイックサイン, きつねサイン | Pinky + index finger |
| 6 | HandGun | 指鉄砲, 銃, ハンドガン | Thumb + index finger |
| 7 | ThumbsUp | サムズアップ, 親指, いいね | Thumb only |

### Menu Structure
```
<tool name=""CreateExpressionGroup"">
<arg name=""displayName"">Combat</arg>
<arg name=""destination"">Registered</arg>
</tool> — Create submenu group
<tool name=""MoveExpressionItem"">
<arg name=""itemName"">Angry</arg>
<arg name=""destination"">Unregistered</arg>
</tool> — Move/reorder items
```

### Import & Apply
```
<tool name=""ImportExpressions""></tool> — Import from avatar's existing FX layer (patterns + blink + mouth morph + contacts + prefix). Requires target avatar to be set first.
<tool name=""ApplyFaceEmoToAvatar""></tool> — Generate FX layer from FaceEmo menu. Run after finishing all edits.
```

### Settings
```
<tool name=""ConfigureTargetAvatar"">
<arg name=""avatarName"">Chiffon</arg>
</tool> — Set target avatar (fixes Avatar=None)
<tool name=""ConfigureFaceEmoGeneration""></tool> — View/change generation settings
<tool name=""ConfigureMouthMorphs"">
<arg name=""action"">list</arg>
</tool> — Configure mouth morph BlendShapes
<tool name=""ConfigureAfkFace""></tool> — Configure AFK expression
<tool name=""ConfigureFeatureToggles""></tool> — Configure feature toggles
```

## Preconditions (REQUIRED)

All expression-modifying tools require:
1. FaceEmo (`jp.suzuryg.face-emo`) installed
2. A `FaceEmoLauncher` in the scene (created via `ExecuteMenu('FaceEmo/New Menu')`)
3. `TargetAvatar` configured on the launcher (via `ConfigureTargetAvatar`)

If any precondition is missing, tools return an Error with the recovery step.
Analysis tools (AnalyzeFaceBlendShapes, SearchExpressionShapesV2) are read-only and NOT gated.

## Workflows

### A. Setup FaceEmo on Avatar (""FaceEmoを適用して"")
```
1. <tool name=""FindFaceEmo""></tool> → Check if FaceEmo exists and is configured for this avatar
2. If already configured → skip to step 6
3. If not found: <tool name=""ExecuteMenu"">
   <arg name=""menuPath"">FaceEmo/New Menu</arg>
   </tool>
4. <tool name=""ConfigureTargetAvatar"">
   <arg name=""avatarName"">AvatarName</arg>
   </tool> → Set target avatar
5. <tool name=""ImportExpressions""></tool> → Auto-import expressions from existing FX layer
6. <tool name=""LaunchFaceEmoWindow""></tool> → Open FaceEmo editor window
```
This is the primary workflow when user asks to ""apply"" or ""set up"" FaceEmo on an avatar.
ImportExpressions reads the existing FX Animator and recreates the expression setup in FaceEmo.

### B. Create Expression from Preset (RECOMMENDED — fastest path) (""笑顔の表情を作って"")
```
1. <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool>
2. <tool name=""OpenExpressionSession"">
   <arg name=""newName"">Smile</arg>
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → MainWindow + ExpressionEditor を開く (Live セッション。avatarRootName を必ず渡すこと — 渡さないと scene の最初の configured launcher が選ばれ、別 avatar の menu に commit されてしまう)
3. <tool name=""SuggestExpressionShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""intent"">smile</arg>
   </tool> → 'shape1=80;shape2=100;...' を取得
4. <tool name=""SetExpressionPreviewMulti"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""blendShapeData""><shapeData></arg>
   </tool> → ExpressionEditor のライブプレビューに即反映
5. (任意) <tool name=""ReadExpressionFromWindow""></tool> → ユーザーが手で動かしたスライダーを取り込む
6. <tool name=""CommitExpressionSession""></tool> → .anim 保存 + FaceEmo Menu に登録
7. <tool name=""RefreshFaceEmoMainView""></tool> → MainView の Mode サムネを最新化
8. <tool name=""CaptureFaceEmoModeThumbnail"">
   <arg name=""modeName"">Smile</arg>
   </tool> → 登録された Mode のサムネを PNG として保存し、AI 応答に画像添付
9. <tool name=""ApplyFaceEmoToAvatar""></tool> → FX レイヤー生成
```
This is the canonical flow. FaceEmo and a configured launcher+avatar are REQUIRED.
Note: CaptureFaceEmoModeThumbnail / CaptureFaceEmoGestureTable / CaptureFaceEmoExMenuThumbnail all require the Mode to be COMMITTED to the FaceEmo menu first — call them after CommitExpressionSession.
IMPORTANT: In Live session mode, SetExpressionPreviewMulti writes to FaceEmo's ExpressionEditor preview, NOT the scene mesh. CaptureFacePreview / GetActiveBlendShapes will show the unmodified mesh and look like nothing happened. Trust the SetExpressionPreviewMulti success message; verify visually with CaptureFaceEmoModeThumbnail AFTER Commit.
intent keywords supported: smile / angry / surprised / sad / cry / wink / sleep / kiss / shy
plus Japanese aliases (笑顔/怒り/驚き/...). If preset confidence is low, fall back to Workflow C.

### C. Create Expression Manually (preset miss / fine-tuning) (""人差し指で驚いた表情にして"")
```
1. <tool name=""OpenExpressionSession"">
   <arg name=""newName"">Surprised</arg>
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → ExpressionEditor を開く (avatarRootName 必須)
2. <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → SMR / カテゴリ確認
3. <tool name=""SearchExpressionShapesV2"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""categories"">eye,mouth,brow</arg>
   </tool> → カテゴリ別 shape 候補
4. <tool name=""SetExpressionPreviewMulti"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""blendShapeData"">eye_surprised=100;mouth_open=60;brow_up=80</arg>
   </tool> → ライブ反映
5. (任意) <tool name=""ReadExpressionFromWindow""></tool> → 手調整を取り込む
6. <tool name=""CommitExpressionSession""></tool> → 保存・登録
```
If overwriting: remove the old expression first with <tool name=""RemoveExpression""></tool>, then proceed from step 2.
NEVER pass `'Body'` literally — derive `<faceSmrPath>` from AnalyzeFaceBlendShapes output.

### D. Edit Existing Expression
```
1. <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Get face SMR path
2. <tool name=""PreviewFaceEmoExpression"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""meshObjectName""><faceSmrPath></arg>
   </tool> → Preview current expression
3. <tool name=""SearchExpressionShapesV2"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""categories"">mouth</arg>
   </tool> → Find shapes to adjust
4. <tool name=""SetExpressionPreviewMulti"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""blendShapeData"">mouth_smile=100;mouth_open=30</arg>
   </tool> → Adjust (values 0-100)
5. <tool name=""CaptureFacePreview"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Verify
6. <tool name=""UpdateExpressionAnimation"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""meshObjectName""><faceSmrPath></arg>
   <arg name=""animPath"">Assets/.../smile_v2.anim</arg>
   </tool> → Update clip + FaceEmo
```

### E. Register Existing .anim File
```
1. <tool name=""AddExpression"">
   <arg name=""displayName"">Angry</arg>
   <arg name=""destination"">Registered</arg>
   <arg name=""animationClipPath"">Assets/.../angry.anim</arg>
   </tool>
2. <tool name=""AddGestureBranch"">
   <arg name=""expressionName"">Angry</arg>
   <arg name=""conditions"">Left=Fist</arg>
   </tool>
```

### F. Organize Menu
```
1. <tool name=""ListFaceEmoExpressions""></tool> → List all
2. <tool name=""CreateExpressionGroup"">
   <arg name=""displayName"">Combat</arg>
   <arg name=""destination"">Registered</arg>
   </tool>
3. <tool name=""MoveExpressionItem"">
   <arg name=""itemName"">Angry</arg>
   <arg name=""destination"">Combat</arg>
   </tool>
```

### G. Apply to Avatar
```
1. <tool name=""ApplyFaceEmoToAvatar""></tool> → Generate FX layer
```
Run this after finishing all expression edits to generate the FX layer and parameters.

### H. Preview / List Registered Expressions (""今どんな表情が入ってる？"")
```
1. <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Get face SMR path
2. <tool name=""ListFaceEmoExpressions""></tool> → List all expressions with gesture assignments
3. For each expression the user wants to see:
   <tool name=""PreviewFaceEmoExpression"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""meshObjectName""><faceSmrPath></arg>
   </tool> → Apply blend shapes to mesh
   <tool name=""CaptureFacePreview"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Capture face image for user
```

### I. Delete Expression (""怒りの表情を消して"")
```
1. <tool name=""ListFaceEmoExpressions""></tool> → Confirm the expression exists
2. <tool name=""AskUser""></tool> → Confirm deletion with user
3. <tool name=""RemoveExpression"">
   <arg name=""displayName"">Angry</arg>
   </tool> → Remove (tool has built-in confirmation)
```

### J. Swap Gestures Between Expressions (""笑顔と怒りのジェスチャーを入れ替えて"")
```
1. <tool name=""InspectExpressionDetail"">
   <arg name=""expressionName"">Smile</arg>
   </tool> → Get current gesture conditions
   <tool name=""InspectExpressionDetail"">
   <arg name=""expressionName"">Angry</arg>
   </tool> → Get current gesture conditions
2. <tool name=""RemoveGestureBranch"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""branchIndex"">0</arg>
   </tool> → Remove old branch from Smile
   <tool name=""RemoveGestureBranch"">
   <arg name=""expressionName"">Angry</arg>
   <arg name=""branchIndex"">0</arg>
   </tool> → Remove old branch from Angry
3. <tool name=""AddGestureBranch"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""conditions""><Angry's old condition></arg>
   </tool> → Assign Angry's gesture to Smile
   <tool name=""AddGestureBranch"">
   <arg name=""expressionName"">Angry</arg>
   <arg name=""conditions""><Smile's old condition></arg>
   </tool> → Assign Smile's gesture to Angry
```

### K. Set Default Expression (""何もしてないときの表情を笑顔にして"")
```
1. <tool name=""ListFaceEmoExpressions""></tool> → Find the expression
2. <tool name=""SetDefaultExpression"">
   <arg name=""expressionName"">Smile</arg>
   </tool> → Set as default (Neutral gesture)
```
The default expression is shown when no gesture is active.

### L. Configure AFK Expression (""AFK中は寝顔にして"")
```
1. Check if the desired .anim clip already exists
2. If not → create it first:
   <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Get profile
   <tool name=""SuggestExpressionShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""intent"">sleep</arg>
   </tool> → Get preset shapeData
   <tool name=""SetExpressionPreviewMulti"">
   <arg name=""avatarRootName"">Avatar</arg>
   <arg name=""blendShapeData""><shapeData></arg>
   </tool> → Apply
   <tool name=""CaptureFacePreview"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Verify
   Use CreateExpressionClip or CreateExpressionClipFromData to save the .anim file
3. <tool name=""ConfigureAfkFace"">
   <arg name=""enableAfk"">true</arg>
   <arg name=""afkFacePath"">Assets/.../sleeping.anim</arg>
   </tool> → Set AFK clip
```
ConfigureAfkFace params: enableAfk, afkEnterFacePath, afkFacePath, afkExitFacePath, exitDuration.

### M. Left/Right Hand Separate Conditions (""左手グーで笑顔、右手ピースで怒り"")
```
1. <tool name=""AddExpression"">
   <arg name=""displayName"">Smile</arg>
   <arg name=""destination"">Registered</arg>
   <arg name=""animationClipPath"">Assets/.../smile.anim</arg>
   </tool>
   <tool name=""AddExpression"">
   <arg name=""displayName"">Angry</arg>
   <arg name=""destination"">Registered</arg>
   <arg name=""animationClipPath"">Assets/.../angry.anim</arg>
   </tool>
2. <tool name=""AddGestureBranch"">
   <arg name=""expressionName"">Smile</arg>
   <arg name=""conditions"">Left=Fist</arg>
   </tool>
   <tool name=""AddGestureBranch"">
   <arg name=""expressionName"">Angry</arg>
   <arg name=""conditions"">Right=Victory</arg>
   </tool>
```
Use `Left=` or `Right=` prefix to specify which hand triggers the expression.
Both hands: `'Left=Fist;Right=Victory'` (AND condition).
Either hand: `'Either=Fist'`.

### N. Batch Create Multiple Expressions (""表情を全部作り直して"")
```
1. <tool name=""AnalyzeFaceBlendShapes"">
   <arg name=""avatarRootName"">Avatar</arg>
   </tool> → Build profile once (cached for the rest of the batch)
2. <tool name=""ListFaceEmoExpressions""></tool> → Check current state
3. <tool name=""AskUser""></tool> → Confirm which expressions to create/replace
4. For each expression, repeat Workflow B (preset path):
   a. <tool name=""SuggestExpressionShapes"">
      <arg name=""avatarRootName"">Avatar</arg>
      <arg name=""intent""><intent></arg>
      </tool> → Get shapeData
   b. <tool name=""SetExpressionPreviewMulti"">
      <arg name=""avatarRootName"">Avatar</arg>
      <arg name=""blendShapeData""><shapeData></arg>
      </tool> → Apply
   c. <tool name=""CaptureFacePreview"">
      <arg name=""avatarRootName"">Avatar</arg>
      </tool> → Verify
   d. <tool name=""AskUser""></tool> → Confirm
   e. <tool name=""CreateAndRegisterExpression""></tool> → Save + register
   f. <tool name=""AddGestureBranch""></tool> → Assign gesture
   (no per-iteration reset needed — next preview overwrites)
```

### O. Workflow C: Gesture-Aware Expression Creation (Plan C) (""<avatar> に <表情> つけて"")
```
1. <tool name=""ResolveTargetAvatar"">
   <arg name=""promptHint""><promptHint></arg>
   </tool> → avatar 名 + confidence
   - 'None' なら user に Hierarchy から選んでもらう
   - 'Ambiguous' なら AskUser で alternatives から選択

2. <tool name=""InspectFaceEmoState"">
   <arg name=""avatarRootName"">avatarRootName</arg>
   </tool> → state
   - NoLauncher: <tool name=""AutoSetupFaceEmoForAvatar"">
     <arg name=""avatarRootName"">avatarRootName</arg>
     </tool>
   - LauncherUnconfigured: 同上
   - HasModes: 次へ

3. 発話に 'AI 任せ' / '編集する' キーワード無ければ AskUser top-mode

4. Mode 選択 (modes >1 なら AskUser、1 つなら採用宣言)

5. <tool name=""ListGestureBindings"">
   <arg name=""launcherName"">launcher</arg>
   <arg name=""modeName"">mode</arg>
   </tool> → 現在 bindings 確認
   AskUser gesture (8-grid + IntentGestureMap の推奨 ★)
   発話に gesture 名あれば skip

6. Hand qualifier (デフォルト Either、'左手で' 等で override)

7. <tool name=""FindBranchByCondition"">...</tool> が >=0 なら既存 binding 有
   → AskUser [上書き / 編集 / Cancel]
   <tool name=""DetectGestureConflicts"">...</tool> が空でなければ shadowed branches を user に提示

8. AI 任せ mode: SuggestExpressionShapes (Plan A) → 3 variation サムネ生成 → AskUser
   編集 mode:    <tool name=""SuggestCandidateShapes"">
                 <arg name=""avatarRootName"">avatar</arg>
                 <arg name=""intent"">intent</arg>
                 <arg name=""breadth"">wide</arg>
                 </tool> → 10-15 候補 + 3 案
                 <tool name=""OpenExpressionSession"">
                 <arg name=""modeName""></arg>
                 <arg name=""newName"">temp</arg>
                 <arg name=""avatarRootName"">avatar</arg>
                 <arg name=""editMode"">create-branch-clip</arg>
                 </tool>
                 <tool name=""ApplyExpressionVariation"">...</tool> → SetExpressionPreviewMulti で値を適用
                 AskUser [編集する / 次の variation / Cancel]

9. user 編集完了 → <tool name=""CommitExpressionSessionToBranch"">
   <arg name=""modeName"">modeName</arg>
   <arg name=""gesture"">gesture</arg>
   <arg name=""hand"">hand</arg>
   <arg name=""slot"">Base</arg>
   <arg name=""overwriteMode"">Overwrite</arg>
   </tool>

10. <tool name=""CaptureFaceEmoGestureTable"">
    <arg name=""modeName"">modeName</arg>
    <arg name=""avatarRootName"">avatarRootName</arg>
    </tool> → 結果画像

注: Registered 7 枠を消費しないこと。Branch 経路 (CommitExpressionSessionToBranch) がデフォルト。
注: 既存 clip 編集モード時 (editMode='edit-existing-clip') は別ルート、AI は新 shape 追加のみ。
```

## Emotion-to-Preset Guide
For natural-language expression requests, prefer `SuggestExpressionShapes` with one of the
canonical intent keywords below (Japanese aliases auto-resolve to the same preset):

| Emotion | Preset intent | Japanese aliases |
|---------|---------------|-------------------|
| 笑顔 / Smile | `smile` | 笑, 笑顔, にこ, joy, happy, fun, cheerful |
| 怒り / Angry | `angry` | 怒, おこ, mad, irritated, rage |
| 驚き / Surprised | `surprised` | 驚, びっくり, astonished, shock |
| 悲しみ / Sad | `sad` | 悲, sorrow, down |
| 泣き / Crying | `cry` | 泣, tear, weep |
| 照れ / Embarrassed | `shy` | 照, てれ, blush, embarrass |
| ウインク / Wink | `wink` | ウインク, ウィンク |
| 寝顔 / Sleeping | `sleep` | 眠, 寝, drowsy |
| キス / Kiss | `kiss` | キス, ちゅ, chu |

If the preset's confidence is low (< 0.5), fall back to Workflow C with
`SearchExpressionShapesV2('Avatar', 'eye,mouth,brow')` to inspect categorized shapes manually.

## Important Notes
- **Registered max 6**: Exceeding this causes an error. Use a folder (1 slot, holds up to 8) or Unregistered.
- **Avatar=None**: Use ConfigureTargetAvatar to resolve. Check with FindFaceEmo first.
- **NEVER guess mesh or BlendShape names** — call AnalyzeFaceBlendShapes first; use Suggest/SearchV2.
- **Values 0-100, NEVER 0-1**. SetExpressionPreviewMulti will reject `0.8`-style inputs with a clear error.
- **Multi-SMR aware**: SetExpressionPreviewMulti routes shapes to face / eyelash / tongue / teeth automatically.
- **FaceEmo is for facial expressions only**. Object toggles use SetupObjectToggle.
- **Apply after editing**: Run ApplyFaceEmoToAvatar to generate the FX layer after changes." },

            { "avatar-optimization", @"---
title: Avatar Optimization
description: VRChat avatar optimization techniques using AAO, NDMF, etc.
tags: optimization, AAO, Avatar Optimizer, NDMF, performance
---

# Avatar Optimization

## Overview
Optimization techniques to improve VRChat avatar performance rank.
Primarily uses Avatar Optimizer (AAO) and the NDMF framework.

## Installed Tools
- **Avatar Optimizer (AAO)** `com.anatawa12.avatar-optimizer` - Mesh optimization
- **NDMF** `nadena.dev.ndmf` - Non-destructive framework
- **Modular Avatar** `nadena.dev.modular-avatar` - Modular avatar system
- **VRCFury** `com.vrcfury.vrcfury` - Non-destructive tools
- **lilToon** `jp.lilxyzw.liltoon` - Shader
- **NDMF Mesh Simplifier** `jp.lilxyzw.ndmfmeshsimplifier` - Mesh simplification
- **VRC Quest Tools** `com.github.kurotu.vrc-quest-tools` - Quest support

## PC Performance Rank Thresholds (Official)

| Category | Excellent | Good | Medium | Poor |
|----------|-----------|------|--------|------|
| Triangles | 32,000 | 70,000 | 70,000 | 70,000 |
| Texture Memory | 40 MB | 75 MB | 110 MB | 150 MB |
| Skinned Meshes | 1 | 2 | 8 | 16 |
| Basic Meshes | 4 | 8 | 16 | 24 |
| Material Slots | 4 | 8 | 16 | 32 |
| PhysBones | 4 | 8 | 16 | 32 |
| PB Transforms | 16 | 64 | 128 | 256 |
| PB Colliders | 4 | 8 | 16 | 32 |
| PB Collision Check | 32 | 128 | 256 | 512 |
| Contacts | 8 | 16 | 24 | 32 |
| Constraints | 100 | 250 | 300 | 350 |
| Constraint Depth | 20 | 50 | 80 | 100 |
| Animators | 1 | 4 | 16 | 32 |
| Bones | 75 | 150 | 256 | 400 |
| Lights | 0 | 0 | 0 | 1 |
| Particle Systems | 0 | 4 | 8 | 16 |
| Total Particles | 0 | 300 | 1,000 | 2,500 |
| Mesh Particle Polys | 0 | 1,000 | 2,000 | 5,000 |
| Trail Renderers | 1 | 2 | 4 | 8 |
| Line Renderers | 1 | 2 | 4 | 8 |
| Cloths | 0 | 1 | 1 | 1 |
| Cloth Vertices | 0 | 50 | 100 | 200 |
| Physics Colliders | 0 | 1 | 8 | 8 |
| Physics Rigidbodies | 0 | 1 | 8 | 8 |
| Audio Sources | 1 | 4 | 8 | 8 |

- Exceeding Poor = **Very Poor**
- Overall rank = worst category rank

## Mobile Performance Rank Thresholds (Official)

| Category | Excellent | Good | Medium | Poor |
|----------|-----------|------|--------|------|
| Triangles | 7,500 | 10,000 | 15,000 | 20,000 |
| Texture Memory | 10 MB | 18 MB | 25 MB | 40 MB |
| Skinned Meshes | 1 | 1 | 2 | 2 |
| Basic Meshes | 1 | 1 | 2 | 2 |
| Material Slots | 1 | 1 | 2 | 4 |
| PhysBones | 0 | 4 | 6 | 8 |
| PB Transforms | 0 | 16 | 32 | 64 |
| PB Colliders | 0 | 4 | 8 | 16 |
| PB Collision Check | 0 | 16 | 32 | 64 |
| Contacts | 2 | 4 | 8 | 16 |
| Animators | 1 | 1 | 1 | 2 |
| Bones | 75 | 90 | 150 | 150 |
| Particle Systems | 0 | 0 | 0 | 2 |
| Total Particles Active | 0 | 0 | 0 | 200 |
| Mesh Particle Active Polys | 0 | 0 | 0 | 400 |

## AAO (Avatar Optimizer) Key Components

### Trace and Optimize
The most important component for automatic whole-avatar optimization.
```
1. Select the avatar root
2. <tool name=""AddComponent"">
   <arg name=""gameObjectName"">avatarRootName</arg>
   <arg name=""componentName"">AAOTraceAndOptimize</arg>
   </tool>
   *Verify exact component name with SearchTools
3. Optimization is automatically applied at build time
```

### Merge Skinned Mesh
Combines multiple SkinnedMeshRenderers into one to reduce draw calls.
```
Steps:
1. Create an empty GameObject as parent of meshes to combine
2. Add MergeSkinnedMesh component
3. Configure target Renderers
```

### Remove Mesh By Box / By BlendShape
Removes invisible mesh portions to reduce polygon count.
- Used when removing body mesh under clothing
- Removes parts hidden by BlendShapes

## Recommended Optimization Workflow

### 1. Check Current Status
```
<tool name=""GetAvatarPerformanceStats"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
Shows performance rank for all categories. Check each category rank and overall rank.

### 2. Apply Trace and Optimize
Most effective and safe optimization. Applied non-destructively.

### 3. Merge Meshes
Combine meshes using the same material to reduce draw calls.

### 4. Remove Unnecessary Meshes
- Body mesh under clothing
- Unused accessories
- Hidden objects

### 5. Texture Optimization
- Important when texture memory exceeds thresholds
- Batch compress with Avatar Compressor (`dev.limitex.avatar-compressor`)
- Downsize unnecessarily large textures (4096→2048, 2048→1024)
- Utilize ASTC/BC7 compression

### 6. Material Atlas
- Combine multiple materials into one to reduce Material Slots
- Use texture atlas to reduce draw calls

### 7. PhysBone Optimization
- Remove unnecessary PhysBones
- Shorten chain length (reduce Affected Transforms)
- Reduce collider count (reduce Collision Check Count)
- Use exclusions to exclude unnecessary child Transforms

## Quest Support
When creating Quest avatar builds:
- Use VRC Quest Tools
- Significantly reduce polygon count (NDMF Mesh Simplifier)
- Switch shaders to VRChat/Mobile variants
- Keep PhysBones within Quest limits
- Particle systems cannot be used (threshold is 0)

## Notes
- AAO/NDMF tools are non-destructive → applied only at build time, original assets are unchanged
- Use ""Build & Test"" for local testing before uploading
- Compare performance rank before and after optimization
- Recommend running [GetAvatarPerformanceStats] for final check before build" },

            { "vrchat-parameters", @"---
title: VRChat Animator Parameters Reference
description: Technical reference for VRChat avatar Animator parameters, syncing, and built-in parameters
tags: VRChat, Animator, Parameters, Expression, sync
---

# VRChat Animator Parameters Reference

## Parameter Types and Ranges

| Type | Range | Sync Cost |
|------|-------|-----------|
| **Int** | 0–255 | 8 bits |
| **Float** | -1.0–1.0 | 8 bits |
| **Bool** | true/false | 1 bit |

- Total sync parameter limit: **256 bits**
- Bool: Best for toggles (ON/OFF)
- Int: Multiple outfit set switching, etc.
- Float: RadialPuppet (slider), gradient control, etc.

## Built-in Parameters

### Always Available
| Parameter Name | Type | Description | Sync Type |
|---------------|------|-------------|-----------|
| `IsLocal` | Bool | Whether this is the local player | None |
| `Viseme` | Int | Lip sync (0-14) | Speech |
| `Voice` | Float | Voice input level | Speech |
| `GestureLeft` | Int | Left hand gesture (0-7) | IK |
| `GestureRight` | Int | Right hand gesture (0-7) | IK |
| `GestureLeftWeight` | Float | Left trigger press amount | Playable |
| `GestureRightWeight` | Float | Right trigger press amount | Playable |
| `AngularY` | Float | Rotation speed | IK |
| `VelocityX` | Float | Lateral velocity | IK |
| `VelocityY` | Float | Vertical velocity | IK |
| `VelocityZ` | Float | Forward/backward velocity | IK |
| `VelocityMagnitude` | Float | Speed magnitude | IK |
| `Upright` | Float | Upright degree (0=prone, 1=standing) | IK |
| `Grounded` | Bool | Ground contact | IK |
| `Seated` | Bool | Seated state | IK |
| `AFK` | Bool | AFK state | IK |
| `TrackingType` | Int | Tracking type | Playable |
| `VRMode` | Int | 0=Desktop, 1=VR | IK |
| `MuteSelf` | Bool | Self-muted | Playable |
| `InStation` | Bool | In a station | IK |
| `Earmuffs` | Bool | Earmuffs enabled | Playable |
| `IsOnFriendsList` | Bool | On friends list | Other |
| `AvatarVersion` | Int | 3 if built with SDK3 (2020.3.2+), else 0 | IK |
| `IsAnimatorEnabled` | Bool | Whether Animator is enabled | None |
| `ScaleModified` | Bool | True if avatar is being scaled, false at default size | Playable |
| `ScaleFactor` | Float | Eye height scale | Playable |
| `ScaleFactorInverse` | Float | Inverse of ScaleFactor | Playable |
| `EyeHeightAsMeters` | Float | Eye height (meters) | Playable |
| `EyeHeightAsPercent` | Float | Eye height normalized within 0.2m–5.0m: (h-0.2)/4.8, range ~0.0–1.0 | Playable |

### Gesture Values (GestureLeft/GestureRight)
| Value | Gesture |
|-------|---------|
| 0 | Neutral |
| 1 | Fist |
| 2 | HandOpen |
| 3 | FingerPoint |
| 4 | Victory |
| 5 | RockNRoll |
| 6 | HandGun |
| 7 | ThumbsUp |

### TrackingType Values
| Value | Description |
|-------|-------------|
| 0 | Uninitialized |
| 1 | Generic Rig |
| 2 | Hands-only, no fingers (transient state) |
| 3 | Head and hands (3-point VR) |
| 4 | Head + Hands + Hip (4-point VR) |
| 5 | Head + Hands + Feet (5-point, no hip) |
| 6 | Full Body (Head + Hands + Hip + Feet) |

## Sync Types

| Type | Description |
|------|-------------|
| **Speech** | Lip sync/voice. Sent with voice data |
| **Playable** | Used in Playable layer. Animator state sync |
| **IK** | Sent with IK data. Position/tracking related |
| **None** | No sync. Local only |
| **Other** | Synced via a dedicated channel (e.g. friends-list state) |

## Custom Parameters

### VRCExpressionParameters
- Defined in VRCAvatarDescriptor's `expressionParameters`
- **Automatically linked** to same-named parameters in the FX AnimatorController
- `networkSynced=true`: Synced to other players (consumes budget)
- `networkSynced=false`: Local only (no budget cost)
- `saved=true`: Value persists across world changes and avatar switches

### Parameter Sync Mechanism
1. User operates controls in Expression Menu
2. VRCExpressionParameters values change
3. Automatically reflected to same-named FX Animator parameters
4. Animator transitions based on conditions

### Sync Cost Calculation
```
Bool × N = N bits
Int × N = N × 8 bits
Float × N = N × 8 bits
Total ≤ 256 bits
```

Example: Bool×10 + Float×5 + Int×2 = 10 + 40 + 16 = 66 bits

## Playable Layers

VRChat avatar's 5 layers:
| Index | Name | Purpose |
|-------|------|---------|
| 0 | Base | Locomotion |
| 1 | Additive | Additive animations (breathing, etc.) |
| 2 | Gesture | Gestures and hand movement |
| 3 | Action | Emotes and full-body animations |
| 4 | FX | **Toggles, expressions, gimmicks** |

- FX (index=4, type=5) is the primary layer for object toggles and gimmicks
- FX layer Weight must be 1.0
- Write Defaults (WD) must be consistent across the entire avatar — all states ON or all OFF (all Playable Layer controllers count as ONE controller). Mixed WD behaves like WD-Off: properties stick and facial expressions fail to reset; the SDK only warns, it does not auto-fix.
- Exception (non-negotiable): additive-blending layers and Direct Blend Tree single-state layers must always be WD ON regardless of the rest, since WD OFF makes their values multiply toward infinity.
- The official baseline is WD OFF (built-in/sample animators are OFF); consistent ON is also valid — the rule is consistency, not a specific value.
- If you go all-OFF: give every state a clip/blend tree, and apply an Avatar Mask to any layer that animates Transforms.

## Tool Usage

### Check Parameters
```
<tool name=""ListVRCExpressionParameters"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```

### Add Parameter
```
<tool name=""AddVRCExpressionParameter"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""paramName"">ParamName</arg>
<arg name=""type"">Bool</arg>
<arg name=""defaultValue"">1.0</arg>
<arg name=""saved"">true</arg>
<arg name=""synced"">true</arg>
</tool>
```

### Remove Parameter
```
<tool name=""RemoveVRCExpressionParameter"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""paramName"">ParamName</arg>
</tool>
```

### Add Parameter to FX Controller
```
<tool name=""AddAnimatorParameter"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""name"">ParamName</arg>
<arg name=""type"">bool</arg>
<arg name=""defaultValue"">true</arg>
</tool>
```

### Object Toggle (One-Step Setup)
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""targetPath"">objectPath</arg>
</tool>
```

## Notes
- Built-in parameters don't need to be defined in ExpressionParameters (automatically available)
- Custom parameter names override built-ins if they conflict
- Exceeding 256-bit sync cost will cause the avatar to malfunction
- Parameter names must **exactly match** between FX Controller and ExpressionParameters
- To use built-in parameters in an Animator, simply add a same-named parameter to the Animator" },

            { "object-toggle", @"---
title: VRChat Object Toggle
description: Set up toggles to show/hide objects from the Expression Menu
tags: VRChat, Expression Menu, toggle, ON/OFF, FX layer, parameter
---

# VRChat Object Toggle Setup

## Overview
Create toggles that show/hide GameObjects from the VRChat avatar's
Expression Menu (radial menu).

## Prerequisites
- VRChat Avatar SDK 3.0 is installed
- Avatar has a VRCAvatarDescriptor configured
- ExpressionParameters / ExpressionsMenu assets are assigned
- FX AnimatorController is assigned

## Easy Method (Recommended): SetupObjectToggle

One-step tool for complete setup:
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""targetPath"">toggleTargetPath</arg>
</tool>
```

Example: Toggle Sailor-Jersey:
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">Chiffon</arg>
<arg name=""targetPath"">Sailor-Jersey</arg>
</tool>
```

Default OFF (initially hidden):
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">Chiffon</arg>
<arg name=""targetPath"">Sailor-Jersey</arg>
<arg name=""defaultOn"">false</arg>
</tool>
```

With a custom name:
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">Chiffon</arg>
<arg name=""targetPath"">Sailor-Jersey</arg>
<arg name=""toggleName"">SailorJersey</arg>
</tool>
```

This tool automatically creates:
1. ON/OFF animation clips (Assets/Animations/Toggles/)
2. Layer, states, and transitions in the FX AnimatorController
3. Bool parameter in ExpressionParameters
4. Toggle control in ExpressionsMenu

## Manual Setup (Using Individual Tools)

### Step 1: Identify Target Object Path
```
<tool name=""ListChildren"">
<arg name=""name"">avatarRootName</arg>
</tool>
```
Find the target GameObject from the avatar's direct children.
Specify the path as a relative path from the avatar root.

### Step 2: Create Toggle Animations
```
<tool name=""CreateToggleAnimations"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""targetPath"">relativeObjectPath</arg>
</tool>
```
Creates two animation clips: ON (m_IsActive=1) and OFF (m_IsActive=0).

### Step 3: Check FX Controller
```
<tool name=""GetVRCFXControllerPath"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
Get the FX AnimatorController asset path.

### Step 4: Add Parameter to FX Controller
```
<tool name=""AddAnimatorParameter"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""name"">toggleName</arg>
<arg name=""type"">bool</arg>
<arg name=""defaultValue"">true</arg>
</tool>
```

### Step 5: Add FX Layer
```
<tool name=""AddAnimatorLayer"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""layerName"">Toggle_toggleName</arg>
</tool>
```
```
<tool name=""SetAnimatorLayerWeight"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""layerIndex"">layerIndex</arg>
<arg name=""weight"">1.0</arg>
</tool>
```

### Step 6: Add States
```
<tool name=""AddAnimatorState"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""stateName"">ON</arg>
<arg name=""motionPath"">onClipPath</arg>
<arg name=""layerIndex"">layerIndex</arg>
</tool>
<tool name=""AddAnimatorState"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""stateName"">OFF</arg>
<arg name=""motionPath"">offClipPath</arg>
<arg name=""layerIndex"">layerIndex</arg>
</tool>
```

### Step 7: Add Transitions
```
<tool name=""AddAnimatorTransition"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""fromState"">OFF</arg>
<arg name=""toState"">ON</arg>
<arg name=""conditions"">toggleName=true</arg>
<arg name=""layerIndex"">layerIndex</arg>
</tool>
<tool name=""AddAnimatorTransition"">
<arg name=""controllerPath"">fxControllerPath</arg>
<arg name=""fromState"">ON</arg>
<arg name=""toState"">OFF</arg>
<arg name=""conditions"">toggleName=false</arg>
<arg name=""layerIndex"">layerIndex</arg>
</tool>
```

### Step 8: Add Expression Parameter
```
<tool name=""AddVRCExpressionParameter"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""paramName"">toggleName</arg>
<arg name=""type"">Bool</arg>
<arg name=""defaultValue"">1.0</arg>
<arg name=""saved"">true</arg>
<arg name=""synced"">true</arg>
</tool>
```
- Bool parameter, synced to other players
- defaultValue: 1.0=default ON, 0.0=default OFF

### Step 9: Add Expression Menu Toggle
```
<tool name=""AddVRCExpressionsMenuToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">toggleName</arg>
<arg name=""paramName"">toggleName</arg>
</tool>
```

## When Menu is Full (SubMenu Support)

Expression Menu allows a maximum of 8 controls per page. When full, use submenus.

### Creating a SubMenu
```
<tool name=""AddVRCExpressionsMenuSubMenu"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">Outfits</arg>
</tool>
```
A new VRCExpressionsMenu asset is automatically generated and linked as a SubMenu control.

### Adding Controls to a SubMenu
Use the `subMenuPath` parameter to add within a submenu:
```
<tool name=""AddVRCExpressionsMenuToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">Hat</arg>
<arg name=""paramName"">Hat</arg>
<arg name=""subMenuPath"">Outfits</arg>
</tool>
<tool name=""AddVRCExpressionsMenuToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">Glasses</arg>
<arg name=""paramName"">Glasses</arg>
<arg name=""subMenuPath"">Outfits</arg>
</tool>
```

### Nested SubMenus
`subMenuPath` supports slash-separated nesting:
```
<tool name=""AddVRCExpressionsMenuSubMenu"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">Details</arg>
<arg name=""subMenuPath"">Outfits</arg>
</tool>
<tool name=""AddVRCExpressionsMenuToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">Ring</arg>
<arg name=""paramName"">Ring</arg>
<arg name=""subMenuPath"">Outfits/Details</arg>
</tool>
```

## Tool Call Examples

### Example 1: Outfit Toggle (One-Step Setup)
```
User: ""Make Sailor-Jersey toggleable from the Expression Menu""
AI: <tool name=""SetupObjectToggle"">
    <arg name=""avatarRootName"">Chiffon</arg>
    <arg name=""targetPath"">Sailor-Jersey</arg>
    </tool>
    Result: Creates ON/OFF animations, FX layer, parameter, and menu entry in one step
```

### Example 2: Accessory Toggle (Default OFF)
```
User: ""Add glasses as a toggle, hidden by default""
AI: <tool name=""SetupObjectToggle"">
    <arg name=""avatarRootName"">Avatar</arg>
    <arg name=""targetPath"">Glasses</arg>
    <arg name=""defaultOn"">false</arg>
    </tool>
```

### Example 3: SubMenu When Menu is Full
```
User: ""The menu is full but I want to add another toggle""
AI: <tool name=""InspectVRCExpressionsMenu"">
    <arg name=""avatarRootName"">Avatar</arg>
    </tool>
    → Confirm 8 controls
    <tool name=""AddVRCExpressionsMenuSubMenu"">
    <arg name=""avatarRootName"">Avatar</arg>
    <arg name=""controlName"">Accessories</arg>
    </tool>
    → Create submenu
    <tool name=""SetupObjectToggle"">
    <arg name=""avatarRootName"">Avatar</arg>
    <arg name=""targetPath"">NewItem</arg>
    </tool>
    → If menu is full, manually add to submenu:
    <tool name=""AddVRCExpressionsMenuToggle"">
    <arg name=""avatarRootName"">Avatar</arg>
    <arg name=""controlName"">NewItem</arg>
    <arg name=""paramName"">NewItem</arg>
    <arg name=""subMenuPath"">Accessories</arg>
    </tool>
```

## VRChat Expression Menu Basics

### Expression Parameter
- Defined in VRCExpressionParameters asset
- Types: Bool (1bit), Int (0-255, 8bit), Float (-1.0–1.0, 8bit)
- Synced: Synced to other players (up to 256 bits total)
- Saved: Value persists across world changes and avatar switches
- Automatically linked to same-named parameters in FX Controller
- See `vrchat-parameters` skill for details

### Expression Menu Control Types
- **Toggle**: ON/OFF switch (for Bool parameters) → `AddVRCExpressionsMenuToggle`
- **Button**: ON only while pressed → `AddVRCExpressionsMenuButton`
- **SubMenu**: Navigate to submenu → `AddVRCExpressionsMenuSubMenu`
- **RadialPuppet**: Rotary slider (Float) → `AddVRCExpressionsMenuRadialPuppet`
- **TwoAxisPuppet**: 2-axis joystick
- **FourAxisPuppet**: 4-direction input

### Removing Controls
```
<tool name=""RemoveVRCExpressionsMenuControl"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""controlName"">controlName</arg>
</tool>
<tool name=""RemoveVRCExpressionParameter"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""paramName"">parameterName</arg>
</tool>
```

### FX Layer Rules
- Layer Weight must be set to 1.0
- Transition ExitTime must be disabled
- Transition Duration must be 0
- Write Defaults (WD) must be consistent across the entire avatar — all states ON or all OFF (VRChat treats every Playable Layer controller as one controller; mixing makes everything behave as WD-Off, so properties stick and expressions don't reset — the SDK only warns). Official baseline is WD OFF.
- Exception (non-negotiable): single-state layers, Direct Blend Tree states, and additive-blending layers must always be WD ON regardless of the rest of the avatar — WD OFF on a DBT makes blendshape values multiply toward infinity.
- If using all-OFF: put a clip/blend tree in every state (empty WD-Off states overwrite to default), and apply an Avatar Mask when animating Transforms.

## Notes
- Expression Parameter total sync cost limit is 256 bits
- Expression Menu allows maximum 8 controls per page
- Parameter names must exactly match between FX Controller and ExpressionParameters
- Undo supported: Operations can be undone with Ctrl+Z
- IMPORTANT: SetupObjectToggle directly edits FX/Param/Menu. FaceEmo is unrelated to object toggles — never use FaceEmo for object toggles. FaceEmo is exclusively for facial expression (face BlendShape) management.

## Troubleshooting
- Toggle not working → Check that parameter names match between FX Controller and ExpressionParameters
- Not showing in menu → Check that ExpressionsMenu is assigned in VRCAvatarDescriptor
- Not visible to other players → Check that parameter Synced=true
- Wrong default state → Check parameter defaultValue and FX initial state
- Menu is full → Create a submenu with AddVRCExpressionsMenuSubMenu and add via subMenuPath" },

            { "weapon-gimmick-setup", @"---
title: Weapon Gimmick Positioning
description: Placing and aligning weapon gimmicks on a VRChat avatar
tags: weapon, gimmick, alignment, VRCFury, Modular Avatar, knife, sword, gun
---

# Weapon Gimmick Positioning

## Overview
Setup and alignment procedure for integrating weapon gimmicks (swords, guns, knives, etc.)
into a VRChat avatar.

**Important: Use `AlignAccessoryToBone` for weapon alignment. Do not guess coordinates with SetTransform.**
For detailed placement procedure, see `ReadSkill('accessory-setup')`.

## Step 1: Analyze Gimmick Structure
Check if the gimmick contains MA/VRCFury components:
```
<tool name=""AnalyzeGimmickStructure"">
<arg name=""gameObjectName"">weaponPrefabName</arg>
</tool>
```
- BoneProxy already configured → Just place as child of avatar
- BoneProxy not configured → Manual placement needed

## Step 2: Place Prefab as Child of Avatar
```
<tool name=""InstantiatePrefab"">
<arg name=""assetPath"">Assets/.../Weapon.prefab</arg>
<arg name=""parentName"">avatarRootName</arg>
</tool>
```

## Step 3: Determine Attachment Location and Auto-Align

### Holding in Hand
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">weaponName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">RightHand</arg>
<arg name=""attachmentStyle"">grip</arg>
</tool>
```

### Hip/Thigh Attachment (Holster, Sheath)
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">weaponName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">RightUpperLeg</arg>
<arg name=""attachmentStyle"">surface</arg>
<arg name=""direction"">right</arg>
</tool>
```

### Back Attachment
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">weaponName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">Spine</arg>
<arg name=""attachmentStyle"">surface</arg>
<arg name=""direction"">back</arg>
</tool>
```

## Step 4: Verify
```
<tool name=""CaptureMultiAngle"">
<arg name=""targetName"">weaponName</arg>
<arg name=""angles"">front,left,right,back</arg>
</tool>
```

## Step 5: Fine Adjustment
Leave fine adjustments after auto-placement to the user:
""If fine adjustment is needed, use the Scene view gizmos or Transform panel for direct manipulation.""

**Do not repeatedly guess coordinates with SetTransform.**

## VRCFury Weapon Gimmicks

For VRCFury, gimmicks typically have:
- Full Controller (adds animator layers)
- Toggle (ON/OFF switching)

Prefabs with VRCFury components are integrated non-destructively
by simply placing them as children of the avatar.

## Modular Avatar Weapon Gimmicks

For MA (Modular Avatar):
- MA Merge Animator
- MA Menu Item
- MA Parameters

Prefabs with MA components are also auto-integrated when placed as children of the avatar.

## Draw/Holster Gimmick (Constraint Method)

### VRC Parent Constraint (Recommended)
For dynamically following weapons to the hand:
1. Add VRC Parent Constraint component to the weapon object
2. Set the hand bone (Hand_R, etc.) as Source
3. Weight=1.0, IsActive=true

### Draw/Holster (Holster → Hand)
1. Set up 2 Sources (holster position, hand position)
2. Switch source weights via Animator
3. Control via Expression Menu toggle/button

## Notes
- **Do not guess coordinates with SetTransform** → Use AlignAccessoryToBone
- **Do not use ArmatureLink/SetupOutfit for weapons** → Those are for outfits
- Match the avatar's Write Defaults: keep the WHOLE avatar (all Playable Layers count as one controller) either all-ON or all-OFF — never mixed (mixing makes properties ""stick"" and breaks expressions; the SDK only warns).
- Exception: additive layers and Direct Blend Tree single-state layers must always be WD ON regardless of the avatar's setting (WD OFF makes their values blow up).
- If the avatar is all-OFF: every state of your weapon layers needs a clip/blend tree, and animating Transforms requires an Avatar Mask.
- Watch Expression Parameter budget (256 bits)
- VRC Constraint recommended: Lighter than Unity Constraint, optimized for VRChat runtime" },

            { "animation-creation", @"---
title: Animation Clip Creation
description: Creating AnimationClips with keyframes for motion, BlendShape, and property animations
tags: animation, motion, keyframe, animationclip, blendshape, motion, animation
---

# AnimationClip Creation Guide

## Overview
Create Unity AnimationClips via text using `CreateAnimationClip` and `SetAnimationCurve`.
Supports bone rotation/position, BlendShapes, object ON/OFF, and any other animatable property.

## Tools Used
- `ListBones(avatarRootName)` — Check bone hierarchy and paths
- `ListHumanoidMapping(avatarRootName)` — Check Humanoid bone ↔ Transform name mapping
- `InspectBone(avatarRootName, boneName)` — Check bone's current position/rotation
- `CreateAnimationClip(clipName, savePath, length, isLooping)` — Create an empty clip
- `SetAnimationCurve(clipPath, bonePath, property, keyframes)` — Add curve (keyframes)
- `GetAnimationClipInfo(clipPath)` — Inspect the created clip

## Coordinate System and Properties

### Transform Rotation (Euler Angles, Degrees)
- `rotation.x` — X-axis rotation (forward/backward tilt: + tilts forward)
- `rotation.y` — Y-axis rotation (left/right facing: + faces left)
- `rotation.z` — Z-axis rotation (lateral tilt: + tilts right)

**Note**: Values are in degrees. Example: 45 = 45 degrees

### Transform Position (Meters)
- `position.x` — X-axis (right is +)
- `position.y` — Y-axis (up is +)
- `position.z` — Z-axis (forward is +)

**Note**: Local coordinate system. Relative to the parent bone.

### Transform Scale
- `scale.x`, `scale.y`, `scale.z` — Scale per axis (1.0 is default)

### BlendShape
- `blendShape.ShapeName` — BlendShape on SkinnedMeshRenderer (0–100)
- ShapeName is case-sensitive

### GameObject ON/OFF
- `active` — 1=visible, 0=hidden

## Determining bonePath

### Procedure
1. Use `ListBones('avatarName')` to check the bone hierarchy
2. Use the relative path from the object that has the Animator component
3. Example: If avatar root is `MyAvatar` and right upper arm is `MyAvatar/Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R`
   → bonePath is `Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R`

### Common Bone Structure
```
Armature/
  Hips/                        ← Center of body (pelvis)
    Spine/                     ← Lower spine
      Chest/                   ← Chest
        Neck/                  ← Neck
          Head/                ← Head
        Shoulder_L/            ← Left shoulder
          Upper_Arm_L/         ← Left upper arm
            Lower_Arm_L/       ← Left forearm
              Hand_L/          ← Left hand
        Shoulder_R/            ← Right shoulder
          Upper_Arm_R/         ← Right upper arm
            Lower_Arm_R/       ← Right forearm
              Hand_R/          ← Right hand
    Upper_Leg_L/               ← Left thigh
      Lower_Leg_L/             ← Left shin
        Foot_L/                ← Left foot
    Upper_Leg_R/               ← Right thigh
      Lower_Leg_R/             ← Right shin
        Foot_R/                ← Right foot
```
**Note**: Actual bone names differ per avatar. Always verify with `ListBones`.

## Keyframes Syntax

### Basic Format
```
time:value, time:value, time:value
```
- Time: in seconds (0.0 = start, 1.0 = 1 second later)
- Value: numeric value appropriate for the property

### Examples
```
0:0, 0.5:45, 1.0:0        ← 0°→45°→0° (round trip)
0:0, 0.3:90, 0.7:90, 1.0:0  ← Raise, hold, return
0:0, 1.0:360               ← Full rotation
```

## Motion Creation Examples

### Waving Animation
```
User: ""Create a right-hand waving animation""

1. Check bone structure:
<tool name=""ListBones"">
<arg name=""avatarRootName"">MyAvatar</arg>
<arg name=""filter"">Arm</arg>
</tool>

2. Create clip:
<tool name=""CreateAnimationClip"">
<arg name=""clipName"">wave</arg>
<arg name=""savePath"">Assets/Animations</arg>
<arg name=""length"">2.0</arg>
<arg name=""isLooping"">true</arg>
</tool>

3. Raise right upper arm (Z-axis rotation to raise arm sideways):
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/wave.anim</arg>
<arg name=""bonePath"">Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R</arg>
<arg name=""property"">rotation.z</arg>
<arg name=""keyframes"">0:-60, 0.3:-60, 1.7:-60, 2.0:-60</arg>
</tool>

4. Wave with forearm (Z-axis oscillation):
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/wave.anim</arg>
<arg name=""bonePath"">Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R/Lower_Arm_R</arg>
<arg name=""property"">rotation.z</arg>
<arg name=""keyframes"">0:0, 0.3:-30, 0.6:30, 0.9:-30, 1.2:30, 1.5:-30, 1.8:30, 2.0:0</arg>
</tool>
```

### Nodding Animation
```
User: ""Create a nodding animation""

1. Check bones → Get Head bone path
2. Create clip:
<tool name=""CreateAnimationClip"">
<arg name=""clipName"">nod</arg>
<arg name=""savePath"">Assets/Animations</arg>
<arg name=""length"">0.8</arg>
<arg name=""isLooping"">false</arg>
</tool>

3. Tilt head forward (X-axis rotation):
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/nod.anim</arg>
<arg name=""bonePath"">Armature/Hips/Spine/Chest/Neck/Head</arg>
<arg name=""property"">rotation.x</arg>
<arg name=""keyframes"">0:0, 0.2:15, 0.4:0, 0.6:10, 0.8:0</arg>
</tool>
```

### Facial Expression Animation (BlendShape)
```
User: ""Create a smile animation""

1. Check BlendShape names:
<tool name=""ListBlendShapes"">
<arg name=""goName"">Body</arg>
</tool>

2. Create clip:
<tool name=""CreateAnimationClip"">
<arg name=""clipName"">smile</arg>
<arg name=""savePath"">Assets/Animations</arg>
<arg name=""length"">0.5</arg>
<arg name=""isLooping"">false</arg>
</tool>

3. Set smile BlendShape:
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/smile.anim</arg>
<arg name=""bonePath"">Body</arg>
<arg name=""property"">blendShape.face_smile</arg>
<arg name=""keyframes"">0:0, 0.3:100, 0.5:100</arg>
</tool>
```

### Object Toggle (Show/Hide)
```
<tool name=""CreateAnimationClip"">
<arg name=""clipName"">hat_on</arg>
<arg name=""savePath"">Assets/Animations</arg>
<arg name=""length"">0.0</arg>
<arg name=""isLooping"">false</arg>
</tool>
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/hat_on.anim</arg>
<arg name=""bonePath"">Hat</arg>
<arg name=""property"">active</arg>
<arg name=""keyframes"">0:1</arg>
</tool>

<tool name=""CreateAnimationClip"">
<arg name=""clipName"">hat_off</arg>
<arg name=""savePath"">Assets/Animations</arg>
<arg name=""length"">0.0</arg>
<arg name=""isLooping"">false</arg>
</tool>
<tool name=""SetAnimationCurve"">
<arg name=""clipPath"">Assets/Animations/hat_off.anim</arg>
<arg name=""bonePath"">Hat</arg>
<arg name=""property"">active</arg>
<arg name=""keyframes"">0:0</arg>
</tool>
```

## Tips for Natural Motion

### Rotation Value Guidelines (Human Range of Motion)
- **Neck rotation**: X-axis ±30°, Y-axis ±60°, Z-axis ±30°
- **Raising arm from shoulder**: Z-axis -80° (sideways), X-axis -80° (forward)
- **Elbow bend**: Y-axis 0–140°
- **Waist rotation**: X-axis ±30°, Y-axis ±40°, Z-axis ±20°
- **Finger bend**: X-axis 0–90°

### Movement Principles
- **Same start and end values** for smooth looping
- **Add easing**: Closer keyframe intervals at the start and end of movement
- **Exaggerate**: Avatars appear small in VRChat, so make movements bigger than real life
- **Chain multiple bones**: When raising an arm, stagger shoulder → upper arm → forearm slightly for natural movement
- **Symmetry and asymmetry**: Even for two-handed motions, slight offset looks more natural

## Notes
- Bone names differ per avatar, so always verify with `ListBones` before setting curves
- Rotation values are local Euler angles. Dependent on parent bone orientation
- Gimbal lock is a singularity of Euler-angle representation (near a ±90° middle-axis rotation where two axes align) and is unrelated to how large the rotation is. Curves interpolate x/y/z independently, so values beyond 360° are valid; the only caveat for >360° is the interpolation path (angle wrapping), not gimbal lock
- Keep Write Defaults (WD) consistent across the ENTIRE avatar — all states ON or all OFF (VRChat treats every Playable Layer controller as one controller, not just FX; mixing makes WD-On states behave as WD-Off, so properties stick and expressions don't reset — the SDK only warns). The official baseline is OFF, but consistent ON is equally valid; the rule is consistency, not a specific value.
- Exception (non-negotiable): additive layers and Direct Blend Tree single-state layers must always be WD ON regardless of the rest, or their values multiply toward infinity.
- If you choose all-OFF: put a clip or blend tree in every state (empty WD-Off states overwrite to default), and when animating Transforms apply an Avatar Mask (a 0-transform mask means ""allow all"").
- Assign created clips to AnimatorController States using: `SetAnimatorStateMotion`" },

            { "outfit-setup", @"---
title: VRChat Outfit Setup
description: Outfit dressing procedure for VRChat avatars (both compatible and incompatible outfits)
tags: VRChat, outfit, Modular Avatar, Setup Outfit, dressing, fitting, incompatible outfit
---

# VRChat Outfit Setup

## Overview
Procedure for dressing a VRChat avatar in outfits.
- **Compatible outfits**: Can be dressed using Modular Avatar (MA) Setup Outfit alone
- **Incompatible outfits**: Use OutfitFittingTools for bone remapping and body adaptation, then finish with MA Setup Outfit

## Prerequisites
- Modular Avatar (`nadena.dev.modular-avatar`) is installed
- Avatar has a VRCAvatarDescriptor configured

## Compatible vs Incompatible Outfits
- **Compatible outfits**: Bone structure is pre-adjusted for the avatar. Can be dressed with MA Setup Outfit alone
- **Incompatible outfits**: Different bone structure. Use OutfitFittingTools for auto-fitting, then finish with MA Setup Outfit

## Setup Procedure

### Step 1: Check Current Outfit
```
<tool name=""ListChildren"">
<arg name=""name"">avatarRootName</arg>
</tool>
```
Check the avatar's direct children to identify outfit-related objects.
Outfit objects are typically mesh objects or outfit prefabs other than the Armature.

### Step 2: Remove Existing Outfit
Hide each existing outfit object and set its tag to EditorOnly.
**Hiding alone is not enough**. Data remains when uploading, so the EditorOnly tag is also needed.

```
<tool name=""SetActive"">
<arg name=""gameObjectName"">avatarRootName/outfitObjectName</arg>
<arg name=""active"">false</arg>
</tool>
<tool name=""SetTag"">
<arg name=""gameObjectName"">avatarRootName/outfitObjectName</arg>
<arg name=""tag"">EditorOnly</arg>
</tool>
```

Execute for all outfit parts if there are multiple.
**Note**: Do not remove the Armature, Body (base mesh), or the root with VRCAvatarDescriptor.

### Step 2.5: Reset Existing Shrink BlendShapes
Reset Shrink BlendShapes from the previous outfit to 0.
If shrinks remain, the base body will appear thin.

1. Check shrink-related BlendShapes on the Body mesh:
```
<tool name=""ListBlendShapesEx"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""filter"">shrink</arg>
</tool>
```

2. Reset all non-zero shrinks to 0:
```
<tool name=""SetMultipleBlendShapes"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""blendShapeData"">Shrink_XXX=0;Shrink_YYY=0</arg>
</tool>
```

### Step 3: Search for Compatible Outfit Prefab
```
<tool name=""SearchAssets"">
<arg name=""query"">outfitName</arg>
<arg name=""typeFilter"">Prefab</arg>
</tool>
```
Select a prefab that contains the avatar name (e.g., `Chiffon_RetroKimono.prefab`).
Compatible outfits typically include the avatar name in the prefab name.

### Step 4: Place Outfit Prefab as Child of Avatar
```
<tool name=""InstantiatePrefab"">
<arg name=""assetPath"">Assets/path/to/outfit.prefab</arg>
<arg name=""parentName"">avatarRootName</arg>
</tool>
```
**Important**: Always specify the avatar root name as the 2nd argument `parentName`.
Placing at scene root instead of as avatar child will prevent Setup Outfit from working.

### Step 5: Run Modular Avatar Setup Outfit
```
<tool name=""SetupOutfit"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""outfitName"">outfitObjectName</arg>
</tool>
```
This automatically configures:
- ModularAvatarMergeArmature component addition
- Bone name remapping
- A/T-pose difference correction
- MeshSettings (ProbeAnchor, RootBone) configuration

### Step 5.5: Apply Shrink BlendShapes
Set body shrink BlendShapes for the new outfit to prevent skin clipping.

1. Check shrink-related BlendShapes on the Body mesh:
```
<tool name=""ListBlendShapesEx"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""filter"">shrink</arg>
</tool>
```

2. Set shrinks for body parts covered by the outfit to 100:
```
<tool name=""SetMultipleBlendShapes"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""blendShapeData"">Shrink_XXX=100;Shrink_YYY=100</arg>
</tool>
```

Note: Shrink names correspond to body parts or outfit areas.
Apply shrinks for parts covered by the outfit.
It's preferable to ask the user which shrinks to apply.

### Step 6: Verify
```
<tool name=""InspectGameObject"">
<arg name=""gameObjectName"">avatarRootName/outfitObjectName</arg>
</tool>
```
Verify that ModularAvatarMergeArmature and other components have been added.

## Adding Outfit Toggles
To add Expression Menu toggles for outfit parts:
```
<tool name=""SetupObjectToggle"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""targetPath"">outfitPartPath</arg>
</tool>
```
See the `object-toggle` skill for details.

---

## Incompatible Outfit Fitting Procedure

Even incompatible outfits (different bone structure) can be auto-fitted using OutfitFittingTools.

### Prerequisites
- Outfit prefab is already placed in the scene (doesn't need to be under the avatar, scene root is OK)
- After fitting, move to avatar child and run MA Setup Outfit

### Step 1: Compatibility Analysis
```
<tool name=""AnalyzeOutfitCompatibility"">
<arg name=""outfitName"">outfitObjectName</arg>
<arg name=""avatarName"">avatarRootName</arg>
</tool>
```
Check bone structure comparison, proportion differences, and compatibility score.

### Step 2: Bone Mapping Verification
```
<tool name=""MapOutfitBones"">
<arg name=""outfitName"">outfitObjectName</arg>
<arg name=""avatarName"">avatarRootName</arg>
</tool>
```
Review the auto-generated bone mapping table.
Report any low-confidence mappings to the user.

### Step 3: Execute Retarget
```
<tool name=""RetargetOutfit"">
<arg name=""outfitName"">outfitObjectName</arg>
<arg name=""avatarName"">avatarRootName</arg>
<arg name=""adaptStrength"">1.0</arg>
</tool>
```
- Remaps bones to avatar side
- Recalculates bind poses
- `adaptStrength=1.0` deforms mesh to conform to body surface (surface conforming)
- Auto-detects body mesh and pushes penetrating vertices to body surface + margin
- Performs accurate mesh-space correction via inverse skinning
- New mesh assets are saved to `Generated/OutfitFitting/`

### Step 4: Penetration Check
```
<tool name=""DetectMeshPenetration"">
<arg name=""outfitMeshName"">outfitMeshName</arg>
<arg name=""bodyMeshName"">Body</arg>
</tool>
```
Detects penetration between outfit mesh and body. Skip Step 5 if no penetration.

### Step 5: Fix Penetration (If Needed)
```
<tool name=""FixMeshPenetration"">
<arg name=""outfitMeshName"">outfitMeshName</arg>
<arg name=""bodyMeshName"">Body</arg>
<arg name=""offset"">0.001</arg>
</tool>
```
Pushes penetrating and too-close vertices outward along normals.
Performs accurate mesh-space correction via inverse skinning, considering bone rotation.

### Step 6: Apply Existing Shrink BlendShapes (If Needed)
Apply the avatar's existing Shrink BlendShapes.
**Note**: The avatar's mesh is not modified. Only existing BlendShapes are activated.
```
<tool name=""ListBlendShapesEx"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""filter"">shrink</arg>
</tool>
<tool name=""SetMultipleBlendShapes"">
<arg name=""gameObjectName"">avatarRootName/Body</arg>
<arg name=""blendShapeData"">Shrink_XXX=100;Shrink_YYY=100</arg>
</tool>
```

### Step 7: Finish with MA Setup Outfit
Move the retargeted outfit as a child of the avatar and finish with the same procedure as compatible outfits.
```
<tool name=""SetupOutfit"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""outfitName"">outfitObjectName</arg>
</tool>
```

### Step 8: Weight Transfer (If Needed)
If joint deformation looks unnatural, transfer weights from the avatar body mesh.
```
<tool name=""TransferOutfitWeights"">
<arg name=""outfitMeshName"">outfitMeshName</arg>
<arg name=""avatarBodyMeshName"">Body</arg>
</tool>
```

### Fitting Notes
- All operations are non-destructive (original meshes are unchanged, new assets are generated)
- Can be undone with Undo
- If the outfit has multiple SkinnedMeshRenderers, all are processed automatically
- Fine adjustments after fitting may require user judgment

---

## Common Mistakes
1. **Placing outfit at scene root** (for compatible outfits) → Must be placed as child of avatar
2. **Not removing existing outfit** → Old and new outfits overlap
3. **Only hiding without EditorOnly tag** → Data remains at upload, increasing file size
4. **Running Setup Outfit directly on incompatible outfit** → Won't work due to mismatched bone structure. Run RetargetOutfit first

## Notes
- MA Setup Outfit is non-destructive. Bone merging is executed at build time
- Compatible outfit prefab names typically contain the target avatar name
- Do not remove the Armature, Body, or other base body structures
- After outfit changes, recommend checking performance with GetAvatarPerformanceStats" },

            { "liltoon-effects", @"---
title: lilToon Animation Effects
description: lilToon shader scroll, blink, and overlay configuration procedures
tags: lilToon, emission, scroll, blink, animation
---

# lilToon Animation Effects Guide

## ScrollRotate Vector Format
`(scrollU, scrollV, angle, rotationSpeed)`
- scrollU/V: Scroll speed (full texture width/sec). 2.0 = repeats twice per second
- angle: Static rotation angle (radians)
- rotationSpeed: Dynamic rotation speed (radians/sec)

## Blink Vector Format
`(strength, type, speed, phase)`
- strength: 0-1, type: 0=smooth/1=ON-OFF, speed: radians/sec, phase: phase offset

## Pattern 1: Emission Scroll (Shooting Stars, Light Streaks)

### Steps
1. Check material:
   <tool name=""InspectLilToonMaterial"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   </tool>
2. Enable emission:
   <tool name=""SetLilToonFloat"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""property"">_UseEmission</arg>
   <arg name=""value"">1</arg>
   </tool>
3. Generate + apply texture:
   <tool name=""GenerateTextureWithAI"">
   <arg name=""gameObjectName"">AvatarRoot/Hair</arg>
   <arg name=""prompt"">seamless shooting stars sparkle pattern on transparent background</arg>
   <arg name=""islandIndices""></arg>
   <arg name=""materialIndex"">0</arg>
   <arg name=""textureProperty"">_EmissionMap</arg>
   </tool>
   - **Important**: _EmissionMap can be empty (auto-generates from transparent canvas)
4. Set color:
   <tool name=""SetLilToonColors"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""property"">Emission</arg>
   <arg name=""color"">1,1,1,1</arg>
   </tool>
5. Scroll:
   <tool name=""SetMaterialVector"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""propertyName"">_EmissionMap_ScrollRotate</arg>
   <arg name=""value"">0,2,0,0</arg>
   </tool>
   - Scrolls upward at speed 2

### Adding Blink
<tool name=""SetMaterialVector"">
<arg name=""materialPath"">Assets/.../material.mat</arg>
<arg name=""propertyName"">_EmissionBlink</arg>
<arg name=""value"">0.5,0,3.14,0</arg>
</tool>
- 50% strength smooth pulsing

## Pattern 2: Main2nd Overlay Scroll

1. <tool name=""SetLilToonFloat"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""property"">_UseMain2ndTex</arg>
   <arg name=""value"">1</arg>
   </tool>
2. <tool name=""GenerateTextureWithAI"">
   <arg name=""gameObjectName"">AvatarRoot/Hair</arg>
   <arg name=""prompt"">seamless sparkle overlay pattern</arg>
   <arg name=""islandIndices""></arg>
   <arg name=""materialIndex"">0</arg>
   <arg name=""textureProperty"">_Main2ndTex</arg>
   </tool>
3. <tool name=""SetMaterialVector"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""propertyName"">_Main2ndTex_ScrollRotate</arg>
   <arg name=""value"">1,0.5,0,0</arg>
   </tool>
4. Blend mode:
   <tool name=""SetMaterialFloat"">
   <arg name=""materialPath"">Assets/.../material.mat</arg>
   <arg name=""propertyName"">_Main2ndTexBlendMode</arg>
   <arg name=""value"">1</arg>
   </tool> (1=Add)

## Scroll-Compatible Properties

| Texture | ScrollRotate | Enable Flag |
|---------|-------------|-------------|
| _EmissionMap | _EmissionMap_ScrollRotate | _UseEmission=1 |
| _Emission2ndMap | _Emission2ndMap_ScrollRotate | _UseEmission2nd=1 |
| _Main2ndTex | _Main2ndTex_ScrollRotate | _UseMain2ndTex=1 |
| _Main3rdTex | _Main3rdTex_ScrollRotate | _UseMain3rdTex=1 |

## Common Mistakes
1. Setting scroll without assigning texture → Invisible. Assign texture first
2. Leaving _UseEmission=0 → Emission disabled. Set to 1 first
3. _EmissionColor=(0,0,0) → Black won't glow
4. ScrollRotate value too small (0.1, etc.) → Recommend 1-5
" },

            { "texture-editing", @"---
title: Texture Editing & AI Generation
description: Mesh island-based texture color changes and AI image generation for partial texture replacement
tags: texture, color change, gradient, AI generation, island, HSV, paint, color mistake, wrong mesh
---

# Texture Editing & AI Generation

## Mandatory Workflow (CRITICAL)
1. DISCOVER: ScanAvatarMeshes(avatarRoot) → visually identify all meshes
2. IDENTIFY: Use the image to find the target mesh. NEVER guess by name alone
3. INSPECT: ListRenderers(path) → confirm material
4. LEARN: Read the ""Common Mistakes"" section below before proceeding
5. EXECUTE: ApplyGradientEx / AdjustHSV (correct parameters)
6. VERIFY: CaptureSceneView() → visually confirm the result
7. CONFIRM: AskUser(""結果はいかがですか？"") → get user approval

## Overview

Edit avatar textures (main color, emission, normal map, etc.)
on a per-mesh-island basis. Supports color adjustments through AI image generation.

## Workflow

### Pattern A: Color Change (Gradient, HSV, Brightness/Contrast)

1. `ListRenderers(avatarName)` to check renderer list and materials
2. `ListMeshIslands(gameObjectName)` to get island list
3. `EnableIslandSelectionMode(gameObjectName)` to launch Scene view island selection
4. Have the user click on islands
5. `GetSelectedIslands()` to get selected island indices
6. Call editing tool:
   - `ApplyGradientEx(gameObjectName, fromColor, toColor, ...)` — Gradient
   - `AdjustHSV(gameObjectName, hueShift, satScale, valScale, islandIndices)` — Hue/Saturation/Value
   - `AdjustBrightnessContrast(gameObjectName, brightness, contrast, islandIndices)` — Brightness/Contrast

### Pattern B: AI Image Generation for Texture Replacement

1. `ListRenderers(avatarName)` to check renderer list and materials
2. `ListMeshIslands(gameObjectName)` to get island list
3. Identify target islands (`EnableIslandSelectionMode` → user selection → `GetSelectedIslands()`, or infer from island list)
4. Call `GenerateTextureWithAI`:

```
GenerateTextureWithAI(
  gameObjectName,      // Hierarchy path (e.g., ""avatarName/Body"")
  prompt,              // Generation prompt (e.g., ""make the eyes look like a galaxy nebula"")
  islandIndices,       // Island indices (e.g., ""5;6"")
  materialIndex,       // Material slot number (e.g., 0)
  textureProperty,     // Texture property name (e.g., ""_MainTex"")
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
- Semicolon-separated: `""0;1;3""`
- Empty string targets the entire texture

## Tool Call Examples

### Example 1: Make Eyes Look Like Space
```
User: ""Make the avatar's eyes look like space""

AI:
1. Confirm Body Material[0]: body contains eyes
<tool name=""ListRenderers"">
<arg name=""gameObjectName"">avatarName</arg>
</tool>
2. Island list
<tool name=""ListMeshIslands"">
<arg name=""gameObjectName"">avatarName/Body</arg>
</tool>
3.
<tool name=""EnableIslandSelectionMode"">
<arg name=""gameObjectName"">avatarName/Body</arg>
</tool>
4. ""Please click on the eye area in the Scene view""
5. → ""5;6""
<tool name=""GetSelectedIslands"">
</tool>
6.
<tool name=""GenerateTextureWithAI"">
<arg name=""gameObjectName"">avatarName/Body</arg>
<arg name=""prompt"">Transform the eye iris area into a cosmic galaxy nebula with deep blue, purple, and sparkles</arg>
<arg name=""islandIndices"">5;6</arg>
<arg name=""materialIndex"">0</arg>
<arg name=""textureProperty"">_MainTex</arg>
</tool>
```

### Example 2: Make Eyes Glow with Emission
```
AI:
1. (Same island identification as above)
2.
<tool name=""GenerateTextureWithAI"">
<arg name=""gameObjectName"">avatarName/Body</arg>
<arg name=""prompt"">Create a glowing nebula emission pattern</arg>
<arg name=""islandIndices"">5;6</arg>
<arg name=""materialIndex"">0</arg>
<arg name=""textureProperty"">_EmissionMap</arg>
</tool>
```

### Example 3: Hair Gradient
```
AI:
1.
<tool name=""ListMeshIslands"">
<arg name=""gameObjectName"">avatarName/hair</arg>
</tool>
2.
<tool name=""EnableIslandSelectionMode"">
<arg name=""gameObjectName"">avatarName/hair</arg>
</tool>
3. User selects hair tip islands
4. → ""0;1;2""
<tool name=""GetSelectedIslands"">
</tool>
5.
<tool name=""ApplyGradientEx"">
<arg name=""gameObjectName"">avatarName/hair</arg>
<arg name=""fromColor"">1.0,0.8,0.9</arg>
<arg name=""toColor"">0.5,0.2,0.8</arg>
<arg name=""direction"">top_to_bottom</arg>
<arg name=""blendMode"">screen</arg>
<arg name=""islandIndices"">0;1;2</arg>
</tool>
```

## Notes

- **Island selection is done by clicking in Scene view**. Let the user select rather than guessing
- `GenerateTextureWithAI` `textureProperty` defaults to `_MainTex`. Always explicitly specify `_EmissionMap` when editing emission
- AI generation preserves UV structure, so it won't draw in transparent areas. Specifying islands improves accuracy
- For multi-material objects, specify the correct slot with `materialIndex`
- For color-only changes, `AdjustHSV` or `ApplyGradientEx` is faster and more reliable

## Troubleshooting

- **Stack Overflow**: `FindGO` bug. Fixed in latest version
- **Texture property not found**: Check property name with `InspectMaterial`
- **AI-generated image is different size**: AI model constraint. System prompt strongly requests same size
- **Emission not glowing**: Material's `_UseEmission` may be 0. Set to 1 with `SetMaterialFloat`

## Common Mistakes (CRITICAL)

### Parameter Format
- ❌ SetLilToonColors(property=""_Color"") → ✅ property=""Main""
- ❌ blendMode=""overwrite"" → ✅ ""replace"" (valid: screen/overlay/tint/multiply/replace)

### Method Selection
- ❌ SetMaterialProperty(""_Color"") → no visible effect on lilToon
- ✅ ApplyGradientEx() → changes texture directly, always works

### Target Identification
- ❌ Guess path: ApplyGradientEx(""Armature/Head/Hair"", ...)
- ✅ ScanAvatarMeshes → visually confirm → use correct path

### Color Quick Reference
- Recolor:
<tool name=""ApplyGradientEx"">
<arg name=""gameObjectName"">go</arg>
<arg name=""fromColor"">#FF0000</arg>
<arg name=""toColor"">#FF0000</arg>
<arg name=""blendMode"">tint</arg>
</tool>
- Lighten:
<tool name=""ApplyGradientEx"">
<arg name=""gameObjectName"">go</arg>
<arg name=""fromColor"">#FFFFFF</arg>
<arg name=""toColor"">#FFFFFF</arg>
<arg name=""blendMode"">screen</arg>
</tool>
- Gradient:
<tool name=""ApplyGradientEx"">
<arg name=""gameObjectName"">go</arg>
<arg name=""fromColor"">#FF0000</arg>
<arg name=""toColor"">#0000FF</arg>
</tool>
- Brighten:
<tool name=""AdjustHSV"">
<arg name=""gameObjectName"">go</arg>
<arg name=""hueShift"">0</arg>
<arg name=""saturationScale"">1</arg>
<arg name=""valueScale"">1.5</arg>
</tool>
- Darken:
<tool name=""AdjustHSV"">
<arg name=""gameObjectName"">go</arg>
<arg name=""hueShift"">0</arg>
<arg name=""saturationScale"">1</arg>
<arg name=""valueScale"">0.5</arg>
</tool>
- Desaturate:
<tool name=""AdjustHSV"">
<arg name=""gameObjectName"">go</arg>
<arg name=""hueShift"">0</arg>
<arg name=""saturationScale"">0</arg>
<arg name=""valueScale"">1</arg>
</tool>" },

            { "accessory-setup", @"---
title: Accessory & Prop Placement
description: Non-destructive placement of weapons, accessories, and props on avatars (MA Bone Proxy + auto-alignment)
tags: accessory, prop, weapon, knife, ring, holster, BoneProxy, alignment
---

# Accessory & Prop Placement Guide

## Overview
Procedure for non-destructively placing rings, weapons, holsters, pouches, and other props
on avatar bones.
**Unlike outfits, do NOT use SetupOutfit/SetupOutfitWizard.**

## Decision Criteria: Outfit vs Accessory

| Item | Outfit | Accessory |
|------|--------|-----------|
| Examples | Jacket, pants, shoes | Ring, knife, pouch, gun |
| Bone structure | Multiple bones within Armature | Single mesh or few parts |
| Tool | SetupOutfit / SetupOutfitWizard | AlignAccessoryToBone + MA Bone Proxy |
| Connection method | Armature merge | MA Bone Proxy (single bone follow) |

## Standard Workflow

### Step 1: Structure Analysis (For Gimmick-Equipped Items)
For Prefabs containing MA/VRCFury components, analyze the structure first:
```
<tool name=""AnalyzeGimmickStructure"">
<arg name=""gameObjectName"">prefabName</arg>
</tool>
```
→ If child objects with BoneProxy are found, just place as child of avatar.

### Step 2: Measure Avatar
Get the target avatar's dimensions (used for auto scale calculation):
```
<tool name=""MeasureAvatarBody"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```

### Step 3: Place Prefab in Scene
Instantiate **as a child of the avatar**:
```
<tool name=""InstantiatePrefab"">
<arg name=""assetPath"">Assets/.../Item.prefab</arg>
<arg name=""parentName"">avatarRootName</arg>
</tool>
```

### Step 4: Ask User About Attachment Location
```
<tool name=""AskUser"">
<arg name=""question"">Where should it be attached?</arg>
<arg name=""option1"">Hold in right hand</arg>
<arg name=""option2"">Hold in left hand</arg>
<arg name=""option3"">Attach to hip</arg>
<arg name=""option4"">Attach to thigh</arg>
</tool>
```

### Step 5: Auto-Alignment (Most Important)
**NEVER use SetTransform for manual coordinate guessing.**
Attach with the appropriate style based on user selection:

#### Holding in Hand (grip)
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">itemName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">RightHand</arg>
<arg name=""attachmentStyle"">grip</arg>
</tool>
```

#### Attached to Body (surface) — Hip, Thigh, Back, etc.
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">itemName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">RightUpperLeg</arg>
<arg name=""attachmentStyle"">surface</arg>
<arg name=""direction"">right</arg>
</tool>
```
direction parameter specifies attachment direction:
- Outer thigh: direction='right' (right leg) / 'left' (left leg)
- Front of hip: direction='front'
- Back of hip: direction='back'
- Back: direction='back'

#### Wrapping Around (wrap) — Bracelets, Cuffs, etc.
```
<tool name=""AlignAccessoryToBone"">
<arg name=""accessoryName"">itemName</arg>
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""boneName"">LeftLowerArm</arg>
<arg name=""attachmentStyle"">wrap</arg>
</tool>
```

### Step 6: Verify Result
Capture from SceneView to verify:
```
<tool name=""CaptureMultiAngle"">
<arg name=""targetName"">itemName</arg>
<arg name=""angles"">front,left,right,back</arg>
</tool>
```

### Step 7: Leave Fine Adjustment to User
After auto-placement, fine adjustment is **more efficient when the user directly manipulates** using the Transform panel or Scene view gizmos rather than AI guessing coordinates.
Guide as follows:
- ""If fine adjustment is needed, use the Scene view gizmos (W/E/R keys) or the Transform panel in the UnityAgent window for direct manipulation.""
- Do not repeatedly guess with SetTransform

## Bone Name Reference (HumanBodyBones)
Key bone names:
- Hands: RightHand, LeftHand
- Fingers: RightIndexProximal, LeftRingProximal, etc.
- Arms: RightUpperArm, RightLowerArm, LeftUpperArm, LeftLowerArm
- Legs: RightUpperLeg, RightLowerLeg, LeftUpperLeg, LeftLowerLeg
- Torso: Hips, Spine, Chest, UpperChest
- Head: Head, Neck
- Feet: RightFoot, LeftFoot

## attachmentStyle Selection Guide

| Style | Use Case | Behavior |
|-------|----------|----------|
| surface | Holster, pouch, knife (sheath) | Detects body surface via BodySDF, aligns item's flat side to body |
| grip | Handheld weapons (sword, gun, staff) | Positions at grip along hand bone |
| wrap | Bracelet, wristband, cuff | Wraps around the bone circumference |

## Ring Special Workflow
Rings have dedicated tools:
```
<tool name=""AttachRingWithBoneProxy"">
<arg name=""ringName"">ringName</arg>
<arg name=""boneName"">RightRingProximal</arg>
</tool>
<tool name=""AlignRingToBone"">
<arg name=""ringName"">ringName</arg>
</tool>
```
Fine adjustment: NudgeRing, AdjustRingScale, RotateRing

## Notes
- **Do not guess coordinates with SetTransform**: AlignAccessoryToBone auto-calculates
- **Do not use ArmatureLink/SetupOutfit for props**: Bone merge is for outfits
- For gimmick-equipped Prefabs (with MA/VRCFury), use AnalyzeGimmickStructure first
- scaleToAvatar=true (default) auto-corrects for avatars with different scales
- Guide users to use Scene view gizmos or Transform panel for post-placement fine adjustment" },

            { "troubleshooting", @"---
title: Avatar Troubleshooting
description: Systematically diagnose and resolve common VRChat avatar issues
tags: troubleshooting, diagnosis, bug, issue, Write Defaults, parameter, performance
---

# Avatar Troubleshooting

## Overview
When an avatar issue is reported, **investigate with diagnostic tools first** rather than guessing fixes.

## Step 1: Comprehensive Diagnosis (Always Run First)

```
<tool name=""ValidateAvatar"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ Returns a categorized list of issues: Error/Warning/Info.
Errors must be fixed, Warnings are recommended, Info is for reference.

### Issues Detected by ValidateAvatar:
- Write Defaults inconsistency (per layer)
- Parameter budget exceeded
- Missing References
- Expression Menu issues
- Polygon count classification
- AAO TraceAndOptimize not applied
- Non-standard shaders

## Step 2: Performance Check

```
<tool name=""GetAvatarPerformanceStats"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ Returns VRChat performance rank for all categories.
Identify bottleneck categories and suggest improvements.

## Step 3: Individual Diagnosis (As Needed)

### Write Defaults Issues
```
<tool name=""CheckWriteDefaults"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ WD state per layer (ON/OFF/MIXED).
MIXED is problematic → needs unification.

Fix method: See ReadSkill('avatar-optimization').

### Parameter Budget
```
<tool name=""CheckParameterBudget"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ Sync parameter bit consumption. Exceeding 256 bits is an error.

Solutions:
- Remove unnecessary parameters:
<tool name=""RemoveVRCExpressionParameter"">
<arg name=""avatarRootName"">avatarRootName</arg>
<arg name=""paramName"">parameterName</arg>
</tool>
- Change to synced=false (local only)
- Change Int→Bool (save 8bit→1bit)

### PhysBone Issues
```
<tool name=""ListVRCPhysBones"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ All PhysBones and their parameters.
Identify excessive PhysBone count or inefficient settings.

## Common Issues and Solutions

| Symptom | Diagnosis | Solution |
|---------|-----------|----------|
| Expressions not working | ValidateAvatar → Check FX | Fix FX controller/parameter name mismatch |
| Toggles not working | CheckWriteDefaults | Unify WD (all ON or all OFF) |
| Avatar is heavy | GetAvatarPerformanceStats | Identify bottleneck category → optimize |
| Parameter budget exceeded | CheckParameterBudget | Remove unnecessary parameters or make local |
| Hair/skirt not moving | ListVRCPhysBones | Check PhysBone settings, pull/spring values |
| Mesh disappearing | InspectGameObject → check active | Enable with SetActive |
| Material is pink | InspectMaterial | Check shader, reconfigure |
| Bones flying off | InspectVRCPhysBone | Fix abnormal parameter values |
| ""GameObject 'X' not found"" | Wrong path | ScanAvatarMeshes to discover correct paths |
| ""Material not found at 'X'"" | Wrong material path | SearchAssets(name, ""Material"") |
| ""Unknown color property"" | Raw property name used | Use friendly names (Main/Shadow/Rim) |
| ""Invalid blendMode"" | Non-existent mode | Valid: screen/overlay/tint/multiply/replace |

## Key Principles
- **Don't fix by guessing**: First identify the cause with diagnostic tools
- **Fix one thing at a time**: Multiple changes make cause identification difficult
- **Re-diagnose after fixing**: Verify fix results with [ValidateAvatar]
- If user report contradicts diagnostic results, investigate further" },

            { "physbone-setup", @"---
title: PhysBone Setup
description: Guide for adding VRCPhysBone, applying templates, and adjusting parameters
tags: PhysBone, physics, hair, skirt, tail, ears, breast
---

# PhysBone Setup Guide

## Overview
Procedure for setting up VRCPhysBone on avatar's dynamic parts (hair, skirt, tail, etc.).
Use templates for efficient setup.

## Step 1: Check Existing PhysBones

```
<tool name=""ListVRCPhysBones"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ Check already configured PhysBones. Avoid duplicate additions.

## Step 2: Identify Bone Chain

Identify the parent of the bones you want to make dynamic:
```
<tool name=""GetHierarchyTree"">
<arg name=""name"">avatarRootName/Armature</arg>
<arg name=""maxDepth"">5</arg>
</tool>
```

PhysBone should be added to the **root of the chain**.
Example: Adding PhysBone to Hair_front_Root → All children (Hair_front_0, Hair_front_1...) will be dynamic.

## Step 3: Apply Template (Recommended)

Apply a template based on the bone type:
```
<tool name=""AddVRCPhysBone"">
<arg name=""goName"">boneName</arg>
</tool>
<tool name=""ApplyVRCPhysBoneTemplate"">
<arg name=""goName"">boneName</arg>
<arg name=""template"">templateName</arg>
</tool>
```

### Template List and Use Cases

| Template | Use Case | Characteristics |
|----------|----------|----------------|
| Hair | Bangs, side hair, ponytail | Light sway, grabbable |
| Skirt | Skirt, coat hem | Stronger gravity, not grabbable |
| Tail | Tail, kemono tail | Springy, hinge-limited |
| Breast | Chest | Subtle, semi-fixed |
| Ears | Kemono ears, bunny ears | Slight sway, stiff |
| Ribbon | Ribbons, hanging cloth | Lots of sway, no limits |

### Template Parameter Values

| Parameter | Hair | Skirt | Tail | Breast | Ears | Ribbon |
|-----------|------|-------|------|--------|------|--------|
| pull | 0.2 | 0.3 | 0.3 | 0.15 | 0.1 | 0.3 |
| spring | 0.2 | 0.4 | 0.5 | 0.3 | 0.1 | 0.5 |
| stiffness | 0.2 | 0.1 | 0.3 | 0.3 | 0.5 | 0.1 |
| gravity | 0.1 | 0.3 | 0.05 | 0.05 | 0.02 | 0.15 |
| immobile | 0 | 0 | 0 | 0.5 | 0 | 0 |
| limitType | Angle | Angle | Hinge | Angle | Angle | None |
| maxAngleX | 60 | 45 | 90 | 30 | 30 | - |

## Step 4: Custom Adjustments (As Needed)

Fine-tune after applying template:
```
<tool name=""ConfigureVRCPhysBone"">
<arg name=""goName"">boneName</arg>
<arg name=""pull"">0.3</arg>
<arg name=""gravity"">0.2</arg>
</tool>
```

### Parameter Meanings and Tuning Guide

| Parameter | Range | Meaning | Effect of Increasing |
|-----------|-------|---------|---------------------|
| pull | 0-1 | Force to return to original pose | Returns to pose faster |
| spring | 0-1 | Spring elasticity | More bouncy |
| stiffness | 0-1 | Rigidity | Harder to bend |
| gravity | 0-1 | Gravity influence | Hangs down more |
| gravityFalloff | 0-1 | Gravity decay by angle | Less gravity when upright |
| immobile | 0-1 | Movement sway suppression | Less sway when moving |
| radius | 0+ | Collision radius (meters) | Larger collision area |

## Step 5: Collider Setup (Penetration Prevention)

Add colliders to prevent penetration through the body:
```
<tool name=""AddVRCPhysBoneCollider"">
<arg name=""goName"">Head</arg>
<arg name=""shapeType"">1</arg>
<arg name=""radius"">0.08</arg>
<arg name=""height"">0.15</arg>
</tool>
<tool name=""LinkVRCColliderToPhysBone"">
<arg name=""physBoneGoName"">Hair_Root</arg>
<arg name=""colliderGoName"">Head</arg>
</tool>
```

### Common Collider Placements

| Location | Shape | Purpose |
|----------|-------|---------|
| Head | Capsule | Prevent hair from penetrating the head |
| Chest | Capsule | Prevent long hair from penetrating the body |
| UpperLeg | Capsule | Prevent skirt from penetrating the legs |

## Step 6: Performance Check

```
<tool name=""GetAvatarPerformanceStats"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
Check ranks for PhysBones, PB Transforms, PB Colliders, and PB Collision Checks.

## Template Inference Rules from Bone Names

| Keyword in Bone Name | Template |
|---------------------|----------|
| Hair, hair | Hair |
| Skirt, skirt | Skirt |
| Tail, tail | Tail |
| Breast, breast | Breast |
| Ear, ear | Ears |
| Ribbon, ribbon, Bow | Ribbon |
| Coat, coat, Cape | Skirt |
| Ahoge, ahoge | Ribbon |

If unclear, check existing settings with InspectVRCPhysBone or ask the user.

## Notes
- Add PhysBone to the **root of the chain** (not the tip)
- Do not add multiple PhysBones to the same bone
- Use exclusions to exclude unwanted children:
<tool name=""SetVRCPhysBoneExclusions"">
<arg name=""goName"">boneName</arg>
<arg name=""exclusionNames"">exclude1,exclude2</arg>
</tool>
- Specify endpoint position via Configure:
<tool name=""ConfigureVRCPhysBone"">
<arg name=""goName"">boneName</arg>
<arg name=""endpointPosition"">0,0.1,0</arg>
</tool>
- Performance rank thresholds: See
<tool name=""ReadSkill"">
<arg name=""skillName"">avatar-optimization</arg>
</tool>" },

            { "batch-operations", @"---
title: Batch Operations Guide
description: Patterns for bulk changes to multiple objects and materials
tags: batch, bulk, multiple, material, component, change
---

# Batch Operations Guide

## Overview
Efficient patterns for applying the same change to multiple objects or materials.
**Use appropriate tools and patterns rather than operating one by one.**

## Pattern 1: Bulk Renderer Configuration

### Shadow Settings (Dedicated Tool)
```
<tool name=""BatchConfigureShadows"">
<arg name=""rootGoName"">avatarRootName</arg>
<arg name=""shadowCasting"">1</arg>
<arg name=""receiveShadows"">1</arg>
</tool>
```
→ Bulk change shadow settings for all Renderers.

### Get Renderer List → Configure Individually
```
<tool name=""ListRenderers"">
<arg name=""gameObjectName"">avatarRootName</arg>
</tool>
```
→ Returns paths of all Renderers. For each Renderer:
```
<tool name=""SetProperty"">
<arg name=""goName"">path</arg>
<arg name=""componentType"">SkinnedMeshRenderer</arg>
<arg name=""propertyPath"">probeAnchor</arg>
<arg name=""value"">referenceTarget</arg>
</tool>
```

## Pattern 2: Bulk Material Changes

### Step 1: Get Material List
```
<tool name=""ListRenderers"">
<arg name=""gameObjectName"">avatarRootName</arg>
</tool>
```
Check material slots for each Renderer.

### Step 2: Check Material Properties
```
<tool name=""InspectMaterial"">
<arg name=""materialPath"">Assets/.../Material.mat</arg>
</tool>
```

### Step 3: Bulk Apply
When applying the same change to multiple materials, make consecutive tool calls:
```
<tool name=""SetMaterialFloat"">
<arg name=""materialPath"">Assets/.../Mat1.mat</arg>
<arg name=""propertyName"">_Metallic</arg>
<arg name=""value"">0.8</arg>
</tool>
<tool name=""SetMaterialFloat"">
<arg name=""materialPath"">Assets/.../Mat2.mat</arg>
<arg name=""propertyName"">_Metallic</arg>
<arg name=""value"">0.8</arg>
</tool>
<tool name=""SetMaterialFloat"">
<arg name=""materialPath"">Assets/.../Mat3.mat</arg>
<arg name=""propertyName"">_Metallic</arg>
<arg name=""value"">0.8</arg>
</tool>
```

### lilToon Bulk Settings Example
```
<tool name=""SetMaterialFloat"">
<arg name=""materialPath"">Assets/.../Mat.mat</arg>
<arg name=""propertyName"">_OutlineWidth</arg>
<arg name=""value"">0.1</arg>
</tool>
<tool name=""SetMaterialFloat"">
<arg name=""materialPath"">Assets/.../Mat.mat</arg>
<arg name=""propertyName"">_OutlineFixWidth</arg>
<arg name=""value"">1</arg>
</tool>
```

## Pattern 3: Bulk BlendShape Settings

### Set Multiple BlendShapes at Once
```
<tool name=""SetMultipleBlendShapes"">
<arg name=""gameObjectName"">Body</arg>
<arg name=""blendShapeData"">Shrink_UpperBody=100;Shrink_LowerBody=100;Shrink_Arm=100</arg>
</tool>
```

### Check All BlendShapes
```
<tool name=""ListBlendShapes"">
<arg name=""goName"">Body</arg>
</tool>
```

## Pattern 4: Bulk Component Operations

### Search for Specific Components in Avatar
```
<tool name=""GetHierarchyTree"">
<arg name=""name"">avatarRootName</arg>
<arg name=""maxDepth"">5</arg>
</tool>
```
→ Identify targets from tree, then operate on each object.

### Bulk PhysBone Check & Configure
```
<tool name=""ListVRCPhysBones"">
<arg name=""avatarRootName"">avatarRootName</arg>
</tool>
```
→ Full PhysBone list. Configure individually:
```
<tool name=""ConfigureVRCPhysBone"">
<arg name=""goName"">Hair_Root</arg>
<arg name=""pull"">0.2</arg>
<arg name=""spring"">0.3</arg>
</tool>
<tool name=""ConfigureVRCPhysBone"">
<arg name=""goName"">Skirt_Root</arg>
<arg name=""pull"">0.3</arg>
<arg name=""gravity"">0.3</arg>
</tool>
```

## Pattern 5: Bulk Object ON/OFF

### Hide Multiple Objects
```
<tool name=""SetActive"">
<arg name=""gameObjectName"">avatarRootName/Outfit_Old/Top</arg>
<arg name=""active"">false</arg>
</tool>
<tool name=""SetActive"">
<arg name=""gameObjectName"">avatarRootName/Outfit_Old/Bottom</arg>
<arg name=""active"">false</arg>
</tool>
<tool name=""SetActive"">
<arg name=""gameObjectName"">avatarRootName/Outfit_Old/Shoes</arg>
<arg name=""active"">false</arg>
</tool>
```

### Set EditorOnly Tag (Exclude from Upload)
```
<tool name=""SetTag"">
<arg name=""gameObjectName"">avatarRootName/Outfit_Old</arg>
<arg name=""tag"">EditorOnly</arg>
</tool>
```

## Pattern 6: Bulk Hierarchy Rename

Identify targets and rename individually:
```
<tool name=""ListChildren"">
<arg name=""name"">parentObjectName</arg>
</tool>
<tool name=""RenameGameObject"">
<arg name=""currentName"">oldName</arg>
<arg name=""newName"">newName</arg>
</tool>
```

## Efficient Operation Principles

1. **Get the list first**: Use ListRenderers, ListVRCPhysBones, ListChildren, etc. for overview
2. **Identify patterns**: Use consecutive calls when repeating the same operation
3. **Prefer dedicated batch tools**: BatchConfigureShadows, SetMultipleBlendShapes, etc.
4. **Supplement with generic tools**: SetProperty works on any component property
5. **Check before and after**: Verify status before and after changes" },

            { "discovery-workflow", @"---
title: Avatar Discovery Workflow
description: How to visually identify and find meshes, materials, and bones on any avatar
tags: discovery, avatar, mesh, material, hierarchy, find, search, path, scan, identify
---

# Avatar Discovery Workflow

## Overview
Avatar mesh names, material names, and bone structures differ per avatar.
Names alone CANNOT determine what a mesh is (""Body"" may be a head mesh).
You MUST visually confirm each mesh with ScanAvatarMeshes before any modification.

## Mandatory Workflow

### Step 1: Identify Avatar Root
- [Hierarchy Selection] present → use that root
- Not present → ListRootObjects() → identify avatar candidates
- Multiple avatars → AskUser(""Which avatar?"", ...)

### Step 2: Visual Mesh Identification (CRITICAL)
```
<tool name=""ScanAvatarMeshes"">
<arg name=""avatarRootName"">AvatarRoot</arg>
</tool>
```
→ Receive a grid image with each mesh isolated
→ Visually determine which mesh is hair, body, clothes, etc.

### Step 3: Identify Target Mesh
- Match user's request (e.g., ""hair color"") with grid image
- Ambiguous → AskUser(""Which mesh?"", mesh1, mesh2)

### Step 4: Material Details (for color changes)
```
<tool name=""ListRenderers"">
<arg name=""gameObjectName"">targetMeshPath</arg>
</tool>
```
→ Get material name/path
```
<tool name=""InspectLilToonMaterial"">
<arg name=""materialPath"">materialPath</arg>
</tool>
```
→ Check current colors/properties

## Common Failure Patterns
- ❌ Name-based guessing: ""Hair"" → actually an accessory
- ❌ Path guessing: ""Armature/Head/Hair"" → path doesn't exist
- ❌ Color change without ScanAvatarMeshes → wrong mesh modified
- ❌ Using FindObjectsByName without verifying → multiple matches, wrong one used
- ✅ ScanAvatarMeshes → visually confirm → operate on correct mesh

## Example
User: ""Make the hair blue""

```
Agent: Let me visually identify all meshes on the avatar.
<tool name=""ScanAvatarMeshes"">
<arg name=""avatarRootName"">MANUKA</arg>
</tool>

System: ""Scanned 6 meshes. [1] Manuka_atama — 12k verts...
        [2] Manuka_hair_front — 8k verts... ...""
        + image (isolated grid of each mesh)

Agent: From the image, [2] Manuka_hair_front and [3] Manuka_hair_bun
       are the hair meshes. Let me read the color change procedure.
<tool name=""ReadSkill"">
<arg name=""skillName"">texture-editing</arg>
</tool>
```
(Continue with texture-editing skill workflow → CaptureSceneView → AskUser)" },
        };
    }
}
