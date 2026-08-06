# Build RustEditStandalone Harmony mod and deploy DLL to workspace HarmonyMods/
# Source: .cursor/HarmonyMods/RustEditStandalone/
# Runtime: HarmonyMods/RustEditStandalone.dll
# Config: HarmonyConfig/RustEdit.json

Write-Host "Building RustEditStandalone..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RustEditStandalone.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\RustEditStandalone.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RustEditStandalone.dll"
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

$destPath = Join-Path $harmonyModsPath "RustEditStandalone.dll"
try {
    Copy-Item -Path $dllPath -Destination $destPath -Force -ErrorAction Stop
}
catch {
    Write-Host "`nBuild succeeded but DEPLOY FAILED (destination likely locked by a running server):" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Unload the mod first (harmony.unload RustEditStandalone), then re-run this script." -ForegroundColor Yellow
    exit 1
}

$srcInfo = Get-Item $dllPath
$dstInfo = Get-Item $destPath
if ($srcInfo.Length -ne $dstInfo.Length) {
    Write-Host "`nDeploy verification FAILED: deployed DLL size ($($dstInfo.Length)) != built DLL size ($($srcInfo.Length))." -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild successful! RustEditStandalone.dll copied to $destPath" -ForegroundColor Green
Write-Host "Load with: harmony.load RustEditStandalone" -ForegroundColor Yellow
Write-Host "Config: HarmonyConfig/RustEdit.json" -ForegroundColor Yellow
