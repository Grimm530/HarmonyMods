# Build InventoryViewer Harmony Mod
# Output: <server root>\HarmonyMods\InventoryViewer.dll (DLL only)

Write-Host "Building InventoryViewer Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InventoryViewer.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InventoryViewer.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\InventoryViewer.dll"
    }
    $destPath = Join-Path $harmonyModsPath "InventoryViewer.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! InventoryViewer.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> InventoryViewer" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/InventoryViewer.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
