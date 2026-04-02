using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public enum LayerBlendMode { Override, Additive }

    public class PoseLayer
    {
        public string Name;
        public LayerBlendMode BlendMode;
        public float Weight = 1f;
        public bool IsVisible = true;
        public bool IsSolo;
        public bool IsMute;
        public HashSet<int> BoneGroupMask; // null = all groups
        public List<PoseKeyframe> Keyframes = new List<PoseKeyframe>();
        public Dictionary<HumanBodyBones, AnimationCurve> BoneCurves
            = new Dictionary<HumanBodyBones, AnimationCurve>();
        public AnimationCurve DefaultCurve; // null = inherit global

        public PoseLayer(string name = "Base")
        {
            Name = name;
        }

        public AnimationCurve GetEffectiveCurve(HumanBodyBones bone, AnimationCurve globalDefault)
        {
            if (BoneCurves.TryGetValue(bone, out var c)) return c;
            return DefaultCurve ?? globalDefault;
        }

        public bool IsBoneInMask(HumanBodyBones bone)
        {
            if (BoneGroupMask == null) return true;
            for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
            {
                if (!BoneGroupMask.Contains(g)) continue;
                foreach (var b in BonePoseEditorState.BoneGroups[g].bones)
                {
                    if (b == bone) return true;
                }
            }
            return false;
        }
    }

    public static class LayerEvaluator
    {
        public static Dictionary<HumanBodyBones, Quaternion> EvaluateAllLayers(
            List<PoseLayer> layers, float time,
            Dictionary<HumanBodyBones, Quaternion> originalRotations,
            AnimationCurve globalDefaultCurve)
        {
            var result = new Dictionary<HumanBodyBones, Quaternion>(originalRotations);

            // Determine if any layer has Solo enabled
            bool anySolo = false;
            foreach (var layer in layers)
            {
                if (layer.IsSolo && layer.IsVisible && !layer.IsMute)
                {
                    anySolo = true;
                    break;
                }
            }

            foreach (var layer in layers)
            {
                if (!layer.IsVisible || layer.IsMute) continue;
                if (anySolo && !layer.IsSolo) continue;
                if (layer.Keyframes.Count == 0) continue;
                if (layer.Weight <= 0.001f) continue;

                var layerPose = EvaluateLayer(layer, time, originalRotations, globalDefaultCurve);

                foreach (var kvp in layerPose)
                {
                    if (!layer.IsBoneInMask(kvp.Key)) continue;
                    if (!result.ContainsKey(kvp.Key)) continue;

                    switch (layer.BlendMode)
                    {
                        case LayerBlendMode.Override:
                            result[kvp.Key] = Quaternion.Slerp(
                                result[kvp.Key], kvp.Value, layer.Weight);
                            break;

                        case LayerBlendMode.Additive:
                        {
                            if (!originalRotations.TryGetValue(kvp.Key, out var orig))
                                orig = Quaternion.identity;
                            // Compute delta from original
                            var delta = kvp.Value * Quaternion.Inverse(orig);
                            var blendedDelta = Quaternion.Slerp(
                                Quaternion.identity, delta, layer.Weight);
                            result[kvp.Key] = blendedDelta * result[kvp.Key];
                            break;
                        }
                    }
                }
            }

            return result;
        }

        private static Dictionary<HumanBodyBones, Quaternion> EvaluateLayer(
            PoseLayer layer, float time,
            Dictionary<HumanBodyBones, Quaternion> originalRotations,
            AnimationCurve globalDefaultCurve)
        {
            var result = new Dictionary<HumanBodyBones, Quaternion>();
            var keyframes = layer.Keyframes;

            if (keyframes.Count == 0) return result;

            PoseKeyframe before = null, after = null;
            for (int i = 0; i < keyframes.Count; i++)
            {
                if (keyframes[i].Time <= time) before = keyframes[i];
                if (keyframes[i].Time >= time && after == null) after = keyframes[i];
            }

            if (before == null) before = keyframes[0];
            if (after == null) after = keyframes[keyframes.Count - 1];

            float raw = (before == after) ? 0f
                : Mathf.InverseLerp(before.Time, after.Time, time);

            var bones = BonePoseEditorState.BoneTransforms;

            foreach (var bone in originalRotations.Keys)
            {
                bool hasBefore = before.BoneRotations.TryGetValue(bone, out var rotB);
                bool hasAfter = after.BoneRotations.TryGetValue(bone, out var rotA);

                if (hasBefore && hasAfter)
                {
                    // Convert both to Local space if they have space overrides
                    SpaceOverride spaceB = null, spaceA = null;
                    before.BoneSpaces?.TryGetValue(bone, out spaceB);
                    after.BoneSpaces?.TryGetValue(bone, out spaceA);

                    var localB = BonePoseEditorSpaceSwitching.ConvertToLocal(
                        rotB, spaceB, bone, bones);
                    var localA = BonePoseEditorSpaceSwitching.ConvertToLocal(
                        rotA, spaceA, bone, bones);

                    var curve = layer.GetEffectiveCurve(bone, globalDefaultCurve);
                    float t = curve.Evaluate(raw);
                    result[bone] = Quaternion.Slerp(localB, localA, t);
                }
                else if (hasBefore)
                {
                    SpaceOverride spaceB = null;
                    before.BoneSpaces?.TryGetValue(bone, out spaceB);
                    result[bone] = BonePoseEditorSpaceSwitching.ConvertToLocal(
                        rotB, spaceB, bone, bones);
                }
                else if (hasAfter)
                {
                    SpaceOverride spaceA = null;
                    after.BoneSpaces?.TryGetValue(bone, out spaceA);
                    result[bone] = BonePoseEditorSpaceSwitching.ConvertToLocal(
                        rotA, spaceA, bone, bones);
                }
            }

            return result;
        }
    }
}
