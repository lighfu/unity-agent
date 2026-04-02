#if VRCFURY
using com.vrcfury.api;
using com.vrcfury.api.Components;
using com.vrcfury.api.Actions;
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
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class OutfitWizardTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ========== Helpers ==========

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            return go;
        }

        private static bool IsOutfit(Transform child)
        {
            // An outfit typically has an Armature child or SkinnedMeshRenderers
            if (child.Find("Armature") != null) return true;
            if (child.GetComponent<SkinnedMeshRenderer>() != null) return true;
            if (child.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0) return true;
            return false;
        }

        // ========== 1. SetupOutfitWizard ==========

        [AgentTool("Complete outfit setup: deactivate old outfit → instantiate prefab → MA Setup Outfit → create toggle → add optimizer. prefabPath: outfit prefab asset path. oldOutfitName: outfit to deactivate (optional). toggleMethod: 'auto'/'vrcfury'/'ma'.")]
        public static string SetupOutfitWizard(string avatarRootName, string prefabPath,
            string oldOutfitName = "", string toggleMethod = "auto",
            string toggleMenuPath = "", string addOptimizer = "true")
        {
            var maErr = MAAvailability.CheckOrError();
            if (maErr != null) return maErr;

            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return $"Error: Prefab not found at '{prefabPath}'.";

            bool doOptimize = addOptimizer.Equals("true", StringComparison.OrdinalIgnoreCase);

            // Build confirmation message
            var confirmSb = new StringBuilder();
            confirmSb.AppendLine($"アバター: {avatarRootName}");
            confirmSb.AppendLine($"プレハブ: {prefabPath}");
            if (!string.IsNullOrEmpty(oldOutfitName))
                confirmSb.AppendLine($"旧衣装無効化: {oldOutfitName}");
            confirmSb.AppendLine($"トグル方式: {toggleMethod}");
            confirmSb.AppendLine($"オプティマイザー追加: {doOptimize}");
            confirmSb.AppendLine("\n以下の手順を実行します:");
            if (!string.IsNullOrEmpty(oldOutfitName))
                confirmSb.AppendLine("  1. 旧衣装を無効化 (SetActive=false, Tag=EditorOnly)");
            confirmSb.AppendLine("  2. プレハブをインスタンス化");
            confirmSb.AppendLine("  3. MA Setup Outfit 実行");
            confirmSb.AppendLine("  4. トグル作成");
            if (doOptimize)
                confirmSb.AppendLine("  5. AAO TraceAndOptimize 追加 (未存在時)");

            if (!AgentSettings.RequestConfirmation(
                "衣装セットアップウィザード", confirmSb.ToString()))
                return "Cancelled: User denied the operation.";

            var result = new StringBuilder();
            result.AppendLine($"Outfit Setup Wizard for '{avatarRootName}':");
            int step = 1;

            // Step 1: Deactivate old outfit
            if (!string.IsNullOrEmpty(oldOutfitName))
            {
                var oldOutfit = avatarRoot.transform.Find(oldOutfitName);
                if (oldOutfit != null)
                {
                    Undo.RecordObject(oldOutfit.gameObject, "Deactivate Old Outfit");
                    oldOutfit.gameObject.SetActive(false);
                    oldOutfit.gameObject.tag = "EditorOnly";
                    EditorUtility.SetDirty(oldOutfit.gameObject);
                    result.AppendLine($"  [{step}] Deactivated old outfit '{oldOutfitName}'.");
                }
                else
                {
                    result.AppendLine($"  [{step}] Warning: Old outfit '{oldOutfitName}' not found. Skipped.");
                }
                step++;
            }

            // Step 2: Instantiate prefab
            GameObject outfitInstance;
            try
            {
                outfitInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(outfitInstance, "Instantiate Outfit Prefab");
                outfitInstance.transform.SetParent(avatarRoot.transform, false);
                outfitInstance.transform.localPosition = Vector3.zero;
                outfitInstance.transform.localRotation = Quaternion.identity;
                outfitInstance.transform.localScale = Vector3.one;
                result.AppendLine($"  [{step}] Instantiated '{outfitInstance.name}' under avatar.");
            }
            catch (Exception ex)
            {
                return result.ToString() + $"\n  [{step}] Error: Failed to instantiate prefab: {ex.Message}";
            }
            step++;

            // Step 3: MA Setup Outfit
            try
            {
                MAComponentFactory.SetupOutfit(outfitInstance);
                result.AppendLine($"  [{step}] MA Setup Outfit completed.");
            }
            catch (Exception ex)
            {
                result.AppendLine($"  [{step}] Warning: MA Setup Outfit failed: {ex.Message}");
            }
            step++;

            // Step 4: Create toggle
            string outfitName = outfitInstance.name;
            string menuPath = !string.IsNullOrEmpty(toggleMenuPath)
                ? toggleMenuPath
                : outfitName;

            bool toggleCreated = false;

#if VRCFURY
            if (toggleMethod == "auto" || toggleMethod == "vrcfury")
            {
                try
                {
                    var holderName = $"VRCFury_Toggle_{outfitName}";
                    var holder = new GameObject(holderName);
                    Undo.RegisterCreatedObjectUndo(holder, "Create Outfit Toggle");
                    holder.transform.SetParent(avatarRoot.transform, false);

                    var toggle = FuryComponents.CreateToggle(holder);
                    toggle.SetMenuPath(menuPath);
                    toggle.SetSaved();
                    toggle.SetDefaultOn();
                    toggle.GetActions().AddTurnOn(outfitInstance);

                    EditorUtility.SetDirty(holder);
                    result.AppendLine($"  [{step}] Created VRCFury toggle '{menuPath}'.");
                    toggleCreated = true;
                }
                catch (Exception ex)
                {
                    result.AppendLine($"  [{step}] Warning: VRCFury toggle failed: {ex.Message}");
                }
            }
#endif

            if (!toggleCreated && (toggleMethod == "auto" || toggleMethod == "ma"))
            {
                // Fallback: use VRChatTools.SetupObjectToggle approach via MA
                try
                {
                    string toggleResult = VRChatTools.SetupObjectToggle(avatarRootName, outfitName, outfitName, true);
                    if (toggleResult.Contains("Success") || toggleResult.Contains("Setting up"))
                    {
                        result.AppendLine($"  [{step}] Created MA-based toggle for '{outfitName}'.");
                        toggleCreated = true;
                    }
                    else
                    {
                        result.AppendLine($"  [{step}] Warning: MA toggle: {toggleResult}");
                    }
                }
                catch (Exception ex)
                {
                    result.AppendLine($"  [{step}] Warning: MA toggle failed: {ex.Message}");
                }
            }

            if (!toggleCreated)
            {
                result.AppendLine($"  [{step}] Warning: No toggle created. Create manually.");
            }
            step++;

            // Step 5: Add AAO TraceAndOptimize
#if AVATAR_OPTIMIZER
            if (doOptimize)
            {
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
                step++;
            }
#else
            if (doOptimize)
            {
                result.AppendLine($"  [{step}] AAO not installed. Skipped optimizer.");
                step++;
            }
#endif

            result.AppendLine();
            result.AppendLine($"Done! Outfit '{outfitName}' is set up.");

            return result.ToString().TrimEnd();
        }

        // ========== 2. DeactivateOutfit ==========

        [AgentTool("Deactivate an outfit. Sets inactive and tags EditorOnly.")]
        public static string DeactivateOutfit(string avatarRootName, string outfitName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var outfit = avatarRoot.transform.Find(outfitName);
            if (outfit == null)
                return $"Error: Outfit '{outfitName}' not found under '{avatarRootName}'.";

            if (!AgentSettings.RequestConfirmation(
                "衣装の無効化",
                $"対象: {outfitName}\nSetActive(false) + Tag=EditorOnly にします。"))
                return "Cancelled: User denied the operation.";

            Undo.RecordObject(outfit.gameObject, "Deactivate Outfit");
            outfit.gameObject.SetActive(false);
            outfit.gameObject.tag = "EditorOnly";
            EditorUtility.SetDirty(outfit.gameObject);

            return $"Success: Deactivated outfit '{outfitName}'. Set inactive and tagged EditorOnly.";
        }

        // ========== 3. ListOutfits ==========

        [AgentTool("List all outfits under an avatar (direct children with Armature or SkinnedMeshRenderer). Shows active/inactive and MA components.")]
        public static string ListOutfits(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Outfits under '{avatarRootName}':");

            int count = 0;
            for (int i = 0; i < avatarRoot.transform.childCount; i++)
            {
                var child = avatarRoot.transform.GetChild(i);
                if (!IsOutfit(child)) continue;

                count++;
                string activeStatus = child.gameObject.activeSelf ? "Active" : "Inactive";
                string tag = child.gameObject.tag == "EditorOnly" ? " [EditorOnly]" : "";

                sb.AppendLine($"  {child.name} — {activeStatus}{tag}");

                // Check for MA components
                var maComponents = new List<string>();
                foreach (var comp in child.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().FullName;
                    if (typeName != null && typeName.Contains("modular_avatar"))
                        maComponents.Add(comp.GetType().Name);
                }
                if (maComponents.Count > 0)
                    sb.AppendLine($"    MA: {string.Join(", ", maComponents)}");

                // Count meshes
                var smrs = child.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length > 0)
                    sb.AppendLine($"    Meshes: {smrs.Length}");
            }

            if (count == 0)
                return $"No outfits found under '{avatarRootName}'.";

            sb.Insert(sb.ToString().IndexOf(':') + 1, $" ({count} found)");

            return sb.ToString().TrimEnd();
        }
    }
}
