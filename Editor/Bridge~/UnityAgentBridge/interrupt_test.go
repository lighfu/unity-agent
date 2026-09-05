package main

import (
	"bufio"
	"encoding/json"
	"net"
	"strings"
	"testing"
	"time"
)

// startFakeUnity wires a Bridge to a loopback TCP connection that stands in for the Unity
// editor, completes the hello handshake, and returns the Unity-side end. Loopback TCP rather
// than net.Pipe so writes are buffered and the test never deadlocks on an unread line.
func startFakeUnity(t *testing.T, bridge *Bridge) (net.Conn, *bufio.Reader) {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() { _ = ln.Close() })

	go func() {
		conn, err := ln.Accept()
		if err != nil {
			return
		}
		bridge.swapUnityConn(conn)
		bridge.handleUnityConn(conn)
	}()

	unity, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	t.Cleanup(func() { _ = unity.Close() })

	if _, err := unity.Write([]byte(`{"type":"hello","version":"test","token":"secret"}` + "\n")); err != nil {
		t.Fatalf("write hello: %v", err)
	}
	reader := bufio.NewReader(unity)
	line, err := reader.ReadString('\n')
	if err != nil {
		t.Fatalf("read hello_ack: %v", err)
	}
	var ack wireMsg
	if err := json.Unmarshal([]byte(line), &ack); err != nil || ack.Type != "hello_ack" || !ack.OK {
		t.Fatalf("unexpected hello_ack: %q (err=%v)", line, err)
	}
	return unity, reader
}

// dispatchTestCall registers a call as the HTTP handler would and sends it to Unity, then
// consumes the "call" line on the Unity side so the wire is in a known state.
func dispatchTestCall(t *testing.T, bridge *Bridge, reader *bufio.Reader, tool string) *pendingCall {
	t.Helper()
	call := &pendingCall{
		ID:       "call-" + tool,
		Tool:     tool,
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	bridge.mu.Lock()
	bridge.pending[call.ID] = call
	bridge.mu.Unlock()
	if err := bridge.dispatchToUnity(call); err != nil {
		t.Fatalf("dispatch: %v", err)
	}
	line, err := reader.ReadString('\n')
	if err != nil {
		t.Fatalf("read call line: %v", err)
	}
	var msg wireMsg
	if err := json.Unmarshal([]byte(line), &msg); err != nil || msg.Type != "call" || msg.ID != call.ID {
		t.Fatalf("expected call %s on the wire, got %q", call.ID, line)
	}
	return call
}

func awaitResult(t *testing.T, call *pendingCall) callResult {
	t.Helper()
	select {
	case res := <-call.Response:
		return res
	case <-time.After(2 * time.Second):
		t.Fatalf("call %s was not answered within 2 s — it would have waited the full callTimeout", call.ID)
		return callResult{}
	}
}

func assertNotPending(t *testing.T, bridge *Bridge, id string) {
	t.Helper()
	bridge.mu.Lock()
	_, still := bridge.pending[id]
	bridge.mu.Unlock()
	if still {
		t.Fatalf("call %s is still in pending after being failed", id)
	}
}

func TestDispatchedCallFailsAtOnceOnDomainReload(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	call := dispatchTestCall(t, bridge, reader, "RefreshAssetDatabase")

	// What AgentMCPBridgeClient sends from beforeAssemblyReload, then the socket goes away.
	if _, err := unity.Write([]byte(`{"type":"shutdown","reason":"domain_reload"}` + "\n")); err != nil {
		t.Fatalf("write shutdown: %v", err)
	}
	_ = unity.Close()

	res := awaitResult(t, call)
	if res.OK {
		t.Fatalf("expected an error result, got OK")
	}
	if res.ErrCode != errCodeInterrupted {
		t.Fatalf("expected code %d, got %d", errCodeInterrupted, res.ErrCode)
	}
	if !strings.Contains(res.Error, "reloaded the app domain") {
		t.Fatalf("expected a domain-reload message, got %q", res.Error)
	}
	for _, want := range []string{"RefreshAssetDatabase", "NOT re-sent", "CompareAssemblyBaseline"} {
		if !strings.Contains(res.Data, want) {
			t.Fatalf("error data should mention %q, got %q", want, res.Data)
		}
	}
	assertNotPending(t, bridge, call.ID)
}

func TestDispatchedCallFailsAtOnceOnConnectionLoss(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	call := dispatchTestCall(t, bridge, reader, "GetEditorState")

	// No shutdown notice — the socket just dies, as it would on a crash or a kill.
	_ = unity.Close()

	res := awaitResult(t, call)
	if res.OK || res.ErrCode != errCodeInterrupted {
		t.Fatalf("expected code %d error, got ok=%v code=%d", errCodeInterrupted, res.OK, res.ErrCode)
	}
	if !strings.Contains(res.Error, "connection lost") {
		t.Fatalf("expected a connection-lost message, got %q", res.Error)
	}
	if strings.Contains(res.Error, "reloaded") {
		t.Fatalf("an unannounced disconnect must not be reported as a reload: %q", res.Error)
	}
	assertNotPending(t, bridge, call.ID)
}

func TestQueuedCallSurvivesUnityDisconnect(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	dispatched := dispatchTestCall(t, bridge, reader, "ListRootObjects")

	// A call that was never written to the wire has nothing to be interrupted by. It must
	// stay pending so the reconnect flush can still deliver it.
	queued := &pendingCall{
		ID:       "call-queued",
		Tool:     "GetConsoleLogs",
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	bridge.mu.Lock()
	bridge.pending[queued.ID] = queued
	bridge.mu.Unlock()

	_ = unity.Close()

	// The dispatched one fails...
	awaitResult(t, dispatched)

	// ...and by then the cleanup has run, so the queued one's fate is settled too.
	select {
	case res := <-queued.Response:
		t.Fatalf("queued call must not be failed by a disconnect, got %+v", res)
	case <-time.After(200 * time.Millisecond):
	}
	bridge.mu.Lock()
	_, still := bridge.pending[queued.ID]
	bridge.mu.Unlock()
	if !still {
		t.Fatalf("queued call was removed from pending by the disconnect cleanup")
	}
}
