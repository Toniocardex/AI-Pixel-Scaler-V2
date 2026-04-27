<#!
  Flusso consigliato dopo modifiche ai moduli:
    1) dotnet test
    2) dotnet build (Release)
    3) opzionale: dotnet publish (self-contained win-x64 in dist/win-x64)

  Uso (dalla root del repo):
    .\scripts\ai-pixel-scaler-flow.ps1
    .\scripts\ai-pixel-scaler-flow.ps1 -Publish
#>
param(
    [switch] $Publish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

Write-Host "==> dotnet test (Release)" -ForegroundColor Cyan
dotnet test (Join-Path $RepoRoot "tests/AiPixelScaler.Core.Tests/AiPixelScaler.Core.Tests.csproj") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> dotnet build (Release)" -ForegroundColor Cyan
dotnet build (Join-Path $RepoRoot "AiPixelScaler.sln") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $Publish) {
    Write-Host "OK: test + build completati." -ForegroundColor Green
    exit 0
}

$out = Join-Path $RepoRoot "dist/win-x64"
Write-Host "==> dotnet publish -> $out" -ForegroundColor Cyan
dotnet publish (Join-Path $RepoRoot "src/AiPixelScaler.Desktop/AiPixelScaler.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    -o $out `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "OK: test + build + publish completati. Eseguibile: $out/AiPixelScaler.Desktop.exe" -ForegroundColor Green
