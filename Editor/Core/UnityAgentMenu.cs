namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// メニューバーの「UnityAgent」に並ぶ項目のパスと並び順。ここを見れば全体の構成が分かるよう、
    /// 各ウィンドウの [MenuItem] はこの定数だけを参照する。
    ///
    /// Unity は隣り合う項目の priority が 11 以上離れていると区切り線を引く。グループの先頭を
    /// 100 単位で離し、グループ内は 1 ずつ増やす。サブメニューの位置は中の最小の priority で決まる。
    /// 名前に '&amp;' を入れないこと。Windows のメニューはアクセスキーの印として消してしまう。
    /// </summary>
    internal static class UnityAgentMenu
    {
        private const string Root = "UnityAgent/";
        private const string Developer = Root + "Developer/";

        // ─── チャット ───
        public const string OpenChat = Root + "Open Chat";
        public const int OpenChatOrder = 0;

        // ─── アバター ───
        public const string AvatarOptimizer = Root + "Avatar Optimizer";
        public const int AvatarOptimizerOrder = 100;
        public const string OutfitFitting = Root + "Outfit Fitting";
        public const int OutfitFittingOrder = 101;
        public const string MochiFitterCatalog = Root + "MochiFitter Catalog";
        public const int MochiFitterCatalogOrder = 102;
        public const string TextureAtlas = Root + "Texture Atlas";
        public const int TextureAtlasOrder = 103;

        // ─── メッシュ・ポーズ ───
        public const string MeshPainter = Root + "Mesh Painter";
        public const int MeshPainterOrder = 200;
        public const string MeshWeightEditor = Root + "Mesh and Weight Editor";
        public const int MeshWeightEditorOrder = 201;
        public const string ShrinkEditor = Root + "Shrink Editor";
        public const int ShrinkEditorOrder = 202;
        public const string BonePoseEditor = Root + "Bone Pose Editor";
        public const int BonePoseEditorOrder = 203;
        public const string PoseEstimation = Root + "Pose Estimation";
        public const int PoseEstimationOrder = 204;

        // ─── エージェントの管理 ───
        public const string SkillManagement = Root + "Skill Management";
        public const int SkillManagementOrder = 300;
        public const string ToolConsole = Root + "Tool Console";
        public const int ToolConsoleOrder = 301;
        public const string TranslationManagement = Root + "Translation Management";
        public const int TranslationManagementOrder = 302;
        public const string Flowchart = Root + "Flowchart";
        public const int FlowchartOrder = 303;
        public const string AgentLog = Root + "Agent Log";
        public const int AgentLogOrder = 304;

        // ─── 開発用 (サブメニュー) ───
        public const string AOBake = Developer + "AO Bake";
        public const int AOBakeOrder = 1000;
        public const string MeshGeneration = Developer + "Mesh Generation";
        public const int MeshGenerationOrder = 1001;
        public const string FaceEmoTest = Developer + "FaceEmo Test";
        public const int FaceEmoTestOrder = 1002;
        public const string MATest = Developer + "MA Test";
        public const int MATestOrder = 1003;
        public const string NDMFTester = Developer + "NDMF Tester";
        public const int NDMFTesterOrder = 1004;
        public const string MeshPainterLegacy = Developer + "Mesh Painter (Legacy)";
        public const int MeshPainterLegacyOrder = 1005;

        public const string AnimatorAsCodeTest = Developer + "Animator-as-Code";
        public const int AnimatorAsCodeTestOrder = 1100;
        public const string AvatarMaskTest = Developer + "AvatarMask";
        public const int AvatarMaskTestOrder = 1101;
        public const string FlowchartCompileSample = Developer + "Flowchart: Compile Sample to Console";
        public const int FlowchartCompileSampleOrder = 1102;
        public const string FlowchartSaveSample = Developer + "Flowchart: Save Sample to Disk";
        public const int FlowchartSaveSampleOrder = 1103;
    }
}
