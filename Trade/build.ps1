# Build Trade Harmony Mod
# Output: <server root>\HarmonyMods\Trade.dll (DLL only)

Write-Host "Building Trade Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Trade.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Trade.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Trade.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Trade.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Trade.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> Trade" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Trade.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
