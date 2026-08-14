# Build PlaytimeTracker Harmony Mod
# Output: <server root>\HarmonyMods\PlaytimeTracker.dll (DLL only)

Write-Host "Building PlaytimeTracker Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PlaytimeTracker.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\"))
    }
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\PlaytimeTracker.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\PlaytimeTracker.dll"
    }
    Copy-Item -Path $dllPath -Destination (Join-Path $harmonyModsPath "PlaytimeTracker.dll") -Force
    Write-Host "`nBuild successful! PlaytimeTracker.dll copied to $harmonyModsPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/PlaytimeTracker.json" -ForegroundColor Gray
    Write-Host "Data:   HarmonyData/PlaytimeTracker/" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
