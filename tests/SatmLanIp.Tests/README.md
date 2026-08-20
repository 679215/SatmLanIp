# SatmLanIp.Tests

CI-safe unit tests for pure protocol / room / parse helpers. No game DLLs required.

## Run

One-shot script (restore + full suite + CI filter):

```powershell
./tests/SatmLanIp.Tests/run-tests.ps1
```

Same commands as CI:

```bash
dotnet restore tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore -v minimal --filter "Category!=KnownFailure"
```

Full suite (no KnownFailure markers currently):

```bash
dotnet test tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore
```

## Layout

| File | Covers |
|------|--------|
| `LanProtocolTests.cs` | SLIP encode/parse, payload lengths |
| `LanRoomTests.cs` | Room snap, ready masks, lobby strings |
| `LanSessionTests.cs` | Session AllReady / InRoom wrappers |
| `LanPoseTests.cs` | Pose float payload |
| `LanConfigTests.cs` | Port validation defaults |
| `LanHostParseTests.cs` | JoinAddress host:port parsing |
| `LanLocalIpTests.cs` | Advertise IP filtering / formatting |
| `LanFusionStartTests.cs` | Fusion bind port, client target, actor ids |

## Known failures

None. Keep `Category!=KnownFailure` in CI so future regression markers can land without breaking the pipeline.
