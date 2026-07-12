# Build script for InventoryShortcuts Harmony Mod
# Output: <server root>\HarmonyMods\InventoryShortcuts.dll

Write-Host "Building InventoryShortcuts..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InventoryShortcuts.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}

$serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
}

# csproj: AppendTargetFrameworkToOutputPath=false → bin\Release\InventoryShortcuts.dll
$dllPath = Join-Path $PSScriptRoot "bin\Release\InventoryShortcuts.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InventoryShortcuts.dll"
}
if (-not (Test-Path $dllPath)) {
    Write-Host "`nBuild succeeded but no InventoryShortcuts.dll found under bin\Release\" -ForegroundColor Red
    exit 1
}

$destPath = Join-Path $harmonyModsPath "InventoryShortcuts.dll"
$srcItem = Get-Item -LiteralPath $dllPath

try {
    Copy-Item -LiteralPath $dllPath -Destination $destPath -Force -ErrorAction Stop
} catch {
    Write-Host "`nBuild succeeded but could not copy to $destPath" -ForegroundColor Red
    Write-Host "The Rust server may have the DLL loaded. Run: harmony.unload InventoryShortcuts" -ForegroundColor Yellow
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

$destItem = Get-Item -LiteralPath $destPath
if ((Get-FileHash -LiteralPath $dllPath).Hash -ne (Get-FileHash -LiteralPath $destPath).Hash) {
    Write-Host "`nBuild succeeded but deployed file does not match build output." -ForegroundColor Red
    Write-Host "  Source: $($srcItem.FullName) ($($srcItem.Length) bytes, $($srcItem.LastWriteTime))" -ForegroundColor Red
    Write-Host "  Dest:   $($destItem.FullName) ($($destItem.Length) bytes, $($destItem.LastWriteTime))" -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild successful!" -ForegroundColor Green
Write-Host "  Source: $($srcItem.FullName) ($($srcItem.Length) bytes)" -ForegroundColor Gray
Write-Host "  Deploy: $destPath ($($destItem.Length) bytes)" -ForegroundColor Green
Write-Host "Load with: harmony.unload InventoryShortcuts  then  harmony.load InventoryShortcuts" -ForegroundColor Yellow
Write-Host "Config: HarmonyConfig/InventoryShortcuts.json" -ForegroundColor Yellow
