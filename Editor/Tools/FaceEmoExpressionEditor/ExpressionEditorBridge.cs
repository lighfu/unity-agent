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

        public ExpressionEditorBridge()
        {
            // Lazy init in TryOpen
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
