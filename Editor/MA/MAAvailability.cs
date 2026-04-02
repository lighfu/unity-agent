namespace AjisaiFlow.UnityAgent.Editor.MA
{
    internal static class MAAvailability
    {
        public static bool IsInstalled =>
#if MODULAR_AVATAR
            true;
#else
            false;
#endif

        public static string CheckOrError() =>
            IsInstalled ? null : "Error: Modular Avatar is not installed. Please install nadena.dev.modular-avatar.";
    }
}
