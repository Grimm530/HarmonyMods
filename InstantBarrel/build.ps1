# Build script for InstantBarrel Harmony Mod
# Output: D:\!RustServer\HarmonyMods\InstantBarrel.dll

Write-Host "Building InstantBarrel..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "InstantBarrel.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\InstantBarrel.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\InstantBarrel.dll"
    }
    $destPath = Join-Path $harmonyModsPath "InstantBarrel.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! InstantBarrel.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load InstantBarrel" -ForegroundColor Yellow
    Write-Host "Harmony-only required; config-only. See README.md." -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
