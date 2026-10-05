using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class IMGUIInspectionTestWindow : EditorWindow
    {
        void OnGUI()
        {
            GUI.SetNextControlName("capture-button");
            GUI.Button(new Rect(20, 30, 120, 28), new GUIContent("IMGUI capture probe", "Probe tooltip"));
        }
    }

    public sealed class IMGUIInspectionToolsTests
    {
        const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        IMGUIInspectionTestWindow window;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<IMGUIInspectionTestWindow>();
            window.titleContent = new GUIContent("UnityAgent IMGUI Inspection Test");
            window.position = new Rect(100, 100, 320, 200);
            window.ShowUtility();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (window != null) window.Close();
            yield return null;
        }

        static string InspectionId(string queued)
        {
            var match = Regex.Match(queued, @"inspectionId=(imgui-[a-f0-9]+)");
            Assert.IsTrue(match.Success, queued);
            return match.Groups[1].Value;
        }

        static IEnumerator WaitForResult(string id, Action<string> result)
        {
            string state;
            double deadline = EditorApplication.timeSinceStartup + 6;
            do
            {
                yield return null;
                state = IMGUIInspectionTools.GetIMGUIInspectionResult(id);
            } while ((state.Contains("status=queued") || state.Contains("status=capturing")) &&
                     EditorApplication.timeSinceStartup < deadline);
            result(state);
        }

        [UnityTest]
        public IEnumerator CapturesRealDrawTextAndContentRelativeRectThenStopsDebugger()
        {
            string queued = IMGUIInspectionTools.InspectIMGUI(windowInstanceId: UIAutomationUtility.InstanceId(window));
            string id = InspectionId(queued);
            Assert.That(IMGUIInspectionTools.GetIMGUIInspectionResult(id), Does.Contain("status=queued"));
            string result = null;
            yield return WaitForResult(id, value => result = value);
            Assert.That(result, Does.Contain("status=completed"), result);
            Assert.That(result, Does.Contain("text=\"IMGUI capture probe\""), result);
            Assert.That(result, Does.Contain("tooltip=\"Probe tooltip\""), result);
            Assert.That(result, Does.Contain("rect=(20,30,120x28)"), result);
            Assert.AreEqual(result, IMGUIInspectionTools.GetIMGUIInspectionResult(id), "Polling must return the frozen snapshot.");
            Assert.IsNull(IMGUIInspectionTools.DebuggedHost, "The snapshot must not leave the native debugger running.");
        }

        [UnityTest]
        public IEnumerator ExistingNativeDebuggerSessionIsPreserved()
        {
            Type helper = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIViewDebuggerHelper");
            object host = typeof(EditorWindow).GetField("m_Parent", InstanceAny).GetValue(window);
            MethodInfo stop = helper.GetMethod("StopDebugging", StaticAny);
            helper.GetMethod("DebugWindow", StaticAny).Invoke(null, new[] { host });
            try
            {
                Assert.AreSame(host, IMGUIInspectionTools.DebuggedHost, "The test must establish an existing debugger session.");
                string id = InspectionId(IMGUIInspectionTools.InspectIMGUI(windowInstanceId: UIAutomationUtility.InstanceId(window)));
                string result = null;
                yield return WaitForResult(id, value => result = value);
                Assert.That(result, Does.Contain("status=failed"), result);
                Assert.That(result, Does.Contain("already active"), result);
                Assert.AreSame(host, IMGUIInspectionTools.DebuggedHost, "Refusing an inspection must preserve the existing debugger.");
            }
            finally
            {
                stop.Invoke(null, null);
            }
        }

        [Test]
        public void InvalidCaptureOptionsDoNotStartNativeDebugger()
        {
            Assert.That(IMGUIInspectionTools.InspectIMGUI(maxInstructions: 0), Does.StartWith("Error:"));
            Assert.That(IMGUIInspectionTools.InspectIMGUI(timeoutSeconds: float.NaN), Does.StartWith("Error:"));
            Assert.That(IMGUIInspectionTools.GetIMGUIInspectionResult("missing"), Does.StartWith("Error:"));
        }
    }
}
