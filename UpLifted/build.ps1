# Build UpLifted Harmony mod
# Copies only UpLifted.dll into server HarmonyMods/

Write-Host "Building UpLifted Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "UpLifted.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\UpLifted.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\UpLifted.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "UpLifted.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! UpLifted.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/UpLifted.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/UpLifted/" -ForegroundColor Yellow
    Write-Host "Lang:   HarmonyLanguage/UpLifted.json" -ForegroundColor Gray
    Write-Host "CUI:    cui.endtest UPLIFTED" -ForegroundColor Gray
    Write-Host "Unload oxide/plugins/UpLifted.cs so both copies do not run." -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
