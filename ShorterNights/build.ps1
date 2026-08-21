# Build script for ShorterNights Harmony Mod
# Output: <workspace>\HarmonyMods\ShorterNights.dll

Write-Host "Building ShorterNights..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ShorterNights\ShorterNights.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "ShorterNights\bin\Release\net48\ShorterNights.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "ShorterNights\bin\Release\ShorterNights.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ShorterNights.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ShorterNights.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Mod: shorter nights + game time under hotbar. Load: harmony.load ShorterNights" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
