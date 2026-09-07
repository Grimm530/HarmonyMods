# Build script for HideAdminActions Harmony Mod
# Output: D:\!RustServer\HarmonyMods\HideAdminActions.dll

Write-Host "Building HideAdminActions..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "HideAdminActions.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
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
    Write-Host "The mod will load automatically on next server start (harmony.load HideAdminActions)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
