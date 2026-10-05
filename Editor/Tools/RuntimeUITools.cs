using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Canvas UI automation. uGUI and TMP remain optional package dependencies.</summary>
    public static class RuntimeUITools
    {
        private const int MaxWalkNodes = 20000;
        private static readonly Dictionary<string, Type> OptionalTypes = new Dictionary<string, Type>();
        private static readonly string[] EventNames =
        {
            "pointerMove", "pointerEnter", "pointerExit", "pointerDown", "pointerUp", "pointerClick", "initializePotentialDrag",
            "beginDrag", "drag", "endDrag", "drop", "scroll", "updateSelected", "select", "deselect", "move", "submit", "cancel"
        };

        [AgentTool("Inspect loaded scenes' Canvas/uGUI UI, including inactive objects, custom event handlers and TMP. " +
                   "Returns instance IDs, exact hierarchy paths, component types, label, value, interactability, screen bounds, renderer alpha/culling, CanvasGroup raycast flags, Graphic.raycastTarget, actions and public UnityEvents. " +
                   "rootSelector optionally restricts to one subtree; selector is a GameObject instance ID, exact /root/child path, or unique exact name. " +
                   "filter matches path, component type or label. maxElements=1..1000; capped walks and omitted matches are reported. " +
                   "Screen bounds are pixels with bottom-left origin; world-space Canvas requires its assigned worldCamera. " +
                   "Use ListUIAutomationTargets and InspectUIToolkit(documentInstanceId:...) for UI Toolkit UIDocuments; legacy OnGUI has no inspectable control tree.",
            Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string ListRuntimeUI(string rootSelector = "", string filter = "", bool includeInactive = true, int maxElements = 200)
        {
            if (maxElements < 1 || maxElements > 1000) return "Error: maxElements must be between 1 and 1000.";
            GameObject root = null;
            if (!string.IsNullOrWhiteSpace(rootSelector) && !TryResolve(rootSelector, false, out root, out string error)) return "Error: " + error;
            var rows = new List<string>();
            int visited = 0, matched = 0;
            bool capped = false;
            foreach (GameObject go in InspectionObjects(root))
            {
                if (++visited > MaxWalkNodes) { capped = true; break; }
                if (FindCanvas(go) == null || (root != null && !go.transform.IsChildOf(root.transform))) continue;
                if (!includeInactive && !go.activeInHierarchy) continue;
                Component[] components = go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToArray();
                string path = PathOf(go), label = LabelOf(go);
                if (!string.IsNullOrEmpty(filter) && !Contains(path, filter) && !Contains(label, filter) &&
                    !components.Any(c => Contains(c.GetType().FullName, filter))) continue;
                matched++;
                if (rows.Count >= maxElements) continue;
                string eligibility = EligibilityError(go);
                var actions = SupportedEvents(go).ToList();
                if (actions.Contains("pointerClick")) actions.Add("click");
                if (components.Any(c => ValueProperty(c) != null)) actions.Add("setValue");
                var values = components.Select(c => ValueDescription(c)).Where(v => v != null);
                var events = components.SelectMany(c => EventDescriptions(c));
                rows.Add($"id={UIAutomationUtility.InstanceId(go)} path={Quote(path)} scene={Quote(go.scene.name)} active={go.activeInHierarchy} " +
                         $"interactable={eligibility == null} reason={Quote(eligibility ?? "")} label={Quote(label)} " +
                         $"types=[{string.Join(",", components.Select(c => c.GetType().FullName))}] " +
                         $"values=[{string.Join(",", values)}] screenRect={ScreenRect(go)} " +
                         RenderingDescription(go) + " " +
                         $"actions=[{string.Join(",", actions)}] unityEvents=[{string.Join(",", events)}]");
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Canvas UI: {rows.Count} shown, {matched} matches, {Math.Max(0, matched - rows.Count)} omitted. playMode={EditorApplication.isPlaying}.");
            sb.AppendLine("Actions dispatch to the selected object's handlers; alpha, culling and raycast flags describe rendering/input configuration, not a physical hit-test or occlusion test. Renderer values reflect the last Canvas update.");
            if (capped) sb.AppendLine($"Walk stopped after {MaxWalkNodes} loaded scene objects; match count is a lower bound. Use rootSelector to narrow the UI.");
            if (rows.Count == 0) sb.AppendLine("No matching Canvas UI. Use ListUIAutomationTargets and InspectUIToolkit for UI Toolkit UIDocuments.");
            foreach (string row in rows) sb.AppendLine(row);
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Click a Canvas UI GameObject in Play Mode using Unity EventSystem pointerDown, pointerUp and pointerClick handlers. " +
                   "selector is the instance ID from ListRuntimeUI, an exact /root/child path, or a unique exact name. " +
                   "Rejects inactive, disabled or non-interactable targets; requires an existing active EventSystem and click handler. " +
                   "Queued callbacks may open dialogs; poll GetUIActionResult(actionId) for completion. This targeted dispatch does not perform a physical screen hit-test.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string ClickRuntimeUI(string selector)
        {
            if (!TryActionTarget(selector, out GameObject go, out string error)) return "Error: " + error;
            if (!SupportedEvents(go).Contains("pointerClick")) return "Error: target has no enabled pointerClick handler. Select the Button/Toggle object rather than its text child.";
            if (!TryEventSystem(out _, out error)) return "Error: " + error;
            int playSession = UIAutomationUtility.PlaySession;
            return UIAutomationUtility.Queue(go, () =>
            {
                RequireEligible(go, playSession);
                Vector2 position = ScreenCenter(go);
                object data = CreateEventData("pointerClick", position, Vector2.zero, "Left");
                foreach (string eventName in new[] { "pointerDown", "pointerUp", "pointerClick" })
                {
                    RequireEligible(go, playSession);
                    if (SupportedEvents(go).Contains(eventName)) Dispatch(go, eventName, data);
                }
            });
        }

        [AgentTool("Set an interactable Canvas control value in Play Mode, firing its normal value-change callbacks. " +
                   "Supports Toggle (true/false), InputField/TMP_InputField (text), Slider/Scrollbar (finite number), Dropdown/TMP_Dropdown (option index), " +
                   "and ScrollRect (x,y normalized position in 0..1). componentType disambiguates controls on one GameObject. " +
                   "selector is the ID/path/unique name from ListRuntimeUI. Queues the action; poll GetUIActionResult.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string SetRuntimeUIValue(string selector, string value, string componentType = "")
        {
            if (!TryActionTarget(selector, out GameObject go, out string error)) return "Error: " + error;
            var matches = go.GetComponents<Component>().Where(c => c != null && ValueProperty(c) != null && TypeMatches(c, componentType)).ToArray();
            if (matches.Length != 1) return $"Error: expected one supported value control; found {matches.Length}. Specify its full componentType from ListRuntimeUI.";
            Component component = matches[0];
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) return "Error: value control is disabled.";
            PropertyInfo property = ValueProperty(component);
            object converted;
            try { converted = ParseControlValue(component, property, value); }
            catch (Exception ex) { return "Error: invalid control value: " + ex.Message; }
            int playSession = UIAutomationUtility.PlaySession;
            return UIAutomationUtility.Queue(go, () =>
            {
                RequireEligible(go, playSession);
                if (component == null || (component is Behaviour b && !b.isActiveAndEnabled)) throw new InvalidOperationException("The value control was destroyed or disabled before dispatch.");
                // Bounds/options can change while queued, so parse again before invoking the setter.
                converted = ParseControlValue(component, property, value);
                property.SetValue(component, converted);
            });
        }

        [AgentTool("Dispatch one standard Unity EventSystem event to a Canvas GameObject in Play Mode. " +
                   "eventName: pointerMove, pointerEnter, pointerExit, pointerDown, pointerUp, pointerClick, initializePotentialDrag, beginDrag, drag, endDrag, drop, scroll, " +
                   "updateSelected, select, deselect, move, submit, cancel. Coordinates x/y are bottom-left screen pixels; deltaX/Y are pointer delta or scroll delta, " +
                   "and for move they specify navigation direction. button is Left/Right/Middle. Requires existing EventSystem and matching enabled handler. " +
                   "This dispatches to the selected object without a physical hit-test; queue completion is available via GetUIActionResult.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string SendRuntimeUIEvent(string selector, string eventName, float x = 0, float y = 0,
            float deltaX = 0, float deltaY = 0, string button = "Left")
        {
            string canonical = EventNames.FirstOrDefault(n => string.Equals(n, eventName, StringComparison.OrdinalIgnoreCase));
            if (canonical == null) return "Error: unsupported eventName. Supported: " + string.Join(", ", EventNames);
            if (!Finite(x) || !Finite(y) || !Finite(deltaX) || !Finite(deltaY)) return "Error: coordinates and deltas must be finite.";
            if (!new[] { "Left", "Right", "Middle" }.Contains(button)) return "Error: button must be Left, Right or Middle.";
            if (!TryActionTarget(selector, out GameObject go, out string error)) return "Error: " + error;
            if (!SupportedEvents(go).Contains(canonical)) return "Error: target has no enabled " + canonical + " handler.";
            if (!TryEventSystem(out _, out error)) return "Error: " + error;
            int playSession = UIAutomationUtility.PlaySession;
            return UIAutomationUtility.Queue(go, () =>
            {
                RequireEligible(go, playSession);
                Dispatch(go, canonical, CreateEventData(canonical, new Vector2(x, y), new Vector2(deltaX, deltaY), button));
            });
        }

        [AgentTool("Invoke one named public UnityEvent field/property of a Canvas UI component in Play Mode. " +
                   "componentType must identify exactly one component (full type name recommended). eventName is listed by ListRuntimeUI. " +
                   "argumentsJson is a JSON array matching the event's Invoke signature: numbers, bool, string, enum names, vector/color arrays, " +
                   "or Unity object references as {\"instanceId\":123}. No arbitrary method invocation. Rejects disabled targets and validates all arguments before queuing. " +
                   "Poll GetUIActionResult(actionId) for completion.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string InvokeRuntimeUIUnityEvent(string selector, string componentType, string eventName, string argumentsJson = "[]")
        {
            if (string.IsNullOrWhiteSpace(componentType) || string.IsNullOrWhiteSpace(eventName)) return "Error: componentType and eventName are required.";
            if (!TryActionTarget(selector, out GameObject go, out string error)) return "Error: " + error;
            var components = go.GetComponents<Component>().Where(c => c != null && TypeMatches(c, componentType)).ToArray();
            if (components.Length != 1) return $"Error: componentType matched {components.Length} components; use a unique full type name.";
            Component component = components[0];
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) return "Error: event component is disabled.";
            UnityEventBase unityEvent;
            MethodInfo invoke;
            object[] arguments;
            try
            {
                unityEvent = ReadUnityEvent(component, eventName);
                invoke = EventInvoke(unityEvent);
                arguments = ParseEventArguments(argumentsJson, invoke);
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
            int playSession = UIAutomationUtility.PlaySession;
            return UIAutomationUtility.Queue(go, () =>
            {
                RequireEligible(go, playSession);
                if (component == null || (component is Behaviour b && !b.isActiveAndEnabled)) throw new InvalidOperationException("Event component was destroyed or disabled before dispatch.");
                UnityEventBase current = ReadUnityEvent(component, eventName);
                if (!ReferenceEquals(current, unityEvent)) throw new InvalidOperationException("UnityEvent changed before dispatch; inspect and retry.");
                foreach (object argument in arguments)
                    if (argument is UnityEngine.Object unityObject && unityObject == null)
                        throw new InvalidOperationException("An object event argument was destroyed before dispatch.");
                invoke.Invoke(current, arguments);
            });
        }

        private static IEnumerable<GameObject> SceneObjects()
        {
            return Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go != null && go.scene.IsValid() && go.scene.isLoaded &&
                !EditorUtility.IsPersistent(go));
        }

        private static IEnumerable<GameObject> InspectionObjects(GameObject root)
        {
            if (root == null)
            {
                foreach (GameObject go in SceneObjects().OrderBy(g => UIAutomationUtility.InstanceId(g))) yield return go;
                yield break;
            }
            var pending = new Stack<Transform>();
            pending.Push(root.transform);
            while (pending.Count > 0)
            {
                Transform current = pending.Pop();
                yield return current.gameObject;
                for (int i = current.childCount - 1; i >= 0; i--) pending.Push(current.GetChild(i));
            }
        }

        internal static bool TryResolve(string selector, bool requireCanvas, out GameObject target, out string error)
        {
            target = null;
            error = null;
            if (string.IsNullOrWhiteSpace(selector)) { error = "selector is required; use an instance ID from ListRuntimeUI."; return false; }
            if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                target = UIAutomationUtility.FindObject(id) as GameObject;
                if (target == null || !target.scene.IsValid() || !target.scene.isLoaded || EditorUtility.IsPersistent(target))
                { target = null; error = "No loaded scene GameObject with instance ID " + id + "."; return false; }
            }
            else
            {
                string path = selector.TrimStart('/');
                bool isPath = selector.Contains("/");
                GameObject[] matches = SceneObjects().Where(g => isPath ? PathOf(g).TrimStart('/') == path : g.name == selector).Take(3).ToArray();
                if (matches.Length != 1)
                {
                    error = matches.Length == 0 ? "No loaded scene GameObject matches selector." : "Ambiguous selector. Use an instance ID from ListRuntimeUI: " +
                        string.Join(", ", matches.Select(g => UIAutomationUtility.InstanceId(g) + "=" + PathOf(g)));
                    return false;
                }
                target = matches[0];
            }
            if (requireCanvas && FindCanvas(target) == null) { error = "Target is not beneath a Canvas. Use UI Toolkit tools for UIDocuments."; target = null; return false; }
            return true;
        }

        private static bool TryActionTarget(string selector, out GameObject go, out string error)
        {
            if (!TryResolve(selector, true, out go, out error)) return false;
            if (!EditorApplication.isPlaying) { error = "Runtime UI actions require Play Mode."; return false; }
            error = EligibilityError(go);
            return error == null;
        }

        private static void RequireEligible(GameObject go, int playSession)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended before dispatch.");
            if (UIAutomationUtility.PlaySession != playSession) throw new InvalidOperationException("Play Mode changed before dispatch; inspect and retry.");
            string error = EligibilityError(go);
            if (error != null) throw new InvalidOperationException(error);
        }

        internal static string EligibilityError(GameObject go)
        {
            if (go == null) return "Target was destroyed.";
            if (!go.activeInHierarchy) return "Target is inactive.";
            Canvas canvas = FindCanvas(go);
            if (canvas == null) return "Target is no longer beneath a Canvas.";
            if (go.GetComponentsInParent<Canvas>(true).Any(c => !c.isActiveAndEnabled)) return "A parent Canvas is disabled.";
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || !IsA(c.GetType(), "UnityEngine.UI.Selectable")) continue;
                if (c is Behaviour b && !b.isActiveAndEnabled) return "Selectable is disabled.";
                MethodInfo method = c.GetType().GetMethod("IsInteractable", Type.EmptyTypes);
                if (method != null && !(bool)method.Invoke(c, null)) return "Selectable is not interactable.";
            }
            // Match CanvasGroup's ignoreParentGroups semantics at each transform boundary.
            for (Transform t = go.transform; t != null; t = t.parent)
            {
                bool stop = false;
                foreach (CanvasGroup group in t.GetComponents<CanvasGroup>())
                {
                    if (!group.isActiveAndEnabled) continue;
                    if (!group.interactable) return "CanvasGroup is not interactable.";
                    stop |= group.ignoreParentGroups;
                }
                if (stop) break;
            }
            return null;
        }

        private static Canvas FindCanvas(GameObject go) => go == null ? null : go.GetComponentInParent<Canvas>(true);
        private static bool IsA(Type type, string fullName)
        {
            for (; type != null; type = type.BaseType) if (type.FullName == fullName) return true;
            return false;
        }
        private static bool TypeMatches(Component c, string type) => string.IsNullOrEmpty(type) || c.GetType().Name == type || c.GetType().FullName == type;
        private static Type OptionalType(string fullName)
        {
            if (!OptionalTypes.TryGetValue(fullName, out Type type))
            {
                type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
                OptionalTypes[fullName] = type;
            }
            return type;
        }
        private static string PathOf(GameObject go)
        {
            var names = new List<string>();
            for (Transform t = go.transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return "/" + string.Join("/", names);
        }
        private static bool Contains(string text, string filter) => text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        private static string Quote(string value) => JNode.Str(value == null ? "" : value.Length > 300 ? value.Substring(0, 300) + "…" : value).ToJson();
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static string LabelOf(GameObject go)
        {
            var labels = new List<string>();
            var pending = new Queue<Transform>();
            pending.Enqueue(go.transform);
            int visited = 0;
            while (pending.Count > 0 && visited++ < 64 && labels.Count < 4)
            {
                Transform current = pending.Dequeue();
                foreach (Component c in current.GetComponents<Component>())
                {
                    if (c == null || (!IsA(c.GetType(), "UnityEngine.UI.Text") && !IsA(c.GetType(), "TMPro.TMP_Text"))) continue;
                    try
                    {
                        string text = c.GetType().GetProperty("text")?.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(text)) labels.Add(text);
                    }
                    catch { }
                    if (labels.Count >= 4) break;
                }
                for (int i = 0; i < current.childCount && pending.Count < 64; i++) pending.Enqueue(current.GetChild(i));
            }
            return string.Join(" | ", labels);
        }

        private static PropertyInfo ValueProperty(Component component)
        {
            Type type = component.GetType();
            string property = IsA(type, "UnityEngine.UI.Toggle") ? "isOn" :
                IsA(type, "UnityEngine.UI.InputField") || IsA(type, "TMPro.TMP_InputField") ? "text" :
                IsA(type, "UnityEngine.UI.Slider") || IsA(type, "UnityEngine.UI.Scrollbar") ||
                IsA(type, "UnityEngine.UI.Dropdown") || IsA(type, "TMPro.TMP_Dropdown") ? "value" :
                IsA(type, "UnityEngine.UI.ScrollRect") ? "normalizedPosition" : null;
            return property == null ? null : type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        }

        private static string ValueDescription(Component c)
        {
            PropertyInfo property = ValueProperty(c);
            if (property == null) return null;
            try { return c.GetType().Name + "." + property.Name + "=" + Quote(Convert.ToString(property.GetValue(c), CultureInfo.InvariantCulture)); }
            catch { return c.GetType().Name + "." + property.Name + "=unavailable"; }
        }

        internal static object ParseControlValue(Component c, PropertyInfo property, string value)
        {
            Type type = property.PropertyType;
            if (type == typeof(string)) return value ?? "";
            if (type == typeof(bool))
            {
                if (!bool.TryParse(value, out bool boolean)) throw new ArgumentException("expected true or false.");
                return boolean;
            }
            if (type == typeof(int))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) throw new ArgumentException("expected an integer option index.");
                var options = c.GetType().GetProperty("options")?.GetValue(c) as System.Collections.ICollection;
                if (options == null || index < 0 || index >= options.Count) throw new ArgumentException("option index is outside the available options.");
                return index;
            }
            if (type == typeof(float))
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || !Finite(number)) throw new ArgumentException("expected a finite number.");
                float min = IsA(c.GetType(), "UnityEngine.UI.Scrollbar") ? 0 : Convert.ToSingle(c.GetType().GetProperty("minValue")?.GetValue(c), CultureInfo.InvariantCulture);
                float max = IsA(c.GetType(), "UnityEngine.UI.Scrollbar") ? 1 : Convert.ToSingle(c.GetType().GetProperty("maxValue")?.GetValue(c), CultureInfo.InvariantCulture);
                if (number < min || number > max) throw new ArgumentException($"expected a value between {min} and {max}.");
                if (c.GetType().GetProperty("wholeNumbers")?.GetValue(c) is bool whole && whole && number != Mathf.Round(number)) throw new ArgumentException("control requires a whole number.");
                return number;
            }
            if (type == typeof(Vector2))
            {
                string[] parts = (value ?? "").Split(',');
                if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !Finite(x) || !Finite(y) || x < 0 || x > 1 || y < 0 || y > 1)
                    throw new ArgumentException("expected x,y normalized coordinates in 0..1.");
                return new Vector2(x, y);
            }
            throw new ArgumentException("unsupported control value type " + type.Name);
        }

        private static IEnumerable<string> SupportedEvents(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();
            foreach (string name in EventNames)
            {
                Type handler = OptionalType("UnityEngine.EventSystems.I" + char.ToUpperInvariant(name[0]) + name.Substring(1) + "Handler");
                if (handler != null && components.Any(c => c != null && handler.IsInstanceOfType(c) && (!(c is Behaviour b) || b.isActiveAndEnabled))) yield return name;
            }
        }

        private static bool TryEventSystem(out object system, out string error)
        {
            Type type = OptionalType("UnityEngine.EventSystems.EventSystem");
            system = type?.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            error = system == null || (system is Behaviour b && !b.isActiveAndEnabled) ? "No active EventSystem. Add/enable the scene's EventSystem before sending UI events." : null;
            return error == null;
        }

        private static object CreateEventData(string eventName, Vector2 position, Vector2 delta, string button)
        {
            if (!TryEventSystem(out object system, out string error)) throw new InvalidOperationException(error);
            bool pointer = eventName.StartsWith("pointer", StringComparison.Ordinal) || new[] { "initializePotentialDrag", "beginDrag", "drag", "endDrag", "drop", "scroll" }.Contains(eventName);
            string dataName = pointer ? "PointerEventData" : eventName == "move" ? "AxisEventData" : "BaseEventData";
            Type type = OptionalType("UnityEngine.EventSystems." + dataName);
            if (type == null) throw new InvalidOperationException(dataName + " type is unavailable.");
            object data = Activator.CreateInstance(type, system);
            if (pointer)
            {
                SetData(data, "position", position);
                SetData(data, "pressPosition", position);
                SetData(data, "delta", delta);
                SetData(data, "scrollDelta", delta);
                SetData(data, "pointerId", -1);
                SetData(data, "clickCount", 1);
                SetData(data, "eligibleForClick", true);
                PropertyInfo buttonProperty = type.GetProperty("button");
                buttonProperty?.SetValue(data, Enum.Parse(buttonProperty.PropertyType, button));
            }
            else if (eventName == "move")
            {
                SetData(data, "moveVector", delta);
                PropertyInfo direction = type.GetProperty("moveDir");
                string name = delta == Vector2.zero ? "None" : Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x > 0 ? "Right" : "Left" : delta.y > 0 ? "Up" : "Down";
                direction?.SetValue(data, Enum.Parse(direction.PropertyType, name));
            }
            return data;
        }

        private static void SetData(object data, string name, object value) => data.GetType().GetProperty(name)?.SetValue(data, value);
        private static void Dispatch(GameObject go, string eventName, object data)
        {
            Type execute = OptionalType("UnityEngine.EventSystems.ExecuteEvents");
            string handlerName = eventName == "initializePotentialDrag" ? eventName : eventName + "Handler";
            object handler = execute?.GetProperty(handlerName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (handler == null) throw new InvalidOperationException("No ExecuteEvents handler for " + eventName + ".");
            Type interfaceType = handler.GetType().GetGenericArguments()[0];
            MethodInfo method = execute.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "Execute" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
            SetData(data, "pointerPress", go);
            SetData(data, "rawPointerPress", go);
            SetData(data, "pointerEnter", go);
            SetData(data, "pointerDrag", go);
            SetPointerRaycasts(go, data);
            if (!(bool)method.MakeGenericMethod(interfaceType).Invoke(null, new[] { (object)go, data, handler }))
                throw new InvalidOperationException("The " + eventName + " handler is no longer available.");
        }

        private static void SetPointerRaycasts(GameObject go, object data)
        {
            if (!IsA(data.GetType(), "UnityEngine.EventSystems.PointerEventData")) return;
            Type raycastType = OptionalType("UnityEngine.EventSystems.RaycastResult");
            Type raycasterType = OptionalType("UnityEngine.UI.GraphicRaycaster");
            Canvas canvas = FindCanvas(go);
            if (raycastType == null || raycasterType == null || canvas == null) return;
            Component raycaster = canvas.GetComponentInParent(raycasterType);
            if (raycaster == null) return;
            // Supply the target Canvas's event camera. We intentionally dispatch to the selected
            // object; this synthetic result is not a claim that a screen hit-test found it.
            object result = Activator.CreateInstance(raycastType);
            raycastType.GetField("gameObject")?.SetValue(result, go);
            raycastType.GetField("module")?.SetValue(result, raycaster);
            raycastType.GetField("screenPosition")?.SetValue(result, data.GetType().GetProperty("position")?.GetValue(data));
            raycastType.GetField("worldPosition")?.SetValue(result, go.transform.position);
            SetData(data, "pointerPressRaycast", result);
            SetData(data, "pointerCurrentRaycast", result);
        }

        private static string ScreenRect(GameObject go)
        {
            if (!(go.transform is RectTransform rect)) return "unavailable";
            Canvas canvas = FindCanvas(go)?.rootCanvas;
            if (canvas == null || (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null)) return "unavailable(no Canvas camera)";
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2[] points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            if (points.Any(p => !Finite(p.x) || !Finite(p.y))) return "unavailable";
            return string.Format(CultureInfo.InvariantCulture, "({0:F1},{1:F1},{2:F1},{3:F1})", points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x) - points.Min(p => p.x), points.Max(p => p.y) - points.Min(p => p.y));
        }

        private static string RenderingDescription(GameObject go)
        {
            CanvasRenderer renderer = go.GetComponent<CanvasRenderer>();
            string alpha = renderer == null ? "unavailable(no CanvasRenderer)" : renderer.GetInheritedAlpha().ToString("G4", CultureInfo.InvariantCulture);
            string rendererAlpha = renderer == null ? "unavailable" : renderer.GetAlpha().ToString("G4", CultureInfo.InvariantCulture);
            var groups = new List<string>();
            bool blocksRaycasts = true, ignoreParents = false;
            for (Transform t = go.transform; t != null; t = t.parent)
            {
                foreach (CanvasGroup group in t.GetComponents<CanvasGroup>())
                {
                    groups.Add(string.Format(CultureInfo.InvariantCulture, "id={0}:alpha={1:G4}:enabled={2}:blocksRaycasts={3}:ignoreParentGroups={4}",
                        UIAutomationUtility.InstanceId(group), group.alpha, group.isActiveAndEnabled, group.blocksRaycasts, group.ignoreParentGroups));
                    if (!group.isActiveAndEnabled || ignoreParents) continue;
                    blocksRaycasts &= group.blocksRaycasts;
                    ignoreParents |= group.ignoreParentGroups;
                }
                Canvas canvas = t.GetComponent<Canvas>();
                if (canvas != null && canvas.overrideSorting) break;
            }
            var graphics = go.GetComponents<Component>().Where(c => c != null && IsA(c.GetType(), "UnityEngine.UI.Graphic"))
                .Select(c => c.GetType().Name + ":raycastTarget=" + c.GetType().GetProperty("raycastTarget")?.GetValue(c) +
                             ":enabled=" + (!(c is Behaviour b) || b.isActiveAndEnabled));
            return $"inheritedAlpha={alpha} rendererAlpha={rendererAlpha} rendererCulled={(renderer == null ? "unavailable" : renderer.cull.ToString())} " +
                   $"canvasGroupsBlockRaycasts={blocksRaycasts} canvasGroups=[{string.Join(",", groups)}] graphics=[{string.Join(",", graphics)}]";
        }

        private static Vector2 ScreenCenter(GameObject go)
        {
            Canvas canvas = FindCanvas(go)?.rootCanvas;
            if (!(go.transform is RectTransform rect) || canvas == null) return Vector2.zero;
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        }

        private static IEnumerable<MemberInfo> EventMembers(Component c) => c.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m is FieldInfo f && typeof(UnityEventBase).IsAssignableFrom(f.FieldType) || m is PropertyInfo p && p.CanRead && p.GetIndexParameters().Length == 0 && typeof(UnityEventBase).IsAssignableFrom(p.PropertyType));
        private static UnityEventBase ReadUnityEvent(Component c, string name)
        {
            MemberInfo[] members = EventMembers(c).Where(m => m.Name == name).ToArray();
            if (members.Length != 1) throw new ArgumentException("Expected one public UnityEvent field/property named '" + name + "'.");
            object value = members[0] is FieldInfo field ? field.GetValue(c) : ((PropertyInfo)members[0]).GetValue(c);
            return value as UnityEventBase ?? throw new ArgumentException("UnityEvent is null.");
        }
        private static MethodInfo EventInvoke(UnityEventBase unityEvent)
        {
            MethodInfo[] methods = unityEvent.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "Invoke" && !m.IsGenericMethod).ToArray();
            if (methods.Length != 1) throw new ArgumentException("UnityEvent must have one unambiguous public Invoke signature.");
            return methods[0];
        }
        private static IEnumerable<string> EventDescriptions(Component c)
        {
            foreach (MemberInfo member in EventMembers(c).Take(40))
            {
                string description;
                try
                {
                    UnityEventBase unityEvent = ReadUnityEvent(c, member.Name);
                    description = c.GetType().Name + "." + member.Name + "(" + string.Join(",", EventInvoke(unityEvent).GetParameters().Select(p => p.ParameterType.Name)) + ") persistentListeners=" + unityEvent.GetPersistentEventCount();
                }
                catch { description = c.GetType().Name + "." + member.Name + "(unavailable)"; }
                yield return description;
            }
        }
        private static object[] ParseEventArguments(string json, MethodInfo invoke)
        {
            if (json == null || json.Length > 65536) throw new ArgumentException("argumentsJson must be a JSON array of at most 65536 characters.");
            JNode node = JNode.Parse(json);
            ParameterInfo[] parameters = invoke.GetParameters();
            if (node.Type != JNode.JType.Array || node.Count != parameters.Length) throw new ArgumentException($"argumentsJson must be a JSON array with {parameters.Length} argument(s).");
            return parameters.Select((p, i) => ConvertEventArgument(node[i], p.ParameterType)).ToArray();
        }
        internal static object ConvertEventArgument(JNode node, Type type)
        {
            if (type == typeof(string) && node.Type == JNode.JType.String) return node.AsString;
            if (type == typeof(bool) && node.Type == JNode.JType.Bool) return node.AsBool;
            if (type.IsEnum && node.Type == JNode.JType.String)
            {
                object value = Enum.Parse(type, node.AsString, true);
                if (!Enum.IsDefined(type, value)) throw new ArgumentException("Undefined enum value.");
                return value;
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (node.IsNull) return null;
                JNode id = node["instanceId"];
                if (node.Type != JNode.JType.Object || id.Type != JNode.JType.Number || id.AsNumber != Math.Truncate(id.AsNumber) || id.AsNumber < int.MinValue || id.AsNumber > int.MaxValue)
                    throw new ArgumentException("Object arguments require {\"instanceId\":123} or null.");
                UnityEngine.Object value = UIAutomationUtility.FindObject(id.AsInt);
                if (value == null || !type.IsInstanceOfType(value)) throw new ArgumentException("Object instance does not match " + type.Name + ".");
                return value;
            }
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Color))
            {
                int count = type == typeof(Vector2) ? 2 : type == typeof(Vector3) ? 3 : 4;
                if (node.Type != JNode.JType.Array || node.Count != count) throw new ArgumentException(type.Name + " requires a " + count + "-number array.");
                float[] values = node.AsArray.Select(n => (float)ConvertEventArgument(n, typeof(float))).ToArray();
                if (type == typeof(Vector2)) return new Vector2(values[0], values[1]);
                if (type == typeof(Vector3)) return new Vector3(values[0], values[1], values[2]);
                if (type == typeof(Color)) return new Color(values[0], values[1], values[2], values[3]);
                return new Vector4(values[0], values[1], values[2], values[3]);
            }
            if (node.Type == JNode.JType.Number && (type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long)))
            {
                double number = node.AsNumber;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("Numeric event argument must be finite.");
                if ((type == typeof(int) || type == typeof(long)) && number != Math.Truncate(number)) throw new ArgumentException("Integer event argument cannot contain a fraction.");
                object value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                if (value is float f && !Finite(f)) throw new ArgumentException("Float event argument is out of range.");
                return value;
            }
            throw new ArgumentException("Argument is incompatible with " + type.Name + ".");
        }
    }
}
