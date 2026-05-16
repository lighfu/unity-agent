// Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// FaceEmo の ExpressionEditor 内部 (IExpressionEditor / ExpressionEditorModelFacade) への
    /// reflection を局所化する薄い層。バージョン差で壊れた場合の破損面積を最小化する。
    /// </summary>
    internal sealed class ExpressionEditorBridge : IDisposable
    {
        public bool IsHealthy { get; private set; }
        public string LastReflectionError { get; private set; }

        private object _expressionEditor;   // resolved IExpressionEditor impl
        private object _facade;             // ExpressionEditorModelFacade
        private FaceEmoLauncherComponent _launcher;

        // Cached reflection members (populated in TryOpen after _facade is set).
        // Null after Dispose or before first successful TryOpen.
        private Type _blendShapeType;
        private MethodInfo _setBlendShapeValueMethod;
        private PropertyInfo _animatedBlendShapesProperty;

        private const string AppMainAsm = "jp.suzuryg.face-emo.appmain.Editor";
        private const string DetailAsm = "jp.suzuryg.face-emo.detail.Editor";

        public ExpressionEditorBridge()
        {
            // Lazy init in TryOpen
        }

        public bool TryOpen(FaceEmoLauncherComponent launcher, AnimationClip clip)
        {
            // Reset state for retry safety: stale references from a prior successful
            // TryOpen must not leak through if this TryOpen fails early.
            _expressionEditor = null;
            _facade = null;
            _launcher = null;
            _blendShapeType = null;
            _setBlendShapeValueMethod = null;
            _animatedBlendShapesProperty = null;
            IsHealthy = false;

            if (launcher == null || clip == null)
            {
                return Fail("TryOpen: null launcher or clip");
            }

            try
            {
                _launcher = launcher;

                // 1. Resolve IExpressionEditor via FaceEmo's DI container
                var installerType = Type.GetType($"Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, {AppMainAsm}");
                if (installerType == null) { return Fail("FaceEmoInstaller type not found"); }

                var installer = Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container")?.GetValue(installer);
                if (container == null) { return Fail("DI container property missing"); }

                var ieeType = Type.GetType($"Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, {DetailAsm}");
                if (ieeType == null) { return Fail("IExpressionEditor type not found"); }

                var resolve = container.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Resolve"
                        && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                if (resolve == null) { return Fail("Container.Resolve<T>() not found"); }

                _expressionEditor = resolve.MakeGenericMethod(ieeType).Invoke(container, null);
                if (_expressionEditor == null) { return Fail("Resolve<IExpressionEditor> returned null"); }

                // 2. Open editor with clip
                var openMethod = ieeType.GetMethod("Open");
                if (openMethod == null) { return Fail("IExpressionEditor.Open not found"); }
                openMethod.Invoke(_expressionEditor, new object[] { clip });

                // 3. Acquire facade via reflection (spike 0.3 result)
                var facadeField = _expressionEditor.GetType()
                    .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.FieldType.Name == "ExpressionEditorModelFacade");
                if (facadeField == null) { return Fail("ExpressionEditorModelFacade field not found"); }

                _facade = facadeField.GetValue(_expressionEditor);
                if (_facade == null) { return Fail("Facade field is null"); }

                // 4. Cache reflection members so Session loops don't pay GetMethod/GetProperty cost per shape.
                _blendShapeType = Type.GetType("Suzuryg.FaceEmo.Domain.BlendShape, jp.suzuryg.face-emo.domain.Runtime");
                if (_blendShapeType == null) { return Fail("BlendShape type not found"); }

                _setBlendShapeValueMethod = _facade.GetType().GetMethod("SetBlendShapeValue");
                if (_setBlendShapeValueMethod == null) { return Fail("Facade.SetBlendShapeValue not found"); }

                _animatedBlendShapesProperty = _facade.GetType().GetProperty("AnimatedBlendShapes");
                if (_animatedBlendShapesProperty == null) { return Fail("Facade.AnimatedBlendShapes not found"); }

                IsHealthy = true;
                LastReflectionError = null;
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"TryOpen: {inner.GetType().Name}: {inner.Message}");
            }
        }

        public bool TryGetAnimatedBlendShapes(out IReadOnlyDictionary<(string path, string name), float> values)
        {
            values = null;
            if (!IsHealthy || _facade == null) return false;
            try
            {
                if (_animatedBlendShapesProperty == null)
                {
                    return Fail("TryGetAnimatedBlendShapes: cached AnimatedBlendShapes property is null (Bridge bug)");
                }

                var dict = _animatedBlendShapesProperty.GetValue(_facade) as System.Collections.IDictionary;
                if (dict == null) { return Fail("TryGetAnimatedBlendShapes: AnimatedBlendShapes is not IDictionary"); }

                var result = new Dictionary<(string, string), float>();
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    // Key is BlendShape struct with Path and Name properties
                    var key = entry.Key;
                    var keyType = key.GetType();
                    var path = keyType.GetProperty("Path")?.GetValue(key) as string ?? "";
                    var name = keyType.GetProperty("Name")?.GetValue(key) as string ?? "";
                    float v = Convert.ToSingle(entry.Value);
                    result[(path, name)] = v;
                }
                values = result;
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"TryGetAnimatedBlendShapes: {inner.GetType().Name}: {inner.Message}");
            }
        }

        public bool TrySetBlendShape(string smrRelativePath, string shapeName, float value)
        {
            if (!IsHealthy || _facade == null) return false;
            try
            {
                if (_blendShapeType == null)
                {
                    return Fail("TrySetBlendShape: cached BlendShape type is null (Bridge bug)");
                }
                if (_setBlendShapeValueMethod == null)
                {
                    return Fail("TrySetBlendShape: cached SetBlendShapeValue method is null (Bridge bug)");
                }

                // Confirmed via FaceEmo source: public BlendShape(string path, string name) — (path, name) order.
                var bs = Activator.CreateInstance(_blendShapeType, new object[] { smrRelativePath, shapeName });

                _setBlendShapeValueMethod.Invoke(_facade, new object[] { bs, value });
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"TrySetBlendShape: {inner.GetType().Name}: {inner.Message}");
            }
        }

        public bool TryOpenPreviewWindow()
        {
            if (!IsHealthy) return false;
            try
            {
                // FaceEmo's PreviewWindow is in Detail.ExpressionEditor.Views.PreviewWindow
                // It's typically shown via EditorWindow.GetWindow<PreviewWindow>()
                var pwType = Type.GetType($"Suzuryg.FaceEmo.Detail.ExpressionEditor.Views.PreviewWindow, {DetailAsm}");
                if (pwType == null) { return Fail("TryOpenPreviewWindow: PreviewWindow type not found"); }

                var getWindow = typeof(EditorWindow)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetWindow"
                        && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (getWindow == null) { return Fail("TryOpenPreviewWindow: EditorWindow.GetWindow<T>() not found"); }

                // NOTE: fallback path if GetWindow<T> reflection ever fails would be
                // `ScriptableObject.CreateInstance(pwType) as EditorWindow; window?.Show();`
                // — intentionally not implemented; primary path is preferred for focus/dock behaviour.
                var window = getWindow.MakeGenericMethod(pwType).Invoke(null, null);
                if (window == null)
                {
                    return Fail("TryOpenPreviewWindow: GetWindow returned null");
                }
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"TryOpenPreviewWindow: {inner.GetType().Name}: {inner.Message}");
            }
        }

        /// <summary>
        /// Sets <see cref="IsHealthy"/> to false, records <paramref name="msg"/> on
        /// <see cref="LastReflectionError"/>, and logs a Warning.
        ///
        /// Called from TryOpen and from any Try* method whose failure indicates the
        /// Bridge has become unhealthy. Callers reach here exactly once per state
        /// transition: if Session retries Live calls in a loop, the first failure
        /// logs and Session demotes to Degraded, so subsequent calls short-circuit
        /// on <c>!IsHealthy</c> and don't re-enter the Bridge.
        ///
        /// Trade-off: per-call failures inside a Session loop (e.g. 32 blendshapes)
        /// could still spam the console if Session keeps calling without demoting.
        /// If that becomes a problem in practice, split into <c>Fail</c> /
        /// <c>FailQuiet</c> later. Today this is acceptable because the first
        /// failure flips <see cref="IsHealthy"/> and the loop exits on the next check.
        /// </summary>
        private bool Fail(string msg)
        {
            IsHealthy = false;
            LastReflectionError = msg;
            Debug.LogWarning($"[ExpressionEditorBridge] {msg}");
            return false;
        }

        public void Dispose()
        {
            // Dispose FaceEmo's ExpressionEditorPresenter so its CompositeDisposable chain
            // disposes ExpressionEditorModelFacade → PreviewClipSampler → DestroyImmediate
            // the instantiated preview avatar in the scene. Without this, each new TryOpen
            // creates a fresh DI container + presenter + sampler + preview avatar, and the
            // prior avatar lingers in the scene with HideAndDontSave (visible in Scene/Game
            // view, invisible in Hierarchy) — N TryOpen calls leave N avatars stacked at
            // PreviewClipSampler.Origin (100,100,100).
            try { (_expressionEditor as IDisposable)?.Dispose(); }
            catch (Exception ex) { Debug.Log($"[ExpressionEditorBridge] Dispose of IExpressionEditor non-fatal: {ex.Message}"); }

            _expressionEditor = null;
            _facade = null;
            _launcher = null;
            _blendShapeType = null;
            _setBlendShapeValueMethod = null;
            _animatedBlendShapesProperty = null;
        }

        /// <summary>
        /// Find and destroy any leftover FaceEmo preview-avatar GameObjects in the active scene.
        /// FaceEmo's PreviewClipSampler instantiates the TargetAvatar at world position (100,100,100)
        /// with hideFlags=HideAndDontSave. If a session was abandoned without Dispose (e.g. domain
        /// reload before disposal, or a TryOpen that crashed mid-flight), the cloned avatar lingers.
        /// This helper sweeps them up.
        /// </summary>
        /// <param name="preserveActiveSession">
        /// When true (default), skips the avatar belonging to the currently-active session — its
        /// PreviewClipSampler is still using it. Set to false to wipe ALL preview avatars even if
        /// it disrupts an in-progress session.
        /// </param>
        /// <returns>Count of destroyed preview avatars.</returns>
        public static int CleanupOrphanPreviewAvatars(bool preserveActiveSession = true)
        {
            // FaceEmo's preview-avatar marker: hideFlags == HideAndDontSave AND positioned at
            // (100,100,100) AND has a SkinnedMeshRenderer somewhere (typical avatar). Use
            // FindObjectsOfTypeAll because HideAndDontSave hides them from regular FindObjectsOfType.
            //
            // Each preview avatar's name is "{targetAvatar.gameObject.name}(Clone)" because it's
            // produced by Object.Instantiate(targetAvatar). If a session is currently active, its
            // expected clone name is computed here and skipped so we don't yank the running
            // session's avatar out from under it.
            string activeCloneName = null;
            if (preserveActiveSession)
            {
                var active = FaceEmoExpressionSession.Active;
                var target = active?.Launcher?.AV3Setting?.TargetAvatar;
                if (target != null) activeCloneName = target.gameObject.name + "(Clone)";
            }

            int destroyed = 0;
            int preserved = 0;
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            const float epsilon = 0.001f;
            var origin = new Vector3(100f, 100f, 100f);
            foreach (var go in all)
            {
                if (go == null) continue;
                if (go.hideFlags != HideFlags.HideAndDontSave) continue;
                if (go.transform.parent != null) continue; // FaceEmo preview avatars are scene roots
                if ((go.transform.position - origin).sqrMagnitude > epsilon) continue;
                if (go.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) == null) continue;
                if (activeCloneName != null && go.name == activeCloneName)
                {
                    preserved++;
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(go);
                destroyed++;
            }
            if (destroyed > 0 || preserved > 0)
                Debug.Log($"[ExpressionEditorBridge] Cleaned up {destroyed} orphan FaceEmo preview avatar(s)" +
                          (preserved > 0 ? $", preserved {preserved} for active session." : "."));
            return destroyed;
        }
    }
}
#endif
