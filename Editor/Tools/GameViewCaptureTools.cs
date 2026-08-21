using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// GameView and scene-Camera capture — the "what the player sees" half of the capture family.
    ///
    /// Why this exists: every capture tool before it looked through the editor's own SceneView camera,
    /// which is an authoring viewport, not the game. A Screen Space-Overlay Canvas, a letterboxed
    /// aspect ratio, a post-processing volume, a camera-culling mask and anything that only exists
    /// while Play mode is running are all invisible from there. GestureManagerEnterPlayMode could put
    /// the editor into Play mode but nothing could then look at the result.
    ///
    /// Both tools here take the RENDER route (a camera drawn into a RenderTexture) whenever possible:
    /// free resolution, no focus stealing, no other application photographed by accident.
    /// <see cref="CaptureGameView"/> falls back to the WINDOW route (Win32 PrintWindow of the Game
    /// pane) only when the internal play-mode renderer cannot be resolved, and says so in the result —
    /// the two routes return genuinely different pictures and the reader has to know which one arrived.
    /// </summary>
    public static class GameViewCaptureTools
    {
        // Unity's own name for the off-screen play-mode render, and the type that declares it.
        //
        // Verified by reading the assembly metadata of the installed editors: in BOTH 2022.3.22f1 and
        // 6000.5.2f1 the only declarations of this name anywhere in UnityEditor.CoreModule.dll /
        // UnityEditor.dll are
        //     internal static void UnityEditor.EditorGUIUtility.RenderPlayModeViewCamerasInternal(
        //         RenderTexture target, int targetDisplay, Vector2 mousePosition, bool gizmos, bool renderIMGUI)
        // and its *_Injected binding stub. UnityEditor.Handles does NOT declare it; an earlier revision of
        // this file searched Handles, found nothing on any Unity version, and therefore demoted every single
        // capture to the window route (losing width/height, includeGizmos, and any capture at all with no
        // Game view open or on a non-Windows editor). The method is internal, so its SIGNATURE is still
        // discovered at runtime rather than assumed — see ResolveRenderBinding.
        private const string RenderMethodName = "RenderPlayModeViewCamerasInternal";

        // Every candidate is matched on this prefix, not on the exact name, so a version that renames the
        // suffix (…Internal2, …) is still found and then validated by signature.
        private const string RenderMethodPrefix = "RenderPlayModeViewCameras";

        // Used when the Game view's own target size cannot be read. 720p is a real 16:9 resolution, so a
        // capture taken with it is still usable; the result always states that the size was a fallback.
        private const int FallbackWidth = 1280;
        private const int FallbackHeight = 720;

        // Unity's maximum RenderTexture dimension. Asking for more fails inside the graphics driver with
        // a message that says nothing about which argument was wrong.
        private const int MaxDimension = 16384;

        // ─────────────────────────────────────────────────────────────────────────
        // CaptureGameView
        // ─────────────────────────────────────────────────────────────────────────

        [AgentTool(@"Capture the GAME view — the image the player sees through the scene's cameras,
including Screen Space-Overlay Canvases, camera culling and post-processing. Works in Edit AND Play mode.

This is NOT CaptureSceneView. The SceneView is the editor's authoring viewport (its own free camera,
grid, gizmos, selection outline); the GameView is the camera stack the game actually renders. A uGUI
Canvas that is off-screen, a wrong aspect ratio, a camera whose culling mask drops a layer, a
post-processing volume that washes out the scene — all of those are visible only here.

width/height: render resolution. 0/0 (the default) asks the open Game view for its current target size
  so the aspect matches what the user is looking at. Pass BOTH or NEITHER: deriving one from the other
  would need an assumed aspect ratio, and a silently wrong aspect is one of the bugs this tool is used
  to find. Pass an explicit size when two captures must be comparable (DiffImages requires identical
  dimensions, and the Game view's size changes whenever the user resizes the window).
includeGizmos: also draw the Game view gizmo layer (colliders, camera frustums, OnDrawGizmos).
  Default false = the clean player-facing frame.
maxWidth>0 downscales the LONGER side, aspect preserved. The ATTACHED image is the downscaled one, so
  take pixel coordinates from the 'output WxH' figure in the result, never from width/height.
format='png' (lossless, default) or 'jpg' (smaller, lossy via jpgQuality 1-100, default 90).
saveToPath: optional extra copy at an explicit path. Every capture is also dumped to
  %TEMP%/unity-agent-captures/ under a NEW name each time (newest 20 kept), so a before/after pair can
  both be re-read with the Read tool; use saveToPath for anything that must outlive those 20.
cropRegion='x,y,w,h' in pixels, origin BOTTOM-LEFT — the same convention as DiffImages.maskRegion, and
  deliberately NOT the top-left 'region' of CaptureEditorWindow. Out of range is an error, never a
  silent clamp, because a clamped crop covers a different area than the one you asked about.

TWO ROUTES. The result always states which one ran, as route=render or route=window:
  route=render (preferred) — the play-mode camera stack rendered off-screen through Unity's internal
    play-mode-view renderer. Any resolution, no focus stolen, works even with no Game view open.
  route=window (fallback) — used only when that internal method cannot be resolved in this Unity
    version. The Game PANE is cut out of a focus-free PrintWindow shot of the Unity window, so
    width/height do not apply (you get the pane's on-screen size), includeGizmos does not apply (the
    Game view's own Gizmos toggle decides) and cropRegion is measured inside the pane. This route needs
    the Game tab to be the FRONT tab of its dock: a background tab is not drawn anywhere, so it returns
    an error rather than the front tab's pixels labelled as the game. The result also states HOW the OS
    window was identified — a rect match against Unity's own ContainerWindow is reliable, a containment
    heuristic is not, and in the latter case a floating window lying over the Game view could have been
    photographed instead.

READ THE NOTES AT THE END OF THE RESULT before drawing conclusions from the image:
  - Edit mode: the render is real, but no scripts have run. You are looking at the scene's initial
    state, not gameplay. Enter Play mode first if you need runtime state.
  - No Game view open: route=render still produces an image, but it is one nobody is looking at.
  - No enabled camera on display 0: the frame is nothing but the clear colour. The tool says so rather
    than handing back a flat image that reads like a working capture.",
            Author = "ajisaiflow", Category = "GameViewCapture", Risk = ToolRisk.Safe)]
        public static string CaptureGameView(
            int width = 0,
            int height = 0,
            bool includeGizmos = false,
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "",
            string cropRegion = "")
        {
            if ((width == 0) != (height == 0))
                return "Error: width and height must be given together (or both left at 0 to use the Game " +
                       $"view's own size). Got width={width}, height={height} — filling in the missing one " +
                       "would require guessing an aspect ratio.";
            if (width < 0 || height < 0)
                return $"Error: width and height cannot be negative (got {width}x{height}).";
            if (width > MaxDimension || height > MaxDimension)
                return $"Error: {width}x{height} exceeds the maximum RenderTexture dimension ({MaxDimension}).";

            // AntiAliasing is pinned to 1 rather than the shared default of 2: the play-mode render applies
            // the project's own quality settings, and forcing MSAA on the destination would resolve the
            // frame a second time (and is not a knob this tool exposes, so it could not be turned off).
            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath, cropRegion,
                                            background: "scene", antiAliasing: 1);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            int renderW = width, renderH = height;
            string sizeNote;
            if (renderW == 0)
            {
                if (TryGetPlayModeViewTargetSize(out int autoW, out int autoH, out string sizeError))
                {
                    renderW = autoW;
                    renderH = autoH;
                    sizeNote = $" Resolution {renderW}x{renderH} came from the Game view's own target size " +
                               "(PlayModeView.GetMainPlayModeViewTargetSize) — pass width/height explicitly if " +
                               "this capture has to be comparable with another one.";
                }
                else
                {
                    renderW = FallbackWidth;
                    renderH = FallbackHeight;
                    sizeNote = $" Resolution FELL BACK to {FallbackWidth}x{FallbackHeight}: the Game view's own " +
                               $"target size could not be read ({sizeError}). The aspect ratio here is 16:9 and " +
                               "may differ from what the user sees — pass width/height explicitly to be sure.";
                }
            }
            else
            {
                sizeNote = $" Resolution {renderW}x{renderH} was requested explicitly.";
            }

            // Try the render route first. Only MECHANISM failures (missing internal method, unmappable
            // signature, render target or read-back failure) fall through to the window route; an invalid
            // cropRegion or format is the caller's error and must surface as an error, not as a different
            // picture taken a different way.
            Texture2D tex = TryRenderGameView(renderW, renderH, includeGizmos, opt,
                                              out string mechanismFailure, out string renderWarning);
            if (tex != null)
            {
                // Probed before Finish, which takes ownership of the texture.
                string flatNote = DescribeFlatFrame(tex,
                    "the play-mode camera stack drew nothing — no camera reached display 0, or the internal " +
                    "renderer declined to draw with no Game view open");

                string message = CaptureCommon.Finish(tex, opt, "GameView", CaptureRoute.Render,
                                                      out string finishError, destroySource: true);
                if (message == null) return $"Error: {finishError}";
                return message + sizeNote + BuildRenderRouteNotes(includeGizmos) + flatNote +
                       (renderWarning ?? "");
            }

            AgentLogger.Warning(LogTag.Tool,
                "CaptureGameView: the internal play-mode render route is unavailable " +
                $"({mechanismFailure}); falling back to the window route.");

#if UNITY_EDITOR_WIN
            return CaptureGameViewViaWindow(opt, mechanismFailure, width, height, includeGizmos);
#else
            return "Error: the internal play-mode-view render route is unavailable in this editor " +
                   $"({mechanismFailure}), and the window fallback needs Win32 PrintWindow, which exists " +
                   "only on the Windows editor. No GameView capture is possible here. CaptureSceneView " +
                   "captures the authoring viewport instead — a different picture, not a substitute.";
#endif
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CaptureFromCamera
        // ─────────────────────────────────────────────────────────────────────────

        [AgentTool(@"Render one specific scene Camera into an image, at any resolution, without disturbing
the editor. Use this when the subject is defined by a camera that already exists — a cutscene camera, a
VRChat mirror or portal camera, a thumbnail camera, a second display camera — rather than by the
SceneView's current framing or by the main game camera.

cameraName: empty (default) = Camera.main, i.e. the enabled active camera tagged MainCamera. Otherwise
  matched against the scene cameras in this order: exact name, case-insensitive name, then
  case-insensitive substring. Contains a '/'? Then it is matched as a full hierarchy path
  (Parent/Child/Camera), which is how you disambiguate several cameras sharing a name — call ListCameras
  to get the exact paths. INACTIVE GameObjects are searched too (Resources.FindObjectsOfTypeAll, not
  GameObject.Find), because a disabled cutscene camera is exactly the thing you want to preview; cameras
  living inside prefab ASSETS are excluded, since an asset has no scene to render.
width/height (default 1024x1024): render resolution, and therefore the aspect ratio. The camera's aspect
  is overridden to width/height for the render — without that the framing would follow whatever viewport
  the camera was last drawn in and the same call would return different framing on different machines.
includeSkybox (default true): true leaves the camera's own clearFlags alone, so a camera that clears to a
  solid colour still will not draw a skybox — the result says so rather than pretending the flag worked.
  false forces SolidColor clearing, which removes the skybox from a Skybox-clearing camera.
background: 'scene' (default, camera's own clear settings) | 'transparent' | '#RRGGBB'.
  'transparent' forces SolidColor clearing with alpha 0 and REQUIRES format='png' (JPG has no alpha
  channel, so the combination is refused instead of silently flattened). It also overrides includeSkybox
  entirely — you cannot have both a skybox and an empty background.
  '#RRGGBB' forces SolidColor clearing with that colour and likewise overrides includeSkybox.
maxWidth>0 downscales the LONGER side, aspect preserved. Pixel coordinates must be read off the
  'output WxH' figure in the result, not off width/height.
format='png' (default) or 'jpg' (jpgQuality 1-100, default 90).
saveToPath: optional extra copy. Every capture is also dumped under a fresh name in
  %TEMP%/unity-agent-captures/ (newest 20 kept).
cropRegion='x,y,w,h' in pixels, origin BOTTOM-LEFT (same convention as DiffImages.maskRegion). Out of
  range is an error, not a silent clamp.
antiAliasing: MSAA samples, 1/2/4/8, default 2. Thin geometry (hair cards, wires, a UI outline) is
  unreadable at 1.

WHAT THIS DOES NOT SHOW: gizmos, the grid, selection outlines and editor overlays are not part of a
camera render — Camera.Render draws the scene, not the editor. Use CaptureSceneView for those.
Camera state (targetTexture, aspect, enabled, clearFlags, backgroundColor) is snapshotted and restored
in a finally block, so an exception mid-render cannot leave the camera pointing at a destroyed target.
The aspect override is cleared with Camera.ResetAspect afterwards; an explicit aspect override set by
another script is therefore not preserved, because Unity offers no way to read whether one existed.
A disabled camera component renders fine. If the camera's GameObject is INACTIVE the render is still
attempted and the result says so — treat that image as approximate, since nothing that depends on the
object being active (its scripts, its Animator) has run.
ONE CAMERA, ALONE: the render target is cleared before the camera draws, so a camera whose clearFlags is
Depth or Nothing — the normal setup for an overlay, UI, mirror or second-display camera — shows its own
geometry over an EMPTY background, not over the frame the camera beneath it would normally have drawn.
The result says so when that applies. Use CaptureGameView for the composited stack.",
            Author = "ajisaiflow", Category = "GameViewCapture", Risk = ToolRisk.Safe)]
        public static string CaptureFromCamera(
            string cameraName = "",
            int width = 1024,
            int height = 1024,
            bool includeSkybox = true,
            string background = "scene",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "",
            string cropRegion = "",
            int antiAliasing = 2)
        {
            if (width <= 0 || height <= 0)
                return $"Error: width and height must be positive (got {width}x{height}).";
            if (width > MaxDimension || height > MaxDimension)
                return $"Error: {width}x{height} exceeds the maximum RenderTexture dimension ({MaxDimension}).";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath, cropRegion,
                                            background, antiAliasing);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            // Validate() already accepted the background, so this cannot fail here; the values are what
            // decide the clear settings below.
            if (!CaptureCommon.TryParseBackground(opt.Background, out CaptureBackgroundMode bgMode,
                                                  out Color bgColor, out string bgError))
                return $"Error: {bgError}";

            var cameras = EnumerateSceneCameras(out int prefabAssetCameras, out int nonSceneCameras);
            if (!TryResolveCamera(cameraName, cameras, prefabAssetCameras, nonSceneCameras,
                                  out Camera cam, out string resolveNote, out string resolveError))
                return $"Error: {resolveError}";

            var notes = new StringBuilder();
            notes.Append(' ').Append(resolveNote);

            if (!cam.gameObject.activeInHierarchy)
                notes.Append(" NOTE: the camera's GameObject is INACTIVE (activeInHierarchy=false). " +
                             "Camera.Render was called anyway and this image is its output, but nothing that " +
                             "depends on the object being active has run, so treat the framing and any " +
                             "script-driven state as approximate.");
            if (!cam.enabled)
                notes.Append(" The Camera component is disabled (enabled=false), which does not affect an " +
                             "explicit Render call — it only means Unity does not draw this camera on its own.");

            var prevTarget = cam.targetTexture;
            bool prevEnabled = cam.enabled;
            var prevClearFlags = cam.clearFlags;
            var prevBackground = cam.backgroundColor;

            if (prevTarget != null)
                notes.Append($" This camera already had targetTexture '{prevTarget.name}' assigned; it was " +
                             "temporarily redirected to the capture target and restored afterwards.");

            bool keepAlpha = bgMode == CaptureBackgroundMode.Transparent;

            // Resolved before the try: the catch below must not touch the Camera again, because an exception
            // raised by a destroyed camera would make HierarchyPath throw a second time from inside the
            // handler and escape as an unrelated MissingReferenceException.
            string camPath = HierarchyPath(cam.gameObject);

            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                rt = CaptureCommon.GetTemporaryTarget(width, height, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";

                // The target comes out of RenderTexture's pool still holding an earlier frame, so it is
                // cleared to transparent black BEFORE the camera draws. A camera that clears only depth (or
                // nothing) would otherwise composite itself over the previous capture and report success.
                if (!TryClearTarget(rt, Color.clear, out string clearError))
                {
                    notes.Append($" WARNING: the capture target could not be cleared before rendering " +
                                 $"({clearError}). It is a POOLED RenderTexture, so any area this camera does " +
                                 "not draw over may still contain a previous capture's pixels — do not trust " +
                                 "the background of this image.");
                }

                switch (bgMode)
                {
                    case CaptureBackgroundMode.Transparent:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(prevBackground.r, prevBackground.g, prevBackground.b, 0f);
                        notes.Append(" background=transparent: clearFlags forced to SolidColor with alpha 0, so " +
                                     "includeSkybox was IGNORED (a skybox and an empty background are mutually " +
                                     "exclusive). If the PNG comes back opaque anyway, the render pipeline " +
                                     "overwrote alpha — URP/HDRP post-processing commonly does.");
                        break;
                    case CaptureBackgroundMode.SolidColor:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(bgColor.r, bgColor.g, bgColor.b, 1f);
                        notes.Append($" background={opt.Background}: clearFlags forced to SolidColor, so " +
                                     "includeSkybox was IGNORED.");
                        break;
                    default:
                        if (!includeSkybox && prevClearFlags == CameraClearFlags.Skybox)
                        {
                            cam.clearFlags = CameraClearFlags.SolidColor;
                            notes.Append(" includeSkybox=false: clearFlags switched from Skybox to SolidColor for " +
                                         $"this render, clearing to the camera's own background colour " +
                                         $"#{ColorUtility.ToHtmlStringRGB(prevBackground)}.");
                        }
                        else if (includeSkybox && prevClearFlags != CameraClearFlags.Skybox)
                        {
                            notes.Append($" includeSkybox=true had NO effect: this camera clears with " +
                                         $"{prevClearFlags}, not Skybox, so no skybox is drawn. That is the " +
                                         "camera's own setting, not a failure of this capture.");
                        }

                        // Depth / Nothing means the camera does not touch the colour buffer at all. In the
                        // editor that camera is normally drawn ON TOP of another camera's output; here it is
                        // rendered alone into a cleared target, so what surrounds its geometry is the clear,
                        // not the scene the user sees behind it in the Game view.
                        if (prevClearFlags == CameraClearFlags.Depth || prevClearFlags == CameraClearFlags.Nothing)
                        {
                            notes.Append($" NOTE: this camera's clearFlags is {prevClearFlags}, so it does NOT " +
                                         "clear the colour buffer. Only the geometry it draws is real: the " +
                                         "background here is the capture target's own clear (transparent black, " +
                                         "which encodes as black unless background='transparent'), NOT whatever " +
                                         "camera normally renders underneath it. Use CaptureGameView to see this " +
                                         "camera composited over the rest of the stack.");
                        }
                        break;
                }

                cam.aspect = (float)width / height;
                cam.targetTexture = rt;
                cam.Render();

                tex = CaptureCommon.ReadBack(rt, keepAlpha, out string readError);
                if (tex == null) return $"Error: {readError}";

                // Probed before Finish takes ownership of the texture.
                notes.Append(DescribeFlatFrame(tex,
                    "this camera drew nothing — check where it is pointing, its culling mask, and its near / " +
                    "far clip planes"));

                string message = CaptureCommon.Finish(tex, opt, $"Camera '{camPath}'", CaptureRoute.Render,
                                                      out string finishError, destroySource: true);
                tex = null;   // Finish owns it now, including on its own failure path.
                if (message == null) return $"Error: {finishError}";
                return message + notes.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: rendering camera '{camPath}' failed: {ex.Message}";
            }
            finally
            {
                // Restored unconditionally and in this order: the target first, so nothing can observe the
                // camera still pointing at a RenderTexture that is about to go back to the pool.
                cam.targetTexture = prevTarget;
                cam.enabled = prevEnabled;
                cam.clearFlags = prevClearFlags;
                cam.backgroundColor = prevBackground;
                cam.ResetAspect();

                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CaptureFromPose
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Default near plane: VRChat's range, not Unity's 0.3. See the CaptureFromPose docstring.</summary>
        private const float DefaultPoseNear = 0.01f;

        /// <summary>Default far plane.</summary>
        private const float DefaultPoseFar = 1000f;

        /// <summary>
        /// Smallest far/near ratio worth mentioning. The value is derived from this tool's OWN defaults on
        /// purpose: a note that also fires at <see cref="DefaultPoseNear"/> / <see cref="DefaultPoseFar"/>
        /// fires on every single call, and a note that is always there is a note nobody reads. The 1.05
        /// leaves slack for the float error in 1000f/0.01f, which lands a hair above 100000.
        /// </summary>
        private const float DepthRatioNoteFloor = (DefaultPoseFar / DefaultPoseNear) * 1.05f;

        /// <summary>
        /// Width of the divider drawn between the two eyes of a stereo pair.
        ///
        /// The pre-flight size check and <see cref="ComposeSideBySide"/> MUST agree on this number. A check
        /// that forgets the gutter lets through a width whose composite is then too large, and the caller
        /// gets Unity's "invalid parameters" instead of the size advice the check exists to give.
        /// </summary>
        private static int StereoGutter(int eyeWidth) => Mathf.Max(2, eyeWidth / 128);

        /// <summary>Width of the finished side-by-side pair for a given per-eye width.</summary>
        private static int StereoCompositeWidth(int eyeWidth) => eyeWidth * 2 + StereoGutter(eyeWidth);

        /// <summary>
        /// Largest per-eye width whose composite still fits. Searched rather than solved so it stays correct
        /// if the gutter ever stops being a simple fraction of the width.
        /// </summary>
        private static int MaxStereoEyeWidth()
        {
            int w = MaxDimension / 2;
            while (w > 1 && StereoCompositeWidth(w) > MaxDimension) w--;
            return w;
        }

        [AgentTool(@"Put a THROW-AWAY camera at an exact world pose and render one frame from it — the only
capture tool that looks at the scene from INSIDE the subject instead of orbiting it from outside.

Use it when the picture you need is 'what does this point in space see': an avatar's eye position, a
mirror's viewpoint, the inside of a helmet, a camera prop that does not exist yet. The alternatives cannot
do this — CaptureFromCamera needs a Camera that is already in the scene, CaptureMultiAngle and
CaptureMeshIsolated always circle the subject, CaptureSceneView returns the editor viewport.

near IS THE POINT OF THIS TOOL. VRChat runs a near clip of roughly 0.01-0.05, Unity's camera default is
0.3, and a whole class of shader bugs (anything keyed off _ProjectionParams.y, near-plane clipping,
depth precision) NEVER FIRES at 0.3. The default here is therefore near=0.01, NOT Unity's 0.3.

WHERE — exactly one of:
  position='x,y,z' (world space), or
  positionFromBone='Head' plus optional avatarName / offset='0,0.07,0.02'.
  positionFromBone takes a humanoid bone name (Head, LeftEye, ...) resolved through the avatar's Animator
  when the avatar is humanoid, otherwise a GameObject name or hierarchy path. avatarName limits the search
  to one avatar; without it the name is resolved scene-wide and the result warns when it was ambiguous.
  offsetInBoneSpace=true (default) rotates offset into the BONE's own axes; false adds it in world axes.

WHERE IT LOOKS — at most one of rotation='x,y,z' (world euler) or lookAt='x,y,z' (world point to aim at).
With NEITHER the camera keeps world identity, i.e. it looks along world +Z. That is not the bone's forward
and not the avatar's forward; it merely coincides for an avatar standing unrotated facing +Z. Pass lookAt
when the aim matters.

stereoSeparation>0 renders TWO frames, offset half the separation each way along the camera's right axis,
and returns them side by side in ONE image labelled L and R (0.065 is the usual human IPD). This is for
chasing 'it is broken in one eye only'. It is a pair of mono renders, NOT a real VR stereo render: nothing
the XR pipeline does per eye (single-pass instancing, per-eye projection matrices, per-eye post) is
reproduced, so a bug that lives in that machinery will not show up here.

cullingMask: '' (default) renders every layer. 'Default,UI' or '0,5' renders only those. Prefix ~ to
invert, so '~Water' is everything except Water. An unknown layer name is an error, not a dropped layer.

fov / near / far are handed to Unity as given and READ BACK: if Unity clamps one, the result says so
instead of describing a picture taken with different values. A far/near ratio well past the default
0.01/1000 costs depth precision and is reported — distant z-fighting in that image is the ratio, not a
capture bug. The default pairing itself is not reported: it is the ratio this tool chose for you, and a
note on every single capture is a note nobody reads.

The camera is created for this call and DestroyImmediate'd in a finally, on a HideAndDontSave GameObject.
It is never saved, never selected, never left behind, and the scene is NOT marked dirty. No existing
camera is touched, so an interrupted call cannot leave one of yours mis-configured.

Route is always render: gizmos, the grid, selection outlines and editor overlays are NOT in this image
(Camera.Render draws the scene, not the editor). Neither is per-camera post-processing — this is a bare
Camera with no PostProcessLayer / Volume components of its own, so a scene camera's grading, bloom and AA
stack is absent by design. Use CaptureFromCamera when you need that stack.",
            Author = "ajisaiflow", Category = "GameViewCapture", Risk = ToolRisk.Safe)]
        public static string CaptureFromPose(
            string position = "",
            string rotation = "",
            string lookAt = "",
            string positionFromBone = "",
            string avatarName = "",
            string offset = "",
            bool offsetInBoneSpace = true,
            float fov = 60f,
            float near = DefaultPoseNear,
            float far = DefaultPoseFar,
            float stereoSeparation = 0f,
            string cullingMask = "",
            int width = 1024,
            int height = 1024,
            bool includeSkybox = true,
            string background = "scene",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "",
            string cropRegion = "",
            int antiAliasing = 2)
        {
            if (width <= 0 || height <= 0)
                return $"Error: width and height must be positive (got {width}x{height}).";
            if (width > MaxDimension || height > MaxDimension)
                return $"Error: {width}x{height} exceeds the maximum RenderTexture dimension ({MaxDimension}).";
            if (fov <= 0f || fov >= 180f)
                return $"Error: fov must be greater than 0 and less than 180 (got {F(fov)}).";
            if (near <= 0f)
                return $"Error: near must be greater than 0 (got {F(near)}); a near plane at or behind the " +
                       "camera has no projection.";
            if (far <= near)
                return $"Error: far ({F(far)}) must be greater than near ({F(near)}).";
            if (stereoSeparation < 0f)
                return $"Error: stereoSeparation must not be negative (got {F(stereoSeparation)}). Use 0 for a " +
                       "single frame, or a positive distance such as 0.065 for a left/right pair.";

            // The pair is composed into ONE Texture2D, so the pair — not the eye — is what has to fit. The
            // divider between the eyes counts: leaving it out passes a width that fails later inside the
            // composer, where Unity can only say "invalid parameters".
            if (stereoSeparation > 0f && StereoCompositeWidth(width) > MaxDimension)
                return $"Error: stereoSeparation>0 pairs two {width}-wide frames side by side with a " +
                       $"{StereoGutter(width)}px divider between them, {StereoCompositeWidth(width)}px in " +
                       $"total, which exceeds the maximum texture dimension ({MaxDimension}). Use width <= " +
                       $"{MaxStereoEyeWidth()} for a stereo pair.";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath, cropRegion,
                                            background, antiAliasing);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            if (!CaptureCommon.TryParseBackground(opt.Background, out CaptureBackgroundMode bgMode,
                                                  out Color bgColor, out string bgError))
                return $"Error: {bgError}";

            var notes = new StringBuilder();

            if (!TryResolvePosePosition(position, positionFromBone, avatarName, offset, offsetInBoneSpace,
                                        notes, out Vector3 camPos, out string whereLabel, out string posError))
                return $"Error: {posError}";

            if (!TryResolvePoseRotation(rotation, lookAt, camPos, notes,
                                        out Quaternion camRot, out string rotError))
                return $"Error: {rotError}";

            if (!TryParseCullingMask(cullingMask, out int mask, out string maskLabel, out string maskError))
                return $"Error: {maskError}";

            bool stereo = stereoSeparation > 0f;
            bool keepAlpha = bgMode == CaptureBackgroundMode.Transparent;

            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D left = null;
            Texture2D right = null;
            Texture2D composite = null;
            try
            {
                // HideAndDontSave, not a scene object the user has to clean up: the GameObject still belongs
                // to the active scene (that is what makes it renderable) but is never serialised, never shows
                // up in the Hierarchy, and cannot be left behind by a save. An explicit Camera.Render draws it
                // regardless of Camera.enabled, so the component stays disabled and Unity never draws this
                // camera on its own into the Game view.
                camGo = new GameObject("__PoseCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = false;
                cam.fieldOfView = fov;
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
                cam.aspect = (float)width / height;
                cam.cullingMask = mask;
                cam.transform.SetPositionAndRotation(camPos, camRot);

                // Unity clamps these silently. Reporting the value it actually used keeps the reader from
                // concluding that a near-plane bug does not reproduce when the near plane was never applied.
                notes.Append(DescribeClamp("fov", fov, cam.fieldOfView));
                notes.Append(DescribeClamp("near", near, cam.nearClipPlane));
                notes.Append(DescribeClamp("far", far, cam.farClipPlane));

                float ratio = cam.nearClipPlane > 0f ? cam.farClipPlane / cam.nearClipPlane : 0f;
                if (ratio > DepthRatioNoteFloor)
                    notes.Append($" NOTE: far/near is {F(ratio)} (near {F(cam.nearClipPlane)}, far " +
                                 $"{F(cam.farClipPlane)}), well past the {F(DefaultPoseFar / DefaultPoseNear)} " +
                                 "this tool defaults to. The depth buffer loses precision at that ratio and " +
                                 "distant surfaces z-fight — lower far if the background flickers. That is the " +
                                 "ratio, not a fault in this capture.");

                switch (bgMode)
                {
                    case CaptureBackgroundMode.Transparent:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                        notes.Append(" background=transparent: clearFlags SolidColor with alpha 0, so " +
                                     "includeSkybox was IGNORED. If the PNG comes back opaque the render " +
                                     "pipeline overwrote alpha — URP/HDRP post-processing commonly does.");
                        break;
                    case CaptureBackgroundMode.SolidColor:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(bgColor.r, bgColor.g, bgColor.b, 1f);
                        notes.Append($" background={opt.Background}: clearFlags SolidColor, so includeSkybox " +
                                     "was IGNORED.");
                        break;
                    default:
                        if (includeSkybox)
                        {
                            cam.clearFlags = CameraClearFlags.Skybox;
                            if (RenderSettings.skybox == null)
                                notes.Append(" includeSkybox=true but the scene has no skybox material " +
                                             "(RenderSettings.skybox is null), so the background is the " +
                                             "camera's clear colour, not a sky.");
                        }
                        else
                        {
                            cam.clearFlags = CameraClearFlags.SolidColor;
                            notes.Append(" includeSkybox=false: clearFlags SolidColor, clearing to the fresh " +
                                         "camera's own default background colour.");
                        }
                        break;
                }

                rt = CaptureCommon.GetTemporaryTarget(width, height, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";

                if (!stereo)
                {
                    left = RenderOnce(cam, rt, keepAlpha, notes, out string renderError);
                    if (left == null) return $"Error: {renderError}";

                    notes.Append(DescribeFlatFrame(left,
                        "the camera drew nothing from here — check the pose, the culling mask, and whether " +
                        "the near plane is already past the geometry you expected to see"));

                    string singleMsg = CaptureCommon.Finish(left, opt, $"Pose {whereLabel}", CaptureRoute.Render,
                                                            out string singleFinishError, destroySource: true);
                    left = null;   // Finish owns it now, including on its own failure path.
                    if (singleMsg == null) return $"Error: {singleFinishError}";
                    return singleMsg + DescribePose(camPos, camRot, cam, maskLabel) + notes.ToString();
                }

                // Both eyes are offset from the SAME centre pose, so the pair is symmetric about the point the
                // caller asked for rather than starting at it — position='eye centre' stays the eye centre.
                Vector3 rightAxis = camRot * Vector3.right;
                float half = stereoSeparation * 0.5f;

                cam.transform.position = camPos - rightAxis * half;
                left = RenderOnce(cam, rt, keepAlpha, notes, out string leftError);
                if (left == null) return $"Error: left eye: {leftError}";

                cam.transform.position = camPos + rightAxis * half;
                right = RenderOnce(cam, rt, keepAlpha, notes, out string rightError);
                if (right == null) return $"Error: right eye: {rightError}";

                notes.Append(DescribeFlatFrame(left,
                    "the LEFT eye drew nothing — check the pose, the culling mask, and the near plane"));
                notes.Append(DescribeFlatFrame(right,
                    "the RIGHT eye drew nothing — check the pose, the culling mask, and the near plane"));

                composite = ComposeSideBySide(left, right, keepAlpha, out int labelsDrawn,
                                              out string composeError);
                if (composite == null) return $"Error: {composeError}";

                string msg = CaptureCommon.Finish(composite, opt, $"Stereo pair from pose {whereLabel}",
                                                  CaptureRoute.Render, out string finishError,
                                                  destroySource: true);
                composite = null;   // Finish owns it now.
                if (msg == null) return $"Error: {finishError}";

                string labelNote = labelsDrawn == 2
                    ? ""
                    : $" NOTE: only {labelsDrawn} of the 2 eye labels could be burned into the image — the " +
                      "LEFT eye is the left half regardless.";
                return msg + $" Stereo pair: left half = LEFT eye, right half = RIGHT eye, separation " +
                       $"{F(stereoSeparation)}m about the given position." + labelNote +
                       DescribePose(camPos, camRot, cam, maskLabel) + notes.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: rendering from pose {whereLabel} failed: {ex.Message}";
            }
            finally
            {
                if (left != null) UnityEngine.Object.DestroyImmediate(left);
                if (right != null) UnityEngine.Object.DestroyImmediate(right);
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                // Last, and unconditionally: the camera must not outlive this call even if the render threw.
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>
        /// One clear + render + read-back into a texture the caller owns.
        ///
        /// The clear is not optional: GetTemporaryTarget hands out pooled surfaces that still physically hold
        /// the previous capture, and the second eye of a stereo pair would otherwise inherit the first eye's
        /// pixels wherever it draws nothing — the exact comparison the pair exists to make.
        /// </summary>
        private static Texture2D RenderOnce(Camera cam, RenderTexture rt, bool keepAlpha, StringBuilder notes,
                                            out string error)
        {
            error = null;
            if (!TryClearTarget(rt, Color.clear, out string clearError))
            {
                notes.Append($" WARNING: the capture target could not be cleared before rendering " +
                             $"({clearError}). It is a POOLED RenderTexture, so any area this camera did not " +
                             "draw over may still hold a previous capture's pixels.");
            }

            try
            {
                cam.targetTexture = rt;
                cam.Render();
            }
            catch (Exception ex)
            {
                error = $"Camera.Render failed: {ex.Message}";
                return null;
            }
            finally
            {
                cam.targetTexture = null;
            }

            var tex = CaptureCommon.ReadBack(rt, keepAlpha, out string readError);
            if (tex == null)
            {
                error = readError;
                return null;
            }
            return tex;
        }

        /// <summary>
        /// Resolves the camera position from either an explicit world vector or a bone, and hands back a short
        /// human label for the result line. Ambiguity is reported, never resolved silently — a 'Head' that
        /// matched three avatars would otherwise produce a perfectly plausible picture of the wrong one.
        /// </summary>
        private static bool TryResolvePosePosition(string position, string positionFromBone, string avatarName,
                                                   string offset, bool offsetInBoneSpace, StringBuilder notes,
                                                   out Vector3 camPos, out string whereLabel, out string error)
        {
            camPos = Vector3.zero;
            whereLabel = "";
            error = null;

            bool hasPosition = !string.IsNullOrWhiteSpace(position);
            bool hasBone = !string.IsNullOrWhiteSpace(positionFromBone);

            if (hasPosition && hasBone)
            {
                error = $"position ('{position}') and positionFromBone ('{positionFromBone}') were both given " +
                        "and they name different places. Pass exactly one.";
                return false;
            }
            if (!hasPosition && !hasBone)
            {
                error = "no camera position was given. Pass position='x,y,z' for a world-space point, or " +
                        "positionFromBone='Head' (optionally with avatarName and offset='0,0.07,0.02').";
                return false;
            }

            Vector3 offsetVec = Vector3.zero;
            bool hasOffset = !string.IsNullOrWhiteSpace(offset);
            if (hasOffset && !TryParseVector3(offset, "offset", out offsetVec, out error)) return false;

            if (hasPosition)
            {
                if (!TryParseVector3(position, "position", out camPos, out error)) return false;
                if (hasOffset)
                {
                    // World axes: there is no bone to borrow a frame from, so offsetInBoneSpace is meaningless
                    // here and saying so beats applying it in an invented space.
                    camPos += offsetVec;
                    notes.Append($" offset {V(offsetVec)} was added in WORLD axes (offsetInBoneSpace does not " +
                                 "apply to an explicit position — there is no bone to take axes from).");
                }
                whereLabel = V(camPos);
                return true;
            }

            if (!TryResolveBone(positionFromBone, avatarName, notes, out Transform bone, out error)) return false;

            camPos = bone.position;
            if (hasOffset)
            {
                camPos += offsetInBoneSpace ? bone.rotation * offsetVec : offsetVec;
                notes.Append($" offset {V(offsetVec)} was applied in " +
                             (offsetInBoneSpace ? "the BONE's axes" : "WORLD axes") + ".");
            }
            whereLabel = $"'{HierarchyPath(bone.gameObject)}' {V(camPos)}";
            return true;
        }

        /// <summary>
        /// Bone lookup with three routes, tried in this order: a humanoid bone through the Animator (the only
        /// route that survives an avatar whose bones carry non-standard names), an exact name match under the
        /// named avatar, then the repo-wide fuzzy FindGameObject when no avatar was named.
        /// </summary>
        private static bool TryResolveBone(string boneName, string avatarName, StringBuilder notes,
                                           out Transform bone, out string error)
        {
            bone = null;
            error = null;
            string wanted = boneName.Trim();

            GameObject root = null;
            if (!string.IsNullOrWhiteSpace(avatarName))
            {
                root = MeshAnalysisTools.FindGameObject(avatarName.Trim());
                if (root == null)
                {
                    error = $"avatarName '{avatarName}' matched no GameObject in the loaded scenes.";
                    return false;
                }
            }

            if (root != null)
            {
                var animator = root.GetComponent<Animator>() ?? root.GetComponentInChildren<Animator>(true);
                if (animator != null && animator.isHuman &&
                    Enum.TryParse(wanted, true, out HumanBodyBones humanBone) &&
                    humanBone != HumanBodyBones.LastBone)
                {
                    var t = animator.GetBoneTransform(humanBone);
                    if (t != null)
                    {
                        notes.Append($" Bone '{wanted}' was resolved as the humanoid {humanBone} of " +
                                     $"'{HierarchyPath(root)}'.");
                        bone = t;
                        return true;
                    }
                    notes.Append($" '{wanted}' is a humanoid bone name but this avatar's rig has no {humanBone} " +
                                 "mapped, so it was looked up by GameObject name instead.");
                }

                var matches = root.GetComponentsInChildren<Transform>(true)
                                  .Where(t => string.Equals(t.name, wanted, StringComparison.OrdinalIgnoreCase))
                                  .ToList();
                if (matches.Count == 0)
                {
                    error = $"positionFromBone '{wanted}' matched no transform under avatarName " +
                            $"'{HierarchyPath(root)}'. Pass a humanoid bone name (Head, LeftEye, ...) or an " +
                            "exact child name.";
                    return false;
                }
                if (matches.Count > 1)
                {
                    error = $"positionFromBone '{wanted}' matched {matches.Count} transforms under " +
                            $"'{HierarchyPath(root)}': " +
                            string.Join(", ", matches.Take(5).Select(t => HierarchyPath(t.gameObject))) +
                            (matches.Count > 5 ? ", ..." : "") +
                            ". Pass the full hierarchy path instead of the bare name.";
                    return false;
                }
                bone = matches[0];
                return true;
            }

            var found = MeshAnalysisTools.FindGameObject(wanted);
            if (found == null)
            {
                error = $"positionFromBone '{wanted}' matched no GameObject in the loaded scenes. Pass " +
                        "avatarName as well if the bone lives under an inactive avatar, or use a humanoid " +
                        "bone name (Head, LeftEye, ...) together with avatarName.";
                return false;
            }

            // FindGameObject returns the FIRST match; with two avatars in the scene that is a coin flip, and a
            // capture of the wrong head looks entirely successful. Count the namesakes and say so.
            int namesakes = 0;
            try
            {
                namesakes = UnityEngine.Object.FindObjectsOfType<Transform>(true)
                    .Count(t => string.Equals(t.name, found.name, StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool, $"CaptureFromPose: namesake count skipped ({ex.Message}).");
            }
            if (namesakes > 1)
            {
                notes.Append($" WARNING: {namesakes} transforms in the loaded scenes are called " +
                             $"'{found.name}' and '{HierarchyPath(found)}' is the one that was used. Pass " +
                             "avatarName to say which avatar you meant.");
            }

            bone = found.transform;
            return true;
        }

        private static bool TryResolvePoseRotation(string rotation, string lookAt, Vector3 camPos,
                                                   StringBuilder notes, out Quaternion camRot, out string error)
        {
            camRot = Quaternion.identity;
            error = null;

            bool hasRotation = !string.IsNullOrWhiteSpace(rotation);
            bool hasLookAt = !string.IsNullOrWhiteSpace(lookAt);

            if (hasRotation && hasLookAt)
            {
                error = $"rotation ('{rotation}') and lookAt ('{lookAt}') were both given and they aim the " +
                        "camera differently. Pass at most one.";
                return false;
            }

            if (hasRotation)
            {
                if (!TryParseVector3(rotation, "rotation", out Vector3 euler, out error)) return false;
                camRot = Quaternion.Euler(euler);
                return true;
            }

            if (hasLookAt)
            {
                if (!TryParseVector3(lookAt, "lookAt", out Vector3 target, out error)) return false;
                Vector3 dir = target - camPos;
                if (dir.sqrMagnitude < 1e-10f)
                {
                    error = $"lookAt {V(target)} is the camera position itself, so there is no direction to " +
                            "aim along.";
                    return false;
                }
                // Vector3.up fails only when the aim is exactly vertical; Quaternion.LookRotation returns
                // identity there, which would silently point the camera at the horizon instead of the target.
                Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.9999f
                    ? Vector3.forward
                    : Vector3.up;
                camRot = Quaternion.LookRotation(dir, up);
                return true;
            }

            notes.Append(" No rotation or lookAt was given, so the camera looks along WORLD +Z. That is not " +
                         "the bone's forward and not the avatar's forward — pass lookAt or rotation if the " +
                         "aim matters.");
            return true;
        }

        /// <summary>
        /// '' = every layer, 'Default,UI' or '0,5' = only those, '~Water' = everything except those.
        /// An unrecognised layer is an error listing the layers this project actually defines: a dropped
        /// layer would come back as a picture that is simply missing objects, with nothing to explain why.
        /// </summary>
        private static bool TryParseCullingMask(string cullingMask, out int mask, out string label,
                                                out string error)
        {
            mask = ~0;
            label = "every layer";
            error = null;

            string spec = (cullingMask ?? "").Trim();
            if (spec.Length == 0) return true;

            bool invert = spec.StartsWith("~", StringComparison.Ordinal);
            if (invert) spec = spec.Substring(1).Trim();
            if (spec.Length == 0)
            {
                error = "cullingMask '~' names no layer to exclude.";
                return false;
            }

            int bits = 0;
            var named = new List<string>();
            foreach (string raw in spec.Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;

                int index;
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                {
                    if (index < 0 || index > 31)
                    {
                        error = $"cullingMask layer index {index} is out of range — Unity has layers 0-31.";
                        return false;
                    }
                }
                else
                {
                    index = LayerMask.NameToLayer(token);
                    if (index < 0)
                    {
                        error = $"cullingMask names layer '{token}', which this project does not define. " +
                                $"Defined layers: {DescribeDefinedLayers()}.";
                        return false;
                    }
                }

                bits |= 1 << index;
                string name = LayerMask.LayerToName(index);
                named.Add(string.IsNullOrEmpty(name) ? index.ToString(CultureInfo.InvariantCulture)
                                                     : $"{name}({index})");
            }

            if (bits == 0)
            {
                error = $"cullingMask '{cullingMask}' resolved to no layer at all, which would render an empty " +
                        "frame. Leave it empty to render every layer.";
                return false;
            }

            mask = invert ? ~bits : bits;
            label = invert ? $"every layer except {string.Join(", ", named)}"
                           : $"only {string.Join(", ", named)}";
            return true;
        }

        private static string DescribeDefinedLayers()
        {
            var defined = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name)) defined.Add($"{name}({i})");
            }
            return defined.Count == 0 ? "(none)" : string.Join(", ", defined);
        }

        /// <summary>
        /// Says so when Unity did not accept a clip plane or field of view verbatim. Silence here would let a
        /// near-plane investigation conclude 'not reproducible' from a frame rendered at a different near plane.
        /// </summary>
        private static string DescribeClamp(string name, float requested, float applied)
        {
            if (Mathf.Abs(requested - applied) <= Mathf.Max(1e-6f, Mathf.Abs(requested) * 1e-5f)) return "";
            return $" NOTE: {name}={F(requested)} was CLAMPED by Unity to {F(applied)}; this image was " +
                   $"rendered with {F(applied)}, not {F(requested)}.";
        }

        private static string DescribePose(Vector3 pos, Quaternion rot, Camera cam, string maskLabel)
        {
            return $" Camera at {V(pos)}, euler {V(rot.eulerAngles)}, forward {V(rot * Vector3.forward)}, " +
                   $"fov {F(cam.fieldOfView)}, near {F(cam.nearClipPlane)}, far {F(cam.farClipPlane)}, " +
                   $"rendering {maskLabel}.";
        }

        /// <summary>
        /// Left and right frames on one plate, separated by a thin gutter so the seam is visible, with the eye
        /// burned into each half. Two attachments would let the reader mix the eyes up; one image cannot.
        /// The caller owns the result.
        /// </summary>
        private static Texture2D ComposeSideBySide(Texture2D left, Texture2D right, bool keepAlpha,
                                                   out int labelsDrawn, out string error)
        {
            error = null;
            labelsDrawn = 0;
            if (left == null || right == null)
            {
                error = "one of the two eye frames is missing.";
                return null;
            }
            if (left.width != right.width || left.height != right.height)
            {
                error = $"the eye frames differ in size ({left.width}x{left.height} vs " +
                        $"{right.width}x{right.height}) and cannot be paired.";
                return null;
            }

            int gutter = StereoGutter(left.width);
            int w = StereoCompositeWidth(left.width);
            int h = left.height;
            Texture2D composite = null;
            try
            {
                composite = new Texture2D(w, h, keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);

                var plate = new Color(0.15f, 0.15f, 0.15f, keepAlpha ? 0f : 1f);
                var bg = new Color[w * h];
                for (int i = 0; i < bg.Length; i++) bg[i] = plate;
                composite.SetPixels(bg);

                composite.SetPixels(0, 0, left.width, h, left.GetPixels());
                composite.SetPixels(left.width + gutter, 0, right.width, h, right.GetPixels());

                int scale = Mathf.Clamp(left.width / 48, 2, 10);
                int margin = Mathf.Max(3, scale);
                if (CaptureCommon.DrawTextWithBackground(composite, margin, margin, "L", scale, apply: false))
                    labelsDrawn++;
                if (CaptureCommon.DrawTextWithBackground(composite, left.width + gutter + margin, margin, "R",
                                                         scale, apply: false))
                    labelsDrawn++;

                composite.Apply(false, false);
                return composite;
            }
            catch (Exception ex)
            {
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
                error = $"the stereo pair could not be composed: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// 'x,y,z' with invariant-culture floats. Rejects anything else by name, because a vector that parsed
        /// to zero would put the camera at the world origin and return a picture of somewhere else entirely.
        /// </summary>
        private static bool TryParseVector3(string s, string argName, out Vector3 v, out string error)
        {
            v = Vector3.zero;
            error = null;

            var parts = (s ?? "").Split(',');
            if (parts.Length != 3)
            {
                error = $"{argName}='{s}' is not a vector — it needs exactly three comma-separated numbers, " +
                        "as in '0,1.35,0.08'.";
                return false;
            }

            var values = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out values[i]))
                {
                    error = $"{argName}='{s}': component {i} ('{parts[i].Trim()}') is not a number.";
                    return false;
                }
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    error = $"{argName}='{s}': component {i} is not finite.";
                    return false;
                }
            }

            v = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        private static string V(Vector3 v)
            => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

        private static string F(float value)
            => value.ToString("0.####", CultureInfo.InvariantCulture);

        // ─────────────────────────────────────────────────────────────────────────
        // ListCameras
        // ─────────────────────────────────────────────────────────────────────────

        [AgentTool(@"List every Camera in the loaded scenes, in the order they draw, with the state that
decides what each one actually renders.

Call this before CaptureFromCamera (to get an exact name or hierarchy path) and whenever CaptureGameView
comes back looking wrong — an empty or flat GameView is almost always one of the facts on this list:
no camera is enabled, the camera's GameObject is inactive, it renders into a targetTexture instead of the
screen, or it is assigned to a display other than 0.

Per camera: index, name, hierarchy path, scene, enabled, activeInHierarchy, depth, targetDisplay,
clearFlags, fieldOfView, orthographic (plus orthographicSize when it is), whether a targetTexture is
assigned, and whether it is Camera.main.

Sorted by DEPTH ASCENDING, which is the order Unity draws them: a lower depth draws first and a higher
depth draws on top. Two cameras with the same depth have no defined order between them.

INACTIVE GameObjects are included — a disabled camera is a normal thing to look for and hiding it would
make the list read as 'this camera does not exist'. Cameras inside prefab ASSETS and editor-internal
cameras (the SceneView camera, preview cameras) are excluded, because neither belongs to a scene you can
render; the count of each exclusion is reported so the list is never silently short. Cameras opened in
Prefab Mode DO appear, because a prefab stage is a real (preview) scene.",
            Author = "ajisaiflow", Category = "GameViewCapture", Risk = ToolRisk.Safe)]
        public static string ListCameras()
        {
            var cameras = EnumerateSceneCameras(out int prefabAssetCameras, out int nonSceneCameras);
            string exclusions = DescribeExclusions(prefabAssetCameras, nonSceneCameras);

            if (cameras.Count == 0)
            {
                return "No Camera exists in any loaded scene." + exclusions +
                       " CaptureFromCamera has nothing to render until a Camera exists, and CaptureGameView " +
                       "would return nothing but a clear colour. CaptureSceneView captures the editor's own " +
                       "SceneView camera instead, which is always available.";
            }

            var main = Camera.main;
            var sb = new StringBuilder();
            sb.AppendLine($"Cameras: {cameras.Count} found, sorted by depth ascending (lower depth draws first, " +
                          "higher depth draws on top).");
            sb.AppendLine("---");

            for (int i = 0; i < cameras.Count; i++)
            {
                var cam = cameras[i];
                var go = cam.gameObject;
                string sceneName = go.scene.IsValid()
                    ? (string.IsNullOrEmpty(go.scene.name) ? "(unnamed scene)" : go.scene.name)
                    : "unknown";
                string rtInfo = cam.targetTexture != null
                    ? $"targetTexture='{cam.targetTexture.name}' ({cam.targetTexture.width}x{cam.targetTexture.height}) " +
                      "→ renders THERE, not into the GameView"
                    : "targetTexture=none";
                string projection = cam.orthographic
                    ? $"orthographic=true orthographicSize={cam.orthographicSize.ToString("0.###", CultureInfo.InvariantCulture)}"
                    : "orthographic=false";
                string hidden = go.hideFlags != HideFlags.None ? $" hideFlags={go.hideFlags}" : "";

                sb.AppendLine($"[{i}] \"{cam.name}\" depth={cam.depth.ToString("0.###", CultureInfo.InvariantCulture)}" +
                              $"{(main == cam ? " (Camera.main)" : "")}");
                sb.AppendLine($"    path={HierarchyPath(go)}  scene={sceneName}{hidden}");
                sb.AppendLine($"    enabled={Lower(cam.enabled)} activeInHierarchy={Lower(go.activeInHierarchy)} " +
                              $"targetDisplay={cam.targetDisplay} (Display {cam.targetDisplay + 1})");
                sb.AppendLine($"    clearFlags={cam.clearFlags} " +
                              $"fieldOfView={cam.fieldOfView.ToString("0.#", CultureInfo.InvariantCulture)} " +
                              $"{projection}");
                sb.AppendLine($"    {rtInfo}");
            }

            sb.AppendLine("---");
            sb.AppendLine(main != null
                ? $"Camera.main = \"{main.name}\" ({HierarchyPath(main.gameObject)})"
                : "Camera.main = null — no camera is simultaneously tagged 'MainCamera', enabled and active. " +
                  "CaptureFromCamera with an empty cameraName cannot resolve a camera in this state; pass a " +
                  "name or path from the list above.");

            var drawing = cameras.Where(WouldDrawToGameView).ToList();
            sb.AppendLine(drawing.Count > 0
                ? "Drawn into the GameView (enabled, active, no targetTexture, targetDisplay=0), in draw order: " +
                  string.Join(", ", drawing.Select(c => $"\"{c.name}\""))
                : "NOTHING would be drawn into the GameView: no camera is enabled AND active AND free of a " +
                  "targetTexture AND on targetDisplay=0. CaptureGameView would return only the clear colour.");

            if (exclusions.Length > 0) sb.Append(exclusions.TrimStart());
            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Render route: Unity's internal play-mode-view renderer
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// What ResolveRenderBinding worked out about
        /// <c>EditorGUIUtility.RenderPlayModeViewCamerasInternal</c> in THIS Unity version. Cached for the
        /// lifetime of the domain: enumerating the static methods of several editor types is not free, and
        /// re-deriving the same answer would also re-log the same warning on every capture.
        /// </summary>
        private sealed class RenderBinding
        {
            public MethodInfo Method;
            /// <summary>One entry per parameter, in declaration order. Null when the signature is unusable.</summary>
            public ArgRole[] Roles;
            /// <summary>Human-readable signature, for the result message and the log.</summary>
            public string Signature = "unresolved";
            /// <summary>Null when the binding is usable; otherwise why it is not.</summary>
            public string Failure;
        }

        /// <summary>
        /// What each discovered parameter is fed. Deriving this from the parameter's TYPE AND NAME instead
        /// of assuming a fixed argument list is the whole point: the method is internal, its signature has
        /// changed across Unity versions, and a positional guess that happens to compile would render with
        /// gizmos and IMGUI silently swapped, or pass a display index where a bool belongs.
        /// </summary>
        private enum ArgRole
        {
            /// <summary>The destination RenderTexture.</summary>
            Target,
            /// <summary>Display index; 0 is Display 1, which is what the Game view shows.</summary>
            TargetDisplay,
            /// <summary>Mouse position handed to IMGUI; zero is correct for an unattended capture.</summary>
            MousePosition,
            /// <summary>The caller's includeGizmos.</summary>
            Gizmos,
            /// <summary>A flag we always want on: render IMGUI, clear the target.</summary>
            AlwaysTrue,
        }

        private static RenderBinding _renderBinding;

        private static RenderBinding ResolveRenderBinding()
        {
            if (_renderBinding != null) return _renderBinding;

            var binding = new RenderBinding();
            try
            {
                var hostTypes = RenderHostTypes(out string searchedTypes);

                // Types are searched in priority order (EditorGUIUtility first — that is where the method
                // really lives), and within a type the exact name comes before the …_Injected binding stub.
                var candidates = new List<MethodInfo>();
                foreach (var hostType in hostTypes)
                {
                    candidates.AddRange(hostType
                        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.FlattenHierarchy)
                        .Where(m => m.Name.StartsWith(RenderMethodPrefix, StringComparison.Ordinal))
                        .OrderBy(m => m.Name == RenderMethodName ? 0 : 1)
                        .ThenBy(m => m.Name.Length));
                }

                if (candidates.Count == 0)
                {
                    binding.Failure =
                        $"no static method whose name starts with '{RenderMethodPrefix}' exists on any of " +
                        $"{searchedTypes} in Unity {Application.unityVersion}";
                }
                else
                {
                    var rejected = new List<string>();
                    foreach (var candidate in candidates)
                    {
                        string signature = DescribeSignature(candidate);
                        if (TryMapParameters(candidate, out ArgRole[] roles, out string mapError))
                        {
                            binding.Method = candidate;
                            binding.Roles = roles;
                            binding.Signature = signature;
                            break;
                        }
                        rejected.Add($"{signature} — {mapError}");
                    }

                    if (binding.Method == null)
                    {
                        // Resolved the symbol but could not build a call for it. Deliberately NOT guessed:
                        // an argument list assembled by position would render something plausible-looking
                        // with the wrong flags, which is worse than falling back to the window route.
                        binding.Failure =
                            "the internal render method exists but its signature could not be mapped to " +
                            "arguments: " + string.Join(" | ", rejected);
                    }
                }
            }
            catch (Exception ex)
            {
                binding.Failure = "reflection for the internal play-mode render method failed: " + ex.Message;
            }

            if (binding.Failure != null)
                AgentLogger.Warning(LogTag.Tool, $"GameViewCaptureTools: {binding.Failure}.");
            else
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: resolved play-mode render as {binding.Signature}.");

            _renderBinding = binding;
            return binding;
        }

        /// <summary>
        /// The types that may declare the internal play-mode render, in priority order.
        ///
        /// <c>UnityEditor.EditorGUIUtility</c> is the real answer on every Unity version checked, and it is a
        /// PUBLIC type (only the method is internal), so it is named directly rather than looked up by string.
        /// The others are searched afterwards purely so that a future version which MOVES the method is
        /// found instead of silently disabling the whole render route — the previous revision of this file
        /// searched Handles alone and therefore never resolved the method at all. Types that do not exist on
        /// this version are skipped rather than failing the lookup.
        /// <paramref name="searchedTypes"/> lists what was actually searched, so the failure message names
        /// the places that were looked in instead of a single hard-coded type.
        /// </summary>
        private static List<Type> RenderHostTypes(out string searchedTypes)
        {
            var types = new List<Type> { typeof(EditorGUIUtility) };
            AddInternalEditorType(types, "UnityEditor.PlayModeView");
            AddInternalEditorType(types, "UnityEditor.GameView");
            types.Add(typeof(Handles));

            searchedTypes = string.Join(", ", types.Select(t => t.FullName));
            return types;
        }

        /// <summary>Appends an internal UnityEditor type by name, or logs and skips it when absent.</summary>
        private static void AddInternalEditorType(List<Type> types, string fullName)
        {
            try
            {
                var type = typeof(EditorWindow).Assembly.GetType(fullName);
                if (type != null && !types.Contains(type)) types.Add(type);
                else if (type == null)
                    AgentLogger.Debug(LogTag.Tool,
                        $"GameViewCaptureTools: {fullName} does not exist in Unity {Application.unityVersion}; " +
                        "it was skipped as a search location for the play-mode render method.");
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: looking up {fullName} failed ({ex.Message}); it was skipped as a " +
                    "search location for the play-mode render method.");
            }
        }

        private static bool TryMapParameters(MethodInfo method, out ArgRole[] roles, out string error)
        {
            roles = null;
            error = null;

            if (method.IsGenericMethodDefinition)
            {
                error = "it is generic";
                return false;
            }

            var parameters = method.GetParameters();
            var mapped = new ArgRole[parameters.Length];
            bool sawTarget = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.IsOut)
                {
                    error = $"parameter '{p.Name}' is an out parameter, which this call site cannot supply";
                    return false;
                }

                // The *_Injected binding stubs take the Vector2 by reference; Invoke handles that as long as
                // we look through the by-ref type when deciding what the parameter means.
                Type type = p.ParameterType;
                if (type.IsByRef) type = type.GetElementType();
                string name = (p.Name ?? "").ToLowerInvariant();

                if (type == typeof(RenderTexture))
                {
                    if (sawTarget)
                    {
                        // A second render target means this overload does something other than what we
                        // think it does (a separate depth target, a source and a destination). Feeding
                        // both the same texture would produce an image, which is exactly the kind of
                        // plausible-looking wrong result the window-route fallback exists to avoid.
                        error = $"it takes more than one RenderTexture (parameter {i} '{p.Name}'), so which " +
                                "one receives the capture is not knowable";
                        return false;
                    }
                    mapped[i] = ArgRole.Target;
                    sawTarget = true;
                }
                else if (type == typeof(Vector2))
                {
                    mapped[i] = ArgRole.MousePosition;
                }
                else if (type == typeof(int) && name.Contains("display"))
                {
                    mapped[i] = ArgRole.TargetDisplay;
                }
                else if (type == typeof(bool) && name.Contains("gizmo"))
                {
                    mapped[i] = ArgRole.Gizmos;
                }
                else if (type == typeof(bool) && (name.Contains("gui") || name.Contains("clear")))
                {
                    mapped[i] = ArgRole.AlwaysTrue;
                }
                else
                {
                    error = $"parameter {i} '{p.Name}' of type {type.Name} has no known meaning";
                    return false;
                }
            }

            if (!sawTarget)
            {
                error = "it takes no RenderTexture, so there is nowhere for the capture to land";
                return false;
            }

            roles = mapped;
            return true;
        }

        private static string DescribeSignature(MethodInfo method)
        {
            var parts = method.GetParameters()
                .Select(p =>
                {
                    Type t = p.ParameterType;
                    string prefix = t.IsByRef ? (p.IsOut ? "out " : "ref ") : "";
                    if (t.IsByRef) t = t.GetElementType();
                    return $"{prefix}{t.Name} {p.Name}";
                });
            // The declaring type is printed rather than assumed: the whole point of RenderHostTypes is that
            // the method's home has moved before and may move again, and a log line naming the wrong type is
            // how the previous revision's bug survived review.
            string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "(unknown type)";
            return $"{typeName}.{method.Name}({string.Join(", ", parts)})";
        }

        /// <summary>
        /// Renders the play-mode camera stack into a fresh Texture2D the caller owns, or returns null with
        /// the reason. Only MECHANISM failures are reported here — everything this returns null for is a
        /// reason to try the window route instead.
        ///
        /// <paramref name="warning"/> is null on a clean run and otherwise carries a sentence that must be
        /// appended to the result: it means the image was produced but something about it cannot be trusted.
        /// </summary>
        private static Texture2D TryRenderGameView(int width, int height, bool includeGizmos,
                                                   CaptureOptions opt, out string failure, out string warning)
        {
            failure = null;
            warning = null;

            var binding = ResolveRenderBinding();
            if (binding.Failure != null)
            {
                failure = binding.Failure;
                return null;
            }

            var args = new object[binding.Roles.Length];
            for (int i = 0; i < binding.Roles.Length; i++)
            {
                switch (binding.Roles[i])
                {
                    case ArgRole.Target: args[i] = null; break;              // filled once the target exists
                    case ArgRole.TargetDisplay: args[i] = 0; break;
                    case ArgRole.MousePosition: args[i] = Vector2.zero; break;
                    case ArgRole.Gizmos: args[i] = includeGizmos; break;
                    case ArgRole.AlwaysTrue: args[i] = true; break;
                }
            }

            RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                rt = CaptureCommon.GetTemporaryTarget(width, height, opt, out string rtError);
                if (rt == null)
                {
                    failure = rtError;
                    return null;
                }

                // Cleared before the internal renderer runs. It is a POOLED target, so if that renderer (or
                // the camera stack it drives) does not clear colour itself, the leftovers would be the
                // PREVIOUS capture's frame — and the "no camera reached display 0, so this frame is nothing
                // but the clear colour" note further down would then be a lie.
                if (!TryClearTarget(rt, Color.clear, out string clearError))
                {
                    warning = " WARNING: the capture target could not be cleared before rendering " +
                              $"({clearError}). It is a POOLED RenderTexture, so anything the play-mode " +
                              "cameras did not draw over may still be a previous capture's pixels — do not " +
                              "trust the background of this image.";
                }

                for (int i = 0; i < binding.Roles.Length; i++)
                    if (binding.Roles[i] == ArgRole.Target) args[i] = rt;

                binding.Method.Invoke(null, args);

                // keepAlpha is false: this tool exposes no transparent background, and the play-mode
                // cameras routinely leave alpha at 0, which would otherwise encode as an invisible PNG.
                Texture2D tex = CaptureCommon.ReadBack(rt, keepAlpha: false, out string readError);
                if (tex == null)
                {
                    failure = readError;
                    return null;
                }
                return tex;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                failure = $"{binding.Signature} threw {inner.GetType().Name}: {inner.Message}";
                return null;
            }
            catch (Exception ex)
            {
                failure = $"calling {binding.Signature} failed: {ex.Message}";
                return null;
            }
            finally
            {
                // The internal renderer leaves its own target bound; restoring this is what keeps the next
                // unrelated ReadPixels in the editor from reading out of our released texture.
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// The facts that decide whether the render-route image means what the reader will assume it means.
        /// All of them are conditions under which the capture legitimately succeeds while showing something
        /// other than "the game as the user sees it right now".
        /// </summary>
        private static string BuildRenderRouteNotes(bool includeGizmos)
        {
            var sb = new StringBuilder();
            sb.Append(includeGizmos
                ? " includeGizmos=true: the Game view gizmo layer is drawn on top of the frame."
                : " includeGizmos=false: no gizmos. SceneView-only overlays (grid, selection outline) are " +
                  "never part of a GameView render regardless of this flag.");

            var gameViews = FindGameViews(out string identification);
            if (gameViews.Count == 0)
            {
                sb.Append(" NOTE: no Game view window is open in this editor layout, so this image is NOT " +
                          "something the user is looking at — it was rendered off-screen. Open Window > " +
                          "General > Game if you need to compare against the on-screen view.");
                if (identification != null) sb.Append(' ').Append(identification);
            }

            if (!EditorApplication.isPlaying)
            {
                sb.Append(" NOTE: the editor is in EDIT mode. The camera stack rendered for real, but no " +
                          "scripts have run, so this is the scene's initial state — not gameplay. Nothing " +
                          "spawned at runtime, no animation past frame 0, no UI driven by script. Enter Play " +
                          "mode first if that matters.");
            }

            var cameras = EnumerateSceneCameras(out _, out _);
            int drawing = cameras.Count(WouldDrawToGameView);
            if (drawing == 0)
            {
                sb.Append(" WARNING: no camera is enabled AND active AND free of a targetTexture AND on " +
                          "targetDisplay=0, so this frame contains nothing but the clear colour. Call " +
                          "ListCameras to see why.");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Locates the open Game view window(s). Reported honestly: when
        /// <c>UnityEditor.PlayModeView</c> cannot be resolved the search falls back to a type-name /
        /// title heuristic and says so, because "the list may be incomplete" and "no Game view is open"
        /// lead to opposite conclusions about the image.
        /// <paramref name="identification"/> is null when the answer is authoritative.
        /// </summary>
        private static List<EditorWindow> FindGameViews(out string identification)
        {
            identification = null;
            var result = new List<EditorWindow>();

            Type playModeViewType = null;
            try { playModeViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView"); }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: UnityEditor.PlayModeView lookup failed ({ex.Message}).");
            }

            if (playModeViewType == null)
            {
                identification =
                    "(UnityEditor.PlayModeView could not be resolved, so open Game views were looked up by " +
                    "type name and window title instead — this list may be incomplete.)";
            }

            EditorWindow[] windows;
            try { windows = Resources.FindObjectsOfTypeAll<EditorWindow>(); }
            catch (Exception ex)
            {
                identification = $"(open Game views could not be enumerated: {ex.Message})";
                return result;
            }

            foreach (var window in windows)
            {
                if (window == null) continue;

                bool isGameView;
                if (playModeViewType != null)
                {
                    isGameView = playModeViewType.IsInstanceOfType(window);
                }
                else
                {
                    string typeName = window.GetType().Name;
                    string title = window.titleContent != null ? window.titleContent.text : "";
                    isGameView = typeName == "GameView" ||
                                 (title != null && title.IndexOf("Game", StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!isGameView) continue;
                if (window.position.width <= 0f || window.position.height <= 0f) continue;
                result.Add(window);
            }
            return result;
        }

        private static bool TryGetPlayModeViewTargetSize(out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            try
            {
                var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
                if (type == null)
                {
                    error = "UnityEditor.PlayModeView does not exist in this Unity version";
                    return false;
                }

                var method = type.GetMethod("GetMainPlayModeViewTargetSize",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (method == null)
                {
                    error = "PlayModeView.GetMainPlayModeViewTargetSize() (no-argument overload) does not exist " +
                            "in this Unity version";
                    return false;
                }
                if (method.ReturnType != typeof(Vector2))
                {
                    error = $"PlayModeView.GetMainPlayModeViewTargetSize returns {method.ReturnType.Name}, " +
                            "not Vector2, so its result cannot be read as a size";
                    return false;
                }

                var size = (Vector2)method.Invoke(null, null);
                int w = Mathf.RoundToInt(size.x);
                int h = Mathf.RoundToInt(size.y);
                if (w <= 0 || h <= 0)
                {
                    // Unity returns a negative or zero size when no play-mode view exists to ask.
                    error = $"PlayModeView.GetMainPlayModeViewTargetSize returned {size.x}x{size.y}, which is " +
                            "not a usable resolution (no Game view is open?)";
                    return false;
                }
                if (w > MaxDimension || h > MaxDimension)
                {
                    error = $"PlayModeView.GetMainPlayModeViewTargetSize returned {w}x{h}, beyond the maximum " +
                            $"RenderTexture dimension ({MaxDimension})";
                    return false;
                }

                width = w;
                height = h;
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                error = $"PlayModeView.GetMainPlayModeViewTargetSize threw {inner.GetType().Name}: {inner.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"PlayModeView.GetMainPlayModeViewTargetSize could not be called: {ex.Message}";
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Window route fallback (Windows only)
        // ─────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR_WIN
        /// <summary>
        /// Cuts the Game view pane out of a focus-free PrintWindow shot of the Unity window that contains
        /// it. Used only when the internal render route is unavailable — the picture is the pane as it is
        /// on screen right now, at its on-screen size, so width/height and includeGizmos no longer apply.
        /// </summary>
        private static string CaptureGameViewViaWindow(CaptureOptions opt, string renderFailure,
                                                       int requestedWidth, int requestedHeight,
                                                       bool includeGizmos)
        {
            string prefix = "Error: the internal play-mode-view render route is unavailable " +
                            $"({renderFailure}), and the window fallback could not run: ";

            using (new WindowCaptureNative.DpiScope())
            {
                var gameViews = FindGameViews(out string identification);
                if (gameViews.Count == 0)
                {
                    return prefix + "no Game view window is open, so there is no pane on screen to " +
                           "photograph. Open Window > General > Game and try again." +
                           (identification != null ? " " + identification : "");
                }
                // WHICH Game view. A BACKGROUND tab is not drawn anywhere: PrintWindow returns the dock
                // showing whichever tab IS in front (a SceneView, an Inspector), and cutting the Game pane's
                // rect out of that bitmap yields another window's pixels labelled "GameView pane 'Game'".
                // CaptureEditorWindow and ListUIElements both refuse that case explicitly; so does this.
                if (!TrySelectDrawnGameView(gameViews, out EditorWindow gameView, out string tabNote,
                                            out string tabError))
                {
                    return prefix + tabError;
                }

                var monitors = WindowCaptureNative.EnumerateMonitors();
                Rect posPt = gameView.position;
                var (paneX, paneY, paneW, paneH) =
                    WindowCaptureNative.UnityRectToPhysical(posPt.x, posPt.y, posPt.width, posPt.height, monitors);
                if (paneW <= 0 || paneH <= 0)
                {
                    return prefix + $"the Game view pane has no drawable area ({paneW}x{paneH}) — it is " +
                           "probably collapsed or on a hidden tab.";
                }

                if (!TryResolveGameViewContainer(gameView, paneX, paneY, paneW, paneH, monitors,
                                                 out IntPtr hwnd, out int winX, out int winY,
                                                 out int winW, out int winH,
                                                 out string containerHow, out string containerError))
                {
                    return prefix + containerError;
                }

                if (!WindowCaptureNative.TryPrintWindow(hwnd, out byte[] bgra, out int bmpW, out int bmpH,
                                                        out string printError))
                {
                    return prefix + $"PrintWindow failed: {printError}";
                }

                // The pane offset was computed against the rect the window had when it was MEASURED. If the
                // bitmap that came back is a different size the window moved or resized in between, every
                // offset is stale, and the crop would return a shifted region as a success.
                if (bmpW != winW || bmpH != winH)
                {
                    return prefix + $"the Unity window changed size between being measured ({winW}x{winH}) and " +
                           $"being captured ({bmpW}x{bmpH}), so the Game pane's offset inside the bitmap is " +
                           "stale and the crop would be shifted. Retry.";
                }

                // One conversion, through the single helper that owns it: the pane's offset inside the
                // window is measured top-left (Win32), the crop CaptureCommon applies is bottom-left
                // (image space). Writing 'bmpH - y' here by hand is how a capture ends up mirrored while
                // still reporting success.
                var paneTopLeft = new Rect(paneX - winX, paneY - winY, paneW, paneH);
                Rect paneBottomLeft = CaptureCommon.RectTopLeftToBottomLeft(paneTopLeft, bmpH);

                int cropX = Mathf.RoundToInt(paneBottomLeft.x);
                int cropY = Mathf.RoundToInt(paneBottomLeft.y);
                int cropW = paneW;
                int cropH = paneH;
                string cropNote = "";

                if (opt.HasCropRegion)
                {
                    // The caller's cropRegion is relative to the PANE, but CaptureCommon will apply it to
                    // the whole window bitmap, so the two rectangles are composed here (both are
                    // bottom-left based, so the offsets simply add).
                    if (!CaptureCommon.TryParseCropRegionSyntax(opt.CropRegion, out int ux, out int uy,
                                                                out int uw, out int uh, out string cropError))
                    {
                        return $"Error: {cropError}";
                    }
                    if (ux < 0 || uy < 0 || ux + uw > paneW || uy + uh > paneH)
                    {
                        return $"Error: cropRegion x={ux} y={uy} w={uw} h={uh} does not fit inside the " +
                               $"{paneW}x{paneH} Game view pane. On the window route cropRegion is measured " +
                               "inside the pane (origin bottom-left), not against the width/height you passed.";
                    }
                    cropX += ux;
                    cropY += uy;
                    cropW = uw;
                    cropH = uh;
                    cropNote = $" cropRegion was applied inside the {paneW}x{paneH} pane.";
                }

                // Checked here rather than left to CaptureCommon so the message names the real cause: the
                // pane's rect landing outside the bitmap means the wrong OS window was identified (or it
                // moved), not that the caller passed a bad cropRegion.
                if (cropX < 0 || cropY < 0 || cropX + cropW > bmpW || cropY + cropH > bmpH)
                {
                    return prefix + $"the Game pane maps to ({cropX},{cropY}) {cropW}x{cropH} (origin " +
                           $"bottom-left) inside the {bmpW}x{bmpH} bitmap of the window it was matched to, " +
                           $"which is outside it. The window was identified as {containerHow}, so either the " +
                           "wrong OS window was picked or the layout changed mid-capture. Retry, or capture " +
                           "the pane with CaptureEditorWindow.";
                }

                opt.CropRegion = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                                               cropX, cropY, cropW, cropH);

                string title = gameView.titleContent != null && !string.IsNullOrEmpty(gameView.titleContent.text)
                    ? gameView.titleContent.text
                    : gameView.GetType().Name;

                string message = CaptureCommon.FinishFromBgra(bgra, bmpW, bmpH, opt, $"GameView pane '{title}'",
                                                              CaptureRoute.Window, out string finishError);
                if (message == null) return $"Error: {finishError}";

                var notes = new StringBuilder();
                notes.Append(" WINDOW ROUTE (fallback): the internal play-mode render was unavailable " +
                             $"({renderFailure}), so the Game pane was cut out of a focus-free PrintWindow " +
                             $"shot of the Unity window. The 'source' size above is that whole window; the " +
                             $"attached image is the {paneW}x{paneH} pane.");
                notes.Append(cropNote);
                notes.Append($" The OS window was identified as {containerHow}.");
                if (!string.IsNullOrEmpty(tabNote)) notes.Append(tabNote);
                if (requestedWidth != 0 || requestedHeight != 0)
                    notes.Append($" width/height ({requestedWidth}x{requestedHeight}) were IGNORED — the window " +
                                 "route can only return the pane at its on-screen size.");
                if (includeGizmos)
                    notes.Append(" includeGizmos was IGNORED — the window route shows whatever the Game view " +
                                 "itself is drawing, so its own Gizmos toggle decides.");
                if (gameViews.Count > 1)
                    notes.Append($" {gameViews.Count} Game views are open; the one captured here is '{title}' — " +
                                 "the first one that is the front tab of its dock, since a background tab is " +
                                 "not drawn at all.");
                if (identification != null) notes.Append(' ').Append(identification);
                if (!EditorApplication.isPlaying)
                    notes.Append(" The editor is in EDIT mode, so this is the scene's initial state, not gameplay.");
                if (!EnumerateSceneCameras(out _, out _).Any(WouldDrawToGameView))
                    notes.Append(" WARNING: no camera is enabled AND active AND free of a targetTexture AND on " +
                                 "targetDisplay=0, so the pane is showing Unity's own placeholder instead of a " +
                                 "frame of the game. Call ListCameras to see why.");

                return message + notes.ToString();
            }
        }

        /// <summary>
        /// Picks the Game view that is actually being DRAWN, or refuses.
        ///
        /// A tab that is behind another tab in the same dock has a perfectly valid
        /// <c>EditorWindow.position</c> and is not rendered by anything, so the window route would cut that
        /// rect out of a bitmap containing the FRONT tab instead — SceneView pixels returned as
        /// "GameView pane 'Game'", route=window, Success. Unity's answer (HostView.actualView) is internal,
        /// so it is read by reflection; when the reflection does not resolve, the capture continues WITH A
        /// NOTE rather than being refused (the same policy as ListUIElements), because "unknown" must not be
        /// treated as "no".
        /// </summary>
        private static bool TrySelectDrawnGameView(List<EditorWindow> gameViews, out EditorWindow chosen,
                                                   out string note, out string error)
        {
            chosen = null;
            note = "";
            error = null;

            EditorWindow unknownTab = null;
            var behind = new List<string>();

            foreach (var window in gameViews)
            {
                if (window == null) continue;
                bool? active = TryIsActiveTab(window);
                if (active == true)
                {
                    chosen = window;
                    return true;
                }
                if (active == null)
                {
                    if (unknownTab == null) unknownTab = window;
                    continue;
                }
                behind.Add($"'{DescribeWindowTitle(window)}'");
            }

            if (unknownTab != null)
            {
                chosen = unknownTab;
                note = " Whether this Game view is the FRONT tab of its dock could NOT be determined (Unity's " +
                       "internal HostView reflection did not resolve on this version), so it was captured " +
                       "anyway: if the image shows some other pane — a SceneView, an Inspector — that is why.";
                return true;
            }

            error = "the Game view is a BACKGROUND tab in its dock (" + string.Join(", ", behind) + "), so " +
                    "nothing is drawing it. A PrintWindow shot of that dock shows whichever tab IS in front, " +
                    "and cutting the Game pane's rect out of it would return another window's pixels labelled " +
                    "as the game — so no image is returned. Click the Game tab to bring it forward and retry, " +
                    "or fix the render route (which needs no window at all).";
            return false;
        }

        /// <summary>
        /// Which OS window PrintWindow must be pointed at, and its rect: a docked Game view has no HWND of
        /// its own (the whole dock is one OS window), so the container is photographed and the pane cropped.
        ///
        /// Two mechanisms, and the difference matters enough to be reported:
        /// - RELIABLE: Unity's own ContainerWindow rect for this EditorWindow, matched against the
        ///   enumerated windows by intersection-over-union. Unity never exposes the HWND, only the rect.
        /// - HEURISTIC: the smallest visible Unity window that fully contains the pane. This is ambiguous by
        ///   construction — a floating Unity window parked over a docked Game view (Package Manager, an
        ///   undocked Console, Preferences) contains the pane rect too AND is smaller than the main window,
        ///   so it wins the "smallest containing" contest and its pixels would be returned as the game. The
        ///   caller prints <paramref name="how"/> next to the image so the reader can check.
        /// </summary>
        private static bool TryResolveGameViewContainer(EditorWindow gameView,
                                                        int paneX, int paneY, int paneW, int paneH,
                                                        List<WindowCaptureNative.MonitorDescriptor> monitors,
                                                        out IntPtr hwnd, out int winX, out int winY,
                                                        out int winW, out int winH,
                                                        out string how, out string error)
        {
            hwnd = IntPtr.Zero;
            winX = 0;
            winY = 0;
            winW = 0;
            winH = 0;
            how = null;
            error = null;

            // includeInvisible:true plus a manual visibility filter, matching
            // WindowCaptureTools.EnumerateUnityTopLevelWindows and UIElementTools.ResolveOwningWindow: a
            // floating Unity container or popup can carry no caption at all, and the caption filter inside
            // EnumerateTopLevelWindows drops exactly those — i.e. it can drop the pane's real container.
            var candidates = new List<WindowCaptureNative.WindowDescriptor>();
            foreach (var w in WindowCaptureNative.EnumerateTopLevelWindows(includeInvisible: true))
            {
                if (!w.IsUnity || !w.IsVisible || w.IsMinimized) continue;
                if (w.Width <= 0 || w.Height <= 0) continue;
                candidates.Add(w);
            }

            RectInt? containerRect = TryGetContainerRectPhysical(gameView, monitors, out string hintError);

            if (containerRect.HasValue)
            {
                // The OS rect is a few pixels larger than Unity's (Windows 10+ counts an invisible resize
                // border into GetWindowRect), so the true container scores ~0.9 while a window that merely
                // overlaps scores far lower. Below 0.5 nothing is accepted.
                double bestScore = 0;
                bool found = false;
                WindowCaptureNative.WindowDescriptor best = default(WindowCaptureNative.WindowDescriptor);
                foreach (var w in candidates)
                {
                    var candidate = new RectInt(w.X, w.Y, w.Width, w.Height);
                    long intersection = IntersectArea(containerRect.Value, candidate);
                    if (intersection <= 0) continue;
                    double union = (double)containerRect.Value.width * containerRect.Value.height
                                 + (double)w.Width * w.Height - intersection;
                    if (union <= 0) continue;
                    double iou = intersection / union;
                    if (iou < 0.5 || iou <= bestScore) continue;
                    bestScore = iou;
                    best = w;
                    found = true;
                }
                if (found)
                {
                    hwnd = best.Hwnd;
                    winX = best.X;
                    winY = best.Y;
                    winW = best.Width;
                    winH = best.Height;
                    how = $"0x{best.Hwnd.ToInt64():X8}, matched to the Game view's own ContainerWindow rect " +
                          $"({bestScore * 100.0:F0}% overlap) — not a guess";
                    return true;
                }
            }

            // Heuristic fallback. Full containment (not 90% coverage) because the pane is cropped out of this
            // window's bitmap exactly, and smallest-first because a floating container sits on top of the main
            // window and both contain the pane.
            {
                long bestArea = long.MaxValue;
                bool found = false;
                WindowCaptureNative.WindowDescriptor best = default(WindowCaptureNative.WindowDescriptor);
                foreach (var w in candidates)
                {
                    if (paneX < w.X || paneY < w.Y ||
                        paneX + paneW > w.X + w.Width || paneY + paneH > w.Y + w.Height) continue;
                    long area = (long)w.Width * w.Height;
                    if (area >= bestArea) continue;
                    bestArea = area;
                    best = w;
                    found = true;
                }
                if (found)
                {
                    hwnd = best.Hwnd;
                    winX = best.X;
                    winY = best.Y;
                    winW = best.Width;
                    winH = best.Height;
                    string title = string.IsNullOrEmpty(best.Title) ? "(untitled)" : best.Title;
                    how = $"0x{best.Hwnd.ToInt64():X8} \"{title}\" by HEURISTIC: the smallest visible Unity " +
                          "window containing the pane rect, so ANOTHER Unity window lying over the Game view " +
                          "(Package Manager, an undocked Console) could have been picked instead — check that " +
                          "the image really shows the game";
                    how += containerRect.HasValue
                        ? $" — WARNING: the ContainerWindow rect ({containerRect.Value.x},{containerRect.Value.y} " +
                          $"{containerRect.Value.width}x{containerRect.Value.height}) matched no visible " +
                          "top-level Unity window, which usually means the container is cloaked or on a " +
                          "monitor layout this conversion got wrong"
                        : $" (the reliable ContainerWindow rect could not be read: {hintError})";
                    return true;
                }
            }

            // Nothing in the enumeration contained the pane. The process main window is the last candidate,
            // and it is only usable if it really does contain the pane — cropping outside a bitmap would
            // return another part of the editor as if it were the game.
            IntPtr main = WindowCaptureNative.GetUnityMainWindow(out string mainError);
            if (main == IntPtr.Zero)
            {
                error = $"no visible Unity window contains the Game pane at ({paneX},{paneY}) {paneW}x{paneH}, " +
                        $"and the main window handle is unavailable ({mainError}).";
                return false;
            }
            if (!WindowCaptureNative.TryGetWindowRect(main, out int mx, out int my, out int mw, out int mh,
                                                      out string rectError))
            {
                error = $"no visible Unity window contains the Game pane at ({paneX},{paneY}) {paneW}x{paneH}, " +
                        $"and the main window rect could not be read ({rectError}).";
                return false;
            }
            if (paneX < mx || paneY < my || paneX + paneW > mx + mw || paneY + paneH > my + mh)
            {
                error = $"the Game pane at ({paneX},{paneY}) {paneW}x{paneH} is not inside any visible Unity " +
                        $"window (main window is ({mx},{my}) {mw}x{mh}). It may be on a hidden tab, on a " +
                        "monitor that was just disconnected, or reported at a stale position.";
                return false;
            }

            hwnd = main;
            winX = mx;
            winY = my;
            winW = mw;
            winH = mh;
            how = $"0x{main.ToInt64():X8}, the Unity MAIN window as a last resort by HEURISTIC: no enumerated " +
                  "Unity window matched or contained the pane, so this is the main window merely containing " +
                  "its rect — an unrelated part of the editor could be what you see" +
                  (containerRect.HasValue ? "" : $" (the ContainerWindow rect could not be read: {hintError})");
            return true;
        }

        /// <summary>
        /// The Game view's ContainerWindow rect converted to PHYSICAL pixels (the space the enumerated OS
        /// window rects live in), or null with the reason.
        ///
        /// Unity exposes the ContainerWindow's rect but never its HWND, which is why identifying the OS
        /// window means matching rects rather than asking for a handle. Every link of the chain
        /// (EditorWindow.m_Parent → HostView.window → ContainerWindow.position) is internal, so a miss at any
        /// step degrades to the containment heuristic and is reported as such — never guessed.
        /// </summary>
        private static RectInt? TryGetContainerRectPhysical(
            EditorWindow window, List<WindowCaptureNative.MonitorDescriptor> monitors, out string error)
        {
            error = null;
            try
            {
                FieldInfo parentField = FindFieldUpHierarchy(typeof(EditorWindow), "m_Parent");
                if (parentField == null)
                {
                    error = "EditorWindow.m_Parent does not exist on this Unity version";
                    return null;
                }
                object host = parentField.GetValue(window);
                // A destroyed HostView is still a live managed reference that Unity's == reports as null;
                // calling into it would throw MissingReferenceException.
                if (host == null || (host is UnityEngine.Object hostObject && hostObject == null))
                {
                    error = "the window's HostView is null or destroyed";
                    return null;
                }

                object container = null;
                PropertyInfo windowProperty = FindPropertyUpHierarchy(host.GetType(), "window");
                if (windowProperty != null && windowProperty.CanRead)
                    container = windowProperty.GetValue(host, null);
                if (container == null)
                {
                    FieldInfo windowField = FindFieldUpHierarchy(host.GetType(), "m_Window");
                    if (windowField != null) container = windowField.GetValue(host);
                }
                if (container == null || (container is UnityEngine.Object containerObject && containerObject == null))
                {
                    error = "the HostView has no live ContainerWindow";
                    return null;
                }

                Rect logical;
                PropertyInfo positionProperty = FindPropertyUpHierarchy(container.GetType(), "position");
                if (positionProperty != null && positionProperty.CanRead &&
                    positionProperty.PropertyType == typeof(Rect) &&
                    positionProperty.GetValue(container, null) is Rect fromProperty)
                {
                    logical = fromProperty;
                }
                else
                {
                    FieldInfo positionField = FindFieldUpHierarchy(container.GetType(), "m_PixelRect");
                    if (positionField != null && positionField.FieldType == typeof(Rect) &&
                        positionField.GetValue(container) is Rect fromField)
                    {
                        logical = fromField;
                    }
                    else
                    {
                        error = $"neither {container.GetType().Name}.position nor m_PixelRect is readable on " +
                                "this Unity version";
                        return null;
                    }
                }

                var (cx, cy, cw, ch) = WindowCaptureNative.UnityRectToPhysical(
                    logical.x, logical.y, logical.width, logical.height, monitors);
                if (cw <= 0 || ch <= 0)
                {
                    error = $"the ContainerWindow rect converted to an empty physical rect ({cw}x{ch})";
                    return null;
                }
                return new RectInt(cx, cy, cw, ch);
            }
            catch (Exception ex)
            {
                error = $"reading the ContainerWindow rect failed ({ex.Message})";
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: {error}; the containment heuristic is used instead.");
                return null;
            }
        }

        /// <summary>
        /// True / false when Unity's internals say whether this window is the frontmost tab of its dock, null
        /// when they could not be read. Null must stay distinguishable from false: guessing "yes" captures
        /// another tab's pixels, guessing "no" refuses a perfectly capturable window.
        /// </summary>
        private static bool? TryIsActiveTab(EditorWindow window)
        {
            try
            {
                FieldInfo parentField = FindFieldUpHierarchy(typeof(EditorWindow), "m_Parent");
                if (parentField == null)
                {
                    AgentLogger.Debug(LogTag.Tool,
                        "GameViewCaptureTools: EditorWindow.m_Parent not found on this Unity version; the " +
                        "active-tab check for the window route is unavailable.");
                    return null;
                }

                object host = parentField.GetValue(window);
                if (host == null) return null;
                if (host is UnityEngine.Object hostObject && hostObject == null) return null;

                Type hostType = host.GetType();
                PropertyInfo actualProperty = FindPropertyUpHierarchy(hostType, "actualView");
                if (actualProperty != null && actualProperty.CanRead)
                    return ReferenceEquals(actualProperty.GetValue(host, null), window);

                FieldInfo actualField = FindFieldUpHierarchy(hostType, "m_ActualView");
                if (actualField != null)
                    return ReferenceEquals(actualField.GetValue(host), window);

                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: neither {hostType.Name}.actualView nor m_ActualView resolved; the " +
                    "active-tab check for the window route is unavailable.");
                return null;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: the active-tab check failed ({ex.Message}); it is reported as " +
                    "unknown rather than guessed.");
                return null;
            }
        }

        private static string DescribeWindowTitle(EditorWindow window)
        {
            if (window == null) return "(destroyed)";
            string title = window.titleContent != null ? window.titleContent.text : null;
            return string.IsNullOrEmpty(title) ? window.GetType().Name : title;
        }

        private static long IntersectArea(RectInt a, RectInt b)
        {
            int x0 = Mathf.Max(a.x, b.x);
            int y0 = Mathf.Max(a.y, b.y);
            int x1 = Mathf.Min(a.x + a.width, b.x + b.width);
            int y1 = Mathf.Min(a.y + a.height, b.y + b.height);
            return (long)Mathf.Max(0, x1 - x0) * Mathf.Max(0, y1 - y0);
        }

        // Private members declared on a BASE type are not returned by GetField/GetProperty on the derived
        // type, and m_Parent / actualView / window sit at different levels of the EditorWindow and HostView
        // chains depending on the Unity version, so the chain is walked explicitly.
        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo FindFieldUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindPropertyUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
#endif

        // ─────────────────────────────────────────────────────────────────────────
        // Camera enumeration / resolution
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every Camera that belongs to a loaded scene, sorted by depth ascending (Unity's draw order).
        ///
        /// <c>Resources.FindObjectsOfTypeAll</c> rather than FindObjectsOfType or GameObject.Find, because
        /// a camera on an INACTIVE GameObject is exactly what a preview capture is usually for and the
        /// other two do not return it. That also drags in two kinds of camera that cannot be rendered as
        /// part of a scene, so both are filtered out and COUNTED (never silently dropped):
        /// cameras inside prefab ASSETS on disk, and editor-internal cameras with no scene at all — the
        /// SceneView camera, preview and thumbnail cameras. Cameras in a Prefab Mode stage survive the
        /// filter, since a prefab stage is a real preview scene.
        /// </summary>
        private static List<Camera> EnumerateSceneCameras(out int prefabAssetCameras, out int nonSceneCameras)
        {
            prefabAssetCameras = 0;
            nonSceneCameras = 0;
            var result = new List<Camera>();

            Camera[] all;
            try { all = Resources.FindObjectsOfTypeAll<Camera>(); }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"GameViewCaptureTools: enumerating cameras failed: {ex.Message}");
                return result;
            }

            foreach (var cam in all)
            {
                if (cam == null) continue;
                if (EditorUtility.IsPersistent(cam)) { prefabAssetCameras++; continue; }
                if (!cam.gameObject.scene.IsValid()) { nonSceneCameras++; continue; }
                result.Add(cam);
            }

            // Ordered so the list reads as the draw order. OrderBy is stable, so equal depths keep the
            // enumeration order instead of shuffling between calls.
            return result.OrderBy(c => c.depth).ToList();
        }

        private static string DescribeExclusions(int prefabAssetCameras, int nonSceneCameras)
        {
            if (prefabAssetCameras == 0 && nonSceneCameras == 0) return "";
            var parts = new List<string>(2);
            if (prefabAssetCameras > 0)
                parts.Add($"{prefabAssetCameras} inside prefab ASSETS (no scene to render into)");
            if (nonSceneCameras > 0)
                parts.Add($"{nonSceneCameras} editor-internal with no scene (SceneView / preview cameras)");
            return " Excluded from this list: " + string.Join("; ", parts) + ".";
        }

        /// <summary>True when this camera is one of the cameras the GameView actually shows.</summary>
        private static bool WouldDrawToGameView(Camera cam)
            => cam != null && cam.enabled && cam.gameObject.activeInHierarchy
               && cam.targetTexture == null && cam.targetDisplay == 0;

        /// <summary>
        /// Resolves the cameraName argument. <paramref name="note"/> is always filled with what was
        /// resolved and how, so an ambiguous name cannot silently pick a different camera than the reader
        /// has in mind — the path of the camera that was actually rendered is in the result.
        /// </summary>
        private static bool TryResolveCamera(string cameraName, List<Camera> cameras,
                                             int prefabAssetCameras, int nonSceneCameras,
                                             out Camera camera, out string note, out string error)
        {
            camera = null;
            note = null;
            error = null;

            string exclusions = DescribeExclusions(prefabAssetCameras, nonSceneCameras);

            if (cameras.Count == 0)
            {
                error = "no Camera exists in any loaded scene, so there is nothing to render." + exclusions +
                        " Call ListCameras to confirm, or use CaptureSceneView to capture the editor's own " +
                        "SceneView camera instead.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cameraName))
            {
                var main = Camera.main;
                if (main == null)
                {
                    error = "cameraName was empty so Camera.main was used, but Camera.main is null — no camera " +
                            "is simultaneously tagged 'MainCamera', enabled and active. Pass an explicit " +
                            "cameraName; call ListCameras for the exact names and hierarchy paths. Candidates: " +
                            DescribeCandidates(cameras);
                    return false;
                }
                if (!cameras.Contains(main))
                {
                    // Camera.main resolved to something outside the scene set (an editor preview camera).
                    // Rendering it would be a different subject than the caller means, so refuse.
                    error = $"Camera.main resolved to '{main.name}', which is not one of the scene cameras this " +
                            "tool can render (it belongs to no loaded scene). Pass an explicit cameraName; " +
                            "call ListCameras for the list.";
                    return false;
                }
                camera = main;
                note = $"Rendered Camera.main: '{HierarchyPath(main.gameObject)}'.";
                return true;
            }

            string needle = cameraName.Trim();
            List<Camera> matches;
            string how;

            if (needle.IndexOf('/') >= 0)
            {
                matches = cameras.Where(c => string.Equals(HierarchyPath(c.gameObject), needle,
                                                           StringComparison.OrdinalIgnoreCase)).ToList();
                how = "hierarchy path";
            }
            else
            {
                matches = cameras.Where(c => string.Equals(c.name, needle, StringComparison.Ordinal)).ToList();
                how = "exact name";
                if (matches.Count == 0)
                {
                    matches = cameras.Where(c => string.Equals(c.name, needle, StringComparison.OrdinalIgnoreCase)).ToList();
                    how = "case-insensitive name";
                }
                if (matches.Count == 0)
                {
                    matches = cameras.Where(c => c.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    how = "case-insensitive substring";
                }
            }

            if (matches.Count == 0)
            {
                error = $"no Camera matches '{cameraName}'." + exclusions +
                        " Available: " + DescribeCandidates(cameras) +
                        " Call ListCameras for full paths, or pass a Parent/Child/Camera path to disambiguate.";
                return false;
            }

            // Sorted by path so a repeated call with the same ambiguous name renders the same camera
            // instead of following enumeration order, which is not stable across domain reloads.
            matches = matches.OrderBy(c => HierarchyPath(c.gameObject), StringComparer.Ordinal).ToList();
            camera = matches[0];

            var sb = new StringBuilder();
            sb.Append($"Rendered '{HierarchyPath(camera.gameObject)}' (matched '{cameraName}' by {how}).");
            if (matches.Count > 1)
            {
                sb.Append($" AMBIGUOUS: {matches.Count} cameras matched — ");
                sb.Append(string.Join(", ", matches.Select(c => HierarchyPath(c.gameObject))));
                sb.Append(". The first by path was used; pass a full hierarchy path to pick another.");
            }
            note = sb.ToString();
            return true;
        }

        private static string DescribeCandidates(List<Camera> cameras)
        {
            var names = cameras.Take(10).Select(c => $"'{c.name}'").ToList();
            string joined = string.Join(", ", names);
            return cameras.Count > names.Count ? $"{joined}, … ({cameras.Count} total)" : joined;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Small helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a warning sentence when every sampled pixel of the finished render is the same colour,
        /// or an empty string otherwise.
        ///
        /// This is the one failure mode a render-route capture cannot otherwise report: the render
        /// succeeds mechanically, the encode succeeds, the attachment succeeds, and the image is a flat
        /// rectangle. Without this note the caller reads "Success" and concludes the scene looks like that.
        /// It is a NOTE and not an error because a genuinely uniform frame is possible (a fade-out, an
        /// unlit night scene, a deliberately empty background), and refusing to return one would be its own
        /// kind of lie.
        ///
        /// Sampled on a coarse grid rather than pixel by pixel: any real frame differs within a handful of
        /// samples, so the cost is a few hundred GetPixel calls instead of a full-resolution scan.
        /// A texture that cannot be read yields no note — an unproven claim is not made either way.
        /// </summary>
        private static string DescribeFlatFrame(Texture2D tex, string likelyCause)
        {
            if (tex == null || tex.width <= 1 || tex.height <= 1) return "";

            try
            {
                const int steps = 32;
                int stepX = Mathf.Max(1, tex.width / steps);
                int stepY = Mathf.Max(1, tex.height / steps);
                Color first = tex.GetPixel(0, 0);
                for (int y = 0; y < tex.height; y += stepY)
                {
                    for (int x = 0; x < tex.width; x += stepX)
                    {
                        Color c = tex.GetPixel(x, y);
                        if (c != first) return "";
                    }
                }
                return $" WARNING: every sampled pixel of this frame is the same colour " +
                       $"(#{ColorUtility.ToHtmlStringRGBA(first)}), i.e. the image is flat. If the subject is " +
                       $"not genuinely one colour, {likelyCause}.";
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"GameViewCaptureTools: flat-frame probe skipped ({ex.Message}).");
                return "";
            }
        }

        /// <summary>
        /// Gives a freshly acquired render target a DEFINED starting state, and says so when it could not.
        ///
        /// Why this is not optional: CaptureCommon.GetTemporaryTarget hands out RenderTexture.GetTemporary
        /// surfaces, i.e. RECYCLED memory whose contents are undefined — and on D3D11 the DiscardContents of
        /// ReleaseTemporary is a no-op, so the surface still physically holds whatever was rendered into it
        /// last. A camera whose clearFlags is Depth or Nothing (the normal setup for an overlay, UI, mirror or
        /// second-display camera, which is exactly what CaptureFromCamera exists to preview) does not clear
        /// colour, so without this the PREVIOUS capture's frame becomes the background of this one and comes
        /// back as a clean "Success" — DescribeFlatFrame cannot catch it, because the result is not flat.
        ///
        /// GL.Clear needs the target bound, so RenderTexture.active is swapped and restored in a finally;
        /// leaving it bound would make the next unrelated ReadPixels in the editor read out of our texture.
        /// </summary>
        private static bool TryClearTarget(RenderTexture rt, Color color, out string error)
        {
            error = null;
            if (rt == null)
            {
                error = "there is no render target to clear";
                return false;
            }

            var prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, color);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
        }

        /// <summary>
        /// Slash-separated path from the scene root. Reported alongside the plain name because names are
        /// not unique — several avatars in one scene routinely each own a camera called "Camera", and only
        /// the path says which one was rendered.
        /// </summary>
        private static string HierarchyPath(GameObject go)
        {
            if (go == null) return "(destroyed)";
            var sb = new StringBuilder(go.name);
            var parent = go.transform.parent;
            while (parent != null)
            {
                sb.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return sb.ToString();
        }

        private static string Lower(bool value) => value ? "true" : "false";
    }
}
