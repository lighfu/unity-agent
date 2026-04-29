using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class PhysBoneTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private const string PhysBoneTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
        private const string PhysBoneColliderTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static Vector3? ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }

        // ================================================================
        // PhysBone Add / Remove
        // ================================================================

        [AgentTool("Add a VRCPhysBone component to a GameObject. rootTransformName: optional root bone name (defaults to self). Use ConfigureVRCPhysBone or ApplyVRCPhysBoneTemplate after adding.")]
        public static string AddVRCPhysBone(string goName, string rootTransformName = "")
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            if (go.GetComponent(pbType) != null)
                return $"Error: '{goName}' already has a VRCPhysBone component.";

            var pb = Undo.AddComponent(go, pbType);

            if (!string.IsNullOrEmpty(rootTransformName))
            {
                var rootGo = FindGO(rootTransformName);
                if (rootGo == null)
                    return $"Warning: VRCPhysBone added but rootTransform '{rootTransformName}' not found. Set it manually.";

                var so = new SerializedObject(pb);
                var rootProp = so.FindProperty("rootTransform");
                if (rootProp != null)
                {
                    rootProp.objectReferenceValue = rootGo.transform;
                    so.ApplyModifiedProperties();
                }
            }

            return $"Success: Added VRCPhysBone to '{goName}'." +
                   (string.IsNullOrEmpty(rootTransformName) ? " RootTransform: (self)." : $" RootTransform: '{rootTransformName}'.");
        }

        [AgentTool("Remove a VRCPhysBone component from a GameObject. Requires user confirmation.")]
        public static string RemoveVRCPhysBone(string goName)
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var pb = go.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            if (!AgentSettings.RequestConfirmation(
                "PhysBone 削除",
                $"'{goName}' の VRCPhysBone を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(pb);
            return $"Success: Removed VRCPhysBone from '{goName}'.";
        }

        // ================================================================
        // PhysBone List / Inspect / Configure  (moved from VRChatTools.cs)
        // ================================================================

        [AgentTool("List all VRCPhysBone paths under an avatar (overview). Use InspectVRCPhysBone for full per-bone details.")]
        public static string ListVRCPhysBones(string avatarRootName)
        {
            var physBoneType = FindType(PhysBoneTypeName);
            if (physBoneType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var physBones = go.GetComponentsInChildren(physBoneType, true);
            if (physBones.Length == 0) return $"No PhysBone components found under '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"PhysBones under '{avatarRootName}' ({physBones.Length}):");
            for (int i = 0; i < physBones.Length; i++)
            {
                var pb = physBones[i];
                string path = GetRelativePath(go.transform, pb.transform);
                sb.AppendLine($"  [{i}] {path}");
            }
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect a VRCPhysBone component in full detail (mirrors the SDK Inspector): version, transforms, forces (Simplified/Advanced w/ Momentum), limits (Angle/Hinge/Polar with rotation), collision (radius + collider paths), stretch/squish, grab/pose, options. Pass the GameObject name that owns the PhysBone.")]
        public static string InspectVRCPhysBone(string goName)
        {
            var physBoneType = FindType(PhysBoneTypeName);
            if (physBoneType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var physBone = go.GetComponent(physBoneType);
            if (physBone == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            var so = new SerializedObject(physBone);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCPhysBone on '{VRChatTools.FormatScenePath(physBone)}':");

            // Header
            var version = so.FindProperty("version");
            if (version != null)
                sb.AppendLine($"  Version: {VRChatTools.EnumDisplayName(version)}");

            // Transforms
            sb.AppendLine("  --- Transforms ---");
            var rootTransform = so.FindProperty("rootTransform");
            sb.AppendLine($"    RootTransform: {(rootTransform?.objectReferenceValue != null ? VRChatTools.FormatScenePath(rootTransform.objectReferenceValue) : "(self)")}");

            var ignoreTransforms = so.FindProperty("ignoreTransforms");
            if (ignoreTransforms != null && ignoreTransforms.isArray)
            {
                sb.AppendLine($"    IgnoreTransforms: {ignoreTransforms.arraySize}");
                for (int i = 0; i < ignoreTransforms.arraySize; i++)
                {
                    var t = ignoreTransforms.GetArrayElementAtIndex(i).objectReferenceValue;
                    sb.AppendLine($"      [{i}] {(t != null ? VRChatTools.FormatScenePath(t) : "None")}");
                }
            }

            AppendBool(sb, so, "ignoreOtherPhysBones", "    IgnoreOtherPhysBones");

            var endpointPosition = so.FindProperty("endpointPosition");
            if (endpointPosition != null)
            {
                var ep = endpointPosition.vector3Value;
                sb.AppendLine($"    EndpointPosition: ({ep.x:F4}, {ep.y:F4}, {ep.z:F4})");
            }

            var multiChildType = so.FindProperty("multiChildType");
            if (multiChildType != null)
                sb.AppendLine($"    MultiChildType: {VRChatTools.EnumDisplayName(multiChildType)}");

            // Forces
            sb.AppendLine("  --- Forces ---");
            var integrationType = so.FindProperty("integrationType");
            bool isAdvanced = integrationType != null && integrationType.intValue != 0;
            if (integrationType != null)
                sb.AppendLine($"    IntegrationType: {VRChatTools.EnumDisplayName(integrationType)}");
            AppendFloatProperty(sb, so, "pull", "    Pull");
            // VRC SDK reuses the `spring` field; the Inspector relabels it "Momentum" when IntegrationType=Advanced
            AppendFloatProperty(sb, so, "spring", isAdvanced ? "    Momentum" : "    Spring");
            AppendFloatProperty(sb, so, "stiffness", "    Stiffness");
            AppendFloatProperty(sb, so, "gravity", "    Gravity");
            AppendFloatProperty(sb, so, "gravityFalloff", "    GravityFalloff");
            var immobileType = so.FindProperty("immobileType");
            if (immobileType != null)
                sb.AppendLine($"    ImmobileType: {VRChatTools.EnumDisplayName(immobileType)}");
            AppendFloatProperty(sb, so, "immobile", "    Immobile");

            // Limits — always emit underlying fields so AI can see persisted data even when
            // the inspector hides them due to LimitType. Append "(inactive: LimitType=None)" etc.
            sb.AppendLine("  --- Limits ---");
            var limitType = so.FindProperty("limitType");
            if (limitType != null)
                sb.AppendLine($"    LimitType: {VRChatTools.EnumDisplayName(limitType)}");
            int ltVal = limitType != null ? limitType.intValue : 0;
            // 0=None, 1=Angle, 2=Hinge, 3=Polar
            string maxAngleXLabel = ltVal switch
            {
                1 or 2 => "    MaxAngle",
                3 => "    MaxAngleX",
                _ => "    MaxAngleX (inactive)",
            };
            AppendFloatProperty(sb, so, "maxAngleX", maxAngleXLabel);
            string maxAngleZLabel = ltVal == 3 ? "    MaxAngleZ" : "    MaxAngleZ (inactive)";
            AppendFloatProperty(sb, so, "maxAngleZ", maxAngleZLabel);
            var limitRotation = so.FindProperty("limitRotation");
            if (limitRotation != null)
            {
                var lr = limitRotation.vector3Value;
                string rotLabel = ltVal != 0 ? "    Rotation" : "    Rotation (inactive)";
                sb.AppendLine($"{rotLabel}: ({lr.x:F4}, {lr.y:F4}, {lr.z:F4})");
            }

            // Collision
            sb.AppendLine("  --- Collision ---");
            AppendFloatProperty(sb, so, "radius", "    Radius");
            var allowCollision = so.FindProperty("allowCollision");
            if (allowCollision != null)
                sb.AppendLine($"    AllowCollision: {VRChatTools.EnumDisplayName(allowCollision)}");
            var colliders = so.FindProperty("colliders");
            if (colliders != null && colliders.isArray)
            {
                sb.AppendLine($"    Colliders: {colliders.arraySize}");
                for (int i = 0; i < colliders.arraySize; i++)
                {
                    var c = colliders.GetArrayElementAtIndex(i).objectReferenceValue;
                    sb.AppendLine($"      [{i}] {(c != null ? VRChatTools.FormatScenePath(c) : "None")}");
                }
            }

            // Stretch & Squish
            sb.AppendLine("  --- Stretch & Squish ---");
            AppendFloatProperty(sb, so, "stretchMotion", "    StretchMotion");
            AppendFloatProperty(sb, so, "maxStretch", "    MaxStretch");
            AppendFloatProperty(sb, so, "maxSquish", "    MaxSquish");

            // Grab & Pose
            sb.AppendLine("  --- Grab & Pose ---");
            var allowGrabbing = so.FindProperty("allowGrabbing");
            if (allowGrabbing != null)
                sb.AppendLine($"    AllowGrabbing: {VRChatTools.EnumDisplayName(allowGrabbing)}");
            var allowPosing = so.FindProperty("allowPosing");
            if (allowPosing != null)
                sb.AppendLine($"    AllowPosing: {VRChatTools.EnumDisplayName(allowPosing)}");
            AppendFloatProperty(sb, so, "grabMovement", "    GrabMovement");
            AppendBool(sb, so, "snapToHand", "    SnapToHand");

            // Options
            sb.AppendLine("  --- Options ---");
            var parameter = so.FindProperty("parameter");
            if (parameter != null)
                sb.AppendLine($"    Parameter: {(string.IsNullOrEmpty(parameter.stringValue) ? "(none)" : parameter.stringValue)}");
            AppendBool(sb, so, "isAnimated", "    IsAnimated");
            AppendBool(sb, so, "resetWhenDisabled", "    ResetWhenDisabled");

            // Affected transforms count (auxiliary, not in inspector but useful)
            var exclusionsProp = so.FindProperty("ignoreTransforms");
            var exclusions = new HashSet<Transform>();
            if (exclusionsProp != null && exclusionsProp.isArray)
            {
                for (int i = 0; i < exclusionsProp.arraySize; i++)
                {
                    var excl = exclusionsProp.GetArrayElementAtIndex(i);
                    if (excl.objectReferenceValue != null)
                        exclusions.Add((Transform)excl.objectReferenceValue);
                }
            }
            Transform root = (rootTransform != null && rootTransform.objectReferenceValue != null)
                ? (Transform)rootTransform.objectReferenceValue
                : physBone.transform;
            int affectedCount = CountTransformsRecursive(root, exclusions) - 1;
            sb.AppendLine($"  AffectedTransforms: {affectedCount}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Configure VRCPhysBone parameters (full inspector parity). Sentinels for 'unchanged': float=-999, int=-1, string=null. Forces: pull, spring (= 'Momentum' when integrationType=Advanced), stiffness, gravity, gravityFalloff, immobile, integrationType (0=Simplified,1=Advanced), immobileType (0=All,1=World). Limits: limitType (0=None,1=Angle,2=Hinge,3=Polar), maxAngleX, maxAngleZ, limitRotation ('x,y,z' euler). Collision: radius, allowCollision (Permission: 0=False,1=True,2=Other). Stretch&Squish: stretchMotion, maxStretch, maxSquish. Grab&Pose: allowGrabbing (Permission: 0=False,1=True,2=Other), allowPosing (Permission: 0=False,1=True,2=Other), grabMovement, snapToHand (0=false,1=true). Transforms: multiChildType (0=Ignore,1=First,2=Average), ignoreOtherPhysBones (0=false,1=true), endpointPosition ('x,y,z'). Options: isAnimated (0=false,1=true), resetWhenDisabled (0=false,1=true), parameter.")]
        public static string ConfigureVRCPhysBone(
            string goName,
            // Forces
            float pull = -999, float spring = -999, float stiffness = -999,
            float gravity = -999, float gravityFalloff = -999, float immobile = -999,
            int integrationType = -1, int immobileType = -1,
            // Limits
            int limitType = -1, float maxAngleX = -999, float maxAngleZ = -999, string limitRotation = null,
            // Collision
            float radius = -999, int allowCollision = -1,
            // Stretch & Squish
            float stretchMotion = -999, float maxStretch = -999, float maxSquish = -999,
            // Grab & Pose
            int allowGrabbing = -1, int allowPosing = -1, float grabMovement = -999, int snapToHand = -1,
            // Transforms
            int multiChildType = -1, int ignoreOtherPhysBones = -1, string endpointPosition = null,
            // Options
            int isAnimated = -1, int resetWhenDisabled = -1, string parameter = null)
        {
            var physBoneType = FindType(PhysBoneTypeName);
            if (physBoneType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var physBone = go.GetComponent(physBoneType);
            if (physBone == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            Undo.RecordObject(physBone, "Configure PhysBone via Agent");

            var so = new SerializedObject(physBone);
            int changed = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Configured PhysBone on '{goName}':");

            // Forces
            changed += SetFloatIfChanged(so, sb, "pull", pull);
            changed += SetFloatIfChanged(so, sb, "spring", spring);
            changed += SetFloatIfChanged(so, sb, "stiffness", stiffness);
            changed += SetFloatIfChanged(so, sb, "gravity", gravity);
            changed += SetFloatIfChanged(so, sb, "gravityFalloff", gravityFalloff);
            changed += SetFloatIfChanged(so, sb, "immobile", immobile);
            changed += SetIntIfChanged(so, sb, "integrationType", integrationType);
            changed += SetIntIfChanged(so, sb, "immobileType", immobileType);

            // Limits
            changed += SetIntIfChanged(so, sb, "limitType", limitType);
            changed += SetFloatIfChanged(so, sb, "maxAngleX", maxAngleX);
            changed += SetFloatIfChanged(so, sb, "maxAngleZ", maxAngleZ);
            changed += SetVector3IfChanged(so, sb, "limitRotation", limitRotation);

            // Collision
            changed += SetFloatIfChanged(so, sb, "radius", radius);
            changed += SetIntIfChanged(so, sb, "allowCollision", allowCollision);

            // Stretch & Squish
            changed += SetFloatIfChanged(so, sb, "stretchMotion", stretchMotion);
            changed += SetFloatIfChanged(so, sb, "maxStretch", maxStretch);
            changed += SetFloatIfChanged(so, sb, "maxSquish", maxSquish);

            // Grab & Pose
            changed += SetIntIfChanged(so, sb, "allowGrabbing", allowGrabbing);
            changed += SetIntIfChanged(so, sb, "allowPosing", allowPosing);
            changed += SetFloatIfChanged(so, sb, "grabMovement", grabMovement);
            changed += SetBoolIfChanged(so, sb, "snapToHand", snapToHand);

            // Transforms
            changed += SetIntIfChanged(so, sb, "multiChildType", multiChildType);
            changed += SetBoolIfChanged(so, sb, "ignoreOtherPhysBones", ignoreOtherPhysBones);
            changed += SetVector3IfChanged(so, sb, "endpointPosition", endpointPosition);

            // Options
            changed += SetBoolIfChanged(so, sb, "isAnimated", isAnimated);
            changed += SetBoolIfChanged(so, sb, "resetWhenDisabled", resetWhenDisabled);

            if (parameter != null)
            {
                var prop = so.FindProperty("parameter");
                if (prop != null)
                {
                    prop.stringValue = parameter;
                    sb.AppendLine($"  parameter: {parameter}");
                    changed++;
                }
            }

            if (changed == 0) return $"No changes made to PhysBone on '{goName}' (all values unchanged).";

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(physBone);
            sb.AppendLine($"  ({changed} parameter(s) updated)");

            return sb.ToString().TrimEnd();
        }

        // ================================================================
        // PhysBone Templates
        // ================================================================

        private struct PhysBoneTemplate
        {
            public float pull, spring, stiffness, gravity, immobile, radius;
            public int limitType; // 0=None, 1=Angle, 2=Hinge
            public float maxAngleX;
            public bool allowGrabbing, allowPosing;
        }

        private static readonly Dictionary<string, PhysBoneTemplate> PhysBoneTemplates = new Dictionary<string, PhysBoneTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            { "Hair", new PhysBoneTemplate { pull = 0.2f, spring = 0.2f, stiffness = 0.2f, gravity = 0.1f, immobile = 0f, limitType = 1, maxAngleX = 60f, radius = 0.02f, allowGrabbing = true, allowPosing = true } },
            { "Skirt", new PhysBoneTemplate { pull = 0.3f, spring = 0.4f, stiffness = 0.1f, gravity = 0.3f, immobile = 0f, limitType = 1, maxAngleX = 45f, radius = 0.02f, allowGrabbing = false, allowPosing = false } },
            { "Tail", new PhysBoneTemplate { pull = 0.3f, spring = 0.5f, stiffness = 0.3f, gravity = 0.05f, immobile = 0f, limitType = 2, maxAngleX = 90f, radius = 0.03f, allowGrabbing = true, allowPosing = true } },
            { "Breast", new PhysBoneTemplate { pull = 0.15f, spring = 0.3f, stiffness = 0.3f, gravity = 0.05f, immobile = 0.5f, limitType = 1, maxAngleX = 30f, radius = 0.05f, allowGrabbing = false, allowPosing = false } },
            { "Ears", new PhysBoneTemplate { pull = 0.1f, spring = 0.1f, stiffness = 0.5f, gravity = 0.02f, immobile = 0f, limitType = 1, maxAngleX = 30f, radius = 0.01f, allowGrabbing = true, allowPosing = true } },
            { "Ribbon", new PhysBoneTemplate { pull = 0.3f, spring = 0.5f, stiffness = 0.1f, gravity = 0.15f, immobile = 0f, limitType = 0, maxAngleX = 0f, radius = 0.01f, allowGrabbing = true, allowPosing = false } },
        };

        [AgentTool("Apply a predefined PhysBone template. Templates: 'Hair', 'Skirt', 'Tail', 'Breast', 'Ears', 'Ribbon'. Adjusts pull, spring, stiffness, gravity, limits, grab/pose.")]
        public static string ApplyVRCPhysBoneTemplate(string goName, string template)
        {
            var physBoneType = FindType(PhysBoneTypeName);
            if (physBoneType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var physBone = go.GetComponent(physBoneType);
            if (physBone == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            if (!PhysBoneTemplates.TryGetValue(template, out var tmpl))
                return $"Error: Unknown template '{template}'. Available: {string.Join(", ", PhysBoneTemplates.Keys)}.";

            if (!AgentSettings.RequestConfirmation(
                "PhysBone テンプレート適用",
                $"対象: {goName}\n" +
                $"テンプレート: {template}\n" +
                $"pull={tmpl.pull}, spring={tmpl.spring}, stiffness={tmpl.stiffness}\n" +
                $"gravity={tmpl.gravity}, immobile={tmpl.immobile}\n" +
                $"limitType={tmpl.limitType}, maxAngleX={tmpl.maxAngleX}, radius={tmpl.radius}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(physBone, "Apply PhysBone Template");
            var so = new SerializedObject(physBone);

            void SetFloat(string name, float val) { var p = so.FindProperty(name); if (p != null) p.floatValue = val; }
            void SetInt(string name, int val) { var p = so.FindProperty(name); if (p != null) p.intValue = val; }

            SetFloat("pull", tmpl.pull);
            SetFloat("spring", tmpl.spring);
            SetFloat("stiffness", tmpl.stiffness);
            SetFloat("gravity", tmpl.gravity);
            SetFloat("immobile", tmpl.immobile);
            SetFloat("radius", tmpl.radius);
            SetInt("limitType", tmpl.limitType);
            if (tmpl.limitType != 0)
                SetFloat("maxAngleX", tmpl.maxAngleX);
            // VRC SDK Permission enum: 0=False, 1=True, 2=Other (verified empirically via enumDisplayNames).
            SetInt("allowGrabbing", tmpl.allowGrabbing ? 1 : 0);
            SetInt("allowPosing", tmpl.allowPosing ? 1 : 0);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(physBone);

            return $"Success: Applied '{template}' template to PhysBone on '{goName}'.\n" +
                   $"  pull={tmpl.pull}, spring={tmpl.spring}, stiffness={tmpl.stiffness}\n" +
                   $"  gravity={tmpl.gravity}, immobile={tmpl.immobile}\n" +
                   $"  limitType={tmpl.limitType}, maxAngleX={tmpl.maxAngleX}, radius={tmpl.radius}\n" +
                   $"  allowGrabbing={tmpl.allowGrabbing}, allowPosing={tmpl.allowPosing}";
        }

        // ================================================================
        // PhysBone Collider
        // ================================================================

        [AgentTool("Add a VRCPhysBoneCollider to a GameObject. shapeType: 0=Sphere, 1=Capsule, 2=Plane. position format: 'x,y,z'. rotation format: 'x,y,z' (euler degrees).")]
        public static string AddVRCPhysBoneCollider(string goName, int shapeType = 0, float radius = 0.05f, float height = 0f, string position = "", string rotation = "")
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = Undo.AddComponent(go, colliderType);
            var so = new SerializedObject(col);

            var shapeProp = so.FindProperty("shapeType");
            if (shapeProp != null) shapeProp.intValue = shapeType;

            var radiusProp = so.FindProperty("radius");
            if (radiusProp != null) radiusProp.floatValue = radius;

            var heightProp = so.FindProperty("height");
            if (heightProp != null) heightProp.floatValue = height;

            var posVec = ParseVector3(position);
            if (posVec.HasValue)
            {
                var posProp = so.FindProperty("position");
                if (posProp != null) posProp.vector3Value = posVec.Value;
            }

            var rotVec = ParseVector3(rotation);
            if (rotVec.HasValue)
            {
                var rotProp = so.FindProperty("rotation");
                if (rotProp != null) rotProp.quaternionValue = Quaternion.Euler(rotVec.Value);
            }

            so.ApplyModifiedProperties();

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };
            string shapeName = shapeType >= 0 && shapeType < shapeNames.Length ? shapeNames[shapeType] : shapeType.ToString();
            return $"Success: Added VRCPhysBoneCollider ({shapeName}, radius={radius:F3}) to '{goName}'.";
        }

        [AgentTool("List all VRCPhysBoneCollider components under an avatar. Shows shape, radius, height, and which PhysBones reference them.")]
        public static string ListVRCPhysBoneColliders(string avatarRootName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var colliders = go.GetComponentsInChildren(colliderType, true);
            if (colliders.Length == 0)
                return $"No PhysBoneCollider found under '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"PhysBoneColliders under '{avatarRootName}' ({colliders.Length}):");

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                var so = new SerializedObject(col);
                string path = GetRelativePath(go.transform, col.transform);

                var shape = so.FindProperty("shapeType");
                var radius = so.FindProperty("radius");
                var height = so.FindProperty("height");

                int shapeVal = shape != null ? shape.intValue : -1;
                string shapeName = shapeVal >= 0 && shapeVal < shapeNames.Length ? shapeNames[shapeVal] : "?";

                sb.Append($"  [{i}] {path} ({shapeName}");
                if (radius != null) sb.Append($", r={radius.floatValue:F3}");
                if (height != null && height.floatValue > 0) sb.Append($", h={height.floatValue:F3}");
                sb.AppendLine(")");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect a VRCPhysBoneCollider in detail. Shows shapeType, radius, height, position, rotation, insideBounds, bonesAsSpheres.")]
        public static string InspectVRCPhysBoneCollider(string goName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            var so = new SerializedObject(col);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCPhysBoneCollider on '{goName}':");

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };
            var shape = so.FindProperty("shapeType");
            if (shape != null)
            {
                int v = shape.intValue;
                sb.AppendLine($"  ShapeType: {(v >= 0 && v < shapeNames.Length ? shapeNames[v] : v.ToString())}");
            }

            var radius = so.FindProperty("radius");
            if (radius != null) sb.AppendLine($"  Radius: {radius.floatValue:F4}");

            var height = so.FindProperty("height");
            if (height != null) sb.AppendLine($"  Height: {height.floatValue:F4}");

            var pos = so.FindProperty("position");
            if (pos != null) sb.AppendLine($"  Position: {pos.vector3Value}");

            var rot = so.FindProperty("rotation");
            if (rot != null) sb.AppendLine($"  Rotation: {rot.quaternionValue.eulerAngles}");

            var inside = so.FindProperty("insideBounds");
            if (inside != null) sb.AppendLine($"  InsideBounds: {inside.boolValue}");

            var bonesAsSpheres = so.FindProperty("bonesAsSpheres");
            if (bonesAsSpheres != null) sb.AppendLine($"  BonesAsSpheres: {bonesAsSpheres.boolValue}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Configure a VRCPhysBoneCollider. shapeType: 0=Sphere,1=Capsule,2=Plane (-1=unchanged). position/rotation format: 'x,y,z'. Use -999 for unchanged floats.")]
        public static string ConfigureVRCPhysBoneCollider(string goName, int shapeType = -1, float radius = -999, float height = -999, string position = "", string rotation = "", int insideBounds = -1)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            Undo.RecordObject(col, "Configure PhysBoneCollider");
            var so = new SerializedObject(col);
            int changed = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Configured PhysBoneCollider on '{goName}':");

            if (shapeType >= 0)
            {
                var p = so.FindProperty("shapeType");
                if (p != null) { p.intValue = shapeType; sb.AppendLine($"  shapeType: {shapeType}"); changed++; }
            }
            if (radius != -999)
            {
                var p = so.FindProperty("radius");
                if (p != null) { p.floatValue = radius; sb.AppendLine($"  radius: {radius:F4}"); changed++; }
            }
            if (height != -999)
            {
                var p = so.FindProperty("height");
                if (p != null) { p.floatValue = height; sb.AppendLine($"  height: {height:F4}"); changed++; }
            }

            var posVec = ParseVector3(position);
            if (posVec.HasValue)
            {
                var p = so.FindProperty("position");
                if (p != null) { p.vector3Value = posVec.Value; sb.AppendLine($"  position: {posVec.Value}"); changed++; }
            }

            var rotVec = ParseVector3(rotation);
            if (rotVec.HasValue)
            {
                var p = so.FindProperty("rotation");
                if (p != null) { p.quaternionValue = Quaternion.Euler(rotVec.Value); sb.AppendLine($"  rotation: {rotVec.Value}"); changed++; }
            }

            if (insideBounds >= 0)
            {
                var p = so.FindProperty("insideBounds");
                if (p != null) { p.boolValue = insideBounds != 0; sb.AppendLine($"  insideBounds: {p.boolValue}"); changed++; }
            }

            if (changed == 0) return "No changes made (all values unchanged).";

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(col);
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Remove a VRCPhysBoneCollider from a GameObject. Requires user confirmation.")]
        public static string RemoveVRCPhysBoneCollider(string goName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            if (!AgentSettings.RequestConfirmation(
                "PhysBoneCollider 削除",
                $"'{goName}' の VRCPhysBoneCollider を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(col);
            return $"Success: Removed VRCPhysBoneCollider from '{goName}'.";
        }

        // ================================================================
        // PhysBone <-> Collider Link
        // ================================================================

        [AgentTool("Add a VRCPhysBoneCollider reference to a VRCPhysBone's collider list. physBoneGoName: the PhysBone's GameObject, colliderGoName: the Collider's GameObject.")]
        public static string LinkVRCColliderToPhysBone(string physBoneGoName, string colliderGoName)
        {
            var pbType = FindType(PhysBoneTypeName);
            var colType = FindType(PhysBoneColliderTypeName);
            if (pbType == null || colType == null) return "Error: VRChat SDK not found.";

            var pbGo = FindGO(physBoneGoName);
            if (pbGo == null) return $"Error: GameObject '{physBoneGoName}' not found.";
            var pb = pbGo.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{physBoneGoName}'.";

            var colGo = FindGO(colliderGoName);
            if (colGo == null) return $"Error: GameObject '{colliderGoName}' not found.";
            var col = colGo.GetComponent(colType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{colliderGoName}'.";

            Undo.RecordObject(pb, "Link Collider to PhysBone");
            var so = new SerializedObject(pb);
            var collidersProp = so.FindProperty("colliders");
            if (collidersProp == null || !collidersProp.isArray)
                return "Error: Cannot find 'colliders' property on VRCPhysBone.";

            // Check for duplicates
            for (int i = 0; i < collidersProp.arraySize; i++)
            {
                if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue == col)
                    return $"Info: '{colliderGoName}' is already linked to PhysBone on '{physBoneGoName}'.";
            }

            int idx = collidersProp.arraySize;
            collidersProp.InsertArrayElementAtIndex(idx);
            collidersProp.GetArrayElementAtIndex(idx).objectReferenceValue = col;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);

            return $"Success: Linked '{colliderGoName}' collider to PhysBone on '{physBoneGoName}' (total: {collidersProp.arraySize}).";
        }

        [AgentTool("Remove a VRCPhysBoneCollider reference from a VRCPhysBone's collider list.")]
        public static string UnlinkVRCColliderFromPhysBone(string physBoneGoName, string colliderGoName)
        {
            var pbType = FindType(PhysBoneTypeName);
            var colType = FindType(PhysBoneColliderTypeName);
            if (pbType == null || colType == null) return "Error: VRChat SDK not found.";

            var pbGo = FindGO(physBoneGoName);
            if (pbGo == null) return $"Error: GameObject '{physBoneGoName}' not found.";
            var pb = pbGo.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{physBoneGoName}'.";

            var colGo = FindGO(colliderGoName);
            if (colGo == null) return $"Error: GameObject '{colliderGoName}' not found.";
            var col = colGo.GetComponent(colType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{colliderGoName}'.";

            Undo.RecordObject(pb, "Unlink Collider from PhysBone");
            var so = new SerializedObject(pb);
            var collidersProp = so.FindProperty("colliders");
            if (collidersProp == null || !collidersProp.isArray)
                return "Error: Cannot find 'colliders' property on VRCPhysBone.";

            bool found = false;
            for (int i = collidersProp.arraySize - 1; i >= 0; i--)
            {
                if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue == col)
                {
                    // Clear reference first, then delete element
                    collidersProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    collidersProp.DeleteArrayElementAtIndex(i);
                    found = true;
                }
            }

            if (!found)
                return $"Info: '{colliderGoName}' was not in PhysBone's collider list on '{physBoneGoName}'.";

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);
            return $"Success: Unlinked '{colliderGoName}' from PhysBone on '{physBoneGoName}'.";
        }

        // ================================================================
        // PhysBone Exclusions
        // (Endpoint position is configurable via ConfigureVRCPhysBone(endpointPosition).)
        // ================================================================

        [AgentTool("Set exclusion transforms for a VRCPhysBone. exclusionNames: comma-separated list of GameObjects to exclude from PhysBone influence. Pass empty string to clear all exclusions.")]
        public static string SetVRCPhysBoneExclusions(string goName, string exclusionNames)
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var pb = go.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{goName}'.";

            Undo.RecordObject(pb, "Set PhysBone Exclusions");
            var so = new SerializedObject(pb);
            // VRC SDK serializes exclusions under the field name 'ignoreTransforms'.
            var exProp = so.FindProperty("ignoreTransforms");
            if (exProp == null || !exProp.isArray)
                return "Error: Cannot find 'ignoreTransforms' property on VRCPhysBone.";

            // Clear existing
            exProp.ClearArray();

            if (string.IsNullOrWhiteSpace(exclusionNames))
            {
                so.ApplyModifiedProperties();
                return $"Success: Cleared all exclusions from PhysBone on '{goName}'.";
            }

            var names = exclusionNames.Split(',');
            var added = new List<string>();
            var notFound = new List<string>();

            foreach (var rawName in names)
            {
                string name = rawName.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var exGo = FindGO(name);
                if (exGo == null)
                {
                    notFound.Add(name);
                    continue;
                }

                int idx = exProp.arraySize;
                exProp.InsertArrayElementAtIndex(idx);
                exProp.GetArrayElementAtIndex(idx).objectReferenceValue = exGo.transform;
                added.Add(name);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Set {added.Count} exclusion(s) on PhysBone '{goName}':");
            foreach (var n in added) sb.AppendLine($"  + {n}");
            if (notFound.Count > 0)
            {
                sb.AppendLine("  Not found:");
                foreach (var n in notFound) sb.AppendLine($"  ? {n}");
            }
            return sb.ToString().TrimEnd();
        }

        // ================================================================
        // Internal helpers
        // ================================================================

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return root.name;
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void AppendBool(StringBuilder sb, SerializedObject so, string name, string label)
        {
            var p = so.FindProperty(name);
            if (p != null) sb.AppendLine($"{label}: {p.boolValue}");
        }

        private static void AppendFloatProperty(StringBuilder sb, SerializedObject so, string propName, string label)
        {
            var prop = so.FindProperty(propName);
            if (prop != null)
                sb.AppendLine($"{label}: {prop.floatValue:F3}");
        }

        private static int CountTransformsRecursive(Transform t, HashSet<Transform> exclusions)
        {
            if (exclusions.Contains(t)) return 0;
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
                count += CountTransformsRecursive(t.GetChild(i), exclusions);
            return count;
        }

        private static int SetFloatIfChanged(SerializedObject so, StringBuilder sb, string propName, float value)
        {
            if (value == -999) return 0;
            var prop = so.FindProperty(propName);
            if (prop == null) return 0;
            prop.floatValue = value;
            sb.AppendLine($"  {propName}: {value:F3}");
            return 1;
        }

        private static int SetIntIfChanged(SerializedObject so, StringBuilder sb, string propName, int value)
        {
            if (value == -1) return 0;
            var prop = so.FindProperty(propName);
            if (prop == null) return 0;
            prop.intValue = value;
            sb.AppendLine($"  {propName}: {value}");
            return 1;
        }

        private static int SetBoolIfChanged(SerializedObject so, StringBuilder sb, string propName, int value)
        {
            if (value < 0) return 0;
            var prop = so.FindProperty(propName);
            if (prop == null) return 0;
            prop.boolValue = value != 0;
            sb.AppendLine($"  {propName}: {prop.boolValue}");
            return 1;
        }

        private static int SetVector3IfChanged(SerializedObject so, StringBuilder sb, string propName, string xyz)
        {
            if (string.IsNullOrEmpty(xyz)) return 0;
            var parts = xyz.Split(',');
            if (parts.Length != 3) { sb.AppendLine($"  (skipped {propName}: expected 'x,y,z', got '{xyz}')"); return 0; }
            if (!float.TryParse(parts[0].Trim(), out var x) ||
                !float.TryParse(parts[1].Trim(), out var y) ||
                !float.TryParse(parts[2].Trim(), out var z))
            {
                sb.AppendLine($"  (skipped {propName}: parse failed for '{xyz}')");
                return 0;
            }
            var prop = so.FindProperty(propName);
            if (prop == null) return 0;
            prop.vector3Value = new Vector3(x, y, z);
            sb.AppendLine($"  {propName}: ({x:F4}, {y:F4}, {z:F4})");
            return 1;
        }
    }
}
