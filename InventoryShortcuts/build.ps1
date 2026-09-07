# Build script for InventoryShortcuts Harmony Mod
# Output: D:\!RustServer\HarmonyMods\InventoryShortcuts.dll

Write-Host "Building InventoryShortcuts..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InventoryShortcuts.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InventoryShortcuts.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\InventoryShortcuts.dll"
    }
    $destPath = Join-Path $harmonyModsPath "InventoryShortcuts.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! InventoryShortcuts.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load InventoryShortcuts" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/InventoryShortcuts.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
