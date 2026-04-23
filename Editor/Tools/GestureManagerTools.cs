using UnityEngine;
using UnityEditor;
using System.Text;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

#if GESTURE_MANAGER
using BlackStartX.GestureManager;
using BlackStartX.GestureManager.Editor.Modules;
using GmModule = BlackStartX.GestureManager.Data.ModuleBase;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// BlackStartX Gesture Manager (vrchat.blackstartx.gesture-manager) 連携ツール。
    /// パッケージ未導入でもコンパイルは通る（全 API が "not installed" を返す）。
    /// GM は Play mode ではなく Edit mode の simulation tool なので、Play mode 判定は不要。
    /// </summary>
    public static class GestureManagerTools
    {
#if GESTURE_MANAGER
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static GestureManager FindInstance()
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<GestureManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<GestureManager>(true);
#endif
            return all.Length == 0 ? null : all[0];
        }
#endif

        [AgentTool(@"Report the BlackStartX Gesture Manager state in the current scene.
Returns: whether GM is installed, how many GM instances exist, which avatar is being previewed,
whether the preview module is active, current gestures (left/right), and a short parameter sample.
Use before calling GestureManagerSetParam / ExitPreview to confirm a preview is running.")]
        public static string GetGestureManagerState()
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed (vrchat.blackstartx.gesture-manager). Tool is a no-op.";
#else
            var sb = new StringBuilder();
            sb.AppendLine("GestureManager: installed");
            sb.AppendLine($"  Version: {GestureManager.Version}");

#if UNITY_2023_1_OR_NEWER
            var instances = Object.FindObjectsByType<GestureManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var instances = Object.FindObjectsOfType<GestureManager>(true);
#endif
            sb.AppendLine($"  Instances in scene: {instances.Length}");

            if (instances.Length == 0)
            {
                sb.AppendLine("  (No GestureManager GameObject present. Spawn the GestureManager prefab or call GestureManagerEnterPreview.)");
                return sb.ToString().TrimEnd();
            }

            for (int i = 0; i < instances.Length; i++)
            {
                var gm = instances[i];
                sb.AppendLine($"  [{i}] '{gm.gameObject.name}' activeInHierarchy={gm.gameObject.activeInHierarchy}");
                var mod = gm.Module;
                if (mod == null)
                {
                    sb.AppendLine("      Module: (none — not previewing)");
                    continue;
                }
                sb.AppendLine($"      Module: {mod.GetType().Name} Avatar='{mod.Name}' Active={mod.Active} PlayingCustomAnim={mod.PlayingCustomAnimation}");

                var vrc3 = mod as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
                if (vrc3 != null)
                {
                    sb.AppendLine($"      Params: {vrc3.Params.Count} total");
                    var sample = vrc3.Params.Take(8).Select(kv =>
                        $"{kv.Key}={kv.Value.FloatValue().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"      Sample: {string.Join(", ", sample)}");
                }
            }

            sb.AppendLine($"  ControlledAvatars: {GestureManager.ControlledAvatars.Count}");
            foreach (var kv in GestureManager.ControlledAvatars)
                sb.AppendLine($"    - {kv.Key.name}");
            return sb.ToString().TrimEnd();
#endif
        }

        [AgentTool(@"Enter Unity Play mode with GestureManager pre-targeted at the given avatar.
Equivalent to: set GM 'Favourite Avatar' -> click 'Enter Play-Mode'.
After domain reload Unity starts Play mode and GM auto-attaches to the favourite avatar.

IMPORTANT: This triggers a domain reload. The MCP bridge WILL disconnect briefly and the next
tool call may fail until the bridge reconnects. Pass confirm=true to acknowledge.

If already in Play mode, this tool errors out (use ExitPlayMode + re-enter if needed).
Avatar must have a VRCAvatarDescriptor. Spawns a GestureManager GameObject if none exists.")]
        public static string GestureManagerEnterPlayMode(string avatarName, bool confirm = false)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!confirm)
                return "Error: Dangerous operation - pass confirm=true to proceed. This will trigger a Unity domain reload and briefly disconnect the MCP bridge.";

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return "Error: Already in Play mode (or entering). Call ExitPlayMode first.";

            if (EditorApplication.isCompiling)
                return "Error: Unity is compiling. Wait and retry.";

            var go = FindGO(avatarName);
            if (go == null) return $"Error: GameObject '{avatarName}' not found.";

            // Get VRCAvatarDescriptor via base SDK type (stored as VRC_AvatarDescriptor on settings.favourite).
            var descriptor = go.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>();
            if (descriptor == null)
                return $"Error: '{avatarName}' has no VRC_AvatarDescriptor. GM cannot target it.";

            // GM's TryInitialize only picks up avatars that are activeInHierarchy after domain reload.
            // Silently activating would be surprising — but returning an error forces the AI to do
            // SetActive + re-enter, which wastes a round trip + another domain reload. Compromise:
            // activate here, record via Undo, and surface it in the success message.
            bool activated = false;
            if (!descriptor.gameObject.activeInHierarchy)
            {
                Undo.RecordObject(descriptor.gameObject, "Activate avatar for GM Enter Play-Mode");
                descriptor.gameObject.SetActive(true);
                activated = true;
                if (!descriptor.gameObject.activeInHierarchy)
                    return $"Error: '{avatarName}' is still inactive after SetActive(true) — an ancestor is disabled. Activate the parent chain manually and retry.";
            }

            var gm = FindInstance();
            bool spawned = false;
            if (gm == null)
            {
                var hostGo = new GameObject("GestureManager");
                Undo.RegisterCreatedObjectUndo(hostGo, "Spawn GestureManager");
                gm = hostGo.AddComponent<GestureManager>();
                spawned = true;
            }
            else if (!gm.gameObject.activeInHierarchy)
            {
                gm.gameObject.SetActive(true);
            }

            if (gm.settings == null)
                return "Error: GestureManager.settings is null. Open the GestureManager inspector once to initialize it, then retry.";

            gm.settings.favourite = descriptor;
            EditorUtility.SetDirty(gm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);

            // Fire and forget - domain reload ahead.
            EditorApplication.EnterPlaymode();

            var parts = new System.Collections.Generic.List<string>();
            if (spawned) parts.Add("spawned GestureManager");
            if (activated) parts.Add($"activated inactive avatar '{avatarName}' (Undo-recorded)");
            parts.Add($"set favourite='{avatarName}'");
            parts.Add("EnterPlaymode requested");
            return $"Success: {string.Join(", ", parts)}. MCP bridge will briefly disconnect during domain reload. After reload, GM auto-attaches.";
#endif
        }

        [AgentTool(@"Exit Unity Play mode (returns to Edit mode).
Wraps EditorApplication.ExitPlaymode(). Triggers a domain reload; same caveats as EnterPlayMode.
Pass confirm=true to proceed.")]
        public static string ExitPlayMode(bool confirm = false)
        {
            if (!confirm)
                return "Error: Dangerous operation - pass confirm=true to proceed. This will trigger a Unity domain reload.";
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                return "Not in Play mode - nothing to exit.";
            EditorApplication.ExitPlaymode();
            return "Success: ExitPlaymode requested. MCP bridge will briefly disconnect during domain reload.";
        }

        [AgentTool(@"Start a Gesture Manager preview on the given avatar GameObject.
Equivalent to clicking 'Enter Play-Mode with this Avatar' in the GM inspector.
Spawns a GestureManager GameObject if none exists. Avatar must have a VRCAvatarDescriptor.
Does NOT enter Unity Play mode - GM is an Edit-mode simulator.")]
        public static string GestureManagerEnterPreview(string avatarName)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed. Install vrchat.blackstartx.gesture-manager first.";
#else
            var go = FindGO(avatarName);
            if (go == null) return $"Error: GameObject '{avatarName}' not found.";

            var module = ModuleHelper.GetModuleFor(go);
            if (module == null) return $"Error: '{avatarName}' has no VRCAvatarDescriptor (or SDK not resolvable). GM cannot preview it.";
            if (!module.IsValidDesc())
            {
                var errs = string.Join("; ", module.GetErrors());
                return $"Error: Avatar descriptor is invalid: {errs}";
            }

            var gm = FindInstance();
            bool spawned = false;
            if (gm == null)
            {
                var hostGo = new GameObject("GestureManager");
                Undo.RegisterCreatedObjectUndo(hostGo, "Spawn GestureManager");
                gm = hostGo.AddComponent<GestureManager>();
                spawned = true;
            }
            else if (!gm.gameObject.activeInHierarchy)
            {
                gm.gameObject.SetActive(true);
            }

            // Unlink any previous module first.
            if (gm.Module != null) gm.UnlinkModule();

            gm.SetModule(module);

            if (gm.Module == null)
                return $"Error: SetModule returned null — GM refused to preview '{avatarName}'. Check Console for GM errors.";

            var msg = spawned
                ? $"Success: Spawned GestureManager and started preview on '{avatarName}'."
                : $"Success: Started preview on '{avatarName}' (reused existing GestureManager '{gm.gameObject.name}').";
            return msg;
#endif
        }

        [AgentTool(@"Stop the current Gesture Manager preview (if any).
Calls UnlinkModule() on the first GestureManager instance in the scene.
Leaves the GestureManager GameObject in place. Use GestureManagerDestroy if you want it removed.")]
        public static string GestureManagerExitPreview()
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "No GestureManager instance in scene — nothing to exit.";
            if (gm.Module == null) return $"GestureManager '{gm.gameObject.name}' is not previewing anything.";
            string avatarName = gm.Module.Name;
            gm.UnlinkModule();
            return $"Success: Exited preview of '{avatarName}'.";
#endif
        }

        [AgentTool(@"Read a VRC3 Animator parameter value WITHOUT side effects via Gesture Manager.
Returns current value, default value, and type. Requires an active GM preview.
Unlike GestureManagerSetParam, this does not trigger OnChange handlers.")]
        public static string GestureManagerGetParam(string paramName)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "Error: No GestureManager instance in scene.";
            if (gm.Module == null) return "Error: GestureManager is not previewing any avatar.";

            var vrc3 = gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            if (vrc3 == null) return $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3.";

            if (!vrc3.Params.TryGetValue(paramName, out var param))
            {
                var hints = vrc3.Params.Keys.Where(k => k.IndexOf(paramName, System.StringComparison.OrdinalIgnoreCase) >= 0).Take(10).ToArray();
                string hintStr = hints.Length > 0 ? $" Did you mean: {string.Join(", ", hints)}?" : "";
                return $"Error: Parameter '{paramName}' not found.{hintStr}";
            }

            string valueStr;
            switch (param.Type)
            {
                case UnityEngine.AnimatorControllerParameterType.Bool:
                case UnityEngine.AnimatorControllerParameterType.Trigger:
                    valueStr = param.BoolValue().ToString();
                    break;
                case UnityEngine.AnimatorControllerParameterType.Int:
                    valueStr = param.IntValue().ToString();
                    break;
                default:
                    valueStr = param.FloatValue().ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }

            return $"{paramName} ({param.Type}) = {valueStr}";
#endif
        }

        [AgentTool(@"List ALL Gesture Manager VRC3 parameters with their current runtime values in one call.
Includes both user-defined ExpressionParameters and VRC base params (GestureLeft, Viseme, Grounded, etc).
Much richer than ListAnimatorRuntimeParameters during GM preview (where Animator has no runtimeAnimatorController).
filter: case-insensitive substring match on param name. limit caps output (default 200).")]
        public static string ListGestureManagerParams(string filter = "", int limit = 200)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "Error: No GestureManager instance in scene.";
            if (gm.Module == null) return "Error: GestureManager is not previewing any avatar.";

            var vrc3 = gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            if (vrc3 == null) return $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3.";

            string filterLower = string.IsNullOrEmpty(filter) ? null : filter.ToLowerInvariant();
            var sb = new StringBuilder();
            sb.AppendLine($"GestureManager params on '{vrc3.Name}' ({vrc3.Params.Count} total)"
                + (filterLower != null ? $" filter='{filter}'" : ""));
            sb.AppendLine("---");

            int shown = 0;
            int remaining = 0;
            foreach (var kv in vrc3.Params)
            {
                if (filterLower != null && kv.Key.ToLowerInvariant().IndexOf(filterLower, System.StringComparison.Ordinal) < 0) continue;
                if (shown >= limit) { remaining++; continue; }

                var p = kv.Value;
                string valueStr;
                switch (p.Type)
                {
                    case UnityEngine.AnimatorControllerParameterType.Bool:
                    case UnityEngine.AnimatorControllerParameterType.Trigger:
                        valueStr = p.BoolValue().ToString();
                        break;
                    case UnityEngine.AnimatorControllerParameterType.Int:
                        valueStr = p.IntValue().ToString();
                        break;
                    default:
                        valueStr = p.FloatValue().ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                        break;
                }
                sb.AppendLine($"  [{p.Type}] {kv.Key} = {valueStr}");
                shown++;
            }

            if (remaining > 0) sb.AppendLine($"  ... {remaining} more (raise 'limit' to see).");
            if (shown == 0 && filterLower != null) sb.AppendLine($"  (no params matched '{filter}')");
            return sb.ToString().TrimEnd();
#endif
        }

        [AgentTool(@"Set a VRC3 Animator parameter value on the currently-previewed avatar via Gesture Manager.
Simulates OSC / Contact / radial menu input without needing the actual sender.
value format: float (e.g. '0.75'), int (e.g. '3'), or bool ('true'/'false'). Auto-detects param type.
Requires an active preview (see GestureManagerEnterPreview). Returns old → new value.")]
        public static string GestureManagerSetParam(string paramName, string value)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "Error: No GestureManager instance in scene. Call GestureManagerEnterPreview first.";
            if (gm.Module == null) return "Error: GestureManager is not previewing any avatar. Call GestureManagerEnterPreview first.";

            var vrc3 = gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            if (vrc3 == null) return $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3 (VRC3 avatar required).";

            if (!vrc3.Params.TryGetValue(paramName, out var param))
            {
                var hints = vrc3.Params.Keys.Where(k => k.IndexOf(paramName, System.StringComparison.OrdinalIgnoreCase) >= 0).Take(10).ToArray();
                string hintStr = hints.Length > 0 ? $" Did you mean: {string.Join(", ", hints)}?" : "";
                return $"Error: Parameter '{paramName}' not found on avatar.{hintStr}";
            }

            float oldValue = param.FloatValue();
            float newValue;

            switch (param.Type)
            {
                case UnityEngine.AnimatorControllerParameterType.Bool:
                case UnityEngine.AnimatorControllerParameterType.Trigger:
                    if (!TryParseBool(value, out var b))
                        return $"Error: Param '{paramName}' is {param.Type}; expected 'true' or 'false', got '{value}'.";
                    param.Set(vrc3, b);
                    newValue = b ? 1f : 0f;
                    break;
                case UnityEngine.AnimatorControllerParameterType.Int:
                    if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iv))
                        return $"Error: Param '{paramName}' is Int; expected integer, got '{value}'.";
                    param.Set(vrc3, iv);
                    newValue = iv;
                    break;
                case UnityEngine.AnimatorControllerParameterType.Float:
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fv))
                        return $"Error: Param '{paramName}' is Float; expected number, got '{value}'.";
                    param.Set(vrc3, fv);
                    newValue = fv;
                    break;
                default:
                    return $"Error: Unsupported parameter type {param.Type}.";
            }

            return $"Success: {paramName} ({param.Type}): {oldValue:F4} → {newValue:F4}";
#endif
        }

        [AgentTool(@"Inspect the CURRENT state of a FX/Base/Gesture/Action/Additive/Sitting/TPose/IKPose layer
inside Gesture Manager's PlayableGraph (the layers that GetAnimatorCurrentStateInfo CAN'T see because they
live in GM's private playable graph, not animator.runtimeAnimatorController).

layerName: case-insensitive substring match across '<PlayableType>.<innerLayerName>' (e.g., 'FX.Squish_Drive_Breast_C').
If empty, lists all layers across all playables and returns. Pass a specific name to get full state details.

Returns playable type (FX/Gesture/etc), inner layer index, layer weight, current state hash/name (if resolvable),
normalizedTime, isInTransition, and playing clip weights.")]
        public static string GetGmAnimatorCurrentStateInfo(string layerName = "")
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "Error: No GestureManager instance in scene.";
            if (gm.Module == null) return "Error: GestureManager is not previewing any avatar.";

            var vrc3 = gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            if (vrc3 == null) return $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3.";

            var vrc3Type = vrc3.GetType();
            var layersField = vrc3Type.GetField("_layers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (layersField == null) return "Error: Could not locate ModuleVrc3._layers via reflection (API changed?).";

            var layersDict = layersField.GetValue(vrc3) as System.Collections.IDictionary;
            if (layersDict == null) return "Error: _layers is null or not an IDictionary.";

            string filterLower = string.IsNullOrEmpty(layerName) ? null : layerName.ToLowerInvariant();
            bool listMode = filterLower == null;

            var sb = new StringBuilder();
            if (listMode) sb.AppendLine($"GestureManager PlayableGraph layers on '{vrc3.Name}':");

            System.Reflection.FieldInfo playableField = null;
            System.Reflection.FieldInfo weightField = null;

            foreach (System.Collections.DictionaryEntry entry in layersDict)
            {
                string playableTypeName = entry.Key?.ToString() ?? "?";
                var layerDataValue = entry.Value;
                if (layerDataValue == null) continue;

                var layerDataType = layerDataValue.GetType();
                if (playableField == null) playableField = layerDataType.GetField("Playable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (weightField == null) weightField = layerDataType.GetField("Weight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (playableField == null) return "Error: LayerData.Playable field not found via reflection.";

                var playableObj = playableField.GetValue(layerDataValue);
                if (!(playableObj is UnityEngine.Animations.AnimatorControllerPlayable playable)) continue;
                if (!UnityEngine.Playables.PlayableExtensions.IsValid(playable)) continue;

                int innerCount;
                try { innerCount = playable.GetLayerCount(); }
                catch { continue; }

                for (int i = 0; i < innerCount; i++)
                {
                    string innerName = playable.GetLayerName(i);
                    string fullName = $"{playableTypeName}.{innerName}";
                    if (listMode)
                    {
                        float w = playable.GetLayerWeight(i);
                        sb.AppendLine($"  [{playableTypeName}#{i}] '{innerName}' weight={w.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                        continue;
                    }

                    if (fullName.ToLowerInvariant().IndexOf(filterLower, System.StringComparison.Ordinal) < 0
                        && (innerName == null || innerName.ToLowerInvariant().IndexOf(filterLower, System.StringComparison.Ordinal) < 0))
                        continue;

                    // Found — dump detailed info.
                    var cur = playable.GetCurrentAnimatorStateInfo(i);
                    var clips = playable.GetCurrentAnimatorClipInfo(i);
                    bool inTransition = playable.IsInTransition(i);
                    float layerWeight = playable.GetLayerWeight(i);

                    sb.AppendLine($"Match: {fullName}  (playableType={playableTypeName}, innerIndex={i})");
                    sb.AppendLine($"  layerWeight: {layerWeight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"  currentState: <hash {cur.fullPathHash:X8}> (name not resolvable from playable — use InspectAnimatorController)");
                    sb.AppendLine($"  normalizedTime: {cur.normalizedTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"  length: {cur.length.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}s");
                    sb.AppendLine($"  speed: {cur.speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"  loop: {cur.loop}");
                    sb.AppendLine($"  isInTransition: {inTransition}");
                    if (inTransition)
                    {
                        var next = playable.GetNextAnimatorStateInfo(i);
                        var tr = playable.GetAnimatorTransitionInfo(i);
                        sb.AppendLine($"  -> nextState: <hash {next.fullPathHash:X8}>");
                        sb.AppendLine($"  -> transition.normalizedTime: {tr.normalizedTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine($"  playingClips ({clips.Length}):");
                    if (clips.Length == 0) sb.AppendLine("    (none)");
                    foreach (var ci in clips)
                        sb.AppendLine($"    - '{(ci.clip != null ? ci.clip.name : "<null>")}' weight={ci.weight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    return sb.ToString().TrimEnd();
                }
            }

            if (listMode) return sb.ToString().TrimEnd();
            return $"Error: No layer matches '{layerName}'. Use GetGmAnimatorCurrentStateInfo with empty layerName to list all.";
#endif
        }

#if GESTURE_MANAGER
        private static bool TryParseBool(string s, out bool result)
        {
            s = s?.Trim().ToLowerInvariant();
            if (s == "true" || s == "1") { result = true; return true; }
            if (s == "false" || s == "0") { result = false; return true; }
            result = false;
            return false;
        }
#endif
    }
}
