using AjisaiFlow.MD3SDK.Editor;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart.UI
{
    /// <summary>
    /// Resolves the active MD3Theme using the same precedence as UnityAgentWindow,
    /// so the flowchart editor stays visually consistent with the main agent window.
    /// </summary>
    internal static class FlowchartTheme
    {
        public static MD3Theme Resolve()
        {
            switch (AgentSettings.ThemeMode)
            {
                case 1: return MD3Theme.Dark();
                case 2: return MD3Theme.Light();
                case 3: return AgentSettings.BuildCustomTheme();
                default: return MD3Theme.Auto();
            }
        }
    }
}
