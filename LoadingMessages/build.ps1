# Build script for LoadingMessages Harmony Mod
# Output: <server root>/HarmonyMods/LoadingMessages.dll

Write-Host "Building LoadingMessages..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "LoadingMessages.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\LoadingMessages.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\LoadingMessages.dll"
    }
    $destPath = Join-Path $harmonyModsPath "LoadingMessages.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! LoadingMessages.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/LoadingMessages.json" -ForegroundColor Yellow
    Write-Host "Load with: harmony.load LoadingMessages" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
