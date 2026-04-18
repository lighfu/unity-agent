namespace AjisaiFlow.UnityAgent.Editor
{
    public static class ToolProgress
    {
        public static bool IsActive;
        public static float Progress; // 0.0 ~ 1.0
        public static string Status;
        public static string Detail; // multi-line metadata (model, prompt, etc.)

        public static void Report(float progress, string status, string detail = null)
        {
            IsActive = true;
            Progress = progress;
            Status = status;
            if (detail != null)
                Detail = detail;
        }

        public static void Clear()
        {
            IsActive = false;
            Progress = 0f;
            Status = null;
            Detail = null;
        }
    }
}
