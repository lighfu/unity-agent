---
title: FaceEmo Expression Menu Setup (Complete Guide)
description: Build, edit, and configure gesture-based expression menus using FaceEmo
tags: FaceEmo, expression, Expression Menu, gesture, non-destructive
---

# FaceEmo Expression Menu Setup (Complete Guide)

## Overview
FaceEmo is an expression menu configuration tool for VRChat Avatars 3.0.
It allows non-destructive setup of expression animations linked to hand gestures.
Package: `jp.suzuryg.face-emo`

## Basic Tools (Always Available — Reflection Version)
```
<tool name="FindFaceEmo"></tool> — Discover all FaceEmo objects in the scene
<tool name="InspectFaceEmo">
<arg name="gameObjectName">FaceEmo</arg>
</tool> — Show detailed AV3 settings, expression modes, gestures
<tool name="ListFaceEmoExpressions">
<arg name="gameObjectName">FaceEmo</arg>
</tool> — List all expression modes and branches
<tool name="LaunchFaceEmoWindow">
<arg name="gameObjectName">FaceEmo</arg>
</tool> — Launch the FaceEmo editor window
<tool name="ReadFaceEmoProperty">
<arg name="gameObjectName">FaceEmo</arg>
<arg name="propertyPath">AV3Setting.TargetAvatar</arg>
</tool> — Read a property
<tool name="WriteFaceEmoProperty">
<arg name="gameObjectName">FaceEmo</arg>
<arg name="propertyPath">AV3Setting.SmoothAnalogFist</arg>
<arg name="value">false</arg>
</tool> — Write a property
<tool name="ListFaceEmoProperties">
<arg name="gameObjectName">FaceEmo</arg>
<arg name="subObject">AV3Setting</arg>
</tool> — List properties
```

## Expression Management Tools (FaceEmo Advanced)
```
<tool name="AddExpression">
<arg name="displayName">Angry</arg>
<arg name="destination">Registered</arg>
<arg name="animationClipPath">Assets/Animations/angry.anim</arg>
</tool> — Add a new expression
<tool name="RemoveExpression">
<arg name="displayName">Angry</arg>
</tool> — Remove an expression (with confirmation)
<tool name="CopyExpression">
<arg name="sourceExpressionName">Smile</arg>
<arg name="newDisplayName">Smile2</arg>
<arg name="destination">Registered</arg>
</tool> — Duplicate and rename an expression
<tool name="SetExpressionAnimation">
<arg name="expressionName">Angry</arg>
<arg name="animationClipPath">Assets/Animations/angry.anim</arg>
</tool> — Set animation
<tool name="ModifyExpressionProperties">
<arg name="expressionName">Angry</arg>
<arg name="newDisplayName">Angry_v2</arg>
</tool> — Modify expression properties
<tool name="SetDefaultExpression">
<arg name="expressionName">Smile</arg>
</tool> — Set default expression
<tool name="InspectExpressionDetail">
<arg name="expressionName">Angry</arg>
</tool> — Detailed info (branches, conditions, animations)
<tool name="CreateAndRegisterExpression">
<arg name="meshObjectName">Body</arg>
<arg name="expressionName">Angry</arg>
<arg name="animPath">Assets/Animations/angry.anim</arg>
</tool> — Create expression from BlendShape + register (one step)
```

## Gesture Branch Management
```
<tool name="AddGestureBranch">
<arg name="expressionName">Angry</arg>
<arg name="conditions">Left=Fist</arg>
<arg name="baseAnimationPath">Assets/Animations/angry.anim</arg>
</tool> — Add gesture branch
  Condition format: 'Left=Fist;Right=Victory' or 'Either!=Neutral'
  Hand: Left/Right/Either/Both/OneSide
  Gesture: Neutral/Fist/HandOpen/Fingerpoint/Victory/RockNRoll/HandGun/ThumbsUp
<tool name="RemoveGestureBranch">
<arg name="expressionName">Angry</arg>
<arg name="branchIndex">0</arg>
</tool> — Remove branch (with confirmation)
<tool name="AddGestureCondition">
<arg name="expressionName">Angry</arg>
<arg name="branchIndex">0</arg>
<arg name="hand">Right</arg>
<arg name="gesture">Fist</arg>
</tool> — Add condition to existing branch
<tool name="ModifyBranchProperties">
<arg name="expressionName">Angry</arg>
<arg name="branchIndex">0</arg>
<arg name="eyeTracking">Animation</arg>
</tool> — Modify branch properties
```

