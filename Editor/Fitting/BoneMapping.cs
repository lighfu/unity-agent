using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Bone mapping between outfit and avatar skeletons.
    /// </summary>
    internal static class FittingBoneMap
    {
        public struct BoneMapEntry
        {
            public Transform outfitBone;
            public Transform avatarBone;
            public float confidence;
            public string method;
        }

        private static readonly string[] StripPrefixes = {
            "Armature_", "armature_", "mixamorig:", "Mixamorig:",
            "Bip01_", "Bip001_", "J_Bip_", "J_Sec_", "J_Adj_",
            "DEF-", "MCH-", "ORG-",
        };

        private static readonly string[] StripSuffixes = {
            "_end", "_End", ".001", ".002", ".003",
        };

        // Side suffixes are normalized (not stripped) to preserve L/R distinction.
        // Without this, "UpperArm.L" and "UpperArm.R" both collapse to "upperarm"
        // causing cross-side matching (left bone → right bone position).
        private static readonly (string suffix, string normalized)[] SideSuffixes = {
            (".L", "_l"), (".R", "_r"),
            ("_L", "_l"), ("_R", "_r"),
            ("_left", "_l"), ("_right", "_r"),
            ("_Left", "_l"), ("_Right", "_r"),
        };

        public static List<BoneMapEntry> BuildBoneMapping(Transform outfitRoot, Transform avatarRoot)
        {
            var outfitBones = CollectBones(outfitRoot);
            var avatarBones = CollectAvatarBones(avatarRoot);
            var avatarBoneDict = new Dictionary<string, Transform>();
            var avatarBoneDictLower = new Dictionary<string, Transform>();
            var avatarBoneStripped = new Dictionary<string, Transform>();

            foreach (var b in avatarBones)
            {
                if (!avatarBoneDict.ContainsKey(b.name))
                    avatarBoneDict[b.name] = b;
                string lower = b.name.ToLowerInvariant();
                if (!avatarBoneDictLower.ContainsKey(lower))
                    avatarBoneDictLower[lower] = b;
                string stripped = StripBoneName(b.name);
                if (stripped.Length > 0 && !avatarBoneStripped.ContainsKey(stripped))
                    avatarBoneStripped[stripped] = b;
            }

            var humanoidMap = BuildHumanoidBridge(outfitRoot, avatarRoot);

            var result = new List<BoneMapEntry>();
            var mapped = new Dictionary<Transform, Transform>();

            foreach (var outfitBone in outfitBones)
            {
                var entry = new BoneMapEntry { outfitBone = outfitBone };

                if (avatarBoneDict.TryGetValue(outfitBone.name, out var exactMatch))
                {
                    entry.avatarBone = exactMatch;
                    entry.confidence = 1.0f;
                    entry.method = "Exact";
                }
                else if (avatarBoneDictLower.TryGetValue(outfitBone.name.ToLowerInvariant(), out var caseMatch))
                {
                    entry.avatarBone = caseMatch;
                    entry.confidence = 0.95f;
                    entry.method = "CaseInsensitive";
                }
                else
                {
                    string stripped = StripBoneName(outfitBone.name);
                    if (stripped.Length > 0 && avatarBoneStripped.TryGetValue(stripped, out var strippedMatch))
                    {
                        entry.avatarBone = strippedMatch;
                        entry.confidence = 0.85f;
                        entry.method = "StrippedName";
                    }
                    else if (humanoidMap.TryGetValue(outfitBone, out var humanoidMatch))
                    {
                        entry.avatarBone = humanoidMatch;
                        entry.confidence = 0.9f;
                        entry.method = "Humanoid";
                    }
                }

                if (entry.avatarBone != null)
                    mapped[entry.outfitBone] = entry.avatarBone;
                result.Add(entry);
            }

            // Hierarchy inference pass
            for (int i = 0; i < result.Count; i++)
            {
                var entry = result[i];
                if (entry.avatarBone != null) continue;

                var parent = entry.outfitBone.parent;
                if (parent != null && mapped.TryGetValue(parent, out var avatarParent))
                {
                    Transform bestChild = null;
                    float bestSim = 0f;
                    foreach (Transform child in avatarParent)
                    {
                        float sim = ComputeNameSimilarity(entry.outfitBone.name, child.name);
                        if (sim > bestSim)
                        {
                            bestSim = sim;
                            bestChild = child;
                        }
                    }

                    if (bestChild != null && bestSim > 0.3f)
                    {
                        entry.avatarBone = bestChild;
                        entry.confidence = 0.7f * bestSim;
                        entry.method = "HierarchyInference";
                        mapped[entry.outfitBone] = bestChild;
                        result[i] = entry;
                    }
                }
            }

            // Fallback: map to nearest mapped ancestor
            for (int i = 0; i < result.Count; i++)
            {
                var entry = result[i];
                if (entry.avatarBone != null) continue;

                var ancestor = entry.outfitBone.parent;
                while (ancestor != null)
                {
                    if (mapped.TryGetValue(ancestor, out var avatarAncestor))
                    {
                        entry.avatarBone = avatarAncestor;
                        entry.confidence = 0.3f;
                        entry.method = "AncestorFallback";
                        mapped[entry.outfitBone] = avatarAncestor;
                        result[i] = entry;
                        break;
                    }
                    ancestor = ancestor.parent;
                }
            }

            return result;
        }

        public static Dictionary<Transform, Transform> BuildHumanoidBridge(Transform outfitRoot, Transform avatarRoot)
        {
            var result = new Dictionary<Transform, Transform>();

            var outfitAnimator = outfitRoot.GetComponent<Animator>();
            var avatarAnimator = avatarRoot.GetComponent<Animator>();
            if (outfitAnimator == null || !outfitAnimator.isHuman) return result;
            if (avatarAnimator == null || !avatarAnimator.isHuman) return result;

            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var outfitBone = outfitAnimator.GetBoneTransform(bone);
                var avatarBone = avatarAnimator.GetBoneTransform(bone);
                if (outfitBone != null && avatarBone != null)
                    result[outfitBone] = avatarBone;
            }

            return result;
        }

        public static List<Transform> CollectBones(Transform root)
        {
            var bones = new List<Transform>();
            CollectBonesRecursive(root, bones);
            return bones;
        }

        public static List<Transform> CollectAvatarBones(Transform avatarRoot)
        {
            var armature = FindArmature(avatarRoot);
            if (armature != null)
            {
                var bones = new List<Transform> { avatarRoot };
                CollectBonesRecursive(armature, bones);
                return bones;
            }
            return CollectBones(avatarRoot);
        }

        private static void CollectBonesRecursive(Transform t, List<Transform> list)
        {
            list.Add(t);
            foreach (Transform child in t)
                CollectBonesRecursive(child, list);
        }

        public static Transform FindArmature(Transform root)
        {
            foreach (Transform child in root)
            {
                string lower = child.name.ToLowerInvariant();
                if (lower == "armature" || lower.Contains("armature"))
                    return child;
            }
            return null;
        }

        public static float MeasureArmatureHeight(Transform armature)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            MeasureHeightRecursive(armature, ref minY, ref maxY);
            return maxY - minY;
        }

        private static void MeasureHeightRecursive(Transform t, ref float minY, ref float maxY)
        {
            float y = t.position.y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            foreach (Transform child in t)
                MeasureHeightRecursive(child, ref minY, ref maxY);
        }

        public static string StripBoneName(string name)
        {
            string result = name;
            foreach (var prefix in StripPrefixes)
            {
                if (result.StartsWith(prefix))
                {
                    result = result.Substring(prefix.Length);
                    break;
                }
            }

            // Extract and normalize side indicator (L/R) instead of discarding it.
            // e.g. "UpperArm.L" → "upperarm_l", "UpperArm_R" → "upperarm_r"
            string sideTag = "";
            foreach (var (suffix, normalized) in SideSuffixes)
            {
                if (result.EndsWith(suffix))
                {
                    sideTag = normalized;
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }

            foreach (var suffix in StripSuffixes)
            {
                if (result.EndsWith(suffix))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }
            return result.ToLowerInvariant() + sideTag;
        }

        public static float ComputeNameSimilarity(string a, string b)
        {
            string sa = StripBoneName(a);
            string sb = StripBoneName(b);
            if (sa == sb && sa.Length > 0) return 1f;
            if (sa.Length == 0 || sb.Length == 0) return 0f;

            int lcs = LongestCommonSubstring(sa, sb);
            return (float)lcs / Mathf.Max(sa.Length, sb.Length);
        }

        private static int LongestCommonSubstring(string a, string b)
        {
            int maxLen = 0;
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                    if (dp[i, j] > maxLen) maxLen = dp[i, j];
                }
            }
            return maxLen;
        }

        public static string GetShortPath(Transform t, Transform root)
        {
            if (t == root) return t.name;
            var parts = new List<string>();
            var current = t;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            string path = string.Join("/", parts);
            return path.Length > 33 ? "..." + path.Substring(path.Length - 30) : path;
        }

        public static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null) { depth++; t = t.parent; }
            return depth;
        }
    }
}
