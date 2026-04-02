#if FACE_EMO
using Suzuryg.FaceEmo.Domain;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Components.Data;
using Suzuryg.FaceEmo.Components.Settings;
using FaceEmoMenu = Suzuryg.FaceEmo.Domain.Menu;
using FaceEmoAnimation = Suzuryg.FaceEmo.Domain.Animation;
#endif
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
#if FACE_EMO
    /// <summary>
    /// FaceEmo advanced tools using the public domain model API for type-safe expression management.
    /// Reads and writes directly to scene data (MenuRepositoryComponent.SerializableMenu)
    /// instead of the backup FaceEmoProject asset.
    /// Requires the FaceEmo package (jp.suzuryg.face-emo) to be installed.
    /// </summary>
    public static class FaceEmoAdvancedTools
    {
        // ═══════════════════════════════════════════
        //  A. Expression Management (6 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Add a new expression mode to FaceEmo menu. destination: 'Registered' (default), group name, or 'Unregistered'. animationClipPath: optional asset path to AnimationClip.")]
        public static string AddExpression(string displayName, string destination = "Registered",
            string animationClipPath = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            string dest = FaceEmoAPI.ResolveDestination(menu, destination);

            if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
                return $"Error: Cannot add item to '{destination}'. The destination may be full (Registered max=7) or not found.";

            string modeId = FaceEmoAPI.AddMode(menu, dest);
            FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: displayName);

            // Set animation if provided
            if (!string.IsNullOrEmpty(animationClipPath))
            {
                string guid = AssetDatabase.AssetPathToGUID(animationClipPath);
                if (string.IsNullOrEmpty(guid))
                    return $"Error: Animation clip not found at '{animationClipPath}'.";
                FaceEmoAPI.SetModeAnimation(menu, new FaceEmoAnimation(guid), modeId);
            }

            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Added expression '{displayName}' to {destination} (id={modeId}).{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Remove an expression mode from FaceEmo by display name. Requires confirmation.")]
        public static string RemoveExpression(string displayName, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, displayName);
            if (modeId == null) return $"Error: Expression '{displayName}' not found.";

            if (!AgentSettings.RequestConfirmation(
                "表情の削除",
                $"表情「{displayName}」(id={modeId}) を削除します。"))
                return "Cancelled: User denied the removal.";

            FaceEmoAPI.RemoveMenuItem(menu, modeId);
            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Removed expression '{displayName}'.{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Set animation clip for a FaceEmo expression. slot: 'Mode' (default), 'Base', 'Left', 'Right', 'Both'. branchIndex required for branch slots (Base/Left/Right/Both).")]
        public static string SetExpressionAnimation(string expressionName, string animationClipPath,
            string slot = "Mode", int branchIndex = -1, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            string guid = AssetDatabase.AssetPathToGUID(animationClipPath);
            if (string.IsNullOrEmpty(guid))
                return $"Error: Animation clip not found at '{animationClipPath}'.";

            var anim = new FaceEmoAnimation(guid);

            if (slot.Equals("Mode", StringComparison.OrdinalIgnoreCase))
            {
                FaceEmoAPI.SetModeAnimation(menu, anim, modeId);
            }
            else
            {
                if (branchIndex < 0)
                    return "Error: branchIndex is required for branch animation slots (Base/Left/Right/Both).";

                var branchSlot = FaceEmoAPI.ParseBranchSlot(slot);
                if (branchSlot == null)
                    return $"Error: Unknown slot '{slot}'. Use: Mode, Base, Left, Right, Both.";

                FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIndex, branchSlot.Value, anim);
            }

            FaceEmoAPI.SaveMenu(launcher, menu);
            string clipName = System.IO.Path.GetFileNameWithoutExtension(animationClipPath);
            return $"Success: Set animation '{clipName}' on '{expressionName}' slot={slot}" +
                   (branchIndex >= 0 ? $" branch[{branchIndex}]" : "") + $".{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Modify FaceEmo expression properties. eyeTracking/mouthTracking: 'Tracking' or 'Animation'. blinkEnabled/mouthMorphCancelerEnabled: 'true' or 'false'. Only specified parameters are changed.")]
        public static string ModifyExpressionProperties(string expressionName,
            string newDisplayName = "", string eyeTracking = "", string mouthTracking = "",
            string blinkEnabled = "", string mouthMorphCancelerEnabled = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            EyeTrackingControl? eye = null;
            if (!string.IsNullOrEmpty(eyeTracking))
                eye = eyeTracking.Equals("Animation", StringComparison.OrdinalIgnoreCase)
                    ? EyeTrackingControl.Animation : EyeTrackingControl.Tracking;

            MouthTrackingControl? mouth = null;
            if (!string.IsNullOrEmpty(mouthTracking))
                mouth = mouthTracking.Equals("Animation", StringComparison.OrdinalIgnoreCase)
                    ? MouthTrackingControl.Animation : MouthTrackingControl.Tracking;

            bool? blink = string.IsNullOrEmpty(blinkEnabled) ? (bool?)null : bool.Parse(blinkEnabled);
            bool? mouthCancel = string.IsNullOrEmpty(mouthMorphCancelerEnabled) ? (bool?)null : bool.Parse(mouthMorphCancelerEnabled);

            FaceEmoAPI.ModifyModeProperties(menu, modeId,
                displayName: string.IsNullOrEmpty(newDisplayName) ? null : newDisplayName,
                eyeTrackingControl: eye,
                mouthTrackingControl: mouth,
                blinkEnabled: blink,
                mouthMorphCancelerEnabled: mouthCancel);

            FaceEmoAPI.SaveMenu(launcher, menu);
            var changes = new List<string>();
            if (!string.IsNullOrEmpty(newDisplayName)) changes.Add($"name='{newDisplayName}'");
            if (eye.HasValue) changes.Add($"eyeTracking={eye}");
            if (mouth.HasValue) changes.Add($"mouthTracking={mouth}");
            if (blink.HasValue) changes.Add($"blink={blink}");
            if (mouthCancel.HasValue) changes.Add($"mouthMorphCanceler={mouthCancel}");
            return $"Success: Modified '{expressionName}': {string.Join(", ", changes)}.{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Set the default expression in FaceEmo.")]
        public static string SetDefaultExpression(string expressionName, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            if (!FaceEmoAPI.CanSetDefaultSelectionTo(menu, modeId))
                return $"Error: Cannot set '{expressionName}' as default selection.";

            FaceEmoAPI.SetDefaultSelection(menu, modeId);
            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Set '{expressionName}' as default expression.{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Inspect a single FaceEmo expression in detail. Shows animation, branches, conditions, and tracking settings.")]
        public static string InspectExpressionDetail(string expressionName, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Expression: \"{mode.DisplayName}\" (id={modeId})");
            sb.AppendLine($"  ChangeDefaultFace: {mode.ChangeDefaultFace}");
            sb.AppendLine($"  EyeTracking: {mode.EyeTrackingControl}");
            sb.AppendLine($"  MouthTracking: {mode.MouthTrackingControl}");
            sb.AppendLine($"  BlinkEnabled: {mode.BlinkEnabled}");
            sb.AppendLine($"  MouthMorphCanceler: {mode.MouthMorphCancelerEnabled}");

            // Mode animation
            string modeAnim = mode.Animation != null ? FaceEmoAPI.GuidToAnimName(mode.Animation.GUID) : "None";
            sb.AppendLine($"  Animation: {modeAnim}");

            // Branches
            if (mode.Branches != null && mode.Branches.Count > 0)
            {
                sb.AppendLine($"  Branches ({mode.Branches.Count}):");
                for (int b = 0; b < mode.Branches.Count; b++)
                {
                    var branch = mode.Branches[b];
                    sb.AppendLine($"    Branch[{b}]:");

                    // Conditions
                    if (branch.Conditions != null && branch.Conditions.Count > 0)
                    {
                        var condParts = new List<string>();
                        foreach (var cond in branch.Conditions)
                            condParts.Add($"{cond.Hand}{(cond.ComparisonOperator == ComparisonOperator.NotEqual ? "!=" : "=")}{cond.HandGesture}");
                        sb.AppendLine($"      Conditions: {string.Join(", ", condParts)}");
                    }

                    sb.AppendLine($"      EyeTracking: {branch.EyeTrackingControl}");
                    sb.AppendLine($"      MouthTracking: {branch.MouthTrackingControl}");
                    sb.AppendLine($"      Blink: {branch.BlinkEnabled}, MouthMorphCanceler: {branch.MouthMorphCancelerEnabled}");
                    sb.AppendLine($"      LeftTrigger: {branch.IsLeftTriggerUsed}, RightTrigger: {branch.IsRightTriggerUsed}");

                    string baseAnim = branch.BaseAnimation != null ? FaceEmoAPI.GuidToAnimName(branch.BaseAnimation.GUID) : "None";
                    string leftAnim = branch.LeftHandAnimation != null ? FaceEmoAPI.GuidToAnimName(branch.LeftHandAnimation.GUID) : "None";
                    string rightAnim = branch.RightHandAnimation != null ? FaceEmoAPI.GuidToAnimName(branch.RightHandAnimation.GUID) : "None";
                    string bothAnim = branch.BothHandsAnimation != null ? FaceEmoAPI.GuidToAnimName(branch.BothHandsAnimation.GUID) : "None";

                    sb.AppendLine($"      Base: {baseAnim}");
                    if (leftAnim != "None") sb.AppendLine($"      Left: {leftAnim}");
                    if (rightAnim != "None") sb.AppendLine($"      Right: {rightAnim}");
                    if (bothAnim != "None") sb.AppendLine($"      Both: {bothAnim}");
                }
            }
            else
            {
                sb.AppendLine("  Branches: (none)");
            }

            return sb.ToString().TrimEnd();
        }

        // ═══════════════════════════════════════════
        //  B. Branch / Gesture Management (4 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Add a gesture branch to a FaceEmo expression. conditions format: 'Left=Fist;Right=Victory' or 'Either!=Neutral'. hand: Left/Right/Either/Both/OneSide. gesture: Neutral/Fist/HandOpen/Fingerpoint/Victory/RockNRoll/HandGun/ThumbsUp. Operator: = (Equals) or != (NotEqual).")]
        public static string AddGestureBranch(string expressionName, string conditions,
            string baseAnimationPath = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            // Parse conditions
            List<Condition> condList;
            try
            {
                condList = FaceEmoAPI.ParseConditions(conditions);
            }
            catch (ArgumentException ex)
            {
                return $"Error: {ex.Message}";
            }

            if (condList.Count == 0)
                return "Error: At least one condition is required.";

            FaceEmoAPI.AddBranch(menu, modeId, condList);

            // Set base animation if provided
            if (!string.IsNullOrEmpty(baseAnimationPath))
            {
                string guid = AssetDatabase.AssetPathToGUID(baseAnimationPath);
                if (string.IsNullOrEmpty(guid))
                    return $"Error: Animation clip not found at '{baseAnimationPath}'.";

                int branchIdx = mode.Branches.Count - 1; // newly added branch is at end
                FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIdx, BranchAnimationType.Base, new FaceEmoAnimation(guid));
            }

            FaceEmoAPI.SaveMenu(launcher, menu);
            var condStr = string.Join(", ", condList.Select(c =>
                $"{c.Hand}{(c.ComparisonOperator == ComparisonOperator.NotEqual ? "!=" : "=")}{c.HandGesture}"));
            return $"Success: Added branch to '{expressionName}' with conditions: {condStr}.{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Remove a gesture branch from a FaceEmo expression by index. Requires confirmation.")]
        public static string RemoveGestureBranch(string expressionName, int branchIndex, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            if (!FaceEmoAPI.CanRemoveBranch(menu, modeId, branchIndex))
                return $"Error: Cannot remove branch[{branchIndex}] from '{expressionName}'. Index may be out of range.";

            if (!AgentSettings.RequestConfirmation(
                "ブランチの削除",
                $"表情「{expressionName}」のブランチ[{branchIndex}]を削除します。"))
                return "Cancelled: User denied the removal.";

            FaceEmoAPI.RemoveBranch(menu, modeId, branchIndex);
            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Removed branch[{branchIndex}] from '{expressionName}'.{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Add a gesture condition to an existing branch. hand: Left/Right/Either/Both/OneSide. gesture: Neutral/Fist/HandOpen/Fingerpoint/Victory/RockNRoll/HandGun/ThumbsUp. comparisonOperator: 'Equals' or 'NotEqual'.")]
        public static string AddGestureCondition(string expressionName, int branchIndex,
            string hand, string gesture, string comparisonOperator = "Equals", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            if (!FaceEmoAPI.CanAddCondition(menu, modeId, branchIndex))
                return $"Error: Cannot add condition to branch[{branchIndex}] of '{expressionName}'.";

            try
            {
                Hand h = FaceEmoAPI.ParseHand(hand);
                HandGesture g = FaceEmoAPI.ParseGesture(gesture);
                ComparisonOperator op = comparisonOperator.Equals("NotEqual", StringComparison.OrdinalIgnoreCase)
                    ? ComparisonOperator.NotEqual : ComparisonOperator.Equals;

                FaceEmoAPI.AddCondition(menu, modeId, branchIndex, h, g, op);
            }
            catch (ArgumentException ex)
            {
                return $"Error: {ex.Message}";
            }

            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Added condition {hand}{(comparisonOperator == "NotEqual" ? "!=" : "=")}{gesture} to '{expressionName}' branch[{branchIndex}].{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Modify tracking and trigger properties of a gesture branch. eyeTracking/mouthTracking: 'Tracking' or 'Animation'. Other params: 'true' or 'false'.")]
        public static string ModifyBranchProperties(string expressionName, int branchIndex,
            string eyeTracking = "", string mouthTracking = "", string blinkEnabled = "",
            string mouthMorphCancelerEnabled = "", string isLeftTriggerUsed = "",
            string isRightTriggerUsed = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            if (!FaceEmoAPI.CanModifyBranchProperties(menu, modeId, branchIndex))
                return $"Error: Cannot modify branch[{branchIndex}] of '{expressionName}'.";

            EyeTrackingControl? eye = null;
            if (!string.IsNullOrEmpty(eyeTracking))
                eye = eyeTracking.Equals("Animation", StringComparison.OrdinalIgnoreCase)
                    ? EyeTrackingControl.Animation : EyeTrackingControl.Tracking;

            MouthTrackingControl? mouth = null;
            if (!string.IsNullOrEmpty(mouthTracking))
                mouth = mouthTracking.Equals("Animation", StringComparison.OrdinalIgnoreCase)
                    ? MouthTrackingControl.Animation : MouthTrackingControl.Tracking;

            bool? blink = string.IsNullOrEmpty(blinkEnabled) ? (bool?)null : bool.Parse(blinkEnabled);
            bool? mouthCancel = string.IsNullOrEmpty(mouthMorphCancelerEnabled) ? (bool?)null : bool.Parse(mouthMorphCancelerEnabled);
            bool? leftTrigger = string.IsNullOrEmpty(isLeftTriggerUsed) ? (bool?)null : bool.Parse(isLeftTriggerUsed);
            bool? rightTrigger = string.IsNullOrEmpty(isRightTriggerUsed) ? (bool?)null : bool.Parse(isRightTriggerUsed);

            FaceEmoAPI.ModifyBranchProperties(menu, modeId, branchIndex,
                eyeTrackingControl: eye,
                mouthTrackingControl: mouth,
                blinkEnabled: blink,
                mouthMorphCancelerEnabled: mouthCancel,
                isLeftTriggerUsed: leftTrigger,
                isRightTriggerUsed: rightTrigger);

            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Modified branch[{branchIndex}] properties on '{expressionName}'.{FaceEmoAPI.WindowWarning()}";
        }

        // ═══════════════════════════════════════════
        //  C. Menu Structure (2 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Create a submenu group in FaceEmo. destination: 'Registered' (default) or 'Unregistered'.")]
        public static string CreateExpressionGroup(string displayName,
            string destination = "Registered", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            string dest = FaceEmoAPI.ResolveDestination(menu, destination);

            if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
                return $"Error: Cannot add group to '{destination}'. Destination may be full or not found.";

            string groupId = FaceEmoAPI.AddGroup(menu, dest, displayName);

            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Created group '{displayName}' in {destination} (id={groupId}).{FaceEmoAPI.WindowWarning()}";
        }

        [AgentTool("Move a mode or group to a different location in FaceEmo menu. destination: 'Registered', 'Unregistered', or group name. index: optional insertion position (-1 for end).")]
        public static string MoveExpressionItem(string itemName, string destination,
            int index = -1, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            // Find item by name (could be mode or group)
            string itemId = null;
            var (modeId, _) = FaceEmoAPI.FindExpression(menu, itemName);
            if (modeId != null)
            {
                itemId = modeId;
            }
            else
            {
                // Try as group
                var groupId = FaceEmoAPI.FindGroupByName(menu, itemName);
                if (groupId != null)
                    itemId = groupId;
            }

            if (itemId == null) return $"Error: Item '{itemName}' not found.";

            string dest = FaceEmoAPI.ResolveDestination(menu, destination);

            if (!FaceEmoAPI.CanMoveMenuItemTo(menu, new List<string> { itemId }, dest))
                return $"Error: Cannot move '{itemName}' to '{destination}'.";

            FaceEmoAPI.MoveMenuItem(menu, new List<string> { itemId }, dest);
            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Moved '{itemName}' to {destination}" +
                   (index >= 0 ? $" at index {index}" : "") + $".{FaceEmoAPI.WindowWarning()}";
        }

        // ═══════════════════════════════════════════
        //  D. AV3 Settings (4 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Configure FaceEmo generation settings. All params optional — only specified values are changed. Shows current values when called with no parameters.")]
        public static string ConfigureFaceEmoGeneration(string smoothAnalogFist = "",
            string transitionDuration = "", string replaceBlink = "", string matchWriteDefaults = "",
            string generateThumbnails = "", string disableTrackingControls = "",
            string addParameterPrefix = "", string disableFxDuringDancing = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var av3SO = FaceEmoAPI.GetAV3SettingSO(launcher);
            if (av3SO == null) return "Error: AV3Setting not found on launcher.";

            // If no params specified, show current values
            bool anySet = !string.IsNullOrEmpty(smoothAnalogFist) || !string.IsNullOrEmpty(transitionDuration)
                || !string.IsNullOrEmpty(replaceBlink) || !string.IsNullOrEmpty(matchWriteDefaults)
                || !string.IsNullOrEmpty(generateThumbnails) || !string.IsNullOrEmpty(disableTrackingControls)
                || !string.IsNullOrEmpty(addParameterPrefix) || !string.IsNullOrEmpty(disableFxDuringDancing);

            if (!anySet)
            {
                var sb = new StringBuilder();
                sb.AppendLine("FaceEmo Generation Settings:");
                sb.AppendLine($"  SmoothAnalogFist: {av3SO.FindProperty("SmoothAnalogFist")?.boolValue}");
                sb.AppendLine($"  TransitionDuration: {av3SO.FindProperty("TransitionDurationSeconds")?.doubleValue}");
                sb.AppendLine($"  ReplaceBlink: {av3SO.FindProperty("ReplaceBlink")?.boolValue}");
                sb.AppendLine($"  MatchAvatarWriteDefaults: {av3SO.FindProperty("MatchAvatarWriteDefaults")?.boolValue}");
                sb.AppendLine($"  GenerateExMenuThumbnails: {av3SO.FindProperty("GenerateExMenuThumbnails")?.boolValue}");
                sb.AppendLine($"  DisableTrackingControls: {av3SO.FindProperty("DisableTrackingControls")?.boolValue}");
                sb.AppendLine($"  AddParameterPrefix: {av3SO.FindProperty("AddParameterPrefix")?.boolValue}");
                sb.AppendLine($"  DisableFxDuringDancing: {av3SO.FindProperty("DisableFxDuringDancing")?.boolValue}");
                return sb.ToString().TrimEnd();
            }

            var av3 = FaceEmoAPI.GetAV3Setting(launcher);
            Undo.RecordObject(av3, "Configure FaceEmo Generation");
            var changes = new List<string>();

            void SetBool(string propName, string value, string label)
            {
                if (string.IsNullOrEmpty(value)) return;
                var prop = av3SO.FindProperty(propName);
                if (prop == null) return;
                prop.boolValue = bool.Parse(value);
                changes.Add($"{label}={value}");
            }

            SetBool("SmoothAnalogFist", smoothAnalogFist, "smoothAnalogFist");
            SetBool("ReplaceBlink", replaceBlink, "replaceBlink");
            SetBool("MatchAvatarWriteDefaults", matchWriteDefaults, "matchWriteDefaults");
            SetBool("GenerateExMenuThumbnails", generateThumbnails, "generateThumbnails");
            SetBool("DisableTrackingControls", disableTrackingControls, "disableTrackingControls");
            SetBool("AddParameterPrefix", addParameterPrefix, "addParameterPrefix");
            SetBool("DisableFxDuringDancing", disableFxDuringDancing, "disableFxDuringDancing");

            if (!string.IsNullOrEmpty(transitionDuration))
            {
                var prop = av3SO.FindProperty("TransitionDurationSeconds");
                if (prop != null)
                {
                    prop.doubleValue = double.Parse(transitionDuration);
                    changes.Add($"transitionDuration={transitionDuration}");
                }
            }

            av3SO.ApplyModifiedProperties();
            EditorUtility.SetDirty(av3);
            FaceEmoAPI.RefreshWindowIfOpen(launcher);
            return $"Success: Updated generation settings: {string.Join(", ", changes)}.";
        }

        [AgentTool("Configure mouth morph blend shapes. action: 'list' (default), 'add', 'remove'. For add/remove, specify meshPath (transform path from avatar root) and blendShapeName.")]
        public static string ConfigureMouthMorphs(string action = "list",
            string meshPath = "", string blendShapeName = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var av3 = FaceEmoAPI.GetAV3Setting(launcher);
            if (av3 == null) return "Error: AV3Setting not found.";

            switch (action.ToLower())
            {
                case "list":
                {
                    if (av3.MouthMorphs == null || av3.MouthMorphs.Count == 0)
                        return "No mouth morphs configured.";

                    var sb = new StringBuilder();
                    sb.AppendLine($"Mouth Morphs ({av3.MouthMorphs.Count}):");
                    for (int i = 0; i < av3.MouthMorphs.Count; i++)
                    {
                        var bs = av3.MouthMorphs[i];
                        sb.AppendLine($"  [{i}] {bs.Path}.{bs.Name}");
                    }
                    return sb.ToString().TrimEnd();
                }

                case "add":
                {
                    if (string.IsNullOrEmpty(meshPath) || string.IsNullOrEmpty(blendShapeName))
                        return "Error: meshPath and blendShapeName are required for 'add'.";

                    var newBs = new BlendShape(meshPath, blendShapeName);

                    // Check for duplicates
                    if (av3.MouthMorphs.Any(b => b.Path == meshPath && b.Name == blendShapeName))
                        return $"Error: Mouth morph '{meshPath}.{blendShapeName}' already exists.";

                    Undo.RecordObject(av3, "Add Mouth Morph");
                    av3.MouthMorphs.Add(newBs);
                    EditorUtility.SetDirty(av3);
                    FaceEmoAPI.RefreshWindowIfOpen(launcher);
                    return $"Success: Added mouth morph '{meshPath}.{blendShapeName}'.";
                }

                case "remove":
                {
                    if (string.IsNullOrEmpty(blendShapeName))
                        return "Error: blendShapeName is required for 'remove'.";

                    Undo.RecordObject(av3, "Remove Mouth Morph");
                    int removed = av3.MouthMorphs.RemoveAll(b =>
                        b.Name == blendShapeName &&
                        (string.IsNullOrEmpty(meshPath) || b.Path == meshPath));

                    if (removed == 0)
                        return $"Error: Mouth morph '{blendShapeName}' not found.";

                    EditorUtility.SetDirty(av3);
                    FaceEmoAPI.RefreshWindowIfOpen(launcher);
                    return $"Success: Removed {removed} mouth morph(s) matching '{blendShapeName}'.";
                }

                default:
                    return $"Error: Unknown action '{action}'. Use 'list', 'add', or 'remove'.";
            }
        }

        [AgentTool("Configure AFK face settings. enableAfk: 'true'/'false'. Clip paths are asset paths to AnimationClips.")]
        public static string ConfigureAfkFace(string enableAfk = "", string afkEnterFacePath = "",
            string afkFacePath = "", string afkExitFacePath = "", string exitDuration = "",
            string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var av3SO = FaceEmoAPI.GetAV3SettingSO(launcher);
            if (av3SO == null) return "Error: AV3Setting not found.";

            bool anySet = !string.IsNullOrEmpty(enableAfk) || !string.IsNullOrEmpty(afkEnterFacePath)
                || !string.IsNullOrEmpty(afkFacePath) || !string.IsNullOrEmpty(afkExitFacePath)
                || !string.IsNullOrEmpty(exitDuration);

            if (!anySet)
            {
                var sb = new StringBuilder();
                sb.AppendLine("AFK Face Settings:");
                sb.AppendLine($"  ChangeAfkFace: {av3SO.FindProperty("ChangeAfkFace")?.boolValue}");

                var enterClip = av3SO.FindProperty("AfkEnterFace")?.objectReferenceValue;
                var faceClip = av3SO.FindProperty("AfkFace")?.objectReferenceValue;
                var exitClip = av3SO.FindProperty("AfkExitFace")?.objectReferenceValue;
                sb.AppendLine($"  AfkEnterFace: {(enterClip != null ? enterClip.name : "None")}");
                sb.AppendLine($"  AfkFace: {(faceClip != null ? faceClip.name : "None")}");
                sb.AppendLine($"  AfkExitFace: {(exitClip != null ? exitClip.name : "None")}");
                sb.AppendLine($"  AfkExitDuration: {av3SO.FindProperty("AfkExitDurationSeconds")?.floatValue}");
                return sb.ToString().TrimEnd();
            }

            Undo.RecordObject(FaceEmoAPI.GetAV3Setting(launcher), "Configure AFK Face");
            var changes = new List<string>();

            if (!string.IsNullOrEmpty(enableAfk))
            {
                var prop = av3SO.FindProperty("ChangeAfkFace");
                if (prop != null) { prop.boolValue = bool.Parse(enableAfk); changes.Add($"enableAfk={enableAfk}"); }
            }

            void SetClip(string propName, string path, string label)
            {
                if (string.IsNullOrEmpty(path)) return;
                var prop = av3SO.FindProperty(propName);
                if (prop == null) return;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { changes.Add($"{label}=Error(not found)"); return; }
                prop.objectReferenceValue = clip;
                changes.Add($"{label}={clip.name}");
            }

            SetClip("AfkEnterFace", afkEnterFacePath, "enterFace");
            SetClip("AfkFace", afkFacePath, "afkFace");
            SetClip("AfkExitFace", afkExitFacePath, "exitFace");

            if (!string.IsNullOrEmpty(exitDuration))
            {
                var prop = av3SO.FindProperty("AfkExitDurationSeconds");
                if (prop != null) { prop.floatValue = float.Parse(exitDuration); changes.Add($"exitDuration={exitDuration}"); }
            }

            av3SO.ApplyModifiedProperties();
            EditorUtility.SetDirty(FaceEmoAPI.GetAV3Setting(launcher));
            FaceEmoAPI.RefreshWindowIfOpen(launcher);
            return $"Success: Updated AFK settings: {string.Join(", ", changes)}.";
        }

        [AgentTool("Configure FaceEmo feature toggles. Shows current values when called with no parameters. All params: 'true' or 'false'.")]
        public static string ConfigureFeatureToggles(string emoteSelect = "", string blinkOff = "",
            string danceGimmick = "", string contactLock = "", string overrideToggle = "",
            string voice = "", string handPatternSwap = "", string handPatternDisableLeft = "",
            string handPatternDisableRight = "", string controllerQuest = "",
            string controllerIndex = "", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var av3SO = FaceEmoAPI.GetAV3SettingSO(launcher);
            if (av3SO == null) return "Error: AV3Setting not found.";

            bool anySet = !string.IsNullOrEmpty(emoteSelect) || !string.IsNullOrEmpty(blinkOff)
                || !string.IsNullOrEmpty(danceGimmick) || !string.IsNullOrEmpty(contactLock)
                || !string.IsNullOrEmpty(overrideToggle) || !string.IsNullOrEmpty(voice)
                || !string.IsNullOrEmpty(handPatternSwap) || !string.IsNullOrEmpty(handPatternDisableLeft)
                || !string.IsNullOrEmpty(handPatternDisableRight) || !string.IsNullOrEmpty(controllerQuest)
                || !string.IsNullOrEmpty(controllerIndex);

            if (!anySet)
            {
                var sb = new StringBuilder();
                sb.AppendLine("FaceEmo Feature Toggles:");
                sb.AppendLine($"  EmoteSelect: {av3SO.FindProperty("AddConfig_EmoteSelect")?.boolValue}");
                sb.AppendLine($"  BlinkOff: {av3SO.FindProperty("AddConfig_BlinkOff")?.boolValue}");
                sb.AppendLine($"  DanceGimmick: {av3SO.FindProperty("AddConfig_DanceGimmick")?.boolValue}");
                sb.AppendLine($"  ContactLock: {av3SO.FindProperty("AddConfig_ContactLock")?.boolValue}");
                sb.AppendLine($"  Override: {av3SO.FindProperty("AddConfig_Override")?.boolValue}");
                sb.AppendLine($"  Voice: {av3SO.FindProperty("AddConfig_Voice")?.boolValue}");
                sb.AppendLine($"  HandPattern_Swap: {av3SO.FindProperty("AddConfig_HandPattern_Swap")?.boolValue}");
                sb.AppendLine($"  HandPattern_DisableLeft: {av3SO.FindProperty("AddConfig_HandPattern_DisableLeft")?.boolValue}");
                sb.AppendLine($"  HandPattern_DisableRight: {av3SO.FindProperty("AddConfig_HandPattern_DisableRight")?.boolValue}");
                sb.AppendLine($"  Controller_Quest: {av3SO.FindProperty("AddConfig_Controller_Quest")?.boolValue}");
                sb.AppendLine($"  Controller_Index: {av3SO.FindProperty("AddConfig_Controller_Index")?.boolValue}");
                return sb.ToString().TrimEnd();
            }

            Undo.RecordObject(FaceEmoAPI.GetAV3Setting(launcher), "Configure Feature Toggles");
            var changes = new List<string>();

            void SetToggle(string propName, string value, string label)
            {
                if (string.IsNullOrEmpty(value)) return;
                var prop = av3SO.FindProperty(propName);
                if (prop == null) return;
                prop.boolValue = bool.Parse(value);
                changes.Add($"{label}={value}");
            }

            SetToggle("AddConfig_EmoteSelect", emoteSelect, "emoteSelect");
            SetToggle("AddConfig_BlinkOff", blinkOff, "blinkOff");
            SetToggle("AddConfig_DanceGimmick", danceGimmick, "danceGimmick");
            SetToggle("AddConfig_ContactLock", contactLock, "contactLock");
            SetToggle("AddConfig_Override", overrideToggle, "override");
            SetToggle("AddConfig_Voice", voice, "voice");
            SetToggle("AddConfig_HandPattern_Swap", handPatternSwap, "handPatternSwap");
            SetToggle("AddConfig_HandPattern_DisableLeft", handPatternDisableLeft, "handPatternDisableLeft");
            SetToggle("AddConfig_HandPattern_DisableRight", handPatternDisableRight, "handPatternDisableRight");
            SetToggle("AddConfig_Controller_Quest", controllerQuest, "controllerQuest");
            SetToggle("AddConfig_Controller_Index", controllerIndex, "controllerIndex");

            av3SO.ApplyModifiedProperties();
            EditorUtility.SetDirty(FaceEmoAPI.GetAV3Setting(launcher));
            FaceEmoAPI.RefreshWindowIfOpen(launcher);
            return $"Success: Updated feature toggles: {string.Join(", ", changes)}.";
        }

        // ═══════════════════════════════════════════
        //  E. Avatar & Copy (2 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Set the target avatar for FaceEmo. Finds VRCAvatarDescriptor by name in scene and assigns it to AV3Setting.TargetAvatar. Use this when FindFaceEmo() shows Avatar=None.")]
        public static string ConfigureTargetAvatar(string avatarName, string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            if (FaceEmoAPI.GetAV3Setting(launcher) == null) return "Error: AV3Setting not found on FaceEmo launcher.";

            // Find VRCAvatarDescriptor in scene by name
            var descriptorType = typeof(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor);
            var descriptors = UnityEngine.Object.FindObjectsOfType(descriptorType);

            MonoBehaviour targetDescriptor = null;

            foreach (var desc in descriptors)
            {
                var comp = desc as Component;
                if (comp == null) continue;
                if (comp.gameObject.name.Contains(avatarName))
                {
                    targetDescriptor = comp as MonoBehaviour;
                    break;
                }
            }

            if (targetDescriptor == null)
            {
                var names = new List<string>();
                foreach (var desc in descriptors)
                {
                    var comp = desc as Component;
                    if (comp != null) names.Add(comp.gameObject.name);
                }
                string available = names.Count > 0 ? $" Available: {string.Join(", ", names)}" : " No VRCAvatarDescriptors found in scene.";
                return $"Error: VRCAvatarDescriptor named '{avatarName}' not found.{available}";
            }

            // Build hierarchy path for TargetAvatarPath
            string avatarPath = targetDescriptor.gameObject.name;
            Transform parent = targetDescriptor.transform.parent;
            while (parent != null)
            {
                avatarPath = parent.name + "/" + avatarPath;
                parent = parent.parent;
            }

            var av3Target = FaceEmoAPI.GetAV3Setting(launcher);
            Undo.RecordObject(av3Target, "Configure FaceEmo Target Avatar");
            av3Target.TargetAvatar = targetDescriptor;
            av3Target.TargetAvatarPath = avatarPath;
            EditorUtility.SetDirty(av3Target);
            FaceEmoAPI.RefreshWindowIfOpen(launcher);

            return $"Success: Set FaceEmo target avatar to '{targetDescriptor.gameObject.name}' (path={avatarPath}).";
        }

        [AgentTool("Copy/duplicate a FaceEmo expression to create a variant. destination: 'Registered' (default), 'Unregistered', or group name.")]
        public static string CopyExpression(string sourceExpressionName, string newDisplayName,
            string destination = "Registered", string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, sourceExpressionName);
            if (modeId == null) return $"Error: Expression '{sourceExpressionName}' not found.";

            string dest = FaceEmoAPI.ResolveDestination(menu, destination);

            if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
                return $"Error: Cannot add item to '{destination}'. The destination may be full (Registered max=7) or not found.";

            string copiedId = FaceEmoAPI.CopyMode(menu, modeId, dest);
            FaceEmoAPI.ModifyModeProperties(menu, copiedId, displayName: newDisplayName);

            FaceEmoAPI.SaveMenu(launcher, menu);
            return $"Success: Copied expression '{sourceExpressionName}' as '{newDisplayName}' to {destination} (id={copiedId}).{FaceEmoAPI.WindowWarning()}";
        }

        // ═══════════════════════════════════════════
        //  F. Cross-tool Integration (2 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Create AnimationClip from current blend shapes AND register as new FaceEmo expression in one step. meshObjectName: mesh with blend shapes. expressionName: display name. animPath: where to save clip. meshPath: optional relative path from avatar root.")]
        public static string CreateAndRegisterExpression(string meshObjectName,
            string expressionName, string animPath, string meshPath = "",
            string destination = "Registered", string faceEmoObjectName = "")
        {
            // Step 1: Create animation clip from blend shapes
            string clipResult = BlendShapeTools.CreateExpressionClip(meshObjectName, animPath, meshPath);
            if (clipResult.StartsWith("Error")) return clipResult;

            // Step 2: Register in FaceEmo
            string addResult = AddExpression(expressionName, destination, animPath, faceEmoObjectName);
            if (addResult.StartsWith("Error"))
                return $"Warning: Animation clip created but FaceEmo registration failed.\nClip: {clipResult}\nFaceEmo: {addResult}";

            return $"Success: Created expression '{expressionName}' with animation from '{meshObjectName}'.\n  Clip: {animPath}\n  {addResult}";
        }

        [AgentTool("Preview a FaceEmo expression on the avatar mesh in Scene view. Resolves the animation from FaceEmo domain model and applies blend shapes. slot: 'Mode' (default), 'Base', 'Left', 'Right', 'Both'.")]
        public static string PreviewFaceEmoExpression(string expressionName,
            string meshObjectName, int branchIndex = -1, string slot = "Mode",
            string faceEmoObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(faceEmoObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: Could not load FaceEmo menu from scene.";

            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, expressionName);
            if (modeId == null) return $"Error: Expression '{expressionName}' not found.";

            // Resolve animation GUID
            string guid = null;

            if (slot.Equals("Mode", StringComparison.OrdinalIgnoreCase))
            {
                guid = mode.Animation?.GUID;
            }
            else if (branchIndex >= 0 && branchIndex < mode.Branches.Count)
            {
                var branch = mode.Branches[branchIndex];
                switch (slot.ToLower())
                {
                    case "base": guid = branch.BaseAnimation?.GUID; break;
                    case "left": guid = branch.LeftHandAnimation?.GUID; break;
                    case "right": guid = branch.RightHandAnimation?.GUID; break;
                    case "both": guid = branch.BothHandsAnimation?.GUID; break;
                    default: return $"Error: Unknown slot '{slot}'.";
                }
            }
            else if (branchIndex >= 0)
            {
                return $"Error: Branch index {branchIndex} out of range (0-{mode.Branches.Count - 1}).";
            }
            else
            {
                guid = mode.Animation?.GUID;
            }

            if (string.IsNullOrEmpty(guid))
                return $"Error: No animation set for '{expressionName}' slot={slot}" +
                       (branchIndex >= 0 ? $" branch[{branchIndex}]" : "") + ".";

            string clipPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(clipPath))
                return $"Error: Could not resolve animation GUID '{guid}'.";

            return BlendShapeTools.PreviewExpressionClip(meshObjectName, clipPath);
        }

        // ═══════════════════════════════════════════
        //  G. Import from FX Layer (1 tool)
        // ═══════════════════════════════════════════

        [AgentTool("Auto-import expression patterns from avatar's FX layer into FaceEmo menu. " +
            "Also imports blink clip, mouth morph cancel clip, contact receivers, and parameter prefix settings. " +
            "This reads the existing FX Animator and creates matching FaceEmo modes/branches.")]
        public static string ImportExpressions(string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            return FaceEmoAPI.ImportAll(launcher);
        }

        // ═══════════════════════════════════════════
        //  H. Expression Building (5 tools)
        //  Thin wrappers over BlendShapeTools so the
        //  LLM can stay within FaceEmo tool namespace.
        // ═══════════════════════════════════════════

        [AgentTool("Search blend shapes on a mesh for expression creation. " +
            "filter (required): keyword to narrow results (e.g. 'smile', 'eye', 'mouth', 'brow'). " +
            "Synonyms are auto-expanded (e.g. 'smile' also matches 'joy', 'happy'). " +
            "NEVER call without filter — always specify a keyword.")]
        public static string SearchExpressionShapes(string meshObjectName, string filter)
        {
            var expanded = BlendShapeTools.ExpandSynonyms(filter);
            return BlendShapeTools.SearchBlendShapesMulti(meshObjectName, filter, expanded);
        }

        [AgentTool("Set blend shape values to preview an expression on the mesh. " +
            "Format: 'shapeName=value;shapeName2=value2' (values 0-100). " +
            "Use SearchExpressionShapes first to find correct shape names.")]
        public static string SetExpressionPreview(string meshObjectName, string blendShapeData)
            => BlendShapeTools.SetMultipleBlendShapes(meshObjectName, blendShapeData);

        [AgentTool("Focus camera on avatar face and capture expression preview image. " +
            "Returns an image for visual verification. Use after SetExpressionPreview.")]
        public static string CaptureExpressionPreview(string avatarRootName)
        {
            BlendShapeTools.FocusOnFace(avatarRootName);
            return BlendShapeTools.CaptureExpressionPreview(avatarRootName);
        }

        [AgentTool("Reset all blend shapes to 0 after expression preview. " +
            "Call this after finishing expression creation or adjustment.")]
        public static string ResetExpressionPreview(string meshObjectName)
            => BlendShapeTools.ResetBlendShapes(meshObjectName);

        [AgentTool("Get current non-zero blend shape values on a mesh. " +
            "Useful for capturing expression state before saving to animation clip.")]
        public static string GetCurrentExpressionValues(string meshObjectName)
            => BlendShapeTools.GetActiveBlendShapes(meshObjectName);

        // ═══════════════════════════════════════════
        //  I. Expression Clip Management (2 tools)
        // ═══════════════════════════════════════════

        [AgentTool("Re-create expression animation clip from current mesh blend shapes " +
            "and update an existing FaceEmo expression's animation assignment. " +
            "Use after adjusting blend shapes with SetExpressionPreview to update an existing expression.")]
        public static string UpdateExpressionAnimation(string expressionName,
            string meshObjectName, string animPath, string meshPath = "",
            string gameObjectName = "")
        {
            // Step 1: Create clip from current mesh weights
            string clipResult = BlendShapeTools.CreateExpressionClip(meshObjectName, animPath, meshPath);
            if (clipResult.StartsWith("Error")) return clipResult;

            // Step 2: Update FaceEmo assignment
            string setResult = SetExpressionAnimation(expressionName, animPath, "Mode", -1, gameObjectName);
            if (setResult.StartsWith("Error"))
                return $"Warning: Clip created at '{animPath}' but FaceEmo update failed: {setResult}";

            return $"Updated '{expressionName}': animation re-created from '{meshObjectName}' and FaceEmo assignment updated.";
        }

        [AgentTool("Create expression animation clip from explicit blend shape data " +
            "and register as new FaceEmo expression in one step. " +
            "Format: 'shapeName=value;shapeName2=value2'. No mesh preview step needed. " +
            "destination: 'Registered' (max 7), group name, or 'Unregistered'.")]
        public static string CreateExpressionFromData(string displayName,
            string animPath, string meshPath, string blendShapeData,
            string destination = "Registered", string gameObjectName = "")
        {
            string clipResult = BlendShapeTools.CreateExpressionClipFromData(animPath, meshPath, blendShapeData);
            if (clipResult.StartsWith("Error")) return clipResult;

            string addResult = AddExpression(displayName, destination, animPath, gameObjectName);
            if (addResult.StartsWith("Error"))
                return $"Warning: Clip created at '{animPath}' but FaceEmo registration failed: {addResult}";

            return $"Created expression '{displayName}' from data.\n  Clip: {animPath}\n  {addResult}";
        }

        // ═══════════════════════════════════════════
        //  J. Apply to Avatar (1 tool)
        // ═══════════════════════════════════════════

        [AgentTool("Apply FaceEmo menu to avatar — generates the FX layer and expression parameters. " +
            "Run this after finishing all expression edits. " +
            "Requires target avatar to be set (use ConfigureTargetAvatar if Avatar=None).")]
        public static string ApplyFaceEmoToAvatar(string gameObjectName = "")
        {
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null) return "Error: FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            string error = FaceEmoAPI.ApplyToAvatar(launcher);
            if (error != null) return $"Error: {error}";

            return "FaceEmo applied to avatar successfully. FX layer has been generated.";
        }
    }
#endif
}
