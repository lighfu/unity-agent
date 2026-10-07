using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using AjisaiFlow.UnityAgent.Editor.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEditorInternal;
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Contains(string text, string expected) => Check(text.Contains(expected, StringComparison.Ordinal), $"Expected '{expected}' in '{text}'.");
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static object BackgroundField(string name) => typeof(BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools.AvatarTools).GetNestedType("AvatarBackground", BindingFlags.NonPublic).GetField(name, Flags).GetValue(null);

    private static int Main(string[] args)
    {
        try
        {
#if GESTURE_MANAGER
            TestValidationAndPreview();
            TestSafeGetters();
            TestCameraAndContacts();
            TestPoseAndAnimation();
            TestBackgroundOwnership();
            TestProfilerRestoration();
            TestTrackingAndWeights();
#else
            foreach (var method in typeof(GestureManagerTools).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetCustomAttribute<AjisaiFlow.UnityAgent.SDK.AgentToolAttribute>() != null))
            {
                var values = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : "").ToArray();
                Contains((string)method.Invoke(null, values), "Gesture Manager package not installed");
            }
#endif
            if (args.Length == 2 && args[0] == "--gm-assembly-dir") CheckOfficialMetadata(Path.GetFullPath(args[1]));
            Console.WriteLine($"PASS: {_checks} Gesture Manager utility checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
#if GESTURE_MANAGER
            TestRig.Reset();
#endif
        }
    }

