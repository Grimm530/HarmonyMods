# Build script for ChatIcons Harmony Mod
# Output: D:\!RustServer\HarmonyMods\ChatIcons.dll
# Replaces Oxide CustomIcon plugin - set Steam Avatar User ID in HarmonyConfig/ChatIcons.json

Write-Host "Building ChatIcons..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ChatIcons.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ChatIcons.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ChatIcons.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ChatIcons.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ChatIcons.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load ChatIcons (or automatic at startup)" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ChatIcons.json - set Steam Avatar User ID for non-user chat icons" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
