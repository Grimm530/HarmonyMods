# Build script for CommunityTab Harmony Mod
# Output: C:\!2XRUST\HarmonyMods\CommunityTab.dll
# Strips modded/oxide/carbon browser tags so server lists in Community tab
#
Write-Host "Building CommunityTab Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CommunityTab.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "C:\!2XRUST\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CommunityTab.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\CommunityTab.dll"
    }
    $destPath = Join-Path $harmonyModsPath "CommunityTab.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! CommunityTab.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load CommunityTab" -ForegroundColor Yellow
    Write-Host "Strips compressed ^z/^o/^y (and legacy modded) from GameTags for Community tab" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