#if GESTURE_MANAGER
    private static void TestValidationAndPreview()
    {
        TestRig.Reset();
        TestRig.Manager = null;
        Contains(GestureManagerTools.GestureManagerGetTrackingControl(), "No GestureManager");
        TestRig.Reset().Active = false;
        Contains(GestureManagerTools.GestureManagerGetSceneCamera(), "preview is not active");
        TestRig.Reset();
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -0.1f, 1.1f })
            Contains(GestureManagerTools.GestureManagerSetControllerWeight("FX", invalid), "Error:");
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, 0f, 20.1f })
            Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true, distance: invalid), "Error:");
        Check(!Profiler.enabled, "Invalid settings must not enable the profiler.");
        Check(!(Renderer)BackgroundField("_cover"), "Invalid settings must not create a background.");
    }

    private static void TestCameraAndContacts()
    {
        var module = TestRig.Reset();
        var camera = Camera.allCameras[0];
        Contains(GestureManagerTools.GestureManagerSetSceneCamera(true), "Success:");
        Check(camera.SyncCount == 1, "syncNow must invoke the GM synchronization operation.");
        Contains(GestureManagerTools.GestureManagerGetSceneCamera(), "enabled=True");
        SceneView.lastActiveSceneView = null;
        Contains(GestureManagerTools.GestureManagerSetSceneCamera(true), "Error: No active Scene View");
        Check(camera.SyncCount == 1, "A missing Scene View must not copy camera values.");
        Contains(GestureManagerTools.GestureManagerSetSceneCamera(false), "enabled=False");
        Contains(GestureManagerTools.GestureManagerSetSceneCamera(true, "Missing", false), "Error:");
        Contains(GestureManagerTools.GestureManagerSetClickableContacts(true, "Hand"), "tag='Hand'");
        Contains(GestureManagerTools.GestureManagerSetClickableContacts(false), "enabled=False");
        Check(module.AvatarTools.ContactsClickable.Released == 1, "Disabling contacts must release held contacts.");
        Contains(GestureManagerTools.GestureManagerGetClickableContacts(), "tag=''");
    }

    private static void TestSafeGetters()
    {
        var module = TestRig.Reset();
        var toolsType = module.AvatarTools.GetType();
        var sceneType = toolsType.GetNestedType("UpdateSceneCamera", BindingFlags.Public);
        var originalCamera = new GameObject { name = "Existing Target" }.Attach(new Camera { name = "Existing Target" });
        sceneType.GetMethod("Set", Flags).Invoke(null, new object[] { originalCamera });
        // This is a fresh preview with no SceneCamera utility. Its official lazy constructor
        // would replace Existing Target with MainCamera and write the persisted preference.
        int constructors = BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools.AvatarTools.UpdateSceneCamera.ConstructorCalls;
        int writes = BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools.AvatarTools.UpdateSceneCamera.PreferenceWrites;
        Contains(GestureManagerTools.GestureManagerGetSceneCamera(), "camera='Existing Target'");
        Check(toolsType.GetField("_sceneCamera", Flags).GetValue(module.AvatarTools) == null, "A Safe Scene Camera getter must not construct the lazy utility.");
        Check(BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools.AvatarTools.UpdateSceneCamera.ConstructorCalls == constructors, "Inspecting camera state must not run the side-effecting constructor.");
        Check(BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools.AvatarTools.UpdateSceneCamera.PreferenceWrites == writes, "Inspecting camera state must not write persisted settings.");
        Check(sceneType.GetField("_camera", Flags).GetValue(null) == originalCamera, "Inspecting camera state must preserve the existing global target.");
        Profiler.enabled = true;
        ProfilerDriver.enabled = false;
        Contains(GestureManagerTools.GestureManagerGetAnimatorPerformance(), "profilerEnabled=True");
        Check(Profiler.enabled && !ProfilerDriver.enabled, "Performance inspection must not change global profiler flags.");
        Contains(GestureManagerTools.GestureManagerGetClickableContacts(), "enabled=False");
        Check(module.AvatarTools.ContactsClickable.Released == 0, "Contact inspection must not release or trigger receivers.");
        Contains(GestureManagerTools.GestureManagerSetSceneCamera(false), "Success:");
    }

    private static void TestPoseAndAnimation()
    {
        var module = TestRig.Reset();
        Contains(GestureManagerTools.GestureManagerSetPoseAvatar(true), "enabled=True");
        Check(module.AvatarAnimator.applyRootMotion, "Pose toggle must use GM's root-motion lifecycle.");
        Contains(GestureManagerTools.GestureManagerSetPoseAvatar(false), "enabled=False");
        module.AvatarAnimator.isHuman = false;
        Contains(GestureManagerTools.GestureManagerSetPoseAvatar(true), "requires a humanoid");
        Contains(GestureManagerTools.GestureManagerPlayTestAnimation("Assets/Missing.anim"), "Error:");
        var clip = new AnimationClip { name = "Emote" };
        AssetDatabase.Assets["Assets/Emote.anim"] = clip;
        Contains(GestureManagerTools.GestureManagerPlayTestAnimation("Assets/Emote.anim"), "playing=True");
        Contains(GestureManagerTools.GestureManagerGetTestAnimation(), "Assets/Emote.anim");
        Contains(GestureManagerTools.GestureManagerStopTestAnimation(), "playing=False");
        Check(module.CustomAnim == clip, "Stopping must retain the selected clip.");
        Contains(GestureManagerTools.GestureManagerPlayTestAnimation(), "playing=True");
    }

    private static void TestBackgroundOwnership()
    {
        var first = TestRig.Reset();
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true, textureAssetPath: "missing"), "Error:");
        Check(!(Renderer)BackgroundField("_cover"), "Missing texture must not create a hidden object.");
        var texture = new Texture { name = "Backdrop" };
        AssetDatabase.Assets["Assets/Backdrop.png"] = texture;
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true, textureAssetPath: "Assets/Backdrop.png", constant: true), "enabled=True");
        var cover = (Renderer)BackgroundField("_cover");
        var material = cover.sharedMaterial;
        Contains(GestureManagerTools.GestureManagerGetAvatarBackground(), "textureAssetPath='Assets/Backdrop.png'");
        var second = new ModuleVrc3();
        TestRig.Module = second;
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true), "Another preview owns");
        Check((Renderer)BackgroundField("_cover") == cover, "A second preview must not take over a globally shared background silently.");
        first.Active = false;
        EditorApplication.Tick();
        Check(!cover && !material, "Ending the owner preview must destroy its background and temporary material.");
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true), "Success:");
        var nextCover = (Renderer)BackgroundField("_cover");
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(false), "enabled=False");
        Check(!nextCover, "Explicit disable must remove the background.");
        Contains(GestureManagerTools.GestureManagerSetAvatarBackground(true), "Success:");
        nextCover = (Renderer)BackgroundField("_cover");
        AssemblyReloadEvents.Reload();
        Check(!nextCover, "Reload must clean up an owned background.");
    }

    private static void TestProfilerRestoration()
    {
        foreach (var original in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var module = TestRig.Reset();
            Profiler.enabled = original.Item1;
            ProfilerDriver.enabled = original.Item2;
            Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Success:");
            Check(Profiler.enabled && ProfilerDriver.enabled, "Starting must enable both profiler flags.");
            Contains(GestureManagerTools.GestureManagerGetAnimatorPerformance(), "recordingThroughTool=True");
            Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Success:");
            module.Active = false;
            // GM's Unlink disables the global profiler before our owner lifecycle observes it.
            Profiler.enabled = ProfilerDriver.enabled = false;
            EditorApplication.Tick();
            Check(Profiler.enabled == original.Item1 && ProfilerDriver.enabled == original.Item2, "Preview end must restore both original flags, including after repeated starts.");
        }
        TestRig.Reset();
        Profiler.enabled = true;
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(false), "No benchmark");
        Check(Profiler.enabled, "Stopping without tool ownership must preserve a profiler enabled elsewhere.");
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Success:");
        AssemblyReloadEvents.Reload();
        Check(Profiler.enabled && !ProfilerDriver.enabled, "Reload must restore original flags.");
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Success:");
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(false), "Success:");
        Check(Profiler.enabled && !ProfilerDriver.enabled, "Explicit stop must restore original flags.");
        var owner = TestRig.Reset();
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Success:");
        TestRig.Module = new ModuleVrc3();
        Contains(GestureManagerTools.GestureManagerSetAnimatorPerformance(true), "Another preview started");
        Check(Profiler.enabled, "An ownership conflict must preserve the owner's profiler.");
        TestRig.Module = owner;
        Profiler.enabled = ProfilerDriver.enabled = false;
        Contains(GestureManagerTools.GestureManagerGetAnimatorPerformance(), "recordingThroughTool=False");
        owner.Active = false;
        EditorApplication.Tick();
    }

    private static void TestTrackingAndWeights()
    {
        var first = TestRig.Reset();
        var second = new ModuleVrc3();
        Contains(GestureManagerTools.GestureManagerSetTrackingControl("head", "Animation"), "Success:");
        Check(first.TrackingControls["Head"] == VRC_AnimatorTrackingControl.TrackingType.Animation, "Requested tracking target must change.");
        Check(second.TrackingControls["Head"] == VRC_AnimatorTrackingControl.TrackingType.Tracking, "Tracking state must be per-preview.");
        Contains(GestureManagerTools.GestureManagerSetTrackingControl("Head", "NoChange"), "Success:");
        Check(first.TrackingControls["Head"] == VRC_AnimatorTrackingControl.TrackingType.Animation, "NoChange must preserve tracking state.");
        Contains(GestureManagerTools.GestureManagerSetTrackingControl("all", "Tracking"), "Success:");
        Check(first.TrackingControls.Values.All(v => v == VRC_AnimatorTrackingControl.TrackingType.Tracking), "all must update each part.");
        Contains(GestureManagerTools.GestureManagerSetTrackingControl("missing", "Tracking"), "Unknown bodyPart");
        Contains(GestureManagerTools.GestureManagerSetTrackingControl("Head", "7"), "Error:");
        Contains(GestureManagerTools.GestureManagerSetLocomotion(false), "enabled=False");
        Contains(GestureManagerTools.GestureManagerSetPoseSpace(true), "PoseSpace=Pose");
        Contains(GestureManagerTools.GestureManagerGetTrackingControl(), "Locomotion: enabled=False");
        var data = first._layers[VRCAvatarDescriptor.AnimLayerType.FX];
        Contains(GestureManagerTools.GestureManagerSetControllerWeight("FX", 0.4f), "Success:");
        Check(data.Weight.Weight == 0.4f && data.Weight._control == null, "Whole-controller changes must cancel an active controller blend.");
        data.Weight._subControls[1] = new object();
        Contains(GestureManagerTools.GestureManagerSetControllerWeight("FX", 0.2f, 1), "Success:");
        Check(data.Playable.State.Layers[1] == 0.2f && !data.Weight._subControls.ContainsKey(1), "Sub-layer changes must cancel only that pending blend.");
        Contains(GestureManagerTools.GestureManagerSetControllerWeight("FX", 0.2f, 2), "Error:");
        Check(data.Playable.State.Layers[1] == 0.2f, "Invalid index must preserve valid layer weights.");
    }
