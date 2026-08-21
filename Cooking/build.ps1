# Build script for Cooking Harmony Mod
# Output: <server root>\HarmonyMods\Cooking.dll
# Config: HarmonyConfig/Cooking.json
# Data:   Custom cooking data directory or HarmonyData/Cooking

Write-Host "Building Cooking Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Cooking.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Cooking.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Cooking.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\Cooking.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "Cooking.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  Cooking.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/Cooking.json" -ForegroundColor Yellow
    Write-Host "Data:    Custom cooking data directory or HarmonyData/Cooking" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup. Unload oxide/plugins/Cooking.cs if both present (file left in place)." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED! Check errors above." -ForegroundColor Red
    exit 1
}
