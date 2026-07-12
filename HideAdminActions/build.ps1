# Build script for HideAdminActions Harmony Mod
# Output: <server root>\HarmonyMods\HideAdminActions.dll

Write-Host "Building HideAdminActions..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "HideAdminActions.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\HideAdminActions.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\HideAdminActions.dll"
    }
    $destPath = Join-Path $harmonyModsPath "HideAdminActions.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! HideAdminActions.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Restart the server (or harmony.load HideAdminActions) for changes to take effect." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
