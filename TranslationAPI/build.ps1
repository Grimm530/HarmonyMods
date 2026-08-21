# Build script for TranslationAPI Harmony Mod
# Output: <workspace>\HarmonyMods\TranslationAPI.dll
# Requires: TranslationAPI Oxide bridge plugin (oxide/plugins/TranslationAPI.cs) for ChatTranslator/Rustcord

Write-Host "Building TranslationAPI Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TranslationAPI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TranslationAPI.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TranslationAPI.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TranslationAPI.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TranslationAPI.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/TranslationAPI.json (created on first load if missing)" -ForegroundColor Yellow
    Write-Host "Load: harmony.load TranslationAPI (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
