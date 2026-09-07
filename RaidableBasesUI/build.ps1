# Build script for RaidableBasesUI Harmony Mod
# Output: server-root HarmonyMods/RaidableBasesUI.dll

Write-Host "Building RaidableBasesUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RaidableBasesUI\RaidableBasesUI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        $harmonyModsPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "HarmonyMods"
    }
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "RaidableBasesUI\bin\Release\net48\RaidableBasesUI.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "RaidableBasesUI\bin\Release\RaidableBasesUI.dll"
    }
  $destPath = Join-Path $harmonyModsPath "RaidableBasesBuyableUI.dll"
  Copy-Item -Path $dllPath -Destination $destPath -Force
  # Legacy filename (some bridges still reference RaidableBasesUI.dll)
  $legacyDest = Join-Path $harmonyModsPath "RaidableBasesUI.dll"
  Copy-Item -Path $dllPath -Destination $legacyDest -Force
  Write-Host "`nBuild successful! RaidableBasesBuyableUI.dll copied to $destPath" -ForegroundColor Green
  Write-Host "Load with: harmony.load RaidableBasesBuyableUI" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
