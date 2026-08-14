# Build IndustrialRecycler Harmony Mod
# Output: <server root>\HarmonyMods\IndustrialRecycler.dll (DLL only)

Write-Host "Building IndustrialRecycler Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "IndustrialRecycler.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\IndustrialRecycler.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\IndustrialRecycler.dll"
    }
    $destPath = Join-Path $harmonyModsPath "IndustrialRecycler.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! IndustrialRecycler.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/IndustrialRecycler.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load IndustrialRecycler (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
