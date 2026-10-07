using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

#if GESTURE_MANAGER
using BlackStartX.GestureManager;
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.Tools;
using UnityEditorInternal;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Profiling;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        private const string GmUtilityNotInstalled = "Error: Gesture Manager package not installed (vrchat.blackstartx.gesture-manager).";

        [AgentTool("Get Gesture Manager Scene Camera synchronization status and target camera. Requires an active VRC3 preview.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetSceneCamera()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            // The public SceneCamera property constructs a utility whose constructor calls Set,
            // replacing the global camera and writing EditorPrefs. A Safe getter reads static
            // state directly so inspecting an untouched preview cannot change either setting.
            return GmUtilityRead((gm, module) => SceneCameraState());
#endif
        }

        [AgentTool("Configure Gesture Manager Scene Camera, which continuously matches Game View to the last active Scene View. cameraName may be a scene hierarchy path; blank uses the existing target or GM's main camera. enabled=false disconnects synchronization. syncNow copies the Scene View immediately. Changes to the camera are Undo-recorded.")]
        public static string GestureManagerSetSceneCamera(bool enabled, string cameraName = "", bool syncNow = true)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                var tool = GmAvatarTools(module).SceneCamera;
                var field = GmUtilityField(tool.GetType(), "_camera");
                var setter = GmUtilityMethod(tool.GetType(), "Set", typeof(Camera));
                var updater = GmUtilityMethod(tool.GetType(), "Update", typeof(ModuleVrc3));
                Camera camera = null;
                if (enabled)
                {
                    camera = string.IsNullOrWhiteSpace(cameraName)
                        ? field.GetValue(null) as Camera ?? GmUtilityProperty(typeof(ModuleVrc3), "MainCamera").GetValue(null) as Camera
                        : GmResolveCamera(cameraName);
                    if (!camera) return "Error: No target camera found. Supply cameraName or create/enable the main camera.";
                    if (syncNow && (!SceneView.lastActiveSceneView || !SceneView.lastActiveSceneView.camera))
                        return "Error: No active Scene View camera. Open a Scene View and retry, or pass syncNow=false.";
                    Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform }, "Gesture Manager Scene Camera");
                }
                setter.Invoke(null, new object[] { camera });
                if (enabled && syncNow) updater.Invoke(tool, new object[] { module });
                module.UpdateRunning();
                return "Success: " + SceneCameraState();
            });
#endif
        }

        [AgentTool("Get Gesture Manager Clickable Contacts enabled state and collision-tag filter. A blank tag accepts all tags.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetClickableContacts()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) => ClickableContactsState(GmAvatarTools(module).ContactsClickable));
#endif
        }

        [AgentTool("Configure Gesture Manager Clickable Contacts mouse interaction with Contact Receivers. A blank tag accepts all tags. Disabling releases any contacts currently held by the mouse. These preferences are shared with the GM Inspector.")]
        public static string GestureManagerSetClickableContacts(bool enabled, string tag = "")
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                var tool = GmAvatarTools(module).ContactsClickable;
                var tagProperty = GmUtilityField(tool.GetType(), "_tag").GetValue(tool);
                var tagSetter = GmUtilityProperty(tagProperty.GetType(), "Property");
                var toggle = GmUtilityMethod(tool.GetType(), "Toggle", typeof(ModuleVrc3));
                var release = GmUtilityMethod(tool.GetType(), "Disable");
                if (!enabled) release.Invoke(tool, null);
                tagSetter.SetValue(tagProperty, tag ?? "");
                if (GmUtilityActive(tool) != enabled) toggle.Invoke(tool, new object[] { module });
                module.UpdateRunning();
                return "Success: " + ClickableContactsState(tool);
            });
