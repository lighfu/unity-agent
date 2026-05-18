// Editor/Tools/FaceEmoPlanC/AgentTools/CurationTools.cs
#if FACE_EMO
using System.Linq;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Curation;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.AgentTools
{
    public static class CurationTools
    {
        [AgentTool(
            "intent (smile/angry/etc) から avatar の関連 BlendShape candidate 10-15 個 + variations 3 案を返す。" +
            "breadth: 'narrow'=3 / 'wide'=10-15 (default wide).")]
        public static string SuggestCandidateShapes(string avatarRootName, string intent, string breadth = "wide")
        {
            FaceBlendShapeProfile profile = null;
            try { profile = LoadProfile(avatarRootName); }
            catch (System.Exception ex) { return $"Error: profile load: {ex.Message}"; }
            if (profile == null) return $"Error: profile null for '{avatarRootName}'.";

            int b = (breadth?.ToLowerInvariant() == "narrow") ? 3 : 15;
            var result = CandidateShapeBuilder.Build(profile, intent, b);

            var sb = new StringBuilder();
            sb.AppendLine($"intent={intent} candidates={result.Candidates.Count} variations={result.Variations.Count}");
            sb.Append("candidates=");
            sb.AppendLine(string.Join(",", result.Candidates));
            sb.AppendLine("seed=" + string.Join(";", result.SeedValues.Select(kv => $"{kv.Key}={kv.Value:F0}")));
            for (int i = 0; i < result.Variations.Count; i++)
            {
                var v = result.Variations[i];
                sb.AppendLine($"variation[{i}] name={v.Name}");
                sb.AppendLine("  values=" + string.Join(";", v.Values.Select(kv => $"{kv.Key}={kv.Value:F0}")));
            }
            return sb.ToString();
        }

        [AgentTool(
            "Active session の Editor 値を指定 variation (やさしい/満面/はにかみ 等) の値に差替するための情報を返す。" +
            "戻り値の 'shape=val' リストを SetExpressionPreviewMulti に渡すこと。")]
        public static string ApplyExpressionVariation(string avatarRootName, string intent, string variationName)
        {
            var session = FaceEmoExpressionSession.Active;
            if (session == null) return "Error: no active session.";

            FaceBlendShapeProfile profile = null;
            try { profile = LoadProfile(avatarRootName); } catch { }
            if (profile == null) return $"Error: profile null for '{avatarRootName}'.";
            var build = CandidateShapeBuilder.Build(profile, intent, 15);
            var v = build.Variations.FirstOrDefault(x => x.Name == variationName);
            if (v == null) return $"Error: variation '{variationName}' not found. Available: " +
                                   string.Join(",", build.Variations.Select(x => x.Name));

            var sb = new StringBuilder();
            sb.AppendLine($"variation={variationName} candidates={build.Candidates.Count}");
            foreach (var cand in build.Candidates)
            {
                float val = v.Values.TryGetValue(cand, out var f) ? f : 0f;
                sb.AppendLine($"  {cand}={val:F0}");
            }
            sb.AppendLine($"applied={build.Candidates.Count} (use SetExpressionPreviewMulti to apply on session)");
            return sb.ToString();
        }

        [AgentTool(
            "指定 intent の variation 名 3 個を返す (例: smile→ やさしい,満面,はにかみ)。")]
        public static string ListExpressionVariations(string intent)
        {
            var labels = ExpressionVariations.GetLabels(intent);
            return string.Join(",", labels);
        }

        // ───── helpers ─────

        // FaceProfileTools.LoadOrBuild は internal なので同アセンブリから直接呼べる。
        // キャッシュを利用し、未キャッシュなら FaceSmr 特定 → プロファイル構築 → 保存まで行う。
        private static FaceBlendShapeProfile LoadProfile(string avatarRootName)
        {
            var profile = FaceProfileTools.LoadOrBuild(avatarRootName, out string err);
            if (profile == null)
                throw new System.Exception(err ?? "unknown error");
            return profile;
        }
    }
}
#endif
