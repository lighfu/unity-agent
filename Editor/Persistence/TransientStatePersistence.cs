using System;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor;

namespace AjisaiFlow.UnityAgent.Editor.Persistence
{
    /// <summary>
    /// Domain reload を跨いで <see cref="UserChoiceState"/> / <see cref="ToolConfirmState"/> /
    /// <see cref="BatchToolConfirmState"/> といった static フィールド群を保持する。
    ///
    /// 永続化先は Unity 標準の <see cref="SessionState"/>:
    /// - reload は生き残るが Unity 終了で消える
    /// - チャット履歴と違いユーザー選択待ちは "次回起動時に復元したい" 性質ではないので、
    ///   Library/UnityAgent/CurrentSession.json ではなくこちらが正解
    ///
    /// 動作:
    /// 1. <c>AssemblyReloadEvents.beforeAssemblyReload</c> で各 state を JSON にして SessionState へ保存
    /// 2. <c>[InitializeOnLoad]</c> で AppDomain 立ち上げ時に SessionState から読み戻し
    /// 3. 復元後はキーを削除 (二重復元防止 + 古いペイロードの残留を防ぐ)
    /// </summary>
    [InitializeOnLoad]
    internal static class TransientStatePersistence
    {
        const string K_UserChoice = "UnityAgent.Transient.UserChoice";
        const string K_ToolConfirm = "UnityAgent.Transient.ToolConfirm";
        const string K_BatchConfirm = "UnityAgent.Transient.BatchConfirm";

        static TransientStatePersistence()
        {
            // AppDomain 起動直後 (= reload 後) に呼ばれる。
            // 直前の reload で保存されたペイロードがあれば各 state に書き戻す。
            try { RestoreAll(); }
            catch (Exception ex) { Debug.LogWarning($"[UnityAgent] TransientStatePersistence.RestoreAll failed: {ex.Message}"); }

            AssemblyReloadEvents.beforeAssemblyReload += SaveAll;
        }

        // ───────────────────────────────────────────────
        //  Save (called on beforeAssemblyReload)
        // ───────────────────────────────────────────────

        static void SaveAll()
        {
            try
            {
                SaveUserChoice();
                SaveToolConfirm();
                SaveBatchConfirm();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] TransientStatePersistence.SaveAll failed: {ex.Message}");
            }
        }

        static void SaveUserChoice()
        {
            if (!UserChoiceState.IsPending && UserChoiceState.SelectedIndex < 0)
            {
                SessionState.EraseString(K_UserChoice);
                return;
            }
            var snap = new UserChoiceSnapshot
            {
                isPending = UserChoiceState.IsPending,
                question = UserChoiceState.Question ?? "",
                options = UserChoiceState.Options ?? Array.Empty<string>(),
                importance = UserChoiceState.Importance ?? "info",
                selectedIndex = UserChoiceState.SelectedIndex,
                customText = UserChoiceState.CustomText ?? "",
                hasCustomText = UserChoiceState.CustomText != null,
            };
            SessionState.SetString(K_UserChoice, JsonUtility.ToJson(snap));
        }

        static void SaveToolConfirm()
        {
            if (!ToolConfirmState.IsPending && ToolConfirmState.SelectedIndex < 0 && !ToolConfirmState.SessionSkipAll)
            {
                SessionState.EraseString(K_ToolConfirm);
                return;
            }
            var snap = new ToolConfirmSnapshot
            {
                isPending = ToolConfirmState.IsPending,
                toolName = ToolConfirmState.ToolName ?? "",
                description = ToolConfirmState.Description ?? "",
                parameters = ToolConfirmState.Parameters ?? "",
                selectedIndex = ToolConfirmState.SelectedIndex,
                sessionSkipAll = ToolConfirmState.SessionSkipAll,
            };
            SessionState.SetString(K_ToolConfirm, JsonUtility.ToJson(snap));
        }

        static void SaveBatchConfirm()
        {
            if (!BatchToolConfirmState.IsPending && !BatchToolConfirmState.IsResolved)
            {
                SessionState.EraseString(K_BatchConfirm);
                return;
            }
            var items = BatchToolConfirmState.Items;
            var itemSnaps = items != null
                ? new BatchToolItemSnapshot[items.Count]
                : Array.Empty<BatchToolItemSnapshot>();
            for (int i = 0; items != null && i < items.Count; i++)
            {
                itemSnaps[i] = new BatchToolItemSnapshot
                {
                    toolName = items[i].toolName ?? "",
                    description = items[i].description ?? "",
                    parameters = items[i].parameters ?? "",
                    approved = items[i].approved,
                };
            }

            string[] approvedTools;
            if (BatchToolConfirmState.ApprovedTools != null)
            {
                approvedTools = new string[BatchToolConfirmState.ApprovedTools.Count];
                int idx = 0;
                foreach (var t in BatchToolConfirmState.ApprovedTools)
                    approvedTools[idx++] = t;
            }
            else
            {
                approvedTools = Array.Empty<string>();
            }

            var snap = new BatchConfirmSnapshot
            {
                isPending = BatchToolConfirmState.IsPending,
                isResolved = BatchToolConfirmState.IsResolved,
                items = itemSnaps,
                approvedTools = approvedTools,
                hasApprovedTools = BatchToolConfirmState.ApprovedTools != null,
            };
            SessionState.SetString(K_BatchConfirm, JsonUtility.ToJson(snap));
        }

