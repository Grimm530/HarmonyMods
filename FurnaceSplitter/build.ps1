# Build script for FurnaceSplitter Harmony Mod
# Output: D:\!RustServer\HarmonyMods\FurnaceSplitter.dll

Write-Host "Building FurnaceSplitter..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "FurnaceSplitter\FurnaceSplitter.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "FurnaceSplitter\bin\Release\net48\FurnaceSplitter.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "FurnaceSplitter\bin\Release\FurnaceSplitter.dll"
    }
    $destPath = Join-Path $harmonyModsPath "FurnaceSplitter.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! FurnaceSplitter.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Standalone mod: split + auto fuel. Config: HarmonyConfig/FurnaceSplitter.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
