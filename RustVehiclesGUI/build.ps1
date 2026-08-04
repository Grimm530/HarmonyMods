# Build RustVehiclesGUI Harmony mod
# Copies only RustVehiclesGUI.dll into server HarmonyMods/

Write-Host "Building RustVehiclesGUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RustVehiclesGUI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\RustVehiclesGUI.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RustVehiclesGUI.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RustVehiclesGUI.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RustVehiclesGUI.dll -> $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> RustVehicles -> ServerPanel -> RustVehiclesGUI" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RustVehiclesGUI.json" -ForegroundColor Gray
    Write-Host "Data:   HarmonyData/RustVehiclesGUI/ (images, players)" -ForegroundColor Gray
    Write-Host "Load:   harmony.load RustVehiclesGUI" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
