# Build script for DefendableHomes Harmony Mod
# Output: <server root>\HarmonyMods\DefendableHomes.dll
# Config: HarmonyConfig/DefendableHomes.json
# Data:   HarmonyData/DefendableHomes.json

Write-Host "Building DefendableHomes Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "DefendableHomes\DefendableHomes.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "DefendableHomes\bin\Release\DefendableHomes.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "DefendableHomes\bin\Release\net48\DefendableHomes.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under DefendableHomes\bin\Release\DefendableHomes.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "DefendableHomes.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! DefendableHomes.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/DefendableHomes.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/DefendableHomes.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load DefendableHomes (requires 0GrimmNPC; or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
