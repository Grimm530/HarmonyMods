# Build Scale Harmony Mod
# Output: <server root>\HarmonyMods\Scale.dll (DLL only)

Write-Host "Building Scale Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Scale.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Scale.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Scale.dll"
    }
    $destPath = Join-Path $harmonyModsPath "Scale.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Scale.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> Scale" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/Scale.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
