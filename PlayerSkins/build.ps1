# Build PlayerSkins Harmony Mod
# Output: <server root>\HarmonyMods\PlayerSkins.dll (DLL only)

Write-Host "Building PlayerSkins Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PlayerSkins.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\PlayerSkins.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\PlayerSkins.dll"
    }
    $destPath = Join-Path $harmonyModsPath "PlayerSkins.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! PlayerSkins.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> Economics (optional) -> PlayerSkins" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/PlayerSkins.json" -ForegroundColor Gray
    Write-Host "Data: HarmonyData/PlayerSkins/" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/PlayerSkins.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
