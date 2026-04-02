using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// OSC上級ツール: FXレイヤー・ブレンドツリー・Parameter Driver・スムージング・マルチプレクサ。
    /// VRChat FX コントローラーへの直接レイヤー追加でOSC連携アニメーションを構築。
    /// </summary>
    public static class OSCAdvancedTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ===== Shared Helpers =====

        private static AnimatorController GetFXControllerForAvatar(string avatarRootName, out string error)
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
            var baseLayers = dso.FindProperty("baseAnimationLayers");
            if (baseLayers == null || !baseLayers.isArray)
            {
                error = "Error: Could not read baseAnimationLayers.";
                return null;
            }

            for (int i = 0; i < baseLayers.arraySize; i++)
            {
                var layer = baseLayers.GetArrayElementAtIndex(i);
                var layerType = layer.FindPropertyRelative("type");
                if (layerType != null && layerType.intValue == 5) // FX = 5
                {
                    var ac = layer.FindPropertyRelative("animatorController");
                    if (ac?.objectReferenceValue is AnimatorController controller)
                        return controller;
                    error = "Error: FX layer has no AnimatorController assigned.";
                    return null;
                }
            }

            error = "Error: FX layer not found in baseAnimationLayers.";
            return null;
        }

        private static void FindOrAddAnimatorParam(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (controller.parameters.Any(p => p.name == name))
                return;
            controller.AddParameter(name, type);
        }

        /// <summary>
        /// VRCAvatarParameterDriver をステートに追加し、エントリを設定する。
        /// リフレクション経由で VRC SDK 型を使用。
        /// </summary>
        private static bool AddParameterDriverToState(AnimatorState state, bool localOnly, List<ParameterDriverEntry> entries, out string error)
        {
            error = null;

            var driverType = VRChatTools.FindVrcType("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver");
            if (driverType == null)
            {
                error = "Error: VRCAvatarParameterDriver type not found. Ensure VRChat SDK is installed.";
                return false;
            }

            var behaviour = state.AddStateMachineBehaviour(driverType);
            if (behaviour == null)
            {
                error = "Error: Failed to add VRCAvatarParameterDriver to state.";
                return false;
            }

            var so = new SerializedObject(behaviour);

            var localOnlyProp = so.FindProperty("localOnly");
            if (localOnlyProp != null)
                localOnlyProp.boolValue = localOnly;

            var parametersProp = so.FindProperty("parameters");
            if (parametersProp == null || !parametersProp.isArray)
            {
                error = "Error: Cannot access parameters array on VRCAvatarParameterDriver.";
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                parametersProp.InsertArrayElementAtIndex(i);
                var element = parametersProp.GetArrayElementAtIndex(i);

                var nameProp = element.FindPropertyRelative("name");
                if (nameProp != null) nameProp.stringValue = entry.name;

                // type: 0=Set, 1=Add, 2=Random, 3=Copy
                var typeProp = element.FindPropertyRelative("type");
                if (typeProp != null) typeProp.intValue = entry.type;

                var valueProp = element.FindPropertyRelative("value");
                if (valueProp != null) valueProp.floatValue = entry.value;

                if (entry.type == 3) // Copy
                {
                    var sourceProp = element.FindPropertyRelative("source");
                    if (sourceProp != null) sourceProp.stringValue = entry.source;

                    var convertProp = element.FindPropertyRelative("convertRange");
                    if (convertProp != null) convertProp.boolValue = entry.convertRange;

                    if (entry.convertRange)
                    {
                        var srcMin = element.FindPropertyRelative("sourceMin");
                        var srcMax = element.FindPropertyRelative("sourceMax");
                        var dstMin = element.FindPropertyRelative("destMin");
                        var dstMax = element.FindPropertyRelative("destMax");
                        if (srcMin != null) srcMin.floatValue = entry.sourceMin;
                        if (srcMax != null) srcMax.floatValue = entry.sourceMax;
                        if (dstMin != null) dstMin.floatValue = entry.destMin;
                        if (dstMax != null) dstMax.floatValue = entry.destMax;
                    }
                }
            }

            so.ApplyModifiedProperties();
            return true;
        }

        private struct ParameterDriverEntry
        {
            public string name;
            public int type; // 0=Set, 1=Add, 2=Random, 3=Copy
            public float value;
            public string source;
            public bool convertRange;
            public float sourceMin, sourceMax, destMin, destMax;
        }

        /// <summary>
        /// ExpressionParameters に既存チェック付きでパラメータを追加。
        /// </summary>
        private static bool EnsureExpressionParameter(string avatarRootName, string name, int valueType, bool synced, bool saved, float defaultValue, out string error)
        {
            error = null;
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                error = "Error: No VRCAvatarDescriptor found.";
                return false;
            }

            var dso = new SerializedObject(descriptor);
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
            {
                error = "Error: No ExpressionParameters assigned.";
                return false;
            }

            var paramsObj = exprParamsProp.objectReferenceValue;
            var paramsSo = new SerializedObject(paramsObj);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) { error = "Error: Cannot read parameters."; return false; }

            // Check if already exists
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var existingName = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                if (existingName == name)
                    return true; // Already exists, skip
            }

            int idx = parameters.arraySize;
            parameters.InsertArrayElementAtIndex(idx);
            var newParam = parameters.GetArrayElementAtIndex(idx);
            newParam.FindPropertyRelative("name").stringValue = name;
            newParam.FindPropertyRelative("valueType").intValue = valueType;
            newParam.FindPropertyRelative("defaultValue").floatValue = defaultValue;
            newParam.FindPropertyRelative("saved").boolValue = saved;
            newParam.FindPropertyRelative("networkSynced").boolValue = synced;

            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsObj);
            return true;
        }

        // ===== Tool 1: SetupOSCDrivenBlendTree =====

        [AgentTool(@"Create a 2D Freeform Directional blend tree layer in the FX controller, driven by two OSC float parameters. Useful for face tracking blendshape control or joystick input.
paramX/paramY: float parameters for X/Y axis (added to ExpressionParameters as Float, Local).
motionPaths: semicolon-separated asset paths to AnimationClips.
positions: semicolon-separated 'x,y' positions matching each motion.
Requires confirmation.")]
        public static string SetupOSCDrivenBlendTree(string avatarRootName, string layerName,
            string paramX, string paramY, string motionPaths, string positions)
        {
            var fxController = GetFXControllerForAvatar(avatarRootName, out string err);
            if (fxController == null) return err;

            if (string.IsNullOrEmpty(layerName)) return "Error: layerName is required.";
            if (string.IsNullOrEmpty(paramX) || string.IsNullOrEmpty(paramY))
                return "Error: paramX and paramY are required.";
            if (string.IsNullOrEmpty(motionPaths) || string.IsNullOrEmpty(positions))
                return "Error: motionPaths and positions are required.";

            var motionPathArray = motionPaths.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            var positionArray = positions.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

            if (motionPathArray.Length != positionArray.Length)
                return $"Error: motionPaths ({motionPathArray.Length}) and positions ({positionArray.Length}) must have the same count.";

            if (motionPathArray.Length == 0)
                return "Error: At least one motion is required.";

            // Parse positions
            var parsedPositions = new List<Vector2>();
            foreach (var posStr in positionArray)
            {
                var parts = posStr.Split(',');
                if (parts.Length != 2 ||
                    !float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    return $"Error: Invalid position format '{posStr}'. Use 'x,y'.";
                parsedPositions.Add(new Vector2(x, y));
            }

            // Load motions
            var motions = new List<Motion>();
            foreach (var path in motionPathArray)
            {
                var motion = AssetDatabase.LoadAssetAtPath<Motion>(path);
                if (motion == null) return $"Error: Motion not found at '{path}'.";
                motions.Add(motion);
            }

            // Check layer name collision
            if (fxController.layers.Any(l => l.name == layerName))
                return $"Error: Layer '{layerName}' already exists in FX controller.";

            if (!AgentSettings.RequestConfirmation(
                "OSCブレンドツリーレイヤーの作成",
                $"FXコントローラーに 2D Freeform Directional ブレンドツリーレイヤーを追加します。\n" +
                $"レイヤー名: {layerName}\n" +
                $"パラメータ: X={paramX}, Y={paramY}\n" +
                $"モーション数: {motions.Count}\n" +
                $"ExpressionParametersにも Float, Local で自動追加されます。"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(fxController, "Setup OSC Driven BlendTree");

            // Ensure animator parameters
            FindOrAddAnimatorParam(fxController, paramX, AnimatorControllerParameterType.Float);
            FindOrAddAnimatorParam(fxController, paramY, AnimatorControllerParameterType.Float);

            // Ensure expression parameters
            EnsureExpressionParameter(avatarRootName, paramX, 1, false, false, 0f, out _);
            EnsureExpressionParameter(avatarRootName, paramY, 1, false, false, 0f, out _);

            // Create layer
            bool useWriteDefaults = VRChatTools.DetectWriteDefaults(fxController);

            fxController.AddLayer(layerName);
            var layers = fxController.layers;
            var newLayer = layers[layers.Length - 1];
            newLayer.defaultWeight = 1f;
            fxController.layers = layers;

            var sm = fxController.layers[fxController.layers.Length - 1].stateMachine;

            // Create blend tree state
            var btState = sm.AddState("BlendTree");
            btState.writeDefaultValues = useWriteDefaults;

            var blendTree = new BlendTree();
            blendTree.name = layerName + "_BlendTree";
            blendTree.blendType = BlendTreeType.FreeformDirectional2D;
            blendTree.useAutomaticThresholds = false;
            blendTree.blendParameter = paramX;
            blendTree.blendParameterY = paramY;

            AssetDatabase.AddObjectToAsset(blendTree, fxController);

            for (int i = 0; i < motions.Count; i++)
                blendTree.AddChild(motions[i], parsedPositions[i]);

            btState.motion = blendTree;
            sm.defaultState = btState;

            EditorUtility.SetDirty(fxController);
            AssetDatabase.SaveAssets();

            return $"Success: Created OSC-driven BlendTree layer '{layerName}' with {motions.Count} motions.\n" +
                   $"  Parameters: {paramX} (X), {paramY} (Y) - Float, Local\n" +
                   $"  BlendType: FreeformDirectional2D\n" +
                   $"  Write Defaults: {useWriteDefaults}";
        }

        // ===== Tool 2: SetupOSCSmoothingLayer =====

        [AgentTool(@"Create an OSC input smoothing layer using Motion Time technique. Smooths raw OSC float input for visual appeal.
paramName: the raw OSC input parameter (Float, should already exist or will be added as Local).
Creates '{paramName}Smoothed' as the smoothed output parameter.
smoothingSpeed: 0.1 (very smooth) to 1.0 (instant). Controls animation curve easing.
Requires confirmation.")]
        public static string SetupOSCSmoothingLayer(string avatarRootName, string paramName, float smoothingSpeed = 0.5f)
        {
            var fxController = GetFXControllerForAvatar(avatarRootName, out string err);
            if (fxController == null) return err;

            if (string.IsNullOrEmpty(paramName))
                return "Error: paramName is required.";

            smoothingSpeed = Mathf.Clamp(smoothingSpeed, 0.01f, 1f);

            string smoothedParam = paramName + "Smoothed";
            string layerName = $"OSC_Smooth_{paramName}";

            if (fxController.layers.Any(l => l.name == layerName))
                return $"Error: Layer '{layerName}' already exists.";

            if (!AgentSettings.RequestConfirmation(
                "OSCスムージングレイヤーの作成",
                $"Motion Time方式のスムージングレイヤーを作成します。\n" +
                $"入力: {paramName} (raw OSC)\n" +
                $"出力: {smoothedParam} (smoothed)\n" +
                $"速度: {smoothingSpeed}\n" +
                $"レイヤー名: {layerName}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(fxController, "Setup OSC Smoothing Layer");

            // Ensure animator parameters
            FindOrAddAnimatorParam(fxController, paramName, AnimatorControllerParameterType.Float);
            FindOrAddAnimatorParam(fxController, smoothedParam, AnimatorControllerParameterType.Float);

            // Ensure expression parameters
            EnsureExpressionParameter(avatarRootName, paramName, 1, false, false, 0f, out _);
            EnsureExpressionParameter(avatarRootName, smoothedParam, 1, false, false, 0f, out _);

            // Create smoothing animation clip (0 -> 1 linear over 1 second)
            string clipDir = PackagePaths.GetGeneratedDir("OSC");
            if (!System.IO.Directory.Exists(clipDir))
                System.IO.Directory.CreateDirectory(clipDir);

            string clipPath = $"{clipDir}/{paramName}_Smooth.anim";
            clipPath = AssetDatabase.GenerateUniqueAssetPath(clipPath);

            var clip = new AnimationClip();
            clip.name = $"{paramName}_Smooth";

            // The clip drives the smoothed parameter from 0 to 1
            // When used with Motion Time, the parameter controls playback position
            var binding = new EditorCurveBinding
            {
                path = "",
                type = typeof(Animator),
                propertyName = smoothedParam
            };

            Keyframe[] keys;
            if (smoothingSpeed >= 0.9f)
            {
                // Near-instant: linear
                keys = new[] { new Keyframe(0f, 0f), new Keyframe(1f, 1f) };
            }
            else
            {
                // Smooth: ease curve
                keys = new[]
                {
                    new Keyframe(0f, 0f) { outTangent = smoothingSpeed * 2f },
                    new Keyframe(0.5f, 0.5f),
                    new Keyframe(1f, 1f) { inTangent = smoothingSpeed * 2f }
                };
            }

            var curve = new AnimationCurve(keys);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            AssetDatabase.CreateAsset(clip, clipPath);

            // Create layer
            bool useWriteDefaults = VRChatTools.DetectWriteDefaults(fxController);

            fxController.AddLayer(layerName);
            var layers = fxController.layers;
            var newLayer = layers[layers.Length - 1];
            newLayer.defaultWeight = 1f;
            fxController.layers = layers;

            var sm = fxController.layers[fxController.layers.Length - 1].stateMachine;

            var state = sm.AddState("Smoothing");
            state.motion = clip;
            state.writeDefaultValues = useWriteDefaults;
            state.timeParameterActive = true;
            state.timeParameter = paramName;
            state.speed = 1f;

            sm.defaultState = state;

            EditorUtility.SetDirty(fxController);
            AssetDatabase.SaveAssets();

            return $"Success: Created OSC smoothing layer '{layerName}'.\n" +
                   $"  Input: {paramName} (raw OSC, Float)\n" +
                   $"  Output: {smoothedParam} (smoothed, Float)\n" +
                   $"  Speed: {smoothingSpeed}\n" +
                   $"  Clip: {clipPath}\n" +
                   $"  Use '{smoothedParam}' in your blend trees/animations for smooth control.";
        }

        // ===== Tool 3: SetupOSCParameterDriver =====

        [AgentTool(@"Create a Parameter Driver layer that copies/remaps one OSC parameter to another. Useful for range conversion (e.g. HR 60-200 BPM -> 0-1 Float).
sourceParam: the source parameter name.
destParam: the destination parameter name.
driverType: 'copy' (direct copy) or 'remap' (range conversion).
For remap: sourceMin/sourceMax define the input range, destMin/destMax define the output range.
Requires confirmation.")]
        public static string SetupOSCParameterDriver(string avatarRootName, string sourceParam, string destParam,
            string driverType = "copy", float sourceMin = 0f, float sourceMax = 1f, float destMin = 0f, float destMax = 1f)
        {
            var fxController = GetFXControllerForAvatar(avatarRootName, out string err);
            if (fxController == null) return err;

            if (string.IsNullOrEmpty(sourceParam) || string.IsNullOrEmpty(destParam))
                return "Error: sourceParam and destParam are required.";

            bool isRemap = driverType.Equals("remap", StringComparison.OrdinalIgnoreCase);
            string layerName = $"OSC_Driver_{sourceParam}_to_{destParam}";

            if (fxController.layers.Any(l => l.name == layerName))
                return $"Error: Layer '{layerName}' already exists.";

            string details = isRemap
                ? $"Copy+Remap: {sourceParam} [{sourceMin}-{sourceMax}] -> {destParam} [{destMin}-{destMax}]"
                : $"Copy: {sourceParam} -> {destParam}";

            if (!AgentSettings.RequestConfirmation(
                "OSC Parameter Driverの作成",
                $"Parameter Driverレイヤーを作成します。\n" +
                $"{details}\n" +
                $"レイヤー名: {layerName}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(fxController, "Setup OSC Parameter Driver");

            // Ensure animator parameters
            FindOrAddAnimatorParam(fxController, sourceParam, AnimatorControllerParameterType.Float);
            FindOrAddAnimatorParam(fxController, destParam, AnimatorControllerParameterType.Float);

            // Ensure expression parameters
            EnsureExpressionParameter(avatarRootName, sourceParam, 1, false, false, 0f, out _);
            EnsureExpressionParameter(avatarRootName, destParam, 1, false, false, 0f, out _);

            // Create layer
            bool useWriteDefaults = VRChatTools.DetectWriteDefaults(fxController);

            fxController.AddLayer(layerName);
            var layers = fxController.layers;
            var newLayer = layers[layers.Length - 1];
            newLayer.defaultWeight = 1f;
            fxController.layers = layers;

            var sm = fxController.layers[fxController.layers.Length - 1].stateMachine;

            // Create empty animation clip for the state
            string clipDir = PackagePaths.GetGeneratedDir("OSC");
            if (!System.IO.Directory.Exists(clipDir))
                System.IO.Directory.CreateDirectory(clipDir);

            string clipPath = $"{clipDir}/{sourceParam}_to_{destParam}_empty.anim";
            clipPath = AssetDatabase.GenerateUniqueAssetPath(clipPath);
            var emptyClip = new AnimationClip { name = "Empty" };
            AssetDatabase.CreateAsset(emptyClip, clipPath);

            var state = sm.AddState("Driver");
            state.motion = emptyClip;
            state.writeDefaultValues = useWriteDefaults;

            sm.defaultState = state;

            // Add Parameter Driver
            var entry = new ParameterDriverEntry
            {
                name = destParam,
                type = 3, // Copy
                source = sourceParam,
                convertRange = isRemap,
                sourceMin = sourceMin,
                sourceMax = sourceMax,
                destMin = destMin,
                destMax = destMax
            };

            if (!AddParameterDriverToState(state, true, new List<ParameterDriverEntry> { entry }, out string driverErr))
            {
                // Clean up the layer if driver failed
                return driverErr ?? "Error: Failed to add Parameter Driver.";
            }

            EditorUtility.SetDirty(fxController);
            AssetDatabase.SaveAssets();

            return $"Success: Created Parameter Driver layer '{layerName}'.\n  {details}";
        }

        // ===== Tool 4: SetupMultiplexReceiver =====

        [AgentTool(@"Create a time-division multiplexing state machine to receive more data than the 256-bit sync budget allows. Uses an index parameter (Int, Synced) as pointer and a payload parameter (Float, Synced) as data carrier.
indexParam: Int parameter for selecting which target to write to.
payloadParam: Float parameter carrying the value.
targetParamPrefix: prefix for target parameters (creates {prefix}_0 to {prefix}_{count-1}, all Float, Local).
count: number of target parameters (max 32).
Budget cost: 8+8=16 bits synced (index+payload only).
Requires confirmation.")]
        public static string SetupMultiplexReceiver(string avatarRootName, string indexParam, string payloadParam,
            string targetParamPrefix, int count = 8)
        {
            var fxController = GetFXControllerForAvatar(avatarRootName, out string err);
            if (fxController == null) return err;

            if (string.IsNullOrEmpty(indexParam) || string.IsNullOrEmpty(payloadParam) || string.IsNullOrEmpty(targetParamPrefix))
                return "Error: indexParam, payloadParam, and targetParamPrefix are required.";

            if (count < 2) return "Error: count must be at least 2.";
            if (count > 32)
                return "Error: count exceeds maximum of 32. Large state machines can cause performance issues.";

            string layerName = $"OSC_Mux_{targetParamPrefix}";

            if (fxController.layers.Any(l => l.name == layerName))
                return $"Error: Layer '{layerName}' already exists.";

            string warning = count > 16 ? $"\n[WARN] {count} states is a large multiplexer. Consider reducing count." : "";

            if (!AgentSettings.RequestConfirmation(
                "マルチプレクサレシーバーの構築",
                $"時分割多重化ステートマシンを構築します。\n" +
                $"インデックス: {indexParam} (Int, Synced, 8bit)\n" +
                $"ペイロード: {payloadParam} (Float, Synced, 8bit)\n" +
                $"ターゲット: {targetParamPrefix}_0 ~ {targetParamPrefix}_{count - 1} (Float, Local)\n" +
                $"ステート数: {count}\n" +
                $"Budget消費: 16bit (Synced分のみ){warning}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(fxController, "Setup Multiplex Receiver");

            // Ensure animator parameters
            FindOrAddAnimatorParam(fxController, indexParam, AnimatorControllerParameterType.Int);
            FindOrAddAnimatorParam(fxController, payloadParam, AnimatorControllerParameterType.Float);

            // Ensure expression parameters (synced for index and payload)
            EnsureExpressionParameter(avatarRootName, indexParam, 0, true, false, 0f, out _);  // Int, Synced
            EnsureExpressionParameter(avatarRootName, payloadParam, 1, true, false, 0f, out _); // Float, Synced

            // Create target parameters (all Local)
            for (int i = 0; i < count; i++)
            {
                string targetName = $"{targetParamPrefix}_{i}";
                FindOrAddAnimatorParam(fxController, targetName, AnimatorControllerParameterType.Float);
                EnsureExpressionParameter(avatarRootName, targetName, 1, false, false, 0f, out _);
            }

            // Create layer
            bool useWriteDefaults = VRChatTools.DetectWriteDefaults(fxController);

            fxController.AddLayer(layerName);
            var layers = fxController.layers;
            var newLayer = layers[layers.Length - 1];
            newLayer.defaultWeight = 1f;
            fxController.layers = layers;

            var sm = fxController.layers[fxController.layers.Length - 1].stateMachine;

            // Create empty clip for states
            string clipDir = PackagePaths.GetGeneratedDir("OSC");
            if (!System.IO.Directory.Exists(clipDir))
                System.IO.Directory.CreateDirectory(clipDir);

            string clipPath = $"{clipDir}/{targetParamPrefix}_mux_empty.anim";
            clipPath = AssetDatabase.GenerateUniqueAssetPath(clipPath);
            var emptyClip = new AnimationClip { name = "MuxEmpty" };
            AssetDatabase.CreateAsset(emptyClip, clipPath);

            // Create idle/wait state as default
            var idleState = sm.AddState("Idle");
            idleState.motion = emptyClip;
            idleState.writeDefaultValues = useWriteDefaults;
            sm.defaultState = idleState;

            // Create states for each index
            var states = new AnimatorState[count];
            for (int i = 0; i < count; i++)
            {
                string targetName = $"{targetParamPrefix}_{i}";
                states[i] = sm.AddState($"Set_{i}");
                states[i].motion = emptyClip;
                states[i].writeDefaultValues = useWriteDefaults;

                // Add parameter driver: Copy payload -> target_i
                var driverEntry = new ParameterDriverEntry
                {
                    name = targetName,
                    type = 3, // Copy
                    source = payloadParam,
                    convertRange = false,
                };

                AddParameterDriverToState(states[i], true, new List<ParameterDriverEntry> { driverEntry }, out _);

                // Transition from Idle to this state when index == i
                var toState = idleState.AddTransition(states[i]);
                toState.hasExitTime = false;
                toState.duration = 0f;
                toState.AddCondition(AnimatorConditionMode.Equals, i, indexParam);

                // Exit transition back to idle
                var toIdle = states[i].AddExitTransition();
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0f;
                toIdle.duration = 0f;
            }

            EditorUtility.SetDirty(fxController);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created multiplex receiver '{layerName}' with {count} channels.");
            sb.AppendLine($"  Index: {indexParam} (Int, Synced, 8bit)");
            sb.AppendLine($"  Payload: {payloadParam} (Float, Synced, 8bit)");
            sb.AppendLine($"  Targets: {targetParamPrefix}_0 ~ {targetParamPrefix}_{count - 1} (Float, Local)");
            sb.AppendLine($"  Total budget cost: 16 bits");
            sb.AppendLine($"  Effective channels: {count} (vs {count * 8} bits if individually synced)");
            sb.AppendLine();
            sb.AppendLine("Usage: Set index to the target channel, then update payload with the value.");
            sb.AppendLine("The state machine copies payload to the selected target parameter.");

            return sb.ToString().TrimEnd();
        }

        // ===== Tool 5: ExplainOSCDataTypes =====

        [AgentTool("Reference guide for VRChat OSC data types, conversion rules, quantization behavior, and best practices. No parameters needed.")]
        public static string ExplainOSCDataTypes()
        {
            var sb = new StringBuilder();
            sb.AppendLine("VRChat OSC Data Types & Best Practices");
            sb.AppendLine("=======================================");
            sb.AppendLine();
            sb.AppendLine("== Data Types ==");
            sb.AppendLine("  Bool   - true/false. OSC: Int (0 or 1). Sync cost: 1 bit.");
            sb.AppendLine("  Int    - Integer. OSC: Int (clamped to 0-255 for synced). Sync cost: 8 bits.");
            sb.AppendLine("  Float  - Floating point. OSC: Float. Sync cost: 8 bits.");
            sb.AppendLine();
            sb.AppendLine("== Quantization (Synced parameters only) ==");
            sb.AppendLine("  Synced Float: quantized to 8 bits (256 steps).");
            sb.AppendLine("    Range -1.0 to 1.0: resolution ~0.0078 per step.");
            sb.AppendLine("    Range  0.0 to 1.0: resolution ~0.0039 per step.");
            sb.AppendLine("  Synced Int: clamped to unsigned 8-bit (0-255).");
            sb.AppendLine("  Synced Bool: 1 bit, no quantization.");
            sb.AppendLine("  Local parameters: NO quantization, full precision.");
            sb.AppendLine();
            sb.AppendLine("== OSC Type Conversion ==");
            sb.AppendLine("  OSC Int   -> VRC Bool:  0 = false, non-zero = true");
            sb.AppendLine("  OSC Int   -> VRC Int:   direct (clamped if synced)");
            sb.AppendLine("  OSC Int   -> VRC Float: cast to float");
            sb.AppendLine("  OSC Float -> VRC Bool:  0.0 = false, non-zero = true");
            sb.AppendLine("  OSC Float -> VRC Int:   truncated to integer");
            sb.AppendLine("  OSC Float -> VRC Float: direct");
            sb.AppendLine("  OSC Bool  -> VRC Bool:  direct");
            sb.AppendLine("  OSC Bool  -> VRC Int:   false=0, true=1");
            sb.AppendLine("  OSC Bool  -> VRC Float: false=0.0, true=1.0");
            sb.AppendLine();
            sb.AppendLine("== Budget ==");
            sb.AppendLine("  Total sync budget: 256 bits per avatar.");
            sb.AppendLine("  Bool=1bit, Int=8bits, Float=8bits.");
            sb.AppendLine("  Local (non-synced) parameters: zero budget cost.");
            sb.AppendLine("  Built-in parameters (Viseme, Gesture, etc.): not counted.");
            sb.AppendLine();
            sb.AppendLine("== Best Practices ==");
            sb.AppendLine("  1. Use Local for OSC-only parameters (face tracking, HR, telemetry).");
            sb.AppendLine("     These don't need to sync to other players.");
            sb.AppendLine("  2. Use Float for smooth/continuous values, Bool for toggles.");
            sb.AppendLine("  3. For face tracking: all params should be Float, Local, non-Saved.");
            sb.AppendLine("  4. For high-precision needs: use Local Float (no quantization).");
            sb.AppendLine("  5. Use time-division multiplexing to exceed the 256-bit limit.");
            sb.AppendLine("     (See SetupMultiplexReceiver tool)");
            sb.AppendLine("  6. Apply smoothing to raw OSC inputs for visual quality.");
            sb.AppendLine("     (See SetupOSCSmoothingLayer tool)");
            sb.AppendLine("  7. Delete/regenerate OSC config after parameter changes.");
            sb.AppendLine("     VRChat caches the config and won't detect changes automatically.");
            sb.AppendLine("  8. Test with VRChat's OSC debug panel (Action Menu > Options > OSC > Debug).");

            return sb.ToString().TrimEnd();
        }
    }
}
