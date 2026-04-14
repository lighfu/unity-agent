// UnityAgentBridge — Out-of-process MCP HTTP/SSE endpoint that survives Unity domain reloads.
//
// Architecture:
//
//   ┌─────────────┐    HTTP+SSE    ┌──────────────┐    TCP+JSONL    ┌──────────────┐
//   │ MCP client  │ ─────────────→ │   Bridge     │ ←─────────────→ │ Unity Editor │
//   │ (Claude/etc)│                │   (this)     │                 │              │
//   └─────────────┘                └──────────────┘                 └──────────────┘
//                                   long-lived                       reloads, reconnects
//
// The bridge owns the public MCP HTTP endpoint and the persistent SSE channel for one MCP
// client. It accepts a single Unity TCP connection. When Unity disconnects (domain reload),
// pending tool-call requests are held in a queue and re-dispatched when Unity reconnects.
//
// P1 SCOPE: skeleton only. Just enough to:
//   1. Accept Unity over TCP (with shared-secret token auth)
//   2. Accept Claude Code / curl over HTTP /mcp (Bearer token auth)
//   3. Forward `tools/call` from MCP client → Unity → response
//   4. Survive Unity reload (buffering tool calls, replying to MCP client when Unity is back)
//
// NOT in P1: OAuth discovery, SSE, multi-MCP-client, fault-tolerant reconnect after crash.
// Those land in P2 and beyond.
//
// Build (Windows): go build -o ../../../Editor/Bridge/bin/win-x64/UnityAgentBridge.exe .
package main

import (
	"bufio"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"sync"
	"sync/atomic"
	"time"
)

// ─────────────────────────────────────────────────────────────────────────
// Constants & flags
// ─────────────────────────────────────────────────────────────────────────

const (
	defaultPublicPort   = 17800 // MCP HTTP endpoint exposed to Claude Code et al.
	defaultInternalPort = 17801 // TCP endpoint where Unity connects in.
	mcpEndpointPath     = "/mcp"
	idleQuitGrace       = 5 * time.Minute  // Quit after both sides are gone for this long.
	callTimeout         = 120 * time.Second // Per-call timeout, matches AgentMCPServer.cs default.
)

var (
	publicPort   = flag.Int("public-port", defaultPublicPort, "HTTP port for MCP clients")
	internalPort = flag.Int("internal-port", defaultInternalPort, "TCP port where Unity connects")
	authToken    = flag.String("token", "", "Shared secret for both MCP Bearer auth and Unity hello (REQUIRED)")
	logFile      = flag.String("log", "", "Optional log file path. Empty = stderr only.")
	verbose      = flag.Bool("verbose", false, "Verbose logging")
)

// ─────────────────────────────────────────────────────────────────────────
// Bridge: shared state between HTTP and TCP halves
// ─────────────────────────────────────────────────────────────────────────

type pendingCall struct {
	ID       string          // bridge-internal request id (uuid-ish string)
	Tool     string          // tool name
	Args     json.RawMessage // tool arguments
	Response chan callResult // closed when Unity responds (or timeout)
	Created  time.Time
}

type callResult struct {
	OK      bool
	Text    string
	Error   string
	ErrCode int
}

// Bridge holds the routing state. There is exactly one of these per process.
type Bridge struct {
	token string

	mu             sync.Mutex
	unityConn      net.Conn // current Unity TCP connection (nil if disconnected)
	unityWriter    *bufio.Writer
	pending        map[string]*pendingCall // bridge-id → call
	queueWhenDown  []*pendingCall          // calls received while Unity was disconnected
	mcpClientCount int                     // number of active MCP HTTP requests in flight
	lastActivity   time.Time

	// Atomic counters for diagnostics
	callsServed atomic.Uint64
}

func newBridge(token string) *Bridge {
	return &Bridge{
		token:        token,
		pending:      make(map[string]*pendingCall),
		lastActivity: time.Now(),
	}
}

// ─────────────────────────────────────────────────────────────────────────
// Unity-side: TCP server (single connection)
// ─────────────────────────────────────────────────────────────────────────

