using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class RuntimeUIToolsTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private static int RuntimeClicks, RuntimeValueChanges, RuntimeNumber, RuntimeObjectEvents;
        private static string RuntimeText;

        private static void RecordClick() => RuntimeClicks++;
        private static void RecordToggle(bool value) { if (value) RuntimeValueChanges++; }
        private static void RecordText(string value) => RuntimeText = value;
        private static void RecordNumber(int value) => RuntimeNumber = value;
        private static void RecordObject(GameObject value) => RuntimeObjectEvents++;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private GameObject Create(string name, Transform parent = null, bool canvas = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _objects.Add(go);
            if (parent != null) go.transform.SetParent(parent, false);
            if (canvas) go.AddComponent<Canvas>();
            return go;
        }

        [Test]
        public void InspectionIncludesInactiveCanvasChildrenAndNamedUnityEvents()
        {
            GameObject canvas = Create("UI Automation Test Canvas", canvas: true);
            GameObject child = Create("Inactive Control", canvas.transform);
            child.AddComponent<RuntimeUITestEvents>();
            child.SetActive(false);

            string result = RuntimeUITools.ListRuntimeUI(UIAutomationUtility.InstanceId(canvas).ToString());
            StringAssert.Contains("id=" + UIAutomationUtility.InstanceId(child), result);
            StringAssert.Contains("/UI Automation Test Canvas/Inactive Control", result);
            StringAssert.Contains("active=False", result);
            StringAssert.Contains("RuntimeUITestEvents.onRequested(String)", result);

            string activeOnly = RuntimeUITools.ListRuntimeUI(UIAutomationUtility.InstanceId(canvas).ToString(), includeInactive: false);
            StringAssert.DoesNotContain("id=" + UIAutomationUtility.InstanceId(child), activeOnly);
        }

        [Test]
        public void InspectionReportsTruncatedMatches()
        {
            GameObject canvas = Create("Bounded Canvas", canvas: true);
            Create("First", canvas.transform);
            Create("Second", canvas.transform);

            string result = RuntimeUITools.ListRuntimeUI(UIAutomationUtility.InstanceId(canvas).ToString(), maxElements: 1);
            StringAssert.Contains("1 shown, 3 matches, 2 omitted", result);
        }

        [Test]
        public void DuplicateNamesAndDuplicatePathsRequireInstanceIds()
        {
            string uniqueName = "Duplicate UI " + Guid.NewGuid();
            GameObject canvas = Create(uniqueName, canvas: true);
            GameObject first = Create("Duplicate", canvas.transform);
            Create("Duplicate", canvas.transform);

            Assert.IsFalse(RuntimeUITools.TryResolve("/" + uniqueName + "/Duplicate", true, out _, out string error));
            StringAssert.Contains("Ambiguous selector", error);
            Assert.IsTrue(RuntimeUITools.TryResolve(UIAutomationUtility.InstanceId(first).ToString(), true, out GameObject resolved, out _));
            Assert.AreSame(first, resolved);
        }

        [Test]
        public void SelectorsCannotResolveAnObjectOutsideCanvas()
        {
            GameObject objectWithoutCanvas = Create("No Canvas");
            Assert.IsFalse(RuntimeUITools.TryResolve(UIAutomationUtility.InstanceId(objectWithoutCanvas).ToString(), true, out _, out string error));
            StringAssert.Contains("not beneath a Canvas", error);
        }

        [Test]
        public void CanvasGroupEligibilityHonorsIgnoreParentGroups()
        {
            GameObject canvas = Create("Group Canvas", canvas: true);
            CanvasGroup parentGroup = canvas.AddComponent<CanvasGroup>();
            parentGroup.interactable = false;
            GameObject child = Create("Child Group", canvas.transform);

            StringAssert.Contains("not interactable", RuntimeUITools.EligibilityError(child));
            CanvasGroup childGroup = child.AddComponent<CanvasGroup>();
            childGroup.ignoreParentGroups = true;
            Assert.IsNull(RuntimeUITools.EligibilityError(child));
            childGroup.interactable = false;
            StringAssert.Contains("not interactable", RuntimeUITools.EligibilityError(child));
        }

        [Test]
        public void InactiveOrDisabledCanvasCannotReceiveActions()
        {
            GameObject canvas = Create("Disabled Canvas", canvas: true);
            GameObject child = Create("Child", canvas.transform);
            canvas.GetComponent<Canvas>().enabled = false;
            StringAssert.Contains("Canvas is disabled", RuntimeUITools.EligibilityError(child));
            canvas.GetComponent<Canvas>().enabled = true;
            child.SetActive(false);
            StringAssert.Contains("inactive", RuntimeUITools.EligibilityError(child));
        }

        [Test]
        public void InspectionSeparatelyReportsRenderingAndRaycastFlags()
        {
            GameObject canvas = Create("Raycast Canvas", canvas: true);
            CanvasGroup group = canvas.AddComponent<CanvasGroup>();
            group.alpha = 0.25f;
            group.blocksRaycasts = false;
            GameObject child = Create("Raycast Child", canvas.transform);
            child.AddComponent<CanvasRenderer>();
            string result = RuntimeUITools.ListRuntimeUI(UIAutomationUtility.InstanceId(child).ToString());
            StringAssert.Contains("inheritedAlpha=", result);
            StringAssert.Contains("alpha=0.25", result);
            StringAssert.Contains("canvasGroupsBlockRaycasts=False", result);
            StringAssert.Contains("not a physical hit-test", result);
            Assert.IsNull(RuntimeUITools.EligibilityError(child), "Targeted dispatch remains available when physical raycasts are disabled.");
        }

        [Test]
        public void RuntimeActionsRejectEditModeWithoutInvokingListeners()
        {
            GameObject canvas = Create("Event Canvas", canvas: true);
            RuntimeUITestEvents events = canvas.AddComponent<RuntimeUITestEvents>();
            int count = 0;
            events.onRequested.AddListener(_ => count++);

            string result = RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "onRequested", "[\"test\"]");
            StringAssert.Contains("require Play Mode", result);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void EventArgumentConversionRejectsFractionalIntegerAndOverflow()
        {
            Assert.Throws<ArgumentException>(() => RuntimeUITools.ConvertEventArgument(JNode.Num(1.5), typeof(int)));
            Assert.Throws<OverflowException>(() => RuntimeUITools.ConvertEventArgument(JNode.Num(double.MaxValue), typeof(int)));
            Assert.Throws<ArgumentException>(() => RuntimeUITools.ConvertEventArgument(JNode.Num(double.MaxValue), typeof(float)));
            Assert.AreEqual(42, RuntimeUITools.ConvertEventArgument(JNode.Num(42), typeof(int)));
        }

        [Test]
        public void EventArgumentConversionRequiresMatchingTypesAndDefinedEnums()
        {
            Assert.Throws<ArgumentException>(() => RuntimeUITools.ConvertEventArgument(JNode.Str("true"), typeof(bool)));
            Assert.Throws<ArgumentException>(() => RuntimeUITools.ConvertEventArgument(JNode.Str("900"), typeof(RenderMode)));
            Assert.AreEqual(RenderMode.WorldSpace, RuntimeUITools.ConvertEventArgument(JNode.Str("WorldSpace"), typeof(RenderMode)));
            Assert.AreEqual(new Vector2(0.25f, 0.75f), RuntimeUITools.ConvertEventArgument(JNode.Arr(JNode.Num(0.25), JNode.Num(0.75)), typeof(Vector2)));
        }

        [Test]
        public void ScrollRectValueRejectsOutOfBoundsWithoutUGUIDependency()
        {
            GameObject canvas = Create("Value Canvas", canvas: true);
            RuntimeUITestEvents component = canvas.AddComponent<RuntimeUITestEvents>();
            var property = typeof(RuntimeUITestEvents).GetProperty(nameof(RuntimeUITestEvents.normalizedPosition));
            Assert.Throws<ArgumentException>(() => RuntimeUITools.ParseControlValue(component, property, "1.1,0.5"));
            Assert.AreEqual(new Vector2(0.5f, 1), RuntimeUITools.ParseControlValue(component, property, "0.5,1"));
        }

        [Test]
        public void InvalidEventsAndCoordinatesAreRejected()
        {
            StringAssert.Contains("unsupported eventName", RuntimeUITools.SendRuntimeUIEvent("unused", "arbitraryMethod"));
            StringAssert.Contains("must be finite", RuntimeUITools.SendRuntimeUIEvent("unused", "pointerDown", float.NaN));
        }

        [UnityTest]
        public IEnumerator QueuedRuntimeActionsInvokeButtonToggleAndTypedUnityEvents()
        {
            Type buttonType = OptionalType("UnityEngine.UI.Button");
            Type toggleType = OptionalType("UnityEngine.UI.Toggle");
            Type eventSystemType = OptionalType("UnityEngine.EventSystems.EventSystem");
            if (buttonType == null || toggleType == null || eventSystemType == null) Assert.Ignore("Integration test requires optional com.unity.ugui.");
            yield return new EnterPlayMode();
            buttonType = OptionalType("UnityEngine.UI.Button");
            toggleType = OptionalType("UnityEngine.UI.Toggle");
            eventSystemType = OptionalType("UnityEngine.EventSystems.EventSystem");
            RuntimeClicks = RuntimeValueChanges = RuntimeNumber = RuntimeObjectEvents = 0;
            RuntimeText = null;

            GameObject canvas = Create("Runtime Integration Canvas", canvas: true);
            GameObject system = Create("Runtime Integration EventSystem");
            Component eventSystem = system.AddComponent(eventSystemType);
            eventSystemType.GetProperty("current").SetValue(null, eventSystem);
            GameObject buttonObject = Create("Runtime Button", canvas.transform);
            Component button = buttonObject.AddComponent(buttonType);
            ((UnityEvent)buttonType.GetProperty("onClick").GetValue(button)).AddListener(RecordClick);
            string clickResult = RuntimeUITools.ClickRuntimeUI(UIAutomationUtility.InstanceId(buttonObject).ToString());
            StringAssert.StartsWith("Queued:", clickResult);
            Assert.AreEqual(0, RuntimeClicks, "Listeners must run after the tool response.");
            for (int i = 0; i < 30 && RuntimeClicks == 0; i++) yield return null;
            Assert.AreEqual(1, RuntimeClicks);

            GameObject toggleObject = Create("Runtime Toggle", canvas.transform);
            Component toggle = toggleObject.AddComponent(toggleType);
            toggleType.GetProperty("isOn").SetValue(toggle, false);
            ((UnityEvent<bool>)toggleType.GetField("onValueChanged").GetValue(toggle)).AddListener(RecordToggle);
            StringAssert.StartsWith("Queued:", RuntimeUITools.SetRuntimeUIValue(UIAutomationUtility.InstanceId(toggleObject).ToString(), "true"));
            for (int i = 0; i < 30 && RuntimeValueChanges == 0; i++) yield return null;
            Assert.AreEqual(true, toggleType.GetProperty("isOn").GetValue(toggle));
            Assert.AreEqual(1, RuntimeValueChanges);

            RuntimeUITestEvents events = canvas.AddComponent<RuntimeUITestEvents>();
            events.onRequested.AddListener(RecordText);
            events.onNumber.AddListener(RecordNumber);
            StringAssert.StartsWith("Queued:", RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "onRequested", "[\"日本語のイベント\"]"));
            StringAssert.StartsWith("Queued:", RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "onNumber", "[42]"));
            for (int i = 0; i < 30 && (RuntimeText == null || RuntimeNumber != 42); i++) yield return null;
            Assert.AreEqual("日本語のイベント", RuntimeText);
            Assert.AreEqual(42, RuntimeNumber);

            buttonType.GetProperty("interactable").SetValue(button, false);
            StringAssert.StartsWith("Error:", RuntimeUITools.ClickRuntimeUI(UIAutomationUtility.InstanceId(buttonObject).ToString()));
            StringAssert.StartsWith("Error:", RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "onNumber", "[1.5]"));
            StringAssert.StartsWith("Error:", RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "ToString", "[]"));
            Assert.AreEqual(42, RuntimeNumber);

            GameObject argument = Create("Object Event Argument", canvas.transform);
            events.onObject.AddListener(RecordObject);
            string objectResult = RuntimeUITools.InvokeRuntimeUIUnityEvent(UIAutomationUtility.InstanceId(canvas).ToString(),
                typeof(RuntimeUITestEvents).FullName, "onObject", "[{\"instanceId\":" + UIAutomationUtility.InstanceId(argument) + "}]");
            StringAssert.StartsWith("Queued:", objectResult);
            string actionId = objectResult.Substring("Queued: actionId=".Length, 32);
            LogAssert.Expect(LogType.Warning, new Regex("UI action .* failed: An object event argument was destroyed before dispatch"));
            UnityEngine.Object.DestroyImmediate(argument);
            for (int i = 0; i < 30 && !UIActionTools.GetUIActionResult(actionId).Contains("state=failed"); i++) yield return null;
            StringAssert.Contains("state=failed", UIActionTools.GetUIActionResult(actionId));
            Assert.AreEqual(0, RuntimeObjectEvents);

            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator EnsureExitPlayMode()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        private static Type OptionalType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    }

    public sealed class RuntimeUITestEvents : MonoBehaviour
    {
        public UnityEvent<string> onRequested = new UnityEvent<string>();
        public UnityEvent<int> onNumber = new UnityEvent<int>();
        public UnityEvent<GameObject> onObject = new UnityEvent<GameObject>();
        public Vector2 normalizedPosition { get; set; }
    }
}
