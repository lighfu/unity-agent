# UnityAgentBridge (Go)

Out-of-process MCP HTTP endpoint that survives Unity domain reloads.

## Why

When UnityAgent runs its MCP server **inside the Unity Editor process** (InProc mode), every
script edit triggers a domain reload that tears down the HTTP listener mid-request. External
tools like Claude Code or Codex see their MCP connection drop whenever the AI writes a script.

This bridge process holds the MCP HTTP endpoint outside Unity. Unity connects to it as a TCP
client. When Unity reloads, the bridge stays alive, queues incoming tool calls, and resumes
delivery once Unity reconnects.

## Architecture

```
┌─────────────┐    HTTP POST   ┌──────────────┐    TCP+JSONL    ┌──────────────┐
│ MCP client  │ ─────────────→ │   Bridge     │ ←─────────────→ │ Unity Editor │
│ (Claude/etc)│                │   (this)     │                 │              │
└─────────────┘                └──────────────┘                 └──────────────┘
                                long-lived                       reloads, reconnects
```

- Public side: HTTP `POST /mcp` with Bearer token (matches existing AgentMCPServer.cs surface).
  `GET /mcp` SSE is intentionally not supported in P1 and returns 405.
- Internal side: TCP listener, line-delimited JSON, shared-secret hello

## Build

Prerequisite: **Go 1.21+**. Install via `winget install GoLang.Go` on Windows, or your
package manager / https://go.dev/dl/ elsewhere.

```powershell
# Windows (PowerShell)
cd Editor/Bridge~/UnityAgentBridge
./build.ps1            # builds win-x64 (current host)
./build.ps1 -All       # cross-builds for win-x64, osx-x64, osx-arm64, linux-x64
```

```bash
# Linux / macOS
cd Editor/Bridge~/UnityAgentBridge
./build.sh             # builds the host RID (linux-x64, osx-x64, or osx-arm64)
./build.sh --all       # cross-builds for all 4 supported RIDs
```

Output goes to `Editor/Bridge/bin/<rid>/UnityAgentBridge[.exe]`. The `Bridge~` source folder
has a trailing tilde so Unity ignores it during import; the `Bridge` folder (no tilde) is
imported and contains only the prebuilt binaries.

> **Note:** binaries are NOT checked into git. They are produced either by the build scripts
> above (for local dev) or by the CI release workflow (`.github/workflows/release-editor.yml`,
> which cross-builds all 4 RIDs into the VPM release zip). The `.meta` files are kept in git
> so Unity's GUIDs for the binary slots remain stable across releases.

## Run (manual / dev)

```powershell
./UnityAgentBridge.exe --token YOUR_TOKEN --internal-port 17801 --public-port 17800 --verbose
```

Unity will then connect over TCP and HTTP MCP clients can hit `http://127.0.0.1:17800/mcp`.

### Flags

| Flag | Default | Meaning |
| --- | --- | --- |
| `--token` | *(required)* | Shared secret for both the MCP Bearer auth and the Unity hello |
| `--public-port` | `17800` | HTTP port for MCP clients |
| `--internal-port` | `17801` | TCP port where Unity connects |
| `--idle-quit` | `5m` | Exit after this long with no living Unity owner, no Unity connection, **and** no MCP client activity. `0` (or any negative value) disables the idle quit, so the bridge runs until killed |
| `--log` | *(none)* | Log file path. Empty = stderr only |
| `--verbose` | `false` | Verbose logging |

Durations accept Go syntax (`30s`, `5m`, `1h30m`). UnityAgent's auto-spawn does not pass
`--idle-quit`, so a bridge started from Unity uses the 5-minute default.

## Run (via UnityAgent)

In Unity:

1. UnityAgent → MCP Settings
2. Set `Server Mode` to `Bridge`
3. Enable `MCP Server`
4. The bridge is auto-spawned by `AgentMCPServerBootstrap` and Unity connects automatically

## Check whether Unity is ready

`GET /health` reports Bridge liveness. From an MCP client, call the Bridge tool
`GetUnityConnectionStatus` with empty arguments to check the Editor independently. The tool
responds during a disconnect and returns a JSON object in the result's text content.

