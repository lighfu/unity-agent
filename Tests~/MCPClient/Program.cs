using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using AjisaiFlow.UnityAgent.Editor.MCP;

internal static class Program
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "echo")
        {
            string line;
            while ((line = Console.ReadLine()) != null)
                Console.WriteLine(line);
            return 0;
        }

        int failed = 0;
        Run("preserves notifications after a response", NotificationsAfterResponse, ref failed);
        Run("routes out-of-order responses to their own requests", OutOfOrderResponses, ref failed);
        Run("drains notifications while no request is waiting", IdleNotifications, ref failed);
        Run("ignores unsolicited and malformed responses", InvalidResponses, ref failed);
        Run("cleans up timed-out and canceled request state", RequestCleanup, ref failed);
        Run("handles a disposed process during send", DisposedProcessSend, ref failed);
        Run("writes complete JSONL messages in FIFO order", OrderedWrites, ref failed);
        return failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action test, ref int failed)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    static MCPClient Client() => new MCPClient("test", "unused", null, null);
    static object Invoke(MCPClient client, string name, params object[] args) =>
        typeof(MCPClient).GetMethod(name, PrivateInstance).Invoke(client, args);

    static void Register(MCPClient client, int id) =>
        Invoke(client, "SendRequest", "ping", JNode.Obj(), id);

    static void Receive(MCPClient client, params string[] lines)
    {
        var queue = (Queue<string>)typeof(MCPClient).GetField("_lineQueue", PrivateInstance).GetValue(client);
        foreach (var line in lines) queue.Enqueue(line);
    }

    static JNode Response(MCPClient client, int id)
    {
        JNode response = null;
        var routine = (IEnumerator)Invoke(client, "WaitForResponse", id, new Action<JNode>(r => response = r), 1);
        try
        {
            // An already buffered response must complete without yielding a frame.
            Assert(!routine.MoveNext(), "response was lost or routed to another request");
            Assert(response != null, "missing response");
            return response;
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }

    static void NotificationsAfterResponse()
    {
        using var client = Client();
        Register(client, 1);
        Receive(client, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}");
        Response(client, 1);
        var notifications = client.DrainNotifications();
        Assert(notifications?.Count == 1, "notification after matching response was discarded");
        Assert(notifications[0].Method == "notifications/tools/list_changed", "wrong notification");
        Assert(client.DrainNotifications() == null, "notification delivered twice");
    }

    static void OutOfOrderResponses()
    {
        using var client = Client();
        Register(client, 1);
        Register(client, 2);
        Receive(client, "{\"id\":2,\"result\":\"second\"}", "{\"id\":1,\"result\":\"first\"}");
        Assert(Response(client, 1)["result"].AsString == "first", "wrong first result");
        Assert(Response(client, 2)["result"].AsString == "second", "wrong second result");
    }

    static void IdleNotifications()
    {
        using var client = Client();
        Receive(client, "{\"method\":\"notifications/resources/list_changed\",\"params\":{\"n\":7}}");
        var notifications = client.DrainNotifications();
        Assert(notifications?.Count == 1, "idle notification was never processed");
        Assert(notifications[0].Params["n"].AsInt == 7, "notification parameters lost");
    }

    static void InvalidResponses()
    {
        using var client = Client();
        Register(client, 1);
        Receive(client, "not JSON", "{\"id\":99,\"result\":\"unknown\"}",
            "{\"id\":1.5,\"result\":\"fractional\"}", "{\"id\":1,\"result\":\"valid\"}");
        Assert(Response(client, 1)["result"].AsString == "valid", "accepted an invalid response ID");
    }

    static void OrderedWrites()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        // Environment.ProcessPath is the apphost for dotnet run, and dotnet for dotnet <dll>.
        if (System.IO.Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("echo");
        using var process = Process.Start(start);
        using var client = Client();
        typeof(MCPClient).GetField("_process", PrivateInstance).SetValue(client, process);
        const int count = 128;
        var read = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                Assert(line != null, "writer closed early");
                var message = JNode.Parse(line);
                Assert(message["id"].AsInt == i + 1, "JSONL frame was reordered or interleaved");
                Assert(message["payload"].AsString == new string('x', 8192), "JSONL payload was corrupted");
            }
        });
        try
        {
            for (int i = 0; i < count; i++)
                Invoke(client, "SendLine", JNode.Obj(("id", JNode.Num(i + 1)), ("payload", JNode.Str(new string('x', 8192)))).ToJson());
            Assert(read.Wait(TimeSpan.FromSeconds(15)), "timed out reading JSONL frames");
        }
        finally
        {
            // Unblock any writer if a failed assertion stopped the reader.
            if (!process.HasExited) process.Kill();
            process.WaitForExit();
        }
    }

    static void RequestCleanup()
    {
        using var client = Client();
        Register(client, 1);
        bool timedOut = false;
        var timeout = (IEnumerator)Invoke(client, "WaitForResponse", 1,
            new Action<JNode>(r => timedOut = r == null), 0);
        Assert(!timeout.MoveNext() && timedOut, "timeout did not complete");

        Register(client, 2);
        var canceled = (IEnumerator)Invoke(client, "WaitForResponse", 2,
            new Action<JNode>(_ => throw new InvalidOperationException("canceled request completed")), 1);
        Assert(canceled.MoveNext(), "request should wait for a response");
        ((IDisposable)canceled).Dispose();

        Receive(client, "{\"id\":1,\"result\":\"late\"}", "{\"id\":2,\"result\":\"canceled\"}");
        client.DrainNotifications();
        var responses = (Dictionary<int, JNode>)typeof(MCPClient).GetField("_responses", PrivateInstance).GetValue(client);
        var pending = (HashSet<int>)typeof(MCPClient).GetField("_pendingRequests", PrivateInstance).GetValue(client);
        Assert(responses.Count == 0 && pending.Count == 0, "finished request state was retained");
    }

    static void DisposedProcessSend()
    {
        var client = Client();
        var process = new Process();
        process.Dispose();
        typeof(MCPClient).GetField("_process", PrivateInstance).SetValue(client, process);

        // SendLine must absorb the disposed-process race instead of leaking an
        // ObjectDisposedException through the coroutine caller.
        try
        {
            Invoke(client, "SendLine", "{}");
        }
        finally
        {
            typeof(MCPClient).GetField("_process", PrivateInstance).SetValue(client, null);
            client.Dispose();
        }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
