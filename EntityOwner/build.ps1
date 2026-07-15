# Build EntityOwner Harmony Mod
# Output: <server root>\HarmonyMods\EntityOwner.dll (DLL only)

Write-Host "Building EntityOwner Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "EntityOwner.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\EntityOwner.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\EntityOwner.dll"
    }
    $destPath = Join-Path $harmonyModsPath "EntityOwner.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! EntityOwner.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> EntityOwner" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/EntityOwner.json" -ForegroundColor Gray
    Write-Host "Lang: HarmonyLanguage/EntityOwner.json (optional overrides)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
