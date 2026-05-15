// Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// 「いま編集中の表情」を表す高レベル抽象。
    /// Live (Bridge 経由) と Degraded (AssetPathFallback 経由) の切替を集約する。
    /// </summary>
    public sealed class FaceEmoExpressionSession : IDisposable
    {
        public enum SyncMode { Live, Degraded }

        // Ambient session — set by Open*, consumed by SetExpressionPreviewMulti auto-session check
        private static FaceEmoExpressionSession _active;
        public static FaceEmoExpressionSession Active => _active;

        public SyncMode Mode { get; private set; }
        public string ModeId { get; private set; }           // FaceEmo Mode ID; null when new + not committed
        public AnimationClip Clip { get; private set; }
        public string TmpName { get; private set; }          // "Tmp_<hex>" for auto-session
        public FaceEmoLauncherComponent Launcher { get; private set; }
        public bool IsNewExpression { get; private set; }
        public string PendingDisplayName { get; private set; }
        public string PendingSavePath { get; private set; }

        private ExpressionEditorBridge _bridge;

        private FaceEmoExpressionSession() { }

        public static FaceEmoExpressionSession OpenForMode(string modeName, string gameObjectName = "")
            => throw new NotImplementedException(); // Task 3.3

        public static FaceEmoExpressionSession OpenForNewExpression(string displayName, string animSavePath, string gameObjectName = "")
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
            if (!gate.Ok) throw new InvalidOperationException(gate.ErrorMessage);

            // Dispose previous ambient session
            _active?.Dispose();

            var session = new FaceEmoExpressionSession
            {
                Launcher = gate.Launcher,
                IsNewExpression = true,
                PendingDisplayName = string.IsNullOrEmpty(displayName) ? GenerateTmpName() : displayName,
                PendingSavePath = animSavePath,
                TmpName = displayName == null ? GenerateTmpName() : null,
                Clip = new AnimationClip(),
            };
            session.Clip.name = session.PendingDisplayName;

            // Try Live via Bridge
            session._bridge = new ExpressionEditorBridge();
            if (session._bridge.TryOpen(session.Launcher, session.Clip))
            {
                session._bridge.TryOpenPreviewWindow();
                session.Mode = SyncMode.Live;
            }
            else
            {
                Debug.LogWarning($"[FaceEmoExpressionSession] Bridge unhealthy ({session._bridge.LastReflectionError}). Falling back to Degraded.");
                session.Mode = SyncMode.Degraded;
            }

            _active = session;
            return session;
        }

        public void SetBlendShape(string smrRelativePath, string shapeName, float value)
            => throw new NotImplementedException(); // Task 3.4/3.5

        public IReadOnlyDictionary<string, float> GetCurrentValues()
            => throw new NotImplementedException(); // Task 3.6

        public void Commit()
            => throw new NotImplementedException(); // Task 3.6

        public void Dispose()
        {
            if (_active == this) _active = null;
            _bridge?.Dispose();
            _bridge = null;
        }

        // ----- helpers -----
        internal static string GenerateTmpName()
        {
            return "Tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }
    }
}
#endif
