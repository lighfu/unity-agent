---
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
- Use "Build & Test" for local testing before uploading
- Compare performance rank before and after optimization
- Recommend running [GetAvatarPerformanceStats] for final check before build
