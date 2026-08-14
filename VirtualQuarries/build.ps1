# Build VirtualQuarries Harmony mod
# Copies only VirtualQuarries.dll into server HarmonyMods/

Write-Host "Building VirtualQuarries Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "VirtualQuarries.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\VirtualQuarries.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\VirtualQuarries.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }

    $destPath = Join-Path $harmonyModsPath "VirtualQuarries.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! VirtualQuarries.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/VirtualQuarries.json" -ForegroundColor Yellow
    Write-Host "Data:   HarmonyData/VirtualQuarries/" -ForegroundColor Yellow
    Write-Host "Lang:   HarmonyLanguage/VirtualQuarries.json" -ForegroundColor Gray
    Write-Host "CUI:    cui.endtest VIRTUALQUARRIES" -ForegroundColor Gray
    Write-Host "Unload oxide/plugins/VirtualQuarries.cs so both copies do not run." -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
