using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AnimationClip の binding path を一括で付け替えるツール。
    ///
    /// メッシュの階層上の位置が変わると、それを参照するクリップの binding path が外れ、
    /// アニメーションは<b>エラーも警告も出さずに無反応になる</b>。頭部を別の胴体へ移植した、
    /// 衣装をリネームした、Modular Avatar 導入でオブジェクトを動かした — いずれでも起きる。
    /// 直すこと自体は「全カーブの path を書き換える」だけだが、
    /// <c>SetAnimationCurve</c> は 1 カーブずつなので 9 クリップ × 13 カーブでは現実的でない。
    ///
    /// <c>Imported/&lt;timestamp&gt;/</c> に置かれる FaceEmo のクリップのように
    /// <b>バイナリシリアライズされた .anim はテキスト置換が効かない</b>
    /// (プロジェクトが Force Text であっても)。そのため <see cref="AnimationUtility"/> 経由で
    /// binding を読み直して書き戻す。
    /// </summary>
    public static class AnimationRebindTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        /// <summary>1 クリップ分の書き換え計画。</summary>
        private sealed class ClipPlan
        {
            public string SourcePath;
            public string OutputPath;          // in-place なら SourcePath と同じ
            public AnimationClip Clip;
            public int MatchedFloat;
            public int MatchedObject;
            public int TotalCurves;
            public List<string> Samples = new List<string>();   // "old -> new" を数件だけ
            public List<EditorCurveBinding> NewFloatBindings = new List<EditorCurveBinding>();
            public List<AnimationCurve> FloatCurves = new List<AnimationCurve>();
            public List<EditorCurveBinding> NewObjectBindings = new List<EditorCurveBinding>();
            public List<ObjectReferenceKeyframe[]> ObjectCurves = new List<ObjectReferenceKeyframe[]>();
            public List<EditorCurveBinding> OldFloatBindings = new List<EditorCurveBinding>();
            public List<EditorCurveBinding> OldObjectBindings = new List<EditorCurveBinding>();
        }

        [AgentTool("Rewrite the binding paths of every curve in one or more AnimationClips. "
                 + "Use this after a mesh moves in the hierarchy (head transplant, costume rename, "
                 + "Modular Avatar reparenting): the clip keeps working on the old path and simply does "
                 + "NOTHING on the new hierarchy, with no error or warning. "
                 + "clipPaths: ';'-separated .anim asset paths and/or folders (folders are searched recursively). "
                 + "fromPath: the current binding path to match, or '*' to match every binding. "
                 + "toPath: the replacement (empty means the avatar root). "
                 + "matchMode: 'exact' (default, path must equal fromPath) or 'prefix' (fromPath and everything "
                 + "under it, matched on path separators so 'Body' does not match 'BodyExtra'). "
                 + "With fromPath='*', every binding path is moved under toPath and matchMode is ignored. "
                 + "outputFolder: write modified copies there instead of editing in place. "
                 + "verifyAgainst: a GameObject name to check the NEW paths against — reports paths that do not "
                 + "resolve and blendShape.* properties whose blend shape is missing on the destination mesh. "
                 + "dryRun: report the diff without writing. "
                 + "Works on binary-serialized .anim files (FaceEmo's Imported clips) because it goes through "
                 + "AnimationUtility, not text replacement.",
                 Risk = ToolRisk.Caution)]
        public static string RebindAnimationClipPaths(string clipPaths, string fromPath,
            string toPath = "", string matchMode = "exact", string outputFolder = "",
            string verifyAgainst = "", bool dryRun = false)
        {
            if (string.IsNullOrWhiteSpace(clipPaths))
                return "Error: clipPaths is required — pass .anim asset paths and/or folders separated by ';'.";
            if (string.IsNullOrEmpty(fromPath))
                return "Error: fromPath is required. Pass the current binding path, or '*' to match every binding.";

            bool matchAll = fromPath == "*";
            bool prefixMode = matchMode.Equals("prefix", StringComparison.OrdinalIgnoreCase);
            if (!matchAll && !prefixMode && !matchMode.Equals("exact", StringComparison.OrdinalIgnoreCase))
                return $"Error: unknown matchMode '{matchMode}'. Use 'exact' or 'prefix'.";

            if (!matchAll && fromPath == toPath)
                return $"Error: fromPath and toPath are both '{fromPath}' — nothing would change.";

            // ── 対象クリップを集める ──
            var clipAssetPaths = new List<string>();
            string collectError = CollectClips(clipPaths, clipAssetPaths);
            if (collectError != null) return collectError;
            if (clipAssetPaths.Count == 0)
                return $"Error: no AnimationClip found under '{clipPaths}'.";

            // ── 出力先の検証 ──
            if (!string.IsNullOrEmpty(outputFolder))
            {
                outputFolder = outputFolder.TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(outputFolder))
                    return $"Error: outputFolder '{outputFolder}' does not exist. Create it first, or omit it to edit in place.";

                var clashes = clipAssetPaths
                    .Select(p => outputFolder + "/" + System.IO.Path.GetFileName(p))
                    .Where(p => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                    .ToList();
                if (clashes.Count > 0)
                    return $"Error: {clashes.Count} file(s) already exist in '{outputFolder}':\n  "
                         + string.Join("\n  ", clashes.Take(10))
                         + (clashes.Count > 10 ? $"\n  ... and {clashes.Count - 10} more" : "")
                         + "\nPick an empty folder — silently overwriting or auto-renaming would make it "
                         + "impossible to tell which copy the Mode actually references.";
            }

            // ── 計画を組む ──
            var plans = new List<ClipPlan>();
            foreach (string assetPath in clipAssetPaths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip == null) return $"Error: could not load AnimationClip at '{assetPath}'.";

                var plan = BuildPlan(clip, assetPath, fromPath, toPath, matchAll, prefixMode,
                                     out string planError);
                if (planError != null) return planError;
                plans.Add(plan);
            }

            int totalMatched = plans.Sum(p => p.MatchedFloat + p.MatchedObject);
            if (totalMatched == 0)
            {
                // 「0 件書き換えて成功」は、この作業でいちばん危険な返答。fromPath の綴りが
                // 違っていても同じ見え方になり、直したつもりで無反応のまま先へ進んでしまう。
                var seen = plans.SelectMany(p => p.OldFloatBindings.Concat(p.OldObjectBindings))
                                .Select(b => b.path).Distinct().OrderBy(x => x).Take(15).ToList();
                return $"Error: fromPath '{fromPath}' matched no curve in any of the {plans.Count} clip(s). "
                     + "Nothing was written.\n  paths actually present: "
                     + (seen.Count == 0 ? "(the clips have no curves)"
                        : string.Join(", ", seen.Select(p => p.Length == 0 ? "(root)" : p)));
            }

            // ── 書き込み ──
            var written = new List<ClipPlan>();
            if (!dryRun)
            {
                foreach (var plan in plans)
                {
                    if (plan.MatchedFloat + plan.MatchedObject == 0) continue;   // 変化なしは触らない

                    var target = plan.Clip;
                    if (!string.IsNullOrEmpty(outputFolder))
                    {
                        string dst = outputFolder + "/" + System.IO.Path.GetFileName(plan.SourcePath);
                        if (!AssetDatabase.CopyAsset(plan.SourcePath, dst))
                            return $"Error: failed to copy '{plan.SourcePath}' to '{dst}'. "
                                 + $"{written.Count} clip(s) were already written before this failure.";
                        target = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
                        if (target == null)
                            return $"Error: copied '{dst}' but could not load it back as an AnimationClip.";
                        plan.OutputPath = dst;
                    }

                    Undo.RecordObject(target, "Rebind Animation Clip Paths");
                    ApplyPlan(target, plan);
                    EditorUtility.SetDirty(target);
                    written.Add(plan);
                }
                AssetDatabase.SaveAssets();
            }

            return BuildReport(dryRun, fromPath, toPath, matchAll, prefixMode, outputFolder,
                               plans, verifyAgainst);
        }

        // ═══════════════════════════════════════════
        //  Planning
        // ═══════════════════════════════════════════

        private static ClipPlan BuildPlan(AnimationClip clip, string assetPath, string fromPath,
            string toPath, bool matchAll, bool prefixMode, out string error)
        {
            error = null;
            var plan = new ClipPlan { Clip = clip, SourcePath = assetPath, OutputPath = assetPath };

            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            // オブジェクト参照カーブ (マテリアル差し替え等) も同じ扱いが要る。
            // 片方だけ直すと、見た目には直ったのにマテリアルだけ動かないクリップができる。
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            plan.TotalCurves = floatBindings.Length + objBindings.Length;

            // 衝突検出用。書き換え後に (path, type, propertyName) が重なると、後から
            // SetEditorCurve した方が前のカーブを黙って上書きして消す。
            var occupied = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var b in floatBindings)
            {
                plan.OldFloatBindings.Add(b);
                var nb = b;
                if (TryRewrite(b.path, fromPath, toPath, matchAll, prefixMode, out string newPath))
                {
                    nb.path = newPath;
                    plan.MatchedFloat++;
                    if (plan.Samples.Count < 3)
                        plan.Samples.Add($"{Show(b.path)} -> {Show(newPath)} ({b.propertyName})");
                }
                if (!Reserve(occupied, nb, assetPath, out error)) return plan;
                plan.NewFloatBindings.Add(nb);
                plan.FloatCurves.Add(AnimationUtility.GetEditorCurve(clip, b));
            }

            foreach (var b in objBindings)
            {
                plan.OldObjectBindings.Add(b);
                var nb = b;
                if (TryRewrite(b.path, fromPath, toPath, matchAll, prefixMode, out string newPath))
                {
                    nb.path = newPath;
                    plan.MatchedObject++;
                    if (plan.Samples.Count < 3)
                        plan.Samples.Add($"{Show(b.path)} -> {Show(newPath)} ({b.propertyName}, object ref)");
                }
                if (!Reserve(occupied, nb, assetPath, out error)) return plan;
                plan.NewObjectBindings.Add(nb);
                plan.ObjectCurves.Add(AnimationUtility.GetObjectReferenceCurve(clip, b));
            }

            return plan;
        }

        /// <summary>書き換え後の binding が他とぶつからないか記録しつつ確認する。</summary>
        private static bool Reserve(Dictionary<string, string> occupied, EditorCurveBinding b,
            string assetPath, out string error)
        {
            string key = b.path + "\0" + (b.type != null ? b.type.FullName : "?") + "\0" + b.propertyName;
            if (occupied.TryGetValue(key, out string firstOwner))
            {
                error = $"Error: in '{assetPath}', the rewrite would put two different curves on the same "
                      + $"binding ({Show(b.path)} / {b.propertyName}). The second one would silently "
                      + $"overwrite the first, so nothing was written. Conflicting source path: {firstOwner}.";
                return false;
            }
            occupied[key] = Show(b.path);
            error = null;
            return true;
        }

        /// <summary>
        /// 1 本の binding path を書き換える。対象外なら false。
        /// prefix 一致は必ずパス区切りで切る — そうしないと "Body" が "BodyExtra" にも当たる。
        /// </summary>
        private static bool TryRewrite(string oldPath, string fromPath, string toPath,
            bool matchAll, bool prefixMode, out string newPath)
        {
            newPath = null;

            if (matchAll)
            {
                newPath = Join(toPath, oldPath);
                return newPath != oldPath;
            }

            if (string.Equals(oldPath, fromPath, StringComparison.Ordinal))
            {
                newPath = toPath;
                return true;
            }

            if (prefixMode
                && oldPath.Length > fromPath.Length
                && oldPath.StartsWith(fromPath, StringComparison.Ordinal)
                && oldPath[fromPath.Length] == '/')
            {
                newPath = Join(toPath, oldPath.Substring(fromPath.Length + 1));
                return true;
            }

            return false;
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + "/" + b;
        }

        /// <summary>ルート (空文字) を目に見える形にする。</summary>
        private static string Show(string path) => string.IsNullOrEmpty(path) ? "(root)" : path;

        private static void ApplyPlan(AnimationClip clip, ClipPlan plan)
        {
            // いったん全消ししてから入れ直す。1 本ずつ「消して足す」と、書き換え先が
            // まだ消していない既存 binding と一致した瞬間にそれを潰してしまう。
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                AnimationUtility.SetEditorCurve(clip, b, null);
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                AnimationUtility.SetObjectReferenceCurve(clip, b, null);

            for (int i = 0; i < plan.NewFloatBindings.Count; i++)
                AnimationUtility.SetEditorCurve(clip, plan.NewFloatBindings[i], plan.FloatCurves[i]);
            for (int i = 0; i < plan.NewObjectBindings.Count; i++)
                AnimationUtility.SetObjectReferenceCurve(clip, plan.NewObjectBindings[i], plan.ObjectCurves[i]);
        }

        // ═══════════════════════════════════════════
        //  Clip collection
        // ═══════════════════════════════════════════

        private static string CollectClips(string clipPaths, List<string> result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in clipPaths.Split(';'))
            {
                string entry = raw.Trim().TrimEnd('/');
                if (entry.Length == 0) continue;

                if (AssetDatabase.IsValidFolder(entry))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { entry }))
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        // FindAssets は FBX 等に埋め込まれたクリップも拾う。埋め込みクリップは
                        // アセットとして書き戻せないので、独立した .anim だけを対象にする。
                        if (p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) && seen.Add(p))
                            result.Add(p);
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(entry)))
                    return $"Error: '{entry}' is neither an existing asset nor a folder.";
                if (!entry.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                    return $"Error: '{entry}' is not a .anim file. Clips embedded in an FBX cannot be rebound "
                         + "in place — extract them first.";
                if (seen.Add(entry)) result.Add(entry);
            }

            result.Sort(StringComparer.Ordinal);
            return null;
        }

        // ═══════════════════════════════════════════
        //  Report + verification
        // ═══════════════════════════════════════════

        private static string BuildReport(bool dryRun, string fromPath, string toPath, bool matchAll,
            bool prefixMode, string outputFolder, List<ClipPlan> plans, string verifyAgainst)
        {
            var sb = new StringBuilder();
            var changed = plans.Where(p => p.MatchedFloat + p.MatchedObject > 0).ToList();
            int curves = changed.Sum(p => p.MatchedFloat + p.MatchedObject);

            string rule = matchAll
                ? $"every binding -> under {Show(toPath)}"
                : $"{Show(fromPath)} -> {Show(toPath)} ({(prefixMode ? "prefix" : "exact")})";

            sb.AppendLine(dryRun
                ? $"DRY RUN — nothing was written. Rule: {rule}"
                : $"Success: rebound {curves} curve(s) in {changed.Count} clip(s). Rule: {rule}");

            if (!dryRun && !string.IsNullOrEmpty(outputFolder))
                sb.AppendLine($"  copies written to '{outputFolder}' — the originals are untouched.");
            else if (!dryRun)
                sb.AppendLine("  edited IN PLACE (Ctrl+Z restores the clips).");

            sb.AppendLine($"  clips: {plans.Count} scanned, {changed.Count} affected, "
                        + $"{plans.Count - changed.Count} untouched");

            foreach (var p in changed)
            {
                int n = p.MatchedFloat + p.MatchedObject;
                string outNote = (!dryRun && p.OutputPath != p.SourcePath) ? $" -> {p.OutputPath}" : "";
                sb.AppendLine($"    {p.SourcePath}: {n}/{p.TotalCurves} curve(s){outNote}");
                foreach (string s in p.Samples) sb.AppendLine($"      {s}");
                if (n > p.Samples.Count) sb.AppendLine($"      ... and {n - p.Samples.Count} more");
            }

            var untouched = plans.Where(p => p.MatchedFloat + p.MatchedObject == 0).ToList();
            if (untouched.Count > 0)
            {
                sb.AppendLine($"  {untouched.Count} clip(s) had no matching curve and were left alone:");
                foreach (var p in untouched.Take(10)) sb.AppendLine($"    {p.SourcePath}");
                if (untouched.Count > 10) sb.AppendLine($"    ... and {untouched.Count - 10} more");
            }

            if (!string.IsNullOrEmpty(verifyAgainst))
                AppendVerification(sb, verifyAgainst, changed);
            else
                sb.AppendLine("  Pass verifyAgainst=<avatar root name> to check that the new paths actually "
                            + "resolve and that the blend shapes exist — a clip pointing at a missing path or "
                            + "a missing blend shape applies cleanly and changes nothing.");

            if (dryRun) sb.Append("  Re-run with dryRun=false to apply.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 書き換え後のパスが実際に解決するか、blendShape.* のシェイプが存在するかを照合する。
        /// 「直しても無反応」の原因はほぼここなので、直した直後に見えるようにする。
        /// </summary>
        private static void AppendVerification(StringBuilder sb, string rootName, List<ClipPlan> changed)
        {
            var root = FindGO(rootName);
            if (root == null)
            {
                sb.AppendLine($"  verify: GameObject '{rootName}' not found in the scene — could not verify. "
                            + "(Inactive objects are searched too, so check the spelling.)");
                return;
            }

            sb.AppendLine($"  verify against '{root.name}':");

            // path -> そこで要求される blendShape 名の集合
            var wanted = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var plan in changed)
            {
                foreach (var b in plan.NewFloatBindings.Concat(plan.NewObjectBindings))
                {
                    if (!wanted.TryGetValue(b.path, out var shapes))
                    {
                        shapes = new HashSet<string>(StringComparer.Ordinal);
                        wanted[b.path] = shapes;
                    }
                    if (b.propertyName != null && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        shapes.Add(b.propertyName.Substring("blendShape.".Length));
                }
            }

            bool allGood = true;
            foreach (var kv in wanted.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string path = kv.Key;
                Transform t = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
                if (t == null)
                {
                    allGood = false;
                    sb.AppendLine($"    ✗ {Show(path)} does NOT resolve under '{root.name}' — curves on this path do nothing.");
                    continue;
                }

                if (kv.Value.Count == 0)
                {
                    sb.AppendLine($"    ✓ {Show(path)} resolves");
                    continue;
                }

                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null)
                {
                    allGood = false;
                    sb.AppendLine($"    ✗ {Show(path)} resolves but has no SkinnedMeshRenderer with a mesh — "
                                + $"{kv.Value.Count} blendShape curve(s) cannot apply.");
                    continue;
                }

                var have = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    have.Add(smr.sharedMesh.GetBlendShapeName(i));

                var missing = kv.Value.Where(s => !have.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
                if (missing.Count == 0)
                {
                    sb.AppendLine($"    ✓ {Show(path)} resolves — all {kv.Value.Count} blend shape(s) exist "
                                + $"(mesh has {have.Count})");
                    continue;
                }

                allGood = false;
                sb.AppendLine($"    ✗ {Show(path)}: {missing.Count} of {kv.Value.Count} blend shape(s) are MISSING "
                            + $"on '{smr.sharedMesh.name}':");
                foreach (string m in missing.Take(15)) sb.AppendLine($"        {m}");
                if (missing.Count > 15) sb.AppendLine($"        ... and {missing.Count - 15} more");
            }

            if (allGood)
                sb.AppendLine("    every rewritten path resolves and every blend shape exists.");
            else
                sb.AppendLine("    ⚠ the marks above are why an animation can look correctly rebound and still "
                            + "do nothing. Fix them before testing in-game.");
        }
    }
}
