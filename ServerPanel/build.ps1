# Build ServerPanel Harmony Mod (ServerPanel + ServerPanelPopUps consolidated)
# Copies only ServerPanel.dll to server root HarmonyMods/

Write-Host "Building ServerPanel..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ServerPanel.csproj"
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
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ServerPanel.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ServerPanel.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "ServerPanel.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! ServerPanel.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load ServerPanel" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ServerPanel.json + HarmonyConfig/ServerPanelPopUps.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/ServerPanel/ + HarmonyData/ServerPanelPopUps/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
