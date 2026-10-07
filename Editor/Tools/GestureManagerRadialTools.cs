using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;

#if GESTURE_MANAGER
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.Params;
using BlackStartX.GestureManager.Modules;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        [AgentTool(@"Read Gesture Manager's built-in radial menu state: remote/friend clones and their delays,
clone placement/synchronization settings, wheel palette, internal TPose/IKPose/HeadChop toggles,
and EditMode. Includes the Options menu catalog mapping UI labels to animator parameter names.
section is all, clones, options, palette, or modes. offset/limit (default 50, max 200) independently
page clones.items and optionsCatalog; each has total/returned/hasMore/nextOffset metadata.
Clone indices retain GM's source indices; options retain the UI catalog order. Zero is not unlimited.
Pages reflect the live preview; spawning/removing clones can change subsequent page indices.
Use GestureManagerGetParam/GestureManagerSetParam for catalog parameter controls. Expressions are read with
GestureManagerGetExpressionMenu. Looks changes the GM wheel's colors. Requires an active VRC3 preview.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetRadialState(int offset = 0, int limit = GestureManagerPaging.DefaultLimit, string section = "all")
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            var pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            var key = (section ?? "").Trim().ToLowerInvariant();
            if (key != "all" && key != "clones" && key != "options" && key != "palette" && key != "modes")
                return "Error: section must be all, clones, options, palette, or modes.";
            try
            {
                var state = JNode.Obj(("avatar", JNode.Str(module.Name)), ("section", JNode.Str(key)));
                if (key == "all" || key == "clones") state.AsObject["clones"] = RadialCloneState(module, offset, limit);
                if (key == "all" || key == "palette") state.AsObject["palette"] = RadialPaletteState();
                if (key == "all" || key == "modes") state.AsObject["modes"] = RadialModeState(module);
                if (key == "all" || key == "options")
                {
                    var page = GestureManagerPaging.Page(RadialOptionsCatalog(module).AsArray, offset, limit);
                    state.AsObject["optionsCatalog"] = page["items"];
                    state.AsObject["optionsCatalogPage"] = page["page"];
                }
                return state.ToJson();
            }
            catch (Exception ex) { return RadialError(ex); }
#endif
        }

        [AgentTool(@"Change Gesture Manager's Looks menu palette, saved using GM's own EditorPrefs/color APIs.
target is Main, Border or Selected; color is #RRGGBB or #RRGGBBAA.
reset=true restores the selected target's GM default; target=All is supported for reset only.
The palette is an editor-wide preference and all live GM wheels update. Requires a VRC3 preview.")]
        public static string GestureManagerSetRadialAppearance(string target, string color = "", bool reset = false)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out _, out var error)) return error;
            try
            {
                var key = (target ?? "").Trim().ToLowerInvariant();
                if (key != "main" && key != "border" && key != "selected" && key != "all")
                    return "Error: target must be Main, Border, Selected, or All (reset only).";
                if (reset && !string.IsNullOrWhiteSpace(color)) return "Error: Specify color or reset=true, not both.";
                if (key == "all" && !reset) return "Error: target=All requires reset=true.";
                Color parsed = default;
                if (!reset && (string.IsNullOrEmpty(color) || color[0] != '#' || (color.Length != 7 && color.Length != 9) ||
                    !ColorUtility.TryParseHtmlString(color, out parsed)))
                    return "Error: color must be #RRGGBB or #RRGGBBAA.";
                foreach (var name in new[] { "main", "border", "selected" })
                {
                    if (key != "all" && key != name) continue;
                    var palette = RadialPalette(name);
                    palette.Color = reset ? palette.Reset : parsed;
                }
                return JNode.Obj(("success", JNode.Bool(true)), ("palette", RadialPaletteState())).ToJson();
            }
            catch (Exception ex) { return RadialError(ex); }
#endif
        }

        [AgentTool(@"Configure the Gesture Manager Clones menu's clone spacing and shared network delay.
offsetJson is [x,y,z], measured from the source avatar once per clone; blank keeps current spacing.
networkDelay is seconds in 0..10; -1 keeps the current value. Changes reposition existing clones.
reset=true restores GM defaults: offset [-1,0,0], networkDelay 0.1; omit other settings when resetting.
Requires an active VRC3 preview outside dummy modes. Individual delays use GestureManagerClone.
Returned clones.items uses default paging; continue with GestureManagerGetRadialState(section=clones).")]
        public static string GestureManagerConfigureClones(string offsetJson = "", float networkDelay = -1f, bool reset = false)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                RadialRequireNormalMode(module);
                if (reset && (!string.IsNullOrWhiteSpace(offsetJson) || networkDelay != -1f))
                    return "Error: reset=true cannot be combined with offsetJson/networkDelay.";
                if (!RadialFinite(networkDelay) || networkDelay != -1f && (networkDelay < 0 || networkDelay > 10))
                    return "Error: networkDelay must be in 0..10 seconds, or -1 to keep the current value.";
                Vector3? offset = reset ? new Vector3(-1, 0, 0) : (Vector3?)null;
                if (!string.IsNullOrWhiteSpace(offsetJson)) offset = RadialOffset(offsetJson);
                var offsetField = RadialFieldInfo(module, "_cloneOffset");
                var delayField = RadialFieldInfo(module, "_cloneSyncDelay");
                var reorder = RadialMethod(module, "ReorderClones", Type.EmptyTypes);
                if (offset.HasValue)
                {
                    if (RadialClones(module).Cast<ModuleVrc3>().Any(c => !c.Avatar))
                        return "Error: A clone was removed externally. Wait for GM's next update and retry.";
                    offsetField.SetValue(module, offset.Value);
                    reorder.Invoke(module, null);
                }
                if (reset || networkDelay != -1f) delayField.SetValue(module, reset ? 0.1f : networkDelay);
                return JNode.Obj(("success", JNode.Bool(true)), ("clones", RadialCloneState(module))).ToJson();
            }
            catch (Exception ex) { return RadialError(ex); }
#endif
        }

        [AgentTool(@"Operate Gesture Manager's remote/friend clones using GM's own clone lifecycle.
action is spawnRemote, spawnFriend, remove, setFriend, or setDelay.
remove/setFriend/setDelay require cloneIndex from GestureManagerGetRadialState (zero-based).
setFriend uses isFriend. setDelay uses delay in seconds (0..10). Spawning uses GM's source snapshot,
network synchronization and configured spacing; clones are simulation objects removed with the preview.
Requires an active VRC3 preview outside dummy modes.
Returned clones.items uses default paging; continue with GestureManagerGetRadialState(section=clones).")]
        public static string GestureManagerClone(string action, int cloneIndex = -1, bool isFriend = false, float delay = 0f)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                RadialRequireNormalMode(module);
                var key = (action ?? "").Trim().ToLowerInvariant();
                var clones = RadialClones(module);
                if (key == "spawnremote" || key == "spawnfriend")
                {
                    if (cloneIndex != -1) return "Error: Spawning does not take a cloneIndex.";
                    if (!(RadialField(module, "_memoryClone") is UnityEngine.Object snapshot) || !snapshot)
                        return "Error: GM has no avatar snapshot for cloning. Restart the preview and retry.";
                    if (clones.Cast<ModuleVrc3>().Any(c => !c.Avatar))
                        return "Error: A clone was removed externally. Wait for GM's next update and retry.";
                    RadialCall(module, "SpawnClone", new[] { typeof(ModuleSettings) },
                        new ModuleSettings { isRemote = true, isOnFriendsList = key == "spawnfriend" });
                }
                else
                {
                    if (key != "remove" && key != "setfriend" && key != "setdelay")
                        return "Error: action must be spawnRemote, spawnFriend, remove, setFriend, or setDelay.";
                    if (cloneIndex < 0 || cloneIndex >= clones.Count)
                        return $"Error: cloneIndex must refer to an existing clone (count={clones.Count}).";
                    var clone = (ModuleVrc3)clones[cloneIndex];
                    if (key == "remove")
                    {
                        if (clones.Cast<ModuleVrc3>().Where((c, i) => i != cloneIndex).Any(c => !c.Avatar))
                            return "Error: Another clone was removed externally. Wait for GM's next update and retry.";
                        RadialCall(module, "RemoveClone", new[] { typeof(int) }, cloneIndex);
                    }
                    else if (key == "setfriend")
                    {
                        if (!clone.Avatar) return "Error: The clone avatar was removed. Wait for GM's next update and retry.";
                        var param = clone.GetParam("IsOnFriendsList");
                        if (param == null) return "Error: The clone's IsOnFriendsList parameter is unavailable.";
                        param.Set(clone, isFriend);
                        clone.Avatar.name = (string)RadialCall(module, "Clone", new[] { typeof(bool) }, isFriend);
                    }
                    else
                    {
                        if (!RadialFinite(delay) || delay < 0 || delay > 10)
                            return "Error: delay must be finite and in 0..10 seconds.";
                        RadialFieldInfo(clone, "_cloneDelay").SetValue(clone, delay);
                    }
                }
                return JNode.Obj(("success", JNode.Bool(true)), ("clones", RadialCloneState(module))).ToJson();
            }
            catch (Exception ex) { return RadialError(ex); }
#endif
        }

        [AgentTool(@"Set Gesture Manager's Options menu internal mode controls: TPose, IKPose, HeadChop, or EditMode.
enabled sets the selected toggle. These controls are separate from named Animator parameters.
TPose/IKPose/HeadChop use GM's own Vrc3Param handlers. EditMode uses GM's animation-editing dummy
avatar and exits through its StopExecution lifecycle; other dummy modes must be exited first.
Use GestureManagerGetRadialState for current toggles and the general Options parameter catalog.")]
        public static string GestureManagerSetRadialMode(string mode, bool enabled)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                var key = (mode ?? "").Trim().ToLowerInvariant();
                var dummy = RadialField(module, "DummyMode");
                if (key == "editmode")
                {
                    if (dummy != null && dummy.GetType().Name != "Vrc3EditMode")
                        return "Error: Exit the active dummy/pose/test-animation mode before changing EditMode.";
                    if (enabled && dummy == null) RadialCall(module, "EnableEditMode", Type.EmptyTypes);
                    else if (!enabled && dummy != null) RadialCall(dummy, "StopExecution", Type.EmptyTypes);
                }
                else
                {
                    string field = key == "tpose" ? "PoseT" : key == "ikpose" ? "PoseIK" : key == "headchop" ? "HeadChop" : null;
                    if (field == null) return "Error: mode must be TPose, IKPose, HeadChop, or EditMode.";
                    RadialRequireNormalMode(module);
                    if (key == "tpose" || key == "ikpose")
                    {
                        var layer = key == "tpose" ? VRCAvatarDescriptor.AnimLayerType.TPose : VRCAvatarDescriptor.AnimLayerType.IKPose;
                        var layers = (IDictionary)RadialField(module, "_layers");
                        if (layers == null || !layers.Contains(layer))
                            return $"Error: GM's {layer} animation layer is unavailable. Restart the preview and retry.";
                        var weight = RadialField(layers[layer], "Weight");
                        if (weight == null || !(RadialField(weight, "_playableMixer") is AnimationLayerMixerPlayable mixer) || !mixer.IsValid())
                            return $"Error: GM's {layer} layer mixer is invalid. Restart the preview and retry.";
                        var index = (int)RadialField(weight, "_index");
                        if (index < 0 || index >= mixer.GetInputCount())
                            return $"Error: GM's {layer} mixer input is unavailable. Restart the preview and retry.";
                    }
                    var param = (Vrc3Param)RadialField(module, field);
                    // GM's internal toggles have Name=null. Its OSC output lookup throws for
                    // these when running. Preserve Set/OnChange/radial updates while suppressing
                    // output for this unnamed parameter, restoring the sender in all cases.
                    var osc = RadialField(module, "OscModule");
                    var senderField = RadialFieldInfo(osc, "_sender");
                    var sender = senderField.GetValue(osc);
                    try { senderField.SetValue(osc, null); param.Set(module, enabled); }
                    finally { senderField.SetValue(osc, sender); }
                }
                return JNode.Obj(("success", JNode.Bool(true)), ("modes", RadialModeState(module))).ToJson();
            }
            catch (Exception ex) { return RadialError(ex); }
#endif
        }

