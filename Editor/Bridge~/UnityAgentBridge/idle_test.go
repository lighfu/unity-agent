package main

import (
	"net"
	"os"
	"os/exec"
	"testing"
	"time"
)

func TestIdleQuitChecksOwningEditorProcess(t *testing.T) {
	tests := []struct {
		name         string
		ownerPID     int
		ownerAlive   bool
		connected    bool
		inFlight     int
		idleFor      time.Duration
		grace        time.Duration
		wantQuit     bool
		wantPIDCheck bool
	}{
		{"long reload with live editor", 42, true, false, 0, 30 * time.Minute, 5 * time.Minute, false, true},
		{"dead editor after grace", 42, false, false, 0, 6 * time.Minute, 5 * time.Minute, true, true},
		{"legacy hello after grace", 0, false, false, 0, 6 * time.Minute, 5 * time.Minute, true, false},
		{"invalid owner after grace", -1, false, false, 0, 6 * time.Minute, 5 * time.Minute, true, false},
		{"recent disconnect", 42, false, false, 0, time.Minute, 5 * time.Minute, false, false},
		{"active Unity connection", 42, false, true, 0, 6 * time.Minute, 5 * time.Minute, false, false},
		{"active MCP request", 42, false, false, 1, 6 * time.Minute, 5 * time.Minute, false, false},
		{"idle quit disabled", 42, false, false, 0, 6 * time.Minute, 0, false, false},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			bridge := newBridge("secret")
			now := time.Now()
			bridge.lastActivity = now.Add(-test.idleFor)
			bridge.ownerUnityPID = test.ownerPID
			bridge.mcpClientCount = test.inFlight
			if test.connected {
				local, remote := net.Pipe()
				defer local.Close()
				defer remote.Close()
				bridge.unityConn = local
			}
			checkedPID := false
			quit := bridge.shouldQuitWhenIdle(now, test.grace, func(pid int) bool {
				checkedPID = true
				if pid != test.ownerPID {
					t.Fatalf("checked PID %d, want %d", pid, test.ownerPID)
				}
				// The OS query must not hold the transport lock.
				if !bridge.mu.TryLock() {
					t.Fatal("process liveness checked while bridge lock held")
				}
				bridge.mu.Unlock()
				return test.ownerAlive
			})
			if quit != test.wantQuit || checkedPID != test.wantPIDCheck {
				t.Fatalf("quit=%v PID check=%v, want quit=%v PID check=%v", quit, checkedPID, test.wantQuit, test.wantPIDCheck)
			}
		})
	}
}

func TestIdleQuitRechecksTrafficDuringOwnerProbe(t *testing.T) {
	for _, change := range []string{"owner", "connection", "request", "activity"} {
		t.Run(change, func(t *testing.T) {
			bridge := newBridge("secret")
			now := time.Now()
			bridge.lastActivity = now.Add(-30 * time.Minute)
			bridge.ownerUnityPID = 42
			quit := bridge.shouldQuitWhenIdle(now, defaultIdleQuitGrace, func(pid int) bool {
				bridge.mu.Lock()
				defer bridge.mu.Unlock()
				switch change {
				case "owner":
					bridge.ownerUnityPID = 43
				case "connection":
					local, remote := net.Pipe()
					t.Cleanup(func() { _ = local.Close(); _ = remote.Close() })
					bridge.unityConn = local
				case "request":
					bridge.mcpClientCount = 1
				case "activity":
					bridge.lastActivity = now
				}
				return false
			})
			if quit {
				t.Fatalf("bridge quit after %s changed during owner process probe", change)
			}
		})
	}
}

func waitForUnityOwner(t *testing.T, bridge *Bridge, pid int, connected bool) {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		bridge.mu.Lock()
		matched := bridge.ownerUnityPID == pid && (bridge.unityConn != nil) == connected
		bridge.mu.Unlock()
		if matched {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("Unity owner/connection did not become pid=%d connected=%v", pid, connected)
}

func TestAuthenticatedHelloKeepsEndpointAliveDuringLongReload(t *testing.T) {
	bridge := newBridge("secret")
	unity, _ := startFakeUnityWithHello(t, bridge,
		wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: os.Getpid()})
	waitForUnityOwner(t, bridge, os.Getpid(), true)
	_ = unity.Close()
	waitForUnityOwner(t, bridge, os.Getpid(), false)
	bridge.mu.Lock()
	bridge.lastActivity = time.Now().Add(-30 * time.Minute)
	bridge.mu.Unlock()
	if bridge.shouldQuitWhenIdle(time.Now(), defaultIdleQuitGrace, processAlive) {
		t.Fatal("live owning editor lost its HTTP endpoint during a long reload")
	}
}

func TestLegacyHelloReplacesPreviousUnityOwner(t *testing.T) {
	bridge := newBridge("secret")
	_, _ = startFakeUnityWithHello(t, bridge,
		wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: os.Getpid()})
	waitForUnityOwner(t, bridge, os.Getpid(), true)
	_, _ = startFakeUnity(t, bridge)
	waitForUnityOwner(t, bridge, 0, true)
}

func TestProcessAliveRecognizesCurrentAndExitedProcess(t *testing.T) {
	if !processAlive(os.Getpid()) {
		t.Fatal("current process was reported dead")
	}
	if processAlive(0) || processAlive(-1) {
		t.Fatal("invalid PID was reported alive")
	}
	child := exec.Command(os.Args[0], "-test.run=^TestProcessAliveChild$")
	if err := child.Start(); err != nil {
		t.Fatalf("start child: %v", err)
	}
	pid := child.Process.Pid
	if err := child.Wait(); err != nil {
		t.Fatalf("wait child: %v", err)
	}
	if processAlive(pid) {
		t.Fatal("exited child process was reported alive")
	}
}

func TestProcessAliveChild(t *testing.T) {}
