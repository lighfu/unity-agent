using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AnimationClip の作成・カーブ設定ツール。
    /// モーションアニメーション、BlendShape アニメ、オブジェクトトグルなどの作成に使用。
    /// </summary>
    public static class AnimationClipTools
    {
        [AgentTool("Create a new empty AnimationClip asset. savePath should be a folder (e.g. 'Assets/Animations'). Returns the asset path. Use SetAnimationCurve to add keyframes after creation.")]
        public static string CreateAnimationClip(string clipName, string savePath = "Assets", float length = 1.0f, bool isLooping = false)
        {
            if (string.IsNullOrEmpty(clipName))
                return "Error: clipName is required.";

            // Ensure directory exists
            if (!System.IO.Directory.Exists(savePath))
                System.IO.Directory.CreateDirectory(savePath);

            string assetPath = $"{savePath}/{clipName}.anim";

            // Check for existing
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null)
                return $"Error: AnimationClip already exists at '{assetPath}'. Use a different name or path.";

            var clip = new AnimationClip();
            clip.name = clipName;

            // Set loop
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = isLooping;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();

            return $"Success: Created AnimationClip at '{assetPath}' (length={length}s, loop={isLooping}). Use SetAnimationCurve to add keyframes.";
        }

        [AgentTool(@"Add or replace an animation curve on an AnimationClip.
bonePath: relative path from Animator root to the target bone (e.g. 'Armature/Hips/Spine/Chest/Upper_Arm_R'). Use ListBones or ListHumanoidMapping to find paths. Use empty string for the root object.
property: One of:
  Transform: 'position.x','position.y','position.z','rotation.x','rotation.y','rotation.z','scale.x','scale.y','scale.z'
  BlendShape: 'blendShape.ShapeName' (on SkinnedMeshRenderer, value 0-100)
  GameObject: 'active' (1=active, 0=inactive)
keyframes: Comma-separated 'time:value' pairs. Example: '0:0, 0.5:45, 1.0:0'
  For rotation properties, values are in DEGREES (Euler angles).
  For position, values are in local space METERS.
  For blendShape, values are 0-100.
  For active, use 0 or 1.")]
        public static string SetAnimationCurve(string clipPath, string bonePath, string property, string keyframes)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return $"Error: AnimationClip not found at '{clipPath}'.";

            // Parse property to EditorCurveBinding
            if (!TryParseProperty(property, out Type componentType, out string unityPropertyName))
                return $"Error: Unknown property '{property}'. Valid: position.x/y/z, rotation.x/y/z, scale.x/y/z, blendShape.Name, active.";

            // Parse keyframes
            if (!TryParseKeyframes(keyframes, out Keyframe[] keys, out string parseError))
                return $"Error: Failed to parse keyframes: {parseError}";

            var binding = new EditorCurveBinding
            {
                path = bonePath ?? "",
                type = componentType,
                propertyName = unityPropertyName
            };

            Undo.RecordObject(clip, $"Set AnimationCurve {property}");

            var curve = new AnimationCurve(keys);
            AnimationUtility.SetEditorCurve(clip, binding, curve);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            float duration = keys.Length > 0 ? keys[keys.Length - 1].time : 0f;
            return $"Success: Set curve '{property}' on '{bonePath}' ({keys.Length} keyframes, duration={duration:F2}s).";
        }

        [AgentTool("Remove an animation curve from an AnimationClip. bonePath and property must match an existing curve.")]
        public static string RemoveAnimationCurve(string clipPath, string bonePath, string property)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return $"Error: AnimationClip not found at '{clipPath}'.";

            if (!TryParseProperty(property, out Type componentType, out string unityPropertyName))
                return $"Error: Unknown property '{property}'.";

            var binding = new EditorCurveBinding
            {
                path = bonePath ?? "",
                type = componentType,
                propertyName = unityPropertyName
            };

            Undo.RecordObject(clip, $"Remove AnimationCurve {property}");
            AnimationUtility.SetEditorCurve(clip, binding, null);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return $"Success: Removed curve '{property}' from '{bonePath}'.";
        }

        [AgentTool("Inspect an AnimationClip asset. Shows all curves with their keyframes, clip length, loop setting, and event count.")]
        public static string GetAnimationClipInfo(string clipPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return $"Error: AnimationClip not found at '{clipPath}'.";

            var sb = new StringBuilder();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            sb.AppendLine($"AnimationClip: {clip.name}");
            sb.AppendLine($"  Length: {clip.length:F3}s");
            sb.AppendLine($"  Loop: {settings.loopTime}");
            sb.AppendLine($"  FrameRate: {clip.frameRate}");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            sb.AppendLine($"  Curves ({bindings.Length}):");

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                string friendlyProp = UnityPropertyToFriendly(binding.type, binding.propertyName);
                sb.Append($"    [{binding.path}] {friendlyProp} ({curve.keys.Length} keys):");

                // Show keyframe values
                var keyStrs = curve.keys.Select(k => $"{k.time:F2}:{k.value:F2}");
                sb.AppendLine($" {string.Join(", ", keyStrs)}");
            }

            // Object reference curves (e.g. sprite swaps)
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objBindings.Length > 0)
            {
                sb.AppendLine($"  Object Reference Curves ({objBindings.Length}):");
                foreach (var binding in objBindings)
                    sb.AppendLine($"    [{binding.path}] {binding.propertyName}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Update AnimationClip settings: looping and wrap mode.")]
        public static string SetAnimationClipSettings(string clipPath, bool isLooping = true)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                return $"Error: AnimationClip not found at '{clipPath}'.";

            Undo.RecordObject(clip, "Set AnimationClip Settings");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = isLooping;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return $"Success: Updated clip settings (loop={isLooping}).";
        }

        // =================================================================
        // AnimationEvent
        // =================================================================

        [AgentTool(@"Add an AnimationEvent to an AnimationClip at a specific time.
time: time in seconds when the event fires.
functionName: name of the method to call on the receiving MonoBehaviour.
stringParameter/intParameter/floatParameter: optional parameters passed to the function.")]
        public static string AddAnimationEvent(string clipPath, float time, string functionName,
            string stringParameter = "", int intParameter = 0, float floatParameter = 0f)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) return $"Error: AnimationClip not found at '{clipPath}'.";

            Undo.RecordObject(clip, "Add AnimationEvent");

            var evt = new AnimationEvent();
            evt.time = time;
            evt.functionName = functionName;
            if (!string.IsNullOrEmpty(stringParameter)) evt.stringParameter = stringParameter;
            evt.intParameter = intParameter;
            evt.floatParameter = floatParameter;

            var events = AnimationUtility.GetAnimationEvents(clip);
            var eventList = new List<AnimationEvent>(events) { evt };
            AnimationUtility.SetAnimationEvents(clip, eventList.ToArray());

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return $"Success: Added AnimationEvent at t={time:F3}s calling '{functionName}' on '{clip.name}'.";
        }

        [AgentTool("Remove an AnimationEvent from an AnimationClip by index (0-based). Use GetAnimationClipInfo to see events.")]
        public static string RemoveAnimationEvent(string clipPath, int eventIndex)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) return $"Error: AnimationClip not found at '{clipPath}'.";

            var events = AnimationUtility.GetAnimationEvents(clip);
            if (eventIndex < 0 || eventIndex >= events.Length)
                return $"Error: Event index {eventIndex} out of range (0-{events.Length - 1}).";

            Undo.RecordObject(clip, "Remove AnimationEvent");

            var eventList = new List<AnimationEvent>(events);
            string removedName = eventList[eventIndex].functionName;
            eventList.RemoveAt(eventIndex);
            AnimationUtility.SetAnimationEvents(clip, eventList.ToArray());

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return $"Success: Removed event[{eventIndex}] '{removedName}' from '{clip.name}' ({eventList.Count} events remaining).";
        }

        [AgentTool("List all AnimationEvents on an AnimationClip with time, function name, and parameters.")]
        public static string ListAnimationEvents(string clipPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) return $"Error: AnimationClip not found at '{clipPath}'.";

            var events = AnimationUtility.GetAnimationEvents(clip);
            if (events.Length == 0) return $"No AnimationEvents on '{clip.name}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"AnimationEvents on '{clip.name}' ({events.Length}):");
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                sb.Append($"  [{i}] t={e.time:F3}s: {e.functionName}(");
                var args = new List<string>();
                if (!string.IsNullOrEmpty(e.stringParameter)) args.Add($"\"{e.stringParameter}\"");
                if (e.intParameter != 0) args.Add($"int={e.intParameter}");
                if (e.floatParameter != 0) args.Add($"float={e.floatParameter:F3}");
                if (e.objectReferenceParameter != null) args.Add($"obj={e.objectReferenceParameter.name}");
                sb.Append(string.Join(", ", args));
                sb.AppendLine(")");
            }

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Property name mapping
        // =================================================================

        private static bool TryParseProperty(string property, out Type componentType, out string unityPropertyName)
        {
            componentType = null;
            unityPropertyName = null;

            if (string.IsNullOrEmpty(property))
                return false;

            string lower = property.ToLower();

            // Transform rotation (Euler degrees)
            if (lower == "rotation.x") { componentType = typeof(Transform); unityPropertyName = "localEulerAnglesRaw.x"; return true; }
            if (lower == "rotation.y") { componentType = typeof(Transform); unityPropertyName = "localEulerAnglesRaw.y"; return true; }
            if (lower == "rotation.z") { componentType = typeof(Transform); unityPropertyName = "localEulerAnglesRaw.z"; return true; }

            // Transform position
            if (lower == "position.x") { componentType = typeof(Transform); unityPropertyName = "m_LocalPosition.x"; return true; }
            if (lower == "position.y") { componentType = typeof(Transform); unityPropertyName = "m_LocalPosition.y"; return true; }
            if (lower == "position.z") { componentType = typeof(Transform); unityPropertyName = "m_LocalPosition.z"; return true; }

            // Transform scale
            if (lower == "scale.x") { componentType = typeof(Transform); unityPropertyName = "m_LocalScale.x"; return true; }
            if (lower == "scale.y") { componentType = typeof(Transform); unityPropertyName = "m_LocalScale.y"; return true; }
            if (lower == "scale.z") { componentType = typeof(Transform); unityPropertyName = "m_LocalScale.z"; return true; }

            // GameObject active
            if (lower == "active") { componentType = typeof(GameObject); unityPropertyName = "m_IsActive"; return true; }

            // BlendShape (blendShape.ShapeName)
            if (property.StartsWith("blendShape.", StringComparison.OrdinalIgnoreCase))
            {
                componentType = typeof(SkinnedMeshRenderer);
                // Keep original casing for shape name
                unityPropertyName = "blendShape." + property.Substring("blendShape.".Length);
                return true;
            }

            return false;
        }

        private static string UnityPropertyToFriendly(Type type, string unityPropName)
        {
            if (type == typeof(Transform))
            {
                if (unityPropName.StartsWith("localEulerAnglesRaw.")) return "rotation." + unityPropName.Last();
                if (unityPropName.StartsWith("m_LocalPosition.")) return "position." + unityPropName.Last();
                if (unityPropName.StartsWith("m_LocalScale.")) return "scale." + unityPropName.Last();
            }
            if (type == typeof(GameObject) && unityPropName == "m_IsActive") return "active";
            if (type == typeof(SkinnedMeshRenderer) && unityPropName.StartsWith("blendShape.")) return unityPropName;
            return $"{type.Name}.{unityPropName}";
        }

        // =================================================================
        // Keyframe parser
        // =================================================================

        private static bool TryParseKeyframes(string keyframesStr, out Keyframe[] keys, out string error)
        {
            keys = null;
            error = null;

            if (string.IsNullOrEmpty(keyframesStr))
            {
                error = "Keyframes string is empty.";
                return false;
            }

            var parts = keyframesStr.Split(',');
            var keyList = new List<Keyframe>();

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0)
                {
                    error = $"Invalid keyframe format '{trimmed}'. Expected 'time:value'.";
                    return false;
                }

                string timeStr = trimmed.Substring(0, colonIdx).Trim();
                string valueStr = trimmed.Substring(colonIdx + 1).Trim();

                if (!float.TryParse(timeStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float time))
                {
                    error = $"Cannot parse time '{timeStr}' in keyframe '{trimmed}'.";
                    return false;
                }

                if (!float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    error = $"Cannot parse value '{valueStr}' in keyframe '{trimmed}'.";
                    return false;
                }

                keyList.Add(new Keyframe(time, value));
            }

            if (keyList.Count == 0)
            {
                error = "No valid keyframes found.";
                return false;
            }

            keys = keyList.ToArray();
            return true;
        }
    }
}
