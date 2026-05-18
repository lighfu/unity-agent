// Editor/Tools/FaceEmoPlanC/Conventions/IntentGestureMap.cs
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Conventions
{
    /// <summary>
    /// intent (smile/angry/...) → 推奨 HandGesture 名 を返す静的データ。
    /// Step 4a の 8-grid 表示で ★ マーク用。
    /// preset 不在 intent は null を返す (推奨無し)。
    /// </summary>
    public static class IntentGestureMap
    {
        // value は FaceEmoAPI.ParseGesture が受け付ける文字列 (PascalCase)
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "smile",       "HandOpen" },
            { "happy",       "HandOpen" },
            { "joy",         "HandOpen" },
            { "angry",       "Fist" },
            { "mad",         "Fist" },
            { "pout",        "Fist" },
            { "sad",         "Neutral" },
            { "cry",         "Neutral" },
            { "sob",         "Neutral" },
            { "surprise",    "HandOpen" },
            { "shock",       "HandOpen" },
            { "wink",        "Victory" },
            { "playful",     "Victory" },
            { "sleepy",      "Neutral" },
            { "tired",       "Neutral" },
            { "confident",   "ThumbsUp" },
            { "smug",        "ThumbsUp" },
            { "love",        "HandGun" },
            { "heart",       "HandGun" },
            { "cool",        "RockNRoll" },
            { "rock",        "RockNRoll" },
            { "concentrate", "Fingerpoint" },
        };

        /// <summary>intent → 推奨 gesture 名。preset 不在なら null。</summary>
        public static string GetRecommendedGesture(string intent)
        {
            if (string.IsNullOrEmpty(intent)) return null;
            return Map.TryGetValue(intent.ToLowerInvariant(), out var g) ? g : null;
        }

        /// <summary>サポート intent 名一覧 (小文字)。</summary>
        public static IEnumerable<string> SupportedIntents => Map.Keys;
    }
}
