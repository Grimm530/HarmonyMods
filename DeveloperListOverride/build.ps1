# Build script for DeveloperListOverride Harmony Mod
# Output: D:\!RustServer\HarmonyMods\DeveloperListOverride.dll

Write-Host "Building DeveloperListOverride..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "DeveloperListOverride\DeveloperListOverride.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "DeveloperListOverride\bin\Release\net48\DeveloperListOverride.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "DeveloperListOverride\bin\Release\DeveloperListOverride.dll"
    }
    $destPath = Join-Path $harmonyModsPath "DeveloperListOverride.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! DeveloperListOverride.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load DeveloperListOverride" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/DeveloperListOverride.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
