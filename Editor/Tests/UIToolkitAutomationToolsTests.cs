using System;
using System.Collections;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class UIToolkitAutomationTestWindow : EditorWindow
    {
        public enum Choice { First, Second }
        [Flags] public enum Options { None = 0, A = 1, B = 2, C = 4 }
        public Button Button;
        public IntegerField Number;
        public TextField Text;
        public int Clicks;
        public int Changes;

        private void OnEnable()
        {
            titleContent = new GUIContent("UnityAgent UI Toolkit Test");
            rootVisualElement.Clear();
            Button = new Button(() => Clicks++) { name = "action", text = "Apply" };
            Button.style.height = 40;
            Number = new IntegerField("Amount") { name = "number", value = 1 };
            Number.RegisterValueChangedCallback(_ => Changes++);
            Text = new TextField("Text") { name = "text", value = "before" };
            rootVisualElement.Add(Button);
            rootVisualElement.Add(Number);
            rootVisualElement.Add(Text);
            rootVisualElement.Add(new EnumField(Choice.First) { name = "choice" });
            rootVisualElement.Add(new UnityEditor.UIElements.EnumFlagsField(Options.None) { name = "options" });
            rootVisualElement.Add(new UnityEditor.UIElements.ObjectField { name = "object", objectType = typeof(Texture2D) });
            rootVisualElement.Add(new FloatField { name = "float", value = 1 });
        }
    }

    public sealed class UIToolkitAutomationToolsTests
    {
        private UIToolkitAutomationTestWindow window;

        [SetUp]
        public void SetUp()
        {
            window = ScriptableObject.CreateInstance<UIToolkitAutomationTestWindow>();
            window.position = new Rect(100, 100, 320, 240);
            window.ShowUtility();
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) window.Close();
        }

        private string Id(string name)
        {
            string report = UIToolkitAutomationTools.InspectUIToolkit(windowInstanceId: UIAutomationUtility.InstanceId(window), filter: name);
            var match = Regex.Match(report, @"elementId=(\S+).*name=""" + name + @"""");
            Assert.IsTrue(match.Success, report);
            return match.Groups[1].Value;
        }

        private static IEnumerator Completed(string result)
        {
            var match = Regex.Match(result, @"actionId=([a-f0-9]+)");
            Assert.IsTrue(match.Success, result);
            string status = "";
            double deadline = EditorApplication.timeSinceStartup + 10;
            do
            {
                yield return null;
                status = UIActionTools.GetUIActionResult(match.Groups[1].Value);
            } while (status.Contains("state=queued") && EditorApplication.timeSinceStartup < deadline);
            Assert.That(status, Does.Contain("state=completed"), status);
        }

        [UnityTest]
        public IEnumerator PointerClickInvokesRealButtonCallbackOnce()
        {
            yield return null;
            yield return null;
            yield return Completed(UIToolkitAutomationTools.ClickUIToolkitElement(Id("action")));
            Assert.AreEqual(1, window.Clicks);
        }

        [UnityTest]
        public IEnumerator ValueSetterNotifiesAndWithoutNotifyDoesNot()
        {
            yield return null;
            string id = Id("number");
            yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(id, "42"));
            Assert.AreEqual(42, window.Number.value);
            Assert.AreEqual(1, window.Changes);
            yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(id, "43", notify: false));
            Assert.AreEqual(43, window.Number.value);
            Assert.AreEqual(1, window.Changes);
        }

        [UnityTest]
        public IEnumerator ElementHandleDoesNotRetargetWhenSiblingInserted()
        {
            yield return null;
            string id = Id("number");
            window.rootVisualElement.Insert(0, new IntegerField { value = 99 });
            yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(id, "7"));
            Assert.AreEqual(7, window.Number.value);
        }

        [UnityTest]
        public IEnumerator RecycledTextOrDetachedElementRejectsOldHandle()
        {
            yield return null;
            string id = Id("action");
            window.Button.text = "Different action";
            Assert.That(UIToolkitAutomationTools.ClickUIToolkitElement(id), Does.StartWith("Error: stale"));
            id = Id("number");
            window.Number.RemoveFromHierarchy();
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(id, "9"), Does.StartWith("Error: stale"));
        }

        [UnityTest]
        public IEnumerator DisabledAndAncestorHiddenFieldsRejectChanges()
        {
            yield return null;
            string id = Id("number");
            window.Number.SetEnabled(false);
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(id, "9"), Does.Contain("disabled"));
            window.Number.SetEnabled(true);
            window.rootVisualElement.style.display = DisplayStyle.None;
            yield return null;
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(id, "9"), Does.Contain("hidden"));
            Assert.AreEqual(1, window.Number.value);
        }

        [UnityTest]
        public IEnumerator TargetChangedBetweenQueueAndDispatchFailsWithoutMutation()
        {
            yield return null;
            string result = UIToolkitAutomationTools.SetUIToolkitValue(Id("number"), "9");
            string actionId = Regex.Match(result, @"actionId=([a-f0-9]+)").Groups[1].Value;
            Assert.IsNotEmpty(actionId, result);
            window.Number.SetEnabled(false);
            LogAssert.Expect(LogType.Warning, new Regex("UI action .* failed: The element is disabled"));
            yield return null;
            yield return null;
            Assert.That(UIActionTools.GetUIActionResult(actionId), Does.Contain("state=failed"));
            Assert.AreEqual(1, window.Number.value);
        }

        [UnityTest]
        public IEnumerator PointerMoveCannotRetargetClickToRecycledButton()
        {
            yield return null;
            yield return null;
            window.Button.RegisterCallback<PointerMoveEvent>(_ => window.Button.text = "Another item");
            string result = UIToolkitAutomationTools.ClickUIToolkitElement(Id("action"));
            string actionId = Regex.Match(result, @"actionId=([a-f0-9]+)").Groups[1].Value;
            Assert.IsNotEmpty(actionId, result);
            LogAssert.Expect(LogType.Warning, new Regex("UI action .* failed: Error: stale or unknown elementId"));
            for (int i = 0; i < 30 && UIActionTools.GetUIActionResult(actionId).Contains("state=queued"); i++) yield return null;
            Assert.That(UIActionTools.GetUIActionResult(actionId), Does.Contain("state=failed"));
            Assert.AreEqual(0, window.Clicks);
        }

        [UnityTest]
        public IEnumerator InvalidValueAndEventNeverQueueAnAction()
        {
            yield return null;
            string id = Id("number");
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(id, "1.5"), Does.StartWith("Error:"));
            Assert.That(UIToolkitAutomationTools.SendUIToolkitEvent(id, "scroll", deltaY: float.NaN), Does.StartWith("Error:"));
            Assert.That(UIToolkitAutomationTools.SendUIToolkitEvent(id, "keyDown", key: "99999"), Does.StartWith("Error:"));
            Assert.That(UIToolkitAutomationTools.SendUIToolkitEvent(id, "unknown"), Does.StartWith("Error:"));
            Assert.AreEqual(1, window.Number.value);
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(Id("float"), "NaN"), Does.Contain("finite"));
        }

        [UnityTest]
        public IEnumerator EnumFieldsUseInitializedConcreteEnumType()
        {
            yield return null;
            yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(Id("choice"), "Second"));
            Assert.AreEqual(UIToolkitAutomationTestWindow.Choice.Second, window.rootVisualElement.Q<EnumField>("choice").value);
            yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(Id("options"), "A,B"));
            Assert.AreEqual(UIToolkitAutomationTestWindow.Options.A | UIToolkitAutomationTestWindow.Options.B,
                window.rootVisualElement.Q<UnityEditor.UIElements.EnumFlagsField>("options").value);
            Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(Id("options"), "99999"), Does.StartWith("Error:"));
        }

        [UnityTest]
        public IEnumerator EnumTypeChangedWhileQueuedRejectsOldValue()
        {
            yield return null;
            string result = UIToolkitAutomationTools.SetUIToolkitValue(Id("choice"), "Second");
            string actionId = Regex.Match(result, @"actionId=([a-f0-9]+)").Groups[1].Value;
            Assert.IsNotEmpty(actionId, result);
            window.rootVisualElement.Q<EnumField>("choice").Init(UIToolkitAutomationTestWindow.Options.A);
            LogAssert.Expect(LogType.Warning, new Regex("UI action .* failed: The field's enum type changed"));
            for (int i = 0; i < 30 && UIActionTools.GetUIActionResult(actionId).Contains("state=queued"); i++) yield return null;
            Assert.That(UIActionTools.GetUIActionResult(actionId), Does.Contain("state=failed"));
            Assert.AreEqual(UIToolkitAutomationTestWindow.Options.A, window.rootVisualElement.Q<EnumField>("choice").value);
        }

        [UnityTest]
        public IEnumerator ObjectFieldRejectsWrongTypesAndSetsValidReference()
        {
            var wrong = new GameObject("Wrong ObjectField type");
            var texture = new Texture2D(1, 1);
            try
            {
                yield return null;
                string id = Id("object");
                Assert.That(UIToolkitAutomationTools.SetUIToolkitValue(id, UIAutomationUtility.InstanceId(wrong).ToString()), Does.Contain("accepts Texture2D"));
                yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(id, UIAutomationUtility.InstanceId(texture).ToString()));
                Assert.AreSame(texture, window.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>("object").value);
                yield return Completed(UIToolkitAutomationTools.SetUIToolkitValue(id, "null"));
                Assert.IsNull(window.rootVisualElement.Q<UnityEditor.UIElements.ObjectField>("object").value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrong);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator AmbiguousTitleRequiresExactWindow()
        {
            var other = ScriptableObject.CreateInstance<UIToolkitAutomationTestWindow>();
            try
            {
                other.ShowUtility();
                yield return null;
                string result = UIToolkitAutomationTools.InspectUIToolkit(windowTitleContains: "UnityAgent UI Toolkit Test");
                Assert.That(result, Does.Contain("ambiguous"));
                Assert.That(UIToolkitAutomationTools.InspectUIToolkit(windowInstanceId: UIAutomationUtility.InstanceId(other)), Does.Contain("elementId="));
            }
            finally { other.Close(); }
        }
    }
}
