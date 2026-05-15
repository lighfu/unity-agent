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

        private const string AppMainAsm = "jp.suzuryg.face-emo.appmain.Editor";
        private const string DetailAsm = "jp.suzuryg.face-emo.detail.Editor";

        public ExpressionEditorBridge()
        {
            // Lazy init in TryOpen
        }

        public bool TryOpen(FaceEmoLauncherComponent launcher, AnimationClip clip)
        {
            if (launcher == null || clip == null)
            {
                LastReflectionError = "TryOpen: null launcher or clip";
                IsHealthy = false;
                return false;
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

                IsHealthy = true;
                LastReflectionError = null;
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"{inner.GetType().Name}: {inner.Message}");
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
                if (pwType == null) { LastReflectionError = "PreviewWindow type not found"; return false; }

                var getWindow = typeof(EditorWindow)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetWindow"
                        && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (getWindow == null) { LastReflectionError = "EditorWindow.GetWindow<T>() not found"; return false; }

                // NOTE: fallback path if GetWindow<T> reflection ever fails would be
                // `ScriptableObject.CreateInstance(pwType) as EditorWindow; window?.Show();`
                // — intentionally not implemented; primary path is preferred for focus/dock behaviour.
                var window = getWindow.MakeGenericMethod(pwType).Invoke(null, null);
                return window != null;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                LastReflectionError = $"TryOpenPreviewWindow: {inner.GetType().Name}: {inner.Message}";
                return false;
            }
        }

        private bool Fail(string msg)
        {
            IsHealthy = false;
            LastReflectionError = msg;
            Debug.LogWarning($"[ExpressionEditorBridge] {msg}");
            return false;
        }

        public void Dispose()
        {
            // No explicit close — FaceEmo keeps its window open
            _expressionEditor = null;
            _facade = null;
        }
    }
}
#endif