#endif
        }

        [AgentTool("Get Gesture Manager Pose Avatar mode and custom-animation status. Pose mode masks humanoid animations so bone transforms can be edited.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetPoseAvatar()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) => $"PoseAvatar: enabled={GmUtilityField(typeof(ModuleVrc3), "PoseMode").GetValue(module)} applyRootMotion={module.AvatarAnimator.applyRootMotion} customAnimationPlaying={module.PlayingCustomAnimation}");
#endif
        }

        [AgentTool("Start or stop Gesture Manager Pose Avatar mode using the same operation as its Start/Stop Posing button. Requires a humanoid Animator. When enabled, edit bone transforms with the normal transform tools.")]
        public static string GestureManagerSetPoseAvatar(bool enabled)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                if (!module.AvatarAnimator || !module.AvatarAnimator.isHuman) return "Error: Pose Avatar requires a humanoid Animator.";
                var field = GmUtilityField(typeof(ModuleVrc3), "PoseMode");
                var tool = GmAvatarTools(module).PoseAvatar;
                var toggle = GmUtilityMethod(tool.GetType(), "Toggle", typeof(ModuleVrc3));
                if ((bool)field.GetValue(module) != enabled)
                {
                    Undo.RecordObject(module.AvatarAnimator, "Gesture Manager Pose Avatar");
                    toggle.Invoke(tool, new object[] { module });
                }
                module.UpdateRunning();
                return $"Success: PoseAvatar enabled={field.GetValue(module)}";
            });
#endif
        }

        [AgentTool("Get Gesture Manager Avatar Background enabled state, camera, texture, distance, and constant-update setting.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetAvatarBackground()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) => BackgroundState(GmBackgroundType()));
#endif
        }

        [AgentTool("Configure Gesture Manager Avatar Background. cameraName is a scene hierarchy path; blank retains the camera or auto-selects VRCCam. textureAssetPath is a project Texture asset path; blank retains it and 'none' clears it. distance follows the Inspector range 0.1..20 meters beyond the camera near plane. constant=true keeps the background aligned with the camera. enabled=false removes the temporary background. A background created by this tool is cleaned up when its preview ends.")]
        public static string GestureManagerSetAvatarBackground(bool enabled, string cameraName = "", string textureAssetPath = "", float distance = 0.1f, bool constant = false)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            if (!GmUtilityFinite(distance) || distance < 0.1f || distance > 20f)
                return "Error: distance must be finite and in the range 0.1..20.";
            return GmUtilityRead((gm, module) =>
            {
                if (enabled && _gmUtilityBackgroundOwner != null && _gmUtilityBackgroundOwner != module)
                {
                    if (_gmUtilityBackgroundOwner.Active)
                        return "Error: Another preview owns the background created through this tool. Disable its background first.";
                    GmUtilityRemoveOwnedBackground();
                }
                var type = GmBackgroundType();
                var coverField = GmUtilityField(type, "_cover");
                var cameraField = GmUtilityField(type, "_cameraOb");
                var textureField = GmUtilityField(type, "_texture");
                var distanceField = GmUtilityField(type, "_distance");
                var realtimeField = GmUtilityField(type, "_realTime");
                var setup = GmUtilityMethod(type, "SetUp");
                var off = GmUtilityMethod(type, "ToggleOff");
                var camera = cameraField.GetValue(null) as Camera;
                if (!string.IsNullOrWhiteSpace(cameraName)) camera = GmResolveCamera(cameraName);
                if (enabled && !camera)
                    camera = Camera.allCameras.FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy && c.name == "VRCCam");
                if (enabled && !camera) return "Error: No background camera found. Supply cameraName or enable a camera named VRCCam.";
                var texture = textureField.GetValue(null) as Texture;
                if (!string.IsNullOrWhiteSpace(textureAssetPath))
                {
                    texture = string.Equals(textureAssetPath, "none", StringComparison.OrdinalIgnoreCase) ? null : AssetDatabase.LoadAssetAtPath<Texture>(textureAssetPath);
                    if (!texture && !string.Equals(textureAssetPath, "none", StringComparison.OrdinalIgnoreCase))
                        return $"Error: Texture asset '{textureAssetPath}' not found.";
                }
                var priorCover = coverField.GetValue(null) as Renderer;
                cameraField.SetValue(null, camera);
                textureField.SetValue(null, texture);
                distanceField.SetValue(null, distance);
                realtimeField.SetValue(null, enabled && constant);
                if (enabled)
                {
                    setup.Invoke(null, null);
                    var cover = coverField.GetValue(null) as Renderer;
                    if (!cover) return "Error: Gesture Manager could not create the background.";
                    if (!priorCover)
                    {
                        _gmUtilityBackgroundOwner = module;
                        _gmUtilityOwnedBackground = cover;
                        GmUtilityWatchLifecycle();
                    }
                }
                else if (priorCover)
                {
                    var material = priorCover.sharedMaterial;
                    off.Invoke(null, null);
                    if (material && !EditorUtility.IsPersistent(material)) UnityEngine.Object.DestroyImmediate(material);
                    _gmUtilityBackgroundOwner = null;
                    _gmUtilityOwnedBackground = null;
                }
                module.UpdateRunning();
                return "Success: " + BackgroundState(type);
            });
