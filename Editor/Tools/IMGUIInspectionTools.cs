using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>One-repaint snapshots from Unity's optional internal IMGUI debugger.</summary>
    [InitializeOnLoad]
    public static class IMGUIInspectionTools
    {
        const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const int MaxStoredResults = 32;
        const int MaxScanInstructions = 20000;
        static readonly object ResultLock = new object();
        static readonly Dictionary<string, string> Results = new Dictionary<string, string>();
        static readonly Queue<string> ResultOrder = new Queue<string>();
        static Session _active;
        static EventInfo _ownershipEvent;
        static Delegate _ownershipHandler;
        static UnityEngine.Object _debuggedHost;
        static string _ownershipError;
        internal static UnityEngine.Object DebuggedHost => _debuggedHost;

        sealed class Session
        {
            public string Id;
            public EditorWindow Window;
            public UnityEngine.Object Host;
            public int MaxRows;
            public string Filter;
            public bool Activate;
            public double Timeout;
            public double Started;
            public bool InstructionsChanged;
            public bool OwnsDebugger;
            public bool HooksInstalled;
            public MethodInfo Stop;
            public MethodInfo GetDraw;
            public MethodInfo GetNamed;
            public MethodInfo GetProperty;
            public MethodInfo GetUnified;
            public EventInfo InstructionsEvent;
            public EventInfo DebuggingEvent;
            public Delegate DebuggingHandler;
        }

        static IMGUIInspectionTools()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CancelForReload;
            EditorApplication.quitting += CancelForReload;
            try
            {
                var helper = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIViewDebuggerHelper");
                _ownershipEvent = helper?.GetEvent("onDebuggingViewchanged", StaticAny);
                if (_ownershipEvent == null) throw new NotSupportedException("IMGUI debugger ownership events are unavailable.");
                _ownershipHandler = Delegate.CreateDelegate(_ownershipEvent.EventHandlerType,
                    typeof(IMGUIInspectionTools).GetMethod(nameof(TrackDebuggingChanged), StaticAny));
                _ownershipEvent.GetAddMethod(true).Invoke(null, new object[] { _ownershipHandler });
            }
            catch (Exception ex) { _ownershipError = ExceptionMessage(ex); }
        }

        [AgentTool("Inspect drawn IMGUI labels, buttons/field artwork, named controls and property paths in an " +
                   "open Unity EditorWindow using Unity's internal IMGUI debugger. This complements " +
                   "InspectUIToolkit/ListUIElements for Console, Project, custom OnGUI windows and IMGUI " +
                   "regions embedded in UI Toolkit. Returns inspectionId immediately; poll " +
                   "GetIMGUIInspectionResult after the editor repaints. activate=true selects/focuses the " +
                   "target tab at dispatch; false requires it already be the active tab. maxInstructions " +
                   "is 1..1000 output rows; filter matches visible text, tooltip, style, control name or " +
                   "property path. timeoutSeconds is 0.5..30. An existing IMGUI debugger session is never " +
                   "replaced; close its inspection first if this tool reports busy. " +
                   "Coverage is best-effort: draw instructions are artwork, not a complete control tree; " +
                   "one widget may generate several rows, custom GPU drawing/optimized inspector blocks " +
                   "may be absent, and callbacks/values are not reconstructable. Internal APIs can be " +
                   "unavailable on a Unity version. Use measured rects with ClickEditorUIAt, or send " +
                   "keyboard/scroll/command events with SendEditorUIEvent, then verify by another snapshot.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string InspectIMGUI(string windowTitleContains = "", int windowInstanceId = 0,
            int matchIndex = -1, int maxInstructions = 200, string filter = "", float timeoutSeconds = 5,
            bool activate = true)
        {
            if (maxInstructions < 1 || maxInstructions > 1000)
                return "Error: maxInstructions must be between 1 and 1000.";
            if (!UIAutomationUtility.IsFinite(timeoutSeconds) || timeoutSeconds < 0.5f || timeoutSeconds > 30)
                return "Error: timeoutSeconds must be between 0.5 and 30.";
            if (_active != null)
                return $"Error: IMGUI inspection {_active.Id} is still pending. Poll GetIMGUIInspectionResult first.";
            var window = UIAutomationUtility.ResolveWindow(windowTitleContains, windowInstanceId, matchIndex, out string error);
            if (window == null) return "Error: " + error;
            var session = new Session
            {
                Id = "imgui-" + Guid.NewGuid().ToString("N"), Window = window,
                MaxRows = maxInstructions, Filter = filter ?? "", Activate = activate, Timeout = timeoutSeconds,
            };
            _active = session; // Reserve the single native debugger before returning to the caller.
            Store(session.Id, $"inspectionId={session.Id}\nstatus=queued");
            EditorApplication.CallbackFunction begin = null;
            begin = () =>
            {
                EditorApplication.update -= begin;
                Begin(session);
            };
            EditorApplication.update += begin;
            return $"inspectionId={session.Id}\nstatus=queued\nPoll GetIMGUIInspectionResult; no UI input has been sent yet.";
        }

        [AgentTool("Read an asynchronous InspectIMGUI snapshot by inspectionId. Returns queued/capturing, " +
                   "completed with measured draw/control/property rows, or failed with the reason. " +
                   "This reads only a bounded in-memory result cache and is safe while Unity's main " +
                   "thread is waiting in a modal dialog. Results expire after 32 newer requests or a " +
                   "domain reload; retry InspectIMGUI for a current snapshot.",
            Category = "UIAutomation", Risk = ToolRisk.Safe)]
        public static string GetIMGUIInspectionResult(string inspectionId)
        {
            if (string.IsNullOrWhiteSpace(inspectionId)) return "Error: inspectionId is required.";
            lock (ResultLock)
                return Results.TryGetValue(inspectionId.Trim(), out string result) ? result
                    : "Error: unknown or expired inspectionId. Start another InspectIMGUI snapshot.";
        }

        static void Begin(Session session)
        {
            if (_active != session) return;
            try
            {
                if (session.Window == null) throw new InvalidOperationException("The target window closed before inspection started.");
                var editorAssembly = typeof(EditorWindow).Assembly;
                var helper = editorAssembly.GetType("UnityEditor.GUIViewDebuggerHelper");
                if (helper == null)
                    throw new NotSupportedException("Unity's internal IMGUI debugger API is unavailable on this editor version.");
                if (_ownershipError != null || _ownershipHandler == null)
                    throw new NotSupportedException("Cannot verify ownership of Unity's internal IMGUI debugger: " + _ownershipError);
                if (ExternalDebuggerIsActive())
                    throw new InvalidOperationException("An IMGUI debugger session is already active; stop its inspection and retry.");

                if (session.Activate) session.Window.Focus();
                if (session.Window == null) throw new InvalidOperationException("The target closed while receiving focus.");
                if (ExternalDebuggerIsActive())
                    throw new InvalidOperationException("An IMGUI debugger session started while the target was receiving focus; retry after it stops.");
                var parent = FindField(typeof(EditorWindow), "m_Parent")?.GetValue(session.Window) as UnityEngine.Object;
                if (parent == null) throw new InvalidOperationException("The target has no HostView; show its window first.");
                var actual = FindProperty(parent.GetType(), "actualView")?.GetValue(parent, null) as EditorWindow;
                if (actual != session.Window)
                    throw new InvalidOperationException("The target is a background tab. Pass activate=true to select it.");
                session.Host = parent;
                var start = helper.GetMethod("DebugWindow", StaticAny);
                session.Stop = helper.GetMethod("StopDebugging", StaticAny);
                session.GetDraw = helper.GetMethod("GetDrawInstructions", StaticAny);
                session.GetNamed = helper.GetMethod("GetNamedControlInstructions", StaticAny);
                session.GetProperty = helper.GetMethod("GetPropertyInstructions", StaticAny);
                session.GetUnified = helper.GetMethod("GetUnifiedInstructions", StaticAny);
                session.InstructionsEvent = helper.GetEvent("onViewInstructionsChanged", StaticAny);
                session.DebuggingEvent = helper.GetEvent("onDebuggingViewchanged", StaticAny);
                if (start == null || session.Stop == null || session.GetDraw == null ||
                    session.InstructionsEvent == null || session.DebuggingEvent == null)
                    throw new NotSupportedException("This Unity version does not expose the required IMGUI debugger methods/events.");

                session.DebuggingHandler = Delegate.CreateDelegate(session.DebuggingEvent.EventHandlerType,
                    typeof(IMGUIInspectionTools).GetMethod(nameof(OnDebuggingChanged), StaticAny));
                session.InstructionsEvent.GetAddMethod(true).Invoke(null, new object[] { (Action)OnInstructionsChanged });
                session.HooksInstalled = true;
                session.DebuggingEvent.GetAddMethod(true).Invoke(null, new object[] { session.DebuggingHandler });
                session.Started = EditorApplication.timeSinceStartup;
                EditorApplication.update += Update;
                // Mark ownership before DebugWindow, because native callbacks may be synchronous.
                session.OwnsDebugger = true;
                start.Invoke(null, new object[] { parent });
                if (_active != session) return;
                helper.GetMethod("ClearInstructions", StaticAny)?.Invoke(null, null);
                session.InstructionsChanged = false;
                session.Window.Repaint();
                Store(session.Id, $"inspectionId={session.Id}\nstatus=capturing\nWaiting for the target's next repaint.");
            }
            catch (Exception ex)
            {
                Finish(session, "status=failed\nError: " + ExceptionMessage(ex));
            }
        }

        static void OnInstructionsChanged()
        {
            if (_active != null) _active.InstructionsChanged = true;
        }

        static void TrackDebuggingChanged(UnityEngine.Object view, bool debugging)
        {
            if (debugging) _debuggedHost = view;
            else if (_debuggedHost == view) _debuggedHost = null;
        }

        static bool ExternalDebuggerIsActive()
        {
            // GUIDebugger.active is relative to the current GUI pass: it reads false on an
            // EditorApplication.update even while a window is being debugged. Track the native
            // lifecycle events globally instead, and cover sessions restored before our listener.
            if (_debuggedHost != null) return true;
            var inspectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIViewDebuggerWindow");
            if (inspectorType == null) return false;
            var inspected = FindField(inspectorType, "m_Inspected");
            if (inspected == null) return false;
            foreach (UnityEngine.Object inspector in Resources.FindObjectsOfTypeAll(inspectorType))
                if (inspector != null && inspected.GetValue(inspector) is UnityEngine.Object host && host != null)
                    return true;
            return false;
        }

        // Action<GUIView,bool> accepts a method whose reference parameter is a GUIView base class.
        static void OnDebuggingChanged(UnityEngine.Object view, bool debugging)
        {
            var session = _active;
            if (session == null) return;
            if ((debugging && view != session.Host) || (!debugging && view == session.Host))
            {
                session.OwnsDebugger = false;
                Finish(session, "status=failed\nError: another debugger session replaced or stopped this inspection.");
            }
        }

        static void Update()
        {
            var session = _active;
            if (session == null) return;
            try
            {
                if (session.Window == null || session.Host == null)
                    throw new InvalidOperationException("The target window or its host closed during inspection.");
                var actual = FindProperty(session.Host.GetType(), "actualView")?.GetValue(session.Host, null) as EditorWindow;
                if (actual != session.Window)
                    throw new InvalidOperationException("The active tab changed during inspection; no other tab's metadata was returned.");
                double elapsed = EditorApplication.timeSinceStartup - session.Started;
                if (session.InstructionsChanged && elapsed >= 0.1)
                {
                    Finish(session, BuildReport(session));
                    return;
                }
                if (elapsed >= session.Timeout)
                    throw new TimeoutException("The target did not produce debugger instructions before the timeout. " +
                                               "Reopen/activate it and capture its pixels instead.");
            }
            catch (Exception ex)
            {
                Finish(session, "status=failed\nError: " + ExceptionMessage(ex));
            }
        }

        static string BuildReport(Session session)
        {
            var draws = ReadInstructions(session.GetDraw);
            var named = ReadInstructions(session.GetNamed);
            var properties = ReadInstructions(session.GetProperty);
            var unified = ReadInstructions(session.GetUnified);
            var enabled = new Dictionary<string, bool>();
            for (int i = 0; i < unified.Count && i < MaxScanInstructions; i++)
            {
                object instruction = unified[i];
                var type = ReadField(instruction, "type");
                var index = ReadField(instruction, "typeInstructionIndex");
                var state = ReadField(instruction, "enabled");
                if (type != null && index != null && state is bool value)
                    enabled[type + ":" + index] = value;
            }
            var origin = session.Window.rootVisualElement.worldBound.position;
            if (!UIAutomationUtility.IsFinite(origin.x) || !UIAutomationUtility.IsFinite(origin.y))
                throw new InvalidOperationException("The window's layout origin is unavailable, so instruction rects cannot be translated safely.");
            var rows = new List<string>();
            int matches = 0;
            int scanned = 0;
            for (int i = 0; i < draws.Count && scanned < MaxScanInstructions; i++, scanned++)
            {
                var draw = draws[i];
                var content = ReadField(draw, "usedGUIContent") as GUIContent;
                var style = ReadField(draw, "usedGUIStyle") as GUIStyle;
                string label = ReadField(draw, "label") as string;
                string text = content?.text ?? label ?? "";
                string row = $"[draw:{i}] text=\"{Clean(text)}\" label=\"{Clean(label)}\" " +
                             $"tooltip=\"{Clean(content?.tooltip)}\" style=\"{Clean(style?.name)}\" " +
                             $"rect={FormatRect(ReadField(draw, "rect"), origin)} " +
                             $"visibleRect={FormatRect(ReadField(draw, "visibleRect"), origin)} " + State(enabled, "kStyleDraw", i);
                AddRow(row, session, rows, ref matches);
            }
            for (int i = 0; i < named.Count && scanned < MaxScanInstructions; i++, scanned++)
            {
                var control = named[i];
                string row = $"[named:{i}] name=\"{Clean(ReadField(control, "name") as string)}\" " +
                             $"controlId={ReadField(control, "id")} rect={FormatRect(ReadField(control, "rect"), origin)} " + State(enabled, "kLayoutNamedControl", i);
                AddRow(row, session, rows, ref matches);
            }
            for (int i = 0; i < properties.Count && scanned < MaxScanInstructions; i++, scanned++)
            {
                var property = properties[i];
                string row = $"[property:{i}] path=\"{Clean(ReadField(property, "path") as string)}\" " +
                             $"targetType=\"{Clean(ReadField(property, "targetTypeName") as string)}\" " +
                             $"rect={FormatRect(ReadField(property, "rect"), origin)} " + State(enabled, "kPropertyBegin", i);
                AddRow(row, session, rows, ref matches);
            }
            var sb = new StringBuilder();
            sb.AppendLine("status=completed");
            sb.AppendLine($"IMGUI snapshot: \"{Clean(session.Window.titleContent?.text)}\" windowInstanceId={UIAutomationUtility.InstanceId(session.Window)}");
            sb.AppendLine($"draws={draws.Count}, namedControls={named.Count}, properties={properties.Count}, matches={matches}, shown={rows.Count}");
            if ((long)draws.Count + named.Count + properties.Count > scanned)
                sb.AppendLine($"NOTE: instruction scan stopped at {MaxScanInstructions}; match/omitted counts are lower bounds. Inspect a smaller window/layout to see further instructions.");
            if (unified.Count > MaxScanInstructions)
                sb.AppendLine($"NOTE: enabled-state scan stopped at {MaxScanInstructions}; further states are unknown.");
            sb.AppendLine("rect/visibleRect are WINDOW-RELATIVE Unity logical points, top-left origin; dock tab strip excluded.");
            sb.AppendLine("NOTE: these are drawing instructions, not a complete widget tree. Rows can overlap or represent fragments of one control; snapshot indices expire on the next repaint. Custom drawing and cached/optimized inspector blocks may be absent. Values/callbacks are not enumerated.");
            if (session.Activate) sb.AppendLine("NOTE: the target was selected/focused for this repaint.");
            if (session.GetNamed == null || session.GetProperty == null || session.GetUnified == null)
                sb.AppendLine("NOTE: some optional debugger APIs are unavailable; missing metadata is reported as unknown.");
            if (matches > rows.Count) sb.AppendLine($"NOTE: {matches - rows.Count} matching rows omitted by maxInstructions={session.MaxRows}.");
            if (draws.Count + named.Count + properties.Count == 0)
                sb.AppendLine("No IMGUI instructions were exposed. This does not prove the window has no UI; use InspectUIToolkit and CaptureEditorWindow.");
            else if (rows.Count == 0) sb.AppendLine("No instruction matches the filter; widen or remove it.");
            foreach (string row in rows) sb.AppendLine(row);
            return sb.ToString().TrimEnd();
        }

        static void AddRow(string row, Session session, List<string> rows, ref int matches)
        {
            if (session.Filter.Length > 0 && row.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            matches++;
            if (rows.Count < session.MaxRows) rows.Add(row);
        }

        static IList ReadInstructions(MethodInfo method)
        {
            if (method == null) return Array.Empty<object>();
            var list = (IList)Activator.CreateInstance(method.GetParameters()[0].ParameterType);
            method.Invoke(null, new object[] { list });
            return list;
        }

        static object ReadField(object instance, string name)
            => instance?.GetType().GetField(name, InstanceAny)?.GetValue(instance);

        static string State(Dictionary<string, bool> states, string type, int index)
            => states.TryGetValue(type + ":" + index, out bool value) ? "enabled=" + (value ? "yes" : "no") : "enabled=unknown";

        static string FormatRect(object value, Vector2 origin)
        {
            if (!(value is Rect rect) || !UIAutomationUtility.IsFinite(rect.x) || !UIAutomationUtility.IsFinite(rect.y) ||
                !UIAutomationUtility.IsFinite(rect.width) || !UIAutomationUtility.IsFinite(rect.height)) return "unavailable";
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##}x{3:0.##})",
                rect.x - origin.x, rect.y - origin.y, rect.width, rect.height);
        }

        static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('"', '\'');
            return text.Length <= 160 ? text : text.Substring(0, 160) + "...";
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        static void Finish(Session session, string result)
        {
            if (_active != session) return;
            _active = null;
            EditorApplication.update -= Update;
            try
            {
                if (session.OwnsDebugger) session.Stop?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                result += "\nNOTE: stopping the IMGUI debugger failed: " + ExceptionMessage(ex);
            }
            finally
            {
                if (session.HooksInstalled)
                {
                    try { session.InstructionsEvent?.GetRemoveMethod(true)?.Invoke(null, new object[] { (Action)OnInstructionsChanged }); }
                    catch (Exception ex) { result += "\nNOTE: debugger event cleanup failed: " + ExceptionMessage(ex); }
                    try { session.DebuggingEvent?.GetRemoveMethod(true)?.Invoke(null, new object[] { session.DebuggingHandler }); }
                    catch (Exception ex) { result += "\nNOTE: debugger ownership cleanup failed: " + ExceptionMessage(ex); }
                }
                Store(session.Id, "inspectionId=" + session.Id + "\n" + result);
            }
        }

        static void CancelForReload()
        {
            if (_active != null) Finish(_active, "status=failed\nError: editor reload/quit interrupted the inspection.");
            try { _ownershipEvent?.GetRemoveMethod(true)?.Invoke(null, new object[] { _ownershipHandler }); }
            catch (Exception) { /* The domain is going away; preserve any user-owned debugger. */ }
        }

        static string ExceptionMessage(Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            return error.Message;
        }

        static void Store(string id, string result)
        {
            lock (ResultLock)
            {
                if (!Results.ContainsKey(id)) ResultOrder.Enqueue(id);
                Results[id] = result;
                while (ResultOrder.Count > MaxStoredResults) Results.Remove(ResultOrder.Dequeue());
            }
        }
    }
}
