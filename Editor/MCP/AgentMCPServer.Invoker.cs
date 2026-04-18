using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
                var sw = Stopwatch.StartNew();
                string res = Handlers.ImplSearchTool(query, limit);
                sw.Stop();
                AgentLogger.Debug(LogTag.MCP, $"meta SearchUnityTool query=\"{Truncate(query, 80)}\" limit={limit} textBytes={res.Length} elapsed={sw.ElapsedMilliseconds}ms");
                call.SetResult(res);
                return;
            }
            if (call.ToolName == "DescribeUnityTool")
            {
                string name = call.Arguments["name"].AsString ?? "";
                var sw = Stopwatch.StartNew();
                string res = Handlers.ImplDescribeTool(name);
                sw.Stop();
                AgentLogger.Debug(LogTag.MCP, $"meta DescribeUnityTool name={name} textBytes={res.Length} elapsed={sw.ElapsedMilliseconds}ms");
                call.SetResult(res);
                return;
            }
            if (call.ToolName == "ExecuteUnityTool")
            {
                string targetName = call.Arguments["name"].AsString ?? "";
                if (string.IsNullOrEmpty(targetName))
                {
                    AgentLogger.Warning(LogTag.MCP, "ExecuteUnityTool called without 'name' argument.");
                    call.SetError("ExecuteUnityTool: 'name' is required.", null, -32602);
                    return;
                }
                JNode targetArgs = call.Arguments["arguments"];
                if (targetArgs == null || targetArgs.Type != JNode.JType.Object)
                    targetArgs = JNode.Obj();

                AgentLogger.Debug(LogTag.MCP, $"meta ExecuteUnityTool → rewrite target={targetName} argsBytes={targetArgs.ToJson().Length}");
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
                AgentLogger.Warning(LogTag.MCP, $"Tool not found: '{call.ToolName}'. {detail}");
                call.SetError($"Tool '{call.ToolName}' not found.", detail, -32601);
                return;
            }

            var info = toolInfo.Value;
            var method = info.method;

            // 有効化 + リスクゲート (tools/list と同じ判定を再適用)
            if (!AgentSettings.IsToolEnabled(method.Name, info.isExternal))
            {
                AgentLogger.Warning(LogTag.MCP, $"Tool '{method.Name}' rejected: disabled in UnityAgent settings.");
                call.SetError($"Tool '{method.Name}' is disabled in UnityAgent settings.", null, -32000);
                return;
            }
            var exposeRisk = AgentSettings.MCPServerExposeRisk;
            if ((int)info.resolvedRisk > (int)exposeRisk)
            {
                AgentLogger.Warning(LogTag.MCP, $"Tool '{method.Name}' rejected: risk {info.resolvedRisk} > expose limit {exposeRisk}.");
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
                AgentLogger.Warning(LogTag.MCP, $"Bind error tool={method.Name}: {bindError}");
                call.SetError(bindError, null, -32602);
                return;
            }

            // 実行
            int groupBefore = Undo.GetCurrentGroup();
            object rawResult;
            string argsJson = call.Arguments.ToJson();
            var invokeSw = Stopwatch.StartNew();
            try
            {
                // ルーチン呼び出し単位は Debug。WAITING_USER_CHOICE や FAIL は上位レベルで残る。
                AgentLogger.Debug(LogTag.MCP,
                    $"invoke START tool={method.Name} risk={info.resolvedRisk} external={info.isExternal} params={method.GetParameters().Length} argsBytes={argsJson.Length} args={Truncate(argsJson, 400)}");
                rawResult = method.Invoke(null, typedArgs);
            }
            catch (TargetInvocationException tex)
            {
                invokeSw.Stop();
                var inner = tex.InnerException ?? tex;
                string data = DeveloperMode.IsDevBuild ? inner.ToString() : null;
                AgentLogger.Warning(LogTag.MCP, $"invoke FAIL tool={method.Name} elapsed={invokeSw.ElapsedMilliseconds}ms ex={inner.GetType().Name}: {inner.Message}");
                call.SetError($"Error executing tool {method.Name}: {inner.Message}", data, -32000);
                return;
            }
            catch (Exception ex)
            {
                invokeSw.Stop();
                string data = DeveloperMode.IsDevBuild ? ex.ToString() : null;
                AgentLogger.Warning(LogTag.MCP, $"invoke FAIL tool={method.Name} elapsed={invokeSw.ElapsedMilliseconds}ms ex={ex.GetType().Name}: {ex.Message}");
                call.SetError($"Error executing tool {method.Name}: {ex.Message}", data, -32000);
                return;
            }

            if (rawResult is IEnumerator enumerator)
            {
                AgentLogger.Debug(LogTag.MCP, $"invoke async tool={method.Name} (IEnumerator coroutine path, sync elapsed={invokeSw.ElapsedMilliseconds}ms)");
                // コルーチンを起動し、完了時に結果を回収
                EditorCoroutineUtility.StartCoroutineOwnerless(RunAsyncTool(method.Name, enumerator, call, groupBefore, invokeSw));
                return;
            }
            invokeSw.Stop();

            // 同期結果
            string resStr = rawResult?.ToString() ?? "Success (No return value)";

            // AskUser 等のユーザー対話ツールが sentinel を返した場合は UI 側で選択を待つ
            if (resStr == "__WAITING_USER_CHOICE__")
            {
                AgentLogger.Info(LogTag.MCP, $"invoke WAITING_USER_CHOICE tool={method.Name} question=\"{Truncate(UserChoiceState.Question ?? "", 80)}\"");
                AgentMCPServer.TraceLog($"  WaitForUserChoice start: tool={method.Name}, pending={UserChoiceState.IsPending}, question={UserChoiceState.Question}");
                AgentMCPServer.RaiseUserChoiceRequested();
                EditorCoroutineUtility.StartCoroutineOwnerless(WaitForUserChoice(call));
                return;
            }

            CaptureAndClearPendingImage(call);
            AgentLogger.Debug(LogTag.MCP,
                $"invoke OK tool={method.Name} elapsed={invokeSw.ElapsedMilliseconds}ms textBytes={resStr.Length} imgBytes={(call.ImageBytes?.Length ?? 0)}");
            call.SetResult(resStr);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        /// <summary>
        /// Capture-style tools (CaptureSceneView / CaptureExpressionPreview / CaptureMultiAngle)
        /// stash the PNG on <see cref="Tools.SceneViewTools.PendingImageBytes"/> so the in-editor
        /// chat can inline it into the next user turn. For MCP callers the bytes must be pulled
        /// off the static slot and attached to this specific call's result, otherwise the
        /// remote LLM only ever sees the tool's summary string ("Success: Captured ...") and
        /// silently misses the actual image.
        /// </summary>
        static void CaptureAndClearPendingImage(PendingCall call)
        {
            var bytes = Tools.SceneViewTools.PendingImageBytes;
            if (bytes == null || bytes.Length == 0) return;
            call.SetImage(bytes, Tools.SceneViewTools.PendingImageMimeType);
            Tools.SceneViewTools.ClearPendingImage();
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

        static IEnumerator RunAsyncTool(string toolName, IEnumerator inner, PendingCall call, int groupBefore, Stopwatch sw)
        {
            string asyncResult = null;
            int steps = 0;
            while (true)
            {
                bool hasMore;
                try
                {
                    hasMore = inner.MoveNext();
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    string data = DeveloperMode.IsDevBuild ? ex.ToString() : null;
                    AgentLogger.Warning(LogTag.MCP, $"invoke FAIL async tool={toolName} steps={steps} elapsed={sw.ElapsedMilliseconds}ms ex={ex.GetType().Name}: {ex.Message}");
                    call.SetError($"Error during async tool {toolName}: {ex.Message}", data, -32000);
                    yield break;
                }
                if (!hasMore) break;
                if (inner.Current is string s) asyncResult = s;
                steps++;
                yield return inner.Current;
            }

            sw.Stop();
            CaptureAndClearPendingImage(call);
            string resText = asyncResult ?? "Success (No return value)";
            AgentLogger.Debug(LogTag.MCP,
                $"invoke OK async tool={toolName} steps={steps} elapsed={sw.ElapsedMilliseconds}ms textBytes={resText.Length} imgBytes={(call.ImageBytes?.Length ?? 0)}");
            call.SetResult(resText);
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
