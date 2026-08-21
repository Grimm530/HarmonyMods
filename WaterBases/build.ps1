# Build WaterBases Harmony Mod
# Output: <server root>\HarmonyMods\WaterBases.dll (DLL only)

Write-Host "Building WaterBases Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "WaterBases.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))

    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\WaterBases.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\WaterBases.dll"
    }
    $destPath = Join-Path $harmonyModsPath "WaterBases.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force

    Write-Host "`nBuild successful! WaterBases.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/WaterBases.json" -ForegroundColor Gray
    Write-Host "Lang:   HarmonyLanguage/WaterBases.json" -ForegroundColor Gray
    Write-Host "Load order: 0Permissions -> WaterBases" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
