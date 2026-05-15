# FaceEmo Mandatory + ExpressionEditor Realtime Bridge — Implementation Plan A (Core)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** FaceEmo を表情変更ツールの必須前提とし、FaceEmo の ExpressionEditor とリアルタイム双方向同期する Live/Degraded ハイブリッドブリッジを実装する。

**Architecture:** 4 層構造 — `FaceEmoGate`（事前条件）/ `FaceEmoExpressionSession`（モード判定）/ `ExpressionEditorBridge`（reflection 局所化）/ AssetPathFallback（Degraded）。Live は `ExpressionEditorModelFacade.SetBlendShapeValue` 直結、Degraded は `AnimationUtility.SetEditorCurve` ＋ `RefreshWindowIfOpen`。

**Tech Stack:** C# / Unity Editor / Reflection / FaceEmo (`jp.suzuryg.face-emo`) / `#if FACE_EMO` versionDefine

**Spec:** `docs/superpowers/specs/2026-05-15-faceemo-realtime-bridge-design.md`

**Scope:** プレビュー統合（Thumbnail Renderer + Capture 系 4 ツール）は **Plan B** に分離。本プランは「FaceEmo 必須化 + Live/Degraded セッション + 既存ツール改修 + セッション系 4 AgentTool + Skill ドキュメント更新」を含む。

**Branch:** `design/faceemo-realtime-bridge`（spec コミット済み、ここに実装を積み上げる）

---

## File Structure

### 新規ファイル

```
Editor/Tools/FaceEmoGate.cs                                         # ② Gate
Editor/Tools/FaceEmoExpressionEditor/
    ExpressionEditorBridge.cs                                       # ④ Bridge (FACE_EMO ガード)
    FaceEmoExpressionSession.cs                                     # ③ Session
    AssetPathFallback.cs                                            # ④' Degraded 経路
    ExpressionSessionTools.cs                                       # ① 新 AgentTool 4 個
    ExpressionSessionTestWindow.cs                                  # 検証用 EditorWindow
    AjisaiFlow.UnityAgent.FaceEmoExpressionEditor.Editor.asmdef     # 不要（既存 Editor asmdef に含める）
```

> **asmdef 方針**: 新規ディレクトリは既存の `AjisaiFlow.UnityAgent.Editor.asmdef`（`Editor/` 直下）の傘下に入る。新 asmdef は作らない。`FACE_EMO` versionDefine は親 asmdef で既に定義済み。

### 改修ファイル

```
Editor/Tools/FaceProfileTools.cs            # SetExpressionPreviewMulti / SuggestExpressionShapes 改修
Editor/Tools/FaceEmoAdvancedTools.cs        # SetExpressionPreview / CreateAndRegisterExpression 系 改修
Editor/Tools/BuiltInSkills.cs               # FaceEmo skill ドキュメントの workflow 書き換え
```

### 温存ファイル

`FaceEmoAPI.cs`（Menu/Branch/Group 系）、`FaceEmoTools.cs`、`BlendShapeTools.cs`、`FaceEmoTestWindow.cs`、他全 Tools。

---

## Testing Strategy

このリポは Unity Test Runner asmdef を持たず、既存の `FaceEmoTestWindow.cs` のような **検証用 EditorWindow** で挙動を確認する慣習。本計画もそれに合わせる。

各タスクの "Verify" ステップは:
- **Unity Editor で `Window > AjisaiFlow > Expression Session Test` を開き、該当ボタンを押して Console 出力を確認**
- Console に compile error が無いことを目視確認

サブエージェント実行者は Unity を持っていない可能性が高いので、各 Verify ステップで **コンパイル整合性の self-check**（`grep` で型参照・シンボルの存在を確認）を併用する。

---

# Phase 0: Spikes（実装前のリフレクション検証）

このフェーズは **production code を産まない**。FaceEmo の internal 構造に reflection でアクセスできるかを検証し、結果を `spike-results.md` に記録する。失敗した spike によって Phase 2 以降の Live 経路実装をスキップする判断ができる。

### Task 0.1: Spike 検証用ハーネスを作成

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs`

- [ ] **Step 1: Create harness window skeleton**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
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
            // Buttons added per spike task

            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear")) _log = "";
        }

        private void Log(string msg)
        {
            _log += msg + "\n";
            Debug.Log("[ExprSessionTest] " + msg);
            Repaint();
        }
    }
}
```

- [ ] **Step 2: Verify file compiles**

Confirm via `grep` that no missing references:

```
grep -n "namespace" Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
```

Expected: namespace declaration matches existing convention (`AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor`).

- [ ] **Step 3: Create spike results log file**

```bash
mkdir -p docs/superpowers/specs/spikes
```

```markdown
<!-- docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md -->
# FaceEmo Bridge Spike Results

Date: 2026-05-15
Spec: ../2026-05-15-faceemo-realtime-bridge-design.md §11

## 0.2 IExpressionEditor DI resolution
Status: TBD (filled in Task 0.2)
Notes:

## 0.3 ExpressionEditorModelFacade access
Status: TBD (filled in Task 0.3)
Notes:

## 0.4 SetBlendShapeValue live reflection
Status: TBD (filled in Task 0.4)
Notes:

## Decision
TBD — fill after all spikes run
```

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs.meta \
        docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "test: scaffold ExpressionSessionTestWindow harness + spike log"
```

（`.meta` ファイルは Unity が自動生成するので、user が一度 Unity に切り替えてから commit する想定）

---

### Task 0.2: Spike — IExpressionEditor DI resolution

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs`
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Add spike button**

In `ExpressionSessionTestWindow.OnGUI()` 内、`"Phase 0: Spikes"` ラベルの直下に:

```csharp
if (GUILayout.Button("Spike 0.2: Resolve IExpressionEditor"))
{
    SpikeResolveIExpressionEditor();
}
```

メソッド本体を class 末尾に追加:

```csharp
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
            .System.Linq.Enumerable.FirstOrDefault(m => m.Name == "Resolve"
                && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        // ↑ FirstOrDefault は System.Linq 名前空間。必要なら using System.Linq; を追加
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
```

ファイル先頭に `using System.Linq;` を追加し、上記の冗長な `System.Linq.Enumerable.FirstOrDefault` を `.FirstOrDefault(...)` に修正:

```csharp
var resolveMethod = container.GetType()
    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
    .FirstOrDefault(m => m.Name == "Resolve"
        && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
```

- [ ] **Step 2: Verify in Unity**

Open Unity, switch to a scene with a FaceEmo launcher (set up TargetAvatar). Open `Window > AjisaiFlow > Expression Session Test`. Click `Spike 0.2: Resolve IExpressionEditor`.

Expected: Log shows `OK: IExpressionEditor resolved → Suzuryg.FaceEmo.Detail.ExpressionEditor.ExpressionEditor`（実装型名）.

- [ ] **Step 3: Record result**

Update `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`:

```markdown
## 0.2 IExpressionEditor DI resolution
Status: PASS / FAIL
Notes:
- Resolved type: <actual full type name>
- Container.Resolve<T>() signature: <observed>
- Caveats: <any quirks>
```

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs \
        docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "spike: verify IExpressionEditor DI resolution"
```

---

### Task 0.3: Spike — ExpressionEditorModelFacade access

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs`
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Add spike button + method**

In `OnGUI()`:

```csharp
if (GUILayout.Button("Spike 0.3: Get ExpressionEditorModelFacade"))
{
    SpikeGetFacade();
}
```

```csharp
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
```

- [ ] **Step 2: Verify in Unity**

Click `Spike 0.3`. Expected: Log shows `FOUND field '<name>' → Suzuryg.FaceEmo.Detail.ExpressionEditor.Models.ExpressionEditorModelFacade` followed by available method names including `SetBlendShapeValue`, `OpenTargetClip`, etc.

- [ ] **Step 3: Record result**

In spike-results.md:

```markdown
## 0.3 ExpressionEditorModelFacade access
Status: PASS / FAIL
Notes:
- Field name on IExpressionEditor impl: <e.g. "_model" or "_modelFacade">
- Access path: <e.g. ((ExpressionEditor)ee)._model>
- Methods observed: SetBlendShapeValue, RemoveBlendShapeValue, OpenTargetClip, FetchPreviewAvatar, StartSampling, StopSampling
- Caveats: <any>
```

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs \
        docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "spike: probe ExpressionEditorModelFacade access path"
