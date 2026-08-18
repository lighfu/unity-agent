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
            string argWarning;
            string bindError = BindArguments(method, call.Arguments, out typedArgs, out argWarning);
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
        /// <param name="warning">
        /// スキーマにない引数が渡されていた場合の警告文 (無ければ null)。バインド自体は成功させ、
        /// 結果の先頭にこれを差し込む。<b>捨てたことを黙っていてはいけない</b>のが要点で、
        /// 実際に「別のツールでは有効な引数名を渡したが、このツールには無かったので無視され、
        /// 既定の自動探索でまったく別のオブジェクトが操作されたのに Success が返った」という
        /// 事故が起きている (issue #7)。エラーで返さないのは、既存のクライアントが送っている
        /// 余分なキーで呼び出しが一斉に失敗するのを避けるため。
        /// </param>
        /// <returns>成功時 null。失敗時はエラーメッセージ。</returns>
        static string BindArguments(MethodInfo method, JNode args, out object[] typedArgs, out string warning)
        {
            var parameters = method.GetParameters();
            typedArgs = new object[parameters.Length];

            // 未知キーの検出はバインドより先に行う。必須引数の欠落で早期 return する場合でも
            // 警告は返したい — 「name が必須」と「gameObjectName は無視した」は同時に起きる
            // (issue #7 の GetHierarchyTree がまさにこれ)。
            warning = BuildUnknownArgumentWarning(method, parameters, args);

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                string key = ResolveArgKey(args, p.Name);
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

        /// <summary>
        /// 引数キーに対応する JSON のキー名を返す (無ければ null)。完全一致を優先し、
        /// 無ければ大文字小文字を無視して<b>一意に決まる場合だけ</b>採用する。
        ///
        /// MCP 側の JSON オブジェクトは序数比較の Dictionary なので、以前は綴りが同じでも
        /// 大小が違うだけで「渡していない」扱いになり、既定値のまま黙って実行されていた。
        /// チャット経路 (<c>UnityAgentCore</c> の XML <c>&lt;arg&gt;</c> バインド) は元から
        /// 大文字小文字を無視するので、経路によって通ったり通らなかったりしていた。
        /// 複数候補があるときに勝手に選ばないのは、どちらを使ったか呼び出し側に見えないため。
        /// </summary>
        static string ResolveArgKey(JNode args, string paramName)
        {
            if (args == null || paramName == null) return null;
            if (args.Has(paramName)) return paramName;

            string found = null;
            foreach (var key in args.Keys)
            {
                if (!string.Equals(key, paramName, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return null;   // 大小違いが複数 — 曖昧なので採用しない
                found = key;
            }
            return found;
        }

        /// <summary>
        /// どのパラメータにも対応しない引数キーを集めて警告文を作る (無ければ null)。
        /// </summary>
        static string BuildUnknownArgumentWarning(MethodInfo method, ParameterInfo[] parameters, JNode args)
        {
            if (args == null || args.Type != JNode.JType.Object) return null;

            var unknown = new List<string>();
            foreach (var key in args.Keys)
            {
                bool known = false;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (string.Equals(parameters[i].Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known) unknown.Add(key);
            }
            if (unknown.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.Append("Warning: ").Append(method.Name).Append(" ignored ");
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

            // 「無視された」だけでは足りない。無視されたのが対象を選ぶ引数だった場合、
            // ツールは既定の探索にフォールバックして別のオブジェクトを操作し、しかも成功を返す。
            // 成功・失敗のどちらの本文にも前置されるので、「下の結果」のような書き方はしない。
            sb.Append(" Unknown arguments are dropped, so the tool used its default instead —"
                      + " if that argument was selecting the target, this call may have acted"
                      + " on a different object than you intended.");
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
