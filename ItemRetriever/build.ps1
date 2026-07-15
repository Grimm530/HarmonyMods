# Build ItemRetriever Harmony Mod
# Copies only ItemRetriever.dll to server root HarmonyMods/

Write-Host "Building ItemRetriever..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ItemRetriever.csproj"
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
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ItemRetriever.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ItemRetriever.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "ItemRetriever.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! ItemRetriever.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load ItemRetriever" -ForegroundColor Yellow
    Write-Host "Library plugin - no HarmonyConfig / HarmonyData files." -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
