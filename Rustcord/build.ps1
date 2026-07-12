# Build script for Rustcord Harmony Mod
# Output: D:\!RustServer\HarmonyMods\Rustcord.dll
# No Oxide. Uses Discord webhooks for Game->Discord. Compatible with ticket-support-system-discord relay.

Write-Host "Building Rustcord Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Rustcord.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Rustcord.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Rustcord.dll"
    }
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $destPath = Join-Path $harmonyModsPath "Rustcord.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Rustcord.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/Rustcord.json or oxide/config/Rustcord.json" -ForegroundColor Yellow
    Write-Host "Add Webhooks to config: Webhooks dict or ChannelConfig.WebhookUrl per channel" -ForegroundColor Yellow
    Write-Host "Load: harmony.load Rustcord (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
