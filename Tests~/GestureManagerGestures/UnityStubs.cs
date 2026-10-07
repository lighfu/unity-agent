// This harness exercises input validation and package-absent tool/schema behavior.
// It does not simulate Unity or Gesture Manager's installed runtime.
namespace UnityEngine { }
namespace UnityEditor
{
    internal static class EditorApplication
    {
        internal static bool isPlaying;
        internal static bool isPlayingOrWillChangePlaymode;
        internal static void ExitPlaymode() { isPlaying = false; isPlayingOrWillChangePlaymode = false; }
    }
}
