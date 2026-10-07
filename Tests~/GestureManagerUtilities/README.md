# Gesture Manager utility regression checks

```sh
dotnet run --project Tests~/GestureManagerUtilities/GestureManagerUtilities.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerUtilities/GestureManagerUtilities.Tests.csproj --configuration Release -p:GmEnabled=false
```

The harness links the production utility tools. Narrow Unity/GM test doubles
exercise validation, camera synchronization, contact release, background ownership
and cleanup, profiler state restoration, per-preview tracking, and weight changes.
The Scene Camera read check also verifies that a fresh preview does not construct
GM's lazy utility, replace its global camera target, or write its preferences.
These checks do not replace Unity Editor integration testing.

An optional metadata check verifies every reflected GM member against separately
compiled official Gesture Manager assemblies and their real Unity/VRC dependencies:

```sh
dotnet run --project Tests~/GestureManagerUtilities/GestureManagerUtilities.Tests.csproj --configuration Release -- --gm-assembly-dir /path/to/compiled/dependencies
```

The directory must contain `vrchat.blackstartx.gesture-manager.editor.dll`, its
runtime DLL, and dependency DLLs. This check only reads assembly metadata and
does not invoke Unity's native API.
