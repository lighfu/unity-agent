using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    internal enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// ログの発生元を示す事前定義タグ。
    /// </summary>
    internal enum LogTag
    {
        Core,
        Provider,
        Tool,
        MCP,
        Fitting,
        UI,
        Settings,
        WebServer,
        Skill,
        L10n,
        Atlas,
        Update,
    }

    internal readonly struct LogEntry
    {
        public readonly DateTime Timestamp;
        public readonly LogLevel Level;
        public readonly LogTag Tag;
        public readonly string Message;
        public readonly int ThreadId;
        public readonly string ThreadName;

        public LogEntry(LogLevel level, LogTag tag, string message, int threadId, string threadName)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Tag = tag;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
        }
    }

    /// <summary>
    /// UnityAgent 全体で使える静的ロガー。
    /// メモリ内リングバッファにログを蓄積し、AgentLogWindow で閲覧できる。
    /// </summary>
    internal static class AgentLogger
    {
        private const int MaxEntries = 1000;

        private static readonly LogEntry[] _buffer = new LogEntry[MaxEntries];
        private static int _head;
        private static int _count;
        private static readonly object _lock = new object();

        /// <summary>新しいログが追加されたときに発火する。UIの自動更新に使用。</summary>
        public static event Action<LogEntry> OnLogAdded;

        public static void Log(LogLevel level, LogTag tag, string message)
        {
            var thread = Thread.CurrentThread;
            var entry = new LogEntry(level, tag, message, thread.ManagedThreadId, thread.Name);

            lock (_lock)
            {
                _buffer[_head] = entry;
                _head = (_head + 1) % MaxEntries;
                if (_count < MaxEntries) _count++;
            }

            // Warning/Error は Unity Console にも出力
            if (level >= LogLevel.Warning)
            {
                string formatted = $"[UnityAgent:{tag}] {message}";
                if (level == LogLevel.Warning)
                    UnityEngine.Debug.LogWarning(formatted);
                else
                    UnityEngine.Debug.LogError(formatted);
            }

            OnLogAdded?.Invoke(entry);
        }

        /// <summary>DebugMode が有効な時のみ記録する。</summary>
        public static void Debug(LogTag tag, string message)
        {
            if (!AgentSettings.DebugMode) return;
            Log(LogLevel.Debug, tag, message);
        }

        public static void Info(LogTag tag, string message)
        {
            Log(LogLevel.Info, tag, message);
        }

        public static void Warning(LogTag tag, string message)
        {
            Log(LogLevel.Warning, tag, message);
        }

        public static void Error(LogTag tag, string message)
        {
            Log(LogLevel.Error, tag, message);
        }

        /// <summary>現在のログエントリを古い順に返す。</summary>
        public static List<LogEntry> GetEntries()
        {
            lock (_lock)
            {
                var result = new List<LogEntry>(_count);
                int start = _count < MaxEntries ? 0 : _head;
                for (int i = 0; i < _count; i++)
                {
                    result.Add(_buffer[(start + i) % MaxEntries]);
                }
                return result;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _count = 0;
            }
        }
    }
}
