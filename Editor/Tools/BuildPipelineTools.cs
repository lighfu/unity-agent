using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class BuildPipelineTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Trigger NDMF manual bake on an avatar. This processes all NDMF plugins (MA, AAO, etc.) and shows the result in scene. Use avatarRootName to specify the avatar.")]
        public static string TriggerNDMFManualBake(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            // Try to find NDMF AvatarProcessor
            var processorType = FindType("nadena.dev.ndmf.AvatarProcessor");
            if (processorType == null)
            {
                // Try alternative: manual bake menu item
                var manualBakeType = FindType("nadena.dev.ndmf.ManualBake");
                if (manualBakeType != null)
                {
                    // Select the avatar first
                    Selection.activeGameObject = go;
                    // Pin the parameterless overload (invoked with null args) to avoid
                    // AmbiguousMatchException if BakeAvatar is ever overloaded.
                    var bakeMethod = manualBakeType.GetMethod("BakeAvatar",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (bakeMethod != null)
                    {
                        try
                        {
                            bakeMethod.Invoke(null, null);
                            return $"Success: NDMF manual bake triggered for '{avatarRootName}'.";
                        }
                        catch (Exception e)
                        {
                            return $"Error: NDMF bake failed: {e.InnerException?.Message ?? e.Message}";
                        }
                    }
                }

                return "Error: NDMF not found. Install 'Non-Destructive Modular Framework' package.";
            }

            // Bake exactly like the official "Tools/NDM Framework/Manual bake avatar" menu:
            // NDMF's ManualProcessAvatar() CLONES the avatar and processes the clone, so it tolerates
            // prefab instances/assets. The plain ProcessAvatar(GameObject) overload processes the avatar
            // IN PLACE, and NDMF's BuildContext rejects prefab instances with
            // "Can't process an avatar that contains prefab instances/assets" — which is why the direct
            // call failed on real avatars. Prefer ManualProcessAvatar; fall back to the in-place API
            // (older NDMF) and finally the menu item.
            try
            {
                Selection.activeGameObject = go;

                // ManualProcessAvatar(GameObject obj, INDMFPlatformProvider platform = null).
                // Match by name + first-parameter type (never by name alone) to avoid
                // AmbiguousMatchException if the API gains overloads.
                var manualMethod = processorType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "ManualProcessAvatar"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(GameObject));
                if (manualMethod != null)
                {
                    var baked = InvokeStaticWithGameObject(manualMethod, go) as GameObject;
                    if (baked != null)
                    {
                        Selection.activeGameObject = baked;
                        EditorGUIUtility.PingObject(baked);
                    }
                    return $"Success: NDMF manual bake completed for '{avatarRootName}'. " +
                           $"A processed clone '{(baked != null ? baked.name : avatarRootName + " (Clone)")}' was created; " +
                           "the original avatar was left untouched.";
                }

                // Fallback (older NDMF): in-place ProcessAvatar(GameObject). Pin the single-GameObject
                // overload by parameter type to avoid AmbiguousMatchException. NOTE: this fails on avatars
                // that still contain prefab instances/assets.
                var processMethod = processorType.GetMethod("ProcessAvatar",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(GameObject) }, null);
                if (processMethod != null)
                {
                    processMethod.Invoke(null, new object[] { go });
                    return $"Success: NDMF ProcessAvatar completed for '{avatarRootName}'.";
                }

                // Final fallback: menu item (also clones internally; operates on the current Selection)
                bool menuResult = EditorApplication.ExecuteMenuItem("Tools/NDM Framework/Manual bake avatar");
                if (!menuResult)
                    return "Error: NDMF ManualProcessAvatar/ProcessAvatar not found, and the 'Tools/NDM Framework/Manual bake avatar' menu item could not be executed. NDMF may be missing or its API changed.";
                return $"Success: NDMF manual bake triggered via menu for '{avatarRootName}'.";
            }
            catch (Exception e)
            {
                return $"Error: NDMF processing failed: {e.InnerException?.Message ?? e.Message}";
            }
        }

        [AgentTool("List all installed NDMF plugins and their processing order. Shows which plugins will run during avatar build.")]
        public static string ListNDMFPlugins()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NDMF Plugin Analysis:");

            // Check for common NDMF-based packages
            var plugins = new (string typeName, string displayName, string define)[]
            {
                ("nadena.dev.modular_avatar.core.editor.BuildFramework", "Modular Avatar", "MODULAR_AVATAR"),
                ("Anatawa12.AvatarOptimizer.EntryPoint", "Avatar Optimizer", "AVATAR_OPTIMIZER"),
                ("VRCFury.Builder.VRCFuryBuilder", "VRCFury", "VRCFURY"),
                ("net.fushizen.mms.MeshSimplifierPlugin", "NDMF Mesh Simplifier", "NDMF_MESH_SIMPLIFIER"),
                ("nadena.dev.ndmf.AvatarProcessor", "NDMF Core", ""),
            };

            int found = 0;
            foreach (var (typeName, displayName, define) in plugins)
            {
                var type = FindType(typeName);
                if (type != null)
                {
                    found++;
                    var assembly = type.Assembly;
                    var version = assembly.GetName().Version;
                    sb.AppendLine($"  [{found}] {displayName}");
                    sb.AppendLine($"      Assembly: {assembly.GetName().Name}");
                    if (version != null && version.Major > 0)
                        sb.AppendLine($"      Version: {version}");
                }
            }

            if (found == 0)
            {
                sb.AppendLine("  No NDMF plugins detected.");
                sb.AppendLine("  Install NDMF-based tools like Modular Avatar or Avatar Optimizer.");
            }
            else
            {
                sb.AppendLine($"\n  Total: {found} plugin(s) detected.");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Run pre-build validation checks. Validates avatar before building: descriptor, parameters, missing refs, performance. Returns pass/fail with details.")]
        public static string ValidatePreBuild(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var descriptorType = VRChatTools.FindVrcType(VRChatTools.VrcDescriptorTypeName);
            if (descriptorType == null) return "Error: VRChat SDK not found.";

            var descriptor = go.GetComponent(descriptorType);
            if (descriptor == null) return $"Error: No VRCAvatarDescriptor on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Pre-Build Validation for '{avatarRootName}':");

            int errors = 0, warnings = 0;

            // 1. Descriptor check
            var dso = new SerializedObject(descriptor);
            var viewPos = dso.FindProperty("ViewPosition");
            if (viewPos != null && viewPos.vector3Value == Vector3.zero)
            {
                sb.AppendLine("  [Warning] ViewPosition is at origin (0,0,0). Set it to eye level.");
                warnings++;
            }

            // 2. Expression Parameters
            var exprParams = dso.FindProperty("expressionParameters");
            if (exprParams == null || exprParams.objectReferenceValue == null)
            {
                sb.AppendLine("  [Warning] No ExpressionParameters assigned.");
                warnings++;
            }
            else
            {
                var paramsSo = new SerializedObject(exprParams.objectReferenceValue);
                var parameters = paramsSo.FindProperty("parameters");
                if (parameters != null)
                {
                    int bits = 0;
                    for (int i = 0; i < parameters.arraySize; i++)
                    {
                        var param = parameters.GetArrayElementAtIndex(i);
                        var synced = param.FindPropertyRelative("networkSynced");
                        if (synced != null && synced.boolValue)
                        {
                            var vt = param.FindPropertyRelative("valueType");
                            bits += (vt != null && vt.intValue == 2) ? 1 : 8;
                        }
                    }
                    if (bits > 256)
                    {
                        sb.AppendLine($"  [Error] Parameter budget exceeded: {bits}/256 bits.");
                        errors++;
                    }
                }
            }

            // 3. Expression Menu
            var exprMenu = dso.FindProperty("expressionsMenu");
            if (exprMenu == null || exprMenu.objectReferenceValue == null)
            {
                sb.AppendLine("  [Warning] No ExpressionsMenu assigned.");
                warnings++;
            }
            else
            {
                var menuSo = new SerializedObject(exprMenu.objectReferenceValue);
                var controls = menuSo.FindProperty("controls");
                if (controls != null && controls.arraySize > 8)
                {
                    sb.AppendLine($"  [Error] Root menu has {controls.arraySize} controls (max 8).");
                    errors++;
                }
            }

            // 4. Missing components
            int missingRefs = 0;
            var allComponents = go.GetComponentsInChildren<Component>(true);
            foreach (var comp in allComponents)
            {
                if (comp == null) { missingRefs++; continue; }
            }
            if (missingRefs > 0)
            {
                sb.AppendLine($"  [Error] {missingRefs} missing component(s) (scripts removed/broken).");
                errors++;
            }

            // 5. Animator check
            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                sb.AppendLine("  [Error] No Animator component on avatar root.");
                errors++;
            }
            else if (animator.avatar == null)
            {
                sb.AppendLine("  [Error] Animator has no Avatar asset assigned.");
                errors++;
            }
            else if (!animator.isHuman)
            {
                sb.AppendLine("  [Error] Animator avatar is not Humanoid.");
                errors++;
            }

            // 6. Scale check
            if (go.transform.localScale != Vector3.one)
            {
                sb.AppendLine($"  [Warning] Avatar root scale is {go.transform.localScale} (should be (1,1,1)).");
                warnings++;
            }

            // 7. Position check
            if (go.transform.position != Vector3.zero)
            {
                sb.AppendLine($"  [Warning] Avatar root position is not at origin.");
                warnings++;
            }

            // 8. Polygon count
            int totalTris = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) totalTris += smr.sharedMesh.triangles.Length / 3;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) totalTris += mf.sharedMesh.triangles.Length / 3;

            if (totalTris > 70000)
            {
                sb.AppendLine($"  [Warning] High polygon count: {totalTris:N0} (Very Poor rank).");
                warnings++;
            }

            sb.AppendLine();
            if (errors == 0 && warnings == 0)
                sb.AppendLine("  [PASS] All checks passed. Ready to build.");
            else if (errors == 0)
                sb.AppendLine($"  [PASS with warnings] {warnings} warning(s). Build should succeed.");
            else
                sb.AppendLine($"  [FAIL] {errors} error(s), {warnings} warning(s). Fix errors before building.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Clean NDMF/VRChat build cache and temporary files. Removes generated assets from previous manual bakes.")]
        public static string CleanBuildCache()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Build cache cleanup:");

            int cleaned = 0;

            // Clean NDMF manual bake clones in scene.
            // Name-based detection is heuristic: "(Clone)" is also the default name for any
            // Instantiate() call, so destruction is gated behind explicit user confirmation.
            var clones = UnityEngine.Object.FindObjectsOfType<GameObject>()
                .Where(g => g.name.Contains("(Clone)") && g.transform.parent == null)
                .ToArray();

            if (clones.Length > 0)
            {
                sb.AppendLine($"  Found {clones.Length} root clone object(s) in scene (heuristic match on '(Clone)' suffix):");
                foreach (var clone in clones)
                    sb.AppendLine($"    - {clone.name}");

                if (AgentSettings.RequestConfirmation(
                    "ビルドキャッシュのクリーンアップ",
                    $"以下の {clones.Length} 個のルートオブジェクトを削除します。\n" +
                    "名前に '(Clone)' を含むだけのオブジェクトも対象になります。意図しないオブジェクトが含まれていないか確認してください。\n\n" +
                    string.Join("\n", clones.Select(c => $"  - {c.name}"))))
                {
                    foreach (var clone in clones)
                    {
                        Undo.DestroyObjectImmediate(clone);
                        cleaned++;
                    }
                }
                else
                {
                    sb.AppendLine("  Skipped clone deletion (user declined).");
                }
            }

            // Clean temp folders
            string[] tempPaths = {
                "Assets/_NDMF_Temp",
                "Assets/~NDMF_Temp",
            };

            foreach (var path in tempPaths)
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    sb.AppendLine($"  Deleted temp folder: {path}");
                    cleaned++;
                }
            }

            if (cleaned == 0)
                sb.AppendLine("  No build cache/temporary files found.");
            else
                sb.AppendLine($"\n  Cleaned {cleaned} item(s).");

            AssetDatabase.Refresh();
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Execute a VRChat SDK build and test (local test in VRChat). Requires VRChat SDK.")]
        public static string TriggerVRChatBuildTest(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            Selection.activeGameObject = go;

            // Try to invoke VRChat SDK build
            try
            {
                bool result = EditorApplication.ExecuteMenuItem("VRChat SDK/Build & Test New Build");
                if (result) return $"Success: VRChat Build & Test triggered for '{avatarRootName}'.";
                return "Error: Could not execute 'VRChat SDK/Build & Test New Build' menu item.";
            }
            catch (Exception e)
            {
                return $"Error: VRChat build failed: {e.Message}";
            }
        }

        // ===== Helpers =====

        // Invokes a static NDMF method whose first parameter is the avatar GameObject, filling any
        // remaining parameters with their default value (or null for reference types). Lets us call
        // ManualProcessAvatar(GameObject, INDMFPlatformProvider = null) without a compile-time NDMF ref.
        private static object InvokeStaticWithGameObject(MethodInfo method, GameObject go)
        {
            var pars = method.GetParameters();
            var args = new object[pars.Length];
            args[0] = go;
            for (int i = 1; i < pars.Length; i++)
                args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : null;
            return method.Invoke(null, args);
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
