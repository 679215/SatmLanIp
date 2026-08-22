# Thunderstore / GitHub publish

Publish **only** this directory — not the parent crash-analysis workspace.

## Before upload

1. Create a Thunderstore team (ASCII letters, numbers, underscore).
2. Replace `YOUR_USER` in `README.md` and `src/SatmLanIp/manifest.json` (default: `679215`).
3. Validate: https://thunderstore.io/tools/manifest-v1-validator/

Dependency string: `{YourTeam}-SatmLanIp-1.0.3`  
Category: `mods`

## Zip root

| File | Required |
|------|----------|
| `manifest.json` | yes |
| `README.md` | yes |
| `LICENSE` | yes |
| `NOTICE` | yes |
| `icon.png` | yes (256×256) |
| `SatmLanIp.dll` | yes |
| `CHANGELOG.md` | optional |

Exclude: `libs/` except `libs/README.md`, game DLLs, `bin/`, `obj/`, logs, configs,
`.git/`, `.agents/`, `.playwright-mcp/`, `skills-lock.json`, tests, and `.github/`
from the Thunderstore zip. For the public Git repository, include `libs/README.md`
but never include the actual interop DLLs.

```powershell
dotnet restore src/SatmLanIp/SatmLanIp.csproj --locked-mode
dotnet build src/SatmLanIp/SatmLanIp.csproj -c Release --no-restore
dotnet restore tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore --filter "Category!=KnownFailure"

$ver = "1.0.3"
$stage = "thunderstore-stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item src/SatmLanIp/manifest.json, README.md, CHANGELOG.md, LICENSE, NOTICE, src/SatmLanIp/icon.png $stage
Copy-Item src/SatmLanIp/bin/Release/net6.0/SatmLanIp.dll $stage
Compress-Archive -Path "$stage\*" -DestinationPath "SatmLanIp-$ver-thunderstore.zip" -Force
```

Upload: https://thunderstore.io/package/create/ → **Shift at Midnight**

## GitHub

1. New empty public repo
2. Copy only this directory
3. Replace `YOUR_USER`
4. Check:

```powershell
git ls-files | Select-String -Pattern '\.dll$|libs/|\.cfg$|\.log$|bin/|obj/'
Test-Path libs/README.md
dotnet restore tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test tests/SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore --filter "Category!=KnownFailure"
dotnet restore src/SatmLanIp/SatmLanIp.csproj --locked-mode
```
