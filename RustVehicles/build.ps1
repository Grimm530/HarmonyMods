# Build RustVehicles Harmony mod
# Copies only RustVehicles.dll into server HarmonyMods/

Write-Host "Building RustVehicles..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RustVehicles.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\RustVehicles.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RustVehicles.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RustVehicles.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RustVehicles.dll -> $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> (optional Economics) -> RustVehicles" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RustVehicles.json" -ForegroundColor Gray
    Write-Host "Data:   HarmonyData/RustVehicles/RustVehicles.json" -ForegroundColor Gray
    Write-Host "Load:   harmony.load RustVehicles" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
