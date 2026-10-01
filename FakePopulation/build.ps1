# Build script for FakePopulation Harmony Mod
# Output: <workspace>\HarmonyMods\FakePopulation.dll

Write-Host "Building FakePopulation..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "FakePopulation\FakePopulation.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "FakePopulation\bin\Release\net48\FakePopulation.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "FakePopulation\bin\Release\FakePopulation.dll"
    }
    $destPath = Join-Path $harmonyModsPath "FakePopulation.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! FakePopulation.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/FakePopulation.json (BonusPlayers: extra players to show)" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
