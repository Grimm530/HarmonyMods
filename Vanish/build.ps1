# Build script for Vanish Harmony Mod
# Output: <server root>\HarmonyMods\Vanish.dll
# Config: HarmonyConfig/Vanish.json

Write-Host "Building Vanish Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Vanish.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    # csproj OutputPath is bin\Release\ (AppendTargetFrameworkToOutputPath=false)
    $dllPath = Join-Path $PSScriptRoot "bin\Release\Vanish.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Vanish.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found under bin\Release\Vanish.dll" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Vanish.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    # Ensure vanish icon directory exists (icon path: HarmonyImages/Vanish/vanish.png or rust2x.png relative to server root)
    $iconDir = Join-Path (Split-Path $harmonyModsPath -Parent) "HarmonyImages\Vanish"
    if (-not (Test-Path $iconDir)) {
        New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
    }
    $iconPath = Join-Path $iconDir "vanish.png"
    $iconPathAlt = Join-Path $iconDir "rust2x.png"
    if (-not (Test-Path $iconPath) -and -not (Test-Path $iconPathAlt)) {
        Write-Host "Icon: Place vanish.png (or rust2x.png) in $iconDir for the vanish UI icon to show" -ForegroundColor Yellow
    }
    Write-Host "`nBuild successful! Vanish.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/Vanish.json (created on first load if missing)" -ForegroundColor Yellow
    Write-Host "Load: harmony.load Vanish (or automatic at startup)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
