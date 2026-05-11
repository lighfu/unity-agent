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

        // Compute a tight world-space AABB for any Renderer.
        // For SkinnedMeshRenderer, prefer sharedMesh.bounds (mesh-local) transformed
        // by transform.localToWorldMatrix — avoids the runtime-inflated bounds that
        // SMR uses for skinning safety. For other renderers, use Renderer.bounds.
        private static Bounds ComputeTightBounds(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var localBounds = smr.sharedMesh.bounds;
                var corners = new Vector3[8];
                Vector3 min = localBounds.min, max = localBounds.max;
                corners[0] = new Vector3(min.x, min.y, min.z);
                corners[1] = new Vector3(max.x, min.y, min.z);
                corners[2] = new Vector3(min.x, max.y, min.z);
                corners[3] = new Vector3(max.x, max.y, min.z);
                corners[4] = new Vector3(min.x, min.y, max.z);
                corners[5] = new Vector3(max.x, min.y, max.z);
                corners[6] = new Vector3(min.x, max.y, max.z);
                corners[7] = new Vector3(max.x, max.y, max.z);

                var matrix = smr.transform.localToWorldMatrix;
                Vector3 worldMin = matrix.MultiplyPoint(corners[0]);
                Vector3 worldMax = worldMin;
                for (int i = 1; i < 8; i++)
                {
                    Vector3 wp = matrix.MultiplyPoint(corners[i]);
                    worldMin = Vector3.Min(worldMin, wp);
                    worldMax = Vector3.Max(worldMax, wp);
                }
                var b = new Bounds();
                b.SetMinMax(worldMin, worldMax);
                return b;
            }
            return r.bounds;
        }

        public static void ClearPendingImage()
        {
            PendingImageBytes = null;
            PendingImageMimeType = null;
        }

        /// <summary>
        /// Path of the most recently dumped capture image in %TEMP%.
        /// Useful for AI clients that don't render MCP image attachments inline:
        /// the AI can Read this file path to see the actual image.
        /// </summary>
        public static string LastCaptureDebugPath { get; private set; }

        public static void SetPendingImage(byte[] bytes, string mimeType)
        {
            PendingImageBytes = bytes;
            PendingImageMimeType = mimeType;

            // Always-on debug dump to %TEMP% so any capture-style tool can be
            // visually inspected via the Read tool.
            try
            {
                string ext = (mimeType == "image/jpeg" || mimeType == "image/jpg") ? ".jpg" : ".png";
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unity-agent-last-capture" + ext);
                System.IO.File.WriteAllBytes(path, bytes);
                LastCaptureDebugPath = path;
            }
            catch
            {
                LastCaptureDebugPath = null;
            }
        }

        // ─── Quality / format options shared by all capture-style tools ───
        // Encodes a Texture2D with optional downscale + format choice. Returns null on failure.
        // Caller is responsible for DestroyImmediate-ing the source Texture2D.
        internal static byte[] EncodeWithOptions(Texture2D tex, int maxWidth, string format, int jpgQuality, out string mime)
        {
            mime = "image/png";
            if (tex == null) return null;

            string fmt = (format ?? "png").Trim().ToLowerInvariant();
            bool isJpg = fmt == "jpg" || fmt == "jpeg";
            mime = isJpg ? "image/jpeg" : "image/png";
            int q = Mathf.Clamp(jpgQuality, 1, 100);

            // Optional bilinear downscale (longer side ≤ maxWidth, preserves aspect)
            Texture2D resized = null;
            RenderTexture rt = null;
            Texture2D toEncode = tex;
            try
            {
                if (maxWidth > 0)
                {
                    int longer = Mathf.Max(tex.width, tex.height);
                    if (longer > maxWidth)
                    {
                        float scale = (float)maxWidth / longer;
                        int dw = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale));
                        int dh = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
                        rt = RenderTexture.GetTemporary(dw, dh, 0, RenderTextureFormat.ARGB32);
                        rt.filterMode = FilterMode.Bilinear;
                        var prevActive = RenderTexture.active;
                        Graphics.Blit(tex, rt);
                        RenderTexture.active = rt;
                        resized = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
                        resized.ReadPixels(new Rect(0, 0, dw, dh), 0, 0);
                        resized.Apply();
                        RenderTexture.active = prevActive;
                        toEncode = resized;
                    }
                }
                return isJpg ? toEncode.EncodeToJPG(q) : toEncode.EncodeToPNG();
            }
            finally
            {
                if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Saves bytes to an explicit path (creates dirs as needed). Silent on failure.
        // Returns true if a save was attempted (path non-empty) regardless of success.
        internal static bool TrySaveToPath(byte[] bytes, string saveToPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(saveToPath) || bytes == null || bytes.Length == 0) return false;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(saveToPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(saveToPath, bytes);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return true;
            }
        }

        [AgentTool("Take a screenshot of the current SceneView. " +
            "width/height (default 1024) set the render resolution. " +
            "maxWidth>0 downscales the longer side (preserves aspect). " +
            "format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90). " +
            "saveToPath: optional explicit save path in addition to the auto-attached image. " +
            "Use this to verify object placement, rotation, and visual appearance.")]
        public static string CaptureSceneView(int width = 1024, int height = 1024, int maxWidth = 0, string format = "png", int jpgQuality = 90, string saveToPath = "")
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

            byte[] outBytes = EncodeWithOptions(tex, maxWidth, format, jpgQuality, out string mime);
            UnityEngine.Object.DestroyImmediate(tex);

            if (outBytes == null || outBytes.Length == 0)
                return "Error: Failed to encode SceneView image.";

            SetPendingImage(outBytes, mime);
            string saveMsg = "";
            if (TrySaveToPath(outBytes, saveToPath, out string saveErr))
                saveMsg = saveErr != null ? $" (saveToPath failed: {saveErr})" : $" Saved to '{saveToPath}'.";

            return $"Success: Captured SceneView screenshot ({width}x{height}, {outBytes.Length} bytes, {(mime == "image/jpeg" ? "jpg" : "png")}). The image has been attached for your review.{saveMsg}";
        }

        [AgentTool("Capture a target from multiple angles and compose into a grid image. " +
            "angles: comma-separated from front,back,left,right,top,45left,45right. Default: front,left,right,back. " +
            "cellSize is the per-cell resolution (default 384). " +
            "maxWidth>0 downscales the final composite (preserves aspect). " +
            "format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90). " +
            "saveToPath: optional explicit save path.")]
        public static string CaptureMultiAngle(string targetName, string angles = "front,left,right,back", int cellSize = 384, int maxWidth = 0, string format = "png", int jpgQuality = 90, string saveToPath = "")
        {
            var target = FindGO(targetName);
            if (target == null) return $"Error: GameObject '{targetName}' not found.";

            // Calculate bounds from ACTIVE+ENABLED renderers only (inactive clothing
            // variants etc. would otherwise inflate bounds and push the camera too far away).
            var allRenderers = target.GetComponentsInChildren<Renderer>(true);
            var renderers = allRenderers.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0)
            {
                // Fallback: if nothing is active, use all renderers (better than failing)
                renderers = allRenderers;
                if (renderers.Length == 0)
                    return $"Error: No renderers found under '{targetName}'.";
            }

            // For SkinnedMeshRenderer, .bounds is the runtime-skinned bounding box which
            // includes animation extension and is often inflated. Use sharedMesh.bounds
            // transformed to world space for tighter framing.
            Bounds bounds = ComputeTightBounds(renderers[0]);
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(ComputeTightBounds(renderers[i]));

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
                byte[] outBytes = EncodeWithOptions(composite, maxWidth, format, jpgQuality, out string mime);
                UnityEngine.Object.DestroyImmediate(composite);

                if (outBytes == null || outBytes.Length == 0)
                    return "Error: Failed to encode composite image.";

                SetPendingImage(outBytes, mime);
                string saveMsg = "";
                if (TrySaveToPath(outBytes, saveToPath, out string saveErr))
                    saveMsg = saveErr != null ? $" (saveToPath failed: {saveErr})" : $" Saved to '{saveToPath}'.";

                string labelInfo = string.Join(", ", capturedLabels.Select((l, i) => $"[{i}]={l}"));
                return $"Success: Captured {cellTextures.Count} angles of '{targetName}' in a {cols}x{rows} grid ({gridW}x{gridH}px, {outBytes.Length} bytes, {(mime == "image/jpeg" ? "jpg" : "png")}). Layout (left-to-right, top-to-bottom): {labelInfo}. The image has been attached for your review.{saveMsg}";
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

        [AgentTool("Scan all meshes under an avatar and capture each one ISOLATED (other meshes hidden) into a labeled grid image. " +
            "cellSize is the per-mesh cell resolution (default 256). " +
            "maxWidth>0 downscales the final composite. " +
            "format='png' (lossless, default) or 'jpg' (smaller via jpgQuality 1-100, default 90). " +
            "saveToPath: optional explicit save path. " +
            "Use this BEFORE modifying any mesh to visually identify what each GameObject actually is. Returns image + text mapping.")]
        public static string ScanAvatarMeshes(string avatarRootName, int cellSize = 256, int maxWidth = 0, string format = "png", int jpgQuality = 90, string saveToPath = "")
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            if (allRenderers.Length == 0)
                return $"Error: No renderers found under '{avatarRootName}'.";

            // SCENE-WIDE isolation set: include every Renderer in the active scene so other
            // active avatars don't bleed into the per-mesh capture. Without this, scenes that
            // have multiple active avatar variants (e.g., capra + capra (BBP 4)) would show
            // both avatars in every cell, masking the per-mesh isolation effect.
            var sceneRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            var targetRendererSet = new HashSet<Renderer>(allRenderers);

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

            // Save original enabled states for ALL scene renderers (we toggle them all)
            var originalStates = new bool[sceneRenderers.Length];
            for (int i = 0; i < sceneRenderers.Length; i++)
                originalStates[i] = sceneRenderers[i].enabled;

            try
            {
                for (int idx = 0; idx < rendererList.Count; idx++)
                {
                    var targetRenderer = rendererList[idx].renderer;
                    int vertCount = rendererList[idx].vertCount;

                    // Isolate scene-wide: disable every renderer in the scene, then
                    // enable only the target. This prevents other active avatars or
                    // overlapping meshes from bleeding into the per-mesh capture.
                    for (int j = 0; j < sceneRenderers.Length; j++)
                        sceneRenderers[j].enabled = (sceneRenderers[j] == targetRenderer);

                    // Camera position from target bounds (use tight mesh.bounds for SMR
                    // to avoid runtime-inflated skinning bounds pushing camera too far)
                    var bounds = ComputeTightBounds(targetRenderer);
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

                // Restore original states for every scene renderer we toggled
                for (int j = 0; j < sceneRenderers.Length; j++)
                    sceneRenderers[j].enabled = originalStates[j];

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
                byte[] outBytes = EncodeWithOptions(composite, maxWidth, format, jpgQuality, out string mime);
                UnityEngine.Object.DestroyImmediate(composite);

                if (outBytes == null || outBytes.Length == 0)
                    return "Error: Failed to encode grid image.";

                SetPendingImage(outBytes, mime);
                string saveMsg = "";
                if (TrySaveToPath(outBytes, saveToPath, out string saveErr))
                    saveMsg = saveErr != null ? $" (saveToPath failed: {saveErr})" : $" Saved to '{saveToPath}'.";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Scanned {count} meshes under '{avatarRootName}'.");
                sb.AppendLine($"Grid {cols}x{rows} ({gridW}x{gridH}px, {outBytes.Length} bytes, {(mime == "image/jpeg" ? "jpg" : "png")}), left→right, top→bottom:");
                foreach (var label in labels)
                    sb.AppendLine($"  {label}");
                sb.Append("Image attached. Identify each mesh visually before proceeding.");
                if (!string.IsNullOrEmpty(saveMsg)) sb.Append(saveMsg);
                return sb.ToString();
            }
            finally
            {
                // Restore states in case of exception (scene-wide)
                for (int j = 0; j < sceneRenderers.Length; j++)
                    sceneRenderers[j].enabled = originalStates[j];

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
