using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MVFrameCaptureTools
    {
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const double TimeoutSeconds = 50;

        [Serializable]
        private sealed class FrameResult
        {
            public string mvAssetPath, camera, cameraName, cameraHierarchy, cameraScene, cameraObjectId;
            public double requestedTimeSeconds, evaluatedTimeSeconds;
            public int width, height;
            public Vector3 position;
            public Quaternion rotation;
            public bool orthographic;
            public float fieldOfView, orthographicSize;
        }

        [Serializable]
        private sealed class RenderManifest
        {
            public string state = "", format = "", source = "", sourceCameraClipId = "";
            public int width = 0, height = 0, frames = 0;
            public double start = 0;
        }

        [AgentTool("Capture one evaluated frame from a compatible MV renderer and return a PNG plus the actual sample time " +
                   "and camera. Use the exact MVProject asset path under Assets/ and load its generated scene first. " +
                   "camera: viewer for the evaluated MV viewer at that time, scene:<exact Camera name or /hierarchy/path>, " +
                   "scene-id:<Camera GlobalObjectId>, " +
                   "or mv:<MV camera clip ID>. The call waits for scene evaluation and video seek before rendering. " +
                   "Use the same MV, time, camera and dimensions to compare edits. Requires Edit Mode and a graphics device. " +
                   "Rendering stops timeline playback and restores the previous preview.",
            Category = "Capture", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static IEnumerator CaptureSequenceFrame(string mvAssetPath, double timeSeconds, string camera,
            int width = 960, int height = 540)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                yield return "Error: MV capture requires Unity Edit Mode with an active graphics device.";
                yield break;
            }
            if (width < 64 || height < 64 || (long)width * height > 1920L * 1080 ||
                width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            {
                yield return "Error: capture dimensions must be at least 64 pixels each, within the GPU texture limit, and at most 2,073,600 pixels total.";
                yield break;
            }

            string path = (mvAssetPath ?? "").Trim().Replace('\\', '/');
            var project = path.StartsWith("Assets/", StringComparison.Ordinal) ? AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject : null;
            Type projectType = project != null ? project.GetType() : null;
            if (projectType == null || !projectType.IsPublic || projectType.Name != "MVProject")
            {
                yield return "Error: mvAssetPath must identify a compatible MVProject asset under Assets/.";
                yield break;
            }
            path = AssetDatabase.GetAssetPath(project);

            // A renderer is identified through the supplied asset's own assembly and namespace.
            string typePrefix = string.IsNullOrEmpty(projectType.Namespace) ? "" : projectType.Namespace + ".";
            Type FindRelatedType(string name) => projectType.Assembly.GetType(typePrefix + name);
            var settingsType = FindRelatedType("MVRenderSettings");
            var jobType = FindRelatedType("MVRenderJob");
            var start = settingsType == null ? null : jobType?.GetMethod("Start", StaticMembers, null,
                new[] { projectType, settingsType, typeof(string) }, null);
            var cancel = jobType?.GetMethod("Cancel", StaticMembers, null, Type.EmptyTypes, null);
            var isRunning = jobType?.GetProperty("IsRunning", StaticMembers);
            var outputDirectory = jobType?.GetProperty("OutputDirectory", StaticMembers);
            var status = jobType?.GetProperty("Status", StaticMembers);
            var renderCamera = jobType?.GetProperty("RenderCamera", StaticMembers);
            var sampleTime = jobType?.GetProperty("SampleTime", StaticMembers);
            var projectDuration = projectType?.GetProperty("Duration");
            var findDirector = FindRelatedType("MVClipOps")?.GetMethod("FindDirector", StaticMembers, null, new[] { projectType }, null);
            var resolveMVCamera = FindRelatedType("MVCameras")?.GetMethod("ResolveCamera", StaticMembers, null, new[] { projectType, typeof(string) }, null);
            if (start?.ReturnType != typeof(void) || cancel?.ReturnType != typeof(void) || isRunning?.PropertyType != typeof(bool) ||
                outputDirectory?.PropertyType != typeof(string) || status?.PropertyType != typeof(string) ||
                renderCamera?.PropertyType != typeof(Camera) || sampleTime?.PropertyType != typeof(double) ||
                projectDuration?.PropertyType != typeof(double) || findDirector?.ReturnType != typeof(PlayableDirector) ||
                resolveMVCamera?.ReturnType != typeof(Camera))
            {
                yield return "Error: this MVProject asset does not expose the supported MV render API.";
                yield break;
            }
            if ((bool)isRunning.GetValue(null))
            {
                yield return "Error: the MV renderer is already running. Wait for that render before capturing a frame.";
                yield break;
            }
            double duration = (double)projectDuration.GetValue(project);
            if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds) || timeSeconds < 0 || timeSeconds >= duration)
            {
                yield return FormattableString.Invariant($"Error: timeSeconds must be in [0, {duration:R}) for this MV.");
                yield break;
            }
            var director = findDirector.Invoke(null, new[] { project }) as PlayableDirector;
            if (director == null || !director.gameObject.scene.isLoaded)
            {
                yield return "Error: open the scene containing this MV's generated PlayableDirector before capturing.";
                yield break;
            }

            string selector = (camera ?? "").Trim();
            Camera sourceCamera = null;
            string source, clipId = "";
            if (selector == "viewer") source = "Viewer";
            else if (selector.StartsWith("mv:", StringComparison.Ordinal) && selector.Length > 3)
            {
                source = "MVCamera";
                clipId = selector.Substring(3);
            }
            else if (selector.StartsWith("scene:", StringComparison.Ordinal) && selector.Length > 6)
            {
                source = "Camera";
                string name = selector.Substring(6);
                var matches = Resources.FindObjectsOfTypeAll<Camera>().Where(candidate => candidate != null &&
                    candidate.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(candidate) &&
                    (name.StartsWith("/", StringComparison.Ordinal)
                        ? "/" + AvatarAnatomyTools.GetHierarchyPathInternal(candidate.gameObject) == name
                        : candidate.name == name)).Take(2).ToArray();
                if (matches.Length != 1)
                {
                    yield return "Error: the scene camera selector must match exactly one loaded Camera. Use its /hierarchy/path or scene-id to disambiguate.";
                    yield break;
                }
                sourceCamera = matches[0];
            }
            else if (selector.StartsWith("scene-id:", StringComparison.Ordinal))
            {
                source = "Camera";
                if (GlobalObjectId.TryParse(selector.Substring(9), out var id))
                    sourceCamera = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Camera;
                if (sourceCamera == null || !sourceCamera.gameObject.scene.isLoaded || EditorUtility.IsPersistent(sourceCamera))
                {
                    yield return "Error: scene-id must identify a loaded scene Camera component.";
                    yield break;
                }
            }
            else
            {
                yield return "Error: camera must be viewer, scene:<name or /hierarchy/path>, scene-id:<Camera GlobalObjectId>, or mv:<clip ID>.";
                yield break;
            }

            var settings = Activator.CreateInstance(settingsType);
            settingsType.GetField("Format").SetValue(settings, Enum.Parse(settingsType.GetField("Format").FieldType, "PngSequence"));
            settingsType.GetField("Source").SetValue(settings, Enum.Parse(settingsType.GetField("Source").FieldType, source));
            settingsType.GetField("Width").SetValue(settings, width);
            settingsType.GetField("Height").SetValue(settings, height);
            settingsType.GetField("Fps").SetValue(settings, 60);
            settingsType.GetField("Start").SetValue(settings, timeSeconds);
            settingsType.GetField("End").SetValue(settings, Math.Min(duration, timeSeconds + 1.0 / 60));
            settingsType.GetField("IncludeAudio").SetValue(settings, false);
            settingsType.GetField("SourceCamera").SetValue(settings, sourceCamera);
            settingsType.GetField("SourceCameraClipId").SetValue(settings, clipId);

            string tempPath = Path.Combine(Path.GetTempPath(), "UnityAgent-MVFrame-" + Guid.NewGuid().ToString("N"));
            string jobPath = null;
            byte[] bytes;
            FrameResult result = null;
            bool IsOwnRender() => jobPath != null && (string)outputDirectory.GetValue(null) == jobPath;
            Camera.CameraCallback noteCamera = rendered =>
            {
                if (!IsOwnRender() || rendered != renderCamera.GetValue(null) as Camera) return;
                var cameraUsed = source == "MVCamera"
                    ? resolveMVCamera.Invoke(null, new object[] { project, clipId }) as Camera
                    : sourceCamera;
                if (cameraUsed == null && source != "Viewer") return;
                result = new FrameResult
                {
                    mvAssetPath = path,
                    camera = selector,
                    cameraName = cameraUsed != null ? cameraUsed.name : "MV Viewer",
                    cameraHierarchy = cameraUsed != null ? "/" + AvatarAnatomyTools.GetHierarchyPathInternal(cameraUsed.gameObject) : "",
                    cameraScene = cameraUsed != null ? cameraUsed.gameObject.scene.path : director.gameObject.scene.path,
                    cameraObjectId = source == "Camera" ? GlobalObjectId.GetGlobalObjectIdSlow(cameraUsed).ToString() : clipId,
                    requestedTimeSeconds = timeSeconds,
                    evaluatedTimeSeconds = (double)sampleTime.GetValue(null),
                    position = rendered.transform.position,
                    rotation = rendered.transform.rotation,
                    orthographic = rendered.orthographic,
                    fieldOfView = rendered.fieldOfView,
                    orthographicSize = rendered.orthographicSize,
                    width = width,
                    height = height,
                };
            };

            Camera.onPreCull += noteCamera;
            try
            {
                string error = null;
                try { start.Invoke(null, new[] { project, settings, tempPath }); }
                catch (TargetInvocationException ex) { error = ex.GetBaseException().Message; }
                if (error != null)
                {
                    yield return "Error: the MV renderer could not start the capture. " + error;
                    yield break;
                }

                jobPath = (string)outputDirectory.GetValue(null);
                if (string.IsNullOrEmpty(jobPath))
                {
                    yield return "Error: the MV renderer started without recording an output directory.";
                    yield break;
                }
                double deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                while (IsOwnRender() && (bool)isRunning.GetValue(null) && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                if (IsOwnRender() && (bool)isRunning.GetValue(null))
                {
                    yield return "Error: the MV renderer did not finish evaluating and rendering the frame within 50 seconds. " + status.GetValue(null);
                    yield break;
                }

                string manifestPath = Path.Combine(jobPath, "render.json");
                var manifest = File.Exists(manifestPath) ? JsonUtility.FromJson<RenderManifest>(File.ReadAllText(manifestPath)) : null;
                if (manifest?.state != "complete" || manifest.frames != 1 || manifest.format != "PngSequence" ||
                    manifest.source != source || manifest.sourceCameraClipId != clipId || manifest.width != width || manifest.height != height ||
                    result == null || !(Math.Abs(manifest.start - result.evaluatedTimeSeconds) <= 1e-8))
                {
                    yield return "Error: no complete MV frame with matching evaluation and camera was recorded. " + status.GetValue(null);
                    yield break;
                }
                bytes = File.ReadAllBytes(Path.Combine(jobPath, "frame_000000.png"));
            }
            finally
            {
                Camera.onPreCull -= noteCamera;
                try { if (IsOwnRender() && (bool)isRunning.GetValue(null)) cancel.Invoke(null, null); }
                finally
                {
                    if (Directory.Exists(tempPath))
                    {
                        try { Directory.Delete(tempPath, recursive: true); }
                        catch (IOException ex) { Debug.LogWarning("[UnityAgent] Could not delete temporary MV render files: " + ex.Message); }
                        catch (UnauthorizedAccessException ex) { Debug.LogWarning("[UnityAgent] Could not delete temporary MV render files: " + ex.Message); }
                    }
                }
            }
            yield return JsonUtility.ToJson(result, true);
            // The invoker consumes the shared image slot as soon as this iterator completes.
            SceneViewTools.SetPendingImage(bytes, "image/png", dumpDebugCopy: false);
        }
    }
}
