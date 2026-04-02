---
title: VRChat Object Toggle
description: Set up toggles to show/hide objects from the Expression Menu
tags: VRChat, Expression Menu, toggle, ON/OFF, FX layer, parameter
---

# VRChat Object Toggle Setup

## Overview
Create toggles that show/hide GameObjects from the VRChat avatar's
Expression Menu (radial menu).

## Prerequisites
- VRChat Avatar SDK 3.0 is installed
- Avatar has a VRCAvatarDescriptor configured
- ExpressionParameters / ExpressionsMenu assets are assigned
- FX AnimatorController is assigned

## Easy Method (Recommended): SetupObjectToggle

One-step tool for complete setup:
```
[SetupObjectToggle('avatarRootName', 'toggleTargetPath')]
```

Example: Toggle Sailor-Jersey:
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey')]
```

Default OFF (initially hidden):
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey', defaultOn=false)]
```

With a custom name:
```
[SetupObjectToggle('Chiffon', 'Sailor-Jersey', toggleName='SailorJersey')]
```

This tool automatically creates:
1. ON/OFF animation clips (Assets/Animations/Toggles/)
2. Layer, states, and transitions in the FX AnimatorController
3. Bool parameter in ExpressionParameters
4. Toggle control in ExpressionsMenu

## Manual Setup (Using Individual Tools)

### Step 1: Identify Target Object Path
```
[ListChildren('avatarRootName')]
```
Find the target GameObject from the avatar's direct children.
Specify the path as a relative path from the avatar root.

### Step 2: Create Toggle Animations
```
[CreateToggleAnimations('avatarRootName', 'relativeObjectPath')]
```
Creates two animation clips: ON (m_IsActive=1) and OFF (m_IsActive=0).

### Step 3: Check FX Controller
```
[GetFXControllerPath('avatarRootName')]
```
Get the FX AnimatorController asset path.

### Step 4: Add Parameter to FX Controller
```
[AddAnimatorParameter('fxControllerPath', 'toggleName', 'bool', 'true')]
```

### Step 5: Add FX Layer
```
[AddAnimatorLayer('fxControllerPath', 'Toggle_toggleName')]
```
```
[SetAnimatorLayerWeight('fxControllerPath', layerIndex, 1.0)]
```

### Step 6: Add States
```
[AddAnimatorState('fxControllerPath', 'ON', 'onClipPath', layerIndex)]
[AddAnimatorState('fxControllerPath', 'OFF', 'offClipPath', layerIndex)]
```

### Step 7: Add Transitions
```
[AddAnimatorTransition('fxControllerPath', 'OFF', 'ON', 'toggleName=true', layerIndex)]
[AddAnimatorTransition('fxControllerPath', 'ON', 'OFF', 'toggleName=false', layerIndex)]
```

### Step 8: Add Expression Parameter
```
[AddExpressionParameter('avatarRootName', 'toggleName', 'Bool', 1.0, saved=true, synced=true)]
```
- Bool parameter, synced to other players
- defaultValue: 1.0=default ON, 0.0=default OFF

### Step 9: Add Expression Menu Toggle
```
[AddExpressionsMenuToggle('avatarRootName', 'toggleName', 'toggleName')]
```

## When Menu is Full (SubMenu Support)

Expression Menu allows a maximum of 8 controls per page. When full, use submenus.

### Creating a SubMenu
```
[AddExpressionsMenuSubMenu('avatarRootName', 'Outfits')]
```
A new VRCExpressionsMenu asset is automatically generated and linked as a SubMenu control.

### Adding Controls to a SubMenu
Use the `subMenuPath` parameter to add within a submenu:
```
[AddExpressionsMenuToggle('avatarRootName', 'Hat', 'Hat', subMenuPath='Outfits')]
[AddExpressionsMenuToggle('avatarRootName', 'Glasses', 'Glasses', subMenuPath='Outfits')]
```

### Nested SubMenus
`subMenuPath` supports slash-separated nesting:
```
[AddExpressionsMenuSubMenu('avatarRootName', 'Details', subMenuPath='Outfits')]
[AddExpressionsMenuToggle('avatarRootName', 'Ring', 'Ring', subMenuPath='Outfits/Details')]
```

## Tool Call Examples

### Example 1: Outfit Toggle (One-Step Setup)
```
User: "Make Sailor-Jersey toggleable from the Expression Menu"
AI: [SetupObjectToggle('Chiffon', 'Sailor-Jersey')]
    Result: Creates ON/OFF animations, FX layer, parameter, and menu entry in one step
```

### Example 2: Accessory Toggle (Default OFF)
```
User: "Add glasses as a toggle, hidden by default"
AI: [SetupObjectToggle('Avatar', 'Glasses', defaultOn=false)]
```

### Example 3: SubMenu When Menu is Full
```
User: "The menu is full but I want to add another toggle"
AI: [InspectExpressionsMenu('Avatar')]
    → Confirm 8 controls
    [AddExpressionsMenuSubMenu('Avatar', 'Accessories')]
    → Create submenu
    [SetupObjectToggle('Avatar', 'NewItem')]
    → If menu is full, manually add to submenu:
    [AddExpressionsMenuToggle('Avatar', 'NewItem', 'NewItem', subMenuPath='Accessories')]
```

## VRChat Expression Menu Basics

### Expression Parameter
- Defined in VRCExpressionParameters asset
- Types: Bool (1bit), Int (0-255, 8bit), Float (-1.0–1.0, 8bit)
- Synced: Synced to other players (up to 256 bits total)
- Saved: Value persists across world changes and avatar switches
- Automatically linked to same-named parameters in FX Controller
- See `vrchat-parameters` skill for details

### Expression Menu Control Types
- **Toggle**: ON/OFF switch (for Bool parameters) → `AddExpressionsMenuToggle`
- **Button**: ON only while pressed → `AddExpressionsMenuButton`
- **SubMenu**: Navigate to submenu → `AddExpressionsMenuSubMenu`
- **RadialPuppet**: Rotary slider (Float) → `AddExpressionsMenuRadialPuppet`
- **TwoAxisPuppet**: 2-axis joystick
- **FourAxisPuppet**: 4-direction input

### Removing Controls
```
[RemoveExpressionsMenuControl('avatarRootName', 'controlName')]
[RemoveExpressionParameter('avatarRootName', 'parameterName')]
```

### FX Layer Rules
- Layer Weight must be set to 1.0
- Transition ExitTime must be disabled
- Transition Duration must be 0
- Write Defaults must be consistent across the entire avatar

## Notes
- Expression Parameter total sync cost limit is 256 bits
- Expression Menu allows maximum 8 controls per page
- Parameter names must exactly match between FX Controller and ExpressionParameters
- Undo supported: Operations can be undone with Ctrl+Z
- IMPORTANT: SetupObjectToggle directly edits FX/Param/Menu. FaceEmo is unrelated to object toggles — never use FaceEmo for object toggles. FaceEmo is exclusively for facial expression (face BlendShape) management.

## Troubleshooting
- Toggle not working → Check that parameter names match between FX Controller and ExpressionParameters
- Not showing in menu → Check that ExpressionsMenu is assigned in VRCAvatarDescriptor
- Not visible to other players → Check that parameter Synced=true
- Wrong default state → Check parameter defaultValue and FX initial state
- Menu is full → Create a submenu with AddExpressionsMenuSubMenu and add via subMenuPath