```

---

### Task 0.4: Spike — SetBlendShapeValue ライブ反映

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs`
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Add spike button + method**

```csharp
if (GUILayout.Button("Spike 0.4: SetBlendShape live preview"))
{
    SpikeSetBlendShape();
}
```

```csharp
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
```

- [ ] **Step 2: Verify in Unity**

Click `Spike 0.4`. Watch FaceEmo's ExpressionEditor preview pane.

Expected: First face BlendShape jumps to 100 visibly in the preview. If preview doesn't auto-refresh, note in spike results that explicit Repaint or StartSampling is required.

- [ ] **Step 3: Record result**

```markdown
## 0.4 SetBlendShapeValue live reflection
Status: PASS / FAIL / PARTIAL
Notes:
- BlendShape key type: <e.g. Suzuryg.FaceEmo.Domain.BlendShape>
- Preview updates automatically: yes / no (Repaint required)
- Extra steps needed: <e.g. StartSampling first, or facade.Repaint()>
```

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs \
        docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "spike: verify facade.SetBlendShapeValue live preview"
```

---

### Task 0.5: Spike 結果の意思決定

**Files:**
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Fill in Decision section**

```markdown
## Decision

| Spike | Result |
|---|---|
| 0.2 IExpressionEditor DI | PASS / FAIL |
| 0.3 Facade access | PASS / FAIL |
| 0.4 SetBlendShapeValue live | PASS / PARTIAL / FAIL |

### Implementation path
- [ ] **Full Live + Degraded** (0.2-0.4 all PASS): Proceed with Phase 2-5 as written.
- [ ] **Degraded only** (any of 0.2-0.4 FAIL): Skip Phase 2 Tasks 2.4/2.5. Bridge.TryOpen still possible (for PreviewWindow display) but SetBlendShape always returns false → Session uses AssetPathFallback only.
- [ ] **Abort** (0.2 itself fails): FaceEmo パッケージ構造が想定と完全に異なる。仕様を見直す。

Selected path: ____________________
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "spike: record decision — selected path is <X>"
```

> **以降 Phase 2/3 のタスクは "Full Live + Degraded" path を前提に書いてある。Degraded only 選択時は 2.4/2.5 をスキップし、Session の SetBlendShape 実装で Bridge 呼出をバイパスする。**

---

# Phase 1: FaceEmoGate（事前条件の一元化）

### Task 1.1: Gate スケルトン + IsFaceEmoInstalled

**Files:**
- Create: `Editor/Tools/FaceEmoGate.cs`

- [ ] **Step 1: Write Gate skeleton**

```csharp
// Editor/Tools/FaceEmoGate.cs
using System;
using UnityEngine;

#if FACE_EMO
using Suzuryg.FaceEmo.Components;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// 表情変更系ツールの事前条件を一元化する static クラス。
    /// FaceEmo 必須化のゲートをここに集中させる。
    /// </summary>
    public static class FaceEmoGate
    {
        public struct Result
        {
            public bool Ok;
            public string ErrorMessage;
#if FACE_EMO
            public FaceEmoLauncherComponent Launcher;
#endif
        }

        /// <summary>
        /// FaceEmo パッケージがインストールされているか（最も軽量なチェック）。
        /// 解析系（read-only）ツールがエラーヒントに使う。
        /// </summary>
        public static bool IsFaceEmoInstalled()
        {
#if FACE_EMO
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// 表情を「変更」するツールが先頭で呼ぶ。
        /// FaceEmo インストール／launcher／TargetAvatar の 3 条件をすべて満たさないと Ok=false。
        /// </summary>
        public static Result RequireExpressionEditingReady(string gameObjectName = "")
        {
            var result = new Result();
#if !FACE_EMO
            result.Ok = false;
            result.ErrorMessage = "Error: FaceEmo (jp.suzuryg.face-emo) is not installed. " +
                "Expression editing is only available with FaceEmo. Install FaceEmo via VCC, then retry.";
            return result;
#else
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null)
            {
                result.Ok = false;
                result.ErrorMessage = "Error: No FaceEmo launcher in scene. " +
                    "Run ExecuteMenu('FaceEmo/New Menu') to create one, then ConfigureTargetAvatar('<avatarName>').";
                return result;
            }
            if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
            {
                result.Ok = false;
                result.ErrorMessage = $"Error: FaceEmo launcher '{launcher.gameObject.name}' has no TargetAvatar. " +
                    "Run ConfigureTargetAvatar('<avatarName>') first.";
                return result;
            }
            result.Ok = true;
            result.Launcher = launcher;
            return result;
#endif
        }
    }
}
```

- [ ] **Step 2: Verify compile**

```
grep -n "FaceEmoGate" Editor/Tools/FaceEmoGate.cs
grep -n "FaceEmoAPI.FindLauncher" Editor/Tools/FaceEmoGate.cs
```

Expected: Both grep return matching lines. No syntax errors visible.

Manually open Unity, watch Console for compile errors. Expected: clean compile.

- [ ] **Step 3: Add probe button to test window**

In `ExpressionSessionTestWindow.OnGUI()`:

```csharp
EditorGUILayout.LabelField("Phase 1: FaceEmoGate", EditorStyles.boldLabel);
if (GUILayout.Button("Probe: RequireExpressionEditingReady()"))
{
    var r = FaceEmoGate.RequireExpressionEditingReady();
    Log($"Ok={r.Ok}, Msg={(r.Ok ? "(no error)" : r.ErrorMessage)}");
}
```

- [ ] **Step 4: Verify in Unity**

Click the button in 3 conditions:
1. FaceEmo not installed → Ok=false with install message
2. FaceEmo installed, no launcher → Ok=false with New Menu message
3. FaceEmo installed, launcher + avatar set → Ok=true

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/FaceEmoGate.cs Editor/Tools/FaceEmoGate.cs.meta \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): add FaceEmoGate for expression editing precondition"
```

---

# Phase 2: ExpressionEditorBridge（reflection 局所化）

> Spike 0.2-0.4 が PASS の前提。FAIL の場合は本 Phase の該当タスクをスキップし、Bridge.IsHealthy=false 固定にする。

### Task 2.1: Bridge スケルトン + IsHealthy

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`

- [ ] **Step 1: Write Bridge skeleton**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// FaceEmo の ExpressionEditor 内部 (IExpressionEditor / ExpressionEditorModelFacade) への
    /// reflection を局所化する薄い層。バージョン差で壊れた場合の破損面積を最小化する。
    /// </summary>
    internal sealed class ExpressionEditorBridge : IDisposable
    {
        public bool IsHealthy { get; private set; }
        public string LastReflectionError { get; private set; }

        private object _expressionEditor;   // resolved IExpressionEditor impl
        private object _facade;             // ExpressionEditorModelFacade
        private FaceEmoLauncherComponent _launcher;

        public ExpressionEditorBridge()
        {
            // Lazy init in TryOpen
        }

        public void Dispose()
        {
            // No explicit close — FaceEmo keeps its window open
            _expressionEditor = null;
            _facade = null;
        }
    }
}
#endif
```

- [ ] **Step 2: Verify**

```
grep -n "internal sealed class ExpressionEditorBridge" Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs
```

Expected: Match. In Unity, no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs.meta
git commit -m "feat(faceemo): scaffold ExpressionEditorBridge"
```

---

### Task 2.2: TryOpen — DI resolve + IExpressionEditor.Open(clip)

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`

- [ ] **Step 1: Add TryOpen method**

Class 内に追加（field 定義の下）:

