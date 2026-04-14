using System;
using System.IO;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Persistence
{
    /// <summary>
    /// Domain reload を跨いで UnityAgent のチャットセッションを保存・復元する。
    /// 保存先は <c>Library/UnityAgent/CurrentSession.json</c> でプロジェクト固有 (Library 配下なので git に乗らない)。
    ///
    /// 呼び出し規約:
    /// - <see cref="Save"/> は <c>AssemblyReloadEvents.beforeAssemblyReload</c> またはユーザーが意図的に保存したい時に呼ぶ。
    /// - <see cref="Load"/> は <c>UnityAgentWindow.OnEnable</c> で呼ぶ。
    /// - <see cref="Clear"/> はユーザーが Window を閉じた時 (= reload 由来でない OnDisable) に呼ぶ。
    ///
    /// 保存ファイルの寿命:
    /// - 通常: domain reload を跨ぐ間だけ存在。reload 後の Load で読まれて使われる。
    /// - ユーザーが Window を閉じた場合: Clear で削除 → 次回開いたとき空のセッションから始まる。
    /// - Unity 自体を強制終了/クラッシュ: 残ったまま → 次回起動時に Load されてセッション復元。これは意図した挙動。
    /// </summary>
    internal static class ChatSessionPersistence
    {
        const string FileName = "CurrentSession.json";
        const string SubDir = "UnityAgent";

        /// <summary>添付ファイル (元サイズ) の永続化上限。これを超える添付は永続化スキップ。</summary>
        public const long MaxAttachmentBytes = 10 * 1024 * 1024;

        static string SessionFilePath
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Library", SubDir, FileName);
            }
        }

        public static bool Exists
        {
            get
            {
                try { return File.Exists(SessionFilePath); }
                catch { return false; }
            }
        }

        public static void Save(SessionSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                snapshot.savedAt = DateTime.UtcNow.ToString("o");

                string path = SessionFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(snapshot);

                // アトミック書き込み: 一時ファイルに書いてから move
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] ChatSessionPersistence.Save failed: {ex.Message}");
            }
        }

        public static SessionSnapshot Load()
        {
            try
            {
                string path = SessionFilePath;
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var snap = JsonUtility.FromJson<SessionSnapshot>(json);
                if (snap == null) return null;

                // ── schema migrations ──
                if (snap.version < 2)
                {
                    // Back up the v1 file before overwriting on next save.
                    try
                    {
                        string backup = path + ".v1.bak";
                        if (!File.Exists(backup)) File.Copy(path, backup);
                    }
                    catch { /* best-effort */ }

                    ChatHistoryMigrator.UpgradeV1ToV2(snap);
                    snap.version = 2;
                    Debug.Log("[UnityAgent] Migrated CurrentSession.json v1 → v2 (ToolCall entries).");
                }

                if (snap.version > 2)
                {
                    Debug.LogWarning(
                        $"[UnityAgent] CurrentSession.json version {snap.version} is newer than supported (2). " +
                        "Loading best-effort.");
                }

                return snap;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] ChatSessionPersistence.Load failed: {ex.Message}");
                return null;
            }
        }

        public static void Clear()
        {
            try
            {
                string path = SessionFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] ChatSessionPersistence.Clear failed: {ex.Message}");
            }
        }
    }
}
