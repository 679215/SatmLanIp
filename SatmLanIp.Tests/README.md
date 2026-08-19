# SatmLanIp.Tests

CI-safe unit tests for pure protocol / room / parse helpers. No game DLLs required.

## Run

```bash
dotnet restore SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore -v minimal
```

Filter out known production gaps (tests stay in tree as regression markers):

```bash
dotnet test SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore --filter "Category!=KnownFailure"
```

## Layout

| File | Covers |
|------|--------|
| `LanProtocolTests.cs` | SLIP encode/parse, payload lengths |
| `LanRoomTests.cs` | Room snap, ready masks, lobby strings |
| `LanPoseTests.cs` | Pose float payload |
| `LanConfigTests.cs` | Port validation defaults |
| `LanHostParseTests.cs` | JoinAddress host:port parsing |
| `LanLocalIpTests.cs` | Advertise IP filtering / formatting |
| `LanFusionStartTests.cs` | Fusion bind port, client target |

## Known failures (production bugs — do not weaken tests)

These assert desired behavior; production code may not match yet:

- `LanHostParse_rejects_empty_host` — `:37241` should fail
- `LanHostParse_rejects_port_reserved_for_fusion` — port 65535 should fail at parse layer
- `LanHostParse_rejects_invalid_explicit_ports` — `10.0.0.1:65535`
- `LanFusionStart_TryClientTarget_rejects_reserved_port`
- `LanRoom_TryReadSnap_rejects_null_short_and_negative_offsets` — negative offset throws instead of returning false
