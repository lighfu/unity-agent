---
title: VRChat Animator Parameters Reference
description: Technical reference for VRChat avatar Animator parameters, syncing, and built-in parameters
tags: VRChat, Animator, Parameters, Expression, sync
---

# VRChat Animator Parameters Reference

## Parameter Types and Ranges

| Type | Range | Sync Cost |
|------|-------|-----------|
| **Int** | 0–255 | 8 bits |
| **Float** | -1.0–1.0 | 8 bits |
| **Bool** | true/false | 1 bit |

- Total sync parameter limit: **256 bits**
- Bool: Best for toggles (ON/OFF)
- Int: Multiple outfit set switching, etc.
- Float: RadialPuppet (slider), gradient control, etc.

## Built-in Parameters

### Always Available
| Parameter Name | Type | Description | Sync Type |
|---------------|------|-------------|-----------|
| `IsLocal` | Bool | Whether this is the local player | None |
| `Viseme` | Int | Lip sync (0-14) | Speech |
| `Voice` | Float | Voice input level | Speech |
| `GestureLeft` | Int | Left hand gesture (0-7) | IK |
| `GestureRight` | Int | Right hand gesture (0-7) | IK |
| `GestureLeftWeight` | Float | Left trigger press amount | Playable |
| `GestureRightWeight` | Float | Right trigger press amount | Playable |
| `AngularY` | Float | Rotation speed | IK |
| `VelocityX` | Float | Lateral velocity | IK |
| `VelocityY` | Float | Vertical velocity | IK |
| `VelocityZ` | Float | Forward/backward velocity | IK |
| `VelocityMagnitude` | Float | Speed magnitude | IK |
| `Upright` | Float | Upright degree (0=prone, 1=standing) | IK |
| `Grounded` | Bool | Ground contact | IK |
| `Seated` | Bool | Seated state | IK |
| `AFK` | Bool | AFK state | IK |
| `TrackingType` | Int | Tracking type | Playable |
| `VRMode` | Int | 0=Desktop, 1=VR | IK |
| `MuteSelf` | Bool | Self-muted | Playable |
| `InStation` | Bool | In a station | IK |
| `Earmuffs` | Bool | Earmuffs enabled | Playable |
| `IsOnFriendsList` | Bool | On friends list | Other |
| `AvatarVersion` | Int | 3 if built with SDK3 (2020.3.2+), else 0 | IK |
| `IsAnimatorEnabled` | Bool | Whether Animator is enabled | None |
| `ScaleModified` | Bool | True if avatar is being scaled, false at default size | Playable |
| `ScaleFactor` | Float | Eye height scale | Playable |
| `ScaleFactorInverse` | Float | Inverse of ScaleFactor | Playable |
| `EyeHeightAsMeters` | Float | Eye height (meters) | Playable |
| `EyeHeightAsPercent` | Float | Eye height normalized within 0.2m–5.0m: (h-0.2)/4.8, range ~0.0–1.0 | Playable |

### Gesture Values (GestureLeft/GestureRight)
| Value | Gesture |
|-------|---------|
| 0 | Neutral |
| 1 | Fist |
| 2 | HandOpen |
| 3 | FingerPoint |
| 4 | Victory |
| 5 | RockNRoll |
| 6 | HandGun |
| 7 | ThumbsUp |

### TrackingType Values
| Value | Description |
|-------|-------------|
| 0 | Uninitialized |
| 1 | Generic Rig |
| 2 | Hands-only, no fingers (transient state) |
| 3 | Head and hands (3-point VR) |
| 4 | Head + Hands + Hip (4-point VR) |
| 5 | Head + Hands + Feet (5-point, no hip) |
| 6 | Full Body (Head + Hands + Hip + Feet) |

## Sync Types

| Type | Description |
|------|-------------|
| **Speech** | Lip sync/voice. Sent with voice data |
| **Playable** | Used in Playable layer. Animator state sync |
| **IK** | Sent with IK data. Position/tracking related |
| **None** | No sync. Local only |
| **Other** | Synced via a dedicated channel (e.g. friends-list state) |

## Custom Parameters

### VRCExpressionParameters
- Defined in VRCAvatarDescriptor's `expressionParameters`
- **Automatically linked** to same-named parameters in the FX AnimatorController
- `networkSynced=true`: Synced to other players (consumes budget)
- `networkSynced=false`: Local only (no budget cost)
- `saved=true`: Value persists across world changes and avatar switches

### Parameter Sync Mechanism
1. User operates controls in Expression Menu
2. VRCExpressionParameters values change
3. Automatically reflected to same-named FX Animator parameters
4. Animator transitions based on conditions

### Sync Cost Calculation
```
Bool × N = N bits
Int × N = N × 8 bits
Float × N = N × 8 bits
Total ≤ 256 bits
```

Example: Bool×10 + Float×5 + Int×2 = 10 + 40 + 16 = 66 bits

## Playable Layers

VRChat avatar's 5 layers:
| Index | Name | Purpose |
|-------|------|---------|
| 0 | Base | Locomotion |
| 1 | Additive | Additive animations (breathing, etc.) |
| 2 | Gesture | Gestures and hand movement |
| 3 | Action | Emotes and full-body animations |
| 4 | FX | **Toggles, expressions, gimmicks** |

- FX (index=4, type=5) is the primary layer for object toggles and gimmicks
- FX layer Weight must be 1.0
- Write Defaults (WD) must be consistent across the entire avatar — all states ON or all OFF (all Playable Layer controllers count as ONE controller). Mixed WD behaves like WD-Off: properties stick and facial expressions fail to reset; the SDK only warns, it does not auto-fix.
- Exception (non-negotiable): additive-blending layers and Direct Blend Tree single-state layers must always be WD ON regardless of the rest, since WD OFF makes their values multiply toward infinity.
- The official baseline is WD OFF (built-in/sample animators are OFF); consistent ON is also valid — the rule is consistency, not a specific value.
- If you go all-OFF: give every state a clip/blend tree, and apply an Avatar Mask to any layer that animates Transforms.

## Tool Usage

### Check Parameters
```
[ListVRCExpressionParameters('avatarRootName')]
```

### Add Parameter
```
[AddVRCExpressionParameter('avatarRootName', 'ParamName', 'Bool', 1.0, saved=true, synced=true)]
```

### Remove Parameter
```
[RemoveVRCExpressionParameter('avatarRootName', 'ParamName')]
```

### Add Parameter to FX Controller
```
[AddAnimatorParameter('fxControllerPath', 'ParamName', 'bool', 'true')]
```

### Object Toggle (One-Step Setup)
```
[SetupObjectToggle('avatarRootName', 'objectPath')]
```

## Notes
- Built-in parameters don't need to be defined in ExpressionParameters (automatically available)
- Custom parameter names override built-ins if they conflict
- Exceeding 256-bit sync cost will cause the avatar to malfunction
- Parameter names must **exactly match** between FX Controller and ExpressionParameters
- To use built-in parameters in an Animator, simply add a same-named parameter to the Animator
