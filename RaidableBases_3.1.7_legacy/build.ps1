# Legacy 3.1.7 tree. Do NOT copy over HarmonyMods\RaidableBases.dll — that is the live
# RaidableBases project. This script compiles only; it never deploys into the loader folder.

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

    Write-Host "`nLegacy compile succeeded: $dllPath" -ForegroundColor Green
    Write-Host "Not copied to HarmonyMods — live RaidableBases.dll is built from .cursor\HarmonyMods\RaidableBases" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
