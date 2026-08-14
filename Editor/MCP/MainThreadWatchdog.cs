using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Detects a stalled Unity main thread so MCP callers get a diagnosis instead of a timeout.
    ///
    /// Why: tool execution is queued onto <c>EditorApplication.update</c> (see
    /// <see cref="AgentMCPServer.PumpMainThread"/>). A modal dialog — <c>EditorUtility.DisplayDialog</c>,
    /// a script-reload prompt, an importer popup — stops <c>update</c> from running at all, so every
    /// queued call dies of timeout with no hint about the cause. Agents then burn round trips
    /// guessing, and parallel agents all stall together.
    ///
    /// The listener thread calls <see cref="TryDescribeStall"/> before enqueuing. It never touches
    /// Unity APIs (those are main-thread only) — only Win32 and plain fields.
    /// </summary>
    internal static class MainThreadWatchdog
    {
        /// <summary>
        /// How long the main thread may go without pumping before an incoming call is rejected
        /// with a diagnosis instead of being queued behind whatever is blocking it. Generous
        /// enough that an ordinary slow frame (asset import, inspector rebuild) never trips it.
        /// </summary>
        public const int DefaultStallThresholdMs = 5_000;

        /// <summary>
        /// Environment.TickCount is a 32-bit ms counter, so it is safe to read/write as volatile
        /// on every platform (a volatile long would tear on 32-bit). It wraps every ~49.7 days;
        /// unchecked subtraction still yields the correct delta across the wrap.
        /// </summary>
        static volatile int _lastPumpTick;

        static volatile bool _seeded;

        /// <summary>
        /// Name of the tool currently executing on the main thread, or null when idle.
        /// Distinguishes "a legitimately slow tool is running" from "the editor is wedged".
        /// </summary>
        static volatile string _inFlightTool;

        /// <summary>
        /// Editor state as of the last pump. Unity's own flags are main-thread only, so when the
        /// main thread is wedged — precisely when a caller most needs to know what is going on —
        /// they cannot be read at all. A snapshot plus <see cref="StalledMilliseconds"/> answers
        /// both "what was Unity doing" and "how long ago was that true", which together are more
        /// informative than a live read would have been.
        /// </summary>
        static volatile bool _compiling;
        static volatile bool _importing;
        static volatile bool _playing;
        static volatile bool _paused;

        /// <summary>
        /// Sampled on the main thread and cached, rather than read on demand: EditorPrefs is a
        /// Unity API, and the whole point of the off-thread reader is to work when Unity APIs
        /// cannot be called at all.
        /// </summary>
        static volatile string _autoRefresh;

        static IntPtr _mainWindow = IntPtr.Zero;

        /// <summary>
        /// Whether the main-window lookup has been attempted. Without this, a session with no main
        /// window (batchmode, minimized to tray, the moments before the window exists) would retry
        /// on every single update tick — allocating an undisposed Process and running a full
        /// top-level window enumeration ~100 times a second for the life of the editor.
        /// </summary>
        static volatile bool _mainWindowResolved;

        /// <summary>Called from the main thread at the top of every pump tick.</summary>
        public static void NotePump()
        {
            _lastPumpTick = Environment.TickCount;
            _seeded = true;

#if UNITY_EDITOR_WIN
            // Cache the main window handle while the editor is demonstrably responsive.
            // Resolving it later (during a stall) could pick up the modal popup instead.
            if (!_mainWindowResolved)
            {
                _mainWindowResolved = true;
                try
                {
                    using (var process = Process.GetCurrentProcess())
                        _mainWindow = process.MainWindowHandle;
                }
                catch
                {
                    _mainWindow = IntPtr.Zero;
                }
            }
#endif
        }

        /// <summary>Snapshots Unity's state from the main thread so other threads can read it.</summary>
        public static void NoteEditorState(bool compiling, bool importing, bool playing, bool paused)
        {
            _compiling = compiling;
            _importing = importing;
            _playing = playing;
            _paused = paused;
        }

        /// <summary>
        /// Reads the last snapshot. Safe from any thread; every field is volatile and read
        /// independently, so the set can straddle two pumps. That is acceptable here — these are
        /// diagnostics, and the pump interval is far shorter than any state they describe.
        /// </summary>
        public static void ReadSnapshot(out bool compiling, out bool importing, out bool playing,
                                        out bool paused, out string inFlightTool, out bool everPumped,
                                        out string autoRefresh)
        {
            compiling = _compiling;
            importing = _importing;
            playing = _playing;
            paused = _paused;
            inFlightTool = _inFlightTool;
            everPumped = _seeded;
            autoRefresh = _autoRefresh;
        }

        /// <summary>Caches the Auto Refresh setting sampled on the main thread.</summary>
        public static void NoteAutoRefresh(string description) => _autoRefresh = description;

        /// <summary>
        /// Live OS-level check for a modal window, independent of the stall threshold.
        /// Accurate even while the editor is frozen, because it asks Windows, not Unity.
        /// </summary>
        public static bool TryGetModalWindow(out string description, out string title)
            => TryDescribeModalWindow(out description, out title);

        public static void NoteToolStart(string toolName) => _inFlightTool = toolName;

        public static void NoteToolFinish() => _inFlightTool = null;

        /// <summary>Clears state on server start so a previous session's timestamp is not reused.</summary>
        public static void Reset()
        {
            _seeded = false;
            _inFlightTool = null;
            _mainWindow = IntPtr.Zero;
            _mainWindowResolved = false;
            _compiling = _importing = _playing = _paused = false;
            _autoRefresh = null;
        }

        /// <summary>Milliseconds since the main thread last pumped. 0 before the first tick.</summary>
        public static int StalledMilliseconds()
        {
            if (!_seeded) return 0;
            int delta = unchecked(Environment.TickCount - _lastPumpTick);
            return delta < 0 ? 0 : delta;
        }

        /// <summary>
        /// Decides whether an incoming call should be rejected immediately with a diagnosis.
        ///
        /// Rejects ONLY when a modal window is up. That is the case where waiting is futile: the
        /// pump cannot run again until a human clicks something, so every queued call is doomed
        /// and the caller deserves to hear why now rather than after a 120s timeout.
        ///
        /// Everything else — a slow tool, an asset import, a domain reload — resolves on its own,
        /// so the call is queued as usual. Rejecting those would turn a wait into a retry loop and
        /// break legitimately long operations (bakes, variant compiles, large imports routinely
        /// hold the main thread for far more than the threshold).
        /// </summary>
        public static bool TryDescribeStall(int thresholdMs, out string message)
        {
            message = null;
            if (thresholdMs <= 0) return false;

            int stalledMs = StalledMilliseconds();
            if (stalledMs < thresholdMs) return false;

            if (!TryDescribeModalWindow(out string modalDesc, out string modalTitle)) return false;

            string inFlight = _inFlightTool;
            var sb = new StringBuilder();
            sb.Append("Unity main thread has been blocked for ");
            sb.Append((stalledMs / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("s by a modal window.");
            sb.Append("  Window: ").AppendLine(modalDesc);
            if (inFlight != null)
                sb.Append("  Raised by tool: ").AppendLine(inFlight);

            if (LooksLikeProgressWindow(modalTitle))
            {
                sb.Append("  This looks like a PROGRESS bar, not a question — Unity is busy (import / compile / bake). ");
                sb.Append("Wait a few seconds and retry; no human action is needed.");
            }
            else
            {
                sb.Append("  This looks like a DIALOG awaiting an answer. A human must dismiss it in the Unity window. ");
                sb.Append("No tool call can run until then — every queued call is blocked, not just this one.");
            }

            message = sb.ToString();
            return true;
        }

        /// <summary>
        /// Unity shows progress with EditorUtility.DisplayProgressBar, which also disables the main
        /// window and is therefore indistinguishable from a question dialog at the Win32 level.
        /// Titles are caller-supplied so this can only be a hint — but "retry shortly" versus
        /// "a human must click something" is exactly the distinction the caller needs, so it is
        /// worth guessing and saying which guess was made.
        /// </summary>
        static bool LooksLikeProgressWindow(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            return t.Contains("hold on")
                || t.Contains("importing")
                || t.Contains("compiling")
                || t.Contains("loading")
                || t.Contains("applying")
                || t.Contains("building")
                || t.Contains("baking")
                || t.Contains("progress");
        }

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

        /// <summary>
        /// A Windows modal dialog disables its owner. That is a far more reliable signal than
        /// matching window class names, because Unity draws its dialogs itself rather than using
        /// the OS "#32770" dialog class.
        /// </summary>
        static bool TryDescribeModalWindow(out string description, out string title)
        {
            description = null;
            title = null;
            IntPtr main = _mainWindow;
            if (main == IntPtr.Zero) return false;

            try
            {
                if (IsWindowEnabled(main)) return false;

                IntPtr popup = GetLastActivePopup(main);
                if (popup == IntPtr.Zero || popup == main || !IsWindowVisible(popup))
                {
                    description = "(unidentified modal window)";
                    return true;
                }

                var titleBuf = new StringBuilder(256);
                GetWindowText(popup, titleBuf, titleBuf.Capacity);
                var cls = new StringBuilder(128);
                GetClassName(popup, cls, cls.Capacity);

                title = titleBuf.ToString();
                description = string.IsNullOrEmpty(title)
                    ? $"(untitled) [{cls}]"
                    : $"\"{title}\" [{cls}]";
                return true;
            }
            catch
            {
                // P/Invoke failure must never break request handling — fall back to "not modal".
                return false;
            }
        }
#else
        static bool TryDescribeModalWindow(out string description, out string title)
        {
            description = null;
            title = null;
            return false;
        }
#endif
    }
}
