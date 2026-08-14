# Build script for Quest Harmony Mod
# Output: <server root>\HarmonyMods\Quest.dll
# Config: HarmonyConfig/Quest.json
# Data:   HarmonyData/Quest  Images: HarmonyImages/Quest

Write-Host "Building Quest Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Quest.csproj"
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

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Quest.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Quest.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\Quest.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "Quest.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  Quest.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/Quest.json" -ForegroundColor Yellow
    Write-Host "Data:    CustomDataDirectory or HarmonyData/Quest" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup. Unload oxide/plugins/Quest.cs if both present." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED! Check errors above." -ForegroundColor Red
    exit 1
}
