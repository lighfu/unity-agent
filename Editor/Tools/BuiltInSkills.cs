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
[GetAvatarPerformanceStats('avatarRootName')]
```

### 2. AvatarDescriptor Check
Verify the configuration is correct:
```
[InspectVRCAvatarDescriptor('avatarRootName')]
```

### 3. Common Issues to Check
- ViewPosition is between the eyes
- LipSync is correctly configured
- ExpressionParameters cost is within 256 bits

### 4. Execute Build
Open the SDK Control Panel:
```
[ExecuteMenu('VRChat SDK/Show Control Panel')]
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
| Polygons | ≤32,000 | ≤70,000 | ≤120,000 | ≤200,000 |
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
Steps:
1. Place the outfit Prefab as a child of the avatar
2. Add MA Merge Armature to the outfit's Armature object
3. Bones are automatically merged at build time
```

### MA Merge Animator
Integrates animator layers into the avatar's FX layer.
```
Steps:
1. Prepare the gimmick's Animator Controller
2. Add MA Merge Animator component
3. Specify the target layer type (FX, etc.)
```

### MA Menu Item / MA Parameters
Non-destructively adds Expression Menu and Parameters.

### MA Bone Proxy
Non-destructively places objects as children of specific bones.
Used for making weapons or accessories follow the hand or Head.

## General Outfit Setup Procedure

1. Place the outfit Prefab as a child of the avatar:
   ```
   [SetParent('outfitName', 'avatarRootName')]
   ```

2. Verify MA Merge Armature is configured on the outfit's Armature:
   ```
   [InspectGameObject('avatarRootName/outfitName/Armature')]
   ```

3. Prevent body mesh clipping:
   - Use AAO's Remove Mesh in Box to remove body mesh under the clothing
   - Or use BlendShapes to shrink the body

4. Material adjustments:
   - Check that outfit materials match the avatar's skin color
   - Adjust texture colors as needed

## Notes
- Write Defaults: Keep consistent across the entire avatar (all ON or all OFF)
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
- **Registered** expressions: max **7** (shown in Expression Menu)
- **Unregistered** expressions: unlimited (not in menu, gesture-only)

## Tool Reference

### Detection & Inspection
```
[FindFaceEmo()] — Discover FaceEmo objects in scene
[InspectFaceEmo('FaceEmo')] — Show AV3 settings, expression modes, gestures
[ListFaceEmoExpressions('FaceEmo')] — List all expressions and branches
[InspectExpressionDetail('Angry')] — Detailed info (branches, conditions, animations)
[LaunchFaceEmoWindow('FaceEmo')] — Open the FaceEmo editor window
```

### Expression Building
Build expressions entirely within FaceEmo tools. NEVER guess BlendShape names.
```
[SearchExpressionShapes('Body', 'eye')] — Search blend shapes by keyword filter
[SetExpressionPreview('Body', 'eye_joy=100;mouth_smile=80')] — Set blend shape values for preview
[CaptureExpressionPreview('Avatar')] — Focus camera on face + capture preview image
[ResetExpressionPreview('Body')] — Reset all blend shapes to 0
[GetCurrentExpressionValues('Body')] — Get current non-zero blend shape values
```

### Expression Registration & Management
```
[AddExpression('Angry', 'Registered', 'Assets/.../angry.anim')] — Add new expression
[RemoveExpression('Angry')] — Remove expression (with confirmation)
[CopyExpression('Smile', 'Smile2', 'Registered')] — Duplicate expression
[SetExpressionAnimation('Angry', 'Assets/.../angry.anim')] — Set/change animation clip
[ModifyExpressionProperties('Angry', newDisplayName='Angry_v2')] — Modify properties
[SetDefaultExpression('Smile')] — Set default expression
[CreateAndRegisterExpression('Body', 'Smile', 'Assets/.../smile.anim')] — Save current mesh weights as clip + register in one step
[CreateExpressionFromData('Smile', 'Assets/.../smile.anim', 'Body', 'eye_joy=100;mouth_smile=80')] — Create clip from explicit data + register
[UpdateExpressionAnimation('Smile', 'Body', 'Assets/.../smile_v2.anim')] — Re-create clip from current mesh weights + update existing expression
[PreviewFaceEmoExpression('Smile', 'Body')] — Preview existing expression on mesh
```

### Gesture Branch Management
```
[AddGestureBranch('Angry', 'Left=Fist', 'Assets/.../angry.anim')] — Add gesture branch
[RemoveGestureBranch('Angry', 0)] — Remove branch by index
[AddGestureCondition('Angry', 0, 'Right', 'Fist')] — Add condition to branch
[ModifyBranchProperties('Angry', 0, eyeTracking='Animation')] — Modify branch properties
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
[CreateExpressionGroup('Combat', 'Registered')] — Create submenu group
[MoveExpressionItem('Angry', 'Unregistered')] — Move/reorder items
```

### Import & Apply
```
[ImportExpressions()] — Import from avatar's existing FX layer (patterns + blink + mouth morph + contacts + prefix). Requires target avatar to be set first.
[ApplyFaceEmoToAvatar()] — Generate FX layer from FaceEmo menu. Run after finishing all edits.
```

### Settings
```
[ConfigureTargetAvatar('Chiffon')] — Set target avatar (fixes Avatar=None)
[ConfigureFaceEmoGeneration()] — View/change generation settings
[ConfigureMouthMorphs('list')] — Configure mouth morph BlendShapes
[ConfigureAfkFace()] — Configure AFK expression
[ConfigureFeatureToggles()] — Configure feature toggles
```

## Workflows

### A. Setup FaceEmo on Avatar (""FaceEmoを適用して"")
```
1. [FindFaceEmo()] → Check if FaceEmo exists and is configured for this avatar
2. If already configured → skip to step 6
3. If not found: [ExecuteMenu('FaceEmo/New Menu')]
4. [ConfigureTargetAvatar('AvatarName')] → Set target avatar
5. [ImportExpressions()] → Auto-import expressions from existing FX layer
6. [LaunchFaceEmoWindow()] → Open FaceEmo editor window
```
This is the primary workflow when user asks to ""apply"" or ""set up"" FaceEmo on an avatar.
ImportExpressions reads the existing FX Animator and recreates the expression setup in FaceEmo.

### B. Assign Expression to Gesture (""人差し指で驚いた表情にして"")
```
1. [ListFaceEmoExpressions()] → Check if the requested gesture already has an expression
2. If gesture already assigned → [AskUser] whether to overwrite
   - If denied → stop
