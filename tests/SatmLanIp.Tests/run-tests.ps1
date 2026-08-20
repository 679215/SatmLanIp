# Local / CI-parity runner for pure-logic tests (no game DLLs).
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "SatmLanIp.Tests.csproj"

dotnet restore $proj --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Full suite (includes KnownFailure regression markers when present)
dotnet test $proj --no-restore -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# CI-green subset (same filter as .github/workflows/repo-hygiene.yml)
dotnet test $proj --no-restore --no-build --filter "Category!=KnownFailure" -v minimal
exit $LASTEXITCODE
