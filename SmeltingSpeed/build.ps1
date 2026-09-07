# Build script for SmeltingSpeed Harmony Mod
# Output: D:\!RustServer\HarmonyMods\SmeltingSpeed.dll

Write-Host "Building SmeltingSpeed..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SmeltingSpeed\SmeltingSpeed.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "SmeltingSpeed\bin\Release\net48\SmeltingSpeed.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "SmeltingSpeed\bin\Release\SmeltingSpeed.dll"
    }
    $destPath = Join-Path $harmonyModsPath "SmeltingSpeed.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! SmeltingSpeed.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Mod halves smelt time for all furnace types. Load: harmony.load SmeltingSpeed" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
