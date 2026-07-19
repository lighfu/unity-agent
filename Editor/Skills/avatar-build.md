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
<tool name="GetAvatarPerformanceStats">
<arg name="avatarRootName">avatarRootName</arg>
</tool>
```

### 2. AvatarDescriptor Check
Verify the configuration is correct:
```
<tool name="InspectVRCAvatarDescriptor">
<arg name="avatarRootName">avatarRootName</arg>
</tool>
```

### 3. Common Issues to Check
- ViewPosition is between the eyes
- LipSync is correctly configured
- ExpressionParameters cost is within 256 bits

### 4. Check Authentication
```
<tool name="CheckVRChatAuthentication">
</tool>
```
If not authenticated, open the Control Panel with `OpenVRChatSdkControlPanel` and ask the user to log in.

### 5. Build & Publish
Upload directly with the upload tool. Visibility defaults to **private**:
```
<tool name="UploadVRChatAvatar">
<arg name="avatarRootName">avatarRootName</arg>
</tool>
```
- NEW avatars (no blueprint ID) also require `contentName` and `thumbnailPath`.
- `visibility=public` is only allowed after the user explicitly approved it — ask with `AskUser` first, then pass `confirmPublic=true`. A native confirmation dialog is shown to the user as a final gate.
- The SDK may show its copyright-agreement dialog once per session; the user must answer it.
- For local testing instead of uploading, use `TriggerVRChatBuildTest`.
- To review or edit already-uploaded content, use `ListVRChatUploadedContent` / `GetVRChatContentInfo` / `UpdateVRChatContentInfo`.

## Performance Rank Thresholds (PC)
| Category | Excellent | Good | Medium | Poor |
|----------|-----------|------|--------|------|
| Triangles | ≤32,000 | ≤70,000 | ≤70,000 | ≤70,000 |
| Materials | ≤4 | ≤8 | ≤16 | ≤32 |
| PhysBone | ≤4 | ≤8 | ≤16 | ≤32 |
| Bones | ≤75 | ≤150 | ≤256 | ≤400 |

**Note**: Triangles only distinguish Excellent (≤32,000) from the rest (≤70,000); Good/Medium/Poor share the 70,000 cap and exceeding 70,000 = Very Poor. For the full PC + Mobile threshold tables see the avatar-optimization skill.

## Troubleshooting
- "SDK not found" → VRChat SDK package is not installed
- Build errors → Check the Console window for errors
- Not logged in → Login required in SDK Control Panel
