# Build Backpacks Harmony Mod
# Copies only Backpacks.dll to server root HarmonyMods/

Write-Host "Building Backpacks..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Backpacks.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Backpacks.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Backpacks.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Backpacks.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! Backpacks.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Backpacks" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Backpacks.json" -ForegroundColor Yellow
    Write-Host "Data: custom path from config (default C:\!DataPersistence\oxide\data\Backpacks)" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
