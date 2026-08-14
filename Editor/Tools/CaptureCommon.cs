using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Which of the two capture routes produced an image. Every capture tool must report this,
    /// because the two routes return genuinely different pictures of the same subject:
    ///
    /// - <see cref="Render"/>: a camera drawn into a RenderTexture. Free resolution, no focus stealing,
    ///   clean output — but no gizmos, no grid, no selection outline, no editor chrome.
    /// - <see cref="Window"/>: the OS window bitmap. Exactly what the user sees, at the window's own
    ///   size, including overlays — but nothing about it can be re-framed after the fact.
    ///
    /// Callers that fall back from one route to the other MUST pass the route they actually used.
    /// Reporting the requested route instead of the used one is the specific lie this type exists to prevent.
    /// </summary>
    internal static class CaptureRoute
    {
        internal const string Render = "render";
        internal const string Window = "window";
        internal const string Unknown = "unknown";
    }

    /// <summary>How <see cref="CaptureOptions.Background"/> was interpreted.</summary>
    internal enum CaptureBackgroundMode
    {
        /// <summary>Leave the scene / window background exactly as it is.</summary>
        Scene,
        /// <summary>Clear to alpha 0 and keep the alpha channel through encoding. PNG only.</summary>
        Transparent,
        /// <summary>Clear to an explicit opaque colour parsed from "#RRGGBB".</summary>
        SolidColor,
    }

    /// <summary>
    /// The knobs every capture tool shares: downscale, container format, output path, crop, background
    /// and MSAA. Bundled into one object so a new option added here reaches all capture tools at once
    /// instead of being threaded through a dozen signatures by hand.
    ///
    /// Field defaults are the tool defaults; a tool that exposes fewer knobs simply leaves the rest alone.
    /// Nothing here is validated on assignment — call <see cref="Validate"/> (or just hand the object to
    /// <see cref="CaptureCommon.Finish"/>, which validates first) so that bad combinations surface as one
    /// clear error instead of a silently wrong image.
    /// </summary>
    internal sealed class CaptureOptions
    {
        /// <summary>Upper bound on the LONGER side of the output, aspect preserved. 0 disables downscaling.</summary>
        public int MaxWidth;

        /// <summary>"png" (lossless, keeps alpha) or "jpg" (smaller, no alpha).</summary>
        public string Format = "png";

        /// <summary>1-100, JPG only. Ignored for PNG.</summary>
        public int JpgQuality = 90;

        /// <summary>Optional explicit output file. Empty means "attach only, plus the rolling debug dump".</summary>
        public string SaveToPath = "";

        /// <summary>
        /// "x,y,w,h" in pixels, origin BOTTOM-LEFT — the same convention as DiffImages.maskRegion, so a
        /// rectangle measured once can be reused between capturing and diffing. Out-of-range rectangles are
        /// an error, never a silent clamp: a clamped crop would return a different area than asked for and
        /// the caller would read the result as if it covered their rectangle.
        /// </summary>
        public string CropRegion = "";

        /// <summary>"scene" (untouched), "transparent" (alpha 0, PNG only) or "#RRGGBB".</summary>
        public string Background = "scene";

        /// <summary>MSAA sample count: 1, 2, 4 or 8. Default 2 — cheap, and removes the jagged edges that make thin geometry unreadable.</summary>
        public int AntiAliasing = 2;

        internal CaptureOptions() { }

        /// <summary>Convenience factory mirroring the argument order capture tools expose to callers.</summary>
        internal static CaptureOptions Create(int maxWidth, string format, int jpgQuality, string saveToPath,
                                              string cropRegion = "", string background = "scene",
                                              int antiAliasing = 2)
        {
            return new CaptureOptions
            {
                MaxWidth = maxWidth,
                Format = string.IsNullOrWhiteSpace(format) ? "png" : format,
                JpgQuality = jpgQuality,
                SaveToPath = saveToPath ?? "",
                CropRegion = cropRegion ?? "",
                Background = string.IsNullOrWhiteSpace(background) ? "scene" : background,
                AntiAliasing = antiAliasing,
            };
        }

        internal string NormalizedFormat
        {
            get
            {
                string f = (Format ?? "png").Trim().ToLowerInvariant();
                return f.Length == 0 ? "png" : f;
            }
        }

        internal bool IsJpg
        {
            get { string f = NormalizedFormat; return f == "jpg" || f == "jpeg"; }
        }

        internal string MimeType => IsJpg ? "image/jpeg" : "image/png";

        internal string Extension => IsJpg ? ".jpg" : ".png";

        internal int ClampedJpgQuality => Mathf.Clamp(JpgQuality, 1, 100);

        internal bool HasCropRegion => !string.IsNullOrWhiteSpace(CropRegion);

        /// <summary>True when the caller asked for an alpha channel, i.e. Background == "transparent".</summary>
        internal bool WantsTransparency
            => string.Equals((Background ?? "").Trim(), "transparent", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Checks everything that can be checked without knowing the image size: format name, background
        /// syntax, the transparent+jpg contradiction and the MSAA sample count. Crop bounds need the actual
        /// resolution and are therefore checked later, inside <see cref="CaptureCommon.Finish"/>.
        /// </summary>
        internal bool Validate(out string error)
        {
            error = null;

            string f = NormalizedFormat;
            if (f != "png" && f != "jpg" && f != "jpeg")
            {
                error = $"format '{Format}' is not supported — use 'png' or 'jpg'.";
                return false;
            }

            if (!CaptureCommon.TryParseBackground(Background, out var mode, out _, out string bgError))
            {
                error = bgError;
                return false;
            }

            // JPG has no alpha channel, so a transparent request would come back as an opaque black (or
            // white) frame that looks like a successful capture. Refuse the combination instead.
            if (mode == CaptureBackgroundMode.Transparent && IsJpg)
            {
                error = "background='transparent' requires format='png' — JPG cannot store an alpha channel, " +
                        "so the transparency would be silently flattened.";
                return false;
            }

            if (!CaptureCommon.TryResolveAntiAliasing(AntiAliasing, out _, out string aaError))
            {
                error = aaError;
                return false;
            }

            if (HasCropRegion && !CaptureCommon.TryParseCropRegionSyntax(CropRegion, out _, out _, out _, out _, out string cropError))
            {
                error = cropError;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The single exit taken by every capture tool: crop, downscale, encode, attach, dump, save, and
    /// describe what actually happened.
    ///
    /// This used to be two near-identical private helpers (SceneViewTools.EncodeWithOptions and
    /// WindowCaptureTools.EncodeAndAttach) which drifted apart — one grew maxWidth handling, the other
    /// grew saveToPath, and neither reported which capture route produced the picture. Every new option
    /// had to be implemented twice, and any tool that picked the wrong helper quietly lost half of them.
    ///
    /// Two rules hold throughout:
    /// - A failure is never dressed up as success. Every method here returns null plus a filled error
    ///   rather than an empty or black image.
    /// - GPU and native resources are released in finally, including on the exception paths.
    /// </summary>
    internal static class CaptureCommon
    {
        /// <summary>
        /// Path of the numbered %TEMP% dump written for the capture that just finished, or null if the dump
        /// could not be written. AI clients that do not render MCP image attachments inline can Read this
        /// file to actually see the picture.
        ///
        /// The dump itself is written by <see cref="SceneViewTools.SetPendingImage"/> — one implementation,
        /// one directory, one 20-file retention window. Writing a second copy from here would halve that
        /// window and make the same capture appear twice in a directory listing, so this property only
        /// mirrors the path that call produced.
        /// </summary>
        internal static string LastCaptureDebugPath { get; private set; }

        // ─────────────────────────────────────────────────────────────────────────
        // Public entry points
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finishes a capture that already exists as a Texture2D (the render route).
        ///
        /// Returns the success message on success, or null with <paramref name="error"/> filled. Callers are
        /// expected to write <c>if (msg == null) return $"Error: {error}";</c> — the split exists so a caller
        /// can add its own context to the failure instead of relaying a pre-formatted sentence.
        ///
        /// <paramref name="route"/> must be the route actually used (see <see cref="CaptureRoute"/>), not the
        /// one requested. <paramref name="label"/> is the human description of the subject, e.g.
        /// "SceneView" or "EditorWindow 'Inspector'".
        ///
        /// Ownership: the source texture is NOT destroyed unless <paramref name="destroySource"/> is true,
        /// matching the previous EncodeWithOptions contract. Intermediates created here are always released.
        /// </summary>
        internal static string Finish(Texture2D tex, CaptureOptions opt, string label, string route,
                                     out string error, bool destroySource = false)
        {
            error = null;
            if (tex == null)
            {
                error = "capture produced no texture (nothing was rendered).";
                return null;
            }

            try
            {
                return FinishCore(tex, opt, label, route, out error);
            }
            finally
            {
                if (destroySource) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Finishes a capture that arrived as a raw BGRA32 buffer with bottom-up rows — the shape
        /// WindowCaptureNative returns (the window route).
        ///
        /// The alpha byte of a GDI 32bpp DIB is undefined, so unless the caller explicitly asked for
        /// transparency the alpha channel is normalised to opaque IN PLACE in <paramref name="bgra"/>.
        /// Without that, a driver that happens to return zero alpha would produce a fully transparent PNG
        /// that reads as a successful capture of nothing. The buffer is expected to be a fresh capture
        /// buffer; do not pass one you intend to reuse unmodified.
        ///
        /// background="transparent" is rejected here: an OS window bitmap has no meaningful alpha to keep.
        /// </summary>
        internal static string FinishFromBgra(byte[] bgra, int w, int h, CaptureOptions opt, string label,
                                              string route, out string error)
        {
            error = null;
            if (bgra == null || bgra.Length == 0)
            {
                error = "capture produced no pixel data.";
                return null;
            }
            if (w <= 0 || h <= 0)
            {
                error = $"capture reported an invalid size ({w}x{h}).";
                return null;
            }

            long expected = (long)w * h * 4;
            if (bgra.Length < expected)
            {
                error = $"pixel buffer is {bgra.Length} bytes but {w}x{h} BGRA32 needs {expected}.";
                return null;
            }

            opt = opt ?? new CaptureOptions();
            if (opt.WantsTransparency)
            {
                error = "background='transparent' is not available on the window route — an OS window bitmap " +
                        "carries no usable alpha channel. Capture the same subject through a camera " +
                        "(render route) if you need transparency.";
                return null;
            }

            // Undefined DIB alpha would encode as an invisible PNG. Force opacity.
            int total = (int)expected;
            for (int i = 3; i < total; i += 4) bgra[i] = 255;

            // LoadRawTextureData wants an exactly sized buffer; a longer one (padded stride, reused scratch
            // buffer) is trimmed here rather than being rejected further down with an opaque Unity message.
            byte[] raw = bgra;
            if (bgra.Length != total)
            {
                raw = new byte[total];
                Buffer.BlockCopy(bgra, 0, raw, 0, total);
            }

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(w, h, TextureFormat.BGRA32, false);
                tex.LoadRawTextureData(raw);
                tex.Apply(false, false);
                return FinishCore(tex, opt, label, route, out error);
            }
            catch (Exception ex)
            {
                error = $"failed to build a texture from the captured pixels: {ex.Message}";
                return null;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Core pipeline
        // ─────────────────────────────────────────────────────────────────────────

        private static string FinishCore(Texture2D source, CaptureOptions opt, string label, string route,
                                         out string error)
        {
            error = null;
            opt = opt ?? new CaptureOptions();

            if (!opt.Validate(out error)) return null;

            int srcW = source.width, srcH = source.height;

            RectInt crop = new RectInt(0, 0, srcW, srcH);
            bool cropped = false;
            if (opt.HasCropRegion)
            {
                if (!TryParseCropRegion(opt.CropRegion, srcW, srcH, out crop, out error)) return null;
                cropped = crop.x != 0 || crop.y != 0 || crop.width != srcW || crop.height != srcH;
            }

            Texture2D cropTex = null;
            try
            {
                Texture2D staged = source;
                if (cropped)
                {
                    cropTex = CropTexture(source, crop, opt.WantsTransparency, out error);
                    if (cropTex == null) return null;
                    staged = cropTex;
                }

                int preW = staged.width, preH = staged.height;

                // Downscale + encode live in Encode() rather than here, because
                // SceneViewTools.EncodeWithOptions needs exactly that half (bytes, no attach/save/report)
                // and a second copy of it is precisely how maxWidth handling and saveToPath drifted apart
                // between the two original helpers.
                byte[] bytes = Encode(staged, opt, out _, out int outW, out int outH, out error);
                if (bytes == null) return null;

                Attach(bytes, opt.MimeType);
                string debugPath = LastCaptureDebugPath;

                string saveNote = string.Empty;
                if (!string.IsNullOrWhiteSpace(opt.SaveToPath))
                {
                    if (TryWriteFile(opt.SaveToPath, bytes, out string saveError))
                        saveNote = $" Saved to '{opt.SaveToPath}'.";
                    else
                        saveNote = $" WARNING: saveToPath failed ({saveError}) — the image is attached but was not written to that path.";
                }

                var sb = new StringBuilder();
                sb.Append("Success: Captured ").Append(string.IsNullOrWhiteSpace(label) ? "image" : label);
                sb.Append(" (route=").Append(NormalizeRoute(route));
                sb.Append(", output ").Append(outW).Append('x').Append(outH);
                sb.Append(", source ").Append(srcW).Append('x').Append(srcH);
                if (cropped)
                    sb.Append($", cropped to {crop.width}x{crop.height} at x={crop.x},y={crop.y} (bottom-left origin)");
                if (outW != preW || outH != preH)
                    sb.Append($", downscaled to fit maxWidth={opt.MaxWidth}");
                sb.Append(", ").Append(bytes.Length).Append(" bytes, ").Append(opt.NormalizedFormat);
                if (opt.WantsTransparency) sb.Append(", background=transparent (alpha preserved)");
                sb.Append("). The image has been attached for your review.");
                sb.Append(saveNote);
                if (!string.IsNullOrEmpty(debugPath)) sb.Append($" Debug copy at '{debugPath}'.");
                else sb.Append(" Debug copy: unavailable (could not write to the temp dump directory).");
                return sb.ToString();
            }
            finally
            {
                if (cropTex != null) UnityEngine.Object.DestroyImmediate(cropTex);
            }
        }

        /// <summary>
        /// The single encoder: optional bilinear downscale so the LONGER side fits
        /// <see cref="CaptureOptions.MaxWidth"/>, then PNG or JPG bytes.
        ///
        /// <paramref name="outWidth"/> / <paramref name="outHeight"/> are the resolution of the image that
        /// was ACTUALLY encoded, which differs from the source whenever maxWidth kicked in. Callers must
        /// report these and not what they asked for: saying "2048x2048" while attaching a 512x512 PNG makes
        /// every pixel coordinate later derived from that image (DiffImages maskRegion, cropRegion, "the seam
        /// is at x=1400") point somewhere else.
        ///
        /// Returns null with <paramref name="error"/> filled on failure — never a partial or empty buffer.
        /// The source texture is left alone; the caller still owns it. Intermediates are released in finally.
        /// Nothing is attached, saved or dumped here: that is <see cref="Finish"/>'s job, so this method can
        /// be reused by callers that only want the bytes.
        /// </summary>
        internal static byte[] Encode(Texture2D tex, CaptureOptions opt, out string mime,
                                      out int outWidth, out int outHeight, out string error)
        {
            error = null;
            opt = opt ?? new CaptureOptions();
            mime = opt.MimeType;
            outWidth = 0;
            outHeight = 0;

            if (tex == null)
            {
                error = "there is no texture to encode.";
                return null;
            }

            outWidth = tex.width;
            outHeight = tex.height;

            int targetW = tex.width, targetH = tex.height;
            if (opt.MaxWidth > 0)
            {
                int longer = Mathf.Max(targetW, targetH);
                if (longer > opt.MaxWidth)
                {
                    float scale = (float)opt.MaxWidth / longer;
                    targetW = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
                    targetH = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
                }
            }

            Texture2D resized = null;
            RenderTexture rt = null;
            try
            {
                Texture2D toEncode = tex;
                if (targetW != tex.width || targetH != tex.height)
                {
                    rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Bilinear;
                    var prevActive = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(tex, rt);
                        RenderTexture.active = rt;
                        // RGBA32 unconditionally: dropping to RGB24 here would silently discard the alpha
                        // channel that background='transparent' exists to produce.
                        resized = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
                        resized.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                        resized.Apply(false, false);
                    }
                    finally
                    {
                        RenderTexture.active = prevActive;
                    }
                    toEncode = resized;
                }

                byte[] bytes;
                try
                {
                    bytes = opt.IsJpg ? toEncode.EncodeToJPG(opt.ClampedJpgQuality) : toEncode.EncodeToPNG();
                }
                catch (Exception ex)
                {
                    error = $"failed to encode the image as {opt.NormalizedFormat}: {ex.Message}";
                    return null;
                }
                if (bytes == null || bytes.Length == 0)
                {
                    error = $"failed to encode the image as {opt.NormalizedFormat} (the encoder returned no data).";
                    return null;
                }

                outWidth = toEncode.width;
                outHeight = toEncode.height;
                return bytes;
            }
            catch (Exception ex)
            {
                error = $"failed to downscale the image to maxWidth={opt.MaxWidth}: {ex.Message}";
                return null;
            }
            finally
            {
                if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Makes the encoded bytes the pending MCP image attachment, which also writes the numbered %TEMP%
        /// debug dump, and records the resulting path in <see cref="LastCaptureDebugPath"/>.
        ///
        /// SceneViewTools keeps the previous path when a dump fails to write, so the path is only accepted
        /// here when it actually changed: every successful dump gets a fresh name, so an unchanged path means
        /// the write failed and reporting it would point the caller at somebody else's image.
        /// </summary>
        internal static void Attach(byte[] bytes, string mimeType)
        {
            if (bytes == null || bytes.Length == 0)
            {
                LastCaptureDebugPath = null;
                return;
            }

            // SceneViewTools owns both the pending-attachment state the MCP layer reads and the dump.
            // dumpDebugCopy is passed explicitly and is true on purpose: everything arriving here IS a
            // capture, so it belongs in the rolling capture directory. Non-captures (chat attachments,
            // generated textures) call SetPendingImage directly with false so they cannot push real
            // captures out of the 20-file retention window that before/after comparison depends on.
            string before = SceneViewTools.LastCaptureDebugPath;
            SceneViewTools.SetPendingImage(bytes, mimeType, dumpDebugCopy: true);
            string after = SceneViewTools.LastCaptureDebugPath;

            if (!string.IsNullOrEmpty(after) && !string.Equals(after, before, StringComparison.Ordinal))
            {
                LastCaptureDebugPath = after;
            }
            else
            {
                LastCaptureDebugPath = null;
                AgentLogger.Warning(LogTag.Tool,
                    "CaptureCommon: the capture was attached but no debug dump was written " +
                    "(SceneViewTools.LastCaptureDebugPath did not advance).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Crop
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Flips a rectangle between the two origins this codebase uses, and is the ONLY place that
        /// conversion may be written.
        ///
        /// - Window / Win32 / UI Toolkit space: origin TOP-left, y grows downward. That is what
        ///   <c>EditorWindow.position</c>, <c>VisualElement.worldBound</c> and CaptureEditorWindow's
        ///   <c>region</c> argument are expressed in.
        /// - Image / Unity space: origin BOTTOM-left, y grows upward. That is what <c>Texture2D</c> indexing,
        ///   <c>cropRegion</c> and DiffImages' <c>maskRegion</c> are expressed in.
        ///
        /// The function is its own inverse, so the same call converts either way given the matching height.
        /// <paramref name="texHeight"/> must be the height of the image the rectangle is measured against; a
        /// height from a different (e.g. pre-downscale) image flips the rectangle to the wrong row band.
        ///
        /// Why centralise something this small: a tool that writes <c>height - y</c> by hand and forgets to
        /// subtract the rectangle's own height lands one box-height off, and a tool that forgets the flip
        /// entirely annotates the mirrored half of the picture. Both come back as a successful capture with
        /// boxes in plausible-looking wrong places, which is far harder to notice than an error.
        /// </summary>
        internal static Rect RectTopLeftToBottomLeft(Rect r, int texHeight)
            => new Rect(r.x, texHeight - r.y - r.height, r.width, r.height);

        /// <summary>
        /// Parses "x,y,w,h" and checks it fits inside <paramref name="width"/> x <paramref name="height"/>.
        /// Origin is BOTTOM-LEFT. An empty string yields the full image and true.
        ///
        /// Note the deliberate asymmetry with CaptureEditorWindow's <c>region</c>, which is TOP-LEFT based
        /// because window coordinates are: crop happens in image space (Unity convention), region happens in
        /// window space (Win32 convention). Mixing the two silently crops the wrong half of the picture.
        /// </summary>
        internal static bool TryParseCropRegion(string cropRegion, int width, int height,
                                                out RectInt rect, out string error)
        {
            rect = new RectInt(0, 0, width, height);
            error = null;
            if (string.IsNullOrWhiteSpace(cropRegion)) return true;

            if (!TryParseCropRegionSyntax(cropRegion, out int x, out int y, out int w, out int h, out error))
                return false;

            // Clamping would return a different area than requested while still reporting success.
            if (x < 0 || y < 0 || x + w > width || y + h > height)
            {
                error = $"cropRegion x={x} y={y} w={w} h={h} does not fit inside the {width}x{height} image " +
                        "(origin is bottom-left).";
                return false;
            }

            rect = new RectInt(x, y, w, h);
            return true;
        }

        /// <summary>Syntax-only half of <see cref="TryParseCropRegion"/>, usable before the size is known.</summary>
        internal static bool TryParseCropRegionSyntax(string cropRegion, out int x, out int y, out int w, out int h,
                                                      out string error)
        {
            x = 0; y = 0; w = 0; h = 0; error = null;
            if (string.IsNullOrWhiteSpace(cropRegion))
            {
                error = "cropRegion is empty.";
                return false;
            }

            var parts = cropRegion.Split(',');
            if (parts.Length != 4)
            {
                error = $"cropRegion '{cropRegion}' must be 'x,y,w,h' in pixels.";
                return false;
            }
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out w) ||
                !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out h))
            {
                error = $"cropRegion '{cropRegion}' must be four integers.";
                return false;
            }
            if (w <= 0 || h <= 0)
            {
                error = $"cropRegion width and height must be positive (got {w}x{h}).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Copies a sub-rectangle (bottom-left origin) into a fresh texture. Returns null with an error on
        /// failure — never a partly filled texture.
        /// </summary>
        private static Texture2D CropTexture(Texture2D src, RectInt rect, bool keepAlpha, out string error)
        {
            error = null;
            Color[] pixels = null;

            // GetPixels only accepts a handful of readable formats, and window captures arrive as BGRA32.
            // Try the direct read, and fall back to normalising through a RenderTexture when it is refused.
            try
            {
                if (SupportsGetPixels(src.format))
                    pixels = src.GetPixels(rect.x, rect.y, rect.width, rect.height);
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"CaptureCommon.CropTexture: direct GetPixels on {src.format} failed ({ex.Message}); " +
                    "falling back to a RenderTexture copy.");
                pixels = null;
            }

            if (pixels == null)
            {
                Texture2D normalized = null;
                RenderTexture rt = null;
                var prevActive = RenderTexture.active;
                try
                {
                    rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Point;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    normalized = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    normalized.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                    normalized.Apply(false, false);
                    pixels = normalized.GetPixels(rect.x, rect.y, rect.width, rect.height);
                }
                catch (Exception ex)
                {
                    error = $"cropRegion could not be applied: {ex.Message}";
                    return null;
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    if (normalized != null) UnityEngine.Object.DestroyImmediate(normalized);
                    if (rt != null) RenderTexture.ReleaseTemporary(rt);
                }
            }

            if (pixels == null || pixels.Length != rect.width * rect.height)
            {
                error = "cropRegion could not be applied (unexpected pixel count from the source texture).";
                return null;
            }

            Texture2D dst = null;
            try
            {
                dst = new Texture2D(rect.width, rect.height,
                    keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                dst.SetPixels(pixels);
                dst.Apply(false, false);
                return dst;
            }
            catch (Exception ex)
            {
                if (dst != null) UnityEngine.Object.DestroyImmediate(dst);
                error = $"cropRegion could not be applied: {ex.Message}";
                return null;
            }
        }

        private static bool SupportsGetPixels(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.RGB24:
                case TextureFormat.Alpha8:
                case TextureFormat.RGBAFloat:
                case TextureFormat.RGBAHalf:
                    return true;
                default:
                    return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Background / MSAA / render targets
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses "scene", "transparent" or "#RRGGBB" (with or without the '#'; "#RRGGBBAA" is accepted too).
        /// Unrecognised values are an error rather than a fallback to "scene", because a typo'd colour that
        /// silently keeps the scene background is indistinguishable from a working capture.
        /// </summary>
        internal static bool TryParseBackground(string background, out CaptureBackgroundMode mode,
                                                out Color color, out string error)
        {
            mode = CaptureBackgroundMode.Scene;
            color = Color.clear;
            error = null;

            string b = (background ?? "scene").Trim();
            if (b.Length == 0 || b.Equals("scene", StringComparison.OrdinalIgnoreCase))
                return true;

            if (b.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                mode = CaptureBackgroundMode.Transparent;
                color = new Color(0f, 0f, 0f, 0f);
                return true;
            }

            string hex = b.StartsWith("#", StringComparison.Ordinal) ? b : "#" + b;
            if (ColorUtility.TryParseHtmlString(hex, out Color parsed))
            {
                mode = CaptureBackgroundMode.SolidColor;
                color = parsed;
                return true;
            }

            error = $"background '{background}' is not understood — use 'scene', 'transparent' or '#RRGGBB'.";
            return false;
        }

        /// <summary>
        /// Accepts 1, 2, 4 or 8 — the sample counts Unity's RenderTexture supports — plus 0, which a caller
        /// that never filled the field in leaves behind and which is treated as "no MSAA" (1). Anything else
        /// is an error rather than a nearest-supported guess, because silently rendering at a different
        /// sample count than requested shows up as an unexplained quality difference between captures.
        /// </summary>
        internal static bool TryResolveAntiAliasing(int requested, out int samples, out string error)
        {
            samples = 1;
            error = null;
            if (requested == 1 || requested == 2 || requested == 4 || requested == 8)
            {
                samples = requested;
                return true;
            }
            if (requested == 0)
            {
                // 0 reads as "unset" from callers that never filled the field in; treat it as no MSAA.
                samples = 1;
                return true;
            }
            error = $"antiAliasing must be 1, 2, 4 or 8 (got {requested}).";
            return false;
        }

        /// <summary>
        /// Temporary render target honouring the option set's MSAA. Always ARGB32 so the transparent path has
        /// an alpha channel to write into. The caller MUST release it with RenderTexture.ReleaseTemporary in
        /// a finally block. Returns null with an error if the sample count is invalid.
        /// </summary>
        internal static RenderTexture GetTemporaryTarget(int width, int height, CaptureOptions opt,
                                                         out string error, int depthBits = 24)
        {
            error = null;
            if (width <= 0 || height <= 0)
            {
                error = $"render target size must be positive (got {width}x{height}).";
                return null;
            }
            int samples = 1;
            if (opt != null && !TryResolveAntiAliasing(opt.AntiAliasing, out samples, out error)) return null;

            return RenderTexture.GetTemporary(width, height, depthBits, RenderTextureFormat.ARGB32,
                                              RenderTextureReadWrite.Default, samples);
        }

        /// <summary>
        /// Reads a render target back into a Texture2D the caller owns (DestroyImmediate it).
        /// An MSAA target cannot be read directly, so it is resolved through a single-sample copy first.
        /// RenderTexture.active is restored in finally.
        /// </summary>
        internal static Texture2D ReadBack(RenderTexture rt, bool keepAlpha, out string error)
        {
            error = null;
            if (rt == null)
            {
                error = "no render target to read back.";
                return null;
            }

            RenderTexture resolve = null;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture readFrom = rt;
                if (rt.antiAliasing > 1)
                {
                    resolve = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(rt, resolve);
                    readFrom = resolve;
                }

                RenderTexture.active = readFrom;
                tex = new Texture2D(rt.width, rt.height,
                    keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply(false, false);
                return tex;
            }
            catch (Exception ex)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                error = $"failed to read the render target back: {ex.Message}";
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (resolve != null) RenderTexture.ReleaseTemporary(resolve);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Contact-sheet grid
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Grid shape for a contact sheet: up to 3 cells stay on one row (side-by-side comparison is the
        /// point at those counts), and above that the grid is as square as possible, filling left-to-right,
        /// top-to-bottom.
        ///
        /// NOT yet the single source of truth for sheet layout. The only caller today is
        /// CaptureAnimationFrames; CaptureMultiAngle, CaptureMeshIsolated and ScanAvatarMeshes in
        /// SceneViewTools still each compute cols/rows inline and disagree with this rule and with each
        /// other (e.g. count=7 gives 4x2 there but 3x3 here; count=4 gives 2x2 in both by coincidence).
        /// So do NOT derive a cell's position in a SceneViewTools sheet from this method — read the
        /// cols x rows that tool actually reports. Migrating those three call sites here is still open.
        /// </summary>
        internal static void ComputeGrid(int count, out int cols, out int rows)
        {
            if (count <= 0) { cols = 0; rows = 0; return; }
            if (count <= 3) { cols = count; rows = 1; return; }

            cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            rows = Mathf.CeilToInt((float)count / cols);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Overlay drawing (boxes + labels burned into the pixels)
        // ─────────────────────────────────────────────────────────────────────────
        //
        // Annotations are drawn straight into the Texture2D instead of through GUI/TextMesh/font assets,
        // because every capture path here runs outside an OnGUI context and often with no scene to put a
        // TextMesh into. A font asset would also make the label depend on what happens to be imported in
        // the user's project — the same tool would draw text in one project and nothing in another.
        //
        // ALL coordinates below are Texture2D coordinates: origin BOTTOM-LEFT, y grows upward. If the
        // rectangle you have came from a window or a VisualElement it is top-left based, so run it through
        // RectTopLeftToBottomLeft first. Everything is clipped to the texture; drawing partly or fully
        // outside is not an error, it just draws less (and the return value says whether anything landed).

        /// <summary>Glyph cell width in font pixels, before <c>scale</c>.</summary>
        internal const int GlyphWidth = 5;

        /// <summary>Glyph cell height in font pixels, before <c>scale</c>.</summary>
        internal const int GlyphHeight = 7;

        /// <summary>Horizontal advance per character in font pixels: the 5px cell plus a 1px gap.</summary>
        private const int GlyphAdvance = GlyphWidth + 1;

        // 5x7 dot matrix, one row per string, '#' = lit. Rows run TOP to BOTTOM (converted to the
        // bottom-left texture origin when drawn), so these literals read the same way up as the output.
        private static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
        {
            { '0', Glyph(".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.") },
            { '1', Glyph("..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.") },
            { '2', Glyph(".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####") },
            { '3', Glyph("#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###.") },
            { '4', Glyph("...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.") },
            { '5', Glyph("#####", "#....", "####.", "....#", "....#", "#...#", ".###.") },
            { '6', Glyph("..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###.") },
            { '7', Glyph("#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...") },
            { '8', Glyph(".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.") },
            { '9', Glyph(".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##..") },

            { 'A', Glyph(".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#") },
            { 'B', Glyph("####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.") },
            { 'C', Glyph(".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.") },
            { 'D', Glyph("####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.") },
            { 'E', Glyph("#####", "#....", "#....", "####.", "#....", "#....", "#####") },
            { 'F', Glyph("#####", "#....", "#....", "####.", "#....", "#....", "#....") },
            { 'G', Glyph(".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####") },
            { 'H', Glyph("#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#") },
            { 'I', Glyph(".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.") },
            { 'J', Glyph("..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..") },
            { 'K', Glyph("#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#") },
            { 'L', Glyph("#....", "#....", "#....", "#....", "#....", "#....", "#####") },
            { 'M', Glyph("#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#") },
            { 'N', Glyph("#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#") },
            { 'O', Glyph(".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.") },
            { 'P', Glyph("####.", "#...#", "#...#", "####.", "#....", "#....", "#....") },
            { 'Q', Glyph(".###.", "#...#", "#...#", "#...#", "#.#.#", ".###.", "....#") },
            { 'R', Glyph("####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#") },
            { 'S', Glyph(".####", "#....", "#....", ".###.", "....#", "....#", "####.") },
            { 'T', Glyph("#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..") },
            { 'U', Glyph("#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.") },
            { 'V', Glyph("#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..") },
            { 'W', Glyph("#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#") },
            { 'X', Glyph("#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#") },
            { 'Y', Glyph("#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..") },
            { 'Z', Glyph("#####", "....#", "...#.", "..#..", ".#...", "#....", "#####") },

            { ' ', Glyph(".....", ".....", ".....", ".....", ".....", ".....", ".....") },
            { '.', Glyph(".....", ".....", ".....", ".....", ".....", ".##..", ".##..") },
            { ',', Glyph(".....", ".....", ".....", ".....", ".##..", "..#..", ".#...") },
            { ':', Glyph(".....", ".##..", ".##..", ".....", ".##..", ".##..", ".....") },
            { '-', Glyph(".....", ".....", ".....", "#####", ".....", ".....", ".....") },
            { '_', Glyph(".....", ".....", ".....", ".....", ".....", ".....", "#####") },
            { '=', Glyph(".....", ".....", "#####", ".....", "#####", ".....", ".....") },
            { '+', Glyph(".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....") },
            { '*', Glyph(".....", "..#..", "#.#.#", ".###.", "#.#.#", "..#..", ".....") },
            { '/', Glyph("....#", "....#", "...#.", "..#..", ".#...", "#....", "#....") },
            { '\\', Glyph("#....", "#....", ".#...", "..#..", "...#.", "....#", "....#") },
            { '[', Glyph(".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###.") },
            { ']', Glyph(".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###.") },
            { '(', Glyph("...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.") },
            { ')', Glyph(".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...") },
            { '<', Glyph("...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#.") },
            { '>', Glyph(".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#...") },
            { '%', Glyph("##..#", "##.#.", "...#.", "..#..", ".#...", ".#.##", "#..##") },
            { '#', Glyph(".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#.") },
            { '!', Glyph("..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#..") },
            { '?', Glyph(".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#..") },
            { '\'', Glyph("..#..", "..#..", ".....", ".....", ".....", ".....", ".....") },
        };

        // Drawn for any character the table has no entry for. A visible box beats skipping the character:
        // a silently dropped glyph turns "angle=45" into "angle45" and the reader has no way to tell that
        // the label is not what the tool meant to write.
        private static readonly byte[] MissingGlyph =
            Glyph("#####", "#...#", "#...#", "#...#", "#...#", "#...#", "#####");

        private static byte[] Glyph(params string[] rows)
        {
            var g = new byte[GlyphHeight];
            for (int r = 0; r < GlyphHeight && r < rows.Length; r++)
            {
                string row = rows[r] ?? string.Empty;
                byte bits = 0;
                for (int c = 0; c < GlyphWidth && c < row.Length; c++)
                    if (row[c] == '#') bits |= (byte)(1 << (GlyphWidth - 1 - c));
                g[r] = bits;
            }
            return g;
        }

        /// <summary>
        /// Pixel size the given text will occupy, so a caller can lay out a background box, keep a label
        /// inside the image, or centre it in a contact-sheet cell without duplicating the advance maths.
        /// Width excludes the trailing inter-character gap; an empty string measures 0 x 0.
        /// </summary>
        internal static void MeasureText(string text, int scale, out int width, out int height)
        {
            int s = Mathf.Max(1, scale);
            if (string.IsNullOrEmpty(text)) { width = 0; height = 0; return; }
            width = text.Length * GlyphAdvance * s - s;   // no gap after the last glyph
            height = GlyphHeight * s;
        }

        /// <summary>
        /// Draws <paramref name="text"/> with the built-in 5x7 dot matrix font.
        ///
        /// <paramref name="x"/> / <paramref name="y"/> are the BOTTOM-LEFT corner of the text box in
        /// Texture2D coordinates (origin bottom-left). <paramref name="scale"/> is an integer pixel
        /// multiplier: 1 gives 5x7 text, which is unreadable on a 2048px capture, so 2-4 is usual.
        ///
        /// Lowercase input is normalised to uppercase — the font has no lowercase glyphs, and dropping the
        /// letters instead would silently shorten the label. Any character outside
        /// [0-9 A-Z space . , : - _ = + * / \ [ ] ( ) &lt; &gt; % # ! ? '] is drawn as a hollow box, so a
        /// missing glyph (Japanese text, for instance) is visible rather than invisible.
        ///
        /// Returns true if at least one pixel landed inside the texture. False means the label is NOT in the
        /// image — fully off-canvas, empty text, or an unreadable texture — and a caller that promises an
        /// annotated image must report that instead of claiming the label is there.
        ///
        /// Pass <paramref name="apply"/> = false when drawing many items in a loop and call
        /// <c>tex.Apply(false, false)</c> once afterwards. Forgetting that Apply leaves the annotations out
        /// of the encoded image on some platforms, so the default is true.
        /// </summary>
        internal static bool DrawText(Texture2D tex, int x, int y, string text, Color color, int scale,
                                      bool apply = true)
        {
            if (tex == null)
            {
                AgentLogger.Warning(LogTag.Tool, "CaptureCommon.DrawText: no texture to draw into.");
                return false;
            }
            if (string.IsNullOrEmpty(text)) return false;

            int s = Mathf.Max(1, scale);
            bool drewAnything = false;
            // Read while the texture is known-good: touching tex.format from inside the catch below would
            // throw a second time if the texture is what went wrong, and that one would escape.
            TextureFormat fmt = tex.format;
            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = char.ToUpperInvariant(text[i]);
                    byte[] glyph;
                    if (!Font.TryGetValue(ch, out glyph)) glyph = MissingGlyph;

                    int gx = x + i * GlyphAdvance * s;
                    // Early-out only when the whole glyph cell is off-canvas horizontally; a partly visible
                    // glyph is still clipped per-pixel by FillRectRaw.
                    if (gx + GlyphWidth * s <= 0 || gx >= tex.width) continue;

                    for (int row = 0; row < GlyphHeight; row++)
                    {
                        byte bits = glyph[row];
                        if (bits == 0) continue;
                        // Row 0 is the TOP of the glyph, but y grows upward in a Texture2D.
                        int py = y + (GlyphHeight - 1 - row) * s;
                        for (int col = 0; col < GlyphWidth; col++)
                        {
                            if ((bits & (1 << (GlyphWidth - 1 - col))) == 0) continue;
                            if (FillRectRaw(tex, gx + col * s, py, s, s, color)) drewAnything = true;
                        }
                    }
                }

                if (apply && drewAnything) tex.Apply(false, false);
                return drewAnything;
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"CaptureCommon.DrawText: could not write into the {fmt} texture ({ex.Message}); " +
                    "the label is missing from the image.");
                return false;
            }
        }

        /// <summary>
        /// Draws the four edges of a rectangle, growing <paramref name="thickness"/> pixels INWARD so the
        /// box never covers anything outside the region it marks.
        ///
        /// <paramref name="x"/> / <paramref name="y"/> are the BOTTOM-LEFT corner in Texture2D coordinates
        /// (origin bottom-left, y upward). A rect taken from <c>EditorWindow.position</c> or
        /// <c>VisualElement.worldBound</c> is top-left based and must go through
        /// <see cref="RectTopLeftToBottomLeft"/> first, or the box marks the mirrored half of the image.
        ///
        /// Out-of-range rectangles are clipped, not rejected — an element scrolled halfway out of its window
        /// should still get the visible part of its box. Returns true only if at least one pixel landed
        /// inside the texture, so a caller can tell "annotated" from "silently drew nothing".
        ///
        /// Pass <paramref name="apply"/> = false inside a loop and Apply once at the end.
        /// </summary>
        internal static bool DrawRect(Texture2D tex, int x, int y, int w, int h, Color color, int thickness,
                                      bool apply = true)
        {
            if (tex == null)
            {
                AgentLogger.Warning(LogTag.Tool, "CaptureCommon.DrawRect: no texture to draw into.");
                return false;
            }
            if (w <= 0 || h <= 0) return false;

            // A thickness larger than half the box would make the two opposite edges overlap and read as a
            // filled blob, which hides the very pixels the box is pointing at.
            int t = Mathf.Clamp(thickness, 1, Mathf.Max(1, Mathf.Min(w, h) / 2));
            bool drew = false;
            TextureFormat fmt = tex.format;   // see DrawText: never read this from inside the catch
            try
            {
                drew |= FillRectRaw(tex, x, y, w, t, color);                    // bottom
                drew |= FillRectRaw(tex, x, y + h - t, w, t, color);            // top
                int sideH = h - 2 * t;
                if (sideH > 0)
                {
                    drew |= FillRectRaw(tex, x, y + t, t, sideH, color);        // left
                    drew |= FillRectRaw(tex, x + w - t, y + t, t, sideH, color);// right
                }

                if (apply && drew) tex.Apply(false, false);
                return drew;
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"CaptureCommon.DrawRect: could not write into the {fmt} texture ({ex.Message}); " +
                    "the box is missing from the image.");
                return false;
            }
        }

        /// <summary>
        /// <see cref="DrawText"/> on top of a filled plate, for labels that have to stay readable over
        /// unknown content. A bare number drawn on a light UI panel or a white material disappears, and the
        /// reader then believes the element was never numbered.
        ///
        /// <paramref name="backgroundColor"/> is composited src-over, so an alpha below 1 tints instead of
        /// hiding what is underneath (0.65 alpha black is a good default). <paramref name="padding"/> is the
        /// plate margin around the glyphs, in output pixels.
        ///
        /// Returns true only if the TEXT was drawn: a plate with no legible text on it is not an annotation.
        /// </summary>
        internal static bool DrawTextWithBackground(Texture2D tex, int x, int y, string text, Color textColor,
                                                   Color backgroundColor, int scale, int padding = 2,
                                                   bool apply = true)
        {
            if (tex == null)
            {
                AgentLogger.Warning(LogTag.Tool, "CaptureCommon.DrawTextWithBackground: no texture to draw into.");
                return false;
            }
            if (string.IsNullOrEmpty(text)) return false;

            int pad = Mathf.Max(0, padding);
            MeasureText(text, scale, out int tw, out int th);
            bool plateDrawn = false;
            try
            {
                plateDrawn = FillRectRaw(tex, x - pad, y - pad, tw + pad * 2, th + pad * 2, backgroundColor);
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"CaptureCommon.DrawTextWithBackground: could not fill the label plate ({ex.Message}).");
                // Keep going: text without a plate is still better than no attempt, and DrawText's return
                // value stays the honest answer to "is the label in the image".
            }

            bool textDrawn = DrawText(tex, x, y, text, textColor, scale, apply);

            // DrawText only Applies when it actually drew something, so a plate whose text fell off-canvas
            // would otherwise sit in the CPU pixels un-uploaded and appear or not depending on the encode path.
            if (apply && plateDrawn && !textDrawn)
            {
                try { tex.Apply(false, false); }
                catch (Exception ex)
                {
                    AgentLogger.Warning(LogTag.Tool,
                        $"CaptureCommon.DrawTextWithBackground: Apply failed after drawing the plate ({ex.Message}).");
                }
            }
            return textDrawn;
        }

        /// <summary>
        /// White text on a 65%-opaque black plate — the combination used for index numbers and time stamps
        /// on contact sheets and annotated window captures.
        /// </summary>
        internal static bool DrawTextWithBackground(Texture2D tex, int x, int y, string text, int scale,
                                                   bool apply = true)
            => DrawTextWithBackground(tex, x, y, text, Color.white, new Color(0f, 0f, 0f, 0.65f), scale, 2, apply);

        /// <summary>
        /// Fills a clipped axis-aligned rectangle, compositing src-over when the colour is translucent.
        /// Returns false when the rectangle lies entirely outside the texture — the caller uses that to
        /// distinguish "drawn" from "nothing to draw".
        /// Does NOT call Apply; every public entry point above owns that decision.
        /// </summary>
        private static bool FillRectRaw(Texture2D tex, int x, int y, int w, int h, Color color)
        {
            if (w <= 0 || h <= 0) return false;

            int x0 = Mathf.Max(0, x);
            int y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(tex.width, x + w);
            int y1 = Mathf.Min(tex.height, y + h);
            if (x1 <= x0 || y1 <= y0) return false;

            float a = Mathf.Clamp01(color.a);
            bool opaque = a >= 0.999f;

            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++)
                {
                    if (opaque)
                    {
                        tex.SetPixel(px, py, color);
                        continue;
                    }
                    Color dst = tex.GetPixel(px, py);
                    tex.SetPixel(px, py, new Color(
                        dst.r + (color.r - dst.r) * a,
                        dst.g + (color.g - dst.g) * a,
                        dst.b + (color.b - dst.b) * a,
                        a + dst.a * (1f - a)));   // src-over: stays opaque over opaque pixels
                }
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Files
        // ─────────────────────────────────────────────────────────────────────────

        private static string NormalizeRoute(string route)
        {
            string r = (route ?? "").Trim();
            if (r.Length == 0) return CaptureRoute.Unknown;
            string lower = r.ToLowerInvariant();
            if (lower == CaptureRoute.Render || lower == CaptureRoute.Window || lower == CaptureRoute.Unknown)
                return lower;

            // Pass the caller's value through rather than mislabelling it, but flag the deviation.
            AgentLogger.Debug(LogTag.Tool,
                $"CaptureCommon: non-standard capture route '{route}' reported (expected 'render' or 'window').");
            return r;
        }

        private static bool TryWriteFile(string path, byte[] bytes, out string error)
        {
            error = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AgentLogger.Warning(LogTag.Tool, $"CaptureCommon: could not write '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
