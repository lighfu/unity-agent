package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
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