#endif
        }

        [AgentTool("Get the selected Gesture Manager test AnimationClip, its asset path, and whether it is playing.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetTestAnimation()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) => $"TestAnimation: playing={module.PlayingCustomAnimation} clip='{(module.CustomAnim ? module.CustomAnim.name : "(none)")}' assetPath='{AssetDatabase.GetAssetPath(module.CustomAnim)}'");
#endif
        }

        [AgentTool("Play an AnimationClip through Gesture Manager's Test Animation feature. animationAssetPath is an exact project AnimationClip asset path; blank reuses the selected clip. GM handles its dummy/avatar lifecycle and restores its transform when stopping.")]
        public static string GestureManagerPlayTestAnimation(string animationAssetPath = "")
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                var clip = string.IsNullOrWhiteSpace(animationAssetPath) ? module.CustomAnim : AssetDatabase.LoadAssetAtPath<AnimationClip>(animationAssetPath);
                if (!clip) return $"Error: AnimationClip '{animationAssetPath}' not found or no clip is selected.";
                module.PlayCustomAnimation(clip);
                module.UpdateRunning();
                return $"Success: TestAnimation playing={module.PlayingCustomAnimation} clip='{clip.name}'";
            });
#endif
        }

        [AgentTool("Stop Gesture Manager Test Animation using its StopCustomAnimation lifecycle, keeping the selected clip available for later playback.")]
        public static string GestureManagerStopTestAnimation()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                if (module.PlayingCustomAnimation) module.StopCustomAnimation();
                module.UpdateRunning();
                return $"Success: TestAnimation playing={module.PlayingCustomAnimation}";
            });
#endif
        }

        [AgentTool("Read Gesture Manager Animator Performance benchmark: DirectorUpdate, DirectorUpdateAnimationBegin, and DirectorUpdateAnimationEnd last/average/maximum milliseconds and sample counts. Values come from the same benchmark used by its Inspector. Start with GestureManagerSetAnimatorPerformance(enabled=true), allow editor frames to pass, then read.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetAnimatorPerformance()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) => AnimatorPerformanceState(GmAvatarTools(module).PerformanceAnimator));
#endif
        }

        [AgentTool("Start/stop Gesture Manager Animator Performance recording. Enabling resets GM benchmark samples and enables the global Unity Profiler. Disabling or ending the preview restores the Profiler and ProfilerDriver enabled flags from before this tool started it. Performance includes other animators in the editor, as does GM's Inspector benchmark.")]
        public static string GestureManagerSetAnimatorPerformance(bool enabled)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                var tool = GmAvatarTools(module).PerformanceAnimator;
                var benchmark = GmUtilityField(tool.GetType(), "_benchmark");
                var foldout = GmUtilityField(typeof(GmgDynamicFunction), "Foldout");
                // Check benchmark fields before enabling a process-wide editor feature.
                AnimatorPerformanceState(tool);
                // Validate the official Toggle API before changing global profiler state.
                var toggle = GmUtilityMethod(tool.GetType(), "Toggle", typeof(ModuleVrc3));
                if (enabled)
                {
                    if (_gmUtilityProfilerOwner != null && _gmUtilityProfilerOwner != module)
                        return "Error: Another preview started the profiler through this tool. Stop its benchmark first.";
                    if (_gmUtilityProfilerOwner == null)
                    {
                        _gmUtilityProfilerBefore = Profiler.enabled;
                        _gmUtilityProfilerDriverBefore = ProfilerDriver.enabled;
                        _gmUtilityProfilerOwner = module;
                        GmUtilityWatchLifecycle();
                    }
                    if (!Profiler.enabled) toggle.Invoke(tool, new object[] { module });
                    Profiler.enabled = ProfilerDriver.enabled = true;
                    benchmark.SetValue(tool, Activator.CreateInstance(benchmark.FieldType));
                    foldout.SetValue(tool, true);
                }
                else if (_gmUtilityProfilerOwner != null)
                    GmUtilityRestoreProfiler();
                else return "No benchmark was started through this tool. Current profiler settings were retained.";
                module.UpdateRunning();
                return "Success: " + AnimatorPerformanceState(tool);
            });
