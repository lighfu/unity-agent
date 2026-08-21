using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Reflection;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class ScriptExecutionTools
    {
        [AgentTool(@"Execute arbitrary C# code in the Editor as a last resort when no existing tool covers the operation.
The code runs inside a static Execute() method. Use 'return' to return a result string.
Always requires user confirmation.

Debug.Log is NOT the return channel. A script that only logs is reported as '(no return value)' and the
lines it wrote are repeated back, so a forgotten 'return' costs no re-run — but 'return' is still the way
to get a value out.

usings: ';' separated extra namespaces to add on top of the defaults
  (System, System.Linq, System.Collections.Generic, System.Text, UnityEngine, UnityEditor).
additionalReferences: ';' separated assembly names to add to the compiler's reference set.
  The default set is a whitelist (see ToolUtility.IsScriptReference) because referencing all
  300+ loaded assemblies overflows the Windows command-line limit. If a BCL type fails to
  resolve — 'HashSet<>' and friends are the usual suspects — pass the assembly that defines it,
  e.g. additionalReferences='System.Core'. Compile errors list the current reference set so you
  can see what was actually available.

Reflection tip: prefer InvokeMember for one-off internal API calls. It always uses
BindingFlags.Instance|Static|Public|NonPublic, so the classic 'forgot Instance, silently got null,
then NullReferenceException' failure cannot happen.")]
        public static string RunEditorScript(string code, string usings = "", string additionalReferences = "")
        {
            if (string.IsNullOrWhiteSpace(code))
                return "Error: No code provided.";

            // Always require confirmation
            if (!AgentSettings.RequestConfirmation(
                "C#スクリプトを実行",
                $"以下のコードを実行します:\n\n{code}"))
                return "Cancelled: User denied script execution.";

            Debug.Log($"[UnityAgent] RunEditorScript executing:\n{code}");

            if (!TryCompileScript(code, usings, additionalReferences, out var method, out string compileError))
                return compileError;

            // Execute. The capture starts here, after the log above, so this tool's own banner is not
            // reported back as if the script had written it.
            var console = new ScriptConsoleCapture();
            try
            {
                object result = method.Invoke(null, null);
                return DescribeScriptResult(result, console);
            }
            catch (TargetInvocationException tex)
            {
                var inner = tex.InnerException;
                return $"Runtime Error: {inner?.Message ?? tex.Message}\n{inner?.StackTrace ?? tex.StackTrace}";
            }
            catch (Exception ex)
            {
                return $"Runtime Error: {ex.Message}\n{ex.StackTrace}";
            }
            finally
            {
                console.Dispose();
            }
        }

        // ── console capture ──────────────────────────────────────────────────

        /// <summary>
        /// Keeps the console lines a script writes while it runs.
        ///
        /// A script whose only output was Debug.Log used to come back as a bare "Script executed
        /// successfully.", which reads as "it ran and matched nothing" rather than "you forgot to
        /// return". The two look identical from the outside, and telling them apart costs one wasted
        /// run plus one lookup.
        ///
        /// Attached for the duration of ONE invoke and detached in a finally: the handler is global, so
        /// a listener left behind would file somebody else's logs under this script. Anything the editor
        /// logs from the main thread while the script runs is captured too — a script is not the only
        /// thing that can write to the console, and this makes no attempt to tell them apart.
        /// </summary>
        internal sealed class ScriptConsoleCapture : IDisposable
        {
            private const int MaxKeptLines = 20;
            private const int MaxLineLength = 500;

            private readonly List<string> _lines = new List<string>();
            private bool _detached;

            /// <summary>Every line seen, including the ones past <see cref="MaxKeptLines"/>.</summary>
            public int Count { get; private set; }

            public ScriptConsoleCapture()
            {
                Application.logMessageReceived += OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                Count++;
                if (_lines.Count >= MaxKeptLines) return;
                string text = condition ?? "";
                if (text.Length > MaxLineLength)
                    text = text.Substring(0, MaxLineLength) + "... (line truncated)";
                _lines.Add($"[{type}] {text}");
            }

            public void Dispose()
            {
                if (_detached) return;
                _detached = true;
                Application.logMessageReceived -= OnLog;
            }

            /// <summary>
            /// What to append to a script that returned nothing, or "" when it wrote nothing either.
            /// </summary>
            public string DescribeForEmptyResult()
            {
                if (Count == 0) return "";

                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine($"Note: {Count} console line(s) were written while this ran. Console output is " +
                              "NOT the return channel — use `return <string>` to get a value back through " +
                              "this tool. The lines are repeated here so this run does not have to be redone:");
                foreach (var line in _lines) sb.AppendLine("  " + line);
                if (Count > _lines.Count)
                    sb.AppendLine($"  ... and {Count - _lines.Count} more line(s); see the Unity console.");
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Turns what a script body produced into this tool's result. Shared by the synchronous runner
        /// and the job runner so the two cannot drift in what "success" reads like.
        /// </summary>
        internal static string DescribeScriptResult(object returned, ScriptConsoleCapture console)
        {
            if (returned != null) return returned.ToString();
            return "Script executed successfully. (no return value)" +
                   (console != null ? console.DescribeForEmptyResult() : "");
        }

        [AgentTool(@"Call a method, or read/write a property or field, by reflection — including internal
and private members. A declarative alternative to hand-writing reflection in RunEditorScript.

typeName: full type name ('UnityEditor.SceneView') or a plain class name to search loaded assemblies for.
memberName: method, property or field name.
args: ';' separated arguments, each 'type:value'. Supported types:
  string:hello   int:3   float:1.5   bool:true   null:   enum:UnityEditor.BuildTarget.StandaloneWindows64
  For a property or field, a single argument means WRITE; no arguments means READ.
target: how to find the instance. Empty = static member.
  'window:<title substring>'  an open EditorWindow
  'gameobject:<name>'         a GameObject in the scene
  'component:<name>/<Type>'   a component on a GameObject
  'asset:<assetPath>'         an asset loaded from disk

BindingFlags are always Instance|Static|Public|NonPublic, applied at every level of the inheritance
chain, so an internal instance member is never silently missed (the failure mode where omitting
Instance returns null and the next line throws NullReferenceException).

DANGEROUS: this is arbitrary code execution — it can call any method and write any field the editor
can reach. Same risk tier as RunEditorScript.",
            Risk = ToolRisk.Dangerous)]
        public static string InvokeMember(string typeName, string memberName, string args = "", string target = "")
        {
            if (string.IsNullOrWhiteSpace(typeName)) return "Error: typeName is required.";
            if (string.IsNullOrWhiteSpace(memberName)) return "Error: memberName is required.";

            if (!AgentSettings.RequestConfirmation(
                "リフレクション呼び出し",
                $"{typeName}.{memberName}({args})\ntarget: {(string.IsNullOrEmpty(target) ? "(static)" : target)}"))
                return "Cancelled: User denied reflection call.";

            if (!TryResolveType(typeName, out Type type, out string typeErr)) return typeErr;
            if (!TryParseArgs(args, out object[] parsedArgs, out string argErr)) return argErr;
            if (!TryResolveTarget(target, type, out object instance, out string targetErr)) return targetErr;

            // Property / field before method: a name collision is rare, and a data member is the
            // cheaper interpretation to get wrong.
            var property = FindMember(type, t => t.GetProperty(memberName, DeclaredFlags));
            if (property != null)
                return AccessProperty(type, property, instance, parsedArgs);

            var field = FindMember(type, t => t.GetField(memberName, DeclaredFlags));
            if (field != null)
                return AccessField(type, field, instance, parsedArgs);

            var methods = CollectMethods(type, memberName);
            if (methods.Count == 0)
            {
                var candidates = type.GetMembers(DeclaredFlags | BindingFlags.FlattenHierarchy)
                    .Where(m => m.Name.IndexOf(memberName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => $"{m.MemberType} {m.Name}")
                    .Distinct().Take(15).ToArray();
                string hint = candidates.Length > 0
                    ? $" Similar members: {string.Join(", ", candidates)}"
                    : "";
                return $"Error: '{type.FullName}' has no member '{memberName}'.{hint}";
            }

            var sameArity = methods.Where(m => m.GetParameters().Length == parsedArgs.Length).ToList();
            if (sameArity.Count == 0)
            {
                return $"Error: no overload of '{memberName}' takes {parsedArgs.Length} argument(s). " +
                       $"Available: {string.Join(" | ", methods.Select(DescribeSignature))}";
            }

            // Arity alone is not enough: Foo(string) and Foo(int) both accept one argument, and
            // Convert.ChangeType would happily turn int:1 into "1" for whichever overload happened
            // to come first. Prefer the ones whose parameter types actually match.
            MethodInfo match;
            if (sameArity.Count == 1)
            {
                match = sameArity[0];
            }
            else
            {
                var exact = sameArity.Where(m => ParametersMatchExactly(m, parsedArgs)).ToList();
                if (exact.Count == 1)
                {
                    match = exact[0];
                }
                else
                {
                    return $"Error: '{memberName}' is ambiguous for the given arguments " +
                           $"({exact.Count} exact / {sameArity.Count} by arity). " +
                           $"Candidates: {string.Join(" | ", sameArity.Select(DescribeSignature))}. " +
                           "Give argument types that match exactly (e.g. int:3 vs string:3).";
                }
            }

            if (!match.IsStatic && instance == null)
                return $"Error: '{memberName}' is an instance method but no target was resolved. " +
                       "Pass target='window:<title>' / 'gameobject:<name>' / 'component:<name>/<Type>' / 'asset:<path>'.";

            object[] coerced;
            try { coerced = CoerceArgs(parsedArgs, match.GetParameters()); }
            catch (Exception ex) { return $"Error: argument conversion failed: {ex.Message}"; }

            try
            {
                object result = match.Invoke(match.IsStatic ? null : instance, coerced);
                return $"Success: {type.Name}.{memberName} returned {Describe(result)}";
            }
            catch (TargetInvocationException tex)
            {
                var inner = tex.InnerException;
                return $"Runtime Error in {type.Name}.{memberName}: {inner?.Message ?? tex.Message}\n{inner?.StackTrace}";
            }
            catch (Exception ex)
            {
                return $"Error invoking {type.Name}.{memberName}: {ex.Message}";
            }
        }

        // ── reflection helpers ───────────────────────────────────────────────

        /// <summary>
        /// Instance + static, public + non-public, declared on ONE type only. BindingFlags.NonPublic
        /// does not walk the inheritance chain and FlattenHierarchy only surfaces statics, so the
        /// only way to reach a private instance member of a base class is to search each level.
        /// </summary>
        private const BindingFlags DeclaredFlags =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static T FindMember<T>(Type type, Func<Type, T> lookup) where T : class
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var found = lookup(t);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>All overloads of a name across the inheritance chain, base-class copies of an
        /// override excluded (the most-derived declaration wins).</summary>
        private static List<MethodInfo> CollectMethods(Type type, string memberName)
        {
            var result = new List<MethodInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var m in t.GetMethods(DeclaredFlags))
                {
                    if (m.Name != memberName) continue;
                    string signature = DescribeSignature(m);
                    if (seen.Add(signature)) result.Add(m);
                }
            }
            return result;
        }

        private static string DescribeSignature(MethodInfo m) =>
            $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})";

        /// <summary>True when every supplied argument is already of the parameter's type (or null
        /// for a reference/nullable parameter) — i.e. no conversion is needed to call it.</summary>
        private static bool ParametersMatchExactly(MethodInfo method, object[] args)
        {
            var parameters = method.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                var expected = parameters[i].ParameterType;
                if (args[i] == null)
                {
                    if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null) return false;
                    continue;
                }
                if (expected.IsEnum && args[i].GetType() == typeof(int)) continue;
                if (!expected.IsInstanceOfType(args[i])) return false;
            }
            return true;
        }

        private static string AccessProperty(Type type, PropertyInfo property, object instance, object[] args)
        {
            if (args.Length > 1)
                return $"Error: '{property.Name}' is a property — pass one argument to write it, or none to read it " +
                       $"(got {args.Length}).";

            bool isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic ?? false;
            if (!isStatic && instance == null)
                return $"Error: '{property.Name}' is an instance property but no target was resolved.";

            object obj = isStatic ? null : instance;
            if (args.Length == 0)
            {
                if (!property.CanRead) return $"Error: property '{property.Name}' is write-only.";
                try { return $"Success: {type.Name}.{property.Name} = {Describe(property.GetValue(obj))}"; }
                catch (Exception ex) { return $"Error reading '{property.Name}': {ex.GetBaseException().Message}"; }
            }

            if (!property.CanWrite) return $"Error: property '{property.Name}' is read-only.";
            try
            {
                object value = ConvertTo(args[0], property.PropertyType);
                property.SetValue(obj, value);
                return $"Success: {type.Name}.{property.Name} set to {Describe(value)}";
            }
            catch (Exception ex) { return $"Error writing '{property.Name}': {ex.GetBaseException().Message}"; }
        }

        private static string AccessField(Type type, FieldInfo field, object instance, object[] args)
        {
            if (args.Length > 1)
                return $"Error: '{field.Name}' is a field — pass one argument to write it, or none to read it " +
                       $"(got {args.Length}).";

            if (!field.IsStatic && instance == null)
                return $"Error: '{field.Name}' is an instance field but no target was resolved.";

            object obj = field.IsStatic ? null : instance;
            if (args.Length == 0)
            {
                try { return $"Success: {type.Name}.{field.Name} = {Describe(field.GetValue(obj))}"; }
                catch (Exception ex) { return $"Error reading '{field.Name}': {ex.GetBaseException().Message}"; }
            }

            if (field.IsInitOnly || field.IsLiteral)
                return $"Error: field '{field.Name}' is readonly/const.";
            try
            {
                object value = ConvertTo(args[0], field.FieldType);
                field.SetValue(obj, value);
                return $"Success: {type.Name}.{field.Name} set to {Describe(value)}";
            }
            catch (Exception ex) { return $"Error writing '{field.Name}': {ex.GetBaseException().Message}"; }
        }

        private static bool TryResolveType(string typeName, out Type type, out string error)
        {
            error = null;
            type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type != null) return true;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var exact = new List<Type>();
            var byName = new List<Type>();

            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.FullName == typeName) exact.Add(t);
                    else if (t.Name == typeName) byName.Add(t);
                }
            }

            if (exact.Count > 0) { type = exact[0]; return true; }
            if (byName.Count == 1) { type = byName[0]; return true; }
            if (byName.Count > 1)
            {
                error = $"Error: '{typeName}' is ambiguous across {byName.Count} types. Use a full name: " +
                        string.Join(", ", byName.Take(10).Select(t => t.FullName));
                return false;
            }

            error = $"Error: type '{typeName}' not found in any loaded assembly.";
            return false;
        }

        private static bool TryResolveTarget(string target, Type expectedType, out object instance, out string error)
        {
            instance = null;
            error = null;
            if (string.IsNullOrWhiteSpace(target)) return true;

            int sep = target.IndexOf(':');
            if (sep < 0)
            {
                error = "Error: target must be 'window:<title>' | 'gameobject:<name>' | 'component:<name>/<Type>' | 'asset:<path>'.";
                return false;
            }

            string kind = target.Substring(0, sep).Trim().ToLowerInvariant();
            string value = target.Substring(sep + 1).Trim();

            switch (kind)
            {
                case "window":
                {
                    var windows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                        .Where(w => w != null && w.titleContent != null
                                 && w.titleContent.text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    if (windows.Count == 0)
                    {
                        var open = Resources.FindObjectsOfTypeAll<EditorWindow>()
                            .Where(w => w != null && w.titleContent != null)
                            .Select(w => w.titleContent.text).Distinct().Take(20);
                        error = $"Error: no EditorWindow whose title contains '{value}'. Open windows: {string.Join(", ", open)}";
                        return false;
                    }
                    // Prefer one that is actually of the requested type — a title substring can match several.
                    instance = windows.FirstOrDefault(w => expectedType.IsInstanceOfType(w)) ?? windows[0];
                    return true;
                }
                case "gameobject":
                {
                    var go = MaterialAdvancedTools.FindGameObjectIncludingInactive(value);
                    if (go == null) { error = $"Error: GameObject '{value}' not found."; return false; }
                    instance = go;
                    return true;
                }
                case "component":
                {
                    int slash = value.LastIndexOf('/');
                    if (slash <= 0)
                    {
                        error = "Error: component target must be 'component:<gameObjectName>/<ComponentType>'.";
                        return false;
                    }
                    string goName = value.Substring(0, slash);
                    string compName = value.Substring(slash + 1);
                    var go = MaterialAdvancedTools.FindGameObjectIncludingInactive(goName);
                    if (go == null) { error = $"Error: GameObject '{goName}' not found."; return false; }
                    var comp = go.GetComponents<Component>()
                        .FirstOrDefault(c => c != null &&
                            (c.GetType().Name == compName || c.GetType().FullName == compName));
                    if (comp == null)
                    {
                        var have = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name);
                        error = $"Error: '{goName}' has no component '{compName}'. Has: {string.Join(", ", have)}";
                        return false;
                    }
                    instance = comp;
                    return true;
                }
                case "asset":
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(value);
                    if (asset == null) { error = $"Error: no asset at '{value}'."; return false; }
                    instance = asset;
                    return true;
                }
                default:
                    error = $"Error: unknown target kind '{kind}'. Use window | gameobject | component | asset.";
                    return false;
            }
        }

        /// <summary>Parses "type:value;type:value" into boxed CLR values.</summary>
        private static bool TryParseArgs(string args, out object[] result, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(args))
            {
                result = Array.Empty<object>();
                return true;
            }

            var parts = args.Split(';');
            var list = new List<object>(parts.Length);
            var ic = CultureInfo.InvariantCulture;

            foreach (var raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;

                int sep = part.IndexOf(':');
                if (sep < 0)
                {
                    result = null;
                    error = $"Error: argument '{part}' must be 'type:value' (string:foo, int:3, float:1.5, bool:true, null:, enum:Full.Type.Value).";
                    return false;
                }

                string kind = part.Substring(0, sep).Trim().ToLowerInvariant();
                string value = part.Substring(sep + 1);

                switch (kind)
                {
                    case "string": case "str": list.Add(value); break;
                    case "null": list.Add(null); break;
                    case "int":
                        if (!int.TryParse(value.Trim(), NumberStyles.Integer, ic, out int i))
                        { result = null; error = $"Error: '{value}' is not an int."; return false; }
                        list.Add(i); break;
                    case "long":
                        if (!long.TryParse(value.Trim(), NumberStyles.Integer, ic, out long l))
                        { result = null; error = $"Error: '{value}' is not a long."; return false; }
                        list.Add(l); break;
                    case "float":
                        if (!float.TryParse(value.Trim(), NumberStyles.Float, ic, out float f))
                        { result = null; error = $"Error: '{value}' is not a float."; return false; }
                        list.Add(f); break;
                    case "double":
                        if (!double.TryParse(value.Trim(), NumberStyles.Float, ic, out double d))
                        { result = null; error = $"Error: '{value}' is not a double."; return false; }
                        list.Add(d); break;
                    case "bool":
                        if (!ToolUtility.TryParseBool(value.Trim(), out bool b))
                        { result = null; error = $"Error: '{value}' is not a bool."; return false; }
                        list.Add(b); break;
                    case "enum":
                    {
                        if (!TryParseEnum(value.Trim(), out object ev, out string enumErr))
                        { result = null; error = enumErr; return false; }
                        list.Add(ev); break;
                    }
                    default:
                        result = null;
                        error = $"Error: unknown argument type '{kind}'. Use string | int | long | float | double | bool | null | enum.";
                        return false;
                }
            }

            result = list.ToArray();
            return true;
        }

        private static bool TryParseEnum(string spec, out object value, out string error)
        {
            value = null;
            error = null;
            int lastDot = spec.LastIndexOf('.');
            if (lastDot <= 0)
            {
                error = $"Error: enum argument '{spec}' must be 'Full.Enum.Type.Value'.";
                return false;
            }
            string typeName = spec.Substring(0, lastDot);
            string memberName = spec.Substring(lastDot + 1);

            if (!TryResolveType(typeName, out Type enumType, out string typeErr)) { error = typeErr; return false; }
            if (!enumType.IsEnum) { error = $"Error: '{typeName}' is not an enum."; return false; }

            try { value = Enum.Parse(enumType, memberName, ignoreCase: true); return true; }
            catch
            {
                error = $"Error: '{memberName}' is not a member of {typeName}. Values: {string.Join(", ", Enum.GetNames(enumType).Take(20))}";
                return false;
            }
        }

        private static object[] CoerceArgs(object[] args, ParameterInfo[] parameters)
        {
            var result = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
                result[i] = ConvertTo(args[i], parameters[i].ParameterType);
            return result;
        }

        private static object ConvertTo(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            if (targetType.IsEnum && value is int enumInt) return Enum.ToObject(targetType, enumInt);
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static string Describe(object value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{s}\"";
            if (value is UnityEngine.Object uo) return $"{uo.GetType().Name} '{uo.name}'";
            if (value is System.Collections.IEnumerable en && !(value is string))
            {
                var items = en.Cast<object>().Take(20).Select(o => o?.ToString() ?? "null").ToArray();
                return $"[{string.Join(", ", items)}]{(items.Length == 20 ? " (truncated)" : "")}";
            }
            return $"{value} ({value.GetType().Name})";
        }

        // ── compilation helpers ──────────────────────────────────────────────

        /// <summary>
        /// Compiles a RunEditorScript body and hands back its entry point, or a formatted compile
        /// error. Shared by RunEditorScript and RunEditorScriptAsync so the two cannot drift in
        /// what they accept, nor in how they explain an unresolved type.
        /// </summary>
        internal static bool TryCompileScript(string code, string usings, string additionalReferences,
                                              out MethodInfo entryPoint, out string error)
        {
            entryPoint = null;
            error = null;

            var extraUsings = SplitList(usings);
            string fullSource = BuildSource(code, extraUsings);

            var provider = new CSharpCodeProvider();
            var compilerParams = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false
            };
            var referenced = CollectReferences(compilerParams, SplitList(additionalReferences));
            var results = provider.CompileAssemblyFromSource(compilerParams, fullSource);

            if (results.Errors.HasErrors)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Compile Error:");
                bool sawMissingType = false;
                foreach (CompilerError err in results.Errors)
                {
                    if (err.IsWarning) continue;
                    sb.AppendLine($"  Line {err.Line - GetLineOffset(extraUsings.Count)}: {err.ErrorText}");
                    // CS0246 (type not found) / CS0234 (namespace member missing) almost always
                    // mean a missing /reference:, not a typo in the user's code.
                    if (err.ErrorNumber == "CS0246" || err.ErrorNumber == "CS0234")
                        sawMissingType = true;
                }

                if (sawMissingType)
                {
                    sb.AppendLine();
                    sb.AppendLine("A type could not be resolved. Referenced assemblies were:");
                    sb.AppendLine("  " + string.Join(", ", referenced.OrderBy(r => r, StringComparer.Ordinal)));
                    sb.AppendLine("Pass the missing one via additionalReferences (e.g. 'System.Core'), " +
                                  "and any missing namespace via usings.");
                }
                error = sb.ToString().TrimEnd();
                return false;
            }

            var type = results.CompiledAssembly.GetType("AgentScript.DynamicScript");
            entryPoint = type?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
            if (entryPoint == null)
            {
                error = "Compile succeeded but the generated entry point AgentScript.DynamicScript.Execute was not found.";
                return false;
            }
            return true;
        }

        private static List<string> SplitList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .ToList();
        }

        /// <summary>
        /// Adds whitelisted assemblies plus any explicitly requested extras to the compiler
        /// parameters. Returns the simple names actually referenced, for error reporting:
        /// when a type fails to resolve, the reference set is the first thing worth seeing.
        /// </summary>
        private static List<string> CollectReferences(CompilerParameters compilerParams, List<string> additional)
        {
            var extra = new HashSet<string>(additional, StringComparer.OrdinalIgnoreCase);
            var referenced = new List<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.IsNullOrEmpty(asm.Location)) continue;
                    string name = asm.GetName().Name;
                    if (!ToolUtility.IsScriptReference(name) && !extra.Contains(name)) continue;
                    compilerParams.ReferencedAssemblies.Add(asm.Location);
                    referenced.Add(name);
                }
                catch
                {
                    // Dynamic assemblies have no Location.
                }
            }

            foreach (var want in extra)
                if (!referenced.Contains(want, StringComparer.OrdinalIgnoreCase))
                    referenced.Add($"{want} (NOT LOADED — could not reference)");

            return referenced;
        }

        private static string BuildSource(string code, List<string> extraUsings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");
            foreach (var ns in extraUsings)
                sb.AppendLine($"using {ns.TrimEnd(';')};");
            sb.AppendLine("namespace AgentScript {");
            sb.AppendLine("  public static class DynamicScript {");
            sb.AppendLine("    public static object Execute() {");
            sb.AppendLine(code);

            // If code doesn't contain a return statement, add a default return
            if (!code.Contains("return "))
                sb.AppendLine("      return null;");

            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Number of lines before user code starts (for error line correction)
        private static int GetLineOffset(int extraUsingCount)
        {
            // Lines: using (6) + extra usings + namespace (1) + class (1) + method (1) = 9 + extras
            return 9 + extraUsingCount;
        }
    }
}
