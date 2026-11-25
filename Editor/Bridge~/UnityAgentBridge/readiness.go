package main

import (
	"encoding/json"
	"net"
	"net/http"
	"time"
)

const (
	unityConnectionStatusTool = "GetUnityConnectionStatus"
	unityStatusMaxAge         = 3 * time.Second
)

// A unity_status frame reports what the editor knows independently of the tool queue.
type editorStatus struct {
	Status          string
	MainThreadAgeMS *int
	PlayMode        string
	Reason          string
}

type unityConnectionStatus struct {
	BridgeAlive      bool   `json:"bridgeAlive"`
	UnityConnected   bool   `json:"unityConnected"`
	Readiness        string `json:"readiness"`
	Reason           string `json:"reason,omitempty"`
	DisconnectReason string `json:"disconnectReason,omitempty"`
	StatusAgeMS      *int64 `json:"statusAgeMs,omitempty"`
	MainThreadAgeMS  *int   `json:"mainThreadAgeMs,omitempty"`
	PlayMode         string `json:"playMode,omitempty"`
	NextAction       string `json:"nextAction,omitempty"`
}

func (b *Bridge) noteUnityStatus(conn net.Conn, status editorStatus) {
	switch status.Status {
	case "ready", "starting", "compiling", "importing", "play_mode_transition", "dialog_wait", "busy", "unknown":
	default:
		return
	}
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.unityConn == conn {
		b.unityStatus = status
		b.unityStatusAt = time.Now()
	}
}

func (b *Bridge) currentUnityConnectionStatus() unityConnectionStatus {
	b.mu.Lock()
	defer b.mu.Unlock()
	status := unityConnectionStatus{BridgeAlive: true, UnityConnected: b.unityConn != nil}
	if !status.UnityConnected {
		status.DisconnectReason = b.unityDisconnectReason
		switch b.unityDisconnectReason {
		case "":
			status.Readiness = "waiting_for_connection"
		case "domain_reload", "transport_closed":
			status.Readiness = "reconnecting"
		default:
			status.Readiness = "disconnected"
		}
		return status
	}

	if b.unityStatusAt.IsZero() {
		status.Readiness = "unknown"
		status.Reason = "Awaiting a Unity status report. Older editors may only support GetEditorState."
		return status
	}
	age := time.Since(b.unityStatusAt)
	ageMS := age.Milliseconds()
	status.StatusAgeMS = &ageMS
	if age >= unityStatusMaxAge {
		status.Readiness = "unknown"
		status.Reason = "Unity has stopped publishing current status."
		return status
	}
	status.Readiness = b.unityStatus.Status
	status.Reason = b.unityStatus.Reason
	status.MainThreadAgeMS = b.unityStatus.MainThreadAgeMS
	status.PlayMode = b.unityStatus.PlayMode
	if status.Readiness == "dialog_wait" {
		status.NextAction = "Use ExecuteUnityTool(GetEditorState) and ExecuteUnityTool(AnswerModalDialog, arguments={dryRun:true}) to inspect the dialog before choosing a button."
	}
	return status
}

func (b *Bridge) writeUnityConnectionStatus(w http.ResponseWriter, id json.RawMessage) {
	status, err := json.Marshal(b.currentUnityConnectionStatus())
	if err != nil {
		writeJSONRPCError(w, id, -32603, "Could not encode Unity connection status", err.Error())
		return
	}
	writeToolCallResult(w, id, callResult{OK: true, Text: string(status)}, unityConnectionStatusTool)
}
