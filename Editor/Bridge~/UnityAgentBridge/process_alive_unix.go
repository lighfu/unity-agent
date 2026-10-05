//go:build darwin || linux

package main

import (
	"errors"
	"syscall"
)

func processAlive(pid int) bool {
	if pid <= 0 {
		return false
	}
	// Signal 0 checks existence without delivering a signal. EPERM means the process
	// exists but belongs to another user, so only ESRCH proves the owner has exited.
	err := syscall.Kill(pid, 0)
	return !errors.Is(err, syscall.ESRCH)
}
