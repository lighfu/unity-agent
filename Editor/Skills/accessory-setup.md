---
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
- "If fine adjustment is needed, use the Scene view gizmos (W/E/R keys) or the Transform panel in the UnityAgent window for direct manipulation."
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
- Guide users to use Scene view gizmos or Transform panel for post-placement fine adjustment
