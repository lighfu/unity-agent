using System;
using System.Globalization;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;

#if GESTURE_MANAGER
using BlackStartX.GestureManager.Data;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        private static readonly string[] GestureNames =
            { "Neutral", "Fist", "Open", "FingerPoint", "Victory", "RockNRoll", "Gun", "ThumbsUp" };

        [AgentTool(@"Read both hands' Gesture Manager gesture index/name and weight (0..1).
Includes the mapping of all eight gestures. Requires an active VRC3 GM preview.", Category = "GestureManager", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetGestures()
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            var sb = new StringBuilder($"GestureManager gestures on '{module.Name}':\n");
            foreach (string hand in new[] { "Left", "Right" })
            {
                if (!module.Params.TryGetValue("Gesture" + hand, out var gesture)
                    || !module.Params.TryGetValue("Gesture" + hand + "Weight", out var weight))
                    return $"Error: GM built-in parameters for {hand} hand are unavailable.";
                int index = gesture.IntValue();
                string name = index >= 0 && index < GestureNames.Length ? GestureNames[index] : "Unknown";
                sb.AppendLine($"  {hand}: {index} ({name}), weight={weight.FloatValue().ToString("F4", CultureInfo.InvariantCulture)}");
            }
            sb.Append("Mapping: ");
            for (int i = 0; i < GestureNames.Length; i++)
                sb.Append((i == 0 ? "" : ", ") + i + "=" + GestureNames[i]);
            return sb.ToString();
#endif
        }

        [AgentTool(@"Set a Gesture Manager hand gesture and/or its weight.
hand: left, right, or both. gesture: index 0..7 or Neutral/Fist/Open/FingerPoint/Victory/RockNRoll/Gun/ThumbsUp.
gesture='' leaves the gesture unchanged. weight=-1 uses GM's normal automatic weight behavior;
otherwise weight must be finite and in 0..1. Uses GM's hand-change callbacks, including the neutral weight reset.
All inputs are validated before either hand changes. Requires an active VRC3 GM preview.", Category = "GestureManager")]
        public static string GestureManagerSetGesture(string hand, string gesture = "", float weight = -1)
        {
            string normalizedHand = (hand ?? "").Trim().ToLowerInvariant();
            if (normalizedHand != "left" && normalizedHand != "right" && normalizedHand != "both")
                return "Error: hand must be left, right, or both.";
            return GestureManagerSetGestures(
                normalizedHand == "right" ? "" : gesture,
                normalizedHand == "left" ? "" : gesture,
                normalizedHand == "right" ? -1 : weight,
                normalizedHand == "left" ? -1 : weight);
        }

        [AgentTool(@"Set left/right gestures independently in one Gesture Manager call.
left/right: gesture index 0..7 or name; empty leaves that hand's gesture unchanged.
leftWeight/rightWeight: -1 leaves weight to GM's hand-change behavior, or a finite value in 0..1.
Weights are applied after hand-change callbacks. Validates both hands before writing any parameter.
Requires an active VRC3 GM preview.", Category = "GestureManager")]
        public static string GestureManagerSetGestures(string left = "", string right = "",
            float leftWeight = -1, float rightWeight = -1)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            bool changeLeft = !string.IsNullOrWhiteSpace(left), changeRight = !string.IsNullOrWhiteSpace(right);
            int leftIndex = 0, rightIndex = 0;
            if (changeLeft && !TryParseGestureIndex(left, out leftIndex))
                return "Error: Invalid left gesture. Use 0..7 or Neutral/Fist/Open/FingerPoint/Victory/RockNRoll/Gun/ThumbsUp.";
            if (changeRight && !TryParseGestureIndex(right, out rightIndex))
                return "Error: Invalid right gesture. Use 0..7 or Neutral/Fist/Open/FingerPoint/Victory/RockNRoll/Gun/ThumbsUp.";
            if (!IsGestureWeight(leftWeight) || !IsGestureWeight(rightWeight))
                return "Error: Gesture weights must be -1 (automatic) or finite values in 0..1.";
            if (!changeLeft && !changeRight && leftWeight == -1 && rightWeight == -1)
                return "Error: Specify at least one gesture or weight to change.";
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            if ((changeLeft || leftWeight != -1) && (!module.Params.ContainsKey("GestureLeft") || !module.Params.ContainsKey("GestureLeftWeight"))
                || (changeRight || rightWeight != -1) && (!module.Params.ContainsKey("GestureRight") || !module.Params.ContainsKey("GestureRightWeight")))
                return "Error: Required GM gesture parameters are unavailable. No parameters were changed.";

            if (changeLeft) module.OnNewHand(GestureHand.Left, leftIndex);
            if (changeRight) module.OnNewHand(GestureHand.Right, rightIndex);
            if (leftWeight != -1) module.Params["GestureLeftWeight"].Set(module, leftWeight);
            if (rightWeight != -1) module.Params["GestureRightWeight"].Set(module, rightWeight);
            return "Success: " + GestureManagerGetGestures();
#endif
        }

        internal static bool TryParseGestureIndex(string value, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                if (number < 0 || number >= GestureNames.Length) return false;
                index = number;
                return true;
            }
            var normalized = new StringBuilder();
            foreach (char c in value)
                if (!char.IsWhiteSpace(c) && c != '&' && c != '-' && c != '_')
                    normalized.Append(char.ToLowerInvariant(c));
            string name = normalized.ToString();
            if (name == "idle" || name == "none") name = "neutral";
            if (name == "rockroll" || name == "rockandroll") name = "rocknroll";
            for (int i = 0; i < GestureNames.Length; i++)
                if (string.Equals(name, GestureNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    return true;
                }
            return false;
        }

        internal static bool IsGestureWeight(float value) =>
            value == -1 || !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
    }
}
