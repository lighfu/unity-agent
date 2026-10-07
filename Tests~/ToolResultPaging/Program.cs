using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AjisaiFlow.UnityAgent.Editor.MCP;

internal static class Program
{
    private static int _checks;
    private static void Assert(bool condition, string message)
    {
        Interlocked.Increment(ref _checks);
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        ToolResultPager.ResetForTests();
        ShortResults();
        LosslessResults();
        ValidationAndTerminals();
        SavedPageRecognition();
        ExpirationAndEviction();
        Concurrency();
        ToolResultPager.ResetForTests();
        Console.WriteLine($"ToolResultPaging: {_checks} checks passed.");
    }

    private static void ShortResults()
    {
        foreach (string text in new[] { "", "Success", "Error: absent", "{\"value\":3}", "a\r\nb\n", " \t\n\r\n ", "😀 日本語" })
            Assert(ToolResultPager.CreateFirstPage("Short", text) == text, "Short initial results must remain byte-for-byte equivalent strings.");
        Assert(ToolResultPager.CreateFirstPage("Null", null) == "", "Null result text must normalize to empty text.");
        string exact = new string('x', 1024);
        Assert(ToolResultPager.CreateFirstPage("Exact", exact, limit: 1, maxChars: 1024) == exact, "Exactly one maximum-size segment must retain the raw short-result format.");
        string fiftyLines = new string('\n', 50);
        Assert(ToolResultPager.CreateFirstPage("ExactRows", fiftyLines) == fiftyLines, "A final newline must not invent an extra empty segment.");
        var fiftyOne = Parse(ToolResultPager.CreateFirstPage("Rows", fiftyLines + "\n"));
        Assert(fiftyOne["page"]["total"].AsInt == 51 && fiftyOne["page"]["returned"].AsInt == 50, "Line count alone must trigger bounded paging even for a small text result.");
    }

    private static void LosslessResults()
    {
        string mixedNewlines = "a\r\nb\nc\rd\r\n\n\re";
        var mixed = Parse(ToolResultPager.CreateFirstPage("Newlines", mixedNewlines, limit: 1));
        Assert(mixed["page"]["total"].AsInt == 7, "CRLF, LF and bare CR must each define one line segment.");
        Assert(ReadAll(mixed, 1, 1024).SequenceEqual(new[] { "a\r\n", "b\n", "c\r", "d\r\n", "\n", "\r", "e" }), "Line segments must retain their exact original newline delimiters.");

        foreach (string text in new[] {
            string.Concat(Enumerable.Range(0, 140).Select(i => "Line " + i + "\r\n")),
            new string('a', 1023) + "\r\n" + new string('b', 2500) + "\nTail",
            new string('x', 1023) + "😀" + new string('z', 1024) + "\n",
            string.Concat(Enumerable.Repeat("😸 日本語\r\n", 150)),
            new string('k', 25000),
            "Error: " + new string('e', 18000),
            JNode.Obj(("escaped", JNode.Str(string.Concat(Enumerable.Repeat("quote\" slash\\ CR\r LF\n emoji😀", 600))))).ToJson()
        })
        {
            var first = Parse(ToolResultPager.CreateFirstPage("Lossless", text, limit: 1, maxChars: 1024));
            foreach (var budget in new[] { (limit: 1, chars: 1024), (limit: 50, chars: 8192), (limit: 200, chars: 32768) })
            {
                var fragments = ReadAll(first, budget.limit, budget.chars);
                Assert(string.Concat(fragments) == text, "Joining returned fragments must reconstruct the complete original result exactly.");
                CheckUnicodeAndNewlineBoundaries(text, fragments);
            }
        }
        string manyLongRows = string.Concat(Enumerable.Repeat(new string('r', 1023) + "\n", 100));
        var budgetPage = Parse(ToolResultPager.CreateFirstPage("Budget", manyLongRows));
        Assert(budgetPage["page"]["returned"].AsInt == 8 && budgetPage["page"]["returnedChars"].AsInt == 8192, "Character budget must cap a page before its record limit.");
        var segmentPage = Parse(ToolResultPager.ReadPage(budgetPage["resultId"].AsString, limit: 3, maxChars: 32768));
        Assert(segmentPage["page"]["returned"].AsInt == 3 && segmentPage["page"]["returnedChars"].AsInt == 3072, "Record limit must also cap pages below their character budget.");
        Assert(ReadAll(budgetPage, 50, 8192).Count == 13, "Character-capped pages must advance through all segments without duplicates.");
    }

