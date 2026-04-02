#if FACE_EMO
using Suzuryg.FaceEmo.Domain;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Components.Data;
using Suzuryg.FaceEmo.Components.Settings;
using FaceEmoMenu = Suzuryg.FaceEmo.Domain.Menu;
using FaceEmoAnimation = Suzuryg.FaceEmo.Domain.Animation;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public class FaceEmoTestWindow : EditorWindow
    {
        [MenuItem("Window/紫陽花広場/FaceEmo Test")]
        public static void ShowWindow()
        {
            var w = GetWindow<FaceEmoTestWindow>("FaceEmo Test");
            w.minSize = new Vector2(800, 400);
        }

        // ── State ──
        private FaceEmoLauncherComponent[] _launchers;
        private string[] _launcherNames;
        private int _selectedLauncherIndex;

        // ── Cached menu ──
        private FaceEmoMenu _cachedMenu;
        private List<string> _modeNames;   // "[R] name" or "[U] name"
        private List<string> _modeIds;
        private List<FaceEmoAPI.MenuItemInfo> _menuItems; // tree-ordered list

        // ── Left panel ──
        private Vector2 _leftScroll;
        private int _selectedModeIndex;
        private string _selectedItemId;       // currently selected mode/group ID
        private string _addModeName = "新しい表情パターン";
        private string _addGroupName = "新しいグループ";
        private bool _foldActions = true;
        private bool _foldEdit = false;
        private bool _foldAnim = false;
        private bool _foldSettings = false;
        private bool _foldLog = false;

        // ── Move state ──
        private bool _foldMove = false;
        private int _moveDestIndex;           // index into _destNames
        private string[] _destNames;          // destination popup labels
        private string[] _destIds;            // destination IDs

        // ── Edit fields ──
        private string _editDisplayName = "";
        private int _editEyeTracking;     // 0=keep, 1=Tracking, 2=Animation
        private int _editMouthTracking;
        private int _editBlink;           // 0=keep, 1=true, 2=false
        private int _editMouthCancel;
        private int _editChangeDefaultFace;   // 0=keep, 1=true, 2=false
        private int _editUseAnimName;         // 0=keep, 1=true, 2=false
        private bool _editSetAsDefault;
        private AnimationClip _editAnimClip;

        // ── Branch edit fields (direct values, initialized from branch) ──
        private int _editBranchIndex = -1;
        private int _brEditEye;            // 0=Tracking, 1=Animation
        private int _brEditMouth;          // 0=Tracking, 1=Animation
        private bool _brEditBlink;
        private bool _brEditMouthCancel;
        private bool _brEditLeftTrigger;
        private bool _brEditRightTrigger;
        private AnimationClip _brEditBaseClip;
        private AnimationClip _brEditLeftClip;
        private AnimationClip _brEditRightClip;
        private AnimationClip _brEditBothClip;

        // ── Branch add fields ──
        private bool _foldBranchAdd;
        private int _brAddHand;            // index into _handNames
        private int _brAddGesture;         // index into _gestureNames
        private int _brAddOp;              // 0=Equals, 1=NotEqual
        private readonly List<Condition> _brAddConditions = new List<Condition>();

        // ── Condition add to existing branch ──
        private int _condAddBranchIndex = -1;
        private int _condAddHand;
        private int _condAddGesture;
        private int _condAddOp;

        // ── Condition edit ──
        private int _condEditBranchIndex = -1;
        private int _condEditIndex = -1;
        private int _condEditHand;
        private int _condEditGesture;
        private int _condEditOp;

        // ── Enum names for popups ──
        private static readonly string[] _handNames =
            { "Left", "Right", "Either", "Both", "OneSide" };
        private static readonly Hand[] _handValues =
            { Hand.Left, Hand.Right, Hand.Either, Hand.Both, Hand.OneSide };
        private static readonly string[] _gestureNames =
            { "Neutral", "Fist", "HandOpen", "Fingerpoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };
        private static readonly HandGesture[] _gestureValues =
            { HandGesture.Neutral, HandGesture.Fist, HandGesture.HandOpen, HandGesture.Fingerpoint,
              HandGesture.Victory, HandGesture.RockNRoll, HandGesture.HandGun, HandGesture.ThumbsUp };
        private static readonly string[] _opNames = { "Equals", "NotEqual" };
        private static readonly string[] _trackingOpts = { "Tracking", "Animation" };

        // ── Right panel ──
        private Vector2 _rightScroll;

        // ── Log ──
        private Vector2 _logScroll;
        private readonly List<string> _log = new List<string>();

        // ── Layout ──
        private float _leftWidth = 280f;
        private const float MIN_LEFT = 200f;
        private const float MIN_RIGHT = 300f;
        private const float SPLITTER_W = 4f;
        private bool _draggingSplitter;

        private void OnEnable() => RefreshLaunchers();

        // ══════════════════════════════════════════════════════
        //  OnGUI — top bar + left/right split
        // ══════════════════════════════════════════════════════

        private float _topBarHeight;

        private void OnGUI()
        {
            DrawTopBar();

            // Capture toolbar height after layout
            if (Event.current.type == EventType.Repaint)
                _topBarHeight = GUILayoutUtility.GetLastRect().yMax;

            var launcher = GetSelectedLauncher();
            if (launcher == null)
            {
                DrawNoLauncherUI();
                return;
            }

            // --- Split area: use window position directly ---
            float top = _topBarHeight;
            float totalWidth = position.width;
            float totalHeight = position.height - top;
            if (totalHeight < 10) return; // guard

            // Clamp left width
            _leftWidth = Mathf.Clamp(_leftWidth, MIN_LEFT, totalWidth - MIN_RIGHT - SPLITTER_W);

            var leftRect = new Rect(0, top, _leftWidth, totalHeight);
            var splitterRect = new Rect(_leftWidth, top, SPLITTER_W, totalHeight);
            var rightRect = new Rect(_leftWidth + SPLITTER_W, top,
                totalWidth - _leftWidth - SPLITTER_W, totalHeight);

            // Splitter interaction
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
            {
                _draggingSplitter = true;
                Event.current.Use();
            }
            if (_draggingSplitter)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _leftWidth = Event.current.mousePosition.x;
                    _leftWidth = Mathf.Clamp(_leftWidth, MIN_LEFT, totalWidth - MIN_RIGHT - SPLITTER_W);
                    Repaint();
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _draggingSplitter = false;
                    Event.current.Use();
                }
            }

            // Draw splitter visual
            EditorGUI.DrawRect(splitterRect, new Color(0.2f, 0.2f, 0.2f, 1f));

            // Draw panels
            GUILayout.BeginArea(leftRect);
            DrawLeftPanel(launcher);
            GUILayout.EndArea();

            GUILayout.BeginArea(rightRect);
            DrawRightPanel(launcher);
            GUILayout.EndArea();
        }

        // ══════════════════════════════════════════════════════
        //  No Launcher — Setup UI
        // ══════════════════════════════════════════════════════

        private void DrawNoLauncherUI()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "FaceEmo がシーンにセットアップされていません。\n" +
                "FaceEmo を使うには、まずシーンに FaceEmo オブジェクトを作成してください。",
                MessageType.Warning);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("FaceEmo を新規作成", GUILayout.Width(200), GUILayout.Height(30)))
            {
                EditorApplication.ExecuteMenuItem("FaceEmo/New Menu");
                // Delay refresh to let FaceEmo create the object
                EditorApplication.delayCall += () =>
                {
                    RefreshLaunchers();
                    Repaint();
                };
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("または、ヒエラルキー内のアバターの FaceEmo アイコンをクリックして作成できます。",
                EditorStyles.centeredGreyMiniLabel);
        }

        // ══════════════════════════════════════════════════════
        //  Top Bar — Launcher + Load
        // ══════════════════════════════════════════════════════

        private void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Launcher", GUILayout.Width(55));
            if (_launcherNames != null && _launcherNames.Length > 0)
                _selectedLauncherIndex = EditorGUILayout.Popup(_selectedLauncherIndex, _launcherNames,
                    EditorStyles.toolbarPopup, GUILayout.MinWidth(150));
            else
                EditorGUILayout.LabelField("(none)", GUILayout.Width(60));

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55)))
                RefreshLaunchers();

            GUILayout.FlexibleSpace();

            var launcher = GetSelectedLauncher();
            if (launcher != null && GUILayout.Button("Load Menu", EditorStyles.toolbarButton, GUILayout.Width(80)))
                LoadMenuFromLauncher(launcher);

            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════
        //  Left Panel — Mode list + Actions
        // ══════════════════════════════════════════════════════

        private void DrawLeftPanel(FaceEmoLauncherComponent launcher)
        {
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            // ── Launcher Info + Avatar Status ──
            var av3 = FaceEmoAPI.GetAV3Setting(launcher);
            if (av3 != null && av3.TargetAvatar != null)
            {
                EditorGUILayout.LabelField($"Avatar: {av3.TargetAvatar.gameObject.name}", EditorStyles.miniLabel);
            }
            else if (av3 != null)
            {
                EditorGUILayout.HelpBox(
                    "ターゲットアバターが設定されていません (Avatar=None)。\n" +
                    "表情メニューの生成にはアバターの設定が必要です。",
                    MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Avatar", GUILayout.Width(45));
                var descriptorType = typeof(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor);
                var newAvatar = EditorGUILayout.ObjectField(null, descriptorType, true) as MonoBehaviour;
                if (newAvatar != null)
                {
                    Undo.RecordObject(av3, "Set FaceEmo Target Avatar");
                    av3.TargetAvatar = newAvatar;
                    // Build hierarchy path
                    string path = newAvatar.gameObject.name;
                    Transform p = newAvatar.transform.parent;
                    while (p != null) { path = p.name + "/" + path; p = p.parent; }
                    av3.TargetAvatarPath = path;
                    EditorUtility.SetDirty(av3);
                    Log($"Target avatar set to '{newAvatar.gameObject.name}'");
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("AV3Setting が見つかりません。FaceEmo の初期化に問題がある可能性があります。", MessageType.Error);
            }

            Separator();

            // ── Menu Item Tree ──
            if (_cachedMenu == null || _menuItems == null)
            {
                EditorGUILayout.HelpBox("Press 'Load Menu' to load data.", MessageType.Info);
            }
            else
            {
                // Header
                int modeCount = _menuItems.Count(m => m.Type == MenuItemType.Mode);
                int groupCount = _menuItems.Count(m => m.Type == MenuItemType.Group);
                EditorGUILayout.LabelField($"Menu Items ({modeCount} modes, {groupCount} groups)", EditorStyles.boldLabel);

                // Default expression
                string defaultName = "(none)";
                if (!string.IsNullOrEmpty(_cachedMenu.DefaultSelection))
                {
                    var defMode = FaceEmoAPI.FindExpressionById(_cachedMenu, _cachedMenu.DefaultSelection);
                    if (defMode != null) defaultName = defMode.DisplayName;
                }
                EditorGUILayout.LabelField($"Default: {defaultName}", EditorStyles.miniLabel);
                EditorGUILayout.Space(2);

                // Section headers + tree items
                EditorGUILayout.LabelField($"Registered ({_cachedMenu.Registered.Order.Count}/{7})",
                    EditorStyles.miniBoldLabel);
                DrawMenuItemTree(launcher, "R");
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"Unregistered ({_cachedMenu.Unregistered.Order.Count})",
                    EditorStyles.miniBoldLabel);
                DrawMenuItemTree(launcher, "U");

                Separator();

                // ── Actions ──
                _foldActions = EditorGUILayout.Foldout(_foldActions, "Add / Remove / Move", true, EditorStyles.foldoutHeader);
                if (_foldActions)
                {
                    // Add Mode
                    _addModeName = EditorGUILayout.TextField("Mode Name", _addModeName);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ Registered", GUILayout.Height(20)))
                        AddModeToScene(launcher, _addModeName, FaceEmoMenu.RegisteredId);
                    if (GUILayout.Button("+ Unregistered", GUILayout.Height(20)))
                        AddModeToScene(launcher, _addModeName, FaceEmoMenu.UnregisteredId);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(2);

                    // Add Group
                    _addGroupName = EditorGUILayout.TextField("Group Name", _addGroupName);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ Group (R)", GUILayout.Height(20)))
                        AddGroupToScene(launcher, _addGroupName, FaceEmoMenu.RegisteredId);
                    if (GUILayout.Button("+ Group (U)", GUILayout.Height(20)))
                        AddGroupToScene(launcher, _addGroupName, FaceEmoMenu.UnregisteredId);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(2);

                    // Remove selected
                    bool hasItem = _selectedItemId != null;
                    GUI.enabled = hasItem;
                    if (GUILayout.Button("Remove Selected", GUILayout.Height(20)))
                        RemoveItemFromScene(launcher);
                    GUI.enabled = true;

                    EditorGUILayout.Space(2);

                    // Move selected
                    if (hasItem && _destNames != null && _destNames.Length > 0)
                    {
                        EditorGUILayout.LabelField("Move Selected To:", EditorStyles.miniLabel);
                        EditorGUILayout.BeginHorizontal();
                        _moveDestIndex = EditorGUILayout.Popup(_moveDestIndex, _destNames);
                        if (GUILayout.Button("Move", GUILayout.Width(50), GUILayout.Height(20)))
                            MoveItemInScene(launcher);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                Separator();

                // ── Edit ──
                _foldEdit = EditorGUILayout.Foldout(_foldEdit, "Edit Properties", true, EditorStyles.foldoutHeader);
                if (_foldEdit && HasSelection())
                {
                    EditorGUILayout.LabelField(_modeNames[_selectedModeIndex], EditorStyles.miniLabel);
                    _editDisplayName = EditorGUILayout.TextField("DisplayName", _editDisplayName);
                    string[] trackOpts = { "(keep)", "Tracking", "Animation" };
                    _editEyeTracking = EditorGUILayout.Popup("Eye", _editEyeTracking, trackOpts);
                    _editMouthTracking = EditorGUILayout.Popup("Mouth", _editMouthTracking, trackOpts);
                    string[] boolOpts = { "(keep)", "true", "false" };
                    _editBlink = EditorGUILayout.Popup("Blink", _editBlink, boolOpts);
                    _editMouthCancel = EditorGUILayout.Popup("MouthCancel", _editMouthCancel, boolOpts);
                    _editChangeDefaultFace = EditorGUILayout.Popup("ChangeDefaultFace", _editChangeDefaultFace, boolOpts);
                    _editUseAnimName = EditorGUILayout.Popup("UseAnimName", _editUseAnimName, boolOpts);
                    _editSetAsDefault = EditorGUILayout.Toggle("Set as Default", _editSetAsDefault);
                    if (GUILayout.Button("Apply"))
                        ApplyPropertyChanges(launcher);
                }

                Separator();

                // ── Animation ──
                _foldAnim = EditorGUILayout.Foldout(_foldAnim, "Animation", true, EditorStyles.foldoutHeader);
                if (_foldAnim && HasSelection())
                {
                    EditorGUILayout.LabelField(_modeNames[_selectedModeIndex], EditorStyles.miniLabel);
                    _editAnimClip = (AnimationClip)EditorGUILayout.ObjectField(
                        "Clip", _editAnimClip, typeof(AnimationClip), false);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Assign")) AssignAnimation(launcher);
                    if (GUILayout.Button("Clear")) ClearAnimation(launcher);
                    EditorGUILayout.EndHorizontal();
                }

                Separator();
            }

            // ── Settings ──
            _foldSettings = EditorGUILayout.Foldout(_foldSettings, "Settings", true, EditorStyles.foldoutHeader);
            if (_foldSettings && launcher.AV3Setting != null)
            {
                DrawSettingsSection(launcher);
            }

            Separator();

            // ── Log ──
            _foldLog = EditorGUILayout.Foldout(_foldLog, $"Log ({_log.Count})", true, EditorStyles.foldoutHeader);
            if (_foldLog)
            {
                if (GUILayout.Button("Clear", GUILayout.Width(45)))
                    _log.Clear();
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MaxHeight(100));
                for (int i = _log.Count - 1; i >= 0; i--)
                    EditorGUILayout.LabelField(_log[i], EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();
        }

        // ══════════════════════════════════════════════════════
        //  Right Panel — Expression Detail + Branch Editing
        // ══════════════════════════════════════════════════════

        private void DrawRightPanel(FaceEmoLauncherComponent launcher)
        {
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_cachedMenu == null || _selectedItemId == null)
            {
                EditorGUILayout.LabelField("Select an item to view details.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndScrollView();
                return;
            }

            // Check if selected item is a Group
            if (FaceEmoAPI.ContainsGroup(_cachedMenu, _selectedItemId))
            {
                DrawGroupDetail(launcher, _selectedItemId);
                EditorGUILayout.EndScrollView();
                return;
            }

            // Mode detail
            string modeId = _selectedItemId;
            var mode = FaceEmoAPI.FindExpressionById(_cachedMenu, modeId);
            if (mode == null)
            {
                EditorGUILayout.LabelField("Mode not found.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndScrollView();
                return;
            }

            // ── Header ──
            EditorGUILayout.LabelField(mode.DisplayName, EditorStyles.largeLabel);
            EditorGUILayout.LabelField($"ID: {modeId}", EditorStyles.miniLabel);

            Separator();

            // ── Mode Properties ──
            SectionLabel("Mode Properties");
            Row("ChangeDefaultFace", mode.ChangeDefaultFace.ToString());
            Row("UseAnimNameAsDisplay", mode.UseAnimationNameAsDisplayName.ToString());
            Row("EyeTrackingControl", mode.EyeTrackingControl.ToString());
            Row("MouthTrackingControl", mode.MouthTrackingControl.ToString());
            Row("BlinkEnabled", mode.BlinkEnabled.ToString());
            Row("MouthMorphCanceler", mode.MouthMorphCancelerEnabled.ToString());
            bool isDefault = _cachedMenu.DefaultSelection == modeId;
            Row("IsDefaultExpression", isDefault.ToString());

            Separator();

            // ── Mode Animation ──
            SectionLabel("Mode Animation");
            DrawAnimField(mode.Animation);

            Separator();

            // ── Branches ──
            SectionLabel($"Branches ({mode.Branches.Count})");

            if (mode.Branches.Count == 0)
            {
                EditorGUILayout.LabelField("(no branches)", EditorStyles.miniLabel);
            }

            int branchCount = mode.Branches.Count;
            for (int b = 0; b < branchCount; b++)
            {
                DrawBranch(launcher, modeId, b, mode.Branches[b], branchCount);
                if (b < branchCount - 1)
                    Separator();
            }

            Separator();

            // ── Add Branch ──
            _foldBranchAdd = EditorGUILayout.Foldout(_foldBranchAdd, "Add Branch", true, EditorStyles.foldoutHeader);
            if (_foldBranchAdd)
            {
                DrawAddBranchUI(launcher, modeId);
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        // ── Group detail (right panel) ──

        private string _editGroupName = "";

        private void DrawGroupDetail(FaceEmoLauncherComponent launcher, string groupId)
        {
            var group = _cachedMenu.GetGroup(groupId);
            if (group == null)
            {
                EditorGUILayout.LabelField("Group not found.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // ── Header ──
            EditorGUILayout.LabelField($"\u25b6 {group.DisplayName}", EditorStyles.largeLabel);
            EditorGUILayout.LabelField($"ID: {groupId}", EditorStyles.miniLabel);

            Separator();

            // ── Edit DisplayName ──
            SectionLabel("Group Properties");
            EditorGUILayout.BeginHorizontal();
            _editGroupName = EditorGUILayout.TextField("DisplayName", _editGroupName);
            if (GUILayout.Button("Apply", GUILayout.Width(55)))
            {
                if (!string.IsNullOrEmpty(_editGroupName))
                {
                    var menu = FaceEmoAPI.LoadMenu(launcher);
                    if (menu != null)
                    {
                        FaceEmoAPI.ModifyGroupProperties(menu, groupId, _editGroupName);
                        FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Rename Group");
                        Log($"Renamed group '{group.DisplayName}' → '{_editGroupName}'");
                        LoadMenuFromLauncher(launcher);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            Separator();

            // ── Stats ──
            SectionLabel("Info");
            Row("Items", group.Order.Count.ToString());
            Row("IsFull", group.IsFull.ToString());
            Row("FreeSpace", group.FreeSpace.ToString());

            Separator();

            // ── Contained Items ──
            SectionLabel($"Contents ({group.Order.Count})");
            if (group.Order.Count == 0)
            {
                EditorGUILayout.LabelField("(empty group)", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUI.indentLevel++;
                foreach (string childId in group.Order)
                {
                    var childType = group.GetType(childId);
                    if (childType == MenuItemType.Mode)
                    {
                        var childMode = group.GetMode(childId);
                        string name = childMode?.DisplayName ?? childId;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"\u25cf {name}", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                        {
                            _selectedItemId = childId;
                            int idx = _modeIds.IndexOf(childId);
                            if (idx >= 0) _selectedModeIndex = idx;
                            Repaint();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else if (childType == MenuItemType.Group)
                    {
                        var childGroup = group.GetGroup(childId);
                        string name = childGroup?.DisplayName ?? childId;
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"\u25b6 {name} ({childGroup?.Order.Count ?? 0})", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                        {
                            _selectedItemId = childId;
                            Repaint();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }

            // ── Add to group ──
            Separator();
            SectionLabel("Add to this Group");
            EditorGUILayout.BeginHorizontal();
            _addModeName = EditorGUILayout.TextField(_addModeName);
            if (GUILayout.Button("+ Mode", GUILayout.Width(60)))
                AddModeToScene(launcher, _addModeName, groupId);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _addGroupName = EditorGUILayout.TextField(_addGroupName);
            if (GUILayout.Button("+ Group", GUILayout.Width(60)))
                AddGroupToScene(launcher, _addGroupName, groupId);
            EditorGUILayout.EndHorizontal();
        }

        // ── Branch detail + actions ──

        private void DrawBranch(FaceEmoLauncherComponent launcher, string modeId, int index, IBranch branch, int branchCount)
        {
            // Conditions summary for header
            var condParts = new List<string>();
            foreach (var c in branch.Conditions)
            {
                string op = c.ComparisonOperator == ComparisonOperator.NotEqual ? "!=" : "=";
                condParts.Add($"{c.Hand} {op} {c.HandGesture}");
            }
            string condHeader = condParts.Count > 0 ? string.Join(", ", condParts) : "(no conditions)";

            // Header with action buttons
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Branch [{index}]  {condHeader}", EditorStyles.boldLabel);

            // Reorder buttons
            GUI.enabled = index > 0;
            if (GUILayout.Button("\u25b2", GUILayout.Width(22)))
                MoveBranchInScene(launcher, modeId, index, index - 1);
            GUI.enabled = index < branchCount - 1;
            if (GUILayout.Button("\u25bc", GUILayout.Width(22)))
                MoveBranchInScene(launcher, modeId, index, index + 1);
            GUI.enabled = true;

            bool isEditing = (_editBranchIndex == index);
            if (GUILayout.Button(isEditing ? "Done" : "Edit", GUILayout.Width(45)))
            {
                if (!isEditing)
                    EnterBranchEditMode(index, branch);
                else
                    _editBranchIndex = -1;
            }
            if (GUILayout.Button("X", GUILayout.Width(22)))
                RemoveBranchFromScene(launcher, modeId, index);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;

            // Conditions
            SubLabel("Conditions");
            EditorGUI.indentLevel++;
            if (branch.Conditions.Count == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
            }
            for (int c = 0; c < branch.Conditions.Count; c++)
            {
                var cond = branch.Conditions[c];

                // Inline edit mode for this condition
                if (_condEditBranchIndex == index && _condEditIndex == c)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"[{c}]", GUILayout.Width(24));
                    _condEditHand = EditorGUILayout.Popup(_condEditHand, _handNames, GUILayout.Width(70));
                    _condEditOp = EditorGUILayout.Popup(_condEditOp, _opNames, GUILayout.Width(70));
                    _condEditGesture = EditorGUILayout.Popup(_condEditGesture, _gestureNames, GUILayout.Width(90));
                    if (GUILayout.Button("OK", GUILayout.Width(28)))
                        ModifyConditionInScene(launcher, modeId, index, c);
                    if (GUILayout.Button("x", GUILayout.Width(20)))
                    { _condEditBranchIndex = -1; _condEditIndex = -1; }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    string opStr = cond.ComparisonOperator == ComparisonOperator.NotEqual ? "NotEqual" : "Equals";
                    EditorGUILayout.LabelField($"[{c}] {cond.Hand}  {opStr}  {cond.HandGesture}",
                        GUILayout.ExpandWidth(true));
                    GUI.enabled = c > 0;
                    if (GUILayout.Button("\u25b2", GUILayout.Width(22)))
                        MoveConditionInScene(launcher, modeId, index, c, c - 1);
                    GUI.enabled = c < branch.Conditions.Count - 1;
                    if (GUILayout.Button("\u25bc", GUILayout.Width(22)))
                        MoveConditionInScene(launcher, modeId, index, c, c + 1);
                    GUI.enabled = true;
                    if (GUILayout.Button("E", GUILayout.Width(22)))
                    {
                        _condEditBranchIndex = index;
                        _condEditIndex = c;
                        _condEditHand = System.Array.IndexOf(_handValues, cond.Hand);
                        _condEditGesture = System.Array.IndexOf(_gestureValues, cond.HandGesture);
                        _condEditOp = cond.ComparisonOperator == ComparisonOperator.NotEqual ? 1 : 0;
                    }
                    if (GUILayout.Button("x", GUILayout.Width(20)))
                        RemoveConditionFromScene(launcher, modeId, index, c);
                    EditorGUILayout.EndHorizontal();
                }
            }

            // Add condition inline
            if (_condAddBranchIndex == index)
            {
                EditorGUILayout.BeginHorizontal();
                _condAddHand = EditorGUILayout.Popup(_condAddHand, _handNames, GUILayout.Width(70));
                _condAddOp = EditorGUILayout.Popup(_condAddOp, _opNames, GUILayout.Width(70));
                _condAddGesture = EditorGUILayout.Popup(_condAddGesture, _gestureNames, GUILayout.Width(90));
                if (GUILayout.Button("Add", GUILayout.Width(35)))
                    AddConditionToScene(launcher, modeId, index);
                if (GUILayout.Button("x", GUILayout.Width(20)))
                    _condAddBranchIndex = -1;
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("+ Condition", EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    _condAddBranchIndex = index;
                    _condAddHand = 0; _condAddGesture = 0; _condAddOp = 0;
                }
            }
            EditorGUI.indentLevel--;

            // Tracking & Toggles
            SubLabel("Tracking & Toggles");
            EditorGUI.indentLevel++;
            if (isEditing)
            {
                _brEditEye = EditorGUILayout.Popup("EyeTrackingControl", _brEditEye, _trackingOpts);
                _brEditMouth = EditorGUILayout.Popup("MouthTrackingControl", _brEditMouth, _trackingOpts);
                _brEditBlink = EditorGUILayout.Toggle("BlinkEnabled", _brEditBlink);
                _brEditMouthCancel = EditorGUILayout.Toggle("MouthMorphCanceler", _brEditMouthCancel);
                _brEditLeftTrigger = EditorGUILayout.Toggle("IsLeftTriggerUsed", _brEditLeftTrigger);
                _brEditRightTrigger = EditorGUILayout.Toggle("IsRightTriggerUsed", _brEditRightTrigger);
                Row("IsReachable", branch.IsReachable.ToString());
            }
            else
            {
                Row("EyeTrackingControl", branch.EyeTrackingControl.ToString());
                Row("MouthTrackingControl", branch.MouthTrackingControl.ToString());
                Row("BlinkEnabled", branch.BlinkEnabled.ToString());
                Row("MouthMorphCanceler", branch.MouthMorphCancelerEnabled.ToString());
                Row("IsLeftTriggerUsed", branch.IsLeftTriggerUsed.ToString());
                Row("IsRightTriggerUsed", branch.IsRightTriggerUsed.ToString());
                Row("IsReachable", branch.IsReachable.ToString());
            }
            EditorGUI.indentLevel--;

            // Animations — all 4 slots
            SubLabel("Animations");
            EditorGUI.indentLevel++;
            if (isEditing)
            {
                _brEditBaseClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "Base", _brEditBaseClip, typeof(AnimationClip), false);
                _brEditLeftClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "Left Hand", _brEditLeftClip, typeof(AnimationClip), false);
                _brEditRightClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "Right Hand", _brEditRightClip, typeof(AnimationClip), false);
                _brEditBothClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "Both Hands", _brEditBothClip, typeof(AnimationClip), false);
            }
            else
            {
                DrawAnimRow("Base", branch.BaseAnimation);
                DrawAnimRow("Left Hand", branch.LeftHandAnimation);
                DrawAnimRow("Right Hand", branch.RightHandAnimation);
                DrawAnimRow("Both Hands", branch.BothHandsAnimation);
            }
            EditorGUI.indentLevel--;

            // Apply button (edit mode)
            if (isEditing)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Apply Changes", GUILayout.Height(22)))
                    ApplyBranchAllChanges(launcher, modeId, index);
            }

            EditorGUI.indentLevel--;
        }

        private void EnterBranchEditMode(int index, IBranch branch)
        {
            _editBranchIndex = index;
            _brEditEye = branch.EyeTrackingControl == EyeTrackingControl.Animation ? 1 : 0;
            _brEditMouth = branch.MouthTrackingControl == MouthTrackingControl.Animation ? 1 : 0;
            _brEditBlink = branch.BlinkEnabled;
            _brEditMouthCancel = branch.MouthMorphCancelerEnabled;
            _brEditLeftTrigger = branch.IsLeftTriggerUsed;
            _brEditRightTrigger = branch.IsRightTriggerUsed;
            _brEditBaseClip = LoadClipFromAnim(branch.BaseAnimation);
            _brEditLeftClip = LoadClipFromAnim(branch.LeftHandAnimation);
            _brEditRightClip = LoadClipFromAnim(branch.RightHandAnimation);
            _brEditBothClip = LoadClipFromAnim(branch.BothHandsAnimation);
        }

        private static AnimationClip LoadClipFromAnim(FaceEmoAnimation anim)
        {
            if (anim == null || string.IsNullOrEmpty(anim.GUID)) return null;
            string path = AssetDatabase.GUIDToAssetPath(anim.GUID);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        // ── Add Branch UI ──

        private void DrawAddBranchUI(FaceEmoLauncherComponent launcher, string modeId)
        {
            EditorGUILayout.LabelField("Conditions:", EditorStyles.miniLabel);

            // Show accumulated conditions
            for (int i = 0; i < _brAddConditions.Count; i++)
            {
                var c = _brAddConditions[i];
                string opStr = c.ComparisonOperator == ComparisonOperator.NotEqual ? "!=" : "=";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  {c.Hand} {opStr} {c.HandGesture}", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("x", GUILayout.Width(20)))
                {
                    _brAddConditions.RemoveAt(i);
                    Repaint();
                    return; // avoid modifying list during iteration
                }
                EditorGUILayout.EndHorizontal();
            }

            // Add condition row
            EditorGUILayout.BeginHorizontal();
            _brAddHand = EditorGUILayout.Popup(_brAddHand, _handNames, GUILayout.Width(70));
            _brAddOp = EditorGUILayout.Popup(_brAddOp, _opNames, GUILayout.Width(70));
            _brAddGesture = EditorGUILayout.Popup(_brAddGesture, _gestureNames, GUILayout.Width(90));
            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                var op = _brAddOp == 1 ? ComparisonOperator.NotEqual : ComparisonOperator.Equals;
                _brAddConditions.Add(new Condition(_handValues[_brAddHand], _gestureValues[_brAddGesture], op));
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            GUI.enabled = _brAddConditions.Count > 0;
            if (GUILayout.Button("Add Branch", GUILayout.Height(22)))
            {
                AddBranchToScene(launcher, modeId);
            }
            GUI.enabled = true;
        }

        // ── Animation helpers ──

        private static void DrawAnimField(FaceEmoAnimation anim)
        {
            if (anim == null)
            {
                Row("Clip", "None");
                return;
            }
            string guid = anim.GUID;
            string name = FaceEmoAPI.GuidToAnimName(guid);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Row("Clip", name);
            EditorGUI.indentLevel++;
            Row("GUID", string.IsNullOrEmpty(guid) ? "(empty)" : guid);
            Row("Path", string.IsNullOrEmpty(path) ? "(unresolved)" : path);
            EditorGUI.indentLevel--;
            // Thumbnail
            DrawAnimThumbnail(guid);
        }

        private static void DrawAnimRow(string label, FaceEmoAnimation anim)
        {
            if (anim == null)
            {
                Row(label, "None");
                return;
            }
            string guid = anim.GUID;
            string name = FaceEmoAPI.GuidToAnimName(guid);

            EditorGUILayout.BeginHorizontal();
            // Small thumbnail on the left
            var clip = LoadClipFromGuid(guid);
            if (clip != null)
            {
                var preview = AssetPreview.GetAssetPreview(clip);
                if (preview != null)
                {
                    var thumbRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);
                }
            }
            EditorGUILayout.LabelField($"{label}: {name}", GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawAnimThumbnail(string guid)
        {
            var clip = LoadClipFromGuid(guid);
            if (clip == null) return;
            var preview = AssetPreview.GetAssetPreview(clip);
            if (preview == null) return;
            var rect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64));
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
        }

        private static AnimationClip LoadClipFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        // ══════════════════════════════════════════════════════
        //  Settings Section
        // ══════════════════════════════════════════════════════

        private static readonly string[] _wdLabels = { "WD統一 (ギミック優先)", "WDオフ (表情優先)" };

        private void DrawSettingsSection(FaceEmoLauncherComponent launcher)
        {
            var av3 = launcher.AV3Setting;

            // WriteDefaults
            EditorGUILayout.LabelField("WriteDefaults", EditorStyles.miniBoldLabel);
            int wdIndex = av3.MatchAvatarWriteDefaults ? 0 : 1;
            int newWd = GUILayout.SelectionGrid(wdIndex, _wdLabels, 1, EditorStyles.radioButton);
            if (newWd != wdIndex)
            {
                Undo.RecordObject(av3, "FaceEmo Test WD");
                av3.MatchAvatarWriteDefaults = (newWd == 0);
                EditorUtility.SetDirty(av3);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Log($"WriteDefaults: {_wdLabels[newWd]}");
            }

            EditorGUILayout.Space(4);

            // Thumbnail generation toggle
            bool genThumb = EditorGUILayout.Toggle("Generate ExMenu Thumbnails", av3.GenerateExMenuThumbnails);
            if (genThumb != av3.GenerateExMenuThumbnails)
            {
                Undo.RecordObject(av3, "FaceEmo Test Thumbnail");
                av3.GenerateExMenuThumbnails = genThumb;
                EditorUtility.SetDirty(av3);
                Log($"GenerateExMenuThumbnails: {genThumb}");
            }

            EditorGUILayout.Space(4);

            // Import from FX Layer
            EditorGUILayout.LabelField("Import", EditorStyles.miniBoldLabel);
            if (!FaceEmoAPI.HasTargetAvatar(launcher))
            {
                EditorGUILayout.HelpBox("ターゲットアバターが未設定のため、Import できません。", MessageType.Warning);
                GUI.enabled = false;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import Expressions"))
                DoImport(launcher, "Expressions", FaceEmoAPI.ImportExpressionPatterns);
            if (GUILayout.Button("Import Optional"))
                DoImport(launcher, "Optional Clips", FaceEmoAPI.ImportOptionalClips);
            if (GUILayout.Button("Import All"))
                DoImport(launcher, "All", FaceEmoAPI.ImportAll);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            EditorGUILayout.Space(4);

            // Apply to Avatar
            EditorGUILayout.LabelField("Apply", EditorStyles.miniBoldLabel);
            if (!FaceEmoAPI.HasTargetAvatar(launcher))
            {
                EditorGUILayout.HelpBox("ターゲットアバターが未設定のため、Apply できません。", MessageType.Warning);
                GUI.enabled = false;
            }
            if (GUILayout.Button("Apply to Avatar", GUILayout.Height(24)))
            {
                ApplyToAvatar(launcher);
            }
            GUI.enabled = true;
        }

        private void ApplyToAvatar(FaceEmoLauncherComponent launcher)
        {
            if (!EditorUtility.DisplayDialog("FaceEmo Test",
                "Apply FaceEmo to avatar?\nThis will generate the FX layer.",
                "Apply", "Cancel"))
                return;

            Log("Applying to avatar...");
            string error = FaceEmoAPI.ApplyToAvatar(launcher);
            if (error == null)
                Log("Applied successfully.");
            else
                Log($"Error: {error}");
        }

        private void DoImport(FaceEmoLauncherComponent launcher, string label,
            System.Func<FaceEmoLauncherComponent, string> importFunc)
        {
            if (!EditorUtility.DisplayDialog("FaceEmo Test",
                $"Import {label} from avatar's FX layer?",
                "Import", "Cancel"))
                return;

            Log($"Importing {label}...");
            string result = importFunc(launcher);
            Log(result);
            LoadMenuFromLauncher(launcher);
        }

        // ══════════════════════════════════════════════════════
        //  Data Actions
        // ══════════════════════════════════════════════════════

        private void AddModeToScene(FaceEmoLauncherComponent launcher, string displayName, string dest)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }
            if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
            { Log($"Error: Cannot add to {dest} (full?)."); return; }

            string modeId = FaceEmoAPI.AddMode(menu, dest);
            FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: displayName);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Add");
            Log($"Added '{displayName}' to {dest} (id={modeId})");
            LoadMenuFromLauncher(launcher);
        }

        private void ApplyPropertyChanges(FaceEmoLauncherComponent launcher)
        {
            if (!HasSelection()) return;

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            string modeId = _modeIds[_selectedModeIndex];
            var mode = FaceEmoAPI.FindExpressionById(menu, modeId);
            if (mode == null) { Log($"Error: Mode not found."); return; }

            string newName = string.IsNullOrEmpty(_editDisplayName) ? null : _editDisplayName;
            EyeTrackingControl? eye = _editEyeTracking == 0 ? (EyeTrackingControl?)null
                : (_editEyeTracking == 1 ? EyeTrackingControl.Tracking : EyeTrackingControl.Animation);
            MouthTrackingControl? mouth = _editMouthTracking == 0 ? (MouthTrackingControl?)null
                : (_editMouthTracking == 1 ? MouthTrackingControl.Tracking : MouthTrackingControl.Animation);
            bool? blink = _editBlink == 0 ? (bool?)null : (_editBlink == 1);
            bool? mouthCancel = _editMouthCancel == 0 ? (bool?)null : (_editMouthCancel == 1);
            bool? changeDefaultFace = _editChangeDefaultFace == 0 ? (bool?)null : (_editChangeDefaultFace == 1);
            bool? useAnimName = _editUseAnimName == 0 ? (bool?)null : (_editUseAnimName == 1);

            bool hasPropertyChanges = newName != null || eye.HasValue || mouth.HasValue ||
                blink.HasValue || mouthCancel.HasValue || changeDefaultFace.HasValue || useAnimName.HasValue;

            if (!hasPropertyChanges && !_editSetAsDefault)
            { Log("No changes specified."); return; }

            if (hasPropertyChanges)
            {
                FaceEmoAPI.ModifyModeProperties(menu, modeId, displayName: newName,
                    changeDefaultFace: changeDefaultFace,
                    useAnimationNameAsDisplayName: useAnimName,
                    eyeTrackingControl: eye,
                    mouthTrackingControl: mouth, blinkEnabled: blink, mouthMorphCancelerEnabled: mouthCancel);
            }

            if (_editSetAsDefault && FaceEmoAPI.CanSetDefaultSelectionTo(menu, modeId))
            {
                FaceEmoAPI.SetDefaultSelection(menu, modeId);
            }

            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Edit");

            var changes = new List<string>();
            if (newName != null) changes.Add($"name='{newName}'");
            if (eye.HasValue) changes.Add($"eye={eye}");
            if (mouth.HasValue) changes.Add($"mouth={mouth}");
            if (blink.HasValue) changes.Add($"blink={blink}");
            if (mouthCancel.HasValue) changes.Add($"mouthCancel={mouthCancel}");
            if (changeDefaultFace.HasValue) changes.Add($"changeDefaultFace={changeDefaultFace}");
            if (useAnimName.HasValue) changes.Add($"useAnimName={useAnimName}");
            if (_editSetAsDefault) changes.Add("setAsDefault");
            Log($"Modified: {string.Join(", ", changes)}");
            _editSetAsDefault = false;
            LoadMenuFromLauncher(launcher);
        }

        private void AssignAnimation(FaceEmoLauncherComponent launcher)
        {
            if (_editAnimClip == null) { Log("Error: No clip selected."); return; }
            if (!HasSelection()) return;

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            string modeId = _modeIds[_selectedModeIndex];
            string guid = FaceEmoAPI.ClipToGuid(_editAnimClip);
            if (string.IsNullOrEmpty(guid)) { Log("Error: Invalid clip."); return; }

            FaceEmoAPI.SetModeAnimation(menu, new FaceEmoAnimation(guid), modeId);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Assign Anim");
            Log($"Assigned '{_editAnimClip.name}' to {_modeNames[_selectedModeIndex]}");
            LoadMenuFromLauncher(launcher);
        }

        private void ClearAnimation(FaceEmoLauncherComponent launcher)
        {
            if (!HasSelection()) return;

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            string modeId = _modeIds[_selectedModeIndex];
            FaceEmoAPI.SetModeAnimation(menu, null, modeId);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Clear Anim");
            Log($"Cleared animation from {_modeNames[_selectedModeIndex]}");
            LoadMenuFromLauncher(launcher);
        }

        // ══════════════════════════════════════════════════════
        //  Branch Data Actions
        // ══════════════════════════════════════════════════════

        private void AddBranchToScene(FaceEmoLauncherComponent launcher, string modeId)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            int newIndex = FaceEmoAPI.AddBranch(menu, modeId, new List<Condition>(_brAddConditions));
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Add Branch");

            var condStr = string.Join(", ", _brAddConditions.Select(c =>
            {
                string op = c.ComparisonOperator == ComparisonOperator.NotEqual ? "!=" : "=";
                return $"{c.Hand}{op}{c.HandGesture}";
            }));
            Log($"Added branch[{newIndex}] ({condStr})");
            _brAddConditions.Clear();
            LoadMenuFromLauncher(launcher);
        }

        private void RemoveBranchFromScene(FaceEmoLauncherComponent launcher, string modeId, int branchIndex)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanRemoveBranch(menu, modeId, branchIndex))
            { Log($"Error: Cannot remove branch[{branchIndex}]."); return; }

            FaceEmoAPI.RemoveBranch(menu, modeId, branchIndex);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Remove Branch");
            Log($"Removed branch[{branchIndex}]");

            if (_editBranchIndex == branchIndex) _editBranchIndex = -1;
            else if (_editBranchIndex > branchIndex) _editBranchIndex--;
            if (_condAddBranchIndex == branchIndex) _condAddBranchIndex = -1;
            else if (_condAddBranchIndex > branchIndex) _condAddBranchIndex--;
            if (_condEditBranchIndex == branchIndex) { _condEditBranchIndex = -1; _condEditIndex = -1; }
            else if (_condEditBranchIndex > branchIndex) _condEditBranchIndex--;

            LoadMenuFromLauncher(launcher);
        }

        private void AddConditionToScene(FaceEmoLauncherComponent launcher, string modeId, int branchIndex)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanAddCondition(menu, modeId, branchIndex))
            { Log($"Error: Cannot add condition to branch[{branchIndex}]."); return; }

            var hand = _handValues[_condAddHand];
            var gesture = _gestureValues[_condAddGesture];
            var op = _condAddOp == 1 ? ComparisonOperator.NotEqual : ComparisonOperator.Equals;

            FaceEmoAPI.AddCondition(menu, modeId, branchIndex, hand, gesture, op);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Add Condition");

            string opStr = op == ComparisonOperator.NotEqual ? "!=" : "=";
            Log($"Added condition: {hand}{opStr}{gesture} to branch[{branchIndex}]");
            _condAddBranchIndex = -1;
            LoadMenuFromLauncher(launcher);
        }

        private void RemoveConditionFromScene(FaceEmoLauncherComponent launcher, string modeId, int branchIndex, int conditionIndex)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanRemoveCondition(menu, modeId, branchIndex, conditionIndex))
            { Log($"Error: Cannot remove condition[{conditionIndex}] from branch[{branchIndex}]."); return; }

            FaceEmoAPI.RemoveCondition(menu, modeId, branchIndex, conditionIndex);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Remove Condition");
            Log($"Removed condition[{conditionIndex}] from branch[{branchIndex}]");

            if (_condEditBranchIndex == branchIndex && _condEditIndex == conditionIndex)
            { _condEditBranchIndex = -1; _condEditIndex = -1; }
            else if (_condEditBranchIndex == branchIndex && _condEditIndex > conditionIndex)
                _condEditIndex--;

            LoadMenuFromLauncher(launcher);
        }

        private void ModifyConditionInScene(FaceEmoLauncherComponent launcher, string modeId, int branchIndex, int conditionIndex)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanModifyCondition(menu, modeId, branchIndex, conditionIndex))
            { Log($"Error: Cannot modify condition[{conditionIndex}] in branch[{branchIndex}]."); return; }

            var hand = _handValues[_condEditHand];
            var gesture = _gestureValues[_condEditGesture];
            var op = _condEditOp == 1 ? ComparisonOperator.NotEqual : ComparisonOperator.Equals;
            var newCond = new Condition(hand, gesture, op);

            FaceEmoAPI.ModifyCondition(menu, modeId, branchIndex, conditionIndex, newCond);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Modify Condition");

            string opStr = op == ComparisonOperator.NotEqual ? "!=" : "=";
            Log($"Modified condition[{conditionIndex}] in branch[{branchIndex}]: {hand}{opStr}{gesture}");
            _condEditBranchIndex = -1; _condEditIndex = -1;
            LoadMenuFromLauncher(launcher);
        }

        private void MoveConditionInScene(FaceEmoLauncherComponent launcher, string modeId, int branchIndex, int from, int to)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanChangeConditionOrder(menu, modeId, branchIndex, from))
            { Log($"Error: Cannot reorder condition[{from}] in branch[{branchIndex}]."); return; }

            FaceEmoAPI.ChangeConditionOrder(menu, modeId, branchIndex, from, to);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Move Condition");
            Log($"Moved condition[{from}] -> [{to}] in branch[{branchIndex}]");

            // Update edit index tracking
            if (_condEditBranchIndex == branchIndex)
            {
                if (_condEditIndex == from) _condEditIndex = to;
                else if (from < to && _condEditIndex > from && _condEditIndex <= to) _condEditIndex--;
                else if (from > to && _condEditIndex >= to && _condEditIndex < from) _condEditIndex++;
            }

            LoadMenuFromLauncher(launcher);
        }

        private void MoveBranchInScene(FaceEmoLauncherComponent launcher, string modeId, int from, int to)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanChangeBranchOrder(menu, modeId, from))
            { Log($"Error: Cannot reorder branch[{from}]."); return; }

            FaceEmoAPI.ChangeBranchOrder(menu, modeId, from, to);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Move Branch");
            Log($"Moved branch[{from}] -> [{to}]");

            // Update edit index tracking
            if (_editBranchIndex == from) _editBranchIndex = to;
            else if (from < to && _editBranchIndex > from && _editBranchIndex <= to) _editBranchIndex--;
            else if (from > to && _editBranchIndex >= to && _editBranchIndex < from) _editBranchIndex++;

            if (_condAddBranchIndex == from) _condAddBranchIndex = to;
            if (_condEditBranchIndex == from) _condEditBranchIndex = to;

            LoadMenuFromLauncher(launcher);
        }

        private void ApplyBranchAllChanges(FaceEmoLauncherComponent launcher, string modeId, int branchIndex)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanModifyBranchProperties(menu, modeId, branchIndex))
            { Log($"Error: Cannot modify branch[{branchIndex}]."); return; }

            // ── Apply properties ──
            var eye = _brEditEye == 0 ? EyeTrackingControl.Tracking : EyeTrackingControl.Animation;
            var mouth = _brEditMouth == 0 ? MouthTrackingControl.Tracking : MouthTrackingControl.Animation;

            FaceEmoAPI.ModifyBranchProperties(menu, modeId, branchIndex,
                eyeTrackingControl: eye,
                mouthTrackingControl: mouth,
                blinkEnabled: _brEditBlink,
                mouthMorphCancelerEnabled: _brEditMouthCancel,
                isLeftTriggerUsed: _brEditLeftTrigger,
                isRightTriggerUsed: _brEditRightTrigger);

            // ── Apply animations (all 4 slots) ──
            SetSlotFromClip(menu, modeId, branchIndex, BranchAnimationType.Base, _brEditBaseClip);
            SetSlotFromClip(menu, modeId, branchIndex, BranchAnimationType.Left, _brEditLeftClip);
            SetSlotFromClip(menu, modeId, branchIndex, BranchAnimationType.Right, _brEditRightClip);
            SetSlotFromClip(menu, modeId, branchIndex, BranchAnimationType.Both, _brEditBothClip);

            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Edit Branch");
            Log($"Applied all changes to branch[{branchIndex}]: eye={eye}, mouth={mouth}, " +
                $"blink={_brEditBlink}, mouthCancel={_brEditMouthCancel}, " +
                $"leftTrigger={_brEditLeftTrigger}, rightTrigger={_brEditRightTrigger}, " +
                $"base={ClipName(_brEditBaseClip)}, left={ClipName(_brEditLeftClip)}, " +
                $"right={ClipName(_brEditRightClip)}, both={ClipName(_brEditBothClip)}");
            _editBranchIndex = -1;
            LoadMenuFromLauncher(launcher);
        }

        private static void SetSlotFromClip(FaceEmoMenu menu, string modeId, int branchIndex,
            BranchAnimationType slot, AnimationClip clip)
        {
            if (clip != null)
            {
                string guid = FaceEmoAPI.ClipToGuid(clip);
                if (!string.IsNullOrEmpty(guid))
                    FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIndex, slot, new FaceEmoAnimation(guid));
            }
            else
            {
                FaceEmoAPI.SetBranchAnimation(menu, modeId, branchIndex, slot, null);
            }
        }

        private static string ClipName(AnimationClip clip) => clip != null ? clip.name : "(none)";

        // ══════════════════════════════════════════════════════
        //  Launcher / Menu Helpers
        // ══════════════════════════════════════════════════════

        private void RefreshLaunchers()
        {
            _launchers = FaceEmoAPI.FindAllLaunchers();
            _launcherNames = _launchers.Select(l =>
            {
                string av = (l.AV3Setting != null && l.AV3Setting.TargetAvatar != null)
                    ? l.AV3Setting.TargetAvatar.gameObject.name : "None";
                return $"{l.gameObject.name} ({av})";
            }).ToArray();
            if (_selectedLauncherIndex >= _launchers.Length) _selectedLauncherIndex = 0;
            _cachedMenu = null; _modeNames = null; _modeIds = null;
        }

        private FaceEmoLauncherComponent GetSelectedLauncher()
        {
            if (_launchers == null || _launchers.Length == 0) return null;
            if (_selectedLauncherIndex < 0 || _selectedLauncherIndex >= _launchers.Length) return null;
            return _launchers[_selectedLauncherIndex];
        }

        private void LoadMenuFromLauncher(FaceEmoLauncherComponent launcher)
        {
            _cachedMenu = FaceEmoAPI.LoadMenu(launcher);
            if (_cachedMenu == null)
            {
                Log("Error: Could not load menu.");
                _modeNames = null; _modeIds = null;
                return;
            }

            _modeNames = new List<string>();
            _modeIds = new List<string>();

            var allExpressions = FaceEmoAPI.GetAllExpressions(_cachedMenu);
            foreach (var (id, mode, prefix) in allExpressions)
            {
                _modeNames.Add($"[{prefix}] {mode.DisplayName}");
                _modeIds.Add(id);
            }

            // Tree items
            _menuItems = FaceEmoAPI.GetAllMenuItems(_cachedMenu);

            // Destination list for move
            BuildDestinationList();

            if (_selectedModeIndex >= _modeNames.Count) _selectedModeIndex = 0;
            // Restore selection by ID
            if (_selectedItemId != null)
            {
                int idx = _modeIds.IndexOf(_selectedItemId);
                if (idx >= 0) _selectedModeIndex = idx;
                else _selectedItemId = null;
            }
            Log($"Loaded: {_modeNames.Count} modes (R={_cachedMenu.Registered.Order.Count}, U={_cachedMenu.Unregistered.Order.Count})");
        }

        // ══════════════════════════════════════════════════════
        //  Tree View
        // ══════════════════════════════════════════════════════

        private void DrawMenuItemTree(FaceEmoLauncherComponent launcher, string rootPrefix)
        {
            if (_menuItems == null) return;

            foreach (var item in _menuItems)
            {
                if (item.RootPrefix != rootPrefix) continue;

                // Indent by depth
                float indent = item.Depth * 16f;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent);

                bool isMode = item.Type == MenuItemType.Mode;
                bool isSelected = isMode && _selectedItemId == item.Id;
                bool isSelectedGroup = !isMode && _selectedItemId == item.Id;

                // Icon prefix
                string icon = isMode ? "\u25cf " : "\u25b6 ";  // ● or ▶
                string label = $"{icon}{item.DisplayName}";

                var r = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));
                if (isSelected || isSelectedGroup)
                    EditorGUI.DrawRect(r, new Color(0.24f, 0.48f, 0.90f, 0.3f));
                GUI.Label(r, label, EditorStyles.label);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedItemId = item.Id;
                    if (isMode)
                    {
                        int idx = _modeIds.IndexOf(item.Id);
                        if (idx >= 0) _selectedModeIndex = idx;
                    }
                    else
                    {
                        // Initialize group edit name
                        _editGroupName = item.DisplayName;
                    }
                    _editBranchIndex = -1;
                    _condEditBranchIndex = -1; _condEditIndex = -1;
                    _condAddBranchIndex = -1;
                    Repaint();
                    Event.current.Use();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void BuildDestinationList()
        {
            var names = new List<string>();
            var ids = new List<string>();

            names.Add("Registered"); ids.Add(FaceEmoMenu.RegisteredId);
            names.Add("Unregistered"); ids.Add(FaceEmoMenu.UnregisteredId);

            if (_menuItems != null)
            {
                foreach (var item in _menuItems)
                {
                    if (item.Type == MenuItemType.Group)
                    {
                        string indent = new string(' ', item.Depth * 2);
                        names.Add($"{indent}[{item.RootPrefix}] {item.DisplayName}");
                        ids.Add(item.Id);
                    }
                }
            }

            _destNames = names.ToArray();
            _destIds = ids.ToArray();
            if (_moveDestIndex >= _destNames.Length) _moveDestIndex = 0;
        }

        // ══════════════════════════════════════════════════════
        //  Group / Move Data Actions
        // ══════════════════════════════════════════════════════

        private void AddGroupToScene(FaceEmoLauncherComponent launcher, string displayName, string dest)
        {
            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }
            if (!FaceEmoAPI.CanAddMenuItemTo(menu, dest))
            { Log($"Error: Cannot add to {dest} (full?)."); return; }

            string groupId = FaceEmoAPI.AddGroup(menu, dest, displayName);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Add Group");
            Log($"Added group '{displayName}' (id={groupId})");
            LoadMenuFromLauncher(launcher);
        }

        private void RemoveItemFromScene(FaceEmoLauncherComponent launcher)
        {
            if (_selectedItemId == null) return;

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            if (!FaceEmoAPI.CanRemoveMenuItem(menu, _selectedItemId))
            { Log($"Error: Cannot remove '{_selectedItemId}'."); return; }

            // Find display name for log
            string displayName = _selectedItemId;
            if (_menuItems != null)
            {
                var found = _menuItems.FirstOrDefault(m => m.Id == _selectedItemId);
                if (found.Id != null) displayName = found.DisplayName;
            }

            FaceEmoAPI.RemoveMenuItem(menu, _selectedItemId);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Remove");
            Log($"Removed '{displayName}' (id={_selectedItemId})");
            _selectedItemId = null;
            _editBranchIndex = -1;
            LoadMenuFromLauncher(launcher);
        }

        private void MoveItemInScene(FaceEmoLauncherComponent launcher)
        {
            if (_selectedItemId == null || _destIds == null || _moveDestIndex >= _destIds.Length) return;

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null) { Log("Error: Could not load menu."); return; }

            string dest = _destIds[_moveDestIndex];
            var ids = new List<string> { _selectedItemId };

            if (!FaceEmoAPI.CanMoveMenuItemTo(menu, ids, dest))
            { Log($"Error: Cannot move to '{_destNames[_moveDestIndex]}'."); return; }

            FaceEmoAPI.MoveMenuItem(menu, ids, dest);
            FaceEmoAPI.SaveMenu(launcher, menu, "FaceEmo Test Move");
            Log($"Moved '{_selectedItemId}' → {_destNames[_moveDestIndex]}");
            LoadMenuFromLauncher(launcher);
        }

        // ══════════════════════════════════════════════════════
        //  UI Helpers
        // ══════════════════════════════════════════════════════

        private bool HasSelection()
        {
            return _modeIds != null && _selectedModeIndex >= 0 && _selectedModeIndex < _modeIds.Count;
        }

        private void Log(string msg)
        {
            _log.Add($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
            Debug.Log($"[FaceEmoTest] {msg}");
            Repaint();
        }

        private static GUIStyle _richLabelStyle;
        private static GUIStyle RichLabel
        {
            get
            {
                if (_richLabelStyle == null)
                {
                    _richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true };
                }
                return _richLabelStyle;
            }
        }

        private static string ColorizeValue(string value)
        {
            if (value == "True")  return "<color=#4CAF50><b>TRUE</b></color>";
            if (value == "False") return "<color=#F44336><b>FALSE</b></color>";
            return value;
        }

        private static void Row(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(160));
            EditorGUILayout.LabelField(ColorizeValue(value), RichLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void SectionLabel(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        private static void SubLabel(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel);
        }

        private static void Separator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space(2);
        }
    }
}
#endif
