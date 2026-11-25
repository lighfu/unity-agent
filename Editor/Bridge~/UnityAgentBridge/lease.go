package main

import (
	"bytes"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/http"
	"strings"
	"time"
)

const (
	unityLeaseHeader        = "X-UnityAgent-Lease"
	unityLeaseMetaKey       = "unityagent/leaseToken"
	acquireUnityLeaseTool   = "AcquireUnityLease"
	getUnityLeaseStatusTool = "GetUnityLeaseStatus"
	renewUnityLeaseTool     = "RenewUnityLease"
	releaseUnityLeaseTool   = "ReleaseUnityLease"
	defaultLeaseTTL         = 300
	minLeaseTTL             = 10
	maxLeaseTTL             = 3600
	errCodeLeaseConflict    = -32004
)

var errConflictingLeaseTokens = errors.New("conflicting Unity lease credentials")

// The lease belongs to the Bridge process and is independent of the Unity connection.
// Fields are guarded by Bridge.mu; call records use the pointer only as a generation ID.
type unityLease struct {
	owner      string
	purpose    string
	token      string
	editorPID  int
	acquiredAt time.Time
	expiresAt  time.Time
	draining   bool
	timer      *time.Timer
}

type unityLeaseStatus struct {
	State         string `json:"state"`
	Owner         string `json:"owner,omitempty"`
	Purpose       string `json:"purpose,omitempty"`
	AcquiredAt    string `json:"acquiredAt,omitempty"`
	ExpiresAt     string `json:"expiresAt,omitempty"`
	InFlightCalls int    `json:"inFlightCalls"`
	QueuedCalls   int    `json:"queuedCalls"`
	LeaseToken    string `json:"leaseToken,omitempty"`
}

func isUnityLeaseTool(name string) bool {
	switch name {
	case acquireUnityLeaseTool, getUnityLeaseStatusTool, renewUnityLeaseTool, releaseUnityLeaseTool:
		return true
	default:
		return false
	}
}

func unityLeaseTools() []any {
	token := map[string]any{"type": "string", "description": "Lease credential from AcquireUnityLease. The X-UnityAgent-Lease header can be used instead."}
	ttl := map[string]any{"type": "integer", "default": defaultLeaseTTL, "minimum": minLeaseTTL, "maximum": maxLeaseTTL}
	tool := func(name, description string, properties map[string]any, required ...string) map[string]any {
		schema := map[string]any{"type": "object", "properties": properties, "additionalProperties": false}
		if len(required) != 0 {
			schema["required"] = required
		}
		return map[string]any{"name": name, "description": description, "inputSchema": schema}
	}
	return []any{
		tool(acquireUnityLeaseTool, "Reserve all Bridge-forwarded Unity calls for one workflow. Save the returned leaseToken privately and pass it with subsequent Unity calls.",
			map[string]any{"owner": map[string]any{"type": "string", "maxLength": 128}, "purpose": map[string]any{"type": "string", "maxLength": 512}, "ttlSeconds": ttl}, "owner", "purpose"),
		tool(getUnityLeaseStatusTool, "Report the current reservation, its owner and purpose, expiry, and outstanding Unity calls without exposing its credential. Works while Unity is disconnected.", map[string]any{}),
		tool(renewUnityLeaseTool, "Set your active reservation to expire ttlSeconds from now.", map[string]any{"leaseToken": token, "ttlSeconds": ttl}),
		tool(releaseUnityLeaseTool, "End your reservation. Queued calls are discarded and already dispatched Unity work drains before other clients can run.", map[string]any{"leaseToken": token}),
	}
}

func (b *Bridge) leaseStatusLocked() unityLeaseStatus {
	status := unityLeaseStatus{State: "free", InFlightCalls: len(b.abandonedCalls)}
	for _, call := range b.pending {
		if call.DispatchedOn == nil {
			status.QueuedCalls++
		} else {
			status.InFlightCalls++
		}
	}
	if lease := b.lease; lease != nil {
		status.State = "held"
		if lease.draining {
			status.State = "draining"
		}
		status.Owner = lease.owner
		status.Purpose = lease.purpose
		status.AcquiredAt = lease.acquiredAt.UTC().Format(time.RFC3339Nano)
		status.ExpiresAt = lease.expiresAt.UTC().Format(time.RFC3339Nano)
	}
	return status
}