| `readiness` | When it appears |
| --- | --- |
| `ready` | Unity recently reported that its main thread can process tools |
| `waiting_for_connection`, `reconnecting`, `disconnected` | The Bridge is alive and has no Unity connection. `disconnectReason` records the last known cause when available |
| `starting`, `compiling`, `importing`, `play_mode_transition`, `busy` | The Editor is connected and reports work or a transition in progress |
| `dialog_wait` | The Editor detects a blocking dialog |
| `unknown` | The Editor is connected, but its readiness report is missing or at least three seconds old |

`unityConnected` reports the last observed transport state. `statusAgeMs` shows how long ago the
Bridge received the latest Editor report on that connection. `mainThreadAgeMs`, when present,
shows the age of the main-thread tick when the Editor sampled it.

For `dialog_wait`, call `ExecuteUnityTool` with `name=GetEditorState` to inspect the Editor state.
Use `ExecuteUnityTool` with `name=AnswerModalDialog` and `arguments={"dryRun":true}` to read the
message and buttons of a supported native dialog. Choose a button explicitly to answer it.

## Verify the target and classify failures

Set `UNITY_AGENT_MCP_TOKEN` to the token for the selected local MCP endpoint. Run the example
from the repository root with Python 3. It initializes the named MCP client, authenticates the
request and calls `GetUnityAgentInfo` with `detail=connection` when invoked without a tool name.
The report includes the editor PID, absolute project path, active scene path, endpoint, and
authentication configuration. In Bridge mode, register the local `/mcp` URL and Bearer token
in the client configuration. Each running Unity project needs its own MCP ports.

```powershell
python Tests~/mcp_call.py --endpoint http://127.0.0.1:17800/mcp --expect-project 'C:\Unity\ProjectA' --expect-scene Assets/Main.unity
'{"name":"GetPlayModeState","arguments":{}}' | python Tests~/mcp_call.py --expect-project 'C:\Unity\ProjectA' ExecuteUnityTool -
```

The `--expect-project` and `--expect-scene` checks run before the named tool. The example
writes a JSON failure report to stderr and exits with a nonzero status.

| Exit status | Failure layer | MCP response |
| --- | --- | --- |
| `10` | HTTP or network | HTTP status outside the success range, or no HTTP connection |
| `11` | JSON-RPC | An `error` envelope or malformed JSON-RPC response. Tool-specific failures include `error.data` with `tool`, `reason` and `suggestions` |
| `12` | Tool execution | `result.isError` is `true`. `result.structuredContent` contains `tool`, `reason` and `suggestions` |
| `13` | Target verification | The connected Editor identity is missing or differs from the expected project or scene |

For example, an unknown tool name or invalid argument is a JSON-RPC error. A tool that accepts
the request and reports a missing scene target is an execution error. This distinction lets a
client decide which input to correct and whether it needs to verify the previous operation.

## Wire protocol (Unity ↔ Bridge)

Each direction is line-delimited JSON. One object per line.

### Unity → Bridge

```jsonc
// handshake (must be first message); unityPid is optional for older clients
{"type":"hello","version":"1","token":"<shared-secret>","unityPid":12345}

// readiness report, sent only when the handshake acknowledgement advertises unityStatus
{"type":"unity_status","status":"ready","mainThreadAgeMs":15,"playMode":"edit"}

// tool result
{"type":"result","id":"<bridge-id>","ok":true,"text":"<output>"}
{"type":"result","id":"<bridge-id>","ok":true,"text":"<description>","imageBase64":"<encoded-image>","imageMimeType":"image/png"}
{"type":"error", "id":"<bridge-id>","code":-32000,"message":"...","data":"..."}

// reload notice (sent in beforeAssemblyReload, then connection closes)
{"type":"shutdown","reason":"domain_reload"}
```

### Bridge → Unity

```jsonc
// hello acknowledgement
{"type":"hello_ack","ok":true,"unityStatus":true}
{"type":"hello_ack","ok":false,"error":"bad token"}

// tool call dispatch
{"type":"call","id":"<bridge-id>","tool":"WriteFile","args":{...}}
```

