# Build script for Backpacks Harmony Mod
# Output: D:\!RustServer\HarmonyMods\Backpacks.dll

Write-Host "Building Backpacks..." -ForegroundColor Cyan

# Full 3.17 Harmony port (BackpacksHarmony) — NOT the legacy minimal mod under Backpacks/Backpacks/
$projectPath = Join-Path $PSScriptRoot "Backpacks.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\Backpacks.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Backpacks.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Backpacks.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Backpacks.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Backpacks" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Backpacks.json (or oxide/config/Backpacks.json)" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
