# FaceEmo Thumbnail Preview Integration — Implementation Plan B

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate FaceEmo's `MainThumbnailDrawer` / `GestureTableThumbnailDrawer` / `ExMenuThumbnailDrawer` so AI can capture per-Mode face thumbnails and display them in chat responses, plus refresh FaceEmo's MainView after edits.

**Architecture:** Reflection-resolve FaceEmo's `ThumbnailDrawerBase` derivatives via `Type.GetType()` (lives in `jp.suzuryg.face-emo.detail.Editor`, not referenced by our asmdef). Construct drawers with `(launcher.AV3Setting, launcher.ThumbnailSetting)` — both compile-time accessible. Drive `Update()` synchronously to materialize textures, encode to PNG, save under `Library/UnityAgent/face-thumbnails/`. New AgentTools route through a single `FaceEmoThumbnailRenderer` layer (mirrors Plan A's Bridge pattern).

**Tech Stack:** C# / Unity Editor / Reflection / FaceEmo (`jp.suzuryg.face-emo`) / `#if FACE_EMO` versionDefine

**Spec:** `docs/superpowers/specs/2026-05-15-faceemo-realtime-bridge-design.md` §4-bis (Preview Integration), §6 (Tool inventory rows tagged 新規/プレビュー), §10 (Renderer risks)

**Depends on:** Plan A (merged to master at `babc233`). All references to `FaceEmoGate`, `FaceEmoLauncherComponent`, `FaceEmoAPI.FindLauncher`/`LoadMenu`/`FindExpression`/`RefreshWindowIfOpen` are stable post-Plan A.

**Branch:** Create new `feat/faceemo-thumbnail-integration` from master.

---

## Scope

### Included (Plan B core)

- **Renderer**: `FaceEmoThumbnailRenderer` — reflection layer for 3 Drawer types
- **AgentTools (4)**:
  - `CaptureFaceEmoModeThumbnail(modeName)` (A — Mode サムネ PNG)
  - `RefreshFaceEmoMainView(modeName?)` (A — MainView 再生成)
  - `CaptureFaceEmoGestureTable(modeName)` (B — 8 セル合成 PNG)
  - `CaptureFaceEmoExMenuThumbnails(modeName?)` (C — ExMenu 焼込画像)
- Updates to `BuiltInSkills.cs` workflow B and CHANGELOG

### Excluded

- D `InspectorThumbnailDrawer` — AI 操作対象外（FaceEmo の UI サイズ調整用）
- E mouseover 拡大プレビュー — FaceEmo MainView 内 UI で AI 操作対象外
- F `PreviewWindow` — Plan A の `ExpressionEditorBridge.TryOpenPreviewWindow()` で既に対応済み

---

## File Structure

### New files

```
Editor/Tools/FaceEmoExpressionEditor/
    FaceEmoThumbnailRenderer.cs           # Reflection layer for 3 ThumbnailDrawer types
    FaceEmoThumbnailTools.cs              # 4 AgentTools (Capture/Refresh)
```

### Modified files

```
Editor/Tools/BuiltInSkills.cs             # Append thumbnail-capture step to Workflow B
Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs   # Phase B test buttons
CHANGELOG.md                              # Plan B entry under Unreleased
```

### Asmdef placement

The new files go under `Editor/Tools/FaceEmoExpressionEditor/` which is already covered by `Editor/AjisaiFlow.UnityAgent.Editor.asmdef`. No new asmdef needed. `FACE_EMO` versionDefine is already set up in the parent asmdef.

---

## Testing Strategy

This repo has no Unity Test Runner asmdef — verification is via the existing `ExpressionSessionTestWindow` (extended in Plan A) plus static `grep` checks during implementation. Manual integration test happens at end of Plan B (Phase 4) via the user opening Unity.

Subagent's "Verify" steps use grep-based static checks. The user runs the test window after Plan B is merged.

---

# Phase 0: Spike — ThumbnailDrawer reflection

This phase verifies the 3 Drawer types can be instantiated via reflection with `(launcher.AV3Setting, launcher.ThumbnailSetting)`. If the spike fails, Plan B can't ship — fall back to a revised design (e.g. trigger FaceEmo's own thumbnail refresh via `FaceEmoLauncher.Launch()` only, without our own rendering).

