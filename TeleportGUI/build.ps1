# Build script for TeleportGUI Harmony Mod
# Output: D:\!RustServer\HarmonyMods\TeleportGUI.dll

Write-Host "Building TeleportGUI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TeleportGUI\TeleportGUI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "TeleportGUI\bin\Release\net48\TeleportGUI.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "TeleportGUI\bin\Release\TeleportGUI.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TeleportGUI.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TeleportGUI.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load TeleportGUI" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/TeleportGUI.json (or oxide/config/TeleportGUI.json)" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
