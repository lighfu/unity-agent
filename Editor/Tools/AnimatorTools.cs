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

        // --- Helpers ---

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
