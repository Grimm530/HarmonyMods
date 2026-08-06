# Build script for GrimmBoss Harmony Mod
# Output: <server root>\HarmonyMods\GrimmBoss.dll
# Config: HarmonyConfig/GrimmBoss.json
# Data:   HarmonyData/GrimmBoss/
# Requires: 0GrimmNPC (NpcSpawn Harmony port)

Write-Host "Building GrimmBoss Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "GrimmBoss\GrimmBoss.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "GrimmBoss\bin\Release\GrimmBoss.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "GrimmBoss\bin\Release\net48\GrimmBoss.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under GrimmBoss\bin\Release\GrimmBoss.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "GrimmBoss.dll"

    # Copy ONLY the mod DLL - never any of the referenced Rust/Unity assemblies.
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! GrimmBoss.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/GrimmBoss.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/GrimmBoss/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load 0GrimmNPC then harmony.load GrimmBoss (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
