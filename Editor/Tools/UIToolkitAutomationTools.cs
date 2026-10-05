using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Retained UI inspection and actions for EditorWindows and runtime UIDocuments.</summary>
    public static class UIToolkitAutomationTools
    {
        private const int MaxNodes = 20000;
        private const int MaxSnapshots = 16;
        private sealed class Handle
        {
            public WeakReference Element;
            public string Name;
            public string Text;
            public string Binding;
        }
        private sealed class Snapshot
        {
            public UnityEngine.Object Owner;
            public WeakReference Root;
            public DateTime Created;
            public bool Playing;
            public int PlaySession;
            public readonly Dictionary<string, Handle> Handles = new Dictionary<string, Handle>();
        }
        private static readonly Dictionary<string, Snapshot> Snapshots = new Dictionary<string, Snapshot>();
        private static readonly Queue<string> SnapshotOrder = new Queue<string>();

        [AgentTool("List loaded EditorWindows and scene UIDocuments with exact instance IDs, titles, UI technology " +
                   "and bounds. InspectUIToolkit uses these IDs. IMGUI controls require InspectIMGUI/CaptureEditorWindow " +
                   "and SendEditorUIEvent. Canvas/uGUI uses ListRuntimeUI. Menus use SearchMenu/ExecuteMenu; " +
                   "blocking native dialogs use GetEditorState/AnswerModalDialog. Read-only.",
            Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string ListUIAutomationTargets()
        {
            var sb = new StringBuilder("Unity UI automation targets (loaded objects; closed windows are not listed):\n");
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w != null).OrderBy(w => UIAutomationUtility.InstanceId(w)))
            {
                bool hasOnGUI = window.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                sb.AppendLine($"windowInstanceId={UIAutomationUtility.InstanceId(window)} title={Quote(UIAutomationUtility.Title(window))} " +
                    $"type={window.GetType().FullName} toolkitChildren={window.rootVisualElement.hierarchy.childCount} " +
                    $"onGUI={hasOnGUI} rect={RectText(window.position)}");
            }
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>()
                .Where(d => d != null && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded).OrderBy(d => UIAutomationUtility.InstanceId(d)))
                sb.AppendLine($"documentInstanceId={UIAutomationUtility.InstanceId(document)} name={Quote(document.name)} " +
                    $"scene={Quote(document.gameObject.scene.name)} active={document.isActiveAndEnabled} " +
                    $"attached={document.rootVisualElement?.panel != null}");
            sb.AppendLine("Editor/input rects use logical points. InspectUIToolkit documents use panel coordinates. " +
                "IMGUI draw instructions are partial metadata; custom GPU drawing/native UI may only be visible in captures.");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect a UI Toolkit EditorWindow or runtime UIDocument. Specify windowInstanceId (preferred), " +
                   "windowTitleContains, or documentInstanceId from ListUIAutomationTargets. Title ambiguity is an error " +
                   "unless matchIndex is supplied (ID-sorted title matches, not ListEditorWindows indexes). " +
                   "Reports elementId, hierarchy path, type, name, text/label, tooltip, bindingPath, value, classes, " +
                   "enabled/visible/attached/focusable and bounds. filter matches type/name/text/tooltip/bindingPath; " +
                   "includeHidden=true includes disabled/hidden elements. maxElements=1..2000. Walk capped at 20000 nodes/200 depth. " +
                   "Use returned elementId for ClickUIToolkitElement/SetUIToolkitValue/SendUIToolkitEvent/ScrollUIToolkitElement. " +
                   "IDs hold exact element references; expire after 15 minutes, 16 inspections, domain reload or tree replacement. " +
                   "Reinspect virtualized controls after scrolling. IMGUIContainer contents are not retained elements; " +
                   "use InspectIMGUI and coordinate input. Read-only.", Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string InspectUIToolkit(string windowTitleContains = "", int windowInstanceId = 0,
            int documentInstanceId = 0, int matchIndex = -1, string filter = "", int maxElements = 200, bool includeHidden = true)
        {
            if (maxElements < 1 || maxElements > 2000) return "Error: maxElements must be between 1 and 2000.";
            if (documentInstanceId != 0 && (windowInstanceId != 0 || !string.IsNullOrEmpty(windowTitleContains)))
                return "Error: choose an EditorWindow or a UIDocument, not both.";
            UnityEngine.Object owner;
            VisualElement root;
            if (documentInstanceId != 0)
            {
                var doc = Resources.FindObjectsOfTypeAll<UIDocument>().FirstOrDefault(d => d != null &&
                    UIAutomationUtility.InstanceId(d) == documentInstanceId && d.gameObject.scene.IsValid() && d.gameObject.scene.isLoaded);
                if (doc == null) return "Error: scene UIDocument not found. Call ListUIAutomationTargets again.";
                owner = doc;
                root = doc.rootVisualElement;
            }
            else
            {
                var window = UIAutomationUtility.ResolveWindow(windowTitleContains, windowInstanceId, matchIndex, out string error);
                if (window == null) return error;
                owner = window;
                root = window.rootVisualElement;
            }
            if (root == null) return "Error: no UI Toolkit root exists yet. Show/enable the target and inspect again.";
            string snapshotId = Guid.NewGuid().ToString("N");
            var snapshot = new Snapshot { Owner = owner, Root = new WeakReference(root), Created = DateTime.UtcNow,
                Playing = EditorApplication.isPlaying, PlaySession = UIAutomationUtility.PlaySession };
            var sb = new StringBuilder($"UI Toolkit ownerInstanceId={UIAutomationUtility.InstanceId(owner)} name={Quote(owner.name)} snapshotId={snapshotId}\n");
            sb.AppendLine(owner is EditorWindow ? "rect: root-relative Unity logical points, top-left origin (last layout)." :
                "rect: UIDocument panel coordinates, top-left origin (last layout; not Game View screen pixels).");
            int walked = 0, matched = 0;
            bool depthCap = false;
            var stack = new Stack<(VisualElement element, string path, int depth)>();
            stack.Push((root, "root", 0));
            Vector2 origin = owner is EditorWindow ? root.worldBound.position : Vector2.zero;
            while (stack.Count > 0 && walked < MaxNodes)
            {
                var entry = stack.Pop();
                var element = entry.element;
                walked++;
                string text = ReadText(element);
                string binding = element is IBindable bindable ? bindable.bindingPath : "";
                bool shown = IsShown(element);
                if ((includeHidden || shown) && (string.IsNullOrEmpty(filter) ||
                    Contains(element.GetType().Name, filter) || Contains(element.name, filter) || Contains(text, filter) ||
                    Contains(element.tooltip, filter) || Contains(binding, filter)))
                {
                    matched++;
                    if (snapshot.Handles.Count < maxElements)
                    {
                        string elementId = snapshotId + ":" + walked.ToString(CultureInfo.InvariantCulture);
                        snapshot.Handles.Add(elementId, new Handle { Element = new WeakReference(element), Name = element.name, Text = text, Binding = binding });
                        Rect rect = element.worldBound;
                        rect.position -= origin;
                        var valueProperty = ValueProperty(element);
                        string value = "";
                        if (valueProperty != null)
                        {
                            try { value = " valueType=" + valueProperty.PropertyType.Name + " value=" + Quote(Convert.ToString(valueProperty.GetValue(element), CultureInfo.InvariantCulture)); }
                            catch { value = " value=unavailable"; }
                        }
                        sb.AppendLine($"elementId={elementId} path={entry.path} type={element.GetType().Name} name={Quote(element.name)} " +
                            $"text={Quote(text)} tooltip={Quote(element.tooltip)} bindingPath={Quote(binding)}{value} " +
                            $"enabled={element.enabledInHierarchy} visible={shown} attached={element.panel != null} " +
                            $"focusable={element.focusable} picking={element.pickingMode} classes={Quote(string.Join(" ", element.GetClasses()))} rect={RectText(rect)}" +
                            (element is IMGUIContainer ? " IMGUI: controls inside this container need InspectIMGUI/coordinate input." : ""));
                    }
                }
                if (entry.depth >= 200) { if (element.hierarchy.childCount > 0) depthCap = true; continue; }
                for (int i = element.hierarchy.childCount - 1; i >= 0; i--)
                    stack.Push((element.hierarchy[i], entry.path + "/" + i, entry.depth + 1));
            }
            if (Snapshots.Count >= MaxSnapshots) Snapshots.Remove(SnapshotOrder.Dequeue());
            Snapshots.Add(snapshotId, snapshot);
            SnapshotOrder.Enqueue(snapshotId);
            sb.AppendLine($"walked={walked} matched={matched} returned={snapshot.Handles.Count} " +
                $"truncated={matched > snapshot.Handles.Count || stack.Count > 0 || depthCap}");
            if (stack.Count > 0 || depthCap) sb.AppendLine("Walk/depth cap reached; totals are lower bounds. Narrow the inspected area/filter.");
            if (walked == 1) sb.AppendLine("Only a root was found. An OnGUI window can still draw a complete IMGUI interface: use InspectIMGUI/CaptureEditorWindow.");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Click a UI Toolkit element using its elementId from InspectUIToolkit. Sends pointer-down/up at " +
                   "the actual element center through Unity event dispatch (including Button.clicked), not a bare ClickEvent. " +
                   "Rejects stale, hidden, disabled, detached, clipped/covered targets. ScrollUIToolkitElement first if needed. " +
                   "Queued; poll GetUIActionResult and inspect afterward. UIDocument actions require Play mode.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string ClickUIToolkitElement(string elementId) => QueueElement(elementId, true, element =>
        {
            Vector2 center = ClickPosition(element);
            SendPointer(element, EventType.MouseMove, center);
            RequireCurrentHandle(elementId, element);
            try
            {
                SendPointer(element, EventType.MouseDown, center);
                RequireCurrentHandle(elementId, element);
                SendPointer(element, EventType.MouseUp, center);
            }
            finally
            {
                // Cancel capture without clicking a control recycled/detached by a down callback.
                if (element.panel != null) element.ReleasePointer(PointerId.mousePointerId);
            }
        });

        [AgentTool("Set a UI Toolkit field's public INotifyValueChanged<T> value using an elementId from InspectUIToolkit. " +
                   "value is raw text for strings, true/false for booleans, invariant number for numeric fields, enum name, " +
                   "comma-separated components for Vector2/3/4, Vector2Int/3Int or Color (also #RRGGBB/#RRGGBBAA), " +
                   "or instance ID/null for ObjectField. notify=true uses the normal value setter/ChangeEvent; " +
                   "notify=false uses SetValueWithoutNotify. Unsupported value types return an error without mutation. " +
                   "Queued; poll GetUIActionResult. UIDocument actions require Play mode.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string SetUIToolkitValue(string elementId, string value, bool notify = true)
        {
            if (!Resolve(elementId, out var snapshot, out var element, out string error)) return error;
            var property = ValueProperty(element);
            if (property == null || !property.CanWrite) return "Error: this element is not a writable INotifyValueChanged<T> field.";
            object converted;
            try
            {
                Type valueType = property.PropertyType;
                if (valueType == typeof(Enum))
                    valueType = (property.GetValue(element) as Enum)?.GetType() ?? throw new ArgumentException("The EnumField has no initialized enum type.");
                converted = ConvertValue(value, valueType);
                ValidateObjectValue(element, converted);
            }
            catch (Exception ex) { return "Error: " + ex.GetBaseException().Message; }
            return QueueElement(elementId, false, current =>
            {
                if (property.PropertyType == typeof(Enum) && (property.GetValue(current) as Enum)?.GetType() != converted.GetType())
                    throw new InvalidOperationException("The field's enum type changed before dispatch. Reinspect before setting it.");
                ValidateObjectValue(current, converted);
                if (notify) property.SetValue(current, converted);
                else
                {
                    var valueInterface = typeof(INotifyValueChanged<>).MakeGenericType(property.PropertyType);
                    valueInterface.GetMethod("SetValueWithoutNotify").Invoke(current, new[] { converted });
                }
            });
        }

        [AgentTool("Send a UI Toolkit event to an inspected elementId. eventName: pointerDown, pointerUp, pointerMove, " +
                   "keyDown, keyUp, submit, cancel, scroll, focus, blur. Pointer events use its measured center; " +
                   "key is a Unity KeyCode name and character is empty or one character; modifiers is comma-separated " +
                   "Unity EventModifiers names (e.g. Control,Shift). scroll uses deltaX/deltaY. " +
                   "Use ClickUIToolkitElement for a complete click. Queued; poll GetUIActionResult. " +
                   "These are synthetic Unity events; a dispatched event may be ignored by application code.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string SendUIToolkitEvent(string elementId, string eventName, string key = "", string character = "",
            string modifiers = "None", float deltaX = 0, float deltaY = 0)
        {
            string kind = (eventName ?? "").ToLowerInvariant();
            if (!new[] { "pointerdown", "pointerup", "pointermove", "keydown", "keyup", "submit", "cancel", "scroll", "focus", "blur" }.Contains(kind))
                return "Error: unsupported eventName. Use pointerDown/pointerUp/pointerMove/keyDown/keyUp/submit/cancel/scroll/focus/blur.";
            if (!UIAutomationUtility.IsFinite(deltaX) || !UIAutomationUtility.IsFinite(deltaY)) return "Error: scroll deltas must be finite.";
            if ((character ?? "").Length > 1) return "Error: character must be empty or one character.";
            if (!Enum.TryParse(modifiers, true, out EventModifiers mods) || (mods & ~(EventModifiers.Shift | EventModifiers.Control |
                EventModifiers.Alt | EventModifiers.Command | EventModifiers.CapsLock | EventModifiers.Numeric | EventModifiers.FunctionKey)) != 0)
                return "Error: invalid EventModifiers.";
            KeyCode code = KeyCode.None;
            if (kind == "keydown" || kind == "keyup")
            {
                if (!string.IsNullOrEmpty(key) && (!Enum.TryParse(key, true, out code) || !Enum.IsDefined(typeof(KeyCode), code)))
                    return "Error: invalid Unity KeyCode.";
                if (code == KeyCode.None && string.IsNullOrEmpty(character)) return "Error: key events require key or character.";
            }
            return QueueElement(elementId, kind.StartsWith("pointer") || kind == "scroll", element =>
            {
                switch (kind)
                {
                    case "focus":
                        if (!element.focusable) throw new InvalidOperationException("The element is not focusable.");
                        element.Focus(); break;
                    case "blur": element.Blur(); break;
                    case "submit": using (var evt = NavigationSubmitEvent.GetPooled()) element.SendEvent(evt); break;
                    case "cancel": using (var evt = NavigationCancelEvent.GetPooled()) element.SendEvent(evt); break;
                    case "keydown":
                    case "keyup":
                        var keyboard = new Event { type = kind == "keydown" ? EventType.KeyDown : EventType.KeyUp, keyCode = code,
                            character = string.IsNullOrEmpty(character) ? '\0' : character[0], modifiers = mods };
                        if (kind == "keydown") { using (var evt = KeyDownEvent.GetPooled(keyboard)) element.SendEvent(evt); }
                        else { using (var evt = KeyUpEvent.GetPooled(keyboard)) element.SendEvent(evt); }
                        break;
                    case "scroll":
                        using (var evt = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = ClickPosition(element), delta = new Vector2(deltaX, deltaY), modifiers = mods }))
                            element.SendEvent(evt);
                        break;
                    default:
                        SendPointer(element, kind == "pointerdown" ? EventType.MouseDown : kind == "pointerup" ? EventType.MouseUp : EventType.MouseMove,
                            ClickPosition(element), mods); break;
                }
            });
        }

        [AgentTool("Scroll the nearest enclosing UI Toolkit ScrollView to an inspected elementId. Works for clipped " +
                   "elements; hidden/disabled/detached targets are rejected. Reinspect afterward because virtualized " +
                   "lists can recycle controls. Queued; poll GetUIActionResult.", Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string ScrollUIToolkitElement(string elementId) => QueueElement(elementId, false, element =>
        {
            var scroll = element.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) throw new InvalidOperationException("No enclosing ScrollView was found.");
            scroll.ScrollTo(element);
        });

        private static string QueueElement(string elementId, bool needsHit, Action<VisualElement> action)
        {
            if (!Resolve(elementId, out var snapshot, out var element, out string error)) return error;
            try { CheckInteractive(snapshot, element, false); }
            catch (Exception ex) { return "Error: " + ex.Message; }
            return UIAutomationUtility.Queue(snapshot.Owner, () =>
            {
                if (!Resolve(elementId, out var current, out var target, out string stale)) throw new InvalidOperationException(stale);
                if (current.Owner is EditorWindow window) window.Focus();
                CheckInteractive(current, target, needsHit);
                action(target);
                if (current.Owner is EditorWindow repaint) repaint.Repaint();
            });
        }

        private static bool Resolve(string elementId, out Snapshot snapshot, out VisualElement element, out string error)
        {
            snapshot = null; element = null; error = "Error: stale or unknown elementId. Call InspectUIToolkit again.";
            if (string.IsNullOrEmpty(elementId)) return false;
            int separator = elementId.IndexOf(':');
            if (separator < 0 || !Snapshots.TryGetValue(elementId.Substring(0, separator), out snapshot) ||
                snapshot.Owner == null || DateTime.UtcNow - snapshot.Created > TimeSpan.FromMinutes(15) ||
                !snapshot.Handles.TryGetValue(elementId, out var handle)) return false;
            var root = snapshot.Root.Target as VisualElement;
            element = handle.Element.Target as VisualElement;
            if (root == null || element == null || (element != root && !root.Contains(element))) return false;
            var actualRoot = snapshot.Owner is EditorWindow w ? w.rootVisualElement : (snapshot.Owner as UIDocument)?.rootVisualElement;
            if (!ReferenceEquals(root, actualRoot) || element.name != handle.Name || ReadText(element) != handle.Text ||
                (element is IBindable binding ? binding.bindingPath : "") != handle.Binding) return false;
            error = null;
            return true;
        }

        private static void CheckInteractive(Snapshot snapshot, VisualElement element, bool needsHit)
        {
            if (snapshot.Owner is UIDocument document && (!EditorApplication.isPlaying || !snapshot.Playing ||
                snapshot.PlaySession != UIAutomationUtility.PlaySession || !document.isActiveAndEnabled))
                throw new InvalidOperationException("UIDocument actions require an active document inspected in the current Play session.");
            if (!element.enabledInHierarchy) throw new InvalidOperationException("The element is disabled.");
            if (!IsShown(element)) throw new InvalidOperationException("The element or an ancestor is hidden.");
            if (element.panel == null) throw new InvalidOperationException("The element is detached from its panel.");
            if (needsHit) ClickPosition(element);
        }

        private static void RequireCurrentHandle(string elementId, VisualElement expected)
        {
            if (!Resolve(elementId, out var snapshot, out var current, out string error) || !ReferenceEquals(current, expected))
                throw new InvalidOperationException(error ?? "The UI target changed during input dispatch.");
            CheckInteractive(snapshot, current, true);
        }

        private static Vector2 ClickPosition(VisualElement element)
        {
            Rect rect = element.worldBound;
            if (!FiniteRect(rect) || rect.width <= 0 || rect.height <= 0) throw new InvalidOperationException("No measured, nonempty layout rectangle is available.");
            var picked = element.panel?.Pick(rect.center);
            if (picked == null || (picked != element && !element.Contains(picked)))
                throw new InvalidOperationException("The element center is clipped, covered or not pickable. Scroll/reinspect before clicking.");
            return rect.center;
        }

        private static void SendPointer(VisualElement element, EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None)
        {
            var system = new Event { type = type, mousePosition = position, button = 0, clickCount = 1, modifiers = modifiers };
            if (type == EventType.MouseDown) { using (var evt = PointerDownEvent.GetPooled(system)) element.SendEvent(evt); }
            else if (type == EventType.MouseUp) { using (var evt = PointerUpEvent.GetPooled(system)) element.SendEvent(evt); }
            else { using (var evt = PointerMoveEvent.GetPooled(system)) element.SendEvent(evt); }
        }

        private static bool IsShown(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (!current.visible || current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.opacity <= 0) return false;
            return true;
        }
        private static string ReadText(VisualElement element)
        {
            try
            {
                if (element is TextElement text) return text.text ?? "";
                if (element is Foldout foldout) return foldout.text ?? "";
                var label = element.GetType().GetProperty("label", BindingFlags.Instance | BindingFlags.Public);
                return label != null && label.PropertyType == typeof(string) && label.GetIndexParameters().Length == 0 ? label.GetValue(element) as string ?? "" : "";
            }
            catch { return "(label unavailable)"; }
        }
        private static PropertyInfo ValueProperty(VisualElement element)
        {
            try
            {
                var property = element.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0) return null;
                return typeof(INotifyValueChanged<>).MakeGenericType(property.PropertyType).IsAssignableFrom(element.GetType()) ? property : null;
            }
            catch { return null; }
        }
        private static void ValidateObjectValue(VisualElement element, object value)
        {
            if (!(value is UnityEngine.Object obj)) return;
            if (obj == null) throw new ArgumentException("The assigned object was destroyed before dispatch.");
            var typeProperty = element.GetType().GetProperty("objectType", BindingFlags.Instance | BindingFlags.Public);
            if (typeProperty?.GetValue(element) is Type accepted && !accepted.IsInstanceOfType(obj))
                throw new ArgumentException("This ObjectField accepts " + accepted.Name + ".");
            if (element.GetType().GetProperty("allowSceneObjects", BindingFlags.Instance | BindingFlags.Public)?.GetValue(element) is bool allowScene &&
                !allowScene && (obj is GameObject || obj is Component) && !EditorUtility.IsPersistent(obj))
                throw new ArgumentException("This ObjectField does not allow scene objects.");
        }
        private static object ConvertValue(string value, Type type)
        {
            if (type == typeof(string)) return value ?? "";
            if (type == typeof(bool)) return bool.Parse(value);
            if (type.IsEnum)
            {
                object result = Enum.Parse(type, value, true);
                if (type.IsDefined(typeof(FlagsAttribute), false))
                {
                    var names = Enum.GetNames(type);
                    if (value.Split(',').Any(part => !names.Any(name => string.Equals(name, part.Trim(), StringComparison.OrdinalIgnoreCase))))
                        throw new ArgumentException("Use declared flag names separated by commas.");
                }
                else if (!Enum.IsDefined(type, result)) throw new ArgumentException("Use a declared enum value name.");
                return result;
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) || value == "0") return null;
                var obj = UIAutomationUtility.FindObject(int.Parse(value, CultureInfo.InvariantCulture));
                if (obj == null || !type.IsInstanceOfType(obj)) throw new ArgumentException("Object instance ID does not exist or has the wrong type.");
                return obj;
            }
            if (type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double))
            {
                object result = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                if (result is float f && !UIAutomationUtility.IsFinite(f) || result is double d && (double.IsNaN(d) || double.IsInfinity(d)))
                    throw new ArgumentException("Value must be finite.");
                return result;
            }
            if (type == typeof(Color) && value != null && value.StartsWith("#") && ColorUtility.TryParseHtmlString(value, out var color)) return color;
            int size = type == typeof(Vector2) || type == typeof(Vector2Int) ? 2 :
                type == typeof(Vector3) || type == typeof(Vector3Int) ? 3 : type == typeof(Vector4) || type == typeof(Color) ? 4 : 0;
            if (size == 0) throw new ArgumentException("Unsupported UI field value type: " + type.Name);
            var parts = (value ?? "").Split(',');
            if (parts.Length != size) throw new ArgumentException("Expected " + size + " comma-separated components.");
            if (type == typeof(Vector2Int)) return new Vector2Int(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
            if (type == typeof(Vector3Int)) return new Vector3Int(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
            var numbers = parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (numbers.Any(n => !UIAutomationUtility.IsFinite(n))) throw new ArgumentException("Components must be finite.");
            if (type == typeof(Vector2)) return new Vector2(numbers[0], numbers[1]);
            if (type == typeof(Vector3)) return new Vector3(numbers[0], numbers[1], numbers[2]);
            if (type == typeof(Vector4)) return new Vector4(numbers[0], numbers[1], numbers[2], numbers[3]);
            return new Color(numbers[0], numbers[1], numbers[2], numbers[3]);
        }
        private static bool Contains(string text, string part) => (text ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        private static string Quote(string text)
        {
            text = (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            return "\"" + (text.Length > 240 ? text.Substring(0, 240) + "…" : text) + "\"";
        }
        private static bool FiniteRect(Rect rect) => UIAutomationUtility.IsFinite(rect.x) && UIAutomationUtility.IsFinite(rect.y) &&
            UIAutomationUtility.IsFinite(rect.width) && UIAutomationUtility.IsFinite(rect.height);
        private static string RectText(Rect rect) => FiniteRect(rect) ? string.Format(CultureInfo.InvariantCulture,
            "({0:0.##},{1:0.##},{2:0.##},{3:0.##})", rect.x, rect.y, rect.width, rect.height) : "unavailable";
    }
}
