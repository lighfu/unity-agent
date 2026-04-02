#if VRC_QUEST_TOOLS
using KRT.VRCQuestTools.Components;
using KRT.VRCQuestTools.Models;
#endif
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

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class QuestWorkflowTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

#if VRC_QUEST_TOOLS

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            return go;
        }

#if NDMF_MESH_SIMPLIFIER
        private static Type _simplifierType;

        private static bool InitSimplifierType()
        {
            if (_simplifierType != null) return true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                _simplifierType = asm.GetType("jp.lilxyzw.ndmfmeshsimplifier.runtime.NDMFMeshSimplifier");
                if (_simplifierType != null) return true;
            }
            return false;
        }
#endif

        // ========== 1. SetupQuestConversionWorkflow ==========

        [AgentTool("One-step Quest conversion: add converter settings + auto-add MeshSimplifier to large meshes + add TraceAndOptimize + check limits. simplifyThreshold: triangle count above which simplifier is added (default 20000). simplifyQuality: 0-1 (default 0.5).")]
        public static string SetupQuestConversionWorkflow(string avatarRootName,
            string maxTextureSize = "1024", int simplifyThreshold = 20000,
            float simplifyQuality = 0.5f)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            // Build confirmation
            var confirmSb = new StringBuilder();
            confirmSb.AppendLine($"アバター: {avatarRootName}");
            confirmSb.AppendLine($"テクスチャ上限: {maxTextureSize}");
            confirmSb.AppendLine($"簡略化閾値: {simplifyThreshold} tris");
            confirmSb.AppendLine($"簡略化品質: {simplifyQuality:F2}");
            confirmSb.AppendLine("\n以下を実行します:");
            confirmSb.AppendLine("  1. Quest変換設定 (AvatarConverterSettings) 追加");
            confirmSb.AppendLine("  2. 大メッシュに MeshSimplifier 自動追加");
            confirmSb.AppendLine("  3. AAO TraceAndOptimize 追加");
            confirmSb.AppendLine("  4. Quest制限チェック");

            if (!AgentSettings.RequestConfirmation(
                "Quest変換ワークフロー", confirmSb.ToString()))
                return "Cancelled: User denied the operation.";

            var result = new StringBuilder();
            result.AppendLine($"Quest Conversion Workflow for '{avatarRootName}':");
            int step = 1;

            // Step 1: Add AvatarConverterSettings
            var existing = avatarRoot.GetComponent<AvatarConverterSettings>();
            if (existing != null)
            {
                result.AppendLine($"  [{step}] AvatarConverterSettings already present. Skipped.");
            }
            else
            {
                try
                {
                    var settings = Undo.AddComponent<AvatarConverterSettings>(avatarRoot);
                    var so = new SerializedObject(settings);

                    var removeDynProp = so.FindProperty("removeAvatarDynamics");
                    if (removeDynProp != null) removeDynProp.boolValue = true;

                    var removeVCProp = so.FindProperty("removeVertexColor");
                    if (removeVCProp != null) removeVCProp.boolValue = true;

                    var defaultSettingsProp = so.FindProperty("defaultMaterialConvertSettings");
                    if (defaultSettingsProp != null)
                    {
                        var genTexProp = defaultSettingsProp.FindPropertyRelative("generateQuestTextures");
                        if (genTexProp != null) genTexProp.boolValue = true;

                        var maxTexProp = defaultSettingsProp.FindPropertyRelative("maxTextureSize");
                        if (maxTexProp != null)
                        {
                            int texSize;
                            switch (maxTextureSize)
                            {
                                case "256": texSize = (int)TextureSizeLimit.Max256x256; break;
                                case "512": texSize = (int)TextureSizeLimit.Max512x512; break;
                                case "2048": texSize = (int)TextureSizeLimit.Max2048x2048; break;
                                default: texSize = (int)TextureSizeLimit.Max1024x1024; break;
                            }
                            maxTexProp.intValue = texSize;
                        }

                        var brightnessProp = defaultSettingsProp.FindPropertyRelative("mainTextureBrightness");
                        if (brightnessProp != null) brightnessProp.floatValue = 0.83f;
                    }

                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                    result.AppendLine($"  [{step}] Added AvatarConverterSettings (textureSize={maxTextureSize}).");
                }
                catch (Exception ex)
                {
                    result.AppendLine($"  [{step}] Error adding converter settings: {ex.Message}");
                }
            }
            step++;

            // Step 2: Auto-add MeshSimplifier to large meshes