### Task 0.1: Spike harness — drawer instantiation + RenderOnce probe

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs`
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Add Phase B label and spike button**

In `ExpressionSessionTestWindow.OnGUI()`, insert a new section AFTER the existing "Phase 3: Session" buttons and BEFORE the "Log:" label:

```csharp
EditorGUILayout.LabelField("Phase B: Thumbnail (Plan B Spike)", EditorStyles.boldLabel);
if (GUILayout.Button("Spike B.0: Instantiate ThumbnailDrawers + render first Mode"))
{
    SpikeThumbnailRender();
}
```

- [ ] **Step 2: Add probe method**

Append the method to the same class (before `Log(string)` helper):

```csharp
private void SpikeThumbnailRender()
{
    Log("--- Spike B.0 ---");
#if FACE_EMO
    var launcher = FaceEmoAPI.FindLauncher();
    if (launcher == null) { Log("FAIL: No launcher."); return; }
    if (launcher.AV3Setting == null || launcher.ThumbnailSetting == null)
    { Log("FAIL: AV3Setting or ThumbnailSetting missing on launcher."); return; }

    const string detailAsm = "jp.suzuryg.face-emo.detail.Editor";
    try
    {
        var mainType = System.Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.MainThumbnailDrawer, {detailAsm}");
        if (mainType == null) { Log("FAIL: MainThumbnailDrawer type not found."); return; }

        var drawer = System.Activator.CreateInstance(mainType,
            new object[] { launcher.AV3Setting, launcher.ThumbnailSetting });
        Log($"OK: Drawer instantiated → {drawer.GetType().FullName}");

        // Get first Mode from menu
        var menu = FaceEmoAPI.LoadMenu(launcher);
        var modes = FaceEmoAPI.GetAllExpressions(menu);
        if (modes.Count == 0) { Log("FAIL: No modes in menu."); return; }
        var firstMode = modes[0].mode;
        var anim = firstMode.Animation;
        if (anim == null) { Log("FAIL: First mode has no Animation."); return; }
        Log($"Probing first mode '{firstMode.DisplayName}', anim GUID={anim.GUID}");

        // GetThumbnail returns hourglass first time; RequestUpdate + Update drives it
        var baseType = mainType.BaseType; // ThumbnailDrawerBase
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

        ((System.IDisposable)drawer).Dispose();
    }
    catch (System.Exception ex)
    {
        var inner = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
        Log($"FAIL: {inner.GetType().Name}: {inner.Message}");
    }
#else
    Log("SKIP: FACE_EMO not defined.");
#endif
}
```

- [ ] **Step 3: Static verify**

```
grep -n "SpikeThumbnailRender" Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
grep -n "Phase B: Thumbnail" Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
```

Both should match.

- [ ] **Step 4: Append spike entry to results doc**

In `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`, insert this BEFORE the "## Integration Test Checklist" section:

```markdown
## B.0 ThumbnailDrawer instantiation + render
Status: TBD (user runs Spike B.0 button)
Notes:
- Drawer types: MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer
- Ctor args: (AV3Setting, ThumbnailSetting) — both public properties on FaceEmoLauncherComponent
- Render driver: GetThumbnail + RequestUpdate + Update() loop until GetCachedThumbnailOrNull returns non-null
- Drives synchronously, no main-thread blocking event loop required
- Expected: Cached after a few Update() iterations (1-10 typical)

```

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs \
        docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "spike(plan-b): probe ThumbnailDrawer instantiation + sync render driver"
```

> **DO NOT proceed past Phase 0 until the user runs Spike B.0 in Unity and confirms PASS in the spike doc.** If PARTIAL (cache fill timeout) — the polling-loop count may need adjustment in Phase 1.2. If FAIL (type not found / ctor exception) — escalate; Plan B may need redesign.

---

# Phase 1: FaceEmoThumbnailRenderer (core)

