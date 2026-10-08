package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"os/exec"
	"strings"
	"testing"
	"time"
)

func TestOwnerExitIgnoresConnectionAndMCPActivity(t *testing.T) {
	for _, test := range []struct {
		name      string
		pid       int
		alive     bool
		connected bool
		inFlight  int
		wantQuit  bool
	}{
		{"live editor during reload", 42, true, false, 0, false},
		{"live connected editor", 42, true, true, 1, false},
		{"dead editor while idle", 42, false, false, 0, true},
		{"dead editor with active MCP and Unity socket", 42, false, true, 1, true},
		{"unknown owner", 0, false, false, 0, false},
		{"invalid owner", -1, false, false, 0, false},
	} {
		t.Run(test.name, func(t *testing.T) {
			bridge := newBridge("secret")
			bridge.ownerUnityPID = test.pid
			bridge.exitUnityPID = test.pid
			bridge.mcpClientCount = test.inFlight
			if test.connected {
				local, remote := net.Pipe()
				defer local.Close()
				defer remote.Close()
				bridge.unityConn = local
			}
			checked := false
			quit := bridge.shouldQuitWhenOwnerExited(func(pid int) bool {
				checked = true
				if pid != test.pid {
					t.Fatalf("checked PID %d, want %d", pid, test.pid)
				}
				if !bridge.mu.TryLock() {
					t.Fatal("process liveness checked while bridge lock held")
				}
				bridge.mu.Unlock()
				return test.alive
			})
			if quit != test.wantQuit || checked != (test.pid > 0) {
				t.Fatalf("quit=%v checked=%v, want quit=%v checked=%v", quit, checked, test.wantQuit, test.pid > 0)
			}
		})
	}
}

func TestOwnerExitRechecksOwnerDuringProbe(t *testing.T) {
	for _, test := range []struct {
		name     string
		exitPID  int
		wantQuit bool
	}{
		{"replacement owner", 43, false},
		{"cleared lifetime owner", 0, false},
		{"released idle owner with new MCP activity", 42, true},
	} {
		t.Run(test.name, func(t *testing.T) {
			bridge := newBridge("secret")
			bridge.ownerUnityPID = 42
			bridge.exitUnityPID = 42
			quit := bridge.shouldQuitWhenOwnerExited(func(int) bool {
				bridge.mu.Lock()
				bridge.ownerUnityPID = 0
				bridge.exitUnityPID = test.exitPID
				bridge.mcpClientCount++
				bridge.lastActivity = time.Now()
				bridge.mu.Unlock()
				return false
			})
			if quit != test.wantQuit {
				t.Fatalf("quit=%v, want %v", quit, test.wantQuit)
			}
		})
	}
}

func TestShutdownKeepsLifetimeOwnerUntilProcessExit(t *testing.T) {
	for _, test := range []struct {
		reason    string
		wantOwner int
	}{
		{"editor_quit", os.Getpid()},
		{"user_disabled", 0},
	} {
		t.Run(test.reason, func(t *testing.T) {
			bridge := newBridge("secret")
			unity, _ := startFakeUnityWithHello(t, bridge,
				wireMsg{Type: "hello", Version: "test", Token: "secret", UnityPID: os.Getpid()})
			waitForUnityOwner(t, bridge, os.Getpid(), true)
			if _, err := unity.Write([]byte(`{"type":"shutdown","reason":"` + test.reason + `"}` + "\n")); err != nil {
				t.Fatalf("write shutdown: %v", err)
			}
			_ = unity.Close()
			waitForUnityOwner(t, bridge, test.wantOwner, false)
			if !bridge.shouldQuitWhenOwnerExited(func(pid int) bool {
				if pid != os.Getpid() {
					t.Fatalf("lifetime PID=%d, want %d", pid, os.Getpid())
				}
				return false
			}) {
				t.Fatal("shutdown discarded the owner needed to detect its process exit")
			}
		})
	}
}

func TestStartupUnityPIDPrefersFlagThenEnvironment(t *testing.T) {
	for _, test := range []struct {
		name    string
		flagPID int
		envPID  string
		want    int
	}{
		{"flag only", 42, "", 42},
		{"flag wins over environment", 42, "43", 42},
		{"environment from auto-spawn", 0, "43", 43},
		{"environment with whitespace", 0, " 43 ", 43},
		{"neither", 0, "", 0},
		{"malformed environment", 0, "unity", 0},
		{"non-positive environment", 0, "-1", 0},
	} {
		t.Run(test.name, func(t *testing.T) {
			if got := startupUnityPID(test.flagPID, test.envPID); got != test.want {
				t.Fatalf("startupUnityPID(%d, %q)=%d, want %d", test.flagPID, test.envPID, got, test.want)
			}
		})
	}
}

