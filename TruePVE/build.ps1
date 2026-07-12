# Build script for TruePVE Harmony Mod
# Output: <server root>\HarmonyMods\TruePVE.dll
# Config: HarmonyConfig/TruePVE.json

Write-Host "Building TruePVE Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TruePVE.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TruePVE.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TruePVE.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TruePVE.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TruePVE.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/TruePVE.json (created on first load if missing)" -ForegroundColor Yellow
    Write-Host "Load: harmony.load TruePVE (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
