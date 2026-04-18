using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Debug = UnityEngine.Debug;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class PoseEstimationWindow : EditorWindow
    {
        private enum PoseModel { ROMP, MediaPipe, WHAM }

        // ── UI State ──
        private string _videoPath = "";
        private string _pythonPath = "";
        private float _targetFps = 30f;
        private string _outputFolder = "Assets/Animations";
        private Animator _targetAnimator;
        private Vector2 _scrollPos;
        private PoseModel _selectedModel = PoseModel.ROMP;
        private string _whamDir = "";

        // ── Process State ──
        private Process _process;
        private float _progress;
        private string _statusMessage = "";
        private bool _isRunning;
        private string _jsonOutputPath;
        private string _lastCreatedClipPath;
        private StringBuilder _stderrBuffer;

        // ── Python パス ──
        private static readonly string ScriptDir = Path.GetFullPath(
            Path.Combine("Assets", "紫陽花広場", "UnityAgent", "Editor", "Python~"));

        private static readonly string MediaPipeScript =
            Path.Combine(ScriptDir, "pose_estimator.py");

        private static readonly string RompScript =
            Path.Combine(ScriptDir, "romp_estimator.py");

        private static readonly string WhamScript =
            Path.Combine(ScriptDir, "wham_estimator.py");

        private static readonly string RequirementsFile =
            Path.Combine(ScriptDir, "requirements.txt");

        // ── HumanBodyBones マッピング (SMPL 24 関節対応) ──
        private static readonly Dictionary<string, HumanBodyBones> BoneNameMap =
            new Dictionary<string, HumanBodyBones>
            {
                { "Hips", HumanBodyBones.Hips },
                { "Spine", HumanBodyBones.Spine },
                { "Chest", HumanBodyBones.Chest },
                { "UpperChest", HumanBodyBones.UpperChest },
                { "Neck", HumanBodyBones.Neck },
                { "Head", HumanBodyBones.Head },
                { "LeftShoulder", HumanBodyBones.LeftShoulder },
                { "LeftUpperArm", HumanBodyBones.LeftUpperArm },
                { "LeftLowerArm", HumanBodyBones.LeftLowerArm },
                { "LeftHand", HumanBodyBones.LeftHand },
                { "RightShoulder", HumanBodyBones.RightShoulder },
                { "RightUpperArm", HumanBodyBones.RightUpperArm },
                { "RightLowerArm", HumanBodyBones.RightLowerArm },
                { "RightHand", HumanBodyBones.RightHand },
                { "LeftUpperLeg", HumanBodyBones.LeftUpperLeg },
                { "LeftLowerLeg", HumanBodyBones.LeftLowerLeg },
                { "LeftFoot", HumanBodyBones.LeftFoot },
                { "LeftToes", HumanBodyBones.LeftToes },
                { "RightUpperLeg", HumanBodyBones.RightUpperLeg },
                { "RightLowerLeg", HumanBodyBones.RightLowerLeg },
                { "RightFoot", HumanBodyBones.RightFoot },
                { "RightToes", HumanBodyBones.RightToes },
            };

        [MenuItem("Window/紫陽花広場/Pose Estimation")]
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
            GetWindow<PoseEstimationWindow>(M("ポーズ推定"));
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_pythonPath))
                _pythonPath = DetectPython();
        }

        private void OnDisable()
        {
            KillProcess();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(M("姿勢推定 → AnimationClip 変換"), EditorStyles.boldLabel);
            DrawSeparator();

            DrawInputSection();
            EditorGUILayout.Space(4);
            DrawSettingsSection();
            EditorGUILayout.Space(4);
            DrawExecuteSection();

            if (!string.IsNullOrEmpty(_lastCreatedClipPath))
            {
                EditorGUILayout.Space(8);
                DrawResultSection();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawInputSection()
        {
            EditorGUILayout.LabelField(M("入力"), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _videoPath = EditorGUILayout.TextField(M("動画ファイル"), _videoPath);
            if (GUILayout.Button(M("選択"), GUILayout.Width(50)))
            {
                string path = EditorUtility.OpenFilePanel(
                    M("動画ファイルを選択"), "",
                    "mp4,avi,mov,mkv,webm");
                if (!string.IsNullOrEmpty(path))
                    _videoPath = path;
            }
            EditorGUILayout.EndHorizontal();

            // Target Animator
            _targetAnimator = (Animator)EditorGUILayout.ObjectField(
                M("対象 Animator"), _targetAnimator, typeof(Animator), true);

            if (_targetAnimator == null)
            {
                EditorGUILayout.HelpBox(
                    M("Humanoid Animator を設定してください。ボーンパスの解決に使用します。"),
                    MessageType.Info);
            }
            else if (!_targetAnimator.isHuman)
            {
                EditorGUILayout.HelpBox(
                    M("選択された Animator は Humanoid ではありません。"),
                    MessageType.Warning);
            }
        }

        private void DrawSettingsSection()
        {
            EditorGUILayout.LabelField(M("設定"), EditorStyles.boldLabel);

            // Model selection
            _selectedModel = (PoseModel)EditorGUILayout.EnumPopup(M("推定モデル"), _selectedModel);

            if (_selectedModel == PoseModel.ROMP)
            {
                EditorGUILayout.HelpBox(
                    M("ROMP: SMPL ベースの高精度姿勢推定（pip install simple-romp）\n" +
                    "初回セットアップ:\n" +
                    "  1. pip install simple-romp\n" +
                    "  2. https://smpl.is.tue.mpg.de/ で無料登録\n" +
                    "  3. SMPL_NEUTRAL.pkl をダウンロード\n" +
                    "  4. romp.prepare_smpl -source_dir=<ダウンロード先>"),
                    MessageType.Info);
            }
            else if (_selectedModel == PoseModel.WHAM)
            {
                EditorGUILayout.BeginHorizontal();
                _whamDir = EditorGUILayout.TextField(M("WHAM ディレクトリ"), _whamDir);
                if (GUILayout.Button(M("選択"), GUILayout.Width(50)))
                {
                    string path = EditorUtility.OpenFolderPanel(
                        M("WHAM リポジトリを選択"), _whamDir, "");
                    if (!string.IsNullOrEmpty(path))
                        _whamDir = path;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    M("WHAM 環境の Python パスを指定してください（例: conda env の python.exe）。\n" +
                    "セットアップ: git clone WHAM → conda create → pip install → fetch_demo_data.sh"),
                    MessageType.Info);
            }

            // Python path
            EditorGUILayout.BeginHorizontal();
            _pythonPath = EditorGUILayout.TextField(M("Python パス"), _pythonPath);
            if (GUILayout.Button(M("検出"), GUILayout.Width(50)))
                _pythonPath = DetectPython();
            EditorGUILayout.EndHorizontal();

            _targetFps = EditorGUILayout.FloatField(M("出力 FPS"), _targetFps);
            if (_targetFps <= 0) _targetFps = 30f;

            // Output folder
            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField(M("出力フォルダ"), _outputFolder);
            if (GUILayout.Button(M("選択"), GUILayout.Width(50)))
            {
                string path = EditorUtility.OpenFolderPanel(M("出力フォルダを選択"), _outputFolder, "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert to relative path if inside Assets
                    string dataPath = Application.dataPath;
                    if (path.StartsWith(dataPath))
                        path = "Assets" + path.Substring(dataPath.Length);
                    _outputFolder = path;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawExecuteSection()
        {
            DrawSeparator();

            if (_isRunning)
            {
                EditorGUILayout.LabelField(M("処理中..."), EditorStyles.boldLabel);

                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, _progress, $"{(_progress * 100):F0}%");

                if (!string.IsNullOrEmpty(_statusMessage))
                    EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);

                if (GUILayout.Button(M("キャンセル")))
                    KillProcess();
            }
            else
            {
                using (new EditorGUI.DisabledScope(!CanExecute()))
                {
                    if (GUILayout.Button(M("姿勢推定を実行"), GUILayout.Height(30)))
                        Execute();
                }

                if (!CanExecute())
                {
                    var reasons = new List<string>();
                    if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath))
                        reasons.Add(M("動画ファイルが見つかりません"));
                    if (string.IsNullOrEmpty(_pythonPath))
                        reasons.Add(M("Python が見つかりません"));
                    if (_targetAnimator == null)
                        reasons.Add(M("Animator が未設定です"));
                    else if (!_targetAnimator.isHuman)
                        reasons.Add(M("Animator が Humanoid ではありません"));
                    if (_selectedModel == PoseModel.WHAM &&
                        (string.IsNullOrEmpty(_whamDir) || !Directory.Exists(_whamDir)))
                        reasons.Add(M("WHAM ディレクトリが見つかりません"));

                    if (reasons.Count > 0)
                        EditorGUILayout.HelpBox(
                            string.Join("\n", reasons), MessageType.Warning);
                }
            }
        }

        private void DrawResultSection()
        {
            DrawSeparator();
            EditorGUILayout.LabelField(M("結果"), EditorStyles.boldLabel);

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(_lastCreatedClipPath);
            if (clip != null)
            {
                EditorGUILayout.ObjectField(M("生成された AnimationClip"), clip,
                    typeof(AnimationClip), false);
                EditorGUILayout.LabelField(M("パス"), _lastCreatedClipPath, EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(_lastCreatedClipPath, EditorStyles.miniLabel);
            }
        }

        private bool CanExecute()
        {
            if (_isRunning) return false;
            if (string.IsNullOrEmpty(_videoPath) || !File.Exists(_videoPath)) return false;
            if (string.IsNullOrEmpty(_pythonPath)) return false;
            if (_targetAnimator == null || !_targetAnimator.isHuman) return false;
            if (_selectedModel == PoseModel.WHAM &&
                (string.IsNullOrEmpty(_whamDir) || !Directory.Exists(_whamDir)))
                return false;
            return true;
        }

        // ================================================================
        // Python 実行
        // ================================================================

        private void Execute()
        {
            _isRunning = true;
            _progress = 0f;
            _lastCreatedClipPath = null;
            _stderrBuffer = new StringBuilder();

            // JSON 出力先 (一時ファイル)
            _jsonOutputPath = Path.Combine(Path.GetTempPath(),
                $"pose_{Guid.NewGuid():N}.json");

            EditorApplication.update += OnUpdate;

            if (_selectedModel == PoseModel.WHAM)
            {
                // WHAM: conda 環境に依存管理を任せ、直接実行
                _statusMessage = M("WHAM 姿勢推定を実行中...");
                RunPoseEstimation();
            }
            else if (_selectedModel == PoseModel.ROMP)
            {
                // ROMP: simple-romp がインストール済みであることを前提に実行
                _statusMessage = M("ROMP 姿勢推定を実行中...");
                InstallPackage("simple-romp", () =>
                {
                    _statusMessage = M("ROMP 姿勢推定を実行中...");
                    RunPoseEstimation();
                });
            }
            else
            {
                // MediaPipe: pip install → pose estimation を順に実行
                _statusMessage = M("Python 依存パッケージを確認中...");
                InstallDependencies(() =>
                {
                    _statusMessage = M("MediaPipe 姿勢推定を実行中...");
                    RunPoseEstimation();
                });
            }
        }

        private void InstallDependencies(Action onComplete)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"-m pip install -r \"{RequirementsFile}\" --quiet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                var proc = Process.Start(startInfo);
                var stderrBuilder = new StringBuilder();
                proc.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        lock (stderrBuilder)
                            stderrBuilder.AppendLine(ev.Data);
                };
                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        int exitCode = proc.ExitCode;
                        proc.Dispose();

                        if (exitCode != 0)
                        {
                            string err;
                            lock (stderrBuilder)
                                err = stderrBuilder.ToString();
                            _statusMessage = string.Format(M("pip install 失敗: {0}"), err);
                            _isRunning = false;
                            EditorApplication.update -= OnUpdate;
                            Repaint();
                            return;
                        }
                        onComplete?.Invoke();
                    };
                };
            }
            catch (Exception ex)
            {
                _statusMessage = string.Format(M("Python 実行エラー: {0}"), ex.Message);
                _isRunning = false;
                EditorApplication.update -= OnUpdate;
            }
        }

        private void InstallPackage(string packageName, Action onComplete)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"-m pip install {packageName} --quiet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                var proc = Process.Start(startInfo);
                var stderrBuilder = new StringBuilder();
                proc.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        lock (stderrBuilder)
                            stderrBuilder.AppendLine(ev.Data);
                };
                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        int exitCode = proc.ExitCode;
                        proc.Dispose();

                        if (exitCode != 0)
                        {
                            string err;
                            lock (stderrBuilder)
                                err = stderrBuilder.ToString();
                            _statusMessage = string.Format(M("pip install {0} 失敗: {1}"), packageName, err);
                            _isRunning = false;
                            EditorApplication.update -= OnUpdate;
                            Repaint();
                            return;
                        }
                        onComplete?.Invoke();
                    };
                };
            }
            catch (Exception ex)
            {
                _statusMessage = string.Format(M("Python 実行エラー: {0}"), ex.Message);
                _isRunning = false;
                EditorApplication.update -= OnUpdate;
            }
        }

        private void RunPoseEstimation()
        {
            string fpsArg = _targetFps > 0 ? $"--fps {_targetFps}" : "";
            string script;
            string extraArgs;

            if (_selectedModel == PoseModel.WHAM)
            {
                script = WhamScript;
                extraArgs = $"--wham-dir \"{_whamDir}\" ";
            }
            else if (_selectedModel == PoseModel.ROMP)
            {
                script = RompScript;
                extraArgs = "";
            }
            else
            {
                script = MediaPipeScript;
                extraArgs = "";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{script}\" " +
                            extraArgs +
                            $"--input \"{_videoPath}\" " +
                            $"--output \"{_jsonOutputPath}\" {fpsArg}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                _process = Process.Start(startInfo);
                _process.EnableRaisingEvents = true;
                _process.ErrorDataReceived += OnStderrData;
                _process.BeginErrorReadLine();
                _process.Exited += OnProcessExited;
            }
            catch (Exception ex)
            {
                _statusMessage = string.Format(M("Python 実行エラー: {0}"), ex.Message);
                _isRunning = false;
                EditorApplication.update -= OnUpdate;
                _process = null;
            }
        }

        private void OnStderrData(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            if (e.Data.StartsWith("PROGRESS:"))
            {
                if (float.TryParse(e.Data.Substring("PROGRESS:".Length),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float p))
                    _progress = p;
            }
            else
            {
                lock (_stderrBuffer)
                    _stderrBuffer.AppendLine(e.Data);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            EditorApplication.delayCall += () =>
            {
                EditorApplication.update -= OnUpdate;
                EditorUtility.ClearProgressBar();

                if (_process == null) return;

                int exitCode = _process.ExitCode;
                _process.Dispose();
                _process = null;

                if (exitCode != 0)
                {
                    string err;
                    lock (_stderrBuffer)
                        err = _stderrBuffer.ToString();
                    _statusMessage = string.Format(M("姿勢推定失敗 (exit={0}): {1}"), exitCode, err);
                    _isRunning = false;
                    Debug.LogError($"[PoseEstimation] {_statusMessage}");
                    Repaint();
                    return;
                }

                // JSON を読み込んで AnimationClip を生成
                try
                {
                    GenerateAnimationClip();
                    _statusMessage = "";
                }
                catch (Exception ex)
                {
                    _statusMessage = string.Format(M("AnimationClip 生成エラー: {0}"), ex.Message);
                    Debug.LogException(ex);
                }
                finally
                {
                    _isRunning = false;
                    // 一時ファイル削除
                    if (File.Exists(_jsonOutputPath))
                        File.Delete(_jsonOutputPath);
                    Repaint();
                }
            };
        }

        private void OnUpdate()
        {
            // Repaint to update progress bar
            Repaint();
        }

        private void KillProcess()
        {
            if (_process != null && !_process.HasExited)
            {
                try { _process.Kill(); } catch { }
                _process.Dispose();
                _process = null;
            }
            _isRunning = false;
            _statusMessage = M("キャンセルしました");
            EditorApplication.update -= OnUpdate;
            EditorUtility.ClearProgressBar();

            if (!string.IsNullOrEmpty(_jsonOutputPath) && File.Exists(_jsonOutputPath))
                File.Delete(_jsonOutputPath);
        }

        // ================================================================
        // AnimationClip 生成 (Humanoid Muscle Curves)
        // ================================================================

        private void GenerateAnimationClip()
        {
            string jsonText = File.ReadAllText(_jsonOutputPath, Encoding.UTF8);
            var data = SimpleJson.Parse(jsonText) as Dictionary<string, object>;
            if (data == null)
                throw new Exception("JSON のパースに失敗しました");

            float fps = Convert.ToSingle(data["fps"]);
            var framesArray = data["frames"] as List<object>;
            if (framesArray == null || framesArray.Count == 0)
                throw new Exception("フレームデータが空です");

            // 出力フォルダ確保
            if (!AssetDatabase.IsValidFolder(_outputFolder))
                EnsureAssetFolder(_outputFolder);

            string videoName = Path.GetFileNameWithoutExtension(_videoPath);
            string clipName = $"PoseEstimation_{videoName}";
            string clipPath = $"{_outputFolder}/{clipName}.anim";

            int suffix = 1;
            while (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
            {
                clipPath = $"{_outputFolder}/{clipName}_{suffix}.anim";
                suffix++;
            }

            var animator = _targetAnimator;
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();

            // ボーンの元の状態を保存
            var savedStates = new Dictionary<HumanBodyBones, Quaternion>();
            Vector3 savedHipsPos = Vector3.zero;

            foreach (var kvp in BoneNameMap)
            {
                var t = animator.GetBoneTransform(kvp.Value);
                if (t != null)
                    savedStates[kvp.Value] = t.localRotation;
            }
            var hipsTransform = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hipsTransform != null)
                savedHipsPos = hipsTransform.localPosition;

            // キーフレーム収集用
            int muscleCount = HumanTrait.MuscleCount;
            var rootPosKeys = new[] {
                new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>()
            };
            var rootRotKeys = new[] {
                new List<Keyframe>(), new List<Keyframe>(),
                new List<Keyframe>(), new List<Keyframe>()
            };
            var muscleKeys = new List<Keyframe>[muscleCount];
            for (int i = 0; i < muscleCount; i++)
                muscleKeys[i] = new List<Keyframe>();

            try
            {
                foreach (var frameObj in framesArray)
                {
                    var frame = frameObj as Dictionary<string, object>;
                    if (frame == null) continue;

                    float time = Convert.ToSingle(frame["time"]);
                    var bones = frame["bones"] as Dictionary<string, object>;
                    if (bones == null) continue;

                    // JSON の回転をボーンに適用
                    foreach (var boneKvp in bones)
                    {
                        string boneName = boneKvp.Key;
                        var boneData = boneKvp.Value as Dictionary<string, object>;
                        if (boneData == null) continue;
                        if (!BoneNameMap.ContainsKey(boneName)) continue;

                        HumanBodyBones hbb = BoneNameMap[boneName];
                        var boneTransform = animator.GetBoneTransform(hbb);
                        if (boneTransform == null) continue;

                        if (boneData.ContainsKey("rotation"))
                        {
                            var rot = ParseFloat4(boneData["rotation"] as List<object>);
                            var deltaRot = new Quaternion(rot[0], rot[1], rot[2], rot[3]);
                            if (!deltaRot.IsValid())
                                deltaRot = Quaternion.identity;

                            // レストポーズ × デルタ回転
                            var restRot = savedStates.ContainsKey(hbb)
                                ? savedStates[hbb] : Quaternion.identity;
                            boneTransform.localRotation = restRot * deltaRot;
                        }

                        if (boneName == "Hips" && boneData.ContainsKey("position"))
                        {
                            var pos = ParseFloat3(boneData["position"] as List<object>);
                            boneTransform.localPosition = savedHipsPos
                                + new Vector3(pos[0], pos[1], pos[2]);
                        }
                    }

                    // HumanPose からマッスル値を取得
                    handler.GetHumanPose(ref humanPose);

                    rootPosKeys[0].Add(new Keyframe(time, humanPose.bodyPosition.x));
                    rootPosKeys[1].Add(new Keyframe(time, humanPose.bodyPosition.y));
                    rootPosKeys[2].Add(new Keyframe(time, humanPose.bodyPosition.z));
                    rootRotKeys[0].Add(new Keyframe(time, humanPose.bodyRotation.x));
                    rootRotKeys[1].Add(new Keyframe(time, humanPose.bodyRotation.y));
                    rootRotKeys[2].Add(new Keyframe(time, humanPose.bodyRotation.z));
                    rootRotKeys[3].Add(new Keyframe(time, humanPose.bodyRotation.w));

                    for (int i = 0; i < muscleCount && i < humanPose.muscles.Length; i++)
                        muscleKeys[i].Add(new Keyframe(time, humanPose.muscles[i]));
                }
            }
            finally
            {
                // ボーンの状態を復元
                foreach (var kvp in savedStates)
                {
                    var t = animator.GetBoneTransform(kvp.Key);
                    if (t != null)
                        t.localRotation = kvp.Value;
                }
                if (hipsTransform != null)
                    hipsTransform.localPosition = savedHipsPos;

                handler.Dispose();
            }

            // AnimationClip 作成
            var clip = new AnimationClip();
            clip.name = Path.GetFileNameWithoutExtension(clipPath);
            clip.frameRate = fps;

            // Root motion カーブ
            string[] rootPosAttrs = { "RootT.x", "RootT.y", "RootT.z" };
            string[] rootRotAttrs = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

            for (int i = 0; i < 3; i++)
                SetMuscleCurve(clip, rootPosAttrs[i], rootPosKeys[i].ToArray());
            for (int i = 0; i < 4; i++)
                SetMuscleCurve(clip, rootRotAttrs[i], rootRotKeys[i].ToArray());

            // Muscle カーブ
            for (int i = 0; i < muscleCount; i++)
            {
                if (muscleKeys[i].Count > 0)
                    SetMuscleCurve(clip, HumanTrait.MuscleName[i],
                        muscleKeys[i].ToArray());
            }

            // 保存
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _lastCreatedClipPath = clipPath;
            Debug.Log($"[PoseEstimation] AnimationClip を生成しました: {clipPath} " +
                      $"({framesArray.Count} frames, {fps} fps, " +
                      $"{muscleCount} muscles)");
        }

        private static void SetMuscleCurve(AnimationClip clip,
            string attribute, Keyframe[] keys)
        {
            var binding = new EditorCurveBinding
            {
                path = "",
                type = typeof(Animator),
                propertyName = attribute,
            };
            var curve = new AnimationCurve(keys);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            folderPath = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureAssetFolder(parent);

            string folderName = Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static float[] ParseFloat3(List<object> list)
        {
            if (list == null || list.Count < 3)
                return new float[] { 0, 0, 0 };
            return new float[]
            {
                Convert.ToSingle(list[0]),
                Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2])
            };
        }

        private static float[] ParseFloat4(List<object> list)
        {
            if (list == null || list.Count < 4)
                return new float[] { 0, 0, 0, 1 };
            return new float[]
            {
                Convert.ToSingle(list[0]),
                Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2]),
                Convert.ToSingle(list[3])
            };
        }

        // ================================================================
        // Python 検出
        // ================================================================

        private static string DetectPython()
        {
            string[] candidates = { "python", "python3", "py" };

            foreach (string cmd in candidates)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using (var proc = Process.Start(startInfo))
                    {
                        if (proc.WaitForExit(3000) && proc.ExitCode == 0)
                            return cmd;
                        if (!proc.HasExited)
                            try { proc.Kill(); } catch { }
                    }
                }
                catch
                {
                    // not found, try next
                }
            }

            return "";
        }

        // ================================================================
        // UI ユーティリティ
        // ================================================================

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        // ================================================================
        // Quaternion ユーティリティ
        // ================================================================

        // ================================================================
        // 簡易 JSON パーサー
        // ================================================================

        private static class SimpleJson
        {
            public static object Parse(string json)
            {
                int index = 0;
                return ParseValue(json, ref index);
            }

            private static object ParseValue(string json, ref int index)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) return null;

                char c = json[index];
                switch (c)
                {
                    case '{': return ParseObject(json, ref index);
                    case '[': return ParseArray(json, ref index);
                    case '"': return ParseString(json, ref index);
                    case 't':
                    case 'f': return ParseBool(json, ref index);
                    case 'n': return ParseNull(json, ref index);
                    default: return ParseNumber(json, ref index);
                }
            }

            private static Dictionary<string, object> ParseObject(string json, ref int index)
            {
                var dict = new Dictionary<string, object>();
                index++; // skip '{'
                SkipWhitespace(json, ref index);

                while (index < json.Length && json[index] != '}')
                {
                    string key = ParseString(json, ref index);
                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == ':') index++;
                    object value = ParseValue(json, ref index);
                    dict[key] = value;
                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == ',') index++;
                    SkipWhitespace(json, ref index);
                }

                if (index < json.Length) index++; // skip '}'
                return dict;
            }

            private static List<object> ParseArray(string json, ref int index)
            {
                var list = new List<object>();
                index++; // skip '['
                SkipWhitespace(json, ref index);

                while (index < json.Length && json[index] != ']')
                {
                    list.Add(ParseValue(json, ref index));
                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == ',') index++;
                    SkipWhitespace(json, ref index);
                }

                if (index < json.Length) index++; // skip ']'
                return list;
            }

            private static string ParseString(string json, ref int index)
            {
                if (json[index] != '"')
                    throw new Exception($"Expected '\"' at position {index}");

                index++; // skip opening '"'
                var sb = new StringBuilder();

                while (index < json.Length && json[index] != '"')
                {
                    if (json[index] == '\\')
                    {
                        index++;
                        if (index >= json.Length) break;
                        switch (json[index])
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (index + 4 < json.Length)
                                {
                                    string hex = json.Substring(index + 1, 4);
                                    sb.Append((char)Convert.ToInt32(hex, 16));
                                    index += 4;
                                }
                                break;
                            default: sb.Append(json[index]); break;
                        }
                    }
                    else
                    {
                        sb.Append(json[index]);
                    }
                    index++;
                }

                if (index < json.Length) index++; // skip closing '"'
                return sb.ToString();
            }

            private static double ParseNumber(string json, ref int index)
            {
                int start = index;
                if (index < json.Length && json[index] == '-') index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
                if (index < json.Length && json[index] == '.')
                {
                    index++;
                    while (index < json.Length && char.IsDigit(json[index])) index++;
                }
                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                        index++;
                    while (index < json.Length && char.IsDigit(json[index])) index++;
                }

                string numStr = json.Substring(start, index - start);
                return double.Parse(numStr,
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            private static bool ParseBool(string json, ref int index)
            {
                if (json.Substring(index, 4) == "true")
                {
                    index += 4;
                    return true;
                }
                index += 5; // "false"
                return false;
            }

            private static object ParseNull(string json, ref int index)
            {
                index += 4; // "null"
                return null;
            }

            private static void SkipWhitespace(string json, ref int index)
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                    index++;
            }
        }
    }

    // ================================================================
    // Quaternion 拡張
    // ================================================================

    internal static class QuaternionExtensions
    {
        public static bool IsValid(this Quaternion q)
        {
            float sqrMag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return sqrMag > 0.001f && !float.IsNaN(sqrMag) && !float.IsInfinity(sqrMag);
        }
    }
}
