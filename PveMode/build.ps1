# Build script for 0PveMode Harmony Mod
# Output: <server root>\HarmonyMods\0PveMode.dll
# Config/data/lang keys stay PveMode* (same as 0Permissions / Permissions*)

Write-Host "Building 0PveMode Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PveMode\PveMode.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "PveMode\bin\Release\net48\0PveMode.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "PveMode\bin\Release\0PveMode.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under PveMode\bin\Release\0PveMode.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "0PveMode.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force

    # Remove legacy DLL so HarmonyLoader does not load both / wrong order.
    $legacy = Join-Path $harmonyModsPath "PveMode.dll"
    if (Test-Path $legacy) {
        Remove-Item $legacy -Force
        Write-Host "Removed legacy HarmonyMods\PveMode.dll" -ForegroundColor Yellow
    }

    Write-Host "`nBuild successful! 0PveMode.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/PveMode.json" -ForegroundColor Yellow
    Write-Host "Data: HarmonyData/PveMode/players.json" -ForegroundColor Yellow
    Write-Host "Lang: HarmonyLanguage/PveMode.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load 0PveMode (0 prefix sorts before Convoy/ArmoredTrain)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
