#if AVATAR_OPTIMIZER
using Anatawa12.AvatarOptimizer;
using Anatawa12.AvatarOptimizer.API;
#endif
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AvatarOptimizerTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
#if AVATAR_OPTIMIZER

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            return go;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrEmpty(value)) return false;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
            return false;
        }

        // ========== 1. AddTraceAndOptimize ==========

        [AgentTool("Add AAO Trace And Optimize to avatar for automatic build-time optimization. Handles unused object removal, blend shape optimization, PhysBone optimization, mesh merging, and texture optimization.")]
        public static string AddTraceAndOptimize(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var existing = avatarRoot.GetComponent<TraceAndOptimize>();
            if (existing != null)
                return $"Error: '{avatarRootName}' already has a TraceAndOptimize component.";

            if (!AgentSettings.RequestConfirmation(
                "AAO Trace And Optimize の追加",
                $"対象: {avatarRootName}\n自動ビルド時最適化コンポーネントを追加します。"))
                return "Cancelled: User denied the operation.";

            Undo.AddComponent<TraceAndOptimize>(avatarRoot);
            EditorUtility.SetDirty(avatarRoot);

            return $"Success: Added TraceAndOptimize to '{avatarRootName}'.\n  Default settings: all optimizations enabled.";
        }

        // ========== 2. ConfigureTraceAndOptimize ==========

        [AgentTool("Configure AAO Trace And Optimize settings. Shows current values when called without parameters.")]
        public static string ConfigureTraceAndOptimize(string avatarRootName,
            string optimizeBlendShape = "", string removeUnusedObjects = "",
            string preserveEndBone = "", string optimizePhysBone = "",
            string optimizeAnimator = "", string mergeSkinnedMesh = "",
            string optimizeTexture = "", string mmdWorldCompatibility = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var comp = avatarRoot.GetComponent<TraceAndOptimize>();
            if (comp == null)
                return $"Error: '{avatarRootName}' does not have a TraceAndOptimize component. Use AddTraceAndOptimize first.";

            var so = new SerializedObject(comp);

            bool anySet = !string.IsNullOrEmpty(optimizeBlendShape) ||
                          !string.IsNullOrEmpty(removeUnusedObjects) ||
                          !string.IsNullOrEmpty(preserveEndBone) ||
                          !string.IsNullOrEmpty(optimizePhysBone) ||
                          !string.IsNullOrEmpty(optimizeAnimator) ||
                          !string.IsNullOrEmpty(mergeSkinnedMesh) ||
                          !string.IsNullOrEmpty(optimizeTexture) ||
                          !string.IsNullOrEmpty(mmdWorldCompatibility);

            if (!anySet)
            {
                // Show current values
                var sb = new StringBuilder();
                sb.AppendLine($"TraceAndOptimize settings on '{avatarRootName}':");
                sb.AppendLine($"  optimizeBlendShape: {so.FindProperty("optimizeBlendShape")?.boolValue}");
                sb.AppendLine($"  removeUnusedObjects: {so.FindProperty("removeUnusedObjects")?.boolValue}");
                sb.AppendLine($"  preserveEndBone: {so.FindProperty("preserveEndBone")?.boolValue}");
                sb.AppendLine($"  optimizePhysBone: {so.FindProperty("optimizePhysBone")?.boolValue}");
                sb.AppendLine($"  optimizeAnimator: {so.FindProperty("optimizeAnimator")?.boolValue}");
                sb.AppendLine($"  mergeSkinnedMesh: {so.FindProperty("mergeSkinnedMesh")?.boolValue}");
                sb.AppendLine($"  optimizeTexture: {so.FindProperty("optimizeTexture")?.boolValue}");
                sb.AppendLine($"  mmdWorldCompatibility: {so.FindProperty("mmdWorldCompatibility")?.boolValue}");
                return sb.ToString().TrimEnd();
            }

            // Apply changes
            Undo.RecordObject(comp, "Configure TraceAndOptimize");

            var changes = new List<string>();
            void SetBool(string propName, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!TryParseBool(value, out bool boolVal))
                    return;
                var prop = so.FindProperty(propName);
                if (prop != null)
                {
                    prop.boolValue = boolVal;
                    changes.Add($"  {propName}: {boolVal}");
                }
            }

            SetBool("optimizeBlendShape", optimizeBlendShape);
            SetBool("removeUnusedObjects", removeUnusedObjects);
            SetBool("preserveEndBone", preserveEndBone);
            SetBool("optimizePhysBone", optimizePhysBone);
            SetBool("optimizeAnimator", optimizeAnimator);
            SetBool("mergeSkinnedMesh", mergeSkinnedMesh);
            SetBool("optimizeTexture", optimizeTexture);
            SetBool("mmdWorldCompatibility", mmdWorldCompatibility);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            return $"Success: Updated TraceAndOptimize on '{avatarRootName}':\n{string.Join("\n", changes)}";
        }

        // ========== 3. AddMergeSkinnedMesh ==========

        [AgentTool("Add AAO Merge Skinned Mesh to combine multiple SkinnedMeshRenderers into one. targetMeshName: destination mesh object. sourceMeshes: semicolon-separated source mesh object names to merge.")]
        public static string AddMergeSkinnedMesh(string targetMeshName, string sourceMeshes,
            string removeEmptyRendererObject = "true", string mergeBlendShapes = "true")
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var targetSmr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (targetSmr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            var sourceNames = sourceMeshes.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (sourceNames.Length == 0)
                return "Error: No source meshes specified.";

            var sourceRenderers = new List<SkinnedMeshRenderer>();
            foreach (var name in sourceNames)
            {
                var go = FindGO(name);
                if (go == null)
                    return $"Error: Source mesh '{name}' not found.";
                var smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr == null)
                    return $"Error: Source '{name}' does not have a SkinnedMeshRenderer.";
                sourceRenderers.Add(smr);
            }

            bool removeEmpty = !removeEmptyRendererObject.Equals("false", StringComparison.OrdinalIgnoreCase);
            bool mergeBS = !mergeBlendShapes.Equals("false", StringComparison.OrdinalIgnoreCase);

            if (!AgentSettings.RequestConfirmation(
                "AAO Merge Skinned Mesh の追加",
                $"対象メッシュ: {targetMeshName}\n" +
                $"ソース ({sourceRenderers.Count}): {string.Join(", ", sourceNames)}\n" +
                $"空レンダラー削除: {removeEmpty}, BlendShape結合: {mergeBS}"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent<MergeSkinnedMesh>(targetObj);
            comp.Initialize(2);
            comp.RemoveEmptyRendererObject = removeEmpty;
            comp.MergeBlendShapes = mergeBS;

            foreach (var smr in sourceRenderers)
                comp.SourceSkinnedMeshRenderers.Add(smr);

            EditorUtility.SetDirty(comp);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Added MergeSkinnedMesh to '{targetMeshName}'.");
            sb.AppendLine($"  Sources ({sourceRenderers.Count}):");
            foreach (var smr in sourceRenderers)
                sb.AppendLine($"    - {smr.gameObject.name}");
            sb.AppendLine($"  RemoveEmptyRendererObject: {removeEmpty}");
            sb.AppendLine($"  MergeBlendShapes: {mergeBS}");
            return sb.ToString().TrimEnd();
        }

        // ========== 4. AddRemoveMeshInBox ==========

        [AgentTool("Add AAO Remove Mesh In Box to remove polygons inside a bounding box. Useful for removing hidden body mesh under clothes. center/size: Vector3 as 'x,y,z'. rotation: Quaternion as 'x,y,z,w' (default identity).")]
        public static string AddRemoveMeshInBox(string targetMeshName, string center, string size,
            string rotation = "0,0,0,1")
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var smr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            // Parse center
            var centerParts = center.Split(',');
            if (centerParts.Length != 3)
                return "Error: center must be 'x,y,z'.";
            if (!float.TryParse(centerParts[0].Trim(), out float cx) ||
                !float.TryParse(centerParts[1].Trim(), out float cy) ||
                !float.TryParse(centerParts[2].Trim(), out float cz))
                return "Error: Invalid center values.";

            // Parse size
            var sizeParts = size.Split(',');
            if (sizeParts.Length != 3)
                return "Error: size must be 'x,y,z'.";
            if (!float.TryParse(sizeParts[0].Trim(), out float sx) ||
                !float.TryParse(sizeParts[1].Trim(), out float sy) ||
                !float.TryParse(sizeParts[2].Trim(), out float sz))
                return "Error: Invalid size values.";

            // Parse rotation
            var rotParts = rotation.Split(',');
            Quaternion rot = Quaternion.identity;
            if (rotParts.Length == 4)
            {
                if (float.TryParse(rotParts[0].Trim(), out float rx) &&
                    float.TryParse(rotParts[1].Trim(), out float ry) &&
                    float.TryParse(rotParts[2].Trim(), out float rz) &&
                    float.TryParse(rotParts[3].Trim(), out float rw))
                    rot = new Quaternion(rx, ry, rz, rw);
            }

            if (!AgentSettings.RequestConfirmation(
                "AAO Remove Mesh In Box の追加",
                $"対象: {targetMeshName}\n" +
                $"Center: ({cx}, {cy}, {cz})\n" +
                $"Size: ({sx}, {sy}, {sz})"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent<RemoveMeshInBox>(targetObj);
            comp.Initialize(1);
            comp.Boxes = new[]
            {
                new RemoveMeshInBox.BoundingBox
                {
                    Center = new Vector3(cx, cy, cz),
                    Size = new Vector3(sx, sy, sz),
                    Rotation = rot
                }
            };

            EditorUtility.SetDirty(comp);

            return $"Success: Added RemoveMeshInBox to '{targetMeshName}'.\n" +
                   $"  Center: ({cx}, {cy}, {cz})\n" +
                   $"  Size: ({sx}, {sy}, {sz})\n" +
                   $"  Rotation: ({rot.x}, {rot.y}, {rot.z}, {rot.w})";
        }

        // ========== 5. AddRemoveMeshByBlendShape ==========

        [AgentTool("Add AAO Remove Mesh By BlendShape to remove polygons affected by specified blend shapes. shapeKeys: semicolon-separated blend shape names. tolerance: minimum delta threshold (default 0.001).")]
        public static string AddRemoveMeshByBlendShape(string targetMeshName, string shapeKeys,
            string tolerance = "0.001")
        {
            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var smr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            var keys = shapeKeys.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (keys.Length == 0)
                return "Error: No shape keys specified.";

            // Validate shape keys exist
            var mesh = smr.sharedMesh;
            if (mesh != null)
            {
                var missing = new List<string>();
                foreach (var key in keys)
                {
                    if (mesh.GetBlendShapeIndex(key) < 0)
                        missing.Add(key);
                }
                if (missing.Count > 0)
                    return $"Error: BlendShape(s) not found on '{targetMeshName}': {string.Join(", ", missing)}";
            }

            if (!double.TryParse(tolerance, out double tol))
                tol = 0.001;

            if (!AgentSettings.RequestConfirmation(
                "AAO Remove Mesh By BlendShape の追加",
                $"対象: {targetMeshName}\n" +
                $"ShapeKeys: {string.Join(", ", keys)}\n" +
                $"Tolerance: {tol}"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent<RemoveMeshByBlendShape>(targetObj);
            comp.Initialize(1);
            comp.Tolerance = tol;

            foreach (var key in keys)
                comp.ShapeKeys.Add(key);

            EditorUtility.SetDirty(comp);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Added RemoveMeshByBlendShape to '{targetMeshName}'.");
            sb.AppendLine($"  Tolerance: {tol}");
            sb.AppendLine($"  ShapeKeys ({keys.Length}):");
            foreach (var key in keys)
                sb.AppendLine($"    - {key}");
            return sb.ToString().TrimEnd();
        }

        // ========== 6. ListAAOComponents ==========

        [AgentTool("List all Avatar Optimizer components on an avatar and its children.")]
        public static string ListAAOComponents(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = avatarRoot.GetComponentsInChildren<AvatarTagComponent>(true);
            if (components == null || components.Length == 0)
                return $"No AAO components found on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"AAO components on '{avatarRootName}' ({components.Length}):");

            foreach (var comp in components)
            {
                var typeName = comp.GetType().Name;
                var path = GetRelativePath(avatarRoot.transform, comp.transform);
                sb.AppendLine($"  [{typeName}] on '{path}'");

                // Show details per type
                if (comp is TraceAndOptimize tao)
                {
                    var so = new SerializedObject(tao);
                    sb.AppendLine($"    optimizeBlendShape={so.FindProperty("optimizeBlendShape")?.boolValue}");
                    sb.AppendLine($"    removeUnusedObjects={so.FindProperty("removeUnusedObjects")?.boolValue}");
                    sb.AppendLine($"    mergeSkinnedMesh={so.FindProperty("mergeSkinnedMesh")?.boolValue}");
                    sb.AppendLine($"    optimizeTexture={so.FindProperty("optimizeTexture")?.boolValue}");
                }
                else if (comp is MergeSkinnedMesh msm)
                {
                    var sources = new List<string>();
                    foreach (var smr in msm.SourceSkinnedMeshRenderers)
                        sources.Add(smr != null ? smr.gameObject.name : "(null)");
                    sb.AppendLine($"    Sources: {(sources.Count > 0 ? string.Join(", ", sources) : "(none)")}");
                    sb.AppendLine($"    RemoveEmptyRendererObject={msm.RemoveEmptyRendererObject}");
                }
                else if (comp is RemoveMeshInBox rmib)
                {
                    var boxes = rmib.Boxes;
                    sb.AppendLine($"    Boxes: {boxes.Length}");
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        var box = boxes[i];
                        sb.AppendLine($"      [{i}] Center=({box.Center.x:F3},{box.Center.y:F3},{box.Center.z:F3}) Size=({box.Size.x:F3},{box.Size.y:F3},{box.Size.z:F3})");
                    }
                }
                else if (comp is RemoveMeshByBlendShape rmbbs)
                {
                    var keys = new List<string>();
                    foreach (var key in rmbbs.ShapeKeys)
                        keys.Add(key);
                    sb.AppendLine($"    ShapeKeys: {(keys.Count > 0 ? string.Join(", ", keys) : "(none)")}");
                    sb.AppendLine($"    Tolerance={rmbbs.Tolerance}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 7. RemoveAAOComponent ==========

        [AgentTool("Remove an AAO component by type and target object name. componentType: TraceAndOptimize, MergeSkinnedMesh, RemoveMeshInBox, RemoveMeshByBlendShape. index: 0-based index when multiple components of same type exist (default 0).")]
        public static string RemoveAAOComponent(string goName, string componentType, int index = 0)
        {
            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            Type targetType;
            switch (componentType)
            {
                case "TraceAndOptimize": targetType = typeof(TraceAndOptimize); break;
                case "MergeSkinnedMesh": targetType = typeof(MergeSkinnedMesh); break;
                case "RemoveMeshInBox": targetType = typeof(RemoveMeshInBox); break;
                case "RemoveMeshByBlendShape": targetType = typeof(RemoveMeshByBlendShape); break;
                default:
                    return $"Error: Unknown component type '{componentType}'. Use: TraceAndOptimize, MergeSkinnedMesh, RemoveMeshInBox, RemoveMeshByBlendShape.";
            }

            var comps = go.GetComponents(targetType);
            if (comps == null || comps.Length == 0)
                return $"Error: No {componentType} component found on '{goName}'.";

            if (index < 0 || index >= comps.Length)
                return $"Error: Index {index} out of range. '{goName}' has {comps.Length} {componentType} component(s).";

            if (!AgentSettings.RequestConfirmation(
                "AAO コンポーネントの削除",
                $"対象: {goName}\n" +
                $"コンポーネント: {componentType} (index {index})"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(comps[index]);
            EditorUtility.SetDirty(go);

            return $"Success: Removed {componentType} (index {index}) from '{goName}'.";
        }

        // ========== Internal Type Helpers ==========

        private static Type _freezeBlendShapeType;
        private static Type _mergeBoneType;

        private static bool InitInternalTypes()
        {
            if (_freezeBlendShapeType != null && _mergeBoneType != null) return true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                _freezeBlendShapeType ??= asm.GetType("Anatawa12.AvatarOptimizer.FreezeBlendShape");
                _mergeBoneType ??= asm.GetType("Anatawa12.AvatarOptimizer.MergeBone");
                if (_freezeBlendShapeType != null && _mergeBoneType != null) return true;
            }
            return _freezeBlendShapeType != null;
        }

        // ========== 8. AddFreezeBlendShape ==========

        [AgentTool("Add AAO Freeze BlendShape to lock specific blend shapes at current values for optimization. shapeKeys: semicolon-separated blend shape names.")]
        public static string AddFreezeBlendShape(string targetMeshName, string shapeKeys)
        {
            if (!InitInternalTypes() || _freezeBlendShapeType == null)
                return "Error: FreezeBlendShape type not found. Is Avatar Optimizer installed?";

            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var smr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            var keys = shapeKeys.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (keys.Length == 0)
                return "Error: No shape keys specified.";

            // Validate shape keys exist
            var mesh = smr.sharedMesh;
            if (mesh != null)
            {
                var missing = new List<string>();
                foreach (var key in keys)
                {
                    if (mesh.GetBlendShapeIndex(key) < 0)
                        missing.Add(key);
                }
                if (missing.Count > 0)
                    return $"Error: BlendShape(s) not found on '{targetMeshName}': {string.Join(", ", missing)}";
            }

            if (!AgentSettings.RequestConfirmation(
                "AAO Freeze BlendShape の追加",
                $"対象: {targetMeshName}\nShapeKeys: {string.Join(", ", keys)}"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent(targetObj, _freezeBlendShapeType);

            // Access PrefabSafeSet<string> field and call AddRange
            var shapeKeysSetField = _freezeBlendShapeType.GetField("shapeKeysSet",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (shapeKeysSetField != null)
            {
                var shapeKeysSet = shapeKeysSetField.GetValue(comp);
                if (shapeKeysSet != null)
                {
                    var addRangeMethod = shapeKeysSet.GetType().GetMethod("AddRange",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (addRangeMethod != null)
                        addRangeMethod.Invoke(shapeKeysSet, new object[] { keys.AsEnumerable() });
                }
            }

            EditorUtility.SetDirty(comp);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Added FreezeBlendShape to '{targetMeshName}'.");
            sb.AppendLine($"  ShapeKeys ({keys.Length}):");
            foreach (var key in keys)
                sb.AppendLine($"    - {key}");
            return sb.ToString().TrimEnd();
        }

        // ========== 9. ListFreezeBlendShapes ==========

        [AgentTool("List all AAO Freeze BlendShape components on an avatar.")]
        public static string ListFreezeBlendShapes(string avatarRootName)
        {
            if (!InitInternalTypes() || _freezeBlendShapeType == null)
                return "Error: FreezeBlendShape type not found. Is Avatar Optimizer installed?";

            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = avatarRoot.GetComponentsInChildren(_freezeBlendShapeType, true);
            if (components == null || components.Length == 0)
                return $"No FreezeBlendShape components found on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"FreezeBlendShape components on '{avatarRootName}' ({components.Length}):");

            foreach (var comp in components)
            {
                var path = GetRelativePath(avatarRoot.transform, comp.transform);
                sb.AppendLine($"  [{path}]");

                // Read shape keys via SerializedObject
                var so = new SerializedObject(comp);
                var mainSetProp = so.FindProperty("shapeKeysSet");
                if (mainSetProp != null)
                {
                    // PrefabSafeSet stores items in mainSet (List<T>)
                    var mainListProp = mainSetProp.FindPropertyRelative("mainSet");
                    if (mainListProp != null && mainListProp.isArray)
                    {
                        for (int i = 0; i < mainListProp.arraySize; i++)
                        {
                            var elem = mainListProp.GetArrayElementAtIndex(i);
                            sb.AppendLine($"    - {elem.stringValue}");
                        }
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 10. AddMergeBone ==========

        [AgentTool("Add AAO Merge Bone to merge a bone into its parent at build time. Reduces bone count.")]
        public static string AddMergeBone(string boneName, string avoidNameConflict = "true")
        {
            if (!InitInternalTypes() || _mergeBoneType == null)
                return "Error: MergeBone type not found. Is Avatar Optimizer installed?";

            var go = FindGO(boneName);
            if (go == null)
                return $"Error: GameObject '{boneName}' not found.";

            var existing = go.GetComponent(_mergeBoneType);
            if (existing != null)
                return $"Error: '{boneName}' already has a MergeBone component.";

            bool avoid = !avoidNameConflict.Equals("false", StringComparison.OrdinalIgnoreCase);

            if (!AgentSettings.RequestConfirmation(
                "AAO Merge Bone の追加",
                $"対象ボーン: {boneName}\navoidNameConflict: {avoid}"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent(go, _mergeBoneType);
            var so = new SerializedObject(comp);
            var avoidProp = so.FindProperty("avoidNameConflict");
            if (avoidProp != null)
                avoidProp.boolValue = avoid;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            return $"Success: Added MergeBone to '{boneName}'.\n  avoidNameConflict: {avoid}";
        }

        // ========== 11. AddMergeBoneBatch ==========

        [AgentTool("Add AAO Merge Bone to multiple bones. boneNames: semicolon-separated.")]
        public static string AddMergeBoneBatch(string avatarRootName, string boneNames, string avoidNameConflict = "true")
        {
            if (!InitInternalTypes() || _mergeBoneType == null)
                return "Error: MergeBone type not found. Is Avatar Optimizer installed?";

            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var names = boneNames.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (names.Length == 0)
                return "Error: No bone names specified.";

            bool avoid = !avoidNameConflict.Equals("false", StringComparison.OrdinalIgnoreCase);

            // Resolve all bones first
            var bones = new List<(string name, Transform transform)>();
            foreach (var name in names)
            {
                var found = avatarRoot.transform.Find(name);
                if (found == null)
                {
                    // Search recursively
                    found = FindChildRecursive(avatarRoot.transform, name);
                }
                if (found == null)
                    return $"Error: Bone '{name}' not found under '{avatarRootName}'.";
                if (found.GetComponent(_mergeBoneType) != null)
                    continue; // Skip already has MergeBone
                bones.Add((name, found));
            }

            if (bones.Count == 0)
                return "Info: All specified bones already have MergeBone components.";

            if (!AgentSettings.RequestConfirmation(
                "AAO Merge Bone 一括追加",
                $"対象: {bones.Count} ボーン\n" +
                $"ボーン: {string.Join(", ", bones.Select(b => b.name))}\n" +
                $"avoidNameConflict: {avoid}"))
                return "Cancelled: User denied the operation.";

            var sb = new StringBuilder();
            sb.AppendLine($"AddMergeBoneBatch results ({bones.Count}):");

            foreach (var (name, t) in bones)
            {
                var comp = Undo.AddComponent(t.gameObject, _mergeBoneType);
                var so = new SerializedObject(comp);
                var avoidProp = so.FindProperty("avoidNameConflict");
                if (avoidProp != null)
                    avoidProp.boolValue = avoid;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(comp);
                sb.AppendLine($"  Added MergeBone to '{name}'");
            }

            return sb.ToString().TrimEnd();
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

#endif
    }
}
