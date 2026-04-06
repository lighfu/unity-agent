using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor
{
    public static class ToolRegistry
    {
        public struct ToolInfo
        {
            public MethodInfo method;
            public AgentToolAttribute attribute;
            public bool isExternal;
            public string assemblyName;
            public ToolRisk resolvedRisk;
        }

        private static List<ToolInfo> _cache;
        private static readonly string MainAssemblyName = typeof(ToolRegistry).Assembly.GetName().Name;

        public static List<ToolInfo> GetAllTools()
        {
            if (_cache != null) return _cache;

            const string sdkAssemblyName = "AjisaiFlow.UnityAgent.SDK";
            var mainAssembly = typeof(ToolRegistry).Assembly;
            var mainAssemblyName = mainAssembly.GetName().Name;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a == mainAssembly ||
                            a.GetReferencedAssemblies().Any(r =>
                                r.Name == sdkAssemblyName || r.Name == mainAssemblyName));

            var result = new List<ToolInfo>();

            foreach (var asm in assemblies)
            {
                bool isExternal = asm != mainAssembly;
                string asmName = asm.GetName().Name;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    foreach (var method in methods)
                    {
                        var attr = GetAgentToolAttribute(method);
                        if (attr == null) continue;

                        var risk = ResolveRisk(attr, method.Name, isExternal);

                        result.Add(new ToolInfo
                        {
                            method = method,
                            attribute = attr,
                            isExternal = isExternal,
                            assemblyName = asmName,
                            resolvedRisk = risk
                        });
                    }
                }
            }

            // Warn about duplicate tool names
            var duplicates = result
                .GroupBy(t => t.method.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);
            foreach (var dup in duplicates)
            {
                var locations = string.Join(", ",
                    dup.Select(t => $"{t.method.DeclaringType.Name}({t.assemblyName})"));
                AgentLogger.Warning(LogTag.Tool, $"Duplicate tool name '{dup.Key}' found in: {locations}");
            }

            _cache = result;
            return _cache;
        }

        public static List<MethodInfo> GetAllMethods()
        {
            return GetAllTools().Select(t => t.method).ToList();
        }

        /// <summary>
        /// 有効なツールのメソッド一覧を返す。
        /// システムプロンプトや実行パスで使用。
        /// </summary>
        public static List<MethodInfo> GetEnabledMethods()
        {
            return GetAllTools()
                .Where(t => AgentSettings.IsToolEnabled(t.method.Name, t.isExternal))
                .Select(t => t.method)
                .ToList();
        }

        public static void InvalidateCache()
        {
            _cache = null;
        }

        /// <summary>
        /// リスク解決: 内蔵ツールはメソッド名プレフィックスで判定、外部ツールは属性値をそのまま使用。
        /// </summary>
        private static ToolRisk ResolveRisk(AgentToolAttribute attr, string methodName, bool isExternal)
        {
            // 外部ツール → 属性の値をそのまま使用
            if (isExternal)
                return attr.Risk;

            // 内蔵ツール → メソッド名プレフィックス判定で上書き
            return ClassifyByMethodName(methodName);
        }

        /// <summary>
        /// メソッド名プレフィックスからリスクレベルを判定する。
        /// 内蔵ツールのフォールバック判定として利用。
        /// </summary>
        /// <summary>
        /// AgentToolAttribute を取得する。同一アセンブリの型が一致しない場合は
        /// 文字列ベースで検索し、プロパティをリフレクションで読み取る。
        /// これにより、SDK を Editor DLL に内蔵しても外部ツールの属性を認識できる。
        /// </summary>
        internal static AgentToolAttribute GetAgentToolAttribute(MethodInfo method)
        {
            // Direct type match (same assembly or type-forwarded)
            var attr = method.GetCustomAttribute<AgentToolAttribute>();
            if (attr != null) return attr;

            // Fallback: string-based match for cross-assembly compatibility
            var rawAttr = method.GetCustomAttributes(false)
                .FirstOrDefault(a => a.GetType().FullName == "AjisaiFlow.UnityAgent.SDK.AgentToolAttribute");
            if (rawAttr == null) return null;

            var attrType = rawAttr.GetType();
            var desc = attrType.GetProperty("Description")?.GetValue(rawAttr) as string ?? "";
            var result = new AgentToolAttribute(desc);
            result.Author = attrType.GetProperty("Author")?.GetValue(rawAttr) as string;
            result.Version = attrType.GetProperty("Version")?.GetValue(rawAttr) as string;
            result.Category = attrType.GetProperty("Category")?.GetValue(rawAttr) as string;
            result.Url = attrType.GetProperty("Url")?.GetValue(rawAttr) as string;
            var riskVal = attrType.GetProperty("Risk")?.GetValue(rawAttr);
            if (riskVal != null) result.Risk = (ToolRisk)(int)riskVal;
            return result;
        }

        internal static ToolRisk ClassifyByMethodName(string methodName)
        {
            if (methodName.StartsWith("Delete") ||
                methodName.StartsWith("Remove") ||
                methodName.StartsWith("Flatten") ||
                methodName.StartsWith("Reset") ||
                methodName.StartsWith("Clean") ||
                methodName.StartsWith("Run") ||
                methodName.StartsWith("Trigger") ||
                methodName.StartsWith("BatchDelete"))
                return ToolRisk.Dangerous;

            if (methodName.StartsWith("List") ||
                methodName.StartsWith("Get") ||
                methodName.StartsWith("Inspect") ||
                methodName.StartsWith("Search") ||
                methodName.StartsWith("Find") ||
                methodName.StartsWith("Show") ||
                methodName.StartsWith("Validate") ||
                methodName.StartsWith("Check") ||
                methodName.StartsWith("Read") ||
                methodName.StartsWith("Preview") ||
                methodName.StartsWith("Focus") ||
                methodName.StartsWith("Select") ||
                methodName.StartsWith("Capture") ||
                methodName.StartsWith("Launch") ||
                methodName.StartsWith("Open") ||
                methodName.StartsWith("Ask"))
                return ToolRisk.Safe;

            return ToolRisk.Caution;
        }
    }
}
