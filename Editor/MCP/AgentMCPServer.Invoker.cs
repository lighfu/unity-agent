using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.SDK;
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

            string pagingError = call.ConfigureResultPaging();
            if (pagingError != null)
            {
                call.SetError(pagingError, null, -32602);
                return;
            }

            // ── Meta-tools (SearchUnityTool / DescribeUnityTool / ExecuteUnityTool) ──
            if (TryInvokeMetaTool(call)) return;

            JNode executeEnvelope = null;
            if (call.ToolName == "ExecuteUnityTool")
            {
                executeEnvelope = call.Arguments;
                string targetName = call.Arguments["name"].AsString ?? "";
                if (string.IsNullOrEmpty(targetName))
                {
                    AgentLogger.Warning(LogTag.MCP, "ExecuteUnityTool called without 'name' argument.");
                    call.SetError("ExecuteUnityTool: 'name' is required.", null, -32602);
                    return;
                }
                if (targetName == "ExecuteUnityTool")
                {
                    // 再入は深さ 1 で打ち切る。通せば name をどこまでも入れ子にできてしまう
                    AgentLogger.Warning(LogTag.MCP, "ExecuteUnityTool called with name='ExecuteUnityTool' (nesting refused).");
                    call.SetError("ExecuteUnityTool cannot invoke itself.",
                        "Pass the Unity tool's own name as 'name' — e.g. ExecuteUnityTool(name=\"ListRootObjects\", arguments={}).",
                        -32602);
                    return;
                }
                JNode targetArgs = call.Arguments["arguments"];
                if (targetArgs != null && !targetArgs.IsNull && targetArgs.Type != JNode.JType.Object)
                {
                    call.SetError("ExecuteUnityTool: 'arguments' must be an object.", null, -32602);
                    return;
                }
                if (targetArgs == null || targetArgs.IsNull)
                    targetArgs = JNode.Obj();

                AgentLogger.Debug(LogTag.MCP, $"meta ExecuteUnityTool → rewrite target={targetName} argsBytes={targetArgs.ToJson().Length}");
                // 元の call を rewrite して通常のディスパッチパスに再入
                call.Rewrite(targetName, targetArgs);

                // rewrite 後にもう一度だけメタツールを見る (#25)。DescribeUnityTool の出力は
                // 毎回 "Usage: ExecuteUnityTool(name=...)" で締まるので、呼び出し側は
                // 「Unity 側の道具は全部 ExecuteUnityTool 経由」と学習し、検索までその経路に
                // 載せてくる。ここで拾わないと FindTool に落ちて "not found" になり、似た名前の
                // Unity ツールも無いので "Did you mean" すら出ない。
                if (TryInvokeMetaTool(call)) return;
                // fall through to normal dispatch (GetUnityAgentInfo は通常ツールとして登録
                // されているので、ここから FindTool で普通に見つかる)
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
            string argWarning;
            bool strict = info.resolvedRisk != ToolRisk.Safe;
            if (strict && executeEnvelope != null && ToolResultRequest.ValidateExecuteEnvelope(executeEnvelope) is string invalidEnvelope)
            {
                call.SetError(invalidEnvelope, null, -32602);
                return;
            }
            string bindError = BindArguments(method, strict, call.Arguments, out typedArgs, out argWarning);
            if (argWarning != null)
            {
                // PendingCall が結果 / エラーの先頭に差し込む。ここで直接前置しないのは、
                // 結果を確定させる箇所が同期・非同期・ユーザー選択待ちの 3 経路あるため。
                call.ArgumentWarning = argWarning;
                AgentLogger.Warning(LogTag.MCP, $"tool={method.Name}: {argWarning}");
            }
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
                call.SetError($"Error executing tool {method.Name}: {inner.Message}", data, executionFailure: true);
                return;
            }
            catch (Exception ex)
            {
                invokeSw.Stop();
                string data = DeveloperMode.IsDevBuild ? ex.ToString() : null;
                AgentLogger.Warning(LogTag.MCP, $"invoke FAIL tool={method.Name} elapsed={invokeSw.ElapsedMilliseconds}ms ex={ex.GetType().Name}: {ex.Message}");
                call.SetError($"Error executing tool {method.Name}: {ex.Message}", data, executionFailure: true);
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
            var waitSw = Stopwatch.StartNew();
            while (UserChoiceState.SelectedIndex < 0)
            {
                if (call.Cancelled)
                {
                    waitSw.Stop();
                    AgentLogger.Info(LogTag.MCP, $"invoke USER_CHOICE CANCELLED tool={call.ToolName} waited={waitSw.ElapsedMilliseconds}ms");
                    UserChoiceState.Clear();
                    call.SetError("User choice cancelled.", null, -32000);
                    yield break;
                }
                yield return null;
            }

            waitSw.Stop();
            int idx = UserChoiceState.SelectedIndex;
            bool isCustomText = UserChoiceState.CustomText != null;
            string selected = UserChoiceState.CustomText
                ?? UserChoiceState.Options?[idx]
                ?? "";
            string resultText = isCustomText
                ? $"User responded: \"{selected}\""
                : $"User selected: \"{selected}\"";
            AgentLogger.Info(LogTag.MCP,
                $"invoke USER_CHOICE RESOLVED tool={call.ToolName} waited={waitSw.ElapsedMilliseconds}ms kind={(isCustomText ? "custom-text" : "option-index")} index={idx} textLen={selected.Length}");
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
                    call.SetError($"Error during async tool {toolName}: {ex.Message}", data, executionFailure: true);
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

        /// <summary>Bind arguments before any tool code can run. Read-only tools retain legacy warnings.</summary>
        static string BindArguments(MethodInfo method, bool strict, JNode args, out object[] typedArgs, out string warning)
        {
            var parameters = method.GetParameters();
            typedArgs = new object[parameters.Length];
            warning = null;
            if (strict && args != null && !args.IsNull && args.Type != JNode.JType.Object)
                return $"Arguments for {method.Name} must be an object.";
            string unknown = DescribeUnknownArguments(method, parameters, args);
            if (unknown != null)
            {
                if (strict) return $"{method.Name} received {unknown}";
                warning = $"Warning: {method.Name} ignored {unknown}";
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                string key = ResolveArgKey(args, p.Name, ArgumentAlias(method, p.Name), strict, out string conflict);
                if (conflict != null) return conflict;
                JNode raw = key != null ? args[key] : JNode.NullNode;

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

        static string ResolveArgKey(JNode args, string paramName, string alias, bool strict, out string conflict)
        {
            conflict = null;
            if (args == null || paramName == null) return null;
            if (!strict && args.Has(paramName)) return paramName;

            string found = null;
            foreach (var key in args.Keys)
            {
                if (!MatchesArgument(key, paramName, alias)) continue;
                if (found != null)
                {
                    if (strict) conflict = $"Arguments '{found}' and '{key}' refer to the same parameter. Pass '{paramName}' once.";
                    return null;
                }
                found = key;
            }
            return found;
        }

        static bool MatchesArgument(string key, string parameter, string alias) =>
            string.Equals(key, parameter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, alias, StringComparison.OrdinalIgnoreCase);

        static string ArgumentAlias(MethodInfo method, string parameter) => (method.DeclaringType?.Name, method.Name, parameter) switch
        {
            ("SceneTools", "SetTransform" or "SetTransformWorld" or "GetTransform" or "DeleteGameObject" or "DuplicateGameObject" or "SetSiblingIndex" or "MoveUp" or "MoveDown", "name") => "gameObjectName",
            ("SceneTools", "SetParent", "childName") => "gameObjectName",
            ("SceneTools", "RenameGameObject", "currentName") => "gameObjectName",
            ("InspectorTools" or "ComponentTools" or "PrefabTools", _, "gameObjectName") => "name",
            ("InspectorTools", "SetActive", "active") => "isActive",
            _ => null,
        };

        static string DescribeUnknownArguments(MethodInfo method, ParameterInfo[] parameters, JNode args)
        {
            if (args == null || args.Type != JNode.JType.Object) return null;

            var unknown = new List<string>();
            foreach (var key in args.Keys)
            {
                bool known = false;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (MatchesArgument(key, parameters[i].Name, ArgumentAlias(method, parameters[i].Name)))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known) unknown.Add(key);
            }
            if (unknown.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.Append(unknown.Count == 1 ? "an unknown argument " : "unknown arguments ");
            for (int i = 0; i < unknown.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('\'').Append(unknown[i]).Append('\'');
                string hint = SuggestParameter(parameters, unknown[i]);
                if (hint != null) sb.Append(" (did you mean '").Append(hint).Append("'?)");
            }
            sb.Append('.');

            sb.Append(parameters.Length == 0
                ? " This tool takes no arguments."
                : " Accepted: " + string.Join(", ", parameters.Select(p => p.Name)) + ".");

            return sb.ToString();
        }

        /// <summary>
        /// 未知の引数名に近いパラメータ名を 1 つ返す (無ければ null)。大文字小文字違いは
        /// <see cref="ResolveArgKey"/> が吸収済みなので、ここでは部分一致だけを見る。
        /// </summary>
        static string SuggestParameter(ParameterInfo[] parameters, string unknownKey)
        {
            if (string.IsNullOrEmpty(unknownKey)) return null;
            foreach (var p in parameters)
            {
                if (p.Name == null) continue;
                if (p.Name.IndexOf(unknownKey, StringComparison.OrdinalIgnoreCase) >= 0
                    || unknownKey.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p.Name;
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

        // ─── Meta-tools ───

        /// <summary>
        /// SearchUnityTool / DescribeUnityTool を name で判定して実行する。該当しなければ false。
        /// トップレベルの呼び出しと、ExecuteUnityTool が rewrite した後の両方から呼ばれる。
        /// ExecuteUnityTool 自体はここでは扱わない (rewrite の前に Invoke 側で処理する)。
        /// </summary>
        static bool TryInvokeMetaTool(PendingCall call)
        {
            if (call.ToolName == "SearchUnityTool")
            {
                string query = call.Arguments["query"].AsString ?? "";
                if (!ToolResultRequest.ReadInteger(call.Arguments, "limit", 20, out int limit, out string searchError)
                    || !ToolResultRequest.ReadInteger(call.Arguments, "offset", 0, out int offset, out searchError))
                {
                    call.SetError(searchError, null, -32602);
                    return true;
                }
                if (offset < 0 || limit < 1 || limit > ToolResultPager.MaximumLimit)
                {
                    call.SetError("Error: SearchUnityTool offset must be non-negative and limit must be between 1 and 200.", null, -32602);
                    return true;
                }
                var sw = Stopwatch.StartNew();
                string res = Handlers.ImplSearchTool(query, limit, offset);
                sw.Stop();
                AgentLogger.Debug(LogTag.MCP, $"meta SearchUnityTool query=\"{Truncate(query, 80)}\" limit={limit} textBytes={res.Length} elapsed={sw.ElapsedMilliseconds}ms");
                call.SetResult(res);
                return true;
            }
            if (call.ToolName == "DescribeUnityTool")
            {
                string name = call.Arguments["name"].AsString ?? "";
                var sw = Stopwatch.StartNew();
                string res = Handlers.ImplDescribeTool(name);
                sw.Stop();
                AgentLogger.Debug(LogTag.MCP, $"meta DescribeUnityTool name={name} textBytes={res.Length} elapsed={sw.ElapsedMilliseconds}ms");
                call.SetResult(res);
                return true;
            }
            return false;
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
