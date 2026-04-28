---
title: VRChat Avatar Build
description: Build and upload avatars using the VRChat SDK
tags: VRChat, build, upload, SDK
---

# VRChat Avatar Build Procedure

## Overview
Build and upload avatars using the VRChat SDK Control Panel.
Perform performance validation before building and prompt fixes if issues are found.

## Prerequisites
- VRChat SDK (com.vrchat.avatars) is installed
- Avatar has a VRCAvatarDescriptor configured
- Logged in to VRChat SDK

## Procedure

### 1. Performance Validation
First, check the avatar's performance:
```
[GetAvatarPerformanceStats('avatarRootName')]
```

### 2. AvatarDescriptor Check
Verify the configuration is correct:
```
[InspectVRCAvatarDescriptor('avatarRootName')]
```

### 3. Common Issues to Check
- ViewPosition is between the eyes
- LipSync is correctly configured
- ExpressionParameters cost is within 256 bits

### 4. Execute Build
Open the SDK Control Panel:
```
[ExecuteMenu('VRChat SDK/Show Control Panel')]
```

**Note**: The actual build and upload must be done manually by the user in the SDK Control Panel.
The AI supports up to opening the Control Panel and guides the user through the process.

### 5. Post-Build Guidance
Tell the user:
- Select the "Build & Publish" tab in the Control Panel
- Select the avatar
- Use "Build & Test" for local testing, or "Build & Publish" to upload

## Performance Rank Thresholds (PC)
| Category | Excellent | Good | Medium | Poor |
|----------|-----------|------|--------|------|
| Polygons | ≤32,000 | ≤70,000 | ≤120,000 | ≤200,000 |
| Materials | ≤4 | ≤8 | ≤16 | ≤32 |
| PhysBone | ≤4 | ≤8 | ≤16 | ≤32 |
| Bones | ≤75 | ≤150 | ≤256 | ≤400 |

## Troubleshooting
- "SDK not found" → VRChat SDK package is not installed
- Build errors → Check the Console window for errors
- Not logged in → Login required in SDK Control Panel
