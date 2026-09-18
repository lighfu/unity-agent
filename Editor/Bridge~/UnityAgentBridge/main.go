// UnityAgentBridge — Out-of-process MCP HTTP endpoint that survives Unity domain reloads.
//
// Architecture:
//
//	┌─────────────┐    HTTP POST   ┌──────────────┐    TCP+JSONL    ┌──────────────┐
//	│ MCP client  │ ─────────────→ │   Bridge     │ ←─────────────→ │ Unity Editor │
//	│ (Claude/etc)│                │   (this)     │                 │              │
//	└─────────────┘                └──────────────┘                 └──────────────┘
//	                                long-lived                       reloads, reconnects
//
// The bridge owns the public MCP HTTP endpoint for one MCP client. It accepts a single Unity
// TCP connection. When Unity disconnects (domain reload), calls already sent to Unity fail at
// once with an error naming the cause (-32003), while not-yet-sent tool-call requests are held
// in a queue and re-dispatched when Unity reconnects.
//
// P1 SCOPE: skeleton only. Just enough to:
//  1. Accept Unity over TCP (with shared-secret token auth)
//  2. Accept Claude Code / curl over HTTP /mcp (Bearer token auth)
//  3. Forward `tools/call` from MCP client → Unity → response
//  4. Survive Unity reload (buffering tool calls, replying to MCP client when Unity is back)
//
// NOT in P1: OAuth discovery, SSE, multi-MCP-client, fault-tolerant reconnect after crash.
// Those land in P2 and beyond.
//
// Build (Windows): go build -o ../../../Editor/Bridge/bin/win-x64/UnityAgentBridge.exe .
package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// ─────────────────────────────────────────────────────────────────────────
// Constants & flags
// ─────────────────────────────────────────────────────────────────────────

const (
	defaultPublicPort                       = 17800 // MCP HTTP endpoint exposed to Claude Code et al.
	defaultInternalPort                     = 17801 // TCP endpoint where Unity connects in.
	mcpEndpointPath                         = "/mcp"
	latestProtocolVersion                   = "2025-06-18"
	defaultProtocolVersionWhenHeaderMissing = "2025-03-26"
	headerProtocolVersion                   = "MCP-Protocol-Version"
	defaultIdleQuitGrace                    = 5 * time.Minute   // Quit after both sides are gone for this long.
	callTimeout                             = 120 * time.Second // Per-call timeout, matches AgentMCPServer.cs default.
	maintenanceInterval                     = 30 * time.Second  // How often the watchdog prunes and checks for idleness.
	defaultHelloTimeout                     = 10 * time.Second  // How long a new connection has to authenticate before it is dropped.
)

var (
	publicPort    = flag.Int("public-port", defaultPublicPort, "HTTP port for MCP clients")
	internalPort  = flag.Int("internal-port", defaultInternalPort, "TCP port where Unity connects")
	authToken     = flag.String("token", "", "Shared secret for both MCP Bearer auth and Unity hello (REQUIRED)")
	logFile       = flag.String("log", "", "Optional log file path. Empty = stderr only.")
	verbose       = flag.Bool("verbose", false, "Verbose logging")
	idleQuitGrace = flag.Duration("idle-quit", defaultIdleQuitGrace,
		"Exit after this long with no Unity connection AND no MCP client activity (e.g. 5m, 30s). Zero or negative disables the idle quit entirely.")
)

// ─────────────────────────────────────────────────────────────────────────
// Bridge: shared state between HTTP and TCP halves
// ─────────────────────────────────────────────────────────────────────────

type pendingCall struct {
	ID       string          // bridge-internal request id (uuid-ish string)
	Tool     string          // tool name
	Args     json.RawMessage // tool arguments
	Response chan callResult // receives when Unity responds, disconnects, or times out
	Created  time.Time
	Context  context.Context // MCP request context; nil for internal/test calls
	// The Unity connection this call was written to, nil while it sits in queueWhenDown.
	// Lets the connection's cleanup fail exactly the calls that died with it — not the ones
	// still queued, and not ones already re-dispatched on a newer connection. Guarded by
	// Bridge.mu.
	DispatchedOn net.Conn
}

type callResult struct {
	OK      bool
	Text    string
	Error   string
	ErrCode int
	Data    string // JSON-RPC error.data: guidance for the caller, never shown on success
}

