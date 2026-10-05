using System;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>Window-scoped input for both IMGUI and UI Toolkit, without moving the OS cursor.</summary>
    public static class EditorUIInputTools
    {
        [AgentTool("Send input to an open Unity EditorWindow, including IMGUI controls that have no retained " +
                   "element tree. action: click, doubleClick, rightClick, mouseDown, mouseUp, mouseMove, " +
                   "mouseDrag, drag, scroll, contextClick, key, keyDown, keyUp, command or validateCommand. " +
                   "x/y are WINDOW-RELATIVE Unity logical points, top-left origin, excluding the dock tab strip; " +
                   "use ListUIElements/InspectUIToolkit measurements or scale screenshot pixels back to points. " +
                   "mouseButton: 0=left, 1=right, 2=middle. For drag, deltaX/deltaY are displacement in points " +
                   "and dragSteps is 1..60; mouseDrag is a single event for an existing press. For scroll, " +
                   "deltaX/deltaY are Unity scroll units (positive y scrolls down). key is a KeyCode name " +
                   "(Return, Escape, Tab, A, F1, ...); character optionally contains one UTF-16 character. " +
                   "modifiers accepts Shift, Control/Ctrl, Alt, Command/Cmd separated by '+' or ','. " +
                   "commandName is required for command/validateCommand (e.g. Copy, Paste, SelectAll). " +
                   "Prefer windowInstanceId from ListUIAutomationTargets; otherwise use a title substring, with " +
                   "matchIndex when several windows match. The target is activated/focused ONLY at dispatch. " +
                   "Returns a queued action ID; poll GetUIActionResult, then inspect/capture to verify the " +
                   "effect. Completion means input was dispatched, not that a control accepted it. A click " +
                   "can open a modal dialog; GetEditorState/AnswerModalDialog remain available on Windows.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string SendEditorUIEvent(string action, string windowTitleContains = "",
            int windowInstanceId = 0, int matchIndex = -1, float x = 0, float y = 0,
            int mouseButton = 0, float deltaX = 0, float deltaY = 0, string key = "",
            string character = "", string modifiers = "", string commandName = "", int dragSteps = 8)
        {
            if (!TryBuildEvents(action, x, y, mouseButton, deltaX, deltaY, key, character,
                    modifiers, commandName, dragSteps, out var events, out bool usesPosition, out string error))
                return "Error: " + error;

            var window = UIAutomationUtility.ResolveWindow(windowTitleContains, windowInstanceId, matchIndex, out error);
            if (window == null) return error;
            if (usesPosition && !ValidatePositions(events, window.position.size, out error))
                return "Error: " + error;

            return UIAutomationUtility.Queue(window, () => Dispatch(window, events, usesPosition));
        }

        [AgentTool("Click a point in any open Unity EditorWindow (UI Toolkit or IMGUI). x/y are window-relative " +
                   "Unity logical points, top-left origin, excluding the dock tab strip. mouseButton: " +
                   "0=left, 1=right, 2=middle; clickCount: 1 or 2. Prefer windowInstanceId from ListUIAutomationTargets. " +
                   "Activates/focuses at dispatch and returns a queued action ID; poll GetUIActionResult and " +
                   "inspect/capture to verify. For keyboard, scrolling or dragging use SendEditorUIEvent.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string ClickEditorUIAt(float x, float y, string windowTitleContains = "",
            int windowInstanceId = 0, int matchIndex = -1, int mouseButton = 0,
            int clickCount = 1, string modifiers = "")
        {
            if (clickCount != 1 && clickCount != 2)
                return "Error: clickCount must be 1 or 2.";
            return SendEditorUIEvent(clickCount == 2 ? "doubleClick" : "click", windowTitleContains,
                windowInstanceId, matchIndex, x, y, mouseButton, modifiers: modifiers);
        }

        [AgentTool("Type text into the currently focused text control of an open Unity EditorWindow, " +
                   "including IMGUI text fields. Click/focus the field first with ClickEditorUIAt or the " +
                   "UI Toolkit focus action. text is 1..4096 UTF-16 characters, including Japanese and " +
                   "valid Unicode surrogate pairs; invalid surrogate sequences and control characters " +
                   "other than newline/tab are rejected. CRLF and CR are normalized to one Return; " +
                   "newlines and tabs follow the control's normal keyboard behavior (a single-line " +
                   "field may submit, or Tab may move focus). Does not clear/select existing text " +
                   "or change the clipboard. Activates the target window at dispatch. Prefer " +
                   "windowInstanceId from ListUIAutomationTargets. Returns a queued action ID; " +
                   "poll GetUIActionResult and inspect/capture to verify the accepted text.",
            Category = "UIAutomation", Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string TypeEditorUIText(string text, string windowTitleContains = "",
            int windowInstanceId = 0, int matchIndex = -1)
        {
            if (!TryBuildTextEvents(text, out var events, out string error)) return "Error: " + error;
            var window = UIAutomationUtility.ResolveWindow(windowTitleContains, windowInstanceId, matchIndex, out error);
            if (window == null) return error;
            return UIAutomationUtility.Queue(window, () => Dispatch(window, events, false));
        }

        internal static bool TryBuildTextEvents(string text, out List<Event> events, out string error)
        {
            events = new List<Event>();
            error = null;
            if (string.IsNullOrEmpty(text) || text.Length > 4096)
            {
                error = "text must contain between 1 and 4096 UTF-16 characters.";
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (char.IsHighSurrogate(character))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    {
                        error = "text contains an unpaired high surrogate at index " + i + ".";
                        return false;
                    }
                    i++; // Validate the pair before building either event.
                }
                else if (char.IsLowSurrogate(character))
                {
                    error = "text contains an unpaired low surrogate at index " + i + ".";
                    return false;
                }
                else if (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')
                {
                    error = "text contains a non-text control character at index " + i + ".";
                    return false;
                }
            }
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                KeyCode code = KeyCode.None;
                if (character == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    character = '\n';
                }
                if (character == '\n') code = KeyCode.Return;
                else if (character == '\t') code = KeyCode.Tab;
                // Event.character is a UTF-16 code unit. Sending both members of a validated
                // surrogate pair preserves the full string accepted by Unity's text editor.
                events.Add(Keyboard(EventType.KeyDown, code, character, EventModifiers.None));
                events.Add(Keyboard(EventType.KeyUp, code, character, EventModifiers.None));
            }
            return true;
        }

        static void Dispatch(EditorWindow window, List<Event> events, bool usesPosition)
        {
            window.Focus();
            if (window == null) throw new InvalidOperationException("The target window closed while receiving focus.");
            var root = window.rootVisualElement;
            if (root == null || root.panel == null)
                throw new InvalidOperationException("The target window has no attached panel; show it and retry.");
            if (usesPosition && !ValidatePositions(events, window.position.size, out string error))
                throw new InvalidOperationException("Window geometry changed before dispatch: " + error);

            // EditorWindow.SendEvent forwards to the HostView. Its UI Toolkit panel includes the dock
            // margins, and HostView.InvokeOnGUI begins an offset area at root.worldBound. Translate
            // the caller's content-relative point into that host/panel coordinate space exactly once.
            var origin = root.worldBound.position;
            if (usesPosition && (!UIAutomationUtility.IsFinite(origin.x) || !UIAutomationUtility.IsFinite(origin.y)))
                throw new InvalidOperationException("The window has no valid layout origin yet; repaint and retry.");

            bool balanceMouse = events.Count > 1 && events[0].type == EventType.MouseDown &&
                                events[events.Count - 1].type == EventType.MouseUp;
            Event pendingPress = null;
            try
            {
                foreach (var evt in events)
                {
                    if (window == null) return; // A handler is allowed to close its own window.
                    if (usesPosition) evt.mousePosition += origin;
                    if (balanceMouse && evt.type == EventType.MouseDown) pendingPress = new Event(evt);
                    if (balanceMouse && evt.type == EventType.MouseUp) pendingPress = null;
                    try { window.SendEvent(evt); }
                    catch (ExitGUIException)
                    {
                        // Opening menus/popups can end the current GUI pass. Continue to the release
                        // event so a synthetic click never leaves the originating control pressed.
                    }
                }
            }
            finally
            {
                if (pendingPress != null && window != null)
                {
                    pendingPress.type = EventType.MouseUp;
                    try { window.SendEvent(pendingPress); }
                    catch (Exception) { /* Preserve the original dispatch failure, if any. */ }
                }
            }
            if (window != null) window.Repaint();
        }

        internal static bool ValidatePositions(List<Event> events, Vector2 size, out string error)
        {
            error = null;
            if (!UIAutomationUtility.IsFinite(size.x) || !UIAutomationUtility.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
            {
                error = "The target window has no valid drawing area.";
                return false;
            }
            foreach (var evt in events)
            {
                var p = evt.mousePosition;
                if (!UIAutomationUtility.IsFinite(p.x) || !UIAutomationUtility.IsFinite(p.y) ||
                    p.x < 0 || p.y < 0 || p.x >= size.x || p.y >= size.y)
                {
                    error = $"Point ({p.x},{p.y}) is outside the window's {size.x}x{size.y} logical-point drawing area.";
                    return false;
                }
            }
            return true;
        }

        internal static bool TryBuildEvents(string action, float x, float y, int mouseButton,
            float deltaX, float deltaY, string key, string character, string modifiers, string commandName,
            int dragSteps, out List<Event> events, out bool usesPosition, out string error)
        {
            events = new List<Event>();
            usesPosition = false;
            error = null;
            string kind = (action ?? "").Trim().ToLowerInvariant();
            if (!TryParseModifiers(modifiers, out var flags, out error)) return false;

            switch (kind)
            {
                case "click": case "doubleclick": case "rightclick":
                case "mousedown": case "mouseup": case "mousemove": case "mousedrag":
                case "drag": case "scroll": case "contextclick":
                    usesPosition = true;
                    break;
                case "key": case "keydown": case "keyup": case "command": case "validatecommand":
                    break;
                default:
                    error = "Unknown action. Use click, doubleClick, rightClick, mouseDown/up/move/drag, drag, " +
                            "scroll, contextClick, key/down/up, command or validateCommand.";
                    return false;
            }
            if (usesPosition)
            {
                if (!UIAutomationUtility.IsFinite(x) || !UIAutomationUtility.IsFinite(y) ||
                    !UIAutomationUtility.IsFinite(deltaX) || !UIAutomationUtility.IsFinite(deltaY))
                {
                    error = "Coordinates and deltas must be finite numbers.";
                    return false;
                }
                if (mouseButton < 0 || mouseButton > 2)
                {
                    error = "mouseButton must be 0 (left), 1 (right) or 2 (middle).";
                    return false;
                }
            }
            var position = new Vector2(x, y);
            var delta = new Vector2(deltaX, deltaY);
            switch (kind)
            {
                case "click": case "doubleclick": case "rightclick":
                    int button = kind == "rightclick" ? 1 : mouseButton;
                    int clicks = kind == "doubleclick" ? 2 : 1;
                    for (int i = 1; i <= clicks; i++)
                    {
                        events.Add(Mouse(EventType.MouseDown, position, button, flags, i));
                        events.Add(Mouse(EventType.MouseUp, position, button, flags, i));
                    }
                    break;
                case "drag":
                    if (dragSteps < 1 || dragSteps > 60)
                    {
                        error = "dragSteps must be between 1 and 60.";
                        return false;
                    }
                    events.Add(Mouse(EventType.MouseDown, position, mouseButton, flags));
                    for (int i = 1; i <= dragSteps; i++)
                        events.Add(Mouse(EventType.MouseDrag, position + delta * ((float)i / dragSteps),
                            mouseButton, flags, delta: delta / dragSteps));
                    events.Add(Mouse(EventType.MouseUp, position + delta, mouseButton, flags));
                    break;
                case "mousedown": events.Add(Mouse(EventType.MouseDown, position, mouseButton, flags)); break;
                case "mouseup": events.Add(Mouse(EventType.MouseUp, position, mouseButton, flags)); break;
                case "mousemove": events.Add(Mouse(EventType.MouseMove, position, mouseButton, flags, delta: delta)); break;
                case "mousedrag": events.Add(Mouse(EventType.MouseDrag, position, mouseButton, flags, delta: delta)); break;
                case "scroll": events.Add(Mouse(EventType.ScrollWheel, position, mouseButton, flags, delta: delta)); break;
                case "contextclick": events.Add(Mouse(EventType.ContextClick, position, 1, flags)); break;
                case "key": case "keydown": case "keyup":
                    if (!TryParseKey(key, character, out var keyCode, out char keyCharacter, out error)) return false;
                    if (kind != "keyup") events.Add(Keyboard(EventType.KeyDown, keyCode, keyCharacter, flags));
                    if (kind != "keydown") events.Add(Keyboard(EventType.KeyUp, keyCode, keyCharacter, flags));
                    break;
                case "command": case "validatecommand":
                    if (string.IsNullOrWhiteSpace(commandName) || commandName.Length > 128 ||
                        commandName.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    {
                        error = "commandName must be non-empty, at most 128 characters, and contain no line breaks or NUL.";
                        return false;
                    }
                    events.Add(new Event { type = kind == "command" ? EventType.ExecuteCommand : EventType.ValidateCommand,
                        commandName = commandName.Trim(), modifiers = flags });
                    break;
            }
            return true;
        }

        static Event Mouse(EventType type, Vector2 position, int button, EventModifiers modifiers,
            int clickCount = 1, Vector2 delta = default)
            => new Event { type = type, mousePosition = position, button = button,
                modifiers = modifiers, clickCount = clickCount, delta = delta };

        static Event Keyboard(EventType type, KeyCode key, char character, EventModifiers modifiers)
            => new Event { type = type, keyCode = key, character = character, modifiers = modifiers };

        internal static bool TryParseKey(string key, string character, out KeyCode code, out char value, out string error)
        {
            code = KeyCode.None;
            value = '\0';
            error = null;
            character = character ?? "";
            if (character.Length > 1 || (character.Length == 1 && char.IsSurrogate(character[0])))
            {
                error = "character must be one non-surrogate UTF-16 character; use TypeEditorUIText or a UI Toolkit value write for full text.";
                return false;
            }
            if (character.Length == 1) value = character[0];
            string name = (key ?? "").Trim();
            if (string.Equals(name, "Enter", StringComparison.OrdinalIgnoreCase)) name = "Return";
            if (string.Equals(name, "Esc", StringComparison.OrdinalIgnoreCase)) name = "Escape";
            if (name.Length > 0 && (char.IsDigit(name[0]) || name[0] == '-' ||
                    !Enum.TryParse(name, true, out code) || !Enum.IsDefined(typeof(KeyCode), code)))
            {
                error = $"Unknown KeyCode '{key}'. Supply a name such as Return, Escape, Tab, A or F1.";
                return false;
            }
            if (code == KeyCode.None && value == '\0')
            {
                error = "Give key or character for a keyboard event.";
                return false;
            }
            // IMGUI text editing checks character as well as keyCode for navigation controls.
            if (value == '\0')
            {
                if (code == KeyCode.Return || code == KeyCode.KeypadEnter) value = '\n';
                else if (code == KeyCode.Tab) value = '\t';
                else if (code == KeyCode.Backspace) value = '\b';
            }
            return true;
        }

        internal static bool TryParseModifiers(string text, out EventModifiers flags, out string error)
        {
            flags = EventModifiers.None;
            error = null;
            foreach (string part in (text ?? "").Split(new[] { '+', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "none": break;
                    case "shift": flags |= EventModifiers.Shift; break;
                    case "control": case "ctrl": flags |= EventModifiers.Control; break;
                    case "alt": flags |= EventModifiers.Alt; break;
                    case "command": case "cmd": flags |= EventModifiers.Command; break;
                    case "capslock": flags |= EventModifiers.CapsLock; break;
                    case "numeric": flags |= EventModifiers.Numeric; break;
                    case "functionkey": flags |= EventModifiers.FunctionKey; break;
                    default:
                        error = $"Unknown modifier '{part}'. Use Shift, Control/Ctrl, Alt or Command/Cmd.";
                        return false;
                }
            }
            return true;
        }
    }
}
