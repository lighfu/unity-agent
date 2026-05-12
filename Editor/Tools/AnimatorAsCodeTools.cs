#if ANIMATOR_AS_CODE
using AnimatorAsCode.V1;
using AnimatorAsCode.V1.VRC;
#if MODULAR_AVATAR
using AnimatorAsCode.V1.ModularAvatar;
#endif
#endif
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AAC (Hai-VR AnimatorAsCode V1) を AI に直接書かせる Buildup API。
    /// 既存のテンプレート API は廃止し、AI が C# スニペットで任意の Animator
    /// 構造を組み立てる方式に特化している。
    ///
    /// ワークフロー:
    ///   1. AacBeginSystem(systemName, avatarRoot)              — container + AacFlBase 作成
    ///   2. AacExecuteScript(systemName, "var layer = ctrl...") — AAC fluent builder を C# で記述
    ///      （複数回呼んで段階的に組み立て可）
    ///   3. AacCommitSystem(systemName)                          — 完了確定 (SaveAssets + session 解除)
    /// または:
    ///   - AacDiscardSession(systemName) で container ごと破棄
    ///
    /// セッションは in-memory dict に保持されるため、ドメインリロードで失われる。
    /// リロードが起きたら最初から組み直すこと。
    /// </summary>
    public static class AnimatorAsCodeTools
    {
#if ANIMATOR_AS_CODE && MODULAR_AVATAR

        // ========== Session Storage ==========

        private class Session
        {
            public string SystemKey;          // sanitized systemName (key on disk)
            public string OriginalName;       // user-supplied systemName (key in _sessions)
            public GameObject AvatarRoot;
            public string AssetDir;
            public string ContainerPath;
            public AacFlBase Aac;
            public AacFlController Controller;
            public readonly List<string> ScriptLog = new List<string>();
            public DateTime CreatedAt;
        }

        private static readonly Dictionary<string, Session> _sessions =
            new Dictionary<string, Session>(StringComparer.Ordinal);

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string name)
            => MeshAnalysisTools.FindGameObject(name);

        private static readonly Regex s_InvalidPathChars = new Regex(@"[\\/:*?""<>|]", RegexOptions.Compiled);
        private static string SanitizePath(string s)
            => string.IsNullOrEmpty(s) ? "_" : s_InvalidPathChars.Replace(s, "_");

        private static string ResolveAssetDir(string assetDir, string avatarRootName)
        {
            if (!string.IsNullOrEmpty(assetDir)) return assetDir;
            return $"{PackagePaths.GeneratedRoot}/AnimatorAsCode/{SanitizePath(avatarRootName)}";
        }

        private static string ContainerPath(string assetDir, string systemKey)
            => $"{assetDir}/AAC_{systemKey}_Container.asset";

        private static bool ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "on":
                case "yes":
                case "enabled":
                    return true;
                default:
                    return false;
            }
        }

        private static string Truncate(string s, int max)
            => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        // ========== 1. AacBeginSystem ==========

        [AgentTool(
            "Begin a new AAC system session. Creates an empty AacGeneratedContainer .asset, an AacFlBase, " +
            "and a fresh AacFlController, stored in memory under systemName. Returns the systemName key. " +
            "Next: call AacExecuteScript(systemName, code) to build layers/states/transitions/clips with the AAC fluent builder, " +
            "then AacCommitSystem(systemName) to finalize. A domain reload drops in-memory sessions (start over). " +
            "Re-calling AacBeginSystem with the same systemName fails — call AacDiscardSession first to reset.")]
        public static string AacBeginSystem(string systemName, string avatarRootName, string assetDir = "")
        {
            if (string.IsNullOrWhiteSpace(systemName))
                return "Error: systemName must not be empty.";
            if (_sessions.ContainsKey(systemName))
                return $"Error: Session '{systemName}' already exists. Call AacDiscardSession first to reset.";

            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var systemKey = systemName.Replace(" ", "_");
            var dir = ResolveAssetDir(assetDir, avatarRootName);
            ToolUtility.EnsureAssetDirectory(dir);
            var containerPath = ContainerPath(dir, systemKey);

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(containerPath) != null)
                AssetDatabase.DeleteAsset(containerPath);

            var container = ScriptableObject.CreateInstance<AacGeneratedContainer>();
            AssetDatabase.CreateAsset(container, containerPath);

            var config = new AacConfiguration
            {
                SystemName       = systemKey,
                AnimatorRoot     = avatarRoot.transform,
                DefaultValueRoot = avatarRoot.transform,
                AssetContainer   = container,
                ContainerMode    = AacConfiguration.Container.Everything,
                AssetKey         = systemKey,
                DefaultsProvider = new AacDefaultsProvider(writeDefaults: false)
            };
            var aac  = AacV1.Create(config);
            var ctrl = aac.NewAnimatorController();

            _sessions[systemName] = new Session
            {
                SystemKey     = systemKey,
                OriginalName  = systemName,
                AvatarRoot    = avatarRoot,
                AssetDir      = dir,
                ContainerPath = containerPath,
                Aac           = aac,
                Controller    = ctrl,
                CreatedAt     = DateTime.UtcNow
            };

            return $"Success: Session '{systemName}' started.\n" +
                   $"  Container: {containerPath}\n" +
                   $"  AvatarRoot: {avatarRoot.name}\n" +
                   $"  Next: AacExecuteScript('{systemName}', code) — see its description for the in-scope variables and namespaces.";
        }

        // ========== 2. AacExecuteScript ==========

        [AgentTool(
            "Execute a C# snippet against an active AAC session. Body runs inside a static method with these in-scope parameters:\n" +
            "  AacFlBase aac, AacFlController ctrl, GameObject avatarRoot\n" +
            "Available namespaces (auto-imported): System, System.Linq, System.Collections.Generic, UnityEngine, UnityEditor, UnityEditor.Animations, AnimatorAsCode.V1, AnimatorAsCode.V1.VRC, AnimatorAsCode.V1.ModularAvatar, VRC.SDK3.Avatars.Components.\n" +
            "Use AAC fluent builder syntax (https://docs.hai-vr.dev/docs/products/animator-as-code/functions/base). For MA integration call MaAc.Create(holder) where holder is a GameObject you create under avatarRoot.\n" +
            "Use `return \"summary\";` to log what you built — the string is stored in the session and returned. Multiple calls accumulate.\n" +
            "Compile errors are reported with line numbers; your code starts at line 1. Requires user confirmation per call.",
            Risk = ToolRisk.Caution)]
        public static string AacExecuteScript(string systemName, string code)
        {
            if (!_sessions.TryGetValue(systemName, out var session))
                return $"Error: Session '{systemName}' not found. Call AacBeginSystem first.";
            if (string.IsNullOrWhiteSpace(code))
                return "Error: code must not be empty.";

            if (!AgentSettings.RequestConfirmation(
                "AAC スクリプト実行",
                $"systemName: {systemName}\n\n以下のコードを実行します:\n\n{Truncate(code, 1200)}"))
                return "Cancelled: User denied script execution.";

            Debug.Log($"[UnityAgent] AacExecuteScript ({systemName}):\n{code}");

            var fullSource = BuildAacSource(code);
            var provider = new CSharpCodeProvider();
            var compilerParams = new CompilerParameters
            {
                GenerateInMemory   = true,
                GenerateExecutable = false
            };
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(asm.Location))
                        compilerParams.ReferencedAssemblies.Add(asm.Location);
                }
                catch { /* dynamic assemblies have no Location */ }
            }

            var results = provider.CompileAssemblyFromSource(compilerParams, fullSource);
            if (results.Errors.HasErrors)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Compile Error:");
                foreach (CompilerError error in results.Errors)
                {
                    if (!error.IsWarning)
                        sb.AppendLine($"  Line {error.Line - LineOffset}: {error.ErrorText}");
                }
                return sb.ToString().TrimEnd();
            }

            try
            {
                var assembly = results.CompiledAssembly;
                var type     = assembly.GetType("AacScript.DynamicScript");
                var method   = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                var result   = method.Invoke(null, new object[] { session.Aac, session.Controller, session.AvatarRoot });

                var summary = result?.ToString() ?? "(no summary returned)";
                session.ScriptLog.Add(summary);
                return $"Success: Script executed.\n{summary}";
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

        // 自動 using の総行数 + namespace/class/method の宣言ライン数。
        // BuildAacSource を変更した場合は同期して更新する。
        private const int LineOffset = 13;

        private static string BuildAacSource(string code)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEditor.Animations;");
            sb.AppendLine("using AnimatorAsCode.V1;");
            sb.AppendLine("using AnimatorAsCode.V1.VRC;");
            sb.AppendLine("using AnimatorAsCode.V1.ModularAvatar;");
            sb.AppendLine("using VRC.SDK3.Avatars.Components;");
            sb.AppendLine("namespace AacScript {");
            sb.AppendLine("  public static class DynamicScript {");
            sb.AppendLine("    public static object Execute(AacFlBase aac, AacFlController ctrl, GameObject avatarRoot) {");
            sb.AppendLine(code);
            if (!code.Contains("return "))
                sb.AppendLine("      return null;");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== 3. AacCommitSystem ==========

        [AgentTool(
            "Commit an in-progress AAC system. Confirms with the user, runs AssetDatabase.SaveAssets, and removes the session from memory. " +
            "All AAC assets are already persisted incrementally during AacExecuteScript (AAC's AddObjectToAsset is called immediately), " +
            "so commit is mostly a 'finish + save + checkpoint' step. " +
            "If you created a holder GameObject during scripting, it remains in the scene.")]
        public static string AacCommitSystem(string systemName)
        {
            if (!_sessions.TryGetValue(systemName, out var session))
                return $"Error: Session '{systemName}' not found.";

            if (!AgentSettings.RequestConfirmation(
                "AAC システムのコミット",
                $"systemName: {systemName}\n" +
                $"  Container: {session.ContainerPath}\n" +
                $"  Script invocations: {session.ScriptLog.Count}\n" +
                $"これでセッションを確定し、メモリから解放します。"))
                return "Cancelled: User denied the operation.";

            EditorUtility.SetDirty(session.AvatarRoot);
            AssetDatabase.SaveAssets();
            _sessions.Remove(systemName);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Committed AAC system '{systemName}'.");
            sb.AppendLine($"  Container: {session.ContainerPath}");
            sb.AppendLine($"  Script invocations: {session.ScriptLog.Count}");
            return sb.ToString().TrimEnd();
        }

        // ========== 4. AacDiscardSession ==========

        [AgentTool(
            "Discard an in-progress AAC session: deletes the container .asset (incl. all sub-assets created so far) and removes the session from memory. " +
            "Use this to abort a session and start over. Any holder GameObject created during AacExecuteScript is NOT removed by this tool — clean it up manually.",
            Risk = ToolRisk.Dangerous)]
        public static string AacDiscardSession(string systemName)
        {
            if (!_sessions.TryGetValue(systemName, out var session))
                return $"Error: Session '{systemName}' not found.";

            if (!AgentSettings.RequestConfirmation(
                "AAC セッション破棄",
                $"以下を削除します:\n  Session: {systemName}\n  Container: {session.ContainerPath}\n" +
                $"※ シーン内に作成された holder GameObject は削除されません。"))
                return "Cancelled: User denied the operation.";

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(session.ContainerPath) != null)
                AssetDatabase.DeleteAsset(session.ContainerPath);
            _sessions.Remove(systemName);
            AssetDatabase.SaveAssets();

            return $"Success: Discarded session '{systemName}'.";
        }

        // ========== 5. AacListSessions ==========

        [AgentTool("List currently active in-memory AAC sessions.", Risk = ToolRisk.Safe)]
        public static string AacListSessions()
        {
            if (_sessions.Count == 0)
                return "(no active sessions)";

            var sb = new StringBuilder();
            sb.AppendLine($"Active sessions ({_sessions.Count}):");
            foreach (var kv in _sessions)
            {
                var s = kv.Value;
                sb.AppendLine($"  - {kv.Key}: avatar={s.AvatarRoot?.name ?? "(null)"}, scripts={s.ScriptLog.Count}, " +
                              $"age={(DateTime.UtcNow - s.CreatedAt).TotalSeconds:F0}s, container={s.ContainerPath}");
            }
            return sb.ToString().TrimEnd();
        }

        // ========== 6. AacInspectSession ==========

        [AgentTool(
            "Inspect an AAC session: shows the avatar root, asset paths, and summaries returned by previous AacExecuteScript calls.",
            Risk = ToolRisk.Safe)]
        public static string AacInspectSession(string systemName)
        {
            if (!_sessions.TryGetValue(systemName, out var session))
                return $"Error: Session '{systemName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Session '{systemName}':");
            sb.AppendLine($"  AvatarRoot: {session.AvatarRoot?.name}");
            sb.AppendLine($"  AssetDir:   {session.AssetDir}");
            sb.AppendLine($"  Container:  {session.ContainerPath}");
            sb.AppendLine($"  Age:        {(DateTime.UtcNow - session.CreatedAt).TotalSeconds:F0}s");
            sb.AppendLine($"  Script log ({session.ScriptLog.Count} entries):");
            for (int i = 0; i < session.ScriptLog.Count; i++)
                sb.AppendLine($"    [{i}] {Truncate(session.ScriptLog[i], 300)}");
            return sb.ToString().TrimEnd();
        }

        // ========== Internal: Test window access ==========

        /// <summary>テストウィンドウからセッション一覧を参照するためのヘルパー。</summary>
        internal static IReadOnlyDictionary<string, object> ListSessionsForTestWindow()
        {
            var dict = new Dictionary<string, object>();
            foreach (var kv in _sessions)
                dict[kv.Key] = new
                {
                    kv.Value.AvatarRoot,
                    kv.Value.ContainerPath,
                    kv.Value.ScriptLog.Count,
                    kv.Value.CreatedAt
                };
            return dict;
        }

#endif
    }
}
