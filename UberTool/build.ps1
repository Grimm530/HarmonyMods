# Build UberTool Harmony Mod
# Output: <server root>\HarmonyMods\UberTool.dll (DLL only)

Write-Host "Building UberTool Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "UberTool.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\UberTool.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\UberTool.dll"
    }
    $destPath = Join-Path $harmonyModsPath "UberTool.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! UberTool.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/UberTool.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load UberTool (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
