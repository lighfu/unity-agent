// Editor/Tools/FaceEmoPlanC/Curation/CandidateShapeBuilder.cs
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Curation
{
    /// <summary>
    /// intent → 10-15 個の candidate shape を絞り込む。
    /// 既存 FaceProfile (FacePreset / PresetCandidate / BlendShapeCategorizer) を再利用。
    /// </summary>
    public static class CandidateShapeBuilder
    {
        public sealed class Result
        {
            public List<string> Candidates { get; set; }
            public Dictionary<string, float> SeedValues { get; set; }
            public List<ExpressionVariations.Variation> Variations { get; set; }
        }

        private static readonly Dictionary<string, string> Synonyms = new Dictionary<string, string>
        {
            { "ニコニコ", "smile" }, { "笑顔", "smile" }, { "笑い", "smile" },
            { "happy",   "smile" }, { "joy",  "smile" },
            { "怒り",     "angry" },
            { "悲しい",   "sad" },
            { "驚き",     "surprise" },
        };

        // FaceCategory enum name strings used for section.category comparison (CategorySection.category is string).
        // Must match FaceCategory.ToString() values: "Eye", "Mouth", "Brow", "Cheek", "Tongue", "Other".
        private static readonly string[] SmileCategories    = { "Mouth", "Cheek", "Eye" };
        private static readonly string[] AngryCategories    = { "Brow", "Mouth", "Eye" };
        private static readonly string[] SadCategories      = { "Brow", "Eye", "Mouth" };
        private static readonly string[] SurpriseCategories = { "Eye", "Mouth", "Brow" };
        private static readonly string[] WinkCategories     = { "Eye" };
        private static readonly string[] SleepyCategories   = { "Eye" };
        private static readonly string[] DefaultCategories  = { "Mouth", "Eye", "Brow", "Cheek" };

        /// <summary>
        /// Build candidate shapes for the given intent and profile.
        /// Note: preset name matching uses FacePreset.ToString() and case-insensitive comparison
        /// to tolerate serialization differences (e.g., if FaceProfile builder lowercases names).
        /// If no matching preset is found, seed silently falls back to empty.
        /// </summary>
        public static Result Build(FaceBlendShapeProfile profile, string intent, int breadth)
        {
            if (profile == null) return Empty();
            string normIntent = NormalizeIntent(intent);

            var seed = new Dictionary<string, float>();
            FacePreset? preset = BlendShapeCategorizer.ResolvePreset(normIntent);
            if (preset.HasValue)
            {
                // PresetCandidate.preset stores the FacePreset enum name as a string (e.g., "Smile").
                string presetName = preset.Value.ToString();
                var candidate = profile.presets.FirstOrDefault(p => string.Equals(p.preset, presetName, System.StringComparison.OrdinalIgnoreCase));
                if (candidate != null)
                {
                    // PresetEntry has: string shapeName, float value
                    foreach (var e in candidate.entries.Take(3))
                        seed[e.shapeName] = e.value;
                }
            }

            var relatedRanked = ComputeRelatedShapes(profile, normIntent, seed.Keys);

            int targetSize = breadth <= 0 ? 15 : breadth;
            var ordered = seed.Keys.Concat(relatedRanked.Where(s => !seed.ContainsKey(s)))
                                   .Distinct()
                                   .Take(targetSize)
                                   .ToList();

            var variations = ExpressionVariations.Generate(normIntent, seed, ordered);

            return new Result
            {
                Candidates = ordered,
                SeedValues = seed,
                Variations = variations,
            };
        }

        private static Result Empty() => new Result
        {
            Candidates = new List<string>(),
            SeedValues = new Dictionary<string, float>(),
            Variations = new List<ExpressionVariations.Variation>(),
        };

        private static string NormalizeIntent(string intent)
        {
            if (string.IsNullOrEmpty(intent)) return "";
            foreach (var kv in Synonyms)
                if (intent.Contains(kv.Key)) return kv.Value;
            return intent.ToLowerInvariant();
        }

        private static string[] GetTargetCategories(string intent)
        {
            switch (intent)
            {
                case "smile":    return SmileCategories;
                case "angry":    return AngryCategories;
                case "sad":
                case "cry":      return SadCategories;
                case "surprise": return SurpriseCategories;
                case "wink":     return WinkCategories;
                case "sleepy":   return SleepyCategories;
                default:         return DefaultCategories;
            }
        }

        private static List<string> ComputeRelatedShapes(
            FaceBlendShapeProfile profile, string intent, IEnumerable<string> seedKeys)
        {
            var seedPrefixes = seedKeys
                .Select(s => System.Text.RegularExpressions.Regex.Replace(s, @"_\d+$", ""))
                .Where(s => s.Length >= 4)
                .Distinct()
                .ToList();

            var targetCategories = GetTargetCategories(intent);

            var ranked = new List<(string name, int score)>();
            foreach (var section in profile.categories)
            {
                // CategorySection.category is a string (e.g., "Eye", "Mouth")
                if (!targetCategories.Contains(section.category)) continue;
                foreach (var entry in section.shapes)
                {
                    int score = 0;
                    // ShapeEntry.tags is List<string>, ShapeEntry.name is the blendshape name
                    if (entry.tags != null && entry.tags.Contains(intent)) score += 100;
                    if (seedPrefixes.Any(p => entry.name.IndexOf(
                            p, System.StringComparison.OrdinalIgnoreCase) >= 0))
                        score += 50;
                    score += 10;
                    ranked.Add((entry.name, score));
                }
            }
            return ranked.OrderByDescending(r => r.score).Select(r => r.name).Distinct().ToList();
        }
    }
}
