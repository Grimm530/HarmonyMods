# Build Kits Harmony Mod
# Copies only Kits.dll to server root HarmonyMods/

Write-Host "Building Kits..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Kits.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $candidate = Join-Path $PSScriptRoot "..\..\.."
        $root = [System.IO.Path]::GetFullPath($candidate)
    }
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Kits.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Kits.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Kits.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! Kits.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Kits" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Kits.json  Data: HarmonyData/Kits/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
