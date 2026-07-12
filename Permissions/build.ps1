# Build Permissions Harmony Mod
Write-Host "Building Permissions..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "Permissions.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = $env:RUST_SERVER_ROOT
    if (-not $root) {
        $candidate = Join-Path $PSScriptRoot "..\..\.."
        $root = [System.IO.Path]::GetFullPath($candidate)
    }
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\Permissions.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\Permissions.dll"
    }
    if (-not (Test-Path $dllPath)) {
        Write-Host "Build output not found" -ForegroundColor Red
        exit 1
    }
    $destPath = Join-Path $harmonyModsPath "Permissions.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! Permissions.dll -> $destPath" -ForegroundColor Green
    Write-Host "Data: HarmonyData/Permissions/  Commands: perm.grant / perm.usergroup / perm.show" -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
