using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Fitting;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AccessoryPlacementTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private const float ReferenceHeight = 1.5f;

        // ========== Tool: MeasureAvatarBody ==========

        [AgentTool("Measure avatar body dimensions from Humanoid bone mapping. Returns height, arm span, shoulder/hip width, hand size, limb lengths, scale ratio vs 1.5m reference, and major bone world positions.", Risk = ToolRisk.Safe)]
        public static string MeasureAvatarBody(string avatarRootName)
        {
            var avatarGo = FindGO(avatarRootName);
            if (avatarGo == null) return $"Error: Avatar '{avatarRootName}' not found.";

            var animator = avatarGo.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return $"Error: '{avatarRootName}' is not a Humanoid avatar.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Avatar Body Measurement: {avatarGo.name} ===");

            // Collect key bone positions
            var bones = new Dictionary<string, Vector3>();
            var boneTransforms = new Dictionary<string, Transform>();
            var boneList = new (HumanBodyBones bone, string label)[]
            {
                (HumanBodyBones.Hips, "Hips"),
                (HumanBodyBones.Spine, "Spine"),
                (HumanBodyBones.Chest, "Chest"),
                (HumanBodyBones.UpperChest, "UpperChest"),
                (HumanBodyBones.Neck, "Neck"),
                (HumanBodyBones.Head, "Head"),
                (HumanBodyBones.LeftShoulder, "LeftShoulder"),
                (HumanBodyBones.RightShoulder, "RightShoulder"),
                (HumanBodyBones.LeftUpperArm, "LeftUpperArm"),
                (HumanBodyBones.RightUpperArm, "RightUpperArm"),
                (HumanBodyBones.LeftLowerArm, "LeftLowerArm"),
                (HumanBodyBones.RightLowerArm, "RightLowerArm"),
                (HumanBodyBones.LeftHand, "LeftHand"),
                (HumanBodyBones.RightHand, "RightHand"),
                (HumanBodyBones.LeftUpperLeg, "LeftUpperLeg"),
                (HumanBodyBones.RightUpperLeg, "RightUpperLeg"),
                (HumanBodyBones.LeftLowerLeg, "LeftLowerLeg"),
                (HumanBodyBones.RightLowerLeg, "RightLowerLeg"),
                (HumanBodyBones.LeftFoot, "LeftFoot"),
                (HumanBodyBones.RightFoot, "RightFoot"),
                (HumanBodyBones.LeftMiddleProximal, "LeftMiddleProximal"),
                (HumanBodyBones.RightMiddleProximal, "RightMiddleProximal"),
            };

            foreach (var (bone, label) in boneList)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    bones[label] = t.position;
                    boneTransforms[label] = t;
                }
            }

            // Height: feet to head top (approximate with head + offset)
            float height = 0f;
            if (bones.ContainsKey("Head") && bones.ContainsKey("LeftFoot") && bones.ContainsKey("RightFoot"))
            {
                float feetY = (bones["LeftFoot"].y + bones["RightFoot"].y) * 0.5f;
                float headY = bones["Head"].y;
                // Estimate top of head as ~8% above head bone
                height = (headY - feetY) * 1.08f;
            }

            float scaleRatio = height > 0.01f ? height / ReferenceHeight : 1f;

            sb.AppendLine($"\n--- Dimensions ---");
            sb.AppendLine($"  Height (est.): {height:F4}m");
            sb.AppendLine($"  Scale ratio (vs {ReferenceHeight}m ref): {scaleRatio:F3}");

            // Arm span
            if (bones.ContainsKey("LeftHand") && bones.ContainsKey("RightHand"))
            {
                float armSpan = Vector3.Distance(bones["LeftHand"], bones["RightHand"]);
                sb.AppendLine($"  Arm span (hand to hand): {armSpan:F4}m");
            }

            // Shoulder width
            if (bones.ContainsKey("LeftUpperArm") && bones.ContainsKey("RightUpperArm"))
            {
                float shoulderW = Vector3.Distance(bones["LeftUpperArm"], bones["RightUpperArm"]);
                sb.AppendLine($"  Shoulder width: {shoulderW:F4}m");
            }

            // Hip width
            if (bones.ContainsKey("LeftUpperLeg") && bones.ContainsKey("RightUpperLeg"))
            {
                float hipW = Vector3.Distance(bones["LeftUpperLeg"], bones["RightUpperLeg"]);
                sb.AppendLine($"  Hip width: {hipW:F4}m");
            }

            // Limb lengths
            sb.AppendLine($"\n--- Limb Lengths ---");
            AppendLimbLength(sb, bones, "LeftUpperArm", "LeftLowerArm", "  Left upper arm");
            AppendLimbLength(sb, bones, "LeftLowerArm", "LeftHand", "  Left forearm");
            AppendLimbLength(sb, bones, "RightUpperArm", "RightLowerArm", "  Right upper arm");
            AppendLimbLength(sb, bones, "RightLowerArm", "RightHand", "  Right forearm");
            AppendLimbLength(sb, bones, "LeftUpperLeg", "LeftLowerLeg", "  Left thigh");
            AppendLimbLength(sb, bones, "LeftLowerLeg", "LeftFoot", "  Left shin");
            AppendLimbLength(sb, bones, "RightUpperLeg", "RightLowerLeg", "  Right thigh");
            AppendLimbLength(sb, bones, "RightLowerLeg", "RightFoot", "  Right shin");

            // Hand size
            if (bones.ContainsKey("LeftHand") && bones.ContainsKey("LeftMiddleProximal"))
            {
                float leftHand = Vector3.Distance(bones["LeftHand"], bones["LeftMiddleProximal"]);
                sb.AppendLine($"  Left hand (wrist→middle proximal): {leftHand:F4}m");
            }
            if (bones.ContainsKey("RightHand") && bones.ContainsKey("RightMiddleProximal"))
            {
                float rightHand = Vector3.Distance(bones["RightHand"], bones["RightMiddleProximal"]);
                sb.AppendLine($"  Right hand (wrist→middle proximal): {rightHand:F4}m");
            }

            // Torso length
            if (bones.ContainsKey("Hips") && bones.ContainsKey("Head"))
            {
                float torso = Vector3.Distance(bones["Hips"], bones["Head"]);
                sb.AppendLine($"  Torso (hips→head): {torso:F4}m");
            }

            // Bone world positions
            sb.AppendLine($"\n--- Bone World Positions ---");
            foreach (var (bone, label) in boneList)
            {
                if (bones.ContainsKey(label))
                {
                    var p = bones[label];
                    sb.AppendLine($"  {label}: ({p.x:F4}, {p.y:F4}, {p.z:F4})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== Tool: FindBodySurfacePoint ==========

        [AgentTool("Find the body surface point near a bone by ray marching through the BodySDF. direction: 'front'/'back'/'left'/'right'/'up'/'down' or custom 'x,y,z' vector. offsetAlongBone: 0-1 position along bone (default 0.5). Returns surface world position, normal, and distance from bone.", Risk = ToolRisk.Safe)]
        public static string FindBodySurfacePoint(string avatarRootName, string boneName,
            string direction = "front", float offsetAlongBone = 0.5f)
        {
            var avatarGo = FindGO(avatarRootName);
            if (avatarGo == null) return $"Error: Avatar '{avatarRootName}' not found.";

            Transform bone = ResolveBoneTransform(boneName, avatarGo);
            if (bone == null) return $"Error: Bone '{boneName}' not found. Use HumanBodyBones names (e.g., LeftUpperLeg) or the bone's GameObject name.";

            var sdf = BodySDFCache.GetOrBuild(avatarGo, out _, out _, out _);
            if (sdf == null) return "Error: Could not build BodySDF. No body mesh found on avatar.";

            // Determine ray origin along bone
            Vector3 rayOrigin = bone.position;
            Transform nextBone = MeshAnalysisTools.FindNextBoneInChain(bone, null);
            if (nextBone != null && offsetAlongBone > 0.001f)
                rayOrigin = Vector3.Lerp(bone.position, nextBone.position, Mathf.Clamp01(offsetAlongBone));

            // Parse direction
            Vector3 dir;
            if (!TryParseDirection(direction, bone, out dir))
                return $"Error: Invalid direction '{direction}'. Use 'front'/'back'/'left'/'right'/'up'/'down' or 'x,y,z' vector.";

            // Ray march from bone outward using SDF
            Vector3 surfacePos;
            Vector3 surfaceNormal;
            float distance;
            if (!RayMarchSurface(sdf, rayOrigin, dir, out surfacePos, out surfaceNormal, out distance))
                return $"Error: Could not find body surface in direction '{direction}' from bone '{boneName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Body Surface Point ===");
            sb.AppendLine($"  Bone: {bone.name}");
            sb.AppendLine($"  Direction: {direction}");
            sb.AppendLine($"  Ray origin: ({rayOrigin.x:F4}, {rayOrigin.y:F4}, {rayOrigin.z:F4})");
            sb.AppendLine($"  Surface position: ({surfacePos.x:F4}, {surfacePos.y:F4}, {surfacePos.z:F4})");
            sb.AppendLine($"  Surface normal: ({surfaceNormal.x:F4}, {surfaceNormal.y:F4}, {surfaceNormal.z:F4})");
            sb.AppendLine($"  Distance from bone: {distance:F4}m");
            return sb.ToString().TrimEnd();
        }

        // ========== Tool: SnapToBodySurface ==========

        [AgentTool("Snap a GameObject to the avatar's body surface. Uses BodySDF to find the surface point, then aligns the object's flattest face to the surface. surfaceOffset: distance from surface in meters (default 0.005). alignAxis: 'auto' or specific axis like '-Z', '+Y' etc to control which object face points toward body.")]
        public static string SnapToBodySurface(string objectName, string avatarRootName, string boneName,
            string direction = "front", float surfaceOffset = 0.005f, string alignAxis = "auto",
            float offsetAlongBone = 0.5f)
        {
            var go = FindGO(objectName);
            if (go == null) return $"Error: GameObject '{objectName}' not found.";
            var avatarGo = FindGO(avatarRootName);
            if (avatarGo == null) return $"Error: Avatar '{avatarRootName}' not found.";
            Transform bone = ResolveBoneTransform(boneName, avatarGo);
            if (bone == null) return $"Error: Bone '{boneName}' not found. Use HumanBodyBones names (e.g., LeftUpperLeg) or the bone's GameObject name.";

            var sdf = BodySDFCache.GetOrBuild(avatarGo, out _, out _, out _);
            if (sdf == null) return "Error: Could not build BodySDF. No body mesh found on avatar.";

            // Ray origin
            Vector3 rayOrigin = bone.position;
            Transform nextBone = MeshAnalysisTools.FindNextBoneInChain(bone, null);
            if (nextBone != null && offsetAlongBone > 0.001f)
                rayOrigin = Vector3.Lerp(bone.position, nextBone.position, Mathf.Clamp01(offsetAlongBone));

            // Direction
            Vector3 dir;
            if (!TryParseDirection(direction, bone, out dir))
                return $"Error: Invalid direction '{direction}'.";

            // Find surface
            Vector3 surfacePos, surfaceNormal;
            float distance;
            if (!RayMarchSurface(sdf, rayOrigin, dir, out surfacePos, out surfaceNormal, out distance))
                return $"Error: Could not find body surface in direction '{direction}' from bone '{boneName}'.";

            // Position: surface + offset along normal
            Vector3 targetPos = surfacePos + surfaceNormal * surfaceOffset;

            // Rotation: align object face to surface
            Quaternion targetRot = ComputeSurfaceAlignment(go, surfaceNormal, alignAxis);

            // Apply
            Undo.RecordObject(go.transform, "Snap to Body Surface");
            go.transform.position = targetPos;
            go.transform.rotation = targetRot;

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Snapped '{go.name}' to body surface near '{bone.name}'.");
            sb.AppendLine($"  Surface point: ({surfacePos.x:F4}, {surfacePos.y:F4}, {surfacePos.z:F4})");
            sb.AppendLine($"  Normal: ({surfaceNormal.x:F4}, {surfaceNormal.y:F4}, {surfaceNormal.z:F4})");
            sb.AppendLine($"  Position (with offset): ({targetPos.x:F4}, {targetPos.y:F4}, {targetPos.z:F4})");
            sb.AppendLine($"  Rotation: ({go.transform.eulerAngles.x:F1}, {go.transform.eulerAngles.y:F1}, {go.transform.eulerAngles.z:F1})");
            sb.AppendLine($"  Surface offset: {surfaceOffset:F4}m");
            return sb.ToString().TrimEnd();
        }

        // ========== Tool: AlignAccessoryToBone ==========

        [AgentTool("Align an accessory to a bone with body-aware placement. attachmentStyle: 'surface' (holster/pouch on body), 'grip' (held weapon in hand), 'wrap' (bracelet/cuff around limb). direction: surface search direction. surfaceOffset: gap from body (default 0.005). scaleToAvatar: auto-scale to avatar proportions (default true).")]
        public static string AlignAccessoryToBone(string accessoryName, string avatarRootName, string boneName,
            string attachmentStyle = "surface", string direction = "front",
            float surfaceOffset = 0.005f, string alignAxis = "auto", bool scaleToAvatar = true)
        {
            var go = FindGO(accessoryName);
            if (go == null) return $"Error: Accessory '{accessoryName}' not found.";
            var avatarGo = FindGO(avatarRootName);
            if (avatarGo == null) return $"Error: Avatar '{avatarRootName}' not found.";
            Transform bone = ResolveBoneTransform(boneName, avatarGo);
            if (bone == null) return $"Error: Bone '{boneName}' not found. Use HumanBodyBones names (e.g., LeftUpperLeg) or the bone's GameObject name.";

            string style = attachmentStyle.ToLowerInvariant();

            // For "wrap" on finger bones, delegate to existing AlignRingToBone
            if (style == "wrap" && IsFingerBone(bone, avatarGo))
                return MeshAnalysisTools.AlignRingToBone(accessoryName);

            var animator = avatarGo.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return $"Error: '{avatarRootName}' is not a Humanoid avatar.";

            // Compute avatar scale ratio
            float scaleRatio = 1f;
            if (scaleToAvatar)
                scaleRatio = ComputeScaleRatio(animator);

            int undoGroup = Undo.GetCurrentGroup();

            switch (style)
            {
                case "surface":
                    return AlignSurface(go, avatarGo, bone, direction, surfaceOffset, alignAxis, scaleRatio, undoGroup);
                case "grip":
                    return AlignGrip(go, avatarGo, animator, bone, scaleRatio, undoGroup);
                case "wrap":
                    return AlignWrap(go, avatarGo, bone, scaleRatio, undoGroup);
                default:
                    return $"Error: Unknown attachmentStyle '{attachmentStyle}'. Use 'surface', 'grip', or 'wrap'.";
            }
        }

        // ========== Surface Placement ==========

        private static string AlignSurface(GameObject go, GameObject avatarGo, Transform bone,
            string direction, float surfaceOffset, string alignAxis, float scaleRatio, int undoGroup)
        {
            var sdf = BodySDFCache.GetOrBuild(avatarGo, out _, out _, out _);
            if (sdf == null) return "Error: Could not build BodySDF. No body mesh found on avatar.";

            Vector3 dir;
            if (!TryParseDirection(direction, bone, out dir))
                return $"Error: Invalid direction '{direction}'.";

            // Ray from bone center
            Vector3 rayOrigin = bone.position;
            Transform nextBone = MeshAnalysisTools.FindNextBoneInChain(bone, null);
            if (nextBone != null)
                rayOrigin = Vector3.Lerp(bone.position, nextBone.position, 0.5f);

            Vector3 surfacePos, surfaceNormal;
            float distance;
            if (!RayMarchSurface(sdf, rayOrigin, dir, out surfacePos, out surfaceNormal, out distance))
                return $"Error: Could not find body surface in direction '{direction}'.";

            Vector3 targetPos = surfacePos + surfaceNormal * surfaceOffset;
            Quaternion targetRot = ComputeSurfaceAlignment(go, surfaceNormal, alignAxis);

            Undo.RecordObject(go.transform, "Align Accessory (Surface)");
            go.transform.position = targetPos;
            go.transform.rotation = targetRot;
            if (scaleRatio > 0.01f && Mathf.Abs(scaleRatio - 1f) > 0.01f)
                go.transform.localScale = Vector3.one * scaleRatio;

            Undo.CollapseUndoOperations(undoGroup);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Placed '{go.name}' on body surface near '{bone.name}' (style=surface).");
            sb.AppendLine($"  Surface: ({surfacePos.x:F4}, {surfacePos.y:F4}, {surfacePos.z:F4})");
            sb.AppendLine($"  Normal: ({surfaceNormal.x:F4}, {surfaceNormal.y:F4}, {surfaceNormal.z:F4})");
            sb.AppendLine($"  Position: ({targetPos.x:F4}, {targetPos.y:F4}, {targetPos.z:F4})");
            sb.AppendLine($"  Rotation: ({go.transform.eulerAngles.x:F1}, {go.transform.eulerAngles.y:F1}, {go.transform.eulerAngles.z:F1})");
            sb.AppendLine($"  Scale: {scaleRatio:F3}");
            return sb.ToString().TrimEnd();
        }

        // ========== Grip Placement ==========

        private static string AlignGrip(GameObject go, GameObject avatarGo, Animator animator,
            Transform bone, float scaleRatio, int undoGroup)
        {
            // Determine hand bone
            Transform hand = bone;
            bool isLeftHand = IsLeftSideBone(bone);
            var handBone = isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            var handTransform = animator.GetBoneTransform(handBone);
            if (handTransform != null)
                hand = handTransform;

            // Collect accessory vertices to find longest axis
            var verts = MeshAnalysisTools.CollectMeshVerticesLocal(go);
            if (verts.Count == 0) return "Error: No mesh found on accessory.";

            Vector3 min = verts[0], max = verts[0];
            foreach (var v in verts)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            Vector3 extents = max - min;
            Vector3 meshCenter = MeshAnalysisTools.ComputeMedianCenter(verts);

            // Longest axis = grip direction (weapon's main axis)
            Vector3 longestAxis;
            if (extents.x >= extents.y && extents.x >= extents.z)
                longestAxis = Vector3.right;
            else if (extents.y >= extents.x && extents.y >= extents.z)
                longestAxis = Vector3.up;
            else
                longestAxis = Vector3.forward;

            // Align longest axis to hand forward direction (bone Z)
            Vector3 handForward = hand.forward;
            Vector3 handUp = hand.up;

            // Find the secondary axis (thickest remaining) for up alignment
            Vector3 upAxis;
            if (longestAxis == Vector3.right)
                upAxis = extents.y >= extents.z ? Vector3.up : Vector3.forward;
            else if (longestAxis == Vector3.up)
                upAxis = extents.x >= extents.z ? Vector3.right : Vector3.forward;
            else
                upAxis = extents.x >= extents.y ? Vector3.right : Vector3.up;

            Quaternion alignRot = Quaternion.LookRotation(handForward, handUp)
                                * Quaternion.Inverse(Quaternion.LookRotation(longestAxis, upAxis));

            // Position at hand, offset by mesh center
            Vector3 targetPos = hand.position - (alignRot * (meshCenter * scaleRatio));

            Undo.RecordObject(go.transform, "Align Accessory (Grip)");
            go.transform.position = targetPos;
            go.transform.rotation = alignRot;
            if (scaleRatio > 0.01f && Mathf.Abs(scaleRatio - 1f) > 0.01f)
                go.transform.localScale = Vector3.one * scaleRatio;

            Undo.CollapseUndoOperations(undoGroup);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Placed '{go.name}' in grip at '{hand.name}' (style=grip).");
            sb.AppendLine($"  Hand: {hand.name} ({(isLeftHand ? "Left" : "Right")})");
            sb.AppendLine($"  Weapon longest axis: {(longestAxis == Vector3.right ? "X" : longestAxis == Vector3.up ? "Y" : "Z")}");
            sb.AppendLine($"  Position: ({targetPos.x:F4}, {targetPos.y:F4}, {targetPos.z:F4})");
            sb.AppendLine($"  Rotation: ({go.transform.eulerAngles.x:F1}, {go.transform.eulerAngles.y:F1}, {go.transform.eulerAngles.z:F1})");
            sb.AppendLine($"  Scale: {scaleRatio:F3}");
            return sb.ToString().TrimEnd();
        }

        // ========== Wrap Placement ==========

        private static string AlignWrap(GameObject go, GameObject avatarGo, Transform bone,
            float scaleRatio, int undoGroup)
        {
            // For non-finger wrap (bracelet, cuff): position at bone, align to bone direction
            Transform nextBone = MeshAnalysisTools.FindNextBoneInChain(bone, go.transform);
            Vector3 boneDir = nextBone != null
                ? (nextBone.position - bone.position).normalized
                : bone.up;

            // Collect mesh to find hole axis
            var verts = MeshAnalysisTools.CollectMeshVerticesLocal(go);
            if (verts.Count == 0) return "Error: No mesh found on accessory.";

            Vector3 min = verts[0], max = verts[0];
            foreach (var v in verts)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            Vector3 extents = max - min;
            Vector3 meshCenter = MeshAnalysisTools.ComputeMedianCenter(verts);

            // Shortest axis = hole axis (same logic as ring)
            Vector3 holeAxis;
            string holeAxisName;
            if (extents.x <= extents.y && extents.x <= extents.z)
            { holeAxis = Vector3.right; holeAxisName = "X"; }
            else if (extents.y <= extents.x && extents.y <= extents.z)
            { holeAxis = Vector3.up; holeAxisName = "Y"; }
            else
            { holeAxis = Vector3.forward; holeAxisName = "Z"; }

            // Up hint perpendicular to bone direction
            Vector3 upHint = Vector3.Cross(Vector3.right, boneDir);
            if (upHint.sqrMagnitude < 0.001f)
                upHint = Vector3.Cross(Vector3.forward, boneDir);
            upHint.Normalize();

            Vector3 ringUp;
            if (holeAxisName == "X")
                ringUp = extents.y >= extents.z ? Vector3.up : Vector3.forward;
            else if (holeAxisName == "Y")
                ringUp = extents.x >= extents.z ? Vector3.right : Vector3.forward;
            else
                ringUp = extents.x >= extents.y ? Vector3.right : Vector3.up;

            Quaternion alignRot = Quaternion.LookRotation(boneDir, upHint)
                                * Quaternion.Inverse(Quaternion.LookRotation(holeAxis, ringUp));

            // Position at bone midpoint
            Vector3 midpoint = nextBone != null
                ? Vector3.Lerp(bone.position, nextBone.position, 0.5f)
                : bone.position;

            Vector3 targetPos = midpoint - (alignRot * (meshCenter * scaleRatio));

            Undo.RecordObject(go.transform, "Align Accessory (Wrap)");
            go.transform.position = targetPos;
            go.transform.rotation = alignRot;
            if (scaleRatio > 0.01f && Mathf.Abs(scaleRatio - 1f) > 0.01f)
                go.transform.localScale = Vector3.one * scaleRatio;

            Undo.CollapseUndoOperations(undoGroup);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Wrapped '{go.name}' around '{bone.name}' (style=wrap).");
            sb.AppendLine($"  Hole axis: {holeAxisName} → aligned to bone direction");
            sb.AppendLine($"  Position: ({targetPos.x:F4}, {targetPos.y:F4}, {targetPos.z:F4})");
            sb.AppendLine($"  Rotation: ({go.transform.eulerAngles.x:F1}, {go.transform.eulerAngles.y:F1}, {go.transform.eulerAngles.z:F1})");
            sb.AppendLine($"  Scale: {scaleRatio:F3}");
            return sb.ToString().TrimEnd();
        }

        // ========== Tool: AnalyzeGimmickStructure ==========

        [AgentTool("Analyze a gimmick prefab's structure. Lists all direct children classified as [Mesh], [BoneProxy], [Animator], or [Other]. For MA Bone Proxy children, shows target bone name, path, attachment mode, and local transform. Useful for understanding multi-part gimmicks (weapons with holsters, hand mounts, etc.) before placement.", Risk = ToolRisk.Safe)]
        public static string AnalyzeGimmickStructure(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Gimmick Structure: {go.name} ===");

            int childCount = go.transform.childCount;
            sb.AppendLine($"Children: {childCount}");
            if (childCount == 0)
            {
                sb.AppendLine("  (no children)");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine();

            int meshCount = 0;
            int boneProxyCount = 0;
            var boneProxyEntries = new List<string>();

            for (int i = 0; i < childCount; i++)
            {
                var child = go.transform.GetChild(i);
                var role = ClassifyChild(child, out string details);

                switch (role)
                {
                    case ChildRole.Mesh:
                        meshCount++;
                        sb.AppendLine($"  [Mesh]      {child.name} ({details})");
                        break;

                    case ChildRole.BoneProxy:
                        boneProxyCount++;
                        sb.AppendLine($"  [BoneProxy] {child.name} {details}");
                        // Local transform for bone proxy children
                        var lp = child.localPosition;
                        var lr = child.localEulerAngles;
                        sb.AppendLine($"              Position: ({lp.x:F4}, {lp.y:F4}, {lp.z:F4}), Rotation: ({lr.x:F1}, {lr.y:F1}, {lr.z:F1})");
                        boneProxyEntries.Add($"  {child.name} {details}");
                        break;

                    case ChildRole.Animator:
                        sb.AppendLine($"  [Animator]  {child.name} ({details})");
                        break;

                    default:
                        sb.AppendLine($"  [Other]     {child.name} ({details})");
                        break;
                }
            }

            // VRCFury / other notable components on root
            var rootNotable = DetectNotableComponents(go);
            if (!string.IsNullOrEmpty(rootNotable))
            {
                sb.AppendLine();
                sb.AppendLine($"Root components: {rootNotable}");
            }

            // Summary
            if (boneProxyCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Attachment Points: {boneProxyCount}");
            }

            // Suggested workflow
            sb.AppendLine();
            sb.AppendLine("Suggested workflow:");
            if (boneProxyCount > 0 && boneProxyCount >= childCount - meshCount)
            {
                sb.AppendLine("  BoneProxy components handle bone attachment automatically at build time.");
                sb.AppendLine("  No additional placement setup is needed in most cases.");
                sb.AppendLine("  If positions don't match the target avatar's body shape:");
                sb.AppendLine("    1. MeasureAvatarBody to understand the avatar's proportions");
                sb.AppendLine("    2. Use SetTransform to adjust individual BoneProxy children's local transforms");
                sb.AppendLine("  Do NOT use SnapToBodySurface or AlignAccessoryToBone on BoneProxy children.");
            }
            else if (boneProxyCount > 0)
            {
                sb.AppendLine("  1. MeasureAvatarBody to get avatar scale ratio");
                sb.AppendLine("  2. BoneProxy children are auto-attached; adjust with SetTransform if needed");
                sb.AppendLine("  3. For non-BoneProxy children, use AlignAccessoryToBone or SetTransform");
                sb.AppendLine("  Do NOT use SnapToBodySurface or AlignAccessoryToBone on BoneProxy children.");
            }
            else
            {
                sb.AppendLine("  1. MeasureAvatarBody to get avatar scale ratio");
                sb.AppendLine("  2. Use AlignAccessoryToBone or SetTransform to position the gimmick");
            }

            return sb.ToString().TrimEnd();
        }

        private enum ChildRole { Mesh, BoneProxy, Animator, Other }

        private static ChildRole ClassifyChild(Transform child, out string details)
        {
            // Check MA Bone Proxy first (highest priority classification)
            var bpInfo = MAComponentFactory.GetBoneProxyInfo(child.gameObject);
            if (bpInfo != null)
            {
                details = $"→ {bpInfo.Value.targetName} ({bpInfo.Value.targetPath}), mode={bpInfo.Value.mode}";
                return ChildRole.BoneProxy;
            }

            // Check mesh renderers
            var smr = child.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                details = $"SkinnedMeshRenderer, {smr.sharedMesh.vertexCount} verts";
                return ChildRole.Mesh;
            }
            var mr = child.GetComponent<MeshRenderer>();
            var mf = child.GetComponent<MeshFilter>();
            if (mr != null && mf != null && mf.sharedMesh != null)
            {
                details = $"MeshRenderer, {mf.sharedMesh.vertexCount} verts";
                return ChildRole.Mesh;
            }

            // Check Animator
            var anim = child.GetComponent<Animator>();
            if (anim != null)
            {
                details = anim.runtimeAnimatorController != null
                    ? $"controller={anim.runtimeAnimatorController.name}"
                    : "no controller";
                return ChildRole.Animator;
            }

            // Other - list notable component types
            var notable = DetectNotableComponents(child.gameObject);
            details = string.IsNullOrEmpty(notable) ? "Transform only" : notable;
            return ChildRole.Other;
        }


        private static Transform FindAvatarRoot(Transform t)
        {
            while (t != null)
            {
                if (t.GetComponent("VRCAvatarDescriptor") != null)
                    return t;
                t = t.parent;
            }
            return null;
        }

        private static string DetectNotableComponents(GameObject go)
        {
            var notable = new List<string>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;

                // Skip common Unity components
                if (typeName == "Transform" || typeName == "RectTransform") continue;
                if (typeName == "MeshRenderer" || typeName == "MeshFilter") continue;
                if (typeName == "SkinnedMeshRenderer") continue;
                if (typeName == "Animator") continue;
                if (typeName == "ModularAvatarBoneProxy") continue;

                // Detect VRCFury components (no dependency needed)
                if (typeName.StartsWith("VRCFury"))
                {
                    notable.Add(typeName);
                    continue;
                }

                // Detect MA components
                if (typeName.StartsWith("ModularAvatar"))
                {
                    notable.Add(typeName);
                    continue;
                }

                // Detect VRC SDK components
                if (typeName.StartsWith("VRC"))
                {
                    notable.Add(typeName);
                    continue;
                }

                // Detect Particle System, Cloth, etc.
                if (typeName == "ParticleSystem" || typeName == "Cloth" ||
                    typeName == "TrailRenderer" || typeName == "LineRenderer")
                {
                    notable.Add(typeName);
                }
            }

            return notable.Count > 0 ? string.Join(", ", notable) : null;
        }

        // ========== Helpers ==========

        private static Transform ResolveBoneTransform(string boneName, GameObject avatarGo)
        {
            if (avatarGo != null)
            {
                var animator = avatarGo.GetComponent<Animator>();
                if (animator != null && animator.isHuman
                    && Enum.TryParse<HumanBodyBones>(boneName, true, out var humanBone)
                    && humanBone != HumanBodyBones.LastBone)
                {
                    var t = animator.GetBoneTransform(humanBone);
                    if (t != null) return t;
                }
            }
            var go = FindGO(boneName);
            return go != null ? go.transform : null;
        }

        private static void AppendLimbLength(StringBuilder sb, Dictionary<string, Vector3> bones, string from, string to, string label)
        {
            if (bones.ContainsKey(from) && bones.ContainsKey(to))
            {
                float len = Vector3.Distance(bones[from], bones[to]);
                sb.AppendLine($"{label}: {len:F4}m");
            }
        }

        private static float ComputeScaleRatio(Animator animator)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            if (head == null || leftFoot == null || rightFoot == null) return 1f;

            float feetY = (leftFoot.position.y + rightFoot.position.y) * 0.5f;
            float height = (head.position.y - feetY) * 1.08f;
            return height > 0.01f ? height / ReferenceHeight : 1f;
        }

        private static bool TryParseDirection(string direction, Transform bone, out Vector3 dir)
        {
            dir = Vector3.zero;
            string lower = direction.Trim().ToLowerInvariant();

            switch (lower)
            {
                case "front":   dir = Vector3.forward;  return true;
                case "back":    dir = Vector3.back;     return true;
                case "left":    dir = Vector3.left;     return true;
                case "right":   dir = Vector3.right;    return true;
                case "up":      dir = Vector3.up;       return true;
                case "down":    dir = Vector3.down;     return true;
            }

            // Try parsing as "x,y,z"
            var parts = direction.Split(',');
            if (parts.Length == 3)
            {
                float x, y, z;
                if (float.TryParse(parts[0].Trim(), out x) &&
                    float.TryParse(parts[1].Trim(), out y) &&
                    float.TryParse(parts[2].Trim(), out z))
                {
                    dir = new Vector3(x, y, z).normalized;
                    return dir.sqrMagnitude > 0.001f;
                }
            }

            return false;
        }

        /// <summary>
        /// Ray march from origin along direction to find the SDF zero-crossing (body surface).
        /// Starts inside the body (negative SDF) and marches outward, or starts outside and marches in.
        /// Uses bisection for precision.
        /// </summary>
        private static bool RayMarchSurface(BodySDF sdf, Vector3 origin, Vector3 direction,
            out Vector3 surfacePos, out Vector3 surfaceNormal, out float distance)
        {
            surfacePos = origin;
            surfaceNormal = direction;
            distance = 0f;

            float stepSize = 0.002f;
            float maxDist = 0.5f;
            int maxSteps = (int)(maxDist / stepSize);

            float prevSDF = sdf.SampleSmooth(origin);

            for (int i = 1; i <= maxSteps; i++)
            {
                float t = i * stepSize;
                Vector3 p = origin + direction * t;
                float currentSDF = sdf.SampleSmooth(p);

                // Detect zero-crossing (sign change)
                if ((prevSDF < 0f && currentSDF >= 0f) || (prevSDF >= 0f && currentSDF < 0f))
                {
                    // Bisection refinement
                    float lo = (i - 1) * stepSize;
                    float hi = t;
                    for (int b = 0; b < 12; b++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        float midSDF = sdf.SampleSmooth(origin + direction * mid);
                        if ((prevSDF < 0f && midSDF < 0f) || (prevSDF >= 0f && midSDF >= 0f))
                            lo = mid;
                        else
                            hi = mid;
                    }

                    float finalT = (lo + hi) * 0.5f;
                    surfacePos = origin + direction * finalT;
                    surfaceNormal = sdf.GradientSmooth(surfacePos);
                    distance = finalT;
                    return true;
                }

                prevSDF = currentSDF;
            }

            return false;
        }

        /// <summary>
        /// Compute rotation to align an object's flattest face to a surface normal.
        /// "auto" mode detects the shortest-extent axis as the face normal.
        /// </summary>
        private static Quaternion ComputeSurfaceAlignment(GameObject go, Vector3 surfaceNormal, string alignAxis)
        {
            Vector3 objectFaceDir;

            if (alignAxis.ToLowerInvariant() == "auto")
            {
                // Auto-detect: use the shortest-extent axis as the "face" (flat side)
                var verts = MeshAnalysisTools.CollectMeshVerticesLocal(go);
                if (verts.Count > 0)
                {
                    Vector3 min = verts[0], max = verts[0];
                    foreach (var v in verts)
                    {
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }
                    Vector3 extents = max - min;

                    // Shortest extent axis → the flat face normal direction
                    if (extents.x <= extents.y && extents.x <= extents.z)
                        objectFaceDir = -Vector3.right;
                    else if (extents.y <= extents.x && extents.y <= extents.z)
                        objectFaceDir = -Vector3.up;
                    else
                        objectFaceDir = -Vector3.forward;
                }
                else
                {
                    objectFaceDir = -Vector3.forward;
                }
            }
            else
            {
                objectFaceDir = ParseAxisString(alignAxis);
            }

            // Rotation from object face direction to inward normal (-surfaceNormal = toward body)
            return Quaternion.FromToRotation(objectFaceDir, -surfaceNormal);
        }

        private static Vector3 ParseAxisString(string axis)
        {
            string s = axis.Trim().ToUpperInvariant();
            switch (s)
            {
                case "+X": case "X":   return Vector3.right;
                case "-X":              return Vector3.left;
                case "+Y": case "Y":   return Vector3.up;
                case "-Y":              return Vector3.down;
                case "+Z": case "Z":   return Vector3.forward;
                case "-Z":              return Vector3.back;
                default:                return Vector3.back;
            }
        }

        private static bool IsFingerBone(Transform bone, GameObject avatarGo)
        {
            var animator = avatarGo.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return false;

            var fingerBones = new HumanBodyBones[]
            {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
            };

            foreach (var fb in fingerBones)
            {
                var t = animator.GetBoneTransform(fb);
                if (t == bone) return true;
            }
            return false;
        }

        private static bool IsLeftSideBone(Transform bone)
        {
            string lower = bone.name.ToLowerInvariant();
            return lower.Contains("left") || lower.Contains("_l") || lower.EndsWith(".l");
        }
    }
}
