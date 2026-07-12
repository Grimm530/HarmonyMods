# Build script for Radar Harmony Mod
# Output: <server root>\HarmonyMods\Radar.dll

Write-Host "Building Radar..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Radar.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Radar.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Radar.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Radar.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Radar.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load Radar)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
