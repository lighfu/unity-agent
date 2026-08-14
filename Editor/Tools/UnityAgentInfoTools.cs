using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// "What am I actually talking to?" — the one call that answers it.
    ///
    /// Before this existed, a client connecting over MCP could not learn the UnityAgent version
    /// (the only version on the wire was <c>serverInfo.version</c>, which reported the assembly
    /// version and therefore <c>0.0.0.0</c>), could not learn the Unity version at all, and could
    /// not tell whether a missing tool was a bug or an optional package that had simply never been
    /// installed — roughly a fifth of the tool surface is compiled out by asmdef versionDefines.
    /// </summary>
    public static class UnityAgentInfoTools
    {
        private const string DetailBrief = "brief";
        private const string DetailFull = "full";

        [AgentTool(@"Report what this UnityAgent install actually is: its version, the Unity version and
project it is attached to, how many tools are registered and how many of those are reachable, which
optional packages are present, and how the MCP server is wired up.

detail='brief' (default) is a handful of lines and is meant to be called once at the start of a
  session so later answers are not guesses about the environment.
detail='full' adds the per-category and per-risk tool breakdown, every detected package with its
  version, the MCP endpoint / bridge state, and project render settings. Use it when writing a bug
  report — it is the information a maintainer would otherwise have to ask for one field at a time.

WHY THE TOOL COUNT IS NOT A CONSTANT: twelve asmdef versionDefines (MODULAR_AVATAR, NDMF, FACE_EMO,
AVATAR_OPTIMIZER, VRCFURY, LILTOON, ...) compile whole tool modules in or out. A tool that is
'missing' on one machine and present on another is usually an absent package, not a broken build,
and the Packages section is how you tell those two apart.

MCP-VISIBLE IS NOT THE SAME AS ENABLED: tools whose resolved risk exceeds the MCP expose limit
(default Caution) are reachable from the in-editor chat but not over MCP. The counts are reported
separately because 'the tool exists and is enabled' does not imply 'you can call it from here'.

Secrets are never returned — tokens and passwords are reported only as configured / not set.
Anything that cannot be determined is reported as 'unknown' rather than omitted, so a blank never
reads as a zero.",
            Author = "ajisaiflow", Category = "Meta", Risk = ToolRisk.Safe)]
        public static string GetUnityAgentInfo(string detail = DetailBrief)
        {
            string mode = (detail ?? DetailBrief).Trim().ToLowerInvariant();
            if (mode.Length == 0) mode = DetailBrief;
            if (mode != DetailBrief && mode != DetailFull)
                return $"Error: detail must be '{DetailBrief}' or '{DetailFull}' (got '{detail}').";

            var tools = CollectToolStats();
            var packages = CollectPackages(out bool upmAvailable);
            var sb = new StringBuilder();

            if (mode == DetailBrief)
            {
                AppendBrief(sb, tools, packages);
                return sb.ToString().TrimEnd();
            }

            AppendFull(sb, tools, packages, upmAvailable);
            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────
        // brief
        // ─────────────────────────────────────────────────────────────

        private static void AppendBrief(StringBuilder sb, ToolStats tools, List<PackageState> packages)
        {
            sb.AppendLine($"UnityAgent {SafeGet(() => UpdateChecker.CurrentVersion)} " +
                          $"(Unity {SafeGet(() => Application.unityVersion)}, " +
                          $"{SafeGet(() => Application.platform.ToString())}, " +
                          $"buildTarget={SafeGet(() => EditorUserBuildSettings.activeBuildTarget.ToString())})");

            sb.AppendLine($"Tools: {tools.UniqueNames} unique, {tools.BuiltInEnabled + tools.ExternalEnabled} enabled / " +
                          $"{tools.BuiltInDisabled + tools.ExternalDisabled} disabled — " +
                          $"MCP-visible {tools.McpVisible} (exposeRisk={tools.ExposeRisk})");

            sb.AppendLine($"MCP: {DescribeMcpOneLine()}");

            var installed = packages.Where(p => p.Installed).ToList();
            if (installed.Count == 0)
            {
                sb.AppendLine("Packages: none of the known optional packages detected");
            }
            else
            {
                var head = installed.Take(3).Select(p => $"{p.Display} {p.Version}");
                string more = installed.Count > 3 ? $" (+{installed.Count - 3} more)" : "";
                sb.AppendLine($"Packages: {string.Join(", ", head)}{more}");
            }

            sb.AppendLine($"Project: {SafeGet(() => Application.productName)} — " +
                          $"{SafeGet(() => PlayerSettings.colorSpace.ToString())}, {DescribeRenderPipeline()}");

            sb.AppendLine($"Pass detail='{DetailFull}' for category/risk breakdown, all packages, and MCP details.");
        }

        // ─────────────────────────────────────────────────────────────
        // full
        // ─────────────────────────────────────────────────────────────

        private static void AppendFull(StringBuilder sb, ToolStats tools, List<PackageState> packages, bool upmAvailable)
        {
            sb.AppendLine("=== UnityAgent ===");
            sb.AppendLine($"version        : {SafeGet(() => UpdateChecker.CurrentVersion)}");
            sb.AppendLine($"packageRoot    : {SafeGet(() => PackagePaths.PackageRoot)}");
            sb.AppendLine($"update         : {DescribeUpdateState()}");
            sb.AppendLine($"license        : {DescribeLicenseState()}");
            sb.AppendLine($"devBuild       : {SafeGet(() => DeveloperMode.IsDevBuild.ToString())}");
            sb.AppendLine($"debugMode      : {SafeGet(() => AgentSettings.DebugMode.ToString())}");
            sb.AppendLine();

            sb.AppendLine("=== Unity ===");
            sb.AppendLine($"unityVersion   : {SafeGet(() => Application.unityVersion)}");
            sb.AppendLine($"platform       : {SafeGet(() => Application.platform.ToString())}");
            sb.AppendLine($"buildTarget    : {SafeGet(() => EditorUserBuildSettings.activeBuildTarget.ToString())}");
            sb.AppendLine($"batchMode      : {SafeGet(() => Application.isBatchMode.ToString())}");
            sb.AppendLine($"colorSpace     : {SafeGet(() => PlayerSettings.colorSpace.ToString())}");
            sb.AppendLine($"renderPipeline : {DescribeRenderPipeline()}");
            sb.AppendLine($"projectName    : {SafeGet(() => Application.productName)}");
            sb.AppendLine($"projectPath    : {SafeGet(GetProjectRoot)}");
            sb.AppendLine();

            sb.AppendLine("=== Tools ===");
            string dupNote = tools.RegisteredEntries == tools.UniqueNames
                ? ""
                : $" ({tools.RegisteredEntries - tools.UniqueNames} duplicate name(s) — the registry keeps them, dispatch takes the first)";
            sb.AppendLine($"registered     : {tools.RegisteredEntries} entries, {tools.UniqueNames} unique names{dupNote}");
            sb.AppendLine($"built-in       : {tools.BuiltInEnabled} enabled / {tools.BuiltInDisabled} disabled");
            sb.AppendLine($"external       : {tools.ExternalEnabled} enabled / {tools.ExternalDisabled} disabled " +
                          "(external tools are opt-in: absent from the allow-list means disabled)");
            sb.AppendLine($"mcpVisible     : {tools.McpVisible} " +
                          $"(exposeRisk={tools.ExposeRisk} hides {tools.HiddenByRisk} higher-risk tool(s) from MCP only; " +
                          "the in-editor chat is not affected)");
            sb.AppendLine($"risk           : Safe {tools.Safe} / Caution {tools.Caution} / Dangerous {tools.Dangerous}");
            sb.AppendLine($"confirmGated   : {tools.ConfirmRequired} tool(s) ask for confirmation before running");
            sb.AppendLine($"skills         : {DescribeSkills()}");
            sb.AppendLine($"categories     : {FormatCategories(tools.Categories)}");
            sb.AppendLine();

            sb.AppendLine("=== MCP ===");
            AppendMcpSection(sb);
            sb.AppendLine();

            sb.AppendLine("=== Packages ===");
            AppendPackageSection(sb, packages, upmAvailable);
        }

        // ─────────────────────────────────────────────────────────────
        // tools
        // ─────────────────────────────────────────────────────────────

        private struct ToolStats
        {
            public int RegisteredEntries;
            public int UniqueNames;
            public int BuiltInEnabled, BuiltInDisabled;
            public int ExternalEnabled, ExternalDisabled;
            public int McpVisible, HiddenByRisk;
            public int Safe, Caution, Dangerous;
            public int ConfirmRequired;
            public ToolRisk ExposeRisk;
            public List<KeyValuePair<string, int>> Categories;
        }

        private static ToolStats CollectToolStats()
        {
            var stats = new ToolStats
            {
                ExposeRisk = ToolRisk.Caution,
                Categories = new List<KeyValuePair<string, int>>(),
            };

            try
            {
                stats.ExposeRisk = AgentSettings.MCPServerExposeRisk;

                // ToolInfo is a struct, so a registry entry whose reflection failed shows up as a
                // default value with a null method rather than being absent. Filter first, or every
                // count below is off by however many of those exist.
                var all = ToolRegistry.GetAllTools().Where(t => t.method != null).ToList();
                stats.RegisteredEntries = all.Count;

                // The registry warns about duplicate tool names but deliberately keeps both entries,
                // while MCP dispatch resolves a name to exactly one tool. Counting raw entries would
                // therefore report more tools than can actually be called.
                var unique = all
                    .GroupBy(t => t.method.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                stats.UniqueNames = unique.Count;

                var categories = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var t in unique)
                {
                    bool enabled = false;
                    try { enabled = AgentSettings.IsToolEnabled(t.method.Name, t.isExternal); }
                    catch { /* treat as disabled; the counts below stay internally consistent */ }

                    if (t.isExternal)
                    {
                        if (enabled) stats.ExternalEnabled++; else stats.ExternalDisabled++;
                    }
                    else
                    {
                        if (enabled) stats.BuiltInEnabled++; else stats.BuiltInDisabled++;
                    }

                    // resolvedRisk, not attribute.Risk: the attribute defaults to Caution, so every
                    // built-in tool that did not spell out a risk would otherwise be counted as
                    // Caution even though the registry reclassified it from the method name.
                    switch (t.resolvedRisk)
                    {
                        case ToolRisk.Safe: stats.Safe++; break;
                        case ToolRisk.Dangerous: stats.Dangerous++; break;
                        default: stats.Caution++; break;
                    }

                    if (enabled)
                    {
                        if ((int)t.resolvedRisk <= (int)stats.ExposeRisk) stats.McpVisible++;
                        else stats.HiddenByRisk++;
                    }

                    try
                    {
                        if (AgentSettings.IsToolConfirmRequired(t.method.Name)) stats.ConfirmRequired++;
                    }
                    catch { /* confirmation config is advisory here */ }

                    string cat = CategoryOf(t);
                    categories.TryGetValue(cat, out int n);
                    categories[cat] = n + 1;
                }

                stats.Categories = categories
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool, $"GetUnityAgentInfo: tool statistics unavailable: {ex.Message}");
            }

            return stats;
        }

        /// <summary>
        /// Most built-in tools leave <see cref="AgentToolAttribute.Category"/> unset, so the
        /// declaring type name is the fallback everywhere else in this codebase (the settings
        /// window, the tool console and the system prompt all group the same way). Grouping on the
        /// attribute alone would drop the majority of tools into one empty bucket.
        /// </summary>
        private static string CategoryOf(ToolRegistry.ToolInfo t)
        {
            var declared = t.attribute?.Category;
            if (!string.IsNullOrEmpty(declared)) return declared;

            var type = t.method.DeclaringType;
            if (type == null) return "Other";
            string name = type.Name.Replace("Tools", "");
            return string.IsNullOrEmpty(name) ? "Other" : name;
        }

        private static string FormatCategories(List<KeyValuePair<string, int>> categories)
        {
            if (categories == null || categories.Count == 0) return "unknown";
            const int Shown = 24;
            var head = categories.Take(Shown).Select(kv => $"{kv.Key}({kv.Value})");
            string tail = categories.Count > Shown
                ? $" ... +{categories.Count - Shown} more categories"
                : "";
            return string.Join(" ", head) + tail;
        }

        private static string DescribeSkills()
        {
            try
            {
                var skills = SkillTools.GetAllSkills();
                if (skills == null) return "unknown";
                int disabled = 0;
                foreach (var key in skills.Keys)
                {
                    try { if (AgentSettings.IsSkillDisabled(key)) disabled++; }
                    catch { /* counted as enabled */ }
                }
                return $"{skills.Count - disabled} enabled, {disabled} disabled";
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // MCP
        // ─────────────────────────────────────────────────────────────

        private static string DescribeMcpOneLine()
        {
            try
            {
                if (!AgentSettings.MCPServerEnabled)
                    return "disabled in UnityAgent settings";

                var mode = AgentSettings.MCPServerMode;
                if (mode == MCPServerMode.Bridge)
                {
                    var bridge = AgentMCPBridgeClient.Shared;
                    string state = bridge.IsConnected ? "connected"
                                 : bridge.IsStarting ? "starting"
                                 : "NOT connected";
                    return $"Bridge {state}, public port {AgentSettings.MCPBridgePublicPort}";
                }

                var server = AgentMCPServer.Shared;
                return server.IsRunning
                    ? $"running InProc at {server.Endpoint}, {server.TotalCallsServed} calls served"
                    : $"InProc, NOT running (configured port {AgentSettings.MCPServerPort})";
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }

        private static void AppendMcpSection(StringBuilder sb)
        {
            try
            {
                sb.AppendLine($"enabled        : {AgentSettings.MCPServerEnabled}");
                sb.AppendLine($"mode           : {AgentSettings.MCPServerMode}");
                sb.AppendLine($"protocol       : {MCPHttpProtocol.LatestProtocolVersion} (latest supported)");
                sb.AppendLine($"exposeRisk     : {AgentSettings.MCPServerExposeRisk}");

                // Never print the token. Whether one is configured is the only part that helps
                // diagnose a 401, and the value itself is a credential.
                string token = AgentSettings.MCPServerToken;
                sb.AppendLine($"token          : {(string.IsNullOrEmpty(token) ? "not set" : "configured (value withheld)")}");

                var server = AgentMCPServer.Shared;
                sb.AppendLine($"inProcServer   : {(server.IsRunning ? $"running on port {server.Port}" : "stopped")}");
                sb.AppendLine($"endpoint       : {(server.IsRunning ? server.Endpoint : "n/a (server not running)")}");
                sb.AppendLine($"callsServed    : {server.TotalCallsServed}");

                var bridge = AgentMCPBridgeClient.Shared;
                string bridgeState = bridge.IsConnected ? $"connected on internal port {bridge.Port}"
                                   : bridge.IsStarting ? "starting"
                                   : "not connected";
                sb.AppendLine($"bridge         : {bridgeState} " +
                              $"(configured internal {AgentSettings.MCPBridgeInternalPort} / public {AgentSettings.MCPBridgePublicPort})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(MCP state unavailable: {ex.GetType().Name}: {ex.Message})");
            }

            // Outbound MCP servers — UnityAgent as a client. Separate try: a failure here says
            // nothing about the inbound server above.
            try
            {
                if (!MCPManager.IsInitialized)
                {
                    sb.AppendLine("outboundServers: MCPManager not initialized");
                    return;
                }
                var statuses = MCPManager.GetServerStatuses();
                if (statuses == null || statuses.Count == 0)
                {
                    sb.AppendLine("outboundServers: none configured");
                    return;
                }
                sb.AppendLine($"outboundServers: {statuses.Count}");
                foreach (var s in statuses)
                {
                    string line = s.IsConnected
                        ? $"connected, {s.ToolCount} tools"
                        : $"DISCONNECTED{(string.IsNullOrEmpty(s.LastError) ? "" : $" — {s.LastError}")}";
                    sb.AppendLine($"  - {s.Name}: {line}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"outboundServers: unknown ({ex.GetType().Name})");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // packages
        // ─────────────────────────────────────────────────────────────

        private struct PackageState
        {
            public string Id;
            public string Display;
            public string DefineSymbol;   // null when the package has no versionDefine
            public bool DefineActive;
            public bool FoundInUpm;
            public string Version;        // "unknown" when detected only by define / reflection
            public string ReflectionNote; // non-empty when a type probe or sub-assembly decided it
            public bool SubAssemblyEmpty; // installed, but its tool assembly compiled nothing
            public bool Installed => FoundInUpm || DefineActive || ReflectionNote != null;
        }

        private struct PackageProbe
        {
            public string Id;
            public string Display;
            public string DefineSymbol;
            public string ProbeType;      // resolved through the loaded assemblies when set
            public string SubAssembly;    // tools loaded from this assembly prove the package is usable
        }

        private static readonly PackageProbe[] Probes =
        {
            new PackageProbe { Id = "com.vrchat.avatars", Display = "VRChat SDK - Avatars", DefineSymbol = null, ProbeType = VRChatTools.VrcDescriptorTypeName },
            new PackageProbe { Id = "com.vrchat.worlds",  Display = "VRChat SDK - Worlds",  DefineSymbol = null, ProbeType = "VRC.SDK3.Components.VRCSceneDescriptor" },
            new PackageProbe { Id = "com.vrchat.base",    Display = "VRChat SDK - Base",    DefineSymbol = null, ProbeType = "VRC.SDKBase.VRC_AvatarDescriptor" },
            new PackageProbe { Id = "nadena.dev.ndmf",                        Display = "NDMF",                  DefineSymbol = "NDMF" },
            new PackageProbe { Id = "nadena.dev.modular-avatar",              Display = "Modular Avatar",        DefineSymbol = "MODULAR_AVATAR" },
            new PackageProbe { Id = "jp.suzuryg.face-emo",                    Display = "FaceEmo",               DefineSymbol = "FACE_EMO" },
            new PackageProbe { Id = "com.vrcfury.vrcfury",                    Display = "VRCFury",               DefineSymbol = "VRCFURY" },
            new PackageProbe { Id = "com.github.kurotu.vrc-quest-tools",      Display = "VRC Quest Tools",       DefineSymbol = "VRC_QUEST_TOOLS" },
            new PackageProbe { Id = "net.nekobako.blend-shape-modifier",      Display = "BlendShape Modifier",   DefineSymbol = "BLEND_SHAPE_MODIFIER" },
            new PackageProbe { Id = "dev.hai-vr.animator-as-code.v1",         Display = "Animator As Code",      DefineSymbol = "ANIMATOR_AS_CODE" },
            new PackageProbe { Id = "com.anatawa12.avatar-optimizer",         Display = "Avatar Optimizer",      DefineSymbol = "AVATAR_OPTIMIZER" },
            new PackageProbe { Id = "jp.lilxyzw.liltoon",                     Display = "lilToon",               DefineSymbol = "LILTOON" },
            new PackageProbe { Id = "jp.lilxyzw.ndmfmeshsimplifier",          Display = "NDMF Mesh Simplifier",  DefineSymbol = "NDMF_MESH_SIMPLIFIER" },
            new PackageProbe { Id = "vrchat.blackstartx.gesture-manager",     Display = "Gesture Manager",       DefineSymbol = "GESTURE_MANAGER" },
            // NET_RS64_TTT is declared only on the TexTransTool sub-assembly's asmdef, so #if cannot
            // see it from here. Counting tools that actually loaded from that assembly is a stronger
            // signal than either UPM or a type probe: the versionDefine carries a [1.0.0,2.0.0)
            // range, so a package outside that range is installed yet compiles no tools at all —
            // which is exactly the state a caller needs to be able to distinguish.
            new PackageProbe { Id = "net.rs64.tex-trans-tool", Display = "TexTransTool", DefineSymbol = null, SubAssembly = "AjisaiFlow.UnityAgent.TexTransTool.Editor" },
        };

        private static List<PackageState> CollectPackages(out bool upmAvailable)
        {
            var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            upmAvailable = true;
            try
            {
                var registered = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                if (registered == null)
                {
                    upmAvailable = false;
                }
                else
                {
                    foreach (var p in registered)
                    {
                        if (p == null || string.IsNullOrEmpty(p.name)) continue;
                        byId[p.name] = string.IsNullOrEmpty(p.version) ? "unknown" : p.version;
                    }
                }
            }
            catch (Exception ex)
            {
                upmAvailable = false;
                AgentLogger.Warning(LogTag.Tool, $"GetUnityAgentInfo: UPM package list unavailable: {ex.Message}");
            }

            var result = new List<PackageState>(Probes.Length);
            foreach (var probe in Probes)
            {
                var state = new PackageState
                {
                    Id = probe.Id,
                    Display = probe.Display,
                    DefineSymbol = probe.DefineSymbol,
                    DefineActive = probe.DefineSymbol != null && IsDefineActive(probe.DefineSymbol),
                    Version = "unknown",
                };

                if (upmAvailable && byId.TryGetValue(probe.Id, out string version))
                {
                    state.FoundInUpm = true;
                    state.Version = version;
                }

                if (!string.IsNullOrEmpty(probe.ProbeType))
                {
                    try
                    {
                        if (VRChatTools.FindVrcType(probe.ProbeType) != null)
                            state.ReflectionNote = "type present";
                    }
                    catch { /* a failed probe is simply no evidence, not a failure of the tool */ }
                }

                if (!string.IsNullOrEmpty(probe.SubAssembly))
                {
                    try
                    {
                        int loaded = ToolRegistry.GetAllTools()
                            .Count(t => t.method != null &&
                                        string.Equals(t.assemblyName, probe.SubAssembly, StringComparison.Ordinal));
                        if (loaded > 0)
                            state.ReflectionNote = $"{loaded} tools loaded from {probe.SubAssembly}";
                        else if (state.FoundInUpm)
                            state.SubAssemblyEmpty = true;
                    }
                    catch { /* registry unavailable; the UPM row still stands on its own */ }
                }

                result.Add(state);
            }

            return result;
        }

        private static void AppendPackageSection(StringBuilder sb, List<PackageState> packages, bool upmAvailable)
        {
            if (!upmAvailable)
            {
                // Without this line an empty version column reads as "not installed", when in fact
                // the package list itself could not be obtained.
                sb.AppendLine("WARNING: the UPM package list could not be read — versions below are " +
                              "unknown and packages installed only through UPM will not appear at all.");
            }

            var installed = packages.Where(p => p.Installed).ToList();
            var missing = packages.Where(p => !p.Installed).ToList();

            if (installed.Count == 0)
            {
                sb.AppendLine("none of the known optional packages were detected");
            }
            else
            {
                int width = installed.Max(p => p.Display.Length);
                foreach (var p in installed)
                {
                    var how = new List<string>();
                    if (p.FoundInUpm) how.Add("upm");
                    if (p.DefineActive) how.Add($"define {p.DefineSymbol}");
                    if (!string.IsNullOrEmpty(p.ReflectionNote)) how.Add($"reflection ({p.ReflectionNote})");

                    // "define set but not in UPM" means a manual drop into Assets/, which UPM cannot
                    // see. Saying how each row was detected is what makes that case readable.
                    string note = "";
                    if (p.SubAssemblyEmpty)
                        note = "  <- INSTALLED BUT NO TOOLS COMPILED (version outside the asmdef versionDefine range?)";
                    else if (!p.FoundInUpm && p.DefineActive)
                        note = "  <- not registered with UPM (installed under Assets/?)";
                    else if (p.FoundInUpm && p.DefineSymbol != null && !p.DefineActive)
                        note = "  <- UPM has it but the define is off (recompile pending?)";

                    sb.AppendLine($"{p.Display.PadRight(width)}  {p.Version,-12} {string.Join(" + ", how)}{note}");
                }
            }

            if (missing.Count > 0)
                sb.AppendLine($"not installed  : {string.Join(", ", missing.Select(p => p.Display))}");

            sb.AppendLine("note           : TexTransTool's NET_RS64_TTT define lives on a separate asmdef and " +
                          "cannot be observed from here, so only UPM can confirm it.");
        }

        /// <summary>
        /// Compile-time package defines, read through a switch because <c>#if</c> cannot be applied
        /// to a table entry. Every symbol here must match a versionDefine in
        /// Editor/AjisaiFlow.UnityAgent.Editor.asmdef, or it silently reports false forever.
        /// </summary>
        private static bool IsDefineActive(string symbol)
        {
            switch (symbol)
            {
                case "NDMF":
#if NDMF
                    return true;
#else
                    return false;
#endif
                case "MODULAR_AVATAR":
#if MODULAR_AVATAR
                    return true;
#else
                    return false;
#endif
                case "FACE_EMO":
#if FACE_EMO
                    return true;
#else
                    return false;
#endif
                case "VRCFURY":
#if VRCFURY
                    return true;
#else
                    return false;
#endif
                case "VRC_QUEST_TOOLS":
#if VRC_QUEST_TOOLS
                    return true;
#else
                    return false;
#endif
                case "BLEND_SHAPE_MODIFIER":
#if BLEND_SHAPE_MODIFIER
                    return true;
#else
                    return false;
#endif
                case "ANIMATOR_AS_CODE":
#if ANIMATOR_AS_CODE
                    return true;
#else
                    return false;
#endif
                case "AVATAR_OPTIMIZER":
#if AVATAR_OPTIMIZER
                    return true;
#else
                    return false;
#endif
                case "LILTOON":
#if LILTOON
                    return true;
#else
                    return false;
#endif
                case "NDMF_MESH_SIMPLIFIER":
#if NDMF_MESH_SIMPLIFIER
                    return true;
#else
                    return false;
#endif
                case "GESTURE_MANAGER":
#if GESTURE_MANAGER
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // small helpers
        // ─────────────────────────────────────────────────────────────

        private static string DescribeUpdateState()
        {
            try
            {
                string latest = UpdateChecker.Latest?.version;
                if (string.IsNullOrEmpty(latest))
                    return "unknown (version check has not completed)";
                return UpdateChecker.IsUpdateAvailable()
                    ? $"{latest} available"
                    : $"up to date (latest published: {latest})";
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }

        private static string DescribeLicenseState()
        {
            try
            {
                if (UpdateChecker.IsLicenseCheckFailed) return "check failed";
                if (UpdateChecker.IsExpired)
                {
                    string date = UpdateChecker.ExpirationDateStr;
                    string reason = UpdateChecker.ExpirationReason;
                    return $"EXPIRED{(string.IsNullOrEmpty(date) ? "" : $" on {date}")}" +
                           $"{(string.IsNullOrEmpty(reason) ? "" : $" — {reason}")}";
                }
                return "ok";
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }

        private static string DescribeRenderPipeline()
        {
            try
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                return rp == null ? "Built-in" : rp.GetType().Name;
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }

        private static string GetProjectRoot()
        {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// Reports a field as "unknown" instead of failing the whole call. An omitted line would be
        /// read as "this does not apply here", which is a different and wrong claim.
        /// </summary>
        private static string SafeGet(Func<string> getter)
        {
            try
            {
                string value = getter();
                return string.IsNullOrEmpty(value) ? "unknown" : value;
            }
            catch (Exception ex)
            {
                return $"unknown ({ex.GetType().Name})";
            }
        }
    }
}
