using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.Editor.MCP;
using UnityEditor;

internal static class Program
{
    static int Main()
    {
        string project = Path.Combine(Path.GetTempPath(), "UnityAgent-MCPBridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        string bridgeDir = Path.Combine(project, "Library", "UnityAgent");
        Directory.CreateDirectory(bridgeDir);
        File.WriteAllText(Path.Combine(bridgeDir, "Bridge.lock"), Process.GetCurrentProcess().Id.ToString());
        UnityEngine.Application.dataPath = Path.Combine(project, "Assets");
        int failed = 0;
        using var peer = new BridgePeer();
        AgentSettings.MCPServerEnabled = true;
        AgentSettings.MCPServerMode = MCPServerMode.Bridge;
        AgentSettings.MCPBridgeInternalPort = peer.Port;
        try
        {
            Run("starts after reload without delayCall or natural updates", () => Startup(peer), ref failed);
            Run("executes queued background calls on main thread across frame limit", () => QueuedCalls(peer), ref failed);
            Run("reconnects after real bridge EOF without natural updates", () => Reconnect(peer), ref failed);
            Run("old readers cannot disconnect rapid replacement connections", () => RapidReconnect(peer), ref failed);
            Run("disabling stops the background wakeup timer", Disable, ref failed);
            Run("assembly reload disconnects and stops the wakeup timer", () => Reload(peer), ref failed);
            Run("editor quit sends a distinct shutdown and stops the wakeup timer", () => Quit(peer), ref failed);
        }
        finally
        {
            AssemblyReloadEvents.Reload();
            peer.Dispose();
            Directory.Delete(project, true);
        }
        return failed == 0 ? 0 : 1;
    }

    static void Startup(BridgePeer peer)
    {
        RuntimeHelpers.RunClassConstructor(typeof(AgentMCPServerBootstrap).TypeHandle);
        Assert(EditorApplication.UpdateCount == 0, "startup ran an update directly");
        WaitFor(() => AgentMCPBridgeClient.Shared.IsConnected && peer.Current != null, "initial connection");
        using var process = Process.GetCurrentProcess();
        Assert(peer.Current.UnityPid == process.Id, "hello did not identify the owning editor process");
        Assert(ModalAutoAnswer.InitializeCount == 0, "test accidentally invoked delayCall");
        WaitFor(() => EditorApplication.WorkerSignalCount > 0, "timer worker tick");
    }

    static void QueuedCalls(BridgePeer peer)
    {
        var connection = peer.Current;
        int before = Invoker.Executed.Count;
        int updates = EditorApplication.UpdateCount;
        for (int i = 0; i < 9; i++) connection.SendCall("call-" + i, "value-" + i);
        WaitFor(() => connection.Results.Count >= 9, "nine background calls");
        Assert(Invoker.Executed.Count == before + 9, "calls lost or invoked twice");
        Assert(EditorApplication.UpdateCount - updates >= 3, "four calls per frame limit was bypassed");
        for (int i = 0; i < 9; i++)
        {
            Assert(Invoker.Executed[before + i] == "call-" + i, "main-thread calls reordered");
            Assert(connection.Results.TryDequeue(out var result), "missing result frame");
            Assert(result["id"].AsString == "call-" + i, "result frames reordered");
            Assert(result["text"].AsString == "value-" + i, "arguments/result changed in transit");
        }
    }

    static void Reconnect(BridgePeer peer)
    {
        var previous = peer.Current;
        previous.Dispose();
        WaitFor(() => peer.Current != null && peer.Current != previous && AgentMCPBridgeClient.Shared.IsConnected,
            "connection after bridge EOF");
        peer.Current.SendCall("after-eof", "reconnected");
        WaitFor(() => peer.Current.Results.Count == 1, "read-only call after reconnect");
        Assert(peer.Current.Results.TryDequeue(out var result) && result["text"].AsString == "reconnected",
            "reconnected socket did not route result");
    }

    static void Disable()
    {
        AgentSettings.MCPServerEnabled = false;
        WaitForTimerStop();
    }

    static void RapidReconnect(BridgePeer peer)
    {
        var retired = (Thread)typeof(AgentMCPBridgeClient).GetField("_readerThread",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(AgentMCPBridgeClient.Shared);
        var oldConnection = peer.Current;
        AgentLogger.HoldNextEof();
        try
        {
            oldConnection.Dispose();
            Assert(AgentLogger.EofObserved.Wait(1000), "retired reader did not reach EOF");
            AgentMCPBridgeClient.Shared.Disconnect("test-replace-at-eof");
            AgentMCPBridgeClient.Shared.Connect(peer.Port, "test-token");
            WaitFor(() => peer.Current != oldConnection, "replacement at old reader EOF");
        }
        finally { AgentLogger.ReleaseEof.Set(); }
        Assert(retired.Join(1000), "retired reader did not finish");
        Assert(AgentMCPBridgeClient.Shared.IsConnected, "retired reader finally disconnected replacement");

        for (int i = 0; i < 10; i++)
        {
            var previous = peer.Current;
            AgentMCPBridgeClient.Shared.Disconnect("test-replace");
            AgentMCPBridgeClient.Shared.Connect(peer.Port, "test-token");
            WaitFor(() => peer.Current != previous, "replacement handshake");
            // Let the retired reader exit while the new connection is active.
            Thread.Sleep(30);
            Assert(AgentMCPBridgeClient.Shared.IsConnected, "retired reader disconnected replacement");
            peer.Current.SendCall("replacement-" + i, "generation-" + i);
            WaitFor(() => peer.Current.Results.Count == 1, "replacement call");
            Assert(peer.Current.Results.TryDequeue(out var result) && result["text"].AsString == "generation-" + i,
                "replacement reader routed the wrong result");
        }
    }

    static void Reload(BridgePeer peer)
    {
        AgentSettings.MCPServerEnabled = true;
        AgentMCPServerBootstrap.StartIfEnabled();
        WaitFor(() => AgentMCPBridgeClient.Shared.IsConnected, "connection before reload");
        int signals = EditorApplication.SignalCount;
        WaitFor(() => EditorApplication.SignalCount > signals, "restarted timer");
        var connection = peer.Current;
        AssemblyReloadEvents.Reload();
        Assert(!AgentMCPBridgeClient.Shared.IsConnected, "reload kept Unity TCP connection alive");
        WaitFor(() => connection.Shutdowns.Count > 0, "domain reload shutdown frame");
        Assert(connection.Shutdowns.TryDequeue(out var shutdown) && shutdown["reason"].AsString == "domain_reload",
            "reload announced an editor exit");
        WaitForTimerStop();
    }

    static void Quit(BridgePeer peer)
    {
        var previous = peer.Current;
        AgentSettings.MCPServerEnabled = true;
        AgentMCPServerBootstrap.StartIfEnabled();
        WaitFor(() => AgentMCPBridgeClient.Shared.IsConnected && peer.Current != previous, "connection before quit");
        var connection = peer.Current;
        EditorApplication.Quit();
        Assert(!AgentMCPBridgeClient.Shared.IsConnected, "quit kept Unity TCP connection alive");
        WaitFor(() => connection.Shutdowns.Count > 0, "editor quit shutdown frame");
        Assert(connection.Shutdowns.TryDequeue(out var shutdown) && shutdown["reason"].AsString == "editor_quit",
            "quit was misidentified as a domain reload");
        WaitForTimerStop();
    }

    static void WaitForTimerStop()
    {
        // Allow the supervisor to observe disabling and any already requested
        // tick to finish, then verify that no later timer tick wakes the editor.
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 500) EditorApplication.RunRequestedUpdate(25);
        int signals = EditorApplication.SignalCount;
        Thread.Sleep(500);
        Assert(EditorApplication.SignalCount == signals, "background wakeup kept firing after stop");
    }

    static void WaitFor(Func<bool> condition, string description)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > 5000)
                throw new Exception("timed out waiting for " + description);
            EditorApplication.RunRequestedUpdate(25);
        }
    }

    static void Run(string name, Action test, ref int failed)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

