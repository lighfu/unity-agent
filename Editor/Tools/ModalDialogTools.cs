using System;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MCP;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Answers the modal dialog that is holding Unity's main thread.
    ///
    /// This is the follow-up to <see cref="EditorStateTools.GetEditorState"/>: that tool can
    /// report "a DIALOG is waiting for an answer" while everything else is frozen, and this one
    /// is the hand that presses the button. Like GetEditorState it is served on the listener /
    /// reader thread (see <see cref="ListenerThreadTools"/>), never queued for the main thread —
    /// queued, it would wait behind the very dialog it exists to dismiss.
    ///
    /// Windows only. The dialog's own message loop keeps running while Unity's main thread sits
    /// inside it, which is what makes posting a click or a key to it work.
    /// </summary>
    public static class ModalDialogTools
    {
        // "Answer" is not a prefix ToolRegistry knows, so this would default to Caution anyway;
        // the explicit flag documents that Caution is the intended level, not an accident.
        // Dangerous would hide it from MCP at the default expose level — and the whole point is
        // to be reachable from an unattended MCP session.
        [AgentTool("Press a button on the modal dialog that is currently blocking Unity's main thread " +
                   "(EditorUtility.DisplayDialog, 'Save changes?', a package's own popup, ...). " +
                   "Served OFF the main thread, so it works while every other tool is stuck — " +
                   "call it when GetEditorState reports modalDialog. Windows only. " +
                   "dryRun=true: do not press anything; return the dialog's title, message, and buttons. " +
                   "Do this first when you don't know what the dialog says. " +
                   "button: the visible label ('OK', 'Cancel', 'はい', ...) or its 0-based index from the left, " +
                   "or one of the special values 'enter' (accept via keyboard), 'escape' (cancel via keyboard), " +
                   "'close' (the title-bar X, usually = Cancel) — use those when the dialog draws its own " +
                   "buttons and dryRun lists none. " +
                   "titleContains: only act if the dialog title contains this (case-insensitive); otherwise " +
                   "nothing is pressed and the dialog is described instead — guard against answering the " +
                   "wrong dialog. " +
                   "Returns what was pressed and whether the dialog went away. The main thread may need a " +
                   "moment more to resume; confirm with GetEditorState rather than assuming.",
            Risk = ToolRisk.Caution, RiskExplicit = true)]
        public static string AnswerModalDialog(string button = "", string titleContains = "", bool dryRun = false)
        {
            if (!ModalDialogNative.IsSupported)
                return "Error: AnswerModalDialog is Windows-only. On this platform a human has to dismiss the dialog.";

            if (!ModalDialogNative.TryDescribe(out var dialog, out string describeError))
            {
                if (describeError != null) return $"Error: {describeError}";
                return "No modal dialog is showing. If Unity still looks stuck, GetEditorState will say why.";
            }

            var sb = new StringBuilder();
            AppendDescription(sb, dialog);

            if (!string.IsNullOrEmpty(titleContains)
                && (dialog.Title ?? "").IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                sb.AppendLine($"pressed: (nothing — title does not contain '{titleContains}')");
                return sb.ToString().TrimEnd();
            }

            if (dryRun || string.IsNullOrWhiteSpace(button))
            {
                sb.AppendLine(dryRun
                    ? "pressed: (nothing — dryRun)"
                    : "pressed: (nothing — no button given; pass button=<label|index|enter|escape|close>)");
                return sb.ToString().TrimEnd();
            }

            string what;
            bool ok;
            string error;
            switch (button.Trim().ToLowerInvariant())
            {
                case "enter":
                case "return":
                    what = "Enter key";
                    ok = ModalDialogNative.TryPostKey(dialog.Handle, ModalDialogNative.VK_RETURN, out error);
                    break;
                case "escape":
                case "esc":
                    what = "Escape key";
                    ok = ModalDialogNative.TryPostKey(dialog.Handle, ModalDialogNative.VK_ESCAPE, out error);
                    break;
                case "close":
                    what = "WM_CLOSE (title-bar X)";
                    ok = ModalDialogNative.TryClose(dialog.Handle, out error);
                    break;
                default:
                {
                    var target = FindButton(dialog, button);
                    if (target == null)
                    {
                        sb.AppendLine($"pressed: (nothing — no button matches '{button}')");
                        sb.AppendLine(dialog.Buttons.Count == 0
                            ? "hint: this dialog exposes no Win32 buttons (it draws its own). Try button='enter', 'escape' or 'close'."
                            : "hint: use one of the labels or indices listed above.");
                        return sb.ToString().TrimEnd();
                    }
                    what = $"'{target.Text}' (index {target.Index})";
                    ok = ModalDialogNative.TryClick(target, out error);
                    break;
                }
            }

            if (!ok)
            {
                sb.AppendLine($"pressed: (failed) {what} — {error}");
                return sb.ToString().TrimEnd();
            }

            bool dismissed = ModalDialogNative.WaitForDismiss(dialog.Handle, 1500);
            // Leave a trace in the Console: an unattended run must be able to find out afterwards
            // that a dialog was answered by a tool, and with what.
            AgentLogger.Warning(LogTag.Tool,
                $"AnswerModalDialog pressed {what} on \"{dialog.Title}\" (dismissed={dismissed})");

            sb.AppendLine($"pressed: {what}");
            sb.AppendLine($"dismissed: {(dismissed ? "true" : "false — the dialog is still showing 1.5 s later; it may have rejected the input or opened a follow-up")}");
            return sb.ToString().TrimEnd();
        }

        static void AppendDescription(StringBuilder sb, ModalDialogNative.DialogInfo d)
        {
            sb.AppendLine($"dialog: \"{d.Title}\" [{d.ClassName}]");
            sb.AppendLine($"message: {(string.IsNullOrEmpty(d.Message) ? "(none readable)" : d.Message.Replace("\r", "").Replace("\n", " / "))}");
            if (d.Buttons.Count == 0)
            {
                sb.AppendLine("buttons: (none exposed as Win32 controls)");
            }
            else
            {
                sb.Append("buttons:");
                foreach (var b in d.Buttons) sb.Append($" [{b.Index}] {b.Text}");
                sb.AppendLine();
            }

            // The raw child list is what tells us WHAT KIND of dialog this is when the button
            // heuristic comes up empty — a Unity-drawn window has different children from an
            // OS dialog. Cap it so a busy window cannot flood the reply.
            const int maxChildren = 24;
            sb.Append($"children: {d.Children.Count}");
            if (d.Children.Count > 0)
            {
                sb.Append(" —");
                for (int i = 0; i < d.Children.Count && i < maxChildren; i++)
                {
                    var c = d.Children[i];
                    string text = (c.Text ?? "").Replace("\r", "").Replace("\n", " ");
                    if (text.Length > 40) text = text.Substring(0, 40) + "…";
                    sb.Append($" {c.ClassName}{(text.Length > 0 ? $"(\"{text}\")" : "")}");
                }
                if (d.Children.Count > maxChildren) sb.Append($" … +{d.Children.Count - maxChildren} more");
            }
            sb.AppendLine();
        }

        static ModalDialogNative.ButtonInfo FindButton(ModalDialogNative.DialogInfo d, string button)
        {
            string wanted = button.Trim();
            if (int.TryParse(wanted, out int index))
                return index >= 0 && index < d.Buttons.Count ? d.Buttons[index] : null;

            foreach (var b in d.Buttons)
                if (string.Equals(b.Text, wanted, StringComparison.OrdinalIgnoreCase)) return b;
            foreach (var b in d.Buttons)
                if ((b.Text ?? "").IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) return b;
            return null;
        }
    }
}
