using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class SceneViewTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        public static byte[] PendingImageBytes { get; private set; }
        public static string PendingImageMimeType { get; private set; }

        public static void ClearPendingImage()
        {
            PendingImageBytes = null;
            PendingImageMimeType = null;
        }

        public static void SetPendingImage(byte[] bytes, string mimeType)
        {
            PendingImageBytes = bytes;
            PendingImageMimeType = mimeType;
        }

        [AgentTool("Capture a screenshot of the current SceneView. The image is sent to you for visual inspection. Use this to verify object placement, rotation, and visual appearance.")]
        public static string CaptureSceneView(int width = 512, int height = 512)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return "Error: No active SceneView found. Please open a SceneView first.";

            var camera = sceneView.camera;
            if (camera == null)
                return "Error: SceneView camera not available.";

            var rt = new RenderTexture(width, height, 24);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(rt);

            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            if (pngBytes == null || pngBytes.Length == 0)
                return "Error: Failed to capture SceneView image.";

            PendingImageBytes = pngBytes;
            PendingImageMimeType = "image/png";

            return $"Success: Captured SceneView screenshot ({width}x{height}, {pngBytes.Length} bytes). The image has been attached for your review.";
        }

        [AgentTool("Capture a target from multiple angles and compose into a grid image. angles: comma-separated from front,back,left,right,top,45left,45right. Default: front,left,right,back. cellSize is the resolution per cell.")]
        public static string CaptureMultiAngle(string targetName, string angles = "front,left,right,back", int cellSize = 256)
        {
            var target = FindGO(targetName);
            if (target == null) return $"Error: GameObject '{targetName}' not found.";

            // Calculate bounds from all renderers
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return $"Error: No renderers found under '{targetName}'.";

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 center = bounds.center;
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            float distance = maxExtent * 2.5f;

            // Parse angles
            var angleList = angles.Split(',').Select(a => a.Trim().ToLower()).Where(a => !string.IsNullOrEmpty(a)).ToList();
            if (angleList.Count == 0) return "Error: No valid angles specified.";
            if (angleList.Count > 7) return "Error: Maximum 7 angles allowed.";

            // Get SceneView camera settings for clipping planes etc.
            var sceneView = SceneView.lastActiveSceneView;
            float nearClip = 0.01f;
            float farClip = 1000f;
            float fov = 60f;
            if (sceneView != null && sceneView.camera != null)
            {
                nearClip = sceneView.camera.nearClipPlane;
                farClip = sceneView.camera.farClipPlane;
                fov = sceneView.camera.fieldOfView;
            }

            // Create temporary camera
            var camGo = new GameObject("__MultiAngleCaptureCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.enabled = false;

            var rt = new RenderTexture(cellSize, cellSize, 24);
            var cellTextures = new List<Texture2D>();
            var capturedLabels = new List<string>();

            try
            {
                foreach (var angle in angleList)
                {
                    Vector3 dir = GetAngleDirection(angle);
                    if (dir == Vector3.zero)
                    {
                        // Skip unknown angle
                        continue;
                    }

                    cam.transform.position = center - dir * distance;
                    cam.transform.LookAt(center);
                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(cellSize, cellSize, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, cellSize, cellSize), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    cellTextures.Add(tex);
                    capturedLabels.Add(angle);
                }

                if (cellTextures.Count == 0) return "Error: No valid angles could be captured.";

                // Calculate grid layout
                int count = cellTextures.Count;
                int cols, rows;
                if (count <= 2) { cols = count; rows = 1; }
                else if (count == 3) { cols = 3; rows = 1; }
                else if (count == 4) { cols = 2; rows = 2; }
                else if (count <= 6) { cols = 3; rows = 2; }
                else { cols = 4; rows = 2; }

                int gridW = cols * cellSize;
                int gridH = rows * cellSize;
                var composite = new Texture2D(gridW, gridH, TextureFormat.RGB24, false);

                // Fill with dark gray background
                var bgPixels = new Color[gridW * gridH];
                for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = new Color(0.15f, 0.15f, 0.15f);
                composite.SetPixels(bgPixels);

                // Place each cell
                for (int i = 0; i < cellTextures.Count; i++)
                {
                    int col = i % cols;
                    int row = rows - 1 - (i / cols); // top-left origin
                    int x = col * cellSize;
                    int y = row * cellSize;

                    composite.SetPixels(x, y, cellSize, cellSize, cellTextures[i].GetPixels());

                    // Draw a label bar at the bottom of each cell (8px tall dark semi-transparent bar)
                    int barHeight = 8;
                    for (int bx = 0; bx < cellSize; bx++)
                    {
                        for (int by = 0; by < barHeight; by++)
                        {
                            composite.SetPixel(x + bx, y + by, new Color(0, 0, 0, 0.7f));
                        }
                    }
                }

                composite.Apply();
                byte[] pngBytes = composite.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(composite);

                if (pngBytes == null || pngBytes.Length == 0)
                    return "Error: Failed to encode composite image.";

                PendingImageBytes = pngBytes;
                PendingImageMimeType = "image/png";

                string labelInfo = string.Join(", ", capturedLabels.Select((l, i) => $"[{i}]={l}"));
                return $"Success: Captured {cellTextures.Count} angles of '{targetName}' in a {cols}x{rows} grid ({gridW}x{gridH}px). Layout (left-to-right, top-to-bottom): {labelInfo}. The image has been attached for your review.";
            }
            finally
            {
                // Cleanup
                foreach (var tex in cellTextures)
                    UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        [AgentTool("Scan all meshes under an avatar and capture each one ISOLATED (other meshes hidden) into a labeled grid image. Use this BEFORE modifying any mesh to visually identify what each GameObject actually is. Returns image + text mapping.")]
        public static string ScanAvatarMeshes(string avatarRootName, int cellSize = 192)
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            if (allRenderers.Length == 0)
                return $"Error: No renderers found under '{avatarRootName}'.";

            // Sort by vertex count (largest first), limit to 16
            var rendererList = new List<(Renderer renderer, int vertCount)>();
            foreach (var r in allRenderers)
            {
                int verts = 0;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    verts = smr.sharedMesh.vertexCount;
                else if (r is MeshRenderer mr)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        verts = mf.sharedMesh.vertexCount;
                }
                rendererList.Add((r, verts));
            }
            rendererList.Sort((a, b) => b.vertCount.CompareTo(a.vertCount));
            if (rendererList.Count > 16) rendererList.RemoveRange(16, rendererList.Count - 16);

            int count = rendererList.Count;

            // Grid layout
            int cols, rows;
            if (count <= 2) { cols = count; rows = 1; }
            else if (count <= 4) { cols = 2; rows = 2; }
            else if (count <= 6) { cols = 3; rows = 2; }
            else if (count <= 9) { cols = 3; rows = 3; }
            else { cols = 4; rows = (count + 3) / 4; }

            // Camera setup
            var sceneView = SceneView.lastActiveSceneView;
            float nearClip = 0.01f, farClip = 1000f, fov = 60f;
            if (sceneView != null && sceneView.camera != null)
            {
                nearClip = sceneView.camera.nearClipPlane;
                farClip = sceneView.camera.farClipPlane;
                fov = sceneView.camera.fieldOfView;
            }

            var camGo = new GameObject("__ScanMeshCaptureCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            cam.enabled = false;

            var rt = new RenderTexture(cellSize, cellSize, 24);
            var cellTextures = new List<Texture2D>();
            var labels = new List<string>();

            // Save original enabled states
            var originalStates = new bool[allRenderers.Length];
            for (int i = 0; i < allRenderers.Length; i++)
                originalStates[i] = allRenderers[i].enabled;

            try
            {
                for (int idx = 0; idx < rendererList.Count; idx++)
                {
                    var targetRenderer = rendererList[idx].renderer;
                    int vertCount = rendererList[idx].vertCount;

                    // Isolate: disable all, enable only target
                    for (int j = 0; j < allRenderers.Length; j++)
                        allRenderers[j].enabled = (allRenderers[j] == targetRenderer);

                    // Camera position from target bounds
                    var bounds = targetRenderer.bounds;
                    float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    float distance = maxExtent * 2.5f;
                    if (distance < 0.1f) distance = 0.5f;
                    Vector3 dir = GetAngleDirection("45right");
                    cam.transform.position = bounds.center - dir * distance;
                    cam.transform.LookAt(bounds.center);

                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(cellSize, cellSize, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, cellSize, cellSize), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    cellTextures.Add(tex);

                    // Label info
                    string matName = targetRenderer.sharedMaterial != null ? targetRenderer.sharedMaterial.name : "none";
                    string goName = targetRenderer.gameObject.name;
                    labels.Add($"[{idx + 1}] {goName} — {vertCount:N0} verts, mat: {matName}");
                }

                // Restore original states
                for (int j = 0; j < allRenderers.Length; j++)
                    allRenderers[j].enabled = originalStates[j];

                // Composite grid
                int gridW = cols * cellSize;
                int gridH = rows * cellSize;
                var composite = new Texture2D(gridW, gridH, TextureFormat.RGB24, false);

                var bgPixels = new Color[gridW * gridH];
                for (int i = 0; i < bgPixels.Length; i++) bgPixels[i] = new Color(0.15f, 0.15f, 0.15f);
                composite.SetPixels(bgPixels);

                for (int i = 0; i < cellTextures.Count; i++)
                {
                    int col = i % cols;
                    int row = rows - 1 - (i / cols);
                    int x = col * cellSize;
                    int y = row * cellSize;
                    composite.SetPixels(x, y, cellSize, cellSize, cellTextures[i].GetPixels());

                    // Dark label bar at bottom of cell
                    int barHeight = 8;
                    for (int bx = 0; bx < cellSize; bx++)
                        for (int by = 0; by < barHeight; by++)
                            composite.SetPixel(x + bx, y + by, new Color(0, 0, 0, 0.7f));
                }

                composite.Apply();
                byte[] pngBytes = composite.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(composite);

                if (pngBytes == null || pngBytes.Length == 0)
                    return "Error: Failed to encode grid image.";

                PendingImageBytes = pngBytes;
                PendingImageMimeType = "image/png";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Scanned {count} meshes under '{avatarRootName}'.");
                sb.AppendLine($"Grid {cols}x{rows} ({gridW}x{gridH}px), left→right, top→bottom:");
                foreach (var label in labels)
                    sb.AppendLine($"  {label}");
                sb.Append("Image attached. Identify each mesh visually before proceeding.");
                return sb.ToString();
            }
            finally
            {
                // Restore states in case of exception
                for (int j = 0; j < allRenderers.Length; j++)
                    allRenderers[j].enabled = originalStates[j];

                foreach (var tex in cellTextures)
                    UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static Vector3 GetAngleDirection(string angle)
        {
            switch (angle)
            {
                case "front": return Vector3.forward;
                case "back": return Vector3.back;
                case "left": return Vector3.left;
                case "right": return Vector3.right;
                case "top": return Vector3.up;
                case "45left": return (Vector3.forward + Vector3.left).normalized;
                case "45right": return (Vector3.forward + Vector3.right).normalized;
                default: return Vector3.zero;
            }
        }
    }
}
