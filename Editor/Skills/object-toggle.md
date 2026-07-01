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
<tool name="SetupObjectToggle">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="targetPath">toggleTargetPath</arg>
</tool>
```

Example: Toggle Sailor-Jersey:
```
<tool name="SetupObjectToggle">
<arg name="avatarRootName">Chiffon</arg>
<arg name="targetPath">Sailor-Jersey</arg>
</tool>
```

Default OFF (initially hidden):
```
<tool name="SetupObjectToggle">
<arg name="avatarRootName">Chiffon</arg>
<arg name="targetPath">Sailor-Jersey</arg>
<arg name="defaultOn">false</arg>
</tool>
```

With a custom name:
```
<tool name="SetupObjectToggle">
<arg name="avatarRootName">Chiffon</arg>
<arg name="targetPath">Sailor-Jersey</arg>
<arg name="toggleName">SailorJersey</arg>
</tool>
```

This tool automatically creates:
1. ON/OFF animation clips (Assets/Animations/Toggles/)
2. Layer, states, and transitions in the FX AnimatorController
3. Bool parameter in ExpressionParameters
4. Toggle control in ExpressionsMenu

## Manual Setup (Using Individual Tools)

### Step 1: Identify Target Object Path
```
<tool name="ListChildren">
<arg name="name">avatarRootName</arg>
</tool>
```
Find the target GameObject from the avatar's direct children.
Specify the path as a relative path from the avatar root.

### Step 2: Create Toggle Animations
```
<tool name="CreateToggleAnimations">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="targetPath">relativeObjectPath</arg>
</tool>
```
Creates two animation clips: ON (m_IsActive=1) and OFF (m_IsActive=0).

### Step 3: Check FX Controller
```
<tool name="GetVRCFXControllerPath">
<arg name="avatarRootName">avatarRootName</arg>
</tool>
```
Get the FX AnimatorController asset path.

### Step 4: Add Parameter to FX Controller
```
<tool name="AddAnimatorParameter">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="name">toggleName</arg>
<arg name="type">bool</arg>
<arg name="defaultValue">true</arg>
</tool>
```

### Step 5: Add FX Layer
```
<tool name="AddAnimatorLayer">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="layerName">Toggle_toggleName</arg>
</tool>
```
```
<tool name="SetAnimatorLayerWeight">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="layerIndex">layerIndex</arg>
<arg name="weight">1.0</arg>
</tool>
```

### Step 6: Add States
```
<tool name="AddAnimatorState">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="stateName">ON</arg>
<arg name="motionPath">onClipPath</arg>
<arg name="layerIndex">layerIndex</arg>
</tool>
<tool name="AddAnimatorState">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="stateName">OFF</arg>
<arg name="motionPath">offClipPath</arg>
<arg name="layerIndex">layerIndex</arg>
</tool>
```

### Step 7: Add Transitions
```
<tool name="AddAnimatorTransition">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="fromState">OFF</arg>
<arg name="toState">ON</arg>
<arg name="conditions">toggleName=true</arg>
<arg name="layerIndex">layerIndex</arg>
</tool>
<tool name="AddAnimatorTransition">
<arg name="controllerPath">fxControllerPath</arg>
<arg name="fromState">ON</arg>
<arg name="toState">OFF</arg>
<arg name="conditions">toggleName=false</arg>
<arg name="layerIndex">layerIndex</arg>
</tool>
```

### Step 8: Add Expression Parameter
```
<tool name="AddVRCExpressionParameter">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="paramName">toggleName</arg>
<arg name="type">Bool</arg>
<arg name="defaultValue">1.0</arg>
<arg name="saved">true</arg>
<arg name="synced">true</arg>
</tool>
```
- Bool parameter, synced to other players
- defaultValue: 1.0=default ON, 0.0=default OFF

### Step 9: Add Expression Menu Toggle
```
<tool name="AddVRCExpressionsMenuToggle">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">toggleName</arg>
<arg name="paramName">toggleName</arg>
</tool>
```

## When Menu is Full (SubMenu Support)

Expression Menu allows a maximum of 8 controls per page. When full, use submenus.

### Creating a SubMenu
```
<tool name="AddVRCExpressionsMenuSubMenu">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">Outfits</arg>
</tool>
```
A new VRCExpressionsMenu asset is automatically generated and linked as a SubMenu control.

### Adding Controls to a SubMenu
Use the `subMenuPath` parameter to add within a submenu:
```
<tool name="AddVRCExpressionsMenuToggle">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">Hat</arg>
<arg name="paramName">Hat</arg>
<arg name="subMenuPath">Outfits</arg>
</tool>
<tool name="AddVRCExpressionsMenuToggle">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">Glasses</arg>
<arg name="paramName">Glasses</arg>
<arg name="subMenuPath">Outfits</arg>
</tool>
```

### Nested SubMenus
`subMenuPath` supports slash-separated nesting:
```
<tool name="AddVRCExpressionsMenuSubMenu">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">Details</arg>
<arg name="subMenuPath">Outfits</arg>
</tool>
<tool name="AddVRCExpressionsMenuToggle">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">Ring</arg>
<arg name="paramName">Ring</arg>
<arg name="subMenuPath">Outfits/Details</arg>
</tool>
```

## Tool Call Examples

### Example 1: Outfit Toggle (One-Step Setup)
```
User: "Make Sailor-Jersey toggleable from the Expression Menu"
AI: <tool name="SetupObjectToggle">
    <arg name="avatarRootName">Chiffon</arg>
    <arg name="targetPath">Sailor-Jersey</arg>
    </tool>
    Result: Creates ON/OFF animations, FX layer, parameter, and menu entry in one step
