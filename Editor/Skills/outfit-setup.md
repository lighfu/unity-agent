---
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
- After outfit changes, recommend checking performance with GetAvatarPerformanceStats
