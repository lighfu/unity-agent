using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MCP;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        try
        {
            string[] names = { "Neutral", "Fist", "Open", "FingerPoint", "Victory", "RockNRoll", "Gun", "ThumbsUp" };
            for (int i = 0; i < names.Length; i++)
            {
                Assert(GestureManagerTools.TryParseGestureIndex(names[i], out int named) && named == i, "gesture name " + names[i]);
                Assert(GestureManagerTools.TryParseGestureIndex(i.ToString(CultureInfo.InvariantCulture), out int numeric) && numeric == i, "numeric gesture " + i);
            }
            foreach (string name in new[] { "Rock&Roll", "rock-and-roll", "rock_n_roll", " ROCK N ROLL " })
                Assert(GestureManagerTools.TryParseGestureIndex(name, out int index) && index == 5, "UI rock-and-roll spelling " + name);
            Assert(GestureManagerTools.TryParseGestureIndex("Thumbs Up", out int thumb) && thumb == 7, "spaced hand name");
            Assert(GestureManagerTools.TryParseGestureIndex("Idle", out int idle) && idle == 0, "idle alias");
            foreach (string invalid in new[] { null, "", " ", "-1", "8", "100000000000000", "1.0", "ThumbsDown", "Unknown" })
                Assert(!GestureManagerTools.TryParseGestureIndex(invalid, out _), "invalid gesture must fail: " + invalid);
            foreach (float valid in new[] { -1f, 0f, float.Epsilon, 0.5f, 1f })
                Assert(GestureManagerTools.IsGestureWeight(valid), "valid gesture weight " + valid);
            foreach (float invalid in new[] { -2f, -0.001f, 1.001f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert(!GestureManagerTools.IsGestureWeight(invalid), "invalid weight must fail: " + invalid);

            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                Assert(GestureManagerTools.TryParseGestureIndex("FINGERPOINT", out int finger) && finger == 3, "gesture names must be culture independent");
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }

            var parameter = typeof(GestureManagerTools).GetMethod("GestureManagerGetParam").GetParameters()[0];
            Assert(parameter.IsOptional && (string)parameter.DefaultValue == "", "GetParam must be callable without arguments through tool schema");
            Assert(GestureManagerTools.GestureManagerGetParam().Contains("not installed"), "no-argument GetParam must return normal missing-package error");
            Assert(GestureManagerTools.GestureManagerGetGestures().Contains("not installed"), "read gestures missing-package fallback");
            Assert(GestureManagerTools.GestureManagerSetGesture("left", "Fist").Contains("not installed"), "write gesture missing-package fallback");
            Assert(GestureManagerTools.GestureManagerSetGesture("feet", "Fist").StartsWith("Error:"), "unknown hand must fail");
            Assert(GestureManagerTools.ListGestureManagerParams(limit: -1).StartsWith("Error:"), "negative output limit must fail");
            var details = typeof(GestureManagerTools).GetMethod("GetGmAnimatorCurrentStateInfo").GetParameters()[1];
            Assert(details.IsOptional && (bool)details.DefaultValue, "Animator state read defaults to all details");
            CheckPaging();

            var tools = typeof(GestureManagerTools).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.GetCustomAttribute<AgentToolAttribute>() != null).ToArray();
            var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
            {
                Assert(toolNames.Add(tool.Name), "tool names must remain unique across partial files: " + tool.Name);
                if (tool.Name == "ExitPlayMode") continue; // This general Unity operation does not need GM.
                var arguments = tool.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType == typeof(string) ? (object)"left"
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                string result = (string)tool.Invoke(null, arguments);
                Assert(result.Contains("not installed"), "missing-package fallback for " + tool.Name);
            }
            Console.WriteLine($"Gesture Manager input/schema tests passed ({_checks} checks; installed Unity runtime is tested separately).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void CheckPaging()
    {
        foreach (int limit in new[] { 1, 50, 200 })
            Assert(GestureManagerPaging.Validate(0, limit) == null, "valid page size " + limit);
        foreach (int limit in new[] { int.MinValue, -1, 0, 201, int.MaxValue })
            Assert(GestureManagerPaging.Validate(0, limit) != null, "reject unsafe page size " + limit);
        Assert(GestureManagerPaging.Validate(-1, 50) != null, "reject negative offset");
        Assert(GestureManagerPaging.Validate(int.MaxValue, 50) == null, "large offset is valid terminal input");

        var items = Enumerable.Range(0, 205).Select(value => JNode.Num(value)).ToArray();
        foreach (int limit in new[] { 1, 17, 50, 200 })
        {
            var joined = new List<int>();
            int offset = 0;
            while (true)
            {
                var result = GestureManagerPaging.Page(items, offset, limit);
                var page = result["page"];
                Assert(result["items"].Count <= limit && page["returned"].AsInt == result["items"].Count,
                    "returned items obey page size");
                Assert(page["total"].AsInt == 205 && page["offset"].AsInt == offset && page["limit"].AsInt == limit,
                    "page reports the filtered collection size and requested position");
                joined.AddRange(result["items"].AsArray.Select(node => node.AsInt));
                Assert(page["remaining"].AsInt == 205 - joined.Count, "remaining excludes previous pages");
                if (!page["hasMore"].AsBool)
                {
                    Assert(page["nextOffset"].IsNull, "terminal page has no next position");
                    break;
                }
                Assert(page["nextOffset"].AsInt > offset, "every non-terminal page makes progress");
                offset = page["nextOffset"].AsInt;
            }
            Assert(joined.SequenceEqual(Enumerable.Range(0, 205)), "all pages reconstruct the collection without omissions or duplicates");
        }
        foreach (int offset in new[] { 205, 206, int.MaxValue })
        {
            var terminal = GestureManagerPaging.Page(items, offset, 200);
            Assert(terminal["items"].Count == 0 && terminal["page"]["returned"].AsInt == 0
                && terminal["page"]["remaining"].AsInt == 0 && !terminal["page"]["hasMore"].AsBool
                && terminal["page"]["nextOffset"].IsNull, "out-of-range offsets are empty terminal pages without overflow");
        }
        var empty = GestureManagerPaging.Page(Array.Empty<JNode>(), 0, 50);
        Assert(empty["items"].Count == 0 && empty["page"]["total"].AsInt == 0
            && !empty["page"]["hasMore"].AsBool, "empty collection returns terminal metadata");
        var budgeted = GestureManagerPaging.Metadata(100, 5, 50, 2);
        Assert(budgeted["nextOffset"].AsInt == 7 && budgeted["remaining"].AsInt == 93,
            "an additional budget continues at the first omitted record");
        var nearIntLimit = GestureManagerPaging.Metadata(int.MaxValue, int.MaxValue - 1, 200, 1);
        Assert(!nearIntLimit["hasMore"].AsBool && nearIntLimit["nextOffset"].IsNull,
            "metadata arithmetic is safe at int maximum");

        foreach (string name in new[] { "GetGestureManagerState", "GestureManagerGetParam", "ListGestureManagerParams", "GetGmAnimatorCurrentStateInfo",
            "GestureManagerGetExpressionMenu", "GestureManagerGetExpressionMenuParameters", "GestureManagerGetOscState",
            "GestureManagerGetRadialState" })
        {
            var method = typeof(GestureManagerTools).GetMethod(name);
            Assert(method != null, "paged tool exists: " + name);
            var parameters = method.GetParameters();
            Assert(parameters.Single(p => p.Name == "offset").IsOptional
                && (int)parameters.Single(p => p.Name == "offset").DefaultValue == 0, "default first page: " + name);
            Assert(parameters.Single(p => p.Name == "limit").IsOptional
                && (int)parameters.Single(p => p.Name == "limit").DefaultValue == 50, "bounded default: " + name);
        }
        Assert(GestureManagerTools.GestureManagerGetParam("GestureLeft", offset: 1).StartsWith("Error:"),
            "named single-value reads reject offset rather than ignoring it");
        Assert(GestureManagerTools.GestureManagerGetParam(limit: 0).StartsWith("Error:"), "blank GetParam rejects unbounded limit");
        Assert(GestureManagerTools.ListGestureManagerParams(offset: -1).StartsWith("Error:"), "List rejects negative offset");
        Assert(GestureManagerTools.GetGmAnimatorCurrentStateInfo(clipLimit: 0).Contains("clipLimit"),
            "clip page controls reject unbounded output");
    }
}
