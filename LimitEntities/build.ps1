# Build LimitEntities Harmony Mod
# Output: <server root>\HarmonyMods\LimitEntities.dll (DLL only)

Write-Host "Building LimitEntities Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "LimitEntities.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\LimitEntities.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\LimitEntities.dll"
    }
    $destPath = Join-Path $harmonyModsPath "LimitEntities.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! LimitEntities.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: Permissions -> LimitEntities" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/LimitEntities.json" -ForegroundColor Gray
    Write-Host "Data: HarmonyData/LimitEntities.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
