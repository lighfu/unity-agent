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
                bool enable = ToolUtility.ParseBool(trimmed.Substring(eqIdx + 1));

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

        [AgentTool("Set a Vector4 property on a material. value: 'x,y', 'x,y,z', or 'x,y,z,w' — omitted components default to 0.")]
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
Reads BOTH the MaterialPropertyBlock (MPB) and the shared material, because Unity's Animator
routes material-property animations through MPB in most modern paths — a value that looks animated
on screen is often missing from sharedMaterial.GetVector().

Returns:
  - 'propertyName = (x, y, z, w)  [source=MPB]'   when the value is live-driven via MPB
  - 'propertyName = (x, y, z, w)  [source=sharedMaterial]'   when no MPB override exists
If both are present, both values are shown so the caller can see the base vs animated delta.")]
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

            // MPB check — Animator-driven material properties usually land here (not in sharedMaterial)
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbHasProp = !mpb.isEmpty && mpb.HasVector(propertyName);
            Vector4 shared = mat.GetVector(propertyName);

            if (mpbHasProp)
            {
                Vector4 live = mpb.GetVector(propertyName);
                return $"{propertyName} = ({live.x:F4}, {live.y:F4}, {live.z:F4}, {live.w:F4})  [source=MPB]\n"
                     + $"  sharedMaterial base = ({shared.x:F4}, {shared.y:F4}, {shared.z:F4}, {shared.w:F4})";
            }
            return $"{propertyName} = ({shared.x:F4}, {shared.y:F4}, {shared.z:F4}, {shared.w:F4})  [source=sharedMaterial]\n"
                 + $"  (no MPB override. If an Animator is animating this property in Play mode, the shader uses an "
                 + $"instance material written by Unity's animation binding system — call "
                 + $"GetRendererInstanceMaterialVector during Play mode to see that value.)";
        }

        [AgentTool(@"Read a Vector4 from the renderer's INSTANCE material (renderer.materials[i]).
Unity's Animator animates material properties by writing to the per-renderer instance, not to the shared
asset and not via MaterialPropertyBlock. sharedMaterial.GetVector returns the baked asset value; this tool
returns what the shader actually renders with. Requires Play mode: accessing renderer.materials[i] in Edit
mode would leak a material instance.

Output format:
  propertyName = (x, y, z, w)  [source=instance]
    shared base = (...)
    MPB value = (...) (only when an additional MPB override exists)")]
        public static string GetRendererInstanceMaterialVector(string gameObjectName, string propertyName, int materialIndex = 0)
        {
            if (!EditorApplication.isPlaying)
                return "Error: Requires Play mode. Accessing renderer.materials[i] in Edit mode would leak a material instance. Use GetMaterialVector (shared+MPB) for Edit-mode reads.";

            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var shareds = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= shareds.Length)
                return $"Error: Material index {materialIndex} out of range (0-{shareds.Length - 1}).";
            var sharedMat = shareds[materialIndex];
            if (sharedMat == null) return $"Error: Material at index {materialIndex} is null.";
            if (!sharedMat.HasProperty(propertyName))
                return $"Error: Material '{sharedMat.name}' has no property '{propertyName}'.";

            int propIdx = sharedMat.shader.FindPropertyIndex(propertyName);
            if (propIdx >= 0)
            {
                var propType = ShaderUtil.GetPropertyType(sharedMat.shader, propIdx);
                if (propType != ShaderUtil.ShaderPropertyType.Vector)
                    return $"Error: Property '{propertyName}' is of type {propType}, not Vector.";
            }

            // This access instantiates if not already (safe in Play mode, auto-cleaned on exit).
            var instances = renderer.materials;
            if (materialIndex >= instances.Length)
                return $"Error: renderer.materials length ({instances.Length}) is smaller than materialIndex ({materialIndex}).";
            var inst = instances[materialIndex];
            if (inst == null) return "Error: instance material is null.";

            Vector4 instV = inst.GetVector(propertyName);
            Vector4 sharedV = sharedMat.GetVector(propertyName);
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbHas = !mpb.isEmpty && mpb.HasVector(propertyName);

            var sb = new StringBuilder();
            sb.AppendLine($"{propertyName} = ({instV.x:F4}, {instV.y:F4}, {instV.z:F4}, {instV.w:F4})  [source=instance]");
            sb.AppendLine($"  shared base = ({sharedV.x:F4}, {sharedV.y:F4}, {sharedV.z:F4}, {sharedV.w:F4})");
            if (mpbHas)
            {
                Vector4 mv = mpb.GetVector(propertyName);
                sb.AppendLine($"  MPB value = ({mv.x:F4}, {mv.y:F4}, {mv.z:F4}, {mv.w:F4})");
            }
            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Read a float/range from the renderer's INSTANCE material (renderer.materials[i]).
Same Animator-driven-value semantics as GetRendererInstanceMaterialVector. Play mode only.")]
        public static string GetRendererInstanceMaterialFloat(string gameObjectName, string propertyName, int materialIndex = 0)
        {
            if (!EditorApplication.isPlaying)
                return "Error: Requires Play mode.";
            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";
            var shareds = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= shareds.Length)
                return $"Error: Material index {materialIndex} out of range (0-{shareds.Length - 1}).";
            var sharedMat = shareds[materialIndex];
            if (sharedMat == null || !sharedMat.HasProperty(propertyName))
                return $"Error: Property '{propertyName}' not found.";
            var instancesF = renderer.materials;
            if (materialIndex >= instancesF.Length)
                return $"Error: renderer.materials length ({instancesF.Length}) is smaller than materialIndex ({materialIndex}).";
            var inst = instancesF[materialIndex];
            if (inst == null) return "Error: instance material is null.";
            float instF = inst.GetFloat(propertyName);
            float sharedF = sharedMat.GetFloat(propertyName);
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbHas = !mpb.isEmpty && mpb.HasFloat(propertyName);
            var sb = new StringBuilder();
            sb.AppendLine($"{propertyName} = {instF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}  [source=instance]");
            sb.AppendLine($"  shared base = {sharedF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}");
            if (mpbHas) sb.AppendLine($"  MPB value = {mpb.GetFloat(propertyName).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Read a Color from the renderer's INSTANCE material (renderer.materials[i]).
Same Animator-driven-value semantics as GetRendererInstanceMaterialVector. Play mode only.")]
        public static string GetRendererInstanceMaterialColor(string gameObjectName, string propertyName, int materialIndex = 0)
        {
            if (!EditorApplication.isPlaying)
                return "Error: Requires Play mode.";
            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";
            var shareds = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= shareds.Length)
                return $"Error: Material index {materialIndex} out of range (0-{shareds.Length - 1}).";
            var sharedMat = shareds[materialIndex];
            if (sharedMat == null || !sharedMat.HasProperty(propertyName))
                return $"Error: Property '{propertyName}' not found.";
            var instancesC = renderer.materials;
            if (materialIndex >= instancesC.Length)
                return $"Error: renderer.materials length ({instancesC.Length}) is smaller than materialIndex ({materialIndex}).";
            var inst = instancesC[materialIndex];
            if (inst == null) return "Error: instance material is null.";
            Color instC = inst.GetColor(propertyName);
            Color sharedC = sharedMat.GetColor(propertyName);
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbHas = !mpb.isEmpty && mpb.HasColor(propertyName);
            var sb = new StringBuilder();
            sb.AppendLine($"{propertyName} = ({instC.r:F3}, {instC.g:F3}, {instC.b:F3}, {instC.a:F3})  [source=instance]");
            sb.AppendLine($"  shared base = ({sharedC.r:F3}, {sharedC.g:F3}, {sharedC.b:F3}, {sharedC.a:F3})");
            if (mpbHas)
            {
                Color mc = mpb.GetColor(propertyName);
                sb.AppendLine($"  MPB value = ({mc.r:F3}, {mc.g:F3}, {mc.b:F3}, {mc.a:F3})");
            }
            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Read an int from the renderer's INSTANCE material (renderer.materials[i]).
Same Animator-driven-value semantics as GetRendererInstanceMaterialVector. Play mode only.")]
        public static string GetRendererInstanceMaterialInt(string gameObjectName, string propertyName, int materialIndex = 0)
        {
            if (!EditorApplication.isPlaying)
                return "Error: Requires Play mode.";
            var go = MeshAnalysisTools.FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";
            var shareds = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= shareds.Length)
                return $"Error: Material index {materialIndex} out of range (0-{shareds.Length - 1}).";
            var sharedMat = shareds[materialIndex];
            if (sharedMat == null || !sharedMat.HasProperty(propertyName))
                return $"Error: Property '{propertyName}' not found.";
            var instancesI = renderer.materials;
            if (materialIndex >= instancesI.Length)
                return $"Error: renderer.materials length ({instancesI.Length}) is smaller than materialIndex ({materialIndex}).";
            var inst = instancesI[materialIndex];
            if (inst == null) return "Error: instance material is null.";
            int instI = inst.GetInt(propertyName);
            int sharedI = sharedMat.GetInt(propertyName);
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbHas = !mpb.isEmpty && mpb.HasInt(propertyName);
            var sb = new StringBuilder();
            sb.AppendLine($"{propertyName} = {instI}  [source=instance]");
            sb.AppendLine($"  shared base = {sharedI}");
            if (mpbHas) sb.AppendLine($"  MPB value = {mpb.GetInt(propertyName)}");
            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Dump ALL Vector4 properties of a GameObject's material in one call.
Each row shows the MaterialPropertyBlock value (if overridden) and/or the sharedMaterial value.
The source column ('MPB' / 'shared' / 'MPB+shared') makes it obvious which values are Animator-driven
vs baked into the asset — essential when debugging 'shader renders but GetVector returns zero'.
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

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb, materialIndex);
            bool mpbEmpty = mpb.isEmpty;

            var sb = new StringBuilder();
            sb.AppendLine($"Material: {mat.name} (Shader: {shader.name})");
            sb.AppendLine($"MPB has overrides: {!mpbEmpty}");
            sb.AppendLine($"Vector4 properties" + (filterLower != null ? $" filter='{filter}'" : "") + ":");
            sb.AppendLine("---");

            int shown = 0;
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.Vector) continue;
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (filterLower != null && name.ToLowerInvariant().IndexOf(filterLower, System.StringComparison.Ordinal) < 0) continue;

                Vector4 v = mat.GetVector(name);
                bool mpbHas = !mpbEmpty && mpb.HasVector(name);
                Vector4 mpbV = mpbHas ? mpb.GetVector(name) : default;
                string desc = ShaderUtil.GetPropertyDescription(shader, i);
                string descStr = string.IsNullOrEmpty(desc) ? "" : $" \"{desc}\"";
                if (mpbHas)
                {
                    sb.AppendLine($"  {name}{descStr} = ({mpbV.x:F4}, {mpbV.y:F4}, {mpbV.z:F4}, {mpbV.w:F4})  [MPB]  shared=({v.x:F4}, {v.y:F4}, {v.z:F4}, {v.w:F4})");
                }
                else
                {
                    sb.AppendLine($"  {name}{descStr} = ({v.x:F4}, {v.y:F4}, {v.z:F4}, {v.w:F4})  [shared]");
                }
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

        [AgentTool(@"Dump a material's properties with BOTH the shader's declared type and the slot the
value is actually serialized into (m_Ints / m_Floats / m_Colors / m_TexEnvs), and flag mismatches.

Why this is not the same as InspectMaterial: Material.SetFloat on a property the shader declares as
Integer is SILENTLY IGNORED — measured on Unity 2022.3, the value lands in neither m_Ints nor
m_Floats and the property keeps its default forever. Nothing throws and nothing is logged, so the
mistake only surfaces later as 'why is this still 0'. This tool shows the declared type next to the
actual storage slot and the effective value, which makes that case obvious at a glance.

ShaderLab gotcha this exposes: the legacy Int keyword maps to ShaderPropertyType.Float and is
written with SetFloat, while Integer (Unity 2021.1+) is a real integer stored in m_Ints and must be
written with SetInteger. Shader source saying Int can therefore mean either one; only the declared
type reported here is authoritative.

propertyFilter: case-insensitive substring; only matching property names are listed. Essential on
  uber-shaders — a Poiyomi material can declare 3000+ properties.
mismatchOnly: return only the rows where declared type and storage slot disagree (default false).
  Use it to sweep a folder of materials quickly. Implies no ORPHAN listing.
maxOrphans: cap on the ORPHAN listing (default 20). The count is always exact.

Rows are marked:
  MISMATCH — declared type and storage slot disagree; the value will not survive a save/reload.
  AT DEFAULT — an Integer-declared property still holding the shader's default value. Expected for
             untouched properties (and for a value deliberately set to the default), but this is
             also exactly what a swallowed SetFloat leaves behind. Check these first whenever a
             value you wrote is not taking effect.
  ORPHAN   — serialized but no longer declared by the shader (leftover from a shader change).
             Hundreds of these are normal for a material that switched shaders; they are dead
             weight in the asset, not errors.",
            Risk = ToolRisk.Safe)]
        public static string DumpMaterial(
            string materialPath,
            string propertyFilter = "",
            bool mismatchOnly = false,
            int maxOrphans = 20)
        {
            if (maxOrphans < 0) maxOrphans = 0;
            string filter = string.IsNullOrWhiteSpace(propertyFilter) ? null : propertyFilter.Trim();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";
            var shader = mat.shader;
            if (shader == null) return $"Error: Material '{mat.name}' has no shader.";

            if (!TryReadStoredSlots(mat, out var storedFloats, out var storedInts, out var storedColors,
                                    out var storedTextures, out bool intsArrayExists, out string slotError))
                return $"Error: {slotError}";

            var sb = new StringBuilder();
            sb.AppendLine($"Material: {mat.name}  ({materialPath})");
            sb.AppendLine($"  Shader: {shader.name}");
            if (!intsArrayExists)
                sb.AppendLine("  NOTE: this Unity version has no m_Ints array — Int/Float mismatches cannot occur.");
            sb.AppendLine("  declared = shader property type, stored = serialized slot, effective = value a fresh load would see");
            sb.AppendLine("---");

            int mismatches = 0, shown = 0, intDeclared = 0, intNotSet = 0;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            int propCount = shader.GetPropertyCount();

            for (int i = 0; i < propCount; i++)
            {
                string name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                declared.Add(name);

                string storedDesc;
                string effective;
                bool mismatch = false;
                bool notSet = false;

                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        bool inFloats = storedFloats.TryGetValue(name, out float f);
                        bool inInts = storedInts.TryGetValue(name, out int strayInt);
                        storedDesc = inFloats
                            ? $"m_Floats={f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}"
                            : "(absent)";
                        if (inInts)
                        {
                            storedDesc += $" +m_Ints={strayInt}";
                            mismatch = !inFloats;
                        }
                        effective = mat.GetFloat(name).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    }
#if UNITY_2021_1_OR_NEWER
                    case ShaderPropertyType.Int:
                    {
                        intDeclared++;
                        bool inInts = storedInts.TryGetValue(name, out int iv);
                        bool inFloats = storedFloats.TryGetValue(name, out float strayFloat);
                        storedDesc = inInts ? $"m_Ints={iv}" : "(absent)";
                        if (inFloats)
                        {
                            // Measured on 2022.3: SetFloat against an Integer property does not even
                            // reach m_Floats. A stray entry here therefore means the material was
                            // written by something else (an older Unity, a text edit, a shader whose
                            // property type changed) and the two slots genuinely disagree.
                            storedDesc += $" +m_Floats={strayFloat.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}";
                            mismatch = !inInts || Math.Abs(strayFloat - iv) > 0.0001f;
                        }
                        else
                        {
                            // Unity writes every Integer property into m_Ints at its default when the
                            // material is created, so "absent" almost never happens — comparing
                            // against the shader default is what actually identifies an untouched
                            // value. That is also the fingerprint of a SetFloat Unity swallowed.
                            int declaredDefault = shader.GetPropertyDefaultIntValue(i);
                            if (!inInts || iv == declaredDefault)
                            {
                                notSet = true;
                                intNotSet++;
                                storedDesc += $" (default={declaredDefault})";
                            }
                        }
                        effective = mat.GetInteger(name).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    }
#endif
                    case ShaderPropertyType.Color:
                    {
                        bool inColors = storedColors.TryGetValue(name, out Color c);
                        storedDesc = inColors ? $"m_Colors=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})" : "(absent)";
                        var ec = mat.GetColor(name);
                        effective = $"({ec.r:F2},{ec.g:F2},{ec.b:F2},{ec.a:F2})";
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        // Vectors live in m_Colors too (Unity stores them as a color-shaped struct).
                        bool inColors = storedColors.TryGetValue(name, out Color c);
                        storedDesc = inColors ? $"m_Colors=({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})" : "(absent)";
                        var v = mat.GetVector(name);
                        effective = $"({v.x:F3},{v.y:F3},{v.z:F3},{v.w:F3})";
                        break;
                    }
                    case ShaderPropertyType.Texture:
                    {
                        storedDesc = storedTextures.Contains(name) ? "m_TexEnvs" : "(absent)";
                        var tex = mat.GetTexture(name);
                        effective = tex != null ? tex.name : "none";
                        break;
                    }
                    default:
                        storedDesc = "(unknown type)";
                        effective = "?";
                        break;
                }

                if (mismatch) mismatches++;
                if (mismatchOnly && !mismatch) continue;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                shown++;
                sb.Append($"  {name,-28} declared={type,-8} {storedDesc,-38} effective={effective}");
                sb.AppendLine(mismatch ? "  MISMATCH" : notSet ? "  AT DEFAULT" : "");
            }

            // Serialized names the shader no longer declares.
            var orphans = new List<string>();
            foreach (var n in storedFloats.Keys) if (!declared.Contains(n)) orphans.Add($"{n} (m_Floats)");
            foreach (var n in storedInts.Keys) if (!declared.Contains(n)) orphans.Add($"{n} (m_Ints)");
            foreach (var n in storedColors.Keys) if (!declared.Contains(n)) orphans.Add($"{n} (m_Colors)");
            foreach (var n in storedTextures) if (!declared.Contains(n)) orphans.Add($"{n} (m_TexEnvs)");

            if (orphans.Count > 0)
            {
                sb.AppendLine("---");
                if (mismatchOnly)
                {
                    // A material that switched shaders carries hundreds of these. When the caller
                    // asked only for mismatches, listing them buries the answer.
                    sb.AppendLine($"  ORPHAN: {orphans.Count} serialized properties are not declared by '{shader.name}' " +
                                  "(not listed — set mismatchOnly=false to see them).");
                }
                else
                {
                    sb.AppendLine($"  ORPHAN ({orphans.Count}) — serialized but not declared by '{shader.name}':");
                    foreach (var o in orphans.OrderBy(o => o, StringComparer.Ordinal).Take(maxOrphans))
                        sb.AppendLine($"    {o}");
                    if (orphans.Count > maxOrphans)
                        sb.AppendLine($"    ... {orphans.Count - maxOrphans} more (raise maxOrphans to see them)");
                }
            }

            sb.AppendLine("---");
            if (intDeclared > 0)
            {
                sb.AppendLine($"Integer-declared properties: {intDeclared} ({intNotSet} at their shader default).");
                if (intNotSet > 0)
                    sb.AppendLine("  If you set one of those and it did not take effect, you used SetFloat — " +
                                  "Unity silently ignores it for Integer properties. Use SetInteger.");
            }
            if (mismatches == 0)
                sb.Append($"No type mismatches. {propCount} declared properties.");
            else
                sb.Append($"{mismatches} MISMATCH(es) out of {propCount} declared properties. " +
                          "These values will revert to their defaults when the material is reloaded — " +
                          "rewrite them with the setter matching the declared type (SetInteger for Int).");
            if (shown == 0)
            {
                if (mismatchOnly) sb.Append("  (mismatchOnly=true and nothing matched)");
                else if (filter != null) sb.Append($"  (no declared property name contains '{filter}')");
            }
            else if (filter != null)
            {
                sb.Append($"  ({shown} shown, filtered by '{filter}')");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Walks a Unity "named property" array (m_Floats / m_Ints / m_Colors / m_TexEnvs), each
        /// element being a {first: name, second: value} pair.
        /// </summary>
        private static void ReadNamedArray(
            SerializedProperty savedProperties, string arrayName, Action<string, SerializedProperty> onEntry)
        {
            var array = savedProperties.FindPropertyRelative(arrayName);
            if (array == null || !array.isArray) return;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var first = element.FindPropertyRelative("first");
                var second = element.FindPropertyRelative("second");
                if (first == null || second == null) continue;
                string name = first.stringValue;
                if (string.IsNullOrEmpty(name)) continue;
                onEntry(name, second);
            }
        }

        /// <summary>
        /// Reads a material's four serialized value arrays. Which array a value landed in is the
        /// whole point: Unity resolves a property by declared type, so a value sitting in the wrong
        /// array is invisible at runtime while still being right there in the asset.
        /// </summary>
        private static bool TryReadStoredSlots(Material mat,
                                               out Dictionary<string, float> floats,
                                               out Dictionary<string, int> ints,
                                               out Dictionary<string, Color> colors,
                                               out HashSet<string> textures,
                                               out bool intsArrayExists,
                                               out string error)
        {
            floats = new Dictionary<string, float>(StringComparer.Ordinal);
            ints = new Dictionary<string, int>(StringComparer.Ordinal);
            colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            textures = new HashSet<string>(StringComparer.Ordinal);
            intsArrayExists = false;
            error = null;

            var so = new SerializedObject(mat);
            try
            {
                var saved = so.FindProperty("m_SavedProperties");
                if (saved == null)
                {
                    error = $"'{mat.name}' has no m_SavedProperties (unexpected serialization layout).";
                    return false;
                }

                var localFloats = floats;
                var localColors = colors;
                var localTextures = textures;
                ReadNamedArray(saved, "m_Floats", (name, val) => localFloats[name] = val.floatValue);
                ReadNamedArray(saved, "m_Colors", (name, val) => localColors[name] = val.colorValue);
                ReadNamedArray(saved, "m_TexEnvs", (name, _) => localTextures.Add(name));

                // m_Ints only exists from Unity 2021.2; before that every numeric lived in m_Floats
                // and no mismatch was possible.
                intsArrayExists = saved.FindPropertyRelative("m_Ints") != null;
                if (intsArrayExists)
                {
                    var localInts = ints;
                    ReadNamedArray(saved, "m_Ints", (name, val) => localInts[name] = val.intValue);
                }
            }
            finally
            {
                so.Dispose();
            }
            return true;
        }

        [AgentTool(@"Compare two materials property by property, with the declared shader type and the
serialized slot each value actually landed in.

Answers the question a pair of DumpMaterial calls makes you assemble by hand: what differs between
these two materials, and is any of it a type/slot accident rather than a real edit?

For every property it reports:
  property  the shader property name
  type      the declared shader type (Float, Range, Int, Color, Vector, Texture)
  slot      which serialized array holds it — m_Floats, m_Ints, m_Colors, m_TexEnvs, or (absent)
  valueA    effective value on A (what a fresh load would see)
  valueB    effective value on B

slot is the reason this tool exists. An Integer-declared property lives in m_Ints and is written
with SetInteger; SetFloat against it is swallowed with no warning and no error, so the material
reads back at its old value while the calling code believes it set something. A row where the two
materials disagree on slot, or where a value sits in a slot that does not match its declared type,
is that bug rather than an intentional difference.

showAll: list every property instead of only the differing ones (default false).
propertyFilter: case-insensitive substring match on the property name.
maxRows: cap the table (default 60) so a Poiyomi-class shader cannot flood the response.

Different shaders are allowed — properties are matched by name and rows say which side declares
each one.",
            Category = "Material")]
        public static string DiffMaterials(string pathA, string pathB, bool showAll = false,
                                           string propertyFilter = "", int maxRows = 60)
        {
            if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
                return "Error: pathA and pathB are both required.";
            if (maxRows <= 0) maxRows = 60;
            string filter = string.IsNullOrWhiteSpace(propertyFilter) ? null : propertyFilter.Trim();

            var a = AssetDatabase.LoadAssetAtPath<Material>(pathA);
            if (a == null) return $"Error: Material not found at '{pathA}'.";
            var b = AssetDatabase.LoadAssetAtPath<Material>(pathB);
            if (b == null) return $"Error: Material not found at '{pathB}'.";
            if (a.shader == null) return $"Error: '{a.name}' has no shader.";
            if (b.shader == null) return $"Error: '{b.name}' has no shader.";

            if (!TryReadStoredSlots(a, out var fa, out var ia, out var ca, out var ta, out bool intsA, out string errA))
                return $"Error: {errA}";
            if (!TryReadStoredSlots(b, out var fb, out var ib, out var cb, out var tb, out bool intsB, out string errB))
                return $"Error: {errB}";

            var rows = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int differing = 0, slotAnomalies = 0, truncated = 0;

            void Consider(Material owner, Shader shader, int index)
            {
                string name = shader.GetPropertyName(index);
                if (!seen.Add(name)) return;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;

                var type = shader.GetPropertyType(index);
                string slotA = DescribeSlot(a, name, type, fa, ia, ca, ta, out string valueA, out bool oddA);
                string slotB = DescribeSlot(b, name, type, fb, ib, cb, tb, out string valueB, out bool oddB);

                bool differs = !string.Equals(valueA, valueB, StringComparison.Ordinal)
                               || !string.Equals(slotA, slotB, StringComparison.Ordinal);
                if (differs) differing++;
                if (oddA || oddB) slotAnomalies++;

                if (!showAll && !differs && !oddA && !oddB) return;
                if (rows.Count >= maxRows) { truncated++; return; }

                string flag = (oddA || oddB) ? "  <-- SLOT ANOMALY" : "";
                rows.Add($"{name}\t{type}\tA[{slotA}]={valueA}\tB[{slotB}]={valueB}{flag}");
            }

            for (int i = 0; i < a.shader.GetPropertyCount(); i++) Consider(a, a.shader, i);
            for (int i = 0; i < b.shader.GetPropertyCount(); i++) Consider(b, b.shader, i);

            var sb = new StringBuilder();
            sb.AppendLine($"A: {a.name}  ({pathA})  shader={a.shader.name}");
            sb.AppendLine($"B: {b.name}  ({pathB})  shader={b.shader.name}");
            if (a.shader != b.shader)
                sb.AppendLine("  NOTE: different shaders — properties are matched by name, and a property " +
                              "declared on only one side reads as (not declared) on the other.");
            if (!intsA || !intsB)
                sb.AppendLine("  NOTE: this Unity version has no m_Ints array — Int/Float slot mismatches cannot occur.");
            sb.AppendLine($"properties compared: {seen.Count}  differing: {differing}  slot anomalies: {slotAnomalies}");
            sb.AppendLine("property\ttype\tA[slot]=value\tB[slot]=value");
            sb.AppendLine("---");

            if (rows.Count == 0)
                sb.AppendLine(showAll ? "(no properties matched the filter)" : "(no differences)");
            else
                foreach (string row in rows) sb.AppendLine(row);

            if (truncated > 0)
                sb.AppendLine($"... {truncated} more row(s) suppressed by maxRows={maxRows}. " +
                              "Narrow with propertyFilter or raise maxRows.");
            if (slotAnomalies > 0)
                sb.AppendLine("SLOT ANOMALY = the value sits in an array that does not match its declared type. " +
                              "Usually a SetFloat against an Integer property (silently dropped) or a shader whose " +
                              "property type changed after the material was authored.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Names the serialized array a property occupies and reads back its effective value.
        /// <paramref name="anomalous"/> is set when the occupied array contradicts the declared
        /// type — the fingerprint of a write that Unity accepted syntactically and then discarded.
        /// </summary>
        private static string DescribeSlot(Material mat, string name, ShaderPropertyType type,
                                           Dictionary<string, float> floats, Dictionary<string, int> ints,
                                           Dictionary<string, Color> colors, HashSet<string> textures,
                                           out string value, out bool anomalous)
        {
            anomalous = false;
            var ic = System.Globalization.CultureInfo.InvariantCulture;

            if (!mat.HasProperty(name))
            {
                value = "(not declared)";
                return "-";
            }

            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                {
                    bool inFloats = floats.ContainsKey(name);
                    bool inInts = ints.ContainsKey(name);
                    anomalous = inInts && !inFloats;
                    value = mat.GetFloat(name).ToString("0.####", ic);
                    return inFloats ? (inInts ? "m_Floats+m_Ints" : "m_Floats") : (inInts ? "m_Ints" : "absent");
                }
#if UNITY_2021_1_OR_NEWER
                case ShaderPropertyType.Int:
                {
                    bool inInts = ints.ContainsKey(name);
                    bool inFloats = floats.ContainsKey(name);
                    anomalous = inFloats;
                    value = mat.GetInteger(name).ToString(ic);
                    return inInts ? (inFloats ? "m_Ints+m_Floats" : "m_Ints") : (inFloats ? "m_Floats" : "absent");
                }
#endif
                case ShaderPropertyType.Color:
                {
                    var c = mat.GetColor(name);
                    value = $"({c.r.ToString("F3", ic)},{c.g.ToString("F3", ic)},{c.b.ToString("F3", ic)},{c.a.ToString("F3", ic)})";
                    return colors.ContainsKey(name) ? "m_Colors" : "absent";
                }
                case ShaderPropertyType.Vector:
                {
                    var v = mat.GetVector(name);
                    value = $"({v.x.ToString("F3", ic)},{v.y.ToString("F3", ic)},{v.z.ToString("F3", ic)},{v.w.ToString("F3", ic)})";
                    // Vectors share m_Colors — Unity stores them in the same color-shaped struct.
                    return colors.ContainsKey(name) ? "m_Colors" : "absent";
                }
                case ShaderPropertyType.Texture:
                {
                    var tex = mat.GetTexture(name);
                    value = tex == null ? "(none)" : tex.name;
                    return textures.Contains(name) ? "m_TexEnvs" : "absent";
                }
                default:
                    value = "(unhandled type)";
                    return "?";
            }
        }

        [AgentTool(@"Write a TSV snapshot of every renderer material assignment under a GameObject
(or the whole scene). Pair with CompareSnapshots to prove that a conversion is fully reversible.

rootObject: GameObject name. Empty = every renderer in the active scene.
outputPath: where to write the TSV. Empty = a temp file (path is returned).

Columns: rendererPath, slotIndex, materialGuid, materialPath, materialName.
When rootObject is given, rendererPath is RELATIVE to it, so a before/after pair still lines up
if the root was renamed or duplicated. Same-named siblings get a #n suffix so their rows stay
distinct. Rows are sorted for line-by-line comparison.",
            Risk = ToolRisk.Caution)]
        public static string SnapshotSceneMaterials(string rootObject = "", string outputPath = "")
        {
            Renderer[] renderers;
            Transform relativeTo = null;
            if (string.IsNullOrWhiteSpace(rootObject))
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            }
            else
            {
                var go = FindGameObjectIncludingInactive(rootObject);
                if (go == null) return $"Error: GameObject '{rootObject}' not found.";
                renderers = go.GetComponentsInChildren<Renderer>(true);
                relativeTo = go.transform;
            }

            if (renderers.Length == 0)
                return string.IsNullOrWhiteSpace(rootObject)
                    ? "Error: No renderers in the active scene."
                    : $"Error: No renderers under '{rootObject}'.";

            var rows = new List<string>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                string path = GetHierarchyPath(r.transform, relativeTo);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    string assetPath = m != null ? AssetDatabase.GetAssetPath(m) : "";
                    string guid = string.IsNullOrEmpty(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);
                    rows.Add(string.Join("\t", path, i.ToString(),
                        guid, assetPath, m != null ? m.name : "(none)"));
                }
            }
            rows.Sort(StringComparer.Ordinal);

            string target = string.IsNullOrWhiteSpace(outputPath)
                ? System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"unity-agent-materials-{(string.IsNullOrWhiteSpace(rootObject) ? "scene" : SanitizeFileName(rootObject))}.tsv")
                : outputPath;

            try
            {
                string dir = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                var content = new StringBuilder();
                content.AppendLine("rendererPath\tslotIndex\tmaterialGuid\tmaterialPath\tmaterialName");
                foreach (var row in rows) content.AppendLine(row);
                System.IO.File.WriteAllText(target, content.ToString());
            }
            catch (Exception ex)
            {
                return $"Error: cannot write snapshot to '{target}': {ex.Message}";
            }

            return $"Success: {rows.Count} slots across {renderers.Length} renderers written to '{target}'.";
        }

        [AgentTool(@"Compare two material snapshots written by SnapshotSceneMaterials.
Returns 'identical' or only the rows that differ (added / removed / changed), so a 74-slot
round-trip check is one call instead of a manual diff.

maxRows: cap on how many rows to print per difference category (default 25). Counts are always
exact; only the listing is capped, because two snapshots of unrelated hierarchies differ in
every row and printing all of them buries the answer.",
            Risk = ToolRisk.Safe)]
        public static string CompareSnapshots(string pathA, string pathB, int maxRows = 25)
        {
            if (maxRows <= 0) maxRows = 25;
            if (!TryReadSnapshot(pathA, out var a, out string errA)) return $"Error: {errA}";
            if (!TryReadSnapshot(pathB, out var b, out string errB)) return $"Error: {errB}";

            var added = new List<string>();
            var removed = new List<string>();
            var changed = new List<string>();

            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out string bVal)) removed.Add($"{kv.Key} -> {kv.Value}");
                else if (!string.Equals(kv.Value, bVal, StringComparison.Ordinal))
                    changed.Add($"{kv.Key}\n      A: {kv.Value}\n      B: {bVal}");
            }
            foreach (var kv in b)
                if (!a.ContainsKey(kv.Key)) added.Add($"{kv.Key} -> {kv.Value}");

            if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
                return $"IDENTICAL: {a.Count} slots match exactly.\n  A: {pathA}\n  B: {pathB}";

            var sb = new StringBuilder();
            sb.AppendLine($"DIFFERENT: {a.Count} slots in A, {b.Count} in B.");
            sb.AppendLine($"  A: {pathA}");
            sb.AppendLine($"  B: {pathB}");

            // Zero overlap almost always means the two snapshots are of different hierarchies
            // rather than a real regression. Say so instead of printing every row twice.
            if (changed.Count == 0 && removed.Count == a.Count && added.Count == b.Count)
            {
                sb.AppendLine("  NO renderer path is shared between the two snapshots.");
                sb.AppendLine("  These are almost certainly snapshots of DIFFERENT hierarchies, not a before/after pair.");
                sb.AppendLine("  Sample from A: " + string.Join(", ", a.Keys.Take(3)));
                sb.AppendLine("  Sample from B: " + string.Join(", ", b.Keys.Take(3)));
                return sb.ToString().TrimEnd();
            }

            AppendCapped(sb, "Changed", changed, maxRows);
            AppendCapped(sb, "Only in A", removed, maxRows);
            AppendCapped(sb, "Only in B", added, maxRows);
            return sb.ToString().TrimEnd();
        }

        private static void AppendCapped(StringBuilder sb, string label, List<string> rows, int maxRows)
        {
            if (rows.Count == 0) return;
            sb.AppendLine($"  {label} ({rows.Count}):");
            foreach (var r in rows.Take(maxRows)) sb.AppendLine($"    {r}");
            if (rows.Count > maxRows)
                sb.AppendLine($"    ... {rows.Count - maxRows} more (raise maxRows to see them)");
        }

        /// <summary>Reads a snapshot TSV into rendererPath+slot -> "guid | path | name".</summary>
        private static bool TryReadSnapshot(string path, out Dictionary<string, string> rows, out string error)
        {
            rows = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "snapshot path is empty."; return false; }
            if (!System.IO.File.Exists(path)) { error = $"snapshot not found: '{path}'."; return false; }

            string[] lines;
            try { lines = System.IO.File.ReadAllLines(path); }
            catch (Exception ex) { error = $"cannot read '{path}': {ex.Message}"; return false; }

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i].StartsWith("rendererPath\t", StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split('\t');
                if (cols.Length < 5) continue;
                string key = $"{cols[0]}[{cols[1]}]";
                if (rows.ContainsKey(key))
                {
                    // Should not happen now that sibling names are disambiguated, but silently
                    // dropping a row here would turn a real difference into "IDENTICAL".
                    error = $"'{path}' has duplicate row key '{key}'. The snapshot cannot be compared " +
                            "reliably — regenerate it with the current version of SnapshotSceneMaterials.";
                    return false;
                }
                rows[key] = $"{cols[2]} | {cols[3]} | {cols[4]}";
            }

            if (rows.Count == 0) { error = $"'{path}' contains no snapshot rows."; return false; }
            return true;
        }

        /// <summary>
        /// Hierarchy path, stopping at <paramref name="relativeTo"/> (exclusive) when given.
        /// Excluding the root keeps a snapshot pair comparable after a rename or duplication.
        ///
        /// Same-named siblings get a "#n" suffix. Without it two renderers called "Hair" under one
        /// parent produce identical keys, the later row overwrites the earlier one when the TSV is
        /// read back, and a material change on the first one compares as IDENTICAL — a false proof
        /// that a conversion round-tripped.
        /// </summary>
        private static string GetHierarchyPath(Transform t, Transform relativeTo = null)
        {
            var stack = new Stack<string>();
            while (t != null && t != relativeTo)
            {
                stack.Push(t.name + SiblingSuffix(t));
                t = t.parent;
            }
            return stack.Count == 0 ? "." : string.Join("/", stack);
        }

        /// <summary>
        /// Finds a scene GameObject by name, INCLUDING inactive ones. GameObject.Find skips
        /// inactive objects, which is the common case here: a before/after snapshot is usually
        /// taken of an avatar that is toggled off in the scene.
        /// </summary>
        internal static GameObject FindGameObjectIncludingInactive(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var direct = GameObject.Find(name);
            if (direct != null) return direct;

            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null || t.name != name) continue;
                if (!t.gameObject.scene.IsValid()) continue; // skip prefab-stage / asset instances
                return t.gameObject;
            }
            return null;
        }

        /// <summary>"" when the name is unique among its siblings, "#n" (0-based) otherwise.</summary>
        private static string SiblingSuffix(Transform t)
        {
            var parent = t.parent;
            int index = 0, matches = 0;
            if (parent == null)
            {
                var scene = t.gameObject.scene;
                if (!scene.IsValid()) return "";
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != t.name) continue;
                    if (root.transform == t) index = matches;
                    matches++;
                }
            }
            else
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child.name != t.name) continue;
                    if (child == t) index = matches;
                    matches++;
                }
            }
            return matches > 1 ? $"#{index}" : "";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
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

        [AgentTool(@"List materials, narrowed before the search runs rather than after.

FindAssets('t:Material') across a whole avatar project loads thousands of assets to read one
field from each, which is why an unnarrowed material sweep times out. Every argument here cuts
the set before that cost is paid, and the call reports what it skipped instead of silently
returning a partial list.

root: GameObject name in the active scene. Collects materials from renderers under it and does
  not touch the asset database at all — by far the cheapest option, and usually the one you want.
  Inactive objects are included, because a toggled-off variant is still the thing being compared.
folder: project folder to search, e.g. 'Assets/Avatars/Manuka'. Ignored when root is given.
shaderNameContains: case-insensitive substring of the shader name, e.g. 'lilToon', 'Sunao'.
limit: maximum materials to list (default 100).
maxScan: give up after examining this many assets (default 2000).

A scan that hits maxScan or the internal time budget says INCOMPLETE. Treat that as 'narrow the
query', never as 'this is all of them'.",
            Category = "Material")]
        public static string FindMaterials(string shaderNameContains = "", string root = "",
                                           string folder = "", int limit = 100, int maxScan = 2000)
        {
            if (limit <= 0) limit = 100;
            if (maxScan <= 0) maxScan = 2000;
            string shaderFilter = string.IsNullOrWhiteSpace(shaderNameContains) ? null : shaderNameContains.Trim();

            var found = new List<Material>();
            var seen = new HashSet<int>();
            var sb = new StringBuilder();
            bool incomplete = false;
            int scanned = 0, filtered = 0;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            const long BudgetMs = 20_000;

            if (!string.IsNullOrWhiteSpace(root))
            {
                var go = FindGameObjectIncludingInactive(root.Trim());
                if (go == null)
                    return $"Error: GameObject '{root}' not found in the active scene (inactive objects were included in the search).";

                sb.AppendLine($"Source: renderers under '{go.name}' (scene, no asset database scan)");
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        scanned++;
                        if (!seen.Add(m.GetInstanceID())) continue;
                        if (!MatchesShader(m, shaderFilter)) { filtered++; continue; }
                        if (found.Count < limit) found.Add(m);
                    }
                }
            }
            else
            {
                string[] folders = null;
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    string f = folder.Trim().TrimEnd('/');
                    if (!AssetDatabase.IsValidFolder(f))
                        return $"Error: '{f}' is not a project folder. Pass a path like 'Assets/Avatars/Manuka'.";
                    folders = new[] { f };
                }

                string[] guids = folders == null
                    ? AssetDatabase.FindAssets("t:Material")
                    : AssetDatabase.FindAssets("t:Material", folders);

                sb.AppendLine($"Source: asset database{(folders == null ? " (entire project)" : $" under '{folders[0]}'")} — {guids.Length} material assets");
                if (folders == null && shaderFilter == null && guids.Length > limit)
                    sb.AppendLine($"  NOTE: no folder and no shader filter, so this lists the first {limit} of {guids.Length} in " +
                                  "asset-database order — which is arbitrary, not 'the most relevant'. Narrow with root or folder.");

                foreach (string guid in guids)
                {
                    if (found.Count >= limit && shaderFilter == null) break;
                    if (scanned >= maxScan) { incomplete = true; break; }
                    if (budget.ElapsedMilliseconds > BudgetMs) { incomplete = true; break; }

                    scanned++;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (m == null) continue;
                    if (!MatchesShader(m, shaderFilter)) { filtered++; continue; }
                    if (found.Count < limit) found.Add(m);
                }
            }

            sb.AppendLine($"scanned: {scanned}  matched: {found.Count}{(filtered > 0 ? $"  filtered out by shader: {filtered}" : "")}");
            if (shaderFilter != null) sb.AppendLine($"shaderNameContains: '{shaderFilter}'");
            sb.AppendLine("---");

            foreach (var m in found)
            {
                string path = AssetDatabase.GetAssetPath(m);
                sb.AppendLine($"{(string.IsNullOrEmpty(path) ? "(scene instance)" : path)}\t{m.name}\t{(m.shader != null ? m.shader.name : "(no shader)")}");
            }
            if (found.Count == 0) sb.AppendLine("(no materials matched)");

            if (incomplete)
                sb.AppendLine($"INCOMPLETE: stopped after {scanned} assets ({budget.ElapsedMilliseconds}ms). " +
                              "Raise maxScan or narrow with root / folder — this is NOT the full set.");
            else if (found.Count >= limit)
                sb.AppendLine($"Truncated at limit={limit}. Raise limit or narrow the query to see the rest.");

            return sb.ToString().TrimEnd();
        }

        static bool MatchesShader(Material m, string shaderFilter)
        {
            if (shaderFilter == null) return true;
            var s = m.shader;
            return s != null && s.name.IndexOf(shaderFilter, StringComparison.OrdinalIgnoreCase) >= 0;
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
