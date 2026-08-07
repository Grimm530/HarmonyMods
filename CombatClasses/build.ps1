# Build script for CombatClasses Harmony Mod
# Output: <server root>\HarmonyMods\CombatClasses.dll
# Config: HarmonyConfig/CombatClasses.json
# Data:   CustomDataDirectory (default C:\!DataPersistence\oxide\data\CombatClasses) or HarmonyData/CombatClasses

Write-Host "Building CombatClasses Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CombatClasses.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $candidate = Join-Path $PSScriptRoot "..\..\..\"
        $root = [System.IO.Path]::GetFullPath($candidate)
    }

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\CombatClasses.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CombatClasses.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\CombatClasses.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "CombatClasses.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  CombatClasses.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/CombatClasses.json" -ForegroundColor Yellow
    Write-Host "Data:    CustomDataDirectory or HarmonyData/CombatClasses" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup. Unload oxide/plugins/CombatClasses.cs if both present." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED! Check errors above." -ForegroundColor Red
    exit 1
}
