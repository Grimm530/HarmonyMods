# Build RemoverTool Harmony mod
# Copies only RemoverTool.dll into server HarmonyMods/

Write-Host "Building RemoverTool..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RemoverTool.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RemoverTool.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\RemoverTool.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "RemoverTool.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RemoverTool.dll -> $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> (optional Economics, RustRewards) -> RemoverTool" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RemoverTool.json" -ForegroundColor Gray
    Write-Host "Data:   HarmonyData/RemoverTool/" -ForegroundColor Gray
    Write-Host "Lang:   HarmonyLanguage/RemoverTool.json (optional override)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
