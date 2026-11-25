package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestBridgeReadinessThroughEditorReload(t *testing.T) {
	bridge := newBridge("secret")
	list := postStatusTestRPC(bridge, `{"jsonrpc":"2.0","id":1,"method":"tools/list"}`)
	if !strings.Contains(list.Body.String(), `"name":"GetUnityConnectionStatus"`) {
		t.Fatalf("readiness tool is missing from tools/list: %s", list.Body.String())
	}
	initial := getStatusThroughMCP(t, bridge)
	if !initial.BridgeAlive || initial.UnityConnected || initial.Readiness != "waiting_for_connection" {
		t.Fatalf("unexpected initial status: %+v", initial)
	}

	unity, _ := startFakeUnity(t, bridge)
	// The editor can track a compilation event while its main thread has stopped ticking.
	// A fresh report can still describe that event despite an old main-thread sample.
	age := 12000
	if err := bridge.writeUnityMsg(unity, wireMsg{
		Type: "unity_status", Status: "compiling", MainThreadAgeMS: &age, PlayMode: "edit", Reason: "Unity is compiling scripts.",
	}); err != nil {
		t.Fatal(err)
	}
	compiling := waitForMCPReadiness(t, bridge, "compiling")
	if !compiling.UnityConnected || compiling.PlayMode != "edit" || compiling.MainThreadAgeMS == nil || *compiling.MainThreadAgeMS != age || compiling.Reason == "" {
		t.Fatalf("incomplete compiling status: %+v", compiling)
	}

	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "unity_status", Status: "dialog_wait"}); err != nil {
		t.Fatal(err)
	}
	dialog := waitForMCPReadiness(t, bridge, "dialog_wait")
	for _, hint := range []string{"GetEditorState", "AnswerModalDialog", "dryRun:true"} {
		if !strings.Contains(dialog.NextAction, hint) {
			t.Fatalf("readiness tool should tell callers how to inspect the dialog, missing %q in %q", hint, dialog.NextAction)
		}
	}

	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "shutdown", Reason: "domain_reload"}); err != nil {
		t.Fatal(err)
	}
	_ = unity.Close()
	reloading := waitForMCPReadiness(t, bridge, "reconnecting")
	if !reloading.BridgeAlive || reloading.UnityConnected || reloading.DisconnectReason != "domain_reload" || reloading.StatusAgeMS != nil {
		t.Fatalf("unexpected status while reloading: %+v", reloading)
	}
	bridge.mu.Lock()
	pendingCalls, queuedCalls := len(bridge.pending), len(bridge.queueWhenDown)
	bridge.mu.Unlock()
	if pendingCalls != 0 || queuedCalls != 0 {
		t.Fatalf("status probe reached Unity's queue: pending=%d queued=%d", pendingCalls, queuedCalls)
	}

	unity, _ = startFakeUnity(t, bridge)
	unknown := waitForMCPReadiness(t, bridge, "unknown")
	if !unknown.UnityConnected || unknown.DisconnectReason != "" || unknown.StatusAgeMS != nil {
		t.Fatalf("received an old observation after reconnect: %+v", unknown)
	}
	if err := bridge.writeUnityMsg(unity, wireMsg{Type: "unity_status", Status: "ready"}); err != nil {
		t.Fatal(err)
	}
	ready := waitForMCPReadiness(t, bridge, "ready")
	if !ready.UnityConnected || ready.StatusAgeMS == nil {
		t.Fatalf("incomplete ready status: %+v", ready)
	}

	bridge.mu.Lock()
	bridge.unityStatusAt = time.Now().Add(-unityStatusMaxAge)
	bridge.mu.Unlock()
	stale := getStatusThroughMCP(t, bridge)
	if !stale.UnityConnected || stale.Readiness != "unknown" || stale.StatusAgeMS == nil || *stale.StatusAgeMS < unityStatusMaxAge.Milliseconds() {
		t.Fatalf("stale status should not report ready: %+v", stale)
	}
}

func postStatusTestRPC(bridge *Bridge, payload string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(payload))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()
	bridge.handleMCP(rec, req)
	return rec
}

func getStatusThroughMCP(t *testing.T, bridge *Bridge) unityConnectionStatus {
	t.Helper()
	rec := postStatusTestRPC(bridge, `{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"GetUnityConnectionStatus","arguments":{}}}`)
	var reply struct {
		Result struct {
			Content []struct{ Text string }
		}
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &reply); err != nil || len(reply.Result.Content) != 1 {
		t.Fatalf("invalid readiness result: %s (%v)", rec.Body.String(), err)
	}
	var status unityConnectionStatus
	if err := json.Unmarshal([]byte(reply.Result.Content[0].Text), &status); err != nil {
		t.Fatalf("invalid readiness payload: %s (%v)", rec.Body.String(), err)
	}
	return status
}

func waitForMCPReadiness(t *testing.T, bridge *Bridge, readiness string) unityConnectionStatus {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for {
		status := getStatusThroughMCP(t, bridge)
		if status.Readiness == readiness {
			return status
		}
		if time.Now().After(deadline) {
			t.Fatalf("expected readiness %q, got %+v", readiness, status)
		}
		time.Sleep(time.Millisecond)
	}
}
