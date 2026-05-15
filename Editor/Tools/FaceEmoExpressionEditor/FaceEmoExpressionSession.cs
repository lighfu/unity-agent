// Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Domain;
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
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
            if (!gate.Ok) throw new InvalidOperationException(gate.ErrorMessage);

            var menu = FaceEmoAPI.LoadMenu(gate.Launcher);
            if (menu == null) throw new InvalidOperationException("Error: Failed to load FaceEmo menu.");
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) throw new InvalidOperationException($"Error: Mode '{modeName}' not found in FaceEmo menu.");

            string guid = null;
            var animProp = mode.GetType().GetProperty("Animation");
            if (animProp != null)
            {
                var anim = animProp.GetValue(mode);
                if (anim != null)
                    guid = anim.GetType().GetProperty("GUID")?.GetValue(anim) as string;
            }
            AnimationClip clip = null;
            if (!string.IsNullOrEmpty(guid))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            if (clip == null) clip = new AnimationClip { name = $"{modeName}_clip" };

            _active?.Dispose();
            var session = new FaceEmoExpressionSession
            {
                Launcher = gate.Launcher,
                IsNewExpression = false,
                ModeId = modeId,
                PendingDisplayName = modeName,
                Clip = clip,
            };

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
        {
            if (string.IsNullOrEmpty(smrRelativePath) || string.IsNullOrEmpty(shapeName))
                throw new ArgumentException("smrRelativePath and shapeName are required");

            if (Mode == SyncMode.Live)
            {
                if (_bridge != null && _bridge.TrySetBlendShape(smrRelativePath, shapeName, value))
                    return;

                // Live failed at runtime — downgrade for the rest of the session
                Debug.LogWarning($"[FaceEmoExpressionSession] Live SetBlendShape failed ({_bridge?.LastReflectionError}). Downgrading to Degraded.");
                Mode = SyncMode.Degraded;
            }

            // Degraded path (Task 3.5 implements DegradedSet)
            DegradedSet(smrRelativePath, shapeName, value);
        }

        private void DegradedSet(string smrRelativePath, string shapeName, float value)
        {
            AssetPathFallback.WriteBlendShapeCurve(Clip, smrRelativePath, shapeName, value);
            // For asset clips, ensure save
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
            {
                EditorUtility.SetDirty(Clip);
            }
            AssetPathFallback.RefreshFaceEmoWindow(Launcher);
        }

        public IReadOnlyDictionary<string, float> GetCurrentValues()
        {
            var result = new Dictionary<string, float>();

            // Live path: read from facade
            if (Mode == SyncMode.Live && _bridge != null &&
                _bridge.TryGetAnimatedBlendShapes(out var live))
            {
                foreach (var kv in live)
                    result[kv.Key.name] = kv.Value;
                return result;
            }

            // Degraded / fallback: read from clip's curves
            if (Clip != null)
            {
                foreach (var b in AnimationUtility.GetCurveBindings(Clip))
                {
                    if (!b.propertyName.StartsWith("blendShape.")) continue;
                    var curve = AnimationUtility.GetEditorCurve(Clip, b);
                    if (curve == null || curve.length == 0) continue;
                    string shape = b.propertyName.Substring("blendShape.".Length);
                    result[shape] = curve[0].value;
                }
            }
            return result;
        }

        public void Commit()
        {
            if (Clip == null) throw new InvalidOperationException("No clip to commit.");

            // 1. Save the clip asset
            string finalPath = PendingSavePath;
            if (string.IsNullOrEmpty(finalPath))
                finalPath = $"Assets/UnityAgent/Expressions/{PendingDisplayName ?? Clip.name}.anim";

            string dir = System.IO.Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                // Create folder hierarchy
                string fullDir = System.IO.Path.Combine(Application.dataPath, "..", dir);
                if (!System.IO.Directory.Exists(fullDir)) System.IO.Directory.CreateDirectory(fullDir);
                AssetDatabase.Refresh();
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
                AssetDatabase.CreateAsset(Clip, finalPath);
            EditorUtility.SetDirty(Clip);
            AssetDatabase.SaveAssets();

            // 2. Register / update Mode in FaceEmo menu
            var menu = FaceEmoAPI.LoadMenu(Launcher);
            if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");

            string guid = AssetDatabase.AssetPathToGUID(finalPath);
            var animObj = Activator.CreateInstance(
                Type.GetType("Suzuryg.FaceEmo.Domain.Animation, jp.suzuryg.face-emo.domain.Runtime"),
                new object[] { guid });

            if (IsNewExpression)
            {
                string dest = FaceEmoAPI.ResolveDestination(menu, "Registered");
                if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
                    dest = FaceEmoAPI.ResolveDestination(menu, "Unregistered");
                string modeId = FaceEmoAPI.AddMode(menu, dest);
                FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: PendingDisplayName);
                FaceEmoAPI.SetModeAnimation(menu, (Suzuryg.FaceEmo.Domain.Animation)animObj, modeId);
                ModeId = modeId;
                IsNewExpression = false;
            }
            else
            {
                FaceEmoAPI.SetModeAnimation(menu, (Suzuryg.FaceEmo.Domain.Animation)animObj, ModeId);
            }
            FaceEmoAPI.SaveMenu(Launcher, menu, $"Commit Expression '{PendingDisplayName}'");
        }

        public void OverrideSavePath(string path)
        {
            PendingSavePath = path;
        }

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