3. [SearchExpressionShapes('Body', 'eye')] → Find relevant blend shapes
   [SearchExpressionShapes('Body', 'mouth')] → (search multiple keywords as needed)
4. [SetExpressionPreview('Body', 'eye_surprised=100;mouth_open=60')] → Build expression
5. [CaptureExpressionPreview('Avatar')] → Capture for visual verification
6. [AskUser] → Confirm expression appearance
7. [CreateAndRegisterExpression('Body', 'Surprised', 'Assets/.../surprised.anim')] → Save + register
8. [AddGestureBranch('Surprised', 'Left=FingerPoint')] → Assign gesture condition
9. [ResetExpressionPreview('Body')] → Clean up
```
If overwriting: remove the old expression first with [RemoveExpression], then proceed from step 3.
IMPORTANT: Always use SearchExpressionShapes with filter. NEVER guess BlendShape names.

### C. Create Expression from Scratch
```
1. [SearchExpressionShapes('Body', 'eye')] → Find shapes by keyword
2. [SetExpressionPreview('Body', 'eye_joy=100;mouth_smile=80')] → Set preview
3. [CaptureExpressionPreview('Avatar')] → Capture for visual verification
4. [AskUser] → Confirm with user
5. [CreateAndRegisterExpression('Body', 'Smile', 'Assets/.../smile.anim')] → Save + register
6. [AddGestureBranch('Smile', 'Left=Fist')] → Assign gesture (if user specified)
7. [ResetExpressionPreview('Body')] → Clean up
```
IMPORTANT: Always use SearchExpressionShapes with filter. NEVER guess BlendShape names.

### D. Edit Existing Expression
```
1. [PreviewFaceEmoExpression('Smile', 'Body')] → Preview current expression
2. [SearchExpressionShapes('Body', 'mouth')] → Search for shapes to adjust
3. [SetExpressionPreview('Body', 'mouth_smile=100;mouth_open=30')] → Adjust
4. [CaptureExpressionPreview('Avatar')] → Verify
5. [UpdateExpressionAnimation('Smile', 'Body', 'Assets/.../smile_v2.anim')] → Update clip + FaceEmo
6. [ResetExpressionPreview('Body')] → Clean up
```

### E. Register Existing .anim File
```
1. [AddExpression('Angry', 'Registered', 'Assets/.../angry.anim')]
2. [AddGestureBranch('Angry', 'Left=Fist')]
```

### F. Organize Menu
```
1. [ListFaceEmoExpressions()] → List all
2. [CreateExpressionGroup('Combat', 'Registered')]
3. [MoveExpressionItem('Angry', 'Combat')]
```

### G. Apply to Avatar
```
1. [ApplyFaceEmoToAvatar()] → Generate FX layer
```
Run this after finishing all expression edits to generate the FX layer and parameters.

### H. Preview / List Registered Expressions (""今どんな表情が入ってる？"")
```
1. [ListFaceEmoExpressions()] → List all expressions with gesture assignments
2. For each expression the user wants to see:
   [PreviewFaceEmoExpression('Smile', 'Body')] → Apply blend shapes to mesh
   [CaptureExpressionPreview('Avatar')] → Capture face image for user
   [ResetExpressionPreview('Body')] → Reset before next preview
```

### I. Delete Expression (""怒りの表情を消して"")
```
1. [ListFaceEmoExpressions()] → Confirm the expression exists
2. [AskUser] → Confirm deletion with user
3. [RemoveExpression('Angry')] → Remove (tool has built-in confirmation)
```

### J. Swap Gestures Between Expressions (""笑顔と怒りのジェスチャーを入れ替えて"")
```
1. [InspectExpressionDetail('Smile')] → Get current gesture conditions
   [InspectExpressionDetail('Angry')] → Get current gesture conditions
2. [RemoveGestureBranch('Smile', 0)] → Remove old branch from Smile
   [RemoveGestureBranch('Angry', 0)] → Remove old branch from Angry