## Menu Structure Management
```
<tool name="CreateExpressionGroup">
<arg name="displayName">Combat</arg>
<arg name="destination">Registered</arg>
</tool> — Create submenu group
<tool name="MoveExpressionItem">
<arg name="itemName">Angry</arg>
<arg name="destination">Unregistered</arg>
</tool> — Move/reorder expressions or groups
```

## AV3 Settings
```
<tool name="ConfigureTargetAvatar">
<arg name="avatarName">Chiffon</arg>
</tool> — Set target avatar (resolves Avatar=None)
<tool name="ConfigureFaceEmoGeneration"></tool> — View/change generation settings
<tool name="ConfigureMouthMorphs">
<arg name="action">list</arg>
</tool> — Configure mouth morph BlendShapes
<tool name="ConfigureAfkFace"></tool> — Configure AFK expression
<tool name="ConfigureFeatureToggles"></tool> — Configure feature toggles (emote selection, contact lock, etc.)
```

## Hand Gesture Reference
| ID | Gesture | Operation |
|----|---------|-----------|
| 0 | Neutral | No input |
| 1 | Fist | Full trigger press |
| 2 | HandOpen | All fingers open |
| 3 | FingerPoint | Index finger only |
| 4 | Victory | Index + middle finger |
| 5 | RockNRoll | Pinky + index finger |
| 6 | HandGun | Thumb + index finger |
| 7 | ThumbsUp | Thumb only |

## Workflow Examples

### New Setup
```
1. <tool name="FindFaceEmo"></tool> → Check FaceEmo status
2. If no FaceEmo exists:
<tool name="ExecuteMenu">
<arg name="menuPath">FaceEmo/New Menu</arg>
</tool>
3. If Avatar=None:
<tool name="ConfigureTargetAvatar">
<arg name="avatarName">Chiffon</arg>
</tool>
4. <tool name="LaunchFaceEmoWindow">
<arg name="gameObjectName">FaceEmo</arg>
</tool> → Open the window
```

### Adding an Expression (with Gesture)
```
1. <tool name="AddExpression">
<arg name="displayName">Angry</arg>
<arg name="destination">Registered</arg>
<arg name="animationClipPath">Assets/Animations/angry.anim</arg>
</tool>
2. <tool name="AddGestureBranch">
<arg name="expressionName">Angry</arg>
<arg name="conditions">Left=Fist</arg>
<arg name="baseAnimationPath">Assets/Animations/angry.anim</arg>
</tool>
3. <tool name="InspectExpressionDetail">
<arg name="expressionName">Angry</arg>
</tool> → Verify
```

### Organizing the Expression Menu
```
1. <tool name="ListFaceEmoExpressions"></tool> → List all expressions
2. <tool name="CreateExpressionGroup">
<arg name="displayName">Combat</arg>
<arg name="destination">Registered</arg>
</tool> → Create group
3. <tool name="MoveExpressionItem">
<arg name="itemName">Angry</arg>
<arg name="destination">Combat</arg>
</tool> → Move into group
```

### Duplicating / Creating Variants
```
1. <tool name="CopyExpression">
<arg name="sourceExpressionName">Smile</arg>
<arg name="newDisplayName">BigSmile</arg>
<arg name="destination">Registered</arg>
</tool> → Duplicate
2. <tool name="SetExpressionAnimation">
<arg name="expressionName">BigSmile</arg>
<arg name="animationClipPath">Assets/Animations/smile_strong.anim</arg>
</tool> → Change animation
```

### Bulk Configuration
```
<tool name="ConfigureFaceEmoGeneration">
<arg name="transitionDuration">0.05</arg>
<arg name="smoothAnalogFist">false</arg>
</tool>
<tool name="ConfigureFeatureToggles">
<arg name="contactLock">true</arg>
<arg name="danceGimmick">true</arg>
</tool>
```

## Important Notes
- Avatar=None issue: Resolve with ConfigureTargetAvatar. If FindFaceEmo shows None, run this first
- Maximum 6 Registered items shown in the Expression Menu (a folder/group counts as 1 slot and can hold up to 8 inside; Unregistered/Archive is unlimited): use a folder or Unregistered if exceeding this limit
- FaceEmo works with NDMF/Modular Avatar to non-destructively generate FX layers
- IMPORTANT: FaceEmo is exclusively for facial expressions (face BlendShapes). Use SetupObjectToggle for object toggles
