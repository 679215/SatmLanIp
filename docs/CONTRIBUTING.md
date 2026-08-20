# Contributing

## Requirements

- Legitimate Shift at Midnight install
- BepInEx IL2CPP `6.0.755`
- .NET SDK for `net6.0`

## Build

1. Copy interop DLLs into `libs/` (see `libs/README.md`). Do not commit them.
2. `dotnet restore src/SatmLanIp/SatmLanIp.csproj --locked-mode`
3. `dotnet restore tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode`
4. `dotnet test tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore --filter "Category!=KnownFailure"`
5. `dotnet build src/SatmLanIp/SatmLanIp.csproj -c Release --no-restore`
6. Copy `src/SatmLanIp/bin/Release/net6.0/SatmLanIp.dll` to `BepInEx/plugins/`

## Pull requests

No game assemblies, logs, or personal configs in the diff.
