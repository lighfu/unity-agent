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
