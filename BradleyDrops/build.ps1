# Build BradleyDrops Harmony mod
# Copies only BradleyDrops.dll into server HarmonyMods/

Write-Host "Building BradleyDrops Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "BradleyDrops.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\BradleyDrops.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\BradleyDrops.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "BradleyDrops.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! BradleyDrops.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/BradleyDrops.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/BradleyDrops/" -ForegroundColor Yellow
    Write-Host "Lang:   HarmonyLanguage/BradleyDrops.json" -ForegroundColor Gray
    Write-Host "CUI:    cui.endtest BRADLEYDROPS" -ForegroundColor Gray
    Write-Host "Unload oxide/plugins/BradleyDrops.cs so both copies do not run." -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
