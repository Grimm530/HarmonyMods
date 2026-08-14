# Build Minimap Harmony Mod
# Copies only Minimap.dll to server root HarmonyMods/ and map arrows to HarmonyImages/Minimap/

Write-Host "Building Minimap..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Minimap.csproj"
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
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Minimap.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Minimap.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Minimap.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    $imagesPath = Join-Path $root "HarmonyImages\Minimap"
    if (-not (Test-Path $imagesPath)) {
        New-Item -ItemType Directory -Path $imagesPath -Force | Out-Null
    }
    $resPath = Join-Path $PSScriptRoot "Resources"
    if (Test-Path $resPath) {
        Copy-Item -Path (Join-Path $resPath "maparrow.*.png") -Destination $imagesPath -Force
    }

    Write-Host "Build successful! Minimap.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Minimap" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Minimap.json  Data: HarmonyData/Minimap/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
