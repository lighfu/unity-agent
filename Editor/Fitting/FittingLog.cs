using System.Text;
using System.Diagnostics;

namespace AjisaiFlow.UnityAgent.Editor.Fitting
{
    /// <summary>
    /// Thread-safe structured log for fitting pipeline.
    /// Accumulates timestamped messages during execution.
    /// </summary>
    internal class FittingLog
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly object _lock = new object();

        /// <summary>Current progress 0-1 (written by worker, read by UI).</summary>
        public volatile float Progress;

        /// <summary>Set to true to request cancellation.</summary>
        public volatile bool IsCancelled;

        /// <summary>Elapsed seconds since Start().</summary>
        public float ElapsedSeconds => (float)_sw.Elapsed.TotalSeconds;

        public void Start() { _sw.Restart(); }

        public void Section(string title)
        {
            lock (_lock)
                _sb.AppendLine($"[{_sw.Elapsed.TotalSeconds,6:F1}s] ── {title} ──");
        }

        public void Info(string message)
        {
            lock (_lock)
                _sb.AppendLine($"[{_sw.Elapsed.TotalSeconds,6:F1}s] {message}");
        }

        public void Stat(string label, string value)
        {
            lock (_lock)
                _sb.AppendLine($"[{_sw.Elapsed.TotalSeconds,6:F1}s]   {label}: {value}");
        }

        public void Warn(string message)
        {
            lock (_lock)
                _sb.AppendLine($"[{_sw.Elapsed.TotalSeconds,6:F1}s] WARN: {message}");
        }

        public void Error(string message)
        {
            lock (_lock)
                _sb.AppendLine($"[{_sw.Elapsed.TotalSeconds,6:F1}s] ERROR: {message}");
        }

        public string GetText()
        {
            lock (_lock)
                return _sb.ToString();
        }

        public int Length
        {
            get { lock (_lock) return _sb.Length; }
        }
    }
}