### Task 1.1: Renderer skeleton + TryInitialize

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`

- [ ] **Step 1: Write skeleton with reflection cache + Init**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
#if FACE_EMO
using System;
using System.IO;
using System.Reflection;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// FaceEmo の MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer を
    /// reflection でインスタンス化し、PNG を Library/UnityAgent/face-thumbnails/ に出力する薄い層。
    /// バージョン差で reflection が壊れた場合は IsHealthy=false でフォールバック表示する。
    /// </summary>
    internal sealed class FaceEmoThumbnailRenderer : IDisposable
    {
        public bool IsHealthy { get; private set; }
        public string LastReflectionError { get; private set; }

        private const string DetailAsm = "jp.suzuryg.face-emo.detail.Editor";

        private object _mainDrawer;
        private object _gestureDrawer;
        private object _exMenuDrawer;
        private FaceEmoLauncherComponent _launcher;

        // Cached MethodInfo (resolved once in TryInitialize)
        private MethodInfo _getThumbnail;
        private MethodInfo _requestUpdate;
        private MethodInfo _update;
        private MethodInfo _getCached;

        public static string CacheRoot => "Library/UnityAgent/face-thumbnails";

        public bool TryInitialize(FaceEmoLauncherComponent launcher)
        {
            // Reset state
            _mainDrawer = null;
            _gestureDrawer = null;
            _exMenuDrawer = null;
            _launcher = null;
            _getThumbnail = null;
            _requestUpdate = null;
            _update = null;
            _getCached = null;
            IsHealthy = false;
            LastReflectionError = null;

            if (launcher == null) return Fail("launcher is null");
            if (launcher.AV3Setting == null) return Fail("launcher.AV3Setting is null");
            if (launcher.ThumbnailSetting == null) return Fail("launcher.ThumbnailSetting is null");

            try
            {
                _launcher = launcher;

                var mainType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.MainThumbnailDrawer, {DetailAsm}");
                var gestureType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.GestureTableThumbnailDrawer, {DetailAsm}");
                var exMenuType = Type.GetType($"Suzuryg.FaceEmo.Detail.Drawing.ExMenuThumbnailDrawer, {DetailAsm}");
                if (mainType == null) return Fail("MainThumbnailDrawer type not found");
                if (gestureType == null) return Fail("GestureTableThumbnailDrawer type not found");
                if (exMenuType == null) return Fail("ExMenuThumbnailDrawer type not found");

                _mainDrawer = Activator.CreateInstance(mainType, launcher.AV3Setting, launcher.ThumbnailSetting);
                _gestureDrawer = Activator.CreateInstance(gestureType, launcher.AV3Setting, launcher.ThumbnailSetting);
                _exMenuDrawer = Activator.CreateInstance(exMenuType, launcher.AV3Setting, launcher.ThumbnailSetting);

                // Cache MethodInfos from base class
                var baseType = mainType.BaseType; // ThumbnailDrawerBase
                _getThumbnail = baseType.GetMethod("GetThumbnail");
                _requestUpdate = baseType.GetMethod("RequestUpdate");
                _update = baseType.GetMethod("Update");
                _getCached = baseType.GetMethod("GetCachedThumbnailOrNull");
                if (_getThumbnail == null) return Fail("GetThumbnail method missing");
                if (_requestUpdate == null) return Fail("RequestUpdate method missing");
                if (_update == null) return Fail("Update method missing");
                if (_getCached == null) return Fail("GetCachedThumbnailOrNull method missing");

                IsHealthy = true;
                return true;
            }
            catch (Exception ex)
            {
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                return Fail($"{inner.GetType().Name}: {inner.Message}");
            }
        }

        public void Dispose()
        {
            (_mainDrawer as IDisposable)?.Dispose();
            (_gestureDrawer as IDisposable)?.Dispose();
            (_exMenuDrawer as IDisposable)?.Dispose();
            _mainDrawer = null;
            _gestureDrawer = null;
            _exMenuDrawer = null;
            _launcher = null;
        }

        private bool Fail(string msg)
        {
            IsHealthy = false;
            LastReflectionError = msg;
            Debug.LogWarning($"[FaceEmoThumbnailRenderer] {msg}");
            return false;
        }
    }
}
#endif
```

- [ ] **Step 2: Static verify**

```
grep -n "internal sealed class FaceEmoThumbnailRenderer" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
grep -n "TryInitialize" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
grep -n "CacheRoot" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
```

All three should match. In Unity, no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
git commit -m "feat(faceemo): scaffold FaceEmoThumbnailRenderer with TryInitialize"
```

---

### Task 1.2: RenderModeThumbnail (A — single Mode)

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`

- [ ] **Step 1: Add helper for sync-driving GetThumbnail**

Inside the class, after `Dispose()`:

```csharp
/// <summary>
/// Render a thumbnail synchronously by repeatedly calling Update() until the cache fills.
/// Returns the cached Texture2D or null on timeout.
/// </summary>
private Texture2D DriveSyncRender(object drawer, Suzuryg.FaceEmo.Domain.Animation animation, AnimationClip clip, int maxIterations = 50)
{
    // Prime the cache request
    _getThumbnail.Invoke(drawer, new object[] { animation });
    _requestUpdate.Invoke(drawer, new object[] { clip });

    for (int i = 0; i < maxIterations; i++)
    {
        _update.Invoke(drawer, null);
        var cached = _getCached.Invoke(drawer, new object[] { animation }) as Texture2D;
        if (cached != null) return cached;
    }
    return null;
}

/// <summary>
/// Save a Texture2D to the renderer's PNG cache. Returns the saved path (relative to project root).
/// </summary>
private string SaveAsPng(Texture2D texture, string fileName)
{
    if (texture == null) return null;
    Directory.CreateDirectory(CacheRoot);
    string path = Path.Combine(CacheRoot, fileName).Replace('\\', '/');

    // Need readable texture for EncodeToPNG — copy via RenderTexture if needed
    var readable = MakeReadableCopy(texture);
    File.WriteAllBytes(path, readable.EncodeToPNG());
    if (readable != texture) UnityEngine.Object.DestroyImmediate(readable);
    return path;
}

private static Texture2D MakeReadableCopy(Texture2D source)
{
    if (source.isReadable) return source;
    var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
    Graphics.Blit(source, rt);
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
    copy.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    return copy;
}
```

