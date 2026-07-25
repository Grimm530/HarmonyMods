# Build script for Leaderboard Harmony Mod
# Output: <workspace>\HarmonyMods\Leaderboard.dll

Write-Host "Building Leaderboard..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Leaderboard.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    # Workspace root is three levels up from .cursor/HarmonyMods/Leaderboard
    $workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $workspaceRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Leaderboard.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Leaderboard.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Leaderboard.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Leaderboard.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load Leaderboard" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