// Wire format (each direction): one JSON object per line, newline-terminated.
//
// Unity → Bridge (handshake):
//
//	{"type":"hello","version":"1","token":"..."}
//
// Unity → Bridge (response to a call):
//
//	{"type":"result","id":"...","ok":true,"text":"..."}
//	{"type":"error", "id":"...","code":-32000,"message":"...","data":"..."}
//
// Unity → Bridge (reload notice; sent in beforeAssemblyReload):
//
//	{"type":"shutdown","reason":"domain_reload"}
//
// Bridge → Unity (call dispatch):
//
//	{"type":"call","id":"...","tool":"WriteFile","args":{...}}
//
// Bridge → Unity (handshake ack):
//
//	{"type":"hello_ack","ok":true}
//	{"type":"hello_ack","ok":false,"error":"bad token"}
type wireMsg struct {
	Type    string          `json:"type"`
	Version string          `json:"version,omitempty"`
	Token   string          `json:"token,omitempty"`
	ID      string          `json:"id,omitempty"`
	Tool    string          `json:"tool,omitempty"`
	Args    json.RawMessage `json:"args,omitempty"`
	OK      bool            `json:"ok,omitempty"`
	Text    string          `json:"text,omitempty"`
	Error   string          `json:"error,omitempty"`
	Message string          `json:"message,omitempty"`
	Code    int             `json:"code,omitempty"`
	Data    string          `json:"data,omitempty"`
	Reason  string          `json:"reason,omitempty"`
}

func (b *Bridge) startUnityTCPServer(addr string) error {
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		return fmt.Errorf("listen %s: %w", addr, err)
	}
	log.Printf("[bridge] unity TCP listening on %s", addr)

	go func() {
		for {
			conn, err := ln.Accept()
			if err != nil {
				log.Printf("[bridge] accept error: %v", err)
				return
			}
			// Single-connection model: if a previous Unity is still connected, drop the old one
			// (likely a stale handle from before reload).
			b.swapUnityConn(conn)
			go b.handleUnityConn(conn)
		}
	}()
	return nil
}

func (b *Bridge) swapUnityConn(newConn net.Conn) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.unityConn != nil {
		log.Printf("[bridge] dropping previous Unity connection (new connection arrived)")
		_ = b.unityConn.Close()
	}
	b.unityConn = newConn
	b.unityWriter = bufio.NewWriter(newConn)
	b.lastActivity = time.Now()
}

func (b *Bridge) handleUnityConn(conn net.Conn) {
	defer func() {
		_ = conn.Close()
		b.mu.Lock()
		if b.unityConn == conn {
			b.unityConn = nil
			b.unityWriter = nil
		}
		b.mu.Unlock()
		log.Printf("[bridge] unity connection closed")
	}()

	scanner := bufio.NewScanner(conn)
	scanner.Buffer(make([]byte, 0, 64*1024), 16*1024*1024) // 16MB max line for big tool results
	authed := false

	for scanner.Scan() {
		line := scanner.Bytes()
		if len(line) == 0 {
			continue
		}
		var msg wireMsg
		if err := json.Unmarshal(line, &msg); err != nil {
			log.Printf("[bridge] unity sent malformed JSON: %v", err)
			continue
		}

		if !authed {
			if msg.Type != "hello" {
				_ = b.writeUnityMsg(conn, wireMsg{Type: "hello_ack", OK: false, Error: "expected hello first"})
				return
			}
			if msg.Token != b.token {
				_ = b.writeUnityMsg(conn, wireMsg{Type: "hello_ack", OK: false, Error: "bad token"})
				log.Printf("[bridge] unity auth failed (token mismatch)")
				return
			}
			authed = true
			_ = b.writeUnityMsg(conn, wireMsg{Type: "hello_ack", OK: true})
			log.Printf("[bridge] unity authed (version=%s)", msg.Version)
			b.flushQueuedCalls()
			continue
		}

		switch msg.Type {
		case "result":
			b.deliverResult(msg.ID, callResult{OK: true, Text: msg.Text})
		case "error":
			b.deliverResult(msg.ID, callResult{OK: false, Error: msg.Message, ErrCode: msg.Code})
		case "shutdown":
			log.Printf("[bridge] unity announced shutdown (reason=%s)", msg.Reason)
			// Connection will close shortly — let the deferred cleanup handle it.
		default:
			log.Printf("[bridge] unknown message type from unity: %s", msg.Type)
		}
	}

	if err := scanner.Err(); err != nil && !errors.Is(err, io.EOF) {
		log.Printf("[bridge] unity scanner error: %v", err)
	}
}

