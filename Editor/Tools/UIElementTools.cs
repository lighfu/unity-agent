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
    /// <summary>
    /// Structural read-out of an EditorWindow's UI Toolkit element tree, plus the numbered overlay that
    /// ties that structure back to a screenshot.
    ///
    /// A capture on its own answers "what does this window look like" but not "where is the Apply button
    /// and is it disabled" — coordinates read off a picture by eye are guesses, and a guessed rect is
    /// indistinguishable from a measured one once it is written down. This walks the real element tree
    /// instead, and (on Windows) burns a numbered box over each element so the listing and the pixels can
    /// be read against each other.
    ///
    /// Deliberately READ-ONLY: no clicks, no focus changes, no value writes, not even a Repaint. Pressing a
    /// button from here could open a modal dialog, and a modal dialog pumps its own message loop on the
    /// editor's main thread — the next tool call would not be serviced until a human closed it.
    /// </summary>
    public static class UIElementTools
    {
        /// <summary>Hard cap on the walk. A pathological tree must not hang the editor's main thread.</summary>
        private const int MaxWalkNodes = 20000;

        /// <summary>Depth cap, for the same reason. Real editor UI stays well under 40 levels.</summary>
        private const int MaxWalkDepth = 200;

        /// <summary>Label / name text is truncated to this many characters so one element stays on one row.</summary>
        private const int MaxTextChars = 60;

        [AgentTool(@"List the UI Toolkit element tree of an EditorWindow: every element's number, type,
name, visible text, WINDOW-RELATIVE rect, enabled / visible state and depth in the hierarchy. Read-only —
it never clicks, focuses, repaints or sets a value.

Use it to turn a screenshot into structure: 'where is the Apply button', 'is this field disabled', 'why is
that row not on screen'. Call ListEditorWindows first to see which titles are open.

windowTitleContains: case-insensitive substring of the window title.
matchIndex (0-based, default 0) picks among the windows whose TITLE MATCHES — it is NOT the index printed
  by ListEditorWindows. When several titles match, the result says which one was inspected.
filter: case-insensitive substring matched against the TYPE NAME, the element name, OR the visible text
  (any one is enough). It also decides what gets a box when annotate=true, so filter='Button' annotates
  only the buttons.
maxElements (default 200) caps the ROWS. A real window has hundreds of elements. When the cap cuts the
  list, the result states how many matched and how many are missing — it never truncates silently.

rect IS WINDOW-RELATIVE, IN UNITY LOGICAL POINTS, ORIGIN TOP-LEFT, y GROWING DOWNWARD: (0,0) is the
top-left of EditorWindow.position, i.e. the window's own drawing area — the same area CaptureEditorWindow
captures, so the dock's tab strip is OUTSIDE it. The numbers come from worldBound (absolute layout) minus
the root element's own origin, NOT from layout, which is parent-relative and would be short by every
ancestor's offset — a number overlay placed from layout values drifts further off the deeper the element
sits. On a 150%-scaled display the physical pixels of a capture are 1.5x these values, so scale before
handing them to CaptureEditorWindow's region argument (also top-left based) and remember that cropRegion /
DiffImages.maskRegion are BOTTOM-left based. Values are whatever the LAST layout pass produced; an element
with no layout yet is reported as rect=unavailable instead of as zeros.

annotate=true (Windows editor only) additionally attaches the window's picture with a numbered box over
each listed element, drawn straight into the pixels — no font asset is involved, so it looks the same in
every project. The bitmap comes from PrintWindow(PW_RENDERFULLCONTENT): the user's focus is NOT taken and
applications lying over Unity do not appear in it. Annotation is SKIPPED, with the reason in the result,
when this editor is not Windows, when the window is a background tab of its dock (a background tab is not
drawn anywhere, so the boxes would land on the tab that IS in front), when PrintWindow declines, when the
window has no decomposable tree (see IMGUI below), or when no listed element has a non-empty rect. There is
no BitBlt fallback on purpose: that route photographs the desktop, so anything overlapping the editor would
be boxed as if it were this window's own UI.
  maxAnnotations (default 60) caps the BOXES independently of maxElements — past roughly that many the
  boxes overlap into an unreadable mesh. Elements beyond the cap are still listed, just not boxed, and the
  result says how many. A '*' right after an element's number means both its box and its number are in the
  image; no '*' means the element is listed only.
  CAVEAT: PrintWindow copies the window's LAST PAINTED content while the boxes come from its CURRENT
  layout. If the window has not repainted since the change you are verifying, pixels and boxes can
  disagree. Capture with CaptureEditorWindow(waitForRepaint=true) first if that matters.
maxWidth / format / jpgQuality / saveToPath apply to the annotated image ONLY and are ignored when
  annotate=false. maxWidth>0 downscales the longer side; outlines and numbers are drawn thicker beforehand
  so they survive the downscale.

IMGUI WINDOWS CANNOT BE DECOMPOSED, AND THEIR IMGUIContainer IS INVISIBLE FROM HERE. An IMGUI
EditorWindow — most of Unity's own: Console, Project, Animation, Audio Mixer — draws every control
imperatively inside one OnGUI callback, so the controls exist nowhere as objects. Do NOT expect to see an
IMGUIContainer standing in for them: the container that runs that OnGUI is NOT under rootVisualElement,
Unity's window backend puts it in the HOST VIEW's panel tree as a SIBLING of this window's root element
(verified on 2022.3: DefaultWindowBackend inserts it into panel.visualTree, and DefaultEditorWindowBackend
adds the window's own root to that same tree). Such a window therefore walks to exactly ONE row — its own
empty root — with no IMGUIContainer anywhere in it.
That single row is returned with a note that names the OnGUI method, found by reflection on the window's
type. The reflection is what makes the note truthful, because a one-row tree has a SECOND and completely
different cause: rootVisualElement is created on demand, so a window that has never been shown (or whose
CreateGUI has not run yet) also walks to one row. The note says which of the two it is, or says unknown
when reflection cannot tell — it never guesses, because 'show the window and retry' is useless advice for
a Console that is already open and drawing. The list is never empty, which would read as 'this window has
no UI'. For an OnGUI window, capture the pixels (CaptureEditorWindow) and read that OnGUI's source instead.

The listing works on every platform; only annotate needs the Windows editor.",
            Author = "ajisaiflow", Category = "WindowCapture", Risk = ToolRisk.Safe)]
        public static string ListUIElements(
            string windowTitleContains,
            int matchIndex = 0,
            bool annotate = false,
            int maxElements = 200,
            string filter = "",
            int maxAnnotations = 60,
            int maxWidth = 0,
            string format = "png",
            int jpgQuality = 90,
            string saveToPath = "")
        {
            if (string.IsNullOrWhiteSpace(windowTitleContains))
                return "Error: windowTitleContains is empty. Call ListEditorWindows to see the open titles.";
            if (maxElements <= 0)
                return $"Error: maxElements must be positive (got {maxElements}).";
            if (annotate && maxAnnotations <= 0)
                return $"Error: maxAnnotations must be positive when annotate=true (got {maxAnnotations}). " +
                       "Pass annotate=false if you do not want an image at all.";

            var allWindows = EnumerateValidEditorWindows();
            var matches = allWindows
                .Where(w => TitleOf(w).IndexOf(windowTitleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matches.Count == 0)
            {
                var titles = allWindows.Select(w => $"\"{TitleOf(w)}\"").Distinct().ToList();
                string available = titles.Count == 0
                    ? "(none — no EditorWindow is loaded)"
                    : string.Join(", ", titles.Take(20)) + (titles.Count > 20 ? $", ... ({titles.Count} in total)" : "");
                return $"Error: no EditorWindow whose title contains '{windowTitleContains}'. Available: {available}";
            }
            if (matchIndex < 0 || matchIndex >= matches.Count)
            {
                string candidates = string.Join(", ", matches.Select((w, i) => $"[{i}] \"{TitleOf(w)}\""));
                return $"Error: matchIndex {matchIndex} is out of range — {matches.Count} window(s) match " +
                       $"'{windowTitleContains}': {candidates}.";
            }

            EditorWindow window = matches[matchIndex];
            string title = TitleOf(window);

            // Validate the image options before walking anything: reporting a bad format only after the
            // listing has been built would bury the error under a few hundred rows.
            var opt = CaptureOptions.Create(maxWidth, format, jpgQuality, saveToPath);
            if (annotate)
            {
                if (!opt.Validate(out string optError)) return $"Error: {optError}";
            }

            VisualElement root;
            try
            {
                root = window.rootVisualElement;
            }
            catch (Exception ex)
            {
                return $"Error: reading rootVisualElement of EditorWindow '{title}' threw ({ex.Message}). " +
                       "The window is probably being destroyed or was never created; re-open it and retry.";
            }
            if (root == null)
            {
                return $"Error: EditorWindow '{title}' has no rootVisualElement (it returned null). The window " +
                       "has not been created or shown yet, so no element tree exists — this is NOT 'the window " +
                       "has no UI'. Show the window and call again.";
            }

            var notes = new List<string>();

            // Every rect is reported relative to the ROOT's own origin, so the answer is window-relative
            // whatever coordinate space the panel itself happens to live in (a docked window's panel is
            // shared with its host view). Subtracting a wrong origin is invisible in the listing but shifts
            // every annotation box by the same amount, so the one case where the origin is unknown is
            // reported rather than assumed to be zero.
            Vector2 origin = Vector2.zero;
            try
            {
                Rect rootBound = root.worldBound;
                if (IsFinite(rootBound))
                {
                    origin = new Vector2(rootBound.x, rootBound.y);

                    // The whole "window-relative" claim rests on the root spanning the window's drawing area.
                    // It always does in practice, but if it ever does not, every rect below — and every
                    // annotation box drawn from one — is off by the difference, and that is invisible unless
                    // it is said. 2 points of slack absorbs rounding.
                    Rect posPt = window.position;
                    if (Mathf.Abs(rootBound.width - posPt.width) > 2f ||
                        Mathf.Abs(rootBound.height - posPt.height) > 2f)
                    {
                        notes.Add($"the root element is {rootBound.width:F0}x{rootBound.height:F0} points but " +
                                  $"the window's drawing area is {posPt.width:F0}x{posPt.height:F0} — it does " +
                                  "NOT span the window, so rects below are relative to the root's top-left " +
                                  "rather than the window's, and any annotation box may be offset by the " +
                                  "difference.");
                    }
                }
                else
                {
                    notes.Add("the root element's own worldBound is unavailable (no layout pass has run yet), " +
                              "so every rect below is raw panel coordinates rather than window-relative and " +
                              "may be stale or zero.");
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool, $"ListUIElements: reading root worldBound failed ({ex.Message}).");
                notes.Add("the root element's own worldBound could not be read, so every rect below is raw " +
                          "panel coordinates rather than window-relative.");
            }

            var walk = new TreeWalk();
            WalkTree(root, 0, origin, walk);
            if (walk.NodeCapHit)
                notes.Add($"the walk STOPPED after {MaxWalkNodes} elements — the tree is larger than that and " +
                          "the rest was not visited, so the totals below are lower bounds.");
            if (walk.DepthCapHit)
                notes.Add($"at least one branch reached the depth cap of {MaxWalkDepth} and its deeper " +
                          "children were not visited.");

            // An IMGUI window and a UI Toolkit window with a handful of elements look the same in a row
            // count, so the shape of the tree is tested explicitly and said out loud.
            bool imguiOnly = LooksLikeImguiOnlyWindow(walk.Elements, out int imguiIndex);
            bool rootOnly = walk.Elements.Count <= 1;

            // A one-row tree has TWO causes that the shape alone cannot tell apart, and they need opposite
            // advice. Unity hands a window's OnGUI to an IMGUIContainer that the backend inserts into the
            // HOST VIEW's panel tree — a SIBLING of this window's root, not a child of it — so a classic
            // IMGUI window (Console, Project, Animation) walks to exactly one row, its own empty root, with
            // no IMGUIContainer in sight. That looks identical to a UI Toolkit window that was never shown.
            // Asking the type whether it declares OnGUI is what separates them: without it, an open, visibly
            // drawing Console gets told "the window has probably never been shown", and the caller re-opens
            // it and retries forever instead of learning the one fact that matters — that it is IMGUI.
            string onGuiOwner = null;
            bool? drawsWithOnGui = rootOnly ? TryFindOnGuiCallback(window, out onGuiOwner) : (bool?)null;
            bool onGuiDrawnRoot = rootOnly && drawsWithOnGui == true;

            // The filter is not applied on the two degenerate shapes below, so the header must not claim it
            // was: "1 match filter 'Button'" next to a note saying the filter was skipped is a contradiction
            // the reader has to resolve by guessing.
            string appliedFilter = string.Empty;

            List<ElementInfo> matched;
            if (imguiOnly)
            {
                matched = new List<ElementInfo> { walk.Elements[imguiIndex] };
                notes.Add("THIS WINDOW IS DRAWN WITH IMGUI AND HAS NO ELEMENT TREE, so ListUIElements cannot " +
                          "decompose its contents. Its rootVisualElement holds nothing but IMGUIContainer(s) — " +
                          "the one listed below covers the window — and every control inside is drawn " +
                          "imperatively by an OnGUI callback, existing nowhere as an object. Capture the pixels " +
                          "with CaptureEditorWindow and read the window's OnGUI source instead. The single row " +
                          "below is everything there is to report (the only other element is the root " +
                          "container itself); it is NOT a filtered or truncated result.");
                if (!string.IsNullOrWhiteSpace(filter))
                    notes.Add($"filter '{filter}' was NOT applied: the tree has only the IMGUIContainer row.");
            }
            else if (rootOnly)
            {
                matched = new List<ElementInfo>(walk.Elements);
                if (drawsWithOnGui == true)
                {
                    notes.Add("THIS WINDOW IS DRAWN WITH IMGUI AND HAS NO ELEMENT TREE, so ListUIElements " +
                              $"cannot decompose its contents: {onGuiOwner}.OnGUI draws every control " +
                              "imperatively and they exist nowhere as objects. Its rootVisualElement being " +
                              "empty is NORMAL for such a window and does NOT mean it has never been shown — " +
                              "the IMGUIContainer that runs OnGUI belongs to the HOST VIEW's panel tree, as a " +
                              "sibling of this window's root, so no walk starting at rootVisualElement can " +
                              "ever reach it. The single root row below is everything the element tree has to " +
                              "report; it is NOT a filtered or truncated result. Re-showing the window cannot " +
                              "surface the OnGUI drawing either, since it is not made of elements at all — " +
                              "that would only help if this window ALSO builds UI Toolkit content in " +
                              "CreateGUI, which a purely IMGUI window does not. Capture the pixels with " +
                              $"CaptureEditorWindow and read {onGuiOwner}.OnGUI instead.");
                }
                else if (drawsWithOnGui == false)
                {
                    notes.Add("this window's tree contains ONLY the root element, and its type declares no " +
                              "OnGUI method either, so it is not an IMGUI window. rootVisualElement is created " +
                              "on demand, so the usual cause is a window that has never been shown (or one " +
                              "that builds its UI in CreateGUI, which has not run yet). This is NOT 'the " +
                              "window has no UI'.");
                }
                else
                {
                    notes.Add("this window's tree contains ONLY the root element, and whether it draws through " +
                              "OnGUI is UNKNOWN (reflection over its type failed — see the editor log). The " +
                              "cause is therefore one of two, and this tool will not pick for you: either it " +
                              "is an IMGUI window, whose OnGUI IMGUIContainer lives on the host view's panel " +
                              "tree and is invisible from rootVisualElement, or it is a UI Toolkit window that " +
                              "has never been shown. Capture the pixels with CaptureEditorWindow to see which. " +
                              "This is NOT 'the window has no UI'.");
                }
                if (!string.IsNullOrWhiteSpace(filter))
                    notes.Add($"filter '{filter}' was NOT applied: the tree has only the root row.");
            }
            else if (string.IsNullOrWhiteSpace(filter))
            {
                matched = new List<ElementInfo>(walk.Elements);
            }
            else
            {
                appliedFilter = filter;
                matched = walk.Elements.Where(e => MatchesFilter(e, filter)).ToList();
            }

            var rows = matched.Count > maxElements ? matched.Take(maxElements).ToList() : matched;

            if (!annotate && (maxWidth > 0 || !string.IsNullOrWhiteSpace(saveToPath) ||
                              !string.Equals(opt.NormalizedFormat, "png", StringComparison.Ordinal) ||
                              jpgQuality != 90))
            {
                notes.Add("maxWidth / format / jpgQuality / saveToPath were IGNORED: they only apply to the " +
                          "image produced by annotate=true.");
            }

            string imageMessage = null;
            if (annotate)
            {
                if (imguiOnly || rootOnly)
                {
                    // Same skip, but the REASON has to match the diagnosis above: "only one box is available"
                    // is true of every branch here, while "the controls are drawn by OnGUI and are not
                    // elements at all" is the fact that tells the caller not to retry.
                    string why = onGuiDrawnRoot
                        ? $"this window's controls are drawn imperatively by {onGuiOwner}.OnGUI and are not " +
                          "elements, so there is no element rect to box"
                        : imguiOnly
                            ? "the tree holds nothing but IMGUIContainer(s) covering the window, so the only " +
                              "box available would be one rectangle around the whole window"
                            : "the tree holds only the root element, so the only box available would be one " +
                              "rectangle around the whole window";
                    notes.Add($"annotate=true was skipped: {why}, which says nothing. Use CaptureEditorWindow " +
                              "for the pixels.");
                }
                else if (rows.Count == 0)
                {
                    notes.Add(string.IsNullOrWhiteSpace(appliedFilter)
                        ? "annotate=true was skipped: there is no element to box."
                        : $"annotate=true was skipped: no element matches filter '{appliedFilter}', so there is " +
                          "nothing to box.");
                }
                else
                {
#if UNITY_EDITOR_WIN
                    imageMessage = BuildAnnotatedImage(window, title, rows, maxAnnotations, opt, notes);
#else
                    notes.Add("annotate=true was IGNORED: the annotated image needs the Win32 PrintWindow path, " +
                              "and this editor is not running on Windows. The listing below is complete and " +
                              "platform-independent; capture the window with your OS screenshot tool and use " +
                              "the rects below to locate elements in it.");
#endif
                }
            }

            return FormatReport(window, title, windowTitleContains, matchIndex, matches.Count, appliedFilter,
                                maxElements, walk, matched.Count, rows, notes, imageMessage);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Element model
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One row of the listing. <c>Rect</c> is only meaningful when <c>HasRect</c> is true; a missing rect
        /// is reported as "unavailable" rather than as (0,0,0x0), because a zero rect looks like a real
        /// measurement of a collapsed element and would place an annotation box at the window's corner.
        /// </summary>
        private sealed class ElementInfo
        {
            public int Index;
            public int Depth;
            public string TypeName = "unknown";
            public string Name = string.Empty;
            public string Text = string.Empty;
            public Rect Rect;
            public bool HasRect;
            public bool IsImguiContainer;

            /// <summary>False when enabled / visible / display could not be read; they then print as unknown.</summary>
            public bool StateKnown;
            public bool Enabled;
            public bool Visible;
            public bool DisplayNone;

            /// <summary>True only when BOTH the box and its number landed inside the attached image.</summary>
            public bool Boxed;
        }

        private sealed class TreeWalk
        {
            public readonly List<ElementInfo> Elements = new List<ElementInfo>();
            public bool NodeCapHit;
            public bool DepthCapHit;
            public int DeepestDepth;
        }

        /// <summary>
        /// Depth-first pre-order walk over <c>VisualElement.hierarchy</c> — the REAL parent/child tree.
        ///
        /// Not <c>Children()</c> / the indexer: those are redirected through contentContainer, so a
        /// ScrollView reports the elements you added and hides its own viewport, content container and
        /// scrollers. Those hidden elements are exactly the ones that explain "my row is laid out at y=900
        /// but the window is 400 tall", so the debugging tool has to see them. It is the same tree the UI
        /// Toolkit Debugger shows.
        /// </summary>
        private static void WalkTree(VisualElement element, int depth, Vector2 origin, TreeWalk walk)
        {
            if (element == null) return;
            if (walk.Elements.Count >= MaxWalkNodes)
            {
                walk.NodeCapHit = true;
                return;
            }

            walk.Elements.Add(Describe(element, depth, origin, walk.Elements.Count));
            if (depth > walk.DeepestDepth) walk.DeepestDepth = depth;

            if (depth >= MaxWalkDepth)
            {
                walk.DepthCapHit = true;
                return;
            }

            int childCount;
            try
            {
                childCount = element.hierarchy.childCount;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: reading hierarchy.childCount of {element.GetType().Name} failed ({ex.Message}).");
                return;
            }

            for (int i = 0; i < childCount; i++)
            {
                VisualElement child;
                try
                {
                    child = element.hierarchy[i];
                }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.Tool,
                        $"ListUIElements: reading child {i} of {element.GetType().Name} failed ({ex.Message}).");
                    continue;
                }
                WalkTree(child, depth + 1, origin, walk);
                if (walk.NodeCapHit) return;
            }
        }

        private static ElementInfo Describe(VisualElement element, int depth, Vector2 origin, int index)
        {
            var info = new ElementInfo
            {
                Index = index,
                Depth = depth,
                TypeName = element.GetType().Name,
                Name = Sanitize(element.name, MaxTextChars),
                Text = ReadVisibleText(element),
                IsImguiContainer = element is IMGUIContainer,
            };

            try
            {
                Rect world = element.worldBound;
                if (IsFinite(world))
                {
                    info.Rect = new Rect(world.x - origin.x, world.y - origin.y, world.width, world.height);
                    info.HasRect = true;
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: reading worldBound of {info.TypeName} failed ({ex.Message}).");
            }

            try
            {
                info.Enabled = element.enabledInHierarchy;
                info.Visible = element.visible;
                info.DisplayNone = element.resolvedStyle.display == DisplayStyle.None;
                info.StateKnown = true;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: reading the state of {info.TypeName} failed ({ex.Message}); it is " +
                    "reported as unknown.");
                info.StateKnown = false;
            }

            return info;
        }

        /// <summary>
        /// The element's own visible text, by TYPE first and reflection only as a last resort.
        ///
        /// - <see cref="TextElement"/> covers Label, Button and every other text-drawing element: its
        ///   <c>text</c> IS what the user reads on screen.
        /// - <see cref="Foldout"/> keeps its caption in <c>text</c> as well, but is not a TextElement.
        /// - Everything else (Toggle, TextField and the rest of the BaseField family) exposes its caption as
        ///   <c>label</c>, declared on the generic base <c>BaseField&lt;T&gt;</c>. There is no non-generic
        ///   base to cast to, so that one is read by cached reflection.
        ///
        /// An element with no text at all returns empty — that is a real answer, not a failure. Note that a
        /// text field's current VALUE surfaces on its inner text element's row, because that element's text
        /// is the value; the field's own row carries the label.
        /// </summary>
        private static string ReadVisibleText(VisualElement element)
        {
            try
            {
                if (element is TextElement textElement) return Sanitize(textElement.text, MaxTextChars);
                if (element is Foldout foldout) return Sanitize(foldout.text, MaxTextChars);
                return Sanitize(ReadTextByReflection(element), MaxTextChars);
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: reading the text of {element.GetType().Name} failed ({ex.Message}).");
                return string.Empty;
            }
        }

        // One lookup per concrete element type per domain. A miss is cached as null on purpose: most element
        // types have neither property, and re-walking their base chain for every row of a 500-element tree
        // is pure waste. Cleared by the domain reload, so a Unity upgrade re-probes.
        private static readonly Dictionary<Type, PropertyInfo> TextPropertyCache =
            new Dictionary<Type, PropertyInfo>();

        private static string ReadTextByReflection(VisualElement element)
        {
            Type type = element.GetType();
            PropertyInfo property;
            if (!TextPropertyCache.TryGetValue(type, out property))
            {
                property = FindStringProperty(type, "label") ?? FindStringProperty(type, "text");
                TextPropertyCache[type] = property;
            }
            if (property == null) return null;

            try
            {
                return property.GetValue(element, null) as string;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: reading {type.Name}.{property.Name} failed ({ex.Message}).");
                return null;
            }
        }

        // DeclaredOnly up the chain: BaseField<T>.label is declared on a CONSTRUCTED generic base
        // (BaseField<bool> for a Toggle), which a plain GetProperty on the derived type does find, but the
        // explicit walk also copes with a version that shadows the member — and it can never pick an
        // indexer, which GetProperty would happily return for a type with a string indexer.
        private static PropertyInfo FindStringProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead && property.PropertyType == typeof(string) &&
                    property.GetIndexParameters().Length == 0)
                {
                    return property;
                }
            }
            return null;
        }

        /// <summary>
        /// True when the tree is "root + IMGUIContainer(s) and nothing else", i.e. the window's own root holds
        /// nothing but imperative drawing surfaces and has no element structure to report.
        ///
        /// This catches the window that PUT an IMGUIContainer under its own root (a custom window wrapping an
        /// inspector drawer, a hybrid window). It does NOT catch a classic OnGUI window: Unity's backend
        /// parents that window's IMGUIContainer to the HOST VIEW's panel tree, as a sibling of the window's
        /// root, so it never appears in this walk at all — such a window arrives here with a single row and is
        /// identified by <see cref="TryFindOnGuiCallback"/> instead. (Were it a child of the root, the
        /// internal EditorWindow.isUIToolkitWindow — literally m_UIRootElement.childCount &gt; 0 — would be
        /// true for every IMGUI window, which it is not.)
        ///
        /// Tested by shape rather than by counting rows: a UI Toolkit window with two elements and a wrapper
        /// window produce the same row count, and a UI Toolkit window that merely embeds an IMGUIContainer
        /// among real elements must NOT be written off as undecomposable.
        /// </summary>
        private static bool LooksLikeImguiOnlyWindow(List<ElementInfo> elements, out int imguiIndex)
        {
            imguiIndex = -1;
            if (elements == null || elements.Count < 2) return false;

            for (int i = 1; i < elements.Count; i++)
            {
                if (!elements[i].IsImguiContainer) return false;
                if (imguiIndex < 0) imguiIndex = i;
            }
            return imguiIndex >= 0;
        }

        /// <summary>
        /// Whether this window paints through an OnGUI callback: true / false when its type chain could be
        /// inspected, null when reflection threw. <paramref name="declaringType"/> names the type that declares
        /// the OnGUI, so the note can point the reader at the exact source file to read.
        ///
        /// This is the ONLY signal that separates "an IMGUI window, whose element tree is empty BY DESIGN"
        /// from "a UI Toolkit window that has not built its UI yet" — both walk to a single root row, and the
        /// advice for one is useless for the other. Unity's own isUIToolkitWindow cannot help: it is just
        /// m_UIRootElement.childCount &gt; 0, i.e. the very row count that is ambiguous here.
        ///
        /// OnGUI is a Unity message, not an override, so it is usually PRIVATE and may be declared anywhere
        /// between the window's own class and EditorWindow (a subclass of a drawing base inherits it) — hence
        /// the explicit walk with DeclaredOnly, which a single GetMethod would miss for a private member on a
        /// base type. Parameterless only, so an unrelated OnGUI(Rect) overload cannot be mistaken for the
        /// message. The walk stops AT EditorWindow: that class declares no OnGUI, and going further would only
        /// reach ScriptableObject.
        /// </summary>
        private static bool? TryFindOnGuiCallback(EditorWindow window, out string declaringType)
        {
            declaringType = null;
            if (window == null) return null;

            try
            {
                for (Type t = window.GetType(); t != null && t != typeof(EditorWindow); t = t.BaseType)
                {
                    MethodInfo method = t.GetMethod("OnGUI",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly,
                        null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        declaringType = t.Name;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: probing {window.GetType().Name} for an OnGUI callback failed " +
                    $"({ex.Message}); whether the window is IMGUI-drawn is reported as unknown.");
                return null;
            }
        }

        private static bool MatchesFilter(ElementInfo element, string filter)
        {
            return element.TypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (element.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (element.Text ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Report
        // ─────────────────────────────────────────────────────────────────────────

        private static string FormatReport(EditorWindow window, string title, string titleQuery, int matchIndex,
                                           int matchCount, string filter, int maxElements, TreeWalk walk,
                                           int matchedCount, List<ElementInfo> rows, List<string> notes,
                                           string imageMessage)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"UI elements of EditorWindow \"{title}\" [{window.GetType().Name}]");
            if (matchCount > 1)
                sb.AppendLine($"matchIndex={matchIndex} of {matchCount} windows whose title contains '{titleQuery}'.");

            sb.Append($"tree: {walk.Elements.Count} element(s) walked (max depth {walk.DeepestDepth})");
            sb.Append(string.IsNullOrWhiteSpace(filter)
                ? ", no filter"
                : $", {matchedCount} match filter '{filter}'");
            sb.AppendLine($", {rows.Count} shown. Depth-first, VisualElement.hierarchy order.");

            if (matchedCount > rows.Count)
            {
                sb.AppendLine($"NOTE: the listing was TRUNCATED at maxElements={maxElements} — " +
                              $"{matchedCount - rows.Count} further matching element(s) are not shown. Raise " +
                              "maxElements or narrow filter; nothing below is a complete picture of the window.");
            }

            sb.AppendLine("Row: [index]<'*' when the element is boxed in the attached image> d<depth, root=d0> " +
                          "[type] name text rect enabled visible.");
            sb.AppendLine("rect=(x,y,WxH) is WINDOW-RELATIVE in Unity logical POINTS: origin is the top-left " +
                          "of EditorWindow.position (the window's own drawing area, what CaptureEditorWindow " +
                          "captures; the dock tab strip is outside it) and y grows DOWNWARD. From worldBound, " +
                          "not layout. Physical pixels of a capture are pixelsPerPoint times these numbers.");
            sb.AppendLine("Read-only: nothing here was clicked, focused, repainted or modified.");

            foreach (string note in notes) sb.AppendLine("NOTE: " + note);

            sb.AppendLine("---");
            if (rows.Count == 0)
            {
                // Never let an empty row block stand on its own: it reads as "this window has no UI", which is
                // the one conclusion the walk has already disproved.
                sb.AppendLine(string.IsNullOrWhiteSpace(filter)
                    ? $"(nothing to list, yet {walk.Elements.Count} element(s) were walked — the tree is NOT " +
                      "empty. Please report this: it means the listing lost rows it had already collected.)"
                    : $"(no element matches filter '{filter}' — {walk.Elements.Count} element(s) were walked, " +
                      "so the tree is NOT empty. Drop or widen the filter to see them.)");
            }
            foreach (ElementInfo element in rows) sb.AppendLine(FormatRow(element));

            if (!string.IsNullOrEmpty(imageMessage))
            {
                sb.AppendLine("---");
                sb.AppendLine(imageMessage);
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatRow(ElementInfo element)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(element.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            sb.Append(element.Boxed ? "* " : "  ");
            sb.Append('d').Append(element.Depth.ToString(CultureInfo.InvariantCulture));
            sb.Append(" [").Append(element.TypeName).Append(']');
            sb.Append(string.IsNullOrEmpty(element.Name) ? " name=(none)" : $" name=\"{element.Name}\"");
            if (!string.IsNullOrEmpty(element.Text)) sb.Append($" text=\"{element.Text}\"");
            sb.Append(element.HasRect
                ? $" rect=({element.Rect.x:F0},{element.Rect.y:F0},{element.Rect.width:F0}x{element.Rect.height:F0})"
                : " rect=unavailable(no layout yet)");
            if (element.StateKnown)
            {
                sb.Append(element.Enabled ? " enabled=yes" : " enabled=no");
                sb.Append(element.Visible ? " visible=yes" : " visible=no");
                if (element.DisplayNone) sb.Append(" display=none(not laid out)");
            }
            else
            {
                sb.Append(" enabled=unknown visible=unknown");
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Shared small helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A rect is only usable when all four components are real numbers. UI Toolkit reports NaN for an
        /// element that has never been laid out, and Mathf.RoundToInt(NaN) is 0 — so without this check a
        /// never-laid-out element would be drawn as a box in the window's corner and reported as measured.
        /// </summary>
        private static bool IsFinite(Rect r)
        {
            return !float.IsNaN(r.x) && !float.IsNaN(r.y) && !float.IsNaN(r.width) && !float.IsNaN(r.height)
                && !float.IsInfinity(r.x) && !float.IsInfinity(r.y)
                && !float.IsInfinity(r.width) && !float.IsInfinity(r.height);
        }

        // Element text is arbitrary user content: a newline would split one element across two rows of the
        // listing and a quote would break the "..." quoting, both of which make the output unparseable.
        // Truncation is marked with "..." so a cut label cannot be mistaken for the whole string.
        private static string Sanitize(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(Math.Min(text.Length, maxChars) + 3);
            for (int i = 0; i < text.Length; i++)
            {
                if (i >= maxChars)
                {
                    sb.Append("...");
                    break;
                }
                char c = text[i];
                if (c == '\n' || c == '\r' || c == '\t') c = ' ';
                else if (c == '"') c = '\'';
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string TitleOf(EditorWindow window)
        {
            if (window == null) return "(destroyed)";
            GUIContent content = window.titleContent;
            return content != null && !string.IsNullOrEmpty(content.text) ? content.text : "(untitled)";
        }

        // Own copy rather than a shared helper: WindowCaptureTools' equivalent is private to that file (and
        // the file is Windows-only), while this listing has to work on every platform.
        private static List<EditorWindow> EnumerateValidEditorWindows()
        {
            var all = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var list = new List<EditorWindow>(all.Length);
            foreach (EditorWindow w in all)
            {
                if (w == null) continue;
                if (w.GetType().IsAbstract) continue;
                if (w.titleContent == null || string.IsNullOrEmpty(w.titleContent.text)) continue;
                if (w.position.width <= 0 || w.position.height <= 0) continue;
                list.Add(w);
            }
            return list;
        }

#if UNITY_EDITOR_WIN
        // ─────────────────────────────────────────────────────────────────────────
        // Annotation (Windows only — it needs Win32 PrintWindow)
        // ─────────────────────────────────────────────────────────────────────────

        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // The colour carries NO meaning: it only keeps two overlapping boxes apart, and each box's number is
        // drawn in the same colour so a number can be traced back to its own outline. The NUMBER is the
        // identity, and it is the element's index in the listing above.
        private static readonly Color[] BoxPalette =
        {
            new Color(1f, 0.18f, 0.60f),   // magenta
            new Color(0.10f, 0.90f, 1f),   // cyan
            new Color(1f, 0.80f, 0.05f),   // amber
            new Color(0.35f, 1f, 0.30f),   // green
        };

        /// <summary>
        /// The window's picture with a numbered box over each row. Returns the CaptureCommon result sentence
        /// (which starts with "Success:") or null when no image could be produced — in which case the reason
        /// is appended to <paramref name="notes"/>, never swallowed. Sets <see cref="ElementInfo.Boxed"/> on
        /// the elements whose box AND number actually landed inside the image, so the listing's '*' markers
        /// describe what is really there rather than what was attempted.
        /// </summary>
        private static string BuildAnnotatedImage(EditorWindow window, string title, List<ElementInfo> rows,
                                                 int maxAnnotations, CaptureOptions opt, List<string> notes)
        {
            // A background tab is not drawn anywhere, so PrintWindow would return the dock area showing
            // whichever tab IS in front — and boxes from this window's layout over another window's pixels
            // is exactly the plausible-looking wrong answer this package refuses to produce.
            bool? activeTab = TryIsActiveTab(window);
            if (activeTab == false)
            {
                notes.Add("annotate=true was REFUSED: this window is not the active tab of its dock, so it is " +
                          "not being drawn and the boxes would land on the tab that is in front. Bring the tab " +
                          "forward yourself (or capture it with CaptureEditorWindow(focusless=false), which " +
                          "takes the user's focus) and retry. The listing itself is unaffected.");
                return null;
            }
            if (activeTab == null)
            {
                notes.Add("whether this window is the front tab of its dock could not be determined (Unity's " +
                          "internal HostView reflection did not resolve on this version) — if the image shows " +
                          "a different window than the listing, that is why.");
            }

            var shot = default(WindowShot);
            bool published = false;
            try
            {
                if (!TryShootWindowArea(window, out shot, out string shotError))
                {
                    notes.Add($"no annotated image: {shotError}");
                    return null;
                }

                var targets = new List<ElementInfo>();
                int noRect = 0, empty = 0, beyondCap = 0;
                foreach (ElementInfo element in rows)
                {
                    if (!element.HasRect) { noRect++; continue; }
                    if (element.Rect.width < 1f || element.Rect.height < 1f) { empty++; continue; }
                    if (targets.Count >= maxAnnotations) { beyondCap++; continue; }
                    targets.Add(element);
                }

                if (targets.Count == 0)
                {
                    notes.Add($"no annotated image: none of the {rows.Count} listed element(s) has a non-empty " +
                              $"rect to box ({noRect} have no layout yet, {empty} are zero-sized).");
                    return null;
                }

                // Annotations are burned in BEFORE CaptureCommon downscales for maxWidth, so a 3000px-wide
                // capture squeezed to 1200 would shrink a 2x glyph to about 5px and a 2px outline to under
                // one pixel. Pre-multiply both by the downscale factor so what is attached stays readable.
                float shrink = 1f;
                if (opt.MaxWidth > 0)
                {
                    int longer = Mathf.Max(shot.Texture.width, shot.Texture.height);
                    if (longer > opt.MaxWidth) shrink = (float)longer / opt.MaxWidth;
                }
                int labelScale = Mathf.Clamp(Mathf.RoundToInt(2f * shrink), 2, 8);
                int thickness = Mathf.Clamp(Mathf.RoundToInt(2f * shrink), 2, 6);

                int boxesDrawn = 0, numbersDrawn = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    ElementInfo element = targets[i];
                    Color color = BoxPalette[i % BoxPalette.Length];

                    // Logical points -> physical pixels of the captured area. ShiftX/ShiftY remove the part
                    // of the window that fell outside the container bitmap, so x=0 of an element is x=0 of
                    // the image even when the left edge was clipped away.
                    float px = element.Rect.x * shot.ScaleX - shot.ShiftX;
                    float py = element.Rect.y * shot.ScaleY - shot.ShiftY;
                    float pw = element.Rect.width * shot.ScaleX;
                    float ph = element.Rect.height * shot.ScaleY;

                    // Window space (top-left origin) -> texture space (bottom-left origin), through the one
                    // helper allowed to write that flip. A hand-written "height - y" here is how a tool ends
                    // up boxing the mirrored half of the picture and still reporting success.
                    Rect flipped = CaptureCommon.RectTopLeftToBottomLeft(new Rect(px, py, pw, ph),
                                                                        shot.Texture.height);
                    int bx = Mathf.RoundToInt(flipped.x);
                    int by = Mathf.RoundToInt(flipped.y);
                    int bw = Mathf.Max(1, Mathf.RoundToInt(flipped.width));
                    int bh = Mathf.Max(1, Mathf.RoundToInt(flipped.height));

                    bool box = CaptureCommon.DrawRect(shot.Texture, bx, by, bw, bh, color, thickness,
                                                      apply: false);

                    string label = element.Index.ToString(CultureInfo.InvariantCulture);
                    CaptureCommon.MeasureText(label, labelScale, out _, out int textHeight);
                    // Inside the box, hugging its top edge, so the number reads as belonging to that box.
                    // A box too short to hold the number gets it just above instead of on top of the next
                    // element's outline.
                    int ly = bh >= textHeight + thickness + 2
                        ? by + bh - textHeight - thickness - 1
                        : by + bh + 1;
                    bool number = CaptureCommon.DrawTextWithBackground(shot.Texture, bx + thickness + 1, ly,
                        label, color, new Color(0f, 0f, 0f, 0.75f), labelScale, 2, apply: false);

                    if (box) boxesDrawn++;
                    if (number) numbersDrawn++;
                    element.Boxed = box && number;
                }

                try
                {
                    shot.Texture.Apply(false, false);
                }
                catch (Exception ex)
                {
                    notes.Add($"no annotated image: uploading the annotated pixels failed ({ex.Message}).");
                    return null;
                }

                string imageLabel = $"UI element boxes over EditorWindow '{title}' " +
                                    $"[{targets.Count} boxes, numbers match the listing; {shot.How}]";
                string message = CaptureCommon.Finish(shot.Texture, opt, imageLabel, CaptureRoute.Window,
                                                      out string finishError);
                if (message == null)
                {
                    notes.Add($"no annotated image: {finishError}");
                    return null;
                }

                // Only now that an image really exists may the drawing statistics be reported, and only now
                // may the '*' markers survive (see the finally below).
                var summary = new List<string>();
                summary.Add($"{boxesDrawn} of {targets.Count} box(es) landed inside the image");
                if (numbersDrawn < boxesDrawn)
                    summary.Add($"{boxesDrawn - numbersDrawn} box(es) have a number that fell outside the " +
                                "image and are therefore NOT marked with '*' above");
                if (beyondCap > 0)
                    summary.Add($"{beyondCap} listed element(s) were NOT boxed because maxAnnotations=" +
                                $"{maxAnnotations} was reached — the boxed ones are the first {targets.Count} " +
                                "in the listing order, and every box carries its listing number");
                if (noRect > 0) summary.Add($"{noRect} listed element(s) have no layout yet and cannot be boxed");
                if (empty > 0) summary.Add($"{empty} listed element(s) are zero-sized and cannot be boxed");
                summary.Add("the bitmap is the window's LAST PAINTED content while the boxes come from its " +
                            "CURRENT layout, so they can disagree if the window has not repainted since");
                notes.Add("annotated image: " + string.Join("; ", summary) + ".");

                published = true;
                return message;
            }
            catch (Exception ex)
            {
                notes.Add($"no annotated image: annotating threw ({ex.Message}).");
                return null;
            }
            finally
            {
                // A '*' in the listing promises "this element's box and number ARE in the attached image".
                // If encoding, attaching or anything else after the drawing loop failed there is no image at
                // all, so the markers have to come back off — a listing full of stars next to "no annotated
                // image" is precisely the plausible-looking lie this package refuses to ship.
                if (!published)
                {
                    foreach (ElementInfo element in rows) element.Boxed = false;
                }

                // CaptureCommon.Finish does not take ownership (destroySource defaults to false), so the
                // texture is released here on every path including the exception ones.
                if (shot.Texture != null) UnityEngine.Object.DestroyImmediate(shot.Texture);
            }
        }

        /// <summary>
        /// A captured EditorWindow area plus everything needed to place a box on it. Owned by the caller:
        /// <see cref="Texture"/> must be DestroyImmediate'd.
        /// </summary>
        private struct WindowShot
        {
            /// <summary>RGBA32, bottom-up (Texture2D convention), cropped to the EditorWindow's own rect.</summary>
            public Texture2D Texture;
            /// <summary>Physical pixels per Unity logical point, horizontally / vertically.</summary>
            public float ScaleX;
            public float ScaleY;
            /// <summary>Physical pixels of the window lost off the LEFT / TOP edge of the container bitmap.</summary>
            public int ShiftX;
            public int ShiftY;
            /// <summary>How the pixels were obtained, for the result message.</summary>
            public string How;
        }

        /// <summary>
        /// PrintWindows the OS window an EditorWindow lives in and cuts out that window's own rect.
        ///
        /// A DOCKED EditorWindow has no HWND of its own — the whole Unity dock is one OS window — so the
        /// only focus-free way to get its pixels is to photograph the container and crop. The crop offset is
        /// (the EditorWindow's physical screen rect) minus (the container's physical screen rect); using
        /// desktop coordinates directly inside a window-local bitmap would land on an arbitrary part of the
        /// editor and still look like a successful capture.
        ///
        /// There is deliberately NO BitBlt fallback (unlike CaptureEditorWindow): a screen-rect grab
        /// photographs the desktop, so a window lying over the editor would be boxed as if it were this
        /// window's own UI, and raising Unity to avoid that would steal the user's focus for a read-only
        /// query.
        /// </summary>
        private static bool TryShootWindowArea(EditorWindow window, out WindowShot shot, out string error)
        {
            shot = default(WindowShot);
            error = null;

            Rect posPt = window.position;
            if (posPt.width < 1f || posPt.height < 1f)
            {
                error = $"the window rect is empty ({posPt.width:F0}x{posPt.height:F0}) — it may be minimized " +
                        "or closing";
                return false;
            }

            using (new WindowCaptureNative.DpiScope())
            {
                var monitors = WindowCaptureNative.EnumerateMonitors();
                var (sx, sy, sw, sh) = WindowCaptureNative.UnityRectToPhysical(
                    posPt.x, posPt.y, posPt.width, posPt.height, monitors);
                if (sw <= 0 || sh <= 0)
                {
                    error = $"the window rect converted to an empty physical rect ({sw}x{sh})";
                    return false;
                }

                IntPtr hwnd = ResolveOwningWindow(new RectInt(sx, sy, sw, sh), out string how, out error);
                if (hwnd == IntPtr.Zero) return false;

                if (!WindowCaptureNative.TryGetWindowRect(hwnd, out int wx, out int wy, out int ww, out int wh,
                                                          out string rectError))
                {
                    error = $"the container window's rect could not be read ({rectError}), so the crop offset " +
                            "is unknown";
                    return false;
                }

                if (!WindowCaptureNative.TryPrintWindow(hwnd, out byte[] bgra, out int bw, out int bh,
                                                        out string printError))
                {
                    error = $"PrintWindow declined ({printError}). This tool does not fall back to a " +
                            "screen-rect BitBlt, because that route photographs the desktop and would box " +
                            "whatever is lying over the editor as if it were this window's UI. Capture the " +
                            "window with CaptureEditorWindow instead and use the rects above by hand";
                    return false;
                }

                if (bw != ww || bh != wh)
                {
                    error = $"the container window changed size between being measured ({ww}x{wh}) and being " +
                            $"captured ({bw}x{bh}), so every box would be offset — retry";
                    return false;
                }

                // Frame of reference: the bitmap's top-left pixel is (wx, wy) on the desktop.
                int cx = sx - wx;
                int cy = sy - wy;
                int clipX = Mathf.Max(0, cx);
                int clipY = Mathf.Max(0, cy);
                int clipW = Mathf.Min(bw, cx + sw) - clipX;
                int clipH = Mathf.Min(bh, cy + sh) - clipY;
                if (clipW <= 0 || clipH <= 0)
                {
                    error = $"the window's own rect (x={cx},y={cy},{sw}x{sh}) lies entirely outside the " +
                            $"{bw}x{bh} bitmap of the OS window it was matched to — the window moved, or the " +
                            "wrong container was identified. Retry, and check hwnd in ListEditorWindows";
                    return false;
                }

                Texture2D texture = BuildTexture(bgra, bw, bh, clipX, clipY, clipW, clipH, out string buildError);
                if (texture == null)
                {
                    error = buildError;
                    return false;
                }

                shot.Texture = texture;
                shot.ScaleX = sw / posPt.width;
                shot.ScaleY = sh / posPt.height;
                shot.ShiftX = clipX - cx;
                shot.ShiftY = clipY - cy;
                shot.How = how;
                if (clipW != sw || clipH != sh)
                {
                    shot.How += $"; the window area was clipped to {clipW}x{clipH} of {sw}x{sh} by the edge of " +
                                "the container bitmap, so elements in the missing strip have no box";
                }
                return true;
            }
        }

        /// <summary>
        /// Which OS window owns the pixels of a rect: the SMALLEST visible window of this Unity process that
        /// covers at least 90% of it, else the Unity main window.
        ///
        /// Smallest, because a floating EditorWindow sits on top of the main window and both contain its
        /// rect. This is a geometric HEURISTIC — a floating Unity window parked over the target would be
        /// picked instead — so the reasoning is returned in <paramref name="how"/> and travels with the
        /// image rather than being hidden. (CaptureEditorWindow does better by matching Unity's internal
        /// ContainerWindow rect; that machinery is private to WindowCaptureTools and is not worth
        /// re-implementing for an overlay whose correctness the reader can see in the picture.)
        /// </summary>
        private static IntPtr ResolveOwningWindow(RectInt subject, out string how, out string error)
        {
            how = null;
            error = null;

            long subjectArea = (long)subject.width * subject.height;
            if (subjectArea <= 0)
            {
                error = $"the window rect is empty ({subject.width}x{subject.height})";
                return IntPtr.Zero;
            }

            IntPtr best = IntPtr.Zero;
            double bestArea = double.MaxValue;
            double bestCoverage = 0;
            // includeInvisible:true plus a manual visibility filter: a floating Unity container can have no
            // caption at all, and the caption filter inside EnumerateTopLevelWindows would drop exactly it.
            foreach (var d in WindowCaptureNative.EnumerateTopLevelWindows(includeInvisible: true))
            {
                if (!d.IsUnity || !d.IsVisible || d.IsMinimized) continue;
                if (d.Width <= 0 || d.Height <= 0) continue;

                double coverage = (double)IntersectArea(subject, new RectInt(d.X, d.Y, d.Width, d.Height))
                                / subjectArea;
                if (coverage < 0.9) continue;
                double area = (double)d.Width * d.Height;
                if (area >= bestArea) continue;

                bestArea = area;
                bestCoverage = coverage;
                best = d.Hwnd;
            }

            if (best != IntPtr.Zero)
            {
                how = $"PrintWindow(PW_RENDERFULLCONTENT) of 0x{best.ToInt64():X8}, the smallest visible Unity " +
                      $"OS window covering {bestCoverage * 100.0:F0}% of this window's rect (HEURISTIC: a " +
                      "floating Unity window lying over the target could have been picked instead — check the " +
                      "image), cropped to the window's own rect";
                return best;
            }

            IntPtr main = WindowCaptureNative.GetUnityMainWindow(out string mainError);
            if (main == IntPtr.Zero)
            {
                error = "no visible Unity OS window covers this window's rect and the Unity main window handle " +
                        $"is unavailable ({mainError})";
                return IntPtr.Zero;
            }
            how = $"PrintWindow(PW_RENDERFULLCONTENT) of the Unity main window 0x{main.ToInt64():X8} as a " +
                  "fallback — no enumerated Unity OS window covers this window's rect (the editor may be " +
                  "minimized), so verify the image really shows the window you asked for";
            return main;
        }

        /// <summary>
        /// Turns a sub-rectangle of a BGRA32 capture buffer into an RGBA32 Texture2D that can be drawn into.
        ///
        /// Three conversions happen here, and each one silently produces a plausible wrong image if skipped:
        /// - <paramref name="cropX"/> / <paramref name="cropYTop"/> are WINDOW coordinates (top-left origin)
        ///   while the buffer rows are already bottom-up (WindowCaptureNative flips them to match
        ///   Texture2D), so the rectangle goes through CaptureCommon.RectTopLeftToBottomLeft — the only
        ///   place allowed to write that flip.
        /// - BGRA byte order becomes RGBA.
        /// - The alpha byte of a GDI 32bpp DIB is UNDEFINED and is forced to 255. Left alone, a driver that
        ///   returns zero there yields a fully transparent PNG that reads as a successful capture of nothing.
        ///
        /// RGBA32 rather than BGRA32 on purpose: Texture2D.GetPixel / SetPixel, which the annotation drawing
        /// uses, do not accept BGRA32.
        /// </summary>
        private static Texture2D BuildTexture(byte[] bgra, int bufferWidth, int bufferHeight,
                                              int cropX, int cropYTop, int cropW, int cropH, out string error)
        {
            error = null;

            long needed = (long)bufferWidth * bufferHeight * 4;
            if (bgra == null || bgra.Length < needed)
            {
                error = $"the captured buffer is {(bgra == null ? 0 : bgra.Length)} bytes but a " +
                        $"{bufferWidth}x{bufferHeight} BGRA32 bitmap needs {needed}";
                return null;
            }

            Rect flipped = CaptureCommon.RectTopLeftToBottomLeft(
                new Rect(cropX, cropYTop, cropW, cropH), bufferHeight);
            int bottom = Mathf.RoundToInt(flipped.y);

            if (cropW <= 0 || cropH <= 0 || cropX < 0 || bottom < 0 ||
                cropX + cropW > bufferWidth || bottom + cropH > bufferHeight)
            {
                error = $"the crop rect (x={cropX},y={cropYTop},{cropW}x{cropH}) does not fit inside the " +
                        $"{bufferWidth}x{bufferHeight} capture";
                return null;
            }

            var pixels = new Color32[cropW * cropH];
            for (int row = 0; row < cropH; row++)
            {
                int sourceBase = ((bottom + row) * bufferWidth + cropX) * 4;
                int targetBase = row * cropW;
                for (int col = 0; col < cropW; col++)
                {
                    int o = sourceBase + col * 4;
                    pixels[targetBase + col] = new Color32(bgra[o + 2], bgra[o + 1], bgra[o], 255);
                }
            }

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture;
            }
            catch (Exception ex)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                error = $"could not build a texture from the captured pixels: {ex.Message}";
                return null;
            }
        }

        private static long IntersectArea(RectInt a, RectInt b)
        {
            int x0 = Mathf.Max(a.x, b.x);
            int y0 = Mathf.Max(a.y, b.y);
            int x1 = Mathf.Min(a.x + a.width, b.x + b.width);
            int y1 = Mathf.Min(a.y + a.height, b.y + b.height);
            return (long)Mathf.Max(0, x1 - x0) * Mathf.Max(0, y1 - y0);
        }

        /// <summary>
        /// True / false when Unity's internals say whether this window is the frontmost tab of its dock,
        /// null when they could not be read. Null is a real answer and must stay distinguishable from false:
        /// guessing "yes" would let the annotation run against another tab's pixels, and guessing "no" would
        /// refuse a perfectly capturable window.
        ///
        /// HostView.actualView is internal, hence reflection; a miss is logged and degrades to null.
        /// </summary>
        private static bool? TryIsActiveTab(EditorWindow window)
        {
            try
            {
                FieldInfo parentField = FindFieldUpHierarchy(typeof(EditorWindow), "m_Parent");
                if (parentField == null)
                {
                    AgentLogger.Debug(LogTag.Tool,
                        "ListUIElements: EditorWindow.m_Parent not found on this Unity version; the active-tab " +
                        "check is unavailable.");
                    return null;
                }

                object host = parentField.GetValue(window);
                if (host == null) return null;
                // A destroyed HostView is still a live managed reference that Unity's == reports as null;
                // calling into it would throw MissingReferenceException.
                if (host is UnityEngine.Object hostObject && hostObject == null) return null;

                Type hostType = host.GetType();
                PropertyInfo actualProperty = FindPropertyUpHierarchy(hostType, "actualView");
                if (actualProperty != null && actualProperty.CanRead)
                    return ReferenceEquals(actualProperty.GetValue(host, null), window);

                FieldInfo actualField = FindFieldUpHierarchy(hostType, "m_ActualView");
                if (actualField != null)
                    return ReferenceEquals(actualField.GetValue(host), window);

                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: neither {hostType.Name}.actualView nor m_ActualView resolved; the " +
                    "active-tab check is unavailable.");
                return null;
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.Tool,
                    $"ListUIElements: the active-tab check failed ({ex.Message}); it is reported as unknown.");
                return null;
            }
        }

        // Private members declared on a BASE type are not returned by GetField/GetProperty on the derived
        // type, and m_Parent / actualView sit at different levels of the EditorWindow and HostView chains
        // depending on the Unity version, so the chain is walked explicitly.
        private static FieldInfo FindFieldUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindPropertyUpHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, InstanceAny | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
#endif
    }
}
