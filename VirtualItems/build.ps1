# Build VirtualItems Harmony Mod
# Output: <server root>\HarmonyMods\VirtualItems.dll (DLL only)

Write-Host "Building VirtualItems Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "VirtualItems.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\VirtualItems.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\VirtualItems.dll"
    }
    $destPath = Join-Path $harmonyModsPath "VirtualItems.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! VirtualItems.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/VirtualItems.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load VirtualItems (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
