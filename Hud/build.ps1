# Build script for Hud Harmony Mod
# Output: <server root>\HarmonyMods\Hud.dll
# Config: HarmonyConfig/Hud.json
# Data:   HarmonyData/Hud  Images: HarmonyImages/Hud

Write-Host "Building Hud Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Hud.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Hud.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Hud.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\Hud.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "Hud.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  Hud.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/Hud.json" -ForegroundColor Yellow
    Write-Host "Data:    CustomDataDirectory or HarmonyData/Hud" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup. Unload oxide/plugins/Hud.cs if both present." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED! Check errors above." -ForegroundColor Red
    exit 1
}
