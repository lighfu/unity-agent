// Editor/Tools/FaceEmoExpressionEditor/AssetPathFallback.cs
#if FACE_EMO
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// Bridge が IsHealthy=false の時の表情編集経路。
    /// EditorCurveBinding を AnimationClip に書き、FaceEmo ウィンドウを再読込させる。
    /// </summary>
    internal static class AssetPathFallback
    {
        public static void WriteBlendShapeCurve(AnimationClip clip, string smrRelativePath, string shapeName, float value)
        {
            if (clip == null) return;
            var binding = new EditorCurveBinding
            {
                path = smrRelativePath,
                type = typeof(SkinnedMeshRenderer),
                propertyName = $"blendShape.{shapeName}",
            };
            var curve = new AnimationCurve(new Keyframe(0f, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        public static void RefreshFaceEmoWindow(FaceEmoLauncherComponent launcher)
        {
            FaceEmoAPI.RefreshWindowIfOpen(launcher);
        }
    }
}
#endif
