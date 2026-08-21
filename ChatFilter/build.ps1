# Build script for ChatFilter Harmony Mod
# Output: <server root>\HarmonyMods\ChatFilter.dll

Write-Host "Building ChatFilter..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ChatFilter\ChatFilter.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
    $harmonyModsPath = Join-Path $root "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "ChatFilter\bin\Release\net48\ChatFilter.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "ChatFilter\bin\Release\ChatFilter.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ChatFilter.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ChatFilter.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load with: harmony.load ChatFilter" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ChatFilter.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