#endif
        }

        [AgentTool("Get Gesture Manager Tracking Control body-part states, playable-controller weights, locomotion enabled state, and temporary pose space. These are runtime simulation values; animation behaviours may change them on later frames.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetTrackingControl()
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                var controls = GmTrackingControls(module);
                var sb = new StringBuilder("TrackingControl:\n");
                foreach (var pair in controls) sb.AppendLine($"  {pair.Key}: {pair.Value}");
                sb.AppendLine($"Locomotion: enabled={!(bool)GmUtilityField(typeof(ModuleVrc3), "LocomotionDisabled").GetValue(module)}");
                sb.AppendLine($"PoseSpace: {((bool)GmUtilityField(typeof(ModuleVrc3), "PoseSpace").GetValue(module) ? "Pose" : "Default")}");
                sb.AppendLine("AnimationControllers:");
                foreach (DictionaryEntry entry in GmUtilityLayers(module))
                {
                    var weight = GmUtilityField(entry.Value.GetType(), "Weight").GetValue(entry.Value) as AnimatorControllerWeight;
                    var playable = (AnimatorControllerPlayable)GmUtilityField(entry.Value.GetType(), "Playable").GetValue(entry.Value);
                    sb.AppendLine($"  {entry.Key}: weight={(weight != null && playable.IsValid() ? GmUtilityFloat(weight.Weight) : "(invalid playable)")}");
                }
                return sb.ToString().TrimEnd();
            });
#endif
        }

        [AgentTool("Set a Gesture Manager Tracking Control body part to Tracking or Animation. bodyPart accepts Head, Left Hand, Right Hand, Hip, Left Foot, Right Foot, Left Fingers, Right Fingers, Eye & Eyelid, Mouth & Jaw, or all. NoChange preserves the current state, matching VRChat tracking-control behaviour. Animation behaviours may overwrite this runtime setting.")]
        public static string GestureManagerSetTrackingControl(string bodyPart, string trackingType)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            VRC_AnimatorTrackingControl.TrackingType tracking;
            if (!Enum.TryParse(trackingType, true, out tracking) || !Enum.IsDefined(typeof(VRC_AnimatorTrackingControl.TrackingType), tracking))
                return "Error: trackingType must be Tracking, Animation, or NoChange.";
            return GmUtilityRead((gm, module) =>
            {
                var controls = GmTrackingControls(module);
                bool all = string.Equals(bodyPart, "all", StringComparison.OrdinalIgnoreCase);
                var key = controls.Keys.FirstOrDefault(k => string.Equals(k, bodyPart, StringComparison.OrdinalIgnoreCase));
                if (!all && key == null) return $"Error: Unknown bodyPart '{bodyPart}'. Valid values: {string.Join(", ", controls.Keys)}, all.";
                if (tracking != VRC_AnimatorTrackingControl.TrackingType.NoChange)
                {
                    if (all) foreach (var part in controls.Keys.ToArray()) controls[part] = tracking;
                    else controls[key] = tracking;
                }
                return $"Success: TrackingControl {bodyPart}={tracking}";
            });
