//go:build windows

package main

import (
	"errors"
	"syscall"
)

// A process handle becomes signaled when that process exits. SYNCHRONIZE is sufficient:
// no executable details or elevated process-query permissions are needed.
func processAlive(pid int) bool {
	if pid <= 0 || uint64(pid) > uint64(^uint32(0)) {
		return false
	}
	handle, err := syscall.OpenProcess(syscall.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		// Access denied still means a process exists; do not drop the endpoint just because
		// the editor's process permissions prevent inspection.
		return errors.Is(err, syscall.ERROR_ACCESS_DENIED)
	}
	defer syscall.CloseHandle(handle)
	state, err := syscall.WaitForSingleObject(handle, 0)
	return err != nil || state != syscall.WAIT_OBJECT_0
}
