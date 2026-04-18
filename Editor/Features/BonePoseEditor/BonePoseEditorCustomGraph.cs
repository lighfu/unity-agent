using UnityEngine;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class BonePoseEditorCustomGraph
    {
        // ── Constants ──
        private const float GraphHeight = 200f;
        private const float TangentHandleRadius = 6f;
        private const float KeyframeDotRadius = 5f;
        private const float MinZoom = 0.5f;
        private const float MaxZoom = 5f;
        private const float TangentHandleLength = 40f;

        private static readonly Color ColorBackground = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColorGridMajor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        private static readonly Color ColorGridMinor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
        private static readonly Color ColorCurve = new Color(0f, 0.8f, 1f);
        private static readonly Color ColorKeyframe = new Color(1f, 1f, 1f);
        private static readonly Color ColorKeyframeSelected = new Color(1f, 0.92f, 0.016f);
        private static readonly Color ColorTangent = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color ColorTimeline = new Color(1f, 0.25f, 0.25f, 0.7f);
        private static readonly Color ColorAxisLabel = new Color(0.5f, 0.5f, 0.5f);

        // ── Drag state ──
        private static bool _isDraggingKey;
        private static bool _isDraggingInTangent;
        private static bool _isDraggingOutTangent;
        private static bool _isPanning;
        private static Vector2 _panStartMousePos;
        private static Vector2 _panStartOffset;

        // ── Coordinate Conversion ──

        private static Vector2 CurveToScreen(Rect rect, float time, float value,
            float zoom, Vector2 pan)
        {
            float x = rect.x + (time * zoom + pan.x) * rect.width;
            float y = rect.yMax - (value * zoom + pan.y) * rect.height;
            return new Vector2(x, y);
        }

        private static void ScreenToCurve(Rect rect, Vector2 screenPos,
            float zoom, Vector2 pan, out float time, out float value)
        {
            time = ((screenPos.x - rect.x) / rect.width - pan.x) / zoom;
            value = ((rect.yMax - screenPos.y) / rect.height - pan.y) / zoom;
        }

        // ── Main Draw ──

        public static void Draw(AnimationCurve curve, HumanBodyBones bone)
        {
            if (curve == null) return;

            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(GraphHeight));
            if (rect.width <= 0 || rect.height <= 0) return;

            float zoom = BonePoseEditorState.GraphZoom;
            Vector2 pan = BonePoseEditorState.GraphPanOffset;

            DrawBackground(rect);
            DrawGrid(rect, zoom, pan);
            DrawValueAxis(rect, zoom, pan);
            DrawTimeAxis(rect, zoom, pan);
            DrawBezierCurve(rect, curve, zoom, pan);
            DrawKeyframePoints(rect, curve, zoom, pan);

            int selectedIdx = BonePoseEditorState.SelectedCurveKeyIndex;
            if (selectedIdx >= 0 && selectedIdx < curve.length)
                DrawTangentHandles(rect, curve, selectedIdx, zoom, pan);

            DrawCurrentTimeIndicator(rect, zoom, pan);
            HandleInteraction(rect, curve, bone, ref zoom, ref pan);

            BonePoseEditorState.GraphZoom = zoom;
            BonePoseEditorState.GraphPanOffset = pan;
        }

        // ── Drawing Layers ──

        private static void DrawBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, ColorBackground);
        }

        private static void DrawGrid(Rect rect, float zoom, Vector2 pan)
        {
            // Major grid lines at 0.25 intervals for value, time-dependent for time
            float majorValueStep = 0.25f;
            float majorTimeStep = 0.25f;

            // Minor grid
            DrawGridLines(rect, zoom, pan, majorTimeStep * 0.5f, majorValueStep * 0.5f,
                ColorGridMinor);
            // Major grid
            DrawGridLines(rect, zoom, pan, majorTimeStep, majorValueStep, ColorGridMajor);
        }

        private static void DrawGridLines(Rect rect, float zoom, Vector2 pan,
            float timeStep, float valueStep, Color color)
        {
            Handles.color = color;

            // Vertical lines (time)
            float tStart = -pan.x / zoom;
            float tEnd = (1f - pan.x) / zoom;
            float t0 = Mathf.Floor(tStart / timeStep) * timeStep;
            for (float t = t0; t <= tEnd; t += timeStep)
            {
                var p = CurveToScreen(rect, t, 0, zoom, pan);
                if (p.x >= rect.x && p.x <= rect.xMax)
                    Handles.DrawLine(new Vector3(p.x, rect.y), new Vector3(p.x, rect.yMax));
            }

            // Horizontal lines (value)
            float vStart = -pan.y / zoom;
            float vEnd = (1f - pan.y) / zoom;
            float v0 = Mathf.Floor(vStart / valueStep) * valueStep;
            for (float v = v0; v <= vEnd; v += valueStep)
            {
                var p = CurveToScreen(rect, 0, v, zoom, pan);
                if (p.y >= rect.y && p.y <= rect.yMax)
                    Handles.DrawLine(new Vector3(rect.x, p.y), new Vector3(rect.xMax, p.y));
            }
        }

        private static void DrawTimeAxis(Rect rect, float zoom, Vector2 pan)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = ColorAxisLabel },
                fontSize = 9,
            };

            float step = 0.25f;
            float tStart = -pan.x / zoom;
            float tEnd = (1f - pan.x) / zoom;
            float t0 = Mathf.Floor(tStart / step) * step;
            for (float t = t0; t <= tEnd; t += step)
            {
                var p = CurveToScreen(rect, t, 0, zoom, pan);
                if (p.x >= rect.x && p.x <= rect.xMax)
                    GUI.Label(new Rect(p.x - 16, rect.yMax - 14, 32, 14),
                        t.ToString("F2"), style);
            }
        }

        private static void DrawValueAxis(Rect rect, float zoom, Vector2 pan)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ColorAxisLabel },
                fontSize = 9,
            };

            float[] values = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            foreach (float v in values)
            {
                var p = CurveToScreen(rect, 0, v, zoom, pan);
                if (p.y >= rect.y && p.y <= rect.yMax)
                    GUI.Label(new Rect(rect.x + 2, p.y - 7, 30, 14),
                        v.ToString("F2"), style);
            }
        }

        private static void DrawBezierCurve(Rect rect, AnimationCurve curve,
            float zoom, Vector2 pan)
        {
            if (curve.length < 2) return;

            for (int i = 0; i < curve.length - 1; i++)
            {
                var k0 = curve.keys[i];
                var k1 = curve.keys[i + 1];
                float dt = k1.time - k0.time;
                if (dt <= 0f) continue;

                // Hermite → Bezier conversion
                Vector2 p0 = CurveToScreen(rect, k0.time, k0.value, zoom, pan);
                Vector2 p3 = CurveToScreen(rect, k1.time, k1.value, zoom, pan);
                Vector2 p1 = CurveToScreen(rect,
                    k0.time + dt / 3f,
                    k0.value + k0.outTangent * dt / 3f, zoom, pan);
                Vector2 p2 = CurveToScreen(rect,
                    k1.time - dt / 3f,
                    k1.value - k1.inTangent * dt / 3f, zoom, pan);

                Handles.DrawBezier(
                    new Vector3(p0.x, p0.y),
                    new Vector3(p3.x, p3.y),
                    new Vector3(p1.x, p1.y),
                    new Vector3(p2.x, p2.y),
                    ColorCurve, null, 2f);
            }
        }

        private static void DrawKeyframePoints(Rect rect, AnimationCurve curve,
            float zoom, Vector2 pan)
        {
            int selectedIdx = BonePoseEditorState.SelectedCurveKeyIndex;

            for (int i = 0; i < curve.length; i++)
            {
                var key = curve.keys[i];
                Vector2 screenPos = CurveToScreen(rect, key.time, key.value, zoom, pan);

                Color dotColor = i == selectedIdx
                    ? ColorKeyframeSelected : ColorKeyframe;
                float radius = i == selectedIdx
                    ? KeyframeDotRadius * 1.3f : KeyframeDotRadius;

                EditorGUI.DrawRect(new Rect(
                    screenPos.x - radius, screenPos.y - radius,
                    radius * 2f, radius * 2f), dotColor);
            }
        }

        private static void DrawTangentHandles(Rect rect, AnimationCurve curve,
            int selectedIdx, float zoom, Vector2 pan)
        {
            var key = curve.keys[selectedIdx];
            Vector2 keyScreen = CurveToScreen(rect, key.time, key.value, zoom, pan);

            // In tangent (left side)
            if (selectedIdx > 0)
            {
                Vector2 inDir = new Vector2(-1f, key.inTangent).normalized;
                Vector2 inHandlePos = keyScreen + new Vector2(
                    -TangentHandleLength, TangentHandleLength * key.inTangent);

                // Clamp to reasonable range
                inHandlePos = ClampTangentHandle(keyScreen, inHandlePos, TangentHandleLength);

                Handles.color = ColorTangent;
                Handles.DrawLine(
                    new Vector3(keyScreen.x, keyScreen.y),
                    new Vector3(inHandlePos.x, inHandlePos.y));

                EditorGUI.DrawRect(new Rect(
                    inHandlePos.x - TangentHandleRadius,
                    inHandlePos.y - TangentHandleRadius,
                    TangentHandleRadius * 2f,
                    TangentHandleRadius * 2f),
                    ColorTangent);
            }

            // Out tangent (right side)
            if (selectedIdx < curve.length - 1)
            {
                Vector2 outHandlePos = keyScreen + new Vector2(
                    TangentHandleLength, -TangentHandleLength * key.outTangent);

                outHandlePos = ClampTangentHandle(keyScreen, outHandlePos, TangentHandleLength);

                Handles.color = ColorTangent;
                Handles.DrawLine(
                    new Vector3(keyScreen.x, keyScreen.y),
                    new Vector3(outHandlePos.x, outHandlePos.y));

                EditorGUI.DrawRect(new Rect(
                    outHandlePos.x - TangentHandleRadius,
                    outHandlePos.y - TangentHandleRadius,
                    TangentHandleRadius * 2f,
                    TangentHandleRadius * 2f),
                    ColorTangent);
            }
        }

        private static Vector2 ClampTangentHandle(Vector2 origin, Vector2 handle,
            float maxLen)
        {
            var delta = handle - origin;
            if (delta.magnitude > maxLen)
                delta = delta.normalized * maxLen;
            return origin + delta;
        }

        private static void DrawCurrentTimeIndicator(Rect rect, float zoom, Vector2 pan)
        {
            float time = BonePoseEditorState.CurrentTime /
                Mathf.Max(0.001f, BonePoseEditorState.AnimationLength);
            var p = CurveToScreen(rect, time, 0, zoom, pan);

            if (p.x >= rect.x && p.x <= rect.xMax)
            {
                EditorGUI.DrawRect(new Rect(p.x - 1, rect.y, 2, rect.height),
                    ColorTimeline);
            }
        }

        // ── Interaction ──

        private static void HandleInteraction(Rect rect, AnimationCurve curve,
            HumanBodyBones bone, ref float zoom, ref Vector2 pan)
        {
            Event e = Event.current;
            if (e == null) return;
            if (!rect.Contains(e.mousePosition)
                && e.type != EventType.MouseDrag && e.type != EventType.MouseUp)
                return;

            int selectedIdx = BonePoseEditorState.SelectedCurveKeyIndex;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        float delta = -e.delta.y * 0.05f;
                        zoom = Mathf.Clamp(zoom + delta, MinZoom, MaxZoom);
                        e.Use();
                    }
                    break;

                case EventType.MouseDown:
                    if (!rect.Contains(e.mousePosition)) break;

                    if (e.button == 2) // Middle click — pan
                    {
                        _isPanning = true;
                        _panStartMousePos = e.mousePosition;
                        _panStartOffset = pan;
                        e.Use();
                        break;
                    }

                    if (e.button != 0) break;

                    // Check tangent handle hit first (if key is selected)
                    if (selectedIdx >= 0 && selectedIdx < curve.length)
                    {
                        var key = curve.keys[selectedIdx];
                        Vector2 keyScreen = CurveToScreen(rect, key.time, key.value, zoom, pan);

                        // In tangent
                        if (selectedIdx > 0)
                        {
                            Vector2 inHandle = keyScreen + new Vector2(
                                -TangentHandleLength, TangentHandleLength * key.inTangent);
                            inHandle = ClampTangentHandle(keyScreen, inHandle, TangentHandleLength);
                            if (Vector2.Distance(e.mousePosition, inHandle) <= TangentHandleRadius + 3f)
                            {
                                _isDraggingInTangent = true;
                                e.Use();
                                break;
                            }
                        }
                        // Out tangent
                        if (selectedIdx < curve.length - 1)
                        {
                            Vector2 outHandle = keyScreen + new Vector2(
                                TangentHandleLength, -TangentHandleLength * key.outTangent);
                            outHandle = ClampTangentHandle(keyScreen, outHandle, TangentHandleLength);
                            if (Vector2.Distance(e.mousePosition, outHandle) <= TangentHandleRadius + 3f)
                            {
                                _isDraggingOutTangent = true;
                                e.Use();
                                break;
                            }
                        }
                    }

                    // Check keyframe hit
                    int hitIdx = -1;
                    float bestDist = KeyframeDotRadius + 3f;
                    for (int i = 0; i < curve.length; i++)
                    {
                        var key = curve.keys[i];
                        Vector2 screenPos = CurveToScreen(rect, key.time, key.value, zoom, pan);
                        float dist = Vector2.Distance(e.mousePosition, screenPos);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            hitIdx = i;
                        }
                    }

                    if (hitIdx >= 0)
                    {
                        BonePoseEditorState.SelectedCurveKeyIndex = hitIdx;
                        _isDraggingKey = true;
                    }
                    else
                    {
                        BonePoseEditorState.SelectedCurveKeyIndex = -1;
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (_isPanning)
                    {
                        Vector2 deltaPx = e.mousePosition - _panStartMousePos;
                        pan = _panStartOffset + new Vector2(
                            deltaPx.x / rect.width,
                            -deltaPx.y / rect.height);
                        e.Use();
                    }
                    else if (_isDraggingKey && selectedIdx >= 0 && selectedIdx < curve.length)
                    {
                        ScreenToCurve(rect, e.mousePosition, zoom, pan,
                            out float newTime, out float newValue);
                        newTime = Mathf.Clamp(newTime, 0f, 1f);
                        newValue = Mathf.Clamp(newValue, 0f, 1f);

                        var keys = curve.keys;
                        var key = keys[selectedIdx];
                        key.time = newTime;
                        key.value = newValue;

                        // Auto smooth tangent
                        if (BonePoseEditorState.CurrentTangentMode == TangentMode.Auto)
                            AutoSmoothKey(ref key, curve, selectedIdx);

                        keys[selectedIdx] = key;
                        curve.keys = keys;
                        BonePoseEditorState.SetBoneCurve(bone, curve);
                        e.Use();
                    }
                    else if (_isDraggingInTangent && selectedIdx >= 0 && selectedIdx < curve.length)
                    {
                        var key = curve.keys[selectedIdx];
                        Vector2 keyScreen = CurveToScreen(rect, key.time, key.value, zoom, pan);
                        Vector2 delta = e.mousePosition - keyScreen;
                        if (Mathf.Abs(delta.x) > 0.001f)
                        {
                            float tangent = -delta.y / delta.x;
                            key.inTangent = -tangent; // Invert because screen Y is flipped
                            if (BonePoseEditorState.CurrentTangentMode == TangentMode.Unified)
                                key.outTangent = key.inTangent;
                        }
                        var keys = curve.keys;
                        keys[selectedIdx] = key;
                        curve.keys = keys;
                        BonePoseEditorState.SetBoneCurve(bone, curve);
                        e.Use();
                    }
                    else if (_isDraggingOutTangent && selectedIdx >= 0 && selectedIdx < curve.length)
                    {
                        var key = curve.keys[selectedIdx];
                        Vector2 keyScreen = CurveToScreen(rect, key.time, key.value, zoom, pan);
                        Vector2 delta = e.mousePosition - keyScreen;
                        if (Mathf.Abs(delta.x) > 0.001f)
                        {
                            float tangent = -delta.y / delta.x;
                            key.outTangent = tangent;
                            if (BonePoseEditorState.CurrentTangentMode == TangentMode.Unified)
                                key.inTangent = key.outTangent;
                        }
                        var keys = curve.keys;
                        keys[selectedIdx] = key;
                        curve.keys = keys;
                        BonePoseEditorState.SetBoneCurve(bone, curve);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    _isDraggingKey = false;
                    _isDraggingInTangent = false;
                    _isDraggingOutTangent = false;
                    _isPanning = false;
                    if (rect.Contains(e.mousePosition))
                        e.Use();
                    break;
            }
        }

        private static void AutoSmoothKey(ref Keyframe key, AnimationCurve curve, int index)
        {
            if (curve.length < 2) return;

            if (index > 0 && index < curve.length - 1)
            {
                var prev = curve.keys[index - 1];
                var next = curve.keys[index + 1];
                float dt = next.time - prev.time;
                if (dt > 0.001f)
                {
                    float slope = (next.value - prev.value) / dt;
                    key.inTangent = slope;
                    key.outTangent = slope;
                }
            }
            else if (index == 0 && curve.length > 1)
            {
                var next = curve.keys[1];
                float dt = next.time - key.time;
                if (dt > 0.001f)
                    key.outTangent = (next.value - key.value) / dt;
                key.inTangent = key.outTangent;
            }
            else if (index == curve.length - 1 && curve.length > 1)
            {
                var prev = curve.keys[index - 1];
                float dt = key.time - prev.time;
                if (dt > 0.001f)
                    key.inTangent = (key.value - prev.value) / dt;
                key.outTangent = key.inTangent;
            }
        }
    }
}