func (b *Bridge) leaseConflictLocked(reason string) callResult {
	status := b.leaseStatusLocked()
	return callResult{ErrCode: errCodeLeaseConflict, Error: reason, LeaseStatus: &status}
}

func (b *Bridge) finishLeaseDrainLocked() {
	if b.lease != nil && b.lease.draining && len(b.pending) == 0 && len(b.abandonedCalls) == 0 {
		b.lease = nil
	}
}

func (b *Bridge) drainLeaseLocked(lease *unityLease, reason string) {
	if lease.draining {
		return
	}
	lease.draining = true
	lease.timer.Stop()

	// Reconnecting may already have pulled requests out of queueWhenDown. Check
	// pending as well so a release racing the reconnect cannot send a stale call.
	var dropped []*pendingCall
	for id, call := range b.pending {
		if call.Lease == lease && call.DispatchedOn == nil {
			delete(b.pending, id)
			dropped = append(dropped, call)
		}
	}
	kept := b.queueWhenDown[:0]
	for _, call := range b.queueWhenDown {
		if b.pending[call.ID] == call {
			kept = append(kept, call)
		}
	}
	clear(b.queueWhenDown[len(kept):])
	b.queueWhenDown = kept
	b.finishLeaseDrainLocked()
	for _, call := range dropped {
		call.Response <- b.leaseConflictLocked(reason)
	}
}

func (b *Bridge) expireLeaseLocked(now time.Time) {
	if lease := b.lease; lease != nil && !lease.draining && !now.Before(lease.expiresAt) {
		b.drainLeaseLocked(lease, "Unity reservation expired before this call was dispatched.")
	}
}

func (b *Bridge) scheduleLeaseExpiryLocked(lease *unityLease) {
	if lease.timer != nil {
		lease.timer.Stop()
	}
	lease.timer = time.AfterFunc(time.Until(lease.expiresAt), func() {
		b.mu.Lock()
		defer b.mu.Unlock()
		if b.lease == lease {
			b.expireLeaseLocked(time.Now())
		}
	})
}

func (b *Bridge) admitUnityCall(call *pendingCall, token string) *callResult {
	b.mu.Lock()
	defer b.mu.Unlock()
	b.expireLeaseLocked(time.Now())
	switch lease := b.lease; {
	case lease == nil && token != "":
		denial := b.leaseConflictLocked("Unity reservation credential is no longer active.")
		return &denial
	case lease != nil && lease.draining:
		denial := b.leaseConflictLocked("Unity reservation is waiting for dispatched calls to finish.")
		return &denial
	case lease != nil && !constantTimeEq(lease.token, token):
		denial := b.leaseConflictLocked("Unity is reserved by another workflow.")
		return &denial
	case lease != nil:
		call.Lease = lease
	}
	b.pending[call.ID] = call
	return nil
}

type unityLeaseArgs struct {
	Owner      string `json:"owner"`
	Purpose    string `json:"purpose"`
	TTLSeconds *int   `json:"ttlSeconds"`
	LeaseToken string `json:"leaseToken"`
}

