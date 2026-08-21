# Build AdminMenu Harmony Mod
# Copies only AdminMenu.dll to server root HarmonyMods/

Write-Host "Building AdminMenu..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AdminMenu.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AdminMenu.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AdminMenu.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "AdminMenu.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! AdminMenu.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load AdminMenu" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/AdminMenu.json  Data: HarmonyData/AdminMenu/" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
