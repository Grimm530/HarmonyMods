# Build LootQoL Harmony Mod
# Output: <server root>\HarmonyMods\LootQoL.dll (DLL only)

Write-Host "Building LootQoL Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "LootQoL.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\LootQoL.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\LootQoL.dll"
    }
    $destPath = Join-Path $harmonyModsPath "LootQoL.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! LootQoL.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> LootQoL" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/LootQoL.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/LootQoL.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
