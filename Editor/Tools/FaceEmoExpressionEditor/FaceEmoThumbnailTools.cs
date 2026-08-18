// Editor/Tools/FaceEmoExpressionEditor/FaceEmoThumbnailTools.cs
#if FACE_EMO
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    /// <summary>
    /// AgentTools for capturing FaceEmo expression thumbnails (Plan B).
    /// All tools require FaceEmoGate.RequireExpressionEditingReady() to pass.
    ///
    /// Launcher resolution priority (consistent across all 4 tools):
    ///   1. explicit gameObjectName  → the launcher GameObject itself (highest priority)
    ///   2. explicit avatarRootName  → FindLauncherForAvatar
    ///   3. FaceEmoExpressionSession.Active.Launcher (if a session is open)
    ///   4. generic auto-find (first configured FaceEmo* root in scene order)
    ///
    /// Without an explicit target, the Capture tools would silently look up the Mode in
    /// some arbitrary launcher's menu (often the wrong avatar's) and report
    /// "Mode 'X' not found" even though the Mode IS registered — just elsewhere.
    /// </summary>
    public static class FaceEmoThumbnailTools
    {
        /// <summary>
        /// 対象ランチャーを決める。<paramref name="gameObjectName"/> はランチャー
        /// GameObject 名の直接指定で、他の FaceEmo 系ツール (InspectFaceEmo /
        /// ListFaceEmoExpressions / SetExpressionAnimation …) と同じ引数名・同じ意味。
        ///
        /// この 4 ツールだけ <c>avatarRootName</c> しか受け取らなかったため、FaceEmo 系を
        /// 続けて呼んでいる最中に対象の指定方法だけが黙って切り替わっていた。渡した
        /// <c>gameObjectName</c> は捨てられ、既定の自動探索が別アバターの先頭ランチャーを
        /// 拾い、<c>RefreshFaceEmoMainView</c> に至っては**別のランチャーを操作して成功を返して
        /// いた** (issue #7)。
        /// </summary>
        private static FaceEmoGate.Result ResolveGate(string avatarRootName, string gameObjectName = "")
        {
            if (!string.IsNullOrEmpty(gameObjectName))
                return FaceEmoGate.RequireExpressionEditingReady(gameObjectName);
            if (!string.IsNullOrEmpty(avatarRootName))
                return FaceEmoGate.RequireExpressionEditingReadyForAvatar(avatarRootName);
            var active = FaceEmoExpressionSession.Active;
            if (active?.Launcher != null)
                return FaceEmoGate.RequireExpressionEditingReady(active.Launcher.gameObject.name);
            return FaceEmoGate.RequireExpressionEditingReady();
        }

        [AgentTool("Capture a single FaceEmo Mode's face thumbnail as a PNG and return its path. " +
                   "Use this to embed expression preview images in AI responses. " +
                   "modeName: the FaceEmo Mode display name to render. " +
                   "gameObjectName: optional — the FaceEmo launcher GameObject name (same meaning as in the " +
                   "other FaceEmo tools); takes priority over avatarRootName. " +
                   "avatarRootName: optional — when specified, picks the launcher targeting that avatar " +
                   "(otherwise prefers the active session's launcher, then generic auto-find).")]
        public static string CaptureFaceEmoModeThumbnail(string modeName, string avatarRootName = "", string gameObjectName = "")
        {
            var gate = ResolveGate(avatarRootName, gameObjectName);
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}. Expression editing still works; only thumbnails are unavailable.";

            var path = r.RenderModeThumbnail(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError} (searched launcher '{gate.Launcher.gameObject.name}' — pass avatarRootName if Mode is on a different launcher)";
            return $"Success: Captured thumbnail at '{path}' (launcher '{gate.Launcher.gameObject.name}').";
        }

        [AgentTool("Force-refresh FaceEmo's MainView thumbnail cache after editing an expression. " +
                   "Call this after CommitExpressionSession so the MainView shows the updated face. " +
                   "modeName is informational (the relaunch is global). " +
                   "gameObjectName / avatarRootName: optional launcher targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string RefreshFaceEmoMainView(string modeName = "", string avatarRootName = "", string gameObjectName = "")
        {
            var gate = ResolveGate(avatarRootName, gameObjectName);
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            return r.RefreshMainView(string.IsNullOrEmpty(modeName) ? null : modeName)
                ? $"Success: MainView refreshed (launcher '{gate.Launcher.gameObject.name}')."
                : $"Error: {r.LastReflectionError}";
        }

        [AgentTool("Capture a 4×2 grid of the 8 hand-gesture face thumbnails for a Mode and return the composite PNG path. " +
                   "Use this to show the user how all gesture combinations look. " +
                   "modeName: the FaceEmo Mode display name. " +
                   "gameObjectName / avatarRootName: optional launcher targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string CaptureFaceEmoGestureTable(string modeName, string avatarRootName = "", string gameObjectName = "")
        {
            var gate = ResolveGate(avatarRootName, gameObjectName);
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            var path = r.RenderGestureTable(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError} (searched launcher '{gate.Launcher.gameObject.name}')";
            return $"Success: Captured gesture table at '{path}' (launcher '{gate.Launcher.gameObject.name}').";
        }

        [AgentTool("Capture the ExMenu (VRChat menu)-baked thumbnail for a Mode and return its PNG path. " +
                   "Use this to preview what the avatar's VRChat radial menu will look like after upload. " +
                   "modeName: the FaceEmo Mode display name. " +
                   "gameObjectName / avatarRootName: optional launcher targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string CaptureFaceEmoExMenuThumbnail(string modeName, string avatarRootName = "", string gameObjectName = "")
        {
            var gate = ResolveGate(avatarRootName, gameObjectName);
            if (!gate.Ok) return gate.ErrorMessage;

            using var r = new FaceEmoThumbnailRenderer();
            if (!r.TryInitialize(gate.Launcher))
                return $"Error: Thumbnail renderer init failed — {r.LastReflectionError}.";

            var path = r.RenderExMenuThumbnail(modeName);
            if (path == null)
                return $"Error: {r.LastReflectionError} (searched launcher '{gate.Launcher.gameObject.name}')";
            return $"Success: Captured ExMenu thumbnail at '{path}' (launcher '{gate.Launcher.gameObject.name}').";
        }
    }
}
#endif