    private static void ValidationAndTerminals()
    {
        foreach (int offset in new[] { -1, int.MinValue })
        {
            Assert(ToolResultPager.Validate(offset, 50, 8192) != null, "Negative offsets must fail validation.");
            Assert(ToolResultPager.CreateFirstPage("Bad", "text", offset).StartsWith("Error:"), "Initial invalid offset must fail before storing a snapshot.");
            Assert(ToolResultPager.ReadPage("anything", offset).StartsWith("Error:"), "Continuation invalid offset must fail.");
        }
        foreach (int limit in new[] { 0, -1, 201, int.MaxValue })
        {
            Assert(ToolResultPager.Validate(0, limit, 8192) != null, "Invalid record limits must fail validation.");
            Assert(ToolResultPager.CreateFirstPage("Bad", "text", limit: limit).StartsWith("Error:"), "Initial invalid limit must fail.");
            Assert(ToolResultPager.ReadPage("anything", limit: limit).StartsWith("Error:"), "Continuation invalid limit must fail.");
        }
        foreach (int chars in new[] { 0, 1023, 32769, int.MaxValue })
        {
            Assert(ToolResultPager.Validate(0, 50, chars) != null, "Invalid character budgets must fail validation.");
            Assert(ToolResultPager.CreateFirstPage("Bad", "text", maxChars: chars).StartsWith("Error:"), "Initial invalid character budget must fail.");
            Assert(ToolResultPager.ReadPage("anything", maxChars: chars).StartsWith("Error:"), "Continuation invalid character budget must fail.");
        }
        Assert(ToolResultPager.Validate(int.MaxValue, 200, 32768) == null, "Large offsets and maximum valid budgets must remain valid.");
        Assert(ToolResultPager.ReadPage(null).Contains("resultId is required"), "Missing snapshot identifiers must fail explicitly.");
        Assert(ToolResultPager.ReadPage("missing").Contains("unavailable"), "Unknown snapshots must fail explicitly.");
        string text = new string('t', 5000);
        var first = Parse(ToolResultPager.CreateFirstPage("Terminals", text, limit: 1));
        string id = first["resultId"].AsString;
        int total = first["page"]["total"].AsInt;
        foreach (int offset in new[] { total, total + 4, int.MaxValue })
        {
            var response = Parse(ToolResultPager.ReadPage(id, offset));
            CheckPage(response, offset, 50, 8192);
            Assert(response["text"].AsString == "" && response["page"]["returned"].AsInt == 0 && response["page"]["nextOffset"].IsNull, "Out-of-range offsets must return empty terminal pages without overflow.");
        }
        var partial = Parse(ToolResultPager.CreateFirstPage("Partial", text, offset: 2, limit: 1));
        Assert(partial["text"].AsString == text.Substring(2048, 1024), "An explicit initial offset must select its stable structural segment.");
        var empty = Parse(ToolResultPager.CreateFirstPage("Empty", "", offset: 1));
        CheckPage(empty, 1, 50, 8192);
        Assert(empty["page"]["total"].AsInt == 0 && Parse(ToolResultPager.ReadPage(empty["resultId"].AsString))["text"].AsString == "", "Empty snapshots must still support explicit paged terminal reads.");
        var one = Parse(ToolResultPager.CreateFirstPage("One", "one", offset: 1));
        var oneRead = Parse(ToolResultPager.ReadPage(one["resultId"].AsString));
        Assert(oneRead["text"].AsString == "one" && oneRead["page"]["total"].AsInt == 1, "ReadPage must always return metadata even for a short retained snapshot.");
    }

