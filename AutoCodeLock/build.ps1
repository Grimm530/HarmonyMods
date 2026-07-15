# Build AutoCodeLock Harmony Mod
# Output: <server root>\HarmonyMods\AutoCodeLock.dll (DLL only)

Write-Host "Building AutoCodeLock Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AutoCodeLock.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AutoCodeLock.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AutoCodeLock.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AutoCodeLock.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AutoCodeLock.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> AutoCodeLock" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/AutoCodeLock.json" -ForegroundColor Gray
    Write-Host "Data: HarmonyData/AutoCodeLock/user_data.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/AutoCodeLock.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
