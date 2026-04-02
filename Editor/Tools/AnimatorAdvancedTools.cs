using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Animator 上級機能ツール：BlendTree、AvatarMask、State/Transition 詳細設定。
    /// VRChat FX レイヤーの構築に不可欠。
    /// </summary>
    public static class AnimatorAdvancedTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        // =================================================================
        // BlendTree
        // =================================================================

        [AgentTool(@"Create a BlendTree in an AnimatorController state.
blendType: Simple1D, SimpleDirectional2D, FreeformDirectional2D, FreeformCartesian2D, Direct.
blendParameter: name of the float parameter for X-axis blending.
blendParameterY: (2D only) name of float parameter for Y-axis.
The state's motion is replaced with the new BlendTree. Add children with AddBlendTreeChild.")]
        public static string CreateBlendTree(string controllerPath, string stateName, string blendType = "Simple1D",
            string blendParameter = "", string blendParameterY = "", int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;
            var stateWrapper = sm.states.FirstOrDefault(s => s.state.name == stateName);
            if (stateWrapper.state == null)
                return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            if (!TryParseBlendTreeType(blendType, out BlendTreeType btType))
                return $"Error: Unknown blendType '{blendType}'. Valid: Simple1D, SimpleDirectional2D, FreeformDirectional2D, FreeformCartesian2D, Direct.";

            var blendTree = new BlendTree();
            blendTree.name = stateName + "_BlendTree";
            blendTree.blendType = btType;
            blendTree.useAutomaticThresholds = false;

            if (!string.IsNullOrEmpty(blendParameter))
                blendTree.blendParameter = blendParameter;
            if (!string.IsNullOrEmpty(blendParameterY) && Is2DBlendType(btType))
                blendTree.blendParameterY = blendParameterY;

            // Store as sub-asset of the controller
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            Undo.RecordObject(stateWrapper.state, "Create BlendTree");
            stateWrapper.state.motion = blendTree;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return $"Success: Created BlendTree '{blendTree.name}' (type={btType}) in state '{stateName}'. Use AddBlendTreeChild to add motions.";
        }

        [AgentTool(@"Add a child motion to a BlendTree.
threshold: (1D) the parameter value at which this motion is fully active.
position: (2D) comma-separated 'x,y' position in 2D blend space.
motionPath: asset path to AnimationClip or leave empty to add an empty slot.
directBlendParameter: (Direct mode only) parameter name for this child's weight.")]
        public static string AddBlendTreeChild(string controllerPath, string stateName, string motionPath = "",
            float threshold = 0f, string position = "", string directBlendParameter = "", int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            var blendTree = state.motion as BlendTree;
            if (blendTree == null) return $"Error: State '{stateName}' does not have a BlendTree. Use CreateBlendTree first.";

            Motion motion = null;
            if (!string.IsNullOrEmpty(motionPath))
            {
                motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                if (motion == null) return $"Error: Motion not found at '{motionPath}'.";
            }

            Undo.RecordObject(blendTree, "Add BlendTree Child");

            if (Is2DBlendType(blendTree.blendType) && !string.IsNullOrEmpty(position))
            {
                var pos = ParseVector2(position);
                if (!pos.HasValue) return "Error: Invalid position format. Use 'x,y'.";
                blendTree.AddChild(motion, pos.Value);
            }
            else if (blendTree.blendType == BlendTreeType.Direct)
            {
                blendTree.AddChild(motion);
                // Set direct blend parameter on the last child
                if (!string.IsNullOrEmpty(directBlendParameter))
                {
                    var children = blendTree.children;
                    var last = children[children.Length - 1];
                    last.directBlendParameter = directBlendParameter;
                    children[children.Length - 1] = last;
                    blendTree.children = children;
                }
            }
            else
            {
                blendTree.AddChild(motion, threshold);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            string motionName = motion != null ? motion.name : "empty";
            return $"Success: Added child '{motionName}' to BlendTree in '{stateName}' ({blendTree.children.Length} children total).";
        }

        [AgentTool("Remove a child from a BlendTree by index (0-based).")]
        public static string RemoveBlendTreeChild(string controllerPath, string stateName, int childIndex, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            var blendTree = state.motion as BlendTree;
            if (blendTree == null) return $"Error: State '{stateName}' does not have a BlendTree.";

            if (childIndex < 0 || childIndex >= blendTree.children.Length)
                return $"Error: Child index {childIndex} out of range (0-{blendTree.children.Length - 1}).";

            Undo.RecordObject(blendTree, "Remove BlendTree Child");
            blendTree.RemoveChild(childIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return $"Success: Removed child[{childIndex}] from BlendTree in '{stateName}' ({blendTree.children.Length} remaining).";
        }

        [AgentTool("Inspect a BlendTree on a state. Shows blend type, parameters, and all children with thresholds/positions.")]
        public static string InspectBlendTree(string controllerPath, string stateName, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            var blendTree = state.motion as BlendTree;
            if (blendTree == null) return $"Error: State '{stateName}' does not have a BlendTree.";

            var sb = new StringBuilder();
            sb.AppendLine($"BlendTree on '{stateName}':");
            sb.AppendLine($"  Type: {blendTree.blendType}");
            sb.AppendLine($"  Parameter: {blendTree.blendParameter}");
            if (Is2DBlendType(blendTree.blendType))
                sb.AppendLine($"  ParameterY: {blendTree.blendParameterY}");
            sb.AppendLine($"  AutoThresholds: {blendTree.useAutomaticThresholds}");
            sb.AppendLine($"  MinThreshold: {blendTree.minThreshold:F3}, MaxThreshold: {blendTree.maxThreshold:F3}");

            var children = blendTree.children;
            sb.AppendLine($"  Children ({children.Length}):");
            for (int i = 0; i < children.Length; i++)
            {
                var c = children[i];
                string motionName = c.motion != null ? c.motion.name : "null";
                if (Is2DBlendType(blendTree.blendType))
                    sb.AppendLine($"    [{i}] {motionName} pos=({c.position.x:F2},{c.position.y:F2}) timeScale={c.timeScale:F2}");
                else if (blendTree.blendType == BlendTreeType.Direct)
                    sb.AppendLine($"    [{i}] {motionName} directParam={c.directBlendParameter} timeScale={c.timeScale:F2}");
                else
                    sb.AppendLine($"    [{i}] {motionName} threshold={c.threshold:F3} timeScale={c.timeScale:F2}");
            }

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // AvatarMask
        // =================================================================

        [AgentTool(@"Create a new AvatarMask asset. savePath is a folder path.
By default all humanoid body parts are enabled. Use ConfigureAvatarMask to modify.")]
        public static string CreateAvatarMask(string maskName, string savePath = "Assets")
        {
            if (!System.IO.Directory.Exists(savePath))
                System.IO.Directory.CreateDirectory(savePath);

            string assetPath = $"{savePath}/{maskName}.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(assetPath) != null)
                return $"Error: AvatarMask already exists at '{assetPath}'.";

            var mask = new AvatarMask();
            mask.name = maskName;
            AssetDatabase.CreateAsset(mask, assetPath);
            AssetDatabase.SaveAssets();

            return $"Success: Created AvatarMask at '{assetPath}'.";
        }

        [AgentTool(@"Configure humanoid body part masking on an AvatarMask.
bodyParts: semicolon-separated 'partName=true/false'.
Valid parts: Root, Body, Head, LeftLeg, RightLeg, LeftArm, RightArm, LeftFingers, RightFingers, LeftFootIK, RightFootIK, LeftHandIK, RightHandIK.
Example: 'Head=true;LeftArm=false;RightArm=false' to only enable head.")]
        public static string ConfigureAvatarMask(string maskPath, string bodyParts)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) return $"Error: AvatarMask not found at '{maskPath}'.";

            Undo.RecordObject(mask, "Configure AvatarMask");

            var entries = bodyParts.Split(';');
            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;

                string partName = trimmed.Substring(0, eqIdx).Trim();
                string valStr = trimmed.Substring(eqIdx + 1).Trim().ToLower();
                bool active = valStr == "true" || valStr == "1";

                if (TryParseBodyPart(partName, out AvatarMaskBodyPart part))
                    mask.SetHumanoidBodyPartActive(part, active);
            }

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            return $"Success: Configured AvatarMask body parts at '{maskPath}'.";
        }

        [AgentTool(@"Add transform paths to an AvatarMask from an avatar's bone hierarchy.
avatarGoName: the avatar root GameObject name (must have an Animator with Avatar).
If setAllActive is true, all transforms are set active. Otherwise all inactive.
Use SetAvatarMaskTransform to toggle individual transforms.")]
        public static string SetAvatarMaskTransformsFromAvatar(string maskPath, string avatarGoName, bool setAllActive = true)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) return $"Error: AvatarMask not found at '{maskPath}'.";

            var go = FindGO(avatarGoName);
            if (go == null) return $"Error: GameObject '{avatarGoName}' not found.";

            var animator = go.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
                return $"Error: '{avatarGoName}' has no Animator or Avatar.";

            var transforms = go.GetComponentsInChildren<Transform>(true);
            Undo.RecordObject(mask, "Set AvatarMask Transforms");

            mask.transformCount = transforms.Length;
            for (int i = 0; i < transforms.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(transforms[i], go.transform);
                mask.SetTransformPath(i, path);
                mask.SetTransformActive(i, setAllActive);
            }

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            return $"Success: Set {transforms.Length} transform paths on AvatarMask from '{avatarGoName}' (allActive={setAllActive}).";
        }

        [AgentTool(@"Toggle specific transform paths in an AvatarMask.
paths: semicolon-separated 'transformPath=true/false'. Example: 'Armature/Hips/Spine=true;Armature/Hips/LeftUpLeg=false'")]
        public static string SetAvatarMaskTransform(string maskPath, string paths)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) return $"Error: AvatarMask not found at '{maskPath}'.";

            Undo.RecordObject(mask, "Set AvatarMask Transform");
            int updated = 0;

            var entries = paths.Split(';');
            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;

                string path = trimmed.Substring(0, eqIdx).Trim();
                bool active = trimmed.Substring(eqIdx + 1).Trim().ToLower() == "true";

                for (int i = 0; i < mask.transformCount; i++)
                {
                    if (mask.GetTransformPath(i) == path)
                    {
                        mask.SetTransformActive(i, active);
                        updated++;
                        break;
                    }
                }
            }

            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            return $"Success: Updated {updated} transforms on AvatarMask.";
        }

        [AgentTool("Inspect an AvatarMask. Shows body part states and transform paths with active states.")]
        public static string InspectAvatarMask(string maskPath)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) return $"Error: AvatarMask not found at '{maskPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"AvatarMask: {mask.name}");

            sb.AppendLine("  Humanoid Body Parts:");
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                sb.AppendLine($"    {part}: {mask.GetHumanoidBodyPartActive(part)}");
            }

            sb.AppendLine($"  Transforms ({mask.transformCount}):");
            int shown = 0;
            for (int i = 0; i < mask.transformCount && shown < 100; i++)
            {
                sb.AppendLine($"    [{(mask.GetTransformActive(i) ? "ON" : "  ")}] {mask.GetTransformPath(i)}");
                shown++;
            }
            if (mask.transformCount > 100)
                sb.AppendLine($"    ... ({mask.transformCount - 100} more)");

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // AnimatorState Advanced
        // =================================================================

        [AgentTool(@"Configure advanced properties on an AnimatorState.
writeDefaults: CRITICAL for VRChat FX - true writes default values, false preserves previous. Must be consistent across all states.
speed: playback speed multiplier (1.0=normal). speedParameter: drive speed with a float parameter.
cycleOffset: start offset (0-1). cycleOffsetParameter: drive offset with a float parameter.
mirror: flip animation horizontally. tag: custom tag string.")]
        public static string ConfigureAnimatorState(string controllerPath, string stateName, int layerIndex = 0,
            int writeDefaults = -1, float speed = float.NaN, string speedParameter = null,
            float cycleOffset = float.NaN, string cycleOffsetParameter = null,
            int mirror = -1, string tag = null)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            Undo.RecordObject(state, "Configure AnimatorState");

            if (writeDefaults >= 0) state.writeDefaultValues = writeDefaults != 0;
            if (!float.IsNaN(speed)) state.speed = speed;
            if (speedParameter != null)
            {
                state.speedParameter = speedParameter;
                state.speedParameterActive = !string.IsNullOrEmpty(speedParameter);
            }
            if (!float.IsNaN(cycleOffset)) state.cycleOffset = cycleOffset;
            if (cycleOffsetParameter != null)
            {
                state.cycleOffsetParameter = cycleOffsetParameter;
                state.cycleOffsetParameterActive = !string.IsNullOrEmpty(cycleOffsetParameter);
            }
            if (mirror >= 0) state.mirror = mirror != 0;
            if (tag != null) state.tag = tag;

            EditorUtility.SetDirty(controller);
            return $"Success: Configured state '{stateName}' (WD={state.writeDefaultValues}, speed={state.speed:F2}).";
        }

        [AgentTool("Inspect detailed properties of an AnimatorState. Shows motion, writeDefaults, speed, transitions, and behaviors.")]
        public static string InspectAnimatorState(string controllerPath, string stateName, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            var sb = new StringBuilder();
            sb.AppendLine($"AnimatorState: {state.name}");
            sb.AppendLine($"  Motion: {(state.motion != null ? state.motion.name : "none")} ({(state.motion is BlendTree ? "BlendTree" : "AnimationClip")})");
            sb.AppendLine($"  WriteDefaultValues: {state.writeDefaultValues}");
            sb.AppendLine($"  Speed: {state.speed:F2}{(state.speedParameterActive ? $" (param={state.speedParameter})" : "")}");
            sb.AppendLine($"  CycleOffset: {state.cycleOffset:F3}{(state.cycleOffsetParameterActive ? $" (param={state.cycleOffsetParameter})" : "")}");
            sb.AppendLine($"  Mirror: {state.mirror}");
            sb.AppendLine($"  Tag: {(string.IsNullOrEmpty(state.tag) ? "(none)" : state.tag)}");
            sb.AppendLine($"  IKOnFeet: {state.iKOnFeet}");

            // Transitions
            sb.AppendLine($"  Transitions ({state.transitions.Length}):");
            foreach (var t in state.transitions)
            {
                string dest = t.destinationState != null ? t.destinationState.name : (t.isExit ? "Exit" : "?");
                string conds = FormatConditions(t.conditions);
                sb.AppendLine($"    -> {dest} (exitTime={t.hasExitTime}, duration={t.duration:F3}, fixed={t.hasFixedDuration}) [{conds}]");
            }

            // Behaviors
            var behaviors = state.behaviours;
            if (behaviors.Length > 0)
            {
                sb.AppendLine($"  Behaviors ({behaviors.Length}):");
                foreach (var b in behaviors)
                    sb.AppendLine($"    {b.GetType().Name}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Batch set writeDefaultValues on all states in a layer. Essential for VRChat FX layers.
detectExisting: if true, detects the majority setting first and reports it (dry run).")]
        public static string BatchSetWriteDefaults(string controllerPath, int layerIndex, bool writeDefaults, bool detectExisting = false)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;
            var allStates = GetAllStatesRecursive(sm);

            if (detectExisting)
            {
                int onCount = allStates.Count(s => s.writeDefaultValues);
                int offCount = allStates.Count - onCount;
                return $"Layer[{layerIndex}] '{layers[layerIndex].name}': {allStates.Count} states total, WriteDefaults ON={onCount}, OFF={offCount}. Majority={((onCount >= offCount) ? "ON" : "OFF")}.";
            }

            foreach (var state in allStates)
            {
                Undo.RecordObject(state, "Batch Set WriteDefaults");
                state.writeDefaultValues = writeDefaults;
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Set WriteDefaults={writeDefaults} on {allStates.Count} states in layer[{layerIndex}].";
        }

        // =================================================================
        // Transition Advanced
        // =================================================================

        [AgentTool(@"Configure advanced transition settings.
fromState: source state name (or 'Any' for AnyState transitions).
toState: destination state name.
hasExitTime: whether to wait for exit time before transitioning.
exitTime: normalized time (0-1) at which transition starts (only if hasExitTime=true). >1 means after N loops.
duration: transition blend duration in seconds (if hasFixedDuration=true) or normalized (if false).
hasFixedDuration: true=seconds, false=normalized time.
offset: start time in destination state (0-1).
canTransitionToSelf: (AnyState only) allow transitioning to the current state.
interruptionSource: 0=None, 1=Source, 2=Destination, 3=SourceThenDestination, 4=DestinationThenSource.
orderedInterruption: higher-priority transitions interrupt lower ones.")]
        public static string ConfigureTransition(string controllerPath, string fromState, string toState, int layerIndex = 0,
            int hasExitTime = -1, float exitTime = float.NaN, float duration = float.NaN,
            int hasFixedDuration = -1, float offset = float.NaN, int canTransitionToSelf = -1,
            int interruptionSource = -1, int orderedInterruption = -1)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range.";

            var sm = layers[layerIndex].stateMachine;
            AnimatorStateTransition transition = null;

            if (fromState.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                var dest = FindState(controller, toState, layerIndex);
                if (dest == null) return $"Error: Destination state '{toState}' not found.";
                transition = sm.anyStateTransitions.FirstOrDefault(t => t.destinationState == dest);
            }
            else
            {
                var src = FindState(controller, fromState, layerIndex);
                if (src == null) return $"Error: Source state '{fromState}' not found.";
                var dest = FindState(controller, toState, layerIndex);
                transition = src.transitions.FirstOrDefault(t =>
                    (dest != null && t.destinationState == dest) || (toState == "Exit" && t.isExit));
            }

            if (transition == null) return $"Error: Transition '{fromState}' -> '{toState}' not found in layer[{layerIndex}].";

            Undo.RecordObject(transition, "Configure Transition");

            if (hasExitTime >= 0) transition.hasExitTime = hasExitTime != 0;
            if (!float.IsNaN(exitTime)) transition.exitTime = exitTime;
            if (!float.IsNaN(duration)) transition.duration = duration;
            if (hasFixedDuration >= 0) transition.hasFixedDuration = hasFixedDuration != 0;
            if (!float.IsNaN(offset)) transition.offset = offset;
            if (canTransitionToSelf >= 0) transition.canTransitionToSelf = canTransitionToSelf != 0;
            if (interruptionSource >= 0)
                transition.interruptionSource = (TransitionInterruptionSource)interruptionSource;
            if (orderedInterruption >= 0) transition.orderedInterruption = orderedInterruption != 0;

            EditorUtility.SetDirty(controller);
            return $"Success: Configured transition '{fromState}' -> '{toState}' (exitTime={transition.hasExitTime}, duration={transition.duration:F3}).";
        }

        [AgentTool("Add an exit transition from a state (transition to the state machine's exit node).")]
        public static string AddExitTransition(string controllerPath, string stateName, string conditions = "", int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var state = FindState(controller, stateName, layerIndex);
            if (state == null) return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            Undo.RecordObject(state, "Add Exit Transition");
            var transition = state.AddExitTransition();

            if (!string.IsNullOrEmpty(conditions))
            {
                transition.hasExitTime = false;
                var parsed = ParseConditions(conditions, controller);
                foreach (var c in parsed)
                    transition.AddCondition(c.mode, c.threshold, c.parameter);
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Added exit transition from '{stateName}'.";
        }

        [AgentTool("Remove a transition between states. fromState can be 'Any'. toState can be 'Exit'.")]
        public static string RemoveTransition(string controllerPath, string fromState, string toState, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range.";

            var sm = layers[layerIndex].stateMachine;

            if (fromState.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                var dest = FindState(controller, toState, layerIndex);
                if (dest == null) return $"Error: Destination state '{toState}' not found.";
                var t = sm.anyStateTransitions.FirstOrDefault(tr => tr.destinationState == dest);
                if (t == null) return $"Error: AnyState -> '{toState}' transition not found.";

                Undo.RecordObject(sm, "Remove AnyState Transition");
                sm.RemoveAnyStateTransition(t);
            }
            else
            {
                var src = FindState(controller, fromState, layerIndex);
                if (src == null) return $"Error: Source state '{fromState}' not found.";

                AnimatorStateTransition transition;
                if (toState.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                    transition = src.transitions.FirstOrDefault(t => t.isExit);
                else
                {
                    var dest = FindState(controller, toState, layerIndex);
                    transition = src.transitions.FirstOrDefault(t => t.destinationState == dest);
                }

                if (transition == null) return $"Error: Transition '{fromState}' -> '{toState}' not found.";

                Undo.RecordObject(src, "Remove Transition");
                src.RemoveTransition(transition);
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Removed transition '{fromState}' -> '{toState}' in layer[{layerIndex}].";
        }

        // =================================================================
        // Layer Advanced
        // =================================================================

        [AgentTool(@"Configure advanced layer properties.
blendingMode: 0=Override, 1=Additive.
maskPath: asset path to an AvatarMask to assign to this layer.
iKPass: enable IK callbacks.
weight: default weight (0-1).")]
        public static string ConfigureAnimatorLayer(string controllerPath, int layerIndex,
            int blendingMode = -1, string maskPath = null, int iKPass = -1, float weight = float.NaN)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            Undo.RecordObject(controller, "Configure Animator Layer");

            var layer = layers[layerIndex];
            if (blendingMode >= 0) layer.blendingMode = (AnimatorLayerBlendingMode)blendingMode;
            if (maskPath != null)
            {
                if (string.IsNullOrEmpty(maskPath))
                    layer.avatarMask = null;
                else
                {
                    var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
                    if (mask == null) return $"Error: AvatarMask not found at '{maskPath}'.";
                    layer.avatarMask = mask;
                }
            }
            if (iKPass >= 0) layer.iKPass = iKPass != 0;
            if (!float.IsNaN(weight)) layer.defaultWeight = weight;

            layers[layerIndex] = layer;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            return $"Success: Configured layer[{layerIndex}] '{layer.name}' (blend={layer.blendingMode}, mask={layer.avatarMask?.name ?? "none"}).";
        }

        [AgentTool("Set the default state of a layer's state machine.")]
        public static string SetDefaultState(string controllerPath, string stateName, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range.";

            var sm = layers[layerIndex].stateMachine;
            var stateWrapper = sm.states.FirstOrDefault(s => s.state.name == stateName);
            if (stateWrapper.state == null)
                return $"Error: State '{stateName}' not found in layer[{layerIndex}].";

            Undo.RecordObject(sm, "Set Default State");
            sm.defaultState = stateWrapper.state;

            EditorUtility.SetDirty(controller);
            return $"Success: Set default state to '{stateName}' in layer[{layerIndex}].";
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static AnimatorState FindState(AnimatorController controller, string stateName, int layerIndex)
        {
            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length) return null;
            var sm = layers[layerIndex].stateMachine;
            return FindStateRecursive(sm, stateName);
        }

        private static AnimatorState FindStateRecursive(AnimatorStateMachine sm, string stateName)
        {
            var found = sm.states.FirstOrDefault(s => s.state.name == stateName);
            if (found.state != null) return found.state;

            foreach (var sub in sm.stateMachines)
            {
                var result = FindStateRecursive(sub.stateMachine, stateName);
                if (result != null) return result;
            }
            return null;
        }

        private static List<AnimatorState> GetAllStatesRecursive(AnimatorStateMachine sm)
        {
            var result = new List<AnimatorState>();
            foreach (var s in sm.states) result.Add(s.state);
            foreach (var sub in sm.stateMachines)
                result.AddRange(GetAllStatesRecursive(sub.stateMachine));
            return result;
        }

        private static bool TryParseBlendTreeType(string type, out BlendTreeType result)
        {
            switch (type.ToLower().Replace("_", ""))
            {
                case "simple1d": result = BlendTreeType.Simple1D; return true;
                case "simpledirectional2d": result = BlendTreeType.SimpleDirectional2D; return true;
                case "freeformdirectional2d": result = BlendTreeType.FreeformDirectional2D; return true;
                case "freeformcartesian2d": result = BlendTreeType.FreeformCartesian2D; return true;
                case "direct": result = BlendTreeType.Direct; return true;
                default: result = BlendTreeType.Simple1D; return false;
            }
        }

        private static bool Is2DBlendType(BlendTreeType type) =>
            type == BlendTreeType.SimpleDirectional2D || type == BlendTreeType.FreeformDirectional2D || type == BlendTreeType.FreeformCartesian2D;

        private static bool TryParseBodyPart(string name, out AvatarMaskBodyPart part)
        {
            switch (name.ToLower().Replace("_", "").Replace(" ", ""))
            {
                case "root": part = AvatarMaskBodyPart.Root; return true;
                case "body": part = AvatarMaskBodyPart.Body; return true;
                case "head": part = AvatarMaskBodyPart.Head; return true;
                case "leftleg": part = AvatarMaskBodyPart.LeftLeg; return true;
                case "rightleg": part = AvatarMaskBodyPart.RightLeg; return true;
                case "leftarm": part = AvatarMaskBodyPart.LeftArm; return true;
                case "rightarm": part = AvatarMaskBodyPart.RightArm; return true;
                case "leftfingers": part = AvatarMaskBodyPart.LeftFingers; return true;
                case "rightfingers": part = AvatarMaskBodyPart.RightFingers; return true;
                case "leftfootik": part = AvatarMaskBodyPart.LeftFootIK; return true;
                case "rightfootik": part = AvatarMaskBodyPart.RightFootIK; return true;
                case "lefthandik": part = AvatarMaskBodyPart.LeftHandIK; return true;
                case "righthandik": part = AvatarMaskBodyPart.RightHandIK; return true;
                default: part = AvatarMaskBodyPart.Root; return false;
            }
        }

        private static Vector2? ParseVector2(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 2) return null;
            if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            return null;
        }

        private static string FormatConditions(AnimatorCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0) return "no conditions";
            return string.Join(", ", conditions.Select(c =>
            {
                string op = c.mode switch
                {
                    AnimatorConditionMode.If => "=true",
                    AnimatorConditionMode.IfNot => "=false",
                    AnimatorConditionMode.Greater => $">{c.threshold:G}",
                    AnimatorConditionMode.Less => $"<{c.threshold:G}",
                    AnimatorConditionMode.Equals => $"=={c.threshold:G}",
                    AnimatorConditionMode.NotEqual => $"!={c.threshold:G}",
                    _ => "?"
                };
                return $"{c.parameter}{op}";
            }));
        }

        private struct ParsedCondition
        {
            public AnimatorConditionMode mode;
            public float threshold;
            public string parameter;
        }

        private static List<ParsedCondition> ParseConditions(string conditions, AnimatorController controller)
        {
            var result = new List<ParsedCondition>();
            var parts = conditions.Split(';');

            foreach (var part in parts)
            {
                string p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                var cond = new ParsedCondition();

                if (TryParseOp(p, "!=", out cond.parameter, out float v1))
                { cond.mode = AnimatorConditionMode.NotEqual; cond.threshold = v1; }
                else if (TryParseOp(p, "==", out cond.parameter, out float v2))
                { cond.mode = AnimatorConditionMode.Equals; cond.threshold = v2; }
                else if (TryParseOp(p, ">", out cond.parameter, out float v3))
                { cond.mode = AnimatorConditionMode.Greater; cond.threshold = v3; }
                else if (TryParseOp(p, "<", out cond.parameter, out float v4))
                { cond.mode = AnimatorConditionMode.Less; cond.threshold = v4; }
                else if (TryParseBool(p, out cond.parameter, out bool bv))
                { cond.mode = bv ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot; cond.threshold = 0; }
                else
                { cond.parameter = p; cond.mode = AnimatorConditionMode.If; cond.threshold = 0; }

                result.Add(cond);
            }
            return result;
        }

        private static bool TryParseOp(string input, string op, out string paramName, out float value)
        {
            int idx = input.IndexOf(op);
            if (idx > 0)
            {
                paramName = input.Substring(0, idx).Trim();
                if (float.TryParse(input.Substring(idx + op.Length).Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
            }
            paramName = ""; value = 0; return false;
        }

        private static bool TryParseBool(string input, out string paramName, out bool value)
        {
            int idx = input.IndexOf('=');
            if (idx > 0)
            {
                paramName = input.Substring(0, idx).Trim();
                string v = input.Substring(idx + 1).Trim().ToLower();
                if (v == "true") { value = true; return true; }
                if (v == "false") { value = false; return true; }
            }
            paramName = ""; value = false; return false;
        }
    }
}