        // ───────────────────────────────────────────────
        //  Restore (called from static ctor on AppDomain start)
        // ───────────────────────────────────────────────

        static void RestoreAll()
        {
            RestoreUserChoice();
            RestoreToolConfirm();
            RestoreBatchConfirm();
        }

        static void RestoreUserChoice()
        {
            string json = SessionState.GetString(K_UserChoice, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var snap = JsonUtility.FromJson<UserChoiceSnapshot>(json);
                if (snap == null) return;

                UserChoiceState.IsPending = snap.isPending;
                UserChoiceState.Question = snap.question;
                UserChoiceState.Options = (snap.options != null && snap.options.Length > 0) ? snap.options : null;
                UserChoiceState.Importance = snap.importance;
                UserChoiceState.SelectedIndex = snap.selectedIndex;
                UserChoiceState.CustomText = snap.hasCustomText ? snap.customText : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] RestoreUserChoice failed: {ex.Message}");
            }
            finally
            {
                SessionState.EraseString(K_UserChoice);
            }
        }

        static void RestoreToolConfirm()
        {
            string json = SessionState.GetString(K_ToolConfirm, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var snap = JsonUtility.FromJson<ToolConfirmSnapshot>(json);
                if (snap == null) return;

                ToolConfirmState.IsPending = snap.isPending;
                ToolConfirmState.ToolName = snap.toolName;
                ToolConfirmState.Description = snap.description;
                ToolConfirmState.Parameters = snap.parameters;
                ToolConfirmState.SelectedIndex = snap.selectedIndex;
                ToolConfirmState.SessionSkipAll = snap.sessionSkipAll;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] RestoreToolConfirm failed: {ex.Message}");
            }
            finally
            {
                SessionState.EraseString(K_ToolConfirm);
            }
        }

        static void RestoreBatchConfirm()
        {
            string json = SessionState.GetString(K_BatchConfirm, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var snap = JsonUtility.FromJson<BatchConfirmSnapshot>(json);
                if (snap == null) return;

                if (snap.items != null && snap.items.Length > 0)
                {
                    var list = new System.Collections.Generic.List<BatchToolItem>();
                    foreach (var it in snap.items)
                    {
                        if (it == null) continue;
                        list.Add(new BatchToolItem
                        {
                            toolName = it.toolName,
                            description = it.description,
                            parameters = it.parameters,
                            approved = it.approved,
                        });
                    }
                    BatchToolConfirmState.Items = list;
                }
                else
                {
                    BatchToolConfirmState.Items = null;
                }

                BatchToolConfirmState.IsPending = snap.isPending;
                BatchToolConfirmState.IsResolved = snap.isResolved;

                if (snap.hasApprovedTools)
                {
                    var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    if (snap.approvedTools != null)
                        foreach (var t in snap.approvedTools) set.Add(t);
                    BatchToolConfirmState.ApprovedTools = set;
                }
                else
                {
                    BatchToolConfirmState.ApprovedTools = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] RestoreBatchConfirm failed: {ex.Message}");
            }
            finally
            {
                SessionState.EraseString(K_BatchConfirm);
            }
        }

        // ───────────────────────────────────────────────
        //  Snapshot DTOs (JsonUtility)
        // ───────────────────────────────────────────────

        [Serializable]
        private class UserChoiceSnapshot
        {
            public bool isPending;
            public string question = "";
            public string[] options = Array.Empty<string>();
            public string importance = "info";
            public int selectedIndex = -1;
            public string customText = "";
            public bool hasCustomText;
        }

        [Serializable]
        private class ToolConfirmSnapshot
        {
            public bool isPending;
            public string toolName = "";
            public string description = "";
            public string parameters = "";
            public int selectedIndex = -1;
            public bool sessionSkipAll;
        }

        [Serializable]
        private class BatchConfirmSnapshot
        {
            public bool isPending;
            public bool isResolved;
            public BatchToolItemSnapshot[] items = Array.Empty<BatchToolItemSnapshot>();
            public string[] approvedTools = Array.Empty<string>();
            public bool hasApprovedTools;
        }
    }
}
