using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class LilToonTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
#if LILTOON

        // ========== Helpers ==========

        private static Material LoadMaterial(string materialPath)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        private static bool IsLilToonMaterial(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            var name = mat.shader.name;
            return name.Contains("lilToon") || name.Contains("liltoon") || name.Contains("lil/");
        }

        private static string GetColorPropertyName(string property)
        {
            switch (property)
            {
                case "Main": return "_Color";
                case "Outline": return "_OutlineColor";
                case "Emission": return "_EmissionColor";
                case "Emission2nd": return "_Emission2ndColor";
                case "Shadow": return "_ShadowColor";
                case "Shadow2nd": return "_Shadow2ndColor";
                case "Shadow3rd": return "_Shadow3rdColor";
                case "Rim": return "_RimColor";
                case "MatCap": return "_MatCapColor";
                case "MatCap2nd": return "_MatCap2ndColor";
                default: return null;
            }
        }

        private static string GetTexturePropertyName(string property)
        {
            switch (property)
            {
                case "Main": return "_MainTex";
                case "Bump": case "Normal": return "_BumpMap";
                case "Emission": return "_EmissionMap";
                case "Emission2nd": return "_Emission2ndMap";
                case "Outline": return "_OutlineTex";
                case "Shadow": return "_ShadowColorTex";
                case "Shadow2nd": return "_Shadow2ndColorTex";
                case "MatCap": return "_MatCapTex";
                case "MatCap2nd": return "_MatCap2ndTex";
                case "Rim": return "_RimColorTex";
                default: return null;
            }
        }

        private static string GetFloatPropertyName(string property)
        {
            switch (property)
            {
                case "Cutoff": return "_Cutoff";
                case "OutlineWidth": return "_OutlineWidth";
                case "BumpScale": return "_BumpScale";
                case "Smoothness": return "_Smoothness";
                case "Metallic": return "_Metallic";
                case "AsUnlit": return "_AsUnlit";
                case "ShadowStrength": return "_ShadowStrength";
                case "ShadowBorder": return "_ShadowBorder";
                case "ShadowBlur": return "_ShadowBlur";
                case "RimBorder": return "_RimBorder";
                case "RimBlur": return "_RimBlur";
                default: return null;
            }
        }

        // ========== 1. InspectLilToonMaterial ==========

        [AgentTool("Inspect a lilToon material. Shows shader variant, rendering mode, and key properties (colors, textures, outline, emission, etc.). materialPath: asset path like 'Assets/xxx/material.mat'.")]
        public static string InspectLilToonMaterial(string materialPath)
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";

            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material. Shader: {mat.shader.name}";

            var sb = new StringBuilder();
            sb.AppendLine($"lilToon Material: {mat.name}");
            sb.AppendLine($"  Shader: {mat.shader.name}");
            sb.AppendLine($"  RenderQueue: {mat.renderQueue}");

            // Main
            if (mat.HasProperty("_Color"))
            {
                var c = mat.GetColor("_Color");
                sb.AppendLine($"  Main Color: ({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})");
            }
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                sb.AppendLine($"  Main Texture: {AssetDatabase.GetAssetPath(mat.GetTexture("_MainTex"))}");

            // Cutoff
            if (mat.HasProperty("_Cutoff"))
                sb.AppendLine($"  Cutoff: {mat.GetFloat("_Cutoff"):F3}");

            // Outline
            if (mat.HasProperty("_OutlineColor"))
            {
                var oc = mat.GetColor("_OutlineColor");
                sb.AppendLine($"  Outline Color: ({oc.r:F3}, {oc.g:F3}, {oc.b:F3}, {oc.a:F3})");
            }
            if (mat.HasProperty("_OutlineWidth"))
                sb.AppendLine($"  Outline Width: {mat.GetFloat("_OutlineWidth"):F4}");

            // Shadow
            if (mat.HasProperty("_UseShadow") && mat.GetFloat("_UseShadow") > 0.5f)
            {
                sb.AppendLine($"  Shadow: Enabled");
                if (mat.HasProperty("_ShadowStrength"))
                    sb.AppendLine($"    Strength: {mat.GetFloat("_ShadowStrength"):F3}");
                if (mat.HasProperty("_ShadowColor"))
                {
                    var sc = mat.GetColor("_ShadowColor");
                    sb.AppendLine($"    Color: ({sc.r:F3}, {sc.g:F3}, {sc.b:F3}, {sc.a:F3})");
                }
            }

            // Emission
            if (mat.HasProperty("_UseEmission") && mat.GetFloat("_UseEmission") > 0.5f)
            {
                sb.AppendLine($"  Emission: Enabled");
                if (mat.HasProperty("_EmissionColor"))
                {
                    var ec = mat.GetColor("_EmissionColor");
                    sb.AppendLine($"    Color: ({ec.r:F3}, {ec.g:F3}, {ec.b:F3}, {ec.a:F3})");
                }
            }

            // Rim
            if (mat.HasProperty("_UseRim") && mat.GetFloat("_UseRim") > 0.5f)
            {
                sb.AppendLine($"  Rim Light: Enabled");
                if (mat.HasProperty("_RimColor"))
                {
                    var rc = mat.GetColor("_RimColor");
                    sb.AppendLine($"    Color: ({rc.r:F3}, {rc.g:F3}, {rc.b:F3}, {rc.a:F3})");
                }
            }

            // MatCap
            if (mat.HasProperty("_UseMatCap") && mat.GetFloat("_UseMatCap") > 0.5f)
            {
                sb.AppendLine($"  MatCap: Enabled");
                if (mat.HasProperty("_MatCapTex") && mat.GetTexture("_MatCapTex") != null)
                    sb.AppendLine($"    Texture: {AssetDatabase.GetAssetPath(mat.GetTexture("_MatCapTex"))}");
            }

            // Normal Map
            if (mat.HasProperty("_UseBumpMap") && mat.GetFloat("_UseBumpMap") > 0.5f)
            {
                sb.AppendLine($"  Normal Map: Enabled");
                if (mat.HasProperty("_BumpScale"))
                    sb.AppendLine($"    Scale: {mat.GetFloat("_BumpScale"):F3}");
            }

            // Reflection
            if (mat.HasProperty("_UseReflection") && mat.GetFloat("_UseReflection") > 0.5f)
            {
                sb.AppendLine($"  Reflection: Enabled");
                if (mat.HasProperty("_Metallic"))
                    sb.AppendLine($"    Metallic: {mat.GetFloat("_Metallic"):F3}");
                if (mat.HasProperty("_Smoothness"))
                    sb.AppendLine($"    Smoothness: {mat.GetFloat("_Smoothness"):F3}");
            }

            // Lighting
            if (mat.HasProperty("_AsUnlit"))
                sb.AppendLine($"  AsUnlit: {mat.GetFloat("_AsUnlit"):F3}");

            return sb.ToString().TrimEnd();
        }

        // ========== 2. SetLilToonColors ==========

        [AgentTool("Set lilToon color properties. property: 'Main', 'Outline', 'Emission', 'Emission2nd', 'Shadow', 'Shadow2nd', 'Shadow3rd', 'Rim', 'MatCap', 'MatCap2nd'. color: 'r,g,b,a' (0-1).")]
        public static string SetLilToonColors(string materialPath, string property, string color)
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";
            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material.";

            var propName = GetColorPropertyName(property);
            if (propName == null)
                return $"Error: Unknown color property '{property}'. Use: Main, Outline, Emission, Emission2nd, Shadow, Shadow2nd, Shadow3rd, Rim, MatCap, MatCap2nd.";

            if (!mat.HasProperty(propName))
                return $"Error: Material does not have property '{propName}'.";

            var parts = color.Split(',');
            if (parts.Length < 3 || parts.Length > 4)
                return "Error: color must be 'r,g,b' or 'r,g,b,a' (0-1).";

            if (!float.TryParse(parts[0].Trim(), out float r) ||
                !float.TryParse(parts[1].Trim(), out float g) ||
                !float.TryParse(parts[2].Trim(), out float b))
                return "Error: Invalid color values.";
            float a = 1f;
            if (parts.Length == 4 && !float.TryParse(parts[3].Trim(), out a))
                return "Error: Invalid alpha value.";

            Undo.RecordObject(mat, "Set lilToon Color");
            mat.SetColor(propName, new Color(r, g, b, a));
            EditorUtility.SetDirty(mat);

            return $"Success: Set {property} color ({propName}) to ({r:F3}, {g:F3}, {b:F3}, {a:F3}) on '{mat.name}'.";
        }

        // ========== 3. SetLilToonFloat ==========

        [AgentTool("Set a lilToon float property. property: 'Cutoff' (0-1), 'OutlineWidth' (0+), 'BumpScale' (-10 to 10), 'Smoothness' (0-1), 'Metallic' (0-1), 'AsUnlit' (0-1), 'ShadowStrength' (0-1), 'ShadowBorder', 'ShadowBlur', 'RimBorder', 'RimBlur'. Or use raw property name like '_PropertyName'.")]
        public static string SetLilToonFloat(string materialPath, string property, float value)
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";
            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material.";

            var propName = GetFloatPropertyName(property);
            if (propName == null)
            {
                // Allow raw property name
                if (property.StartsWith("_"))
                    propName = property;
                else
                    return $"Error: Unknown float property '{property}'. Use: Cutoff, OutlineWidth, BumpScale, Smoothness, Metallic, AsUnlit, ShadowStrength, ShadowBorder, ShadowBlur, RimBorder, RimBlur, or raw '_PropertyName'.";
            }

            if (!mat.HasProperty(propName))
                return $"Error: Material does not have property '{propName}'.";

            Undo.RecordObject(mat, "Set lilToon Float");
            mat.SetFloat(propName, value);
            EditorUtility.SetDirty(mat);

            return $"Success: Set {propName} to {value:F4} on '{mat.name}'.";
        }

        // ========== 4. SetLilToonTexture ==========

        [AgentTool("Set a lilToon texture property. property: 'Main', 'Bump'/'Normal', 'Emission', 'Emission2nd', 'Outline', 'Shadow', 'Shadow2nd', 'MatCap', 'MatCap2nd', 'Rim'. texturePath: asset path to texture file.")]
        public static string SetLilToonTexture(string materialPath, string property, string texturePath)
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";
            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material.";

            var propName = GetTexturePropertyName(property);
            if (propName == null)
            {
                if (property.StartsWith("_"))
                    propName = property;
                else
                    return $"Error: Unknown texture property '{property}'. Use: Main, Bump/Normal, Emission, Emission2nd, Outline, Shadow, Shadow2nd, MatCap, MatCap2nd, Rim, or raw '_PropertyName'.";
            }

            if (!mat.HasProperty(propName))
                return $"Error: Material does not have property '{propName}'.";

            Texture tex = null;
            if (!string.IsNullOrEmpty(texturePath) && texturePath != "null" && texturePath != "none")
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                if (tex == null)
                    return $"Error: Texture not found at '{texturePath}'.";
            }

            Undo.RecordObject(mat, "Set lilToon Texture");
            mat.SetTexture(propName, tex);
            EditorUtility.SetDirty(mat);

            return $"Success: Set {propName} to '{(tex != null ? texturePath : "(none)")}' on '{mat.name}'.";
        }

        // ========== 5. ChangeLilToonRenderingMode ==========

        [AgentTool("Change lilToon shader rendering mode. mode: 'Opaque', 'Cutout', 'Transparent', 'Fur', 'FurCutout', 'FurTwoPass', 'Gem', 'Refraction', 'RefractionBlur'. outline: 'true'/'false'. lite: 'true'/'false'.")]
        public static string ChangeLilToonRenderingMode(string materialPath, string mode,
            string outline = "", string lite = "")
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";
            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material.";

            // Parse rendering mode enum via reflection (lilToon.RenderingMode is in lilToon namespace)
            Type renderingModeType = null;
            Type transparentModeType = null;
            Type lilMaterialUtilsType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (renderingModeType == null)
                    renderingModeType = assembly.GetType("lilToon.RenderingMode");
                if (transparentModeType == null)
                    transparentModeType = assembly.GetType("lilToon.TransparentMode");
                if (lilMaterialUtilsType == null)
                    lilMaterialUtilsType = assembly.GetType("lilToon.lilMaterialUtils");
                if (renderingModeType != null && transparentModeType != null && lilMaterialUtilsType != null)
                    break;
            }

            if (renderingModeType == null || lilMaterialUtilsType == null)
                return "Error: lilToon types not found. Is lilToon installed?";

            object renderingMode;
            try
            {
                renderingMode = Enum.Parse(renderingModeType, mode, true);
            }
            catch
            {
                return $"Error: Unknown rendering mode '{mode}'. Use: Opaque, Cutout, Transparent, Fur, FurCutout, FurTwoPass, Gem, Refraction, RefractionBlur.";
            }

            object transparentMode = Enum.Parse(transparentModeType, "Normal");
            bool isOutline = !string.IsNullOrEmpty(outline) && outline.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isLite = !string.IsNullOrEmpty(lite) && lite.Equals("true", StringComparison.OrdinalIgnoreCase);

            // If outline is not specified, detect from current shader
            if (string.IsNullOrEmpty(outline))
                isOutline = mat.shader.name.Contains("Outline");
            if (string.IsNullOrEmpty(lite))
                isLite = mat.shader.name.Contains("Lite") || mat.shader.name.Contains("lite");

            if (!AgentSettings.RequestConfirmation(
                "lilToon レンダリングモード変更",
                $"マテリアル: {mat.name}\n" +
                $"モード: {mode}\n" +
                $"アウトライン: {isOutline}, Lite: {isLite}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(mat, "Change lilToon Rendering Mode");

            // Call lilMaterialUtils.SetupMaterialWithRenderingMode via reflection (internal method)
            var setupMethod = lilMaterialUtilsType.GetMethod("SetupMaterialWithRenderingMode",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (setupMethod != null)
            {
                // SetupMaterialWithRenderingMode(Material, RenderingMode, TransparentMode, bool isoutl, bool islite, bool istess, bool ismulti)
                setupMethod.Invoke(null, new object[] { mat, renderingMode, transparentMode, isOutline, isLite, false, false });
            }
            else
            {
                // Fallback: manually set shader by name
                string shaderName = BuildShaderName(mode, isOutline, isLite);
                var shader = Shader.Find(shaderName);
                if (shader == null)
                    return $"Error: Shader '{shaderName}' not found.";
                mat.shader = shader;
            }

            EditorUtility.SetDirty(mat);

            return $"Success: Changed rendering mode of '{mat.name}' to {mode}.\n" +
                   $"  Shader: {mat.shader.name}\n" +
                   $"  Outline: {isOutline}, Lite: {isLite}";
        }

        private static string BuildShaderName(string mode, bool outline, bool lite)
        {
            if (lite)
            {
                if (outline)
                {
                    switch (mode)
                    {
                        case "Opaque": return "Hidden/lilToonLiteOutline";
                        case "Cutout": return "Hidden/lilToonLiteCutoutOutline";
                        case "Transparent": return "Hidden/lilToonLiteTransparentOutline";
                        default: return "Hidden/lilToonLite";
                    }
                }
                switch (mode)
                {
                    case "Opaque": return "Hidden/lilToonLite";
                    case "Cutout": return "Hidden/lilToonLiteCutout";
                    case "Transparent": return "Hidden/lilToonLiteTransparent";
                    default: return "Hidden/lilToonLite";
                }
            }

            if (outline)
            {
                switch (mode)
                {
                    case "Opaque": return "Hidden/lilToonOutline";
                    case "Cutout": return "Hidden/lilToonCutoutOutline";
                    case "Transparent": return "Hidden/lilToonTransparentOutline";
                    default: return "Hidden/lilToonOutline";
                }
            }

            switch (mode)
            {
                case "Opaque": return "lilToon";
                case "Cutout": return "Hidden/lilToonCutout";
                case "Transparent": return "Hidden/lilToonTransparent";
                case "Fur": return "Hidden/lilToonFur";
                case "FurCutout": return "Hidden/lilToonFurCutout";
                case "FurTwoPass": return "Hidden/lilToonFurTwoPass";
                case "Gem": return "Hidden/lilToonGem";
                case "Refraction": return "Hidden/lilToonRefraction";
                case "RefractionBlur": return "Hidden/lilToonRefractionBlur";
                default: return "lilToon";
            }
        }

        // ========== 6. ListLilToonPresets ==========

        [AgentTool("List all available lilToon presets with their categories.")]
        public static string ListLilToonPresets()
        {
            var guids = AssetDatabase.FindAssets("t:lilToonPreset");
            if (guids.Length == 0)
                return "No lilToon presets found.";

            var sb = new StringBuilder();
            sb.AppendLine($"lilToon presets ({guids.Length}):");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                // Extract category from path
                var dirName = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
                var lastDir = dirName?.Split('/').LastOrDefault() ?? "";
                sb.AppendLine($"  [{lastDir}] {name}");
                sb.AppendLine($"    Path: {path}");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 7. ApplyLilToonPreset ==========

        [AgentTool("Apply a lilToon preset to a material. Use ListLilToonPresets to see available presets. presetName: preset name or asset path.")]
        public static string ApplyLilToonPreset(string materialPath, string presetName)
        {
            var mat = LoadMaterial(materialPath);
            if (mat == null)
                return $"Error: Material not found at '{materialPath}'.";
            if (!IsLilToonMaterial(mat))
                return $"Error: '{materialPath}' is not a lilToon material.";

            // Find preset by name or path
            ScriptableObject preset = null;
            string presetPath = "";

            if (presetName.EndsWith(".asset"))
            {
                // Direct path
                preset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(presetName);
                presetPath = presetName;
            }
            else
            {
                // Search by name
                var guids = AssetDatabase.FindAssets("t:lilToonPreset");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                    {
                        preset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        presetPath = path;
                        break;
                    }
                }
            }

            if (preset == null)
                return $"Error: lilToon preset '{presetName}' not found. Use ListLilToonPresets to see available presets.";

            if (!AgentSettings.RequestConfirmation(
                "lilToon プリセットの適用",
                $"マテリアル: {mat.name}\n" +
                $"プリセット: {System.IO.Path.GetFileNameWithoutExtension(presetPath)}\n" +
                $"パス: {presetPath}"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(mat, "Apply lilToon Preset");

            // Call lilToonPreset.ApplyPreset via reflection (it's a public static method on a type without namespace)
            Type presetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                presetType = assembly.GetType("lilToonPreset");
                if (presetType != null) break;
            }

            if (presetType == null)
                return "Error: lilToonPreset type not found.";

            var applyMethod = presetType.GetMethod("ApplyPreset",
                BindingFlags.Static | BindingFlags.Public);
            if (applyMethod == null)
                return "Error: ApplyPreset method not found on lilToonPreset.";

            // ApplyPreset(Material material, lilToonPreset preset, bool ismulti)
            bool isMulti = mat.shader.name.Contains("Multi") || mat.shader.name.Contains("multi");
            applyMethod.Invoke(null, new object[] { mat, preset, isMulti });

            EditorUtility.SetDirty(mat);

            return $"Success: Applied preset '{System.IO.Path.GetFileNameWithoutExtension(presetPath)}' to '{mat.name}'.\n" +
                   $"  Current shader: {mat.shader.name}";
        }

        // ========== Batch Helpers ==========

        private static List<Material> CollectLilToonMaterials(GameObject avatarRoot)
        {
            var mats = new HashSet<Material>();
            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && IsLilToonMaterial(m)) mats.Add(m);
            return mats.ToList();
        }

        // ========== 8. ListLilToonMaterials ==========

        [AgentTool("List all lilToon materials used by an avatar with shader variant and renderer usage.")]
        public static string ListLilToonMaterials(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return $"No renderers found under '{avatarRootName}'.";

            // Collect materials with usage info
            var matUsage = new Dictionary<Material, List<string>>();
            foreach (var r in renderers)
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !IsLilToonMaterial(m)) continue;
                    if (!matUsage.ContainsKey(m))
                        matUsage[m] = new List<string>();
                    if (!matUsage[m].Contains(r.gameObject.name))
                        matUsage[m].Add(r.gameObject.name);
                }
            }

            if (matUsage.Count == 0)
                return $"No lilToon materials found under '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"lilToon materials on '{avatarRootName}' ({matUsage.Count}):");

            foreach (var kvp in matUsage)
            {
                var mat = kvp.Key;
                var users = kvp.Value;
                var path = AssetDatabase.GetAssetPath(mat);
                sb.AppendLine($"  {mat.name}");
                sb.AppendLine($"    Shader: {mat.shader.name}");
                sb.AppendLine($"    Path: {path}");
                sb.AppendLine($"    Used by: {string.Join(", ", users)}");
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 9. BatchSetLilToonFloat ==========

        [AgentTool("Set a lilToon float property on ALL lilToon materials under an avatar. Same properties as SetLilToonFloat.")]
        public static string BatchSetLilToonFloat(string avatarRootName, string property, float value)
        {
            var go = FindGO(avatarRootName);
            if (go == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var propName = GetFloatPropertyName(property);
            if (propName == null)
            {
                if (property.StartsWith("_"))
                    propName = property;
                else
                    return $"Error: Unknown float property '{property}'. Use: Cutoff, OutlineWidth, BumpScale, Smoothness, Metallic, AsUnlit, ShadowStrength, ShadowBorder, ShadowBlur, RimBorder, RimBlur, or raw '_PropertyName'.";
            }

            var mats = CollectLilToonMaterials(go);
            if (mats.Count == 0)
                return $"No lilToon materials found under '{avatarRootName}'.";

            // Filter to materials that have the property
            var applicable = mats.Where(m => m.HasProperty(propName)).ToList();
            if (applicable.Count == 0)
                return $"No lilToon materials under '{avatarRootName}' have property '{propName}'.";

            if (!AgentSettings.RequestConfirmation(
                "lilToon 一括Float設定",
                $"対象: {applicable.Count} マテリアル\n" +
                $"プロパティ: {propName}\n" +
                $"値: {value:F4}"))
                return "Cancelled: User denied the operation.";

            foreach (var mat in applicable)
            {
                Undo.RecordObject(mat, "Batch Set lilToon Float");
                mat.SetFloat(propName, value);
                EditorUtility.SetDirty(mat);
            }

            return $"Success: Set {propName} to {value:F4} on {applicable.Count} lilToon material(s) under '{avatarRootName}'.";
        }

        // ========== 10. BatchSetLilToonColors ==========

        [AgentTool("Set a lilToon color property on ALL lilToon materials under an avatar. Same format as SetLilToonColors.")]
        public static string BatchSetLilToonColors(string avatarRootName, string property, string color)
        {
            var go = FindGO(avatarRootName);
            if (go == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var propName = GetColorPropertyName(property);
            if (propName == null)
                return $"Error: Unknown color property '{property}'. Use: Main, Outline, Emission, Emission2nd, Shadow, Shadow2nd, Shadow3rd, Rim, MatCap, MatCap2nd.";

            var parts = color.Split(',');
            if (parts.Length < 3 || parts.Length > 4)
                return "Error: color must be 'r,g,b' or 'r,g,b,a' (0-1).";

            if (!float.TryParse(parts[0].Trim(), out float r) ||
                !float.TryParse(parts[1].Trim(), out float g) ||
                !float.TryParse(parts[2].Trim(), out float b))
                return "Error: Invalid color values.";
            float a = 1f;
            if (parts.Length == 4 && !float.TryParse(parts[3].Trim(), out a))
                return "Error: Invalid alpha value.";

            var mats = CollectLilToonMaterials(go);
            if (mats.Count == 0)
                return $"No lilToon materials found under '{avatarRootName}'.";

            var applicable = mats.Where(m => m.HasProperty(propName)).ToList();
            if (applicable.Count == 0)
                return $"No lilToon materials under '{avatarRootName}' have property '{propName}'.";

            if (!AgentSettings.RequestConfirmation(
                "lilToon 一括Color設定",
                $"対象: {applicable.Count} マテリアル\n" +
                $"プロパティ: {propName}\n" +
                $"色: ({r:F3}, {g:F3}, {b:F3}, {a:F3})"))
                return "Cancelled: User denied the operation.";

            var newColor = new Color(r, g, b, a);
            foreach (var mat in applicable)
            {
                Undo.RecordObject(mat, "Batch Set lilToon Color");
                mat.SetColor(propName, newColor);
                EditorUtility.SetDirty(mat);
            }

            return $"Success: Set {property} color ({propName}) to ({r:F3}, {g:F3}, {b:F3}, {a:F3}) on {applicable.Count} lilToon material(s) under '{avatarRootName}'.";
        }

#endif
    }
}
