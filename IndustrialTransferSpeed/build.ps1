# Build script for IndustrialTransferSpeed Harmony Mod
# Output: <workspace>\HarmonyMods\IndustrialTransferSpeed.dll

Write-Host "Building IndustrialTransferSpeed..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "IndustrialTransferSpeed\IndustrialTransferSpeed.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    # Workspace root is three levels up from .cursor/HarmonyMods/IndustrialTransferSpeed
    $workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $workspaceRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "IndustrialTransferSpeed\bin\Release\net48\IndustrialTransferSpeed.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "IndustrialTransferSpeed\bin\Release\IndustrialTransferSpeed.dll"
    }
    $destPath = Join-Path $harmonyModsPath "IndustrialTransferSpeed.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! IndustrialTransferSpeed.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/IndustrialTransferSpeed.json (created on first run)" -ForegroundColor Yellow
    Write-Host "Load with: harmony.load IndustrialTransferSpeed" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
