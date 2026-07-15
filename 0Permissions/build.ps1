# Build 0Permissions Harmony Mod (loads first alphabetically)
Write-Host "Building 0Permissions..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "0Permissions.csproj"
$serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$harmonyModsPath = Join-Path $serverRoot "HarmonyMods"

dotnet build $projectPath -c Release
if ($LASTEXITCODE -eq 0) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\0Permissions.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\0Permissions.dll"
    }

    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
    }

    # Remove legacy Permissions.dll so only 0Permissions loads
    $legacyPath = Join-Path $harmonyModsPath "Permissions.dll"
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force
        Write-Host "Removed legacy Permissions.dll" -ForegroundColor Yellow
    }

    $destPath = Join-Path $harmonyModsPath "0Permissions.dll"
    Copy-Item -LiteralPath $dllPath -Destination $destPath -Force
    Write-Host "Build successful! 0Permissions.dll -> $destPath" -ForegroundColor Green
    Write-Host "Data: HarmonyData/Permissions/  Commands: perm.grant / perm.usergroup / perm.show" -ForegroundColor Yellow
    Write-Host "Reload: harmony.reload 0Permissions" -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
