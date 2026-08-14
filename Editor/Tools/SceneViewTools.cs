using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class SceneViewTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        public static byte[] PendingImageBytes { get; private set; }
        public static string PendingImageMimeType { get; private set; }

        // Compute a tight world-space AABB for any Renderer.
        // For SkinnedMeshRenderer, prefer sharedMesh.bounds (mesh-local) transformed
        // by transform.localToWorldMatrix — avoids the runtime-inflated bounds that
        // SMR uses for skinning safety. For other renderers, use Renderer.bounds.
        private static Bounds ComputeTightBounds(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var localBounds = smr.sharedMesh.bounds;
                var corners = new Vector3[8];
                Vector3 min = localBounds.min, max = localBounds.max;
                corners[0] = new Vector3(min.x, min.y, min.z);
                corners[1] = new Vector3(max.x, min.y, min.z);
                corners[2] = new Vector3(min.x, max.y, min.z);
                corners[3] = new Vector3(max.x, max.y, min.z);
                corners[4] = new Vector3(min.x, min.y, max.z);
                corners[5] = new Vector3(max.x, min.y, max.z);
                corners[6] = new Vector3(min.x, max.y, max.z);
                corners[7] = new Vector3(max.x, max.y, max.z);

                var matrix = smr.transform.localToWorldMatrix;
                Vector3 worldMin = matrix.MultiplyPoint(corners[0]);
                Vector3 worldMax = worldMin;
                for (int i = 1; i < 8; i++)
                {
                    Vector3 wp = matrix.MultiplyPoint(corners[i]);
                    worldMin = Vector3.Min(worldMin, wp);
                    worldMax = Vector3.Max(worldMax, wp);
                }
                var b = new Bounds();
                b.SetMinMax(worldMin, worldMax);
                return b;
            }
            return r.bounds;
        }

        public static void ClearPendingImage()
        {
            PendingImageBytes = null;
            PendingImageMimeType = null;
        }

        /// <summary>
        /// Path of the most recently dumped capture image, under
        /// %TEMP%/unity-agent-captures/capture-YYYYMMDD-HHMMSS-NNN.png|jpg.
        /// Useful for AI clients that don't render MCP image attachments inline:
        /// the AI can Read this file path to see the actual image.
        /// Each dump gets a fresh file name, so a path handed out earlier keeps showing the image it
        /// was handed out for (a fixed name used to make before/after pairs read back identically).
        /// Only the newest 20 dumps are kept (CaptureDumpRetainCount) — older files are deleted,
        /// so a path stays readable for about the next 20 captures, not forever.
        /// null only if nothing has been dumped yet in this session or the write failed;
        /// a SetPendingImage call with dumpDebugCopy:false leaves this pointing at the last real dump.
        /// </summary>
        public static string LastCaptureDebugPath { get; private set; }

        // Debug-dump directory. Sequential names (timestamp + per-session counter) instead of one
        // fixed file: two captures in a row, or two agents capturing at once, used to overwrite each
        // other, so reading back the "before" path returned the "after" image and a regression could
        // be invented or hidden. Bounded to the newest CaptureDumpRetainCount files.
        private const string CaptureDumpDirName = "unity-agent-captures";
        private const int CaptureDumpRetainCount = 20;
        private static int _captureDumpSeq;

        public static void SetPendingImage(byte[] bytes, string mimeType)
            => SetPendingImage(bytes, mimeType, true);

        /// <summary>
        /// Sets the image that will be attached to the current tool result.
        /// dumpDebugCopy = true (what the two-argument overload does, i.e. the historical behaviour)
        /// also writes a numbered copy under %TEMP%/unity-agent-captures/ and points
        /// LastCaptureDebugPath at it.
        /// Pass false for images that are NOT captures — a user-supplied chat attachment, a generated
        /// texture — so they are neither duplicated into the capture directory nor allowed to push real
        /// captures out of the 20-file retention window that before/after comparison depends on.
        ///
        /// Where the line falls today:
        ///   false — UnityAgentWindow (web-dashboard attachment, chat attachment) and
        ///           TextureGenerationTools (AI-generated texture, already written to the project as an
        ///           asset). These are inputs and assets, not observations of the editor; a chat session
        ///           with a few screenshots pasted in used to evict every capture in the window.
        ///   true  — everything routed through CaptureCommon.Attach, plus ImageDiffTools' diff and mask
        ///           images. A diff IS a capture-class artefact: it is the observation an agent compares
        ///           against later captures, so it has to stay re-readable from the dump directory.
        /// </summary>
        public static void SetPendingImage(byte[] bytes, string mimeType, bool dumpDebugCopy)
        {
            PendingImageBytes = bytes;
            PendingImageMimeType = mimeType;

            if (!dumpDebugCopy) return;
            if (bytes == null || bytes.Length == 0) return;   // nothing to dump; keep the previous path

            string dir = null;
            try
            {
                string ext = (mimeType == "image/jpeg" || mimeType == "image/jpg") ? ".jpg" : ".png";
                dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CaptureDumpDirName);
                System.IO.Directory.CreateDirectory(dir);   // no-op when it already exists

                // The counter restarts on every domain reload, and a second Unity instance has its own,
                // so the name is additionally probed for collisions instead of being trusted blindly.
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string path = null;
                for (int attempt = 0; attempt < 1000 && path == null; attempt++)
                {
                    int seq = System.Threading.Interlocked.Increment(ref _captureDumpSeq) & 0x7fffffff;
                    string candidate = System.IO.Path.Combine(dir, $"capture-{stamp}-{seq % 1000:D3}{ext}");
                    if (!System.IO.File.Exists(candidate)) path = candidate;
                }
                // 1000 taken names inside one second: fall back to a name that cannot collide rather
                // than overwriting somebody else's dump.
                if (path == null)
                    path = System.IO.Path.Combine(dir, $"capture-{stamp}-{Guid.NewGuid():N}{ext}");

                System.IO.File.WriteAllBytes(path, bytes);
                LastCaptureDebugPath = path;
            }
            catch
            {
                LastCaptureDebugPath = null;
            }

            // Pruning is deliberately outside the try above: failing to delete an old dump must not
            // throw away the path of the dump that was just written successfully.
            if (dir != null) PruneCaptureDumps(dir);
        }

        // Keeps only the newest CaptureDumpRetainCount dumps so the directory cannot grow without
        // bound. Best-effort: a file locked by an image viewer or another Unity instance is left alone.
        private static void PruneCaptureDumps(string dir)
        {
            try
            {
                var files = System.IO.Directory.GetFiles(dir, "capture-*");
                if (files.Length <= CaptureDumpRetainCount) return;

                // Ordered by write time rather than by name: the per-session counter wraps, so the
                // name is not a reliable age ordering across reloads or across Unity instances.
                var times = new DateTime[files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    try { times[i] = System.IO.File.GetLastWriteTimeUtc(files[i]); }
                    catch { times[i] = DateTime.MinValue; }   // unreadable → treat as oldest
                }
                Array.Sort(times, files);

                for (int i = 0; i < files.Length - CaptureDumpRetainCount; i++)
                {
                    try { System.IO.File.Delete(files[i]); }
                    catch
                    {
                        // Locked or already gone. Retention is a housekeeping nicety, never a reason to
                        // fail a capture, and reporting it would spam the console on every screenshot.
                    }
                }
            }
            catch
            {
                // Directory listing failed (permissions, deleted underneath us). The dump itself
                // already succeeded, so this is not surfaced to the caller.
            }
        }

        // ─── Quality / format options shared by all capture-style tools ───
        // Encodes a Texture2D with optional downscale + format choice. Returns null on failure.
        // Caller is responsible for DestroyImmediate-ing the source Texture2D.
        // Thin wrapper for callers that do not report resolution; prefer the overload with
        // outWidth/outHeight so the reported size matches the bytes that were attached.
        internal static byte[] EncodeWithOptions(Texture2D tex, int maxWidth, string format, int jpgQuality, out string mime)
            => EncodeWithOptions(tex, maxWidth, format, jpgQuality, out mime, out _, out _);

        // Same as above, but reports the resolution of the image that was ACTUALLY encoded.
        // outWidth/outHeight differ from the requested width/height whenever maxWidth kicked in,
        // and callers must print these rather than what they asked for: reporting '2048x2048'
        // while attaching a 512x512 PNG makes every pixel coordinate derived from that image
        // (DiffImages maskRegion, cropRegion, "the seam is at x=1400") point at the wrong place.
        //
        // The downscale + encode itself lives in CaptureCommon.Encode, which is also what
        // CaptureCommon.Finish runs through. Every tool in THIS file now finishes through
        // CaptureCommon.Finish (so that route=, source resolution and the debug-dump path are worded
        // identically everywhere); this pair of overloads remains for FaceCameraCapture, which builds its
        // own message and only wants the bytes.
        internal static byte[] EncodeWithOptions(Texture2D tex, int maxWidth, string format, int jpgQuality,
                                                 out string mime, out int outWidth, out int outHeight)
        {
            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath: "");
            byte[] bytes = CaptureCommon.Encode(tex, opt, out mime, out outWidth, out outHeight, out string error);

            // This signature has nowhere to put a reason, and every caller only checks for null, so the
            // reason would otherwise vanish. Log it instead of letting "Failed to encode" be the whole story.
            if (bytes == null && !string.IsNullOrEmpty(error))
                AgentLogger.Warning(LogTag.Tool, $"SceneViewTools.EncodeWithOptions: {error}");

            return bytes;
        }

        /// <summary>
        /// Renders the active SceneView camera into a fresh Texture2D without touching PendingImage or the
        /// %TEMP% debug dump. Caller owns the texture and must DestroyImmediate it.
        /// Used by A/B comparison tools that need several renders per call, and by CaptureSceneView.
        /// Returns null with a filled <paramref name="error"/> if no SceneView is available or an option is
        /// invalid — never a black or half-drawn texture.
        ///
        /// <paramref name="opt"/> supplies MSAA (AntiAliasing) and the background treatment; pass null to get
        /// the historical behaviour (no MSAA, scene background, opaque RGB24 read-back), which is what the
        /// DiffImages pair relies on to make two renders comparable.
        /// <paramref name="drawMode"/> is "shaded" | "wireframe" | "shadedwireframe" (already lower-cased by
        /// the caller), <paramref name="lighting"/> is "scene" | "neutral".
        /// Notes about anything that silently could not be done (a wireframe pass with no visible lines, a
        /// neutral light that could not be created) are appended to <paramref name="notes"/> when non-null;
        /// callers surface them so the picture is never described as something it is not.
        ///
        /// Everything this method touches on the SceneView camera — target texture, transform, projection,
        /// clip planes, clear flags, background colour — plus GL.wireframe and the scene's ambient settings
        /// is restored in a finally block. GL.wireframe in particular leaks into every editor repaint if it
        /// is left on, so it is reset even on the exception path.
        /// </summary>
        internal static Texture2D RenderSceneViewToTexture(int width, int height, out string error,
                                                           bool overridePose = false,
                                                           Vector3 pivot = default,
                                                           Quaternion rotation = default,
                                                           float orthoSize = 0f,
                                                           CaptureOptions opt = null,
                                                           string drawMode = "shaded",
                                                           string lighting = "scene",
                                                           List<string> notes = null)
        {
            error = null;
            if (width <= 0 || height <= 0)
            {
                error = $"render size must be positive (got {width}x{height}).";
                return null;
            }

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                error = "No active SceneView found. Open a SceneView and frame the subject first.";
                return null;
            }
            var camera = sceneView.camera;
            if (camera == null)
            {
                error = "SceneView camera not available.";
                return null;
            }

            // No MSAA and no background override by default: ImageDiffTools renders two of these and
            // compares them pixel by pixel, so the defaults have to stay exactly what they were.
            opt = opt ?? CaptureOptions.Create(0, "png", 90, saveToPath: "", cropRegion: "",
                                               background: "scene", antiAliasing: 1);
            if (!CaptureCommon.TryParseBackground(opt.Background, out CaptureBackgroundMode bgMode,
                                                  out Color bgColor, out error))
                return null;
            bool keepAlpha = bgMode == CaptureBackgroundMode.Transparent;

            string mode = NormalizeChoice(drawMode, "shaded");
            bool wireOnly = mode == "wireframe";
            bool shadedWire = mode == "shadedwireframe";
            if (mode != "shaded" && !wireOnly && !shadedWire)
            {
                error = $"drawMode '{drawMode}' is not understood — use 'shaded', 'wireframe' or 'shadedWireframe'.";
                return null;
            }

            string light = NormalizeChoice(lighting, "scene");
            if (light != "scene" && light != "neutral")
            {
                error = $"lighting '{lighting}' is not understood — use 'scene' or 'neutral'.";
                return null;
            }

            // Saved even when not overriding, so the restore path below is unconditional and
            // cannot leave the SceneView camera somewhere the user did not put it.
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var oldPos = camera.transform.position;
            var oldRot = camera.transform.rotation;
            bool oldOrtho = camera.orthographic;
            float oldOrthoSize = camera.orthographicSize;
            float oldNear = camera.nearClipPlane;
            float oldFar = camera.farClipPlane;
            var oldClearFlags = camera.clearFlags;
            var oldBackground = camera.backgroundColor;
            bool oldWireframe = GL.wireframe;

            RenderTexture rt = null;
            Texture2D shadedTex = null;
            Texture2D wireTex = null;
            NeutralLightingScope lightingScope = null;
            try
            {
                if (light == "neutral")
                {
                    lightingScope = NeutralLightingScope.Create(camera, out string lightError);
                    if (lightingScope == null && notes != null)
                        notes.Add($"lighting='neutral' could NOT be applied ({lightError}); the image uses the " +
                                  "scene's own lighting, so a dark scene is still dark");
                }

                if (overridePose)
                {
                    // The camera transform is driven directly rather than through sceneView.pivot /
                    // rotation / size, because those are applied during the SceneView's own repaint —
                    // a Render() issued in the same call would still use the old transform and
                    // silently return the previous framing.
                    camera.orthographic = true;
                    camera.orthographicSize = orthoSize;
                    float back = Mathf.Max(orthoSize * 4f, 10f);
                    camera.transform.rotation = rotation;
                    camera.transform.position = pivot - rotation * Vector3.forward * back;
                    camera.nearClipPlane = 0.01f;
                    camera.farClipPlane = back * 4f;
                }

                // Without this the camera keeps the aspect of the SceneView panel, so the same
                // pose renders differently depending on how the user has sized their window.
                camera.aspect = (float)width / height;

                if (bgMode != CaptureBackgroundMode.Scene)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = bgColor;
                }

                rt = CaptureCommon.GetTemporaryTarget(width, height, opt, out error);
                if (rt == null) return null;

                camera.targetTexture = rt;

                // Pass 1: shaded, unless only wires were asked for.
                GL.wireframe = wireOnly;
                camera.Render();
                shadedTex = CaptureCommon.ReadBack(rt, keepAlpha, out error);
                if (shadedTex == null) return null;

                if (!shadedWire) return shadedTex;

                // Pass 2 for shadedWireframe: the same view in wireframe over a black clear, so any lit
                // pixel in it marks an edge. Detecting the lines from the shaded pass is impossible —
                // they are drawn in the material's own colour and would be indistinguishable from shading.
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                GL.wireframe = true;
                camera.Render();
                GL.wireframe = false;
                wireTex = CaptureCommon.ReadBack(rt, keepAlpha: false, out error);
                if (wireTex == null) return null;

                var composed = CompositeShadedWireframe(shadedTex, wireTex, keepAlpha, out int wirePixels,
                                                        out error);
                if (composed == null) return null;
                if (wirePixels == 0 && notes != null)
                    notes.Add("drawMode='shadedWireframe' found NO wireframe pixels (the wireframe pass came " +
                              "back empty — a fully black material, or a shader that ignores GL.wireframe), so " +
                              "the image is the shaded pass alone");

                UnityEngine.Object.DestroyImmediate(shadedTex);
                shadedTex = null;
                return composed;
            }
            catch (Exception ex)
            {
                error = $"the SceneView render failed: {ex.Message}";
                if (shadedTex != null)
                {
                    UnityEngine.Object.DestroyImmediate(shadedTex);
                    shadedTex = null;
                }
                return null;
            }
            finally
            {
                // GL.wireframe first: leaving it on breaks every subsequent editor repaint, which is far
                // worse than a mis-framed capture, so it is reset before anything else can throw.
                GL.wireframe = oldWireframe;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                camera.transform.position = oldPos;
                camera.transform.rotation = oldRot;
                camera.orthographic = oldOrtho;
                camera.orthographicSize = oldOrthoSize;
                camera.nearClipPlane = oldNear;
                camera.farClipPlane = oldFar;
                camera.clearFlags = oldClearFlags;
                camera.backgroundColor = oldBackground;
                camera.ResetAspect();
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (wireTex != null) UnityEngine.Object.DestroyImmediate(wireTex);
                if (lightingScope != null) lightingScope.Dispose();
            }
        }

        // Luminance above which a pixel of the black-cleared wireframe pass counts as a line. Kept low on
        // purpose: wires inherit the material's colour, so a dark material draws dim lines.
        private const float WireLumaThreshold = 0.02f;

        /// <summary>
        /// Burns the wireframe pass into the shaded pass. Where the wireframe pass has a lit pixel the
        /// output gets a line whose colour is chosen for contrast against the shaded pixel underneath —
        /// a fixed line colour disappears on either white or black surfaces, and an invisible wire looks
        /// exactly like a mesh with no edges there.
        ///
        /// <paramref name="wirePixels"/> reports how many line pixels were found so the caller can say so
        /// when the answer is zero. Returns null with an error rather than a partly composed image.
        /// The shaded/wire textures are left to the caller to destroy.
        /// </summary>
        private static Texture2D CompositeShadedWireframe(Texture2D shaded, Texture2D wire, bool keepAlpha,
                                                          out int wirePixels, out string error)
        {
            error = null;
            wirePixels = 0;
            if (shaded == null || wire == null)
            {
                error = "the shadedWireframe composite is missing one of its two passes.";
                return null;
            }
            if (shaded.width != wire.width || shaded.height != wire.height)
            {
                error = $"the shadedWireframe passes have different sizes ({shaded.width}x{shaded.height} vs " +
                        $"{wire.width}x{wire.height}).";
                return null;
            }

            Texture2D outTex = null;
            try
            {
                Color[] shadedPixels = shaded.GetPixels();
                Color[] wirePixelData = wire.GetPixels();
                if (shadedPixels.Length != wirePixelData.Length)
                {
                    error = "the shadedWireframe passes returned different pixel counts.";
                    return null;
                }

                for (int i = 0; i < shadedPixels.Length; i++)
                {
                    Color w = wirePixelData[i];
                    float wireLuma = w.r * 0.299f + w.g * 0.587f + w.b * 0.114f;
                    if (wireLuma <= WireLumaThreshold) continue;

                    Color s = shadedPixels[i];
                    float shadedLuma = s.r * 0.299f + s.g * 0.587f + s.b * 0.114f;
                    Color line = shadedLuma > 0.5f
                        ? new Color(0.04f, 0.04f, 0.05f)
                        : new Color(0.95f, 0.96f, 1f);
                    // A wire sitting on a transparent pixel (silhouette edge) must stay visible, so the
                    // line is opaque; elsewhere the shaded pass's own alpha is kept.
                    line.a = keepAlpha ? 1f : s.a;
                    shadedPixels[i] = line;
                    wirePixels++;
                }

                outTex = new Texture2D(shaded.width, shaded.height,
                                       keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                outTex.SetPixels(shadedPixels);
                outTex.Apply(false, false);
                return outTex;
            }
            catch (Exception ex)
            {
                if (outTex != null) UnityEngine.Object.DestroyImmediate(outTex);
                error = $"the shadedWireframe composite failed: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Temporary "readable no matter what the scene lighting is" setup: one directional key light aimed
        /// over the camera's shoulder plus flat ambient. Everything it changes — the scene's ambient mode,
        /// colour and intensity, and the light object itself — is undone in <see cref="Dispose"/>, including
        /// on the exception path.
        ///
        /// This does NOT switch the scene's own lights off. It guarantees the subject is lit; it does not
        /// reproduce a fixed studio look, so two captures of the same subject in differently lit scenes are
        /// still not directly comparable.
        ///
        /// Unity may flag the scene as modified because RenderSettings lives in the scene's lighting data.
        /// The values are restored, so saving after a capture writes back what was there before.
        /// </summary>
        private sealed class NeutralLightingScope : IDisposable
        {
            private GameObject _lightGo;
            private readonly UnityEngine.Rendering.AmbientMode _ambientMode;
            private readonly Color _ambientLight;
            private readonly float _ambientIntensity;
            private bool _disposed;

            private NeutralLightingScope(GameObject lightGo)
            {
                _lightGo = lightGo;
                _ambientMode = RenderSettings.ambientMode;
                _ambientLight = RenderSettings.ambientLight;
                _ambientIntensity = RenderSettings.ambientIntensity;
            }

            /// <summary>Returns null with a reason if the temporary light could not be created.</summary>
            internal static NeutralLightingScope Create(Camera camera, out string error)
            {
                error = null;
                GameObject go = null;
                try
                {
                    go = new GameObject("__NeutralCaptureLight") { hideFlags = HideFlags.HideAndDontSave };
                    var scope = new NeutralLightingScope(go);

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.color = Color.white;
                    light.intensity = 1.1f;
                    light.shadows = LightShadows.None;   // shadow maps add nothing to an inspection shot

                    // Key light over the camera's left shoulder, so the subject is lit from wherever the
                    // camera happens to be rather than from a fixed world direction.
                    Quaternion camRot = camera != null ? camera.transform.rotation : Quaternion.identity;
                    Vector3 dir = camRot * new Vector3(-0.45f, -0.7f, 1f);
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                    go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.34f, 0.34f, 0.38f, 1f);
                    RenderSettings.ambientIntensity = 1f;
                    return scope;
                }
                catch (Exception ex)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                    error = ex.Message;
                    return null;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    RenderSettings.ambientMode = _ambientMode;
                    RenderSettings.ambientLight = _ambientLight;
                    RenderSettings.ambientIntensity = _ambientIntensity;
                }
                catch (Exception ex)
                {
                    AgentLogger.Warning(LogTag.Tool,
                        $"SceneViewTools: the scene's ambient settings could NOT be restored after " +
                        $"lighting='neutral' ({ex.Message}). Check Window > Rendering > Lighting.");
                }
                finally
                {
                    if (_lightGo != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_lightGo);
                        _lightGo = null;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the optional pivot / rotation / orthoSize trio into a camera pose.
        /// All three must be supplied together: a half-specified pose would silently mix the
        /// caller's intent with whatever the user last left the SceneView pointing at, which is
        /// exactly the non-reproducibility these arguments exist to remove.
        /// </summary>
        internal static bool TryResolvePose(string pivot, string rotation, float orthoSize,
                                            out bool overridePose, out Vector3 pivotV,
                                            out Quaternion rotationQ, out string error)
        {
            overridePose = false;
            pivotV = Vector3.zero;
            rotationQ = Quaternion.identity;
            error = null;

            bool hasPivot = !string.IsNullOrWhiteSpace(pivot);
            bool hasRot = !string.IsNullOrWhiteSpace(rotation);
            bool hasSize = orthoSize > 0f;
            if (!hasPivot && !hasRot && !hasSize) return true;   // use the SceneView as-is

            if (!hasPivot || !hasRot || !hasSize)
            {
                error = "pivot, rotation and orthoSize must be given together (orthoSize > 0). " +
                        $"Got pivot='{pivot}', rotation='{rotation}', orthoSize={orthoSize}.";
                return false;
            }

            if (!TryParseVec3(pivot, out pivotV))
            {
                error = $"Could not parse pivot '{pivot}'. Expected 'x,y,z' (e.g. '0,1.2,0').";
                return false;
            }
            if (!TryParseVec3(rotation, out Vector3 euler))
            {
                error = $"Could not parse rotation '{rotation}'. Expected euler 'x,y,z' (e.g. '0,180,0').";
                return false;
            }

            rotationQ = Quaternion.Euler(euler);
            overridePose = true;
            return true;
        }

        static bool TryParseVec3(string s, out Vector3 v)
        {
            v = Vector3.zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != 3) return false;
            var ci = CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, ci, out float x)) return false;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, ci, out float y)) return false;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, ci, out float z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        // Saves bytes to an explicit path (creates dirs as needed). Silent on failure.
        // Returns true if a save was attempted (path non-empty) regardless of success.
        internal static bool TrySaveToPath(byte[] bytes, string saveToPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(saveToPath) || bytes == null || bytes.Length == 0) return false;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(saveToPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(saveToPath, bytes);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return true;
            }
        }

        // Lower-cases and trims an enum-like argument, substituting the default for an empty value.
        // Unknown values are rejected by the caller — never silently mapped onto the default, because a
        // typo'd drawMode that quietly renders shaded is indistinguishable from a working capture.
        private static string NormalizeChoice(string value, string fallback)
        {
            string s = (value ?? string.Empty).Trim().ToLowerInvariant();
            return s.Length == 0 ? fallback : s;
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        [AgentTool(@"Take a screenshot of the current SceneView.

source='render' (DEFAULT) draws the SceneView camera into a RenderTexture: any resolution, the user's focus
  is untouched, and the picture is clean — but gizmos, the grid, the selection outline and every editor
  overlay are ABSENT, because none of those are part of the camera's render.
source='window' photographs the SceneView tab as it is actually drawn on screen (via CaptureEditorWindow's
  focus-free PrintWindow route), so gizmos / grid / selection outline / overlays ARE in the picture. In
  return the resolution is the window's own, and width/height/pivot/rotation/orthoSize/drawMode/lighting/
  background/cropRegion/antiAliasing cannot apply — passing any of them with source='window' is an error
  rather than a silently ignored argument. Windows Editor only.
The result always states route=render or route=window, so an image can never be mistaken for the other kind.

width/height (default 1024) set the render resolution (route=render only).
maxWidth>0 downscales the longer side (preserves aspect). The result reports 'output WxH, source WxH'; the
  ATTACHED image is the output one, so build any pixel coordinate (DiffImages maskRegion, cropRegion, 'the
  seam is at x=...') against that.
format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90).
saveToPath: optional explicit save path in addition to the auto-attached image.
Every capture is also dumped to %TEMP%/unity-agent-captures/capture-YYYYMMDD-HHMMSS-NNN.png (.jpg for
  format='jpg'), a NEW file each time, so a before/after pair can both be re-read with the Read tool
  instead of the second overwriting the first. Only the newest 20 dumps are kept — older ones are
  deleted, so use saveToPath for anything that must survive longer than that.

drawMode='shaded' (default) | 'wireframe' | 'shadedWireframe' — wireframe uses GL.wireframe, so it shows
  the actual triangulation (retopo checks, hidden n-gon fans, decimation damage). 'shadedWireframe' renders
  both passes and burns the edges into the shaded image with a contrast-picked line colour. If the wireframe
  pass comes back empty the result SAYS so instead of handing back the shaded image as if it had wires.
lighting='scene' (default) | 'neutral' — 'neutral' adds a temporary directional key light over the camera's
  shoulder and flat ambient, so a subject in a dark or lightless scene is visible at all. It does NOT switch
  the scene's own lights off, so it makes the shot readable, not reproducible; the scene's ambient settings
  are restored afterwards (Unity may still flag the scene as modified).
background='scene' (default) | 'transparent' | '#RRGGBB' — 'transparent' requires format='png' (JPG has no
  alpha and would silently flatten it to black) and replaces the skybox/clear colour with alpha 0, which is
  what you want when the image is going to be composited or diffed against a different background.
cropRegion='x,y,w,h' in pixels of the rendered image, origin BOTTOM-LEFT — the same convention as
  DiffImages.maskRegion, so a rectangle measured once works in both. (CaptureEditorWindow's 'region' is
  TOP-LEFT based because window coordinates are; mixing the two crops the mirrored band.) A rectangle that
  does not fit is an error, never a silent clamp.
antiAliasing=1|2|4|8 (default 2) — MSAA sample count. 1 is jagged on thin geometry; anything else is an error.

pivot / rotation / orthoSize: pin the framing instead of using wherever the SceneView happens to
  be pointing. Supply all three or none.
    pivot     'x,y,z' world position to look at, e.g. '0,1.2,0'
    rotation  euler 'x,y,z' for the view direction, e.g. '0,180,0' to look at a character's front
    orthoSize HALF the world-space height that fits in the frame, in metres — so pick it as
              (subject height to show) / 2. On a ~1.6 m avatar: 0.15 a head close-up, 0.4 head
              and shoulders, 0.9 the whole body. Bigger = more of the scene, i.e. further away.
  COMPARING TWO STATES REQUIRES THIS. Without it any tool that changes Selection moves the
  SceneView camera, and two captures of the same subject silently differ in framing — a close-up
  and a full-body shot look like a regression that never happened. The camera is restored
  afterwards, so pinning the framing does not disturb what the user sees.
  CAUTION: pinning switches the camera to ORTHOGRAPHIC projection (orthoSize only means anything
  there), while omitting the trio uses the SceneView as-is, normally perspective. So pin BOTH
  shots of a pair or neither — pinning only one compares an ortho render against a perspective
  one, which is the very ""same subject looks like a different object"" failure these arguments
  exist to prevent.",
            Author = "ajisaiflow", Category = "SceneView", Risk = ToolRisk.Safe)]
        public static string CaptureSceneView(int width = 1024, int height = 1024, int maxWidth = 0,
                                              string format = "png", int jpgQuality = 90, string saveToPath = "",
                                              string pivot = "", string rotation = "", float orthoSize = 0f,
                                              string source = "render", string drawMode = "shaded",
                                              string lighting = "scene", string background = "scene",
                                              string cropRegion = "", int antiAliasing = 2)
        {
            string src = NormalizeChoice(source, CaptureRoute.Render);
            if (src != CaptureRoute.Render && src != CaptureRoute.Window)
                return $"Error: source '{source}' is not understood — use 'render' (the camera drawn into a " +
                       "RenderTexture: any resolution, no gizmos) or 'window' (the SceneView tab as drawn on " +
                       "screen: gizmos and overlays included, window resolution).";

            string mode = NormalizeChoice(drawMode, "shaded");
            if (mode != "shaded" && mode != "wireframe" && mode != "shadedwireframe")
                return $"Error: drawMode '{drawMode}' is not understood — use 'shaded', 'wireframe' or " +
                       "'shadedWireframe'.";

            string light = NormalizeChoice(lighting, "scene");
            if (light != "scene" && light != "neutral")
                return $"Error: lighting '{lighting}' is not understood — use 'scene' or 'neutral'.";

            if (!TryResolvePose(pivot, rotation, orthoSize, out bool overridePose,
                                out Vector3 pivotV, out Quaternion rotationQ, out string poseError))
                return $"Error: {poseError}";

            if (src == CaptureRoute.Window)
            {
                // Only the arguments whose non-default value proves an intent the window route cannot honour
                // are refused. width/height/antiAliasing have no "unset" value to test, so they are reported
                // as inapplicable in the result instead of guessed at.
                if (overridePose)
                    return "Error: source='window' cannot honour pivot/rotation/orthoSize — the window route " +
                           "photographs the SceneView as the user has it framed and cannot move the camera. " +
                           "Use source='render' to pin the framing, or drop the pose arguments to accept the " +
                           "user's current view.";
                if (mode != "shaded")
                    return $"Error: source='window' cannot honour drawMode='{drawMode}' — GL.wireframe only " +
                           "affects a camera render, and the window route copies pixels the SceneView already " +
                           "drew. Use source='render', or switch the SceneView's own shading mode by hand first.";
                if (light != "scene")
                    return $"Error: source='window' cannot honour lighting='{lighting}' — the window route " +
                           "copies pixels that were already drawn, so no temporary light can affect them. " +
                           "Use source='render'.";
                if (!string.Equals(NormalizeChoice(background, "scene"), "scene", StringComparison.Ordinal))
                    return $"Error: source='window' cannot honour background='{background}' — an OS window " +
                           "bitmap has no separable background and no usable alpha. Use source='render'.";
                if (!string.IsNullOrWhiteSpace(cropRegion))
                    return "Error: source='window' does not take cropRegion. Crop a window capture with " +
                           "CaptureEditorWindow's 'region' argument instead, which is TOP-LEFT based like the " +
                           "window coordinates it is measured in (cropRegion is BOTTOM-LEFT based); silently " +
                           "reusing one as the other crops the mirrored band of the image.";

#if UNITY_EDITOR_WIN
                return CaptureSceneViewThroughWindow(maxWidth, format, jpgQuality, saveToPath);
#else
                return "Error: source='window' is only available in the Windows Editor (it needs the Win32 " +
                       "PrintWindow path). Use source='render' on this platform — note that gizmos, the grid " +
                       "and the selection outline will not be in the picture.";
#endif
            }

            if (width < 8 || height < 8 || width > 16384 || height > 16384)
                return $"Error: width/height must be between 8 and 16384 (got {width}x{height}).";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath, cropRegion, background,
                                            antiAliasing);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            // Shares RenderSceneViewToTexture so the RenderTexture cleanup, camera restore and the
            // GL.wireframe reset happen in a finally. Inline, a throw from Render()/ReadPixels() left the
            // SceneView camera pointing at a destroyed target and corrupted later repaints.
            var notes = new List<string>();
            var tex = RenderSceneViewToTexture(width, height, out string renderError,
                                               overridePose, pivotV, rotationQ, orthoSize,
                                               opt, mode, light, notes);
            if (tex == null) return $"Error: {renderError}";

            string label = $"SceneView (drawMode={mode}, lighting={light})";
            string msg = CaptureCommon.Finish(tex, opt, label, CaptureRoute.Render, out string finishError,
                                              destroySource: true);
            if (msg == null) return $"Error: {finishError}";

            string poseMsg = overridePose
                ? $" Framing pinned (orthographic projection): pivot={pivot} rotation={rotation} orthoSize={F(orthoSize)} (reuse these exact values for a comparable shot; an unpinned capture renders in perspective and is not comparable to this one)."
                : " Framing: whatever the SceneView is currently showing — pass pivot/rotation/orthoSize if you intend to compare this against another capture.";
            string noteMsg = notes.Count > 0 ? " NOTE: " + string.Join("; ", notes) + "." : "";
            return msg + poseMsg + noteMsg;
        }

