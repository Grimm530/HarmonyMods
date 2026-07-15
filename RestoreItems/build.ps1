# Build RestoreItems Harmony Mod
# Copies only RestoreItems.dll to server root HarmonyMods/

Write-Host "Building RestoreItems..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RestoreItems.csproj"
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
    $dllPath = Join-Path $PSScriptRoot "bin\Release\RestoreItems.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RestoreItems.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! RestoreItems.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load RestoreItems" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RestoreItems.json" -ForegroundColor Yellow
    Write-Host "Data: HarmonyData/RestoreItems/" -ForegroundColor Yellow
    Write-Host "Grant perm: perm grant user restoreitems.use" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
