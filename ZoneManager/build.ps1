# Build ZoneManager Harmony Mod
# Output: <server root>\HarmonyMods\ZoneManager.dll (DLL only)

Write-Host "Building ZoneManager Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ZoneManager.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ZoneManager.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ZoneManager.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "ZoneManager.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ZoneManager.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> ZoneManager (Spawns optional)" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ZoneManager.json" -ForegroundColor Gray
    Write-Host "Data:   HarmonyData/ZoneManager/" -ForegroundColor Gray
    Write-Host "Lang:   HarmonyLanguage/ZoneManager.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
