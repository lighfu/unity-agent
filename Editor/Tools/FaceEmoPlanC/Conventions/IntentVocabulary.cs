// Editor/Tools/FaceEmoPlanC/Conventions/IntentVocabulary.cs
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Conventions
{
    /// <summary>
    /// 発話文からのキーワード抽出辞書。
    /// Orchestrator の AskUser スキップ判定 (Sec 2 of spec) に使用。
    /// </summary>
    public static class IntentVocabulary
    {
        public enum TopMode { Auto, Interactive, Unspecified }

        private static readonly string[] AutoKeywords =
            { "任せて", "おまかせ", "適当", "quick", "一発", "ぱっと" };

        private static readonly string[] InteractiveKeywords =
            { "編集", "調整", "詳しく", "ちゃんと", "カスタム", "手で" };

        // 日本語 → HandGesture PascalCase
        private static readonly Dictionary<string, string> HandPoseJaToEn =
            new Dictionary<string, string>
        {
            { "パー",     "HandOpen" },
            { "ぱー",     "HandOpen" },
            { "グー",     "Fist" },
            { "ぐー",     "Fist" },
            { "握り",     "Fist" },
            { "ピース",   "Victory" },
            { "ぴーす",   "Victory" },
            { "グッド",   "ThumbsUp" },
            { "ぐっど",   "ThumbsUp" },
            { "指差し",   "Fingerpoint" },
            { "さしゆび", "Fingerpoint" },
            { "ロック",   "RockNRoll" },
            { "ろっく",   "RockNRoll" },
            { "ハンドガン", "HandGun" },
            { "中立",     "Neutral" },
            { "ニュートラル", "Neutral" },
        };

        // 日本語 → Hand qualifier
        private static readonly Dictionary<string, string> HandQualifierJaToEn =
            new Dictionary<string, string>
        {
            { "左手で", "Left" },
            { "ひだりてで", "Left" },
            { "右手で", "Right" },
            { "みぎてで", "Right" },
            { "両手で", "Both" },
            { "りょうてで", "Both" },
            { "片手で", "OneSide" },
            { "かたてで", "OneSide" },
        };

        /// <summary>top-level モード推定。"任せて" 等で Auto、"編集" 等で Interactive。</summary>
        public static TopMode DetectTopMode(string utterance)
        {
            if (string.IsNullOrEmpty(utterance)) return TopMode.Unspecified;
            string lower = utterance.ToLowerInvariant();
            if (AutoKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                return TopMode.Auto;
            if (InteractiveKeywords.Any(k => lower.Contains(k.ToLowerInvariant())))
                return TopMode.Interactive;
            return TopMode.Unspecified;
        }

        /// <summary>発話中の HandGesture を検出 (英名 / 日本語名 / FaceEmoAPI.ParseGesture 経由)。</summary>
        public static string DetectHandPose(string utterance)
        {
            if (string.IsNullOrEmpty(utterance)) return null;
            // 日本語マッチ優先
            foreach (var kv in HandPoseJaToEn)
                if (utterance.Contains(kv.Key)) return kv.Value;
            // 英名そのまま
            string[] englishNames = { "Neutral", "Fist", "HandOpen", "Fingerpoint",
                                       "Victory", "RockNRoll", "HandGun", "ThumbsUp" };
            foreach (var en in englishNames)
                if (utterance.IndexOf(en, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return en;
            return null;
        }

        /// <summary>発話中の Hand qualifier を検出。デフォルト推定値は呼出側で Either に。</summary>
        public static string DetectHandQualifier(string utterance)
        {
            if (string.IsNullOrEmpty(utterance)) return null;
            foreach (var kv in HandQualifierJaToEn)
                if (utterance.Contains(kv.Key)) return kv.Value;
            // 英名直接
            string[] englishHands = { "Either", "Left", "Right", "Both", "OneSide" };
            foreach (var en in englishHands)
                if (utterance.IndexOf(en, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return en;
            return null;
        }
    }
}
