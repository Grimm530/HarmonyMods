# Build TeleportGUI Harmony mod and deploy DLL to workspace HarmonyMods/
# Source: .cursor/HarmonyMods/TeleportGUI/
# Runtime: HarmonyMods/TeleportGUI.dll
# Config: HarmonyConfig/TeleportGUI.json
# Data:   HarmonyData/TeleportGUI/

Write-Host "Building TeleportGUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TeleportGUI\TeleportGUI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}

# csproj sets AppendTargetFrameworkToOutputPath=false -> bin\Release\TeleportGUI.dll
# Prefer that; only fall back to net48 if the primary output is missing.
$dllPath = Join-Path $PSScriptRoot "TeleportGUI\bin\Release\TeleportGUI.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "TeleportGUI\bin\Release\net48\TeleportGUI.dll"
}
if (-not (Test-Path $dllPath)) {
    Write-Host "`nBuild succeeded but DLL not found." -ForegroundColor Red
    exit 1
}

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$harmonyModsPath = Join-Path $workspaceRoot "HarmonyMods"
if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
}

$destPath = Join-Path $harmonyModsPath "TeleportGUI.dll"
try {
    Copy-Item -Path $dllPath -Destination $destPath -Force -ErrorAction Stop
}
catch {
    Write-Host "`nBuild succeeded but DEPLOY FAILED (destination likely locked by a running server):" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Unload the mod first (harmony.unload TeleportGUI), then re-run this script." -ForegroundColor Yellow
    exit 1
}

# Verify the deployed DLL actually matches what we just built.
$srcInfo = Get-Item $dllPath
$dstInfo = Get-Item $destPath
if ($srcInfo.Length -ne $dstInfo.Length) {
    Write-Host "`nDeploy verification FAILED: deployed DLL size ($($dstInfo.Length)) != built DLL size ($($srcInfo.Length))." -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild successful! TeleportGUI.dll copied to $destPath" -ForegroundColor Green
Write-Host "Load with: harmony.load TeleportGUI" -ForegroundColor Yellow
Write-Host "Config: HarmonyConfig/TeleportGUI.json" -ForegroundColor Yellow
Write-Host "Data:   HarmonyData/TeleportGUI/ (userdata.json, warpdata.json)" -ForegroundColor Yellow
