# Thunderstore / GitHub publish

Publish **only** this directory — not the parent crash-analysis workspace.

## Before upload

1. Create a Thunderstore team (ASCII letters, numbers, underscore).
2. Replace `YOUR_USER` in `README.md` and `manifest.json`.
3. Validate: https://thunderstore.io/tools/manifest-v1-validator/

Dependency string: `{YourTeam}-SatmLanIp-1.0.0`  
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
dotnet restore SatmLanIp.csproj --locked-mode
dotnet build SatmLanIp.csproj -c Release --no-restore
dotnet restore SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore

$ver = "1.0.0"
$stage = "thunderstore-stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Copy-Item manifest.json, README.md, CHANGELOG.md, LICENSE, NOTICE, icon.png $stage
Copy-Item bin\Release\net6.0\SatmLanIp.dll $stage
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
dotnet restore SatmLanIp.Tests/SatmLanIp.Tests.csproj --locked-mode
dotnet test SatmLanIp.Tests/SatmLanIp.Tests.csproj --no-restore
dotnet restore SatmLanIp.csproj --locked-mode
```
