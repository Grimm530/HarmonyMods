# Build KaruzaVehicles Harmony Mod
# Output: <server root>\HarmonyMods\KaruzaVehicles.dll (DLL only)

Write-Host "Building KaruzaVehicles Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "KaruzaVehicles.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\KaruzaVehicles.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\KaruzaVehicles.dll"
    }
    $destPath = Join-Path $harmonyModsPath "KaruzaVehicles.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! KaruzaVehicles.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> KaruzaVehicles" -ForegroundColor Yellow
    Write-Host "Unload Oxide: KaruzaEntitiesCommon, RustCar, RustHelicopter, RustPlane, KaruzaVehiclePush, KaruzaVehicleHorseTowing, BulletProjectile, CustomEntities" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/KaruzaEntitiesCommon.json, RustCar.json, RustHelicopter.json, RustPlane.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/KaruzaVehicles.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
