using System;
using System.Collections.Generic;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor.MeshPaint
{
    internal enum MeshPaintOpType
    {
        Paint,
        Gradient,
        HSV,
        BrightnessContrast,
    }

    [Serializable]
    internal struct PaintOpParams
    {
        public Color color;
    }

    [Serializable]
    internal struct GradientOpParams
    {
        public Color fromColor;
        public Color toColor;
        public int directionIndex;    // index into MeshPaintOpConstants.DirectionValues
        public int blendModeIndex;    // index into MeshPaintOpConstants.BlendModeValues
        public float startT;
        public float endT;
    }

    [Serializable]
    internal struct HSVOpParams
    {
        public float hueShift;
        public float satScale;
        public float valScale;
    }

    [Serializable]
    internal struct BCOpParams
    {
        public float brightness;
        public float contrast;
    }

    internal static class MeshPaintOpConstants
    {
        public static readonly string[] DirectionValues = { "top_to_bottom", "bottom_to_top", "left_to_right", "right_to_left" };
        public static readonly string[] BlendModeValues = { "screen", "overlay", "tint", "multiply", "replace" };
    }

    /// <summary>
    /// A single staged edit in a Mesh Painter v2 session.
    /// Params are stored inline as typed structs and selected via <see cref="Type"/>.
    /// IslandScope is null/empty → whole texture; otherwise restricted to these island indices.
    /// </summary>
    internal class MeshPaintOperation
    {
        public MeshPaintOpType Type;
        public List<int> IslandScope;
        public PaintOpParams Paint;
        public GradientOpParams Gradient;
        public HSVOpParams HSV;
        public BCOpParams BC;
        public DateTime CreatedAt = DateTime.Now;

        public string ShortLabel()
        {
            switch (Type)
            {
                case MeshPaintOpType.Paint:
                    return $"Paint {ColorToHex(Paint.color)}";
                case MeshPaintOpType.Gradient:
                    return $"Gradient {ColorToHex(Gradient.fromColor)}→{ColorToHex(Gradient.toColor)}";
                case MeshPaintOpType.HSV:
                    return $"HSV H{Gradient.fromColor:F0} " +
                           $"H{HSV.hueShift:+#;-#;0} S{HSV.satScale:0.00} V{HSV.valScale:0.00}";
                case MeshPaintOpType.BrightnessContrast:
                    return $"B/C B{BC.brightness:+0.00;-0.00;0} C{BC.contrast:+0.00;-0.00;0}";
                default:
                    return Type.ToString();
            }
        }

        public string ScopeLabel()
        {
            if (IslandScope == null || IslandScope.Count == 0)
                return "[all]";
            if (IslandScope.Count <= 3)
                return "[" + string.Join(",", IslandScope) + "]";
            return $"[{IslandScope[0]},{IslandScope[1]},…+{IslandScope.Count - 2}]";
        }

        public MeshPaintOperation Clone()
        {
            return new MeshPaintOperation
            {
                Type = Type,
                IslandScope = IslandScope != null ? new List<int>(IslandScope) : null,
                Paint = Paint,
                Gradient = Gradient,
                HSV = HSV,
                BC = BC,
                CreatedAt = CreatedAt,
            };
        }

        public bool IsNoop()
        {
            switch (Type)
            {
                case MeshPaintOpType.HSV:
                    return Mathf.Approximately(HSV.hueShift, 0f)
                        && Mathf.Approximately(HSV.satScale, 1f)
                        && Mathf.Approximately(HSV.valScale, 1f);
                case MeshPaintOpType.BrightnessContrast:
                    return Mathf.Approximately(BC.brightness, 0f)
                        && Mathf.Approximately(BC.contrast, 0f);
                default:
                    return false;
            }
        }

        private static string ColorToHex(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }

    /// <summary>
    /// Dispatches a single <see cref="MeshPaintOperation"/> to the appropriate
    /// pure pixel function in <see cref="TextureEditCore"/>. Returns a new Color[].
    /// </summary>
    internal static class MeshPaintOpApplier
    {
        public static Color[] Apply(
            Color[] input, MeshPaintOperation op,
            int width, int height,
            Mesh mesh, List<UVIsland> islands, int[] islandGroups)
        {
            if (input == null || op == null) return input;

            List<int> scope = op.IslandScope;
            if ((scope == null || scope.Count == 0) && islands != null)
            {
                scope = new List<int>(islands.Count);
                for (int i = 0; i < islands.Count; i++) scope.Add(i);
            }

            switch (op.Type)
            {
                case MeshPaintOpType.Paint:
                    return TextureEditCore.ComputeGradient(
                        input, width, height, mesh, scope, islands, islandGroups,
                        op.Paint.color, op.Paint.color, 1, false, "replace", 0f, 1f);

                case MeshPaintOpType.Gradient:
                {
                    ResolveAxis(op.Gradient.directionIndex, out int axis, out bool invert);
                    string blend = SafeIndex(MeshPaintOpConstants.BlendModeValues, op.Gradient.blendModeIndex, "replace");
                    return TextureEditCore.ComputeGradient(
                        input, width, height, mesh, scope, islands, islandGroups,
                        op.Gradient.fromColor, op.Gradient.toColor,
                        axis, invert, blend, op.Gradient.startT, op.Gradient.endT);
                }

                case MeshPaintOpType.HSV:
                    return TextureEditCore.ComputeHSV(
                        input, width, height, mesh, op.IslandScope, islands,
                        op.HSV.hueShift, op.HSV.satScale, op.HSV.valScale);

                case MeshPaintOpType.BrightnessContrast:
                    return TextureEditCore.ComputeBrightnessContrast(
                        input, width, height, mesh, op.IslandScope, islands,
                        op.BC.brightness, op.BC.contrast);

                default:
                    return input;
            }
        }

        public static void ResolveAxis(int directionIndex, out int axis, out bool invert)
        {
            string dir = SafeIndex(MeshPaintOpConstants.DirectionValues, directionIndex, "top_to_bottom");
            switch (dir)
            {
                case "top_to_bottom": axis = 1; invert = true; break;
                case "bottom_to_top": axis = 1; invert = false; break;
                case "left_to_right": axis = 0; invert = false; break;
                case "right_to_left": axis = 0; invert = true; break;
                default: axis = 1; invert = true; break;
            }
        }

        private static string SafeIndex(string[] array, int index, string fallback)
        {
            if (array == null || index < 0 || index >= array.Length) return fallback;
            return array[index];
        }
    }
}
