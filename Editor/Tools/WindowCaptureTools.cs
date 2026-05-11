#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    // Windows-only EditorWindow / monitor capture tools.
    // Pipeline: BitBlt screen rect → BGRA32 byte[] → Texture2D(BGRA32) → PNG → SceneViewTools.SetPendingImage
    // EditorWindow capture uses Focus()+Repaint() to bring the target into view, then captures
    // the on-screen rect derived from EditorWindow.position * EditorGUIUtility.pixelsPerPoint.
    public static class WindowCaptureTools
    {
        [AgentTool("List all currently loaded EditorWindow instances (settings windows / panels / inspectors) with title, type and screen rect. " +
            "Call this before CaptureEditorWindow to discover available titles for screenshot/capture. " +
            "Returns: indexed lines like '[0] [TypeName] \"Title\" pos=(x,y) size=(WxH)'. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListEditorWindows()
        {
            var windows = EnumerateValidEditorWindows();
            if (windows.Count == 0) return "No EditorWindow instances found.";

            var sb = new StringBuilder();
            sb.AppendLine($"EditorWindows: {windows.Count} found");
            sb.AppendLine("---");
            for (int i = 0; i < windows.Count; i++)
            {
                var w = windows[i];
                string typeName = w.GetType().Name;
                string title = w.titleContent != null ? w.titleContent.text : "(untitled)";
                Rect p = w.position;
                sb.AppendLine($"[{i}] [{typeName}] \"{title}\" pos=({p.x:F0},{p.y:F0}) size=({p.width:F0}x{p.height:F0})");
            }
            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all physical display monitors with device name, primary flag, virtual-screen position, resolution, and per-monitor DPI/scale (for screenshot scaling decisions). " +
            "Call before CaptureMonitor to choose monitorId. " +
            "Also reports Unity's EditorGUIUtility.pixelsPerPoint for diagnostics. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListMonitors()
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var monitors = WindowCaptureNative.EnumerateMonitors();
                if (monitors.Count == 0) return "No monitors detected.";

                var sb = new StringBuilder();
                sb.AppendLine($"Monitors: {monitors.Count} found");
                sb.AppendLine("---");
                for (int i = 0; i < monitors.Count; i++)
                {
                    var m = monitors[i];
                    string primary = m.IsPrimary ? " (Primary)" : "";
                    float scale = m.DpiX / 96f;
                    sb.AppendLine($"[{i}] {m.DeviceName}{primary} pos=({m.X},{m.Y}) size=({m.Width}x{m.Height}) DPI={m.DpiX} scale={scale:F2}x");
                }
                sb.AppendLine($"Unity EditorGUIUtility.pixelsPerPoint = {EditorGUIUtility.pixelsPerPoint:F2}");
                return sb.ToString().TrimEnd();
            }
        }

        [AgentTool("Take a screenshot of an EditorWindow (settings panel, Inspector, Console, custom tool window, etc.) whose title contains the given substring. " +
            "The window is focused (tab activated / brought to front) before capture. " +
            "RECOMMENDED: set waitForRepaint=true when targeting a docked tab that may not be currently active — it forces a synchronous Unity repaint via internal HostView.RepaintImmediately() so the newly-activated tab renders before BitBlt (without this, you may capture the previously-active tab in the same dock). " +
            "matchIndex (0-based) disambiguates if multiple windows match the title. " +
            "maxWidth>0: downscale (bilinear) so the LONGER side is at most maxWidth pixels (preserves aspect — useful for token reduction; e.g. maxWidth=1920 halves a 4K capture). " +
            "format='png' (lossless, default) or 'jpg' (much smaller for UI screenshots, lossy via jpgQuality 1-100, default 90). " +
            "saveToPath: optional absolute file path to also save the encoded bytes. " +
            "Result message includes a 'Debug copy at' path (in %TEMP%) which can be used with the Read tool to view the image directly. " +
            "Recommended for token economy: maxWidth=1920, format='jpg', jpgQuality=85. " +
            "Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string CaptureEditorWindow(
            string titleContains,
            int matchIndex = 0,
            bool waitForRepaint = false,
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "")
        {
            if (string.IsNullOrEmpty(titleContains)) return "Error: titleContains is empty.";

            using (new WindowCaptureNative.DpiScope())
            {
                var all = EnumerateValidEditorWindows();
                var matches = all.Where(w => w.titleContent != null
                                          && !string.IsNullOrEmpty(w.titleContent.text)
                                          && w.titleContent.text.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                                 .ToList();

                if (matches.Count == 0)
                {
                    string available = string.Join(", ",
                        all.Select(w => $"\"{w.titleContent?.text}\"").Distinct().Take(20));
                    return $"Error: No EditorWindow whose title contains '{titleContains}'. Available: {available}";
                }
                if (matchIndex < 0 || matchIndex >= matches.Count)
                {
                    string titles = string.Join(", ",
                        matches.Select((w, i) => $"[{i}] \"{w.titleContent.text}\""));
                    return $"Error: matchIndex {matchIndex} out of range (matches={matches.Count}: {titles}).";
                }

                var window = matches[matchIndex];
                try { window.Focus(); window.Repaint(); }
                catch { /* swallow — best-effort focus */ }

                if (waitForRepaint)
                {
                    // Force a synchronous repaint of the focused window via internal HostView.RepaintImmediately().
                    // Plain Thread.Sleep here would block the main thread and prevent Unity from rendering at all.
                    TryRepaintImmediately(window);
                }

                Rect posPt = window.position;
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var (x, y, w, h) = WindowCaptureNative.UnityRectToPhysical(posPt.x, posPt.y, posPt.width, posPt.height, monitors);
                if (w <= 0 || h <= 0)
                    return $"Error: Window '{window.titleContent.text}' has zero-size rect (may be minimized/closed).";

                byte[] pixels;
                try
                {
                    pixels = WindowCaptureNative.CaptureScreenRect(x, y, w, h, includeLayeredWindows: false);
                }
                catch (Exception ex)
                {
                    return $"Error: Capture failed: {ex.Message}";
                }

                return EncodeAndAttach(pixels, w, h, $"EditorWindow '{window.titleContent.text}'", maxWidth, format, jpgQuality, saveToPath);
            }
        }

        [AgentTool("Take a screenshot of an entire physical monitor / display. " +
            "monitorId='primary' (default) selects the primary display; an integer index like '0' selects by EnumDisplayMonitors order; or a device name like '\\\\.\\DISPLAY1' selects exactly. " +
            "maxWidth>0: downscale (bilinear) so the LONGER side is at most maxWidth pixels (preserves aspect — strongly recommended for 4K displays to reduce 4MB PNG to ~250KB JPG; e.g. maxWidth=1920 halves a 4K capture). " +
            "format='png' (lossless, default) or 'jpg' (much smaller for full-screen captures, lossy via jpgQuality 1-100, default 90). " +
            "saveToPath: optional absolute file path to also save the encoded bytes. " +
            "Result message includes a 'Debug copy at' path (in %TEMP%) which can be used with the Read tool to view the image directly. " +
            "Recommended for token economy on 4K: maxWidth=1920, format='jpg', jpgQuality=85. " +
            "Call ListMonitors first to see available IDs and per-monitor DPI. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string CaptureMonitor(
            string monitorId = "primary",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "")
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var resolved = WindowCaptureNative.ResolveMonitor(monitorId, monitors);
                if (resolved == null)
                {
                    string ids = string.Join(", ",
                        monitors.Select((m, i) => $"\"{i}\"({m.DeviceName}{(m.IsPrimary ? "/Primary" : "")})"));
                    return $"Error: monitorId '{monitorId}' not found. Available: {ids}";
                }
                var m = resolved.Value;

                byte[] pixels;
                try
                {
                    pixels = WindowCaptureNative.CaptureScreenRect(m.X, m.Y, m.Width, m.Height, includeLayeredWindows: true);
                }
                catch (Exception ex)
                {
                    return $"Error: Capture failed: {ex.Message}";
                }

                return EncodeAndAttach(pixels, m.Width, m.Height, $"Monitor '{m.DeviceName}'", maxWidth, format, jpgQuality, saveToPath);
            }
        }

        // ─── Helpers ───

        // Reflection helper — use HostView.RepaintImmediately() to synchronously force a paint
        // of the (newly-focused) tab. Without this, BitBlt captures the previously visible tab.
        private static void TryRepaintImmediately(EditorWindow window)
        {
            try
            {
                var parentField = typeof(EditorWindow).GetField("m_Parent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (parentField == null) return;
                var hostView = parentField.GetValue(window);
                if (hostView == null) return;
                var method = hostView.GetType().GetMethod("RepaintImmediately",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method == null) return;
                method.Invoke(hostView, null);
            }
            catch
            {
                // Best-effort — internal API may change between Unity versions.
            }
        }

        private static List<EditorWindow> EnumerateValidEditorWindows()
        {
            var all = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var list = new List<EditorWindow>(all.Length);
            foreach (var w in all)
            {
                if (w == null) continue;
                if (w.GetType().IsAbstract) continue;
                if (w.titleContent == null || string.IsNullOrEmpty(w.titleContent.text)) continue;
                if (w.position.width <= 0 || w.position.height <= 0) continue;
                list.Add(w);
            }
            return list;
        }

        private static string EncodeAndAttach(byte[] bgraPixels, int width, int height, string label,
            int maxWidth, string format, int jpgQuality, string saveToPath)
        {
            // Normalize encoding params
            string fmt = (format ?? "png").Trim().ToLowerInvariant();
            bool isJpg = fmt == "jpg" || fmt == "jpeg";
            string mime = isJpg ? "image/jpeg" : "image/png";
            string ext = isJpg ? ".jpg" : ".png";
            int q = Mathf.Clamp(jpgQuality, 1, 100);

            // Decide output dimensions (downscale longer side to maxWidth, preserve aspect)
            int outW = width, outH = height;
            if (maxWidth > 0)
            {
                int longer = Mathf.Max(width, height);
                if (longer > maxWidth)
                {
                    float scale = (float)maxWidth / longer;
                    outW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                    outH = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                }
            }

            Texture2D srcTex = null;
            Texture2D dstTex = null;
            RenderTexture rt = null;
            try
            {
                srcTex = new Texture2D(width, height, TextureFormat.BGRA32, false);
                srcTex.LoadRawTextureData(bgraPixels);
                srcTex.Apply();

                Texture2D toEncode;
                if (outW == width && outH == height)
                {
                    toEncode = srcTex;
                }
                else
                {
                    // Bilinear downscale via Graphics.Blit + RenderTexture
                    rt = RenderTexture.GetTemporary(outW, outH, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Bilinear;
                    var prevActive = RenderTexture.active;
                    Graphics.Blit(srcTex, rt);
                    RenderTexture.active = rt;
                    dstTex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
                    dstTex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
                    dstTex.Apply();
                    RenderTexture.active = prevActive;
                    toEncode = dstTex;
                }

                byte[] encodedBytes = isJpg ? toEncode.EncodeToJPG(q) : toEncode.EncodeToPNG();
                if (encodedBytes == null || encodedBytes.Length == 0)
                    return $"Error: Failed to encode capture as {fmt.ToUpper()}.";

                SceneViewTools.SetPendingImage(encodedBytes, mime);
                // Debug dump is now centralized in SetPendingImage — read path from LastCaptureDebugPath.
                string debugPath = SceneViewTools.LastCaptureDebugPath;

                // Optional explicit saveToPath
                string explicitSaveMsg = string.Empty;
                if (!string.IsNullOrWhiteSpace(saveToPath))
                {
                    try
                    {
                        var dir = System.IO.Path.GetDirectoryName(saveToPath);
                        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir);
                        System.IO.File.WriteAllBytes(saveToPath, encodedBytes);
                        explicitSaveMsg = $" Saved to '{saveToPath}'.";
                    }
                    catch (Exception ex)
                    {
                        explicitSaveMsg = $" (saveToPath failed: {ex.Message})";
                    }
                }

                string sizeMsg = (outW != width || outH != height)
                    ? $"{width}x{height} → {outW}x{outH}"
                    : $"{outW}x{outH}";
                string debugMsg = debugPath != null ? $" Debug copy at '{debugPath}'." : string.Empty;
                return $"Success: Captured {label} ({sizeMsg}, {encodedBytes.Length} bytes, {fmt}). The image has been attached for your review.{explicitSaveMsg}{debugMsg}";
            }
            finally
            {
                if (srcTex != null) UnityEngine.Object.DestroyImmediate(srcTex);
                if (dstTex != null) UnityEngine.Object.DestroyImmediate(dstTex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
#endif