- [ ] **Step 2: Add `RenderModeThumbnail` public method**

```csharp
/// <summary>
/// Render the single-Mode thumbnail (A — MainThumbnailDrawer) and save as PNG.
/// Returns the saved PNG path, or null on failure.
/// </summary>
public string RenderModeThumbnail(string modeName)
{
    if (!IsHealthy) { LastReflectionError = "Renderer not healthy — call TryInitialize first"; return null; }
    if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

    var menu = FaceEmoAPI.LoadMenu(_launcher);
    if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
    var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
    if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }
    var anim = mode.Animation;
    if (anim == null || string.IsNullOrEmpty(anim.GUID))
    { LastReflectionError = $"Mode '{modeName}' has no animation"; return null; }

    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(anim.GUID));
    if (clip == null) { LastReflectionError = $"Clip not found for GUID {anim.GUID}"; return null; }

    var texture = DriveSyncRender(_mainDrawer, anim, clip);
    if (texture == null) { LastReflectionError = "Render timed out (50 iterations)"; return null; }

    return SaveAsPng(texture, $"{SanitizeFileName(modeName)}.png");
}

private static string SanitizeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    var sb = new System.Text.StringBuilder(name.Length);
    foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
    return sb.ToString();
}
```

- [ ] **Step 3: Add test button**

In `ExpressionSessionTestWindow.OnGUI()`, under the existing "Phase B" section, add:

```csharp
if (GUILayout.Button("Test: Renderer.RenderModeThumbnail('Neutral')"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    using var r = new FaceEmoThumbnailRenderer();
    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); return; }
    var path = r.RenderModeThumbnail("Neutral");
    Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
}
```

- [ ] **Step 4: Static verify**

```
grep -n "RenderModeThumbnail" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
grep -n "DriveSyncRender" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
grep -n "Renderer.RenderModeThumbnail" Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
```

All three should match. In Unity, compile clean.

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Renderer.RenderModeThumbnail — single Mode PNG output"
```

---

### Task 1.3: RenderExMenuThumbnail (C — VRChat menu image)

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`

- [ ] **Step 1: Add `RenderExMenuThumbnail`**

The logic is identical to `RenderModeThumbnail` but uses `_exMenuDrawer` and a different file prefix:

```csharp
/// <summary>
/// Render the ExMenu (VRChat-baked) thumbnail (C — ExMenuThumbnailDrawer) and save as PNG.
/// Returns the saved PNG path, or null on failure.
/// </summary>
public string RenderExMenuThumbnail(string modeName)
{
    if (!IsHealthy) { LastReflectionError = "Renderer not healthy"; return null; }
    if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

    var menu = FaceEmoAPI.LoadMenu(_launcher);
    if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
    var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
    if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }
    var anim = mode.Animation;
    if (anim == null || string.IsNullOrEmpty(anim.GUID))
    { LastReflectionError = $"Mode '{modeName}' has no animation"; return null; }

    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(anim.GUID));
    if (clip == null) { LastReflectionError = $"Clip not found for GUID {anim.GUID}"; return null; }

    var texture = DriveSyncRender(_exMenuDrawer, anim, clip);
    if (texture == null) { LastReflectionError = "ExMenu render timed out"; return null; }

    return SaveAsPng(texture, $"exmenu_{SanitizeFileName(modeName)}.png");
}
```

- [ ] **Step 2: Add test button**

```csharp
if (GUILayout.Button("Test: Renderer.RenderExMenuThumbnail('Neutral')"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    using var r = new FaceEmoThumbnailRenderer();
    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); return; }
    var path = r.RenderExMenuThumbnail("Neutral");
    Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
}
```

- [ ] **Step 3: Static verify**

```
grep -n "RenderExMenuThumbnail" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
```

Match.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Renderer.RenderExMenuThumbnail — ExMenu baked image"
```

---

# Phase 2: Gesture table + MainView refresh

### Task 2.1: RenderGestureTable (B — 8 cell grid)

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`

- [ ] **Step 1: Understand the gesture mapping**

FaceEmo's `HandGesture` enum (from `Suzuryg.FaceEmo.Domain`) has 8 values:
0=Neutral, 1=Fist, 2=HandOpen, 3=Fingerpoint, 4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp.

