namespace AjisaiFlow.UnityAgent.Editor
{
    public static class UserChoiceState
    {
        public static bool IsPending;
        public static string Question;
        public static string[] Options;
        public static string Importance; // "info", "warning", "critical"
        public static int SelectedIndex = -1;
        public static string CustomText;

        public static void Request(string question, string[] options, string importance = "info")
        {
            Question = question;
            Options = options;
            Importance = importance;
            SelectedIndex = -1;
            CustomText = null;
            IsPending = true;
        }

        public static void Select(int index)
        {
            SelectedIndex = index;
            IsPending = false;
        }

        public static void SelectCustom(string text)
        {
            CustomText = text;
            SelectedIndex = 0;
            IsPending = false;
        }

        public static void Clear()
        {
            IsPending = false;
            Question = null;
            Options = null;
            Importance = "info";
            SelectedIndex = -1;
            CustomText = null;
        }
    }
}
