// Editor/Tools/FaceEmoExpressionEditor/FaceEmoExpressionSession.cs
#if FACE_EMO
using System;
using System.Collections.Generic;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Domain;
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

        /// <summary>
        /// Session の編集モード。Open* で設定、Commit* の分岐ルートを決める。
        /// </summary>
        public enum SessionEditMode
        {
            NewMode,           // OpenForNewExpression — 新 Mode (Registered) を作る経路 (Plan A 既定)
            EditExistingClip,  // OpenForBranch — 既存 Branch の clip を Editor で直接編集
            CreateBranchClip,  // OpenForNewExpression + 後で CommitAsBranchOf で Branch に割当
        }

        /// <summary>CommitAsBranchOf 時の既存 binding に対する挙動。</summary>
        public enum OverwriteMode
        {
            Ask,            // 呼出側で AskUser、引数で具体的 mode を再指定する想定
            Overwrite,      // 新 clip 作成 + Branch 参照差替 (旧 clip は asset 残)
            EditExisting,   // 既存 clip を編集 (= OpenForBranch にフォールバック)
            Cancel,         // 操作中断
        }

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

        /// <summary>Open* 時に設定。Commit* の routing に使用。</summary>
        public SessionEditMode EditMode { get; private set; } = SessionEditMode.NewMode;

        /// <summary>OpenForBranch / CommitAsBranchOf で使用する Branch 同定情報。</summary>
        internal string TargetModeName { get; private set; }
        internal string TargetGesture { get; private set; }
        internal string TargetHand { get; private set; }
        internal string TargetSlot { get; private set; }   // "Base"/"Left"/"Right"/"Both"

        /// <summary>Open 時に snapshot した launcher 名 (R2: Mode 同時編集検出用)。</summary>
        internal string LauncherSnapshot { get; private set; }

        /// <summary>
        /// Returns the GameObject FaceEmo currently has as its preview-avatar clone for this
        /// session (via reflection through the Bridge). Null if no Bridge or chain not opened.
        /// Used by cleanup utilities to skip the active session's avatar.
        /// </summary>
        internal GameObject CurrentPreviewAvatar => _bridge?.GetCurrentPreviewAvatar();

        private FaceEmoExpressionSession() { }

        public static FaceEmoExpressionSession OpenForMode(string modeName, string gameObjectName = "", string avatarRootName = "")
        {
            FaceEmoGate.Result gate;
            if (!string.IsNullOrEmpty(gameObjectName))
                gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
            else if (!string.IsNullOrEmpty(avatarRootName))
                gate = FaceEmoGate.RequireExpressionEditingReadyForAvatar(avatarRootName);
            else
                gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) throw new InvalidOperationException(StripErrorPrefix(gate.ErrorMessage));

            var menu = FaceEmoAPI.LoadMenu(gate.Launcher);
            if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) throw new InvalidOperationException($"Mode '{modeName}' not found in FaceEmo menu.");

            string guid = mode.Animation?.GUID;
            AnimationClip clip = null;
            if (!string.IsNullOrEmpty(guid))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            if (clip == null) clip = new AnimationClip { name = $"{modeName}_clip" };

            _active?.Dispose();
            // Belt-and-braces: sweep any FaceEmo preview avatars left over from the prior
            // session's Bridge.Dispose chain (FaceEmo's internal Dispose has edge cases that
            // occasionally leave the clone alive). Safe to wipe ALL here because we just
            // disposed _active, so the next TryOpen will create a fresh avatar regardless.
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: false);
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
                Debug.LogWarning($"[FaceEmoExpressionSession] Bridge unhealthy ({session._bridge.LastReflectionError}). Falling back to Degraded.");
                session.Mode = SyncMode.Degraded;
            }
            session.EditMode = SessionEditMode.NewMode;
            session.LauncherSnapshot = gate.Launcher?.gameObject?.name;
            _active = session;
            // Same post-open sweep as OpenForNewExpression — see note there.
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: true);
            return session;
        }

        public static FaceEmoExpressionSession OpenForNewExpression(string displayName, string animSavePath, string gameObjectName = "", string avatarRootName = "")
        {
            // Prefer explicit launcher name; otherwise use avatar-aware lookup if avatarRootName given.
            FaceEmoGate.Result gate;
            if (!string.IsNullOrEmpty(gameObjectName))
                gate = FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
            else if (!string.IsNullOrEmpty(avatarRootName))
                gate = FaceEmoGate.RequireExpressionEditingReadyForAvatar(avatarRootName);
            else
                gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) throw new InvalidOperationException(StripErrorPrefix(gate.ErrorMessage));

            // Dispose previous ambient session
            _active?.Dispose();
            // Belt-and-braces: sweep any FaceEmo preview avatars left over from the prior
            // session's Bridge.Dispose chain (FaceEmo's internal Dispose has edge cases that
            // occasionally leave the clone alive). Safe to wipe ALL here because we just
            // disposed _active, so the next TryOpen will create a fresh avatar regardless.
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: false);

            bool isAuto = string.IsNullOrEmpty(displayName);
            string tmpName = isAuto ? GenerateTmpName() : null;
            var session = new FaceEmoExpressionSession
            {
                Launcher = gate.Launcher,
                IsNewExpression = true,
                PendingDisplayName = isAuto ? tmpName : displayName,
                PendingSavePath = animSavePath,
                TmpName = tmpName,
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

            session.EditMode = SessionEditMode.NewMode;
            session.LauncherSnapshot = gate.Launcher?.gameObject?.name;
            _active = session;
            // Post-open sweep: FaceEmo's TryOpen path occasionally leaves an extra hidden
            // avatar clone behind (suspected: PropertyEditorWindow's OnOpenClipRequested
            // subscription re-firing Open in the same tick). Preserve the active session's
            // clone (reference) and wipe any extras. Keeps the FaceEmo PreviewWindow clean.
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: true);
            return session;
        }

        /// <summary>
        /// 既存 Branch の指定 slot の clip を Editor で開く (EditExistingClip モード)。
        /// Plan C 用。Branch 既存前提、無ければ throw。
        /// </summary>
        /// <param name="launcherName">target launcher 名 (Mode 同時編集検出用)</param>
        /// <param name="modeName">target Mode 表示名</param>
        /// <param name="gesture">"HandOpen" 等 (FaceEmoAPI.ParseGesture 形式)</param>
        /// <param name="hand">"Either" 等 (FaceEmoAPI.ParseHand 形式)</param>
        /// <param name="slot">"Base"/"Left"/"Right"/"Both"</param>
        /// <param name="avatarRootName">avatar 同定用 (FaceEmoGate 経由)</param>
        public static FaceEmoExpressionSession OpenForBranch(
            string launcherName, string modeName,
            string gesture, string hand, string slot,
            string avatarRootName)
        {
            FaceEmoGate.Result gate;
            if (!string.IsNullOrEmpty(avatarRootName))
                gate = FaceEmoGate.RequireExpressionEditingReadyForAvatar(avatarRootName);
            else if (!string.IsNullOrEmpty(launcherName))
                gate = FaceEmoGate.RequireExpressionEditingReady(launcherName);
            else
                gate = FaceEmoGate.RequireExpressionEditingReady();
            if (!gate.Ok) throw new InvalidOperationException(StripErrorPrefix(gate.ErrorMessage));

            var menu = FaceEmoAPI.LoadMenu(gate.Launcher);
            if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");
            var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
            if (modeId == null) throw new InvalidOperationException($"Mode '{modeName}' not found in FaceEmo menu.");

            var hg = FaceEmoAPI.ParseGesture(gesture);
            var hd = FaceEmoAPI.ParseHand(hand);
            var slotType = FaceEmoAPI.ParseBranchSlot(slot) ?? BranchAnimationType.Base;

            int branchIndex = -1;
            for (int i = 0; i < (mode.Branches?.Count ?? 0); i++)
            {
                var b = mode.Branches[i];
                if (b.Conditions == null) continue;
                bool match = System.Linq.Enumerable.Any(b.Conditions, c => c.Hand == hd && c.HandGesture == hg);
                if (match) { branchIndex = i; break; }
            }
            if (branchIndex < 0)
                throw new InvalidOperationException($"Branch ({hand}, {gesture}) not found in Mode '{modeName}'.");

            var branch = mode.Branches[branchIndex];
            Suzuryg.FaceEmo.Domain.Animation anim;
            switch (slotType)
            {
                case BranchAnimationType.Left:  anim = branch.LeftHandAnimation;  break;
                case BranchAnimationType.Right: anim = branch.RightHandAnimation; break;
                case BranchAnimationType.Both:  anim = branch.BothHandsAnimation; break;
                default:                        anim = branch.BaseAnimation;      break;
            }

            AnimationClip clip = null;
            if (anim != null && !string.IsNullOrEmpty(anim.GUID))
            {
                var path = AssetDatabase.GUIDToAssetPath(anim.GUID);
                if (!string.IsNullOrEmpty(path))
                    clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            if (clip == null)
                throw new InvalidOperationException(
                    $"Branch ({hand}, {gesture}) slot '{slot}' has no animation clip.");

            _active?.Dispose();
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: false);
            var session = new FaceEmoExpressionSession
            {
                Launcher = gate.Launcher,
                IsNewExpression = false,
                ModeId = modeId,
                Clip = clip,
                TmpName = null,
                Mode = SyncMode.Live,
                EditMode = SessionEditMode.EditExistingClip,
                LauncherSnapshot = gate.Launcher?.gameObject?.name,
                TargetModeName = modeName,
                TargetGesture = gesture,
                TargetHand = hand,
                TargetSlot = slot,
            };
            session._bridge = new ExpressionEditorBridge();
            if (session._bridge.TryOpen(gate.Launcher, clip))
            {
                session._bridge.TryOpenPreviewWindow();
                session.Mode = SyncMode.Live;
            }
            else
            {
                Debug.LogWarning($"[FaceEmoExpressionSession] Bridge unhealthy ({session._bridge.LastReflectionError}). Falling back to Degraded.");
                session._bridge.Dispose();
                session._bridge = null;
                session.Mode = SyncMode.Degraded;
            }
            ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserveActiveSession: true);
            _active = session;
            return session;
        }

        public sealed class CommitResult
        {
            public bool Ok { get; set; }
            public string ErrorMessage { get; set; }
            public string FinalClipPath { get; set; }
            public int BranchIndex { get; set; }
            public string DestinationDescription { get; set; }  // 例: "表情パターン1 / (Either, HandOpen) / BaseAnimation"
        }

        /// <summary>
        /// 現在の Editor 値を新 clip に保存し、指定 Mode の指定 Branch (新規 OR 既存) の指定 slot に割当てる。
        /// Spec Sec 7.3 の atomic 6 step を実装。失敗時は各 step に応じて rollback。
        /// </summary>
        public CommitResult CommitAsBranchOf(
            string modeName, string gesture, string hand, string slot,
            OverwriteMode overwriteMode = OverwriteMode.Overwrite)
        {
            string clipPath = null;
            int addedBranchIndex = -1;
            bool didAddBranch = false;
            var menu = FaceEmoAPI.LoadMenu(Launcher);

            try
            {
                if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");
                var (modeId, mode) = FaceEmoAPI.FindExpression(menu, modeName);
                if (modeId == null) throw new InvalidOperationException($"Mode '{modeName}' not found in FaceEmo menu.");

                var hg = FaceEmoAPI.ParseGesture(gesture);
                var hd = FaceEmoAPI.ParseHand(hand);
                var slotType = FaceEmoAPI.ParseBranchSlot(slot) ?? BranchAnimationType.Base;

                Undo.SetCurrentGroupName($"Plan C: expression to ({hand}, {gesture}) on {modeName}");
                int undoGroup = Undo.GetCurrentGroup();

                // ① 現在 Editor 値 → 新 clip ファイル
                var values = GetCurrentValuesWithPaths();
                string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string baseName = $"expr_{ts}";
                string dir = "Assets/Generated/UnityAgent/FaceEmoPlanC";
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string fullDir = System.IO.Path.Combine(Application.dataPath, "..", dir);
                    if (!System.IO.Directory.Exists(fullDir))
                        System.IO.Directory.CreateDirectory(fullDir);
                    AssetDatabase.Refresh();
                }
                clipPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}.anim");
                var newClip = new AnimationClip { name = baseName };
                ApplyValuesToClip(newClip, values);
                AssetDatabase.CreateAsset(newClip, clipPath);
                AssetDatabase.SaveAssetIfDirty(newClip);

                // ② 既存 Branch 検索
                int branchIdx = FindBranchByCondition(mode, hd, hg);
                bool isNew = (branchIdx < 0);
                if (isNew)
                {
                    // ③ AddBranch (新規)
                    var conditions = new List<Condition>
                    {
                        new Condition(hd, hg, ComparisonOperator.Equals),
                    };
                    branchIdx = FaceEmoAPI.AddBranch(menu, modeId, conditions);
                    addedBranchIndex = branchIdx;
                    didAddBranch = true;
                }
                else if (overwriteMode == OverwriteMode.Cancel)
                {
                    throw new InvalidOperationException("Existing branch present and overwriteMode=Cancel.");
                }

                // ④ slot 割当
                var faceEmoAnim = new Suzuryg.FaceEmo.Domain.Animation(AssetDatabase.AssetPathToGUID(clipPath));
                FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIdx, slotType, faceEmoAnim);

                // ⑤ menu save
                FaceEmoAPI.SaveMenu(Launcher, menu, $"Plan C: expression to ({hand}, {gesture}) on {modeName}");
                AssetDatabase.SaveAssets();

                // ⑥ MainView refresh (失敗しても warn のみ)
                try { FaceEmoAPI.RefreshWindowIfOpen(Launcher); }
                catch (System.Exception refEx)
                {
                    Debug.LogWarning($"[PlanC] MainView refresh non-fatal: {refEx.Message}");
                }

                Undo.CollapseUndoOperations(undoGroup);

                return new CommitResult
                {
                    Ok = true,
                    FinalClipPath = clipPath,
                    BranchIndex = branchIdx,
                    DestinationDescription = $"{modeName} / ({hand}, {gesture}) / {slot}",
                };
            }
            catch (System.Exception ex)
            {
                // rollback: ④ で throw なら ③ をロールバック、clip は削除
                try
                {
                    if (didAddBranch && menu != null && addedBranchIndex >= 0)
                    {
                        var (modeId2, _) = FaceEmoAPI.FindExpression(menu, modeName);
                        if (modeId2 != null) FaceEmoAPI.RemoveBranch(menu, modeId2, addedBranchIndex);
                    }
                    if (!string.IsNullOrEmpty(clipPath) && AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
                        AssetDatabase.DeleteAsset(clipPath);
                }
                catch (System.Exception rex)
                {
                    Debug.LogWarning($"[PlanC] Rollback partial failure: {rex.Message}");
                }
                return new CommitResult { Ok = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>既存 clip を上書き保存 (EditExistingClip モード時)。Branch 参照は変えない。</summary>
        public CommitResult CommitInPlace()
        {
            if (EditMode != SessionEditMode.EditExistingClip)
                return new CommitResult { Ok = false, ErrorMessage = "CommitInPlace requires EditExistingClip mode." };
            if (Clip == null)
                return new CommitResult { Ok = false, ErrorMessage = "Session has no clip reference." };
            try
            {
                Undo.RegisterCompleteObjectUndo(Clip, "Plan C: in-place clip edit");
                var values = GetCurrentValuesWithPaths();
                ApplyValuesToClip(Clip, values);
                EditorUtility.SetDirty(Clip);
                AssetDatabase.SaveAssetIfDirty(Clip);
                return new CommitResult
                {
                    Ok = true,
                    FinalClipPath = AssetDatabase.GetAssetPath(Clip),
                    DestinationDescription = $"{TargetModeName} / ({TargetHand}, {TargetGesture}) / {TargetSlot} (in-place)",
                };
            }
            catch (System.Exception ex)
            {
                return new CommitResult { Ok = false, ErrorMessage = ex.Message };
            }
        }

        // ───────── helpers ─────────

        private static int FindBranchByCondition(
            IMode mode,
            Hand hand,
            HandGesture gesture)
        {
            if (mode?.Branches == null) return -1;
            for (int i = 0; i < mode.Branches.Count; i++)
            {
                var b = mode.Branches[i];
                if (b.Conditions == null) continue;
                if (System.Linq.Enumerable.Any(b.Conditions, c => c.Hand == hand && c.HandGesture == gesture))
                    return i;
            }
            return -1;
        }

        private static void ApplyValuesToClip(AnimationClip clip,
            System.Collections.Generic.IReadOnlyDictionary<(string path, string name), float> values)
        {
            clip.ClearCurves();
            foreach (var kv in values)
            {
                var binding = new EditorCurveBinding
                {
                    path = kv.Key.path,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = $"blendShape.{kv.Key.name}",
                };
                var curve = AnimationCurve.Linear(0f, kv.Value, 1f / 60f, kv.Value);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
        }

        public void SetBlendShape(string smrRelativePath, string shapeName, float value)
        {
            if (smrRelativePath == null) throw new ArgumentNullException(nameof(smrRelativePath));
            if (string.IsNullOrEmpty(shapeName)) throw new ArgumentException("shapeName is required", nameof(shapeName));
            // smrRelativePath of "" means "the animator's root GameObject" — accept it.

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
            AssetPathFallback.WriteBlendShapeCurve(Clip, smrRelativePath, shapeName, value);
            // Mark serialized-asset clips dirty so the next AssetDatabase.SaveAssets persists the curve write.
            // In-memory clips become dirty automatically via SetEditorCurve.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
                EditorUtility.SetDirty(Clip);
            AssetPathFallback.RefreshFaceEmoWindow(Launcher);
        }

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

        /// <summary>
        /// Plan C commit 用: blendshape (path, name) → value を返す path-preserving 版。
        /// 既存 GetCurrentValues は shape 名のみで commit には使えない。
        /// </summary>
        internal IReadOnlyDictionary<(string path, string name), float> GetCurrentValuesWithPaths()
        {
            var result = new Dictionary<(string, string), float>();
            if (Mode == SyncMode.Live && _bridge != null
                && _bridge.TryGetAnimatedBlendShapes(out var live))
            {
                foreach (var kv in live)
                    result[(kv.Key.path, kv.Key.name)] = kv.Value;
                return result;
            }
            if (Clip != null)
            {
                foreach (var b in AnimationUtility.GetCurveBindings(Clip))
                {
                    if (!b.propertyName.StartsWith("blendShape.")) continue;
                    var curve = AnimationUtility.GetEditorCurve(Clip, b);
                    if (curve == null || curve.length == 0) continue;
                    string shape = b.propertyName.Substring("blendShape.".Length);
                    result[(b.path, shape)] = curve[0].value;
                }
            }
            return result;
        }

        public void Commit()
        {
            if (Clip == null) throw new InvalidOperationException("No clip to commit.");

            // 1. Save the clip asset
            string finalPath = PendingSavePath;
            if (string.IsNullOrEmpty(finalPath))
                finalPath = $"Assets/UnityAgent/Expressions/{PendingDisplayName ?? Clip.name}.anim";
            finalPath = finalPath.Replace('\\', '/');

            string dir = System.IO.Path.GetDirectoryName(finalPath)?.Replace('\\', '/');
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

            // Re-derive the path from the clip itself so the GUID always points to the live asset,
            // whether we just created it at finalPath or it already existed at a different path
            // (OpenForMode → UpdateExpressionAnimation flow).
            string clipPath = AssetDatabase.GetAssetPath(Clip);
            if (string.IsNullOrEmpty(clipPath))
                throw new InvalidOperationException("Failed to obtain asset path for clip after save.");
            string guid = AssetDatabase.AssetPathToGUID(clipPath);

            // 2. Register / update Mode in FaceEmo menu
            var menu = FaceEmoAPI.LoadMenu(Launcher);
            if (menu == null) throw new InvalidOperationException("Failed to load FaceEmo menu.");

            var anim = new Suzuryg.FaceEmo.Domain.Animation(guid);

            if (IsNewExpression)
            {
                string registeredDest = FaceEmoAPI.ResolveDestination(menu, "Registered");
                string dest;
                if (FaceEmoAPI.CanAddMenuItemTo(menu, registeredDest))
                {
                    dest = registeredDest;
                    LastCommitDestination = "Registered";
                }
                else
                {
                    dest = FaceEmoAPI.ResolveDestination(menu, "Unregistered");
                    LastCommitDestination = "Unregistered";
                }
                string modeId = FaceEmoAPI.AddMode(menu, dest);
                FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: PendingDisplayName);
                FaceEmoAPI.SetModeAnimation(menu, anim, modeId);
                ModeId = modeId;
                IsNewExpression = false;
            }
            else
            {
                FaceEmoAPI.SetModeAnimation(menu, anim, ModeId);
                LastCommitDestination = "Existing";
            }
            FaceEmoAPI.SaveMenu(Launcher, menu, $"Commit Expression '{PendingDisplayName}'");
        }

        /// <summary>
        /// "Registered" | "Unregistered" | "Existing" — set by Commit. Useful for callers
        /// to detect the FaceEmo 7-item cap fallback and surface a Note to the user.
        /// Null until first Commit.
        /// </summary>
        public string LastCommitDestination { get; private set; }

        /// <summary>
        /// If this session's last Commit hit the FaceEmo Registered cap and fell back to
        /// Unregistered, returns a multi-sentence Note string explaining how to recover.
        /// Returns empty string otherwise. Designed to be appended to AgentTool success
        /// messages so the AI/user sees the cap state and actionable next steps.
        /// </summary>
        public string GetUnregisteredFallbackNote()
        {
            if (LastCommitDestination != "Unregistered") return string.Empty;
            string name = PendingDisplayName ?? ModeId ?? "<name>";
            return " Note: Registered list is at FaceEmo's 7-item cap, so this Mode landed in Unregistered" +
                   " (still gesture-assignable but NOT visible in the VRChat radial Expression Menu)." +
                   " To make it appear in the radial menu, either" +
                   $" (a) RemoveExpression on an existing Registered item to free a slot, then MoveExpressionItem('{name}', 'Registered'), or" +
                   $" (b) CreateExpressionGroup('<groupName>', 'Registered') and MoveExpressionItem('{name}', '<groupName>') to consolidate (a Group counts as 1 slot).";
        }

        public void OverrideSavePath(string path)
        {
            PendingSavePath = path;
        }

        public void Dispose()
        {
            if (_active == this) _active = null;
            _bridge?.Dispose();
            _bridge = null;

            // Destroy uncommitted in-memory clip to avoid editor-object leak
            if (Clip != null && IsNewExpression && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Clip)))
            {
                UnityEngine.Object.DestroyImmediate(Clip);
            }
            Clip = null;
        }

        // ----- helpers -----
        internal static string GenerateTmpName()
        {
            return "Tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }

        // Strip the gate-supplied "Error: " prefix so tool-boundary catch sites (which prepend
        // "Error: " unconditionally) don't produce a "Error: Error: ..." double prefix.
        private static string StripErrorPrefix(string s)
            => s != null && s.StartsWith("Error: ") ? s.Substring("Error: ".Length) : s;
    }
}
#endif