    private static void ExpirationAndEviction()
    {
        DateTime now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        ToolResultPager.ConfigureForTests(() => now, 2, 100000, TimeSpan.FromMinutes(15));
        string first = Snapshot("First");
        string second = Snapshot("Second");
        Parse(ToolResultPager.ReadPage(first));
        string third = Snapshot("Third");
        Assert(ToolResultPager.ReadPage(second).Contains("evicted"), "Entry capacity must evict the least recently read snapshot.");
        Assert(Parse(ToolResultPager.ReadPage(first))["toolName"].AsString == "First" && Parse(ToolResultPager.ReadPage(third))["toolName"].AsString == "Third", "Recent snapshots must remain available after LRU eviction.");
        var beforeExpiration = Parse(ToolResultPager.ReadPage(first));
        Assert(DateTime.Parse(beforeExpiration["expiresAt"].AsString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) == now.AddMinutes(15), "Snapshot expiration must be 15 minutes after creation.");
        now = now.AddMinutes(14);
        Parse(ToolResultPager.ReadPage(first));
        now = now.AddMinutes(1);
        Assert(ToolResultPager.ReadPage(first).Contains("expired"), "Reading a snapshot must not extend its creation-based expiry.");
        Assert(ToolResultPager.ReadPage(third).Contains("expired"), "Snapshots expire at the exact TTL boundary.");

        ToolResultPager.ConfigureForTests(() => now, 32, 5500, TimeSpan.FromMinutes(15));
        string small = Snapshot("Small");
        string newer = Snapshot("Newer");
        Assert(ToolResultPager.ReadPage(small).Contains("evicted"), "Character capacity must evict old results even below entry capacity.");
        Parse(ToolResultPager.ReadPage(newer));
        string giantText = new string('g', 12000);
        var giant = Parse(ToolResultPager.CreateFirstPage("Giant", giantText, limit: 1));
        Assert(ToolResultPager.ReadPage(newer).Contains("evicted"), "A single over-budget result must be retained alone.");
        Assert(string.Concat(ReadAll(giant, 200, 32768)) == giantText, "A single oversized retained result must remain lossless.");
        string afterGiant = Snapshot("AfterGiant");
        Assert(ToolResultPager.ReadPage(giant["resultId"].AsString).Contains("evicted"), "A subsequent result may evict the older over-budget singleton.");
        Parse(ToolResultPager.ReadPage(afterGiant));

        ToolResultPager.ConfigureForTests(() => now, 32, 8 * 1024 * 1024, TimeSpan.FromMinutes(15));
        string oldest = Snapshot("Oldest");
        for (int i = 0; i < 32; i++) Snapshot("Result" + i);
        Assert(ToolResultPager.ReadPage(oldest).Contains("evicted"), "The production entry capacity must retain at most 32 snapshots.");
        ToolResultPager.ResetForTests();
    }

