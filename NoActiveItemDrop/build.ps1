# Build script for NoActiveItemDrop Harmony Mod
# Output: <server root>\HarmonyMods\NoActiveItemDrop.dll

Write-Host "Building NoActiveItemDrop..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "NoActiveItemDrop.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\NoActiveItemDrop.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\NoActiveItemDrop.dll"
    }
    $destPath = Join-Path $harmonyModsPath "NoActiveItemDrop.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! NoActiveItemDrop.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load NoActiveItemDrop" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