#if NDMF_MESH_SIMPLIFIER
            if (InitSimplifierType())
            {
                int simplifiedCount = 0;
                var smrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (smr.sharedMesh == null) continue;
                    int triCount = smr.sharedMesh.triangles.Length / 3;
                    if (triCount <= simplifyThreshold) continue;

                    // Skip if already has simplifier
                    if (smr.GetComponent(_simplifierType) != null) continue;

                    var comp = Undo.AddComponent(smr.gameObject, _simplifierType);
                    var compSo = new SerializedObject(comp);
                    var qualityProp = compSo.FindProperty("quality");
                    if (qualityProp != null)
                        qualityProp.floatValue = simplifyQuality;
                    compSo.ApplyModifiedProperties();
                    EditorUtility.SetDirty(comp);
                    simplifiedCount++;
                    result.AppendLine($"    Added MeshSimplifier to '{smr.gameObject.name}' ({triCount:N0} tris, quality={simplifyQuality:F2}).");
                }
                result.AppendLine($"  [{step}] MeshSimplifier: added to {simplifiedCount} mesh(es) over {simplifyThreshold:N0} tris.");
            }
            else
            {
                result.AppendLine($"  [{step}] NDMF Mesh Simplifier type not found. Skipped.");
            }
#else
            result.AppendLine($"  [{step}] NDMF Mesh Simplifier not installed. Skipped.");
#endif
            step++;

            // Step 3: Add TraceAndOptimize
#if AVATAR_OPTIMIZER
            var existingTao = avatarRoot.GetComponent<TraceAndOptimize>();
            if (existingTao == null)
            {
                Undo.AddComponent<TraceAndOptimize>(avatarRoot);
                EditorUtility.SetDirty(avatarRoot);
                result.AppendLine($"  [{step}] Added AAO TraceAndOptimize.");
            }
            else
            {
                result.AppendLine($"  [{step}] AAO TraceAndOptimize already present. Skipped.");
            }
#else
            result.AppendLine($"  [{step}] Avatar Optimizer not installed. Skipped.");