Each Mode has `Branches[]` where each branch has `Conditions[]` (Hand × HandGesture × ComparisonOperator). The branch's `BaseAnimation` / `LeftHandAnimation` / `RightHandAnimation` / `BothHandsAnimation` slots provide the actual clip per gesture combination. For a simple GestureTable, we capture 8 cells = the Mode's BaseAnimation for each HandGesture index when matched by any branch, or fall back to the Mode's main animation when no branch matches.

For the first implementation, capture the **same Mode animation 8 times** in a 4x2 grid as a placeholder. This is a sensible first cut — the more sophisticated per-branch rendering can be Plan B-2.

Actually no — keep it useful: for each of the 8 HandGestures, find the matching branch's animation (if any), else use the Mode's animation. Render each cell.

- [ ] **Step 2: Add gesture-name helper**

```csharp
private static readonly string[] GestureNames =
{
    "Neutral", "Fist", "HandOpen", "Fingerpoint",
    "Victory", "RockNRoll", "HandGun", "ThumbsUp"
};

/// <summary>
/// For each of the 8 hand gestures, find the matching branch's animation in the given Mode.
/// Returns an array of 8 (Animation, AnimationClip) pairs, indexed by HandGesture (0-7).
/// Falls back to the Mode's base animation when no branch matches a given gesture.
/// </summary>
private (Suzuryg.FaceEmo.Domain.Animation anim, AnimationClip clip)[] ResolveGestureAnimations(Suzuryg.FaceEmo.Domain.IMode mode)
{
    var result = new (Suzuryg.FaceEmo.Domain.Animation, AnimationClip)[8];
    var baseAnim = mode.Animation;
    var baseClip = baseAnim != null && !string.IsNullOrEmpty(baseAnim.GUID)
        ? AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(baseAnim.GUID))
        : null;

    // Initialize all slots to base
    for (int i = 0; i < 8; i++) result[i] = (baseAnim, baseClip);

    // Walk branches; for each branch, if conditions cover a specific gesture, override that slot
    if (mode.Branches != null)
    {
        foreach (var branch in mode.Branches)
        {
            if (branch == null || branch.Conditions == null) continue;
            foreach (var cond in branch.Conditions)
            {
                int gestureIdx = (int)cond.HandGesture;
                if (gestureIdx < 0 || gestureIdx >= 8) continue;
                var slotAnim = branch.BaseAnimation ?? baseAnim;
                if (slotAnim != null && !string.IsNullOrEmpty(slotAnim.GUID))
                {
                    var slotClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(slotAnim.GUID));
                    if (slotClip != null) result[gestureIdx] = (slotAnim, slotClip);
                }
            }
        }
    }
    return result;
}
```

- [ ] **Step 3: Add `RenderGestureTable` method**

```csharp
/// <summary>
/// Render an 8-cell gesture table (B — GestureTableThumbnailDrawer) and save as a composite PNG.
/// Returns the saved PNG path, or null on failure.
/// </summary>
public string RenderGestureTable(string modeName)
{
    if (!IsHealthy) { LastReflectionError = "Renderer not healthy"; return null; }
    if (_launcher == null) { LastReflectionError = "Launcher is null"; return null; }

    var menu = FaceEmoAPI.LoadMenu(_launcher);
    if (menu == null) { LastReflectionError = "Could not load FaceEmo menu"; return null; }
    var (_, mode) = FaceEmoAPI.FindExpression(menu, modeName);
    if (mode == null) { LastReflectionError = $"Mode '{modeName}' not found"; return null; }

    var slots = ResolveGestureAnimations(mode);

    // Render each cell
    var cells = new Texture2D[8];
    int cellW = 0, cellH = 0;
    for (int i = 0; i < 8; i++)
    {
        if (slots[i].clip == null) continue;
        var tex = DriveSyncRender(_gestureDrawer, slots[i].anim, slots[i].clip);
        if (tex == null) continue;
        var readable = MakeReadableCopy(tex);
        cells[i] = readable;
        if (cellW == 0) { cellW = readable.width; cellH = readable.height; }
    }

    if (cellW == 0) { LastReflectionError = "No gesture cells could be rendered"; return null; }

    // Composite into 4x2 grid (4 cols × 2 rows) with 2px border, gesture name label
    const int padding = 4;
    const int labelH = 14;
    int gridW = cellW * 4 + padding * 5;
    int gridH = (cellH + labelH) * 2 + padding * 3;
    var composite = new Texture2D(gridW, gridH, TextureFormat.RGBA32, false);

    // Fill with dark gray background
    var bg = new Color32(40, 40, 48, 255);
    var bgPixels = new Color32[gridW * gridH];
    for (int p = 0; p < bgPixels.Length; p++) bgPixels[p] = bg;
    composite.SetPixels32(bgPixels);

    for (int i = 0; i < 8; i++)
    {
        int col = i % 4;
        int row = i / 4;
        int x = padding + col * (cellW + padding);
        int y = padding + row * (cellH + labelH + padding);
        if (cells[i] != null)
        {
            var pixels = cells[i].GetPixels32();
            composite.SetPixels32(x, y + labelH, cellW, cellH, pixels);
        }
    }
    composite.Apply();

    string path = SaveAsPng(composite, $"{SanitizeFileName(modeName)}_gestures.png");

    // Cleanup
    foreach (var c in cells) if (c != null) UnityEngine.Object.DestroyImmediate(c);
    UnityEngine.Object.DestroyImmediate(composite);
    return path;
}
```

