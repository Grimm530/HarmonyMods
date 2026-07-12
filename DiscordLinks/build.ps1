# Build script for DiscordLinks Harmony Mod
# Output: D:\!RustServer\HarmonyMods\DiscordLinks.dll
# Config: HarmonyConfig/DiscordLinks.json | Data: HarmonyData/DiscordLinks/links.json

Write-Host "Building DiscordLinks..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "DiscordLinks.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\DiscordLinks.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\DiscordLinks.dll"
    }
    $destPath = Join-Path $harmonyModsPath "DiscordLinks.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! DiscordLinks.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load DiscordLinks (or automatic at startup)" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/DiscordLinks.json | Data: HarmonyData/DiscordLinks/links.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
