# Build SortButton Harmony Mod
# Output: <server root>\HarmonyMods\SortButton.dll (DLL only)

Write-Host "Building SortButton Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SortButton.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\SortButton.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\SortButton.dll"
    }
    $destPath = Join-Path $harmonyModsPath "SortButton.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! SortButton.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> SortButton" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/SortButton.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
