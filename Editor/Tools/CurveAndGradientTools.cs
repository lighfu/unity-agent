using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// 汎用 AnimationCurve / Gradient プロパティセッター。
    /// 任意のコンポーネントのカーブ型・グラデーション型プロパティを SerializedProperty 経由で設定する。
    /// TrailRenderer.widthCurve, ParticleSystem のカーブ設定, Gradient プロパティ等に対応。
    /// </summary>
    public static class CurveAndGradientTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // =================================================================
        // AnimationCurve
        // =================================================================

        [AgentTool(@"Set an AnimationCurve property on any component via SerializedProperty.
goName: GameObject name. componentType: component class name (e.g. 'TrailRenderer').
propertyPath: serialized property path (e.g. 'm_Parameters.widthCurve'). Use DeepInspectComponent to find paths.
keyframes: comma-separated 'time:value' pairs. Example: '0:1, 0.5:0.5, 1:0'
wrapMode: 0=Default, 1=Clamp, 2=Loop, 4=PingPong, 8=ClampForever.")]
        public static string SetCurveProperty(string goName, string componentType, string propertyPath, string keyframes,
            int preWrapMode = -1, int postWrapMode = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var comp = FindComponent(go, componentType);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found on '{componentType}'.";
            if (prop.propertyType != SerializedPropertyType.AnimationCurve)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not AnimationCurve.";

            var keys = ParseKeyframes(keyframes);
            if (keys == null) return "Error: Invalid keyframes format. Use 'time:value, time:value'.";

            Undo.RecordObject(comp, $"Set AnimationCurve {propertyPath}");

            var curve = new AnimationCurve(keys);
            if (preWrapMode >= 0) curve.preWrapMode = (WrapMode)preWrapMode;
            if (postWrapMode >= 0) curve.postWrapMode = (WrapMode)postWrapMode;

            prop.animationCurveValue = curve;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(comp);
            return $"Success: Set AnimationCurve on '{componentType}.{propertyPath}' ({keys.Length} keyframes).";
        }

        [AgentTool(@"Get the current value of an AnimationCurve property on a component. Shows all keyframes with time, value, tangents, and wrap modes.")]
        public static string GetCurveProperty(string goName, string componentType, string propertyPath)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var comp = FindComponent(go, componentType);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";
            if (prop.propertyType != SerializedPropertyType.AnimationCurve)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not AnimationCurve.";

            var curve = prop.animationCurveValue;
            var sb = new StringBuilder();
            sb.AppendLine($"AnimationCurve '{componentType}.{propertyPath}':");
            sb.AppendLine($"  PreWrapMode: {curve.preWrapMode}, PostWrapMode: {curve.postWrapMode}");
            sb.AppendLine($"  Keyframes ({curve.keys.Length}):");
            foreach (var key in curve.keys)
            {
                sb.AppendLine($"    t={key.time:F3}: value={key.value:F4}, inTangent={key.inTangent:F3}, outTangent={key.outTangent:F3}, weightedMode={key.weightedMode}");
            }

            // Also output in compact format for easy copying
            var compact = string.Join(", ", curve.keys.Select(k => $"{k.time:F2}:{k.value:F3}"));
            sb.AppendLine($"  Compact: {compact}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"List all AnimationCurve and Gradient properties on a component. Useful for discovering which properties can be set with SetCurveProperty/SetGradientProperty.")]
        public static string ListCurveAndGradientProperties(string goName, string componentType)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var comp = FindComponent(go, componentType);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.GetIterator();
            var curves = new List<string>();
            var gradients = new List<string>();

            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (prop.propertyType == SerializedPropertyType.AnimationCurve)
                {
                    var curve = prop.animationCurveValue;
                    curves.Add($"  {prop.propertyPath} ({curve.keys.Length} keys)");
                    enterChildren = false;
                }
                else if (prop.propertyType == SerializedPropertyType.Gradient)
                {
                    gradients.Add($"  {prop.propertyPath}");
                    enterChildren = false;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Curve & Gradient properties on '{componentType}' of '{goName}':");

            if (curves.Count > 0)
            {
                sb.AppendLine($"  AnimationCurve ({curves.Count}):");
                foreach (var c in curves) sb.AppendLine($"  {c}");
            }
            if (gradients.Count > 0)
            {
                sb.AppendLine($"  Gradient ({gradients.Count}):");
                foreach (var g in gradients) sb.AppendLine($"  {g}");
            }

            if (curves.Count == 0 && gradients.Count == 0)
                sb.AppendLine("  (none found)");

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Gradient
        // =================================================================

        [AgentTool(@"Set a Gradient property on any component via SerializedProperty.
goName: GameObject name. componentType: component class name.
propertyPath: serialized property path. Use ListCurveAndGradientProperties to find paths.
colorKeys: semicolon-separated 'time:r,g,b' (all 0-1). Example: '0:1,1,1;0.5:1,0,0;1:0,0,1'
alphaKeys: semicolon-separated 'time:alpha'. Example: '0:1;0.8:1;1:0'
gradientMode: 0=Blend, 1=Fixed.")]
        public static string SetGradientProperty(string goName, string componentType, string propertyPath,
            string colorKeys, string alphaKeys = "0:1;1:1", int gradientMode = 0)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var comp = FindComponent(go, componentType);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found on '{componentType}'.";
            if (prop.propertyType != SerializedPropertyType.Gradient)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not Gradient.";

            var gColorKeys = ParseGradientColorKeys(colorKeys);
            var gAlphaKeys = ParseGradientAlphaKeys(alphaKeys);
            if (gColorKeys == null) return "Error: Invalid colorKeys format. Use 'time:r,g,b;time:r,g,b'.";
            if (gAlphaKeys == null) return "Error: Invalid alphaKeys format. Use 'time:alpha;time:alpha'.";

            var gradient = new Gradient();
            gradient.mode = (GradientMode)gradientMode;
            gradient.SetKeys(gColorKeys, gAlphaKeys);

            Undo.RecordObject(comp, $"Set Gradient {propertyPath}");
            prop.gradientValue = gradient;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(comp);
            return $"Success: Set Gradient on '{componentType}.{propertyPath}' ({gColorKeys.Length} color keys, {gAlphaKeys.Length} alpha keys).";
        }

        [AgentTool("Get the current value of a Gradient property on a component. Shows all color and alpha keys.")]
        public static string GetGradientProperty(string goName, string componentType, string propertyPath)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var comp = FindComponent(go, componentType);
            if (comp == null) return $"Error: Component '{componentType}' not found on '{goName}'.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return $"Error: Property '{propertyPath}' not found.";
            if (prop.propertyType != SerializedPropertyType.Gradient)
                return $"Error: Property '{propertyPath}' is {prop.propertyType}, not Gradient.";

            var gradient = prop.gradientValue;
            var sb = new StringBuilder();
            sb.AppendLine($"Gradient '{componentType}.{propertyPath}':");
            sb.AppendLine($"  Mode: {gradient.mode}");

            sb.AppendLine($"  ColorKeys ({gradient.colorKeys.Length}):");
            foreach (var ck in gradient.colorKeys)
                sb.AppendLine($"    t={ck.time:F3}: ({ck.color.r:F2},{ck.color.g:F2},{ck.color.b:F2})");

            sb.AppendLine($"  AlphaKeys ({gradient.alphaKeys.Length}):");
            foreach (var ak in gradient.alphaKeys)
                sb.AppendLine($"    t={ak.time:F3}: {ak.alpha:F2}");

            // Compact format
            var compactColor = string.Join(";", gradient.colorKeys.Select(k => $"{k.time:F2}:{k.color.r:F2},{k.color.g:F2},{k.color.b:F2}"));
            var compactAlpha = string.Join(";", gradient.alphaKeys.Select(k => $"{k.time:F2}:{k.alpha:F2}"));
            sb.AppendLine($"  CompactColor: {compactColor}");
            sb.AppendLine($"  CompactAlpha: {compactAlpha}");

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static Component FindComponent(GameObject go, string typeName)
        {
            // Try exact match first
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    return comp;
            }

            // Try partial match
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return comp;
            }

            // Try full type name with namespace
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().FullName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return comp;
            }

            return null;
        }

        private static Keyframe[] ParseKeyframes(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var parts = input.Split(',');
            var keys = new List<Keyframe>();

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float time) ||
                    !float.TryParse(trimmed.Substring(colonIdx + 1).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    return null;

                keys.Add(new Keyframe(time, value));
            }

            return keys.Count > 0 ? keys.ToArray() : null;
        }

        private static GradientColorKey[] ParseGradientColorKeys(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var entries = input.Split(';');
            var keys = new List<GradientColorKey>();

            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float time))
                    return null;

                var rgb = trimmed.Substring(colonIdx + 1).Split(',');
                if (rgb.Length != 3) return null;
                if (!float.TryParse(rgb[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ||
                    !float.TryParse(rgb[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ||
                    !float.TryParse(rgb[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                    return null;

                keys.Add(new GradientColorKey(new Color(r, g, b), time));
            }

            return keys.Count > 0 ? keys.ToArray() : null;
        }

        private static GradientAlphaKey[] ParseGradientAlphaKeys(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var entries = input.Split(';');
            var keys = new List<GradientAlphaKey>();

            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float time) ||
                    !float.TryParse(trimmed.Substring(colonIdx + 1).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha))
                    return null;

                keys.Add(new GradientAlphaKey(alpha, time));
            }

            return keys.Count > 0 ? keys.ToArray() : null;
        }
    }
}
