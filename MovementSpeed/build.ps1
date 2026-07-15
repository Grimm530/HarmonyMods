# Build MovementSpeed Harmony Mod
# Output: <server root>\HarmonyMods\MovementSpeed.dll
# Config: HarmonyConfig/MovementSpeed.json

Write-Host "Building MovementSpeed Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "MovementSpeed.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\"))
    }

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\MovementSpeed.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\MovementSpeed.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "MovementSpeed.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host ""
    Write-Host "Build successful!  MovementSpeed.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config:  HarmonyConfig/MovementSpeed.json" -ForegroundColor Yellow
    Write-Host "Load:    auto on startup (alphabetical). Ready callbacks bind Permissions; no forced order." -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "Build FAILED!" -ForegroundColor Red
    exit 1
}
