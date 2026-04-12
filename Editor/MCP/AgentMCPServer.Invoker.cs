using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// `tools/call` の本体。メインスレッドから実行される。
    /// 同期ツールは即実行して <see cref="PendingCall.SetResult"/>。
    /// <see cref="IEnumerator"/> を返す非同期ツールはエディタコルーチンで駆動し、
    /// 最後に yield された文字列を結果とする (UnityAgentCore.ExecuteToolsAsync と同じ挙動)。
    /// </summary>
    internal static class Invoker
    {
        public static void Invoke(PendingCall call)
        {
            if (string.IsNullOrEmpty(call.ToolName))
            {
                call.SetError("Tool name is required.", null, -32602);
                return;
            }

            // ── Meta-tools (SearchUnityTool / DescribeUnityTool / ExecuteUnityTool) ──
            if (call.ToolName == "SearchUnityTool")
            {
                string query = call.Arguments["query"].AsString ?? "";
                int limit = 20;
                var limNode = call.Arguments["limit"];
                if (limNode != null && limNode.Type == JNode.JType.Number) limit = limNode.AsInt;
                call.SetResult(Handlers.ImplSearchTool(query, limit));
                return;
            }
            if (call.ToolName == "DescribeUnityTool")
            {
                string name = call.Arguments["name"].AsString ?? "";
                call.SetResult(Handlers.ImplDescribeTool(name));
                return;
            }
            if (call.ToolName == "ExecuteUnityTool")
            {
                string targetName = call.Arguments["name"].AsString ?? "";
                if (string.IsNullOrEmpty(targetName))
                {
                    call.SetError("ExecuteUnityTool: 'name' is required.", null, -32602);
                    return;
                }
                JNode targetArgs = call.Arguments["arguments"];
                if (targetArgs == null || targetArgs.Type != JNode.JType.Object)
                    targetArgs = JNode.Obj();

                // 元の call を rewrite して通常のディスパッチパスに再入
                call.Rewrite(targetName, targetArgs);
                // fall through to normal dispatch
            }

            var toolInfo = FindTool(call.ToolName);
            if (toolInfo == null)
            {
                var suggestions = SuggestSimilar(call.ToolName);
                string detail = suggestions.Count > 0
                    ? $"Did you mean: {string.Join(", ", suggestions)}"
                    : "No matching tool.";
                call.SetError($"Tool '{call.ToolName}' not found.", detail, -32601);
                return;
            }

            var info = toolInfo.Value;
            var method = info.method;

            // 有効化 + リスクゲート (tools/list と同じ判定を再適用)
            if (!AgentSettings.IsToolEnabled(method.Name, info.isExternal))
            {
                call.SetError($"Tool '{method.Name}' is disabled in UnityAgent settings.", null, -32000);
                return;
            }
            var exposeRisk = AgentSettings.MCPServerExposeRisk;
            if ((int)info.resolvedRisk > (int)exposeRisk)
            {
                call.SetError(
                    $"Tool '{method.Name}' risk level ({info.resolvedRisk}) exceeds MCP expose limit ({exposeRisk}).",
                    null, -32000);
                return;
            }

            // 引数バインド
            object[] typedArgs;
            string bindError = BindArguments(method, call.Arguments, out typedArgs);
            if (bindError != null)
            {
                call.SetError(bindError, null, -32602);
                return;
            }

            // 実行
            int groupBefore = Undo.GetCurrentGroup();
            object rawResult;
            try
            {
                AgentLogger.Info(LogTag.MCP, $"[MCPServer] call method={method.Name} args={call.Arguments.ToJson()}");
                rawResult = method.Invoke(null, typedArgs);
            }
            catch (TargetInvocationException tex)
            {
                var inner = tex.InnerException ?? tex;
                string data = DeveloperMode.IsDevBuild ? inner.ToString() : null;
                call.SetError($"Error executing tool {method.Name}: {inner.Message}", data, -32000);
                return;
            }
            catch (Exception ex)
            {
                string data = DeveloperMode.IsDevBuild ? ex.ToString() : null;
                call.SetError($"Error executing tool {method.Name}: {ex.Message}", data, -32000);
                return;
            }

            if (rawResult is IEnumerator enumerator)
            {
                // コルーチンを起動し、完了時に結果を回収
                EditorCoroutineUtility.StartCoroutineOwnerless(RunAsyncTool(method.Name, enumerator, call, groupBefore));
                return;
            }

            // 同期結果
            string resStr = rawResult?.ToString() ?? "Success (No return value)";

            // AskUser 等のユーザー対話ツールが sentinel を返した場合は UI 側で選択を待つ
            if (resStr == "__WAITING_USER_CHOICE__")
            {
                AgentMCPServer.TraceLog($"  WaitForUserChoice start: tool={method.Name}, pending={UserChoiceState.IsPending}, question={UserChoiceState.Question}");
                AgentMCPServer.RaiseUserChoiceRequested();
                EditorCoroutineUtility.StartCoroutineOwnerless(WaitForUserChoice(call));
                return;
            }

            call.SetResult(resStr);
        }

        /// <summary>
        /// AskUser 等でユーザー選択待ちになった場合、<see cref="UserChoiceState"/> が解決するまで
        /// コルーチンでポーリングし、確定したら MCP 呼び出しに結果を返却する。
        /// </summary>
        static IEnumerator WaitForUserChoice(PendingCall call)
        {
            while (UserChoiceState.SelectedIndex < 0)
            {
                if (call.Cancelled)
                {
                    UserChoiceState.Clear();
                    call.SetError("User choice cancelled.", null, -32000);
                    yield break;
                }
                yield return null;
            }

            string selected = UserChoiceState.CustomText
                ?? UserChoiceState.Options?[UserChoiceState.SelectedIndex]
                ?? "";
            string resultText = UserChoiceState.CustomText != null
                ? $"User responded: \"{selected}\""
                : $"User selected: \"{selected}\"";
            UserChoiceState.Clear();
            call.SetResult(resultText);
        }

        static IEnumerator RunAsyncTool(string toolName, IEnumerator inner, PendingCall call, int groupBefore)
        {
            string asyncResult = null;
            while (true)
            {
                bool hasMore;
                try
                {
                    hasMore = inner.MoveNext();
                }
                catch (Exception ex)
                {
                    string data = DeveloperMode.IsDevBuild ? ex.ToString() : null;
                    call.SetError($"Error during async tool {toolName}: {ex.Message}", data, -32000);
                    yield break;
                }
                if (!hasMore) break;
                if (inner.Current is string s) asyncResult = s;
                yield return inner.Current;
            }

            call.SetResult(asyncResult ?? "Success (No return value)");
        }

        // ─── Argument binding ───

        /// <summary>JSON object → 型変換済み引数配列。</summary>
        /// <returns>成功時 null。失敗時はエラーメッセージ。</returns>
        static string BindArguments(MethodInfo method, JNode args, out object[] typedArgs)
        {
            var parameters = method.GetParameters();
            typedArgs = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                JNode raw = args.Has(p.Name) ? args[p.Name] : JNode.NullNode;

                if (raw.IsNull)
                {
                    if (p.HasDefaultValue)
                    {
                        typedArgs[i] = p.DefaultValue;
                        continue;
                    }
                    return $"Missing required argument '{p.Name}' for {method.Name}.";
                }

                object converted;
                try
                {
                    converted = ConvertJsonToParam(raw, p.ParameterType);
                }
                catch (Exception ex)
                {
                    return $"Cannot convert argument '{p.Name}' to {p.ParameterType.Name}: {ex.Message}";
                }

                // 必須 string が空の場合は拒否 (既存の UnityAgentCore と同じ挙動)
                if (!p.HasDefaultValue && p.ParameterType == typeof(string)
                    && converted is string sv && string.IsNullOrWhiteSpace(sv))
                {
                    return $"Required parameter '{p.Name}' cannot be empty.";
                }

                typedArgs[i] = converted;
            }

            return null;
        }

        static object ConvertJsonToParam(JNode node, Type targetType)
        {
            if (targetType == typeof(string))
            {
                switch (node.Type)
                {
                    case JNode.JType.String: return node.AsString;
                    case JNode.JType.Number: return node.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case JNode.JType.Bool: return node.AsBool ? "true" : "false";
                    case JNode.JType.Array:
                    case JNode.JType.Object:
                        return node.ToJson();
                    default: return "";
                }
            }
            if (targetType == typeof(int)) return (int)GetNumber(node);
            if (targetType == typeof(long)) return (long)GetNumber(node);
            if (targetType == typeof(short)) return (short)GetNumber(node);
            if (targetType == typeof(byte)) return (byte)GetNumber(node);
            if (targetType == typeof(float)) return (float)GetNumber(node);
            if (targetType == typeof(double)) return GetNumber(node);
            if (targetType == typeof(decimal)) return (decimal)GetNumber(node);
            if (targetType == typeof(bool))
            {
                if (node.Type == JNode.JType.Bool) return node.AsBool;
                if (node.Type == JNode.JType.String)
                    return bool.Parse(node.AsString);
                if (node.Type == JNode.JType.Number) return node.AsNumber != 0;
                throw new FormatException("Expected boolean.");
            }

            // Fallback: try Convert.ChangeType through string representation
            string asString = node.Type == JNode.JType.String
                ? node.AsString
                : node.ToJson();
            return Convert.ChangeType(asString, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }

        static double GetNumber(JNode node)
        {
            if (node.Type == JNode.JType.Number) return node.AsNumber;
            if (node.Type == JNode.JType.String)
            {
                if (double.TryParse(node.AsString,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double d))
                    return d;
            }
            if (node.Type == JNode.JType.Bool) return node.AsBool ? 1 : 0;
            throw new FormatException("Expected numeric value.");
        }

        // ─── Tool lookup ───

        static ToolRegistry.ToolInfo? FindTool(string name)
        {
            foreach (var info in ToolRegistry.GetAllTools())
            {
                if (info.method == null) continue;
                if (string.Equals(info.method.Name, name, StringComparison.OrdinalIgnoreCase))
                    return info;
            }
            return null;
        }

        static List<string> SuggestSimilar(string name)
        {
            return ToolRegistry.GetAllTools()
                .Where(t => t.method != null)
                .Select(t => t.method.Name)
                .Where(n => n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(5)
                .ToList();
        }
    }
}