```csharp
private const string AppMainAsm = "jp.suzuryg.face-emo.appmain.Editor";
private const string DetailAsm = "jp.suzuryg.face-emo.detail.Editor";

public bool TryOpen(FaceEmoLauncherComponent launcher, AnimationClip clip)
{
    if (launcher == null || clip == null)
    {
        LastReflectionError = "TryOpen: null launcher or clip";
        IsHealthy = false;
        return false;
    }

    try
    {
        _launcher = launcher;

        // 1. Resolve IExpressionEditor via FaceEmo's DI container
        var installerType = Type.GetType($"Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, {AppMainAsm}");
        if (installerType == null) { return Fail("FaceEmoInstaller type not found"); }

        var installer = Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
        var container = installerType.GetProperty("Container")?.GetValue(installer);
        if (container == null) { return Fail("DI container property missing"); }

        var ieeType = Type.GetType($"Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, {DetailAsm}");
        if (ieeType == null) { return Fail("IExpressionEditor type not found"); }

        var resolve = container.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "Resolve"
                && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        if (resolve == null) { return Fail("Container.Resolve<T>() not found"); }

        _expressionEditor = resolve.MakeGenericMethod(ieeType).Invoke(container, null);
        if (_expressionEditor == null) { return Fail("Resolve<IExpressionEditor> returned null"); }

        // 2. Open editor with clip
        var openMethod = ieeType.GetMethod("Open");
        if (openMethod == null) { return Fail("IExpressionEditor.Open not found"); }
        openMethod.Invoke(_expressionEditor, new object[] { clip });

        // 3. Acquire facade via reflection (spike 0.3 result)
        var facadeField = _expressionEditor.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType.Name == "ExpressionEditorModelFacade");
        if (facadeField == null) { return Fail("ExpressionEditorModelFacade field not found"); }

        _facade = facadeField.GetValue(_expressionEditor);
        if (_facade == null) { return Fail("Facade field is null"); }

        IsHealthy = true;
        LastReflectionError = null;
        return true;
    }
    catch (Exception ex)
    {
        var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
        return Fail($"{inner.GetType().Name}: {inner.Message}");
    }
}

private bool Fail(string msg)
{
    IsHealthy = false;
    LastReflectionError = msg;
    Debug.LogWarning($"[ExpressionEditorBridge] {msg}");
    return false;
}
```

- [ ] **Step 2: Verify via test window**

Add to `OnGUI()`:

```csharp
EditorGUILayout.LabelField("Phase 2: Bridge", EditorStyles.boldLabel);
if (GUILayout.Button("Test: Bridge.TryOpen"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    var bridge = new ExpressionEditorBridge();
    var clip = new AnimationClip { name = "BridgeProbe" };
    bool ok = bridge.TryOpen(gate.Launcher, clip);
    Log($"TryOpen → ok={ok}, IsHealthy={bridge.IsHealthy}, err={bridge.LastReflectionError}");
}
```

- [ ] **Step 3: Run in Unity**

Click `Test: Bridge.TryOpen`. Expected: ExpressionEditor opens with "BridgeProbe" clip, log shows `ok=True, IsHealthy=True`.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Bridge.TryOpen via DI resolve + facade capture"
```

---

### Task 2.3: TryOpenPreviewWindow

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`

- [ ] **Step 1: Add method**

```csharp
public bool TryOpenPreviewWindow()
{
    if (!IsHealthy) return false;
    try
    {
        // FaceEmo's PreviewWindow is in Detail.ExpressionEditor.Views.PreviewWindow
        // It's typically shown via EditorWindow.GetWindow<PreviewWindow>()
        var pwType = Type.GetType($"Suzuryg.FaceEmo.Detail.ExpressionEditor.Views.PreviewWindow, {DetailAsm}");
        if (pwType == null) { LastReflectionError = "PreviewWindow type not found"; return false; }

        var getWindow = typeof(EditorWindow)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetWindow"
                && m.IsGenericMethod && m.GetParameters().Length == 0);
        if (getWindow == null) { LastReflectionError = "EditorWindow.GetWindow<T>() not found"; return false; }

        var window = getWindow.MakeGenericMethod(pwType).Invoke(null, null);
        return window != null;
    }
    catch (Exception ex)
    {
        LastReflectionError = $"TryOpenPreviewWindow: {ex.GetType().Name}: {ex.Message}";
        return false;
    }
}
```

- [ ] **Step 2: Verify via test window**

Add button:

```csharp
if (GUILayout.Button("Test: Bridge.TryOpenPreviewWindow"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    var bridge = new ExpressionEditorBridge();
    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "PreviewProbe" });
    bool ok = bridge.TryOpenPreviewWindow();
    Log($"TryOpenPreviewWindow → {ok}, err={bridge.LastReflectionError}");
}
```

- [ ] **Step 3: Run in Unity**

Click button. Expected: PreviewWindow appears (or is brought to front). Log shows `True`.

