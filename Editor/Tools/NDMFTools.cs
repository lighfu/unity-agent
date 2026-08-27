using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// NDMF (Non-Destructive Modular Framework) introspection tools.
    /// These tools depend on `nadena.dev.ndmf` and degrade gracefully when it is absent.
    /// </summary>
    public static class NDMFTools
    {
        private const string NdmfAssemblyName = "nadena.dev.ndmf";
        private const string NdmfRuntimeAssemblyName = "nadena.dev.ndmf-runtime";
        // Fallback type-resolution: NDMF moves namespaces between minor versions, so we
        // primarily search by simple name within the NDMF assembly rather than hardcoded paths.
        private static readonly string[] PluginBaseSimpleNames = { "PluginBase", "Plugin`1" };
        private static readonly string[] ErrorReportSimpleNames = { "ErrorReport" };
        private static readonly string[] PreviewSessionSimpleNames = { "PreviewSession" };

        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool("List ALL parameters that will be present on the built avatar (post-build view), including those contributed by NDMF/Modular Avatar/VRCFury/PhysBone/Contact/etc. via the NDMF ParameterInfo API. Returns name, type, default, sync state, source component path and plugin. Falls back gracefully when NDMF is not installed. For the raw VRCExpressionParameters asset only, use ListVRCExpressionParameters.")]
        public static string ListNDMFParameters(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var paramInfoType = FindAnyType("nadena.dev.ndmf.ParameterInfo");
            if (paramInfoType == null)
                return "Error: NDMF (nadena.dev.ndmf.ParameterInfo) not found. Install NDMF (Non-Destructive Modular Framework) to use this tool.";

            try
            {
                var forUiField = paramInfoType.GetField("ForUI", BindingFlags.Public | BindingFlags.Static);
                var forUi = forUiField?.GetValue(null);
                if (forUi == null) return "Error: NDMF ParameterInfo.ForUI is unavailable.";

                MethodInfo getMethod = null;
                foreach (var m in paramInfoType.GetMethods())
                {
                    if (m.Name == "GetParametersForObject")
                    {
                        getMethod = m;
                        break;
                    }
                }
                if (getMethod == null) return "Error: NDMF GetParametersForObject method not found.";

                var args = getMethod.GetParameters().Length == 1
                    ? new object[] { go }
                    : new object[] { go, null };
                if (!(getMethod.Invoke(forUi, args) is IEnumerable result))
                    return "Error: NDMF returned no parameter list.";

                var providedType = FindAnyType("nadena.dev.ndmf.ProvidedParameter");
                if (providedType == null) return "Error: NDMF ProvidedParameter type missing.";

                var pEffectiveName = providedType.GetProperty("EffectiveName");
                var pOriginalName = providedType.GetProperty("OriginalName");
                var pNamespace = providedType.GetProperty("Namespace");
                var pSource = providedType.GetProperty("Source");
                var pPlugin = providedType.GetProperty("Plugin");
                var pParameterType = providedType.GetProperty("ParameterType");
                var pIsAnimatorOnly = providedType.GetProperty("IsAnimatorOnly");
                var pIsHidden = providedType.GetProperty("IsHidden");
                var pWantSynced = providedType.GetProperty("WantSynced");
                var pDefaultValue = providedType.GetProperty("DefaultValue");
                var pBitUsage = providedType.GetProperty("BitUsage");

                var sb = new StringBuilder();
                var entries = new List<string>();
                int total = 0;
                int totalCost = 0;

                foreach (var pp in result)
                {
                    if (pp == null) continue;
                    total++;

                    string name = pEffectiveName?.GetValue(pp) as string ?? "?";
                    string original = pOriginalName?.GetValue(pp) as string;
                    string ns = pNamespace?.GetValue(pp)?.ToString() ?? "?";
                    var source = pSource?.GetValue(pp) as Component;
                    var plugin = pPlugin?.GetValue(pp);
                    var paramType = pParameterType?.GetValue(pp);
                    bool isAnimatorOnly = (pIsAnimatorOnly?.GetValue(pp) as bool?) ?? false;
                    bool isHidden = (pIsHidden?.GetValue(pp) as bool?) ?? false;
                    bool wantSynced = (pWantSynced?.GetValue(pp) as bool?) ?? false;
                    float? defaultV = pDefaultValue?.GetValue(pp) as float?;
                    int bits = (pBitUsage?.GetValue(pp) as int?) ?? 0;
                    totalCost += bits;

                    string typeStr = paramType?.ToString() ?? "Untyped";
                    string nameDisplay = (original != null && original != name) ? $"{name} (orig:{original})" : name;
                    string defaultStr = defaultV.HasValue ? defaultV.Value.ToString("F2") : "(none)";
                    string flags = string.Join(",",
                        new[]
                        {
                            wantSynced ? "Synced" : null,
                            isAnimatorOnly ? "AnimatorOnly" : null,
                            isHidden ? "Hidden" : null,
                            ns == "PhysBonesPrefix" ? "PhysBonesPrefix" : null,
                        }.Where(x => x != null));
                    string sourceStr = source != null ? $"{source.GetType().Name} on {FormatScenePath(source)}" : "?";
                    string pluginName = plugin != null ? $", plugin={plugin.GetType().Name}" : "";
                    entries.Add($"  {nameDisplay} ({typeStr}) = {defaultStr} [{flags}] (cost: {bits}) ← {sourceStr}{pluginName}");
                }

                sb.AppendLine($"NDMF Parameters on '{avatarRootName}' (post-build view, {total} total):");
                foreach (var e in entries) sb.AppendLine(e);
                sb.AppendLine($"  NDMF Synced Cost: {totalCost}/256 bits");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return $"Error during NDMF introspection: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // NDMF runtime info
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Detect installed NDMF (Non-Destructive Modular Framework) runtime info: assembly version, location, and high-level plugin/pass counts. Use this to answer 'is NDMF installed?' or 'which version of NDMF is loaded?'. Returns a one-paragraph summary; degrades gracefully when NDMF is absent.")]
        public static string GetNDMFInfo()
        {
            var assembly = FindNdmfAssembly();
            if (assembly == null)
                return "NDMF not installed: 'nadena.dev.ndmf' assembly was not found in the loaded AppDomain.";

            var sb = new StringBuilder();
            sb.AppendLine("NDMF Runtime Info:");
            var name = assembly.GetName();
            sb.AppendLine($"  Assembly: {name.Name}");
            if (name.Version != null) sb.AppendLine($"  Version : {name.Version}");
            try { sb.AppendLine($"  Location: {assembly.Location}"); } catch { /* dynamic asm */ }

            int pluginCount = TryEnumeratePluginTypes(out var pluginTypes) ? pluginTypes.Count : 0;
            sb.AppendLine($"  Plugins detected: {pluginCount}");
            if (pluginCount > 0)
            {
                foreach (var p in pluginTypes)
                    sb.AppendLine($"    - {p.FullName}");
            }

            var processAvatarType = FindNdmfType("AvatarProcessor");
            var paramInfoType = FindNdmfType("ParameterInfo");
            var errorReportType = FindNdmfType(ErrorReportSimpleNames);
            var previewSessionType = FindNdmfType(PreviewSessionSimpleNames);

            sb.AppendLine($"  ProcessAvatar API: {(processAvatarType != null ? processAvatarType.FullName : "missing")}");
            sb.AppendLine($"  ParameterInfo API: {(paramInfoType != null ? paramInfoType.FullName : "missing")}");
            sb.AppendLine($"  ErrorReport API : {(errorReportType != null ? errorReportType.FullName : "missing")}");
            sb.AppendLine($"  Preview API     : {(previewSessionType != null ? previewSessionType.FullName : "missing")}");

            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────────
        // Plugin Registry (Plugin<T> / PluginBase)
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Enumerate every NDMF Plugin registered in the current Unity project via the official Plugin Registry (subclasses of nadena.dev.ndmf.fluent.PluginBase / Plugin<T>). For each plugin, returns the qualified name, display name, source assembly, and declared passes when introspectable. More accurate than ListNDMFPlugins (which uses string matching). Use to answer 'which NDMF plugins will run during build?'.")]
        public static string ListNDMFPluginRegistry()
        {
            if (FindNdmfAssembly() == null)
                return "NDMF not installed: 'nadena.dev.ndmf' assembly was not found.";

            if (!TryEnumeratePluginTypes(out var pluginTypes))
                return "Error: NDMF PluginBase type not found. The plugin registry API may have changed.";

            var sb = new StringBuilder();
            sb.AppendLine($"NDMF Plugin Registry ({pluginTypes.Count} plugin(s)):");

            int idx = 0;
            foreach (var pluginType in pluginTypes.OrderBy(t => t.FullName))
            {
                idx++;
                sb.AppendLine($"  [{idx}] {pluginType.FullName}");
                sb.AppendLine($"      Assembly: {pluginType.Assembly.GetName().Name}");

                object instance = TryInstantiate(pluginType);
                if (instance != null)
                {
                    string qualified = GetMemberValue(instance, "QualifiedName") as string;
                    string display = GetMemberValue(instance, "DisplayName") as string;
                    if (!string.IsNullOrEmpty(qualified)) sb.AppendLine($"      QualifiedName: {qualified}");
                    if (!string.IsNullOrEmpty(display)) sb.AppendLine($"      DisplayName  : {display}");
                }
                else
                {
                    sb.AppendLine("      (instance unavailable — QualifiedName/DisplayName cannot be inspected)");
                }
            }

            if (pluginTypes.Count == 0)
                sb.AppendLine("  No NDMF plugins are registered. Install Modular Avatar / Avatar Optimizer / VRCFury etc.");

            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────────
        // Error Report
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Inspect NDMF's Error Report after a Manual Bake or build attempt. This is the ONLY reliable way to " +
                   "tell whether a bake succeeded: NDMF returns a baked GameObject even when processing failed, so " +
                   "neither the returned object nor the word 'Success' proves anything. " +
                   "Counts are reported per NDMF severity (internalError / error / nonFatal / information) plus " +
                   "uploadBlocking=true|false — NDMF blocks the avatar upload for Error and InternalError only. " +
                   "severity: 'all' (default) | 'internalError' | 'error' | 'nonFatal' | 'information'. " +
                   "maxEntries: cap on listed entries (default 50, max 500). " +
                   "Each entry carries the source plugin, the pass name, the avatar, the message, and the hierarchy " +
                   "path of every scene object the error points at. Degrades gracefully when NDMF is missing.")]
        public static string InspectNDMFErrorReport(int maxEntries = 50, string severity = "all")
        {
            if (maxEntries <= 0) maxEntries = 50;
            if (maxEntries > 500) maxEntries = 500;

            string filter = ParseNdmfSeverityFilter(severity);
            if (filter == null)
                return $"Error: unknown severity '{severity}'. Use all | internalError | error | nonFatal | information.";

            if (!TryCollectNdmfErrors(out var entries, out string diagnostic))
                return diagnostic;

            var counts = new NdmfErrorCounts();
            foreach (var e in entries) Tally(ref counts, e.severity);

            var sb = new StringBuilder();
            sb.AppendLine($"NDMF Error Report: {counts.Format()}");

            if (entries.Count == 0)
            {
                sb.AppendLine("  (no entries — bake has not produced any reports, or the report has been cleared)");
                return sb.ToString().TrimEnd();
            }

            var shown = filter == "all"
                ? entries
                : entries.Where(e => e.severity == filter).ToList();

            if (filter != "all")
                sb.AppendLine($"  (severity filter '{filter}': {shown.Count} of {entries.Count} entries)");

            if (shown.Count == 0)
            {
                sb.AppendLine("  (no entries at this severity)");
                return sb.ToString().TrimEnd();
            }

            foreach (var e in shown.Take(maxEntries))
            {
                var head = new StringBuilder($"  [{e.severity}]");
                head.Append(string.IsNullOrEmpty(e.plugin) ? " (no plugin context)" : " " + e.plugin);
                if (!string.IsNullOrEmpty(e.pass)) head.Append(" / " + e.pass);
                if (!string.IsNullOrEmpty(e.avatar)) head.Append(" @" + e.avatar);
                sb.AppendLine(head.ToString());
                sb.AppendLine("      " + e.message);
                foreach (var path in e.objectPaths) sb.AppendLine("      → " + path);
            }
            if (shown.Count > maxEntries)
                sb.AppendLine($"  … +{shown.Count - maxEntries} more entries not listed (raise maxEntries to see them)");

            return sb.ToString().TrimEnd();
        }

        // ── Error report collection ──────────────────────────────────────────

        /// <summary>
        /// NDMF's own severities, most severe first. Matched by exact enum name: a substring test
        /// folds "InternalError" into "Error" and makes uploadBlocking unanswerable.
        /// </summary>
        private static readonly string[] NdmfSeverityNames =
            { "InternalError", "Error", "NonFatal", "Information" };

        /// <summary>Per-severity tallies over NDMF's error report.</summary>
        internal struct NdmfErrorCounts
        {
            public int internalError;
            public int error;
            public int nonFatal;
            public int information;
            public int unknown;
            public int total;

            /// <summary>NDMF blocks the avatar upload for Error and InternalError only.</summary>
            public bool UploadBlocking => internalError > 0 || error > 0;

            public string Format() =>
                $"internalError={internalError}, error={error}, nonFatal={nonFatal}, information={information}"
                + (unknown > 0 ? $", unknown={unknown}" : "")
                + $", total={total}, uploadBlocking={(UploadBlocking ? "true" : "false")}";
        }

        private class NdmfErrorEntry
        {
            public string severity;
            public string plugin;
            public string pass;
            public string avatar;
            public string message;
            public readonly List<string> objectPaths = new List<string>();
        }

        /// <summary>
        /// Severity tallies only. Lets other tools report the outcome of a bake without
        /// re-implementing the reflection walk.
        /// </summary>
        internal static bool TryGetErrorReportCounts(out NdmfErrorCounts counts, out string diagnostic)
        {
            counts = default;
            if (!TryCollectNdmfErrors(out var entries, out diagnostic)) return false;
            foreach (var e in entries) Tally(ref counts, e.severity);
            return true;
        }

        private static void Tally(ref NdmfErrorCounts counts, string severity)
        {
            switch (severity)
            {
                case "InternalError": counts.internalError++; break;
                case "Error": counts.error++; break;
                case "NonFatal": counts.nonFatal++; break;
                case "Information": counts.information++; break;
                default: counts.unknown++; break;
            }
            counts.total++;
        }

        /// <summary>Returns the canonical severity name, "all", or null when unrecognised.</summary>
        private static string ParseNdmfSeverityFilter(string severity)
        {
            string s = (severity ?? "all").Trim();
            if (s.Length == 0 || string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)) return "all";
            if (string.Equals(s, "info", StringComparison.OrdinalIgnoreCase)) return "Information";
            foreach (var name in NdmfSeverityNames)
                if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return name;
            return null;
        }

        private static bool TryCollectNdmfErrors(out List<NdmfErrorEntry> entries, out string diagnostic)
        {
            entries = null;

            var errorReportType = FindNdmfType(ErrorReportSimpleNames);
            if (errorReportType == null)
            {
                diagnostic = "NDMF ErrorReport API not found. Either NDMF is not installed, or the API has moved.";
                return false;
            }

            object reportsValue = TryFindStaticEnumerable(errorReportType,
                new[] { "Reports", "_reports", "AllReports", "CurrentReports" });
            if (!(reportsValue is IEnumerable reports))
            {
                var sbDiag = new StringBuilder();
                sbDiag.AppendLine($"NDMF ErrorReport ({errorReportType.FullName}) found but no known reports collection accessor exists.");
                sbDiag.AppendLine("Available static members:");
                foreach (var m in errorReportType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m is FieldInfo fi) sbDiag.AppendLine($"  field {fi.Name} : {fi.FieldType.Name}");
                    else if (m is PropertyInfo pi) sbDiag.AppendLine($"  prop  {pi.Name} : {pi.PropertyType.Name}");
                }
                diagnostic = sbDiag.ToString().TrimEnd();
                return false;
            }

            entries = new List<NdmfErrorEntry>();
            foreach (var report in reports)
            {
                if (report == null) continue;
                string avatar = GetMemberValue(report, "AvatarName") as string;

                if (!(GetMemberValue(report, "Errors") is IEnumerable errorsCol)) continue;

                foreach (var ctx in errorsCol)
                {
                    if (ctx == null) continue;

                    // ErrorContext is a struct carrying { TheError, Plugin, PassName,
                    // ExtensionContext }. The plugin hangs off the CONTEXT, not off the report —
                    // reading report.Plugin is what made every entry print its plugin as "?".
                    object inner = GetMemberValue(ctx, "TheError") ?? ctx;

                    var entry = new NdmfErrorEntry
                    {
                        severity = CanonicalNdmfSeverity(
                            GetMemberValue(inner, "Severity") ?? GetMemberValue(ctx, "Severity")),
                        plugin = DescribeNdmfPlugin(GetMemberValue(ctx, "Plugin")),
                        pass = GetMemberValue(ctx, "PassName") as string,
                        avatar = avatar,
                        message = DescribeNdmfError(inner),
                    };
                    CollectNdmfReferencePaths(inner, entry.objectPaths);
                    entries.Add(entry);
                }
            }

            diagnostic = null;
            return true;
        }

        private static string CanonicalNdmfSeverity(object severityValue)
        {
            if (severityValue == null) return "Unknown";
            string name = severityValue.ToString();
            foreach (var s in NdmfSeverityNames)
                if (string.Equals(name, s, StringComparison.OrdinalIgnoreCase)) return s;
            return "Unknown";
        }

        private static string DescribeNdmfPlugin(object plugin)
        {
            if (plugin == null) return null;
            return GetMemberValue(plugin, "DisplayName") as string
                   ?? GetMemberValue(plugin, "QualifiedName") as string
                   ?? plugin.GetType().Name;
        }

        private static string DescribeNdmfError(object inner)
        {
            if (inner == null) return "(no error object)";

            string title = InvokeMember(inner, "FormatTitle") as string
                           ?? GetMemberValue(inner, "TitleKey") as string;
            string detail = InvokeMember(inner, "FormatDetails") as string
                            ?? GetMemberValue(inner, "DetailsKey") as string;

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(detail))
            {
                // Every IError implements ToMessage() for the Unity log; fall back to it rather
                // than printing a bare type name.
                return InvokeMember(inner, "ToMessage") as string ?? inner.GetType().Name;
            }

            if (string.IsNullOrEmpty(title)) title = inner.GetType().Name;
            if (!string.IsNullOrEmpty(detail) && detail.Length > 200) detail = detail.Substring(0, 200) + "…";
            return string.IsNullOrEmpty(detail) ? title : $"{title} — {detail}";
        }

        private static void CollectNdmfReferencePaths(object inner, List<string> into)
        {
            var refs = GetMemberValue(inner, "References") as IEnumerable
                       ?? GetMemberValue(inner, "_references") as IEnumerable;
            if (refs == null) return;

            foreach (var r in refs)
            {
                if (r == null) continue;

                // ObjectReference.Path is the path relative to the avatar root and stays readable
                // after the bake has destroyed the clone, so prefer it over walking the transform.
                string path = GetMemberValue(r, "Path") as string;
                if (string.IsNullOrEmpty(path))
                {
                    var obj = GetMemberValue(r, "Object") as UnityEngine.Object;
                    var go = obj as GameObject ?? (obj as Component)?.gameObject;
                    if (go == null) continue;   // an asset, not a scene object — no hierarchy path
                    path = AvatarAnatomyTools.GetHierarchyPathInternal(go);
                }
                if (!string.IsNullOrEmpty(path) && !into.Contains(path)) into.Add(path);
            }
        }

        [AgentTool("Clear the NDMF Error Report buffer. Useful before re-running TriggerNDMFManualBake so InspectNDMFErrorReport only shows fresh entries. Returns the number of cleared entries; degrades gracefully when NDMF is missing.")]
        public static string ClearNDMFErrorReport()
        {
            var errorReportType = FindNdmfType(ErrorReportSimpleNames);
            if (errorReportType == null)
                return "NDMF ErrorReport API not found.";

            var clearMethod = errorReportType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static)
                              ?? errorReportType.GetMethod("ClearReports", BindingFlags.Public | BindingFlags.Static);
            if (clearMethod == null)
                return "NDMF ErrorReport.Clear() not available on this NDMF version.";

            try
            {
                clearMethod.Invoke(null, null);
                return "Success: NDMF Error Report cleared.";
            }
            catch (Exception ex)
            {
                return $"Error: failed to clear NDMF Error Report: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // NDMF Console / debug windows
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Open the NDMF Console editor window (Tools/NDM Framework/Show NDMF Console) which visualizes plugin/pass execution history and timings. Use this when the user wants to inspect the most recent build trace interactively. Returns whether the menu item was successfully executed.")]
        public static string OpenNDMFConsole()
        {
            string[] menuCandidates =
            {
                "Tools/NDM Framework/Show NDMF Console",
                "Tools/NDM Framework/Debug Tools/Show NDMF Console",
                "Tools/NDM Framework/NDMF Console",
            };

            foreach (var menu in menuCandidates)
            {
                if (EditorApplication.ExecuteMenuItem(menu))
                    return $"Success: opened '{menu}'.";
            }

            return "Error: NDMF Console menu item not found. Either NDMF is missing or the menu path has changed.";
        }

        // ─────────────────────────────────────────────────────────────────
        // NDMF Preview System
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Toggle the global NDMF Preview System on/off. When enabled, NDMF-aware plugins (Modular Avatar, TexTransTool, ndmf-mesh-simplifier, VRCQuestTools, etc.) project their build-time output into the scene view via IRenderFilter without baking. Pass enabled=true to enable, false to disable. Internally calls NDMFPreview.ToggleEnablePreviews when the current state differs from the requested state. Returns 'no change' if already in the desired state.")]
        public static string SetNDMFPreviewEnabled(bool enabled)
        {
            var ndmfPreviewType = FindNdmfType("NDMFPreview");
            if (ndmfPreviewType == null)
                return "Error: nadena.dev.ndmf.preview.NDMFPreview type not found.";

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            // Read current state via EnablePreviewsUI (Boolean static property).
            var stateProp = ndmfPreviewType.GetProperty("EnablePreviewsUI", flags);
            if (stateProp == null)
                return "Error: NDMFPreview.EnablePreviewsUI property not found.";

            bool current;
            try { current = (bool)(stateProp.GetValue(null) ?? false); }
            catch (Exception ex) { return $"Error reading current preview state: {ex.GetType().Name}: {ex.Message}"; }

            if (current == enabled)
                return $"NDMF preview is already {(enabled ? "ENABLED" : "DISABLED")} (no change).";

            // Prefer ToggleEnablePreviews() — that's NDMF's own internal toggle path.
            var toggleMethod = ndmfPreviewType.GetMethod("ToggleEnablePreviews", flags, null, Type.EmptyTypes, null);
            if (toggleMethod != null)
            {
                try
                {
                    toggleMethod.Invoke(null, null);
                    bool now = (bool)(stateProp.GetValue(null) ?? false);
                    return $"Success: NDMF preview is now {(now ? "ENABLED" : "DISABLED")} (via NDMFPreview.ToggleEnablePreviews).";
                }
                catch (Exception ex)
                {
                    return $"Error invoking ToggleEnablePreviews: {ex.GetType().Name}: {ex.Message}";
                }
            }

            // Fallback: try writing the property directly if writable.
            if (stateProp.CanWrite)
            {
                try
                {
                    stateProp.SetValue(null, enabled);
                    return $"Success: NDMF preview is now {(enabled ? "ENABLED" : "DISABLED")} (via direct write).";
                }
                catch (Exception ex)
                {
                    return $"Error writing EnablePreviewsUI: {ex.GetType().Name}: {ex.Message}";
                }
            }

            return "Error: NDMFPreview exposes neither ToggleEnablePreviews() nor a writable EnablePreviewsUI on this version.";
        }

        [AgentTool("List every IRenderFilter currently registered with the NDMF Preview System. Each filter represents a plugin contribution to the live scene preview (e.g. MA Material Setter, AAO mesh changes). Use this to answer 'what is touching my preview?'. Degrades gracefully when NDMF preview is missing or the registry API has changed.")]
        public static string ListNDMFPreviewFilters()
        {
            var filterType = FindNdmfType("IRenderFilter");
            if (filterType == null)
                return "NDMF Preview API not found (IRenderFilter).";

            var filters = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (filterType.IsAssignableFrom(t)) filters.Add(t);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"NDMF Preview Filters ({filters.Count}):");
            foreach (var f in filters.OrderBy(t => t.FullName))
                sb.AppendLine($"  - {f.FullName} ({f.Assembly.GetName().Name})");
            if (filters.Count == 0)
                sb.AppendLine("  (none — no IRenderFilter implementations are loaded)");
            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers (private)
        // ─────────────────────────────────────────────────────────────────

        private static Assembly FindNdmfAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name == NdmfAssemblyName || name == NdmfRuntimeAssemblyName)
                    return asm;
            }
            return null;
        }

        // Search any nadena.dev.ndmf* assembly for a type whose simple name matches one of the
        // candidates. Resilient to namespace shifts across NDMF minor versions.
        private static Type FindNdmfType(params string[] simpleNames)
        {
            var ndmfAsms = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("nadena.dev.ndmf", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var asm in ndmfAsms)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    foreach (var name in simpleNames)
                    {
                        if (t.Name == name) return t;
                    }
                }
            }
            return null;
        }

        private static bool TryEnumeratePluginTypes(out List<Type> result)
        {
            result = new List<Type>();
            var pluginBase = FindNdmfType(PluginBaseSimpleNames);
            if (pluginBase == null) return false;

            // If we matched the closed generic Plugin<T>, walk up to its declaring open generic so
            // IsAssignableFrom works for any T. Otherwise treat the resolved type as the base.
            if (pluginBase.IsGenericType && !pluginBase.IsGenericTypeDefinition)
                pluginBase = pluginBase.GetGenericTypeDefinition();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                    if (IsSubclassOfNdmfPlugin(t, pluginBase)) result.Add(t);
                }
            }
            return true;
        }

        // Walks the inheritance chain looking for either a direct assignment or a closed generic
        // Plugin<T> match. Needed because Plugin<T> is the open generic and concrete plugins
        // declare `class MyPlugin : Plugin<MyPlugin>`.
        private static bool IsSubclassOfNdmfPlugin(Type candidate, Type pluginBase)
        {
            if (pluginBase.IsAssignableFrom(candidate)) return true;
            if (!pluginBase.IsGenericTypeDefinition) return false;

            for (var cur = candidate.BaseType; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                if (cur.IsGenericType && cur.GetGenericTypeDefinition() == pluginBase) return true;
            }
            return false;
        }

        private static object TryInstantiate(Type type)
        {
            try
            {
                var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                                   ?? type.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp != null)
                {
                    var v = instanceProp.GetValue(null);
                    if (v != null) return v;
                }
                return Activator.CreateInstance(type, nonPublic: true);
            }
            catch
            {
                return null;
            }
        }

        // Probe a type for a static field or property whose value is an IEnumerable, trying
        // each candidate name. Returns null if nothing matches.
        private static object TryFindStaticEnumerable(Type type, string[] candidateNames)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var name in candidateNames)
            {
                var prop = type.GetProperty(name, flags);
                if (prop != null)
                {
                    try
                    {
                        var v = prop.GetValue(null);
                        if (v is IEnumerable) return v;
                    }
                    catch { /* ignore */ }
                }
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        var v = field.GetValue(null);
                        if (v is IEnumerable) return v;
                    }
                    catch { /* ignore */ }
                }
            }
            return null;
        }

        private static object GetMemberValue(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanRead)
            {
                try { return prop.GetValue(obj); }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.Tool, $"NDMFTools.GetMemberValue: prop '{t.Name}.{name}' threw {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
            var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                try { return field.GetValue(obj); }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.Tool, $"NDMFTools.GetMemberValue: field '{t.Name}.{name}' threw {ex.GetType().Name}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private static object InvokeMember(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var method = obj.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return method?.Invoke(obj, null);
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool, $"NDMFTools.InvokeMember: '{obj.GetType().Name}.{name}()' threw {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static string FormatScenePath(UnityEngine.Object obj)
        {
            if (obj == null) return "None";
            Transform t = obj is GameObject go ? go.transform : (obj as Component)?.transform;
            if (t == null) return obj.name;

            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
            {
                sb.Insert(0, "/");
                sb.Insert(0, p.name);
            }
            return sb.ToString();
        }

        private static Type FindAnyType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName); } catch { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// True when the NDMF ParameterInfo API is loadable in the current AppDomain.
        /// Used by sibling tools to surface a "call ListNDMFParameters next" hint only when meaningful.
        internal static bool IsNDMFAvailable() => FindAnyType("nadena.dev.ndmf.ParameterInfo") != null;
    }
}