#endif
            step++;

            // Step 4: Quest limits check
            result.AppendLine($"  [{step}] Quest Limits Check:");

            int totalPolygons = 0;
            var allSmrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var allMfs = avatarRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (var smr in allSmrs)
                if (smr.sharedMesh != null) totalPolygons += smr.sharedMesh.triangles.Length / 3;
            foreach (var mf in allMfs)
                if (mf.sharedMesh != null) totalPolygons += mf.sharedMesh.triangles.Length / 3;

            var allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int materialSlots = allRenderers.Sum(r => r.sharedMaterials?.Length ?? 0);
            int meshCount = allSmrs.Length + allMfs.Length;

            var physBoneType = VRChatTools.FindVrcType(VRChatTools.VrcPhysBoneTypeName);
            int pbCount = physBoneType != null ? avatarRoot.GetComponentsInChildren(physBoneType, true).Length : 0;

            string Check(bool ok) => ok ? "[OK]" : "[OVER]";
            result.AppendLine($"    Polygons: {totalPolygons:N0}/70,000 {Check(totalPolygons <= 70000)}");
            result.AppendLine($"    Materials: {materialSlots}/32 {Check(materialSlots <= 32)}");
            result.AppendLine($"    Meshes: {meshCount}/16 {Check(meshCount <= 16)}");
            result.AppendLine($"    PhysBones: {pbCount} (removed on Quest)");

            result.AppendLine();
            result.AppendLine("Done! Review settings and build to test Quest output.");

            return result.ToString().TrimEnd();
        }

        // ========== 2. DiagnoseQuestReadiness ==========

        [AgentTool("Quest readiness diagnostic. Reports polygon count, materials, PhysBones, texture sizes, shader compatibility, and specific recommendations.")]
        public static string DiagnoseQuestReadiness(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Quest Readiness Diagnostic for '{avatarRootName}':");
            sb.AppendLine();

            var recommendations = new List<string>();

            // === Polygons per mesh ===
            sb.AppendLine("  Mesh Analysis:");
            int totalPolygons = 0;
            var meshInfos = new List<(string name, int tris)>();

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                int tris = smr.sharedMesh.triangles.Length / 3;
                totalPolygons += tris;
                meshInfos.Add((smr.gameObject.name, tris));
            }
            foreach (var mf in avatarRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                int tris = mf.sharedMesh.triangles.Length / 3;
                totalPolygons += tris;
                meshInfos.Add((mf.gameObject.name, tris));
            }

            foreach (var (name, tris) in meshInfos.OrderByDescending(m => m.tris))
            {
                string flag = tris > 20000 ? " [HIGH]" : "";
                sb.AppendLine($"    {name}: {tris:N0} tris{flag}");
            }
            sb.AppendLine($"    Total: {totalPolygons:N0}/70,000");
            if (totalPolygons > 70000)
                recommendations.Add($"Reduce polygons by {totalPolygons - 70000:N0}. Use MeshSimplifier on large meshes or RemoveMeshInBox/RemoveMeshByBlendShape.");

            // === Materials & Shaders ===
            sb.AppendLine();
            sb.AppendLine("  Material Analysis:");
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            var uniqueMats = new HashSet<Material>();
            int totalSlots = 0;
            var nonCompatible = new List<string>();

            foreach (var r in renderers)
            {
                if (r.sharedMaterials == null) continue;
                totalSlots += r.sharedMaterials.Length;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    uniqueMats.Add(m);
                    if (m.shader != null)
                    {
                        var sn = m.shader.name;
                        // Quest only supports VRChat/Mobile shaders
                        bool isQuestCompatible = sn.Contains("VRChat") || sn.Contains("Mobile") ||
                                                  sn.Contains("Unlit") || sn.Contains("Standard");
                        // lilToon and most PC shaders will be auto-converted
                        bool willAutoConvert = sn.Contains("lilToon") || sn.Contains("liltoon") || sn.Contains("lil/");
                        if (!isQuestCompatible && !willAutoConvert && !nonCompatible.Contains($"{m.name}: {sn}"))
                            nonCompatible.Add($"{m.name}: {sn}");
                    }
                }
            }

            sb.AppendLine($"    Material slots: {totalSlots}/32");
            sb.AppendLine($"    Unique materials: {uniqueMats.Count}");
            if (totalSlots > 32)
                recommendations.Add($"Reduce material slots from {totalSlots} to 32. Use MergeSkinnedMesh or RemoveExtraMaterialSlots.");
            if (nonCompatible.Count > 0)
            {
                sb.AppendLine($"    Non-auto-convertible shaders ({nonCompatible.Count}):");
                foreach (var nc in nonCompatible.Take(5))
                    sb.AppendLine($"      - {nc}");
                recommendations.Add("Some shaders won't auto-convert. Add MaterialSwap entries or change shaders.");
            }

            // === PhysBones ===
            sb.AppendLine();
            sb.AppendLine("  PhysBone Analysis:");
            var physBoneType = VRChatTools.FindVrcType(VRChatTools.VrcPhysBoneTypeName);
            if (physBoneType != null)
            {
                var physBones = avatarRoot.GetComponentsInChildren(physBoneType, true);
                sb.AppendLine($"    PhysBones: {physBones.Length} (all removed on Quest by default)");
                if (physBones.Length > 0)
                    recommendations.Add("PhysBones will be removed on Quest. Use KeepPhysBone to preserve critical ones (max 8 on Quest).");
            }

            // === Textures ===
            sb.AppendLine();
            sb.AppendLine("  Texture Analysis:");
            var largeTextures = new List<string>();
            foreach (var mat in uniqueMats)
            {
                var texPropNames = new[] { "_MainTex", "_BumpMap", "_EmissionMap", "_ShadowColorTex" };
                foreach (var tp in texPropNames)
                {
                    if (!mat.HasProperty(tp)) continue;
                    var tex = mat.GetTexture(tp) as Texture2D;
                    if (tex == null) continue;
                    if (tex.width > 2048 || tex.height > 2048)
                        largeTextures.Add($"{tex.name} ({tex.width}x{tex.height}) on {mat.name}");
                }
            }
            if (largeTextures.Count > 0)
            {
                sb.AppendLine($"    Large textures (>2048):");
                foreach (var lt in largeTextures.Take(5))
                    sb.AppendLine($"      - {lt}");
                recommendations.Add("Large textures increase VRAM. Set maxTextureSize to 1024 or 512 in converter settings.");
            }
            else
            {
                sb.AppendLine("    No oversized textures found.");
            }

            // === Converter Settings ===
            sb.AppendLine();
            var converterSettings = avatarRoot.GetComponent<AvatarConverterSettings>();
            if (converterSettings != null)
                sb.AppendLine("  AvatarConverterSettings: Present");
            else
            {
                sb.AppendLine("  AvatarConverterSettings: NOT present");
                recommendations.Add("Add AvatarConverterSettings with AddQuestConverterSettings or SetupQuestConversionWorkflow.");
            }

            // === Recommendations ===
            if (recommendations.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Recommendations:");
                for (int i = 0; i < recommendations.Count; i++)
                    sb.AppendLine($"    {i + 1}. {recommendations[i]}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("  Avatar appears Quest-ready!");
            }

            return sb.ToString().TrimEnd();
        }

#endif
    }
}