#if UNITY_EDITOR_WIN
        /// <summary>
        /// source='window' for CaptureSceneView: hands the job to CaptureEditorWindow rather than
        /// re-deriving it here.
        ///
        /// A docked SceneView has no OS window of its own, so a window capture has to resolve the container
        /// HWND, cut the tab's rect out of the main-window bitmap, refuse a background tab and force a
        /// synchronous repaint. All of that already exists once, in CaptureEditorWindow; a second copy in
        /// this file would be the same class of duplication that made the two encoders drift apart.
        ///
        /// The one thing that has to be computed here is WHICH same-titled window to take: with two Scene
        /// tabs open, matchIndex 0 is not necessarily the active one. The index is derived with the same
        /// enumeration and filter CaptureEditorWindow applies, so both sides agree on the ordering.
        /// </summary>
        private static string CaptureSceneViewThroughWindow(int maxWidth, string format, int jpgQuality,
                                                            string saveToPath)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return "Error: No active SceneView found. source='window' photographs the SceneView tab, so " +
                       "one has to be open on screen.";

            string title = sceneView.titleContent != null ? sceneView.titleContent.text : null;
            if (string.IsNullOrEmpty(title))
                return "Error: the active SceneView has no window title, so the window route cannot address " +
                       "it. Use source='render'.";

            if (!TryFindEditorWindowMatchIndex(sceneView, title, out int matchIndex, out int matchCount,
                                               out string indexError))
                return $"Error: {indexError}";

            string msg = WindowCaptureTools.CaptureEditorWindow(
                titleContains: title, matchIndex: matchIndex, waitForRepaint: true,
                maxWidth: maxWidth, format: format, jpgQuality: jpgQuality, saveToPath: saveToPath,
                bringToFront: true, focusless: true, region: "");

            if (msg == null) return "Error: the window capture returned nothing.";
            if (!msg.StartsWith("Success", StringComparison.Ordinal)) return msg;

            string which = matchCount > 1
                ? $" There are {matchCount} windows titled '{title}'; matchIndex={matchIndex} is the last " +
                  "ACTIVE SceneView (SceneView.lastActiveSceneView), which is the same one source='render' " +
                  "would have rendered."
                : "";
            return msg + " This is CaptureSceneView with source='window': the picture is the SceneView tab as " +
                   "drawn on screen, so gizmos, the grid, the selection outline and editor overlays are IN it, " +
                   "and width/height/antiAliasing did not apply (the resolution is the window's own). Use " +
                   "source='render' for a clean, arbitrary-resolution image without any of the overlays." + which;
        }

        /// <summary>
        /// Index of <paramref name="target"/> among the EditorWindows whose title contains
        /// <paramref name="title"/>, using the same enumeration + filter CaptureEditorWindow applies
        /// (Resources.FindObjectsOfTypeAll, skipping abstract types, untitled windows and zero-size rects).
        /// Returns false with a reason when the target itself does not survive that filter, because guessing
        /// 0 in that case would capture a different Scene tab and report it as this one.
        /// </summary>
        private static bool TryFindEditorWindowMatchIndex(EditorWindow target, string title,
                                                          out int matchIndex, out int matchCount,
                                                          out string error)
        {
            matchIndex = -1;
            matchCount = 0;
            error = null;

            var all = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var w in all)
            {
                if (w == null) continue;
                if (w.GetType().IsAbstract) continue;
                if (w.titleContent == null || string.IsNullOrEmpty(w.titleContent.text)) continue;
                if (w.position.width <= 0 || w.position.height <= 0) continue;
                if (w.titleContent.text.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (ReferenceEquals(w, target)) matchIndex = matchCount;
                matchCount++;
            }

            if (matchIndex < 0)
            {
                error = $"the active SceneView ('{title}') is not addressable as a window: it has a zero-size " +
                        "rect or no title, which happens while it is being opened, resized or closed. Retry, " +
                        "or use source='render'.";
                return false;
            }
            return true;
        }
