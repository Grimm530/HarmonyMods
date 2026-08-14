# Build ChestStacks Harmony Mod
# Output: <server root>\HarmonyMods\ChestStacks.dll (DLL only)

Write-Host "Building ChestStacks Harmony mod..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "ChestStacks.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "bin\Release\net48\ChestStacks.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "bin\Release\ChestStacks.dll"
    }
    $destPath = Join-Path $harmonyModsPath "ChestStacks.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! ChestStacks.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Load order: 0Permissions -> ChestStacks" -ForegroundColor Yellow
    Write-Host "Config: HarmonyConfig/ChestStacks.json" -ForegroundColor Gray
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
