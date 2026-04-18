// TexTransTool (TTT) bake / preview tools.
// Tier 3: invoke internal-only entry points (ManualBake, PreviewUtility) via
// reflection because their classes are internal. Methods themselves are
// public static so invocation is safe.
#if NET_RS64_TTT

using System;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AjisaiFlow.UnityAgent.TexTransTool.Editor
{
    public static class TttBakeTools
    {
        private const string Category = "TexTransTool";
        private const string Version = "0.1.0";
        private const string EditorAssembly = "net.rs64.tex-trans-tool.editor";
        private const string ManualBakeType = "net.rs64.TexTransTool.Build.ManualBake";
        private const string PreviewUtilityType = "net.rs64.TexTransTool.Editor.OtherMenuItem.PreviewUtility";

        [AgentTool("TTT のアバターを手動ベイクする。元アバターの複製を作り (Z 方向に +2m オフセット)、複製側のテクスチャをベイクする。元のアバターはそのまま残る。内部で PreviewUtility.ExitPreviews() が先に呼ばれる。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Caution)]
        public static string TttManualBake(string avatarHierarchyPath)
        {
            if (string.IsNullOrWhiteSpace(avatarHierarchyPath))
                return "Error: avatarHierarchyPath is empty.";

            var target = FindInScenes(avatarHierarchyPath);
            if (target == null)
                return $"Error: avatar '{avatarHierarchyPath}' not found in scene.";

            var method = FindPublicStaticMethod(ManualBakeType, EditorAssembly, "ManualBakeAvatar", typeof(GameObject));
            if (method == null)
                return $"Error: could not resolve {ManualBakeType}.ManualBakeAvatar. TTT may have changed.";

            try
            {
                method.Invoke(null, new object[] { target });
                return "{\"ok\":true,\"action\":\"ManualBakeAvatar\",\"avatar\":\"" + EscapeJson(avatarHierarchyPath) + "\"}";
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return $"Error: ManualBakeAvatar failed: {inner.GetType().Name}: {inner.Message}";
            }
            catch (Exception e)
            {
                return $"Error: {e.GetType().Name}: {e.Message}";
            }
        }

        [AgentTool("TTT の NDMF プレビューを終了する (RealTime / OneTime 両方)。ベイクや書込み系ツール呼出し前に呼ぶと安全。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Safe)]
        public static string TttExitPreviews()
        {
            var method = FindPublicStaticMethod(PreviewUtilityType, EditorAssembly, "ExitPreviews");
            if (method == null)
                return $"Error: could not resolve {PreviewUtilityType}.ExitPreviews. TTT may have changed.";

            try
            {
                method.Invoke(null, Array.Empty<object>());
                return "{\"ok\":true,\"action\":\"ExitPreviews\"}";
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return $"Error: ExitPreviews failed: {inner.GetType().Name}: {inner.Message}";
            }
            catch (Exception e)
            {
                return $"Error: {e.GetType().Name}: {e.Message}";
            }
        }

        // ------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------

        private static MethodInfo FindPublicStaticMethod(string typeFullName, string assemblyName, string methodName, params Type[] argTypes)
        {
            Type type = Type.GetType($"{typeFullName}, {assemblyName}", throwOnError: false);
            if (type == null)
            {
                // Fallback: scan loaded assemblies.
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != assemblyName) continue;
                    type = asm.GetType(typeFullName, throwOnError: false);
                    if (type != null) break;
                }
            }
            if (type == null) return null;

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            if (argTypes == null || argTypes.Length == 0)
                return type.GetMethod(methodName, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
            return type.GetMethod(methodName, flags, binder: null, types: argTypes, modifiers: null);
        }

        private static GameObject FindInScenes(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath)) return null;
            var segments = hierarchyPath.Split('/');
            if (segments.Length == 0) return null;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != segments[0]) continue;
                    var current = root.transform;
                    bool ok = true;
                    for (int s = 1; s < segments.Length; s++)
                    {
                        var child = current.Find(segments[s]);
                        if (child == null) { ok = false; break; }
                        current = child;
                    }
                    if (ok) return current.gameObject;
                }
            }
            return null;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append($"\\u{(int)ch:X4}");
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

#endif
