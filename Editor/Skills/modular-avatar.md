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
- For Quest builds, watch parameter count from MA-generated animator layers
