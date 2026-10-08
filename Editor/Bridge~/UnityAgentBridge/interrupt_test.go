package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"net"
	"os"
	"strings"
	"testing"
	"time"
)

// startFakeUnity wires a Bridge to a loopback TCP connection that stands in for the Unity
// editor, completes the hello handshake, and returns the Unity-side end. Loopback TCP rather
// than net.Pipe so writes are buffered and the test never deadlocks on an unread line.
func startFakeUnity(t *testing.T, bridge *Bridge) (net.Conn, *bufio.Reader) {
	return startFakeUnityWithToken(t, bridge, "secret")
}

func startFakeUnityWithToken(t *testing.T, bridge *Bridge, token string) (net.Conn, *bufio.Reader) {
	return startFakeUnityWithHello(t, bridge, wireMsg{Type: "hello", Version: "test", Token: token})
}

func startFakeUnityWithHello(t *testing.T, bridge *Bridge, hello wireMsg) (net.Conn, *bufio.Reader) {
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
		bridge.handleUnityConn(conn)
	}()

	unity, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	t.Cleanup(func() { _ = unity.Close() })

	helloJSON, err := json.Marshal(hello)
	if err != nil {
		t.Fatalf("marshal hello: %v", err)
	}
	if _, err := unity.Write(append(helloJSON, '\n')); err != nil {
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

func TestUnauthenticatedConnectionCannotReplaceAuthenticatedUnity(t *testing.T) {
	bridge := newBridge("secret")
	healthy, reader := startFakeUnityWithHello(t, bridge,
		wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: os.Getpid()})
	waitForUnityOwner(t, bridge, os.Getpid(), true)

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen second connection: %v", err)
	}
	t.Cleanup(func() { _ = ln.Close() })
	go func() {
		conn, err := ln.Accept()
		if err == nil {
			bridge.handleUnityConn(conn)
		}
	}()
	attacker, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial second connection: %v", err)
	}
	t.Cleanup(func() { _ = attacker.Close() })
	if _, err := attacker.Write([]byte(`{"type":"hello","version":"test","token":"wrong","unityPid":123}` + "\n")); err != nil {
		t.Fatalf("write bad hello: %v", err)
	}
	ack, err := bufio.NewReader(attacker).ReadString('\n')
	if err != nil || !strings.Contains(ack, `"error":"bad token"`) {
		t.Fatalf("expected rejected hello, got %q (err=%v)", ack, err)
	}
	bridge.mu.Lock()
	ownerPID := bridge.ownerUnityPID
	bridge.mu.Unlock()
	if ownerPID != os.Getpid() {
		t.Fatalf("unauthenticated hello changed Unity owner to %d", ownerPID)
	}

	call := &pendingCall{
		ID:       "call-after-bad-hello",
		Tool:     "GetEditorState",
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	bridge.mu.Lock()
	bridge.pending[call.ID] = call
	bridge.mu.Unlock()
	if err := bridge.dispatchToUnity(call); err != nil {
		t.Fatalf("authenticated connection was replaced by bad hello: %v", err)
	}
	line, err := reader.ReadString('\n')
	if err != nil || !strings.Contains(line, call.ID) {
		t.Fatalf("expected call on healthy connection, got %q (err=%v)", line, err)
	}
	bridge.cancelPendingCall(call)
	_ = healthy.Close()
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

func TestDispatchedCallNamesEditorQuitInsteadOfCrash(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	call := dispatchTestCall(t, bridge, reader, "SaveScene")

	if _, err := unity.Write([]byte(`{"type":"shutdown","reason":"editor_quit"}` + "\n")); err != nil {
		t.Fatalf("write shutdown: %v", err)
	}
	_ = unity.Close()

	res := awaitResult(t, call)
	if res.OK || res.ErrCode != errCodeInterrupted {
		t.Fatalf("expected code %d error, got ok=%v code=%d", errCodeInterrupted, res.OK, res.ErrCode)
	}
	if res.Error != "Unity editor quit while this call was running" {
		t.Fatalf("expected an editor-quit message, got %q", res.Error)
	}
	// The announced quit must not be described as an unannounced crash.
	for _, unwanted := range []string{"without a shutdown notice", "crashed"} {
		if strings.Contains(res.Data, unwanted) {
			t.Fatalf("error data contradicts the announced quit with %q: %q", unwanted, res.Data)
		}
	}
	for _, want := range []string{"SaveScene", "NOT re-sent"} {
		if !strings.Contains(res.Data, want) {
			t.Fatalf("error data should mention %q, got %q", want, res.Data)
		}
	}
	assertNotPending(t, bridge, call.ID)
}

func TestOwnerExitAnswersQueuedAndDispatchedCallsAndRefusesNewOnes(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	t.Cleanup(func() { _ = unity.Close() })
	dispatched := dispatchTestCall(t, bridge, reader, "BuildPlayer")
	queued := &pendingCall{
		ID:       "call-queued",
		Tool:     "GetConsoleLogs",
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	bridge.mu.Lock()
	bridge.pending[queued.ID] = queued
	bridge.queueWhenDown = append(bridge.queueWhenDown, queued)
	bridge.mu.Unlock()

	if failed := bridge.failCallsForOwnerExit(); failed != 2 {
		t.Fatalf("answered %d calls, want 2", failed)
	}
	for _, test := range []struct {
		call      *pendingCall
		wantError string
	}{
		{dispatched, "Unity exited while this call was running"},
		{queued, "Unity exited before this call could run"},
	} {
		res := awaitResult(t, test.call)
		if res.OK || res.ErrCode != errCodeInterrupted || res.Error != test.wantError {
			t.Fatalf("%s: got ok=%v code=%d error=%q, want %q", test.call.Tool, res.OK, res.ErrCode, res.Error, test.wantError)
		}
		if !strings.Contains(res.Data, test.call.Tool) {
			t.Fatalf("%s: error data should name the tool, got %q", test.call.Tool, res.Data)
		}
		assertNotPending(t, bridge, test.call.ID)
	}
	bridge.mu.Lock()
	queueLength := len(bridge.queueWhenDown)
	bridge.mu.Unlock()
	if queueLength != 0 {
		t.Fatalf("%d call(s) left in the reconnect queue", queueLength)
	}

	// A call that slips in while the bridge drains must be refused, not queued for an
	// editor that will never reconnect.
	late := &pendingCall{
		ID:       "call-late",
		Tool:     "GetEditorState",
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	bridge.mu.Lock()
	bridge.pending[late.ID] = late
	bridge.mu.Unlock()
	if err := bridge.dispatchToUnity(late); !errors.Is(err, errOwnerExited) {
		t.Fatalf("dispatch after owner exit returned %v, want errOwnerExited", err)
	}
	assertNotPending(t, bridge, late.ID)
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

func TestSilentConnectionIsDroppedAfterHelloTimeout(t *testing.T) {
	bridge := newBridge("secret")
	bridge.helloTimeout = 50 * time.Millisecond

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() { _ = ln.Close() })

	handlerReturned := make(chan struct{})
	go func() {
		conn, err := ln.Accept()
		if err != nil {
			return
		}
		bridge.handleUnityConn(conn)
		close(handlerReturned)
	}()

	silent, err := net.Dial("tcp", ln.Addr().String())
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	t.Cleanup(func() { _ = silent.Close() })

	select {
	case <-handlerReturned:
	case <-time.After(2 * time.Second):
		t.Fatal("a connection that never sent hello was left open — its goroutine and fd would leak for the life of the bridge")
	}

	// Stopping the read is not enough; the socket itself has to go.
	_ = silent.SetReadDeadline(time.Now().Add(time.Second))
	if _, err := silent.Read(make([]byte, 1)); err == nil {
		t.Fatal("expected the bridge to close the unauthenticated connection")
	}
}

func TestAuthenticatedConnectionOutlivesTheHelloDeadline(t *testing.T) {
	bridge := newBridge("secret")
	bridge.helloTimeout = 50 * time.Millisecond
	unity, reader := startFakeUnity(t, bridge)

	// Well past the hello deadline. If it were not cleared once the handshake succeeded,
	// an editor sitting idle between calls would be dropped mid-session.
	time.Sleep(150 * time.Millisecond)

	call := dispatchTestCall(t, bridge, reader, "GetEditorState")
	bridge.cancelPendingCall(call)
	_ = unity.Close()
}
