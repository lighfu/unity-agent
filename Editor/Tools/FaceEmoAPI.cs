#if FACE_EMO
using Suzuryg.FaceEmo.Domain;
using Suzuryg.FaceEmo.Components;
using Suzuryg.FaceEmo.Components.Data;
using Suzuryg.FaceEmo.Components.Settings;
using FaceEmoMenu = Suzuryg.FaceEmo.Domain.Menu;
using FaceEmoAnimation = Suzuryg.FaceEmo.Domain.Animation;
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Object = UnityEngine.Object;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// FaceEmo シーンデータ操作の共有API。
    /// FaceEmoAdvancedTools, FaceEmoTestWindow, および将来のツールすべてがこのAPIを利用する。
    /// </summary>
    /// <remarks>
    /// <para><b>概要</b></para>
    /// <para>
    /// FaceEmo (<c>jp.suzuryg.face-emo</c>) のドメインモデルへの薄いファサード。
    /// Launcher検索、Menu読み書き、表情/グループ/ブランチ/コンディション操作、
    /// AV3設定アクセス、アバターへの適用を提供する。
    /// </para>
    ///
    /// <para><b>条件付きコンパイル</b></para>
    /// <para>
    /// <c>#if FACE_EMO</c> で囲まれており、FaceEmoパッケージが存在する時のみコンパイルされる。
    /// asmdef の versionDefines で <c>jp.suzuryg.face-emo</c> → <c>FACE_EMO</c> が定義される。
    /// </para>
    ///
    /// <para><b>依存関係</b></para>
    /// <para>
    /// asmdef 参照は <c>jp.suzuryg.face-emo.domain.Runtime</c> と
    /// <c>jp.suzuryg.face-emo.components.Runtime</c> のみ。
    /// <c>appmain.Editor</c>（FaceEmoInstaller/Zenject）や <c>usecase.Runtime</c> は
    /// 参照せず、<see cref="ApplyToAvatar"/> ではリフレクションで呼び出す。
    /// </para>
    ///
    /// <para><b>ドメインモデル</b></para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Menu</b> — ルート。Registered（最大7個）と Unregistered（無制限）の2つのMenuItemListを持つ。
    ///     <c>Menu.RegisteredId = "Registered"</c>, <c>Menu.UnregisteredId = "UnRegistered"</c>（大文字R注意）
    ///   </description></item>
    ///   <item><description>
    ///     <b>Mode</b> — 1つの表情パターン。DisplayName, Animation, Branches[], トラッキング設定を持つ。
    ///   </description></item>
    ///   <item><description>
    ///     <b>Group</b> — サブメニュー。IMenuItemList を実装し、Mode や 入れ子の Group を格納できる。
    ///   </description></item>
    ///   <item><description>
    ///     <b>Branch</b> — ジェスチャー分岐。Conditions（AND結合）と4つのアニメーションスロット
    ///     (Base, Left, Right, Both) を持つ。
    ///   </description></item>
    ///   <item><description>
    ///     <b>Condition</b> — 1つの判定条件。Hand × HandGesture × ComparisonOperator (Equals/NotEqual)。
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>基本的な使用パターン</b></para>
    /// <code>
    /// // 1. Launcher を見つける
    /// var launcher = FaceEmoAPI.FindLauncher();
    /// if (launcher == null) return "Not found." + FaceEmoAPI.GetLauncherHint();
    ///
    /// // 2. Menu をロード
    /// var menu = FaceEmoAPI.LoadMenu(launcher);
    ///
    /// // 3. ドメイン操作（直接 menu.XXX() を呼んでも、API ラッパーを使っても可）
    /// menu.ModifyModeProperties(modeId, displayName: "新しい名前");
    /// FaceEmoAPI.AddBranch(menu, modeId, conditions);
    ///
    /// // 4. 保存（Undo登録 + SO子オブジェクトクリーンアップ + SetDirty）
    /// FaceEmoAPI.SaveMenu(launcher, menu, "Undo Label");
    ///
    /// // ※ FaceEmo ウィンドウが開いている場合は再起動が必要
    /// </code>
    ///
    /// <para><b>注意事項</b></para>
    /// <list type="bullet">
    ///   <item><description>
    ///     LoadMenu → 操作 → SaveMenu は毎回セットで行う。LoadMenu で返る Menu は
    ///     シリアライズデータからの復元コピーであり、Save しないと変更は破棄される。
    ///   </description></item>
    ///   <item><description>
    ///     SaveMenu は内部で古い SerializableMenu の子 ScriptableObject を DestroyImmediate し、
    ///     新しいデータで上書きする。これは FaceEmo 自体のリポジトリパターンと同じ。
    ///   </description></item>
    ///   <item><description>
    ///     FaceEmo ウィンドウが開いている状態でデータを変更すると、ウィンドウ側のキャッシュと
    ///     不整合が生じる。変更後は FaceEmo ウィンドウの再起動を推奨する。
    ///   </description></item>
    ///   <item><description>
    ///     ApplyToAvatar はリフレクション経由のため、FaceEmo のバージョンアップで
    ///     内部クラス名が変わると動作しなくなる可能性がある。
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>API カテゴリ一覧</b></para>
    /// <list type="table">
    ///   <listheader><term>カテゴリ</term><description>メソッド</description></listheader>
    ///   <item><term>Launcher Discovery</term>
    ///     <description>FindLauncher, FindAllLaunchers, GetLauncherHint</description></item>
    ///   <item><term>Menu Load/Save</term>
    ///     <description>LoadMenu, SaveMenu</description></item>
    ///   <item><term>Expression Query</term>
    ///     <description>FindExpression, FindExpressionById, GetAllExpressions</description></item>
    ///   <item><term>Group/Destination</term>
    ///     <description>ResolveDestination, FindGroupByName, AddGroup, ModifyGroupProperties,
    ///     GetAllMenuItems, CanMoveMenuItemTo, MoveMenuItem, CopyMode, CopyGroup</description></item>
    ///   <item><term>Enum Parsing</term>
    ///     <description>ParseGesture, ParseHand, ParseConditions, ParseBranchSlot</description></item>
    ///   <item><term>Branch Management</term>
    ///     <description>AddBranch, CanRemoveBranch, RemoveBranch, CanModifyBranchProperties,
    ///     ModifyBranchProperties, SetBranchAnimation, CanChangeBranchOrder, ChangeBranchOrder,
    ///     CopyBranch</description></item>
    ///   <item><term>Condition Management</term>
    ///     <description>CanAddCondition, AddCondition, CanModifyCondition, ModifyCondition,
    ///     CanRemoveCondition, RemoveCondition, CanChangeConditionOrder, ChangeConditionOrder</description></item>
    ///   <item><term>Asset Utils</term>
    ///     <description>GuidToAnimName, ClipToGuid</description></item>
    ///   <item><term>AV3 Settings</term>
    ///     <description>GetAV3Setting, GetAV3SettingSO</description></item>
    ///   <item><term>Apply</term>
    ///     <description>ApplyToAvatar</description></item>
    ///   <item><term>Import</term>
    ///     <description>ImportAll, ImportExpressionPatterns, ImportOptionalClips</description></item>
    ///   <item><term>UI/Messages</term>
    ///     <description>WindowWarning</description></item>
    /// </list>
    ///
    /// <para><b>API がラップしていないドメイン操作</b></para>
    /// <para>
    /// 以下の Menu ドメインメソッドはラッパーを設けていないが、LoadMenu 後に
    /// <c>menu.XXX()</c> で直接呼び出せる:
    /// ModifyModeProperties, SetDefaultSelection, CanSetDefaultSelectionTo,
    /// AddMode, RemoveMenuItem, CanRemoveMenuItem, CanAddMenuItemTo,
    /// SetAnimation (Mode レベル), ContainsMode, ContainsGroup, GetMode, GetGroup。
    /// </para>
    /// </remarks>
    public static class FaceEmoAPI
    {
        // ═══════════════════════════════════════════
        //  Launcher Discovery
        // ═══════════════════════════════════════════

        /// <summary>
        /// アクティブシーンから FaceEmoLauncherComponent を検索する。
        /// </summary>
        /// <param name="gameObjectName">
        /// 空文字列の場合は自動検出（"FaceEmo*" ルートオブジェクト優先 → FindObjectOfType フォールバック）。
        /// 指定時はその名前の GameObject から取得する。
        /// </param>
        /// <returns>見つかった Launcher。見つからない場合は null。</returns>
        public static FaceEmoLauncherComponent FindLauncher(string gameObjectName = "")
        {
            if (!string.IsNullOrEmpty(gameObjectName))
            {
                var go = MeshAnalysisTools.FindGameObject(gameObjectName);
                if (go == null) return null;
                return go.GetComponent<FaceEmoLauncherComponent>();
            }

            // Auto-find: prefer FaceEmo* named root objects
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root.name.StartsWith("FaceEmo", StringComparison.OrdinalIgnoreCase))
                {
                    var comp = root.GetComponent<FaceEmoLauncherComponent>();
                    if (comp != null) return comp;
                }
            }

            // Fallback: search all objects in scene
            return Object.FindObjectOfType<FaceEmoLauncherComponent>();
        }

        /// <summary>
        /// Find all FaceEmoLauncherComponents in the active scene.
        /// </summary>
        public static FaceEmoLauncherComponent[] FindAllLaunchers()
        {
            return Object.FindObjectsOfType<FaceEmoLauncherComponent>();
        }

        /// <summary>
        /// Hint message when launcher is not found, listing available FaceEmo objects in scene.
        /// </summary>
        public static string GetLauncherHint()
        {
            var launchers = Object.FindObjectsOfType<FaceEmoLauncherComponent>();
            if (launchers == null || launchers.Length == 0)
                return " No FaceEmo objects found in scene. Use ExecuteMenu('FaceEmo/New Menu') to create one.";

            var details = launchers.Select(l =>
            {
                string avatar = (l.AV3Setting != null && l.AV3Setting.TargetAvatar != null)
                    ? l.AV3Setting.TargetAvatar.gameObject.name : "None";
                return $"{l.gameObject.name} (Avatar={avatar})";
            });
            return $" Available FaceEmo objects: {string.Join(", ", details)}";
        }

        // ═══════════════════════════════════════════
        //  Menu Load / Save
        // ═══════════════════════════════════════════

        /// <summary>
        /// シーンの MenuRepositoryComponent から Menu ドメインオブジェクトをロードする。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <returns>
        /// デシリアライズされた Menu。MenuRepositoryComponent が無い場合や
        /// SerializableMenu が null の場合は null を返す。
        /// </returns>
        /// <remarks>
        /// 返される Menu はシリアライズデータのコピーであるため、
        /// 変更を永続化するには <see cref="SaveMenu"/> を呼ぶ必要がある。
        /// </remarks>
        public static FaceEmoMenu LoadMenu(FaceEmoLauncherComponent launcher)
        {
            var menuRepo = launcher.GetComponent<MenuRepositoryComponent>();
            if (menuRepo == null || menuRepo.SerializableMenu == null) return null;
            return menuRepo.SerializableMenu.Load();
        }

        /// <summary>
        /// Menu ドメインオブジェクトをシーンの MenuRepositoryComponent に保存する。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <param name="menu">保存する Menu。</param>
        /// <param name="undoLabel">Undo 履歴に表示されるラベル。</param>
        /// <returns>保存に成功した場合 true。MenuRepositoryComponent が見つからない場合 false。</returns>
        /// <remarks>
        /// 内部処理: Undo.RecordObject → 古い子 ScriptableObject の DestroyImmediate →
        /// SerializableMenu.Save → SetDirty。
        /// FaceEmo 本体と同じ SO クリーンアップパターンを踏襲している。
        /// </remarks>
        public static bool SaveMenu(FaceEmoLauncherComponent launcher, FaceEmoMenu menu,
            string undoLabel = "FaceEmo Edit")
        {
            var menuRepo = launcher.GetComponent<MenuRepositoryComponent>();
            if (menuRepo == null || menuRepo.SerializableMenu == null)
            {
                Debug.LogError("[FaceEmoAPI] SaveMenu failed: MenuRepositoryComponent or SerializableMenu is missing.");
                return false;
            }
            Undo.RecordObject(menuRepo, undoLabel);
            CleanupSerializableMenuChildren(menuRepo.SerializableMenu);
            menuRepo.SerializableMenu.Save(menu, isAsset: false);
            EditorUtility.SetDirty(menuRepo);
            RefreshWindowIfOpen(launcher);
            return true;
        }

        // ═══════════════════════════════════════════
        //  Expression Query
        // ═══════════════════════════════════════════

        /// <summary>
        /// 表示名で Mode を検索する。Registered → Unregistered の順、Group 内も再帰検索。
        /// </summary>
        /// <param name="menu">検索対象の Menu。</param>
        /// <param name="displayName">検索する表示名（完全一致）。</param>
        /// <returns>見つかった場合は (ID, Mode)。見つからない場合は (null, null)。</returns>
        public static (string id, IMode mode) FindExpression(FaceEmoMenu menu, string displayName)
        {
            var result = SearchMenuItemList(menu.Registered, displayName);
            if (result.id != null) return result;
            return SearchMenuItemList(menu.Unregistered, displayName);
        }

        /// <summary>
        /// ID で Mode を検索する。Registered → Unregistered の順、Group 内も再帰検索。
        /// </summary>
        /// <param name="menu">検索対象の Menu。</param>
        /// <param name="modeId">検索する Mode ID。</param>
        /// <returns>見つかった Mode。見つからない場合は null。</returns>
        public static IMode FindExpressionById(FaceEmoMenu menu, string modeId)
        {
            return SearchMenuItemListById(menu.Registered, modeId)
                ?? SearchMenuItemListById(menu.Unregistered, modeId);
        }

        /// <summary>
        /// 全 Mode を列挙する。Group 内も再帰的に収集される。
        /// </summary>
        /// <returns>各要素は (ID, Mode, prefix)。prefix は "R"(Registered) または "U"(Unregistered)。</returns>
        public static List<(string id, IMode mode, string prefix)> GetAllExpressions(FaceEmoMenu menu)
        {
            var result = new List<(string id, IMode mode, string prefix)>();
            CollectExpressions(menu.Registered, "R", result);
            CollectExpressions(menu.Unregistered, "U", result);
            return result;
        }

        // ═══════════════════════════════════════════
        //  Menu Item Operations
        // ═══════════════════════════════════════════

        /// <summary>
        /// 宛先にアイテムを追加可能か検証する。宛先が満杯の場合は false。
        /// </summary>
        public static bool CanAddMenuItemTo(FaceEmoMenu menu, string destination)
        {
            return menu.CanAddMenuItemTo(destination);
        }

        /// <summary>
        /// 宛先に Mode を追加する。
        /// </summary>
        /// <param name="menu">対象 Menu。</param>
        /// <param name="destination">追加先 ID（RegisteredId / UnregisteredId / Group ID）。</param>
        /// <returns>新しい Mode の ID。</returns>
        public static string AddMode(FaceEmoMenu menu, string destination)
        {
            return menu.AddMode(destination);
        }

        /// <summary>
        /// Mode のプロパティを変更する。null のパラメータは変更しない。
        /// </summary>
        public static void ModifyModeProperties(FaceEmoMenu menu, string modeId,
            string displayName = null,
            bool? changeDefaultFace = null,
            bool? useAnimationNameAsDisplayName = null,
            EyeTrackingControl? eyeTrackingControl = null,
            MouthTrackingControl? mouthTrackingControl = null,
            bool? blinkEnabled = null,
            bool? mouthMorphCancelerEnabled = null)
        {
            menu.ModifyModeProperties(modeId,
                displayName: displayName,
                changeDefaultFace: changeDefaultFace,
                useAnimationNameAsDisplayName: useAnimationNameAsDisplayName,
                eyeTrackingControl: eyeTrackingControl,
                mouthTrackingControl: mouthTrackingControl,
                blinkEnabled: blinkEnabled,
                mouthMorphCancelerEnabled: mouthMorphCancelerEnabled);
        }

        /// <summary>
        /// Mode をデフォルト表情に設定可能か検証する。
        /// </summary>
        public static bool CanSetDefaultSelectionTo(FaceEmoMenu menu, string modeId)
        {
            return menu.CanSetDefaultSelectionTo(modeId);
        }

        /// <summary>
        /// Mode をデフォルト表情に設定する。
        /// </summary>
        public static void SetDefaultSelection(FaceEmoMenu menu, string modeId)
        {
            menu.SetDefaultSelection(modeId);
        }

        /// <summary>
        /// メニューアイテム（Mode / Group）を削除可能か検証する。
        /// </summary>
        public static bool CanRemoveMenuItem(FaceEmoMenu menu, string id)
        {
            return menu.CanRemoveMenuItem(id);
        }

        /// <summary>
        /// メニューアイテム（Mode / Group）を削除する。Group の場合は中身も再帰的に削除される。
        /// </summary>
        public static void RemoveMenuItem(FaceEmoMenu menu, string id)
        {
            menu.RemoveMenuItem(id);
        }

        /// <summary>
        /// Mode レベルのアニメーションを設定する。null を渡すとクリアされる。
        /// </summary>
        public static void SetModeAnimation(FaceEmoMenu menu, FaceEmoAnimation anim, string modeId)
        {
            menu.SetAnimation(anim, modeId);
        }

        /// <summary>
        /// Mode が存在するか確認する。
        /// </summary>
        public static bool ContainsMode(FaceEmoMenu menu, string id)
        {
            return menu.ContainsMode(id);
        }

        /// <summary>
        /// Group が存在するか確認する。
        /// </summary>
        public static bool ContainsGroup(FaceEmoMenu menu, string id)
        {
            return menu.ContainsGroup(id);
        }

        // ═══════════════════════════════════════════
        //  Group / Destination
        // ═══════════════════════════════════════════

        /// <summary>
        /// 宛先文字列を ID に解決する。
        /// "Registered" → RegisteredId、"Unregistered" → UnregisteredId、
        /// グループ表示名 → Group ID。いずれにもマッチしなければ入力をそのまま ID として返す。
        /// </summary>
        public static string ResolveDestination(FaceEmoMenu menu, string destination)
        {
            if (string.IsNullOrEmpty(destination) || destination.Equals("Registered", StringComparison.OrdinalIgnoreCase))
                return FaceEmoMenu.RegisteredId;
            if (destination.Equals("Unregistered", StringComparison.OrdinalIgnoreCase))
                return FaceEmoMenu.UnregisteredId;

            var groupResult = FindGroupByName(menu, destination);
            if (groupResult != null) return groupResult;

            return destination; // Assume it's a raw ID
        }

        /// <summary>
        /// 表示名で Group ID を検索する。Registered → Unregistered、入れ子 Group も再帰検索。
        /// </summary>
        public static string FindGroupByName(FaceEmoMenu menu, string displayName)
        {
            return FindGroupInList(menu.Registered, displayName)
                ?? FindGroupInList(menu.Unregistered, displayName);
        }

        /// <summary>
        /// 宛先にグループを新規追加する。
        /// </summary>
        /// <param name="menu">対象 Menu。</param>
        /// <param name="destination">追加先 ID（RegisteredId / UnregisteredId / Group ID）。</param>
        /// <param name="displayName">グループの表示名。</param>
        /// <returns>新しいグループの ID。</returns>
        public static string AddGroup(FaceEmoMenu menu, string destination, string displayName = "NewGroup")
        {
            string groupId = menu.AddGroup(destination);
            menu.ModifyGroupProperties(groupId, displayName: displayName);
            return groupId;
        }

        /// <summary>
        /// Modify group display name.
        /// </summary>
        public static void ModifyGroupProperties(FaceEmoMenu menu, string groupId, string displayName)
        {
            menu.ModifyGroupProperties(groupId, displayName: displayName);
        }

        /// <summary>
        /// 全メニューアイテム（Mode + Group）をツリー順のフラットリストで返す。
        /// 各要素の Depth / RootPrefix / ParentId でツリー構造を再構築できる。
        /// </summary>
        /// <returns><see cref="MenuItemInfo"/> のリスト。Registered → Unregistered の順。</returns>
        public static List<MenuItemInfo> GetAllMenuItems(FaceEmoMenu menu)
        {
            var result = new List<MenuItemInfo>();
            CollectMenuItems(menu, menu.Registered, "R", 0, FaceEmoMenu.RegisteredId, result);
            CollectMenuItems(menu, menu.Unregistered, "U", 0, FaceEmoMenu.UnregisteredId, result);
            return result;
        }

        /// <summary>
        /// アイテムを宛先に移動可能か検証する（CanMoveMenuItemFrom + CanMoveMenuItemTo）。
        /// </summary>
        public static bool CanMoveMenuItemTo(FaceEmoMenu menu, List<string> ids, string destination)
        {
            return menu.CanMoveMenuItemFrom(ids) && menu.CanMoveMenuItemTo(ids, destination);
        }

        /// <summary>
        /// メニューアイテム（Mode / Group）を宛先に移動する。Registered ↔ Unregistered ↔ Group 間で移動可能。
        /// </summary>
        public static void MoveMenuItem(FaceEmoMenu menu, List<string> ids, string destination)
        {
            menu.MoveMenuItem(ids, destination);
        }

        /// <summary>
        /// Copy a mode to a destination. Returns new mode ID.
        /// </summary>
        public static string CopyMode(FaceEmoMenu menu, string modeId, string destination)
        {
            return menu.CopyMode(modeId, destination);
        }

        /// <summary>
        /// Copy a group to a destination. Returns new group ID.
        /// </summary>
        public static string CopyGroup(FaceEmoMenu menu, string groupId, string destination)
        {
            return menu.CopyGroup(groupId, destination);
        }

        /// <summary>
        /// ツリー表示用のメニューアイテム情報。
        /// <see cref="GetAllMenuItems"/> が返すフラットリストの各要素。
        /// </summary>
        public struct MenuItemInfo
        {
            /// <summary>Mode ID または Group ID。</summary>
            public string Id;
            /// <summary>Mode か Group か。</summary>
            public MenuItemType Type;
            /// <summary>Mode.DisplayName または Group.DisplayName。</summary>
            public string DisplayName;
            /// <summary>ツリー内の深さ（0 = Registered/Unregistered 直下）。</summary>
            public int Depth;
            /// <summary>"R"(Registered) または "U"(Unregistered)。どちらのルートに属するか。</summary>
            public string RootPrefix;
            /// <summary>直接の親の ID。RegisteredId / UnregisteredId / Group ID のいずれか。</summary>
            public string ParentId;
        }

        // ═══════════════════════════════════════════
        //  Enum Parsing
        // ═══════════════════════════════════════════

        /// <summary>
        /// Parse gesture string to HandGesture enum.
        /// Accepts: "Fist", "hand_open", "ThumbsUp", etc.
        /// </summary>
        public static HandGesture ParseGesture(string gesture)
        {
            switch (gesture.ToLower())
            {
                case "neutral": return HandGesture.Neutral;
                case "fist": return HandGesture.Fist;
                case "handopen": case "hand_open": return HandGesture.HandOpen;
                case "fingerpoint": case "finger_point": return HandGesture.Fingerpoint;
                case "victory": return HandGesture.Victory;
                case "rocknroll": case "rock_n_roll": return HandGesture.RockNRoll;
                case "handgun": case "hand_gun": return HandGesture.HandGun;
                case "thumbsup": case "thumbs_up": return HandGesture.ThumbsUp;
                default:
                    if (Enum.TryParse<HandGesture>(gesture, true, out var hg)) return hg;
                    throw new ArgumentException($"Unknown gesture: '{gesture}'");
            }
        }

        /// <summary>
        /// Parse hand string to Hand enum.
        /// Accepts: "Left", "Right", "Either", "Both", "OneSide", "one_side".
        /// </summary>
        public static Hand ParseHand(string hand)
        {
            switch (hand.ToLower())
            {
                case "left": return Hand.Left;
                case "right": return Hand.Right;
                case "either": return Hand.Either;
                case "both": return Hand.Both;
                case "oneside": case "one_side": return Hand.OneSide;
                default:
                    if (Enum.TryParse<Hand>(hand, true, out var h)) return h;
                    throw new ArgumentException($"Unknown hand: '{hand}'");
            }
        }

        /// <summary>
        /// Parse conditions string "Left=Fist;Right!=Victory" → List&lt;Condition&gt;.
        /// </summary>
        public static List<Condition> ParseConditions(string conditionsStr)
        {
            var condList = new List<Condition>();

            foreach (string part in conditionsStr.Split(';'))
            {
                string trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                ComparisonOperator op;
                string[] tokens;
                if (trimmed.Contains("!="))
                {
                    op = ComparisonOperator.NotEqual;
                    tokens = trimmed.Split(new[] { "!=" }, StringSplitOptions.None);
                }
                else if (trimmed.Contains("="))
                {
                    op = ComparisonOperator.Equals;
                    tokens = trimmed.Split('=');
                }
                else
                {
                    throw new ArgumentException($"Invalid condition format: '{trimmed}'. Expected 'Hand=Gesture' or 'Hand!=Gesture'.");
                }

                if (tokens.Length != 2)
                    throw new ArgumentException($"Invalid condition: '{trimmed}'.");

                Hand hand = ParseHand(tokens[0].Trim());
                HandGesture gesture = ParseGesture(tokens[1].Trim());
                condList.Add(new Condition(hand, gesture, op));
            }

            return condList;
        }

        // ═══════════════════════════════════════════
        //  Branch Management
        // ═══════════════════════════════════════════

        /// <summary>
        /// Mode にジェスチャー分岐 (Branch) を追加する。
        /// </summary>
        /// <param name="menu">対象 Menu。</param>
        /// <param name="modeId">追加先の Mode ID。</param>
        /// <param name="conditions">発火条件リスト（AND 結合）。</param>
        /// <returns>追加された Branch のインデックス。</returns>
        public static int AddBranch(FaceEmoMenu menu, string modeId, List<Condition> conditions)
        {
            var mode = FindExpressionById(menu, modeId);
            int prevCount = mode?.Branches?.Count ?? 0;
            menu.AddBranch(modeId, conditions);
            return prevCount; // newly added branch index
        }

        /// <summary>
        /// Check if a branch can be removed from a mode.
        /// </summary>
        public static bool CanRemoveBranch(FaceEmoMenu menu, string modeId, int branchIndex)
        {
            return menu.CanRemoveBranch(modeId, branchIndex);
        }

        /// <summary>
        /// Remove a gesture branch from a mode by index.
        /// </summary>
        public static void RemoveBranch(FaceEmoMenu menu, string modeId, int branchIndex)
        {
            menu.RemoveBranch(modeId, branchIndex);
        }

        /// <summary>
        /// Check if a condition can be added to a branch.
        /// </summary>
        public static bool CanAddCondition(FaceEmoMenu menu, string modeId, int branchIndex)
        {
            return menu.CanAddConditionTo(modeId, branchIndex);
        }

        /// <summary>
        /// Add a gesture condition to an existing branch.
        /// </summary>
        public static void AddCondition(FaceEmoMenu menu, string modeId, int branchIndex,
            Hand hand, HandGesture gesture, ComparisonOperator op = ComparisonOperator.Equals)
        {
            menu.AddCondition(modeId, branchIndex, new Condition(hand, gesture, op));
        }

        /// <summary>
        /// Check if branch properties can be modified.
        /// </summary>
        public static bool CanModifyBranchProperties(FaceEmoMenu menu, string modeId, int branchIndex)
        {
            return menu.CanModifyBranchProperties(modeId, branchIndex);
        }

        /// <summary>
        /// Branch のトラッキング・トリガー設定を変更する。null のパラメータは変更しない。
        /// </summary>
        public static void ModifyBranchProperties(FaceEmoMenu menu, string modeId, int branchIndex,
            EyeTrackingControl? eyeTrackingControl = null,
            MouthTrackingControl? mouthTrackingControl = null,
            bool? blinkEnabled = null,
            bool? mouthMorphCancelerEnabled = null,
            bool? isLeftTriggerUsed = null,
            bool? isRightTriggerUsed = null)
        {
            menu.ModifyBranchProperties(modeId, branchIndex,
                eyeTrackingControl: eyeTrackingControl,
                mouthTrackingControl: mouthTrackingControl,
                blinkEnabled: blinkEnabled,
                mouthMorphCancelerEnabled: mouthMorphCancelerEnabled,
                isLeftTriggerUsed: isLeftTriggerUsed,
                isRightTriggerUsed: isRightTriggerUsed);
        }

        /// <summary>
        /// Branch のアニメーションスロットにクリップを設定する。anim に null を渡すとクリアされる。
        /// </summary>
        /// <param name="branchType">スロット種別: Base, Left, Right, Both。</param>
        public static void SetBranchAnimation(FaceEmoMenu menu, string modeId, int branchIndex,
            BranchAnimationType branchType, FaceEmoAnimation anim)
        {
            menu.SetAnimation(anim, modeId, branchIndex, branchType);
        }

        /// <summary>
        /// スロット名文字列 ("Base" / "Left" / "Right" / "Both") → BranchAnimationType。
        /// マッチしない場合は null。
        /// </summary>
        public static BranchAnimationType? ParseBranchSlot(string slot)
        {
            switch (slot.ToLower())
            {
                case "base": return BranchAnimationType.Base;
                case "left": return BranchAnimationType.Left;
                case "right": return BranchAnimationType.Right;
                case "both": return BranchAnimationType.Both;
                default: return null;
            }
        }

        /// <summary>
        /// Check if branch order can be changed.
        /// </summary>
        public static bool CanChangeBranchOrder(FaceEmoMenu menu, string modeId, int from)
        {
            return menu.CanChangeBranchOrder(modeId, from);
        }

        /// <summary>
        /// Mode 内の Branch の順序を変更する。from → to に移動。
        /// </summary>
        public static void ChangeBranchOrder(FaceEmoMenu menu, string modeId, int from, int to)
        {
            menu.ChangeBranchOrder(modeId, from, to);
        }

        /// <summary>
        /// Check if a condition can be modified.
        /// </summary>
        public static bool CanModifyCondition(FaceEmoMenu menu, string modeId, int branchIndex, int conditionIndex)
        {
            return menu.CanModifyCondition(modeId, branchIndex, conditionIndex);
        }

        /// <summary>
        /// Modify an existing condition on a branch.
        /// </summary>
        public static void ModifyCondition(FaceEmoMenu menu, string modeId, int branchIndex, int conditionIndex,
            Condition condition)
        {
            menu.ModifyCondition(modeId, branchIndex, conditionIndex, condition);
        }

        /// <summary>
        /// Check if a condition can be removed.
        /// </summary>
        public static bool CanRemoveCondition(FaceEmoMenu menu, string modeId, int branchIndex, int conditionIndex)
        {
            return menu.CanRemoveCondition(modeId, branchIndex, conditionIndex);
        }

        /// <summary>
        /// Remove a condition from a branch.
        /// </summary>
        public static void RemoveCondition(FaceEmoMenu menu, string modeId, int branchIndex, int conditionIndex)
        {
            menu.RemoveCondition(modeId, branchIndex, conditionIndex);
        }

        /// <summary>
        /// Check if a condition can be reordered within a branch.
        /// </summary>
        public static bool CanChangeConditionOrder(FaceEmoMenu menu, string modeId, int branchIndex, int from)
        {
            return menu.CanChangeConditionOrder(modeId, branchIndex, from);
        }

        /// <summary>
        /// Branch 内の Condition の順序を変更する。from → to に移動。
        /// </summary>
        public static void ChangeConditionOrder(FaceEmoMenu menu, string modeId, int branchIndex, int from, int to)
        {
            menu.ChangeConditionOrder(modeId, branchIndex, from, to);
        }

        /// <summary>
        /// Branch を別の Mode（または同じ Mode）にコピーする。宛先 Mode の末尾に追加される。
        /// </summary>
        public static void CopyBranch(FaceEmoMenu menu, string srcModeId, int srcBranchIndex, string dstModeId)
        {
            menu.CopyBranch(srcModeId, srcBranchIndex, dstModeId);
        }

        // ═══════════════════════════════════════════
        //  Asset Utils
        // ═══════════════════════════════════════════

        /// <summary>
        /// GUID → AnimationClip のファイル名（拡張子なし）。
        /// アセットが見つからない場合は "(GUID:xxxxxxxx)" を返す。空/null なら "None"。
        /// </summary>
        public static string GuidToAnimName(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return "None";
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return $"(GUID:{guid.Substring(0, Mathf.Min(8, guid.Length))})";
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// AnimationClip → アセット GUID。clip が null なら null を返す。
        /// </summary>
        public static string ClipToGuid(AnimationClip clip)
        {
            if (clip == null) return null;
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip));
        }

        // ═══════════════════════════════════════════
        //  AV3 Settings
        // ═══════════════════════════════════════════

        /// <summary>
        /// Launcher から AV3Setting ScriptableObject を取得する。
        /// MatchAvatarWriteDefaults, GenerateExMenuThumbnails 等のプロパティに直接アクセス可能。
        /// </summary>
        public static AV3Setting GetAV3Setting(FaceEmoLauncherComponent launcher)
        {
            return launcher.AV3Setting;
        }

        /// <summary>
        /// Launcher にターゲットアバターが設定されているか確認する。
        /// </summary>
        /// <returns>AV3Setting と TargetAvatar の両方が非 null の場合 true。</returns>
        public static bool HasTargetAvatar(FaceEmoLauncherComponent launcher)
        {
            var av3 = launcher.AV3Setting;
            return av3 != null && av3.TargetAvatar != null;
        }

        /// <summary>
        /// AV3Setting の SerializedObject を取得する（SerializedProperty 経由のプロパティ読み書き用）。
        /// </summary>
        public static SerializedObject GetAV3SettingSO(FaceEmoLauncherComponent launcher)
        {
            if (launcher.AV3Setting == null) return null;
            return new SerializedObject(launcher.AV3Setting);
        }

        // ═══════════════════════════════════════════
        //  UI / Messages
        // ═══════════════════════════════════════════

        private const string AppMainEditorAssembly = "jp.suzuryg.face-emo.appmain.Editor";

        /// <summary>
        /// FaceEmo ウィンドウが開いていたら FaceEmoLauncher.Launch() で再起動する。
        /// SaveMenu から自動呼び出しされるため、通常は明示呼び出し不要。
        /// AV3Setting のみの変更（ConfigureTargetAvatar 等）で Menu 保存を伴わない場合に直接呼ぶ。
        /// </summary>
        public static void RefreshWindowIfOpen(FaceEmoLauncherComponent launcher)
        {
            try
            {
                var mainWindowType = Type.GetType(
                    $"Suzuryg.FaceEmo.AppMain.MainWindow, {AppMainEditorAssembly}");
                if (mainWindowType == null) return;

                var windows = Resources.FindObjectsOfTypeAll(mainWindowType);
                if (windows == null || windows.Length == 0) return;

                var launcherType = Type.GetType(
                    $"Suzuryg.FaceEmo.AppMain.FaceEmoLauncher, {AppMainEditorAssembly}");
                if (launcherType == null) return;

                var launchMethod = launcherType.GetMethod("Launch",
                    BindingFlags.Public | BindingFlags.Static);
                if (launchMethod == null) return;

                launchMethod.Invoke(null, new object[] { launcher });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FaceEmoAPI] Failed to refresh FaceEmo window: {ex.Message}");
            }
        }

        /// <summary>
        /// FaceEmo ウィンドウの更新に関する注記。SaveMenu が自動でウィンドウを再読み込みするため、
        /// 通常は手動リオープン不要。
        /// </summary>
        public static string WindowWarning()
        {
            return "";
        }

        // ═══════════════════════════════════════════
        //  Apply to Avatar (reflection-based)
        // ═══════════════════════════════════════════

        /// <summary>
        /// FaceEmo メニューをアバターに適用する（FX レイヤー生成）。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <returns>成功時は null。失敗時はエラーメッセージ文字列。</returns>
        /// <remarks>
        /// <para>
        /// 内部で FaceEmoInstaller（Zenject DI コンテナ）をリフレクション経由でインスタンス化し、
        /// IGenerateFxUseCase.Handle() を呼び出す。asmdef 参照を増やさないための設計。
        /// </para>
        /// <para>処理フロー: FaceEmoInstaller 作成 → IBackupper.SetName() → IGenerateFxUseCase.Prepare() → Handle()。</para>
        /// <para>Play Mode 中は実行不可。</para>
        /// </remarks>
        public static string ApplyToAvatar(FaceEmoLauncherComponent launcher)
        {
            if (EditorApplication.isPlaying)
                return "Cannot apply during Play Mode.";

            var av3 = GetAV3Setting(launcher);
            if (av3 == null)
                return "AV3Setting not found on FaceEmo launcher. FaceEmo may not be fully initialized.";
            if (av3.TargetAvatar == null)
                return "Target avatar is not set (Avatar=None). Use ConfigureTargetAvatar to set the target avatar first.";

            try
            {
                // FaceEmoInstaller(GameObject) — creates DI container
                var installerType = Type.GetType(
                    "Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, jp.suzuryg.face-emo.appmain.Editor");
                if (installerType == null)
                    return "FaceEmoInstaller type not found. Is FaceEmo installed?";

                var installer = Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container")?.GetValue(installer);
                if (container == null)
                    return "Could not access DI container.";

                // Resolve IGenerateFxUseCase
                var useCaseType = Type.GetType(
                    "Suzuryg.FaceEmo.UseCase.IGenerateFxUseCase, jp.suzuryg.face-emo.usecase.Runtime");
                if (useCaseType == null)
                    return "IGenerateFxUseCase type not found.";

                var resolveMethod = container.GetType()
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Resolve" && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 0);
                if (resolveMethod == null)
                    return "DiContainer.Resolve<T>() not found.";

                var useCase = resolveMethod.MakeGenericMethod(useCaseType).Invoke(container, null);
                if (useCase == null)
                    return "Failed to resolve IGenerateFxUseCase.";

                // Resolve IBackupper and set backup name (required before Handle)
                var backupperType = Type.GetType(
                    "Suzuryg.FaceEmo.UseCase.IBackupper, jp.suzuryg.face-emo.usecase.Runtime");
                if (backupperType != null)
                {
                    var backupper = resolveMethod.MakeGenericMethod(backupperType).Invoke(container, null);
                    if (backupper != null)
                    {
                        backupperType.GetMethod("SetName")?.Invoke(backupper, new object[] { launcher.gameObject.name });
                    }
                }

                // Prepare() → List<string>
                var paths = useCaseType.GetMethod("Prepare")?.Invoke(useCase, null) as List<string>
                    ?? new List<string>();

                // Handle("", paths)
                useCaseType.GetMethod("Handle")?.Invoke(useCase, new object[] { "", paths });

                return null; // success
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                return ex.InnerException?.Message ?? ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ═══════════════════════════════════════════
        //  Import from FX Layer (reflection-based)
        // ═══════════════════════════════════════════

        private const string DetailEditorAssembly = "jp.suzuryg.face-emo.detail.Editor";

        /// <summary>
        /// アバターの FX レイヤーから表情パターン・ブリンク・MouthMorphCancel・Contact・パラメータプレフィックスを
        /// 一括でインポートする。FaceEmoLauncher.ImportPatternsAndOptions() と同等の処理をリフレクション経由で実行。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <returns>成功時はサマリー文字列（パターン数、クリップ名、Contact数、Prefix有無）。失敗時は "Error: ..." 文字列。</returns>
        /// <remarks>
        /// <para>処理フロー:</para>
        /// <list type="number">
        ///   <item><description>TargetAvatar (VRCAvatarDescriptor) を取得</description></item>
        ///   <item><description>Menu をロードし、ExpressionImporter をリフレクションで生成</description></item>
        ///   <item><description>ImportExpressionPatterns — FX レイヤーの表情パターンを Mode として取り込む</description></item>
        ///   <item><description>ImportOptionalClips — Blink / MouthMorphCancel クリップを AV3Setting に設定</description></item>
        ///   <item><description>ContactSettingImporter.Import — VRCContactReceiver を AV3Setting に取り込む</description></item>
        ///   <item><description>FxParameterChecker.CheckPrefixNeeds — パラメータプレフィックスの要否を判定</description></item>
        ///   <item><description>SaveMenu + SetDirty(AV3Setting) で永続化</description></item>
        /// </list>
        /// <para>
        /// リフレクション対象は <c>jp.suzuryg.face-emo.detail.Editor</c> アセンブリ内の
        /// ExpressionImporter, ContactSettingImporter, FxParameterChecker, ImportUtility, LocalizationSetting。
        /// asmdef 参照を増やさない設計。
        /// </para>
        /// <para>Play Mode 中は実行不可。</para>
        /// </remarks>
        public static string ImportAll(FaceEmoLauncherComponent launcher)
        {
            var avatar = GetAvatarDescriptor(launcher, out var err);
            if (avatar == null) return err;

            var av3 = launcher.AV3Setting;
            var menu = LoadMenu(launcher);
            if (menu == null) return "Error: Failed to load FaceEmo menu.";

            var importer = CreateExpressionImporter(menu, av3, out err);
            if (importer == null) return err;

            try
            {
                var importerType = importer.GetType();

                // ImportExpressionPatterns
                var importPatternsMethod = importerType.GetMethod("ImportExpressionPatterns");
                if (importPatternsMethod == null) return "Error: ImportExpressionPatterns method not found.";
                var importedPatterns = importPatternsMethod.Invoke(importer, new object[] { avatar });
                int patternCount = (importedPatterns as System.Collections.IList)?.Count ?? 0;

                // ImportOptionalClips → (AnimationClip blink, AnimationClip mouthMorphCancel)
                var importClipsMethod = importerType.GetMethod("ImportOptionalClips");
                if (importClipsMethod == null) return "Error: ImportOptionalClips method not found.";
                var clipsResult = importClipsMethod.Invoke(importer, new object[] { avatar });
                string blinkName = null, mouthName = null;
                if (clipsResult != null)
                {
                    var blink = clipsResult.GetType().GetField("blink")?.GetValue(clipsResult) as AnimationClip;
                    var mouth = clipsResult.GetType().GetField("mouthMorphCancel")?.GetValue(clipsResult) as AnimationClip;
                    blinkName = blink != null ? blink.name : null;
                    mouthName = mouth != null ? mouth.name : null;
                }

                // ContactSettingImporter
                int contactCount = 0;
                var contactImporterType = Type.GetType(
                    $"Suzuryg.FaceEmo.Detail.AV3.Importers.ContactSettingImporter, {DetailEditorAssembly}");
                if (contactImporterType != null)
                {
                    var contactImporter = Activator.CreateInstance(contactImporterType, new object[] { av3 });
                    var importMethod = contactImporterType.GetMethod("Import");
                    if (importMethod != null)
                    {
                        var contacts = importMethod.Invoke(contactImporter, new object[] { avatar });
                        contactCount = (contacts as System.Collections.IList)?.Count ?? 0;
                    }
                }

                // FxParameterChecker.CheckPrefixNeeds
                bool needsPrefix = false;
                var checkerType = Type.GetType(
                    $"Suzuryg.FaceEmo.Detail.AV3.Importers.FxParameterChecker, {DetailEditorAssembly}");
                if (checkerType != null)
                {
                    var checkMethod = checkerType.GetMethod("CheckPrefixNeeds",
                        BindingFlags.Public | BindingFlags.Static);
                    if (checkMethod != null)
                    {
                        needsPrefix = (bool)checkMethod.Invoke(null, new object[] { avatar });
                        av3.AddParameterPrefix = needsPrefix;
                    }
                }

                // Save
                SaveMenu(launcher, menu, "Import Expressions");
                EditorUtility.SetDirty(av3);

                // Build summary
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Import completed.");
                sb.AppendLine($"  Patterns: {patternCount} mode(s) imported");
                if (blinkName != null) sb.AppendLine($"  Blink: {blinkName}");
                if (mouthName != null) sb.AppendLine($"  MouthMorphCancel: {mouthName}");
                if (contactCount > 0) sb.AppendLine($"  Contacts: {contactCount} receiver(s)");
                sb.AppendLine($"  ParameterPrefix: {(needsPrefix ? "Enabled" : "Disabled")}");
                sb.Append(WindowWarning());
                return sb.ToString();
            }
            catch (TargetInvocationException ex)
            {
                return $"Error: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// アバターの FX レイヤーから表情パターンのみをインポートする。
        /// FX Animator 内の各レイヤーを解析し、ジェスチャー分岐に対応する Mode/Branch を Menu に追加する。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <returns>成功時はインポート数を含むメッセージ。失敗時は "Error: ..." 文字列。</returns>
        /// <remarks>
        /// <para><see cref="ImportAll"/> のサブセット。表情パターンのみを取り込み、Menu を保存する。</para>
        /// <para>Blink/MouthMorphCancel/Contact/Prefix は変更しない。</para>
        /// </remarks>
        public static string ImportExpressionPatterns(FaceEmoLauncherComponent launcher)
        {
            var avatar = GetAvatarDescriptor(launcher, out var err);
            if (avatar == null) return err;

            var av3 = launcher.AV3Setting;
            var menu = LoadMenu(launcher);
            if (menu == null) return "Error: Failed to load FaceEmo menu.";

            var importer = CreateExpressionImporter(menu, av3, out err);
            if (importer == null) return err;

            try
            {
                var method = importer.GetType().GetMethod("ImportExpressionPatterns");
                if (method == null) return "Error: ImportExpressionPatterns method not found.";
                var imported = method.Invoke(importer, new object[] { avatar });
                int count = (imported as System.Collections.IList)?.Count ?? 0;

                SaveMenu(launcher, menu, "Import Expression Patterns");
                return $"Imported {count} expression pattern(s)." + WindowWarning();
            }
            catch (TargetInvocationException ex)
            {
                return $"Error: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// アバターの FX レイヤーからブリンク・MouthMorphCancel クリップのみをインポートする。
        /// ImportOptionalClips は AV3Setting のプロパティを副作用で変更するため、SetDirty を呼ぶ。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <returns>成功時はクリップ名を含むメッセージ。失敗時は "Error: ..." 文字列。</returns>
        /// <remarks>
        /// <para><see cref="ImportAll"/> のサブセット。Menu の保存は行わない（AV3Setting のみ SetDirty）。</para>
        /// </remarks>
        public static string ImportOptionalClips(FaceEmoLauncherComponent launcher)
        {
            var avatar = GetAvatarDescriptor(launcher, out var err);
            if (avatar == null) return err;

            var av3 = launcher.AV3Setting;
            var menu = LoadMenu(launcher);
            if (menu == null) return "Error: Failed to load FaceEmo menu.";

            var importer = CreateExpressionImporter(menu, av3, out err);
            if (importer == null) return err;

            try
            {
                var method = importer.GetType().GetMethod("ImportOptionalClips");
                if (method == null) return "Error: ImportOptionalClips method not found.";
                var result = method.Invoke(importer, new object[] { avatar });

                string blinkName = null, mouthName = null;
                if (result != null)
                {
                    var blink = result.GetType().GetField("blink")?.GetValue(result) as AnimationClip;
                    var mouth = result.GetType().GetField("mouthMorphCancel")?.GetValue(result) as AnimationClip;
                    blinkName = blink != null ? blink.name : null;
                    mouthName = mouth != null ? mouth.name : null;
                }

                EditorUtility.SetDirty(av3);

                var sb = new System.Text.StringBuilder("Optional clips imported.");
                if (blinkName != null) sb.Append($" Blink: {blinkName}.");
                if (mouthName != null) sb.Append($" MouthMorphCancel: {mouthName}.");
                if (blinkName == null && mouthName == null) sb.Append(" No clips found in FX layer.");
                return sb.ToString();
            }
            catch (TargetInvocationException ex)
            {
                return $"Error: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// av3Setting.TargetAvatar から VRCAvatarDescriptor (Component) を取得する。
        /// Play Mode チェック、AV3Setting null チェック、TargetAvatar null チェックを含む。
        /// </summary>
        /// <param name="launcher">対象の Launcher。</param>
        /// <param name="error">失敗時のエラーメッセージ。成功時は null。</param>
        /// <returns>VRCAvatarDescriptor (Component として返却)。失敗時は null。</returns>
        private static Component GetAvatarDescriptor(FaceEmoLauncherComponent launcher, out string error)
        {
            error = null;
            if (EditorApplication.isPlaying)
            {
                error = "Error: Cannot import during Play Mode.";
                return null;
            }

            var av3 = GetAV3Setting(launcher);
            if (av3 == null)
            {
                error = "Error: AV3Setting not found on FaceEmo launcher.";
                return null;
            }
            if (av3.TargetAvatar == null)
            {
                error = "Error: Target avatar is not set (Avatar=None). Use ConfigureTargetAvatar to set the target avatar first.";
                return null;
            }

            // AV3Setting.TargetAvatar is MonoBehaviour; it's actually VRCAvatarDescriptor at runtime
            return av3.TargetAvatar as Component;
        }

        /// <summary>
        /// ExpressionImporter インスタンスをリフレクションで生成する共通ヘルパー。
        /// </summary>
        /// <param name="menu">操作対象の Menu（LoadMenu で取得したもの）。</param>
        /// <param name="av3Setting">Launcher の AV3Setting。</param>
        /// <param name="error">失敗時のエラーメッセージ。成功時は null。</param>
        /// <returns>ExpressionImporter インスタンス (object)。失敗時は null。</returns>
        /// <remarks>
        /// <para>内部で以下をリフレクション解決する:</para>
        /// <list type="bullet">
        ///   <item><description>ImportUtility.GetNewAssetDir() — アセット出力先ディレクトリ</description></item>
        ///   <item><description>new LocalizationSetting() — ローカライズ設定</description></item>
        ///   <item><description>new ExpressionImporter(menu, av3Setting, assetDir, locSetting)</description></item>
        /// </list>
        /// </remarks>
        private static object CreateExpressionImporter(FaceEmoMenu menu, AV3Setting av3Setting, out string error)
        {
            error = null;

            // ImportUtility.GetNewAssetDir()
            var utilType = Type.GetType(
                $"Suzuryg.FaceEmo.Detail.AV3.Importers.ImportUtility, {DetailEditorAssembly}");
            if (utilType == null)
            {
                error = "Error: ImportUtility type not found. Is FaceEmo installed?";
                return null;
            }
            var getDirMethod = utilType.GetMethod("GetNewAssetDir", BindingFlags.Public | BindingFlags.Static);
            if (getDirMethod == null)
            {
                error = "Error: ImportUtility.GetNewAssetDir() not found.";
                return null;
            }
            var assetDir = getDirMethod.Invoke(null, null) as string;

            // new LocalizationSetting()
            var locType = Type.GetType(
                $"Suzuryg.FaceEmo.Detail.Localization.LocalizationSetting, {DetailEditorAssembly}");
            if (locType == null)
            {
                error = "Error: LocalizationSetting type not found.";
                return null;
            }
            var locSetting = Activator.CreateInstance(locType);

            // new ExpressionImporter(menu, av3Setting, assetDir, locSetting)
            var importerType = Type.GetType(
                $"Suzuryg.FaceEmo.Detail.AV3.Importers.ExpressionImporter, {DetailEditorAssembly}");
            if (importerType == null)
            {
                error = "Error: ExpressionImporter type not found.";
                return null;
            }

            try
            {
                return Activator.CreateInstance(importerType, new object[] { menu, av3Setting, assetDir, locSetting });
            }
            catch (Exception ex)
            {
                error = $"Error: Failed to create ExpressionImporter: {ex.InnerException?.Message ?? ex.Message}";
                return null;
            }
        }

        // ═══════════════════════════════════════════
        //  Private — SO Cleanup
        // ═══════════════════════════════════════════

        private static void CleanupSerializableMenuChildren(SerializableMenu serMenu)
        {
            if (serMenu == null) return;

            if (serMenu.Registered != null)
            {
                CleanupMenuItemList(serMenu.Registered);
                Object.DestroyImmediate(serMenu.Registered, true);
            }

            if (serMenu.Unregistered != null)
            {
                CleanupMenuItemList(serMenu.Unregistered);
                Object.DestroyImmediate(serMenu.Unregistered, true);
            }
        }

        private static void CleanupMenuItemList(SerializableMenuItemListBase list)
        {
            if (list == null) return;

            if (list.Modes != null)
            {
                foreach (var mode in list.Modes)
                {
                    if (mode == null) continue;
                    CleanupMode(mode);
                    Object.DestroyImmediate(mode, true);
                }
            }

            if (list.Groups != null)
            {
                foreach (var group in list.Groups)
                {
                    if (group == null) continue;
                    CleanupMenuItemList(group);
                    Object.DestroyImmediate(group, true);
                }
            }
        }

        private static void CleanupMode(SerializableMode mode)
        {
            if (mode.Animation != null)
                Object.DestroyImmediate(mode.Animation, true);

            if (mode.Branches != null)
            {
                foreach (var branch in mode.Branches)
                {
                    if (branch == null) continue;
                    CleanupBranch(branch);
                    Object.DestroyImmediate(branch, true);
                }
            }
        }

        private static void CleanupBranch(SerializableBranch branch)
        {
            if (branch.BaseAnimation != null)
                Object.DestroyImmediate(branch.BaseAnimation, true);
            if (branch.LeftHandAnimation != null)
                Object.DestroyImmediate(branch.LeftHandAnimation, true);
            if (branch.RightHandAnimation != null)
                Object.DestroyImmediate(branch.RightHandAnimation, true);
            if (branch.BothHandsAnimation != null)
                Object.DestroyImmediate(branch.BothHandsAnimation, true);

            if (branch.Conditions != null)
            {
                foreach (var cond in branch.Conditions)
                {
                    if (cond != null)
                        Object.DestroyImmediate(cond, true);
                }
            }
        }

        // ═══════════════════════════════════════════
        //  Private — Search Helpers
        // ═══════════════════════════════════════════

        private static (string id, IMode mode) SearchMenuItemList(IMenuItemList list, string displayName)
        {
            if (list == null) return (null, null);

            foreach (string id in list.Order)
            {
                var itemType = list.GetType(id);
                if (itemType == MenuItemType.Mode)
                {
                    var mode = list.GetMode(id);
                    if (mode != null && mode.DisplayName == displayName)
                        return (id, mode);
                }
                else if (itemType == MenuItemType.Group)
                {
                    var group = list.GetGroup(id);
                    if (group != null)
                    {
                        var result = SearchMenuItemList(group, displayName);
                        if (result.id != null) return result;
                    }
                }
            }
            return (null, null);
        }

        private static IMode SearchMenuItemListById(IMenuItemList list, string modeId)
        {
            if (list == null) return null;

            foreach (string id in list.Order)
            {
                if (list.GetType(id) == MenuItemType.Mode && id == modeId)
                    return list.GetMode(id);
                if (list.GetType(id) == MenuItemType.Group)
                {
                    var r = SearchMenuItemListById(list.GetGroup(id), modeId);
                    if (r != null) return r;
                }
            }
            return null;
        }

        private static void CollectExpressions(IMenuItemList list, string prefix,
            List<(string id, IMode mode, string prefix)> result)
        {
            foreach (string id in list.Order)
            {
                var type = list.GetType(id);
                if (type == MenuItemType.Mode)
                    result.Add((id, list.GetMode(id), prefix));
                else if (type == MenuItemType.Group)
                    CollectExpressions(list.GetGroup(id), prefix, result);
            }
        }

        private static void CollectMenuItems(FaceEmoMenu menu, IMenuItemList list,
            string rootPrefix, int depth, string parentId, List<MenuItemInfo> result)
        {
            foreach (string id in list.Order)
            {
                var type = list.GetType(id);
                if (type == MenuItemType.Mode)
                {
                    var mode = list.GetMode(id);
                    result.Add(new MenuItemInfo
                    {
                        Id = id, Type = MenuItemType.Mode,
                        DisplayName = mode.DisplayName,
                        Depth = depth, RootPrefix = rootPrefix, ParentId = parentId
                    });
                }
                else if (type == MenuItemType.Group)
                {
                    var group = list.GetGroup(id);
                    result.Add(new MenuItemInfo
                    {
                        Id = id, Type = MenuItemType.Group,
                        DisplayName = group.DisplayName,
                        Depth = depth, RootPrefix = rootPrefix, ParentId = parentId
                    });
                    CollectMenuItems(menu, group, rootPrefix, depth + 1, id, result);
                }
            }
        }

        private static string FindGroupInList(IMenuItemList list, string displayName)
        {
            if (list == null) return null;
            foreach (string id in list.Order)
            {
                if (list.GetType(id) == MenuItemType.Group)
                {
                    var group = list.GetGroup(id);
                    if (group != null && group.DisplayName == displayName)
                        return id;
                    var subResult = FindGroupInList(group, displayName);
                    if (subResult != null) return subResult;
                }
            }
            return null;
        }
    }
}
#endif