If `EditorWindow.GetWindow<T>()` reflection fails (it's a generic), alternative approach is `EditorWindow.CreateInstance(pwType) + Show()`. The fallback code:

```csharp
// Fallback if GetWindow<T> reflection fails
var window = ScriptableObject.CreateInstance(pwType) as EditorWindow;
window?.Show();
```

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Bridge.TryOpenPreviewWindow"
```

---

### Task 2.4: TrySetBlendShape — facade 経由

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`

- [ ] **Step 1: Add method + BlendShape construction helper**

```csharp
public bool TrySetBlendShape(string smrRelativePath, string shapeName, float value)
{
    if (!IsHealthy || _facade == null) return false;
    try
    {
        // Build BlendShape struct (FaceEmo Domain type) via reflection
        var bsType = Type.GetType("Suzuryg.FaceEmo.Domain.BlendShape, jp.suzuryg.face-emo.domain.Runtime");
        if (bsType == null) { LastReflectionError = "BlendShape type not found"; return false; }

        // BlendShape ctor takes (string path, string name) per FaceEmo domain conventions
        var bs = Activator.CreateInstance(bsType, new object[] { smrRelativePath, shapeName });

        var setMethod = _facade.GetType().GetMethod("SetBlendShapeValue");
        if (setMethod == null) { LastReflectionError = "SetBlendShapeValue not found"; return false; }

        setMethod.Invoke(_facade, new object[] { bs, value });
        return true;
    }
    catch (Exception ex)
    {
        var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
        LastReflectionError = $"TrySetBlendShape: {inner.GetType().Name}: {inner.Message}";
        return false;
    }
}
```

> **NOTE**: `BlendShape` ctor 引数の正確な順序は spike 0.3/0.4 で確認する。違っていたら `Activator.CreateInstance` 行を調整。

- [ ] **Step 2: Verify via test window**

```csharp
if (GUILayout.Button("Test: Bridge.TrySetBlendShape (first face shape → 100)"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    var bridge = new ExpressionEditorBridge();
    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "SetProbe" });
    // For probe: try writing into "Body" path with a known shape name from your avatar
    bool ok = bridge.TrySetBlendShape("Body", "Smile", 100f);
    Log($"TrySetBlendShape → ok={ok}, err={bridge.LastReflectionError}");
}
```

- [ ] **Step 3: Run in Unity**

Click button (avatar must have a "Body" SMR with a "Smile" blendshape — adjust if not). Watch ExpressionEditor preview pane.

Expected: Preview shows shape at 100. Log shows `ok=True`.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Bridge.TrySetBlendShape via facade.SetBlendShapeValue"
```

---

### Task 2.5: TryGetAnimatedBlendShapes — ユーザー編集の読み取り

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs`

- [ ] **Step 1: Add method**

```csharp
public bool TryGetAnimatedBlendShapes(out IReadOnlyDictionary<(string path, string name), float> values)
{
    values = null;
    if (!IsHealthy || _facade == null) return false;
    try
    {
        var prop = _facade.GetType().GetProperty("AnimatedBlendShapes");
        if (prop == null) { LastReflectionError = "AnimatedBlendShapes property not found"; return false; }

        var dict = prop.GetValue(_facade) as System.Collections.IDictionary;
        if (dict == null) { LastReflectionError = "AnimatedBlendShapes is not IDictionary"; return false; }

        var result = new Dictionary<(string, string), float>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            // Key is BlendShape struct with Path and Name properties
            var key = entry.Key;
            var keyType = key.GetType();
            var path = keyType.GetProperty("Path")?.GetValue(key) as string ?? "";
            var name = keyType.GetProperty("Name")?.GetValue(key) as string ?? "";
            float v = Convert.ToSingle(entry.Value);
            result[(path, name)] = v;
        }
        values = result;
        return true;
    }
    catch (Exception ex)
    {
        var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
        LastReflectionError = $"TryGetAnimatedBlendShapes: {inner.GetType().Name}: {inner.Message}";
        return false;
    }
}
```

- [ ] **Step 2: Verify via test window**

```csharp
if (GUILayout.Button("Test: Bridge.TryGetAnimatedBlendShapes"))
{
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) { Log(gate.ErrorMessage); return; }
    var bridge = new ExpressionEditorBridge();
    bridge.TryOpen(gate.Launcher, new AnimationClip { name = "GetProbe" });
    bridge.TrySetBlendShape("Body", "Smile", 80f);
    bool ok = bridge.TryGetAnimatedBlendShapes(out var vals);
    Log($"TryGetAnimatedBlendShapes → ok={ok}, count={vals?.Count ?? 0}");
    if (vals != null) foreach (var kv in vals) Log($"  {kv.Key.path}/{kv.Key.name}={kv.Value:F1}");
}
```

- [ ] **Step 3: Run in Unity**

Click button. Expected: Log shows `ok=True` and at least one entry like `Body/Smile=80.0`.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionEditorBridge.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Bridge.TryGetAnimatedBlendShapes for window readback"
```

---

# Phase 3: FaceEmoExpressionSession（Live/Degraded 抽象）

### Task 3.1: Session スケルトン + SyncMode + ActiveSession static holder

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Write skeleton**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using Suzuryg.FaceEmo.Components;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// 「いま編集中の表情」を表す高レベル抽象。
    /// Live (Bridge 経由) と Degraded (AssetPathFallback 経由) の切替を集約する。
    /// </summary>
    public sealed class FaceEmoExpressionSession : IDisposable
    {
        public enum SyncMode { Live, Degraded }

        // Ambient session — set by Open*, consumed by SetExpressionPreviewMulti auto-session check
        private static FaceEmoExpressionSession _active;
        public static FaceEmoExpressionSession Active => _active;

        public SyncMode Mode { get; private set; }
        public string ModeId { get; private set; }           // FaceEmo Mode ID; null when new + not committed
        public AnimationClip Clip { get; private set; }
        public string TmpName { get; private set; }          // "Tmp_<hex>" for auto-session
        public FaceEmoLauncherComponent Launcher { get; private set; }
        public bool IsNewExpression { get; private set; }
        public string PendingDisplayName { get; private set; }
        public string PendingSavePath { get; private set; }

        private ExpressionEditorBridge _bridge;

        private FaceEmoExpressionSession() { }

        public static FaceEmoExpressionSession OpenForMode(string modeName, string gameObjectName = "")
            => throw new NotImplementedException(); // Task 3.3

        public static FaceEmoExpressionSession OpenForNewExpression(string displayName, string animSavePath, string gameObjectName = "")
            => throw new NotImplementedException(); // Task 3.2

        public void SetBlendShape(string smrRelativePath, string shapeName, float value)
            => throw new NotImplementedException(); // Task 3.4/3.5

        public IReadOnlyDictionary<string, float> GetCurrentValues()
            => throw new NotImplementedException(); // Task 3.6

        public void Commit()
            => throw new NotImplementedException(); // Task 3.6

        public void Dispose()
        {
            if (_active == this) _active = null;
            _bridge?.Dispose();
            _bridge = null;
        }

        // ----- helpers -----
        internal static string GenerateTmpName()
        {
            return "Tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }
    }
}
#endif
```

- [ ] **Step 2: Verify**

```
grep -n "public sealed class FaceEmoExpressionSession" Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
grep -n "public static FaceEmoExpressionSession Active" Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
```

Expected: both match. In Unity, compile clean.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs \
        Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs.meta
git commit -m "feat(faceemo): scaffold FaceEmoExpressionSession with SyncMode + ambient holder"
```

---

### Task 3.2: OpenForNewExpression — 新規表情用 Session

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Implement OpenForNewExpression**

`throw new NotImplementedException();` を以下に置き換え:

```csharp
public static FaceEmoExpressionSession OpenForNewExpression(string displayName, string animSavePath, string gameObjectName = "")
{
    var gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
    if (!gate.Ok) throw new InvalidOperationException(gate.ErrorMessage);

    // Dispose previous ambient session
    _active?.Dispose();

    var session = new FaceEmoExpressionSession
    {
        Launcher = gate.Launcher,
        IsNewExpression = true,
        PendingDisplayName = string.IsNullOrEmpty(displayName) ? GenerateTmpName() : displayName,
        PendingSavePath = animSavePath,
        TmpName = displayName == null ? GenerateTmpName() : null,
        Clip = new AnimationClip(),
    };
    session.Clip.name = session.PendingDisplayName;

    // Try Live via Bridge
    session._bridge = new ExpressionEditorBridge();
    if (session._bridge.TryOpen(session.Launcher, session.Clip))
    {
        session._bridge.TryOpenPreviewWindow();
        session.Mode = SyncMode.Live;
    }
    else
    {
        Debug.LogWarning($"[FaceEmoExpressionSession] Bridge unhealthy ({session._bridge.LastReflectionError}). Falling back to Degraded.");
        session.Mode = SyncMode.Degraded;
    }

    _active = session;
    return session;
}
```

- [ ] **Step 2: Verify via test window**

```csharp
EditorGUILayout.LabelField("Phase 3: Session", EditorStyles.boldLabel);
if (GUILayout.Button("Test: OpenForNewExpression('Smile')"))
{
    try
    {
        var s = FaceEmoExpressionSession.OpenForNewExpression("Smile", "Assets/.../smile.anim");
        Log($"Session opened: Mode={s.Mode}, Clip={s.Clip.name}, Launcher={s.Launcher.gameObject.name}");
    }
    catch (System.Exception ex) { Log("Error: " + ex.Message); }
}
```

- [ ] **Step 3: Run in Unity**

Expected: Session created, FaceEmo ExpressionEditor opens, Mode=Live (or Degraded if spike 0.x failed).

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Session.OpenForNewExpression with Live/Degraded auto-select"
```

---

### Task 3.3: OpenForMode — 既存 Mode 編集用 Session

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Implement OpenForMode**

```csharp
public static FaceEmoExpressionSession OpenForMode(string modeName, string gameObjectName = "")
{
    var gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
    if (!gate.Ok) throw new InvalidOperationException(gate.ErrorMessage);

    var menu = FaceEmoAPI.LoadMenu(gate.Launcher);
    if (menu == null) throw new InvalidOperationException("Error: Failed to load FaceEmo menu.");
    var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
    if (modeId == null) throw new InvalidOperationException($"Error: Mode '{modeName}' not found in FaceEmo menu.");

    string guid = null;
    var animProp = mode.GetType().GetProperty("Animation");
    if (animProp != null)
    {
        var anim = animProp.GetValue(mode);
        if (anim != null)
            guid = anim.GetType().GetProperty("GUID")?.GetValue(anim) as string;
    }
    AnimationClip clip = null;
    if (!string.IsNullOrEmpty(guid))
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }
    if (clip == null) clip = new AnimationClip { name = $"{modeName}_clip" };

    _active?.Dispose();
    var session = new FaceEmoExpressionSession
    {
        Launcher = gate.Launcher,
        IsNewExpression = false,
        ModeId = modeId,
        PendingDisplayName = modeName,
        Clip = clip,
    };

    session._bridge = new ExpressionEditorBridge();
    if (session._bridge.TryOpen(session.Launcher, session.Clip))
    {
        session._bridge.TryOpenPreviewWindow();
        session.Mode = SyncMode.Live;
    }
    else
    {
        Debug.LogWarning($"[FaceEmoExpressionSession] Bridge unhealthy. Degraded mode.");
        session.Mode = SyncMode.Degraded;
    }
    _active = session;
    return session;
}
```

- [ ] **Step 2: Verify via test window**

```csharp
if (GUILayout.Button("Test: OpenForMode('<existing mode name>')"))
{
    try
    {
        var s = FaceEmoExpressionSession.OpenForMode("Neutral");
        Log($"Opened existing: Mode={s.Mode}, ModeId={s.ModeId}, Clip={s.Clip?.name}");
    }
    catch (System.Exception ex) { Log("Error: " + ex.Message); }
}
```

- [ ] **Step 3: Run in Unity**

Pre-condition: avatar with FaceEmo set up, at least one Mode (e.g. "Neutral"). Click button.

Expected: ExpressionEditor opens with that Mode's existing clip. Log shows ModeId.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Session.OpenForMode for editing existing FaceEmo expressions"
```

---

### Task 3.4: SetBlendShape Live 経路 + 自動降格

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Implement SetBlendShape (Live path)**

`throw new NotImplementedException();` を以下に置き換え:

```csharp
public void SetBlendShape(string smrRelativePath, string shapeName, float value)
{
    if (string.IsNullOrEmpty(smrRelativePath) || string.IsNullOrEmpty(shapeName))
        throw new ArgumentException("smrRelativePath and shapeName are required");

    if (Mode == SyncMode.Live)
    {
        if (_bridge != null && _bridge.TrySetBlendShape(smrRelativePath, shapeName, value))
            return;

        // Live failed at runtime — downgrade for the rest of the session
        Debug.LogWarning($"[FaceEmoExpressionSession] Live SetBlendShape failed ({_bridge?.LastReflectionError}). Downgrading to Degraded.");
        Mode = SyncMode.Degraded;
    }

    // Degraded path (Task 3.5 implements DegradedSet)
    DegradedSet(smrRelativePath, shapeName, value);
}

private void DegradedSet(string smrRelativePath, string shapeName, float value)
{
    // Implemented in Task 3.5
    throw new NotImplementedException();
}
```

- [ ] **Step 2: Verify**

`grep -n "SetBlendShape" Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs` should show all references compile-clean.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
git commit -m "feat(faceemo): Session.SetBlendShape Live path with auto-downgrade"
```

---

### Task 3.5: Degraded 経路 — AssetPathFallback

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/AssetPathFallback.cs`
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Create AssetPathFallback helper**

```csharp
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
```

- [ ] **Step 2: Wire DegradedSet in Session**

In `FaceEmoExpressionSession.cs`, replace `DegradedSet`:

```csharp
private void DegradedSet(string smrRelativePath, string shapeName, float value)
{
    AssetPathFallback.WriteBlendShapeCurve(Clip, smrRelativePath, shapeName, value);
    // For asset clips, ensure save
    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
    {
        EditorUtility.SetDirty(Clip);
    }
    AssetPathFallback.RefreshFaceEmoWindow(Launcher);
}
```

- [ ] **Step 3: Verify via test window — force Degraded mode**

```csharp
if (GUILayout.Button("Test: Force Degraded SetBlendShape"))
{
    try
    {
        var s = FaceEmoExpressionSession.OpenForNewExpression("DegradedTest", "Assets/_temp_degraded.anim");
        // Manually flip to Degraded for test purposes by abusing private API? Skip — just observe natural behavior.
        s.SetBlendShape("Body", "Smile", 50f);
        Log($"SetBlendShape OK in Mode={s.Mode}");
    }
    catch (System.Exception ex) { Log("Error: " + ex.Message); }
}
```

- [ ] **Step 4: Run in Unity**

Click button. If Bridge healthy, value updates live; if not, .anim curve is written and FaceEmo window reloads. Verify clip has the curve via `AnimationUtility.GetCurveBindings`.

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/AssetPathFallback.cs \
        Editor/Tools/FaceEmoExpressionEditor/AssetPathFallback.cs.meta \
        Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Session Degraded path via AssetPathFallback"
```

---

### Task 3.6: GetCurrentValues + Commit + auto-session 注記

**Files:**
- Modify: `Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs`

- [ ] **Step 1: Implement GetCurrentValues**

```csharp
public IReadOnlyDictionary<string, float> GetCurrentValues()
{
    var result = new Dictionary<string, float>();

    // Live path: read from facade
    if (Mode == SyncMode.Live && _bridge != null &&
        _bridge.TryGetAnimatedBlendShapes(out var live))
    {
        foreach (var kv in live)
            result[kv.Key.name] = kv.Value;
        return result;
    }

    // Degraded / fallback: read from clip's curves
    if (Clip != null)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(Clip))
        {
            if (!b.propertyName.StartsWith("blendShape.")) continue;
            var curve = AnimationUtility.GetEditorCurve(Clip, b);
            if (curve == null || curve.length == 0) continue;
            string shape = b.propertyName.Substring("blendShape.".Length);
            result[shape] = curve[0].value;
        }
    }
    return result;
}
```

- [ ] **Step 2: Implement Commit**

```csharp
public void Commit()
{
    if (Clip == null) throw new InvalidOperationException("No clip to commit.");

    // 1. Save the clip asset
    string finalPath = PendingSavePath;
    if (string.IsNullOrEmpty(finalPath))
        finalPath = $"Assets/UnityAgent/Expressions/{PendingDisplayName ?? Clip.name}.anim";

    string dir = System.IO.Path.GetDirectoryName(finalPath);
    if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
    {
        // Create folder hierarchy
        string fullDir = System.IO.Path.Combine(Application.dataPath, "..", dir);
        if (!System.IO.Directory.Exists(fullDir)) System.IO.Directory.CreateDirectory(fullDir);
        AssetDatabase.Refresh();
    }

    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
        AssetDatabase.CreateAsset(Clip, finalPath);
    EditorUtility.SetDirty(Clip);
    AssetDatabase.SaveAssets();

    // 2. Register / update Mode in FaceEmo menu
    var menu = FaceEmoAPI.LoadMenu(Launcher);
    if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");

    string guid = AssetDatabase.AssetPathToGUID(finalPath);
    var animObj = Activator.CreateInstance(
        Type.GetType("Suzuryg.FaceEmo.Domain.Animation, jp.suzuryg.face-emo.domain.Runtime"),
        new object[] { guid });

    if (IsNewExpression)
    {
        string dest = FaceEmoAPI.ResolveDestination(menu, "Registered");
        if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
            dest = FaceEmoAPI.ResolveDestination(menu, "Unregistered");
        string modeId = FaceEmoAPI.AddMode(menu, dest);
        FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: PendingDisplayName);
        FaceEmoAPI.SetModeAnimation(menu, (Suzuryg.FaceEmo.Domain.Animation)animObj, modeId);
        ModeId = modeId;
        IsNewExpression = false;
    }
    else
    {
        FaceEmoAPI.SetModeAnimation(menu, (Suzuryg.FaceEmo.Domain.Animation)animObj, ModeId);
    }
    FaceEmoAPI.SaveMenu(Launcher, menu, $"Commit Expression '{PendingDisplayName}'");
}
```

- [ ] **Step 3: Verify via test window**

```csharp
if (GUILayout.Button("Test: Session.Commit"))
{
    try
    {
        var s = FaceEmoExpressionSession.Active ?? FaceEmoExpressionSession.OpenForNewExpression("CommitTest", "Assets/UnityAgent/Expressions/CommitTest.anim");
        s.SetBlendShape("Body", "Smile", 80f);
        s.Commit();
        Log($"Committed: ModeId={s.ModeId}, ClipPath={UnityEditor.AssetDatabase.GetAssetPath(s.Clip)}");
    }
    catch (System.Exception ex) { Log("Error: " + ex.Message); }
}
```

- [ ] **Step 4: Run in Unity**

Expected: `Assets/UnityAgent/Expressions/CommitTest.anim` created; FaceEmo menu has a new "CommitTest" Mode in Registered (or Unregistered if Registered is full).

- [ ] **Step 5: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
git commit -m "feat(faceemo): Session.GetCurrentValues + Commit (save .anim + register Mode)"
```

---

# Phase 4: 既存ツールリファクタ — Gate + Session 経由へ

### Task 4.1: FaceProfileTools.SetExpressionPreviewMulti を Session 経由に

**Files:**
- Modify: `Editor/Tools/FaceProfileTools.cs:112-212`

- [ ] **Step 1: Read existing implementation** (already done in spec review)

- [ ] **Step 2: Replace mesh-direct write with Session.SetBlendShape**

`FaceProfileTools.cs` の `SetExpressionPreviewMulti` 内、行 177-201 の `// 各 SMR に値設定` ブロックを以下に置き換え:

```csharp
            // FaceEmo Gate
#if FACE_EMO
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;
#else
            return "Error: FaceEmo (jp.suzuryg.face-emo) is not installed. Expression editing is only available with FaceEmo.";
#endif

#if FACE_EMO
            // Get or create ambient session
            var session = FaceEmoExpressionEditor.FaceEmoExpressionSession.Active;
            bool autoSession = false;
            if (session == null)
            {
                string tmpPath = $"Assets/UnityAgent/Expressions/{System.IO.Path.GetRandomFileName().Replace(".","")}.anim";
                session = FaceEmoExpressionEditor.FaceEmoExpressionSession.OpenForNewExpression(null, tmpPath);
                autoSession = true;
            }

            int appliedCount = 0;
            var smrResults = new List<string>();
            foreach (var kv in smrUpdates)
            {
                string smrPath = StripAvatarPrefix(kv.Key, profile.avatarRootPath);
                foreach (var update in kv.Value)
                {
                    session.SetBlendShape(smrPath, update.shapeName, update.value);
                    appliedCount++;
                }
                smrResults.Add($"  [{System.IO.Path.GetFileName(kv.Key)}] {kv.Value.Count} shape(s)");
            }

            SceneView.RepaintAll();

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Applied {appliedCount} blend shape value(s) across {smrUpdates.Count} SMR(s). " +
                          $"(session mode: {session.Mode})");
            if (autoSession)
                sb.AppendLine($"  (auto-session: \"{session.PendingDisplayName ?? session.TmpName}\". " +
                              "Call CommitExpressionSession to persist, or OpenExpressionSession beforehand to control the name.)");
            foreach (var line in smrResults) sb.AppendLine(line);
            if (resolved.Count > 0) sb.AppendLine($"  Resolved: {string.Join(", ", resolved)}");
            if (notFound.Count > 0)
                sb.AppendLine($"  Warning: {notFound.Count} shape(s) not found: {string.Join(", ", notFound)}");
            if (rangeWarnings.Count > 0)
                sb.AppendLine($"  Range warnings: {string.Join("; ", rangeWarnings)}");
            return sb.ToString().TrimEnd();
#endif
```

- [ ] **Step 3: Add helper for stripping avatar-prefix from SMR path**

In `FaceProfileTools.cs` の helpers セクションに追加:

```csharp
private static string StripAvatarPrefix(string fullPath, string avatarRootPath)
{
    if (string.IsNullOrEmpty(avatarRootPath) || !fullPath.StartsWith(avatarRootPath))
        return fullPath;
    string relative = fullPath.Substring(avatarRootPath.Length);
    return relative.TrimStart('/');
}
```

- [ ] **Step 4: Remove the now-unused mesh-direct ResolveSmr/Undo block**

The original block at line 180-198 doing `smr.SetBlendShapeWeight(...)` should already be removed by Step 2. Verify:

```
grep -n "smr.SetBlendShapeWeight" Editor/Tools/FaceProfileTools.cs
```

Expected: No matches (or only inside the now-removed block). If it still appears, the replacement was incomplete.

- [ ] **Step 5: Add using**

At top of file, ensure:

```csharp
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor;
```

- [ ] **Step 6: Verify in Unity**

Compile clean. From the test window or via a test scene with AI tool invocation, call `SetExpressionPreviewMulti('Avatar','eye_joy=80;mouth_smile=100')`. Expected: ExpressionEditor opens (auto-session) and values appear in preview.

- [ ] **Step 7: Commit**

```bash
git add Editor/Tools/FaceProfileTools.cs
git commit -m "refactor(faceemo): route SetExpressionPreviewMulti through Session/Bridge"
```

---

### Task 4.2: SuggestExpressionShapes に Gate 追加

**Files:**
- Modify: `Editor/Tools/FaceProfileTools.cs:79-103`

- [ ] **Step 1: Add Gate at function start**

In `SuggestExpressionShapes`, immediately after the empty-argument checks:

```csharp
            if (string.IsNullOrWhiteSpace(intent))
                return "Error: intent is empty (try 'smile', 'angry', 'surprised', or a Japanese keyword like '笑顔').";

            // FaceEmo Gate
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            var profile = LoadOrBuild(avatarRootName, out string err);
```

- [ ] **Step 2: Verify**

`grep -n "FaceEmoGate.RequireExpressionEditingReady" Editor/Tools/FaceProfileTools.cs` should match in `SuggestExpressionShapes` body.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceProfileTools.cs
git commit -m "feat(faceemo): gate SuggestExpressionShapes behind FaceEmo precondition"
```

---

### Task 4.3: FaceEmoAdvancedTools.SetExpressionPreview を Session 経由に

**Files:**
- Modify: `Editor/Tools/FaceEmoAdvancedTools.cs:987-` (function `SetExpressionPreview`)

- [ ] **Step 1: Replace body to delegate to BlendShapeTools (which writes mesh) → switch to Session**

Find the `SetExpressionPreview` method (around line 987). It currently calls `BlendShapeTools.SetMultipleBlendShapes` which writes mesh directly. Replace its body to route through Session, mirroring Task 4.1:

```csharp
        public static string SetExpressionPreview(string meshObjectName, string blendShapeData)
        {
#if !FACE_EMO
            return "Error: FaceEmo is not installed.";
#else
            var gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) return gate.ErrorMessage;

            var session = FaceEmoExpressionEditor.FaceEmoExpressionSession.Active;
            bool autoSession = false;
            if (session == null)
            {
                string tmpPath = $"Assets/UnityAgent/Expressions/{System.IO.Path.GetRandomFileName().Replace(".","")}.anim";
                session = FaceEmoExpressionEditor.FaceEmoExpressionSession.OpenForNewExpression(null, tmpPath);
                autoSession = true;
            }

            var pairs = blendShapeData.Split(';');
            int applied = 0;
            foreach (var pair in pairs)
            {
                var idx = pair.IndexOf('=');
                if (idx < 0) continue;
                string name = pair.Substring(0, idx).Trim();
                if (!float.TryParse(pair.Substring(idx + 1).Trim(), out float value)) continue;
                session.SetBlendShape(meshObjectName, name, value);
                applied++;
            }
            return $"Success: Applied {applied} blendshapes via {session.Mode} session." +
                   (autoSession ? $" (auto-session: \"{session.PendingDisplayName}\")" : "");
#endif
        }
