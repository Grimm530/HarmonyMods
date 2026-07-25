# Build script for ArmoredTrain Harmony Mod
# Output: <server root>\HarmonyMods\ArmoredTrain.dll
# Config: HarmonyConfig/ArmoredTrain.json
# Data:   HarmonyData/ArmoredTrain/

Write-Host "Building ArmoredTrain Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ArmoredTrain\ArmoredTrain.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "ArmoredTrain\bin\Release\ArmoredTrain.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "ArmoredTrain\bin\Release\net48\ArmoredTrain.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under ArmoredTrain\bin\Release\ArmoredTrain.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "ArmoredTrain.dll"

    # Copy ONLY the mod DLL - never any of the referenced Rust/Unity assemblies.
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ArmoredTrain.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/ArmoredTrain.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/ArmoredTrain/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load ArmoredTrain (requires 0GrimmNPC; or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
