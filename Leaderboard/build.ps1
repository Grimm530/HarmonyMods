# Build script for Leaderboard Harmony Mod
# Copies Leaderboard.dll to server root HarmonyMods/

Write-Host "Building Leaderboard..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Leaderboard.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Leaderboard.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Leaderboard.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Leaderboard.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Leaderboard.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Leaderboard" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