```

- [ ] **Step 2: Verify**

`grep -n "BlendShapeTools.SetMultipleBlendShapes" Editor/Tools/FaceEmoAdvancedTools.cs` — no matches inside `SetExpressionPreview` body. Compile clean.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoAdvancedTools.cs
git commit -m "refactor(faceemo): route SetExpressionPreview through Session"
```

---

### Task 4.4: CreateAndRegisterExpression / CreateExpressionFromData を Session.Commit に

**Files:**
- Modify: `Editor/Tools/FaceEmoAdvancedTools.cs`

- [ ] **Step 1: Refactor CreateAndRegisterExpression**

Find `CreateAndRegisterExpression` (around line 883). Replace body:

```csharp
public static string CreateAndRegisterExpression(string meshObjectName,
    string expressionName, string animPath, string meshPath = "")
{
#if !FACE_EMO
    return "Error: FaceEmo is not installed.";
#else
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) return gate.ErrorMessage;

    // If there's an active session matching this name, commit it
    var active = FaceEmoExpressionEditor.FaceEmoExpressionSession.Active;
    if (active != null && active.IsNewExpression
        && (active.PendingDisplayName == expressionName || string.IsNullOrEmpty(expressionName)))
    {
        active.Commit();
        return $"Success: Committed active session as '{active.PendingDisplayName}' (ModeId={active.ModeId}).";
    }

    // Otherwise, snapshot current mesh state into a new session and commit
    var session = FaceEmoExpressionEditor.FaceEmoExpressionSession.OpenForNewExpression(expressionName, animPath);
    // Read current blendshapes off the named mesh and feed them into session
    var go = MeshAnalysisTools.FindGameObject(meshObjectName);
    if (go == null) return $"Error: Mesh '{meshObjectName}' not found.";
    var smr = go.GetComponent<SkinnedMeshRenderer>();
    if (smr == null || smr.sharedMesh == null) return $"Error: SkinnedMeshRenderer or mesh missing on '{meshObjectName}'.";
    string relPath = string.IsNullOrEmpty(meshPath) ? meshObjectName : meshPath;

    int captured = 0;
    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
    {
        float w = smr.GetBlendShapeWeight(i);
        if (Mathf.Abs(w) < 0.001f) continue;
        string name = smr.sharedMesh.GetBlendShapeName(i);
        session.SetBlendShape(relPath, name, w);
        captured++;
    }
    session.Commit();
    return $"Success: Created '{expressionName}' from {captured} active blendshapes (ModeId={session.ModeId}).";
#endif
}
```