## Lifecycle

- **Bridge spawn**: Unity's `AgentMCPServerBootstrap` checks for an existing `Library/UnityAgent/Bridge.lock`
  pid; if missing or stale, spawns a new process detached from Unity. Lockfile holds the bridge pid.
- **Domain reload**: Unity sends `shutdown`, closes TCP, restarts itself. Bridge keeps running.
  Calls that were **already sent to Unity** fail immediately with JSON-RPC error `-32003`
  (`Unity reloaded the app domain while this call was running`), because the Unity that
  received them no longer exists — waiting the 120 s timeout would only tell the caller
  "timeout", which it cannot tell apart from a slow tool or a hung editor. They are **not**
  re-sent: the usual victim is the call that triggered the reload (`RefreshAssetDatabase`),
  which would otherwise run twice. If the socket closes with no `shutdown` notice, the message
  is `Unity connection lost while this call was running` instead — treat that as a possible
  crash. Calls that arrived while Unity was away (never sent) are queued as before.
- **Unity reconnect**: After reload, Unity starts on the first `EditorApplication.update`, sends
  `hello` again, and the queue is flushed. Startup does not wait for Inspector-driven `delayCall`.
  While Bridge mode is enabled, a 200 ms timer calls Unity's thread-safe native `SignalTick`
  entry point to keep startup, reconnect supervision, and tool coroutines moving when the editor
  is unfocused. Socket readers also signal a tick on incoming calls. These signals do not execute
  tools on the timer thread or bring Unity to the foreground. If a Unity version removes this
  internal API, a warning is logged and ordinary Editor updates remain the fallback.
  If an MCP client cancels or disconnects while a call is still queued, that call is removed and
  will not execute after reconnect. Queued calls older than the 120 s per-call timeout are also
  **dropped instead of dispatched** — running them would execute a tool nobody is waiting for.
  Dropped calls are logged (`dropping stale queued call ...`). The same prune runs every 30 s while
  Unity is away, so the queue cannot grow without bound. Calls already sent to Unity cannot be
  withdrawn from the wire; their result is dropped if the MCP request has gone away.
- **Bridge death**: Unity's `AgentMCPServerBootstrap` keeps a supervisor on `EditorApplication.update`
  for the whole session, not just at startup. At startup it polls every 200 ms for ~5 s (the bridge
  is still coming up). If the connection is later lost — bridge killed, crashed, or idle-quit — it
  retries from 1 s with exponential backoff capped at 30 s, re-checking the lockfile and respawning
  the binary before each attempt. It never gives up permanently; recovery no longer needs a domain
  reload or a manual settings toggle.
- **Idle quit**: Bridge exits after `--idle-quit` (default 5 m) with no Unity connection AND no MCP
  client activity, provided its owning Unity process is also gone. An authenticated `hello` with
  `unityPid` identifies that owner: a long reload or deferred background reconnect cannot kill
  the HTTP endpoint while Unity is still alive. A `shutdown` with any reason other than
  `domain_reload` (MCP disabled, switched to InProc) releases that owner, since nothing will
  reconnect. Older clients omitting `unityPid` keep the legacy idle behavior. Every authenticated `/mcp` request refreshes the timer, and a request still in
  flight blocks the quit outright. Pass `--idle-quit 0` to disable it.

## Limitations (P1)

- Only one Unity connection at a time
- `tools/list` advertises the four top-level Editor tools (SearchUnityTool, DescribeUnityTool, ExecuteUnityTool, GetUnityAgentInfo) and the Bridge-local `GetUnityConnectionStatus`. The Editor tool schemas are mirrored from `Handlers.HandleToolsList` on the C# side. The Bridge also forwards unlisted Editor tool calls when the caller already knows the name.
- No SSE channel (Claude Code falls back to plain POST)
- No OAuth discovery (Bearer token only)
- macOS / Linux binaries are unsigned

P2 will add SSE, OAuth discovery, multi-Unity tenant support.
