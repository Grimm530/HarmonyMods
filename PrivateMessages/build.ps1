# Build PrivateMessages Harmony Mod
# Output: <server root>\HarmonyMods\PrivateMessages.dll (DLL only)

Write-Host "Building PrivateMessages Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PrivateMessages.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\PrivateMessages.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\PrivateMessages.dll"
    }
    $destPath = Join-Path $harmonyModsPath "PrivateMessages.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! PrivateMessages.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> PrivateMessages" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/PrivateMessages.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/PrivateMessages.json" -ForegroundColor Gray
    Write-Host "Chat: /pm /r /reply /msg /tell /send /pmhistory" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
