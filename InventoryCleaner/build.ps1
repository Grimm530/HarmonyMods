# Build InventoryCleaner Harmony Mod
# Output: <server root>\HarmonyMods\InventoryCleaner.dll (DLL only)

Write-Host "Building InventoryCleaner Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InventoryCleaner.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InventoryCleaner.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\InventoryCleaner.dll"
    }
    $destPath = Join-Path $harmonyModsPath "InventoryCleaner.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! InventoryCleaner.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: Permissions -> InventoryCleaner" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/InventoryCleaner.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/InventoryCleaner.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
