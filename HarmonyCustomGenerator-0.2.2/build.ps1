# Build script for HarmonyCustomGenerator (CustomGenerator) Harmony Mod
# Requires: .NET SDK or MSBuild, and .csproj references pointing to your RustDedicated_Data\Managed folder.

Write-Host "Building CustomGenerator..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CustomGenerator\CustomGenerator.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Host "Project not found: $projectPath" -ForegroundColor Red
    exit 1
}

# Old-style .csproj (net48) – dotnet build works; use Release
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful! Copying DLL..." -ForegroundColor Green

    $dllPath = Join-Path $PSScriptRoot "CustomGenerator\bin\Release\CustomGenerator.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Host "DLL not found at expected path: $dllPath" -ForegroundColor Red
        exit 1
    }

    # Deploy to workspace HarmonyMods
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath | Out-Null
        Write-Host "Created $harmonyModsPath" -ForegroundColor Yellow
    }

    $destPath = Join-Path $harmonyModsPath "CustomGenerator.dll"
    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "CustomGenerator.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load on next server start. Config: HarmonyConfig\CustomGenerator.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    Write-Host "If references fail, ensure CustomGenerator.csproj HintPaths point to your RustDedicated_Data\Managed folder." -ForegroundColor Yellow
    exit 1
}
