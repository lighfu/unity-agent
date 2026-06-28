using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class ModularAvatarTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

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

        // ========== 1. SetupObjectToggleMA ==========

        [AgentTool("Set up a non-destructive object toggle using MA components. Creates MA MenuItem + MA ObjectToggle. No animations or FX layers needed. defaultOn=true means the object is visible by default.")]
        public static string SetupObjectToggleMA(string avatarRootName, string targetPath, string toggleName = "", bool defaultOn = true)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var target = avatarRoot.transform.Find(targetPath);
            if (target == null)
            {
                for (int i = 0; i < avatarRoot.transform.childCount; i++)
                {
                    var child = avatarRoot.transform.GetChild(i);
                    if (child.name == targetPath)
                    {
                        target = child;
                        targetPath = child.name;
                        break;
                    }
                }
                if (target == null)
                    return $"Error: Target '{targetPath}' not found under '{avatarRootName}'.";
            }

            if (string.IsNullOrEmpty(toggleName))
                toggleName = target.name.Replace(" ", "_").Replace("-", "_");

            if (!AgentSettings.RequestConfirmation(
                "MA オブジェクトトグルのセットアップ",
                $"'{targetPath}' を非破壊オブジェクトトグルにします。\n" +
                $"トグル名: {toggleName}\n" +
                $"デフォルト: {(defaultOn ? "ON (表示)" : "OFF (非表示)")}\n\n" +
                "以下のMAコンポーネントを追加します:\n" +
                "- ModularAvatarMenuItem (メニュー項目)\n" +
                "- ModularAvatarObjectToggle (オブジェクト切り替え)\n\n" +
                "アニメーションやFXレイヤーは不要です（MAがビルド時に自動生成）。"))
                return "Cancelled: User denied the operation.";

            var sb = new StringBuilder();
            sb.AppendLine($"Setting up MA object toggle for '{targetPath}':");

            // Create holder GameObject
            var holderName = $"Toggle_{toggleName}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create MA Toggle Holder");
            holder.transform.SetParent(avatarRoot.transform, false);
            sb.AppendLine($"  [1/3] Created holder GameObject '{holderName}' under avatar root.");

            // Add ModularAvatarMenuItem (Toggle)
            MAComponentFactory.AddMenuItemToggle(holder, isDefault: defaultOn);
            sb.AppendLine($"  [2/3] Added ModularAvatarMenuItem (Toggle, default={defaultOn}, synced, saved).");

            // Add ModularAvatarObjectToggle
            MAComponentFactory.AddObjectToggle(holder, new List<(GameObject, bool)>
            {
                (target.gameObject, true)
            });
            sb.AppendLine($"  [3/3] Added ModularAvatarObjectToggle (target='{targetPath}', Active=true).");

            sb.AppendLine($"\nDone! '{target.name}' can now be toggled from Expression Menu (non-destructive, MA-powered).");
            return sb.ToString().TrimEnd();
        }

        // ========== 2. AddMenuItem ==========

        [AgentTool("Add a Modular Avatar Menu Item to a GameObject. Creates a non-destructive menu entry. type: Toggle, Button, SubMenu, RadialPuppet. If no paramName specified, uses the GameObject name. iconPath (optional) is a Texture2D asset path for the menu icon.")]
        public static string AddMenuItem(string goName, string type = "Toggle", string paramName = "", float value = 1f, bool synced = true, bool saved = true, bool isDefault = false, string iconPath = "")
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            if (MAComponentFactory.HasMenuItem(go))
                return $"Error: '{goName}' already has a ModularAvatarMenuItem. Remove it first if you want to reconfigure.";

            Texture2D icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (!string.IsNullOrEmpty(iconPath) && icon == null)
                return $"Error: Icon texture not found at '{iconPath}'.";

            var comp = MAComponentFactory.AddMenuItem(go, type,
                string.IsNullOrEmpty(paramName) ? null : paramName,
                value, synced, saved, isDefault, icon);

            if (comp == null)
                return $"Error: Invalid type '{type}'. Valid types: Toggle, Button, SubMenu, RadialPuppet.";

            var effectiveParam = string.IsNullOrEmpty(paramName) ? go.name : paramName;
            return $"Success: Added ModularAvatarMenuItem to '{goName}' (type={type}, param='{effectiveParam}', value={value}, default={isDefault}, synced={synced}, saved={saved}{(icon != null ? $", icon='{iconPath}'" : "")}).";
        }

        // ========== 3. AddMAParameters ==========

        [AgentTool("Add a Modular Avatar Parameters component. Defines expression parameters non-destructively. syncType: Bool, Int, Float, or NotSynced.")]
        public static string AddMAParameters(string goName, string paramName, string syncType = "Bool", float defaultValue = 0f, bool saved = true)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            if (string.IsNullOrEmpty(paramName))
                return "Error: paramName cannot be empty.";

            var maParams = MAComponentFactory.AddOrGetParameters(go);
            if (maParams == null)
                return "Error: Failed to create MAParameters component.";

            if (MAParameterBuilder.HasParameter(maParams, paramName))
                return $"Error: Parameter '{paramName}' already exists on '{goName}'.";

            if (!MAParameterBuilder.AddParam(maParams, paramName, syncType, defaultValue, saved))
                return $"Error: Invalid syncType '{syncType}'. Valid types: Bool, Int, Float, NotSynced.";

            return $"Success: Added MA parameter '{paramName}' (syncType={syncType}, default={defaultValue}, saved={saved}) to '{goName}'.";
        }

        // ========== MA Merge Animator ==========

        private static readonly string[] ValidMergeAnimatorLayers = { "FX", "Gesture", "Action", "Base", "Additive", "Sitting", "TPose", "IKPose" };

        [AgentTool("Add a Modular Avatar Merge Animator to merge an AnimatorController into the avatar non-destructively (no manual edit of the avatar's playable layers). controllerPath is the .controller asset path. layerType: FX (default), Gesture, Action, Base, Additive, Sitting, TPose, IKPose. pathMode: 0=Relative (MA default), 1=Absolute. matchWriteDefaults aligns Write Defaults with the avatar (recommended). deleteAttachedAnimator removes the temporary Animator after merge.")]
        public static string AddMAMergeAnimator(string goName, string controllerPath, string layerType = "FX", int pathMode = 0, bool matchWriteDefaults = true, bool deleteAttachedAnimator = true)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            string layerNorm = (layerType ?? "").ToLowerInvariant();
            if (!ValidMergeAnimatorLayers.Any(l => l.ToLowerInvariant() == layerNorm))
                return $"Error: invalid layerType '{layerType}'. Valid: {string.Join(", ", ValidMergeAnimatorLayers)}.";
            if (pathMode < 0 || pathMode > 1)
                return $"Error: invalid pathMode {pathMode}. Valid: 0=Relative, 1=Absolute.";

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller == null)
                return $"Error: AnimatorController not found at '{controllerPath}'.";

            var comp = MAComponentFactory.AddMergeAnimator(go, controller, pathMode, matchWriteDefaults, deleteAttachedAnimator, layerType);
            if (comp == null)
                return "Error: Failed to add ModularAvatarMergeAnimator.";

            return $"Success: Added ModularAvatarMergeAnimator to '{goName}' (controller='{controllerPath}', layer={layerType}, pathMode={(pathMode == 1 ? "Absolute" : "Relative")}, matchWriteDefaults={matchWriteDefaults}). The controller merges into the avatar's {layerType} layer at build time.";
        }

        // ========== MA Merge Armature ==========

        [AgentTool("Add a Modular Avatar Merge Armature to non-destructively merge an outfit's armature into the avatar's armature. goName is the outfit's Armature object; mergeTargetName is the avatar's Armature (root bone) to merge into. prefix/suffix resolve bone-name mismatches (e.g. outfit bones named 'Hips.1' -> suffix '.1'). Leave prefix/suffix empty when bone names match exactly.")]
        public static string AddMAMergeArmature(string goName, string mergeTargetName, string prefix = "", string suffix = "")
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' (outfit armature) not found.";

            var mergeTarget = FindGO(mergeTargetName);
            if (mergeTarget == null)
                return $"Error: mergeTarget '{mergeTargetName}' (avatar armature) not found.";

            var comp = MAComponentFactory.AddMergeArmature(go, mergeTarget.transform, prefix, suffix);
            if (comp == null)
                return "Error: Failed to add ModularAvatarMergeArmature.";

            return $"Success: Added ModularAvatarMergeArmature to '{goName}' -> merges into '{mergeTargetName}' (prefix='{prefix}', suffix='{suffix}'). Bones merge at build time.";
        }

        // ========== MA Bone Proxy ==========

        [AgentTool("Add a Modular Avatar Bone Proxy to non-destructively attach a GameObject (weapon/accessory/prop) to a specific avatar bone so it follows that bone. goName is the object to attach; targetBoneName is the avatar bone to follow (e.g. 'Head', 'RightHand'). mode: 1=AsChildAtRoot (recommended; snaps to the bone origin), 0=Unset/auto, 2=KeepWorldPose, 3=KeepRotation, 4=KeepPosition. To preserve the object's current world placement, set mode=2 (or run AlignAccessoryToBone first).")]
        public static string AddMABoneProxy(string goName, string targetBoneName, int mode = 1)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            if (mode < 0 || mode > 4)
                return $"Error: invalid mode {mode}. Valid: 0=Unset, 1=AsChildAtRoot, 2=KeepWorldPose, 3=KeepRotation, 4=KeepPosition.";

            var bone = FindGO(targetBoneName);
            if (bone == null)
                return $"Error: Target bone '{targetBoneName}' not found.";

            bool existed = MAComponentFactory.HasBoneProxy(go);
            var comp = MAComponentFactory.AddBoneProxy(go, bone.transform, mode);
            if (comp == null)
                return "Error: Failed to add ModularAvatarBoneProxy.";

            return $"Success: {(existed ? "Updated" : "Added")} ModularAvatarBoneProxy on '{goName}' -> follows bone '{targetBoneName}' (mode={mode}). The object becomes a runtime child of that bone at build time.";
        }

        // ========== MA Menu Container (nested submenu) ==========

        [AgentTool("Create a Modular Avatar menu container under the avatar (MenuInstaller + SubMenu MenuItem). Use this as the top of a nested menu, then add children with AddMAMenuItemUnder. iconPath (optional) sets the submenu icon. The container name (menuName) is used as parentMenuName for children.")]
        public static string CreateMAMenu(string avatarRootName, string menuName, string iconPath = "")
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            Texture2D icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (!string.IsNullOrEmpty(iconPath) && icon == null)
                return $"Error: Icon texture not found at '{iconPath}'.";

            var builder = MAMenuBuilder.Create(avatarRoot, menuName, icon);
            if (builder == null)
                return "Error: Failed to create MA menu (MA not installed?).";

            return $"Success: Created MA menu container '{menuName}' under '{avatarRootName}' (MenuInstaller + SubMenu). Add entries with AddMAMenuItemUnder('{menuName}', displayName, type, ...).";
        }

        [AgentTool("Add a child menu entry under an existing MA menu container (created by CreateMAMenu, or any SubMenu entry). Creates a child GameObject named displayName under parentMenuName with a MenuItem. type: Toggle, Button, RadialPuppet, SubMenu. For type=SubMenu the new entry can itself be used as parentMenuName to nest deeper. iconPath (optional) sets the entry icon.")]
        public static string AddMAMenuItemUnder(string parentMenuName, string displayName, string type = "Toggle", string paramName = "", float value = 1f, string iconPath = "")
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var parent = FindGO(parentMenuName);
            if (parent == null)
                return $"Error: Parent menu '{parentMenuName}' not found.";

            var parentInfo = MAComponentFactory.GetMenuItemInfo(parent);
            if (parentInfo == null || parentInfo.Value.type != "SubMenu")
                return $"Error: '{parentMenuName}' is not an MA menu container (needs a SubMenu MenuItem). Create one with CreateMAMenu, or pass a SubMenu entry created by AddMAMenuItemUnder.";

            Texture2D icon = string.IsNullOrEmpty(iconPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (!string.IsNullOrEmpty(iconPath) && icon == null)
                return $"Error: Icon texture not found at '{iconPath}'.";

            var child = new GameObject(displayName);
            Undo.RegisterCreatedObjectUndo(child, "Create MA Menu Item");
            child.transform.SetParent(parent.transform, false);

            string effectiveParam = string.IsNullOrEmpty(paramName) ? displayName : paramName;
            Component comp;
            switch (type.ToLowerInvariant())
            {
                case "toggle":
                    comp = MAComponentFactory.AddMenuItemToggle(child, effectiveParam, value, true, true, false, icon);
                    break;
                case "button":
                    comp = MAComponentFactory.AddMenuItemButton(child, effectiveParam, value, icon);
                    break;
                case "radialpuppet":
                case "radial":
                    if (string.IsNullOrEmpty(paramName))
                    { Undo.DestroyObjectImmediate(child); return "Error: RadialPuppet requires paramName."; }
                    comp = MAComponentFactory.AddMenuItemRadial(child, paramName, icon);
                    break;
                case "submenu":
                    comp = MAComponentFactory.AddMenuItemSubMenu(child, icon);
                    break;
                default:
                    Undo.DestroyObjectImmediate(child);
                    return $"Error: Invalid type '{type}'. Valid: Toggle, Button, RadialPuppet, SubMenu.";
            }

            if (comp == null)
            { Undo.DestroyObjectImmediate(child); return "Error: Failed to add MenuItem (MA not installed?)."; }

            string nestNote = type.ToLowerInvariant() == "submenu" ? $" Use '{displayName}' as parentMenuName to nest further." : "";
            return $"Success: Added '{displayName}' ({type}) under '{parentMenuName}'.{nestNote}";
        }

        // ========== 4. AddBlendshapeSync ==========

        [AgentTool("Add MA Blendshape Sync to sync blendshapes from a source mesh to a target mesh. Useful for syncing body shape keys (e.g. breast size) to outfit meshes. targetGoName must have a SkinnedMeshRenderer. If blendshapeName is empty, auto-detects all common blendshapes.")]
        public static string AddBlendshapeSync(string targetGoName, string sourceMeshPath, string blendshapeName = "")
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var targetGo = FindGO(targetGoName);
            if (targetGo == null)
                return $"Error: Target GameObject '{targetGoName}' not found.";

            var targetSmr = targetGo.GetComponent<SkinnedMeshRenderer>();
            if (targetSmr == null)
                return $"Error: '{targetGoName}' does not have a SkinnedMeshRenderer. ModularAvatarBlendshapeSync requires one.";

            // Walk up to find avatar root
            Transform avatarRoot = targetGo.transform;
            while (avatarRoot.parent != null)
            {
                avatarRoot = avatarRoot.parent;
                var descriptorType = VRChatTools.FindVrcType(VRChatTools.VrcDescriptorTypeName);
                if (descriptorType != null && avatarRoot.GetComponent(descriptorType) != null)
                    break;
            }

            var sourceTransform = avatarRoot.Find(sourceMeshPath);
            if (sourceTransform == null)
                return $"Error: Source mesh '{sourceMeshPath}' not found under avatar root '{avatarRoot.name}'.";

            var sourceSmr = sourceTransform.GetComponent<SkinnedMeshRenderer>();
            if (sourceSmr == null)
                return $"Error: Source '{sourceMeshPath}' does not have a SkinnedMeshRenderer.";

            var sb = new StringBuilder();

            // Determine blendshapes to sync
            var blendshapesToSync = new List<string>();
            if (string.IsNullOrEmpty(blendshapeName))
            {
                var sourceNames = new HashSet<string>();
                for (int i = 0; i < sourceSmr.sharedMesh.blendShapeCount; i++)
                    sourceNames.Add(sourceSmr.sharedMesh.GetBlendShapeName(i));

                for (int i = 0; i < targetSmr.sharedMesh.blendShapeCount; i++)
                {
                    var name = targetSmr.sharedMesh.GetBlendShapeName(i);
                    if (sourceNames.Contains(name))
                        blendshapesToSync.Add(name);
                }

                if (blendshapesToSync.Count == 0)
                    return $"Error: No common blendshapes found between '{sourceMeshPath}' and '{targetGoName}'.";

                sb.AppendLine($"Auto-detected {blendshapesToSync.Count} common blendshapes.");
            }
            else
            {
                bool foundOnSource = false, foundOnTarget = false;
                for (int i = 0; i < sourceSmr.sharedMesh.blendShapeCount; i++)
                    if (sourceSmr.sharedMesh.GetBlendShapeName(i) == blendshapeName) { foundOnSource = true; break; }
                for (int i = 0; i < targetSmr.sharedMesh.blendShapeCount; i++)
                    if (targetSmr.sharedMesh.GetBlendShapeName(i) == blendshapeName) { foundOnTarget = true; break; }

                if (!foundOnSource)
                    return $"Error: Blendshape '{blendshapeName}' not found on source mesh '{sourceMeshPath}'.";
                if (!foundOnTarget)
                    return $"Error: Blendshape '{blendshapeName}' not found on target mesh '{targetGoName}'.";

                blendshapesToSync.Add(blendshapeName);
            }

            // Get or add component via SDK
            var sync = MAComponentFactory.AddOrGetBlendshapeSync(targetGo);

            // Check existing bindings to avoid duplicates
            var existingNames = MAComponentFactory.GetExistingBindingNames(sync);

            int addedCount = 0;
            int skippedCount = 0;
            foreach (var bsName in blendshapesToSync)
            {
                if (existingNames.Contains(bsName))
                {
                    skippedCount++;
                    continue;
                }

                MAComponentFactory.AddBlendshapeBinding(sync, sourceMeshPath, bsName);
                addedCount++;
            }

            sb.AppendLine($"Success: Added ModularAvatarBlendshapeSync to '{targetGoName}'.");
            sb.AppendLine($"  Source: '{sourceMeshPath}'");
            sb.AppendLine($"  Added: {addedCount} binding(s)");
            if (skippedCount > 0)
                sb.AppendLine($"  Skipped: {skippedCount} duplicate(s)");
            if (blendshapesToSync.Count <= 10)
            {
                sb.AppendLine("  Blendshapes:");
                foreach (var name in blendshapesToSync)
                    sb.AppendLine($"    - {name}");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 5. SetVisibleHeadAccessory ==========

        [AgentTool("Add MA Visible Head Accessory to make a head accessory visible in first-person view.")]
        public static string SetVisibleHeadAccessory(string goName)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            if (MAComponentFactory.HasVisibleHeadAccessory(go))
                return $"Info: '{goName}' already has ModularAvatarVisibleHeadAccessory.";

            MAComponentFactory.AddVisibleHeadAccessory(go);
            return $"Success: Added ModularAvatarVisibleHeadAccessory to '{goName}'. This object will now be visible in first-person view.";
        }

        // ========== 6. InspectMAComponents ==========

        [AgentTool("List all Modular Avatar components on an avatar and its children. Shows component types, targets, and configuration.")]
        public static string InspectMAComponents(string avatarRootName)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var allComponents = MAComponentFactory.GetAllMAComponents(avatarRoot);
            if (allComponents.Length == 0)
                return $"No Modular Avatar components found on '{avatarRootName}' or its children.";

            var sb = new StringBuilder();
            sb.AppendLine($"Modular Avatar components on '{avatarRootName}' ({allComponents.Length} total):");
            sb.AppendLine();

            var grouped = allComponents.GroupBy(c => c.GetType().Name).OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine($"=== {group.Key} ({group.Count()}) ===");

                foreach (var component in group)
                {
                    var goPath = GetRelativePath(avatarRoot.transform, component.transform);
                    sb.Append($"  [{goPath}] ");
                    sb.AppendLine(MAComponentFactory.DescribeComponent(component, avatarRoot.transform));
                }
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 7. RemoveMAComponent ==========

        [AgentTool("Remove a specific Modular Avatar component from a GameObject. componentType: MenuItem, ObjectToggle, MergeArmature, BlendshapeSync, BoneProxy, Parameters, VisibleHeadAccessory, MeshSettings.")]
        public static string RemoveMAComponent(string goName, string componentType)
        {
            var err = MAAvailability.CheckOrError();
            if (err != null) return err;

            var go = FindGO(goName);
            if (go == null)
                return $"Error: GameObject '{goName}' not found.";

            if (!AgentSettings.RequestConfirmation(
                "MA コンポーネントの削除",
                $"'{goName}' から {componentType} コンポーネントを削除します。"))
                return "Cancelled: User denied the operation.";

            if (!MAComponentFactory.RemoveComponent(go, componentType))
            {
                if (componentType.ToLowerInvariant() != "menuitem" &&
                    componentType.ToLowerInvariant() != "objecttoggle" &&
                    componentType.ToLowerInvariant() != "mergearmature" &&
                    componentType.ToLowerInvariant() != "blendshapesync" &&
                    componentType.ToLowerInvariant() != "boneproxy" &&
                    componentType.ToLowerInvariant() != "parameters" &&
                    componentType.ToLowerInvariant() != "visibleheadaccessory" &&
                    componentType.ToLowerInvariant() != "meshsettings")
                    return $"Error: Unknown component type '{componentType}'. Valid types: MenuItem, ObjectToggle, MergeArmature, BlendshapeSync, BoneProxy, Parameters, VisibleHeadAccessory, MeshSettings.";

                return $"Error: No {componentType} component found on '{goName}'.";
            }

            return $"Success: Removed {componentType} from '{goName}'.";
        }
    }
}