    private static void SavedPageRecognition()
    {
        DateTime now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        ToolResultPager.ConfigureForTests(() => now, 2, 100000, TimeSpan.FromMinutes(15));
        string actual = ToolResultPager.CreateFirstPage("Authentic", new string('p', 3000), limit: 1);
        Assert(ToolResultPager.IsSavedPage(actual), "An exact retained page must be recognized without generating another snapshot.");
        var parsed = Parse(actual);
        string id = parsed["resultId"].AsString;
        string continuation = ToolResultPager.ReadPage(id, offset: 1, limit: 2, maxChars: 1024);
        Assert(ToolResultPager.IsSavedPage(continuation), "A continuation with nondefault budgets must be recognized from its retained source.");
        Assert(ToolResultPager.IsSavedPage(ToolResultPager.ReadPage(id, int.MaxValue)), "An authentic terminal page must be recognized.");
        Assert(!ToolResultPager.IsSavedPage(null) && !ToolResultPager.IsSavedPage("plain result") && !ToolResultPager.IsSavedPage("{}"), "Plain text and unrelated JSON must not impersonate a saved page.");
        foreach (string field in new[] { "offset", "limit", "maxChars" })
        {
            var bad = JNode.Parse(actual);
            bad["page"].AsObject[field] = JNode.Num(1.5);
            Assert(!ToolResultPager.IsSavedPage(bad.ToJson()), "Fractional paging numbers must not impersonate valid saved pages.");
            bad["page"].AsObject[field] = JNode.Str("1");
            Assert(!ToolResultPager.IsSavedPage(bad.ToJson()), "String paging numbers must not impersonate valid saved pages.");
            bad["page"].AsObject[field] = JNode.Num((double)int.MaxValue + 1);
            Assert(!ToolResultPager.IsSavedPage(bad.ToJson()), "Overflow paging numbers must not impersonate valid saved pages.");
        }
        foreach (var replacement in new[] { (field: "offset", value: -1), (field: "limit", value: 201), (field: "maxChars", value: 32769) })
        {
            var bad = JNode.Parse(actual);
            bad["page"].AsObject[replacement.field] = JNode.Num(replacement.value);
            Assert(!ToolResultPager.IsSavedPage(bad.ToJson()), "Out-of-contract paging values must fail saved-page recognition.");
        }
        var forged = JNode.Parse(actual);
        forged.AsObject["text"] = JNode.Str(new string('f', 50000));
        Assert(!ToolResultPager.IsSavedPage(forged.ToJson()), "A forged large text payload with otherwise authentic metadata must not bypass paging.");
        forged = JNode.Parse(actual);
        forged.AsObject["resultId"] = JNode.Str("unknown");
        Assert(!ToolResultPager.IsSavedPage(forged.ToJson()), "Unretained snapshot IDs must fail recognition.");
        forged = JNode.Parse(actual);
        forged.AsObject["toolName"] = JNode.Str("Other");
        Assert(!ToolResultPager.IsSavedPage(forged.ToJson()), "Snapshot owner metadata must match the original generated page.");
        Assert(!ToolResultPager.IsSavedPage(" " + actual), "Only an exact generated serialization may be exempted from paging.");
        Snapshot("Second");
        Assert(ToolResultPager.IsSavedPage(actual), "Saved-page recognition must not disturb a retained snapshot.");
        Snapshot("Third");
        Assert(!ToolResultPager.IsSavedPage(actual), "Recognition must not touch LRU ordering or keep an evicted result alive.");
        string expiring = ToolResultPager.CreateFirstPage("Expiring", new string('t', 3000), limit: 1);
        now = now.AddMinutes(15);
        Assert(!ToolResultPager.IsSavedPage(expiring), "Expired generated pages must not bypass the current paging budget.");
        ToolResultPager.ResetForTests();
    }

    private static void Concurrency()
    {
        ToolResultPager.ResetForTests();
        var text = string.Concat(Enumerable.Range(0, 150).Select(i => "Concurrent😀 " + i + "\r\n"));
        var ids = new string[24];
        Parallel.For(0, ids.Length, i => ids[i] = Parse(ToolResultPager.CreateFirstPage("Thread" + i, text, limit: 1))["resultId"].AsString);
        Assert(ids.Distinct(StringComparer.Ordinal).Count() == ids.Length, "Concurrent snapshot creation must use unique random identifiers.");
        Parallel.For(0, ids.Length, i =>
        {
            var first = Parse(ToolResultPager.ReadPage(ids[i], limit: 17, maxChars: 1024));
            Assert(first["toolName"].AsString == "Thread" + i, "Concurrent reads must retain the correct snapshot owner.");
            Assert(string.Concat(ReadAll(first, 17, 1024)) == text, "Concurrent reads must reconstruct isolated immutable snapshots.");
        });
        string id = ids[0];
        Parallel.For(0, 100, i =>
        {
            var page = Parse(ToolResultPager.ReadPage(id, offset: i, limit: 1, maxChars: 1024));
            CheckPage(page, i, 1, 1024);
            Assert(page["text"].AsString == "Concurrent😀 " + i + "\r\n", "Concurrent random-access pages must use stable segment offsets.");
        });
    }

