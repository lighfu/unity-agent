using System.Collections;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class EditorUIInputIntegrationTestWindow : EditorWindow
    {
        public int Clicks;
        public int KeyDowns;
        public KeyCode LastKey;
        public bool ButtonEnabled = true;
        public string Text = "";

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(!ButtonEnabled))
            {
                if (GUI.Button(new Rect(20, 20, 100, 30), "Click probe")) Clicks++;
            }
            GUI.SetNextControlName("unity-agent-text-probe");
            Text = GUI.TextField(new Rect(20, 70, 250, 24), Text);
            if (Event.current.type == EventType.KeyDown)
            {
                KeyDowns++;
                LastKey = Event.current.keyCode;
            }
        }
    }

    public sealed class EditorUIInputIntegrationTests
    {
        private EditorUIInputIntegrationTestWindow window;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<EditorUIInputIntegrationTestWindow>();
            window.titleContent = new GUIContent("UnityAgent IMGUI Input Test");
            window.position = new Rect(100, 100, 320, 200);
            window.ShowUtility();
            // The host must attach and lay out before accepting window-relative input.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (window != null) window.Close();
            yield return null;
        }

        private static IEnumerator Completed(string queued)
        {
            var match = Regex.Match(queued, @"^Queued: actionId=([a-f0-9]+)");
            Assert.IsTrue(match.Success, queued);
            string status;
            double deadline = EditorApplication.timeSinceStartup + 5;
            do
            {
                yield return null;
                status = UIActionTools.GetUIActionResult(match.Groups[1].Value);
            } while (status.Contains("state=queued") && EditorApplication.timeSinceStartup < deadline);
            Assert.That(status, Does.Contain("state=completed"), status);
        }

        [UnityTest]
        public IEnumerator CoordinateClickInvokesRealIMGUIButtonAfterResponse()
        {
            string queued = EditorUIInputTools.ClickEditorUIAt(50, 35, windowInstanceId: UIAutomationUtility.InstanceId(window));
            Assert.AreEqual(0, window.Clicks, "Input must execute after the tool response.");
            yield return Completed(queued);
            Assert.AreEqual(1, window.Clicks, "GUI.Button must process the synthetic press and release.");
        }

        [UnityTest]
        public IEnumerator KeyboardInputReachesRealWindowOnGUIAfterResponse()
        {
            string queued = EditorUIInputTools.SendEditorUIEvent("key", windowInstanceId: UIAutomationUtility.InstanceId(window), key: "F2");
            Assert.AreEqual(0, window.KeyDowns, "Input must execute after the tool response.");
            yield return Completed(queued);
            Assert.AreEqual(1, window.KeyDowns);
            Assert.AreEqual(KeyCode.F2, window.LastKey);
        }

        [UnityTest]
        public IEnumerator CoordinateClickRespectsDisabledIMGUIButton()
        {
            window.ButtonEnabled = false;
            window.Repaint();
            yield return null;
            yield return Completed(EditorUIInputTools.ClickEditorUIAt(50, 35, windowInstanceId: UIAutomationUtility.InstanceId(window)));
            Assert.AreEqual(0, window.Clicks);
        }

        [UnityTest]
        public IEnumerator TypeJapaneseTextIntoFocusedIMGUIField()
        {
            yield return Completed(EditorUIInputTools.ClickEditorUIAt(50, 82, windowInstanceId: UIAutomationUtility.InstanceId(window)));
            string queued = EditorUIInputTools.TypeEditorUIText("日本語テスト", windowInstanceId: UIAutomationUtility.InstanceId(window));
            Assert.AreEqual("", window.Text, "Typing must execute after the tool response.");
            yield return Completed(queued);
            Assert.AreEqual("日本語テスト", window.Text);
        }
    }
}
