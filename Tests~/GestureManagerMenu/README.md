This console harness links the production ExpressionMenu tools, the production
JSON serializer, and the tool attribute. Unity/VRChat/GestureManager host types are
minimal stubs; it verifies menu graph traversal, index/name paths, shared menus and
cycles, explicit truncation, parameter definitions and runtime values, held button
presses, toggle behavior, normalized four-axis mapping, puppet closing, and
validation before multi-parameter writes. A 300-level menu verifies that no
recursion cutoff silently drops menus.

Run from the repository root:

```sh
dotnet run --project Tests~/GestureManagerMenu/GestureManagerMenu.Tests.csproj --configuration Release
dotnet run --project Tests~/GestureManagerMenu/GestureManagerMenu.Tests.csproj --configuration Release -p:GmEnabled=false
```

These checks do not execute the Unity Editor, GestureManager callbacks, Playables,
or OSC sockets. Compile against the real dependencies and exercise an imported
avatar in Unity to verify those integration behaviors.
