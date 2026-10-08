//go:build windows

package main

import (
	"errors"
	"sync"
	"syscall"
)

// Handles stay open for the life of the bridge. Windows never reuses a PID while a handle
// to that process is open, so once the owner has been seen alive, a later check cannot
// mistake an unrelated process that inherited its PID for the editor. Only owner PIDs are
// checked, so the map holds one entry per editor process the bridge has served.
var (
	processHandlesMu sync.Mutex
	processHandles   = map[int]syscall.Handle{}
)

// A process handle becomes signaled when that process exits. SYNCHRONIZE is sufficient:
// no executable details or elevated process-query permissions are needed.
func processAlive(pid int) bool {
	if pid <= 0 || uint64(pid) > uint64(^uint32(0)) {
		return false
	}
	processHandlesMu.Lock()
	defer processHandlesMu.Unlock()
	handle, ok := processHandles[pid]
	if !ok {
		var err error
		handle, err = syscall.OpenProcess(syscall.SYNCHRONIZE, false, uint32(pid))
		if err != nil {
			// Access denied still means a process exists; do not drop the endpoint just because
			// the editor's process permissions prevent inspection.
			return errors.Is(err, syscall.ERROR_ACCESS_DENIED)
		}
		processHandles[pid] = handle
	}
	state, err := syscall.WaitForSingleObject(handle, 0)
	return err != nil || state != syscall.WAIT_OBJECT_0
}
