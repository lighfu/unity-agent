// TexTransTool (TTT) authoring tools.
// Tier 2: create + configure TTT components on scene GameObjects so an AI
// agent can place decals, texture blends, and atlasing directly.
#if NET_RS64_TTT

using System;
using System.Collections.Generic;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
using net.rs64.TexTransTool.TextureAtlas;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AjisaiFlow.UnityAgent.TexTransTool.Editor
{
    public static class TttAuthoringTools
    {
        private const string Category = "TexTransTool";
        private const string Version = "0.1.0";

        [AgentTool("TTT の SimpleDecal コンポーネントをシーンに作成して設定する。parentPath は配置する親 GameObject のヒエラルキパス、targetRenderersCsv は対象 Renderer の CSV (パス)。decalTexturePath は Texture2D アセットパス。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Caution)]
        public static string TttAddSimpleDecal(
            string parentPath,
            string decalTexturePath,
            string targetRenderersCsv,
            string name = "TTT SimpleDecal",
            string blendTypeKey = "Normal",
            float scaleX = 0.1f,
            float scaleY = 0.1f,
            float scaleZ = 0.1f,
            float localPositionX = 0f,
            float localPositionY = 0f,
            float localPositionZ = 0f,
            string targetPropertyName = "_MainTex")
        {
            var parent = FindInScenes(parentPath);
            if (parent == null) return $"Error: parent '{parentPath}' not found in scene.";

            var decal = AssetDatabase.LoadAssetAtPath<Texture2D>(decalTexturePath);
            if (decal == null) return $"Error: decal Texture2D not found at '{decalTexturePath}'.";

            var renderers = ResolveRenderers(targetRenderersCsv, out var missing);
            if (missing.Count > 0)
                return $"Error: renderers not found: {string.Join(", ", missing)}";
            if (renderers.Count == 0)
                return "Error: targetRenderersCsv resolved to zero renderers.";

            var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "TTT SimpleDecal" : name);
            Undo.RegisterCreatedObjectUndo(go, "Create TTT SimpleDecal");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(localPositionX, localPositionY, localPositionZ);
            go.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);

            var simpleDecal = Undo.AddComponent<SimpleDecal>(go);
            simpleDecal.DecalTexture = decal;
            simpleDecal.BlendTypeKey = string.IsNullOrWhiteSpace(blendTypeKey) ? "Normal" : blendTypeKey;
            simpleDecal.TargetPropertyName = new net.rs64.TexTransTool.PropertyName(
                string.IsNullOrWhiteSpace(targetPropertyName) ? "_MainTex" : targetPropertyName);
            simpleDecal.RendererSelector.Mode = RendererSelectMode.Manual;
            simpleDecal.RendererSelector.ManualSelections = new List<Renderer>(renderers);

            EditorUtility.SetDirty(go);

            return Json.Ok(new (string k, string v)[]
            {
                ("created", GetHierarchyPath(go)),
                ("component", nameof(SimpleDecal)),
                ("decal", decalTexturePath),
                ("blend", simpleDecal.BlendTypeKey),
                ("rendererCount", renderers.Count.ToString()),
            });
        }

        [AgentTool("TTT の TextureBlender コンポーネントを作成する。対象テクスチャ (targetTexturePath) を参照するマテリアルを自動検出し blendTexture を重ねる。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Caution)]
        public static string TttAddTextureBlender(
            string parentPath,
            string targetTexturePath,
            string blendTexturePath,
            string name = "TTT TextureBlender",
            string blendTypeKey = "Normal")
        {
            var parent = FindInScenes(parentPath);
            if (parent == null) return $"Error: parent '{parentPath}' not found in scene.";

            var targetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(targetTexturePath);
            if (targetTex == null) return $"Error: target Texture2D not found at '{targetTexturePath}'.";

            var blendTex = AssetDatabase.LoadAssetAtPath<Texture2D>(blendTexturePath);
            if (blendTex == null) return $"Error: blend Texture2D not found at '{blendTexturePath}'.";

            var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "TTT TextureBlender" : name);
            Undo.RegisterCreatedObjectUndo(go, "Create TTT TextureBlender");
            go.transform.SetParent(parent.transform, false);

            var tb = Undo.AddComponent<TextureBlender>(go);
            tb.TargetTexture = new TextureSelector { SelectTexture = targetTex };
            tb.BlendTexture = blendTex;
            tb.BlendTypeKey = string.IsNullOrWhiteSpace(blendTypeKey) ? "Normal" : blendTypeKey;

            EditorUtility.SetDirty(go);

            return Json.Ok(new (string k, string v)[]
            {
                ("created", GetHierarchyPath(go)),
                ("component", nameof(TextureBlender)),
                ("target", targetTexturePath),
                ("blend", blendTexturePath),
                ("blendTypeKey", tb.BlendTypeKey),
            });
        }

        [AgentTool("TTT の AtlasTexture コンポーネントを作成する。targetMaterialsCsv は atlas 対象 Material のアセットパス CSV。atlasSize はアトラス全体のピクセルサイズ (2 の累乗推奨)。",
            Author = "ajisaiflow",
            Version = Version,
            Category = Category,
            Risk = ToolRisk.Caution)]
        public static string TttAddAtlasTexture(
            string parentPath,
            string targetMaterialsCsv,
            string name = "TTT AtlasTexture",
            int atlasSize = 2048)
        {
            var parent = FindInScenes(parentPath);
            if (parent == null) return $"Error: parent '{parentPath}' not found in scene.";

            var mats = new List<Material>();
            var missing = new List<string>();
            foreach (var raw in (targetMaterialsCsv ?? string.Empty).Split(','))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) missing.Add(p); else mats.Add(m);
            }
            if (missing.Count > 0)
                return $"Error: materials not found: {string.Join(", ", missing)}";
            if (mats.Count == 0)
                return "Error: targetMaterialsCsv resolved to zero materials.";
            if (atlasSize <= 0 || atlasSize > 8192)
                return $"Error: atlasSize '{atlasSize}' out of range (1..8192).";

            var go = new GameObject(string.IsNullOrWhiteSpace(name) ? "TTT AtlasTexture" : name);
            Undo.RegisterCreatedObjectUndo(go, "Create TTT AtlasTexture");
            go.transform.SetParent(parent.transform, false);

            var atlas = Undo.AddComponent<AtlasTexture>(go);
            atlas.AtlasTargetMaterials = new List<Material?>(mats);
            atlas.AtlasSetting.AtlasTextureSize = atlasSize;

            EditorUtility.SetDirty(go);

            return Json.Ok(new (string k, string v)[]
            {
                ("created", GetHierarchyPath(go)),
                ("component", nameof(AtlasTexture)),
                ("materialCount", mats.Count.ToString()),
                ("atlasSize", atlasSize.ToString()),
            });
        }

        // ------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------

        private static List<Renderer> ResolveRenderers(string csv, out List<string> missing)
        {
            var list = new List<Renderer>();
            missing = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;

            foreach (var raw in csv.Split(','))
            {
                var path = raw.Trim();
                if (path.Length == 0) continue;
                var go = FindInScenes(path);
                if (go == null) { missing.Add(path); continue; }
                var r = go.GetComponent<Renderer>();
                if (r == null) { missing.Add($"{path} (no Renderer)"); continue; }
                list.Add(r);
            }
            return list;
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

        internal static class Json
        {
            public static string Ok((string key, string value)[] fields)
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true");
                foreach (var (key, value) in fields)
                {
                    sb.Append(',');
                    sb.Append('"').Append(Escape(key)).Append("\":\"").Append(Escape(value)).Append('"');
                }
                sb.Append('}');
                return sb.ToString();
            }

            private static string Escape(string s)
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
}

#endif
