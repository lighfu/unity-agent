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
    public static partial class GestureManagerTools
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

        private static bool TryGetVrc3(out GestureManager gm,
            out BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3 vrc3, out string error)
        {
            gm = FindInstance();
            vrc3 = gm == null ? null : gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            error = gm == null ? "Error: No GestureManager instance in scene. Start a GM preview first."
                : gm.Module == null ? "Error: GestureManager is not previewing any avatar."
                : vrc3 == null ? $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3."
                : !vrc3.Active ? "Error: GestureManager preview is not active."
                : null;
            return error == null;
        }
#endif

        [AgentTool(@"Report the BlackStartX Gesture Manager state in the current scene.
Returns: whether GM is installed, how many GM instances exist, which avatar is being previewed,
whether the preview module is active, current gestures (left/right), and a short parameter sample.
section: all (default), instances, or controlled. Each selected collection has independent page metadata.
offset/limit apply independently to each collection; default limit 50, range 1..200.
Collections use ordinal name order then instance ID. Continue with that collection's nextOffset.
Use before calling GestureManagerSetParam / ExitPreview to confirm a preview is running.")]
        public static string GetGestureManagerState(int offset = 0, int limit = GestureManagerPaging.DefaultLimit,
            string section = "all")
        {
            string pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            string selectedSection = (section ?? "").Trim().ToLowerInvariant();
            if (selectedSection != "all" && selectedSection != "instances" && selectedSection != "controlled")
                return "Error: section must be all, instances, or controlled.";
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
                sb.AppendLine("  (No GestureManager GameObject present. Spawn the GestureManager prefab or call GestureManagerEnterPreview.)");

            var orderedInstances = instances.OrderBy(instance => instance.gameObject.name, System.StringComparer.Ordinal)
                .ThenBy(instance => instance.GetInstanceID()).ToArray();
            int shownInstances = 0;
            var instancePage = selectedSection == "controlled" ? Enumerable.Empty<GestureManager>()
                : orderedInstances.Skip(offset).Take(limit);
            foreach (var gm in instancePage)
            {
                sb.AppendLine($"  [{offset + shownInstances}] '{gm.gameObject.name}' instanceId={gm.GetInstanceID()} activeInHierarchy={gm.gameObject.activeInHierarchy}");
                shownInstances++;
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
                    foreach (var hand in new[] { "Left", "Right" })
                        if (vrc3.Params.TryGetValue("Gesture" + hand, out var gesture))
                        {
                            int index = gesture.IntValue();
                            string gestureName = index >= 0 && index <= 7 ? gm.Module.GetGestureTextNameByIndex(index) : "Unknown";
                            string weight = vrc3.Params.TryGetValue("Gesture" + hand + "Weight", out var wp)
                                ? wp.FloatValue().ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
                            sb.AppendLine($"      {hand} hand: {index} ({gestureName}), weight={weight}");
                        }
                    sb.AppendLine($"      Params: {vrc3.Params.Count} total");
                    var sample = vrc3.Params.OrderBy(kv => kv.Key, System.StringComparer.Ordinal).Take(8).Select(kv =>
                        $"{kv.Key}={kv.Value.FloatValue().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"      Sample: {string.Join(", ", sample)}");
                }
            }
            if (selectedSection != "controlled")
                sb.AppendLine("instancesPage: " + GestureManagerPaging.Metadata(instances.Length, offset, limit, shownInstances).ToJson());

            sb.AppendLine($"  ControlledAvatars: {GestureManager.ControlledAvatars.Count}");
            if (selectedSection != "instances")
            {
                int shownControlled = 0;
                foreach (var kv in GestureManager.ControlledAvatars.OrderBy(kv => kv.Key.name, System.StringComparer.Ordinal)
                    .ThenBy(kv => kv.Key.GetInstanceID()).Skip(offset).Take(limit))
                {
                    sb.AppendLine($"    - {kv.Key.name} instanceId={kv.Key.GetInstanceID()}");
                    shownControlled++;
                }
                sb.AppendLine("controlledAvatarsPage: " + GestureManagerPaging.Metadata(GestureManager.ControlledAvatars.Count, offset, limit, shownControlled).ToJson());
            }
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

            // ModuleHelper.GetModuleFor takes the descriptor component, not the GameObject
            // (GmgAvatarDescriptor is an alias for VRC.SDKBase.VRC_AvatarDescriptor).
            var descriptor = go.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>();
            if (descriptor == null)
                return $"Error: '{avatarName}' has no VRC_AvatarDescriptor. GM cannot preview it.";

            var module = ModuleHelper.GetModuleFor(descriptor);
            if (module == null) return $"Error: '{avatarName}' has a descriptor GM does not support (not an SDK2/SDK3 avatar). GM cannot preview it.";
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
Leaves the GestureManager GameObject in place.")]
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
Returns current value and type. Requires an active GM preview.
Omit paramName (or pass an empty string) to list parameters in ordinal name order.
offset is zero-based, limit defaults to 50 (1..200). The final Page JSON contains nextOffset.
For a named single-parameter read, offset must be 0.
Unlike GestureManagerSetParam, this does not trigger OnChange handlers.")]
        public static string GestureManagerGetParam(string paramName = "", int offset = 0,
            int limit = GestureManagerPaging.DefaultLimit)
        {
            string pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            if (string.IsNullOrWhiteSpace(paramName)) return ListGestureManagerParams(limit: limit, offset: offset);
            if (offset != 0) return "Error: offset must be 0 when reading a named parameter.";
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

        [AgentTool(@"List a page of Gesture Manager VRC3 parameters with their current runtime values.
Includes both user-defined ExpressionParameters and VRC base params (GestureLeft, Viseme, Grounded, etc).
Much richer than ListAnimatorRuntimeParameters during GM preview (where Animator has no runtimeAnimatorController).
filter: case-insensitive substring match on param name, applied before paging.
Ordinal name order; offset is zero-based, limit defaults to 50 (1..200; 0 is invalid).
The final Page JSON reports total matching items, returned, remaining, hasMore, and nextOffset.
Pass nextOffset as offset with the same filter/limit to continue. Reads are live, not snapshots.")]
        public static string ListGestureManagerParams(string filter = "", int limit = GestureManagerPaging.DefaultLimit,
            int offset = 0)
        {
            string pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var gm = FindInstance();
            if (gm == null) return "Error: No GestureManager instance in scene.";
            if (gm.Module == null) return "Error: GestureManager is not previewing any avatar.";

            var vrc3 = gm.Module as BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3;
            if (vrc3 == null) return $"Error: Current module is {gm.Module.GetType().Name}, not ModuleVrc3.";

            var matching = vrc3.Params.Where(kv => string.IsNullOrEmpty(filter)
                || kv.Key.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"GestureManager params on '{vrc3.Name}' ({vrc3.Params.Count} total)"
                + (!string.IsNullOrEmpty(filter) ? $" filter='{filter}'" : ""));
            sb.AppendLine("---");

            int shown = 0;
            foreach (var kv in matching.Skip(offset).Take(limit))
            {
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

            if (matching.Count == 0 && !string.IsNullOrEmpty(filter)) sb.AppendLine($"  (no params matched '{filter}')");
            sb.AppendLine("Page: " + GestureManagerPaging.Metadata(matching.Count, offset, limit, shown).ToJson());
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
                    if (float.IsNaN(fv) || float.IsInfinity(fv))
                        return $"Error: Param '{paramName}' requires a finite number.";
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
Filter is applied before paging; empty matches layers across all playables.
includeDetails=false returns a compact layer/weight list; true (default) also includes full state details.
Ordinal full-name order (then inner index); offset is zero-based, limit defaults to 50 (1..200).
The final Page JSON contains nextOffset. Use the same filter/limit on subsequent calls.
clipOffset/clipLimit page playing clips independently for each returned layer (default 16, 1..200).
Each detailed layer includes clipsPage JSON. Runtime values are live, not snapshots.

Returns playable type (FX/Gesture/etc), inner layer index, layer weight, current state hash/name (if resolvable),
normalizedTime, isInTransition, and playing clip weights.")]
        public static string GetGmAnimatorCurrentStateInfo(string layerName = "", bool includeDetails = true,
            int offset = 0, int limit = GestureManagerPaging.DefaultLimit, int clipOffset = 0, int clipLimit = 16)
        {
            string pagingError = GestureManagerPaging.Validate(offset, limit)
                ?? GestureManagerPaging.Validate(clipOffset, clipLimit)?.Replace("offset", "clipOffset").Replace("limit", "clipLimit");
            if (pagingError != null) return pagingError;
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

            var sb = new StringBuilder();
            sb.AppendLine($"GestureManager PlayableGraph layers on '{vrc3.Name}':");
            var matching = new System.Collections.Generic.List<(string fullName, string typeName, int index,
                UnityEngine.Animations.AnimatorControllerPlayable playable)>();
            var hashField = vrc3Type.GetField("AnimationHashSet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var hashes = hashField?.GetValue(vrc3) as System.Collections.Generic.Dictionary<int, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.DebugHash>;
            string StateName(int hash) => hashes != null && hashes.TryGetValue(hash, out var debugHash) ? debugHash.name : "[UNKNOWN]";

            System.Reflection.FieldInfo playableField = null;

            foreach (System.Collections.DictionaryEntry entry in layersDict)
            {
                string playableTypeName = entry.Key?.ToString() ?? "?";
                var layerDataValue = entry.Value;
                if (layerDataValue == null) continue;

                var layerDataType = layerDataValue.GetType();
                if (playableField == null) playableField = layerDataType.GetField("Playable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
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
                    if (!string.IsNullOrEmpty(layerName)
                        && fullName.IndexOf(layerName, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    matching.Add((fullName, playableTypeName, i, playable));
                }
            }

            var ordered = matching.OrderBy(layer => layer.fullName, System.StringComparer.Ordinal)
                .ThenBy(layer => layer.index).ToList();
            int shown = 0;
            foreach (var layer in ordered.Skip(offset).Take(limit))
            {
                string fullName = layer.fullName;
                string playableTypeName = layer.typeName;
                int i = layer.index;
                var playable = layer.playable;
                string innerName = playable.GetLayerName(i);
                shown++;
                if (!includeDetails)
                {
                    float w = playable.GetLayerWeight(i);
                    sb.AppendLine($"  [{playableTypeName}#{i}] '{innerName}' weight={w.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                    continue;
                }

                // Found — dump detailed info.
                var cur = playable.GetCurrentAnimatorStateInfo(i);
                var clips = playable.GetCurrentAnimatorClipInfo(i);
                bool inTransition = playable.IsInTransition(i);
                float layerWeight = playable.GetLayerWeight(i);

                sb.AppendLine($"Match: {fullName}  (playableType={playableTypeName}, innerIndex={i})");
                sb.AppendLine($"  layerWeight: {layerWeight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine($"  currentState: '{StateName(cur.fullPathHash)}' <hash {cur.fullPathHash:X8}>");
                sb.AppendLine($"  normalizedTime: {cur.normalizedTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine($"  length: {cur.length.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}s");
                sb.AppendLine($"  speed: {cur.speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                sb.AppendLine($"  loop: {cur.loop}");
                sb.AppendLine($"  isInTransition: {inTransition}");
                if (inTransition)
                {
                    var next = playable.GetNextAnimatorStateInfo(i);
                    var tr = playable.GetAnimatorTransitionInfo(i);
                    sb.AppendLine($"  -> nextState: '{StateName(next.fullPathHash)}' <hash {next.fullPathHash:X8}>");
                    sb.AppendLine($"  -> transition.normalizedTime: {tr.normalizedTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine($"  playingClips ({clips.Length}):");
                if (clips.Length == 0) sb.AppendLine("    (none)");
                int clipCount = clipOffset < clips.Length ? System.Math.Min(clipLimit, clips.Length - clipOffset) : 0;
                for (int c = 0; c < clipCount; c++)
                {
                    int clipIndex = clipOffset + c;
                    var ci = clips[clipIndex];
                    sb.AppendLine($"    - [{clipIndex}] '{(ci.clip != null ? ci.clip.name : "<null>")}' weight={ci.weight.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine("  clipsPage: " + GestureManagerPaging.Metadata(clips.Length, clipOffset, clipLimit, clipCount).ToJson());
            }

            if (ordered.Count == 0) sb.AppendLine($"  (no layers matched '{layerName}')");
            sb.AppendLine("Page: " + GestureManagerPaging.Metadata(ordered.Count, offset, limit, shown).ToJson());
            return sb.ToString().TrimEnd();
#endif
        }

#if GESTURE_MANAGER
        private static bool TryParseBool(string s, out bool result) => ToolUtility.TryParseBool(s, out result);
#endif
    }
}
