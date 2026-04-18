// TexTransTool (TTT) introspection tools.
// Read-only surface for AI agents to understand what TTT components exist
// in a scene and what the framework can do. Guarded by NET_RS64_TTT so this
// file compiles to nothing when the tex-trans-tool package is absent.
#if NET_RS64_TTT

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using net.rs64.TexTransTool;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AjisaiFlow.UnityAgent.TexTransTool.Editor
{
    public static class TttReadonlyTools
    {
        private const string Category = "TexTransTool";
        private const string Version = "0.1.0";

        private static readonly Dictionary<Type, PropertyInfo> s_phasePropCache = new();

        [AgentTool("TexTransTool の 7 フェーズを順序付きで返す（AI が TTT の実行順を理解するためのドキュメントツール）。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Safe)]
        public static string TttDescribePhases()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"phases\":[");

            var phases = new (TexTransPhase phase, string ja)[]
            {
                (TexTransPhase.MaterialModification, "マテリアル自体を差し替える/上書きする段階"),
                (TexTransPhase.BeforeUVModification, "UV 変更の前処理"),
                (TexTransPhase.UVModification, "UV を書き換える段階（AtlasTexture 等）"),
                (TexTransPhase.AfterUVModification, "UV 変更後の追加処理"),
                (TexTransPhase.PostProcessing, "テクスチャ後処理"),
                (TexTransPhase.Optimizing, "最適化（AAO 連携等）"),
                (TexTransPhase.UnDefined, "フェーズ未指定（通常は使われない）"),
            };

            for (int i = 0; i < phases.Length; i++)
            {
                var (phase, ja) = phases[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"order\":").Append(i).Append(',');
                sb.Append("\"enumValue\":").Append((int)phase).Append(',');
                sb.Append("\"name\":\"").Append(phase.ToString()).Append("\",");
                sb.Append("\"ja\":\"").Append(EscapeJson(ja)).Append("\"");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AgentTool("ITexTransToolStableComponent を実装する TTT コンポーネント型を列挙する。フィールドが安定でバージョン間互換が保証されているため、AI が参照・提案すべき型の一覧として使える。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Safe)]
        public static string TttListStableComponents()
        {
            var results = new List<(string ns, string name, string menu, int stabilizeVersion)>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(Component).IsAssignableFrom(t)) continue;
                    if (!typeof(ITexTransToolStableComponent).IsAssignableFrom(t)) continue;

                    var menuAttr = (AddComponentMenu)Attribute.GetCustomAttribute(t, typeof(AddComponentMenu));
                    var menu = menuAttr != null ? GetAddComponentMenuName(menuAttr) : string.Empty;

                    int stableVer = TryGetStableVersion(t);

                    results.Add((t.Namespace ?? string.Empty, t.Name, menu ?? string.Empty, stableVer));
                }
            }

            results.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.ns, b.ns);
                return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
            });

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(results.Count).Append(",\"components\":[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"namespace\":\"").Append(EscapeJson(r.ns)).Append("\",");
                sb.Append("\"name\":\"").Append(EscapeJson(r.name)).Append("\",");
                sb.Append("\"addComponentMenu\":\"").Append(EscapeJson(r.menu)).Append("\",");
                sb.Append("\"stabilizeSaveDataVersion\":").Append(r.stabilizeVersion);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AgentTool("指定したアバター配下の TTT コンポーネントを列挙する。各コンポーネントのパス・型・フェーズ・stable 判定・enabled を返す。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Safe)]
        public static string TttListComponents(string avatarHierarchyPath)
        {
            if (string.IsNullOrWhiteSpace(avatarHierarchyPath))
                return "Error: avatarHierarchyPath is empty.";

            var root = FindGameObjectInScenes(avatarHierarchyPath);
            if (root == null)
                return $"Error: GameObject '{avatarHierarchyPath}' not found in current scene (searched active + inactive).";

            var components = root.GetComponentsInChildren<TexTransMonoBase>(true);

            var sb = new StringBuilder();
            sb.Append("{\"avatarPath\":\"").Append(EscapeJson(avatarHierarchyPath)).Append("\",");
            sb.Append("\"count\":").Append(components.Length).Append(",");
            sb.Append("\"components\":[");

            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                if (i > 0) sb.Append(',');

                var type = c.GetType();
                var path = GetHierarchyPath(c.gameObject);
                var phase = TryGetPhase(c);
                bool stable = c is ITexTransToolStableComponent;
                bool enabled = IsBehaviourEnabled(c);

                sb.Append('{');
                sb.Append("\"path\":\"").Append(EscapeJson(path)).Append("\",");
                sb.Append("\"type\":\"").Append(EscapeJson(type.Name)).Append("\",");
                sb.Append("\"namespace\":\"").Append(EscapeJson(type.Namespace ?? string.Empty)).Append("\",");
                sb.Append("\"phase\":").Append(phase.HasValue ? $"\"{phase.Value}\"" : "null").Append(',');
                sb.Append("\"stable\":").Append(stable ? "true" : "false").Append(',');
                sb.Append("\"enabled\":").Append(enabled ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------

        private static TexTransPhase? TryGetPhase(Component component)
        {
            if (component is not TexTransBehavior) return null;
            var type = component.GetType();
            if (!s_phasePropCache.TryGetValue(type, out var prop))
            {
                prop = type.GetProperty(
                    "PhaseDefine",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                s_phasePropCache[type] = prop;
            }
            if (prop == null) return null;
            try
            {
                var raw = prop.GetValue(component);
                return raw is TexTransPhase tp ? tp : null;
            }
            catch
            {
                return null;
            }
        }

        private static int TryGetStableVersion(Type type)
        {
            // ITexTransToolStableComponent.StabilizeSaveDataVersion is a
            // concrete instance property on each type. We can avoid creating
            // a full MonoBehaviour instance by reading the default value via
            // a field-backed implementation when possible. When not possible
            // we fall back to -1.
            var prop = type.GetProperty(
                "StabilizeSaveDataVersion",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null) return -1;

            try
            {
                var instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                var raw = prop.GetValue(instance);
                return raw is int i ? i : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string GetAddComponentMenuName(AddComponentMenu attr)
        {
            var t = typeof(AddComponentMenu);
            var prop = t.GetProperty(
                "componentMenu",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(attr) is string sp) return sp;
            var field = t.GetField(
                "m_AddComponentMenu",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? t.GetField(
                    "componentMenu",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(attr) is string sf) return sf;
            return string.Empty;
        }

        private static bool IsBehaviourEnabled(Component c)
        {
            if (c is Behaviour b) return b.enabled;
            return true;
        }

        private static GameObject FindGameObjectInScenes(string hierarchyPath)
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

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;
            var stack = new Stack<string>();
            var t = go.transform;
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
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
