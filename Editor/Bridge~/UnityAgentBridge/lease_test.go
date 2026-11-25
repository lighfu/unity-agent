package main

import (
	"bufio"
	"context"
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

type leaseTestReply struct {
	Result struct {
		StructuredContent unityLeaseStatus
	}
	Error *struct {
		Code int
		Data toolFailure
	}
}

func leaseTestRequest(ctx context.Context, bridge *Bridge, token, params string) *httptest.ResponseRecorder {
	body := strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"tools/call","params":` + params + `}`)
	req := httptest.NewRequest(http.MethodPost, "/mcp", body).WithContext(ctx)
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	if token != "" {
		req.Header.Set(unityLeaseHeader, token)
	}
	rec := httptest.NewRecorder()
	bridge.handleMCP(rec, req)
	return rec
}

func leaseTestDecode(t *testing.T, rec *httptest.ResponseRecorder) leaseTestReply {
	t.Helper()
	var reply leaseTestReply
	if rec.Code != http.StatusOK || json.Unmarshal(rec.Body.Bytes(), &reply) != nil {
		t.Fatalf("invalid MCP lease response: %d %s", rec.Code, rec.Body.String())
	}
	return reply
}

const testLeaseAcquireParams = `{"name":"AcquireUnityLease","arguments":{"owner":"Verification client","purpose":"Sync, compile, measure and restore","ttlSeconds":60}}`
const testLeaseStatusParams = `{"name":"GetUnityLeaseStatus","arguments":{}}`

func leaseTestAcquire(t *testing.T, bridge *Bridge) unityLeaseStatus {
	t.Helper()
	rec := leaseTestRequest(context.Background(), bridge, "", testLeaseAcquireParams)
	reply := leaseTestDecode(t, rec)
	status := reply.Result.StructuredContent
	if reply.Error != nil || status.State != "held" || status.Owner != "Verification client" || status.Purpose == "" || len(status.LeaseToken) < 32 {
		t.Fatal("acquisition did not return owner, purpose and a credential")
	}
	if rec.Header().Get("Cache-Control") != "no-store" {
		t.Fatal("the response that returns the credential must not be cached")
	}
	return status
}

func leaseTestStatus(t *testing.T, bridge *Bridge, token string) unityLeaseStatus {
	t.Helper()
	rec := leaseTestRequest(context.Background(), bridge, "", testLeaseStatusParams)
	reply := leaseTestDecode(t, rec)
	if reply.Error != nil || reply.Result.StructuredContent.LeaseToken != "" || (token != "" && strings.Contains(rec.Body.String(), token)) {
		t.Fatal("status failed or exposed the other client's credential")
	}
	return reply.Result.StructuredContent
}

func checkLeaseDenial(t *testing.T, rec *httptest.ResponseRecorder, state, token string) {
	t.Helper()
	reply := leaseTestDecode(t, rec)
	if reply.Error == nil || reply.Error.Code != errCodeLeaseConflict || reply.Error.Data.Reservation == nil ||
		reply.Error.Data.Reservation.State != state || reply.Error.Data.Reservation.LeaseToken != "" ||
		(token != "" && strings.Contains(rec.Body.String(), token)) {
		t.Fatalf("expected a private lease conflict in state %s", state)
	}
}

func sendLeaseTestCall(ctx context.Context, bridge *Bridge, token, params string) <-chan *httptest.ResponseRecorder {
	done := make(chan *httptest.ResponseRecorder, 1)
	go func() { done <- leaseTestRequest(ctx, bridge, token, params) }()
	return done
}

func waitLeaseTestResponse(t *testing.T, done <-chan *httptest.ResponseRecorder) *httptest.ResponseRecorder {
	t.Helper()
	select {
	case rec := <-done:
		return rec
	case <-time.After(3 * time.Second):
		t.Fatal("Bridge did not respond to the test caller")
		return nil
	}
}

func readLeaseTestCall(t *testing.T, conn net.Conn, reader *bufio.Reader, tool string) wireMsg {
	t.Helper()
	_ = conn.SetReadDeadline(time.Now().Add(2 * time.Second))
	defer conn.SetReadDeadline(time.Time{})
	line, err := reader.ReadBytes('\n')
	var msg wireMsg
	if err != nil || json.Unmarshal(line, &msg) != nil || msg.Type != "call" || msg.Tool != tool {
		t.Fatalf("expected %s on the fake Unity connection, got %q (%v)", tool, line, err)
	}
	return msg
}

func TestUnityLeaseSurvivesSameEditorReload(t *testing.T) {
	bridge := newBridge("secret")
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	unity, reader := startFakeUnityWithHello(t, bridge, wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: 101})
	waitForUnityOwner(t, bridge, 101, true)

	list := postStatusTestRPC(bridge, `{"jsonrpc":"2.0","id":1,"method":"tools/list"}`)
	for _, tool := range []string{acquireUnityLeaseTool, getUnityLeaseStatusTool, renewUnityLeaseTool, releaseUnityLeaseTool} {
		if !strings.Contains(list.Body.String(), `"name":"`+tool+`"`) {
			t.Fatalf("missing discoverable tool %s", tool)
		}
	}
	if !strings.Contains(list.Header().Get("Access-Control-Allow-Headers"), unityLeaseHeader) {
		t.Fatal("CORS does not allow the lease credential header")
	}
	lease := leaseTestAcquire(t, bridge)
	token := lease.LeaseToken
	checkLeaseDenial(t, leaseTestRequest(ctx, bridge, "", testLeaseAcquireParams), "held", token)
	checkLeaseDenial(t, leaseTestRequest(ctx, bridge, "", `{"name":"CreateGameObject","arguments":{"name":"Competing call"}}`), "held", token)
	if status := leaseTestStatus(t, bridge, token); status.Owner != lease.Owner || status.Purpose != lease.Purpose || status.ExpiresAt != lease.ExpiresAt {
		t.Fatalf("unexpected public reservation status %+v", status)
	}

	compile := sendLeaseTestCall(ctx, bridge, token, `{"name":"RefreshAssetDatabase","arguments":{}}`)
	readLeaseTestCall(t, unity, reader, "RefreshAssetDatabase")
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "shutdown", Reason: "domain_reload"}); err != nil {
		t.Fatal(err)
	}
	_ = unity.Close()
	if reply := leaseTestDecode(t, waitLeaseTestResponse(t, compile)); reply.Error == nil || reply.Error.Code != errCodeInterrupted {
		t.Fatal("already-dispatched compilation did not report its interruption")
	}
	waitForMCPReadiness(t, bridge, "reconnecting")
	if status := leaseTestStatus(t, bridge, token); status.State != "held" || status.InFlightCalls != 0 || status.ExpiresAt != lease.ExpiresAt {
		t.Fatalf("same editor's reservation changed during reload: %+v", status)
	}

	probe := sendLeaseTestCall(ctx, bridge, "", `{"name":"GetEditorState","arguments":{},"_meta":{"unityagent/leaseToken":"`+token+`"}}`)
	waitForLeaseStatus(t, bridge, token, func(s unityLeaseStatus) bool { return s.QueuedCalls == 1 })
	renew := leaseTestDecode(t, leaseTestRequest(ctx, bridge, "", `{"name":"RenewUnityLease","arguments":{"leaseToken":"`+token+`","ttlSeconds":90}}`))
	if renew.Error != nil || renew.Result.StructuredContent.AcquiredAt != lease.AcquiredAt ||
		renew.Result.StructuredContent.ExpiresAt <= lease.ExpiresAt || renew.Result.StructuredContent.LeaseToken != "" {
		t.Fatal("renewal did not preserve the owner while advancing expiry")
	}

	unity, reader = startFakeUnityWithHello(t, bridge, wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: 101})
	msg := readLeaseTestCall(t, unity, reader, "GetEditorState")
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "result", ID: msg.ID, Text: "ready"}); err != nil {
		t.Fatal(err)
	}
	if reply := leaseTestDecode(t, waitLeaseTestResponse(t, probe)); reply.Error != nil {
		t.Fatal("reservation owner could not read Unity after reload")
	}

	wrapped := sendLeaseTestCall(ctx, bridge, "", `{"name":"ExecuteUnityTool","arguments":{"name":"GetEditorState","arguments":{},"leaseToken":"`+token+`"}}`)
	msg = readLeaseTestCall(t, unity, reader, "ExecuteUnityTool")
	if strings.Contains(string(msg.Args), token) || strings.Contains(string(msg.Args), "leaseToken") {
		t.Fatal("credential leaked to the Editor's execution envelope")
	}
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "result", ID: msg.ID, Text: "ready"}); err != nil {
		t.Fatal(err)
	}
	if reply := leaseTestDecode(t, waitLeaseTestResponse(t, wrapped)); reply.Error != nil {
		t.Fatal("wrapped call with an envelope credential failed")
	}

	release := leaseTestDecode(t, leaseTestRequest(ctx, bridge, token, `{"name":"ReleaseUnityLease","arguments":{}}`))
	if release.Error != nil || release.Result.StructuredContent.State != "free" {
		t.Fatal("reservation did not release after its calls completed")
	}
	checkLeaseDenial(t, leaseTestRequest(ctx, bridge, token, `{"name":"GetEditorState","arguments":{}}`), "free", token)
}

func waitForLeaseStatus(t *testing.T, bridge *Bridge, token string, condition func(unityLeaseStatus) bool) unityLeaseStatus {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for {
		status := leaseTestStatus(t, bridge, token)
		if condition(status) {
			return status
		}
		if time.Now().After(deadline) {
			t.Fatalf("reservation never reached the expected status: %+v", status)
		}
		time.Sleep(time.Millisecond)
	}
}

func TestUnityLeaseCannotOvertakeCanceledDispatchedCall(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	call := sendLeaseTestCall(ctx, bridge, "", `{"name":"CreateGameObject","arguments":{"name":"Earlier work"}}`)
	msg := readLeaseTestCall(t, unity, reader, "CreateGameObject")
	cancel()
	waitLeaseTestResponse(t, call)

	checkLeaseDenial(t, leaseTestRequest(context.Background(), bridge, "", testLeaseAcquireParams), "free", "")
	if status := leaseTestStatus(t, bridge, ""); status.InFlightCalls != 1 {
		t.Fatalf("canceled HTTP request dropped its Unity fence: %+v", status)
	}
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "result", ID: msg.ID, Text: "ok"}); err != nil {
		t.Fatal(err)
	}
	waitForLeaseStatus(t, bridge, "", func(s unityLeaseStatus) bool { return s.InFlightCalls == 0 })
	leaseTestAcquire(t, bridge)
}

func TestUnityLeaseReleaseDrainsAlreadyDispatchedCalls(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	token := leaseTestAcquire(t, bridge).LeaseToken
	call := sendLeaseTestCall(ctx, bridge, token, `{"name":"CreateGameObject","arguments":{"name":"Ongoing work"}}`)
	msg := readLeaseTestCall(t, unity, reader, "CreateGameObject")

	release := leaseTestDecode(t, leaseTestRequest(ctx, bridge, token, `{"name":"ReleaseUnityLease","arguments":{}}`))
	if release.Error != nil || release.Result.StructuredContent.State != "draining" {
		t.Fatal("released reservation was dropped before the in-flight work finished")
	}
	checkLeaseDenial(t, leaseTestRequest(ctx, bridge, "", testLeaseAcquireParams), "draining", token)
	checkLeaseDenial(t, leaseTestRequest(ctx, bridge, token, `{"name":"GetEditorState","arguments":{}}`), "draining", token)

	cancel()
	waitLeaseTestResponse(t, call)
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "result", ID: msg.ID, Text: "ok"}); err != nil {
		t.Fatal(err)
	}
	waitForLeaseStatus(t, bridge, token, func(s unityLeaseStatus) bool { return s.State == "free" })
}

func TestUnityLeaseDropsQueuedCallsOnExpiryOrEditorReplacement(t *testing.T) {
	for _, event := range []string{"expires", "different editor"} {
		t.Run(event, func(t *testing.T) {
			bridge := newBridge("secret")
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			if event == "different editor" {
				unity, _ := startFakeUnityWithHello(t, bridge, wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: 101})
				waitForUnityOwner(t, bridge, 101, true)
				if err := bridge.writeUnityMsg(unity, wireMsg{Type: "shutdown", Reason: "domain_reload"}); err != nil {
					t.Fatal(err)
				}
				_ = unity.Close()
				waitForMCPReadiness(t, bridge, "reconnecting")
			}
			token := leaseTestAcquire(t, bridge).LeaseToken
			call := sendLeaseTestCall(ctx, bridge, token, `{"name":"CreateGameObject","arguments":{"name":"Stale queued edit"}}`)
			waitForLeaseStatus(t, bridge, token, func(s unityLeaseStatus) bool { return s.QueuedCalls == 1 })
			if event == "expires" {
				bridge.mu.Lock()
				bridge.lease.expiresAt = time.Now().Add(-time.Second)
				bridge.scheduleLeaseExpiryLocked(bridge.lease)
				bridge.mu.Unlock()
			} else {
				startFakeUnityWithHello(t, bridge, wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: 202})
			}
			checkLeaseDenial(t, waitLeaseTestResponse(t, call), "free", token)
			if status := leaseTestStatus(t, bridge, token); status.InFlightCalls != 0 || status.QueuedCalls != 0 {
				t.Fatalf("queued edit remained eligible for forwarding: %+v", status)
			}
			leaseTestAcquire(t, bridge)
		})
	}
}

func TestUnityLeaseKeepsDisconnectedBridgeAvailableUntilExpiry(t *testing.T) {
	bridge := newBridge("secret")
	leaseTestAcquire(t, bridge)
	bridge.mu.Lock()
	bridge.lastActivity = time.Now().Add(-time.Hour)
	expiration := bridge.lease.expiresAt
	bridge.mu.Unlock()
	isAlive := func(int) bool { return false }
	if bridge.shouldQuitWhenIdle(time.Now(), time.Minute, isAlive) {
		t.Fatal("idle quit would discard an active disconnected reservation")
	}
	if !bridge.shouldQuitWhenIdle(expiration.Add(time.Second), time.Minute, isAlive) {
		t.Fatal("a disconnected expired reservation continues to block idle quit")
	}
}