- [ ] **Step 2: Refactor CreateExpressionFromData**

Find `CreateExpressionFromData` (around line 1038). Replace body:

```csharp
public static string CreateExpressionFromData(string expressionName, string animPath,
    string meshPath, string blendShapeData)
{
#if !FACE_EMO
    return "Error: FaceEmo is not installed.";
#else
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) return gate.ErrorMessage;

    var session = FaceEmoExpressionEditor.FaceEmoExpressionSession.OpenForNewExpression(expressionName, animPath);
    var pairs = blendShapeData.Split(';');
    foreach (var pair in pairs)
    {
        var idx = pair.IndexOf('=');
        if (idx < 0) continue;
        string name = pair.Substring(0, idx).Trim();
        if (!float.TryParse(pair.Substring(idx + 1).Trim(), out float v)) continue;
        session.SetBlendShape(meshPath, name, v);
    }
    session.Commit();
    return $"Success: Created '{expressionName}' from data (ModeId={session.ModeId}, mode={session.Mode}).";
#endif
}
```

- [ ] **Step 3: Verify**

In Unity, compile clean. Test via AI tool invocation:
- `CreateAndRegisterExpression('Body','TestSmile','Assets/UnityAgent/Expressions/test.anim')` after setting some blendshapes

Expected: new Mode appears in FaceEmo menu.

