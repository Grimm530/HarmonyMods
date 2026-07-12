# Build script for TCUpgrade Harmony Mod
# Output: <server root>\HarmonyMods\TCUpgrade.dll (DLL name unchanged for HarmonyLoader)
# Requires: Oxide TCUpgrade.cs plugin for full functionality

Write-Host "Building TCUpgrade Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TCUpgrade.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TCUpgrade.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TCUpgrade.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TCUpgrade.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TCUpgrade.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load TCUpgrade (or automatic at startup)" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/TCUpgrade.json - unload Oxide TCUpgrade plugin first" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
