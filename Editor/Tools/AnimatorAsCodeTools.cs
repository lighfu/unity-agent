#if ANIMATOR_AS_CODE
using AnimatorAsCode.V1;
using AnimatorAsCode.V1.VRC;
#if MODULAR_AVATAR
using AnimatorAsCode.V1.ModularAvatar;
#endif
#endif
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AnimatorAsCodeTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
#if ANIMATOR_AS_CODE && MODULAR_AVATAR

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return null;
            return go;
        }

        private static string ResolveAssetDir(string assetDir, string avatarRootName)
        {
            if (!string.IsNullOrEmpty(assetDir)) return assetDir;
            return $"Assets/紫陽花広場/GeneratedAssets/{avatarRootName}";
        }

        private static void EnsureDirectoryExists(string assetDir)
        {
            if (!AssetDatabase.IsValidFolder(assetDir))
            {
                var parts = assetDir.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }
        }

        private static AacFlBase CreateAacBase(string systemName, Transform root, string assetDir)
        {
            EnsureDirectoryExists(assetDir);

            // Create a container ScriptableObject to hold generated assets
            var containerPath = $"{assetDir}/AAC_{systemName}_Container.asset";
            var container = AssetDatabase.LoadAssetAtPath<AnimatorController>(containerPath);
            if (container == null)
            {
                container = new AnimatorController();
                AssetDatabase.CreateAsset(container, containerPath);
            }

            var config = new AacConfiguration
            {
                SystemName = systemName,
                AnimatorRoot = root,
                DefaultValueRoot = root,
                AssetContainer = container,
                ContainerMode = AacConfiguration.Container.Everything,
                AssetKey = systemName,
                DefaultsProvider = new AacDefaultsProvider(writeDefaults: false)
            };

            return AacV1.Create(config);
        }

        private static AnimatorController SaveController(AacFlController aacCtrl, string assetDir, string systemName)
        {
            var ctrl = aacCtrl.AnimatorController;
            var path = $"{assetDir}/AAC_{systemName}_FX.controller";

            // Check if already at that path
            var existingPath = AssetDatabase.GetAssetPath(ctrl);
            if (string.IsNullOrEmpty(existingPath))
            {
                AssetDatabase.CreateAsset(ctrl, path);
            }

            AssetDatabase.SaveAssets();
            return ctrl;
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

        // ========== 1. CreateSimpleToggle ==========

        [AgentTool("Create a complete toggle system: FX layer with ON/OFF states + MA merge + menu item. objects: semicolon-separated paths to toggle. Uses Animator-as-Code + Modular Avatar.")]
        public static string CreateSimpleToggle(string avatarRootName, string toggleName,
            string objects, string saved = "true", string defaultOn = "false",
            string assetDir = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var objectPaths = objects.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (objectPaths.Length == 0)
                return "Error: No object paths specified.";

            // Validate objects exist
            var targetObjects = new List<GameObject>();
            foreach (var path in objectPaths)
            {
                var target = avatarRoot.transform.Find(path);
                if (target == null)
                    return $"Error: Object '{path}' not found under '{avatarRootName}'.";
                targetObjects.Add(target.gameObject);
            }

            bool isSaved = saved.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isDefaultOn = defaultOn.Equals("true", StringComparison.OrdinalIgnoreCase);
            assetDir = ResolveAssetDir(assetDir, avatarRootName);

            if (!AgentSettings.RequestConfirmation(
                "AAC トグルシステムの作成",
                $"トグル名: {toggleName}\n" +
                $"対象: {string.Join(", ", objectPaths)}\n" +
                $"デフォルト: {(isDefaultOn ? "ON" : "OFF")}, 保存: {isSaved}\n" +
                $"出力先: {assetDir}"))
                return "Cancelled: User denied the operation.";

            var systemName = toggleName.Replace(" ", "_");
            var aac = CreateAacBase(systemName, avatarRoot.transform, assetDir);

            // Create controller and layer
            var ctrl = aac.NewAnimatorController();
            var layer = ctrl.NewLayer(systemName);
            var param = layer.BoolParameter(toggleName);

            // Create OFF state (objects inactive)
            var offClip = aac.NewClip($"{systemName}_OFF");
            foreach (var obj in targetObjects)
                offClip.Toggling(obj, false);
            var offState = layer.NewState("OFF").WithAnimation(offClip);

            // Create ON state (objects active)
            var onClip = aac.NewClip($"{systemName}_ON");
            foreach (var obj in targetObjects)
                onClip.Toggling(obj, true);
            var onState = layer.NewState("ON").WithAnimation(onClip);

            // Transitions
            offState.TransitionsTo(onState).When(param.IsTrue());
            onState.TransitionsTo(offState).When(param.IsFalse());

            // Set default state
            if (isDefaultOn)
                layer.WithDefaultState(onState);

            // Save controller
            var savedCtrl = SaveController(ctrl, assetDir, systemName);

            // Setup MA components on a holder GameObject
            var holderName = $"AAC_{toggleName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create AAC Toggle");
            holder.transform.SetParent(avatarRoot.transform, false);

            var maAc = MaAc.Create(holder);
            maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);

            var maParam = maAc.NewParameter(param);
            if (isSaved && isDefaultOn)
                maParam.WithDefaultValue(true);
            else if (!isSaved)
                maParam.NotSaved();

            maAc.EditMenuItemOnSelf().Toggle(param).Name(toggleName);

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created AAC toggle system '{toggleName}'.");
            sb.AppendLine($"  Controller: {assetDir}/AAC_{systemName}_FX.controller");
            sb.AppendLine($"  Objects ({targetObjects.Count}):");
            foreach (var obj in targetObjects)
                sb.AppendLine($"    - {GetRelativePath(avatarRoot.transform, obj.transform)}");
            sb.AppendLine($"  Default: {(isDefaultOn ? "ON" : "OFF")}, Saved: {isSaved}");
            sb.AppendLine($"  MA components added to '{holderName}'.");

            return sb.ToString().TrimEnd();
        }

        // ========== 2. CreateBlendShapeToggle ==========

        [AgentTool("Create a toggle that drives blend shapes via FX layer + MA. blendShapes: 'name=value;name2=value2'. meshObjectName: object with SkinnedMeshRenderer.")]
        public static string CreateBlendShapeToggle(string avatarRootName, string toggleName,
            string meshObjectName, string blendShapes,
            string saved = "true", string defaultOn = "false", string assetDir = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var meshObj = FindGO(meshObjectName);
            if (meshObj == null)
                return $"Error: GameObject '{meshObjectName}' not found.";

            var smr = meshObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{meshObjectName}' does not have a SkinnedMeshRenderer.";

            // Parse blend shapes
            var bsPairs = new List<(string name, float value)>();
            foreach (var entry in blendShapes.Split(';'))
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split('=');
                if (parts.Length != 2)
                    return $"Error: Invalid blend shape entry '{trimmed}'. Expected 'name=value'.";
                if (!float.TryParse(parts[1].Trim(), out float value))
                    return $"Error: Invalid value '{parts[1].Trim()}'.";
                bsPairs.Add((parts[0].Trim(), value));
            }

            if (bsPairs.Count == 0)
                return "Error: No blend shapes specified.";

            bool isSaved = saved.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isDefaultOn = defaultOn.Equals("true", StringComparison.OrdinalIgnoreCase);
            assetDir = ResolveAssetDir(assetDir, avatarRootName);

            if (!AgentSettings.RequestConfirmation(
                "AAC BlendShapeトグルの作成",
                $"トグル名: {toggleName}\n" +
                $"メッシュ: {meshObjectName}\n" +
                $"BlendShapes: {string.Join(", ", bsPairs.Select(p => $"{p.name}={p.value}"))}"))
                return "Cancelled: User denied the operation.";

            var systemName = toggleName.Replace(" ", "_");
            var aac = CreateAacBase(systemName, avatarRoot.transform, assetDir);

            var ctrl = aac.NewAnimatorController();
            var layer = ctrl.NewLayer(systemName);
            var param = layer.BoolParameter(toggleName);

            // OFF state: blend shapes at 0
            var offClip = aac.NewClip($"{systemName}_OFF");
            foreach (var (name, _) in bsPairs)
                offClip.BlendShape(smr, name, 0f);
            var offState = layer.NewState("OFF").WithAnimation(offClip);

            // ON state: blend shapes at target value
            var onClip = aac.NewClip($"{systemName}_ON");
            foreach (var (name, value) in bsPairs)
                onClip.BlendShape(smr, name, value);
            var onState = layer.NewState("ON").WithAnimation(onClip);

            offState.TransitionsTo(onState).When(param.IsTrue());
            onState.TransitionsTo(offState).When(param.IsFalse());

            if (isDefaultOn)
                layer.WithDefaultState(onState);

            SaveController(ctrl, assetDir, systemName);

            // MA setup
            var holderName = $"AAC_{toggleName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create AAC BlendShape Toggle");
            holder.transform.SetParent(avatarRoot.transform, false);

            var maAc = MaAc.Create(holder);
            maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);

            var maParam = maAc.NewParameter(param);
            if (isSaved && isDefaultOn)
                maParam.WithDefaultValue(true);
            else if (!isSaved)
                maParam.NotSaved();

            maAc.EditMenuItemOnSelf().Toggle(param).Name(toggleName);

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created AAC blend shape toggle '{toggleName}'.");
            sb.AppendLine($"  Mesh: {meshObjectName}");
            sb.AppendLine($"  Blend shapes:");
            foreach (var (name, value) in bsPairs)
                sb.AppendLine($"    - {name} = {value}");

            return sb.ToString().TrimEnd();
        }

        // ========== 3. CreateGestureLayer ==========

        [AgentTool("Create a gesture-driven FX layer using Animator-as-Code. Assigns animation clips to left/right hand gestures. gestureMap: 'LeftFist=clipPath;RightVictory=clipPath2'. Each entry: '{hand}{gesture}={animClipPath}'.")]
        public static string CreateGestureLayer(string avatarRootName, string layerName,
            string gestureMap, string assetDir = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            // Parse gesture map: "LeftFist=clipPath;RightVictory=clipPath2"
            var entries = new List<(string hand, string gesture, string clipPath)>();
            foreach (var entry in gestureMap.Split(';'))
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var eqParts = trimmed.Split('=');
                if (eqParts.Length != 2)
                    return $"Error: Invalid gesture entry '{trimmed}'. Expected 'HandGesture=clipPath'.";

                var key = eqParts[0].Trim();
                var clipPath = eqParts[1].Trim();

                // Parse hand and gesture from key (e.g., "LeftFist", "RightVictory")
                string hand, gesture;
                if (key.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                {
                    hand = "Left";
                    gesture = key.Substring(4);
                }
                else if (key.StartsWith("Right", StringComparison.OrdinalIgnoreCase))
                {
                    hand = "Right";
                    gesture = key.Substring(5);
                }
                else
                {
                    return $"Error: Gesture key '{key}' must start with 'Left' or 'Right'.";
                }

                entries.Add((hand, gesture, clipPath));
            }

            if (entries.Count == 0)
                return "Error: No gesture entries specified.";

            assetDir = ResolveAssetDir(assetDir, avatarRootName);

            if (!AgentSettings.RequestConfirmation(
                "AAC ジェスチャーレイヤーの作成",
                $"レイヤー名: {layerName}\n" +
                $"ジェスチャー数: {entries.Count}\n" +
                $"出力先: {assetDir}"))
                return "Cancelled: User denied the operation.";

            var systemName = layerName.Replace(" ", "_");
            var aac = CreateAacBase(systemName, avatarRoot.transform, assetDir);

            var ctrl = aac.NewAnimatorController();
            var layer = ctrl.NewLayer(systemName);

            var gestureLeft = layer.IntParameter("GestureLeft");
            var gestureRight = layer.IntParameter("GestureRight");

            // Map gesture names to int values
            var gestureValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Neutral", 0 }, { "Fist", 1 }, { "HandOpen", 2 }, { "Fingerpoint", 3 },
                { "Victory", 4 }, { "RockNRoll", 5 }, { "HandGun", 6 }, { "ThumbsUp", 7 }
            };

            // Default idle state
            var idleClip = aac.DummyClipLasting(1f / 60f, AacFlUnit.Frames);
            var idleState = layer.NewState("Idle").WithAnimation(idleClip);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created AAC gesture layer '{layerName}'.");

            foreach (var (hand, gesture, clipPath) in entries)
            {
                if (!gestureValues.TryGetValue(gesture, out int gestureValue))
                {
                    sb.AppendLine($"  Warning: Unknown gesture '{gesture}', skipped.");
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                {
                    sb.AppendLine($"  Warning: Clip not found at '{clipPath}', skipped.");
                    continue;
                }

                var stateName = $"{hand}_{gesture}";
                var state = layer.NewState(stateName).WithAnimation(clip);

                var gestureParam = hand == "Left" ? gestureLeft : gestureRight;
                idleState.TransitionsTo(state).When(gestureParam.IsEqualTo(gestureValue));
                state.TransitionsTo(idleState).When(gestureParam.IsNotEqualTo(gestureValue));

                sb.AppendLine($"  {hand}{gesture} (={gestureValue}) → {clip.name}");
            }

            SaveController(ctrl, assetDir, systemName);

            // MA setup
            var holderName = $"AAC_{layerName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create AAC Gesture Layer");
            holder.transform.SetParent(avatarRoot.transform, false);

            var maAc = MaAc.Create(holder);
            maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();

            sb.AppendLine($"  MA MergeAnimator added to '{holderName}'.");
            return sb.ToString().TrimEnd();
        }

        // ========== 4. CreateRadialPuppet ==========

        [AgentTool("Create a radial puppet (0-1 float slider) with 1D blend tree + MA menu. clip0Path: animation at 0%, clip1Path: animation at 100%.")]
        public static string CreateRadialPuppet(string avatarRootName, string paramName,
            string clip0Path, string clip1Path, string saved = "true", string assetDir = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var clip0 = AssetDatabase.LoadAssetAtPath<AnimationClip>(clip0Path);
            if (clip0 == null)
                return $"Error: Animation clip not found at '{clip0Path}'.";

            var clip1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(clip1Path);
            if (clip1 == null)
                return $"Error: Animation clip not found at '{clip1Path}'.";

            bool isSaved = saved.Equals("true", StringComparison.OrdinalIgnoreCase);
            assetDir = ResolveAssetDir(assetDir, avatarRootName);

            if (!AgentSettings.RequestConfirmation(
                "AAC ラジアルパペットの作成",
                $"パラメータ: {paramName}\n" +
                $"0%: {clip0.name}\n" +
                $"100%: {clip1.name}\n" +
                $"保存: {isSaved}"))
                return "Cancelled: User denied the operation.";

            var systemName = paramName.Replace(" ", "_");
            var aac = CreateAacBase(systemName, avatarRoot.transform, assetDir);

            var ctrl = aac.NewAnimatorController();
            var layer = ctrl.NewLayer(systemName);
            var param = layer.FloatParameter(paramName);

            // Create 1D blend tree
            var blendTree = aac.NewBlendTree().Simple1D(param)
                .WithAnimation(aac.NewClip().NonLooping().Animating(edit => {
                    // Copy keyframes from clip0
                }), 0f)
                .WithAnimation(aac.NewClip().NonLooping().Animating(edit => {
                    // Copy keyframes from clip1
                }), 1f);

            // Actually use the original clips directly
            var bt = aac.NewBlendTree().Simple1D(param)
                .WithAnimation(aac.CopyClip(clip0), 0f)
                .WithAnimation(aac.CopyClip(clip1), 1f);

            var state = layer.NewState(paramName).WithAnimation(bt);

            SaveController(ctrl, assetDir, systemName);

            // MA setup
            var holderName = $"AAC_{paramName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create AAC Radial Puppet");
            holder.transform.SetParent(avatarRoot.transform, false);

            var maAc = MaAc.Create(holder);
            maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);

            var maParam = maAc.NewParameter(param);
            if (!isSaved) maParam.NotSaved();

            maAc.EditMenuItemOnSelf().Radial(param).Name(paramName);

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();

            return $"Success: Created AAC radial puppet '{paramName}'.\n" +
                   $"  0%: {clip0.name}\n" +
                   $"  100%: {clip1.name}\n" +
                   $"  Controller: {assetDir}/AAC_{systemName}_FX.controller\n" +
                   $"  MA components added to '{holderName}'.";
        }

        // ========== 5. CreateIntToggleSwap ==========

        [AgentTool("Create an int-based multi-state swap (e.g., outfit swap). Each state uses a different int value. states: 'name=objectPaths;name2=objectPaths2' where each state turns on its objects and turns off others.")]
        public static string CreateIntToggleSwap(string avatarRootName, string paramName,
            string states, string saved = "true", string defaultState = "0", string assetDir = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            // Parse states: "name=path1,path2;name2=path3,path4"
            var stateEntries = new List<(string name, List<GameObject> objects)>();
            var allObjects = new List<GameObject>();

            foreach (var entry in states.Split(';'))
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var eqParts = trimmed.Split('=');
                if (eqParts.Length != 2)
                    return $"Error: Invalid state entry '{trimmed}'. Expected 'name=path1,path2'.";

                var name = eqParts[0].Trim();
                var pathList = eqParts[1].Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();

                var objs = new List<GameObject>();
                foreach (var path in pathList)
                {
                    var target = avatarRoot.transform.Find(path);
                    if (target == null)
                        return $"Error: Object '{path}' not found under '{avatarRootName}'.";
                    objs.Add(target.gameObject);
                    if (!allObjects.Contains(target.gameObject))
                        allObjects.Add(target.gameObject);
                }
                stateEntries.Add((name, objs));
            }

            if (stateEntries.Count < 2)
                return "Error: At least 2 states are required for int swap.";

            bool isSaved = saved.Equals("true", StringComparison.OrdinalIgnoreCase);
            int defaultIdx = 0;
            int.TryParse(defaultState, out defaultIdx);
            assetDir = ResolveAssetDir(assetDir, avatarRootName);

            if (!AgentSettings.RequestConfirmation(
                "AAC 整数スワップの作成",
                $"パラメータ: {paramName}\n" +
                $"状態数: {stateEntries.Count}\n" +
                $"デフォルト: {defaultIdx}\n" +
                $"保存: {isSaved}"))
                return "Cancelled: User denied the operation.";

            var systemName = paramName.Replace(" ", "_");
            var aac = CreateAacBase(systemName, avatarRoot.transform, assetDir);

            var ctrl = aac.NewAnimatorController();
            var layer = ctrl.NewLayer(systemName);
            var param = layer.IntParameter(paramName);

            var aacStates = new List<AacFlState>();
            for (int i = 0; i < stateEntries.Count; i++)
            {
                var (name, objs) = stateEntries[i];
                var clip = aac.NewClip($"{systemName}_{name}");

                // Turn on this state's objects, turn off all others
                foreach (var allObj in allObjects)
                {
                    bool isOn = objs.Contains(allObj);
                    clip.Toggling(allObj, isOn);
                }

                var state = layer.NewState(name).WithAnimation(clip);
                aacStates.Add(state);
            }

            // Set default state
            if (defaultIdx >= 0 && defaultIdx < aacStates.Count)
                layer.WithDefaultState(aacStates[defaultIdx]);

            // Transitions: from any state to each state based on int value
            for (int i = 0; i < aacStates.Count; i++)
            {
                aacStates[i].TransitionsFromAny().When(param.IsEqualTo(i));
            }

            SaveController(ctrl, assetDir, systemName);

            // MA setup
            var holderName = $"AAC_{paramName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create AAC Int Swap");
            holder.transform.SetParent(avatarRoot.transform, false);

            var maAc = MaAc.Create(holder);
            maAc.NewMergeAnimator(ctrl, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX);

            var maParam = maAc.NewParameter(param);
            if (isSaved && defaultIdx > 0)
                maParam.WithDefaultValue(defaultIdx);
            else if (!isSaved)
                maParam.NotSaved();

            // Create menu items for each state
            for (int i = 0; i < stateEntries.Count; i++)
            {
                var (name, _) = stateEntries[i];
                var menuHolder = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(menuHolder, "Create AAC Menu Item");
                menuHolder.transform.SetParent(holder.transform, false);
                maAc.On(menuHolder).EditMenuItemOnSelf().ToggleSets(param, i).Name(name);
            }

            EditorUtility.SetDirty(holder);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created AAC int swap '{paramName}'.");
            for (int i = 0; i < stateEntries.Count; i++)
            {
                var (name, objs) = stateEntries[i];
                sb.AppendLine($"  [{i}] {name}: {string.Join(", ", objs.Select(o => o.name))}");
            }
            sb.AppendLine($"  Default: [{defaultIdx}]");

            return sb.ToString().TrimEnd();
        }

        // ========== 6. RemoveGeneratedAnimator ==========

        [AgentTool("Remove a previously generated AAC animator controller and associated MA components by system name.")]
        public static string RemoveGeneratedAnimator(string avatarRootName, string systemName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var holderName = $"AAC_{systemName}";
            var holder = avatarRoot.transform.Find(holderName);
            if (holder == null)
            {
                // Try with spaces replaced
                holderName = $"AAC_{systemName.Replace(" ", "_")}";
                holder = avatarRoot.transform.Find(holderName);
            }

            if (holder == null)
                return $"Error: AAC holder '{holderName}' not found under '{avatarRootName}'.";

            if (!AgentSettings.RequestConfirmation(
                "AAC システムの削除",
                $"'{holderName}' とその子オブジェクト、MAコンポーネントを削除します。\n" +
                "生成されたアセットファイルは手動で削除してください。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(holder.gameObject);

            return $"Success: Removed AAC system '{systemName}' (holder '{holderName}' destroyed).\n" +
                   $"Note: Generated assets in the asset directory should be manually deleted if no longer needed.";
        }

#endif
    }
}