3. [AddGestureBranch('Smile', '<Angry's old condition>')] → Assign Angry's gesture to Smile
   [AddGestureBranch('Angry', '<Smile's old condition>')] → Assign Smile's gesture to Angry
```

### K. Set Default Expression (""何もしてないときの表情を笑顔にして"")
```
1. [ListFaceEmoExpressions()] → Find the expression
2. [SetDefaultExpression('Smile')] → Set as default (Neutral gesture)
```
The default expression is shown when no gesture is active.

### L. Configure AFK Expression (""AFK中は寝顔にして"")
```
1. Check if the desired .anim clip already exists
2. If not → create it first (Workflow C, but skip FaceEmo registration)
   [SearchExpressionShapes('Body', 'sleep')] → Find shapes
   [SetExpressionPreview('Body', 'eye_close=100;mouth_relax=50')] → Build expression
   [CaptureExpressionPreview('Avatar')] → Verify
   Use CreateExpressionClip or CreateExpressionClipFromData to save the .anim file
3. [ConfigureAfkFace(enableAfk='true', afkFacePath='Assets/.../sleeping.anim')] → Set AFK clip
4. [ResetExpressionPreview('Body')] → Clean up
```
ConfigureAfkFace params: enableAfk, afkEnterFacePath, afkFacePath, afkExitFacePath, exitDuration.

### M. Left/Right Hand Separate Conditions (""左手グーで笑顔、右手ピースで怒り"")
```
1. [AddExpression('Smile', 'Registered', 'Assets/.../smile.anim')]
   [AddExpression('Angry', 'Registered', 'Assets/.../angry.anim')]
2. [AddGestureBranch('Smile', 'Left=Fist')]
   [AddGestureBranch('Angry', 'Right=Victory')]
```
Use `Left=` or `Right=` prefix to specify which hand triggers the expression.
Both hands: `'Left=Fist;Right=Victory'` (AND condition).
Either hand: `'Either=Fist'`.

### N. Batch Create Multiple Expressions (""表情を全部作り直して"")
```
1. [ListFaceEmoExpressions()] → Check current state
2. [AskUser] → Confirm which expressions to create/replace
3. For each expression, repeat Workflow C:
   a. [SearchExpressionShapes] → Find shapes
   b. [SetExpressionPreview] → Build expression
   c. [CaptureExpressionPreview] → Verify
   d. [AskUser] → Confirm
   e. [CreateAndRegisterExpression] → Save + register
   f. [AddGestureBranch] → Assign gesture
   g. [ResetExpressionPreview] → Clean up before next
```

## Emotion-to-BlendShape Guide
When users describe expressions by emotion (e.g. ""ウインク"", ""泣いている"", ""照れ""),
search for relevant blend shapes using these keyword groups:

| Emotion | Search keywords (use with SearchExpressionShapes) |
|---------|--------------------------------------------------|
| 笑顔 / Smile | smile, happy, joy, mouth, cheek |
| 怒り / Angry | angry, brow_down, mouth, eye |
| 驚き / Surprised | surprised, eye_wide, mouth_open, brow_up |
| 悲しみ / Sad | sad, brow, mouth_down, eye |
| 泣き / Crying | tear, eye_close, sad, mouth |
| 照れ / Embarrassed | shy, blush, cheek, eye_half |
| ウインク / Wink | wink, eye_close, blink (search _L and _R separately) |
| じと目 / Stare | eye_half, jito, narrow, lid |
| 舌出し / Tongue out | tongue, bero, tung |
| ドヤ顔 / Smug | smug, eye_half, mouth, smile |
| 寝顔 / Sleeping | sleep, eye_close, relax, mouth |
| キス / Kiss | kiss, mouth, lip, chu |

IMPORTANT: These are **search hints only**. Actual BlendShape names vary per avatar model.
ALWAYS use SearchExpressionShapes to find the real names. NEVER hardcode or guess.

## Important Notes
- **Registered max 7**: Exceeding this causes an error. Use Unregistered or groups.
- **Avatar=None**: Use ConfigureTargetAvatar to resolve. Check with FindFaceEmo first.
- **NEVER guess BlendShape names** — always use SearchExpressionShapes with filter.
- **FaceEmo is for facial expressions only**. Object toggles use SetupObjectToggle.
- **Apply after editing**: Run ApplyFaceEmoToAvatar to generate the FX layer after changes.
- **Emotion keywords**: Use the Emotion-to-BlendShape Guide above to translate natural language into search keywords." },

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
| Basic Meshes | 1 | 1 | 2 | 4 |
| Material Slots | 1 | 1 | 2 | 4 |
| PhysBones | 0 | 4 | 6 | 8 |
| PB Transforms | 0 | 16 | 32 | 64 |
| PB Colliders | 0 | 4 | 8 | 16 |
| PB Collision Check | 0 | 16 | 32 | 64 |
| Contacts | 0 | 4 | 8 | 16 |
| Animators | 1 | 1 | 2 | 2 |
| Bones | 75 | 90 | 150 | 150 |
| Particle Systems | 0 | 0 | 0 | 0 |
| Audio Sources | 1 | 1 | 4 | 4 |

## AAO (Avatar Optimizer) Key Components

### Trace and Optimize
The most important component for automatic whole-avatar optimization.
```
1. Select the avatar root
2. [AddComponent('avatarRootName', 'AAOTraceAndOptimize')]
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

### Remove Mesh in Box / By BlendShape
Removes invisible mesh portions to reduce polygon count.
- Used when removing body mesh under clothing
- Removes parts hidden by BlendShapes

## Recommended Optimization Workflow

### 1. Check Current Status
```
[GetAvatarPerformanceStats('avatarRootName')]
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
| `Earmuffs` | Bool | Earmuffs enabled | None |
| `IsOnFriendsList` | Bool | On friends list | None |
| `AvatarVersion` | Int | Avatar version (SDK auto-set) | None |
| `IsAnimatorEnabled` | Bool | Whether Animator is enabled | None |
| `ScaleModified` | Float | Scale modification ratio | IK |
| `ScaleFactor` | Float | Eye height scale | IK |
| `ScaleFactorInverse` | Float | Inverse of ScaleFactor | IK |
| `EyeHeightAsMeters` | Float | Eye height (meters) | IK |
| `EyeHeightAsPercent` | Float | Eye height (0.01-100%) | IK |

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
| 2 | No finger tracking (3pt) |
| 3 | Head and hands (3pt + finger) |
| 4 | 4pt VR |
| 5 | Head + Hands + Feet (5pt) |
| 6 | Full Body (FBT) |

## Sync Types

| Type | Description |
|------|-------------|
| **Speech** | Lip sync/voice. Sent with voice data |
| **Playable** | Used in Playable layer. Animator state sync |
| **IK** | Sent with IK data. Position/tracking related |
| **None** | No sync. Local only |

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
- Write Defaults must be consistent across the entire avatar

## Tool Usage

### Check Parameters
```
[ListVRCExpressionParameters('avatarRootName')]
```

### Add Parameter
```
[AddVRCExpressionParameter('avatarRootName', 'ParamName', 'Bool', 1.0, saved=true, synced=true)]
```

### Remove Parameter
```
[RemoveVRCExpressionParameter('avatarRootName', 'ParamName')]
```

### Add Parameter to FX Controller
```
[AddAnimatorParameter('fxControllerPath', 'ParamName', 'bool', 'true')]
```

### Object Toggle (One-Step Setup)
```
[SetupObjectToggle('avatarRootName', 'objectPath')]
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
[SetupObjectToggle('avatarRootName', 'toggleTargetPath')]
```

Example: Toggle Sailor-Jersey:
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey')]
```

Default OFF (initially hidden):
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey', defaultOn=false)]
```

With a custom name:
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey', toggleName='SailorJersey')]
```

This tool automatically creates:
1. ON/OFF animation clips (Assets/Animations/Toggles/)
2. Layer, states, and transitions in the FX AnimatorController
3. Bool parameter in ExpressionParameters
4. Toggle control in ExpressionsMenu

## Manual Setup (Using Individual Tools)

### Step 1: Identify Target Object Path
```
[ListChildren('avatarRootName')]
```
Find the target GameObject from the avatar's direct children.
Specify the path as a relative path from the avatar root.

### Step 2: Create Toggle Animations
```
[CreateToggleAnimations('avatarRootName', 'relativeObjectPath')]
```
Creates two animation clips: ON (m_IsActive=1) and OFF (m_IsActive=0).

### Step 3: Check FX Controller
```
[GetVRCFXControllerPath('avatarRootName')]
```
Get the FX AnimatorController asset path.

### Step 4: Add Parameter to FX Controller
```
[AddAnimatorParameter('fxControllerPath', 'toggleName', 'bool', 'true')]
```

### Step 5: Add FX Layer
```
[AddAnimatorLayer('fxControllerPath', 'Toggle_toggleName')]
```
```
[SetAnimatorLayerWeight('fxControllerPath', layerIndex, 1.0)]
```

### Step 6: Add States
```
[AddAnimatorState('fxControllerPath', 'ON', 'onClipPath', layerIndex)]
[AddAnimatorState('fxControllerPath', 'OFF', 'offClipPath', layerIndex)]
```

### Step 7: Add Transitions
```
[AddAnimatorTransition('fxControllerPath', 'OFF', 'ON', 'toggleName=true', layerIndex)]
[AddAnimatorTransition('fxControllerPath', 'ON', 'OFF', 'toggleName=false', layerIndex)]
```

### Step 8: Add Expression Parameter
```
[AddVRCExpressionParameter('avatarRootName', 'toggleName', 'Bool', 1.0, saved=true, synced=true)]
```
- Bool parameter, synced to other players
- defaultValue: 1.0=default ON, 0.0=default OFF

### Step 9: Add Expression Menu Toggle
```
[AddVRCExpressionsMenuToggle('avatarRootName', 'toggleName', 'toggleName')]
```

## When Menu is Full (SubMenu Support)

Expression Menu allows a maximum of 8 controls per page. When full, use submenus.

### Creating a SubMenu
```
[AddVRCExpressionsMenuSubMenu('avatarRootName', 'Outfits')]
```
A new VRCExpressionsMenu asset is automatically generated and linked as a SubMenu control.

### Adding Controls to a SubMenu
Use the `subMenuPath` parameter to add within a submenu:
```
[AddVRCExpressionsMenuToggle('avatarRootName', 'Hat', 'Hat', subMenuPath='Outfits')]
[AddVRCExpressionsMenuToggle('avatarRootName', 'Glasses', 'Glasses', subMenuPath='Outfits')]
```

### Nested SubMenus
`subMenuPath` supports slash-separated nesting:
```
[AddVRCExpressionsMenuSubMenu('avatarRootName', 'Details', subMenuPath='Outfits')]
[AddVRCExpressionsMenuToggle('avatarRootName', 'Ring', 'Ring', subMenuPath='Outfits/Details')]
```

## Tool Call Examples

### Example 1: Outfit Toggle (One-Step Setup)
```
User: ""Make Sailor-Jersey toggleable from the Expression Menu""
AI: [SetupObjectToggle('Chiffon', 'Sailor-Jersey')]
    Result: Creates ON/OFF animations, FX layer, parameter, and menu entry in one step
```

### Example 2: Accessory Toggle (Default OFF)
```
User: ""Add glasses as a toggle, hidden by default""
AI: [SetupObjectToggle('Avatar', 'Glasses', defaultOn=false)]
```

### Example 3: SubMenu When Menu is Full
```
User: ""The menu is full but I want to add another toggle""
AI: [InspectVRCExpressionsMenu('Avatar')]
    → Confirm 8 controls
    [AddVRCExpressionsMenuSubMenu('Avatar', 'Accessories')]
    → Create submenu
    [SetupObjectToggle('Avatar', 'NewItem')]
    → If menu is full, manually add to submenu:
    [AddVRCExpressionsMenuToggle('Avatar', 'NewItem', 'NewItem', subMenuPath='Accessories')]
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
[RemoveVRCExpressionsMenuControl('avatarRootName', 'controlName')]
[RemoveVRCExpressionParameter('avatarRootName', 'parameterName')]
```

### FX Layer Rules
- Layer Weight must be set to 1.0
- Transition ExitTime must be disabled
- Transition Duration must be 0
- Write Defaults must be consistent across the entire avatar

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
[AnalyzeGimmickStructure('weaponPrefabName')]
```
- BoneProxy already configured → Just place as child of avatar
- BoneProxy not configured → Manual placement needed

## Step 2: Place Prefab as Child of Avatar
```
[InstantiatePrefab('Assets/.../Weapon.prefab', 'avatarRootName')]
```

## Step 3: Determine Attachment Location and Auto-Align

### Holding in Hand
```
[AlignAccessoryToBone('weaponName', 'avatarRootName', 'RightHand', 'grip')]
```

### Hip/Thigh Attachment (Holster, Sheath)
```
[AlignAccessoryToBone('weaponName', 'avatarRootName', 'RightUpperLeg', 'surface', 'right')]
```

### Back Attachment
```
[AlignAccessoryToBone('weaponName', 'avatarRootName', 'Spine', 'surface', 'back')]
```

## Step 4: Verify
```
[CaptureMultiAngle('weaponName', 'front,left,right,back')]
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
- Pay attention to Write Defaults settings (keep consistent with avatar)
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
[ListBones('MyAvatar', 'Arm')]

2. Create clip:
[CreateAnimationClip('wave', 'Assets/Animations', 2.0, true)]

3. Raise right upper arm (Z-axis rotation to raise arm sideways):
[SetAnimationCurve('Assets/Animations/wave.anim', 'Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R', 'rotation.z', '0:-60, 0.3:-60, 1.7:-60, 2.0:-60')]

4. Wave with forearm (Z-axis oscillation):
[SetAnimationCurve('Assets/Animations/wave.anim', 'Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R/Lower_Arm_R', 'rotation.z', '0:0, 0.3:-30, 0.6:30, 0.9:-30, 1.2:30, 1.5:-30, 1.8:30, 2.0:0')]
```

### Nodding Animation
```
User: ""Create a nodding animation""

1. Check bones → Get Head bone path
2. Create clip:
[CreateAnimationClip('nod', 'Assets/Animations', 0.8, false)]

3. Tilt head forward (X-axis rotation):
[SetAnimationCurve('Assets/Animations/nod.anim', 'Armature/Hips/Spine/Chest/Neck/Head', 'rotation.x', '0:0, 0.2:15, 0.4:0, 0.6:10, 0.8:0')]
```

### Facial Expression Animation (BlendShape)
```
User: ""Create a smile animation""

1. Check BlendShape names:
[ListBlendShapes('Body')]

2. Create clip:
[CreateAnimationClip('smile', 'Assets/Animations', 0.5, false)]

3. Set smile BlendShape:
[SetAnimationCurve('Assets/Animations/smile.anim', 'Body', 'blendShape.face_smile', '0:0, 0.3:100, 0.5:100')]
```

### Object Toggle (Show/Hide)
```
[CreateAnimationClip('hat_on', 'Assets/Animations', 0.0, false)]
[SetAnimationCurve('Assets/Animations/hat_on.anim', 'Hat', 'active', '0:1')]

[CreateAnimationClip('hat_off', 'Assets/Animations', 0.0, false)]
[SetAnimationCurve('Assets/Animations/hat_off.anim', 'Hat', 'active', '0:0')]
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
- Rotations exceeding 360° may cause gimbal lock issues
- When using in VRChat's FX layer, pay attention to Write Defaults settings
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
[ListChildren('avatarRootName')]
```
Check the avatar's direct children to identify outfit-related objects.
Outfit objects are typically mesh objects or outfit prefabs other than the Armature.

### Step 2: Remove Existing Outfit
Hide each existing outfit object and set its tag to EditorOnly.
**Hiding alone is not enough**. Data remains when uploading, so the EditorOnly tag is also needed.

```
[SetActive('avatarRootName/outfitObjectName', false)]
[SetTag('avatarRootName/outfitObjectName', 'EditorOnly')]
```

Execute for all outfit parts if there are multiple.
**Note**: Do not remove the Armature, Body (base mesh), or the root with VRCAvatarDescriptor.

### Step 2.5: Reset Existing Shrink BlendShapes
Reset Shrink BlendShapes from the previous outfit to 0.
If shrinks remain, the base body will appear thin.

1. Check shrink-related BlendShapes on the Body mesh:
```
[ListBlendShapesEx('avatarRootName/Body', 'shrink')]
```

2. Reset all non-zero shrinks to 0:
```
[SetMultipleBlendShapes('avatarRootName/Body', 'Shrink_XXX=0;Shrink_YYY=0')]
```

### Step 3: Search for Compatible Outfit Prefab
```
[SearchAssets('outfitName', 'Prefab')]
```
Select a prefab that contains the avatar name (e.g., `Chiffon_RetroKimono.prefab`).
Compatible outfits typically include the avatar name in the prefab name.

### Step 4: Place Outfit Prefab as Child of Avatar
```
[InstantiatePrefab('Assets/path/to/outfit.prefab', 'avatarRootName')]
```
**Important**: Always specify the avatar root name as the 2nd argument `parentName`.
Placing at scene root instead of as avatar child will prevent Setup Outfit from working.

### Step 5: Run Modular Avatar Setup Outfit
```
[SetupOutfit('avatarRootName', 'outfitObjectName')]
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
[ListBlendShapesEx('avatarRootName/Body', 'shrink')]
```

2. Set shrinks for body parts covered by the outfit to 100:
```
[SetMultipleBlendShapes('avatarRootName/Body', 'Shrink_XXX=100;Shrink_YYY=100')]
```

Note: Shrink names correspond to body parts or outfit areas.
Apply shrinks for parts covered by the outfit.
It's preferable to ask the user which shrinks to apply.

### Step 6: Verify
```
[InspectGameObject('avatarRootName/outfitObjectName')]
```
Verify that ModularAvatarMergeArmature and other components have been added.

## Adding Outfit Toggles
To add Expression Menu toggles for outfit parts:
```
[SetupObjectToggle('avatarRootName', 'outfitPartPath')]
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
[AnalyzeOutfitCompatibility('outfitObjectName', 'avatarRootName')]
```
Check bone structure comparison, proportion differences, and compatibility score.

### Step 2: Bone Mapping Verification
```
[MapOutfitBones('outfitObjectName', 'avatarRootName')]
```
Review the auto-generated bone mapping table.
Report any low-confidence mappings to the user.

### Step 3: Execute Retarget
```
[RetargetOutfit('outfitObjectName', 'avatarRootName', 1.0)]
```
- Remaps bones to avatar side
- Recalculates bind poses
- `adaptStrength=1.0` deforms mesh to conform to body surface (surface conforming)
- Auto-detects body mesh and pushes penetrating vertices to body surface + margin
- Performs accurate mesh-space correction via inverse skinning
- New mesh assets are saved to `Generated/OutfitFitting/`

### Step 4: Penetration Check
```
[DetectMeshPenetration('outfitMeshName', 'Body')]
```
Detects penetration between outfit mesh and body. Skip Step 5 if no penetration.

### Step 5: Fix Penetration (If Needed)
```
[FixMeshPenetration('outfitMeshName', 'Body', 0.001)]
```
Pushes penetrating and too-close vertices outward along normals.
Performs accurate mesh-space correction via inverse skinning, considering bone rotation.

### Step 6: Apply Existing Shrink BlendShapes (If Needed)
Apply the avatar's existing Shrink BlendShapes.
**Note**: The avatar's mesh is not modified. Only existing BlendShapes are activated.
```
[ListBlendShapesEx('avatarRootName/Body', 'shrink')]
[SetMultipleBlendShapes('avatarRootName/Body', 'Shrink_XXX=100;Shrink_YYY=100')]
```

### Step 7: Finish with MA Setup Outfit
Move the retargeted outfit as a child of the avatar and finish with the same procedure as compatible outfits.
```
[SetupOutfit('avatarRootName', 'outfitObjectName')]
```

### Step 8: Weight Transfer (If Needed)
If joint deformation looks unnatural, transfer weights from the avatar body mesh.
```
[TransferOutfitWeights('outfitMeshName', 'Body')]
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
1. Check material: `[InspectLilToonMaterial('Assets/.../material.mat')]`
2. Enable emission: `[SetLilToonFloat('Assets/.../material.mat', '_UseEmission', 1)]`
3. Generate + apply texture: `[GenerateTextureWithAI('AvatarRoot/Hair', 'seamless shooting stars sparkle pattern on transparent background', '', 0, '_EmissionMap')]`
   - **Important**: _EmissionMap can be empty (auto-generates from transparent canvas)
4. Set color: `[SetLilToonColors('Assets/.../material.mat', 'Emission', '1,1,1,1')]`
5. Scroll: `[SetMaterialVector('Assets/.../material.mat', '_EmissionMap_ScrollRotate', '0,2,0,0')]`
   - Scrolls upward at speed 2

### Adding Blink
`[SetMaterialVector('Assets/.../material.mat', '_EmissionBlink', '0.5,0,3.14,0')]`
- 50% strength smooth pulsing

## Pattern 2: Main2nd Overlay Scroll

1. `[SetLilToonFloat('Assets/.../material.mat', '_UseMain2ndTex', 1)]`
2. `[GenerateTextureWithAI('AvatarRoot/Hair', 'seamless sparkle overlay pattern', '', 0, '_Main2ndTex')]`
3. `[SetMaterialVector('Assets/.../material.mat', '_Main2ndTex_ScrollRotate', '1,0.5,0,0')]`
4. Blend mode: `[SetMaterialFloat('Assets/.../material.mat', '_Main2ndTexBlendMode', 1)]` (1=Add)

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
1. ListRenderers(""avatarName"") → Confirm Body Material[0]: body contains eyes
2. ListMeshIslands(""avatarName/Body"") → Island list
3. EnableIslandSelectionMode(""avatarName/Body"")
4. ""Please click on the eye area in the Scene view""
5. GetSelectedIslands() → ""5;6""
6. GenerateTextureWithAI(""avatarName/Body"", ""Transform the eye iris area into a cosmic galaxy nebula with deep blue, purple, and sparkles"", ""5;6"", 0, ""_MainTex"")
```

### Example 2: Make Eyes Glow with Emission
```
AI:
1. (Same island identification as above)
2. GenerateTextureWithAI(""avatarName/Body"", ""Create a glowing nebula emission pattern"", ""5;6"", 0, ""_EmissionMap"")
```

### Example 3: Hair Gradient
```
AI:
1. ListMeshIslands(""avatarName/hair"")
2. EnableIslandSelectionMode(""avatarName/hair"")
3. User selects hair tip islands
4. GetSelectedIslands() → ""0;1;2""
5. ApplyGradientEx(""avatarName/hair"", ""1.0,0.8,0.9"", ""0.5,0.2,0.8"", ""top_to_bottom"", ""screen"", ""0;1;2"")
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
- Recolor: ApplyGradientEx(""go"", ""#FF0000"", ""#FF0000"", blendMode=""tint"")
- Lighten: ApplyGradientEx(""go"", ""#FFFFFF"", ""#FFFFFF"", blendMode=""screen"")
- Gradient: ApplyGradientEx(""go"", ""#FF0000"", ""#0000FF"")
- Brighten: AdjustHSV(""go"", 0, 1, 1.5)
- Darken: AdjustHSV(""go"", 0, 1, 0.5)
- Desaturate: AdjustHSV(""go"", 0, 0, 1)" },

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
[AnalyzeGimmickStructure('prefabName')]
```
→ If child objects with BoneProxy are found, just place as child of avatar.

### Step 2: Measure Avatar
Get the target avatar's dimensions (used for auto scale calculation):
```
[MeasureAvatarBody('avatarRootName')]
```

### Step 3: Place Prefab in Scene
Instantiate **as a child of the avatar**:
```
[InstantiatePrefab('Assets/.../Item.prefab', 'avatarRootName')]
```

### Step 4: Ask User About Attachment Location
```
[AskUser('Where should it be attached?', 'Hold in right hand', 'Hold in left hand', 'Attach to hip', 'Attach to thigh')]
```

### Step 5: Auto-Alignment (Most Important)
**NEVER use SetTransform for manual coordinate guessing.**
Attach with the appropriate style based on user selection:

#### Holding in Hand (grip)
```
[AlignAccessoryToBone('itemName', 'avatarRootName', 'RightHand', 'grip')]
```

#### Attached to Body (surface) — Hip, Thigh, Back, etc.
```
[AlignAccessoryToBone('itemName', 'avatarRootName', 'RightUpperLeg', 'surface', 'right')]
```
direction parameter specifies attachment direction:
- Outer thigh: direction='right' (right leg) / 'left' (left leg)
- Front of hip: direction='front'
- Back of hip: direction='back'
- Back: direction='back'

#### Wrapping Around (wrap) — Bracelets, Cuffs, etc.
```
[AlignAccessoryToBone('itemName', 'avatarRootName', 'LeftLowerArm', 'wrap')]
```

### Step 6: Verify Result
Capture from SceneView to verify:
```
[CaptureMultiAngle('itemName', 'front,left,right,back')]
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
[AttachRingWithBoneProxy('ringName', 'RightRingProximal')]
[AlignRingToBone('ringName')]
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
[ValidateAvatar('avatarRootName')]
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
[GetAvatarPerformanceStats('avatarRootName')]
```
→ Returns VRChat performance rank for all categories.
Identify bottleneck categories and suggest improvements.

## Step 3: Individual Diagnosis (As Needed)

### Write Defaults Issues
```
[CheckWriteDefaults('avatarRootName')]
```
→ WD state per layer (ON/OFF/MIXED).
MIXED is problematic → needs unification.

Fix method: See ReadSkill('avatar-optimization').

### Parameter Budget
```
[CheckParameterBudget('avatarRootName')]
```
→ Sync parameter bit consumption. Exceeding 256 bits is an error.

Solutions:
- Remove unnecessary parameters: [RemoveVRCExpressionParameter('avatarRootName', 'parameterName')]
- Change to synced=false (local only)
- Change Int→Bool (save 8bit→1bit)

### PhysBone Issues
```
[ListVRCPhysBones('avatarRootName')]
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
[ListVRCPhysBones('avatarRootName')]
```
→ Check already configured PhysBones. Avoid duplicate additions.

## Step 2: Identify Bone Chain

Identify the parent of the bones you want to make dynamic:
```
[GetHierarchyTree('avatarRootName/Armature', maxDepth=5)]
```

PhysBone should be added to the **root of the chain**.
Example: Adding PhysBone to Hair_front_Root → All children (Hair_front_0, Hair_front_1...) will be dynamic.

## Step 3: Apply Template (Recommended)

Apply a template based on the bone type:
```
[AddPhysBone('boneName')]
[ApplyVRCPhysBoneTemplate('boneName', 'templateName')]
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
[ConfigureVRCPhysBone('boneName', pull=0.3, gravity=0.2)]
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
[AddPhysBoneCollider('Head', 1, 0.08, 0.15)]
[LinkColliderToPhysBone('Hair_Root', 'Head')]
```

### Common Collider Placements

| Location | Shape | Purpose |
|----------|-------|---------|
| Head | Capsule | Prevent hair from penetrating the head |
| Chest | Capsule | Prevent long hair from penetrating the body |
| UpperLeg | Capsule | Prevent skirt from penetrating the legs |

## Step 6: Performance Check

```
[GetAvatarPerformanceStats('avatarRootName')]
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
- Use exclusions to exclude unwanted children: [SetPhysBoneExclusions('boneName', 'exclude1,exclude2')]
- Specify endpoint position: [SetPhysBoneEndpoint('boneName', '0,0.1,0')]
- Performance rank thresholds: See ReadSkill('avatar-optimization')" },

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
[BatchConfigureShadows('avatarRootName', 1, 1)]
```
→ Bulk change shadow settings for all Renderers.

### Get Renderer List → Configure Individually
```
[ListRenderers('avatarRootName')]
```
→ Returns paths of all Renderers. For each Renderer:
```
[SetProperty('path', 'SkinnedMeshRenderer', 'probeAnchor', 'referenceTarget')]
```

## Pattern 2: Bulk Material Changes

### Step 1: Get Material List
```
[ListRenderers('avatarRootName')]
```
Check material slots for each Renderer.

### Step 2: Check Material Properties
```
[InspectMaterial('Assets/.../Material.mat')]
```

### Step 3: Bulk Apply
When applying the same change to multiple materials, make consecutive tool calls:
```
[SetMaterialFloat('Assets/.../Mat1.mat', '_Metallic', 0.8)]
[SetMaterialFloat('Assets/.../Mat2.mat', '_Metallic', 0.8)]
[SetMaterialFloat('Assets/.../Mat3.mat', '_Metallic', 0.8)]
```

### lilToon Bulk Settings Example
```
[SetMaterialFloat('Assets/.../Mat.mat', '_OutlineWidth', 0.1)]
[SetMaterialFloat('Assets/.../Mat.mat', '_OutlineFixWidth', 1)]
```

## Pattern 3: Bulk BlendShape Settings

### Set Multiple BlendShapes at Once
```
[SetMultipleBlendShapes('Body', 'Shrink_UpperBody=100;Shrink_LowerBody=100;Shrink_Arm=100')]
```

### Check All BlendShapes
```
[ListBlendShapes('Body')]
```

## Pattern 4: Bulk Component Operations

### Search for Specific Components in Avatar
```
[GetHierarchyTree('avatarRootName', maxDepth=5)]
```
→ Identify targets from tree, then operate on each object.

### Bulk PhysBone Check & Configure
```
[ListVRCPhysBones('avatarRootName')]
```
→ Full PhysBone list. Configure individually:
```
[ConfigureVRCPhysBone('Hair_Root', pull=0.2, spring=0.3)]
[ConfigureVRCPhysBone('Skirt_Root', pull=0.3, gravity=0.3)]
```

## Pattern 5: Bulk Object ON/OFF

### Hide Multiple Objects
```
[SetActive('avatarRootName/Outfit_Old/Top', false)]
[SetActive('avatarRootName/Outfit_Old/Bottom', false)]
[SetActive('avatarRootName/Outfit_Old/Shoes', false)]
```

### Set EditorOnly Tag (Exclude from Upload)
```
[SetTag('avatarRootName/Outfit_Old', 'EditorOnly')]
```

## Pattern 6: Bulk Hierarchy Rename

Identify targets and rename individually:
```
[ListChildren('parentObjectName')]
[RenameGameObject('oldName', 'newName')]
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
[ScanAvatarMeshes(""AvatarRoot"")]
```
→ Receive a grid image with each mesh isolated
→ Visually determine which mesh is hair, body, clothes, etc.

### Step 3: Identify Target Mesh
- Match user's request (e.g., ""hair color"") with grid image
- Ambiguous → AskUser(""Which mesh?"", mesh1, mesh2)

### Step 4: Material Details (for color changes)
```
[ListRenderers(""targetMeshPath"")]
```
→ Get material name/path
```
[InspectLilToonMaterial(""materialPath"")]
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
[ScanAvatarMeshes(""MANUKA"")]

System: ""Scanned 6 meshes. [1] Manuka_atama — 12k verts...
        [2] Manuka_hair_front — 8k verts... ...""
        + image (isolated grid of each mesh)

Agent: From the image, [2] Manuka_hair_front and [3] Manuka_hair_bun
       are the hair meshes. Let me read the color change procedure.
[ReadSkill(""texture-editing"")]
```
(Continue with texture-editing skill workflow → CaptureSceneView → AskUser)" },
        };
    }
}
