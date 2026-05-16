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
            "両方未指定なら新規 (auto name)。" +
            "avatarRootName を指定すると、そのアバターをターゲットとする launcher を選んでセッションが作られる " +
            "(Workflow B の正規形 — Milfy_Another の表情を作りたいなら avatarRootName='Milfy_Another' を必ず渡す)。" +
            "未指定だと scene 内の最初の configured launcher が選ばれ、後続の SetExpressionPreviewMulti が別 avatar の launcher の menu に commit してしまう可能性がある。")]
        public static string OpenExpressionSession(string modeName = "", string newName = "", string avatarRootName = "")
        {
            try
            {
                FaceEmoExpressionSession session;
                if (!string.IsNullOrEmpty(modeName))
                    session = FaceEmoExpressionSession.OpenForMode(modeName, gameObjectName: "", avatarRootName: avatarRootName);
                else
                {
                    string name = string.IsNullOrEmpty(newName) ? FaceEmoExpressionSession.GenerateTmpName() : newName;
                    string path = $"Assets/UnityAgent/Expressions/{name}.anim";
                    session = FaceEmoExpressionSession.OpenForNewExpression(name, path, gameObjectName: "", avatarRootName: avatarRootName);
                }
                return $"Session opened: name='{session.PendingDisplayName ?? session.ModeId}', mode={session.Mode}, " +
                       $"isNew={session.IsNewExpression}, launcher='{session.Launcher?.gameObject.name}'" +
                       $" (TargetAvatar='{session.Launcher?.AV3Setting?.TargetAvatar?.gameObject.name ?? "?"}').";
            }
            catch (System.Exception ex)
            {
                return $"Error: {ex.Message}";
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
            "animPath 指定で保存先を上書き。" +
            "Registered が FaceEmo の 7 個上限に達している場合は Unregistered に fallback し、その旨を Note で返す。")]
        public static string CommitExpressionSession(string animPath = "")
        {
            var s = FaceEmoExpressionSession.Active;
            if (s == null) return "Error: No active expression session. Call OpenExpressionSession first.";
            try
            {
                if (!string.IsNullOrEmpty(animPath))
                    s.OverrideSavePath(animPath);
                s.Commit();
                return $"Committed: ModeId={s.ModeId}, name='{s.PendingDisplayName}', destination={s.LastCommitDestination}."
                       + s.GetUnregisteredFallbackNote();
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

        [AgentTool("FaceEmo の preview avatar の残骸を破棄する。FaceEmo はセッションを開くたびに HideAndDontSave 付きで " +
            "アバターのクローンを (100,100,100) にインスタンス化する。Plan A の Bridge が適切に Dispose されなかった " +
            "（domain reload 中断、クラッシュ等）場合、これらが累積して Scene/Game view に重なって見える。" +
            "FaceEmo PreviewWindow に複数アバターが見えるときに呼ぶ。" +
            "アクティブセッションの preview は preserveActive=true (デフォルト) で残す。" +
            "preserveActive='false' で全て破棄 (アクティブセッションの編集を壊す)。")]
        public static string CleanupFaceEmoPreviewAvatars(string preserveActive = "true")
        {
            bool preserve = !string.Equals(preserveActive, "false", System.StringComparison.OrdinalIgnoreCase);
            int n = ExpressionEditorBridge.CleanupOrphanPreviewAvatars(preserve);
            return n == 0
                ? "No orphan FaceEmo preview avatars found."
                : $"Destroyed {n} orphan FaceEmo preview avatar(s)" + (preserve ? " (active session preserved)." : ".");
        }
    }
}
#endif
