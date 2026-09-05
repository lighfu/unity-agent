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

        /// <summary>
        /// Treat <see cref="Risk"/> as an explicit choice even when it is <see cref="ToolRisk.Caution"/>.
        ///
        /// Caution is also the property's default value, so a built-in tool cannot otherwise say
        /// "I really am Caution" — the resolver can't tell that apart from "not specified" and
        /// falls back to classifying by method-name prefix. That prefix rule sends anything named
        /// Delete*/Remove*/Reset*/Run*/Trigger* to Dangerous, which hides it from MCP clients under
        /// the default expose level. Set this when the prefix over-states the real risk (deleting a
        /// single EditorPrefs key is not deleting a GameObject).
        ///
        /// Ignored for Safe and Dangerous, which are already unambiguous, and for external tools,
        /// whose Risk value is always taken at face value.
        /// </summary>
        public bool RiskExplicit { get; set; }

        public AgentToolAttribute(string description) => Description = description;
    }
}
