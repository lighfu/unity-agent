using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorClipImporter
    {
        public static PoseLayer ImportClip(AnimationClip clip, Animator animator)
        {
            if (clip == null || animator == null || !animator.isHuman)
                return null;

            var layer = new PoseLayer(clip.name)
            {
                BlendMode = LayerBlendMode.Override
            };

            // Save current bone rotations
            var savedRotations = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                    savedRotations[bone] = t.localRotation;
            }

            var savedHipsPos = Vector3.zero;
            var hipsT = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hipsT != null)
                savedHipsPos = hipsT.localPosition;

            // Get unique keyframe times from muscle curves
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var keyframeTimes = new SortedSet<float>();

            foreach (var binding in bindings)
            {
                if (binding.type != typeof(Animator)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                foreach (var key in curve.keys)
                    keyframeTimes.Add(RoundTime(key.time));
            }

            if (keyframeTimes.Count == 0)
            {
                // Fallback: sample at regular intervals
                float duration = clip.length;
                if (duration <= 0) duration = 1f;
                int sampleCount = Mathf.Max(2, Mathf.CeilToInt(duration * 30f));
                for (int i = 0; i <= sampleCount; i++)
                    keyframeTimes.Add(RoundTime(i * duration / sampleCount));
            }

            // Use HumanPoseHandler to sample at each keyframe time
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();

            try
            {
                foreach (float time in keyframeTimes)
                {
                    // Sample the clip at this time
                    clip.SampleAnimation(animator.gameObject, time);

                    var kf = new PoseKeyframe { Time = time };

                    foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (bone == HumanBodyBones.LastBone) continue;
                        var t = animator.GetBoneTransform(bone);
                        if (t == null) continue;

                        kf.BoneRotations[bone] = t.localRotation;
                        kf.BoneWorldPositions[bone] = t.position;
                    }

                    layer.Keyframes.Add(kf);
                }
            }
            finally
            {
                // Restore original bone rotations
                foreach (var kvp in savedRotations)
                {
                    var t = animator.GetBoneTransform(kvp.Key);
                    if (t != null)
                        t.localRotation = kvp.Value;
                }
                if (hipsT != null)
                    hipsT.localPosition = savedHipsPos;

                handler.Dispose();
            }

            layer.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));

            // Offer keyframe reduction after import
            if (layer.Keyframes.Count > 2
                && EditorUtility.DisplayDialog(M("キーフレーム削減"),
                    string.Format(M("{0} キーフレームをインポートしました。\n冗長なキーフレームを削減しますか？"), layer.Keyframes.Count),
                    M("削減"), M("全て保持")))
            {
                int removed = BonePoseEditorKeyReduction.ReduceKeyframes(
                    layer.Keyframes,
                    BonePoseEditorState.OriginalRotations,
                    BonePoseEditorState.KeyReductionTolerance);
                if (removed > 0)
                    Debug.Log($"[BonePoseEditor] Import reduction: removed {removed} keyframes, " +
                              $"{layer.Keyframes.Count} remaining");
            }

            return layer;
        }

        public static PoseLayer ImportClipFromDialog(Animator animator)
        {
            string path = EditorUtility.OpenFilePanel(
                M("AnimationClipをインポート"), "Assets", "anim");
            if (string.IsNullOrEmpty(path)) return null;

            // Convert absolute path to project-relative
            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                Debug.LogWarning("[BonePoseEditor] Failed to load AnimationClip: " + path);
                return null;
            }

            return ImportClip(clip, animator);
        }

        private static float RoundTime(float time)
        {
            return Mathf.Round(time * 1000f) / 1000f;
        }
    }
}
