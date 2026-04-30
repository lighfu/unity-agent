using System;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    // 表情プレビュー用の安定したキャプチャ。
    // SceneView を使わず、Head ボーンを基準に専用カメラを生成して固定 FOV / 距離 / アンビエントで撮る。
    // これにより SceneView の状態 (角度・解像度・ライティング) に依存しない再現性ある画像を得られる。
    public static class FaceCameraCapture
    {
        private const float DefaultFov = 30f;
        private const float DefaultDistanceMultiplier = 4.5f;
        private const int DefaultResolution = 512;

        [AgentTool("アバターの顔を専用カメラ (固定 FOV 30°/解像度 512×512) でキャプチャする。" +
            "SceneView 非依存なので状態に左右されず、表情プレビューの判断材料として安定。" +
            "CaptureExpressionPreview の代替。Head ボーンが必須 (humanoid only)。" +
            "値域 0-100 の BlendShape 設定後に呼び出すのが標準フロー。",
            Author = "ajisaiflow",
            Category = "FaceProfile",
            Risk = ToolRisk.Safe)]
        public static string CaptureFacePreview(string avatarRootName, int width = DefaultResolution, int height = DefaultResolution)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";

            var root = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (root == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var animator = root.GetComponent<Animator>();
            Transform headBone = null;
            if (animator != null && animator.isHuman)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);

            if (headBone == null)
                return $"Error: Could not find Head bone on '{avatarRootName}' (humanoid Animator required for stable face capture).";

            var faceSmr = AvatarAnatomyTools.FindFaceSmrInternal(root, out _, out _);
            Bounds focusBounds = ComputeFaceBounds(headBone, faceSmr);

            // 専用カメラを動的生成
            var camGo = new GameObject("__FaceCameraCapture_Temp__")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            try
            {
                var camera = camGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
                camera.fieldOfView = DefaultFov;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100f;
                camera.allowHDR = false;
                camera.allowMSAA = false;

                Vector3 center = focusBounds.center;
                float radius = Mathf.Max(focusBounds.extents.x, focusBounds.extents.y, focusBounds.extents.z);
                if (radius < 0.05f) radius = 0.08f;
                float distance = radius * DefaultDistanceMultiplier;

                // カメラを顔の正面 (Z+ または -Z を avatar の forward とみなす) に配置
                Vector3 forward = ResolveAvatarForward(root.transform);
                Vector3 camPos = center + forward * distance;
                camGo.transform.position = camPos;
                camGo.transform.LookAt(center, Vector3.up);

                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;
                var prevActive = RenderTexture.active;
                var prevTarget = camera.targetTexture;
                camera.targetTexture = rt;

                try
                {
                    camera.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();

                    byte[] pngBytes = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);

                    if (pngBytes == null || pngBytes.Length == 0)
                        return "Error: Failed to encode face preview as PNG.";

                    SceneViewTools.SetPendingImage(pngBytes, "image/png");
                    return $"Success: Captured face preview ({width}x{height}, {pngBytes.Length} bytes). " +
                           $"Center=({center.x:F2},{center.y:F2},{center.z:F2}) distance={distance:F2}. Image attached.";
                }
                finally
                {
                    camera.targetTexture = prevTarget;
                    RenderTexture.active = prevActive;
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            catch (Exception ex)
            {
                return $"Error: Capture failed: {ex.Message}";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static Bounds ComputeFaceBounds(Transform headBone, SkinnedMeshRenderer faceSmr)
        {
            // 優先順位:
            // 1. faceSmr の bounds が headBone を含むなら、それを使う
            // 2. headBone の周辺領域を fallback (半径 0.15m の球)
            if (faceSmr != null && faceSmr.sharedMesh != null)
            {
                var b = faceSmr.bounds;
                if (b.size.sqrMagnitude > 0.0001f)
                {
                    // 顔だけに絞るため、頭から離れすぎている分は切り詰める
                    Vector3 headPos = headBone.position;
                    Vector3 clampedCenter = Vector3.Lerp(b.center, headPos, 0.5f);
                    Vector3 clampedSize = Vector3.Min(b.size, new Vector3(0.4f, 0.4f, 0.4f));
                    return new Bounds(clampedCenter, clampedSize);
                }
            }
            return new Bounds(headBone.position + Vector3.up * 0.05f, new Vector3(0.25f, 0.25f, 0.25f));
        }

        private static Vector3 ResolveAvatarForward(Transform avatarRoot)
        {
            if (avatarRoot == null) return Vector3.forward;
            // VRChat avatar は通常 forward = +Z (Unity 標準)
            // ただし root の rotation が変えられている可能性もあるので transform.forward を使う
            Vector3 fwd = avatarRoot.forward;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            return fwd.normalized;
        }
    }
}
