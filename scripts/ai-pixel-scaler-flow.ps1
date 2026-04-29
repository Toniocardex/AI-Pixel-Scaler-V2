<#!
  Flusso consigliato dopo modifiche ai moduli:
    1) dotnet test
    2) dotnet build (Release)
    3) opzionale: dotnet publish in doppia uscita
       - self-contained single-file: dist/win-x64
       - framework-dependent: publish/win-x64

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

function Assert-DesktopAppNotRunningForPublish {
    # Solo sul nome dell'eseguibile (AiPixelScaler.Desktop.exe → ProcessName AiPixelScaler.Desktop).
    # NON usare MainWindowTitle con "*AI Pixel Scaler*": Cursor e altri IDE mettono nel titolo il nome
    # della cartella workspace (es. "AI Pixel Scaler v2") e risultano falsi positivi — sembra che l'app
    # sia ancora aperta o persino che chiudendo "l'app" si chiuda Cursor.
    $running = @(Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -eq 'AiPixelScaler.Desktop' })

    if ($running.Count -gt 0) {
        Write-Host "ERRORE: chiudi AI Pixel Scaler prima del publish framework-dependent (publish/win-x64)." -ForegroundColor Red
        Write-Host "Processo/i bloccati (solo AiPixelScaler.Desktop): $($running.Count)" -ForegroundColor Yellow
        exit 1
    }
}

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

$distOut = Join-Path $RepoRoot "dist/win-x64"
Write-Host "==> dotnet publish (self-contained single-file) -> $distOut" -ForegroundColor Cyan
dotnet publish (Join-Path $RepoRoot "src/AiPixelScaler.Desktop/AiPixelScaler.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    -o $distOut `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishOut = Join-Path $RepoRoot "publish/win-x64"
Assert-DesktopAppNotRunningForPublish
Write-Host "==> dotnet publish (framework-dependent) -> $publishOut" -ForegroundColor Cyan
dotnet publish (Join-Path $RepoRoot "src/AiPixelScaler.Desktop/AiPixelScaler.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    -o $publishOut `
    --self-contained false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "OK: test + build + doppio publish completati." -ForegroundColor Green
Write-Host "  dist   => $distOut/AiPixelScaler.Desktop.exe" -ForegroundColor Green
Write-Host "  publish=> $publishOut/AiPixelScaler.Desktop.exe" -ForegroundColor Green
