# Build HitMarkers Harmony Mod
# Output: <server root>\HarmonyMods\HitMarkers.dll (DLL only)

Write-Host "Building HitMarkers Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "HitMarkers.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\HitMarkers.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\HitMarkers.dll"
    }
    $destPath = Join-Path $harmonyModsPath "HitMarkers.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! HitMarkers.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> HitMarkers" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/HitMarkers.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
