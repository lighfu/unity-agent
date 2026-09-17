package main

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestNegotiateProtocolVersion(t *testing.T) {
	if got := negotiateProtocolVersion("2025-06-18"); got != latestProtocolVersion {
		t.Fatalf("expected latest version, got %q", got)
	}
	if got := negotiateProtocolVersion("2099-01-01"); got != latestProtocolVersion {
		t.Fatalf("expected fallback to latest, got %q", got)
	}
}

func TestAcceptsJSONAndEventStream(t *testing.T) {
	if !acceptsJSONAndEventStream("application/json, text/event-stream") {
		t.Fatal("expected combined Accept header to be valid")
	}
	if !acceptsJSONAndEventStream("application/json; charset=utf-8, text/event-stream") {
		t.Fatal("expected parameters to be ignored")
	}
	if acceptsJSONAndEventStream("application/json") {
		t.Fatal("expected missing text/event-stream to be invalid")
	}
	if acceptsJSONAndEventStream("text/event-stream") {
		t.Fatal("expected missing application/json to be invalid")
	}
	if acceptsJSONAndEventStream("*/*") {
		t.Fatal("expected wildcard Accept to be invalid for Streamable HTTP")
	}
	if acceptsJSONAndEventStream("application/json, */*") {
		t.Fatal("expected wildcard to not stand in for text/event-stream")
	}
}

func TestProtocolVersionHeader(t *testing.T) {
	if !validProtocolVersionHeader("") {
		t.Fatal("missing protocol header should be allowed")
	}
	if !validProtocolVersionHeader("2025-06-18") {
		t.Fatal("latest protocol version should be allowed")
	}
	if !validProtocolVersionHeader("2025-03-26") {
		t.Fatal("default fallback protocol version should be allowed")
	}
	if validProtocolVersionHeader("not-a-date") {
		t.Fatal("invalid protocol version should be rejected")
	}
}

func TestEffectiveProtocolVersionForHeader(t *testing.T) {
	if got := effectiveProtocolVersionForHeader(""); got != defaultProtocolVersionWhenHeaderMissing {
		t.Fatalf("expected missing header fallback %q, got %q", defaultProtocolVersionWhenHeaderMissing, got)
	}
	if got := effectiveProtocolVersionForHeader("2025-03-26"); got != defaultProtocolVersionWhenHeaderMissing {
		t.Fatalf("expected explicit compatibility version, got %q", got)
	}
	if got := effectiveProtocolVersionForHeader("2025-06-18"); got != latestProtocolVersion {
		t.Fatalf("expected latest version, got %q", got)
	}
}

func TestValidOriginHeaderAllowsOnlyLoopbackOrigins(t *testing.T) {
	if !validOriginHeader("") {
		t.Fatal("missing origin should be allowed")
	}
	if !validOriginHeader("http://localhost:3000") {
		t.Fatal("localhost origin should be allowed")
	}
	if !validOriginHeader("http://localhost:3000/") {
		t.Fatal("localhost origin with slash should be allowed")
	}
	if !validOriginHeader("https://127.0.0.1:3000") {
		t.Fatal("127.0.0.1 origin should be allowed")
	}
	if !validOriginHeader("http://127.0.0.2:3000") {
		t.Fatal("127.0.0.2 loopback origin should be allowed")
	}
	if !validOriginHeader("http://[::1]:3000") {
		t.Fatal("IPv6 loopback origin should be allowed")
	}
	if !validOriginHeader("http://[::ffff:127.0.0.1]:3000") {
		t.Fatal("IPv4-mapped IPv6 loopback origin should be allowed")
	}
	if validOriginHeader("https://example.com") {
		t.Fatal("non-local origin should be rejected")
	}
	if validOriginHeader("null") {
		t.Fatal("null origin should be rejected")
	}
	if validOriginHeader("file://local/test.html") {
		t.Fatal("file origin should be rejected")
	}
	if validOriginHeader("http://localhost:3000/path") {
		t.Fatal("origin with path should be rejected")
	}
}

func TestBridgeGETMCPReturns405BecauseStandaloneSSEIsNotSupported(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodGet, "/mcp", nil)
	req.Header.Set("Accept", "text/event-stream")
	req.Header.Set("Authorization", "Bearer secret")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("expected 405, got %d", rec.Code)
	}
}

