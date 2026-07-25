# Build script for Convoy Harmony Mod
# Output: <server root>\HarmonyMods\Convoy.dll
# Config: HarmonyConfig/Convoy.json

Write-Host "Building Convoy Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Convoy\Convoy.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "Convoy\bin\Release\net48\Convoy.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "Convoy\bin\Release\Convoy.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under Convoy\bin\Release\Convoy.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Convoy.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Convoy.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/Convoy.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load Convoy (requires 0GrimmNPC; or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