#if GESTURE_MANAGER
        private const BindingFlags RadialBinding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo RadialFieldInfo(object target, string name) => target.GetType().GetField(name, RadialBinding)
            ?? throw new NotSupportedException($"GM radial field '{name}' is unavailable in this package version.");
        private static object RadialField(object target, string name) => RadialFieldInfo(target, name).GetValue(target);
        private static MethodInfo RadialMethod(object target, string name, Type[] args) => target.GetType().GetMethod(name, RadialBinding, null, args, null)
            ?? throw new NotSupportedException($"GM radial method '{name}' is unavailable in this package version.");
        private static object RadialCall(object target, string name, Type[] types, params object[] args) => RadialMethod(target, name, types).Invoke(target, args);
        private static string RadialError(Exception ex)
        {
            while (ex is TargetInvocationException invocation && invocation.InnerException != null) ex = invocation.InnerException;
            return $"Error: Gesture Manager radial tools: {ex.Message}";
        }
        private static bool RadialFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static JNode RadialNumber(float value) => RadialFinite(value) ? JNode.Num(value) : JNode.Str(value.ToString(CultureInfo.InvariantCulture));
        private static IList RadialClones(ModuleVrc3 module) => (IList)RadialField(module, "_clones");
        private static void RadialRequireNormalMode(ModuleVrc3 module)
        {
            if (RadialField(module, "DummyMode") != null)
                throw new ArgumentException("Exit GM's dummy/pose/test-animation mode before operating these controls.");
        }
        private static Vector3 RadialOffset(string json)
        {
            var node = JNode.Parse(json);
            if (node.Type != JNode.JType.Array || node.Count != 3 || node.AsArray.Any(v => v.Type != JNode.JType.Number || !RadialFinite((float)v.AsNumber)))
                throw new ArgumentException("offsetJson must be [x,y,z] with three finite 32-bit numbers.");
            return new Vector3((float)node[0].AsNumber, (float)node[1].AsNumber, (float)node[2].AsNumber);
        }
        private static JNode RadialCloneState(ModuleVrc3 module, int pageOffset = 0, int limit = GestureManagerPaging.DefaultLimit)
        {
            var offset = (Vector3)RadialField(module, "_cloneOffset");
            var snapshot = RadialClones(module).Cast<ModuleVrc3>().ToArray();
            var clones = snapshot.Select((clone, i) => new { Clone = clone, Index = i }).Skip(pageOffset).Take(limit).Select(entry =>
                JNode.Obj(("index", JNode.Num(entry.Index)), ("name", JNode.Str(entry.Clone.Name)),
                    ("available", JNode.Bool(entry.Clone.Avatar && entry.Clone.AvatarDescriptor)),
                    ("isFriend", JNode.Bool(entry.Clone.GetParam("IsOnFriendsList")?.BoolValue() ?? entry.Clone.Settings?.isOnFriendsList ?? false)),
                    ("delay", RadialNumber((float)RadialField(entry.Clone, "_cloneDelay"))))).ToArray();
            return JNode.Obj(("offset", JNode.Arr(RadialNumber(offset.x), RadialNumber(offset.y), RadialNumber(offset.z))),
                ("networkDelay", RadialNumber((float)RadialField(module, "_cloneSyncDelay"))), ("items", JNode.Arr(clones)),
                ("page", GestureManagerPaging.Metadata(snapshot.Length, pageOffset, limit, clones.Length)));
        }
        private static RadialMenuUtility.Colors.CustomColor RadialPalette(string target) => target == "main"
            ? RadialMenuUtility.Colors.Custom.Main : target == "border"
            ? RadialMenuUtility.Colors.Custom.Border : RadialMenuUtility.Colors.Custom.Selected;
        private static JNode RadialPaletteState()
        {
            return JNode.Obj(new[] { "main", "border", "selected" }.Select(name =>
            {
                // Accessing Colors.Custom initializes all CustomColor instances and writes
                // EditorPrefs. Read persisted colors and GM's defaults without that constructor.
                var title = char.ToUpperInvariant(name[0]) + name.Substring(1);
                var defaults = typeof(RadialMenuUtility.Colors).GetNestedType("Default", BindingFlags.NonPublic);
                var defaultField = defaults?.GetField(title, BindingFlags.Static | BindingFlags.Public);
                if (defaultField == null) throw new NotSupportedException("GM's default radial colors are unavailable in this package version.");
                var fallback = (Color)defaultField.GetValue(null);
                var color = ColorUtility.TryParseHtmlString(EditorPrefs.GetString("GM3 " + title + " Color"), out var saved) ? saved : fallback;
                Color.RGBToHSV(color, out var h, out var s, out var v);
                return (name, JNode.Obj(("color", JNode.Str("#" + ColorUtility.ToHtmlStringRGBA(color))),
                    ("default", JNode.Str("#" + ColorUtility.ToHtmlStringRGBA(fallback))),
                    ("hsv", JNode.Arr(RadialNumber(h), RadialNumber(s), RadialNumber(v)))));
            }).ToArray());
        }
        private static JNode RadialModeState(ModuleVrc3 module)
        {
            var dummy = RadialField(module, "DummyMode");
            return JNode.Obj(("TPose", JNode.Bool(((Vrc3Param)RadialField(module, "PoseT")).BoolValue())),
                ("IKPose", JNode.Bool(((Vrc3Param)RadialField(module, "PoseIK")).BoolValue())),
                ("HeadChop", JNode.Bool(((Vrc3Param)RadialField(module, "HeadChop")).BoolValue())),
                ("EditMode", JNode.Bool(dummy?.GetType().Name == "Vrc3EditMode")),
                ("dummyMode", dummy == null ? JNode.NullNode : JNode.Str(dummy.GetType().Name)));
        }
        private static JNode RadialOptionsCatalog(ModuleVrc3 module)
        {
            var entries = new[]
            {
                ("Extra", "IsLocal", "IsLocal", "Bool"),
                ("Extra", "Gesture Right Weight", "GestureRightWeight", "0..1"),
                ("Extra", "Gesture Left Weight", "GestureLeftWeight", "0..1"),
                ("Extra", "MuteSelf", "MuteSelf", "Bool"),
                ("Extra", "InStation", "InStation", "Bool"),
                ("Extra", "Earmuffs", "Earmuffs", "Bool"),
                ("Extra", "IsOnFriendsList", "IsOnFriendsList", "Bool"),
                ("Tracking", "Tracking Type", "TrackingType", "0=Uninitialized,1=Generic,2=Hands-only,3=Head And Hands,4=4-Point VR,6=Full Body"),
                ("Tracking", "VRMode", "VRMode", "0=desktop,1=VR"),
                ("States", "AFK", "AFK", "Bool"),
                ("States", "Viseme", "Viseme", "0..14 for blendshapes; 0..100 otherwise"),
                ("States", "Seated", "Seated", "Bool"),
                ("States", "Avatar Culling", "IsAnimatorEnabled", "0=culled,1=enabled; manual control disabled while simulateCulling is enabled"),
                ("Locomotion", "Grounded", "Grounded", "Bool"),
                ("Locomotion", "Falling Speed", "VelocityY", "UI radial 0..1 maps to 0..-22 m/s"),
                ("Locomotion", "Upright", "Upright", "0..1"),
                ("Locomotion", "Velocity X", "VelocityX", "UI axis -1..1 maps to -7..7 m/s"),
                ("Locomotion", "Velocity Z", "VelocityZ", "UI axis -1..1 maps to -7..7 m/s")
            };
            return JNode.Arr(entries.Select(item =>
            {
                var param = module.GetParam(item.Item3);
                return JNode.Obj(("section", JNode.Str(item.Item1)), ("label", JNode.Str(item.Item2)),
                    ("parameter", JNode.Str(item.Item3)), ("values", JNode.Str(item.Item4)),
                    ("available", JNode.Bool(param != null)), ("current", param == null ? JNode.NullNode : RadialNumber(param.FloatValue())));
            }).ToArray());
        }
#endif
    }
}
