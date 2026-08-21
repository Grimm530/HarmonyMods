# Build RustRewards Harmony Mod
# Copies only RustRewards.dll to server root HarmonyMods/

Write-Host "Building RustRewards..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RustRewards.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RustRewards.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\RustRewards.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RustRewards.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! RustRewards.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load RustRewards" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RustRewards.json  Data: HarmonyData/RustRewards/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
