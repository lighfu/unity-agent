using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// MCP サーバーの動作モード。詳細は <see cref="AgentSettings.MCPServerMode"/> 参照。
    /// </summary>
    public enum MCPServerMode
    {
        /// <summary>Editor 内で HTTP listener を直接動かす (legacy、domain reload で死ぬ)。</summary>
        InProc = 0,
        /// <summary>別プロセスの bridge binary が HTTP を持つ (recommended、reload を生き残る)。</summary>
        Bridge = 1,
    }

    public static class AgentSettings
    {
        private const string ConfirmDestructiveKey = "UnityAgent_ConfirmDestructive";
        private const string MaxContextTokensKey = "UnityAgent_MaxContextTokens";
        private const int DefaultMaxContextTokens = 900000;
        private const string WebServerPortKey = "UnityAgent_WebServerPort";
        private const int DefaultWebServerPort = 6080;
        private const string WebServerUsernameKey = "UnityAgent_WebServerUsername";
        private const string WebServerPasswordKey = "UnityAgent_WebServerPassword";
        private const string MCPServerEnabledKey = "UnityAgent_MCPServerEnabled";
        private const string MCPServerPortKey = "UnityAgent_MCPServerPort";
        private const int DefaultMCPServerPort = 17932;
        private const string MCPServerTokenKey = "UnityAgent_MCPServerToken";
        private const string MCPServerExposeRiskKey = "UnityAgent_MCPServerExposeRisk";
        private const string MCPServerModeKey = "UnityAgent_MCPServerMode";
        private const string ConfirmToolsKey = "UnityAgent_ConfirmTools";
        private const string DisabledSkillsKey = "UnityAgent_DisabledSkills";
        private const string DisabledToolsKey = "UnityAgent_DisabledTools";
        private const string EnabledExternalToolsKey = "UnityAgent_EnabledExternalTools";
        private const string UILanguageKey = "UnityAgent_UILanguage";
        private const string DiscordLoggingEnabledKey = "UnityAgent_DiscordLoggingEnabled";
        private const string DebugModeKey = "UnityAgent_DebugMode";
        private const string AllowRemoteInsecureHttpKey = "UnityAgent_AllowRemoteInsecureHttp";
        private const string ThemeModeKey = "UnityAgent_ThemeMode";
        private const string ThemeColorPrefix = "UnityAgent_ThemeColor_";
        private const string SeedColorKey = "UnityAgent_SeedColor";

        public static readonly string[] PaletteColorNames =
        {
            "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer",
            "Secondary", "OnSecondary", "SecondaryContainer", "OnSecondaryContainer",
            "Tertiary", "OnTertiary", "TertiaryContainer", "OnTertiaryContainer",
            "Surface", "OnSurface", "SurfaceVariant", "OnSurfaceVariant",
            "SurfaceContainerLowest", "SurfaceContainerLow", "SurfaceContainer", "SurfaceContainerHigh", "SurfaceContainerHighest",
            "Outline", "OutlineVariant",
            "Error", "OnError",
            "InverseSurface", "InverseOnSurface", "InversePrimary",
        };

        public static readonly (string label, int start, int count)[] PaletteColorGroups =
        {
            ("Primary", 0, 4),
            ("Secondary", 4, 4),
            ("Tertiary", 8, 4),
            ("Surface", 12, 9),  // Surface through SurfaceContainerHighest
            ("Outline", 21, 2),
            ("Error", 23, 2),
            ("Inverse", 25, 3),
        };

        public static readonly (string code, string label)[] SupportedLanguages =
        {
            ("en", "English"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("zh", "中文"),
            ("zh-TW", "繁體中文(台灣)"),
            ("es", "Español"),
            ("fr", "Français"),
            ("de", "Deutsch"),
            ("pt", "Português"),
            ("ru", "Русский"),
            ("it", "Italiano"),
            ("pl", "Polski"),
            ("nl", "Nederlands"),
            ("tr", "Türkçe"),
            ("vi", "Tiếng Việt"),
            ("th", "ไทย"),
            ("id", "Bahasa Indonesia"),
            ("ar", "العربية"),
            ("uk", "Українська"),
            ("sv", "Svenska"),
            ("da", "Dansk"),
            ("fi", "Suomi"),
            ("no", "Norsk"),
            ("cs", "Čeština"),
            ("hu", "Magyar"),
            ("ro", "Română"),
        };

        /// <summary>
        /// デフォルトで確認が必要なツール（現在 RequestConfirmation を呼んでいるメソッド群）
        /// </summary>
        public static readonly HashSet<string> DefaultConfirmTools = new HashSet<string>
        {
            "AddFreezeBlendShape",
            "AddMergeBone",
            "AddMergeBoneBatch",
            "AddMergeSkinnedMesh",
            "AddMeshSimplifier",
            "AddQuestConverterSettings",
            "AddRemoveMeshByBlendShape",
            "AddRemoveMeshInBox",
            "AddTraceAndOptimize",
            "ApplyLilToonPreset",
            "ApplyPhysBoneTemplate",
            "BatchDelete",
            "BatchSetLilToonColors",
            "BatchSetLilToonFloat",
            "ChangeLilToonRenderingMode",
            "ConfigureEyeMovement",
            "CreateArmatureLink",
            "CreateBlendShapeLink",
            "CreateBlendShapeToggle",
            "CreateCSharpScript",
            "CreateFullController",
            "CreateGestureLayer",
            "CreateIntToggleSwap",
            "CreateObjectToggle",
            "CreateRadialPuppet",
            "CreateShader",
            "CreateSimpleToggle",
            "DeactivateOutfit",
            "DeleteAsset",
            "DeleteGameObject",
            "FixMeshPenetration",
            "FlattenHierarchy",
            "InsertIntoFile",
            "RemoveAAOComponent",
            "RemoveAnimatorState",
            "RemoveCollider",
            "RemoveComponent",
            "RemoveConstraint",
            "RemoveCustomBlendShape",
            "RemoveExpression",
            "RemoveExpressionsMenuControl",
            "RemoveExpressionParameter",
            "RemoveGeneratedAnimator",
            "RemoveGestureBranch",
            "RemoveMAComponent",
            "RemoveMeshSimplifier",
            "RemoveVRCFuryComponent",
            "RetargetOutfit",
            "RunEditorScript",
            "SetupMultiplexReceiver",
            "SetupObjectToggle",
            "SetupObjectToggleMA",
            "SetupOSCDrivenBlendTree",
            "SetupOSCParameterDriver",
            "SetupOSCSmoothingLayer",
            "SetupOutfitWizard",
            "SetupQuestConversionWorkflow",
            "TransferOutfitWeights",
            "WriteFile",
            "WriteOSCConfigFile",
            "ResetOSCConfig",
            "CustomizeOSCRoute",
            "AddFaceTrackingParameters",
            "AddHeartRateParameters",
            "AddHardwareTelemetryParameters",
            "BlendTextureOntoGameObject",
            "ConfigureDecalSlot",
        };

        private static HashSet<string> _confirmToolsCache;
        private static HashSet<string> _disabledSkillsCache;
        private static HashSet<string> _disabledToolsCache;
        private static HashSet<string> _enabledExternalToolsCache;

        public static string UILanguageRaw
        {
            get => SettingsStore.GetString(UILanguageKey, "auto");
            set => SettingsStore.SetString(UILanguageKey, value);
        }

        public static string UILanguage
        {
            get
            {
                string raw = UILanguageRaw;
                return raw == "auto" ? DetectSystemLanguage() : raw;
            }
            set => UILanguageRaw = value;
        }

        private static string _detectedLanguage;
        private static string DetectSystemLanguage()
        {
            if (_detectedLanguage != null) return _detectedLanguage;
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Japanese: _detectedLanguage = "ja"; break;
                case SystemLanguage.Korean: _detectedLanguage = "ko"; break;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: _detectedLanguage = "zh"; break;
                case SystemLanguage.ChineseTraditional: _detectedLanguage = "zh-TW"; break;
                case SystemLanguage.Spanish: _detectedLanguage = "es"; break;
                case SystemLanguage.French: _detectedLanguage = "fr"; break;
                case SystemLanguage.German: _detectedLanguage = "de"; break;
                case SystemLanguage.Portuguese: _detectedLanguage = "pt"; break;
                case SystemLanguage.Russian: _detectedLanguage = "ru"; break;
                case SystemLanguage.Italian: _detectedLanguage = "it"; break;
                case SystemLanguage.Polish: _detectedLanguage = "pl"; break;
                case SystemLanguage.Dutch: _detectedLanguage = "nl"; break;
                case SystemLanguage.Turkish: _detectedLanguage = "tr"; break;
                case SystemLanguage.Vietnamese: _detectedLanguage = "vi"; break;
                case SystemLanguage.Thai: _detectedLanguage = "th"; break;
                case SystemLanguage.Indonesian: _detectedLanguage = "id"; break;
                case SystemLanguage.Arabic: _detectedLanguage = "ar"; break;
                case SystemLanguage.Ukrainian: _detectedLanguage = "uk"; break;
                case SystemLanguage.Swedish: _detectedLanguage = "sv"; break;
                case SystemLanguage.Danish: _detectedLanguage = "da"; break;
                case SystemLanguage.Finnish: _detectedLanguage = "fi"; break;
                case SystemLanguage.Norwegian: _detectedLanguage = "no"; break;
                case SystemLanguage.Czech: _detectedLanguage = "cs"; break;
                case SystemLanguage.Hungarian: _detectedLanguage = "hu"; break;
                case SystemLanguage.Romanian: _detectedLanguage = "ro"; break;
                default: _detectedLanguage = "en"; break;
            }
            return _detectedLanguage;
        }

        public static bool ConfirmDestructive
        {
            get => SettingsStore.GetBool(ConfirmDestructiveKey, true);
            set => SettingsStore.SetBool(ConfirmDestructiveKey, value);
        }

        public static bool DiscordLoggingEnabled
        {
            get => SettingsStore.GetBool(DiscordLoggingEnabledKey, false);
            set => SettingsStore.SetBool(DiscordLoggingEnabledKey, value);
        }

        public static bool DebugMode
        {
            get => SettingsStore.GetBool(DebugModeKey, false);
            set => SettingsStore.SetBool(DebugModeKey, value);
        }

        /// <summary>
        /// リッチチャット UI (ツール実行カード等) を使うか。false で旧 Info 行表示に戻る escape hatch。
        /// </summary>
        public static bool UseRichChatUI
        {
            get => SettingsStore.GetBool("UnityAgent_UseRichChatUI", true);
            set => SettingsStore.SetBool("UnityAgent_UseRichChatUI", value);
        }

        /// <summary>
        /// true の場合、ローカル以外の HTTP URL への接続も許可する。
        /// デフォルト false（ローカルのみ許可）。
        /// </summary>
        public static bool AllowRemoteInsecureHttp
        {
            get => SettingsStore.GetBool(AllowRemoteInsecureHttpKey, false);
            set => SettingsStore.SetBool(AllowRemoteInsecureHttpKey, value);
        }

        public static int MaxContextTokens
        {
            get => SettingsStore.GetInt(MaxContextTokensKey, DefaultMaxContextTokens);
            set => SettingsStore.SetInt(MaxContextTokensKey, value);
        }

        /// <summary>
        /// プロバイダーの実効コンテキスト上限を返す。
        /// configValue > 0 ならその値、そうでなければ modelInputLimit、それも 0 なら DefaultMaxContextTokens。
        /// </summary>
        public static int ResolveMaxContextTokens(int configValue, int modelInputLimit)
        {
            if (configValue > 0) return configValue;
            if (modelInputLimit > 0) return modelInputLimit;
            return DefaultMaxContextTokens;
        }

        public static int WebServerPort
        {
            get => SettingsStore.GetInt(WebServerPortKey, DefaultWebServerPort);
            set => SettingsStore.SetInt(WebServerPortKey, value);
        }

        public static string WebServerUsername
        {
            get => SettingsStore.GetString(WebServerUsernameKey, "");
            set => SettingsStore.SetString(WebServerUsernameKey, value);
        }

        public static string WebServerPassword
        {
            get => SettingsStore.GetString(WebServerPasswordKey, "");
            set => SettingsStore.SetString(WebServerPasswordKey, value);
        }

        // ─── MCP Server (外部エージェントに Unity ツールを公開) ───

        /// <summary>
        /// UnityAgent を MCP サーバーとして起動するか。
        /// Editor 起動時に UnityAgentWindow が参照し自動起動する。
        /// </summary>
        public static bool MCPServerEnabled
        {
            get => SettingsStore.GetBool(MCPServerEnabledKey, false);
            set => SettingsStore.SetBool(MCPServerEnabledKey, value);
        }

        public static int MCPServerPort
        {
            get => SettingsStore.GetInt(MCPServerPortKey, DefaultMCPServerPort);
            set => SettingsStore.SetInt(MCPServerPortKey, value);
        }

        /// <summary>
        /// Bearer 認証用トークン。未設定の場合は <see cref="EnsureMCPServerToken"/> が自動生成する。
        /// </summary>
        public static string MCPServerToken
        {
            get => SettingsStore.GetString(MCPServerTokenKey, "");
            set => SettingsStore.SetString(MCPServerTokenKey, value ?? "");
        }

        /// <summary>
        /// 外部エージェントへ公開するツールのリスク上限。
        /// デフォルトは Caution (Safe + Caution)。Dangerous はユーザーが明示的に変更した場合のみ。
        /// </summary>
        public static ToolRisk MCPServerExposeRisk
        {
            get => (ToolRisk)SettingsStore.GetInt(MCPServerExposeRiskKey, (int)ToolRisk.Caution);
            set => SettingsStore.SetInt(MCPServerExposeRiskKey, (int)value);
        }

        /// <summary>
        /// トークンを自動生成 (未設定時) し、現在値を返す。
        /// </summary>
        public static string EnsureMCPServerToken()
        {
            string current = MCPServerToken;
            if (!string.IsNullOrEmpty(current)) return current;
            string generated = System.Guid.NewGuid().ToString("N");
            MCPServerToken = generated;
            return generated;
        }

        /// <summary>
        /// トークンを強制再生成する。
        /// </summary>
        public static string RegenerateMCPServerToken()
        {
            string generated = System.Guid.NewGuid().ToString("N");
            MCPServerToken = generated;
            return generated;
        }

        /// <summary>
        /// MCP サーバーの動作モード。
        /// - <see cref="MCPServerMode.InProc"/>: Unity Editor 内で <see cref="MCP.AgentMCPServer"/> が直接 HTTP listener を起動 (legacy)
        /// - <see cref="MCPServerMode.Bridge"/>: 別プロセスの Go binary が HTTP を持ち、Unity は TCP クライアントとして接続 (recommended; reload を生き残る)
        /// </summary>
        public static MCPServerMode MCPServerMode
        {
            get => (MCPServerMode)SettingsStore.GetInt(MCPServerModeKey, (int)AjisaiFlow.UnityAgent.Editor.MCPServerMode.InProc);
            set => SettingsStore.SetInt(MCPServerModeKey, (int)value);
        }

        /// <summary>
        /// Bridge モード時に Unity ↔ bridge 間 TCP に使う内部ポート。
        /// プロジェクトパスから FNV-1a で派生させて衝突しにくい数値にする。
        /// </summary>
        public static int MCPBridgeInternalPort
        {
            get
            {
                int port = SettingsStore.GetInt("UnityAgent_MCPBridgeInternalPort", 0);
                if (port > 0) return port;
                int derived = DeriveBridgePort(17000, 1000) + 1; // odd number for internal
                SettingsStore.SetInt("UnityAgent_MCPBridgeInternalPort", derived);
                return derived;
            }
        }

        /// <summary>
        /// MCP クライアントが叩く公開ポート。プロジェクトパスから派生 (内部ポート - 1)。
        /// </summary>
        public static int MCPBridgePublicPort
        {
            get
            {
                int port = SettingsStore.GetInt("UnityAgent_MCPBridgePublicPort", 0);
                if (port > 0) return port;
                int derived = DeriveBridgePort(17000, 1000); // even number for public
                SettingsStore.SetInt("UnityAgent_MCPBridgePublicPort", derived);
                return derived;
            }
        }

        /// <summary>
        /// プロジェクトパスを FNV-1a でハッシュして 0..range-1 を base に加算。
        /// 同プロジェクトでは常に同じ値、異プロジェクトでは衝突しにくい。
        /// </summary>
        private static int DeriveBridgePort(int basePort, int range)
        {
            string seed;
            try { seed = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath); }
            catch { seed = "unityagent"; }

            uint hash = 2166136261u;
            foreach (char c in seed)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return basePort + (int)(hash % (uint)range) * 2; // *2 to leave room for paired internal port
        }

        /// <summary>
        /// 確認が必要なツール名のセットを取得。
        /// EditorPrefs から読み込み、キャッシュ。未設定ならデフォルトを返す。
        /// </summary>
        public static HashSet<string> GetConfirmTools()
        {
            if (_confirmToolsCache != null) return _confirmToolsCache;

            string saved = SettingsStore.GetString(ConfirmToolsKey, "");
            if (string.IsNullOrEmpty(saved))
            {
                _confirmToolsCache = new HashSet<string>(DefaultConfirmTools);
            }
            else
            {
                _confirmToolsCache = new HashSet<string>(
                    saved.Split(',').Where(s => !string.IsNullOrEmpty(s)));
            }
            return _confirmToolsCache;
        }

        /// <summary>
        /// 確認が必要なツール名のセットを保存。
        /// </summary>
        public static void SetConfirmTools(HashSet<string> tools)
        {
            _confirmToolsCache = new HashSet<string>(tools);
            SettingsStore.SetString(ConfirmToolsKey, string.Join(",", tools));
        }

        /// <summary>
        /// 指定ツールが確認必要かどうかを返す。
        /// </summary>
        public static bool IsToolConfirmRequired(string toolName)
        {
            return GetConfirmTools().Contains(toolName);
        }

        /// <summary>
        /// 個別ツールの確認ON/OFFを切り替え。
        /// </summary>
        public static void SetToolConfirmRequired(string toolName, bool required)
        {
            var tools = GetConfirmTools();
            if (required)
                tools.Add(toolName);
            else
                tools.Remove(toolName);
            SetConfirmTools(tools);
        }

        // ─── Disabled Skills ───

        public static HashSet<string> GetDisabledSkills()
        {
            if (_disabledSkillsCache != null) return _disabledSkillsCache;

            string saved = SettingsStore.GetString(DisabledSkillsKey, "");
            _disabledSkillsCache = string.IsNullOrEmpty(saved)
                ? new HashSet<string>()
                : new HashSet<string>(saved.Split(',').Where(s => !string.IsNullOrEmpty(s)));
            return _disabledSkillsCache;
        }

        public static void SetDisabledSkills(HashSet<string> skills)
        {
            _disabledSkillsCache = new HashSet<string>(skills);
            SettingsStore.SetString(DisabledSkillsKey, string.Join(",", skills));
        }

        public static bool IsSkillDisabled(string skillName)
        {
            return GetDisabledSkills().Contains(skillName);
        }

        public static void SetSkillDisabled(string skillName, bool disabled)
        {
            var skills = GetDisabledSkills();
            if (disabled)
                skills.Add(skillName);
            else
                skills.Remove(skillName);
            SetDisabledSkills(skills);
        }

        // ─── Disabled Tools / Enabled External Tools ───

        /// <summary>
        /// 内蔵ツール: disabled set にいなければ有効。
        /// 外部ツール: enabled set にいなければ無効。
        /// </summary>
        public static bool IsToolEnabled(string toolName, bool isExternal)
        {
            if (isExternal)
                return GetEnabledExternalTools().Contains(toolName);
            return !GetDisabledTools().Contains(toolName);
        }

        public static void SetToolEnabled(string toolName, bool isExternal, bool enabled)
        {
            if (isExternal)
            {
                var tools = GetEnabledExternalTools();
                if (enabled)
                    tools.Add(toolName);
                else
                    tools.Remove(toolName);
                SetEnabledExternalTools(tools);
            }
            else
            {
                var tools = GetDisabledTools();
                if (enabled)
                    tools.Remove(toolName);
                else
                    tools.Add(toolName);
                SetDisabledTools(tools);
            }
        }

        public static HashSet<string> GetDisabledTools()
        {
            if (_disabledToolsCache != null) return _disabledToolsCache;

            string saved = SettingsStore.GetString(DisabledToolsKey, "");
            _disabledToolsCache = string.IsNullOrEmpty(saved)
                ? new HashSet<string>()
                : new HashSet<string>(saved.Split(',').Where(s => !string.IsNullOrEmpty(s)));
            return _disabledToolsCache;
        }

        public static void SetDisabledTools(HashSet<string> tools)
        {
            _disabledToolsCache = new HashSet<string>(tools);
            SettingsStore.SetString(DisabledToolsKey, string.Join(",", tools));
        }

        public static HashSet<string> GetEnabledExternalTools()
        {
            if (_enabledExternalToolsCache != null) return _enabledExternalToolsCache;

            string saved = SettingsStore.GetString(EnabledExternalToolsKey, "");
            _enabledExternalToolsCache = string.IsNullOrEmpty(saved)
                ? new HashSet<string>()
                : new HashSet<string>(saved.Split(',').Where(s => !string.IsNullOrEmpty(s)));
            return _enabledExternalToolsCache;
        }

        public static void SetEnabledExternalTools(HashSet<string> tools)
        {
            _enabledExternalToolsCache = new HashSet<string>(tools);
            SettingsStore.SetString(EnabledExternalToolsKey, string.Join(",", tools));
        }

        /// <summary>
        /// 確認は UnityAgentCore で一元管理するため常に true を返す。
        /// 既存のツール内呼び出しを変更せずに済む。
        /// </summary>
        public static bool RequestConfirmation(string action, string details)
        {
            return true;
        }

        // ─── Theme ───

        /// <summary>0=Auto, 1=Dark, 2=Light, 3=Custom</summary>
        public static int ThemeMode
        {
            get => SettingsStore.GetInt(ThemeModeKey, 0);
            set
            {
                SettingsStore.SetInt(ThemeModeKey, value);
                _cachedCustomTheme = null;
            }
        }

        public static void SetThemeColor(string name, Color color)
        {
            SettingsStore.SetString(ThemeColorPrefix + name, "#" + ColorUtility.ToHtmlStringRGBA(color));
            _cachedCustomTheme = null;
        }

        public static bool TryGetThemeColor(string name, out Color color)
        {
            string hex = SettingsStore.GetString(ThemeColorPrefix + name, "");
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out color))
                return true;
            color = default;
            return false;
        }

        public static void ClearAllThemeColors()
        {
            foreach (var name in PaletteColorNames)
                SettingsStore.DeleteKey(ThemeColorPrefix + name);
            _cachedCustomTheme = null;
        }

        private static MD3Theme _cachedCustomTheme;

        public static MD3Theme BuildCustomTheme()
        {
            if (_cachedCustomTheme != null) return _cachedCustomTheme;

            var baseTheme = EditorGUIUtility.isProSkin ? MD3Theme.Dark() : MD3Theme.Light();
            var theme = new MD3Theme
            {
                IsDark = baseTheme.IsDark,
                Primary = baseTheme.Primary, OnPrimary = baseTheme.OnPrimary,
                PrimaryContainer = baseTheme.PrimaryContainer, OnPrimaryContainer = baseTheme.OnPrimaryContainer,
                Secondary = baseTheme.Secondary, OnSecondary = baseTheme.OnSecondary,
                SecondaryContainer = baseTheme.SecondaryContainer, OnSecondaryContainer = baseTheme.OnSecondaryContainer,
                Tertiary = baseTheme.Tertiary, OnTertiary = baseTheme.OnTertiary,
                TertiaryContainer = baseTheme.TertiaryContainer, OnTertiaryContainer = baseTheme.OnTertiaryContainer,
                Surface = baseTheme.Surface, OnSurface = baseTheme.OnSurface,
                SurfaceVariant = baseTheme.SurfaceVariant, OnSurfaceVariant = baseTheme.OnSurfaceVariant,
                SurfaceContainerLowest = baseTheme.SurfaceContainerLowest, SurfaceContainerLow = baseTheme.SurfaceContainerLow,
                SurfaceContainer = baseTheme.SurfaceContainer, SurfaceContainerHigh = baseTheme.SurfaceContainerHigh,
                SurfaceContainerHighest = baseTheme.SurfaceContainerHighest,
                Outline = baseTheme.Outline, OutlineVariant = baseTheme.OutlineVariant,
                Error = baseTheme.Error, OnError = baseTheme.OnError,
                InverseSurface = baseTheme.InverseSurface, InverseOnSurface = baseTheme.InverseOnSurface,
                InversePrimary = baseTheme.InversePrimary,
            };
            // Apply stored overrides
            foreach (var name in PaletteColorNames)
            {
                if (TryGetThemeColor(name, out var c))
                    SetThemeColorField(theme, name, c);
            }
            _cachedCustomTheme = theme;
            return theme;
        }

        public static void InvalidateThemeCache()
        {
            _cachedCustomTheme = null;
        }

        public static Color GetThemeColorField(MD3Theme theme, string name)
        {
            switch (name)
            {
                case "Primary": return theme.Primary;
                case "OnPrimary": return theme.OnPrimary;
                case "PrimaryContainer": return theme.PrimaryContainer;
                case "OnPrimaryContainer": return theme.OnPrimaryContainer;
                case "Secondary": return theme.Secondary;
                case "OnSecondary": return theme.OnSecondary;
                case "SecondaryContainer": return theme.SecondaryContainer;
                case "OnSecondaryContainer": return theme.OnSecondaryContainer;
                case "Tertiary": return theme.Tertiary;
                case "OnTertiary": return theme.OnTertiary;
                case "TertiaryContainer": return theme.TertiaryContainer;
                case "OnTertiaryContainer": return theme.OnTertiaryContainer;
                case "Surface": return theme.Surface;
                case "OnSurface": return theme.OnSurface;
                case "SurfaceVariant": return theme.SurfaceVariant;
                case "OnSurfaceVariant": return theme.OnSurfaceVariant;
                case "SurfaceContainerLowest": return theme.SurfaceContainerLowest;
                case "SurfaceContainerLow": return theme.SurfaceContainerLow;
                case "SurfaceContainer": return theme.SurfaceContainer;
                case "SurfaceContainerHigh": return theme.SurfaceContainerHigh;
                case "SurfaceContainerHighest": return theme.SurfaceContainerHighest;
                case "Outline": return theme.Outline;
                case "OutlineVariant": return theme.OutlineVariant;
                case "Error": return theme.Error;
                case "OnError": return theme.OnError;
                case "InverseSurface": return theme.InverseSurface;
                case "InverseOnSurface": return theme.InverseOnSurface;
                case "InversePrimary": return theme.InversePrimary;
                default: return Color.magenta;
            }
        }

        public static void SetThemeColorField(MD3Theme theme, string name, Color color)
        {
            switch (name)
            {
                case "Primary": theme.Primary = color; break;
                case "OnPrimary": theme.OnPrimary = color; break;
                case "PrimaryContainer": theme.PrimaryContainer = color; break;
                case "OnPrimaryContainer": theme.OnPrimaryContainer = color; break;
                case "Secondary": theme.Secondary = color; break;
                case "OnSecondary": theme.OnSecondary = color; break;
                case "SecondaryContainer": theme.SecondaryContainer = color; break;
                case "OnSecondaryContainer": theme.OnSecondaryContainer = color; break;
                case "Tertiary": theme.Tertiary = color; break;
                case "OnTertiary": theme.OnTertiary = color; break;
                case "TertiaryContainer": theme.TertiaryContainer = color; break;
                case "OnTertiaryContainer": theme.OnTertiaryContainer = color; break;
                case "Surface": theme.Surface = color; break;
                case "OnSurface": theme.OnSurface = color; break;
                case "SurfaceVariant": theme.SurfaceVariant = color; break;
                case "OnSurfaceVariant": theme.OnSurfaceVariant = color; break;
                case "SurfaceContainerLowest": theme.SurfaceContainerLowest = color; break;
                case "SurfaceContainerLow": theme.SurfaceContainerLow = color; break;
                case "SurfaceContainer": theme.SurfaceContainer = color; break;
                case "SurfaceContainerHigh": theme.SurfaceContainerHigh = color; break;
                case "SurfaceContainerHighest": theme.SurfaceContainerHighest = color; break;
                case "Outline": theme.Outline = color; break;
                case "OutlineVariant": theme.OutlineVariant = color; break;
                case "Error": theme.Error = color; break;
                case "OnError": theme.OnError = color; break;
                case "InverseSurface": theme.InverseSurface = color; break;
                case "InverseOnSurface": theme.InverseOnSurface = color; break;
                case "InversePrimary": theme.InversePrimary = color; break;
            }
        }

        // ─── Seed Color Generation ───

        public static Color SeedColor
        {
            get
            {
                string hex = SettingsStore.GetString(SeedColorKey, "");
                if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c))
                    return c;
                return new Color(0.40f, 0.31f, 0.64f); // MD3 default purple
            }
            set => SettingsStore.SetString(SeedColorKey, "#" + ColorUtility.ToHtmlStringRGB(value));
        }

        /// <summary>
        /// シードカラーから MD3Theme を自動生成する。
        /// HSL トーンマッピングで Primary/Secondary/Tertiary/Surface/Error を導出。
        /// </summary>
        public static MD3Theme GenerateThemeFromSeed(Color seed, bool darkMode)
        {
            Color.RGBToHSV(seed, out float h, out float s, out float _);
            s = Mathf.Max(s, 0.05f);

            // Secondary: same hue, reduced chroma
            float sh = h;
            float ss = s * 0.35f;

            // Tertiary: hue + 60deg, moderate chroma
            float th = Mathf.Repeat(h + 60f / 360f, 1f);
            float ts = s * 0.5f;

            // Error: fixed red
            const float eh = 0f;
            const float es = 0.75f;

            // Neutral: very low chroma for surface/outline
            float ns = s * 0.06f;
            float nvs = s * 0.15f;

            MD3Theme p;
            if (darkMode)
            {
                p = new MD3Theme
                {
                    IsDark = true,
                    Primary            = Tone(h,  s,   80),
                    OnPrimary          = Tone(h,  s,   20),
                    PrimaryContainer   = Tone(h,  s,   30),
                    OnPrimaryContainer = Tone(h,  s,   90),

                    Secondary            = Tone(sh, ss, 80),
                    OnSecondary          = Tone(sh, ss, 20),
                    SecondaryContainer   = Tone(sh, ss, 30),
                    OnSecondaryContainer = Tone(sh, ss, 90),

                    Tertiary            = Tone(th, ts, 80),
                    OnTertiary          = Tone(th, ts, 20),
                    TertiaryContainer   = Tone(th, ts, 30),
                    OnTertiaryContainer = Tone(th, ts, 90),

                    Surface            = Tone(h, ns,   6),
                    OnSurface          = Tone(h, ns,  90),
                    SurfaceVariant     = Tone(h, nvs, 30),
                    OnSurfaceVariant   = Tone(h, nvs, 80),
                    SurfaceContainerLowest  = Tone(h, ns,  4),
                    SurfaceContainerLow     = Tone(h, ns, 10),
                    SurfaceContainer        = Tone(h, ns, 12),
                    SurfaceContainerHigh    = Tone(h, ns, 17),
                    SurfaceContainerHighest = Tone(h, ns, 22),

                    Outline            = Tone(h, nvs, 60),
                    OutlineVariant     = Tone(h, nvs, 30),

                    Error              = Tone(eh, es, 80),
                    OnError            = Tone(eh, es, 20),

                    InverseSurface     = Tone(h, ns,  90),
                    InverseOnSurface   = Tone(h, ns,  20),
                    InversePrimary     = Tone(h, s,   40),
                };
            }
            else
            {
                p = new MD3Theme
                {
                    IsDark = false,
                    Primary            = Tone(h,  s,   40),
                    OnPrimary          = Tone(h,  s,  100),
                    PrimaryContainer   = Tone(h,  s,   90),
                    OnPrimaryContainer = Tone(h,  s,   10),

                    Secondary            = Tone(sh, ss, 40),
                    OnSecondary          = Tone(sh, ss,100),
                    SecondaryContainer   = Tone(sh, ss, 90),
                    OnSecondaryContainer = Tone(sh, ss, 10),

                    Tertiary            = Tone(th, ts, 40),
                    OnTertiary          = Tone(th, ts,100),
                    TertiaryContainer   = Tone(th, ts, 90),
                    OnTertiaryContainer = Tone(th, ts, 10),

                    Surface            = Tone(h, ns,  99),
                    OnSurface          = Tone(h, ns,  10),
                    SurfaceVariant     = Tone(h, nvs, 90),
                    OnSurfaceVariant   = Tone(h, nvs, 30),
                    SurfaceContainerLowest  = Tone(h, ns, 100),
                    SurfaceContainerLow     = Tone(h, ns,  96),
                    SurfaceContainer        = Tone(h, ns,  94),
                    SurfaceContainerHigh    = Tone(h, ns,  92),
                    SurfaceContainerHighest = Tone(h, ns,  90),

                    Outline            = Tone(h, nvs, 50),
                    OutlineVariant     = Tone(h, nvs, 80),

                    Error              = Tone(eh, es, 40),
                    OnError            = Tone(eh, es,100),

                    InverseSurface     = Tone(h, ns,  20),
                    InverseOnSurface   = Tone(h, ns,  95),
                    InversePrimary     = Tone(h, s,   80),
                };
            }
            return p;
        }

        /// <summary>
        /// HSL ベースのトーンカラー生成。
        /// hue: 0-1, saturation: 0-1, tone: 0(黒)-100(白)
        /// </summary>
        private static Color Tone(float hue, float saturation, int tone)
        {
            // HSL → HSV 変換して Color.HSVToRGB を使う
            float l = tone / 100f;
            float v = l + saturation * Mathf.Min(l, 1f - l);
            float sv = v > 0.001f ? 2f * (1f - l / v) : 0f;
            return Color.HSVToRGB(Mathf.Repeat(hue, 1f), sv, v);
        }
    }
}