#endif

        /// <summary>
        /// Distance at which a subject's bounding sphere just fits BOTH the vertical and the horizontal
        /// field of view, plus <paramref name="padding"/> as a fraction of that distance.
        ///
        /// The old formula was <c>maxExtent * 2.5</c>, which ignores the FOV entirely: it framed correctly
        /// only at whatever FOV it had been eyeballed for, and a tall thin mesh (whose diagonal is far larger
        /// than its largest extent) was cut off at top and bottom while a flat one came back tiny.
        ///
        /// <paramref name="aspect"/> is width/height of the target image — pass 1 for a square contact-sheet
        /// cell. The near/far planes are derived from the result so the subject cannot poke through the near
        /// plane (which looks exactly like a hole in the mesh) and so depth precision is not spent on empty
        /// space behind it.
        /// </summary>
        internal static bool TryComputeFraming(Bounds bounds, float verticalFov, float aspect, float padding,
                                               out float distance, out float radius,
                                               out float nearClip, out float farClip, out string error)
        {
            error = null;
            distance = 0f;
            nearClip = 0.01f;
            farClip = 1000f;

            // The bounding SPHERE radius, not the largest extent: it is the only measure that is correct for
            // every camera direction, so 'front' and '45right' frame the same subject the same size.
            radius = bounds.extents.magnitude;
            if (float.IsNaN(radius) || float.IsInfinity(radius))
            {
                error = "the subject's bounds are not a finite size (NaN/Infinity), so no camera distance " +
                        "can be computed. A mesh with corrupt vertices or an extreme transform scale does this.";
                return false;
            }
            // A single point, an empty mesh or a degenerate plane: pick a small radius so the framing is
            // merely arbitrary instead of a division by zero.
            if (radius < 1e-3f) radius = 1e-3f;

            float fov = Mathf.Clamp(verticalFov, 1f, 179f);
            float a = (aspect > 1e-4f && !float.IsNaN(aspect) && !float.IsInfinity(aspect)) ? aspect : 1f;
            float vHalf = fov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * a);
            float sinMin = Mathf.Sin(Mathf.Min(vHalf, hHalf));
            if (sinMin < 1e-4f)
            {
                error = $"the camera field of view ({F(verticalFov)} degrees at aspect {F(a)}) is too narrow to " +
                        "compute a framing distance.";
                return false;
            }

            float pad = Mathf.Max(0f, padding);
            distance = radius / sinMin * (1f + pad);
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f)
            {
                error = "the computed camera distance is not a usable number.";
                return false;
            }

            // Half way between the camera and the front of the bounding sphere, capped so a tiny subject
            // still gets a near plane it cannot cross, and floored so depth precision stays sane.
            nearClip = Mathf.Max(1e-4f, Mathf.Min(0.01f, (distance - radius) * 0.5f));
            farClip = Mathf.Max(nearClip * 1000f, (distance + radius) * 4f);
            return true;
        }

        // padding is a fraction of the framing distance, so anything past a few units is certainly a
        // mistyped pixel count rather than an intent. Rejected instead of clamped: a clamped padding frames
        // the subject differently from what was asked for and still reports success.
        private static bool TryValidatePadding(float padding, out string error)
        {
            error = null;
            if (float.IsNaN(padding) || float.IsInfinity(padding) || padding < 0f || padding > 10f)
            {
                error = $"padding {F(padding)} is outside 0..10. It is a FRACTION of the framing distance " +
                        "(0.1 = 10% margin around the subject), not a pixel count.";
                return false;
            }
            return true;
        }

        // Vertical FOV / clip hints taken from the SceneView so a contact sheet resembles what the user is
        // looking at. Clip planes are only a fallback: TryComputeFraming derives its own from the distance.
        private static float SceneViewFov()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                float fov = sceneView.camera.fieldOfView;
                if (fov > 1f && fov < 179f) return fov;
            }
            return 60f;
        }

        [AgentTool(@"Capture a target from multiple angles and compose into a labeled grid image.

angles: comma-separated from front,back,left,right,top,bottom,45left,45right. Default: front,left,right,back.
  An unrecognised name is an ERROR, not a skipped cell — silently dropping one shifts every index in the
  layout listing below the image, so [2] would name the wrong picture.
  ANGLE NAMES SAY WHERE THE CAMERA STANDS: 'front' puts the camera on +Z looking toward -Z, which is the
  FACE of an avatar built facing +Z (Unity's convention); 'left' puts it on -X, i.e. it shows the subject's
  left side. Same meaning as CaptureAnimationFrames' angle argument, so a pose sheet and an angle sheet of
  the same avatar can be compared cell to cell.
cellSize is the per-cell resolution (default 384). Cells are square and the camera aspect is pinned to 1,
  so the subject is not stretched by whatever aspect the editor happens to have.
padding (default 0.1) is the margin around the subject as a fraction of the camera distance — 0 fits the
  bounding sphere exactly, 0.3 pulls back for context. The distance itself is derived from the SceneView's
  field of view, so changing the FOV no longer changes how big the subject comes out.
maxWidth>0 downscales the final composite (preserves aspect).
format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90).
saveToPath: optional explicit save path.

Bounds come from ACTIVE, ENABLED renderers only, so an inactive clothing variant cannot push the camera
back and shrink the subject. The cell label is burned into the pixels (5x7 dot font, dark plate), so it is
there whatever the material and background are; the result also lists the cell order as text.
Route is always render: gizmos, the grid and the selection outline are NOT in these images.",
            Author = "ajisaiflow", Category = "SceneView", Risk = ToolRisk.Safe)]
        public static string CaptureMultiAngle(string targetName, string angles = "front,left,right,back",
                                               int cellSize = 384, int maxWidth = 0, string format = "png",
                                               int jpgQuality = 90, string saveToPath = "", float padding = 0.1f)
        {
            var target = FindGO(targetName);
            if (target == null) return $"Error: GameObject '{targetName}' not found.";
            if (cellSize < 32 || cellSize > 4096)
                return $"Error: cellSize must be between 32 and 4096 (got {cellSize}).";
            if (!TryValidatePadding(padding, out string padError)) return $"Error: {padError}";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            // Parse the angles before touching the scene: an unknown name must fail before a temporary
            // camera and render target have been created.
            var angleNames = angles == null
                ? new List<string>()
                : angles.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
            if (angleNames.Count == 0) return "Error: No valid angles specified.";
            if (angleNames.Count > 8)
                return $"Error: Maximum 8 angles allowed (got {angleNames.Count}).";

            var dirs = new List<Vector3>(angleNames.Count);
            foreach (var name in angleNames)
            {
                if (!TryGetCameraSide(name, out Vector3 side, out string angleError))
                    return $"Error: {angleError}";
                dirs.Add(side);
            }

            // Calculate bounds from ACTIVE+ENABLED renderers only (inactive clothing
            // variants etc. would otherwise inflate bounds and push the camera too far away).
            var allRenderers = target.GetComponentsInChildren<Renderer>(true);
            var renderers = allRenderers.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
            string boundsNote = "";
            if (renderers.Length == 0)
            {
                // Nothing visible: the bounds of the disabled renderers are still better framing than none,
                // but the picture will be empty, so say which of the two happened.
                renderers = allRenderers.Where(r => r != null).ToArray();
                if (renderers.Length == 0)
                    return $"Error: No renderers found under '{targetName}'.";
                boundsNote = $" WARNING: none of the {renderers.Length} renderers under '{targetName}' is both " +
                             "enabled and on an active GameObject, so the framing is derived from geometry that " +
                             "is NOT drawn — expect empty cells. Use CaptureMeshIsolated with " +
                             "activateHidden=true to photograph a hidden subject.";
            }

            // For SkinnedMeshRenderer, .bounds is the runtime-skinned bounding box which
            // includes animation extension and is often inflated. Use sharedMesh.bounds
            // transformed to world space for tighter framing.
            Bounds bounds = ComputeTightBounds(renderers[0]);
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(ComputeTightBounds(renderers[i]));

            Vector3 center = bounds.center;
            float fov = SceneViewFov();
            if (!TryComputeFraming(bounds, fov, 1f, padding, out float distance, out float radius,
                                   out float nearClip, out float farClip, out string framingError))
                return $"Error: {framingError}";

            GameObject camGo = null;
            RenderTexture rt = null;
            var cellTextures = new List<Texture2D>();
            try
            {
                camGo = new GameObject("__MultiAngleCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = nearClip;
                cam.farClipPlane = farClip;
                cam.aspect = 1f;   // square cells; without this the camera inherits the screen aspect
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.enabled = false;

                rt = CaptureCommon.GetTemporaryTarget(cellSize, cellSize, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";

                for (int i = 0; i < dirs.Count; i++)
                {
                    cam.transform.position = center + dirs[i] * distance;
                    cam.transform.rotation = LookAtRotation(center - cam.transform.position);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;

                    var tex = CaptureCommon.ReadBack(rt, keepAlpha: false, out string readError);
                    if (tex == null) return $"Error: angle '{angleNames[i]}' could not be read back: {readError}";
                    cellTextures.Add(tex);
                }

                CaptureCommon.ComputeGrid(cellTextures.Count, out int cols, out int rows);
                if (cols <= 0 || rows <= 0) return "Error: No valid angles could be captured.";

                var composite = ComposeGrid(cellTextures, cols, rows, cellSize,
                                            angleNames.Select((a, i) => $"[{i}] {a}").ToList(),
                                            out int labelsDrawn, out string composeError);
                if (composite == null) return $"Error: {composeError}";

                string label = $"'{targetName}' from {cellTextures.Count} angles in a {cols}x{rows} grid";
                string msg = CaptureCommon.Finish(composite, opt, label, CaptureRoute.Render,
                                                  out string finishError, destroySource: true);
                if (msg == null) return $"Error: {finishError}";

                string labelInfo = string.Join(", ", angleNames.Select((l, i) => $"[{i}]={l}"));
                string labelNote = labelsDrawn == cellTextures.Count
                    ? ""
                    : $" NOTE: only {labelsDrawn} of {cellTextures.Count} cell labels could be drawn into the " +
                      "image — use the text layout below, not the picture, to tell the cells apart.";
                return msg + $" Layout (left-to-right, top-to-bottom): {labelInfo}." +
                       $" Camera distance {F(distance)}m (subject radius {F(radius)}m, fov {F(fov)}, padding {F(padding)})." +
                       " Angle names say where the CAMERA stands: front = camera on +Z = the face of a +Z-facing avatar." +
                       labelNote + boundsNote;
            }
            finally
            {
                foreach (var tex in cellTextures)
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>
        /// Lays square cells out left-to-right, top-to-bottom on a dark plate and burns
        /// <paramref name="cellLabels"/> into the bottom-left corner of each one.
        ///
        /// Labels are drawn with CaptureCommon's built-in 5x7 font rather than a TextMesh in the scene: a
        /// TextMesh label is a scene object that has to be positioned in front of the camera, is occluded by
        /// the very geometry it describes, needs a builtin font resource to be present, and renders in the
        /// cell BEFORE the grid exists, so it cannot be placed relative to the final image. Burning the text
        /// in afterwards always lands, on any background, at a known place.
        ///
        /// <paramref name="labelsDrawn"/> counts labels that actually reached the pixels, so a caller can say
        /// "the labels are missing" instead of describing a picture that has none.
        /// Returns null with an error rather than a half-filled composite; the caller owns the result.
        /// </summary>
        private static Texture2D ComposeGrid(List<Texture2D> cells, int cols, int rows, int cellSize,
                                             List<string> cellLabels, out int labelsDrawn, out string error)
        {
            error = null;
            labelsDrawn = 0;
            if (cells == null || cells.Count == 0)
            {
                error = "there are no cells to compose.";
                return null;
            }

            int gridW = cols * cellSize;
            int gridH = rows * cellSize;
            Texture2D composite = null;
            try
            {
                composite = new Texture2D(gridW, gridH, TextureFormat.RGB24, false);

                var bgPixels = new Color[gridW * gridH];
                var plate = new Color(0.15f, 0.15f, 0.15f, 1f);
                for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = plate;
                composite.SetPixels(bgPixels);

                // Readable at any cell size: ~1/48 of the cell, floor 2 (a 1x scale 5x7 glyph is unreadable).
                int scale = Mathf.Clamp(cellSize / 48, 2, 10);
                int margin = Mathf.Max(3, scale);

                for (int i = 0; i < cells.Count; i++)
                {
                    int col = i % cols;
                    int row = rows - 1 - (i / cols);   // fill top-left first; texture origin is bottom-left
                    int x = col * cellSize;
                    int y = row * cellSize;

                    if (cells[i] == null)
                    {
                        error = $"cell {i} is missing.";
                        return null;
                    }
                    composite.SetPixels(x, y, cellSize, cellSize, cells[i].GetPixels());

                    if (cellLabels != null && i < cellLabels.Count && !string.IsNullOrEmpty(cellLabels[i]))
                    {
                        if (CaptureCommon.DrawTextWithBackground(composite, x + margin, y + margin,
                                                                 cellLabels[i], scale, apply: false))
                            labelsDrawn++;
                    }
                }

                composite.Apply(false, false);
                return composite;
            }
            catch (Exception ex)
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
                error = $"the {cols}x{rows} composite could not be built: {ex.Message}";
                return null;
            }
        }

        [AgentTool(@"Capture a single mesh/GameObject in scene-wide ISOLATION from multiple angles, composed into a labeled grid.
All other renderers in the scene are hidden during capture so you see ONLY the target mesh — useful for
inspecting hidden or occluded parts. Every Renderer.enabled it touches is restored afterwards, including
when the capture throws.

targetName: exact GameObject name (or partial via FindGameObject). Captures all Renderers under this
  GameObject (self+children).
angles: comma-separated from front,back,left,right,top,bottom,45left,45right. Default: front,left,right,back.
  An unrecognised name is an error, not a skipped cell. 'front' means the CAMERA stands on +Z, i.e. the face
  of an avatar built facing +Z — the same convention as CaptureAnimationFrames' angle.
cellSize: per-angle cell resolution (default 384).
padding (default 0.1): margin around the subject as a fraction of the camera distance. The distance itself
  now follows the SceneView's field of view, so a tall thin mesh is no longer cut off top and bottom.
maxWidth>0 downscales the final composite.
format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90).
saveToPath: optional explicit save path.

activateHidden=false (DEFAULT) only touches Renderer.enabled. If the target sits under a GameObject that is
  SetActive(false) it CANNOT be photographed that way, and this tool then returns an error naming the
  inactive ancestor instead of an empty grid that looks like a mesh with no geometry.
activateHidden=true additionally SetActive(true)s the target's inactive ancestors for the duration of the
  capture and writes each one's own activeSelf back afterwards, so the hierarchy returns exactly as it was
  (this happens in a finally, so a failed capture restores it too). Use it to
  inspect an outfit variant that is currently switched off — but be aware it briefly makes those objects
  active for everything else in the editor too, and an interrupted call (domain reload, exception in a
  callback) can leave them that way. Objects that are inactive INSIDE the target subtree are never touched
  (that would change what the subject is); the result names them instead of quietly omitting them.

The cell label is burned into the pixels (5x7 dot font on a dark plate), so it is legible whatever the
material and background are, and the result also lists the cell order as text.
Route is always render: gizmos, the grid and the selection outline are NOT in these images.",
            Author = "ajisaiflow", Category = "SceneView", Risk = ToolRisk.Caution)]
        public static string CaptureMeshIsolated(string targetName, string angles = "front,left,right,back",
                                                 int cellSize = 384, int maxWidth = 0, string format = "png",
                                                 int jpgQuality = 90, string saveToPath = "",
                                                 float padding = 0.1f, bool activateHidden = false)
        {
            var target = FindGO(targetName);
            if (target == null) return $"Error: GameObject '{targetName}' not found.";
            if (cellSize < 32 || cellSize > 4096)
                return $"Error: cellSize must be between 32 and 4096 (got {cellSize}).";
            if (!TryValidatePadding(padding, out string padError)) return $"Error: {padError}";

            var targetRenderers = target.GetComponentsInChildren<Renderer>(true)
                                        .Where(r => r != null).ToArray();
            if (targetRenderers.Length == 0)
                return $"Error: '{targetName}' has no Renderer (neither itself nor children).";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            var angleNames = angles == null
                ? new List<string>()
                : angles.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
            if (angleNames.Count == 0) return "Error: No valid angles specified.";
            if (angleNames.Count > 8) return $"Error: Maximum 8 angles allowed (got {angleNames.Count}).";

            var dirs = new List<Vector3>(angleNames.Count);
            foreach (var name in angleNames)
            {
                if (!TryGetCameraSide(name, out Vector3 side, out string angleError))
                    return $"Error: {angleError}";
                dirs.Add(side);
            }

            // Scene-wide isolation set, captured BEFORE any temporary object of ours exists so that nothing
            // this tool creates can end up in it and be switched off along with the scene.
            var sceneRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            var targetSet = new HashSet<Renderer>(targetRenderers);

            var originalStates = new bool[sceneRenderers.Length];
            for (int i = 0; i < sceneRenderers.Length; i++)
                originalStates[i] = sceneRenderers[i] != null && sceneRenderers[i].enabled;

            // Ancestors we switched on, to be restored children-first in the finally.
            var ancestorActiveBackup = new List<KeyValuePair<GameObject, bool>>();

            GameObject camGo = null;
            RenderTexture rt = null;
            var cellTextures = new List<Texture2D>();
            try
            {
                if (activateHidden)
                {
                    for (Transform t = target.transform; t != null; t = t.parent)
                    {
                        if (!t.gameObject.activeSelf)
                        {
                            ancestorActiveBackup.Add(new KeyValuePair<GameObject, bool>(t.gameObject, false));
                            t.gameObject.SetActive(true);
                        }
                    }
                }

                // Which target renderers will actually draw. Decided AFTER the optional activation, so the
                // answer is about the state the render will really see.
                var visible = targetRenderers.Where(r => r.gameObject.activeInHierarchy).ToArray();
                if (visible.Length == 0)
                {
                    string blocker = FindInactiveAncestorName(target.transform);
                    return activateHidden
                        ? $"Error: none of the {targetRenderers.Length} renderers under '{targetName}' is on an " +
                          "active GameObject even after activateHidden switched its ancestors on — the inactive " +
                          "objects are INSIDE the target subtree, which this tool does not touch (it would " +
                          "change what the subject is). Activate them yourself, or capture a child that is active."
                        : $"Error: none of the {targetRenderers.Length} renderers under '{targetName}' is on an " +
                          $"active GameObject{(blocker != null ? $" (inactive ancestor: '{blocker}')" : "")}, so " +
                          "isolating them would return an empty picture. Pass activateHidden=true to switch the " +
                          "inactive ancestors on for the duration of the capture (restored afterwards), or " +
                          "activate the object yourself first.";
                }

                string skippedNote = visible.Length == targetRenderers.Length
                    ? ""
                    : $" NOTE: {targetRenderers.Length - visible.Length} of {targetRenderers.Length} renderers " +
                      "under the target are on inactive GameObjects and are NOT in the picture" +
                      (activateHidden ? " (they are inside the target subtree, which activateHidden does not touch)."
                                      : "; pass activateHidden=true to include them.");

                // Framing from the renderers that will actually be drawn — bounds that include hidden
                // geometry push the camera back and shrink the subject for no visible reason.
                Bounds bounds = ComputeTightBounds(visible[0]);
                for (int i = 1; i < visible.Length; i++)
                    bounds.Encapsulate(ComputeTightBounds(visible[i]));

                float fov = SceneViewFov();
                if (!TryComputeFraming(bounds, fov, 1f, padding, out float distance, out float radius,
                                       out float nearClip, out float farClip, out string framingError))
                    return $"Error: {framingError}";
                Vector3 center = bounds.center;

                camGo = new GameObject("__MeshIsolatedCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = nearClip;
                cam.farClipPlane = farClip;
                cam.aspect = 1f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                cam.enabled = false;

                rt = CaptureCommon.GetTemporaryTarget(cellSize, cellSize, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";

                // Isolate target (and its descendants) — disable everything else.
                for (int i = 0; i < sceneRenderers.Length; i++)
                {
                    if (sceneRenderers[i] == null) continue;
                    sceneRenderers[i].enabled = targetSet.Contains(sceneRenderers[i]);
                }

                for (int i = 0; i < dirs.Count; i++)
                {
                    cam.transform.position = center + dirs[i] * distance;
                    cam.transform.rotation = LookAtRotation(center - cam.transform.position);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;

                    var tex = CaptureCommon.ReadBack(rt, keepAlpha: false, out string readError);
                    if (tex == null) return $"Error: angle '{angleNames[i]}' could not be read back: {readError}";
                    cellTextures.Add(tex);
                }

                CaptureCommon.ComputeGrid(cellTextures.Count, out int cols, out int rows);
                var composite = ComposeGrid(cellTextures, cols, rows, cellSize,
                                            angleNames.Select((a, i) => $"[{i}] {a}").ToList(),
                                            out int labelsDrawn, out string composeError);
                if (composite == null) return $"Error: {composeError}";

                int totalVerts = 0;
                foreach (var r in visible)
                {
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) totalVerts += smr.sharedMesh.vertexCount;
                    else if (r is MeshRenderer)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) totalVerts += mf.sharedMesh.vertexCount;
                    }
                }

                string label = $"'{targetName}' in scene-wide isolation ({visible.Length} renderers drawn, " +
                               $"{totalVerts:N0} verts) from {cellTextures.Count} angles in a {cols}x{rows} grid";
                string msg = CaptureCommon.Finish(composite, opt, label, CaptureRoute.Render,
                                                  out string finishError, destroySource: true);
                if (msg == null) return $"Error: {finishError}";

                string labelInfo = string.Join(", ", angleNames.Select((a, i) => $"[{i}]={a}"));
                string labelNote = labelsDrawn == cellTextures.Count
                    ? ""
                    : $" NOTE: only {labelsDrawn} of {cellTextures.Count} cell labels could be drawn into the image.";
                string activateNote = ancestorActiveBackup.Count > 0
                    ? $" activateHidden=true temporarily activated {ancestorActiveBackup.Count} inactive " +
                      "ancestor(s); they have been switched back off."
                    : "";
                return msg + $" Layout: {labelInfo}." +
                       $" Camera distance {F(distance)}m (subject radius {F(radius)}m, fov {F(fov)}, padding {F(padding)})." +
                       " Angle names say where the CAMERA stands: front = camera on +Z." +
                       labelNote + skippedNote + activateNote;
            }
            finally
            {
                for (int j = 0; j < sceneRenderers.Length; j++)
                    if (sceneRenderers[j] != null) sceneRenderers[j].enabled = originalStates[j];

                // SetActive writes each object's OWN activeSelf, so the order cannot change the end state;
                // the list is walked backwards (root first) simply to undo the activation in the reverse
                // order it was applied.
                for (int i = ancestorActiveBackup.Count - 1; i >= 0; i--)
                {
                    if (ancestorActiveBackup[i].Key != null)
                        ancestorActiveBackup[i].Key.SetActive(ancestorActiveBackup[i].Value);
                }

                foreach (var tex in cellTextures)
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        // Name of the closest ancestor (self included) that is SetActive(false), or null when the whole
        // chain is active. Used to name the actual blocker instead of saying "something is inactive".
        private static string FindInactiveAncestorName(Transform t)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                if (!cur.gameObject.activeSelf) return cur.name;
            return null;
        }

        [AgentTool(@"Scan all meshes under an avatar and capture each one ISOLATED (other meshes hidden) into a labeled grid image.

cellSize is the per-mesh cell resolution (default 256).
padding (default 0.1): margin around each mesh as a fraction of the camera distance. The distance follows
  the SceneView's field of view, so a long thin mesh (a tail, a strand of hair) is framed rather than
  clipped, and every cell shows its mesh at a comparable size.
maxWidth>0 downscales the final composite.
format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90).
saveToPath: optional explicit save path.

Each mesh is viewed from 45right — the camera on the front-right (+X/+Z), i.e. the same convention as
CaptureMultiAngle and CaptureAnimationFrames — so a +Z-facing avatar shows its face, not the back of its head.
Meshes are sorted by vertex count (largest first) and capped at 16. Every Renderer.enabled the isolation
touches is restored, including when the capture throws.
Use this BEFORE modifying any mesh to visually identify what each GameObject actually is. Returns image +
text mapping; the cell label is rendered into each cell during the capture.",
            Author = "ajisaiflow", Category = "SceneView", Risk = ToolRisk.Caution)]
        public static string ScanAvatarMeshes(string avatarRootName, int cellSize = 256, int maxWidth = 0,
                                              string format = "png", int jpgQuality = 90, string saveToPath = "",
                                              float padding = 0.1f)
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";
            if (cellSize < 32 || cellSize > 4096)
                return $"Error: cellSize must be between 32 and 4096 (got {cellSize}).";
            if (!TryValidatePadding(padding, out string padError)) return $"Error: {padError}";

            var allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true)
                                         .Where(r => r != null).ToArray();
            if (allRenderers.Length == 0)
                return $"Error: No renderers found under '{avatarRootName}'.";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            // SCENE-WIDE isolation set: include every Renderer in the active scene so other
            // active avatars don't bleed into the per-mesh capture. Without this, scenes that
            // have multiple active avatar variants (e.g., capra + capra (BBP 4)) would show
            // both avatars in every cell, masking the per-mesh isolation effect.
            // Taken before any temporary object of ours exists, so the label TextMesh created below is
            // not in the set and cannot be switched off along with the scene.
            var sceneRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);

            // Sort by vertex count (largest first), limit to 16
            var rendererList = new List<(Renderer renderer, int vertCount)>();
            foreach (var r in allRenderers)
            {
                int verts = 0;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    verts = smr.sharedMesh.vertexCount;
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        verts = mf.sharedMesh.vertexCount;
                }
                rendererList.Add((r, verts));
            }
            rendererList.Sort((a, b) => b.vertCount.CompareTo(a.vertCount));
            int skippedByCap = 0;
            if (rendererList.Count > 16)
            {
                skippedByCap = rendererList.Count - 16;
                rendererList.RemoveRange(16, skippedByCap);
            }

            int count = rendererList.Count;
            CaptureCommon.ComputeGrid(count, out int cols, out int rows);

            float fov = SceneViewFov();
            if (!TryGetCameraSide("45right", out Vector3 cameraSide, out string angleError))
                return $"Error: {angleError}";

            // Save original enabled states for ALL scene renderers (we toggle them all)
            var originalStates = new bool[sceneRenderers.Length];
            for (int i = 0; i < sceneRenderers.Length; i++)
                originalStates[i] = sceneRenderers[i] != null && sceneRenderers[i].enabled;

            GameObject camGo = null;
            RenderTexture rt = null;
            var cellTextures = new List<Texture2D>();
            var labels = new List<string>();
            var hiddenLabels = new List<string>();
            try
            {
                camGo = new GameObject("__ScanMeshCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.aspect = 1f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                cam.enabled = false;

                // ── Label TextMesh (child of camera, rendered into each cell) ──
                // Unlike CaptureMultiAngle / CaptureMeshIsolated — which burn their short angle names in
                // with CaptureCommon's 5x7 dot font — the label here is a GameObject NAME, and avatar meshes
                // are routinely named in Japanese. The dot font has no CJK glyphs and would draw a row of
                // hollow boxes, so the built-in TTF via TextMesh is the only option that can show the name.
                // Position in camera-local space at the bottom of the view.
                // Use a LARGER z (further from camera) so the text occupies a smaller fraction
                // of the cell, avoiding overlap with adjacent cells and giving the mesh more room.
                var labelGo = new GameObject("__ScanMeshLabel") { hideFlags = HideFlags.HideAndDontSave };
                labelGo.transform.SetParent(camGo.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, -1.6f, 3f);  // z=3m, y near bottom of cell
                labelGo.transform.localRotation = Quaternion.identity;
                var labelTm = labelGo.AddComponent<TextMesh>();
                labelTm.alignment = TextAlignment.Center;
                labelTm.anchor = TextAnchor.LowerCenter;
                labelTm.color = Color.white;
                labelTm.fontSize = 100;
                labelTm.characterSize = 0.04f;  // text occupies ~9% viewport width per char
                // Use the Unity built-in legacy font (works without project asset dependencies)
                labelTm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (labelTm.font == null)
                    labelTm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (labelTm.font != null && labelTm.font.material != null)
                    labelTm.GetComponent<MeshRenderer>().sharedMaterial = labelTm.font.material;
                // Neither builtin font resolved: the cells will have NO label in them. Reported below rather
                // than left for the reader to discover, since the text mapping is then the only way to tell
                // the cells apart.
                bool labelFontOk = labelTm.font != null;

                rt = CaptureCommon.GetTemporaryTarget(cellSize, cellSize, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";

                for (int idx = 0; idx < rendererList.Count; idx++)
                {
                    var targetRenderer = rendererList[idx].renderer;
                    int vertCount = rendererList[idx].vertCount;

                    // Isolate scene-wide: disable every renderer in the scene, then
                    // enable only the target. This prevents other active avatars or
                    // overlapping meshes from bleeding into the per-mesh capture.
                    for (int j = 0; j < sceneRenderers.Length; j++)
                    {
                        if (sceneRenderers[j] == null) continue;
                        sceneRenderers[j].enabled = (sceneRenderers[j] == targetRenderer);
                    }

                    // Camera position from target bounds (use tight mesh.bounds for SMR
                    // to avoid runtime-inflated skinning bounds pushing camera too far)
                    var bounds = ComputeTightBounds(targetRenderer);
                    if (!TryComputeFraming(bounds, fov, 1f, padding, out float distance, out _,
                                           out float nearClip, out float farClip, out string framingError))
                        return $"Error: mesh '{targetRenderer.gameObject.name}': {framingError}";
                    cam.nearClipPlane = nearClip;
                    cam.farClipPlane = farClip;
                    cam.transform.position = bounds.center + cameraSide * distance;
                    cam.transform.rotation = LookAtRotation(bounds.center - cam.transform.position);

                    // Set label text for this cell — TextMesh re-renders each frame
                    string goNameForLabel = targetRenderer.gameObject.name;
                    if (goNameForLabel.Length > 14) goNameForLabel = goNameForLabel.Substring(0, 14) + "…";
                    labelTm.text = $"[{idx + 1}] {goNameForLabel}";

                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;

                    var tex = CaptureCommon.ReadBack(rt, keepAlpha: false, out string readError);
                    if (tex == null)
                        return $"Error: mesh '{targetRenderer.gameObject.name}' could not be read back: {readError}";
                    cellTextures.Add(tex);

                    // Label info
                    string matName = targetRenderer.sharedMaterial != null ? targetRenderer.sharedMaterial.name : "none";
                    string goName = targetRenderer.gameObject.name;
                    bool drawn = targetRenderer.gameObject.activeInHierarchy;
                    if (!drawn) hiddenLabels.Add($"[{idx + 1}] {goName}");
                    labels.Add($"[{idx + 1}] {goName} — {vertCount:N0} verts, mat: {matName}" +
                               (drawn ? "" : " — NOT IN THE PICTURE: this GameObject is inactive, so its cell is empty"));
                }

                var composite = ComposeGrid(cellTextures, cols, rows, cellSize,
                                            cellLabels: null, labelsDrawn: out _, error: out string composeError);
                if (composite == null) return $"Error: {composeError}";

                string label = $"{count} meshes under '{avatarRootName}', each isolated, in a {cols}x{rows} grid";
                string msg = CaptureCommon.Finish(composite, opt, label, CaptureRoute.Render,
                                                  out string finishError, destroySource: true);
                if (msg == null) return $"Error: {finishError}";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(msg);
                sb.AppendLine($"Grid {cols}x{rows}, left→right, top→bottom (camera on the front-right = 45right; " +
                              (labelFontOk
                                ? "each cell is labelled in the image"
                                : "the in-cell labels are MISSING — neither builtin font could be loaded, so use " +
                                  "the mapping below to tell the cells apart") + "):");
                foreach (var l in labels)
                    sb.AppendLine($"  {l}");
                if (skippedByCap > 0)
                    sb.AppendLine($"  ... and {skippedByCap} more renderer(s) NOT captured: the scan is capped at " +
                                  "16 meshes (the ones with the most vertices are kept). Use CaptureMeshIsolated " +
                                  "on the remaining objects by name.");
                if (hiddenLabels.Count > 0)
                    sb.AppendLine($"  WARNING: {hiddenLabels.Count} cell(s) are EMPTY because the GameObject is " +
                                  $"inactive: {string.Join(", ", hiddenLabels)}. Use CaptureMeshIsolated with " +
                                  "activateHidden=true to see those meshes.");
                sb.Append("Identify each mesh visually before proceeding.");
                return sb.ToString();
            }
            finally
            {
                // Restore states in case of exception (scene-wide)
                for (int j = 0; j < sceneRenderers.Length; j++)
                    if (sceneRenderers[j] != null) sceneRenderers[j].enabled = originalStates[j];

                foreach (var tex in cellTextures)
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);   // destroys the label child too
            }
        }

        /// <summary>
        /// Resolves an angle name to the offset direction FROM the subject TO the camera, so the camera sits
        /// at <c>center + dir * distance</c> and looks back at the subject.
        ///
        /// Stated that way on purpose, and identical to AnimationCaptureTools.TryResolveAngle. This used to
        /// return the opposite (the tools subtracted it), which put the camera on -Z for 'front': a +Z-facing
        /// avatar came back showing the BACK of its head, left and right were swapped, and 'top' photographed
        /// the subject from BELOW. Every one of those is a plausible-looking picture of the wrong thing, and
        /// comparing such a sheet against CaptureAnimationFrames' — which always used this convention —
        /// looked like the mesh had been mirrored.
        ///
        /// Unknown names are an error: skipping a cell shifts the indices of every cell after it, so the
        /// text layout would name the wrong pictures.
        /// Numeric 'yaw,pitch' is deliberately NOT accepted here (CaptureAnimationFrames does accept it):
        /// these tools take a comma-separated LIST of angles, so a comma inside one angle is ambiguous.
        /// </summary>
        private static bool TryGetCameraSide(string angle, out Vector3 dir, out string error)
        {
            dir = Vector3.forward;
            error = null;
            switch ((angle ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "front": dir = new Vector3(0f, 0f, 1f); return true;    // camera on +Z: the face
                case "back": dir = new Vector3(0f, 0f, -1f); return true;
                case "right": dir = new Vector3(1f, 0f, 0f); return true;    // camera on +X: the right flank
                case "left": dir = new Vector3(-1f, 0f, 0f); return true;
                case "top": dir = new Vector3(0f, 1f, 0f); return true;      // camera above, looking down
                case "bottom": dir = new Vector3(0f, -1f, 0f); return true;
                case "45left": dir = new Vector3(-1f, 0f, 1f).normalized; return true;
                case "45right": dir = new Vector3(1f, 0f, 1f).normalized; return true;
            }
            error = $"angle '{angle}' is not understood. Use front, back, left, right, top, bottom, 45left or " +
                    "45right (the name says where the CAMERA stands: front = camera on +Z = the face of a " +
                    "+Z-facing avatar).";
            return false;
        }

        /// <summary>
        /// LookRotation with a reference up vector that survives looking straight up or down. With
        /// Vector3.up as the reference, angle='top' / 'bottom' point the view direction along the up axis,
        /// where the roll is undefined and Quaternion.LookRotation returns an arbitrary orientation — the
        /// cell then comes back rotated by some multiple of 90 degrees for no visible reason.
        /// </summary>
        private static Quaternion LookAtRotation(Vector3 forward)
        {
            Vector3 f = forward;
            if (f.sqrMagnitude < 1e-8f) f = Vector3.forward;
            f = f.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(f, up);
        }
    }
}
