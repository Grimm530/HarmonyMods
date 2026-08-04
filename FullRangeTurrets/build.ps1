# Build script for FullRangeAutoturrets Harmony Mod
# Output: <workspace>\HarmonyMods\FullRangeAutoturrets.dll

Write-Host "Building FullRangeAutoturrets..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "FullRangeAutoturrets.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    # Workspace root is three levels up from .cursor/HarmonyMods/FullRangeTurrets
    $workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $workspaceRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\FullRangeAutoturrets.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\FullRangeAutoturrets.dll"
    }
    $destPath = Join-Path $harmonyModsPath "FullRangeAutoturrets.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! FullRangeAutoturrets.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load FullRangeAutoturrets" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
