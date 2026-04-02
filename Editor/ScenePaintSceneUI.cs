using UnityEngine;
using UnityEditor;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [InitializeOnLoad]
    public static class ScenePaintSceneUI
    {
        static ScenePaintSceneUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!ScenePaintState.IsActive) return;
            if (ScenePaintState.ActiveRenderer == null)
            {
                ScenePaintState.Deactivate();
                return;
            }

            HandleShortcuts(Event.current);
            HandlePaintInput(sceneView, Event.current);
            DrawBrushCursor(sceneView);

            Handles.BeginGUI();
            DrawOverlayPanel(sceneView);
            Handles.EndGUI();
        }

        // --- Input Handling ---

        private static void HandlePaintInput(SceneView sceneView, Event e)
        {
            if (e == null) return;

            // Alt is reserved for Unity scene navigation (Orbit/Pan/Zoom) — never intercept
            if (e.alt) return;

            // Only suppress default left-click (object selection), let middle/right/scroll through
            if (e.button == 0 || e.type == EventType.Layout || e.type == EventType.Repaint)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            // Update cursor position on mouse move
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (ScenePaintEngine.RaycastMesh(ray, out Vector3 hitWorld, out Vector3 hitNormal,
                                                  out Vector2 hitUV, out int hitTriIndex))
                {
                    ScenePaintState.HasCursorHit = true;
                    ScenePaintState.CursorWorldPos = hitWorld;
                    ScenePaintState.CursorNormal = hitNormal;
                }
                else
                {
                    ScenePaintState.HasCursorHit = false;
                }
            }

            // Ctrl+Click: eyedropper / clone source (Alt is reserved for scene navigation)
            if (e.type == EventType.MouseDown && e.button == 0 && e.control)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (ScenePaintEngine.RaycastMesh(ray, out Vector3 hitWorld, out Vector3 hitNormal,
                                                  out Vector2 hitUV, out int hitTriIndex))
                {
                    if (ScenePaintState.ActiveTool == BrushTool.Clone)
                    {
                        // Set clone source
                        ScenePaintState.CloneSourceUV = hitUV;
                        ScenePaintState.CloneSourceSet = true;
                    }
                    else
                    {
                        // Eyedropper: pick color from display texture
                        int px = Mathf.Clamp((int)(hitUV.x * ScenePaintState.TexWidth), 0, ScenePaintState.TexWidth - 1);
                        int py = Mathf.Clamp((int)(hitUV.y * ScenePaintState.TexHeight), 0, ScenePaintState.TexHeight - 1);
                        Color picked = ScenePaintState.DisplayTexture.GetPixel(px, py);
                        ScenePaintState.RaiseColorPicked(picked);
                    }

                    e.Use();
                    sceneView.Repaint();
                }
                return;
            }

            // Mouse down: begin stroke (left button only, no modifiers)
            if (e.type == EventType.MouseDown && e.button == 0 && !e.control)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (ScenePaintEngine.RaycastMesh(ray, out Vector3 hitWorld, out Vector3 hitNormal,
                                                  out Vector2 hitUV, out int hitTriIndex))
                {
                    // Clone mode: compute offset on first click
                    if (ScenePaintState.ActiveTool == BrushTool.Clone && ScenePaintState.CloneSourceSet)
                    {
                        int w = ScenePaintState.TexWidth;
                        int h = ScenePaintState.TexHeight;
                        int destX = Mathf.RoundToInt(hitUV.x * w);
                        int destY = Mathf.RoundToInt(hitUV.y * h);
                        int srcX = Mathf.RoundToInt(ScenePaintState.CloneSourceUV.x * w);
                        int srcY = Mathf.RoundToInt(ScenePaintState.CloneSourceUV.y * h);
                        ScenePaintState.CloneOffset = new Vector2Int(destX - srcX, destY - srcY);
                    }

                    ScenePaintState.BeginStroke();

                    // Smudge: initialize carry color from first click
                    if (ScenePaintState.ActiveTool == BrushTool.Smudge)
                    {
                        int w = ScenePaintState.TexWidth;
                        int px = Mathf.Clamp(Mathf.RoundToInt(hitUV.x * w), 0, w - 1);
                        int py = Mathf.Clamp(Mathf.RoundToInt(hitUV.y * ScenePaintState.TexHeight), 0, ScenePaintState.TexHeight - 1);
                        ScenePaintState.BeginSmudgeAtPixel(py * w + px);
                    }

                    ScenePaintEngine.DispatchStampPublic(hitUV, hitTriIndex);

                    // Symmetry
                    if (ScenePaintState.SymmetryEnabled)
                    {
                        if (ScenePaintEngine.ComputeMirrorUV(hitWorld, sceneView, out Vector2 mirrorUV, out int mirrorTriIndex))
                            ScenePaintEngine.DispatchStampPublic(mirrorUV, mirrorTriIndex);
                    }

                    ScenePaintEngine.RecomposeDirtyRegion();

                    ScenePaintState.LastHitUV = hitUV;
                    ScenePaintState.LastHitWorldPos = hitWorld;
                    ScenePaintState.LastHitNormal = hitNormal;

                    e.Use();
                    sceneView.Repaint();
                }
            }

            // Mouse drag: continue stroke
            if (e.type == EventType.MouseDrag && e.button == 0 && ScenePaintState.IsStroking)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (ScenePaintEngine.RaycastMesh(ray, out Vector3 hitWorld, out Vector3 hitNormal,
                                                  out Vector2 hitUV, out int hitTriIndex))
                {
                    ScenePaintEngine.InterpolateAndStamp(ScenePaintState.LastHitUV, hitUV, hitTriIndex);

                    // Symmetry
                    if (ScenePaintState.SymmetryEnabled)
                    {
                        if (ScenePaintEngine.ComputeMirrorUV(hitWorld, sceneView, out Vector2 mirrorUV, out int mirrorTriIndex))
                        {
                            if (ScenePaintEngine.ComputeMirrorUV(ScenePaintState.LastHitWorldPos, sceneView,
                                out Vector2 lastMirrorUV, out int _))
                            {
                                ScenePaintEngine.InterpolateAndStamp(lastMirrorUV, mirrorUV, mirrorTriIndex);
                            }
                            else
                            {
                                ScenePaintEngine.DispatchStampPublic(mirrorUV, mirrorTriIndex);
                            }
                        }
                    }

                    ScenePaintEngine.RecomposeDirtyRegion();

                    ScenePaintState.LastHitUV = hitUV;
                    ScenePaintState.LastHitWorldPos = hitWorld;
                    ScenePaintState.LastHitNormal = hitNormal;

                    e.Use();
                    sceneView.Repaint();
                }
            }

            // Mouse up: end stroke and commit
            if (e.type == EventType.MouseUp && e.button == 0 && ScenePaintState.IsStroking)
            {
                ScenePaintState.EndStroke();
                ScenePaintEngine.CommitStroke();
                e.Use();
                sceneView.Repaint();
            }
        }

        // --- Brush Cursor Drawing ---

        private static void DrawBrushCursor(SceneView sceneView)
        {
            if (!ScenePaintState.HasCursorHit) return;

            Vector3 pos = ScenePaintState.CursorWorldPos;
            Vector3 normal = ScenePaintState.CursorNormal;
            float size = ScenePaintState.BrushSize;
            float hardness = ScenePaintState.BrushHardness;

            Color cursorColor = GetToolCursorColor();

            Handles.color = cursorColor;
            Handles.DrawWireDisc(pos, normal, size);

            // Inner circle (hardness threshold)
            if (hardness < 0.99f)
            {
                Handles.color = new Color(cursorColor.r, cursorColor.g, cursorColor.b, 0.4f);
                Handles.DrawWireDisc(pos, normal, size * hardness);
            }

            // Center dot
            Handles.color = new Color(1f, 1f, 1f, 0.6f);
            Handles.DrawWireDisc(pos, normal, size * 0.02f);

            // Clone source marker
            if (ScenePaintState.ActiveTool == BrushTool.Clone && ScenePaintState.CloneSourceSet)
            {
                DrawCloneSourceMarker();
            }

            // Symmetry mirror cursor
            if (ScenePaintState.SymmetryEnabled && sceneView != null)
            {
                if (ScenePaintEngine.ComputeMirrorUV(pos, sceneView, out Vector2 mirrorUV, out int mirrorTriIndex))
                {
                    // Raycast to get mirror world position for cursor
                    var avatarRoot = ScenePaintState.AvatarRoot;
                    if (avatarRoot != null)
                    {
                        Vector3 localPos = avatarRoot.transform.InverseTransformPoint(pos);
                        localPos.x = -localPos.x;
                        Vector3 mirrorWorld = avatarRoot.transform.TransformPoint(localPos);

                        Vector3 localNormal = avatarRoot.transform.InverseTransformDirection(normal);
                        localNormal.x = -localNormal.x;
                        Vector3 mirrorNormal = avatarRoot.transform.TransformDirection(localNormal);

                        Handles.color = new Color(cursorColor.r, cursorColor.g, cursorColor.b, 0.4f);
                        Handles.DrawWireDisc(mirrorWorld, mirrorNormal, size);
                    }
                }
            }
        }

        private static Color GetToolCursorColor()
        {
            switch (ScenePaintState.ActiveTool)
            {
                case BrushTool.Eraser:
                    return new Color(1f, 0.3f, 0.3f, 0.8f);
                case BrushTool.Blur:
                    return new Color(0.5f, 0.8f, 1f, 0.8f);
                case BrushTool.Smudge:
                    return new Color(0.8f, 0.6f, 1f, 0.8f);
                case BrushTool.Clone:
                    return new Color(0.3f, 1f, 0.5f, 0.8f);
                case BrushTool.Dodge:
                    return new Color(1f, 1f, 0.5f, 0.8f);
                case BrushTool.Burn:
                    return new Color(0.6f, 0.3f, 0.1f, 0.8f);
                case BrushTool.Tint:
                    return new Color(0.9f, 0.5f, 0.9f, 0.8f);
                case BrushTool.Sharpen:
                    return new Color(0.2f, 0.9f, 0.9f, 0.8f);
                case BrushTool.Noise:
                    return new Color(0.7f, 0.7f, 0.4f, 0.8f);
                case BrushTool.Saturate:
                    return new Color(1f, 0.4f, 0.7f, 0.8f);
                case BrushTool.Desaturate:
                    return new Color(0.6f, 0.6f, 0.6f, 0.8f);
                default: // Paint
                    return new Color(ScenePaintState.BrushColor.r, ScenePaintState.BrushColor.g,
                                     ScenePaintState.BrushColor.b, 0.8f);
            }
        }

        private static void DrawCloneSourceMarker()
        {
            // Draw crosshair at clone source UV position on the mesh
            // We need to find the world position of the clone source UV
            var uvs = ScenePaintState.UVs;
            var tris = ScenePaintState.Triangles;
            var verts = ScenePaintState.WorldVertices;
            if (uvs == null || tris == null || verts == null) return;

            Vector2 srcUV = ScenePaintState.CloneSourceUV;

            // Find closest triangle to source UV and interpolate world position
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 uv0 = uvs[tris[i]], uv1 = uvs[tris[i + 1]], uv2 = uvs[tris[i + 2]];
                if (IsPointInTriangle(srcUV, uv0, uv1, uv2))
                {
                    // Barycentric interpolation
                    Vector3 bary = ComputeBarycentric(srcUV, uv0, uv1, uv2);
                    Vector3 worldPos = verts[tris[i]] * bary.x + verts[tris[i + 1]] * bary.y + verts[tris[i + 2]] * bary.z;

                    Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                    float crossSize = ScenePaintState.BrushSize * 0.5f;
                    Vector3 camRight = Camera.current.transform.right * crossSize;
                    Vector3 camUp = Camera.current.transform.up * crossSize;
                    Handles.DrawLine(worldPos - camRight, worldPos + camRight);
                    Handles.DrawLine(worldPos - camUp, worldPos + camUp);
                    Handles.DrawWireDisc(worldPos, Camera.current.transform.forward, crossSize * 0.5f);
                    return;
                }
            }
        }

        // --- Overlay Panel ---

        private static void DrawOverlayPanel(SceneView sceneView)
        {
            float panelWidth = 280f;
            float panelHeight = 340f;
            float panelX = 10f;
            float panelY = sceneView.position.height - panelHeight - 50f;

            var rect = new Rect(panelX, panelY, panelWidth, panelHeight);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            float innerX = rect.x + 8;
            float innerWidth = rect.width - 16;
            float y = rect.y + 8;
            float lineHeight = 18f;
            float spacing = 2f;

            // Title
            GUI.Label(new Rect(innerX, y, innerWidth, lineHeight), M("シーンペイント"), EditorStyles.boldLabel);
            y += lineHeight + spacing + 2;

            // Tool selection
            GUI.Label(new Rect(innerX, y, 32, lineHeight), M("ツール:"));
            int toolIdx = (int)ScenePaintState.ActiveTool;
            int newToolIdx = GUI.SelectionGrid(
                new Rect(innerX + 34, y, innerWidth - 34, lineHeight * 3),
                toolIdx, ScenePaintState.ToolLabels, 4);
            if (newToolIdx != toolIdx)
                ScenePaintState.ActiveTool = (BrushTool)newToolIdx;
            y += lineHeight * 3 + spacing + 2;

            // Size slider
            GUI.Label(new Rect(innerX, y, 60, lineHeight), M("サイズ:"));
            ScenePaintState.BrushSize = GUI.HorizontalSlider(
                new Rect(innerX + 62, y + 2, innerWidth - 110, lineHeight),
                ScenePaintState.BrushSize, 0.001f, 0.2f);
            GUI.Label(new Rect(innerX + innerWidth - 44, y, 44, lineHeight),
                ScenePaintState.BrushSize.ToString("F3"), EditorStyles.miniLabel);
            y += lineHeight + spacing;

            // Opacity slider
            GUI.Label(new Rect(innerX, y, 60, lineHeight), M("不透明度:"));
            ScenePaintState.BrushOpacity = GUI.HorizontalSlider(
                new Rect(innerX + 62, y + 2, innerWidth - 110, lineHeight),
                ScenePaintState.BrushOpacity, 0f, 1f);
            GUI.Label(new Rect(innerX + innerWidth - 44, y, 44, lineHeight),
                (ScenePaintState.BrushOpacity * 100f).ToString("F0") + "%", EditorStyles.miniLabel);
            y += lineHeight + spacing;

            // Hardness slider
            GUI.Label(new Rect(innerX, y, 60, lineHeight), M("硬さ:"));
            ScenePaintState.BrushHardness = GUI.HorizontalSlider(
                new Rect(innerX + 62, y + 2, innerWidth - 110, lineHeight),
                ScenePaintState.BrushHardness, 0f, 1f);
            GUI.Label(new Rect(innerX + innerWidth - 44, y, 44, lineHeight),
                (ScenePaintState.BrushHardness * 100f).ToString("F0") + "%", EditorStyles.miniLabel);
            y += lineHeight + spacing + 2;

            // Color + Blend mode row (Paint/Tint/Dodge/Burn use brush color)
            var activeTool = ScenePaintState.ActiveTool;
            bool showColor = activeTool == BrushTool.Paint || activeTool == BrushTool.Tint
                || activeTool == BrushTool.Dodge || activeTool == BrushTool.Burn;
            if (showColor)
            {
                GUI.Label(new Rect(innerX, y, 36, lineHeight), M("カラー:"));
                ScenePaintState.BrushColor = EditorGUI.ColorField(
                    new Rect(innerX + 38, y, 60, lineHeight), ScenePaintState.BrushColor);

                if (activeTool == BrushTool.Paint)
                {
                    GUI.Label(new Rect(innerX + 108, y, 40, lineHeight), M("ブレンド:"));
                    ScenePaintState.BlendModeIndex = EditorGUI.Popup(
                        new Rect(innerX + 150, y, innerWidth - 150, lineHeight),
                        ScenePaintState.BlendModeIndex, ScenePaintState.BlendModeLabels);
                }
                y += lineHeight + spacing + 2;
            }

            // Symmetry + Island Mask toggles
            ScenePaintState.SymmetryEnabled = GUI.Toggle(
                new Rect(innerX, y, 100, lineHeight), ScenePaintState.SymmetryEnabled, M("対称 (M)"));
            ScenePaintState.IslandMaskEnabled = GUI.Toggle(
                new Rect(innerX + 110, y, innerWidth - 110, lineHeight),
                ScenePaintState.IslandMaskEnabled, M("アイランドマスク"));
            y += lineHeight + spacing + 4;

            // Shortcuts hint
            GUI.Label(new Rect(innerX, y, innerWidth, lineHeight),
                M("[ ] サイズ  Ctrl+クリック: スポイト  Alt+ドラッグ: ナビゲーション"), EditorStyles.miniLabel);
            y += lineHeight + spacing + 4;

            // Stop button
            if (GUI.Button(new Rect(innerX, y, innerWidth, 24), M("ペイント終了")))
            {
                ScenePaintState.Deactivate();
            }
        }

        // --- Keyboard Shortcuts ---

        private static void HandleShortcuts(Event e)
        {
            if (e == null || e.type != EventType.KeyDown) return;

            // Ctrl+Z / Ctrl+Shift+Z: Undo/Redo (must be checked before the modifier guard)
            if (e.control && e.keyCode == KeyCode.Z)
            {
                if (ScenePaintState.IsStroking) return; // don't undo mid-stroke

                if (e.shift)
                {
                    if (ScenePaintState.CanRedo)
                    {
                        ScenePaintState.PerformRedo();
                        e.Use();
                    }
                }
                else
                {
                    if (ScenePaintState.CanUndo)
                    {
                        ScenePaintState.PerformUndo();
                        e.Use();
                    }
                }
                return;
            }

            // Ctrl+Y: Redo (alternative)
            if (e.control && e.keyCode == KeyCode.Y)
            {
                if (!ScenePaintState.IsStroking && ScenePaintState.CanRedo)
                {
                    ScenePaintState.PerformRedo();
                    e.Use();
                }
                return;
            }

            if (e.control || e.shift || e.alt) return;

            switch (e.keyCode)
            {
                case KeyCode.LeftBracket:
                    ScenePaintState.BrushSize = Mathf.Max(0.001f, ScenePaintState.BrushSize - 0.005f);
                    e.Use();
                    break;
                case KeyCode.RightBracket:
                    ScenePaintState.BrushSize = Mathf.Min(0.2f, ScenePaintState.BrushSize + 0.005f);
                    e.Use();
                    break;
                case KeyCode.B:
                    ScenePaintState.ActiveTool = BrushTool.Paint;
                    e.Use();
                    break;
                case KeyCode.E:
                    ScenePaintState.ActiveTool = BrushTool.Eraser;
                    e.Use();
                    break;
                case KeyCode.R:
                    ScenePaintState.ActiveTool = BrushTool.Blur;
                    e.Use();
                    break;
                case KeyCode.S:
                    ScenePaintState.ActiveTool = BrushTool.Smudge;
                    e.Use();
                    break;
                case KeyCode.C:
                    ScenePaintState.ActiveTool = BrushTool.Clone;
                    e.Use();
                    break;
                case KeyCode.D:
                    ScenePaintState.ActiveTool = BrushTool.Dodge;
                    e.Use();
                    break;
                case KeyCode.X:
                    ScenePaintState.ActiveTool = BrushTool.Burn;
                    e.Use();
                    break;
                case KeyCode.T:
                    ScenePaintState.ActiveTool = BrushTool.Tint;
                    e.Use();
                    break;
                case KeyCode.H:
                    ScenePaintState.ActiveTool = BrushTool.Sharpen;
                    e.Use();
                    break;
                case KeyCode.N:
                    ScenePaintState.ActiveTool = BrushTool.Noise;
                    e.Use();
                    break;
                case KeyCode.M:
                    ScenePaintState.SymmetryEnabled = !ScenePaintState.SymmetryEnabled;
                    e.Use();
                    break;
            }
        }

        // --- Utility ---

        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
            if ((s < 0) != (t < 0)) return false;
            float area = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
            if (area < 0) { s = -s; t = -t; area = -area; }
            return s > 0 && t > 0 && (s + t) <= area;
        }

        private static Vector3 ComputeBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-10f) return new Vector3(1f, 0f, 0f);
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }
    }
}
