# Build script for RaidRustPlus Harmony Mod
# Output: D:\!RustServer\HarmonyMods\RaidRustPlus.dll

Write-Host "Building RaidRustPlus..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RaidRustPlus\RaidRustPlus.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "RaidRustPlus\bin\Release\net48\RaidRustPlus.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "RaidRustPlus\bin\Release\RaidRustPlus.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RaidRustPlus.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RaidRustPlus.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load RaidRustPlus" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RaidRustPlus.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
