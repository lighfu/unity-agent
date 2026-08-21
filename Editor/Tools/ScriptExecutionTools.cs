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
  (System, System.Linq, System.Collections.Generic, System.Text, UnityEngine, UnityEditor,
  plus 'using Object = UnityEngine.Object' so that Object.FindObjectsOfType / Object.DestroyImmediate
  compile — System and UnityEngine both define Object, and without the alias they are ambiguous here
  even though the same line is fine in a normal Unity script).
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

        [AgentTool(@"List the members of a type — the half of reflection that InvokeMember does not cover.

InvokeMember needs a member NAME and a matching argument list before it can do anything. This is where
those come from. Reach for it the moment a reflection call fails with 'Ambiguous match found' or
'cannot be converted to type X': both mean there are overloads you cannot see.

typeName: full name ('UnityEditor.SceneView') or a plain class name searched across loaded assemblies —
  the same resolution InvokeMember uses, so a name that works here works there.
memberFilter: '' or 'all' (default) | 'methods' | 'properties' | 'fields' | 'events'.
nameContains: case-insensitive substring filter on the member name. A type like GameObject runs to
  hundreds of members, so filter before reading.
maxMembers: cap on printed members (default 150). Truncation is always stated, never silent.

WHAT YOU GET: full name, assembly, kind, the base chain, and every member grouped by the type that
DECLARED it, with visibility, static-ness, return type, and parameter types AND names.

ALL OVERLOADS ARE LISTED, and when a name has more than one, its parameter types are printed as FULL
names. That is the case this tool exists for: two overloads that both read 'Foo(Material m)' in short
form, where one takes UnityEngine.Material and the other a same-named type from another namespace, are
indistinguishable until the full name is shown.

Searches Instance|Static|Public|NonPublic at every level of the inheritance chain, exactly like
InvokeMember — so a member listed here is a member InvokeMember can reach. Property and event accessors
(get_/set_/add_/remove_) are folded into their property or event instead of being listed as methods.
Constructors are not listed: InvokeMember cannot call them. The walk stops short of System.Object and
System.ValueType, whose members (ToString, Equals, GetHashCode, GetType) sit on every type and would pad
every listing — InvokeMember can still call those, they are simply not repeated here.

For an enum, the members ARE the values, so they are printed as 'name = value' ready to paste into
InvokeMember's enum:Full.Type.Value argument form.

Note for lookups by name: InvokeMember resolves a name as property, then field, then method. If this
listing shows the same name as more than one kind, that is the order it will be taken in.",
            Risk = ToolRisk.Safe)]
        public static string DescribeType(string typeName, string memberFilter = "",
                                          string nameContains = "", int maxMembers = 150)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return "Error: typeName is required.";
            if (maxMembers <= 0) return $"Error: maxMembers must be positive (got {maxMembers}).";

            string filter = (memberFilter ?? "").Trim().ToLowerInvariant();
            if (filter.Length == 0) filter = "all";
            if (filter != "all" && filter != "methods" && filter != "properties"
                && filter != "fields" && filter != "events")
                return $"Error: unknown memberFilter '{memberFilter}'. " +
                       "Use '' or 'all' | 'methods' | 'properties' | 'fields' | 'events'.";

            if (!TryResolveType(typeName, out Type type, out string typeErr)) return typeErr;

            string needle = (nameContains ?? "").Trim();
            var entries = CollectTypeMembers(type, filter, needle);

            var sb = new StringBuilder();
            sb.AppendLine($"=== {type.FullName} ===");
            sb.AppendLine($"assembly   : {SafeAssemblyName(type)}");
            sb.AppendLine($"kind       : {DescribeTypeKind(type)}");
            sb.AppendLine($"base chain : {string.Join(" -> ", BaseChain(type))}");

            if (type.IsEnum)
            {
                AppendEnumValues(sb, type, needle, maxMembers);
                return sb.ToString().TrimEnd();
            }

            string scope = filter == "all" ? "" : $", memberFilter='{filter}'";
            string filtered = needle.Length > 0 ? $", nameContains='{needle}'" : "";
            int shown = Mathf.Min(entries.Count, maxMembers);
            sb.AppendLine($"members    : {entries.Count} matched{scope}{filtered}, showing {shown}");

            if (entries.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine(needle.Length > 0
                    ? $"No member of {type.Name} matches '{needle}'. Drop nameContains to see everything."
                    : $"{type.Name} exposes no member of that kind.");
                return sb.ToString().TrimEnd();
            }

            // A short parameter type name is only a problem when two overloads collide under it, so pay
            // the width cost of full names exactly where it buys disambiguation.
            var overloaded = new HashSet<string>(
                entries.GroupBy(e => e.Name, StringComparer.Ordinal)
                       .Where(g => g.Count() > 1)
                       .Select(g => g.Key),
                StringComparer.Ordinal);

            string currentOwner = null;
            for (int i = 0; i < shown; i++)
            {
                var entry = entries[i];
                if (entry.Owner != currentOwner)
                {
                    currentOwner = entry.Owner;
                    sb.AppendLine();
                    sb.AppendLine($"--- declared on {currentOwner} ---");
                }
                sb.AppendLine("  " + entry.Render(overloaded.Contains(entry.Name)));
            }

            if (entries.Count > shown)
            {
                sb.AppendLine();
                sb.AppendLine($"... {entries.Count - shown} more member(s) not shown. Narrow with " +
                              "nameContains, or raise maxMembers.");
            }
            return sb.ToString().TrimEnd();
        }

        // ── type description helpers ─────────────────────────────────────────

        /// <summary>One listed member, kept in a form that can be rendered short or fully qualified.</summary>
        private sealed class MemberEntry
        {
            public string Owner;
            public string Kind;
            public string Name;
            public string Modifiers;
            public Func<bool, string> Signature;

            public string Render(bool useFullTypeNames) =>
                $"[{Kind}]".PadRight(12) + Modifiers.PadRight(24) + Signature(useFullTypeNames);
        }

        /// <summary>
        /// Walks the inheritance chain and collects the members of each level separately. NonPublic does
        /// not cross the chain and FlattenHierarchy only surfaces statics, so a per-level walk is the only
        /// way a private instance member of a base class shows up — the same walk InvokeMember does.
        /// </summary>
        private static List<MemberEntry> CollectTypeMembers(Type type, string filter, string nameContains)
        {
            var entries = new List<MemberEntry>();
            bool Wanted(string name) =>
                nameContains.Length == 0 ||
                name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;

            // Stops short of System.Object / System.ValueType. Their members (ToString, GetHashCode,
            // Equals, GetType) are on literally every type and would pad every single listing; they are
            // still reachable through InvokeMember, which is why the docstring says so out loud.
            for (var t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            {
                string owner = t.FullName ?? t.Name;
                var level = new List<MemberEntry>();

                if (filter == "all" || filter == "properties")
                {
                    foreach (var p in t.GetProperties(DeclaredFlags))
                    {
                        if (!Wanted(p.Name)) continue;
                        var accessor = p.GetMethod ?? p.SetMethod;
                        string access = (p.CanRead ? "get" : "") + (p.CanWrite ? (p.CanRead ? "/set" : "set") : "");
                        var captured = p;
                        level.Add(new MemberEntry
                        {
                            Owner = owner,
                            Kind = "property",
                            Name = p.Name,
                            Modifiers = $"{Visibility(accessor)}{(accessor != null && accessor.IsStatic ? " static" : "")} [{access}]",
                            Signature = full => $"{TypeLabel(captured.PropertyType, full)} {captured.Name}",
                        });
                    }
                }

                if (filter == "all" || filter == "fields")
                {
                    foreach (var f in t.GetFields(DeclaredFlags))
                    {
                        if (!Wanted(f.Name)) continue;
                        var captured = f;
                        string extra = f.IsLiteral ? " const" : (f.IsInitOnly ? " readonly" : (f.IsStatic ? " static" : ""));
                        level.Add(new MemberEntry
                        {
                            Owner = owner,
                            Kind = "field",
                            Name = f.Name,
                            Modifiers = Visibility(f) + extra,
                            Signature = full => $"{TypeLabel(captured.FieldType, full)} {captured.Name}",
                        });
                    }
                }

                if (filter == "all" || filter == "events")
                {
                    foreach (var e in t.GetEvents(DeclaredFlags))
                    {
                        if (!Wanted(e.Name)) continue;
                        var captured = e;
                        var adder = e.AddMethod;
                        level.Add(new MemberEntry
                        {
                            Owner = owner,
                            Kind = "event",
                            Name = e.Name,
                            Modifiers = $"{Visibility(adder)}{(adder != null && adder.IsStatic ? " static" : "")}",
                            Signature = full => $"{TypeLabel(captured.EventHandlerType, full)} {captured.Name}",
                        });
                    }
                }

                if (filter == "all" || filter == "methods")
                {
                    foreach (var m in t.GetMethods(DeclaredFlags))
                    {
                        if (IsAccessor(m)) continue;
                        if (!Wanted(m.Name)) continue;
                        var captured = m;
                        level.Add(new MemberEntry
                        {
                            Owner = owner,
                            Kind = "method",
                            Name = m.Name,
                            Modifiers = Visibility(m) + (m.IsStatic ? " static" : "") + (m.IsAbstract ? " abstract" : ""),
                            Signature = full =>
                                $"{TypeLabel(captured.ReturnType, full)} {captured.Name}(" +
                                string.Join(", ", captured.GetParameters().Select(p => ParameterLabel(p, full))) + ")",
                        });
                    }
                }

                entries.AddRange(level.OrderBy(e => e.Kind, StringComparer.Ordinal)
                                      .ThenBy(e => e.Name, StringComparer.Ordinal));
            }
            return entries;
        }

        /// <summary>get_/set_/add_/remove_ belong to their property or event, not to the method list.
        /// Operators keep their IsSpecialName flag but are real call targets, so they stay.</summary>
        private static bool IsAccessor(MethodInfo m) =>
            m.IsSpecialName &&
            (m.Name.StartsWith("get_", StringComparison.Ordinal) ||
             m.Name.StartsWith("set_", StringComparison.Ordinal) ||
             m.Name.StartsWith("add_", StringComparison.Ordinal) ||
             m.Name.StartsWith("remove_", StringComparison.Ordinal));

        private static void AppendEnumValues(StringBuilder sb, Type type, string nameContains, int maxMembers)
        {
            string underlying = TypeLabel(Enum.GetUnderlyingType(type), false);
            var names = Enum.GetNames(type)
                            .Where(n => nameContains.Length == 0 ||
                                        n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList();
            sb.AppendLine($"underlying : {underlying}{(type.IsDefined(typeof(FlagsAttribute), false) ? " [Flags]" : "")}");
            sb.AppendLine($"values     : {names.Count} matched, showing {Mathf.Min(names.Count, maxMembers)}");
            sb.AppendLine();
            foreach (string name in names.Take(maxMembers))
            {
                object value = Enum.Parse(type, name);
                sb.AppendLine($"  {name} = {Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)}" +
                              $"   (enum:{type.FullName}.{name})");
            }
            if (names.Count > maxMembers)
                sb.AppendLine($"  ... {names.Count - maxMembers} more value(s) not shown.");
        }

        private static string SafeAssemblyName(Type type)
        {
            try { return type.Assembly.GetName().Name; }
            catch (Exception ex) { return $"unknown ({ex.Message})"; }
        }

        private static string DescribeTypeKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            string modifiers = type.IsAbstract && type.IsSealed ? " (static)"
                             : type.IsAbstract ? " (abstract)"
                             : type.IsSealed ? " (sealed)"
                             : "";
            return "class" + modifiers;
        }

        private static List<string> BaseChain(Type type)
        {
            var chain = new List<string>();
            for (var t = type; t != null; t = t.BaseType)
            {
                chain.Add(t.Name);
                if (chain.Count > 12) { chain.Add("..."); break; }
            }
            return chain;
        }

        private static readonly Dictionary<Type, string> PrimitiveTypeNames = new Dictionary<Type, string>
        {
            { typeof(void), "void" },     { typeof(bool), "bool" },     { typeof(byte), "byte" },
            { typeof(sbyte), "sbyte" },   { typeof(char), "char" },     { typeof(decimal), "decimal" },
            { typeof(double), "double" }, { typeof(float), "float" },   { typeof(int), "int" },
            { typeof(uint), "uint" },     { typeof(long), "long" },     { typeof(ulong), "ulong" },
            { typeof(short), "short" },   { typeof(ushort), "ushort" }, { typeof(object), "object" },
            { typeof(string), "string" },
        };

        /// <summary>
        /// Renders a type for a signature line. <paramref name="full"/> switches between the readable
        /// short name and the fully qualified one that tells two same-named types apart.
        /// </summary>
        private static string TypeLabel(Type t, bool full)
        {
            if (t == null) return "?";
            if (PrimitiveTypeNames.TryGetValue(t, out string primitive)) return primitive;
            if (t.IsByRef) return "ref " + TypeLabel(t.GetElementType(), full);
            if (t.IsArray) return TypeLabel(t.GetElementType(), full) + "[]";
            if (t.IsGenericType)
            {
                string raw = full ? (t.GetGenericTypeDefinition().FullName ?? t.Name) : t.Name;
                int tick = raw.IndexOf('`');
                if (tick > 0) raw = raw.Substring(0, tick);
                return $"{raw}<{string.Join(", ", t.GetGenericArguments().Select(a => TypeLabel(a, full)))}>";
            }
            return full ? (t.FullName ?? t.Name) : t.Name;
        }

        private static string ParameterLabel(ParameterInfo p, bool full)
        {
            bool byRef = p.ParameterType.IsByRef;
            string prefix = p.IsOut ? "out " : (byRef ? "ref " : "");
            Type bare = byRef ? p.ParameterType.GetElementType() : p.ParameterType;
            string label = $"{prefix}{TypeLabel(bare, full)} {p.Name}";
            if (!p.IsOptional) return label;
            string fallback;
            try { fallback = p.DefaultValue == null ? "null" : p.DefaultValue.ToString(); }
            catch (Exception) { fallback = "?"; }
            return $"{label} = {fallback}";
        }

        private static string Visibility(MethodBase m)
        {
            if (m == null) return "?";
            if (m.IsPublic) return "public";
            if (m.IsFamilyOrAssembly) return "protected internal";
            if (m.IsFamilyAndAssembly) return "private protected";
            if (m.IsFamily) return "protected";
            if (m.IsAssembly) return "internal";
            return "private";
        }

        private static string Visibility(FieldInfo f)
        {
            if (f.IsPublic) return "public";
            if (f.IsFamilyOrAssembly) return "protected internal";
            if (f.IsFamilyAndAssembly) return "private protected";
            if (f.IsFamily) return "protected";
            if (f.IsAssembly) return "internal";
            return "private";
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
            string fullSource = BuildSource(code, extraUsings, out int lineOffset);

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
                    sb.AppendLine($"  Line {err.Line - lineOffset}: {err.ErrorText}");
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

        /// <summary>
        /// Wraps the submitted body in the shell that makes it compilable, and reports how many lines that
        /// shell put in front of it so compile errors can be renumbered into the caller's own line numbers.
        ///
        /// The count is MEASURED, not hardcoded. A constant here shifts every reported error line by one
        /// the first time somebody adds a using to the preamble, and a line number that is quietly off by
        /// one is far harder to notice than one that is obviously wrong.
        /// </summary>
        private static string BuildSource(string code, List<string> extraUsings, out int lineOffset)
        {
            var preamble = new List<string>
            {
                "using System;",
                "using System.Linq;",
                "using System.Collections.Generic;",
                "using System.Text;",
                "using UnityEngine;",
                "using UnityEditor;",
                // System.Object and UnityEngine.Object both exist, so the most common line in any editor
                // script — Object.FindObjectsOfType<T>() — does not compile once both namespaces are open.
                // A plain Unity project never hits this because it has no 'using System;' at the top; only
                // scripts submitted here do. The alias does not shadow the 'object' keyword, and code that
                // already writes UnityEngine.Object in full keeps working.
                "using Object = UnityEngine.Object;",
            };
            preamble.AddRange(extraUsings.Select(ns => $"using {ns.TrimEnd(';')};"));
            preamble.Add("namespace AgentScript {");
            preamble.Add("  public static class DynamicScript {");
            preamble.Add("    public static object Execute() {");

            var sb = new StringBuilder();
            foreach (string line in preamble) sb.AppendLine(line);
            lineOffset = preamble.Count;

            sb.AppendLine(code);

            // If code doesn't contain a return statement, add a default return
            if (!code.Contains("return "))
                sb.AppendLine("      return null;");

            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

    }
}