#endif

    private sealed class DependencyContext : AssemblyLoadContext
    {
        private readonly string _directory;
        public DependencyContext(string directory) : base(isCollectible: true) { _directory = directory; }
        protected override Assembly Load(AssemblyName name)
        {
            var path = Path.Combine(_directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private static void CheckOfficialMetadata(string directory)
    {
        var context = new DependencyContext(directory);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "vrchat.blackstartx.gesture-manager.editor.dll"));
            const string prefix = "BlackStartX.GestureManager.Editor.Modules.Vrc3.";
            var module = assembly.GetType(prefix + "ModuleVrc3", true);
            var tools = assembly.GetType(prefix + "Tools.AvatarTools", true);
            var dynamicTool = assembly.GetType(prefix + "Tools.GmgDynamicFunction", true);
            var weight = assembly.GetType(prefix + "AnimatorControllerWeight", true);
            Type Nested(string name) => tools.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic) ?? throw new Exception("Missing nested type " + name);
            void Field(Type type, string name, string fieldType, bool isStatic = false)
            {
                var field = type.GetField(name, Flags);
                Check(field != null, $"Official API missing {type.FullName}.{name}");
                Check(field.FieldType.FullName == fieldType && field.IsStatic == isStatic, $"Official API type/storage mismatch: {type.FullName}.{name}={field.FieldType.FullName}");
            }
            void Property(Type type, string name, string propertyType, bool isStatic = false)
            {
                var property = type.GetProperty(name, Flags);
                Check(property != null && property.PropertyType.FullName == propertyType && property.GetMethod.IsStatic == isStatic, $"Official API property mismatch: {type.FullName}.{name}");
            }
            void Method(Type type, string name, bool isStatic, params string[] parameters)
            {
                var method = type.GetMethods(Flags).SingleOrDefault(m => m.Name == name && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
                Check(method != null && method.IsStatic == isStatic && method.ReturnType == typeof(void), $"Official API method mismatch: {type.FullName}.{name}");
            }
            Field(module, "AvatarTools", tools.FullName);
            Field(module, "PoseMode", "System.Boolean");
            Field(module, "LocomotionDisabled", "System.Boolean");
            Field(module, "PoseSpace", "System.Boolean");
            var tracking = module.GetField("TrackingControls", Flags);
            Check(tracking != null && !tracking.IsStatic && tracking.IsInitOnly, "Official tracking dictionary must belong to the preview instance.");
            Check(tracking.FieldType.GetGenericArguments()[0] == typeof(string) && tracking.FieldType.GetGenericArguments()[1].FullName == "VRC.SDKBase.VRC_AnimatorTrackingControl+TrackingType", "Official tracking dictionary key/value types must match.");
            Property(module, "MainCamera", "UnityEngine.Camera", true);
            Field(dynamicTool, "Foldout", "System.Boolean");
            Property(dynamicTool, "Active", "System.Boolean");
            var scene = Nested("UpdateSceneCamera");
            Field(scene, "_camera", "UnityEngine.Camera", true);
            var activePreference = scene.GetField("IsActive", Flags);
            Check(activePreference != null && activePreference.IsStatic, "Official Scene Camera enabled preference must be static.");
            Property(activePreference.FieldType, "Property", "System.Boolean");
            Method(scene, "Set", true, "UnityEngine.Camera");
            Method(scene, "Update", false, module.FullName);
            var contacts = Nested("ClickableContacts");
            Method(contacts, "Toggle", false, module.FullName);
            Method(contacts, "Disable", false);
            var tag = contacts.GetField("_tag", Flags);
            Check(tag != null && !tag.IsStatic, "Official clickable tag must be instance state.");
            Property(tag.FieldType, "Property", "System.String");
            Method(Nested("AvatarPose"), "Toggle", false, module.FullName);
            var background = Nested("AvatarBackground");
            Field(background, "_cover", "UnityEngine.Renderer", true);
            Field(background, "_cameraOb", "UnityEngine.Camera", true);
            Field(background, "_texture", "UnityEngine.Texture", true);
            Field(background, "_distance", "System.Single", true);
            Field(background, "_realTime", "System.Boolean", true);
            Method(background, "SetUp", true);
            Method(background, "ToggleOff", true);
            var performance = Nested("AnimatorPerformance");
            Method(performance, "Toggle", false, module.FullName);
            var benchmark = performance.GetField("_benchmark", Flags);
            Check(benchmark != null && !benchmark.IsStatic && typeof(IDictionary).IsAssignableFrom(benchmark.FieldType), "Official benchmark must be an instance dictionary.");
            var sample = benchmark.FieldType.GetGenericArguments()[1];
            Property(sample, "Frame", "System.Int32");
            Field(sample, "_last", "System.Single");
            Field(sample, "_average", "System.Single");
            Field(sample, "_maximum", "System.Single");
            var layers = module.GetField("_layers", Flags);
            Check(layers != null && !layers.IsStatic && typeof(IDictionary).IsAssignableFrom(layers.FieldType), "Official layers must be an instance dictionary.");
            var layerData = layers.FieldType.GetGenericArguments()[1];
            Field(layerData, "Playable", "UnityEngine.Animations.AnimatorControllerPlayable");
            Field(layerData, "Weight", weight.FullName);
            Field(weight, "_control", "VRC.SDKBase.VRC_PlayableLayerControl");
            var pending = weight.GetField("_subControls", Flags);
            Check(pending != null && !pending.IsStatic && typeof(IDictionary).IsAssignableFrom(pending.FieldType), "Official sublayer blend timers must be an instance dictionary.");
            Console.WriteLine("Official GM assembly reflection contract: PASS.");
        }
        finally { context.Unload(); }
    }
}