- [ ] **Step 4: Add test button**

```csharp
if (GUILayout.Button("Test: Renderer.RenderGestureTable('Neutral')"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    using var r = new FaceEmoThumbnailRenderer();
    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); return; }
    var path = r.RenderGestureTable("Neutral");
    Log(path != null ? $"OK: PNG at {path}" : $"FAIL: {r.LastReflectionError}");
}
```

- [ ] **Step 5: Static verify**

```
grep -n "RenderGestureTable" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
grep -n "ResolveGestureAnimations" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
```

Both match.

- [ ] **Step 6: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Renderer.RenderGestureTable — 4×2 gesture composite PNG"
```

---

### Task 2.2: RefreshMainView (A — invalidate FaceEmo window cache)

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs`

The simplest reliable approach is to delegate to the existing `FaceEmoAPI.RefreshWindowIfOpen()` which calls `FaceEmoLauncher.Launch()` — this fully relaunches the FaceEmo window and forces its internal drawers to repopulate. The `modeName` parameter is informational only (logged) since the relaunch is global.

- [ ] **Step 1: Add `RefreshMainView`**

```csharp
/// <summary>
/// Force FaceEmo's MainView to refresh its thumbnail cache by relaunching the window.
/// modeName is informational (logged), since the relaunch is global.
/// Returns true if the window was open and was refreshed; false if not open or on error.
/// </summary>
public bool RefreshMainView(string modeName = null)
{
    if (_launcher == null) { LastReflectionError = "Launcher is null"; return false; }
    try
    {
        FaceEmoAPI.RefreshWindowIfOpen(_launcher);
        if (!string.IsNullOrEmpty(modeName))
            Debug.Log($"[FaceEmoThumbnailRenderer] MainView refreshed (target Mode: {modeName})");
        return true;
    }
    catch (Exception ex)
    {
        LastReflectionError = $"RefreshMainView: {ex.GetType().Name}: {ex.Message}";
        return false;
    }
}
```

- [ ] **Step 2: Add test button**

```csharp
if (GUILayout.Button("Test: Renderer.RefreshMainView()"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    using var r = new FaceEmoThumbnailRenderer();
    if (!r.TryInitialize(gate.Launcher)) { Log($"FAIL init: {r.LastReflectionError}"); return; }
    bool ok = r.RefreshMainView("Neutral");
    Log(ok ? "OK" : $"FAIL: {r.LastReflectionError}");
}
```

- [ ] **Step 3: Static verify**

```
grep -n "RefreshMainView" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs
```

Match.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailRenderer.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Renderer.RefreshMainView via FaceEmoAPI.RefreshWindowIfOpen"
```

---

# Phase 3: AgentTools surface

### Task 3.1: ExpressionThumbnailTools — 4 AgentTools

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs`

