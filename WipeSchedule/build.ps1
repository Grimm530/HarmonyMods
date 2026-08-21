# Build WipeSchedule Harmony Mod
# Copies only WipeSchedule.dll to server root HarmonyMods/

Write-Host "Building WipeSchedule..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "WipeSchedule.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\WipeSchedule.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\WipeSchedule.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "WipeSchedule.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! WipeSchedule.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load WipeSchedule" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/WipeSchedule.json  Data: HarmonyData/WipeSchedule/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
