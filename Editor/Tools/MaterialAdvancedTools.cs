using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine.Rendering;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Material 上級操作ツール：Shader キーワード管理、プロパティ型別 getter/setter、
    /// renderQueue 設定、Material 複製・比較。
    /// </summary>
    public static class MaterialAdvancedTools
    {
        // =================================================================
        // Shader Keyword Management
        // =================================================================

        [AgentTool(@"Enable or disable a shader keyword on a material.
Keywords control shader variants (e.g., '_EMISSION', '_NORMALMAP', '_ALPHATEST_ON').
Use ListMaterialKeywords to see available keywords.")]
        public static string SetMaterialKeyword(string materialPath, string keyword, bool enabled)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Material Keyword");

            if (enabled)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Keyword '{keyword}' {(enabled ? "enabled" : "disabled")} on '{mat.name}'.";
        }

        [AgentTool(@"Batch set multiple shader keywords on a material.
keywords: semicolon-separated 'keyword=true/false'. Example: '_EMISSION=true;_NORMALMAP=false;_ALPHATEST_ON=true'")]
        public static string SetMaterialKeywords(string materialPath, string keywords)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Material Keywords");

            int count = 0;
            var entries = keywords.Split(';');
            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;

                string kw = trimmed.Substring(0, eqIdx).Trim();
                bool enable = trimmed.Substring(eqIdx + 1).Trim().ToLower() == "true";

                if (enable) mat.EnableKeyword(kw);
                else mat.DisableKeyword(kw);
                count++;
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {count} keywords on '{mat.name}'.";
        }

        [AgentTool("List all enabled shader keywords on a material and the shader's keyword space.")]
        public static string ListMaterialKeywords(string materialPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Material Keywords for '{mat.name}' (shader={mat.shader.name}):");

            var enabled = mat.enabledKeywords;
            sb.AppendLine($"  Enabled ({enabled.Length}):");
            foreach (var kw in enabled)
                sb.AppendLine($"    {kw.name}");

            var shaderKws = mat.shaderKeywords;
            if (shaderKws.Length > enabled.Length)
            {
                sb.AppendLine($"  ShaderKeywords ({shaderKws.Length}):");
                foreach (var kw in shaderKws)
                    sb.AppendLine($"    {kw}");
            }

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Material Property Access (typed)
        // =================================================================

        [AgentTool(@"Set a float/range property on a material. propertyName is the shader property (e.g., '_Metallic', '_Smoothness', '_Cutoff').
Use ListMaterialProperties (in TextureEditTools) to discover property names.")]
        public static string SetMaterialFloat(string materialPath, string propertyName, float value)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            if (!mat.HasFloat(propertyName)) return $"Error: Material '{mat.name}' has no float property '{propertyName}'.";

            Undo.RecordObject(mat, "Set Material Float");
            mat.SetFloat(propertyName, value);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {propertyName}={value:F4} on '{mat.name}'.";
        }

        [AgentTool("Set an integer property on a material.")]
        public static string SetMaterialInt(string materialPath, string propertyName, int value)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            if (!mat.HasInteger(propertyName)) return $"Error: Material '{mat.name}' has no int property '{propertyName}'.";

            Undo.RecordObject(mat, "Set Material Int");
            mat.SetInteger(propertyName, value);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {propertyName}={value} on '{mat.name}'.";
        }

        [AgentTool("Set a color property on a material. color format: hex '#RRGGBB' or '#RRGGBBAA', or 'r,g,b,a' (0-1 floats).")]
        public static string SetMaterialColorProperty(string materialPath, string propertyName, string color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            if (!mat.HasColor(propertyName)) return $"Error: Material '{mat.name}' has no color property '{propertyName}'.";

            if (!TryParseColor(color, out Color c))
                return "Error: Invalid color format. Use '#RRGGBB', '#RRGGBBAA', or 'r,g,b,a'.";

            Undo.RecordObject(mat, "Set Material Color");
            mat.SetColor(propertyName, c);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {propertyName}=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2}) on '{mat.name}'.";
        }

        [AgentTool("Set a Vector4 property on a material. value format: 'x,y,z,w'.")]
        public static string SetMaterialVector(string materialPath, string propertyName, string value)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            if (!mat.HasVector(propertyName)) return $"Error: Material '{mat.name}' has no vector property '{propertyName}'.";

            var parts = value.Split(',');
            if (parts.Length < 2 || parts.Length > 4)
                return "Error: Invalid vector format. Use 'x,y' or 'x,y,z' or 'x,y,z,w'.";

            float x = 0, y = 0, z = 0, w = 0;
            float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
            float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
            if (parts.Length > 2) float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
            if (parts.Length > 3) float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w);

            Undo.RecordObject(mat, "Set Material Vector");
            mat.SetVector(propertyName, new Vector4(x, y, z, w));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {propertyName}=({x:F3},{y:F3},{z:F3},{w:F3}) on '{mat.name}'.";
        }

        [AgentTool(@"Get a Vector4 property value from a GameObject's material (read-only).
Useful when ListMaterialProperties is not enough and you need the raw x,y,z,w components.
Returns 'propertyName = (x, y, z, w)' or an error if the property does not exist or is not a Vector.")]
        public static string GetMaterialVector(string gameObjectName, string propertyName, int materialIndex = 0)
        {
            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{materials.Length - 1}).";

            Material mat = materials[materialIndex];
            if (mat == null) return $"Error: Material at index {materialIndex} is null.";
            if (!mat.HasProperty(propertyName))
                return $"Error: Material '{mat.name}' has no property '{propertyName}'.";

            Shader shader = mat.shader;
            int propIdx = shader.FindPropertyIndex(propertyName);
            if (propIdx >= 0)
            {
                var propType = ShaderUtil.GetPropertyType(shader, propIdx);
                if (propType != ShaderUtil.ShaderPropertyType.Vector)
                    return $"Error: Property '{propertyName}' is of type {propType}, not Vector. Use the matching getter instead.";
            }

            Vector4 v = mat.GetVector(propertyName);
            return $"{propertyName} = ({v.x:F4}, {v.y:F4}, {v.z:F4}, {v.w:F4})";
        }

        [AgentTool(@"Dump ALL Vector4 properties of a GameObject's material in one call.
Much cheaper than calling GetMaterialVector per-property when debugging shaders with many vector parameters.
filter: optional substring match against property name (case-insensitive). Pass '' for all.")]
        public static string GetAllMaterialVectors(string gameObjectName, int materialIndex = 0, string filter = "")
        {
            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return $"Error: Material index {materialIndex} out of range (0-{materials.Length - 1}).";

            Material mat = materials[materialIndex];
            if (mat == null) return $"Error: Material at index {materialIndex} is null.";

            Shader shader = mat.shader;
            int propCount = ShaderUtil.GetPropertyCount(shader);
            string filterLower = string.IsNullOrEmpty(filter) ? null : filter.ToLowerInvariant();

            var sb = new StringBuilder();
            sb.AppendLine($"Material: {mat.name} (Shader: {shader.name})");
            sb.AppendLine($"Vector4 properties" + (filterLower != null ? $" filter='{filter}'" : "") + ":");
            sb.AppendLine("---");

            int shown = 0;
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.Vector) continue;
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (filterLower != null && name.ToLowerInvariant().IndexOf(filterLower, System.StringComparison.Ordinal) < 0) continue;

                Vector4 v = mat.GetVector(name);
                string desc = ShaderUtil.GetPropertyDescription(shader, i);
                string descStr = string.IsNullOrEmpty(desc) ? "" : $" \"{desc}\"";
                sb.AppendLine($"  {name}{descStr} = ({v.x:F4}, {v.y:F4}, {v.z:F4}, {v.w:F4})");
                shown++;
            }

            if (shown == 0)
                sb.AppendLine(filterLower != null ? $"  (no Vector4 properties matched '{filter}')" : "  (no Vector4 properties on this shader)");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set a texture property on a material. texturePath is the asset path to the Texture.")]
        public static string SetMaterialTexture(string materialPath, string propertyName, string texturePath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            if (!mat.HasTexture(propertyName)) return $"Error: Material '{mat.name}' has no texture property '{propertyName}'.";

            Texture tex = null;
            if (!string.IsNullOrEmpty(texturePath))
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                if (tex == null) return $"Error: Texture not found at '{texturePath}'.";
            }

            Undo.RecordObject(mat, "Set Material Texture");
            mat.SetTexture(propertyName, tex);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set {propertyName}={tex?.name ?? "null"} on '{mat.name}'.";
        }

        [AgentTool("Set texture offset and scale (tiling) on a material. offset/scale format: 'x,y'.")]
        public static string SetMaterialTextureTransform(string materialPath, string propertyName,
            string offset = "", string scale = "")
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Material Texture Transform");

            if (!string.IsNullOrEmpty(offset))
            {
                var parts = offset.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ox) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float oy))
                    mat.SetTextureOffset(propertyName, new Vector2(ox, oy));
            }

            if (!string.IsNullOrEmpty(scale))
            {
                var parts = scale.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sx) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sy))
                    mat.SetTextureScale(propertyName, new Vector2(sx, sy));
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set texture transform for '{propertyName}' on '{mat.name}'.";
        }

        // =================================================================
        // Material Render Settings
        // =================================================================

        [AgentTool(@"Set the render queue of a material. Controls draw order.
Common values: 1000=Background, 2000=Geometry, 2450=AlphaTest, 3000=Transparent, 4000=Overlay.
Use -1 to reset to shader default.")]
        public static string SetRenderQueue(string materialPath, int renderQueue)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Render Queue");
            mat.renderQueue = renderQueue;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set renderQueue={renderQueue} on '{mat.name}'.";
        }

        [AgentTool("Enable or disable GPU instancing on a material.")]
        public static string SetMaterialInstancing(string materialPath, bool enabled)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Material Instancing");
            mat.enableInstancing = enabled;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: GPU instancing {(enabled ? "enabled" : "disabled")} on '{mat.name}'.";
        }

        [AgentTool("Enable or disable a shader pass on a material. passName examples: 'ShadowCaster', 'ForwardBase', 'ALWAYS'.")]
        public static string SetShaderPassEnabled(string materialPath, string passName, bool enabled)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Shader Pass");
            mat.SetShaderPassEnabled(passName, enabled);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Pass '{passName}' {(enabled ? "enabled" : "disabled")} on '{mat.name}'.";
        }

        [AgentTool("Set a material override tag. Common tags: 'RenderType' (Opaque/Transparent/TransparentCutout), 'Queue' (Geometry/Transparent).")]
        public static string SetMaterialOverrideTag(string materialPath, string tagName, string tagValue)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            Undo.RecordObject(mat, "Set Material Tag");
            mat.SetOverrideTag(tagName, tagValue);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Set tag '{tagName}'='{tagValue}' on '{mat.name}'.";
        }

        // =================================================================
        // Material Inspection & Utility
        // =================================================================

        [AgentTool("Deep inspect a material. Shows all properties with current values, keywords, render queue, and shader info.")]
        public static string InspectMaterial(string materialPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            var sb = new StringBuilder();
            var shader = mat.shader;
            sb.AppendLine($"Material: {mat.name}");
            sb.AppendLine($"  Shader: {shader.name}");
            sb.AppendLine($"  RenderQueue: {mat.renderQueue}");
            sb.AppendLine($"  GPU Instancing: {mat.enableInstancing}");
            sb.AppendLine($"  DoubleSidedGI: {mat.doubleSidedGI}");

            // Keywords
            var keywords = mat.shaderKeywords;
            if (keywords.Length > 0)
            {
                sb.AppendLine($"  Keywords ({keywords.Length}):");
                foreach (var kw in keywords) sb.AppendLine($"    {kw}");
            }

            // Properties by type
            int propCount = shader.GetPropertyCount();
            var floats = new List<string>();
            var colors = new List<string>();
            var vectors = new List<string>();
            var textures = new List<string>();
            var ints = new List<string>();

            for (int i = 0; i < propCount; i++)
            {
                string name = shader.GetPropertyName(i);
                string desc = shader.GetPropertyDescription(i);
                var type = shader.GetPropertyType(i);

                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        float fVal = mat.GetFloat(name);
                        string range = type == ShaderPropertyType.Range
                            ? $" [{shader.GetPropertyRangeLimits(i).x:F2}-{shader.GetPropertyRangeLimits(i).y:F2}]"
                            : "";
                        floats.Add($"    {name} = {fVal:F4}{range}  // {desc}");
                        break;
                    case ShaderPropertyType.Color:
                        var cVal = mat.GetColor(name);
                        colors.Add($"    {name} = ({cVal.r:F2},{cVal.g:F2},{cVal.b:F2},{cVal.a:F2})  // {desc}");
                        break;
                    case ShaderPropertyType.Vector:
                        var vVal = mat.GetVector(name);
                        vectors.Add($"    {name} = ({vVal.x:F3},{vVal.y:F3},{vVal.z:F3},{vVal.w:F3})  // {desc}");
                        break;
                    case ShaderPropertyType.Texture:
                        var tex = mat.GetTexture(name);
                        string texName = tex != null ? tex.name : "none";
                        var off = mat.GetTextureOffset(name);
                        var scl = mat.GetTextureScale(name);
                        textures.Add($"    {name} = {texName} (offset={off}, scale={scl})  // {desc}");
                        break;
#if UNITY_2021_1_OR_NEWER
                    case ShaderPropertyType.Int:
                        int iVal = mat.GetInteger(name);
                        ints.Add($"    {name} = {iVal}  // {desc}");
                        break;
#endif
                }
            }

            if (floats.Count > 0) { sb.AppendLine($"  Float/Range ({floats.Count}):"); foreach (var f in floats) sb.AppendLine(f); }
            if (colors.Count > 0) { sb.AppendLine($"  Color ({colors.Count}):"); foreach (var c in colors) sb.AppendLine(c); }
            if (vectors.Count > 0) { sb.AppendLine($"  Vector ({vectors.Count}):"); foreach (var v in vectors) sb.AppendLine(v); }
            if (textures.Count > 0) { sb.AppendLine($"  Texture ({textures.Count}):"); foreach (var t in textures) sb.AppendLine(t); }
            if (ints.Count > 0) { sb.AppendLine($"  Int ({ints.Count}):"); foreach (var i in ints) sb.AppendLine(i); }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Duplicate a material with a new name. Optionally change shader.")]
        public static string DuplicateMaterial(string sourcePath, string newName, string savePath = "", string shaderName = "")
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) return $"Error: Material not found at '{sourcePath}'.";

            var newMat = new Material(source);
            newMat.name = newName;

            if (!string.IsNullOrEmpty(shaderName))
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) return $"Error: Shader '{shaderName}' not found.";
                newMat.shader = shader;
            }

            if (string.IsNullOrEmpty(savePath))
                savePath = System.IO.Path.GetDirectoryName(sourcePath);

            string assetPath = $"{savePath}/{newName}.mat";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(newMat, assetPath);
            AssetDatabase.SaveAssets();

            return $"Success: Duplicated material to '{assetPath}'.";
        }

        [AgentTool(@"Copy properties from one material to another. Only matching properties are copied.
Useful for transferring settings between materials with different shaders.")]
        public static string CopyMaterialProperties(string sourcePath, string destPath, bool matchingOnly = true)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) return $"Error: Source material not found at '{sourcePath}'.";
            var dest = AssetDatabase.LoadAssetAtPath<Material>(destPath);
            if (dest == null) return $"Error: Destination material not found at '{destPath}'.";

            Undo.RecordObject(dest, "Copy Material Properties");

            if (matchingOnly)
                dest.CopyMatchingPropertiesFromMaterial(source);
            else
                dest.CopyPropertiesFromMaterial(source);

            EditorUtility.SetDirty(dest);
            AssetDatabase.SaveAssets();

            return $"Success: Copied properties from '{source.name}' to '{dest.name}' (matchingOnly={matchingOnly}).";
        }

        [AgentTool("Search for a shader by name. Returns exact match or partial matches.")]
        public static string FindShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Shader found: {shader.name}");
                sb.AppendLine($"  IsSupported: {shader.isSupported}");
                sb.AppendLine($"  RenderQueue: {shader.renderQueue}");
                sb.AppendLine($"  Properties ({shader.GetPropertyCount()}):");
                for (int i = 0; i < shader.GetPropertyCount() && i < 50; i++)
                {
                    sb.AppendLine($"    {shader.GetPropertyName(i)} ({shader.GetPropertyType(i)}) - {shader.GetPropertyDescription(i)}");
                }
                if (shader.GetPropertyCount() > 50) sb.AppendLine($"    ... ({shader.GetPropertyCount() - 50} more)");
                return sb.ToString().TrimEnd();
            }

            // Try partial search through materials
            var guids = AssetDatabase.FindAssets("t:Shader");
            var matches = new List<string>();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLower().Contains(shaderName.ToLower()))
                    matches.Add(path);
                if (matches.Count >= 20) break;
            }

            if (matches.Count > 0)
                return $"Shader '{shaderName}' not found by exact name. Partial matches:\n" + string.Join("\n", matches.Select(m => $"  {m}"));

            return $"Error: Shader '{shaderName}' not found.";
        }

        [AgentTool("Change the shader of a material. Preserves compatible properties.")]
        public static string ChangeMaterialShader(string materialPath, string shaderName)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            var shader = Shader.Find(shaderName);
            if (shader == null) return $"Error: Shader '{shaderName}' not found.";

            string oldShader = mat.shader.name;
            Undo.RecordObject(mat, "Change Material Shader");
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return $"Success: Changed shader of '{mat.name}' from '{oldShader}' to '{shaderName}'.";
        }

        // =================================================================
        // Shader Creation
        // =================================================================

        [AgentTool("Create a Unity shader file (.shader) and import it. " +
            "shaderCode must start with 'Shader \"Name\"'. " +
            "savePath: where to save (e.g. 'Assets/Shaders/MyShader.shader'). " +
            "Returns shader path and compilation status.",
            Risk = ToolRisk.Caution)]
        public static string CreateShaderFile(string savePath, string shaderCode)
        {
            if (string.IsNullOrEmpty(savePath) || !savePath.EndsWith(".shader"))
                return "Error: savePath must end with '.shader'.";
            if (!savePath.StartsWith("Assets/"))
                return "Error: savePath must start with 'Assets/'.";
            if (string.IsNullOrWhiteSpace(shaderCode))
                return "Error: shaderCode is empty.";

            // Basic validation
            string trimmed = shaderCode.TrimStart();
            if (!trimmed.StartsWith("Shader"))
                return "Error: shaderCode must start with 'Shader \"Name/Path\"'.";

            // Ensure directory exists
            string fullPath = System.IO.Path.GetFullPath(savePath);
            string dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(fullPath, shaderCode);
            AssetDatabase.ImportAsset(savePath, ImportAssetOptions.ForceUpdate);

            // Check compilation
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(savePath);
            if (shader == null)
                return $"Warning: Shader file written to '{savePath}' but failed to load. Check console for errors.";

            var messages = ShaderUtil.GetShaderMessages(shader);
            if (messages.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Warning: Shader '{shader.name}' has {messages.Length} compilation message(s):");
                for (int i = 0; i < Mathf.Min(messages.Length, 5); i++)
                    sb.AppendLine($"  - {messages[i].message}");
                sb.AppendLine($"File saved: '{savePath}'");
                return sb.ToString();
            }

            return $"Success: Shader '{shader.name}' created at '{savePath}'. No compilation errors.";
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static bool TryParseColor(string input, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(input)) return false;

            // Hex format
            if (input.StartsWith("#"))
                return ColorUtility.TryParseHtmlString(input, out color);

            // Float format: r,g,b or r,g,b,a
            var parts = input.Split(',');
            if (parts.Length >= 3)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var ns = System.Globalization.NumberStyles.Float;
                if (float.TryParse(parts[0].Trim(), ns, ic, out float r) &&
                    float.TryParse(parts[1].Trim(), ns, ic, out float g) &&
                    float.TryParse(parts[2].Trim(), ns, ic, out float b))
                {
                    float a = 1f;
                    if (parts.Length > 3) float.TryParse(parts[3].Trim(), ns, ic, out a);
                    color = new Color(r, g, b, a);
                    return true;
                }
            }

            return false;
        }
    }
}
