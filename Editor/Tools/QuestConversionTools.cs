#if VRC_QUEST_TOOLS
using KRT.VRCQuestTools.Components;
using KRT.VRCQuestTools.Models;
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
    public static class QuestConversionTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

#if VRC_QUEST_TOOLS

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return null;
            return go;
        }

        private static AvatarConverterSettings FindConverterSettings(GameObject avatarRoot)
        {
            return avatarRoot.GetComponent<AvatarConverterSettings>();
        }

        private static TextureSizeLimit ParseTextureSize(string size)
        {
            switch (size)
            {
                case "256": return TextureSizeLimit.Max256x256;
                case "512": return TextureSizeLimit.Max512x512;
                case "1024": return TextureSizeLimit.Max1024x1024;
                case "2048": return TextureSizeLimit.Max2048x2048;
                case "0":
                case "nolimit": return TextureSizeLimit.NoLimit;
                default: return TextureSizeLimit.Max1024x1024;
            }
        }

        // ========== 1. AddQuestConverterSettings ==========

        [AgentTool("Add VRC Quest Tools converter settings to avatar for automatic Quest conversion at build time (NDMF).")]
        public static string AddQuestConverterSettings(string avatarRootName,
            string removeAvatarDynamics = "true", string removeVertexColor = "true",
            string maxTextureSize = "1024", string generateQuestTextures = "true",
            string mainTextureBrightness = "0.83")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var existing = FindConverterSettings(avatarRoot);
            if (existing != null)
                return $"Error: '{avatarRootName}' already has AvatarConverterSettings. Use ConfigureQuestConversion to modify.";

            bool removeDynamics = removeAvatarDynamics.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool removeVC = removeVertexColor.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool genTextures = generateQuestTextures.Equals("true", StringComparison.OrdinalIgnoreCase);
            float brightness = 0.83f;
            float.TryParse(mainTextureBrightness, out brightness);
            var texSize = ParseTextureSize(maxTextureSize);

            if (!AgentSettings.RequestConfirmation(
                "Quest変換設定の追加",
                $"アバター: {avatarRootName}\n" +
                $"アバターダイナミクス削除: {removeDynamics}\n" +
                $"頂点カラー削除: {removeVC}\n" +
                $"テクスチャ生成: {genTextures}\n" +
                $"最大テクスチャサイズ: {texSize}\n" +
                $"明るさ: {brightness}"))
                return "Cancelled: User denied the operation.";

            var settings = Undo.AddComponent<AvatarConverterSettings>(avatarRoot);

            // Configure via SerializedObject for reliable access
            var so = new SerializedObject(settings);

            var removeDynProp = so.FindProperty("removeAvatarDynamics");
            if (removeDynProp != null) removeDynProp.boolValue = removeDynamics;

            var removeVCProp = so.FindProperty("removeVertexColor");
            if (removeVCProp != null) removeVCProp.boolValue = removeVC;

            // Configure default material convert settings (ToonLitConvertSettings)
            var defaultSettingsProp = so.FindProperty("defaultMaterialConvertSettings");
            if (defaultSettingsProp != null)
            {
                var genTexProp = defaultSettingsProp.FindPropertyRelative("generateQuestTextures");
                if (genTexProp != null) genTexProp.boolValue = genTextures;

                var maxTexProp = defaultSettingsProp.FindPropertyRelative("maxTextureSize");
                if (maxTexProp != null) maxTexProp.intValue = (int)texSize;

                var brightnessProp = defaultSettingsProp.FindPropertyRelative("mainTextureBrightness");
                if (brightnessProp != null) brightnessProp.floatValue = brightness;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);

            return $"Success: Added AvatarConverterSettings to '{avatarRootName}'.\n" +
                   $"  Remove dynamics: {removeDynamics}\n" +
                   $"  Remove vertex color: {removeVC}\n" +
                   $"  Generate textures: {genTextures}\n" +
                   $"  Max texture size: {texSize}\n" +
                   $"  Brightness: {brightness}";
        }

        // ========== 2. ConfigureQuestConversion ==========

        [AgentTool("Configure existing Quest converter settings on avatar. Shows current values when called with no parameters.")]
        public static string ConfigureQuestConversion(string avatarRootName,
            string removeAvatarDynamics = "", string removeVertexColor = "",
            string maxTextureSize = "", string generateQuestTextures = "",
            string mainTextureBrightness = "", string removeExtraMaterialSlots = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var settings = FindConverterSettings(avatarRoot);
            if (settings == null)
                return $"Error: No AvatarConverterSettings found on '{avatarRootName}'. Use AddQuestConverterSettings first.";

            var so = new SerializedObject(settings);

            bool anySet = !string.IsNullOrEmpty(removeAvatarDynamics) || !string.IsNullOrEmpty(removeVertexColor)
                || !string.IsNullOrEmpty(maxTextureSize) || !string.IsNullOrEmpty(generateQuestTextures)
                || !string.IsNullOrEmpty(mainTextureBrightness) || !string.IsNullOrEmpty(removeExtraMaterialSlots);

            if (!anySet)
            {
                return InspectQuestSettings(avatarRootName);
            }

            Undo.RecordObject(settings, "Configure Quest Conversion");
            var changes = new List<string>();

            if (!string.IsNullOrEmpty(removeAvatarDynamics))
            {
                var prop = so.FindProperty("removeAvatarDynamics");
                if (prop != null) { prop.boolValue = bool.Parse(removeAvatarDynamics); changes.Add($"removeAvatarDynamics={removeAvatarDynamics}"); }
            }

            if (!string.IsNullOrEmpty(removeVertexColor))
            {
                var prop = so.FindProperty("removeVertexColor");
                if (prop != null) { prop.boolValue = bool.Parse(removeVertexColor); changes.Add($"removeVertexColor={removeVertexColor}"); }
            }

            if (!string.IsNullOrEmpty(removeExtraMaterialSlots))
            {
                var prop = so.FindProperty("removeExtraMaterialSlots");
                if (prop != null) { prop.boolValue = bool.Parse(removeExtraMaterialSlots); changes.Add($"removeExtraMaterialSlots={removeExtraMaterialSlots}"); }
            }

            var defaultSettingsProp = so.FindProperty("defaultMaterialConvertSettings");
            if (defaultSettingsProp != null)
            {
                if (!string.IsNullOrEmpty(generateQuestTextures))
                {
                    var prop = defaultSettingsProp.FindPropertyRelative("generateQuestTextures");
                    if (prop != null) { prop.boolValue = bool.Parse(generateQuestTextures); changes.Add($"generateQuestTextures={generateQuestTextures}"); }
                }
                if (!string.IsNullOrEmpty(maxTextureSize))
                {
                    var prop = defaultSettingsProp.FindPropertyRelative("maxTextureSize");
                    if (prop != null) { prop.intValue = (int)ParseTextureSize(maxTextureSize); changes.Add($"maxTextureSize={maxTextureSize}"); }
                }
                if (!string.IsNullOrEmpty(mainTextureBrightness))
                {
                    var prop = defaultSettingsProp.FindPropertyRelative("mainTextureBrightness");
                    if (prop != null) { prop.floatValue = float.Parse(mainTextureBrightness); changes.Add($"mainTextureBrightness={mainTextureBrightness}"); }
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);

            return $"Success: Updated Quest conversion settings: {string.Join(", ", changes)}.";
        }

        // ========== 3. AddMaterialSwap ==========

        [AgentTool("Add a material swap entry for Quest conversion. Replaces originalMaterial with replacementMaterial on Quest.")]
        public static string AddMaterialSwap(string avatarRootName,
            string originalMaterialPath, string replacementMaterialPath)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var originalMat = AssetDatabase.LoadAssetAtPath<Material>(originalMaterialPath);
            if (originalMat == null)
                return $"Error: Original material not found at '{originalMaterialPath}'.";

            var replacementMat = AssetDatabase.LoadAssetAtPath<Material>(replacementMaterialPath);
            if (replacementMat == null)
                return $"Error: Replacement material not found at '{replacementMaterialPath}'.";

            // Find or create MaterialSwap component
            var materialSwap = avatarRoot.GetComponent<MaterialSwap>();
            if (materialSwap == null)
                materialSwap = Undo.AddComponent<MaterialSwap>(avatarRoot);
            else
                Undo.RecordObject(materialSwap, "Add Material Swap");

            // Add mapping via SerializedObject
            var so = new SerializedObject(materialSwap);
            var mappingsProp = so.FindProperty("materialMappings");
            if (mappingsProp != null && mappingsProp.isArray)
            {
                // Check for duplicate
                for (int i = 0; i < mappingsProp.arraySize; i++)
                {
                    var elem = mappingsProp.GetArrayElementAtIndex(i);
                    var origProp = elem.FindPropertyRelative("originalMaterial");
                    if (origProp != null && origProp.objectReferenceValue == originalMat)
                        return $"Error: Material swap for '{originalMat.name}' already exists.";
                }

                int idx = mappingsProp.arraySize;
                mappingsProp.InsertArrayElementAtIndex(idx);
                var newElem = mappingsProp.GetArrayElementAtIndex(idx);
                var newOrigProp = newElem.FindPropertyRelative("originalMaterial");
                var newReplProp = newElem.FindPropertyRelative("replacementMaterial");
                if (newOrigProp != null) newOrigProp.objectReferenceValue = originalMat;
                if (newReplProp != null) newReplProp.objectReferenceValue = replacementMat;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(materialSwap);

            return $"Success: Added material swap '{originalMat.name}' → '{replacementMat.name}' on '{avatarRootName}'.";
        }

        // ========== 4. KeepPhysBone ==========

        [AgentTool("Mark specific PhysBones to keep during Quest conversion. physBoneObjects: semicolon-separated GameObject paths.")]
        public static string KeepPhysBone(string avatarRootName, string physBoneObjects)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var settings = FindConverterSettings(avatarRoot);
            if (settings == null)
                return $"Error: No AvatarConverterSettings found on '{avatarRootName}'. Use AddQuestConverterSettings first.";

            var physBoneType = VRChatTools.FindVrcType(VRChatTools.VrcPhysBoneTypeName);
            if (physBoneType == null)
                return "Error: VRCPhysBone type not found. VRChat SDK may not be installed.";

            var paths = physBoneObjects.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (paths.Length == 0)
                return "Error: No PhysBone object paths specified.";

            var so = new SerializedObject(settings);
            var keepProp = so.FindProperty("physBonesToKeep");
            if (keepProp == null || !keepProp.isArray)
                return "Error: physBonesToKeep property not found.";

            var sb = new StringBuilder();
            int addedCount = 0;

            foreach (var path in paths)
            {
                var target = avatarRoot.transform.Find(path);
                if (target == null)
                {
                    sb.AppendLine($"  Warning: '{path}' not found, skipped.");
                    continue;
                }

                var physBone = target.GetComponent(physBoneType);
                if (physBone == null)
                {
                    sb.AppendLine($"  Warning: '{path}' has no VRCPhysBone, skipped.");
                    continue;
                }

                // Check for duplicates
                bool duplicate = false;
                for (int i = 0; i < keepProp.arraySize; i++)
                {
                    if (keepProp.GetArrayElementAtIndex(i).objectReferenceValue == physBone)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                {
                    sb.AppendLine($"  Skipped: '{path}' already in keep list.");
                    continue;
                }

                int idx = keepProp.arraySize;
                keepProp.InsertArrayElementAtIndex(idx);
                keepProp.GetArrayElementAtIndex(idx).objectReferenceValue = physBone;
                addedCount++;
                sb.AppendLine($"  Added: '{path}'");
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);

            return $"KeepPhysBone result ({addedCount} added):\n{sb.ToString().TrimEnd()}";
        }

        // ========== 5. InspectQuestSettings ==========

        [AgentTool("Inspect current Quest conversion settings on avatar. Shows all configured options.")]
        public static string InspectQuestSettings(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var settings = FindConverterSettings(avatarRoot);
            if (settings == null)
                return $"No AvatarConverterSettings found on '{avatarRootName}'.";

            var so = new SerializedObject(settings);
            var sb = new StringBuilder();
            sb.AppendLine($"Quest Conversion Settings on '{avatarRootName}':");

            var removeDyn = so.FindProperty("removeAvatarDynamics");
            if (removeDyn != null) sb.AppendLine($"  removeAvatarDynamics: {removeDyn.boolValue}");

            var removeVC = so.FindProperty("removeVertexColor");
            if (removeVC != null) sb.AppendLine($"  removeVertexColor: {removeVC.boolValue}");

            var removeExtra = so.FindProperty("removeExtraMaterialSlots");
            if (removeExtra != null) sb.AppendLine($"  removeExtraMaterialSlots: {removeExtra.boolValue}");

            var compressIcons = so.FindProperty("compressExpressionsMenuIcons");
            if (compressIcons != null) sb.AppendLine($"  compressMenuIcons: {compressIcons.boolValue}");

            var ndmfPhase = so.FindProperty("ndmfPhase");
            if (ndmfPhase != null)
            {
                if (ndmfPhase.enumDisplayNames != null && ndmfPhase.enumValueIndex >= 0 && ndmfPhase.enumValueIndex < ndmfPhase.enumDisplayNames.Length)
                    sb.AppendLine($"  ndmfPhase: {ndmfPhase.enumDisplayNames[ndmfPhase.enumValueIndex]}");
            }

            // Material convert settings
            var defaultSettings = so.FindProperty("defaultMaterialConvertSettings");
            if (defaultSettings != null)
            {
                sb.AppendLine("  Default Material Settings:");
                var genTex = defaultSettings.FindPropertyRelative("generateQuestTextures");
                if (genTex != null) sb.AppendLine($"    generateQuestTextures: {genTex.boolValue}");

                var maxTex = defaultSettings.FindPropertyRelative("maxTextureSize");
                if (maxTex != null) sb.AppendLine($"    maxTextureSize: {(TextureSizeLimit)maxTex.intValue}");

                var brightness = defaultSettings.FindPropertyRelative("mainTextureBrightness");
                if (brightness != null) sb.AppendLine($"    mainTextureBrightness: {brightness.floatValue}");

                var genShadow = defaultSettings.FindPropertyRelative("generateShadowFromNormalMap");
                if (genShadow != null) sb.AppendLine($"    generateShadowFromNormalMap: {genShadow.boolValue}");
            }

            // PhysBones to keep
            var keepBones = so.FindProperty("physBonesToKeep");
            if (keepBones != null && keepBones.isArray && keepBones.arraySize > 0)
            {
                sb.AppendLine($"  PhysBones to keep ({keepBones.arraySize}):");
                for (int i = 0; i < keepBones.arraySize; i++)
                {
                    var elem = keepBones.GetArrayElementAtIndex(i);
                    var obj = elem.objectReferenceValue;
                    sb.AppendLine($"    - {(obj != null ? obj.name : "(null)")}");
                }
            }

            // Material swaps
            var materialSwap = avatarRoot.GetComponent<MaterialSwap>();
            if (materialSwap != null)
            {
                var swapSO = new SerializedObject(materialSwap);
                var mappings = swapSO.FindProperty("materialMappings");
                if (mappings != null && mappings.isArray && mappings.arraySize > 0)
                {
                    sb.AppendLine($"  Material Swaps ({mappings.arraySize}):");
                    for (int i = 0; i < mappings.arraySize; i++)
                    {
                        var elem = mappings.GetArrayElementAtIndex(i);
                        var orig = elem.FindPropertyRelative("originalMaterial")?.objectReferenceValue;
                        var repl = elem.FindPropertyRelative("replacementMaterial")?.objectReferenceValue;
                        sb.AppendLine($"    - {(orig != null ? orig.name : "(null)")} → {(repl != null ? repl.name : "(null)")}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 6. CheckQuestLimits ==========

        [AgentTool("Check avatar against VRChat Quest limits. Reports polygon count, material count, mesh count, PhysBone count, and particle systems.")]
        public static string CheckQuestLimits(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Quest Limits Check for '{avatarRootName}':");
            sb.AppendLine();

            // Polygon count
            int totalPolygons = 0;
            var meshFilters = avatarRoot.GetComponentsInChildren<MeshFilter>(true);
            var skinnedMeshRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                    totalPolygons += mf.sharedMesh.triangles.Length / 3;
            }
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                    totalPolygons += smr.sharedMesh.triangles.Length / 3;
            }

            // Material slots
            var allRenderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int totalMaterialSlots = 0;
            var uniqueMaterials = new HashSet<Material>();
            foreach (var renderer in allRenderers)
            {
                if (renderer.sharedMaterials != null)
                {
                    totalMaterialSlots += renderer.sharedMaterials.Length;
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null) uniqueMaterials.Add(mat);
                    }
                }
            }

            // Mesh count
            int meshCount = meshFilters.Length + skinnedMeshRenderers.Length;

            // PhysBones
            var physBoneType = VRChatTools.FindVrcType(VRChatTools.VrcPhysBoneTypeName);
            int physBoneCount = 0;
            if (physBoneType != null)
                physBoneCount = avatarRoot.GetComponentsInChildren(physBoneType, true).Length;

            // Particle systems
            var particleSystems = avatarRoot.GetComponentsInChildren<ParticleSystem>(true);

            // Report with limits
            const int QUEST_POLY_LIMIT = 70000;
            const int QUEST_MATERIAL_LIMIT = 32;
            const int QUEST_MESH_LIMIT = 16;

            string StatusIcon(bool ok) => ok ? "[OK]" : "[OVER]";

            sb.AppendLine($"  Polygons: {totalPolygons:N0} / {QUEST_POLY_LIMIT:N0} {StatusIcon(totalPolygons <= QUEST_POLY_LIMIT)}");
            sb.AppendLine($"  Material Slots: {totalMaterialSlots} / {QUEST_MATERIAL_LIMIT} {StatusIcon(totalMaterialSlots <= QUEST_MATERIAL_LIMIT)}");
            sb.AppendLine($"  Unique Materials: {uniqueMaterials.Count}");
            sb.AppendLine($"  Meshes: {meshCount} / {QUEST_MESH_LIMIT} {StatusIcon(meshCount <= QUEST_MESH_LIMIT)}");
            sb.AppendLine($"  PhysBones: {physBoneCount} (Quest Very Poor allows 0)");
            sb.AppendLine($"  Particle Systems: {particleSystems.Length} (Quest Very Poor allows 0)");
            sb.AppendLine();

            // Overall assessment
            bool withinLimits = totalPolygons <= QUEST_POLY_LIMIT &&
                                totalMaterialSlots <= QUEST_MATERIAL_LIMIT &&
                                meshCount <= QUEST_MESH_LIMIT;

            if (withinLimits && physBoneCount == 0 && particleSystems.Length == 0)
                sb.AppendLine("  Overall: Within Quest limits (Medium or better).");
            else if (withinLimits)
                sb.AppendLine("  Overall: Mesh limits OK, but PhysBones/Particles need removal for Quest.");
            else
                sb.AppendLine("  Overall: Exceeds Quest limits. Optimization needed.");

            // Has converter settings?
            var settings = FindConverterSettings(avatarRoot);
            if (settings != null)
                sb.AppendLine("  AvatarConverterSettings: Present (NDMF auto-conversion enabled).");
            else
                sb.AppendLine("  AvatarConverterSettings: Not found. Use AddQuestConverterSettings to enable auto-conversion.");

            return sb.ToString().TrimEnd();
        }

#endif
    }
}
