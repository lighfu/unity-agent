---
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
[AddMAMergeArmature('outfitName/Armature', 'avatarRootName/Armature', '', '')]
```
- goName = the outfit's Armature object; mergeTargetName = the avatar's Armature (root bone).
- If bone names don't match, pass prefix/suffix (e.g. suffix '.1' for bones named 'Hips.1').
- Bones merge automatically at build time.

### MA Merge Animator
Integrates an Animator Controller into a playable layer (non-destructive).
```
[AddMAMergeAnimator('gimmickHolder', 'Assets/Anim/gimmick.controller', 'FX', 0, true)]
```
- layerType: FX (default), Gesture, Action, Base, Additive, Sitting, TPose, IKPose.
- pathMode: 0=Relative (MA default) / 1=Absolute. matchWriteDefaults=true keeps WD consistent (see Notes).

### MA Menu Item / MA Parameters
Non-destructively adds Expression Menu and Parameters.
- Single entry on an existing object (iconPath optional):
  ```
  [AddMenuItem('Toggle_Hat', 'Toggle', 'Hat', 1, true, true, false, 'Assets/Icons/hat.png')]
  ```
- Nested submenu (container + children; nest deeper via a SubMenu entry):
  ```
  [CreateMAMenu('avatarRootName', 'Outfits')]
  [AddMAMenuItemUnder('Outfits', 'Dress', 'Toggle', 'Dress')]
  [AddMAMenuItemUnder('Outfits', 'Colors', 'SubMenu')]
  [AddMAMenuItemUnder('Colors', 'Red', 'Toggle', 'ColorRed')]
  ```

### MA Bone Proxy
Non-destructively places objects as children of specific bones.
Used for making weapons or accessories follow the hand or Head.
```
[AddMABoneProxy('weaponName', 'RightHand', 1)]
```
- mode 1=AsChildAtRoot (snaps to the bone). To preserve the object's current world placement, use mode 2 (or run AlignAccessoryToBone first).
- For ring/finger accessories, AttachRingWithBoneProxy is a convenience wrapper.

## General Outfit Setup Procedure

1. Place the outfit Prefab as a child of the avatar:
   ```
   [SetParent('outfitName', 'avatarRootName')]
   ```

2. Verify MA Merge Armature is configured on the outfit's Armature:
   ```
   [InspectGameObject('avatarRootName/outfitName/Armature')]
   ```
   If it's missing, add it:
   ```
   [AddMAMergeArmature('avatarRootName/outfitName/Armature', 'avatarRootName/Armature', '', '')]
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
- MA note: Merge Animator's "Match Avatar Write Defaults" (default ON since 1.16.1) only matches the avatar's existing WD — it will NOT fix an already-mixed avatar. Only VRCFury enforces a single WD value avatar-wide.
- If you choose all-OFF: every state needs a clip/blend tree, and any layer animating Transforms needs an Avatar Mask.
- Bones won't merge if names don't match → Use MA Merge Armature settings to resolve
- For Quest builds, watch parameter count from MA-generated animator layers
