# Build script for CopyPaste Harmony Mod
# Copies output to server root HarmonyMods/

Write-Host "Building CopyPaste..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CopyPaste.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }
    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CopyPaste.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\CopyPaste.dll"
    }
    $destPath = Join-Path $harmonyModsPath "CopyPaste.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "Build successful! CopyPaste.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load CopyPaste" -ForegroundColor Yellow
} else {
    Write-Host "Build failed! Check errors above." -ForegroundColor Red
    exit 1
}
