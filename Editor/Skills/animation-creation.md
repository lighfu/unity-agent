---
title: Animation Clip Creation
description: Creating AnimationClips with keyframes for motion, BlendShape, and property animations
tags: animation, motion, keyframe, animationclip, blendshape, motion, animation
---

# AnimationClip Creation Guide

## Overview
Create Unity AnimationClips via text using `CreateAnimationClip` and `SetAnimationCurve`.
Supports bone rotation/position, BlendShapes, object ON/OFF, and any other animatable property.

## Tools Used
- `ListBones(avatarRootName)` — Check bone hierarchy and paths
- `ListHumanoidMapping(avatarRootName)` — Check Humanoid bone ↔ Transform name mapping
- `InspectBone(avatarRootName, boneName)` — Check bone's current position/rotation
- `CreateAnimationClip(clipName, savePath, length, isLooping)` — Create an empty clip
- `SetAnimationCurve(clipPath, bonePath, property, keyframes)` — Add curve (keyframes)
- `GetAnimationClipInfo(clipPath)` — Inspect the created clip

## Coordinate System and Properties

### Transform Rotation (Euler Angles, Degrees)
- `rotation.x` — X-axis rotation (forward/backward tilt: + tilts forward)
- `rotation.y` — Y-axis rotation (left/right facing: + faces left)
- `rotation.z` — Z-axis rotation (lateral tilt: + tilts right)

**Note**: Values are in degrees. Example: 45 = 45 degrees

### Transform Position (Meters)
- `position.x` — X-axis (right is +)
- `position.y` — Y-axis (up is +)
- `position.z` — Z-axis (forward is +)

**Note**: Local coordinate system. Relative to the parent bone.

### Transform Scale
- `scale.x`, `scale.y`, `scale.z` — Scale per axis (1.0 is default)

### BlendShape
- `blendShape.ShapeName` — BlendShape on SkinnedMeshRenderer (0–100)
- ShapeName is case-sensitive

### GameObject ON/OFF
- `active` — 1=visible, 0=hidden

## Determining bonePath

### Procedure
1. Use `ListBones('avatarName')` to check the bone hierarchy
2. Use the relative path from the object that has the Animator component
3. Example: If avatar root is `MyAvatar` and right upper arm is `MyAvatar/Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R`
   → bonePath is `Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R`

### Common Bone Structure
```
Armature/
  Hips/                        ← Center of body (pelvis)
    Spine/                     ← Lower spine
      Chest/                   ← Chest
        Neck/                  ← Neck
          Head/                ← Head
        Shoulder_L/            ← Left shoulder
          Upper_Arm_L/         ← Left upper arm
            Lower_Arm_L/       ← Left forearm
              Hand_L/          ← Left hand
        Shoulder_R/            ← Right shoulder
          Upper_Arm_R/         ← Right upper arm
            Lower_Arm_R/       ← Right forearm
              Hand_R/          ← Right hand
    Upper_Leg_L/               ← Left thigh
      Lower_Leg_L/             ← Left shin
        Foot_L/                ← Left foot
    Upper_Leg_R/               ← Right thigh
      Lower_Leg_R/             ← Right shin
        Foot_R/                ← Right foot
```
**Note**: Actual bone names differ per avatar. Always verify with `ListBones`.

## Keyframes Syntax

### Basic Format
```
time:value, time:value, time:value
```
- Time: in seconds (0.0 = start, 1.0 = 1 second later)
- Value: numeric value appropriate for the property

### Examples
```
0:0, 0.5:45, 1.0:0        ← 0°→45°→0° (round trip)
0:0, 0.3:90, 0.7:90, 1.0:0  ← Raise, hold, return
0:0, 1.0:360               ← Full rotation
```

## Motion Creation Examples

### Waving Animation
```
User: "Create a right-hand waving animation"

1. Check bone structure:
[ListBones('MyAvatar', 'Arm')]

2. Create clip:
[CreateAnimationClip('wave', 'Assets/Animations', 2.0, true)]

3. Raise right upper arm (Z-axis rotation to raise arm sideways):
[SetAnimationCurve('Assets/Animations/wave.anim', 'Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R', 'rotation.z', '0:-60, 0.3:-60, 1.7:-60, 2.0:-60')]

4. Wave with forearm (Z-axis oscillation):
[SetAnimationCurve('Assets/Animations/wave.anim', 'Armature/Hips/Spine/Chest/Shoulder_R/Upper_Arm_R/Lower_Arm_R', 'rotation.z', '0:0, 0.3:-30, 0.6:30, 0.9:-30, 1.2:30, 1.5:-30, 1.8:30, 2.0:0')]
```

### Nodding Animation
```
User: "Create a nodding animation"

1. Check bones → Get Head bone path
2. Create clip:
[CreateAnimationClip('nod', 'Assets/Animations', 0.8, false)]

3. Tilt head forward (X-axis rotation):
[SetAnimationCurve('Assets/Animations/nod.anim', 'Armature/Hips/Spine/Chest/Neck/Head', 'rotation.x', '0:0, 0.2:15, 0.4:0, 0.6:10, 0.8:0')]
```

### Facial Expression Animation (BlendShape)
```
User: "Create a smile animation"

1. Check BlendShape names:
[ListBlendShapes('Body')]

2. Create clip:
[CreateAnimationClip('smile', 'Assets/Animations', 0.5, false)]

3. Set smile BlendShape:
[SetAnimationCurve('Assets/Animations/smile.anim', 'Body', 'blendShape.face_smile', '0:0, 0.3:100, 0.5:100')]
```

### Object Toggle (Show/Hide)
```
[CreateAnimationClip('hat_on', 'Assets/Animations', 0.0, false)]
[SetAnimationCurve('Assets/Animations/hat_on.anim', 'Hat', 'active', '0:1')]

[CreateAnimationClip('hat_off', 'Assets/Animations', 0.0, false)]
[SetAnimationCurve('Assets/Animations/hat_off.anim', 'Hat', 'active', '0:0')]
```

## Tips for Natural Motion

### Rotation Value Guidelines (Human Range of Motion)
- **Neck rotation**: X-axis ±30°, Y-axis ±60°, Z-axis ±30°
- **Raising arm from shoulder**: Z-axis -80° (sideways), X-axis -80° (forward)
- **Elbow bend**: Y-axis 0–140°
- **Waist rotation**: X-axis ±30°, Y-axis ±40°, Z-axis ±20°
- **Finger bend**: X-axis 0–90°

### Movement Principles
- **Same start and end values** for smooth looping
- **Add easing**: Closer keyframe intervals at the start and end of movement
- **Exaggerate**: Avatars appear small in VRChat, so make movements bigger than real life
- **Chain multiple bones**: When raising an arm, stagger shoulder → upper arm → forearm slightly for natural movement
- **Symmetry and asymmetry**: Even for two-handed motions, slight offset looks more natural

## Notes
- Bone names differ per avatar, so always verify with `ListBones` before setting curves
- Rotation values are local Euler angles. Dependent on parent bone orientation
- Rotations exceeding 360° may cause gimbal lock issues
- When using in VRChat's FX layer, pay attention to Write Defaults settings
- Assign created clips to AnimatorController States using: `SetAnimatorStateMotion`
