// Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
#if FACE_EMO
using System;
using System.IO;
using System.Reflection;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// FaceEmo の MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer を
    /// reflection でインスタンス化し、PNG を Library/UnityAgent/face-thumbnails/ に出力する薄い層。
    /// バージョン差で reflection が壊れた場合は IsHealthy=false でフォールバック表示する。
    ///
    /// Failure-policy:
    /// - <see cref="Fail"/> is called only from <see cref="TryInitialize"/> when reflection setup
    ///   fails — it flips <see cref="IsHealthy"/> to false (Renderer permanently unhealthy until
    ///   reinitialized). Per-call methods (Render*) instead set <see cref="LastReflectionError"/>
    ///   directly and return null, leaving <see cref="IsHealthy"/> alone — a missing Mode or
    ///   timed-out render is user-data noise, not a structural reflection break.
    /// </summary>
    internal sealed class FaceEmoThumbnailRenderer : IDisposable
    {
        public bool IsHealthy { get; private set; }
        public string LastReflectionError { get; private set; }

        private const string DetailAsm = "jp.suzuryg.face-emo.detail.Editor";

        private object _mainDrawer;
        private object _gestureDrawer;
        private object _exMenuDrawer;
        private FaceEmoLauncherComponent _launcher;

        // Cached MethodInfo (resolved once in TryInitialize)
        private MethodInfo _getThumbnail;
        private MethodInfo _requestUpdate;
        private MethodInfo _update;
        private MethodInfo _getCached;

        public static string CacheRoot => "Library/UnityAgent/face-thumbnails";

        public bool TryInitialize(FaceEmoLauncherComponent launcher)
        {
            // Reset state for retry safety: stale references from a prior successful
            // TryInitialize must not leak through if this call fails early.
            _mainDrawer = null;
            _gestureDrawer = null;
            _exMenuDrawer = null;
            _launcher = null;
            _getThumbnail = null;
            _requestUpdate = null;
            _update = null;
            _getCached = null;
            IsHealthy = false;
            LastReflectionError = null;

            if (launcher == null) return Fail("launcher is null");
            if (launcher.AV3Setting == null) return Fail("launcher.AV3Setting is null");
            if (launcher.ThumbnailSetting == null) return Fail("launcher.ThumbnailSetting is null");

            try
            {
                _launcher = launcher;

                var mainType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.MainThumbnailDrawer, {DetailAsm}");
                var gestureType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.GestureTableThumbnailDrawer, {DetailAsm}");
                var exMenuType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.ExMenuThumbnailDrawer, {DetailAsm}");
                if (mainType == null) return Fail("MainThumbnailDrawer type not found");
                if (gestureType == null) return Fail("GestureTableThumbnailDrawer type not found");
                if (exMenuType == null) return Fail("ExMenuThumbnailDrawer type not found");

                _mainDrawer = Activator.CreateInstance(mainType, launcher.AV3Setting, launcher.ThumbnailSetting);
                _gestureDrawer = Activator.CreateInstance(gestureType, launcher.AV3Setting, launcher.ThumbnailSetting);
                _exMenuDrawer = Activator.CreateInstance(exMenuType, launcher.AV3Setting, launcher.ThumbnailSetting);

                // Cache MethodInfos from base class (ThumbnailDrawerBase) — public, instance.
                var baseType = mainType.BaseType;
                if (baseType == null) return Fail("MainThumbnailDrawer has no BaseType");
                _getThumbnail = baseType.GetMethod("GetThumbnail");
                _requestUpdate = baseType.GetMethod("RequestUpdate");
                _update = baseType.GetMethod("Update");
                _getCached = baseType.GetMethod("GetCachedThumbnailOrNull");
                if (_getThumbnail == null) return Fail("GetThumbnail method missing");
                if (_requestUpdate == null) return Fail("RequestUpdate method missing");
                if (_update == null) return Fail("Update method missing");
                if (_getCached == null) return Fail("GetCachedThumbnailOrNull method missing");

                IsHealthy = true;
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"{inner.GetType().Name}: {inner.Message}");
            }
        }

        public void Dispose()
        {
            (_mainDrawer as IDisposable)?.Dispose();
            (_gestureDrawer as IDisposable)?.Dispose();
            (_exMenuDrawer as IDisposable)?.Dispose();
            _mainDrawer = null;
            _gestureDrawer = null;
            _exMenuDrawer = null;
            _launcher = null;
            IsHealthy = false;
            LastReflectionError = "disposed";
        }

        private static readonly string[] GestureNames =
        {
            "Neutral", "Fist", "HandOpen", "Fingerpoint",
            "Victory", "RockNRoll", "HandGun", "ThumbsUp"
        };

        /// <summary>
        /// For each of the 8 hand gestures, find the matching branch's animation in the given Mode.
        /// Returns an array of 8 (Animation, AnimationClip) pairs, indexed by HandGesture (0-7).
        /// Falls back to the Mode's base animation when no branch matches a given gesture.
        /// </summary>
        private (Suzuryg.FaceEmo.Domain.Animation anim, AnimationClip clip)[] ResolveGestureAnimations(Suzuryg.FaceEmo.Domain.IMode mode)
        {
            var result = new (Suzuryg.FaceEmo.Domain.Animation, AnimationClip)[8];
            var baseAnim = mode.Animation;
            var baseClip = baseAnim != null && !string.IsNullOrEmpty(baseAnim.GUID)
                ? AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(baseAnim.GUID))
                : null;

            // Initialize all slots to base
            for (int i = 0; i < 8; i++) result[i] = (baseAnim, baseClip);

            // Walk branches; for each branch, if conditions cover a specific gesture, override that slot
            if (mode.Branches != null)
            {
                foreach (var branch in mode.Branches)
                {
                    if (branch == null || branch.Conditions == null) continue;
                    foreach (var cond in branch.Conditions)
                    {
                        int gestureIdx = (int)cond.HandGesture;
                        if (gestureIdx < 0 || gestureIdx >= 8) continue;
                        var slotAnim = branch.BaseAnimation ?? baseAnim;
                        if (slotAnim != null && !string.IsNullOrEmpty(slotAnim.GUID))
                        {
                            var slotClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(slotAnim.GUID));
                            if (slotClip != null) result[gestureIdx] = (slotAnim, slotClip);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Render an 8-cell gesture table (B — GestureTableThumbnailDrawer) and save as a composite PNG.
        /// Returns the saved PNG path, or null on failure.
        /// </summary>
        public string RenderGestureTable(string modeName)
        {
            if (!IsHealthy) { LastReflectionError = "Renderer not healthy"; return null; }
            if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

            var menu = FaceEmoAPI.LoadMenu(_launcher);
            if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
            var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }

            var slots = ResolveGestureAnimations(mode);

            // Render each cell — also track whether MakeReadableCopy allocated a new texture
            var cells = new Texture2D[8];
            var cellOwnsTexture = new bool[8];
            Texture2D composite = null;

            try
            {
                int cellW = 0, cellH = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (slots[i].clip == null) continue;
                    var tex = DriveSyncRender(_gestureDrawer, slots[i].anim, slots[i].clip);
                    if (tex == null) continue;
                    var readable = MakeReadableCopy(tex);
                    cells[i] = readable;
                    cellOwnsTexture[i] = readable != tex;
                    if (cellW == 0) { cellW = readable.width; cellH = readable.height; }
                }

                if (cellW == 0) { LastReflectionError = "No gesture cells could be rendered"; return null; }

                // Composite into 4x2 grid (4 cols × 2 rows) with 2px border, gesture name label
                const int padding = 4;
                const int labelH = 14;
                int gridW = cellW * 4 + padding * 5;
                int gridH = (cellH + labelH) * 2 + padding * 3;
                composite = new Texture2D(gridW, gridH, TextureFormat.RGBA32, false);

                // Fill with dark gray background
                var bg = new Color32(40, 40, 48, 255);
                var bgPixels = new Color32[gridW * gridH];
                for (int p = 0; p < bgPixels.Length; p++) bgPixels[p] = bg;
                composite.SetPixels32(bgPixels);

                for (int i = 0; i < 8; i++)
                {
                    int col = i % 4;
                    int row = i / 4;
                    int x = padding + col * (cellW + padding);
                    int y = padding + row * (cellH + labelH + padding);
                    if (cells[i] != null)
                    {
                        var pixels = cells[i].GetPixels32();
                        composite.SetPixels32(x, y + labelH, cellW, cellH, pixels);
                    }
                }
                composite.Apply();

                return SaveAsPng(composite, $"{SanitizeFileName(modeName)}_gestures.png");
            }
            finally
            {
                // Cleanup — destroy only textures WE allocated (not FaceEmo drawer's cached refs)
                for (int i = 0; i < 8; i++)
                {
                    if (cells[i] != null && cellOwnsTexture[i])
                        UnityEngine.Object.DestroyImmediate(cells[i]);
                }
                if (composite != null) UnityEngine.Object.DestroyImmediate(composite);
            }
        }

        /// <summary>
        /// Triggers FaceEmo's MainView to refresh its thumbnail cache by relaunching the window
        /// (no-op if the window is not open). modeName is informational (logged for context).
        /// Returns false only if the launcher reference is null; otherwise true. Note: the
        /// underlying FaceEmoAPI.RefreshWindowIfOpen is void and silently swallows internal
        /// reflection errors, so a "true" return does not guarantee a visible UI update.
        /// </summary>
        public bool RefreshMainView(string modeName = null)
        {
            if (_launcher == null) { LastReflectionError = "Launcher is null"; return false; }
            try
            {
                FaceEmoAPI.RefreshWindowIfOpen(_launcher);
                if (!string.IsNullOrEmpty(modeName))
                    Debug.Log($"[FaceEmoThumbnailRenderer] MainView refreshed (target Mode: {modeName})");
                return true;
            }
            catch (Exception ex)
            {
                LastReflectionError = $"RefreshMainView: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Render the single-Mode thumbnail (A — MainThumbnailDrawer) and save as PNG.
        /// Returns the saved PNG path, or null on failure (sets <see cref="LastReflectionError"/>).
        /// Does NOT call Fail() — per-call user-data errors must not flip IsHealthy.
        /// </summary>
        public string RenderModeThumbnail(string modeName)
        {
            if (!IsHealthy) { LastReflectionError = "Renderer not healthy — call TryInitialize first"; return null; }
            if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

            var menu = FaceEmoAPI.LoadMenu(_launcher);
            if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
            var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }
            var anim = mode.Animation;
            if (anim == null || string.IsNullOrEmpty(anim.GUID))
            { LastReflectionError = $"Mode '{modeName}' has no animation"; return null; }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(anim.GUID));
            if (clip == null) { LastReflectionError = $"Clip not found for GUID {anim.GUID}"; return null; }

            var texture = DriveSyncRender(_mainDrawer, anim, clip);
            if (texture == null) { LastReflectionError = "Render timed out (50 iterations)"; return null; }

            return SaveAsPng(texture, $"{SanitizeFileName(modeName)}.png");
        }

        /// <summary>
        /// Render the ExMenu (VRChat-baked) thumbnail (C — ExMenuThumbnailDrawer) and save as PNG.
        /// Returns the saved PNG path, or null on failure (sets <see cref="LastReflectionError"/>).
        /// Does NOT call Fail() — per-call user-data errors must not flip IsHealthy.
        /// File name is prefixed with "exmenu_" to distinguish from the Main Mode thumbnail.
        /// </summary>
        public string RenderExMenuThumbnail(string modeName)
        {
            if (!IsHealthy) { LastReflectionError = "Renderer not healthy"; return null; }
            if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

            var menu = FaceEmoAPI.LoadMenu(_launcher);
            if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
            var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }
            var anim = mode.Animation;
            if (anim == null || string.IsNullOrEmpty(anim.GUID))
            { LastReflectionError = $"Mode '{modeName}' has no animation"; return null; }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(anim.GUID));
            if (clip == null) { LastReflectionError = $"Clip not found for GUID {anim.GUID}"; return null; }

            var texture = DriveSyncRender(_exMenuDrawer, anim, clip);
            if (texture == null) { LastReflectionError = "ExMenu render timed out"; return null; }

            return SaveAsPng(texture, $"exmenu_{SanitizeFileName(modeName)}.png");
        }

        /// <summary>
        /// Render a thumbnail synchronously by repeatedly calling Update() until the cache fills.
        /// Returns the cached Texture2D or null on timeout. Wraps reflection exceptions and
        /// surfaces them via <see cref="LastReflectionError"/>.
        /// </summary>
        private Texture2D DriveSyncRender(object drawer, Suzuryg.FaceEmo.Domain.Animation animation, AnimationClip clip, int maxIterations = 50)
        {
            try
            {
                // Prime the cache request: first GetThumbnail returns hourglass placeholder,
                // RequestUpdate schedules the actual render.
                _getThumbnail.Invoke(drawer, new object[] { animation });
                _requestUpdate.Invoke(drawer, new object[] { clip });

                for (int i = 0; i < maxIterations; i++)
                {
                    _update.Invoke(drawer, null);
                    var cached = _getCached.Invoke(drawer, new object[] { animation }) as Texture2D;
                    if (cached != null) return cached;
                }
                return null;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                LastReflectionError = $"DriveSyncRender: {inner.GetType().Name}: {inner.Message}";
                return null;
            }
        }

        /// <summary>
        /// Save a Texture2D to the renderer's PNG cache. Returns the saved path
        /// (relative to project root), or null on failure.
        /// </summary>
        private string SaveAsPng(Texture2D texture, string fileName)
        {
            if (texture == null) return null;
            Texture2D readable = null;
            try
            {
                Directory.CreateDirectory(CacheRoot);
                string path = Path.Combine(CacheRoot, fileName).Replace('\\', '/');

                // EncodeToPNG requires readable texture — copy via RenderTexture if needed.
                readable = MakeReadableCopy(texture);
                File.WriteAllBytes(path, readable.EncodeToPNG());
                return path;
            }
            catch (Exception ex)
            {
                LastReflectionError = $"SaveAsPng: {ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                if (readable != null && readable != texture)
                    UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        private static Texture2D MakeReadableCopy(Texture2D source)
        {
            if (source.isReadable) return source;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            catch
            {
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        /// <summary>
        /// Sets <see cref="IsHealthy"/> to false, records <paramref name="msg"/> on
        /// <see cref="LastReflectionError"/>, and logs a Warning.
        ///
        /// Used by <see cref="TryInitialize"/> only — indicates the Renderer is structurally
        /// unhealthy (reflection paths broken). Per-call Render* failures bypass Fail() and
        /// set LastReflectionError directly so a missing Mode doesn't invalidate the whole
        /// Renderer.
        /// </summary>
        private bool Fail(string msg)
        {
            IsHealthy = false;
            LastReflectionError = msg;
            Debug.LogWarning($"[FaceEmoThumbnailRenderer] {msg}");
            return false;
        }
    }
}
#endif
