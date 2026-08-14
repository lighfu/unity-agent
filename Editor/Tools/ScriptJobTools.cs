using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Fire-and-poll execution for editor scripts that outlive a single MCP call.
    ///
    /// The transport abandons a call at 120 s (AgentMCPServer.DefaultCallTimeoutMs). Work that
    /// takes longer still completes — the editor keeps running it — but the caller never learns
    /// the result, so agents resort to writing results to a file and reading them back. These two
    /// tools remove that detour: start the work, get an id, poll for the outcome.
    ///
    /// What this does NOT do is move work off the main thread. Unity's API is main-thread only, so
    /// a long synchronous script still blocks the editor for its whole duration; the difference is
    /// only that the MCP call returns immediately. A script that returns an IEnumerator IS pumped
    /// one step per editor tick, which does keep the editor responsive — prefer that shape for
    /// anything genuinely long.
    /// </summary>
    public static class ScriptJobTools
    {
        /// <summary>
        /// Jobs kept in memory, finished ones included, so a caller that polls late still gets an
        /// answer. Bounded because nothing else evicts them.
        /// </summary>
        const int MaxRetainedJobs = 32;

        /// <summary>How long a finished job stays readable before it can be evicted.</summary>
        const double RetentionSeconds = 1800;

        const int DefaultTimeoutSeconds = 600;
        const int MaxTimeoutSeconds = 3600;

        sealed class ScriptJob
        {
            public string Id;
            public string CodePreview;
            public MethodInfo Entry;
            public IEnumerator Routine;      // non-null once a script has returned an IEnumerator
            public bool Started;
            public bool Done;
            public string Result;
            public string Error;
            public double StartedAt;
            public double FinishedAt;
            public double TimeoutSeconds;
            public int ConsoleBaseline;
            public int Steps;
        }

        static readonly Dictionary<string, ScriptJob> _jobs = new Dictionary<string, ScriptJob>(StringComparer.Ordinal);
        static readonly List<ScriptJob> _runnable = new List<ScriptJob>();
        static bool _driverRegistered;

        [AgentTool(@"Start a C# editor script and return immediately with a job id, instead of holding
the MCP call open until it finishes.

Use this when the work may take longer than ~100 s — bulk conversions, mass reimports, anything
that has previously come back as 'Timeout'. The transport gives up at 120 s while the editor keeps
working, so without this the result is simply unreachable.

Compilation happens SYNCHRONOUSLY here: a compile error comes back from this call, not from
GetJobResult. Only execution is deferred. Arguments are identical to RunEditorScript.

Shape of the script matters:
  return <value>;              runs to completion in one tick. The editor is frozen for the whole
                               duration — fine for a long-but-uninterruptible operation, but you
                               get no progress and Unity looks hung.
  return <IEnumerator>;        pumped one step per editor tick. The editor stays responsive and
                               GetJobResult can report how many steps have run. Prefer this.
                               e.g.  return Steps(); ... static IEnumerator Steps() { ...; yield return null; ... }

timeoutSeconds: abandon the job after this long (default 600, max 3600). A synchronous script
  cannot be interrupted mid-call — the timeout is only observed between ticks.

DOES NOT SURVIVE A DOMAIN RELOAD. If the script recompiles scripts or triggers a reload, the job
registry is wiped along with every other static, and GetJobResult will report the id as unknown.
Same risk tier as RunEditorScript: this is arbitrary code execution.",
            Risk = ToolRisk.Dangerous)]
        public static string RunEditorScriptAsync(string code, string usings = "",
                                                  string additionalReferences = "",
                                                  int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "Error: No code provided.";

            if (!AgentSettings.RequestConfirmation(
                    "C#スクリプトをバックグラウンド実行",
                    $"以下のコードをジョブとして実行します:\n\n{code}"))
                return "Cancelled: User denied script execution.";

            if (timeoutSeconds <= 0) timeoutSeconds = DefaultTimeoutSeconds;
            if (timeoutSeconds > MaxTimeoutSeconds) timeoutSeconds = MaxTimeoutSeconds;

            // Compile before handing back an id. A job id for code that never compiled would force
            // the caller into a poll just to be told about a typo.
            if (!ScriptExecutionTools.TryCompileScript(code, usings, additionalReferences,
                                                       out var entry, out string compileError))
                return compileError;

            EvictStaleJobs();

            var job = new ScriptJob
            {
                Id = "job-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CodePreview = Preview(code),
                Entry = entry,
                StartedAt = EditorApplication.timeSinceStartup,
                TimeoutSeconds = timeoutSeconds,
                ConsoleBaseline = ConsoleTools.GetEntryCount(),
            };
            _jobs[job.Id] = job;
            _runnable.Add(job);
            EnsureDriver();

            Debug.Log($"[UnityAgent] RunEditorScriptAsync queued {job.Id}:\n{code}");
            return $"jobId: {job.Id}\nstate: queued (starts on the next editor tick)\n" +
                   $"timeoutSeconds: {timeoutSeconds}\n" +
                   $"Poll with GetJobResult(jobId:'{job.Id}').";
        }

        [AgentTool(@"Read the outcome of a RunEditorScriptAsync job.

Reports whether the job is done, what it returned or threw, how long it ran, and any console
messages logged while it was running.

waitSeconds: block up to this long for the job to finish before answering (default 0 = answer
  immediately). Capped so the call cannot outlive the MCP transport's own 120 s limit.

The console excerpt is a time window, not a capture: anything else that logged while the job ran
appears too. Treat it as context, not as the job's own output — use the return value for that.

An unknown jobId lists the ids currently known, which is also how you recover after losing one.
Jobs do not survive a domain reload.")]
        public static IEnumerator GetJobResult(string jobId, int waitSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                yield return "Error: jobId is required.\n" + DescribeKnownJobs();
                yield break;
            }

            jobId = jobId.Trim();
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                yield return $"Error: unknown jobId '{jobId}'. Jobs are lost on domain reload.\n" +
                             DescribeKnownJobs();
                yield break;
            }

            if (waitSeconds > 0)
            {
                if (waitSeconds > EditorStateTools.MaxToolSeconds) waitSeconds = EditorStateTools.MaxToolSeconds;
                double deadline = EditorApplication.timeSinceStartup + waitSeconds;
                while (!job.Done && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
            }

            yield return Describe(job);
        }

        static string Describe(ScriptJob job)
        {
            double elapsed = (job.Done ? job.FinishedAt : EditorApplication.timeSinceStartup) - job.StartedAt;
            var sb = new StringBuilder();
            sb.AppendLine($"jobId: {job.Id}");
            sb.AppendLine($"done: {job.Done}");
            sb.AppendLine($"elapsedMs: {(int)(elapsed * 1000)}");
            if (job.Routine != null || job.Steps > 0)
                sb.AppendLine($"steps: {job.Steps}");
            if (!job.Started) sb.AppendLine("state: queued (has not reached its first tick yet)");
            else if (!job.Done) sb.AppendLine("state: running");

            if (job.Done)
            {
                if (job.Error != null)
                {
                    sb.AppendLine("outcome: FAILED");
                    sb.AppendLine("error:");
                    sb.AppendLine(job.Error);
                }
                else
                {
                    sb.AppendLine("outcome: ok");
                    sb.AppendLine("result:");
                    sb.AppendLine(job.Result ?? "(no value returned)");
                }

                string logs = ReadConsoleSince(job.ConsoleBaseline);
                if (!string.IsNullOrEmpty(logs))
                {
                    sb.AppendLine("--- console during the job (may include unrelated messages) ---");
                    sb.AppendLine(logs);
                }
            }
            else
            {
                sb.AppendLine($"code: {job.CodePreview}");
            }
            return sb.ToString().TrimEnd();
        }

        static string DescribeKnownJobs()
        {
            if (_jobs.Count == 0) return "No jobs are currently known.";
            var sb = new StringBuilder("Known jobs:");
            foreach (var kv in _jobs)
                sb.Append($"\n  {kv.Key}  done={kv.Value.Done}  {kv.Value.CodePreview}");
            return sb.ToString();
        }

        static string ReadConsoleSince(int baseline)
        {
            if (baseline < 0) return null;
            try
            {
                return ConsoleTools.GetConsoleLogs(
                    severity: "all", maxEntries: 30, keyword: "",
                    includeStackTrace: false, sinceIndex: baseline > 0 ? baseline - 1 : -1);
            }
            catch (Exception ex)
            {
                return $"(console unreadable: {ex.GetType().Name})";
            }
        }

        static string Preview(string code)
        {
            string flat = code.Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= 90 ? flat : flat.Substring(0, 87) + "...";
        }

        // ─── driver ───

        static void EnsureDriver()
        {
            if (_driverRegistered) return;
            EditorApplication.update += Drive;
            _driverRegistered = true;
        }

        static void Drive()
        {
            if (_runnable.Count == 0)
            {
                // Nothing left to pump. Unsubscribing keeps a finished session from paying for an
                // empty callback on every tick for the rest of the editor's life.
                EditorApplication.update -= Drive;
                _driverRegistered = false;
                return;
            }

            // One job per tick. Running them all would reintroduce exactly the freeze this exists
            // to avoid when several long jobs are queued together.
            var job = _runnable[0];
            double now = EditorApplication.timeSinceStartup;

            if (now - job.StartedAt > job.TimeoutSeconds)
            {
                Finish(job, null, $"Job abandoned after {job.TimeoutSeconds:F0}s (timeoutSeconds). " +
                                  (job.Routine != null
                                      ? $"It had completed {job.Steps} steps."
                                      : "A synchronous script cannot be interrupted, so it may still be running inside the editor."));
                return;
            }

            try
            {
                if (!job.Started)
                {
                    job.Started = true;
                    object returned = job.Entry.Invoke(null, null);
                    if (returned is IEnumerator routine)
                    {
                        job.Routine = routine;   // pumped from the next tick on
                        return;
                    }
                    Finish(job, returned == null ? "Script executed successfully." : returned.ToString(), null);
                    return;
                }

                if (job.Routine != null)
                {
                    job.Steps++;
                    if (!job.Routine.MoveNext())
                    {
                        Finish(job, "Script coroutine completed.", null);
                        return;
                    }

                    // A yielded string is the script's way of returning a value, matching how the
                    // tool framework treats IEnumerator-returning tools.
                    if (job.Routine.Current is string yielded)
                        Finish(job, yielded, null);
                }
            }
            catch (TargetInvocationException tex)
            {
                var inner = tex.InnerException;
                Finish(job, null, $"{inner?.Message ?? tex.Message}\n{inner?.StackTrace ?? tex.StackTrace}");
            }
            catch (Exception ex)
            {
                Finish(job, null, $"{ex.Message}\n{ex.StackTrace}");
            }
        }

        static void Finish(ScriptJob job, string result, string error)
        {
            job.Done = true;
            job.Result = result;
            job.Error = error;
            job.FinishedAt = EditorApplication.timeSinceStartup;
            job.Routine = null;
            _runnable.Remove(job);
        }

        /// <summary>
        /// Drops finished jobs that are old enough that nobody is coming back for them, then — if
        /// the table is still at its cap — the oldest finished job regardless of age. Running jobs
        /// are never evicted; losing one would strand work with no way to observe it.
        /// </summary>
        static void EvictStaleJobs()
        {
            double now = EditorApplication.timeSinceStartup;
            var expired = new List<string>();
            foreach (var kv in _jobs)
                if (kv.Value.Done && now - kv.Value.FinishedAt > RetentionSeconds)
                    expired.Add(kv.Key);
            foreach (string id in expired) _jobs.Remove(id);

            while (_jobs.Count >= MaxRetainedJobs)
            {
                string oldest = null;
                double oldestAt = double.MaxValue;
                foreach (var kv in _jobs)
                    if (kv.Value.Done && kv.Value.FinishedAt < oldestAt)
                    {
                        oldest = kv.Key;
                        oldestAt = kv.Value.FinishedAt;
                    }
                if (oldest == null) break;   // everything still running — let the table grow
                _jobs.Remove(oldest);
            }
        }
    }
}
