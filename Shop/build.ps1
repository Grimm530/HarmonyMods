# Build Shop Harmony Mod
# Copies only Shop.dll to server root HarmonyMods/

Write-Host "Building Shop..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Shop.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $candidate = Join-Path $PSScriptRoot "..\..\.."
        $root = [System.IO.Path]::GetFullPath($candidate)
    }
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Shop.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Shop.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Shop.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! Shop.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Shop" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Shop.json  Data: HarmonyData/Shop/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
