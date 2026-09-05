using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Pre-registered answers for modal dialogs that may appear while nobody is watching —
    /// the "Save changes?" from an EditorWindow, a package's popup during a Play-mode build.
    ///
    /// A background thread polls the OS for a modal (via <see cref="ModalDialogNative"/>) and,
    /// when one matches a rule, presses the registered button exactly the way
    /// AnswerModalDialog would. It never touches a Unity API: the whole point is to act while
    /// the main thread is held.
    ///
    /// Rules live in Library/UnityAgent/ModalAutoAnswers.json rather than in statics, because
    /// the scenario they exist for (entering Play mode) is itself a domain reload, which wipes
    /// every static. The file is re-read after each reload and the poller restarted.
    /// </summary>
    internal static class ModalAutoAnswer
    {
        internal sealed class Rule
        {
            public string Id;
            public string TitleContains;
            public string MessageContains;
            public string Button;
            public DateTime CreatedUtc;
            public DateTime ExpiresUtc;

            public bool Expired(DateTime nowUtc) => nowUtc >= ExpiresUtc;

            public string Describe(DateTime nowUtc)
            {
                double left = Math.Max(0, (ExpiresUtc - nowUtc).TotalSeconds);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(TitleContains)) parts.Add($"title~\"{TitleContains}\"");
                if (!string.IsNullOrEmpty(MessageContains)) parts.Add($"message~\"{MessageContains}\"");
                return $"[{Id}] {string.Join(" && ", parts)} → press '{Button}' (expires in {left:0}s)";
            }
        }

        public const int DefaultTtlSeconds = 600;
        public const int MaxTtlSeconds = 3600;
        const int PollIntervalMs = 250;

        static readonly object _lock = new object();
        static readonly List<Rule> _rules = new List<Rule>();
        static string _storePath;                 // set on the main thread; null until Initialize
        static volatile string _lastAutoAnswer;   // human-readable, survives reload via the file
        static Thread _poller;
        static volatile bool _stop;

        /// <summary>The most recent automatic press, or null. Safe from any thread.</summary>
        public static string LastAutoAnswer => _lastAutoAnswer;

        // ── lifecycle (main thread) ───────────────────────────────────────────

        /// <summary>Main thread only: resolves the store path, loads rules, restarts the poller.</summary>
        public static void Initialize()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                _storePath = Path.Combine(projectRoot, "Library", "UnityAgent", "ModalAutoAnswers.json");
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool, $"ModalAutoAnswer: cannot resolve store path: {ex.Message}");
                return;
            }
            Load();
            EnsurePollerIfNeeded();
        }

        /// <summary>Stops the poller before a domain reload; Initialize brings it back after.</summary>
        public static void Shutdown()
        {
            _stop = true;
            var t = _poller;
            _poller = null;
            if (t != null && t.IsAlive)
            {
                try { t.Join(500); } catch { /* best effort */ }
            }
        }

        // ── registry ─────────────────────────────────────────────────────────

        public static Rule Add(string titleContains, string messageContains, string button, int ttlSeconds)
        {
            ttlSeconds = Math.Max(1, Math.Min(MaxTtlSeconds, ttlSeconds));
            var now = DateTime.UtcNow;
            var rule = new Rule
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 6),
                TitleContains = titleContains ?? "",
                MessageContains = messageContains ?? "",
                Button = button,
                CreatedUtc = now,
                ExpiresUtc = now.AddSeconds(ttlSeconds),
            };
            lock (_lock)
            {
                PruneExpiredLocked(now);
                _rules.Add(rule);
                SaveLocked();
            }
            EnsurePollerIfNeeded();
            return rule;
        }

        /// <summary>Thread-safe copy of the live (non-expired) rules.</summary>
        public static List<Rule> Snapshot()
        {
            lock (_lock)
            {
                PruneExpiredLocked(DateTime.UtcNow);
                return new List<Rule>(_rules);
            }
        }

        public static int Clear()
        {
            lock (_lock)
            {
                int n = _rules.Count;
                _rules.Clear();
                SaveLocked();
                return n;
            }
        }

        static void PruneExpiredLocked(DateTime nowUtc)
        {
            int before = _rules.Count;
            _rules.RemoveAll(r => r.Expired(nowUtc));
            if (_rules.Count != before) SaveLocked();
        }

        // ── persistence (JSON via JNode; no Unity API) ────────────────────────

        static void Load()
        {
            try
            {
                if (_storePath == null || !File.Exists(_storePath)) return;
                var root = JNode.Parse(File.ReadAllText(_storePath));
                var loaded = new List<Rule>();
                var arr = root["rules"].AsArray;
                if (arr != null)
                {
                    foreach (var n in arr)
                    {
                        var r = new Rule
                        {
                            Id = n["id"].AsString ?? Guid.NewGuid().ToString("N").Substring(0, 6),
                            TitleContains = n["title"].AsString ?? "",
                            MessageContains = n["message"].AsString ?? "",
                            Button = n["button"].AsString ?? "",
                            CreatedUtc = ParseUtc(n["created"].AsString),
                            ExpiresUtc = ParseUtc(n["expires"].AsString),
                        };
                        if (!string.IsNullOrEmpty(r.Button)) loaded.Add(r);
                    }
                }
                string last = root["lastAutoAnswer"].AsString;
                lock (_lock)
                {
                    _rules.Clear();
                    _rules.AddRange(loaded);
                    if (!string.IsNullOrEmpty(last)) _lastAutoAnswer = last;
                    PruneExpiredLocked(DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool, $"ModalAutoAnswer: failed to load {_storePath}: {ex.Message}");
            }
        }

        static void SaveLocked()
        {
            if (_storePath == null) return;
            try
            {
                var items = new List<JNode>();
                foreach (var r in _rules)
                {
                    items.Add(JNode.Obj(
                        ("id", JNode.Str(r.Id)),
                        ("title", JNode.Str(r.TitleContains)),
                        ("message", JNode.Str(r.MessageContains)),
                        ("button", JNode.Str(r.Button)),
                        ("created", JNode.Str(r.CreatedUtc.ToString("o"))),
                        ("expires", JNode.Str(r.ExpiresUtc.ToString("o")))));
                }
                var root = JNode.Obj(
                    ("rules", JNode.Arr(items.ToArray())),
                    ("lastAutoAnswer", JNode.Str(_lastAutoAnswer ?? "")));
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath));
                File.WriteAllText(_storePath, root.ToJson());
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool, $"ModalAutoAnswer: failed to save {_storePath}: {ex.Message}");
            }
        }

        static DateTime ParseUtc(string s)
        {
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                return dt;
            return DateTime.UtcNow;   // unreadable timestamp → treat as just created / already due
        }

        // ── poller (background thread) ────────────────────────────────────────

        static void EnsurePollerIfNeeded()
        {
            if (!ModalDialogNative.IsSupported) return;
            lock (_lock)
            {
                if (_rules.Count == 0) return;
                if (_poller != null && _poller.IsAlive) return;
                _stop = false;
                _poller = new Thread(PollLoop) { IsBackground = true, Name = "UnityAgent.ModalAutoAnswer" };
                _poller.Start();
            }
        }

        static void PollLoop()
        {
            try
            {
                while (!_stop)
                {
                    List<Rule> rules;
                    lock (_lock)
                    {
                        PruneExpiredLocked(DateTime.UtcNow);
                        if (_rules.Count == 0) return;   // nothing left to wait for; Add restarts us
                        rules = new List<Rule>(_rules);
                    }

                    if (ModalDialogNative.TryDescribe(out var dialog, out _) && dialog != null)
                    {
                        var rule = FirstMatch(rules, dialog);
                        if (rule != null) Answer(rule, dialog);
                    }

                    Thread.Sleep(PollIntervalMs);
                }
            }
            catch (ThreadAbortException) { /* domain reload */ }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.Tool, $"ModalAutoAnswer poller stopped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        static Rule FirstMatch(List<Rule> rules, ModalDialogNative.DialogInfo d)
        {
            foreach (var r in rules)
            {
                if (!string.IsNullOrEmpty(r.TitleContains)
                    && (d.Title ?? "").IndexOf(r.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!string.IsNullOrEmpty(r.MessageContains)
                    && (d.Message ?? "").IndexOf(r.MessageContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                return r;
            }
            return null;
        }

        static void Answer(Rule rule, ModalDialogNative.DialogInfo dialog)
        {
            bool ok = Tools.ModalDialogTools.TryPress(dialog, rule.Button, out string what, out string error);
            bool dismissed = ok && ModalDialogNative.WaitForDismiss(dialog.Handle, 1500);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string record = ok
                ? $"{stamp} pressed {what} on \"{dialog.Title}\" (rule {rule.Id}, dismissed={(dismissed ? "true" : "false")})"
                : $"{stamp} FAILED to press '{rule.Button}' on \"{dialog.Title}\" (rule {rule.Id}): {error}";
            _lastAutoAnswer = record;
            lock (_lock) SaveLocked();

            // The Console is the only place an unattended run can find out afterwards that a
            // dialog was answered by a rule and not by a person.
            AgentLogger.Warning(LogTag.Tool, "ModalAutoAnswer " + record);

            // Don't hammer a dialog that rejected the input (or opened a follow-up): give it a
            // moment before the next poll can match it again.
            if (!dismissed) Thread.Sleep(3000);
        }
    }
}
