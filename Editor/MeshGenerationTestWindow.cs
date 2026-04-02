using System;
using System.Collections;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class MeshGenerationTestWindow : EditorWindow
    {
        // ─── UI state ───
        private int _tabIndex;
        private Vector2 _scrollPos;
        private Vector2 _resultScrollPos;

        // ─── Text-to-3D params ───
        private string _textPrompt = "";
        private string _textSavePath = "Assets";
        private int _textTopologyIndex;
        private int _textPolycount = 30000;
        private int _textAiModelIndex;

        // ─── Image-to-3D params ───
        private string _imagePath = "";
        private string _imageSavePath = "Assets";
        private int _imageTopologyIndex;
        private int _imagePolycount = 30000;
        private int _imageAiModelIndex;

        // ─── Execution ───
        private IEnumerator _runningCoroutine;
        private bool _isExecuting;
        private string _lastResult = "";

        // ─── Styles ───
        private GUIStyle _resultStyle;

        // ─── Constants ───
        private static readonly string[] TabLabels = { "Text-to-3D", "Image-to-3D" };
        private static readonly string[] TopologyOptions = { "triangle", "quad" };
        private static readonly string[] TopologyLabels = { "Triangle", "Quad" };
        private static readonly string[] AiModelOptions = { "latest", "meshy-6", "meshy-5" };
        private static readonly string[] AiModelLabels = { "Latest", "Meshy-6", "Meshy-5" };

        [MenuItem("Window/紫陽花広場/Mesh Generation")]
        public static void ShowWindow()
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
            var window = GetWindow<MeshGenerationTestWindow>();
            window.titleContent = new GUIContent("Mesh Generation (Meshy AI)");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void Update()
        {
            if (_runningCoroutine != null)
            {
                try
                {
                    if (!_runningCoroutine.MoveNext())
                    {
                        _runningCoroutine = null;
                        _isExecuting = false;
                        Repaint();
                    }
                    else if (_runningCoroutine.Current is string str)
                    {
                        _lastResult = str;
                        Repaint();
                    }
                }
                catch (Exception ex)
                {
                    _lastResult = $"Error: {ex.Message}\n{ex.StackTrace}";
                    _runningCoroutine = null;
                    _isExecuting = false;
                    Repaint();
                }
            }
        }

        private void OnGUI()
        {
            if (_resultStyle == null)
            {
                _resultStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Tab
            _tabIndex = GUILayout.Toolbar(_tabIndex, TabLabels);
            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(_isExecuting))
            {
                if (_tabIndex == 0)
                    DrawTextTo3D();
                else
                    DrawImageTo3D();
            }

            EditorGUILayout.Space(8);

            // Progress
            if (_isExecuting && ToolProgress.IsActive)
            {
                EditorGUILayout.LabelField(ToolProgress.Status ?? "処理中...", EditorStyles.boldLabel);
                var rect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(rect, ToolProgress.Progress, $"{(ToolProgress.Progress * 100):F0}%");
                if (!string.IsNullOrEmpty(ToolProgress.Detail))
                    EditorGUILayout.LabelField(ToolProgress.Detail, EditorStyles.miniLabel);
                EditorGUILayout.Space(4);
            }

            // Result
            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                _resultScrollPos = EditorGUILayout.BeginScrollView(_resultScrollPos,
                    GUILayout.MinHeight(60), GUILayout.MaxHeight(200));
                EditorGUILayout.TextArea(_lastResult, _resultStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Text-to-3D ───

        private void DrawTextTo3D()
        {
            EditorGUILayout.LabelField("Prompt", EditorStyles.boldLabel);
            _textPrompt = EditorGUILayout.TextArea(_textPrompt, GUILayout.MinHeight(60));

            EditorGUILayout.Space(4);
            _textSavePath = EditorGUILayout.TextField("Save Path", _textSavePath);
            _textTopologyIndex = EditorGUILayout.Popup("Topology", _textTopologyIndex, TopologyLabels);
            _textPolycount = EditorGUILayout.IntSlider("Target Polycount", _textPolycount, 100, 300000);
            _textAiModelIndex = EditorGUILayout.Popup("AI Model", _textAiModelIndex, AiModelLabels);

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_textPrompt)))
            {
                if (GUILayout.Button("生成", GUILayout.Height(30)))
                {
                    _lastResult = "";
                    _isExecuting = true;
                    _runningCoroutine = MeshGenerationTools.GenerateMeshFromText(
                        _textPrompt,
                        _textSavePath,
                        TopologyOptions[_textTopologyIndex],
                        _textPolycount,
                        AiModelOptions[_textAiModelIndex]);
                }
            }
        }

        // ─── Image-to-3D ───

        private void DrawImageTo3D()
        {
            EditorGUILayout.LabelField("Image", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _imagePath = EditorGUILayout.TextField(_imagePath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("Select Image", "Assets", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert absolute path to project-relative path
                    string dataPath = Application.dataPath;
                    if (path.StartsWith(dataPath))
                        _imagePath = "Assets" + path.Substring(dataPath.Length);
                    else
                        _imagePath = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            _imageSavePath = EditorGUILayout.TextField("Save Path", _imageSavePath);
            _imageTopologyIndex = EditorGUILayout.Popup("Topology", _imageTopologyIndex, TopologyLabels);
            _imagePolycount = EditorGUILayout.IntSlider("Target Polycount", _imagePolycount, 100, 300000);
            _imageAiModelIndex = EditorGUILayout.Popup("AI Model", _imageAiModelIndex, AiModelLabels);

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_imagePath)))
            {
                if (GUILayout.Button("生成", GUILayout.Height(30)))
                {
                    _lastResult = "";
                    _isExecuting = true;
                    _runningCoroutine = MeshGenerationTools.GenerateMeshFromImage(
                        _imagePath,
                        _imageSavePath,
                        TopologyOptions[_imageTopologyIndex],
                        _imagePolycount,
                        AiModelOptions[_imageAiModelIndex]);
                }
            }
        }
    }
}