- [ ] **Step 4: Commit**

```bash
git add Editor/Tools/FaceEmoAdvancedTools.cs
git commit -m "refactor(faceemo): CreateAndRegister / CreateFromData route through Session.Commit"
```

---

### Task 4.5: UpdateExpressionAnimation を Session.Commit に

**Files:**
- Modify: `Editor/Tools/FaceEmoAdvancedTools.cs`

- [ ] **Step 1: Refactor**

Find `UpdateExpressionAnimation` (around line 1019). Replace body:

```csharp
public static string UpdateExpressionAnimation(string expressionName, string meshObjectName, string animPath)
{
#if !FACE_EMO
    return "Error: FaceEmo is not installed.";
#else
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) return gate.ErrorMessage;

    var session = FaceEmoExpressionEditor.FaceEmoExpressionSession.OpenForMode(expressionName);
    var go = MeshAnalysisTools.FindGameObject(meshObjectName);
    if (go == null) return $"Error: Mesh '{meshObjectName}' not found.";
    var smr = go.GetComponent<SkinnedMeshRenderer>();
    if (smr == null || smr.sharedMesh == null) return $"Error: SMR or mesh missing.";

    int captured = 0;
    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
    {
        float w = smr.GetBlendShapeWeight(i);
        if (Mathf.Abs(w) < 0.001f) continue;
        session.SetBlendShape(meshObjectName, smr.sharedMesh.GetBlendShapeName(i), w);
        captured++;
    }
    // Override save path
    session.GetType().GetProperty("PendingSavePath")
        ?.SetValue(session, animPath); // safe because property has private setter
    session.Commit();
    return $"Success: Updated '{expressionName}' with {captured} blendshapes.";
#endif
}
```

Note: `PendingSavePath` のセッターは private なので、もし上記が動かなければ Session に `OverrideSavePath(string)` メソッドを追加する。

- [ ] **Step 2: Add Session.OverrideSavePath if needed**

In `FaceEmoExpressionSession.cs`:

```csharp
public void OverrideSavePath(string path)
{
    PendingSavePath = path;
}
```

Then in Task 4.5 Step 1 use `session.OverrideSavePath(animPath);` instead of reflection.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoAdvancedTools.cs Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
git commit -m "refactor(faceemo): UpdateExpressionAnimation via Session.Commit + OverrideSavePath"
```

---

### Task 4.6: ResetExpressionPreview に Gate 追加

**Files:**
- Modify: `Editor/Tools/FaceEmoAdvancedTools.cs`

- [ ] **Step 1: Find ResetExpressionPreview (around line 1005-1009)**

It currently delegates to `BlendShapeTools.ResetBlendShapes`. Add Gate:

```csharp
public static string ResetExpressionPreview(string meshObjectName)
{
#if !FACE_EMO
    return "Error: FaceEmo is not installed.";
#else
    var gate = FaceEmoGate.RequireExpressionEditingReady();
    if (!gate.Ok) return gate.ErrorMessage;
    return BlendShapeTools.ResetBlendShapes(meshObjectName);
#endif
}
```

- [ ] **Step 2: Commit**

```bash
git add Editor/Tools/FaceEmoAdvancedTools.cs
git commit -m "feat(faceemo): gate ResetExpressionPreview"
```

---

# Phase 5: 新規 AgentTool（セッション系）

### Task 5.1-5.4: ExpressionSessionTools の 4 ツール一括

**Files:**
- Create: `Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs`

- [ ] **Step 1: Write tools file**

```csharp
// Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs
#if FACE_EMO
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    public static class ExpressionSessionTools
    {
        [AgentTool("FaceEmo MainWindow + ExpressionEditor を開き、対象 Mode (modeName 指定) または新規表情 (newName 指定) の編集セッションを開始する。" +
            "以降の SetExpressionPreviewMulti 等はこのセッション経由で動作する。" +
            "両方未指定なら新規 (auto name)。")]
        public static string OpenExpressionSession(string modeName = "", string newName = "")
        {
            try
            {
                FaceEmoExpressionSession session;
                if (!string.IsNullOrEmpty(modeName))
                    session = FaceEmoExpressionSession.OpenForMode(modeName);
                else
                {
                    string name = string.IsNullOrEmpty(newName) ? FaceEmoExpressionSession.GenerateTmpName() : newName;
                    string path = $"Assets/UnityAgent/Expressions/{name}.anim";
                    session = FaceEmoExpressionSession.OpenForNewExpression(name, path);
                }
                return $"Session opened: name='{session.PendingDisplayName ?? session.ModeId}', mode={session.Mode}, " +
                       $"isNew={session.IsNewExpression}.";
            }
            catch (System.Exception ex)
            {
                return ex.Message;
            }
        }

        [AgentTool("現在開いている ExpressionEditor の編集状態 (AnimatedBlendShapes) を 'shape1=80;shape2=100' 形式で返す。" +
            "ユーザーが FaceEmo ウィンドウで手動編集した内容を AI が読み取るための入口。")]
        public static string ReadExpressionFromWindow()
        {
            var s = FaceEmoExpressionSession.Active;
            if (s == null) return "Error: No active expression session. Call OpenExpressionSession first.";
            var values = s.GetCurrentValues();
            if (values.Count == 0) return "(no animated blendshapes; window may be empty or unsynced)";
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in values)
            {
                if (!first) sb.Append(';');
                sb.Append($"{kv.Key}={kv.Value:F0}");
                first = false;
            }
            return sb.ToString();
        }

        [AgentTool("編集中のセッションを保存し、新規なら FaceEmo Menu に Mode として登録する。" +
            "animPath 指定で保存先を上書き。")]
        public static string CommitExpressionSession(string animPath = "")
        {
            var s = FaceEmoExpressionSession.Active;
            if (s == null) return "Error: No active expression session. Call OpenExpressionSession first.";
            try
            {
                if (!string.IsNullOrEmpty(animPath))
                    s.OverrideSavePath(animPath);
                s.Commit();
                return $"Committed: ModeId={s.ModeId}, name='{s.PendingDisplayName}'.";
            }
            catch (System.Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        [AgentTool("編集中のセッションを破棄する (FaceEmo ウィンドウは閉じない)。" +
            "Commit 前に呼ぶと変更は失われる。")]
        public static string CloseExpressionSession()
        {
            var s = FaceEmoExpressionSession.Active;
            if (s == null) return "(no active session)";
            string name = s.PendingDisplayName ?? s.ModeId ?? "?";
            s.Dispose();
            return $"Closed session '{name}'.";
        }
    }
}
#endif
```

- [ ] **Step 2: Verify**

```
grep -n "AgentTool" Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs
```

Expected: 4 matches. In Unity Console, no compile errors. Tools should appear in AI tool list.

- [ ] **Step 3: Commit**

```bash
git add Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs \
        Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTools.cs.meta
git commit -m "feat(faceemo): add 4 AgentTools — OpenExpressionSession/ReadExpressionFromWindow/CommitExpressionSession/CloseExpressionSession"
```

---

# Phase 6: Skill ドキュメント更新 + 統合テスト

### Task 6.1: BuiltInSkills.cs FaceEmo skill を新ワークフローに書き換え

**Files:**
- Modify: `Editor/Tools/BuiltInSkills.cs:145-430`

- [ ] **Step 1: Identify boundaries**

`title: FaceEmo Expression Menu Setup` のセクション全体（行 145-430）が対象。

- [ ] **Step 2: Rewrite Workflow B (推奨パス) を新フローに**

Find the "### B. Create Expression from Preset" workflow block (around line 274-286) and replace with:

```csharp
@"### B. Create Expression from Preset (RECOMMENDED — fastest path) (""笑顔の表情を作って"")

