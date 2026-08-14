#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Win32-only P/Invoke layer for window / screen capture.
    /// Constraint: asmdef has allowUnsafeCode=false → use Marshal.Copy semantics only,
    /// no unsafe pointer ops. GDI handles MUST be released in try/finally.
    /// </summary>
    internal static class WindowCaptureNative
    {
        // ─── BitBlt ROP codes ───
        private const uint SRCCOPY = 0x00CC0020;
        private const uint CAPTUREBLT = 0x40000000;

        // ─── DIB constants ───
        private const uint BI_RGB = 0;
        private const uint DIB_RGB_COLORS = 0;

        // ─── MONITORINFOF flags ───
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        // SetThreadDpiAwarenessContext: Per-Monitor V2 (Win10 1703+).
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // ─── Structs ───
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        // For 32-bit BI_RGB the color table is unused but the struct still needs one slot.
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors0;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        // ─── P/Invoke ───
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);
        [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        private const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFO bmi, uint usage);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        // ─── DPI Awareness Scope ───
        // Wrap any block that reads physical screen coordinates so DPI scaling
        // matches between EditorGUIUtility.pixelsPerPoint and Win32 results.
        public sealed class DpiScope : IDisposable
        {
            private IntPtr _previous;
            private bool _applied;

            public DpiScope()
            {
                try
                {
                    _previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                    _applied = true;
                }
                catch (EntryPointNotFoundException)
                {
                    // Pre-Win10 1703: function unavailable. Continue without DPI override.
                    _applied = false;
                }
            }

            public void Dispose()
            {
                if (!_applied) return;
                try { SetThreadDpiAwarenessContext(_previous); } catch { /* swallow */ }
                _applied = false;
            }
        }

        // ─── Monitor descriptor ───
        public struct MonitorDescriptor
        {
            public string DeviceName;   // e.g. \\.\DISPLAY1
            public bool IsPrimary;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public uint DpiX;
            public uint DpiY;
        }

        public static List<MonitorDescriptor> EnumerateMonitors()
        {
            var result = new List<MonitorDescriptor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT lprc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    uint dpiX = 96, dpiY = 96;
                    try { GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out dpiX, out dpiY); }
                    catch { /* Shcore.dll missing on Win7 — keep default 96 */ }

                    result.Add(new MonitorDescriptor
                    {
                        DeviceName = mi.szDevice ?? string.Empty,
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        X = mi.rcMonitor.left,
                        Y = mi.rcMonitor.top,
                        Width = mi.rcMonitor.right - mi.rcMonitor.left,
                        Height = mi.rcMonitor.bottom - mi.rcMonitor.top,
                        DpiX = dpiX,
                        DpiY = dpiY,
                    });
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        // ─── Resolve scale for a Unity-reported window rect ───
        // Unity's EditorWindow.position uses per-monitor logical coordinates: each monitor's
        // values are scaled by that monitor's DPI factor (NOT by EditorGUIUtility.pixelsPerPoint
        // which is a single global value). To find the physical screen rect, we try each monitor's
        // scale and pick the one whose resulting physical rect's center falls inside that monitor.
        public static (int x, int y, int w, int h) UnityRectToPhysical(
            float unityX, float unityY, float unityW, float unityH, List<MonitorDescriptor> monitors)
        {
            foreach (var m in monitors)
            {
                float scale = m.DpiX > 0 ? m.DpiX / 96f : 1f;
                int px = (int)System.Math.Round(unityX * scale);
                int py = (int)System.Math.Round(unityY * scale);
                int pw = (int)System.Math.Round(unityW * scale);
                int ph = (int)System.Math.Round(unityH * scale);
                int cx = px + pw / 2;
                int cy = py + ph / 2;
                if (cx >= m.X && cx < m.X + m.Width && cy >= m.Y && cy < m.Y + m.Height)
                {
                    return (px, py, pw, ph);
                }
            }
            // No monitor matched — fall back to no scaling (1.0).
            return ((int)unityX, (int)unityY, (int)unityW, (int)unityH);
        }

        // ─── Resolve monitor by id ───
        // monitorId: "primary" | integer index | device name like "\\.\DISPLAY1"
        public static MonitorDescriptor? ResolveMonitor(string monitorId, List<MonitorDescriptor> monitors)
        {
            if (monitors == null || monitors.Count == 0) return null;
            if (string.IsNullOrEmpty(monitorId) || string.Equals(monitorId, "primary", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var m in monitors)
                {
                    if (m.IsPrimary) return m;
                }
                return monitors[0];
            }
            if (int.TryParse(monitorId, out int idx))
            {
                if (idx >= 0 && idx < monitors.Count) return monitors[idx];
                return null;
            }
            foreach (var m in monitors)
            {
                if (string.Equals(m.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        // ─── Foreground activation ───
        // EditorWindow.Focus() only activates the tab inside Unity; it does not raise the Unity
        // process above other applications. Capturing by screen rect therefore photographs
        // whatever is actually on top — a browser, a chat window, the user's private screen.
        // Windows refuses a bare SetForegroundWindow from a background app, so the standard
        // AttachThreadInput dance is required to borrow the foreground thread's input state.
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private const int SW_RESTORE = 9;

        /// <summary>
        /// Raises the Unity editor window above other applications so a screen-rect capture
        /// photographs Unity rather than whatever the user happens to have in front.
        /// Returns false with a reason when the OS declines; callers should continue and capture
        /// anyway rather than failing the whole tool.
        /// </summary>
        public static bool TryBringUnityToForeground(out string error)
        {
            // Handle acquisition and its two failure messages are shared with GetUnityMainWindow below;
            // error is already filled when this returns Zero.
            IntPtr hwnd = ResolveProcessMainWindow(out error);
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

                IntPtr foreground = GetForegroundWindow();
                if (foreground == hwnd) return true;

                uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
                uint currentThread = GetCurrentThreadId();

                bool attached = false;
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);

                try
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(currentThread, foregroundThread, false);
                }

                if (GetForegroundWindow() == hwnd) return true;
                error = "Windows declined the foreground request (foreground lock timeout)";
                return false;
            }
            catch (Exception ex)
            {
                error = $"foreground activation failed: {ex.Message}";
                return false;
            }
        }

        // ─── Window geometry and identity ───
        // Everything needed to answer "which window is this, where is it, and is it showing anything"
        // without going through Unity's own EditorWindow list — required for non-Unity targets (VRChat,
        // Blender, a browser, a dropdown) and for locating the editor's own top-level window.
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();

        /// <summary>
        /// The editor's own top-level HWND, from Process.MainWindowHandle. Three capture paths need it —
        /// the foreground dance above raises it, <see cref="TryPrintWindow"/> can photograph it, and a
        /// docked EditorWindow can only be cut out of a main-window image once its screen rect is known —
        /// so the acquisition and its two failure messages live here instead of being re-implemented per
        /// call site with slightly different wording.
        /// Returns IntPtr.Zero with <paramref name="error"/> filled; never throws.
        /// </summary>
        private static IntPtr ResolveProcessMainWindow(out string error)
        {
            error = null;
            try
            {
                IntPtr hwnd;
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                {
                    hwnd = proc.MainWindowHandle;
                }
                if (hwnd == IntPtr.Zero)
                    error = "Unity main window handle unavailable (headless or minimized to tray?)";
                return hwnd;
            }
            catch (Exception ex)
            {
                error = $"cannot resolve Unity main window: {ex.Message}";
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// HWND of the Unity editor's main window, or IntPtr.Zero when it cannot be resolved (batch mode,
        /// no main window yet). Prefer the out-error overload when the result is reported to a user: a bare
        /// Zero cannot explain itself, and "no window" and "lookup threw" need different advice.
        /// </summary>
        public static IntPtr GetUnityMainWindow()
        {
            return ResolveProcessMainWindow(out _);
        }

        /// <summary>Same as <see cref="GetUnityMainWindow()"/> but reports why the handle is unavailable.</summary>
        public static IntPtr GetUnityMainWindow(out string error)
        {
            return ResolveProcessMainWindow(out error);
        }

        /// <summary>
        /// Screen rect of any window in PHYSICAL pixels, from GetWindowRect — i.e. the full window
        /// including frame and caption, not the client area.
        ///
        /// This is the frame of reference <see cref="TryPrintWindow"/> captures in, which is the whole
        /// reason it is public: to cut a sub-rectangle out of a PrintWindow bitmap the caller subtracts
        /// this origin from the target's physical screen position. Reading the origin from anywhere else
        /// (client rect, DWM extended frame bounds) shifts every crop by the frame thickness and still
        /// looks like a successful capture, just of slightly the wrong area.
        ///
        /// Call inside a <see cref="DpiScope"/>. Without it a per-monitor-DPI display reports virtualised
        /// 96-DPI coordinates, so this rect and Unity's own <see cref="UnityRectToPhysical"/> output would
        /// be expressed in two different pixel scales.
        /// </summary>
        public static bool TryGetWindowRect(IntPtr hwnd, out int x, out int y, out int width, out int height,
                                            out string error)
        {
            x = 0; y = 0; width = 0; height = 0;
            error = null;

            if (hwnd == IntPtr.Zero)
            {
                error = "window handle is null.";
                return false;
            }
            if (!IsWindow(hwnd))
            {
                error = $"handle 0x{hwnd.ToInt64():X} is not a window (closed since it was enumerated?)";
                return false;
            }
            if (!GetWindowRect(hwnd, out RECT r))
            {
                error = $"GetWindowRect failed (win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            x = r.left;
            y = r.top;
            width = r.right - r.left;
            height = r.bottom - r.top;
            if (width <= 0 || height <= 0)
            {
                error = $"window rect is empty ({width}x{height}) — the window has no drawable area.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Screen rect of the Unity main window, in physical pixels (see <see cref="TryGetWindowRect"/>).
        ///
        /// Needed because a DOCKED EditorWindow has no HWND of its own — it is drawn inside the main
        /// window. To capture one focus-free you PrintWindow the main window and crop, and the crop offset
        /// is (the EditorWindow's physical screen rect) minus (this origin). EditorWindow.position is in
        /// desktop coordinates, so without this subtraction the crop would be taken at desktop coordinates
        /// inside a window-local bitmap and land on an arbitrary part of the editor.
        /// </summary>
        public static bool TryGetUnityMainWindowRect(out int x, out int y, out int width, out int height,
                                                     out string error)
        {
            x = 0; y = 0; width = 0; height = 0;
            IntPtr hwnd = ResolveProcessMainWindow(out error);
            if (hwnd == IntPtr.Zero) return false;
            return TryGetWindowRect(hwnd, out x, out y, out width, out height, out error);
        }

        // Caption text. Empty is a legitimate answer (many real windows have no caption), so callers should
        // render it as "(untitled)" rather than treating it as a lookup failure.
        private static string GetWindowTitle(IntPtr hwnd)
        {
            int len = GetWindowTextLength(hwnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 1);
            int copied = GetWindowText(hwnd, sb, sb.Capacity);
            return copied > 0 ? sb.ToString() : string.Empty;
        }

        // Every window has a registered class, so an empty result here means the query failed, not that the
        // class is blank — report it as "unknown" instead of an empty string that reads like a real value.
        private static string GetWindowClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            int copied = GetClassName(hwnd, sb, sb.Capacity);
            return copied > 0 ? sb.ToString() : "unknown";
        }

        // Process.GetProcessById throws for a pid that exited between EnumWindows and this lookup, and
        // access to a protected process can be denied outright. Neither is worth aborting an enumeration
        // for, so the name degrades to "unknown" — never to an empty string, which would read as a
        // successfully resolved nameless process.
        private static string ResolveProcessName(uint pid, Dictionary<uint, string> cache)
        {
            if (pid == 0) return "unknown";
            if (cache != null && cache.TryGetValue(pid, out string cached)) return cached;

            string name = "unknown";
            try
            {
                using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    if (!string.IsNullOrEmpty(proc.ProcessName)) name = proc.ProcessName;
                }
            }
            catch
            {
                // Exited, or a protected process we may not open. "unknown" is the honest answer.
            }

            if (cache != null) cache[pid] = name;
            return name;
        }

        // ─── Top-level window descriptor ───
        public struct WindowDescriptor
        {
            public IntPtr Hwnd;
            /// <summary>Caption text. Empty when the window has no caption — not an error.</summary>
            public string Title;
            /// <summary>Win32 class name, or "unknown" if it could not be read.</summary>
            public string ClassName;
            /// <summary>Owning process name without extension, or "unknown" if it could not be resolved.</summary>
            public string ProcessName;
            public int ProcessId;
            public int X;
            public int Y;
            /// <summary>0 together with <see cref="Height"/> means GetWindowRect failed for this window.</summary>
            public int Width;
            public int Height;
            public bool IsVisible;
            public bool IsMinimized;
            /// <summary>True when the window belongs to this Unity process.</summary>
            public bool IsUnity;
        }

        private static WindowDescriptor DescribeWindow(IntPtr hwnd, Dictionary<uint, string> processNameCache,
                                                       uint ownProcessId)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);

            int x = 0, y = 0, w = 0, h = 0;
            if (GetWindowRect(hwnd, out RECT r))
            {
                x = r.left;
                y = r.top;
                w = r.right - r.left;
                h = r.bottom - r.top;
            }

            return new WindowDescriptor
            {
                Hwnd = hwnd,
                Title = GetWindowTitle(hwnd),
                ClassName = GetWindowClass(hwnd),
                ProcessName = ResolveProcessName(pid, processNameCache),
                ProcessId = (int)pid,
                X = x,
                Y = y,
                Width = w,
                Height = h,
                IsVisible = IsWindowVisible(hwnd),
                IsMinimized = IsIconic(hwnd),
                IsUnity = pid != 0 && pid == ownProcessId,
            };
        }

        /// <summary>
        /// Describes one window given its handle — the single-target counterpart of
        /// <see cref="EnumerateTopLevelWindows"/>, for when a caller already has an HWND (from a previous
        /// enumeration, or typed in by the user) and needs to report what it actually captured.
        /// Returns false when the handle is no longer a live window, which is the common case for a handle
        /// carried over from an earlier tool call.
        /// </summary>
        public static bool TryDescribeWindow(IntPtr hwnd, out WindowDescriptor descriptor, out string error)
        {
            descriptor = default(WindowDescriptor);
            error = null;

            if (hwnd == IntPtr.Zero)
            {
                error = "window handle is null.";
                return false;
            }
            if (!IsWindow(hwnd))
            {
                error = $"handle 0x{hwnd.ToInt64():X} is not a window (already closed?)";
                return false;
            }

            try
            {
                descriptor = DescribeWindow(hwnd, null, GetCurrentProcessId());
                return true;
            }
            catch (Exception ex)
            {
                error = $"could not describe window 0x{hwnd.ToInt64():X}: {ex.Message}";
                return false;
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        /// <summary>
        /// Enumerates TOP-LEVEL OS windows (EnumWindows order, i.e. front-to-back in z-order) so tools can
        /// target anything on the desktop — the VRChat client, Blender, a browser, a dropdown or context
        /// menu — not just Unity's own EditorWindow list.
        ///
        /// <paramref name="includeInvisible"/> = false (the normal case) keeps only windows that are
        /// visible AND have a caption. The desktop carries hundreds of captionless top-level windows
        /// (IME hosts, DWM cloaked shells, tray helpers, message-only windows); listing them buries the
        /// handful the caller is looking for and none of them can be usefully captured. Pass true only when
        /// diagnosing why an expected window is missing.
        ///
        /// Call inside a <see cref="DpiScope"/>: the X/Y/Width/Height reported here are physical pixels
        /// only under per-monitor DPI awareness. Outside a scope, a window on a 150%-scaled monitor reports
        /// virtualised coordinates that will not line up with <see cref="CaptureScreenRect"/> or with the
        /// bitmap <see cref="TryPrintWindow"/> produces.
        ///
        /// Never throws: a window that dies mid-enumeration is skipped rather than failing the whole list
        /// (letting an exception escape into EnumWindows' native frame is undefined behaviour).
        /// </summary>
        public static List<WindowDescriptor> EnumerateTopLevelWindows(bool includeInvisible)
        {
            var result = new List<WindowDescriptor>();
            var processNameCache = new Dictionary<uint, string>();
            uint ownProcessId = GetCurrentProcessId();

            // Held in a local so the delegate cannot be collected while native code is calling it back.
            EnumWindowsProc callback = (hwnd, lparam) =>
            {
                try
                {
                    if (!includeInvisible)
                    {
                        if (!IsWindowVisible(hwnd)) return true;
                        if (GetWindowTextLength(hwnd) <= 0) return true;
                    }
                    result.Add(DescribeWindow(hwnd, processNameCache, ownProcessId));
                }
                catch
                {
                    // Drop this one window and keep enumerating — see the "never throws" note above.
                }
                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        // ─── Focus-free window content capture (PrintWindow) ───
        // PW_RENDERFULLCONTENT (Win8.1+) makes DWM render the window's composited surface into the DC,
        // which is what makes GPU-drawn windows (Unity itself, browsers, VRChat) come out as anything
        // other than black. Undocumented but universally used; the flag is simply ignored on older Windows.
        private const uint PW_RENDERFULLCONTENT = 2;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Captures a window's own content with PrintWindow(PW_RENDERFULLCONTENT) — the focus-free route.
        ///
        /// Why it exists: <see cref="CaptureScreenRect"/> photographs the SCREEN, so it only shows the
        /// target if that window happens to be on top, which means raising Unity (or another app) over
        /// whatever the user is doing. PrintWindow asks the window to draw itself instead, so an occluded
        /// — even partly off-screen — window comes back intact and the user's focus is left alone.
        ///
        /// FRAME OF REFERENCE: the bitmap is the FULL window as reported by GetWindowRect (frame and
        /// caption included), NOT the client area, and its top-left pixel corresponds to that rect's
        /// top-left corner. A caller cutting a sub-rectangle out of this image (a docked EditorWindow, a
        /// 'region' argument) must therefore convert with
        ///     relX = physicalScreenX - windowRectX,   relY = physicalScreenY - windowRectY
        /// taking the origin from <see cref="TryGetWindowRect"/> for the same hwnd. Two consequences:
        /// Windows 10+ counts an invisible resize border into GetWindowRect, so the visible content starts
        /// a few pixels in (the subtraction above stays exact — only the framing looks padded); and mixing
        /// in a client-area origin instead shifts every crop by the frame thickness, which still returns a
        /// plausible-looking image of the wrong area.
        ///
        /// OUTPUT: BGRA32 with BOTTOM-UP rows — byte-for-byte the same shape <see cref="CaptureScreenRect"/>
        /// returns, so either route can be handed to the same CaptureCommon.FinishFromBgra. The ALPHA byte
        /// of a GDI 32bpp DIB is UNDEFINED: PrintWindow does not fill it meaningfully. Read as
        /// transparency it yields a fully transparent PNG that reads as a successful capture of nothing, so
        /// force alpha to 255 (CaptureCommon.FinishFromBgra already does) or drop the channel.
        ///
        /// FAILURE IS RETURNED, NEVER THROWN: every path returns false with <paramref name="error"/> filled
        /// so the caller can fall back to the BitBlt route and state in its result which one it used.
        /// PW_RENDERFULLCONTENT usually works for DWM/Direct3D windows but is not guaranteed — some
        /// renderers hand back an all-black or all-transparent surface while still returning success. A
        /// bitmap whose pixels are all one colour is therefore reported as a failure here instead of being
        /// passed on as a black image that looks like a working capture of a dark window.
        ///
        /// Call inside a <see cref="DpiScope"/>, otherwise GetWindowRect returns virtualised 96-DPI
        /// coordinates on a scaled monitor and the bitmap is allocated at the wrong size.
        /// </summary>
        public static bool TryPrintWindow(IntPtr hwnd, out byte[] bgra, out int w, out int h, out string error)
        {
            bgra = null;
            w = 0;
            h = 0;
            error = null;

            if (hwnd == IntPtr.Zero)
            {
                error = "window handle is null.";
                return false;
            }
            if (!IsWindow(hwnd))
            {
                error = $"handle 0x{hwnd.ToInt64():X} is not a window (already closed?)";
                return false;
            }
            if (IsIconic(hwnd))
            {
                error = "window is minimized — PrintWindow has no composited surface to copy while a " +
                        "window is iconic. Restore the window first.";
                return false;
            }
            if (!IsWindowVisible(hwnd))
            {
                error = "window is hidden (IsWindowVisible=false) — PrintWindow would return a blank " +
                        "bitmap. Note that a screen-rect capture of the same coordinates would photograph " +
                        "whatever is actually there, i.e. another application.";
                return false;
            }
            // Only the size is needed here; the origin matters to the CALLER (see the frame-of-reference
            // note above), which reads it from TryGetWindowRect for the same hwnd.
            if (!TryGetWindowRect(hwnd, out _, out _, out int width, out int height, out error))
                return false;

            long byteCount = (long)width * height * 4;
            if (byteCount > int.MaxValue)
            {
                error = $"window is too large to capture ({width}x{height} would need {byteCount} bytes).";
                return false;
            }

            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hbm = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    error = "GetDC(NULL) returned NULL.";
                    return false;
                }

                memDc = CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                {
                    error = "CreateCompatibleDC failed.";
                    return false;
                }

                hbm = CreateCompatibleBitmap(screenDc, width, height);
                if (hbm == IntPtr.Zero)
                {
                    error = $"CreateCompatibleBitmap({width}x{height}) failed.";
                    return false;
                }

                oldBmp = SelectObject(memDc, hbm);

                if (!PrintWindow(hwnd, memDc, PW_RENDERFULLCONTENT))
                {
                    error = $"PrintWindow(PW_RENDERFULLCONTENT) failed (win32 error {Marshal.GetLastWin32Error()}).";
                    return false;
                }

                byte[] pixels = new byte[(int)byteCount];
                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // negative = top-down DIB (rows are flipped below for Unity)
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = BI_RGB,
                    }
                };
                int scanned = GetDIBits(memDc, hbm, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);
                if (scanned == 0)
                {
                    error = "GetDIBits returned 0 — the window bitmap could not be read back.";
                    return false;
                }

                if (IsUniformColor(pixels, width * height))
                {
                    error = $"PrintWindow returned a uniform {width}x{height} bitmap " +
                            $"(every pixel B={pixels[0]} G={pixels[1]} R={pixels[2]}), i.e. no content was " +
                            "drawn. PW_RENDERFULLCONTENT is undocumented and some renderers decline it " +
                            "while still reporting success; fall back to a screen-rect capture.";
                    return false;
                }

                FlipRowsVertically(pixels, width, height);
                bgra = pixels;
                w = width;
                h = height;
                return true;
            }
            catch (Exception ex)
            {
                error = $"PrintWindow capture failed: {ex.Message}";
                return false;
            }
            finally
            {
                if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBmp);
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>
        /// True when every pixel carries the same colour, i.e. the capture is empty (all black, all white,
        /// all cleared). Compares B, G and R only: the DIB's alpha byte is undefined, and alpha noise over
        /// a uniformly black image would otherwise make an empty capture look like real content.
        ///
        /// Two stages, because this runs on every capture and a 4K window is 8.3M pixels: a sparse probe of
        /// roughly 4096 samples decides the normal case (any real screenshot differs within a few samples),
        /// and the exhaustive scan only runs when the probe found nothing — so the full walk is paid for
        /// exactly when the answer is about to be "this capture failed".
        /// </summary>
        private static bool IsUniformColor(byte[] pixels, int pixelCount)
        {
            if (pixels == null || pixelCount <= 1) return true;

            byte b0 = pixels[0], g0 = pixels[1], r0 = pixels[2];

            const int probeCount = 4096;
            int step = Math.Max(1, pixelCount / probeCount);
            for (int i = step; i < pixelCount; i += step)
            {
                int o = i * 4;
                if (pixels[o] != b0 || pixels[o + 1] != g0 || pixels[o + 2] != r0) return false;
            }
            // step==1 means the probe already visited every pixel; no confirmation pass needed.
            if (step == 1) return true;

            for (int i = 1; i < pixelCount; i++)
            {
                int o = i * 4;
                if (pixels[o] != b0 || pixels[o + 1] != g0 || pixels[o + 2] != r0) return false;
            }
            return true;
        }

        // ─── Capture an arbitrary screen rect ───
        // Returns BGRA32 top-down byte[width*height*4]. Throws on failure.
        // includeLayeredWindows=true uses CAPTUREBLT (needed for some overlay/transparent windows on full-screen capture; may include cursor).
        public static byte[] CaptureScreenRect(int x, int y, int width, int height, bool includeLayeredWindows)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"Invalid capture rect: {width}x{height}");

            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hbm = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) throw new InvalidOperationException("GetDC(NULL) returned NULL.");

                memDc = CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC failed.");

                hbm = CreateCompatibleBitmap(screenDc, width, height);
                if (hbm == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleBitmap failed.");

                oldBmp = SelectObject(memDc, hbm);

                uint rop = SRCCOPY | (includeLayeredWindows ? CAPTUREBLT : 0u);
                if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, rop))
                    throw new InvalidOperationException("BitBlt failed.");

                byte[] pixels = new byte[width * height * 4];
                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // negative = top-down DIB (we flip rows below for Unity)
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = BI_RGB,
                    }
                };
                int scanned = GetDIBits(memDc, hbm, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);
                if (scanned == 0) throw new InvalidOperationException("GetDIBits returned 0.");

                // Unity's Texture2D expects bottom-up row order (graphics API convention),
                // but our DIB is top-down. Flip rows in place.
                FlipRowsVertically(pixels, width, height);
                return pixels;
            }
            finally
            {
                if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBmp);
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // Both capture routes (BitBlt and PrintWindow) read a top-down DIB and must hand back the same
        // bottom-up order. If only one of them flips, the caller attaches an upside-down image and still
        // reports success — so the flip lives here once instead of being copied per route.
        private static void FlipRowsVertically(byte[] pixels, int width, int height)
        {
            int stride = width * 4;
            byte[] tmp = new byte[stride];
            for (int row = 0; row < height / 2; row++)
            {
                int top = row * stride;
                int bot = (height - 1 - row) * stride;
                System.Buffer.BlockCopy(pixels, top, tmp, 0, stride);
                System.Buffer.BlockCopy(pixels, bot, pixels, top, stride);
                System.Buffer.BlockCopy(tmp, 0, pixels, bot, stride);
            }
        }
    }
}
#endif
