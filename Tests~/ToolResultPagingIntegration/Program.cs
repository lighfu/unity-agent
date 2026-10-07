using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.Editor.Tools;

internal static class Program
{
    static int _checks;
    static int Main()
    {
        try
        {
            Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", JNode.Obj(("resultId", JNode.Str("unknown"))), out string seedError) == null
                && seedError.Contains("initialized"), "reader allowed before gate seed");
            ListenerThreadTools.RefreshReaderGateOnMainThread();
            Invocation(); Validation(); AsyncImagesErrors(); ErrorPageAuthenticity(); ReaderGate(); FastRoutes(); SearchAndSchemas(); HttpRoutes(); BridgeRoutes();
            Console.WriteLine("Tool result paging integration tests passed: " + _checks + " checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ToolResultPager.ResetForTests(); }
    }

    static void Assert(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
    static JNode Envelope(string name, JNode args = null, int limit = 50, int offset = 0, int maxChars = 8192) => JNode.Obj(
        ("name", JNode.Str(name)), ("arguments", args ?? JNode.Obj()), ("resultLimit", JNode.Num(limit)),
        ("resultOffset", JNode.Num(offset)), ("resultMaxChars", JNode.Num(maxChars)));
    static PendingCall Invoke(string name, JNode args = null)
    {
        var call = new PendingCall(name, args ?? JNode.Obj());
        call.MarkExecutionStart(); Invoker.Invoke(call);
        Pump(() => call.Wait(0), true);
        return call;
    }
    static void Pump(Func<bool> done, bool ticks)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > 5000) throw new Exception("operation timed out");
            if (ticks) UnityEditor.EditorApplication.Tick();
            Thread.Sleep(1);
        }
    }
    static string JoinPages(string initial, Func<JNode, string> read)
    {
        var page = JNode.Parse(initial);
        var text = new StringBuilder(page["text"].AsString);
        string id = page["resultId"].AsString;
        int total = page["page"]["total"].AsInt;
        int iterations = 0;
        while (page["page"]["hasMore"].AsBool)
        {
            int next = page["page"]["nextOffset"].AsInt;
            Assert(next > page["page"]["offset"].AsInt, "continuation did not advance");
            page = JNode.Parse(read(JNode.Obj(("resultId", JNode.Str(id)), ("offset", JNode.Num(next)), ("limit", JNode.Num(17)))));
            Assert(page["resultId"].AsString == id && page["page"]["total"].AsInt == total, "snapshot changed on continuation");
            text.Append(page["text"].AsString);
            Assert(++iterations < 1000, "continuation loop");
        }
        return text.ToString();
    }
    static void Invocation()
    {
        ToolResultPager.ResetForTests(); FixtureTools.Calls = 0;
        string eventText = null;
        Action<string, string, bool> finish = (name, text, error) => eventText = text;
        AgentMCPServer.OnCallFinish += finish;
        try
        {
            var call = Invoke("ExecuteUnityTool", Envelope("GetPagingFixture", JNode.Obj(("offset", JNode.Num(7)), ("limit", JNode.Num(9))), 3));
            Assert(call.Error == null && FixtureTools.Calls == 1, "target did not execute exactly once");
            Assert(FixtureTools.NativeOffset == 7 && FixtureTools.NativeLimit == 9, "transport options replaced native offset/limit");
            var page = JNode.Parse(call.ResultText);
            Assert(page["page"]["returned"].AsInt == 3 && page["page"]["total"].AsInt == 205, "custom envelope page ignored");
            Assert(eventText == FixtureTools.LongText && ToolCallStats.LastResultChars == FixtureTools.LongText.Length, "UI event or stats lost full result");
            Assert(JoinPages(call.ResultText, args => Invoke("ReadUnityToolResultPage", args).ResultText) == FixtureTools.LongText, "reader changed completed result");
            Assert(FixtureTools.Calls == 1, "reading continuation invoked original action");
            var reader = Invoke("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", JNode.Obj(("resultId", page["resultId"]), ("offset", JNode.Num(3))), 1));
            Assert(JNode.Parse(reader.ResultText)["resultId"].AsString == page["resultId"].AsString, "reader was paged twice");
            var mixedCase = Invoke("readUnityToolResultPAGE", JNode.Obj(("resultId", page["resultId"]), ("offset", JNode.Num(3))));
            Assert(JNode.Parse(mixedCase.ResultText)["resultId"].AsString == page["resultId"].AsString, "mixed-case reader was paged twice");
            var mixedArgs = JNode.Obj(("ResultId", page["resultId"]), ("Offset", JNode.Num(3)), ("Limit", JNode.Num(2)));
            Assert(JNode.Parse(Invoke("ReadUnityToolResultPage", mixedArgs).ResultText)["page"]["returned"].AsInt == 2, "reader case-insensitive binding differs from other tools");
            var fastMixed = ListenerThreadTools.Match("readUnityToolResultPAGE", mixedArgs, out string mixedError);
            Assert(mixedError == null && JNode.Parse(fastMixed())["page"]["offset"].AsInt == 3, "fast reader case binding differs from Invoker");
            var direct = Invoke("GetPagingFixture");
            Assert(JNode.Parse(direct.ResultText)["page"]["returned"].AsInt == 50, "direct tool default is unbounded");
            Assert(Invoke("GetPagingShortFixture").ResultText == "short", "short result changed");
            var warning = Invoke("GetPagingFixture", JNode.Obj(("unused", JNode.Str("value"))));
            Assert(JNode.Parse(warning.ResultText)["text"].AsString.StartsWith("Warning:"), "unknown argument warning was not in snapshot");
            var terminal = Invoke("ExecuteUnityTool", Envelope("GetPagingFixture", offset: int.MaxValue));
            Assert(!JNode.Parse(terminal.ResultText)["page"]["hasMore"].AsBool, "terminal offset overflowed");
        }
        finally { AgentMCPServer.OnCallFinish -= finish; }
    }
    static void Validation()
    {
        foreach (string key in new[] { "resultOffset", "resultLimit", "resultMaxChars" })
        foreach (var raw in new[] { JNode.Str("2"), JNode.NullNode, JNode.Bool(true), JNode.Num(1.5), JNode.Num(double.MaxValue), JNode.Num(double.NaN), JNode.Num(double.PositiveInfinity) })
        {
            var args = Envelope("GetPagingFixture"); args.AsObject[key] = raw;
            int before = FixtureTools.Calls;
            var result = Invoke("ExecuteUnityTool", args);
            Assert(result.ErrorCode == -32602 && result.Error != null && FixtureTools.Calls == before, "invalid envelope invoked target: " + key);
            var fastArgs = Envelope("AnswerModalDialog"); fastArgs.AsObject[key] = raw;
            int beforeFast = ModalDialogTools.Calls;
            Assert(ListenerThreadTools.Match("ExecuteUnityTool", fastArgs, out string error) == null && error != null && ModalDialogTools.Calls == beforeFast,
                "invalid envelope reached fast action: " + key);
        }
        foreach (var args in new[] { Envelope("GetPagingFixture", limit: 0), Envelope("GetPagingFixture", limit: 201), Envelope("GetPagingFixture", offset: -1),
            Envelope("GetPagingFixture", maxChars: 1023), Envelope("GetPagingFixture", maxChars: 32769) })
        {
            int before = FixtureTools.Calls;
            Assert(Invoke("ExecuteUnityTool", args).ErrorCode == -32602 && FixtureTools.Calls == before, "invalid bounds invoked target");
        }
        foreach (var raw in new[] { JNode.Str("0"), JNode.Bool(false), JNode.Num(0.5), JNode.NullNode })
        {
            var args = JNode.Obj(("resultId", JNode.Str("unknown")), ("offset", raw));
            Assert(Invoke("ReadUnityToolResultPage", args).ErrorCode == -32602, "reader accepted noninteger offset");
            Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", args, out var error) == null && error != null, "fast reader accepted noninteger offset");
        }
        Assert(Invoke("ExecuteUnityTool", Envelope("ExecuteUnityTool")).Error != null, "nested Execute accepted");
        var unknownReader = JNode.Obj(("resultId", JNode.Str("unknown")), (new string('u', 30000), JNode.Str("unused")));
        var readerFailure = Invoke("ReadUnityToolResultPage", unknownReader);
        Assert(readerFailure.ErrorCode == -32602 && readerFailure.Error != null, "reader unknown-key warning bypassed result budget");
        Assert(readerFailure.Error.Length < 300, "reader echoed an unbounded unknown key");
        Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", unknownReader, out string unknownError) == null && unknownError.Length < 300,
            "fast reader unknown-key validation differs from Invoker");
        foreach (string key in new[] { "resultId", "offset", "limit", "maxChars" })
        {
            var duplicate = JNode.Obj(("resultId", JNode.Str("unknown")));
            duplicate.AsObject[key] = key == "resultId" ? JNode.Str("unknown") : JNode.Num(key == "maxChars" ? 1024 : 1);
            duplicate.AsObject[key.ToUpperInvariant()] = duplicate[key];
            Assert(Invoke("ReadUnityToolResultPage", duplicate).ErrorCode == -32602, "canonical reader argument plus case variant was accepted: " + key);
            Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", duplicate, out string duplicateError) == null && duplicateError != null,
                "fast reader accepted duplicate case variants: " + key);
        }
    }
    static void AsyncImagesErrors()
    {
        int before = FixtureTools.Calls;
        var async = Invoke("GetPagingAsyncFixture");
        Assert(FixtureTools.Calls == before + 1 && JNode.Parse(async.ResultText)["page"]["returned"].AsInt == 50, "async result not paged");
        var image = Invoke("GetPagingImageFixture");
        Assert(image.ImageBytes.SequenceEqual(new byte[] { 1, 2, 3 }) && image.ImageMimeType == "image/test", "page destroyed image payload");
        Assert(SceneViewTools.PendingImageBytes == null && JNode.Parse(image.ResultText)["page"]["returned"].AsInt == 50, "image slot not consumed or text unbounded");
        var choice = new PendingCall("GetPagingChoiceFixture", JNode.Obj()); Invoker.Invoke(choice);
        Assert(!choice.Wait(0), "choice completed before resolution");
        UserChoiceState.CustomText = new string('a', 20000); UserChoiceState.SelectedIndex = 0;
        Pump(() => choice.Wait(0), true);
        Assert(JNode.Parse(choice.ResultText)["page"]["hasMore"].AsBool, "resolved choice is unbounded");
        string eventError = null;
        Action<string, string, bool> finish = (name, text, isError) => { if (isError) eventError = text; };
        AgentMCPServer.OnCallFinish += finish;
        try
        {
            var failure = Invoke("GetPagingFailureFixture");
            Assert(failure.ErrorCode == -32000 && JNode.Parse(failure.Error)["page"]["hasMore"].AsBool, "exception error not bounded");
            Assert(eventError.Length > 20000, "error UI event lost original text");
            Assert(JoinPages(failure.Error, args => Invoke("ReadUnityToolResultPage", args).ResultText) == eventError, "error continuation is lossy");
            var pending = new PendingCall("Fixture", JNode.Obj()); pending.SetError("message", new string('d', 30000), -32603);
            Assert(pending.Error == "message" && pending.ErrorCode == -32603 && JNode.Parse(pending.ErrorData)["page"]["hasMore"].AsBool, "error code/data not preserved");
        }
        finally { AgentMCPServer.OnCallFinish -= finish; }
    }
    static void FastRoutes()
    {
        foreach (string name in new[] { "GetEditorState", "GetUIActionResult", "GetIMGUIInspectionResult", "AnswerModalDialog", "ListModalAutoAnswers", "GetVRChatBuildTestResult" })
        {
            var direct = ListenerThreadTools.Match(name, JNode.Obj(), out string error);
            Assert(error == null && JNode.Parse(direct())["page"]["returned"].AsInt == 50, "fast default unbounded: " + name);
            var wrapped = ListenerThreadTools.Match("ExecuteUnityTool", Envelope(name, limit: 2), out error);
            Assert(error == null && JNode.Parse(wrapped())["page"]["returned"].AsInt == 2, "fast wrapped custom limit ignored: " + name);
        }
        string initial = ListenerThreadTools.Match("GetEditorState", JNode.Obj())();
        int before = FixtureTools.Calls;
        Assert(JoinPages(initial, args => ListenerThreadTools.Match("ReadUnityToolResultPage", args)()) == FixtureTools.LongText && FixtureTools.Calls == before,
            "fast continuation reran action or lost text");
    }
    static void ErrorPageAuthenticity()
    {
        string realPage = Invoke("ExecuteUnityTool", Envelope("GetPagingFixture", limit: 2)).ResultText;
        var existing = new PendingCall("Fixture", JNode.Obj());
        existing.SetError(realPage, realPage, -32603);
        Assert(existing.Error == realPage && existing.ErrorData == realPage, "genuine cached error page was snapshotted again");
        string fake = JNode.Obj(("nextTool", JNode.Str("ReadUnityToolResultPage")), ("page", JNode.Obj()),
            ("text", JNode.Str(new string('x', 30000)))).ToJson();
        var fakeError = new PendingCall("Fixture", JNode.Obj()); fakeError.SetError(fake, fake, -32603);
        Assert(fakeError.Error != fake && fakeError.ErrorData != fake, "fake page-shaped JSON bypassed error budget");
        Assert(JNode.Parse(fakeError.Error)["page"]["returnedChars"].AsInt <= ToolResultPager.DefaultMaxChars, "fake error not bounded");
        Assert(JoinPages(fakeError.Error, args => Invoke("ReadUnityToolResultPage", args).ResultText) == fake, "fake error paging lost original JSON");
        var forged = JNode.Parse(realPage);
        forged.AsObject["text"] = JNode.Str(new string('f', 30000));
        forged.AsObject["padding"] = JNode.Str(new string('p', 40000));
        string forgedText = forged.ToJson();
        var forgedError = new PendingCall("Fixture", JNode.Obj()); forgedError.SetError(forgedText, forgedText);
        Assert(forgedError.Error != forgedText && forgedError.ErrorData != forgedText, "real ID with forged payload bypassed budget");
        Assert(JoinPages(forgedError.Error, args => Invoke("ReadUnityToolResultPage", args).ResultText) == forgedText, "forged payload was not preserved by bounded paging");
    }
    static void ReaderGate()
    {
        var page = JNode.Parse(Invoke("GetPagingFixture").ResultText);
        var args = JNode.Obj(("resultId", page["resultId"]), ("offset", page["page"]["nextOffset"]));
        AgentSettings.DisabledTool = "ReadUnityToolResultPage";
        try
        {
            Assert(Invoke("ReadUnityToolResultPage", args).ErrorCode == -32000, "registered reader did not honor disabled setting");
            Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", args, out string error) == null && error != null,
                "disabled reader bypassed direct listener gate");
            Assert(ListenerThreadTools.Match("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", args), out error) == null && error != null,
                "disabled reader bypassed wrapped listener gate");
        }
        finally { AgentSettings.DisabledTool = null; }
        Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", args, out string enabledError) != null && enabledError == null,
            "reader was not immediately reenabled");
        AgentSettings.MCPServerExposeRisk = (AjisaiFlow.UnityAgent.SDK.ToolRisk)(-1);
        try
        {
            Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", args, out string error) == null && error != null, "reader bypassed direct risk gate");
            Assert(ListenerThreadTools.Match("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", args), out error) == null && error != null,
                "reader bypassed wrapped risk gate");
        }
        finally { AgentSettings.MCPServerExposeRisk = AjisaiFlow.UnityAgent.SDK.ToolRisk.Dangerous; }
        Assert(ListenerThreadTools.Match("ReadUnityToolResultPage", args, out enabledError) != null && enabledError == null, "risk restoration did not reenable reader");
    }
    static void SearchAndSchemas()
    {
        var listing = Handlers.HandleToolsList(JNode.Obj())["tools"].AsArray;
        Assert(listing.Count == 4, "top-level tool surface expanded");
        var execute = listing.Single(x => x["name"].AsString == "ExecuteUnityTool")["inputSchema"]["properties"];
        Assert(execute["resultOffset"]["default"].AsInt == 0 && execute["resultLimit"]["default"].AsInt == 50 && execute["resultMaxChars"]["default"].AsInt == 8192, "envelope schema mismatch");
        var search = Invoke("SearchUnityTool", JNode.Obj(("query", JNode.Str("paging fixture")), ("limit", JNode.Num(2))));
        string result = search.ResultText;
        Assert(result.IndexOf("GetPagingAsyncFixture", StringComparison.Ordinal) < result.IndexOf("GetPagingChoiceFixture", StringComparison.Ordinal), "search equal-score sort unstable");
        int marker = result.LastIndexOf("Page: ", StringComparison.Ordinal);
        var page = JNode.Parse(result.Substring(marker + 6));
        Assert(page["returned"].AsInt == 2 && page["hasMore"].AsBool, "search metadata invalid");
        var terminal = Handlers.ImplSearchTool("paging fixture", 2, int.MaxValue);
        Assert(!JNode.Parse(terminal.Substring(terminal.LastIndexOf("Page: ", StringComparison.Ordinal) + 6))["hasMore"].AsBool, "search terminal overflow");
        Assert(Invoke("SearchUnityTool", JNode.Obj(("query", JNode.Str("fixture")), ("limit", JNode.Num(0)))).ErrorCode == -32602, "search invalid limit accepted");
        Assert(Handlers.ImplDescribeTool("GetPagingFixture").Contains("arguments.offset/limit") && Handlers.ImplDescribeTool("GetPagingFixture").Contains("ReadUnityToolResultPage"), "Describe omitted paging guidance");
        var noMatches = Handlers.ImplSearchTool("not-a-fixture-name-123", 2, 1);
        Assert(JNode.Parse(noMatches.Substring(noMatches.LastIndexOf("Page: ", StringComparison.Ordinal) + 6))["total"].AsInt == 0, "empty search lost zero-total page");
    }
    static int Port()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port;
    }
    static JNode Post(HttpClient client, string name, JNode args, bool ticks = true)
    {
        var body = JNode.Obj(("jsonrpc", JNode.Str("2.0")), ("id", JNode.Num(1)), ("method", JNode.Str("tools/call")),
            ("params", JNode.Obj(("name", JNode.Str(name)), ("arguments", args))));
        var request = client.PostAsync("mcp", new StringContent(body.ToJson(), Encoding.UTF8, "application/json"));
        Pump(() => request.IsCompleted, ticks);
        return JNode.Parse(request.GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }
    static void HttpRoutes()
    {
        var startupPage = JNode.Parse(Invoke("GetPagingFixture").ResultText);
        var startupArgs = JNode.Obj(("resultId", startupPage["resultId"]));
        AgentSettings.DisabledTool = "ReadUnityToolResultPage";
        var server = new AgentMCPServer(); int port = Port(); server.Start(port, "test-token");
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:" + port + "/") };
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        try
        {
            Assert(Post(client, "ReadUnityToolResultPage", startupArgs, false)["error"]["code"].AsInt == -32000, "HTTP startup failed to seed disabled reader");
            AgentSettings.DisabledTool = null;
            int before = FixtureTools.Calls;
            var response = Post(client, "ExecuteUnityTool", Envelope("GetPagingFixture", limit: 4));
            string text = response["result"]["content"][0]["text"].AsString;
            Assert(JNode.Parse(text)["page"]["returned"].AsInt == 4 && FixtureTools.Calls == before + 1, "HTTP envelope not honored");
            var image = Post(client, "GetPagingImageFixture", JNode.Obj())["result"]["content"].AsArray;
            Assert(image.Count == 2 && image[1]["data"].AsString == "AQID" && image[1]["mimeType"].AsString == "image/test", "HTTP image content lost");
            MainThreadWatchdog.Stalled = true;
            var page = JNode.Parse(text);
            var reader = Post(client, "ReadUnityToolResultPage", JNode.Obj(("resultId", page["resultId"]), ("offset", page["page"]["nextOffset"])), false);
            Assert(JNode.Parse(reader["result"]["content"][0]["text"].AsString)["resultId"].AsString == page["resultId"].AsString, "HTTP reader blocked behind main thread");
            int beforeFast = ModalDialogTools.Calls;
            var invalid = Post(client, "ExecuteUnityTool", Envelope("AnswerModalDialog", limit: 0), false);
            Assert(invalid["error"]["code"].AsInt == -32602 && ModalDialogTools.Calls == beforeFast, "HTTP invalid fast envelope performed mutation");
            var fast = Post(client, "ExecuteUnityTool", Envelope("GetEditorState", limit: 2), false);
            Assert(JNode.Parse(fast["result"]["content"][0]["text"].AsString)["page"]["returned"].AsInt == 2, "HTTP fast result unbounded");
            AgentSettings.DisabledTool = "ReadUnityToolResultPage";
            Assert(Post(client, "ReadUnityToolResultPage", startupArgs, false)["error"]["code"].AsInt == -32000, "HTTP direct disabled reader allowed during stall");
            Assert(Post(client, "ExecuteUnityTool", Envelope("ReadUnityToolResultPage", startupArgs), false)["error"]["code"].AsInt == -32000, "HTTP wrapped disabled reader allowed during stall");
            AgentSettings.DisabledTool = null;
            Assert(Post(client, "ReadUnityToolResultPage", startupArgs, false)["result"]["content"].AsArray != null, "HTTP reenabled reader required main-thread wait");
            AgentSettings.MCPServerExposeRisk = (AjisaiFlow.UnityAgent.SDK.ToolRisk)(-1);
            Assert(Post(client, "ReadUnityToolResultPage", startupArgs, false)["error"]["code"].AsInt == -32000, "HTTP reader risk gate bypassed");
            AgentSettings.MCPServerExposeRisk = AjisaiFlow.UnityAgent.SDK.ToolRisk.Dangerous;
            EditorStateTools.Throw = true;
            var failure = Post(client, "ExecuteUnityTool", Envelope("GetEditorState", limit: 1, maxChars: 1024), false);
            var errorPage = JNode.Parse(failure["error"]["data"].AsString);
            Assert(failure["error"]["code"].AsInt == -32603 && errorPage["page"]["limit"].AsInt == 1
                && errorPage["page"]["maxChars"].AsInt == 1024 && errorPage["page"]["returnedChars"].AsInt <= 1024,
                "HTTP fast exception ignored requested budget or lost RPC error");
            EditorStateTools.Throw = false;
        }
        finally { EditorStateTools.Throw = false; AgentSettings.DisabledTool = null; AgentSettings.MCPServerExposeRisk = AjisaiFlow.UnityAgent.SDK.ToolRisk.Dangerous;
            MainThreadWatchdog.Stalled = false; server.Stop(); }
    }
    static void BridgeRoutes()
    {
        var startupPage = JNode.Parse(Invoke("GetPagingFixture").ResultText);
        var startupArgs = JNode.Obj(("resultId", startupPage["resultId"]));
        AgentSettings.DisabledTool = "ReadUnityToolResultPage";
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var peerTask = Task.Run(() =>
        {
            var socket = listener.AcceptTcpClient();
            var reader = new System.IO.StreamReader(socket.GetStream(), Encoding.UTF8);
            var writer = new System.IO.StreamWriter(socket.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Assert(JNode.Parse(reader.ReadLine())["type"].AsString == "hello", "Bridge hello missing");
            writer.WriteLine("{\"type\":\"hello_ack\",\"ok\":true}");
            return (socket, reader, writer);
        });
        var bridge = new AgentMCPBridgeClient(); bridge.Connect(port, "test-token");
        var peer = peerTask.GetAwaiter().GetResult();
        JNode Send(string name, JNode args, bool ticks = true)
        {
            peer.writer.WriteLine(JNode.Obj(("type", JNode.Str("call")), ("id", JNode.Str("fixture")), ("tool", JNode.Str(name)), ("args", args)).ToJson());
            var result = Task.Run(() => peer.reader.ReadLine()); Pump(() => result.IsCompleted, ticks);
            return JNode.Parse(result.GetAwaiter().GetResult());
        }
        try
        {
            var deniedStartup = Send("ReadUnityToolResultPage", startupArgs, false);
            Assert(deniedStartup["type"].AsString == "error" && deniedStartup["code"].AsInt == -32000, "Bridge startup failed to seed disabled reader");
            AgentSettings.DisabledTool = null;
            int before = FixtureTools.Calls;
            var first = Send("ExecuteUnityTool", Envelope("GetPagingFixture", limit: 4));
            var page = JNode.Parse(first["text"].AsString);
            Assert(first["type"].AsString == "result" && page["page"]["returned"].AsInt == 4 && FixtureTools.Calls == before + 1, "C# Bridge envelope not honored");
            var image = Send("GetPagingImageFixture", JNode.Obj());
            Assert(image["imageBase64"].AsString == "AQID" && image["imageMimeType"].AsString == "image/test", "C# Bridge image frame changed");
            MainThreadWatchdog.Stalled = true;
            var read = Send("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", JNode.Obj(("resultId", page["resultId"]), ("offset", page["page"]["nextOffset"]))), false);
            Assert(JNode.Parse(read["text"].AsString)["resultId"].AsString == page["resultId"].AsString, "Bridge reader blocked during main-thread stall");
            int beforeFast = ModalDialogTools.Calls;
            var invalid = Send("ExecuteUnityTool", Envelope("AnswerModalDialog", limit: 0), false);
            Assert(invalid["type"].AsString == "error" && invalid["code"].AsInt == -32602 && ModalDialogTools.Calls == beforeFast, "Bridge invalid fast envelope performed mutation");
            var fast = Send("ExecuteUnityTool", Envelope("GetEditorState", limit: 2), false);
            Assert(JNode.Parse(fast["text"].AsString)["page"]["returned"].AsInt == 2, "Bridge fast result unbounded");
            AgentSettings.DisabledTool = "ReadUnityToolResultPage";
            Assert(Send("ReadUnityToolResultPage", startupArgs, false)["code"].AsInt == -32000, "Bridge direct disabled reader allowed during stall");
            Assert(Send("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", startupArgs), false)["code"].AsInt == -32000, "Bridge wrapped disabled reader allowed during stall");
            AgentSettings.DisabledTool = null;
            Assert(Send("ReadUnityToolResultPage", startupArgs, false)["type"].AsString == "result", "Bridge reenabled reader required main-thread wait");
            AgentSettings.MCPServerExposeRisk = (AjisaiFlow.UnityAgent.SDK.ToolRisk)(-1);
            Assert(Send("ExecuteUnityTool", Envelope("ReadUnityToolResultPage", startupArgs), false)["code"].AsInt == -32000, "Bridge reader risk gate bypassed");
            AgentSettings.MCPServerExposeRisk = AjisaiFlow.UnityAgent.SDK.ToolRisk.Dangerous;
            EditorStateTools.Throw = true;
            var failure = Send("ExecuteUnityTool", Envelope("GetEditorState", limit: 1, maxChars: 1024), false);
            var errorPage = JNode.Parse(failure["message"].AsString);
            Assert(failure["type"].AsString == "error" && failure["code"].AsInt == -32000 && errorPage["page"]["limit"].AsInt == 1
                && errorPage["page"]["maxChars"].AsInt == 1024 && errorPage["page"]["returnedChars"].AsInt <= 1024,
                "Bridge fast exception ignored requested budget or became success");
            EditorStateTools.Throw = false;
        }
        finally { EditorStateTools.Throw = false; AgentSettings.DisabledTool = null; AgentSettings.MCPServerExposeRisk = AjisaiFlow.UnityAgent.SDK.ToolRisk.Dangerous;
            MainThreadWatchdog.Stalled = false; bridge.Disconnect("test-end"); peer.socket.Dispose(); }
    }
}
