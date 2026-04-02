using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class TranslationManagementWindow : EditorWindow
    {
        public static event Action OnTranslationsUpdated;

        // ─── UI state ───
        private int _selectedLangIndex;

        // ─── Translation state ───
        private bool _isTranslating;
        private float _translateProgress;
        private string _translateStatus = "";
        private Stack<IEnumerator> _translationStack;

        // ─── Translation log ───
        private List<string> _translateLog = new List<string>();
        private int _lastLogCount;
        private bool _showTranslateLog;
        private Vector2 _logScrollPos;

        // ─── Tool list cache ───
        private List<(string name, string description)> _tools;

        // ─── Bulk progress cache (avoid per-frame disk reads) ───
        private List<(string code, string label, int untranslated, int total)> _cachedBulkProgress;
        private int _cachedBulkTotalUntranslated;

        [MenuItem("Window/紫陽花広場/Translation Management")]
        public static void Open()
        {
            if (UpdateChecker.IsBlocked)
            {
                EditorUtility.DisplayDialog(M("バージョン期限切れ"),
                    UpdateChecker.IsExpired
                        ? M("このバージョンは期限切れです。最新バージョンを BOOTH からダウンロードしてください。")
                        : M("ライセンス認証に失敗しました。インターネット接続を確認し、Unity を再起動してください。"),
                    "OK");
                return;
            }
            var window = GetWindow<TranslationManagementWindow>();
            window.titleContent = new GUIContent(M("翻訳管理"));
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnEnable()
        {
            string currentLang = AgentSettings.UILanguage;
            _selectedLangIndex = 0;
            for (int i = 0; i < AgentSettings.SupportedLanguages.Length; i++)
            {
                if (AgentSettings.SupportedLanguages[i].code == currentLang)
                {
                    _selectedLangIndex = i;
                    break;
                }
            }

            RefreshToolList();
            RefreshBulkProgress();
        }

        private void RefreshBulkProgress()
        {
            _cachedBulkProgress = L10n.GetAllLanguageProgress();
            _cachedBulkTotalUntranslated = 0;
            foreach (var (code, label, untranslated, total) in _cachedBulkProgress)
                _cachedBulkTotalUntranslated += untranslated;
        }

        private void RefreshToolList()
        {
            var registryTools = ToolRegistry.GetAllTools();
            _tools = new List<(string name, string description)>(registryTools.Count);
            foreach (var t in registryTools)
            {
                string desc = t.attribute?.Description ?? "";
                _tools.Add((t.method.Name, desc));
            }
        }

        private void Update()
        {
            if (_translationStack != null && _translationStack.Count > 0)
            {
                try
                {
                    var current = _translationStack.Peek();
                    bool moved = current.MoveNext();

                    if (!moved)
                    {
                        _translationStack.Pop();
                        if (_translationStack.Count == 0)
                            _translationStack = null;
                    }
                    else
                    {
                        var yielded = current.Current;
                        if (yielded is IEnumerator nested)
                        {
                            _translationStack.Push(nested);
                        }
                        else if (yielded is AsyncOperation asyncOp)
                        {
                            if (!asyncOp.isDone)
                                _translationStack.Push(WaitForAsyncOp(asyncOp));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TranslationManagement] 翻訳コルーチンエラー: {ex.Message}\n{ex.StackTrace}");
                    _translationStack = null;
                    _isTranslating = false;
                    _translateStatus = string.Format(M("エラー: {0}"), ex.Message);
                    _translateLog.Add($"[ERROR] {ex.Message}");
                    _translateLog.Add($"[STACKTRACE] {ex.StackTrace}");
                    Repaint();
                }
            }
        }

        private static IEnumerator WaitForAsyncOp(AsyncOperation op)
        {
            while (!op.isDone)
                yield return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // OnGUI
        // ═══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (_tools == null) RefreshToolList();

            var langs = AgentSettings.SupportedLanguages;
            string[] labels = new string[langs.Length];
            for (int i = 0; i < langs.Length; i++)
                labels[i] = langs[i].label;

            // ─── 言語セレクタ ───
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                EditorGUILayout.LabelField(M("言語"), GUILayout.Width(30));
                int newIndex = EditorGUILayout.Popup(_selectedLangIndex, labels, EditorStyles.toolbarPopup, GUILayout.Width(120));
                if (newIndex != _selectedLangIndex)
                {
                    _selectedLangIndex = newIndex;
                    AgentSettings.UILanguage = langs[newIndex].code;
                    titleContent = new GUIContent(M("翻訳管理"));
                    L10n.Reload();
                    RefreshBulkProgress();
                    OnTranslationsUpdated?.Invoke();
                }
            }
            EditorGUILayout.EndHorizontal();

            string langCode = langs[_selectedLangIndex].code;
            bool needsToolTranslation = langCode != "en";
            bool needsUITranslation = langCode != "ja";

            if (!needsToolTranslation && !needsUITranslation)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox(M("この言語は翻訳不要です。"), MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8);

            // ─── 全言語一括セクション (UI文字列) ───
            DrawBulkTranslationSection();

            EditorGUILayout.Space(8);

            // ─── ツール説明翻訳セクション ───
            if (needsToolTranslation)
            {
                DrawToolTranslationSection(langCode);
                EditorGUILayout.Space(8);
            }

            // ─── UI文字列翻訳セクション ───
            if (needsUITranslation)
            {
                DrawUITranslationSection(langCode);
                EditorGUILayout.Space(8);
            }

            // ─── キャッシュクリア ───
            DrawCacheClearSection(langCode, needsToolTranslation, needsUITranslation);

            // ─── 翻訳ログ ───
            DrawTranslationLog();
        }

        // ─── 全言語一括セクション ───

        private void DrawBulkTranslationSection()
        {
            EditorGUILayout.LabelField(M("全言語一括 (UI文字列)"), EditorStyles.boldLabel);

            if (_cachedBulkProgress == null) RefreshBulkProgress();

            int totalUntranslated = _cachedBulkTotalUntranslated;
            var summaryParts = new List<string>();
            foreach (var (code, label, untranslated, total) in _cachedBulkProgress)
            {
                if (untranslated > 0)
                    summaryParts.Add($"{label}: {untranslated}");
            }

            if (totalUntranslated == 0)
            {
                EditorGUILayout.HelpBox(M("全言語の翻訳が完了しています。"), MessageType.Info);
            }
            else
            {
                string summary = string.Format(M("未翻訳: {0}件 ({1})"),
                    totalUntranslated, string.Join(", ", summaryParts));
                EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = totalUntranslated > 0;
                if (GUILayout.Button(M("全言語の未翻訳をExport"), GUILayout.Height(24)))
                {
                    string exported = L10n.ExportAllUntranslated();
                    EditorGUIUtility.systemCopyBuffer = exported;
                    ShowNotification(new GUIContent(
                        string.Format(M("クリップボードにコピーしました ({0}件)"), totalUntranslated)));
                }
                GUI.enabled = true;

                if (GUILayout.Button(M("一括インポート"), GUILayout.Height(24)))
                {
                    TranslationImportWindow.OpenBulk(() =>
                    {
                        RefreshBulkProgress();
                        OnTranslationsUpdated?.Invoke();
                        Repaint();
                    });
                }

                // 全言語アセット保存
                if (GUILayout.Button(M("全言語をアセットに保存"), GUILayout.Height(24)))
                {
                    int totalSaved = 0;
                    foreach (var (code, label) in AgentSettings.SupportedLanguages)
                    {
                        if (code == "ja") continue;
                        int count = L10n.SaveToAsset(code);
                        totalSaved += count;
                    }
                    if (totalSaved > 0)
                    {
                        AssetDatabase.Refresh();
                        RefreshBulkProgress();
                        ShowNotification(new GUIContent(
                            string.Format(M("全言語をアセットに保存しました ({0}件)"), totalSaved)));
                    }
                    else
                    {
                        ShowNotification(new GUIContent(M("保存する翻訳がありません")));
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── ツール説明翻訳セクション ───

        private void DrawToolTranslationSection(string langCode)
        {
            EditorGUILayout.LabelField(M("ツール説明翻訳"), EditorStyles.boldLabel);

            var (toolTranslated, toolTotal) = ToolTranslationService.GetProgress(langCode, _tools?.Count ?? 0);

            EditorGUILayout.BeginHorizontal();
            {
                // 進捗バー
                Rect progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(18));
                float ratio = toolTotal > 0 ? (float)toolTranslated / toolTotal : 0;
                EditorGUI.ProgressBar(progressRect, ratio, $"{toolTranslated}/{toolTotal}");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                if (_isTranslating)
                {
                    Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(18));
                    EditorGUI.ProgressBar(rect, _translateProgress, _translateStatus);
                }
                else
                {
                    // AI翻訳ボタン
                    bool allToolsDone = toolTranslated >= toolTotal;
                    GUI.enabled = !allToolsDone;
                    if (GUILayout.Button(M("AI翻訳"), GUILayout.Height(24)))
                    {
                        if (ConfirmTranslationCost(langCode))
                            StartTranslation(langCode);
                    }
                    GUI.enabled = true;

                    // Exportボタン
                    if (GUILayout.Button(M("ツール説明をExport"), GUILayout.Height(24)))
                    {
                        string exported = ToolTranslationService.ExportForTranslation(_tools, langCode);
                        EditorGUIUtility.systemCopyBuffer = exported;
                        ShowNotification(new GUIContent(
                            string.Format(M("クリップボードにコピーしました ({0}件)"), _tools.Count)));
                    }

                    // Importボタン
                    if (GUILayout.Button(M("インポート"), GUILayout.Height(24)))
                    {
                        TranslationImportWindow.Open(langCode, () =>
                        {
                            OnTranslationsUpdated?.Invoke();
                            Repaint();
                        }, hasToolOption: true, hasUIOption: false);
                    }

                    // アセットに保存ボタン
                    GUI.enabled = toolTranslated > 0;
                    if (GUILayout.Button(M("アセットに保存"), GUILayout.Height(24)))
                    {
                        int count = ToolTranslationService.SaveToAsset(langCode);
                        if (count > 0)
                        {
                            AssetDatabase.Refresh();
                            ShowNotification(new GUIContent(
                                string.Format(M("アセットに保存しました ({0}件)"), count)));
                        }
                    }
                    GUI.enabled = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── UI文字列翻訳セクション ───

        private void DrawUITranslationSection(string langCode)
        {
            EditorGUILayout.LabelField(M("UI文字列翻訳"), EditorStyles.boldLabel);

            var (uiTranslated, uiTotal) = L10n.GetProgress(langCode);

            EditorGUILayout.BeginHorizontal();
            {
                Rect progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(18));
                float ratio = uiTotal > 0 ? (float)uiTranslated / uiTotal : 0;
                EditorGUI.ProgressBar(progressRect, ratio, $"{uiTranslated}/{uiTotal}");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                // Exportボタン
                if (GUILayout.Button(M("UI文字列をExport"), GUILayout.Height(24)))
                {
                    string exported = L10n.ExportForTranslation(langCode);
                    EditorGUIUtility.systemCopyBuffer = exported;
                    ShowNotification(new GUIContent(
                        string.Format(M("クリップボードにコピーしました ({0}件)"), L10n.GetAllKeys().Count)));
                }

                // Importボタン
                if (GUILayout.Button(M("インポート"), GUILayout.Height(24)))
                {
                    TranslationImportWindow.Open(langCode, () =>
                    {
                        RefreshBulkProgress();
                        OnTranslationsUpdated?.Invoke();
                        Repaint();
                    }, hasToolOption: false, hasUIOption: true);
                }

                // アセットに保存ボタン
                GUI.enabled = uiTranslated > 0;
                if (GUILayout.Button(M("アセットに保存"), GUILayout.Height(24)))
                {
                    int count = L10n.SaveToAsset(langCode);
                    if (count > 0)
                    {
                        AssetDatabase.Refresh();
                        ShowNotification(new GUIContent(
                            string.Format(M("アセットに保存しました ({0}件)"), count)));
                    }
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── キャッシュクリア ───

        private void DrawCacheClearSection(string langCode, bool needsToolTranslation, bool needsUITranslation)
        {
            var langs = AgentSettings.SupportedLanguages;

            int toolTranslated = 0, uiTranslated = 0;
            if (needsToolTranslation)
                (toolTranslated, _) = ToolTranslationService.GetProgress(langCode, _tools?.Count ?? 0);
            if (needsUITranslation)
                (uiTranslated, _) = L10n.GetProgress(langCode);

            int totalTranslated = toolTranslated + uiTranslated;
            if (totalTranslated > 0)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(M("キャッシュクリア"), GUILayout.Height(24), GUILayout.Width(120)))
                    {
                        if (EditorUtility.DisplayDialog(M("キャッシュクリア"),
                            string.Format(M("{0}の翻訳キャッシュを削除しますか？"), langs[_selectedLangIndex].label),
                            M("削除"), M("キャンセル")))
                        {
                            if (needsToolTranslation)
                                ToolTranslationService.ClearCache(langCode);
                            if (needsUITranslation)
                                L10n.ClearCache(langCode);
                            RefreshBulkProgress();
                            OnTranslationsUpdated?.Invoke();
                            Repaint();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ─── 翻訳ログ ───

        private void DrawTranslationLog()
        {
            if (_translateLog.Count == 0) return;

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(_showTranslateLog ? "\u25BC " + M("翻訳ログ") : "\u25B6 " + M("翻訳ログ"),
                    EditorStyles.foldout))
                    _showTranslateLog = !_showTranslateLog;
            }
            EditorGUILayout.EndHorizontal();

            if (_showTranslateLog)
            {
                if (_translateLog.Count != _lastLogCount)
                {
                    _lastLogCount = _translateLog.Count;
                    _logScrollPos.y = float.MaxValue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxHeight(150));
                _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos);
                foreach (var line in _translateLog)
                {
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(M("ログクリア"), EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    _translateLog.Clear();
                    _showTranslateLog = false;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Translation logic
        // ═══════════════════════════════════════════════════════════════

        private bool ConfirmTranslationCost(string langCode)
        {
            if (_tools == null) return false;

            var est = ToolTranslationService.EstimateCost(_tools, langCode);

            if (est.untranslatedCount == 0) return true;

            string langName = AgentSettings.SupportedLanguages[_selectedLangIndex].label;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(M("接続中のAI ({0}) を使用してツール説明を{1}に翻訳します。"), est.modelName, langName));
            sb.AppendLine(M("APIトークンを消費します。"));
            sb.AppendLine();
            sb.AppendLine(string.Format(M("  未翻訳ツール数: {0}件"), est.untranslatedCount));
            sb.AppendLine(string.Format(M("  バッチ数: {0}回"), est.batches));
            sb.AppendLine(string.Format(M("  合計文字数: {0}文字"), est.totalDescChars.ToString("#,0")));
            sb.AppendLine();
            sb.AppendLine(string.Format(M("  推定入力トークン: ~{0}"), est.inputTokens.ToString("#,0")));
            sb.AppendLine(string.Format(M("  推定出力トークン: ~{0}"), est.outputTokens.ToString("#,0")));
            sb.AppendLine(string.Format(M("  合計: ~{0} トークン"), (est.inputTokens + est.outputTokens).ToString("#,0")));
            sb.AppendLine();

            if (est.costUsd >= 0)
            {
                double costJpy = est.costUsd * 150;
                sb.AppendLine(string.Format(M("  推定コスト: ~${0} USD (~{1}円)"), est.costUsd.ToString("F4"), costJpy.ToString("F1")));
            }
            else
            {
                sb.AppendLine(M("  推定コスト: 不明 (モデルの料金表にありません)"));
            }

            sb.AppendLine();
            sb.AppendLine(M("※ 推定値は目安です。実際の消費量は異なる場合があります。"));

            return EditorUtility.DisplayDialog(
                M("AI翻訳の確認"),
                sb.ToString(),
                M("翻訳を実行"),
                M("キャンセル"));
        }

        private void StartTranslation(string langCode)
        {
            if (_tools == null) return;

            _isTranslating = true;
            _translateProgress = 0f;
            _translateStatus = M("準備中...");
            _translateLog.Clear();
            _showTranslateLog = true;
            _translateLog.Add(string.Format(M("翻訳開始: {0} ({1})"),
                AgentSettings.SupportedLanguages[_selectedLangIndex].label, langCode));
            _translateLog.Add(string.Format(M("ツール数: {0}"), _tools.Count));

            var coroutine = ToolTranslationService.TranslateAll(
                _tools,
                langCode,
                onProgress: p =>
                {
                    _translateProgress = p;
                    Repaint();
                },
                onStatus: s =>
                {
                    _translateStatus = s;
                    Repaint();
                },
                onLog: msg =>
                {
                    _translateLog.Add(msg);
                    Debug.Log($"[TranslationManagement] {msg}");
                    Repaint();
                },
                onComplete: () =>
                {
                    _isTranslating = false;
                    _translateStatus = "";
                    _translateLog.Add(M("完了"));
                    Debug.Log("[TranslationManagement] 完了");
                    OnTranslationsUpdated?.Invoke();
                    Repaint();
                },
                onError: e =>
                {
                    _isTranslating = false;
                    _translateStatus = e;
                    _translateLog.Add($"[ERROR] {e}");
                    Debug.LogError($"[TranslationManagement] エラー: {e}");
                    OnTranslationsUpdated?.Invoke();
                    Repaint();
                });

            _translationStack = new Stack<IEnumerator>();
            _translationStack.Push(coroutine);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Translation Import Window
    // ═══════════════════════════════════════════════════════════════

    public class TranslationImportWindow : EditorWindow
    {
        private string _langCode; // null = bulk mode
        private string _inputText = "";
        private Vector2 _scrollPos;
        private Action _onImported;
        private int _importType; // 0 = ツール説明, 1 = UI文字列
        private bool _hasToolOption;
        private bool _hasUIOption;
        private bool _isBulkMode;

        public static void Open(string langCode, Action onImported,
            bool hasToolOption = true, bool hasUIOption = true)
        {
            var win = GetWindow<TranslationImportWindow>(true, M("翻訳インポート"));
            win._langCode = langCode;
            win._onImported = onImported;
            win._hasToolOption = hasToolOption;
            win._hasUIOption = hasUIOption;
            win._importType = hasToolOption ? 0 : 1;
            win._isBulkMode = false;
            win.minSize = new Vector2(500, 400);

            string clip = EditorGUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip) && clip.Contains("{") && clip.Contains("}"))
                win._inputText = clip;

            win.Show();
        }

        public static void OpenBulk(Action onImported)
        {
            var win = GetWindow<TranslationImportWindow>(true, M("全言語一括インポート"));
            win._langCode = null;
            win._onImported = onImported;
            win._hasToolOption = false;
            win._hasUIOption = true;
            win._importType = 1;
            win._isBulkMode = true;
            win.minSize = new Vector2(500, 400);

            string clip = EditorGUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip) && clip.Contains("{") && clip.Contains("}"))
                win._inputText = clip;

            win.Show();
        }

        private void OnGUI()
        {
            if (_isBulkMode)
            {
                DrawBulkGUI();
                return;
            }

            string langName = "";
            foreach (var lang in AgentSettings.SupportedLanguages)
            {
                if (lang.code == _langCode) { langName = lang.label; break; }
            }

            EditorGUILayout.LabelField(
                string.Format(M("翻訳先: {0} ({1})"), langName, _langCode),
                EditorStyles.boldLabel);

            if (_hasToolOption && _hasUIOption)
            {
                string[] typeLabels = { M("ツール説明"), M("UI文字列") };
                _importType = EditorGUILayout.Popup(M("インポート種別"), _importType, typeLabels);
            }
            else
            {
                string typeName = _importType == 0 ? M("ツール説明") : M("UI文字列");
                EditorGUILayout.LabelField(string.Format(M("インポート種別: {0}"), typeName));
            }

            EditorGUILayout.Space(4);

            if (_importType == 0)
            {
                EditorGUILayout.HelpBox(
                    M("他のAIから返された翻訳結果（JSON）を貼り付けてください。") + "\n" +
                    M("形式: { \"ToolName\": \"翻訳済み説明\", ... }"),
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    M("他のAIから返された翻訳結果（JSON）を貼り付けてください。") + "\n" +
                    M("形式: { \"日本語キー\": \"翻訳済みテキスト\", ... }"),
                    MessageType.Info);
            }

            DrawImportArea(() =>
            {
                int count;
                if (_importType == 0)
                    count = ToolTranslationService.ImportTranslations(_inputText, _langCode);
                else
                    count = L10n.ImportTranslations(_inputText, _langCode);
                return count;
            },
            () => _importType == 0
                ? M("形式: { \"ToolName\": \"翻訳済み説明\", ... }")
                : M("形式: { \"日本語キー\": \"翻訳済みテキスト\", ... }"));
        }

        private void DrawBulkGUI()
        {
            EditorGUILayout.LabelField(M("全言語一括インポート (UI文字列)"), EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                M("翻訳済みのJSONを貼り付けてください。") + "\n" +
                M("形式: { \"en\": { \"日本語キー\": \"Translation\", ... }, \"ko\": { ... } }"),
                MessageType.Info);

            DrawImportArea(() => L10n.ImportAllTranslations(_inputText),
                () => M("形式: { \"en\": { \"日本語キー\": \"Translation\", ... }, \"ko\": { ... } }"));
        }

        private void DrawImportArea(System.Func<int> doImport, System.Func<string> getExpectedFormat)
        {
            EditorGUILayout.Space(4);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            _inputText = EditorGUILayout.TextArea(_inputText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(M("クリップボードから貼り付け"), GUILayout.Height(28)))
                {
                    _inputText = EditorGUIUtility.systemCopyBuffer ?? "";
                }

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.85f, 0.4f);
                GUI.enabled = !string.IsNullOrEmpty(_inputText);
                if (GUILayout.Button(M("インポート"), GUILayout.Height(28), GUILayout.Width(120)))
                {
                    int count = doImport();

                    if (count > 0)
                    {
                        _onImported?.Invoke();
                        EditorUtility.DisplayDialog(M("インポート完了"),
                            string.Format(M("{0}件の翻訳をインポートしました。"), count), "OK");
                        Close();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(M("インポート失敗"),
                            M("JSONのパースに失敗しました。") + "\n" +
                            M("形式を確認してください。") + "\n\n" +
                            getExpectedFormat(), "OK");
                    }
                }
                GUI.enabled = true;
                GUI.backgroundColor = prevBg;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
        }
    }
}
