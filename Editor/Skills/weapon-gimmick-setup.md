---
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
<tool name="AnalyzeGimmickStructure">
<arg name="gameObjectName">weaponPrefabName</arg>
</tool>
```
- BoneProxy already configured → Just place as child of avatar
- BoneProxy not configured → Manual placement needed

## Step 2: Place Prefab as Child of Avatar
```
<tool name="InstantiatePrefab">
<arg name="assetPath">Assets/.../Weapon.prefab</arg>
<arg name="parentName">avatarRootName</arg>
</tool>
```

## Step 3: Determine Attachment Location and Auto-Align

### Holding in Hand
```
<tool name="AlignAccessoryToBone">
<arg name="accessoryName">weaponName</arg>
<arg name="avatarRootName">avatarRootName</arg>
<arg name="boneName">RightHand</arg>
<arg name="attachmentStyle">grip</arg>
</tool>
```

### Hip/Thigh Attachment (Holster, Sheath)
```
<tool name="AlignAccessoryToBone">
<arg name="accessoryName">weaponName</arg>
<arg name="avatarRootName">avatarRootName</arg>
<arg name="boneName">RightUpperLeg</arg>
<arg name="attachmentStyle">surface</arg>
<arg name="direction">right</arg>
</tool>
```

### Back Attachment
```
<tool name="AlignAccessoryToBone">
<arg name="accessoryName">weaponName</arg>
<arg name="avatarRootName">avatarRootName</arg>
<arg name="boneName">Spine</arg>
<arg name="attachmentStyle">surface</arg>
<arg name="direction">back</arg>
</tool>
```

## Step 4: Verify
```
<tool name="CaptureMultiAngle">
<arg name="targetName">weaponName</arg>
<arg name="angles">front,left,right,back</arg>
</tool>
```

## Step 5: Fine Adjustment
Leave fine adjustments after auto-placement to the user:
"If fine adjustment is needed, use the Scene view gizmos or Transform panel for direct manipulation."

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
- Match the avatar's Write Defaults: keep the WHOLE avatar (all Playable Layers count as one controller) either all-ON or all-OFF — never mixed (mixing makes properties "stick" and breaks expressions; the SDK only warns).
- Exception: additive layers and Direct Blend Tree single-state layers must always be WD ON regardless of the avatar's setting (WD OFF makes their values blow up).
- If the avatar is all-OFF: every state of your weapon layers needs a clip/blend tree, and animating Transforms requires an Avatar Mask.
- Watch Expression Parameter budget (256 bits)
- VRC Constraint recommended: Lighter than Unity Constraint, optimized for VRChat runtime
