using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        /// Receipts to keep in the current editor session, including finished jobs.
        /// Active jobs are never evicted.
        /// </summary>
        const int MaxRetainedJobs = 32;

        /// <summary>How long a finished job stays readable before it can be evicted.</summary>
        const double RetentionSeconds = 1800;

        const int DefaultTimeoutSeconds = 600;
        const int MaxTimeoutSeconds = 3600;
        // Allow the transport to send the receipt before a synchronous script can block Unity.
        const int TicksBeforeExecution = 4;
        const string SessionIndexKey = "UnityAgent.ScriptJob.Ids";
        const string SessionKeyPrefix = "UnityAgent.ScriptJob.Receipt.";

        [Serializable]
        sealed class JobReceipt
        {
            public string Id;
            public string CodePreview;
            public string State = "queued";
            public long EnqueuedStamp;
            public long FinishedStamp;
            public int Steps;
            public string FinalText;

            public bool IsPending => State == "queued" || State == "running";
        }

        sealed class ScriptJob
        {
            public JobReceipt Receipt;
            public MethodInfo Entry;
            public IEnumerator Routine;      // non-null once a script has returned an IEnumerator
            public int TicksUntilExecution = TicksBeforeExecution;
            public double TimeoutSeconds;
            public int ConsoleBaseline;
        }

        static readonly object _receiptLock = new object();
        static readonly Dictionary<string, JobReceipt> _receipts = new Dictionary<string, JobReceipt>(StringComparer.Ordinal);
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

The iterator goes in `members`, NOT in `code`: the compiler used here rejects local functions, so a
method cannot be declared inside the script body. Write bare 'IEnumerator' nowhere — the default usings
open System.Collections.Generic but not System.Collections, so an unqualified IEnumerator binds to the
generic one and fails. Spell it in full:

  code    = ""return Steps();""
  members = ""static System.Collections.IEnumerator Steps() {
                 for (int i = 0; i < 100; i++) { DoOne(i); yield return null; }
                 yield return ""done: 100 items"";   // a yielded string finishes the job with that value
               }""

timeoutSeconds: abandon the job after this long (default 600, max 3600). A synchronous script
  cannot be interrupted mid-call — the timeout is only observed between ticks.

