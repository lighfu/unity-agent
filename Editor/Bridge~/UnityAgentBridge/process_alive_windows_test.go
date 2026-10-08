//go:build windows

package main

import (
	"os"
	"os/exec"
	"testing"
)

func TestProcessAliveKeepsHandleSoExitedPIDCannotBeReused(t *testing.T) {
	child := exec.Command(os.Args[0], "-test.run=^TestOwnerExitProcessHelper$")
	child.Env = append(os.Environ(), "UNITY_AGENT_OWNER_TEST=owner")
	input, err := child.StdinPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err := child.Start(); err != nil {
		t.Fatalf("start child: %v", err)
	}
	pid := child.Process.Pid
	if !processAlive(pid) {
		_ = child.Process.Kill()
		_ = child.Wait()
		t.Fatal("running child was reported dead")
	}
	_ = input.Close()
	if err := child.Wait(); err != nil {
		t.Fatalf("wait child: %v", err)
	}

	// exec has closed its own handle; ours must still pin the PID.
	processHandlesMu.Lock()
	_, held := processHandles[pid]
	processHandlesMu.Unlock()
	if !held {
		t.Fatal("handle was not retained after the first check")
	}
	for i := 0; i < 3; i++ {
		if processAlive(pid) {
			t.Fatal("exited child was reported alive")
		}
	}
}
