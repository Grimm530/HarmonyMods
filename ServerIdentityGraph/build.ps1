# Build ServerIdentityGraph Harmony mod
# Copies only ServerIdentityGraph.dll into server HarmonyMods/

Write-Host "Building ServerIdentityGraph..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ServerIdentityGraph.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\ServerIdentityGraph.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ServerIdentityGraph.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "ServerIdentityGraph.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ServerIdentityGraph.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/ServerIdentityGraph.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/ServerIdentityGraph/players/{steamId}.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