- [ ] **Step 1: Write the file**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs
#if FACE_EMO
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// AgentTools for capturing FaceEmo expression thumbnails (Plan B).
    /// All tools require FaceEmoGate.RequireExpressionEditingReady() to pass.
    /// </summary>
    public static class FaceEmoThumbnailTools
    {
        [AgentTool("Capture a single FaceEmo Mode's face thumbnail as a PNG and return its path. " +
                   "Use this to embed expression preview images in AI responses. " +
                   "modeName: the FaceEmo Mode display name to render.")]
        public static string CaptureFaceEmoModeThumbnail(string modeName)
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}. Expression editing still works; only thumbnails are unavailable.";

            var path = r.RenderModeThumbnail(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError}";
            return $"Success: Captured thumbnail at '{path}'.";
        }

        [AgentTool("Force-refresh FaceEmo's MainView thumbnail cache after editing an expression. " +
                   "Call this after CommitExpressionSession so the MainView shows the updated face. " +
                   "modeName is informational (the relaunch is global).")]
        public static string RefreshFaceEmoMainView(string modeName = "")
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            return r.RefreshMainView(string.IsNullOrEmpty(modeName) ? null : modeName)
                ? "Success: MainView refreshed."
                : $"Error: {r.LastReflectionError}";
        }

        [AgentTool("Capture a 4×2 grid of the 8 hand-gesture face thumbnails for a Mode and return the composite PNG path. " +
                   "Use this to show the user how all gesture combinations look. " +
                   "modeName: the FaceEmo Mode display name.")]
        public static string CaptureFaceEmoGestureTable(string modeName)
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            var path = r.RenderGestureTable(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError}";
            return $"Success: Captured gesture table at '{path}'.";
        }

        [AgentTool("Capture the ExMenu (VRChat menu)-baked thumbnail for a Mode and return its PNG path. " +
                   "Use this to preview what the avatar's VRChat radial menu will look like after upload. " +
                   "modeName: the FaceEmo Mode display name.")]
        public static string CaptureFaceEmoExMenuThumbnail(string modeName)
        {
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            var path = r.RenderExMenuThumbnail(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError}";
            return $"Success: Captured ExMenu thumbnail at '{path}'.";
        }
    }
}
#endif
```

- [ ] **Step 2: Static verify**

```
grep -cE "AgentTool" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs
```

Expected: 4 matches.

```
grep -nE "CaptureFaceEmoModeThumbnail|RefreshFaceEmoMainView|CaptureFaceEmoGestureTable|CaptureFaceEmoExMenuThumbnail" Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs
```

Expected: 4 method definitions.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs
git commit -m "feat(faceemo): add 4 thumbnail AgentTools — Capture Mode/Gesture/ExMenu + Refresh MainView"
```

---

### Task 3.2: Update Skill workflow + CHANGELOG

**Files:**
- Modify: `Editor/Tools/BuiltInSkills.cs` (FaceEmo skill block)
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Locate Workflow B in BuiltInSkills.cs**

Find the section starting around `### B. Create Expression from Preset (RECOMMENDED — fastest path)` (around line 282 after Plan A rewrites). Replace the workflow body to insert thumbnail-capture steps:

```
### B. Create Expression from Preset (RECOMMENDED — fastest path) (""笑顔の表情を作って"")

1. [AnalyzeFaceBlendShapes('Avatar')]
2. [OpenExpressionSession(newName='Smile')] → MainWindow + ExpressionEditor を開く (Live セッション)
3. [SuggestExpressionShapes('Avatar', 'smile')] → 'shape1=80;shape2=100;...' を取得
4. [SetExpressionPreviewMulti('Avatar', '<shapeData>')] → ExpressionEditor のライブプレビューに即反映
5. [CaptureFaceEmoModeThumbnail('Smile')] → AI 応答に表情画像を添付 (auto-session の場合は新規セッションの暫定名で呼ぶ)
6. (任意) [ReadExpressionFromWindow()] → ユーザーが手で動かしたスライダーを取り込む
7. [CommitExpressionSession()] → .anim 保存 + FaceEmo Menu に登録
8. [RefreshFaceEmoMainView()] → MainView の Mode サムネを最新化
9. [ApplyFaceEmoToAvatar()] → FX レイヤー生成

This is the canonical flow. FaceEmo and a configured launcher+avatar are REQUIRED.
```

- [ ] **Step 2: Append the AgentTool inventory in the same skill**

Look for the existing AgentTool listing in the FaceEmo skill (around the "Expression Building" or "Available Tools" section). Add 4 new bullet items:

```
- [CaptureFaceEmoModeThumbnail('Smile')] → Save Mode face thumbnail as PNG (for AI response embedding)
- [CaptureFaceEmoGestureTable('Smile')] → Save 4×2 grid of all 8 gesture variants
- [CaptureFaceEmoExMenuThumbnail('Smile')] → Save the VRChat menu image
- [RefreshFaceEmoMainView()] → Force-refresh FaceEmo MainView thumbnails after edits
```

- [ ] **Step 3: Update CHANGELOG.md**

Append under `[Unreleased] > Added`:

```markdown
- Plan B (Thumbnail integration): `CaptureFaceEmoModeThumbnail`, `CaptureFaceEmoGestureTable`, `CaptureFaceEmoExMenuThumbnail`, `RefreshFaceEmoMainView` AgentTools.
- `FaceEmoThumbnailRenderer` (internal, reflection layer for FaceEmo's MainThumbnailDrawer / GestureTableThumbnailDrawer / ExMenuThumbnailDrawer).
- PNG output under `Library/UnityAgent/face-thumbnails/`.
```

Remove or update the existing `### Notes` line that says "Plan B is tracked separately and not yet released" — replace with "Plan B (Thumbnail integration) is included in this release."

- [ ] **Step 4: Static verify**

```
grep -n "CaptureFaceEmoModeThumbnail" Editor/Tools/BuiltInSkills.cs
grep -n "RefreshFaceEmoMainView" Editor/Tools/BuiltInSkills.cs
grep -n "Plan B" CHANGELOG.md
```

