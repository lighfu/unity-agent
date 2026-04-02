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
    public static class AvatarValidationTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        // ========== Helpers ==========

        private static Component FindDescriptor(string avatarRootName, out string error)
        {
            error = null;
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                if (go == null)
                    error = $"Error: GameObject '{avatarRootName}' not found.";
                else
                    error = $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";
            }
            return descriptor;
        }

        private static AnimatorController GetFXController(SerializedObject descriptorSo)
        {
            var baseLayers = descriptorSo.FindProperty("baseAnimationLayers");
            if (baseLayers == null || !baseLayers.isArray) return null;

            for (int i = 0; i < baseLayers.arraySize; i++)
            {
                var layer = baseLayers.GetArrayElementAtIndex(i);
                var layerType = layer.FindPropertyRelative("type");
                if (layerType != null && layerType.intValue == 5) // FX = 5
                {
                    var animController = layer.FindPropertyRelative("animatorController");
                    if (animController != null && animController.objectReferenceValue is AnimatorController ac)
                        return ac;
                }
            }
            return null;
        }

        // ========== 1. ValidateAvatar ==========

        [AgentTool("Comprehensive avatar validation: performance rank, parameter bit budget (256 max), Write Defaults consistency, missing references, expression menu issues. Returns categorized issues with severity.")]
        public static string ValidateAvatar(string avatarRootName)
        {
            var descriptor = FindDescriptor(avatarRootName, out string err);
            if (descriptor == null) return err;

            var go = descriptor.gameObject;
            var so = new SerializedObject(descriptor);
            var issues = new List<(string severity, string message)>();

            // === Write Defaults Check ===
            var fxController = GetFXController(so);
            if (fxController != null)
            {
                int wdOn = 0, wdOff = 0;
                foreach (var layer in fxController.layers)
                {
                    if (layer.stateMachine == null) continue;
                    foreach (var childState in layer.stateMachine.states)
                    {
                        if (childState.state.writeDefaultValues)
                            wdOn++;
                        else
                            wdOff++;
                    }
                }
                if (wdOn > 0 && wdOff > 0)
                    issues.Add(("Error", $"Write Defaults inconsistent: {wdOn} states ON, {wdOff} states OFF. Must be uniform across FX controller."));
            }

            // === Parameter Budget ===
            var exprParamsProp = so.FindProperty("expressionParameters");
            if (exprParamsProp != null && exprParamsProp.objectReferenceValue != null)
            {
                var paramsSo = new SerializedObject(exprParamsProp.objectReferenceValue);
                var parameters = paramsSo.FindProperty("parameters");
                if (parameters != null && parameters.isArray)
                {
                    int totalBits = 0;
                    var definedParams = new HashSet<string>();
                    for (int i = 0; i < parameters.arraySize; i++)
                    {
                        var param = parameters.GetArrayElementAtIndex(i);
                        var paramName = param.FindPropertyRelative("name")?.stringValue;
                        if (string.IsNullOrEmpty(paramName)) continue;
                        definedParams.Add(paramName);

                        var synced = param.FindPropertyRelative("networkSynced");
                        if (synced != null && synced.boolValue)
                        {
                            var valueType = param.FindPropertyRelative("valueType");
                            if (valueType != null)
                                totalBits += valueType.intValue == 2 ? 1 : 8; // Bool=1, Int/Float=8
                        }
                    }

                    float usage = totalBits / 256f * 100f;
                    if (totalBits > 256)
                        issues.Add(("Error", $"Parameter budget exceeded: {totalBits}/256 bits ({usage:F0}%)."));
                    else if (usage > 90)
                        issues.Add(("Warning", $"Parameter budget nearly full: {totalBits}/256 bits ({usage:F0}%)."));
                    else
                        issues.Add(("Info", $"Parameter budget: {totalBits}/256 bits ({usage:F0}%)."));

                    // Check menu parameters are defined
                    var menuProp = so.FindProperty("expressionsMenu");
                    if (menuProp != null && menuProp.objectReferenceValue != null)
                    {
                        var menuParams = new HashSet<string>();
                        CollectMenuParameters(menuProp.objectReferenceValue, menuParams, 0, 5);
                        foreach (var mp in menuParams)
                        {
                            if (!string.IsNullOrEmpty(mp) && !definedParams.Contains(mp))
                                issues.Add(("Error", $"Parameter '{mp}' used in menu but not defined in ExpressionParameters."));
                        }
                    }
                }
            }
            else
            {
                issues.Add(("Warning", "No ExpressionParameters assigned."));
            }

            // === Menu Check ===
            var menuCheck = so.FindProperty("expressionsMenu");
            if (menuCheck != null && menuCheck.objectReferenceValue != null)
            {
                var menuSo = new SerializedObject(menuCheck.objectReferenceValue);
                var controls = menuSo.FindProperty("controls");
                if (controls != null && controls.isArray && controls.arraySize > 8)
                    issues.Add(("Warning", $"Root menu has {controls.arraySize} controls (max 8)."));
            }
            else
            {
                issues.Add(("Warning", "No ExpressionsMenu assigned."));
            }

            // === Missing References ===
            int missingCount = 0;
            var allComponents = go.GetComponentsInChildren<Component>(true);
            foreach (var comp in allComponents)
            {
                if (comp == null) { missingCount++; continue; }
                var compSo = new SerializedObject(comp);
                var iter = compSo.GetIterator();
                while (iter.NextVisible(true))
                {
                    if (iter.propertyType == SerializedPropertyType.ObjectReference &&
                        iter.objectReferenceValue == null &&
                        iter.objectReferenceInstanceIDValue != 0)
                    {
                        missingCount++;
                        break; // one per component is enough
                    }
                }
            }
            if (missingCount > 0)
                issues.Add(("Error", $"{missingCount} component(s) with missing references."));

            // === Performance: polygon count ===
            int totalPolygons = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) totalPolygons += smr.sharedMesh.triangles.Length / 3;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) totalPolygons += mf.sharedMesh.triangles.Length / 3;

            if (totalPolygons > 70000)
                issues.Add(("Warning", $"High polygon count: {totalPolygons:N0} (Very Poor on PC, over Quest limit)."));
            else if (totalPolygons > 32000)
                issues.Add(("Warning", $"Polygon count: {totalPolygons:N0} (Poor on PC)."));
            else
                issues.Add(("Info", $"Polygon count: {totalPolygons:N0}."));

            // === AAO TraceAndOptimize check ===