func TestBridgePOSTRejectsNonLocalOrigin(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"initialize"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	req.Header.Set("Origin", "https://example.com")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusForbidden {
		t.Fatalf("expected 403, got %d", rec.Code)
	}
}

func TestBridgePOSTLocalOriginIsEchoedForCORS(t *testing.T) {
	const origin = "http://localhost:3000"
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"initialize"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	req.Header.Set("Origin", origin)
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}
	if got := rec.Header().Get("Access-Control-Allow-Origin"); got != origin {
		t.Fatalf("expected Access-Control-Allow-Origin %q, got %q", origin, got)
	}
}

func TestBridgeHealthRejectsNonLocalOrigin(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodGet, "/health", nil)
	req.Header.Set("Origin", "https://example.com")
	rec := httptest.NewRecorder()

	bridge.handleRoot(rec, req)

	if rec.Code != http.StatusForbidden {
		t.Fatalf("expected 403, got %d", rec.Code)
	}
}

func TestBridgePOSTMissingAcceptStaysCompatible(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}`))
	req.Header.Set("Authorization", "Bearer secret")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200 compatibility response, got %d", rec.Code)
	}
	if got := rec.Header().Get(headerProtocolVersion); got != latestProtocolVersion {
		t.Fatalf("expected protocol header %q, got %q", latestProtocolVersion, got)
	}
}

func TestBridgeInitializeOlderProtocolKeepsResponseHeaderAndBodyAligned(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}
	if got := rec.Header().Get(headerProtocolVersion); got != defaultProtocolVersionWhenHeaderMissing {
		t.Fatalf("expected protocol header %q, got %q", defaultProtocolVersionWhenHeaderMissing, got)
	}
	if body := rec.Body.String(); !strings.Contains(body, `"protocolVersion":"2025-03-26"`) {
		t.Fatalf("expected negotiated protocol in response body, got %s", body)
	}
}

func TestBridgePingMissingProtocolHeaderUsesCompatibilityDefault(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"ping"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}
	if got := rec.Header().Get(headerProtocolVersion); got != defaultProtocolVersionWhenHeaderMissing {
		t.Fatalf("expected protocol header %q, got %q", defaultProtocolVersionWhenHeaderMissing, got)
	}
}

func TestBridgePOSTInvalidProtocolVersionReturns400(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"initialize"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	req.Header.Set(headerProtocolVersion, "not-a-date")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("expected 400, got %d", rec.Code)
	}
}

func TestBridgeNotificationReturns202(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","method":"notifications/initialized"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusAccepted {
		t.Fatalf("expected 202, got %d", rec.Code)
	}
}

func TestBridgeJSONRPCResponseReturns202(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"result":{}}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusAccepted {
		t.Fatalf("expected 202, got %d", rec.Code)
	}
}

func TestBridgeMissingMethodRequestReturnsInvalidRequest(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected JSON-RPC error over 200, got %d", rec.Code)
	}
	if body := rec.Body.String(); !strings.Contains(body, `"code":-32600`) {
		t.Fatalf("expected invalid request error, got %s", body)
	}
}

func TestBridgeResponseWithoutIDReturnsInvalidRequest(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","result":{}}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected JSON-RPC error over 200, got %d", rec.Code)
	}
	if body := rec.Body.String(); !strings.Contains(body, `"code":-32600`) {
		t.Fatalf("expected invalid request error, got %s", body)
	}
}

func TestBridgeResponseWithResultAndErrorReturnsInvalidRequest(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"result":{},"error":{"code":-32000,"message":"bad"}}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected JSON-RPC error over 200, got %d", rec.Code)
	}
	if body := rec.Body.String(); !strings.Contains(body, `"code":-32600`) {
		t.Fatalf("expected invalid request error, got %s", body)
	}
}

func TestBridgeRejectsInvalidJSONRPCVersion(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"1.0","id":1,"method":"ping"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"code":-32600`) {
		t.Fatalf("expected invalid request for jsonrpc 1.0, got status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestBridgeRejectsRequestContainingResponseMembers(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"ping","result":{}}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"code":-32600`) {
		t.Fatalf("expected invalid mixed request/response, got status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestBridgeRejectsStructuredJSONRPCID(t *testing.T) {
	bridge := newBridge("secret")
	req := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":{},"method":"ping"}`))
	req.Header.Set("Authorization", "Bearer secret")
	req.Header.Set("Accept", "application/json, text/event-stream")
	rec := httptest.NewRecorder()

	bridge.handleMCP(rec, req)

	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), `"code":-32600`) {
		t.Fatalf("expected invalid structured id, got status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestCanceledQueuedCallIsRemovedBeforeReconnect(t *testing.T) {
	bridge := newBridge("secret")
	ctx, cancel := context.WithCancel(context.Background())
	req := rpcRequest{
		ID:     []byte("1"),
		Method: "tools/call",
		Params: json.RawMessage(`{"name":"DeleteAsset","arguments":{}}`),
	}
	rec := httptest.NewRecorder()
	done := make(chan struct{})
	go func() {
		bridge.handleToolsCallContext(rec, req, ctx)
		close(done)
	}()

	deadline := time.After(2 * time.Second)
	for {
		bridge.mu.Lock()
		queued := len(bridge.queueWhenDown)
		bridge.mu.Unlock()
		if queued == 1 {
			break
		}
		select {
		case <-deadline:
			t.Fatal("call was not queued while Unity was disconnected")
		default:
			time.Sleep(time.Millisecond)
		}
	}
	cancel()

	select {
	case <-done:
	case <-time.After(2 * time.Second):
		t.Fatal("canceled call handler did not return")
	}
	bridge.mu.Lock()
	defer bridge.mu.Unlock()
	if len(bridge.queueWhenDown) != 0 {
		t.Fatalf("canceled call remained in reconnect queue: %d", len(bridge.queueWhenDown))
	}
	if len(bridge.pending) != 0 {
		t.Fatalf("canceled call remained pending: %d", len(bridge.pending))
	}
}

func TestCanceledQueuedCallIsSkippedByReconnectFlush(t *testing.T) {
	bridge := newBridge("secret")
	ctx, cancel := context.WithCancel(context.Background())
	call := &pendingCall{
		ID:       "canceled-before-flush",
		Tool:     "DeleteAsset",
		Args:     json.RawMessage(`{}`),
		Response: make(chan callResult, 1),
		Created:  time.Now(),
		Context:  ctx,
	}
	bridge.mu.Lock()
	bridge.pending[call.ID] = call
	bridge.mu.Unlock()
	if err := bridge.dispatchToUnity(call); err != nil {
		t.Fatalf("queue call: %v", err)
	}
	cancel()

	// Simulate reconnect cleanup running before the HTTP handler gets to remove
	// its pending entry. The canceled call must not be sent or re-queued.
	bridge.flushQueuedCalls()
	bridge.mu.Lock()
	queued := len(bridge.queueWhenDown)
	_, stillPending := bridge.pending[call.ID]
	bridge.mu.Unlock()
	if queued != 0 {
		t.Fatalf("canceled call was left in reconnect queue: %d", queued)
	}
	if !stillPending {
		t.Fatal("flush should leave pending ownership to the canceled HTTP handler")
	}
	bridge.cancelPendingCall(call)
}

func TestStaleQueuedCallReceivesTimeoutResult(t *testing.T) {
	bridge := newBridge("secret")
	call := &pendingCall{
		ID:       "stale-queued-call",
		Tool:     "DeleteAsset",
		Response: make(chan callResult, 1),
		Created:  time.Now().Add(-callTimeout - time.Second),
	}
	bridge.mu.Lock()
	bridge.pending[call.ID] = call
	bridge.queueWhenDown = append(bridge.queueWhenDown, call)
	bridge.mu.Unlock()

	if dropped := bridge.pruneStaleQueuedCalls(time.Now()); dropped != 1 {
		t.Fatalf("expected one stale call to be dropped, got %d", dropped)
	}
	select {
	case res := <-call.Response:
		if res.OK || res.ErrCode != -32001 || res.Error != "Timeout" {
			t.Fatalf("expected timeout result, got %+v", res)
		}
	case <-time.After(time.Second):
		t.Fatal("stale queued call did not receive timeout result")
	}
}