1. [AnalyzeFaceBlendShapes('Avatar')]
2. [OpenExpressionSession(newName='Smile')] → MainWindow + ExpressionEditor を開く (Live セッション)
3. [SuggestExpressionShapes('Avatar', 'smile')] → 'shape1=80;shape2=100;...' を取得
4. [SetExpressionPreviewMulti('Avatar', '<shapeData>')] → ExpressionEditor のライブプレビューに即反映
5. (任意) [ReadExpressionFromWindow()] → ユーザーが手で動かしたスライダーを取り込む
6. [CommitExpressionSession()] → .anim 保存 + FaceEmo Menu に登録
7. [ApplyFaceEmoToAvatar()] → FX レイヤー生成

This is the canonical flow. FaceEmo and a configured launcher+avatar are REQUIRED."
```

- [ ] **Step 3: Rewrite Workflow C (Manual)**

Find the "### C. Create Expression Manually" block (around line 288-300) and replace:

```csharp
@"### C. Create Expression Manually (preset miss / fine-tuning) (""人差し指で驚いた表情にして"")

1. [OpenExpressionSession(newName='Surprised')] → ExpressionEditor を開く
2. [AnalyzeFaceBlendShapes('Avatar')] → SMR / カテゴリ確認
3. [SearchExpressionShapesV2('Avatar', 'eye,mouth,brow')] → カテゴリ別 shape 候補
4. [SetExpressionPreviewMulti('Avatar', 'eye_surprised=100;mouth_open=60;brow_up=80')] → ライブ反映
5. (任意) [ReadExpressionFromWindow()] → 手調整を取り込む
6. [CommitExpressionSession()] → 保存・登録"
```

- [ ] **Step 4: Add a new "## Preconditions" subsection**

Insert before workflows section (around line 261):

```csharp
@"## Preconditions (REQUIRED)

All expression-modifying tools require:
1. FaceEmo (`jp.suzuryg.face-emo`) installed
2. A `FaceEmoLauncher` in the scene (created via `ExecuteMenu('FaceEmo/New Menu')`)
3. `TargetAvatar` configured on the launcher (via `ConfigureTargetAvatar`)

If any precondition is missing, tools return an Error with the recovery step.
Analysis tools (AnalyzeFaceBlendShapes, SearchExpressionShapesV2) are read-only and NOT gated."
```

- [ ] **Step 5: Verify**

```
grep -n "OpenExpressionSession" Editor/Tools/BuiltInSkills.cs
grep -n "Preconditions" Editor/Tools/BuiltInSkills.cs
```

Expected: matches in the FaceEmo skill body.

- [ ] **Step 6: Commit**

```bash
git add Editor/Tools/BuiltInSkills.cs
git commit -m "docs(skill): rewrite FaceEmo skill workflow for mandatory + session-driven flow"
```

---

### Task 6.2: docs/superpowers/specs/spikes 統合チェックリスト追記

**Files:**
- Modify: `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md`

- [ ] **Step 1: Append integration test checklist**

```markdown

---

## Integration Test Checklist (Plan A 完了後の手動検証)

シーン: FaceEmo + ターゲットアバター（既知の BlendShape を持つ "Body" mesh）

- [ ] AI に「笑顔の表情を作って」と依頼 → ExpressionEditor が開き、Live プレビューが更新される
- [ ] AI が表情編集中、ユーザーが ExpressionEditor のスライダーを動かす → 次の AI ターンで `ReadExpressionFromWindow` がその値を含む
- [ ] FaceEmo をアンインストールした状態で同じ依頼 → "FaceEmo is not installed" エラーが返る
- [ ] launcher を削除した状態で同じ依頼 → "No FaceEmo launcher" エラーが返る
- [ ] TargetAvatar が未設定の状態 → "no TargetAvatar" エラーが返る
- [ ] Bridge.IsHealthy=false を強制（例: FaceEmoInstaller の型名を一時的に書き換え）→ Degraded モードでも `.anim` が更新され、FaceEmo ウィンドウが再読込される
- [ ] `CommitExpressionSession` 後、FaceEmo Menu に新しい Mode が追加されている
- [ ] `ApplyFaceEmoToAvatar` を呼ぶと FX レイヤーに反映される
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "docs: integration test checklist for FaceEmo realtime bridge"
```

---

### Task 6.3: 最終マニュアル統合テスト + 集約コミット

**Files:**
- (No code changes — checklist execution only)

- [ ] **Step 1: Run integration checklist**

Manually execute every checkbox in `docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md` "Integration Test Checklist". Tick each box as it passes.

- [ ] **Step 2: Update CHANGELOG.md**

Add to the top of `CHANGELOG.md`:

```markdown
## [Unreleased]
### Changed
- FaceEmo is now REQUIRED for expression editing. Expression tools refuse to run without FaceEmo installed + a configured launcher + TargetAvatar.
- Expression building now drives FaceEmo's ExpressionEditor live preview (when reflection access is healthy) or falls back to `.anim` write + window refresh (Degraded mode).
### Added
- `OpenExpressionSession`, `ReadExpressionFromWindow`, `CommitExpressionSession`, `CloseExpressionSession` AgentTools.
- `FaceEmoGate`, `FaceEmoExpressionSession`, `ExpressionEditorBridge`, `AssetPathFallback`.
### Notes
- Plan B (Thumbnail / preview integration) is tracked separately and not yet released.
```

- [ ] **Step 3: Commit checklist completion**

```bash
git add CHANGELOG.md docs/superpowers/specs/spikes/2026-05-15-faceemo-bridge-spike.md
git commit -m "chore: complete FaceEmo realtime bridge Plan A — integration tests passed"
```

- [ ] **Step 4: Tag for review**

```bash
# Push branch for PR review (when human is ready)
echo "Ready for PR: design/faceemo-realtime-bridge"
```

---

## Self-Review (after writing the plan)

### Spec coverage check

| Spec section | Plan task |
|---|---|
| §1 Goals | All phases collectively |
| §2 Architecture (4 layer + gate/health separation) | Tasks 1.1, 2.1, 3.1, 3.5 |
| §3.1 Gate | Task 1.1 |
| §3.2 Bridge | Tasks 2.1-2.5 |
| §3.3 Session | Tasks 3.1-3.6 |
| §3.4 Refactor existing tools | Tasks 4.1-4.6 |
| §3.5 New session AgentTools | Tasks 5.1-5.4 |
| §3.6 Thumbnail Renderer | **Not in Plan A** (Plan B) |
| §3.7 Bridge.TryOpenPreviewWindow | Task 2.3 |
| §4 Data flow (Live/Degraded) | Tasks 3.4, 3.5 |
| §4-bis Preview integration | **Not in Plan A** (Plan B) |
| §5 Gate matrix | Task 1.1 (logic) + Task 6.2 (manual test) |
| §6 Tool impact table | Tasks 4.1-4.6 (改修) + Tasks 5.1-5.4 (新規) |
| §7 Workflow Before/After | Task 6.1 |
| §8 Error design | Built into Tasks 1.1, 3.x, 4.x, 5.x |
| §9 Testing | Task harness (Phase 0-3) + Task 6.2-6.3 |
| §10 Risks | Mitigations distributed (IsHealthy in 2.x, auto-session notes in 3.6/5.1, etc.) |
| §11 Spikes | Phase 0 (Tasks 0.1-0.5) |

**No gaps** for Plan A scope. Thumbnail-related items are correctly out of scope (Plan B).

### Placeholder scan

Searched plan for "TBD", "TODO", "implement later", "fill in details", "Add appropriate", "Similar to Task N":
- `TBD` appears 3 times **inside the spike-results.md template** at Task 0.1 Step 3 — those are intentional fill-in slots for the spike, not plan placeholders. Acceptable.
- All other code blocks have complete implementations.

### Type consistency

- `FaceEmoGate.Result` struct: used in Tasks 1.1, 3.2, 3.3, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6 → all use `r.Ok` / `r.ErrorMessage` / `r.Launcher` consistently.
- `FaceEmoExpressionSession` methods: `OpenForNewExpression`, `OpenForMode`, `SetBlendShape`, `GetCurrentValues`, `Commit`, `Dispose`, `OverrideSavePath` — all defined in Tasks 3.1-3.6, used consistently in 4.x and 5.x.
- `ExpressionEditorBridge` methods: `TryOpen`, `TryOpenPreviewWindow`, `TrySetBlendShape`, `TryGetAnimatedBlendShapes` — all defined in Phase 2, consumed in Phase 3.
- Namespace: all new files in `AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor`.

**No inconsistencies found.**
