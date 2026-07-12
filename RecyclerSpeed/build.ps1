# Build script for RecyclerSpeed Harmony Mod
# Output: <server root>\HarmonyMods\RecyclerSpeed.dll

Write-Host "Building RecyclerSpeed..." -ForegroundColor Cyan

$projectPath = Join-Path $PSScriptRoot "RecyclerSpeed\RecyclerSpeed.csproj"
dotnet build $projectPath -c Release

if ($LASTEXITCODE -eq 0) {
    $serverRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
    $harmonyModsPath = Join-Path $serverRoot "HarmonyMods"
    if (-not (Test-Path $harmonyModsPath)) {
        New-Item -ItemType Directory -Path $harmonyModsPath -Force | Out-Null
    }

    $dllPath = Join-Path $PSScriptRoot "RecyclerSpeed\bin\Release\net48\RecyclerSpeed.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Join-Path $PSScriptRoot "RecyclerSpeed\bin\Release\RecyclerSpeed.dll"
    }
    $destPath = Join-Path $harmonyModsPath "RecyclerSpeed.dll"

    Copy-Item -Path $dllPath -Destination $destPath -Force
    Write-Host "`nBuild successful! RecyclerSpeed.dll copied to $destPath" -ForegroundColor Green
    Write-Host "Default: 2x speed (half time). Config: HarmonyConfig/RecyclerSpeed.json" -ForegroundColor Yellow
} else {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}
