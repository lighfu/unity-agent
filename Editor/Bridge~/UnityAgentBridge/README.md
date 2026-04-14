# UnityAgentBridge (Go)

Out-of-process MCP HTTP/SSE endpoint that survives Unity domain reloads.

## Why

When UnityAgent runs its MCP server **inside the Unity Editor process** (InProc mode), every
script edit triggers a domain reload that tears down the HTTP listener mid-request. External
tools like Claude Code or Codex see their MCP connection drop whenever the AI writes a script.

This bridge process holds the MCP HTTP endpoint outside Unity. Unity connects to it as a TCP
client. When Unity reloads, the bridge stays alive, queues incoming tool calls, and resumes
delivery once Unity reconnects.

## Architecture

```
┌─────────────┐    HTTP+SSE    ┌──────────────┐    TCP+JSONL    ┌──────────────┐
│ MCP client  │ ─────────────→ │   Bridge     │ ←─────────────→ │ Unity Editor │
│ (Claude/etc)│                │   (this)     │                 │              │
└─────────────┘                └──────────────┘                 └──────────────┘
                                long-lived                       reloads, reconnects
```

- Public side: HTTP `POST /mcp` with Bearer token (matches existing AgentMCPServer.cs surface)
- Internal side: TCP listener, line-delimited JSON, shared-secret hello

## Build

Prerequisite: **Go 1.21+**. Install via `winget install GoLang.Go` on Windows.

```powershell
cd Editor/Bridge~/UnityAgentBridge
./build.ps1            # builds win-x64 (current host)
./build.ps1 -All       # cross-builds for win-x64, osx-x64, osx-arm64, linux-x64
```

Output goes to `Editor/Bridge/bin/<rid>/UnityAgentBridge[.exe]`. The `Bridge~` source folder
has a trailing tilde so Unity ignores it during import; the `Bridge` folder (no tilde) is
imported and contains only the prebuilt binaries.

## Run (manual / dev)

```powershell
./UnityAgentBridge.exe --token YOUR_TOKEN --internal-port 17801 --public-port 17800 --verbose
```

Unity will then connect over TCP and HTTP MCP clients can hit `http://127.0.0.1:17800/mcp`.

## Run (via UnityAgent)

In Unity:

1. Window → 紫陽花広場 → Unity AI エージェント → Settings → MCP tab
2. Set `Server Mode` to `Bridge`
3. Enable `MCP Server`
4. The bridge is auto-spawned by `AgentMCPServerBootstrap` and Unity connects automatically

## Wire protocol (Unity ↔ Bridge)

Each direction is line-delimited JSON. One object per line.

### Unity → Bridge

```jsonc
// handshake (must be first message)
{"type":"hello","version":"1","token":"<shared-secret>"}

// tool result
{"type":"result","id":"<bridge-id>","ok":true,"text":"<output>"}
{"type":"error", "id":"<bridge-id>","code":-32000,"message":"...","data":"..."}

// reload notice (sent in beforeAssemblyReload, then connection closes)
{"type":"shutdown","reason":"domain_reload"}
```

### Bridge → Unity

```jsonc
// hello acknowledgement
{"type":"hello_ack","ok":true}
{"type":"hello_ack","ok":false,"error":"bad token"}

// tool call dispatch
{"type":"call","id":"<bridge-id>","tool":"WriteFile","args":{...}}
```

## Lifecycle

- **Bridge spawn**: Unity's `AgentMCPServerBootstrap` checks for an existing `Library/UnityAgent/Bridge.lock`
  pid; if missing or stale, spawns a new process detached from Unity. Lockfile holds the bridge pid.
- **Domain reload**: Unity sends `shutdown`, closes TCP, restarts itself. Bridge keeps running,
  queues any in-flight tool calls.
- **Unity reconnect**: After reload, Unity reconnects, sends `hello` again, queue is flushed.
- **Idle quit**: Bridge exits after 5 minutes with no Unity connection AND no MCP client activity.

## Limitations (P1)

- Only one Unity connection at a time
- Only `tools/call` for the 3 meta-tools (SearchUnityTool, DescribeUnityTool, ExecuteUnityTool)
- No SSE channel (Claude Code falls back to plain POST)
- No OAuth discovery (Bearer token only)
- macOS / Linux binaries are unsigned

P2 will add SSE, OAuth discovery, multi-Unity tenant support.
