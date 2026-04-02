using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public enum IKLimb { LeftHand, RightHand, LeftFoot, RightFoot }

    public class IKTarget
    {
        public IKLimb Limb;
        public bool Enabled;
        public bool Pinned;
        public float FKIKBlend = 1f; // 0=FK, 1=IK
        public Vector3 TargetPosition;
        public Vector3 PolePosition;
        public HumanBodyBones UpperBone;
        public HumanBodyBones MiddleBone;
        public HumanBodyBones EndBone;
    }

    public static class TwoBoneIKSolver
    {
        private static readonly (IKLimb limb, HumanBodyBones upper, HumanBodyBones middle, HumanBodyBones end)[]
            LimbDefs = new[]
        {
            (IKLimb.LeftHand, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            (IKLimb.RightHand, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            (IKLimb.LeftFoot, HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            (IKLimb.RightFoot, HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
        };

        public static IKTarget CreateTarget(IKLimb limb,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            foreach (var def in LimbDefs)
            {
                if (def.limb != limb) continue;

                if (!bones.TryGetValue(def.end, out var endT)) return null;
                if (!bones.TryGetValue(def.middle, out var midT)) return null;
                if (!bones.TryGetValue(def.upper, out var upperT)) return null;

                // Default pole: perpendicular from the mid-bone
                var midDir = (endT.position - upperT.position).normalized;
                var perpendicular = (midT.position -
                    Vector3.Project(midT.position - upperT.position, midDir) - upperT.position)
                    .normalized;
                if (perpendicular.sqrMagnitude < 0.001f)
                {
                    perpendicular = limb == IKLimb.LeftFoot || limb == IKLimb.RightFoot
                        ? Vector3.forward
                        : Vector3.back;
                }

                return new IKTarget
                {
                    Limb = limb,
                    Enabled = false,
                    Pinned = false,
                    TargetPosition = endT.position,
                    PolePosition = midT.position + perpendicular * 0.3f,
                    UpperBone = def.upper,
                    MiddleBone = def.middle,
                    EndBone = def.end,
                };
            }
            return null;
        }

        public static List<IKTarget> CreateAllTargets(
            Dictionary<HumanBodyBones, Transform> bones)
        {
            var targets = new List<IKTarget>();
            foreach (var limb in new[] { IKLimb.LeftHand, IKLimb.RightHand,
                                         IKLimb.LeftFoot, IKLimb.RightFoot })
            {
                var target = CreateTarget(limb, bones);
                if (target != null)
                    targets.Add(target);
            }
            return targets;
        }

        public static void Solve(Transform upper, Transform middle, Transform end,
            Vector3 targetPos, Vector3 polePos)
        {
            float L1 = Vector3.Distance(upper.position, middle.position);
            float L2 = Vector3.Distance(middle.position, end.position);

            Vector3 toTarget = targetPos - upper.position;
            float D = toTarget.magnitude;

            // Clamp distance to valid IK range
            float minDist = Mathf.Abs(L1 - L2) + 0.001f;
            float maxDist = L1 + L2 - 0.001f;
            D = Mathf.Clamp(D, minDist, maxDist);

            if (D < 0.001f) return;

            // Cosine theorem: angle at upper bone
            float cosUpper = (L1 * L1 + D * D - L2 * L2) / (2f * L1 * D);
            cosUpper = Mathf.Clamp(cosUpper, -1f, 1f);
            float angleUpper = Mathf.Acos(cosUpper);

            // Cosine theorem: angle at middle bone
            float cosMiddle = (L1 * L1 + L2 * L2 - D * D) / (2f * L1 * L2);
            cosMiddle = Mathf.Clamp(cosMiddle, -1f, 1f);
            float angleMiddle = Mathf.Acos(cosMiddle);

            // Build the IK plane from pole vector
            Vector3 targetDir = toTarget.normalized;
            if (targetDir.sqrMagnitude < 0.001f)
                targetDir = Vector3.up;

            Vector3 poleDir = (polePos - upper.position).normalized;
            Vector3 planeNormal = Vector3.Cross(targetDir, poleDir);
            if (planeNormal.sqrMagnitude < 0.001f)
            {
                // pole and target are colinear, use a fallback
                planeNormal = Vector3.Cross(targetDir, Vector3.up);
                if (planeNormal.sqrMagnitude < 0.001f)
                    planeNormal = Vector3.Cross(targetDir, Vector3.right);
            }
            planeNormal.Normalize();

            // Re-derive pole direction in the plane
            Vector3 bendDir = Vector3.Cross(planeNormal, targetDir).normalized;

            // Upper bone rotation
            Vector3 upperDir = targetDir * Mathf.Cos(angleUpper)
                             + bendDir * Mathf.Sin(angleUpper);
            Quaternion upperWorldRot = Quaternion.LookRotation(upperDir,
                Vector3.Cross(upperDir, planeNormal));

            // Apply rotation to upper bone to point towards the intermediate position
            Vector3 origUpperToMiddle = (middle.position - upper.position).normalized;
            if (origUpperToMiddle.sqrMagnitude > 0.001f)
            {
                Quaternion fromTo = Quaternion.FromToRotation(origUpperToMiddle, upperDir);
                upper.rotation = fromTo * upper.rotation;
            }

            // Recalculate middle bone after upper rotation
            Vector3 middleToEnd = (end.position - middle.position).normalized;
            Vector3 desiredMiddleToEnd = (targetPos - middle.position).normalized;

            if (middleToEnd.sqrMagnitude > 0.001f && desiredMiddleToEnd.sqrMagnitude > 0.001f)
            {
                Quaternion middleFromTo = Quaternion.FromToRotation(
                    middleToEnd, desiredMiddleToEnd);
                middle.rotation = middleFromTo * middle.rotation;
            }
        }

        public static void SolveTarget(IKTarget target,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (target == null || !target.Enabled) return;
            if (!bones.TryGetValue(target.UpperBone, out var upper)) return;
            if (!bones.TryGetValue(target.MiddleBone, out var middle)) return;
            if (!bones.TryGetValue(target.EndBone, out var end)) return;

            Solve(upper, middle, end, target.TargetPosition, target.PolePosition);
        }

        public static void SolveTargetBlended(IKTarget target,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (target == null || !target.Enabled) return;
            if (target.FKIKBlend <= 0.001f) return; // Pure FK — skip

            if (!bones.TryGetValue(target.UpperBone, out var upper)) return;
            if (!bones.TryGetValue(target.MiddleBone, out var middle)) return;
            if (!bones.TryGetValue(target.EndBone, out var end)) return;

            if (target.FKIKBlend >= 0.999f)
            {
                // Full IK — no blending needed
                Solve(upper, middle, end, target.TargetPosition, target.PolePosition);
                return;
            }

            // Save FK rotations
            var fkUpper = upper.rotation;
            var fkMiddle = middle.rotation;
            var fkEnd = end.rotation;

            // Solve IK
            Solve(upper, middle, end, target.TargetPosition, target.PolePosition);

            // Blend FK → IK
            float blend = target.FKIKBlend;
            upper.rotation = Quaternion.Slerp(fkUpper, upper.rotation, blend);
            middle.rotation = Quaternion.Slerp(fkMiddle, middle.rotation, blend);
            end.rotation = Quaternion.Slerp(fkEnd, end.rotation, blend);
        }

        public static void UpdateTargetPositionsFromBones(IKTarget target,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (target == null || target.Pinned) return;
            if (!bones.TryGetValue(target.EndBone, out var endT)) return;
            if (!bones.TryGetValue(target.MiddleBone, out var midT)) return;

            target.TargetPosition = endT.position;
            // Keep pole offset direction but recalculate position
            if (bones.TryGetValue(target.UpperBone, out var upperT))
            {
                var midPoint = (upperT.position + endT.position) * 0.5f;
                var poleDir = (midT.position - midPoint).normalized;
                if (poleDir.sqrMagnitude > 0.001f)
                    target.PolePosition = midT.position + poleDir * 0.3f;
            }
        }
    }
}
