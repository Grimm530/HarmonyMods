# Build Economics Harmony mod
# Copies Economics.dll into server HarmonyMods/
# Uses Facepunch.Sqlite from RustDedicated_Data/Managed (no System.Data.SQLite)

Write-Host "Building Economics..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Economics.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Economics.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Economics.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Economics.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! Economics.dll -> $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/Economics.json" -ForegroundColor Yellow
    Write-Host "Sqlite: Facepunch.Sqlite (Managed) + BalanceSqlitePath in config" -ForegroundColor Yellow
    Write-Host "Load:   harmony.load Economics  (after 0Permissions)" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
