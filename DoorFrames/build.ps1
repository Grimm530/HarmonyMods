# Build DoorFrames Harmony Mod
# Output: <server root>\HarmonyMods\DoorFrames.dll (DLL only)

Write-Host "Building DoorFrames Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "DoorFrames.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\DoorFrames.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\DoorFrames.dll"
    }
    $destPath = Join-Path $harmonyModsPath "DoorFrames.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! DoorFrames.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> DoorFrames" -ForegroundColor Yellow
    Write-Host "Chat: /df.rotate" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
