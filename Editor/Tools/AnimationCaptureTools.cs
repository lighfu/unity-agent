using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Animation review as a contact sheet: one AnimationClip, several sample times, one image.
    ///
    /// Why this exists: nothing in the capture family could show a clip's time axis. Judging a wave, a
    /// blink or a walk cycle meant capturing a pose, scrubbing the Animation window by hand, capturing
    /// again — several tool calls per pose, with the scrub position drifting between them, and no single
    /// image the poses could be compared in.
    ///
    /// Two hazards shape the whole implementation:
    ///
    /// 1. <c>AnimationClip.SampleAnimation</c> writes the pose straight into the scene and leaves it
    ///    there. Everything here goes through <see cref="AnimationMode"/> instead, whose
    ///    StopAnimationMode reverts every property that was touched — the same mechanism the Animation
    ///    window previews with. The stop is not optional and not best-effort: it runs before the result
    ///    is built and again from the finally on every exception path.
    /// 2. <c>AnimationMode.SampleAnimationClip</c> takes SECONDS. Feeding it normalized 0..1 time
    ///    compiles, runs, and returns a sheet of N identical frames clamped near t=0 for any clip longer
    ///    than a second — a wrong answer that looks exactly like a right one. The multiplication by
    ///    <c>clip.length</c> happens in one place, both numbers are burned into every cell, and cells
    ///    that come out identical are reported as such instead of being handed back as a clean success.
    ///
    /// Route: always <see cref="CaptureRoute.Render"/> — a temporary camera into a RenderTexture. No
    /// gizmos, no grid, no selection outline, and no window focus is taken from the user.
    /// </summary>
    public static class AnimationCaptureTools
    {
        // 16 cells is already a 4x4 sheet; past that each cell is too small to read a pose out of, and
        // the sheet stops being a comparison. Splitting a finer sweep across two calls costs nothing.
        private const int MaxFrames = 16;

        // Below ~96px a cell cannot hold a legible time label, and the label is the only thing that tells
        // a reader which pose they are looking at.
        private const int MinCellSize = 96;
        private const int MaxCellSize = 2048;

        // Unity's maximum texture dimension. A sheet over this fails inside the graphics driver with a
        // message that names neither cellSize nor the frame count.
        private const int MaxDimension = 16384;

        // Deliberately fixed instead of copied from the user's SceneView: the framing formula below fits
        // the subject to whatever FOV it is given, so the FOV no longer changes the subject's size — and
        // a fixed value makes two sheets taken days apart directly comparable. The older multi-angle
        // tools copy SceneView.camera.fieldOfView, which makes their framing depend on editor state.
        private const float CameraFov = 60f;

        // Floor for the fitted bounding-sphere radius, so a single-point subject (an empty mesh, a
        // renderer with no geometry) cannot collapse the camera distance to 0 and divide by nothing.
        private const float MinSubjectRadius = 0.02f;

        private static readonly Color DefaultBackdrop = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color CellBorder = new Color(0.35f, 0.35f, 0.35f, 1f);

        // Inset of the time label from its cell's bottom-left corner, in output pixels.
        private const int LabelMargin = 6;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        [AgentTool(@"Sample an AnimationClip at several times and return ONE contact-sheet image, so a whole
motion can be judged in a single look instead of one capture per pose.

clipPath: asset path of the clip. 'Assets/Animations/Wave.anim', or a clip that lives INSIDE another
  asset (an FBX's imported takes are sub-assets) as 'Assets/Model/Char.fbx::Idle'. Without '::' an asset
  holding exactly one clip resolves on its own; when it holds several, the error lists their names rather
  than picking one.
targetName: the GameObject the clip is sampled against — the ROOT its curve paths are relative to (the
  avatar / Animator root), not the mesh. An Animator or Animation component is required, as the marker of
  an animation root: a clip sampled against some other object resolves none of its paths and comes back as
  N identical frames, so the check is refused up front instead of returning that. Inactive objects are
  found too.
frameCount: how many equally spaced samples, with 0 and 1 included as the two ends (default 6, max 16).
  Ignored when 'times' is given.
times: explicit NORMALIZED times, e.g. '0,0.25,0.5,1'. 0 is the clip start and 1 its end whatever
  clip.length is. Cells follow the order you write them in, not sorted order. A value outside 0..1 is an
  error, never a clamp — a clamped time silently duplicates the first or last frame.
cellSize: pixels per square cell (default 384, 96-2048). The sheet is cols x rows cells.
angle: which side the camera stands on. Named: front, back, left, right, top, bottom, 45left, 45right.
  Or degrees as 'yaw' / 'yaw,pitch': yaw 0 = front, +yaw swings toward +X, +pitch lifts the camera above
  the subject (|pitch| <= 89; use 'top'/'bottom' for straight down/up). 'front' puts the camera at +Z
  looking toward -Z, which shows the FACE of a Unity/VRChat-convention avatar because those face +Z.
padding: extra margin around the fitted subject, as a fraction of the distance (default 0.1 = 10%).
lighting: 'scene' (default — the scene's own lights) or 'neutral', which adds a temporary key+fill
  directional light so a dark scene still yields a readable silhouette. 'neutral' modifies NOTHING in the
  scene's lighting settings (no ambient, no skybox, no existing light) and destroys its lights afterwards.
background: 'scene' (default, the scene's skybox/environment), '#RRGGBB' for a flat backdrop — easiest
  for comparing silhouettes — or 'transparent' (PNG only; cell borders are omitted so the alpha stays clean).
maxWidth>0 downscales the LONGER side of the finished sheet, aspect preserved. format='png' (default) or
  'jpg' (jpgQuality 1-100, default 90). saveToPath writes an extra copy at an explicit path.
  cropRegion='x,y,w,h' origin BOTTOM-LEFT, same convention as DiffImages.maskRegion, out of range is an
  error. antiAliasing 1/2/4/8 (default 2). Take pixel coordinates from the 'output WxH' figure in the
  result, never from cellSize — maxWidth may have shrunk the attached image.

FRAMING IS FIXED FOR THE WHOLE SHEET. The camera is placed once, from the UNION of the subject's bounds
over EVERY sampled time (the clip is sampled twice: once to measure, once to render). The subject
therefore sits at the same scale in every cell and the cells are genuinely comparable. A per-cell refit
would zoom in and out as the character moves and no two cells could be compared. The consequence is
worth knowing: a motion that travels a long way makes the subject small in all cells.

TIME IS SAMPLED IN SECONDS. Normalized time is multiplied by clip.length before sampling, because
AnimationMode.SampleAnimationClip takes seconds. Every cell is labelled with BOTH numbers — '[2] T0.50
0.42S' means normalized 0.50, i.e. 0.42 seconds — so a collapsed sheet can be told from a mislabelled one
at a glance, and the result repeats the full table with frame numbers.

WHAT THIS DOES TO YOUR SCENE. The clip is applied through the editor's AnimationMode and
StopAnimationMode() then reverts every property it touched, including on the exception path. Nothing is
keyed, no asset is written, no Renderer or GameObject is enabled or disabled. If the editor is ALREADY in
Animation mode (the Animation window is recording or previewing) this tool refuses instead of taking the
mode away from you. Play mode is refused as well — AnimationMode is an edit-mode facility; use
CaptureGameView or CaptureFromCamera to look at a running game.

Cells that come out flat, or pixel-identical across different times, are reported as WARNINGs at the end
of the result. Those are the symptoms of the wrong target, a clip whose paths do not resolve against it,
or an inactive subject — never a clean success.",
            Author = "ajisaiflow", Category = "AnimationCapture", Risk = ToolRisk.Caution)]
        public static string CaptureAnimationFrames(
            string clipPath,
            string targetName,
            int frameCount = 6,
            string times = "",
            int cellSize = 384,
            string angle = "front",
            float padding = 0.1f,
            string lighting = "scene",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "",
            string cropRegion = "",
            string background = "scene",
            int antiAliasing = 2)
        {
            // ── Guards that must run before anything in the scene is touched ──

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Error: the editor is in (or entering) Play mode. AnimationMode is an EDIT-mode " +
                       "facility: sampling a clip through it now would fight the Animator that is already " +
                       "driving the object, and StopAnimationMode would revert runtime state that no longer " +
                       "belongs to it. Exit Play mode, or use CaptureGameView / CaptureFromCamera to look at " +
                       "the running game instead.";

            if (AnimationMode.InAnimationMode())
                return "Error: the editor is ALREADY in Animation mode — the Animation window is recording or " +
                       "previewing a clip. This tool will not take the mode over: its own StopAnimationMode() " +
                       "would revert the pose you are looking at, and the Animation window would keep drawing " +
                       "as if it still owned the sample. Stop the preview (the record button, or close the " +
                       "Animation window) and call again.";

            if (string.IsNullOrWhiteSpace(clipPath))
                return "Error: clipPath is required — e.g. 'Assets/Animations/Wave.anim', or " +
                       "'Assets/Model/Char.fbx::Idle' for a clip imported inside another asset.";
            if (string.IsNullOrWhiteSpace(targetName))
                return "Error: targetName is required — the GameObject the clip is sampled against (the " +
                       "Animator / avatar root its curve paths are relative to).";
            if (cellSize < MinCellSize || cellSize > MaxCellSize)
                return $"Error: cellSize must be between {MinCellSize} and {MaxCellSize} (got {cellSize}). " +
                       $"Below {MinCellSize} a cell cannot hold a legible time label, and the label is the only " +
                       "thing that says which pose a cell shows.";
            if (float.IsNaN(padding) || float.IsInfinity(padding) || padding < 0f || padding > 10f)
                return $"Error: padding must be between 0 and 10 (got {Fmt3(padding)}). It is a fraction of the " +
                       "fitted camera distance, so 0.1 means 10% extra margin; a negative value would push the " +
                       "camera inside the subject.";

            if (!TryResolveAngle(angle, out Vector3 cameraSide, out string angleLabel, out string angleError))
                return $"Error: {angleError}";
            if (!TryResolveLighting(lighting, out bool neutralLighting, out string lightingError))
                return $"Error: {lightingError}";

            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath, cropRegion,
                                            background, antiAliasing);
            if (!opt.Validate(out string optError)) return $"Error: {optError}";

            // Validate() already accepted the background string, so this parse cannot fail here; the parsed
            // values are what decide the camera's clear settings and the sheet's gutter colour.
            if (!CaptureCommon.TryParseBackground(opt.Background, out CaptureBackgroundMode bgMode,
                                                 out Color bgColor, out string bgError))
                return $"Error: {bgError}";

            // ── Clip ──

            AnimationClip clip = ResolveClip(clipPath, out string clipNote, out string clipError);
            if (clip == null) return $"Error: {clipError}";

            if (float.IsNaN(clip.length) || clip.length <= 0f)
                return $"Error: AnimationClip '{clip.name}' has length {Fmt3(clip.length)}s — there is no time " +
                       "axis to sample, so every requested frame would land on the same instant. An empty clip " +
                       "(one with no keyframes) has length 0; add curves, or capture the pose with " +
                       "CaptureMultiAngle instead.";

            if (!InspectBindings(clip, out int bindingCount, out EditorCurveBinding[] allBindings,
                                 out string bindingsError))
                return $"Error: {bindingsError}";

            // ── Target ──

            var target = MeshAnalysisTools.FindGameObject(targetName);
            if (target == null)
                return $"Error: GameObject '{targetName}' not found in any loaded scene (inactive objects and " +
                       "hierarchy paths were searched too). Pass the exact name or a path like " +
                       "'Avatar/Armature/Hips'.";
            string targetPath = HierarchyPath(target);

            var animator = target.GetComponent<Animator>();
            var legacyAnimation = target.GetComponent<Animation>();
            if (animator == null && legacyAnimation == null)
                return $"Error: '{targetPath}' has neither an Animator nor an Animation component, so it is not " +
                       "an animation root. A clip's curve paths are relative to the root it was authored " +
                       "against; sampling against the wrong object silently resolves nothing and returns " +
                       "identical frames. Pass the avatar / Animator root (usually the object with the " +
                       "VRCAvatarDescriptor or the Animator).";

            // A legacy clip is normally played by the Animation component and a Mecanim one by the Animator.
            // AnimationMode samples the curves directly, so the mismatch is usually harmless — but it is the
            // first thing worth checking when the sheet comes back unanimated, so it is reported, not fixed
            // silently.
            string componentNote;
            if (clip.legacy && animator != null && legacyAnimation == null)
                componentNote = $" NOTE: '{clip.name}' is a LEGACY clip but '{targetPath}' has only an Animator " +
                                "(legacy clips are driven by the Animation component). AnimationMode samples " +
                                "the curves directly, so this often still works; if the frames come back " +
                                "identical, this mismatch is the first thing to check.";
            else if (!clip.legacy && legacyAnimation != null && animator == null)
                componentNote = $" NOTE: '{clip.name}' is a Mecanim clip but '{targetPath}' has only an " +
                                "Animation component (the legacy player). AnimationMode samples the curves " +
                                "directly, so this often still works; if the frames come back identical, this " +
                                "mismatch is the first thing to check.";
            else
                componentNote = string.Empty;

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var framingRenderers = new List<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) framingRenderers.Add(r);
            }
            bool anyVisibleRenderer = framingRenderers.Count > 0;
            if (!anyVisibleRenderer)
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) framingRenderers.Add(renderers[i]);
            }
            if (framingRenderers.Count == 0)
                return $"Error: '{targetPath}' has no Renderer under it (neither itself nor any child), so every " +
                       "cell of the sheet would be empty. Pass the object that actually holds the meshes — an " +
                       "Animator root whose meshes live somewhere else cannot be photographed from here.";

            // ── Sample times ──

            if (!TryResolveTimes(times, frameCount, out float[] normalizedTimes, out string timesNote,
                                 out string timesError))
                return $"Error: {timesError}";

            int count = normalizedTimes.Length;
            CaptureCommon.ComputeGrid(count, out int cols, out int rows);
            long sheetW = (long)cols * cellSize;
            long sheetH = (long)rows * cellSize;
            if (sheetW > MaxDimension || sheetH > MaxDimension)
                return $"Error: a {cols}x{rows} sheet of {cellSize}px cells is {sheetW}x{sheetH}, past the " +
                       $"{MaxDimension}px texture limit. Lower cellSize or take fewer frames.";

            var notes = new StringBuilder();
            var warnings = new StringBuilder();
            if (!string.IsNullOrEmpty(clipNote)) notes.Append(' ').Append(clipNote);
            if (!string.IsNullOrEmpty(timesNote)) notes.Append(' ').Append(timesNote);
            if (!string.IsNullOrEmpty(componentNote)) notes.Append(componentNote);

            bool keepAlpha = bgMode == CaptureBackgroundMode.Transparent;

            // Read before the try: the catch below must not touch the clip again, because an exception raised
            // by an unloaded asset would make the message interpolation throw a second time from inside the
            // handler and escape as something unrelated to the real failure.
            string clipName = clip.name;

            GameObject camGo = null;
            GameObject lightRoot = null;
            RenderTexture rt = null;
            Texture2D composite = null;
            bool animationModeStarted = false;

            try
            {
                AnimationMode.StartAnimationMode();
                animationModeStarted = true;
                if (!AnimationMode.InAnimationMode())
                    return "Error: AnimationMode.StartAnimationMode() did not put the editor into Animation " +
                           "mode, so nothing was sampled and nothing in the scene was changed. Another window " +
                           "most likely claimed the mode in the same frame — try again.";

                // ── Pass 1: measure. The camera has to be placed from the union of every pose, otherwise a
                //    moving subject changes scale from cell to cell and the sheet cannot be compared. ──
                Bounds bounds = new Bounds();
                bool haveBounds = false;
                for (int i = 0; i < count; i++)
                {
                    float seconds = normalizedTimes[i] * clip.length;
                    if (!SampleClipAt(target, clip, seconds, out string measureError))
                        return $"Error: {measureError}";

                    if (TryComputeWorldBounds(framingRenderers, out Bounds frameBounds))
                    {
                        if (!haveBounds) { bounds = frameBounds; haveBounds = true; }
                        else bounds.Encapsulate(frameBounds);
                    }
                }
                if (!haveBounds)
                    return $"Error: could not measure '{targetPath}' at any sampled time — none of its " +
                           $"{framingRenderers.Count} renderer(s) reported usable bounds (no mesh assigned?). " +
                           "Without a size there is nowhere to put the camera, and a guessed distance would " +
                           "put the subject off-screen in every cell.";

                // Fit the subject's bounding SPHERE into both the vertical and the horizontal field of view.
                // The old maxExtent * 2.5 form ignored the FOV entirely, so a tall subject was cut off and a
                // flat one came back tiny.
                Vector3 center = bounds.center;
                float radius = Mathf.Max(bounds.extents.magnitude, MinSubjectRadius);
                float vHalf = CameraFov * 0.5f * Mathf.Deg2Rad;
                const float cellAspect = 1f;   // cells are square; kept explicit so the fit reads as general
                float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cellAspect);
                float distance = radius / Mathf.Sin(Mathf.Min(vHalf, hHalf)) * (1f + padding);
                Vector3 camPos = center + cameraSide * distance;

                // Derived from the fitted distance rather than copied from the SceneView: a near plane left at
                // the editor's value can slice into the front of the subject, and a far plane too close drops
                // the background the skybox clear would otherwise show.
                float nearClip = Mathf.Max(0.01f, (distance - radius) * 0.5f);
                float farClip = (distance + radius) * 4f + 1f;

                camGo = new GameObject("__AnimationFramesCaptureCam");
                camGo.hideFlags = HideFlags.HideAndDontSave;
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;   // rendered only by the explicit Render() calls below
                cam.fieldOfView = CameraFov;
                cam.aspect = cellAspect;
                cam.nearClipPlane = nearClip;
                cam.farClipPlane = farClip;
                camGo.transform.position = camPos;
                camGo.transform.rotation = LookRotationTowards(center - camPos);

                switch (bgMode)
                {
                    case CaptureBackgroundMode.Transparent:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                        notes.Append(" background=transparent: the cells are cleared to alpha 0 and the sheet " +
                                     "keeps its alpha channel, so no skybox is drawn. If the PNG comes back " +
                                     "opaque anyway, the render pipeline overwrote alpha — URP/HDRP " +
                                     "post-processing commonly does.");
                        break;
                    case CaptureBackgroundMode.SolidColor:
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(bgColor.r, bgColor.g, bgColor.b, 1f);
                        break;
                    default:
                        cam.clearFlags = CameraClearFlags.Skybox;
                        // Unity falls back to this colour when the scene has no skybox material.
                        cam.backgroundColor = DefaultBackdrop;
                        break;
                }

                if (neutralLighting)
                {
                    lightRoot = CreateNeutralLights(camGo.transform.rotation, out string lightNote);
                    if (lightRoot != null) notes.Append(' ').Append(lightNote);
                    else warnings.Append(" WARNING: lighting='neutral' was requested but the temporary lights " +
                                         "could not be created, so the cells are lit by the scene only.");
                }

                rt = CaptureCommon.GetTemporaryTarget(cellSize, cellSize, opt, out string rtError);
                if (rt == null) return $"Error: {rtError}";
                cam.targetTexture = rt;

                composite = new Texture2D((int)sheetW, (int)sheetH,
                    keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                FillSheetBackground(composite, bgMode, bgColor);

                // ── Pass 2: render. Same camera for every cell; only the clip time changes. ──
                var checksums = new ulong[count];
                var flatCells = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    float seconds = normalizedTimes[i] * clip.length;
                    if (!SampleClipAt(target, clip, seconds, out string sampleError))
                        return $"Error: {sampleError}";

                    cam.Render();

                    Texture2D cell = CaptureCommon.ReadBack(rt, keepAlpha, out string readError);
                    if (cell == null)
                        return $"Error: frame {i} (normalized {Fmt3(normalizedTimes[i])} = {Fmt3(seconds)}s) " +
                               $"could not be read back: {readError}";
                    try
                    {
                        Color32[] pixels = cell.GetPixels32();
                        if (pixels == null || pixels.Length != cellSize * cellSize)
                            return $"Error: frame {i} came back as {(pixels == null ? 0 : pixels.Length)} pixels " +
                                   $"but {cellSize}x{cellSize} needs {cellSize * cellSize}. The sheet would be " +
                                   "assembled from mismatched cells, so nothing was returned.";

                        CellOrigin(i, cols, rows, cellSize, out int cellX, out int cellY);
                        composite.SetPixels32(cellX, cellY, cellSize, cellSize, pixels);
                        Probe(pixels, out checksums[i], out flatCells[i]);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(cell);
                    }
                }

                // Restore the scene as early as possible: everything below is pixel work that has no business
                // holding the user's hierarchy in a sampled pose. The finally still covers the exception paths.
                string stopNote = StopAnimationModeNow(ref animationModeStarted);

                // ── Labels and cell separators ──
                int labelScale = Mathf.Clamp(cellSize / 128, 1, 4);
                int labelsMissing = 0;
                int labelsShortened = 0;
                for (int i = 0; i < count; i++)
                {
                    CellOrigin(i, cols, rows, cellSize, out int cellX, out int cellY);

                    // Skipped for transparent sheets: an opaque frame around every cell would be the only
                    // solid thing in an otherwise clean alpha image.
                    if (!keepAlpha)
                        CaptureCommon.DrawRect(composite, cellX, cellY, cellSize, cellSize, CellBorder, 1,
                                               apply: false);

                    string label = BuildCellLabel(i, normalizedTimes[i], normalizedTimes[i] * clip.length,
                                                  cellSize, labelScale, out bool hasSeconds);
                    if (label == null) { labelsMissing++; continue; }
                    if (!hasSeconds) labelsShortened++;
                    if (!CaptureCommon.DrawTextWithBackground(composite, cellX + LabelMargin,
                                                              cellY + LabelMargin, label, labelScale,
                                                              apply: false))
                        labelsMissing++;
                }
                composite.Apply(false, false);

                // ── Honest reading of what the pixels actually contain ──
                int flatCount = 0;
                for (int i = 0; i < count; i++) if (flatCells[i]) flatCount++;

                bool allIdentical = count > 1;
                for (int i = 1; i < count && allIdentical; i++)
                    if (checksums[i] != checksums[0]) allIdentical = false;

                int distinctTimes = DistinctCount(normalizedTimes);

                notes.Append(DescribeClip(clip, bindingCount));
                notes.Append(DescribeFrames(normalizedTimes, clip));
                notes.Append($" Grid {cols}x{rows}, cells left-to-right then top-to-bottom, {cellSize}px each.");
                notes.Append($" Camera: {angleLabel}, one placement for the whole sheet — distance " +
                             $"{Fmt3(distance)} from the bounds centre of the UNION of all {count} sampled " +
                             $"poses (subject radius {Fmt3(radius)}, fov {Fmt3(CameraFov)}, padding " +
                             $"{Fmt3(padding)}). Size differences between cells are the animation, not the " +
                             "camera.");
                if (!string.IsNullOrEmpty(stopNote)) notes.Append(stopNote);

                int resolvedBindings = CountResolvableBindings(allBindings, target, out bool bindingCheckRan);
                if (!bindingCheckRan)
                    notes.Append(" Curve-path resolution against the target: unknown (AnimationUtility refused " +
                                 "the query) — the sheet is still a real render, but this call cannot say " +
                                 "whether the clip addresses this hierarchy.");
                else if (resolvedBindings == 0)
                    warnings.Append($" WARNING: none of the clip's {bindingCount} curve bindings resolve to an " +
                                    $"object under '{targetPath}'. The clip was almost certainly authored " +
                                    "against a different root, so every cell shows the SAME unanimated pose. " +
                                    "Check that targetName is the root the clip belongs to.");
                else if (resolvedBindings < bindingCount)
                    notes.Append($" {resolvedBindings} of {bindingCount} curve bindings resolve under " +
                                 $"'{targetPath}'; the rest address objects that do not exist there and were " +
                                 "ignored by the sampler.");

                if (!anyVisibleRenderer)
                    warnings.Append($" WARNING: every Renderer under '{targetPath}' is disabled or on an " +
                                    "inactive GameObject. Framing was computed from their meshes, but Unity " +
                                    "draws nothing for them, so the cells show the scene behind the subject. " +
                                    "This tool deliberately does not enable renderers or activate objects (the " +
                                    "old CaptureMeshIsolated did, and left the scene changed) — call SetActive " +
                                    "on the subject yourself and capture again.");

                if (flatCount == count)
                    warnings.Append($" WARNING: all {count} cells are a single flat colour — nothing was " +
                                    "rendered. The subject is outside the frame, hidden, or on a layer the " +
                                    "camera cannot see. Do not read this sheet as 'the animation does nothing'.");
                else if (flatCount > 0)
                    warnings.Append($" WARNING: {flatCount} of {count} cells are a single flat colour, i.e. " +
                                    "empty. The subject probably leaves the frame at those times.");

                if (allIdentical && distinctTimes > 1 && flatCount != count)
                    warnings.Append($" WARNING: all {count} cells are pixel-identical although " +
                                    $"{distinctTimes} different times were sampled. Normalized time IS " +
                                    "multiplied by clip.length here, so this is not the seconds-versus-" +
                                    "normalized mistake: look instead for a clip whose curves do not address " +
                                    "this hierarchy, an animation invisible from this angle (a blendshape seen " +
                                    "from behind), or curves that are genuinely constant.");

                if (labelsMissing > 0)
                    warnings.Append($" WARNING: {labelsMissing} of {count} time labels could not be drawn into " +
                                    "the image — read the frame table above instead of the picture for those.");
                else if (labelsShortened > 0)
                    notes.Append($" {labelsShortened} label(s) were shortened to fit the cell and show the " +
                                 "normalized time only; the seconds for every frame are in the table above.");

                string sheetLabel = $"AnimationClip '{clipName}' on '{targetPath}' ({count} frames, " +
                                    $"{cols}x{rows} sheet)";
                string message = CaptureCommon.Finish(composite, opt, sheetLabel, CaptureRoute.Render,
                                                      out string finishError, destroySource: true);
                composite = null;   // Finish owns the texture from here on, including on its failure path
                if (message == null) return $"Error: {finishError}";

                return message + notes.ToString() + warnings.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: capturing '{clipName}' on '{targetPath}' failed: {ex.Message}. The scene was " +
                       "restored (AnimationMode was stopped as this call unwound) and no asset was written.";
            }
            finally
            {
                // Order matters. Reverting the scene is the one cleanup step that must not be skipped, and it
                // is wrapped so that a throw here cannot leak the hidden camera and lights below into the
                // user's hierarchy.
                if (animationModeStarted)
                {
                    try { AnimationMode.StopAnimationMode(); }
                    catch (Exception ex)
                    {
                        AgentLogger.Error(LogTag.Tool,
                            "CaptureAnimationFrames: AnimationMode.StopAnimationMode() failed while unwinding; " +
                            $"the sampled pose may still be applied to the scene: {ex}");
                    }
                }

                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (lightRoot != null) UnityEngine.Object.DestroyImmediate(lightRoot);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // AnimationMode
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies <paramref name="clip"/> to <paramref name="target"/> at <paramref name="seconds"/> —
        /// SECONDS, not normalized time; the one multiplication by clip.length lives in the caller.
        ///
        /// The Begin/EndSampling pair is what lets AnimationMode record which properties it wrote so
        /// StopAnimationMode can put them back, and EndSampling runs from a finally so an exception inside
        /// the sample cannot leave the recorder half-open for the rest of the session.
        ///
        /// AnimationMode.SampleAnimationClip returns void in Unity 2022.3 (verified against
        /// UnityEditor.CoreModule.dll metadata, where the scripting docs claim a bool), so there is no
        /// per-sample success flag to check. The caller asserts membership of Animation mode once, with
        /// AnimationMode.InAnimationMode(), right after starting it.
        /// </summary>
        private static bool SampleClipAt(GameObject target, AnimationClip clip, float seconds, out string error)
        {
            error = null;
            bool sampling = false;
            // Read up front: the catch below must not touch the clip again, in case the clip is what went wrong.
            string clipName = clip.name;
            try
            {
                AnimationMode.BeginSampling();
                sampling = true;
                AnimationMode.SampleAnimationClip(target, clip, seconds);
                return true;
            }
            catch (Exception ex)
            {
                error = $"sampling '{clipName}' at {Fmt3(seconds)}s failed: {ex.Message}. The scene is " +
                        "restored as this call unwinds.";
                return false;
            }
            finally
            {
                if (sampling)
                {
                    try { AnimationMode.EndSampling(); }
                    catch (Exception ex)
                    {
                        AgentLogger.Warning(LogTag.Tool,
                            $"CaptureAnimationFrames: AnimationMode.EndSampling() failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Leaves Animation mode and reports, for the result string, what actually happened. Clears
        /// <paramref name="started"/> either way so the caller's finally does not repeat the call.
        /// </summary>
        private static string StopAnimationModeNow(ref bool started)
        {
            if (!started) return string.Empty;
            try
            {
                AnimationMode.StopAnimationMode();
                started = false;
                return " The scene is back as it was: AnimationMode.StopAnimationMode() reverted every " +
                       "property the clip touched, nothing was keyed and no asset was written.";
            }
            catch (Exception ex)
            {
                started = false;
                AgentLogger.Error(LogTag.Tool,
                    $"CaptureAnimationFrames: AnimationMode.StopAnimationMode() failed: {ex}");
                return $" WARNING: AnimationMode.StopAnimationMode() threw ({ex.Message}), so the sampled pose " +
                       "MAY STILL be applied to your scene. Open the Animation window and stop any preview, or " +
                       "reload the scene without saving, before trusting what the editor now shows.";
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Clip resolution
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves 'Assets/x.anim' or 'Assets/x.fbx::ClipName' to an AnimationClip.
        ///
        /// Imported model files keep their takes as SUB-assets, which LoadAssetAtPath does not hand back, so
        /// a plain path into an FBX has to go through LoadAllAssetsAtPath. When such a file holds more than
        /// one clip this returns an error listing them instead of picking the first: capturing the wrong take
        /// produces a perfectly plausible sheet of the wrong motion.
        /// </summary>
        private static AnimationClip ResolveClip(string clipPath, out string note, out string error)
        {
            note = null;
            error = null;

            string raw = (clipPath ?? string.Empty).Trim().Replace('\\', '/');
            string subName = null;
            int sep = raw.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0)
            {
                subName = raw.Substring(sep + 2).Trim();
                raw = raw.Substring(0, sep).Trim();
                if (subName.Length == 0)
                {
                    error = $"clipPath '{clipPath}' ends with '::' but names no clip after it. Write " +
                            "'Assets/Model/Char.fbx::Idle', or drop the '::' when the asset holds one clip.";
                    return null;
                }
            }

            string assetPath = ToProjectRelative(raw);
            // Only mentioned when the path was rewritten (an absolute path under the project folder), so the
            // caller can see WHICH path was actually queried without every message repeating itself.
            string pathNote = string.Equals(assetPath, raw, StringComparison.Ordinal)
                ? string.Empty
                : $" (resolved from '{clipPath}')";

            if (subName == null)
            {
                AnimationClip direct = null;
                try { direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath); }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.Tool,
                        $"CaptureAnimationFrames: LoadAssetAtPath<AnimationClip>('{assetPath}') threw " +
                        $"({ex.Message}); falling back to LoadAllAssetsAtPath.");
                }
                if (direct != null) return direct;
            }

            UnityEngine.Object[] all;
            try
            {
                all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            }
            catch (Exception ex)
            {
                error = $"'{assetPath}'{pathNote} could not be read as an asset: {ex.Message}";
                return null;
            }

            if (all == null || all.Length == 0)
            {
                error = $"no asset found at '{assetPath}'{pathNote}. The path must be project-relative " +
                        "('Assets/...' or 'Packages/...') and the asset must exist in the AssetDatabase — a " +
                        "file copied into the project folder is not there until Unity has imported it.";
                return null;
            }

            var clips = new List<AnimationClip>();
            var otherTypes = new HashSet<string>();
            for (int i = 0; i < all.Length; i++)
            {
                var asClip = all[i] as AnimationClip;
                // Unity's own preview clips can appear among an importer's sub-assets and are not takes.
                if (asClip != null && !asClip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    clips.Add(asClip);
                else if (all[i] != null)
                    otherTypes.Add(all[i].GetType().Name);
            }

            if (clips.Count == 0)
            {
                string what = otherTypes.Count > 0 ? string.Join(", ", ToArray(otherTypes)) : "nothing readable";
                error = $"'{assetPath}'{pathNote} exists but holds no AnimationClip (it contains: {what}). " +
                        "Point clipPath at a clip asset (.anim), or at the model file whose imported takes you " +
                        "want ('Assets/Model/Char.fbx::Idle'). An AnimatorController is not a clip — run " +
                        "InspectAnimatorController on it to find the clips its states reference.";
                return null;
            }

            if (subName != null)
            {
                for (int i = 0; i < clips.Count; i++)
                    if (string.Equals(clips[i].name, subName, StringComparison.Ordinal)) return clips[i];

                for (int i = 0; i < clips.Count; i++)
                {
                    if (string.Equals(clips[i].name, subName, StringComparison.OrdinalIgnoreCase))
                    {
                        note = $"clipPath asked for '{subName}' and matched '{clips[i].name}' " +
                               "case-insensitively.";
                        return clips[i];
                    }
                }

                error = $"'{assetPath}'{pathNote} has no clip named '{subName}'. It contains {clips.Count}: " +
                        $"{JoinClipNames(clips)}.";
                return null;
            }

            if (clips.Count == 1)
            {
                note = $"clipPath named no sub-asset and '{assetPath}' holds exactly one AnimationClip " +
                       $"('{clips[0].name}'), so that one was captured.";
                return clips[0];
            }

            error = $"'{assetPath}'{pathNote} holds {clips.Count} AnimationClips, so which one to capture is " +
                    $"ambiguous. Name it as '{assetPath}::<name>'. Available: {JoinClipNames(clips)}.";
            return null;
        }

        private static string ToProjectRelative(string path)
        {
            string p = (path ?? string.Empty).Replace('\\', '/').Trim();
            if (p.Length == 0) return p;

            string dataPath = Application.dataPath.Replace('\\', '/');
            if (p.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                string rest = p.Substring(dataPath.Length).TrimStart('/');
                return rest.Length == 0 ? "Assets" : "Assets/" + rest;
            }
            return p;
        }

        private static string JoinClipNames(List<AnimationClip> clips)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < clips.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('\'').Append(clips[i].name).Append('\'');
                if (i >= 19 && clips.Count > 20)
                {
                    sb.Append($", ... (+{clips.Count - 20} more)");
                    break;
                }
            }
            return sb.ToString();
        }

        private static string[] ToArray(HashSet<string> set)
        {
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Curve bindings
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Collects the clip's curve bindings. Returns false only for a clip with no curves at all: sampling
        /// that would produce N copies of the current pose and report success, which is precisely the kind of
        /// empty answer this tool must not give.
        ///
        /// <paramref name="bindings"/> is empty (never null) when AnimationUtility refuses the query; the
        /// caller reports resolution as 'unknown' in that case rather than claiming zero.
        /// </summary>
        private static bool InspectBindings(AnimationClip clip, out int bindingCount,
                                            out EditorCurveBinding[] bindings, out string error)
        {
            error = null;
            bindingCount = 0;
            bindings = new EditorCurveBinding[0];

            EditorCurveBinding[] floatCurves;
            EditorCurveBinding[] objectCurves;
            try
            {
                floatCurves = AnimationUtility.GetCurveBindings(clip) ?? new EditorCurveBinding[0];
                objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip) ?? new EditorCurveBinding[0];
            }
            catch (Exception ex)
            {
                // Cannot inspect: say nothing rather than guess. The capture itself does not depend on this.
                AgentLogger.Warning(LogTag.Tool,
                    $"CaptureAnimationFrames: could not read the curve bindings of '{clip.name}': {ex.Message}");
                bindingCount = -1;
                return true;
            }

            var merged = new EditorCurveBinding[floatCurves.Length + objectCurves.Length];
            Array.Copy(floatCurves, merged, floatCurves.Length);
            Array.Copy(objectCurves, 0, merged, floatCurves.Length, objectCurves.Length);

            if (merged.Length == 0)
            {
                error = $"AnimationClip '{clip.name}' has no curves, so there is nothing to sample — the sheet " +
                        "would be N copies of the pose the scene is already in. Add curves to the clip, or " +
                        "capture the current pose with CaptureMultiAngle.";
                return false;
            }

            bindings = merged;
            bindingCount = merged.Length;
            return true;
        }

        /// <summary>
        /// How many of the clip's bindings actually address an object under <paramref name="target"/>. Zero
        /// out of many is the signature of a clip authored against a different root — the single most common
        /// reason a contact sheet comes back as N identical frames.
        /// </summary>
        private static int CountResolvableBindings(EditorCurveBinding[] bindings, GameObject target,
                                                   out bool checkRan)
        {
            checkRan = false;
            if (bindings == null || bindings.Length == 0 || target == null) return 0;

            int resolved = 0;
            try
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (AnimationUtility.GetAnimatedObject(target, bindings[i]) != null) resolved++;
                }
                checkRan = true;
                return resolved;
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    "CaptureAnimationFrames: AnimationUtility.GetAnimatedObject failed while checking whether " +
                    $"the clip addresses '{target.name}': {ex.Message}");
                checkRan = false;
                return 0;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Times
        // ─────────────────────────────────────────────────────────────────────────

        private static bool TryResolveTimes(string times, int frameCount, out float[] normalized,
                                            out string note, out string error)
        {
            normalized = null;
            note = null;
            error = null;

            string spec = (times ?? string.Empty).Trim();
            if (spec.Length > 0)
            {
                var parts = spec.Split(',');
                var values = new List<float>(parts.Length);
                for (int i = 0; i < parts.Length; i++)
                {
                    string entry = parts[i].Trim();
                    if (entry.Length == 0)
                    {
                        error = $"times '{times}' contains an empty entry. Write the values as '0,0.25,0.5,1' " +
                                "with no trailing comma.";
                        return false;
                    }
                    if (!float.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ||
                        float.IsNaN(v) || float.IsInfinity(v))
                    {
                        error = $"times entry '{entry}' is not a number. times takes NORMALIZED values in " +
                                "0..1 (0 = clip start, 1 = clip end) separated by commas, e.g. '0,0.25,0.5,1'.";
                        return false;
                    }
                    if (v < 0f || v > 1f)
                    {
                        error = $"times entry '{entry}' is outside 0..1. These are NORMALIZED times, not " +
                                "seconds: 0 is the clip start and 1 its end whatever clip.length happens to " +
                                "be. Out-of-range values are refused rather than clamped, because a clamped " +
                                "time silently duplicates the first or last frame.";
                        return false;
                    }
                    values.Add(v);
                }

                if (values.Count > MaxFrames)
                {
                    error = $"times lists {values.Count} samples, over the maximum of {MaxFrames}. Past 16 " +
                            "cells each one is too small to read a pose out of; split the sweep across two " +
                            "calls instead.";
                    return false;
                }

                normalized = values.ToArray();
                note = $"times was given ({normalized.Length} explicit sample(s)), so frameCount={frameCount} " +
                       "was ignored. Cells follow the order you wrote, not sorted order.";
                return true;
            }

            if (frameCount < 1)
            {
                error = $"frameCount must be at least 1 (got {frameCount}).";
                return false;
            }
            if (frameCount > MaxFrames)
            {
                error = $"frameCount={frameCount} exceeds the maximum of {MaxFrames}. A 16-cell sheet at " +
                        "cellSize=384 is already 1536x1536 and finer cells stop being readable — take two " +
                        "sheets with explicit 'times' ranges instead.";
                return false;
            }

            normalized = new float[frameCount];
            if (frameCount == 1)
            {
                normalized[0] = 0f;
                note = "frameCount=1, so only the clip start (normalized 0) was sampled. Pass 2 or more, or " +
                       "an explicit 'times' list, to see the motion.";
            }
            else
            {
                for (int i = 0; i < frameCount; i++)
                    normalized[i] = (float)i / (frameCount - 1);
            }
            return true;
        }

        private static int DistinctCount(float[] values)
        {
            var seen = new HashSet<float>();
            for (int i = 0; i < values.Length; i++) seen.Add(values[i]);
            return seen.Count;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Camera placement
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the camera SIDE: the returned vector is the offset direction FROM the subject TO the
        /// camera, so the camera sits at <c>center + dir * distance</c> and looks back at the subject.
        ///
        /// Stated that way on purpose. The sign convention is the one thing a reader cannot recover from the
        /// image, and getting it backwards produces a mirrored sheet that still looks correct — 'front'
        /// returning the back of the head is a plausible picture of the wrong thing.
        /// </summary>
        private static bool TryResolveAngle(string angle, out Vector3 dir, out string label, out string error)
        {
            dir = Vector3.forward;
            label = null;
            error = null;

            string a = (angle ?? string.Empty).Trim();
            if (a.Length == 0)
            {
                label = "front (camera on +Z, looking toward -Z)";
                return true;
            }

            switch (a.ToLowerInvariant())
            {
                case "front":
                    dir = new Vector3(0f, 0f, 1f);
                    label = "front (camera on +Z, looking toward -Z — the face of a +Z-facing avatar)";
                    return true;
                case "back":
                    dir = new Vector3(0f, 0f, -1f);
                    label = "back (camera on -Z, looking toward +Z)";
                    return true;
                case "right":
                    dir = new Vector3(1f, 0f, 0f);
                    label = "right (camera on +X — the right flank of a +Z-facing avatar)";
                    return true;
                case "left":
                    dir = new Vector3(-1f, 0f, 0f);
                    label = "left (camera on -X — the left flank of a +Z-facing avatar)";
                    return true;
                case "top":
                    dir = new Vector3(0f, 1f, 0f);
                    label = "top (camera above, looking straight down)";
                    return true;
                case "bottom":
                    dir = new Vector3(0f, -1f, 0f);
                    label = "bottom (camera below, looking straight up)";
                    return true;
                case "45left":
                    dir = new Vector3(-1f, 0f, 1f).normalized;
                    label = "45left (camera on the front-left, yaw -45)";
                    return true;
                case "45right":
                    dir = new Vector3(1f, 0f, 1f).normalized;
                    label = "45right (camera on the front-right, yaw +45)";
                    return true;
            }

            // Numeric 'yaw' or 'yaw,pitch' in degrees.
            var parts = a.Split(',');
            if (parts.Length == 1 || parts.Length == 2)
            {
                float yaw, pitch = 0f;
                bool ok = float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                         out yaw);
                if (ok && parts.Length == 2)
                    ok = float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                        out pitch);

                if (ok && !float.IsNaN(yaw) && !float.IsInfinity(yaw) &&
                    !float.IsNaN(pitch) && !float.IsInfinity(pitch))
                {
                    if (pitch < -89f || pitch > 89f)
                    {
                        error = $"angle pitch {Fmt3(pitch)} is outside -89..89. At exactly +-90 the view " +
                                "direction is parallel to world up and the camera's roll is undefined — use " +
                                "angle='top' or angle='bottom' for straight down / straight up.";
                        return false;
                    }

                    float yawRad = yaw * Mathf.Deg2Rad;
                    float pitchRad = pitch * Mathf.Deg2Rad;
                    dir = new Vector3(Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                                      Mathf.Sin(pitchRad),
                                      Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)).normalized;
                    label = $"yaw {Fmt3(yaw)} / pitch {Fmt3(pitch)} degrees (yaw 0 = camera on +Z = front, " +
                            "+yaw toward +X, +pitch above the subject)";
                    return true;
                }
            }

            error = $"angle '{angle}' is not understood. Use one of front, back, left, right, top, bottom, " +
                    "45left, 45right, or degrees as 'yaw' / 'yaw,pitch' (yaw 0 = front, +yaw swings toward " +
                    "+X, +pitch lifts the camera above the subject).";
            return false;
        }

        /// <summary>
        /// LookRotation with a reference up vector that survives looking straight up or down, where
        /// Vector3.up is parallel to the view direction and Quaternion.LookRotation has no defined roll.
        /// </summary>
        private static Quaternion LookRotationTowards(Vector3 forward)
        {
            if (forward.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 f = forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(f, up);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Bounds
        // ─────────────────────────────────────────────────────────────────────────

        private static bool TryComputeWorldBounds(List<Renderer> renderers, out Bounds bounds)
        {
            bounds = new Bounds();
            bool have = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (!TryRendererWorldBounds(renderers[i], out Bounds b)) continue;
                if (!have) { bounds = b; have = true; }
                else bounds.Encapsulate(b);
            }
            return have;
        }

        /// <summary>
        /// World-space AABB for one renderer at the pose the scene is currently in.
        ///
        /// For an ACTIVE renderer this is Renderer.bounds, the box Unity itself culls with, which follows the
        /// sampled pose — that is what makes the union over all sample times track the motion. Unity does not
        /// maintain that box for a renderer on an inactive GameObject, so those fall back to the mesh's own
        /// bounds pushed through the transform: framing a subject that is currently hidden is still better
        /// than reporting 'no size' and refusing to render.
        /// </summary>
        private static bool TryRendererWorldBounds(Renderer r, out Bounds bounds)
        {
            bounds = new Bounds();
            if (r == null) return false;

            if (r.gameObject.activeInHierarchy)
            {
                Bounds live = r.bounds;
                if (live.size.sqrMagnitude > 1e-10f)
                {
                    bounds = live;
                    return true;
                }
            }

            Mesh mesh = null;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                mesh = smr.sharedMesh;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null) return false;

            Bounds local = mesh.bounds;
            Matrix4x4 m = r.transform.localToWorldMatrix;
            Vector3 min = local.min, max = local.max;
            Vector3 worldMin = Vector3.zero, worldMax = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x,
                                         (i & 2) == 0 ? min.y : max.y,
                                         (i & 4) == 0 ? min.z : max.z);
                Vector3 w = m.MultiplyPoint3x4(corner);
                if (i == 0) { worldMin = w; worldMax = w; }
                else { worldMin = Vector3.Min(worldMin, w); worldMax = Vector3.Max(worldMax, w); }
            }
            bounds.SetMinMax(worldMin, worldMax);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Lighting
        // ─────────────────────────────────────────────────────────────────────────

        private static bool TryResolveLighting(string lighting, out bool neutral, out string error)
        {
            neutral = false;
            error = null;
            string l = (lighting ?? "scene").Trim();
            if (l.Length == 0 || l.Equals("scene", StringComparison.OrdinalIgnoreCase)) return true;
            if (l.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            {
                neutral = true;
                return true;
            }
            error = $"lighting '{lighting}' is not understood — use 'scene' (the scene's own lights) or " +
                    "'neutral' (adds temporary key + fill directional lights for this capture only).";
            return false;
        }

        /// <summary>
        /// Two temporary directional lights, oriented relative to the capture camera, so an unlit or
        /// night-time scene still yields a readable silhouette.
        ///
        /// Deliberately does NOT touch RenderSettings (ambient light, skybox) the way a fuller 'neutral'
        /// mode could: those are serialized scene state, and modifying them would leave the scene marked
        /// dirty even after a correct restore. Lights are objects, so they can simply be deleted again.
        /// Returns null (and logs) if creation fails; the caller then reports scene lighting only.
        /// </summary>
        private static GameObject CreateNeutralLights(Quaternion cameraRotation, out string note)
        {
            note = null;
            GameObject root = null;
            try
            {
                root = new GameObject("__AnimationFramesCaptureLights");
                root.hideFlags = HideFlags.HideAndDontSave;

                // Key over the camera's upper left, fill from the opposite side at a third of the intensity.
                AddDirectionalLight(root, cameraRotation * Quaternion.Euler(30f, -25f, 0f), 1.1f,
                                    new Color(1f, 0.98f, 0.95f));
                AddDirectionalLight(root, cameraRotation * Quaternion.Euler(-15f, 150f, 0f), 0.35f,
                                    new Color(0.9f, 0.94f, 1f));

                note = "lighting='neutral': two temporary directional lights (key + fill, parented to a " +
                       "HideAndDontSave object) were added for this capture and destroyed again. No ambient " +
                       "light, skybox or existing light was modified, so an unlit / toon shader that ignores " +
                       "directional light — or an HDRP scene, whose lights need HDAdditionalLightData — may " +
                       "look unchanged. They also lit the SceneView for the moment they existed.";
                return root;
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"CaptureAnimationFrames: could not create the neutral lights: {ex.Message}");
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
        }

        private static void AddDirectionalLight(GameObject parent, Quaternion rotation, float intensity,
                                                Color color)
        {
            var go = new GameObject("Light");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(parent.transform, false);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;   // a temp light casting shadows only adds noise to a small cell
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Sheet assembly
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bottom-left corner of cell <paramref name="index"/> in Texture2D coordinates. Cells read
        /// left-to-right, TOP-to-bottom, which in a bottom-left-origin texture means the first row sits at
        /// the highest y — forget that flip and the sheet comes back with its rows in reverse order while
        /// every label still looks right.
        /// </summary>
        private static void CellOrigin(int index, int cols, int rows, int cellSize, out int x, out int y)
        {
            int col = index % cols;
            int row = rows - 1 - (index / cols);
            x = col * cellSize;
            y = row * cellSize;
        }

        private static void FillSheetBackground(Texture2D sheet, CaptureBackgroundMode mode, Color color)
        {
            Color fill;
            switch (mode)
            {
                case CaptureBackgroundMode.Transparent: fill = new Color(0f, 0f, 0f, 0f); break;
                case CaptureBackgroundMode.SolidColor: fill = new Color(color.r, color.g, color.b, 1f); break;
                default: fill = DefaultBackdrop; break;
            }

            // Color32 rather than Color: a 4x4 sheet of 384px cells is 2.4M pixels, and the float array would
            // be 37MB of transient garbage for a fill that only needs 4 bytes per pixel.
            var pixels = new Color32[sheet.width * sheet.height];
            Color32 packed = fill;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = packed;
            sheet.SetPixels32(pixels);
        }

        /// <summary>
        /// Longest time label that fits the cell: index + normalized + seconds, falling back to index +
        /// normalized, then to the bare index. Returns null when not even the index fits, so the caller can
        /// report the labels as missing instead of promising a labelled sheet.
        /// </summary>
        private static string BuildCellLabel(int index, float normalized, float seconds, int cellSize,
                                             int scale, out bool includesSeconds)
        {
            includesSeconds = false;
            string full = $"[{index}] T{Fmt2(normalized)} {Fmt2(seconds)}S";
            string medium = $"[{index}] T{Fmt2(normalized)}";
            string minimal = $"[{index}]";

            if (Fits(full, scale, cellSize)) { includesSeconds = true; return full; }
            if (Fits(medium, scale, cellSize)) return medium;
            if (Fits(minimal, scale, cellSize)) return minimal;
            return null;
        }

        private static bool Fits(string text, int scale, int cellSize)
        {
            CaptureCommon.MeasureText(text, scale, out int w, out int h);
            // LabelMargin on the left, the same again on the right, plus the plate padding on both sides.
            return w + LabelMargin * 2 + 4 <= cellSize && h + LabelMargin * 2 + 4 <= cellSize;
        }

        /// <summary>
        /// FNV-1a over the cell's pixels plus a flat-frame test. The checksum is what lets the result say
        /// 'every cell is pixel-identical', which is the observable symptom of a clip that does not address
        /// the target — and the one thing that stops such a sheet from being reported as a clean success.
        /// </summary>
        private static void Probe(Color32[] pixels, out ulong checksum, out bool flat)
        {
            checksum = FnvOffsetBasis;
            flat = true;
            if (pixels == null || pixels.Length == 0) return;

            Color32 first = pixels[0];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (flat && (p.r != first.r || p.g != first.g || p.b != first.b || p.a != first.a))
                    flat = false;
                checksum = (checksum ^ (ulong)p.r) * FnvPrime;
                checksum = (checksum ^ (ulong)p.g) * FnvPrime;
                checksum = (checksum ^ (ulong)p.b) * FnvPrime;
                checksum = (checksum ^ (ulong)p.a) * FnvPrime;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Reporting
        // ─────────────────────────────────────────────────────────────────────────

        private static string DescribeClip(AnimationClip clip, int bindingCount)
        {
            string kind = clip.legacy ? "legacy" : "Mecanim (generic/humanoid)";
            string bindings = bindingCount >= 0
                ? $"{bindingCount} curve bindings"
                : "curve bindings: unknown (AnimationUtility refused the query)";
            return $" Clip '{clip.name}': length {Fmt3(clip.length)}s, {Fmt3(clip.frameRate)} fps, {kind}, " +
                   $"{bindings}.";
        }

        private static string DescribeFrames(float[] normalized, AnimationClip clip)
        {
            var sb = new StringBuilder(" Frames (normalized time = seconds");
            bool hasFrameRate = clip.frameRate > 0f && !float.IsNaN(clip.frameRate) &&
                                !float.IsInfinity(clip.frameRate);
            if (hasFrameRate) sb.Append(", clip frame");
            sb.Append("): ");

            for (int i = 0; i < normalized.Length; i++)
            {
                if (i > 0) sb.Append("; ");
                float seconds = normalized[i] * clip.length;
                sb.Append($"[{i}] {Fmt3(normalized[i])} = {Fmt3(seconds)}s");
                if (hasFrameRate) sb.Append($" (frame {Mathf.RoundToInt(seconds * clip.frameRate)})");
            }
            sb.Append('.');
            return sb.ToString();
        }

        private static string HierarchyPath(GameObject go)
        {
            if (go == null) return "<null>";
            var sb = new StringBuilder(go.name);
            Transform t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }

        private static string Fmt2(float value)
            => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Fmt3(float value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
