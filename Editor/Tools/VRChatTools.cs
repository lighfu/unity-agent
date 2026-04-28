using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class VRChatTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        internal const string VrcDescriptorTypeName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        internal const string VrcPhysBoneTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
        internal const string VrcExpressionParametersTypeName = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";
        internal const string VrcExpressionsMenuTypeName = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu";

        internal static Type FindVrcType(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullTypeName);
                if (type != null) return type;
            }
            return null;
        }

        internal static Component FindAvatarDescriptor(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return null;

            var type = FindVrcType(VrcDescriptorTypeName);
            if (type == null) return null;

            return go.GetComponent(type);
        }

        [AgentTool("Inspect VRCAvatarDescriptor settings (Viewpoint, LipSync, Eye Look, Playable Layers, Lower Body, Expressions, Colliders, Rig). Requires VRChat SDK.")]
        public static string InspectVRCAvatarDescriptor(string avatarRootName)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCAvatarDescriptor on '{avatarRootName}':");

            // Viewpoint (full precision)
            var viewPosition = so.FindProperty("ViewPosition");
            if (viewPosition != null)
            {
                var v = viewPosition.vector3Value;
                sb.AppendLine($"  ViewPosition: ({v.x:F6}, {v.y:F6}, {v.z:F6})");
            }

            // LipSync
            var lipSync = so.FindProperty("lipSync");
            if (lipSync != null)
                sb.AppendLine($"  LipSync: {EnumDisplayName(lipSync)}");

            var lipSyncMesh = so.FindProperty("VisemeSkinnedMesh");
            if (lipSyncMesh != null && lipSyncMesh.objectReferenceValue != null)
                sb.AppendLine($"  VisemeSkinnedMesh: {FormatScenePath(lipSyncMesh.objectReferenceValue)}");

            // Visemes
            var visemeBlendShapes = so.FindProperty("VisemeBlendShapes");
            if (visemeBlendShapes != null && visemeBlendShapes.isArray && visemeBlendShapes.arraySize > 0)
            {
                sb.AppendLine($"  Visemes ({visemeBlendShapes.arraySize}):");
                string[] visemeNames = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
                for (int i = 0; i < visemeBlendShapes.arraySize; i++)
                {
                    string label = i < visemeNames.Length ? visemeNames[i] : $"v_{i}";
                    sb.AppendLine($"    {label}: {visemeBlendShapes.GetArrayElementAtIndex(i).stringValue}");
                }
            }

            // Eye Look
            AppendEyeLook(sb, so);

            // Playable Layers
            var customizeAnimLayers = so.FindProperty("customizeAnimationLayers");
            if (customizeAnimLayers != null)
                sb.AppendLine($"  CustomizeAnimationLayers: {customizeAnimLayers.boolValue}");

            var baseLayers = so.FindProperty("baseAnimationLayers");
            AppendAnimLayers(sb, baseLayers, "Base Animation Layers");

            var specialLayers = so.FindProperty("specialAnimationLayers");
            AppendAnimLayers(sb, specialLayers, "Special Animation Layers");

            // Lower Body
            var autoFootsteps = so.FindProperty("autoFootsteps");
            var autoLocomotion = so.FindProperty("autoLocomotion");
            if (autoFootsteps != null || autoLocomotion != null)
            {
                sb.AppendLine("  Lower Body:");
                if (autoFootsteps != null)
                    sb.AppendLine($"    AutoFootsteps (3-4 point tracking): {autoFootsteps.boolValue}");
                if (autoLocomotion != null)
                    sb.AppendLine($"    ForceLocomotion (6 point tracking): {autoLocomotion.boolValue}");
            }

            // Expressions
            var expressionParams = so.FindProperty("expressionParameters");
            sb.AppendLine($"  ExpressionParameters: {FormatAssetRef(expressionParams?.objectReferenceValue)}");

            var expressionsMenu = so.FindProperty("expressionsMenu");
            sb.AppendLine($"  ExpressionsMenu: {FormatAssetRef(expressionsMenu?.objectReferenceValue)}");

            // Colliders
            string[] colliderNames =
            {
                "collider_head", "collider_torso",
                "collider_handL", "collider_handR",
                "collider_footL", "collider_footR",
                "collider_fingerIndexL", "collider_fingerIndexR",
                "collider_fingerMiddleL", "collider_fingerMiddleR",
                "collider_fingerRingL", "collider_fingerRingR",
                "collider_fingerLittleL", "collider_fingerLittleR",
            };
            bool anyCollider = colliderNames.Any(n => so.FindProperty(n) != null);
            if (anyCollider)
            {
                sb.AppendLine("  Colliders:");
                foreach (var name in colliderNames)
                {
                    var prop = so.FindProperty(name);
                    if (prop == null) continue;
                    var stateProp = prop.FindPropertyRelative("state");
                    string stateStr = stateProp != null ? EnumDisplayName(stateProp) : "?";
                    sb.AppendLine($"    {name.Substring("collider_".Length)}: {stateStr}");
                }
            }

            // Unity Version & Rig Type
            var unityVersionProp = so.FindProperty("unityVersion");
            if (unityVersionProp != null && !string.IsNullOrEmpty(unityVersionProp.stringValue))
                sb.AppendLine($"  UnityVersion: {unityVersionProp.stringValue}");

            var animator = go.GetComponent<Animator>();
            if (animator != null && animator.avatar != null)
            {
                string rigType = animator.avatar.isHuman ? "Humanoid" : (animator.avatar.isValid ? "Generic" : "Invalid");
                sb.AppendLine($"  RigType: {rigType}");
            }
            else
            {
                sb.AppendLine("  RigType: (no Animator/Avatar)");
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendAnimLayers(StringBuilder sb, SerializedProperty layers, string label)
        {
            if (layers == null || !layers.isArray) return;
            sb.AppendLine($"  {label} ({layers.arraySize}):");
            for (int i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                var typeProp = layer.FindPropertyRelative("type");
                var isDefault = layer.FindPropertyRelative("isDefault");
                var animController = layer.FindPropertyRelative("animatorController");
                var maskProp = layer.FindPropertyRelative("mask");

                string typeName = typeProp != null ? EnumDisplayName(typeProp) : "?";

                bool hasController = animController != null && animController.objectReferenceValue != null;
                bool isDef = isDefault != null && isDefault.boolValue;
                // VRC SDK inspector convention: when isDefault=true the controller field is ignored and shown as "Default"
                string controllerName = isDef ? "Default" : (hasController ? FormatAssetRef(animController.objectReferenceValue) : "None");
                string defaultStr = isDef ? " [Default]" : "";
                string maskStr = maskProp != null && maskProp.objectReferenceValue != null ? $", mask={FormatAssetRef(maskProp.objectReferenceValue)}" : "";
                sb.AppendLine($"    {i} {typeName}: {controllerName}{defaultStr}{maskStr}");
            }
        }

        private static string EnumDisplayName(SerializedProperty prop)
        {
            if (prop == null) return "?";
            var names = prop.enumDisplayNames;
            int idx = prop.enumValueIndex;
            if (names != null && idx >= 0 && idx < names.Length) return names[idx];
            return idx.ToString();
        }

        /// Returns the scene hierarchy path of a GameObject/Component (e.g. "Avatar/Armature/Hips").
        /// AI agents need the full path to disambiguate same-named objects and to use it as input for other tools.
        private static string FormatScenePath(UnityEngine.Object obj)
        {
            if (obj == null) return "None";
            Transform t = obj is GameObject go ? go.transform : (obj as Component)?.transform;
            if (t == null) return obj.name;

            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
            {
                sb.Insert(0, "/");
                sb.Insert(0, p.name);
            }
            return sb.ToString();
        }

        /// Returns "name (Assets/.../foo.controller)" for assets, or just "name" if not an asset.
        private static string FormatAssetRef(UnityEngine.Object obj)
        {
            if (obj == null) return "None";
            var path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? obj.name : $"{obj.name} ({path})";
        }

        private static void AppendEyeLook(StringBuilder sb, SerializedObject so)
        {
            var enableEyeLook = so.FindProperty("enableEyeLook");
            if (enableEyeLook == null || !enableEyeLook.boolValue)
            {
                sb.AppendLine("  Eye Look: Disabled");
                return;
            }
            sb.AppendLine("  Eye Look: Enabled");

            var eyeSettings = so.FindProperty("customEyeLookSettings");
            if (eyeSettings == null) return;

            // Eye Movement sliders
            var eyeMovement = eyeSettings.FindPropertyRelative("eyeMovement");
            if (eyeMovement != null)
            {
                var excitement = eyeMovement.FindPropertyRelative("excitement");
                var confidence = eyeMovement.FindPropertyRelative("confidence");
                sb.AppendLine("  Eye Movement:");
                if (excitement != null)
                    sb.AppendLine($"    Calm ←→ Excited: {excitement.floatValue:F1}  (0=Calm, 1=Excited)");
                if (confidence != null)
                    sb.AppendLine($"    Shy ←→ Confident: {confidence.floatValue:F1}  (0=Shy, 1=Confident)");
            }

            // Eye transforms
            var leftEye = eyeSettings.FindPropertyRelative("leftEye");
            var rightEye = eyeSettings.FindPropertyRelative("rightEye");
            sb.AppendLine("  Eye Transforms:");
            sb.AppendLine($"    Left Eye: {FormatScenePath(leftEye?.objectReferenceValue)}");
            sb.AppendLine($"    Right Eye: {FormatScenePath(rightEye?.objectReferenceValue)}");

            // Eyelid type — use enumDisplayNames to avoid hard-coding enum order (VRC SDK: None, Bones, Blendshapes)
            var eyelidType = eyeSettings.FindPropertyRelative("eyelidType");
            if (eyelidType != null)
                sb.AppendLine($"  Eyelid Type: {EnumDisplayName(eyelidType)}");

            // Eyelid mesh & blendshapes (resolve names from sharedMesh)
            var eyelidsMesh = eyeSettings.FindPropertyRelative("eyelidsSkinnedMesh");
            SkinnedMeshRenderer eyelidsSmr = null;
            if (eyelidsMesh != null && eyelidsMesh.objectReferenceValue != null)
            {
                eyelidsSmr = eyelidsMesh.objectReferenceValue as SkinnedMeshRenderer;
                sb.AppendLine($"  Eyelids Mesh: {FormatScenePath(eyelidsMesh.objectReferenceValue)}");
            }

            var eyelidsBlendshapes = eyeSettings.FindPropertyRelative("eyelidsBlendshapes");
            if (eyelidsBlendshapes != null && eyelidsBlendshapes.isArray && eyelidsBlendshapes.arraySize > 0)
            {
                string[] labels = { "Blink", "LookingUp", "LookingDown" };
                sb.AppendLine("  Eyelid Blendshapes:");
                Mesh sharedMesh = eyelidsSmr != null ? eyelidsSmr.sharedMesh : null;
                for (int i = 0; i < eyelidsBlendshapes.arraySize && i < labels.Length; i++)
                {
                    int blendIndex = eyelidsBlendshapes.GetArrayElementAtIndex(i).intValue;
                    string blendName = "?";
                    if (sharedMesh != null && blendIndex >= 0 && blendIndex < sharedMesh.blendShapeCount)
                        blendName = sharedMesh.GetBlendShapeName(blendIndex);
                    sb.AppendLine($"    {labels[i]}: {blendName} (index {blendIndex})");
                }
            }

            // Rotation states (linked + euler values for left/right)
            foreach (string stateName in new[] { "eyesLookingStraight", "eyesLookingUp", "eyesLookingDown", "eyesLookingLeft", "eyesLookingRight" })
            {
                var state = eyeSettings.FindPropertyRelative(stateName);
                if (state == null) continue;
                var linked = state.FindPropertyRelative("linked");
                var leftQ = state.FindPropertyRelative("left");
                var rightQ = state.FindPropertyRelative("right");
                string leftEuler = leftQ != null ? FormatQuaternionEuler(leftQ.quaternionValue) : "?";
                string rightEuler = rightQ != null ? FormatQuaternionEuler(rightQ.quaternionValue) : "?";
                bool isLinked = linked != null && linked.boolValue;
                if (isLinked)
                    sb.AppendLine($"  {stateName}: linked=true, euler={leftEuler}");
                else
                    sb.AppendLine($"  {stateName}: linked=false, left={leftEuler}, right={rightEuler}");
            }
        }

        private static string FormatQuaternionEuler(Quaternion q)
        {
            var e = q.eulerAngles;
            // Normalize to (-180, 180] for readability
            if (e.x > 180f) e.x -= 360f;
            if (e.y > 180f) e.y -= 360f;
            if (e.z > 180f) e.z -= 360f;
            return $"({e.x:F4}, {e.y:F4}, {e.z:F4})";
        }

        [AgentTool("Set Eye Movement personality on VRCAvatarDescriptor. excitement: 0=Calm (less blinking) to 1=Excited (more blinking). confidence: 0=Shy (avoids eye contact) to 1=Confident (holds eye contact). Values are rounded to 0.1. Pass -1 to leave unchanged. Example: ConfigureVRCEyeMovement(\"MyAvatar\", 0.3, 0.7) for a calm and confident character.")]
        public static string ConfigureVRCEyeMovement(string avatarRootName, float excitement = -1f, float confidence = -1f)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);

            var enableEyeLook = so.FindProperty("enableEyeLook");
            if (enableEyeLook == null || !enableEyeLook.boolValue)
                return "Error: Eye Look is not enabled on this avatar. Enable it in the VRC Avatar Descriptor inspector first.";

            var eyeSettings = so.FindProperty("customEyeLookSettings");
            if (eyeSettings == null) return "Error: customEyeLookSettings not found.";

            var eyeMovement = eyeSettings.FindPropertyRelative("eyeMovement");
            if (eyeMovement == null) return "Error: eyeMovement property not found.";

            var excitementProp = eyeMovement.FindPropertyRelative("excitement");
            var confidenceProp = eyeMovement.FindPropertyRelative("confidence");
            if (excitementProp == null || confidenceProp == null) return "Error: Eye movement properties not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Eye Movement on '{avatarRootName}':");

            int changed = 0;

            if (excitement >= 0f)
            {
                if (excitement > 1f) return "Error: excitement must be between 0 and 1.";
                float rounded = Mathf.Round(excitement * 10f) * 0.1f;
                float prev = excitementProp.floatValue;
                excitementProp.floatValue = rounded;
                sb.AppendLine($"  Calm ←→ Excited: {prev:F1} → {rounded:F1}");
                changed++;
            }
            else
            {
                sb.AppendLine($"  Calm ←→ Excited: {excitementProp.floatValue:F1} (unchanged)");
            }

            if (confidence >= 0f)
            {
                if (confidence > 1f) return "Error: confidence must be between 0 and 1.";
                float rounded = Mathf.Round(confidence * 10f) * 0.1f;
                float prev = confidenceProp.floatValue;
                confidenceProp.floatValue = rounded;
                sb.AppendLine($"  Shy ←→ Confident: {prev:F1} → {rounded:F1}");
                changed++;
            }
            else
            {
                sb.AppendLine($"  Shy ←→ Confident: {confidenceProp.floatValue:F1} (unchanged)");
            }

            if (changed > 0)
            {
                Undo.RecordObject(descriptor, "Configure Eye Movement");
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(descriptor);
                sb.AppendLine($"Updated {changed} setting(s).");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all VRCPhysBone paths under an avatar (overview). Use InspectVRCPhysBone for full per-bone details.")]
        public static string ListVRCPhysBones(string avatarRootName)
        {
            var physBoneType = FindVrcType(VrcPhysBoneTypeName);
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
            var physBoneType = FindVrcType(VrcPhysBoneTypeName);
            if (physBoneType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var physBone = go.GetComponent(physBoneType);
            if (physBone == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            var so = new SerializedObject(physBone);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCPhysBone on '{FormatScenePath(physBone)}':");

            // Header
            var version = so.FindProperty("version");
            if (version != null)
                sb.AppendLine($"  Version: {EnumDisplayName(version)}");

            // Transforms
            sb.AppendLine("  --- Transforms ---");
            var rootTransform = so.FindProperty("rootTransform");
            sb.AppendLine($"    RootTransform: {(rootTransform?.objectReferenceValue != null ? FormatScenePath(rootTransform.objectReferenceValue) : "(self)")}");

            var ignoreTransforms = so.FindProperty("ignoreTransforms");
            if (ignoreTransforms != null && ignoreTransforms.isArray)
            {
                sb.AppendLine($"    IgnoreTransforms: {ignoreTransforms.arraySize}");
                for (int i = 0; i < ignoreTransforms.arraySize; i++)
                {
                    var t = ignoreTransforms.GetArrayElementAtIndex(i).objectReferenceValue;
                    sb.AppendLine($"      [{i}] {(t != null ? FormatScenePath(t) : "None")}");
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
                sb.AppendLine($"    MultiChildType: {EnumDisplayName(multiChildType)}");

            // Forces
            sb.AppendLine("  --- Forces ---");
            var integrationType = so.FindProperty("integrationType");
            bool isAdvanced = integrationType != null && integrationType.intValue != 0;
            if (integrationType != null)
                sb.AppendLine($"    IntegrationType: {EnumDisplayName(integrationType)}");
            AppendFloatProperty(sb, so, "pull", "    Pull");
            // VRC SDK reuses the `spring` field; the Inspector relabels it "Momentum" when IntegrationType=Advanced
            AppendFloatProperty(sb, so, "spring", isAdvanced ? "    Momentum" : "    Spring");
            AppendFloatProperty(sb, so, "stiffness", "    Stiffness");
            AppendFloatProperty(sb, so, "gravity", "    Gravity");
            AppendFloatProperty(sb, so, "gravityFalloff", "    GravityFalloff");
            var immobileType = so.FindProperty("immobileType");
            if (immobileType != null)
                sb.AppendLine($"    ImmobileType: {EnumDisplayName(immobileType)}");
            AppendFloatProperty(sb, so, "immobile", "    Immobile");

            // Limits — always emit underlying fields so AI can see persisted data even when
            // the inspector hides them due to LimitType. Append "(inactive: LimitType=None)" etc.
            sb.AppendLine("  --- Limits ---");
            var limitType = so.FindProperty("limitType");
            if (limitType != null)
                sb.AppendLine($"    LimitType: {EnumDisplayName(limitType)}");
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
                sb.AppendLine($"    AllowCollision: {EnumDisplayName(allowCollision)}");
            var colliders = so.FindProperty("colliders");
            if (colliders != null && colliders.isArray)
            {
                sb.AppendLine($"    Colliders: {colliders.arraySize}");
                for (int i = 0; i < colliders.arraySize; i++)
                {
                    var c = colliders.GetArrayElementAtIndex(i).objectReferenceValue;
                    sb.AppendLine($"      [{i}] {(c != null ? FormatScenePath(c) : "None")}");
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
                sb.AppendLine($"    AllowGrabbing: {EnumDisplayName(allowGrabbing)}");
            var allowPosing = so.FindProperty("allowPosing");
            if (allowPosing != null)
                sb.AppendLine($"    AllowPosing: {EnumDisplayName(allowPosing)}");
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

        private static void AppendBool(StringBuilder sb, SerializedObject so, string name, string label)
        {
            var p = so.FindProperty(name);
            if (p != null) sb.AppendLine($"{label}: {p.boolValue}");
        }

        private static int CountTransformsRecursive(Transform t, HashSet<Transform> exclusions)
        {
            if (exclusions.Contains(t)) return 0;
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
                count += CountTransformsRecursive(t.GetChild(i), exclusions);
            return count;
        }

        [AgentTool("List expression parameters from the VRCExpressionParameters asset assigned to the avatar (raw VRC SDK only — does NOT include NDMF/MA/VRCFury build-time additions). For the full post-build view including non-destructive contributors, use ListNDMFParameters.")]
        public static string ListVRCExpressionParameters(string avatarRootName)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var exprParamsProp = so.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return $"No ExpressionParameters assigned on '{avatarRootName}'.";

            var paramsSo = new SerializedObject(exprParamsProp.objectReferenceValue);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null || !parameters.isArray)
                return "Error: Could not read parameters array.";

            var sb = new StringBuilder();
            sb.AppendLine($"Expression Parameters on '{avatarRootName}' (asset='{exprParamsProp.objectReferenceValue.name}', {parameters.arraySize}):");

            int totalCost = 0;
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var paramName = param.FindPropertyRelative("name");
                var valueType = param.FindPropertyRelative("valueType");
                var defaultValue = param.FindPropertyRelative("defaultValue");
                var saved = param.FindPropertyRelative("saved");
                var networkSynced = param.FindPropertyRelative("networkSynced");

                string nameStr = paramName != null ? paramName.stringValue : "?";
                if (string.IsNullOrEmpty(nameStr)) continue;

                string typeStr = "?";
                int cost = 0;
                if (valueType != null)
                {
                    switch (valueType.intValue)
                    {
                        case 0: typeStr = "Int"; cost = 8; break;
                        case 1: typeStr = "Float"; cost = 8; break;
                        case 2: typeStr = "Bool"; cost = 1; break;
                        default: typeStr = $"Unknown({valueType.intValue})"; break;
                    }
                }

                bool isSynced = networkSynced != null && networkSynced.boolValue;
                if (isSynced) totalCost += cost;

                float defV = defaultValue?.floatValue ?? 0f;
                string defaultStr = typeStr == "Bool"
                    ? (defV != 0f ? "1" : "0")
                    : typeStr == "Int" ? ((int)Mathf.Round(defV)).ToString() : defV.ToString("F2");
                string savedStr = saved != null && saved.boolValue ? " Saved" : "";
                string syncedStr = isSynced ? "Synced" : "Local";

                sb.AppendLine($"  {nameStr} ({typeStr}) = {defaultStr} [{syncedStr}]{savedStr} (cost: {cost})");
            }

            sb.AppendLine($"  Static Synced Cost: {totalCost}/256 bits");
            sb.AppendLine("(Hint: NDMF/MA/VRCFury may add more parameters at build — see ListNDMFParameters.)");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect VRC Expressions Menu structure recursively (controls, submenus).")]
        public static string InspectVRCExpressionsMenu(string avatarRootName)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found. Ensure VRChat Avatar SDK is installed.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var menuProp = so.FindProperty("expressionsMenu");
            if (menuProp == null || menuProp.objectReferenceValue == null)
                return $"No ExpressionsMenu assigned on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Expressions Menu for '{avatarRootName}':");
            BuildMenuTree(sb, menuProp.objectReferenceValue, 0, 3);

            return sb.ToString().TrimEnd();
        }

        private static void BuildMenuTree(StringBuilder sb, UnityEngine.Object menuObj, int depth, int maxDepth)
        {
            if (menuObj == null || depth > maxDepth) return;

            string indent = new string(' ', depth * 2 + 2);
            var menuSo = new SerializedObject(menuObj);
            var controls = menuSo.FindProperty("controls");

            if (controls == null || !controls.isArray) return;

            for (int i = 0; i < controls.arraySize; i++)
            {
                var control = controls.GetArrayElementAtIndex(i);
                var controlName = control.FindPropertyRelative("name");
                var controlType = control.FindPropertyRelative("type");
                var parameterName = control.FindPropertyRelative("parameter");

                string nameStr = controlName != null ? controlName.stringValue : "?";
                string typeStr = "?";
                if (controlType != null)
                {
                    switch (controlType.intValue)
                    {
                        case 101: typeStr = "Button"; break;
                        case 102: typeStr = "Toggle"; break;
                        case 103: typeStr = "SubMenu"; break;
                        case 201: typeStr = "TwoAxisPuppet"; break;
                        case 202: typeStr = "FourAxisPuppet"; break;
                        case 203: typeStr = "RadialPuppet"; break;
                        default: typeStr = $"Type({controlType.intValue})"; break;
                    }
                }

                string paramStr = "";
                if (parameterName != null)
                {
                    var paramNameProp = parameterName.FindPropertyRelative("name");
                    if (paramNameProp != null && !string.IsNullOrEmpty(paramNameProp.stringValue))
                        paramStr = $" param={paramNameProp.stringValue}";
                }

                sb.AppendLine($"{indent}[{typeStr}] {nameStr}{paramStr}");

                // Recurse into submenus
                if (controlType != null && controlType.intValue == 103)
                {
                    var subMenu = control.FindPropertyRelative("subMenu");
                    if (subMenu != null && subMenu.objectReferenceValue != null)
                    {
                        BuildMenuTree(sb, subMenu.objectReferenceValue, depth + 1, maxDepth);
                    }
                }
            }
        }

        // Performance stats moved to VRChatPerformanceTools.cs

        [AgentTool("Configure VRCPhysBone parameters (full inspector parity). Sentinels for 'unchanged': float=-999, int=-1, string=null. Forces: pull, spring (= 'Momentum' when integrationType=Advanced), stiffness, gravity, gravityFalloff, immobile, integrationType (0=Simplified,1=Advanced), immobileType (0=All,1=World). Limits: limitType (0=None,1=Angle,2=Hinge,3=Polar), maxAngleX, maxAngleZ, limitRotation ('x,y,z' euler). Collision: radius, allowCollision (Permission: 0=True,1=False,2=Other). Stretch&Squish: stretchMotion, maxStretch, maxSquish. Grab&Pose: allowGrabbing (Permission), allowPosing (Permission), grabMovement, snapToHand (0=false,1=true). Transforms: multiChildType (0=Ignore,1=First,2=Average), ignoreOtherPhysBones (0=false,1=true), endpointPosition ('x,y,z'). Options: isAnimated (0=false,1=true), resetWhenDisabled (0=false,1=true), parameter.")]
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
            var physBoneType = FindVrcType(VrcPhysBoneTypeName);
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

        private static int SetFloatIfChanged(SerializedObject so, StringBuilder sb, string propName, float value)
        {
            if (value == -999) return 0;

            var prop = so.FindProperty(propName);
            if (prop == null) return 0;

            prop.floatValue = value;
            sb.AppendLine($"  {propName}: {value:F3}");
            return 1;
        }

        /// <summary>
        /// Detect the dominant Write Defaults setting from an existing AnimatorController.
        /// Returns true if majority of states use WD=ON, false otherwise.
        /// </summary>
        internal static bool DetectWriteDefaults(AnimatorController controller)
        {
            int wdOn = 0, wdOff = 0;
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var childState in layer.stateMachine.states)
                {
                    if (childState.state.writeDefaultValues)
                        wdOn++;
                    else
                        wdOff++;
                }
            }
            return wdOn >= wdOff; // Default to ON if equal or no states
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

        private static void AppendFloatProperty(StringBuilder sb, SerializedObject so, string propName, string label)
        {
            var prop = so.FindProperty(propName);
            if (prop != null)
                sb.AppendLine($"{label}: {prop.floatValue:F3}");
        }

        internal static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return root.name;

            var path = new StringBuilder(target.name);
            var current = target.parent;
            while (current != null && current != root)
            {
                path.Insert(0, current.name + "/");
                current = current.parent;
            }
            return path.ToString();
        }


        /// <summary>
        /// InsertArrayElementAtIndex で追加したメニューコントロールを初期化。
        /// 前の要素からコピーされたデータをクリアする。
        /// </summary>
        private static void ClearMenuControl(SerializedProperty control)
        {
            control.FindPropertyRelative("name").stringValue = "";
            control.FindPropertyRelative("type").intValue = 102; // default to Toggle
            control.FindPropertyRelative("value").floatValue = 1f;

            var icon = control.FindPropertyRelative("icon");
            if (icon != null) icon.objectReferenceValue = null;

            var subMenu = control.FindPropertyRelative("subMenu");
            if (subMenu != null) subMenu.objectReferenceValue = null;

            var parameter = control.FindPropertyRelative("parameter");
            if (parameter != null)
            {
                var paramName = parameter.FindPropertyRelative("name");
                if (paramName != null) paramName.stringValue = "";
            }

            // Clear arrays (subParameters, labels) to avoid inheriting from previous element
            var subParameters = control.FindPropertyRelative("subParameters");
            if (subParameters != null && subParameters.isArray)
                subParameters.ClearArray();

            var labels = control.FindPropertyRelative("labels");
            if (labels != null && labels.isArray)
                labels.ClearArray();
        }

        // ─── Expression Parameter / Menu / Toggle Tools ───

        [AgentTool("Add a parameter to VRCExpressionParameters. type: Bool, Int, or Float. saved=true persists between sessions. synced=true syncs to other players.")]
        public static string AddVRCExpressionParameter(string avatarRootName, string paramName, string type = "Bool", float defaultValue = 0f, bool saved = true, bool synced = true)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var exprParamsProp = so.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return $"Error: No ExpressionParameters asset assigned on '{avatarRootName}'. Assign one first.";

            var paramsObj = exprParamsProp.objectReferenceValue;
            var paramsSo = new SerializedObject(paramsObj);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null || !parameters.isArray)
                return "Error: Could not read parameters array.";

            // Parse type
            int valueType;
            switch (type.ToLower())
            {
                case "bool": valueType = 2; break;
                case "int": valueType = 0; break;
                case "float": valueType = 1; break;
                default: return $"Error: Unknown type '{type}'. Valid: Bool, Int, Float.";
            }

            // Check if parameter already exists
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var existingName = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (existingName != null && existingName.stringValue == paramName)
                    return $"Info: Parameter '{paramName}' already exists in ExpressionParameters.";
            }

            Undo.RecordObject(paramsObj, "Add Expression Parameter");

            int newIndex = parameters.arraySize;
            parameters.InsertArrayElementAtIndex(newIndex);
            var newParam = parameters.GetArrayElementAtIndex(newIndex);
            newParam.FindPropertyRelative("name").stringValue = paramName;
            newParam.FindPropertyRelative("valueType").intValue = valueType;
            newParam.FindPropertyRelative("defaultValue").floatValue = defaultValue;
            newParam.FindPropertyRelative("saved").boolValue = saved;
            newParam.FindPropertyRelative("networkSynced").boolValue = synced;

            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsObj);

            int cost = valueType == 2 ? 1 : 8;
            return $"Success: Added parameter '{paramName}' ({type}, default={defaultValue}, saved={saved}, synced={synced}, cost={cost}bit) to ExpressionParameters.";
        }

        [AgentTool("Add a Toggle control to VRC Expressions Menu. paramName must match an ExpressionParameters entry. subMenuPath: if specified, adds to a submenu (creates if needed). value is the activation value (default 1).")]
        public static string AddVRCExpressionsMenuToggle(string avatarRootName, string controlName, string paramName, float value = 1f, string subMenuPath = "")
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var menuProp = so.FindProperty("expressionsMenu");
            if (menuProp == null || menuProp.objectReferenceValue == null)
                return $"Error: No ExpressionsMenu asset assigned on '{avatarRootName}'. Assign one first.";

            var targetMenu = menuProp.objectReferenceValue;

            // Navigate to submenu if specified
            if (!string.IsNullOrEmpty(subMenuPath))
            {
                var parts = subMenuPath.Split('/');
                foreach (var part in parts)
                {
                    var menuSo = new SerializedObject(targetMenu);
                    var controls = menuSo.FindProperty("controls");
                    if (controls == null) return "Error: Could not read menu controls.";

                    UnityEngine.Object foundSubMenu = null;
                    for (int i = 0; i < controls.arraySize; i++)
                    {
                        var ctrl = controls.GetArrayElementAtIndex(i);
                        var ctrlName = ctrl.FindPropertyRelative("name");
                        var ctrlType = ctrl.FindPropertyRelative("type");
                        if (ctrlName != null && ctrlName.stringValue == part && ctrlType != null && ctrlType.intValue == 103)
                        {
                            var subMenu = ctrl.FindPropertyRelative("subMenu");
                            if (subMenu != null && subMenu.objectReferenceValue != null)
                                foundSubMenu = subMenu.objectReferenceValue;
                        }
                    }

                    if (foundSubMenu == null)
                        return $"Error: Submenu '{part}' not found in menu. Create it first or omit subMenuPath.";
                    targetMenu = foundSubMenu;
                }
            }

            var targetMenuSo = new SerializedObject(targetMenu);
            var menuControls = targetMenuSo.FindProperty("controls");
            if (menuControls == null) return "Error: Could not read menu controls.";

            if (menuControls.arraySize >= 8)
                return "Error: Menu already has 8 controls (VRChat maximum). Use a submenu.";

            // Check if control already exists
            for (int i = 0; i < menuControls.arraySize; i++)
            {
                var existingName = menuControls.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (existingName != null && existingName.stringValue == controlName)
                    return $"Info: Control '{controlName}' already exists in the menu.";
            }

            Undo.RecordObject(targetMenu, "Add Expressions Menu Toggle");

            int newIndex = menuControls.arraySize;
            menuControls.InsertArrayElementAtIndex(newIndex);
            var newControl = menuControls.GetArrayElementAtIndex(newIndex);
            ClearMenuControl(newControl);
            newControl.FindPropertyRelative("name").stringValue = controlName;
            newControl.FindPropertyRelative("type").intValue = 102; // Toggle
            newControl.FindPropertyRelative("value").floatValue = value;

            // Set parameter
            var paramProp = newControl.FindPropertyRelative("parameter");
            if (paramProp != null)
            {
                var paramNameProp = paramProp.FindPropertyRelative("name");
                if (paramNameProp != null)
                    paramNameProp.stringValue = paramName;
            }

            targetMenuSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetMenu);

            return $"Success: Added Toggle control '{controlName}' (param='{paramName}', value={value}) to existing Expressions Menu.";
        }

        [AgentTool("Create ON/OFF toggle animation clips for a GameObject (enable/disable). Creates two clips: one that activates and one that deactivates the object. targetPath is the path relative to avatar root (e.g. 'Sailor-Jersey' or 'Armature/Hips/Object'). Returns the created clip paths.")]
        public static string CreateToggleAnimations(string avatarRootName, string targetPath, string saveDir = "")
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null) return $"Error: GameObject '{avatarRootName}' not found.";

            // Resolve target to validate it exists
            var target = avatarRoot.transform.Find(targetPath);
            if (target == null)
            {
                // Try finding by name as direct child
                for (int i = 0; i < avatarRoot.transform.childCount; i++)
                {
                    var child = avatarRoot.transform.GetChild(i);
                    if (child.name == targetPath)
                    {
                        target = child;
                        break;
                    }
                }
                if (target == null)
                    return $"Error: Target '{targetPath}' not found under '{avatarRootName}'.";
            }

            // Determine save directory
            if (string.IsNullOrEmpty(saveDir))
                saveDir = "Assets/Animations/Toggles";
            if (!System.IO.Directory.Exists(saveDir))
                System.IO.Directory.CreateDirectory(saveDir);

            string safeName = target.name.Replace(" ", "_");
            string onPath = $"{saveDir}/{safeName}_ON.anim";
            string offPath = $"{saveDir}/{safeName}_OFF.anim";

            // Create ON clip (GameObject active = true)
            var onClip = new AnimationClip();
            onClip.name = $"{safeName}_ON";
            var onBinding = new EditorCurveBinding
            {
                path = targetPath,
                type = typeof(GameObject),
                propertyName = "m_IsActive"
            };
            var onCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f / 60f, 1f));
            AnimationUtility.SetEditorCurve(onClip, onBinding, onCurve);
            AssetDatabase.CreateAsset(onClip, onPath);

            // Create OFF clip (GameObject active = false)
            var offClip = new AnimationClip();
            offClip.name = $"{safeName}_OFF";
            var offBinding = new EditorCurveBinding
            {
                path = targetPath,
                type = typeof(GameObject),
                propertyName = "m_IsActive"
            };
            var offCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f / 60f, 0f));
            AnimationUtility.SetEditorCurve(offClip, offBinding, offCurve);
            AssetDatabase.CreateAsset(offClip, offPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return $"Success: Created toggle animations:\n  ON: {onPath}\n  OFF: {offPath}\nUse these with an FX Animator layer to toggle '{target.name}'.";
        }

        [AgentTool("Set up a complete object toggle on an avatar: creates animations, adds FX layer with states/transitions, adds expression parameter, and adds menu control. This is the all-in-one tool for object toggles. defaultOn=true means the object is visible by default.")]
        public static string SetupObjectToggle(string avatarRootName, string targetPath, string toggleName = "", bool defaultOn = true, string saveDir = "")
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null) return $"Error: GameObject '{avatarRootName}' not found.";

            // Resolve target
            var target = avatarRoot.transform.Find(targetPath);
            if (target == null)
            {
                for (int i = 0; i < avatarRoot.transform.childCount; i++)
                {
                    var child = avatarRoot.transform.GetChild(i);
                    if (child.name == targetPath)
                    {
                        target = child;
                        targetPath = child.name;
                        break;
                    }
                }
                if (target == null)
                    return $"Error: Target '{targetPath}' not found under '{avatarRootName}'.";
            }

            if (string.IsNullOrEmpty(toggleName))
                toggleName = target.name.Replace(" ", "_").Replace("-", "_");

            // Confirmation
            if (!AgentSettings.RequestConfirmation(
                "オブジェクトトグルのセットアップ",
                $"'{targetPath}' をExpression Menuでトグルできるようにします。\n" +
                $"パラメータ名: {toggleName}\n" +
                $"デフォルト: {(defaultOn ? "ON (表示)" : "OFF (非表示)")}\n\n" +
                "以下を既存アセットに追加します:\n" +
                "- ON/OFFアニメーションクリップ（新規作成）\n" +
                "- 既存FXコントローラーにレイヤー追加\n" +
                "- 既存Expression Parametersにパラメータ追加\n" +
                "- 既存Expression Menuにトグル追加"))
                return "Cancelled: User denied the operation.";

            var sb = new StringBuilder();
            sb.AppendLine($"Setting up object toggle for '{targetPath}':");

            // Step 1: Create toggle animations
            if (string.IsNullOrEmpty(saveDir))
                saveDir = "Assets/Animations/Toggles";
            if (!System.IO.Directory.Exists(saveDir))
                System.IO.Directory.CreateDirectory(saveDir);

            string safeName = target.name.Replace(" ", "_");
            string onPath = $"{saveDir}/{safeName}_ON.anim";
            string offPath = $"{saveDir}/{safeName}_OFF.anim";

            var onClip = new AnimationClip { name = $"{safeName}_ON" };
            var onBinding = new EditorCurveBinding { path = targetPath, type = typeof(GameObject), propertyName = "m_IsActive" };
            AnimationUtility.SetEditorCurve(onClip, onBinding, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f / 60f, 1f)));
            AssetDatabase.CreateAsset(onClip, onPath);

            var offClip = new AnimationClip { name = $"{safeName}_OFF" };
            var offBinding = new EditorCurveBinding { path = targetPath, type = typeof(GameObject), propertyName = "m_IsActive" };
            AnimationUtility.SetEditorCurve(offClip, offBinding, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f / 60f, 0f)));
            AssetDatabase.CreateAsset(offClip, offPath);

            sb.AppendLine($"  [1/4] Created animations: {onPath}, {offPath}");

            // Step 2: Get FX controller from avatar descriptor
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) { sb.AppendLine("  Warning: VRChat SDK not found. Skipping FX/Param/Menu setup."); return sb.ToString().TrimEnd(); }

            var descriptor = avatarRoot.GetComponent(descriptorType);
            if (descriptor == null) { sb.AppendLine("  Warning: No VRCAvatarDescriptor. Skipping FX/Param/Menu setup."); return sb.ToString().TrimEnd(); }

            var descriptorSo = new SerializedObject(descriptor);

            // Find FX layer controller
            AnimatorController fxController = null;
            var baseLayers = descriptorSo.FindProperty("baseAnimationLayers");
            if (baseLayers != null && baseLayers.isArray)
            {
                // FX is typically index 4 (Base=0, Additive=1, Gesture=2, Action=3, FX=4)
                for (int i = 0; i < baseLayers.arraySize; i++)
                {
                    var layer = baseLayers.GetArrayElementAtIndex(i);
                    var animController = layer.FindPropertyRelative("animatorController");
                    if (animController != null && animController.objectReferenceValue is AnimatorController ac)
                    {
                        // Check if this is the FX layer (index 4 or the last one)
                        var layerType = layer.FindPropertyRelative("type");
                        if (layerType != null && layerType.intValue == 5) // FX = 5
                        {
                            fxController = ac;
                            break;
                        }
                    }
                }
            }

            if (fxController == null)
            {
                sb.AppendLine("  Warning: FX AnimatorController not found. Skipping layer setup.");
            }
            else
            {
                // Add parameter to FX controller
                if (!fxController.parameters.Any(p => p.name == toggleName))
                {
                    Undo.RecordObject(fxController, "Add Toggle Parameter");
                    fxController.AddParameter(toggleName, AnimatorControllerParameterType.Bool);

                    // Set default value
                    var fxParams = fxController.parameters;
                    var lastParam = fxParams.Last();
                    lastParam.defaultBool = defaultOn;
                    fxController.parameters = fxParams;
                }

                // Add layer
                string layerName = $"Toggle_{toggleName}";
                if (!fxController.layers.Any(l => l.name == layerName))
                {
                    // Detect Write Defaults setting from existing layers
                    bool useWriteDefaults = DetectWriteDefaults(fxController);

                    Undo.RecordObject(fxController, "Add Toggle Layer");
                    fxController.AddLayer(layerName);

                    // Set layer weight to 1
                    var layers = fxController.layers;
                    var newLayer = layers[layers.Length - 1];
                    newLayer.defaultWeight = 1f;
                    fxController.layers = layers;

                    var sm = fxController.layers[fxController.layers.Length - 1].stateMachine;
                    Undo.RecordObject(sm, "Setup Toggle States");

                    // Add ON/OFF states (match existing WD setting)
                    var onState = sm.AddState("ON");
                    onState.motion = onClip;
                    onState.writeDefaultValues = useWriteDefaults;

                    var offState = sm.AddState("OFF");
                    offState.motion = offClip;
                    offState.writeDefaultValues = useWriteDefaults;

                    // Set default state
                    sm.defaultState = defaultOn ? onState : offState;

                    // Add transitions
                    var toOn = offState.AddTransition(onState);
                    toOn.hasExitTime = false;
                    toOn.duration = 0f;
                    toOn.AddCondition(AnimatorConditionMode.If, 0, toggleName);

                    var toOff = onState.AddTransition(offState);
                    toOff.hasExitTime = false;
                    toOff.duration = 0f;
                    toOff.AddCondition(AnimatorConditionMode.IfNot, 0, toggleName);
                }

                EditorUtility.SetDirty(fxController);
                sb.AppendLine($"  [2/4] Added FX layer '{layerName}' with ON/OFF states and transitions.");
            }

            // Step 3: Add Expression Parameter
            var exprParamsProp = descriptorSo.FindProperty("expressionParameters");
            if (exprParamsProp != null && exprParamsProp.objectReferenceValue != null)
            {
                var paramsObj = exprParamsProp.objectReferenceValue;
                var paramsSo = new SerializedObject(paramsObj);
                var parameters = paramsSo.FindProperty("parameters");

                bool paramExists = false;
                if (parameters != null && parameters.isArray)
                {
                    for (int i = 0; i < parameters.arraySize; i++)
                    {
                        var existingName = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                        if (existingName != null && existingName.stringValue == toggleName)
                        { paramExists = true; break; }
                    }

                    if (!paramExists)
                    {
                        Undo.RecordObject(paramsObj, "Add Expression Parameter");
                        int idx = parameters.arraySize;
                        parameters.InsertArrayElementAtIndex(idx);
                        var newParam = parameters.GetArrayElementAtIndex(idx);
                        newParam.FindPropertyRelative("name").stringValue = toggleName;
                        newParam.FindPropertyRelative("valueType").intValue = 2; // Bool
                        newParam.FindPropertyRelative("defaultValue").floatValue = defaultOn ? 1f : 0f;
                        newParam.FindPropertyRelative("saved").boolValue = true;
                        newParam.FindPropertyRelative("networkSynced").boolValue = true;
                        paramsSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(paramsObj);
                    }
                }
                sb.AppendLine($"  [3/4] {(paramExists ? "Parameter already exists" : "Added Expression Parameter")} '{toggleName}' (Bool, saved, synced).");
            }
            else
            {
                sb.AppendLine("  [3/4] Warning: No ExpressionParameters asset assigned. Skipping.");
            }

            // Step 4: Add Menu Toggle
            var menuProp = descriptorSo.FindProperty("expressionsMenu");
            if (menuProp != null && menuProp.objectReferenceValue != null)
            {
                var menuObj = menuProp.objectReferenceValue;
                var menuSo = new SerializedObject(menuObj);
                var controls = menuSo.FindProperty("controls");

                bool controlExists = false;
                if (controls != null && controls.isArray)
                {
                    for (int i = 0; i < controls.arraySize; i++)
                    {
                        var existingName = controls.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                        if (existingName != null && existingName.stringValue == toggleName)
                        { controlExists = true; break; }
                    }

                    if (!controlExists && controls.arraySize < 8)
                    {
                        Undo.RecordObject(menuObj, "Add Menu Toggle");
                        int idx = controls.arraySize;
                        controls.InsertArrayElementAtIndex(idx);
                        var newControl = controls.GetArrayElementAtIndex(idx);
                        ClearMenuControl(newControl);
                        newControl.FindPropertyRelative("name").stringValue = toggleName;
                        newControl.FindPropertyRelative("type").intValue = 102; // Toggle
                        newControl.FindPropertyRelative("value").floatValue = 1f;

                        var paramProp = newControl.FindPropertyRelative("parameter");
                        if (paramProp != null)
                        {
                            var paramNameProp = paramProp.FindPropertyRelative("name");
                            if (paramNameProp != null) paramNameProp.stringValue = toggleName;
                        }

                        menuSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(menuObj);
                    }
                }

                if (controlExists)
                    sb.AppendLine($"  [4/4] Menu control '{toggleName}' already exists.");
                else if (controls != null && controls.arraySize >= 8)
                    sb.AppendLine("  [4/4] Warning: Menu is full (8 controls max). Add to a submenu manually.");
                else
                    sb.AppendLine($"  [4/4] Added Toggle control '{toggleName}' to Expressions Menu.");
            }
            else
            {
                sb.AppendLine("  [4/4] Warning: No ExpressionsMenu asset assigned. Skipping.");
            }

            AssetDatabase.SaveAssets();
            sb.AppendLine($"\nDone! '{target.name}' can now be toggled from Expression Menu.");

            return sb.ToString().TrimEnd();
        }

        // ─── Menu Navigation Helper ───

        /// <summary>
        /// Navigate to the target menu from avatar descriptor, handling subMenuPath traversal.
        /// Returns null on error (error message set in errorMsg).
        /// </summary>
        private static UnityEngine.Object NavigateToTargetMenu(string avatarRootName, string subMenuPath, out string errorMsg)
        {
            errorMsg = null;
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) { errorMsg = "Error: VRChat SDK not found."; return null; }

            var go = FindGO(avatarRootName);
            if (go == null) { errorMsg = $"Error: GameObject '{avatarRootName}' not found."; return null; }

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) { errorMsg = $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'."; return null; }

            var so = new SerializedObject(descriptor);
            var menuProp = so.FindProperty("expressionsMenu");
            if (menuProp == null || menuProp.objectReferenceValue == null)
            { errorMsg = $"Error: No ExpressionsMenu asset assigned on '{avatarRootName}'. Assign one first."; return null; }

            var targetMenu = menuProp.objectReferenceValue;

            if (!string.IsNullOrEmpty(subMenuPath))
            {
                var parts = subMenuPath.Split('/');
                foreach (var part in parts)
                {
                    var menuSo = new SerializedObject(targetMenu);
                    var controls = menuSo.FindProperty("controls");
                    if (controls == null) { errorMsg = "Error: Could not read menu controls."; return null; }

                    UnityEngine.Object foundSubMenu = null;
                    for (int i = 0; i < controls.arraySize; i++)
                    {
                        var ctrl = controls.GetArrayElementAtIndex(i);
                        var ctrlName = ctrl.FindPropertyRelative("name");
                        var ctrlType = ctrl.FindPropertyRelative("type");
                        if (ctrlName != null && ctrlName.stringValue == part && ctrlType != null && ctrlType.intValue == 103)
                        {
                            var subMenu = ctrl.FindPropertyRelative("subMenu");
                            if (subMenu != null && subMenu.objectReferenceValue != null)
                                foundSubMenu = subMenu.objectReferenceValue;
                        }
                    }

                    if (foundSubMenu == null)
                    { errorMsg = $"Error: Submenu '{part}' not found in menu. Create it first with AddVRCExpressionsMenuSubMenu or omit subMenuPath."; return null; }
                    targetMenu = foundSubMenu;
                }
            }

            return targetMenu;
        }

        /// <summary>
        /// Check menu capacity and duplicate control name. Returns error message or null if OK.
        /// </summary>
        private static string ValidateMenuForNewControl(SerializedProperty menuControls, string controlName)
        {
            if (menuControls.arraySize >= 8)
                return "Error: Menu already has 8 controls (VRChat maximum). Use a submenu.";

            for (int i = 0; i < menuControls.arraySize; i++)
            {
                var existingName = menuControls.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (existingName != null && existingName.stringValue == controlName)
                    return $"Info: Control '{controlName}' already exists in the menu.";
            }

            return null;
        }

        [AgentTool("Add a Button control to VRC Expressions Menu. Button sets param while held, resets after ~1s. subMenuPath navigates to a submenu first.")]
        public static string AddVRCExpressionsMenuButton(string avatarRootName, string controlName, string paramName, float value = 1f, string subMenuPath = "")
        {
            var targetMenu = NavigateToTargetMenu(avatarRootName, subMenuPath, out string err);
            if (targetMenu == null) return err;

            var targetMenuSo = new SerializedObject(targetMenu);
            var menuControls = targetMenuSo.FindProperty("controls");
            if (menuControls == null) return "Error: Could not read menu controls.";

            string validation = ValidateMenuForNewControl(menuControls, controlName);
            if (validation != null) return validation;

            Undo.RecordObject(targetMenu, "Add Expressions Menu Button");

            int newIndex = menuControls.arraySize;
            menuControls.InsertArrayElementAtIndex(newIndex);
            var newControl = menuControls.GetArrayElementAtIndex(newIndex);
            ClearMenuControl(newControl);
            newControl.FindPropertyRelative("name").stringValue = controlName;
            newControl.FindPropertyRelative("type").intValue = 101; // Button
            newControl.FindPropertyRelative("value").floatValue = value;

            var paramProp = newControl.FindPropertyRelative("parameter");
            if (paramProp != null)
            {
                var paramNameProp = paramProp.FindPropertyRelative("name");
                if (paramNameProp != null)
                    paramNameProp.stringValue = paramName;
            }

            targetMenuSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetMenu);

            return $"Success: Added Button control '{controlName}' (param='{paramName}', value={value}) to Expressions Menu.";
        }

        [AgentTool("Add a SubMenu control to VRC Expressions Menu. Creates a new menu asset and links it. savePath: where to save the new menu asset (auto-generated if empty). subMenuPath navigates to a submenu first.")]
        public static string AddVRCExpressionsMenuSubMenu(string avatarRootName, string controlName, string savePath = "", string subMenuPath = "")
        {
            var targetMenu = NavigateToTargetMenu(avatarRootName, subMenuPath, out string err);
            if (targetMenu == null) return err;

            var targetMenuSo = new SerializedObject(targetMenu);
            var menuControls = targetMenuSo.FindProperty("controls");
            if (menuControls == null) return "Error: Could not read menu controls.";

            string validation = ValidateMenuForNewControl(menuControls, controlName);
            if (validation != null) return validation;

            // Create new VRCExpressionsMenu asset
            var menuType = FindVrcType(VrcExpressionsMenuTypeName);
            if (menuType == null) return "Error: VRCExpressionsMenu type not found.";

            if (string.IsNullOrEmpty(savePath))
            {
                string saveDir = "Assets/VRCMenus";
                if (!System.IO.Directory.Exists(saveDir))
                    System.IO.Directory.CreateDirectory(saveDir);
                savePath = $"{saveDir}/{controlName}.asset";
            }
            else
            {
                string dir = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
            }

            var newMenuAsset = ScriptableObject.CreateInstance(menuType);
            AssetDatabase.CreateAsset(newMenuAsset, savePath);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(targetMenu, "Add Expressions Menu SubMenu");

            int newIndex = menuControls.arraySize;
            menuControls.InsertArrayElementAtIndex(newIndex);
            var newControl = menuControls.GetArrayElementAtIndex(newIndex);
            ClearMenuControl(newControl);
            newControl.FindPropertyRelative("name").stringValue = controlName;
            newControl.FindPropertyRelative("type").intValue = 103; // SubMenu

            var subMenuProp = newControl.FindPropertyRelative("subMenu");
            if (subMenuProp != null)
                subMenuProp.objectReferenceValue = newMenuAsset;

            targetMenuSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetMenu);

            return $"Success: Added SubMenu control '{controlName}' linked to '{savePath}'.";
        }

        [AgentTool("Add a RadialPuppet control to VRC Expressions Menu. Controls a float parameter (0-1) with a radial slider. subMenuPath navigates to a submenu first.")]
        public static string AddVRCExpressionsMenuRadialPuppet(string avatarRootName, string controlName, string paramName, string subMenuPath = "")
        {
            var targetMenu = NavigateToTargetMenu(avatarRootName, subMenuPath, out string err);
            if (targetMenu == null) return err;

            var targetMenuSo = new SerializedObject(targetMenu);
            var menuControls = targetMenuSo.FindProperty("controls");
            if (menuControls == null) return "Error: Could not read menu controls.";

            string validation = ValidateMenuForNewControl(menuControls, controlName);
            if (validation != null) return validation;

            Undo.RecordObject(targetMenu, "Add Expressions Menu RadialPuppet");

            int newIndex = menuControls.arraySize;
            menuControls.InsertArrayElementAtIndex(newIndex);
            var newControl = menuControls.GetArrayElementAtIndex(newIndex);
            ClearMenuControl(newControl);
            newControl.FindPropertyRelative("name").stringValue = controlName;
            newControl.FindPropertyRelative("type").intValue = 203; // RadialPuppet

            // RadialPuppet uses subParameters[0].name for the parameter
            var subParameters = newControl.FindPropertyRelative("subParameters");
            if (subParameters != null && subParameters.isArray)
            {
                subParameters.InsertArrayElementAtIndex(0);
                var subParam = subParameters.GetArrayElementAtIndex(0);
                var subParamName = subParam.FindPropertyRelative("name");
                if (subParamName != null)
                    subParamName.stringValue = paramName;
            }

            targetMenuSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetMenu);

            return $"Success: Added RadialPuppet control '{controlName}' (subParam='{paramName}') to Expressions Menu.";
        }

        [AgentTool("Remove a control from VRC Expressions Menu by name. Requires user confirmation. subMenuPath navigates to a submenu first.")]
        public static string RemoveVRCExpressionsMenuControl(string avatarRootName, string controlName, string subMenuPath = "")
        {
            var targetMenu = NavigateToTargetMenu(avatarRootName, subMenuPath, out string err);
            if (targetMenu == null) return err;

            var targetMenuSo = new SerializedObject(targetMenu);
            var menuControls = targetMenuSo.FindProperty("controls");
            if (menuControls == null) return "Error: Could not read menu controls.";

            int foundIndex = -1;
            for (int i = 0; i < menuControls.arraySize; i++)
            {
                var existingName = menuControls.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (existingName != null && existingName.stringValue == controlName)
                { foundIndex = i; break; }
            }

            if (foundIndex < 0)
                return $"Error: Control '{controlName}' not found in the menu.";

            if (!AgentSettings.RequestConfirmation(
                "メニューコントロールの削除",
                $"Expression Menuから '{controlName}' を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(targetMenu, "Remove Expressions Menu Control");
            menuControls.DeleteArrayElementAtIndex(foundIndex);
            targetMenuSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetMenu);

            return $"Success: Removed control '{controlName}' from Expressions Menu.";
        }

        [AgentTool("Remove a parameter from VRCExpressionParameters by name. Requires user confirmation.")]
        public static string RemoveVRCExpressionParameter(string avatarRootName, string paramName)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var exprParamsProp = so.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
                return $"Error: No ExpressionParameters asset assigned on '{avatarRootName}'.";

            var paramsObj = exprParamsProp.objectReferenceValue;
            var paramsSo = new SerializedObject(paramsObj);
            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null || !parameters.isArray)
                return "Error: Could not read parameters array.";

            int foundIndex = -1;
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var existingName = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (existingName != null && existingName.stringValue == paramName)
                { foundIndex = i; break; }
            }

            if (foundIndex < 0)
                return $"Error: Parameter '{paramName}' not found in ExpressionParameters.";

            if (!AgentSettings.RequestConfirmation(
                "パラメータの削除",
                $"ExpressionParametersから '{paramName}' を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(paramsObj, "Remove Expression Parameter");
            parameters.DeleteArrayElementAtIndex(foundIndex);
            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsObj);

            return $"Success: Removed parameter '{paramName}' from ExpressionParameters.";
        }

        [AgentTool("Run Modular Avatar Setup Outfit on a clothing GameObject that is already placed as a child of the avatar. Automatically adds MergeArmature and MeshSettings components.")]
        public static string SetupOutfit(string avatarRootName, string outfitName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            // Strip avatar root name prefix if the LLM passed a full path (e.g. "AvatarRoot/Outfit")
            if (outfitName.StartsWith(go.name + "/"))
                outfitName = outfitName.Substring(go.name.Length + 1);

            var outfit = go.transform.Find(outfitName);
            if (outfit == null)
                return $"Error: Outfit '{outfitName}' not found under '{avatarRootName}'. Make sure the outfit is placed as a child of the avatar first using InstantiatePrefab with parentName.";

            var maErr = MAAvailability.CheckOrError();
            if (maErr != null) return maErr;

            try
            {
                MAComponentFactory.SetupOutfit(outfit.gameObject);
                return $"Success: Setup Outfit completed for '{outfitName}'. ModularAvatarMergeArmature and MeshSettings have been added.";
            }
            catch (Exception ex)
            {
                return $"Error: Setup Outfit failed: {ex.Message}";
            }
        }

        [AgentTool("Get the FX AnimatorController path from an avatar's VRCAvatarDescriptor.")]
        public static string GetVRCFXControllerPath(string avatarRootName)
        {
            var descriptorType = FindVrcType(VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";

            var so = new SerializedObject(descriptor);
            var baseLayers = so.FindProperty("baseAnimationLayers");
            if (baseLayers == null || !baseLayers.isArray)
                return "Error: Could not read baseAnimationLayers.";

            for (int i = 0; i < baseLayers.arraySize; i++)
            {
                var layer = baseLayers.GetArrayElementAtIndex(i);
                var layerType = layer.FindPropertyRelative("type");
                if (layerType != null && layerType.intValue == 5) // FX
                {
                    var animController = layer.FindPropertyRelative("animatorController");
                    if (animController != null && animController.objectReferenceValue != null)
                    {
                        string path = AssetDatabase.GetAssetPath(animController.objectReferenceValue);
                        return $"FX Controller: {path}";
                    }
                    return "Error: FX layer has no AnimatorController assigned.";
                }
            }

            return "Error: FX layer not found in baseAnimationLayers.";
        }

        // ========== PhysBone Templates ==========

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
            var physBoneType = FindVrcType(VrcPhysBoneTypeName);
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
            SetInt("allowGrabbing", tmpl.allowGrabbing ? 0 : 1); // 0=True, 1=False
            SetInt("allowPosing", tmpl.allowPosing ? 0 : 1);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(physBone);

            return $"Success: Applied '{template}' template to PhysBone on '{goName}'.\n" +
                   $"  pull={tmpl.pull}, spring={tmpl.spring}, stiffness={tmpl.stiffness}\n" +
                   $"  gravity={tmpl.gravity}, immobile={tmpl.immobile}\n" +
                   $"  limitType={tmpl.limitType}, maxAngleX={tmpl.maxAngleX}, radius={tmpl.radius}\n" +
                   $"  allowGrabbing={tmpl.allowGrabbing}, allowPosing={tmpl.allowPosing}";
        }
    }
}
