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
