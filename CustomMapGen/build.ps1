# Build script for CustomMapGen Harmony Mod
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $sdks = & dotnet --list-sdks 2>$null
    if (-not $sdks) {
        Write-Host "No .NET SDK found. Run: .\install-dependencies.ps1" -ForegroundColor Yellow
        Write-Host "Or install from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Building CustomMapGen..." -ForegroundColor Cyan
    $rustManaged = $env:RUST_MANAGED
    if ($rustManaged) {
        Write-Host "Using RUST_MANAGED: $rustManaged" -ForegroundColor DarkGray
        dotnet build CustomMapGen.csproj -c Release -p:RustManaged="$rustManaged"
    } else {
        dotnet build CustomMapGen.csproj -c Release
    }

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nBuild successful! Copying DLL to HarmonyMods..." -ForegroundColor Green

        # Deploy target: env wins; else workspace HarmonyMods (repo root = three levels up from this script).
        # On a machine with no D: drive, do not default to D:\—set HARMONY_MODS_DEPLOY for e.g. D:\!RustServer\HarmonyMods.
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
        $defaultHarmonyMods = Join-Path $repoRoot "HarmonyMods"
        $harmonyModsPath = if ($env:HARMONY_MODS_DEPLOY) { $env:HARMONY_MODS_DEPLOY } else { $defaultHarmonyMods }
        if (-not (Test-Path $harmonyModsPath)) {
            New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
        }

        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CustomMapGen.dll"
        if (-not (Test-Path $dllPath)) {
            $dllPath = Join-Path $PSScriptRoot "bin\Release\CustomMapGen.dll"
        }
        $destPath = Join-Path $harmonyModsPath "CustomMapGen.dll"

        Copy-Item -Path $dllPath -Destination $destPath -Force
        Write-Host "`nCustomMapGen.dll copied to $destPath" -ForegroundColor Green
        Write-Host "The mod will load automatically on next server start." -ForegroundColor Yellow
    } else {
        Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}
