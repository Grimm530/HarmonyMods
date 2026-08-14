# Build script for BetterChat Harmony Mod
# Output: <workspace>\HarmonyMods\BetterChat.dll

Write-Host "Building BetterChat..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "BetterChat.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $workspaceRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\BetterChat.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\BetterChat.dll"
    }
    $destPath = Join-Path $harmonyModsPath "BetterChat.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! BetterChat.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load BetterChat" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/BetterChat.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
