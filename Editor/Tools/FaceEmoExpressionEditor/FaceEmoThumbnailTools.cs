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
    ///   1. explicit avatarRootName  → FindLauncherForAvatar (highest priority)
    ///   2. FaceEmoExpressionSession.Active.Launcher (if a session is open)
    ///   3. generic auto-find (first configured FaceEmo* root in scene order)
    ///
    /// Without (1) and (2), the Capture tools would silently look up the Mode in
    /// some arbitrary launcher's menu (often the wrong avatar's) and report
    /// "Mode 'X' not found" even though the Mode IS registered — just elsewhere.
    /// </summary>
    public static class FaceEmoThumbnailTools
    {
        private static FaceEmoGate.Result ResolveGate(string avatarRootName)
        {
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
                   "avatarRootName: optional — when specified, picks the launcher targeting that avatar " +
                   "(otherwise prefers the active session's launcher, then generic auto-find).")]
        public static string CaptureFaceEmoModeThumbnail(string modeName, string avatarRootName = "")
        {
            var gate = ResolveGate(avatarRootName);
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
                   "avatarRootName: optional avatar targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string RefreshFaceEmoMainView(string modeName = "", string avatarRootName = "")
        {
            var gate = ResolveGate(avatarRootName);
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
                   "avatarRootName: optional avatar targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string CaptureFaceEmoGestureTable(string modeName, string avatarRootName = "")
        {
            var gate = ResolveGate(avatarRootName);
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
                   "avatarRootName: optional avatar targeting (see CaptureFaceEmoModeThumbnail).")]
        public static string CaptureFaceEmoExMenuThumbnail(string modeName, string avatarRootName = "")
        {
            var gate = ResolveGate(avatarRootName);
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