```

### Example 2: Accessory Toggle (Default OFF)
```
User: "Add glasses as a toggle, hidden by default"
AI: <tool name="SetupObjectToggle">
    <arg name="avatarRootName">Avatar</arg>
    <arg name="targetPath">Glasses</arg>
    <arg name="defaultOn">false</arg>
    </tool>
```

### Example 3: SubMenu When Menu is Full
```
User: "The menu is full but I want to add another toggle"
AI: <tool name="InspectVRCExpressionsMenu">
    <arg name="avatarRootName">Avatar</arg>
    </tool>
    → Confirm 8 controls
    <tool name="AddVRCExpressionsMenuSubMenu">
    <arg name="avatarRootName">Avatar</arg>
    <arg name="controlName">Accessories</arg>
    </tool>
    → Create submenu
    <tool name="SetupObjectToggle">
    <arg name="avatarRootName">Avatar</arg>
    <arg name="targetPath">NewItem</arg>
    </tool>
    → If menu is full, manually add to submenu:
    <tool name="AddVRCExpressionsMenuToggle">
    <arg name="avatarRootName">Avatar</arg>
    <arg name="controlName">NewItem</arg>
    <arg name="paramName">NewItem</arg>
    <arg name="subMenuPath">Accessories</arg>
    </tool>
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
- **Toggle**: ON/OFF switch (for Bool parameters) → `AddVRCExpressionsMenuToggle`
- **Button**: ON only while pressed → `AddVRCExpressionsMenuButton`
- **SubMenu**: Navigate to submenu → `AddVRCExpressionsMenuSubMenu`
- **RadialPuppet**: Rotary slider (Float) → `AddVRCExpressionsMenuRadialPuppet`
- **TwoAxisPuppet**: 2-axis joystick
- **FourAxisPuppet**: 4-direction input

### Removing Controls
```
<tool name="RemoveVRCExpressionsMenuControl">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="controlName">controlName</arg>
</tool>
<tool name="RemoveVRCExpressionParameter">
<arg name="avatarRootName">avatarRootName</arg>
<arg name="paramName">parameterName</arg>
</tool>
```

### FX Layer Rules
- Layer Weight must be set to 1.0
- Transition ExitTime must be disabled
- Transition Duration must be 0
- Write Defaults (WD) must be consistent across the entire avatar — all states ON or all OFF (VRChat treats every Playable Layer controller as one controller; mixing makes everything behave as WD-Off, so properties stick and expressions don't reset — the SDK only warns). Official baseline is WD OFF.
- Exception (non-negotiable): single-state layers, Direct Blend Tree states, and additive-blending layers must always be WD ON regardless of the rest of the avatar — WD OFF on a DBT makes blendshape values multiply toward infinity.
- If using all-OFF: put a clip/blend tree in every state (empty WD-Off states overwrite to default), and apply an Avatar Mask when animating Transforms.

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
- Menu is full → Create a submenu with AddVRCExpressionsMenuSubMenu and add via subMenuPath
