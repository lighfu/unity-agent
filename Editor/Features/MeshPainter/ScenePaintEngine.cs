using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class ScenePaintEngine
    {
        /// <summary>
        /// Raycast against the baked mesh using Moller-Trumbore.
        /// Returns hit info in world space + interpolated UV.
        /// </summary>
        public static bool RaycastMesh(Ray worldRay, out Vector3 hitWorld, out Vector3 hitNormal,
                                        out Vector2 hitUV, out int hitTriIndex)
        {
            hitWorld = Vector3.zero;
            hitNormal = Vector3.up;
            hitUV = Vector2.zero;
            hitTriIndex = -1;

            var renderer = ScenePaintState.ActiveRenderer;
            var verts = ScenePaintState.WorldVertices;
            var uvs = ScenePaintState.UVs;
            var tris = ScenePaintState.Triangles;

            if (renderer == null || verts == null || uvs == null || tris == null)
                return false;

            float closestDist = float.MaxValue;
            float closestU = 0, closestV = 0;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v0 = verts[tris[i]];
                Vector3 v1 = verts[tris[i + 1]];
                Vector3 v2 = verts[tris[i + 2]];

                if (RayTriangleIntersect(worldRay.origin, worldRay.direction, v0, v1, v2,
                                         out float dist, out float u, out float v))
                {
                    if (dist > 0 && dist < closestDist)
                    {
                        closestDist = dist;
                        hitTriIndex = i / 3;
                        closestU = u;
                        closestV = v;
                    }
                }
            }

            if (hitTriIndex < 0) return false;

            int ti = hitTriIndex * 3;
            float w0 = 1f - closestU - closestV;

            // Interpolate UV
            hitUV = uvs[tris[ti]] * w0 + uvs[tris[ti + 1]] * closestU + uvs[tris[ti + 2]] * closestV;

            // Hit world position
            hitWorld = worldRay.origin + worldRay.direction * closestDist;

            // Compute face normal
            Vector3 e1 = verts[tris[ti + 1]] - verts[tris[ti]];
            Vector3 e2 = verts[tris[ti + 2]] - verts[tris[ti]];
            hitNormal = Vector3.Cross(e1, e2).normalized;

            return true;
        }

        /// <summary>
        /// Stamp brush at a UV center position.
        /// </summary>
        public static void StampBrush(Vector2 centerUV, int hitTriIndex)
        {
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;
            float opacity = ScenePaintState.BrushOpacity;
            float hardness = ScenePaintState.BrushHardness;
            var acc = ScenePaintState.StrokeAccumulator;
            if (acc == null) return;

            float pixelRadius = ComputePixelRadius(hitTriIndex);
            if (pixelRadius < 0.5f) pixelRadius = 0.5f;

            float cx = centerUV.x * w;
            float cy = centerUV.y * h;
            float radiusSq = pixelRadius * pixelRadius;

            int minX = Mathf.Clamp(Mathf.FloorToInt(cx - pixelRadius) - 1, 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + pixelRadius) + 1, 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(cy - pixelRadius) - 1, 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + pixelRadius) + 1, 0, h - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float dy = (y + 0.5f) - cy;
                    float distSq = dx * dx + dy * dy;

                    if (distSq > radiusSq) continue;

                    // Island mask check
                    if (ScenePaintState.IslandMaskEnabled && ScenePaintState.MaskedTriangles != null)
                    {
                        if (!IsPixelInMaskedTriangle(x, y))
                            continue;
                    }

                    // Hardness falloff using smoothstep
                    float dist = Mathf.Sqrt(distSq);
                    float normalizedDist = dist / pixelRadius;
                    float falloff;
                    if (normalizedDist <= hardness)
                    {
                        falloff = 1f;
                    }
                    else
                    {
                        float t = (normalizedDist - hardness) / (1f - hardness + 0.001f);
                        t = Mathf.Clamp01(t);
                        // smoothstep
                        falloff = 1f - (t * t * (3f - 2f * t));
                    }

                    float strength = falloff * opacity;
                    int idx = y * w + x;
                    acc[idx] = Mathf.Max(acc[idx], strength);
                }
            }

            // Expand dirty rect
            ExpandDirtyRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Interpolate between two UV positions and stamp at intervals.
        /// Dispatches to the correct stamp method based on active tool.
        /// </summary>
        public static void InterpolateAndStamp(Vector2 fromUV, Vector2 toUV, int hitTriIndex)
        {
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;

            float pixelRadius = ComputePixelRadius(hitTriIndex);
            if (pixelRadius < 0.5f) pixelRadius = 0.5f;

            float step = pixelRadius * 0.25f / Mathf.Max(w, h);
            if (step < 1e-6f) step = 1e-6f;

            Vector2 diff = toUV - fromUV;
            float dist = diff.magnitude;
            if (dist < 1e-8f)
            {
                DispatchStampPublic(toUV, hitTriIndex);
                return;
            }

            Vector2 dir = diff / dist;
            float traveled = 0;
            while (traveled < dist)
            {
                Vector2 pos = fromUV + dir * traveled;
                DispatchStampPublic(pos, hitTriIndex);
                traveled += step;
            }
            DispatchStampPublic(toUV, hitTriIndex);
        }

        /// <summary>
        /// Dispatch stamp to the correct method based on active tool.
        /// </summary>
        public static void DispatchStampPublic(Vector2 uv, int hitTriIndex)
        {
            switch (ScenePaintState.ActiveTool)
            {
                case BrushTool.Blur:
                case BrushTool.Sharpen: // reuses blur kernel, difference is in RecomposeDirtyRegion
                    StampBlur(uv, hitTriIndex);
                    break;
                case BrushTool.Smudge:
                    StampSmudge(uv, hitTriIndex);
                    break;
                default:
                    StampBrush(uv, hitTriIndex);
                    break;
            }
        }

        /// <summary>
        /// Recompose only the dirty region of the display texture.
        /// </summary>
        public static void RecomposeDirtyRegion()
        {
            var state = ScenePaintState.DirtyRect;
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;
            var basePixels = ScenePaintState.BasePixels;
            var display = ScenePaintState.DisplayTexture;
            if (basePixels == null || display == null) return;

            var tool = ScenePaintState.ActiveTool;

            // Smudge writes directly to SmudgeWorkPixels, just copy to display
            if (tool == BrushTool.Smudge)
            {
                var smudge = ScenePaintState.SmudgeWorkPixels;
                if (smudge == null) return;

                int minX = Mathf.Max(state.x, 0);
                int minY = Mathf.Max(state.y, 0);
                int maxX = Mathf.Min(state.x + state.width, w - 1);
                int maxY = Mathf.Min(state.y + state.height, h - 1);
                if (minX > maxX || minY > maxY) return;

                Color32[] pixels = display.GetPixels32();
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                        pixels[y * w + x] = smudge[y * w + x];
                display.SetPixels32(pixels);
                display.Apply();
                return;
            }

            var acc = ScenePaintState.StrokeAccumulator;
            if (acc == null) return;

            // Clamp dirty rect
            {
                int minX = Mathf.Max(state.x, 0);
                int minY = Mathf.Max(state.y, 0);
                int maxX = Mathf.Min(state.x + state.width, w - 1);
                int maxY = Mathf.Min(state.y + state.height, h - 1);

                if (minX > maxX || minY > maxY) return;

                Color brushColor = ScenePaintState.BrushColor;
                int blendMode = ScenePaintState.BlendModeIndex;
                var originalPixels = ScenePaintState.OriginalPixels;

                // Force blend mode for Dodge/Burn
                if (tool == BrushTool.Dodge) blendMode = 4;
                else if (tool == BrushTool.Burn) blendMode = 5;

                // Pre-compute brush HSV for Tint tool
                float brushH = 0, brushS = 0, brushV = 0;
                if (tool == BrushTool.Tint)
                    Color.RGBToHSV(brushColor, out brushH, out brushS, out brushV);

                Color32[] pixels = display.GetPixels32();

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int idx = y * w + x;
                        float a = acc[idx];
                        if (a < 0.001f) continue;

                        Color32 baseC = basePixels[idx];

                        switch (tool)
                        {
                            case BrushTool.Eraser:
                                if (originalPixels != null)
                                    pixels[idx] = Color32.Lerp(baseC, originalPixels[idx], a);
                                break;

                            case BrushTool.Blur:
                            {
                                var blurred = ScenePaintState.BlurredPixels;
                                if (blurred != null)
                                    pixels[idx] = Color32.Lerp(baseC, blurred[idx], a);
                                break;
                            }

                            case BrushTool.Sharpen:
                            {
                                var blurred = ScenePaintState.BlurredPixels;
                                if (blurred != null)
                                {
                                    // Unsharp mask: sharpened = 2 * base - blurred
                                    Color32 bl = blurred[idx];
                                    byte sr = (byte)Mathf.Clamp(baseC.r * 2 - bl.r, 0, 255);
                                    byte sg = (byte)Mathf.Clamp(baseC.g * 2 - bl.g, 0, 255);
                                    byte sb = (byte)Mathf.Clamp(baseC.b * 2 - bl.b, 0, 255);
                                    Color32 sharpened = new Color32(sr, sg, sb, baseC.a);
                                    pixels[idx] = Color32.Lerp(baseC, sharpened, a);
                                }
                                break;
                            }

                            case BrushTool.Clone:
                            {
                                var offset = ScenePaintState.CloneOffset;
                                int sx = x - offset.x;
                                int sy = y - offset.y;
                                if (sx >= 0 && sx < w && sy >= 0 && sy < h)
                                {
                                    Color32 sourceColor = basePixels[sy * w + sx];
                                    pixels[idx] = Color32.Lerp(baseC, sourceColor, a);
                                }
                                break;
                            }

                            case BrushTool.Tint:
                            {
                                // Apply brush hue/saturation, keep base luminance
                                Color baseF = (Color)baseC;
                                Color.RGBToHSV(baseF, out _, out _, out float baseV);
                                Color tinted = Color.HSVToRGB(brushH, brushS, baseV);
                                tinted.a = baseF.a;
                                pixels[idx] = Color.Lerp(baseF, tinted, a);
                                break;
                            }

                            case BrushTool.Noise:
                            {
                                // Deterministic noise based on pixel position + stroke hash
                                int hash = idx * 1471 + ScenePaintState.UndoGroup * 7919;
                                hash = (hash ^ (hash >> 16)) * 0x45d9f3b;
                                float noise = ((hash & 0xFF) / 255f - 0.5f) * 0.3f;
                                Color baseF = (Color)baseC;
                                Color noisy = new Color(
                                    Mathf.Clamp01(baseF.r + noise),
                                    Mathf.Clamp01(baseF.g + noise),
                                    Mathf.Clamp01(baseF.b + noise),
                                    baseF.a);
                                pixels[idx] = Color.Lerp(baseF, noisy, a);
                                break;
                            }

                            case BrushTool.Saturate:
                            {
                                Color baseF = (Color)baseC;
                                Color.RGBToHSV(baseF, out float h2, out float s2, out float v2);
                                s2 = Mathf.Clamp01(s2 + 0.5f);
                                Color saturated = Color.HSVToRGB(h2, s2, v2);
                                saturated.a = baseF.a;
                                pixels[idx] = Color.Lerp(baseF, saturated, a);
                                break;
                            }

                            case BrushTool.Desaturate:
                            {
                                Color baseF = (Color)baseC;
                                float gray = baseF.r * 0.299f + baseF.g * 0.587f + baseF.b * 0.114f;
                                Color grayColor = new Color(gray, gray, gray, baseF.a);
                                pixels[idx] = Color.Lerp(baseF, grayColor, a);
                                break;
                            }

                            default:
                            {
                                // Paint, Dodge, Burn
                                Color blended = ApplyBlend(baseC, brushColor, blendMode, a);
                                pixels[idx] = blended;
                                break;
                            }
                        }
                    }
                }

                display.SetPixels32(pixels);
                display.Apply();
            }
        }

        /// <summary>
        /// Stamp blur brush at a UV center position.
        /// Computes local average for each pixel in the brush.
        /// </summary>
        public static void StampBlur(Vector2 centerUV, int hitTriIndex)
        {
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;
            float opacity = ScenePaintState.BrushOpacity;
            float hardness = ScenePaintState.BrushHardness;
            var acc = ScenePaintState.StrokeAccumulator;
            var basePixels = ScenePaintState.BasePixels;
            var blurred = ScenePaintState.BlurredPixels;
            if (acc == null || basePixels == null || blurred == null) return;

            float pixelRadius = ComputePixelRadius(hitTriIndex);
            if (pixelRadius < 0.5f) pixelRadius = 0.5f;

            int kernelRadius = Mathf.Max(2, Mathf.RoundToInt(pixelRadius * 0.3f));

            float cx = centerUV.x * w;
            float cy = centerUV.y * h;
            float radiusSq = pixelRadius * pixelRadius;

            int minX = Mathf.Clamp(Mathf.FloorToInt(cx - pixelRadius) - 1, 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + pixelRadius) + 1, 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(cy - pixelRadius) - 1, 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + pixelRadius) + 1, 0, h - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float dy = (y + 0.5f) - cy;
                    float distSq = dx * dx + dy * dy;
                    if (distSq > radiusSq) continue;

                    if (ScenePaintState.IslandMaskEnabled && ScenePaintState.MaskedTriangles != null)
                    {
                        if (!IsPixelInMaskedTriangle(x, y))
                            continue;
                    }

                    // Compute kernel average from BasePixels
                    int sumR = 0, sumG = 0, sumB = 0, count = 0;
                    int kMinX = Mathf.Max(x - kernelRadius, 0);
                    int kMaxX = Mathf.Min(x + kernelRadius, w - 1);
                    int kMinY = Mathf.Max(y - kernelRadius, 0);
                    int kMaxY = Mathf.Min(y + kernelRadius, h - 1);
                    for (int ky = kMinY; ky <= kMaxY; ky++)
                    {
                        for (int kx = kMinX; kx <= kMaxX; kx++)
                        {
                            Color32 kc = basePixels[ky * w + kx];
                            sumR += kc.r; sumG += kc.g; sumB += kc.b;
                            count++;
                        }
                    }

                    int idx = y * w + x;
                    if (count > 0)
                        blurred[idx] = new Color32((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count), basePixels[idx].a);

                    // Hardness falloff
                    float dist = Mathf.Sqrt(distSq);
                    float normalizedDist = dist / pixelRadius;
                    float falloff;
                    if (normalizedDist <= hardness)
                        falloff = 1f;
                    else
                    {
                        float t = (normalizedDist - hardness) / (1f - hardness + 0.001f);
                        t = Mathf.Clamp01(t);
                        falloff = 1f - (t * t * (3f - 2f * t));
                    }

                    float strength = falloff * opacity;
                    acc[idx] = Mathf.Max(acc[idx], strength);
                }
            }

            ExpandDirtyRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Stamp smudge brush at a UV center position.
        /// Blends carry color into work pixels, then updates carry color.
        /// </summary>
        public static void StampSmudge(Vector2 centerUV, int hitTriIndex)
        {
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;
            float opacity = ScenePaintState.BrushOpacity;
            float hardness = ScenePaintState.BrushHardness;
            var work = ScenePaintState.SmudgeWorkPixels;
            if (work == null) return;

            float pixelRadius = ComputePixelRadius(hitTriIndex);
            if (pixelRadius < 0.5f) pixelRadius = 0.5f;

            float cx = centerUV.x * w;
            float cy = centerUV.y * h;
            float radiusSq = pixelRadius * pixelRadius;

            int minX = Mathf.Clamp(Mathf.FloorToInt(cx - pixelRadius) - 1, 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + pixelRadius) + 1, 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(cy - pixelRadius) - 1, 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + pixelRadius) + 1, 0, h - 1);

            Color carry = ScenePaintState.SmudgeCarryColor;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) - cx;
                    float dy = (y + 0.5f) - cy;
                    float distSq = dx * dx + dy * dy;
                    if (distSq > radiusSq) continue;

                    if (ScenePaintState.IslandMaskEnabled && ScenePaintState.MaskedTriangles != null)
                    {
                        if (!IsPixelInMaskedTriangle(x, y))
                            continue;
                    }

                    float dist = Mathf.Sqrt(distSq);
                    float normalizedDist = dist / pixelRadius;
                    float falloff;
                    if (normalizedDist <= hardness)
                        falloff = 1f;
                    else
                    {
                        float t = (normalizedDist - hardness) / (1f - hardness + 0.001f);
                        t = Mathf.Clamp01(t);
                        falloff = 1f - (t * t * (3f - 2f * t));
                    }

                    float strength = falloff * opacity;
                    int idx = y * w + x;
                    Color current = work[idx];
                    Color newColor = Color.Lerp(current, carry, strength);
                    work[idx] = newColor;
                }
            }

            // Update carry color from brush center
            int centerPx = Mathf.Clamp(Mathf.RoundToInt(cx), 0, w - 1);
            int centerPy = Mathf.Clamp(Mathf.RoundToInt(cy), 0, h - 1);
            int centerIdx = centerPy * w + centerPx;
            Color centerColor = work[centerIdx];
            ScenePaintState.SmudgeCarryColor = Color.Lerp(carry, centerColor, 0.5f);

            ExpandDirtyRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Compute mirror UV for symmetry painting.
        /// Mirrors across the avatar's local X axis.
        /// </summary>
        public static bool ComputeMirrorUV(Vector3 hitWorldPos, SceneView sceneView,
            out Vector2 mirrorUV, out int mirrorTriIndex)
        {
            mirrorUV = Vector2.zero;
            mirrorTriIndex = -1;

            var avatarRoot = ScenePaintState.AvatarRoot;
            if (avatarRoot == null || sceneView == null) return false;

            // World → avatar local, flip X, back to world
            Vector3 localPos = avatarRoot.transform.InverseTransformPoint(hitWorldPos);
            localPos.x = -localPos.x;
            Vector3 mirrorWorld = avatarRoot.transform.TransformPoint(localPos);

            // Raycast from camera direction toward mirror point
            Vector3 camDir = (mirrorWorld - sceneView.camera.transform.position).normalized;
            Ray mirrorRay = new Ray(mirrorWorld - camDir * 0.1f, camDir);

            return RaycastMesh(mirrorRay, out _, out _, out mirrorUV, out mirrorTriIndex);
        }

        /// <summary>
        /// Commit the current stroke in memory only (no disk I/O).
        /// Just bakes the stroke result into BasePixels for the next stroke.
        /// </summary>
        public static void CommitStroke()
        {
            var display = ScenePaintState.DisplayTexture;
            if (display == null) return;

            // For Smudge: SmudgeWorkPixels is the final result
            if (ScenePaintState.ActiveTool == BrushTool.Smudge && ScenePaintState.SmudgeWorkPixels != null)
            {
                display.SetPixels32(ScenePaintState.SmudgeWorkPixels);
                display.Apply();
                ScenePaintState.SmudgeWorkPixels = null;
            }

            // Bake stroke into base pixels for next stroke
            ScenePaintState.BasePixels = display.GetPixels32();
            ScenePaintState.StrokeAccumulator = null;
        }

        /// <summary>
        /// Save the current DisplayTexture to disk as PNG.
        /// Called on deactivate / explicit save — NOT on every stroke.
        /// When <paramref name="keepPaintingAfterSave"/> is true (default), a fresh
        /// DisplayTexture is re-created so the user can keep painting; when false
        /// (called from Deactivate), the material is left pointing at the saved disk
        /// asset so Deactivate can destroy the runtime tex without leaving mat with
        /// a dangling reference.
        /// </summary>
        public static void SaveDisplayTexture(bool keepPaintingAfterSave = true)
        {
            var display = ScenePaintState.DisplayTexture;
            var renderer = ScenePaintState.ActiveRenderer;
            var avatarRoot = ScenePaintState.AvatarRoot;
            var mat = ScenePaintState.PaintMaterial;

            if (display == null || renderer == null || avatarRoot == null || mat == null) return;

            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName)) avatarName = avatarRoot.name;

            string texName = renderer.gameObject.name;
            string savedPath = TextureUtility.SaveTexture(display, avatarName, texName);
            if (!string.IsNullOrEmpty(savedPath))
            {
                var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(savedPath);
                if (savedTex != null)
                    mat.mainTexture = savedTex;
            }

            // Save metadata
            var metadata = MetadataManager.LoadMetadata(avatarName, renderer.gameObject.name);
            if (metadata != null)
                MetadataManager.SaveMetadata(metadata, avatarName, renderer.gameObject.name);

            if (keepPaintingAfterSave)
            {
                // Re-assign display texture for continued painting
                ScenePaintState.DisplayTexture = TextureUtility.CreateEditableTexture(
                    mat.mainTexture as Texture2D);
                if (ScenePaintState.DisplayTexture != null)
                    mat.mainTexture = ScenePaintState.DisplayTexture;
            }
            // else: mat.mainTexture stays bound to the saved disk asset (savedTex above).
            //       Deactivate's DestroyImmediate(DisplayTexture) will clean up the old
            //       runtime tex without orphaning mat.mainTexture.
        }

        /// <summary>
        /// Compute pixel-space brush radius from world-space brush size.
        /// Uses triangle world-area vs UV-area ratio.
        /// </summary>
        public static float ComputePixelRadius(int hitTriIndex)
        {
            var verts = ScenePaintState.WorldVertices;
            var uvs = ScenePaintState.UVs;
            var tris = ScenePaintState.Triangles;
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;

            if (verts == null || uvs == null || tris == null || hitTriIndex < 0)
                return 10f; // fallback

            int ti = hitTriIndex * 3;
            if (ti + 2 >= tris.Length) return 10f;

            // World area
            Vector3 wv0 = verts[tris[ti]];
            Vector3 wv1 = verts[tris[ti + 1]];
            Vector3 wv2 = verts[tris[ti + 2]];
            float worldArea = Vector3.Cross(wv1 - wv0, wv2 - wv0).magnitude * 0.5f;

            // UV area in pixel space
            Vector2 uv0 = uvs[tris[ti]];
            Vector2 uv1 = uvs[tris[ti + 1]];
            Vector2 uv2 = uvs[tris[ti + 2]];
            float uvAreaPixels = Mathf.Abs(
                (uv1.x - uv0.x) * w * ((uv2.y - uv0.y) * h) -
                (uv2.x - uv0.x) * w * ((uv1.y - uv0.y) * h)
            ) * 0.5f;

            if (worldArea < 1e-10f) return 10f;

            float scale = Mathf.Sqrt(uvAreaPixels / worldArea);
            return ScenePaintState.BrushSize * scale;
        }

        /// <summary>
        /// Apply blend mode between base and brush colors.
        /// </summary>
        public static Color ApplyBlend(Color32 baseColor32, Color brushColor, int blendMode, float strength)
        {
            Color baseC = (Color)baseColor32;
            Color result;

            switch (blendMode)
            {
                case 1: // multiply
                    result = new Color(baseC.r * brushColor.r, baseC.g * brushColor.g, baseC.b * brushColor.b);
                    break;
                case 2: // screen
                    result = new Color(
                        1f - (1f - baseC.r) * (1f - brushColor.r),
                        1f - (1f - baseC.g) * (1f - brushColor.g),
                        1f - (1f - baseC.b) * (1f - brushColor.b));
                    break;
                case 3: // overlay
                    result = new Color(
                        baseC.r < 0.5f ? 2f * baseC.r * brushColor.r : 1f - 2f * (1f - baseC.r) * (1f - brushColor.r),
                        baseC.g < 0.5f ? 2f * baseC.g * brushColor.g : 1f - 2f * (1f - baseC.g) * (1f - brushColor.g),
                        baseC.b < 0.5f ? 2f * baseC.b * brushColor.b : 1f - 2f * (1f - baseC.b) * (1f - brushColor.b));
                    break;
                case 4: // dodge (覆い焼き)
                    result = new Color(
                        Mathf.Min(baseC.r + brushColor.r * 0.5f, 1f),
                        Mathf.Min(baseC.g + brushColor.g * 0.5f, 1f),
                        Mathf.Min(baseC.b + brushColor.b * 0.5f, 1f));
                    break;
                case 5: // burn (焼き込み)
                    result = new Color(
                        Mathf.Max(baseC.r * (1f - 0.5f), 0f),
                        Mathf.Max(baseC.g * (1f - 0.5f), 0f),
                        Mathf.Max(baseC.b * (1f - 0.5f), 0f));
                    break;
                default: // normal
                    result = brushColor;
                    break;
            }

            result = Color.Lerp(baseC, result, strength);
            result.a = baseC.a;
            return result;
        }

        /// <summary>
        /// Build island mask from selected islands' triangle indices.
        /// </summary>
        public static void BuildIslandMask(List<UVIsland> islands, HashSet<int> selectedIndices)
        {
            if (islands == null || selectedIndices == null || selectedIndices.Count == 0)
            {
                ScenePaintState.MaskedTriangles = null;
                return;
            }

            var masked = new HashSet<int>();
            foreach (int idx in selectedIndices)
            {
                if (idx >= 0 && idx < islands.Count)
                {
                    foreach (int triIdx in islands[idx].triangleIndices)
                        masked.Add(triIdx);
                }
            }
            ScenePaintState.MaskedTriangles = masked;
        }

        // --- Private helpers ---

        private static bool RayTriangleIntersect(Vector3 origin, Vector3 dir,
            Vector3 v0, Vector3 v1, Vector3 v2,
            out float t, out float u, out float v)
        {
            t = 0; u = 0; v = 0;
            const float EPSILON = 1e-8f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 h = Vector3.Cross(dir, e2);
            float a = Vector3.Dot(e1, h);

            if (a < EPSILON) return false; // backface cull + parallel

            float f = 1f / a;
            Vector3 s = origin - v0;
            u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, e1);
            v = f * Vector3.Dot(dir, q);
            if (v < 0f || u + v > 1f) return false;

            t = f * Vector3.Dot(e2, q);
            return t > EPSILON;
        }

        private static void ExpandDirtyRect(int minX, int minY, int maxX, int maxY)
        {
            var dr = ScenePaintState.DirtyRect;
            int newX = Mathf.Min(dr.x, minX);
            int newY = Mathf.Min(dr.y, minY);
            int newMaxX = Mathf.Max(dr.x + dr.width, maxX);
            int newMaxY = Mathf.Max(dr.y + dr.height, maxY);
            ScenePaintState.DirtyRect = new RectInt(newX, newY, newMaxX - newX, newMaxY - newY);
        }

        /// <summary>
        /// Check if a pixel coordinate falls within any masked triangle's UV area.
        /// Uses simple point-in-triangle test against the masked triangles.
        /// </summary>
        private static bool IsPixelInMaskedTriangle(int px, int py)
        {
            var uvs = ScenePaintState.UVs;
            var tris = ScenePaintState.Triangles;
            var masked = ScenePaintState.MaskedTriangles;
            int w = ScenePaintState.TexWidth;
            int h = ScenePaintState.TexHeight;

            if (uvs == null || tris == null || masked == null) return false;

            float fx = (px + 0.5f) / w;
            float fy = (py + 0.5f) / h;
            Vector2 p = new Vector2(fx, fy);

            foreach (int triIdx in masked)
            {
                int ti = triIdx * 3;
                if (ti + 2 >= tris.Length) continue;

                Vector2 uv0 = uvs[tris[ti]];
                Vector2 uv1 = uvs[tris[ti + 1]];
                Vector2 uv2 = uvs[tris[ti + 2]];

                if (IsPointInTriangle(p, uv0, uv1, uv2))
                    return true;
            }
            return false;
        }

        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
            if ((s < 0) != (t < 0)) return false;
            float area = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
            if (area < 0) { s = -s; t = -t; area = -area; }
            return s > 0 && t > 0 && (s + t) <= area;
        }
    }
}
