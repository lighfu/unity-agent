using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Reflection-driven fixture fields intentionally have no direct callers.
#pragma warning disable CS0169, CS0649

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public bool Persistent;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
        public static void DestroyImmediate(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            if (value is GameObject go) foreach (var component in go.Components) component.Destroyed = true;
        }
    }
    public class Transform : Object { }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public Transform transform = new Transform();
        public List<Component> Components = new List<Component>();
        public T GetComponent<T>() where T : Component => Components.Find(c => c is T) as T;
        public T Attach<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }
    public class Camera : Component
    {
        public bool enabled = true;
        public int SyncCount;
        public static Camera[] allCameras = Array.Empty<Camera>();
    }
    public class Texture : Object { }
    public class AnimationClip : Object { }
    public class Material : Object { }
    public class Renderer : Component { public Material sharedMaterial = new Material(); }
    public class Animator : Component { public bool isHuman = true; public bool applyRootMotion; }
}
namespace UnityEditor
{
    public class SceneView : UnityEngine.Object { public static SceneView lastActiveSceneView; public UnityEngine.Camera camera; }
    public static class Undo
    {
        public static int Records;
        public static void RecordObject(UnityEngine.Object value, string message) { Records++; }
        public static void RecordObjects(UnityEngine.Object[] values, string message) { Records += values.Length; }
    }
    public static class EditorUtility { public static bool IsPersistent(UnityEngine.Object value) => value.Persistent; }
    public static class AssetDatabase
    {
        public static Dictionary<string, UnityEngine.Object> Assets = new Dictionary<string, UnityEngine.Object>();
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => Assets.TryGetValue(path, out var value) ? value as T : null;
        public static string GetAssetPath(UnityEngine.Object obj)
        {
            foreach (var pair in Assets) if (pair.Value == obj) return pair.Key;
            return "";
        }
    }
    public static class EditorApplication
    {
        public static event Action update;
        public static event Action quitting;
        public static void Tick() => update?.Invoke();
        public static void Quit() => quitting?.Invoke();
    }
    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload;
        public static void Reload() => beforeAssemblyReload?.Invoke();
    }
}
namespace UnityEditorInternal { public static class ProfilerDriver { public static bool enabled; } }
namespace UnityEngine.Profiling { public static class Profiler { public static bool enabled; } }
namespace UnityEngine.Playables
{
    public static class PlayableExtensions { public static bool IsValid(this UnityEngine.Animations.AnimatorControllerPlayable playable) => playable.State != null; }
}
namespace UnityEngine.Animations
{
    public sealed class PlayableState { public float[] Layers = { 1f, 1f }; }
    public struct AnimatorControllerPlayable
    {
        public PlayableState State;
        public int GetLayerCount() => State.Layers.Length;
        public void SetLayerWeight(int layer, float value) => State.Layers[layer] = value;
    }
}
namespace VRC.SDKBase
{
    public class VRC_AnimatorTrackingControl { public enum TrackingType { NoChange, Tracking, Animation } }
}
namespace VRC.SDK3.Avatars.Components
{
    public class VRCAvatarDescriptor { public enum AnimLayerType { Base, Additive, Sitting, TPose, IKPose, Gesture, Action, FX } }
}
namespace BlackStartX.GestureManager
{
    public class GestureManager { public static string Version => "3.9.9"; }
}
namespace BlackStartX.GestureManager.Editor.Modules.Vrc3
{
    using UnityEngine;
    using UnityEngine.Animations;
    using VRC.SDKBase;
    using VRC.SDK3.Avatars.Components;
    using BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools;
    public class AnimatorControllerWeight
    {
        internal object _control = new object();
        internal Dictionary<int, object> _subControls = new Dictionary<int, object>();
        public float Weight { get; private set; } = 1f;
        public void Set(float value) => Weight = value;
    }
    public class ModuleVrc3
    {
        internal static Camera MainCamera => Camera.allCameras.Length == 0 ? null : Camera.allCameras[0];
        internal readonly AvatarTools AvatarTools = new AvatarTools();
        internal readonly Dictionary<string, VRC_AnimatorTrackingControl.TrackingType> TrackingControls = new Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>
        {
            { "Head", VRC_AnimatorTrackingControl.TrackingType.Tracking },
            { "Left Hand", VRC_AnimatorTrackingControl.TrackingType.Tracking },
            { "Right Hand", VRC_AnimatorTrackingControl.TrackingType.Tracking }
        };
        internal bool LocomotionDisabled;
        internal bool PoseSpace;
        internal bool PoseMode;
        internal readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, LayerData> _layers = new Dictionary<VRCAvatarDescriptor.AnimLayerType, LayerData>();
        public bool Active = true;
        public GameObject Avatar = new GameObject { name = "Avatar" };
        public Animator AvatarAnimator;
        public bool PlayingCustomAnimation;
        public AnimationClip CustomAnim;
        public int RunningUpdates;
        public ModuleVrc3()
        {
            AvatarAnimator = Avatar.Attach(new Animator());
            _layers[VRCAvatarDescriptor.AnimLayerType.FX] = new LayerData
            {
                Playable = new AnimatorControllerPlayable { State = new PlayableState() },
                Weight = new AnimatorControllerWeight()
            };
        }
        public void UpdateRunning() { RunningUpdates++; }
        public void PlayCustomAnimation(AnimationClip clip) { CustomAnim = clip; PlayingCustomAnimation = clip; }
        public void StopCustomAnimation() { PlayingCustomAnimation = false; }
        internal struct LayerData { internal AnimatorControllerPlayable Playable; internal AnimatorControllerWeight Weight; }
    }
}
namespace BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools
{
    using UnityEngine;
    public abstract class GmgDynamicFunction
    {
        protected bool Foldout;
        protected internal abstract bool Active { get; }
        protected internal abstract void Toggle(ModuleVrc3 module);
    }
    public class AvatarTools
    {
        private UpdateSceneCamera _sceneCamera;
        public UpdateSceneCamera SceneCamera => _sceneCamera ??= new UpdateSceneCamera();
        private ClickableContacts _clickableContacts;
        public ClickableContacts ContactsClickable => _clickableContacts ??= new ClickableContacts();
        private AvatarPose _avatarPose;
        public AvatarPose PoseAvatar => _avatarPose ??= new AvatarPose();
        private AnimatorPerformance _animatorPerformance;
        public AnimatorPerformance PerformanceAnimator => _animatorPerformance ??= new AnimatorPerformance();
        public class UpdateSceneCamera : GmgDynamicFunction
        {
            private static Camera _camera;
            private static readonly Pref<bool> IsActive = new Pref<bool>();
            public static int ConstructorCalls;
            public static int PreferenceWrites;
            protected internal override bool Active => IsActive.Property;
            public UpdateSceneCamera() { ConstructorCalls++; Set(IsActive.Property ? ModuleVrc3.MainCamera : null); }
            private static void Set(Camera camera) { _camera = camera; IsActive.Property = camera; PreferenceWrites++; }
            protected void Update(ModuleVrc3 module) { if (_camera && UnityEditor.SceneView.lastActiveSceneView?.camera) _camera.SyncCount++; }
            protected internal override void Toggle(ModuleVrc3 module) => Set(_camera ? null : ModuleVrc3.MainCamera);
        }
        private sealed class Pref<T> { internal T Property { get; set; } }
        public class ClickableContacts : GmgDynamicFunction
        {
            private readonly Pref<string> _tag = new Pref<string>();
            private bool _enabled;
            public int Released;
            protected internal override bool Active => _enabled;
            private void Disable() { Released++; }
            protected internal override void Toggle(ModuleVrc3 module) { _enabled = !_enabled; }
        }
        public class AvatarPose : GmgDynamicFunction
        {
            private bool _mode;
            protected internal override bool Active => _mode;
            protected internal override void Toggle(ModuleVrc3 module) { _mode = module.PoseMode = !module.PoseMode; module.AvatarAnimator.applyRootMotion = _mode; }
        }
        private class AvatarBackground
        {
            private static Renderer _cover;
            private static Texture _texture;
            private static Camera _cameraOb;
            private static float _distance;
            private static bool _realTime;
            private static void SetUp() { if (!_cover) _cover = new GameObject { name = "Background" }.Attach(new Renderer()); }
            private static void ToggleOff() { Object.DestroyImmediate(_cover.gameObject); }
        }
        public class AnimatorPerformance : GmgDynamicFunction
        {
            private Dictionary<string, Benchmark> _benchmark = new Dictionary<string, Benchmark> { { "Update.DirectorUpdate", new Benchmark() } };
            protected internal override bool Active => UnityEngine.Profiling.Profiler.enabled;
            protected internal override void Toggle(ModuleVrc3 module)
            {
                UnityEngine.Profiling.Profiler.enabled = UnityEditorInternal.ProfilerDriver.enabled = !Active;
                if (Active) _benchmark = new Dictionary<string, Benchmark>();
            }
            private class Benchmark
            {
                public int Frame { get; private set; }
                private float _last;
                private float _average;
                private float _maximum;
            }
        }
    }
}
namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    using BlackStartX.GestureManager;
    using BlackStartX.GestureManager.Editor.Modules.Vrc3;
    public static partial class GestureManagerTools
    {
        private static bool TryGetVrc3(out GestureManager gm, out ModuleVrc3 module, out string error)
        {
            gm = TestRig.Manager;
            module = TestRig.Module;
            error = gm == null ? "Error: No GestureManager instance in scene." : module == null || !module.Active ? "Error: GestureManager preview is not active." : null;
            return error == null;
        }
        private static UnityEngine.GameObject FindGO(string path) => TestRig.Objects.TryGetValue(path, out var value) ? value : null;
    }
    public static class TestRig
    {
        public static GestureManager Manager;
        public static ModuleVrc3 Module;
        public static Dictionary<string, UnityEngine.GameObject> Objects = new Dictionary<string, UnityEngine.GameObject>();
        public static ModuleVrc3 Reset()
        {
#if GESTURE_MANAGER
            typeof(GestureManagerTools).GetMethod("GmUtilityCleanup", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
#endif
            UnityEditor.EditorApplication.Tick();
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEditorInternal.ProfilerDriver.enabled = false;
            Manager = new GestureManager();
            Module = new ModuleVrc3();
            Objects.Clear();
            UnityEditor.AssetDatabase.Assets.Clear();
            var camera = new UnityEngine.GameObject { name = "VRCCam" }.Attach(new UnityEngine.Camera { name = "VRCCam" });
            Objects["VRCCam"] = camera.gameObject;
            UnityEngine.Camera.allCameras = new[] { camera };
            UnityEditor.SceneView.lastActiveSceneView = new UnityEditor.SceneView { camera = camera };
            return Module;
        }
    }
}
