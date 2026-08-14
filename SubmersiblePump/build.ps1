# Build SubmersiblePump Harmony Mod
# Output: <server root>\HarmonyMods\SubmersiblePump.dll (DLL only)

Write-Host "Building SubmersiblePump Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "SubmersiblePump.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\SubmersiblePump.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\SubmersiblePump.dll"
    }
    $destPath = Join-Path $harmonyModsPath "SubmersiblePump.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! SubmersiblePump.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> SubmersiblePump" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/SubmersiblePump.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
