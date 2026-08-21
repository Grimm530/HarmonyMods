# Build Spawns Harmony Mod
# Output: <server root>\HarmonyMods\Spawns.dll (DLL only)

Write-Host "Building Spawns Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Spawns.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\Spawns.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Spawns.dll"
    }
    Copy-Item -Path $dllPath -Destination (Join-Path $harmonyModsPath "Spawns.dll") -Force
    Write-Host "`nBuild successful! Spawns.dll copied to $harmonyModsPath" -ForegroundColor Green
    Write-Host "Data: HarmonyData/Spawns/" -ForegroundColor Gray
    Write-Host "API:  AppDomain Spawns_ApiType" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
