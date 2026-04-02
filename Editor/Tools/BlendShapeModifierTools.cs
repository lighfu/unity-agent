using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class BlendShapeModifierTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
#if BLEND_SHAPE_MODIFIER

        // ========== Helpers ==========

        // BlendShapeModifier types are internal, so we use reflection
        private static Type _bsmType;
        private static Type _blendShapeType;
        private static Type _blendShapeFrameType;
        private static Type _sampleExpressionType;
        private static Type _mergeExpressionType;

        private static bool InitTypes()
        {
            if (_bsmType != null) return true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_bsmType == null)
                    _bsmType = assembly.GetType("net.nekobako.BlendShapeModifier.Runtime.BlendShapeModifier");
                if (_blendShapeType == null)
                    _blendShapeType = assembly.GetType("net.nekobako.BlendShapeModifier.Runtime.BlendShape");
                if (_blendShapeFrameType == null)
                    _blendShapeFrameType = assembly.GetType("net.nekobako.BlendShapeModifier.Runtime.BlendShapeFrame");
                if (_sampleExpressionType == null)
                    _sampleExpressionType = assembly.GetType("net.nekobako.BlendShapeModifier.Runtime.BlendShapeSampleExpression");
                if (_mergeExpressionType == null)
                    _mergeExpressionType = assembly.GetType("net.nekobako.BlendShapeModifier.Runtime.BlendShapeMergeExpression");

                if (_bsmType != null && _blendShapeType != null && _blendShapeFrameType != null
                    && _sampleExpressionType != null && _mergeExpressionType != null)
                    return true;
            }

            return _bsmType != null;
        }

        private static Component[] FindBSMComponents(GameObject target)
        {
            if (!InitTypes()) return new Component[0];
            return target.GetComponents(_bsmType);
        }

        private static Component[] FindBSMComponentsInChildren(GameObject root)
        {
            if (!InitTypes()) return new Component[0];
            return root.GetComponentsInChildren(_bsmType, true);
        }

        // ========== 1. ListBlendShapeModifiers ==========

        [AgentTool("List all BlendShapeModifier components on an avatar. Shows target mesh and configured shapes.")]
        public static string ListBlendShapeModifiers(string avatarRootName)
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            if (!InitTypes())
                return "Error: BlendShapeModifier types not found. Package may not be installed correctly.";

            var components = FindBSMComponentsInChildren(avatarRoot);
            if (components.Length == 0)
                return $"No BlendShapeModifier components found on '{avatarRootName}' or its children.";

            var sb = new StringBuilder();
            sb.AppendLine($"BlendShapeModifier components on '{avatarRootName}' ({components.Length} total):");
            sb.AppendLine();

            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                var so = new SerializedObject(comp);

                var rendererProp = so.FindProperty("Renderer");
                string rendererName = "(null)";
                if (rendererProp != null && rendererProp.objectReferenceValue != null)
                    rendererName = rendererProp.objectReferenceValue.name;

                sb.AppendLine($"  [{i}] on '{comp.gameObject.name}' — Renderer: {rendererName}");

                var shapesProp = so.FindProperty("Shapes");
                if (shapesProp != null && shapesProp.isArray)
                {
                    for (int j = 0; j < shapesProp.arraySize; j++)
                    {
                        var shapeProp = shapesProp.GetArrayElementAtIndex(j);
                        var nameProp = shapeProp.FindPropertyRelative("Name");
                        var weightProp = shapeProp.FindPropertyRelative("Weight");
                        var framesProp = shapeProp.FindPropertyRelative("Frames");
                        int frameCount = framesProp != null && framesProp.isArray ? framesProp.arraySize : 0;

                        sb.AppendLine($"      Shape: \"{nameProp?.stringValue ?? "?"}\" weight={weightProp?.floatValue ?? 0} frames={frameCount}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 2. AddBlendShapeModifier ==========

        [AgentTool("Add a BlendShapeModifier component to create custom non-destructive blend shapes. targetMeshName: GameObject with SkinnedMeshRenderer.")]
        public static string AddBlendShapeModifier(string targetMeshName)
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            if (!InitTypes())
                return "Error: BlendShapeModifier types not found.";

            var smr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            // Check for existing
            var existing = FindBSMComponents(targetObj);
            if (existing.Length > 0)
                return $"Info: '{targetMeshName}' already has {existing.Length} BlendShapeModifier component(s). Use AddCustomBlendShape to add shapes.";

            var comp = Undo.AddComponent(targetObj, _bsmType);

            // Set the Renderer reference
            var so = new SerializedObject(comp);
            var rendererProp = so.FindProperty("Renderer");
            if (rendererProp != null)
            {
                rendererProp.objectReferenceValue = smr;
                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(comp);

            return $"Success: Added BlendShapeModifier to '{targetMeshName}' (Renderer: {smr.name}).";
        }

        // ========== 3. AddCustomBlendShape ==========

        [AgentTool("Add a custom blend shape to a BlendShapeModifier. The new shape is composed from existing shapes via 'Sample' expressions. sources: 'existingShapeName=weight;anotherShape=weight2'. NDMF processes at build time.")]
        public static string AddCustomBlendShape(string targetMeshName, string newShapeName,
            string sources, float weight = 100f, int modifierIndex = 0)
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            if (!InitTypes())
                return "Error: BlendShapeModifier types not found.";

            var components = FindBSMComponents(targetObj);
            if (components.Length == 0)
                return $"Error: No BlendShapeModifier on '{targetMeshName}'. Use AddBlendShapeModifier first.";

            if (modifierIndex < 0 || modifierIndex >= components.Length)
                return $"Error: Modifier index {modifierIndex} out of range (0-{components.Length - 1}).";

            // Parse sources: "shapeName=weight;shapeName2=weight2"
            var sourcePairs = new List<(string name, float value)>();
            foreach (var entry in sources.Split(';'))
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split('=');
                if (parts.Length != 2)
                    return $"Error: Invalid source entry '{trimmed}'. Expected 'shapeName=weight'.";
                if (!float.TryParse(parts[1].Trim(), out float val))
                    return $"Error: Invalid weight '{parts[1].Trim()}'.";
                sourcePairs.Add((parts[0].Trim(), val));
            }

            if (sourcePairs.Count == 0)
                return "Error: No source shapes specified.";

            var comp = components[modifierIndex];
            var so = new SerializedObject(comp);
            var shapesProp = so.FindProperty("Shapes");
            if (shapesProp == null || !shapesProp.isArray)
                return "Error: Shapes property not found.";

            // Check for duplicate name
            for (int i = 0; i < shapesProp.arraySize; i++)
            {
                var existingName = shapesProp.GetArrayElementAtIndex(i).FindPropertyRelative("Name")?.stringValue;
                if (existingName == newShapeName)
                    return $"Error: Shape '{newShapeName}' already exists. Use RemoveCustomBlendShape first.";
            }

            // Create BlendShape via reflection and SerializedObject
            var newShape = Activator.CreateInstance(_blendShapeType);
            var nameField = _blendShapeType.GetField("Name");
            var weightField = _blendShapeType.GetField("Weight");
            var framesField = _blendShapeType.GetField("Frames");

            if (nameField != null) nameField.SetValue(newShape, newShapeName);
            if (weightField != null) weightField.SetValue(newShape, weight);

            // Create frames list
            var frameListType = typeof(List<>).MakeGenericType(_blendShapeFrameType);
            var framesList = Activator.CreateInstance(frameListType);

            // Create a single frame at weight=100 with the expression
            var frame = Activator.CreateInstance(_blendShapeFrameType);
            var frameWeightField = _blendShapeFrameType.GetField("Weight");
            var frameExpressionField = _blendShapeFrameType.GetField("Expression");
            if (frameWeightField != null) frameWeightField.SetValue(frame, 100f);

            if (sourcePairs.Count == 1)
            {
                // Single source: use BlendShapeSampleExpression
                var sampleExpr = Activator.CreateInstance(_sampleExpressionType);
                var sampleNameField = _sampleExpressionType.GetField("Name");
                var sampleWeightField = _sampleExpressionType.GetField("Weight");
                if (sampleNameField != null) sampleNameField.SetValue(sampleExpr, sourcePairs[0].name);
                if (sampleWeightField != null) sampleWeightField.SetValue(sampleExpr, sourcePairs[0].value);
                if (frameExpressionField != null) frameExpressionField.SetValue(frame, sampleExpr);
            }
            else
            {
                // Multiple sources: use BlendShapeMergeExpression
                var mergeExpr = Activator.CreateInstance(_mergeExpressionType);
                var expressionInterfaceType = _sampleExpressionType.GetInterfaces()
                    .FirstOrDefault(t => t.Name == "IBlendShapeExpression");

                if (expressionInterfaceType != null)
                {
                    var exprListType = typeof(List<>).MakeGenericType(expressionInterfaceType);
                    var exprList = Activator.CreateInstance(exprListType);
                    var addMethod = exprListType.GetMethod("Add");

                    foreach (var (name, val) in sourcePairs)
                    {
                        var sampleExpr = Activator.CreateInstance(_sampleExpressionType);
                        var sampleNameField = _sampleExpressionType.GetField("Name");
                        var sampleWeightField = _sampleExpressionType.GetField("Weight");
                        if (sampleNameField != null) sampleNameField.SetValue(sampleExpr, name);
                        if (sampleWeightField != null) sampleWeightField.SetValue(sampleExpr, val);
                        addMethod.Invoke(exprList, new[] { sampleExpr });
                    }

                    var mergeExprsField = _mergeExpressionType.GetField("Expressions");
                    if (mergeExprsField != null) mergeExprsField.SetValue(mergeExpr, exprList);
                }

                if (frameExpressionField != null) frameExpressionField.SetValue(frame, mergeExpr);
            }

            var addFrameMethod = frameListType.GetMethod("Add");
            addFrameMethod.Invoke(framesList, new[] { frame });
            if (framesField != null) framesField.SetValue(newShape, framesList);

            // Add to Shapes array via SerializedObject
            int idx = shapesProp.arraySize;
            shapesProp.InsertArrayElementAtIndex(idx);
            var newElem = shapesProp.GetArrayElementAtIndex(idx);
            newElem.managedReferenceValue = newShape;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Added custom blend shape '{newShapeName}' to '{targetMeshName}'.");
            sb.AppendLine($"  Weight: {weight}");
            sb.AppendLine($"  Sources ({sourcePairs.Count}):");
            foreach (var (name, val) in sourcePairs)
                sb.AppendLine($"    - {name} = {val}");
            sb.AppendLine("  Processed at build time by NDMF.");

            return sb.ToString().TrimEnd();
        }

        // ========== 4. RemoveCustomBlendShape ==========

        [AgentTool("Remove a custom blend shape from a BlendShapeModifier by name.")]
        public static string RemoveCustomBlendShape(string targetMeshName, string shapeName,
            int modifierIndex = 0)
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            if (!InitTypes())
                return "Error: BlendShapeModifier types not found.";

            var components = FindBSMComponents(targetObj);
            if (components.Length == 0)
                return $"Error: No BlendShapeModifier on '{targetMeshName}'.";

            if (modifierIndex < 0 || modifierIndex >= components.Length)
                return $"Error: Modifier index {modifierIndex} out of range.";

            var comp = components[modifierIndex];
            var so = new SerializedObject(comp);
            var shapesProp = so.FindProperty("Shapes");
            if (shapesProp == null || !shapesProp.isArray)
                return "Error: Shapes property not found.";

            // Find shape by name
            int removeIdx = -1;
            for (int i = 0; i < shapesProp.arraySize; i++)
            {
                var nameProp = shapesProp.GetArrayElementAtIndex(i).FindPropertyRelative("Name");
                if (nameProp != null && nameProp.stringValue == shapeName)
                {
                    removeIdx = i;
                    break;
                }
            }

            if (removeIdx < 0)
                return $"Error: Shape '{shapeName}' not found in BlendShapeModifier.";

            if (!AgentSettings.RequestConfirmation(
                "カスタムBlendShapeの削除",
                $"'{targetMeshName}' のカスタムBlendShape '{shapeName}' を削除します。"))
                return "Cancelled: User denied the operation.";

            shapesProp.DeleteArrayElementAtIndex(removeIdx);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            return $"Success: Removed custom blend shape '{shapeName}' from '{targetMeshName}'.";
        }

        // ========== 5. InspectBlendShapeModifier ==========

        [AgentTool("Inspect a BlendShapeModifier component. Shows all custom shapes with their source expressions.")]
        public static string InspectBlendShapeModifier(string targetMeshName, int modifierIndex = 0)
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            if (!InitTypes())
                return "Error: BlendShapeModifier types not found.";

            var components = FindBSMComponents(targetObj);
            if (components.Length == 0)
                return $"No BlendShapeModifier found on '{targetMeshName}'.";

            if (modifierIndex < 0 || modifierIndex >= components.Length)
                return $"Error: Modifier index {modifierIndex} out of range (0-{components.Length - 1}).";

            var comp = components[modifierIndex];
            var so = new SerializedObject(comp);

            var sb = new StringBuilder();

            var rendererProp = so.FindProperty("Renderer");
            string rendererName = "(null)";
            if (rendererProp != null && rendererProp.objectReferenceValue != null)
                rendererName = rendererProp.objectReferenceValue.name;

            sb.AppendLine($"BlendShapeModifier [{modifierIndex}] on '{targetMeshName}':");
            sb.AppendLine($"  Renderer: {rendererName}");

            var shapesProp = so.FindProperty("Shapes");
            if (shapesProp == null || !shapesProp.isArray || shapesProp.arraySize == 0)
            {
                sb.AppendLine("  Shapes: (empty)");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"  Shapes ({shapesProp.arraySize}):");

            for (int i = 0; i < shapesProp.arraySize; i++)
            {
                var shapeProp = shapesProp.GetArrayElementAtIndex(i);
                var nameProp = shapeProp.FindPropertyRelative("Name");
                var weightProp = shapeProp.FindPropertyRelative("Weight");
                var framesProp = shapeProp.FindPropertyRelative("Frames");

                sb.AppendLine($"    [{i}] \"{nameProp?.stringValue ?? "?"}\" (weight={weightProp?.floatValue ?? 0})");

                if (framesProp != null && framesProp.isArray)
                {
                    for (int j = 0; j < framesProp.arraySize; j++)
                    {
                        var frameProp = framesProp.GetArrayElementAtIndex(j);
                        var frameWeight = frameProp.FindPropertyRelative("Weight");
                        var exprProp = frameProp.FindPropertyRelative("Expression");

                        sb.Append($"      Frame[{j}] weight={frameWeight?.floatValue ?? 0}: ");

                        if (exprProp != null)
                        {
                            var exprTypeName = exprProp.managedReferenceFullTypename;
                            if (!string.IsNullOrEmpty(exprTypeName))
                            {
                                var parts = exprTypeName.Split(' ');
                                var typeName = parts.Length >= 2 ? parts[parts.Length - 1] : exprTypeName;
                                var dotIdx = typeName.LastIndexOf('.');
                                typeName = dotIdx >= 0 ? typeName.Substring(dotIdx + 1) : typeName;

                                if (typeName == "BlendShapeSampleExpression")
                                {
                                    var sampleName = exprProp.FindPropertyRelative("Name")?.stringValue ?? "?";
                                    var sampleWeight = exprProp.FindPropertyRelative("Weight")?.floatValue ?? 0;
                                    sb.AppendLine($"Sample(\"{sampleName}\", {sampleWeight})");
                                }
                                else if (typeName == "BlendShapeMergeExpression")
                                {
                                    var exprsList = exprProp.FindPropertyRelative("Expressions");
                                    if (exprsList != null && exprsList.isArray)
                                    {
                                        sb.AppendLine($"Merge({exprsList.arraySize} sources):");
                                        for (int k = 0; k < exprsList.arraySize; k++)
                                        {
                                            var subExpr = exprsList.GetArrayElementAtIndex(k);
                                            var subName = subExpr.FindPropertyRelative("Name")?.stringValue ?? "?";
                                            var subWeight = subExpr.FindPropertyRelative("Weight")?.floatValue ?? 0;
                                            sb.AppendLine($"        - Sample(\"{subName}\", {subWeight})");
                                        }
                                    }
                                    else
                                    {
                                        sb.AppendLine("Merge(?)");
                                    }
                                }
                                else
                                {
                                    sb.AppendLine(typeName);
                                }
                            }
                            else
                            {
                                sb.AppendLine("(null expression)");
                            }
                        }
                        else
                        {
                            sb.AppendLine("(no expression)");
                        }
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

#endif
    }
}
