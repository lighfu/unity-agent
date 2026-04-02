using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class ExpressionParameterTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Set the sync type and flags for an expression parameter. syncType: 0=Int, 1=Float, 2=Bool. saved: persist between sessions. synced: network sync (costs bits).")]
        public static string SetParameterSync(string avatarRootName, string parameterName,
            int syncType = -1, int saved = -1, int synced = -1, float defaultValue = -999f)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot access parameters array.";

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name");
                if (name == null || name.stringValue != parameterName) continue;

                if (syncType >= 0)
                {
                    var vt = param.FindPropertyRelative("valueType");
                    if (vt != null) vt.intValue = syncType;
                }
                if (saved >= 0)
                {
                    var s = param.FindPropertyRelative("saved");
                    if (s != null) s.boolValue = saved != 0;
                }
                if (synced >= 0)
                {
                    var ns = param.FindPropertyRelative("networkSynced");
                    if (ns != null) ns.boolValue = synced != 0;
                }
                if (defaultValue > -999f)
                {
                    var dv = param.FindPropertyRelative("defaultValue");
                    if (dv != null) dv.floatValue = defaultValue;
                }

                paramsSo.ApplyModifiedProperties();

                string[] typeNames = { "Int", "Float", "Bool" };
                var vtFinal = param.FindPropertyRelative("valueType");
                string typeStr = vtFinal != null && vtFinal.intValue >= 0 && vtFinal.intValue < 3
                    ? typeNames[vtFinal.intValue] : "?";
                return $"Success: Updated parameter '{parameterName}' (type={typeStr}).";
            }

            return $"Error: Parameter '{parameterName}' not found in ExpressionParameters.";
        }

        [AgentTool("Batch set sync/saved flags for multiple parameters. parameterNames: comma-separated list.")]
        public static string BatchConfigureParameters(string avatarRootName, string parameterNames,
            int synced = -1, int saved = -1)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot access parameters array.";

            var names = parameterNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim()).ToHashSet();

            int updated = 0;
            var notFound = new List<string>(names);

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name) || !names.Contains(name)) continue;

                notFound.Remove(name);

                if (synced >= 0)
                {
                    var ns = param.FindPropertyRelative("networkSynced");
                    if (ns != null) ns.boolValue = synced != 0;
                }
                if (saved >= 0)
                {
                    var s = param.FindPropertyRelative("saved");
                    if (s != null) s.boolValue = saved != 0;
                }
                updated++;
            }

            paramsSo.ApplyModifiedProperties();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Updated {updated} parameter(s).");
            if (notFound.Count > 0)
                sb.AppendLine($"  Not found: {string.Join(", ", notFound)}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Find expression parameters not used in the FX controller or expression menus. Helps identify waste in parameter budget.")]
        public static string FindUnusedParameters(string avatarRootName)
        {
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                return go == null
                    ? $"Error: GameObject '{avatarRootName}' not found."
                    : $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";
            }

            var dso = new SerializedObject(descriptor);

            // Get defined parameters
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return "Error: No ExpressionParameters assigned.";

            var paramsSo = new SerializedObject(exprParamsProp.objectReferenceValue);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            var definedParams = new Dictionary<string, int>(); // name -> valueType
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                var vt = param.FindPropertyRelative("valueType");
                if (!string.IsNullOrEmpty(name))
                    definedParams[name] = vt?.intValue ?? -1;
            }

            // Collect parameters used in FX controller
            var usedInFX = new HashSet<string>();
            var baseLayers = dso.FindProperty("baseAnimationLayers");
            if (baseLayers != null && baseLayers.isArray)
            {
                for (int i = 0; i < baseLayers.arraySize; i++)
                {
                    var layer = baseLayers.GetArrayElementAtIndex(i);
                    var ac = layer.FindPropertyRelative("animatorController");
                    if (ac?.objectReferenceValue is AnimatorController controller)
                    {
                        foreach (var p in controller.parameters)
                            usedInFX.Add(p.name);
                    }
                }
            }

            // Collect parameters used in menus
            var usedInMenu = new HashSet<string>();
            var menuProp = dso.FindProperty("expressionsMenu");
            if (menuProp?.objectReferenceValue != null)
                CollectMenuParameters(menuProp.objectReferenceValue, usedInMenu, 0, 8);

            // Find unused
            var unused = new List<string>();
            var usedOnlyInMenu = new List<string>();
            var usedOnlyInFX = new List<string>();

            foreach (var kvp in definedParams)
            {
                // Skip VRChat built-in parameters
                if (IsBuiltInParameter(kvp.Key)) continue;

                bool inFX = usedInFX.Contains(kvp.Key);
                bool inMenu = usedInMenu.Contains(kvp.Key);

                if (!inFX && !inMenu)
                    unused.Add(kvp.Key);
                else if (!inFX && inMenu)
                    usedOnlyInMenu.Add(kvp.Key);
                else if (inFX && !inMenu)
                    usedOnlyInFX.Add(kvp.Key);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Parameter usage analysis for '{avatarRootName}':");
            sb.AppendLine($"  Total defined: {definedParams.Count}");

            if (unused.Count > 0)
            {
                sb.AppendLine($"\n  Unused parameters ({unused.Count}) - not in FX or menu:");
                foreach (var p in unused)
                {
                    string[] typeNames = { "Int", "Float", "Bool" };
                    int vt = definedParams[p];
                    string typeStr = vt >= 0 && vt < 3 ? typeNames[vt] : "?";
                    int cost = vt == 2 ? 1 : 8;
                    sb.AppendLine($"    {p} ({typeStr}, {cost}bit) - SAFE TO REMOVE");
                }
            }

            if (usedOnlyInMenu.Count > 0)
            {
                sb.AppendLine($"\n  In menu only ({usedOnlyInMenu.Count}) - not referenced in FX:");
                foreach (var p in usedOnlyInMenu)
                    sb.AppendLine($"    {p}");
            }

            if (usedOnlyInFX.Count > 0)
            {
                sb.AppendLine($"\n  In FX only ({usedOnlyInFX.Count}) - not in menu:");
                foreach (var p in usedOnlyInFX)
                    sb.AppendLine($"    {p}");
            }

            if (unused.Count == 0 && usedOnlyInMenu.Count == 0)
                sb.AppendLine("\n  All parameters are in use.");

            // Calculate potential savings
            if (unused.Count > 0)
            {
                int saveBits = 0;
                foreach (var p in unused)
                {
                    var paramSo2 = paramsSo;
                    for (int i = 0; i < parameters.arraySize; i++)
                    {
                        var param = parameters.GetArrayElementAtIndex(i);
                        if (param.FindPropertyRelative("name")?.stringValue == p)
                        {
                            var ns = param.FindPropertyRelative("networkSynced");
                            if (ns != null && ns.boolValue)
                            {
                                int vt = definedParams[p];
                                saveBits += vt == 2 ? 1 : 8;
                            }
                            break;
                        }
                    }
                }
                if (saveBits > 0)
                    sb.AppendLine($"\n  Potential savings: {saveBits} bits by removing unused synced parameters.");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Find parameters used in expression menus but not defined in ExpressionParameters.")]
        public static string FindUndefinedMenuParameters(string avatarRootName)
        {
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                return go == null
                    ? $"Error: GameObject '{avatarRootName}' not found."
                    : $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";
            }

            var dso = new SerializedObject(descriptor);

            // Get defined parameters
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return "Error: No ExpressionParameters assigned.";

            var paramsSo = new SerializedObject(exprParamsProp.objectReferenceValue);
            var parameters = paramsSo.FindProperty("parameters");
            var definedNames = new HashSet<string>();
            if (parameters != null)
            {
                for (int i = 0; i < parameters.arraySize; i++)
                {
                    var name = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                    if (!string.IsNullOrEmpty(name)) definedNames.Add(name);
                }
            }

            // Collect menu parameters
            var menuProp = dso.FindProperty("expressionsMenu");
            if (menuProp?.objectReferenceValue == null) return "No expression menu assigned.";

            var menuParams = new HashSet<string>();
            CollectMenuParameters(menuProp.objectReferenceValue, menuParams, 0, 8);

            var undefined = menuParams.Where(p => !string.IsNullOrEmpty(p) && !definedNames.Contains(p)).ToList();

            if (undefined.Count == 0) return "All menu parameters are properly defined.";

            var sb = new StringBuilder();
            sb.AppendLine($"Undefined parameters ({undefined.Count}) - used in menu but not in ExpressionParameters:");
            foreach (var p in undefined)
                sb.AppendLine($"  [Error] {p}");
            sb.AppendLine("\nThese parameters will not work until added to ExpressionParameters.");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Suggest optimizations for the parameter budget. Identifies parameters that could be unsynced or have reduced types.")]
        public static string OptimizeParameterBudget(string avatarRootName)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            var sb = new StringBuilder();
            sb.AppendLine($"Parameter optimization suggestions for '{avatarRootName}':");

            int totalBits = 0;
            int syncedBools = 0, syncedInts = 0, syncedFloats = 0;
            var suggestions = new List<string>();

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name)) continue;

                var vt = param.FindPropertyRelative("valueType")?.intValue ?? -1;
                var synced = param.FindPropertyRelative("networkSynced");
                var saved = param.FindPropertyRelative("saved");
                var defVal = param.FindPropertyRelative("defaultValue")?.floatValue ?? 0f;

                bool isSynced = synced?.boolValue ?? false;
                if (!isSynced) continue;

                int cost = vt == 2 ? 1 : 8;
                totalBits += cost;

                switch (vt)
                {
                    case 0: syncedInts++; break;
                    case 1: syncedFloats++; break;
                    case 2: syncedBools++; break;
                }

                // Suggest: Int used as toggle (0/1) -> could be Bool
                if (vt == 0 && (defVal == 0f || defVal == 1f))
                    suggestions.Add($"  '{name}' is Int but default is {defVal:F0}. If used as toggle, switch to Bool to save 7 bits.");

                // Suggest: Float with 0 default, might be just on/off
                if (vt == 1 && defVal == 0f)
                    suggestions.Add($"  '{name}' is synced Float (8bit). If it's a toggle, switch to Bool (1bit) to save 7 bits.");
            }

            sb.AppendLine($"  Current usage: {totalBits}/256 bits ({totalBits / 256f * 100f:F1}%)");
            sb.AppendLine($"  Synced: {syncedBools} Bool ({syncedBools}bit), {syncedInts} Int ({syncedInts * 8}bit), {syncedFloats} Float ({syncedFloats * 8}bit)");

            if (suggestions.Count > 0)
            {
                sb.AppendLine($"\nSuggestions ({suggestions.Count}):");
                foreach (var s in suggestions)
                    sb.AppendLine(s);
            }
            else
            {
                sb.AppendLine("\n  No optimization suggestions. Parameters look well configured.");
            }

            sb.AppendLine($"\n  Remaining: {256 - totalBits} bits");
            return sb.ToString().TrimEnd();
        }

        // ===== Helpers =====

        private static SerializedObject GetExpressionParametersSO(string avatarRootName, out string error)
        {
            error = null;
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                error = go == null
                    ? $"Error: GameObject '{avatarRootName}' not found."
                    : $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";
                return null;
            }

            var dso = new SerializedObject(descriptor);
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
            {
                error = "Error: No ExpressionParameters assigned.";
                return null;
            }

            return new SerializedObject(exprParamsProp.objectReferenceValue);
        }

        private static bool IsBuiltInParameter(string name)
        {
            // VRChat built-in parameters that don't count toward budget
            switch (name)
            {
                case "IsLocal":
                case "Viseme":
                case "Voice":
                case "GestureLeft":
                case "GestureRight":
                case "GestureLeftWeight":
                case "GestureRightWeight":
                case "AngularY":
                case "VelocityX":
                case "VelocityY":
                case "VelocityZ":
                case "Upright":
                case "Grounded":
                case "Seated":
                case "AFK":
                case "TrackingType":
                case "VRMode":
                case "MuteSelf":
                case "InStation":
                case "Earmuffs":
                case "IsOnFriendsList":
                case "AvatarVersion":
                case "ScaleModified":
                case "ScaleFactor":
                case "ScaleFactorInverse":
                case "EyeHeightAsMeters":
                case "EyeHeightAsPercent":
                    return true;
                default:
                    return false;
            }
        }

        private static void CollectMenuParameters(UnityEngine.Object menuObj, HashSet<string> result, int depth, int maxDepth)
        {
            if (menuObj == null || depth > maxDepth) return;

            var menuSo = new SerializedObject(menuObj);
            var controls = menuSo.FindProperty("controls");
            if (controls == null || !controls.isArray) return;

            for (int i = 0; i < controls.arraySize; i++)
            {
                var control = controls.GetArrayElementAtIndex(i);
                var parameter = control.FindPropertyRelative("parameter");
                if (parameter != null)
                {
                    var paramName = parameter.FindPropertyRelative("name");
                    if (paramName != null && !string.IsNullOrEmpty(paramName.stringValue))
                        result.Add(paramName.stringValue);
                }

                var subParams = control.FindPropertyRelative("subParameters");
                if (subParams != null && subParams.isArray)
                {
                    for (int j = 0; j < subParams.arraySize; j++)
                    {
                        var sp = subParams.GetArrayElementAtIndex(j);
                        var spName = sp.FindPropertyRelative("name");
                        if (spName != null && !string.IsNullOrEmpty(spName.stringValue))
                            result.Add(spName.stringValue);
                    }
                }

                var controlType = control.FindPropertyRelative("type");
                if (controlType != null && controlType.intValue == 103) // SubMenu
                {
                    var subMenu = control.FindPropertyRelative("subMenu");
                    if (subMenu?.objectReferenceValue != null)
                        CollectMenuParameters(subMenu.objectReferenceValue, result, depth + 1, maxDepth);
                }
            }
        }
    }
}
