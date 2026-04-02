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
    /// <summary>
    /// FaceEmo integration tools using reflection only (no compile-time dependency on FaceEmo package).
    /// All FaceEmo types are accessed via reflection and SerializedObject to avoid compilation errors
    /// when FaceEmo is not installed.
    /// </summary>
    public static class FaceEmoTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        // Cached types — resolved lazily via reflection
        private static Type _launcherCompType;
        private static Type _launcherEditorType;
        private static Type _projectType;
        private static Type _mainWindowType;
        private static bool _typesResolved;

        private static void EnsureTypes()
        {
            if (_typesResolved) return;
            _typesResolved = true;
            _launcherCompType = FindType("Suzuryg.FaceEmo.Components.FaceEmoLauncherComponent");
            _launcherEditorType = FindType("Suzuryg.FaceEmo.AppMain.FaceEmoLauncher");
            _projectType = FindType("Suzuryg.FaceEmo.Components.FaceEmoProject");
            _mainWindowType = FindType("Suzuryg.FaceEmo.AppMain.MainWindow");
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

        private static bool IsInstalled()
        {
            EnsureTypes();
            return _launcherCompType != null;
        }

        // ─── AgentTools ───

        [AgentTool("Find all FaceEmo objects in the current scene. Shows names, target avatars, and instance IDs. FaceEmo is a face expression menu tool for VRChat.")]
        public static string FindFaceEmo()
        {
            if (!IsInstalled())
                return "FaceEmo is not installed in this project (package: jp.suzuryg.face-emo).";

            var launchers = UnityEngine.Object.FindObjectsOfType(_launcherCompType);
            if (launchers.Length == 0)
                return "No FaceEmo objects found in the scene. Use ExecuteMenu('FaceEmo/New Menu') to create one.";

            var sb = new StringBuilder();
            sb.AppendLine($"FaceEmo objects in scene ({launchers.Length}):");

            foreach (var obj in launchers)
            {
                var comp = obj as Component;
                if (comp == null) continue;

                var so = new SerializedObject(obj);
                int instanceId = so.FindProperty("InstanceId")?.intValue ?? -1;

                // Follow AV3Setting ScriptableObject reference → TargetAvatar
                string avatarName = "None";
                var av3Prop = so.FindProperty("AV3Setting");
                if (av3Prop != null && av3Prop.objectReferenceValue != null)
                {
                    var av3SO = new SerializedObject(av3Prop.objectReferenceValue);
                    var targetProp = av3SO.FindProperty("TargetAvatar");
                    if (targetProp != null && targetProp.objectReferenceValue != null)
                        avatarName = targetProp.objectReferenceValue.name;
                }

                string status = avatarName == "None" ? " [NOT CONFIGURED]" : "";
                sb.AppendLine($"  {comp.gameObject.name}  (InstanceId={instanceId}, Avatar={avatarName}){status}");
            }

            // Add guidance when avatars are not configured
            bool anyConfigured = false;
            foreach (var obj in launchers)
            {
                var so2 = new SerializedObject(obj);
                var av3 = so2.FindProperty("AV3Setting");
                if (av3 != null && av3.objectReferenceValue != null)
                {
                    var av3SO = new SerializedObject(av3.objectReferenceValue);
                    var targetProp = av3SO.FindProperty("TargetAvatar");
                    if (targetProp != null && targetProp.objectReferenceValue != null)
                    { anyConfigured = true; break; }
                }
            }
            if (!anyConfigured)
                sb.AppendLine("\nWarning: No FaceEmo object has a configured avatar (Avatar=None). Use ConfigureTargetAvatar(\"AvatarName\") to set the target avatar.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect a FaceEmo configuration in detail. Shows AV3 settings (target avatar, blink, mouth morphs, options). gameObjectName defaults to first FaceEmo_* found.")]
        public static string InspectFaceEmo(string gameObjectName = "")
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return string.IsNullOrEmpty(gameObjectName)
                    ? "No FaceEmo objects found. Use ExecuteMenu('FaceEmo/New Menu') to create one."
                    : $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            var sb = new StringBuilder();
            sb.AppendLine($"FaceEmo: {(comp as Component).gameObject.name}");
            sb.AppendLine($"InstanceId: {so.FindProperty("InstanceId")?.intValue}");

            // Read AV3Setting
            var av3Ref = so.FindProperty("AV3Setting");
            if (av3Ref != null && av3Ref.objectReferenceValue != null)
            {
                sb.AppendLine();
                sb.AppendLine("── AV3 Settings ──");
                var av3SO = new SerializedObject(av3Ref.objectReferenceValue);
                DumpImportantAV3Fields(av3SO, sb);
            }

            // Avatar configuration warning
            if (!IsAvatarConfigured(comp))
            {
                sb.AppendLine();
                sb.AppendLine("⚠ Avatar is NOT configured. FaceEmo cannot be used until an avatar is assigned.");
                sb.AppendLine("Use CreateExpressionClipFromData to create expression clips directly.");
            }

            // Find and show expression menu data
            sb.AppendLine();
            sb.AppendLine("── Expression Menu ──");
            AppendMenuData(comp, sb);

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all FaceEmo expression modes with their gesture mappings and animation names.")]
        public static string ListFaceEmoExpressions(string gameObjectName = "")
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return string.IsNullOrEmpty(gameObjectName)
                    ? "No FaceEmo objects found."
                    : $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"FaceEmo Expressions: {(comp as Component).gameObject.name}");

            if (!IsAvatarConfigured(comp))
            {
                sb.AppendLine("⚠ Avatar is NOT configured. FaceEmo cannot be used. Use CreateExpressionClipFromData to create expression clips directly.");
                return sb.ToString().TrimEnd();
            }

            AppendMenuData(comp, sb);
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Open the FaceEmo editor window for a specific FaceEmo object.")]
        public static string LaunchFaceEmoWindow(string gameObjectName = "")
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return string.IsNullOrEmpty(gameObjectName)
                    ? "No FaceEmo objects found."
                    : $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            // Pre-check: Avatar must be configured to avoid NullReferenceException in FaceEmoInstaller
            if (!IsAvatarConfigured(comp))
                return $"Error: FaceEmo on '{(comp as Component).gameObject.name}' has no avatar configured (Avatar=None). Cannot launch FaceEmo window. Use CreateExpressionClipFromData to create expression clips directly.";

            EnsureTypes();
            if (_launcherEditorType != null)
            {
                var launchMethod = _launcherEditorType.GetMethod("Launch",
                    BindingFlags.Public | BindingFlags.Static);
                if (launchMethod != null)
                {
                    try
                    {
                        launchMethod.Invoke(null, new object[] { comp });
                        return $"Success: FaceEmo window launched for '{(comp as Component).gameObject.name}'.";
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        return $"Error launching FaceEmo window: {inner.Message}";
                    }
                }
            }

            // Fallback
            Selection.activeGameObject = (comp as Component).gameObject;
            return $"FaceEmo object selected. Click the FaceEmo button in Inspector to open the editor.";
        }

        [AgentTool("Read a serialized property from a FaceEmo component. Use dot notation for nested fields. Examples: 'AV3Setting.TargetAvatar', 'AV3Setting.SmoothAnalogFist', 'AV3Setting.BlinkClip'.")]
        public static string ReadFaceEmoProperty(string gameObjectName, string propertyPath)
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            // Handle dot notation for nested ScriptableObject access
            var result = ResolveNestedProperty(comp, propertyPath);
            if (result.error != null) return result.error;

            return $"{propertyPath} = {ReadPropertyValue(result.prop)}";
        }

        [AgentTool("Write a serialized property on a FaceEmo component. Supports string, int, float, bool, enum. Examples: WriteFaceEmoProperty('FaceEmo_1', 'AV3Setting.SmoothAnalogFist', 'false').")]
        public static string WriteFaceEmoProperty(string gameObjectName, string propertyPath, string value)
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            var result = ResolveNestedProperty(comp, propertyPath);
            if (result.error != null) return result.error;

            Undo.RecordObject(result.targetObject, "Modify FaceEmo Property");

            if (!WritePropertyValue(result.prop, value))
                return $"Error: Cannot write value '{value}' to '{propertyPath}' (type: {result.prop.propertyType}).";

            result.so.ApplyModifiedProperties();
            EditorUtility.SetDirty(result.targetObject);
            return $"Success: {propertyPath} = {value}";
        }

        [AgentTool("List all serialized properties of a FaceEmo sub-object. subObject can be: 'AV3Setting', 'ThumbnailSetting', 'ExpressionEditorSetting', or empty for the launcher itself.")]
        public static string ListFaceEmoProperties(string gameObjectName, string subObject = "")
        {
            if (!IsInstalled()) return "FaceEmo is not installed.";

            var comp = FindLauncherComponent(gameObjectName);
            if (comp == null)
                return $"Error: FaceEmo launcher not found on '{gameObjectName}'.";

            SerializedObject so;
            if (string.IsNullOrEmpty(subObject))
            {
                so = new SerializedObject(comp);
            }
            else
            {
                var launcherSO = new SerializedObject(comp);
                var refProp = launcherSO.FindProperty(subObject);
                if (refProp == null)
                    return $"Error: Property '{subObject}' not found on launcher component.";
                if (refProp.objectReferenceValue == null)
                    return $"Error: '{subObject}' is null (not assigned).";
                so = new SerializedObject(refProp.objectReferenceValue);
            }

            var sb = new StringBuilder();
            string label = string.IsNullOrEmpty(subObject) ? "Launcher" : subObject;
            sb.AppendLine($"Properties of {label} on '{gameObjectName}':");

            var iter = so.GetIterator();
            bool enter = true;
            while (iter.NextVisible(enter))
            {
                enter = true;
                if (iter.name == "m_Script" || iter.name == "m_ObjectHideFlags") continue;
                if (iter.depth > 3) continue;

                string indent = new string(' ', iter.depth * 2 + 2);
                if (iter.hasChildren && iter.propertyType == SerializedPropertyType.Generic)
                {
                    if (iter.isArray)
                        sb.AppendLine($"{indent}{iter.propertyPath} (Array[{iter.arraySize}])");
                    else
                        sb.AppendLine($"{indent}{iter.propertyPath}:");
                }
                else
                {
                    sb.AppendLine($"{indent}{iter.propertyPath} = {ReadPropertyValue(iter)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ─── Internal Helpers ───

        /// <summary>
        /// Check if a FaceEmo launcher component has a configured avatar (TargetAvatar != null).
        /// </summary>
        private static bool IsAvatarConfigured(UnityEngine.Object launcherComp)
        {
            if (launcherComp == null) return false;
            var so = new SerializedObject(launcherComp);
            var av3Ref = so.FindProperty("AV3Setting");
            if (av3Ref == null || av3Ref.objectReferenceValue == null) return false;
            var av3SO = new SerializedObject(av3Ref.objectReferenceValue);
            var targetProp = av3SO.FindProperty("TargetAvatar");
            return targetProp != null && targetProp.objectReferenceValue != null;
        }

        private static UnityEngine.Object FindLauncherComponent(string gameObjectName)
        {
            EnsureTypes();
            if (_launcherCompType == null) return null;

            if (!string.IsNullOrEmpty(gameObjectName))
            {
                var go = FindGO(gameObjectName);
                if (go == null) return null;
                return go.GetComponent(_launcherCompType);
            }

            // Auto-find: prefer FaceEmo_* named root objects
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root.name.StartsWith("FaceEmo", StringComparison.OrdinalIgnoreCase))
                {
                    var comp = root.GetComponent(_launcherCompType);
                    if (comp != null) return comp;
                }
            }

            // Fallback: search all
            return UnityEngine.Object.FindObjectOfType(_launcherCompType);
        }

        private static void DumpImportantAV3Fields(SerializedObject av3SO, StringBuilder sb)
        {
            // Target avatar
            AppendPropLine(av3SO, "TargetAvatar", sb, "  Target Avatar");
            AppendPropLine(av3SO, "TargetAvatarPath", sb, "  Target Avatar Path");

            // Blink
            AppendPropLine(av3SO, "UseBlinkClip", sb, "  Use Blink Clip");
            AppendPropLine(av3SO, "BlinkClip", sb, "  Blink Clip");

            // Mouth morphs
            var mouthMorphs = av3SO.FindProperty("MouthMorphs");
            if (mouthMorphs != null)
                sb.AppendLine($"  Mouth Morphs: {mouthMorphs.arraySize} entries");

            AppendPropLine(av3SO, "UseMouthMorphCancelClip", sb, "  Use Mouth Morph Cancel Clip");
            AppendPropLine(av3SO, "MouthMorphCancelClip", sb, "  Mouth Morph Cancel Clip");

            // Additional meshes
            var additionalMeshes = av3SO.FindProperty("AdditionalSkinnedMeshes");
            if (additionalMeshes != null)
                sb.AppendLine($"  Additional Skinned Meshes: {additionalMeshes.arraySize}");

            // Options
            sb.AppendLine("  ─ Options ─");
            AppendPropLine(av3SO, "SmoothAnalogFist", sb, "  Smooth Analog Fist");
            AppendPropLine(av3SO, "TransitionDurationSeconds", sb, "  Transition Duration (sec)");
            AppendPropLine(av3SO, "ReplaceBlink", sb, "  Replace Blink");
            AppendPropLine(av3SO, "DisableTrackingControls", sb, "  Disable Tracking Controls");
            AppendPropLine(av3SO, "MatchAvatarWriteDefaults", sb, "  Match Avatar Write Defaults");
            AppendPropLine(av3SO, "GenerateExMenuThumbnails", sb, "  Generate ExMenu Thumbnails");
            AppendPropLine(av3SO, "DisableFxDuringDancing", sb, "  Disable FX During Dancing");

            // AFK
            AppendPropLine(av3SO, "ChangeAfkFace", sb, "  Change AFK Face");

            // Config flags
            sb.AppendLine("  ─ Menu Configs ─");
            AppendPropLine(av3SO, "AddConfig_EmoteSelect", sb, "  Emote Select");
            AppendPropLine(av3SO, "AddConfig_BlinkOff", sb, "  Blink Off");
            AppendPropLine(av3SO, "AddConfig_DanceGimmick", sb, "  Dance Gimmick");
            AppendPropLine(av3SO, "AddConfig_ContactLock", sb, "  Contact Lock");
            AppendPropLine(av3SO, "AddConfig_Override", sb, "  Override");
            AppendPropLine(av3SO, "AddConfig_Voice", sb, "  Voice");
        }

        private static void AppendPropLine(SerializedObject so, string propName, StringBuilder sb, string label)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) return;
            sb.AppendLine($"{label}: {ReadPropertyValue(prop)}");
        }

        private static void AppendMenuData(UnityEngine.Object launcherComp, StringBuilder sb)
        {
            EnsureTypes();

            // Strategy 1: Find FaceEmoProject via AssetDatabase
            if (_projectType != null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{_projectType.Name}");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var project = AssetDatabase.LoadAssetAtPath(path, _projectType);
                    if (project == null) continue;

                    var projectSO = new SerializedObject(project);
                    var menuRef = projectSO.FindProperty("SerializableMenu");
                    if (menuRef != null && menuRef.objectReferenceValue != null)
                    {
                        sb.AppendLine($"  Project: {path}");
                        DumpMenuFromSerializableMenu(menuRef.objectReferenceValue, sb);
                        return;
                    }
                }
            }

            // Strategy 2: Try loading menu via FaceEmoInstaller + MenuRepository (reflection)
            var installerType = FindType("Suzuryg.FaceEmo.AppMain.FaceEmoInstaller");
            if (installerType != null)
            {
                try
                {
                    var comp = launcherComp as Component;
                    if (comp == null) { sb.AppendLine("  (Cannot access menu data)"); return; }

                    var ctor = installerType.GetConstructor(new[] { typeof(GameObject) });
                    if (ctor != null)
                    {
                        var installer = ctor.Invoke(new object[] { comp.gameObject });
                        var containerProp = installerType.GetProperty("Container");
                        if (containerProp != null)
                        {
                            var container = containerProp.GetValue(installer);
                            // Resolve IMenuRepository
                            var repoType = FindType("Suzuryg.FaceEmo.UseCase.IMenuRepository");
                            if (container != null && repoType != null)
                            {
                                var resolveMethod = container.GetType().GetMethod("Resolve", Type.EmptyTypes);
                                if (resolveMethod != null)
                                {
                                    var genericResolve = resolveMethod.MakeGenericMethod(repoType);
                                    var repo = genericResolve.Invoke(container, null);

                                    // Load menu
                                    var loadMethod = repoType.GetMethod("Load");
                                    if (loadMethod != null)
                                    {
                                        var menu = loadMethod.Invoke(repo, new object[] { null });
                                        DumpDomainMenu(menu, sb);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  (Error reading menu via installer: {ex.InnerException?.Message ?? ex.Message})");
                }
            }

            // Strategy 3: Search for sub-assets
            var av3Ref = new SerializedObject(launcherComp).FindProperty("AV3Setting");
            if (av3Ref != null && av3Ref.objectReferenceValue != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(av3Ref.objectReferenceValue);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    // Look for menu data in the same asset
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var sub in subAssets)
                    {
                        if (sub != null && sub.GetType().Name == "SerializableMenu")
                        {
                            DumpMenuFromSerializableMenu(sub, sb);
                            return;
                        }
                    }

                    // Look for FaceEmoProject in the same directory
                    string dir = System.IO.Path.GetDirectoryName(assetPath).Replace("\\", "/");
                    string[] projGuids = AssetDatabase.FindAssets($"t:ScriptableObject", new[] { dir });
                    foreach (string guid in projGuids)
                    {
                        string projPath = AssetDatabase.GUIDToAssetPath(guid);
                        var asset = AssetDatabase.LoadMainAssetAtPath(projPath);
                        if (asset != null && asset.GetType().Name == "FaceEmoProject")
                        {
                            var projectSO = new SerializedObject(asset);
                            var menuRef = projectSO.FindProperty("SerializableMenu");
                            if (menuRef != null && menuRef.objectReferenceValue != null)
                            {
                                sb.AppendLine($"  Project: {projPath}");
                                DumpMenuFromSerializableMenu(menuRef.objectReferenceValue, sb);
                                return;
                            }
                        }
                    }
                }
            }

            sb.AppendLine("  (No expression menu data found. Open FaceEmo window to initialize.)");
        }

        private static void DumpMenuFromSerializableMenu(UnityEngine.Object menuObj, StringBuilder sb)
        {
            var menuSO = new SerializedObject(menuObj);
            var defaultSel = menuSO.FindProperty("DefaultSelection");
            if (defaultSel != null)
                sb.AppendLine($"  Default Selection: \"{defaultSel.stringValue}\"");

            // Read Registered menu item list
            var registeredRef = menuSO.FindProperty("Registered");
            if (registeredRef != null && registeredRef.objectReferenceValue != null)
            {
                DumpMenuItemList(registeredRef.objectReferenceValue, sb, "Registered");
            }

            // Read Unregistered menu item list
            var unregisteredRef = menuSO.FindProperty("Unregistered");
            if (unregisteredRef != null && unregisteredRef.objectReferenceValue != null)
            {
                var unregSO = new SerializedObject(unregisteredRef.objectReferenceValue);
                var orderProp = unregSO.FindProperty("Order");
                int unregCount = orderProp != null ? orderProp.arraySize : 0;
                if (unregCount > 0)
                    sb.AppendLine($"  Unregistered items: {unregCount}");
            }
        }

        private static void DumpMenuItemList(UnityEngine.Object listObj, StringBuilder sb, string label)
        {
            var listSO = new SerializedObject(listObj);

            // Order array contains IDs of modes/groups
            var orderProp = listSO.FindProperty("Order");
            if (orderProp == null || orderProp.arraySize == 0)
            {
                sb.AppendLine($"  {label}: (empty)");
                return;
            }

            sb.AppendLine($"  {label} ({orderProp.arraySize} items):");

            // Modes dictionary (serialized as parallel arrays or similar)
            var modesProp = listSO.FindProperty("Modes");
            var groupsProp = listSO.FindProperty("Groups");

            // Read mode data
            if (modesProp != null && modesProp.isArray)
            {
                for (int i = 0; i < modesProp.arraySize; i++)
                {
                    var modeRef = modesProp.GetArrayElementAtIndex(i);
                    if (modeRef.objectReferenceValue == null) continue;
                    DumpMode(modeRef.objectReferenceValue, sb, i);
                }
            }

            // Read group data
            if (groupsProp != null && groupsProp.isArray)
            {
                for (int i = 0; i < groupsProp.arraySize; i++)
                {
                    var groupRef = groupsProp.GetArrayElementAtIndex(i);
                    if (groupRef.objectReferenceValue == null) continue;
                    DumpGroup(groupRef.objectReferenceValue, sb, i);
                }
            }
        }

        private static void DumpMode(UnityEngine.Object modeObj, StringBuilder sb, int index)
        {
            var modeSO = new SerializedObject(modeObj);
            string displayName = modeSO.FindProperty("DisplayName")?.stringValue ?? "(unnamed)";
            bool changeDefault = modeSO.FindProperty("ChangeDefaultFace")?.boolValue ?? false;
            int eyeTracking = modeSO.FindProperty("EyeTrackingControl")?.enumValueIndex ?? 0;
            int mouthTracking = modeSO.FindProperty("MouthTrackingControl")?.enumValueIndex ?? 0;
            bool blinkEnabled = modeSO.FindProperty("BlinkEnabled")?.boolValue ?? true;

            // Animation GUID
            string animGuid = "";
            var animProp = modeSO.FindProperty("Animation");
            if (animProp != null)
            {
                var guidProp = animProp.FindPropertyRelative("GUID") ?? animProp.FindPropertyRelative("_guid");
                if (guidProp != null) animGuid = guidProp.stringValue;
            }
            string animName = GuidToAnimationName(animGuid);

            sb.AppendLine($"    [{index}] Mode: \"{displayName}\"" +
                          (changeDefault ? " [DEFAULT]" : "") +
                          $"  Animation={animName}");

            // Branches
            var branchesProp = modeSO.FindProperty("Branches");
            if (branchesProp != null && branchesProp.isArray && branchesProp.arraySize > 0)
            {
                for (int b = 0; b < branchesProp.arraySize; b++)
                {
                    var branchRef = branchesProp.GetArrayElementAtIndex(b);
                    if (branchRef.objectReferenceValue == null) continue;
                    DumpBranch(branchRef.objectReferenceValue, sb, b);
                }
            }
        }

        private static void DumpBranch(UnityEngine.Object branchObj, StringBuilder sb, int branchIndex)
        {
            var branchSO = new SerializedObject(branchObj);

            // Conditions
            var condsProp = branchSO.FindProperty("Conditions");
            string condStr = "";
            if (condsProp != null && condsProp.isArray && condsProp.arraySize > 0)
            {
                var parts = new List<string>();
                for (int c = 0; c < condsProp.arraySize; c++)
                {
                    var condElem = condsProp.GetArrayElementAtIndex(c);
                    int hand = condElem.FindPropertyRelative("Hand")?.enumValueIndex ?? 0;
                    int gesture = condElem.FindPropertyRelative("HandGesture")?.enumValueIndex ?? 0;
                    int op = condElem.FindPropertyRelative("ComparisonOperator")?.enumValueIndex ?? 0;

                    string handStr = hand == 0 ? "L" : "R";
                    string gestureStr = GetGestureName(gesture);
                    string opStr = GetComparisonOp(op);
                    parts.Add($"{handStr}{opStr}{gestureStr}");
                }
                condStr = string.Join(", ", parts);
            }

            // Animations
            string baseAnim = ReadAnimationGuid(branchSO, "BaseAnimation");
            string leftAnim = ReadAnimationGuid(branchSO, "LeftHandAnimation");
            string rightAnim = ReadAnimationGuid(branchSO, "RightHandAnimation");
            string bothAnim = ReadAnimationGuid(branchSO, "BothHandsAnimation");

            sb.Append($"      Branch[{branchIndex}]");
            if (!string.IsNullOrEmpty(condStr)) sb.Append($" ({condStr})");
            sb.Append($": base={baseAnim}");
            if (!string.IsNullOrEmpty(leftAnim) && leftAnim != "None") sb.Append($", left={leftAnim}");
            if (!string.IsNullOrEmpty(rightAnim) && rightAnim != "None") sb.Append($", right={rightAnim}");
            if (!string.IsNullOrEmpty(bothAnim) && bothAnim != "None") sb.Append($", both={bothAnim}");
            sb.AppendLine();
        }

        private static void DumpGroup(UnityEngine.Object groupObj, StringBuilder sb, int index)
        {
            var groupSO = new SerializedObject(groupObj);
            string displayName = groupSO.FindProperty("DisplayName")?.stringValue ?? "(unnamed)";
            var orderProp = groupSO.FindProperty("Order");
            int count = orderProp != null ? orderProp.arraySize : 0;
            sb.AppendLine($"    [{index}] Group: \"{displayName}\" ({count} sub-items)");
        }

        private static void DumpDomainMenu(object menu, StringBuilder sb)
        {
            if (menu == null) { sb.AppendLine("  (null menu)"); return; }

            var menuType = menu.GetType();

            // DefaultSelection
            var defaultSelProp = menuType.GetProperty("DefaultSelection");
            if (defaultSelProp != null)
                sb.AppendLine($"  Default Selection: \"{defaultSelProp.GetValue(menu)}\"");

            // Registered
            var registeredProp = menuType.GetProperty("Registered");
            if (registeredProp != null)
            {
                var registered = registeredProp.GetValue(menu);
                DumpDomainMenuItemList(registered, sb, "Registered");
            }
        }

        private static void DumpDomainMenuItemList(object list, StringBuilder sb, string label)
        {
            if (list == null) { sb.AppendLine($"  {label}: null"); return; }

            var listType = list.GetType();
            var orderProp = listType.GetProperty("Order");
            if (orderProp == null) return;

            var order = orderProp.GetValue(list) as System.Collections.IList;
            if (order == null || order.Count == 0)
            {
                sb.AppendLine($"  {label}: (empty)");
                return;
            }

            sb.AppendLine($"  {label} ({order.Count} items):");

            var getTypeMeth = listType.GetMethod("GetType", new[] { typeof(string) });
            var getModeMeth = listType.GetMethod("GetMode", new[] { typeof(string) });
            var getGroupMeth = listType.GetMethod("GetGroup", new[] { typeof(string) });

            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i] as string;
                if (id == null) continue;

                // Determine type
                object itemType = null;
                try { itemType = getTypeMeth?.Invoke(list, new object[] { id }); }
                catch { }

                string typeStr = itemType?.ToString() ?? "?";

                if (typeStr == "Mode" && getModeMeth != null)
                {
                    try
                    {
                        var mode = getModeMeth.Invoke(list, new object[] { id });
                        DumpDomainMode(mode, sb, i);
                    }
                    catch { sb.AppendLine($"    [{i}] Mode (id={id}) — error reading"); }
                }
                else if (typeStr == "Group" && getGroupMeth != null)
                {
                    try
                    {
                        var group = getGroupMeth.Invoke(list, new object[] { id });
                        DumpDomainGroup(group, sb, i);
                    }
                    catch { sb.AppendLine($"    [{i}] Group (id={id}) — error reading"); }
                }
                else
                {
                    sb.AppendLine($"    [{i}] {typeStr} (id={id})");
                }
            }
        }

        private static void DumpDomainMode(object mode, StringBuilder sb, int index)
        {
            if (mode == null) return;
            var t = mode.GetType();

            string name = t.GetProperty("DisplayName")?.GetValue(mode) as string ?? "(unnamed)";
            bool changeDefault = (bool)(t.GetProperty("ChangeDefaultFace")?.GetValue(mode) ?? false);

            var animProp = t.GetProperty("Animation");
            string animName = "None";
            if (animProp != null)
            {
                var anim = animProp.GetValue(mode);
                if (anim != null)
                {
                    var guidProp = anim.GetType().GetProperty("GUID");
                    string guid = guidProp?.GetValue(anim) as string;
                    animName = GuidToAnimationName(guid);
                }
            }

            sb.AppendLine($"    [{index}] Mode: \"{name}\"" +
                          (changeDefault ? " [DEFAULT]" : "") +
                          $"  Animation={animName}");

            // Branches
            var branchesProp = t.GetProperty("Branches");
            if (branchesProp != null)
            {
                var branches = branchesProp.GetValue(mode) as System.Collections.IList;
                if (branches != null)
                {
                    for (int b = 0; b < branches.Count; b++)
                    {
                        DumpDomainBranch(branches[b], sb, b);
                    }
                }
            }
        }

        private static void DumpDomainBranch(object branch, StringBuilder sb, int branchIndex)
        {
            if (branch == null) return;
            var t = branch.GetType();

            // Conditions
            var condsProp = t.GetProperty("Conditions");
            string condStr = "";
            if (condsProp != null)
            {
                var conds = condsProp.GetValue(branch) as System.Collections.IList;
                if (conds != null && conds.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var cond in conds)
                    {
                        var ct = cond.GetType();
                        int hand = Convert.ToInt32(ct.GetProperty("Hand")?.GetValue(cond) ?? 0);
                        int gesture = Convert.ToInt32(ct.GetProperty("HandGesture")?.GetValue(cond) ?? 0);
                        string handStr = hand == 0 ? "L" : "R";
                        string gestureStr = GetGestureName(gesture);
                        parts.Add($"{handStr}={gestureStr}");
                    }
                    condStr = string.Join(", ", parts);
                }
            }

            // Animations
            string baseAnim = ReadDomainAnimGuid(branch, "BaseAnimation");

            sb.Append($"      Branch[{branchIndex}]");
            if (!string.IsNullOrEmpty(condStr)) sb.Append($" ({condStr})");
            sb.Append($": base={baseAnim}");
            sb.AppendLine();
        }

        private static void DumpDomainGroup(object group, StringBuilder sb, int index)
        {
            if (group == null) return;
            var t = group.GetType();
            string name = t.GetProperty("DisplayName")?.GetValue(group) as string ?? "(unnamed)";
            int count = Convert.ToInt32(t.GetProperty("Count")?.GetValue(group) ?? 0);
            sb.AppendLine($"    [{index}] Group: \"{name}\" ({count} sub-items)");
        }

        // ─── Property Resolution ───

        private struct ResolvedProperty
        {
            public SerializedObject so;
            public SerializedProperty prop;
            public UnityEngine.Object targetObject;
            public string error;
        }

        /// <summary>
        /// Resolves dot-notation paths like "AV3Setting.SmoothAnalogFist" by following
        /// ScriptableObject references automatically.
        /// </summary>
        private static ResolvedProperty ResolveNestedProperty(UnityEngine.Object comp, string propertyPath)
        {
            var so = new SerializedObject(comp);
            var parts = propertyPath.Split('.');

            // Try direct path first
            var directProp = so.FindProperty(propertyPath);
            if (directProp != null)
                return new ResolvedProperty { so = so, prop = directProp, targetObject = comp };

            // Walk the path, following ScriptableObject references
            UnityEngine.Object currentObj = comp;
            SerializedObject currentSO = so;

            for (int i = 0; i < parts.Length; i++)
            {
                var prop = currentSO.FindProperty(parts[i]);
                if (prop == null)
                {
                    // Try remaining path as a single property
                    string remaining = string.Join(".", parts, i, parts.Length - i);
                    prop = currentSO.FindProperty(remaining);
                    if (prop != null)
                        return new ResolvedProperty { so = currentSO, prop = prop, targetObject = currentObj };

                    return new ResolvedProperty { error = $"Error: Property '{parts[i]}' not found at depth {i} in path '{propertyPath}'." };
                }

                // If this is the last part, return it
                if (i == parts.Length - 1)
                    return new ResolvedProperty { so = currentSO, prop = prop, targetObject = currentObj };

                // If it's a ScriptableObject reference, follow it
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                {
                    currentObj = prop.objectReferenceValue;
                    currentSO = new SerializedObject(currentObj);
                }
                else
                {
                    // Not a reference — try remaining as nested path
                    string remaining = string.Join(".", parts, i + 1, parts.Length - i - 1);
                    var nestedProp = prop.FindPropertyRelative(remaining);
                    if (nestedProp != null)
                        return new ResolvedProperty { so = currentSO, prop = nestedProp, targetObject = currentObj };

                    return new ResolvedProperty { error = $"Error: Cannot navigate through '{parts[i]}' (type: {prop.propertyType})." };
                }
            }

            return new ResolvedProperty { error = $"Error: Could not resolve property path '{propertyPath}'." };
        }

        // ─── Value Helpers ───

        private static string ReadPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("G");
                case SerializedPropertyType.String: return $"\"{prop.stringValue}\"";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                        : "None";
                case SerializedPropertyType.Color:
                    return $"#{ColorUtility.ToHtmlStringRGBA(prop.colorValue)}";
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                case SerializedPropertyType.Generic:
                    return prop.isArray ? $"(Array[{prop.arraySize}])" : "(object)";
                default: return $"({prop.propertyType})";
            }
        }

        private static bool WritePropertyValue(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, out int iv)) { prop.intValue = iv; return true; }
                    return false;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out bool bv)) { prop.boolValue = bv; return true; }
                    if (value == "0") { prop.boolValue = false; return true; }
                    if (value == "1") { prop.boolValue = true; return true; }
                    return false;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, out float fv)) { prop.floatValue = fv; return true; }
                    return false;
                case SerializedPropertyType.String:
                    prop.stringValue = value; return true;
                case SerializedPropertyType.Enum:
                    if (prop.enumDisplayNames != null)
                    {
                        int idx = Array.IndexOf(prop.enumDisplayNames, value);
                        if (idx >= 0) { prop.enumValueIndex = idx; return true; }
                    }
                    if (int.TryParse(value, out int ei)) { prop.enumValueIndex = ei; return true; }
                    return false;
                case SerializedPropertyType.ObjectReference:
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                    if (obj != null) { prop.objectReferenceValue = obj; return true; }
                    string path = AssetDatabase.GUIDToAssetPath(value);
                    if (!string.IsNullOrEmpty(path))
                    {
                        obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (obj != null) { prop.objectReferenceValue = obj; return true; }
                    }
                    return false;
                default: return false;
            }
        }

        // ─── Animation & Gesture Helpers ───

        private static string ReadAnimationGuid(SerializedObject so, string fieldName)
        {
            var animProp = so.FindProperty(fieldName);
            if (animProp == null) return "None";

            var guidProp = animProp.FindPropertyRelative("GUID")
                        ?? animProp.FindPropertyRelative("_guid");
            if (guidProp == null) return "None";

            return GuidToAnimationName(guidProp.stringValue);
        }

        private static string ReadDomainAnimGuid(object obj, string propName)
        {
            var prop = obj.GetType().GetProperty(propName);
            if (prop == null) return "None";
            var anim = prop.GetValue(obj);
            if (anim == null) return "None";
            var guidProp = anim.GetType().GetProperty("GUID");
            string guid = guidProp?.GetValue(anim) as string;
            return GuidToAnimationName(guid);
        }

        private static string GuidToAnimationName(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return "None";
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return $"(GUID:{guid.Substring(0, Mathf.Min(8, guid.Length))})";
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        private static readonly string[] GestureNames =
        {
            "Neutral", "Fist", "HandOpen", "FingerPoint",
            "Victory", "RockNRoll", "HandGun", "ThumbsUp"
        };

        private static string GetGestureName(int index)
        {
            return index >= 0 && index < GestureNames.Length ? GestureNames[index] : index.ToString();
        }

        private static string GetComparisonOp(int index)
        {
            switch (index)
            {
                case 0: return ">=";
                case 1: return ">";
                case 2: return "=";
                case 3: return "<";
                case 4: return "<=";
                default: return "?";
            }
        }
    }
}
