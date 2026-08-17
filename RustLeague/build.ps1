# Build RustLeague Harmony Mod
# Output: <server root>\HarmonyMods\RustLeague.dll (DLL only)

Write-Host "Building RustLeague Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RustLeague.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\RustLeague.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\RustLeague.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RustLeague.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RustLeague.dll copied to $destPath" -ForegroundColor Green

    $svr1 = "c:\svr1\HarmonyMods"
    if (Test-Path $svr1) {
        Copy-Item -Path $dllPath -Destination (Join-Path $svr1 "RustLeague.dll") -Force
        Write-Host "Also copied to $svr1\RustLeague.dll" -ForegroundColor Green
    }
    Write-Host "Load order: 0Permissions -> RustLeague" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/RustLeague.json" -ForegroundColor Gray
    Write-Host "Unload oxide plugin RustLeague if it is still loaded." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
