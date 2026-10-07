# Regression checks

Run these commands from the repository root. The C# console harnesses link the
production source files directly and have no NuGet dependencies. They use .NET 9
and C# 9 syntax, matching the language level supported by Unity 2022.3.

```sh
dotnet run --project Tests~/Core/Core.Tests.csproj --configuration Release
dotnet run --project Tests~/MCPClient/MCPClient.Tests.csproj --configuration Release
dotnet run --project Tests~/MCPBridge/MCPBridge.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerGestures/GestureManagerGestures.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerMenu/GestureManagerMenu.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerMenu/GestureManagerMenu.Tests.csproj --configuration Release -p:GmEnabled=false
dotnet run --project Tests~/GestureManagerUtilities/GestureManagerUtilities.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerUtilities/GestureManagerUtilities.Tests.csproj --configuration Release -p:GmEnabled=false
node --test BrowserExtension~/tests/*.test.js
cd Editor/Bridge~/UnityAgentBridge
go test ./...
go vet ./...
```

The MCP client harness replaces only Editor logging and secret masking. It checks
response routing, notification delivery, and complete, ordered JSONL writes to a
real child process. The MCP bridge harness links the production bootstrap,
TCP client, and wakeup helper to an editor scheduler stub and a real loopback
bridge peer. It checks startup without `delayCall`, background main-thread
dispatch, reconnect after socket EOF, rapid socket replacement, and wakeup
cleanup on disabling/reload.
The scheduler executes updates only when `SignalTick` requests them and asserts
that Editor APIs and tools run on the main thread. These are not full Unity
Editor integration tests. Import the package with its Unity/VRChat dependencies
to verify Editor behavior and the native wakeup API.

The Gesture Manager gesture harness checks input ranges, gesture-name aliases,
culture-independent parsing, optional tool arguments, and missing-package fallbacks.
It also checks bounded paging defaults, continuation metadata, complete ordered
page reconstruction, terminal offsets, and integer-boundary handling.
It compiles every GM tool partial without the optional package and checks tool-name
uniqueness and missing-package responses, including OSC and radial tools.
The menu harness checks traversal, shared/cyclic submenus, parameter discovery,
control paths, complete paged reconstruction (including nested labels, slots, and
parameter usages), and validating puppet inputs before changing any parameter using
small SDK/Unity stand-ins. These harnesses do not replace compilation against the
actual Gesture Manager/VRChat SDK or testing previews in the Unity Editor.
The utility harness covers temporary-background ownership, restoring profiler
settings, camera/contact configuration, and tracking/weight changes. Its README
also documents an optional reflection-contract check against actual GM assemblies.

GitHub Actions runs the C# harnesses on Windows and Linux, and runs the Go tests
with the race detector on Linux. Locally, `go test -race ./...` also needs a
supported C toolchain. `Tests~` is ignored by Unity and excluded from release zips.
