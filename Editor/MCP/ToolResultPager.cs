using System;
using System.Collections.Generic;
using System.Globalization;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Bounded delivery of a completed tool result. Continuation reads an immutable
    /// text snapshot; it never invokes the original tool again.
    /// </summary>
    internal static class ToolResultPager
    {
        internal const int DefaultLimit = 50;
        internal const int MaximumLimit = 200;
        internal const int DefaultMaxChars = 8192;
        internal const int MinMaxChars = 1024;
        internal const int MaximumMaxChars = 32768;
        private const int SegmentMaxChars = 1024;
        private const int DefaultEntryCapacity = 32;
        private const long DefaultCharacterCapacity = 8 * 1024 * 1024;

        private sealed class Snapshot
        {
            internal string Id;
            internal string ToolName;
            internal string Text;
            // Includes the final end offset, so segment i is [Starts[i], Starts[i+1]).
            internal int[] Starts;
            internal DateTime ExpiresAt;
            internal LinkedListNode<string> LruNode;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Snapshot> Snapshots = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
        private static readonly LinkedList<string> Lru = new LinkedList<string>();
        private static Func<DateTime> _utcNow = () => DateTime.UtcNow;
        private static TimeSpan _timeToLive = TimeSpan.FromMinutes(15);
        private static int _entryCapacity = DefaultEntryCapacity;
        private static long _characterCapacity = DefaultCharacterCapacity;
        private static long _cachedCharacters;

        internal static string Validate(int offset, int limit, int maxChars)
        {
            if (offset < 0) return "Error: offset must be a non-negative integer.";
            if (limit < 1 || limit > MaximumLimit) return $"Error: limit must be between 1 and {MaximumLimit}.";
            if (maxChars < MinMaxChars || maxChars > MaximumMaxChars)
                return $"Error: maxChars must be between {MinMaxChars} and {MaximumMaxChars}.";
            return null;
        }

        internal static string CreateFirstPage(string toolName, string text, int offset = 0, int limit = DefaultLimit, int maxChars = DefaultMaxChars)
        {
            string error = Validate(offset, limit, maxChars);
            if (error != null) return error;
            text = text ?? "";
            int[] starts = SegmentStarts(text);
            if (offset == 0 && starts.Length - 1 <= limit && text.Length <= maxChars) return text;

            var snapshot = new Snapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                ToolName = toolName ?? "",
                Text = text,
                Starts = starts,
            };
            lock (CacheLock)
            {
                DateTime now = _utcNow().ToUniversalTime();
                PruneExpired(now);
                snapshot.ExpiresAt = now.Add(_timeToLive);
                snapshot.LruNode = Lru.AddLast(snapshot.Id);
                Snapshots.Add(snapshot.Id, snapshot);
                _cachedCharacters += text.Length;
                // Retain even a single over-budget result losslessly. A subsequent result
                // can evict it through the ordinary least-recently-used policy.
                while (Snapshots.Count > _entryCapacity || (_cachedCharacters > _characterCapacity && Snapshots.Count > 1))
                    RemoveSnapshot(Lru.First.Value);
                return BuildPage(snapshot, offset, limit, maxChars);
            }
        }

        internal static string ReadPage(string resultId, int offset = 0, int limit = DefaultLimit, int maxChars = DefaultMaxChars)
        {
            string error = Validate(offset, limit, maxChars);
            if (error != null) return error;
            if (string.IsNullOrEmpty(resultId)) return "Error: resultId is required.";
            lock (CacheLock)
            {
                PruneExpired(_utcNow().ToUniversalTime());
                if (!Snapshots.TryGetValue(resultId, out var snapshot))
                    return "Error: Tool result snapshot is unavailable (unknown, expired or evicted resultId).";
                Lru.Remove(snapshot.LruNode);
                snapshot.LruNode = Lru.AddLast(snapshot.Id);
                return BuildPage(snapshot, offset, limit, maxChars);
            }
        }

        /// <summary>Recognize only an exact page generated from a currently retained snapshot.</summary>
        internal static bool IsSavedPage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var candidate = JNode.Parse(text);
            string resultId = candidate["resultId"].AsString;
            var page = candidate["page"];
            if (string.IsNullOrEmpty(resultId)
                || !TryPageInteger(page["offset"], out var offset)
                || !TryPageInteger(page["limit"], out var limit)
                || !TryPageInteger(page["maxChars"], out var maxChars)
                || Validate(offset, limit, maxChars) != null) return false;
            lock (CacheLock)
            {
                PruneExpired(_utcNow().ToUniversalTime());
                return Snapshots.TryGetValue(resultId, out var snapshot)
                    && string.Equals(text, BuildPage(snapshot, offset, limit, maxChars), StringComparison.Ordinal);
            }
        }

        private static bool TryPageInteger(JNode value, out int number)
        {
            number = 0;
            if (value.Type != JNode.JType.Number || !(value.AsNumber >= int.MinValue && value.AsNumber <= int.MaxValue)
                || Math.Truncate(value.AsNumber) != value.AsNumber) return false;
            number = (int)value.AsNumber;
            return true;
        }

        private static string BuildPage(Snapshot snapshot, int offset, int limit, int maxChars)
        {
            int total = snapshot.Starts.Length - 1;
            int available = offset < total ? total - offset : 0;
            int take = Math.Min(limit, available);
            int returned = 0;
            int returnedChars = 0;
            for (int i = 0; i < take; i++)
            {
                int segment = offset + i;
                int characters = snapshot.Starts[segment + 1] - snapshot.Starts[segment];
                if (characters > maxChars - returnedChars) break;
                returnedChars += characters;
                returned++;
            }
            // Each record has at most 1024 characters and maxChars is at least 1024,
            // so every nonterminal read advances by at least one complete record.
            int remaining = available - returned;
            bool hasMore = remaining > 0;
            string fragment = returned == 0 ? "" : snapshot.Text.Substring(snapshot.Starts[offset], returnedChars);
            return JNode.Obj(
                ("resultId", JNode.Str(snapshot.Id)),
                ("toolName", JNode.Str(snapshot.ToolName)),
                ("text", JNode.Str(fragment)),
                ("page", JNode.Obj(
                    ("unit", JNode.Str("textSegments")),
                    ("total", JNode.Num(total)),
                    ("offset", JNode.Num(offset)),
                    ("limit", JNode.Num(limit)),
                    ("returned", JNode.Num(returned)),
                    ("remaining", JNode.Num(remaining)),
                    ("hasMore", JNode.Bool(hasMore)),
                    ("nextOffset", hasMore ? JNode.Num(offset + returned) : JNode.NullNode),
                    ("maxChars", JNode.Num(maxChars)),
                    ("returnedChars", JNode.Num(returnedChars)))),
                ("expiresAt", JNode.Str(snapshot.ExpiresAt.ToString("o", CultureInfo.InvariantCulture))),
                ("nextTool", JNode.Str("ReadUnityToolResultPage"))).ToJson();
        }

        private static int[] SegmentStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (int start = 0; start < text.Length;)
            {
                int end = start;
                int ceiling = start + Math.Min(SegmentMaxChars, text.Length - start);
                while (end < ceiling)
                {
                    char character = text[end++];
                    if (character == '\n') break;
                    if (character != '\r') continue;
                    if (end < text.Length && text[end] == '\n')
                    {
                        if (end == ceiling) { end--; break; }
                        end++;
                    }
                    break;
                }
                if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
                starts.Add(end);
                start = end;
            }
            return starts.ToArray();
        }

        private static void PruneExpired(DateTime now)
        {
            var expired = new List<string>();
            foreach (var entry in Snapshots)
                if (entry.Value.ExpiresAt <= now) expired.Add(entry.Key);
            foreach (var id in expired) RemoveSnapshot(id);
        }

        private static void RemoveSnapshot(string id)
        {
            var snapshot = Snapshots[id];
            Snapshots.Remove(id);
            Lru.Remove(snapshot.LruNode);
            _cachedCharacters -= snapshot.Text.Length;
        }

        // Internal seams for deterministic tests; no public API or Unity state is changed.
        internal static void ConfigureForTests(Func<DateTime> utcNow, int entryCapacity, long characterCapacity, TimeSpan timeToLive)
        {
            if (utcNow == null) throw new ArgumentNullException(nameof(utcNow));
            if (entryCapacity < 1 || characterCapacity < 1 || timeToLive <= TimeSpan.Zero) throw new ArgumentOutOfRangeException();
            lock (CacheLock)
            {
                Snapshots.Clear();
                Lru.Clear();
                _cachedCharacters = 0;
                _utcNow = utcNow;
                _entryCapacity = entryCapacity;
                _characterCapacity = characterCapacity;
                _timeToLive = timeToLive;
            }
        }

        internal static void ResetForTests() => ConfigureForTests(() => DateTime.UtcNow, DefaultEntryCapacity, DefaultCharacterCapacity, TimeSpan.FromMinutes(15));
    }
}
