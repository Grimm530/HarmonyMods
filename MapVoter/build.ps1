# Build script for MapVoter Harmony Mod
# Output: <workspace>\HarmonyMods\MapVoter.dll

Write-Host "Building MapVoter..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "MapVoter.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $harmonyModsPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\MapVoter.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\MapVoter.dll"
    }
    $destPath = Join-Path $harmonyModsPath "MapVoter.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! MapVoter.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Config: HarmonyConfig/MapVoter.json (in server root)" -ForegroundColor Yellow
    Write-Host "Command: Type 'mvote' in chat to open map voting UI" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
