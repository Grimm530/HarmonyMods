# Build Economics Harmony mod
# Copies only Economics.dll into server HarmonyMods/

Write-Host "Building Economics..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Economics.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Economics.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Economics.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Economics.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Economics.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/Economics.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/Economics/ (or Custom economics data directory)" -ForegroundColor Yellow
    Write-Host "Load with: harmony.load Economics  (after Permissions)" -ForegroundColor Yellow
    Write-Host "Runtime SQLite: place System.Data.SQLite.dll in RustDedicated_Data/Managed" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
