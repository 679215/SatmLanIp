# Run all tests (includes KnownFailure regression markers)
dotnet test SatmLanIp.Tests.csproj --locked-mode -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# CI-green subset
dotnet test SatmLanIp.Tests.csproj --no-build --filter "Category!=KnownFailure" -v minimal
exit $LASTEXITCODE
