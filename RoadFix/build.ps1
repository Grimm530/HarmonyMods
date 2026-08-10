# Build script for RoadFix Harmony Mod
# Output: <serverRoot>\HarmonyMods\RoadFix.dll

Write-Host "Building RoadFix..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RoadFix.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RoadFix.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\RoadFix.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RoadFix.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RoadFix.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load RoadFix" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RoadFix.json (created on first load)" -ForegroundColor Gray
    Write-Host "Requires a NEW procedural map for mesh/terrain changes to apply." -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
