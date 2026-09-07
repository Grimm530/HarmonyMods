# Build script for AlphaLoot Harmony Mod
# Output: D:\!RustServer\HarmonyMods\AlphaLoot.dll

Write-Host "Building AlphaLoot Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AlphaLoot.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AlphaLoot.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AlphaLoot.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AlphaLoot.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AlphaLoot.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/AlphaLoot.json | Data: HarmonyData/AlphaLoot/LootProfiles/" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
