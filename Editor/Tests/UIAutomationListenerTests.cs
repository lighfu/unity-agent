using System.Collections;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class UIAutomationListenerTests
    {
        [TestCase("GetUIActionResult", "actionId")]
        [TestCase("GetIMGUIInspectionResult", "inspectionId")]
        public void ResultsAreAvailableOffMainThreadIncludingWrappedMCPCalls(string name, string argument)
        {
            var args = JNode.Obj((argument, JNode.Str("unknown-id")));
            var direct = ListenerThreadTools.Match(name, args);
            var wrapped = ListenerThreadTools.Match("ExecuteUnityTool", JNode.Obj(
                ("name", JNode.Str(name)), ("arguments", args)));
            Assert.IsNotNull(direct);
            Assert.IsNotNull(wrapped);
            Assert.That(Task.Run(direct).GetAwaiter().GetResult(), Does.Contain("unknown or expired"));
            Assert.That(Task.Run(wrapped).GetAwaiter().GetResult(), Does.Contain("unknown or expired"));
            Assert.IsNull(ListenerThreadTools.Match("ClickUIToolkitElement", args), "Mutations must stay on the main thread.");
        }

        [UnityTest]
        public IEnumerator RunningActionCanBePolledFromListenerDuringCallback()
        {
            var target = ScriptableObject.CreateInstance<UIToolkitAutomationTestWindow>();
            string actionId = null;
            string duringCallback = null;
            try
            {
                string queued = UIAutomationUtility.Queue(target, () =>
                {
                    var read = ListenerThreadTools.Match("GetUIActionResult", JNode.Obj(("actionId", JNode.Str(actionId))));
                    duringCallback = Task.Run(read).GetAwaiter().GetResult();
                });
                actionId = Regex.Match(queued, @"actionId=([a-f0-9]+)").Groups[1].Value;
                Assert.IsNotEmpty(actionId, queued);
                for (int i = 0; i < 30 && duringCallback == null; i++) yield return null;
                Assert.That(duringCallback, Does.Contain("state=running"));
                Assert.That(UIActionTools.GetUIActionResult(actionId), Does.Contain("state=completed"));
            }
            finally { Object.DestroyImmediate(target); }
        }
    }
}
