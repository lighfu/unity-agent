// Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    public class ExpressionSessionTestWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _log = "";

        [MenuItem("Window/AjisaiFlow/Expression Session Test")]
        public static void Open() => GetWindow<ExpressionSessionTestWindow>("ExprSession Test");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Phase 0: Spikes", EditorStyles.boldLabel);
            if (GUILayout.Button("Spike 0.2: Resolve IExpressionEditor"))
            {
                SpikeResolveIExpressionEditor();
            }
            if (GUILayout.Button("Spike 0.3: Get ExpressionEditorModelFacade"))
            {
                SpikeGetFacade();
            }
            if (GUILayout.Button("Spike 0.4: SetBlendShape live preview"))
            {
                SpikeSetBlendShape();
            }

            EditorGUILayout.LabelField("Phase 1: FaceEmoGate", EditorStyles.boldLabel);
            if (GUILayout.Button("Probe: RequireExpressionEditingReady()"))
            {
                var r = FaceEmoGate.RequireExpressionEditingReady();
                Log($"Ok={r.Ok}, Msg={(r.Ok ? "(no error)" : r.ErrorMessage)}");
            }

            EditorGUILayout.LabelField("Phase 2: Bridge", EditorStyles.boldLabel);
            if (GUILayout.Button("Test: Bridge.TryOpen"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    var bridge = new ExpressionEditorBridge();
                    var clip = new AnimationClip { name = "BridgeProbe" };
                    bool ok = bridge.TryOpen(gate.Launcher, clip);
                    Log($"TryOpen → ok={ok}, IsHealthy={bridge.IsHealthy}, err={bridge.LastReflectionError}");
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Bridge.TryOpenPreviewWindow"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    var bridge = new ExpressionEditorBridge();
                    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "PreviewProbe" });
                    bool ok = bridge.TryOpenPreviewWindow();
                    Log($"TryOpenPreviewWindow → {ok}, err={bridge.LastReflectionError}");
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Bridge.TrySetBlendShape (first face shape → 100)"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    var bridge = new ExpressionEditorBridge();
                    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "SetProbe" });
                    // For probe: try writing into "Body" path with a known shape name from your avatar
                    bool ok = bridge.TrySetBlendShape("Body", "Smile", 100f);
                    Log($"TrySetBlendShape → ok={ok}, err={bridge.LastReflectionError}");
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Bridge.TryGetAnimatedBlendShapes"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    var bridge = new ExpressionEditorBridge();
                    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "GetProbe" });
                    bridge.TrySetBlendShape("Body", "Smile", 80f);
                    bool ok = bridge.TryGetAnimatedBlendShapes(out var vals);
                    Log($"TryGetAnimatedBlendShapes → ok={ok}, count={vals?.Count ?? 0}");
                    if (vals != null) foreach (var kv in vals) Log($"  {kv.Key.path}/{kv.Key.name}={kv.Value:F1}");
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }

            EditorGUILayout.LabelField("Phase 3: Session", EditorStyles.boldLabel);
            if (GUILayout.Button("Test: OpenForNewExpression('Smile')"))
            {
#if FACE_EMO
                try
                {
                    var s = FaceEmoExpressionSession.OpenForNewExpression("Smile", "Assets/UnityAgent/Expressions/smile.anim");
                    Log($"Session opened: Mode={s.Mode}, Clip={s.Clip.name}, Launcher={s.Launcher.gameObject.name}");
                }
                catch (System.Exception ex) { Log("Error: " + ex.Message); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: OpenForMode('Neutral')"))
            {
#if FACE_EMO
                try
                {
                    var s = FaceEmoExpressionSession.OpenForMode("Neutral");
                    Log($"Opened existing: Mode={s.Mode}, ModeId={s.ModeId}, Clip={s.Clip?.name}");
                }
                catch (System.Exception ex) { Log("Error: " + ex.Message); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Force Degraded SetBlendShape"))
            {
#if FACE_EMO
                try
                {
                    var s = FaceEmoExpressionSession.OpenForNewExpression("DegradedTest", "Assets/_temp_degraded.anim");
                    // Naturally observe behavior: Live if Bridge healthy, else Degraded writes curve.
                    s.SetBlendShape("Body", "Smile", 50f);
                    Log($"SetBlendShape OK in Mode={s.Mode}");
                }
                catch (System.Exception ex) { Log("Error: " + ex.Message); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Session.Commit"))
            {
#if FACE_EMO
                try
                {
                    var s = FaceEmoExpressionSession.Active ?? FaceEmoExpressionSession.OpenForNewExpression("CommitTest", "Assets/UnityAgent/Expressions/CommitTest.anim");
                    s.SetBlendShape("Body", "Smile", 80f);
                    s.Commit();
                    Log($"Committed: ModeId={s.ModeId}, ClipPath={UnityEditor.AssetDatabase.GetAssetPath(s.Clip)}");
                }
                catch (System.Exception ex) { Log("Error: " + ex.Message); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }

            EditorGUILayout.LabelField("Phase B: Thumbnail (Plan B Spike)", EditorStyles.boldLabel);
            if (GUILayout.Button("Spike B.0: Instantiate ThumbnailDrawers + render first Mode"))
            {
                SpikeThumbnailRender();
            }
            if (GUILayout.Button("Test: Renderer.RenderModeThumbnail('Neutral')"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    using var r = new FaceEmoThumbnailRenderer();
                    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); }
                    else
                    {
                        var path = r.RenderModeThumbnail("Neutral");
                        Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
                    }
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Renderer.RenderExMenuThumbnail('Neutral')"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (!gate.Ok) { Log(gate.ErrorMessage); }
                else
                {
                    using var r = new FaceEmoThumbnailRenderer();
                    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); }
                    else
                    {
                        var path = r.RenderExMenuThumbnail("Neutral");
                        Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
                    }
                }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Renderer.RenderGestureTable('Neutral')"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (gate.Ok)
                {
                    using var r = new FaceEmoThumbnailRenderer();
                    if (r.TryInitialize(gate.Launcher))
                    {
                        var path = r.RenderGestureTable("Neutral");
                        Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
                    }
                    else { Log($"FAIL init: {r.LastReflectionError}"); }
                }
                else { Log(gate.ErrorMessage); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }
            if (GUILayout.Button("Test: Renderer.RefreshMainView()"))
            {
#if FACE_EMO
                var gate = FaceEmoGate.RequireExpressionEditingReady();
                if (gate.Ok)
                {
                    using var r = new FaceEmoThumbnailRenderer();
                    if (r.TryInitialize(gate.Launcher))
                    {
                        bool ok = r.RefreshMainView("Neutral");
                        Log(ok ? "OK" : $"FAIL: {r.LastReflectionError}");
                    }
                    else { Log($"FAIL init: {r.LastReflectionError}"); }
                }
                else { Log(gate.ErrorMessage); }
#else
                Log("SKIP: FACE_EMO not defined.");
#endif
            }

            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear")) _log = "";
        }

        private void SpikeThumbnailRender()
        {
            Log("--- Spike B.0 ---");
#if FACE_EMO
            var launcher = FaceEmoAPI.FindLauncher();
            if (launcher == null) { Log("FAIL: No launcher."); return; }
            if (launcher.AV3Setting == null || launcher.ThumbnailSetting == null)
            { Log("FAIL: AV3Setting or ThumbnailSetting missing on launcher."); return; }

            const string detailAsm = "jp.suzuryg.face-emo.detail.Editor";
            object drawer = null;
            try
            {
                var mainType = System.Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.MainThumbnailDrawer, {detailAsm}");
                if (mainType == null) { Log("FAIL: MainThumbnailDrawer type not found."); return; }

                drawer = System.Activator.CreateInstance(mainType,
                    new object[] { launcher.AV3Setting, launcher.ThumbnailSetting });
                Log($"OK: Drawer instantiated → {drawer.GetType().FullName}");

                // Get first Mode from menu
                var menu = FaceEmoAPI.LoadMenu(launcher);
                if (menu == null) { Log("FAIL: No menu repository on launcher."); return; }
                var modes = FaceEmoAPI.GetAllExpressions(menu);
                if (modes.Count == 0) { Log("FAIL: No modes in menu."); return; }
                var firstMode = modes[0].mode;
                var anim = firstMode.Animation;
                if (anim == null) { Log("FAIL: First mode has no Animation."); return; }
                Log($"Probing first mode '{firstMode.DisplayName}', anim GUID={anim.GUID}");

                // GetThumbnail returns hourglass first time; RequestUpdate + Update drives it
                var baseType = mainType.BaseType; // ThumbnailDrawerBase
                if (baseType == null) { Log("FAIL: MainThumbnailDrawer has no BaseType."); return; }
                var getThumbnail = baseType.GetMethod("GetThumbnail");
                var requestUpdate = baseType.GetMethod("RequestUpdate");
                var updateMethod = baseType.GetMethod("Update");
                var getCached = baseType.GetMethod("GetCachedThumbnailOrNull");
                if (getThumbnail == null || requestUpdate == null || updateMethod == null || getCached == null)
                { Log("FAIL: Required ThumbnailDrawerBase methods not all found."); return; }

                var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(anim.GUID));
                if (clip == null) { Log($"FAIL: Could not load clip for GUID {anim.GUID}."); return; }

                // First GetThumbnail: returns hourglass placeholder, schedules update
                getThumbnail.Invoke(drawer, new object[] { anim });
                requestUpdate.Invoke(drawer, new object[] { clip });

                // Drive synchronously up to 50 iterations; each Update() advances the coroutine
                Texture2D result = null;
                for (int i = 0; i < 50; i++)
                {
                    updateMethod.Invoke(drawer, null);
                    result = getCached.Invoke(drawer, new object[] { anim }) as Texture2D;
                    if (result != null) { Log($"Cached after {i + 1} Update() iterations: {result.width}x{result.height}"); break; }
                }
                if (result == null)
                {
                    Log("PARTIAL: Drawer didn't fill cache after 50 iterations. May need event-pump tick or different polling.");
                }
                else
                {
                    Log("OK: Thumbnail rendered. Visual verification — does the cached texture look correct?");
                }
            }
            catch (System.Exception ex)
            {
                var inner = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                Log($"FAIL: {inner.GetType().Name}: {inner.Message}");
            }
            finally
            {
                (drawer as System.IDisposable)?.Dispose();
            }
#else
            Log("SKIP: FACE_EMO not defined.");
#endif
        }

        private void Log(string msg)
        {
            _log += msg + "\n";
            Debug.Log("[ExprSessionTest] " + msg);
            Repaint();
        }

        private void SpikeResolveIExpressionEditor()
        {
            Log("--- Spike 0.2 ---");
#if FACE_EMO
            var launcher = FaceEmoAPI.FindLauncher();
            if (launcher == null) { Log("FAIL: No FaceEmoLauncher in scene."); return; }

            try
            {
                var installerType = System.Type.GetType(
                    "Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, jp.suzuryg.face-emo.appmain.Editor");
                if (installerType == null) { Log("FAIL: FaceEmoInstaller type not found."); return; }

                var installer = System.Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container").GetValue(installer);

                var ieeType = System.Type.GetType(
                    "Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, jp.suzuryg.face-emo.detail.Editor");
                if (ieeType == null) { Log("FAIL: IExpressionEditor type not found."); return; }

                var resolveMethod = container.GetType()
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Resolve"
                        && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                if (resolveMethod == null) { Log("FAIL: Resolve<T>() not found."); return; }

                var ee = resolveMethod.MakeGenericMethod(ieeType).Invoke(container, null);
                Log($"OK: IExpressionEditor resolved → {ee?.GetType().FullName ?? "null"}");
            }
            catch (System.Exception ex)
            {
                Log($"FAIL: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
#else
            Log("SKIP: FACE_EMO not defined.");
#endif
        }

        private void SpikeGetFacade()
        {
            Log("--- Spike 0.3 ---");
#if FACE_EMO
            var launcher = FaceEmoAPI.FindLauncher();
            if (launcher == null) { Log("FAIL: No launcher."); return; }
            try
            {
                // Resolve IExpressionEditor as in 0.2
                var installerType = System.Type.GetType("Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, jp.suzuryg.face-emo.appmain.Editor");
                var installer = System.Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container").GetValue(installer);
                var ieeType = System.Type.GetType("Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, jp.suzuryg.face-emo.detail.Editor");
                var resolve = container.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                var ee = resolve.MakeGenericMethod(ieeType).Invoke(container, null);

                // Probe all instance fields/props for ExpressionEditorModelFacade
                var facadeTypeName = "ExpressionEditorModelFacade";
                var eeType = ee.GetType();
                Log($"Probing {eeType.FullName} for facade...");

                var allFields = eeType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var f in allFields)
                {
                    if (f.FieldType.Name == facadeTypeName)
                    {
                        var v = f.GetValue(ee);
                        Log($"FOUND field '{f.Name}' → {v?.GetType().FullName ?? "null"}");
                        if (v != null)
                        {
                            // dump available methods
                            foreach (var m in v.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                if (m.DeclaringType == v.GetType())
                                    Log($"  method: {m.Name}({m.GetParameters().Length} args)");
                            }
                        }
                        return;
                    }
                }
                Log("FAIL: No field of type ExpressionEditorModelFacade on IExpressionEditor impl.");

                // Optional: also probe presenter
                // (record in notes if direct field access fails — may need presenter-mediated path)
            }
            catch (System.Exception ex)
            {
                Log($"FAIL: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
#else
            Log("SKIP: FACE_EMO not defined.");
#endif
        }

        private void SpikeSetBlendShape()
        {
            Log("--- Spike 0.4 ---");
#if FACE_EMO
            var launcher = FaceEmoAPI.FindLauncher();
            if (launcher == null) { Log("FAIL: No launcher."); return; }
            if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
            { Log("FAIL: AV3Setting/TargetAvatar missing."); return; }

            try
            {
                // 1. Open ExpressionEditor with a temp clip
                var clip = new AnimationClip { name = "SpikeProbeClip" };
                // Drive via FaceEmo's own launcher: open editor for a new clip
                var ieeType = System.Type.GetType("Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, jp.suzuryg.face-emo.detail.Editor");
                var installerType = System.Type.GetType("Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, jp.suzuryg.face-emo.appmain.Editor");
                var installer = System.Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container").GetValue(installer);
                var resolve = container.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                var ee = resolve.MakeGenericMethod(ieeType).Invoke(container, null);

                ieeType.GetMethod("Open").Invoke(ee, new object[] { clip });
                Log("ExpressionEditor opened with probe clip.");

                // 2. Acquire facade (using field name discovered in 0.3 — placeholder "_model")
                var facadeField = ee.GetType().GetField("_model", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                               ?? ee.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                      .FirstOrDefault(f => f.FieldType.Name == "ExpressionEditorModelFacade");
                if (facadeField == null) { Log("FAIL: facade field not found (re-run 0.3)."); return; }
                var facade = facadeField.GetValue(ee);
                Log($"Facade acquired: {facade.GetType().FullName}");

                // 3. Find first face blendshape and try SetBlendShapeValue
                var faceShapesProp = facade.GetType().GetProperty("FaceBlendShapes");
                var faceShapes = faceShapesProp.GetValue(facade) as System.Collections.IDictionary;
                if (faceShapes == null || faceShapes.Count == 0) { Log("FAIL: FaceBlendShapes empty."); return; }
                object firstKey = null;
                foreach (var k in faceShapes.Keys) { firstKey = k; break; }
                Log($"Trying SetBlendShapeValue on first shape: {firstKey}");

                var setMethod = facade.GetType().GetMethod("SetBlendShapeValue");
                setMethod.Invoke(facade, new object[] { firstKey, 100f });
                Log("OK: SetBlendShapeValue invoked without exception.");
                Log("→ Manually verify: ExpressionEditor preview shows the shape at 100. Repaint may be needed.");
            }
            catch (System.Exception ex)
            {
                Log($"FAIL: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
#else
            Log("SKIP: FACE_EMO not defined.");
#endif
        }
    }
}
