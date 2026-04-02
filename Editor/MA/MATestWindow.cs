using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.MA
{
    internal class MATestWindow : EditorWindow
    {
        [MenuItem("Window/紫陽花広場/MA Test")]
        private static void Open()
        {
            var w = GetWindow<MATestWindow>();
            w.titleContent = new GUIContent("MA Test");
            w.minSize = new Vector2(420, 500);
        }

        // ========== State ==========

        private Vector2 _scroll;
        private string _log = "";

        // Target references
        private GameObject _avatarRoot;
        private GameObject _targetGO;
        private Transform _boneTarget;

        // MenuItem params
        private int _menuItemTypeIdx;
        private readonly string[] _menuItemTypes = { "Toggle", "Button", "SubMenu", "RadialPuppet" };
        private string _menuParamName = "";
        private float _menuValue = 1f;
        private bool _menuSynced = true;
        private bool _menuSaved = true;
        private bool _menuIsDefault;

        // Parameter params
        private string _paramName = "TestParam";
        private int _paramSyncTypeIdx;
        private readonly string[] _paramSyncTypes = { "Bool", "Int", "Float", "NotSynced" };
        private float _paramDefault;
        private bool _paramSaved = true;
        private bool _paramLocalOnly;

        // BoneProxy params
        private int _boneProxyModeIdx;
        private readonly string[] _boneProxyModes = { "AsChildAtRoot", "AsChildKeepWorldPose" };

        // ObjectToggle params
        private GameObject _toggleTargetObj;
        private bool _toggleActive = true;

        // BlendshapeSync params
        private string _bsSrcPath = "";
        private string _bsName = "";

        // MenuBuilder params
        private string _menuHolderName = "TestMenu";
        private string _menuToggleName = "Toggle1";
        private string _menuToggleParam = "toggle_param";
        private string _menuRadialName = "Radial1";
        private string _menuRadialParam = "radial_param";

        // SetupOutfit target
        private GameObject _outfitGO;

        // Foldouts
        private bool _foldAvailability = true;
        private bool _foldFactory = true;
        private bool _foldMenuBuilder = true;
        private bool _foldParamBuilder = true;
        private bool _foldInspect = true;
        private bool _foldLog = true;

        // ========== GUI ==========

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawAvailabilitySection();
            DrawTargetFields();
            DrawFactorySection();
            DrawMenuBuilderSection();
            DrawParameterBuilderSection();
            DrawInspectSection();
            DrawLogSection();

            EditorGUILayout.EndScrollView();
        }

        // ---------- Availability ----------

        private void DrawAvailabilitySection()
        {
            _foldAvailability = EditorGUILayout.Foldout(_foldAvailability, "MAAvailability", true, EditorStyles.foldoutHeader);
            if (!_foldAvailability) return;

            EditorGUI.indentLevel++;

            bool installed = MAAvailability.IsInstalled;
            EditorGUILayout.LabelField("IsInstalled", installed ? "true" : "false");

            var err = MAAvailability.CheckOrError();
            EditorGUILayout.LabelField("CheckOrError()", err ?? "(null = OK)");

            if (!installed)
            {
                EditorGUILayout.HelpBox("Modular Avatar is not installed. Most tests will be skipped.", MessageType.Warning);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Target Fields ----------

        private void DrawTargetFields()
        {
            EditorGUILayout.LabelField("Common Targets", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", _avatarRoot, typeof(GameObject), true);
            _targetGO = (GameObject)EditorGUILayout.ObjectField("Target GO", _targetGO, typeof(GameObject), true);
            _boneTarget = (Transform)EditorGUILayout.ObjectField("Bone Target", _boneTarget, typeof(Transform), true);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Factory Section ----------

        private void DrawFactorySection()
        {
            _foldFactory = EditorGUILayout.Foldout(_foldFactory, "MAComponentFactory", true, EditorStyles.foldoutHeader);
            if (!_foldFactory) return;

            EditorGUI.indentLevel++;

            // --- MenuItem ---
            EditorGUILayout.LabelField("MenuItem", EditorStyles.miniBoldLabel);
            _menuItemTypeIdx = EditorGUILayout.Popup("Type", _menuItemTypeIdx, _menuItemTypes);
            _menuParamName = EditorGUILayout.TextField("Param Name", _menuParamName);
            _menuValue = EditorGUILayout.FloatField("Value", _menuValue);
            _menuSynced = EditorGUILayout.Toggle("Synced", _menuSynced);
            _menuSaved = EditorGUILayout.Toggle("Saved", _menuSaved);
            _menuIsDefault = EditorGUILayout.Toggle("Is Default", _menuIsDefault);

            if (GUILayout.Button("AddMenuItem"))
            {
                RunTest("AddMenuItem", () =>
                {
                    RequireTarget();
                    var comp = MAComponentFactory.AddMenuItem(_targetGO, _menuItemTypes[_menuItemTypeIdx],
                        string.IsNullOrEmpty(_menuParamName) ? null : _menuParamName,
                        _menuValue, _menuSynced, _menuSaved, _menuIsDefault);
                    return comp != null
                        ? $"Added MenuItem ({_menuItemTypes[_menuItemTypeIdx]}) to '{_targetGO.name}'"
                        : "Failed: MenuItem already exists or invalid type";
                });
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("HasMenuItem"))
            {
                RunTest("HasMenuItem", () =>
                {
                    RequireTarget();
                    return $"HasMenuItem: {MAComponentFactory.HasMenuItem(_targetGO)}";
                });
            }
            if (GUILayout.Button("GetMenuItemInfo"))
            {
                RunTest("GetMenuItemInfo", () =>
                {
                    RequireTarget();
                    var info = MAComponentFactory.GetMenuItemInfo(_targetGO);
                    return info != null
                        ? $"type={info.Value.type}, param={info.Value.param}, value={info.Value.value}, default={info.Value.isDefault}"
                        : "No MenuItem found";
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // --- BoneProxy ---
            EditorGUILayout.LabelField("BoneProxy", EditorStyles.miniBoldLabel);
            _boneProxyModeIdx = EditorGUILayout.Popup("Attachment Mode", _boneProxyModeIdx, _boneProxyModes);

            if (GUILayout.Button("AddBoneProxy"))
            {
                RunTest("AddBoneProxy", () =>
                {
                    RequireTarget();
                    if (_boneTarget == null) throw new Exception("Bone Target is not set.");
                    MAComponentFactory.AddBoneProxy(_targetGO, _boneTarget);
                    return $"Added BoneProxy to '{_targetGO.name}' → bone '{_boneTarget.name}'";
                });
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("HasBoneProxy"))
            {
                RunTest("HasBoneProxy", () =>
                {
                    RequireTarget();
                    return $"HasBoneProxy: {MAComponentFactory.HasBoneProxy(_targetGO)}";
                });
            }
            if (GUILayout.Button("GetBoneProxyInfo"))
            {
                RunTest("GetBoneProxyInfo", () =>
                {
                    RequireTarget();
                    var info = MAComponentFactory.GetBoneProxyInfo(_targetGO);
                    return info != null
                        ? $"target={info.Value.targetName}, path={info.Value.targetPath}, mode={info.Value.mode}"
                        : "No BoneProxy found";
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // --- ObjectToggle ---
            EditorGUILayout.LabelField("ObjectToggle", EditorStyles.miniBoldLabel);
            _toggleTargetObj = (GameObject)EditorGUILayout.ObjectField("Toggle Object", _toggleTargetObj, typeof(GameObject), true);
            _toggleActive = EditorGUILayout.Toggle("Active", _toggleActive);

            if (GUILayout.Button("AddObjectToggle"))
            {
                RunTest("AddObjectToggle", () =>
                {
                    RequireTarget();
                    if (_toggleTargetObj == null) throw new Exception("Toggle Object is not set.");
                    var comp = MAComponentFactory.AddObjectToggle(_targetGO,
                        new List<(GameObject, bool)> { (_toggleTargetObj, _toggleActive) });
                    return comp != null
                        ? $"Added ObjectToggle to '{_targetGO.name}' (target='{_toggleTargetObj.name}', active={_toggleActive})"
                        : "Failed to add ObjectToggle";
                });
            }

            EditorGUILayout.Space(4);

            // --- VisibleHeadAccessory ---
            EditorGUILayout.LabelField("VisibleHeadAccessory", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add"))
            {
                RunTest("AddVisibleHeadAccessory", () =>
                {
                    RequireTarget();
                    MAComponentFactory.AddVisibleHeadAccessory(_targetGO);
                    return $"Added VisibleHeadAccessory to '{_targetGO.name}'";
                });
            }
            if (GUILayout.Button("Has?"))
            {
                RunTest("HasVisibleHeadAccessory", () =>
                {
                    RequireTarget();
                    return $"HasVisibleHeadAccessory: {MAComponentFactory.HasVisibleHeadAccessory(_targetGO)}";
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // --- BlendshapeSync ---
            EditorGUILayout.LabelField("BlendshapeSync", EditorStyles.miniBoldLabel);
            _bsSrcPath = EditorGUILayout.TextField("Source Mesh Path", _bsSrcPath);
            _bsName = EditorGUILayout.TextField("Blendshape Name", _bsName);

            if (GUILayout.Button("AddOrGetBlendshapeSync + AddBinding"))
            {
                RunTest("BlendshapeSync", () =>
                {
                    RequireTarget();
                    if (string.IsNullOrEmpty(_bsSrcPath)) throw new Exception("Source Mesh Path is empty.");
                    if (string.IsNullOrEmpty(_bsName)) throw new Exception("Blendshape Name is empty.");
                    var sync = MAComponentFactory.AddOrGetBlendshapeSync(_targetGO);
                    MAComponentFactory.AddBlendshapeBinding(sync, _bsSrcPath, _bsName);
                    return $"Added BlendshapeSync binding: '{_bsName}' from '{_bsSrcPath}'";
                });
            }

            EditorGUILayout.Space(4);

            // --- SetupOutfit ---
            EditorGUILayout.LabelField("SetupOutfit", EditorStyles.miniBoldLabel);
            _outfitGO = (GameObject)EditorGUILayout.ObjectField("Outfit GO", _outfitGO, typeof(GameObject), true);

            if (GUILayout.Button("SetupOutfit"))
            {
                RunTest("SetupOutfit", () =>
                {
                    if (_outfitGO == null) throw new Exception("Outfit GO is not set.");
                    MAComponentFactory.SetupOutfit(_outfitGO);
                    return $"SetupOutfit completed for '{_outfitGO.name}'";
                });
            }

            EditorGUILayout.Space(4);

            // --- RemoveComponent ---
            EditorGUILayout.LabelField("RemoveComponent", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var ct in MAComponentFactory.ValidComponentTypes)
            {
                if (GUILayout.Button(ct, EditorStyles.miniButton))
                {
                    var typeName = ct;
                    RunTest($"Remove({typeName})", () =>
                    {
                        RequireTarget();
                        bool ok = MAComponentFactory.RemoveComponent(_targetGO, typeName);
                        return ok ? $"Removed {typeName} from '{_targetGO.name}'" : $"No {typeName} found on '{_targetGO.name}'";
                    });
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Menu Builder Section ----------

        private void DrawMenuBuilderSection()
        {
            _foldMenuBuilder = EditorGUILayout.Foldout(_foldMenuBuilder, "MAMenuBuilder", true, EditorStyles.foldoutHeader);
            if (!_foldMenuBuilder) return;

            EditorGUI.indentLevel++;

            _menuHolderName = EditorGUILayout.TextField("Holder Name", _menuHolderName);
            _menuToggleName = EditorGUILayout.TextField("Toggle Name", _menuToggleName);
            _menuToggleParam = EditorGUILayout.TextField("Toggle Param", _menuToggleParam);
            _menuRadialName = EditorGUILayout.TextField("Radial Name", _menuRadialName);
            _menuRadialParam = EditorGUILayout.TextField("Radial Param", _menuRadialParam);

            if (GUILayout.Button("Create Menu (Toggle + Radial)"))
            {
                RunTest("MAMenuBuilder", () =>
                {
                    RequireAvatar();
                    var builder = MAMenuBuilder.Create(_avatarRoot, _menuHolderName);
                    if (builder == null) throw new Exception("MAMenuBuilder.Create returned null (MA not installed?)");
                    builder
                        .AddToggle(_menuToggleName, _menuToggleParam, isDefault: true)
                        .AddRadial(_menuRadialName, _menuRadialParam);
                    return $"Created menu '{_menuHolderName}' with Toggle + Radial under '{_avatarRoot.name}'";
                });
            }

            if (GUILayout.Button("Create Nested SubMenu"))
            {
                RunTest("MAMenuBuilder Nested", () =>
                {
                    RequireAvatar();
                    var builder = MAMenuBuilder.Create(_avatarRoot, _menuHolderName + "_Nested");
                    if (builder == null) throw new Exception("MAMenuBuilder.Create returned null");
                    var sub = builder.AddSubMenu("SubMenu1");
                    sub.AddToggle("Sub Toggle", "sub_toggle_param")
                       .AddButton("Sub Button", "sub_btn_param");
                    return $"Created nested menu under '{_avatarRoot.name}'";
                });
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Parameter Builder Section ----------

        private void DrawParameterBuilderSection()
        {
            _foldParamBuilder = EditorGUILayout.Foldout(_foldParamBuilder, "MAParameterBuilder", true, EditorStyles.foldoutHeader);
            if (!_foldParamBuilder) return;

            EditorGUI.indentLevel++;

            _paramName = EditorGUILayout.TextField("Param Name", _paramName);
            _paramSyncTypeIdx = EditorGUILayout.Popup("Sync Type", _paramSyncTypeIdx, _paramSyncTypes);
            _paramDefault = EditorGUILayout.FloatField("Default Value", _paramDefault);
            _paramSaved = EditorGUILayout.Toggle("Saved", _paramSaved);
            _paramLocalOnly = EditorGUILayout.Toggle("Local Only", _paramLocalOnly);

            if (GUILayout.Button("AddOrGetParameters + AddParam"))
            {
                RunTest("AddParam", () =>
                {
                    RequireTarget();
                    var maParams = MAComponentFactory.AddOrGetParameters(_targetGO);
                    if (maParams == null) throw new Exception("Failed to get MAParameters (MA not installed?)");
                    bool ok = MAParameterBuilder.AddParam(maParams, _paramName,
                        _paramSyncTypes[_paramSyncTypeIdx], _paramDefault, _paramSaved, _paramLocalOnly);
                    return ok
                        ? $"Added param '{_paramName}' ({_paramSyncTypes[_paramSyncTypeIdx]}) to '{_targetGO.name}'"
                        : $"Failed: param '{_paramName}' already exists or invalid syncType";
                });
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("HasParameter"))
            {
                RunTest("HasParameter", () =>
                {
                    RequireTarget();
                    var maParams = MAComponentFactory.AddOrGetParameters(_targetGO);
                    if (maParams == null) throw new Exception("No MAParameters component");
                    return $"HasParameter('{_paramName}'): {MAParameterBuilder.HasParameter(maParams, _paramName)}";
                });
            }
            if (GUILayout.Button("GetParameterNames"))
            {
                RunTest("GetParameterNames", () =>
                {
                    RequireTarget();
                    var maParams = MAComponentFactory.AddOrGetParameters(_targetGO);
                    if (maParams == null) throw new Exception("No MAParameters component");
                    var names = MAParameterBuilder.GetParameterNames(maParams);
                    return names.Count > 0
                        ? $"Parameters: {string.Join(", ", names)}"
                        : "No parameters found";
                });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Inspect Section ----------

        private void DrawInspectSection()
        {
            _foldInspect = EditorGUILayout.Foldout(_foldInspect, "Inspect MA Components", true, EditorStyles.foldoutHeader);
            if (!_foldInspect) return;

            EditorGUI.indentLevel++;

            if (GUILayout.Button("GetAllMAComponents (Avatar Root)"))
            {
                RunTest("GetAllMAComponents", () =>
                {
                    RequireAvatar();
                    var comps = MAComponentFactory.GetAllMAComponents(_avatarRoot);
                    if (comps.Length == 0) return "No MA components found.";

                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {comps.Length} MA component(s):");
                    foreach (var c in comps)
                    {
                        var path = GetRelativePath(_avatarRoot.transform, c.transform);
                        sb.AppendLine($"  [{path}] {MAComponentFactory.GetMAComponentTypeName(c)}");
                        sb.AppendLine($"    {MAComponentFactory.DescribeComponent(c, _avatarRoot.transform)}");
                    }
                    return sb.ToString().TrimEnd();
                });
            }

            if (GUILayout.Button("Describe Target GO Components"))
            {
                RunTest("DescribeComponents", () =>
                {
                    RequireTarget();
                    var comps = _targetGO.GetComponents<Component>();
                    var sb = new StringBuilder();
                    int maCount = 0;
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        if (!MAComponentFactory.IsMAComponent(c)) continue;
                        maCount++;
                        sb.AppendLine($"  {MAComponentFactory.GetMAComponentTypeName(c)}: {MAComponentFactory.DescribeComponent(c, _targetGO.transform)}");
                    }
                    return maCount > 0
                        ? $"MA components on '{_targetGO.name}' ({maCount}):\n{sb.ToString().TrimEnd()}"
                        : $"No MA components on '{_targetGO.name}'";
                });
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ---------- Log Section ----------

        private void DrawLogSection()
        {
            _foldLog = EditorGUILayout.Foldout(_foldLog, "Log", true, EditorStyles.foldoutHeader);
            if (!_foldLog) return;

            if (GUILayout.Button("Clear Log"))
                _log = "";

            EditorGUILayout.TextArea(_log, GUILayout.MinHeight(120));
        }

        // ========== Helpers ==========

        private void Log(string msg)
        {
            _log = $"[{DateTime.Now:HH:mm:ss}] {msg}\n{_log}";
            Repaint();
        }

        private void RunTest(string label, Func<string> action)
        {
            try
            {
                var result = action();
                Log($"[OK] {label}: {result}");
            }
            catch (Exception ex)
            {
                Log($"[ERR] {label}: {ex.Message}");
            }
        }

        private void RequireTarget()
        {
            if (_targetGO == null) throw new Exception("Target GO is not set.");
        }

        private void RequireAvatar()
        {
            if (_avatarRoot == null) throw new Exception("Avatar Root is not set.");
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
    }
}
