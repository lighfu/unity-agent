using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class PoseKeyframe
    {
        public float Time;
        public Dictionary<HumanBodyBones, Quaternion> BoneRotations
            = new Dictionary<HumanBodyBones, Quaternion>();
        public Dictionary<HumanBodyBones, Vector3> BoneWorldPositions
            = new Dictionary<HumanBodyBones, Vector3>();
        public Dictionary<HumanBodyBones, SpaceOverride> BoneSpaces
            = new Dictionary<HumanBodyBones, SpaceOverride>();
    }

    public enum CurvePreset { Linear, Smooth, EaseIn, EaseOut, Constant }
    public enum TangentMode { Auto, Unified, Broken }

    public static class BonePoseEditorState
    {
        // Mode
        public static bool IsActive;
        public static Animator TargetAnimator;

        // Bone cache
        public static Dictionary<HumanBodyBones, Transform> BoneTransforms
            = new Dictionary<HumanBodyBones, Transform>();

        // Selection
        public static HumanBodyBones SelectedBone = (HumanBodyBones)(-1);
        public static HumanBodyBones HoveredBone = (HumanBodyBones)(-1);

        // Settings
        public static bool SymmetryEnabled = true;
        public static bool UseWorldRotation;
        public static bool AutoKeyEnabled;
        public static bool OnionSkinEnabled;

        // ─── Graph Editor ───

        public static bool ShowGraphEditor;
        public static float GraphZoom = 1f;
        public static Vector2 GraphPanOffset = Vector2.zero;
        public static int SelectedCurveKeyIndex = -1;
        public static TangentMode CurrentTangentMode = TangentMode.Auto;

        // ─── Curves (AnimationCurve graph) ───

        public static AnimationCurve DefaultCurve = CreatePreset(CurvePreset.Smooth);

        // ─── Layers (Phase 2) ───

        public static List<PoseLayer> Layers = new List<PoseLayer>();
        public static int ActiveLayerIndex;

        public static PoseLayer ActiveLayer
        {
            get
            {
                if (Layers == null || Layers.Count == 0) return null;
                if (ActiveLayerIndex < 0 || ActiveLayerIndex >= Layers.Count)
                    ActiveLayerIndex = 0;
                return Layers[ActiveLayerIndex];
            }
        }

        // Redirect Keyframes / BoneCurves to active layer
        private static readonly List<PoseKeyframe> _fallbackKeyframes = new List<PoseKeyframe>();
        private static readonly Dictionary<HumanBodyBones, AnimationCurve> _fallbackBoneCurves
            = new Dictionary<HumanBodyBones, AnimationCurve>();

        public static List<PoseKeyframe> Keyframes
            => ActiveLayer?.Keyframes ?? _fallbackKeyframes;

        public static Dictionary<HumanBodyBones, AnimationCurve> BoneCurves
            => ActiveLayer?.BoneCurves ?? _fallbackBoneCurves;

        // ─── Pose Library (Phase 3) ───

        public static PoseLibraryAsset CurrentLibrary;
        public static float PoseBlendWeight = 1f;

        // ─── Root Motion ───

        public static bool BakeRootPosition;
        public static bool BakeRootRotation;
        public static float RootHeightOffset;

        // ─── Keyframe Reduction ───

        public static float KeyReductionTolerance = 0.5f;

        // ─── IK (Phase 4) ───

        public static List<IKTarget> IKTargets;
        public static bool IKEnabled;

        // ─── Per-bone Curve Accessors ───

        public static AnimationCurve GetBoneCurve(HumanBodyBones bone)
        {
            return BoneCurves.TryGetValue(bone, out var c) ? c : DefaultCurve;
        }

        public static void SetBoneCurve(HumanBodyBones bone, AnimationCurve curve)
        {
            BoneCurves[bone] = new AnimationCurve(curve.keys);
        }

        public static void ClearBoneCurve(HumanBodyBones bone)
        {
            BoneCurves.Remove(bone);
        }

        public static AnimationCurve CreatePreset(CurvePreset preset)
        {
            switch (preset)
            {
                case CurvePreset.Linear:
                    return AnimationCurve.Linear(0, 0, 1, 1);
                case CurvePreset.EaseIn:
                    return new AnimationCurve(
                        new Keyframe(0, 0, 0, 0),
                        new Keyframe(1, 1, 2, 2));
                case CurvePreset.EaseOut:
                    return new AnimationCurve(
                        new Keyframe(0, 0, 2, 2),
                        new Keyframe(1, 1, 0, 0));
                case CurvePreset.Constant:
                    return new AnimationCurve(
                        new Keyframe(0, 0, 0, 0),
                        new Keyframe(1, 0, 0, 0));
                default: // Smooth
                    return AnimationCurve.EaseInOut(0, 0, 1, 1);
            }
        }

        // Reset / Copy (per-bone)
        public static Dictionary<HumanBodyBones, Quaternion> OriginalRotations
            = new Dictionary<HumanBodyBones, Quaternion>();
        public static Quaternion? CopiedRotation;

        // Pose clipboard (whole-keyframe copy)
        public static PoseKeyframe ClipboardKeyframe;

        // ─── Animation Timeline ───

        public static float CurrentTime;
        public static float AnimationLength = 2f;
        public static int FrameRate = 30;
        public static bool IsPlaying;
        public static bool LoopPlayback = true;

        // ─── Activate / Deactivate ───

        public static void Activate(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;

            TargetAnimator = animator;
            BoneTransforms.Clear();
            OriginalRotations.Clear();

            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    BoneTransforms[bone] = t;
                    OriginalRotations[bone] = t.localRotation;
                }
            }

            SelectedBone = (HumanBodyBones)(-1);
            HoveredBone = (HumanBodyBones)(-1);
            CopiedRotation = null;
            ClipboardKeyframe = null;
            CurrentTime = 0f;
            IsPlaying = false;
            AutoKeyEnabled = false;
            ShowGraphEditor = false;
            GraphZoom = 1f;
            GraphPanOffset = Vector2.zero;
            SelectedCurveKeyIndex = -1;
            CurrentTangentMode = TangentMode.Auto;

            // Root motion defaults
            BakeRootPosition = false;
            BakeRootRotation = false;
            RootHeightOffset = 0f;
            KeyReductionTolerance = 0.5f;

            // Initialize layers with a single Base layer
            Layers.Clear();
            Layers.Add(new PoseLayer("Base"));
            ActiveLayerIndex = 0;

            // Initialize IK targets
            IKTargets = TwoBoneIKSolver.CreateAllTargets(BoneTransforms);
            IKEnabled = false;

            // Library state persists across sessions
            PoseBlendWeight = 1f;

            IsActive = true;
        }

        public static void Deactivate()
        {
            IsActive = false;
            IsPlaying = false;
            AutoKeyEnabled = false;
            TargetAnimator = null;
            BoneTransforms.Clear();
            OriginalRotations.Clear();
            CurrentTime = 0f;
            SelectedBone = (HumanBodyBones)(-1);
            HoveredBone = (HumanBodyBones)(-1);
            CopiedRotation = null;
            ClipboardKeyframe = null;
            ShowGraphEditor = false;

            Layers.Clear();
            ActiveLayerIndex = 0;

            IKTargets = null;
            IKEnabled = false;
        }

        // ─── Keyframe Operations ───

        public static void CaptureKeyframe()
        {
            if (TargetAnimator == null) return;

            Keyframes.RemoveAll(k => Mathf.Abs(k.Time - CurrentTime) < 0.001f);

            var kf = new PoseKeyframe { Time = CurrentTime };
            foreach (var kvp in BoneTransforms)
            {
                kf.BoneRotations[kvp.Key] = kvp.Value.localRotation;
                kf.BoneWorldPositions[kvp.Key] = kvp.Value.position;
            }

            Keyframes.Add(kf);
            Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        public static void TryAutoKey()
        {
            if (AutoKeyEnabled && IsActive && !IsPlaying)
                CaptureKeyframe();
        }

        public static void RemoveKeyframeAtCurrentTime()
        {
            Keyframes.RemoveAll(k => Mathf.Abs(k.Time - CurrentTime) < 0.001f);
        }

        public static bool HasKeyframeAtCurrentTime()
        {
            return Keyframes.Exists(k => Mathf.Abs(k.Time - CurrentTime) < 0.001f);
        }

        public static PoseKeyframe GetKeyframeAtCurrentTime()
        {
            return Keyframes.Find(k => Mathf.Abs(k.Time - CurrentTime) < 0.001f);
        }

        // ─── Keyframe Navigation ───

        public static float? GetPrevKeyframeTime()
        {
            float? best = null;
            foreach (var kf in Keyframes)
            {
                if (kf.Time < CurrentTime - 0.001f)
                    best = kf.Time;
            }
            return best;
        }

        public static float? GetNextKeyframeTime()
        {
            foreach (var kf in Keyframes)
            {
                if (kf.Time > CurrentTime + 0.001f)
                    return kf.Time;
            }
            return null;
        }

        public static void NavigateToTime(float time)
        {
            CurrentTime = time;
            if (Keyframes.Count > 0)
                ApplyPoseAtTime(time);
        }

        // ─── Pose Clipboard ───

        public static void CopyPoseToClipboard()
        {
            var kf = GetKeyframeAtCurrentTime();
            if (kf != null)
            {
                ClipboardKeyframe = kf;
            }
            else
            {
                var copy = new PoseKeyframe { Time = 0 };
                foreach (var kvp in BoneTransforms)
                {
                    copy.BoneRotations[kvp.Key] = kvp.Value.localRotation;
                    copy.BoneWorldPositions[kvp.Key] = kvp.Value.position;
                }
                ClipboardKeyframe = copy;
            }
        }

        public static void PastePoseFromClipboard()
        {
            if (ClipboardKeyframe == null || TargetAnimator == null) return;
            foreach (var kvp in ClipboardKeyframe.BoneRotations)
            {
                if (BoneTransforms.TryGetValue(kvp.Key, out var t))
                    t.localRotation = kvp.Value;
            }
            CaptureKeyframe();
        }

        // ─── Interpolation (Layer-aware) ───

        public static void ApplyPoseAtTime(float time)
        {
            if (TargetAnimator == null) return;

            // Check if any layer has keyframes
            bool anyKeyframes = false;
            foreach (var layer in Layers)
            {
                if (layer.Keyframes.Count > 0) { anyKeyframes = true; break; }
            }
            if (!anyKeyframes) return;

            // Use LayerEvaluator for multi-layer blending
            var finalRotations = LayerEvaluator.EvaluateAllLayers(
                Layers, time, OriginalRotations, DefaultCurve);

            foreach (var kvp in finalRotations)
            {
                if (BoneTransforms.TryGetValue(kvp.Key, out var t))
                    t.localRotation = kvp.Value;
            }

            // Apply IK after pose evaluation
            if (IKEnabled && IKTargets != null)
            {
                foreach (var target in IKTargets)
                {
                    if (target.Enabled)
                        TwoBoneIKSolver.SolveTargetBlended(target, BoneTransforms);
                }
            }
        }

        // ─── Onion Skin helpers ───

        public static PoseKeyframe GetPrevKeyframe()
        {
            PoseKeyframe best = null;
            foreach (var kf in Keyframes)
            {
                if (kf.Time < CurrentTime - 0.001f)
                    best = kf;
            }
            return best;
        }

        public static PoseKeyframe GetNextKeyframe()
        {
            foreach (var kf in Keyframes)
            {
                if (kf.Time > CurrentTime + 0.001f)
                    return kf;
            }
            return null;
        }

        // ─── Bone-level Dope Sheet Helpers ───

        public static bool IsBoneAnimatedInKeyframe(HumanBodyBones bone, PoseKeyframe kf)
        {
            return kf.BoneRotations.TryGetValue(bone, out var rot)
                && OriginalRotations.TryGetValue(bone, out var orig)
                && Quaternion.Angle(rot, orig) > 0.5f;
        }

        public static bool IsBoneChangedFromPrev(HumanBodyBones bone,
            PoseKeyframe kf, PoseKeyframe prev)
        {
            if (prev == null) return IsBoneAnimatedInKeyframe(bone, kf);
            bool hasCur = kf.BoneRotations.TryGetValue(bone, out var rc);
            bool hasPrev = prev.BoneRotations.TryGetValue(bone, out var rp);
            if (!hasCur && !hasPrev) return false;
            if (!hasCur || !hasPrev) return true;
            return Quaternion.Angle(rc, rp) > 0.5f;
        }

        // ─── Group-level Dope Sheet Helpers ───

        public static readonly string[] BoneGroupShortNames =
            { "Body", "L.Arm", "R.Arm", "L.Leg", "R.Leg", "L.Fing", "R.Fing" };

        public static bool IsGroupAnimatedInKeyframe(int groupIndex, PoseKeyframe kf)
        {
            foreach (var bone in BoneGroups[groupIndex].bones)
            {
                if (kf.BoneRotations.TryGetValue(bone, out var rot)
                    && OriginalRotations.TryGetValue(bone, out var orig)
                    && Quaternion.Angle(rot, orig) > 0.5f)
                    return true;
            }
            return false;
        }

        public static bool IsGroupChangedFromPrev(int groupIndex,
            PoseKeyframe kf, PoseKeyframe prev)
        {
            if (prev == null) return IsGroupAnimatedInKeyframe(groupIndex, kf);
            foreach (var bone in BoneGroups[groupIndex].bones)
            {
                bool hasCur = kf.BoneRotations.TryGetValue(bone, out var rc);
                bool hasPrev = prev.BoneRotations.TryGetValue(bone, out var rp);
                if (!hasCur && !hasPrev) continue;
                if (!hasCur || !hasPrev) return true;
                if (Quaternion.Angle(rc, rp) > 0.5f) return true;
            }
            return false;
        }

        // ─── Mirror ───

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> MirrorMap
            = new Dictionary<HumanBodyBones, HumanBodyBones>
        {
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg },
            { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot },
            { HumanBodyBones.LeftToes, HumanBodyBones.RightToes },
            { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder },
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm },
            { HumanBodyBones.LeftHand, HumanBodyBones.RightHand },
            { HumanBodyBones.LeftThumbProximal, HumanBodyBones.RightThumbProximal },
            { HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.RightThumbIntermediate },
            { HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal },
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal },
            { HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate },
            { HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal },
            { HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate },
            { HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal },
            { HumanBodyBones.LeftRingIntermediate, HumanBodyBones.RightRingIntermediate },
            { HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal },
            { HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.RightLittleIntermediate },
            { HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal },
            { HumanBodyBones.LeftEye, HumanBodyBones.RightEye },
        };

        public static HumanBodyBones GetMirrorBone(HumanBodyBones bone)
        {
            if (MirrorMap.TryGetValue(bone, out var mirror)) return mirror;
            foreach (var kvp in MirrorMap)
            {
                if (kvp.Value == bone) return kvp.Key;
            }
            return (HumanBodyBones)(-1);
        }

        public static Quaternion ComputeMirrorRotation(Quaternion rot)
        {
            var euler = rot.eulerAngles;
            return Quaternion.Euler(euler.x, -euler.y, -euler.z);
        }

        // ─── Bone Parent Map ───

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> ParentMap
            = new Dictionary<HumanBodyBones, HumanBodyBones>
        {
            { HumanBodyBones.Spine, HumanBodyBones.Hips },
            { HumanBodyBones.Chest, HumanBodyBones.Spine },
            { HumanBodyBones.UpperChest, HumanBodyBones.Chest },
            { HumanBodyBones.Neck, HumanBodyBones.UpperChest },
            { HumanBodyBones.Head, HumanBodyBones.Neck },
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg },
            { HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg },
            { HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips },
            { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg },
            { HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg },
            { HumanBodyBones.RightToes, HumanBodyBones.RightFoot },
            { HumanBodyBones.LeftShoulder, HumanBodyBones.UpperChest },
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm },
            { HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm },
            { HumanBodyBones.RightShoulder, HumanBodyBones.UpperChest },
            { HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder },
            { HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm },
            { HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm },
            { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftHand },
            { HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbProximal },
            { HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftThumbIntermediate },
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftHand },
            { HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexProximal },
            { HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftIndexIntermediate },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftHand },
            { HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleProximal },
            { HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftMiddleIntermediate },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftHand },
            { HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingProximal },
            { HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftRingIntermediate },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftHand },
            { HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleProximal },
            { HumanBodyBones.LeftLittleDistal, HumanBodyBones.LeftLittleIntermediate },
            { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightHand },
            { HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbProximal },
            { HumanBodyBones.RightThumbDistal, HumanBodyBones.RightThumbIntermediate },
            { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightHand },
            { HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexProximal },
            { HumanBodyBones.RightIndexDistal, HumanBodyBones.RightIndexIntermediate },
            { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightHand },
            { HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleProximal },
            { HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightMiddleIntermediate },
            { HumanBodyBones.RightRingProximal, HumanBodyBones.RightHand },
            { HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingProximal },
            { HumanBodyBones.RightRingDistal, HumanBodyBones.RightRingIntermediate },
            { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightHand },
            { HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleProximal },
            { HumanBodyBones.RightLittleDistal, HumanBodyBones.RightLittleIntermediate },
            { HumanBodyBones.LeftEye, HumanBodyBones.Head },
            { HumanBodyBones.RightEye, HumanBodyBones.Head },
            { HumanBodyBones.Jaw, HumanBodyBones.Head },
        };

        public static bool TryGetParentBone(HumanBodyBones bone, out HumanBodyBones parent)
        {
            return ParentMap.TryGetValue(bone, out parent);
        }

        // ─── Bone Groups ───

        public static readonly (string name, HumanBodyBones[] bones)[] BoneGroups = new[]
        {
            ("Body", new[] {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.Jaw,
                HumanBodyBones.LeftEye, HumanBodyBones.RightEye,
            }),
            ("Left Arm", new[] {
                HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            }),
            ("Right Arm", new[] {
                HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            }),
            ("Left Leg", new[] {
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            }),
            ("Right Leg", new[] {
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            }),
            ("Left Fingers", new[] {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            }),
            ("Right Fingers", new[] {
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
            }),
        };
    }
}
