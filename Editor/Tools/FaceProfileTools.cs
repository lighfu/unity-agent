using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    // 表情設定フローの高レベル API。アバター単位のプロファイルをキャッシュして、
    // 顔メッシュ特定 → カテゴリ分類 → プリセット推定 → preview 値設定までを一気通貫で提供する。
    //
    // 設計の動機:
    //   従来の表情設定フロー (SearchExpressionShapes / SetExpressionPreview) は
    //   メッシュ名のハードコード ('Body') と単一キーワード検索に依存しており、
    //   Chiffon 系 (Body=顔メッシュの罠) や複数 SMR 分散アバターで精度が低かった。
    //   このツールは IdentifyFaceSmr を必須前段としてプロファイルを構築し、
    //   AI が SMR を意識せずに表情を組めるようにする。
    public static class FaceProfileTools
    {
        // ═══════════════════════════════════════════
        //  Public MCP tools
        // ═══════════════════════════════════════════

        [AgentTool("アバターの顔 BlendShape を解析し、Face SMR と追加 SMR (eyelash/tongue/teeth 等)、" +
            "カテゴリ分類済み shape リスト、各表情プリセット (smile/angry/surprised/...) の候補ミックスを返す。" +
            "結果は Library/UnityAgent/face-profiles/ にキャッシュされる。force=true で強制再生成。" +
            "表情設定ワークフローでは最初にこのツールを呼び、SuggestExpressionShapes / SetExpressionPreviewMulti と組み合わせる。",
            Author = "ajisaiflow",
            Category = "FaceProfile",
            Risk = ToolRisk.Safe)]
        public static string AnalyzeFaceBlendShapes(string avatarRootName, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";

            var root = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (root == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var faceSmr = AvatarAnatomyTools.FindFaceSmrInternal(root, out string tier, out string reason);
            if (faceSmr == null)
            {
                return $"Error: Could not identify face SMR on '{avatarRootName}' (reason={reason}). " +
                       $"Try IdentifyFaceSmr / IdentifyBodySmr to diagnose.";
            }

            var bodySmr = AvatarAnatomyTools.FindBodySmrInternal(root);
            string fingerprint = FaceProfileCache.ComputeFingerprint(faceSmr, avatarRootName);

            FaceBlendShapeProfile profile = null;
            bool fromCache = false;
            if (!force)
            {
                profile = FaceProfileCache.TryGet(fingerprint);
                fromCache = profile != null;
            }

            if (profile == null)
            {
                var extras = AvatarAnatomyTools.FindExtraFaceSmrsInternal(root, faceSmr, bodySmr);
                profile = BuildProfile(root, faceSmr, extras, tier, fingerprint);
                FaceProfileCache.Save(profile);
            }

            return FormatProfileJson(profile, fromCache);
        }

        [AgentTool("プリセット (smile/angry/surprised/sad/cry/wink/sleep/kiss/shy) または任意キーワードから、" +
            "そのアバターに最適な BlendShape ミックスを返す。" +
            "戻り値は SetExpressionPreviewMulti 互換の 'shape=value;...' 形式。値域は 0-100 (NEVER 0-1)。" +
            "プロファイル未生成なら自動で AnalyzeFaceBlendShapes を実行する。" +
            "intent はプリセット名 (smile/angry/...) または日本語キーワード (笑顔/怒り/...) を受け付ける。",
            Author = "ajisaiflow",
            Category = "FaceProfile",
            Risk = ToolRisk.Safe)]
        public static string SuggestExpressionShapes(string avatarRootName, string intent)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";
            if (string.IsNullOrWhiteSpace(intent))
                return "Error: intent is empty (try 'smile', 'angry', 'surprised', or a Japanese keyword like '笑顔').";

            var profile = LoadOrBuild(avatarRootName, out string err);
            if (profile == null) return err;

            FacePreset? preset = BlendShapeCategorizer.ResolvePreset(intent);
            if (preset.HasValue)
            {
                var candidate = profile.presets.FirstOrDefault(p => p.preset == preset.Value.ToString());
                if (candidate == null || candidate.entries.Count == 0)
                {
                    return $"Error: Preset '{preset.Value}' has no candidate shapes on this avatar. " +
                           $"Confidence may be 0. Try SearchExpressionShapesV2 with categories like 'eye,mouth' as fallback.";
                }
                return FormatPresetSuggestion(candidate, intent, preset.Value);
            }

            // Fallback: keyword based search across categorized shapes.
            return FormatKeywordSuggestion(profile, intent);
        }

        [AgentTool("複数 SMR にまたがる BlendShape を一括設定する。" +
            "blendShapeData フォーマット: 'shapeName=value;shapeName2=value2' (値域 0-100)。" +
            "プロファイルから shape を持つ SMR を自動でルーティングするので、AI は SMR を意識しなくて良い。" +
            "値域 0-100 を超える値は警告し、1.0 以下の値は誤って 0-1 で渡された可能性をログに残す。",
            Author = "ajisaiflow",
            Category = "FaceProfile",
            Risk = ToolRisk.Safe)]
        public static string SetExpressionPreviewMulti(string avatarRootName, string blendShapeData)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";
            if (string.IsNullOrWhiteSpace(blendShapeData))
                return "Error: blendShapeData is empty. Format: 'shapeName=value;shapeName2=value2'";

            var profile = LoadOrBuild(avatarRootName, out string err);
            if (profile == null) return err;

            var entries = ParseShapeData(blendShapeData);
            if (entries.Count == 0)
                return "Error: No valid 'shape=value' pairs in blendShapeData.";

            // 値域チェック
            var rangeWarnings = new List<string>();
            int suspiciouslyLow = 0;
            foreach (var (_, value) in entries)
            {
                if (value < 0f || value > 100f)
                    rangeWarnings.Add($"value {value:F2} out of [0, 100]");
                if (value > 0f && value <= 1f) suspiciouslyLow++;
            }
            if (suspiciouslyLow == entries.Count && entries.Count > 0)
            {
                return "Error: All values are between 0 and 1.0. " +
                       "BlendShape weights use 0-100 range, NOT 0-1. " +
                       "Did you confuse with VRChat Expression Parameter floats? " +
                       "Multiply by 100 (e.g. 0.8 → 80) and retry.";
            }

            // shape → SMR のルックアップを構築
            var index = BuildShapeIndex(profile);

            // SMR ごとに設定値を集計
            var smrUpdates = new Dictionary<string, List<(string shapeName, int idx, float value)>>();
            var notFound = new List<string>();
            var resolved = new List<string>();

            foreach (var (shapeName, value) in entries)
            {
                if (!index.TryGetValue(shapeName.ToLowerInvariant(), out var hits) || hits.Count == 0)
                {
                    // 部分一致を試みる
                    var partial = TryPartialMatch(index, shapeName);
                    if (partial == null)
                    {
                        notFound.Add(shapeName);
                        continue;
                    }
                    hits = partial;
                }

                foreach (var hit in hits)
                {
                    if (!smrUpdates.TryGetValue(hit.smrPath, out var list))
                    {
                        list = new List<(string, int, float)>();
                        smrUpdates[hit.smrPath] = list;
                    }
                    list.Add((hit.shapeName, hit.index, value));
                }
                resolved.Add($"{shapeName}={value:F0}");
            }

            // 各 SMR に値設定
            int appliedCount = 0;
            var smrResults = new List<string>();
            foreach (var kv in smrUpdates)
            {
                var smr = ResolveSmr(kv.Key);
                if (smr == null || smr.sharedMesh == null)
                {
                    smrResults.Add($"  [skip] SMR or mesh missing at path '{kv.Key}' (avatar may have changed since cache; rerun AnalyzeFaceBlendShapes with force=true)");
                    continue;
                }
                Undo.RecordObject(smr, "SetExpressionPreviewMulti");
                int shapeCount = smr.sharedMesh.blendShapeCount;
                foreach (var update in kv.Value)
                {
                    if (update.idx >= 0 && update.idx < shapeCount)
                    {
                        smr.SetBlendShapeWeight(update.idx, update.value);
                        appliedCount++;
                    }
                }
                smrResults.Add($"  [{smr.name}] {kv.Value.Count} shape(s)");
            }

            SceneView.RepaintAll();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Applied {appliedCount} blend shape value(s) across {smrUpdates.Count} SMR(s).");
            foreach (var line in smrResults) sb.AppendLine(line);
            if (resolved.Count > 0) sb.AppendLine($"  Resolved: {string.Join(", ", resolved)}");
            if (notFound.Count > 0)
                sb.AppendLine($"  Warning: {notFound.Count} shape(s) not found: {string.Join(", ", notFound)}");
            if (rangeWarnings.Count > 0)
                sb.AppendLine($"  Range warnings: {string.Join("; ", rangeWarnings)}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("プロファイル上の categorized shape を、複数のカテゴリキーワードで一括検索する。" +
            "categories: カンマ区切り (例 'eye,mouth,brow')。" +
            "戻り値は SMR ごとに該当 shape を列挙する。" +
            "プリセットが unfit な場合のフォールバック / 細部調整に使う。",
            Author = "ajisaiflow",
            Category = "FaceProfile",
            Risk = ToolRisk.Safe)]
        public static string SearchExpressionShapesV2(string avatarRootName, string categories)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";
            if (string.IsNullOrWhiteSpace(categories))
                return "Error: categories is empty (e.g. 'eye,mouth,brow').";

            var profile = LoadOrBuild(avatarRootName, out string err);
            if (profile == null) return err;

            var requestedCategories = categories.Split(',', ';')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(NormalizeCategory)
                .Where(c => c.HasValue)
                .Select(c => c.Value.ToString())
                .ToHashSet();

            if (requestedCategories.Count == 0)
            {
                return "Error: No valid categories. Use eye, mouth, brow, cheek, tongue, or other.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Categorized shapes for '{avatarRootName}' (filter: {string.Join(", ", requestedCategories)}):");

            int totalFound = 0;
            foreach (var section in profile.categories)
            {
                if (!requestedCategories.Contains(section.category)) continue;
                if (section.shapes.Count == 0) continue;

                sb.AppendLine($"  [{section.category}] ({section.shapes.Count}):");
                foreach (var shape in section.shapes)
                {
                    string tagsStr = shape.tags != null && shape.tags.Count > 0
                        ? $" tags={string.Join(",", shape.tags)}" : "";
                    sb.AppendLine($"    {shape.name}  (smr={ShortSmrName(shape.smrPath)}){tagsStr}");
                    totalFound++;
                }
            }

            if (totalFound == 0)
                sb.AppendLine($"  (no shapes found in requested categories)");
            else
                sb.AppendLine($"  Total: {totalFound} shape(s)");

            return sb.ToString().TrimEnd();
        }

        // ═══════════════════════════════════════════
        //  Internal helpers
        // ═══════════════════════════════════════════

        private static FaceBlendShapeProfile LoadOrBuild(string avatarRootName, out string error)
        {
            error = null;
            var root = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (root == null)
            {
                error = $"Error: GameObject '{avatarRootName}' not found.";
                return null;
            }

            var faceSmr = AvatarAnatomyTools.FindFaceSmrInternal(root, out string tier, out string reason);
            if (faceSmr == null)
            {
                error = $"Error: Could not identify face SMR on '{avatarRootName}' (reason={reason}). " +
                        $"Try IdentifyFaceSmr / IdentifyBodySmr to diagnose.";
                return null;
            }

            string fingerprint = FaceProfileCache.ComputeFingerprint(faceSmr, avatarRootName);
            var profile = FaceProfileCache.TryGet(fingerprint);
            if (profile != null) return profile;

            var bodySmr = AvatarAnatomyTools.FindBodySmrInternal(root);
            var extras = AvatarAnatomyTools.FindExtraFaceSmrsInternal(root, faceSmr, bodySmr);
            profile = BuildProfile(root, faceSmr, extras, tier, fingerprint);
            FaceProfileCache.Save(profile);
            return profile;
        }

        private static FaceBlendShapeProfile BuildProfile(
            GameObject avatarRoot,
            SkinnedMeshRenderer faceSmr,
            List<SkinnedMeshRenderer> extras,
            string faceSmrTier,
            string fingerprint)
        {
            var profile = new FaceBlendShapeProfile
            {
                avatarRootPath = AvatarAnatomyTools.GetHierarchyPathInternal(avatarRoot),
                faceSmrPath = AvatarAnatomyTools.GetHierarchyPathInternal(faceSmr.gameObject),
                faceSmrTier = faceSmrTier ?? string.Empty,
                avatarFingerprint = fingerprint,
                cachedAtIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                faceSmrShapes = faceSmr.sharedMesh != null ? faceSmr.sharedMesh.blendShapeCount : 0,
            };

            var allEntries = new List<ShapeEntry>();

            // face SMR を最優先で追加
            CollectShapes(faceSmr, profile.faceSmrPath, allEntries);

            // extra SMR (eyelash/tongue/teeth) を追加
            foreach (var smr in extras)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                string path = AvatarAnatomyTools.GetHierarchyPathInternal(smr.gameObject);
                profile.extraFaceSmrPaths.Add(path);
                CollectShapes(smr, path, allEntries);
            }

            profile.totalShapes = allEntries.Count;

            // categorized
            var grouped = new Dictionary<string, List<ShapeEntry>>();
            foreach (var entry in allEntries)
            {
                if (!grouped.TryGetValue(entry.category, out var list))
                {
                    list = new List<ShapeEntry>();
                    grouped[entry.category] = list;
                }
                list.Add(entry);
            }

            foreach (FaceCategory cat in Enum.GetValues(typeof(FaceCategory)))
            {
                string key = cat.ToString();
                grouped.TryGetValue(key, out var shapes);
                profile.categories.Add(new CategorySection
                {
                    category = key,
                    shapes = shapes ?? new List<ShapeEntry>(),
                });
            }

            // presets
            profile.presets = PresetMatcher.ComputeAll(allEntries);

            return profile;
        }

        private static void CollectShapes(SkinnedMeshRenderer smr, string smrPath, List<ShapeEntry> entries)
        {
            if (smr == null || smr.sharedMesh == null) return;
            var mesh = smr.sharedMesh;
            int count = mesh.blendShapeCount;
            for (int i = 0; i < count; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                var (cat, tags) = BlendShapeCategorizer.Categorize(name);
                entries.Add(new ShapeEntry
                {
                    smrPath = smrPath,
                    index = i,
                    name = name,
                    category = cat.ToString(),
                    tags = tags,
                });
            }
        }

        private static Dictionary<string, List<(string smrPath, string shapeName, int index)>> BuildShapeIndex(
            FaceBlendShapeProfile profile)
        {
            var index = new Dictionary<string, List<(string, string, int)>>();
            foreach (var section in profile.categories)
            {
                foreach (var shape in section.shapes)
                {
                    string key = shape.name.ToLowerInvariant();
                    if (!index.TryGetValue(key, out var list))
                    {
                        list = new List<(string, string, int)>();
                        index[key] = list;
                    }
                    list.Add((shape.smrPath, shape.name, shape.index));
                }
            }
            return index;
        }

        private static List<(string smrPath, string shapeName, int index)> TryPartialMatch(
            Dictionary<string, List<(string smrPath, string shapeName, int index)>> index,
            string shapeName)
        {
            string queryLower = shapeName.ToLowerInvariant();
            foreach (var kv in index)
            {
                if (kv.Key.Contains(queryLower)) return kv.Value;
            }
            return null;
        }

        private static SkinnedMeshRenderer ResolveSmr(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath)) return null;
            // GameObject.Find は階層パスを '/' 区切りで受け付ける
            var go = GameObject.Find(hierarchyPath);
            if (go == null)
            {
                // 末尾名で fallback 検索
                var name = hierarchyPath.Substring(hierarchyPath.LastIndexOf('/') + 1);
                go = MeshAnalysisTools.FindGameObject(name);
            }
            return go == null ? null : go.GetComponent<SkinnedMeshRenderer>();
        }

        private static List<(string name, float value)> ParseShapeData(string data)
        {
            var result = new List<(string name, float value)>();
            if (string.IsNullOrEmpty(data)) return result;
            foreach (var entry in data.Split(';'))
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) continue;
                string name = trimmed.Substring(0, eqIdx).Trim();
                string valStr = trimmed.Substring(eqIdx + 1).Trim();
                if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    result.Add((name, val));
            }
            return result;
        }

        private static FaceCategory? NormalizeCategory(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string lower = raw.ToLowerInvariant();
            switch (lower)
            {
                case "eye": case "eyes": case "目": return FaceCategory.Eye;
                case "mouth": case "lip": case "lips": case "口": return FaceCategory.Mouth;
                case "brow": case "brows": case "eyebrow": case "eyebrows": case "眉": return FaceCategory.Brow;
                case "cheek": case "cheeks": case "頬": return FaceCategory.Cheek;
                case "tongue": case "舌": return FaceCategory.Tongue;
                case "other": case "その他": return FaceCategory.Other;
            }
            return null;
        }

        private static string ShortSmrName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            int idx = fullPath.LastIndexOf('/');
            return idx >= 0 ? fullPath.Substring(idx + 1) : fullPath;
        }

        // ═══════════════════════════════════════════
        //  Output formatting
        // ═══════════════════════════════════════════

        private static string FormatProfileJson(FaceBlendShapeProfile profile, bool fromCache)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FaceBlendShapeProfile (cache={(fromCache ? "hit" : "fresh")}):");
            sb.AppendLine($"  avatar: {profile.avatarRootPath}");
            sb.AppendLine($"  faceSmr: {profile.faceSmrPath}  (tier={profile.faceSmrTier}, shapes={profile.faceSmrShapes})");
            if (profile.extraFaceSmrPaths.Count > 0)
                sb.AppendLine($"  extraSmrs ({profile.extraFaceSmrPaths.Count}): {string.Join(", ", profile.extraFaceSmrPaths.Select(ShortSmrName))}");
            sb.AppendLine($"  totalShapes: {profile.totalShapes}");
            sb.AppendLine($"  fingerprint: {profile.avatarFingerprint}");
            sb.AppendLine();

            sb.AppendLine("  Categories:");
            foreach (var section in profile.categories)
            {
                if (section.shapes.Count == 0) continue;
                sb.AppendLine($"    {section.category}: {section.shapes.Count} shape(s)");
            }

            sb.AppendLine();
            sb.AppendLine("  Presets (preview = SuggestExpressionShapes(avatar, intent)):");
            foreach (var preset in profile.presets.OrderByDescending(p => p.confidence))
            {
                string status = preset.matchedRequired == preset.totalRequired && preset.totalRequired > 0
                    ? "OK"
                    : preset.matchedRequired > 0 ? "PARTIAL" : "MISS";
                sb.AppendLine($"    {preset.preset}: confidence={preset.confidence:F2} " +
                              $"(req={preset.matchedRequired}/{preset.totalRequired}, " +
                              $"shapes={preset.entries.Count}) [{status}]");
            }

            return sb.ToString().TrimEnd();
        }

        private static string FormatPresetSuggestion(PresetCandidate candidate, string intent, FacePreset preset)
        {
            if (candidate.entries.Count == 0)
                return $"Error: Preset '{preset}' has no candidate shapes (confidence={candidate.confidence:F2}).";

            // SetExpressionPreviewMulti 互換の shape=value 形式
            var pairs = candidate.entries.Select(e => $"{e.shapeName}={e.value:F0}").ToArray();
            string shapeData = string.Join(";", pairs);

            var sb = new StringBuilder();
            sb.AppendLine($"Suggested expression for intent '{intent}' → preset '{preset}':");
            sb.AppendLine($"  confidence: {candidate.confidence:F2} (required {candidate.matchedRequired}/{candidate.totalRequired})");
            sb.AppendLine($"  shapeData: {shapeData}");
            sb.AppendLine($"  Apply: SetExpressionPreviewMulti('<avatar>', '{shapeData}')");
            sb.AppendLine();
            sb.AppendLine("  Breakdown:");
            foreach (var entry in candidate.entries)
            {
                sb.AppendLine($"    [{entry.slotCategory}] {entry.shapeName}={entry.value:F0}  (smr={ShortSmrName(entry.smrPath)})");
            }
            if (candidate.confidence < 0.5f)
            {
                sb.AppendLine();
                sb.AppendLine("  Warning: low confidence. Consider SearchExpressionShapesV2 to inspect categorized shapes manually.");
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatKeywordSuggestion(FaceBlendShapeProfile profile, string keyword)
        {
            string lower = keyword.ToLowerInvariant();
            var hits = new List<ShapeEntry>();
            foreach (var section in profile.categories)
            {
                foreach (var shape in section.shapes)
                {
                    if (shape.name.ToLowerInvariant().Contains(lower)) { hits.Add(shape); continue; }
                    if (shape.tags != null && shape.tags.Any(t => t.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0))
                        hits.Add(shape);
                }
            }
            if (hits.Count == 0)
                return $"No preset matched '{keyword}' and no categorized shapes contain that keyword. " +
                       $"Try SearchExpressionShapesV2 with a category like 'eye,mouth'.";

            var sb = new StringBuilder();
            sb.AppendLine($"No preset matched '{keyword}'. Categorized shape hits ({hits.Count}):");
            foreach (var hit in hits.Take(20))
            {
                string tagsStr = hit.tags != null && hit.tags.Count > 0
                    ? $" tags={string.Join(",", hit.tags)}" : "";
                sb.AppendLine($"  [{hit.category}] {hit.name}  (smr={ShortSmrName(hit.smrPath)}){tagsStr}");
            }
            if (hits.Count > 20) sb.AppendLine($"  ... and {hits.Count - 20} more");
            sb.AppendLine();
            sb.AppendLine("  Build manually: SetExpressionPreviewMulti('<avatar>', 'shape1=80;shape2=100')");
            return sb.ToString().TrimEnd();
        }
    }
}
