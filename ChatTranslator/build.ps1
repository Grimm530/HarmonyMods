# Build script for ChatTranslator Harmony Mod
# Output: D:\!RustServer\HarmonyMods\ChatTranslator.dll
# Requires: TranslationAPI Harmony mod (must be loaded first)

Write-Host "Building ChatTranslator Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ChatTranslator.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ChatTranslator.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ChatTranslator.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ChatTranslator.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ChatTranslator.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/ChatTranslator.json (created on first load if missing)" -ForegroundColor Yellow
    Write-Host "Requires: TranslationAPI.dll (load first)" -ForegroundColor Gray
    Write-Host "Load: harmony.load ChatTranslator (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
