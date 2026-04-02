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
[FindFaceEmo()] — Discover all FaceEmo objects in the scene
[InspectFaceEmo('FaceEmo')] — Show detailed AV3 settings, expression modes, gestures
[ListFaceEmoExpressions('FaceEmo')] — List all expression modes and branches
[LaunchFaceEmoWindow('FaceEmo')] — Launch the FaceEmo editor window
[ReadFaceEmoProperty('FaceEmo', 'AV3Setting.TargetAvatar')] — Read a property
[WriteFaceEmoProperty('FaceEmo', 'AV3Setting.SmoothAnalogFist', 'false')] — Write a property
[ListFaceEmoProperties('FaceEmo', 'AV3Setting')] — List properties
```

## Expression Management Tools (FaceEmo Advanced)
```
[AddExpression('Angry', 'Registered', 'Assets/Animations/angry.anim')] — Add a new expression
[RemoveExpression('Angry')] — Remove an expression (with confirmation)
[CopyExpression('Smile', 'Smile2', 'Registered')] — Duplicate and rename an expression
[SetExpressionAnimation('Angry', 'Assets/Animations/angry.anim')] — Set animation
[ModifyExpressionProperties('Angry', newDisplayName='Angry_v2')] — Modify expression properties
[SetDefaultExpression('Smile')] — Set default expression
[InspectExpressionDetail('Angry')] — Detailed info (branches, conditions, animations)
[CreateAndRegisterExpression('Body', 'Angry', 'Assets/Animations/angry.anim')] — Create expression from BlendShape + register (one step)
```

## Gesture Branch Management
```
[AddGestureBranch('Angry', 'Left=Fist', 'Assets/Animations/angry.anim')] — Add gesture branch
  Condition format: 'Left=Fist;Right=Victory' or 'Either!=Neutral'
  Hand: Left/Right/Either/Both/OneSide
  Gesture: Neutral/Fist/HandOpen/Fingerpoint/Victory/RockNRoll/HandGun/ThumbsUp
[RemoveGestureBranch('Angry', 0)] — Remove branch (with confirmation)
[AddGestureCondition('Angry', 0, 'Right', 'Fist')] — Add condition to existing branch
[ModifyBranchProperties('Angry', 0, eyeTracking='Animation')] — Modify branch properties
```

## Menu Structure Management
```
[CreateExpressionGroup('Combat', 'Registered')] — Create submenu group
[MoveExpressionItem('Angry', 'Unregistered')] — Move/reorder expressions or groups
```

## AV3 Settings
```
[ConfigureTargetAvatar('Chiffon')] — Set target avatar (resolves Avatar=None)
[ConfigureFaceEmoGeneration()] — View/change generation settings
[ConfigureMouthMorphs('list')] — Configure mouth morph BlendShapes
[ConfigureAfkFace()] — Configure AFK expression
[ConfigureFeatureToggles()] — Configure feature toggles (emote selection, contact lock, etc.)
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
1. [FindFaceEmo()] → Check FaceEmo status
2. If no FaceEmo exists: [ExecuteMenu('FaceEmo/New Menu')]
3. If Avatar=None: [ConfigureTargetAvatar('Chiffon')]
4. [LaunchFaceEmoWindow('FaceEmo')] → Open the window
```

### Adding an Expression (with Gesture)
```
1. [AddExpression('Angry', 'Registered', 'Assets/Animations/angry.anim')]
2. [AddGestureBranch('Angry', 'Left=Fist', 'Assets/Animations/angry.anim')]
3. [InspectExpressionDetail('Angry')] → Verify
```

### Organizing the Expression Menu
```
1. [ListFaceEmoExpressions()] → List all expressions
2. [CreateExpressionGroup('Combat', 'Registered')] → Create group
3. [MoveExpressionItem('Angry', 'Combat')] → Move into group
```

### Duplicating / Creating Variants
```
1. [CopyExpression('Smile', 'BigSmile', 'Registered')] → Duplicate
2. [SetExpressionAnimation('BigSmile', 'Assets/Animations/smile_strong.anim')] → Change animation
```

### Bulk Configuration
```
[ConfigureFaceEmoGeneration(transitionDuration='0.05', smoothAnalogFist='false')]
[ConfigureFeatureToggles(contactLock='true', danceGimmick='true')]
```

## Important Notes
- Avatar=None issue: Resolve with ConfigureTargetAvatar. If FindFaceEmo shows None, run this first
- Maximum 7 Registered items: Use Unregistered or groups if exceeding this limit
- FaceEmo works with NDMF/Modular Avatar to non-destructively generate FX layers
- IMPORTANT: FaceEmo is exclusively for facial expressions (face BlendShapes). Use SetupObjectToggle for object toggles
