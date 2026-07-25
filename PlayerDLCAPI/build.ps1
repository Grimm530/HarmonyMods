# Build PlayerDLCAPI Harmony mod and deploy only the entry DLL.

Write-Host "Building PlayerDLCAPI..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "PlayerDLCAPI.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed. Check errors above." -ForegroundColor Red
    exit 1
}

$root = $env:RUST_SERVER_ROOT
if (-not $root) {
    $root = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot "..\..\..")
    )
}

$harmonyModsPath = Join-Path $root "HarmonyMods"
if (-not (Test-Path $harmonyModsPath)) {
    New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\PlayerDLCAPI.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "Build output not found: $dllPath" -ForegroundColor Red
    exit 1
}

$destination = Join-Path $harmonyModsPath "PlayerDLCAPI.dll"
Copy-Item -Path $dllPath -Destination $destination -Force

Write-Host "Build successful. PlayerDLCAPI.dll copied to $destination" -ForegroundColor Green
Write-Host "Load before Shop and PlayerSkins: harmony.load PlayerDLCAPI" -ForegroundColor Yellow
