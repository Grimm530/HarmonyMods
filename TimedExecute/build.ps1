# Build TimedExecute Harmony Mod
# Output: <server root>\HarmonyMods\TimedExecute.dll (DLL only)

Write-Host "Building TimedExecute Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TimedExecute.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TimedExecute.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TimedExecute.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TimedExecute.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TimedExecute.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/TimedExecute.json" -ForegroundColor Gray
    Write-Host "The mod will load automatically on next server start (harmony.load TimedExecute)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
