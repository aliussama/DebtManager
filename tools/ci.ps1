$ErrorActionPreference = "Stop"

Write-Host "== Build ==" -ForegroundColor Cyan
dotnet build

Write-Host "== Tests ==" -ForegroundColor Cyan
dotnet test --no-build

Write-Host "== Done ==" -ForegroundColor Green