func (b *Bridge) writeUnityMsg(conn net.Conn, msg wireMsg) error {
	bytes, err := json.Marshal(msg)
	if err != nil {
		return err
	}
	bytes = append(bytes, '\n')
	_, err = conn.Write(bytes)
	return err
}

// flushQueuedCalls re-dispatches calls that arrived while Unity was disconnected.
func (b *Bridge) flushQueuedCalls() {
	b.mu.Lock()
	queue := b.queueWhenDown
	b.queueWhenDown = nil
	b.mu.Unlock()

	for _, call := range queue {
		log.Printf("[bridge] flushing queued call id=%s tool=%s", call.ID, call.Tool)
		if err := b.dispatchToUnity(call); err != nil {
			b.deliverResult(call.ID, callResult{OK: false, Error: "dispatch after reconnect failed: " + err.Error(), ErrCode: -32603})
		}
	}
}

// dispatchToUnity sends a call message to Unity. Caller must have already inserted into pending map.
// If Unity is not connected, the call is queued and returns nil (deferred dispatch).
func (b *Bridge) dispatchToUnity(call *pendingCall) error {
	b.mu.Lock()
	defer b.mu.Unlock()

	if b.unityConn == nil {
		log.Printf("[bridge] queueing call id=%s (unity not connected)", call.ID)
		b.queueWhenDown = append(b.queueWhenDown, call)
		return nil
	}
	msg := wireMsg{Type: "call", ID: call.ID, Tool: call.Tool, Args: call.Args}
	bytes, err := json.Marshal(msg)
	if err != nil {
		return err
	}
	bytes = append(bytes, '\n')
	if _, err := b.unityWriter.Write(bytes); err != nil {
		return err
	}
	return b.unityWriter.Flush()
}

func (b *Bridge) deliverResult(id string, result callResult) {
	b.mu.Lock()
	call, ok := b.pending[id]
	if ok {
		delete(b.pending, id)
	}
	b.mu.Unlock()
	if !ok {
		log.Printf("[bridge] orphan result for unknown id=%s", id)
		return
	}
	select {
	case call.Response <- result:
	default:
		// Response channel full (timeout already happened). Drop.
	}
	b.callsServed.Add(1)
}

// ─────────────────────────────────────────────────────────────────────────
// MCP-side: HTTP server (JSON-RPC over /mcp)
// ─────────────────────────────────────────────────────────────────────────

type rpcRequest struct {
	JsonRPC string          `json:"jsonrpc"`
	ID      json.RawMessage `json:"id,omitempty"`
	Method  string          `json:"method"`
	Params  json.RawMessage `json:"params,omitempty"`
}