// Error code for a call that was dispatched to Unity and then orphaned by the connection
// closing underneath it. Distinct from -32001 (Timeout) so a client can tell "Unity went
// away" from "the tool is slow" without waiting callTimeout to find out, and from -32002,
// which the Unity side already uses for "main thread blocked" (AgentMCPBridgeClient /
// AgentMCPServer reject a call up front when a modal dialog holds the main thread).
const errCodeInterrupted = -32003

// errCallCanceled is internal: it tells a reconnect flush that the HTTP caller
// went away before the queued call could be dispatched.
var errCallCanceled = errors.New("call canceled")

// Bridge holds the routing state. There is exactly one of these per process.
type Bridge struct {
	token string

	// How long a freshly accepted connection has to send its hello. A field rather than a
	// constant so tests can shorten it without a package-level global.
	helloTimeout time.Duration

	mu             sync.Mutex
	unityConn      net.Conn // current Unity TCP connection (nil if disconnected)
	unityWriter    *bufio.Writer
	pending        map[string]*pendingCall // bridge-id → call
	queueWhenDown  []*pendingCall          // calls received while Unity was disconnected
	mcpClientCount int                     // MCP HTTP requests currently being served (idle-quit guard)
	lastActivity   time.Time               // last Unity message OR MCP HTTP request; drives the idle quit

	// Atomic counters for diagnostics
	callsServed atomic.Uint64
}

func newBridge(token string) *Bridge {
	return &Bridge{
		token:        token,
		helloTimeout: defaultHelloTimeout,
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
			go b.handleUnityConn(conn)
		}
	}()
	return nil
}

func (b *Bridge) swapUnityConn(newConn net.Conn) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.unityConn != nil && b.unityConn != newConn {
		log.Printf("[bridge] dropping previous Unity connection (new connection arrived)")
		_ = b.unityConn.Close()
	}
	b.unityConn = newConn
	b.unityWriter = bufio.NewWriter(newConn)
	b.lastActivity = time.Now()
}

