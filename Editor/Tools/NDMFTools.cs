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
                foreach (var p in pluginTypes.Take(10))
                    sb.AppendLine($"    - {p.FullName}");
                if (pluginCount > 10) sb.AppendLine($"    ... +{pluginCount - 10} more");
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

        [AgentTool("Inspect NDMF's Error Report after a Manual Bake or build attempt. Lists every error/warning/info entry recorded by NDMF (typically populated by ModularAvatar / AAO / VRCFury during bake). Use this immediately after TriggerNDMFManualBake to diagnose why a build failed. Returns category counts and per-entry source plugin and message; degrades gracefully when no reports exist or NDMF is missing.")]
        public static string InspectNDMFErrorReport()
        {
            var errorReportType = FindNdmfType(ErrorReportSimpleNames);
            if (errorReportType == null)
                return "NDMF ErrorReport API not found. Either NDMF is not installed, or the API has moved.";

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
                return sbDiag.ToString().TrimEnd();
            }

            var sb = new StringBuilder();
            int total = 0;
            int errors = 0, warnings = 0, infos = 0;
            var lines = new List<string>();

            foreach (var report in reports)
            {
                if (report == null) continue;

                object plugin = GetMemberValue(report, "Plugin") ?? GetMemberValue(report, "AssemblyName");
                string pluginName = plugin?.ToString() ?? "?";

                var errorsCol = GetMemberValue(report, "Errors") as IEnumerable;
                if (errorsCol == null) continue;

                foreach (var err in errorsCol)
                {
                    if (err == null) continue;
                    total++;
                    object severity = GetMemberValue(err, "TheError")?.GetType().GetProperty("Severity")?.GetValue(GetMemberValue(err, "TheError"))
                                       ?? GetMemberValue(err, "Severity");
                    string sev = severity?.ToString() ?? "Error";
                    if (sev.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0) warnings++;
                    else if (sev.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0) infos++;
                    else errors++;

                    object inner = GetMemberValue(err, "TheError") ?? err;
                    string title = GetMemberValue(inner, "TitleKey") as string
                                   ?? InvokeMember(inner, "FormatTitle") as string
                                   ?? inner.GetType().Name;
                    string detail = InvokeMember(inner, "FormatDetails") as string
                                    ?? GetMemberValue(inner, "DetailsKey") as string;

                    string detailTrim = detail != null && detail.Length > 200 ? detail.Substring(0, 200) + "…" : detail;
                    lines.Add($"  [{sev}] {pluginName}: {title}{(detailTrim != null ? $" — {detailTrim}" : "")}");
                }
            }

            sb.AppendLine($"NDMF Error Report ({total} entries: {errors} error(s), {warnings} warning(s), {infos} info):");
            if (lines.Count == 0)
            {
                sb.AppendLine("  (no entries — bake has not produced any reports, or the report has been cleared)");
            }
            else
            {
                foreach (var l in lines.Take(50)) sb.AppendLine(l);
                if (lines.Count > 50) sb.AppendLine($"  … +{lines.Count - 50} more entries");
            }
            return sb.ToString().TrimEnd();
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
                try { return prop.GetValue(obj); } catch { return null; }
            }
            var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                try { return field.GetValue(obj); } catch { return null; }
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
            catch
            {
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
    }
}