#endif
        }

        [AgentTool("Set Gesture Manager runtime locomotion enabled/disabled state, corresponding to VRC Animator Locomotion Control. Later animation behaviours may overwrite it.")]
        public static string GestureManagerSetLocomotion(bool enabled)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                GmUtilityField(typeof(ModuleVrc3), "LocomotionDisabled").SetValue(module, !enabled);
                return $"Success: Locomotion enabled={enabled}";
            });
#endif
        }

        [AgentTool("Set Gesture Manager runtime temporary pose space: enabled=true selects Pose, false selects Default. This is the Tracking Control debug value, separate from Pose Avatar bone-editing mode. Later animation behaviours may overwrite it.")]
        public static string GestureManagerSetPoseSpace(bool enabled)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            return GmUtilityRead((gm, module) =>
            {
                GmUtilityField(typeof(ModuleVrc3), "PoseSpace").SetValue(module, enabled);
                return $"Success: PoseSpace={(enabled ? "Pose" : "Default")}";
            });
#endif
        }

        [AgentTool("Set a Gesture Manager playable controller weight (0..1). controller is Base, Additive, Sitting, TPose, IKPose, Gesture, Action, or FX. animatorLayer=-1 sets the whole controller; a nonnegative index sets that AnimatorControllerPlayable layer. Pending GM weight blends are cancelled for the affected target so they do not overwrite the change immediately. Later animation behaviours can still change weights.")]
        public static string GestureManagerSetControllerWeight(string controller, float weight, int animatorLayer = -1)
        {
#if !GESTURE_MANAGER
            return GmUtilityNotInstalled;
#else
            if (!GmUtilityFinite(weight) || weight < 0f || weight > 1f) return "Error: weight must be finite and in the range 0..1.";
            if (animatorLayer < -1) return "Error: animatorLayer must be -1 or a nonnegative layer index.";
            VRCAvatarDescriptor.AnimLayerType layer;
            if (!Enum.TryParse(controller, true, out layer) || !Enum.IsDefined(typeof(VRCAvatarDescriptor.AnimLayerType), layer))
                return $"Error: Unknown controller '{controller}'.";
            return GmUtilityRead((gm, module) =>
            {
                var layers = GmUtilityLayers(module);
                if (!layers.Contains(layer)) return $"Error: Controller '{controller}' is not present in the preview.";
                var data = layers[layer];
                var playable = (AnimatorControllerPlayable)GmUtilityField(data.GetType(), "Playable").GetValue(data);
                var weightController = GmUtilityField(data.GetType(), "Weight").GetValue(data) as AnimatorControllerWeight;
                if (!playable.IsValid() || weightController == null) return "Error: Controller playable is invalid. Restart the preview.";
                if (animatorLayer == -1)
                {
                    GmUtilityField(typeof(AnimatorControllerWeight), "_control").SetValue(weightController, null);
                    weightController.Set(weight);
                }
                else
                {
                    if (animatorLayer >= playable.GetLayerCount()) return $"Error: animatorLayer must be smaller than {playable.GetLayerCount()}.";
                    var pending = GmUtilityField(typeof(AnimatorControllerWeight), "_subControls").GetValue(weightController) as IDictionary;
                    if (pending == null) throw new MissingMemberException("AnimatorControllerWeight._subControls");
                    pending.Remove(animatorLayer);
                    playable.SetLayerWeight(animatorLayer, weight);
                }
                return $"Success: Controller {layer} animatorLayer={animatorLayer} weight={GmUtilityFloat(weight)}";
            });
#endif
        }