func TestBridgeExitsAfterStartupOwnerDiesWithActiveMCP(t *testing.T) {
	owner := exec.Command(os.Args[0], "-test.run=^TestOwnerExitProcessHelper$")
	owner.Env = append(os.Environ(), "UNITY_AGENT_OWNER_TEST=owner")
	ownerInput, err := owner.StdinPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err := owner.Start(); err != nil {
		t.Fatalf("start owner: %v", err)
	}
	t.Cleanup(func() {
		_ = ownerInput.Close()
		_ = owner.Process.Kill()
		if owner.ProcessState == nil {
			_ = owner.Wait()
		}
	})

	bridge := startBridgeProcess(t, owner.Process.Pid)
	client := &http.Client{Timeout: 10 * time.Second}
	defer client.CloseIdleConnections()
	request, err := http.NewRequest(http.MethodPost, "http://"+bridge.publicAddr+"/mcp",
		strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"PendingTool","arguments":{}}}`))
	if err != nil {
		t.Fatal(err)
	}
	request.Header.Set("Authorization", "Bearer secret")
	request.Header.Set("Accept", "application/json, text/event-stream")
	request.Header.Set("Content-Type", "application/json")
	type reply struct {
		body []byte
		err  error
	}
	replied := make(chan reply, 1)
	go func() {
		response, err := client.Do(request)
		if err != nil {
			replied <- reply{err: err}
			return
		}
		defer response.Body.Close()
		body, err := io.ReadAll(response.Body)
		replied <- reply{body, err}
	}()
	select {
	case <-bridge.queued:
	case got := <-replied:
		t.Fatalf("MCP request finished before being queued: %s %v", got.body, got.err)
	case <-time.After(5 * time.Second):
		t.Fatal("MCP request was not queued")
	}
	_ = ownerInput.Close()
	if err := owner.Wait(); err != nil {
		t.Fatalf("owner exit: %v", err)
	}
	select {
	case got := <-replied:
		if got.err != nil {
			t.Fatalf("queued MCP request ended with a transport error instead of a JSON-RPC answer: %v", got.err)
		}
		var answer rpcResponse
		if err := json.Unmarshal(got.body, &answer); err != nil {
			t.Fatalf("decode answer %q: %v", got.body, err)
		}
		if answer.Error == nil || answer.Error.Code != errCodeInterrupted ||
			answer.Error.Message != "Unity exited before this call could run" {
			t.Fatalf("answer=%s, want interrupted error for a call that never ran", got.body)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("queued MCP request was not answered after its owner exited")
	}
	select {
	case <-bridge.exited:
		if bridge.exitErr != nil {
			t.Fatalf("bridge exit: %v", bridge.exitErr)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("bridge stayed alive after its owner exited with idle quit disabled and an active MCP request")
	}
}

type bridgeProcess struct {
	publicAddr string
	exited     chan struct{}
	exitErr    error         // valid once exited is closed
	queued     chan struct{} // closed when the bridge logs that it queued a call
}

// startBridgeProcess runs main() in a child test process with idle quit disabled. Ports
// are reserved by listening and then released for the child, so another process can take
// one in between; a child that exits before serving is retried on fresh ports.
func startBridgeProcess(t *testing.T, ownerPID int) *bridgeProcess {
	t.Helper()
	const attempts = 3
	for attempt := 1; ; attempt++ {
		bridge, err := tryStartBridgeProcess(t, ownerPID)
		if err == nil {
			return bridge
		}
		if attempt == attempts {
			t.Fatalf("bridge did not start after %d attempts: %v", attempts, err)
		}
		t.Logf("bridge start attempt %d failed, retrying on new ports: %v", attempt, err)
	}
}

func tryStartBridgeProcess(t *testing.T, ownerPID int) (*bridgeProcess, error) {
	public, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	internal, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		_ = public.Close()
		t.Fatal(err)
	}
	publicAddr := public.Addr().String()
	command := exec.Command(os.Args[0], "-test.run=^TestOwnerExitProcessHelper$", "--",
		"--token=secret", "--idle-quit=0",
		fmt.Sprintf("--public-port=%d", public.Addr().(*net.TCPAddr).Port),
		fmt.Sprintf("--internal-port=%d", internal.Addr().(*net.TCPAddr).Port))
	_ = public.Close()
	_ = internal.Close()
	// Same channel as Unity's auto-spawn, which avoids a flag that stale binaries reject.
	command.Env = append(os.Environ(), "UNITY_AGENT_OWNER_TEST=bridge",
		fmt.Sprintf("%s=%d", unityPIDEnv, ownerPID))
	stderr, err := command.StderrPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err := command.Start(); err != nil {
		t.Fatalf("start bridge: %v", err)
	}
	bridge := &bridgeProcess{publicAddr: publicAddr, exited: make(chan struct{}), queued: make(chan struct{})}
	go func() { bridge.exitErr = command.Wait(); close(bridge.exited) }()
	t.Cleanup(func() { _ = command.Process.Kill(); <-bridge.exited })
	go func() {
		scanner := bufio.NewScanner(stderr)
		queued := false
		for scanner.Scan() {
			if !queued && strings.Contains(scanner.Text(), "queueing call") {
				queued = true
				close(bridge.queued)
			}
		}
	}()

	client := &http.Client{Timeout: 200 * time.Millisecond}
	defer client.CloseIdleConnections()
	deadline := time.Now().Add(20 * time.Second)
	for {
		if response, err := client.Get("http://" + publicAddr + "/health"); err == nil {
			response.Body.Close()
			if response.StatusCode == http.StatusOK {
				return bridge, nil
			}
		}
		select {
		case <-bridge.exited:
			return nil, fmt.Errorf("bridge exited before listening: %v", bridge.exitErr)
		default:
		}
		if time.Now().After(deadline) {
			t.Fatal("bridge did not start listening")
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func TestOwnerExitProcessHelper(t *testing.T) {
	switch os.Getenv("UNITY_AGENT_OWNER_TEST") {
	case "owner":
		_, _ = io.Copy(io.Discard, os.Stdin)
		os.Exit(0)
	case "bridge":
		os.Args = append(os.Args[:1], flag.Args()...)
		main()
		os.Exit(0)
	}
}
