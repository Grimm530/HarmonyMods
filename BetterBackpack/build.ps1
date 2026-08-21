# Build script for BetterBackpack Harmony Mod
# Output: <server root>\HarmonyMods\BetterBackpack.dll

Write-Host "Building BetterBackpack..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "BetterBackpack\BetterBackpack.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "BetterBackpack\bin\Release\net48\BetterBackpack.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "BetterBackpack\bin\Release\BetterBackpack.dll"
    }
    $destPath = Join-Path $harmonyModsPath "BetterBackpack.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! BetterBackpack.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load BetterBackpack" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
