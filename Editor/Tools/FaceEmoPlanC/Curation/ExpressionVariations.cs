// Editor/Tools/FaceEmoPlanC/Curation/ExpressionVariations.cs
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Curation
{
    /// <summary>
    /// intent ごとの 3 案 (やさしい/満面/はにかみ 等) を生成する。
    /// 各 variation は同 candidate set を共有し、活性 shape の subset と値が違う。
    /// </summary>
    public static class ExpressionVariations
    {
        public sealed class Variation
        {
            public string Name { get; set; }                     // 例 "やさしい"
            public Dictionary<string, float> Values { get; set; } // shapeName → 0-100
        }

        // intent → 3 案ラベル
        private static readonly Dictionary<string, string[]> IntentLabels =
            new Dictionary<string, string[]>
        {
            { "smile",    new[] { "やさしい", "満面", "はにかみ" } },
            { "angry",    new[] { "不満", "激怒", "むすっと" } },
            { "sad",      new[] { "しょんぼり", "大泣き", "我慢" } },
            { "surprise", new[] { "びっくり", "驚愕", "ぽかん" } },
            { "wink",     new[] { "軽い", "しっかり", "キュート" } },
            { "sleepy",   new[] { "うとうと", "熟睡", "寝起き" } },
        };

        private static readonly string[] GenericLabels = { "弱", "中", "強" };

        /// <summary>intent に対する variation ラベル 3 個 (preset 不在は generic)。</summary>
        public static string[] GetLabels(string intent)
        {
            return IntentLabels.TryGetValue(intent?.ToLowerInvariant() ?? "", out var labels)
                ? labels : GenericLabels;
        }

        /// <summary>
        /// candidate shape リスト + seed 値 → 3 variation を生成。
        /// 案1 (low): seed × 0.6 のみ。案2 (mid): seed × 1.0 + 関連 shape 70%。案3 (high): seed × 0.7 + intent 別追加。
        /// </summary>
        public static List<Variation> Generate(
            string intent,
            IReadOnlyDictionary<string, float> seedValues,    // PresetMap 由来 (shapeName → 100 ベース)
            IReadOnlyList<string> relatedShapes)               // candidate set 残り
        {
            var labels = GetLabels(intent);
            var variations = new List<Variation>();

            // 案1: low intensity
            var low = new Dictionary<string, float>();
            foreach (var kv in seedValues) low[kv.Key] = kv.Value * 0.6f;
            variations.Add(new Variation { Name = labels[0], Values = low });

            // 案2: full + related も活性
            var mid = new Dictionary<string, float>();
            foreach (var kv in seedValues) mid[kv.Key] = kv.Value;
            foreach (var rs in relatedShapes.Take(5))           // 上位 5 個
                if (!mid.ContainsKey(rs)) mid[rs] = 70f;
            variations.Add(new Variation { Name = labels[1], Values = mid });

            // 案3: intent 別の追加 shape (shy なら eye_close / cheek_blush)
            var high = new Dictionary<string, float>();
            foreach (var kv in seedValues) high[kv.Key] = kv.Value * 0.7f;
            foreach (var extra in GetIntentExtras(intent, relatedShapes))
                if (!high.ContainsKey(extra.Key)) high[extra.Key] = extra.Value;
            variations.Add(new Variation { Name = labels[2], Values = high });

            return variations;
        }

        // intent ごとに案3 で追加 activate したい shape (related から見つかれば)
        private static IEnumerable<KeyValuePair<string, float>> GetIntentExtras(
            string intent, IReadOnlyList<string> relatedShapes)
        {
            string i = intent?.ToLowerInvariant() ?? "";
            if (i == "smile") // はにかみ = shy + blush
            {
                foreach (var rs in relatedShapes)
                {
                    if (rs.ToLowerInvariant().Contains("close")) yield return Kv(rs, 50f);
                    if (rs.ToLowerInvariant().Contains("blush")) yield return Kv(rs, 60f);
                    if (rs.ToLowerInvariant().Contains("照") || rs.Contains("てれ")) yield return Kv(rs, 60f);
                }
            }
        }

        private static KeyValuePair<string, float> Kv(string k, float v)
            => new KeyValuePair<string, float>(k, v);
    }
}