func (b *Bridge) handleUnityLeaseTool(w http.ResponseWriter, id json.RawMessage, name string, rawArgs json.RawMessage, token string) {
	var args unityLeaseArgs
	if len(rawArgs) != 0 {
		decoder := json.NewDecoder(bytes.NewReader(rawArgs))
		decoder.DisallowUnknownFields()
		if err := decoder.Decode(&args); err != nil {
			writeJSONRPCError(w, id, -32602, "Invalid Unity lease arguments", err.Error())
			return
		}
	}

	switch name {
	case acquireUnityLeaseTool:
		args.Owner = strings.TrimSpace(args.Owner)
		args.Purpose = strings.TrimSpace(args.Purpose)
		if args.Owner == "" || args.Purpose == "" || len([]rune(args.Owner)) > 128 || len([]rune(args.Purpose)) > 512 || args.LeaseToken != "" {
			writeJSONRPCError(w, id, -32602, "Invalid Unity lease arguments", "Supply owner (1–128 characters), purpose (1–512 characters), and optional ttlSeconds.")
			return
		}
	case renewUnityLeaseTool:
		if args.Owner != "" || args.Purpose != "" {
			writeJSONRPCError(w, id, -32602, "Invalid Unity lease arguments", "Renew accepts leaseToken and ttlSeconds.")
			return
		}
	case releaseUnityLeaseTool:
		if args.Owner != "" || args.Purpose != "" || args.TTLSeconds != nil {
			writeJSONRPCError(w, id, -32602, "Invalid Unity lease arguments", "Release accepts leaseToken.")
			return
		}
	case getUnityLeaseStatusTool:
		if args.Owner != "" || args.Purpose != "" || args.TTLSeconds != nil || args.LeaseToken != "" {
			writeJSONRPCError(w, id, -32602, "Invalid Unity lease arguments", "Lease status takes no arguments.")
			return
		}
	}
	ttl := defaultLeaseTTL
	if args.TTLSeconds != nil {
		ttl = *args.TTLSeconds
	}
	if ttl < minLeaseTTL || ttl > maxLeaseTTL {
		writeJSONRPCError(w, id, -32602, "Invalid Unity lease duration", "ttlSeconds must be between 10 and 3600.")
		return
	}
	token, err := combineLeaseTokens(token, args.LeaseToken)
	if err != nil {
		writeJSONRPCError(w, id, -32602, "Invalid Unity lease credentials", err.Error())
		return
	}

	status, failure := b.changeUnityLease(name, args, token, time.Duration(ttl)*time.Second)
	if failure != nil {
		writeToolCallResult(w, id, *failure, name)
		return
	}
	text, _ := json.Marshal(status)
	w.Header().Set("Cache-Control", "no-store")
	writeJSONRPCResult(w, id, map[string]any{
		"content":           []any{map[string]any{"type": "text", "text": string(text)}},
		"isError":           false,
		"structuredContent": status,
	})
}

func (b *Bridge) changeUnityLease(name string, args unityLeaseArgs, token string, ttl time.Duration) (unityLeaseStatus, *callResult) {
	b.mu.Lock()
	defer b.mu.Unlock()
	now := time.Now()
	b.expireLeaseLocked(now)

	switch name {
	case acquireUnityLeaseTool:
		if b.lease != nil || len(b.pending) != 0 || len(b.abandonedCalls) != 0 {
			denial := b.leaseConflictLocked("Unity has a reservation or outstanding calls. Wait until the Editor is available.")
			return unityLeaseStatus{}, &denial
		}
		var secret [32]byte
		if _, err := rand.Read(secret[:]); err != nil {
			return unityLeaseStatus{}, &callResult{ErrCode: -32603, Error: "Could not create a Unity lease credential."}
		}
		lease := &unityLease{
			owner: args.Owner, purpose: args.Purpose, token: base64.RawURLEncoding.EncodeToString(secret[:]),
			editorPID: b.ownerUnityPID, acquiredAt: now, expiresAt: now.Add(ttl),
		}
		b.lease = lease
		b.scheduleLeaseExpiryLocked(lease)
		status := b.leaseStatusLocked()
		status.LeaseToken = lease.token
		return status, nil
	case renewUnityLeaseTool, releaseUnityLeaseTool:
		lease := b.lease
		if lease == nil || lease.draining || !constantTimeEq(lease.token, token) {
			denial := b.leaseConflictLocked("A current Unity reservation credential is required.")
			return unityLeaseStatus{}, &denial
		}
		if name == renewUnityLeaseTool {
			lease.expiresAt = now.Add(ttl)
			b.scheduleLeaseExpiryLocked(lease)
		} else {
			b.drainLeaseLocked(lease, "Unity reservation was released before this call was dispatched.")
		}
	}
	return b.leaseStatusLocked(), nil
}

func combineLeaseTokens(values ...string) (string, error) {
	var token string
	for _, value := range values {
		if value == "" {
			continue
		}
		if token != "" && !constantTimeEq(token, value) {
			return "", errConflictingLeaseTokens
		}
		token = value
	}
	return token, nil
}

// ExecuteUnityTool's envelope can carry a lease credential for clients that can
// only set schema arguments. Remove it before Unity can read or log that envelope.
func takeExecuteLeaseToken(rawArgs json.RawMessage) (json.RawMessage, string, error) {
	var fields map[string]json.RawMessage
	if json.Unmarshal(rawArgs, &fields) != nil || fields["leaseToken"] == nil {
		return rawArgs, "", nil
	}
	var token string
	if err := json.Unmarshal(fields["leaseToken"], &token); err != nil || token == "" {
		return nil, "", errors.New("leaseToken must be a non-empty string")
	}
	delete(fields, "leaseToken")
	args, err := json.Marshal(fields)
	return args, token, err
}
