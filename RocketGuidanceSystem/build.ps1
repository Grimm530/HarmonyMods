# Build RocketGuidanceSystem Harmony Mod
# Output: <server root>\HarmonyMods\RocketGuidanceSystem.dll (DLL only)

Write-Host "Building RocketGuidanceSystem Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RocketGuidanceSystem.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\RocketGuidanceSystem.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RocketGuidanceSystem.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RocketGuidanceSystem.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RocketGuidanceSystem.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/RocketGuidanceSystem.json" -ForegroundColor Yellow
    Write-Host "Load: harmony.load RocketGuidanceSystem (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
