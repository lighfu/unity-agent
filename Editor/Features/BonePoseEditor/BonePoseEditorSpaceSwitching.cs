using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public enum BoneSpace { Local, World, ParentBone }

    [System.Serializable]
    public class SpaceOverride
    {
        public BoneSpace Space = BoneSpace.Local;
        public HumanBodyBones ReferenceBone; // ParentBone mode reference
    }

    public static class BonePoseEditorSpaceSwitching
    {
        /// <summary>
        /// Convert a world rotation to a space-relative representation for storage.
        /// </summary>
        public static Quaternion WorldToSpace(Quaternion worldRot,
            SpaceOverride space, Dictionary<HumanBodyBones, Transform> bones)
        {
            switch (space.Space)
            {
                case BoneSpace.World:
                    return worldRot;

                case BoneSpace.ParentBone:
                    if (bones.TryGetValue(space.ReferenceBone, out var refBone))
                        return Quaternion.Inverse(refBone.rotation) * worldRot;
                    return worldRot;

                case BoneSpace.Local:
                default:
                    return worldRot; // Caller handles local conversion
            }
        }

        /// <summary>
        /// Convert a stored space-relative rotation back to a local rotation
        /// that can be applied to the bone.
        /// </summary>
        public static Quaternion SpaceToLocal(Quaternion storedRot,
            SpaceOverride space, HumanBodyBones bone,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (!bones.TryGetValue(bone, out var boneT)) return storedRot;
            var parent = boneT.parent;
            if (parent == null) return storedRot;

            switch (space.Space)
            {
                case BoneSpace.World:
                    // storedRot is world rotation → convert to local
                    return Quaternion.Inverse(parent.rotation) * storedRot;

                case BoneSpace.ParentBone:
                    if (bones.TryGetValue(space.ReferenceBone, out var refBone))
                    {
                        // storedRot is relative to refBone → convert to world → to local
                        Quaternion worldRot = refBone.rotation * storedRot;
                        return Quaternion.Inverse(parent.rotation) * worldRot;
                    }
                    return storedRot;

                case BoneSpace.Local:
                default:
                    return storedRot; // Already local
            }
        }

        /// <summary>
        /// Switch space for a bone while maintaining visual position.
        /// Updates the rotation value in the keyframe.
        /// </summary>
        public static void SwitchSpace(HumanBodyBones bone,
            SpaceOverride newSpace, PoseKeyframe kf,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (!bones.TryGetValue(bone, out var boneT)) return;
            if (!kf.BoneRotations.TryGetValue(bone, out var currentRot)) return;

            // Get current space
            SpaceOverride oldSpace = null;
            if (kf.BoneSpaces.TryGetValue(bone, out var existing))
                oldSpace = existing;

            // Current local rotation → world rotation
            Quaternion worldRot;
            if (oldSpace == null || oldSpace.Space == BoneSpace.Local)
            {
                var parent = boneT.parent;
                worldRot = parent != null
                    ? parent.rotation * currentRot
                    : currentRot;
            }
            else if (oldSpace.Space == BoneSpace.World)
            {
                worldRot = currentRot;
            }
            else // ParentBone
            {
                if (bones.TryGetValue(oldSpace.ReferenceBone, out var oldRef))
                    worldRot = oldRef.rotation * currentRot;
                else
                    worldRot = currentRot;
            }

            // World rotation → new space representation
            Quaternion newRot;
            switch (newSpace.Space)
            {
                case BoneSpace.World:
                    newRot = worldRot;
                    break;
                case BoneSpace.ParentBone:
                    if (bones.TryGetValue(newSpace.ReferenceBone, out var newRef))
                        newRot = Quaternion.Inverse(newRef.rotation) * worldRot;
                    else
                        newRot = worldRot;
                    break;
                default: // Local
                {
                    var parent = boneT.parent;
                    newRot = parent != null
                        ? Quaternion.Inverse(parent.rotation) * worldRot
                        : worldRot;
                    break;
                }
            }

            kf.BoneRotations[bone] = newRot;
            kf.BoneSpaces[bone] = new SpaceOverride
            {
                Space = newSpace.Space,
                ReferenceBone = newSpace.ReferenceBone,
            };
        }

        /// <summary>
        /// Convert a rotation stored in a given space to Local space for interpolation.
        /// </summary>
        public static Quaternion ConvertToLocal(Quaternion storedRot,
            SpaceOverride space, HumanBodyBones bone,
            Dictionary<HumanBodyBones, Transform> bones)
        {
            if (space == null || space.Space == BoneSpace.Local)
                return storedRot;
            return SpaceToLocal(storedRot, space, bone, bones);
        }
    }
}
