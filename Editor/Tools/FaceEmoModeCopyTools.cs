#if FACE_EMO
using Suzuryg.FaceEmo.Domain;
using Suzuryg.FaceEmo.Components;
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
    /// ランチャーをまたいで FaceEmo の Mode を丸ごと複製するツール。
    ///
    /// 「頭部を別アバターへ移植して表情一式を持っていく」「衣装違いの派生アバターを作る」
    /// といった作業では、15 ブランチの Mode を組み直すのに <c>SetExpressionAnimation</c> ×16 と
    /// <c>ModifyBranchProperties</c> ×14 で 30 回近いツール呼び出しが要り、その大半が
    /// 同じ設定の繰り返しだった (issue #8)。
    ///
    /// 既存の <c>CopyExpression</c> は <c>Menu.CopyMode</c> を使うが、これは<b>同一メニュー内</b>の
    /// 複製しかできない。ランチャーが違えば Menu インスタンスも別なので、宛先側で Mode を
    /// 作り直してブランチ・条件・トラッキング設定を 1 つずつ移す必要がある。
    /// </summary>
    public static class FaceEmoModeCopyTools
    {
        /// <summary>クリップ差し替えの解決結果 1 件分。</summary>
        private struct RemapEntry
        {
            public string Where;        // "Mode" / "Branch[3].Base" など
            public string FromPath;
            public string ToPath;
            public bool Remapped;       // false なら元のクリップのまま
        }

        [AgentTool("Copy a whole FaceEmo Mode from one launcher to another, including every branch, "
                 + "its conditions, and the eye/mouth tracking, blink, mouth-morph-canceler and trigger flags. "
                 + "Use this for head transplants and costume variants instead of rebuilding a 15-branch Mode "
                 + "with ~30 separate calls. "
                 + "srcModeName: the Mode display name on the source launcher. "
                 + "dstGameObjectName: destination FaceEmo launcher GameObject name (required). "
                 + "srcGameObjectName: source launcher (default: auto-find). "
                 + "newModeName: display name at the destination (default: same as source). "
                 + "destination: 'Registered' (max 7 items), 'Unregistered', or a group display name. "
                 + "clipRemap: either 'oldPath->newPath;oldPath2->newPath2' (paths or clip names) or a single "
                 + "folder path, in which case each source clip is looked up by name inside that folder. "
                 + "Clips with no match keep the ORIGINAL clip and are listed in the report — check that list, "
                 + "because a Mode that still points at the source avatar's clips looks correct but does nothing. "
                 + "dryRun: report the plan without writing anything.",
                 Risk = ToolRisk.Caution)]
        public static string CopyFaceEmoMode(string srcModeName, string dstGameObjectName,
            string srcGameObjectName = "", string newModeName = "", string destination = "Registered",
            string clipRemap = "", bool dryRun = false)
        {
            if (string.IsNullOrEmpty(dstGameObjectName))
                return "Error: dstGameObjectName is required — pass the destination FaceEmo launcher GameObject name. "
                     + "Use FindFaceEmo to list the launchers in the scene.";

            var srcLauncher = FaceEmoAPI.FindLauncher(srcGameObjectName);
            if (srcLauncher == null) return "Error: source FaceEmo launcher not found." + FaceEmoAPI.GetLauncherHint();

            var dstLauncher = FaceEmoAPI.FindLauncher(dstGameObjectName);
            if (dstLauncher == null) return $"Error: destination FaceEmo launcher '{dstGameObjectName}' not found." + FaceEmoAPI.GetLauncherHint();

            bool sameLauncher = ReferenceEquals(srcLauncher, dstLauncher)
                             || srcLauncher.gameObject == dstLauncher.gameObject;

            var srcMenu = FaceEmoAPI.LoadMenu(srcLauncher);
            if (srcMenu == null) return $"Error: could not load the menu of '{srcLauncher.gameObject.name}'.";

            // 同じランチャーなら Menu インスタンスも 1 つにする。2 つ読むと、保存した方の変更で
            // もう一方の変更が上書きされて消える。
            var dstMenu = sameLauncher ? srcMenu : FaceEmoAPI.LoadMenu(dstLauncher);
            if (dstMenu == null) return $"Error: could not load the menu of '{dstLauncher.gameObject.name}'.";

            var (srcModeId, srcMode) = FaceEmoAPI.FindExpression(srcMenu, srcModeName);
            if (srcModeId == null)
                return $"Error: Mode '{srcModeName}' not found on launcher '{srcLauncher.gameObject.name}'. "
                     + "Use ListFaceEmoExpressions to see what it has.";

            string dest = FaceEmoAPI.ResolveDestination(dstMenu, destination);
            if (!FaceEmoAPI.CanAddMenuItemTo(dstMenu, dest))
                return $"Error: cannot add a Mode to '{destination}' on '{dstLauncher.gameObject.name}'. "
                     + "Registered holds at most 7 items — move something to Unregistered or into a group first.";

            // クリップ差し替え表を組む
            Dictionary<string, string> explicitMap;
            string remapFolder;
            string remapError = ParseClipRemap(clipRemap, out explicitMap, out remapFolder);
            if (remapError != null) return remapError;
            bool remapRequested = explicitMap.Count > 0 || remapFolder != null;

            var log = new List<RemapEntry>();

            // ── 宛先に Mode を作る ──
            string newId = FaceEmoAPI.AddMode(dstMenu, dest);
            string finalName = string.IsNullOrEmpty(newModeName) ? srcMode.DisplayName : newModeName;
            FaceEmoAPI.ModifyModeProperties(dstMenu, newId,
                displayName: finalName,
                changeDefaultFace: srcMode.ChangeDefaultFace,
                useAnimationNameAsDisplayName: srcMode.UseAnimationNameAsDisplayName,
                eyeTrackingControl: srcMode.EyeTrackingControl,
                mouthTrackingControl: srcMode.MouthTrackingControl,
                blinkEnabled: srcMode.BlinkEnabled,
                mouthMorphCancelerEnabled: srcMode.MouthMorphCancelerEnabled);

            var modeAnim = Remap(srcMode.Animation, "Mode", explicitMap, remapFolder, log);
            if (modeAnim != null) FaceEmoAPI.SetModeAnimation(dstMenu, modeAnim, newId);

            // ── ブランチを 1 つずつ移す ──
            int branchCount = srcMode.Branches?.Count ?? 0;
            for (int b = 0; b < branchCount; b++)
            {
                var srcBranch = srcMode.Branches[b];

                // 条件はコピーして渡す。srcBranch.Conditions は IReadOnlyList なのでそのままは使えない。
                var conds = new List<Condition>();
                if (srcBranch.Conditions != null) conds.AddRange(srcBranch.Conditions);

                FaceEmoAPI.AddBranch(dstMenu, newId, conds);

                FaceEmoAPI.ModifyBranchProperties(dstMenu, newId, b,
                    eyeTrackingControl: srcBranch.EyeTrackingControl,
                    mouthTrackingControl: srcBranch.MouthTrackingControl,
                    blinkEnabled: srcBranch.BlinkEnabled,
                    mouthMorphCancelerEnabled: srcBranch.MouthMorphCancelerEnabled,
                    isLeftTriggerUsed: srcBranch.IsLeftTriggerUsed,
                    isRightTriggerUsed: srcBranch.IsRightTriggerUsed);

                SetSlot(dstMenu, newId, b, BranchAnimationType.Base, srcBranch.BaseAnimation,
                        $"Branch[{b}].Base", explicitMap, remapFolder, log);
                SetSlot(dstMenu, newId, b, BranchAnimationType.Left, srcBranch.LeftHandAnimation,
                        $"Branch[{b}].Left", explicitMap, remapFolder, log);
                SetSlot(dstMenu, newId, b, BranchAnimationType.Right, srcBranch.RightHandAnimation,
                        $"Branch[{b}].Right", explicitMap, remapFolder, log);
                SetSlot(dstMenu, newId, b, BranchAnimationType.Both, srcBranch.BothHandsAnimation,
                        $"Branch[{b}].Both", explicitMap, remapFolder, log);
            }

            if (!dryRun) FaceEmoAPI.SaveMenu(dstLauncher, dstMenu);

            return BuildReport(dryRun, srcLauncher.gameObject.name, srcModeName,
                               dstLauncher.gameObject.name, finalName, newId, dest,
                               branchCount, remapRequested, log);
        }

        // ═══════════════════════════════════════════
        //  Clip remapping
        // ═══════════════════════════════════════════

        /// <summary>
        /// clipRemap 引数を解釈する。空なら差し替えなし。フォルダパスならフォルダ照合モード、
        /// それ以外は "from-&gt;to" の並びとして解釈する。戻り値はエラーメッセージ (成功なら null)。
        /// </summary>
        private static string ParseClipRemap(string clipRemap,
            out Dictionary<string, string> explicitMap, out string remapFolder)
        {
            explicitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            remapFolder = null;

            if (string.IsNullOrWhiteSpace(clipRemap)) return null;

            string trimmed = clipRemap.Trim();

            // 矢印を含まないならフォルダ指定とみなす。存在しなければ、対応表のつもりで
            // 綴りを間違えた可能性が高いので、黙って「差し替えなし」に落とさずエラーにする
            // (差し替え漏れは移植先で「動かない表情」として後から発覚するため)。
            if (trimmed.IndexOf("->", StringComparison.Ordinal) < 0 && trimmed.IndexOf('→') < 0)
            {
                if (!AssetDatabase.IsValidFolder(trimmed.TrimEnd('/')))
                    return $"Error: clipRemap '{trimmed}' is neither a 'from->to' list nor an existing folder. "
                         + "Pass 'oldPath->newPath;oldPath2->newPath2', or a folder such as "
                         + "'Assets/Suzuryg/FaceEmo/Imported/20260817_101500'.";
                remapFolder = trimmed.TrimEnd('/');
                return null;
            }

            foreach (string part in trimmed.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;

                string[] tokens = p.IndexOf("->", StringComparison.Ordinal) >= 0
                    ? p.Split(new[] { "->" }, StringSplitOptions.None)
                    : p.Split('→');

                if (tokens.Length != 2)
                    return $"Error: bad clipRemap entry '{p}'. Expected 'from->to'.";

                string from = tokens[0].Trim();
                string to = tokens[1].Trim();
                if (from.Length == 0 || to.Length == 0)
                    return $"Error: bad clipRemap entry '{p}'. Both sides must be non-empty.";

                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(to)))
                    return $"Error: clipRemap target '{to}' does not exist. "
                         + "The right-hand side must be an asset path of a clip that is already imported.";

                explicitMap[from] = to;
                // 名前だけでも引けるようにしておく (左辺にパスを書いたときの利便のため)。
                string fromName = System.IO.Path.GetFileNameWithoutExtension(from);
                if (!string.IsNullOrEmpty(fromName) && !explicitMap.ContainsKey(fromName))
                    explicitMap[fromName] = to;
            }

            return explicitMap.Count > 0 ? null : "Error: clipRemap contained no usable 'from->to' entries.";
        }

        /// <summary>
        /// 元クリップに対応する差し替え先を決める。差し替えが要らない / 見つからない場合は
        /// 元の <see cref="FaceEmoAnimation"/> をそのまま返す。src が null なら null。
        /// </summary>
        private static FaceEmoAnimation Remap(FaceEmoAnimation src, string where,
            Dictionary<string, string> explicitMap, string remapFolder, List<RemapEntry> log)
        {
            if (src == null || string.IsNullOrEmpty(src.GUID)) return null;

            string fromPath = AssetDatabase.GUIDToAssetPath(src.GUID);
            string fromName = string.IsNullOrEmpty(fromPath)
                ? null : System.IO.Path.GetFileNameWithoutExtension(fromPath);

            string toPath = null;
            if (!string.IsNullOrEmpty(fromPath) && explicitMap.TryGetValue(fromPath, out string byPath))
                toPath = byPath;
            else if (!string.IsNullOrEmpty(fromName) && explicitMap.TryGetValue(fromName, out string byName))
                toPath = byName;
            else if (remapFolder != null && !string.IsNullOrEmpty(fromName))
                toPath = FindInFolder(remapFolder, fromName);

            var entry = new RemapEntry
            {
                Where = where,
                FromPath = string.IsNullOrEmpty(fromPath) ? $"(missing asset, GUID:{src.GUID})" : fromPath,
                ToPath = toPath,
                Remapped = toPath != null && toPath != fromPath,
            };
            log.Add(entry);

            if (!entry.Remapped) return src;

            string newGuid = AssetDatabase.AssetPathToGUID(toPath);
            // ここに来る時点で存在は確認済みだが、フォルダ照合の結果は検証していないので念のため。
            if (string.IsNullOrEmpty(newGuid)) return src;
            return new FaceEmoAnimation(newGuid);
        }

        /// <summary>フォルダ内から同名の AnimationClip を探す。無ければ null。</summary>
        private static string FindInFolder(string folder, string clipName)
        {
            string[] guids = AssetDatabase.FindAssets($"t:AnimationClip \"{clipName}\"", new[] { folder });
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                // FindAssets は部分一致なので、名前が完全一致するものだけを採る。
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), clipName,
                        StringComparison.Ordinal))
                    return path;
            }
            return null;
        }

        private static void SetSlot(FaceEmoMenu menu, string modeId, int branchIndex,
            BranchAnimationType slot, FaceEmoAnimation src, string where,
            Dictionary<string, string> explicitMap, string remapFolder, List<RemapEntry> log)
        {
            var anim = Remap(src, where, explicitMap, remapFolder, log);
            if (anim == null) return;   // 元が未設定なら宛先も未設定のままにする
            FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIndex, slot, anim);
        }

        // ═══════════════════════════════════════════
        //  Report
        // ═══════════════════════════════════════════

        private static string BuildReport(bool dryRun, string srcLauncher, string srcModeName,
            string dstLauncher, string dstModeName, string newId, string dest,
            int branchCount, bool remapRequested, List<RemapEntry> log)
        {
            var sb = new StringBuilder();
            sb.AppendLine(dryRun
                ? $"DRY RUN — nothing was written. Would copy Mode '{srcModeName}' from '{srcLauncher}' "
                  + $"to '{dstLauncher}' as '{dstModeName}'."
                : $"Success: copied Mode '{srcModeName}' from '{srcLauncher}' to '{dstLauncher}' "
                  + $"as '{dstModeName}' (id={newId}).");
            sb.AppendLine($"  branches: {branchCount}  (conditions, tracking, blink, mouth-morph-canceler and trigger flags copied)");

            int assigned = log.Count;
            int remapped = log.Count(e => e.Remapped);
            var kept = log.Where(e => !e.Remapped).ToList();

            sb.AppendLine($"  clips: {assigned} slot(s) set, {remapped} remapped, {kept.Count} kept as-is");

            if (remapped > 0)
            {
                sb.AppendLine("  remapped:");
                foreach (var e in log.Where(x => x.Remapped))
                    sb.AppendLine($"    {e.Where}: {e.FromPath} -> {e.ToPath}");
            }

            if (remapRequested && kept.Count > 0)
            {
                // 差し替えを頼まれたのに一部が元のままなのは、移植先アバターに対して
                // 効かないクリップが残るということ。見た目は成功するので必ず名指しで出す。
                sb.AppendLine($"  ⚠ {kept.Count} clip(s) had NO remap target and still point at the ORIGINAL asset:");
                foreach (var e in kept)
                    sb.AppendLine($"    {e.Where}: {e.FromPath}");
                sb.AppendLine("    If these belong to the source avatar, the copied Mode will look correct but do nothing "
                            + "on the destination avatar. Re-run with a clipRemap entry for each, or fix them with "
                            + "SetExpressionAnimation.");
            }

            if (!dryRun)
            {
                sb.AppendLine("  Next: check the blend shape names actually exist on the destination mesh — a clip that "
                            + "targets missing shapes applies cleanly and changes nothing.");
                sb.Append(FaceEmoAPI.WindowWarning());
            }
            else
            {
                sb.Append("  Re-run with dryRun=false to apply.");
            }

            return sb.ToString().TrimEnd();
        }
    }
#endif
}
