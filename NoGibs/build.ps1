# Build script for NoGibs Harmony Mod
Write-Host "Building NoGibs..." -ForegroundColor Cyan

Push-Location $PSScriptRoot
try {
    dotnet build NoGibs.csproj -c Release
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

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\NoGibs.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\NoGibs.dll"
    }
    $destPath = Join-Path $harmonyModsPath "NoGibs.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nNoGibs.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load NoGibs" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
