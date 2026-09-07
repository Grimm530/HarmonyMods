# Build script for Prodigy Harmony Mod
# Output: D:\!RustServer\HarmonyMods\Prodigy.dll

Write-Host "Building Prodigy..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Prodigy.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Prodigy.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Prodigy.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Prodigy.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Prodigy.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Prodigy" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
