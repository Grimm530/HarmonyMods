# Build script for CommandHistory Harmony Mod
# Output: <server root>\HarmonyMods\CommandHistory.dll

Write-Host "Building CommandHistory..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "CommandHistory.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\HarmonyMods"))
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\CommandHistory.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\CommandHistory.dll"
    }
    $destPath = Join-Path $harmonyModsPath "CommandHistory.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! CommandHistory.dll copied to $destPath" -ForegroundColor Green
    Write-Host "The mod will load automatically on next server start (harmony.load CommandHistory)." -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
