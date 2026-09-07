# Build script for TruePVE Harmony Mod
# Output: D:\!RustServer\HarmonyMods\TruePVE.dll

Write-Host "Building TruePVE..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "TruePVE.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\TruePVE.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\TruePVE.dll"
    }
    $destPath = Join-Path $harmonyModsPath "TruePVE.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! TruePVE.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load TruePVE" -ForegroundColor Yellow
    Write-Host "Config: TruePVE.json in HarmonyConfig/ or Config/ or server root (default created in HarmonyConfig/ on first load)" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
