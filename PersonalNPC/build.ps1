# Build PersonalNPC Harmony Mod
# Copies only PersonalNPC.dll to server root HarmonyMods/

Write-Host "Building PersonalNPC..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PersonalNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\PersonalNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\PersonalNPC.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "PersonalNPC.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! PersonalNPC.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load PersonalNPC" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/PersonalNPC/PersonalNPC.json  Data: HarmonyData/PersonalNPC/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
