// Editor/Tools/FaceEmoExpressionEditor/ExpressionSessionTestWindow.cs
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor
{
    public class ExpressionSessionTestWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _log = "";

        [MenuItem("Window/AjisaiFlow/Expression Session Test")]
        public static void Open() => GetWindow<ExpressionSessionTestWindow>("ExprSession Test");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Phase 0: Spikes", EditorStyles.boldLabel);
            if (GUILayout.Button("Spike 0.2: Resolve IExpressionEditor"))
            {
                SpikeResolveIExpressionEditor();
            }

            EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear")) _log = "";
        }

        private void Log(string msg)
        {
            _log += msg + "\n";
            Debug.Log("[ExprSessionTest] " + msg);
            Repaint();
        }

        private void SpikeResolveIExpressionEditor()
        {
            Log("--- Spike 0.2 ---");
#if FACE_EMO
            var launcher = FaceEmoAPI.FindLauncher();
            if (launcher == null) { Log("FAIL: No FaceEmoLauncher in scene."); return; }

            try
            {
                var installerType = System.Type.GetType(
                    "Suzuryg.FaceEmo.AppMain.FaceEmoInstaller, jp.suzuryg.face-emo.appmain.Editor");
                if (installerType == null) { Log("FAIL: FaceEmoInstaller type not found."); return; }

                var installer = System.Activator.CreateInstance(installerType, new object[] { launcher.gameObject });
                var container = installerType.GetProperty("Container").GetValue(installer);

                var ieeType = System.Type.GetType(
                    "Suzuryg.FaceEmo.Detail.ExpressionEditor.IExpressionEditor, jp.suzuryg.face-emo.detail.Editor");
                if (ieeType == null) { Log("FAIL: IExpressionEditor type not found."); return; }

                var resolveMethod = container.GetType()
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Resolve"
                        && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                if (resolveMethod == null) { Log("FAIL: Resolve<T>() not found."); return; }

                var ee = resolveMethod.MakeGenericMethod(ieeType).Invoke(container, null);
                Log($"OK: IExpressionEditor resolved → {ee?.GetType().FullName ?? "null"}");
            }
            catch (System.Exception ex)
            {
                Log($"FAIL: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
#else
            Log("SKIP: FACE_EMO not defined.");
#endif
        }
    }
}
