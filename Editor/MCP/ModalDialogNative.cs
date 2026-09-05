using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Win32 view of the modal dialog that is holding Unity's main thread, and the means to
    /// answer it. Everything here talks to the OS and never to Unity, so it is safe from the
    /// MCP listener / bridge reader thread — which is the only thread still running while a
    /// modal is up.
    ///
    /// Windows only. A modal on macOS is an NSAlert and needs a different mechanism; the
    /// methods report that instead of pretending.
    /// </summary>
    internal static class ModalDialogNative
    {
        internal sealed class ChildInfo
        {
            public IntPtr Handle;
            public string ClassName;
            public string Text;
        }

        internal sealed class ButtonInfo
        {
            public int Index;
            public IntPtr Handle;
            public string Text;
        }

        internal sealed class DialogInfo
        {
            public IntPtr Handle;
            public string Title;
            public string ClassName;
            public string Message;
            public readonly List<ChildInfo> Children = new List<ChildInfo>();
            public readonly List<ButtonInfo> Buttons = new List<ButtonInfo>();
        }

        public const int VK_RETURN = 0x0D;
        public const int VK_ESCAPE = 0x1B;

        public static bool IsSupported =>
#if UNITY_EDITOR_WIN
            true;
#else
            false;
#endif

#if UNITY_EDITOR_WIN
        const uint WM_CLOSE = 0x0010;
        const uint WM_GETTEXT = 0x000D;
        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;
        const uint BM_CLICK = 0x00F5;
        const uint SMTO_ABORTIFHUNG = 0x0002;

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam,
                                                uint flags, uint timeoutMs, out IntPtr result);

        /// <summary>
        /// Describes the modal popup currently disabling Unity's main window, with every visible
        /// child control listed so a caller can see what kind of dialog it is even when the
        /// button heuristics find nothing.
        /// </summary>
        public static bool TryDescribe(out DialogInfo info, out string error)
        {
            info = null;
            error = null;

            IntPtr popup = MainThreadWatchdog.GetModalPopupHandle();
            if (popup == IntPtr.Zero) return false;

            try
            {
                var d = new DialogInfo { Handle = popup };
                d.Title = WindowText(popup);
                d.ClassName = ClassOf(popup);

                var children = new List<IntPtr>();
                EnumChildWindows(popup, (h, _) => { children.Add(h); return true; }, IntPtr.Zero);

                var message = new StringBuilder();
                foreach (var h in children)
                {
                    if (!IsWindowVisible(h)) continue;
                    var child = new ChildInfo { Handle = h, ClassName = ClassOf(h), Text = ControlText(h) };
                    d.Children.Add(child);

                    // Dialog buttons are "Button" class windows; everything else that carries
                    // text (Static labels, the rich edit some task dialogs use) is the message.
                    if (string.Equals(child.ClassName, "Button", StringComparison.OrdinalIgnoreCase))
                    {
                        d.Buttons.Add(new ButtonInfo { Index = d.Buttons.Count, Handle = h, Text = StripAccelerator(child.Text) });
                    }
                    else if (!string.IsNullOrWhiteSpace(child.Text))
                    {
                        if (message.Length > 0) message.Append('\n');
                        message.Append(child.Text.Trim());
                    }
                }
                d.Message = message.ToString();
                info = d;
                return true;
            }
            catch (Exception ex)
            {
                error = $"enumerating the dialog failed: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>Presses a button the way a mouse click would, without moving the cursor.</summary>
        public static bool TryClick(ButtonInfo button, out string error)
        {
            error = null;
            if (button == null || button.Handle == IntPtr.Zero) { error = "no button handle"; return false; }
            if (!IsWindow(button.Handle)) { error = "the button window no longer exists"; return false; }
            if (!PostMessage(button.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero))
            {
                error = $"PostMessage(BM_CLICK) failed (Win32 error {Marshal.GetLastWin32Error()})";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Posts a key press to the dialog itself. This is the fallback for dialogs that draw
        /// their own buttons (no Win32 child controls): Enter accepts, Escape cancels, because
        /// the dialog's message loop is still running even though Unity's main thread is not.
        /// </summary>
        public static bool TryPostKey(IntPtr hwnd, int vk, out string error)
        {
            error = null;
            if (!IsWindow(hwnd)) { error = "the dialog window no longer exists"; return false; }
            if (!PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero)
                || !PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, (IntPtr)(1L << 31 | 1L << 30)))
            {
                error = $"PostMessage(WM_KEYDOWN/UP) failed (Win32 error {Marshal.GetLastWin32Error()})";
                return false;
            }
            return true;
        }

        /// <summary>Asks the dialog to close, which is what the title-bar X does (= Cancel).</summary>
        public static bool TryClose(IntPtr hwnd, out string error)
        {
            error = null;
            if (!IsWindow(hwnd)) { error = "the dialog window no longer exists"; return false; }
            if (!PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            {
                error = $"PostMessage(WM_CLOSE) failed (Win32 error {Marshal.GetLastWin32Error()})";
                return false;
            }
            return true;
        }

        public static bool IsStillShowing(IntPtr hwnd)
        {
            try { return IsWindow(hwnd) && IsWindowVisible(hwnd); }
            catch { return false; }
        }

        /// <summary>
        /// Waits briefly for the dialog to go away after an answer was posted. Returns true if it
        /// did. The main thread may take a moment longer to resume — that is for the caller to
        /// judge from GetEditorState's snapshotAge, not from this.
        /// </summary>
        public static bool WaitForDismiss(IntPtr hwnd, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (!IsStillShowing(hwnd)) return true;
                Thread.Sleep(100);
                waited += 100;
            }
            return !IsStillShowing(hwnd);
        }

        static string WindowText(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// WM_GETTEXT with a timeout rather than GetWindowText: for a control owned by another
        /// thread GetWindowText sends the message synchronously, and if that thread is wedged the
        /// listener thread would wedge with it. ABORTIFHUNG keeps this honest.
        /// </summary>
        static string ControlText(IntPtr h)
        {
            var sb = new StringBuilder(2048);
            SendMessageTimeout(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 300, out _);
            return sb.ToString();
        }

        static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>"&amp;OK" is displayed as "OK" with the O underlined; match what the user sees.</summary>
        static string StripAccelerator(string s) => (s ?? "").Replace("&", "").Trim();
#else
        public static bool TryDescribe(out DialogInfo info, out string error)
        {
            info = null;
            error = "modal dialog control is Windows-only";
            return false;
        }

        public static bool TryClick(ButtonInfo button, out string error) { error = "Windows-only"; return false; }
        public static bool TryPostKey(IntPtr hwnd, int vk, out string error) { error = "Windows-only"; return false; }
        public static bool TryClose(IntPtr hwnd, out string error) { error = "Windows-only"; return false; }
        public static bool IsStillShowing(IntPtr hwnd) => false;
        public static bool WaitForDismiss(IntPtr hwnd, int timeoutMs) => false;
#endif
    }
}