All three should match.

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/BuiltInSkills.cs CHANGELOG.md
git commit -m "docs: Plan B — wire thumbnail tools into Workflow B + CHANGELOG"
```

---

### Task 3.3: Integration test checklist + final commit

**Files:**
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Append Plan B integration tests**

Add to the bottom of `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`, after the existing Plan A integration test checklist:

```markdown

---

## Plan B Integration Test Checklist (after merge)

シーン: FaceEmo + ターゲットアバター + 表情 1 つ以上が登録された状態

- [ ] `CaptureFaceEmoModeThumbnail('Neutral')` → `Library/UnityAgent/face-thumbnails/Neutral.png` が生成され、顔が正しく描画されている
- [ ] `CaptureFaceEmoGestureTable('Neutral')` → 4×2 のグリッド合成 PNG が生成され、8 セルが暗背景の上に並んでいる
- [ ] `CaptureFaceEmoExMenuThumbnail('Neutral')` → ExMenu サイズのサムネ PNG が生成されている
- [ ] 既存表情を編集 → `CommitExpressionSession` → `RefreshFaceEmoMainView()` → FaceEmo MainView のサムネが新しい表情を反映している
- [ ] FaceEmo をアンインストール → 各 Capture ツールが "Error: FaceEmo is not installed." を返す
- [ ] `FaceEmoThumbnailRenderer.IsHealthy=false` を強制 → Capture ツールが `Error: Thumbnail renderer init failed — ...` を返す（表情変更ツールは健在）
- [ ] サムネ PNG が `Library/UnityAgent/face-thumbnails/` に蓄積する。ファイル名衝突時は上書き
- [ ] 名前に invalid file char を含む Mode（例: `'Test/Slash'`）でも path が正しく sanitize される
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "docs: Plan B integration test checklist (8 manual scenarios)"
```

---

## Self-Review

### Spec coverage check

| Spec §4-bis item | Plan B task |
|---|---|
| A — `MainThumbnailDrawer` | Tasks 1.1 (init) + 1.2 (RenderModeThumbnail) |
| A — RefreshMainView | Task 2.2 |
| B — `GestureTableThumbnailDrawer` | Task 2.1 |
| C — `ExMenuThumbnailDrawer` | Task 1.3 |
| D — Inspector | Out of scope (per spec) |
| E — mouseover | Out of scope (per spec) |
| F — `PreviewWindow` | Already in Plan A `Bridge.TryOpenPreviewWindow` |
| `FaceEmoThumbnailRenderer` class | Tasks 1.1-1.3, 2.1, 2.2 |
| 4 AgentTools | Task 3.1 |
| Workflow B update | Task 3.2 |
| Renderer.IsHealthy fallback | Task 1.1 (TryInitialize sets it) + Task 3.1 (all tools check & return error) |
| PNG cache location `Library/UnityAgent/face-thumbnails/` | Task 1.1 (CacheRoot property) |
| File size management (oldest-N) | Not addressed in Plan B — risk §10 is acknowledged but defer to actual ops issue |

**Gap**: PNG cache eviction (max N files) — deliberately deferred. Mark as Plan B-2 candidate.

### Placeholder scan

- No "TBD", "TODO", "implement later" in production code blocks. (Spike doc has TBD by design, filled by user.)
- All steps have either code or explicit verify commands.
- Self-review template asks for code; provided in every step.

### Type consistency

- `FaceEmoThumbnailRenderer.TryInitialize(launcher)` — same arg used by all consumers in Phase 3
- `RenderModeThumbnail(string modeName)` / `RenderGestureTable(string modeName)` / `RenderExMenuThumbnail(string modeName)` — consistent signatures, all return `string` PNG path or null
- `RefreshMainView(string modeName = null)` — single optional arg, consistent with `RefreshFaceEmoMainView(string modeName = "")` AgentTool (Task 3.1)
- `Suzuryg.FaceEmo.Domain.Animation` and `Suzuryg.FaceEmo.Domain.IMode` used consistently
- AgentTool method names match exactly between spec §4-bis, plan tasks 3.1, and workflow doc in Task 3.2

### File-structure check

- `FaceEmoThumbnailRenderer.cs` ~280 lines after all tasks: borderline large but cohesive (one clear responsibility: render thumbnails)
- `FaceEmoThumbnailTools.cs` ~80 lines: focused
- No files split unnecessarily

**Plan B is self-contained**: each task produces working, testable code (sync drawer instantiation → ModeThumbnail → ExMenu → GestureTable → RefreshMainView → AgentTools surface → docs). Each commit leaves the repo in a buildable state.
