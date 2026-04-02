using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
using MASetupOutfit = nadena.dev.modular_avatar.core.editor.SetupOutfit;
#endif

namespace AjisaiFlow.UnityAgent.Editor.MA
{
    /// <summary>
    /// Factory for creating and managing Modular Avatar components.
    /// All methods handle Undo registration and EditorUtility.SetDirty internally.
    /// </summary>
    internal static class MAComponentFactory
    {
#if MODULAR_AVATAR

        // ========== MergeArmature ==========

        public static Component AddMergeArmature(GameObject target, Transform mergeTarget,
            string prefix = "", string suffix = "")
        {
            var comp = Undo.AddComponent<ModularAvatarMergeArmature>(target);
            var targetRef = new AvatarObjectReference();
            targetRef.Set(mergeTarget.gameObject);
            comp.mergeTarget = targetRef;
            comp.prefix = prefix;
            comp.suffix = suffix;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        // ========== BoneProxy ==========

        public static Component AddBoneProxy(GameObject target, Transform bone,
            BoneProxyAttachmentMode mode = BoneProxyAttachmentMode.AsChildAtRoot)
        {
            var comp = target.GetComponent<ModularAvatarBoneProxy>();
            if (comp == null)
                comp = Undo.AddComponent<ModularAvatarBoneProxy>(target);
            else
                Undo.RecordObject(comp, "Update MA BoneProxy");
            comp.target = bone;
            comp.attachmentMode = mode;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        /// <summary>
        /// Get the BoneProxy target bone from a GameObject, or null if not present.
        /// </summary>
        public static Transform GetBoneProxyTarget(GameObject go)
        {
            var bp = go.GetComponent<ModularAvatarBoneProxy>();
            return bp != null ? bp.target : null;
        }

        /// <summary>
        /// Check if a GameObject has a BoneProxy component.
        /// </summary>
        public static bool HasBoneProxy(GameObject go)
        {
            return go.GetComponent<ModularAvatarBoneProxy>() != null;
        }

        /// <summary>
        /// Get BoneProxy info for display purposes. Returns (targetName, targetPath, attachmentMode) or null.
        /// </summary>
        public static (string targetName, string targetPath, string mode)? GetBoneProxyInfo(GameObject go)
        {
            var bp = go.GetComponent<ModularAvatarBoneProxy>();
            if (bp == null) return null;
            string targetName = bp.target != null ? bp.target.name : "(null)";
            string targetPath = BuildBoneProxyTargetPath(bp);
            string mode = bp.attachmentMode.ToString();
            return (targetName, targetPath, mode);
        }

        private static string BuildBoneProxyTargetPath(ModularAvatarBoneProxy boneProxy)
        {
            if (boneProxy.target != null)
            {
                var parts = new List<string>();
                var t = boneProxy.target;
                // Walk up to avatar root
                Transform avatarRoot = boneProxy.transform;
                while (avatarRoot != null)
                {
                    if (avatarRoot.GetComponent("VRCAvatarDescriptor") != null)
                        break;
                    avatarRoot = avatarRoot.parent;
                }
                while (t != null && t != avatarRoot)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }
                parts.Reverse();
                return parts.Count > 0 ? string.Join("/", parts) : boneProxy.target.name;
            }

            if (boneProxy.boneReference != HumanBodyBones.LastBone)
            {
                string path = boneProxy.boneReference.ToString();
                if (!string.IsNullOrEmpty(boneProxy.subPath))
                    path += "/" + boneProxy.subPath;
                return path;
            }

            return boneProxy.subPath ?? "(unknown)";
        }

        // ========== MergeAnimator ==========

        public static Component AddMergeAnimator(GameObject target,
            RuntimeAnimatorController controller,
            int pathMode = 0, bool matchAvatarWriteDefaults = true,
            bool deleteAttachedAnimator = true)
        {
            var comp = Undo.AddComponent<ModularAvatarMergeAnimator>(target);
            comp.animator = controller;
            comp.pathMode = (MergeAnimatorPathMode)pathMode;
            comp.matchAvatarWriteDefaults = matchAvatarWriteDefaults;
            comp.deleteAttachedAnimator = deleteAttachedAnimator;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        // ========== MenuInstaller ==========

        public static Component AddMenuInstaller(GameObject target)
        {
            var comp = Undo.AddComponent<ModularAvatarMenuInstaller>(target);
            EditorUtility.SetDirty(comp);
            return comp;
        }

        // ========== MenuItem ==========

        public static Component AddMenuItemToggle(GameObject target, string paramName = null,
            float value = 1f, bool synced = true, bool saved = true, bool isDefault = false,
            Texture2D icon = null)
        {
            var comp = Undo.AddComponent<ModularAvatarMenuItem>(target);
            comp.PortableControl.Type = PortableControlType.Toggle;
            comp.PortableControl.Value = value;
            if (!string.IsNullOrEmpty(paramName))
                comp.PortableControl.Parameter = paramName;
            comp.isSynced = synced;
            comp.isSaved = saved;
            comp.isDefault = isDefault;
            comp.automaticValue = false;
            if (icon != null)
                comp.PortableControl.Icon = icon;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        public static Component AddMenuItemSubMenu(GameObject target, Texture2D icon = null)
        {
            var comp = Undo.AddComponent<ModularAvatarMenuItem>(target);
            comp.PortableControl.Type = PortableControlType.SubMenu;
            if (icon != null)
                comp.PortableControl.Icon = icon;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        public static Component AddMenuItemRadial(GameObject target, string paramName,
            Texture2D icon = null)
        {
            var comp = Undo.AddComponent<ModularAvatarMenuItem>(target);
            comp.PortableControl.Type = PortableControlType.RadialPuppet;
            comp.PortableControl.Parameter = paramName;
            if (icon != null)
                comp.PortableControl.Icon = icon;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        public static Component AddMenuItemButton(GameObject target, string paramName = null,
            float value = 1f, Texture2D icon = null)
        {
            var comp = Undo.AddComponent<ModularAvatarMenuItem>(target);
            comp.PortableControl.Type = PortableControlType.Button;
            comp.PortableControl.Value = value;
            if (!string.IsNullOrEmpty(paramName))
                comp.PortableControl.Parameter = paramName;
            if (icon != null)
                comp.PortableControl.Icon = icon;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        /// <summary>
        /// Add a MenuItem with configurable type. Returns the component or null if type is invalid.
        /// </summary>
        public static Component AddMenuItem(GameObject target, string type,
            string paramName = null, float value = 1f,
            bool synced = true, bool saved = true, bool isDefault = false)
        {
            if (!TryParseControlType(type, out var controlType))
                return null;

            var existing = target.GetComponent<ModularAvatarMenuItem>();
            if (existing != null)
                return null;

            var comp = Undo.AddComponent<ModularAvatarMenuItem>(target);
            comp.PortableControl.Type = controlType;
            comp.PortableControl.Value = value;
            if (!string.IsNullOrEmpty(paramName))
                comp.PortableControl.Parameter = paramName;
            comp.isSynced = synced;
            comp.isSaved = saved;
            comp.isDefault = isDefault;
            comp.automaticValue = false;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        /// <summary>
        /// Check if a MenuItem already exists on the target.
        /// </summary>
        public static bool HasMenuItem(GameObject target)
        {
            return target.GetComponent<ModularAvatarMenuItem>() != null;
        }

        /// <summary>
        /// Get MenuItem info for display. Returns (type, param, value, isDefault, synced, saved) or null.
        /// </summary>
        public static (string type, string param, float value, bool isDefault, bool synced, bool saved)?
            GetMenuItemInfo(GameObject go)
        {
            var mi = go.GetComponent<ModularAvatarMenuItem>();
            if (mi == null) return null;
            return (
                mi.PortableControl.Type.ToString(),
                mi.PortableControl.Parameter,
                mi.PortableControl.Value,
                mi.isDefault,
                mi.isSynced,
                mi.isSaved
            );
        }

        // ========== Parameters ==========

        public static Component AddOrGetParameters(GameObject target)
        {
            var comp = target.GetComponent<ModularAvatarParameters>();
            if (comp == null)
                comp = Undo.AddComponent<ModularAvatarParameters>(target);
            return comp;
        }

        // ========== BlendshapeSync ==========

        public static Component AddOrGetBlendshapeSync(GameObject target)
        {
            var comp = target.GetComponent<ModularAvatarBlendshapeSync>();
            if (comp == null)
                comp = Undo.AddComponent<ModularAvatarBlendshapeSync>(target);
            return comp;
        }

        /// <summary>
        /// Add a blendshape binding to an existing BlendshapeSync component.
        /// </summary>
        public static void AddBlendshapeBinding(Component blendshapeSync,
            string referenceMeshPath, string blendshapeName, string localBlendshape = "")
        {
            var sync = (ModularAvatarBlendshapeSync)blendshapeSync;
            Undo.RecordObject(sync, "Add Blendshape Binding");
            sync.Bindings.Add(new BlendshapeBinding
            {
                ReferenceMesh = new AvatarObjectReference { referencePath = referenceMeshPath },
                Blendshape = blendshapeName,
                LocalBlendshape = localBlendshape
            });
            EditorUtility.SetDirty(sync);
        }

        /// <summary>
        /// Get existing blendshape binding names from a BlendshapeSync component.
        /// </summary>
        public static HashSet<string> GetExistingBindingNames(Component blendshapeSync)
        {
            var sync = (ModularAvatarBlendshapeSync)blendshapeSync;
            var names = new HashSet<string>();
            if (sync.Bindings != null)
                foreach (var b in sync.Bindings)
                    names.Add(b.Blendshape);
            return names;
        }

        // ========== ObjectToggle ==========

        public static Component AddObjectToggle(GameObject target, List<(GameObject obj, bool active)> objects)
        {
            var comp = Undo.AddComponent<ModularAvatarObjectToggle>(target);
            var toggledObjects = new List<ToggledObject>();
            foreach (var (obj, active) in objects)
            {
                var targetRef = new AvatarObjectReference();
                targetRef.Set(obj);
                toggledObjects.Add(new ToggledObject { Object = targetRef, Active = active });
            }
            comp.Objects = toggledObjects;
            EditorUtility.SetDirty(comp);
            return comp;
        }

        // ========== VisibleHeadAccessory ==========

        public static Component AddVisibleHeadAccessory(GameObject target)
        {
            var existing = target.GetComponent<ModularAvatarVisibleHeadAccessory>();
            if (existing != null) return existing;
            var comp = Undo.AddComponent<ModularAvatarVisibleHeadAccessory>(target);
            return comp;
        }

        public static bool HasVisibleHeadAccessory(GameObject go)
        {
            return go.GetComponent<ModularAvatarVisibleHeadAccessory>() != null;
        }

        // ========== SetupOutfit ==========

        public static void SetupOutfit(GameObject outfitObject)
        {
            MASetupOutfit.SetupOutfitUI(outfitObject);
        }

        // ========== Component Removal ==========

        /// <summary>
        /// Remove a specific MA component by type name. Returns true if removed.
        /// </summary>
        public static bool RemoveComponent(GameObject target, string componentType)
        {
            Component comp = null;
            switch (componentType.ToLowerInvariant())
            {
                case "menuitem": comp = target.GetComponent<ModularAvatarMenuItem>(); break;
                case "objecttoggle": comp = target.GetComponent<ModularAvatarObjectToggle>(); break;
                case "mergearmature": comp = target.GetComponent<ModularAvatarMergeArmature>(); break;
                case "blendshapesync": comp = target.GetComponent<ModularAvatarBlendshapeSync>(); break;
                case "boneproxy": comp = target.GetComponent<ModularAvatarBoneProxy>(); break;
                case "parameters": comp = target.GetComponent<ModularAvatarParameters>(); break;
                case "visibleheadaccessory": comp = target.GetComponent<ModularAvatarVisibleHeadAccessory>(); break;
                case "meshsettings": comp = target.GetComponent<ModularAvatarMeshSettings>(); break;
                default: return false;
            }

            if (comp == null) return false;
            Undo.DestroyObjectImmediate(comp);
            return true;
        }

        // ========== Query ==========

        /// <summary>
        /// Get all MA components (AvatarTagComponent) under a root.
        /// </summary>
        public static Component[] GetAllMAComponents(GameObject root)
        {
            return root.GetComponentsInChildren<AvatarTagComponent>(true);
        }

        /// <summary>
        /// Get the specific MA component type name for a component instance.
        /// </summary>
        public static string GetMAComponentTypeName(Component comp)
        {
            return comp.GetType().Name;
        }

        /// <summary>
        /// Check if a component is an MA component.
        /// </summary>
        public static bool IsMAComponent(Component comp)
        {
            return comp is AvatarTagComponent;
        }

        // ========== Inspection Helpers ==========

        /// <summary>
        /// Format a single MA component for display. Returns a description string.
        /// </summary>
        public static string DescribeComponent(Component comp, Transform avatarRoot)
        {
            switch (comp)
            {
                case ModularAvatarMergeArmature mergeArm:
                    return $"mergeTarget={mergeArm.mergeTarget?.Get(comp)?.name ?? "(null)"}, prefix='{mergeArm.prefix}', suffix='{mergeArm.suffix}'";

                case ModularAvatarMenuItem menuItem:
                    return $"type={menuItem.PortableControl.Type}, param='{menuItem.PortableControl.Parameter}', value={menuItem.PortableControl.Value}, default={menuItem.isDefault}, synced={menuItem.isSynced}, saved={menuItem.isSaved}";

                case ModularAvatarObjectToggle objToggle:
                    if (objToggle.Objects != null && objToggle.Objects.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder("objects:");
                        foreach (var obj in objToggle.Objects)
                            sb.Append($"\n    - {obj.Object?.referencePath ?? "(null)"} Active={obj.Active}");
                        return sb.ToString();
                    }
                    return "objects: (empty)";

                case ModularAvatarBlendshapeSync bsSync:
                    if (bsSync.Bindings != null && bsSync.Bindings.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder($"bindings ({bsSync.Bindings.Count}):");
                        foreach (var binding in bsSync.Bindings)
                            sb.Append($"\n    - {binding.Blendshape} from '{binding.ReferenceMesh?.referencePath ?? "(null)"}'" +
                                (string.IsNullOrEmpty(binding.LocalBlendshape) ? "" : $" as '{binding.LocalBlendshape}'"));
                        return sb.ToString();
                    }
                    return "bindings: (empty)";

                case ModularAvatarBoneProxy boneProxy:
                    return $"target={boneProxy.target?.name ?? "(null)"}, attachmentMode={boneProxy.attachmentMode}";

                case ModularAvatarMeshSettings meshSettings:
                    return $"bounds={meshSettings.Bounds}, anchor={meshSettings.ProbeAnchor?.referencePath ?? "(auto)"}";

                case ModularAvatarVisibleHeadAccessory _:
                    return "(enabled)";

                case ModularAvatarParameters maParams:
                    if (maParams.parameters != null && maParams.parameters.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder($"parameters ({maParams.parameters.Count}):");
                        foreach (var p in maParams.parameters)
                            sb.Append($"\n    - {p.nameOrPrefix} ({p.syncType}, default={p.defaultValue}, saved={p.saved})");
                        return sb.ToString();
                    }
                    return "parameters: (empty)";

                default:
                    return comp.GetType().Name;
            }
        }

        // ========== Type Parsing Helpers ==========

        public static bool TryParseControlType(string type, out PortableControlType result)
        {
            switch (type.ToLowerInvariant())
            {
                case "toggle": result = PortableControlType.Toggle; return true;
                case "button": result = PortableControlType.Button; return true;
                case "submenu": result = PortableControlType.SubMenu; return true;
                case "radialpuppet": result = PortableControlType.RadialPuppet; return true;
                default: result = PortableControlType.Toggle; return false;
            }
        }

        public static bool TryParseSyncType(string syncType, out ParameterSyncType result)
        {
            switch (syncType.ToLowerInvariant())
            {
                case "bool": result = ParameterSyncType.Bool; return true;
                case "int": result = ParameterSyncType.Int; return true;
                case "float": result = ParameterSyncType.Float; return true;
                case "notsynced": result = ParameterSyncType.NotSynced; return true;
                default: result = ParameterSyncType.Bool; return false;
            }
        }

        /// <summary>
        /// Valid component type names for RemoveComponent.
        /// </summary>
        public static readonly string[] ValidComponentTypes = {
            "MenuItem", "ObjectToggle", "MergeArmature", "BlendshapeSync",
            "BoneProxy", "Parameters", "VisibleHeadAccessory", "MeshSettings"
        };

#else
        // Stubs for when MA is not installed — all return null/false/empty
        public static Component AddMergeArmature(GameObject t, Transform m, string p = "", string s = "") => null;
        public static Component AddBoneProxy(GameObject t, Transform b, int m = 0) => null;
        public static Transform GetBoneProxyTarget(GameObject go) => null;
        public static bool HasBoneProxy(GameObject go) => false;
        public static (string targetName, string targetPath, string mode)? GetBoneProxyInfo(GameObject go) => null;
        public static Component AddMergeAnimator(GameObject t, RuntimeAnimatorController c, int p = 0, bool m = true, bool d = true) => null;
        public static Component AddMenuInstaller(GameObject t) => null;
        public static Component AddMenuItemToggle(GameObject t, string paramName = null, float value = 1f, bool synced = true, bool saved = true, bool isDefault = false, Texture2D icon = null) => null;
        public static Component AddMenuItemSubMenu(GameObject t, Texture2D i = null) => null;
        public static Component AddMenuItemRadial(GameObject t, string p, Texture2D i = null) => null;
        public static Component AddMenuItemButton(GameObject t, string p = null, float v = 1f, Texture2D i = null) => null;
        public static Component AddMenuItem(GameObject t, string ty, string p = null, float v = 1f, bool sy = true, bool sa = true, bool d = false) => null;
        public static bool HasMenuItem(GameObject t) => false;
        public static (string type, string param, float value, bool isDefault, bool synced, bool saved)? GetMenuItemInfo(GameObject go) => null;
        public static Component AddOrGetParameters(GameObject t) => null;
        public static Component AddOrGetBlendshapeSync(GameObject t) => null;
        public static void AddBlendshapeBinding(Component b, string r, string n, string l = "") { }
        public static HashSet<string> GetExistingBindingNames(Component b) => new HashSet<string>();
        public static Component AddObjectToggle(GameObject t, List<(GameObject, bool)> o) => null;
        public static Component AddVisibleHeadAccessory(GameObject t) => null;
        public static bool HasVisibleHeadAccessory(GameObject go) => false;
        public static void SetupOutfit(GameObject o) { }
        public static bool RemoveComponent(GameObject t, string c) => false;
        public static Component[] GetAllMAComponents(GameObject r) => new Component[0];
        public static string GetMAComponentTypeName(Component c) => c.GetType().Name;
        public static bool IsMAComponent(Component c) => false;
        public static string DescribeComponent(Component c, Transform r) => c.GetType().Name;
        public static bool TryParseControlType(string t, out int r) { r = 0; return false; }
        public static bool TryParseSyncType(string s, out int r) { r = 0; return false; }
        public static readonly string[] ValidComponentTypes = new string[0];
#endif
    }
}
