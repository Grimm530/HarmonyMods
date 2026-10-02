# Build script for Radar Harmony Mod
# Output: <server root>\HarmonyMods\Radar.dll

Write-Host "Building Radar..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Radar.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Radar.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Radar.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\Radar.dll" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "Radar.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host ""
    Write-Host "Build successful! Radar.dll -> $destPath" -ForegroundColor Green
    Write-Host "Reload with: harmony.load Radar" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
