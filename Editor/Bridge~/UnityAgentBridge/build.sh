#!/usr/bin/env bash
# UnityAgentBridge build script (Linux / macOS host).
#
# Builds the Go bridge binary and drops it into the Unity package's
# Editor/Bridge/bin/<rid>/ folder so Unity can spawn it at runtime.
#
# Usage:
#   cd Editor/Bridge~/UnityAgentBridge
#   ./build.sh           # builds for the host RID
#   ./build.sh --all     # cross-builds for all 4 supported RIDs
#
# Prerequisites:
#   - Go 1.21+ on PATH (https://go.dev/dl/ or your package manager)

set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")" && pwd)
cd "$SCRIPT_DIR"

if ! command -v go >/dev/null 2>&1; then
    echo "Error: Go is not installed or not on PATH." >&2
    echo "Install via your package manager or https://go.dev/dl/" >&2
    exit 1
fi

# Output goes to Editor/Bridge/bin/<rid>/ (sibling of Bridge~/UnityAgentBridge).
# Bridge~ has the trailing tilde so Unity ignores it; the actual binaries land
# under Editor/Bridge/bin/<rid>/ which Unity DOES import.
OUT_ROOT=$(cd "$SCRIPT_DIR/../../Bridge/bin" && pwd)

build_target() {
    local goos=$1 goarch=$2 rid=$3 exe_name=$4
    local out_dir="$OUT_ROOT/$rid"
    mkdir -p "$out_dir"
    local out_path="$out_dir/$exe_name"
    echo "[build] $rid -> $out_path"
    CGO_ENABLED=0 GOOS="$goos" GOARCH="$goarch" \
        go build -trimpath -ldflags="-s -w" -o "$out_path" .
}

detect_host_rid() {
    local uname_s uname_m
    uname_s=$(uname -s)
    uname_m=$(uname -m)
    case "$uname_s" in
        Linux)
            echo "linux-x64"
            ;;
        Darwin)
            case "$uname_m" in
                arm64|aarch64) echo "osx-arm64" ;;
                *)             echo "osx-x64"   ;;
            esac
            ;;
        MINGW*|MSYS*|CYGWIN*)
            echo "win-x64"
            ;;
        *)
            echo "Error: unsupported host OS: $uname_s" >&2
            exit 1
            ;;
    esac
}

if [[ "${1:-}" == "--all" || "${1:-}" == "-All" ]]; then
    build_target windows amd64 win-x64   UnityAgentBridge.exe
    build_target linux   amd64 linux-x64 UnityAgentBridge
    build_target darwin  amd64 osx-x64   UnityAgentBridge
    build_target darwin  arm64 osx-arm64 UnityAgentBridge
else
    host_rid=$(detect_host_rid)
    case "$host_rid" in
        win-x64)   build_target windows amd64 win-x64   UnityAgentBridge.exe ;;
        linux-x64) build_target linux   amd64 linux-x64 UnityAgentBridge    ;;
        osx-x64)   build_target darwin  amd64 osx-x64   UnityAgentBridge    ;;
        osx-arm64) build_target darwin  arm64 osx-arm64 UnityAgentBridge    ;;
    esac
fi

echo
echo "Build complete. Artifacts:"
find "$OUT_ROOT" -type f -name 'UnityAgentBridge*' ! -name '*.meta' | while read -r f; do
    size=$(du -h "$f" | cut -f1)
    echo "  $f ($size)"
done
