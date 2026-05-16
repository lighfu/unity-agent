// Editor/Tools/FaceEmoPlanC/AgentTools/GestureTools.cs
#if FACE_EMO
using System.Linq;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using Suzuryg.FaceEmo.Domain;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.AgentTools
{
    public static class GestureTools
    {
        [AgentTool(
            "指定 Mode の Branch を全列挙。各 Branch の (gesture, hand, slot, clipName) を返す。")]
        public static string ListGestureBindings(string launcherName, string modeName)
        {
            var launcher = FaceEmoAPI.FindLauncher(launcherName);
            if (launcher == null) return $"Error: launcher '{launcherName}' not found.";
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: failed to load menu.";
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) return $"Error: Mode '{modeName}' not found.";
            var sb = new StringBuilder();
            sb.AppendLine($"Mode={modeName} branches={mode.Branches?.Count ?? 0}");
            if (mode.Branches == null) return sb.ToString();
            for (int i = 0; i < mode.Branches.Count; i++)
            {
                var b = mode.Branches[i];
                var cond = b.Conditions?.FirstOrDefault();
                string g = cond != null ? cond.HandGesture.ToString() : "?";
                string h = cond != null ? cond.Hand.ToString() : "?";
                sb.AppendLine($"  [{i}] ({h}, {g})");
                sb.AppendLine($"    Base={ClipName(b.BaseAnimation)}");
                sb.AppendLine($"    Left={ClipName(b.LeftHandAnimation)} Right={ClipName(b.RightHandAnimation)} Both={ClipName(b.BothHandsAnimation)}");
            }
            return sb.ToString();
        }

        [AgentTool(
            "Mode 内で (gesture, hand) に一致する Branch index を検索。なければ -1。")]
        public static string FindBranchByCondition(string launcherName, string modeName, string gesture, string hand)
        {
            var launcher = FaceEmoAPI.FindLauncher(launcherName);
            if (launcher == null) return $"Error: launcher '{launcherName}' not found.";
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: failed to load menu.";
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) return $"Error: Mode '{modeName}' not found.";
            var hg = FaceEmoAPI.ParseGesture(gesture);
            var hd = FaceEmoAPI.ParseHand(hand);
            if (mode.Branches != null)
            {
                for (int i = 0; i < mode.Branches.Count; i++)
                {
                    var b = mode.Branches[i];
                    if (b.Conditions != null && b.Conditions.Any(c => c.Hand == hd && c.HandGesture == hg))
                        return $"{i}";
                }
            }
            return "-1";
        }

        [AgentTool(
            "新規 (gesture, hand) Branch を追加した場合に first-match 規則で無効化される既存 Branch をリストアップ。")]
        public static string DetectGestureConflicts(string launcherName, string modeName, string gesture, string hand)
        {
            var launcher = FaceEmoAPI.FindLauncher(launcherName);
            if (launcher == null) return $"Error: launcher '{launcherName}' not found.";
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: failed to load menu.";
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) return $"Error: Mode '{modeName}' not found.";
            var hg = FaceEmoAPI.ParseGesture(gesture);
            var hd = FaceEmoAPI.ParseHand(hand);
            var sb = new StringBuilder();
            sb.AppendLine($"Conflicts for new ({hand}, {gesture}) in Mode={modeName}:");
            if (mode.Branches == null) return sb.ToString();
            for (int i = 0; i < mode.Branches.Count; i++)
            {
                var b = mode.Branches[i];
                if (b.Conditions == null) continue;
                foreach (var c in b.Conditions)
                {
                    if (c.HandGesture != hg) continue;
                    bool overlap = OverlapsHand(c.Hand, hd);
                    if (overlap)
                        sb.AppendLine($"  [{i}] ({c.Hand}, {c.HandGesture}) → would be shadowed by new ({hand}, {gesture})");
                }
            }
            return sb.ToString();
        }

        [AgentTool(
            "指定 clip を Mode の (gesture, hand, slot) Branch に割当。新規 Branch 自動作成。" +
            "overwriteMode: Overwrite (既存 slot を上書き) / EditExisting (既存を保持) / Cancel.")]
        public static string AssignClipToGesture(
            string launcherName, string modeName, string gesture, string hand, string slot,
            string clipPath, string overwriteMode = "Overwrite")
        {
            var launcher = FaceEmoAPI.FindLauncher(launcherName);
            if (launcher == null) return $"Error: launcher '{launcherName}' not found.";
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) return "Error: failed to load menu.";
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) return $"Error: Mode '{modeName}' not found.";
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AnimationClip>(clipPath);
            if (clip == null) return $"Error: clip '{clipPath}' not loadable.";
            var hg = FaceEmoAPI.ParseGesture(gesture);
            var hd = FaceEmoAPI.ParseHand(hand);
            var slotType = FaceEmoAPI.ParseBranchSlot(slot) ?? BranchAnimationType.Base;

            int branchIdx = -1;
            if (mode.Branches != null)
            {
                for (int i = 0; i < mode.Branches.Count; i++)
                {
                    var b = mode.Branches[i];
                    if (b.Conditions != null && b.Conditions.Any(c => c.Hand == hd && c.HandGesture == hg))
                    { branchIdx = i; break; }
                }
            }
            bool isNew = branchIdx < 0;
            string omLower = (overwriteMode ?? "Overwrite").ToLowerInvariant();
            if (!isNew && omLower == "cancel")
                return "Cancelled: existing branch present, overwriteMode=Cancel.";
            if (!isNew && omLower == "editexisting")
                return $"OK existing branch [{branchIdx}] kept (no overwrite).";

            UnityEditor.Undo.SetCurrentGroupName($"PlanC: assign clip ({hand},{gesture}) on {modeName}");
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();

            if (isNew)
            {
                var conds = new System.Collections.Generic.List<Condition>
                { new Condition(hd, hg, ComparisonOperator.Equals) };
                branchIdx = FaceEmoAPI.AddBranch(menu, modeId, conds);
            }
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(clipPath);
            FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIdx, slotType,
                new Animation(guid));
            FaceEmoAPI.SaveMenu(launcher, menu);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
            return $"OK branchIndex={branchIdx} slot={slot} isNew={isNew}";
        }

        // ───── helpers ─────

        private static string ClipName(Animation a)
        {
            if (a == null || string.IsNullOrEmpty(a.GUID)) return "(empty)";
            var p = UnityEditor.AssetDatabase.GUIDToAssetPath(a.GUID);
            return string.IsNullOrEmpty(p) ? "(missing)" : System.IO.Path.GetFileNameWithoutExtension(p);
        }

        // Either/Both overlap with all; Left vs Right disjoint; OneSide overlaps Left and Right but not Both
        private static bool OverlapsHand(Hand existing, Hand incoming)
        {
            if (existing == incoming) return true;
            if (incoming == Hand.Either) return true;
            if (existing == Hand.Either) return true;
            if (incoming == Hand.OneSide && (existing == Hand.Left || existing == Hand.Right)) return true;
            if (existing == Hand.OneSide && (incoming == Hand.Left || incoming == Hand.Right)) return true;
            return false;
        }
    }
}
#endif
