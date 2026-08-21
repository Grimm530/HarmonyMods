# Build InfiniteVendingStock Harmony Mod
# Output: <server root>\HarmonyMods\InfiniteVendingStock.dll (DLL only)

Write-Host "Building InfiniteVendingStock Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InfiniteVendingStock.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InfiniteVendingStock.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\InfiniteVendingStock.dll"
    }
    $destPath = Join-Path $harmonyModsPath "InfiniteVendingStock.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! InfiniteVendingStock.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/InfiniteVendingStock.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
