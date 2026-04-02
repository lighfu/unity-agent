using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor.Interfaces;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MetaTools
    {
        [AgentTool("List all registered tools grouped by status: enabled, disabled, and external. " +
                   "Use the optional filter parameter to show only a specific group.")]
        public static string ListTools(string filter = "all")
        {
            var allTools = ToolRegistry.GetAllTools();

            var enabled = new List<ToolRegistry.ToolInfo>();
            var disabled = new List<ToolRegistry.ToolInfo>();
            var externalEnabled = new List<ToolRegistry.ToolInfo>();
            var externalDisabled = new List<ToolRegistry.ToolInfo>();

            foreach (var t in allTools)
            {
                bool on = AgentSettings.IsToolEnabled(t.method.Name, t.isExternal);
                if (t.isExternal)
                {
                    if (on) externalEnabled.Add(t);
                    else externalDisabled.Add(t);
                }
                else
                {
                    if (on) enabled.Add(t);
                    else disabled.Add(t);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Tool Summary ===");
            sb.AppendLine($"Built-in : {enabled.Count} enabled, {disabled.Count} disabled");
            sb.AppendLine($"External : {externalEnabled.Count} enabled, {externalDisabled.Count} disabled");
            sb.AppendLine($"Total    : {allTools.Count}");
            sb.AppendLine();

            bool showAll = filter.Equals("all", StringComparison.OrdinalIgnoreCase);

            if (showAll || filter.Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                AppendToolGroup(sb, "Enabled Built-in Tools", enabled);
            }

            if (showAll || filter.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                AppendToolGroup(sb, "Disabled Built-in Tools", disabled);
            }

            if (showAll || filter.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                AppendToolGroup(sb, "External Tools (enabled)", externalEnabled);
                AppendToolGroup(sb, "External Tools (disabled)", externalDisabled);
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendToolGroup(StringBuilder sb, string title, List<ToolRegistry.ToolInfo> tools)
        {
            sb.AppendLine($"--- {title} ({tools.Count}) ---");
            if (tools.Count == 0)
            {
                sb.AppendLine("  (none)");
                sb.AppendLine();
                return;
            }

            // Group by declaring class
            var groups = tools
                .GroupBy(t => t.method.DeclaringType.Name)
                .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                string className = g.Key.EndsWith("Tools") ? g.Key.Substring(0, g.Key.Length - 5) : g.Key;
                var names = g.OrderBy(t => t.method.Name).Select(t =>
                {
                    string risk = t.resolvedRisk == ToolRisk.Dangerous ? "!" :
                                  t.resolvedRisk == ToolRisk.Safe ? "" : "~";
                    string ext = t.isExternal ? $" ({t.assemblyName})" : "";
                    return $"{risk}{t.method.Name}{ext}";
                });
                sb.AppendLine($"  [{className}] {string.Join(", ", names)}");
            }
            sb.AppendLine();
        }

        [AgentTool("Search for available tools by keyword to understand how to use them.")]
        public static string SearchTools(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return "Error: Endpoint keyword cannot be empty.";

            var allToolInfos = ToolRegistry.GetAllTools();
            var matchedTools = allToolInfos
                .Where(t => t.method.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (t.attribute?.Description ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.method.DeclaringType.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matchedTools.Count == 0)
            {
                return $"No tools found matching '{keyword}'. Try a different keyword.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matchedTools.Count} tools matching '{keyword}':");

            foreach (var toolInfo in matchedTools)
            {
                var method = toolInfo.method;
                var attr = toolInfo.attribute;
                bool isEnabled = AgentSettings.IsToolEnabled(method.Name, toolInfo.isExternal);
                string disabledTag = isEnabled ? "" : " [無効]";

                sb.AppendLine($"- {method.Name}{disabledTag}: {attr?.Description ?? ""}");

                var parms = method.GetParameters();
                int reqCount = parms.Count(p => !p.HasDefaultValue);
                var parameters = string.Join(", ", parms.Select(p =>
                {
                    string paramInfo = $"{p.ParameterType.Name} {p.Name}";
                    if (p.HasDefaultValue)
                    {
                        var val = p.DefaultValue;
                        if (val == null) paramInfo += " = null";
                        else if (val is string) paramInfo += $" = \"{val}\"";
                        else if (val is bool) paramInfo += $" = {val.ToString().ToLower()}";
                        else
                        {
                            string hint = "";
                            if (val is int intVal && intVal == -999) hint = " [unchanged]";
                            else if (val is float floatVal && floatVal == -999f) hint = " [unchanged]";
                            else if (val is int intVal2 && intVal2 == -1) hint = " [unchanged]";
                            paramInfo += $" = {val}{hint}";
                        }
                    }
                    else
                    {
                        paramInfo += " [REQUIRED]";
                    }
                    return paramInfo;
                }));

                string returnType = method.ReturnType == typeof(System.Collections.IEnumerator) ? "async" : method.ReturnType.Name;
                sb.AppendLine($"  Usage: {method.Name}({parameters}) -> {returnType}");
                if (reqCount > 0)
                    sb.AppendLine($"  * {reqCount} REQUIRED parameter(s) must be provided.");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
    }
}
