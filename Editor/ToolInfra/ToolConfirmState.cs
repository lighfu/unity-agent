namespace AjisaiFlow.UnityAgent.Editor
{
    public static class ToolConfirmState
    {
        public const int APPROVE = 0;
        public const int CANCEL = 1;
        public const int APPROVE_AND_DISABLE = 2;
        public const int APPROVE_ALL_SESSION = 3;

        public static bool IsPending;
        public static string ToolName;
        public static string Description;
        public static string Parameters;
        public static int SelectedIndex = -1;
        public static bool SessionSkipAll;

        public static void Request(string toolName, string description, string parameters)
        {
            ToolName = toolName;
            Description = description;
            Parameters = parameters;
            SelectedIndex = -1;
            IsPending = true;
        }

        public static void Select(int index)
        {
            SelectedIndex = index;
            IsPending = false;
        }

        public static void Clear()
        {
            IsPending = false;
            ToolName = null;
            Description = null;
            Parameters = null;
            SelectedIndex = -1;
        }
    }
}
