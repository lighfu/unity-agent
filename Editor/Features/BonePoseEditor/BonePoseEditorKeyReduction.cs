using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorKeyReduction
    {
        /// <summary>
        /// Remove redundant keyframes where interpolation between neighbors
        /// produces results within the angle tolerance for all bones.
        /// First and last keyframes are always preserved.
        /// </summary>
        public static int ReduceKeyframes(List<PoseKeyframe> keyframes,
            Dictionary<HumanBodyBones, Quaternion> originalRotations,
            float angleTolerance = 0.5f)
        {
            if (keyframes == null || keyframes.Count <= 2)
                return 0;

            int removed = 0;
            bool changed = true;

            // Iteratively remove the least significant keyframes
            while (changed)
            {
                changed = false;

                // Build list of removable candidates (skip first and last)
                var candidates = new List<(int index, float maxAngle)>();

                for (int i = 1; i < keyframes.Count - 1; i++)
                {
                    float maxAngle = ComputeMaxInterpolationError(
                        keyframes[i - 1], keyframes[i], keyframes[i + 1],
                        originalRotations);

                    if (maxAngle <= angleTolerance)
                        candidates.Add((i, maxAngle));
                }

                // Sort by smallest error first (most redundant)
                candidates.Sort((a, b) => a.maxAngle.CompareTo(b.maxAngle));

                // Remove one at a time and re-evaluate
                // (removing from end to preserve indices)
                if (candidates.Count > 0)
                {
                    keyframes.RemoveAt(candidates[0].index);
                    removed++;
                    changed = true;
                }
            }

            return removed;
        }

        /// <summary>
        /// Compute the maximum angle error across all bones if keyframe kf[i]
        /// were removed and its neighbors interpolated directly.
        /// </summary>
        private static float ComputeMaxInterpolationError(
            PoseKeyframe before, PoseKeyframe middle, PoseKeyframe after,
            Dictionary<HumanBodyBones, Quaternion> originalRotations)
        {
            float maxAngle = 0f;

            // Compute interpolation ratio
            float totalSpan = after.Time - before.Time;
            float t = totalSpan > 0.0001f
                ? (middle.Time - before.Time) / totalSpan
                : 0.5f;

            foreach (var bone in originalRotations.Keys)
            {
                bool hasB = before.BoneRotations.TryGetValue(bone, out var rotB);
                bool hasM = middle.BoneRotations.TryGetValue(bone, out var rotM);
                bool hasA = after.BoneRotations.TryGetValue(bone, out var rotA);

                // Only check bones that exist in the middle keyframe
                if (!hasM) continue;

                // If missing from before/after, use original rotation
                if (!hasB) rotB = originalRotations.TryGetValue(bone, out var ob)
                    ? ob : Quaternion.identity;
                if (!hasA) rotA = originalRotations.TryGetValue(bone, out var oa)
                    ? oa : Quaternion.identity;

                // Interpolated value if middle were removed
                var interpolated = Quaternion.Slerp(rotB, rotA, t);

                // Angle difference between interpolated and actual
                float angle = Quaternion.Angle(interpolated, rotM);
                if (angle > maxAngle)
                    maxAngle = angle;
            }

            return maxAngle;
        }
    }
}
