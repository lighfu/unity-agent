using System;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile
{
    // BlendShape 名から (顔パーツ category, 感情/状態 tags) を推定する。
    // 既存 BlendShapeTools.SynonymGroups の語彙を seed にして、日英混在 / 命名規則差異を吸収する。
    public static class BlendShapeCategorizer
    {
        // 顔パーツ判定の優先順位 (上から順にチェック、最初にマッチしたものを採用)。
        // 競合語彙 (例: "eye_brow") は Brow を先に判定するため Brow を Eye より先に置く。
        private static readonly (FaceCategory cat, string[] keywords)[] CategoryKeywords =
        {
            (FaceCategory.Brow, new[] { "brow", "eyebrow", "mayu", "眉", "まゆ" }),
            (FaceCategory.Cheek, new[] { "cheek", "blush", "頬", "ほお", "ほっぺ" }),
            (FaceCategory.Tongue, new[] { "tongue", "bero", "べろ", "舌" }),
            (FaceCategory.Eye, new[] {
                "eyelid", "eye", "pupil", "iris", "gaze", "blink", "wink",
                "まぶた", "瞳", "目", "黒目", "白目", "瞼",
            }),
            (FaceCategory.Mouth, new[] {
                "mouth", "lip", "tooth", "teeth", "kiss",
                "くち", "唇", "口", "歯",
                "vrc.v_", "viseme",
            }),
        };

        // 感情・状態タグ。category と直交して付与され、PresetMatcher のキーになる。
        private static readonly (string tag, string[] keywords)[] TagKeywords =
        {
            ("smile", new[] { "smile", "joy", "happy", "fun", "cheerful", "笑", "にこ", "にっこり" }),
            ("angry", new[] { "angry", "anger", "irritated", "mad", "怒", "おこ", "イラ" }),
            ("surprised", new[] { "surprised", "surprise", "astonished", "shock", "驚", "びっくり", "ガーン" }),
            ("sad", new[] { "sad", "sorrow", "unhappy", "down", "悲", "しょんぼり" }),
            ("cry", new[] { "cry", "tear", "crying", "weep", "泣", "なき" }),
            ("wink", new[] { "wink", "ウインク", "ウィンク" }),
            ("sleep", new[] { "sleep", "sleeping", "drowsy", "眠", "寝", "ねむ" }),
            ("kiss", new[] { "kiss", "chu", "キス", "ちゅ", "ちゅー" }),
            ("shy", new[] { "shy", "embarrass", "blush", "照", "てれ", "ぽっ" }),
            ("close", new[] { "close", "closed", "shut", "閉", "とじ" }),
            ("open", new[] { "open", "opened", "wide", "開", "あけ", "ひらき" }),
            ("narrow", new[] { "narrow", "squint", "half", "jito", "じと", "細", "ジト" }),
            ("up", new[] { "_up", "up_", "raise", "高", "上" }),
            ("down", new[] { "_down", "down_", "lower", "下", "落" }),
            ("joy", new[] { "joy" }),
            ("happy", new[] { "happy" }),
            ("fun", new[] { "fun" }),
            // 視線
            ("look", new[] { "look", "視線", "gaze" }),
            // 口形 (viseme 系) は Mouth カテゴリに入るが個別タグも付与
            ("aa", new[] { "vrc.v_aa", "viseme_aa" }),
            ("ee", new[] { "vrc.v_ee", "viseme_ee" }),
            ("oh", new[] { "vrc.v_oh", "viseme_oh" }),
            ("ih", new[] { "vrc.v_ih", "viseme_ih" }),
            ("ou", new[] { "vrc.v_ou", "viseme_ou" }),
        };

        public static (FaceCategory category, List<string> tags) Categorize(string shapeName)
        {
            if (string.IsNullOrEmpty(shapeName))
                return (FaceCategory.Other, new List<string>());

            string lower = shapeName.ToLowerInvariant();

            FaceCategory category = FaceCategory.Other;
            foreach (var (cat, keywords) in CategoryKeywords)
            {
                if (ContainsAny(lower, shapeName, keywords))
                {
                    category = cat;
                    break;
                }
            }

            var tags = new List<string>();
            foreach (var (tag, keywords) in TagKeywords)
            {
                if (ContainsAny(lower, shapeName, keywords) && !tags.Contains(tag))
                    tags.Add(tag);
            }

            return (category, tags);
        }

        private static bool ContainsAny(string lower, string original, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                // 日本語 / Unicode キーワードは元の文字列に対してチェック。
                // ASCII 系は小文字化済みでチェック。
                if (IsAscii(kw))
                {
                    if (lower.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
                }
                else
                {
                    if (original.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
                }
            }
            return false;
        }

        private static bool IsAscii(string s)
        {
            foreach (var ch in s)
                if (ch > 127) return false;
            return true;
        }

        // 感情キーワード ("smile") からプリセット名 (FacePreset) を解決する。
        // intent 引数の正規化に使用される。
        public static FacePreset? ResolvePreset(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return null;
            string lower = keyword.ToLowerInvariant();

            var map = new (FacePreset preset, string[] aliases)[]
            {
                (FacePreset.Smile, new[] { "smile", "joy", "happy", "fun", "cheerful", "laugh", "笑", "にこ" }),
                (FacePreset.Angry, new[] { "angry", "anger", "irritated", "mad", "rage", "怒" }),
                (FacePreset.Surprised, new[] { "surprised", "surprise", "astonished", "shock", "驚", "びっくり" }),
                (FacePreset.Sad, new[] { "sad", "sorrow", "down", "悲", "しょんぼり" }),
                (FacePreset.Cry, new[] { "cry", "crying", "tear", "weep", "泣" }),
                (FacePreset.Wink, new[] { "wink", "ウインク", "ウィンク" }),
                (FacePreset.Sleep, new[] { "sleep", "sleeping", "drowsy", "眠", "寝" }),
                (FacePreset.Kiss, new[] { "kiss", "chu", "キス", "ちゅ" }),
                (FacePreset.Shy, new[] { "shy", "embarrass", "blush", "照", "てれ" }),
            };

            foreach (var (preset, aliases) in map)
            {
                foreach (var alias in aliases)
                {
                    if (IsAscii(alias))
                    {
                        if (lower.IndexOf(alias, StringComparison.Ordinal) >= 0) return preset;
                    }
                    else
                    {
                        if (keyword.IndexOf(alias, StringComparison.Ordinal) >= 0) return preset;
                    }
                }
            }
            return null;
        }
    }
}
