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
    public static class AnimatorTools
    {
        [AgentTool("Inspect an AnimatorController asset. Shows layers, parameters, states, and transitions. controllerPath is the asset path (e.g. 'Assets/...controller').")]
        public static string InspectAnimatorController(string controllerPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"AnimatorController: {controller.name}");

            // Parameters
            var parameters = controller.parameters;
            sb.AppendLine($"\nParameters ({parameters.Length}):");
            foreach (var p in parameters)
            {
                string defaultVal = p.type switch
                {
                    AnimatorControllerParameterType.Bool => p.defaultBool.ToString(),
                    AnimatorControllerParameterType.Int => p.defaultInt.ToString(),
                    AnimatorControllerParameterType.Float => p.defaultFloat.ToString("F2"),
                    AnimatorControllerParameterType.Trigger => "trigger",
                    _ => ""
                };
                sb.AppendLine($"  {p.name} ({p.type}) = {defaultVal}");
            }

            // Layers
            var layers = controller.layers;
            sb.AppendLine($"\nLayers ({layers.Length}):");
            for (int li = 0; li < layers.Length; li++)
            {
                var layer = layers[li];
                sb.AppendLine($"\n  Layer[{li}]: {layer.name} (weight={layer.defaultWeight:F2}, blending={layer.blendingMode})");

                var sm = layer.stateMachine;
                if (sm == null) continue;

                // Default state
                string defaultState = sm.defaultState != null ? sm.defaultState.name : "none";
                sb.AppendLine($"    Default State: {defaultState}");

                // States
                var states = sm.states;
                sb.AppendLine($"    States ({states.Length}):");
                foreach (var s in states)
                {
                    string motionName = s.state.motion != null ? s.state.motion.name : "none";
                    sb.AppendLine($"      - {s.state.name} (motion={motionName})");

                    // Transitions from this state
                    foreach (var t in s.state.transitions)
                    {
                        string destName = t.destinationState != null ? t.destinationState.name : "Exit";
                        string conditions = FormatConditions(t.conditions);
                        sb.AppendLine($"        -> {destName} [{conditions}] (hasExitTime={t.hasExitTime})");
                    }
                }

                // Any state transitions
                var anyTransitions = sm.anyStateTransitions;
                if (anyTransitions.Length > 0)
                {
                    sb.AppendLine($"    AnyState Transitions ({anyTransitions.Length}):");
                    foreach (var t in anyTransitions)
                    {
                        string destName = t.destinationState != null ? t.destinationState.name : "Exit";
                        string conditions = FormatConditions(t.conditions);
                        sb.AppendLine($"      Any -> {destName} [{conditions}]");
                    }
                }

                // Entry transitions
                var entryTransitions = sm.entryTransitions;
                if (entryTransitions.Length > 0)
                {
                    sb.AppendLine($"    Entry Transitions ({entryTransitions.Length}):");
                    foreach (var t in entryTransitions)
                    {
                        string destName = t.destinationState != null ? t.destinationState.name : "?";
                        string conditions = FormatConditions(t.conditions);
                        sb.AppendLine($"      Entry -> {destName} [{conditions}]");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Create a new empty AnimatorController at the specified path (e.g. 'Assets/Animations/MyController.controller').")]
        public static string CreateAnimatorController(string savePath)
        {
            if (!savePath.EndsWith(".controller"))
                savePath += ".controller";

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(savePath);
            if (controller == null) return $"Error: Failed to create AnimatorController at '{savePath}'.";

            AssetDatabase.SaveAssets();
            return $"Success: Created AnimatorController at '{savePath}'.";
        }

        [AgentTool("Add a parameter to an AnimatorController. type: bool, int, float, or trigger. defaultValue is optional.")]
        public static string AddAnimatorParameter(string controllerPath, string name, string type, string defaultValue = "")
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            // Check if parameter already exists
            if (controller.parameters.Any(p => p.name == name))
                return $"Info: Parameter '{name}' already exists in the controller.";

            AnimatorControllerParameterType paramType;
            switch (type.ToLower())
            {
                case "bool": paramType = AnimatorControllerParameterType.Bool; break;
                case "int": paramType = AnimatorControllerParameterType.Int; break;
                case "float": paramType = AnimatorControllerParameterType.Float; break;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; break;
                default: return $"Error: Unknown parameter type '{type}'. Valid: bool, int, float, trigger.";
            }

            Undo.RecordObject(controller, "Add Animator Parameter");
            controller.AddParameter(name, paramType);

            // Set default value if provided
            if (!string.IsNullOrEmpty(defaultValue))
            {
                var parameters = controller.parameters;
                var param = parameters.Last();
                switch (paramType)
                {
                    case AnimatorControllerParameterType.Bool:
                        param.defaultBool = defaultValue.ToLower() == "true";
                        break;
                    case AnimatorControllerParameterType.Int:
                        if (int.TryParse(defaultValue, out int intVal))
                            param.defaultInt = intVal;
                        break;
                    case AnimatorControllerParameterType.Float:
                        if (float.TryParse(defaultValue, out float floatVal))
                            param.defaultFloat = floatVal;
                        break;
                }
                controller.parameters = parameters;
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Added parameter '{name}' ({type}) to controller.";
        }

        [AgentTool("Add a state to an AnimatorController layer. motionPath is optional asset path to an AnimationClip. layerIndex defaults to 0.")]
        public static string AddAnimatorState(string controllerPath, string stateName, string motionPath = "", int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;

            // Check if state already exists
            if (sm.states.Any(s => s.state.name == stateName))
                return $"Info: State '{stateName}' already exists in layer[{layerIndex}].";

            Undo.RecordObject(sm, "Add Animator State");
            var state = sm.AddState(stateName);

            if (!string.IsNullOrEmpty(motionPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                if (clip != null)
                    state.motion = clip;
                else
                    return $"Warning: State '{stateName}' added but motion not found at '{motionPath}'.";
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Added state '{stateName}' to layer[{layerIndex}].";
        }

        [AgentTool("Add a transition between states. fromState can be 'Any' or 'Entry'. conditions: semicolon-separated, e.g. 'IsOpen=true;Speed>0.5;MyTrigger'. Operators: =true/=false (bool), >/< (float/int), bare name (trigger). Empty = exit time transition. layerIndex defaults to 0.")]
        public static string AddAnimatorTransition(string controllerPath, string fromState, string toState, string conditions = "", int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;

            // Find destination state
            var destStateWrapper = sm.states.FirstOrDefault(s => s.state.name == toState);
            if (destStateWrapper.state == null)
                return $"Error: Destination state '{toState}' not found in layer[{layerIndex}].";

            Undo.RecordObject(sm, "Add Animator Transition");

            AnimatorStateTransition transition;

            if (fromState.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                transition = sm.AddAnyStateTransition(destStateWrapper.state);
            }
            else if (fromState.Equals("Entry", StringComparison.OrdinalIgnoreCase))
            {
                var entryTransition = sm.AddEntryTransition(destStateWrapper.state);
                // Entry transitions have conditions but no full AnimatorStateTransition properties
                if (!string.IsNullOrEmpty(conditions))
                {
                    var parsedConditions = ParseConditions(conditions, controller);
                    foreach (var c in parsedConditions)
                        entryTransition.AddCondition(c.mode, c.threshold, c.parameter);
                }
                EditorUtility.SetDirty(controller);
                return $"Success: Added Entry -> '{toState}' transition in layer[{layerIndex}].";
            }
            else
            {
                var srcStateWrapper = sm.states.FirstOrDefault(s => s.state.name == fromState);
                if (srcStateWrapper.state == null)
                    return $"Error: Source state '{fromState}' not found in layer[{layerIndex}].";

                Undo.RecordObject(srcStateWrapper.state, "Add Transition");
                transition = srcStateWrapper.state.AddTransition(destStateWrapper.state);
            }

            // Apply conditions
            if (!string.IsNullOrEmpty(conditions))
            {
                transition.hasExitTime = false;
                var parsedConditions = ParseConditions(conditions, controller);
                foreach (var c in parsedConditions)
                    transition.AddCondition(c.mode, c.threshold, c.parameter);
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Added transition '{fromState}' -> '{toState}' in layer[{layerIndex}].";
        }

        [AgentTool("Set the motion (AnimationClip) for an existing state. motionPath is the asset path to the clip.")]
        public static string SetAnimatorStateMotion(string controllerPath, string stateName, string motionPath, int layerIndex = 0)
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

            var clip = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
            if (clip == null) return $"Error: Motion not found at '{motionPath}'.";

            Undo.RecordObject(stateWrapper.state, "Set State Motion");
            stateWrapper.state.motion = clip;

            EditorUtility.SetDirty(controller);
            return $"Success: Set motion of '{stateName}' to '{clip.name}'.";
        }

        [AgentTool("Remove a state from an AnimatorController layer. Requires user confirmation.")]
        public static string RemoveAnimatorState(string controllerPath, string stateName, int layerIndex = 0)
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

            if (!AgentSettings.RequestConfirmation(
                "Animator Stateを削除",
                $"'{stateName}' をレイヤー[{layerIndex}]から削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.RecordObject(sm, "Remove Animator State");
            sm.RemoveState(stateWrapper.state);

            EditorUtility.SetDirty(controller);
            return $"Success: Removed state '{stateName}' from layer[{layerIndex}].";
        }

        [AgentTool("Set the default weight of an AnimatorController layer.")]
        public static string SetAnimatorLayerWeight(string controllerPath, int layerIndex, float weight)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            Undo.RecordObject(controller, "Set Layer Weight");
            // layers is a copy; must reassign
            layers[layerIndex].defaultWeight = weight;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            return $"Success: Set layer[{layerIndex}] '{layers[layerIndex].name}' weight to {weight:F2}.";
        }

        [AgentTool("Add a new layer to an AnimatorController.")]
        public static string AddAnimatorLayer(string controllerPath, string layerName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            Undo.RecordObject(controller, "Add Animator Layer");
            controller.AddLayer(layerName);

            EditorUtility.SetDirty(controller);
            var layers = controller.layers;
            return $"Success: Added layer '{layerName}' (index={layers.Length - 1}) to controller.";
        }

        [AgentTool("Remove a layer from an AnimatorController by index. Removes the layer's StateMachine, all states, and transitions. Layer 0 is the base layer and cannot be removed. Requires user confirmation.")]
        public static string RemoveAnimatorLayer(string controllerPath, int layerIndex)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";
            if (layerIndex == 0)
                return "Error: Cannot remove layer[0] (base layer). Animator controllers require at least one layer.";

            string layerName = layers[layerIndex].name;
            int stateCount = layers[layerIndex].stateMachine != null ? layers[layerIndex].stateMachine.states.Length : 0;

            if (!AgentSettings.RequestConfirmation(
                "Animator Layerを削除",
                $"レイヤー[{layerIndex}] '{layerName}' (states={stateCount}) を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.RecordObject(controller, "Remove Animator Layer");
            controller.RemoveLayer(layerIndex);

            EditorUtility.SetDirty(controller);
            return $"Success: Removed layer[{layerIndex}] '{layerName}' from controller.";
        }

        [AgentTool("Remove a parameter from an AnimatorController by name. Note: this does not remove references in transitions or BlendTrees that already use the parameter. Requires user confirmation.")]
        public static string RemoveAnimatorParameter(string controllerPath, string parameterName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var parameters = controller.parameters;
            int idx = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName) { idx = i; break; }
            }
            if (idx < 0)
                return $"Error: Parameter '{parameterName}' not found in controller.";

            if (!AgentSettings.RequestConfirmation(
                "Animator Parameterを削除",
                $"パラメータ '{parameterName}' をコントローラーから削除します。\n注意: 既存の transition / BlendTree が参照していると無効な参照が残ります。"))
                return "Cancelled: User denied the removal.";

            Undo.RecordObject(controller, "Remove Animator Parameter");
            controller.RemoveParameter(idx);

            EditorUtility.SetDirty(controller);
            return $"Success: Removed parameter '{parameterName}' from controller.";
        }

        [AgentTool("Move a layer in an AnimatorController to a different index. Reordering changes execution priority — later layers override earlier ones for parameters with the same name (critical for VRChat FX). Layer 0 is the base layer; cannot move to or from index 0.")]
        public static string MoveAnimatorLayer(string controllerPath, int fromIndex, int toIndex)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var layers = controller.layers;
            if (fromIndex < 0 || fromIndex >= layers.Length)
                return $"Error: fromIndex {fromIndex} out of range (0-{layers.Length - 1}).";
            if (toIndex < 0 || toIndex >= layers.Length)
                return $"Error: toIndex {toIndex} out of range (0-{layers.Length - 1}).";
            if (fromIndex == 0 || toIndex == 0)
                return "Error: Layer 0 is the base layer and cannot be moved or replaced. Reorder layers 1..N only.";
            if (fromIndex == toIndex)
                return $"Info: Layer already at index {toIndex}, no change.";

            Undo.RecordObject(controller, "Move Animator Layer");
            var moved = layers[fromIndex];
            var list = new List<AnimatorControllerLayer>(layers);
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, moved);
            controller.layers = list.ToArray();

            EditorUtility.SetDirty(controller);
            return $"Success: Moved layer '{moved.name}' from index {fromIndex} to {toIndex}.";
        }

        [AgentTool("Rename an AnimatorController layer.")]
        public static string RenameAnimatorLayer(string controllerPath, int layerIndex, string newName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";
            if (string.IsNullOrEmpty(newName))
                return "Error: newName must not be empty.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            for (int i = 0; i < layers.Length; i++)
            {
                if (i != layerIndex && layers[i].name == newName)
                    return $"Error: Layer name '{newName}' already exists at index {i}.";
            }

            string oldName = layers[layerIndex].name;
            if (oldName == newName)
                return $"Info: Layer name unchanged ('{newName}').";

            Undo.RecordObject(controller, "Rename Animator Layer");
            layers[layerIndex].name = newName;
            controller.layers = layers; // layers is a copy — reassign

            EditorUtility.SetDirty(controller);
            return $"Success: Renamed layer[{layerIndex}] '{oldName}' to '{newName}'.";
        }

        [AgentTool("Rename an AnimatorState within a layer. Internal references (transitions, default state) update automatically since Unity tracks states by reference, not by name.")]
        public static string RenameAnimatorState(string controllerPath, string stateName, string newName, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";
            if (string.IsNullOrEmpty(newName))
                return "Error: newName must not be empty.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;
            var wrapper = sm.states.FirstOrDefault(s => s.state.name == stateName);
            if (wrapper.state == null)
                return $"Error: State '{stateName}' not found in layer[{layerIndex}].";
            if (stateName == newName)
                return $"Info: State name unchanged ('{newName}').";
            if (sm.states.Any(s => s.state.name == newName))
                return $"Error: State '{newName}' already exists in layer[{layerIndex}].";

            Undo.RecordObject(wrapper.state, "Rename Animator State");
            wrapper.state.name = newName;

            EditorUtility.SetDirty(controller);
            return $"Success: Renamed state '{stateName}' to '{newName}' in layer[{layerIndex}].";
        }

        [AgentTool("Rename an AnimatorController parameter. Auto-updates transition conditions and BlendTree parameter references within this controller. External references (animation clips, VRC ExpressionParameters) are NOT updated.")]
        public static string RenameAnimatorParameter(string controllerPath, string oldName, string newName)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";
            if (string.IsNullOrEmpty(newName))
                return "Error: newName must not be empty.";
            if (oldName == newName)
                return $"Info: Parameter name unchanged ('{newName}').";

            var parameters = controller.parameters;
            int idx = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == oldName) idx = i;
                else if (parameters[i].name == newName)
                    return $"Error: Parameter '{newName}' already exists.";
            }
            if (idx < 0)
                return $"Error: Parameter '{oldName}' not found.";

            Undo.RecordObject(controller, "Rename Animator Parameter");
            parameters[idx].name = newName;
            controller.parameters = parameters;

            int fixedConditions = 0;
            int fixedBlendTrees = 0;
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                UpdateConditionsRecursive(layer.stateMachine, oldName, newName, ref fixedConditions);
                UpdateBlendTreeParametersRecursive(layer.stateMachine, oldName, newName, ref fixedBlendTrees);
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Renamed parameter '{oldName}' to '{newName}'. Updated {fixedConditions} transition condition(s), {fixedBlendTrees} BlendTree parameter reference(s).";
        }

        [AgentTool("Set the default value of an existing AnimatorController parameter. defaultValue: 'true'/'false' for bool, integer for int, decimal for float. Trigger params have no default and return an info message.")]
        public static string SetAnimatorParameterDefault(string controllerPath, string parameterName, string defaultValue)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";

            var parameters = controller.parameters;
            int idx = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName) { idx = i; break; }
            }
            if (idx < 0)
                return $"Error: Parameter '{parameterName}' not found.";

            var p = parameters[idx];
            string oldVal;
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                    oldVal = p.defaultBool.ToString();
                    string lower = (defaultValue ?? "").Trim().ToLower();
                    if (lower != "true" && lower != "false" && lower != "0" && lower != "1")
                        return $"Error: Bool default must be 'true' or 'false'. Got '{defaultValue}'.";
                    p.defaultBool = (lower == "true" || lower == "1");
                    break;
                case AnimatorControllerParameterType.Int:
                    oldVal = p.defaultInt.ToString();
                    if (!int.TryParse(defaultValue, out int iVal))
                        return $"Error: '{defaultValue}' is not a valid integer.";
                    p.defaultInt = iVal;
                    break;
                case AnimatorControllerParameterType.Float:
                    oldVal = p.defaultFloat.ToString("F3");
                    if (!float.TryParse(defaultValue, out float fVal))
                        return $"Error: '{defaultValue}' is not a valid float.";
                    p.defaultFloat = fVal;
                    break;
                case AnimatorControllerParameterType.Trigger:
                    return "Info: Trigger parameters do not have default values; nothing to set.";
                default:
                    return $"Error: Unknown parameter type {p.type}.";
            }

            Undo.RecordObject(controller, "Set Animator Parameter Default");
            controller.parameters = parameters;
            EditorUtility.SetDirty(controller);
            return $"Success: Set '{parameterName}' default: {oldVal} → {defaultValue}.";
        }

        [AgentTool("Duplicate an AnimatorState within the same layer. Copies motion, speed, writeDefaults, mirror, IK foot, tag, transitions FROM the state, and state behaviours. Useful for L/R symmetric setups.")]
        public static string DuplicateAnimatorState(string controllerPath, string stateName, string newStateName, int layerIndex = 0)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) return $"Error: AnimatorController not found at '{controllerPath}'.";
            if (string.IsNullOrEmpty(newStateName))
                return "Error: newStateName must not be empty.";

            var layers = controller.layers;
            if (layerIndex < 0 || layerIndex >= layers.Length)
                return $"Error: Layer index {layerIndex} out of range (0-{layers.Length - 1}).";

            var sm = layers[layerIndex].stateMachine;
            var srcWrapper = sm.states.FirstOrDefault(s => s.state.name == stateName);
            if (srcWrapper.state == null)
                return $"Error: State '{stateName}' not found in layer[{layerIndex}].";
            if (sm.states.Any(s => s.state.name == newStateName))
                return $"Error: State '{newStateName}' already exists in layer[{layerIndex}].";

            Undo.RecordObject(sm, "Duplicate Animator State");
            var src = srcWrapper.state;
            var dst = sm.AddState(newStateName);
            dst.motion = src.motion;
            dst.speed = src.speed;
            dst.cycleOffset = src.cycleOffset;
            dst.iKOnFeet = src.iKOnFeet;
            dst.mirror = src.mirror;
            dst.writeDefaultValues = src.writeDefaultValues;
            dst.tag = src.tag;

            // Copy transitions FROM the state
            int copiedTransitions = 0;
            foreach (var t in src.transitions)
            {
                var newT = (t.destinationState != null)
                    ? dst.AddTransition(t.destinationState, t.hasExitTime)
                    : dst.AddExitTransition();
                newT.duration = t.duration;
                newT.offset = t.offset;
                newT.interruptionSource = t.interruptionSource;
                newT.orderedInterruption = t.orderedInterruption;
                newT.canTransitionToSelf = t.canTransitionToSelf;
                newT.exitTime = t.exitTime;
                newT.hasExitTime = t.hasExitTime;
                foreach (var c in t.conditions)
                    newT.AddCondition(c.mode, c.threshold, c.parameter);
                copiedTransitions++;
            }

            // Copy state behaviours (deep copy via Instantiate + AddObjectToAsset)
            int copiedBehaviours = 0;
            if (src.behaviours != null && src.behaviours.Length > 0)
            {
                var newBehaviours = new List<StateMachineBehaviour>();
                foreach (var b in src.behaviours)
                {
                    if (b == null) continue;
                    var copy = ScriptableObject.Instantiate(b);
                    copy.name = b.GetType().Name;
                    AssetDatabase.AddObjectToAsset(copy, controller);
                    copy.hideFlags = HideFlags.HideInHierarchy;
                    newBehaviours.Add(copy);
                    copiedBehaviours++;
                }
                dst.behaviours = newBehaviours.ToArray();
            }

            EditorUtility.SetDirty(controller);
            return $"Success: Duplicated state '{stateName}' as '{newStateName}' in layer[{layerIndex}] " +
                   $"(transitions={copiedTransitions}, behaviours={copiedBehaviours}).";
        }

        // --- Helpers ---

        private static void UpdateConditionsRecursive(AnimatorStateMachine sm, string oldName, string newName, ref int count)
        {
            foreach (var s in sm.states)
                UpdateStateTransitionConditions(s.state.transitions, oldName, newName, ref count);
            UpdateStateTransitionConditions(sm.anyStateTransitions, oldName, newName, ref count);
            UpdateBaseTransitionConditions(sm.entryTransitions, oldName, newName, ref count);
            foreach (var sub in sm.stateMachines)
                UpdateConditionsRecursive(sub.stateMachine, oldName, newName, ref count);
        }

        private static void UpdateStateTransitionConditions(AnimatorStateTransition[] transitions, string oldName, string newName, ref int count)
        {
            foreach (var t in transitions)
            {
                var conditions = t.conditions;
                bool changed = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter == oldName)
                    {
                        conditions[i].parameter = newName;
                        changed = true;
                        count++;
                    }
                }
                if (changed) t.conditions = conditions;
            }
        }

        private static void UpdateBaseTransitionConditions(AnimatorTransition[] transitions, string oldName, string newName, ref int count)
        {
            foreach (var t in transitions)
            {
                var conditions = t.conditions;
                bool changed = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter == oldName)
                    {
                        conditions[i].parameter = newName;
                        changed = true;
                        count++;
                    }
                }
                if (changed) t.conditions = conditions;
            }
        }

        private static void UpdateBlendTreeParametersRecursive(AnimatorStateMachine sm, string oldName, string newName, ref int count)
        {
            foreach (var s in sm.states)
            {
                if (s.state.motion is BlendTree bt)
                    UpdateBlendTreeParameter(bt, oldName, newName, ref count);
            }
            foreach (var sub in sm.stateMachines)
                UpdateBlendTreeParametersRecursive(sub.stateMachine, oldName, newName, ref count);
        }

        private static void UpdateBlendTreeParameter(BlendTree bt, string oldName, string newName, ref int count)
        {
            if (bt.blendParameter == oldName) { bt.blendParameter = newName; count++; }
            if (bt.blendParameterY == oldName) { bt.blendParameterY = newName; count++; }

            var children = bt.children;
            bool changed = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].directBlendParameter == oldName)
                {
                    children[i].directBlendParameter = newName;
                    count++;
                    changed = true;
                }
                if (children[i].motion is BlendTree childBt)
                    UpdateBlendTreeParameter(childBt, oldName, newName, ref count);
            }
            if (changed) bt.children = children;
        }

        // --- Existing helpers below ---

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

                // Try operators in order of specificity
                if (TryParseCondition(p, "!=", out cond.parameter, out float val1))
                {
                    cond.mode = AnimatorConditionMode.NotEqual;
                    cond.threshold = val1;
                }
                else if (TryParseCondition(p, "==", out cond.parameter, out float val2))
                {
                    cond.mode = AnimatorConditionMode.Equals;
                    cond.threshold = val2;
                }
                else if (TryParseCondition(p, ">=", out cond.parameter, out float val3))
                {
                    // Approximate >= with >
                    cond.mode = AnimatorConditionMode.Greater;
                    cond.threshold = val3 - 0.001f;
                }
                else if (TryParseCondition(p, "<=", out cond.parameter, out float val4))
                {
                    // Approximate <= with <
                    cond.mode = AnimatorConditionMode.Less;
                    cond.threshold = val4 + 0.001f;
                }
                else if (TryParseCondition(p, ">", out cond.parameter, out float val5))
                {
                    cond.mode = AnimatorConditionMode.Greater;
                    cond.threshold = val5;
                }
                else if (TryParseCondition(p, "<", out cond.parameter, out float val6))
                {
                    cond.mode = AnimatorConditionMode.Less;
                    cond.threshold = val6;
                }
                else if (TryParseBoolCondition(p, out cond.parameter, out bool boolVal))
                {
                    cond.mode = boolVal ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                    cond.threshold = 0;
                }
                else
                {
                    // Assume it's a trigger name
                    cond.parameter = p;
                    cond.mode = AnimatorConditionMode.If;
                    cond.threshold = 0;
                }

                result.Add(cond);
            }

            return result;
        }

        private static bool TryParseCondition(string input, string op, out string paramName, out float value)
        {
            int idx = input.IndexOf(op);
            if (idx > 0)
            {
                paramName = input.Substring(0, idx).Trim();
                string valStr = input.Substring(idx + op.Length).Trim();
                if (float.TryParse(valStr, out value))
                    return true;
            }
            paramName = "";
            value = 0;
            return false;
        }

        private static bool TryParseBoolCondition(string input, out string paramName, out bool value)
        {
            int idx = input.IndexOf('=');
            if (idx > 0)
            {
                paramName = input.Substring(0, idx).Trim();
                string valStr = input.Substring(idx + 1).Trim().ToLower();
                if (valStr == "true") { value = true; return true; }
                if (valStr == "false") { value = false; return true; }
            }
            paramName = "";
            value = false;
            return false;
        }
    }
}
