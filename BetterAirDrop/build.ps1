# Build script for BetterAirDrop Harmony Mod
# Output: D:\!RustServer\HarmonyMods\BetterAirDrop.dll

Write-Host "Building BetterAirDrop..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "BetterAirDrop.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\BetterAirDrop.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\BetterAirDrop.dll"
    }
    $destPath = Join-Path $harmonyModsPath "BetterAirDrop.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! BetterAirDrop.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load BetterAirDrop)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
