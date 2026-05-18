// Editor/Tools/FaceEmoPlanC/Conventions/HandPoseDisplay.cs
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Conventions
{
    /// <summary>
    /// HandGesture / Hand qualifier の表示文字列 (絵文字 + 英名 + 日本語名)。
    /// AskUser 系のラベル生成で使用。
    /// </summary>
    public static class HandPoseDisplay
    {
        // FaceEmoAPI.ParseGesture が受け付ける PascalCase → 表示
        private static readonly Dictionary<string, (string emoji, string ja)> GestureMap =
            new Dictionary<string, (string, string)>
        {
            { "Neutral",     ("😐", "ニュートラル") },
            { "Fist",        ("✊", "握り") },
            { "HandOpen",    ("✋", "パー") },
            { "Fingerpoint", ("☝️", "指差し") },
            { "Victory",     ("✌️", "ピース") },
            { "RockNRoll",   ("🤘", "ロック") },
            { "HandGun",     ("🤙", "ハンドガン") },
            { "ThumbsUp",    ("👍", "グッド") },
        };

        private static readonly Dictionary<string, string> HandJa = new Dictionary<string, string>
        {
            { "Either",  "どちらの手でも (Either)" },
            { "Left",    "左手のみ (Left)" },
            { "Right",   "右手のみ (Right)" },
            { "Both",    "両手 (Both)" },
            { "OneSide", "片手だけ (OneSide)" },
        };

        /// <summary>例: "✋ HandOpen / パー"</summary>
        public static string FormatGesture(string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return gesture ?? "";
            return GestureMap.TryGetValue(gesture, out var v)
                ? $"{v.emoji} {gesture} / {v.ja}"
                : gesture;
        }

        /// <summary>例: "どちらの手でも (Either)"</summary>
        public static string FormatHand(string hand)
        {
            if (string.IsNullOrEmpty(hand)) return hand ?? "";
            return HandJa.TryGetValue(hand, out var v) ? v : hand;
        }

        /// <summary>絵文字単体 ("✋" など)。</summary>
        public static string GetEmoji(string gesture)
        {
            return GestureMap.TryGetValue(gesture ?? "", out var v) ? v.emoji : "";
        }
    }
}
