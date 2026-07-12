# Build script for FullRangeAutoturrets Harmony Mod
# Output: D:\!RustServer\HarmonyMods\FullRangeAutoturrets.dll

Write-Host "Building FullRangeAutoturrets..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "FullRangeAutoturrets.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = "D:\!RustServer\HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\FullRangeAutoturrets.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\FullRangeAutoturrets.dll"
    }
    $destPath = Join-Path $harmonyModsPath "FullRangeAutoturrets.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! FullRangeAutoturrets.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load FullRangeAutoturrets)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
