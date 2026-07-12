# Build script for RaidableBasesBuyableUI Harmony Mod
# Output: server-root HarmonyMods/RaidableBasesBuyableUI.dll

Write-Host "Building RaidableBasesBuyableUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RaidableBasesUI\RaidableBasesUI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "RaidableBasesUI\bin\Release\RaidableBasesBuyableUI.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "RaidableBasesUI\bin\Release\net48\RaidableBasesBuyableUI.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RaidableBasesBuyableUI.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    # Remove old thin companion DLL name if present (this mod replaces it)
    $legacy = Join-Path $harmonyModsPath "RaidableBasesUI.dll"
    if (Test-Path $legacy) {
        Remove-Item $legacy -Force -ErrorAction SilentlyContinue
        Write-Host "Removed legacy RaidableBasesUI.dll (replaced by RaidableBasesBuyableUI.dll)" -ForegroundColor Yellow
    }

    Write-Host "`nBuild successful! RaidableBasesBuyableUI.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load RaidableBasesBuyableUI" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RaidableBasesBuyableUI.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/RaidableBasesBuyableUI/" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
