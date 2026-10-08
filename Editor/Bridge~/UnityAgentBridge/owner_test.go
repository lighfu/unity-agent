package main

import (
	"bufio"
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

	// Reserve distinct ephemeral ports until both have been chosen.
	public, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer public.Close()
	internal, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer internal.Close()
	publicAddr := public.Addr().String()
	bridge := exec.Command(os.Args[0], "-test.run=^TestOwnerExitProcessHelper$", "--",
		"--token=secret", "--idle-quit=0", fmt.Sprintf("--unity-pid=%d", owner.Process.Pid),
		fmt.Sprintf("--public-port=%d", public.Addr().(*net.TCPAddr).Port),
		fmt.Sprintf("--internal-port=%d", internal.Addr().(*net.TCPAddr).Port))
	bridge.Env = append(os.Environ(), "UNITY_AGENT_OWNER_TEST=bridge")
	stderr, err := bridge.StderrPipe()
	if err != nil {
		t.Fatal(err)
	}
	_ = public.Close()
	_ = internal.Close()
	if err := bridge.Start(); err != nil {
		t.Fatalf("start bridge: %v", err)
	}
	exited := make(chan struct{})
	var exitErr error
	go func() { exitErr = bridge.Wait(); close(exited) }()
	t.Cleanup(func() { _ = bridge.Process.Kill(); <-exited })
	queued := make(chan struct{})
	go func() {
		scanner := bufio.NewScanner(stderr)
		for scanner.Scan() {
			line := scanner.Text()
			if strings.Contains(line, "queueing call") {
				close(queued)
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
				break
			}
		}
		select {
		case <-exited:
			t.Fatalf("bridge exited before listening: %v", exitErr)
		default:
		}
		if time.Now().After(deadline) {
			t.Fatal("bridge did not start listening")
		}
		time.Sleep(10 * time.Millisecond)
	}
	client.Timeout = 10 * time.Second
	request, err := http.NewRequest(http.MethodPost, "http://"+publicAddr+"/mcp",
		strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"PendingTool","arguments":{}}}`))
	if err != nil {
		t.Fatal(err)
	}
	request.Header.Set("Authorization", "Bearer secret")
	request.Header.Set("Accept", "application/json, text/event-stream")
	request.Header.Set("Content-Type", "application/json")
	requestDone := make(chan struct{})
	go func() {
		if response, err := client.Do(request); err == nil {
			response.Body.Close()
		}
		close(requestDone)
	}()
	select {
	case <-queued:
	case <-requestDone:
		t.Fatal("MCP request finished before being queued")
	case <-time.After(5 * time.Second):
		t.Fatal("MCP request was not queued")
	}
	_ = ownerInput.Close()
	if err := owner.Wait(); err != nil {
		t.Fatalf("owner exit: %v", err)
	}
	select {
	case <-exited:
		if exitErr != nil {
			t.Fatalf("bridge exit: %v", exitErr)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("bridge stayed alive after its owner exited with idle quit disabled and an active MCP request")
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
