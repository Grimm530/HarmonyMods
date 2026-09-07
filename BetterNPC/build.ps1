# Build script for BetterNPC Harmony Mod
# Output: <server root>\HarmonyMods\BetterNPC.dll
# Config: HarmonyConfig/BetterNpc.json
# Data:   HarmonyData/BetterNpc/

Write-Host "Building BetterNPC Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "BetterNPC\BetterNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "BetterNPC\bin\Release\BetterNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "BetterNPC\bin\Release\net48\BetterNPC.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under BetterNPC\bin\Release\BetterNPC.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "BetterNPC.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! BetterNPC.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/BetterNpc.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/BetterNpc/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load BetterNPC (requires 0GrimmNPC + 0Permissions; or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