    private static string Snapshot(string name) => Parse(ToolResultPager.CreateFirstPage(name, new string('x', 4000), limit: 1))["resultId"].AsString;

    private static List<string> ReadAll(JNode initial, int limit, int maxChars)
    {
        string id = initial["resultId"].AsString;
        string tool = initial["toolName"].AsString;
        string expires = initial["expiresAt"].AsString;
        int total = initial["page"]["total"].AsInt;
        var fragments = new List<string>();
        int offset = 0;
        while (true)
        {
            var response = Parse(ToolResultPager.ReadPage(id, offset, limit, maxChars));
            CheckPage(response, offset, limit, maxChars);
            Assert(response["resultId"].AsString == id && response["toolName"].AsString == tool && response["expiresAt"].AsString == expires, "Continuation identity and expiration must remain stable.");
            Assert(response["page"]["total"].AsInt == total, "A retained snapshot must have a stable total.");
            fragments.Add(response["text"].AsString);
            if (!response["page"]["hasMore"].AsBool) break;
            Assert(response["page"]["returned"].AsInt > 0, "Every nonterminal page must advance at least one record.");
            offset = response["page"]["nextOffset"].AsInt;
        }
        return fragments;
    }

    private static void CheckPage(JNode response, int offset, int limit, int maxChars)
    {
        var page = response["page"];
        Assert(page["unit"].AsString == "textSegments", "The offset unit must be explicit in every metadata result.");
        Assert(page["offset"].AsInt == offset && page["limit"].AsInt == limit && page["maxChars"].AsInt == maxChars, "Metadata must reflect the exact paging request.");
        int available = offset < page["total"].AsInt ? page["total"].AsInt - offset : 0;
        int returned = page["returned"].AsInt;
        Assert(returned >= 0 && returned <= Math.Min(limit, available), "Record count must obey the page limit and remaining total.");
        Assert(page["returnedChars"].AsInt == response["text"].AsString.Length && page["returnedChars"].AsInt <= maxChars, "Character count must match the bounded fragment.");
        Assert(page["remaining"].AsInt == available - returned, "Remaining count must exclude exactly the returned records.");
        bool hasMore = available > returned;
        Assert(page["hasMore"].AsBool == hasMore, "hasMore must reflect exact remaining records.");
        Assert(hasMore ? page["nextOffset"].AsInt == offset + returned : page["nextOffset"].IsNull, "Continuation must advance by actual returned records and terminate with null.");
        Assert(response["nextTool"].AsString == "ReadUnityToolResultPage", "The next-read tool must be discoverable from every paged result.");
    }

    private static void CheckUnicodeAndNewlineBoundaries(string original, List<string> fragments)
    {
        int position = 0;
        foreach (string fragment in fragments)
        {
            Assert(fragment.Length == 0 || !char.IsLowSurrogate(fragment[0]), "No page may begin in the middle of a surrogate pair.");
            Assert(fragment.Length == 0 || !char.IsHighSurrogate(fragment[fragment.Length - 1]), "No page may end in the middle of a surrogate pair.");
            position += fragment.Length;
            if (position > 0 && position < original.Length)
                Assert(original[position - 1] != '\r' || original[position] != '\n', "No page may split CRLF into separate fragments.");
        }
    }

    private static JNode Parse(string json)
    {
        Assert(!json.StartsWith("Error:", StringComparison.Ordinal), json);
        var result = JNode.Parse(json);
        Assert(result.Type == JNode.JType.Object && result["page"].Type == JNode.JType.Object, "Paged results must be valid metadata JSON: " + json);
        return result;
    }
}
