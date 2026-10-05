# Regression checks

Run these commands from the repository root. The C# console harnesses link the
production source files directly and have no NuGet dependencies. They use .NET 9
and C# 9 syntax, matching the language level supported by Unity 2022.3.

```sh
dotnet run --project Tests~/Core/Core.Tests.csproj --configuration Release
dotnet run --project Tests~/MCPClient/MCPClient.Tests.csproj --configuration Release
dotnet run --project Tests~/MCPBridge/MCPBridge.Tests.csproj --configuration Release
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

GitHub Actions runs the C# harnesses on Windows and Linux, and runs the Go tests
with the race detector on Linux. Locally, `go test -race ./...` also needs a
supported C toolchain. `Tests~` is ignored by Unity and excluded from release zips.
