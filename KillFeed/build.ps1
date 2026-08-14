# Build KillFeed Harmony Mod
# Output: <server root>\HarmonyMods\KillFeed.dll (DLL only)

Write-Host "Building KillFeed Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "KillFeed.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\KillFeed.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\KillFeed.dll"
    }
    $destPath = Join-Path $harmonyModsPath "KillFeed.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! KillFeed.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/KillFeed.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load KillFeed (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
