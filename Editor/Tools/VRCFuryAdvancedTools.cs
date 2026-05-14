#if VRCFURY
using com.vrcfury.api;
using com.vrcfury.api.Components;
using com.vrcfury.api.Actions;
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
    public static class VRCFuryAdvancedTools
    {
#if VRCFURY

        // ========== Helpers ==========

        private const string VrcFuryTypeName = "VF.Model.VRCFury";

        private static Type FindVRCFuryType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(VrcFuryTypeName);
                if (type != null) return type;
            }
            return null;
        }

        private static Component[] FindVRCFuryComponents(GameObject root)
        {
            var vrcFuryType = FindVRCFuryType();
            if (vrcFuryType == null) return new Component[0];
            return root.GetComponentsInChildren(vrcFuryType, true);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string GetContentTypeName(SerializedObject so)
        {
            var contentProp = so.FindProperty("content");
            if (contentProp == null) return "(no content)";
            var managedRef = contentProp.managedReferenceFullTypename;
            if (string.IsNullOrEmpty(managedRef)) return "(null)";
            // Format: "assembly TypeNamespace.TypeName"
            var parts = managedRef.Split(' ');
            if (parts.Length >= 2)
            {
                var fullType = parts[parts.Length - 1];
                var dotIdx = fullType.LastIndexOf('.');
                return dotIdx >= 0 ? fullType.Substring(dotIdx + 1) : fullType;
            }
            return managedRef;
        }

        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return null;
            return go;
        }

        // ========== 1. ListVRCFuryComponents ==========

        [AgentTool("List all VRCFury components on an avatar. Shows feature types and target objects.")]
        public static string ListVRCFuryComponents(string avatarRootName)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = FindVRCFuryComponents(avatarRoot);
            if (components.Length == 0)
                return $"No VRCFury components found on '{avatarRootName}' or its children.";

            var sb = new StringBuilder();
            sb.AppendLine($"VRCFury components on '{avatarRootName}' ({components.Length} total):");
            sb.AppendLine();

            for (int i = 0; i < components.Length; i++)
            {
                var comp = components[i];
                var goPath = GetRelativePath(avatarRoot.transform, comp.transform);
                var so = new SerializedObject(comp);
                var featureType = GetContentTypeName(so);

                sb.AppendLine($"  [{i}] {goPath} — {featureType}");

                // Show key info based on feature type
                var contentProp = so.FindProperty("content");
                if (contentProp != null)
                {
                    switch (featureType)
                    {
                        case "Toggle":
                            var menuPath = contentProp.FindPropertyRelative("name");
                            var saved = contentProp.FindPropertyRelative("saved");
                            var slider = contentProp.FindPropertyRelative("slider");
                            if (menuPath != null)
                                sb.AppendLine($"        menuPath: {menuPath.stringValue}");
                            if (saved != null)
                                sb.AppendLine($"        saved: {saved.boolValue}");
                            if (slider != null)
                                sb.AppendLine($"        slider: {slider.boolValue}");
                            break;

                        case "ArmatureLink":
                            var linkMode = contentProp.FindPropertyRelative("linkMode");
                            if (linkMode != null)
                                sb.AppendLine($"        linkMode: {linkMode.enumDisplayNames[linkMode.enumValueIndex]}");
                            break;

                        case "FullController":
                            var controllers = contentProp.FindPropertyRelative("controllers");
                            if (controllers != null && controllers.isArray)
                                sb.AppendLine($"        controllers: {controllers.arraySize}");
                            break;
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 2. InspectVRCFuryComponent ==========

        [AgentTool("Inspect a VRCFury component by index. Use ListVRCFuryComponents first to find the index.")]
        public static string InspectVRCFuryComponent(string avatarRootName, int componentIndex)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = FindVRCFuryComponents(avatarRoot);
            if (componentIndex < 0 || componentIndex >= components.Length)
                return $"Error: Index {componentIndex} out of range (0-{components.Length - 1}). Use ListVRCFuryComponents to see available components.";

            var comp = components[componentIndex];
            var so = new SerializedObject(comp);
            var featureType = GetContentTypeName(so);
            var goPath = GetRelativePath(avatarRoot.transform, comp.transform);

            var sb = new StringBuilder();
            sb.AppendLine($"VRCFury [{componentIndex}] on '{goPath}' — {featureType}");
            sb.AppendLine();

            var contentProp = so.FindProperty("content");
            if (contentProp != null)
            {
                DumpSerializedProperty(sb, contentProp, "  ", 0);
            }
            else
            {
                sb.AppendLine("  (no content property found)");
            }

            return sb.ToString().TrimEnd();
        }

        private static void DumpSerializedProperty(StringBuilder sb, SerializedProperty prop, string indent, int depth)
        {
            if (depth > 6) return; // prevent infinite recursion

            var iter = prop.Copy();
            var end = prop.GetEndProperty();
            bool enterChildren = true;

            while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, end))
            {
                enterChildren = false;
                string currentIndent = indent + new string(' ', depth * 2);

                switch (iter.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        sb.AppendLine($"{currentIndent}{iter.name}: {iter.intValue}");
                        break;
                    case SerializedPropertyType.Boolean:
                        sb.AppendLine($"{currentIndent}{iter.name}: {iter.boolValue}");
                        break;
                    case SerializedPropertyType.Float:
                        sb.AppendLine($"{currentIndent}{iter.name}: {iter.floatValue}");
                        break;
                    case SerializedPropertyType.String:
                        sb.AppendLine($"{currentIndent}{iter.name}: \"{iter.stringValue}\"");
                        break;
                    case SerializedPropertyType.Enum:
                        if (iter.enumDisplayNames != null && iter.enumValueIndex >= 0 && iter.enumValueIndex < iter.enumDisplayNames.Length)
                            sb.AppendLine($"{currentIndent}{iter.name}: {iter.enumDisplayNames[iter.enumValueIndex]}");
                        else
                            sb.AppendLine($"{currentIndent}{iter.name}: (enum {iter.enumValueIndex})");
                        break;
                    case SerializedPropertyType.ObjectReference:
                        var objRef = iter.objectReferenceValue;
                        sb.AppendLine($"{currentIndent}{iter.name}: {(objRef != null ? $"{objRef.name} ({objRef.GetType().Name})" : "(null)")}");
                        break;
                    case SerializedPropertyType.Vector3:
                        sb.AppendLine($"{currentIndent}{iter.name}: {iter.vector3Value}");
                        break;
                    case SerializedPropertyType.ManagedReference:
                        var refType = iter.managedReferenceFullTypename;
                        if (!string.IsNullOrEmpty(refType))
                        {
                            var parts = refType.Split(' ');
                            var typeName = parts.Length >= 2 ? parts[parts.Length - 1] : refType;
                            var dotIdx = typeName.LastIndexOf('.');
                            typeName = dotIdx >= 0 ? typeName.Substring(dotIdx + 1) : typeName;
                            sb.AppendLine($"{currentIndent}{iter.name}: [{typeName}]");
                        }
                        else
                        {
                            sb.AppendLine($"{currentIndent}{iter.name}: [null ref]");
                        }
                        DumpSerializedProperty(sb, iter, indent, depth + 1);
                        break;
                    default:
                        if (iter.isArray)
                        {
                            sb.AppendLine($"{currentIndent}{iter.name}: (array [{iter.arraySize}])");
                            for (int i = 0; i < Mathf.Min(iter.arraySize, 20); i++)
                            {
                                var elem = iter.GetArrayElementAtIndex(i);
                                sb.AppendLine($"{currentIndent}  [{i}]:");
                                DumpSerializedProperty(sb, elem, indent, depth + 2);
                            }
                            if (iter.arraySize > 20)
                                sb.AppendLine($"{currentIndent}  ... ({iter.arraySize - 20} more)");
                        }
                        else if (iter.hasVisibleChildren)
                        {
                            sb.AppendLine($"{currentIndent}{iter.name}:");
                            DumpSerializedProperty(sb, iter, indent, depth + 1);
                        }
                        break;
                }
            }
        }

        // ========== 3. CreateObjectToggle ==========

        [AgentTool("Create a VRCFury toggle for showing/hiding GameObjects. menuPath: 'Category/Toggle Name'. objects: semicolon-separated paths relative to avatar root. saved: persist across worlds.")]
        public static string CreateObjectToggle(string avatarRootName, string menuPath,
            string objects, string saved = "true", string defaultOn = "false",
            string exclusiveTag = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var objectPaths = objects.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (objectPaths.Length == 0)
                return "Error: No object paths specified. Use semicolon-separated paths relative to avatar root.";

            // Validate all objects exist
            var targetObjects = new List<GameObject>();
            foreach (var path in objectPaths)
            {
                var target = avatarRoot.transform.Find(path);
                if (target == null)
                    return $"Error: Object '{path}' not found under '{avatarRootName}'.";
                targetObjects.Add(target.gameObject);
            }

            bool isSaved = ToolUtility.ParseBool(saved);
            bool isDefaultOn = ToolUtility.ParseBool(defaultOn);

            if (!AgentSettings.RequestConfirmation(
                "VRCFury オブジェクトトグルの作成",
                $"メニューパス: {menuPath}\n" +
                $"対象オブジェクト: {string.Join(", ", objectPaths)}\n" +
                $"保存: {isSaved}, デフォルトON: {isDefaultOn}" +
                (string.IsNullOrEmpty(exclusiveTag) ? "" : $"\n排他タグ: {exclusiveTag}")))
                return "Cancelled: User denied the operation.";

            // Create toggle on the first target's parent or avatar root
            var holderName = $"VRCFury_{menuPath.Replace("/", "_")}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create VRCFury Toggle");
            holder.transform.SetParent(avatarRoot.transform, false);

            var toggle = FuryComponents.CreateToggle(holder);
            toggle.SetMenuPath(menuPath);

            if (isSaved) toggle.SetSaved();
            if (isDefaultOn) toggle.SetDefaultOn();
            if (!string.IsNullOrEmpty(exclusiveTag)) toggle.AddExclusiveTag(exclusiveTag);

            var actions = toggle.GetActions();
            foreach (var targetObj in targetObjects)
            {
                actions.AddTurnOn(targetObj);
            }

            EditorUtility.SetDirty(holder);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created VRCFury object toggle.");
            sb.AppendLine($"  Menu path: {menuPath}");
            sb.AppendLine($"  Objects ({targetObjects.Count}):");
            foreach (var obj in targetObjects)
                sb.AppendLine($"    - {GetRelativePath(avatarRoot.transform, obj.transform)}");
            sb.AppendLine($"  Saved: {isSaved}, Default ON: {isDefaultOn}");
            if (!string.IsNullOrEmpty(exclusiveTag))
                sb.AppendLine($"  Exclusive tag: {exclusiveTag}");

            return sb.ToString().TrimEnd();
        }

        // ========== 4. CreateBlendShapeToggle ==========

        [AgentTool("Create a VRCFury toggle that drives blend shapes. menuPath: menu location. blendShapes: 'name=value;name2=value2'. slider: use radial slider instead of on/off.")]
        public static string CreateBlendShapeToggle(string avatarRootName, string menuPath,
            string blendShapes, string saved = "true", string slider = "false",
            string defaultOn = "false")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            // Parse blendShapes: "name=value;name2=value2"
            var bsPairs = new List<(string name, float value)>();
            foreach (var entry in blendShapes.Split(';'))
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split('=');
                if (parts.Length != 2)
                    return $"Error: Invalid blend shape entry '{trimmed}'. Expected 'name=value'.";
                if (!float.TryParse(parts[1].Trim(), out float value))
                    return $"Error: Invalid value '{parts[1].Trim()}' for blend shape '{parts[0].Trim()}'.";
                bsPairs.Add((parts[0].Trim(), value));
            }

            if (bsPairs.Count == 0)
                return "Error: No blend shapes specified.";

            bool isSaved = ToolUtility.ParseBool(saved);
            bool isSlider = ToolUtility.ParseBool(slider);
            bool isDefaultOn = ToolUtility.ParseBool(defaultOn);

            if (!AgentSettings.RequestConfirmation(
                "VRCFury BlendShapeトグルの作成",
                $"メニューパス: {menuPath}\n" +
                $"BlendShapes: {string.Join(", ", bsPairs.Select(p => $"{p.name}={p.value}"))}\n" +
                $"スライダー: {isSlider}, 保存: {isSaved}"))
                return "Cancelled: User denied the operation.";

            var holderName = $"VRCFury_{menuPath.Replace("/", "_")}";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create VRCFury BlendShape Toggle");
            holder.transform.SetParent(avatarRoot.transform, false);

            var toggle = FuryComponents.CreateToggle(holder);
            toggle.SetMenuPath(menuPath);
            if (isSaved) toggle.SetSaved();
            if (isSlider) toggle.SetSlider();
            if (isDefaultOn) toggle.SetDefaultOn();

            var actions = toggle.GetActions();
            foreach (var (name, value) in bsPairs)
            {
                actions.AddBlendshape(name, value);
            }

            EditorUtility.SetDirty(holder);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created VRCFury blend shape toggle.");
            sb.AppendLine($"  Menu path: {menuPath}");
            sb.AppendLine($"  Mode: {(isSlider ? "Slider (radial)" : "Toggle (on/off)")}");
            sb.AppendLine($"  Blend shapes:");
            foreach (var (name, value) in bsPairs)
                sb.AppendLine($"    - {name} = {value}");

            return sb.ToString().TrimEnd();
        }

        // ========== 5. CreateArmatureLink ==========

        [AgentTool("Create a VRCFury ArmatureLink to merge an outfit's armature into the avatar. outfitObjectName: root of outfit armature.")]
        public static string CreateArmatureLink(string outfitObjectName, string targetBone = "Hips",
            string recursive = "true", string align = "false")
        {
            var outfitObj = FindGO(outfitObjectName);
            if (outfitObj == null)
                return $"Error: GameObject '{outfitObjectName}' not found.";

            bool isRecursive = ToolUtility.ParseBool(recursive);
            bool isAlign = ToolUtility.ParseBool(align);

            HumanBodyBones bone;
            if (!Enum.TryParse(targetBone, true, out bone))
                return $"Error: Invalid bone name '{targetBone}'. Use HumanBodyBones enum values (e.g., Hips, Spine, Chest, Head).";

            if (!AgentSettings.RequestConfirmation(
                "VRCFury ArmatureLinkの作成",
                $"オブジェクト: {outfitObjectName}\n" +
                $"ターゲットボーン: {targetBone}\n" +
                $"再帰: {isRecursive}, 整列: {isAlign}"))
                return "Cancelled: User denied the operation.";

            var armatureLink = FuryComponents.CreateArmatureLink(outfitObj);
            armatureLink.LinkFrom(outfitObj);
            armatureLink.LinkTo(bone);
            armatureLink.SetRecursive(isRecursive);
            armatureLink.SetAlign(isAlign);

            EditorUtility.SetDirty(outfitObj);

            return $"Success: Created VRCFury ArmatureLink on '{outfitObjectName}'.\n" +
                   $"  Target bone: {targetBone}\n" +
                   $"  Recursive: {isRecursive}, Align: {isAlign}";
        }

        // ========== 6. CreateFullController ==========

        [AgentTool("Add an existing AnimatorController/Menu/Params to avatar via VRCFury FullController. controllerPath: asset path.")]
        public static string CreateFullController(string avatarRootName, string controllerPath = "",
            string menuPath = "", string paramsPath = "", string layerType = "FX")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            if (string.IsNullOrEmpty(controllerPath) && string.IsNullOrEmpty(menuPath) && string.IsNullOrEmpty(paramsPath))
                return "Error: At least one of controllerPath, menuPath, or paramsPath must be specified.";

            if (!AgentSettings.RequestConfirmation(
                "VRCFury FullControllerの作成",
                $"アバター: {avatarRootName}\n" +
                (string.IsNullOrEmpty(controllerPath) ? "" : $"コントローラー: {controllerPath}\n") +
                (string.IsNullOrEmpty(menuPath) ? "" : $"メニュー: {menuPath}\n") +
                (string.IsNullOrEmpty(paramsPath) ? "" : $"パラメータ: {paramsPath}\n") +
                $"レイヤータイプ: {layerType}"))
                return "Cancelled: User denied the operation.";

            var holderName = "VRCFury_FullController";
            var holder = new GameObject(holderName);
            Undo.RegisterCreatedObjectUndo(holder, "Create VRCFury FullController");
            holder.transform.SetParent(avatarRoot.transform, false);

            var fullController = FuryComponents.CreateFullController(holder);

            var body = new StringBuilder();
            bool anyFailure = false;

            if (!string.IsNullOrEmpty(controllerPath))
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                if (controller == null)
                {
                    body.AppendLine($"  Warning: Controller not found at '{controllerPath}'.");
                    anyFailure = true;
                }
                else
                {
                    // Parse layer type and attach via reflection (VRCFury API is internal).
                    // Enum.Parse and Invoke can throw on bad input / API drift — guarded so
                    // the failure surfaces as an honest Warning rather than an exception.
                    bool attached = false;
                    string failReason = "VRCFury/VRChat SDK reflection API not found (incompatible version?)";
                    try
                    {
                        var vrcDescType = VRChatTools.FindVrcType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                        var animLayerType = vrcDescType?.Assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor+AnimLayerType");
                        if (animLayerType != null)
                        {
                            var layerEnum = Enum.Parse(animLayerType, layerType, true);
                            var addMethod = fullController.GetType().GetMethod("AddController");
                            if (addMethod != null)
                            {
                                addMethod.Invoke(fullController, new object[] { controller, layerEnum });
                                attached = true;
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        failReason = $"invalid layerType '{layerType}' (expected e.g. Base/Additive/Gesture/Action/FX)";
                    }
                    catch (Exception ex)
                    {
                        failReason = $"reflection call failed: {(ex.InnerException ?? ex).Message}";
                    }
                    if (attached)
                        body.AppendLine($"  Controller: {controller.name} (type={layerType})");
                    else
                    {
                        body.AppendLine($"  Warning: Could not attach controller '{controller.name}' — {failReason}.");
                        anyFailure = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(menuPath))
            {
                var menuType = VRChatTools.FindVrcType("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var menu = menuType != null ? AssetDatabase.LoadAssetAtPath(menuPath, menuType) : null;
                if (menu == null)
                {
                    body.AppendLine($"  Warning: Menu not found at '{menuPath}'.");
                    anyFailure = true;
                }
                else
                {
                    bool attached = false;
                    string failReason = "VRCFury AddMenu reflection API not found (incompatible version?)";
                    var addMenuMethod = fullController.GetType().GetMethod("AddMenu");
                    if (addMenuMethod != null)
                    {
                        try
                        {
                            addMenuMethod.Invoke(fullController, new object[] { menu, "" });
                            attached = true;
                        }
                        catch (Exception ex)
                        {
                            failReason = $"AddMenu invoke failed: {(ex.InnerException ?? ex).Message}";
                        }
                    }
                    if (attached)
                        body.AppendLine($"  Menu: {menu.name}");
                    else
                    {
                        body.AppendLine($"  Warning: Could not attach menu '{menu.name}' — {failReason}.");
                        anyFailure = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(paramsPath))
            {
                var paramsType = VRChatTools.FindVrcType("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
                var prms = paramsType != null ? AssetDatabase.LoadAssetAtPath(paramsPath, paramsType) : null;
                if (prms == null)
                {
                    body.AppendLine($"  Warning: Params not found at '{paramsPath}'.");
                    anyFailure = true;
                }
                else
                {
                    bool attached = false;
                    string failReason = "VRCFury AddParams reflection API not found (incompatible version?)";
                    var addParamsMethod = fullController.GetType().GetMethod("AddParams");
                    if (addParamsMethod != null)
                    {
                        try
                        {
                            addParamsMethod.Invoke(fullController, new object[] { prms });
                            attached = true;
                        }
                        catch (Exception ex)
                        {
                            failReason = $"AddParams invoke failed: {(ex.InnerException ?? ex).Message}";
                        }
                    }
                    if (attached)
                        body.AppendLine($"  Params: {prms.name}");
                    else
                    {
                        body.AppendLine($"  Warning: Could not attach params '{prms.name}' — {failReason}.");
                        anyFailure = true;
                    }
                }
            }

            EditorUtility.SetDirty(holder);

            var sb = new StringBuilder();
            sb.AppendLine(anyFailure
                ? $"Partial: Created VRCFury FullController on '{avatarRootName}', but some assets could not be attached."
                : $"Success: Created VRCFury FullController on '{avatarRootName}'.");
            sb.Append(body.ToString());
            return sb.ToString().TrimEnd();
        }

        // ========== 7. CreateBlendShapeLink ==========

        [AgentTool(@"Add a VRCFury BlendShapeLink component to a mesh and configure its includeAll/excludes options (BlendShapeLink is internal, configured via reflection).
LIMITATION: link source meshes are NOT auto-populated by this tool — after creation, open the VRCFury Inspector on the component and add the source mesh(es) manually. Without link sources the component has no effect.")]
        public static string CreateBlendShapeLink(string avatarRootName, string linkedMeshName,
            string includeAll = "true", string excludeList = "")
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var linkedMeshObj = FindGO(linkedMeshName);
            if (linkedMeshObj == null)
                return $"Error: GameObject '{linkedMeshName}' not found.";

            var smr = linkedMeshObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                return $"Error: '{linkedMeshName}' does not have a SkinnedMeshRenderer.";

            bool isIncludeAll = ToolUtility.ParseBool(includeAll);

            if (!AgentSettings.RequestConfirmation(
                "VRCFury BlendShapeLinkの作成",
                $"リンクメッシュ: {linkedMeshName}\n" +
                $"全BlendShape含む: {isIncludeAll}" +
                (string.IsNullOrEmpty(excludeList) ? "" : $"\n除外: {excludeList}")))
                return "Cancelled: User denied the operation.";

            // BlendShapeLink is internal, so we need to use reflection
            // Find the BlendShapeLink type
            Type blendShapeLinkType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                blendShapeLinkType = assembly.GetType("VF.Model.Feature.BlendShapeLink");
                if (blendShapeLinkType != null) break;
            }

            if (blendShapeLinkType == null)
                return "Error: BlendShapeLink type not found. VRCFury may be an incompatible version.";

            // Create VRCFury component manually via reflection
            var vrcFuryType = FindVRCFuryType();
            if (vrcFuryType == null)
                return "Error: VRCFury component type not found.";

            var comp = Undo.AddComponent(linkedMeshObj, vrcFuryType);
            var so = new SerializedObject(comp);

            // Create the BlendShapeLink content
            var contentProp = so.FindProperty("content");
            if (contentProp == null)
            {
                // Without the 'content' property we cannot configure anything — roll back
                // the component we just added rather than reporting a hollow success.
                Undo.DestroyObjectImmediate(comp);
                return "Error: VRCFury 'content' property not found. VRCFury may be an incompatible version.";
            }

            var bsLink = Activator.CreateInstance(blendShapeLinkType);

            // Set includeAll
            var includeAllField = blendShapeLinkType.GetField("includeAll");
            bool includeAllSet = includeAllField != null;
            if (includeAllSet)
                includeAllField.SetValue(bsLink, isIncludeAll ? 1 : 0); // enum: Skin=0, All=1

            // Set excludes
            bool excludesApplied = true;
            if (!string.IsNullOrEmpty(excludeList))
            {
                var excludesField = blendShapeLinkType.GetField("excludes");
                if (excludesField != null)
                {
                    var excludeNames = excludeList.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    excludesField.SetValue(bsLink, excludeNames);
                }
                else
                {
                    excludesApplied = false;
                }
            }

            contentProp.managedReferenceValue = bsLink;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Created VRCFury BlendShapeLink on '{linkedMeshName}'.");
            if (includeAllSet)
                sb.AppendLine($"  Include all: {isIncludeAll}");
            else
                sb.AppendLine($"  Warning: 'includeAll' field not found — VRCFury default used instead.");
            if (!string.IsNullOrEmpty(excludeList))
            {
                if (excludesApplied)
                    sb.AppendLine($"  Excludes: {excludeList}");
                else
                    sb.AppendLine($"  Warning: 'excludes' field not found — exclude list was NOT applied.");
            }
            sb.AppendLine("  Note: link source mesh(es) are NOT set by this tool — add them in the VRCFury Inspector, otherwise the component has no effect.");

            return sb.ToString().TrimEnd();
        }

        // ========== 8. RemoveVRCFuryComponent ==========

        [AgentTool("Remove a VRCFury component by index. Use ListVRCFuryComponents to find the index.")]
        public static string RemoveVRCFuryComponent(string avatarRootName, int componentIndex)
        {
            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = FindVRCFuryComponents(avatarRoot);
            if (componentIndex < 0 || componentIndex >= components.Length)
                return $"Error: Index {componentIndex} out of range (0-{components.Length - 1}).";

            var comp = components[componentIndex];
            var so = new SerializedObject(comp);
            var featureType = GetContentTypeName(so);
            var goPath = GetRelativePath(avatarRoot.transform, comp.transform);

            if (!AgentSettings.RequestConfirmation(
                "VRCFury コンポーネントの削除",
                $"[{componentIndex}] {goPath} — {featureType} を削除します。"))
                return "Cancelled: User denied the operation.";

            // If the holder GameObject was created just for this VRCFury component, remove it too
            var holder = comp.gameObject;
            var otherComponents = holder.GetComponents<Component>();
            bool hasOnlyTransformAndThis = otherComponents.Length == 2; // Transform + VRCFury

            Undo.DestroyObjectImmediate(comp);

            if (hasOnlyTransformAndThis && holder.transform.childCount == 0 &&
                holder.name.StartsWith("VRCFury_"))
            {
                Undo.DestroyObjectImmediate(holder);
                return $"Success: Removed VRCFury {featureType} from '{goPath}' and cleaned up empty holder.";
            }

            return $"Success: Removed VRCFury {featureType} [{componentIndex}] from '{goPath}'.";
        }

#endif
    }
}
