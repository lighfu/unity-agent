#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    // Windows-only capture of EditorWindows, arbitrary OS windows and whole monitors.
    //
    // Two window-route methods exist, and every result states which one produced the image:
    //   PrintWindow(PW_RENDERFULLCONTENT) — asks the window to draw itself. The user's focus is left
    //     alone and applications lying on top of the target do not appear in the picture. Default.
    //   Screen-rect BitBlt — photographs the desktop at the target's coordinates. It shows exactly what
    //     is on screen, which is also its weakness: the target has to be on top, which is why the legacy
    //     path raised Unity to the foreground and stole the user's focus.
    //
    // A DOCKED EditorWindow has no HWND of its own — the entire Unity dock is one OS window — so it is
    // captured by PrintWindow-ing the Unity main window and cutting the tab's rect out of that bitmap.
    // A floating one owns a top-level window and is captured directly. Which of the two happened is
    // always reported, because the failure modes differ.
    //
    // Pipeline: BGRA32 bottom-up byte[] → CaptureCommon.FinishFromBgra (crop → downscale → encode →
    // attach → numbered %TEMP% dump).
    public static class WindowCaptureTools
    {
        // Reflection into EditorWindow/HostView internals. Instance members, any visibility: the members
        // we need (m_Parent, actualView, m_Panes, docked) are internal, and Unity has moved them between
        // field and property before, so both are probed and a miss degrades to "unknown".
        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [AgentTool(@"List every loaded EditorWindow together with the facts needed to capture it correctly:
title, type, screen rect, docked or floating, whether it is the FRONT tab of its dock, whether it is
actually visible, whether Unity's keyboard focus is on it, its OS window handle, and the names of the
tabs sharing its dock.

Call this before CaptureEditorWindow — not only to discover titles, but to check activeTab. A background
tab of a dock is not drawn at all, so NO capture route can photograph it; CaptureEditorWindow with the
default focusless=true refuses such a target instead of returning a picture of the tab that is in front.

Per-window fields:
  docked     yes = it is a tab in a dock. Read it together with hwnd: hwnd=…(unity main) means the pixels
             are inside the Unity main window and capturing the tab means cropping that bitmap, while a
             different hwnd means the dock itself is a floating container ('yes(into a floating
             container)'). no = an undocked floating window. 'inferred-from-geometry' means Unity's
             internal flag was unreadable and the answer comes from which OS window the rect matches.
  activeTab  the frontmost tab of its dock. 'unknown' means the internal HostView reflection did not
             resolve on this Unity version — never a guess.
  visible    activeTab AND its OS window is present and not minimized. This is the field that explains
             why an otherwise valid capture would come back showing something else.
  focus      EditorWindow.focusedWindow, i.e. Unity's own keyboard focus. It says nothing about whether
             Unity is the foreground application at the OS level.
  hwnd       the OS window the pixels actually live in. Pass it to CaptureWindow to grab the whole
             container (tab strip, toolbar and all) instead of just this window's drawing area.
  tabs       every tab sharing the dock, '*' marking the active one. 'none' = alone in its host view.

pos/size are Unity's own logical points (EditorWindow.position), not physical pixels: on a 150%-scaled
display the captured image is 1.5x these numbers. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListEditorWindows()
        {
            var windows = EnumerateValidEditorWindows();
            if (windows.Count == 0) return "No EditorWindow instances found.";

            using (new WindowCaptureNative.DpiScope())
            {
                // One enumeration for the whole listing: resolving each window's container separately
                // would run EnumWindows once per row, and the desktop can change between passes so the
                // rows would not even be consistent with each other.
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var unityWindows = EnumerateUnityTopLevelWindows();
                IntPtr mainHwnd = WindowCaptureNative.GetUnityMainWindow(out string mainError);

                var sb = new StringBuilder();
                sb.AppendLine($"EditorWindows: {windows.Count} found");
                if (mainHwnd == IntPtr.Zero)
                    sb.AppendLine($"NOTE: the Unity main window handle is unavailable ({mainError}) — " +
                                  "docked/visible/hwnd cannot be resolved for docked windows.");
                sb.AppendLine("---");

                var focused = EditorWindow.focusedWindow;
                for (int i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    string typeName = w.GetType().Name;
                    string title = TitleOf(w);
                    Rect p = w.position;

                    var dock = InspectDockState(w);
                    var (px, py, pw, ph) = WindowCaptureNative.UnityRectToPhysical(p.x, p.y, p.width, p.height, monitors);
                    var container = ResolveContainerWindow(new RectInt(px, py, pw, ph),
                        ContainerRectHint(w, monitors, out _), unityWindows, mainHwnd);

                    sb.AppendLine($"[{i}] [{typeName}] \"{title}\" pos=({p.x:F0},{p.y:F0}) size=({p.width:F0}x{p.height:F0})" +
                                  $" docked={DescribeDocked(dock, container)}" +
                                  $" activeTab={Tri(dock.IsActiveTab)}" +
                                  $" visible={DescribeVisible(dock, container)}" +
                                  $" focus={(ReferenceEquals(focused, w) ? "yes" : "no")}" +
                                  $" hwnd={DescribeHwnd(container)}" +
                                  $" tabs={dock.TabsDisplay}");
                }
                return sb.ToString().TrimEnd();
            }
        }

        [AgentTool("List all physical display monitors with device name, primary flag, virtual-screen position, resolution, and per-monitor DPI/scale (for screenshot scaling decisions). " +
            "Call before CaptureMonitor to choose monitorId. " +
            "Also reports Unity's EditorGUIUtility.pixelsPerPoint for diagnostics. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListMonitors()
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var monitors = WindowCaptureNative.EnumerateMonitors();
                if (monitors.Count == 0) return "No monitors detected.";

                var sb = new StringBuilder();
                sb.AppendLine($"Monitors: {monitors.Count} found");
                sb.AppendLine("---");
                for (int i = 0; i < monitors.Count; i++)
                {
                    var m = monitors[i];
                    string primary = m.IsPrimary ? " (Primary)" : "";
                    float scale = m.DpiX / 96f;
                    sb.AppendLine($"[{i}] {m.DeviceName}{primary} pos=({m.X},{m.Y}) size=({m.Width}x{m.Height}) DPI={m.DpiX} scale={scale:F2}x");
                }
                sb.AppendLine($"Unity EditorGUIUtility.pixelsPerPoint = {EditorGUIUtility.pixelsPerPoint:F2}");
                return sb.ToString().TrimEnd();
            }
        }

        [AgentTool(@"Screenshot an EditorWindow (Inspector, Console, a settings panel, a custom tool window)
whose title contains the given substring.

focusless=true (DEFAULT) captures with PrintWindow(PW_RENDERFULLCONTENT): the window draws itself into an
offscreen bitmap, so the user's focus is NOT stolen and applications lying on top of Unity do not appear
in the picture. Because a docked EditorWindow has no OS window of its own, this captures the Unity main
window and cuts out the tab's rect; a floating window is captured directly. The result always says which
of the two happened and which method produced the pixels.

WHAT focusless CANNOT DO — read this before trusting the image:
  * A BACKGROUND TAB of a dock is not drawn at all, so no bitmap of it exists anywhere. This tool refuses
    such a target instead of handing back a picture of the tab that IS in front. Use focusless=false,
    which activates the tab and then forces a synchronous repaint on the spot (waitForRepaint is not
    needed for that — a queued repaint would never run, because no editor event loop can execute while
    this call holds the main thread), or check activeTab in ListEditorWindows first. The result of such a
    capture always carries a note saying the tab was activated, and a WARNING if the synchronous repaint
    could not be performed, in which case the image is probably still the tab that was in front.
  * That refusal can only fire when activeTab is KNOWN. If Unity's internal HostView reflection does not
    resolve on this version, activeTab reads 'unknown' and a background tab passes through to PrintWindow,
    which then returns the front tab's pixels under this window's name. The result says so explicitly
    rather than hiding it — read the notes, and check activeTab in ListEditorWindows.
  * PrintWindow copies the window's LAST PAINTED content. IMGUI only repaints on events, so a window that
    has not been touched since the change you are verifying can come back stale. Pass waitForRepaint=true
    to force a synchronous repaint (HostView.RepaintImmediately) before the bitmap is taken; if that
    internal method is unavailable the result says the flag had no effect instead of silently ignoring it.
  * PW_RENDERFULLCONTENT is undocumented and some GPU-composited windows decline it. On failure this tool
    falls back to the legacy screen-rect BitBlt route — which DOES raise Unity unless bringToFront=false,
    i.e. focus is best-effort, not guaranteed. The result message names the route that was used.

bringToFront (default true) is DEPRECATED and is IGNORED while focusless=true succeeds — it applies only
to the screen-rect route, i.e. when focusless=false or when PrintWindow declined and the tool fell back.
There it raises Unity above other applications via Win32 before the BitBlt, because EditorWindow.Focus()
alone switches the tab inside Unity and does not raise Unity at the OS level — so without it the capture
photographs whatever app is really on top (a browser, a chat window). The method= field of the result says
whether the foreground was actually touched.

region='x,y,w,h' crops inside the area this tool captures — the EditorWindow's OWN rect, not the whole
Unity window, so x=0,y=0 is this window's top-left corner even when it was cut out of a main-window
bitmap. Origin TOP-LEFT with y growing DOWNWARD (window coordinates), which is deliberately the opposite
of cropRegion / DiffImages.maskRegion elsewhere
in this package (bottom-left origin, y upward). Mixing the two conventions crops the mirrored band of the
image and still reports success. The numbers are PHYSICAL pixels of the captured image, so on a
150%-scaled display they are 1.5x the logical values EditorWindow.position reports. Out-of-range
rectangles are an error, never a silent clamp.

matchIndex (0-based) picks among the windows whose TITLE MATCHES — it is not the index printed by
ListEditorWindows.
maxWidth>0: downscale (bilinear) so the LONGER side is at most maxWidth pixels, aspect preserved.
format='png' (lossless, default) or 'jpg' (much smaller for UI screenshots, jpgQuality 1-100, default 90).
saveToPath: optional absolute path to also write the encoded bytes to.
Recommended for token economy: maxWidth=1920, format='jpg', jpgQuality=85.

The captured area is EditorWindow.position — the window's own drawing area — so the dock's tab strip is
normally outside it. Use CaptureWindow with the hwnd from ListEditorWindows to grab the whole container.
The result includes a 'Debug copy at' path in %TEMP% that the Read tool can open directly.
Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string CaptureEditorWindow(
            string titleContains,
            int matchIndex = 0,
            bool waitForRepaint = false,
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "",
            bool bringToFront = true,
            bool focusless = true,
            string region = "")
        {
            if (string.IsNullOrEmpty(titleContains)) return "Error: titleContains is empty.";

            using (new WindowCaptureNative.DpiScope())
            {
                var all = EnumerateValidEditorWindows();
                var matches = all.Where(w => w.titleContent != null
                                          && !string.IsNullOrEmpty(w.titleContent.text)
                                          && w.titleContent.text.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                                 .ToList();

                if (matches.Count == 0)
                {
                    string available = string.Join(", ",
                        all.Select(w => $"\"{w.titleContent?.text}\"").Distinct().Take(20));
                    return $"Error: No EditorWindow whose title contains '{titleContains}'. Available: {available}";
                }
                if (matchIndex < 0 || matchIndex >= matches.Count)
                {
                    string titles = string.Join(", ",
                        matches.Select((w, i) => $"[{i}] \"{w.titleContent.text}\""));
                    return $"Error: matchIndex {matchIndex} out of range (matches={matches.Count}: {titles}).";
                }

                var window = matches[matchIndex];
                string title = TitleOf(window);

                // Validate the encoding options before doing anything with the user's screen: a bad
                // format would otherwise be reported only after a foreground raise had already stolen
                // focus for a capture that was never going to be returned.
                var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
                if (!opt.Validate(out string optError)) return $"Error: {optError}";

                var dock = InspectDockState(window);

                // A background tab has no pixels anywhere — PrintWindow would return the dock area
                // showing whichever tab IS in front, and that image, labelled with this window's name,
                // is the exact kind of plausible-looking wrong answer this package refuses to produce.
                if (focusless && dock.IsActiveTab == false)
                {
                    return $"Error: EditorWindow '{title}' is not the active tab of its dock " +
                           $"(tabs={dock.TabsDisplay}), so it is not being drawn and focus-free capture " +
                           "would photograph the tab that is in front instead. Call again with " +
                           "focusless=false (this activates the tab, forces a synchronous repaint of it and " +
                           "raises Unity, i.e. it takes the user's focus), or activate the tab yourself first.";
                }

                Rect posPt = window.position;
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var (sx, sy, sw, sh) = WindowCaptureNative.UnityRectToPhysical(posPt.x, posPt.y, posPt.width, posPt.height, monitors);
                if (sw <= 0 || sh <= 0)
                    return $"Error: Window '{title}' has zero-size rect (may be minimized/closed).";
                var subject = new RectInt(sx, sy, sw, sh);

                // focusless mode must not touch focus at all, so Focus() is skipped. RepaintImmediately
                // does not move focus (it paints the host view in place), so waitForRepaint stays
                // available in both modes — it is the only way to get fresh pixels out of an IMGUI
                // window that has had no events since the change being verified.
                if (!focusless)
                {
                    try { window.Focus(); window.Repaint(); }
                    catch { /* swallow — best-effort focus */ }
                }

                // Focus() and Repaint() only switch the tab and queue a paint; the paint itself happens on
                // the next editor event loop, which cannot run while this tool call is holding the main
                // thread. So when focusless=false was used to bring a BACKGROUND tab forward, the paint has
                // to be forced synchronously HERE — regardless of waitForRepaint — or the capture a few
                // lines below (a BitBlt taken 80 ms later, still on this same thread) photographs the tab
                // that was in front and returns it labelled with this window's name.
                // IsActiveTab == null is treated the same way: not knowing whether a tab switch happened is
                // not a reason to skip the one call that makes the switch visible.
                var tabNotes = new List<string>();
                bool forceRepaint = !focusless && dock.IsActiveTab != true;
                if (waitForRepaint || forceRepaint)
                {
                    bool repainted = TryRepaintImmediately(window, out string repaintError);
                    if (forceRepaint)
                    {
                        string what = dock.IsActiveTab == false
                            ? "this window was a BACKGROUND tab of its dock and was activated with " +
                              $"EditorWindow.Focus() (tabs={dock.TabsDisplay})"
                            : "whether this window was the front tab of its dock could not be determined, so " +
                              "EditorWindow.Focus() was called in case a tab switch was needed";
                        tabNotes.Add(repainted
                            ? what + ", then a synchronous repaint (HostView.RepaintImmediately) was forced so " +
                              "the pixels below are this window's — if the image still shows another tab, the " +
                              "switch did not complete and the capture is not the window you asked for"
                            : what + $", but the synchronous repaint could NOT be performed ({repaintError}), and " +
                              "no editor event loop can run while this call holds the main thread — WARNING: " +
                              "the image is therefore likely to be the tab that WAS in front, not this window");
                    }
                    else if (!repainted)
                    {
                        tabNotes.Add($"waitForRepaint=true had NO effect ({repaintError}), so the pixels are " +
                                     "whatever this window painted last and may predate the change being verified");
                    }
                }

                IntPtr mainHwnd = WindowCaptureNative.GetUnityMainWindow(out string mainError);
                var container = ResolveContainerWindow(subject,
                    ContainerRectHint(window, monitors, out string containerHintError),
                    EnumerateUnityTopLevelWindows(), mainHwnd);

                if (!container.Resolved)
                {
                    // No handle means PrintWindow is impossible, but the screen-rect route photographs
                    // coordinates rather than a window, so it still works. Say so instead of failing.
                    string why = container.Error;
                    if (string.IsNullOrEmpty(why)) why = mainError;
                    if (string.IsNullOrEmpty(why)) why = "reason unavailable";
                    return CaptureWindowContent(IntPtr.Zero, subject, subject, region,
                        focusless: false, targetCanBeRaised: true, allowRaise: bringToFront,
                        label: $"EditorWindow '{title}'",
                        context: JoinNotes(tabNotes,
                            $"the OS window owning this EditorWindow could not be resolved ({why}), " +
                            "so PrintWindow was unavailable"),
                        opt: opt);
                }

                if (container.IsMinimized)
                {
                    return $"Error: the OS window hosting EditorWindow '{title}' is minimized " +
                           $"({container.How}). PrintWindow has no composited surface while a window is " +
                           "iconic, and a screen-rect capture would photograph whatever is really at " +
                           "those coordinates, so neither route can return this window. Restore the " +
                           "Unity window and retry.";
                }

                // States which OS window was targeted, not which route read it — the method= field says that,
                // and the two can differ (PrintWindow may decline and hand over to the screen-rect route).
                string dockNote = container.IsUnityMainWindow
                    ? "docked: it has no HWND of its own, so the target was the Unity main window and this " +
                      "window's rect was cut out of that bitmap"
                    : $"floating: the target was its own OS window 0x{container.Hwnd.ToInt64():X}";
                // Unity's own docked flag as a cross-check on the geometric match. The two disagree
                // harmlessly in one direction and alarmingly in the other, so they are worded apart.
                if (dock.IsDocked == true && !container.IsUnityMainWindow)
                    dockNote += " (EditorWindow.docked reports true as well: a window tabbed into a FLOATING " +
                                "container counts as docked, which is consistent with this)";
                else if (dock.IsDocked == false && container.IsUnityMainWindow)
                    dockNote += " — WARNING: EditorWindow.docked reports FALSE, i.e. Unity considers this " +
                                "window floating, yet the pixels were taken from the Unity main window. Its " +
                                "own container is probably minimized or hidden, so the image may not be the " +
                                "window you asked for; restore the floating window and retry";
                // The background-tab guard above can only refuse what it knows about: with the HostView
                // reflection unresolved, IsActiveTab is null and a background tab passes straight through to
                // PrintWindow, which returns the dock area showing whichever tab IS in front — under this
                // window's name. Same note as UIElementTools.BuildAnnotatedImage, for the same reason.
                if (dock.IsActiveTab == null)
                    dockNote += "; whether this window is the front tab of its dock could not be determined " +
                                "(Unity's internal HostView reflection did not resolve on this version) — if " +
                                "the image shows a different window than the one asked for, that is why";
                // How the OS window was identified travels with the image: on the heuristic path the wrong
                // window could have been picked, and the reader has to be able to see that.
                if (!string.IsNullOrEmpty(container.How)) dockNote += $"; {container.How}";
                if (!string.IsNullOrEmpty(containerHintError))
                    dockNote += $" [ContainerWindow rect unavailable: {containerHintError}]";
                // No note about bringToFront here: whether the foreground was touched is visible in the
                // method= field of the result, and a note claiming it was ignored would contradict that
                // field on the fallback path, where it is honoured.

                return CaptureWindowContent(container.Hwnd, container.Rect, subject, region,
                    focusless, targetCanBeRaised: true, allowRaise: bringToFront,
                    label: $"EditorWindow '{title}'", context: JoinNotes(tabNotes, dockNote), opt: opt);
            }
        }

        [AgentTool(@"List top-level OS windows — any application, not just Unity: the VRChat client, Blender,
a browser, a dropdown or a context menu. Use it to find the hwnd to hand to CaptureWindow.

titleContains / processName: case-insensitive substring filters. processName is the executable name
without '.exe' (e.g. 'chrome', 'vrchat'), or 'unknown' for a process that could not be opened — a
protected or already-exited process degrades to 'unknown' rather than to a blank that would read like a
successfully resolved nameless process.
includeInvisible=false (default) lists only windows that are visible AND have a caption. The desktop
carries hundreds of captionless top-level windows (IME hosts, cloaked shells, tray and message-only
windows) that bury the handful you are looking for and cannot be usefully captured. Pass true only when
diagnosing why an expected window is missing.

Handles are printed as 0x-prefixed hex and can be pasted straight into CaptureWindow(hwnd='0x...').
They are valid only while the window lives — re-list rather than reusing a handle from an earlier session.

Menus and dropdowns are separate top-level windows and do appear here (Win32 menus use class '#32768'),
which is the only way to capture one. Note the catch: an open IMGUI popup inside Unity pumps its own
modal message loop on the main thread, so a tool call issued while it is open may not be serviced until
it closes.

Rects are physical pixels on the virtual desktop, so a window on a secondary monitor can have negative
coordinates. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListWindows(
            string titleContains = "",
            string processName = "",
            bool includeInvisible = false)
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var all = WindowCaptureNative.EnumerateTopLevelWindows(includeInvisible);
                if (all.Count == 0)
                {
                    return "Error: window enumeration returned nothing at all — not even the Unity window. " +
                           "EnumWindows itself failed; this is not 'no windows are open'.";
                }

                var filtered = all.Where(d =>
                        (string.IsNullOrWhiteSpace(titleContains) ||
                         (d.Title ?? string.Empty).IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0) &&
                        (string.IsNullOrWhiteSpace(processName) ||
                         (d.ProcessName ?? string.Empty).IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                if (filtered.Count == 0)
                {
                    var hint = new StringBuilder();
                    hint.Append("No top-level window matches");
                    if (!string.IsNullOrWhiteSpace(titleContains)) hint.Append($" titleContains='{titleContains}'");
                    if (!string.IsNullOrWhiteSpace(processName)) hint.Append($" processName='{processName}'");
                    hint.Append($" ({all.Count} windows enumerated");
                    hint.Append(includeInvisible ? ")." : ", visible-with-caption only — retry with includeInvisible=true).");
                    return hint.ToString();
                }

                // Cap the output rather than returning thousands of lines with includeInvisible=true, and
                // say how many were dropped so the list is never mistaken for the whole desktop.
                const int maxRows = 200;
                var rows = filtered.Take(maxRows).ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"Top-level windows: {rows.Count} shown of {filtered.Count} matching ({all.Count} enumerated), front-to-back in z-order");
                if (filtered.Count > rows.Count)
                    sb.AppendLine($"NOTE: {filtered.Count - rows.Count} further matches were omitted — narrow titleContains/processName.");
                sb.AppendLine("Pass hwnd straight to CaptureWindow, e.g. CaptureWindow(hwnd='0x00120A3C').");
                sb.AppendLine("---");
                for (int i = 0; i < rows.Count; i++)
                {
                    var d = rows[i];
                    var flags = new List<string>();
                    flags.Add(d.IsVisible ? "visible" : "hidden");
                    if (d.IsMinimized) flags.Add("MINIMIZED(cannot be captured)");
                    if (d.IsUnity) flags.Add("this-unity-process");
                    string title = string.IsNullOrEmpty(d.Title) ? "(untitled)" : d.Title;
                    string size = (d.Width > 0 && d.Height > 0) ? $"{d.Width}x{d.Height}" : "unavailable(GetWindowRect failed)";
                    sb.AppendLine($"[{i}] 0x{d.Hwnd.ToInt64():X8} \"{title}\" class={d.ClassName} " +
                                  $"process={d.ProcessName}(pid {d.ProcessId}) pos=({d.X},{d.Y}) size=({size}) " +
                                  string.Join(" ", flags));
                }
                return sb.ToString().TrimEnd();
            }
        }

        [AgentTool(@"Screenshot ANY top-level OS window — the VRChat client, Blender, a browser, a dropdown,
or Unity's own main window — by handle or by title substring. Call ListWindows first.

hwnd: the handle from ListWindows. Read as HEXADECIMAL with or without the 0x prefix, so '1234' means
0x1234, not decimal 1234. Handles die with their window; re-list instead of reusing an old one.
titleContains: case-insensitive substring of the caption, used when hwnd is not given.
matchIndex: -1 (default) requires the title to match EXACTLY ONE window; if several match, the tool
returns an error listing the candidates with their indices so you can pick one deliberately. Pass 0, 1, …
to select from that list. Silently capturing the first of six browser windows is worse than one extra
call.

focusless=true (DEFAULT) uses PrintWindow(PW_RENDERFULLCONTENT): the window renders itself offscreen, so
it comes back intact even when occluded or partly off-screen, and the user's focus is untouched. If the
window's renderer declines PW_RENDERFULLCONTENT the tool falls back to a screen-rect BitBlt and says so —
and that fallback shows whatever is drawn ON TOP of the target, because a window belonging to another
process cannot be raised from inside Unity (only a Unity-owned target can, and then only on the fallback
path). focusless=false skips straight to the screen-rect route.

A MINIMIZED window is rejected outright: an iconic window has no composited surface for PrintWindow, and
its rect is parked off-screen, so both routes would return something that is not the window. Restore it
first. A hidden window (IsWindowVisible=false) is rejected for the same reason.

region='x,y,w,h' crops inside the window. Origin is the window's TOP-LEFT with y growing DOWNWARD
(window coordinates) — deliberately the opposite of cropRegion / DiffImages.maskRegion, which are
bottom-left based. Numbers are physical pixels. Note that Windows 10+ counts an invisible resize border
into a window's rect, so x=0,y=0 sits a few pixels outside the visible edge. Out-of-range rectangles are
an error, never a silent clamp.

maxWidth>0 downscales so the LONGER side fits, aspect preserved. format='png' (default) or 'jpg' with
jpgQuality 1-100. saveToPath optionally writes the bytes to an absolute path. The result reports the
route, the method, the output and source resolution, and a 'Debug copy at' path in %TEMP%.
Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string CaptureWindow(
            string hwnd = "",
            string titleContains = "",
            int matchIndex = -1,
            bool focusless = true,
            string region = "",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "")
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
                if (!opt.Validate(out string optError)) return $"Error: {optError}";

                WindowCaptureNative.WindowDescriptor target;
                string selectionNote = null;

                if (!string.IsNullOrWhiteSpace(hwnd))
                {
                    if (!TryParseHwnd(hwnd, out IntPtr handle, out string hwndError)) return $"Error: {hwndError}";
                    if (!WindowCaptureNative.TryDescribeWindow(handle, out target, out string describeError))
                        return $"Error: {describeError} Call ListWindows for a current handle — handles are " +
                               "not stable once a window closes.";
                    if (!string.IsNullOrWhiteSpace(titleContains))
                        selectionNote = "titleContains was ignored because an explicit hwnd was given";
                }
                else if (!string.IsNullOrWhiteSpace(titleContains))
                {
                    var candidates = WindowCaptureNative.EnumerateTopLevelWindows(includeInvisible: false)
                        .Where(d => (d.Title ?? string.Empty).IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Where(d => d.Width > 0 && d.Height > 0)
                        .ToList();

                    if (candidates.Count == 0)
                        return $"Error: no visible top-level window's title contains '{titleContains}'. Call " +
                               "ListWindows (add includeInvisible=true if you expect a captionless window) to " +
                               "see what is actually open.";

                    if (candidates.Count > 1 && matchIndex < 0)
                        return $"Error: {candidates.Count} windows match '{titleContains}' — pass matchIndex to " +
                               "choose one deliberately:\n" + DescribeCandidates(candidates);

                    int index = matchIndex < 0 ? 0 : matchIndex;
                    if (index >= candidates.Count)
                        return $"Error: matchIndex {matchIndex} out of range ({candidates.Count} match " +
                               $"'{titleContains}'):\n" + DescribeCandidates(candidates);

                    target = candidates[index];
                    if (candidates.Count > 1)
                        selectionNote = $"matchIndex={index} of {candidates.Count} title matches";
                }
                else
                {
                    return "Error: pass either hwnd (hex, from ListWindows) or titleContains.";
                }

                string title = string.IsNullOrEmpty(target.Title) ? "(untitled)" : target.Title;
                string id = $"0x{target.Hwnd.ToInt64():X8}";

                if (target.IsMinimized)
                    return $"Error: window \"{title}\" ({id}) is MINIMIZED. PrintWindow has no composited " +
                           "surface while a window is iconic, and an iconic window's rect is parked " +
                           "off-screen, so a screen-rect capture would photograph something else entirely. " +
                           "Restore the window and retry — returning a black image here would look like a " +
                           "successful capture of a dark window.";
                if (!target.IsVisible)
                    return $"Error: window \"{title}\" ({id}) is hidden (IsWindowVisible=false). PrintWindow " +
                           "would return a blank bitmap and a screen-rect capture of its coordinates would " +
                           "photograph whatever application is actually there.";
                if (target.Width <= 0 || target.Height <= 0)
                    return $"Error: the rect of window \"{title}\" ({id}) is unavailable or empty " +
                           $"({target.Width}x{target.Height}) — nothing to capture.";

                var rect = new RectInt(target.X, target.Y, target.Width, target.Height);
                var context = new List<string>();
                if (selectionNote != null) context.Add(selectionNote);
                context.Add(target.IsUnity
                    ? "target belongs to this Unity process"
                    : $"target belongs to {target.ProcessName} (pid {target.ProcessId}), another process");

                return CaptureWindowContent(target.Hwnd, rect, null, region,
                    focusless, targetCanBeRaised: target.IsUnity, allowRaise: true,
                    label: $"window \"{title}\" ({id}, {target.ProcessName})",
                    context: string.Join("; ", context), opt: opt);
            }
        }

        [AgentTool("Take a screenshot of an entire physical monitor / display. " +
            "monitorId='primary' (default) selects the primary display; an integer index like '0' selects by EnumDisplayMonitors order; or a device name like '\\\\.\\DISPLAY1' selects exactly. " +
            "maxWidth>0: downscale (bilinear) so the LONGER side is at most maxWidth pixels (preserves aspect — strongly recommended for 4K displays to reduce 4MB PNG to ~250KB JPG; e.g. maxWidth=1920 halves a 4K capture). " +
            "format='png' (lossless, default) or 'jpg' (much smaller for full-screen captures, lossy via jpgQuality 1-100, default 90). " +
            "saveToPath: optional absolute file path to also save the encoded bytes. " +
            "Result message includes a 'Debug copy at' path (in %TEMP%) which can be used with the Read tool to view the image directly. " +
            "Recommended for token economy on 4K: maxWidth=1920, format='jpg', jpgQuality=85. " +
            "Call ListMonitors first to see available IDs and per-monitor DPI. Windows Editor only.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string CaptureMonitor(
            string monitorId = "primary",
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "")
        {
            using (new WindowCaptureNative.DpiScope())
            {
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var resolved = WindowCaptureNative.ResolveMonitor(monitorId, monitors);
                if (resolved == null)
                {
                    string ids = string.Join(", ",
                        monitors.Select((m, i) => $"\"{i}\"({m.DeviceName}{(m.IsPrimary ? "/Primary" : "")})"));
                    return $"Error: monitorId '{monitorId}' not found. Available: {ids}";
                }
                var m = resolved.Value;

                byte[] pixels;
                try
                {
                    pixels = WindowCaptureNative.CaptureScreenRect(m.X, m.Y, m.Width, m.Height, includeLayeredWindows: true);
                }
                catch (Exception ex)
                {
                    return $"Error: Capture failed: {ex.Message}";
                }

                return EncodeAndAttach(pixels, m.Width, m.Height, $"Monitor '{m.DeviceName}'", maxWidth, format, jpgQuality, saveToPath);
            }
        }

        // ─── Capture core ───

        /// <summary>
        /// The single body shared by CaptureEditorWindow and CaptureWindow: try PrintWindow, fall back to
        /// a screen-rect BitBlt, cut out the requested area, and hand the pixels to CaptureCommon.
        ///
        /// <paramref name="windowRect"/> is the target HWND's screen rect, i.e. the frame of reference of
        /// the PrintWindow bitmap (its top-left pixel is that rect's top-left corner).
        /// <paramref name="subject"/> is the part of the desktop actually wanted, in the same physical
        /// screen coordinates; pass null to mean "the whole window". Keeping these separate is what makes
        /// a docked EditorWindow work: the bitmap is the entire editor, the subject is one tab's rect.
        ///
        /// <paramref name="region"/> is the caller's optional rectangle INSIDE the subject, top-left
        /// origin. It is converted to CaptureCommon's bottom-left cropRegion through
        /// CaptureCommon.RectTopLeftToBottomLeft — never by hand here, because a flip written per tool is
        /// how one tool ends up cropping the mirrored band of the image while still reporting success.
        ///
        /// Returns the finished tool message, "Success: ..." or "Error: ...", ready to relay verbatim.
        /// </summary>
        private static string CaptureWindowContent(
            IntPtr hwnd,
            RectInt windowRect,
            RectInt? subject,
            string region,
            bool focusless,
            bool targetCanBeRaised,
            bool allowRaise,
            string label,
            string context,
            CaptureOptions opt)
        {
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(context)) notes.Add(context);

            byte[] bgra = null;
            int bw = 0, bh = 0;
            RectInt cut = default(RectInt);
            string method = null;

            // Re-read the rect immediately before capturing: the caller measured it a few calls ago and a
            // window that moved in between would shift every crop derived from the stale origin.
            if (hwnd != IntPtr.Zero)
            {
                if (WindowCaptureNative.TryGetWindowRect(hwnd, out int wx, out int wy, out int ww, out int wh,
                                                         out string rectError))
                {
                    windowRect = new RectInt(wx, wy, ww, wh);
                }
                else
                {
                    notes.Add($"the window rect could not be re-read ({rectError}); the crop uses the rect " +
                              "measured earlier in this call");
                }
            }

            if (focusless && hwnd != IntPtr.Zero)
            {
                if (WindowCaptureNative.TryPrintWindow(hwnd, out bgra, out bw, out bh, out string printError))
                {
                    method = "PrintWindow(PW_RENDERFULLCONTENT), focus untouched";
                    cut = subject.HasValue
                        ? new RectInt(subject.Value.x - windowRect.x, subject.Value.y - windowRect.y,
                                      subject.Value.width, subject.Value.height)
                        : new RectInt(0, 0, bw, bh);
                }
                else
                {
                    bgra = null;
                    notes.Add($"PrintWindow declined ({printError}) so the legacy screen-rect route was used");
                }
            }
            else if (focusless)
            {
                notes.Add("no window handle was available, so PrintWindow could not be used");
            }

            if (bgra == null)
            {
                RectInt screenRect = subject ?? windowRect;
                if (screenRect.width <= 0 || screenRect.height <= 0)
                    return $"Error: the area to capture is empty ({screenRect.width}x{screenRect.height}).";

                bool raised = false;
                if (allowRaise && targetCanBeRaised)
                {
                    if (WindowCaptureNative.TryBringUnityToForeground(out string fgError))
                    {
                        // The window is now activated, but DWM composites the z-order change
                        // asynchronously. Without a beat here BitBlt can still grab the previously
                        // topmost app. DWM runs in its own process, so sleeping the editor thread
                        // does not prevent the other window from being painted away.
                        System.Threading.Thread.Sleep(80);
                        raised = true;
                    }
                    else
                    {
                        notes.Add($"bringToFront failed ({fgError}) — another application may be in the picture");
                    }
                }
                else if (!targetCanBeRaised)
                {
                    notes.Add("the target belongs to another process and cannot be raised from here, so " +
                              "anything drawn on top of it is in the picture");
                }
                else
                {
                    notes.Add("bringToFront=false — anything drawn on top of the target is in the picture");
                }

                try
                {
                    bgra = WindowCaptureNative.CaptureScreenRect(screenRect.x, screenRect.y,
                        screenRect.width, screenRect.height, includeLayeredWindows: false);
                }
                catch (Exception ex)
                {
                    return $"Error: Capture failed: {ex.Message}";
                }
                bw = screenRect.width;
                bh = screenRect.height;
                cut = new RectInt(0, 0, bw, bh);
                method = raised
                    ? "screen-rect BitBlt after raising Unity to the foreground"
                    : "screen-rect BitBlt without raising the target";
            }

            var bitmap = new RectInt(0, 0, bw, bh);
            RectInt available = Intersect(cut, bitmap);
            if (available.width <= 0 || available.height <= 0)
            {
                return $"Error: the area to capture (x={cut.x},y={cut.y},{cut.width}x{cut.height} inside the " +
                       $"window) lies entirely outside the {bw}x{bh} bitmap that was captured. The window " +
                       "most likely moved or was resized between being measured and being captured — retry.";
            }
            if (available.width != cut.width || available.height != cut.height)
            {
                notes.Add($"the target rect stuck out of the captured window bitmap and was clipped to " +
                          $"{available.width}x{available.height}");
            }

            RectInt final = available;
            if (!string.IsNullOrWhiteSpace(region))
            {
                if (!CaptureCommon.TryParseCropRegionSyntax(region, out int rx, out int ry, out int rw, out int rh,
                                                            out string regionError))
                {
                    return $"Error: region '{region}' must be 'x,y,w,h' — four integers in physical pixels, " +
                           $"origin TOP-LEFT of the captured window ({regionError})";
                }
                if (rx < 0 || ry < 0 || rx + rw > cut.width || ry + rh > cut.height)
                {
                    return $"Error: region x={rx},y={ry},w={rw},h={rh} does not fit inside the " +
                           $"{cut.width}x{cut.height} area being captured. The origin is that area's " +
                           "TOP-LEFT corner and y grows downward, and the numbers are physical pixels — on " +
                           "a 150%-scaled display they are 1.5x the logical values EditorWindow.position " +
                           "reports. The rectangle is not clamped, because a clamped crop would return a " +
                           "different area than the one asked for while still reporting success.";
                }
                final = new RectInt(cut.x + rx, cut.y + ry, rw, rh);
                RectInt clipped = Intersect(final, bitmap);
                if (clipped.width != final.width || clipped.height != final.height)
                {
                    return $"Error: region x={rx},y={ry},w={rw},h={rh} fits the target rect but falls partly " +
                           $"outside the {bw}x{bh} bitmap that was actually captured — the window moved or " +
                           "was resized between being measured and being captured. Retry.";
                }
                notes.Add($"region {rw}x{rh} at x={rx},y={ry} of the window (top-left origin)");
            }

            if (final.x != 0 || final.y != 0 || final.width != bw || final.height != bh)
            {
                // CaptureCommon crops in image space (bottom-left origin); everything above is in window
                // space (top-left origin). One conversion, in the one place allowed to write it.
                Rect flipped = CaptureCommon.RectTopLeftToBottomLeft(
                    new Rect(final.x, final.y, final.width, final.height), bh);
                opt.CropRegion = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                    Mathf.RoundToInt(flipped.x), Mathf.RoundToInt(flipped.y), final.width, final.height);
            }

            string fullLabel = notes.Count > 0
                ? $"{label} [method={method}; {string.Join("; ", notes)}]"
                : $"{label} [method={method}]";

            string message = CaptureCommon.FinishFromBgra(bgra, bw, bh, opt, fullLabel,
                                                          CaptureRoute.Window, out string error);
            return message ?? $"Error: {error}";
        }

        // ─── Dock / host-view inspection ───

        /// <summary>
        /// What can be learned about an EditorWindow's place in the editor layout. Every field that could
        /// not be read stays null / "unknown"; nothing here is inferred from something else, because a
        /// guessed "activeTab=yes" would send CaptureEditorWindow off to photograph the wrong tab.
        /// </summary>
        private sealed class DockState
        {
            /// <summary>null = unknown. True when this window is its host view's actualView.</summary>
            public bool? IsActiveTab;
            /// <summary>null = unknown. EditorWindow.docked, i.e. Unity's own answer.</summary>
            public bool? IsDocked;
            /// <summary>Formatted sibling tab list: "[A*, B]", "none(single-view host)" or "unknown".</summary>
            public string TabsDisplay = "unknown";
        }

        // EditorWindow's own internals are probed once per domain: ListEditorWindows inspects every window
        // in the layout, so probing (and logging a miss) per row would repeat the same work and the same
        // warning a dozen times in one call. A domain reload clears these, so a Unity version change
        // re-probes rather than caching a stale answer.
        private static bool _editorWindowMembersProbed;
        private static FieldInfo _parentField;
        private static PropertyInfo _dockedProperty;

        private static void ProbeEditorWindowMembers()
        {
            if (_editorWindowMembersProbed) return;
            _editorWindowMembersProbed = true;
            try
            {
                _parentField = FindFieldUpHierarchy(typeof(EditorWindow), "m_Parent");
                if (_parentField == null)
                {
                    AgentLogger.Warning(LogTag.Tool,
                        "WindowCaptureTools: EditorWindow.m_Parent not found on this Unity version — dock " +
                        "state (activeTab / sibling tabs) and waitForRepaint are unavailable; they will be " +
                        "reported as unknown rather than guessed.");
                }

                // EditorWindow.docked is internal; get_docked is present in Unity 2022.3's
                // UnityEditor.CoreModule.dll. A miss leaves IsDocked null and the caller falls back to
                // what the window geometry says, labelled as an inference.
                var docked = FindPropertyUpHierarchy(typeof(EditorWindow), "docked");
                if (docked != null && docked.CanRead && docked.PropertyType == typeof(bool))
                {
                    _dockedProperty = docked;
                }
                else
                {
                    AgentLogger.Debug(LogTag.Tool,
                        "WindowCaptureTools: EditorWindow.docked is not readable on this Unity version; " +
                        "docked/floating will be inferred from window geometry instead.");
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool,
                    $"WindowCaptureTools: probing EditorWindow internals failed ({ex.Message}); dock state " +
                    "will be reported as unknown.");
            }
        }

        private static DockState InspectDockState(EditorWindow window)
        {
            var state = new DockState();
            if (window == null) return state;
            ProbeEditorWindowMembers();

            if (_dockedProperty != null)
            {
                try
                {
                    object value = _dockedProperty.GetValue(window, null);
                    if (value is bool docked) state.IsDocked = docked;
                }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.Tool,
                        $"WindowCaptureTools: reading EditorWindow.docked failed ({ex.Message}).");
                }
            }

            if (!TryGetHostView(window, out object hostView)) return state;
            Type hostType = hostView.GetType();

            // HostView.actualView is the window the host is currently showing, so "am I the active tab"
            // is an identity comparison against it. ReferenceEquals rather than == : a destroyed
            // actualView must read as "not this window", not throw or compare fake-null-equal.
            try
            {
                object actual = null;
                bool haveActual = false;
                var actualProp = FindPropertyUpHierarchy(hostType, "actualView");
                if (actualProp != null && actualProp.CanRead)
                {
                    actual = actualProp.GetValue(hostView, null);
                    haveActual = true;
                }
                else
                {
                    var actualField = FindFieldUpHierarchy(hostType, "m_ActualView");
                    if (actualField != null)
                    {
                        actual = actualField.GetValue(hostView);
                        haveActual = true;
                    }
                }
                if (haveActual) state.IsActiveTab = ReferenceEquals(actual, window);
                else
                    AgentLogger.Debug(LogTag.Tool,
                        $"WindowCaptureTools: neither {hostType.Name}.actualView nor m_ActualView resolved; " +
                        "activeTab will be reported as unknown.");
                state.TabsDisplay = DescribeTabs(hostView, hostType, actual, haveActual);
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"WindowCaptureTools: inspecting {hostType.Name} failed ({ex.Message}); dock state is unknown.");
            }

            return state;
        }

        // DockArea.m_Panes holds every tab sharing the dock. A plain HostView has no tab list at all,
        // which is a real answer ("this window is alone in its host"), so it is distinguished from the
        // field having gone missing on a newer Unity — the latter must read as unknown.
        private static string DescribeTabs(object hostView, Type hostType, object activeView, bool haveActive)
        {
            var panesField = FindFieldUpHierarchy(hostType, "m_Panes");
            if (panesField == null)
            {
                if (string.Equals(hostType.Name, "HostView", StringComparison.Ordinal))
                    return "none(single-view host)";
                AgentLogger.Debug(LogTag.Tool,
                    $"WindowCaptureTools: {hostType.Name}.m_Panes not found; sibling tabs are unknown.");
                return "unknown";
            }

            try
            {
                if (!(panesField.GetValue(hostView) is System.Collections.IEnumerable panes)) return "unknown";

                var names = new List<string>();
                foreach (object pane in panes)
                {
                    var ew = pane as EditorWindow;
                    string name = TitleOf(ew);
                    // Only mark the active tab when we actually know which one it is, so an unmarked list
                    // never implies "none of these is in front".
                    if (haveActive && ReferenceEquals(pane, activeView)) name += "*";
                    names.Add(name);
                }
                if (names.Count == 0) return "none(empty dock)";
                return "[" + string.Join(", ", names) + "]" + (haveActive ? "" : " (active tab unknown)");
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"WindowCaptureTools: reading {hostType.Name}.m_Panes failed ({ex.Message}).");
                return "unknown";
            }
        }

        /// <summary>
        /// EditorWindow.m_Parent, the HostView the window is drawn in. Every piece of layout information
        /// this file needs — active tab, sibling tabs, synchronous repaint — hangs off it, so the
        /// acquisition (and the Unity fake-null trap that comes with it) lives in one place.
        /// Returns false when the field is gone or the host view has been destroyed.
        /// </summary>
        private static bool TryGetHostView(EditorWindow window, out object hostView)
        {
            hostView = null;
            if (window == null) return false;
            ProbeEditorWindowMembers();
            if (_parentField == null) return false;   // already reported once by the probe
            try
            {
                object value = _parentField.GetValue(window);
                if (value == null) return false;
                // A destroyed HostView is a live managed reference that Unity's == operator reports as
                // null. Calling into it would throw MissingReferenceException, so filter it here.
                if (value is UnityEngine.Object asObject && asObject == null) return false;
                hostView = value;
                return true;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool, $"WindowCaptureTools: resolving m_Parent failed ({ex.Message}).");
                return false;
            }
        }

        // Private fields declared on a BASE type are not returned by GetField on the derived type, and
        // m_Panes/m_Parent live at different levels of the HostView/DockArea and EditorWindow chains
        // depending on the Unity version, so the chain is walked explicitly.
        private static FieldInfo FindFieldUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        // Same reason as FindFieldUpHierarchy: the object in hand is usually a DockArea while
        // actualView is declared on its HostView base, and reflection's inherited-member rules for
        // non-public properties are version-dependent enough not to bet a capture on.
        private static PropertyInfo FindPropertyUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        /// <summary>
        /// Reflection helper — HostView.RepaintImmediately() (a public method of GUIView in 2022.3) to
        /// synchronously force a paint of the (newly-focused) tab. Without it, BitBlt captures the
        /// previously visible tab, and PrintWindow copies whatever the window painted last, which can
        /// predate the change being checked.
        ///
        /// Returns false with a reason in <paramref name="error"/> when the paint did not happen. The reason
        /// is handed to the caller instead of only being logged: a capture whose freshness could not be
        /// forced is a capture the reader has to distrust, and a log line the tool's caller never sees does
        /// not tell them that.
        /// </summary>
        private static bool TryRepaintImmediately(EditorWindow window, out string error)
        {
            error = null;
            try
            {
                if (!TryGetHostView(window, out object hostView))
                {
                    error = "the HostView (EditorWindow.m_Parent) could not be resolved on this Unity version";
                    return false;
                }
                var method = hostView.GetType().GetMethod("RepaintImmediately", InstanceAny);
                if (method == null)
                {
                    error = $"{hostView.GetType().Name}.RepaintImmediately does not exist on this Unity version";
                    AgentLogger.Debug(LogTag.Tool,
                        "WindowCaptureTools: HostView.RepaintImmediately not found; the synchronous repaint had no effect.");
                    return false;
                }
                method.Invoke(hostView, null);
                return true;
            }
            catch (Exception ex)
            {
                // Internal API may change between Unity versions — report it rather than swallowing it.
                error = $"HostView.RepaintImmediately threw ({ex.GetBaseException().Message})";
                AgentLogger.Debug(LogTag.Tool,
                    $"WindowCaptureTools: invoking HostView.RepaintImmediately failed ({ex.GetBaseException().Message}).");
                return false;
            }
        }

        // The context string of a capture is a single "; "-joined sentence, and the tab/repaint warnings have
        // to survive next to the dock note on every return path — dropping them on one path is how a stale
        // or wrong-tab image would come back with nothing to distrust it by.
        private static string JoinNotes(List<string> notes, string tail)
        {
            var parts = new List<string>();
            if (notes != null) parts.AddRange(notes.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (!string.IsNullOrWhiteSpace(tail)) parts.Add(tail);
            return string.Join("; ", parts);
        }

        // ─── Container (OS window) resolution ───

        /// <summary>
        /// Which OS window an EditorWindow's pixels live in. It has to be resolved geometrically because
        /// Unity exposes no HWND for a ContainerWindow at all, and it has to be right: the crop offset for
        /// a docked window is taken from this window's rect.
        ///
        /// The match is made against the EditorWindow's own ContainerWindow RECT (read by reflection),
        /// not against "which OS window contains this tab". The latter is ambiguous in a way that produces
        /// a convincing wrong picture: a floating utility window parked over a narrow docked tab contains
        /// that tab's rect too, and is smaller than the main window, so any containment-based rule would
        /// pick it and then crop a slice of the floating window while labelling it with the docked
        /// window's name. Comparing whole rects (intersection over union) separates the two cleanly.
        /// </summary>
        private struct ContainerResolution
        {
            public bool Resolved;
            public IntPtr Hwnd;
            /// <summary>The container's screen rect in physical pixels (the PrintWindow frame of reference).</summary>
            public RectInt Rect;
            public bool IsUnityMainWindow;
            public bool IsMinimized;
            public bool IsVisible;
            /// <summary>How the answer was reached, for the result message.</summary>
            public string How;
            /// <summary>Why nothing was resolved. Only meaningful when Resolved is false.</summary>
            public string Error;
        }

        // Visible top-level windows owned by THIS Unity process. includeInvisible:true plus a manual
        // IsVisible filter on purpose: a floating Unity container or a popup can have no caption, and the
        // caption filter inside EnumerateTopLevelWindows would drop exactly those.
        private static List<WindowCaptureNative.WindowDescriptor> EnumerateUnityTopLevelWindows()
        {
            var result = new List<WindowCaptureNative.WindowDescriptor>();
            foreach (var d in WindowCaptureNative.EnumerateTopLevelWindows(includeInvisible: true))
            {
                if (!d.IsUnity) continue;
                if (!d.IsVisible) continue;
                if (d.Width <= 0 || d.Height <= 0) continue;
                result.Add(d);
            }
            return result;
        }

        /// <summary>
        /// The ContainerWindow rect in PHYSICAL pixels — the space the enumerated OS window rects live in —
        /// or null when Unity would not report it, with the reason in <paramref name="error"/>.
        /// </summary>
        private static RectInt? ContainerRectHint(EditorWindow window,
            List<WindowCaptureNative.MonitorDescriptor> monitors, out string error)
        {
            if (!TryGetContainerWindowRect(window, out Rect logical, out error)) return null;

            var (cx, cy, cw, ch) = WindowCaptureNative.UnityRectToPhysical(
                logical.x, logical.y, logical.width, logical.height, monitors);
            if (cw <= 0 || ch <= 0)
            {
                error = $"the ContainerWindow rect is empty ({cw}x{ch})";
                return null;
            }
            return new RectInt(cx, cy, cw, ch);
        }

        /// <summary>
        /// The rect of the ContainerWindow — the OS-level window an EditorWindow is drawn in — in Unity's
        /// logical points, i.e. the same coordinate space as EditorWindow.position, so
        /// WindowCaptureNative.UnityRectToPhysical converts it the same way.
        ///
        /// Unity exposes the ContainerWindow's rect but never its HWND, which is why identifying the OS
        /// window means matching this rect against the enumerated top-level windows rather than asking for
        /// a handle. Returns false with a reason when any link of the chain (m_Parent → window → position)
        /// is unavailable; the caller then falls back to a heuristic and reports that it did.
        /// </summary>
        private static bool TryGetContainerWindowRect(EditorWindow window, out Rect logicalRect, out string error)
        {
            logicalRect = default(Rect);
            error = null;

            if (!TryGetHostView(window, out object hostView))
            {
                error = "the HostView (EditorWindow.m_Parent) could not be resolved";
                return false;
            }

            object container;
            try
            {
                container = null;
                var windowProperty = FindPropertyUpHierarchy(hostView.GetType(), "window");
                if (windowProperty != null && windowProperty.CanRead)
                    container = windowProperty.GetValue(hostView, null);
                if (container == null)
                {
                    var windowField = FindFieldUpHierarchy(hostView.GetType(), "m_Window");
                    if (windowField != null) container = windowField.GetValue(hostView);
                }
            }
            catch (Exception ex)
            {
                error = $"reading the host view's ContainerWindow failed ({ex.Message})";
                return false;
            }

            if (container == null)
            {
                error = "the host view has no ContainerWindow (it is not hosted in an OS window)";
                return false;
            }
            // Same fake-null trap as the HostView: a destroyed ContainerWindow is still a live reference.
            if (container is UnityEngine.Object containerObject && containerObject == null)
            {
                error = "the ContainerWindow has been destroyed";
                return false;
            }

            try
            {
                var positionProperty = FindPropertyUpHierarchy(container.GetType(), "position");
                if (positionProperty != null && positionProperty.CanRead &&
                    positionProperty.PropertyType == typeof(Rect))
                {
                    if (positionProperty.GetValue(container, null) is Rect fromProperty)
                    {
                        logicalRect = fromProperty;
                        return true;
                    }
                }
                var positionField = FindFieldUpHierarchy(container.GetType(), "m_PixelRect");
                if (positionField != null && positionField.FieldType == typeof(Rect))
                {
                    if (positionField.GetValue(container) is Rect fromField)
                    {
                        logicalRect = fromField;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"reading ContainerWindow.position failed ({ex.Message})";
                return false;
            }

            error = $"neither {container.GetType().Name}.position nor m_PixelRect is readable on this Unity version";
            return false;
        }

        /// <param name="subject">The EditorWindow's own screen rect, physical pixels.</param>
        /// <param name="containerRect">
        /// The ContainerWindow's screen rect in physical pixels when Unity would tell us (see
        /// <see cref="TryGetContainerWindowRect"/>), otherwise null. When present it is matched by
        /// intersection-over-union, which is the reliable answer; when absent the code falls back to the
        /// ambiguous containment heuristic and SAYS SO in <c>How</c>.
        /// </param>
        private static ContainerResolution ResolveContainerWindow(
            RectInt subject, RectInt? containerRect,
            List<WindowCaptureNative.WindowDescriptor> unityWindows, IntPtr mainHwnd)
        {
            var result = new ContainerResolution { Hwnd = IntPtr.Zero };

            long subjectArea = (long)subject.width * subject.height;
            if (subjectArea <= 0)
            {
                result.Error = $"the window rect is empty ({subject.width}x{subject.height})";
                return result;
            }

            bool found = false;
            double bestScore = 0;
            string how = null;
            WindowCaptureNative.WindowDescriptor best = default(WindowCaptureNative.WindowDescriptor);

            if (containerRect.HasValue && unityWindows != null)
            {
                // Best intersection-over-union against the ContainerWindow rect Unity reported. The OS
                // rect is a few pixels larger (Windows 10+ counts an invisible resize border into
                // GetWindowRect), so the true container scores ~0.9 while an unrelated window that merely
                // overlaps scores far lower. Below 0.5 nothing is accepted.
                foreach (var d in unityWindows)
                {
                    var candidate = new RectInt(d.X, d.Y, d.Width, d.Height);
                    long intersection = IntersectArea(containerRect.Value, candidate);
                    if (intersection <= 0) continue;
                    double union = (double)containerRect.Value.width * containerRect.Value.height
                                 + (double)d.Width * d.Height - intersection;
                    if (union <= 0) continue;
                    double iou = intersection / union;
                    if (iou < 0.5 || iou <= bestScore) continue;
                    bestScore = iou;
                    best = d;
                    found = true;
                }
                if (found)
                    how = $"matched the EditorWindow's ContainerWindow rect ({bestScore * 100.0:F0}% overlap)";
            }

            if (!found && unityWindows != null)
            {
                // Heuristic fallback: the SMALLEST window that covers (nearly) all of the subject rect.
                // Smallest, because a floating EditorWindow sits on top of the main window and both
                // contain its rect. This can still pick the wrong window when an unrelated floating window
                // happens to cover the target, which is exactly why How records that it was a guess.
                double bestArea = double.MaxValue;
                double bestCoverage = 0;
                foreach (var d in unityWindows)
                {
                    var candidate = new RectInt(d.X, d.Y, d.Width, d.Height);
                    double coverage = (double)IntersectArea(subject, candidate) / subjectArea;
                    if (coverage < 0.9) continue;
                    double area = (double)d.Width * d.Height;
                    if (area >= bestArea) continue;
                    bestArea = area;
                    bestCoverage = coverage;
                    best = d;
                    found = true;
                }
                if (found)
                {
                    how = $"HEURISTIC: smallest visible Unity window covering {bestCoverage * 100.0:F0}% of " +
                          "the window rect, so another window lying over the target could have been picked " +
                          "instead";
                    // Distinguish "we never had the reliable answer" from "we had it and it did not match".
                    // The second case means the real container is not among the visible top-level windows —
                    // minimized, hidden or cloaked — or that the rect could not be converted to physical
                    // pixels on this multi-monitor/DPI layout. Either way the image may not be this window.
                    how += containerRect.HasValue
                        ? $" — WARNING: the ContainerWindow rect ({containerRect.Value.x},{containerRect.Value.y} " +
                          $"{containerRect.Value.width}x{containerRect.Value.height}) matched no visible " +
                          "top-level Unity window, so verify the image really shows the window you asked for"
                        : " (the ContainerWindow rect could not be read at all)";
                }
            }

            if (found)
            {
                result.Resolved = true;
                result.Hwnd = best.Hwnd;
                result.Rect = new RectInt(best.X, best.Y, best.Width, best.Height);
                result.IsUnityMainWindow = mainHwnd != IntPtr.Zero && best.Hwnd == mainHwnd;
                result.IsMinimized = best.IsMinimized;
                result.IsVisible = best.IsVisible;
                result.How = how;
                return result;
            }

            // Nothing covers the rect. The usual cause is a minimized editor (rect parked off-screen), so
            // fall back to the main window and report its state rather than claiming "not resolved".
            if (mainHwnd == IntPtr.Zero)
            {
                result.Error = "the Unity main window handle is unavailable";
                return result;
            }
            if (!WindowCaptureNative.TryDescribeWindow(mainHwnd, out var mainDescriptor, out string describeError))
            {
                result.Error = describeError;
                return result;
            }

            result.Resolved = true;
            result.Hwnd = mainHwnd;
            result.Rect = new RectInt(mainDescriptor.X, mainDescriptor.Y, mainDescriptor.Width, mainDescriptor.Height);
            result.IsUnityMainWindow = true;
            result.IsMinimized = mainDescriptor.IsMinimized;
            result.IsVisible = mainDescriptor.IsVisible;
            result.How = mainDescriptor.IsMinimized
                ? "fell back to the Unity main window, which is MINIMIZED — no window covers the reported rect"
                : "fell back to the Unity main window; no enumerated Unity window covers the reported rect";
            return result;
        }

        // ─── Formatting helpers ───

        private static string Tri(bool? value) => value.HasValue ? (value.Value ? "yes" : "no") : "unknown";

        private static string DescribeDocked(DockState dock, ContainerResolution container)
        {
            if (!dock.IsDocked.HasValue)
            {
                if (!container.Resolved) return "unknown";
                return container.IsUnityMainWindow
                    ? "yes(inferred-from-geometry)"
                    : "no(floating, inferred-from-geometry)";
            }
            if (!dock.IsDocked.Value) return "no(floating)";
            // Docked does not imply "inside the main window": a whole tab group can live in a floating
            // container, and that difference decides whether capturing it crops the main window bitmap.
            if (container.Resolved && !container.IsUnityMainWindow) return "yes(into a floating container)";
            return "yes";
        }

        private static string DescribeVisible(DockState dock, ContainerResolution container)
        {
            if (!container.Resolved) return "unknown";
            if (container.IsMinimized) return "no(os window minimized)";
            if (!container.IsVisible) return "no(os window hidden)";
            if (!dock.IsActiveTab.HasValue) return "unknown(active tab unknown)";
            return dock.IsActiveTab.Value ? "yes" : "no(background tab)";
        }

        private static string DescribeHwnd(ContainerResolution container)
        {
            if (!container.Resolved)
                return $"unavailable({(string.IsNullOrEmpty(container.Error) ? "reason unavailable" : container.Error)})";
            return $"0x{container.Hwnd.ToInt64():X8}" + (container.IsUnityMainWindow ? "(unity main)" : "");
        }

        private static string DescribeCandidates(List<WindowCaptureNative.WindowDescriptor> candidates)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                var d = candidates[i];
                string title = string.IsNullOrEmpty(d.Title) ? "(untitled)" : d.Title;
                sb.AppendLine($"  matchIndex={i}: 0x{d.Hwnd.ToInt64():X8} \"{title}\" process={d.ProcessName}" +
                              $"(pid {d.ProcessId}) size=({d.Width}x{d.Height})");
            }
            return sb.ToString().TrimEnd();
        }

        private static string TitleOf(EditorWindow window)
        {
            if (window == null) return "(destroyed)";
            var content = window.titleContent;
            return content != null && !string.IsNullOrEmpty(content.text) ? content.text : "(untitled)";
        }

        // ─── Geometry helpers ───

        private static RectInt Intersect(RectInt a, RectInt b)
        {
            int x0 = Mathf.Max(a.x, b.x);
            int y0 = Mathf.Max(a.y, b.y);
            int x1 = Mathf.Min(a.x + a.width, b.x + b.width);
            int y1 = Mathf.Min(a.y + a.height, b.y + b.height);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        private static long IntersectArea(RectInt a, RectInt b)
        {
            RectInt i = Intersect(a, b);
            return (long)i.width * i.height;
        }

        /// <summary>
        /// Parses a window handle the way ListWindows prints it: hexadecimal, '0x' optional. Hex even
        /// without the prefix — a bare "1234" is 0x1234 — because accepting it as decimal would silently
        /// address a different window, and there is no way to tell afterwards which reading was meant.
        /// </summary>
        private static bool TryParseHwnd(string text, out IntPtr handle, out string error)
        {
            handle = IntPtr.Zero;
            error = null;

            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                error = "hwnd is empty.";
                return false;
            }
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            value = value.TrimStart('0');
            if (value.Length == 0)
            {
                error = $"hwnd '{text}' is zero, which is not a window handle.";
                return false;
            }
            if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
            {
                error = $"hwnd '{text}' is not a hexadecimal window handle. Copy the 0x… value ListWindows " +
                        "prints; it is read as HEX even without the 0x prefix, so '1234' means 0x1234.";
                return false;
            }
            if (IntPtr.Size == 4 && parsed > uint.MaxValue)
            {
                error = $"hwnd '{text}' does not fit in a 32-bit handle on this editor build.";
                return false;
            }

            handle = new IntPtr(unchecked((long)parsed));
            return true;
        }

        private static List<EditorWindow> EnumerateValidEditorWindows()
        {
            var all = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var list = new List<EditorWindow>(all.Length);
            foreach (var w in all)
            {
                if (w == null) continue;
                if (w.GetType().IsAbstract) continue;
                if (w.titleContent == null || string.IsNullOrEmpty(w.titleContent.text)) continue;
                if (w.position.width <= 0 || w.position.height <= 0) continue;
                list.Add(w);
            }
            return list;
        }

        // Turns a raw BGRA32 screen grab into the attached image plus the result sentence the capture tools
        // return verbatim: a "Success: ..." line, or "Error: ..." when anything went wrong. Callers relay the
        // string as-is, so that contract has to hold for both outcomes.
        //
        // The body used to be a near-duplicate of SceneViewTools.EncodeWithOptions — the two drifted, so
        // window captures never gained cropRegion and scene captures never gained saveToPath reporting.
        // Everything now runs through CaptureCommon, which additionally:
        //   - stamps route=window into the message, because a BitBlt of the screen and a camera render of
        //     the same subject are different pictures and the reader has to know which one this is;
        //   - forces the alpha channel opaque (a GDI DIB's alpha byte is undefined, and a driver returning
        //     zero there yields a fully transparent PNG that reads as a successful capture of nothing);
        //   - rejects a format other than png/jpg instead of silently substituting png.
        //
        // Kept for CaptureMonitor, which captures a fixed rect and needs none of the window plumbing in
        // CaptureWindowContent (routes, crop composition, foreground handling).
        private static string EncodeAndAttach(byte[] bgraPixels, int width, int height, string label,
            int maxWidth, string format, int jpgQuality, string saveToPath)
        {
            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
            string message = CaptureCommon.FinishFromBgra(bgraPixels, width, height, opt, label,
                                                          CaptureRoute.Window, out string error);
            return message ?? $"Error: {error}";
        }
    }
}
#endif
