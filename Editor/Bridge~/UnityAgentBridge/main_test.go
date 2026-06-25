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
