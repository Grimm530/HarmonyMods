# Build DynamicCupShare Harmony Mod
# Output: <server root>\HarmonyMods\DynamicCupShare.dll (DLL only)

Write-Host "Building DynamicCupShare Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "DynamicCupShare.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\DynamicCupShare.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\DynamicCupShare.dll"
    }
    $destPath = Join-Path $harmonyModsPath "DynamicCupShare.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! DynamicCupShare.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> DynamicCupShare" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/DynamicCupShare.json" -ForegroundColor Gray
    Write-Host "Data: HarmonyData/DynamicCupShare/" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/DynamicCupShare.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
