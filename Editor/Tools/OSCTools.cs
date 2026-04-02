using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// VRChat OSC 設定・検証・テンプレートツール。
    /// OSCパラメータのアドレス管理、設定JSON生成、フェイストラッキング等テンプレート一括追加。
    /// </summary>
    public static class OSCTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ===== JSON Serialization Classes =====

        [Serializable]
        private class OSCConfig
        {
            public string id;
            public string name;
            public List<OSCParamEntry> parameters = new List<OSCParamEntry>();
        }

        [Serializable]
        private class OSCParamEntry
        {
            public string name;
            public OSCEndpoint input;
            public OSCEndpoint output;
        }

        [Serializable]
        private class OSCEndpoint
        {
            public string address;
            public string type;
        }

        // ===== Shared Helpers =====

        private static string GetBlueprintId(string avatarRootName, out string error)
        {
            error = null;
            var go = FindGO(avatarRootName);
            if (go == null) { error = $"Error: GameObject '{avatarRootName}' not found."; return null; }

            var pipelineType = VRChatTools.FindVrcType("VRC.Core.PipelineManager");
            if (pipelineType == null) { error = "Error: VRChat SDK not found (PipelineManager type missing)."; return null; }

            var pipeline = go.GetComponent(pipelineType);
            if (pipeline == null) { error = $"Error: No PipelineManager on '{avatarRootName}'. Upload the avatar first."; return null; }

            var so = new SerializedObject(pipeline);
            var blueprintProp = so.FindProperty("blueprintId");
            if (blueprintProp == null || string.IsNullOrEmpty(blueprintProp.stringValue))
            {
                error = "Error: blueprintId is empty. Upload the avatar to VRChat first.";
                return null;
            }

            return blueprintProp.stringValue;
        }

        private static string GetOSCConfigPath(string userId, string avatarId)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "AppData", "LocalLow", "VRChat", "VRChat", "OSC", userId, "Avatars", avatarId + ".json");
        }

        private static SerializedObject GetExpressionParametersSO(string avatarRootName, out string error)
        {
            error = null;
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                error = go == null
                    ? $"Error: GameObject '{avatarRootName}' not found."
                    : $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";
                return null;
            }

            var dso = new SerializedObject(descriptor);
            var exprParamsProp = dso.FindProperty("expressionParameters");
            if (exprParamsProp == null || exprParamsProp.objectReferenceValue == null)
            {
                error = "Error: No ExpressionParameters assigned.";
                return null;
            }

            return new SerializedObject(exprParamsProp.objectReferenceValue);
        }

        private static AnimatorController GetFXController(string avatarRootName, out string error)
        {
            error = null;
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                error = go == null
                    ? $"Error: GameObject '{avatarRootName}' not found."
                    : $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";
                return null;
            }

            var dso = new SerializedObject(descriptor);
            var baseLayers = dso.FindProperty("baseAnimationLayers");
            if (baseLayers == null || !baseLayers.isArray)
            {
                error = "Error: Could not read baseAnimationLayers.";
                return null;
            }

            for (int i = 0; i < baseLayers.arraySize; i++)
            {
                var layer = baseLayers.GetArrayElementAtIndex(i);
                var layerType = layer.FindPropertyRelative("type");
                if (layerType != null && layerType.intValue == 5) // FX = 5
                {
                    var ac = layer.FindPropertyRelative("animatorController");
                    if (ac?.objectReferenceValue is AnimatorController controller)
                        return controller;
                    error = "Error: FX layer has no AnimatorController assigned.";
                    return null;
                }
            }

            error = "Error: FX layer not found in baseAnimationLayers.";
            return null;
        }

        private static string ValueTypeToOSCType(int valueType)
        {
            switch (valueType)
            {
                case 0: return "Int";
                case 1: return "Float";
                case 2: return "Bool";
                default: return "Float";
            }
        }

        private static string[] ValueTypeNames = { "Int", "Float", "Bool" };

        private static string BuildOSCConfigJson(string avatarId, string avatarName, List<OSCParamEntry> parameters)
        {
            var config = new OSCConfig { id = avatarId, name = avatarName, parameters = parameters };
            return JsonUtility.ToJson(config, true);
        }

        /// <summary>
        /// VRChat 組み込みパラメータ一覧 (OSC出力専用)
        /// </summary>
        private static readonly (string name, string type)[] BuiltInOSCOutputParams =
        {
            ("VelocityX", "Float"), ("VelocityY", "Float"), ("VelocityZ", "Float"),
            ("InStation", "Bool"), ("Seated", "Bool"), ("AFK", "Bool"),
            ("Upright", "Float"), ("AngularY", "Float"),
            ("Grounded", "Bool"), ("Viseme", "Int"), ("Voice", "Float"),
            ("GestureLeft", "Int"), ("GestureRight", "Int"),
            ("GestureLeftWeight", "Float"), ("GestureRightWeight", "Float"),
            ("TrackingType", "Int"), ("VRMode", "Int"),
            ("MuteSelf", "Bool"), ("IsLocal", "Bool"),
            ("Earmuffs", "Bool"), ("IsOnFriendsList", "Bool"),
            ("AvatarVersion", "Int"),
            ("ScaleModified", "Bool"), ("ScaleFactor", "Float"),
            ("ScaleFactorInverse", "Float"),
            ("EyeHeightAsMeters", "Float"), ("EyeHeightAsPercent", "Float"),
        };

        /// <summary>
        /// VRChat 組み込みパラメータ名チェック
        /// </summary>
        private static bool IsBuiltInParameter(string name)
        {
            foreach (var p in BuiltInOSCOutputParams)
                if (p.name == name) return true;
            return false;
        }

        // ===== Tool 1: ListOSCAddresses =====

        [AgentTool("List all OSC addresses for an avatar's expression parameters. Shows /avatar/parameters/{name} format with type, sync state, and direction. Also lists VRChat built-in output-only parameters.")]
        public static string ListOSCAddresses(string avatarRootName)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            var sb = new StringBuilder();
            sb.AppendLine($"OSC Addresses for '{avatarRootName}':");
            sb.AppendLine();
            sb.AppendLine("== Expression Parameters ==");

            int count = 0;
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name)) continue;

                var vt = param.FindPropertyRelative("valueType")?.intValue ?? -1;
                var synced = param.FindPropertyRelative("networkSynced");
                bool isSynced = synced != null && synced.boolValue;
                string typeStr = vt >= 0 && vt < 3 ? ValueTypeNames[vt] : "?";
                string syncStr = isSynced ? "Synced" : "Local";
                string direction = isSynced ? "In/Out" : "In";

                sb.AppendLine($"  /avatar/parameters/{name}  ({typeStr}, {syncStr}, {direction})");
                count++;
            }

            sb.AppendLine($"  ({count} parameters)");
            sb.AppendLine();
            sb.AppendLine("== Built-in Parameters (Output only) ==");
            foreach (var (name, type) in BuiltInOSCOutputParams)
                sb.AppendLine($"  /avatar/parameters/{name}  ({type}, Output)");

            return sb.ToString().TrimEnd();
        }

        // ===== Tool 2: GenerateOSCConfigJson =====

        [AgentTool("Generate a preview of the VRChat OSC config JSON for an avatar. Does not write to disk. Shows the JSON that WriteOSCConfigFile would create.")]
        public static string GenerateOSCConfigJson(string avatarRootName)
        {
            var blueprintId = GetBlueprintId(avatarRootName, out string bpErr);
            if (blueprintId == null) return bpErr;

            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            var entries = new List<OSCParamEntry>();

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name)) continue;

                var vt = param.FindPropertyRelative("valueType")?.intValue ?? 1;
                string oscType = ValueTypeToOSCType(vt);
                string address = $"/avatar/parameters/{name}";

                entries.Add(new OSCParamEntry
                {
                    name = name,
                    input = new OSCEndpoint { address = address, type = oscType },
                    output = new OSCEndpoint { address = address, type = oscType }
                });
            }

            string json = BuildOSCConfigJson(blueprintId, avatarRootName, entries);
            return $"OSC Config JSON preview for '{avatarRootName}' (blueprint: {blueprintId}):\n\n{json}";
        }

        // ===== Tool 3: WriteOSCConfigFile =====

        [AgentTool("Write the OSC config JSON file to VRChat's OSC directory. Requires userId (e.g. 'usr_xxxxxxxx'). Writes to %USERPROFILE%/AppData/LocalLow/VRChat/VRChat/OSC/{userId}/Avatars/{blueprintId}.json. Requires confirmation.")]
        public static string WriteOSCConfigFile(string avatarRootName, string userId)
        {
            if (string.IsNullOrEmpty(userId) || !userId.StartsWith("usr_"))
                return "Error: userId must start with 'usr_' (e.g. 'usr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx').";

            var blueprintId = GetBlueprintId(avatarRootName, out string bpErr);
            if (blueprintId == null) return bpErr;

            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            string configPath = GetOSCConfigPath(userId, blueprintId);

            if (!AgentSettings.RequestConfirmation(
                "OSC設定ファイルの書き込み",
                $"VRChat OSC設定JSONをプロジェクト外のディレクトリに書き込みます。\n" +
                $"パス: {configPath}\n" +
                $"アバター: {avatarRootName} (blueprint: {blueprintId})"))
                return "Cancelled: User denied the operation.";

            var entries = new List<OSCParamEntry>();

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name)) continue;

                var vt = param.FindPropertyRelative("valueType")?.intValue ?? 1;
                string oscType = ValueTypeToOSCType(vt);
                string address = $"/avatar/parameters/{name}";

                entries.Add(new OSCParamEntry
                {
                    name = name,
                    input = new OSCEndpoint { address = address, type = oscType },
                    output = new OSCEndpoint { address = address, type = oscType }
                });
            }

            string json = BuildOSCConfigJson(blueprintId, avatarRootName, entries);

            string dir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(configPath, json, Encoding.UTF8);

            return $"Success: Wrote OSC config to:\n  {configPath}\n  ({entries.Count} parameters)";
        }

        // ===== Tool 4: ResetOSCConfig =====

        [AgentTool("Delete the OSC config file for an avatar, forcing VRChat to regenerate it on next load. Requires userId. Requires confirmation.")]
        public static string ResetOSCConfig(string avatarRootName, string userId)
        {
            if (string.IsNullOrEmpty(userId) || !userId.StartsWith("usr_"))
                return "Error: userId must start with 'usr_'.";

            var blueprintId = GetBlueprintId(avatarRootName, out string bpErr);
            if (blueprintId == null) return bpErr;

            string configPath = GetOSCConfigPath(userId, blueprintId);

            if (!File.Exists(configPath))
                return $"Info: No OSC config file found at:\n  {configPath}\n  Nothing to delete.";

            if (!AgentSettings.RequestConfirmation(
                "OSC設定ファイルの削除",
                $"VRChat OSC設定ファイルを削除します。VRChatが次回ロード時に再生成します。\n" +
                $"パス: {configPath}"))
                return "Cancelled: User denied the operation.";

            File.Delete(configPath);
            return $"Success: Deleted OSC config file:\n  {configPath}\n  VRChat will regenerate it on next avatar load.";
        }

        // ===== Tool 5: CustomizeOSCRoute =====

        [AgentTool("Customize the OSC routing for a specific parameter in the config JSON. Change input/output address or type. Useful for routing VRChat OSC to external apps. Requires userId. Leave inputAddress/outputAddress empty to keep current. outputType: Int, Float, or Bool.")]
        public static string CustomizeOSCRoute(string avatarRootName, string userId, string paramName,
            string inputAddress = "", string outputAddress = "", string outputType = "")
        {
            if (string.IsNullOrEmpty(userId) || !userId.StartsWith("usr_"))
                return "Error: userId must start with 'usr_'.";

            if (string.IsNullOrEmpty(paramName))
                return "Error: paramName is required.";

            var blueprintId = GetBlueprintId(avatarRootName, out string bpErr);
            if (blueprintId == null) return bpErr;

            string configPath = GetOSCConfigPath(userId, blueprintId);

            if (!File.Exists(configPath))
                return $"Error: OSC config file not found at:\n  {configPath}\n  Use WriteOSCConfigFile first.";

            if (!AgentSettings.RequestConfirmation(
                "OSCルーティングのカスタマイズ",
                $"パラメータ '{paramName}' のOSCルーティングを変更します。\n" +
                $"ファイル: {configPath}\n" +
                (string.IsNullOrEmpty(inputAddress) ? "" : $"入力アドレス: {inputAddress}\n") +
                (string.IsNullOrEmpty(outputAddress) ? "" : $"出力アドレス: {outputAddress}\n") +
                (string.IsNullOrEmpty(outputType) ? "" : $"出力型: {outputType}\n")))
                return "Cancelled: User denied the operation.";

            string jsonText = File.ReadAllText(configPath, Encoding.UTF8);
            var config = JsonUtility.FromJson<OSCConfig>(jsonText);
            if (config == null || config.parameters == null)
                return "Error: Failed to parse OSC config JSON.";

            var entry = config.parameters.FirstOrDefault(p => p.name == paramName);
            if (entry == null)
                return $"Error: Parameter '{paramName}' not found in OSC config.";

            var changes = new StringBuilder();

            if (!string.IsNullOrEmpty(inputAddress) && entry.input != null)
            {
                string prev = entry.input.address;
                entry.input.address = inputAddress;
                changes.AppendLine($"  Input address: {prev} -> {inputAddress}");
            }

            if (!string.IsNullOrEmpty(outputAddress) && entry.output != null)
            {
                string prev = entry.output.address;
                entry.output.address = outputAddress;
                changes.AppendLine($"  Output address: {prev} -> {outputAddress}");
            }

            if (!string.IsNullOrEmpty(outputType) && entry.output != null)
            {
                string prev = entry.output.type;
                entry.output.type = outputType;
                changes.AppendLine($"  Output type: {prev} -> {outputType}");
            }

            if (changes.Length == 0)
                return "Info: No changes specified. Provide inputAddress, outputAddress, or outputType.";

            string newJson = JsonUtility.ToJson(config, true);
            File.WriteAllText(configPath, newJson, Encoding.UTF8);

            return $"Success: Updated OSC routing for '{paramName}':\n{changes.ToString().TrimEnd()}";
        }

        // ===== Tool 6: ValidateOSCSetup =====

        [AgentTool("Comprehensive OSC setup validation. Checks: type mismatches between ExpressionParameters and FX Animator, face tracking params that are unnecessarily synced (budget waste), float quantization warnings, built-in parameter name collisions, and budget summary.")]
        public static string ValidateOSCSetup(string avatarRootName)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var fxController = GetFXController(avatarRootName, out string fxErr);

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters.";

            var sb = new StringBuilder();
            sb.AppendLine($"OSC Setup Validation for '{avatarRootName}':");

            int totalBits = 0;
            int issues = 0;
            int warnings = 0;

            // Collect FX animator parameter types for mismatch detection
            var fxParamTypes = new Dictionary<string, AnimatorControllerParameterType>();
            if (fxController != null)
            {
                foreach (var p in fxController.parameters)
                    fxParamTypes[p.name] = p.type;
            }

            // Known face-tracking parameter prefixes
            var ftPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "EyeClosed", "EyeSquint", "EyeWide", "EyeLid",
                "JawOpen", "JawForward", "JawLeft", "JawRight",
                "MouthClosed", "MouthOpen", "MouthSmile", "MouthFrown", "MouthStretch",
                "MouthDimple", "MouthPress", "MouthShrink", "MouthPucker",
                "MouthUpperUp", "MouthLowerDown", "MouthRollUpper", "MouthRollLower",
                "MouthFunnel", "MouthTightener",
                "NoseSneer", "CheekPuff", "CheekSquint", "CheekSuck",
                "BrowDown", "BrowInnerUp", "BrowOuterUp",
                "TongueOut", "TongueCurl", "TongueUp", "TongueDown", "TongueLeft", "TongueRight",
                "TongueBulge", "TongueFlat", "TongueTwist",
                "EyeX", "EyeY", "EyeDilation", "EyeConstrict",
                "v2/"
            };

            sb.AppendLine();
            sb.AppendLine("--- Issues ---");

            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var name = param.FindPropertyRelative("name")?.stringValue;
                if (string.IsNullOrEmpty(name)) continue;

                var vt = param.FindPropertyRelative("valueType")?.intValue ?? -1;
                var synced = param.FindPropertyRelative("networkSynced");
                bool isSynced = synced != null && synced.boolValue;

                if (isSynced)
                    totalBits += vt == 2 ? 1 : 8;

                // Check 1: Type mismatch with FX Animator
                if (fxController != null && fxParamTypes.TryGetValue(name, out var fxType))
                {
                    bool mismatch = false;
                    string exprType = vt >= 0 && vt < 3 ? ValueTypeNames[vt] : "?";
                    string fxTypeStr = fxType.ToString();

                    if (vt == 0 && fxType != AnimatorControllerParameterType.Int) mismatch = true;
                    else if (vt == 1 && fxType != AnimatorControllerParameterType.Float) mismatch = true;
                    else if (vt == 2 && fxType != AnimatorControllerParameterType.Bool) mismatch = true;

                    if (mismatch)
                    {
                        sb.AppendLine($"  [ERROR] Type mismatch: '{name}' is {exprType} in ExpressionParameters but {fxTypeStr} in FX Animator.");
                        issues++;
                    }
                }

                // Check 2: Face tracking params that are synced (budget waste)
                if (isSynced)
                {
                    bool isFT = false;
                    foreach (var prefix in ftPrefixes)
                    {
                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("FT_") || name.StartsWith("v2/"))
                        {
                            isFT = true;
                            break;
                        }
                    }

                    if (isFT)
                    {
                        int cost = vt == 2 ? 1 : 8;
                        sb.AppendLine($"  [WARN] Face tracking parameter '{name}' is Synced ({cost}bit). FT params are local-only and should be unsynced to save budget.");
                        warnings++;
                    }
                }

                // Check 3: Float quantization warning for synced floats
                if (isSynced && vt == 1)
                {
                    // Only warn once in summary, not per-param
                }

                // Check 4: Built-in parameter name collision
                if (IsBuiltInParameter(name))
                {
                    sb.AppendLine($"  [WARN] '{name}' collides with a VRChat built-in parameter name. This may cause unexpected behavior.");
                    warnings++;
                }
            }

            // Count synced floats for quantization warning
            int syncedFloats = 0;
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var param = parameters.GetArrayElementAtIndex(i);
                var vt = param.FindPropertyRelative("valueType")?.intValue ?? -1;
                var synced = param.FindPropertyRelative("networkSynced");
                if (synced != null && synced.boolValue && vt == 1)
                    syncedFloats++;
            }

            if (syncedFloats > 0)
            {
                sb.AppendLine($"  [INFO] {syncedFloats} synced Float parameter(s): values are quantized to 8-bit (256 steps, ~0.004 resolution). High-precision data may lose accuracy.");
                warnings++;
            }

            if (issues == 0 && warnings == 0)
                sb.AppendLine("  No issues found.");

            // Budget summary
            sb.AppendLine();
            sb.AppendLine("--- Budget Summary ---");
            sb.AppendLine($"  Synced bits used: {totalBits}/256 ({totalBits / 256f * 100f:F1}%)");
            sb.AppendLine($"  Remaining: {256 - totalBits} bits");

            if (totalBits > 256)
            {
                sb.AppendLine($"  [ERROR] Over budget by {totalBits - 256} bits! Avatar may not load.");
                issues++;
            }

            sb.AppendLine();
            sb.AppendLine($"Total: {issues} error(s), {warnings} warning(s).");

            return sb.ToString().TrimEnd();
        }

        // ===== Tool 7: AddFaceTrackingParameters =====

        /// <summary>
        /// VRCFaceTracking 統一表現パラメータ: base セット (15個)
        /// </summary>
        private static readonly (string name, string type)[] FTParamsBase =
        {
            ("EyeClosedRight", "Float"), ("EyeClosedLeft", "Float"),
            ("EyeSquintRight", "Float"), ("EyeSquintLeft", "Float"),
            ("EyeWideRight", "Float"), ("EyeWideLeft", "Float"),
            ("JawOpen", "Float"),
            ("MouthClosed", "Float"), ("MouthOpen", "Float"),
            ("MouthSmileRight", "Float"), ("MouthSmileLeft", "Float"),
            ("MouthFrownRight", "Float"), ("MouthFrownLeft", "Float"),
            ("MouthStretchRight", "Float"), ("MouthStretchLeft", "Float"),
        };

        /// <summary>
        /// VRCFaceTracking 統一表現パラメータ: full 追加分 (42個)
        /// </summary>
        private static readonly (string name, string type)[] FTParamsFullExtra =
        {
            // Eye Gaze
            ("EyeXRight", "Float"), ("EyeXLeft", "Float"),
            ("EyeYRight", "Float"), ("EyeYLeft", "Float"),
            ("EyeDilationRight", "Float"), ("EyeDilationLeft", "Float"),
            ("EyeConstrictRight", "Float"), ("EyeConstrictLeft", "Float"),
            // Brow
            ("BrowDownRight", "Float"), ("BrowDownLeft", "Float"),
            ("BrowInnerUpRight", "Float"), ("BrowInnerUpLeft", "Float"),
            ("BrowOuterUpRight", "Float"), ("BrowOuterUpLeft", "Float"),
            // Nose
            ("NoseSneerRight", "Float"), ("NoseSneerLeft", "Float"),
            // Cheek
            ("CheekPuffRight", "Float"), ("CheekPuffLeft", "Float"),
            ("CheekSquintRight", "Float"), ("CheekSquintLeft", "Float"),
            ("CheekSuckRight", "Float"), ("CheekSuckLeft", "Float"),
            // Jaw
            ("JawForward", "Float"), ("JawLeft", "Float"), ("JawRight", "Float"),
            // Mouth extended
            ("MouthUpperUpRight", "Float"), ("MouthUpperUpLeft", "Float"),
            ("MouthLowerDownRight", "Float"), ("MouthLowerDownLeft", "Float"),
            ("MouthRollUpper", "Float"), ("MouthRollLower", "Float"),
            ("MouthPuckerRight", "Float"), ("MouthPuckerLeft", "Float"),
            ("MouthTightenerRight", "Float"), ("MouthTightenerLeft", "Float"),
            ("MouthPressRight", "Float"), ("MouthPressLeft", "Float"),
            // Tongue
            ("TongueOut", "Float"), ("TongueCurl", "Float"),
            ("TongueUp", "Float"), ("TongueDown", "Float"),
            ("TongueLeft", "Float"), ("TongueRight", "Float"),
        };

        [AgentTool("Add VRCFaceTracking unified expression parameters in bulk. standard: 'base' (15 params: eyes, jaw, mouth basics) or 'full' (base + 42 extra: gaze, brow, nose, cheek, jaw, mouth extended, tongue). All Float, Local (non-synced), non-saved = zero budget cost. Adds to both ExpressionParameters and FX Animator. Requires confirmation.")]
        public static string AddFaceTrackingParameters(string avatarRootName, string standard = "base")
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var fxController = GetFXController(avatarRootName, out string fxErr);

            bool isFull = standard.Equals("full", StringComparison.OrdinalIgnoreCase);
            var paramsToAdd = new List<(string name, string type)>(FTParamsBase);
            if (isFull)
                paramsToAdd.AddRange(FTParamsFullExtra);

            if (!AgentSettings.RequestConfirmation(
                "フェイストラッキングパラメータの追加",
                $"VRCFaceTracking {(isFull ? "full" : "base")}パラメータ ({paramsToAdd.Count}個) を追加します。\n" +
                $"すべて Float, Local (非Sync), 非Saved → Budget消費ゼロ\n" +
                $"ExpressionParameters と FX Animator の両方に追加されます。"))
                return "Cancelled: User denied the operation.";

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters array.";

            // Collect existing parameter names
            var existingNames = new HashSet<string>();
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var name = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                if (!string.IsNullOrEmpty(name)) existingNames.Add(name);
            }

            // Collect existing FX params
            var existingFXParams = new HashSet<string>();
            if (fxController != null)
            {
                foreach (var p in fxController.parameters)
                    existingFXParams.Add(p.name);
            }

            int addedExpr = 0, addedFX = 0, skipped = 0;

            Undo.RecordObject(paramsSo.targetObject, "Add Face Tracking Parameters");
            if (fxController != null)
                Undo.RecordObject(fxController, "Add Face Tracking FX Parameters");

            foreach (var (pName, pType) in paramsToAdd)
            {
                // Add to ExpressionParameters
                if (!existingNames.Contains(pName))
                {
                    int idx = parameters.arraySize;
                    parameters.InsertArrayElementAtIndex(idx);
                    var newParam = parameters.GetArrayElementAtIndex(idx);
                    newParam.FindPropertyRelative("name").stringValue = pName;
                    newParam.FindPropertyRelative("valueType").intValue = 1; // Float
                    newParam.FindPropertyRelative("defaultValue").floatValue = 0f;
                    newParam.FindPropertyRelative("saved").boolValue = false;
                    newParam.FindPropertyRelative("networkSynced").boolValue = false;
                    addedExpr++;
                    existingNames.Add(pName);
                }
                else
                {
                    skipped++;
                }

                // Add to FX Animator
                if (fxController != null && !existingFXParams.Contains(pName))
                {
                    fxController.AddParameter(pName, AnimatorControllerParameterType.Float);
                    existingFXParams.Add(pName);
                    addedFX++;
                }
            }

            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsSo.targetObject);
            if (fxController != null)
                EditorUtility.SetDirty(fxController);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Added VRCFaceTracking {(isFull ? "full" : "base")} parameters:");
            sb.AppendLine($"  ExpressionParameters: {addedExpr} added, {skipped} already existed");
            if (fxController != null)
                sb.AppendLine($"  FX Animator: {addedFX} added");
            else
                sb.AppendLine($"  FX Animator: skipped ({fxErr ?? "not available"})");
            sb.AppendLine($"  Budget cost: 0 bits (all Local, non-synced)");

            return sb.ToString().TrimEnd();
        }

        // ===== Tool 8: AddHeartRateParameters =====

        private static readonly (string name, string type, float defaultValue)[] HeartRateParams =
        {
            ("HeartRate", "Float", 0f),        // Normalized 0-1 (mapped from BPM)
            ("HeartRateBPM", "Int", 0),         // Raw BPM value (0-255)
            ("HeartRateConnected", "Bool", 0f), // Whether HR monitor is connected
        };

        [AgentTool("Add heart rate monitoring parameters for OSC heart rate apps (Pulsoid, HRtoVRChat, etc.). Adds HeartRate (Float, normalized 0-1), HeartRateBPM (Int, raw BPM), HeartRateConnected (Bool). All Local, non-saved = zero budget. Requires confirmation.")]
        public static string AddHeartRateParameters(string avatarRootName)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var fxController = GetFXController(avatarRootName, out string fxErr);

            if (!AgentSettings.RequestConfirmation(
                "心拍数パラメータの追加",
                "HeartRate, HeartRateBPM, HeartRateConnected を追加します。\n" +
                "すべて Local, 非Saved → Budget消費ゼロ"))
                return "Cancelled: User denied the operation.";

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters array.";

            var existingNames = new HashSet<string>();
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var name = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                if (!string.IsNullOrEmpty(name)) existingNames.Add(name);
            }

            var existingFXParams = new HashSet<string>();
            if (fxController != null)
                foreach (var p in fxController.parameters) existingFXParams.Add(p.name);

            Undo.RecordObject(paramsSo.targetObject, "Add Heart Rate Parameters");
            if (fxController != null) Undo.RecordObject(fxController, "Add Heart Rate FX Parameters");

            int added = 0;
            foreach (var (pName, pType, defVal) in HeartRateParams)
            {
                int valueType = pType == "Int" ? 0 : pType == "Float" ? 1 : 2;
                var fxParamType = pType == "Int" ? AnimatorControllerParameterType.Int
                    : pType == "Float" ? AnimatorControllerParameterType.Float
                    : AnimatorControllerParameterType.Bool;

                if (!existingNames.Contains(pName))
                {
                    int idx = parameters.arraySize;
                    parameters.InsertArrayElementAtIndex(idx);
                    var newParam = parameters.GetArrayElementAtIndex(idx);
                    newParam.FindPropertyRelative("name").stringValue = pName;
                    newParam.FindPropertyRelative("valueType").intValue = valueType;
                    newParam.FindPropertyRelative("defaultValue").floatValue = defVal;
                    newParam.FindPropertyRelative("saved").boolValue = false;
                    newParam.FindPropertyRelative("networkSynced").boolValue = false;
                    added++;
                }

                if (fxController != null && !existingFXParams.Contains(pName))
                {
                    fxController.AddParameter(pName, fxParamType);
                    existingFXParams.Add(pName);
                }
            }

            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsSo.targetObject);
            if (fxController != null) EditorUtility.SetDirty(fxController);

            return $"Success: Added {added} heart rate parameter(s) (budget cost: 0 bits).";
        }

        // ===== Tool 9: AddHardwareTelemetryParameters =====

        private static readonly (string name, string type, float defaultValue)[] TelemetryParams =
        {
            ("CPU_Temp", "Float", 0f),
            ("CPU_Usage", "Float", 0f),
            ("GPU_Temp", "Float", 0f),
            ("GPU_Usage", "Float", 0f),
            ("RAM_Usage", "Float", 0f),
            ("FPS", "Float", 0f),
        };

        [AgentTool("Add PC hardware telemetry parameters for OSC monitoring apps. Adds CPU_Temp, CPU_Usage, GPU_Temp, GPU_Usage, RAM_Usage, FPS (all Float). All Local, non-saved = zero budget. Requires confirmation.")]
        public static string AddHardwareTelemetryParameters(string avatarRootName)
        {
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string err);
            if (paramsSo == null) return err;

            var fxController = GetFXController(avatarRootName, out string fxErr);

            if (!AgentSettings.RequestConfirmation(
                "ハードウェアテレメトリパラメータの追加",
                "CPU_Temp, CPU_Usage, GPU_Temp, GPU_Usage, RAM_Usage, FPS を追加します。\n" +
                "すべて Local, 非Saved → Budget消費ゼロ"))
                return "Cancelled: User denied the operation.";

            var parameters = paramsSo.FindProperty("parameters");
            if (parameters == null) return "Error: Cannot read parameters array.";

            var existingNames = new HashSet<string>();
            for (int i = 0; i < parameters.arraySize; i++)
            {
                var name = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                if (!string.IsNullOrEmpty(name)) existingNames.Add(name);
            }

            var existingFXParams = new HashSet<string>();
            if (fxController != null)
                foreach (var p in fxController.parameters) existingFXParams.Add(p.name);

            Undo.RecordObject(paramsSo.targetObject, "Add Telemetry Parameters");
            if (fxController != null) Undo.RecordObject(fxController, "Add Telemetry FX Parameters");

            int added = 0;
            foreach (var (pName, pType, defVal) in TelemetryParams)
            {
                if (!existingNames.Contains(pName))
                {
                    int idx = parameters.arraySize;
                    parameters.InsertArrayElementAtIndex(idx);
                    var newParam = parameters.GetArrayElementAtIndex(idx);
                    newParam.FindPropertyRelative("name").stringValue = pName;
                    newParam.FindPropertyRelative("valueType").intValue = 1; // Float
                    newParam.FindPropertyRelative("defaultValue").floatValue = defVal;
                    newParam.FindPropertyRelative("saved").boolValue = false;
                    newParam.FindPropertyRelative("networkSynced").boolValue = false;
                    added++;
                }

                if (fxController != null && !existingFXParams.Contains(pName))
                {
                    fxController.AddParameter(pName, AnimatorControllerParameterType.Float);
                    existingFXParams.Add(pName);
                }
            }

            paramsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(paramsSo.targetObject);
            if (fxController != null) EditorUtility.SetDirty(fxController);

            return $"Success: Added {added} telemetry parameter(s) (budget cost: 0 bits).";
        }

        // ===== Tool 10: ListVRChatOSCInputEndpoints =====

        [AgentTool("Reference table of all VRChat /input/* OSC endpoints. Shows available input controls for buttons, axes, and chatbox.")]
        public static string ListVRChatOSCInputEndpoints()
        {
            var sb = new StringBuilder();
            sb.AppendLine("VRChat OSC Input Endpoints (/input/*):");
            sb.AppendLine();
            sb.AppendLine("== Buttons (Int: 1=press, 0=release) ==");
            sb.AppendLine("  /input/MoveForward       Move forward");
            sb.AppendLine("  /input/MoveBackward      Move backward");
            sb.AppendLine("  /input/MoveLeft           Move left");
            sb.AppendLine("  /input/MoveRight          Move right");
            sb.AppendLine("  /input/LookLeft           Rotate view left");
            sb.AppendLine("  /input/LookRight          Rotate view right");
            sb.AppendLine("  /input/Jump               Jump");
            sb.AppendLine("  /input/Run                Sprint toggle");
            sb.AppendLine("  /input/ComfortLeft        Comfort turn left");
            sb.AppendLine("  /input/ComfortRight       Comfort turn right");
            sb.AppendLine("  /input/DropRight          Drop held object (right)");
            sb.AppendLine("  /input/DropLeft           Drop held object (left)");
            sb.AppendLine("  /input/GrabRight          Grab object (right)");
            sb.AppendLine("  /input/GrabLeft           Grab object (left)");
            sb.AppendLine("  /input/UseRight           Use held object (right)");
            sb.AppendLine("  /input/UseLeft            Use held object (left)");
            sb.AppendLine("  /input/PanicButton        Panic button (safety)");
            sb.AppendLine("  /input/QuickMenuToggleLeft  Toggle Quick Menu (left)");
            sb.AppendLine("  /input/QuickMenuToggleRight Toggle Quick Menu (right)");
            sb.AppendLine("  /input/Voice              Toggle voice (push-to-talk)");
            sb.AppendLine();
            sb.AppendLine("== Axes (Float: -1.0 to 1.0) ==");
            sb.AppendLine("  /input/Vertical           Forward/backward axis");
            sb.AppendLine("  /input/Horizontal         Left/right axis");
            sb.AppendLine("  /input/LookHorizontal     View horizontal rotation");
            sb.AppendLine("  /input/LookVertical       View vertical rotation (desktop)");
            sb.AppendLine("  /input/UseAxisRight       Use axis (right hand)");
            sb.AppendLine("  /input/UseAxisLeft        Use axis (left hand)");
            sb.AppendLine("  /input/GrabAxisRight      Grab axis (right hand)");
            sb.AppendLine("  /input/GrabAxisLeft       Grab axis (left hand)");
            sb.AppendLine("  /input/SpinHoldCwRight    Spin hold clockwise (right)");
            sb.AppendLine("  /input/SpinHoldCcwRight   Spin hold counter-clockwise (right)");
            sb.AppendLine("  /input/SpinHoldCwLeft     Spin hold clockwise (left)");
            sb.AppendLine("  /input/SpinHoldCcwLeft    Spin hold counter-clockwise (left)");
            sb.AppendLine("  /input/SpinHoldUp         Spin hold up");
            sb.AppendLine("  /input/SpinHoldDown       Spin hold down");
            sb.AppendLine();
            sb.AppendLine("== Chatbox ==");
            sb.AppendLine("  /chatbox/input   (String s, Bool b, Bool n)  Send text: s=message, b=send immediately, n=play notification SFX");
            sb.AppendLine("  /chatbox/typing  (Bool b)                     Set typing indicator");

            return sb.ToString().TrimEnd();
        }

        // ===== Tool 11: InspectOSCReadiness =====

        [AgentTool("Check OSC readiness for an avatar: blueprint ID presence, expression parameter count, synced budget usage, and OSC config file existence (if userId provided). Provides a quick go/no-go checklist.")]
        public static string InspectOSCReadiness(string avatarRootName, string userId = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OSC Readiness Check for '{avatarRootName}':");
            sb.AppendLine();

            int pass = 0, fail = 0, warn = 0;

            // Check 1: VRChat SDK
            var descriptorType = VRChatTools.FindVrcType(VRChatTools.VrcDescriptorTypeName);
            if (descriptorType == null)
            {
                sb.AppendLine("  [FAIL] VRChat SDK not found.");
                fail++;
                return sb.ToString().TrimEnd();
            }
            sb.AppendLine("  [PASS] VRChat SDK installed.");
            pass++;

            // Check 2: Avatar Descriptor
            var descriptor = VRChatTools.FindAvatarDescriptor(avatarRootName);
            if (descriptor == null)
            {
                var go = FindGO(avatarRootName);
                if (go == null)
                    sb.AppendLine($"  [FAIL] GameObject '{avatarRootName}' not found.");
                else
                    sb.AppendLine($"  [FAIL] No VRCAvatarDescriptor on '{avatarRootName}'.");
                fail++;
                return sb.ToString().TrimEnd();
            }
            sb.AppendLine("  [PASS] VRCAvatarDescriptor found.");
            pass++;

            // Check 3: Blueprint ID
            var blueprintId = GetBlueprintId(avatarRootName, out string bpErr);
            if (blueprintId == null)
            {
                sb.AppendLine($"  [WARN] Blueprint ID not set. Upload the avatar to VRChat first for OSC config file generation.");
                warn++;
            }
            else
            {
                sb.AppendLine($"  [PASS] Blueprint ID: {blueprintId}");
                pass++;
            }

            // Check 4: Expression Parameters
            var paramsSo = GetExpressionParametersSO(avatarRootName, out string paramErr);
            if (paramsSo == null)
            {
                sb.AppendLine($"  [FAIL] {paramErr}");
                fail++;
            }
            else
            {
                var parameters = paramsSo.FindProperty("parameters");
                int paramCount = 0, totalBits = 0;
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.arraySize; i++)
                    {
                        var param = parameters.GetArrayElementAtIndex(i);
                        var name = param.FindPropertyRelative("name")?.stringValue;
                        if (string.IsNullOrEmpty(name)) continue;
                        paramCount++;

                        var vt = param.FindPropertyRelative("valueType")?.intValue ?? -1;
                        var synced = param.FindPropertyRelative("networkSynced");
                        if (synced != null && synced.boolValue)
                            totalBits += vt == 2 ? 1 : 8;
                    }
                }

                sb.AppendLine($"  [PASS] Expression Parameters: {paramCount} defined");
                pass++;

                // Budget check
                if (totalBits > 256)
                {
                    sb.AppendLine($"  [FAIL] Synced budget: {totalBits}/256 bits (OVER BUDGET by {totalBits - 256})");
                    fail++;
                }
                else if (totalBits > 230)
                {
                    sb.AppendLine($"  [WARN] Synced budget: {totalBits}/256 bits ({256 - totalBits} remaining - low!)");
                    warn++;
                }
                else
                {
                    sb.AppendLine($"  [PASS] Synced budget: {totalBits}/256 bits ({256 - totalBits} remaining)");
                    pass++;
                }
            }

            // Check 5: FX Controller
            var fxController = GetFXController(avatarRootName, out string fxErr);
            if (fxController == null)
            {
                sb.AppendLine($"  [WARN] FX Controller: {fxErr}");
                warn++;
            }
            else
            {
                sb.AppendLine($"  [PASS] FX Controller: {fxController.name} ({fxController.parameters.Length} params, {fxController.layers.Length} layers)");
                pass++;
            }

            // Check 6: OSC config file existence
            if (!string.IsNullOrEmpty(userId) && blueprintId != null)
            {
                string configPath = GetOSCConfigPath(userId, blueprintId);
                if (File.Exists(configPath))
                {
                    var fileInfo = new FileInfo(configPath);
                    sb.AppendLine($"  [PASS] OSC config file exists ({fileInfo.Length} bytes, modified {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm})");
                    pass++;
                }
                else
                {
                    sb.AppendLine($"  [INFO] No OSC config file found. VRChat will auto-generate on first load, or use WriteOSCConfigFile to pre-create.");
                    warn++;
                }
            }
            else if (string.IsNullOrEmpty(userId))
            {
                sb.AppendLine("  [INFO] Provide userId to check OSC config file existence.");
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: {pass} passed, {fail} failed, {warn} info/warnings");

            return sb.ToString().TrimEnd();
        }
    }
}
