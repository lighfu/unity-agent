namespace AjisaiFlow.UnityAgent.SDK
{
    public enum ToolRisk
    {
        Safe = 0,       // 読み取り専用（List, Get, Inspect 等）
        Caution = 1,    // 変更操作（Create, Set, Add 等）
        Dangerous = 2   // 破壊的操作（Delete, Remove, Run 等）
    }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class AgentToolAttribute : System.Attribute
    {
        public string Description { get; }

        // Extended metadata (all optional)
        public string Author { get; set; }
        public string Version { get; set; }
        public string Category { get; set; }
        public string Url { get; set; }
        public ToolRisk Risk { get; set; } = ToolRisk.Caution;

        public AgentToolAttribute(string description) => Description = description;
    }
}
