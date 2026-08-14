# Build AirbourneSpawn Harmony Mod
# Output: <server root>\HarmonyMods\AirbourneSpawn.dll (DLL only)

Write-Host "Building AirbourneSpawn Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AirbourneSpawn.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AirbourneSpawn.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AirbourneSpawn.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AirbourneSpawn.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AirbourneSpawn.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> Kits (optional) -> AirbourneSpawn" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/AirbourneSpawn.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/AirbourneSpawn.json (optional overrides)" -ForegroundColor Gray
    Write-Host "Unload oxide plugin AirbourneSpawn if it is still loaded." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