func (b *Bridge) handleUnityConn(conn net.Conn) {
	// Set from the "shutdown" message, if Unity sends one before the socket closes. Local to
	// this connection on purpose: a reason announced on one connection must never be
	// attributed to another.
	shutdownReason := ""

	defer func() {
		_ = conn.Close()
		b.mu.Lock()
		if b.unityConn == conn {
			b.unityConn = nil
			b.unityWriter = nil
		}
		// Reset lastActivity on disconnect so the idle-quit grace period starts fresh from
		// the moment Unity went away — not from when it first connected. Without this, a
		// long-lived Unity session followed by a domain reload would cause the bridge to
		// quit immediately because lastActivity was set when Unity originally connected
		// (potentially many minutes ago) and the watchdog sees `now - lastActivity > grace`.
		b.lastActivity = time.Now()
		b.mu.Unlock()
		log.Printf("[bridge] unity connection closed")

		// Calls already written to this connection can never be answered: the Unity that
		// received them is gone (domain reload wipes its pending table; a crash wipes
		// everything). Left alone they sit in `pending` until the HTTP handler's
		// callTimeout fires, so the caller waits the full 120 s to learn nothing. Fail
		// them now, and say why, so the caller can move straight to the post-reload
		// checks. They are NOT re-sent: the typical victim is the call that caused the
		// reload (RefreshAssetDatabase), which would then run twice.
		b.failInterruptedCalls(conn, shutdownReason)
	}()

	scanner := bufio.NewScanner(conn)
	scanner.Buffer(make([]byte, 0, 64*1024), 16*1024*1024) // 16MB max line for big tool results
	authed := false

	// Until the hello lands, nothing ties this connection to a live editor and nothing else
	// will ever close it: swapUnityConn — which drops the connection it replaces — now runs
	// only after the hello has been acknowledged. Without a deadline here, a local process
	// that opens a socket and stays silent parks this goroutine and its file descriptor for
	// the life of the bridge. Cleared once authenticated: an editor may legitimately sit
	// idle for hours between calls.
	if err := conn.SetReadDeadline(time.Now().Add(b.helloTimeout)); err != nil {
		log.Printf("[bridge] failed to set hello deadline: %v", err)
		return
	}

	for scanner.Scan() {
		line := scanner.Bytes()
		if len(line) == 0 {
			continue
		}
		// Each successful message keeps lastActivity recent, so a long-lived connection
		// also "earns" a fresh grace period at the moment it eventually disconnects.
		b.mu.Lock()
		b.lastActivity = time.Now()
		b.mu.Unlock()
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
			// Do not replace the current Unity connection until this connection has
			// authenticated and its acknowledgement is on the wire. Otherwise any
			// local process can connect with a bad token, evict a healthy editor
			// session, or race a call write with the handshake acknowledgement.
			if err := b.writeUnityMsg(conn, wireMsg{Type: "hello_ack", OK: true}); err != nil {
				log.Printf("[bridge] failed to acknowledge Unity hello: %v", err)
				return
			}
			if err := conn.SetReadDeadline(time.Time{}); err != nil {
				log.Printf("[bridge] failed to clear hello deadline: %v", err)
				return
			}
			b.swapUnityConn(conn)
			authed = true
			log.Printf("[bridge] unity authed (version=%s)", msg.Version)
			b.flushQueuedCalls()
			continue
		}

		switch msg.Type {
		case "result":
			b.deliverResult(msg.ID, callResult{OK: true, Text: msg.Text})
		case "error":
			b.deliverResult(msg.ID, callResult{OK: false, Error: msg.Message, ErrCode: msg.Code, Data: msg.Data})
		case "shutdown":
			log.Printf("[bridge] unity announced shutdown (reason=%s)", msg.Reason)
			// Connection will close shortly — the deferred cleanup fails the in-flight
			// calls and uses this reason to tell a planned reload from a lost connection.
			shutdownReason = msg.Reason
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

// pruneStaleQueuedCalls drops queued calls older than callTimeout and returns how many went.
//
// The HTTP handler gives up on a call after callTimeout, but a call that remains in
// `queueWhenDown` would still be dispatched to Unity on reconnect and actually run, which is
// dangerous for destructive tools. Pruning also wakes a handler that lost the timeout race.
func (b *Bridge) pruneStaleQueuedCalls(now time.Time) int {
	b.mu.Lock()
	defer b.mu.Unlock()

	kept := b.queueWhenDown[:0]
	dropped := 0
	for _, call := range b.queueWhenDown {
		if age := now.Sub(call.Created); age > callTimeout {
			delete(b.pending, call.ID)
			select {
			case call.Response <- callResult{OK: false, Error: "Timeout", ErrCode: -32001,
				Data: fmt.Sprintf("Tool '%s' was queued for longer than %s while Unity was disconnected.", call.Tool, callTimeout)}:
			default:
			}
			log.Printf("[bridge] dropping stale queued call id=%s tool=%s age=%s (over %s, caller already gave up)",
				call.ID, call.Tool, age.Round(time.Second), callTimeout)
			dropped++
			continue
		}
		kept = append(kept, call)
	}
	b.queueWhenDown = kept
	return dropped
}

// flushQueuedCalls re-dispatches calls that arrived while Unity was disconnected.
// Calls that outlived callTimeout are discarded first — see pruneStaleQueuedCalls.
func (b *Bridge) flushQueuedCalls() {
	if dropped := b.pruneStaleQueuedCalls(time.Now()); dropped > 0 {
		log.Printf("[bridge] discarded %d stale queued call(s) on reconnect", dropped)
	}

	b.mu.Lock()
	queue := b.queueWhenDown
	b.queueWhenDown = nil
	b.mu.Unlock()

	for _, call := range queue {
		log.Printf("[bridge] flushing queued call id=%s tool=%s", call.ID, call.Tool)
		if err := b.dispatchToUnity(call); err != nil {
			if errors.Is(err, errCallCanceled) {
				log.Printf("[bridge] skipping canceled queued call id=%s tool=%s", call.ID, call.Tool)
				continue
			}
			b.deliverResult(call.ID, callResult{OK: false, Error: "dispatch after reconnect failed: " + err.Error(), ErrCode: -32603})
		}
	}
}

// dispatchToUnity sends a call message to Unity. Caller must have already inserted into pending map.
// If Unity is not connected, the call is queued and returns nil (deferred dispatch).
func (b *Bridge) dispatchToUnity(call *pendingCall) error {
	b.mu.Lock()
	defer b.mu.Unlock()
	if call.Context != nil {
		select {
		case <-call.Context.Done():
			return errCallCanceled
		default:
		}
	}
	if current, ok := b.pending[call.ID]; !ok || current != call {
		return errCallCanceled
	}

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
	// Mark before writing: if the write itself fails the caller drops the call from pending
	// anyway, and marking late would leave a window where the connection dies between the
	// write and the mark and the call is never failed.
	call.DispatchedOn = b.unityConn
	if _, err := b.unityWriter.Write(bytes); err != nil {
		return err
	}
	return b.unityWriter.Flush()
}

// cancelPendingCall removes a call from the pending map and, when it has not
// been sent yet, from the reconnect queue. A call already written to Unity
// cannot be withdrawn from the wire, so its eventual result is simply dropped.
func (b *Bridge) cancelPendingCall(call *pendingCall) bool {
	b.mu.Lock()
	defer b.mu.Unlock()

	current, ok := b.pending[call.ID]
	if !ok || current != call {
		return false
	}
	delete(b.pending, call.ID)
	if call.DispatchedOn != nil {
		return true
	}
	for i, queued := range b.queueWhenDown {
		if queued != call {
			continue
		}
		copy(b.queueWhenDown[i:], b.queueWhenDown[i+1:])
		b.queueWhenDown[len(b.queueWhenDown)-1] = nil
		b.queueWhenDown = b.queueWhenDown[:len(b.queueWhenDown)-1]
		break
	}
	return true
}

// failInterruptedCalls answers every call that was dispatched on `conn` and is still
// unanswered, with an error that names the cause. reason is the "shutdown" reason Unity
// announced, or "" if the socket closed without one.
func (b *Bridge) failInterruptedCalls(conn net.Conn, reason string) {
	b.mu.Lock()
	var interrupted []*pendingCall
	for id, call := range b.pending {
		if call.DispatchedOn == conn {
			interrupted = append(interrupted, call)
			delete(b.pending, id)
		}
	}
	b.mu.Unlock()
	if len(interrupted) == 0 {
		return
	}

	now := time.Now()
	for _, call := range interrupted {
		age := now.Sub(call.Created).Round(100 * time.Millisecond)
		var res callResult
		if reason == "domain_reload" {
			res = callResult{
				OK:      false,
				ErrCode: errCodeInterrupted,
				Error:   "Unity reloaded the app domain while this call was running",
				Data: fmt.Sprintf("Tool '%s' was interrupted by a domain reload after %s. "+
					"Unity usually reconnects within ~10 s. Calls made while Unity is disconnected "+
					"are queued and answered on reconnect, so call CompareAssemblyBaseline / "+
					"GetConsoleLogs now instead of polling. The interrupted call was NOT re-sent.",
					call.Tool, age),
			}
		} else {
			res = callResult{
				OK:      false,
				ErrCode: errCodeInterrupted,
				Error:   "Unity connection lost while this call was running",
				Data: fmt.Sprintf("Tool '%s' was interrupted after %s: the Unity connection closed "+
					"without a shutdown notice, so this was not a planned reload — Unity may have "+
					"crashed or been killed. The call was NOT re-sent."+
					"%s", call.Tool, age, reasonSuffix(reason)),
			}
		}
		log.Printf("[bridge] failing interrupted call id=%s tool=%s age=%s reason=%q",
			call.ID, call.Tool, age, reason)
		select {
		case call.Response <- res:
		default:
			// HTTP handler already gave up (timeout) — nobody is listening.
		}
	}
}

// reasonSuffix renders an unexpected shutdown reason for the error data, or nothing.
func reasonSuffix(reason string) string {
	if reason == "" {
		return ""
	}
	return fmt.Sprintf(" (Unity announced shutdown with reason=%q.)", reason)
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
	Result  json.RawMessage `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
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

// beginMCPRequest / endMCPRequest bracket one authenticated MCP HTTP request.
//
// They keep both idle-quit inputs honest: lastActivity marks the traffic itself (Unity messages
// alone used to be the only thing that counted, so a busy MCP client could not stop the bridge
// from quitting while Unity was down), and mcpClientCount keeps a call that runs longer than the
// grace period from being cut off mid-flight.
func (b *Bridge) beginMCPRequest() {
	b.mu.Lock()
	b.mcpClientCount++
	b.lastActivity = time.Now()
	b.mu.Unlock()
}

func (b *Bridge) endMCPRequest() {
	b.mu.Lock()
	if b.mcpClientCount > 0 {
		b.mcpClientCount--
	}
	b.lastActivity = time.Now()
	b.mu.Unlock()
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
	if rejectDisallowedOrigin(w, r) {
		return
	}
	if r.URL.Path != "/" && r.URL.Path != "/health" {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	fmt.Fprintf(w, `{"server":"UnityAgentBridge","status":"ok","calls_served":%d}`, b.callsServed.Load())
}

func (b *Bridge) handleMCP(w http.ResponseWriter, r *http.Request) {
	if rejectDisallowedOrigin(w, r) {
		return
	}

	// CORS for local tools
	applyCORSHeaders(w, r.Header.Get("Origin"))
	w.Header().Set("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Authorization, MCP-Protocol-Version, Mcp-Session-Id, Last-Event-ID")
	w.Header().Set("Access-Control-Expose-Headers", "MCP-Protocol-Version, Mcp-Session-Id")

	if r.Method == http.MethodOptions {
		w.WriteHeader(http.StatusNoContent)
		return
	}
	if !validProtocolVersionHeader(r.Header.Get(headerProtocolVersion)) {
		http.Error(w, "unsupported MCP protocol version", http.StatusBadRequest)
		return
	}
	w.Header().Set(headerProtocolVersion, effectiveProtocolVersionForHeader(r.Header.Get(headerProtocolVersion)))
	if r.Method == http.MethodGet {
		// Bridge P1 does not provide a standalone server-to-client SSE channel.
		// Streamable HTTP allows servers to signal that by returning 405.
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	if r.Method == http.MethodDelete {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	if r.Method != http.MethodPost {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	if warnPostAcceptHeader(r.Header.Get("Accept")) {
		log.Printf("[bridge] non-compliant POST Accept header; continuing for compatibility: %q", r.Header.Get("Accept"))
	}

	// Bearer auth — must match shared token.
	if !b.checkBearer(r.Header.Get("Authorization")) {
		// Per AgentMCPServer.cs convention: respond 200 with JSON-RPC error so Claude Code
		// does not try OAuth discovery. P1 uses static Bearer only.
		writeJSONRPCError(w, json.RawMessage("null"), -32001, "Unauthorized", "Missing or invalid Authorization header.")
		return
	}

	// From here on the caller is a legitimate MCP client, so its traffic counts as activity and
	// postpones the idle quit. Unauthenticated noise deliberately does not.
	b.beginMCPRequest()
	defer b.endMCPRequest()

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
	if req.JsonRPC != "2.0" {
		writeJSONRPCError(w, json.RawMessage("null"), -32600, "Invalid Request", "jsonrpc must be \"2.0\".")
		return
	}
	if !validJSONRPCID(req.ID) {
		writeJSONRPCError(w, json.RawMessage("null"), -32600, "Invalid Request", "id must be a string, number, or null.")
		return
	}
	if req.Method != "" && (len(req.Result) > 0 || req.Error != nil) {
		writeJSONRPCError(w, json.RawMessage("null"), -32600, "Invalid Request", "request must not include result or error.")
		return
	}

	if *verbose {
		log.Printf("[bridge] mcp ← %s id=%s", req.Method, string(req.ID))
	}

	if req.Method == "" {
		isResponse, validResponse := classifyJSONRPCResponse(req)
		if isResponse && validResponse {
			w.WriteHeader(http.StatusAccepted)
			return
		}
		if isResponse {
			writeJSONRPCError(w, json.RawMessage("null"), -32600, "Invalid Request", "JSON-RPC response must include exactly one of result or error.")
			return
		}
	}
	if req.Method == "" {
		writeJSONRPCError(w, json.RawMessage("null"), -32600, "Invalid Request", "Missing method.")
		return
	}

	// Notifications (no response expected).
	if len(req.ID) == 0 || string(req.ID) == "null" {
		w.WriteHeader(http.StatusAccepted)
		return
	}

	switch req.Method {
	case "initialize":
		protocolVersion := negotiateProtocolVersionFromParams(req.Params)
		w.Header().Set(headerProtocolVersion, protocolVersion)
		writeJSONRPCResult(w, req.ID, b.handleInitialize(protocolVersion))
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
		b.handleToolsCallContext(w, req, r.Context())
	default:
		writeJSONRPCError(w, req.ID, -32601, "Method not found", req.Method)
	}
}

func effectiveProtocolVersionForHeader(header string) string {
	header = strings.TrimSpace(header)
	if header == "" {
		return defaultProtocolVersionWhenHeaderMissing
	}
	return header
}

func negotiateProtocolVersion(requested string) string {
	if supportedProtocolVersion(requested) {
		return requested
	}
	return latestProtocolVersion
}

func negotiateProtocolVersionFromParams(rawParams json.RawMessage) string {
	if len(rawParams) == 0 {
		return latestProtocolVersion
	}
	var params struct {
		ProtocolVersion string `json:"protocolVersion"`
	}
	if err := json.Unmarshal(rawParams, &params); err != nil {
		return latestProtocolVersion
	}
	return negotiateProtocolVersion(params.ProtocolVersion)
}

func classifyJSONRPCResponse(req rpcRequest) (isResponse bool, valid bool) {
	hasResult := len(req.Result) > 0
	hasError := req.Error != nil
	if !hasResult && !hasError {
		return false, false
	}
	return true, len(req.ID) > 0 && hasResult != hasError
}

func supportedProtocolVersion(version string) bool {
	switch strings.TrimSpace(version) {
	case latestProtocolVersion, defaultProtocolVersionWhenHeaderMissing, "2024-11-05":
		return true
	default:
		return false
	}
}

func validProtocolVersionHeader(header string) bool {
	header = strings.TrimSpace(header)
	return header == "" || supportedProtocolVersion(header)
}

func acceptsJSONAndEventStream(header string) bool {
	return headerContainsMediaType(header, "application/json") &&
		headerContainsMediaType(header, "text/event-stream")
}

func warnPostAcceptHeader(header string) bool {
	return strings.TrimSpace(header) != "" && !acceptsJSONAndEventStream(header)
}

func rejectDisallowedOrigin(w http.ResponseWriter, r *http.Request) bool {
	if validOriginHeader(r.Header.Get("Origin")) {
		return false
	}
	http.Error(w, "forbidden origin", http.StatusForbidden)
	return true
}

func applyCORSHeaders(w http.ResponseWriter, originHeader string) {
	origin := strings.TrimSpace(originHeader)
	if origin == "" {
		origin = "*"
	} else {
		w.Header().Add("Vary", "Origin")
	}
	w.Header().Set("Access-Control-Allow-Origin", origin)
}

func validOriginHeader(header string) bool {
	origin := strings.TrimSpace(header)
	if origin == "" {
		return true
	}
	if strings.EqualFold(origin, "null") {
		return false
	}
	u, err := url.Parse(origin)
	if err != nil || u.Scheme == "" || u.Host == "" {
		return false
	}
	if u.User != nil || (u.Path != "" && u.Path != "/") || u.RawQuery != "" || u.Fragment != "" {
		return false
	}
	if !strings.EqualFold(u.Scheme, "http") && !strings.EqualFold(u.Scheme, "https") {
		return false
	}
	host := strings.ToLower(u.Hostname())
	if host == "localhost" {
		return true
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

func headerContainsMediaType(header, mediaType string) bool {
	for _, part := range strings.Split(header, ",") {
		item := strings.TrimSpace(part)
		if idx := strings.IndexByte(item, ';'); idx >= 0 {
			item = strings.TrimSpace(item[:idx])
		}
		if strings.EqualFold(item, mediaType) {
			return true
		}
	}
	return false
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
func (b *Bridge) handleInitialize(protocolVersion string) map[string]any {
	return map[string]any{
		"protocolVersion": protocolVersion,
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

// handleToolsList returns the same 4 top-level tools as the InProc server.
// In P1 we hardcode the schemas; P2 will fetch them from Unity at hello time.
//
// This list is a duplicate of Handlers.HandleToolsList on the C# side and the two drift silently:
// tools/call forwards any name straight through to Unity, so a tool missing here still WORKS if the
// client already knows its name — it is merely undiscoverable in Bridge mode. Anything added there
// has to be added here too.
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
			map[string]any{
				"name": "GetUnityAgentInfo",
				"description": "Report UnityAgent's own version, the Unity version and project, how many tools are " +
					"registered and reachable, which optional VRChat/avatar packages are installed, and how MCP is " +
					"wired up. Call once at session start: the tool surface differs between installs because optional " +
					"packages compile whole modules in or out, so a missing tool usually means a missing package.",
				"inputSchema": map[string]any{
					"type": "object",
					"properties": map[string]any{
						"detail": map[string]any{
							"type":        "string",
							"enum":        []any{"brief", "full"},
							"default":     "brief",
							"description": "'brief' (default) is a few lines. 'full' adds per-category and per-risk tool counts, package versions, MCP endpoint/bridge state and project render settings.",
						},
					},
					"required": []any{},
				},
			},
		},
	}
}

func validJSONRPCID(raw json.RawMessage) bool {
	raw = bytes.TrimSpace(raw)
	if len(raw) == 0 || bytes.Equal(raw, []byte("null")) {
		return true
	}
	var value any
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	if err := decoder.Decode(&value); err != nil {
		return false
	}
	if err := decoder.Decode(&struct{}{}); err != io.EOF {
		return false
	}
	switch value.(type) {
	case string, json.Number:
		return true
	default:
		return false
	}
}

func (b *Bridge) handleToolsCallContext(w http.ResponseWriter, req rpcRequest, ctx context.Context) {
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
		Context:  ctx,
	}
	b.mu.Lock()
	b.pending[call.ID] = call
	b.mu.Unlock()

	if err := b.dispatchToUnity(call); err != nil {
		if errors.Is(err, errCallCanceled) {
			b.cancelPendingCall(call)
			return
		}
		b.mu.Lock()
		delete(b.pending, call.ID)
		b.mu.Unlock()
		writeJSONRPCError(w, req.ID, -32603, "Dispatch failed", err.Error())
		return
	}

	timer := time.NewTimer(callTimeout)
	defer timer.Stop()

	select {
	case res := <-call.Response:
		writeToolCallResult(w, req.ID, res)
	case <-ctx.Done():
		if b.cancelPendingCall(call) {
			log.Printf("[bridge] canceled call id=%s tool=%s because MCP request ended", call.ID, call.Tool)
		}
		return
	case <-timer.C:
		if !b.cancelPendingCall(call) {
			// A result may have won the race with the timeout and removed the
			// call from pending just before the timer fired. Consume it instead
			// of reporting a false timeout.
			select {
			case res := <-call.Response:
				writeToolCallResult(w, req.ID, res)
			case <-ctx.Done():
			}
			return
		}
		writeJSONRPCError(w, req.ID, -32001, "Timeout",
			fmt.Sprintf("Tool '%s' did not complete within %s.", params.Name, callTimeout))
	}
}

func writeToolCallResult(w http.ResponseWriter, id json.RawMessage, res callResult) {
	if !res.OK {
		writeJSONRPCError(w, id, res.ErrCode, res.Error, res.Data)
		return
	}
	writeJSONRPCResult(w, id, map[string]any{
		"content": []any{map[string]any{"type": "text", "text": res.Text}},
		"isError": false,
	})
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

	grace := *idleQuitGrace
	if grace > 0 {
		log.Printf("[bridge] idle quit after %s with no Unity and no MCP activity", grace)
	} else {
		log.Printf("[bridge] idle quit disabled (--idle-quit=%s)", grace)
	}

	// Maintenance loop: expire queued calls nobody waits for any more, and — unless the idle quit
	// is disabled — shut down once Unity has been gone AND no MCP client has touched us for the
	// whole grace period. Both an in-flight MCP request and a completed one keep the bridge alive.
	go func() {
		ticker := time.NewTicker(maintenanceInterval)
		defer ticker.Stop()
		for range ticker.C {
			if dropped := b.pruneStaleQueuedCalls(time.Now()); dropped > 0 {
				log.Printf("[bridge] discarded %d stale queued call(s) while Unity was away", dropped)
			}
			if grace <= 0 {
				continue
			}
			b.mu.Lock()
			idleSince := time.Since(b.lastActivity)
			hasUnity := b.unityConn != nil
			inFlight := b.mcpClientCount
			b.mu.Unlock()
			if !hasUnity && inFlight == 0 && idleSince > grace {
				log.Printf("[bridge] idle for %s with no Unity and no MCP activity — exiting", idleSince)
				os.Exit(0)
			}
		}
	}()

	// Block forever
	select {}
}