type rpcResponse struct {
	JsonRPC string          `json:"jsonrpc"`
	ID      json.RawMessage `json:"id,omitempty"`
	Result  any             `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
}

type rpcError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
	Data    string `json:"data,omitempty"`
}

func (b *Bridge) startMCPHTTPServer(addr string) error {
	mux := http.NewServeMux()
	mux.HandleFunc("/", b.handleRoot)
	mux.HandleFunc(mcpEndpointPath, b.handleMCP)

	srv := &http.Server{
		Addr:              addr,
		Handler:           mux,
		ReadHeaderTimeout: 30 * time.Second,
	}
	go func() {
		log.Printf("[bridge] mcp HTTP listening on %s%s", addr, mcpEndpointPath)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("[bridge] http server error: %v", err)
		}
	}()
	return nil
}

func (b *Bridge) handleRoot(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path != "/" && r.URL.Path != "/health" {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	fmt.Fprintf(w, `{"server":"UnityAgentBridge","status":"ok","calls_served":%d}`, b.callsServed.Load())
}

func (b *Bridge) handleMCP(w http.ResponseWriter, r *http.Request) {
	// CORS for local tools
	w.Header().Set("Access-Control-Allow-Origin", "*")
	w.Header().Set("Access-Control-Allow-Methods", "POST, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization")

	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return
	}
	if r.Method != http.MethodPost {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}

	// Bearer auth — must match shared token.
	if !b.checkBearer(r.Header.Get("Authorization")) {
		// Per AgentMCPServer.cs convention: respond 200 with JSON-RPC error so Claude Code
		// does not try OAuth discovery. P1 uses static Bearer only.
		writeJSONRPCError(w, json.RawMessage("null"), -32001, "Unauthorized", "Missing or invalid Authorization header.")
		return
	}

	body, err := io.ReadAll(io.LimitReader(r.Body, 2*1024*1024))
	if err != nil {
		writeJSONRPCError(w, json.RawMessage("null"), -32700, "Parse error", err.Error())
		return
	}
	var req rpcRequest
	if err := json.Unmarshal(body, &req); err != nil {
		writeJSONRPCError(w, json.RawMessage("null"), -32700, "Parse error", err.Error())
		return
	}

	if *verbose {
		log.Printf("[bridge] mcp ← %s id=%s", req.Method, string(req.ID))
	}

	// Notifications (no response expected).
	if len(req.ID) == 0 || string(req.ID) == "null" {
		w.WriteHeader(http.StatusAccepted)
		return
	}

	switch req.Method {
	case "initialize":
		writeJSONRPCResult(w, req.ID, b.handleInitialize())
	case "ping":
		writeJSONRPCResult(w, req.ID, map[string]any{})
	case "tools/list":
		writeJSONRPCResult(w, req.ID, b.handleToolsList())
	case "prompts/list":
		writeJSONRPCResult(w, req.ID, map[string]any{"prompts": []any{}})
	case "resources/list":
		writeJSONRPCResult(w, req.ID, map[string]any{"resources": []any{}})
	case "resources/templates/list":
		writeJSONRPCResult(w, req.ID, map[string]any{"resourceTemplates": []any{}})
	case "tools/call":
		b.handleToolsCall(w, req)
	default:
		writeJSONRPCError(w, req.ID, -32601, "Method not found", req.Method)
	}
}

func (b *Bridge) checkBearer(header string) bool {
	const prefix = "Bearer "
	if len(header) < len(prefix) || header[:len(prefix)] != prefix {
		return false
	}
	return constantTimeEq(header[len(prefix):], b.token)
}

func constantTimeEq(a, b string) bool {
	if len(a) != len(b) {
		return false
	}
	var diff byte
	for i := 0; i < len(a); i++ {
		diff |= a[i] ^ b[i]
	}
	return diff == 0
}

// handleInitialize returns the same shape as AgentMCPServer.Handlers.HandleInitialize so existing
// MCP clients (Claude Code) treat the bridge as a drop-in replacement.
func (b *Bridge) handleInitialize() map[string]any {
	return map[string]any{
		"protocolVersion": "2024-11-05",
		"capabilities": map[string]any{
			"experimental": map[string]any{},
			"prompts":      map[string]any{"listChanged": false},
			"resources":    map[string]any{"subscribe": false, "listChanged": false},
			"tools":        map[string]any{"listChanged": true},
		},
		"serverInfo": map[string]any{
			"name":    "UnityAgentBridge",
			"version": "0.1.0",
		},
	}
}

// handleToolsList returns the same 3 meta-tools as the InProc server.
// In P1 we hardcode the schemas; P2 will fetch them from Unity at hello time.
func (b *Bridge) handleToolsList() map[string]any {
	return map[string]any{
		"tools": []any{
			map[string]any{
				"name":        "SearchUnityTool",
				"description": "Search for Unity Editor tools by keyword.",
				"inputSchema": map[string]any{
					"type": "object",
					"properties": map[string]any{
						"query": map[string]any{"type": "string"},
						"limit": map[string]any{"type": "integer", "default": 20},
					},
					"required": []any{"query"},
				},
			},
			map[string]any{
				"name":        "DescribeUnityTool",
				"description": "Get full parameter schema for a Unity tool.",
				"inputSchema": map[string]any{
					"type": "object",
					"properties": map[string]any{
						"name": map[string]any{"type": "string"},
					},
					"required": []any{"name"},
				},
			},
			map[string]any{
				"name":        "ExecuteUnityTool",
				"description": "Execute a Unity Editor tool by name.",
				"inputSchema": map[string]any{
					"type": "object",
					"properties": map[string]any{
						"name":      map[string]any{"type": "string"},
						"arguments": map[string]any{"type": "object", "additionalProperties": true},
					},
					"required": []any{"name", "arguments"},
				},
			},
		},
	}
}

func (b *Bridge) handleToolsCall(w http.ResponseWriter, req rpcRequest) {
	var params struct {
		Name      string          `json:"name"`
		Arguments json.RawMessage `json:"arguments"`
	}
	if err := json.Unmarshal(req.Params, &params); err != nil {
		writeJSONRPCError(w, req.ID, -32602, "Invalid params", err.Error())
		return
	}
	if params.Name == "" {
		writeJSONRPCError(w, req.ID, -32602, "Invalid params", "tool name is required")
		return
	}

	call := &pendingCall{
		ID:       generateID(),
		Tool:     params.Name,
		Args:     params.Arguments,
		Response: make(chan callResult, 1),
		Created:  time.Now(),
	}
	b.mu.Lock()
	b.pending[call.ID] = call
	b.mu.Unlock()

	if err := b.dispatchToUnity(call); err != nil {
		b.mu.Lock()
		delete(b.pending, call.ID)
		b.mu.Unlock()
		writeJSONRPCError(w, req.ID, -32603, "Dispatch failed", err.Error())
		return
	}

	select {
	case res := <-call.Response:
		if !res.OK {
			writeJSONRPCError(w, req.ID, res.ErrCode, res.Error, "")
			return
		}
		writeJSONRPCResult(w, req.ID, map[string]any{
			"content": []any{
				map[string]any{"type": "text", "text": res.Text},
			},
			"isError": false,
		})
	case <-time.After(callTimeout):
		b.mu.Lock()
		delete(b.pending, call.ID)
		b.mu.Unlock()
		writeJSONRPCError(w, req.ID, -32001, "Timeout",
			fmt.Sprintf("Tool '%s' did not complete within %s.", params.Name, callTimeout))
	}
}

func writeJSONRPCResult(w http.ResponseWriter, id json.RawMessage, result any) {
	resp := rpcResponse{JsonRPC: "2.0", ID: id, Result: result}
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(resp)
}

func writeJSONRPCError(w http.ResponseWriter, id json.RawMessage, code int, message, data string) {
	resp := rpcResponse{
		JsonRPC: "2.0",
		ID:      id,
		Error:   &rpcError{Code: code, Message: message, Data: data},
	}
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(resp)
}

// ─────────────────────────────────────────────────────────────────────────
// Misc helpers
// ─────────────────────────────────────────────────────────────────────────

var idCounter atomic.Uint64

func generateID() string {
	n := idCounter.Add(1)
	return fmt.Sprintf("c%d-%d", time.Now().UnixNano(), n)
}

// ─────────────────────────────────────────────────────────────────────────
// Entry point
// ─────────────────────────────────────────────────────────────────────────

func main() {
	flag.Parse()

	if *authToken == "" {
		fmt.Fprintln(os.Stderr, "ERROR: --token is required")
		os.Exit(2)
	}

	if *logFile != "" {
		f, err := os.OpenFile(*logFile, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
		if err != nil {
			fmt.Fprintf(os.Stderr, "ERROR: open log file %s: %v\n", *logFile, err)
			os.Exit(2)
		}
		defer f.Close()
		log.SetOutput(io.MultiWriter(os.Stderr, f))
	}
	log.SetFlags(log.LstdFlags | log.Lmicroseconds)
	log.Printf("[bridge] UnityAgentBridge starting (public=%d internal=%d)", *publicPort, *internalPort)

	b := newBridge(*authToken)

	if err := b.startUnityTCPServer(fmt.Sprintf("127.0.0.1:%d", *internalPort)); err != nil {
		log.Fatalf("[bridge] unity TCP server failed: %v", err)
	}
	if err := b.startMCPHTTPServer(fmt.Sprintf("127.0.0.1:%d", *publicPort)); err != nil {
		log.Fatalf("[bridge] mcp HTTP server failed: %v", err)
	}

	// Idle quit watchdog: shut down if both Unity and any MCP client have been gone
	// for idleQuitGrace continuously. P1 only checks Unity presence.
	go func() {
		ticker := time.NewTicker(30 * time.Second)
		defer ticker.Stop()
		for range ticker.C {
			b.mu.Lock()
			idleSince := time.Since(b.lastActivity)
			hasUnity := b.unityConn != nil
			b.mu.Unlock()
			if !hasUnity && idleSince > idleQuitGrace {
				log.Printf("[bridge] idle for %s with no Unity — exiting", idleSince)
				os.Exit(0)
			}
		}
	}()

	// Block forever
	select {}
}
