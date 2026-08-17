# Build HarmonyMetrics Harmony Mod
# Output: <server root>\HarmonyMods\HarmonyMetrics.dll (DLL only)

Write-Host "Building HarmonyMetrics Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "HarmonyMetrics.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\HarmonyMetrics.dll"
    $destPath = Join-Path $harmonyModsPath "HarmonyMetrics.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! HarmonyMetrics.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Do not load this DLL while the dedicated server is running." -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/HarmonyMetrics.json" -ForegroundColor Gray
    Write-Host "Commands: harmonymetrics.reloadcfg  harmonymetrics.status" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
