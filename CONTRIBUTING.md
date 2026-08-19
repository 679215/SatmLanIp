# Contributing

## Requirements

- Legitimate Shift at Midnight install
- BepInEx IL2CPP `6.0.755`
- .NET SDK for `net6.0`

## Build

1. Copy interop DLLs into `libs/` (see `libs/README.md`). Do not commit them.
2. `dotnet restore SatmLanIp.csproj --locked-mode`
3. `dotnet restore SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode`
4. `dotnet test SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore`
5. `dotnet build SatmLanIp.csproj -c Release --no-restore`
6. Copy `bin/Release/net6.0/SatmLanIp.dll` to `BepInEx/plugins/`

## Pull requests

No game assemblies, logs, or personal configs in the diff.
