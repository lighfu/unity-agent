using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.Editor.Tools;
using UnityEditor;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static JNode SourcePage(string text)
    {
        var line = text.Split('\n').Single(value => value.StartsWith("sourcePage=", StringComparison.Ordinal));
        return JNode.Parse(line.Substring("sourcePage=".Length));
    }

    private static List<int> ConsoleIndices(string text) => Regex.Matches(text, @"^\[[EWI]\] #(\d+) ", RegexOptions.Multiline)
        .Cast<Match>().Select(match => int.Parse(match.Groups[1].Value)).ToList();

    private static void Main()
    {
        Windows();
        ConsolePages();
        Files();
        Console.WriteLine($"ToolSourcePaging: {_checks} checks passed.");
    }

    private static void Windows()
    {
        foreach (bool fromEnd in new[] { false, true })
        foreach (int total in new[] { 0, 1, 49, 50, 51, 1234 })
        foreach (int limit in new[] { 1, 50, 500 })
        {
            var collected = new HashSet<int>();
            int offset = 0;
            while (true)
            {
                var page = new ToolSourcePaging.Window(total, offset, limit, fromEnd);
                Check(page.Start >= 0 && page.End <= total && page.Count <= limit, "Source window outside range.");
                var metadata = page.Metadata();
                Check(metadata["total"].AsInt == total && metadata["returned"].AsInt == page.Count, "Source counts differ from the selected window.");
                Check(metadata["remaining"].AsInt == total - offset - page.Count, "Remaining source records are inaccurate.");
                for (int i = page.Start; i < page.End; i++) Check(collected.Add(i), "Source windows overlap.");
                if (!page.HasMore)
                {
                    Check(!page.NextOffset.HasValue && metadata["nextOffset"].IsNull, "Terminal window needs no continuation.");
                    break;
                }
                Check(page.Count > 0 && page.NextOffset > offset, "Source window failed to advance.");
                offset = page.NextOffset.Value;
            }
            Check(collected.Count == total, "Joining source windows dropped records.");
            foreach (int terminalOffset in new[] { total, total + 10, int.MaxValue })
            {
                var terminal = new ToolSourcePaging.Window(total, terminalOffset, limit, fromEnd);
                Check(terminal.Count == 0 && terminal.Remaining == 0 && !terminal.HasMore, "Out-of-range source window is not terminal.");
            }
        }
        var legacyTail = new ToolSourcePaging.Window(117, 0, 50, true);
        Check(legacyTail.Start == 67 && legacyTail.End == 117, "Console's existing newest-50 default changed.");
        var legacyHead = new ToolSourcePaging.Window(117, 0, 50, false);
        Check(legacyHead.Start == 0 && legacyHead.End == 50, "NDMF's existing first-50 default changed.");
        var large = new ToolSourcePaging.Window(int.MaxValue, int.MaxValue - 2, 500, false);
        Check(large.Count == 2 && !large.HasMore && large.End == int.MaxValue, "Source paging integer boundary overflowed.");
    }

    private static void ConsolePages()
    {
        LogEntries.Clear();
        for (int i = 0; i < 137; i++)
            LogEntries.Rows.Add(new LogEntry { mode = i % 2 == 0 ? 1 << 8 : 1 << 9,
                message = $"Paging-{i}\nstacktrace-{i}", file = "Assets/Test.cs", line = i + 1 });
        var first = ConsoleTools.GetConsoleLogs();
        Check(ConsoleIndices(first).SequenceEqual(Enumerable.Range(87, 50)), "Default Console page should remain the newest 50 entries, ordered oldest first.");
        Check(first.EndsWith("nextSinceIndex=136", StringComparison.Ordinal), "New-entry polling marker must remain last.");
        var page = SourcePage(first);
        Check(page["total"].AsInt == 137 && page["nextOffset"].AsInt == 50 && page["direction"].AsString == "older", "Console source continuation metadata incorrect.");
        var all = new HashSet<int>();
        int offset = 0;
        do
        {
            string text = ConsoleTools.GetConsoleLogs(maxEntries: 13, includeStackTrace: true, offset: offset);
            page = SourcePage(text);
            foreach (int index in ConsoleIndices(text))
            {
                Check(all.Add(index), "Console source window repeated an entry.");
                Check(text.Contains("stacktrace-" + index, StringComparison.Ordinal), "Console window omitted a requested stack trace.");
            }
            Check(page["returned"].AsInt == ConsoleIndices(text).Count, "Console source returned count differs from entries.");
            if (!page["hasMore"].AsBool) break;
            offset = page["nextOffset"].AsInt;
        } while (true);
        Check(all.SetEquals(Enumerable.Range(0, 137)), "Console source paging did not retrieve every entry.");
        var filtered = ConsoleTools.GetConsoleLogs(severity: "error", maxEntries: 7, keyword: "Paging", regex: @"^Paging-\d+$", sinceIndex: 100);
        Check(ConsoleIndices(filtered).SequenceEqual(new[] { 124, 126, 128, 130, 132, 134, 136 }), "Filtering must happen before Console offset/limit selection.");
        Check(SourcePage(filtered)["total"].AsInt == 18, "Console source total must count filtered, since-window rows only.");
        Check(filtered.Contains("errors=69, warnings=68", StringComparison.Ordinal), "Console whole-project severity counts must remain independent of source page.");
        var older = ConsoleTools.GetConsoleLogs(severity: "error", maxEntries: 7, keyword: "Paging", regex: @"^Paging-\d+$", sinceIndex: 100, offset: 7);
        Check(ConsoleIndices(older).SequenceEqual(new[] { 110, 112, 114, 116, 118, 120, 122 }), "Filtered Console continuation skipped or repeated rows.");
        Check(ConsoleTools.GetConsoleLogs(offset: -1).StartsWith("Error:", StringComparison.Ordinal), "Negative Console offset accepted.");
        Check(ConsoleTools.GetConsoleLogs(regex: "[").StartsWith("Error:", StringComparison.Ordinal), "Invalid Console filter changed into an empty source page.");
        var terminal = ConsoleTools.GetConsoleLogs(offset: int.MaxValue);
        Check(ConsoleIndices(terminal).Count == 0 && !SourcePage(terminal)["hasMore"].AsBool, "Console extreme offset must return an empty terminal page.");
        LogEntries.Clear();
        Check(SourcePage(ConsoleTools.GetConsoleLogs())["total"].AsInt == 0, "Empty Console needs an explicit terminal source page.");
        LogEntries.Rows.Add(new LogEntry { mode = 1 << 10, message = "new-after-reset" });
        Check(ConsoleTools.GetConsoleLogs(sinceIndex: 136).Contains("console was cleared", StringComparison.Ordinal), "Console clear detection changed.");
        Check(LogEntries.Started == LogEntries.Ended, "Console entry read sessions must always close.");
    }

    private static void Files()
    {
        string previous = Directory.GetCurrentDirectory();
        string temporary = Path.Combine(Path.GetTempPath(), "unity-tool-source-paging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "Assets"));
        try
        {
            Directory.SetCurrentDirectory(temporary);
            string source = string.Join("\r\n", Enumerable.Range(0, 700).Select(i => "Line-" + i + "  ")) + "\r\n\tlast\n";
            File.WriteAllText("Assets/large.txt", source);
            string result = FileTools.ReadFile("Assets/large.txt");
            Check(result == source, "Reading >500 lines must preserve every character and original line endings.");
            Check(result.Contains("Line-699", StringComparison.Ordinal), "ReadFile still drops lines beyond the former cap.");
            File.WriteAllText("Assets/one-long-line.txt", new string('x', 30000) + "\t  ");
            Check(FileTools.ReadFile("Assets/one-long-line.txt").Length == 30003, "ReadFile must preserve a long single line and trailing whitespace.");
            Check(FileTools.ReadFile("../large.txt").StartsWith("Error:", StringComparison.Ordinal), "Existing file-path boundary removed.");
            Check(FileTools.ReadFile("Assets/model.fbx").Contains("binary file", StringComparison.Ordinal), "Existing binary-file guard removed.");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(temporary, true);
        }
    }
}
