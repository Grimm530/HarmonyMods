# Build ServerQoL Harmony Mod
# Output: <server root>\HarmonyMods\ServerQoL.dll (DLL only)

Write-Host "Building ServerQoL Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ServerQoL.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ServerQoL.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ServerQoL.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ServerQoL.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ServerQoL.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> ServerQoL" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ServerQoL.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
