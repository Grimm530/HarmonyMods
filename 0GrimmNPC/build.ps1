# Build 0GrimmNPC (NpcSpawn Harmony port)
# DLL prefix 0* so HarmonyLoader loads before ArmoredTrain / Convoy / ZombieHorde.
# Copies only 0GrimmNPC.dll into server HarmonyMods/

Write-Host "Building 0GrimmNPC..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "0GrimmNPC.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\0GrimmNPC.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\0GrimmNPC.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    # Remove legacy GrimmNPC.dll so only 0GrimmNPC loads
    $legacyPath = Join-Path $harmonyModsPath "GrimmNPC.dll"
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force
        Write-Host "Removed legacy GrimmNPC.dll" -ForegroundColor Yellow
    }

    $destPath = Join-Path $harmonyModsPath "0GrimmNPC.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! 0GrimmNPC.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/GrimmNPC.json  Data: HarmonyConfig/NpcSpawn/" -ForegroundColor Yellow
    Write-Host "Load: harmony.load 0GrimmNPC" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