#if GESTURE_MANAGER
        // GM 3.9.9 exposes the utility types but keeps their GUI operations and state internal.
        // Keep this narrowly scoped bridge on the exact official methods/fields; report incompatible
        // package APIs explicitly instead of silently applying a guessed substitute operation.
        private const BindingFlags GmUtilityBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static ModuleVrc3 _gmUtilityProfilerOwner;
        private static bool _gmUtilityProfilerBefore;
        private static bool _gmUtilityProfilerDriverBefore;
        private static ModuleVrc3 _gmUtilityBackgroundOwner;
        private static Renderer _gmUtilityOwnedBackground;
        private static bool _gmUtilityWatching;

        private static string GmUtilityRead(Func<GestureManager, ModuleVrc3, string> operation)
        {
            GestureManager gm;
            ModuleVrc3 module;
            string error;
            if (!TryGetVrc3(out gm, out module, out error)) return error;
            try { return operation(gm, module); }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (cause is MissingMemberException || cause is InvalidCastException || cause is TypeLoadException)
                    return $"Error: Unsupported Gesture Manager utility API (version {GestureManager.Version}): {cause.Message}. Supported API reference: 3.9.9.";
                return $"Error: Gesture Manager utility failed: {cause.GetType().Name}: {cause.Message}";
            }
        }

        private static FieldInfo GmUtilityField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, GmUtilityBindings | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static PropertyInfo GmUtilityProperty(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty(name, GmUtilityBindings | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            throw new MissingMemberException(type.FullName, name);
        }

        private static MethodInfo GmUtilityMethod(Type type, string name, params Type[] arguments)
        {
            var method = type.GetMethod(name, GmUtilityBindings, null, arguments, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static AvatarTools GmAvatarTools(ModuleVrc3 module)
        {
            var tools = GmUtilityField(typeof(ModuleVrc3), "AvatarTools").GetValue(module) as AvatarTools;
            if (tools == null) throw new MissingMemberException("ModuleVrc3.AvatarTools is unavailable.");
            return tools;
        }

        private static bool GmUtilityActive(GmgDynamicFunction tool) => (bool)GmUtilityProperty(tool.GetType(), "Active").GetValue(tool);
        private static bool GmUtilityFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string GmUtilityFloat(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        private static string GmUtilityObject(UnityEngine.Object value) => value ? value.name : "(none)";

        private static Camera GmResolveCamera(string name)
        {
            var go = FindGO(name);
            var camera = go ? go.GetComponent<Camera>() : null;
            if (!camera) throw new ArgumentException($"Scene camera '{name}' not found. Use its GameObject hierarchy path.");
            return camera;
        }

        private static string SceneCameraState()
        {
            var type = typeof(AvatarTools.UpdateSceneCamera);
            var camera = GmUtilityField(type, "_camera").GetValue(null) as Camera;
            var activePreference = GmUtilityField(type, "IsActive").GetValue(null);
            if (activePreference == null) throw new MissingMemberException("UpdateSceneCamera.IsActive");
            bool active = (bool)GmUtilityProperty(activePreference.GetType(), "Property").GetValue(activePreference);
            return $"SceneCamera: enabled={active} camera='{GmUtilityObject(camera)}' sceneViewAvailable={(bool)SceneView.lastActiveSceneView}";
        }

        private static string ClickableContactsState(AvatarTools.ClickableContacts tool)
        {
            var tag = GmUtilityField(tool.GetType(), "_tag").GetValue(tool);
            return $"ClickableContacts: enabled={GmUtilityActive(tool)} tag='{GmUtilityProperty(tag.GetType(), "Property").GetValue(tag)}'";
        }

        private static Type GmBackgroundType()
        {
            var type = typeof(AvatarTools).GetNestedType("AvatarBackground", BindingFlags.NonPublic);
            if (type == null) throw new MissingMemberException("AvatarTools.AvatarBackground");
            return type;
        }

        private static string BackgroundState(Type type)
        {
            var cover = GmUtilityField(type, "_cover").GetValue(null) as Renderer;
            var camera = GmUtilityField(type, "_cameraOb").GetValue(null) as Camera;
            var texture = GmUtilityField(type, "_texture").GetValue(null) as Texture;
            return $"AvatarBackground: enabled={(bool)cover} camera='{GmUtilityObject(camera)}' texture='{GmUtilityObject(texture)}' textureAssetPath='{AssetDatabase.GetAssetPath(texture)}' distance={GmUtilityFloat((float)GmUtilityField(type, "_distance").GetValue(null))} constant={GmUtilityField(type, "_realTime").GetValue(null)}";
        }

        private static string AnimatorPerformanceState(AvatarTools.AnimatorPerformance tool)
        {
            var benchmark = GmUtilityField(tool.GetType(), "_benchmark").GetValue(tool) as IDictionary;
            if (benchmark == null) throw new MissingMemberException("AnimatorPerformance._benchmark");
            var sb = new StringBuilder($"AnimatorPerformance: profilerEnabled={Profiler.enabled} recordingThroughTool={(_gmUtilityProfilerOwner != null && Profiler.enabled)} unit=milliseconds\n");
            foreach (DictionaryEntry entry in benchmark)
            {
                var sample = entry.Value;
                var type = sample.GetType();
                sb.AppendLine($"  {entry.Key}: frames={GmUtilityProperty(type, "Frame").GetValue(sample)} last={GmUtilityFloat((float)GmUtilityField(type, "_last").GetValue(sample))} average={GmUtilityFloat((float)GmUtilityField(type, "_average").GetValue(sample))} maximum={GmUtilityFloat((float)GmUtilityField(type, "_maximum").GetValue(sample))}");
            }
            if (benchmark.Count == 0) sb.AppendLine("  (No samples yet; allow editor frames to pass.)");
            return sb.ToString().TrimEnd();
        }

        private static Dictionary<string, VRC_AnimatorTrackingControl.TrackingType> GmTrackingControls(ModuleVrc3 module)
        {
            var controls = GmUtilityField(typeof(ModuleVrc3), "TrackingControls").GetValue(module) as Dictionary<string, VRC_AnimatorTrackingControl.TrackingType>;
            if (controls == null) throw new MissingMemberException("ModuleVrc3.TrackingControls");
            return controls;
        }

        private static IDictionary GmUtilityLayers(ModuleVrc3 module)
        {
            var layers = GmUtilityField(typeof(ModuleVrc3), "_layers").GetValue(module) as IDictionary;
            if (layers == null) throw new MissingMemberException("ModuleVrc3._layers");
            return layers;
        }

        private static void GmUtilityWatchLifecycle()
        {
            if (_gmUtilityWatching) return;
            _gmUtilityWatching = true;
            EditorApplication.update += GmUtilityLifecycleUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += GmUtilityCleanup;
            EditorApplication.quitting += GmUtilityCleanup;
        }

        private static void GmUtilityLifecycleUpdate()
        {
            if (_gmUtilityProfilerOwner != null && !_gmUtilityProfilerOwner.Active) GmUtilityRestoreProfiler();
            if (_gmUtilityBackgroundOwner != null && !_gmUtilityBackgroundOwner.Active) GmUtilityRemoveOwnedBackground();
            if (_gmUtilityProfilerOwner == null && _gmUtilityBackgroundOwner == null)
            {
                EditorApplication.update -= GmUtilityLifecycleUpdate;
                AssemblyReloadEvents.beforeAssemblyReload -= GmUtilityCleanup;
                EditorApplication.quitting -= GmUtilityCleanup;
                _gmUtilityWatching = false;
            }
        }

        private static void GmUtilityRestoreProfiler()
        {
            if (_gmUtilityProfilerOwner == null) return;
            Profiler.enabled = _gmUtilityProfilerBefore;
            ProfilerDriver.enabled = _gmUtilityProfilerDriverBefore;
            _gmUtilityProfilerOwner = null;
        }

        private static void GmUtilityRemoveOwnedBackground()
        {
            if (_gmUtilityOwnedBackground)
            {
                var material = _gmUtilityOwnedBackground.sharedMaterial;
                UnityEngine.Object.DestroyImmediate(_gmUtilityOwnedBackground.gameObject);
                if (material && !EditorUtility.IsPersistent(material)) UnityEngine.Object.DestroyImmediate(material);
            }
            _gmUtilityOwnedBackground = null;
            _gmUtilityBackgroundOwner = null;
        }

        private static void GmUtilityCleanup()
        {
            GmUtilityRestoreProfiler();
            GmUtilityRemoveOwnedBackground();
        }
#endif
    }
}
