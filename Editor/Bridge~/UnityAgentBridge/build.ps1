# UnityAgentBridge build script (Windows host).
#
# Builds the Go bridge binary for win-x64 and copies it to the Unity package's
# Editor/Bridge/bin/win-x64/ folder so that Unity can spawn it at runtime.
#
# Usage:
#   cd Editor/Bridge~/UnityAgentBridge
#   ./build.ps1                # builds for win-x64 (current host)
#   ./build.ps1 -All           # builds for all supported RIDs (requires cross-compile capable Go)
#
# Prerequisites:
#   - Go 1.21+ installed and on PATH. Install via:
#       winget install GoLang.Go
#     or download from https://go.dev/dl/

param(
    [switch]$All
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Push-Location $ScriptDir
try {
    if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
        Write-Error "Go is not installed or not on PATH. Install via: winget install GoLang.Go"
        exit 1
    }

    # Output goes to Editor/Bridge/bin/<rid>/ (sibling of Bridge~/UnityAgentBridge)
    # Bridge~ has the trailing tilde so Unity ignores it during import; the actual binaries
    # land under Editor/Bridge/bin/<rid>/ which Unity DOES import (no tilde).
    $OutRoot = Resolve-Path (Join-Path $ScriptDir "..\..\Bridge\bin")

    function Build-Target($GoOS, $GoArch, $Rid, $ExeName) {
        $OutDir = Join-Path $OutRoot $Rid
        if (-not (Test-Path $OutDir)) {
            New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        }
        $OutPath = Join-Path $OutDir $ExeName
        Write-Host "[build] $Rid -> $OutPath"

        $env:GOOS = $GoOS
        $env:GOARCH = $GoArch
        $env:CGO_ENABLED = "0"
        & go build -trimpath -ldflags="-s -w" -o $OutPath .
        if ($LASTEXITCODE -ne 0) {
            Write-Error "go build failed for $Rid"
            exit $LASTEXITCODE
        }
    }

    if ($All) {
        Build-Target -GoOS "windows" -GoArch "amd64" -Rid "win-x64"   -ExeName "UnityAgentBridge.exe"
        Build-Target -GoOS "darwin"  -GoArch "amd64" -Rid "osx-x64"   -ExeName "UnityAgentBridge"
        Build-Target -GoOS "darwin"  -GoArch "arm64" -Rid "osx-arm64" -ExeName "UnityAgentBridge"
        Build-Target -GoOS "linux"   -GoArch "amd64" -Rid "linux-x64" -ExeName "UnityAgentBridge"
    }
    else {
        Build-Target -GoOS "windows" -GoArch "amd64" -Rid "win-x64" -ExeName "UnityAgentBridge.exe"
    }

    Write-Host ""
    Write-Host "Build complete. Binary location:" -ForegroundColor Green
    Get-ChildItem -Recurse $OutRoot -Filter "UnityAgentBridge*" | ForEach-Object {
        Write-Host "  $($_.FullName) ($([math]::Round($_.Length / 1KB, 1)) KB)"
    }
}
finally {
    Pop-Location
    Remove-Item env:GOOS -ErrorAction SilentlyContinue
    Remove-Item env:GOARCH -ErrorAction SilentlyContinue
    Remove-Item env:CGO_ENABLED -ErrorAction SilentlyContinue
}
