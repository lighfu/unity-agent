#if AVATAR_OPTIMIZER
using Anatawa12.AvatarOptimizer;
#endif
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Atlas;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class TextureAtlasTools
    {
#if AVATAR_OPTIMIZER
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool("Analyze materials and textures on a GameObject for atlas optimization. " +
            "Shows material names, shader, texture sizes, and recommendations.",
            Risk = ToolRisk.Safe)]
        public static string AnalyzeAtlasCandidates(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var mesh = ToolUtility.GetMesh(go);
            var materials = AtlasAnalyzer.Analyze(renderer);

            if (materials.Count == 0)
                return $"No materials found on '{gameObjectName}'.";

            // Check UV ranges and shared materials
            if (mesh != null)
                AtlasAnalyzer.CheckUVRanges(mesh, materials);
            AtlasAnalyzer.CheckSharedMaterials(renderer, materials);

            // Group by shader family
            var groups = AtlasAnalyzer.GroupByShaderFamily(materials);

            // Estimate memory
            long totalVRAM = AtlasAnalyzer.EstimateTextureMemory(materials);

            var sb = new StringBuilder();
            sb.AppendLine($"Atlas Analysis for '{gameObjectName}':");
            sb.AppendLine($"  Materials: {materials.Count}");
            sb.AppendLine($"  Texture VRAM: {FormatBytes(totalVRAM)}");
            sb.AppendLine($"  Shader families: {groups.Count}");
            sb.AppendLine();

            foreach (var kvp in groups)
            {
                sb.AppendLine($"[{kvp.Key}] ({kvp.Value.Count} materials):");
                foreach (int idx in kvp.Value)
                {
                    var info = materials.First(m => m.MaterialIndex == idx);
                    sb.Append($"  #{info.MaterialIndex} {info.MaterialName}");
                    sb.Append($"  {info.TextureWidth}x{info.TextureHeight}");
                    sb.Append($"  SM:{info.SubmeshIndex}");
                    if (info.TriangleCount > 0)
                        sb.Append($"  tris:{info.TriangleCount}");

                    var flags = new List<string>();
                    if (info.HasUVOutOfRange) flags.Add("UV_OUT_OF_RANGE");
                    if (info.IsSharedAcrossRenderers) flags.Add("SHARED");
                    if (info.MainTex == null) flags.Add("NO_TEXTURE");
                    if (info.IsLilToon && info.MainColor != Color.white) flags.Add("HAS_COLOR");
                    if (flags.Count > 0) sb.Append($"  [{string.Join(",", flags)}]");

                    sb.AppendLine();

                    // Texture details
                    if (info.MainTex != null)
                        sb.AppendLine($"    MainTex: {info.MainTex.name} ({info.MainTex.width}x{info.MainTex.height})");
                    if (info.NormalMap != null)
                        sb.AppendLine($"    Normal: {info.NormalMap.name} ({info.NormalMap.width}x{info.NormalMap.height})");
                    if (info.EmissionMap != null)
                        sb.AppendLine($"    Emission: {info.EmissionMap.name} ({info.EmissionMap.width}x{info.EmissionMap.height})");
                    if (info.IsLilToon && info.MainColor != Color.white)
                        sb.AppendLine($"    _Color: ({info.MainColor.r:F2},{info.MainColor.g:F2},{info.MainColor.b:F2})");
                }
                sb.AppendLine();
            }

            // Recommendations
            sb.AppendLine("Recommendations:");
            bool hasColorBake = materials.Any(m => m.IsLilToon && m.MainColor != Color.white);
            bool hasNoTex = materials.Any(m => m.MainTex == null);
            bool hasShared = materials.Any(m => m.IsSharedAcrossRenderers);
            bool hasUVOut = materials.Any(m => m.HasUVOutOfRange);

            if (hasColorBake)
                sb.AppendLine("  - Run PreprocessForAtlas to bake _Color into textures");
            if (hasNoTex)
                sb.AppendLine("  - Materials without textures will get placeholder textures during preprocessing");
            if (hasShared)
                sb.AppendLine("  - Shared materials will be duplicated during preprocessing");
            if (hasUVOut)
                sb.AppendLine("  - Materials with UV out of [0,1] range cannot be atlased (excluded)");
            if (groups.Count > 1)
                sb.AppendLine("  - Multiple shader families: only same-shader materials can be merged");
            if (!hasColorBake && !hasNoTex && !hasShared && !hasUVOut)
                sb.AppendLine("  - Ready for atlas setup. Run SetupAtlasAAO.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Preview atlas packing layout for selected materials. " +
            "materialIndices: semicolon-separated (e.g. '0;1;3'). " +
            "Returns atlas size, packing efficiency, per-material placement.",
            Risk = ToolRisk.Safe)]
        public static string PreviewAtlasLayout(
            string gameObjectName, string materialIndices,
            int maxSize = 4096, int padding = 4)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var allMaterials = AtlasAnalyzer.Analyze(renderer);
            var indices = ParseIndices(materialIndices);
            if (indices.Count == 0)
                return "Error: No valid material indices specified.";

            var items = new List<(int materialIndex, int width, int height)>();
            foreach (int idx in indices)
            {
                var info = allMaterials.FirstOrDefault(m => m.MaterialIndex == idx);
                if (info == null)
                    return $"Error: Material index {idx} not found.";
                items.Add((idx, info.TextureWidth, info.TextureHeight));
            }

            var layout = MaxRectsPacker.Pack(items, padding, maxSize);

            var sb = new StringBuilder();
            sb.AppendLine($"Atlas Layout Preview:");
            sb.AppendLine($"  Atlas size: {layout.AtlasWidth}x{layout.AtlasHeight}");
            sb.AppendLine($"  Efficiency: {layout.Efficiency * 100:F1}%");
            sb.AppendLine($"  Materials: {indices.Count}");
            sb.AppendLine();

            if (layout.Rects.Count == 0)
            {
                sb.AppendLine("  Could not fit selected materials into atlas!");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("Placement:");
            foreach (var rect in layout.Rects)
            {
                var info = allMaterials.FirstOrDefault(m => m.MaterialIndex == rect.MaterialIndex);
                string name = info != null ? info.MaterialName : $"#{rect.MaterialIndex}";
                sb.AppendLine($"  #{rect.MaterialIndex} {name}: " +
                    $"pos=({rect.PackedX},{rect.PackedY}) " +
                    $"size={rect.PackedWidth}x{rect.PackedHeight}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Pre-process textures for atlas optimization. " +
            "Bakes lilToon _Color into textures and optionally resizes to uniform size. " +
            "Run before setting up AAO MergeSkinnedMesh for best atlas results.",
            Risk = ToolRisk.Caution)]
        public static string PreprocessForAtlas(
            string gameObjectName, string materialIndices,
            bool bakeLilToonColor = true, int uniformSize = 0)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            var indices = ParseIndices(materialIndices);
            if (indices.Count == 0)
                return "Error: No valid material indices specified.";

            if (!AgentSettings.RequestConfirmation(
                "テクスチャ前処理",
                $"対象: {gameObjectName}\n" +
                $"マテリアル数: {indices.Count}\n" +
                $"_Color ベイク: {bakeLilToonColor}\n" +
                $"統一サイズ: {(uniformSize > 0 ? uniformSize + "px" : "なし")}"))
                return "Cancelled: User denied the operation.";

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Preprocess for Atlas");

            var sb = new StringBuilder();
            sb.AppendLine($"Preprocessing '{gameObjectName}':");

            // Step 1: Ensure textures
            foreach (int idx in indices)
            {
                string result = AtlasPreprocessor.EnsureTextures(renderer, idx);
                if (result.StartsWith("OK"))
                    sb.AppendLine($"  {result}");
            }

            // Step 2: Bake _Color
            if (bakeLilToonColor)
            {
                foreach (int idx in indices)
                {
                    string result = AtlasPreprocessor.BakeLilToonColor(renderer, idx);
                    if (!result.StartsWith("Skip"))
                        sb.AppendLine($"  {result}");
                }
            }

            // Step 3: Resize
            if (uniformSize > 0)
            {
                string result = AtlasPreprocessor.ResizeTextures(renderer, indices, uniformSize);
                sb.AppendLine($"  {result}");
            }

            Undo.CollapseUndoOperations(undoGroup);

            sb.AppendLine("Preprocessing complete.");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Setup AAO components for texture atlas optimization. " +
            "Adds TraceAndOptimize (with optimizeTexture + mergeSkinnedMesh enabled) " +
            "and optionally MergeSkinnedMesh for specified source renderers. " +
            "sourceMeshes: semicolon-separated additional mesh names to merge (optional).",
            Risk = ToolRisk.Caution)]
        public static string SetupAtlasAAO(
            string avatarRootName, string targetMeshName,
            string sourceMeshes = "", bool optimizeTexture = true)
        {
            var avatarRoot = FindGO(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: Target mesh '{targetMeshName}' not found.";

            var targetSmr = targetObj.GetComponent<SkinnedMeshRenderer>();
            if (targetSmr == null)
                return $"Error: '{targetMeshName}' does not have a SkinnedMeshRenderer.";

            if (!AgentSettings.RequestConfirmation(
                "AAO アトラス設定",
                $"アバター: {avatarRootName}\n" +
                $"ターゲットメッシュ: {targetMeshName}\n" +
                $"テクスチャ最適化: {optimizeTexture}\n" +
                $"ソースメッシュ: {(string.IsNullOrEmpty(sourceMeshes) ? "なし" : sourceMeshes)}"))
                return "Cancelled: User denied the operation.";

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup Atlas AAO");

            var sb = new StringBuilder();

            // 1. TraceAndOptimize on avatar root
            var existingTAO = avatarRoot.GetComponent<TraceAndOptimize>();
            if (existingTAO == null)
            {
                var tao = Undo.AddComponent<TraceAndOptimize>(avatarRoot);
                var so = new SerializedObject(tao);

                var mergeSkinnedMeshProp = so.FindProperty("mergeSkinnedMesh");
                if (mergeSkinnedMeshProp != null) mergeSkinnedMeshProp.boolValue = true;

                var optimizeTexProp = so.FindProperty("optimizeTexture");
                if (optimizeTexProp != null) optimizeTexProp.boolValue = optimizeTexture;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(tao);

                sb.AppendLine($"Added TraceAndOptimize to '{avatarRootName}' (mergeSkinnedMesh=true, optimizeTexture={optimizeTexture}).");
            }
            else
            {
                // Update existing
                var so = new SerializedObject(existingTAO);
                bool changed = false;

                var mergeSkinnedMeshProp = so.FindProperty("mergeSkinnedMesh");
                if (mergeSkinnedMeshProp != null && !mergeSkinnedMeshProp.boolValue)
                {
                    Undo.RecordObject(existingTAO, "Enable mergeSkinnedMesh");
                    mergeSkinnedMeshProp.boolValue = true;
                    changed = true;
                }

                var optimizeTexProp = so.FindProperty("optimizeTexture");
                if (optimizeTexProp != null && optimizeTexture && !optimizeTexProp.boolValue)
                {
                    Undo.RecordObject(existingTAO, "Enable optimizeTexture");
                    optimizeTexProp.boolValue = true;
                    changed = true;
                }

                if (changed)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(existingTAO);
                    sb.AppendLine($"Updated existing TraceAndOptimize on '{avatarRootName}'.");
                }
                else
                {
                    sb.AppendLine($"TraceAndOptimize already configured on '{avatarRootName}'.");
                }
            }

            // 2. MergeSkinnedMesh if source meshes specified
            if (!string.IsNullOrEmpty(sourceMeshes))
            {
                var sourceNames = sourceMeshes.Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();

                if (sourceNames.Length > 0)
                {
                    // Check for existing MergeSkinnedMesh
                    var existingMSM = targetObj.GetComponent<MergeSkinnedMesh>();
                    if (existingMSM != null)
                    {
                        sb.AppendLine($"Warning: '{targetMeshName}' already has MergeSkinnedMesh. Skipping.");
                    }
                    else
                    {
                        var sourceRenderers = new List<SkinnedMeshRenderer>();
                        foreach (var name in sourceNames)
                        {
                            var srcGo = FindGO(name);
                            if (srcGo == null)
                            {
                                sb.AppendLine($"Warning: Source mesh '{name}' not found. Skipping.");
                                continue;
                            }
                            var srcSmr = srcGo.GetComponent<SkinnedMeshRenderer>();
                            if (srcSmr == null)
                            {
                                sb.AppendLine($"Warning: '{name}' has no SkinnedMeshRenderer. Skipping.");
                                continue;
                            }
                            sourceRenderers.Add(srcSmr);
                        }

                        if (sourceRenderers.Count > 0)
                        {
                            var msm = Undo.AddComponent<MergeSkinnedMesh>(targetObj);
                            msm.Initialize(2);
                            msm.RemoveEmptyRendererObject = true;
                            msm.MergeBlendShapes = true;

                            foreach (var smr in sourceRenderers)
                                msm.SourceSkinnedMeshRenderers.Add(smr);

                            EditorUtility.SetDirty(msm);

                            sb.AppendLine($"Added MergeSkinnedMesh to '{targetMeshName}' with {sourceRenderers.Count} source(s).");
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            sb.AppendLine();
            sb.AppendLine("Atlas AAO setup complete. Build with VRChat SDK to see results.");
            return sb.ToString().TrimEnd();
        }

        // ─── Helpers ───

        private static List<int> ParseIndices(string semicolonSeparated)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(semicolonSeparated)) return result;

            foreach (var part in semicolonSeparated.Split(';'))
            {
                if (int.TryParse(part.Trim(), out int idx))
                    result.Add(idx);
            }
            return result;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

#endif
    }
}
