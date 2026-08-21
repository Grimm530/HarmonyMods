# Build script for GatherManager Harmony Mod
# Output: <workspace>\HarmonyMods\GatherManager.dll

Write-Host "Building GatherManager..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Rust.HarmonyMods\Facepunch.Harmony.GatherManager\Facepunch.Harmony.GatherManager.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "Rust.HarmonyMods\Facepunch.Harmony.GatherManager\bin\Release\net48\GatherManager.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "Rust.HarmonyMods\Facepunch.Harmony.GatherManager\bin\Release\GatherManager.dll"
    }
    $destPath = Join-Path $harmonyModsPath "GatherManager.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! GatherManager.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load GatherManager)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