internal sealed class BridgePeer : IDisposable
{
    readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    readonly Thread _acceptThread;
    readonly ConcurrentQueue<BridgeConnection> _connections = new ConcurrentQueue<BridgeConnection>();
    volatile bool _running = true;
    BridgeConnection _current;
    public int Port { get; }
    public BridgeConnection Current => Volatile.Read(ref _current);

    public BridgePeer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
    }

    void AcceptLoop()
    {
        while (_running)
        {
            TcpClient socket;
            try { socket = _listener.AcceptTcpClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var connection = new BridgeConnection(socket);
                    if (!connection.Authenticate()) { connection.Dispose(); return; } // port probe
                    _connections.Enqueue(connection);
                    Volatile.Write(ref _current, connection);
                    connection.ReadResults();
                }
                catch (IOException) { socket.Dispose(); }
                catch (ObjectDisposedException) { socket.Dispose(); }
            });
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener.Stop();
        while (_connections.TryDequeue(out var connection)) connection.Dispose();
        _acceptThread.Join(1000);
    }
}

internal sealed class BridgeConnection : IDisposable
{
    readonly TcpClient _socket;
    readonly StreamReader _reader;
    readonly StreamWriter _writer;
    public readonly ConcurrentQueue<JNode> Results = new ConcurrentQueue<JNode>();
    public readonly ConcurrentQueue<JNode> Shutdowns = new ConcurrentQueue<JNode>();
    public int UnityPid { get; private set; }

    public BridgeConnection(TcpClient socket)
    {
        _socket = socket;
        _reader = new StreamReader(socket.GetStream(), new UTF8Encoding(false));
        _writer = new StreamWriter(socket.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }
    public bool Authenticate()
    {
        string line = _reader.ReadLine();
        if (line == null) return false;
        var hello = JNode.Parse(line);
        if (hello["type"].AsString != "hello" || hello["token"].AsString != "test-token")
            throw new IOException("invalid test handshake");
        UnityPid = hello["unityPid"].AsInt;
        _writer.WriteLine("{\"type\":\"hello_ack\",\"ok\":true}");
        return true;
    }
    public void SendCall(string id, string value) => _writer.WriteLine(JNode.Obj(
        ("type", JNode.Str("call")), ("id", JNode.Str(id)), ("tool", JNode.Str("ReadOnlyTestTool")),
        ("args", JNode.Obj(("value", JNode.Str(value))))).ToJson());
    public void ReadResults()
    {
        string line;
        while ((line = _reader.ReadLine()) != null)
        {
            var result = JNode.Parse(line);
            if (result["type"].AsString == "result" || result["type"].AsString == "error") Results.Enqueue(result);
            else if (result["type"].AsString == "shutdown") Shutdowns.Enqueue(result);
        }
    }
    public void Dispose() => _socket.Dispose();
}
