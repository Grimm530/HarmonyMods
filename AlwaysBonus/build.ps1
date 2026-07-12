# Build script for AlwaysBonus Harmony Mod
# Output: D:\!RustServer\HarmonyMods\AlwaysBonus.dll

Write-Host "Building AlwaysBonus..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "AlwaysBonus.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\AlwaysBonus.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\AlwaysBonus.dll"
    }
    $destPath = Join-Path $harmonyModsPath "AlwaysBonus.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! AlwaysBonus.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load AlwaysBonus" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/AlwaysBonus.json (created on first load)" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
