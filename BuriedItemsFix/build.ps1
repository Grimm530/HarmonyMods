# Build script for BuriedItemsFix Harmony Mod
Write-Host "Building BuriedItemsFix..." -ForegroundColor Cyan

Push-Location $PSScriptRoot
try {
    dotnet build BuriedItemsFix.csproj -c Release
} finally {
    Pop-Location
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful! Copying DLL to HarmonyMods..." -ForegroundColor Green

    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\BuriedItemsFix.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\BuriedItemsFix.dll"
    }
    $destPath = Join-Path $harmonyModsPath "BuriedItemsFix.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuriedItemsFix.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
