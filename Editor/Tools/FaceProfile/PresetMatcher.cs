using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile
{
    // 各 FacePreset (Smile/Angry/...) に対して、必要な顔パーツ × 感情タグ × 推奨値の組を定義し、
    // 与えられた ShapeEntry 群から候補ミックスを生成する。
    public static class PresetMatcher
    {
        private struct PresetSlot
        {
            public FaceCategory[] Categories;
            public string[] Tags;
            public bool Required;
            public float DefaultValue;

            public PresetSlot(FaceCategory[] categories, string[] tags, bool required, float defaultValue)
            {
                Categories = categories;
                Tags = tags;
                Required = required;
                DefaultValue = defaultValue;
            }
        }

        private static readonly Dictionary<FacePreset, PresetSlot[]> Specs = new Dictionary<FacePreset, PresetSlot[]>
        {
            [FacePreset.Smile] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "smile", "joy", "happy", "fun" }, required: true,  defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "smile", "joy", "happy", "fun", "narrow" }, required: false, defaultValue: 80f),
                new PresetSlot(new[] { FaceCategory.Cheek }, new[] { "up", "smile", "blush" }, required: false, defaultValue: 50f),
            },
            [FacePreset.Angry] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Brow, FaceCategory.Eye }, new[] { "angry" }, required: true, defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "angry" }, required: false, defaultValue: 80f),
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "angry", "narrow" }, required: false, defaultValue: 70f),
            },
            [FacePreset.Surprised] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "surprised", "open", "wide" }, required: true,  defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "surprised", "open", "wide" }, required: false, defaultValue: 80f),
                new PresetSlot(new[] { FaceCategory.Brow },  new[] { "surprised", "up" }, required: false, defaultValue: 70f),
            },
            [FacePreset.Sad] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Brow },  new[] { "sad", "down" }, required: true,  defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "sad", "down" }, required: false, defaultValue: 70f),
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "sad" }, required: false, defaultValue: 60f),
            },
            [FacePreset.Cry] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "cry" }, required: true,  defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "cry", "down", "sad" }, required: false, defaultValue: 80f),
                new PresetSlot(new[] { FaceCategory.Brow },  new[] { "cry", "sad", "down" }, required: false, defaultValue: 70f),
            },
            [FacePreset.Wink] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "wink" }, required: true, defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "smile", "joy" }, required: false, defaultValue: 60f),
            },
            [FacePreset.Sleep] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "sleep", "close" }, required: true,  defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Brow },  new[] { "sleep", "down" }, required: false, defaultValue: 50f),
            },
            [FacePreset.Kiss] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "kiss" }, required: true, defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "close", "narrow" }, required: false, defaultValue: 60f),
            },
            [FacePreset.Shy] = new[]
            {
                new PresetSlot(new[] { FaceCategory.Cheek }, new[] { "shy", "blush" }, required: true, defaultValue: 100f),
                new PresetSlot(new[] { FaceCategory.Mouth }, new[] { "smile" }, required: false, defaultValue: 60f),
                new PresetSlot(new[] { FaceCategory.Eye },   new[] { "shy", "narrow", "close" }, required: false, defaultValue: 70f),
            },
        };

        public static List<PresetCandidate> ComputeAll(IEnumerable<ShapeEntry> entries)
        {
            var result = new List<PresetCandidate>();
            var entryList = entries?.ToList() ?? new List<ShapeEntry>();
            foreach (var kv in Specs)
            {
                var candidate = ComputeOne(kv.Key, kv.Value, entryList);
                result.Add(candidate);
            }
            return result;
        }

        public static PresetCandidate ComputeOne(FacePreset preset, IEnumerable<ShapeEntry> entries)
        {
            if (!Specs.TryGetValue(preset, out var slots))
                return new PresetCandidate { preset = preset.ToString(), confidence = 0f };
            return ComputeOne(preset, slots, entries.ToList());
        }

        private static PresetCandidate ComputeOne(FacePreset preset, PresetSlot[] slots, List<ShapeEntry> entries)
        {
            var candidate = new PresetCandidate { preset = preset.ToString() };
            var usedShapes = new HashSet<string>();
            int totalRequired = 0;
            int matchedRequired = 0;
            int totalOptional = 0;
            int matchedOptional = 0;

            foreach (var slot in slots)
            {
                if (slot.Required) totalRequired++;
                else totalOptional++;

                var match = FindBestMatch(entries, slot, usedShapes);
                if (match != null)
                {
                    var key = $"{match.smrPath}::{match.name}";
                    usedShapes.Add(key);

                    candidate.entries.Add(new PresetEntry
                    {
                        smrPath = match.smrPath,
                        shapeName = match.name,
                        value = slot.DefaultValue,
                        slotCategory = slot.Categories[0].ToString(),
                    });

                    if (slot.Required) matchedRequired++;
                    else matchedOptional++;
                }
            }

            candidate.matchedRequired = matchedRequired;
            candidate.totalRequired = totalRequired;

            if (totalRequired == 0)
            {
                candidate.confidence = totalOptional > 0 ? (float)matchedOptional / totalOptional : 0f;
            }
            else
            {
                float requiredScore = (float)matchedRequired / totalRequired;
                float optionalScore = totalOptional > 0 ? (float)matchedOptional / totalOptional : 0f;
                candidate.confidence = requiredScore * 0.7f + optionalScore * 0.3f;
            }

            return candidate;
        }

        private static ShapeEntry FindBestMatch(List<ShapeEntry> entries, PresetSlot slot, HashSet<string> usedShapes)
        {
            ShapeEntry best = null;
            int bestTagHits = 0;
            int bestNameLen = int.MaxValue;

            foreach (var entry in entries)
            {
                var key = $"{entry.smrPath}::{entry.name}";
                if (usedShapes.Contains(key)) continue;

                if (!CategoriesMatch(entry, slot.Categories)) continue;

                int tagHits = CountTagHits(entry, slot.Tags);
                if (tagHits == 0) continue;

                int nameLen = entry.name?.Length ?? int.MaxValue;
                bool better = tagHits > bestTagHits
                    || (tagHits == bestTagHits && nameLen < bestNameLen);

                if (better)
                {
                    best = entry;
                    bestTagHits = tagHits;
                    bestNameLen = nameLen;
                }
            }

            return best;
        }

        private static bool CategoriesMatch(ShapeEntry entry, FaceCategory[] categories)
        {
            if (entry == null || string.IsNullOrEmpty(entry.category)) return false;
            foreach (var cat in categories)
            {
                if (entry.category == cat.ToString()) return true;
            }
            return false;
        }

        private static int CountTagHits(ShapeEntry entry, string[] tags)
        {
            if (entry?.tags == null || entry.tags.Count == 0) return 0;
            int hits = 0;
            foreach (var t in tags)
                if (entry.tags.Contains(t)) hits++;
            return hits;
        }
    }
}
