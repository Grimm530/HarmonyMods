# Build script for ChatIcons Harmony Mod
# Output: <server root>/HarmonyMods/ChatIcons.dll

Write-Host "Building ChatIcons..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ChatIcons.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
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
    Write-Host "Config: HarmonyConfig/ChatIcons.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
