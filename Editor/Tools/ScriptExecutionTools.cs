using UnityEngine;
using UnityEditor;
using System;
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
        [AgentTool("Execute arbitrary C# code in the Editor as a last resort when no existing tool covers the operation. The code runs inside a static Execute() method. Use 'return' to return a result string. Always requires user confirmation.")]
        public static string RunEditorScript(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "Error: No code provided.";

            // Always require confirmation
            if (!AgentSettings.RequestConfirmation(
                "C#スクリプトを実行",
                $"以下のコードを実行します:\n\n{code}"))
                return "Cancelled: User denied script execution.";

            Debug.Log($"[UnityAgent] RunEditorScript executing:\n{code}");

            // Wrap code in a class/method
            string fullSource = BuildSource(code);

            // Compile
            var provider = new CSharpCodeProvider();
            var compilerParams = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false
            };

            // Add references from all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(asm.Location))
                        compilerParams.ReferencedAssemblies.Add(asm.Location);
                }
                catch
                {
                    // Skip assemblies without a location (dynamic assemblies)
                }
            }

            var results = provider.CompileAssemblyFromSource(compilerParams, fullSource);

            // Check compile errors
            if (results.Errors.HasErrors)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Compile Error:");
                foreach (CompilerError error in results.Errors)
                {
                    if (!error.IsWarning)
                        sb.AppendLine($"  Line {error.Line - GetLineOffset()}: {error.ErrorText}");
                }
                return sb.ToString().TrimEnd();
            }

            // Execute
            try
            {
                var assembly = results.CompiledAssembly;
                var type = assembly.GetType("AgentScript.DynamicScript");
                var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                var result = method.Invoke(null, null);

                if (result == null)
                    return "Script executed successfully.";
                return result.ToString();
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
        }

        private static string BuildSource(string code)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");
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
        private static int GetLineOffset()
        {
            // Lines: using (6) + namespace (1) + class (1) + method (1) = 9
            return 9;
        }
    }
}