The job id and recorded result survive a domain reload in the current editor session. An
interrupted job is reported as not_started or outcome_unknown. The script is never restarted
automatically.
Same risk tier as RunEditorScript: this is arbitrary code execution.",
            Risk = ToolRisk.Dangerous)]
        public static string RunEditorScriptAsync(string code, string usings = "",
                                                  string additionalReferences = "",
                                                  int timeoutSeconds = DefaultTimeoutSeconds,
                                                  string members = "")
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
            if (!ScriptExecutionTools.TryCompileScript(code, usings, additionalReferences, members,
                                                       out var entry, out string compileError))
                return compileError + ScriptExecutionTools.DescribeToolOverlap(code);

            var receipt = new JobReceipt
            {
                Id = "job-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CodePreview = Preview(code),
                EnqueuedStamp = Stopwatch.GetTimestamp(),
            };
            var job = new ScriptJob
            {
                Receipt = receipt,
                Entry = entry,
                TimeoutSeconds = timeoutSeconds,
                ConsoleBaseline = ConsoleTools.GetEntryCount(),
            };
            lock (_receiptLock)
            {
                EvictStaleJobs();
                SaveReceipt(receipt);
                _receipts.Add(receipt.Id, receipt);
                SessionState.SetString(SessionIndexKey, string.Join(",", _receipts.Keys));
            }
            _runnable.Add(job);
            EnsureDriver();

            Debug.Log($"[UnityAgent] RunEditorScriptAsync queued {receipt.Id}:\n{code}");

            // Named here rather than at GetJobResult: the point of a hint is to arrive before the caller
            // has spent the wait believing this was the only way to do it.
            return $"jobId: {receipt.Id}\nstate: queued (starts after a few editor ticks)\n" +
                   $"timeoutSeconds: {timeoutSeconds}\n" +
                   $"Poll with GetJobResult(jobId:'{receipt.Id}')." +
                   ScriptExecutionTools.DescribeToolOverlap(code);
        }

        [AgentTool(@"Read the outcome of a RunEditorScriptAsync job.

Reports whether the job is done, what it returned or threw, how long it ran, and any console
messages logged while it was running.

waitSeconds: block up to this long for the job to finish before answering (default 0 = answer
  immediately). Capped so the call cannot outlive the MCP transport's own 120 s limit.

The console excerpt is a time window, not a capture: anything else that logged while the job ran
appears too. Treat it as context, not as the job's own output — use the return value for that.

An unknown jobId lists the ids currently known, which is also how you recover after losing one.
Receipts survive a domain reload during the current editor session. A reload leaves a queued job
as not_started or a running job as outcome_unknown. In MCP, polling can report progress while a
synchronous script occupies the editor's main thread.")]
        public static IEnumerator GetJobResult(string jobId, int waitSeconds = 0)
        {
            if (!TryFindReceipt(jobId, out var receipt, out string error))
            {
                yield return error;
                yield break;
            }

            if (waitSeconds > 0)
            {
                double deadline = EditorApplication.timeSinceStartup + Math.Min(waitSeconds, EditorStateTools.MaxToolSeconds);
                while (receipt.IsPending && EditorApplication.timeSinceStartup < deadline) yield return null;
            }

            yield return Describe(receipt);
        }

        internal static Func<string> MatchOffMainThread(string jobId, int waitSeconds)
        {
            return () =>
            {
                if (!TryFindReceipt(jobId, out var receipt, out string error)) return error;
                int waitMs = Math.Min(Math.Max(0, waitSeconds), EditorStateTools.MaxToolSeconds) * 1000;
                var clock = Stopwatch.StartNew();
                lock (_receiptLock)
                {
                    long remainingMs;
                    while (receipt.IsPending && (remainingMs = waitMs - clock.ElapsedMilliseconds) > 0)
                        Monitor.Wait(_receiptLock, (int)remainingMs);
                    return Describe(receipt);
                }
            };
        }

        static bool TryFindReceipt(string jobId, out JobReceipt receipt, out string error)
        {
            string id = (jobId ?? "").Trim();
            lock (_receiptLock)
            {
                error = null;
                if (_receipts.TryGetValue(id, out receipt)) return true;
                error = (id.Length == 0 ? "Error: jobId is required." : $"Error: unknown or expired jobId '{id}'.") +
                        "\n" + DescribeKnownJobs();
                return false;
            }
        }

        static double ElapsedSeconds(long start, long end) => (double)(end - start) / Stopwatch.Frequency;

        static string Describe(JobReceipt receipt)
        {
            if (!receipt.IsPending) return receipt.FinalText;
            var sb = new StringBuilder();
            sb.AppendLine($"jobId: {receipt.Id}");
            sb.AppendLine("done: False");
            sb.AppendLine($"elapsedMs: {(long)(ElapsedSeconds(receipt.EnqueuedStamp, Stopwatch.GetTimestamp()) * 1000)}");
            if (receipt.Steps > 0) sb.AppendLine($"steps: {receipt.Steps}");
            sb.AppendLine($"state: {receipt.State}");
            sb.Append($"code: {receipt.CodePreview}");
            return sb.ToString();
        }

        static string DescribeFinished(JobReceipt receipt, string result, string error, string logs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"jobId: {receipt.Id}");
            sb.AppendLine("done: True");
            sb.AppendLine($"elapsedMs: {(long)(ElapsedSeconds(receipt.EnqueuedStamp, receipt.FinishedStamp) * 1000)}");
            if (receipt.Steps > 0) sb.AppendLine($"steps: {receipt.Steps}");
            sb.AppendLine($"state: {receipt.State}");
            if (error != null)
            {
                sb.AppendLine("outcome: FAILED");
                sb.AppendLine("error:");
                sb.AppendLine(error);
            }
            else
            {
                sb.AppendLine("outcome: ok");
                sb.AppendLine("result:");
                sb.AppendLine(result ?? "(no value returned)");
            }
            if (!string.IsNullOrEmpty(logs))
            {
                sb.AppendLine("--- console during the job (may include unrelated messages) ---");
                sb.AppendLine(logs);
            }
            return sb.ToString().TrimEnd();
        }

        static string DescribeKnownJobs()
        {
            if (_receipts.Count == 0) return "No jobs are currently known.";
            var sb = new StringBuilder("Known jobs:");
            foreach (var receipt in _receipts.Values)
                sb.Append($"\n  {receipt.Id}  state={receipt.State}  {receipt.CodePreview}");
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
            var receipt = job.Receipt;
            if (ElapsedSeconds(receipt.EnqueuedStamp, Stopwatch.GetTimestamp()) > job.TimeoutSeconds)
            {
                Finish(job, null, $"Job abandoned after {job.TimeoutSeconds:F0}s (timeoutSeconds). " +
                                  (job.Routine != null
                                      ? $"It had completed {receipt.Steps} steps."
                                      : "The script had not started."));
                return;
            }

            try
            {
                if (receipt.State == "queued")
                {
                    if (--job.TicksUntilExecution > 0) return;
                    lock (_receiptLock)
                    {
                        receipt.State = "running";
                        SaveReceipt(receipt);
                    }
                    object returned = job.Entry.Invoke(null, null);
                    if (returned is IEnumerator routine)
                    {
                        job.Routine = routine;   // pumped from the next tick on
                        return;
                    }

                    // Use the synchronous runner's result format. Finish captures the console once.
                    Finish(job, ScriptExecutionTools.DescribeScriptResult(returned, null), null);
                    return;
                }

                if (job.Routine != null)
                {
                    lock (_receiptLock) receipt.Steps++;
                    if (!job.Routine.MoveNext())
                    {
                        // Same trap as a script that forgets 'return': finishing quietly reads as "it ran
                        // and found nothing" when it means "you never yielded a value".
                        Finish(job, "Script coroutine completed. (no value was yielded — yield a string " +
                                    "to return one; anything the script logged is below)", null);
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
            long finishedStamp = Stopwatch.GetTimestamp();
            string logs = ReadConsoleSince(job.ConsoleBaseline);
            lock (_receiptLock)
            {
                var receipt = job.Receipt;
                receipt.FinishedStamp = finishedStamp;
                receipt.State = error == null ? "succeeded" : "failed";
                receipt.FinalText = DescribeFinished(receipt, result, error, logs);
                SaveReceipt(receipt);
                Monitor.PulseAll(_receiptLock);
            }
            job.Routine = null;
            _runnable.Remove(job);
        }

        static void SaveReceipt(JobReceipt receipt)
            => SessionState.SetString(SessionKeyPrefix + receipt.Id, JsonUtility.ToJson(receipt));

        [InitializeOnLoadMethod]
        static void RestoreReceipts()
        {
            string[] ids = SessionState.GetString(SessionIndexKey, "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            lock (_receiptLock)
            {
                foreach (string id in ids)
                {
                    string json = SessionState.GetString(SessionKeyPrefix + id, "");
                    if (string.IsNullOrEmpty(json)) continue;
                    var receipt = JsonUtility.FromJson<JobReceipt>(json);
                    if (receipt?.Id != id) continue;
                    if (receipt.IsPending)
                    {
                        bool started = receipt.State == "running";
                        receipt.State = started ? "outcome_unknown" : "not_started";
                        receipt.FinishedStamp = Stopwatch.GetTimestamp();
                        string detail = started
                            ? "A domain reload interrupted the job before its outcome was recorded. Inspect the project's state before deciding whether to start a new job."
                            : "A domain reload discarded the queued job before it started.";
                        receipt.FinalText = $"jobId: {id}\ndone: True\nstate: {receipt.State}\n{detail}";
                        SaveReceipt(receipt);
                    }
                    _receipts.Add(id, receipt);
                }
            }
        }

        /// <summary>
        /// Drops finished jobs that are old enough that nobody is coming back for them, then — if
        /// the table is still at its cap — the oldest finished job regardless of age. Running jobs
        /// are never evicted; losing one would strand work with no way to observe it.
        /// </summary>
        static void EvictStaleJobs()
        {
            long now = Stopwatch.GetTimestamp();
            var expired = new List<string>();
            foreach (var receipt in _receipts.Values)
                if (!receipt.IsPending && ElapsedSeconds(receipt.FinishedStamp, now) > RetentionSeconds)
                    expired.Add(receipt.Id);
            foreach (string id in expired) RemoveReceipt(id);

            while (_receipts.Count >= MaxRetainedJobs)
            {
                JobReceipt oldest = null;
                foreach (var receipt in _receipts.Values)
                    if (!receipt.IsPending && (oldest == null || receipt.FinishedStamp < oldest.FinishedStamp))
                        oldest = receipt;
                if (oldest == null) break;   // everything still running — let the table grow
                RemoveReceipt(oldest.Id);
            }
        }

        static void RemoveReceipt(string id)
        {
            _receipts.Remove(id);
            SessionState.EraseString(SessionKeyPrefix + id);
        }
    }
}
