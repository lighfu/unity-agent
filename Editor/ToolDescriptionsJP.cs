using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// Japanese descriptions for AgentTools.
    /// Two-layer system:
    ///   1. Manual overrides dictionary for important/ambiguous tools
    ///   2. Auto-generation from tool name patterns (prefix verb + PascalCase noun → Japanese)
    /// </summary>
    internal static class ToolDescriptionsJP
    {
        /// <summary>
        /// Get Japanese description for a tool. Returns manual override if exists,
        /// otherwise auto-generates from tool name and category.
        /// </summary>
        public static string Get(string toolName, string category)
        {
            if (ManualOverrides.TryGetValue(toolName, out string manual))
                return manual;
            return AutoGenerate(toolName, category);
        }

        // ═══════════════════════════════════════════════════════════════
        // Manual overrides — important / commonly-used / ambiguous tools
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, string> ManualOverrides = new Dictionary<string, string>
        {
            // ── Hierarchy / Scene ──
            { "CreateGameObject", "空のGameObjectを作成" },
            { "DeleteGameObject", "GameObjectを削除" },
            { "FindGameObject", "名前でGameObjectを検索" },
            { "SetParent", "親オブジェクトを変更" },
            { "SetTransform", "位置・回転・スケールを設定" },
            { "GetTransform", "位置・回転・スケールを取得" },
            { "DuplicateGameObject", "GameObjectを複製" },
            { "RenameGameObject", "GameObjectの名前を変更" },
            { "ListRootObjects", "シーン直下のオブジェクト一覧" },
            { "ListChildren", "子オブジェクト一覧" },
            { "GetHierarchyTree", "階層ツリーを表示" },
            { "SetActive", "アクティブ/非アクティブを切り替え" },
            { "SetTag", "タグを設定" },
            { "SetLayer", "レイヤーを設定" },
            { "SelectGameObject", "GameObjectを選択状態にする" },
            { "BatchSetActive", "複数オブジェクトのアクティブを一括設定" },
            { "BatchDelete", "複数オブジェクトを一括削除" },
            { "FlattenHierarchy", "階層を平坦化" },

            // ── Inspector ──
            { "InspectGameObject", "GameObjectの詳細情報を表示" },
            { "DeepInspectComponent", "コンポーネントの全プロパティを表示" },
            { "SetVector3Property", "Vector3プロパティを設定" },
            { "SetColorProperty", "色プロパティを設定" },
            { "SetObjectReference", "オブジェクト参照を設定" },
            { "ToggleComponent", "コンポーネントの有効/無効を切り替え" },
            { "AddComponent", "コンポーネントを追加" },
            { "RemoveComponent", "コンポーネントを削除" },
            { "CopyComponent", "コンポーネントをコピー" },

            // ── Asset ──
            { "CreateFolder", "フォルダを作成" },
            { "CreatePrimitive", "プリミティブメッシュを作成" },
            { "DeleteAsset", "アセットを削除" },
            { "SearchAssets", "アセットを名前・型で検索" },
            { "ListAssetsInFolder", "フォルダ内のアセットを一覧" },
            { "GetAssetInfo", "アセットの詳細情報を取得" },
            { "InstantiatePrefab", "プレハブをシーンに配置" },
            { "SaveAsPrefab", "GameObjectをプレハブとして保存" },
            { "UnpackPrefab", "プレハブを展開" },

            // ── Material / Shader ──
            { "CreateMaterial", "マテリアルを作成" },
            { "SetMaterialColor", "マテリアルの色を設定" },
            { "AssignMaterial", "レンダラーにマテリアルを割り当て" },
            { "InspectMaterial", "マテリアルのプロパティを詳細表示" },
            { "DuplicateMaterial", "マテリアルを複製" },
            { "ChangeMaterialShader", "シェーダーを変更" },
            { "FindShader", "シェーダーを名前で検索" },
            { "SetMaterialFloat", "マテリアルのFloat値を設定" },
            { "SetMaterialProperty", "マテリアルのプロパティを設定" },
            { "ListMaterialProperties", "マテリアルのプロパティ一覧" },

            // ── lilToon ──
            { "InspectLilToonMaterial", "lilToonマテリアルの設定を表示" },
            { "SetLilToonColors", "lilToonの色を設定" },
            { "SetLilToonFloat", "lilToonのFloat値を設定" },
            { "SetLilToonTexture", "lilToonにテクスチャを設定" },
            { "ChangeLilToonRenderingMode", "lilToonの描画モードを変更" },
            { "ListLilToonPresets", "lilToonプリセット一覧" },
            { "ApplyLilToonPreset", "lilToonプリセットを適用" },
            { "ListLilToonMaterials", "シーン内のlilToonマテリアル一覧" },

            // ── Texture ──
            { "ApplyGradientEx", "テクスチャにグラデーションを適用" },
            { "AdjustHSV", "彩度・明度を調整（色の変更はApplyGradientExを使用）" },
            { "AdjustBrightnessContrast", "明度・コントラストを調整" },
            { "BlendTextures", "2つのテクスチャを合成" },
            { "BlendTextureOntoGameObject", "テクスチャを合成してオブジェクトに適用" },
            { "ResizeTexture", "テクスチャのサイズを変更" },
            { "CreateMaskTexture", "マスクテクスチャを生成" },
            { "GenerateTextureWithAI", "AIでテクスチャを生成・書き換え" },
            { "GenerateGuideMap", "ガイドマップを生成" },
            { "AnalyzeTextureMemory", "テクスチャメモリ使用量を分析" },
            { "GetTextureOptimizationRecommendations", "テクスチャ最適化の推奨設定を取得" },

            // ── Mesh Island / Paint ──
            { "ListMeshIslands", "メッシュアイランド（UV区画）一覧" },
            { "InspectMeshIsland", "アイランドの詳細情報" },
            { "EnableIslandSelectionMode", "Scene viewでのアイランド選択モード開始" },
            { "GetSelectedIslands", "選択済みアイランドのインデックスを取得" },
            { "OpenMeshPainter", "メッシュペイントウィンドウを開く" },

            // ── BlendShape ──
            { "ListBlendShapesEx", "BlendShape一覧（フィルタ対応）" },
            { "SetMultipleBlendShapes", "複数BlendShapeの値を一括設定" },
            { "ResetBlendShapes", "全BlendShapeを0にリセット" },
            { "GetActiveBlendShapes", "0以外のBlendShapeを取得" },
            { "ListBlendShapes", "BlendShape一覧" },
            { "SetBlendShape", "BlendShapeの値を設定" },
            { "CreateExpressionClip", "表情アニメーションクリップを作成" },
            { "PreviewExpressionClip", "表情をプレビュー表示" },
            { "FocusOnFace", "カメラを顔にフォーカス" },

            // ── Bone / Constraint ──
            { "ListBones", "ボーン階層を一覧表示" },
            { "InspectBone", "ボーンの位置・回転を確認" },
            { "ListHumanoidMapping", "Humanoidボーンマッピングを表示" },
            { "AddConstraint", "Constraintコンポーネントを追加" },
            { "RemoveConstraint", "Constraintを削除" },

            // ── Animation ──
            { "CreateAnimationClip", "アニメーションクリップを作成" },
            { "SetAnimationCurve", "アニメーションカーブ（キーフレーム）を設定" },
            { "GetAnimationClipInfo", "クリップの情報を表示" },
            { "InspectAnimatorController", "Animatorコントローラーの構成を表示" },
            { "CreateAnimatorController", "Animatorコントローラーを作成" },
            { "AddAnimatorParameter", "Animatorパラメータを追加" },
            { "AddAnimatorState", "Animatorステートを追加" },
            { "AddAnimatorTransition", "ステート間のトランジションを追加" },
            { "AddAnimatorLayer", "Animatorレイヤーを追加" },
            { "SetAnimatorLayerWeight", "レイヤーウェイトを設定" },
            { "RemoveAnimatorState", "Animatorステートを削除" },
            { "CreateBlendTree", "BlendTreeを作成" },
            { "BatchSetWriteDefaults", "Write Defaultsを一括設定" },

            // ── VRChat Core ──
            { "InspectAvatarDescriptor", "VRCアバターの設定を表示" },
            { "GetAvatarPerformanceStats", "パフォーマンスランクを確認" },
            { "ListExpressionParameters", "ExpressionParameterの一覧" },
            { "AddExpressionParameter", "ExpressionParameterを追加" },
            { "RemoveExpressionParameter", "ExpressionParameterを削除" },
            { "InspectExpressionsMenu", "Expressionメニューの内容を表示" },
            { "AddExpressionsMenuToggle", "メニューにトグルを追加" },
            { "AddExpressionsMenuButton", "メニューにボタンを追加" },
            { "AddExpressionsMenuSubMenu", "サブメニューを追加" },
            { "AddExpressionsMenuRadialPuppet", "ラジアルパペットを追加" },
            { "RemoveExpressionsMenuControl", "メニューコントロールを削除" },
            { "SetupObjectToggle", "オブジェクトトグルを一括セットアップ" },
            { "CreateToggleAnimations", "ON/OFFアニメーションを生成" },
            { "GetFXControllerPath", "FXコントローラーのパスを取得" },
            { "ConfigureEyeMovement", "目の動きを設定" },

            // ── PhysBone ──
            { "AddPhysBone", "PhysBoneを追加" },
            { "RemovePhysBone", "PhysBoneを削除" },
            { "ListPhysBones", "PhysBone一覧" },
            { "InspectPhysBone", "PhysBoneの詳細設定を表示" },
            { "ConfigurePhysBone", "PhysBoneのパラメータを設定" },
            { "ApplyPhysBoneTemplate", "PhysBoneテンプレートを適用" },
            { "AddPhysBoneCollider", "PhysBoneコライダーを追加" },
            { "RemovePhysBoneCollider", "PhysBoneコライダーを削除" },

            // ── VRC Constraint ──
            { "AddVRCConstraint", "VRC Constraintを追加" },
            { "InspectVRCConstraint", "VRC Constraintの設定を表示" },
            { "AddHeadChop", "Head Chopを追加（一人称で頭を非表示）" },

            // ── Contact ──
            { "AddContactReceiver", "Contactレシーバーを追加" },
            { "AddContactSender", "Contactセンダーを追加" },

            // ── Modular Avatar ──
            { "SetupOutfit", "MA Setup Outfitで衣装を非破壊セットアップ" },
            { "SetupObjectToggleMA", "MAでオブジェクトトグルをセットアップ" },
            { "AddMenuItem", "MAメニューアイテムを追加" },
            { "AddMAParameters", "MAパラメータを追加" },
            { "AddBlendshapeSync", "MAブレンドシェイプ同期を追加" },
            { "SetVisibleHeadAccessory", "一人称で見えるアクセサリーに設定" },

            // ── FaceEmo (リフレクション) ──
            { "FindFaceEmo", "シーン内のFaceEmoを検索" },
            { "InspectFaceEmo", "FaceEmo設定を表示" },
            { "ListFaceEmoExpressions", "FaceEmo表情一覧" },
            { "LaunchFaceEmoWindow", "FaceEmoエディタを開く" },
            { "ReadFaceEmoProperty", "FaceEmoプロパティを読み取り" },
            { "WriteFaceEmoProperty", "FaceEmoプロパティを変更" },
            { "ListFaceEmoProperties", "FaceEmoプロパティ一覧" },

            // ── FaceEmo Advanced (ドメインAPI) ──
            // A. 表情管理
            { "AddExpression", "FaceEmoに表情を追加" },
            { "RemoveExpression", "FaceEmoから表情を削除" },
            { "SetExpressionAnimation", "FaceEmo表情のアニメーションを設定" },
            { "ModifyExpressionProperties", "FaceEmo表情のプロパティを変更" },
            { "SetDefaultExpression", "FaceEmoのデフォルト表情を設定" },
            { "InspectExpressionDetail", "FaceEmo表情の詳細を表示" },
            // B. ジェスチャー
            { "AddGestureBranch", "FaceEmo表情にジェスチャー分岐を追加" },
            { "RemoveGestureBranch", "FaceEmo表情からジェスチャー分岐を削除" },
            { "AddGestureCondition", "ジェスチャー分岐に条件を追加" },
            { "ModifyBranchProperties", "ジェスチャー分岐のプロパティを変更" },
            // C. メニュー構造
            { "CreateExpressionGroup", "FaceEmoにサブメニューグループを作成" },
            { "MoveExpressionItem", "FaceEmoメニュー内のアイテムを移動" },
            // D. AV3設定
            { "ConfigureFaceEmoGeneration", "FaceEmo生成設定を変更" },
            { "ConfigureMouthMorphs", "口モーフBlendShapeを設定" },
            { "ConfigureAfkFace", "AFK表情を設定" },
            { "ConfigureFeatureToggles", "FaceEmo機能トグルを設定" },
            // E. アバター・コピー
            { "ConfigureTargetAvatar", "FaceEmoのターゲットアバターを設定" },
            { "CopyExpression", "FaceEmo表情を複製" },
            // F. 統合
            { "CreateAndRegisterExpression", "表情クリップ作成+FaceEmo登録を一括実行" },
            { "PreviewFaceEmoExpression", "FaceEmo表情をメッシュにプレビュー" },
            // G. インポート
            { "ImportExpressions", "FXレイヤーからFaceEmoに表情をインポート" },
            // H. 表情構築
            { "SearchExpressionShapes", "表情用BlendShapeをキーワード検索" },
            { "SetExpressionPreview", "BlendShape値を設定して表情プレビュー" },
            { "CaptureExpressionPreview", "表情プレビュー画像を撮影" },
            { "ResetExpressionPreview", "表情プレビューをリセット" },
            { "GetCurrentExpressionValues", "現在の非ゼロBlendShape値を取得" },
            // I. クリップ管理
            { "UpdateExpressionAnimation", "表情クリップを再作成+FaceEmo更新" },
            { "CreateExpressionFromData", "データから表情クリップ作成+FaceEmo登録" },
            // J. 適用
            { "ApplyFaceEmoToAvatar", "FaceEmoをアバターに適用（FXレイヤー生成）" },

            // ── AAO (Avatar Optimizer) ──
            { "AddTraceAndOptimize", "Trace and Optimizeを追加（自動最適化）" },
            { "AddMergeSkinnedMesh", "メッシュ結合コンポーネントを追加" },
            { "AddRemoveMeshInBox", "ボックス内メッシュ削除を追加" },
            { "AddRemoveMeshByBlendShape", "BlendShapeによるメッシュ削除を追加" },
            { "AddFreezeBlendShape", "BlendShapeの固定化を追加" },

            // ── Outfit Fitting ──
            { "AnalyzeOutfitCompatibility", "衣装の互換性を分析" },
            { "MapOutfitBones", "衣装のボーンマッピングを表示" },
            { "RetargetOutfit", "非対応衣装をリターゲット" },
            { "TransferOutfitWeights", "衣装にウェイトを転送" },
            { "DetectMeshPenetration", "メッシュの貫通を検出" },
            { "FixMeshPenetration", "メッシュの貫通を修正" },
            { "DeactivateOutfit", "衣装を非表示+EditorOnlyに設定" },
            { "SetupOutfitWizard", "衣装セットアップウィザードを実行" },

            // ── VRCFury ──
            { "CreateSimpleToggle", "VRCFuryでシンプルトグルを作成" },
            { "CreateArmatureLink", "VRCFuryでArmature Linkを作成" },
            { "CreateFullController", "VRCFuryでFull Controllerを作成" },

            // ── Renderer Settings ──
            { "ListRenderers", "レンダラー・マテリアル一覧" },
            { "InspectRendererSettings", "レンダラーの詳細設定を表示" },
            { "AddLight", "ライトを追加" },
            { "AddTrailRenderer", "トレイルレンダラーを追加" },

            // ── Particle System ──
            { "AddParticleSystem", "パーティクルシステムを追加" },
            { "InspectParticleSystem", "パーティクルシステムの設定を表示" },

            // ── Physics ──
            { "AddBoxCollider", "Box Colliderを追加" },
            { "AddSphereCollider", "Sphere Colliderを追加" },
            { "AddCapsuleCollider", "Capsule Colliderを追加" },
            { "AddRigidbody", "Rigidbodyを追加" },

            // ── File / Script ──
            { "ReadFile", "ファイルの内容を読み取り" },
            { "WriteFile", "ファイルに書き込み" },
            { "CreateCSharpScript", "C#スクリプトを作成" },
            { "RunEditorScript", "エディタスクリプトを実行" },

            // ── Scene ──
            { "SaveCurrentScene", "現在のシーンを保存" },
            { "LoadScene", "シーンを読み込み" },
            { "CaptureSceneView", "Scene viewのスクリーンショット" },
            { "CaptureMultiAngle", "複数角度からスクリーンショット" },

            // ── Build ──
            { "TriggerVRChatBuildTest", "VRChat Build & Test を実行" },
            { "TriggerNDMFManualBake", "NDMFの手動ベイクを実行" },
            { "ValidatePreBuild", "ビルド前の検証" },
            { "ValidateAvatar", "アバターの総合検証" },
            { "CheckWriteDefaults", "Write Defaultsの整合性チェック" },
            { "CheckParameterBudget", "パラメータコストの残量確認" },

            // ── Skill ──
            { "ListSkills", "利用可能なスキル一覧" },
            { "ReadSkill", "スキルの内容を読み取り" },
            { "SearchSkills", "キーワードでスキル検索" },
            { "CreateSkill", "新規スキルファイルを作成" },
            { "UpdateSkill", "スキルファイルを更新" },
            { "DeleteSkill", "スキルファイルを削除" },
            { "GetSkillTemplate", "スキルテンプレートを取得" },

            // ── OSC ──
            { "ListOSCAddresses", "OSCアドレス一覧" },
            { "ValidateOSCSetup", "OSC設定の検証" },

            // ── Quest ──
            { "AddQuestConverterSettings", "Quest変換設定を追加" },
            { "SetupQuestConversionWorkflow", "Quest対応ワークフローをセットアップ" },
            { "DiagnoseQuestReadiness", "Quest対応の準備状況を診断" },
            { "CheckQuestLimits", "Questの制限値を確認" },

            // ── Menu ──
            { "SearchMenu", "Unityメニュー項目を検索" },
            { "ExecuteMenu", "Unityメニュー項目を実行" },
            { "SearchTools", "ツールを名前・説明で検索" },
            { "AskUser", "ユーザーに質問・確認を表示" },
        };

        // ═══════════════════════════════════════════════════════════════
        // Auto-generation from tool name patterns
        // ═══════════════════════════════════════════════════════════════

        private static string AutoGenerate(string toolName, string category)
        {
            // 1. Extract verb prefix and noun
            string verb = ExtractVerb(toolName, out string noun);

            // 2. Translate verb
            string jpVerb = TranslateVerb(verb);

            // 3. Translate noun (split PascalCase → Japanese-friendly)
            string jpNoun = TranslateNoun(noun, category);

            // 4. Translate category
            string jpCategory = TranslateCategory(category);

            if (string.IsNullOrEmpty(jpNoun))
                return $"{jpCategory}の{jpVerb}";

            return $"{jpNoun}を{jpVerb}";
        }

        private static string ExtractVerb(string toolName, out string noun)
        {
            // Common prefixes sorted longest-first
            string[] prefixes =
            {
                "Configure", "Inspect", "Diagnose", "Validate",
                "Analyze", "Generate", "Duplicate", "Activate",
                "Optimize", "Customize", "Explain",
                "Trigger", "Preview", "Capture",
                "Create", "Delete", "Remove", "Search",
                "Rename", "Revert", "Unpack", "Unlink",
                "Setup", "Check", "Reset", "Batch",
                "Focus", "Force", "Clear", "Close",
                "Apply", "Nudge", "Align", "Paste",
                "Mark", "Pack", "Sort", "Move",
                "Find", "Save", "Load", "Open",
                "Keep", "Link", "Edit",
                "List", "Get", "Set", "Add",
            };

            foreach (var p in prefixes)
            {
                if (toolName.StartsWith(p) && toolName.Length > p.Length)
                {
                    noun = toolName.Substring(p.Length);
                    return p;
                }
            }

            noun = toolName;
            return "";
        }

        private static string TranslateVerb(string verb)
        {
            switch (verb)
            {
                case "List": return "一覧取得";
                case "Get": return "取得";
                case "Set": return "設定";
                case "Add": return "追加";
                case "Remove": return "削除";
                case "Delete": return "削除";
                case "Create": return "作成";
                case "Find": return "検索";
                case "Search": return "検索";
                case "Inspect": return "詳細表示";
                case "Configure": return "設定変更";
                case "Apply": return "適用";
                case "Save": return "保存";
                case "Load": return "読み込み";
                case "Open": return "開く";
                case "Close": return "閉じる";
                case "Reset": return "リセット";
                case "Clear": return "クリア";
                case "Validate": return "検証";
                case "Check": return "確認";
                case "Analyze": return "分析";
                case "Generate": return "生成";
                case "Preview": return "プレビュー";
                case "Capture": return "キャプチャ";
                case "Duplicate": return "複製";
                case "Rename": return "名前変更";
                case "Setup": return "セットアップ";
                case "Trigger": return "実行";
                case "Batch": return "一括処理";
                case "Link": return "リンク";
                case "Unlink": return "リンク解除";
                case "Activate": return "有効化";
                case "Diagnose": return "診断";
                case "Optimize": return "最適化";
                case "Edit": return "編集";
                case "Keep": return "保持";
                case "Focus": return "フォーカス";
                case "Force": return "強制設定";
                case "Sort": return "並び替え";
                case "Move": return "移動";
                case "Revert": return "元に戻す";
                case "Unpack": return "展開";
                case "Pack": return "パック";
                case "Customize": return "カスタマイズ";
                case "Mark": return "マーク";
                case "Nudge": return "微調整";
                case "Align": return "整列";
                case "Explain": return "説明";
                default: return "操作";
            }
        }

        // PascalCase noun → readable Japanese-ish label
        private static string TranslateNoun(string noun, string category)
        {
            // Direct noun translations (common domain terms)
            if (NounMap.TryGetValue(noun, out string jpNoun))
                return jpNoun;

            // Split PascalCase and try partial matches
            string spaced = SplitPascalCase(noun);
            return spaced;
        }

        private static readonly Dictionary<string, string> NounMap = new Dictionary<string, string>
        {
            { "GameObjects", "GameObject" },
            { "GameObject", "GameObject" },
            { "Children", "子オブジェクト" },
            { "RootObjects", "ルートオブジェクト" },
            { "HierarchyTree", "階層ツリー" },
            { "Transform", "Transform" },
            { "TransformPath", "Transformパス" },
            { "SiblingIndex", "兄弟順序" },
            { "BlendShapes", "BlendShape" },
            { "BlendShapesEx", "BlendShape（拡張）" },
            { "MultipleBlendShapes", "複数BlendShape" },
            { "ActiveBlendShapes", "アクティブなBlendShape" },
            { "Bones", "ボーン" },
            { "Bone", "ボーン" },
            { "HumanoidMapping", "Humanoidマッピング" },
            { "Renderers", "レンダラー" },
            { "RendererSettings", "レンダラー設定" },
            { "Material", "マテリアル" },
            { "Materials", "マテリアル" },
            { "MaterialColor", "マテリアル色" },
            { "MaterialProperties", "マテリアルプロパティ" },
            { "MaterialProperty", "マテリアルプロパティ" },
            { "AnimatorController", "Animatorコントローラー" },
            { "AnimatorParameter", "Animatorパラメータ" },
            { "AnimatorState", "Animatorステート" },
            { "AnimatorTransition", "トランジション" },
            { "AnimatorLayer", "Animatorレイヤー" },
            { "AnimatorLayerWeight", "レイヤーウェイト" },
            { "AnimationClip", "アニメーションクリップ" },
            { "AnimationCurve", "アニメーションカーブ" },
            { "AnimationClipInfo", "クリップ情報" },
            { "AnimationClipSettings", "クリップ設定" },
            { "AnimationEvents", "アニメーションイベント" },
            { "AnimationEvent", "アニメーションイベント" },
            { "BlendTree", "BlendTree" },
            { "BlendTreeChild", "BlendTree子要素" },
            { "AvatarMask", "Avatarマスク" },
            { "ExpressionClip", "表情クリップ" },
            { "ExpressionClipFromData", "表情クリップ（データから）" },
            { "ExpressionPreview", "表情プレビュー" },
            { "AvatarDescriptor", "Avatar Descriptor" },
            { "AvatarPerformanceStats", "パフォーマンス統計" },
            { "ExpressionParameters", "ExpressionParameter" },
            { "ExpressionParameter", "ExpressionParameter" },
            { "ExpressionsMenu", "Expressionメニュー" },
            { "ExpressionsMenuToggle", "メニュートグル" },
            { "ExpressionsMenuButton", "メニューボタン" },
            { "ExpressionsMenuSubMenu", "サブメニュー" },
            { "ExpressionsMenuRadialPuppet", "ラジアルパペット" },
            { "ExpressionsMenuControl", "メニューコントロール" },
            { "PhysBone", "PhysBone" },
            { "PhysBones", "PhysBone" },
            { "PhysBoneCollider", "PhysBoneコライダー" },
            { "PhysBoneColliders", "PhysBoneコライダー" },
            { "PhysBoneExclusions", "PhysBone除外設定" },
            { "PhysBoneEndpoint", "PhysBoneエンドポイント" },
            { "PhysBoneTemplate", "PhysBoneテンプレート" },
            { "MeshIslands", "メッシュアイランド" },
            { "MeshIsland", "メッシュアイランド" },
            { "MeshIslandPaint", "アイランドペイント" },
            { "IslandSelectionMode", "アイランド選択モード" },
            { "SelectedIslands", "選択済みアイランド" },
            { "MeshBounds", "メッシュ境界" },
            { "MeshPenetration", "メッシュ貫通" },
            { "MeshPainter", "メッシュペインター" },
            { "MeshSimplifier", "メッシュ簡略化" },
            { "MeshSimplifiers", "メッシュ簡略化" },
            { "MergeSkinnedMesh", "メッシュ結合" },
            { "MergeBone", "ボーンマージ" },
            { "MergeBoneBatch", "ボーンマージ（一括）" },
            { "TraceAndOptimize", "Trace and Optimize" },
            { "RemoveMeshInBox", "ボックス内メッシュ削除" },
            { "RemoveMeshByBlendShape", "BlendShapeでメッシュ削除" },
            { "FreezeBlendShape", "BlendShape固定化" },
            { "FreezeBlendShapes", "BlendShape固定化" },
            { "AAOComponents", "AAOコンポーネント" },
            { "AAOComponent", "AAOコンポーネント" },
            { "Constraint", "Constraint" },
            { "Constraints", "Constraint" },
            { "VRCConstraint", "VRC Constraint" },
            { "VRCConstraints", "VRC Constraint" },
            { "VRCConstraintSource", "VRC Constraintソース" },
            { "HeadChop", "Head Chop" },
            { "Contacts", "Contact" },
            { "ContactReceiver", "Contactレシーバー" },
            { "ContactSender", "Contactセンダー" },
            { "Contact", "Contact" },
            { "ParticleSystem", "パーティクルシステム" },
            { "ParticleMain", "パーティクル基本設定" },
            { "ParticleEmission", "パーティクル発生" },
            { "ParticleShape", "パーティクル形状" },
            { "ParticleRenderer", "パーティクルレンダラー" },
            { "ParticleBurst", "パーティクルバースト" },
            { "Cloth", "Cloth" },
            { "Cloths", "Cloth" },
            { "Light", "ライト" },
            { "Lights", "ライト" },
            { "LODGroup", "LODグループ" },
            { "LODGroups", "LODグループ" },
            { "LOD", "LOD" },
            { "TrailRenderer", "トレイルレンダラー" },
            { "LineRenderer", "ラインレンダラー" },
            { "BoxCollider", "Box Collider" },
            { "SphereCollider", "Sphere Collider" },
            { "CapsuleCollider", "Capsule Collider" },
            { "MeshCollider", "Mesh Collider" },
            { "Colliders", "コライダー" },
            { "Collider", "コライダー" },
            { "Rigidbody", "Rigidbody" },
            { "Joint", "Joint" },
            { "Joints", "Joint" },
            { "SpringJoint", "Spring Joint" },
            { "Prefab", "プレハブ" },
            { "PrefabOverrides", "プレハブ上書き" },
            { "PrefabInfo", "プレハブ情報" },
            { "PrefabVariant", "プレハブバリアント" },
            { "PrefabVariantBase", "プレハブバリアント元" },
            { "PrefabVariants", "プレハブバリアント" },
            { "Folder", "フォルダ" },
            { "Asset", "アセット" },
            { "AssetInfo", "アセット情報" },
            { "Assets", "アセット" },
            { "AssetsInFolder", "フォルダ内アセット" },
            { "SubAssets", "サブアセット" },
            { "TopFolders", "トップフォルダ" },
            { "File", "ファイル" },
            { "TextureImporter", "テクスチャインポーター" },
            { "ModelImporter", "モデルインポーター" },
            { "ModelTextures", "モデルテクスチャ" },
            { "ModelMaterials", "モデルマテリアル" },
            { "AudioSource", "AudioSource" },
            { "AudioSources", "AudioSource" },
            { "AudioClips", "AudioClip" },
            { "CurrentScene", "現在のシーン" },
            { "BuildScenes", "ビルドシーン" },
            { "LoadedScenes", "読み込み済みシーン" },
            { "ActiveScene", "アクティブシーン" },
            { "SceneInfo", "シーン情報" },
            { "SceneView", "Scene View" },
            { "MultiAngle", "マルチアングル" },
            { "NDMFManualBake", "NDMFベイク" },
            { "NDMFPlugins", "NDMFプラグイン" },
            { "BuildCache", "ビルドキャッシュ" },
            { "VRChatBuildTest", "VRChat Build & Test" },
            { "PreBuild", "ビルド前" },
            { "WriteDefaults", "Write Defaults" },
            { "ParameterBudget", "パラメータ残量" },
            { "EyeLook", "Eye Look" },
            { "EyeMovement", "目の動き" },
            { "FXControllerPath", "FXコントローラーパス" },
            { "ToggleAnimations", "トグルアニメーション" },
            { "ObjectToggle", "オブジェクトトグル" },
            { "ObjectToggleMA", "MAオブジェクトトグル" },
            { "OutfitCompatibility", "衣装互換性" },
            { "OutfitBones", "衣装ボーン" },
            { "Outfit", "衣装" },
            { "OutfitWeights", "衣装ウェイト" },
            { "OutfitWizard", "衣装ウィザード" },
            { "Outfits", "衣装" },
            { "Skills", "スキル" },
            { "Skill", "スキル" },
            { "SkillTemplate", "スキルテンプレート" },
            { "Tools", "ツール" },
            { "User", "ユーザー" },
            { "FaceEmo", "FaceEmo" },
            { "FaceEmoExpressions", "FaceEmo表情" },
            { "FaceEmoWindow", "FaceEmoウィンドウ" },
            { "FaceEmoProperty", "FaceEmoプロパティ" },
            { "FaceEmoProperties", "FaceEmoプロパティ" },
            { "FaceEmoGeneration", "FaceEmo生成" },
            { "MouthMorphs", "口モーフ" },
            { "AfkFace", "AFK表情" },
            { "FeatureToggles", "機能トグル" },
            { "MAComponents", "MAコンポーネント" },
            { "MAComponent", "MAコンポーネント" },
            { "MAParameters", "MAパラメータ" },
            { "MenuItem", "メニューアイテム" },
            { "BlendshapeSync", "BlendShape同期" },
            { "VisibleHeadAccessory", "可視ヘッドアクセサリ" },
            { "VRCFuryComponents", "VRCFuryコンポーネント" },
            { "VRCFuryComponent", "VRCFuryコンポーネント" },
            { "FullController", "Full Controller" },
            { "OSCAddresses", "OSCアドレス" },
            { "OSCConfigJson", "OSC設定JSON" },
            { "OSCConfigFile", "OSC設定ファイル" },
            { "OSCConfig", "OSC設定" },
            { "OSCRoute", "OSCルート" },
            { "OSCSetup", "OSCセットアップ" },
            { "OSCReadiness", "OSC準備状態" },
            { "OSCDataTypes", "OSCデータ型" },
            { "FaceTrackingParameters", "フェイストラッキングパラメータ" },
            { "HeartRateParameters", "心拍パラメータ" },
            { "HardwareTelemetryParameters", "ハードウェアテレメトリパラメータ" },
            { "VRChatOSCInputEndpoints", "VRChat OSC入力エンドポイント" },
            { "QuestConverterSettings", "Quest変換設定" },
            { "QuestConversion", "Quest変換" },
            { "QuestConversionWorkflow", "Quest変換ワークフロー" },
            { "QuestReadiness", "Quest対応状況" },
            { "QuestLimits", "Quest制限" },
            { "QuestSettings", "Quest設定" },
            { "MaterialSwap", "マテリアルスワップ" },
            { "TextureMemory", "テクスチャメモリ" },
            { "TextureFormats", "テクスチャフォーマット" },
            { "TextureOptimizationRecommendations", "テクスチャ最適化推奨" },
            { "UVOverlaps", "UVオーバーラップ" },
            { "UVUtilization", "UV使用率" },
            { "UVPaintReadiness", "UVペイント準備状態" },
            { "GuideMap", "ガイドマップ" },
            { "Textures", "テクスチャ" },
            { "MaskTexture", "マスクテクスチャ" },
            { "ChannelTexture", "チャンネルテクスチャ" },
            { "Texture", "テクスチャ" },
            { "TextureWithAI", "AI生成テクスチャ" },
            { "DecalSlot", "デカールスロット" },
            { "GradientEx", "グラデーション（拡張）" },
            { "Gradient", "グラデーション" },
            { "HSV", "HSV（色相・彩度・明度）" },
            { "BrightnessContrast", "明度・コントラスト" },
            { "RingToBone", "リングをボーンに" },
            { "Ring", "リング" },
            { "RingScale", "リングスケール" },
            { "RingWithBoneProxy", "リング（BoneProxy付き）" },
            { "OnFace", "顔に" },
            { "Avatar", "アバター" },
            { "Primitive", "プリミティブ" },
            { "CSharpScript", "C#スクリプト" },
            { "Shader", "シェーダー" },
            { "EditorScript", "エディタスクリプト" },
            { "Menu", "メニュー" },
            { "MenuCache", "メニューキャッシュ" },
            { "MenuCategory", "メニューカテゴリ" },
        };

        private static string SplitPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        }

        private static string TranslateCategory(string category)
        {
            switch (category)
            {
                case "Scene": return "シーン操作";
                case "Hierarchy": return "階層操作";
                case "Inspector": return "インスペクター";
                case "Asset": return "アセット";
                case "AssetSearch": return "アセット検索";
                case "Material": return "マテリアル";
                case "MaterialAdvanced": return "マテリアル詳細";
                case "LilToon": return "lilToon";
                case "Texture": return "テクスチャ";
                case "TextureEdit": return "テクスチャ編集";
                case "TextureGeneration": return "テクスチャ生成";
                case "TextureComposition": return "テクスチャ合成";
                case "TextureMemoryAnalysis": return "テクスチャメモリ分析";
                case "BlendShape": return "BlendShape";
                case "BlendShapeModifier": return "BlendShape編集";
                case "Bone": return "ボーン";
                case "MeshIsland": return "メッシュアイランド";
                case "MeshAnalysis": return "メッシュ分析";
                case "MeshSimplifier": return "メッシュ簡略化";
                case "Animator": return "Animator";
                case "AnimatorAdvanced": return "Animator詳細";
                case "AnimatorAsCode": return "Animator自動生成";
                case "AnimationClip": return "アニメーション";
                case "VRChat": return "VRChat";
                case "VRChatConstraint": return "VRC Constraint";
                case "VRChatContact": return "VRC Contact";
                case "VRChatPerformance": return "パフォーマンス";
                case "PhysBone": return "PhysBone";
                case "Physics": return "Physics";
                case "Particle": return "パーティクル";
                case "ModularAvatar": return "Modular Avatar";
                case "FaceEmo": return "FaceEmo";
                case "FaceEmoAdvanced": return "FaceEmo詳細";
                case "AvatarOptimizer": return "AAO最適化";
                case "AvatarValidation": return "アバター検証";
                case "OutfitFitting": return "衣装フィッティング";
                case "OutfitWizard": return "衣装ウィザード";
                case "VRCFuryAdvanced": return "VRCFury";
                case "Renderer": return "レンダラー";
                case "RendererSettings": return "レンダラー設定";
                case "Prefab": return "プレハブ";
                case "Property": return "プロパティ";
                case "Component": return "コンポーネント";
                case "File": return "ファイル";
                case "SceneView": return "Scene View";
                case "SceneManagement": return "シーン管理";
                case "Importer": return "インポーター";
                case "Audio": return "オーディオ";
                case "LOD": return "LOD";
                case "Cloth": return "Cloth";
                case "BuildPipeline": return "ビルド";
                case "ExpressionParameter": return "ExpressionParameter";
                case "Interaction": return "インタラクション";
                case "ScriptExecution": return "スクリプト実行";
                case "Skill": return "スキル";
                case "Meta": return "メタ";
                case "OSC": return "OSC";
                case "OSCAdvanced": return "OSC詳細";
                case "QuestConversion": return "Quest変換";
                case "QuestWorkflow": return "Questワークフロー";
                case "CurveAndGradient": return "カーブ/グラデーション";
                case "UVValidation": return "UV検証";
                default: return category;
            }
        }
    }
}
