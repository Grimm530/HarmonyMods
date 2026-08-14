# Build CustomMagazine Harmony Mod
# Output: <server root>\HarmonyMods\CustomMagazine.dll (DLL only)

Write-Host "Building CustomMagazine Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CustomMagazine.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CustomMagazine.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\CustomMagazine.dll"
    }
    $destPath = Join-Path $harmonyModsPath "CustomMagazine.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! CustomMagazine.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/CustomMagazine.json" -ForegroundColor Gray
    Write-Host "Console: givemagazine <skinid> <steamid>" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