#if AVATAR_OPTIMIZER
            var tao = go.GetComponent<Anatawa12.AvatarOptimizer.TraceAndOptimize>();
            issues.Add(tao != null
                ? ("Info", "AAO TraceAndOptimize: Present.")
                : ("Info", "AAO TraceAndOptimize: Not found. Consider adding for automatic optimization."));
#endif

            // === Non-lilToon shader check ===
            var nonLilMats = new List<string>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    var shaderName = m.shader.name;
                    if (!shaderName.Contains("lilToon") && !shaderName.Contains("liltoon") && !shaderName.Contains("lil/")
                        && !shaderName.Contains("Standard") && !shaderName.Contains("VRChat"))
                    {
                        if (!nonLilMats.Contains($"{m.name} ({shaderName})"))
                            nonLilMats.Add($"{m.name} ({shaderName})");
                    }
                }
            }
            if (nonLilMats.Count > 0)
                issues.Add(("Info", $"Non-standard shaders: {string.Join(", ", nonLilMats.Take(5))}" + (nonLilMats.Count > 5 ? $" +{nonLilMats.Count - 5} more" : "")));

            // === Build Report ===
            var sb = new StringBuilder();
            sb.AppendLine($"Avatar Validation Report for '{avatarRootName}':");
            sb.AppendLine();

            var errors = issues.Where(i => i.severity == "Error").ToList();
            var warnings = issues.Where(i => i.severity == "Warning").ToList();
            var infos = issues.Where(i => i.severity == "Info").ToList();

            if (errors.Count > 0)
            {
                sb.AppendLine($"Errors ({errors.Count}):");
                foreach (var (_, msg) in errors)
                    sb.AppendLine($"  [Error] {msg}");
                sb.AppendLine();
            }
            if (warnings.Count > 0)
            {
                sb.AppendLine($"Warnings ({warnings.Count}):");
                foreach (var (_, msg) in warnings)
                    sb.AppendLine($"  [Warning] {msg}");
                sb.AppendLine();
            }
            if (infos.Count > 0)
            {
                sb.AppendLine($"Info ({infos.Count}):");
                foreach (var (_, msg) in infos)
                    sb.AppendLine($"  [Info] {msg}");
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: {errors.Count} error(s), {warnings.Count} warning(s), {infos.Count} info(s).");

            return sb.ToString().TrimEnd();
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

                // Sub parameters (for puppets)
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

                // Recurse into submenus
                var controlType = control.FindPropertyRelative("type");
                if (controlType != null && controlType.intValue == 103) // SubMenu
                {
                    var subMenu = control.FindPropertyRelative("subMenu");
                    if (subMenu != null && subMenu.objectReferenceValue != null)
                        CollectMenuParameters(subMenu.objectReferenceValue, result, depth + 1, maxDepth);
                }
            }
        }

        // ========== 2. CheckWriteDefaults ==========

        [AgentTool("Check Write Defaults consistency in FX controller. Reports per-layer WD status.")]
        public static string CheckWriteDefaults(string avatarRootName)
        {
            var descriptor = FindDescriptor(avatarRootName, out string err);
            if (descriptor == null) return err;

            var dso = new SerializedObject(descriptor);
            var fxController = GetFXController(dso);
            if (fxController == null)
                return $"Error: No FX AnimatorController found on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Write Defaults report for '{avatarRootName}':");
            sb.AppendLine($"  FX Controller: {fxController.name}");
            sb.AppendLine();

            int totalOn = 0, totalOff = 0;

            foreach (var layer in fxController.layers)
            {
                if (layer.stateMachine == null) continue;

                int layerOn = 0, layerOff = 0;
                foreach (var childState in layer.stateMachine.states)
                {
                    if (childState.state.writeDefaultValues)
                        layerOn++;
                    else
                        layerOff++;
                }

                totalOn += layerOn;
                totalOff += layerOff;

                string status;
                if (layerOn > 0 && layerOff > 0)
                    status = "MIXED";
                else if (layerOn > 0)
                    status = "ON";
                else if (layerOff > 0)
                    status = "OFF";
                else
                    status = "EMPTY";

                sb.AppendLine($"  {layer.name}: WD={status} ({layerOn} on, {layerOff} off)");
            }

            sb.AppendLine();
            sb.AppendLine($"Total: {totalOn} states ON, {totalOff} states OFF.");

            if (totalOn > 0 && totalOff > 0)
                sb.AppendLine("[Error] Write Defaults are inconsistent! All states should use the same setting.");
            else
                sb.AppendLine($"[OK] Write Defaults are consistent: {(totalOn > 0 ? "ON" : "OFF")}.");

            return sb.ToString().TrimEnd();
        }

        // ========== 3. CheckParameterBudget ==========

        [AgentTool("Parameter bit budget analysis. Shows each synced parameter with bit cost and total usage vs 256 limit.")]
        public static string CheckParameterBudget(string avatarRootName)
        {
            var descriptor = FindDescriptor(avatarRootName, out string err);
            if (descriptor == null) return err;

            var dso = new SerializedObject(descriptor);
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return $"Error: No ExpressionParameters assigned on '{avatarRootName}'.";

            var paramsSo = new SerializedObject(exprParamsProp.objectReferenceValue);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null || !parameters.isArray)
                return "Error: Could not read parameters array.";

            var sb = new StringBuilder();
            sb.AppendLine($"Parameter Budget for '{avatarRootName}':");
            sb.AppendLine();
            sb.AppendLine("  Synced Parameters:");

            int totalBits = 0;
            int syncedCount = 0;
            int localCount = 0;

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var paramName = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(paramName)) continue;

                var valueType = param.FindPropertyRelative("valueType");
                var synced = param.FindPropertyRelative("networkSynced");
                var saved = param.FindPropertyRelative("saved");

                string typeStr = "?";
                int cost = 0;
                if (valueType != null)
                {
                    switch (valueType.intValue)
                    {
                        case 0: typeStr = "Int"; cost = 8; break;
                        case 1: typeStr = "Float"; cost = 8; break;
                        case 2: typeStr = "Bool"; cost = 1; break;
                    }
                }

                bool isSynced = synced != null && synced.boolValue;
                bool isSaved = saved != null && saved.boolValue;

                if (isSynced)
                {
                    totalBits += cost;
                    syncedCount++;
                    sb.AppendLine($"    {paramName} ({typeStr}) = {cost}bit {(isSaved ? "[Saved]" : "")}");
                }
                else
                {
                    localCount++;
                }
            }

            sb.AppendLine();
            if (localCount > 0)
                sb.AppendLine($"  Local (unsynced) parameters: {localCount}");
            sb.AppendLine();

            float usage = totalBits / 256f * 100f;
            string bar = new string('=', Math.Min((int)(usage / 5), 20)) + new string('-', Math.Max(20 - (int)(usage / 5), 0));
            sb.AppendLine($"  Usage: [{bar}] {totalBits}/256 bits ({usage:F1}%)");

            if (totalBits > 256)
                sb.AppendLine("  [Error] OVER BUDGET! Reduce synced parameters.");
            else if (usage > 90)
                sb.AppendLine("  [Warning] Nearly full. Consider using local parameters where possible.");
            else
                sb.AppendLine("  [OK] Within budget.");

            sb.AppendLine($"  Remaining: {256 - totalBits} bits ({(256 - totalBits) / 8} Int/Float or {256 - totalBits} Bool parameters).");

            return sb.ToString().TrimEnd();
        }
    }
}
