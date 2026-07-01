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
<tool name="AddMAMergeArmature">
<arg name="goName">outfitName/Armature</arg>
<arg name="mergeTargetName">avatarRootName/Armature</arg>
<arg name="prefix"></arg>
<arg name="suffix"></arg>
</tool>
```
- goName = the outfit's Armature object; mergeTargetName = the avatar's Armature (root bone).
- If bone names don't match, pass prefix/suffix (e.g. suffix '.1' for bones named 'Hips.1').
- Bones merge automatically at build time.

### MA Merge Animator
Integrates an Animator Controller into a playable layer (non-destructive).
```
<tool name="AddMAMergeAnimator">
<arg name="goName">gimmickHolder</arg>
<arg name="controllerPath">Assets/Anim/gimmick.controller</arg>
<arg name="layerType">FX</arg>
<arg name="pathMode">0</arg>
<arg name="matchWriteDefaults">true</arg>
</tool>
```
- layerType: FX (default), Gesture, Action, Base, Additive, Sitting, TPose, IKPose.
- pathMode: 0=Relative (MA default) / 1=Absolute. matchWriteDefaults=true keeps WD consistent (see Notes).

### MA Menu Item / MA Parameters
Non-destructively adds Expression Menu and Parameters.
- Single entry on an existing object (iconPath optional):
  ```
  <tool name="AddMenuItem">
  <arg name="goName">Toggle_Hat</arg>
  <arg name="type">Toggle</arg>
  <arg name="paramName">Hat</arg>
  <arg name="value">1</arg>
  <arg name="synced">true</arg>
  <arg name="saved">true</arg>
  <arg name="isDefault">false</arg>
  <arg name="iconPath">Assets/Icons/hat.png</arg>
  </tool>
  ```
- Nested submenu (container + children; nest deeper via a SubMenu entry):
  ```
  <tool name="CreateMAMenu">
  <arg name="avatarRootName">avatarRootName</arg>
  <arg name="menuName">Outfits</arg>
  </tool>
  <tool name="AddMAMenuItemUnder">
  <arg name="parentMenuName">Outfits</arg>
  <arg name="displayName">Dress</arg>
  <arg name="type">Toggle</arg>
  <arg name="paramName">Dress</arg>
  </tool>
  <tool name="AddMAMenuItemUnder">
  <arg name="parentMenuName">Outfits</arg>
  <arg name="displayName">Colors</arg>
  <arg name="type">SubMenu</arg>
  </tool>
  <tool name="AddMAMenuItemUnder">
  <arg name="parentMenuName">Colors</arg>
  <arg name="displayName">Red</arg>
  <arg name="type">Toggle</arg>
  <arg name="paramName">ColorRed</arg>
  </tool>
  ```

### MA Bone Proxy
Non-destructively places objects as children of specific bones.
Used for making weapons or accessories follow the hand or Head.
```
<tool name="AddMABoneProxy">
<arg name="goName">weaponName</arg>
<arg name="targetBoneName">RightHand</arg>
<arg name="mode">1</arg>
</tool>
```
- mode 1=AsChildAtRoot (snaps to the bone). To preserve the object's current world placement, use mode 2 (or run AlignAccessoryToBone first).
- For ring/finger accessories, AttachRingWithBoneProxy is a convenience wrapper.

## General Outfit Setup Procedure

1. Place the outfit Prefab as a child of the avatar:
   ```
   <tool name="SetParent">
   <arg name="childName">outfitName</arg>
   <arg name="parentName">avatarRootName</arg>
   </tool>
   ```

2. Verify MA Merge Armature is configured on the outfit's Armature:
   ```
   <tool name="InspectGameObject">
   <arg name="gameObjectName">avatarRootName/outfitName/Armature</arg>
   </tool>
   ```
   If it's missing, add it:
   ```
   <tool name="AddMAMergeArmature">
   <arg name="goName">avatarRootName/outfitName/Armature</arg>
   <arg name="mergeTargetName">avatarRootName/Armature</arg>
   <arg name="prefix"></arg>
   <arg name="suffix"></arg>
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
- MA note: Merge Animator's "Match Avatar Write Defaults" (default ON since 1.16.1) only matches the avatar's existing WD — it will NOT fix an already-mixed avatar. Only VRCFury enforces a single WD value avatar-wide.
- If you choose all-OFF: every state needs a clip/blend tree, and any layer animating Transforms needs an Avatar Mask.
- Bones won't merge if names don't match → Use MA Merge Armature settings to resolve
- For Quest builds, watch parameter count from MA-generated animator layers
