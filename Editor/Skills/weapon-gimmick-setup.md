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
- Pay attention to Write Defaults settings (keep consistent with avatar)
- Watch Expression Parameter budget (256 bits)
- VRC Constraint recommended: Lighter than Unity Constraint, optimized for VRChat runtime
