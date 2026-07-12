# Build script for RaidableBases Harmony Mod
# Output: <server root>\HarmonyMods\RaidableBases.dll
# Config: HarmonyConfig/RaidableBases.json
# Data: HarmonyData/RaidableBases/  Paste: HarmonyData/copypaste/

Write-Host "Building RaidableBases Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RaidableBases.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    # csproj OutputPath is bin\Release\ (AppendTargetFrameworkToOutputPath=false)
    $dllPath = Join-Path $PSScriptRoot "bin\Release\RaidableBases.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RaidableBases.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\RaidableBases.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RaidableBases.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RaidableBases.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/RaidableBases.json (created on first load if missing)" -ForegroundColor Yellow
    Write-Host "Data: HarmonyData/RaidableBases/  Paste files: HarmonyData/copypaste/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load CopyPaste then harmony.load RaidableBases (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
